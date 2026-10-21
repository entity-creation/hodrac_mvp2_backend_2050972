using System.ComponentModel.DataAnnotations;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;

namespace Hodrac_Backend_MVP2.DTOs.AdminDtos
{
    // Paged lists reuse Hodrac_Backend_MVP2.DTOs.PartnerDtos.AdminPagedResult<T>.

    // ─── Destinations ─────────────────────────────────────────────────────────

    public record AdminDestinationListItemDto(
        Guid DestinationId,
        string DestinationName,
        Guid CountryId,
        string CountryName,
        List<string> CityNames,
        string? HeroThumbnailUrl,     // Hero image thumbnail, else first image by DisplayOrder
        decimal AverageCostPerDay,
        int SafetyLevel,
        int LuxuryRating,
        long SearchHitCount,
        List<string> TagKeys,
        List<string> CategoryKeys,
        int ImageCount,
        int WishlistCount,            // wishlists that include this destination
        int PartnerLinkCount
    );

    /// <summary>Generic id + display name (+ key/code) used for pickers and detail references.</summary>
    /// <param name="Key">Tag/Category Key, Language code, Currency code; null for cities.</param>
    public record AdminRefDto(Guid Id, string Name, string? Key);

    public record AdminDestinationImageDto(
        Guid DestinationImageId,
        string ImageUrl,
        string ThumbnailUrl,
        string Caption,
        int DisplayOrder,
        string ImageType,
        string ShotContext,
        bool IsAiGenerated
    );

    public record AdminDestinationDetailDto(
        Guid DestinationId,
        string DestinationName,
        string CleanNormalizedSearchName,
        string MetaphoneCode,
        string DoubleMetaphonePrimary,
        string DoubleMetaphoneSecondary,
        Guid CountryId,
        string CountryName,
        DescriptionJsonDto Description,
        List<string> CommonAlternateSpellings,
        List<string> PsychographicVibeTags,
        decimal AverageCostPerDay,
        int LuxuryRating,
        string AccessibilityType,
        int FamilyFriendlyScore,
        int AdventurePaceScore,
        int AestheticTrendScore,
        double Latitude,
        double Longitude,
        long SearchHitCount,
        string TimeZone,
        int SafetyLevel,
        List<AdminDestinationImageDto> Images,
        List<AdminRefDto> Tags,
        List<AdminRefDto> Categories,
        List<AdminRefDto> Languages,
        List<AdminRefDto> Currencies,
        List<AdminRefDto> Cities,
        int WishlistCount,
        int TripPostCount,
        int PartnerLinkCount
    );

    /// <summary>
    /// Search fields (CleanNormalizedSearchName, metaphone codes) are derived server-side from
    /// DestinationName. Scores/ratings are 1–5. Taxonomy arrays are optional; unknown ids/keys → 400.
    /// </summary>
    public record CreateDestinationRequest(
        [Required, MaxLength(200)] string DestinationName,
        [Required] Guid? CountryId,
        [Required, Range(-90d, 90d)] double? Latitude,
        [Required, Range(-180d, 180d)] double? Longitude,
        [Required, Range(typeof(decimal), "0", "99999999.99")] decimal? AverageCostPerDay,
        [Required, Range(1, 5)] int? LuxuryRating,
        [Required, Range(1, 5)] int? SafetyLevel,
        [Range(1, 5)] int? FamilyFriendlyScore,     // default 3
        [Range(1, 5)] int? AdventurePaceScore,      // default 3
        [Range(1, 5)] int? AestheticTrendScore,     // default 3
        [MaxLength(50)] string? AccessibilityType,
        [MaxLength(100)] string? TimeZone,          // default: empty
        DescriptionJsonDto? Description,            // default: empty description object
        List<string>? CommonAlternateSpellings,
        List<string>? PsychographicVibeTags,
        List<string>? TagKeys,
        List<string>? CategoryKeys,
        List<Guid>? LanguageIds,
        List<Guid>? CurrencyIds,
        List<Guid>? CityIds                         // must belong to CountryId
    );

    /// <summary>
    /// Partial update: null = leave unchanged. A provided array replaces that set exactly
    /// ([] clears it). Description, when provided, replaces the whole description object.
    /// Changing DestinationName re-derives the search fields.
    /// </summary>
    public record UpdateDestinationRequest(
        [MaxLength(200)] string? DestinationName,
        Guid? CountryId,
        [Range(-90d, 90d)] double? Latitude,
        [Range(-180d, 180d)] double? Longitude,
        [Range(typeof(decimal), "0", "99999999.99")] decimal? AverageCostPerDay,
        [Range(1, 5)] int? LuxuryRating,
        [Range(1, 5)] int? SafetyLevel,
        [Range(1, 5)] int? FamilyFriendlyScore,
        [Range(1, 5)] int? AdventurePaceScore,
        [Range(1, 5)] int? AestheticTrendScore,
        [MaxLength(50)] string? AccessibilityType,
        [MaxLength(100)] string? TimeZone,
        DescriptionJsonDto? Description,
        List<string>? CommonAlternateSpellings,
        List<string>? PsychographicVibeTags,
        List<string>? TagKeys,
        List<string>? CategoryKeys,
        List<Guid>? LanguageIds,
        List<Guid>? CurrencyIds,
        List<Guid>? CityIds
    );

    // ─── Destination images ───────────────────────────────────────────────────

