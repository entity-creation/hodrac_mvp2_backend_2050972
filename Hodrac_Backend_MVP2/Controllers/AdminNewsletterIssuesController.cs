using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Newsletter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for the weekly newsletter (subscriber endpoints live in AdminNewsletterController).
///
///   GET   /api/admin/newsletter/audience
///   GET   /api/admin/newsletter/issues?status=&amp;page=&amp;pageSize=
///   GET   /api/admin/newsletter/issues/{id}                (incl. previewHtml + delivery counts)
///   POST  /api/admin/newsletter/issues/generate            { issueDate? } create / regenerate a Draft
///   PATCH /api/admin/newsletter/issues/{id}                { subject?, previewText?, bodyMarkdown? }
///   POST  /api/admin/newsletter/issues/{id}/test           send to the calling admin only
///   POST  /api/admin/newsletter/issues/{id}/approve        { scheduledFor? }
///   POST  /api/admin/newsletter/issues/{id}/unapprove
///   POST  /api/admin/newsletter/issues/{id}/cancel
///   POST  /api/admin/newsletter/issues/{id}/send-now
///   GET   /api/admin/newsletter/issues/{id}/deliveries?status=&amp;page=&amp;pageSize=
///
/// Lifecycle: Draft → Approved → Sending → Sent; Draft/Approved/Sending → Cancelled.
/// Editing an Approved issue puts it back to Draft. Only Approved issues are ever sent.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/newsletter")]
public class AdminNewsletterIssuesController : ControllerBase
{
    private readonly HodracDbContext _db;
    private readonly NewsletterDraftService _drafts;
    private readonly NewsletterAudienceService _audience;
    private readonly NewsletterRenderer _renderer;
    private readonly IEmailSender _email;
    private readonly ICurrentUserService _currentUser;
    private readonly NewsletterOptions _options;
    private readonly ILogger<AdminNewsletterIssuesController> _logger;

    public AdminNewsletterIssuesController(
        HodracDbContext db,
        NewsletterDraftService drafts,
        NewsletterAudienceService audience,
        NewsletterRenderer renderer,
        IEmailSender email,
        ICurrentUserService currentUser,
        IOptions<NewsletterOptions> options,
        ILogger<AdminNewsletterIssuesController> logger)
    {
        _db = db;
        _drafts = drafts;
        _audience = audience;
        _renderer = renderer;
        _email = email;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
    }

    // ── Audience ──────────────────────────────────────────────────────────────

    [HttpGet("audience")]
    public async Task<ActionResult<AdminNewsletterAudienceDto>> GetAudience(CancellationToken ct = default)
    {
        var s = await _audience.StatsAsync(ct);
        return Ok(new AdminNewsletterAudienceDto(
            s.ActiveSubscribers, s.UnsubscribedSubscribers, s.OptedInUsers, s.OptedOutUsers, s.TotalUniqueRecipients));
    }

    // ── Issues ────────────────────────────────────────────────────────────────

