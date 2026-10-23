using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services;

/// <summary>Shareable thing types, as used in /api/share/{type}/{id} and /share/{type}/{id}.</summary>
public static class ShareTypes
{
    public const string Destination = "destination";
    public const string Wishlist = "wishlist";
    public const string SharedWishlist = "shared-wishlist";
    public const string Trip = "trip";

    public static readonly IReadOnlyList<string> All = new[] { Destination, Wishlist, SharedWishlist, Trip };

    public static string? Normalize(string? type)
    {
        var t = type?.Trim().ToLowerInvariant();
        return t is not null && All.Contains(t) ? t : null;
    }
}

/// <summary>
/// Everything needed to share one item: the frontend URL plus link-preview text.
/// Never contains author/user information.
/// </summary>
public record ShareInfo(string Type, string Id, string Url, string Title, string Text, string? ImageUrl);

/// <summary>Response of GET /api/share/{type}/{id}.</summary>
public record ShareDataDto(string Url, string PreviewUrl, string Title, string Text, string? ImageUrl);

/// <summary>Response of POST /api/user-wishlists/{id}/share.</summary>
public record WishlistShareLinkDto(string ShareToken, string ShareUrl, DateTimeOffset? SharedAt);

public interface IShareService
{
    /// <summary>
    /// Resolves share info for a publicly viewable item; null when it is missing,
    /// hidden (trips), private (non-template wishlists) or no longer shared.
    /// </summary>
    Task<ShareInfo?> ResolveAsync(string type, string id, CancellationToken ct = default);

    /// <summary>Frontend URL for an item: Frontend:BaseUrl + Share:Paths:{Type}.</summary>
    string BuildFrontendUrl(string type, string id);

    /// <summary>Absolute URL of the server-rendered preview page (/share/{type}/{id}).</summary>
    string BuildPreviewUrl(HttpRequest request, string type, string id);

    string RenderPreviewHtml(ShareInfo info, string previewUrl, bool includeMetaRefresh);

    string RenderNotFoundHtml();
}

public class ShareService : IShareService
{
    public const int MaxDescriptionLength = 200;

    private static readonly Regex TokenPattern = new("^[A-Za-z0-9_-]{16,64}$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly HodracDbContext _db;
    private readonly IConfiguration _config;

    public ShareService(HodracDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ── Tokens ────────────────────────────────────────────────────────────────

    /// <summary>192 random bits, base64url (32 chars, URL-safe, no padding).</summary>
    public static string NewShareToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));

    public static bool IsValidShareToken(string? token) => token is not null && TokenPattern.IsMatch(token);

    // ── URLs ──────────────────────────────────────────────────────────────────

    private string SiteName => NonEmpty(_config["Share:SiteName"]) ?? "Hodrac";

    private string FrontendBaseUrl =>
        (NonEmpty(_config["Frontend:BaseUrl"]) ?? "http://localhost:5173").TrimEnd('/');

    private string PathTemplate(string type) => type switch
    {
        ShareTypes.Destination => NonEmpty(_config["Share:Paths:Destination"]) ?? "/destinations/{id}",
        ShareTypes.Wishlist => NonEmpty(_config["Share:Paths:Wishlist"]) ?? "/wishlists/{id}",
        ShareTypes.SharedWishlist => NonEmpty(_config["Share:Paths:SharedWishlist"]) ?? "/shared/wishlists/{token}",
        ShareTypes.Trip => NonEmpty(_config["Share:Paths:Trip"]) ?? "/trips/{id}",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown share type.")
    };

    public string BuildFrontendUrl(string type, string id)
    {
        var escaped = Uri.EscapeDataString(id);
        var path = PathTemplate(type).Replace("{id}", escaped).Replace("{token}", escaped);
        if (!path.StartsWith('/')) path = "/" + path;
        return FrontendBaseUrl + path;
    }