    /// <summary>
    /// ImageType: "Hero" | "Gallery" | "Activity_Proof" | "Map_Overlay". A new Hero demotes the
    /// existing Hero to Gallery. ThumbnailUrl defaults to ImageUrl. DisplayOrder (1-based) inserts
    /// at that position; omitted = append.
    /// </summary>
    public record CreateDestinationImageRequest(
        [Required, MaxLength(2000)] string ImageUrl,
        [MaxLength(2000)] string? ThumbnailUrl,
        [MaxLength(1000)] string? Caption,
        [Required, MaxLength(30)] string ImageType,
        [MaxLength(1000)] string? ShotContext,
        bool IsAiGenerated = false,
        [Range(1, 10000)] int? DisplayOrder = null
    );

    /// <summary>Partial update: null = unchanged. ThumbnailUrl "" resets it to ImageUrl. DisplayOrder moves the image.</summary>
    public record UpdateDestinationImageRequest(
        [MaxLength(2000)] string? ImageUrl,
        [MaxLength(2000)] string? ThumbnailUrl,
        [MaxLength(1000)] string? Caption,
        [MaxLength(30)] string? ImageType,
        [MaxLength(1000)] string? ShotContext,
        bool? IsAiGenerated,
        [Range(1, 10000)] int? DisplayOrder
    );

    /// <summary>Must list every image of the destination exactly once; DisplayOrder becomes 1..n in this order.</summary>
    public record ReorderDestinationImagesRequest([Required] List<Guid> ImageIds);

    // ─── Tags ─────────────────────────────────────────────────────────────────

    public record AdminTagDto(Guid TagId, string Key, string TagName, string TargetPersonaType, int DestinationCount);

    /// <summary>Key: lowercase snake_case (e.g. "hidden_gem"), unique.</summary>
    public record CreateTagRequest(
        [Required, MaxLength(50)] string Key,
        [Required, MaxLength(100)] string TagName,
        [MaxLength(100)] string? TargetPersonaType
    );

    public record UpdateTagRequest(
        [MaxLength(50)] string? Key,
        [MaxLength(100)] string? TagName,
        [MaxLength(100)] string? TargetPersonaType
    );

    // ─── Categories ───────────────────────────────────────────────────────────

    public record AdminCategoryDto(
        Guid CategoryId,
        string Key,
        string CategoryName,
        string CategoryDescription,
        string IconName,
        string ColorHex,
        int DestinationCount
    );

    /// <summary>Key: lowercase snake_case, unique. ColorHex: "#rrggbb" or empty.</summary>
    public record CreateCategoryRequest(
        [Required, MaxLength(50)] string Key,
        [Required, MaxLength(100)] string CategoryName,
        [MaxLength(2000)] string? CategoryDescription,
        [MaxLength(50)] string? IconName,
        [MaxLength(7)] string? ColorHex
    );

    public record UpdateCategoryRequest(
        [MaxLength(50)] string? Key,
        [MaxLength(100)] string? CategoryName,
        [MaxLength(2000)] string? CategoryDescription,
        [MaxLength(50)] string? IconName,
        [MaxLength(7)] string? ColorHex
    );

    // ─── Countries ────────────────────────────────────────────────────────────

    public record AdminCountryDto(
        Guid CountryId,
        string CountryName,
        string Continent,
        string CountryFlagEmoji,
        string GlobalHeroImage,
        string VisaRequirementsSummary,
        string PowerPlugType,
        string DrivingSide,
        decimal EstimatedDailyTaxRate,
        int CityCount,
        int DestinationCount
    );

    /// <summary>CountryName unique (case-insensitive). DrivingSide "Left" | "Right" | empty. Tax rate is a fraction (0.10 = 10%).</summary>
    public record CreateCountryRequest(
        [Required, MaxLength(100)] string CountryName,
        [MaxLength(50)] string? Continent,
        [MaxLength(10)] string? CountryFlagEmoji,
        [MaxLength(2000)] string? GlobalHeroImage,
        [MaxLength(5000)] string? VisaRequirementsSummary,
        [MaxLength(50)] string? PowerPlugType,
        [MaxLength(20)] string? DrivingSide,
        [Range(typeof(decimal), "0", "1")] decimal? EstimatedDailyTaxRate
    );

    /// <summary>Partial update: null = unchanged; "" clears optional text fields.</summary>
    public record UpdateCountryRequest(
        [MaxLength(100)] string? CountryName,
        [MaxLength(50)] string? Continent,
        [MaxLength(10)] string? CountryFlagEmoji,
        [MaxLength(2000)] string? GlobalHeroImage,
        [MaxLength(5000)] string? VisaRequirementsSummary,
        [MaxLength(50)] string? PowerPlugType,
        [MaxLength(20)] string? DrivingSide,
        [Range(typeof(decimal), "0", "1")] decimal? EstimatedDailyTaxRate
    );

    // ─── Cities ───────────────────────────────────────────────────────────────

    public record AdminCityDto(
        Guid CityId,
        string CityName,
        string CityDescription,
        double Latitude,
        double Longitude,
        Guid CountryId,
        string CountryName,
        int DestinationCount
    );

    /// <summary>(CityName, CountryId) unique (case-insensitive).</summary>
    public record CreateCityRequest(
        [Required, MaxLength(100)] string CityName,
        [Required] Guid? CountryId,
        [Required, Range(-90d, 90d)] double? Latitude,
        [Required, Range(-180d, 180d)] double? Longitude,
        [MaxLength(5000)] string? CityDescription
    );

    public record UpdateCityRequest(
        [MaxLength(100)] string? CityName,
        Guid? CountryId,
        [Range(-90d, 90d)] double? Latitude,
        [Range(-180d, 180d)] double? Longitude,
        [MaxLength(5000)] string? CityDescription
    );
}
