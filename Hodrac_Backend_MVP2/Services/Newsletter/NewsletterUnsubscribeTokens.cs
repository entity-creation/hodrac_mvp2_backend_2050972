using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Hodrac_Backend_MVP2.Services.Newsletter;

/// <summary>
/// Signed unsubscribe tokens for newsletter links. Same mechanism as
/// <see cref="Hosts.HostDocumentLinkSigner"/>: an ASP.NET Data Protection
/// payload (authenticated encryption, HMACSHA256) with the app's key ring,
/// which is persisted in Postgres. The token carries the normalized email and
/// does not expire, so links in old issues keep working; key rotation keeps
/// old keys for unprotecting. A tampered or foreign token fails to decode.
/// </summary>
public class NewsletterUnsubscribeTokens
{
    private const string Purpose = "Hodrac.Newsletter.Unsubscribe.v1";
    private const string Prefix = "nl1|";

    private readonly IDataProtector _protector;

    public NewsletterUnsubscribeTokens(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose);

    /// <summary>Trimmed, lower-case (invariant).</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    /// <summary>URL-safe token (Data Protection output is base64url).</summary>
    public string Create(string email) => _protector.Protect(Prefix + Normalize(email));

    /// <summary>The normalized email the token was made for, or null when invalid.</summary>
    public string? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return null;
        try
        {
            var payload = _protector.Unprotect(token.Trim());
            if (!payload.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            var email = payload[Prefix.Length..];
            return email.Contains('@') ? email : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
