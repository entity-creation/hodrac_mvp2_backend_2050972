using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Hodrac_Backend_MVP2.Events;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.NoSql.Models;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// POST /api/auth/register       — create account
/// POST /api/auth/login          — issue access + refresh tokens
///                                 (403 "Account locked." only after a correct password;
///                                 wrong passwords count toward Identity lockout)
/// POST /api/auth/refresh        — rotate refresh token, issue new access token
///                                 (403 "Account locked." + all tokens revoked when admin-locked)
/// POST /api/auth/logout         — revoke refresh token(s)
/// GET  /api/auth/me             — current user profile (requires auth)
/// PATCH /api/auth/me            — update display name / avatar
/// PATCH /api/auth/me/onboarding — save personalization answers + mark modal complete
/// PATCH /api/auth/me/notification-preferences — email digest opt in/out
/// POST /api/auth/forgot-password       — email a password reset link (always 200)
/// POST /api/auth/reset-password        — reset password with emailed token
/// POST /api/auth/change-password       — change password (requires auth)
/// POST /api/auth/confirm-email         — confirm email with emailed token
/// POST /api/auth/resend-confirmation   — resend confirmation email (requires auth)
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;
    private readonly ICurrentUserService _currentUser;
    private readonly IProfileRepository _profiles;
    private readonly IEmailSender _email;
    private readonly IBehaviorEventQueue _events;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;
    private readonly IdentityOptions _identityOptions;

    public AuthController(
        IUserRepository users,
        ITokenService tokens,
        ICurrentUserService currentUser,
        IProfileRepository profiles,
        IEmailSender email,
        IBehaviorEventQueue events,
        IConfiguration config,
        ILogger<AuthController> logger,
        IOptions<IdentityOptions> identityOptions)
    {
        _users       = users;
        _tokens      = tokens;
        _currentUser = currentUser;
        _profiles    = profiles;
        _email       = email;
        _events      = events;
        _config      = config;
        _logger      = logger;
        _identityOptions = identityOptions.Value;
    }

    // ── POST /api/auth/register ───────────────────────────────────────────────

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Check for duplicate email before calling UserManager to give a clear error
        var existing = await _users.GetByEmailAsync(request.Email, ct);
        if (existing is not null)
            return Conflict(new { error = "An account with this email already exists." });

        var user = new ApplicationUser
        {
            Id          = Guid.NewGuid().ToString(),    // Explicit Guid so Postgres FK columns are predictable
            UserName    = request.Email,
            Email       = request.Email,
            DisplayName = request.DisplayName,
            CreatedAt   = DateTimeOffset.UtcNow,
        };

        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }

        // Create the MongoDB psychographic profile immediately on registration.
        // It starts empty — populated progressively by interactions and onboarding.
        var profile = Hodrac_Backend_MVP2.NoSql.Repositories.ProfileRepository.NewMinimalProfile(user.Id, DateTime.UtcNow);
        // Best-effort: if Mongo is down the account still works; the profile is
        // created later by onboarding / the first behavior event.
        try
        {
            await _profiles.UpsertAsync(profile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create Mongo profile for new user {UserId}", user.Id);
        }

        // Confirmation email is best-effort — never fail registration over it.
        // Login is NOT gated on EmailConfirmed; the frontend can nudge via /me.
        await TrySendConfirmationEmailAsync(user);

        // Issue tokens immediately — user is logged in after registration
        var accessToken  = _tokens.GenerateAccessToken(user);
        var refreshToken = await _tokens.GenerateRefreshTokenAsync(
            user, GetDeviceHint(), ct);

        await _users.UpdateLastLoginAsync(user.Id, ct);

        return CreatedAtAction(nameof(GetMe), null, new AuthResponse(
            AccessToken : accessToken,
            RefreshToken: refreshToken,
            ExpiresIn   : 900,    // 15 minutes in seconds
            User        : MapToUserDto(user)
        ));
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await _users.GetByEmailAsync(request.Email, ct);

        // Deliberately identical error for wrong email vs wrong password
        // — prevents user enumeration attacks
        if (user is null)
            return Unauthorized(new { error = "Invalid email or password." });

        var lockedOut = await _users.IsLockedOutAsync(user);

        if (!await _users.CheckPasswordAsync(user, request.Password))
        {
            // Count toward Identity lockout (MaxFailedAccessAttempts / DefaultLockoutTimeSpan).
            // Skipped while already locked: AccessFailedAsync would overwrite an admin's
            // longer LockoutEnd with the 15-minute default.
            if (!lockedOut)
                await _users.AccessFailedAsync(user);
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // Lock status is only revealed once the password has been verified.
        if (lockedOut)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Account locked." });

        if (user.AccessFailedCount > 0)
            await _users.ResetAccessFailedCountAsync(user);

        var accessToken  = _tokens.GenerateAccessToken(user);
        var refreshToken = await _tokens.GenerateRefreshTokenAsync(
            user, GetDeviceHint(), ct);

        await _users.UpdateLastLoginAsync(user.Id, ct);

        return Ok(new AuthResponse(
            AccessToken : accessToken,
            RefreshToken: refreshToken,
            ExpiresIn   : 900,
            User        : MapToUserDto(user)
        ));
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────────
    // Client sends its stored refresh token. We validate, rotate, issue new pair.
    // The old refresh token is immediately revoked on use (rotation prevents replay).

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { error = "UserId and RefreshToken are required." });

        var record = await _tokens.ValidateAndRotateRefreshTokenAsync(
            request.UserId, request.RefreshToken, ct);

        if (record is null)
            return Unauthorized(new { error = "Invalid or expired refresh token." });

        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Unauthorized(new { error = "User not found." });

        // Admin-locked: the presented token is already rotated out; revoke the rest too.
        if (IsAdminLock(user))
        {
            await _tokens.RevokeAllRefreshTokensAsync(user.Id, ct);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Account locked." });
        }

        // A refresh means the user is actively using the app (access tokens are
        // 15 minutes) — keeps the email digest from emailing active users.
        await _users.UpdateLastActiveAsync(user.Id, ct);

        // Issue fresh pair
        var newAccessToken  = _tokens.GenerateAccessToken(user);
        var newRefreshToken = await _tokens.GenerateRefreshTokenAsync(
            user, record.DeviceHint, ct);

        return Ok(new AuthResponse(
            AccessToken : newAccessToken,
            RefreshToken: newRefreshToken,
            ExpiresIn   : 900,
            User        : MapToUserDto(user)
        ));
    }

    // ── POST /api/auth/newsletter

    [HttpPost("newletter")]
    public async Task<IActionResult> Create(NewsLetterEmail email)
    {
        var context = new ValidationContext(email);
        var results = new List<ValidationResult>();

        var isValidEmail = Validator.TryValidateObject(email, context, results, true);

        if (!isValidEmail)
            return BadRequest();
        var newsLetterEmail = await _users.CreateNewsLetterEmail(email);
        if (newsLetterEmail == null)
            return BadRequest(new { error = "Email already exist" });
        return Ok(newsLetterEmail);
    }

    // ── POST /api/auth/logout ─────────────────────────────────────────────────

    [HttpPost("logout")]
    [AllowAnonymous]   // AllowAnonymous so expired access tokens can still logout
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required." });

        if (request.LogoutAllDevices)
            await _tokens.RevokeAllRefreshTokensAsync(request.UserId, ct);
        else if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            await _tokens.RevokeRefreshTokenAsync(request.UserId, request.RefreshToken, ct);

        return NoContent();
    }

    // ── GET /api/auth/me ──────────────────────────────────────────────────────

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserIdString, ct);
        if (user is null) return NotFound();
        return Ok(MapToUserDto(user));
    }

    // ── PATCH /api/auth/me ────────────────────────────────────────────────────

    [HttpPatch("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateProfileRequest request,
        CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserIdString, ct);
        if (user is null) return NotFound();

        if (request.DisplayName is not null) user.DisplayName = request.DisplayName;
        if (request.AvatarUrl is not null)   user.AvatarUrl   = request.AvatarUrl;

        // null = leave unchanged, "" = clear, otherwise must be ISO 3166-1 alpha-2.
        if (request.CountryCode is not null)
        {
            var code = request.CountryCode.Trim().ToUpperInvariant();
            if (code.Length == 0)
                user.CountryCode = null;
            else if (IsoCountryCodes.IsValid(code))
                user.CountryCode = code;
            else
            {
                ModelState.AddModelError(nameof(request.CountryCode), "countryCode must be a two-letter ISO 3166-1 code, e.g. \"GB\".");
                return ValidationProblem(ModelState);
            }
        }

        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }

        return Ok(MapToUserDto(user));
    }

    // ── PATCH /api/auth/me/onboarding ─────────────────────────────────────────
    // Called by the frontend after the personalization modal is submitted.
    // Sets HasCompletedOnboarding = true so the modal is not shown again.

    // Body is optional for backward compatibility: older clients send nothing,
    // in which case the answers are published as empty strings.

    [HttpPatch("me/onboarding")]
    [Authorize]
    public async Task<IActionResult> CompleteOnboarding(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] OnboardingRequest? request,
        CancellationToken ct = default)
    {
        var userId = _currentUser.UserIdString;
        await _users.MarkOnboardingCompleteAsync(userId, ct);

        // Persisted asynchronously to the MongoDB profile by BehaviorEventWorker →
        // TagInferenceService, so a slow Mongo never fails this request.
        _events.Publish(new OnboardingCompletedEvent(
            UserId            : userId,
            WhoTheyTravelWith : request?.WhoTheyTravelWith?.Trim() ?? string.Empty,
            TripTypePreference: request?.TripTypePreference?.Trim() ?? string.Empty,
            BudgetProfile     : request?.BudgetProfile?.Trim() ?? string.Empty,
            PrimaryPriority   : request?.PrimaryPriority?.Trim() ?? string.Empty,
            OccurredAt        : DateTimeOffset.UtcNow));

        return NoContent();
    }

    // ── PATCH /api/auth/me/notification-preferences ───────────────────────────

    [HttpPatch("me/notification-preferences")]
    [Authorize]
    public async Task<IActionResult> UpdateNotificationPreferences(
        [FromBody] NotificationPreferencesRequest request,
        CancellationToken ct = default)
    {
        await _users.SetNotificationEmailOptInAsync(_currentUser.UserIdString, request.EmailOptIn, ct);
        return Ok(new { emailOptIn = request.EmailOptIn });
    }

    // ── POST /api/auth/forgot-password ────────────────────────────────────────
    // Always 200 with the same body whether or not the account exists
    // (prevents account enumeration). Send failures are logged, not surfaced.

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var response = new { message = "If an account exists for that email, a password reset link has been sent." };

        var user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return Ok(response);

        try
        {
            var token = EncodeToken(await _users.GeneratePasswordResetTokenAsync(user));
            var link  = BuildFrontendLink("reset-password", user.Id, token);

            await _email.SendAsync(
                to       : user.Email,
                subject  : "Reset your Hodrac password",
                bodyLines: new List<string>
                {
                    $"Hi {user.DisplayName},",
                    "We received a request to reset your Hodrac password. Use the link below to choose a new one.",
                    "If you did not request this, you can safely ignore this email. Your password will not change.",
                },
                deepLink : link);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {UserId}", user.Id);
        }

        return Ok(response);
    }

    // ── POST /api/auth/reset-password ─────────────────────────────────────────
    // On success all refresh tokens are revoked — every device must log in again.

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user  = await _users.GetByIdAsync(request.UserId, ct);
        var token = DecodeToken(request.Token);
        if (user is null || token is null)
            return BadRequest(new { error = "Invalid or expired reset link." });

        var result = await _users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
            return IdentityErrors(result);

        await _tokens.RevokeAllRefreshTokensAsync(user.Id, ct);
        return Ok(new { message = "Password has been reset. Please log in again." });
    }

    // ── POST /api/auth/change-password ────────────────────────────────────────
    // On success ALL of the user's refresh tokens are revoked (including the
    // current device's). The current access token stays valid until it expires
    // (<= 15 min); the client should log in again with the new password.

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await _users.GetByIdAsync(_currentUser.UserIdString, ct);
        if (user is null) return NotFound();

        var result = await _users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return IdentityErrors(result);

        await _tokens.RevokeAllRefreshTokensAsync(user.Id, ct);
        return Ok(new { message = "Password changed. Please log in again." });
    }

    // ── POST /api/auth/confirm-email ──────────────────────────────────────────

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user  = await _users.GetByIdAsync(request.UserId, ct);
        var token = DecodeToken(request.Token);
        if (user is null || token is null)
            return BadRequest(new { error = "Invalid or expired confirmation link." });

        if (user.EmailConfirmed)
            return Ok(new { emailConfirmed = true });

        var result = await _users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
            return IdentityErrors(result);

        return Ok(new { emailConfirmed = true });
    }

    // ── POST /api/auth/resend-confirmation ────────────────────────────────────

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting("auth-email")]
    [Authorize]
    public async Task<IActionResult> ResendConfirmation(CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserIdString, ct);
        if (user is null) return NotFound();

        if (user.EmailConfirmed)
            return Ok(new { emailConfirmed = true, sent = false });

        var sent = await TrySendConfirmationEmailAsync(user);
        if (!sent)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Could not send the confirmation email. Please try again later." });

        return Ok(new { emailConfirmed = false, sent = true });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetDeviceHint()
    {
        // Use User-Agent as a lightweight device hint for the "Sessions" UI.
        // Not used for security decisions — purely informational.
        return Request.Headers.UserAgent.ToString()[..Math.Min(300,
            Request.Headers.UserAgent.ToString().Length)];
    }

    /// <summary>Sends the confirmation email (shared with the admin resend). Never throws.</summary>
    private Task<bool> TrySendConfirmationEmailAsync(ApplicationUser user)
        => _users.TrySendConfirmationEmailAsync(user);

    /// <summary>
    /// True for a lock that outlasts Identity's automatic failed-password lockout
    /// window — i.e. an admin lock. Used by refresh so that 5 wrong password guesses
    /// by a third party can't kill the real user's existing sessions.
    /// </summary>
    private bool IsAdminLock(ApplicationUser user)
        => user.LockoutEnabled
           && user.LockoutEnd is { } end
           && end > DateTimeOffset.UtcNow.Add(_identityOptions.Lockout.DefaultLockoutTimeSpan);

    private string BuildFrontendLink(string path, string userId, string encodedToken)
    {
        var baseUrl = (_config["Frontend:BaseUrl"] ?? "https://www.hodrac.com").TrimEnd('/');
        return $"{baseUrl}/{path}?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(encodedToken)}";
    }

    // Identity tokens contain '+', '/', '=' — Base64Url-encode for safe use in links.
    private static string EncodeToken(string token)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string? DecodeToken(string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private IActionResult IdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(error.Code, error.Description);
        return ValidationProblem(ModelState);
    }

    private static UserDto MapToUserDto(ApplicationUser user) => new(
        UserId              : user.Id,
        Email               : user.Email ?? string.Empty,
        DisplayName         : user.DisplayName,
        AvatarUrl           : user.AvatarUrl,
        HasCompletedOnboarding: user.HasCompletedOnboarding,
        CreatedAt           : user.CreatedAt,
        LastLoginAt         : user.LastLoginAt,
        EmailConfirmed      : user.EmailConfirmed,
        NotificationEmailOptIn: user.NotificationEmailOptIn,
        CountryCode         : user.CountryCode
    );
}

