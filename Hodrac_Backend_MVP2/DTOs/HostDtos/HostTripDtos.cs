namespace Hodrac_Backend_MVP2.DTOs.HostDtos
{
    // ─── Host trips, manifest, manual payments, reports ───────────────────────
    // Money is kobo (long). "Seats" count Approved interests only; money totals
    // count every interest on the trip (a withdrawn traveller's money stays on
    // the books until the host records a refund).

    /// <summary>One of my hosted trips (GET /api/host/trips).</summary>
    public record HostTripSummaryDto(
        Guid Id,
        string Title,
        DateOnly? StartDate,
        DateOnly? EndDate,
        string Status,                  // Open | Closed | Cancelled
        bool IsListed,
        bool IsHidden,
        int? SeatsTotal,                // MaxGroupSize (null = no limit)
        long PricePerSeatKobo,
        int Approved,                   // Approved, seat hold not expired
        int SeatsWithDeposit,           // Approved with any money paid
        int SeatsPaidInFull,            // Approved and PaidInFull
        long CollectedKobo,             // seat money through Hodrac (Paystack), net of refunds
        long CollectedManualKobo,       // recorded by the host (net of manual refunds)
        long CollectedTotalKobo,
        long OwedKobo,                  // still owed by approved travellers
        bool IsUpcoming,                // starts today (WAT) or later, or no date yet
        bool IsNextDeparture            // the earliest upcoming dated trip
    );

    /// <summary>One traveller on the manifest. Declined requests are left out.</summary>
    public record ManifestRowDto(
        Guid InterestId,
        Guid TravellerUserId,
        string TravellerName,
        string? Email,
        string? Phone,                  // ContactPhone, else the account's phone number
        string InterestStatus,          // Requested | Approved | Withdrawn | Removed
        string PaymentStatus,
        int? TermsVersionAccepted,
        DateTime? TermsAcceptedAt,
        long PaidKobo,
        long ViaHodracKobo,
        long ManualKobo,                // PaidKobo - ViaHodracKobo (negative after a manual refund of Hodrac money)
        long OwesKobo,                  // Approved and not expired only
        DateOnly? NextDueDate,          // instalment due date; deposit: the seat-hold expiry day (WAT)
        long? NextDueAmountKobo,
        DateTime? SeatHoldExpiresAt,
        DateTime? LastReminderAt,
        DateTime RequestedAt,
        bool DatesChosenByTraveller = false,   // TravellerChooses trip and the traveller has picked dates
        bool CanRemove = false,                // the host may remove them now (an instalment is overdue)
        string? RemovedReason = null,          // InterestStatus Removed only
        DateTime? RemovedAt = null,
        long OverdueKobo = 0                   // unpaid instalments due before today (WAT); 0 unless owing on a non-cancelled trip
    );

    /// <summary>POST /api/host/trips/{id}/travellers/{interestId}/remove. Reason: optional, at most 450 characters.</summary>
    public record RemoveTravellerRequest(string? Reason);

    public record OverdueInstalmentDto(int Sequence, DateOnly DueDate, long AmountKobo, long RemainingKobo);

    public record RemoveTravellerResultDto(
        Guid InterestId,
        string InterestStatus,                 // "Removed"
        string PaymentStatus,                  // unchanged: payments are kept, nothing is refunded
        long AmountPaidKobo,
        DateTime RemovedAt,
        string? RemovedReason,
        List<OverdueInstalmentDto> OverdueInstalments);

    public record ManifestTotalsDto(
        int Travellers,
        int Approved,
        int SeatsWithDeposit,
        int SeatsPaidInFull,
        long PaidKobo,
        long ViaHodracKobo,
        long ManualKobo,
        long OwesKobo,
        long OverdueKobo = 0
    );

    public record ManifestDto(
        Guid TripId,
        string Title,
        DateOnly? StartDate,
        DateOnly? EndDate,
        int? SeatsTotal,
        long PricePerSeatKobo,
        string Currency,
        int TermsVersion,
        List<ManifestRowDto> Rows,
        ManifestTotalsDto Totals
    );

    /// <summary>
    /// Method: Transfer | Cash | Other (AmountKobo &gt; 0) or Refund (AmountKobo &lt; 0,
    /// at most what the traveller has paid).
    /// </summary>
    public record RecordManualPaymentRequest(Guid InterestId, long AmountKobo, string? Method, string? Note);

    public record ManualPaymentDto(
        Guid Id,
        Guid InterestId,
        long AmountKobo,
        string Method,
        string? Note,
        Guid RecordedByUserId,
        DateTime RecordedAt
    );

    /// <summary>The recorded/deleted payment and the interest's totals after the recompute.</summary>
    public record ManualPaymentResultDto(
        ManualPaymentDto Payment,
        long AmountPaidKobo,
        long AmountPaidViaHodracKobo,
        string PaymentStatus,
        DateTime? SeatHoldExpiresAt
    );

    public record WhatsAppNudgeDto(string Url, string Phone, string Message);

    /// <summary>Numbers for one trip (or the totals) in a Mon–Sun WAT week.</summary>
    public record WeeklyReportStatsDto(
        int PageViews,
        Dictionary<string, int> PageViewsBySource,  // "share" (link previews), "public-api" (trip page)
        int LinkClicks,                             // opens of the shared link = "share" page views
        int JoinRequests,
        int Approvals,
        int SeatsPaid,                              // seats whose first money (deposit or more) landed this week
        long CollectedViaHodracKobo,                // seat money of Paystack payments made this week (gross)
        long CollectedManualKobo,                   // manual payments recorded this week (excl. refunds)
        long RefundedManualKobo,                    // manual refunds recorded this week (positive number)
        decimal? HodracSharePercentWeek,            // via Hodrac / (via Hodrac + manual) this week
        decimal? HodracSharePercentAllTime          // Σ AmountPaidViaHodrac / Σ AmountPaid
    );

    public record WeeklyReportTripDto(
        Guid TripId,
        string Title,
        DateOnly? StartDate,
        string Status,
        WeeklyReportStatsDto Stats
    );

    public record HostWeeklyReportDto(
        Guid HostProfileId,
        string HostDisplayName,
        DateOnly WeekStart,             // Monday
        DateOnly WeekEnd,               // Sunday (inclusive)
        string TimeZone,                // "WAT" (UTC+1)
        List<WeeklyReportTripDto> Trips,
        WeeklyReportStatsDto Totals
    );
}
