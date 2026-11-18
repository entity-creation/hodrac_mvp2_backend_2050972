namespace Hodrac_Backend_MVP2.DTOs.AdminDtos
{
    // ─── Admin hosting (money) insights: GET /api/admin/insights/hosting ────────
    // Postgres only (works when MongoDB is down). Money is long kobo.
    //
    // Period: From..To are inclusive West Africa Time (UTC+1) calendar dates;
    // months are WAT calendar months ("yyyy-MM"). A Hodrac payment belongs to
    // the WAT date of its PaidAt (CreatedAt if PaidAt is missing); a manual
    // payment to the WAT date of its RecordedAt; an operator plan payment to
    // the WAT date of its CreatedAt (when it was recorded).
    //
    // Definitions (per period and per month):
    //  - ViaHodracKobo: seat money collected through Paystack, net of refunds:
    //      Σ max(0, SeatAmountKobo − RefundedKobo) over payments whose status is
    //      Success, PartiallyRefunded or Disputed (fully Refunded ones net to 0).
    //      Refunds are counted in the month the payment was paid. The traveller
    //      fee is not seat money and is excluded.
    //  - ManualNetKobo: Σ ManualPayment.AmountKobo (host-recorded money outside
    //      Hodrac; Refund rows are negative), by RecordedAt.
    //  - GmvKobo: ViaHodracKobo + ManualNetKobo (all seat money, net of refunds).
    //  - HodracFeeKobo: Σ HodracFeeKobo over payments that succeeded at some point
    //      (Success, PartiallyRefunded, Refunded, Disputed). The fee is
    //      non-refundable, so refunds don't reduce it. Founding free departures
    //      carry a 0 fee.
    //  - RefundedViaHodracKobo: Σ RefundedKobo over those same payments.
    //  - PercentThroughHodrac: 100 × ViaHodracKobo ÷ GmvKobo, rounded to 1
    //      decimal; null when GmvKobo ≤ 0.
    //  - SeatsPaid: distinct seats (TripInterests) with ≥1 Success /
    //      PartiallyRefunded / Disputed Hodrac payment, or ≥1 positive manual
    //      payment, in the period/month. The period total is distinct over the
    //      whole period (not the sum of the months).
    //  - ActiveHosts: distinct hosts whose hosted trips had a payment counted in
    //      SeatsPaid in the period/month (same distinct rule).
    //  - PaidDepartures: hosted trips whose StartDate falls in the period/month
    //      with ≥1 seat that has money on it now (TripInterest.AmountPaidKobo > 0,
    //      via Hodrac or manual).
    //  - OperatorPlanRevenueKobo / OperatorPlanPayments: Σ / count of
    //      OperatorPlanPayment rows recorded in the period/month.
    //
    // Snapshots (as of now, not period-bound):
    //  - VerifiedHosts: hosts with VerificationStatus = Verified.
    //  - FoundingHostsUsed / FoundingHostLimit: hosts marked founding vs
    //      Hodrac:FoundingHostLimit; FoundingFreeDeparturesClaimed: founding hosts
    //      whose free departure has been used by a trip.
    //  - OperatorPlansActive: hosts on the Operator plan whose
    //      OperatorPlanExpiresAt is in the future.

    public record AdminHostingMonthDto(
        string Month,
        long GmvKobo,
        long ViaHodracKobo,
        long ManualNetKobo,
        long HodracFeeKobo,
        long RefundedViaHodracKobo,
        double? PercentThroughHodrac,
        int SeatsPaid,
        int ActiveHosts,
        int PaidDepartures,
        long OperatorPlanRevenueKobo,
        int OperatorPlanPayments
    );

    public record AdminHostingInsightsDto(
        DateOnly From,
        DateOnly To,
        long GmvKobo,
        long ViaHodracKobo,
        long ManualNetKobo,
        long HodracFeeKobo,
        long RefundedViaHodracKobo,
        double? PercentThroughHodrac,
        int SeatsPaid,
        int ActiveHosts,
        int PaidDepartures,
        long OperatorPlanRevenueKobo,
        int OperatorPlanPayments,
        int VerifiedHosts,
        int FoundingHostsUsed,
        int FoundingHostLimit,
        int FoundingFreeDeparturesClaimed,
        int OperatorPlansActive,
        List<AdminHostingMonthDto> Months
    );
}
