using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Newsletter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Infrastructure.Jobs;

/// <summary>
/// Weekly draft. Runs every 15 minutes (IntervalJobsService) and acts once the
/// draft time for the coming send day has passed (Wednesday 09:00 WAT for
/// Friday by default), so a slot missed through downtime is caught up on the
/// next run. Creates the Draft issue for that Friday only when none exists
/// (any status), then emails every address in Admin:Emails
/// "Newsletter draft ready to review" with a link to the admin page.
/// Idempotent: IssueDate is unique and an existing issue is never touched.
/// </summary>
public class NewsletterDraftJob
{
    private readonly NewsletterDraftService _drafts;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly NewsletterOptions _options;
    private readonly ILogger<NewsletterDraftJob> _logger;

    public NewsletterDraftJob(
        NewsletterDraftService drafts,
        IEmailSender email,
        IConfiguration config,
        IOptions<NewsletterOptions> options,
        ILogger<NewsletterDraftJob> logger)
    {
        _drafts = drafts;
        _email = email;
        _config = config;
        _options = options.Value;
        _logger = logger;
    }

    public Task<NewsletterIssue?> RunAsync(CancellationToken ct) => RunAsync(DateTime.UtcNow, ct);

    /// <summary>The issue it created, or null when nothing was due or it already existed.</summary>
    public async Task<NewsletterIssue?> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var issueDate = NewsletterSchedule.NextIssueDate(_options, nowUtc);
        if (nowUtc < NewsletterSchedule.DraftDueUtc(_options, issueDate)) return null;

        var (outcome, issue) = await _drafts.GenerateAsync(issueDate, nowUtc, regenerate: false, ct);
        if (outcome != NewsletterGenerateOutcome.Created || issue is null) return null;

        _logger.LogInformation("Newsletter draft {IssueId} created for {IssueDate:yyyy-MM-dd}", issue.Id, issueDate);
        await EmailAdminsAsync(issue, ct);
        return issue;
    }

    private async Task EmailAdminsAsync(NewsletterIssue issue, CancellationToken ct)
    {
        var admins = (_config["Admin:Emails"] ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (admins.Count == 0) return;

        var sendAt = HostedJobSupport.WatTime(issue.ScheduledFor);
        var lines = new List<string>
        {
            $"The newsletter for {HostedJobSupport.Day(issue.IssueDate)} is drafted and waiting for review.",
            $"Subject: {issue.Subject}",
            $"It goes out at {sendAt} only if an admin approves it. Until then it stays a draft and nobody gets it.",
        };
        var link = HostedJobSupport.FrontendBaseUrl(_config)
                   + AdminPath(_options.AdminIssuePath, issue.Id);

        foreach (var admin in admins)
        {
            ct.ThrowIfCancellationRequested();
            await HostedJobSupport.TrySendAsync(_email, _logger, admin, "Newsletter draft ready to review", lines, link);
        }
    }

    internal static string AdminPath(string template, Guid id)
    {
        var path = template.Replace("{id}", id.ToString());
        return path.StartsWith('/') ? path : "/" + path;
    }
}

public record NewsletterSendRunResult(int IssuesStarted, int Attempted, int Sent, int Failed, int IssuesCompleted);

/// <summary>
/// Sends approved issues. Runs every 5 minutes (IntervalJobsService):
///  1. Approved issues with ScheduledFor &lt;= now move to Sending and get their
///     recipients snapshotted into NewsletterDelivery (Pending) in the same
///     transaction (see <see cref="NewsletterAudienceService.RecipientsAsync"/>).
///  2. Up to <see cref="NewsletterOptions.BatchSize"/> Pending deliveries are
///     sent per run (oldest issue first). A failure increments Attempts and
///     stays Pending for the next run; after <see cref="NewsletterOptions.MaxAttempts"/>
///     it is Failed.
///  3. An issue with no Pending deliveries left is marked Sent with its counts.
/// Each delivery is saved right after its send, so a restart resumes where it
/// stopped (a crash between the send and the save can repeat that one email).
/// Single-instance assumption, like the other jobs.
/// </summary>
public class NewsletterSendJob
{
    private readonly HodracDbContext _db;
    private readonly NewsletterAudienceService _audience;
    private readonly NewsletterRenderer _renderer;
    private readonly IEmailSender _email;
    private readonly NewsletterOptions _options;
    private readonly ILogger<NewsletterSendJob> _logger;

    public NewsletterSendJob(
        HodracDbContext db,
        NewsletterAudienceService audience,
        NewsletterRenderer renderer,
        IEmailSender email,
        IOptions<NewsletterOptions> options,
        ILogger<NewsletterSendJob> logger)
    {
        _db = db;
        _audience = audience;
        _renderer = renderer;
        _email = email;
        _options = options.Value;
        _logger = logger;
    }

    public Task<NewsletterSendRunResult> RunAsync(CancellationToken ct) => RunAsync(DateTime.UtcNow, ct);

