using Hodrac_Backend_MVP2.Infrastructure.SignalR;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// GET    /api/user-wishlists           — list all wishlists owned by the caller
/// GET    /api/user-wishlists/{id}      — get one (owner or collaborator)
/// PUT    /api/user-wishlists/{id}      — update (editor or owner); handles 409 on version conflict
/// DELETE /api/user-wishlists/{id}      — delete (owner only)
/// GET    /api/shared-wishlists         — wishlists where caller is Editor or Viewer
/// </summary>
[ApiController]
[Route("api/user-wishlists")]
public class UserWishlistsController : ControllerBase
{
    private readonly IWishlistRepository _wishlists;
    private readonly ICollaboratorRepository _collaborators;
    private readonly ICurrentUserService _currentUser;
    private readonly IHubContext<WishlistHub> _wishlistHub;

    public UserWishlistsController(
        IWishlistRepository wishlists,
        ICollaboratorRepository collaborators,
        ICurrentUserService currentUser,
        IHubContext<WishlistHub> wishlistHub)
    {
        _wishlists     = wishlists;
        _collaborators = collaborators;
        _currentUser   = currentUser;
        _wishlistHub = wishlistHub;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var wishlists = await _wishlists.GetUserWishlistsAsync(_currentUser.UserId, ct);
        return Ok(wishlists.Select(WishlistsController.MapToCard));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var wishlist = await _wishlists.GetUserWishlistByIdAsync(id, _currentUser.UserId, ct);
        if (wishlist is null) return NotFound();
        var snapshot = await _wishlists.GetActiveSnapshotAsync(id, ct);
        return Ok(WishlistsController.MapToDetail(wishlist, _currentUser.UserId, snapshot));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateWishlistRequest request,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var role = await _collaborators.GetRoleAsync(id, _currentUser.UserId, ct);
        if (role is null || role == "Viewer") return Forbid();

        var wishlist = await _wishlists.GetUserWishlistByIdAsync(id, _currentUser.UserId, ct);
        if (wishlist is null) return NotFound();

        if (request.WishlistName is not null)        wishlist.WishlistName        = request.WishlistName;
        if (request.WishlistDescription is not null) wishlist.WishlistDescription = request.WishlistDescription;
        if (request.ShortStory is not null)          wishlist.ShortStory          = request.ShortStory;
        if (request.DefaultTravelersCount.HasValue)  wishlist.DefaultTravelersCount = request.DefaultTravelersCount.Value;
        if (request.GlobalInclusions is not null)
            wishlist.GlobalInclusionsJson = JsonSerializer.Serialize(request.GlobalInclusions);

        wishlist.LastInteractedAt = DateTimeOffset.UtcNow;

        if (request.Xmin is not null)
            _wishlists.SetRowVersion(wishlist, request.Xmin.Value);

        try
        {
            var updated  = await _wishlists.UpdateAsync(wishlist, ct);
            var snapshot = await _wishlists.GetActiveSnapshotAsync(id, ct);
            var dto = WishlistsController.MapToDetail(updated, _currentUser.UserId, snapshot);
            await _wishlistHub.Clients
            .Group(wishlist.WishlistId.ToString())
            .SendAsync(
                "WishlistUpdated",
                dto, ct
            );
            return Ok(dto);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "Conflict: this wishlist was updated by another collaborator. Please refresh and reapply your changes.", code = "CONCURRENT_EDIT" });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        try
        {
            await _wishlists.DeleteUserWishlistAsync(id, _currentUser.UserId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Wishlist not found or you are not the owner.");
        }
    }

    [HttpGet("/api/shared-wishlists")]
    public async Task<IActionResult> GetShared(CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var wishlists = await _collaborators.GetSharedWishlistsAsync(_currentUser.UserId, ct);
        return Ok(wishlists.Select(wishlist => WishlistsController.MapToCard(wishlist)));
    }
}

// ─── Request body for PUT ─────────────────────────────────────────────────────
// All fields are nullable — only provided fields are applied.

public record UpdateWishlistRequest(
    string? WishlistName,
    string? WishlistDescription,
    string? ShortStory,
    int? DefaultTravelersCount,
    List<string>? GlobalInclusions,
    /// <summary>
    /// The xmin value the client received on the last GET of this wishlist.
    /// When provided, EF Core uses it to detect if another collaborator saved
    /// between your load and your save, throwing HTTP 409 Conflict if so.
    /// Pass as a uint (e.g. 1234567). Omit to skip concurrency checking.
    /// </summary>
    uint? Xmin
);
