using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace Hodrac_Backend_MVP2.Services.Hosts;

// ─── Host slugs ───────────────────────────────────────────────────────────────

/// <summary>Host page slugs: lowercase a-z/0-9 words joined by single hyphens, 3–80 chars.</summary>
public static partial class HostSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 80;
    private const int GeneratedMaxLength = 60;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex ValidPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    public static bool IsValid(string? slug) =>
        slug is not null && slug.Length is >= MinLength and <= MaxLength && ValidPattern().IsMatch(slug);

    /// <summary>"Lagos Trippers & Co." → "lagos-trippers-co". Never empty; at least <see cref="MinLength"/> chars.</summary>
    public static string FromDisplayName(string displayName)
    {
        // Strip accents (é → e) before dropping everything that isn't a-z0-9.
        var decomposed = displayName.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        var slug = NonAlnum().Replace(sb.ToString().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > GeneratedMaxLength) slug = slug[..GeneratedMaxLength].TrimEnd('-');
        if (slug.Length == 0) slug = "host";
        if (slug.Length < MinLength) slug = $"{slug}-host";
        return slug;
    }

    /// <summary>A candidate with a random suffix, for when the plain slug is taken.</summary>
    public static string WithSuffix(string slug) =>
        $"{slug}-{RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 4)}";
}

// ─── ID document files ────────────────────────────────────────────────────────

/// <summary>
/// Accepts only PDF, JPEG and PNG, decided by the file's leading bytes (magic
/// numbers), never by the client's Content-Type or extension.
/// </summary>
public static class HostDocumentFiles
{
    public const string Pdf = "application/pdf";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };

    /// <summary>The detected content type, or null when the bytes are not PDF/JPEG/PNG.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PdfMagic)) return Pdf;
        if (content.StartsWith(PngMagic)) return Png;
        if (content.StartsWith(JpegMagic)) return Jpeg;
        return null;
    }

    /// <summary>A safe stored file name: no path, no control characters, extension matching the real type.</summary>
    public static string SafeFileName(string? clientFileName, string contentType)
    {
        var ext = contentType switch { Pdf => ".pdf", Png => ".png", _ => ".jpg" };
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName(clientFileName ?? string.Empty));
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/' or ':' or '*' or '?' or '<' or '>' or '|')).ToArray()).Trim();
        if (name.Length == 0) name = "id-document";
        if (name.Length > 100) name = name[..100];
        return name + ext;
    }
}

// ─── Signed admin download links ──────────────────────────────────────────────

public record HostDocumentLink(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Short-lived signed links to a host's private document. The signature is an
/// ASP.NET Data Protection time-limited payload binding document id, admin user
/// id and expiry, so a link is useless after it expires, for another admin, or
/// with any part changed. The download endpoint also requires the Admin policy.
/// </summary>
public class HostDocumentLinkSigner
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);
    private const string Purpose = "Hodrac.HostDocuments.AdminDownload.v1";

    private readonly ITimeLimitedDataProtector _protector;

    public HostDocumentLinkSigner(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public HostDocumentLink Create(Guid documentId, Guid adminUserId) =>
        Create(documentId, adminUserId, DateTimeOffset.UtcNow.Add(DefaultLifetime));

    public HostDocumentLink Create(Guid documentId, Guid adminUserId, DateTimeOffset expiresAt)
    {
        var exp = expiresAt.ToUnixTimeSeconds();
        var sig = _protector.Protect(Payload(documentId, adminUserId, exp), expiresAt);
        var url = $"/api/admin/hosts/documents/{documentId}?exp={exp}&sig={Uri.EscapeDataString(sig)}";
        return new HostDocumentLink(url, DateTimeOffset.FromUnixTimeSeconds(exp));
    }

    public bool IsValid(Guid documentId, Guid adminUserId, long exp, string? sig)
    {
        if (string.IsNullOrWhiteSpace(sig)) return false;
        if (exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        try
        {
            var payload = _protector.Unprotect(sig, out _);
            return string.Equals(payload, Payload(documentId, adminUserId, exp), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Expired, tampered or signed with an unknown key.
            return false;
        }
    }

    private static string Payload(Guid documentId, Guid adminUserId, long exp) => $"{documentId:N}|{adminUserId:N}|{exp}";
}

// ─── Closed hosts ─────────────────────────────────────────────────────────────

/// <summary>
/// The one place the "closed by Hodrac" rule and its responses live
/// (HostProfile.ClosedAt, set by POST /api/admin/hosts/{id}/close).
///
/// Refused for a closed host (403 <see cref="HostMessage"/>): creating a host
/// profile, updating it, payout account, verification submit, creating a
/// hosted trip, converting a trip to hosted, changing a hosted trip's money
/// terms. Still allowed on existing trips: profile GET, manifest (+ CSV),
/// manual payments/refunds, WhatsApp nudge, weekly report.
///
/// Refused for admins (409 <see cref="AdminMessage"/>): verify, reject,
/// founding, operator plan, creating a hosted trip for the host.
/// POST /api/admin/hosts/{id}/reopen clears ClosedAt and un-hides only the
/// trips hidden with <see cref="HiddenTripReason"/>.
/// </summary>
public static class HostClosure
{
    public const string HostMessage = "This host account was closed by Hodrac. Contact support.";
    public const string AdminMessage = "Host account is closed.";

    /// <summary>Reason a hidden trip shows after its host was closed.</summary>
    public const string HiddenTripReason = "Host account closed";

    /// <summary>403 for the host's own setup endpoints.</summary>
    public static Microsoft.AspNetCore.Mvc.ObjectResult HostForbidden() =>
        new(new { error = HostMessage }) { StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden };

    /// <summary>409 for admin actions that would set the host up again.</summary>
    public static Microsoft.AspNetCore.Mvc.ConflictObjectResult AdminConflict() => new(new { error = AdminMessage });
}