    [HttpGet("issues")]
    public async Task<ActionResult<AdminPagedResult<AdminNewsletterIssueSummaryDto>>> List(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.NewsletterIssues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<NewsletterIssueStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                ModelState.AddModelError("Status", "Status must be Draft, Approved, Sending, Sent or Cancelled.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(i => i.Status == parsed);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(i => i.IssueDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminNewsletterIssueSummaryDto>(items.Select(ToSummary).ToList(), page, pageSize, total));
    }

    [HttpGet("issues/{id:guid}")]
    public async Task<ActionResult<AdminNewsletterIssueDto>> Get(Guid id, CancellationToken ct = default)
    {
        var issue = await _db.NewsletterIssues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return NotFound();
        return Ok(await ToDetailAsync(issue, ct));
    }

    [HttpPost("issues/generate")]
    public async Task<ActionResult<AdminNewsletterIssueDto>> Generate(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GenerateNewsletterIssueRequest? request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var issueDate = request?.IssueDate ?? NewsletterSchedule.NextIssueDate(_options, now);

        if (!NewsletterSchedule.IsSendDay(_options, issueDate))
            ModelState.AddModelError("IssueDate", $"IssueDate must be a {_options.SendDay}.");
        else if (NewsletterSchedule.SendAtUtc(_options, issueDate) <= now)
            ModelState.AddModelError("IssueDate", "IssueDate must be a future send day.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var (outcome, issue) = await _drafts.GenerateAsync(issueDate, now, regenerate: true, ct);
        if (outcome == NewsletterGenerateOutcome.NotEditable || issue is null)
            return Conflict(new { error = $"The issue for {issueDate:yyyy-MM-dd} is {issue?.Status}; only a Draft can be regenerated." });

        _logger.LogInformation("Admin {Outcome} newsletter issue {IssueId} for {IssueDate:yyyy-MM-dd}", outcome, issue.Id, issueDate);
        var dto = await ToDetailAsync(issue, ct);
        return outcome == NewsletterGenerateOutcome.Created
            ? CreatedAtAction(nameof(Get), new { id = issue.Id }, dto)
            : Ok(dto);
    }

    [HttpPatch("issues/{id:guid}")]
    public async Task<ActionResult<AdminNewsletterIssueDto>> Update(
        Guid id, [FromBody] UpdateNewsletterIssueRequest request, CancellationToken ct = default)
    {
        var issue = await _db.NewsletterIssues.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return NotFound();
        if (issue.Status is not (NewsletterIssueStatus.Draft or NewsletterIssueStatus.Approved))
            return Conflict(new { error = $"A {issue.Status} issue can't be edited." });

        if (request.Subject is not null && string.IsNullOrWhiteSpace(request.Subject))
            ModelState.AddModelError("Subject", "Subject can't be empty.");
        if (request.Subject?.Trim().Length > NewsletterIssue.MaxSubjectLength)
            ModelState.AddModelError("Subject", $"Subject must be at most {NewsletterIssue.MaxSubjectLength} characters.");
        if (request.PreviewText?.Trim().Length > NewsletterIssue.MaxPreviewTextLength)
            ModelState.AddModelError("PreviewText", $"PreviewText must be at most {NewsletterIssue.MaxPreviewTextLength} characters.");
        if (request.BodyMarkdown is not null && string.IsNullOrWhiteSpace(request.BodyMarkdown))
            ModelState.AddModelError("BodyMarkdown", "BodyMarkdown can't be empty.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (request.Subject is not null) issue.Subject = request.Subject.Trim();
        if (request.PreviewText is not null) issue.PreviewText = request.PreviewText.Trim();
        if (request.BodyMarkdown is not null) issue.BodyMarkdown = request.BodyMarkdown;

        // Any edit needs a fresh approval.
        if (issue.Status == NewsletterIssueStatus.Approved) BackToDraft(issue);
        issue.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(await ToDetailAsync(issue, ct));
    }

    [HttpPost("issues/{id:guid}/test")]
    public async Task<ActionResult<NewsletterTestSendResultDto>> SendTest(Guid id, CancellationToken ct = default)
    {
        var issue = await _db.NewsletterIssues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return NotFound();

        var to = _currentUser.Email;
        if (string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "Your account has no email address." });

        try
        {
            await _email.SendHtmlAsync(_renderer.BuildEmail(issue, to, isTest: true), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Newsletter test send failed for issue {IssueId}", id);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "The test email could not be sent. Try again in a minute." });
        }
        return Ok(new NewsletterTestSendResultDto(to));
    }

    [HttpPost("issues/{id:guid}/approve")]
    public async Task<ActionResult<AdminNewsletterIssueDto>> Approve(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveNewsletterIssueRequest? request,
        CancellationToken ct = default)
    {
        var issue = await _db.NewsletterIssues.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return NotFound();
        if (issue.Status is not (NewsletterIssueStatus.Draft or NewsletterIssueStatus.Approved))
            return Conflict(new { error = $"A {issue.Status} issue can't be approved." });

        var now = DateTime.UtcNow;
        DateTime scheduledFor;
        if (request?.ScheduledFor is { } custom)
        {
            scheduledFor = AsUtc(custom);
            if (scheduledFor <= now)
                ModelState.AddModelError("ScheduledFor", "ScheduledFor must be in the future.");
        }
        else
        {
            scheduledFor = NewsletterSchedule.SendAtUtc(_options, issue.IssueDate);
            if (scheduledFor <= now)
                ModelState.AddModelError("ScheduledFor",
                    "This issue's send time has passed. Give a future scheduledFor, or use send-now.");
        }
        ValidateContent(issue);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        Approve(issue, scheduledFor, now);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Admin approved newsletter issue {IssueId} for {ScheduledFor:o}", id, scheduledFor);
        return Ok(await ToDetailAsync(issue, ct));
    }

    [HttpPost("issues/{id:guid}/send-now")]
    public async Task<ActionResult<AdminNewsletterIssueDto>> SendNow(Guid id, CancellationToken ct = default)
    {
        var issue = await _db.NewsletterIssues.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return NotFound();
        if (issue.Status is not (NewsletterIssueStatus.Draft or NewsletterIssueStatus.Approved))
            return Conflict(new { error = $"A {issue.Status} issue can't be sent." });

        ValidateContent(issue);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var now = DateTime.UtcNow;
        Approve(issue, now, now);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Admin approved newsletter issue {IssueId} to send now", id);
        return Ok(await ToDetailAsync(issue, ct));
    }

    [HttpPost("issues/{id:guid}/unapprove")]
    public async Task<ActionResult<AdminNewsletterIssueDto>> Unapprove(Guid id, CancellationToken ct = default)
    {
        var issue = await _db.NewsletterIssues.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return NotFound();
        if (issue.Status != NewsletterIssueStatus.Approved)
            return Conflict(new { error = $"Only an Approved issue can be unapproved (this one is {issue.Status})." });

        BackToDraft(issue);
        issue.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(await ToDetailAsync(issue, ct));
    }

    [HttpPost("issues/{id:guid}/cancel")]
    public async Task<ActionResult<AdminNewsletterIssueDto>> Cancel(Guid id, CancellationToken ct = default)
    {
        var issue = await _db.NewsletterIssues.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return NotFound();
        if (issue.Status is NewsletterIssueStatus.Sent or NewsletterIssueStatus.Cancelled)
            return Conflict(new { error = $"A {issue.Status} issue can't be cancelled." });

        issue.Status = NewsletterIssueStatus.Cancelled;
        issue.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Stops a send in progress: whatever is still Pending is skipped.
        await _db.NewsletterDeliveries
            .Where(d => d.IssueId == id && d.Status == NewsletterDeliveryStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, NewsletterDeliveryStatus.Skipped)
                .SetProperty(d => d.LastError, "Cancelled"), ct);

        _logger.LogInformation("Admin cancelled newsletter issue {IssueId}", id);
        return Ok(await ToDetailAsync(issue, ct));
    }

    [HttpGet("issues/{id:guid}/deliveries")]
    public async Task<ActionResult<AdminPagedResult<AdminNewsletterDeliveryDto>>> Deliveries(
        Guid id,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        if (!await _db.NewsletterIssues.AnyAsync(i => i.Id == id, ct)) return NotFound();

        var query = _db.NewsletterDeliveries.AsNoTracking().Where(d => d.IssueId == id);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<NewsletterDeliveryStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                ModelState.AddModelError("Status", "Status must be Pending, Sent, Failed or Skipped.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(d => d.Status == parsed);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(d => d.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows
            .Select(d => new AdminNewsletterDeliveryDto(d.Id, d.Email, d.UserId, d.Status.ToString(), d.Attempts, d.LastError, d.SentAt))
            .ToList();
        return Ok(new AdminPagedResult<AdminNewsletterDeliveryDto>(items, page, pageSize, total));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ValidateContent(NewsletterIssue issue)
    {
        if (string.IsNullOrWhiteSpace(issue.Subject)) ModelState.AddModelError("Subject", "Subject is required.");
        if (string.IsNullOrWhiteSpace(issue.BodyMarkdown)) ModelState.AddModelError("BodyMarkdown", "BodyMarkdown is required.");
    }

    private void Approve(NewsletterIssue issue, DateTime scheduledFor, DateTime now)
    {
        issue.Status = NewsletterIssueStatus.Approved;
        issue.ScheduledFor = scheduledFor;
        issue.ApprovedAt = now;
        issue.ApprovedByUserId = _currentUser.UserIdString;
        issue.UpdatedAt = now;
    }

    private void BackToDraft(NewsletterIssue issue)
    {
        issue.Status = NewsletterIssueStatus.Draft;
        issue.ApprovedAt = null;
        issue.ApprovedByUserId = null;
        issue.ScheduledFor = NewsletterSchedule.SendAtUtc(_options, issue.IssueDate);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static AdminNewsletterIssueSummaryDto ToSummary(NewsletterIssue i) => new(
        i.Id, i.IssueDate, i.Subject, i.PreviewText, i.Status.ToString(), i.ScheduledFor, i.GeneratedAt,
        i.ApprovedAt, i.SendStartedAt, i.SentAt, i.RecipientCount, i.SentCount, i.FailedCount, i.CreatedAt, i.UpdatedAt);

    private async Task<AdminNewsletterIssueDto> ToDetailAsync(NewsletterIssue i, CancellationToken ct)
    {
        var counts = await _db.NewsletterDeliveries.AsNoTracking()
            .Where(d => d.IssueId == i.Id)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Count(NewsletterDeliveryStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        return new AdminNewsletterIssueDto(
            i.Id, i.IssueDate, i.Subject, i.PreviewText, i.BodyMarkdown, i.Status.ToString(), i.ScheduledFor, i.GeneratedAt,
            i.ApprovedByUserId, i.ApprovedAt, i.SendStartedAt, i.SentAt, i.RecipientCount, i.SentCount, i.FailedCount,
            i.CreatedAt, i.UpdatedAt,
            new AdminNewsletterDeliveryCountsDto(
                Count(NewsletterDeliveryStatus.Pending), Count(NewsletterDeliveryStatus.Sent),
                Count(NewsletterDeliveryStatus.Failed), Count(NewsletterDeliveryStatus.Skipped)),
            _renderer.RenderPreviewHtml(i));
    }
}
