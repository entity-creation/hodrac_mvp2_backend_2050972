using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.Models;

/// <summary>
/// Extends ASP.NET Core Identity's IdentityUser with Hodrac-specific profile fields.
///
/// Identity stores: Id (Guid as string), Email, NormalizedEmail, PasswordHash,
/// SecurityStamp, ConcurrencyStamp, EmailConfirmed, LockoutEnabled, etc.
///
/// We add: display name, avatar, onboarding state, account timestamps.
///
/// ApplicationUser.Id (string) is the single source of truth for user identity.
/// It is stored as-is in MongoDB (auth_user_reference_id) and parsed as Guid
/// in all Postgres FK columns (SavedWishlist.UserId, WishlistCollaborator.UserId, etc.).
/// </summary>
public class ApplicationUser : IdentityUser
{
    // ── Profile ───────────────────────────────────────────────────────────────

    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;

    // ── Onboarding state ──────────────────────────────────────────────────────
    // Tracks whether the user has completed the personalization modal.
    // Controls whether the frontend shows the modal on next visit.

    public bool HasCompletedOnboarding { get; set; } = false;

    // ── Account lifecycle ─────────────────────────────────────────────────────

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public ICollection<UserRefreshToken> RefreshTokens { get; set; } = new List<UserRefreshToken>();
}

/// <summary>
/// Stores hashed refresh tokens for JWT rotation.
/// One user can have multiple valid tokens (multiple devices).
/// Revoked on logout or on refresh (rotation — old token invalidated, new one issued).
/// </summary>
public class UserRefreshToken
{
    public Guid UserRefreshTokenId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;         // FK to ApplicationUser.Id
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// SHA-256 hash of the raw token sent to the client.
    /// Never store the raw token — hash it like a password.
    /// </summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Device/browser hint for "Sessions" UI. Optional.</summary>
    [MaxLength(300)]
    public string DeviceHint { get; set; } = string.Empty;

    public bool IsRevoked { get; set; } = false;
    public DateTimeOffset? RevokedAt { get; set; }
}
