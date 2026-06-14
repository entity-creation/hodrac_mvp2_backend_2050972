namespace Hodrac_Backend_MVP2.DTOs.DestinationDtos
{
    public record DestinationSummaryDto(
    Guid DestinationId,
    string DestinationName,
    string Location,           // "Tokyo, Japan"
    string ThumbnailUrl,
    decimal AverageCostPerDay,
    int SafetyLevel,
    int LuxuryRating,
    List<string> Tags,
    List<string> Categories
);
}
