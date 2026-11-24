using System.Globalization;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.HostDtos;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services.Hosts;

/// <summary>The filters every dashboard endpoint shares (null = any).</summary>
public record HostDashboardFilter(
    Guid? TripId = null,
    TripPostStatus? Status = null,
    bool? Upcoming = null,            // true: starts today (WAT) or later, or undated; false: started before today
    Guid? DestinationId = null,       // the trip's destination or any stop of its wishlist
    Guid? CityId = null,              // ... through Destination.DestinationCities
    Guid? CountryId = null,           // ... through Destination.CountryId
    DateOnly? StartFrom = null,       // trip StartDate, inclusive (undated trips never match)
    DateOnly? StartTo = null,
    string? Search = null);           // trip title or location label, case-insensitive substring

/// <summary>Extra filters for the travellers list.</summary>
public record HostDashboardTravellerFilter(
    InterestPaymentStatus? PaymentStatus = null,
    TripInterestStatus? InterestStatus = null,
    bool? Owing = null,               // OwesKobo > 0 (or == 0 for false)
    bool? Overdue = null,             // OverdueKobo > 0 (or == 0 for false)
    string? Search = null);           // traveller name, email or phone, or trip title

/// <summary>
/// The host's dashboard across all their hosted trips: totals with a monthly
/// money chart, one row per trip, and one row per traveller (JSON and CSV).
///
/// Trips are the caller's hosted trips (<see cref="HostTripService.OwnedHostedTrips"/>).
/// A fixed number of queries (trips, their location names, interests,
/// succeeded seat payments, manual payments, traveller instalment dates, and
/// for the travellers list the users and last reminders), aggregated in memory
/// as <see cref="HostTripService.ListTripsAsync"/> does.
///
/// Money rules (kobo):
///  - Collected counts everything received, from the interests' stored
///    AmountPaidKobo / AmountPaidViaHodracKobo, on every interest (a withdrawn
///    or removed traveller's money stays until the host records a refund).
///  - Owed and overdue count only Approved, non-expired seats on trips that
///    aren't Cancelled. Overdue = unpaid instalment lines due before today
///    (WAT) on the traveller's schedule (<see cref="PaymentSchedule.OverdueKobo"/>,
///    with the traveller's own dates on TravellerChooses trips).
///  - Fees from succeeded SeatPayments: TravellerFeeKobo (the traveller paid
///    it on top), HodracFeeKobo − TravellerFeeKobo (absorbed by the host),
///    PaystackFeeKobo (borne by the host; null counts as 0). Refunds:
///    Paystack RefundedKobo + manual Refund entries.
///  - Estimated net to host = collected via Hodrac − absorbed Hodrac fee − Paystack fee.
/// </summary>
public class HostDashboardService
{
    public const int MaxPageSize = 100;
    public const int MaxCsvRows = 10_000;
    public const int DefaultChartMonths = 12;
    public const int MaxChartMonths = 36;
    public const int MaxSearchLength = 200;

    public static readonly string[] TripSorts = { "startDate", "owed", "overdue", "collected" };
    public static readonly string[] TravellerSorts = { "nextDueDate", "owes", "overdue" };

    private readonly HodracDbContext _db;

    public HostDashboardService(HodracDbContext db) => _db = db;

    // ── Loaded data ──────────────────────────────────────────────────────────

    private sealed record PaymentRow(
        Guid TripPostId, SeatPaymentStatus Status, long SeatAmountKobo, long HodracFeeKobo,
        long TravellerFeeKobo, long? PaystackFeeKobo, long RefundedKobo, DateTime At);

    private sealed record ManualRow(Guid TripPostId, long AmountKobo, ManualPaymentMethod Method, DateTime RecordedAt);

    private sealed record Seat(
        TripInterest Interest,
        long Owes,
        ScheduleLine? Next,
        DateOnly? NextDue,
        long Overdue,
        List<ScheduleLine> OverdueLines,
        bool CanRemove,
        bool DatesChosen)
    {
        public bool IsSeat => Interest.Status == TripInterestStatus.Approved
            && Interest.PaymentStatus != InterestPaymentStatus.Expired;
    }

