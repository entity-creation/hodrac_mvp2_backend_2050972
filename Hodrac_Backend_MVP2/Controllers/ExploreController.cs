using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.DTOs.WishlistDtos;
using Hodrac_Backend_MVP2.Infrastructure.SignalR;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;


namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Dedicated explore endpoints with full filter + sort + pagination support.
/// These are separate from the basic CRUD endpoints in DestinationsController
/// and WishlistsController — the explore page needs compound WHERE clauses
/// that the basic repositories don't expose.
///
/// GET /api/explore/destinations   — filtered, sorted, paginated destination grid
/// GET /api/explore/wishlists      — filtered, sorted, paginated wishlist grid
/// GET /api/explore/destinations/by-country/{countryId} — country-scoped destinations
/// GET /api/wishlists/matching-user — persona-ranked templates (spec endpoint)
/// </summary>
[ApiController]
public class ExploreController : ControllerBase
{
    private readonly HodracDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IHubContext<WishlistHub> _wishlistHub;
    private readonly IPartnerService _partners;
    private readonly IProfileRepository _profiles;
    private readonly ILogger<ExploreController> _logger;

    public ExploreController(HodracDbContext db, ICurrentUserService currentUser,
        IHubContext<WishlistHub> wishlistHub, IPartnerService partners,
        IProfileRepository profiles, ILogger<ExploreController> logger)
    {
        _db          = db;
        _currentUser = currentUser;
        _wishlistHub = wishlistHub;
        _partners    = partners;
        _profiles    = profiles;
        _logger      = logger;
    }

    // ── GET /api/explore/destinations ─────────────────────────────────────────
    // Supports all filter combinations the explore page needs.
    // All filter params are optional — omitting them returns everything.

    [HttpGet("api/explore/destinations")]
    public async Task<IActionResult> ExploreDestinations(
        // Taxonomy filters
        [FromQuery] string?      tags,           // comma-separated tag keys
        [FromQuery] string?      categories,     // comma-separated category keys
        [FromQuery] Guid?        countryId,
        [FromQuery] string?      accessibility,  // "Train" | "Boat Only" | "Flight" | "Road"

        // Score filters
        [FromQuery] int?         minLuxury,
        [FromQuery] int?         maxLuxury,
        [FromQuery] int?         maxSafetyLevel, // lower = safer, so max is the ceiling
        [FromQuery] int?         minFamilyScore,
        [FromQuery] int?         minAdventureScore,

        // Price filter
        [FromQuery] decimal?     minCost,
        [FromQuery] decimal?     maxCost,

        // Sort
        [FromQuery] string?      sort = "popularity",

        // Pagination
        [FromQuery] int          page     = 1,
        [FromQuery] int          pageSize = 12,

        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);

        var tagList      = Split(tags);
        var categoryList = Split(categories);

        var query = _db.Destinations
            .AsNoTracking()
            .Include(d => d.Country)
            .Include(d => d.Images.OrderBy(i => i.DisplayOrder).Take(1))
            .Include(d => d.DestinationTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DestinationCategories).ThenInclude(dc => dc.Category)
            .AsQueryable();

        // ── Filters ───────────────────────────────────────────────────────────

        if (tagList.Count > 0)
            query = query.Where(d => d.DestinationTags.Any(dt => tagList.Contains(dt.Tag.Key)));

        if (categoryList.Count > 0)
            query = query.Where(d => d.DestinationCategories.Any(dc => categoryList.Contains(dc.Category.Key)));

        if (countryId.HasValue)
            query = query.Where(d => d.CountryId == countryId.Value);

        if (!string.IsNullOrWhiteSpace(accessibility))
            query = query.Where(d => d.AccessibilityType == accessibility);

        if (minLuxury.HasValue)     query = query.Where(d => d.LuxuryRating >= minLuxury.Value);
        if (maxLuxury.HasValue)     query = query.Where(d => d.LuxuryRating <= maxLuxury.Value);
        if (maxSafetyLevel.HasValue) query = query.Where(d => d.SafetyLevel <= maxSafetyLevel.Value);
        if (minFamilyScore.HasValue) query = query.Where(d => d.FamilyFriendlyScore >= minFamilyScore.Value);
        if (minAdventureScore.HasValue) query = query.Where(d => d.AdventurePaceScore >= minAdventureScore.Value);
        if (minCost.HasValue)       query = query.Where(d => d.AverageCostPerDay >= minCost.Value);
        if (maxCost.HasValue)       query = query.Where(d => d.AverageCostPerDay <= maxCost.Value);

        // ── Sort ──────────────────────────────────────────────────────────────

