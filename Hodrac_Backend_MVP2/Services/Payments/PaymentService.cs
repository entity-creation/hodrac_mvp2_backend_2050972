using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Infrastucture.SignalR;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services.Payments;

public enum ApplyOutcome
{
    /// <summary>No SeatPayment has this reference.</summary>
    UnknownReference,
    /// <summary>This call credited the payment.</summary>
    Applied,
    /// <summary>Already credited (or already refunded/disputed): nothing changed.</summary>
    AlreadyApplied,
    /// <summary>Paystack doesn't say "success" (yet): nothing credited.</summary>
    NotPaid,
    /// <summary>Amount, currency or subaccount differ from what we initialized: marked Failed, admins alerted.</summary>
    Mismatch
}

public record ApplyResult(ApplyOutcome Outcome, string Reference, SeatPaymentStatus? Status);

public enum ManualPaymentOutcome
{
    /// <summary>The interest (or manual payment) isn't on this trip, or the interest was declined.</summary>
    NotFound,
    /// <summary>Bad amount for the method (or note too long): 400.</summary>
    Invalid,
    /// <summary>The interest's state doesn't allow it (not Approved/Withdrawn, delete would go below zero): 409.</summary>
    Conflict,
    Done
}

/// <summary>Result of recording or deleting a host-recorded (manual) payment.</summary>
public record ManualPaymentResult(
    ManualPaymentOutcome Outcome,
    string? Error = null,
    ManualPayment? Payment = null,
    TripInterest? Interest = null);

/// <summary>Receipt for one seat payment (the traveller's fee is what they paid on top of the seat).</summary>
public record PaymentReceiptDto(
    string Reference,
    string Status,
    Guid TripId,
    string TripTitle,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string HostDisplayName,
    string Kind,
    int? InstalmentSequence,
    long SeatAmountKobo,
    long FeeKobo,
    long TotalKobo,
    long RefundedKobo,
    string Currency,
    int? TermsVersionAccepted,
    DateTime? PaidAt,
    string? Channel);

/// <summary>
/// Hodrac Host payments: the ONLY place money state changes (seat payments
/// succeeding, refunds, disputes) and where an interest's paid totals and
/// payment status are recomputed.
///
/// Concurrency: every change runs in a transaction that takes, in this order,
/// (1) the trip row FOR NO KEY UPDATE - serializes with approvals/edits (which
/// take FOR UPDATE) and with other payments on the trip, without blocking the
/// foreign-key checks of a concurrent checkout insert - then (2) the
/// SeatPayment row FOR UPDATE by Reference. A second caller for the same
/// reference (verify endpoint racing the webhook, a duplicate webhook) waits,
/// then sees the payment already Success and returns without crediting again.
/// Side effects (emails, notifications, SignalR) run after commit, best-effort.
/// </summary>
public class PaymentService
{
    /// <summary>Statuses whose seat money counts (less any refund).</summary>
    public static readonly SeatPaymentStatus[] CreditedStatuses =
    {
        SeatPaymentStatus.Success,
        SeatPaymentStatus.PartiallyRefunded,
        SeatPaymentStatus.Disputed
    };

    /// <summary>A payment that went through at some point (receipts, "already applied").</summary>
    public static readonly SeatPaymentStatus[] SucceededStatuses =
    {
        SeatPaymentStatus.Success,
        SeatPaymentStatus.Refunded,
        SeatPaymentStatus.PartiallyRefunded,
        SeatPaymentStatus.Disputed
    };

    /// <summary>At most this many of an interest's open checkouts are checked with Paystack at once.</summary>
    public const int MaxOpenCheckoutsToSettle = 5;

