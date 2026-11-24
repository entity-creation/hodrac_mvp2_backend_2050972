using System.Globalization;
using System.Text;
using Hodrac_Backend_MVP2.DTOs.HostDtos;
using Hodrac_Backend_MVP2.Infrastucture.Http;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// The host's hosted trips. Every {id} route answers 404 unless the caller is
/// the trip's author AND the trip is on the caller's own host profile
/// (community trips and other people's trips look the same: not found).
///
///   GET    /api/host/trips                                   my hosted trips (upcoming first)
///   GET    /api/host/trips/{id}/manifest                     travellers + totals
///   GET    /api/host/trips/{id}/manifest.csv                 same rows as CSV
///   POST   /api/host/trips/{id}/manual-payments              { interestId, amountKobo, method, note? }
///   GET    /api/host/trips/{id}/manual-payments?interestId=  recorded payments, newest first
///   DELETE /api/host/trips/{id}/manual-payments/{paymentId}  undo a mistake (recomputes, logged)
///   GET    /api/host/trips/{id}/whatsapp-nudge/{interestId}  { url, phone, message }
///   POST   /api/host/trips/{id}/travellers/{interestId}/remove  { reason? }: remove an approved
///                                                    traveller with an overdue instalment (409 otherwise)
///   GET    /api/host/reports/weekly?weekStart=yyyy-MM-dd     Mon–Sun WAT (default: last full week)
///
/// Every money change pushes "ManifestUpdated" to the trip-host:{id} SignalR group.
/// </summary>
public partial class HostController
{
    // ══ Trips ═════════════════════════════════════════════════════════════════

    [HttpGet("trips")]
    public async Task<ActionResult<List<HostTripSummaryDto>>> GetMyTrips(CancellationToken ct = default) =>
        Ok(await _hostTrips.ListTripsAsync(_currentUser.UserId, ct));

    // ══ Manifest ══════════════════════════════════════════════════════════════

    [HttpGet("trips/{id:guid}/manifest")]
    public async Task<ActionResult<ManifestDto>> GetManifest(Guid id, CancellationToken ct = default)
    {
        var trip = await _hostTrips.GetOwnedHostedTripAsync(_currentUser.UserId, id, ct);
        if (trip is null) return NotFound();
        return Ok(await _hostTrips.BuildManifestAsync(trip, ct));
    }

