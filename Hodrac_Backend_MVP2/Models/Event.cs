using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.Models;

/// <summary>
/// A dated happening (festival, party, concert) in a city. The venue is either a
/// known <see cref="Destination"/> (DestinationId) or free text (VenueName / VenueAddress).
/// Times are stored in UTC; <see cref="TimeZone"/> is the IANA zone to display them in.
/// Prices are in kobo (minor units of <see cref="Currency"/>): null = unknown, 0 = free.
/// </summary>
public class Event
{
    public const int MaxNameLength = 200;
    public const int MaxSlugLength = 220;
    public const int MaxOrganiserLength = 200;
    public const int MaxVenueNameLength = 200;
    public const int MaxVenueAddressLength = 300;
    public const int MaxUrlLength = 500;
    public const int MaxPriceNoteLength = 300;
    public const int MaxInstagramHandleLength = 100;
    public const int CurrencyLength = 3;
    public const string DefaultTimeZone = "Africa/Lagos";
    public const string DefaultCurrency = "NGN";

    public Guid Id { get; set; }

    [Required, MaxLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>URL key, e.g. "oga-bole-festival-2026". Unique; generated on create and kept stable.</summary>
    [Required, MaxLength(MaxSlugLength)]
    public string Slug { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [MaxLength(MaxOrganiserLength)]
    public string? Organiser { get; set; }

    // ── When ──────────────────────────────────────────────────────────────────
    /// <summary>UTC.</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>UTC. Null = end time not announced.</summary>
    public DateTime? EndsAt { get; set; }

    /// <summary>IANA zone (controlled list: DestinationReferenceData.NormalizeTimeZone).</summary>
    [MaxLength(100)]
    public string TimeZone { get; set; } = DefaultTimeZone;

    // ── Where ─────────────────────────────────────────────────────────────────
    /// <summary>The venue when it is a known destination. Cleared if the destination is deleted.</summary>
    public Guid? DestinationId { get; set; }
    public Destination? Destination { get; set; }

    [MaxLength(MaxVenueNameLength)]
    public string? VenueName { get; set; }

    [MaxLength(MaxVenueAddressLength)]
    public string? VenueAddress { get; set; }

    public Guid CityId { get; set; }
    public City City { get; set; } = null!;

    // ── Tickets ───────────────────────────────────────────────────────────────
    [MaxLength(MaxUrlLength)]
    public string? TicketUrl { get; set; }

    /// <summary>Kobo. Null = unknown; 0 = free.</summary>
    public long? PriceMinKobo { get; set; }

    /// <summary>Kobo. Null = unknown; 0 = free.</summary>
    public long? PriceMaxKobo { get; set; }

    [MaxLength(CurrencyLength)]
    public string Currency { get; set; } = DefaultCurrency;

    /// <summary>Free text, e.g. "Early bird ₦5,000; VIP tables on request".</summary>
    [MaxLength(MaxPriceNoteLength)]
    public string? PriceNote { get; set; }

    // ── Links ─────────────────────────────────────────────────────────────────
    /// <summary>Without the leading "@".</summary>
    [MaxLength(MaxInstagramHandleLength)]
    public string? InstagramHandle { get; set; }

    [MaxLength(MaxUrlLength)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(MaxUrlLength)]
    public string? ImageUrl { get; set; }

    // ── Admin ─────────────────────────────────────────────────────────────────
    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
