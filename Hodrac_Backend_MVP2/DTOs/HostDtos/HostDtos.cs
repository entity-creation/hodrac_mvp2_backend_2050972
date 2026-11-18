using System.ComponentModel.DataAnnotations;
using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.Http;

namespace Hodrac_Backend_MVP2.DTOs.HostDtos
{
    // ─── Host profile (the host's own view) ───────────────────────────────────
    // Never includes the payout account number, the subaccount code or any
    // document bytes/keys.

    public record HostPayoutDto(
        string BankCode,
        string? BankName,
        string? AccountName,
        string? AccountLast4
    );

    public record HostProfileDto(
        Guid Id,
        string DisplayName,
        string Slug,
        string? Bio,
        string? LogoUrl,
        string? InstagramHandle,
        string? WhatsAppNumber,
        string VerificationStatus,      // Unverified | Pending | Verified | Rejected
        DateTime? VerifiedAt,
        string? RejectionReason,
        string? CacNumber,
        bool HasIdDocument,
        List<string> PastTripProofUrls,
        bool PayoutAccountSet,
        HostPayoutDto? PayoutAccount,
        string Plan,                    // Host | Operator
        DateTime? OperatorPlanExpiresAt,
        bool IsFoundingHost,
        bool FoundingFreeDepartureUsed,
        DateTime CreatedAt,
        DateTime UpdatedAt
    )
    {
        public static HostProfileDto FromEntity(HostProfile h) => new(
            h.Id,
            h.DisplayName,
            h.Slug,
            h.Bio,
            h.LogoUrl,
            h.InstagramHandle,
            h.WhatsAppNumber,
            h.VerificationStatus.ToString(),
            h.VerifiedAt,
            h.RejectionReason,
            h.CacNumber,
            h.IdDocumentStorageKey is not null,
            h.PastTripProofUrls.ToList(),
            h.PaystackSubaccountCode is not null,
            h.PaystackSubaccountCode is not null && h.SettlementBankCode is not null
                ? new HostPayoutDto(h.SettlementBankCode, h.SettlementBankName, h.SettlementAccountName, h.SettlementAccountLast4)
                : null,
            h.Plan.ToString(),
            h.OperatorPlanExpiresAt,
            h.IsFoundingHost,
            h.FoundingFreeDepartureTripId is not null,
            h.CreatedAt,
            h.UpdatedAt);
    }

    /// <summary>Slug is optional: generated from DisplayName when absent.</summary>
    public record CreateHostProfileRequest(
        [Required, MaxLength(HostProfile.MaxDisplayNameLength)] string DisplayName,
        [MaxLength(HostProfile.MaxSlugLength)] string? Slug,
        [MaxLength(HostProfile.MaxBioLength)] string? Bio,
        [MaxLength(HostProfile.MaxUrlLength)] string? LogoUrl,
        [MaxLength(HostProfile.MaxInstagramHandleLength)] string? InstagramHandle,
        [MaxLength(HostProfile.MaxWhatsAppNumberLength)] string? WhatsAppNumber
    );

    /// <summary>Partial update: null = leave unchanged; "" clears an optional field.</summary>
    public record UpdateHostProfileRequest(
        [MaxLength(HostProfile.MaxDisplayNameLength)] string? DisplayName,
        [MaxLength(HostProfile.MaxSlugLength)] string? Slug,
        [MaxLength(HostProfile.MaxBioLength)] string? Bio,
        [MaxLength(HostProfile.MaxUrlLength)] string? LogoUrl,
        [MaxLength(HostProfile.MaxInstagramHandleLength)] string? InstagramHandle,
        [MaxLength(HostProfile.MaxWhatsAppNumberLength)] string? WhatsAppNumber
    );

    // ─── Banks / payout account ───────────────────────────────────────────────

    public record HostBankDto(string Name, string Code);

    /// <summary>Without Confirm the account name is only resolved and returned; nothing is stored.</summary>
    public record PayoutAccountRequest(
        [Required, MaxLength(HostProfile.MaxBankCodeLength)] string BankCode,
        [Required, MaxLength(20)] string AccountNumber,
        bool Confirm = false
    );

    public record PayoutAccountResultDto(
        bool Confirmed,
        string BankCode,
        string BankName,
        string AccountName,
        string AccountLast4,
        HostProfileDto? Profile      // set when Confirmed
    );

    // ─── Verification (multipart/form-data) ───────────────────────────────────

    public class HostVerificationForm
    {
        [MaxLength(HostProfile.MaxCacNumberLength)]
        public string? CacNumber { get; set; }

        /// <summary>Optional. PDF, JPG or PNG, at most 5 MB. When absent, any earlier upload is kept.</summary>
        public IFormFile? IdDocument { get; set; }

        /// <summary>Optional http(s) links proving a past trip; 0 to 10.</summary>
        public List<string>? PastTripProofUrls { get; set; }
    }
}
