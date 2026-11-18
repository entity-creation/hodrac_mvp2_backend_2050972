using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastructure.Jobs;

/// <summary>
/// Hourly. Hosted trips only.
///  1. Warning: an AwaitingDeposit seat hold ending within 12 hours gets one
///     SeatHoldExpiring email + notification (PaymentReminderLog kind
///     SeatHoldExpiring, no instalment; inserted first, so never twice).
///  2. Expiry: an AwaitingDeposit seat hold past SeatHoldExpiresAt becomes
///     PaymentStatus Expired (the interest stays Approved; capacity and
///     checkout already ignore Expired seats). Locks the trip then the
///     interest (the same order as PaymentService) and re-checks inside the
///     lock, so a payment that landed just before is never overwritten. Then
///     notifies traveller and host and pushes the manifest/interest updates.
/// Per-interest failures are logged and skipped; safe to run twice.
/// </summary>
public class SeatHoldExpiryJob
{
    public static readonly TimeSpan WarningWindow = TimeSpan.FromHours(12);

    private readonly HodracDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IEmailSender _email;
    private readonly PaymentService _payments;
    private readonly HostTripService _trips;
    private readonly IConfiguration _config;
    private readonly ILogger<SeatHoldExpiryJob> _logger;

    public SeatHoldExpiryJob(
        HodracDbContext db,
        INotificationService notifications,
        IEmailSender email,
        PaymentService payments,
        HostTripService trips,
        IConfiguration config,
        ILogger<SeatHoldExpiryJob> logger)
    {
        _db = db;
        _notifications = notifications;
        _email = email;
        _payments = payments;
        _trips = trips;
        _config = config;
        _logger = logger;
    }

    public record RunResult(int Warned, int Expired);

    public Task<RunResult> RunAsync(CancellationToken ct) => RunAsync(DateTime.UtcNow, ct);

