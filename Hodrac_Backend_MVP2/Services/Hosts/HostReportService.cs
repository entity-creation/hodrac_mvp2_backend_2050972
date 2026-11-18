using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.HostDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services.Hosts;

/// <summary>
/// The host weekly report (GET /api/host/reports/weekly, and the Phase 6
/// HostWeeklyReportJob email). Weeks are Monday–Sunday in WAT (UTC+1).
///
/// Per hosted trip of the host (author = the host's user):
///  - page views: TripPageViewsDaily (WAT days), split by source. There is no
///    per-trip outbound link-click data (partner clicks belong to partner
///    links, not trips), so "link clicks" are opens of the shared trip link
///    (the "share" preview page views).
///  - join requests: interests whose RequestedAt is in the week (a re-sent
///    request counts again); approvals: interests still Approved whose
///    RespondedAt is in the week.
///  - seats paid: interests whose first money (a Paystack payment or a
///    positive manual payment) landed in the week.
///  - collected: seat money of Paystack payments paid in the week (gross,
///    before any later refund) vs manual payments recorded in the week;
///    manual refunds are reported separately.
///  - % through Hodrac: this week's split, and all-time from the interests'
///    totals (Σ AmountPaidViaHodracKobo / Σ AmountPaidKobo).
/// </summary>
public class HostReportService
{
    public const string TimeZoneLabel = "WAT";

    private readonly HodracDbContext _db;
    private readonly HostTripService _trips;

    public HostReportService(HodracDbContext db, HostTripService trips)
    {
        _db = db;
        _trips = trips;
    }

    /// <summary>Monday of the last full Mon–Sun week (WAT) before <paramref name="utcNow"/>.</summary>
    public static DateOnly LastFullWeekStart(DateTime utcNow) => MondayOf(HostTripService.TodayWat(utcNow)).AddDays(-7);

    /// <summary>The Monday on or before <paramref name="day"/>.</summary>
    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// Builds the report for the week starting <paramref name="weekStart"/>
    /// (moved back to its Monday; default: the last full week). Null when the
    /// host profile doesn't exist.
    /// </summary>
    public async Task<HostWeeklyReportDto?> BuildWeeklyAsync(Guid hostProfileId, DateOnly? weekStart = null, CancellationToken ct = default)
    {
        var host = await _db.HostProfiles.AsNoTracking()
            .Where(h => h.Id == hostProfileId)
            .Select(h => new { h.Id, h.UserId, h.DisplayName })
            .FirstOrDefaultAsync(ct);
        if (host is null) return null;

        var start = weekStart is { } ws ? MondayOf(ws) : LastFullWeekStart(DateTime.UtcNow);
        var end = start.AddDays(6);
        var fromUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) - HostTripService.Wat;
        var toUtc = fromUtc.AddDays(7);

        var trips = await _db.TripPosts.AsNoTracking()
            .Where(t => t.IsHostedTrip && t.HostProfileId == host.Id && t.AuthorUserId == host.UserId)
            .Select(t => new { t.Id, t.StartDate, t.Status })
            .ToListAsync(ct);
        var tripIds = trips.Select(t => t.Id).ToList();
        var titles = await _trips.TripTitlesAsync(tripIds, ct);

        var views = await _db.TripPageViewsDaily.AsNoTracking()
            .Where(v => tripIds.Contains(v.TripPostId) && v.Date >= start && v.Date <= end)
            .GroupBy(v => new { v.TripPostId, v.Source })
            .Select(g => new { g.Key.TripPostId, g.Key.Source, Count = g.Sum(v => v.Count) })
            .ToListAsync(ct);

        var interests = await _db.TripInterests.AsNoTracking()
            .Where(i => tripIds.Contains(i.TripPostId))
            .Select(i => new { i.Id, i.TripPostId, i.Status, i.RequestedAt, i.RespondedAt, i.AmountPaidKobo, i.AmountPaidViaHodracKobo })
            .ToListAsync(ct);

        var seatPayments = await _db.SeatPayments.AsNoTracking()
            .Where(p => tripIds.Contains(p.TripPostId) && PaymentService.SucceededStatuses.Contains(p.Status) && p.PaidAt != null)
            .Select(p => new { p.TripPostId, p.TripInterestId, p.SeatAmountKobo, PaidAt = p.PaidAt!.Value })
            .ToListAsync(ct);

        var manual = await _db.ManualPayments.AsNoTracking()
            .Where(m => tripIds.Contains(m.TripInterest!.TripPostId))
            .Select(m => new { m.TripInterest!.TripPostId, m.TripInterestId, m.AmountKobo, m.Method, m.RecordedAt })
            .ToListAsync(ct);

