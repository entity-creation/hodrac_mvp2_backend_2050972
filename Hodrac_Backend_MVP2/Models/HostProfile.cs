namespace Hodrac_Backend_MVP2.Models;

// ─── Hodrac Host: host profile ────────────────────────────────────────────────
// One per user who hosts paid (hosted) trips. Community trips never need one.
//
// UserId follows the codebase convention (ApplicationUser.Id parsed as Guid,
// like TripPost.AuthorUserId) rather than a string FK to AspNetUsers.

public enum HostVerificationStatus
{
    Unverified,
    Pending,
    Verified,
    Rejected
}

public enum HostPlan
{
    Host,
    Operator
}

public class HostProfile
{
    public const int MaxDisplayNameLength = 100;
    public const int MaxSlugLength = 80;
    public const int MaxBioLength = 2000;
    public const int MaxUrlLength = 2048;
    public const int MaxInstagramHandleLength = 64;
    public const int MaxWhatsAppNumberLength = 20;
    public const int MaxRejectionReasonLength = 500;
    public const int MaxCacNumberLength = 32;
    public const int MaxStorageKeyLength = 64;
    public const int MaxSubaccountCodeLength = 64;
    public const int MaxBankCodeLength = 16;
    public const int MaxBankNameLength = 100;
    public const int MaxAccountNameLength = 200;
    public const int AccountLast4Length = 4;
    public const int MaxClosedReasonLength = 500;

    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Identity ──────────────────────────────────────────────────────────────
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? LogoUrl { get; set; }
    public string? InstagramHandle { get; set; }
    public string? WhatsAppNumber { get; set; }

    // ── Verification ──────────────────────────────────────────────────────────
    public HostVerificationStatus VerificationStatus { get; set; } = HostVerificationStatus.Unverified;
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    public string? CacNumber { get; set; }

    /// <summary>
    /// Private storage key of the ID document: the <see cref="HostDocument"/> id
    /// as a string. Never a public URL.
    /// </summary>
    public string? IdDocumentStorageKey { get; set; }

    /// <summary>Links proving a past trip (stored as a jsonb string array).</summary>
    public List<string> PastTripProofUrls { get; set; } = new();

    // ── Payout (Paystack subaccount) ──────────────────────────────────────────
    // Never store the full account number once the subaccount exists.
    public string? PaystackSubaccountCode { get; set; }
    public string? SettlementBankCode { get; set; }
    public string? SettlementBankName { get; set; }
    public string? SettlementAccountName { get; set; }
    public string? SettlementAccountLast4 { get; set; }

    // ── Plan ──────────────────────────────────────────────────────────────────
    public HostPlan Plan { get; set; } = HostPlan.Host;
    public DateTime? OperatorPlanExpiresAt { get; set; }

    // ── Founding hosts ────────────────────────────────────────────────────────
    public bool IsFoundingHost { get; set; }

    /// <summary>The trip that used the free founding departure (no FK, avoids a TripPost↔HostProfile cycle).</summary>
    public Guid? FoundingFreeDepartureTripId { get; set; }

    // ── Closed by Hodrac ──────────────────────────────────────────────────────
    // Set by POST /api/admin/hosts/{id}/close. A closed host can't change its
    // setup (profile, payout, verification, new hosted trips, money terms) but
    // can still settle existing trips (manifest, manual payments/refunds).
    public DateTime? ClosedAt { get; set; }
    public string? ClosedReason { get; set; }
    public Guid? ClosedByUserId { get; set; }

    /// <summary>Not mapped (no setter). Use <c>ClosedAt != null</c> in queries.</summary>
    public bool IsClosed => ClosedAt is not null;

    // ── Timestamps ────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TripPost> Trips { get; set; } = new List<TripPost>();
    public ICollection<HostDocument> Documents { get; set; } = new List<HostDocument>();
    public ICollection<OperatorPlanPayment> OperatorPlanPayments { get; set; } = new List<OperatorPlanPayment>();
}

// ─── HostDocument ─────────────────────────────────────────────────────────────
// Private verification documents (ID, CAC certificate) stored in Postgres,
// never exposed by URL; admins get short-lived signed links from the API.
// Purged 90 days after the verification decision.

public static class HostDocumentKinds
{
    public const string IdDocument = "IdDocument";
    public const string CacCertificate = "CacCertificate";

    public static readonly IReadOnlyList<string> All = new[] { IdDocument, CacCertificate };
}

public class HostDocument
{
    public const int MaxKindLength = 32;
    public const int MaxFileNameLength = 255;
    public const int MaxContentTypeLength = 100;
    public const int Sha256Length = 64;
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HostProfileId { get; set; }
    public HostProfile? HostProfile { get; set; }

    /// <summary>One of <see cref="HostDocumentKinds"/>.</summary>
    public string Kind { get; set; } = HostDocumentKinds.IdDocument;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string Sha256 { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

// ─── TripPageViewDaily ────────────────────────────────────────────────────────
// Daily page-view counter per trip and source, for the host weekly report.
// Composite PK (TripPostId, Date, Source); incremented with an upsert.

public static class TripPageViewSources
{
    /// <summary>/share/trip/{id} link-preview page (WhatsApp, Instagram...).</summary>
    public const string Share = "share";
    /// <summary>GET /api/trips/public/{id}.</summary>
    public const string PublicApi = "public-api";

    public static readonly IReadOnlyList<string> All = new[] { Share, PublicApi };
}

public class TripPageViewDaily
{
    public const int MaxSourceLength = 32;

    public Guid TripPostId { get; set; }
    public DateOnly Date { get; set; }
    public string Source { get; set; } = TripPageViewSources.Share;
    public int Count { get; set; }
}

// ─── HostReportLog ────────────────────────────────────────────────────────────
// One row per host per weekly report email (HostWeeklyReportJob), so a week's
// report is never emailed twice. Unique on (HostProfileId, WeekStart).

public class HostReportLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HostProfileId { get; set; }
    public HostProfile? HostProfile { get; set; }

    /// <summary>Monday (WAT) of the reported Mon–Sun week.</summary>
    public DateOnly WeekStart { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
