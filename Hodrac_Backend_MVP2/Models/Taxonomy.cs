using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace Hodrac_Backend_MVP2.Models;

// ─── Category ─────────────────────────────────────────────────────────────────

public class Category
{
    public Guid CategoryId { get; set; }

    [Required, MaxLength(50)]
    public string Key { get; set; } = string.Empty;            // e.g. "food_culture"

    [Required, MaxLength(100)]
    public string CategoryName { get; set; } = string.Empty;

    public string CategoryDescription { get; set; } = string.Empty;

    [MaxLength(50)]
    public string IconName { get; set; } = string.Empty;

    [MaxLength(7)]
    public string ColorHex { get; set; } = string.Empty;       // e.g. "#6366f1"

    public ICollection<DestinationCategory> DestinationCategories { get; set; } = new List<DestinationCategory>();
}

// ─── Tag ──────────────────────────────────────────────────────────────────────

public class Tag
{
    public Guid TagId { get; set; }

    [Required, MaxLength(50)]
    public string Key { get; set; } = string.Empty;            // e.g. "hidden_gem"

    [Required, MaxLength(100)]
    public string TagName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string TargetPersonaType { get; set; } = string.Empty;

    public ICollection<DestinationTag> DestinationTags { get; set; } = new List<DestinationTag>();
}

// ─── Language ─────────────────────────────────────────────────────────────────

public class Language
{
    public Guid LanguageId { get; set; }

    [Required, MaxLength(100)]
    public string LanguageName { get; set; } = string.Empty;

    [MaxLength(10)]
    public string LanguageCode { get; set; } = string.Empty;   // ISO 639-1, e.g. "ja"

    /// <summary>JSON array of survival phrases: [{"phrase":"Thank you","local":"ありがとう"}]</summary>
    [Column(TypeName = "jsonb")]
    public string HelpfulSurvivalPhrasesJson { get; set; } = "[]";

    public bool RequiresCertifiedLocalGuide { get; set; }

    public ICollection<DestinationLanguage> DestinationLanguages { get; set; } = new List<DestinationLanguage>();
    public ICollection<CountryLanguage> CountryLanguages { get; set; } = new List<CountryLanguage>();
}

// ─── Currency ─────────────────────────────────────────────────────────────────

public class Currency
{
    public Guid CurrencyId { get; set; }

    [Required, MaxLength(100)]
    public string CurrencyName { get; set; } = string.Empty;

    [MaxLength(5)]
    public string CurrencyCode { get; set; } = string.Empty;   // ISO 4217, e.g. "JPY"

    [MaxLength(10)]
    public string CurrencySymbol { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,8)")]
    public decimal ExchangeRateToBase { get; set; }            // Base = USD

    public DateTimeOffset LastExchangeRateUpdate { get; set; }

    public ICollection<DestinationCurrency> DestinationCurrencies { get; set; } = new List<DestinationCurrency>();
}

// ─── AggregatedSearchRegistry ─────────────────────────────────────────────────
// Central table for the three-step search pipeline (phonetic → semantic → insert).
// Requires: pgvector extension + Pgvector.EntityFrameworkCore NuGet package.

public class AggregatedSearchRegistry
{
    public Guid AggregatedSearchRegistryId { get; set; }

    [Required, MaxLength(500)]
    public string MasterSearchPhrase { get; set; } = string.Empty;

    /// <summary>JSON array of known typo/variant spellings: ["tokio food","tokyofood"]</summary>
    [Column(TypeName = "jsonb")]
    public string KnownVariantsJson { get; set; } = "[]";

    /// <summary>Groups semantically equivalent phrases. Set by the nightly merge job.</summary>
    [MaxLength(100)]
    public string SemanticClusterId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string CanonicalSemanticPhrase { get; set; } = string.Empty;

    // ── pgvector column ───────────────────────────────────────────────────────
    // Stored as vector(384) — matches all-MiniLM-L6-v2 and text-embedding-ada-002 (truncated).
    // EF mapping configured in AggregatedSearchRegistryConfiguration via HasColumnType("vector(384)").
    public Vector? SemanticEmbedding { get; set; }

    // ── Segmented counters ────────────────────────────────────────────────────
    public long TotalGlobalSearchCount { get; set; }
    public long YoungCoupleSearchCount { get; set; }
    public long FamilyPlannerSearchCount { get; set; }
    public long AdventureDadSearchCount { get; set; }

    public DateTimeOffset LastSearchedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ─── Join Tables ──────────────────────────────────────────────────────────────
// All join tables use composite PK configured in fluent API.

public class DestinationCategory
{
    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

public class DestinationTag
{
    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;

    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public class DestinationCurrency
{
    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;

    public Guid CurrencyId { get; set; }
    public Currency Currency { get; set; } = null!;
}

public class DestinationLanguage
{
    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;

    public Guid LanguageId { get; set; }
    public Language Language { get; set; } = null!;
}

public class DestinationCity
{
    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;

    public Guid CityId { get; set; }
    public City City { get; set; } = null!;
}

public class CountryLanguage
{
    public Guid CountryId { get; set; }
    public Country Country { get; set; } = null!;

    public Guid LanguageId { get; set; }
    public Language Language { get; set; } = null!;
}

public class WishlistDestination
{
    public Guid WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;

    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;
}
