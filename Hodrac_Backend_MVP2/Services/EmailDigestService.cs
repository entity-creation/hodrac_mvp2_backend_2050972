using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hodrac_Backend_MVP2.Services
{
    // Runs on an interval. Email is a FALLBACK for people who are away from
    // the app, never a duplicate of in-app notifications. Rules:
    //   1. Only considers notifications the user hasn't already seen in-app.
    //   2. Skips anyone active in the app more recently than `InactivityWindow`.
    //   3. Batches everything into ONE email per user per run, not one per event.
    //   4. Respects NotificationEmailOptIn.
    public class EmailDigestService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<EmailDigestService> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan InactivityWindow = TimeSpan.FromHours(48);

        public EmailDigestService(IServiceProvider services, ILogger<EmailDigestService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Trip email digest run failed");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            var mailer = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var cutoff = DateTime.UtcNow - InactivityWindow;

            // Group unread notifications by recipient. Replace `Users` /
            // `LastActiveAt` / `NotificationEmailOptIn` with however your
            // existing user table exposes these.
            var candidates = await db.Notifications
                .Where(n => !n.IsRead)
                .GroupBy(n => n.RecipientUserId)
                .Select(g => new { RecipientUserId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            foreach (var candidate in candidates)
            {
                var user = await db.Set<UserForDigest>()
                    .FirstOrDefaultAsync(u => u.Id == candidate.RecipientUserId, ct);

                if (user is null) continue;
                if (!user.NotificationEmailOptIn) continue;
                if (user.LastActiveAt > cutoff) continue; // still active in-app, skip email

                var notifications = await db.Notifications
                    .Where(n => n.RecipientUserId == candidate.RecipientUserId && !n.IsRead)
                    .OrderBy(n => n.CreatedAt)
                    .Take(20)
                    .ToListAsync(ct);

                var summaryLines = notifications
                    .GroupBy(n => n.Type)
                    .Select(g => DescribeGroup(g.Key, g.Count()));

                await mailer.SendAsync(
                    to: user.Email,
                    subject: "Activity on your Hodrac trips",
                    bodyLines: summaryLines.ToList(),
                    deepLink: "https://hodrac.app/notifications");

                _logger.LogInformation("Sent trip digest email to {UserId}", user.Id);
            }
        }

        private static string DescribeGroup(NotificationType type, int count) => type switch
        {
            NotificationType.InterestReceived => $"{count} new {(count == 1 ? "person" : "people")} interested in joining your trip",
            NotificationType.InterestApproved => "You were approved to join a trip",
            NotificationType.InterestDeclined => "A trip request was declined",
            NotificationType.NewThreadMessage => $"{count} new message{(count == 1 ? "" : "s")} in your trip chat",
            _ => "New trip activity"
        };
    }

    // Minimal projection of whatever your real Users table looks like —
    // replace with your actual entity/DbSet.
    public class UserForDigest
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public DateTime LastActiveAt { get; set; }
        public bool NotificationEmailOptIn { get; set; } = true;
    }

    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, List<string> bodyLines, string deepLink);
    }
}
