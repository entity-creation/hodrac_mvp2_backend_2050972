using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hodrac_Backend_MVP2.Models;

// ─── CurrencyRateSnapshot ─────────────────────────────────────────────────────
// Latest exchange rates, one row per base currency (we only store USD and
// derive other bases). Persisted so a restart — or a provider outage at
// startup — still serves the last known rates.

public class CurrencyRateSnapshot
{
    [Key, MaxLength(3)]
    public string BaseCurrency { get; set; } = "USD";

    /// <summary>JSON object: { "EUR": 0.92, "GBP": 0.79, ... } — units per 1 BaseCurrency.</summary>
    [Column(TypeName = "jsonb")]
    public string RatesJson { get; set; } = "{}";

    /// <summary>When the provider says these rates were published.</summary>
    public DateTime ProviderUpdatedAt { get; set; }

    /// <summary>When we last fetched them.</summary>
    public DateTime FetchedAt { get; set; }

    [MaxLength(100)]
    public string Provider { get; set; } = string.Empty;
}
