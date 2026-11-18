namespace Hodrac_Backend_MVP2.DTOs.AdminDtos
{
    // ─── Admin payments search and webhook audit (Hodrac Host) ──────────────────
    // Money is long kobo. Never includes payout account numbers or subaccount
    // codes (those are only on AdminHostDetailDto).

    public record AdminPaymentTripDto(Guid Id, string Title);

    public record AdminPaymentHostDto(Guid Id, string DisplayName);

    public record AdminPaymentPayerDto(Guid Id, string? Name, string? Email);

    public record AdminPaymentDto(
        Guid Id,
        string Reference,
        string Status,
        string Kind,
        int? InstalmentSequence,
        AdminPaymentTripDto Trip,
        AdminPaymentHostDto? Host,
        AdminPaymentPayerDto Payer,
        long SeatAmountKobo,
        long HodracFeeKobo,
        long TravellerFeeKobo,
        long TotalChargedKobo,
        long? PaystackFeeKobo,
        long RefundedKobo,
        string? Channel,
        DateTime CreatedAt,
        DateTime? PaidAt
    );

    public record AdminPaymentEventDto(
        Guid Id,
        string Provider,
        string EventType,
        string? Reference,
        DateTime ReceivedAt,
        DateTime? ProcessedAt,
        string? Error
    );

    /// <summary>
    /// One payment for support: the row, the seat (interest) it paid for, the
    /// raw Paystack verify response and every webhook event with its reference.
    /// </summary>
    public record AdminPaymentDetailDto(
        AdminPaymentDto Payment,
        Guid TripInterestId,
        string Currency,
        DateTime UpdatedAt,
        string? VerifyResponseJson,
        List<AdminPaymentEventDto> WebhookEvents
    );

    public record AdminPaymentEventDetailDto(
        AdminPaymentEventDto Event,
        string PayloadSha256,
        // The webhook body as stored (jsonb: same content, normalised key order/whitespace).
        string Payload
    );

    /// <summary>Result of POST /api/admin/payment-events/{id}/reprocess.</summary>
    public record AdminReprocessEventResultDto(
        // "Processed", "Failed" (Event.Error says why) or "AlreadyProcessed" (nothing re-run).
        string Outcome,
        AdminPaymentEventDto Event
    );
}
