using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.DTOs.EventDtos
{
    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Where the event happens. DestinationId/DestinationName are set when the venue is a
    /// known destination; Name/Address are the free-text venue (Name falls back to the
    /// destination name when only a destination is set).
    /// </summary>
    public record EventVenueDto(Guid? DestinationId, string? DestinationName, string? Name, string? Address);

    public record EventCityDto(Guid Id, string Name);

    /// <summary>Times are UTC; display them in TimeZone. Prices are kobo: null = unknown, 0 = free.</summary>
    public record EventDto(
        Guid Id,
        string Name,
        string Slug,
        string Description,
        string? Organiser,
        DateTime StartsAt,
        DateTime? EndsAt,
        string TimeZone,
        EventVenueDto Venue,
        EventCityDto City,
        string? TicketUrl,
        long? PriceMinKobo,
        long? PriceMaxKobo,
        string Currency,
        string? PriceNote,
        bool IsFree,
        string? InstagramHandle,
        string? WebsiteUrl,
        string? ImageUrl
    );

    /// <summary>Compact form used on destination detail (upcomingEvents).</summary>
    public record EventSummaryDto(
        Guid Id,
        string Name,
        string Slug,
        DateTime StartsAt,
        DateTime? EndsAt,
        bool IsFree,
        long? PriceMinKobo
    );

    // ── Admin ─────────────────────────────────────────────────────────────────

    public record AdminEventDto(
        Guid Id,
        string Name,
        string Slug,
        string Description,
        string? Organiser,
        DateTime StartsAt,
        DateTime? EndsAt,
        string TimeZone,
        EventVenueDto Venue,
        EventCityDto City,
        string? TicketUrl,
        long? PriceMinKobo,
        long? PriceMaxKobo,
        string Currency,
        string? PriceNote,
        bool IsFree,
        string? InstagramHandle,
        string? WebsiteUrl,
        string? ImageUrl,
        bool IsPublished,
        bool IsUpcoming,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    /// <summary>
    /// POST /api/admin/events. StartsAt/EndsAt accept any offset (e.g. "2026-11-28T12:00:00+01:00")
    /// and are stored as UTC. CityId may be omitted when DestinationId is given (defaults to the
    /// destination's first city). TimeZone defaults to "Africa/Lagos", Currency to "NGN".
    /// </summary>
    public record CreateEventRequest(
        [MaxLength(200)] string Name,
        string? Description,
        [MaxLength(200)] string? Organiser,
        DateTimeOffset StartsAt,
        DateTimeOffset? EndsAt,
        [MaxLength(100)] string? TimeZone,
        Guid? DestinationId,
        [MaxLength(200)] string? VenueName,
        [MaxLength(300)] string? VenueAddress,
        Guid? CityId,
        [MaxLength(500)] string? TicketUrl,
        long? PriceMinKobo,
        long? PriceMaxKobo,
        [MaxLength(3)] string? Currency,
        [MaxLength(300)] string? PriceNote,
        [MaxLength(100)] string? InstagramHandle,
        [MaxLength(500)] string? WebsiteUrl,
        [MaxLength(500)] string? ImageUrl,
        bool IsPublished = true
    );

    /// <summary>
    /// PATCH /api/admin/events/{id}. Null = leave unchanged; "" clears an optional text/URL field.
    /// Clear* flags reset EndsAt / DestinationId / both prices to null. The slug never changes.
    /// </summary>
    public record UpdateEventRequest(
        [MaxLength(200)] string? Name,
        string? Description,
        [MaxLength(200)] string? Organiser,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        bool? ClearEndsAt,
        [MaxLength(100)] string? TimeZone,
        Guid? DestinationId,
        bool? ClearDestination,
        [MaxLength(200)] string? VenueName,
        [MaxLength(300)] string? VenueAddress,
        Guid? CityId,
        [MaxLength(500)] string? TicketUrl,
        long? PriceMinKobo,
        long? PriceMaxKobo,
        bool? ClearPrices,
        [MaxLength(3)] string? Currency,
        [MaxLength(300)] string? PriceNote,
        [MaxLength(100)] string? InstagramHandle,
        [MaxLength(500)] string? WebsiteUrl,
        [MaxLength(500)] string? ImageUrl,
        bool? IsPublished
    );
}
