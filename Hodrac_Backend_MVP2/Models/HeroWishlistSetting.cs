namespace Hodrac_Backend_MVP2.Models;

// ─── HeroWishlistSetting ──────────────────────────────────────────────────────
// Single-row table (Id is always 1): the admin-chosen wishlist shown first in
// the landing-page hero. Independent of the featured pool.

public class HeroWishlistSetting
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Set to NULL by the database if the wishlist is deleted.</summary>
    public Guid? HeroWishlistId { get; set; }
    public Wishlist? HeroWishlist { get; set; }

    /// <summary>After this time the hero no longer applies. Null = no end date.</summary>
    public DateTimeOffset? HeroUntil { get; set; }

    public Guid SetByUserId { get; set; }

    public DateTimeOffset SetAt { get; set; } = DateTimeOffset.UtcNow;
}
