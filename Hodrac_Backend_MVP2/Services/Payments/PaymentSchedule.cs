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
    public static List<ScheduleLine> Allocate(TripPost trip, long amountPaidKobo)
    {
        var remaining = Math.Max(0, amountPaidKobo);
        var lines = new List<ScheduleLine>();

        void Add(SeatPaymentKind kind, int? sequence, long amount, DateOnly? due)
        {
            if (amount <= 0) return;
            var paid = Math.Min(remaining, amount);
            remaining -= paid;
            lines.Add(new ScheduleLine(kind, sequence, amount, due, paid));
        }

        Add(SeatPaymentKind.Deposit, null, trip.DepositKobo ?? 0, null);
        foreach (var i in trip.Instalments.OrderBy(i => i.Sequence))
            Add(SeatPaymentKind.Instalment, i.Sequence, i.AmountKobo, i.DueDate);

        return lines;
    }

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
