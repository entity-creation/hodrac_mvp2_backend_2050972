using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.DTOs.AdminDtos
{
    // ─── Wishlist templates ───────────────────────────────────────────────────

    public record AdminWishlistSummaryDto(
        Guid WishlistId,
        string WishlistName,
        string WishlistHeroImage,
        int TotalDays,
        string PeopleType,
        decimal BasePricePerPerson,
        long TotalGlobalSaveCount,
        bool IsFeatured,
        DateTimeOffset? FeaturedUntil,
        int DestinationCount,
        int ForkCount,
        int PartnerLinkCount,
        DateTimeOffset CreatedAt
    );

    public record AdminWishlistDestinationDto(Guid DestinationId, string DestinationName);

    public record AdminItineraryItemDto(
        Guid ItemId,
        int OrderIndex,
        string Title,
        string Description,
        string TimeOfDay,
        string ImageUrl,
        string SocialProofBadge,
        decimal CostModifier,
        bool IsOptional,
        bool IsSelectedByDefault
    );

    public record AdminItineraryDayDto(
        Guid DayId,
        int DayNumber,
        string DayTitle,
        Guid? MorningCityId,
        string? MorningCityName,
        Guid? AfternoonCityId,
        string? AfternoonCityName,
        Guid? EveningCityId,
        string? EveningCityName,
        Guid? TransitFromPreviousDayRouteId,
        List<AdminItineraryItemDto> Items
    );

    /// <summary>Attribution as admins see it — includes inactive rows and the creator's internal ContactEmail.</summary>
    public record AdminAttributionDto(
        Guid WishlistCreatorAttributionId,
        Guid CreatorId,
        string CreatorDisplayName,
        string CreatorHandle,
        string CreatorPlatformName,
        bool CreatorIsVerified,
        string OriginalContentUrl,
        string PermissionType,
        DateTimeOffset PermissionGrantedAt,
        string? PermissionEvidenceUrl,
        bool IsActive,
        string? AttributionNote,
        DateTimeOffset CreatedAt
    );

    public record AdminFeaturedPoolEntryDto(
        Guid FeaturedWishlistPoolId,
        Guid WishlistId,
        string WishlistName,
        string PoolType,
        decimal PaidAmount,
        int DailyImpressionLimit,
        int CurrentImpressionsToday,
        double RandomSelectionWeight,
        DateTimeOffset LastRotationDate
    );

    public record AdminWishlistDetailDto(
        Guid WishlistId,
        string WishlistName,
        string WishlistDescription,
        string ShortStory,
        int TotalDays,
        string PeopleType,
        string WishlistHeroImage,
        List<string> GlobalInclusions,
        string RawContentKeywords,
        List<string> PsychologicalVibeTags,
        int DefaultTravelersCount,
        decimal BasePricePerPerson,
        decimal CalculatedTotalCost,
        decimal DepositAmountRequired,
        string AccommodationInclusions,
        string TransitInclusions,
        string ActivityInclusions,
        long TotalGlobalSaveCount,
        bool IsFeatured,
        DateTimeOffset? FeaturedUntil,
        string PrimaryPersonaTarget,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastInteractedAt,
        int ForkCount,
        int PartnerLinkCount,
        int TripPostCount,
        uint Xmin,
        List<AdminWishlistDestinationDto> Destinations,
        List<AdminItineraryDayDto> ItineraryDays,
        List<AdminAttributionDto> Attributions,
        List<AdminFeaturedPoolEntryDto> FeaturedPoolEntries
    );

    /// <summary>
    /// POST /api/admin/wishlists. CalculatedTotalCost defaults to
    /// BasePricePerPerson × DefaultTravelersCount when omitted.
    /// </summary>
    public record CreateAdminWishlistRequest(
        [Required, MaxLength(200)] string WishlistName,
        string? WishlistDescription,
        string? ShortStory,
        [Range(1, 365)] int TotalDays,
        [MaxLength(100)] string? PeopleType,
        [MaxLength(2000)] string? WishlistHeroImage,
        List<string>? GlobalInclusions,
        string? RawContentKeywords,
        List<string>? PsychologicalVibeTags,
        [Range(1, 100)] int DefaultTravelersCount = 2,
        [Range(0, 9_999_999_999)] decimal BasePricePerPerson = 0,
        [Range(0, 9_999_999_999)] decimal? CalculatedTotalCost = null,
        [Range(0, 9_999_999_999)] decimal DepositAmountRequired = 0,
        string? AccommodationInclusions = null,
        string? TransitInclusions = null,
        string? ActivityInclusions = null,
        [MaxLength(100)] string? PrimaryPersonaTarget = null,
        List<Guid>? DestinationIds = null
    );

    /// <summary>
    /// PATCH /api/admin/wishlists/{id}. Partial: null = leave unchanged.
    /// Xmin (from the last GET) enables optimistic concurrency → 409 on conflict.
    /// When BasePricePerPerson or DefaultTravelersCount change and CalculatedTotalCost is
    /// omitted, it is recomputed — but only if it was previously the derived value.
    /// </summary>
    public record UpdateAdminWishlistRequest(
        [MaxLength(200)] string? WishlistName,
        string? WishlistDescription,
        string? ShortStory,
        [Range(1, 365)] int? TotalDays,
        [MaxLength(100)] string? PeopleType,
        [MaxLength(2000)] string? WishlistHeroImage,
        List<string>? GlobalInclusions,
        string? RawContentKeywords,
        List<string>? PsychologicalVibeTags,
        [Range(1, 100)] int? DefaultTravelersCount,
        [Range(0, 9_999_999_999)] decimal? BasePricePerPerson,
        [Range(0, 9_999_999_999)] decimal? CalculatedTotalCost,
        [Range(0, 9_999_999_999)] decimal? DepositAmountRequired,
        string? AccommodationInclusions,
        string? TransitInclusions,
        string? ActivityInclusions,
        [MaxLength(100)] string? PrimaryPersonaTarget,
        uint? Xmin
    );

    /// <summary>PUT /api/admin/wishlists/{id}/destinations — replaces the whole set (empty list clears it).</summary>
    public record SetWishlistDestinationsRequest([Required] List<Guid> DestinationIds);

    /// <summary>POST /api/admin/wishlists/{id}/feature. FeaturedUntil null = featured indefinitely.</summary>
    public record FeatureWishlistRequest(DateTimeOffset? FeaturedUntil);

    // ─── Featured pool ────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/admin/featured-pool. PoolType: "Editorial" | "Paid" | "Random".
    /// Paid entries need DailyImpressionLimit ≥ 1 (the rotation skips entries whose
    /// CurrentImpressionsToday has reached the limit; ImpressionResetJob zeroes it at midnight UTC).
    /// </summary>
    public record CreateFeaturedPoolEntryRequest(
        [Required] Guid WishlistId,
        [Required, MaxLength(20)] string PoolType,
        [Range(0, 99_999_999)] decimal PaidAmount = 0,
        [Range(0, int.MaxValue)] int DailyImpressionLimit = 0,
        [Range(0, 1_000_000)] double RandomSelectionWeight = 1
    );

    /// <summary>PATCH /api/admin/featured-pool/{id}. Partial: null = leave unchanged.</summary>
    public record UpdateFeaturedPoolEntryRequest(
        [MaxLength(20)] string? PoolType,
        [Range(0, 99_999_999)] decimal? PaidAmount,
        [Range(0, int.MaxValue)] int? DailyImpressionLimit,
        [Range(0, 1_000_000)] double? RandomSelectionWeight,
        bool ResetImpressionsToday = false
    );

    // ─── Creators ─────────────────────────────────────────────────────────────

    /// <summary>Admin creator row — includes the internal ContactEmail and attribution counts.</summary>
    public record AdminCreatorDto(
        Guid CreatorId,
        string DisplayName,
        string Handle,
        string PlatformName,
        string ProfileUrl,
        string? AvatarUrl,
        string? Bio,
        string? ContactEmail,
        bool IsVerified,
        DateTimeOffset CreatedAt,
        int AttributionCount,
        int ActiveAttributionCount
    );
}
