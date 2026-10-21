using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.Identity;

namespace Hodrac_Backend_MVP2.Interfaces
{
    public interface IUserRepository
    {
        Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken ct = default);
        Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken ct = default);

        /// <summary>
        /// Returns the ApplicationUser whose Id matches the Guid used in Postgres FK columns.
        /// Bridges the Guid FK world (Postgres) with the string Id world (ASP.NET Core Identity).
        /// </summary>
        Task<ApplicationUser?> GetByGuidAsync(Guid userId, CancellationToken ct = default);

        Task<IdentityResult> CreateAsync(ApplicationUser user, string password);
        Task<NewsLetterEmail?> CreateNewsLetterEmail(NewsLetterEmail newsLetterEmail);
        Task<IdentityResult> UpdateAsync(ApplicationUser user);
        Task<bool> CheckPasswordAsync(ApplicationUser user, string password);

        Task MarkOnboardingCompleteAsync(string userId, CancellationToken ct = default);
        Task UpdateLastLoginAsync(string userId, CancellationToken ct = default);
        Task UpdateAvatarAsync(string userId, string avatarUrl, CancellationToken ct = default);

        /// <summary>Stamps LastActiveAt = now (login / token refresh). Used by the email digest.</summary>
        Task UpdateLastActiveAsync(string userId, CancellationToken ct = default);
        Task SetNotificationEmailOptInAsync(string userId, bool optIn, CancellationToken ct = default);

        // ── Identity token flows (password reset / email confirmation / change password) ──
        Task<string> GeneratePasswordResetTokenAsync(ApplicationUser user);
        Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword);
        Task<string> GenerateEmailConfirmationTokenAsync(ApplicationUser user);
        Task<IdentityResult> ConfirmEmailAsync(ApplicationUser user, string token);
        Task<IdentityResult> ChangePasswordAsync(ApplicationUser user, string currentPassword, string newPassword);

        /// <summary>
        /// Generates a confirmation token and emails the confirm-email link.
        /// Never throws — returns false if the user has no email or sending failed.
        /// Shared by POST /api/auth/register, /api/auth/resend-confirmation and the admin resend.
        /// </summary>
        Task<bool> TrySendConfirmationEmailAsync(ApplicationUser user);

        // ── Lockout (Identity: LockoutEnabled + LockoutEnd + AccessFailedCount) ──
        Task<bool> IsLockedOutAsync(ApplicationUser user);

        /// <summary>Records a failed password attempt; Identity locks the account after MaxFailedAccessAttempts.</summary>
        Task<IdentityResult> AccessFailedAsync(ApplicationUser user);
        Task<IdentityResult> ResetAccessFailedCountAsync(ApplicationUser user);

        /// <summary>Admin lock: enables lockout for the user and sets LockoutEnd (UTC).</summary>
        Task<IdentityResult> LockAsync(ApplicationUser user, DateTimeOffset until);

        /// <summary>Admin unlock: clears LockoutEnd and the failed-attempt count.</summary>
        Task<IdentityResult> UnlockAsync(ApplicationUser user);

        /// <summary>
        /// Looks up a user by email and returns their Guid (for collaboration invites).
        /// Returns null if no user has that email.
        /// </summary>
        Task<Guid?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default);
    }
}