    private sealed record TripData(
        TripPost Trip,
        string Title,
        string Location,
        List<Guid> DestinationIds,
        List<Seat> Seats,
        List<PaymentRow> Payments,
        List<ManualRow> Manual);

    private sealed record Snapshot(List<TripData> Trips, DateOnly Today, Guid? NextDepartureId);

    private async Task<Snapshot> LoadAsync(Guid userId, HostDashboardFilter filter, CancellationToken ct)
    {
        var today = HostTripService.TodayWat(DateTime.UtcNow);

        var query = _db.TripPosts.AsNoTracking().Where(HostTripService.OwnedHostedTrips(userId));
        if (filter.TripId is { } tripId) query = query.Where(t => t.Id == tripId);
        if (filter.Status is { } status) query = query.Where(t => t.Status == status);
        if (filter.Upcoming == true) query = query.Where(t => t.StartDate == null || t.StartDate >= today);
        if (filter.Upcoming == false) query = query.Where(t => t.StartDate != null && t.StartDate < today);
        if (filter.StartFrom is { } startFrom) query = query.Where(t => t.StartDate != null && t.StartDate >= startFrom);
        if (filter.StartTo is { } startTo) query = query.Where(t => t.StartDate != null && t.StartDate <= startTo);
        if (filter.DestinationId is { } destinationId)
            query = query.Where(t => t.DestinationId == destinationId
                || (t.Wishlist != null && t.Wishlist.WishlistDestinations.Any(wd => wd.DestinationId == destinationId)));
        if (filter.CityId is { } cityId)
            query = query.Where(t => (t.Destination != null && t.Destination.DestinationCities.Any(dc => dc.CityId == cityId))
                || (t.Wishlist != null && t.Wishlist.WishlistDestinations.Any(wd => wd.Destination.DestinationCities.Any(dc => dc.CityId == cityId))));
        if (filter.CountryId is { } countryId)
            query = query.Where(t => (t.Destination != null && t.Destination.CountryId == countryId)
                || (t.Wishlist != null && t.Wishlist.WishlistDestinations.Any(wd => wd.Destination.CountryId == countryId)));

        var trips = await query.ToListAsync(ct);

        // The next departure across all my trips (as on GET /api/host/trips), whatever the filters.
        var nextDepartureId = await _db.TripPosts.AsNoTracking()
            .Where(HostTripService.OwnedHostedTrips(userId))
            .Where(t => t.StartDate != null && t.StartDate >= today && t.Status != TripPostStatus.Cancelled)
            .OrderBy(t => t.StartDate).ThenBy(t => t.Id)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (trips.Count == 0) return new Snapshot(new List<TripData>(), today, nextDepartureId);

        var tripIds = trips.Select(t => t.Id).ToList();
        var places = (await _db.TripPosts.AsNoTracking()
                .Where(t => tripIds.Contains(t.Id))
                .Select(t => new
                {
                    t.Id,
                    t.Caption,
                    t.FreeTextDestination,
                    DestinationName = t.Destination != null ? t.Destination.DestinationName : null,
                    CountryName = t.Destination != null ? t.Destination.Country.CountryName : null,
                    WishlistName = t.Wishlist != null ? t.Wishlist.WishlistName : null,
                    Stops = t.Wishlist!.WishlistDestinations
                        .Select(wd => new { wd.DestinationId, wd.Destination.DestinationName })
                        .ToList(),
                })
                .ToListAsync(ct))
            .ToDictionary(p => p.Id);

        // Title and location label, then the search filter on them.
        var labelled = trips
            .Select(t =>
            {
                var p = places[t.Id];
                var stops = (p.Stops ?? new()).OrderBy(s => s.DestinationName).ToList();
                var location = t.DestinationId is not null && p.DestinationName is not null
                    ? TripLocationLabel.ForDestination(p.DestinationName, p.CountryName)
                    : t.WishlistId is not null && p.WishlistName is not null
                        ? TripLocationLabel.ForWishlist(stops.Select(s => s.DestinationName).ToList(), p.WishlistName)
                        : TripLocationLabel.ForFreeText(p.FreeTextDestination, p.Caption);
                var destinationIds = t.DestinationId is { } d ? new List<Guid> { d } : stops.Select(s => s.DestinationId).ToList();
                return (Trip: t,
                    Title: PaymentService.TripTitle(p.DestinationName, p.WishlistName, p.FreeTextDestination, p.Caption),
                    Location: location,
                    DestinationIds: destinationIds);
            })
            .ToList();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            labelled = labelled
                .Where(x => x.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || x.Location.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            tripIds = labelled.Select(x => x.Trip.Id).ToList();
            if (tripIds.Count == 0) return new Snapshot(new List<TripData>(), today, nextDepartureId);
        }

        var interests = await _db.TripInterests.AsNoTracking()
            .Where(i => tripIds.Contains(i.TripPostId))
            .ToListAsync(ct);

        var payments = await _db.SeatPayments.AsNoTracking()
            .Where(p => tripIds.Contains(p.TripPostId) && PaymentService.SucceededStatuses.Contains(p.Status))
            .Select(p => new PaymentRow(p.TripPostId, p.Status, p.SeatAmountKobo, p.HodracFeeKobo,
                p.TravellerFeeKobo, p.PaystackFeeKobo, p.RefundedKobo, p.PaidAt ?? p.CreatedAt))
            .ToListAsync(ct);

        var manual = await _db.ManualPayments.AsNoTracking()
            .Where(m => tripIds.Contains(m.TripInterest!.TripPostId))
            .Select(m => new ManualRow(m.TripInterest!.TripPostId, m.AmountKobo, m.Method, m.RecordedAt))
            .ToListAsync(ct);

        var travellerChoosesTrips = labelled
            .Where(x => x.Trip.InstalmentDatesMode == InstalmentDatesMode.TravellerChooses)
            .Select(x => x.Trip.Id)
            .ToHashSet();
        var datedInterestIds = interests.Where(i => travellerChoosesTrips.Contains(i.TripPostId)).Select(i => i.Id).ToList();
        var travellerDates = datedInterestIds.Count > 0
            ? await TravellerInstalmentDates.LoadAsync(_db, datedInterestIds, ct)
            : new Dictionary<Guid, Dictionary<int, DateOnly>>();

        var interestsByTrip = interests.ToLookup(i => i.TripPostId);
        var paymentsByTrip = payments.ToLookup(p => p.TripPostId);
        var manualByTrip = manual.ToLookup(m => m.TripPostId);

        var data = labelled
            .Select(x =>
            {
                var trip = x.Trip;
                var seats = interestsByTrip[trip.Id]
                    .Select(i =>
                    {
                        var dates = travellerDates.GetValueOrDefault(i.Id);
                        var lines = PaymentSchedule.Allocate(trip, i.AmountPaidKobo, dates);
                        var owes = trip.Status == TripPostStatus.Cancelled ? 0 : HostTripService.Owes(trip, i);
                        var next = owes > 0 ? lines.FirstOrDefault(l => !l.IsPaid) : null;
                        var overdue = HostTripService.OverdueOwed(trip, owes, lines, today);
                        return new Seat(
                            i,
                            owes,
                            next,
                            next is null ? null : next.DueDate ?? HostTripService.SeatHoldDay(i.SeatHoldExpiresAt),
                            overdue,
                            overdue > 0 ? PaymentSchedule.OverdueLines(lines, today) : new List<ScheduleLine>(),
                            HostTripService.RemovableOverdueLines(trip, i, lines, today).Count > 0,
                            dates is { Count: > 0 });
                    })
                    .ToList();
                return new TripData(trip, x.Title, x.Location, x.DestinationIds, seats,
                    paymentsByTrip[trip.Id].ToList(), manualByTrip[trip.Id].ToList());
            })
            .ToList();

        return new Snapshot(data, today, nextDepartureId);
    }