    public async Task<RunResult> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var expired = await ExpireAsync(nowUtc, ct);
        var warned = await WarnAsync(nowUtc, ct);
        _logger.LogInformation("Seat holds: {Expired} expired, {Warned} warned", expired, warned);
        return new RunResult(warned, expired);
    }

    private IQueryable<TripInterest> HeldSeats() =>
        _db.TripInterests.AsNoTracking()
            .Where(i => i.PaymentStatus == InterestPaymentStatus.AwaitingDeposit
                && i.Status == TripInterestStatus.Approved
                && i.SeatHoldExpiresAt != null
                && i.TripPost!.IsHostedTrip);

    // ── Expiry ───────────────────────────────────────────────────────────────

    private async Task<int> ExpireAsync(DateTime nowUtc, CancellationToken ct)
    {
        var due = await HeldSeats()
            .Where(i => i.SeatHoldExpiresAt <= nowUtc)
            .OrderBy(i => i.SeatHoldExpiresAt)
            .Select(i => new { i.Id, i.TripPostId })
            .ToListAsync(ct);

        var count = 0;
        foreach (var item in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // A deposit paid on Paystack but not recorded yet (missed
                // webhook) is credited first, so a paid seat is never expired.
                await _payments.SettleOpenCheckoutsAsync(item.Id, ct);
                if (await TryExpireAsync(item.TripPostId, item.Id, nowUtc, ct)) count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Seat hold expiry failed for interest {InterestId}", item.Id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
        return count;
    }

    /// <summary>
    /// Expires one seat hold if, inside the trip → interest locks, it is still
    /// an unpaid AwaitingDeposit hold past its expiry. False when anything
    /// changed since the candidate list was read (e.g. the deposit landed).
    /// </summary>
    public async Task<bool> TryExpireAsync(Guid tripId, Guid interestId, DateTime nowUtc, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        TripPost trip;
        TripInterest interest;

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // Lock order: trip, then interest (as PaymentService). ApplyAsync
            // holds the trip lock while it credits, so after these locks the
            // interest row below reflects any payment that just landed.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {tripId} FOR NO KEY UPDATE", ct);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TripInterests\" WHERE \"Id\" = {interestId} FOR NO KEY UPDATE", ct);

            trip = await _db.TripPosts.FirstAsync(t => t.Id == tripId, ct);
            interest = await _db.TripInterests.FirstAsync(i => i.Id == interestId, ct);

            if (!trip.IsHostedTrip
                || interest.Status != TripInterestStatus.Approved
                || interest.PaymentStatus != InterestPaymentStatus.AwaitingDeposit
                || interest.AmountPaidKobo > 0
                || interest.SeatHoldExpiresAt is not { } holdEnds
                || holdEnds > nowUtc)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            // SeatHoldExpiresAt is kept: it records when the hold ended.
            interest.PaymentStatus = InterestPaymentStatus.Expired;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation("Seat hold expired for interest {InterestId} on trip {TripId}", interestId, tripId);

        var emailed = false;
        var to = await TravellerEmailAsync(interest.RequesterUserId, ct);
        if (!string.IsNullOrWhiteSpace(to))
        {
            var title = await _trips.TripTitleAsync(tripId, ct);
            emailed = await HostedJobSupport.TrySendAsync(_email, _logger, to,
                $"Your seat hold on {title} has expired",
                new List<string>
                {
                    $"The deposit for {title}{HostedJobSupport.DateRange(trip.StartDate, trip.EndDate)} wasn't paid before your seat hold ended, so the seat has been released.",
                    "If you still want to go, ask the host to approve you again (they can do it from their trip page), or send a new request with \"Request again\" on the trip page.",
                },
                HostedJobSupport.PayLink(_config, tripId));
        }

        _db.ChangeTracker.Clear();
        await HostedJobSupport.BestEffortAsync(_logger,
            () => _notifications.NotifySeatHoldExpired(trip, interest.RequesterUserId, emailed),
            "seat-hold-expired notification");
        await _payments.PushManifestUpdateAsync(tripId, interestId);
        await _payments.PushInterestAsync(trip, interest, ct);
        return true;
    }

    // ── 12-hour warning ──────────────────────────────────────────────────────

    private async Task<int> WarnAsync(DateTime nowUtc, CancellationToken ct)
    {
        var warnBefore = nowUtc + WarningWindow;
        var soon = await HeldSeats()
            .Where(i => i.SeatHoldExpiresAt > nowUtc && i.SeatHoldExpiresAt <= warnBefore
                && i.TripPost!.Status != TripPostStatus.Cancelled
                && !_db.PaymentReminderLogs.Any(r => r.TripInterestId == i.Id && r.Kind == PaymentReminderKind.SeatHoldExpiring))
            .ToListAsync(ct);

        var count = 0;
        foreach (var interest in soon)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await WarnOneAsync(interest, nowUtc, ct)) count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Seat hold warning failed for interest {InterestId}", interest.Id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
        return count;
    }

    private async Task<bool> WarnOneAsync(TripInterest interest, DateTime nowUtc, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        _db.PaymentReminderLogs.Add(new PaymentReminderLog
        {
            TripInterestId = interest.Id,
            InstalmentSequence = null,
            Kind = PaymentReminderKind.SeatHoldExpiring,
            Channel = PaymentReminderLog.EmailChannel,
            SentAt = nowUtc
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (HostedJobSupport.IsUniqueViolation(ex))
        {
            return false; // another run already warned
        }
        _db.ChangeTracker.Clear();

        var trip = await _db.TripPosts.AsNoTracking().FirstAsync(t => t.Id == interest.TripPostId, ct);
        var title = await _trips.TripTitleAsync(trip.Id, ct);

        var emailed = false;
        var to = await TravellerEmailAsync(interest.RequesterUserId, ct);
        if (!string.IsNullOrWhiteSpace(to))
        {
            var deposit = PaymentSchedule.Allocate(trip, interest.AmountPaidKobo)
                .FirstOrDefault(l => l.Kind == SeatPaymentKind.Deposit)?.RemainingKobo ?? trip.DepositKobo ?? 0;
            var lines = new List<string>
            {
                $"Your seat on {title}{HostedJobSupport.DateRange(trip.StartDate, trip.EndDate)} is held until {HostedJobSupport.WatTime(interest.SeatHoldExpiresAt!.Value)}.",
                deposit > 0
                    ? $"Pay the {PaymentService.Naira(deposit)} deposit before then to keep your place. After that the seat is released."
                    : "Pay before then to keep your place. After that the seat is released.",
                "Pay now from your trip page.",
            };
            emailed = await HostedJobSupport.TrySendAsync(_email, _logger, to,
                $"Your seat hold on {title} ends soon", lines, HostedJobSupport.PayLink(_config, trip.Id));
        }

        await HostedJobSupport.BestEffortAsync(_logger,
            () => _notifications.NotifySeatHoldExpiring(trip, interest.RequesterUserId, emailed),
            "seat-hold-expiring notification");
        return true;
    }

    private Task<string?> TravellerEmailAsync(Guid userId, CancellationToken ct)
    {
        var id = userId.ToString();
        return _db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(ct);
    }
}
