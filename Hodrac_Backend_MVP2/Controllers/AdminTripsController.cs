using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Infrastucture.SignalR;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for trip-post moderation.
///
///   GET    /api/admin/trips?page=&amp;pageSize=&amp;search=&amp;status=&amp;hidden=&amp;authorId=&amp;from=&amp;to=
///   GET    /api/admin/trips/{id}
///   GET    /api/admin/trips/{id}/messages?page=&amp;pageSize=
///   POST   /api/admin/trips/{id}/hide     { reason }
///   POST   /api/admin/trips/{id}/unhide
///   POST   /api/admin/trips/{id}/cancel   { reason? }
///   DELETE /api/admin/trips/{id}/messages/{messageId}
///
/// Mutations take the same per-trip row lock (SELECT ... FOR UPDATE) as
/// TripsController so they serialize with author edits and approvals.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin")]
public class AdminTripsController : ControllerBase
{
    private const int CaptionPreviewLength = 140;

    private readonly HodracDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IHubContext<TripPostHub> _tripPostHub;
    private readonly IHubContext<TripThreadHub> _tripThreadHub;
    private readonly ILogger<AdminTripsController> _logger;
    private readonly INotificationService _notifications;

    public AdminTripsController(
        HodracDbContext db,
        ICurrentUserService currentUser,
        IHubContext<TripPostHub> tripPostHub,
        IHubContext<TripThreadHub> tripThreadHub,
        ILogger<AdminTripsController> logger,
        INotificationService notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _tripPostHub = tripPostHub;
        _tripThreadHub = tripThreadHub;
        _logger = logger;
        _notifications = notifications;
    }

