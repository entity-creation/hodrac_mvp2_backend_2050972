using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Jobs;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Services.Newsletter;

/// <summary>Auto-generated content for one issue, plus what went into it.</summary>
public record NewsletterDraft(
    string Subject,
    string PreviewText,
    string BodyMarkdown,
    DateTime ContentSinceUtc,
    int BlogPostCount,
    int WishlistCount,
    int EventCount);

public enum NewsletterGenerateOutcome
{
    Created,
    Regenerated,
    /// <summary>An issue exists for that date and the caller did not ask to regenerate.</summary>
    AlreadyExists,
    /// <summary>An issue exists but is past Draft (approved, sending, sent, or cancelled mid-send).</summary>
    NotEditable,
}

/// <summary>
/// Builds the weekly issue from stored data only (no invented facts):
///  - blog posts that became visible (BlogQueries.Visible) since the previous Sent issue;
///  - template wishlists created since then (Wishlist.CreatedAt);
///  - published events starting in the next <see cref="NewsletterOptions.UpcomingEventDays"/> days;
///  - a short "New to Hodrac?" section.
/// "Since" is the previous Sent issue's SentAt, or the last
/// <see cref="NewsletterOptions.FirstIssueLookbackDays"/> days for the first issue.
/// Destinations are not included: Destination has no created timestamp, so
/// there is no way to tell which ones are new.
/// All fixed wording follows the humanizer rules (plain, specific, no dashes).
/// </summary>
public partial class NewsletterDraftService
{
    public const string DefaultIntroMarkdown =
        "Hodrac is a travel app for planning trips from Nigeria. You can browse destinations and ready-made trip ideas, " +
        "save the ones you like, and join group trips that other travellers have posted. To start, open {site}, " +
        "pick a trip idea and save it so you can come back to it later.";

    private const int MaxSummaryLength = 160;

    private readonly HodracDbContext _db;
    private readonly IShareService _share;
    private readonly IConfiguration _config;
    private readonly NewsletterOptions _options;

    public NewsletterDraftService(HodracDbContext db, IShareService share, IConfiguration config, IOptions<NewsletterOptions> options)
    {
        _db = db;
        _share = share;
        _config = config;
        _options = options.Value;
    }

    private string FrontendBaseUrl => HostedJobSupport.FrontendBaseUrl(_config);

    // ── Persisting ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the Draft issue for <paramref name="issueDate"/>, or (when
    /// <paramref name="regenerate"/>) rebuilds the content of an existing Draft
    /// (or of a Cancelled issue that never started sending, which goes back to Draft).
    /// </summary>
    public async Task<(NewsletterGenerateOutcome Outcome, NewsletterIssue? Issue)> GenerateAsync(
        DateOnly issueDate, DateTime nowUtc, bool regenerate, CancellationToken ct = default)
    {
        var existing = await _db.NewsletterIssues.FirstOrDefaultAsync(i => i.IssueDate == issueDate, ct);
        if (existing is not null)
        {
            if (!regenerate) return (NewsletterGenerateOutcome.AlreadyExists, existing);
            var editable = existing.Status == NewsletterIssueStatus.Draft
                || (existing.Status == NewsletterIssueStatus.Cancelled && existing.SendStartedAt is null);
            if (!editable) return (NewsletterGenerateOutcome.NotEditable, existing);
        }

        var draft = await BuildAsync(issueDate, nowUtc, ct);

        if (existing is not null)
        {
            Apply(existing, draft, issueDate, nowUtc);
            await _db.SaveChangesAsync(ct);
            return (NewsletterGenerateOutcome.Regenerated, existing);
        }

        var issue = new NewsletterIssue { Id = Guid.NewGuid(), IssueDate = issueDate, CreatedAt = nowUtc };
        Apply(issue, draft, issueDate, nowUtc);
        _db.NewsletterIssues.Add(issue);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (HostedJobSupport.IsUniqueViolation(ex))
        {
            // Created concurrently (job and admin at the same moment).
            _db.Entry(issue).State = EntityState.Detached;
            var winner = await _db.NewsletterIssues.FirstAsync(i => i.IssueDate == issueDate, ct);
            return (NewsletterGenerateOutcome.AlreadyExists, winner);
        }
        return (NewsletterGenerateOutcome.Created, issue);
    }

    private void Apply(NewsletterIssue issue, NewsletterDraft draft, DateOnly issueDate, DateTime nowUtc)
    {
        issue.Subject = draft.Subject;
        issue.PreviewText = draft.PreviewText;
        issue.BodyMarkdown = draft.BodyMarkdown;
        issue.Status = NewsletterIssueStatus.Draft;
        issue.ScheduledFor = NewsletterSchedule.SendAtUtc(_options, issueDate);
        issue.ApprovedAt = null;
        issue.ApprovedByUserId = null;
        issue.GeneratedAt = nowUtc;
        issue.UpdatedAt = nowUtc;
    }