    private static HostDashboardFeesDto Fees(IEnumerable<PaymentRow> payments, IEnumerable<ManualRow> manual, long collectedViaHodrac)
    {
        var pays = payments.ToList();
        var travellerPaid = pays.Sum(p => p.TravellerFeeKobo);
        var hostAbsorbed = pays.Sum(p => Math.Max(0, p.HodracFeeKobo - p.TravellerFeeKobo));
        var paystack = pays.Sum(p => p.PaystackFeeKobo ?? 0);
        var refundedVia = pays.Sum(p => p.RefundedKobo);
        var refundedManual = -manual.Where(m => m.Method == ManualPaymentMethod.Refund && m.AmountKobo < 0).Sum(m => m.AmountKobo);
        return new HostDashboardFeesDto(
            travellerPaid,
            hostAbsorbed,
            paystack,
            refundedVia,
            refundedManual,
            refundedVia + refundedManual,
            EstimatedNetToHostKobo: collectedViaHodrac - hostAbsorbed - paystack);
    }

    // ── Summary ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The chart period: default the last <see cref="DefaultChartMonths"/>
    /// months to today (WAT), from the 1st of the first month; at most
    /// <see cref="MaxChartMonths"/> calendar months. Null error = valid.
    /// </summary>
    public static string? ResolvePeriod(DateOnly? paidFrom, DateOnly? paidTo, DateOnly today, out DateOnly from, out DateOnly to)
    {
        to = paidTo ?? (paidFrom is { } f0 && f0 > today ? f0 : today);
        var firstMonth = new DateOnly(to.Year, to.Month, 1).AddMonths(-(DefaultChartMonths - 1));
        from = paidFrom ?? firstMonth;
        if (to < from) return "'paidTo' must be on or after 'paidFrom'.";
        var months = (to.Year * 12 + to.Month) - (from.Year * 12 + from.Month) + 1;
        return months > MaxChartMonths ? $"The period can be at most {MaxChartMonths} months." : null;
    }

