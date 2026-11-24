using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.HostDtos;
using Hodrac_Backend_MVP2.Infrastructure.Jobs;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services.Hosts;

public enum RemoveTravellerOutcome
{
    Removed,
    /// <summary>Not the host's hosted trip, or the interest isn't on it: 404.</summary>
    NotFound,
    /// <summary>Reason too long: 400.</summary>
    Invalid,
    /// <summary>Not removable (not approved, nothing overdue): 409.</summary>
    Conflict
}

public record RemoveTravellerResult(RemoveTravellerOutcome Outcome, string? Error = null, RemoveTravellerResultDto? Removed = null);

/// <summary>
/// A host removes an approved traveller who has missed an instalment deadline
/// (<see cref="HostTripService.RemovableOverdueLines"/>). The seat is freed
/// (capacity counts Approved only), the traveller leaves the trip chat,
/// reminders stop (the job selects Approved only) and checkout is refused
/// (it needs Approved). Payments are kept as they are: nothing is refunded
/// automatically; the host records a refund through a manual Refund entry.
/// A payment that lands later is still credited and flagged by the
/// "payment needs attention" alert.
/// </summary>
public class TravellerRemovalService
{
    public const string NotRemovableMessage =
        "You can only remove a traveller once an instalment is past its due date and unpaid.";
    public const string NotApprovedMessage = "Only an approved traveller can be removed.";

    private readonly HodracDbContext _db;
    private readonly ITripThreadService _threads;
    private readonly INotificationService _notifications;
    private readonly IEmailSender _email;
    private readonly PaymentService _payments;
    private readonly HostTripService _hostTrips;
    private readonly IConfiguration _config;
    private readonly ILogger<TravellerRemovalService> _logger;

    public TravellerRemovalService(
        HodracDbContext db,
        ITripThreadService threads,
        INotificationService notifications,
        IEmailSender email,
        PaymentService payments,
        HostTripService hostTrips,
        IConfiguration config,
        ILogger<TravellerRemovalService> logger)
    {
        _db = db;
        _threads = threads;
        _notifications = notifications;
        _email = email;
        _payments = payments;
        _hostTrips = hostTrips;
        _config = config;
        _logger = logger;
    }

