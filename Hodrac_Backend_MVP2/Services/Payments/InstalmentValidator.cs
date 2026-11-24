using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Services.Payments;

/// <summary>
/// Validates a hosted trip's price / deposit / instalment schedule. Pure.
/// Rules: price &gt; 0; 0 &lt; deposit ≤ price; instalment amounts &gt; 0;
/// sequences are exactly 1..n; deposit + Σ instalments == price.
/// Dates depend on <see cref="InstalmentDatesMode"/>:
///  - HostSet: every instalment has a due date, strictly increasing (by
///    sequence) and before StartDate when it's set; no payInFullByDate.
///  - TravellerChooses: instalments carry no dates; payInFullByDate is
///    required, after today (when <c>today</c> is given) and before StartDate.
/// Returns field → message (empty when valid). Keys are camelCase request
/// field names, e.g. "depositKobo", "instalments[1].dueDate".
/// </summary>
public static class InstalmentValidator
{
    public const string PriceField = "pricePerSeatKobo";
    public const string DepositField = "depositKobo";
    public const string InstalmentsField = "instalments";
    public const string PayInFullByField = "payInFullByDate";
    public const string TravellerDatesField = "instalmentDates";

    /// <summary>HostSet rules (the original behaviour).</summary>
    public static Dictionary<string, string> Validate(
        long? pricePerSeatKobo,
        long? depositKobo,
        IReadOnlyCollection<TripInstalment>? instalments,
        DateOnly? startDate) =>
        Validate(pricePerSeatKobo, depositKobo, instalments, startDate, InstalmentDatesMode.HostSet, null, null);

    /// <param name="today">
    /// WAT today: payInFullByDate must be after it. Null skips that check
    /// (an edit that doesn't touch the deadline mustn't fail once it has passed).
    /// </param>
    public static Dictionary<string, string> Validate(
        long? pricePerSeatKobo,
        long? depositKobo,
        IReadOnlyCollection<TripInstalment>? instalments,
        DateOnly? startDate,
        InstalmentDatesMode mode,
        DateOnly? payInFullByDate,
        DateOnly? today)
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

        var travellerChooses = mode == InstalmentDatesMode.TravellerChooses;
        var ordered = lines.OrderBy(l => l.Sequence).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var line = ordered[i];
            var key = $"{InstalmentsField}[{i}]";

            if (line.AmountKobo <= 0)
                errors[$"{key}.amountKobo"] = "Instalment amount must be greater than 0.";

            if (travellerChooses)
            {
                if (line.DueDate is not null)
                    errors[$"{key}.dueDate"] = "Leave instalment dates empty: travellers choose their own dates on this trip.";
                continue;
            }

            if (line.DueDate is null)
                errors[$"{key}.dueDate"] = "Each instalment needs a due date.";
            else if (i > 0 && ordered[i - 1].DueDate is { } previous && line.DueDate <= previous)
                errors[$"{key}.dueDate"] = "Each instalment must be due after the one before it.";
            else if (startDate is not null && line.DueDate >= startDate.Value)
                errors[$"{key}.dueDate"] = "Instalments must be due before the trip starts.";
        }

        if (travellerChooses)
        {
            if (payInFullByDate is null)
                errors[PayInFullByField] = "Set the date travellers must pay in full by.";
            else if (today is not null && payInFullByDate <= today.Value)
                errors[PayInFullByField] = "The pay-in-full date must be after today.";
            else if (startDate is not null && payInFullByDate >= startDate.Value)
                errors[PayInFullByField] = "The pay-in-full date must be before the trip starts.";
        }
        else if (payInFullByDate is not null)
        {
            errors[PayInFullByField] = "A pay-in-full date is only used when travellers choose their instalment dates.";
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

    /// <summary>
    /// A traveller's chosen dates on a TravellerChooses trip (accept-terms and
    /// the change-dates endpoint). Rules:
    ///  - each sequence is one of the trip's instalments (1..n), at most once;
    ///  - every unpaid instalment has a date;
    ///  - a paid instalment's date can't change (sending its current date is fine);
    ///  - unpaid dates are on or after <paramref name="today"/> (WAT) and on or
    ///    before PayInFullByDate;
    ///  - dates are strictly increasing by sequence (paid lines' kept dates included).
    /// Returns field → message, keys "instalmentDates" / "instalmentDates[i].dueDate".
    /// </summary>
    public static Dictionary<string, string> ValidateTravellerDates(
        TripPost trip,
        IReadOnlyList<(int Sequence, DateOnly DueDate)> dates,
        IReadOnlySet<int> paidSequences,
        IReadOnlyDictionary<int, DateOnly> existingDates,
        DateOnly today)
    {
        var errors = new Dictionary<string, string>();
        if (trip.InstalmentDatesMode != InstalmentDatesMode.TravellerChooses)
        {
            errors[TravellerDatesField] = "The host sets the instalment dates on this trip.";
            return errors;
        }

        var tripSequences = trip.Instalments.Select(i => i.Sequence).ToHashSet();
        var seen = new HashSet<int>();
        for (var i = 0; i < dates.Count; i++)
        {
            var (sequence, due) = dates[i];
            var key = $"{TravellerDatesField}[{i}]";
            if (!tripSequences.Contains(sequence))
            {
                errors[$"{key}.sequence"] = $"This trip has no instalment {sequence}.";
                continue;
            }
            if (!seen.Add(sequence))
            {
                errors[$"{key}.sequence"] = $"Instalment {sequence} is listed more than once.";
                continue;
            }

            if (paidSequences.Contains(sequence))
            {
                if (!existingDates.TryGetValue(sequence, out var kept) || kept != due)
                    errors[$"{key}.dueDate"] = $"Instalment {sequence} is already paid, so its date can't change.";
                continue;
            }

            if (due < today)
                errors[$"{key}.dueDate"] = "Pick a date from today onwards.";
            else if (trip.PayInFullByDate is { } deadline && due > deadline)
                errors[$"{key}.dueDate"] = $"Pick a date on or before {deadline:yyyy-MM-dd}, the pay-in-full date.";
        }

        var missing = tripSequences.Where(s => !paidSequences.Contains(s) && !seen.Contains(s)).OrderBy(s => s).ToList();
        if (missing.Count > 0)
            errors.TryAdd(TravellerDatesField, $"Pick a date for every unpaid instalment (missing: {string.Join(", ", missing)}).");

        if (errors.Count > 0) return errors;

        // Strictly increasing across the whole schedule (paid lines keep their stored date).
        var merged = new SortedDictionary<int, DateOnly>();
        foreach (var (sequence, date) in existingDates)
            if (paidSequences.Contains(sequence) && tripSequences.Contains(sequence)) merged[sequence] = date;
        foreach (var (sequence, due) in dates)
            if (!paidSequences.Contains(sequence)) merged[sequence] = due;
        DateOnly? previous = null;
        foreach (var (sequence, date) in merged)
        {
            if (previous is { } p && date <= p)
            {
                var index = dates.Select((d, idx) => (d.Sequence, idx)).FirstOrDefault(x => x.Sequence == sequence).idx;
                errors[$"{TravellerDatesField}[{index}].dueDate"] = "Each instalment must be due after the one before it.";
                break;
            }
            previous = date;
        }

        return errors;
    }
}
