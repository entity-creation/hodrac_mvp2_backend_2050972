using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for Hodrac Host money: payments search and the Paystack webhook audit.
///
///   GET  /api/admin/payments?reference=&amp;tripId=&amp;hostId=&amp;payerEmail=&amp;status=&amp;from=&amp;to=&amp;page=&amp;pageSize=
///   GET  /api/admin/payments/{reference}                  detail + verify response + webhook events
///   GET  /api/admin/payment-events?unprocessed=true&amp;eventType=&amp;reference=&amp;page=&amp;pageSize=
///   GET  /api/admin/payment-events/{id}                   detail + raw payload
///   POST /api/admin/payment-events/{id}/reprocess         re-run an unprocessed event (idempotent)
///
/// Filters: reference and eventType are exact (reference case-sensitive, as
/// issued; eventType case-insensitive), payerEmail is a case-insensitive
/// substring, from/to are inclusive WAT (UTC+1) calendar dates on CreatedAt.
/// Never returns payout account numbers or subaccount codes.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
public class AdminPaymentsController : ControllerBase
{
    private static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    private readonly HodracDbContext _db;
    private readonly PaystackWebhookProcessor _webhooks;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AdminPaymentsController> _logger;

    public AdminPaymentsController(
        HodracDbContext db,
        PaystackWebhookProcessor webhooks,
        ICurrentUserService currentUser,
        ILogger<AdminPaymentsController> logger)
    {
        _db = db;
        _webhooks = webhooks;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ══ Payments ══════════════════════════════════════════════════════════════

    [HttpGet("api/admin/payments")]
    public async Task<ActionResult<AdminPagedResult<AdminPaymentDto>>> GetPayments(
        [FromQuery] string? reference = null,
        [FromQuery] Guid? tripId = null,
        [FromQuery] Guid? hostId = null,
        [FromQuery] string? payerEmail = null,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.SeatPayments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(reference))
        {
            var r = reference.Trim();
            query = query.Where(p => p.Reference == r);
        }
        if (tripId is Guid t) query = query.Where(p => p.TripPostId == t);
        if (hostId is Guid h) query = query.Where(p => p.TripPost!.HostProfileId == h);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<SeatPaymentStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                ModelState.AddModelError(nameof(status),
                    $"Status must be one of: {string.Join(", ", Enum.GetNames<SeatPaymentStatus>())}.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(p => p.Status == parsed);
        }

        if (from is not null && to is not null && to < from)
        {
            ModelState.AddModelError(nameof(to), "'to' must be on or after 'from'.");
            return ValidationProblem(ModelState);
        }
        if (from is DateOnly f)
        {
            var fromUtc = StartOfDayUtc(f);
            query = query.Where(p => p.CreatedAt >= fromUtc);
        }
        if (to is DateOnly tt)
        {
            var toUtc = StartOfDayUtc(tt.AddDays(1));
            query = query.Where(p => p.CreatedAt < toUtc);
        }

        if (!string.IsNullOrWhiteSpace(payerEmail))
        {
            var pattern = $"%{EscapeLike(payerEmail.Trim())}%";
            query = query.Where(p => _db.Users.Any(u =>
                u.Id == p.PayerUserId.ToString() && u.Email != null && EF.Functions.ILike(u.Email, pattern)));
        }

        var total = await query.CountAsync(ct);
        var rows = await Project(query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var payers = await LoadPayersAsync(rows.Select(r => r.PayerUserId), ct);
        var items = rows.Select(r => ToDto(r, payers)).ToList();
        return Ok(new AdminPagedResult<AdminPaymentDto>(items, page, pageSize, total));
    }

    [HttpGet("api/admin/payments/{reference}")]
    public async Task<ActionResult<AdminPaymentDetailDto>> GetPayment(string reference, CancellationToken ct = default)
    {
        var row = await Project(_db.SeatPayments.AsNoTracking().Where(p => p.Reference == reference))
            .FirstOrDefaultAsync(ct);
        if (row is null) return NotFound();

        var payers = await LoadPayersAsync(new[] { row.PayerUserId }, ct);
        var events = await _db.PaymentWebhookEvents.AsNoTracking()
            .Where(e => e.Reference == reference)
            .OrderByDescending(e => e.ReceivedAt)
            .Select(EventRow)
            .ToListAsync(ct);

        return Ok(new AdminPaymentDetailDto(
            ToDto(row, payers),
            row.TripInterestId,
            row.Currency,
            row.UpdatedAt,
            row.VerifyResponseJson,
            events));
    }

    // ══ Webhook audit ═════════════════════════════════════════════════════════

    [HttpGet("api/admin/payment-events")]
    public async Task<ActionResult<AdminPagedResult<AdminPaymentEventDto>>> GetEvents(
        [FromQuery] bool unprocessed = false,
        [FromQuery] string? eventType = null,
        [FromQuery] string? reference = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.PaymentWebhookEvents.AsNoTracking();
        // Unprocessed: never processed, or processing recorded an error.
        if (unprocessed) query = query.Where(e => e.ProcessedAt == null || e.Error != null);
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var type = eventType.Trim().ToLowerInvariant();
            query = query.Where(e => e.EventType.ToLower() == type);
        }
        if (!string.IsNullOrWhiteSpace(reference))
        {
            var r = reference.Trim();
            query = query.Where(e => e.Reference == r);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(EventRow)
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminPaymentEventDto>(items, page, pageSize, total));
    }

