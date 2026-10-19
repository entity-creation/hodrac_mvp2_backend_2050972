namespace Hodrac_Backend_MVP2.DTOs.WishlistDtos
{
    // ─── Itinerary editing (user wishlists / forks) ───────────────────────────
    // Used by /api/user-wishlists/{id}/itinerary/... — every endpoint returns the
    // refreshed WishlistDetailDto. For the Guid? city/route fields on updates:
    // null = leave unchanged, Guid.Empty = clear the value.

    /// <summary>POST .../itinerary/days. DayNumber = 1-based insert position (default: append).</summary>
    public record AddItineraryDayRequest(
        string? DayTitle,
        int? DayNumber,
        Guid? MorningCityId,
        Guid? AfternoonCityId,
        Guid? EveningCityId,
        Guid? TransitFromPreviousDayRouteId
    );

    /// <summary>PUT .../itinerary/days/{dayId}. Only provided fields are applied.</summary>
    public record UpdateItineraryDayRequest(
        string? DayTitle,
        Guid? MorningCityId,
        Guid? AfternoonCityId,
        Guid? EveningCityId,
        Guid? TransitFromPreviousDayRouteId
    );

    /// <summary>POST .../itinerary/days/{dayId}/items. OrderIndex = 1-based insert position (default: append).</summary>
    public record AddItineraryItemRequest(
        string Title,
        string? Description,
        string? TimeOfDay,            // "Morning" | "Afternoon" | "Evening"
        string? ImageUrl,
        string? SocialProofBadge,
        decimal? CostModifier,
        bool? IsOptional,
        bool? IsSelectedByDefault,
        int? OrderIndex
    );

    /// <summary>PUT .../itinerary/days/{dayId}/items/{itemId}. Only provided fields are applied.</summary>
    public record UpdateItineraryItemRequest(
        string? Title,
        string? Description,
        string? TimeOfDay,
        string? ImageUrl,
        string? SocialProofBadge,
        decimal? CostModifier,
        bool? IsOptional,
        bool? IsSelectedByDefault
    );

    /// <summary>
    /// PUT .../itinerary/order.
    /// DayOrder: every day id of the wishlist, in the new order (DayNumber becomes 1..n).
    /// ItemOrder: per day, the item ids in the new order (ItemOrderIndex becomes 1..n).
    /// An item id listed under a different day moves to that day. Each listed day's
    /// list must contain every item that will end up in it. Either part may be omitted.
    /// </summary>
    public record ReorderItineraryRequest(
        List<Guid>? DayOrder,
        List<ItineraryDayItemOrder>? ItemOrder
    );

    public record ItineraryDayItemOrder(Guid DayId, List<Guid> ItemIds);
}
