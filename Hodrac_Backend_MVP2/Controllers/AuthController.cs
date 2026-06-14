using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.NoSql.Models;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// POST /api/auth/register       — create account
/// POST /api/auth/login          — issue access + refresh tokens
/// POST /api/auth/refresh        — rotate refresh token, issue new access token
/// POST /api/auth/logout         — revoke refresh token(s)
/// GET  /api/auth/me             — current user profile (requires auth)
/// PATCH /api/auth/me            — update display name / avatar
/// PATCH /api/auth/me/onboarding — mark personalization modal complete
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;
    private readonly ICurrentUserService _currentUser;
    private readonly IProfileRepository _profiles;

    public AuthController(
        IUserRepository users,
        ITokenService tokens,
        ICurrentUserService currentUser,
        IProfileRepository profiles)
    {
        _users       = users;
        _tokens      = tokens;
        _currentUser = currentUser;
        _profiles    = profiles;
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
        var profile = new UserPsychographicProfile
        {
            Id                      = $"prof_{user.Id}",
            AuthUserReferenceId     = user.Id,
            AnonymizedMarketplaceId = $"anon_{Guid.NewGuid():N}",
            SystemTelemetry = new SystemTelemetry
            {
                FirstSeenAt              = DateTime.UtcNow,
                LastInteractionDeltaAt   = DateTime.UtcNow,
            }
        };
        await _profiles.UpsertAsync(profile);

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
        if (user is null || !await _users.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { error = "Invalid email or password." });

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

    [HttpPatch("me/onboarding")]
    [Authorize]
    public async Task<IActionResult> CompleteOnboarding(CancellationToken ct = default)
    {
        await _users.MarkOnboardingCompleteAsync(_currentUser.UserIdString, ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetDeviceHint()
    {
        // Use User-Agent as a lightweight device hint for the "Sessions" UI.
        // Not used for security decisions — purely informational.
        return Request.Headers.UserAgent.ToString()[..Math.Min(300,
            Request.Headers.UserAgent.ToString().Length)];
    }

    private static UserDto MapToUserDto(ApplicationUser user) => new(
        UserId              : user.Id,
        Email               : user.Email ?? string.Empty,
        DisplayName         : user.DisplayName,
        AvatarUrl           : user.AvatarUrl,
        HasCompletedOnboarding: user.HasCompletedOnboarding,
        CreatedAt           : user.CreatedAt,
        LastLoginAt         : user.LastLoginAt
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
    string? AvatarUrl
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
    DateTimeOffset? LastLoginAt
);
