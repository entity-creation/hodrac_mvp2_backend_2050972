using System.ComponentModel.DataAnnotations;
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.DTOs.AdminDtos
{
    // ─── Admin host queue ─────────────────────────────────────────────────────
    // Payout: only "set or not" and the last 4 digits. Documents: metadata plus
    // a short-lived signed link, never bytes or storage keys.

    public record AdminHostSummaryDto(
        Guid Id,
        Guid UserId,
        string DisplayName,
        string Slug,
        string? UserEmail,
        string VerificationStatus,
        int DocumentCount,
        DateTime? LastDocumentUploadedAt,
        string? CacNumber,
        List<string> PastTripProofUrls,
        bool PayoutAccountSet,
        string? PayoutAccountLast4,
        string? PayoutBankName,
        bool IsFoundingHost,
        string Plan,
        DateTime? OperatorPlanExpiresAt,
        DateTime? ClosedAt,         // null = open; set by POST {id}/close
        string? ClosedReason,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public record AdminOperatorPlanPaymentDto(
        Guid Id,
        string Reference,
        long AmountKobo,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        Guid RecordedByUserId,
        DateTime CreatedAt
    );

    public record AdminHostDetailDto(
        AdminHostSummaryDto Summary,
        string? UserDisplayName,
        string? Bio,
        string? LogoUrl,
        string? InstagramHandle,
        string? WhatsAppNumber,
        DateTime? VerifiedAt,
        Guid? VerifiedByUserId,
        string? RejectionReason,
        string? PaystackSubaccountCode,
        string? PayoutAccountName,
        Guid? FoundingFreeDepartureTripId,
        Guid? ClosedByUserId,
        List<AdminOperatorPlanPaymentDto> OperatorPlanPayments
    );

    public record AdminHostDocumentDto(
        Guid Id,
        string Kind,
        string FileName,
        string ContentType,
        long SizeBytes,
        DateTime UploadedAt,
        string Url,                 // relative, admin-only, signed: /api/admin/hosts/documents/{id}?exp=&sig=
        DateTimeOffset ExpiresAt
    );

    public record RejectHostRequest(
        [Required, MaxLength(HostProfile.MaxRejectionReasonLength)] string Reason
    );

    /// <summary>Optional reason shown to the host when their Verified badge is removed.</summary>
    public record RevokeHostVerificationRequest(
        [MaxLength(RevokeHostVerificationRequest.MaxReasonLength)] string? Reason
    )
    {
        // Leaves room for the "Verification removed: " prefix within HostProfile.MaxRejectionReasonLength.
        public const int MaxReasonLength = 450;
    }

    /// <summary>Optional reason for closing a host account (shown to the host).</summary>
    public record CloseHostRequest(
        [MaxLength(CloseHostRequest.MaxReasonLength)] string? Reason
    )
    {
        // Leaves room for the "Host account closed: " prefix within HostProfile.MaxRejectionReasonLength.
        public const int MaxReasonLength = 450;
    }

    /// <summary>
    /// POST /api/admin/hosts/{id}/close. SubaccountDeactivated: true = Paystack
    /// confirmed the subaccount is inactive; false = the Paystack call failed
    /// (admins were emailed; calling close again retries it); null = the host
    /// has no subaccount. HiddenTripIds: trips hidden by this call (none on a
    /// repeat call).
    /// </summary>
    public record AdminHostCloseResultDto(
        AdminHostDetailDto Host,
        bool? SubaccountDeactivated,
        List<Guid> HiddenTripIds
    );

    /// <summary>
    /// POST /api/admin/hosts/{id}/reopen. SubaccountReactivated: true = Paystack
    /// reactivated it; false = the Paystack call failed (admins were emailed);
    /// null = no subaccount, or the host wasn't closed (nothing done).
    /// UnhiddenTripIds: trips this call un-hid (only those close had hidden).
    /// </summary>
    public record AdminHostReopenResultDto(
        AdminHostDetailDto Host,
        bool? SubaccountReactivated,
        List<Guid> UnhiddenTripIds
    );

    public record RecordOperatorPlanRequest(
        [Required, MaxLength(OperatorPlanPayment.MaxReferenceLength)] string Reference,
        long AmountKobo,
        DateOnly PeriodStart,
        DateOnly PeriodEnd
    );
}
