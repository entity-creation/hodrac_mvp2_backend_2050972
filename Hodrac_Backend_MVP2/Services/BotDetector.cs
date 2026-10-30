using System.Text.RegularExpressions;

namespace Hodrac_Backend_MVP2.Services;

// ─── Bot / prefetch detection ─────────────────────────────────────────────────
// Shared by ShareController (preview crawlers get meta tags without a redirect)
// and PartnersController (crawler / prefetch hits on /api/out are logged as bots
// so they don't inflate partner click stats).

public static class BotDetector
{
    // Link-preview crawlers and generic bots.
    private static readonly Regex CrawlerUserAgent = new(
        "facebookexternalhit|facebot|twitterbot|whatsapp|slackbot|linkedinbot|discordbot|telegrambot|"
        + "pinterest|redditbot|applebot|skypeuripreview|vkshare|embedly|iframely|googlebot|bingbot|"
        + "mastodon|snapchat|viber|line/|kakaotalk|bot\\b|crawler|spider|preview",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when the User-Agent looks like a crawler / link-preview fetcher. Empty UA → false.</summary>
    public static bool IsCrawler(string? userAgent)
        => !string.IsNullOrEmpty(userAgent) && CrawlerUserAgent.IsMatch(userAgent);

    /// <summary>True for browser speculative loads (<c>Purpose: prefetch</c> / <c>Sec-Purpose: prefetch</c>).</summary>
    public static bool IsPrefetch(HttpRequest request)
        => request.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
           || request.Headers["Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
           || request.Headers["X-Purpose"].ToString().Contains("preview", StringComparison.OrdinalIgnoreCase)
           || request.Headers["X-Moz"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase);

    /// <summary>Crawler User-Agent or a prefetch request — anything that isn't a person clicking.</summary>
    public static bool IsNonHuman(HttpRequest request)
        => IsCrawler(request.Headers.UserAgent.ToString()) || IsPrefetch(request);
}