    public string BuildPreviewUrl(HttpRequest request, string type, string id)
    {
        var apiBase = NonEmpty(_config["Api:PublicBaseUrl"])?.TrimEnd('/')
                      ?? $"{request.Scheme}://{request.Host}{request.PathBase}";
        return $"{apiBase}/share/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(id)}";
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    public async Task<ShareInfo?> ResolveAsync(string type, string id, CancellationToken ct = default)
    {
        var normalized = ShareTypes.Normalize(type);
        if (normalized is null || string.IsNullOrWhiteSpace(id)) return null;

        return normalized switch
        {
            ShareTypes.Destination => Guid.TryParse(id, out var d) ? await ResolveDestinationAsync(d, ct) : null,
            ShareTypes.Wishlist => Guid.TryParse(id, out var w) ? await ResolveTemplateWishlistAsync(w, ct) : null,
            ShareTypes.SharedWishlist => IsValidShareToken(id) ? await ResolveSharedWishlistAsync(id, ct) : null,
            ShareTypes.Trip => Guid.TryParse(id, out var t) ? await ResolveTripAsync(t, ct) : null,
            _ => null
        };
    }

    private async Task<ShareInfo?> ResolveDestinationAsync(Guid id, CancellationToken ct)
    {
        var d = await _db.Destinations
            .AsNoTracking()
            .Where(x => x.DestinationId == id)
            .Select(x => new
            {
                x.DestinationName,
                CountryName = x.Country.CountryName,
                x.DescriptionJson,
                Image = x.Images
                    .OrderBy(i => i.ImageType == "Hero" ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (d is null) return null;

        var title = string.IsNullOrWhiteSpace(d.CountryName)
            ? d.DestinationName
            : $"{d.DestinationName}, {d.CountryName}";

        var overview = ReadOverview(d.DescriptionJson);
        var text = NonEmpty(overview) ?? $"Discover {d.DestinationName} on {SiteName}.";

        var key = id.ToString();
        return new ShareInfo(ShareTypes.Destination, key, BuildFrontendUrl(ShareTypes.Destination, key),
            title, Trim(text), SafeImage(d.Image));
    }

    // Platform templates only — user forks are private unless shared by token.
    private async Task<ShareInfo?> ResolveTemplateWishlistAsync(Guid id, CancellationToken ct)
    {
        var w = await _db.Wishlists
            .AsNoTracking()
            .Where(x => x.WishlistId == id && x.IsTemplate)
            .Select(x => new { x.WishlistName, x.ShortStory, x.WishlistDescription, x.WishlistHeroImage })
            .FirstOrDefaultAsync(ct);
        if (w is null) return null;

        var key = id.ToString();
        return new ShareInfo(ShareTypes.Wishlist, key, BuildFrontendUrl(ShareTypes.Wishlist, key),
            w.WishlistName, WishlistText(w.WishlistName, w.ShortStory, w.WishlistDescription), SafeImage(w.WishlistHeroImage));
    }

    private async Task<ShareInfo?> ResolveSharedWishlistAsync(string token, CancellationToken ct)
    {
        var w = await _db.Wishlists
            .AsNoTracking()
            .Where(x => x.ShareToken == token && x.ShareEnabledAt != null && !x.IsTemplate)
            .Select(x => new { x.WishlistName, x.ShortStory, x.WishlistDescription, x.WishlistHeroImage })
            .FirstOrDefaultAsync(ct);
        if (w is null) return null;

        return new ShareInfo(ShareTypes.SharedWishlist, token, BuildFrontendUrl(ShareTypes.SharedWishlist, token),
            w.WishlistName, WishlistText(w.WishlistName, w.ShortStory, w.WishlistDescription), SafeImage(w.WishlistHeroImage));
    }

    private string WishlistText(string name, string? shortStory, string? description) =>
        Trim(NonEmpty(shortStory) ?? NonEmpty(description) ?? $"{name} — a trip itinerary on {SiteName}.");

    // Hidden (moderated) trips never resolve. Closed/Cancelled ones do, with
    // the status called out. No author information is ever included.
    private async Task<ShareInfo?> ResolveTripAsync(Guid id, CancellationToken ct)
    {
        var trip = await _db.TripPosts
            .AsNoTracking()
            .Include(t => t.Destination!).ThenInclude(d => d.Country)
            .Include(t => t.Destination!).ThenInclude(d => d.Images)
            .Include(t => t.Wishlist!).ThenInclude(w => w.WishlistDestinations)
                .ThenInclude(wd => wd.Destination).ThenInclude(d => d.Images)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsHidden, ct);
        if (trip is null) return null;

        var dto = PublicTripPostDto.FromEntity(trip);

        var parts = new List<string>();
        if (dto.Status != TripPostStatus.Open) parts.Add(dto.Status.ToString());
        var dates = FormatDates(dto.StartDate, dto.EndDate, dto.IsDateFlexible);
        if (dates is not null) parts.Add(dates);
        // For caption-only trips the label already *is* the caption.
        if (!string.IsNullOrWhiteSpace(dto.Caption) && dto.Caption != dto.DestinationLabel)
            parts.Add(dto.Caption);

        var text = parts.Count > 0 ? string.Join(" · ", parts) : $"Join this trip on {SiteName}.";
        var hasPlace = dto.DestinationLabel != dto.Caption && dto.DestinationLabel != "Trip";
        var title = hasPlace ? $"Trip to {dto.DestinationLabel}" : dto.DestinationLabel;

        var key = id.ToString();
        return new ShareInfo(ShareTypes.Trip, key, BuildFrontendUrl(ShareTypes.Trip, key),
            Trim(title, 120), Trim(text),
            SafeImage(dto.CoverImageUrl) ?? SafeImage(dto.Stops.Select(s => s.ImageUrl).FirstOrDefault(u => u is not null)));
    }

    // ── HTML ──────────────────────────────────────────────────────────────────

    public string RenderPreviewHtml(ShareInfo info, string previewUrl, bool includeMetaRefresh)
    {
        var h = HtmlEncoder.Default;
        var title = h.Encode(info.Title);
        var pageTitle = h.Encode($"{info.Title} | {SiteName}");
        var text = h.Encode(info.Text);
        var url = h.Encode(info.Url);
        var self = h.Encode(previewUrl);
        var site = h.Encode(SiteName);
        var image = SafeImage(info.ImageUrl) ?? SafeImage(_config["Share:DefaultImageUrl"]);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append($"<title>{pageTitle}</title>\n");
        sb.Append($"<meta name=\"description\" content=\"{text}\">\n");
        sb.Append($"<link rel=\"canonical\" href=\"{url}\">\n");
        if (info.Type == ShareTypes.SharedWishlist)
            sb.Append("<meta name=\"robots\" content=\"noindex, nofollow\">\n");
        sb.Append($"<meta property=\"og:site_name\" content=\"{site}\">\n");
        sb.Append("<meta property=\"og:type\" content=\"website\">\n");
        sb.Append($"<meta property=\"og:title\" content=\"{title}\">\n");
        sb.Append($"<meta property=\"og:description\" content=\"{text}\">\n");
        // og:url is this page, not the SPA: Facebook re-scrapes og:url, and the
        // SPA has no server-rendered tags.
        sb.Append($"<meta property=\"og:url\" content=\"{self}\">\n");
        if (image is not null)
        {
            sb.Append($"<meta property=\"og:image\" content=\"{h.Encode(image)}\">\n");
            sb.Append($"<meta property=\"og:image:alt\" content=\"{title}\">\n");
        }
        sb.Append("<meta name=\"twitter:card\" content=\"summary_large_image\">\n");
        sb.Append($"<meta name=\"twitter:title\" content=\"{title}\">\n");
        sb.Append($"<meta name=\"twitter:description\" content=\"{text}\">\n");
        if (image is not null)
            sb.Append($"<meta name=\"twitter:image\" content=\"{h.Encode(image)}\">\n");
        if (includeMetaRefresh)
            sb.Append($"<meta http-equiv=\"refresh\" content=\"0;url={url}\">\n");
        sb.Append("</head>\n<body>\n");
        sb.Append($"<p><a href=\"{url}\">{title}</a></p>\n");
        sb.Append($"<script>window.location.replace(\"{JavaScriptEncoder.Default.Encode(info.Url)}\");</script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    public string RenderNotFoundHtml()
    {
        var h = HtmlEncoder.Default;
        var home = h.Encode(FrontendBaseUrl + "/");
        var site = h.Encode(SiteName);
        return "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
             + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
             + "<meta name=\"robots\" content=\"noindex\">\n"
             + $"<title>Not found | {site}</title>\n"
             + $"<meta property=\"og:title\" content=\"{site}\">\n"
             + "</head>\n<body>\n"
             + $"<p>This link is no longer available. <a href=\"{home}\">Go to {site}</a></p>\n"
             + "</body>\n</html>\n";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? ReadOverview(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<DescriptionJsonDto>(json)?.Overview;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Only absolute http(s) URLs are ever emitted as preview images.</summary>
    internal static string? SafeImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>Collapses whitespace and cuts at a word boundary with an ellipsis.</summary>
    internal static string Trim(string text, int max = MaxDescriptionLength)
    {
        var s = Whitespace.Replace(text ?? string.Empty, " ").Trim();
        if (s.Length <= max) return s;

        var cut = s[..(max - 1)];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > max / 2) cut = cut[..lastSpace];
        return cut.TrimEnd(' ', ',', ';', ':', '.', '-', '·') + "…";
    }

    private static string? FormatDates(DateOnly? start, DateOnly? end, bool flexible)
    {
        static string Day(DateOnly d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        static string DayNoYear(DateOnly d) => d.ToString("d MMM", CultureInfo.InvariantCulture);

        string? range = (start, end) switch
        {
            ({ } s, { } e) when s == e => Day(s),
            ({ } s, { } e) when s.Year == e.Year => $"{DayNoYear(s)} – {Day(e)}",
            ({ } s, { } e) => $"{Day(s)} – {Day(e)}",
            ({ } s, null) => $"From {Day(s)}",
            (null, { } e) => $"Until {Day(e)}",
            _ => null
        };

        if (range is null) return flexible ? "Flexible dates" : null;
        return flexible ? $"{range} (flexible)" : range;
    }
}
