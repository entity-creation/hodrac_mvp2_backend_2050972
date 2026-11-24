using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hodrac_Backend_MVP2.Models
{
    public enum TripPostStatus
    {
        Open,
        Closed,
        Cancelled
    }

    // Who pays Hodrac's fee on a hosted trip: added on top of the seat price
    // as a booking fee, or taken out of the host's share. Accepted in JSON as
    // "PassToTraveller" / "HostAbsorbs" (or 0 / 1).
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FeeMode
    {
        PassToTraveller,
        HostAbsorbs
    }

    // Who picks the instalment due dates on a hosted trip. HostSet (default):
    // every instalment carries the host's DueDate. TravellerChooses: the host
    // sets the amounts and one PayInFullByDate; each traveller picks a date
    // per instalment at accept-terms (TripInterestInstalmentDate) and can move
    // unpaid ones later, up to the deadline. Accepted in JSON as
    // "HostSet" / "TravellerChooses" (or 0 / 1).
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InstalmentDatesMode
    {
        HostSet,
        TravellerChooses
    }

    // One line of a hosted trip's payment schedule after the deposit.
    // EF owned type, stored in its own table TripInstalments keyed
    // (TripPostId, Sequence).
    public class TripInstalment
    {
        public int Sequence { get; set; }
        public long AmountKobo { get; set; }
        // Required when the trip is HostSet; null when TravellerChooses.
        public DateOnly? DueDate { get; set; }
    }

    // A trip post is anchored to exactly ONE of:
    //   - a single Destination (simple "I'm going to Kyoto" post), or
    //   - a Wishlist (a multi-stop itinerary template/fork the user is
    //     actually planning to travel).
    // Enforced by a DB check constraint (see AppDbContext) plus validation
    // in TripsController on create/update — never trust the client to only
    // send one.
    public class TripPost
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid AuthorUserId { get; set; }

        public Guid? DestinationId { get; set; }
        public Destination? Destination { get; set; }

        public Guid? WishlistId { get; set; }
        public Wishlist? Wishlist { get; set; }

        // Fallback for when the poster's destination isn't in the catalog
        // yet, or they don't want to search for it. Optional, and only
        // meaningful when DestinationId and WishlistId are both null — see
        // TripPostDto.FromEntity for how these three are reconciled into
        // one label.
        public string? FreeTextDestination { get; set; }

        // These are the poster's actual planned travel dates for THIS trip,
        // independent of Wishlist.TotalDays (which is just template length).
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public bool IsDateFlexible { get; set; }

        public string Caption { get; set; } = string.Empty;

        // Optional override. If null, the UI falls back to the first
        // Destination image or the Wishlist's WishlistHeroImage — see
        // TripPostDto.FromEntity.
        public string? CoverImageUrl { get; set; }

        public int? MaxGroupSize { get; set; }

        public TripPostStatus Status { get; set; } = TripPostStatus.Open;

        // Moderation: an admin-hidden trip is removed from every listing of
        // other people's trips and 404s for everyone except its author.
        // Independent of Status (a hidden trip keeps its Open/Closed state).
        public const int MaxHiddenReasonLength = 500;
        public bool IsHidden { get; set; }
        public DateTime? HiddenAt { get; set; }
        public string? HiddenReason { get; set; }
        public Guid? HiddenByUserId { get; set; }

        // ── Hodrac Host (hosted, paid trips) ─────────────────────────────────
        // A trip is either a community trip (IsHostedTrip = false, the default:
        // free, always listed, every field below null/default) or a hosted
        // trip (paid seats through Paystack). Money is always kobo (long).
        public const int CurrencyLength = 3;
        public const string DefaultCurrency = "NGN";
        public const int DefaultSeatHoldHours = 48;

        public bool IsHostedTrip { get; set; }

        public Guid? HostProfileId { get; set; }
        public HostProfile? HostProfile { get; set; }

        // Shown in feed/browse/public lists. Community trips are always
        // listed; hosted trips are private (link-only) unless the host opts in.
        public bool IsListed { get; set; } = true;

        public long? PricePerSeatKobo { get; set; }
        public string Currency { get; set; } = DefaultCurrency;
        public long? DepositKobo { get; set; }

        // deposit + sum(instalments) == PricePerSeatKobo (see InstalmentValidator).
        public List<TripInstalment> Instalments { get; set; } = new();

        public FeeMode FeeMode { get; set; } = FeeMode.PassToTraveller;

        // Who picks the instalment dates (see InstalmentDatesMode). PayInFullByDate
        // is required (after today, before StartDate) in TravellerChooses and
        // null in HostSet.
        public InstalmentDatesMode InstalmentDatesMode { get; set; } = InstalmentDatesMode.HostSet;
        public DateOnly? PayInFullByDate { get; set; }

        // Approved-but-unpaid seats expire after this many hours.
        public int SeatHoldHours { get; set; } = DefaultSeatHoldHours;

        // The host's refund and dropout terms. Every edit bumps TermsVersion.
        public string? TermsText { get; set; }
        public int TermsVersion { get; set; } = 1;
        public DateTime? TermsUpdatedAt { get; set; }

        public string? Inclusions { get; set; }
        public string? Exclusions { get; set; }

        // Set by the server when this trip uses the host's free founding departure.
        public bool IsFoundingFreeDeparture { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<TripInterest> Interests { get; set; } = new List<TripInterest>();
        public TripThread? Thread { get; set; }
    }
}
