using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace Hodrac_Backend_MVP2.Models;

public class Destination
{
    public Guid DestinationId { get; set; }

    [Required, MaxLength(200)]
    public string DestinationName { get; set; } = string.Empty;

    /// <summary>Lowercase, accent-stripped. Used for fast exact-match lookup.</summary>
    [MaxLength(200)]
    public string CleanNormalizedSearchName { get; set; } = string.Empty;

    // Phonetic codes for fuzzy matching (computed on write, queried on search)
    [MaxLength(20)]
    public string MetaphoneCode { get; set; } = string.Empty;

    [MaxLength(20)]
    public string DoubleMetaphonePrimary { get; set; } = string.Empty;

    [MaxLength(20)]
    public string DoubleMetaphoneSecondary { get; set; } = string.Empty;

    /// <summary>JSON array: ["Kioto", "京都"]. Stored as jsonb for containment queries.</summary>
    [Column(TypeName = "jsonb")]
    public string CommonAlternateSpellingsJson { get; set; } = "[]";

    // ── Rich description ──────────────────────────────────────────────────────
    // Stored as jsonb so individual fields can be queried server-side.
    // Deserialized to DescriptionJsonDto at the service layer — never in the controller.
    [Column(TypeName = "jsonb")]
    public string DescriptionJson { get; set; } = "{}";

    // ── Pricing ───────────────────────────────────────────────────────────────
    [Column(TypeName = "decimal(10,2)")]
    public decimal AverageCostPerDay { get; set; }

    /// <summary>1–5</summary>
    public int LuxuryRating { get; set; }

    /// <summary>"Train" | "Boat Only" | "Flight"</summary>
    [MaxLength(50)]
    public string AccessibilityType { get; set; } = string.Empty;

    // ── Persona & vibe scores ─────────────────────────────────────────────────
    public int FamilyFriendlyScore { get; set; }
    public int AdventurePaceScore { get; set; }
    public int AestheticTrendScore { get; set; }

    /// <summary>JSON array: ["Adventure","Secluded","Eco-Tourist"]</summary>
    [Column(TypeName = "jsonb")]
    public string PsychographicVibeTagsJson { get; set; } = "[]";

    // ── Geolocation ───────────────────────────────────────────────────────────
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // ── Analytics ─────────────────────────────────────────────────────────────
    public long SearchHitCount { get; set; }

    [MaxLength(100)]
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>1–5: 1 = very safe, 5 = exercise caution</summary>
    public int SafetyLevel { get; set; }

    // ── Foreign keys ──────────────────────────────────────────────────────────
    public Guid CountryId { get; set; }
    public Country Country { get; set; } = null!;

    // ── Navigation ────────────────────────────────────────────────────────────
    public ICollection<DestinationImage> Images { get; set; } = new List<DestinationImage>();
    public ICollection<WishlistDestination> WishlistDestinations { get; set; } = new List<WishlistDestination>();
    public ICollection<DestinationCategory> DestinationCategories { get; set; } = new List<DestinationCategory>();
    public ICollection<DestinationTag> DestinationTags { get; set; } = new List<DestinationTag>();
    public ICollection<DestinationCurrency> DestinationCurrencies { get; set; } = new List<DestinationCurrency>();
    public ICollection<DestinationLanguage> DestinationLanguages { get; set; } = new List<DestinationLanguage>();
    public ICollection<DestinationCity> DestinationCities { get; set; } = new List<DestinationCity>();
    public ICollection<SavedDestination> SavedByUsers { get; set; } = new List<SavedDestination>();
}

// ─── DestinationImage ─────────────────────────────────────────────────────────

public class DestinationImage
{
    public Guid DestinationImageId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    /// <summary>"Hero" | "Gallery" | "Activity_Proof" | "Map_Overlay"</summary>
    [MaxLength(30)]
    public string ImageType { get; set; } = string.Empty;

    public string ShotContext { get; set; } = string.Empty;
    public bool IsAiGenerated { get; set; }

    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;
}
