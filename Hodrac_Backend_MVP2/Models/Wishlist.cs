using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hodrac_Backend_MVP2.Models;

// ─── Wishlist ─────────────────────────────────────────────────────────────────

public class Wishlist
{
    public Guid WishlistId { get; set; }

    [Required, MaxLength(200)]
    public string WishlistName { get; set; } = string.Empty;

    public string WishlistDescription { get; set; } = string.Empty;

    /// <summary>Narrative hook shown on the card — "5 days where every meal is an adventure"</summary>
    public string ShortStory { get; set; } = string.Empty;

    public int TotalDays { get; set; }

    [MaxLength(100)]
    public string PeopleType { get; set; } = string.Empty;     // "Family", "Young Couple", etc.

    public string WishlistHeroImage { get; set; } = string.Empty;

    // ── Content & search ─────────────────────────────────────────────────────
    /// <summary>JSON array: ["Boutique Hotels","Shinkansen Passes"]</summary>
    [Column(TypeName = "jsonb")]
    public string GlobalInclusionsJson { get; set; } = "[]";

    /// <summary>Flat searchable text used by the phonetic/semantic pipeline.</summary>
    public string RawContentKeywords { get; set; } = string.Empty;

    /// <summary>JSON array: ["Aesthetic","Foodie","Teen-Approved"]</summary>
    [Column(TypeName = "jsonb")]
    public string PsychologicalVibeTagsJson { get; set; } = "[]";

    // ── Pricing ───────────────────────────────────────────────────────────────
    public int DefaultTravelersCount { get; set; } = 2;

