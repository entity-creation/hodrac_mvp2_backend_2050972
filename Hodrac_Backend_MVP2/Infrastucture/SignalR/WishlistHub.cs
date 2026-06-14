using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
namespace Hodrac_Backend_MVP2.Infrastructure.SignalR;

// ─── Collaboration Hub ────────────────────────────────────────────────────────

/// <summary>
/// Real-time SignalR hub for wishlist collaboration.
/// Clients join a group identified by wishlistId.
/// When any collaborator saves a change, the server broadcasts to all other group members.
/// </summary>
public class WishlistHub : Hub
{
    public async Task JoinWishlist(Guid wishlistId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, wishlistId.ToString());

    public async Task LeaveWishlist(Guid wishlistId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, wishlistId.ToString());

    public async Task JoinExploreWishlist()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "explore");

    public async Task LeaveExploreWishlist()
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "explore");

    /// <summary>
    /// Called by the API layer (not directly by clients) after a successful PUT.
    /// Broadcasts the updated wishlist snapshot to all collaborators except the sender.
    /// </summary>
    public async Task NotifyWishlistUpdated(Guid wishlistId, object updatedSnapshot)
        => await Clients.OthersInGroup(wishlistId.ToString()).SendAsync("WishlistUpdated", updatedSnapshot);

    public async Task NotifyExploreUpdated(object updatedSnapshot)
        => await Clients.OthersInGroup("explore").SendAsync("ExploreUpdated", updatedSnapshot);

}