    private readonly HodracDbContext _db;
    private readonly IPaystackClient _paystack;
    private readonly ITripThreadService _threads;
    private readonly INotificationService _notifications;
    private readonly IEmailSender _email;
    private readonly IHubContext<TripPostHub> _tripPostHub;
    private readonly ITripsRepository _tripsRepo;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        HodracDbContext db,
        IPaystackClient paystack,
        ITripThreadService threads,
        INotificationService notifications,
        IEmailSender email,
        IHubContext<TripPostHub> tripPostHub,
        ITripsRepository tripsRepo,
        IConfiguration config,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _paystack = paystack;
        _threads = threads;
        _notifications = notifications;
        _email = email;
        _tripPostHub = tripPostHub;
        _tripsRepo = tripsRepo;
        _config = config;
        _logger = logger;
    }

    // ── Apply a verified Paystack transaction ────────────────────────────────

    /// <summary>
    /// Applies a Paystack verify response (never a client- or webhook-supplied
    /// amount). Idempotent on the reference. Clears the change tracker first:
    /// callers must not have unsaved changes.
    /// </summary>
    public async Task<ApplyResult> ApplyAsync(PaystackTransaction verified, CancellationToken ct = default)
    {
        var reference = verified.Reference;
        _db.ChangeTracker.Clear();

        var tripId = await _db.SeatPayments.AsNoTracking()
            .Where(p => p.Reference == reference)
            .Select(p => (Guid?)p.TripPostId)
            .FirstOrDefaultAsync(ct);
        if (tripId is null)
        {
            _logger.LogWarning("Paystack transaction {Reference} doesn't match any seat payment", reference);
            return new ApplyResult(ApplyOutcome.UnknownReference, reference, null);
        }

        SeatPayment payment;
        TripInterest? interest = null;
        string? mismatch = null;
        List<string> attention = new();

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            payment = await LockPaymentAsync(tripId.Value, reference, ct);

            if (SucceededStatuses.Contains(payment.Status))
            {
                await tx.CommitAsync(ct);
                return new ApplyResult(ApplyOutcome.AlreadyApplied, reference, payment.Status);
            }

            if (!verified.IsSuccess)
            {
                // "abandoned"/"ongoing"/"pending" can still complete on the same
                // checkout, so only an explicit "failed" closes the attempt (a
                // later success on it is still credited).
                if (string.Equals(verified.Status, "failed", StringComparison.OrdinalIgnoreCase)
                    && payment.Status == SeatPaymentStatus.Initialized)
                {
                    payment.Status = SeatPaymentStatus.Failed;
                    payment.VerifyResponseJson = verified.RawJson;
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);
                return new ApplyResult(ApplyOutcome.NotPaid, reference, payment.Status);
            }

            mismatch = FindMismatch(payment, verified);
            if (mismatch is not null)
            {
                payment.Status = SeatPaymentStatus.Failed;
                payment.VerifyResponseJson = MismatchJson(mismatch, verified.RawJson);
                payment.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            else
            {
                var now = DateTime.UtcNow;
                payment.Status = SeatPaymentStatus.Success;
                payment.PaidAt = verified.PaidAt ?? now;
                payment.Channel = Truncate(verified.Channel, SeatPayment.MaxChannelLength);
                payment.PaystackFeeKobo = verified.FeesKobo;
                payment.VerifyResponseJson = verified.RawJson;
                payment.UpdatedAt = now;
                await _db.SaveChangesAsync(ct);

                var trip = await _db.TripPosts.FirstAsync(t => t.Id == payment.TripPostId, ct);
                interest = await _db.TripInterests.FirstAsync(i => i.Id == payment.TripInterestId, ct);
                var holdHadExpired = interest.PaymentStatus == InterestPaymentStatus.Expired;
                await RecomputeInterestAsync(interest, trip, ct);
                attention = await FindAttentionProblemsAsync(trip, interest, holdHadExpired, ct);

                // First payment: the traveller joins the trip chat and the seat
                // hold ends (both idempotent for later payments). A payment that
                // lands after a withdrawal/decline is credited but grants nothing.
                if (interest.Status == TripInterestStatus.Approved)
                {
                    await _threads.StageAddToThreadAsync(trip, interest.RequesterUserId);
                    interest.SeatHoldExpiresAt = null;
                }
                else
                {
                    _logger.LogWarning(
                        "Payment {Reference} succeeded for interest {InterestId} in status {Status}; credited without chat access",
                        reference, interest.Id, interest.Status);
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }

        if (mismatch is not null)
        {
            _logger.LogError("Payment {Reference} rejected: {Mismatch}", reference, mismatch);
            await EmailAdminsAsync(
                "Payment mismatch - not credited",
                new List<string>
                {
                    $"Paystack reported payment {reference} as successful, but it doesn't match what Hodrac initialized:",
                    mismatch,
                    "The payment was marked Failed and nothing was credited. Check it in the Paystack dashboard.",
                },
                ct);
            return new ApplyResult(ApplyOutcome.Mismatch, reference, SeatPaymentStatus.Failed);
        }

        await AfterPaymentSucceededAsync(payment, interest!, ct);
        if (attention.Count > 0)
            await AlertPaymentNeedsAttentionAsync(payment, attention, ct);
        return new ApplyResult(ApplyOutcome.Applied, reference, SeatPaymentStatus.Success);
    }

    /// <summary>
    /// Before a new checkout or a seat hold expiry: asks Paystack about this
    /// interest's open (Initialized) checkouts and credits any that were paid
    /// but not recorded yet (webhook late, callback page never loaded), so the
    /// traveller is never charged twice for the same line and a paid seat is
    /// never expired. Best-effort: Paystack errors are logged and skipped.
    /// Clears the change tracker (as ApplyAsync).
    /// </summary>
    public async Task SettleOpenCheckoutsAsync(Guid interestId, CancellationToken ct = default)
    {
        var references = await _db.SeatPayments.AsNoTracking()
            .Where(p => p.TripInterestId == interestId && p.Status == SeatPaymentStatus.Initialized)
            .OrderByDescending(p => p.CreatedAt)
            .Take(MaxOpenCheckoutsToSettle)
            .Select(p => p.Reference)
            .ToListAsync(ct);

        foreach (var reference in references)
        {
            try
            {
                var verified = await _paystack.VerifyAsync(reference, ct);
                await ApplyAsync(verified, ct);
            }
            catch (PaystackException ex)
            {
                _logger.LogInformation("Couldn't check open checkout {Reference} with Paystack: {Message}", reference, ex.Message);
            }
        }
    }

    // A payment that was credited (the money has moved) but lands where it
    // shouldn't: more than the seat price, a traveller who no longer has the
    // seat, a cancelled trip, or a seat hold that expired and was given away.
    // Called inside ApplyAsync's transaction, after the recompute.
    private async Task<List<string>> FindAttentionProblemsAsync(
        TripPost trip, TripInterest interest, bool holdHadExpired, CancellationToken ct)
    {
        var problems = new List<string>();
        var price = trip.PricePerSeatKobo ?? 0;
        if (price > 0 && interest.AmountPaidKobo > price)
            problems.Add($"The traveller has now paid {Naira(interest.AmountPaidKobo)} for a {Naira(price)} seat: {Naira(interest.AmountPaidKobo - price)} too much.");
        if (interest.Status != TripInterestStatus.Approved)
            problems.Add($"The traveller's request is {interest.Status.ToString().ToLowerInvariant()}, so they don't have a seat on this trip.");
        if (trip.Status == TripPostStatus.Cancelled)
            problems.Add("The trip is cancelled.");

        if (holdHadExpired && interest.Status == TripInterestStatus.Approved && trip.MaxGroupSize is { } maxGroupSize)
        {
            var seatsTaken = await _db.TripInterests.CountAsync(i =>
                i.TripPostId == trip.Id
                && i.Status == TripInterestStatus.Approved
                && i.PaymentStatus != InterestPaymentStatus.Expired
                && i.RequesterUserId != trip.AuthorUserId, ct);
            if (seatsTaken + 1 > maxGroupSize)
                problems.Add($"The traveller paid after their seat hold expired, and the trip is now over capacity ({seatsTaken + 1} people for {maxGroupSize} places).");
        }
        return problems;
    }

    private async Task AlertPaymentNeedsAttentionAsync(SeatPayment payment, List<string> problems, CancellationToken ct)
    {
        _logger.LogError("Payment {Reference} credited but needs attention: {Problems}", payment.Reference, string.Join(" ", problems));

        var title = await TripTitleAsync(payment.TripPostId, ct);
        var lines = new List<string>
        {
            $"Payment {payment.Reference} ({Naira(payment.TotalChargedKobo)}) on \"{title}\" went through, but it needs attention:",
        };
        lines.AddRange(problems);
        lines.Add("The money is already in the host's account. Agree with the traveller what happens next; if money goes back to them, record it as a refund on the trip's manifest.");

        var hostUserId = await _db.TripPosts.AsNoTracking()
            .Where(t => t.Id == payment.TripPostId)
            .Select(t => t.AuthorUserId)
            .FirstAsync(ct);
        var hostEmail = await GetUserEmailAsync(hostUserId, ct);
        if (!string.IsNullOrWhiteSpace(hostEmail))
            await TrySendAsync(hostEmail, "A payment on your trip needs attention", lines, $"{FrontendBaseUrl()}/host/trips/{payment.TripPostId}");
        await EmailAdminsAsync("Payment needs attention", lines, ct);
    }

    private static string? FindMismatch(SeatPayment payment, PaystackTransaction verified)
    {
        var problems = new List<string>();
        if (!string.Equals(verified.Reference, payment.Reference, StringComparison.Ordinal))
            problems.Add($"reference {verified.Reference} != {payment.Reference}");
        if (verified.AmountKobo != payment.TotalChargedKobo)
            problems.Add($"amount {verified.AmountKobo} != {payment.TotalChargedKobo} kobo");
        if (!string.Equals(verified.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            problems.Add($"currency {verified.Currency} != {payment.Currency}");
        if (!string.IsNullOrEmpty(payment.SubaccountCode)
            && !string.Equals(verified.SubaccountCode, payment.SubaccountCode, StringComparison.Ordinal))
            problems.Add($"subaccount {verified.SubaccountCode ?? "(none)"} != {payment.SubaccountCode}");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // The audit column keeps Paystack's response next to why it was rejected.
    private static string MismatchJson(string mismatch, string rawJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson);
        return JsonSerializer.Serialize(new { hodracMismatch = mismatch, paystack = doc.RootElement });
    }

    // ── Refunds and disputes (webhook) ───────────────────────────────────────

    /// <summary>
    /// A Paystack refund was processed: adds <paramref name="amountKobo"/> to the
    /// payment's refunded total (Refunded once it covers the whole charge,
    /// PartiallyRefunded before) and recomputes the interest.
    /// </summary>
    public async Task<ApplyResult> ApplyRefundAsync(string reference, long amountKobo, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var tripId = await TripIdForAsync(reference, ct);
        if (tripId is null) return new ApplyResult(ApplyOutcome.UnknownReference, reference, null);

        SeatPayment payment;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            payment = await LockPaymentAsync(tripId.Value, reference, ct);
            if (!CreditedStatuses.Contains(payment.Status))
            {
                await tx.CommitAsync(ct);
                _logger.LogWarning("Refund for {Reference} ignored: payment is {Status}", reference, payment.Status);
                return new ApplyResult(
                    payment.Status == SeatPaymentStatus.Refunded ? ApplyOutcome.AlreadyApplied : ApplyOutcome.NotPaid,
                    reference, payment.Status);
            }

            payment.RefundedKobo = Math.Min(payment.TotalChargedKobo, payment.RefundedKobo + Math.Max(0, amountKobo));
            payment.Status = payment.RefundedKobo >= payment.TotalChargedKobo
                ? SeatPaymentStatus.Refunded
                : SeatPaymentStatus.PartiallyRefunded;
            payment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var trip = await _db.TripPosts.FirstAsync(t => t.Id == payment.TripPostId, ct);
            var interest = await _db.TripInterests.FirstAsync(i => i.Id == payment.TripInterestId, ct);
            await RecomputeInterestAsync(interest, trip, ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await PushManifestUpdateAsync(payment.TripPostId, payment.TripInterestId);
        return new ApplyResult(ApplyOutcome.Applied, reference, payment.Status);
    }

    /// <summary>A chargeback was opened: marks the payment Disputed and alerts the host and admins.</summary>
    public async Task<ApplyResult> MarkDisputedAsync(string reference, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var tripId = await TripIdForAsync(reference, ct);
        if (tripId is null) return new ApplyResult(ApplyOutcome.UnknownReference, reference, null);

        SeatPayment payment;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            payment = await LockPaymentAsync(tripId.Value, reference, ct);
            if (payment.Status == SeatPaymentStatus.Disputed)
            {
                await tx.CommitAsync(ct);
                return new ApplyResult(ApplyOutcome.AlreadyApplied, reference, payment.Status);
            }
            if (payment.Status is not (SeatPaymentStatus.Success or SeatPaymentStatus.PartiallyRefunded))
            {
                await tx.CommitAsync(ct);
                _logger.LogWarning("Dispute for {Reference} ignored: payment is {Status}", reference, payment.Status);
                return new ApplyResult(ApplyOutcome.NotPaid, reference, payment.Status);
            }

            // Seat money still counts while disputed (the host keeps it until
            // Paystack resolves the dispute); the status flags it on the manifest.
            payment.Status = SeatPaymentStatus.Disputed;
            payment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var trip = await _db.TripPosts.AsNoTracking().FirstAsync(t => t.Id == payment.TripPostId, ct);
        var title = await TripTitleAsync(trip.Id, ct);
        var lines = new List<string>
        {
            $"A traveller disputed payment {payment.Reference} ({Naira(payment.TotalChargedKobo)}) on \"{title}\".",
            "Paystack will contact the business to respond. Keep the traveller's accepted terms and your booking records ready.",
        };

        var hostEmailed = false;
        var hostEmail = await GetUserEmailAsync(trip.AuthorUserId, ct);
        if (!string.IsNullOrWhiteSpace(hostEmail))
            hostEmailed = await TrySendAsync(hostEmail, "A payment on your trip was disputed", lines, $"{FrontendBaseUrl()}/host/trips/{trip.Id}");
        await EmailAdminsAsync("Payment dispute opened", lines, ct);
        await BestEffortAsync(() => _notifications.NotifyDisputeOpened(trip, payment.PayerUserId, hostEmailed), "dispute notification");
        await PushManifestUpdateAsync(payment.TripPostId, payment.TripInterestId);

        return new ApplyResult(ApplyOutcome.Applied, reference, payment.Status);
    }

    /// <summary>
    /// A dispute was resolved. Resolution "declined" (the claim was rejected, the
    /// merchant keeps the money) puts the payment back to its paid status. Any
    /// other resolution leaves it Disputed for an admin to reconcile: the money
    /// movement arrives (or is checked) separately.
    /// </summary>
    public async Task<ApplyResult> ResolveDisputeAsync(string reference, string? resolution, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var tripId = await TripIdForAsync(reference, ct);
        if (tripId is null) return new ApplyResult(ApplyOutcome.UnknownReference, reference, null);

        SeatPayment payment;
        var restored = false;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            payment = await LockPaymentAsync(tripId.Value, reference, ct);
            if (payment.Status == SeatPaymentStatus.Disputed
                && string.Equals(resolution, "declined", StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = payment.RefundedKobo > 0 ? SeatPaymentStatus.PartiallyRefunded : SeatPaymentStatus.Success;
                payment.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                restored = true;
            }
            await tx.CommitAsync(ct);
        }

        await EmailAdminsAsync(
            "Payment dispute resolved",
            new List<string>
            {
                $"Paystack resolved the dispute on payment {reference} (resolution: {resolution ?? "unknown"}).",
                restored
                    ? "The payment is back to paid."
                    : "The payment is still marked Disputed: check the outcome in the Paystack dashboard and record any refund.",
            },
            ct);
        if (restored) await PushManifestUpdateAsync(payment.TripPostId, payment.TripInterestId);
        return new ApplyResult(restored ? ApplyOutcome.Applied : ApplyOutcome.AlreadyApplied, reference, payment.Status);
    }

    // ── Manual payments (host-recorded, outside Hodrac) ──────────────────────

    /// <summary>
    /// Records money the host received outside Hodrac (Transfer/Cash/Other,
    /// amount &gt; 0) or a refund the host paid the traveller directly (Refund,
    /// amount &lt; 0, at most what the seat has been paid), then recomputes the
    /// interest. The caller has checked the trip is the host's. Rules:
    ///  - the interest must be Approved or Withdrawn (Requested has no seat yet);
    ///  - a payment can't take the seat past its price (seat money only);
    ///  - an Approved traveller whose seat now has money joins the trip chat and
    ///    the seat hold ends, as with a first Paystack payment (hosts taking a
    ///    transfer still need the traveller in the chat). Refunds never remove
    ///    anyone from the chat: the host does that through the trip.
    /// Lock order as everywhere: trip (FOR NO KEY UPDATE), then the interest.
    /// </summary>
    public async Task<ManualPaymentResult> RecordManualPaymentAsync(
        Guid tripId, Guid interestId, long amountKobo, ManualPaymentMethod method, string? note, Guid recordedByUserId,
        CancellationToken ct = default)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > ManualPayment.MaxNoteLength })
            return new ManualPaymentResult(ManualPaymentOutcome.Invalid, $"Note must be {ManualPayment.MaxNoteLength} characters or fewer.");
        if (method == ManualPaymentMethod.Refund ? amountKobo >= 0 : amountKobo <= 0)
            return new ManualPaymentResult(ManualPaymentOutcome.Invalid, method == ManualPaymentMethod.Refund
                ? "A refund is recorded as a negative amount."
                : "Amount must be more than zero.");

        _db.ChangeTracker.Clear();
        ManualPayment payment;
        TripInterest interest;
        TripPost trip;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            var locked = await LockInterestAsync(tripId, interestId, ct);
            if (locked is null) return new ManualPaymentResult(ManualPaymentOutcome.NotFound);
            (trip, interest) = locked.Value;

            if (interest.Status is not (TripInterestStatus.Approved or TripInterestStatus.Withdrawn or TripInterestStatus.Removed))
                return new ManualPaymentResult(ManualPaymentOutcome.Conflict,
                    "Payments can only be recorded for approved (or withdrawn or removed) travellers.");

            if (method == ManualPaymentMethod.Refund)
            {
                if (-amountKobo > interest.AmountPaidKobo)
                    return new ManualPaymentResult(ManualPaymentOutcome.Invalid,
                        $"You can refund at most {Naira(interest.AmountPaidKobo)}, what this traveller has paid.");
            }
            else
            {
                var price = trip.PricePerSeatKobo ?? 0;
                if (price > 0 && interest.AmountPaidKobo + amountKobo > price)
                    return new ManualPaymentResult(ManualPaymentOutcome.Invalid,
                        $"That's more than this traveller owes ({Naira(Math.Max(0, price - interest.AmountPaidKobo))}).");
            }

            payment = new ManualPayment
            {
                TripInterestId = interest.Id,
                AmountKobo = amountKobo,
                Method = method,
                Note = note,
                RecordedByUserId = recordedByUserId,
                RecordedAt = DateTime.UtcNow,
            };
            _db.ManualPayments.Add(payment);
            await _db.SaveChangesAsync(ct);

            await RecomputeInterestAsync(interest, trip, ct);
            if (interest.Status == TripInterestStatus.Approved && interest.AmountPaidKobo > 0)
            {
                await _threads.StageAddToThreadAsync(trip, interest.RequesterUserId);
                interest.SeatHoldExpiresAt = null;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation(
            "User {UserId} recorded manual payment {PaymentId} ({Method}, {AmountKobo} kobo) on interest {InterestId} of trip {TripId}",
            recordedByUserId, payment.Id, method, amountKobo, interest.Id, tripId);
        await AfterManualChangeAsync(trip, interest, ct);
        return new ManualPaymentResult(ManualPaymentOutcome.Done, Payment: payment, Interest: interest);
    }

    /// <summary>
    /// Deletes a host-recorded payment (a mistake) and recomputes the interest.
    /// Refused when it would leave the seat's money below zero (delete the
    /// refund first). Chat membership is unchanged. Logged for audit.
    /// </summary>
    public async Task<ManualPaymentResult> DeleteManualPaymentAsync(
        Guid tripId, Guid manualPaymentId, Guid deletedByUserId, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var interestId = await _db.ManualPayments.AsNoTracking()
            .Where(m => m.Id == manualPaymentId && m.TripInterest!.TripPostId == tripId)
            .Select(m => (Guid?)m.TripInterestId)
            .FirstOrDefaultAsync(ct);
        if (interestId is null) return new ManualPaymentResult(ManualPaymentOutcome.NotFound);

        ManualPayment payment;
        TripInterest interest;
        TripPost trip;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            var locked = await LockInterestAsync(tripId, interestId.Value, ct);
            if (locked is null) return new ManualPaymentResult(ManualPaymentOutcome.NotFound);
            (trip, interest) = locked.Value;

            var found = await _db.ManualPayments.FirstOrDefaultAsync(m => m.Id == manualPaymentId && m.TripInterestId == interest.Id, ct);
            if (found is null) return new ManualPaymentResult(ManualPaymentOutcome.NotFound);
            payment = found;

            var manualSum = await _db.ManualPayments.Where(m => m.TripInterestId == interest.Id).SumAsync(m => m.AmountKobo, ct);
            if (interest.AmountPaidViaHodracKobo + manualSum - payment.AmountKobo < 0)
                return new ManualPaymentResult(ManualPaymentOutcome.Conflict,
                    "Deleting this payment would leave a refund larger than what was paid. Delete the refund first.");

            _db.ManualPayments.Remove(payment);
            await _db.SaveChangesAsync(ct);
            await RecomputeInterestAsync(interest, trip, ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogWarning(
            "User {UserId} deleted manual payment {PaymentId} ({Method}, {AmountKobo} kobo, recorded {RecordedAt:o} by {RecordedBy}) on interest {InterestId} of trip {TripId}",
            deletedByUserId, payment.Id, payment.Method, payment.AmountKobo, payment.RecordedAt, payment.RecordedByUserId, interest.Id, tripId);
        await AfterManualChangeAsync(trip, interest, ct);
        return new ManualPaymentResult(ManualPaymentOutcome.Done, Payment: payment, Interest: interest);
    }

    // Lock order: trip (FOR NO KEY UPDATE), then the interest (FOR NO KEY UPDATE).
    // Null when the interest isn't on the trip or was declined.
    private async Task<(TripPost Trip, TripInterest Interest)?> LockInterestAsync(Guid tripId, Guid interestId, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {tripId} FOR NO KEY UPDATE", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TripInterests\" WHERE \"Id\" = {interestId} FOR NO KEY UPDATE", ct);

        var trip = await _db.TripPosts.FirstOrDefaultAsync(t => t.Id == tripId, ct);
        var interest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId && i.TripPostId == tripId, ct);
        if (trip is null || interest is null || interest.Status == TripInterestStatus.Declined) return null;
        return (trip, interest);
    }

    private async Task AfterManualChangeAsync(TripPost trip, TripInterest interest, CancellationToken ct)
    {
        await PushManifestUpdateAsync(trip.Id, interest.Id);
        await PushInterestAsync(trip, interest, ct);
    }

    // ── Recompute ────────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes the interest's paid totals and payment status from its seat
    /// payments and manual payments. Saved payment changes must already be
    /// flushed (SaveChanges) in the current transaction; the caller holds the
    /// trip/interest locks and saves the interest.
    /// AmountPaidKobo = Σ credited seat money (less refunds) + Σ manual payments.
    /// AmountPaidViaHodracKobo = Σ credited seat money (less refunds).
    /// </summary>
    public async Task RecomputeInterestAsync(TripInterest interest, TripPost trip, CancellationToken ct = default)
    {
        var payments = await _db.SeatPayments.AsNoTracking()
            .Where(p => p.TripInterestId == interest.Id && SucceededStatuses.Contains(p.Status))
            .Select(p => new { p.Status, p.SeatAmountKobo, p.RefundedKobo })
            .ToListAsync(ct);

        var manual = await _db.ManualPayments.AsNoTracking()
            .Where(m => m.TripInterestId == interest.Id)
            .Select(m => new { m.AmountKobo, m.Method })
            .ToListAsync(ct);

        var viaHodrac = payments
            .Where(p => CreditedStatuses.Contains(p.Status))
            .Sum(p => Math.Max(0, p.SeatAmountKobo - p.RefundedKobo));
        var total = Math.Max(0, viaHodrac + manual.Sum(m => m.AmountKobo));

        var hadRefund = payments.Any(p => p.Status is SeatPaymentStatus.Refunded or SeatPaymentStatus.PartiallyRefunded)
            || manual.Any(m => m.Method == ManualPaymentMethod.Refund);

        interest.AmountPaidViaHodracKobo = viaHodrac;
        interest.AmountPaidKobo = total;
        if (trip.IsHostedTrip)
            interest.PaymentStatus = PaymentSchedule.ComputeStatus(
                total, trip.PricePerSeatKobo ?? 0, trip.DepositKobo ?? 0, interest.PaymentStatus, hadRefund);
    }

    // ── Receipts ─────────────────────────────────────────────────────────────

    /// <summary>Receipts for the given payments (succeeded ones only), newest first.</summary>
    public async Task<List<PaymentReceiptDto>> BuildReceiptsAsync(IQueryable<SeatPayment> payments, CancellationToken ct = default)
    {
        var rows = await payments
            .AsNoTracking()
            .Where(p => SucceededStatuses.Contains(p.Status))
            .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
            .Select(p => new
            {
                Payment = p,
                Trip = new
                {
                    p.TripPost!.Id,
                    p.TripPost.AuthorUserId,
                    p.TripPost.StartDate,
                    p.TripPost.EndDate,
                    p.TripPost.Caption,
                    p.TripPost.FreeTextDestination,
                    DestinationName = p.TripPost.Destination != null ? p.TripPost.Destination.DestinationName : null,
                    WishlistName = p.TripPost.Wishlist != null ? p.TripPost.Wishlist.WishlistName : null,
                    HostName = p.TripPost.HostProfile != null ? p.TripPost.HostProfile.DisplayName : null,
                },
                TermsVersionAccepted = p.TripInterest!.TermsVersionAccepted,
            })
            .ToListAsync(ct);

        // Fall back to the author's display name when the trip has no host profile.
        var authorIds = rows.Where(r => r.Trip.HostName is null).Select(r => r.Trip.AuthorUserId.ToString()).Distinct().ToList();
        var authorNames = authorIds.Count == 0
            ? new Dictionary<string, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return rows.Select(r => new PaymentReceiptDto(
            Reference: r.Payment.Reference,
            Status: r.Payment.Status.ToString(),
            TripId: r.Trip.Id,
            TripTitle: TripTitle(r.Trip.DestinationName, r.Trip.WishlistName, r.Trip.FreeTextDestination, r.Trip.Caption),
            StartDate: r.Trip.StartDate,
            EndDate: r.Trip.EndDate,
            HostDisplayName: r.Trip.HostName ?? authorNames.GetValueOrDefault(r.Trip.AuthorUserId.ToString()) ?? "Host",
            Kind: r.Payment.Kind.ToString(),
            InstalmentSequence: r.Payment.InstalmentSequence,
            SeatAmountKobo: r.Payment.SeatAmountKobo,
            FeeKobo: r.Payment.TravellerFeeKobo,
            TotalKobo: r.Payment.TotalChargedKobo,
            RefundedKobo: r.Payment.RefundedKobo,
            Currency: r.Payment.Currency,
            TermsVersionAccepted: r.TermsVersionAccepted,
            PaidAt: r.Payment.PaidAt,
            Channel: r.Payment.Channel)).ToList();
    }

    public static string TripTitle(string? destinationName, string? wishlistName, string? freeText, string? caption) =>
        !string.IsNullOrWhiteSpace(destinationName) ? destinationName!
        : !string.IsNullOrWhiteSpace(wishlistName) ? wishlistName!
        : !string.IsNullOrWhiteSpace(freeText) ? freeText!
        : !string.IsNullOrWhiteSpace(caption) ? caption!
        : "Trip";

    // ── After a payment succeeds (post-commit, best-effort) ──────────────────

    private async Task AfterPaymentSucceededAsync(SeatPayment payment, TripInterest interest, CancellationToken ct)
    {
        var trip = await _db.TripPosts.AsNoTracking().FirstAsync(t => t.Id == payment.TripPostId, ct);

        var receiptEmailed = false;
        var receipt = (await BuildReceiptsAsync(_db.SeatPayments.Where(p => p.Id == payment.Id), ct)).FirstOrDefault();
        var payerEmail = await GetUserEmailAsync(payment.PayerUserId, ct);
        if (receipt is not null && !string.IsNullOrWhiteSpace(payerEmail))
        {
            var lines = new List<string>
            {
                $"Payment received for {receipt.TripTitle}{DateRange(receipt.StartDate, receipt.EndDate)}, hosted by {receipt.HostDisplayName}.",
                $"{KindLabel(receipt.Kind, receipt.InstalmentSequence)}: {Naira(receipt.SeatAmountKobo)}",
            };
            if (receipt.FeeKobo > 0) lines.Add($"Booking fee: {Naira(receipt.FeeKobo)}");
            lines.Add($"Total paid: {Naira(receipt.TotalKobo)}");
            lines.Add($"Reference: {receipt.Reference}");
            if (receipt.TermsVersionAccepted is not null)
                lines.Add($"You accepted version {receipt.TermsVersionAccepted} of the host's terms before paying.");
            lines.Add("Your host's share went straight to the host's own bank account through Paystack.");

            receiptEmailed = await TrySendAsync(payerEmail, "Your Hodrac payment receipt", lines, $"{FrontendBaseUrl()}/payments/{receipt.Reference}");
        }

        await BestEffortAsync(() => _notifications.NotifyPaymentReceived(trip, payment.PayerUserId), "payment-received notification");
        await BestEffortAsync(() => _notifications.NotifyPaymentConfirmed(trip, payment.PayerUserId, receiptEmailed), "payment-confirmed notification");
        await PushManifestUpdateAsync(trip.Id, interest.Id);
        await PushInterestAsync(trip, interest, ct);
    }

    // Live-updates both sides of the interest (same events as TripsController). Best-effort.
    public Task PushInterestAsync(TripPost trip, TripInterest interest, CancellationToken ct) =>
        BestEffortAsync(async () =>
        {
            var dto = new TripInterestDto
            {
                Id = interest.Id,
                TripPostId = interest.TripPostId,
                RequesterUserId = interest.RequesterUserId,
                Message = interest.Message,
                Status = interest.Status.ToString(),
                PaymentStatus = interest.PaymentStatus.ToString(),
                SeatHoldExpiresAt = interest.SeatHoldExpiresAt
            };
            await _tripPostHub.Clients.Group($"user:{interest.RequesterUserId}").SendAsync("TripInterestStatusChanged", dto, ct);
            var interestList = await _tripsRepo.GetAllInterests(trip.Id);
            await _tripPostHub.Clients.Group($"user:{trip.AuthorUserId}").SendAsync("TripInterestUpdated", interestList, ct);
        }, "interest push");

    /// <summary>
    /// Live manifest: tells the host's open manifest (SignalR group
    /// trip-host:{tripId}) that a row changed. Best-effort.
    /// </summary>
    public Task PushManifestUpdateAsync(Guid tripId, Guid interestId) =>
        BestEffortAsync(
            () => _tripPostHub.Clients.Group(TripPostHub.TripHostGroup(tripId))
                .SendAsync("ManifestUpdated", new { tripId, interestId }),
            "manifest push");

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<Guid?> TripIdForAsync(string reference, CancellationToken ct) =>
        _db.SeatPayments.AsNoTracking()
            .Where(p => p.Reference == reference)
            .Select(p => (Guid?)p.TripPostId)
            .FirstOrDefaultAsync(ct);

    // Lock order: trip (FOR NO KEY UPDATE), then the payment (FOR UPDATE).
    // The change tracker was cleared, so the query after the lock reads the
    // committed row.
    private async Task<SeatPayment> LockPaymentAsync(Guid tripId, string reference, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {tripId} FOR NO KEY UPDATE", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SeatPayments\" WHERE \"Reference\" = {reference} FOR UPDATE", ct);
        return await _db.SeatPayments.FirstAsync(p => p.Reference == reference, ct);
    }

    private async Task<string> TripTitleAsync(Guid tripId, CancellationToken ct)
    {
        var t = await _db.TripPosts.AsNoTracking()
            .Where(x => x.Id == tripId)
            .Select(x => new
            {
                x.Caption,
                x.FreeTextDestination,
                DestinationName = x.Destination != null ? x.Destination.DestinationName : null,
                WishlistName = x.Wishlist != null ? x.Wishlist.WishlistName : null,
            })
            .FirstAsync(ct);
        return TripTitle(t.DestinationName, t.WishlistName, t.FreeTextDestination, t.Caption);
    }

    private Task<string?> GetUserEmailAsync(Guid userId, CancellationToken ct)
    {
        var id = userId.ToString();
        return _db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(ct);
    }

    /// <summary>Emails every address in Admin:Emails. Best-effort.</summary>
    public async Task EmailAdminsAsync(string subject, List<string> lines, CancellationToken ct = default)
    {
        var admins = (_config["Admin:Emails"] ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var admin in admins)
            await TrySendAsync(admin, subject, lines, $"{FrontendBaseUrl()}/admin/payments");
    }

    private async Task<bool> TrySendAsync(string to, string subject, List<string> lines, string link)
    {
        try
        {
            await _email.SendAsync(to, subject, lines, link);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not send payment email \"{Subject}\"", subject);
            return false;
        }
    }

    private async Task BestEffortAsync(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Payment side effect failed: {What}", what);
        }
    }

    private string FrontendBaseUrl() => (_config["Frontend:BaseUrl"] ?? "https://www.hodrac.com").TrimEnd('/');

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    public static string Naira(long kobo) =>
        "₦" + (kobo / 100m).ToString(kobo % 100 == 0 ? "N0" : "N2", System.Globalization.CultureInfo.InvariantCulture);

    private static string KindLabel(string kind, int? sequence) => kind switch
    {
        nameof(SeatPaymentKind.Deposit) => "Deposit",
        nameof(SeatPaymentKind.Instalment) => $"Instalment {sequence}",
        _ => "Full payment"
    };

    private static string DateRange(DateOnly? start, DateOnly? end) =>
        start is null ? "" : end is null || end == start ? $" ({start:d MMM yyyy})" : $" ({start:d MMM} – {end:d MMM yyyy})";
}
