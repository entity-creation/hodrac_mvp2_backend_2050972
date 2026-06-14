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
        Task<IdentityResult> UpdateAsync(ApplicationUser user);
        Task<bool> CheckPasswordAsync(ApplicationUser user, string password);

        Task MarkOnboardingCompleteAsync(string userId, CancellationToken ct = default);
        Task UpdateLastLoginAsync(string userId, CancellationToken ct = default);
        Task UpdateAvatarAsync(string userId, string avatarUrl, CancellationToken ct = default);

        /// <summary>
        /// Looks up a user by email and returns their Guid (for collaboration invites).
        /// Returns null if no user has that email.
        /// </summary>
        Task<Guid?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default);
    }
}
