namespace Hodrac_Backend_MVP2.Services.Newsletter;

// ─── Weekly newsletter configuration ──────────────────────────────────────────
// Bound from the "Newsletter" section (env vars Newsletter__SendDay, ...).
// Defaults match the approved design, so nothing needs to be in appsettings.
// All times are WAT (UTC+1, no daylight saving).

public class NewsletterOptions
{
    public const string SectionName = "Newsletter";

    /// <summary>Day the issue goes out (WAT).</summary>
    public DayOfWeek SendDay { get; set; } = DayOfWeek.Friday;

    /// <summary>Time of day (WAT) the issue goes out once approved. "10:00".</summary>
    public TimeSpan SendTimeWat { get; set; } = TimeSpan.FromHours(10);

    /// <summary>Day the draft for the coming send day is generated (WAT).</summary>
    public DayOfWeek DraftDay { get; set; } = DayOfWeek.Wednesday;

    /// <summary>Time of day (WAT) the draft is generated. "09:00".</summary>
    public TimeSpan DraftTimeWat { get; set; } = TimeSpan.FromHours(9);

    /// <summary>WAT offset from UTC.</summary>
    public double UtcOffsetHours { get; set; } = 1;

    /// <summary>Deliveries attempted per sender run (the sender runs every 5 minutes).</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>A delivery is marked Failed after this many failed attempts.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Content window for the first issue (no earlier Sent issue).</summary>
    public int FirstIssueLookbackDays { get; set; } = 7;

    /// <summary>Events starting within this many days are listed.</summary>
    public int UpcomingEventDays { get; set; } = 30;

    /// <summary>At most this many items per section.</summary>
    public int MaxItemsPerSection { get; set; } = 6;

    // Frontend paths (appended to Frontend:BaseUrl). Destination / wishlist /
    // trip links use ShareService (Share:Paths:*).
    public string BlogPostPath { get; set; } = "/blog/{slug}";
    public string EventPath { get; set; } = "/events/{slug}";
    public string AdminIssuePath { get; set; } = "/admin/newsletter/{id}";

    /// <summary>
    /// Public API base for the unsubscribe link. Empty = Api:PublicBaseUrl,
    /// then https://api.hodrac.com.
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// Markdown for the "New to Hodrac?" section. Empty = the built-in text
    /// (<see cref="NewsletterDraftService.DefaultIntroMarkdown"/>).
    /// </summary>
    public string? IntroMarkdown { get; set; }

    public TimeSpan UtcOffset => TimeSpan.FromHours(UtcOffsetHours);
}

/// <summary>Send/draft times for a weekly issue. Pure functions of the options.</summary>
public static class NewsletterSchedule
{
    /// <summary>UTC instant the issue for <paramref name="issueDate"/> goes out by default (10:00 WAT = 09:00 UTC).</summary>
    public static DateTime SendAtUtc(NewsletterOptions o, DateOnly issueDate) =>
        DateTime.SpecifyKind(issueDate.ToDateTime(TimeOnly.MinValue) + o.SendTimeWat - o.UtcOffset, DateTimeKind.Utc);

    /// <summary>Days between the draft day and the send day (Wed → Fri = 2).</summary>
    public static int DraftLeadDays(NewsletterOptions o)
    {
        var days = ((int)o.SendDay - (int)o.DraftDay + 7) % 7;
        return days == 0 && o.DraftTimeWat >= o.SendTimeWat ? 7 : days;
    }

    /// <summary>UTC instant the draft for <paramref name="issueDate"/> is due (Wednesday 09:00 WAT = 08:00 UTC).</summary>
    public static DateTime DraftDueUtc(NewsletterOptions o, DateOnly issueDate) =>
        DateTime.SpecifyKind(issueDate.AddDays(-DraftLeadDays(o)).ToDateTime(TimeOnly.MinValue) + o.DraftTimeWat - o.UtcOffset, DateTimeKind.Utc);

    /// <summary>The next send day (WAT) whose send time is still ahead of <paramref name="nowUtc"/>.</summary>
    public static DateOnly NextIssueDate(NewsletterOptions o, DateTime nowUtc)
    {
        var todayLocal = DateOnly.FromDateTime(nowUtc + o.UtcOffset);
        var candidate = todayLocal.AddDays(((int)o.SendDay - (int)todayLocal.DayOfWeek + 7) % 7);
        return SendAtUtc(o, candidate) > nowUtc ? candidate : candidate.AddDays(7);
    }

    public static bool IsSendDay(NewsletterOptions o, DateOnly date) => date.DayOfWeek == o.SendDay;
}
