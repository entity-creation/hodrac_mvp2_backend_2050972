using System.Globalization;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Hosts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hodrac_Backend_MVP2.Infrastructure.Jobs;

/// <summary>
/// Shared bits of the Hodrac Host background jobs (reminders, seat holds,
/// reconcile, weekly report, document purge). All dates shown to people are
/// WAT (UTC+1); all money is ₦ from kobo.
/// </summary>
internal static class HostedJobSupport
{
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static string FrontendBaseUrl(IConfiguration config) =>
        (config["Frontend:BaseUrl"] ?? "https://www.hodrac.com").TrimEnd('/');

    /// <summary>Where a traveller pays for their seat.</summary>
    public static string PayLink(IConfiguration config, Guid tripId) => $"{FrontendBaseUrl(config)}/trips/{tripId}";

    public static string Day(DateOnly day) => day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>"Tue 18 Nov 2026, 3:00 PM WAT".</summary>
    public static string WatTime(DateTime utc) =>
        (utc + HostTripService.Wat).ToString("ddd d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture) + " WAT";

    public static string DateRange(DateOnly? start, DateOnly? end) =>
        start is null ? ""
        : end is null || end == start ? $" ({start.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)})"
        : $" ({start.Value.ToString("d MMM", CultureInfo.InvariantCulture)} – {end.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)})";

    /// <summary>Sends one email; false (logged) when it fails. Never throws except on cancellation.</summary>
    public static async Task<bool> TrySendAsync(
        IEmailSender email, ILogger logger, string to, string subject, List<string> lines, string link)
    {
        try
        {
            await email.SendAsync(to, subject, lines, link);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send job email \"{Subject}\"", subject);
            return false;
        }
    }

    /// <summary>Runs a side effect (notification, SignalR push); failures are logged, never thrown.</summary>
    public static async Task BestEffortAsync(ILogger logger, Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Job side effect failed: {What}", what);
        }
    }
}
