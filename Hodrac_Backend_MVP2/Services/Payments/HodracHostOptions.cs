namespace Hodrac_Backend_MVP2.Services.Payments;

// ─── Hodrac Host configuration ────────────────────────────────────────────────
// Bound from the "Hodrac" section (env vars Hodrac__FeePercent, ...). Defaults
// match the spec, so nothing needs to be in appsettings.

public class HodracHostOptions
{
    public const string SectionName = "Hodrac";

    /// <summary>Hodrac's fee as a percentage of each seat payment (2.5 = 2.5%).</summary>
    public decimal FeePercent { get; set; } = 2.5m;

    /// <summary>At most this many hosts can be marked founding.</summary>
    public int FoundingHostLimit { get; set; } = 30;

    /// <summary>Founding hosts can only be marked (and use their free departure) until this date.</summary>
    public DateOnly FoundingDeadline { get; set; } = new(2027, 2, 28);

    /// <summary>Default seat hold for new hosted trips (TripPost.SeatHoldHours).</summary>
    public int DefaultSeatHoldHours { get; set; } = 48;
}

// ─── Paystack configuration ───────────────────────────────────────────────────
// Bound from the "Paystack" section. The secret key comes from env vars /
// user-secrets only (Paystack__SecretKey), never appsettings*.json.

public class PaystackOptions
{
    public const string SectionName = "Paystack";

    public string SecretKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.paystack.co";

    /// <summary>Frontend callback page, e.g. https://www.hodrac.com/trips/payment/callback.</summary>
    public string CallbackUrl { get; set; } = string.Empty;
}
