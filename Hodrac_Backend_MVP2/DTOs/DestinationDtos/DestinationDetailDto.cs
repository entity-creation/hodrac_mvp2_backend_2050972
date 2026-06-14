using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;

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
    List<DestinationImageDto> Images
);
}