    public async Task<HostDashboardSummaryDto> SummaryAsync(
        Guid userId, HostDashboardFilter filter, DateOnly paidFrom, DateOnly paidTo, CancellationToken ct = default)
    {
        var snapshot = await LoadAsync(userId, filter, ct);
        var trips = snapshot.Trips;
        var live = trips.Where(t => t.Trip.Status != TripPostStatus.Cancelled).ToList();
        var seats = live.SelectMany(t => t.Seats).Where(s => s.IsSeat).ToList();
        var allSeats = trips.SelectMany(t => t.Seats).ToList();
        var via = allSeats.Sum(s => s.Interest.AmountPaidViaHodracKobo);
        var total = allSeats.Sum(s => s.Interest.AmountPaidKobo);

        var payments = trips.SelectMany(t => t.Payments).ToList();
        var manual = trips.SelectMany(t => t.Manual).ToList();

        var months = new List<HostDashboardMonthDto>();
        for (var m = new DateOnly(paidFrom.Year, paidFrom.Month, 1); m <= paidTo; m = m.AddMonths(1))
        {
            var start = m < paidFrom ? paidFrom : m;
            var next = m.AddMonths(1);
            bool InMonth(DateTime utc)
            {
                var day = DateOnly.FromDateTime(utc + HostTripService.Wat);
                return day >= start && day < next && day <= paidTo;
            }

            var monthVia = payments
                .Where(p => PaymentService.CreditedStatuses.Contains(p.Status) && InMonth(p.At))
                .Sum(p => Math.Max(0, p.SeatAmountKobo - p.RefundedKobo));
            var monthManual = manual.Where(x => InMonth(x.RecordedAt)).Sum(x => x.AmountKobo);
            months.Add(new HostDashboardMonthDto(
                m.ToString("yyyy-MM", CultureInfo.InvariantCulture), monthVia, monthManual, monthVia + monthManual));
        }

        return new HostDashboardSummaryDto(
            Trips: trips.Count,
            SeatsTotal: live.Sum(t => t.Trip.MaxGroupSize ?? 0),
            SeatsSold: seats.Count,
            SeatsWithDeposit: seats.Count(s => s.Interest.AmountPaidKobo > 0),
            SeatsPaidInFull: seats.Count(s => s.Interest.PaymentStatus == InterestPaymentStatus.PaidInFull),
            CollectedViaHodracKobo: via,
            CollectedManualKobo: total - via,
            CollectedTotalKobo: total,
            OwedKobo: allSeats.Sum(s => s.Owes),
            OverdueKobo: allSeats.Sum(s => s.Overdue),
            OverdueTravellers: allSeats.Count(s => s.Overdue > 0),
            Fees: Fees(payments, manual, via),
            PaidFrom: paidFrom,
            PaidTo: paidTo,
            TimeZone: HostReportService.TimeZoneLabel,
            Monthly: months);
    }

