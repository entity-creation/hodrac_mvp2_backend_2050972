using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;

namespace Hodrac_Backend_MVP2.DTOs.TripPostDtos
{
    public record TripStopDto(Guid DestinationId, string Name, string? ImageUrl);

    public static class TripTypes
    {
        public const string Community = "community";
        public const string Hosted = "hosted";
    }

    // The trip's location label (TripPostDto.DestinationLabel, host dashboard):
    // a destination trip shows "Name, Country"; a wishlist trip its stops
    // (alphabetical, joined by " · ") or the wishlist name when it has none;
    // otherwise the free text, then the caption, then "Trip".
    public static class TripLocationLabel
    {
        public static string ForDestination(string destinationName, string? countryName) =>
            $"{destinationName}, {countryName}".Trim(',', ' ');

        // Callers pass the stops already in display order (alphabetical by name).
        public static string ForWishlist(IReadOnlyCollection<string> orderedStopNames, string wishlistName) =>
            orderedStopNames.Count > 0 ? string.Join(" · ", orderedStopNames) : wishlistName;

        public static string ForFreeText(string? freeTextDestination, string? caption) =>
            !string.IsNullOrWhiteSpace(freeTextDestination) ? freeTextDestination!
            : !string.IsNullOrWhiteSpace(caption) ? caption! : "Trip";
    }

    // DueDate is null when the trip's instalment dates are chosen by each traveller.
    public record TripInstalmentDto(int Sequence, long AmountKobo, DateOnly? DueDate);

    // Public badge for a hosted trip's host. Never payout details, verification
    // documents or the Paystack subaccount.
    public record TripHostDto(string DisplayName, string Slug, string? LogoUrl, bool Verified);

    // The caller's next payment on a hosted trip. Kind: "Deposit" | "Instalment";
    // TotalKobo includes the booking fee the traveller pays on top (if any);
    // DueDate is null for the deposit.
    public record MyNextPaymentDto(string Kind, int? Sequence, long TotalKobo, DateOnly? DueDate);

    // The caller's own interest on a trip, projected alongside the trip in
    // one query (feeds the my* fields of TripPostDto). Never sent as is.
    public record TripViewerInstalmentDate(int Sequence, DateOnly DueDate);

    public class TripViewerInterest
    {
        public Guid Id { get; init; }
        public TripInterestStatus Status { get; init; }
        public InterestPaymentStatus PaymentStatus { get; init; }
        public DateTime? SeatHoldExpiresAt { get; init; }
        public long AmountPaidKobo { get; init; }
        public int? TermsVersionAccepted { get; init; }
        // The viewer's chosen instalment dates (TravellerChooses trips).
        public List<TripViewerInstalmentDate>? InstalmentDates { get; init; }

        public Dictionary<int, DateOnly> DateMap() =>
            (InstalmentDates ?? new List<TripViewerInstalmentDate>())
                .GroupBy(d => d.Sequence)
                .ToDictionary(g => g.Key, g => g.First().DueDate);

