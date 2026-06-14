using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.DTOs.SearchDtos;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SEARCH CONTROLLER
    // GET /api/search?q=...&user_id=...
    // GET /api/popular-searches?user_id=...&limit=10
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    [Route("api")]
    public class SearchController : ControllerBase
    {
        private readonly SearchService _search;
        private readonly IDestinationRepository _destinations;
        private readonly IWishlistRepository _wishlists;
        private readonly Data.HodracDbContext _db;
        private readonly ICurrentUserService _currentUser;

        public SearchController(
            SearchService search,
            IDestinationRepository destinations,
            IWishlistRepository wishlists,
            Data.HodracDbContext db,
            ICurrentUserService currentUser)
        {
            _search = search;
            _destinations = destinations;
            _wishlists = wishlists;
            _db = db;
            _currentUser = currentUser;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] string q,
            [FromQuery(Name = "user_id")] string? userId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest("q parameter is required.");

            var resolvedUserId = userId ?? (_currentUser.IsAuthenticated ? _currentUser.UserIdString : null);
            var result = await _search.SearchAsync(q, resolvedUserId, ct);

            var matchingDestinations = await _destinations.GetMatchingUserAsync(
                new List<string> { result.CanonicalPhrase }, null, 12, ct);
            var (wishlistItems, _) = await _wishlists.GetTemplatesAsync(1, 6, ct);

            return Ok(new SearchResponseDto(
                CanonicalPhrase: result.CanonicalPhrase,
                ClusterId: result.SemanticClusterId,
                Destinations: matchingDestinations.Select(d => new DestinationSummaryDto(
                    d.DestinationId, d.DestinationName,
                    $"{d.DestinationName}, {d.Country?.CountryName}",
                    d.Images.FirstOrDefault()?.ThumbnailUrl ?? string.Empty,
                    d.AverageCostPerDay, d.SafetyLevel, d.LuxuryRating,
                    d.DestinationTags.Select(dt => dt.Tag.TagName).ToList(),
                    d.DestinationCategories.Select(dc => dc.Category.CategoryName).ToList()
                )).ToList(),
                Wishlists: wishlistItems.Select(item => WishlistsController.MapToCard(item)).ToList()
            ));
        }

        [HttpGet("popular-searches")]
        public async Task<IActionResult> GetPopularSearches(
            [FromQuery(Name = "user_id")] string? userId,
            [FromQuery] int limit = 10,
            CancellationToken ct = default)
        {
            var results = await _db.AggregatedSearchRegistry
                .AsNoTracking()
                .OrderByDescending(r => r.TotalGlobalSearchCount)
                .Take(limit)
                .Select(r => new PopularSearchDto(r.MasterSearchPhrase, r.TotalGlobalSearchCount))
                .ToListAsync(ct);

            return Ok(results);
        }
    }
}
