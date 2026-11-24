using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Hodrac Host traveller payments: terms, balance, checkout, verify, receipts,
/// and the Paystack webhook. Hosted trips only: for community trips (and for
/// anyone but the requester) these endpoints return 404.
/// Money is kobo; every payment is verified with Paystack and applied by
/// <see cref="PaymentService"/> (idempotent on the reference).
/// </summary>
[ApiController]
[Authorize]
public class PaymentsController : ControllerBase
{
    /// <summary>An Initialized checkout is reused (same line and amount) while younger than this.</summary>
    public static readonly TimeSpan CheckoutReuseWindow = TimeSpan.FromMinutes(20);

    public const string HostNotVerifiedMessage = "Host isn't verified yet";
    public const string TermsNotAcceptedMessage = "Accept the trip's current terms before paying.";
    public const string StaleTermsMessage = "The terms have changed since you opened them. Read the latest version and accept again.";
    public const string SeatHoldExpiredMessage = "Your seat hold has expired. Ask the host to approve you again, or use \"Request again\" on the trip page to send a new request.";
    public const string AlreadyPaidMessage = "This payment has already been made.";

    private readonly HodracDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaystackClient _paystack;
    private readonly PaymentService _payments;
    private readonly PaystackWebhookProcessor _webhooks;
    private readonly FeeCalculator _fees;
    private readonly PaystackOptions _paystackOptions;
    private readonly IAuthorizationService _authorization;
    private readonly INotificationService _notifications;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        HodracDbContext db,
        ICurrentUserService currentUser,
        IPaystackClient paystack,
        PaymentService payments,
        PaystackWebhookProcessor webhooks,
        FeeCalculator fees,
        IOptions<PaystackOptions> paystackOptions,
        IAuthorizationService authorization,
        INotificationService notifications,
        ILogger<PaymentsController> logger)
    {
        _notifications = notifications;
        _db = db;
        _currentUser = currentUser;
        _paystack = paystack;
        _payments = payments;
        _webhooks = webhooks;
        _fees = fees;
        _paystackOptions = paystackOptions.Value;
        _authorization = authorization;
        _logger = logger;
    }

    private Guid CurrentUserId => _currentUser.UserId;

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public record TermsDto(string TermsText, int TermsVersion, DateTime? TermsUpdatedAt);

    /// <summary>One traveller-chosen instalment date (TravellerChooses trips).</summary>
    public record InstalmentDateRequest(int Sequence, DateOnly DueDate);

    /// <summary>InstalmentDates: required on TravellerChooses trips (a date for every unpaid instalment); ignored otherwise.</summary>
    public record AcceptTermsRequest(int TermsVersion, List<InstalmentDateRequest>? InstalmentDates = null);
    public record AcceptTermsResponse(
        int TermsVersionAccepted, DateTime TermsAcceptedAt, string TermsTextSha256,
        List<InstalmentDateRequest>? InstalmentDates = null);

    public record ChangeInstalmentDatesRequest(List<InstalmentDateRequest>? InstalmentDates);

    public record ScheduleLineDto(string Kind, int? Sequence, long AmountKobo, long PaidKobo, DateOnly? DueDate, string Status, DateTime? PaidAt);
    public record NextPaymentDto(string Kind, int? Sequence, long SeatAmountKobo, long FeeKobo, long TotalKobo);
    public record BalanceDto(
        long PriceKobo,
        long AmountPaidKobo,
        long OwesKobo,
        List<ScheduleLineDto> Schedule,
        NextPaymentDto? NextPayment,
        string PaymentStatus,
        DateTime? SeatHoldExpiresAt,
        int TermsVersion,
        int? TermsVersionAccepted,
        // "HostSet" | "TravellerChooses"; PayInFullByDate only for TravellerChooses.
        string InstalmentDatesMode = nameof(Models.InstalmentDatesMode.HostSet),
        DateOnly? PayInFullByDate = null,
        // TravellerChooses: the traveller has stored dates (otherwise the
        // schedule shows PayInFullByDate as a fallback), and may move unpaid ones now.
        bool DatesChosenByTraveller = false,
        bool CanChangeDates = false);

    /// <summary>Kind: "Deposit" | "Instalment" | "Full". InstalmentSequence is required for Instalment.</summary>
    public record CheckoutRequest(string? Kind, int? InstalmentSequence);
    public record CheckoutResponse(string AuthorizationUrl, string Reference, long TotalKobo);

    public record VerifyResponse(string Status, PaymentReceiptDto? Receipt);

    // ── Terms ────────────────────────────────────────────────────────────────

    [HttpGet("api/trips/{id:guid}/terms")]
    public async Task<ActionResult<TermsDto>> GetTerms(Guid id)
    {
        var trip = await _db.TripPosts.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (trip is null || !trip.IsHostedTrip || (trip.IsHidden && trip.AuthorUserId != CurrentUserId))
            return NotFound();
        return new TermsDto(trip.TermsText ?? string.Empty, trip.TermsVersion, trip.TermsUpdatedAt);
    }

    [HttpPost("api/trips/{id:guid}/interests/{interestId:guid}/accept-terms")]
    public async Task<ActionResult<AcceptTermsResponse>> AcceptTerms(Guid id, Guid interestId, AcceptTermsRequest request)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        // FOR SHARE: a concurrent terms edit (UpdateTrip takes FOR UPDATE) can't
        // bump the version between the check and the save.
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {id} FOR SHARE");

        var trip = await _db.TripPosts.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (trip is null || !trip.IsHostedTrip || trip.IsHidden) return NotFound();

        // One date writer per interest at a time (accept-terms / change-dates).
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TripInterests\" WHERE \"Id\" = {interestId} FOR NO KEY UPDATE");
        var interest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId);
        if (interest is null || interest.TripPostId != id || interest.RequesterUserId != CurrentUserId)
            return NotFound();

        if (interest.Status != TripInterestStatus.Approved)
            return Conflict("Your request to join hasn't been approved yet.");
        if (request.TermsVersion != trip.TermsVersion || string.IsNullOrEmpty(trip.TermsText))
            return Conflict(StaleTermsMessage);

        var now = DateTime.UtcNow;
        List<InstalmentDateRequest>? agreedDates = null;
        if (trip.InstalmentDatesMode == InstalmentDatesMode.TravellerChooses)
        {
            var result = await ApplyTravellerDatesAsync(trip, interest, request.InstalmentDates, now);
            if (result.Errors is not null) return DatesValidationProblem(result.Errors);
            agreedDates = result.Dates;
            interest.TermsInstalmentDatesJson = JsonSerializer.Serialize(
                agreedDates.Select(d => new { sequence = d.Sequence, dueDate = d.DueDate.ToString("yyyy-MM-dd") }));
        }
        else
        {
            interest.TermsInstalmentDatesJson = null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trip.TermsText))).ToLowerInvariant();
        interest.TermsAcceptedAt = now;
        interest.TermsVersionAccepted = trip.TermsVersion;
        interest.TermsTextSha256 = hash;
        interest.TermsAcceptedIp = Truncate(HttpContext.Connection.RemoteIpAddress?.ToString(), TripInterest.MaxTermsAcceptedIpLength);
        interest.TermsAcceptedUserAgent = Truncate(Request.Headers.UserAgent.ToString(), TripInterest.MaxTermsAcceptedUserAgentLength);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _payments.PushManifestUpdateAsync(trip.Id, interest.Id);
        return new AcceptTermsResponse(trip.TermsVersion, now, hash, agreedDates);
    }

    // ── Traveller-chosen instalment dates ────────────────────────────────────

    /// <summary>
    /// TravellerChooses trips: the requester moves the dates of their unpaid
    /// instalments (in order, from today to the pay-in-full date; paid ones
    /// can't change). Reminders already sent for a moved instalment are
    /// cleared so they fire again on the new date; the host is notified.
    /// 404: not the requester / not hosted. 409: host sets the dates, not
    /// approved, or the trip is cancelled. 400: invalid dates. Returns the balance.
    /// </summary>
    [HttpPut("api/trips/{id:guid}/interests/{interestId:guid}/instalment-dates")]
    public async Task<ActionResult<BalanceDto>> ChangeInstalmentDates(Guid id, Guid interestId, ChangeInstalmentDatesRequest request)
    {
        TripPost trip;
        TripInterest interest;
        List<int> changed;
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            // Trip FOR SHARE: no edit or payment on the trip mid-change (what's
            // paid decides which lines may move); then the interest.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {id} FOR SHARE");
            var loadedTrip = await _db.TripPosts.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (loadedTrip is null || !loadedTrip.IsHostedTrip || loadedTrip.IsHidden) return NotFound();
            trip = loadedTrip;

            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TripInterests\" WHERE \"Id\" = {interestId} FOR NO KEY UPDATE");
            var loadedInterest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId);
            if (loadedInterest is null || loadedInterest.TripPostId != id || loadedInterest.RequesterUserId != CurrentUserId)
                return NotFound();
            interest = loadedInterest;

            if (trip.InstalmentDatesMode != InstalmentDatesMode.TravellerChooses)
                return Conflict("The host sets the instalment dates on this trip.");
            if (interest.Status != TripInterestStatus.Approved)
                return Conflict("Only an approved traveller can change their instalment dates.");
            if (trip.Status == TripPostStatus.Cancelled)
                return Conflict("This trip has been cancelled.");

            var result = await ApplyTravellerDatesAsync(trip, interest, request.InstalmentDates, DateTime.UtcNow);
            if (result.Errors is not null) return DatesValidationProblem(result.Errors);
            changed = result.Changed;

            if (changed.Count > 0)
            {
                // Reminders fire again on the new dates.
                await _db.PaymentReminderLogs
                    .Where(r => r.TripInterestId == interest.Id
                        && r.InstalmentSequence != null
                        && changed.Contains(r.InstalmentSequence.Value))
                    .ExecuteDeleteAsync();
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        if (changed.Count > 0)
        {
            try
            {
                await _notifications.NotifyInstalmentDatesChanged(trip, interest.RequesterUserId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Instalment-dates-changed notification failed for interest {InterestId}", interest.Id);
            }
            await _payments.PushManifestUpdateAsync(trip.Id, interest.Id);
        }

        return await GetBalance(id, interestId);
    }

    private sealed record TravellerDatesResult(
        Dictionary<string, string>? Errors, List<InstalmentDateRequest> Dates, List<int> Changed);

    // Validates and stages (caller saves) the traveller's dates for a
    // TravellerChooses trip: unpaid lines take the sent dates, paid lines keep
    // theirs. Dates = the full stored set afterwards; Changed = sequences whose
    // date is new or different. Caller holds the trip and interest locks.
    private async Task<TravellerDatesResult> ApplyTravellerDatesAsync(
        TripPost trip, TripInterest interest, List<InstalmentDateRequest>? sent, DateTime nowUtc)
    {
        var existingRows = await _db.TripInterestInstalmentDates
            .Where(d => d.TripInterestId == interest.Id)
            .ToListAsync();
        var existing = PaymentSchedule.DateMap(existingRows);
        var lines = PaymentSchedule.Allocate(trip, interest.AmountPaidKobo, existing);
        var paid = lines
            .Where(l => l.Kind == SeatPaymentKind.Instalment && l.IsPaid && l.Sequence is not null)
            .Select(l => l.Sequence!.Value)
            .ToHashSet();

        var dates = (sent ?? new List<InstalmentDateRequest>())
            .Where(d => d is not null)
            .Select(d => (d.Sequence, d.DueDate))
            .ToList();
        var errors = InstalmentValidator.ValidateTravellerDates(
            trip, dates, paid, existing, HostTripService.TodayWat(nowUtc));
        if (errors.Count > 0) return new TravellerDatesResult(errors, new(), new());

        var changed = new List<int>();
        foreach (var (sequence, due) in dates.Where(d => !paid.Contains(d.Sequence)))
        {
            var row = existingRows.FirstOrDefault(r => r.Sequence == sequence);
            if (row is null)
            {
                _db.TripInterestInstalmentDates.Add(new TripInterestInstalmentDate
                {
                    TripInterestId = interest.Id,
                    Sequence = sequence,
                    DueDate = due,
                    ChosenAt = nowUtc,
                    UpdatedAt = nowUtc,
                });
                changed.Add(sequence);
            }
            else if (row.DueDate != due)
            {
                row.DueDate = due;
                row.UpdatedAt = nowUtc;
                changed.Add(sequence);
            }
        }

        // Rows for sequences the trip no longer has are dropped.
        var tripSequences = trip.Instalments.Select(i => i.Sequence).ToHashSet();
        _db.TripInterestInstalmentDates.RemoveRange(existingRows.Where(r => !tripSequences.Contains(r.Sequence)));

        var final = existing.Where(e => paid.Contains(e.Key) && tripSequences.Contains(e.Key))
            .Concat(dates.Where(d => !paid.Contains(d.Sequence)).Select(d => new KeyValuePair<int, DateOnly>(d.Sequence, d.DueDate)))
            .OrderBy(e => e.Key)
            .Select(e => new InstalmentDateRequest(e.Key, e.Value))
            .ToList();
        return new TravellerDatesResult(null, final, changed.OrderBy(c => c).ToList());
    }

    private ActionResult DatesValidationProblem(Dictionary<string, string> errors)
    {
        foreach (var (key, message) in errors) ModelState.AddModelError(key, message);
        return ValidationProblem(ModelState);
    }

    // ── Balance ──────────────────────────────────────────────────────────────

    [HttpGet("api/trips/{id:guid}/interests/{interestId:guid}/balance")]
    public async Task<ActionResult<BalanceDto>> GetBalance(Guid id, Guid interestId)
    {
        var (trip, interest) = await LoadRequesterInterestAsync(id, interestId);
        if (trip is null || interest is null) return NotFound();

        var travellerDates = await TravellerInstalmentDates.LoadAsync(_db, interest.Id);
        var lines = PaymentSchedule.Allocate(trip, interest.AmountPaidKobo, travellerDates);
        var paidAt = await _db.SeatPayments.AsNoTracking()
            .Where(p => p.TripInterestId == interest.Id && PaymentService.CreditedStatuses.Contains(p.Status))
            .Select(p => new { p.Kind, p.InstalmentSequence, p.PaidAt })
            .ToListAsync();

        var today = HostTripService.TodayWat(DateTime.UtcNow);
        var firstUnpaid = lines.FirstOrDefault(l => !l.IsPaid);
        var schedule = lines.Select(l => new ScheduleLineDto(
            l.Kind.ToString(),
            l.Sequence,
            l.AmountKobo,
            l.PaidKobo,
            l.DueDate,
            PaymentSchedule.LineStatus(l, firstUnpaid, today),
            l.IsPaid ? LinePaidAt(l, paidAt.Select(p => (p.Kind, p.InstalmentSequence, p.PaidAt))) : null)).ToList();

        NextPaymentDto? next = null;
        if (firstUnpaid is not null)
        {
            var quote = _fees.Calculate(trip, trip.HostProfile ?? new HostProfile(), firstUnpaid.RemainingKobo, DateTime.UtcNow);
            next = new NextPaymentDto(firstUnpaid.Kind.ToString(), firstUnpaid.Sequence, quote.SeatAmountKobo, quote.TravellerFeeKobo, quote.TotalChargedKobo);
        }

        var price = trip.PricePerSeatKobo ?? 0;
        var travellerChooses = trip.InstalmentDatesMode == InstalmentDatesMode.TravellerChooses;
        return new BalanceDto(
            price,
            interest.AmountPaidKobo,
            Math.Max(0, price - interest.AmountPaidKobo),
            schedule,
            next,
            interest.PaymentStatus.ToString(),
            interest.SeatHoldExpiresAt,
            trip.TermsVersion,
            interest.TermsVersionAccepted,
            trip.InstalmentDatesMode.ToString(),
            travellerChooses ? trip.PayInFullByDate : null,
            travellerChooses && travellerDates.Count > 0,
            CanChangeDates(trip, interest, lines, today));
    }

    /// <summary>TravellerChooses, approved, trip not cancelled, an unpaid instalment, and the pay-in-full date not passed.</summary>
    private static bool CanChangeDates(TripPost trip, TripInterest interest, List<ScheduleLine> lines, DateOnly today) =>
        trip.InstalmentDatesMode == InstalmentDatesMode.TravellerChooses
        && interest.Status == TripInterestStatus.Approved
        && trip.Status != TripPostStatus.Cancelled
        && lines.Any(l => l.Kind == SeatPaymentKind.Instalment && !l.IsPaid)
        && trip.PayInFullByDate is { } deadline && today <= deadline;

    // A line's paid time: the latest payment made for that line, or a Full
    // payment (lines covered by manual payments have none).
    private static DateTime? LinePaidAt(ScheduleLine line, IEnumerable<(SeatPaymentKind Kind, int? Seq, DateTime? PaidAt)> payments) =>
        payments
            .Where(p => (p.Kind == line.Kind && p.Seq == line.Sequence) || p.Kind == SeatPaymentKind.Full)
            .Max(p => p.PaidAt);

    // ── Checkout ─────────────────────────────────────────────────────────────

    [HttpPost("api/trips/{id:guid}/interests/{interestId:guid}/checkout")]
    [EnableRateLimiting("payments-checkout")]
    public async Task<ActionResult<CheckoutResponse>> Checkout(Guid id, Guid interestId, CheckoutRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<SeatPaymentKind>(request.Kind, ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind) || int.TryParse(request.Kind, out _))
            return BadRequest("Kind must be Deposit, Instalment or Full.");
        if (kind == SeatPaymentKind.Instalment && request.InstalmentSequence is null)
            return BadRequest("Choose which instalment to pay.");
        var sequence = kind == SeatPaymentKind.Instalment ? request.InstalmentSequence : null;

        // A checkout the traveller already paid but that isn't recorded yet
        // (webhook late, callback page closed) is credited first, so the line
        // shows as paid below and they're never charged for it twice.
        if (await _db.TripInterests.AnyAsync(i => i.Id == interestId && i.TripPostId == id && i.RequesterUserId == CurrentUserId, ct))
            await _payments.SettleOpenCheckoutsAsync(interestId, ct);

        // Locks, held until the Paystack checkout exists (always trip first, the
        // order every other trip/interest writer uses, so nothing deadlocks):
        //  - the trip FOR SHARE: price, schedule, terms and approvals can't
        //    change mid-checkout (edits/approvals take FOR UPDATE and wait);
        //  - the interest FOR NO KEY UPDATE: one checkout per interest at a
        //    time, so two taps can't open two charges for the same line.
        // PaymentService takes the trip FOR NO KEY UPDATE, so a payment being
        // applied on this trip waits for the checkout (and vice versa).
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {id} FOR SHARE", ct);

        var trip = await _db.TripPosts.AsNoTracking()
            .Include(t => t.HostProfile)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trip is null || !trip.IsHostedTrip || trip.IsHidden) return NotFound();

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TripInterests\" WHERE \"Id\" = {interestId} FOR NO KEY UPDATE", ct);

        var interest = await _db.TripInterests.AsNoTracking().FirstOrDefaultAsync(i => i.Id == interestId, ct);
        if (interest is null || interest.TripPostId != id || interest.RequesterUserId != CurrentUserId)
            return NotFound();

        if (interest.Status != TripInterestStatus.Approved)
            return Conflict("Your request to join hasn't been approved.");
        if (trip.Status != TripPostStatus.Open)
            return Conflict($"This trip is {trip.Status.ToString().ToLowerInvariant()} and isn't taking payments.");
        if (interest.TermsVersionAccepted != trip.TermsVersion)
            return Conflict(TermsNotAcceptedMessage);

        var host = trip.HostProfile;
        if (host is null || host.VerificationStatus != HostVerificationStatus.Verified
            || string.IsNullOrWhiteSpace(host.PaystackSubaccountCode))
            return Conflict(HostNotVerifiedMessage);

        // What this checkout covers, from the allocation of what's been paid
        // (traveller dates only label the lines; they never gate a payment).
        var lines = PaymentSchedule.Allocate(trip, interest.AmountPaidKobo,
            await TravellerInstalmentDates.LoadAsync(_db, interest.Id, ct));
        var deposit = lines.FirstOrDefault(l => l.Kind == SeatPaymentKind.Deposit);
        long seatAmount;
        bool coversDeposit;
        switch (kind)
        {
            case SeatPaymentKind.Deposit:
                if (deposit is null || deposit.IsPaid) return Conflict(AlreadyPaidMessage);
                seatAmount = deposit.RemainingKobo;
                coversDeposit = true;
                break;
            case SeatPaymentKind.Instalment:
            {
                var index = lines.FindIndex(l => l.Kind == SeatPaymentKind.Instalment && l.Sequence == sequence);
                if (index < 0) return BadRequest("This trip has no such instalment.");
                if (lines[index].IsPaid) return Conflict(AlreadyPaidMessage);
                if (lines.Take(index).Any(l => !l.IsPaid))
                    return Conflict("Pay the deposit and earlier instalments first.");
                seatAmount = lines[index].RemainingKobo;
                coversDeposit = false;
                break;
            }
            default:
                seatAmount = lines.Sum(l => Math.Max(0, l.RemainingKobo));
                if (seatAmount <= 0) return Conflict(AlreadyPaidMessage);
                coversDeposit = deposit is { IsPaid: false };
                break;
        }

        var now = DateTime.UtcNow;
        if (coversDeposit
            && (interest.PaymentStatus == InterestPaymentStatus.Expired
                || (interest.SeatHoldExpiresAt is { } holdEnds && now >= holdEnds)))
            return Conflict(SeatHoldExpiredMessage);

        var quote = _fees.Calculate(trip, host, seatAmount, now);

        // Reuse a fresh, identical checkout instead of opening a second charge.
        var reuseAfter = now - CheckoutReuseWindow;
        var existing = await _db.SeatPayments.AsNoTracking()
            .Where(p => p.TripInterestId == interest.Id
                && p.PayerUserId == CurrentUserId
                && p.Status == SeatPaymentStatus.Initialized
                && p.Kind == kind
                && p.InstalmentSequence == sequence
                && p.SeatAmountKobo == quote.SeatAmountKobo
                && p.TotalChargedKobo == quote.TotalChargedKobo
                && p.SubaccountCode == host.PaystackSubaccountCode
                && p.AuthorizationUrl != null
                && p.CreatedAt > reuseAfter)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            await tx.CommitAsync(ct);
            return new CheckoutResponse(existing.AuthorizationUrl!, existing.Reference, existing.TotalChargedKobo);
        }

        var payment = new SeatPayment
        {
            TripPostId = trip.Id,
            TripInterestId = interest.Id,
            PayerUserId = CurrentUserId,
            Reference = PaymentReference.New(now),
            Kind = kind,
            InstalmentSequence = sequence,
            SeatAmountKobo = quote.SeatAmountKobo,
            HodracFeeKobo = quote.HodracFeeKobo,
            TravellerFeeKobo = quote.TravellerFeeKobo,
            TotalChargedKobo = quote.TotalChargedKobo,
            Currency = string.IsNullOrWhiteSpace(trip.Currency) ? TripPost.DefaultCurrency : trip.Currency,
            SubaccountCode = host.PaystackSubaccountCode,
            Status = SeatPaymentStatus.Initialized,
        };
        _db.SeatPayments.Add(payment);
        await _db.SaveChangesAsync(ct);

        var payerEmail = await _db.Users.AsNoTracking()
            .Where(u => u.Id == CurrentUserId.ToString())
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(payerEmail)) payerEmail = _currentUser.Email;

        var metadata = new Dictionary<string, string>
        {
            ["tripId"] = trip.Id.ToString(),
            ["interestId"] = interest.Id.ToString(),
            ["seatPaymentId"] = payment.Id.ToString(),
            ["kind"] = kind.ToString(),
        };
        if (sequence is not null) metadata["sequence"] = sequence.Value.ToString();

        try
        {
            var init = await _paystack.InitializeAsync(new PaystackInitializeRequest(
                Email: payerEmail,
                AmountKobo: quote.TotalChargedKobo,
                Reference: payment.Reference,
                CallbackUrl: string.IsNullOrWhiteSpace(_paystackOptions.CallbackUrl) ? null : _paystackOptions.CallbackUrl,
                SubaccountCode: host.PaystackSubaccountCode,
                TransactionChargeKobo: quote.TransactionChargeKobo,
                Bearer: quote.Bearer,
                Metadata: metadata,
                Currency: payment.Currency), ct);

            payment.AuthorizationUrl = init.AuthorizationUrl.Length <= SeatPayment.MaxAuthorizationUrlLength
                ? init.AuthorizationUrl
                : null;
            payment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new CheckoutResponse(init.AuthorizationUrl, payment.Reference, payment.TotalChargedKobo);
        }
        catch (PaystackException ex)
        {
            payment.Status = SeatPaymentStatus.Failed;
            payment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
            return PaystackFailure(ex);
        }
    }

    // ── Verify (frontend callback page) ──────────────────────────────────────

    [HttpGet("api/payments/verify/{reference}")]
    [EnableRateLimiting("payments-verify")]
    public async Task<ActionResult<VerifyResponse>> Verify(string reference, CancellationToken ct)
    {
        var payment = await _db.SeatPayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Reference == reference && p.PayerUserId == CurrentUserId, ct);
        if (payment is null) return NotFound();

        var status = payment.Status;
        if (!PaymentService.SucceededStatuses.Contains(status))
        {
            try
            {
                var verified = await _paystack.VerifyAsync(reference, ct);
                var result = await _payments.ApplyAsync(verified, ct);
                status = result.Status ?? status;
            }
            catch (PaystackException ex)
            {
                return PaystackFailure(ex);
            }
        }

        var receipt = PaymentService.SucceededStatuses.Contains(status)
            ? (await _payments.BuildReceiptsAsync(_db.SeatPayments.Where(p => p.Reference == reference), ct)).FirstOrDefault()
            : null;
        return new VerifyResponse(status.ToString(), receipt);
    }

    // ── Receipts ─────────────────────────────────────────────────────────────

    [HttpGet("api/me/payments")]
    public async Task<ActionResult<List<PaymentReceiptDto>>> MyPayments(CancellationToken ct) =>
        await _payments.BuildReceiptsAsync(_db.SeatPayments.Where(p => p.PayerUserId == CurrentUserId), ct);

    /// <summary>The payer, the trip's author, or an admin; 404 for anyone else.</summary>
    [HttpGet("api/payments/{reference}/receipt")]
    public async Task<ActionResult<PaymentReceiptDto>> Receipt(string reference, CancellationToken ct)
    {
        var access = await _db.SeatPayments.AsNoTracking()
            .Where(p => p.Reference == reference)
            .Select(p => new { p.PayerUserId, p.TripPost!.AuthorUserId })
            .FirstOrDefaultAsync(ct);
        if (access is null) return NotFound();

        var allowed = access.PayerUserId == CurrentUserId
            || access.AuthorUserId == CurrentUserId
            || (await _authorization.AuthorizeAsync(User, "Admin")).Succeeded;
        if (!allowed) return NotFound();

        var receipt = (await _payments.BuildReceiptsAsync(_db.SeatPayments.Where(p => p.Reference == reference), ct)).FirstOrDefault();
        return receipt is null ? NotFound() : receipt;
    }

    // ── Paystack webhook ─────────────────────────────────────────────────────

    /// <summary>
    /// Paystack events. The raw body is read before anything parses it, its
    /// HMAC-SHA512 (<c>x-paystack-signature</c>, keyed with the secret key) is
    /// checked in constant time (401 on failure), then the event is stored,
    /// deduplicated and processed inline (see PaystackWebhookProcessor).
    /// </summary>
    [HttpPost("api/payments/paystack/webhook")]
    [AllowAnonymous]
    [EnableRateLimiting("paystack-webhook")]
    public async Task<IActionResult> PaystackWebhook(CancellationToken ct)
    {
        Request.EnableBuffering();
        if (Request.ContentLength > PaystackWebhookProcessor.MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        byte[] body;
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > PaystackWebhookProcessor.MaxBodyBytes)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge);
                buffer.Write(chunk, 0, read);
            }
            body = buffer.ToArray();
        }
        Request.Body.Position = 0;

        if (!PaystackSignature.IsValid(body, Request.Headers[PaystackSignature.HeaderName].ToString(), _paystackOptions.SecretKey))
        {
            _logger.LogWarning("Paystack webhook with a missing or invalid signature from {Ip}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        // Processed to the end even if Paystack drops the connection.
        var outcome = await _webhooks.HandleAsync(body, CancellationToken.None);
        return outcome == WebhookOutcome.InvalidPayload ? BadRequest() : Ok();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // The caller's own interest on a hosted trip, or (null, null) -> 404.
    private async Task<(TripPost? Trip, TripInterest? Interest)> LoadRequesterInterestAsync(Guid tripId, Guid interestId)
    {
        var trip = await _db.TripPosts.AsNoTracking()
            .Include(t => t.HostProfile)
            .FirstOrDefaultAsync(t => t.Id == tripId);
        if (trip is null || !trip.IsHostedTrip) return (null, null);

        var interest = await _db.TripInterests.AsNoTracking().FirstOrDefaultAsync(i => i.Id == interestId);
        if (interest is null || interest.TripPostId != tripId || interest.RequesterUserId != CurrentUserId)
            return (null, null);
        return (trip, interest);
    }

    /// <summary>Paystack not configured → 503; anything else → 502.</summary>
    private ActionResult PaystackFailure(PaystackException ex)
    {
        if (ex is PaystackNotConfiguredException)
        {
            _logger.LogError("Paystack is not configured; payments are unavailable");
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Payments are not available right now.", detail: "Payments are not available right now.");
        }

        _logger.LogWarning(ex, "Paystack call failed");
        return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Couldn't reach our payment provider. Try again shortly.", detail: "Couldn't reach our payment provider. Try again shortly.");
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