        public static TripViewerInterest From(TripInterest i, IReadOnlyDictionary<int, DateOnly>? dates = null) => new()
        {
            Id = i.Id,
            Status = i.Status,
            PaymentStatus = i.PaymentStatus,
            SeatHoldExpiresAt = i.SeatHoldExpiresAt,
            AmountPaidKobo = i.AmountPaidKobo,
            TermsVersionAccepted = i.TermsVersionAccepted,
            InstalmentDates = dates?.Select(d => new TripViewerInstalmentDate(d.Key, d.Value)).ToList()
        };
    }

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
        string? MyInterestStatus,
        TripPostStatus Status,
        DateTime CreatedAt,
        // Participant counts - signed-in responses only (PublicTripPostDto
        // never carries them). ApprovedCount excludes the author; GoingCount
        // is ApprovedCount + 1 (the author); SpotsLeft is null when the trip
        // has no MaxGroupSize.
        int ApprovedCount = 0,
        int GoingCount = 1,
        int? SpotsLeft = null,
        // Hodrac Host. TripType is "community" or "hosted"; for community trips
        // every hosted field below is null (and IsListed is always true).
        // TravellerFeePreviewKobo: the booking fee a traveller would pay on the
        // full seat price (0 when the host absorbs the fee or it's waived).
        // SeatsLeft: MaxGroupSize - 1 (author) - approved, non-expired seats.
        string TripType = TripTypes.Community,
        bool IsHostedTrip = false,
        bool IsListed = true,
        long? PricePerSeatKobo = null,
        string? Currency = null,
        long? DepositKobo = null,
        List<TripInstalmentDto>? Instalments = null,
        string? FeeMode = null,
        long? TravellerFeePreviewKobo = null,
        int? SeatsLeft = null,
        string? Inclusions = null,
        string? Exclusions = null,
        int? TermsVersion = null,
        TripHostDto? Host = null,
        // The signed-in caller's own (non-withdrawn) interest on this trip;
        // all null when they have none. MyInterestId is set on every trip
        // type; the payment fields only on hosted trips. MyNextPayment is the
        // first unpaid schedule line (deposit, then instalments in order) with
        // its total including any booking fee; null unless the caller is
        // approved with a live (non-expired) seat and something is still owed.
        Guid? MyInterestId = null,
        string? MyPaymentStatus = null,
        DateTime? MySeatHoldExpiresAt = null,
        long? MyAmountPaidKobo = null,
        long? MyOwesKobo = null,
        int? MyTermsVersionAccepted = null,
        MyNextPaymentDto? MyNextPayment = null,
        // Hosted trips: "HostSet" | "TravellerChooses" (null on community trips),
        // and the pay-in-full deadline (TravellerChooses only).
        string? InstalmentDatesMode = null,
        DateOnly? PayInFullByDate = null)
    {
        // approvedCount: approved, non-expired members excluding the author.
        // fees: needed for TravellerFeePreviewKobo (left null without it).
        // myInterest: the caller's own non-withdrawn interest (my* fields).
        public static TripPostDto FromEntity(
            TripPost trip, string? myInterestStatus = null, int approvedCount = 0, FeeCalculator? fees = null,
            TripViewerInterest? myInterest = null)
        {
            var dto = Build(trip, myInterestStatus) with { MyInterestId = myInterest?.Id };
            dto = WithMyPayment(dto, trip, myInterest, fees);
            var going = Math.Max(approvedCount, 0) + 1;
            dto = dto with
            {
                ApprovedCount = Math.Max(approvedCount, 0),
                GoingCount = going,
                SpotsLeft = trip.MaxGroupSize is int max ? Math.Max(max - going, 0) : null,
                IsListed = !trip.IsHostedTrip || trip.IsListed
            };

            var hosted = HostedTripProjection.From(trip, approvedCount, fees);
            if (hosted is null) return dto;

            return dto with
            {
                TripType = TripTypes.Hosted,
                IsHostedTrip = true,
                PricePerSeatKobo = hosted.PricePerSeatKobo,
                Currency = hosted.Currency,
                DepositKobo = hosted.DepositKobo,
                Instalments = hosted.Instalments,
                FeeMode = hosted.FeeMode,
                TravellerFeePreviewKobo = hosted.TravellerFeePreviewKobo,
                SeatsLeft = hosted.SeatsLeft,
                Inclusions = hosted.Inclusions,
                Exclusions = hosted.Exclusions,
                TermsVersion = hosted.TermsVersion,
                Host = hosted.Host,
                InstalmentDatesMode = hosted.InstalmentDatesMode,
                PayInFullByDate = hosted.PayInFullByDate
            };
        }

        // Hosted trips only: the caller's payment state, from the same
        // allocation (PaymentSchedule) and fee rules the balance endpoint uses.
        private static TripPostDto WithMyPayment(
            TripPostDto dto, TripPost trip, TripViewerInterest? mine, FeeCalculator? fees)
        {
            if (mine is null || !trip.IsHostedTrip) return dto;

            var price = trip.PricePerSeatKobo ?? 0;
            MyNextPaymentDto? next = null;
            if (mine.Status == TripInterestStatus.Approved && mine.PaymentStatus != InterestPaymentStatus.Expired)
            {
                var line = PaymentSchedule.Allocate(trip, mine.AmountPaidKobo, mine.DateMap()).FirstOrDefault(l => !l.IsPaid);
                if (line is not null)
                {
                    var total = fees?.Calculate(trip, trip.HostProfile ?? new HostProfile(), line.RemainingKobo, DateTime.UtcNow)
                        .TotalChargedKobo ?? line.RemainingKobo;
                    next = new MyNextPaymentDto(line.Kind.ToString(), line.Sequence, total, line.DueDate);
                }
            }

            return dto with
            {
                MyPaymentStatus = mine.PaymentStatus.ToString(),
                MySeatHoldExpiresAt = mine.SeatHoldExpiresAt,
                MyAmountPaidKobo = mine.AmountPaidKobo,
                MyOwesKobo = Math.Max(0, price - mine.AmountPaidKobo),
                MyTermsVersionAccepted = mine.TermsVersionAccepted,
                MyNextPayment = next
            };
        }

        private static TripPostDto Build(TripPost trip, string? myInterestStatus)
        {
            if (trip.Destination is not null)
            {
                var img = trip.CoverImageUrl
                    ?? trip.Destination.Images?.FirstOrDefault()?.ImageUrl;

                return new TripPostDto(
                    trip.Id,
                    trip.AuthorUserId,
                    DestinationLabel: TripLocationLabel.ForDestination(trip.Destination.DestinationName, trip.Destination.Country?.CountryName),
                    Stops: new List<TripStopDto> { new(trip.Destination.DestinationId, trip.Destination.DestinationName, img) },
                    WishlistId: null,
                    trip.StartDate, trip.EndDate, trip.IsDateFlexible,
                    trip.Caption, img, trip.MaxGroupSize, myInterestStatus, trip.Status, trip.CreatedAt);
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

                var label = TripLocationLabel.ForWishlist(orderedStops.Select(s => s.Name).ToList(), trip.Wishlist.WishlistName);

                var img = trip.CoverImageUrl ?? trip.Wishlist.WishlistHeroImage;

                return new TripPostDto(
                    trip.Id,
                    trip.AuthorUserId,
                    DestinationLabel: label,
                    Stops: orderedStops,
                    WishlistId: trip.WishlistId,
                    trip.StartDate, trip.EndDate, trip.IsDateFlexible,
                    trip.Caption, img, trip.MaxGroupSize, myInterestStatus, trip.Status, trip.CreatedAt);
            }

            // No structured Destination or Wishlist linked — fall back to
            // whatever freeform text the poster gave, then the caption, so
            // the post still has something to show while the catalog is
            // sparse. It just won't be findable via the fuzzy/phonetic
            // Destination search, only plain text match.
            return new TripPostDto(
                trip.Id,
                trip.AuthorUserId,
                DestinationLabel: TripLocationLabel.ForFreeText(trip.FreeTextDestination, trip.Caption),
                Stops: new List<TripStopDto>(),
                WishlistId: null,
                trip.StartDate, trip.EndDate, trip.IsDateFlexible,
                trip.Caption, trip.CoverImageUrl, trip.MaxGroupSize, myInterestStatus, trip.Status, trip.CreatedAt);
        }
    }

    // Projection for logged-out callers (GET /api/trips/public). Drops the
    // author's user id and the per-viewer interest status so anonymous
    // scraping can't correlate posts to accounts.
    public record PublicTripPostDto(
        Guid Id,
        string DestinationLabel,
        List<TripStopDto> Stops,
        Guid? WishlistId,
        DateOnly? StartDate,
        DateOnly? EndDate,
        bool IsDateFlexible,
        string Caption,
        string? CoverImageUrl,
        int? MaxGroupSize,
        TripPostStatus Status,
        DateTime CreatedAt,
        // Hodrac Host, public-safe subset (all null for community trips).
        // SeatsLeft is only given for hosted trips, so community trips still
        // expose no participant counts here.
        string TripType = TripTypes.Community,
        bool IsHostedTrip = false,
        long? PricePerSeatKobo = null,
        string? Currency = null,
        long? DepositKobo = null,
        List<TripInstalmentDto>? Instalments = null,
        string? FeeMode = null,
        long? TravellerFeePreviewKobo = null,
        int? SeatsLeft = null,
        string? Inclusions = null,
        string? Exclusions = null,
        TripHostDto? Host = null,
        string? InstalmentDatesMode = null,
        DateOnly? PayInFullByDate = null)
    {
        // approvedCount only matters for hosted trips (SeatsLeft).
        public static PublicTripPostDto FromEntity(TripPost trip, int approvedCount = 0, FeeCalculator? fees = null)
        {
            var dto = TripPostDto.FromEntity(trip, approvedCount: approvedCount, fees: fees);
            return new PublicTripPostDto(
                dto.Id, dto.DestinationLabel, dto.Stops, dto.WishlistId,
                dto.StartDate, dto.EndDate, dto.IsDateFlexible,
                dto.Caption, dto.CoverImageUrl, dto.MaxGroupSize, dto.Status, dto.CreatedAt,
                dto.TripType, dto.IsHostedTrip, dto.PricePerSeatKobo, dto.Currency, dto.DepositKobo,
                dto.Instalments, dto.FeeMode, dto.TravellerFeePreviewKobo, dto.SeatsLeft,
                dto.Inclusions, dto.Exclusions, dto.Host,
                dto.InstalmentDatesMode, dto.PayInFullByDate);
        }
    }

    // The hosted-trip fields shared by TripPostDto and PublicTripPostDto.
    // Null for community trips. Never carries payout or verification data.
    public record HostedTripProjection(
        long? PricePerSeatKobo,
        string Currency,
        long? DepositKobo,
        List<TripInstalmentDto> Instalments,
        string FeeMode,
        long? TravellerFeePreviewKobo,
        int? SeatsLeft,
        string? Inclusions,
        string? Exclusions,
        int TermsVersion,
        TripHostDto? Host,
        string InstalmentDatesMode,
        DateOnly? PayInFullByDate)
    {
        public static HostedTripProjection? From(TripPost trip, int approvedCount, FeeCalculator? fees)
        {
            if (!trip.IsHostedTrip) return null;

            // The fee a traveller would pay on the full seat price.
            long? travellerFee = null;
            if (fees is not null && trip.PricePerSeatKobo is > 0)
            {
                travellerFee = fees.Calculate(
                    trip.PricePerSeatKobo.Value,
                    trip.FeeMode,
                    trip.IsFoundingFreeDeparture,
                    trip.HostProfile?.Plan ?? HostPlan.Host,
                    trip.HostProfile?.OperatorPlanExpiresAt,
                    DateTime.UtcNow).TravellerFeeKobo;
            }

            var host = trip.HostProfile is null
                ? null
                : new TripHostDto(
                    trip.HostProfile.DisplayName,
                    trip.HostProfile.Slug,
                    trip.HostProfile.LogoUrl,
                    trip.HostProfile.VerificationStatus == HostVerificationStatus.Verified);

            return new HostedTripProjection(
                trip.PricePerSeatKobo,
                trip.Currency,
                trip.DepositKobo,
                trip.Instalments
                    .OrderBy(i => i.Sequence)
                    .Select(i => new TripInstalmentDto(i.Sequence, i.AmountKobo, i.DueDate))
                    .ToList(),
                trip.FeeMode.ToString(),
                travellerFee,
                trip.MaxGroupSize is int max ? Math.Max(max - 1 - Math.Max(approvedCount, 0), 0) : null,
                trip.Inclusions,
                trip.Exclusions,
                trip.TermsVersion,
                host,
                trip.InstalmentDatesMode.ToString(),
                trip.InstalmentDatesMode == Models.InstalmentDatesMode.TravellerChooses ? trip.PayInFullByDate : null);
        }
    }
}
