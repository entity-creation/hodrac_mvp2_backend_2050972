using System.Globalization;
using System.Text;
using Hodrac_Backend_MVP2.DTOs.HostDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Infrastucture.Http;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Hosts;
using Microsoft.AspNetCore.Mvc;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>Query string filters every dashboard endpoint shares (all optional).</summary>
public class HostDashboardQuery
{
    public Guid? TripId { get; set; }
    public string? Status { get; set; }          // Open | Closed | Cancelled
    public bool? Upcoming { get; set; }
    public Guid? DestinationId { get; set; }
    public Guid? CityId { get; set; }
    public Guid? CountryId { get; set; }
    public DateOnly? StartFrom { get; set; }     // trip start date, yyyy-MM-dd, inclusive
    public DateOnly? StartTo { get; set; }
    public string? Search { get; set; }
}

/// <summary>
/// The host's dashboard across all their hosted trips (read-only, so open to
/// hosts closed by Hodrac). Money rules: <see cref="HostDashboardService"/>.
///
///   GET /api/host/dashboard/summary?…filters&amp;paidFrom&amp;paidTo        totals + monthly[] chart
///   GET /api/host/dashboard/trips?…filters&amp;sort&amp;page&amp;pageSize       one row per trip
///   GET /api/host/dashboard/travellers?…filters&amp;paymentStatus&amp;interestStatus&amp;owing&amp;overdue&amp;sort&amp;page&amp;pageSize
///   GET /api/host/dashboard/trips.csv, /travellers.csv              same rows, not paged (≤ 10,000)
///
/// Filters: tripId, status, upcoming, destinationId, cityId, countryId,
/// startFrom, startTo, search (trip title or location; on travellers it also
/// matches the traveller's name, email or phone). Pages: { items, page,
/// pageSize, totalCount }, pageSize 1–100 (default 20).
/// </summary>
public partial class HostController
{
    private const int DefaultDashboardPageSize = 20;

    [HttpGet("dashboard/summary")]
    public async Task<ActionResult<HostDashboardSummaryDto>> GetDashboardSummary(
        [FromQuery] HostDashboardQuery query,
        [FromServices] HostDashboardService dashboard,
        [FromQuery] DateOnly? paidFrom = null,
        [FromQuery] DateOnly? paidTo = null,
        CancellationToken ct = default)
    {
        if (!TryDashboardFilter(query, out var filter)) return ValidationProblem(ModelState);
        var error = HostDashboardService.ResolvePeriod(paidFrom, paidTo, HostTripService.TodayWat(DateTime.UtcNow), out var from, out var to);
        if (error is not null)
        {
            ModelState.AddModelError(nameof(paidTo), error);
            return ValidationProblem(ModelState);
        }
        return Ok(await dashboard.SummaryAsync(_currentUser.UserId, filter, from, to, ct));
    }

    [HttpGet("dashboard/trips")]
    public async Task<ActionResult<AdminPagedResult<HostDashboardTripDto>>> GetDashboardTrips(
        [FromQuery] HostDashboardQuery query,
        [FromServices] HostDashboardService dashboard,
        [FromQuery] string? sort = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultDashboardPageSize,
        CancellationToken ct = default)
    {
        if (!TryDashboardFilter(query, out var filter) || !TrySort(sort, HostDashboardService.TripSorts))
            return ValidationProblem(ModelState);
        var rows = await dashboard.TripsAsync(_currentUser.UserId, filter, sort, ct);
        return Ok(Page(rows, page, pageSize));
    }

    [HttpGet("dashboard/travellers")]
    public async Task<ActionResult<AdminPagedResult<HostDashboardTravellerDto>>> GetDashboardTravellers(
        [FromQuery] HostDashboardQuery query,
        [FromServices] HostDashboardService dashboard,
        [FromQuery] string? paymentStatus = null,
        [FromQuery] string? interestStatus = null,
        [FromQuery] bool? owing = null,
        [FromQuery] bool? overdue = null,
        [FromQuery] string? sort = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultDashboardPageSize,
        CancellationToken ct = default)
    {
        if (!TryDashboardFilter(query, out var filter)
            | !TryTravellerFilter(query.Search, paymentStatus, interestStatus, owing, overdue, out var travellerFilter)
            | !TrySort(sort, HostDashboardService.TravellerSorts))
            return ValidationProblem(ModelState);
        var rows = await dashboard.TravellersAsync(_currentUser.UserId, filter, travellerFilter, sort, ct);
        return Ok(Page(rows, page, pageSize));
    }

