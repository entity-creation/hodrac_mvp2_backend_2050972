using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Services.Payments;

/// <summary>One line of a hosted trip's schedule with the seat money allocated to it (kobo).</summary>
public record ScheduleLine(SeatPaymentKind Kind, int? Sequence, long AmountKobo, DateOnly? DueDate, long PaidKobo)
{
    public long RemainingKobo => AmountKobo - PaidKobo;
    public bool IsPaid => RemainingKobo <= 0;
}

/// <summary>
/// Pure helpers for a hosted trip's payment schedule (spec "balance"):
/// seat money paid (<see cref="TripInterest.AmountPaidKobo"/>: Paystack
/// successes + manual payments) is allocated to the deposit first, then to
/// the instalments in sequence order.
/// </summary>
public static class PaymentSchedule
{
    public static List<ScheduleLine> Allocate(TripPost trip, long amountPaidKobo) =>
        Allocate(trip, amountPaidKobo, travellerDates: null);

    /// <summary>
    /// As <see cref="Allocate(TripPost, long)"/>, with the traveller's own
    /// instalment dates (sequence → date). Only used when the trip is
    /// <see cref="InstalmentDatesMode.TravellerChooses"/>: an instalment takes
    /// the traveller's date, or <see cref="TripPost.PayInFullByDate"/> when
    /// they haven't picked one (yet). HostSet trips always use the host's dates.
    /// </summary>
    public static List<ScheduleLine> Allocate(
        TripPost trip, long amountPaidKobo, IReadOnlyDictionary<int, DateOnly>? travellerDates)
    {
        var remaining = Math.Max(0, amountPaidKobo);
        var lines = new List<ScheduleLine>();
        var travellerChooses = trip.InstalmentDatesMode == InstalmentDatesMode.TravellerChooses;

        void Add(SeatPaymentKind kind, int? sequence, long amount, DateOnly? due)
        {
            if (amount <= 0) return;
            var paid = Math.Min(remaining, amount);
            remaining -= paid;
            lines.Add(new ScheduleLine(kind, sequence, amount, due, paid));
        }

        Add(SeatPaymentKind.Deposit, null, trip.DepositKobo ?? 0, null);
        foreach (var i in trip.Instalments.OrderBy(i => i.Sequence))
        {
            var due = !travellerChooses
                ? i.DueDate
                : travellerDates is not null && travellerDates.TryGetValue(i.Sequence, out var chosen)
                    ? chosen
                    : trip.PayInFullByDate;
            Add(SeatPaymentKind.Instalment, i.Sequence, i.AmountKobo, due);
        }

        return lines;
    }

    /// <summary>The traveller's dates as the sequence → date map <see cref="Allocate(TripPost, long, IReadOnlyDictionary{int, DateOnly}?)"/> takes.</summary>
    public static Dictionary<int, DateOnly> DateMap(IEnumerable<TripInterestInstalmentDate>? dates) =>
        (dates ?? Enumerable.Empty<TripInterestInstalmentDate>())
            .GroupBy(d => d.Sequence)
            .ToDictionary(g => g.Key, g => g.First().DueDate);

    // ── Line status / overdue (shared by balance, manifest, dashboard, removal) ──

    public const string StatusPaid = "Paid";
    public const string StatusOverdue = "Overdue";
    public const string StatusDue = "Due";
    public const string StatusUpcoming = "Upcoming";

    /// <summary>
    /// "Paid" | "Overdue" (unpaid, due date before <paramref name="today"/>) |
    /// "Due" (the first unpaid line) | "Upcoming". <paramref name="today"/> is a
    /// WAT date (<c>HostTripService.TodayWat</c>).
    /// </summary>
    public static string LineStatus(ScheduleLine line, ScheduleLine? firstUnpaid, DateOnly today) =>
        line.IsPaid ? StatusPaid
        : line.DueDate is { } due && due < today ? StatusOverdue
        : ReferenceEquals(line, firstUnpaid) ? StatusDue
        : StatusUpcoming;

    /// <summary>
    /// An unpaid INSTALMENT line whose due date is before <paramref name="today"/>
    /// (WAT). The deposit (no due date: the seat hold covers it) is never overdue.
    /// </summary>
    public static bool IsOverdue(ScheduleLine line, DateOnly today) =>
        line.Kind == SeatPaymentKind.Instalment && !line.IsPaid && line.DueDate is { } due && due < today;

    /// <summary>The overdue instalment lines of an allocation (see <see cref="IsOverdue"/>), in sequence order.</summary>
    public static List<ScheduleLine> OverdueLines(IEnumerable<ScheduleLine> lines, DateOnly today) =>
        lines.Where(l => IsOverdue(l, today)).ToList();

    /// <summary>Σ RemainingKobo of the overdue instalment lines.</summary>
    public static long OverdueKobo(IEnumerable<ScheduleLine> lines, DateOnly today) =>
        lines.Where(l => IsOverdue(l, today)).Sum(l => l.RemainingKobo);

    /// <summary>
    /// The interest's payment status from its totals against the schedule.
    /// <c>hadRefund</c>: some money was refunded (Paystack or a manual refund).
    /// </summary>
    public static InterestPaymentStatus ComputeStatus(
        long amountPaidKobo, long priceKobo, long depositKobo, InterestPaymentStatus current, bool hadRefund)
    {
        if (amountPaidKobo <= 0)
        {
            if (hadRefund) return InterestPaymentStatus.Refunded;
            return current is InterestPaymentStatus.Expired or InterestPaymentStatus.NotRequired
                ? current
                : InterestPaymentStatus.AwaitingDeposit;
        }

        if (priceKobo > 0 && amountPaidKobo >= priceKobo) return InterestPaymentStatus.PaidInFull;
        // Exactly the deposit: DepositPaid. More than the deposit, or a part of
        // it recorded by hand (manual payments), is PartPaid.
        return amountPaidKobo == depositKobo ? InterestPaymentStatus.DepositPaid : InterestPaymentStatus.PartPaid;
    }
}
