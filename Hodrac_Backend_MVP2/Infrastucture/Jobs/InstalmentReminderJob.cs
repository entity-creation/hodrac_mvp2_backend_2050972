using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastructure.Jobs;

/// <summary>
/// Daily at 08:00 WAT (07:00 UTC). Hosted trips only. For each approved
/// traveller who still owes seat money, finds the next unpaid schedule line
/// (deposit: due on the seat-hold expiry day; instalments: their DueDate, WAT
/// dates) and, when it is due in 3 days, in 1 day, today, or was due exactly
/// 2 days ago, emails a "Pay now" reminder and adds an in-app InstalmentDue
/// notification.
///
/// Idempotent: the PaymentReminderLog row (unique per interest, instalment and
/// kind) is inserted BEFORE sending; a unique violation means another run
/// already sent it. A failed send is not retried (at most once). Per-interest
/// failures are logged and skipped.
/// </summary>
public class InstalmentReminderJob
{
    private static readonly InterestPaymentStatus[] OwingStatuses =
    {
        InterestPaymentStatus.AwaitingDeposit,
        InterestPaymentStatus.DepositPaid,
        InterestPaymentStatus.PartPaid
    };

    private readonly HodracDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IEmailSender _email;
    private readonly HostTripService _trips;
    private readonly IConfiguration _config;
    private readonly ILogger<InstalmentReminderJob> _logger;

    public InstalmentReminderJob(
        HodracDbContext db,
        INotificationService notifications,
        IEmailSender email,
        HostTripService trips,
        IConfiguration config,
        ILogger<InstalmentReminderJob> logger)
    {
        _db = db;
        _notifications = notifications;
        _email = email;
        _trips = trips;
        _config = config;
        _logger = logger;
    }

    /// <summary>The reminder for a line due <paramref name="daysUntilDue"/> days from today (WAT), if any.</summary>
    public static PaymentReminderKind? KindFor(int daysUntilDue) => daysUntilDue switch
    {
        3 => PaymentReminderKind.DueIn3Days,
        1 => PaymentReminderKind.DueIn1Day,
        0 => PaymentReminderKind.DueToday,
        -2 => PaymentReminderKind.Overdue2Days,
        _ => null
    };

    public Task<int> RunAsync(CancellationToken ct) => RunAsync(DateTime.UtcNow, ct);

    /// <summary>Sends today's reminders; returns how many were sent.</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var today = HostTripService.TodayWat(nowUtc);

        var interests = await _db.TripInterests.AsNoTracking()
            .Where(i => i.Status == TripInterestStatus.Approved
                && OwingStatuses.Contains(i.PaymentStatus)
                && i.TripPost!.IsHostedTrip
                && i.TripPost.Status != TripPostStatus.Cancelled
                && (i.TripPost.StartDate == null || i.TripPost.StartDate >= today))
            .ToListAsync(ct);
        if (interests.Count == 0) return 0;

        var tripIds = interests.Select(i => i.TripPostId).Distinct().ToList();
        var trips = await _db.TripPosts.AsNoTracking()
            .Where(t => tripIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
        var titles = await _trips.TripTitlesAsync(tripIds, ct);

        var sent = 0;
        foreach (var interest in interests)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await RemindAsync(trips[interest.TripPostId], interest, titles.GetValueOrDefault(interest.TripPostId) ?? "Trip", today, nowUtc, ct))
                    sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Instalment reminder failed for interest {InterestId}", interest.Id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation("Instalment reminders: {Sent} sent ({Candidates} interests checked)", sent, interests.Count);
        return sent;
    }

    private async Task<bool> RemindAsync(TripPost trip, TripInterest interest, string title, DateOnly today, DateTime nowUtc, CancellationToken ct)
    {
        var owes = HostTripService.Owes(trip, interest);
        if (owes <= 0) return false;

        var line = PaymentSchedule.Allocate(trip, interest.AmountPaidKobo).FirstOrDefault(l => !l.IsPaid);
        if (line is null) return false;

        DateOnly? due = line.DueDate;
        if (line.Kind == SeatPaymentKind.Deposit)
        {
            // The deposit is due when the seat hold ends; once it has ended the
            // seat-hold job expires the seat, so there is nothing to remind.
            if (interest.SeatHoldExpiresAt is not { } holdEnds || holdEnds <= nowUtc) return false;
            due = HostTripService.TodayWat(holdEnds);
        }
        if (due is null) return false;

        var kind = KindFor(due.Value.DayNumber - today.DayNumber);
        if (kind is null) return false;

        // Claim the reminder first: a concurrent or repeated run hits the unique index.
        _db.PaymentReminderLogs.Add(new PaymentReminderLog
        {
            TripInterestId = interest.Id,
            InstalmentSequence = line.Sequence,
            Kind = kind.Value,
            Channel = PaymentReminderLog.EmailChannel,
            SentAt = nowUtc
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (HostedJobSupport.IsUniqueViolation(ex))
        {
            return false; // already sent
        }
        _db.ChangeTracker.Clear();

        var requesterId = interest.RequesterUserId.ToString();
        var to = await _db.Users.AsNoTracking()
            .Where(u => u.Id == requesterId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        var emailed = false;
        if (!string.IsNullOrWhiteSpace(to))
        {
            var (subject, lines) = BuildEmail(kind.Value, line, due.Value, owes, trip, interest, title);
            emailed = await HostedJobSupport.TrySendAsync(_email, _logger, to, subject, lines, HostedJobSupport.PayLink(_config, trip.Id));
        }

        await HostedJobSupport.BestEffortAsync(_logger,
            () => _notifications.NotifyInstalmentDue(trip, interest.RequesterUserId, emailed),
            "instalment-due notification");
        return true;
    }

    private static (string Subject, List<string> Lines) BuildEmail(
        PaymentReminderKind kind, ScheduleLine line, DateOnly due, long owes, TripPost trip, TripInterest interest, string title)
    {
        var label = line.Kind == SeatPaymentKind.Deposit ? "Your deposit" : $"Instalment {line.Sequence}";
        var when = kind switch
        {
            PaymentReminderKind.DueIn3Days => "is due in 3 days",
            PaymentReminderKind.DueIn1Day => "is due tomorrow",
            PaymentReminderKind.DueToday => "is due today",
            _ => "is overdue"
        };

        var lines = new List<string>
        {
            kind == PaymentReminderKind.Overdue2Days
                ? $"{label} of {PaymentService.Naira(line.RemainingKobo)} for {title}{HostedJobSupport.DateRange(trip.StartDate, trip.EndDate)} was due on {HostedJobSupport.Day(due)}."
                : $"{label} of {PaymentService.Naira(line.RemainingKobo)} for {title}{HostedJobSupport.DateRange(trip.StartDate, trip.EndDate)} is due on {HostedJobSupport.Day(due)}."
        };
        if (line.Kind == SeatPaymentKind.Deposit && interest.SeatHoldExpiresAt is { } holdEnds)
            lines.Add($"Your seat is held until {HostedJobSupport.WatTime(holdEnds)}.");
        if (owes > line.RemainingKobo)
            lines.Add($"Total left to pay for your seat: {PaymentService.Naira(owes)}.");
        lines.Add("Pay now from your trip page.");
        lines.Add("Already paid the host another way? Ask them to record it on Hodrac, and you can ignore this email.");

        return ($"{label} for {title} {when}", lines);
    }
}