    /// <summary>
    /// The manifest rows as CSV (partner-clicks export pattern: UTF-8 with BOM,
    /// RFC 4180 quoting, formula-injection guard). Money columns are naira
    /// with two decimals; dates are ISO 8601 (UTC for times).
    /// </summary>
    [HttpGet("trips/{id:guid}/manifest.csv")]
    public async Task<IActionResult> GetManifestCsv(Guid id, CancellationToken ct = default)
    {
        var trip = await _hostTrips.GetOwnedHostedTripAsync(_currentUser.UserId, id, ct);
        if (trip is null) return NotFound();
        var manifest = await _hostTrips.BuildManifestAsync(trip, ct);

        var csv = new StringBuilder();
        csv.Append("name,email,phone,requestStatus,paymentStatus,termsVersionAccepted,termsAcceptedAt,")
           .Append("paidNgn,viaHodracNgn,manualNgn,owesNgn,nextDueDate,nextDueNgn,seatHoldExpiresAt,lastReminderAt,requestedAt,")
           .Append("overdueNgn").Append(CsvExport.LineEnd);
        foreach (var r in manifest.Rows)
        {
            csv.Append(CsvExport.Field(r.TravellerName)).Append(',')
               .Append(CsvExport.Field(r.Email)).Append(',')
               .Append(CsvExport.Field(r.Phone)).Append(',')
               .Append(CsvExport.Field(r.InterestStatus)).Append(',')
               .Append(CsvExport.Field(r.PaymentStatus)).Append(',')
               .Append(r.TermsVersionAccepted?.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(CsvExport.Time(r.TermsAcceptedAt)).Append(',')
               .Append(CsvExport.Naira(r.PaidKobo)).Append(',')
               .Append(CsvExport.Naira(r.ViaHodracKobo)).Append(',')
               .Append(CsvExport.Naira(r.ManualKobo)).Append(',')
               .Append(CsvExport.Naira(r.OwesKobo)).Append(',')
               .Append(CsvExport.Date(r.NextDueDate)).Append(',')
               .Append(CsvExport.Naira(r.NextDueAmountKobo)).Append(',')
               .Append(CsvExport.Time(r.SeatHoldExpiresAt)).Append(',')
               .Append(CsvExport.Time(r.LastReminderAt)).Append(',')
               .Append(CsvExport.Time(r.RequestedAt)).Append(',')
               .Append(CsvExport.Naira(r.OverdueKobo)).Append(CsvExport.LineEnd);
        }

        var bytes = CsvExport.ToBytes(csv);
        var slug = CsvExport.Slug(manifest.Title, maxLength: 60);
        var fileName = $"manifest-{(slug.Length == 0 ? trip.Id.ToString() : slug)}-{HostTripService.TodayWat(DateTime.UtcNow):yyyy-MM-dd}.csv";

        _logger.LogInformation("User {UserId} exported the manifest of trip {TripId}", _currentUser.UserId, id);
        return File(bytes, CsvExport.ContentType, fileName);
    }

    // ══ Manual payments and refunds ═══════════════════════════════════════════

    [HttpPost("trips/{id:guid}/manual-payments")]
    public async Task<ActionResult<ManualPaymentResultDto>> RecordManualPayment(
        Guid id, RecordManualPaymentRequest request, CancellationToken ct = default)
    {
        var trip = await _hostTrips.GetOwnedHostedTripAsync(_currentUser.UserId, id, ct);
        if (trip is null) return NotFound();

        if (!Enum.TryParse<ManualPaymentMethod>(request.Method, ignoreCase: true, out var method)
            || !Enum.IsDefined(method) || int.TryParse(request.Method, out _))
        {
            ModelState.AddModelError(nameof(request.Method), "Method must be Transfer, Cash, Other or Refund.");
            return ValidationProblem(ModelState);
        }

        var result = await _payments.RecordManualPaymentAsync(
            id, request.InterestId, request.AmountKobo, method, request.Note, _currentUser.UserId, ct);
        return ManualPaymentResponse(result, nameof(request.AmountKobo));
    }

    [HttpGet("trips/{id:guid}/manual-payments")]
    public async Task<ActionResult<List<ManualPaymentDto>>> GetManualPayments(Guid id, [FromQuery] Guid? interestId, CancellationToken ct = default)
    {
        var trip = await _hostTrips.GetOwnedHostedTripAsync(_currentUser.UserId, id, ct);
        if (trip is null) return NotFound();

        var payments = await _db.ManualPayments.AsNoTracking()
            .Where(m => m.TripInterest!.TripPostId == id && (interestId == null || m.TripInterestId == interestId))
            .OrderByDescending(m => m.RecordedAt)
            .ToListAsync(ct);
        return Ok(payments.Select(ToDto).ToList());
    }

    /// <summary>
    /// Any manual payment on the host's own trip can be deleted (no time limit
    /// in v1); the interest is recomputed and the deletion is logged.
    /// </summary>
    [HttpDelete("trips/{id:guid}/manual-payments/{paymentId:guid}")]
    public async Task<ActionResult<ManualPaymentResultDto>> DeleteManualPayment(Guid id, Guid paymentId, CancellationToken ct = default)
    {
        var trip = await _hostTrips.GetOwnedHostedTripAsync(_currentUser.UserId, id, ct);
        if (trip is null) return NotFound();

        var result = await _payments.DeleteManualPaymentAsync(id, paymentId, _currentUser.UserId, ct);
        return ManualPaymentResponse(result, null);
    }

    private ActionResult<ManualPaymentResultDto> ManualPaymentResponse(ManualPaymentResult result, string? field)
    {
        switch (result.Outcome)
        {
            case ManualPaymentOutcome.NotFound:
                return NotFound();
            case ManualPaymentOutcome.Invalid:
                ModelState.AddModelError(field ?? "AmountKobo", result.Error ?? "Invalid payment.");
                return ValidationProblem(ModelState);
            case ManualPaymentOutcome.Conflict:
                return Conflict(new { error = result.Error });
        }

        var interest = result.Interest!;
        return Ok(new ManualPaymentResultDto(
            ToDto(result.Payment!),
            interest.AmountPaidKobo,
            interest.AmountPaidViaHodracKobo,
            interest.PaymentStatus.ToString(),
            interest.SeatHoldExpiresAt));
    }

    private static ManualPaymentDto ToDto(ManualPayment m) =>
        new(m.Id, m.TripInterestId, m.AmountKobo, m.Method.ToString(), m.Note, m.RecordedByUserId, m.RecordedAt);

    // ══ WhatsApp nudge ════════════════════════════════════════════════════════

    [HttpGet("trips/{id:guid}/whatsapp-nudge/{interestId:guid}")]
    public async Task<ActionResult<WhatsAppNudgeDto>> GetWhatsAppNudge(Guid id, Guid interestId, CancellationToken ct = default)
    {
        var trip = await _hostTrips.GetOwnedHostedTripAsync(_currentUser.UserId, id, ct);
        if (trip is null) return NotFound();

        var manifest = await _hostTrips.BuildManifestAsync(trip, ct);
        var row = manifest.Rows.FirstOrDefault(r => r.InterestId == interestId);
        if (row is null) return NotFound();
        if (row.OwesKobo <= 0)
            return Conflict(new { error = "This traveller doesn't owe anything right now." });

        var nudge = _hostTrips.BuildWhatsAppNudge(trip, manifest.Title, row);
        if (nudge is null)
            return UnprocessableEntity(new { error = "This traveller hasn't shared a phone number we can use for WhatsApp." });
        return Ok(nudge);
    }

    // ══ Remove a traveller who missed a payment deadline ══════════════════════

    /// <summary>
    /// Only when the traveller is Approved and an instalment (never just the
    /// deposit) is unpaid past its due date (WAT; the traveller's own dates
    /// on TravellerChooses trips), else 409. Payments are kept; nothing is
    /// refunded automatically. Allowed for hosts closed by Hodrac (settling
    /// an existing booking). 404 unless it's the caller's own hosted trip.
    /// </summary>
    [HttpPost("trips/{id:guid}/travellers/{interestId:guid}/remove")]
    public async Task<ActionResult<RemoveTravellerResultDto>> RemoveTraveller(
        Guid id, Guid interestId, RemoveTravellerRequest? request,
        [FromServices] TravellerRemovalService removals, CancellationToken ct = default)
    {
        var trip = await _hostTrips.GetOwnedHostedTripAsync(_currentUser.UserId, id, ct);
        if (trip is null) return NotFound();

        var result = await removals.RemoveAsync(id, interestId, request?.Reason, _currentUser.UserId, ct);
        switch (result.Outcome)
        {
            case RemoveTravellerOutcome.NotFound:
                return NotFound();
            case RemoveTravellerOutcome.Invalid:
                ModelState.AddModelError("reason", result.Error ?? "Invalid reason.");
                return ValidationProblem(ModelState);
            case RemoveTravellerOutcome.Conflict:
                return Conflict(new { error = result.Error });
            default:
                return Ok(result.Removed);
        }
    }

    // ══ Weekly report ═════════════════════════════════════════════════════════

    /// <summary>weekStart (yyyy-MM-dd) is moved back to its Monday; default: the last full Mon–Sun week in WAT.</summary>
    [HttpGet("reports/weekly")]
    public async Task<ActionResult<HostWeeklyReportDto>> GetWeeklyReport([FromQuery] DateOnly? weekStart, CancellationToken ct = default)
    {
        var host = await _hosts.GetByUserAsync(_currentUser.UserId, ct);
        if (host is null) return NotFound();

        var report = await _reports.BuildWeeklyAsync(host.Id, weekStart, ct);
        return report is null ? NotFound() : Ok(report);
    }
}
