using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Services.Payments;

/// <summary>
/// Validates a hosted trip's price / deposit / instalment schedule. Pure.
/// Rules: price &gt; 0; 0 &lt; deposit ≤ price; instalment amounts &gt; 0;
/// sequences are exactly 1..n; due dates strictly increasing (by sequence)
/// and before StartDate when it's set; deposit + Σ instalments == price.
/// Returns field → message (empty when valid). Keys are camelCase request
/// field names, e.g. "depositKobo", "instalments[1].dueDate".
/// </summary>
public static class InstalmentValidator
{
    public const string PriceField = "pricePerSeatKobo";
    public const string DepositField = "depositKobo";
    public const string InstalmentsField = "instalments";

    public static Dictionary<string, string> Validate(
        long? pricePerSeatKobo,
        long? depositKobo,
        IReadOnlyCollection<TripInstalment>? instalments,
        DateOnly? startDate)
    {
        var errors = new Dictionary<string, string>();
        var lines = (instalments ?? Array.Empty<TripInstalment>()).ToList();

        if (pricePerSeatKobo is null)
            errors[PriceField] = "Price per seat is required.";
        else if (pricePerSeatKobo <= 0)
            errors[PriceField] = "Price per seat must be greater than 0.";

        if (depositKobo is null)
            errors[DepositField] = "Deposit is required.";
        else if (depositKobo <= 0)
            errors[DepositField] = "Deposit must be greater than 0.";
        else if (pricePerSeatKobo is > 0 && depositKobo > pricePerSeatKobo)
            errors[DepositField] = "Deposit can't be more than the price per seat.";

        // Sequences must be exactly 1..n (no gaps, no duplicates).
        var sequences = lines.Select(l => l.Sequence).OrderBy(s => s).ToList();
        if (!sequences.SequenceEqual(Enumerable.Range(1, lines.Count)))
            errors[InstalmentsField] = "Instalments must be numbered 1, 2, 3... with no gaps or repeats.";

        var ordered = lines.OrderBy(l => l.Sequence).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var line = ordered[i];
            var key = $"{InstalmentsField}[{i}]";

            if (line.AmountKobo <= 0)
                errors[$"{key}.amountKobo"] = "Instalment amount must be greater than 0.";

            if (i > 0 && line.DueDate <= ordered[i - 1].DueDate)
                errors[$"{key}.dueDate"] = "Each instalment must be due after the one before it.";
            else if (startDate is not null && line.DueDate >= startDate.Value)
                errors[$"{key}.dueDate"] = "Instalments must be due before the trip starts.";
        }

        // Only meaningful once the individual amounts are valid.
        if (pricePerSeatKobo is > 0 && depositKobo is > 0 && lines.All(l => l.AmountKobo > 0))
        {
            decimal total = depositKobo.Value + lines.Sum(l => (decimal)l.AmountKobo);
            if (total != pricePerSeatKobo.Value)
                errors.TryAdd(InstalmentsField,
                    "The deposit plus all instalments must add up to the price per seat.");
        }

        return errors;
    }
}
