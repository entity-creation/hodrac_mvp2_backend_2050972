using System.Text.RegularExpressions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.HostDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Images;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Hodrac Host onboarding for the signed-in user.
///
///   POST  /api/host/profile              create my host profile (one per user; 409 if it exists,
///                                        403 if the existing one was closed by Hodrac)
///   GET   /api/host/profile/me           my host profile (404 if none)
///   PATCH /api/host/profile/me           partial update
///   GET   /api/host/banks                Paystack's Nigerian banks (cached 24h)
///   POST  /api/host/payout-account       { bankCode, accountNumber, confirm? }: resolve the
///                                        account name; with confirm=true create/update the
///                                        Paystack subaccount (only the last 4 digits are kept)
///   POST  /api/host/verification         multipart, all optional: cacNumber?, idDocument? (PDF/JPG/PNG
///                                        ≤5 MB), pastTripProofUrls[]? (0–10 http(s) links) → Pending
///                                        (from Unverified/Pending/Rejected; 409 if Verified). Whatever
///                                        is provided is validated; nothing is required to be verified.
///
/// Hosted trips (HostController.Trips.cs): trips list, manifest (+ CSV), manual
/// payments/refunds, WhatsApp nudge link, weekly report.
///
/// A host closed by Hodrac (HostClosure) gets 403 on PATCH profile, payout-account
/// and verification; reads and the trip settlement endpoints stay open.
///
/// Responses never include the payout account number or document bytes/keys.
/// </summary>
[ApiController]
[Authorize]
[Route("api/host")]
public partial class HostController : ControllerBase
{
    private const int MaxProofUrls = 10;
    private const long MaxUploadRequestBytes = 8 * 1024 * 1024;

    private readonly HodracDbContext _db;
    private readonly HostService _hosts;
    private readonly HostTripService _hostTrips;
    private readonly HostReportService _reports;
    private readonly PaymentService _payments;
    private readonly IPaystackClient _paystack;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<HostController> _logger;
    private readonly ImageUrlNormalizer _images;

    public HostController(
        HodracDbContext db,
        HostService hosts,
        HostTripService hostTrips,
        HostReportService reports,
        PaymentService payments,
        IPaystackClient paystack,
        ICurrentUserService currentUser,
        ILogger<HostController> logger,
        ImageUrlNormalizer images)
    {
        _images = images;
        _db = db;
        _hosts = hosts;
        _hostTrips = hostTrips;
        _reports = reports;
        _payments = payments;
        _paystack = paystack;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ══ Profile ═══════════════════════════════════════════════════════════════

    [HttpPost("profile")]
    public async Task<ActionResult<HostProfileDto>> CreateProfile(CreateHostProfileRequest request, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty) return Unauthorized();

        var existing = await _hosts.GetByUserAsync(userId, ct);
        if (existing is not null)
            return existing.IsClosed
                ? HostClosure.HostForbidden()
                : Conflict(new { error = "You already have a host profile." });

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length == 0)
            ModelState.AddModelError(nameof(request.DisplayName), "Display name is required.");

        var slug = NullIfBlank(request.Slug);
        if (slug is not null && !HostSlug.IsValid(slug))
            ModelState.AddModelError(nameof(request.Slug), SlugRuleMessage);

        var host = new HostProfile { UserId = userId, DisplayName = displayName };
        ApplyOptionalFields(host, request.Bio, request.LogoUrl, request.InstagramHandle, request.WhatsAppNumber);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (slug is not null && await _hosts.IsSlugTakenAsync(slug, null, ct))
            return Conflict(new { error = "That link name is taken. Try another." });

        host.Slug = slug ?? await _hosts.GenerateSlugAsync(displayName, ct);

