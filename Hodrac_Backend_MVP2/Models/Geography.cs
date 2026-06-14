using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hodrac_Backend_MVP2.Models;

// ─── Country ─────────────────────────────────────────────────────────────────

public class Country
{
    public Guid CountryId { get; set; }

    [Required, MaxLength(100)]
    public string CountryName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Continent { get; set; } = string.Empty;

    [MaxLength(10)]
    public string CountryFlagEmoji { get; set; } = string.Empty;

    public string GlobalHeroImage { get; set; } = string.Empty;

    public string VisaRequirementsSummary { get; set; } = string.Empty;

    [MaxLength(50)]
    public string PowerPlugType { get; set; } = string.Empty;

    [MaxLength(20)]
    public string DrivingSide { get; set; } = string.Empty;        // "Left" | "Right"

    [Column(TypeName = "decimal(5,4)")]
    public decimal EstimatedDailyTaxRate { get; set; }

    // Navigation
    public ICollection<Destination> Destinations { get; set; } = new List<Destination>();
    public ICollection<City> Cities { get; set; } = new List<City>();
    public ICollection<CountryLanguage> CountryLanguages { get; set; } = new List<CountryLanguage>();
}

// ─── City ─────────────────────────────────────────────────────────────────────

public class City
{
    public Guid CityId { get; set; }

    [Required, MaxLength(100)]
    public string CityName { get; set; } = string.Empty;

    public string CityDescription { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public Guid CountryId { get; set; }
    public Country Country { get; set; } = null!;

    // Self-referencing transit
    public ICollection<TransitRoute> OriginRoutes { get; set; } = new List<TransitRoute>();
    public ICollection<TransitRoute> DestinationRoutes { get; set; } = new List<TransitRoute>();
    public ICollection<DestinationCity> DestinationCities { get; set; } = new List<DestinationCity>();
}

// ─── TransitRoute ─────────────────────────────────────────────────────────────

public class TransitRoute
{
    public Guid TransitRouteId { get; set; }

    public Guid OriginCityId { get; set; }
    public City OriginCity { get; set; } = null!;

    public Guid DestinationCityId { get; set; }
    public City DestinationCity { get; set; } = null!;

    /// <summary>"Shinkansen" | "Flight" | "Ferry" | "Private Driver"</summary>
    [MaxLength(100)]
    public string TransitType { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal EstimatedCostPerPerson { get; set; }

    public int DurationInMinutes { get; set; }
    public int RecommendedTimeBufferMinutes { get; set; }

    public string BookingReferenceUrl { get; set; } = string.Empty;

    [MaxLength(20)]
    public string CarbonFootprintKg { get; set; } = string.Empty;

    /// <summary>JSON array: [{type:"taxi", duration:15, cost:12}]</summary>
    [Column(TypeName = "jsonb")]
    public string SubSegmentsJson { get; set; } = "[]";
}
