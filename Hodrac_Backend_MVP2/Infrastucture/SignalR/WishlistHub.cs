using Microsoft.AspNetCore.SignalR;
using Hodrac_Backend_MVP2.Data;
using Microsoft.EntityFrameworkCore;
namespace Hodrac_Backend_MVP2.Infrastructure.SignalR;

// ─── Collaboration Hub ────────────────────────────────────────────────────────

/// <summary>
/// Real-time SignalR hub for wishlist collaboration.
/// Clients join a group identified by wishlistId.
/// Server-push only: controllers broadcast "WishlistUpdated" / "ExploreUpdated"
/// through IHubContext&lt;WishlistHub&gt; after a successful write. Clients cannot
/// broadcast through this hub.
/// The hub itself allows anonymous connections (public templates are viewable
/// logged out); joining a private wishlist's group requires the caller to be its
/// owner or a collaborator, which needs an access_token on the connection.
/// </summary>
public class WishlistHub : Hub
{
    private readonly HodracDbContext _db;

    public WishlistHub(HodracDbContext db) => _db = db;

    public async Task JoinWishlist(Guid wishlistId)
    {
        if (!await CanViewAsync(wishlistId))
            throw new HubException("You do not have access to this wishlist.");

        await Groups.AddToGroupAsync(Context.ConnectionId, wishlistId.ToString());
    }

    public async Task LeaveWishlist(Guid wishlistId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, wishlistId.ToString());

    // The "explore" group only carries public template updates, so it stays open.
    public async Task JoinExploreWishlist()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "explore");

    public async Task LeaveExploreWishlist()
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "explore");

    /// <summary>Public template, or the caller is the owner or a collaborator.</summary>
    private async Task<bool> CanViewAsync(Guid wishlistId)
    {
        var wishlist = await _db.Wishlists
            .AsNoTracking()
            .Where(w => w.WishlistId == wishlistId)
            .Select(w => new { w.IsTemplate, w.OwnerUserId })
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        if (wishlist is null) return false;
        if (wishlist.IsTemplate) return true;

        if (!Guid.TryParse(Context.UserIdentifier, out var userId)) return false;
        if (wishlist.OwnerUserId == userId) return true;

        return await _db.WishlistCollaborators
            .AnyAsync(c => c.WishlistId == wishlistId && c.UserId == userId, Context.ConnectionAborted);
    }
}