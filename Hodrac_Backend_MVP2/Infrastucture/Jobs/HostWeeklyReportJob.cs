using System.Globalization;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.HostDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastructure.Jobs;

/// <summary>
/// Mondays 08:00 WAT (scheduled daily at 07:00 UTC; it only acts Monday to
/// Wednesday WAT, so a Monday missed through downtime is caught up within two
/// days). Emails every host with at least one hosted trip the report for the
/// last full Mon–Sun week (<see cref="HostReportService.BuildWeeklyAsync"/>)
/// with a link to the host dashboard.
///
/// Idempotent per host per week: a <see cref="HostReportLog"/> row (unique on
/// host + week) is inserted before sending; a unique violation means it was
/// already sent. If the email fails the row is removed so the next run retries.
/// Never includes payout or document data. Per-host failures are logged and skipped.
/// </summary>
public class HostWeeklyReportJob
{
    private readonly HodracDbContext _db;
    private readonly HostReportService _reports;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<HostWeeklyReportJob> _logger;

    public HostWeeklyReportJob(
        HodracDbContext db,
        HostReportService reports,
        IEmailSender email,
        IConfiguration config,
        ILogger<HostWeeklyReportJob> logger)
    {
        _db = db;
        _reports = reports;
        _email = email;
        _config = config;
        _logger = logger;
    }

    /// <summary>Monday to Wednesday (WAT): Monday's run plus two catch-up days.</summary>
    public static bool IsSendDay(DateTime utcNow) =>
        HostTripService.TodayWat(utcNow).DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday;

    public Task<int> RunAsync(CancellationToken ct) => RunAsync(DateTime.UtcNow, ct);

    /// <summary>Sends the last full week's reports not sent yet; returns how many were sent.</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (!IsSendDay(nowUtc)) return 0;

        var weekStart = HostReportService.LastFullWeekStart(nowUtc);
        var hosts = await _db.HostProfiles.AsNoTracking()
            .Where(h => h.ClosedAt == null // closed by Hodrac: no more reports
                && _db.TripPosts.Any(t => t.IsHostedTrip && t.HostProfileId == h.Id && t.AuthorUserId == h.UserId)
                && !_db.HostReportLogs.Any(l => l.HostProfileId == h.Id && l.WeekStart == weekStart))
            .Select(h => new { h.Id, h.UserId })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var host in hosts)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await SendAsync(host.Id, host.UserId, weekStart, nowUtc, ct)) sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Weekly report failed for host {HostProfileId}", host.Id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation("Host weekly reports for week of {WeekStart:yyyy-MM-dd}: {Sent} sent", weekStart, sent);
        return sent;
    }

    private async Task<bool> SendAsync(Guid hostProfileId, Guid hostUserId, DateOnly weekStart, DateTime nowUtc, CancellationToken ct)
    {
        var userId = hostUserId.ToString();
        var to = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(to)) return false;

        var report = await _reports.BuildWeeklyAsync(hostProfileId, weekStart, ct);
        if (report is null) return false;

        // Claim the week first: a concurrent run hits the unique index.
        var log = new HostReportLog { HostProfileId = hostProfileId, WeekStart = weekStart, SentAt = nowUtc };
        _db.HostReportLogs.Add(log);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (HostedJobSupport.IsUniqueViolation(ex))
        {
            return false; // already sent
        }
        _db.ChangeTracker.Clear();

        var (subject, lines) = BuildEmail(report);
        var link = $"{HostedJobSupport.FrontendBaseUrl(_config)}/host";
        if (await HostedJobSupport.TrySendAsync(_email, _logger, to, subject, lines, link))
            return true;

        // Not sent: release the claim so the next run retries.
        await _db.HostReportLogs.Where(l => l.Id == log.Id).ExecuteDeleteAsync(ct);
        return false;
    }

    public static (string Subject, List<string> Lines) BuildEmail(HostWeeklyReportDto report)
    {
        var range = $"{Fmt(report.WeekStart, "d MMM")} – {Fmt(report.WeekEnd, "d MMM yyyy")}";
        var lines = new List<string>
        {
            $"Here's how your hosted trips did from Monday {Fmt(report.WeekStart, "d MMM")} to Sunday {Fmt(report.WeekEnd, "d MMM yyyy")} (WAT).",
        };

        foreach (var trip in report.Trips)
            lines.Add($"{trip.Title}{HostedJobSupport.DateRange(trip.StartDate, null)}: {Summary(trip.Stats)}");

        if (report.Trips.Count > 1)
            lines.Add($"All trips: {Summary(report.Totals)}");

        if (report.Totals.HodracSharePercentAllTime is { } share)
            lines.Add($"Seat money paid through Hodrac so far: {share.ToString("0.#", CultureInfo.InvariantCulture)}%.");

        lines.Add("See the full numbers and your traveller manifests on your host dashboard.");
        return ($"Your Hodrac week: {range}", lines);
    }

    private static string Summary(WeeklyReportStatsDto s)
    {
        var parts = new List<string>
        {
            Count(s.PageViews, "page view"),
            Count(s.JoinRequests, "join request"),
            Count(s.Approvals, "approval"),
            Count(s.SeatsPaid, "new paid seat"),
            $"{PaymentService.Naira(s.CollectedViaHodracKobo + s.CollectedManualKobo)} collected ({PaymentService.Naira(s.CollectedViaHodracKobo)} through Hodrac)",
        };
        if (s.RefundedManualKobo > 0)
            parts.Add($"{PaymentService.Naira(s.RefundedManualKobo)} refunded");
        return string.Join(", ", parts) + ".";
    }

    private static string Count(int n, string noun) =>
        $"{n.ToString("N0", CultureInfo.InvariantCulture)} {noun}{(n == 1 ? "" : "s")}";

    private static string Fmt(DateOnly day, string format) => day.ToString(format, CultureInfo.InvariantCulture);
}