    [Column(TypeName = "decimal(12,2)")]
    public decimal BasePricePerPerson { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal CalculatedTotalCost { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal DepositAmountRequired { get; set; }

    public string AccommodationInclusions { get; set; } = string.Empty;
    public string TransitInclusions { get; set; } = string.Empty;
    public string ActivityInclusions { get; set; } = string.Empty;

    // ── Popularity & featuring ────────────────────────────────────────────────
    public long TotalGlobalSaveCount { get; set; }
    public bool IsFeatured { get; set; }
    public DateTimeOffset? FeaturedUntil { get; set; }

    // ── Behavioral metadata ───────────────────────────────────────────────────
    [MaxLength(100)]
    public string PrimaryPersonaTarget { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastInteractedAt { get; set; }

    // ── Ownership & forking ───────────────────────────────────────────────────
    /// <summary>Null = platform-curated template. Set = user-owned copy.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>True = platform curated template. False = user's editable fork.</summary>
    public bool IsTemplate { get; set; } = true;

    /// <summary>Points to the platform template this was forked from. Null for platform templates.</summary>
    public Guid? ForkedFromId { get; set; }

    /// <summary>Self-referencing navigation. EF configured via fluent API to avoid cascade cycle.</summary>
    public Wishlist? ForkedFrom { get; set; }
    // ── Optimistic concurrency ────────────────────────────────────────────────
    /// <summary>
    /// PostgreSQL xmin system column — used as the optimistic concurrency token.
    /// PostgreSQL automatically increments xmin on every row modification,
    /// so EF Core's Npgsql provider detects concurrent edits without any
    /// application-side bookkeeping, triggers, or NOT NULL constraint issues.
    /// xmin replaces the SQL Server-style byte[] RowVersion which has no
    /// native auto-update equivalent in PostgreSQL.
    /// </summary>
    public uint xmin { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public ICollection<ItineraryDay> ItineraryDays { get; set; } = new List<ItineraryDay>();
    public ICollection<WishlistDestination> WishlistDestinations { get; set; } = new List<WishlistDestination>();
    public ICollection<WishlistCollaborator> Collaborators { get; set; } = new List<WishlistCollaborator>();
    public ICollection<WishlistPricingSnapshot> PricingSnapshots { get; set; } = new List<WishlistPricingSnapshot>();
    public ICollection<SavedWishlist> SavedByUsers { get; set; } = new List<SavedWishlist>();
    public ICollection<FeaturedWishlistPool> FeaturedPoolEntries { get; set; } = new List<FeaturedWishlistPool>();

    /// <summary>
    /// Creator attributions for this wishlist. Most platform-seeded wishlists
    /// have no rows here (they are self-authored by Hodrac). Wishlists built from
    /// a creator's video or article will have one or more rows.
    /// </summary>
    public ICollection<WishlistCreatorAttribution> CreatorAttributions { get; set; }
        = new List<WishlistCreatorAttribution>();
}

// ─── ItineraryDay ─────────────────────────────────────────────────────────────

public class ItineraryDay
{
    public Guid ItineraryDayId { get; set; }
    public int DayNumber { get; set; }

    [MaxLength(200)]
    public string DayTitle { get; set; } = string.Empty;

    // Nullable because not every day has all three time slots
    public Guid? MorningCityId { get; set; }
    public City? MorningCity { get; set; }

    public Guid? AfternoonCityId { get; set; }
    public City? AfternoonCity { get; set; }

    public Guid? EveningCityId { get; set; }
    public City? EveningCity { get; set; }

    /// <summary>Transit leg taken to get into this day's first city from the previous day.</summary>
    public Guid? TransitFromPreviousDayRouteId { get; set; }
    public TransitRoute? TransitFromPreviousDayRoute { get; set; }

    public Guid WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;

    public ICollection<ItineraryItem> ItineraryItems { get; set; } = new List<ItineraryItem>();
}

// ─── ItineraryItem ────────────────────────────────────────────────────────────

public class ItineraryItem
{
    public Guid ItineraryItemId { get; set; }

    [MaxLength(200)]
    public string ItemTitle { get; set; } = string.Empty;

    public string ItemDescription { get; set; } = string.Empty;

    /// <summary>Controls display order within the day.</summary>
    public int ItemOrderIndex { get; set; }

    /// <summary>"Morning" | "Afternoon" | "Evening"</summary>
    [MaxLength(20)]
    public string TimeOfDay { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>e.g. "Traveler Favorite", "Only 4 spots left"</summary>
    public string SocialProofBadge { get; set; } = string.Empty;

    /// <summary>Added to base price when this item is selected.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal IndividualCostModifier { get; set; }

    public bool IsOptionalActivity { get; set; }
    public bool IsSelectedByDefault { get; set; }

    public Guid ItineraryDayId { get; set; }
    public ItineraryDay ItineraryDay { get; set; } = null!;
}

// ─── WishlistPricingSnapshot ──────────────────────────────────────────────────

public class WishlistPricingSnapshot
{
    public Guid WishlistPricingSnapshotId { get; set; }

    public Guid WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;

    public int TravelersCount { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal BasePricePerPerson { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal OptionalActivitiesTotal { get; set; }

    [Column(TypeName = "decimal(5,4)")]
    public decimal SeasonalSurchargePercent { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal DepositAmountRequired { get; set; }

    /// <summary>When this price expires — drives the "Secure your spot" countdown on the frontend.</summary>
    public DateTimeOffset ValidUntil { get; set; }

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ─── WishlistCollaborator ─────────────────────────────────────────────────────

public class WishlistCollaborator
{
    public Guid WishlistCollaboratorId { get; set; }

    public Guid WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;

    public Guid UserId { get; set; }

    [MaxLength(320)]
    public string SharedUserEmail { get; set; } = string.Empty;

    /// <summary>"Owner" | "Editor" | "Viewer"</summary>
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ─── SavedWishlist ────────────────────────────────────────────────────────────

public class SavedWishlist
{
    public Guid SavedWishlistId { get; set; }
    public Guid UserId { get; set; }
    public Guid WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ─── SavedDestination ─────────────────────────────────────────────────────────

public class SavedDestination
{
    public Guid SavedDestinationId { get; set; }
    public Guid UserId { get; set; }
    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ─── FeaturedWishlistPool ─────────────────────────────────────────────────────

public class FeaturedWishlistPool
{
    public Guid FeaturedWishlistPoolId { get; set; }

    public Guid WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;

    /// <summary>"Paid" | "Editorial" | "Random"</summary>
    [MaxLength(20)]
    public string PoolType { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal PaidAmount { get; set; }

    public int DailyImpressionLimit { get; set; }
    public int CurrentImpressionsToday { get; set; }

    /// <summary>Higher = appears more often in random rotation.</summary>
    public double RandomSelectionWeight { get; set; }

    public DateTimeOffset LastRotationDate { get; set; }
}

// ─── Creator ───────────────────────────────────────────────────────────────────
/// <summary>
/// Represents an external content creator (YouTuber, blogger, Instagram travel
/// creator, etc.) whose original content informed or inspired a Hodrac wishlist.
/// Kept separate from ApplicationUser because creators are not required to have
/// Hodrac accounts — their identity lives on their own platform.
/// One creator can be attributed to many wishlists.
/// </summary>
public class Creator
{
    public Guid CreatorId { get; set; }

    /// <summary>Display name shown publicly on the wishlist card and detail page.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Platform handle without the @ symbol, e.g. "tokyo_wanderer".</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>
    /// The primary platform where this creator publishes.
    /// Stored as a string to stay flexible: "YouTube", "Instagram", "TikTok",
    /// "Blog", "Twitter/X", "Podcast", "Other".
    /// </summary>
    public string PlatformName { get; set; } = string.Empty;

    /// <summary>Full URL to the creator's profile/channel page.</summary>
    public string ProfileUrl { get; set; } = string.Empty;

    /// <summary>URL to the creator's avatar image. Nullable.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Short public bio shown on the attribution card. Nullable.</summary>
    public string? Bio { get; set; }

    /// <summary>
    /// Contact email for internal Hodrac use only — never exposed in public API
    /// responses. Used to reach creators for permission requests.
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// True when Hodrac staff have verified this creator's identity (e.g. by
    /// receiving a signed permission email or DM from their verified account).
    /// </summary>
    public bool IsVerified { get; set; } = false;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<WishlistCreatorAttribution> WishlistAttributions { get; set; }
        = new List<WishlistCreatorAttribution>();
}

// ─── WishlistCreatorAttribution ────────────────────────────────────────────────
/// <summary>
/// Join table between Wishlist and Creator that also carries permission metadata.
/// The existence of a row means attribution has been granted.
/// A wishlist can have at most one active attribution at a time in practice,
/// but the schema allows multiple (e.g. two co-creators of a collab video).
/// </summary>
public class WishlistCreatorAttribution
{
    public Guid WishlistCreatorAttributionId { get; set; }

    // ── Foreign keys ──────────────────────────────────────────────────────────
    public Guid WishlistId { get; set; }
    public Guid CreatorId { get; set; }

    /// <summary>
    /// URL of the original content that the wishlist is based on.
    /// e.g. "https://youtube.com/watch?v=abc123"
    /// </summary>
    public string OriginalContentUrl { get; set; } = string.Empty;

    /// <summary>
    /// How permission was obtained.
    /// Values: "Verbal", "Email", "WrittenContract", "OpenLicense", "SelfAuthored"
    /// "SelfAuthored" is used when the creator built the wishlist themselves on Hodrac.
    /// </summary>
    public string PermissionType { get; set; } = string.Empty;

    /// <summary>When the creator granted permission to use their content.</summary>
    public DateTimeOffset PermissionGrantedAt { get; set; }

    /// <summary>
    /// Link to proof of permission — email screenshot, contract PDF, DM screenshot.
    /// Nullable; not always available for verbal permissions.
    /// </summary>
    public string? PermissionEvidenceUrl { get; set; }

    /// <summary>
    /// Whether this attribution is currently active.
    /// Set to false if a creator later revokes permission without requiring
    /// a full wishlist deletion.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Free-text note for internal use.
    /// e.g. "Creator reviewed and approved the final wishlist draft on 2025-11-03."
    /// </summary>
    public string? AttributionNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────────
    public Wishlist Wishlist { get; set; } = null!;
    public Creator Creator { get; set; } = null!;
}