    /// <summary>Same rows and filters as /dashboard/trips, not paged (first 10,000; X-Rows-Truncated: true beyond).</summary>
    [HttpGet("dashboard/trips.csv")]
    public async Task<IActionResult> GetDashboardTripsCsv(
        [FromQuery] HostDashboardQuery query,
        [FromServices] HostDashboardService dashboard,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        if (!TryDashboardFilter(query, out var filter) || !TrySort(sort, HostDashboardService.TripSorts))
            return ValidationProblem(ModelState);
        var rows = await dashboard.TripsAsync(_currentUser.UserId, filter, sort, ct);

        var csv = new StringBuilder();
        csv.Append("tripId,title,location,startDate,endDate,status,currency,feeMode,instalmentDatesMode,seatsTotal,pricePerSeatNgn,")
           .Append("approved,seatsWithDeposit,seatsPaidInFull,collectedViaHodracNgn,collectedManualNgn,collectedTotalNgn,")
           .Append("owedNgn,overdueNgn,overdueTravellers,nextDueDate,hodracFeeTravellerPaidNgn,hodracFeeHostAbsorbedNgn,")
           .Append("paystackFeeNgn,refundedViaHodracNgn,refundedManualNgn,estimatedNetToHostNgn").Append(CsvExport.LineEnd);
        foreach (var r in rows.Take(HostDashboardService.MaxCsvRows))
        {
            csv.Append(r.Id).Append(',')
               .Append(CsvExport.Field(r.Title)).Append(',')
               .Append(CsvExport.Field(r.LocationLabel)).Append(',')
               .Append(CsvExport.Date(r.StartDate)).Append(',')
               .Append(CsvExport.Date(r.EndDate)).Append(',')
               .Append(r.Status).Append(',')
               .Append(CsvExport.Field(r.Currency)).Append(',')
               .Append(r.FeeMode).Append(',')
               .Append(r.InstalmentDatesMode).Append(',')
               .Append(r.SeatsTotal?.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(CsvExport.Naira(r.PricePerSeatKobo)).Append(',')
               .Append(r.Approved.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(r.SeatsWithDeposit.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(r.SeatsPaidInFull.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(CsvExport.Naira(r.CollectedKobo)).Append(',')
               .Append(CsvExport.Naira(r.CollectedManualKobo)).Append(',')
               .Append(CsvExport.Naira(r.CollectedTotalKobo)).Append(',')
               .Append(CsvExport.Naira(r.OwedKobo)).Append(',')
               .Append(CsvExport.Naira(r.OverdueKobo)).Append(',')
               .Append(r.OverdueTravellers.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(CsvExport.Date(r.NextDueDate)).Append(',')
               .Append(CsvExport.Naira(r.Fees.HodracFeeTravellerPaidKobo)).Append(',')
               .Append(CsvExport.Naira(r.Fees.HodracFeeHostAbsorbedKobo)).Append(',')
               .Append(CsvExport.Naira(r.Fees.PaystackFeeKobo)).Append(',')
               .Append(CsvExport.Naira(r.Fees.RefundedViaHodracKobo)).Append(',')
               .Append(CsvExport.Naira(r.Fees.RefundedManualKobo)).Append(',')
               .Append(CsvExport.Naira(r.Fees.EstimatedNetToHostKobo)).Append(CsvExport.LineEnd);
        }

        return await DashboardCsvAsync(csv, "trips", rows.Count, ct);
    }

    /// <summary>Same rows and filters as /dashboard/travellers, not paged (first 10,000; X-Rows-Truncated: true beyond).</summary>
    [HttpGet("dashboard/travellers.csv")]
    public async Task<IActionResult> GetDashboardTravellersCsv(
        [FromQuery] HostDashboardQuery query,
        [FromServices] HostDashboardService dashboard,
        [FromQuery] string? paymentStatus = null,
        [FromQuery] string? interestStatus = null,
        [FromQuery] bool? owing = null,
        [FromQuery] bool? overdue = null,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        if (!TryDashboardFilter(query, out var filter)
            | !TryTravellerFilter(query.Search, paymentStatus, interestStatus, owing, overdue, out var travellerFilter)
            | !TrySort(sort, HostDashboardService.TravellerSorts))
            return ValidationProblem(ModelState);
        var rows = await dashboard.TravellersAsync(_currentUser.UserId, filter, travellerFilter, sort, ct);

        var csv = new StringBuilder();
        csv.Append("tripId,tripTitle,tripStartDate,interestId,name,email,phone,requestStatus,paymentStatus,")
           .Append("paidNgn,viaHodracNgn,manualNgn,owesNgn,nextDueDate,nextDueNgn,overdueNgn,daysOverdue,")
           .Append("lastReminderAt,instalmentDatesMode,datesChosenByTraveller,canRemove,removedReason,removedAt,requestedAt")
           .Append(CsvExport.LineEnd);
        foreach (var r in rows.Take(HostDashboardService.MaxCsvRows))
        {
            csv.Append(r.TripId).Append(',')
               .Append(CsvExport.Field(r.TripTitle)).Append(',')
               .Append(CsvExport.Date(r.TripStartDate)).Append(',')
               .Append(r.InterestId).Append(',')
               .Append(CsvExport.Field(r.TravellerName)).Append(',')
               .Append(CsvExport.Field(r.Email)).Append(',')
               .Append(CsvExport.Field(r.Phone)).Append(',')
               .Append(r.InterestStatus).Append(',')
               .Append(r.PaymentStatus).Append(',')
               .Append(CsvExport.Naira(r.PaidKobo)).Append(',')
               .Append(CsvExport.Naira(r.ViaHodracKobo)).Append(',')
               .Append(CsvExport.Naira(r.ManualKobo)).Append(',')
               .Append(CsvExport.Naira(r.OwesKobo)).Append(',')
               .Append(CsvExport.Date(r.NextDueDate)).Append(',')
               .Append(CsvExport.Naira(r.NextDueAmountKobo)).Append(',')
               .Append(CsvExport.Naira(r.OverdueKobo)).Append(',')
               .Append(r.DaysOverdue?.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(CsvExport.Time(r.LastReminderAt)).Append(',')
               .Append(r.InstalmentDatesMode).Append(',')
               .Append(CsvExport.Bool(r.DatesChosenByTraveller)).Append(',')
               .Append(CsvExport.Bool(r.CanRemove)).Append(',')
               .Append(CsvExport.Field(r.RemovedReason)).Append(',')
               .Append(CsvExport.Time(r.RemovedAt)).Append(',')
               .Append(CsvExport.Time(r.RequestedAt)).Append(CsvExport.LineEnd);
        }

        return await DashboardCsvAsync(csv, "travellers", rows.Count, ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IActionResult> DashboardCsvAsync(StringBuilder csv, string kind, int rowCount, CancellationToken ct)
    {
        var host = await _hosts.GetByUserAsync(_currentUser.UserId, ct);
        var slug = CsvExport.Slug(host?.DisplayName ?? "", maxLength: 60);
        var fileName = $"{kind}-{(slug.Length == 0 ? "host" : slug)}-{HostTripService.TodayWat(DateTime.UtcNow):yyyy-MM-dd}.csv";
        if (rowCount > HostDashboardService.MaxCsvRows) Response.Headers["X-Rows-Truncated"] = "true";

        _logger.LogInformation("User {UserId} exported the dashboard {Kind} CSV ({Rows} rows)", _currentUser.UserId, kind, rowCount);
        return File(CsvExport.ToBytes(csv), CsvExport.ContentType, fileName);
    }

    private static AdminPagedResult<T> Page<T>(List<T> rows, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, HostDashboardService.MaxPageSize);
        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new AdminPagedResult<T>(items, page, pageSize, rows.Count);
    }

    private bool TryDashboardFilter(HostDashboardQuery query, out HostDashboardFilter filter)
    {
        filter = new HostDashboardFilter();
        TripPostStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (TryParseName<TripPostStatus>(query.Status, out var parsed)) status = parsed;
            else ModelState.AddModelError("status", $"Status must be one of: {string.Join(", ", Enum.GetNames<TripPostStatus>())}.");
        }
        if (query.StartFrom is { } from && query.StartTo is { } to && to < from)
            ModelState.AddModelError("startTo", "'startTo' must be on or after 'startFrom'.");
        if (query.Search is { Length: > HostDashboardService.MaxSearchLength })
            ModelState.AddModelError("search", $"Search can be at most {HostDashboardService.MaxSearchLength} characters.");
        if (!ModelState.IsValid) return false;

        filter = new HostDashboardFilter(query.TripId, status, query.Upcoming, query.DestinationId, query.CityId,
            query.CountryId, query.StartFrom, query.StartTo, query.Search);
        return true;
    }

    private bool TryTravellerFilter(string? search, string? paymentStatus, string? interestStatus, bool? owing, bool? overdue,
        out HostDashboardTravellerFilter filter)
    {
        filter = new HostDashboardTravellerFilter();
        InterestPaymentStatus? payment = null;
        TripInterestStatus? interest = null;
        if (!string.IsNullOrWhiteSpace(paymentStatus))
        {
            if (TryParseName<InterestPaymentStatus>(paymentStatus, out var parsed)) payment = parsed;
            else ModelState.AddModelError(nameof(paymentStatus),
                $"Payment status must be one of: {string.Join(", ", Enum.GetNames<InterestPaymentStatus>())}.");
        }
        if (!string.IsNullOrWhiteSpace(interestStatus))
        {
            if (TryParseName<TripInterestStatus>(interestStatus, out var parsed)) interest = parsed;
            else ModelState.AddModelError(nameof(interestStatus),
                $"Interest status must be one of: {string.Join(", ", Enum.GetNames<TripInterestStatus>())}.");
        }
        if (!ModelState.IsValid) return false;

        filter = new HostDashboardTravellerFilter(payment, interest, owing, overdue, search);
        return true;
    }

    private bool TrySort(string? sort, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(sort) || allowed.Contains(sort.Trim(), StringComparer.OrdinalIgnoreCase)) return true;
        ModelState.AddModelError(nameof(sort), $"Sort must be one of: {string.Join(", ", allowed)}.");
        return false;
    }

    // Names only (no numbers), case-insensitive.
    private static bool TryParseName<TEnum>(string value, out TEnum parsed) where TEnum : struct, Enum =>
        Enum.TryParse(value.Trim(), ignoreCase: true, out parsed) && Enum.IsDefined(parsed) && !int.TryParse(value.Trim(), out _);
}
