using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for user accounts.
///
///   GET  /api/admin/users?page=&amp;pageSize=&amp;search=&amp;status=&amp;sort=
///        status: all | confirmed | unconfirmed | locked | onboarded
///        sort:   newest (default) | oldest | lastActive
///   GET  /api/admin/users/{id}
///   POST /api/admin/users/{id}/lock                 { until?, reason? } — also revokes all refresh tokens
///   POST /api/admin/users/{id}/unlock
///   POST /api/admin/users/{id}/resend-confirmation  — 409 if already confirmed
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private static readonly string[] Statuses = { "all", "confirmed", "unconfirmed", "locked", "onboarded" };
    private static readonly string[] Sorts = { "newest", "oldest", "lastActive" };

    private readonly HodracDbContext _db;
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        HodracDbContext db,
        IUserRepository users,
        ITokenService tokens,
        ICurrentUserService currentUser,
        ILogger<AdminUsersController> logger)
    {
        _db = db;
        _users = users;
        _tokens = tokens;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ══ List ══════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<ActionResult<AdminPagedResult<AdminUserRowDto>>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var normalizedStatus = Normalize(status, Statuses) ?? (string.IsNullOrWhiteSpace(status) ? "all" : null);
        if (normalizedStatus is null)
            ModelState.AddModelError(nameof(status), $"Status must be one of: {string.Join(", ", Statuses)}.");

        var normalizedSort = Normalize(sort, Sorts) ?? (string.IsNullOrWhiteSpace(sort) ? "newest" : null);
        if (normalizedSort is null)
            ModelState.AddModelError(nameof(sort), $"Sort must be one of: {string.Join(", ", Sorts)}.");

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var now = DateTimeOffset.UtcNow;
        var query = _db.Users.AsNoTracking();

        query = normalizedStatus switch
        {
            "confirmed" => query.Where(u => u.EmailConfirmed),
            "unconfirmed" => query.Where(u => !u.EmailConfirmed),
            "locked" => query.Where(u => u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now),
            "onboarded" => query.Where(u => u.HasCompletedOnboarding),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.Email!, pattern) ||
                EF.Functions.ILike(u.DisplayName, pattern));
        }

        var total = await query.CountAsync(ct);

        query = normalizedSort switch
        {
            "oldest" => query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id),
            // Postgres sorts NULLs first on DESC — push never-active users to the end.
            "lastActive" => query.OrderBy(u => u.LastActiveAt == null)
                                 .ThenByDescending(u => u.LastActiveAt)
                                 .ThenBy(u => u.Id),
            _ => query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserRowDto(
                u.Id,
                u.Email,
                u.DisplayName,
                u.AvatarUrl,
                u.CountryCode,
                u.EmailConfirmed,
                u.HasCompletedOnboarding,
                u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now,
                u.LockoutEnd,
                u.CreatedAt,
                u.LastLoginAt,
                u.LastActiveAt))
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminUserRowDto>(items, page, pageSize, total));
    }

    // ══ Detail ════════════════════════════════════════════════════════════════

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminUserDetailDto>> GetUser(string id, CancellationToken ct = default)
    {
        var detail = await LoadDetail(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    // ══ Lock / unlock ═════════════════════════════════════════════════════════

    [HttpPost("{id}/lock")]
    public async Task<ActionResult<AdminUserDetailDto>> LockUser(
        string id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AdminLockUserRequest? request,
        CancellationToken ct = default)
    {
        if (string.Equals(id, _currentUser.UserIdString, StringComparison.Ordinal))
            ModelState.AddModelError("id", "You cannot lock your own account.");

        var now = DateTimeOffset.UtcNow;
        var until = request?.Until?.ToUniversalTime() ?? now.AddYears(100);
        if (until <= now)
            ModelState.AddModelError(nameof(AdminLockUserRequest.Until), "Until must be in the future.");

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        var result = await _users.LockAsync(user, until);
        if (!result.Succeeded) return IdentityErrors(result);

        // Kill every session; the current access token still lives until it expires (<= 15 min).
        await _tokens.RevokeAllRefreshTokensAsync(user.Id, ct);

        _logger.LogWarning(
            "Admin {AdminUserId} locked user {UserId} until {Until}. Reason: {Reason}",
            _currentUser.UserIdString, user.Id, until, request?.Reason?.Trim() ?? "(none)");

        return Ok(await LoadDetail(id, ct));
    }

    [HttpPost("{id}/unlock")]
    public async Task<ActionResult<AdminUserDetailDto>> UnlockUser(string id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        var result = await _users.UnlockAsync(user);
        if (!result.Succeeded) return IdentityErrors(result);

        _logger.LogInformation("Admin {AdminUserId} unlocked user {UserId}", _currentUser.UserIdString, user.Id);

        return Ok(await LoadDetail(id, ct));
    }

    // ══ Resend confirmation ═══════════════════════════════════════════════════

    [HttpPost("{id}/resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(string id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        if (user.EmailConfirmed)
            return Conflict(new { error = "Email is already confirmed." });

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            ModelState.AddModelError("email", "User has no email address.");
            return ValidationProblem(ModelState);
        }

        var sent = await _users.TrySendConfirmationEmailAsync(user);
        if (!sent)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Could not send the confirmation email. Please try again later." });

        _logger.LogInformation("Admin {AdminUserId} resent confirmation email to user {UserId}", _currentUser.UserIdString, user.Id);
        return Ok(new { emailConfirmed = false, sent = true });
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    /// <summary>One round trip: the user row plus correlated count subqueries.</summary>
    private async Task<AdminUserDetailDto?> LoadDetail(string id, CancellationToken ct)
    {
        // Guid-keyed tables (trips, wishlists, saved, notifications) store the
        // Identity string Id parsed as a Guid; a non-Guid id matches nothing (Guid.Empty).
        var guid = Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty;
        var now = DateTimeOffset.UtcNow;

        var row = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                User = u,
                TripsPosted = _db.TripPosts.Count(t => t.AuthorUserId == guid),
                TripJoinRequests = _db.TripInterests.Count(i => i.RequesterUserId == guid),
                WishlistsOwned = _db.Wishlists.Count(w => w.OwnerUserId == guid && !w.IsTemplate),
                SavedWishlists = _db.SavedWishlists.Count(s => s.UserId == guid),
                SavedDestinations = _db.SavedDestinations.Count(s => s.UserId == guid),
                UnreadNotifications = _db.Notifications.Count(n => n.RecipientUserId == guid && !n.IsRead),
                ActiveSessions = _db.UserRefreshTokens.Count(t => t.UserId == u.Id && !t.IsRevoked && t.ExpiresAt > now)
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var u = row.User;
        return new AdminUserDetailDto(
            u.Id,
            u.Email,
            u.DisplayName,
            u.AvatarUrl,
            u.CountryCode,
            u.EmailConfirmed,
            u.HasCompletedOnboarding,
            u.LockoutEnabled && u.LockoutEnd is { } end && end > now,
            u.LockoutEnd,
            u.CreatedAt,
            u.LastLoginAt,
            u.LastActiveAt,
            u.NotificationEmailOptIn,
            u.AccessFailedCount,
            new AdminUserCountsDto(
                row.TripsPosted,
                row.TripJoinRequests,
                row.WishlistsOwned,
                row.SavedWishlists,
                row.SavedDestinations,
                row.UnreadNotifications,
                row.ActiveSessions));
    }

    private ActionResult IdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(error.Code, error.Description);
        return ValidationProblem(ModelState);
    }

    private static string? Normalize(string? value, string[] allowed)
        => value is null ? null : allowed.FirstOrDefault(s => string.Equals(s, value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