    /// <summary>The caller must already own the hosted trip (HostTripService.GetOwnedHostedTripAsync).</summary>
    public async Task<RemoveTravellerResult> RemoveAsync(
        Guid tripId, Guid interestId, string? reason, Guid hostUserId, CancellationToken ct = default)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is { Length: > TripInterest.MaxRemovedReasonLength })
            return new RemoveTravellerResult(RemoveTravellerOutcome.Invalid,
                $"Reason must be {TripInterest.MaxRemovedReasonLength} characters or fewer.");

        _db.ChangeTracker.Clear();
        TripPost trip;
        TripInterest interest;
        List<ScheduleLine> overdue;
        var now = DateTime.UtcNow;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // Lock order as everywhere: trip, then the interest. FOR NO KEY
            // UPDATE on the trip serializes with approvals/edits (FOR UPDATE),
            // payments (FOR NO KEY UPDATE) and checkouts/date changes (FOR SHARE).
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TripPosts\" WHERE \"Id\" = {tripId} FOR NO KEY UPDATE", ct);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TripInterests\" WHERE \"Id\" = {interestId} FOR NO KEY UPDATE", ct);

            var loadedTrip = await _db.TripPosts.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId, ct);
            var loadedInterest = await _db.TripInterests.FirstOrDefaultAsync(i => i.Id == interestId && i.TripPostId == tripId, ct);
            if (loadedTrip is null || !loadedTrip.IsHostedTrip || loadedInterest is null)
                return new RemoveTravellerResult(RemoveTravellerOutcome.NotFound);
            trip = loadedTrip;
            interest = loadedInterest;

            if (interest.Status != TripInterestStatus.Approved)
                return new RemoveTravellerResult(RemoveTravellerOutcome.Conflict, NotApprovedMessage);

            var dates = await TravellerInstalmentDates.LoadAsync(_db, interest.Id, ct);
            var lines = PaymentSchedule.Allocate(trip, interest.AmountPaidKobo, dates);
            overdue = HostTripService.RemovableOverdueLines(trip, interest, lines, HostTripService.TodayWat(now));
            if (overdue.Count == 0)
                return new RemoveTravellerResult(RemoveTravellerOutcome.Conflict, NotRemovableMessage);

            interest.Status = TripInterestStatus.Removed;
            interest.RespondedAt = now;
            interest.RemovedAt = now;
            interest.RemovedReason = reason;
            interest.SeatHoldExpiresAt = null;
            if (interest.RequesterUserId != trip.AuthorUserId)
                await _threads.StageRemoveFromThreadAsync(trip.Id, interest.RequesterUserId);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation(
            "Host {UserId} removed interest {InterestId} from trip {TripId} for missed instalment(s) {Sequences}",
            hostUserId, interest.Id, trip.Id, string.Join(",", overdue.Select(l => l.Sequence)));

        // Side effects after commit, best-effort.
        var emailed = await EmailTravellerAsync(trip, interest, overdue, reason, ct);
        await HostedJobSupport.BestEffortAsync(_logger,
            () => _notifications.NotifyRemovedForMissedPayment(trip, interest.RequesterUserId, emailed),
            "removed-for-missed-payment notification");
        await _payments.PushManifestUpdateAsync(trip.Id, interest.Id);
        await _payments.PushInterestAsync(trip, interest, ct);

        return new RemoveTravellerResult(RemoveTravellerOutcome.Removed, Removed: new RemoveTravellerResultDto(
            interest.Id,
            interest.Status.ToString(),
            interest.PaymentStatus.ToString(),
            interest.AmountPaidKobo,
            now,
            reason,
            overdue.Select(l => new OverdueInstalmentDto(l.Sequence ?? 0, l.DueDate!.Value, l.AmountKobo, l.RemainingKobo)).ToList()));
    }

    private async Task<bool> EmailTravellerAsync(
        TripPost trip, TripInterest interest, List<ScheduleLine> overdue, string? reason, CancellationToken ct)
    {
        try
        {
            var travellerId = interest.RequesterUserId.ToString();
            var to = await _db.Users.AsNoTracking()
                .Where(u => u.Id == travellerId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(to)) return false;

            var title = await _hostTrips.TripTitleAsync(trip.Id, ct);
            var hostName = await _db.HostProfiles.AsNoTracking()
                .Where(h => h.Id == trip.HostProfileId)
                .Select(h => h.DisplayName)
                .FirstOrDefaultAsync(ct) ?? "The host";

            var first = overdue[0];
            var lines = new List<string>
            {
                $"{hostName} has removed you from {title}{HostedJobSupport.DateRange(trip.StartDate, trip.EndDate)} because instalment {first.Sequence} of {PaymentService.Naira(first.RemainingKobo)} was due on {HostedJobSupport.Day(first.DueDate!.Value)} and hasn't been paid.",
            };
            if (overdue.Count > 1)
                lines.Add($"{overdue.Count} instalments were overdue in total ({PaymentService.Naira(overdue.Sum(l => l.RemainingKobo))}).");
            if (reason is not null)
                lines.Add($"Message from the host: {reason}");
            lines.Add($"What you've paid so far ({PaymentService.Naira(interest.AmountPaidKobo)}) stays on record. Any refund follows the host's terms; ask the host, who records refunds on Hodrac. The Hodrac booking fee isn't refundable.");
            lines.Add("You no longer have a seat or access to the trip chat.");

            return await HostedJobSupport.TrySendAsync(_email, _logger, to, $"You've been removed from {title}", lines,
                HostedJobSupport.PayLink(_config, trip.Id));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not email removed traveller for interest {InterestId}", interest.Id);
            return false;
        }
    }
}
