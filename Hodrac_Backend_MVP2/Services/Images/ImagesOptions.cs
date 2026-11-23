namespace Hodrac_Backend_MVP2.Services.Images;

// ─── Image hosting configuration ──────────────────────────────────────────────
// Bound from the "Images" section (env vars Images__PublicBaseUrl,
// Images__FallbackBaseUrls__0, ...). The defaults keep today's Vercel Blob host,
// so nothing changes until the config is set.

public class ImagesOptions
{
    public const string SectionName = "Images";

    /// <summary>The Vercel Blob store every image lived on before the move to R2.</summary>
    public const string VercelBlobBaseUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/";

    /// <summary>Base URL new image URLs are built from (seeders) and the frontend loads images from.</summary>
    public string PublicBaseUrl { get; set; } = VercelBlobBaseUrl;

    /// <summary>
    /// Older hosts that hold the same files. The frontend retries an image on the first one
    /// when the primary fails, and <c>--rewrite-image-host</c> rewrites stored URLs from them.
    /// </summary>
    public string[] FallbackBaseUrls { get; set; } = Array.Empty<string>();

    /// <summary>PublicBaseUrl, normalized (validated, ending in "/").</summary>
    public string NormalizedPublicBaseUrl => NormalizeBaseUrl(PublicBaseUrl);

    /// <summary>FallbackBaseUrls, normalized, blanks and duplicates (and the primary) removed.</summary>
    public IReadOnlyList<string> NormalizedFallbackBaseUrls
    {
        get
        {
            var primary = NormalizedPublicBaseUrl;
            return (FallbackBaseUrls ?? Array.Empty<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(NormalizeBaseUrl)
                .Where(u => !string.Equals(u, primary, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>True when <paramref name="url"/> is an absolute http(s) URL.</summary>
    public static bool IsAbsoluteHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !string.IsNullOrEmpty(uri.Host);

    /// <summary>
    /// Trims the value and makes sure it ends with exactly one "/", so a prefix match or
    /// replace can neither produce "//" nor match a longer host (e.g. "img.example.com.evil").
    /// Throws when the value is not an absolute http(s) URL.
    /// </summary>
    public static string NormalizeBaseUrl(string? url)
    {
        if (!IsAbsoluteHttpUrl(url))
            throw new ArgumentException($"Image base URL '{url}' must be an absolute http(s) URL.", nameof(url));
        return url!.Trim().TrimEnd('/') + "/";
    }
}
