using Hodrac.DTOs;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Manages content creators and their attribution to wishlists.
///
/// Endpoints:
///   GET    /api/creators                            — list all creators (public)
///   GET    /api/creators/{id}                       — get one creator (public)
///   POST   /api/creators                            — register a new creator (auth required)
///   PATCH  /api/creators/{id}                       — update creator profile (auth required)
///
///   GET    /api/wishlists/{wishlistId}/attributions          — get all attributions for a wishlist (public)
///   POST   /api/wishlists/{wishlistId}/attributions          — attach a creator (auth required)
///   PATCH  /api/wishlists/{wishlistId}/attributions/{id}     — update an attribution (auth required)
///   DELETE /api/wishlists/{wishlistId}/attributions/{id}     — soft-delete (set IsActive=false) (auth required)
/// </summary>
[ApiController]
public class CreatorController : ControllerBase
{
    private readonly HodracDbContext      _db;
    private readonly ICurrentUserService  _currentUser;

    public CreatorController(HodracDbContext db, ICurrentUserService currentUser)
    {
        _db          = db;
        _currentUser = currentUser;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CREATOR CRUD
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>List all registered creators, ordered by display name.</summary>
    [HttpGet("api/creators")]
    [AllowAnonymous]
    public async Task<IActionResult> ListCreators(CancellationToken ct)
    {
        var creators = await _db.Creators
            .OrderBy(c => c.DisplayName)
            .Select(c => ToDto(c))
            .ToListAsync(ct);

        return Ok(creators);
    }

    /// <summary>Get a single creator by ID.</summary>
    [HttpGet("api/creators/{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCreator(Guid id, CancellationToken ct)
    {
        var creator = await _db.Creators.FindAsync(new object[] { id }, ct);
        if (creator is null) return NotFound(new { error = "Creator not found." });
        return Ok(ToDto(creator));
    }

    /// <summary>
    /// Register a new creator. Any authenticated user can register a creator
    /// (i.e. the Hodrac admin team adds them before linking to a wishlist).
    /// </summary>
    [HttpPost("api/creators")]
    [Authorize]
    public async Task<IActionResult> CreateCreator(
        [FromBody] CreateCreatorRequest req, CancellationToken ct)
    {
        // Prevent duplicate platform+handle combinations
        var exists = await _db.Creators.AnyAsync(
            c => c.PlatformName == req.PlatformName && c.Handle == req.Handle, ct);

        if (exists)
            return Conflict(new { error = $"A creator with handle '{req.Handle}' on {req.PlatformName} already exists." });

        var creator = new Creator
        {
            CreatorId    = Guid.NewGuid(),
            DisplayName  = req.DisplayName,
            Handle       = req.Handle,
            PlatformName = req.PlatformName,
            ProfileUrl   = req.ProfileUrl,
            AvatarUrl    = req.AvatarUrl,
            Bio          = req.Bio,
            ContactEmail = req.ContactEmail,
            IsVerified   = false,
            CreatedAt    = DateTimeOffset.UtcNow,
        };

        _db.Creators.Add(creator);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetCreator), new { id = creator.CreatorId }, ToDto(creator));
    }

    /// <summary>Update a creator's profile fields. Partial updates supported.</summary>
    [HttpPatch("api/creators/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateCreator(
        Guid id, [FromBody] UpdateCreatorRequest req, CancellationToken ct)
    {
        var creator = await _db.Creators.FindAsync(new object[] { id }, ct);
        if (creator is null) return NotFound(new { error = "Creator not found." });

        if (req.DisplayName  is not null) creator.DisplayName  = req.DisplayName;
        if (req.Handle       is not null) creator.Handle       = req.Handle;
        if (req.PlatformName is not null) creator.PlatformName = req.PlatformName;
        if (req.ProfileUrl   is not null) creator.ProfileUrl   = req.ProfileUrl;
        if (req.AvatarUrl    is not null) creator.AvatarUrl    = req.AvatarUrl;
        if (req.Bio          is not null) creator.Bio          = req.Bio;
        if (req.ContactEmail is not null) creator.ContactEmail = req.ContactEmail;
        if (req.IsVerified   is not null) creator.IsVerified   = req.IsVerified.Value;

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(creator));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WISHLIST CREATOR ATTRIBUTIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Return all active creator attributions for a wishlist.
    /// Public — shown on the wishlist detail page to credit creators.
    /// </summary>
    [HttpGet("api/wishlists/{wishlistId:guid}/attributions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAttributions(Guid wishlistId, CancellationToken ct)
    {
        var wishlistExists = await _db.Wishlists.AnyAsync(w => w.WishlistId == wishlistId, ct);
        if (!wishlistExists) return NotFound(new { error = "Wishlist not found." });

        var attributions = await _db.WishlistCreatorAttributions
            .Where(a => a.WishlistId == wishlistId && a.IsActive)
            .Include(a => a.Creator)
            .OrderBy(a => a.PermissionGrantedAt)
            .Select(a => ToAttributionDto(a))
            .ToListAsync(ct);

        return Ok(attributions);
    }

    /// <summary>
    /// Attach a creator attribution to a wishlist.
    /// The creator must already exist (POST /api/creators first).
    /// The caller must be the wishlist owner or an Editor collaborator.
    /// </summary>
    [HttpPost("api/wishlists/{wishlistId:guid}/attributions")]
    [Authorize]
    public async Task<IActionResult> AttachAttribution(
        Guid wishlistId,
        [FromBody] AttachCreatorAttributionRequest req,
        CancellationToken ct)
    {
        // Verify wishlist exists
        var wishlist = await _db.Wishlists
            .Include(w => w.Collaborators)
            .FirstOrDefaultAsync(w => w.WishlistId == wishlistId, ct);

        if (wishlist is null)
            return NotFound(new { error = "Wishlist not found." });

        // Permission check: owner or editor
        if (!CanEdit(wishlist))
            return Forbid();

        // Verify the creator exists
        var creatorExists = await _db.Creators.AnyAsync(c => c.CreatorId == req.CreatorId, ct);
        if (!creatorExists)
            return NotFound(new { error = "Creator not found. Register the creator first via POST /api/creators." });

        // Prevent duplicate creator per wishlist
        var alreadyLinked = await _db.WishlistCreatorAttributions.AnyAsync(
            a => a.WishlistId == wishlistId && a.CreatorId == req.CreatorId, ct);

        if (alreadyLinked)
            return Conflict(new { error = "This creator is already attributed to this wishlist." });

        var attribution = new WishlistCreatorAttribution
        {
            WishlistCreatorAttributionId = Guid.NewGuid(),
            WishlistId           = wishlistId,
            CreatorId            = req.CreatorId,
            OriginalContentUrl   = req.OriginalContentUrl,
            PermissionType       = req.PermissionType,
            PermissionGrantedAt  = req.PermissionGrantedAt,
            PermissionEvidenceUrl= req.PermissionEvidenceUrl,
            IsActive             = true,
            AttributionNote      = req.AttributionNote,
            CreatedAt            = DateTimeOffset.UtcNow,
        };

        _db.WishlistCreatorAttributions.Add(attribution);
        await _db.SaveChangesAsync(ct);

        // Reload with creator for response DTO
        await _db.Entry(attribution).Reference(a => a.Creator).LoadAsync(ct);

        return CreatedAtAction(
            nameof(GetAttributions),
            new { wishlistId },
            ToAttributionDto(attribution));
    }

    /// <summary>
    /// Update an existing attribution — e.g. adding a permission evidence URL
    /// after the email arrives, or deactivating if the creator revokes permission.
    /// </summary>
    [HttpPatch("api/wishlists/{wishlistId:guid}/attributions/{attributionId:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateAttribution(
        Guid wishlistId, Guid attributionId,
        [FromBody] UpdateCreatorAttributionRequest req,
        CancellationToken ct)
    {
        var attribution = await _db.WishlistCreatorAttributions
            .Include(a => a.Creator)
            .Include(a => a.Wishlist).ThenInclude(w => w.Collaborators)
            .FirstOrDefaultAsync(
                a => a.WishlistCreatorAttributionId == attributionId
                  && a.WishlistId == wishlistId, ct);

        if (attribution is null) return NotFound(new { error = "Attribution not found." });

        if (!CanEdit(attribution.Wishlist))
            return Forbid();

        if (req.OriginalContentUrl    is not null) attribution.OriginalContentUrl    = req.OriginalContentUrl;
        if (req.PermissionType        is not null) attribution.PermissionType        = req.PermissionType;
        if (req.PermissionGrantedAt   is not null) attribution.PermissionGrantedAt   = req.PermissionGrantedAt.Value;
        if (req.PermissionEvidenceUrl is not null) attribution.PermissionEvidenceUrl = req.PermissionEvidenceUrl;
        if (req.IsActive              is not null) attribution.IsActive              = req.IsActive.Value;
        if (req.AttributionNote       is not null) attribution.AttributionNote       = req.AttributionNote;

        await _db.SaveChangesAsync(ct);
        return Ok(ToAttributionDto(attribution));
    }

    /// <summary>
    /// Soft-delete an attribution by setting IsActive = false.
    /// Used when a creator revokes permission. Does not delete the wishlist.
    /// </summary>
    [HttpDelete("api/wishlists/{wishlistId:guid}/attributions/{attributionId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeactivateAttribution(
        Guid wishlistId, Guid attributionId, CancellationToken ct)
    {
        var attribution = await _db.WishlistCreatorAttributions
            .Include(a => a.Wishlist).ThenInclude(w => w.Collaborators)
            .FirstOrDefaultAsync(
                a => a.WishlistCreatorAttributionId == attributionId
                  && a.WishlistId == wishlistId, ct);

        if (attribution is null) return NotFound(new { error = "Attribution not found." });

        if (!CanEdit(attribution.Wishlist))
            return Forbid();

        attribution.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if the calling user is the wishlist owner or has Editor role.
    /// </summary>
    private bool CanEdit(Wishlist wishlist)
    {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty) return false;

        // Template / platform wishlists have no owner — allow any authenticated user to manage
        if (wishlist.OwnerUserId is null) return true;

        if (wishlist.OwnerUserId == userId) return true;

        return wishlist.Collaborators.Any(
            c => c.UserId == userId && c.Role is "Owner" or "Editor");
    }

    private static CreatorDto ToDto(Creator c) => new(
        c.CreatorId,
        c.DisplayName,
        c.Handle,
        c.PlatformName,
        c.ProfileUrl,
        c.AvatarUrl,
        c.Bio,
        c.IsVerified
    );

    private static WishlistCreatorAttributionDto ToAttributionDto(WishlistCreatorAttribution a) => new(
        a.WishlistCreatorAttributionId,
        a.WishlistId,
        ToDto(a.Creator),
        a.OriginalContentUrl,
        a.PermissionType,
        a.PermissionGrantedAt,
        a.PermissionEvidenceUrl,
        a.IsActive,
        a.AttributionNote,
        a.CreatedAt
    );
}
