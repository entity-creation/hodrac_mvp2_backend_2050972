using Microsoft.AspNetCore.DataProtection;

namespace Hodrac_Backend_MVP2.Services;

// ─── Partner click context token ──────────────────────────────────────────────
// trackingUrl (/api/out/{linkId}) is opened as a plain browser navigation, so
// the frontend's JWT never reaches it. For logged-in callers PartnerService
// appends ?c=<token>, an encrypted, time-limited Data Protection payload holding
// the user id; PartnersController.Out unprotects it to attribute the click.
// Keys persist in Postgres (PersistKeysToDbContext), so tokens survive restarts.

public static class PartnerClickToken
{
    public const string QueryKey = "c";
    private const string Purpose = "partner-click";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider provider)
        => provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public static string Create(IDataProtectionProvider provider, Guid userId)
        => Protector(provider).Protect(userId.ToString("N"), Lifetime);

    /// <summary>The user id, or null when the token is missing, tampered with or expired.</summary>
    public static Guid? TryRead(IDataProtectionProvider provider, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1000) return null;
        try
        {
            var raw = Protector(provider).Unprotect(token);
            return Guid.TryParseExact(raw, "N", out var id) && id != Guid.Empty ? id : null;
        }
        catch (Exception)
        {
            // CryptographicException (tampered / expired / unknown key) or FormatException.
            return null;
        }
    }
}
