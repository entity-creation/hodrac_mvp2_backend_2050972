using System.Security.Cryptography;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services.Hosts;

/// <summary>
/// Host onboarding: profile, payout account (Paystack subaccount) and
/// verification documents. Request validation lives in HostController; this
/// service does the persistence, Paystack calls and emails.
/// </summary>
public class HostService
{
    private const int SlugAttempts = 5;

    private readonly HodracDbContext _db;
    private readonly IPaystackClient _paystack;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<HostService> _logger;

    public HostService(
        HodracDbContext db,
        IPaystackClient paystack,
        IEmailSender email,
        IConfiguration config,
        ILogger<HostService> logger)
    {
        _db = db;
        _paystack = paystack;
        _email = email;
        _config = config;
        _logger = logger;
    }

    public Task<HostProfile?> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
        _db.HostProfiles.FirstOrDefaultAsync(h => h.UserId == userId, ct);

    public Task<bool> UserHasProfileAsync(Guid userId, CancellationToken ct = default) =>
        _db.HostProfiles.AnyAsync(h => h.UserId == userId, ct);

    public Task<bool> IsSlugTakenAsync(string slug, Guid? excludeHostId, CancellationToken ct = default) =>
        _db.HostProfiles.AnyAsync(h => h.Slug == slug && (excludeHostId == null || h.Id != excludeHostId), ct);

    /// <summary>A free slug derived from the display name (random suffix on collision).</summary>
    public async Task<string> GenerateSlugAsync(string displayName, CancellationToken ct = default)
    {
        var baseSlug = HostSlug.FromDisplayName(displayName);
        if (!await IsSlugTakenAsync(baseSlug, null, ct)) return baseSlug;

        for (var i = 0; i < SlugAttempts; i++)
        {
            var candidate = HostSlug.WithSuffix(baseSlug);
            if (!await IsSlugTakenAsync(candidate, null, ct)) return candidate;
        }
        return HostSlug.WithSuffix(HostSlug.WithSuffix(baseSlug));
    }

