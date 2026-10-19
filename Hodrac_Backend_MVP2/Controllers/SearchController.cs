using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.DTOs.SearchDtos;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SEARCH CONTROLLER
    // GET /api/search?q=...   (counters use the authenticated user only)
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
        private readonly IPartnerService _partners;

        public SearchController(
            SearchService search,
            IDestinationRepository destinations,
            IWishlistRepository wishlists,
            Data.HodracDbContext db,
            ICurrentUserService currentUser,
            IPartnerService partners)
        {
            _search = search;
            _destinations = destinations;
            _wishlists = wishlists;
            _db = db;
            _currentUser = currentUser;
            _partners = partners;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] string q,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest("q parameter is required.");

            // Only the authenticated caller may drive the segmented search counters;
            // a client-supplied user_id query param is deliberately ignored.
            var authenticatedUserId = _currentUser.IsAuthenticated ? _currentUser.UserIdString : null;
            var result = await _search.SearchAsync(q, authenticatedUserId, ct);
            var matchingDestinations = await _destinations.SearchByNameAsync(
                 result.CanonicalPhrase, 12, ct);
            var wishlistItems = await SearchWishlistTemplatesAsync(
                SearchService.Normalize(q), result.CanonicalPhrase, 6, ct);

            return Ok(new SearchResponseDto(
                CanonicalPhrase: result.CanonicalPhrase,
                ClusterId: result.SemanticClusterId,
                Destinations: await _partners.AttachAsync(matchingDestinations.Select(d => new DestinationSummaryDto(
                    d.DestinationId, d.DestinationName,
                    $"{d.DestinationName}, {d.Country?.CountryName}",
                    d.Images.FirstOrDefault()?.ThumbnailUrl ?? string.Empty,
                    d.AverageCostPerDay, d.SafetyLevel, d.LuxuryRating,
                    d.DestinationTags.Select(dt => dt.Tag.TagName).ToList(),
                    d.DestinationCategories.Select(dc => dc.Category.CategoryName).ToList()
                )), ct),
                Wishlists: await _partners.AttachAsync(wishlistItems.Select(item => WishlistsController.MapToCard(item)), ct)
            ));
        }

        /// <summary>
        /// Template wishlists whose name, description, short story, keywords or
        /// destination names contain the query or its canonical phrase (case-insensitive).
        /// Name matches rank first, then by global save count. Empty list if nothing matches.
        /// </summary>
        private async Task<List<Wishlist>> SearchWishlistTemplatesAsync(
            string query, string canonical, int limit, CancellationToken ct)
        {
            var queryPattern = ToContainsPattern(query);
            var canonicalPattern = string.IsNullOrWhiteSpace(canonical)
                ? queryPattern
                : ToContainsPattern(canonical.Trim());

            return await _db.Wishlists
                .AsNoTracking()
                .Where(w => w.IsTemplate)
                .Where(w =>
                    EF.Functions.ILike(w.WishlistName, queryPattern, LikeEscape) ||
                    EF.Functions.ILike(w.WishlistName, canonicalPattern, LikeEscape) ||
                    EF.Functions.ILike(w.WishlistDescription, queryPattern, LikeEscape) ||
                    EF.Functions.ILike(w.WishlistDescription, canonicalPattern, LikeEscape) ||
                    EF.Functions.ILike(w.ShortStory, queryPattern, LikeEscape) ||
                    EF.Functions.ILike(w.ShortStory, canonicalPattern, LikeEscape) ||
                    EF.Functions.ILike(w.RawContentKeywords, queryPattern, LikeEscape) ||
                    EF.Functions.ILike(w.RawContentKeywords, canonicalPattern, LikeEscape) ||
                    w.WishlistDestinations.Any(wd =>
                        EF.Functions.ILike(wd.Destination.DestinationName, queryPattern, LikeEscape) ||
                        EF.Functions.ILike(wd.Destination.DestinationName, canonicalPattern, LikeEscape)))
                .OrderByDescending(w =>
                    EF.Functions.ILike(w.WishlistName, queryPattern, LikeEscape) ||
                    EF.Functions.ILike(w.WishlistName, canonicalPattern, LikeEscape))
                .ThenByDescending(w => w.TotalGlobalSaveCount)
                .Take(limit)
                .ToListAsync(ct);
        }

        private const string LikeEscape = "\\";

        /// <summary>Wraps user input in % wildcards, escaping LIKE metacharacters.</summary>
        private static string ToContainsPattern(string input)
            => "%" + input
                .Replace("\\", "\\\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_") + "%";

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