        bool InWeek(DateTime at) => at >= fromUtc && at < toUtc;

        // When each interest first had money.
        var firstMoney = seatPayments.Select(p => (p.TripInterestId, At: p.PaidAt))
            .Concat(manual.Where(m => m.AmountKobo > 0).Select(m => (m.TripInterestId, At: m.RecordedAt)))
            .GroupBy(x => x.TripInterestId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.At));

        var rows = trips
            .Select(t =>
            {
                var tripViews = views.Where(v => v.TripPostId == t.Id).ToList();
                var tripInterests = interests.Where(i => i.TripPostId == t.Id).ToList();
                var tripManual = manual.Where(m => m.TripPostId == t.Id && InWeek(m.RecordedAt)).ToList();

                var stats = Stats(
                    pageViewsBySource: TripPageViewSources.All.ToDictionary(
                        s => s, s => tripViews.Where(v => v.Source == s).Sum(v => v.Count)),
                    otherViews: tripViews.Where(v => !TripPageViewSources.All.Contains(v.Source)).Sum(v => v.Count),
                    joinRequests: tripInterests.Count(i => InWeek(i.RequestedAt)),
                    approvals: tripInterests.Count(i => i.Status == TripInterestStatus.Approved && i.RespondedAt is { } r && InWeek(r)),
                    seatsPaid: tripInterests.Count(i => firstMoney.TryGetValue(i.Id, out var first) && InWeek(first)),
                    viaHodrac: seatPayments.Where(p => p.TripPostId == t.Id && InWeek(p.PaidAt)).Sum(p => p.SeatAmountKobo),
                    manualIn: tripManual.Where(m => m.AmountKobo > 0).Sum(m => m.AmountKobo),
                    manualRefunded: -tripManual.Where(m => m.AmountKobo < 0).Sum(m => m.AmountKobo),
                    allTimeVia: tripInterests.Sum(i => i.AmountPaidViaHodracKobo),
                    allTimeTotal: tripInterests.Sum(i => i.AmountPaidKobo));

                return new WeeklyReportTripDto(t.Id, titles.GetValueOrDefault(t.Id) ?? "Trip", t.StartDate, t.Status.ToString(), stats);
            })
            .OrderBy(r => r.StartDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var all = rows.Select(r => r.Stats).ToList();
        var totals = Stats(
            pageViewsBySource: TripPageViewSources.All.ToDictionary(s => s, s => all.Sum(x => x.PageViewsBySource.GetValueOrDefault(s))),
            otherViews: all.Sum(x => x.PageViews - x.PageViewsBySource.Values.Sum()),
            joinRequests: all.Sum(x => x.JoinRequests),
            approvals: all.Sum(x => x.Approvals),
            seatsPaid: all.Sum(x => x.SeatsPaid),
            viaHodrac: all.Sum(x => x.CollectedViaHodracKobo),
            manualIn: all.Sum(x => x.CollectedManualKobo),
            manualRefunded: all.Sum(x => x.RefundedManualKobo),
            allTimeVia: interests.Sum(i => i.AmountPaidViaHodracKobo),
            allTimeTotal: interests.Sum(i => i.AmountPaidKobo));

        return new HostWeeklyReportDto(host.Id, host.DisplayName, start, end, TimeZoneLabel, rows, totals);
    }

    private static WeeklyReportStatsDto Stats(
        Dictionary<string, int> pageViewsBySource, int otherViews, int joinRequests, int approvals, int seatsPaid,
        long viaHodrac, long manualIn, long manualRefunded, long allTimeVia, long allTimeTotal) =>
        new(
            PageViews: pageViewsBySource.Values.Sum() + otherViews,
            PageViewsBySource: pageViewsBySource,
            LinkClicks: pageViewsBySource.GetValueOrDefault(TripPageViewSources.Share),
            JoinRequests: joinRequests,
            Approvals: approvals,
            SeatsPaid: seatsPaid,
            CollectedViaHodracKobo: viaHodrac,
            CollectedManualKobo: manualIn,
            RefundedManualKobo: manualRefunded,
            HodracSharePercentWeek: Percent(viaHodrac, viaHodrac + manualIn),
            HodracSharePercentAllTime: Percent(allTimeVia, allTimeTotal));

    private static decimal? Percent(long part, long whole) =>
        whole <= 0 ? null : Math.Round(Math.Min(100m, 100m * part / whole), 1, MidpointRounding.AwayFromZero);
}