    // ── Trips ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every matching trip, sorted: "startDate" (default; upcoming first,
    /// soonest first, undated last, then past trips most recent first, as
    /// GET /api/host/trips), or "owed" / "overdue" / "collected" (largest
    /// first, then the default order).
    /// </summary>
    public async Task<List<HostDashboardTripDto>> TripsAsync(
        Guid userId, HostDashboardFilter filter, string? sort, CancellationToken ct = default)
    {
        var snapshot = await LoadAsync(userId, filter, ct);
        var today = snapshot.Today;

        var rows = snapshot.Trips.Select(t =>
        {
            var trip = t.Trip;
            var seats = t.Seats.Where(s => s.IsSeat).ToList();
            var via = t.Seats.Sum(s => s.Interest.AmountPaidViaHodracKobo);
            var total = t.Seats.Sum(s => s.Interest.AmountPaidKobo);
            return new HostDashboardTripDto(
                trip.Id,
                t.Title,
                t.Location,
                trip.DestinationId,
                trip.WishlistId,
                t.DestinationIds,
                trip.StartDate,
                trip.EndDate,
                trip.Status.ToString(),
                trip.IsListed,
                trip.IsHidden,
                trip.Currency,
                trip.FeeMode.ToString(),
                trip.InstalmentDatesMode.ToString(),
                trip.MaxGroupSize,
                trip.PricePerSeatKobo ?? 0,
                Approved: seats.Count,
                SeatsWithDeposit: seats.Count(s => s.Interest.AmountPaidKobo > 0),
                SeatsPaidInFull: seats.Count(s => s.Interest.PaymentStatus == InterestPaymentStatus.PaidInFull),
                CollectedKobo: via,
                CollectedManualKobo: total - via,
                CollectedTotalKobo: total,
                OwedKobo: t.Seats.Sum(s => s.Owes),
                OverdueKobo: t.Seats.Sum(s => s.Overdue),
                OverdueTravellers: t.Seats.Count(s => s.Overdue > 0),
                NextDueDate: t.Seats.Where(s => s.NextDue is not null).Select(s => s.NextDue).Min(),
                IsUpcoming: trip.StartDate is null || trip.StartDate >= today,
                IsNextDeparture: trip.Id == snapshot.NextDepartureId,
                Fees: Fees(t.Payments, t.Manual, via));
        });

        IOrderedEnumerable<HostDashboardTripDto> ordered = (sort?.Trim() ?? "startDate").ToLowerInvariant() switch
        {
            "owed" => rows.OrderByDescending(r => r.OwedKobo),
            "overdue" => rows.OrderByDescending(r => r.OverdueKobo),
            "collected" => rows.OrderByDescending(r => r.CollectedTotalKobo),
            _ => rows.OrderBy(_ => 0),
        };
        return ordered
            .ThenByDescending(r => r.IsUpcoming)
            .ThenBy(r => r.IsUpcoming ? (r.StartDate ?? DateOnly.MaxValue).DayNumber : -(r.StartDate ?? DateOnly.MinValue).DayNumber)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id)
            .ToList();
    }

    // ── Travellers ───────────────────────────────────────────────────────────

    /// <summary>
    /// One row per traveller across the matching trips (declined requests
    /// left out). The shared filter's Search is not used here; the
    /// traveller filter's Search matches the traveller or the trip title.
    /// Sort: "nextDueDate" (default; soonest first, nothing due last),
    /// "owes" or "overdue" (largest first); then trip start, name.
    /// </summary>
    public async Task<List<HostDashboardTravellerDto>> TravellersAsync(
        Guid userId, HostDashboardFilter filter, HostDashboardTravellerFilter travellerFilter, string? sort,
        CancellationToken ct = default)
    {
        var snapshot = await LoadAsync(userId, filter with { Search = null }, ct);
        var today = snapshot.Today;

        var pairs = snapshot.Trips
            .SelectMany(t => t.Seats.Where(s => s.Interest.Status != TripInterestStatus.Declined).Select(s => (Trip: t, Seat: s)))
            .ToList();

        var interestIds = pairs.Select(p => p.Seat.Interest.Id).ToList();
        var userIds = pairs.Select(p => p.Seat.Interest.RequesterUserId.ToString()).Distinct().ToList();
        var users = userIds.Count == 0
            ? new()
            : await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName, u.Email, u.PhoneNumber })
                .ToDictionaryAsync(u => u.Id, ct);
        var lastReminders = interestIds.Count == 0
            ? new()
            : await _db.PaymentReminderLogs.AsNoTracking()
                .Where(r => interestIds.Contains(r.TripInterestId))
                .GroupBy(r => r.TripInterestId)
                .Select(g => new { g.Key, Last = g.Max(r => r.SentAt) })
                .ToDictionaryAsync(x => x.Key, x => x.Last, ct);

        var rows = pairs.Select(p =>
        {
            var (t, s) = p;
            var i = s.Interest;
            var user = users.GetValueOrDefault(i.RequesterUserId.ToString());
            var oldestOverdue = s.OverdueLines.Select(l => l.DueDate).Min();
            return new HostDashboardTravellerDto(
                t.Trip.Id,
                t.Title,
                t.Trip.StartDate,
                t.Trip.Status.ToString(),
                i.Id,
                i.RequesterUserId,
                string.IsNullOrWhiteSpace(user?.DisplayName) ? "Traveller" : user!.DisplayName,
                user?.Email,
                string.IsNullOrWhiteSpace(i.ContactPhone) ? NullIfBlank(user?.PhoneNumber) : i.ContactPhone,
                i.Status.ToString(),
                i.PaymentStatus.ToString(),
                i.AmountPaidKobo,
                i.AmountPaidViaHodracKobo,
                i.AmountPaidKobo - i.AmountPaidViaHodracKobo,
                s.Owes,
                s.NextDue,
                s.Next?.RemainingKobo,
                s.Next?.Kind.ToString(),
                s.Overdue,
                s.OverdueLines.Count,
                oldestOverdue is { } due ? today.DayNumber - due.DayNumber : null,
                i.SeatHoldExpiresAt,
                lastReminders.TryGetValue(i.Id, out var last) ? last : null,
                t.Trip.InstalmentDatesMode.ToString(),
                s.DatesChosen,
                s.CanRemove,
                i.RemovedReason,
                i.RemovedAt,
                i.RequestedAt);
        });

        if (travellerFilter.PaymentStatus is { } paymentStatus)
            rows = rows.Where(r => r.PaymentStatus == paymentStatus.ToString());
        if (travellerFilter.InterestStatus is { } interestStatus)
            rows = rows.Where(r => r.InterestStatus == interestStatus.ToString());
        if (travellerFilter.Owing is { } owing)
            rows = rows.Where(r => (r.OwesKobo > 0) == owing);
        if (travellerFilter.Overdue is { } overdue)
            rows = rows.Where(r => (r.OverdueKobo > 0) == overdue);
        if (!string.IsNullOrWhiteSpace(travellerFilter.Search))
        {
            var q = travellerFilter.Search.Trim();
            rows = rows.Where(r => r.TravellerName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (r.Email?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Phone?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || r.TripTitle.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<HostDashboardTravellerDto> ordered = (sort?.Trim() ?? "nextDueDate").ToLowerInvariant() switch
        {
            "owes" => rows.OrderByDescending(r => r.OwesKobo),
            "overdue" => rows.OrderByDescending(r => r.OverdueKobo).ThenByDescending(r => r.DaysOverdue ?? -1),
            _ => rows.OrderBy(r => r.NextDueDate is null).ThenBy(r => r.NextDueDate),
        };
        return ordered
            .ThenBy(r => r.TripStartDate is null)
            .ThenBy(r => r.TripStartDate)
            .ThenBy(r => r.TravellerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RequestedAt)
            .ThenBy(r => r.InterestId)
            .ToList();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
