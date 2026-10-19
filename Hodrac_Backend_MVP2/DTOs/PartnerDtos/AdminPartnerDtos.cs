using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.DTOs.PartnerDtos
{
    // ─── Partners ─────────────────────────────────────────────────────────────

    public record AdminPartnerSummaryDto(
        Guid PartnerId,
        string Name,
        string? LogoUrl,
        string DefaultCtaLabel,
        bool IsActive,
        DateTime CreatedAt,
        int LinkCount,
        int ActiveLinkCount
    );

    public record AdminPartnerDetailDto(
        Guid PartnerId,
        string Name,
        string? LogoUrl,
        string DefaultCtaLabel,
        bool IsActive,
        DateTime CreatedAt,
        List<AdminPartnerLinkDto> Links
    );

    public record CreatePartnerRequest(
        [Required, MaxLength(200)] string Name,
        [MaxLength(1000)] string? LogoUrl,
        [MaxLength(100)] string? DefaultCtaLabel,
        bool IsActive = true
    );

    /// <summary>Partial update: null = leave unchanged. LogoUrl "" clears it.</summary>
    public record UpdatePartnerRequest(
        [MaxLength(200)] string? Name,
        [MaxLength(1000)] string? LogoUrl,
        [MaxLength(100)] string? DefaultCtaLabel,
        bool? IsActive
    );

    // ─── Partner links ────────────────────────────────────────────────────────

    public record AdminPartnerLinkDto(
        Guid LinkId,
        Guid PartnerId,
        string PartnerName,
        Guid? DestinationId,
        Guid? WishlistId,
        string TargetType,      // "destination" | "wishlist"
        string? TargetName,
        string BookingUrl,
        string TrackingUrl,     // /api/out/{linkId}
        string? CtaLabel,
        bool IsSponsored,
        int Priority,
        bool IsActive,
        DateTime? StartsAt,
        DateTime? EndsAt,
        DateTime CreatedAt
    );

    /// <summary>Exactly one of DestinationId / WishlistId is required.</summary>
    public record CreatePartnerLinkRequest(
        [Required] Guid PartnerId,
        Guid? DestinationId,
        Guid? WishlistId,
        [Required, MaxLength(2000)] string BookingUrl,
        [MaxLength(100)] string? CtaLabel,
        bool IsSponsored = false,
        [Range(-1000, 1000)] int Priority = 0,
        bool IsActive = true,
        DateTime? StartsAt = null,
        DateTime? EndsAt = null
    );

    /// <summary>
    /// Partial update: null = leave unchanged. Setting DestinationId or WishlistId
    /// retargets the link (the other target is cleared; sending both is a 400).
    /// CtaLabel "" clears it; ClearStartsAt / ClearEndsAt remove the window bounds.
    /// </summary>
    public record UpdatePartnerLinkRequest(
        Guid? DestinationId,
        Guid? WishlistId,
        [MaxLength(2000)] string? BookingUrl,
        [MaxLength(100)] string? CtaLabel,
        bool? IsSponsored,
        [Range(-1000, 1000)] int? Priority,
        bool? IsActive,
        DateTime? StartsAt,
        DateTime? EndsAt,
        bool ClearStartsAt = false,
        bool ClearEndsAt = false
    );

    // ─── Click stats ──────────────────────────────────────────────────────────

    public record ClickStatsDto(
        DateTime From,
        DateTime To,
        int TotalClicks,
        int UniqueUsers,        // distinct logged-in users; anonymous clicks excluded
        List<DailyClicksDto> Daily
    );

    public record DailyClicksDto(DateOnly Date, int Clicks);

    // ─── Leads ────────────────────────────────────────────────────────────────

    public record AdminPartnerLeadDto(
        Guid Id,
        string CompanyName,
        string ContactName,
        string Email,
        string? Website,
        string? BusinessType,
        List<string> Interests,
        string? Message,
        string Status,
        string? InternalNotes,
        DateTime CreatedAt,
        DateTime? UpdatedAt
    );

    public record AdminPagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

    /// <summary>Partial update: null = leave unchanged. InternalNotes "" clears it.</summary>
    public record UpdatePartnerLeadRequest(
        [MaxLength(20)] string? Status,
        [MaxLength(5000)] string? InternalNotes
    );
}