        query = sort switch
        {
            "cost_asc"       => query.OrderBy(d => d.AverageCostPerDay),
            "cost_desc"      => query.OrderByDescending(d => d.AverageCostPerDay),
            "safety"         => query.OrderBy(d => d.SafetyLevel),
            "luxury"         => query.OrderByDescending(d => d.LuxuryRating),
            "family_friendly"=> query.OrderByDescending(d => d.FamilyFriendlyScore),
            "adventure"      => query.OrderByDescending(d => d.AdventurePaceScore),
            _                => query.OrderByDescending(d => d.SearchHitCount),  // popularity
        };

        // ── Paginate ──────────────────────────────────────────────────────────

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<DestinationSummaryDto>(
            Page      : page,
            PageSize  : pageSize,
            TotalCount: total,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            Items     : await _partners.AttachAsync(items.Select(item => MapDestinationToSummary(item)), ct)
        ));
    }

    // ── GET /api/explore/wishlists ────────────────────────────────────────────

    [HttpGet("api/explore/wishlists")]
    public async Task<IActionResult> ExploreWishlists(
        // Taxonomy filters
        [FromQuery] string?  vibeTags,       // comma-separated psychological vibe tags
        [FromQuery] string?  personaType,    // exact PeopleType match
        [FromQuery] string?  personaTypes,   // comma-separated multiple PeopleTypes

        // Price filters
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,

        // Trip length
        [FromQuery] int?     minDays,
        [FromQuery] int?     maxDays,

        // Sort
        [FromQuery] string?  sort = "saves",

        // Pagination
        [FromQuery] int      page     = 1,
        [FromQuery] int      pageSize = 12,

        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);

        var vibeTagList   = Split(vibeTags);
        var personaList   = Split(personaTypes);

        // If single personaType param provided, merge it in
        if (!string.IsNullOrWhiteSpace(personaType) && !personaList.Contains(personaType))
            personaList.Add(personaType);

        var query = _db.Wishlists
            .AsNoTracking()
            .Where(w => w.IsTemplate)
            .AsQueryable();

        // ── Filters ───────────────────────────────────────────────────────────

        if (vibeTagList.Count > 0)
        {
            // PsychologicalVibeTagsJson is a jsonb array of strings. The jsonb ?| operator
            // ("does any of these strings exist as a top-level array element") gives an
            // exact, case-sensitive tag match OR'd across the requested tags, and runs
            // entirely in Postgres: "PsychologicalVibeTagsJson" ?| @vibeTags
            var vibeTagArray = vibeTagList.ToArray();
            query = query.Where(w => EF.Functions.JsonExistAny(w.PsychologicalVibeTagsJson, vibeTagArray));
        }

        if (personaList.Count > 0)
            query = query.Where(w => personaList.Contains(w.PeopleType));

        if (minPrice.HasValue) query = query.Where(w => w.BasePricePerPerson >= minPrice.Value);
        if (maxPrice.HasValue) query = query.Where(w => w.BasePricePerPerson <= maxPrice.Value);
        if (minDays.HasValue)  query = query.Where(w => w.TotalDays >= minDays.Value);
        if (maxDays.HasValue)  query = query.Where(w => w.TotalDays <= maxDays.Value);

        // ── Sort ──────────────────────────────────────────────────────────────

        query = sort switch
        {
            "price_asc"  => query.OrderBy(w => w.BasePricePerPerson),
            "price_desc" => query.OrderByDescending(w => w.BasePricePerPerson),
            "duration"   => query.OrderBy(w => w.TotalDays),
            "newest"     => query.OrderByDescending(w => w.CreatedAt),
            _            => query.OrderByDescending(w => w.TotalGlobalSaveCount),  // saves
        };

        // ── Paginate ──────────────────────────────────────────────────────────

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // No SignalR broadcast here: this is a read. "ExploreUpdated" must only be
        // sent by endpoints that actually change explore data.
        return Ok(new PagedResult<WishlistCardDto>(
            Page      : page,
            PageSize  : pageSize,
            TotalCount: total,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            Items     : await _partners.AttachAsync(items.Select(item => WishlistsController.MapToCard(item)), ct)
        ));
    }

    // ── GET /api/explore/destinations/by-country/{countryId} ─────────────────
    // Country-scoped destination grid with full filter/sort support.
    // Used by the country detail page and the country filter shortcut.

    [HttpGet("api/explore/destinations/by-country/{countryId:guid}")]
    public async Task<IActionResult> DestinationsByCountry(
        Guid     countryId,
        [FromQuery] string? tags,
        [FromQuery] string? sort     = "popularity",
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 12,
        CancellationToken   ct       = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var tagList = Split(tags);

        var query = _db.Destinations
            .AsNoTracking()
            .Include(d => d.Country)
            .Include(d => d.Images.OrderBy(i => i.DisplayOrder).Take(1))
            .Include(d => d.DestinationTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DestinationCategories).ThenInclude(dc => dc.Category)
            .Where(d => d.CountryId == countryId);

        if (tagList.Count > 0)
            query = query.Where(d => d.DestinationTags.Any(dt => tagList.Contains(dt.Tag.Key)));

        query = sort switch
        {
            "cost_asc"  => query.OrderBy(d => d.AverageCostPerDay),
            "cost_desc" => query.OrderByDescending(d => d.AverageCostPerDay),
            _           => query.OrderByDescending(d => d.SearchHitCount),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<DestinationSummaryDto>(
            Page      : page,
            PageSize  : pageSize,
            TotalCount: total,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            Items     : await _partners.AttachAsync(items.Select(item => MapDestinationToSummary(item)), ct)
        ));
    }

    // ── GET /api/wishlists/matching-user ─────────────────────────────────────
    // Spec endpoint: persona-ranked wishlist templates for the JWT caller.
    // Ranks templates by overlap between the caller's Mongo profile tags
    // (interaction-history tags + onboarding answers) and the wishlist's vibe tags.
    // Anonymous / no profile / no matches → top-saved templates. Any `user_id`
    // query param is ignored: the user always comes from the token.

    [HttpGet("api/wishlists/matching-user")]
    public async Task<IActionResult> GetMatchingUserWishlists(
        [FromQuery] int limit = 12,
        CancellationToken ct  = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        var profile     = await ProfileTagSignals.TryLoadProfileAsync(_profiles, _currentUser, _logger);
        var profileTags = ProfileTagSignals.ProfileTags(profile);

        var ranked = new List<Wishlist>();
        if (profileTags.Count > 0)
        {
            // Postgres pre-filter: templates sharing at least one exact vibe tag
            // (jsonb ?| operator), capped so we never pull the whole table.
            var tagArray   = profileTags.ToArray();
            var candidates = await _db.Wishlists
                .AsNoTracking()
                .Where(w => w.IsTemplate
                         && EF.Functions.JsonExistAny(w.PsychologicalVibeTagsJson, tagArray))
                .OrderByDescending(w => w.TotalGlobalSaveCount)
                .Take(200)
                .ToListAsync(ct);

            ranked = candidates
                .Select(w => (Wishlist: w, Score: ProfileTagSignals.Overlap(
                    ProfileTagSignals.ParseTagJson(w.PsychologicalVibeTagsJson), profileTags)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Wishlist.TotalGlobalSaveCount)
                .Take(limit)
                .Select(x => x.Wishlist)
                .ToList();
        }

        // Generic fallback / top-up: most-saved templates not already included.
        if (ranked.Count < limit)
        {
            var have = ranked.Select(w => w.WishlistId).ToList();
            var fill = await _db.Wishlists
                .AsNoTracking()
                .Where(w => w.IsTemplate && !have.Contains(w.WishlistId))
                .OrderByDescending(w => w.TotalGlobalSaveCount)
                .Take(limit - ranked.Count)
                .ToListAsync(ct);
            ranked.AddRange(fill);
        }

        return Ok(await _partners.AttachAsync(ranked.Select(WishlistsController.MapToCard), ct));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<string> Split(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .ToList();

    private static DestinationSummaryDto MapDestinationToSummary(Destination d) => new(
        DestinationId    : d.DestinationId,
        DestinationName  : d.DestinationName,
        Location         : $"{d.DestinationName}, {d.Country?.CountryName}",
        ThumbnailUrl     : d.Images.FirstOrDefault(i => i.ImageType == "Hero")?.ThumbnailUrl
                        ?? d.Images.FirstOrDefault()?.ThumbnailUrl
                        ?? string.Empty,
        AverageCostPerDay: d.AverageCostPerDay,
        SafetyLevel      : d.SafetyLevel,
        LuxuryRating     : d.LuxuryRating,
        Tags             : d.DestinationTags.Select(dt => dt.Tag.TagName).ToList(),
        Categories       : d.DestinationCategories.Select(dc => dc.Category.CategoryName).ToList()
    );
}

// ─── Shared paged result wrapper ─────────────────────────────────────────────
// Used by all paginated endpoints. Frontend reads Page, TotalPages, Items.

public record PagedResult<T>(
    int        Page,
    int        PageSize,
    int        TotalCount,
    int        TotalPages,
    List<T>    Items
);
