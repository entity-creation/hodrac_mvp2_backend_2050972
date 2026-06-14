using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Data;

namespace Hodrac_Backend_MVP2.Services;

public interface ITokenService
{
    /// <summary>Issues a signed JWT access token. Short-lived (15 minutes).</summary>
    string GenerateAccessToken(ApplicationUser user);

    /// <summary>
    /// Creates a cryptographically random refresh token, stores its SHA-256 hash
    /// in the database, and returns the raw token to send to the client.
    /// </summary>
    Task<string> GenerateRefreshTokenAsync(
        ApplicationUser user, string deviceHint, CancellationToken ct = default);

    /// <summary>
    /// Validates the raw refresh token against stored hashes for this user.
    /// If valid: revokes the used token (rotation) and returns the matching record.
    /// If invalid or expired: returns null.
    /// </summary>
    Task<UserRefreshToken?> ValidateAndRotateRefreshTokenAsync(
        string userId, string rawToken, CancellationToken ct = default);

    /// <summary>Revokes all active refresh tokens for this user (logout from all devices).</summary>
    Task RevokeAllRefreshTokensAsync(string userId, CancellationToken ct = default);

    /// <summary>Revokes a single refresh token (logout from one device).</summary>
    Task RevokeRefreshTokenAsync(string userId, string rawToken, CancellationToken ct = default);
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;
    private readonly HodracDbContext _db;

    // Access token lifetime: short — 15 minutes.
    // If compromised, attacker access is bounded.
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    // Refresh token lifetime: 30 days.
    // Sliding expiry is NOT used — each refresh issues a new 30-day token.
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public TokenService(IConfiguration config, HodracDbContext db)
    {
        _config = config;
        _db     = db;
    }

    // ── Access token ──────────────────────────────────────────────────────────

    public string GenerateAccessToken(ApplicationUser user)
    {
        var key     = GetSigningKey();
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.Add(AccessTokenLifetime);

        var claims = new[]
        {
            // "sub" = subject — the standard claim for user identity. Used by ICurrentUserService.
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),   // unique per token
            new Claim("displayName",                 user.DisplayName),
            new Claim("onboardingComplete",          user.HasCompletedOnboarding.ToString().ToLower()),
        };

        var token = new JwtSecurityToken(
            issuer    : _config["Jwt:Issuer"],
            audience  : _config["Jwt:Audience"],
            claims    : claims,
            notBefore : DateTime.UtcNow,
            expires   : expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── Refresh token ─────────────────────────────────────────────────────────

    public async Task<string> GenerateRefreshTokenAsync(
        ApplicationUser user, string deviceHint, CancellationToken ct = default)
    {
        // 64 cryptographically random bytes → 88-char base64 string sent to client
        var rawBytes = RandomNumberGenerator.GetBytes(64);
        var rawToken = Convert.ToBase64String(rawBytes);

        var record = new UserRefreshToken
        {
            UserRefreshTokenId = Guid.NewGuid(),
            UserId             = user.Id,
            TokenHash          = Hash(rawToken),
            ExpiresAt          = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
            DeviceHint         = deviceHint,
        };

        _db.Set<UserRefreshToken>().Add(record);
        await _db.SaveChangesAsync(ct);

        return rawToken;  // Only time the raw token is accessible — send to client immediately
    }

    // ── Refresh token validation + rotation ───────────────────────────────────

    public async Task<UserRefreshToken?> ValidateAndRotateRefreshTokenAsync(
        string userId, string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);

        var record = await _db.Set<UserRefreshToken>()
            .FirstOrDefaultAsync(t =>
                t.UserId    == userId &&
                t.TokenHash == hash   &&
                !t.IsRevoked          &&
                t.ExpiresAt > DateTimeOffset.UtcNow,
            ct);

        if (record is null) return null;

        // Rotation: immediately revoke the used token.
        // If an attacker replays a stolen token, it will already be revoked.
        record.IsRevoked  = true;
        record.RevokedAt  = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return record;
    }

    public async Task RevokeAllRefreshTokensAsync(string userId, CancellationToken ct = default)
        => await _db.Set<UserRefreshToken>()
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsRevoked, true)
                .SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow),
            ct);

    public async Task RevokeRefreshTokenAsync(
        string userId, string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        await _db.Set<UserRefreshToken>()
            .Where(t => t.UserId == userId && t.TokenHash == hash && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsRevoked, true)
                .SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow),
            ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SymmetricSecurityKey GetSigningKey()
    {
        var secret = _config["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>SHA-256 hash of the raw token. Never store the raw token.</summary>
    private static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
