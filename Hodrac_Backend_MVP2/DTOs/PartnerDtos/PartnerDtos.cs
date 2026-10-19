using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.DTOs.PartnerDtos
{
    public record PartnerDto(
        Guid PartnerId,
        string Name,
        string? LogoUrl,
        string BookingUrl,
        string TrackingUrl,     // GET /api/out/{linkId} — logs the click, then 302s to BookingUrl
        string CtaLabel,
        bool IsSponsored
    );

    public record CreatePartnerLeadRequest(
        [Required, MaxLength(200)] string CompanyName,
        [Required, MaxLength(200)] string ContactName,
        [Required, EmailAddress, MaxLength(320)] string Email,
        [MaxLength(500)] string? Website,
        [MaxLength(100)] string? BusinessType,
        [MaxLength(20)] List<string>? Interests,
        [MaxLength(5000)] string? Message
    );
}
