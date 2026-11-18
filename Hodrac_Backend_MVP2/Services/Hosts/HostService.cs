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
    public Task NotifyAdminsOfSubmissionAsync(HostProfile host, CancellationToken ct = default) =>
        EmailAdminsAsync(host.Id, "Host verification to review", new List<string>
        {
            $"{host.DisplayName} ({host.Slug}) submitted their host verification.",
            "Review the ID document and past-trip links in the admin host queue.",
        });

    /// <summary>
    /// Any status -> Unverified with a note, recording the admin and time as the
    /// last decision (VerifiedAt/VerifiedByUserId). Shared by revoke and close.
    /// Caller saves.
    /// </summary>
    public static void RemoveVerification(HostProfile host, Guid adminUserId, string note)
    {
        host.VerificationStatus = HostVerificationStatus.Unverified;
        host.RejectionReason = note.Length <= HostProfile.MaxRejectionReasonLength
            ? note
            : note[..HostProfile.MaxRejectionReasonLength];
        host.VerifiedAt = DateTime.UtcNow;
        host.VerifiedByUserId = adminUserId;
        host.UpdatedAt = DateTime.UtcNow;
    }

    // -- Admin account actions (close / reopen / delete) ----------------------

    /// <summary>
    /// Deactivates the host's Paystack subaccount (sends only active = false;
    /// bank details are untouched). Best-effort, called after the DB commit: on
    /// failure it logs and emails the admins. Returns null when there is no
    /// subaccount, true when deactivated, false when the Paystack call failed.
    /// </summary>
    public async Task<bool?> DeactivateSubaccountAsync(
        Guid hostId, string? subaccountCode, string displayName, string action, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subaccountCode)) return null;
        try
        {
            await _paystack.UpdateSubaccountAsync(subaccountCode, new PaystackSubaccountUpdate(Active: false), ct);
            _logger.LogInformation("Deactivated Paystack subaccount {Code} of host {HostId} ({Action})", subaccountCode, hostId, action);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not deactivate Paystack subaccount {Code} of host {HostId} ({Action})", subaccountCode, hostId, action);
            await EmailAdminsAsync(hostId, "Paystack subaccount still active", new List<string>
            {
                $"The host account {displayName} was {action}, but Paystack didn't deactivate its subaccount {subaccountCode}.",
                $"Error: {ex.Message}",
                "Deactivate it in the Paystack dashboard (for a closed host, closing it again retries).",
            });
            return false;
        }
    }

    /// <summary>
    /// Reactivates the host's Paystack subaccount (sends only active = true).
    /// Same best-effort pattern as <see cref="DeactivateSubaccountAsync"/>:
    /// null = no subaccount, true = reactivated, false = Paystack failed (logged,
    /// admins emailed).
    /// </summary>
    public async Task<bool?> ReactivateSubaccountAsync(
        Guid hostId, string? subaccountCode, string displayName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subaccountCode)) return null;
        try
        {
            await _paystack.UpdateSubaccountAsync(subaccountCode, new PaystackSubaccountUpdate(Active: true), ct);
            _logger.LogInformation("Reactivated Paystack subaccount {Code} of host {HostId} (reopened)", subaccountCode, hostId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not reactivate Paystack subaccount {Code} of host {HostId} (reopened)", subaccountCode, hostId);
            await EmailAdminsAsync(hostId, "Paystack subaccount still inactive", new List<string>
            {
                $"The host account {displayName} was reopened, but Paystack didn't reactivate its subaccount {subaccountCode}.",
                $"Error: {ex.Message}",
                "Reactivate it in the Paystack dashboard before the host is verified again, or payments to this host will fail.",
            });
            return false;
        }
    }

    /// <summary>Emails the host that Hodrac reopened their host account. Returns true when sent. Best-effort.</summary>
    public async Task<bool> EmailHostReopenedAsync(HostProfile host, CancellationToken ct = default)
    {
        var to = await GetUserEmailAsync(host.UserId, ct);
        if (string.IsNullOrWhiteSpace(to)) return false;

        var lines = new List<string>
        {
            $"Hodrac has reopened the host account {host.DisplayName}.",
            "You can edit your host profile and create hosted trips again.",
            "Your profile isn't verified yet: travellers can't pay until a Hodrac admin verifies it again.",
        };

        return await TrySendToHostAsync(host, to, "Your Hodrac host account was reopened", lines, "account reopening");
    }

    /// <summary>Emails the host that their founding status was removed. Returns true when sent. Best-effort.</summary>
    public async Task<bool> EmailFoundingRemovedAsync(HostProfile host, CancellationToken ct = default)
    {
        var to = await GetUserEmailAsync(host.UserId, ct);
        if (string.IsNullOrWhiteSpace(to)) return false;

        var lines = new List<string>
        {
            $"{host.DisplayName} is no longer a founding Hodrac host.",
            host.FoundingFreeDepartureTripId is not null
                ? "The free departure you already used stays fee-free for the travellers on that trip."
                : "The founding free departure is no longer available for your next hosted trip.",
        };

        return await TrySendToHostAsync(host, to, "Your Hodrac founding host status was removed", lines, "founding removal");
    }

    /// <summary>Emails the host that Hodrac closed their host account. Returns true when sent. Best-effort.</summary>
    public async Task<bool> EmailHostClosedAsync(HostProfile host, string? reason, CancellationToken ct = default)
    {
        var to = await GetUserEmailAsync(host.UserId, ct);
        if (string.IsNullOrWhiteSpace(to)) return false;

        var lines = new List<string> { $"Hodrac has closed the host account {host.DisplayName}." };
        if (!string.IsNullOrWhiteSpace(reason)) lines.Add($"Reason: {reason}");
        lines.Add("Your trips can't take new payments, and trips nobody has paid for are no longer shown.");
        lines.Add("Trips with paid travellers stay up: you can still open their manifests and record payments and refunds to settle with them.");
        lines.Add("If you think this is a mistake, contact Hodrac support.");

        return await TrySendToHostAsync(host, to, "Your Hodrac host account was closed", lines, "account closure");
    }

    private async Task<bool> TrySendToHostAsync(HostProfile host, string to, string subject, List<string> lines, string what)
    {
        try
        {
            await _email.SendAsync(to, subject, lines, $"{FrontendBaseUrl()}/host");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not email host {HostId} about their {What}", host.Id, what);
            return false;
        }
    }

    /// <summary>Emails every address in Admin:Emails, linking the admin host page. Best-effort.</summary>
    private async Task EmailAdminsAsync(Guid hostId, string subject, List<string> lines)
    {
        var admins = (_config["Admin:Emails"] ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var link = $"{FrontendBaseUrl()}/admin/hosts/{hostId}";

        foreach (var admin in admins)
        {
            try
            {
                await _email.SendAsync(admin, subject, lines, link);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not email admin ({Subject}) about host {HostId}", subject, hostId);
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
