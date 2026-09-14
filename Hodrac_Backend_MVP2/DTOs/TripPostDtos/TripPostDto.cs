using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.DTOs.TripPostDtos
{
    public record TripStopDto(Guid DestinationId, string Name, string? ImageUrl);

    // Flattens a TripPost — whichever mode it's in — into one shape so the
    // frontend never has to branch on DestinationId vs WishlistId.
    public record TripPostDto(
        Guid Id,
        Guid AuthorUserId,
        string DestinationLabel,      // "Kyoto, Japan" OR "Tokyo → Kyoto → Osaka"
        List<TripStopDto> Stops,      // 1 entry for Destination mode, N for Wishlist mode
        Guid? WishlistId,             // present when this is an itinerary trip, so the
                                       // client can deep-link to the full wishlist/pricing
        DateOnly? StartDate,
        DateOnly? EndDate,
        bool IsDateFlexible,
        string Caption,
        string? CoverImageUrl,
        int? MaxGroupSize,
        TripPostStatus Status,
        DateTime CreatedAt)
    {
        public static TripPostDto FromEntity(TripPost trip)
        {
            if (trip.Destination is not null)
            {
                var img = trip.CoverImageUrl
                    ?? trip.Destination.Images?.FirstOrDefault()?.ImageUrl;

                return new TripPostDto(
                    trip.Id,
                    trip.AuthorUserId,
                    DestinationLabel: $"{trip.Destination.DestinationName}, {trip.Destination.Country?.CountryName}".Trim(',', ' '),
                    Stops: new List<TripStopDto> { new(trip.Destination.DestinationId, trip.Destination.DestinationName, img) },
                    WishlistId: null,
                    trip.StartDate, trip.EndDate, trip.IsDateFlexible,
                    trip.Caption, img, trip.MaxGroupSize, trip.Status, trip.CreatedAt);
            }

            if (trip.Wishlist is not null)
            {
                // WishlistDestination is a plain join table (no ordering
                // column), so stops are shown alphabetically for a stable,
                // predictable order rather than implying a sequence that
                // isn't actually stored.
                var orderedStops = trip.Wishlist.WishlistDestinations
                    .OrderBy(wd => wd.Destination.DestinationName)
                    .Select(wd => new TripStopDto(
                        wd.Destination.DestinationId,
                        wd.Destination.DestinationName,
                        wd.Destination.Images?.FirstOrDefault()?.ImageUrl))
                    .ToList();

                var label = orderedStops.Count > 0
                    ? string.Join(" · ", orderedStops.Select(s => s.Name))
                    : trip.Wishlist.WishlistName;

                var img = trip.CoverImageUrl ?? trip.Wishlist.WishlistHeroImage;

                return new TripPostDto(
                    trip.Id,
                    trip.AuthorUserId,
                    DestinationLabel: label,
                    Stops: orderedStops,
                    WishlistId: trip.WishlistId,
                    trip.StartDate, trip.EndDate, trip.IsDateFlexible,
                    trip.Caption, img, trip.MaxGroupSize, trip.Status, trip.CreatedAt);
            }

            // No structured Destination or Wishlist linked — fall back to
            // whatever freeform text the poster gave, then the caption, so
            // the post still has something to show while the catalog is
            // sparse. It just won't be findable via the fuzzy/phonetic
            // Destination search, only plain text match.
            return new TripPostDto(
                trip.Id,
                trip.AuthorUserId,
                DestinationLabel: !string.IsNullOrWhiteSpace(trip.FreeTextDestination)
                    ? trip.FreeTextDestination!
                    : (!string.IsNullOrWhiteSpace(trip.Caption) ? trip.Caption : "Trip"),
                Stops: new List<TripStopDto>(),
                WishlistId: null,
                trip.StartDate, trip.EndDate, trip.IsDateFlexible,
                trip.Caption, trip.CoverImageUrl, trip.MaxGroupSize, trip.Status, trip.CreatedAt);
        }
    }
}