// ─── Request / Response records ───────────────────────────────────────────────

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(320)]
    string Email,

    [Required, MinLength(8), MaxLength(100)]
    string Password,

    [Required, MaxLength(100)]
    string DisplayName
);

public record LoginRequest(
    [Required, EmailAddress]
    string Email,

    [Required]
    string Password
);

public record RefreshRequest(
    [Required] string UserId,
    [Required] string RefreshToken
);

public record LogoutRequest(
    [Required] string UserId,
    string? RefreshToken,
    bool LogoutAllDevices = false
);

public record UpdateProfileRequest(
    string? DisplayName,
    string? AvatarUrl,
    string? CountryCode
);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,        // seconds until access token expires
    UserDto User
);

public record UserDto(
    string UserId,
    string Email,
    string DisplayName,
    string AvatarUrl,
    bool HasCompletedOnboarding,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool EmailConfirmed,
    bool NotificationEmailOptIn,
    string? CountryCode
);

public record OnboardingRequest(
    [MaxLength(100)] string? WhoTheyTravelWith,
    [MaxLength(100)] string? TripTypePreference,
    [MaxLength(100)] string? BudgetProfile,
    [MaxLength(100)] string? PrimaryPriority
);

public record NotificationPreferencesRequest(
    bool EmailOptIn
);

public record ForgotPasswordRequest(
    [Required, EmailAddress, MaxLength(320)]
    string Email
);

public record ResetPasswordRequest(
    [Required] string UserId,
    [Required] string Token,
    [Required, MinLength(8), MaxLength(100)]
    string NewPassword
);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8), MaxLength(100)]
    string NewPassword
);

public record ConfirmEmailRequest(
    [Required] string UserId,
    [Required] string Token
);
