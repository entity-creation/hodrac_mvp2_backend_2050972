using System.Text.RegularExpressions;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// GET /api/share/{type}/{id}  — share-sheet / copy-link data (JSON)
/// GET /share/{type}/{id}      — link-preview page (Open Graph + Twitter tags) that
///                               redirects people to the frontend
/// type: destination | wishlist | trip | shared-wishlist (id = share token)
/// Only publicly viewable items resolve; everything else is 404.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("public-browse")]
public class ShareController : ControllerBase
{
    // Link-preview crawlers. They get the meta tags without a meta-refresh so
    // they don't follow it to the SPA (which has no server-rendered tags);
    // browsers get both the meta refresh and the JS redirect.
    private static readonly Regex CrawlerUserAgent = new(
        "facebookexternalhit|facebot|twitterbot|whatsapp|slackbot|linkedinbot|discordbot|telegrambot|"
        + "pinterest|redditbot|applebot|skypeuripreview|vkshare|embedly|iframely|googlebot|bingbot|"
        + "mastodon|snapchat|viber|line/|kakaotalk|bot\\b|crawler|spider|preview",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IShareService _share;

    public ShareController(IShareService share) => _share = share;

    [HttpGet("api/share/{type}/{id}")]
    public async Task<ActionResult<ShareDataDto>> GetShareData(string type, string id, CancellationToken ct = default)
    {
        var normalized = ShareTypes.Normalize(type);
        if (normalized is null)
            return BadRequest($"type must be one of: {string.Join(", ", ShareTypes.All)}.");

        var info = await _share.ResolveAsync(normalized, id, ct);
        if (info is null) return NotFound();

        return new ShareDataDto(
            Url: info.Url,
            PreviewUrl: _share.BuildPreviewUrl(Request, info.Type, info.Id),
            Title: info.Title,
            Text: info.Text,
            ImageUrl: info.ImageUrl);
    }

    [HttpGet("share/{type}/{id}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Preview(string type, string id, CancellationToken ct = default)
    {
        var normalized = ShareTypes.Normalize(type);
        var info = normalized is null ? null : await _share.ResolveAsync(normalized, id, ct);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        if (info is null)
        {
            Response.Headers.CacheControl = "no-store";
            return new ContentResult
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentType = "text/html; charset=utf-8",
                Content = _share.RenderNotFoundHtml()
            };
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        var isCrawler = !string.IsNullOrEmpty(userAgent) && CrawlerUserAgent.IsMatch(userAgent);

        // Revocable links must stop working promptly, so they aren't cached.
        Response.Headers.CacheControl = info.Type == ShareTypes.SharedWishlist ? "no-store" : "public, max-age=300";
        Response.Headers.Vary = "User-Agent";

        return new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "text/html; charset=utf-8",
            Content = _share.RenderPreviewHtml(info, _share.BuildPreviewUrl(Request, info.Type, info.Id), includeMetaRefresh: !isCrawler)
        };
    }
}
