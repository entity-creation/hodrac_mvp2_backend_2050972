namespace Hodrac_Backend_MVP2.DTOs.HostDtos
{
    // ─── Host all-trips dashboard (GET /api/host/dashboard/...) ────────────────
    // Money is kobo (long). Definitions (HostDashboardService):
    //  - Collected: everything received, from the interests' stored totals
    //    (Paystack seat money net of Paystack refunds + manual payments net of
    //    manual refunds), on every interest (withdrawn/removed money stays here).
    //  - Owed / overdue: Approved, seat hold not expired, on trips that aren't
    //    Cancelled. Overdue = unpaid instalments due before today (WAT); the
    //    deposit is never overdue (the seat hold covers it).
    //  - Fees, from succeeded Paystack payments: TravellerFeeKobo (Hodrac fee
    //    the traveller paid on top), HodracFeeKobo − TravellerFeeKobo (Hodrac
    //    fee taken out of the host's share), PaystackFeeKobo (borne by the host).
    //  - EstimatedNetToHostKobo = CollectedViaHodracKobo − host-absorbed Hodrac
    //    fee − Paystack fee. An estimate: Paystack settles on its own schedule.

    public record HostDashboardFeesDto(
        long HodracFeeTravellerPaidKobo,   // Σ TravellerFeeKobo
        long HodracFeeHostAbsorbedKobo,    // Σ (HodracFeeKobo − TravellerFeeKobo)
        long PaystackFeeKobo,              // Σ PaystackFeeKobo (unknown fees count as 0)
        long RefundedViaHodracKobo,        // Σ SeatPayment.RefundedKobo (Paystack refunds)
        long RefundedManualKobo,           // manual Refund entries, as a positive number
        long RefundedTotalKobo,
        long EstimatedNetToHostKobo        // estimate, see above
    );

    /// <summary>One month of the money chart ("yyyy-MM", WAT).</summary>
    public record HostDashboardMonthDto(
        string Month,
        long ViaHodracKobo,    // seat money of Paystack payments paid that month, net of their refunds
        long ManualKobo,       // manual payments recorded that month (refunds negative)
        long TotalKobo
    );

    /// <summary>GET /api/host/dashboard/summary.</summary>
    public record HostDashboardSummaryDto(
        int Trips,
        int SeatsTotal,                    // Σ MaxGroupSize of non-cancelled trips
        int SeatsSold,                     // Approved, seat hold not expired, non-cancelled trips
        int SeatsWithDeposit,
        int SeatsPaidInFull,
        long CollectedViaHodracKobo,
        long CollectedManualKobo,
        long CollectedTotalKobo,
        long OwedKobo,
        long OverdueKobo,
        int OverdueTravellers,
        HostDashboardFeesDto Fees,
        DateOnly PaidFrom,                 // the chart period (WAT days, inclusive)
        DateOnly PaidTo,
        string TimeZone,                   // "WAT"
        List<HostDashboardMonthDto> Monthly
    );

    /// <summary>One row of GET /api/host/dashboard/trips: the trips-list fields plus location, overdue and fees.</summary>
    public record HostDashboardTripDto(
        Guid Id,
        string Title,
        string LocationLabel,              // "Name, Country" | wishlist stops | free text
        Guid? DestinationId,
        Guid? WishlistId,
        List<Guid> DestinationIds,         // the destination, or the wishlist's stops
        DateOnly? StartDate,
        DateOnly? EndDate,
        string Status,                     // Open | Closed | Cancelled
        bool IsListed,
        bool IsHidden,
        string Currency,
        string FeeMode,                    // PassToTraveller | HostAbsorbs
        string InstalmentDatesMode,        // HostSet | TravellerChooses
        int? SeatsTotal,
        long PricePerSeatKobo,
        int Approved,
        int SeatsWithDeposit,
        int SeatsPaidInFull,
        long CollectedKobo,                // via Hodrac
        long CollectedManualKobo,
        long CollectedTotalKobo,
        long OwedKobo,                     // 0 on a cancelled trip
        long OverdueKobo,
        int OverdueTravellers,
        DateOnly? NextDueDate,             // earliest next payment across the owing travellers
        bool IsUpcoming,
        bool IsNextDeparture,
        HostDashboardFeesDto Fees
    );

    /// <summary>One row of GET /api/host/dashboard/travellers (declined requests are left out).</summary>
    public record HostDashboardTravellerDto(
        Guid TripId,
        string TripTitle,
        DateOnly? TripStartDate,
        string TripStatus,
        Guid InterestId,
        Guid TravellerUserId,
        string TravellerName,
        string? Email,
        string? Phone,
        string InterestStatus,             // Requested | Approved | Withdrawn | Removed
        string PaymentStatus,
        long PaidKobo,
        long ViaHodracKobo,
        long ManualKobo,
        long OwesKobo,                     // Approved, not expired, trip not cancelled
        DateOnly? NextDueDate,             // instalment due date; deposit: the seat-hold expiry day (WAT)
        long? NextDueAmountKobo,
        string? NextDueKind,               // Deposit | Instalment
        long OverdueKobo,
        int OverdueInstalments,
        int? DaysOverdue,                  // days since the oldest overdue instalment was due
        DateTime? SeatHoldExpiresAt,
        DateTime? LastReminderAt,
        string InstalmentDatesMode,        // the trip's mode
        bool DatesChosenByTraveller,
        bool CanRemove,                    // POST /api/host/trips/{tripId}/travellers/{interestId}/remove is allowed
        string? RemovedReason,
        DateTime? RemovedAt,
        DateTime RequestedAt
    );
}
