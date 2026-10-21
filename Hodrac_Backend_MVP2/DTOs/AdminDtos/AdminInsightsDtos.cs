namespace Hodrac_Backend_MVP2.DTOs.AdminDtos
{
    // ─── Admin behavioural insights (aggregate-only) ──────────────────────────
    // Privacy: no per-user data is ever returned. Any bucket with fewer than
    // MinBucketSize (3) members is folded into an "other" bucket; tags used by
    // fewer than 3 users are folded into "other"; vector averages are null when
    // the audience has fewer than 3 profiles; segment wishlists with fewer than 3
    // segment saves are omitted (their total is reported as SuppressedSaveCount).

    /// <summary>One breakdown bucket. Key "unknown" = not answered/inferred; "other" = suppressed small buckets.</summary>
    public record InsightBucketDto(string Key, int Count);

    /// <summary>Tag usage: Interactions = summed weighted tag_counts, Users = profiles with the tag.</summary>
    public record InsightTagDto(string Tag, long Interactions, int Users);

    public record InsightVectorAveragesDto(
        double AdventureSeeker,
        double SocialButterfly,
        double PlannerVsSpontaneous,
        double CulturalImmersionIndex,
        double TeenValidationPriority,
        double OpennessToExperience,
        double Conscientiousness,
        double Extraversion);

    public record AudienceBreakdownDto(
        int ProfileCount,
        int OnboardedCount,
        List<InsightBucketDto> WhoTheyTravelWith,
        List<InsightBucketDto> TripTypePreference,
        List<InsightBucketDto> BudgetProfile,
        List<InsightBucketDto> PrimaryPriority,
        List<InsightBucketDto> PrimaryTravelerType,
        List<InsightBucketDto> IncomeBracket,
        List<InsightTagDto> TopTags,
        InsightVectorAveragesDto? AverageVectors);

    /// <summary>GET /api/admin/insights/audience</summary>
    public record AdminAudienceInsightsDto(DateTime GeneratedAt, int MinBucketSize, AudienceBreakdownDto Audience);

    public record WishlistSegmentCountersDto(
        DateTime? LastComputedAt,
        List<InsightBucketDto> TravelerTypes,
        List<InsightBucketDto> BudgetTiers,
        List<InsightBucketDto> PrimaryPriorities,
        List<InsightBucketDto> PersonaClusters);

    /// <summary>GET /api/admin/insights/wishlists/{id}/audience</summary>
    public record AdminWishlistAudienceDto(
        Guid WishlistId,
        string WishlistName,
        long TotalGlobalSaveCount,
        int SaverCount,
        int MinBucketSize,
        WishlistSegmentCountersDto? SegmentCounters,
        AudienceBreakdownDto Savers);

    public record SegmentWishlistDto(Guid WishlistId, string WishlistName, int SegmentSaveCount, long TotalGlobalSaveCount);

    /// <summary>GET /api/admin/insights/wishlists?segmentType=&amp;segment=&amp;limit=</summary>
    public record AdminSegmentWishlistsDto(
        string SegmentType,
        string Segment,
        string SegmentKey,
        int MinBucketSize,
        List<SegmentWishlistDto> Items,
        int SuppressedSaveCount);

    /// <summary>GET /api/admin/insights/destinations/{id}/audience</summary>
    public record AdminDestinationAudienceDto(
        Guid DestinationId,
        string DestinationName,
        int WindowDays,
        long ClickCount,
        int UniqueUsers,
        int MinBucketSize,
        AudienceBreakdownDto Clickers);

    /// <summary>GET /api/admin/insights/tags?segmentType=&amp;segment=&amp;limit=</summary>
    public record AdminTagInsightsDto(
        string? SegmentType,
        string? Segment,
        int ProfileCount,
        int MinBucketSize,
        List<InsightTagDto> Tags);

    /// <summary>Rebuild job status (POST/GET /api/admin/insights/rebuild[/{jobId}]).</summary>
    public record InsightsRebuildStatusDto(
        string JobId,
        string Status,
        string Phase,
        DateTime StartedAt,
        DateTime? FinishedAt,
        string? RequestedBy,
        int ProfilesCreated,
        long ProfilesTotal,
        int ProfilesProcessed,
        int ProfilesUpdated,
        int ProfilesFailed,
        int EventsScanned,
        int SavesScanned,
        int WishlistsTotal,
        int WishlistsProcessed,
        int WishlistsFailed,
        long StaleWishlistDocsDeleted,
        string? Error);
}
