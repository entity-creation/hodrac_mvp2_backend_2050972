namespace Hodrac_Backend_MVP2.DTOs.WishlistDtos
{
    public record ItineraryDayDto(
    int DayNumber,
    string DayTitle,
    string? MorningCity,
    string? AfternoonCity,
    string? EveningCity,
    TransitRouteDto? TransitFromPreviousDay,
    List<ItineraryItemDto> Items
);
}
