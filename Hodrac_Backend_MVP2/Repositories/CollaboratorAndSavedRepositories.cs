using Microsoft.EntityFrameworkCore;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Repositories;

// ═══════════════════════════════════════════════════════════════════════════════
// COLLABORATOR REPOSITORY
// ═══════════════════════════════════════════════════════════════════════════════



public class CollaboratorRepository : ICollaboratorRepository
{
    private readonly HodracDbContext _db;

    public CollaboratorRepository(HodracDbContext db) => _db = db;

    public async Task<List<WishlistCollaborator>> GetCollaboratorsAsync(
        Guid wishlistId, CancellationToken ct = default)
        => await _db.WishlistCollaborators
            .AsNoTracking()
            .Where(c => c.WishlistId == wishlistId)
            .OrderBy(c => c.JoinedAt)
            .ToListAsync(ct);

    public async Task<WishlistCollaborator?> GetCollaboratorAsync(
        Guid wishlistId, Guid userId, CancellationToken ct = default)
        => await _db.WishlistCollaborators
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.WishlistId == wishlistId && c.UserId == userId, ct);

    public async Task<string?> GetRoleAsync(
        Guid wishlistId, Guid userId, CancellationToken ct = default)
        => await _db.WishlistCollaborators
            .AsNoTracking()
            .Where(c => c.WishlistId == wishlistId && c.UserId == userId)
            .Select(c => c.Role)
            .FirstOrDefaultAsync(ct);

    public async Task<WishlistCollaborator> AddAsync(
        Guid wishlistId, Guid userId, string email, string role, CancellationToken ct = default)
    {
        var existing = await _db.WishlistCollaborators
            .FirstOrDefaultAsync(c => c.WishlistId == wishlistId && c.UserId == userId, ct);

        if (existing is not null)
            throw new InvalidOperationException("User is already a collaborator on this wishlist.");

        var collaborator = new WishlistCollaborator
        {
            WishlistCollaboratorId = Guid.NewGuid(),
            WishlistId             = wishlistId,
            UserId                 = userId,
            SharedUserEmail        = email,
            Role                   = role,
            JoinedAt               = DateTimeOffset.UtcNow,
        };
        _db.WishlistCollaborators.Add(collaborator);
        await _db.SaveChangesAsync(ct);
        return collaborator;
    }

    public async Task<bool> ChangeRoleAsync(
        Guid wishlistId, Guid userId, string newRole, CancellationToken ct = default)
    {
        var updated = await _db.WishlistCollaborators
            .Where(c => c.WishlistId == wishlistId
                     && c.UserId == userId
                     && c.Role != "Owner")  // Owner role cannot be changed via this path
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Role, newRole), ct);

        return updated > 0;
    }

    public async Task<bool> RemoveAsync(
        Guid wishlistId, Guid userId, CancellationToken ct = default)
    {
        var deleted = await _db.WishlistCollaborators
            .Where(c => c.WishlistId == wishlistId
                     && c.UserId == userId
                     && c.Role != "Owner")  // Owner cannot be removed this way
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    public async Task<List<Wishlist>> GetSharedWishlistsAsync(
        Guid userId, CancellationToken ct = default)
        => await _db.Wishlists
            .AsNoTracking()
            .Where(w => !w.IsTemplate &&
                        w.Collaborators.Any(c => c.UserId == userId && c.Role != "Owner"))
            .OrderByDescending(w => w.LastInteractedAt ?? w.CreatedAt)
            .ToListAsync(ct);
}

// ═══════════════════════════════════════════════════════════════════════════════
// SAVED CONTENT REPOSITORY
// ═══════════════════════════════════════════════════════════════════════════════



public class SavedContentRepository : ISavedContentRepository
{
    private readonly HodracDbContext _db;

    public SavedContentRepository(HodracDbContext db) => _db = db;

    public async Task<List<Wishlist>> GetSavedWishlistsAsync(
        Guid userId, CancellationToken ct = default)
        => await _db.SavedWishlists
            .AsNoTracking()
            .Where(sw => sw.UserId == userId)
            .OrderByDescending(sw => sw.SavedAt)
            .Select(sw => sw.Wishlist)
            .ToListAsync(ct);

    public async Task<List<Destination>> GetSavedDestinationsAsync(
        Guid userId, CancellationToken ct = default)
        => await _db.SavedDestinations
    .AsNoTracking()
    .Where(sd => sd.UserId == userId)
    .Include(sd => sd.Destination)
        .ThenInclude(d => d.Country)
    .Include(sd => sd.Destination)
        .ThenInclude(d => d.DestinationTags)
            .ThenInclude(dt => dt.Tag)
    .Include(sd => sd.Destination)
        .ThenInclude(d => d.DestinationCategories)
            .ThenInclude(dc => dc.Category)
    .Include(sd => sd.Destination)
        .ThenInclude(d => d.Images)
    .OrderByDescending(sd => sd.SavedAt)
    .Select(sd => sd.Destination)
    .ToListAsync(ct);

    public async Task<bool> SaveDestinationAsync(
        Guid destinationId, Guid userId, CancellationToken ct = default)
    {
        var alreadySaved = await _db.SavedDestinations
            .AnyAsync(sd => sd.DestinationId == destinationId && sd.UserId == userId, ct);
        if (alreadySaved) return false;

        _db.SavedDestinations.Add(new SavedDestination
        {
            SavedDestinationId = Guid.NewGuid(),
            UserId             = userId,
            DestinationId      = destinationId,
            SavedAt            = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UnsaveDestinationAsync(
        Guid destinationId, Guid userId, CancellationToken ct = default)
    {
        var saved = await _db.SavedDestinations
            .FirstOrDefaultAsync(sd => sd.DestinationId == destinationId && sd.UserId == userId, ct);
        if (saved is null) return false;

        _db.SavedDestinations.Remove(saved);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IsDestinationSavedAsync(
        Guid destinationId, Guid userId, CancellationToken ct = default)
        => await _db.SavedDestinations
            .AnyAsync(sd => sd.DestinationId == destinationId && sd.UserId == userId, ct);
}
