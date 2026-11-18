using System.Security.Cryptography;
using System.Text;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
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
        ILogger<PaymentsController> logger)
    {
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

    public record AcceptTermsRequest(int TermsVersion);
    public record AcceptTermsResponse(int TermsVersionAccepted, DateTime TermsAcceptedAt, string TermsTextSha256);

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
        int? TermsVersionAccepted);

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

        var interest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId);
        if (interest is null || interest.TripPostId != id || interest.RequesterUserId != CurrentUserId)
            return NotFound();

        if (interest.Status != TripInterestStatus.Approved)
            return Conflict("Your request to join hasn't been approved yet.");
        if (request.TermsVersion != trip.TermsVersion || string.IsNullOrEmpty(trip.TermsText))
            return Conflict(StaleTermsMessage);

        var now = DateTime.UtcNow;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trip.TermsText))).ToLowerInvariant();
        interest.TermsAcceptedAt = now;
        interest.TermsVersionAccepted = trip.TermsVersion;
        interest.TermsTextSha256 = hash;
        interest.TermsAcceptedIp = Truncate(HttpContext.Connection.RemoteIpAddress?.ToString(), TripInterest.MaxTermsAcceptedIpLength);
        interest.TermsAcceptedUserAgent = Truncate(Request.Headers.UserAgent.ToString(), TripInterest.MaxTermsAcceptedUserAgentLength);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _payments.PushManifestUpdateAsync(trip.Id, interest.Id);
        return new AcceptTermsResponse(trip.TermsVersion, now, hash);
    }

    // ── Balance ──────────────────────────────────────────────────────────────

    [HttpGet("api/trips/{id:guid}/interests/{interestId:guid}/balance")]
    public async Task<ActionResult<BalanceDto>> GetBalance(Guid id, Guid interestId)
    {
        var (trip, interest) = await LoadRequesterInterestAsync(id, interestId);
        if (trip is null || interest is null) return NotFound();

        var lines = PaymentSchedule.Allocate(trip, interest.AmountPaidKobo);
        var paidAt = await _db.SeatPayments.AsNoTracking()
            .Where(p => p.TripInterestId == interest.Id && PaymentService.CreditedStatuses.Contains(p.Status))
            .Select(p => new { p.Kind, p.InstalmentSequence, p.PaidAt })
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // WAT
        var firstUnpaid = lines.FirstOrDefault(l => !l.IsPaid);
        var schedule = lines.Select(l => new ScheduleLineDto(
            l.Kind.ToString(),
            l.Sequence,
            l.AmountKobo,
            l.PaidKobo,
            l.DueDate,
            l.IsPaid ? "Paid"
                : l.DueDate is { } due && due < today ? "Overdue"
                : ReferenceEquals(l, firstUnpaid) ? "Due"
                : "Upcoming",
            l.IsPaid ? LinePaidAt(l, paidAt.Select(p => (p.Kind, p.InstalmentSequence, p.PaidAt))) : null)).ToList();

        NextPaymentDto? next = null;
        if (firstUnpaid is not null)
        {
            var quote = _fees.Calculate(trip, trip.HostProfile ?? new HostProfile(), firstUnpaid.RemainingKobo, DateTime.UtcNow);
            next = new NextPaymentDto(firstUnpaid.Kind.ToString(), firstUnpaid.Sequence, quote.SeatAmountKobo, quote.TravellerFeeKobo, quote.TotalChargedKobo);
        }

        var price = trip.PricePerSeatKobo ?? 0;
        return new BalanceDto(
            price,
            interest.AmountPaidKobo,
            Math.Max(0, price - interest.AmountPaidKobo),
            schedule,
            next,
            interest.PaymentStatus.ToString(),
            interest.SeatHoldExpiresAt,
            trip.TermsVersion,
            interest.TermsVersionAccepted);
    }

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

        // What this checkout covers, from the allocation of what's been paid.
        var lines = PaymentSchedule.Allocate(trip, interest.AmountPaidKobo);
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
