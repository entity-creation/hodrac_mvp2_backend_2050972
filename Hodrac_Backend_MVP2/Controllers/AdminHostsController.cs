using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Infrastucture.SignalR;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for Hodrac Host: verification queue and host plan/founding status.
///
///   GET  /api/admin/hosts?status=&amp;closed=&amp;page=&amp;pageSize=&amp;search=   verification queue (closed hosts included unless closed=false)
///   GET  /api/admin/hosts/{id}
///   GET  /api/admin/hosts/{id}/documents                 documents + 5-minute signed links
///   GET  /api/admin/hosts/documents/{docId}?exp=&amp;sig=   download (admin AND valid signature)
///   POST /api/admin/hosts/{id}/verify                    from Unverified/Pending/Rejected (no documents needed)
///   POST /api/admin/hosts/{id}/reject                    { reason }  from Unverified/Pending
///   POST /api/admin/hosts/{id}/revoke                    { reason? } Verified → Unverified
///   POST /api/admin/hosts/{id}/founding                  limit Hodrac:FoundingHostLimit, until Hodrac:FoundingDeadline
///   DELETE /api/admin/hosts/{id}/founding                removes founding status (a used free departure stays fee-free)
///   POST /api/admin/hosts/{id}/operator-plan             { reference, amountKobo, periodStart, periodEnd }
///   POST /api/admin/hosts/{id}/trips                     hosted trip on the host's behalf (body = hosted POST /api/trips)
///   POST /api/admin/hosts/{id}/close                     { reason? } close the host account (see Close)
///   POST /api/admin/hosts/{id}/reopen                    undo close (see Reopen)
///   DELETE /api/admin/hosts/{id}                         delete a host that never had hosted trips or payments
///
/// A closed host (HostClosure) can't be verified, rejected, marked founding,
/// given an operator plan or have hosted trips created for it: 409.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/hosts")]
public class AdminHostsController : ControllerBase
{
    // Serialises founding-host marking so two admins can't both take the last slot.
    private const long FoundingLockKey = 0x484F44524143_01;

    // Founding dates are Nigerian calendar dates (WAT = UTC+1).
    private static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    private readonly HodracDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly HostService _hosts;
    private readonly HostDocumentLinkSigner _signer;
    private readonly INotificationService _notifications;
    private readonly IOptions<HodracHostOptions> _options;
    private readonly TripCreationService _tripCreation;
    private readonly IHubContext<TripPostHub> _tripPostHub;
    private readonly ILogger<AdminHostsController> _logger;

    public AdminHostsController(
        HodracDbContext db,
        ICurrentUserService currentUser,
        HostService hosts,
        HostDocumentLinkSigner signer,
        INotificationService notifications,
        IOptions<HodracHostOptions> options,
        TripCreationService tripCreation,
        IHubContext<TripPostHub> tripPostHub,
        ILogger<AdminHostsController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _hosts = hosts;
        _signer = signer;
        _notifications = notifications;
        _options = options;
        _tripCreation = tripCreation;
        _tripPostHub = tripPostHub;
        _logger = logger;
    }

    // ══ Queue ═════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<ActionResult<AdminPagedResult<AdminHostSummaryDto>>> GetHosts(
        [FromQuery] string? status = null,
        [FromQuery] bool? closed = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.HostProfiles.AsNoTracking();

        HostVerificationStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<HostVerificationStatus>(status.Trim(), ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                ModelState.AddModelError(nameof(status),
                    $"Status must be one of: {string.Join(", ", Enum.GetNames<HostVerificationStatus>())}.");
                return ValidationProblem(ModelState);
            }
            statusFilter = parsed;
            query = query.Where(h => h.VerificationStatus == parsed);
        }

        // Closed hosts are listed by default; closed=true only them, closed=false only open ones.
        if (closed is not null)
            query = closed.Value ? query.Where(h => h.ClosedAt != null) : query.Where(h => h.ClosedAt == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(h =>
                EF.Functions.ILike(h.DisplayName, pattern) ||
                EF.Functions.ILike(h.Slug, pattern) ||
                _db.Users.Any(u => u.Id == h.UserId.ToString() && u.Email != null && EF.Functions.ILike(u.Email, pattern)));
        }

        var total = await query.CountAsync(ct);

        // The pending queue is worked oldest-first; everything else newest-first.
        query = statusFilter == HostVerificationStatus.Pending
            ? query.OrderBy(h => h.UpdatedAt)
            : query.OrderByDescending(h => h.CreatedAt);

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(SummaryRow)
            .ToListAsync(ct);

        var emails = await LoadEmailsAsync(rows.Select(r => r.Host.UserId), ct);
        var items = rows.Select(r => ToSummary(r, emails)).ToList();

        return Ok(new AdminPagedResult<AdminHostSummaryDto>(items, page, pageSize, total));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminHostDetailDto>> GetHost(Guid id, CancellationToken ct = default)
    {
        var detail = await LoadDetailAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    // ══ Documents ═════════════════════════════════════════════════════════════

    [HttpGet("{id:guid}/documents")]
    public async Task<ActionResult<List<AdminHostDocumentDto>>> GetDocuments(Guid id, CancellationToken ct = default)
    {
        if (!await _db.HostProfiles.AnyAsync(h => h.Id == id, ct)) return NotFound();

        var docs = await _db.HostDocuments
            .AsNoTracking()
            .Where(d => d.HostProfileId == id)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new { d.Id, d.Kind, d.FileName, d.ContentType, d.SizeBytes, d.UploadedAt })
            .ToListAsync(ct);

        var adminId = _currentUser.UserId;
        var result = docs.Select(d =>
        {
            var link = _signer.Create(d.Id, adminId);
            return new AdminHostDocumentDto(d.Id, d.Kind, d.FileName, d.ContentType, d.SizeBytes, d.UploadedAt, link.Url, link.ExpiresAt);
        }).ToList();

        _logger.LogInformation("Admin {AdminId} issued {Count} document link(s) for host {HostId}", adminId, result.Count, id);
        return Ok(result);
    }

    /// <summary>Streams a private document. Requires the Admin policy AND a valid, unexpired link signed for this admin.</summary>
    [HttpGet("documents/{docId:guid}")]
    public async Task<IActionResult> DownloadDocument(
        Guid docId,
        [FromQuery] long exp,
        [FromQuery] string? sig,
        CancellationToken ct = default)
    {
        var adminId = _currentUser.UserId;
        if (!_signer.IsValid(docId, adminId, exp, sig))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This document link is invalid or has expired." });

        var doc = await _db.HostDocuments
            .AsNoTracking()
            .Where(d => d.Id == docId)
            .Select(d => new { d.HostProfileId, d.FileName, d.ContentType, d.Content })
            .FirstOrDefaultAsync(ct);
        if (doc is null) return NotFound();

        _logger.LogInformation("Admin {AdminId} downloaded document {DocId} of host {HostId}", adminId, docId, doc.HostProfileId);

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // fileDownloadName → Content-Disposition: attachment.
        return File(doc.Content, doc.ContentType, doc.FileName);
    }

    // ══ Decisions ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies a host. Nothing is required to be verified: allowed from Unverified
    /// (never submitted), Pending or Rejected. 409 only if already Verified.
    /// </summary>
    [HttpPost("{id:guid}/verify")]
    public async Task<ActionResult<AdminHostDetailDto>> Verify(Guid id, CancellationToken ct = default)
    {
        var host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (host is null) return NotFound();
        if (host.IsClosed) return HostClosure.AdminConflict();

        if (host.VerificationStatus == HostVerificationStatus.Verified)
            return Conflict(new { error = "This host is already verified." });

        var adminId = _currentUser.UserId;
        host.VerificationStatus = HostVerificationStatus.Verified;
        host.VerifiedAt = DateTime.UtcNow;
        host.VerifiedByUserId = adminId;
        host.RejectionReason = null;
        host.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin {AdminId} verified host {HostId}", adminId, id);

        var emailed = await _hosts.EmailVerificationDecisionAsync(host, verified: true, reason: null, ct);
        await _notifications.NotifyHostVerified(host.UserId, adminId, emailed);

        return Ok(await LoadDetailAsync(id, ct));
    }

    /// <summary>
    /// Rejects a host's verification. Allowed from Unverified or Pending; 409 if
    /// already Rejected, or if Verified (use POST {id}/revoke to remove the badge).
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<AdminHostDetailDto>> Reject(Guid id, RejectHostRequest request, CancellationToken ct = default)
    {
        var host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (host is null) return NotFound();

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            ModelState.AddModelError(nameof(request.Reason), "A reason is required.");
            return ValidationProblem(ModelState);
        }

        if (host.IsClosed) return HostClosure.AdminConflict();
        if (host.VerificationStatus == HostVerificationStatus.Rejected)
            return Conflict(new { error = "This host is already rejected." });
        if (host.VerificationStatus == HostVerificationStatus.Verified)
            return Conflict(new { error = "This host is verified. Use revoke to remove the Verified badge." });

        var adminId = _currentUser.UserId;
        host.VerificationStatus = HostVerificationStatus.Rejected;
        host.RejectionReason = reason;
        // Reviewer and decision time, kept after the 90-day document purge.
        host.VerifiedAt = DateTime.UtcNow;
        host.VerifiedByUserId = adminId;
        host.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin {AdminId} rejected host {HostId}", adminId, id);

        var emailed = await _hosts.EmailVerificationDecisionAsync(host, verified: false, reason, ct);
        await _notifications.NotifyHostRejected(host.UserId, adminId, emailed);

        return Ok(await LoadDetailAsync(id, ct));
    }

    /// <summary>
    /// Removes an already-verified host's badge: Verified → Unverified (409 otherwise).
    /// RejectionReason becomes "Verification removed: {reason}" (or a plain note), and
    /// VerifiedAt/VerifiedByUserId are reused as the last decision's time and reviewer,
    /// so they record who revoked and when.
    ///
    /// Effects: the "Verified host" badge disappears from trip DTOs and share previews
    /// (they read the status). Checkout keeps requiring Verified, so a revoked host
    /// can't take new payments until re-verified; existing payments and trips are
    /// untouched. Documents are kept (the 90-day purge only applies to Verified/Rejected
    /// decisions). The host is notified in-app and by best-effort email.
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    public async Task<ActionResult<AdminHostDetailDto>> Revoke(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RevokeHostVerificationRequest? request,
        CancellationToken ct = default)
    {
        var host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (host is null) return NotFound();

        if (host.VerificationStatus != HostVerificationStatus.Verified)
            return Conflict(new { error = $"Only verified hosts can have their verification removed (this host is {host.VerificationStatus})." });

        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request!.Reason!.Trim();

        var adminId = _currentUser.UserId;
        // Last decision: who revoked and when.
        HostService.RemoveVerification(host, adminId,
            reason is null ? "Verification removed by an admin." : $"Verification removed: {reason}");
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin {AdminId} revoked verification of host {HostId}", adminId, id);

        var emailed = await _hosts.EmailVerificationRevokedAsync(host, reason, ct);
        await _notifications.NotifyHostVerificationRevoked(host.UserId, adminId, emailed);

        return Ok(await LoadDetailAsync(id, ct));
    }

    // ══ Founding / operator plan ══════════════════════════════════════════════

    [HttpPost("{id:guid}/founding")]
    public async Task<ActionResult<AdminHostDetailDto>> MarkFounding(Guid id, CancellationToken ct = default)
    {
        var options = _options.Value;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", new object[] { FoundingLockKey }, ct);

        var host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (host is null) return NotFound();
        if (host.IsClosed) return HostClosure.AdminConflict();

        if (host.IsFoundingHost) return Ok(await LoadDetailAsync(id, ct));

        var todayWat = DateOnly.FromDateTime(DateTime.UtcNow + Wat);
        if (todayWat > options.FoundingDeadline)
            return Conflict(new { error = $"The founding host offer closed on {options.FoundingDeadline:yyyy-MM-dd}." });

        var founding = await _db.HostProfiles.CountAsync(h => h.IsFoundingHost, ct);
        if (founding >= options.FoundingHostLimit)
            return Conflict(new { error = $"All {options.FoundingHostLimit} founding host places are taken." });

        host.IsFoundingHost = true;
        host.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Admin {AdminId} marked host {HostId} founding ({Count}/{Limit})",
            _currentUser.UserId, id, founding + 1, options.FoundingHostLimit);

        return Ok(await LoadDetailAsync(id, ct));
    }

    /// <summary>
    /// Removes founding status (idempotent: 200 if the host isn't founding),
    /// freeing a place in Hodrac:FoundingHostLimit. A free departure already
    /// claimed stays exactly as it is (that trip keeps IsFoundingFreeDeparture
    /// and the host keeps FoundingFreeDepartureTripId, so re-marking them later
    /// never grants a second one); an unclaimed one is lost, because the claim
    /// requires IsFoundingHost. The host is notified in-app and by email.
    /// </summary>
    [HttpDelete("{id:guid}/founding")]
    public async Task<ActionResult<AdminHostDetailDto>> RemoveFounding(Guid id, CancellationToken ct = default)
    {
        HostProfile? host;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // Same advisory lock as marking, then the host row (like trip creation's claim).
            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", new object[] { FoundingLockKey }, ct);
            await LockHostAsync(id, ct);

            host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
            if (host is null) return NotFound();
            if (!host.IsFoundingHost) return Ok(await LoadDetailAsync(id, ct));

            host.IsFoundingHost = false;
            host.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var adminId = _currentUser.UserId;
        _logger.LogInformation("Admin {AdminId} removed founding status of host {HostId} (free departure trip {TripId} kept)",
            adminId, id, host.FoundingFreeDepartureTripId);

        var emailed = await _hosts.EmailFoundingRemovedAsync(host, ct);
        await _notifications.NotifyHostFoundingRemoved(host.UserId, adminId, emailed);

        return Ok(await LoadDetailAsync(id, ct));
    }

    [HttpPost("{id:guid}/operator-plan")]
    public async Task<ActionResult<AdminHostDetailDto>> RecordOperatorPlan(Guid id, RecordOperatorPlanRequest request, CancellationToken ct = default)
    {
        var host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (host is null) return NotFound();
        if (host.IsClosed) return HostClosure.AdminConflict();

        var reference = request.Reference?.Trim() ?? string.Empty;
        if (reference.Length == 0)
            ModelState.AddModelError(nameof(request.Reference), "Reference is required.");
        if (request.AmountKobo <= 0)
            ModelState.AddModelError(nameof(request.AmountKobo), "Amount must be greater than 0.");
        if (request.PeriodStart == default || request.PeriodEnd == default)
            ModelState.AddModelError(nameof(request.PeriodEnd), "Period start and end are required.");
        else if (request.PeriodEnd < request.PeriodStart)
            ModelState.AddModelError(nameof(request.PeriodEnd), "Period end must be on or after the start.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (await _db.OperatorPlanPayments.AnyAsync(p => p.Reference == reference, ct))
            return Conflict(new { error = "A plan payment with this reference is already recorded." });

        var adminId = _currentUser.UserId;
        _db.OperatorPlanPayments.Add(new OperatorPlanPayment
        {
            HostProfileId = host.Id,
            Reference = reference,
            AmountKobo = request.AmountKobo,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            RecordedByUserId = adminId,
        });

        host.Plan = HostPlan.Operator;
        host.OperatorPlanExpiresAt = EndOfDayUtc(request.PeriodEnd);
        host.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "A plan payment with this reference is already recorded." });
        }

        _logger.LogInformation("Admin {AdminId} recorded operator plan {Reference} for host {HostId} until {PeriodEnd}",
            adminId, reference, id, request.PeriodEnd);

        return Ok(await LoadDetailAsync(id, ct));
    }

    // ══ Close / delete ═══════════════════════════════════════════════════════

    /// <summary>
    /// Closes a host account. In one transaction (the host's trips, then the
    /// host row, locked): verification removed (Unverified, as revoke), founding
    /// status removed (a used free departure stays fee-free), every hosted trip
    /// with no seat payment that went through or is in flight and no manual
    /// payment hidden ("Host account closed"), ClosedAt/ClosedReason/ClosedByUserId
    /// set. Trips with paid travellers stay visible so those travellers keep the
    /// trip and its chat; checkout is refused anyway (host not Verified).
    /// After the commit: the Paystack subaccount is deactivated (best-effort;
    /// on failure admins are emailed and subaccountDeactivated is false), and
    /// the host is notified and emailed.
    ///
    /// Already closed: 200, nothing changes except that the subaccount
    /// deactivation is retried (so a Paystack failure can be retried).
    /// POST {id}/reopen undoes it (except verification and founding).
    /// </summary>
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<AdminHostCloseResultDto>> Close(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CloseHostRequest? request,
        CancellationToken ct = default)
    {
        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request!.Reason!.Trim();
        var adminId = _currentUser.UserId;

        HostProfile? host;
        bool alreadyClosed;
        var hiddenTripIds = new List<Guid>();
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // Trips first (the order every trip writer uses), then the host row.
            await LockHostTripsAsync(id, ct);
            await LockHostAsync(id, ct);
            // A hosted trip created while we waited for the host lock.
            await LockHostTripsAsync(id, ct);

            host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
            if (host is null) return NotFound();

            alreadyClosed = host.IsClosed;
            if (!alreadyClosed)
            {
                var now = DateTime.UtcNow;

                HostService.RemoveVerification(host, adminId,
                    reason is null ? "Host account closed by Hodrac." : $"Host account closed: {reason}");
                // Founding status goes; FoundingFreeDepartureTripId (a used free departure) is kept.
                host.IsFoundingHost = false;
                host.ClosedAt = now;
                host.ClosedReason = reason;
                host.ClosedByUserId = adminId;
                host.UpdatedAt = now;

                // Same fields as admin trip moderation (AdminTripsController.HideTrip).
                var toHide = await _db.TripPosts
                    .Where(t => t.HostProfileId == id && !t.IsHidden)
                    .Where(t => !_db.SeatPayments.Any(p => p.TripPostId == t.Id && KeepTripVisibleStatuses.Contains(p.Status)))
                    .Where(t => !_db.ManualPayments.Any(m => m.TripInterest!.TripPostId == t.Id))
                    .ToListAsync(ct);
                foreach (var trip in toHide)
                {
                    trip.IsHidden = true;
                    trip.HiddenAt = now;
                    trip.HiddenByUserId = adminId;
                    trip.HiddenReason = HostClosure.HiddenTripReason;
                    trip.UpdatedAt = now;
                    hiddenTripIds.Add(trip.Id);
                }

                await _db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }

        if (!alreadyClosed)
        {
            _logger.LogInformation("Admin {AdminId} closed host {HostId} ({HiddenCount} trip(s) hidden): {Reason}",
                adminId, id, hiddenTripIds.Count, reason);

            // Mirrors the moderation hide so open feeds drop the cards.
            foreach (var tripId in hiddenTripIds)
                await _tripPostHub.Clients.All.SendAsync("TripPostRemoved", new { tripId }, ct);
        }
        else
        {
            _logger.LogInformation("Admin {AdminId} closed already-closed host {HostId} (subaccount deactivation retried)", adminId, id);
        }

        var deactivated = await _hosts.DeactivateSubaccountAsync(host.Id, host.PaystackSubaccountCode, host.DisplayName, "closed", ct);

        if (!alreadyClosed)
        {
            var emailed = await _hosts.EmailHostClosedAsync(host, reason, ct);
            await _notifications.NotifyHostClosed(host.UserId, adminId, emailed);
        }

        return Ok(new AdminHostCloseResultDto((await LoadDetailAsync(id, ct))!, deactivated, hiddenTripIds));
    }

    /// <summary>
    /// Reopens a closed host account. Not closed: 200, nothing changes (Paystack
    /// isn't called). In one transaction (same lock order as close: the host's
    /// trips, then the host row): ClosedAt/ClosedReason/ClosedByUserId cleared,
    /// RejectionReason (the "Host account closed..." note) cleared, and only the
    /// trips close hid (IsHidden with HiddenReason == HostClosure.HiddenTripReason)
    /// un-hidden, clearing the same fields as admin unhide; trips hidden by
    /// moderation stay hidden. Verification and founding are NOT restored: the
    /// host stays Unverified (an admin verifies again with POST {id}/verify; the
    /// documents/CAC/proof links are still there) and founding is re-granted
    /// only via POST {id}/founding (a used free departure is never granted twice).
    /// After the commit: the Paystack subaccount is reactivated (best-effort,
    /// active = true only; checkout still needs Verified), and the host is
    /// notified and emailed.
    /// </summary>
    [HttpPost("{id:guid}/reopen")]
    public async Task<ActionResult<AdminHostReopenResultDto>> Reopen(Guid id, CancellationToken ct = default)
    {
        var adminId = _currentUser.UserId;

        HostProfile? host;
        var unhiddenTripIds = new List<Guid>();
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            await LockHostTripsAsync(id, ct);
            await LockHostAsync(id, ct);

            host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
            if (host is null) return NotFound();
            if (!host.IsClosed)
                return Ok(new AdminHostReopenResultDto((await LoadDetailAsync(id, ct))!, null, unhiddenTripIds));

            var now = DateTime.UtcNow;
            host.ClosedAt = null;
            host.ClosedReason = null;
            host.ClosedByUserId = null;
            // Stays Unverified; drop the stale "Host account closed..." note.
            host.RejectionReason = null;
            host.UpdatedAt = now;

            // Same fields as admin unhide (AdminTripsController.UnhideTrip).
            var toUnhide = await _db.TripPosts
                .Where(t => t.HostProfileId == id && t.IsHidden && t.HiddenReason == HostClosure.HiddenTripReason)
                .ToListAsync(ct);
            foreach (var trip in toUnhide)
            {
                trip.IsHidden = false;
                trip.HiddenAt = null;
                trip.HiddenReason = null;
                trip.HiddenByUserId = null;
                trip.UpdatedAt = now;
                unhiddenTripIds.Add(trip.Id);
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Admin unhide sends no realtime event (feeds pick the trip up on the
        // next load), so neither does reopen.
        _logger.LogInformation("Admin {AdminId} reopened host {HostId} ({Count} trip(s) un-hidden)",
            adminId, id, unhiddenTripIds.Count);

        var reactivated = await _hosts.ReactivateSubaccountAsync(host.Id, host.PaystackSubaccountCode, host.DisplayName, ct);

        var emailed = await _hosts.EmailHostReopenedAsync(host, ct);
        await _notifications.NotifyHostReopened(host.UserId, adminId, emailed);

        return Ok(new AdminHostReopenResultDto((await LoadDetailAsync(id, ct))!, reactivated, unhiddenTripIds));
    }

    /// <summary>
    /// Deletes a host profile that was never used: no hosted trips (hidden or
    /// not), no seat payments on them and no operator plan payments; otherwise
    /// 409 (close the account instead) with the counts as extensions. Documents
    /// and weekly-report logs cascade. The user account stays, so they can
    /// create a fresh host profile later. The Paystack subaccount, if any, is
    /// deactivated best-effort after the commit. 204.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteHost(Guid id, CancellationToken ct = default)
    {
        HostProfile? host;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // Trip creation takes this lock too, so no hosted trip can appear meanwhile.
            await LockHostAsync(id, ct);

            host = await _db.HostProfiles.FirstOrDefaultAsync(h => h.Id == id, ct);
            if (host is null) return NotFound();

            var hostedTrips = await _db.TripPosts.CountAsync(t => t.HostProfileId == id, ct);
            var seatPayments = await _db.SeatPayments.CountAsync(p => p.TripPost!.HostProfileId == id, ct);
            var planPayments = await _db.OperatorPlanPayments.CountAsync(p => p.HostProfileId == id, ct);
            if (hostedTrips > 0 || seatPayments > 0 || planPayments > 0)
                return Conflict(new
                {
                    error = "This host has trips or payments, so it can't be deleted. Close the account instead.",
                    hostedTrips,
                    seatPayments,
                    operatorPlanPayments = planPayments,
                });

            _db.HostProfiles.Remove(host);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation("Admin {AdminId} deleted host {HostId} ({Slug}) of user {UserId}",
            _currentUser.UserId, id, host.Slug, host.UserId);

        await _hosts.DeactivateSubaccountAsync(host.Id, host.PaystackSubaccountCode, host.DisplayName, "deleted", ct);
        return NoContent();
    }

    // A trip with any of these seat payments (it went through, or may still)
    // or with any manual payment keeps its travellers: closing never hides it.
    private static readonly SeatPaymentStatus[] KeepTripVisibleStatuses =
    {
        SeatPaymentStatus.Initialized,
        SeatPaymentStatus.Success,
        SeatPaymentStatus.Refunded,
        SeatPaymentStatus.PartiallyRefunded,
        SeatPaymentStatus.Disputed,
    };

    private Task LockHostAsync(Guid id, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"HostProfiles\" WHERE \"Id\" = {id} FOR UPDATE", ct);

    private Task LockHostTripsAsync(Guid hostId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TripPosts\" WHERE \"HostProfileId\" = {hostId} ORDER BY \"Id\" FOR UPDATE", ct);

    // ══ Trips ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// "We build your page in 24h": creates a hosted trip on the host's behalf,
    /// authored by the host's user, with exactly the rules of a host creating
    /// it via POST /api/trips (validation, private by default, founding free
    /// departure). The itinerary is a template wishlist built with the admin
    /// wishlist APIs and referenced by wishlistId. 409 if the host has no payout account.
    /// </summary>
    [HttpPost("{id:guid}/trips")]
    public async Task<ActionResult<TripPostDto>> CreateTripForHost(Guid id, TripsController.CreateTripRequest request)
    {
        var result = await _tripCreation.CreateForHostAsync(id, request);
        switch (result.Outcome)
        {
            case TripCreateOutcome.Created:
                _logger.LogInformation("Admin {AdminId} created hosted trip {TripId} for host {HostId}",
                    _currentUser.UserId, result.Trip!.Id, id);
                return Created($"/api/trips/{result.Trip.Id}", result.Dto);
            case TripCreateOutcome.NotFound:
                return NotFound(new { error = result.Error });
            case TripCreateOutcome.Conflict:
                return Conflict(new { error = result.Error });
            case TripCreateOutcome.Invalid:
                foreach (var (key, message) in result.FieldErrors!) ModelState.AddModelError(key, message);
                return ValidationProblem(ModelState);
            default:
                return BadRequest(result.Error);
        }
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    /// <summary>Last microsecond of the day in UTC (Postgres timestamps are microsecond precision).</summary>
    public static DateTime EndOfDayUtc(DateOnly day) =>
        day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddTicks(-10);

    private sealed class HostRow
    {
        public HostProfile Host { get; init; } = null!;
        public int DocumentCount { get; init; }
        public DateTime? LastDocumentUploadedAt { get; init; }
    }

    // Projects the profile without document bytes: only count and latest upload time.
    private static readonly System.Linq.Expressions.Expression<Func<HostProfile, HostRow>> SummaryRow = h => new HostRow
    {
        Host = h,
        DocumentCount = h.Documents.Count,
        LastDocumentUploadedAt = h.Documents.Max(d => (DateTime?)d.UploadedAt),
    };

    private static AdminHostSummaryDto ToSummary(HostRow r, Dictionary<Guid, string?> emails)
    {
        var h = r.Host;
        return new AdminHostSummaryDto(
            h.Id,
            h.UserId,
            h.DisplayName,
            h.Slug,
            emails.GetValueOrDefault(h.UserId),
            h.VerificationStatus.ToString(),
            r.DocumentCount,
            r.LastDocumentUploadedAt,
            h.CacNumber,
            h.PastTripProofUrls.ToList(),
            h.PaystackSubaccountCode is not null,
            h.SettlementAccountLast4,
            h.SettlementBankName,
            h.IsFoundingHost,
            h.Plan.ToString(),
            h.OperatorPlanExpiresAt,
            h.ClosedAt,
            h.ClosedReason,
            h.CreatedAt,
            h.UpdatedAt);
    }

    private async Task<AdminHostDetailDto?> LoadDetailAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.HostProfiles
            .AsNoTracking()
            .Where(h => h.Id == id)
            .Select(SummaryRow)
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var h = row.Host;
        var userId = h.UserId.ToString();
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.DisplayName })
            .FirstOrDefaultAsync(ct);

        var payments = await _db.OperatorPlanPayments
            .AsNoTracking()
            .Where(p => p.HostProfileId == id)
            .OrderByDescending(p => p.PeriodEnd)
            .Select(p => new AdminOperatorPlanPaymentDto(p.Id, p.Reference, p.AmountKobo, p.PeriodStart, p.PeriodEnd, p.RecordedByUserId, p.CreatedAt))
            .ToListAsync(ct);

        var summary = ToSummary(row, new Dictionary<Guid, string?> { [h.UserId] = user?.Email });
        return new AdminHostDetailDto(
            summary,
            user?.DisplayName,
            h.Bio,
            h.LogoUrl,
            h.InstagramHandle,
            h.WhatsAppNumber,
            h.VerifiedAt,
            h.VerifiedByUserId,
            h.RejectionReason,
            h.PaystackSubaccountCode,
            h.SettlementAccountName,
            h.FoundingFreeDepartureTripId,
            h.ClosedByUserId,
            payments);
    }

    // ApplicationUser.Id is the Guid's string form.
    private async Task<Dictionary<Guid, string?>> LoadEmailsAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().Select(g => g.ToString()).ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string?>();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, string?>();
        foreach (var u in users)
            if (Guid.TryParse(u.Id, out var gid))
                result[gid] = u.Email;
        return result;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