    // ══ Read ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// All trips, hidden ones included. <c>from</c>/<c>to</c> filter on CreatedAt
    /// as a [from, to) UTC range. <c>status</c> is Open | Closed | Cancelled.
    /// </summary>
    [HttpGet("trips")]
    public async Task<ActionResult<AdminPagedResult<AdminTripSummaryDto>>> GetTrips(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? hidden = null,
        [FromQuery] Guid? authorId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<TripPost> query = _db.TripPosts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TripPostStatus>(status.Trim(), ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                ModelState.AddModelError(nameof(status), $"Status must be one of: {string.Join(", ", Enum.GetNames<TripPostStatus>())}.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(t => t.Status == parsed);
        }

        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to);
        if (fromUtc is not null && toUtc is not null && fromUtc >= toUtc)
        {
            ModelState.AddModelError(nameof(from), "'from' must be before 'to'.");
            return ValidationProblem(ModelState);
        }

        if (hidden is not null) query = query.Where(t => t.IsHidden == hidden.Value);
        if (authorId is not null) query = query.Where(t => t.AuthorUserId == authorId.Value);
        if (fromUtc is not null) query = query.Where(t => t.CreatedAt >= fromUtc);
        if (toUtc is not null) query = query.Where(t => t.CreatedAt < toUtc);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Caption, pattern)
                || (t.FreeTextDestination != null && EF.Functions.ILike(t.FreeTextDestination, pattern))
                || (t.Destination != null && EF.Functions.ILike(t.Destination.DestinationName, pattern))
                || (t.Wishlist != null && (EF.Functions.ILike(t.Wishlist.WishlistName, pattern)
                    || t.Wishlist.WishlistDestinations.Any(wd => EF.Functions.ILike(wd.Destination.DestinationName, pattern)))));
        }

        var total = await query.CountAsync(ct);

        var pageIds = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var trips = await WithLabelIncludes()
            .Where(t => pageIds.Contains(t.Id))
            .ToListAsync(ct);
        var tripById = trips.ToDictionary(t => t.Id);

        var interestCounts = await LoadInterestCountsAsync(pageIds, ct);
        var messageCounts = await LoadMessageCountsAsync(pageIds, ct);
        var users = await LoadUsersAsync(trips.Select(t => t.AuthorUserId), ct);

        var items = pageIds
            .Where(tripById.ContainsKey)
            .Select(id =>
            {
                var t = tripById[id];
                return new AdminTripSummaryDto(
                    t.Id,
                    Truncate(t.Caption, CaptionPreviewLength),
                    TripPostDto.FromEntity(t).DestinationLabel,
                    ToAuthor(t.AuthorUserId, users),
                    t.Status,
                    t.IsHidden,
                    t.StartDate,
                    t.EndDate,
                    t.MaxGroupSize,
                    interestCounts.GetValueOrDefault(t.Id, EmptyCounts),
                    messageCounts.GetValueOrDefault(t.Id, 0),
                    t.CreatedAt);
            })
            .ToList();

        return Ok(new AdminPagedResult<AdminTripSummaryDto>(items, page, pageSize, total));
    }

    [HttpGet("trips/{id:guid}")]
    public async Task<ActionResult<AdminTripDetailDto>> GetTrip(Guid id, CancellationToken ct = default)
    {
        var detail = await LoadTripDetail(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>The trip's group chat, newest first. Does not touch anyone's LastReadAt.</summary>
    [HttpGet("trips/{id:guid}/messages")]
    public async Task<ActionResult<AdminPagedResult<AdminThreadMessageDto>>> GetMessages(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (!await _db.TripPosts.AnyAsync(t => t.Id == id, ct)) return NotFound();

        var threadId = await _db.TripThreads
            .Where(t => t.TripPostId == id)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (threadId is null)
            return Ok(new AdminPagedResult<AdminThreadMessageDto>(new List<AdminThreadMessageDto>(), page, pageSize, 0));

        var query = _db.ThreadMessages.AsNoTracking().Where(m => m.TripThreadId == threadId);
        var total = await query.CountAsync(ct);

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var users = await LoadUsersAsync(messages.Select(m => m.SenderUserId), ct);

        var items = messages
            .Select(m => new AdminThreadMessageDto(
                m.Id,
                m.SenderUserId,
                users.TryGetValue(m.SenderUserId, out var u) ? u.DisplayName : null,
                m.Body,
                m.CreatedAt))
            .ToList();

        return Ok(new AdminPagedResult<AdminThreadMessageDto>(items, page, pageSize, total));
    }

    // ══ Moderation ════════════════════════════════════════════════════════════

    /// <summary>
    /// Hides the trip from every listing and from GET /api/trips/{id} for
    /// everyone but its author. Re-hiding an already hidden trip just updates
    /// the reason.
    /// </summary>
    [HttpPost("trips/{id:guid}/hide")]
    public async Task<ActionResult<AdminTripDetailDto>> HideTrip(Guid id, HideTripRequest request, CancellationToken ct = default)
    {
        var reason = NullIfBlank(request.Reason);
        if (reason is null)
            ModelState.AddModelError(nameof(request.Reason), "Reason is required.");
        else if (reason.Length > TripPost.MaxHiddenReasonLength)
            ModelState.AddModelError(nameof(request.Reason), $"Reason can't be longer than {TripPost.MaxHiddenReasonLength} characters.");

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        bool wasHidden;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            await LockTripAsync(id, ct);

            var trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return NotFound();

            wasHidden = trip.IsHidden;
            if (!wasHidden)
            {
                trip.IsHidden = true;
                trip.HiddenAt = DateTime.UtcNow;
                trip.HiddenByUserId = AdminUserId;
            }
            trip.HiddenReason = reason;
            trip.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation("Admin {AdminId} hid trip {TripId}: {Reason}", AdminUserId, id, reason);

        // Mirrors the TripPostCreated broadcast so open feeds drop the card.
        if (!wasHidden)
            await _tripPostHub.Clients.All.SendAsync("TripPostRemoved", new { tripId = id }, ct);

        return Ok(await LoadTripDetail(id, ct));
    }

    [HttpPost("trips/{id:guid}/unhide")]
    public async Task<ActionResult<AdminTripDetailDto>> UnhideTrip(Guid id, CancellationToken ct = default)
    {
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            await LockTripAsync(id, ct);

            var trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return NotFound();

            if (trip.IsHidden)
            {
                trip.IsHidden = false;
                trip.HiddenAt = null;
                trip.HiddenReason = null;
                trip.HiddenByUserId = null;
                trip.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation("Admin {AdminId} unhid trip {TripId}", AdminUserId, id);
        return Ok(await LoadTripDetail(id, ct));
    }

    /// <summary>
    /// Sets the trip to Cancelled (terminal). Idempotent: an already cancelled
    /// trip returns 200 with no changes. The reason is logged only — there is
    /// no column for it yet.
    /// </summary>
    [HttpPost("trips/{id:guid}/cancel")]
    public async Task<ActionResult<AdminTripDetailDto>> CancelTrip(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelTripRequest? request,
        CancellationToken ct = default)
    {
        var reason = NullIfBlank(request?.Reason);
        if (reason is not null && reason.Length > TripPost.MaxHiddenReasonLength)
        {
            ModelState.AddModelError(nameof(CancelTripRequest.Reason), $"Reason can't be longer than {TripPost.MaxHiddenReasonLength} characters.");
            return ValidationProblem(ModelState);
        }

        TripPost? trip;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            await LockTripAsync(id, ct);

            trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return NotFound();

            if (trip.Status == TripPostStatus.Cancelled)
            {
                await tx.CommitAsync(ct);
                return Ok(await LoadTripDetail(id, ct));
            }

            // Any status -> Cancelled is always allowed (same rule as TripsController.UpdateTrip).
            trip.Status = TripPostStatus.Cancelled;
            trip.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation("Admin {AdminId} cancelled trip {TripId}: {Reason}", AdminUserId, id, reason ?? "(no reason)");

        await PushTripCancelledAsync(trip, ct);

        return Ok(await LoadTripDetail(id, ct));
    }

    /// <summary>Hard-deletes one message from the trip's thread.</summary>
    [HttpDelete("trips/{id:guid}/messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid id, Guid messageId, CancellationToken ct = default)
    {
        Guid threadId;
        List<string> participantIds;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            await LockTripAsync(id, ct);

            var thread = await _db.TripThreads
                .AsNoTracking()
                .Where(t => t.TripPostId == id)
                .Select(t => new { t.Id, ParticipantIds = t.Participants.Select(p => p.UserId).ToList() })
                .FirstOrDefaultAsync(ct);
            if (thread is null) return NotFound();

            var deleted = await _db.ThreadMessages
                .Where(m => m.Id == messageId && m.TripThreadId == thread.Id)
                .ExecuteDeleteAsync(ct);
            if (deleted == 0) return NotFound();

            await tx.CommitAsync(ct);

            threadId = thread.Id;
            participantIds = thread.ParticipantIds.Select(u => u.ToString()).ToList();
        }

        _logger.LogInformation("Admin {AdminId} deleted message {MessageId} from trip {TripId}", AdminUserId, messageId, id);

        // Same addressing as ThreadController's ThreadMessageReceived.
        await _tripThreadHub.Clients
            .Users(participantIds)
            .SendAsync("ThreadMessageDeleted", new { tripId = id, threadId, messageId }, ct);

        return NoContent();
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    private Guid AdminUserId => _currentUser.UserId;

    private static readonly AdminTripInterestCountsDto EmptyCounts = new(0, 0, 0, 0);

    // Just enough to build TripPostDto.DestinationLabel.
    private IQueryable<TripPost> WithLabelIncludes() => _db.TripPosts
        .AsNoTracking()
        .Include(t => t.Destination!).ThenInclude(d => d.Country)
        .Include(t => t.Wishlist!).ThenInclude(w => w.WishlistDestinations)
            .ThenInclude(wd => wd.Destination);

    private async Task<AdminTripDetailDto?> LoadTripDetail(Guid id, CancellationToken ct)
    {
        var trip = await WithLabelIncludes().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trip is null) return null;

        var interests = await _db.TripInterests
            .AsNoTracking()
            .Where(i => i.TripPostId == id)
            .OrderBy(i => i.RequestedAt)
            .ToListAsync(ct);

        var thread = await _db.TripThreads
            .AsNoTracking()
            .Where(t => t.TripPostId == id)
            .Select(t => new
            {
                t.Id,
                ParticipantCount = t.Participants.Count,
                MessageCount = t.Messages.Count
            })
            .FirstOrDefaultAsync(ct);

        var users = await LoadUsersAsync(
            interests.Select(i => i.RequesterUserId).Append(trip.AuthorUserId), ct);

        var counts = new AdminTripInterestCountsDto(
            interests.Count(i => i.Status == TripInterestStatus.Requested),
            interests.Count(i => i.Status == TripInterestStatus.Approved),
            interests.Count(i => i.Status == TripInterestStatus.Declined),
            interests.Count(i => i.Status == TripInterestStatus.Withdrawn));

        return new AdminTripDetailDto(
            trip.Id,
            trip.Caption,
            TripPostDto.FromEntity(trip).DestinationLabel,
            trip.DestinationId,
            trip.WishlistId,
            trip.FreeTextDestination,
            trip.CoverImageUrl,
            ToAuthor(trip.AuthorUserId, users),
            trip.Status,
            trip.StartDate,
            trip.EndDate,
            trip.IsDateFlexible,
            trip.MaxGroupSize,
            trip.IsHidden,
            trip.HiddenAt,
            trip.HiddenReason,
            trip.HiddenByUserId,
            counts,
            interests.Select(i =>
            {
                users.TryGetValue(i.RequesterUserId, out var u);
                return new AdminTripInterestDto(
                    i.Id, i.RequesterUserId, u?.DisplayName, u?.Email,
                    i.Status, i.Message, i.RequestedAt, i.RespondedAt);
            }).ToList(),
            thread?.Id,
            thread?.ParticipantCount ?? 0,
            thread?.MessageCount ?? 0,
            trip.CreatedAt,
            trip.UpdatedAt);
    }

    private async Task<Dictionary<Guid, AdminTripInterestCountsDto>> LoadInterestCountsAsync(List<Guid> tripIds, CancellationToken ct)
    {
        var rows = await _db.TripInterests
            .AsNoTracking()
            .Where(i => tripIds.Contains(i.TripPostId))
            .GroupBy(i => new { i.TripPostId, i.Status })
            .Select(g => new { g.Key.TripPostId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.TripPostId)
            .ToDictionary(
                g => g.Key,
                g => new AdminTripInterestCountsDto(
                    g.Where(r => r.Status == TripInterestStatus.Requested).Sum(r => r.Count),
                    g.Where(r => r.Status == TripInterestStatus.Approved).Sum(r => r.Count),
                    g.Where(r => r.Status == TripInterestStatus.Declined).Sum(r => r.Count),
                    g.Where(r => r.Status == TripInterestStatus.Withdrawn).Sum(r => r.Count)));
    }

    private Task<Dictionary<Guid, int>> LoadMessageCountsAsync(List<Guid> tripIds, CancellationToken ct)
        => _db.TripThreads
            .AsNoTracking()
            .Where(t => tripIds.Contains(t.TripPostId))
            .Select(t => new { t.TripPostId, Count = t.Messages.Count })
            .ToDictionaryAsync(x => x.TripPostId, x => x.Count, ct);

    private sealed record UserInfo(string? DisplayName, string? Email);

    // ApplicationUser.Id is the Guid's string form; Postgres FKs store the Guid.
    private async Task<Dictionary<Guid, UserInfo>> LoadUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().Select(g => g.ToString()).ToList();
        if (ids.Count == 0) return new Dictionary<Guid, UserInfo>();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, UserInfo>();
        foreach (var u in users)
            if (Guid.TryParse(u.Id, out var gid))
                result[gid] = new UserInfo(u.DisplayName, u.Email);
        return result;
    }

    private static AdminTripAuthorDto ToAuthor(Guid userId, Dictionary<Guid, UserInfo> users)
        => users.TryGetValue(userId, out var u)
            ? new AdminTripAuthorDto(userId, u.DisplayName, u.Email)
            : new AdminTripAuthorDto(userId, null, null);

    // Tells the author and everyone with a live interest that the trip is now
    // Cancelled (in-app notification + live events), and drops it from open feeds.
    private async Task PushTripCancelledAsync(TripPost trip, CancellationToken ct)
    {
        var requesterIds = await _db.TripInterests
            .AsNoTracking()
            .Where(i => i.TripPostId == trip.Id
                && (i.Status == TripInterestStatus.Requested || i.Status == TripInterestStatus.Approved))
            .Select(i => i.RequesterUserId)
            .ToListAsync(ct);

        var groups = requesterIds
            .Append(trip.AuthorUserId)
            .Distinct()
            .Select(u => $"user:{u}")
            .ToList();

        await _notifications.NotifyTripCancelled(
            trip, requesterIds.Append(trip.AuthorUserId), _currentUser.IsAuthenticated ? _currentUser.UserId : null);

        var payload = new { tripId = trip.Id, status = trip.Status.ToString() };
        await _tripPostHub.Clients.Groups(groups).SendAsync("TripPostStatusChanged", payload, ct);
        await _tripPostHub.Clients.All.SendAsync("TripPostRemoved", new { tripId = trip.Id }, ct);
    }

    private Task LockTripAsync(Guid tripId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {tripId} FOR UPDATE", ct);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";

    /// <summary>Npgsql requires UTC for timestamptz; treat unspecified as UTC.</summary>
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
    };

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
