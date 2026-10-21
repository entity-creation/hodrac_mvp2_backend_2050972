using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.DTOs.UserDtos;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hodrac_Backend_MVP2.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SAVED CONTROLLER
    // POST   /api/destinations/{id}/save    — bookmark destination
    // DELETE /api/destinations/{id}/save    — remove destination bookmark
    // GET    /api/user/saved                — all saved wishlists + destinations
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    public class SavedController : ControllerBase
    {
        private readonly ISavedContentRepository _saved;
        private readonly IWishlistRepository _wishlists;
        private readonly ICurrentUserService _currentUser;
        private readonly IPartnerService _partners;

        public SavedController(
            ISavedContentRepository saved,
            IWishlistRepository wishlists,
            ICurrentUserService currentUser,
            IPartnerService partners)
        {
            _saved = saved;
            _wishlists = wishlists;
            _currentUser = currentUser;
            _partners = partners;
        }

        // Route attributes here use the full path because this controller has no
        // [Route] prefix — its endpoints span /api/destinations and /api/user namespaces.

        [HttpPost("/api/destinations/{id:guid}/save")]
        public async Task<IActionResult> SaveDestination(Guid id, CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            if (!await _saved.DestinationExistsAsync(id, ct)) return NotFound("Destination not found.");
            var saved = await _saved.SaveDestinationAsync(id, _currentUser.UserId, ct);
            return saved ? Ok(new { message = "Destination saved." })
                         : Conflict("Destination already saved.");
        }

        [HttpDelete("/api/destinations/{id:guid}/save")]
        public async Task<IActionResult> UnsaveDestination(Guid id, CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var removed = await _saved.UnsaveDestinationAsync(id, _currentUser.UserId, ct);
            return removed ? Ok(new { message = "Destination removed from saved." })
                           : NotFound("Destination was not saved.");
        }

        [HttpGet("/api/user/saved")]
        public async Task<IActionResult> GetAllSaved(CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var savedWishlists = await _saved.GetSavedWishlistsAsync(_currentUser.UserId, ct);
            var savedDestinations = await _saved.GetSavedDestinationsAsync(_currentUser.UserId, ct);

            return Ok(new SavedContentDto(
                SavedWishlists: await _partners.AttachAsync(savedWishlists.Select(savedWishlist => WishlistsController.MapToCard(savedWishlist)), ct),
                SavedDestinations: await _partners.AttachAsync(savedDestinations.Select(d => new DestinationSummaryDto(
                    d.DestinationId,
                    d.DestinationName,
                    $"{d.DestinationName}, {d.Country?.CountryName}",
                    d.Images.FirstOrDefault(i => i.ImageType == "Hero")?.ThumbnailUrl
                        ?? d.Images.FirstOrDefault()?.ThumbnailUrl ?? string.Empty,
                    d.AverageCostPerDay,
                    d.SafetyLevel,
                    d.LuxuryRating,
                    d.DestinationTags.Select(dt => dt.Tag.TagName).ToList(),
                    d.DestinationCategories.Select(dc => dc.Category.CategoryName).ToList(),
                    IsFeatured: d.IsCurrentlyFeatured(DateTimeOffset.UtcNow)
                )), ct)
            ));
        }
    }
}