        _db.HostProfiles.Add(host);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Raced another request: either this user's second profile or the slug.
            return Conflict(new { error = "You already have a host profile, or that link name is taken." });
        }

        _logger.LogInformation("User {UserId} created host profile {HostId} ({Slug})", userId, host.Id, host.Slug);
        return CreatedAtAction(nameof(GetMyProfile), null, HostProfileDto.FromEntity(host));
    }

    [HttpGet("profile/me")]
    public async Task<ActionResult<HostProfileDto>> GetMyProfile(CancellationToken ct = default)
    {
        var host = await _hosts.GetByUserAsync(_currentUser.UserId, ct);
        return host is null ? NotFound() : Ok(HostProfileDto.FromEntity(host));
    }

    [HttpPatch("profile/me")]
    public async Task<ActionResult<HostProfileDto>> UpdateMyProfile(UpdateHostProfileRequest request, CancellationToken ct = default)
    {
        var host = await _hosts.GetByUserAsync(_currentUser.UserId, ct);
        if (host is null) return NotFound();
        if (host.IsClosed) return HostClosure.HostForbidden();

        var nameChanged = false;
        if (request.DisplayName is not null)
        {
            var displayName = request.DisplayName.Trim();
            if (displayName.Length == 0)
                ModelState.AddModelError(nameof(request.DisplayName), "Display name cannot be blank.");
            else if (displayName != host.DisplayName)
            {
                host.DisplayName = displayName;
                nameChanged = true;
            }
        }

        string? newSlug = null;
        if (request.Slug is not null)
        {
            var slug = request.Slug.Trim();
            if (!HostSlug.IsValid(slug))
                ModelState.AddModelError(nameof(request.Slug), SlugRuleMessage);
            else if (slug != host.Slug)
                newSlug = slug;
        }

        ApplyOptionalFields(host, request.Bio, request.LogoUrl, request.InstagramHandle, request.WhatsAppNumber);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (newSlug is not null)
        {
            if (await _hosts.IsSlugTakenAsync(newSlug, host.Id, ct))
                return Conflict(new { error = "That link name is taken. Try another." });
            host.Slug = newSlug;
        }

        host.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "That link name is taken. Try another." });
        }

        // Paystack business_name follows the display name.
        if (nameChanged) await _hosts.SyncSubaccountNameAsync(host, ct);

        return Ok(HostProfileDto.FromEntity(host));
    }

    // ══ Banks + payout account ════════════════════════════════════════════════

    [HttpGet("banks")]
    public async Task<ActionResult<List<HostBankDto>>> GetBanks(CancellationToken ct = default)
    {
        try
        {
            var banks = await _paystack.ListBanksAsync(ct);
            return Ok(banks.Select(b => new HostBankDto(b.Name, b.Code)).ToList());
        }
        catch (PaystackException ex)
        {
            return PaystackFailure(ex, null);
        }
    }

    [HttpPost("payout-account")]
    public async Task<ActionResult<PayoutAccountResultDto>> SetPayoutAccount(PayoutAccountRequest request, CancellationToken ct = default)
    {
        var host = await _hosts.GetByUserAsync(_currentUser.UserId, ct);
        if (host is null) return NotFound();
        if (host.IsClosed) return HostClosure.HostForbidden();

        var bankCode = request.BankCode?.Trim() ?? string.Empty;
        var accountNumber = Regex.Replace(request.AccountNumber ?? string.Empty, @"[\s-]", "");

        if (!BankCodePattern().IsMatch(bankCode))
            ModelState.AddModelError(nameof(request.BankCode), "Choose a bank from the list.");
        if (!NubanPattern().IsMatch(accountNumber))
            ModelState.AddModelError(nameof(request.AccountNumber), "Account number must be 10 digits.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        try
        {
            var banks = await _paystack.ListBanksAsync(ct);
            var bank = banks.FirstOrDefault(b => b.Code == bankCode);
            if (bank is null)
            {
                ModelState.AddModelError(nameof(request.BankCode), "Choose a bank from the list.");
                return ValidationProblem(ModelState);
            }

            var resolved = await _paystack.ResolveAccountAsync(accountNumber, bankCode, ct);
            var last4 = accountNumber[^HostProfile.AccountLast4Length..];

            if (!request.Confirm)
                return Ok(new PayoutAccountResultDto(false, bank.Code, bank.Name, resolved.AccountName, last4, null));

            await _hosts.SetPayoutAccountAsync(host, bank.Code, bank.Name, accountNumber, resolved.AccountName, ct);
            return Ok(new PayoutAccountResultDto(true, bank.Code, bank.Name, resolved.AccountName, last4, HostProfileDto.FromEntity(host)));
        }
        catch (PaystackException ex)
        {
            return PaystackFailure(ex, nameof(request.AccountNumber));
        }
    }

    // ══ Verification ══════════════════════════════════════════════════════════

    [HttpPost("verification")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<ActionResult<HostProfileDto>> SubmitVerification([FromForm] HostVerificationForm form, CancellationToken ct = default)
    {
        var host = await _hosts.GetByUserAsync(_currentUser.UserId, ct);
        if (host is null) return NotFound();
        if (host.IsClosed) return HostClosure.HostForbidden();

        if (host.VerificationStatus == HostVerificationStatus.Verified)
            return Conflict(new { error = "Your host profile is already verified." });

        var cacNumber = NullIfBlank(form.CacNumber);
        if (cacNumber is not null && !CacPattern().IsMatch(cacNumber))
            ModelState.AddModelError(nameof(form.CacNumber), "CAC number can only contain letters, numbers, spaces and hyphens.");

        var urls = (form.PastTripProofUrls ?? new List<string>())
            .Select(u => u?.Trim() ?? string.Empty)
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // Nothing is required to be verified: links are optional, but any given are validated.
        if (urls.Count > MaxProofUrls)
            ModelState.AddModelError(nameof(form.PastTripProofUrls), $"Add at most {MaxProofUrls} links.");
        else if (urls.Any(u => u.Length > HostProfile.MaxUrlLength || !IsHttpUrl(u)))
            ModelState.AddModelError(nameof(form.PastTripProofUrls), "Each link must be a full http(s) URL.");

        byte[]? content = null;
        string? contentType = null;
        var file = form.IdDocument;
        if (file is null)
        {
            // Optional: keep any previously uploaded document.
        }
        else if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(form.IdDocument), "The ID document is empty.");
        }
        else if (file.Length > HostDocument.MaxSizeBytes)
        {
            ModelState.AddModelError(nameof(form.IdDocument), "The ID document must be 5 MB or smaller.");
        }
        else
        {
            using var ms = new MemoryStream((int)file.Length);
            await file.CopyToAsync(ms, ct);
            content = ms.ToArray();
            contentType = HostDocumentFiles.DetectContentType(content);
            if (contentType is null)
            {
                content = null;
                ModelState.AddModelError(nameof(form.IdDocument), "The ID document must be a PDF, JPG or PNG file.");
            }
        }

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        await _hosts.SubmitVerificationAsync(
            host,
            cacNumber,
            content,
            contentType,
            content is null ? null : HostDocumentFiles.SafeFileName(file!.FileName, contentType!),
            urls,
            ct);

        await _hosts.NotifyAdminsOfSubmissionAsync(host, ct);

        return Ok(HostProfileDto.FromEntity(host));
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    private const string SlugRuleMessage =
        "Link name must be 3–80 characters: lowercase letters and numbers, separated by single hyphens.";

    [GeneratedRegex("^[0-9A-Za-z]{1,16}$")]
    private static partial Regex BankCodePattern();

    [GeneratedRegex(@"^\d{10}$")]
    private static partial Regex NubanPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 \-]{1,31}$")]
    private static partial Regex CacPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._]{1,30}$")]
    private static partial Regex InstagramPattern();

    [GeneratedRegex(@"^\+?[0-9]{7,15}$")]
    private static partial Regex PhonePattern();

    /// <summary>Validates and applies Bio/LogoUrl/Instagram/WhatsApp: null = unchanged, "" = clear.</summary>
    private void ApplyOptionalFields(HostProfile host, string? bio, string? logoUrl, string? instagram, string? whatsApp)
    {
        if (bio is not null) host.Bio = NullIfBlank(bio);

        if (logoUrl is not null)
        {
            var url = NullIfBlank(_images.Normalize(logoUrl));
            if (url is not null && !IsHttpUrl(url))
                ModelState.AddModelError("LogoUrl", "Logo URL must be a full http(s) URL.");
            else
                host.LogoUrl = url;
        }

        if (instagram is not null)
        {
            var handle = NullIfBlank(instagram)?.TrimStart('@');
            if (handle is not null && !InstagramPattern().IsMatch(handle))
                ModelState.AddModelError("InstagramHandle", "Instagram handle can only contain letters, numbers, '.' and '_'.");
            else
                host.InstagramHandle = handle;
        }

        if (whatsApp is not null)
        {
            var phone = NullIfBlank(whatsApp) is { } raw ? Regex.Replace(raw, @"[\s\-()]", "") : null;
            if (phone is not null && !PhonePattern().IsMatch(phone))
                ModelState.AddModelError("WhatsAppNumber", "WhatsApp number must be 7–15 digits, optionally starting with +.");
            else
                host.WhatsAppNumber = phone;
        }
    }

    /// <summary>Paystack errors: not configured → 503; rejected input → 400; anything else → 502.</summary>
    private ActionResult PaystackFailure(PaystackException ex, string? field)
    {
        if (ex is PaystackNotConfiguredException)
        {
            _logger.LogError("Paystack is not configured; host payout endpoints are unavailable");
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Payments are not available right now.");
        }

        if (ex.IsClientError && field is not null)
        {
            ModelState.AddModelError(field, $"Paystack couldn't verify this account: {ex.Message}");
            return ValidationProblem(ModelState);
        }

        _logger.LogWarning(ex, "Paystack call failed");
        return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Couldn't reach our payment provider. Try again shortly.");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