    // ── Building ──────────────────────────────────────────────────────────────

    public async Task<NewsletterDraft> BuildAsync(DateOnly issueDate, DateTime nowUtc, CancellationToken ct = default)
    {
        var lastSent = await _db.NewsletterIssues.AsNoTracking()
            .Where(i => i.Status == NewsletterIssueStatus.Sent && i.IssueDate < issueDate)
            .OrderByDescending(i => i.IssueDate)
            .Select(i => i.SentAt ?? i.SendStartedAt)
            .FirstOrDefaultAsync(ct);
        var isFirstIssue = lastSent is null;
        var since = lastSent ?? nowUtc.AddDays(-Math.Max(1, _options.FirstIssueLookbackDays));
        var sinceOffset = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc));
        var nowOffset = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        var take = Math.Max(1, _options.MaxItemsPerSection);

        var posts = await _db.BlogPosts.AsNoTracking()
            .Visible(nowUtc)
            .Where(p => p.PublishedAt > since)
            .OrderByDescending(p => p.PublishedAt)
            .Take(take)
            .Select(p => new { p.Title, p.Slug, p.Excerpt, p.MetaDescription })
            .ToListAsync(ct);

        var wishlists = await _db.Wishlists.AsNoTracking()
            .Where(w => w.IsTemplate && w.CreatedAt > sinceOffset && w.CreatedAt <= nowOffset)
            .OrderByDescending(w => w.CreatedAt)
            .Take(take)
            .Select(w => new { w.WishlistId, w.WishlistName, w.ShortStory, w.WishlistDescription, w.TotalDays })
            .ToListAsync(ct);

        var horizon = nowUtc.AddDays(Math.Max(1, _options.UpcomingEventDays));
        var events = await _db.Events.AsNoTracking()
            .Published()
            .Upcoming(nowUtc)
            .Where(e => e.StartsAt <= horizon)
            .OrderBy(e => e.StartsAt)
            .Take(take)
            .Select(e => new { e.Name, e.Slug, e.Description, e.StartsAt, e.EndsAt, e.TimeZone, CityName = e.City.CityName })
            .ToListAsync(ct);

        var hasNew = posts.Count > 0 || wishlists.Count > 0;
        var md = new StringBuilder();
        md.Append("Hi,\n\n");
        if (hasNew)
            md.Append(isFirstIssue
                ? "Here's what's new on Hodrac from the past week.\n\n"
                : "Here's what's new on Hodrac since our last email.\n\n");
        else
            md.Append("Nothing new went up on Hodrac this week, so this one is short.\n\n");

        if (posts.Count > 0)
        {
            md.Append("## New on the blog\n\n");
            foreach (var p in posts)
                AppendItem(md, p.Title, BlogUrl(p.Slug), Summary(NonEmpty(p.Excerpt) ?? p.MetaDescription));
        }

        if (wishlists.Count > 0)
        {
            md.Append("## New trip ideas\n\n");
            foreach (var w in wishlists)
            {
                var story = Summary(NonEmpty(w.ShortStory) ?? w.WishlistDescription);
                var days = w.TotalDays > 0 ? $"{w.TotalDays} day{(w.TotalDays == 1 ? "" : "s")}." : null;
                var line = days is null ? story : story is null ? days : $"{days} {story}";
                AppendItem(md, w.WishlistName, _share.BuildFrontendUrl(ShareTypes.Wishlist, w.WishlistId.ToString()), line);
            }
        }

        if (events.Count > 0)
        {
            md.Append($"## Events in the next {_options.UpcomingEventDays} days\n\n");
            foreach (var e in events)
            {
                var when = EventDates(e.StartsAt, e.EndsAt, e.TimeZone);
                var where = NonEmpty(e.CityName);
                var head = where is null ? $"{when}." : $"{when}, {where}.";
                var description = Summary(e.Description);
                AppendItem(md, e.Name, EventUrl(e.Slug), description is null ? head : $"{head} {description}");
            }
        }

        md.Append("## New to Hodrac?\n\n");
        md.Append(IntroMarkdown()).Append("\n\n");
        md.Append("The Hodrac team\n");

        var subject = Subject(issueDate,
            posts.FirstOrDefault()?.Title ?? wishlists.FirstOrDefault()?.WishlistName,
            events.FirstOrDefault()?.Name);
        var preview = PreviewText(posts.Count, wishlists.Count, events.Count);

        return new NewsletterDraft(subject, preview, md.ToString(), since, posts.Count, wishlists.Count, events.Count);
    }

    private string IntroMarkdown()
    {
        var site = SiteLink();
        var text = NonEmpty(_options.IntroMarkdown) ?? DefaultIntroMarkdown;
        return text.Replace("{site}", site);
    }

    private string SiteLink()
    {
        var host = Uri.TryCreate(FrontendBaseUrl, UriKind.Absolute, out var uri) ? uri.Host : FrontendBaseUrl;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        return $"[{EscapeMarkdown(host)}]({FrontendBaseUrl})";
    }

    private string Subject(DateOnly issueDate, string? leadNew, string? leadEvent)
    {
        var subject = leadNew is not null ? $"New on Hodrac: {Clean(leadNew)}"
            : leadEvent is not null ? $"Coming up: {Clean(leadEvent)}"
            : $"Hodrac weekly, {issueDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        return Cut(subject, NewsletterIssue.MaxSubjectLength);
    }

    private string PreviewText(int posts, int wishlists, int events)
    {
        var parts = new List<string>();
        if (posts > 0) parts.Add($"{posts} new article{(posts == 1 ? "" : "s")}");
        if (wishlists > 0) parts.Add($"{wishlists} new trip idea{(wishlists == 1 ? "" : "s")}");
        if (events > 0) parts.Add($"{events} event{(events == 1 ? "" : "s")} in the next {_options.UpcomingEventDays} days");

        var text = parts.Count switch
        {
            0 => "How to start planning a trip on Hodrac.",
            1 => parts[0] + ".",
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1] + ".",
        };
        return Cut(char.ToUpperInvariant(text[0]) + text[1..], NewsletterIssue.MaxPreviewTextLength);
    }

    // ── Links ─────────────────────────────────────────────────────────────────

    public string BlogUrl(string slug) => FrontendUrl(_options.BlogPostPath, slug);

    public string EventUrl(string slug) => FrontendUrl(_options.EventPath, slug);

    private string FrontendUrl(string template, string slug)
    {
        var path = template.Replace("{slug}", Uri.EscapeDataString(slug));
        if (!path.StartsWith('/')) path = "/" + path;
        return FrontendBaseUrl + path;
    }

    // ── Text helpers ──────────────────────────────────────────────────────────

    private static void AppendItem(StringBuilder md, string title, string url, string? summary)
    {
        md.Append("- [").Append(EscapeMarkdown(Clean(title))).Append("](").Append(url).Append(')');
        if (!string.IsNullOrWhiteSpace(summary))
            md.Append("  \n  ").Append(EscapeLineStart(EscapeMarkdown(summary)));
        md.Append("\n\n");
    }

    /// <summary>"Sat 6 Dec" or "Sat 6 Dec to Sun 7 Dec", in the event's time zone (WAT when unknown).</summary>
    private static string EventDates(DateTime startsAtUtc, DateTime? endsAtUtc, string? timeZone)
    {
        var start = ToLocal(startsAtUtc, timeZone);
        var label = start.ToString("ddd d MMM", CultureInfo.InvariantCulture);
        if (endsAtUtc is { } end)
        {
            var endLocal = ToLocal(end, timeZone);
            if (endLocal.Date != start.Date)
                label += " to " + endLocal.ToString("ddd d MMM", CultureInfo.InvariantCulture);
        }
        return label;
    }

    private static DateTime ToLocal(DateTime utc, string? timeZone)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (!string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var tz))
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        return utc.AddHours(1); // WAT
    }

    private static string? Summary(string? text)
    {
        var cleaned = NonEmpty(StripMarkdown(text));
        return cleaned is null ? null : BlogQueries.Snippet(cleaned, MaxSummaryLength);
    }

    /// <summary>Removes embed markers and the most common Markdown so a snippet reads as plain text.</summary>
    private static string? StripMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = EmbedRegex().Replace(text, " ");
        s = MdLinkRegex().Replace(s, "$1");
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        s = HeadingRegex().Replace(s, "");
        return s;
    }

    private static string Clean(string text) => WhitespaceRegex().Replace(text ?? string.Empty, " ").Trim();

    private static string Cut(string text, int max) => text.Length <= max ? text : BlogQueries.Snippet(text, max - 1) ?? text[..max];

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Escapes characters that would change the meaning of stored text inside Markdown.</summary>
    public static string EscapeMarkdown(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '#' or '|' or '!') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A continuation line starting "- ", "+ " or "1. " would open a list; escape that marker.</summary>
    private static string EscapeLineStart(string line)
    {
        var m = ListMarkerRegex().Match(line);
        return m.Success ? line.Insert(m.Groups[1].Index, "\\") : line;
    }

    [GeneratedRegex(@"^\d*([-+.)])(?=\s|$)")]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex EmbedRegex();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MdLinkRegex();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
