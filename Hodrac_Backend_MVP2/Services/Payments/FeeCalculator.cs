using Hodrac_Backend_MVP2.Models;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Services.Payments;

/// <summary>
/// The amounts for one Paystack charge, all in kobo.
/// Paystack split: amount = TotalChargedKobo, subaccount = host,
/// transaction_charge = TransactionChargeKobo (Hodrac's flat fee), bearer = Bearer.
/// </summary>
public record FeeQuote(
    long SeatAmountKobo,
    long HodracFeeKobo,
    long TravellerFeeKobo,
    long TotalChargedKobo,
    long TransactionChargeKobo,
    string Bearer)
{
    /// <summary>Paystack's own fee is borne by the host's subaccount, never Hodrac's main account.</summary>
    public const string SubaccountBearer = "subaccount";
}

/// <summary>
/// Hodrac Host fee rules (spec "Fee rules"). Pure: no I/O, the clock is passed in.
/// <code>
/// fee = 0  if host.Plan == Operator &amp;&amp; OperatorPlanExpiresAt &gt; now
/// fee = 0  if trip.IsFoundingFreeDeparture
/// fee = round(seatAmount * FeePercent / 100)   otherwise (half away from zero)
/// travellerPays = seatAmount + (FeeMode == PassToTraveller ? fee : 0)
/// </code>
/// </summary>
public class FeeCalculator
{
    private readonly decimal _feePercent;

    public FeeCalculator(IOptions<HodracHostOptions> options) : this(options.Value.FeePercent) { }

    public FeeCalculator(decimal feePercent)
    {
        if (feePercent < 0 || feePercent > 100)
            throw new ArgumentOutOfRangeException(nameof(feePercent), feePercent, "Fee percent must be between 0 and 100.");
        _feePercent = feePercent;
    }

    public decimal FeePercent => _feePercent;

    public FeeQuote Calculate(TripPost trip, HostProfile host, long seatAmountKobo, DateTime nowUtc) =>
        Calculate(seatAmountKobo, trip.FeeMode, trip.IsFoundingFreeDeparture, host.Plan, host.OperatorPlanExpiresAt, nowUtc);

    public FeeQuote Calculate(
        long seatAmountKobo,
        FeeMode feeMode,
        bool isFoundingFreeDeparture,
        HostPlan plan,
        DateTime? operatorPlanExpiresAt,
        DateTime nowUtc)
    {
        if (seatAmountKobo <= 0)
            throw new ArgumentOutOfRangeException(nameof(seatAmountKobo), seatAmountKobo, "Seat amount must be greater than 0.");

        var feeWaived =
            (plan == HostPlan.Operator && operatorPlanExpiresAt is not null && operatorPlanExpiresAt.Value > nowUtc)
            || isFoundingFreeDeparture;

        var fee = feeWaived
            ? 0L
            : (long)Math.Round(seatAmountKobo * _feePercent / 100m, 0, MidpointRounding.AwayFromZero);

        var travellerFee = feeMode == FeeMode.PassToTraveller ? fee : 0L;

        return new FeeQuote(
            SeatAmountKobo: seatAmountKobo,
            HodracFeeKobo: fee,
            TravellerFeeKobo: travellerFee,
            TotalChargedKobo: checked(seatAmountKobo + travellerFee),
            TransactionChargeKobo: fee,
            Bearer: FeeQuote.SubaccountBearer);
    }
}