    // ── Payout account ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates the host's Paystack subaccount (or points the existing one at the
    /// new bank account) and stores only the code, bank, account name and last 4.
    /// The full account number goes to Paystack and is never persisted.
    /// </summary>
    public async Task SetPayoutAccountAsync(
        HostProfile host,
        string bankCode,
        string bankName,
        string accountNumber,
        string accountName,
        CancellationToken ct = default)
    {
        var contactEmail = await GetUserEmailAsync(host.UserId, ct);

        PaystackSubaccount subaccount;
        if (host.PaystackSubaccountCode is null)
        {
            subaccount = await _paystack.CreateSubaccountAsync(new PaystackSubaccountRequest(
                BusinessName: host.DisplayName,
                SettlementBankCode: bankCode,
                AccountNumber: accountNumber,
                Description: $"Hodrac Host: {host.Slug}",
                PrimaryContactEmail: contactEmail), ct);
        }
        else
        {
            subaccount = await _paystack.UpdateSubaccountAsync(host.PaystackSubaccountCode, new PaystackSubaccountUpdate(
                BusinessName: host.DisplayName,
                SettlementBankCode: bankCode,
                AccountNumber: accountNumber,
                PrimaryContactEmail: contactEmail), ct);
        }

        host.PaystackSubaccountCode = subaccount.SubaccountCode;
        host.SettlementBankCode = bankCode;
        host.SettlementBankName = Truncate(bankName, HostProfile.MaxBankNameLength);
        host.SettlementAccountName = Truncate(accountName, HostProfile.MaxAccountNameLength);
        host.SettlementAccountLast4 = accountNumber[^HostProfile.AccountLast4Length..];
        host.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Host {HostId} payout account set (subaccount {Code}, bank {BankCode}, ****{Last4})",
            host.Id, subaccount.SubaccountCode, bankCode, host.SettlementAccountLast4);
    }

    /// <summary>Keeps the subaccount's business_name equal to the display name. Best-effort.</summary>
    public async Task SyncSubaccountNameAsync(HostProfile host, CancellationToken ct = default)
    {
        if (host.PaystackSubaccountCode is null) return;
        try
        {
            await _paystack.UpdateSubaccountAsync(host.PaystackSubaccountCode,
                new PaystackSubaccountUpdate(BusinessName: host.DisplayName), ct);
        }
        catch (PaystackException ex)
        {
            _logger.LogWarning(ex, "Could not update Paystack subaccount name for host {HostId}", host.Id);
        }
    }

    // ── Verification ─────────────────────────────────────────────────────────

    /// <summary>
    /// Records a verification submission: replaces the ID document (when one is
    /// given), sets CAC number and past-trip links, and moves the host to Pending.
    /// Every field is optional (nothing is required to be verified); an empty
    /// submission still moves the host to Pending for an admin to review.
    /// </summary>
    public async Task SubmitVerificationAsync(
        HostProfile host,
        string? cacNumber,
        byte[]? idDocument,
        string? idDocumentContentType,
        string? idDocumentFileName,
        List<string> pastTripProofUrls,
        CancellationToken ct = default)
    {
        if (idDocument is not null)
        {
            // Keep only the latest ID document (data minimisation).
            var old = await _db.HostDocuments
                .Where(d => d.HostProfileId == host.Id && d.Kind == HostDocumentKinds.IdDocument)
                .ToListAsync(ct);
            _db.HostDocuments.RemoveRange(old);

            var doc = new HostDocument
            {
                HostProfileId = host.Id,
                Kind = HostDocumentKinds.IdDocument,
                FileName = idDocumentFileName!,
                ContentType = idDocumentContentType!,
                SizeBytes = idDocument.LongLength,
                Content = idDocument,
                Sha256 = Convert.ToHexString(SHA256.HashData(idDocument)).ToLowerInvariant(),
            };
            _db.HostDocuments.Add(doc);
            host.IdDocumentStorageKey = doc.Id.ToString();
        }

        host.CacNumber = cacNumber;
        host.PastTripProofUrls = pastTripProofUrls;
        host.VerificationStatus = HostVerificationStatus.Pending;
        host.RejectionReason = null;
        host.VerifiedAt = null;
        host.VerifiedByUserId = null;
        host.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Host {HostId} submitted verification", host.Id);
    }

    /// <summary>Emails every address in Admin:Emails. Best-effort: failures are logged.</summary>
    public async Task NotifyAdminsOfSubmissionAsync(HostProfile host, CancellationToken ct = default)
    {
        var admins = (_config["Admin:Emails"] ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (admins.Count == 0) return;

        var link = $"{FrontendBaseUrl()}/admin/hosts/{host.Id}";
        var lines = new List<string>
        {
            $"{host.DisplayName} ({host.Slug}) submitted their host verification.",
            "Review the ID document and past-trip links in the admin host queue.",
        };

        foreach (var admin in admins)
        {
            try
            {
                await _email.SendAsync(admin, "Host verification to review", lines, link);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not email admin about host {HostId} verification", host.Id);
            }
        }
    }

    /// <summary>Emails the host the verification decision. Returns true when sent. Best-effort.</summary>
    public async Task<bool> EmailVerificationDecisionAsync(HostProfile host, bool verified, string? reason, CancellationToken ct = default)
    {
        var to = await GetUserEmailAsync(host.UserId, ct);
        if (string.IsNullOrWhiteSpace(to)) return false;

        var lines = verified
            ? new List<string>
            {
                $"Good news: {host.DisplayName} is now a verified Hodrac host.",
                "Your trip pages show the Verified host badge and can take payments.",
            }
            : new List<string>
            {
                "We couldn't verify your host profile yet.",
                $"Reason: {reason}",
                "You can update your details and submit again from your host dashboard.",
            };

        try
        {
            await _email.SendAsync(to,
                verified ? "You're a verified Hodrac host" : "Your Hodrac host verification",
                lines,
                $"{FrontendBaseUrl()}/host");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not email host {HostId} their verification decision", host.Id);
            return false;
        }
    }

    /// <summary>Emails the host that their Verified badge was removed. Returns true when sent. Best-effort.</summary>
    public async Task<bool> EmailVerificationRevokedAsync(HostProfile host, string? reason, CancellationToken ct = default)
    {
        var to = await GetUserEmailAsync(host.UserId, ct);
        if (string.IsNullOrWhiteSpace(to)) return false;

        var lines = new List<string>
        {
            $"The Verified host badge has been removed from {host.DisplayName}.",
        };
        if (!string.IsNullOrWhiteSpace(reason)) lines.Add($"Reason: {reason}");
        lines.Add("Your trips stay up, but they can't take new payments until your profile is verified again.");
        lines.Add("You can update your details and submit for verification again from your host dashboard.");

        try
        {
            await _email.SendAsync(to, "Your Hodrac host verification was removed", lines, $"{FrontendBaseUrl()}/host");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not email host {HostId} about their removed verification", host.Id);
            return false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // ApplicationUser.Id is the Guid's string form.
    public Task<string?> GetUserEmailAsync(Guid userId, CancellationToken ct = default)
    {
        var id = userId.ToString();
        return _db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(ct);
    }

    private string FrontendBaseUrl() => (_config["Frontend:BaseUrl"] ?? "https://www.hodrac.com").TrimEnd('/');

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
