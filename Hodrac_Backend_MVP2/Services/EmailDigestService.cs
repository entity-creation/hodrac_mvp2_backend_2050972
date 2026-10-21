using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hodrac_Backend_MVP2.Services
{
    // Runs on an interval. Email is a FALLBACK for people who are away from
    // the app, never a duplicate of in-app notifications. Rules:
    //   1. Only considers notifications the user hasn't already seen in-app
    //      and that haven't already been emailed (Notification.EmailedAt).
    //   2. Skips anyone active in the app more recently than `InactivityWindow`.
    //   3. Batches everything into ONE email per user per run, not one per event.
    //   4. Respects NotificationEmailOptIn.
    public class EmailDigestService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<EmailDigestService> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan InactivityWindow = TimeSpan.FromHours(48);
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
        private const int MaxNotificationsPerEmail = 100;

        public EmailDigestService(IServiceProvider services, ILogger<EmailDigestService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Short grace period so startup (migrations, warm-up) isn't competing
                // with the first run; afterwards run every RunInterval.
                await Task.Delay(InitialDelay, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await RunOnceAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Trip email digest run failed");
                    }

                    await Task.Delay(RunInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutting down.
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            var mailer = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var baseUrl = (config["Frontend:BaseUrl"] ?? "https://www.hodrac.com").TrimEnd('/');
            var deepLink = $"{baseUrl}/notifications";
            var cutoff = DateTimeOffset.UtcNow - InactivityWindow;

            // Recipients with at least one notification that is unread in-app
            // AND has not already been included in a digest.
            var recipientIds = await db.Notifications
                .Where(n => !n.IsRead && n.EmailedAt == null)
                .Select(n => n.RecipientUserId)
                .Distinct()
                .ToListAsync(ct);

            if (recipientIds.Count == 0) return;

            // Notification.RecipientUserId is a Guid; ApplicationUser.Id is the same
            // Guid stored as a string. Match on both casings to be safe, then map
            // back to the Guid in memory.
            var idStrings = recipientIds
                .SelectMany(g => new[] { g.ToString("D"), g.ToString("D").ToUpperInvariant() })
                .ToList();

            var users = await db.Users
                .AsNoTracking()
                .Where(u => idStrings.Contains(u.Id))
                .Where(u => u.Email != null && u.Email != "")
                .Where(u => u.NotificationEmailOptIn)
                // Skip anyone active in-app within the window (fall back to last
                // login for users who haven't been seen since LastActiveAt existed).
                .Where(u => (u.LastActiveAt ?? u.LastLoginAt) == null
                         || (u.LastActiveAt ?? u.LastLoginAt) <= cutoff)
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(ct);

            var sent = 0;
            foreach (var user in users)
            {
                if (!Guid.TryParse(user.Id, out var recipientGuid)) continue;

                try
                {
                    var notifications = await db.Notifications
                        .AsNoTracking()
                        .Where(n => n.RecipientUserId == recipientGuid && !n.IsRead && n.EmailedAt == null)
                        .OrderBy(n => n.CreatedAt)
                        .Take(MaxNotificationsPerEmail)
                        .Select(n => new { n.Id, n.Type })
                        .ToListAsync(ct);

                    if (notifications.Count == 0) continue;

                    var summaryLines = notifications
                        .GroupBy(n => n.Type)
                        .Select(g => DescribeGroup(g.Key, g.Count()))
                        .ToList();

                    await mailer.SendAsync(
                        to: user.Email!,
                        subject: "Activity on your Hodrac trips",
                        bodyLines: summaryLines,
                        deepLink: deepLink);

                    // Mark exactly the notifications that were summarised in this email.
                    var includedIds = notifications.Select(n => n.Id).ToList();
                    var emailedAt = DateTime.UtcNow;
                    await db.Notifications
                        .Where(n => includedIds.Contains(n.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(n => n.EmailedAt, emailedAt), ct);

                    sent++;
                    _logger.LogInformation(
                        "Sent trip digest email to {UserId} ({Count} notifications)",
                        user.Id, includedIds.Count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad address/SMTP hiccup must not stop the rest of the run;
                    // EmailedAt stays null so this user is retried next run.
                    _logger.LogError(ex, "Failed to send trip digest email to {UserId}", user.Id);
                }
            }

            _logger.LogInformation("Trip email digest run complete: {Sent} email(s) sent", sent);
        }

        private static string DescribeGroup(NotificationType type, int count) => type switch
        {
            NotificationType.InterestReceived => $"{count} new {(count == 1 ? "person" : "people")} interested in joining your trip",
            NotificationType.InterestApproved => "You were approved to join a trip",
            NotificationType.InterestDeclined => "A trip request was declined",
            NotificationType.TripCancelled => $"{count} trip{(count == 1 ? " you were part of was" : "s you were part of were")} cancelled",
            NotificationType.NewThreadMessage => $"{count} new message{(count == 1 ? "" : "s")} in your trip chat",
            _ => "New trip activity"
        };
    }

    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, List<string> bodyLines, string deepLink);
    }
}