    [HttpGet("api/admin/payment-events/{id:guid}")]
    public async Task<ActionResult<AdminPaymentEventDetailDto>> GetEvent(Guid id, CancellationToken ct = default)
    {
        var e = await _db.PaymentWebhookEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        return Ok(new AdminPaymentEventDetailDto(
            new AdminPaymentEventDto(e.Id, e.Provider, e.EventType, e.Reference, e.ReceivedAt, e.ProcessedAt, e.Error),
            e.PayloadSha256,
            e.Payload));
    }

    /// <summary>
    /// Re-runs the webhook processor for an event that isn't processed yet
    /// (e.g. Paystack's verify failed the first time). Already-processed events
    /// are not re-run (200, "AlreadyProcessed"); payments are credited once
    /// either way because ApplyAsync is idempotent on the reference.
    /// </summary>
    [HttpPost("api/admin/payment-events/{id:guid}/reprocess")]
    public async Task<ActionResult<AdminReprocessEventResultDto>> ReprocessEvent(Guid id, CancellationToken ct = default)
    {
        var outcome = await _webhooks.ReprocessAsync(id, ct);
        if (outcome == ReprocessOutcome.NotFound) return NotFound();

        _logger.LogInformation("Admin {AdminId} reprocessed payment event {EventId}: {Outcome}",
            _currentUser.UserId, id, outcome);

        var evt = await _db.PaymentWebhookEvents.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(EventRow)
            .FirstAsync(ct);
        return Ok(new AdminReprocessEventResultDto(outcome.ToString(), evt));
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    private static DateTime StartOfDayUtc(DateOnly watDay) =>
        DateTime.SpecifyKind(watDay.ToDateTime(TimeOnly.MinValue) - Wat, DateTimeKind.Utc);

    private static readonly System.Linq.Expressions.Expression<Func<PaymentWebhookEvent, AdminPaymentEventDto>> EventRow =
        e => new AdminPaymentEventDto(e.Id, e.Provider, e.EventType, e.Reference, e.ReceivedAt, e.ProcessedAt, e.Error);

    private sealed class PaymentRow
    {
        public SeatPayment Payment { get; init; } = null!;
        public Guid PayerUserId => Payment.PayerUserId;
        public Guid TripInterestId => Payment.TripInterestId;
        public string Currency => Payment.Currency;
        public DateTime UpdatedAt => Payment.UpdatedAt;
        public string? VerifyResponseJson => Payment.VerifyResponseJson;
        public string? DestinationName { get; init; }
        public string? WishlistName { get; init; }
        public string? FreeTextDestination { get; init; }
        public string? Caption { get; init; }
        public Guid? HostId { get; init; }
        public string? HostDisplayName { get; init; }
    }

    private static IQueryable<PaymentRow> Project(IQueryable<SeatPayment> query) => query.Select(p => new PaymentRow
    {
        Payment = p,
        DestinationName = p.TripPost!.Destination != null ? p.TripPost.Destination.DestinationName : null,
        WishlistName = p.TripPost.Wishlist != null ? p.TripPost.Wishlist.WishlistName : null,
        FreeTextDestination = p.TripPost.FreeTextDestination,
        Caption = p.TripPost.Caption,
        HostId = p.TripPost.HostProfileId,
        HostDisplayName = p.TripPost.HostProfile != null ? p.TripPost.HostProfile.DisplayName : null,
    });

    private static AdminPaymentDto ToDto(PaymentRow r, Dictionary<Guid, (string? Name, string? Email)> payers)
    {
        var p = r.Payment;
        var payer = payers.GetValueOrDefault(p.PayerUserId);
        return new AdminPaymentDto(
            p.Id,
            p.Reference,
            p.Status.ToString(),
            p.Kind.ToString(),
            p.InstalmentSequence,
            new AdminPaymentTripDto(p.TripPostId,
                PaymentService.TripTitle(r.DestinationName, r.WishlistName, r.FreeTextDestination, r.Caption)),
            r.HostId is Guid hid ? new AdminPaymentHostDto(hid, r.HostDisplayName ?? string.Empty) : null,
            new AdminPaymentPayerDto(p.PayerUserId, payer.Name, payer.Email),
            p.SeatAmountKobo,
            p.HodracFeeKobo,
            p.TravellerFeeKobo,
            p.TotalChargedKobo,
            p.PaystackFeeKobo,
            p.RefundedKobo,
            p.Channel,
            p.CreatedAt,
            p.PaidAt);
    }

    // ApplicationUser.Id is the Guid's string form.
    private async Task<Dictionary<Guid, (string? Name, string? Email)>> LoadPayersAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().Select(g => g.ToString()).ToList();
        var result = new Dictionary<Guid, (string? Name, string? Email)>();
        if (ids.Count == 0) return result;

        var users = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);
        foreach (var u in users)
            if (Guid.TryParse(u.Id, out var gid))
                result[gid] = (u.DisplayName, u.Email);
        return result;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
