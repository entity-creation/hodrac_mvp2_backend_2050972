namespace Hodrac_Backend_MVP2.DTOs.WishlistDtos
{
    public record ItineraryItemDto(
    Guid ItemId,
    string Title,
    string Description,
    string TimeOfDay,
    string ImageUrl,
    string SocialProofBadge,
    decimal CostModifier,
    bool IsOptional,
    bool IsSelectedByDefault
);
}
