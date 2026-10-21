using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Data;

namespace Hodrac_Backend_MVP2.Repositories;

// ─── Interface ────────────────────────────────────────────────────────────────



// ─── Implementation ───────────────────────────────────────────────────────────

public class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HodracDbContext _db;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(
        UserManager<ApplicationUser> userManager,
        HodracDbContext db,
        IEmailSender email,
        IConfiguration config,
        ILogger<UserRepository> logger)
    {
        _userManager = userManager;
        _db          = db;
        _email       = email;
        _config      = config;
        _logger      = logger;
    }

    public async Task<ApplicationUser?> GetByIdAsync(
        string userId, CancellationToken ct = default)
        => await _userManager.FindByIdAsync(userId);

    public async Task<ApplicationUser?> GetByEmailAsync(
        string email, CancellationToken ct = default)
        => await _userManager.FindByEmailAsync(email);

    public async Task<ApplicationUser?> GetByGuidAsync(
        Guid userId, CancellationToken ct = default)
        => await _userManager.FindByIdAsync(userId.ToString());

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        => await _userManager.CreateAsync(user, password);

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user)
        => await _userManager.UpdateAsync(user);

    public async Task<bool> CheckPasswordAsync(ApplicationUser user, string password)
        => await _userManager.CheckPasswordAsync(user, password);

    public async Task MarkOnboardingCompleteAsync(
        string userId, CancellationToken ct = default)
        => await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.HasCompletedOnboarding, true), ct);

    public async Task UpdateLastLoginAsync(
        string userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LastLoginAt, now)
                .SetProperty(u => u.LastActiveAt, now), ct);
    }

    public async Task UpdateLastActiveAsync(
        string userId, CancellationToken ct = default)
        => await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.LastActiveAt, DateTimeOffset.UtcNow), ct);

    public async Task SetNotificationEmailOptInAsync(
        string userId, bool optIn, CancellationToken ct = default)
        => await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.NotificationEmailOptIn, optIn), ct);

    // ── Identity token flows ──────────────────────────────────────────────────

    public async Task<string> GeneratePasswordResetTokenAsync(ApplicationUser user)
        => await _userManager.GeneratePasswordResetTokenAsync(user);

    public async Task<IdentityResult> ResetPasswordAsync(
        ApplicationUser user, string token, string newPassword)
        => await _userManager.ResetPasswordAsync(user, token, newPassword);

    public async Task<string> GenerateEmailConfirmationTokenAsync(ApplicationUser user)
        => await _userManager.GenerateEmailConfirmationTokenAsync(user);

    public async Task<IdentityResult> ConfirmEmailAsync(ApplicationUser user, string token)
        => await _userManager.ConfirmEmailAsync(user, token);

    public async Task<IdentityResult> ChangePasswordAsync(
        ApplicationUser user, string currentPassword, string newPassword)
        => await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);

    public async Task<bool> TrySendConfirmationEmailAsync(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return false;

        try
        {
            var token = EncodeToken(await _userManager.GenerateEmailConfirmationTokenAsync(user));
            var link  = BuildFrontendLink("confirm-email", user.Id, token);

            await _email.SendAsync(
                to       : user.Email,
                subject  : "Confirm your Hodrac email",
                bodyLines: new List<string>
                {
                    $"Hi {user.DisplayName},",
                    "Welcome to Hodrac! Please confirm your email address using the link below.",
                },
                deepLink : link);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email to {UserId}", user.Id);
            return false;
        }
    }

    // ── Lockout ───────────────────────────────────────────────────────────────

    public async Task<bool> IsLockedOutAsync(ApplicationUser user)
        => await _userManager.IsLockedOutAsync(user);

    public async Task<IdentityResult> AccessFailedAsync(ApplicationUser user)
        => await _userManager.AccessFailedAsync(user);

    public async Task<IdentityResult> ResetAccessFailedCountAsync(ApplicationUser user)
        => await _userManager.ResetAccessFailedCountAsync(user);

    public async Task<IdentityResult> LockAsync(ApplicationUser user, DateTimeOffset until)
    {
        // SetLockoutEndDateAsync fails when LockoutEnabled is false, so enable it first.
        if (!user.LockoutEnabled)
        {
            var enabled = await _userManager.SetLockoutEnabledAsync(user, true);
            if (!enabled.Succeeded) return enabled;
        }

        // Npgsql only writes UTC offsets to timestamptz.
        return await _userManager.SetLockoutEndDateAsync(user, until.ToUniversalTime());
    }

    public async Task<IdentityResult> UnlockAsync(ApplicationUser user)
    {
        var cleared = await _userManager.SetLockoutEndDateAsync(user, null);
        if (!cleared.Succeeded) return cleared;
        return await _userManager.ResetAccessFailedCountAsync(user);
    }

    private string BuildFrontendLink(string path, string userId, string encodedToken)
    {
        var baseUrl = (_config["Frontend:BaseUrl"] ?? "https://www.hodrac.com").TrimEnd('/');
        return $"{baseUrl}/{path}?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(encodedToken)}";
    }

    // Identity tokens contain '+', '/', '=' — Base64Url-encode for safe use in links.
    private static string EncodeToken(string token)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public async Task UpdateAvatarAsync(
        string userId, string avatarUrl, CancellationToken ct = default)
        => await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.AvatarUrl, avatarUrl), ct);

    /// <summary>
    /// Used by CollaborationController to resolve an email address to a UserId Guid
    /// when adding a collaborator by email — replaces the Guid.NewGuid() placeholder.
    /// </summary>
    public async Task<Guid?> ResolveUserIdByEmailAsync(
        string email, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null) return null;
        return Guid.TryParse(user.Id, out var id) ? id : null;
    }

    public async Task<NewsLetterEmail?> CreateNewsLetterEmail(NewsLetterEmail newsLetterEmail)
    {
        var exitingUser = await _db.NewsLetterEmails.FirstOrDefaultAsync(u =>
                u.UserEmail == newsLetterEmail.UserEmail
            );
        if (exitingUser == null)
        {
            var newEmail = new NewsLetterEmail { UserEmail = newsLetterEmail.UserEmail };
            await _db.NewsLetterEmails.AddAsync(newEmail);
            await _db.SaveChangesAsync();
            return newEmail;
        }
        return null;
    }
}
