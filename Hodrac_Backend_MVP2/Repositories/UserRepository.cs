using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Data;

namespace Hodrac_Backend_MVP2.Repositories;

// ─── Interface ────────────────────────────────────────────────────────────────



// ─── Implementation ───────────────────────────────────────────────────────────

public class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HodracDbContext _db;

    public UserRepository(UserManager<ApplicationUser> userManager, HodracDbContext db)
    {
        _userManager = userManager;
        _db          = db;
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
        => await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                u => u.LastLoginAt, DateTimeOffset.UtcNow), ct);

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
}
