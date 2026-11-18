using System.Globalization;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Services.Hosts;

/// <summary>
/// The business-plan numbers for Hodrac Host (GMV, Hodrac fee, % of seat money
/// through Hodrac, paid departures, seats, active hosts, founding and operator
/// plans), from Postgres only. Every definition is documented on
/// <see cref="AdminHostingInsightsDto"/>.
/// </summary>
public class HostingInsightsService
{
    private static readonly TimeSpan Wat = HostTripService.Wat;

    private readonly HodracDbContext _db;
    private readonly HodracHostOptions _options;

    public HostingInsightsService(HodracDbContext db, IOptions<HodracHostOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    private static DateTime StartOfDayUtc(DateOnly watDay) =>
        DateTime.SpecifyKind(watDay.ToDateTime(TimeOnly.MinValue) - Wat, DateTimeKind.Utc);

    private static DateOnly WatDate(DateTime utc) => DateOnly.FromDateTime(utc + Wat);

    private static string MonthKey(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public async Task<AdminHostingInsightsDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = StartOfDayUtc(from);
        var toUtc = StartOfDayUtc(to.AddDays(1)); // exclusive

        // ── Hodrac payments (by PaidAt) ──
        var payments = await _db.SeatPayments.AsNoTracking()
            .Where(p => PaymentService.SucceededStatuses.Contains(p.Status)
                && (p.PaidAt ?? p.CreatedAt) >= fromUtc
                && (p.PaidAt ?? p.CreatedAt) < toUtc)
            .Select(p => new
            {
                At = p.PaidAt ?? p.CreatedAt,
                p.Status,
                p.SeatAmountKobo,
                p.HodracFeeKobo,
                p.RefundedKobo,
                p.TripInterestId,
                HostId = p.TripPost!.HostProfileId,
            })
            .ToListAsync(ct);

        // ── Manual payments (by RecordedAt) ──
        var manual = await _db.ManualPayments.AsNoTracking()
            .Where(m => m.RecordedAt >= fromUtc && m.RecordedAt < toUtc)
            .Select(m => new
            {
                At = m.RecordedAt,
                m.AmountKobo,
                m.Method,
                m.TripInterestId,
                HostId = m.TripInterest!.TripPost!.HostProfileId,
            })
            .ToListAsync(ct);

        // ── Paid departures (hosted trips starting in the period, with money on a seat) ──
        var departures = await _db.TripPosts.AsNoTracking()
            .Where(t => t.IsHostedTrip
                && t.StartDate != null && t.StartDate >= from && t.StartDate <= to
                && _db.TripInterests.Any(i => i.TripPostId == t.Id && i.AmountPaidKobo > 0))
            .Select(t => t.StartDate!.Value)
            .ToListAsync(ct);

        // ── Operator plan payments (by CreatedAt) ──
        var plans = await _db.OperatorPlanPayments.AsNoTracking()
            .Where(p => p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
            .Select(p => new { At = p.CreatedAt, p.AmountKobo })
            .ToListAsync(ct);

        // Normalised rows: (month, seat, host) for "a seat was paid".
        var seatPaidRows = payments
            .Where(p => PaymentService.CreditedStatuses.Contains(p.Status))
            .Select(p => (Month: MonthKey(WatDate(p.At)), p.TripInterestId, p.HostId))
            .Concat(manual
                .Where(m => m.AmountKobo > 0 && m.Method != ManualPaymentMethod.Refund)
                .Select(m => (Month: MonthKey(WatDate(m.At)), m.TripInterestId, m.HostId)))
            .ToList();

        AdminHostingMonthDto Bucket(string month, Func<DateOnly, bool> inBucket)
        {
            var pays = payments.Where(p => inBucket(WatDate(p.At))).ToList();
            var viaHodrac = pays
                .Where(p => PaymentService.CreditedStatuses.Contains(p.Status))
                .Sum(p => Math.Max(0, p.SeatAmountKobo - p.RefundedKobo));
            var manualNet = manual.Where(m => inBucket(WatDate(m.At))).Sum(m => m.AmountKobo);
            var gmv = viaHodrac + manualNet;
            var seatRows = seatPaidRows.Where(r => month == "*" || r.Month == month).ToList();
            var monthPlans = plans.Where(p => inBucket(WatDate(p.At))).ToList();

            return new AdminHostingMonthDto(
                month,
                gmv,
                viaHodrac,
                manualNet,
                pays.Sum(p => p.HodracFeeKobo),
                pays.Sum(p => p.RefundedKobo),
                gmv > 0 ? Math.Round(100.0 * viaHodrac / gmv, 1) : null,
                seatRows.Select(r => r.TripInterestId).Distinct().Count(),
                seatRows.Where(r => r.HostId != null).Select(r => r.HostId).Distinct().Count(),
                departures.Count(inBucket),
                monthPlans.Sum(p => p.AmountKobo),
                monthPlans.Count);
        }

        var months = new List<AdminHostingMonthDto>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var start = m;
            var end = m.AddMonths(1);
            months.Add(Bucket(MonthKey(m), d => d >= start && d < end && d >= from && d <= to));
        }
        var total = Bucket("*", d => d >= from && d <= to);

        // ── Snapshots ──
        var now = DateTime.UtcNow;
        var verifiedHosts = await _db.HostProfiles.CountAsync(h => h.VerificationStatus == HostVerificationStatus.Verified, ct);
        var foundingUsed = await _db.HostProfiles.CountAsync(h => h.IsFoundingHost, ct);
        // Every free departure ever used, including by hosts whose founding status was later removed.
        var freeDepartures = await _db.HostProfiles.CountAsync(h => h.FoundingFreeDepartureTripId != null, ct);
        var operatorActive = await _db.HostProfiles.CountAsync(h =>
            h.Plan == HostPlan.Operator && h.OperatorPlanExpiresAt != null && h.OperatorPlanExpiresAt > now, ct);

        return new AdminHostingInsightsDto(
            from,
            to,
            total.GmvKobo,
            total.ViaHodracKobo,
            total.ManualNetKobo,
            total.HodracFeeKobo,
            total.RefundedViaHodracKobo,
            total.PercentThroughHodrac,
            total.SeatsPaid,
            total.ActiveHosts,
            total.PaidDepartures,
            total.OperatorPlanRevenueKobo,
            total.OperatorPlanPayments,
            verifiedHosts,
            foundingUsed,
            _options.FoundingHostLimit,
            freeDepartures,
            operatorActive,
            months);
    }
}
