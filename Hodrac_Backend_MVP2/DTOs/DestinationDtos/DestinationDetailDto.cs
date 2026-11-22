using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.DTOs.EventDtos;
using Hodrac_Backend_MVP2.DTOs.BlogDtos;

namespace Hodrac_Backend_MVP2.DTOs.DestinationDtos
{
    public record DestinationDetailDto(
    Guid DestinationId,
    string DestinationName,
    string CountryName,
    string TimeZone,
    decimal AverageCostPerDay,
    int SafetyLevel,
    int LuxuryRating,
    int FamilyFriendlyScore,
    int AdventurePaceScore,
    DescriptionJsonDto Description,
    List<string> Tags,
    List<string> Categories,
    List<string> Languages,
    List<string> Currencies,
    List<string> Cities,
    List<DestinationImageDto> Images,
    PartnerDto? Partner = null,
    bool IsFeatured = false,
    // Up to 5 published, upcoming events at this destination (detail endpoint only).
    List<EventSummaryDto>? UpcomingEvents = null,
    // Up to 3 visible blog posts linked to this destination, newest first (detail endpoint only).
    List<BlogPostSummaryDto>? RelatedPosts = null
);
}