    public async Task<NewsletterSendRunResult> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var started = 0;
        var due = await _db.NewsletterIssues
            .Where(i => i.Status == NewsletterIssueStatus.Approved && i.ScheduledFor <= nowUtc)
            .OrderBy(i => i.ScheduledFor)
            .ToListAsync(ct);
        foreach (var issue in due)
        {
            await StartAsync(issue, nowUtc, ct);
            started++;
        }
        _db.ChangeTracker.Clear();

        var budget = Math.Max(1, _options.BatchSize);
        var maxAttempts = Math.Max(1, _options.MaxAttempts);
        int attempted = 0, sent = 0, failed = 0, completed = 0;

        var sending = await _db.NewsletterIssues
            .Where(i => i.Status == NewsletterIssueStatus.Sending)
            .OrderBy(i => i.ScheduledFor)
            .ToListAsync(ct);

        foreach (var issue in sending)
        {
            if (budget > 0)
            {
                var batch = await _db.NewsletterDeliveries
                    .Where(d => d.IssueId == issue.Id && d.Status == NewsletterDeliveryStatus.Pending)
                    .OrderBy(d => d.Email)
                    .Take(budget)
                    .ToListAsync(ct);

                foreach (var delivery in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    budget--;
                    attempted++;
                    delivery.Attempts++;
                    try
                    {
                        await _email.SendHtmlAsync(_renderer.BuildEmail(issue, delivery.Email), ct);
                        delivery.Status = NewsletterDeliveryStatus.Sent;
                        delivery.SentAt = DateTime.UtcNow;
                        delivery.LastError = null;
                        sent++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        var error = $"{ex.GetType().Name}: {ex.Message}";
                        delivery.LastError = error.Length <= NewsletterDelivery.MaxLastErrorLength
                            ? error
                            : error[..NewsletterDelivery.MaxLastErrorLength];
                        if (delivery.Attempts >= maxAttempts)
                        {
                            delivery.Status = NewsletterDeliveryStatus.Failed;
                            failed++;
                        }
                        _logger.LogWarning(ex, "Newsletter {IssueId}: send attempt {Attempt} failed for delivery {DeliveryId}",
                            issue.Id, delivery.Attempts, delivery.Id);
                    }
                    await _db.SaveChangesAsync(ct);
                }
            }

            if (await FinishOrCountAsync(issue.Id, nowUtc, ct)) completed++;
        }

        if (attempted > 0 || started > 0 || completed > 0)
            _logger.LogInformation(
                "Newsletter send run: {Started} started, {Attempted} attempted, {Sent} sent, {Failed} failed, {Completed} completed",
                started, attempted, sent, failed, completed);

        return new NewsletterSendRunResult(started, attempted, sent, failed, completed);
    }

    /// <summary>Approved → Sending with the recipient snapshot, in one SaveChanges.</summary>
    private async Task StartAsync(NewsletterIssue issue, DateTime nowUtc, CancellationToken ct)
    {
        var recipients = await _audience.RecipientsAsync(ct);
        foreach (var r in recipients)
        {
            _db.NewsletterDeliveries.Add(new NewsletterDelivery
            {
                Id = Guid.NewGuid(),
                IssueId = issue.Id,
                Email = r.Email,
                UserId = r.UserId,
                Status = NewsletterDeliveryStatus.Pending,
            });
        }

        issue.Status = NewsletterIssueStatus.Sending;
        issue.SendStartedAt = nowUtc;
        issue.RecipientCount = recipients.Count;
        issue.UpdatedAt = nowUtc;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Newsletter {IssueId} started sending to {Count} recipient(s)", issue.Id, recipients.Count);
    }

    /// <summary>Refreshes the counts; marks the issue Sent when nothing is Pending. True when it completed now.</summary>
    private async Task<bool> FinishOrCountAsync(Guid issueId, DateTime nowUtc, CancellationToken ct)
    {
        var counts = await _db.NewsletterDeliveries.AsNoTracking()
            .Where(d => d.IssueId == issueId)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Count(NewsletterDeliveryStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        var sentCount = Count(NewsletterDeliveryStatus.Sent);
        var failedCount = Count(NewsletterDeliveryStatus.Failed);
        var now = DateTime.UtcNow;

        if (Count(NewsletterDeliveryStatus.Pending) > 0)
        {
            await _db.NewsletterIssues
                .Where(i => i.Id == issueId && i.Status == NewsletterIssueStatus.Sending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.SentCount, sentCount)
                    .SetProperty(i => i.FailedCount, failedCount)
                    .SetProperty(i => i.UpdatedAt, now), ct);
            return false;
        }

        var done = await _db.NewsletterIssues
            .Where(i => i.Id == issueId && i.Status == NewsletterIssueStatus.Sending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, NewsletterIssueStatus.Sent)
                .SetProperty(i => i.SentAt, nowUtc)
                .SetProperty(i => i.SentCount, sentCount)
                .SetProperty(i => i.FailedCount, failedCount)
                .SetProperty(i => i.UpdatedAt, now), ct);
        if (done > 0)
            _logger.LogInformation("Newsletter {IssueId} sent: {Sent} delivered, {Failed} failed", issueId, sentCount, failedCount);
        return done > 0;
    }
}
