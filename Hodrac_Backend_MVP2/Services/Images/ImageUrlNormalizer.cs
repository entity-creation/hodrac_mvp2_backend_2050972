using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Services.Images;

/// <summary>
/// Normalizes image URLs on write: a URL that starts with one of Images:FallbackBaseUrls
/// (an old host) has ONLY that prefix swapped for Images:PublicBaseUrl, so pasted links to
/// the old host are stored on the primary one. Anything else is returned trimmed, unchanged.
/// With the default config (no fallbacks) this is a trim and nothing more.
/// </summary>
public class ImageUrlNormalizer
{
    private readonly string _primary;
    private readonly IReadOnlyList<string> _fallbacks;

    public ImageUrlNormalizer(IOptions<ImagesOptions> options)
    {
        var value = options.Value;
        _primary = value.NormalizedPublicBaseUrl;
        _fallbacks = value.NormalizedFallbackBaseUrls;
    }

    /// <summary>
    /// Null or blank values come back unchanged (callers keep their own "clear" semantics);
    /// otherwise the value is trimmed and an old-host prefix is replaced by the primary host.
    /// </summary>
    public string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        var trimmed = url.Trim();

        foreach (var fallback in _fallbacks)
        {
            if (StartsWithBase(trimmed, fallback))
                return _primary + trimmed[fallback.Length..];
        }
        return trimmed;
    }

    /// <summary>True when <paramref name="url"/> is an absolute http(s) URL.</summary>
    public bool IsValidImageUrl(string url) => ImagesOptions.IsAbsoluteHttpUrl(url);

    /// <summary>
    /// Exact prefix match, where the scheme and host part ("https://host/") compares
    /// case-insensitively and any path in the base compares exactly. Bases end with "/",
    /// so a longer host (e.g. "img.example.com.evil") never matches.
    /// </summary>
    private static bool StartsWithBase(string url, string baseUrl)
    {
        if (url.Length < baseUrl.Length) return false;
        var authorityEnd = baseUrl.IndexOf('/', baseUrl.IndexOf("://", StringComparison.Ordinal) + 3);
        if (authorityEnd < 0) authorityEnd = baseUrl.Length;
        return string.Compare(url, 0, baseUrl, 0, authorityEnd, StringComparison.OrdinalIgnoreCase) == 0
            && string.Compare(url, authorityEnd, baseUrl, authorityEnd, baseUrl.Length - authorityEnd, StringComparison.Ordinal) == 0;
    }
}
