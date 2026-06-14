using Hodrac_Backend_MVP2.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Read-only lookup data used to populate filter UIs on the explore page.
///
/// GET /api/tags                        — all tags (filter chips)
/// GET /api/categories                  — all categories (filter chips)
/// GET /api/countries                   — all countries with destination counts
/// GET /api/countries/{id}/cities       — cities scoped to a country
/// GET /api/persona-types               — distinct PeopleType values from wishlists
/// GET /api/filter-options              — everything above in one call (explore page bootstrap)
/// </summary>
[ApiController]
public class LookupController : ControllerBase
{
    private readonly HodracDbContext _db;

    public LookupController(HodracDbContext db) => _db = db;

    // ── GET /api/tags ─────────────────────────────────────────────────────────
    // Used for: filter chips on explore destinations/wishlists page.
    // Cached aggressively — tags change infrequently.

    [HttpGet("api/tags")]
    [ResponseCache(Duration = 300)]   // 5-minute cache
    public async Task<IActionResult> GetTags(CancellationToken ct = default)
    {
        var tags = await _db.Tags
            .AsNoTracking()
            .OrderBy(t => t.TagName)
            .Select(t => new TagDto(t.TagId, t.Key, t.TagName, t.TargetPersonaType))
            .ToListAsync(ct);

        return Ok(tags);
    }

    // ── GET /api/categories ───────────────────────────────────────────────────
    // Used for: category filter panel on explore page.
    // ColorHex and IconName drive the visual filter chip rendering.

    [HttpGet("api/categories")]
    [ResponseCache(Duration = 300)]
    public async Task<IActionResult> GetCategories(CancellationToken ct = default)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.CategoryName)
            .Select(c => new CategoryDto(
                c.CategoryId,
                c.Key,
                c.CategoryName,
                c.CategoryDescription,
                c.IconName,
                c.ColorHex))
            .ToListAsync(ct);

        return Ok(categories);
    }

    // ── GET /api/countries ────────────────────────────────────────────────────
    // Used for: country filter dropdown / map on explore page.
    // Returns flag emoji + destination count so the frontend can show
    // "Japan 🇯🇵 — 24 destinations" without a second request.

    [HttpGet("api/countries")]
    [ResponseCache(Duration = 300)]
    public async Task<IActionResult> GetCountries(CancellationToken ct = default)
    {
        var countries = await _db.Countries
            .AsNoTracking()
            .OrderBy(c => c.CountryName)
            .Select(c => new CountryDto(
                c.CountryId,
                c.CountryName,
                c.Continent,
                c.CountryFlagEmoji,
                c.GlobalHeroImage,
                c.Destinations.Count))
            .ToListAsync(ct);

        return Ok(countries);
    }

    // ── GET /api/countries/{id}/cities ────────────────────────────────────────
    // Used for: city picker in wishlist edit / itinerary day builder.
    // Scoped to a country so the dropdown doesn't list every city globally.

    [HttpGet("api/countries/{id:guid}/cities")]
    public async Task<IActionResult> GetCitiesByCountry(
        Guid id, CancellationToken ct = default)
    {
        var exists = await _db.Countries.AnyAsync(c => c.CountryId == id, ct);
        if (!exists) return NotFound("Country not found.");

        var cities = await _db.Cities
            .AsNoTracking()
            .Where(c => c.CountryId == id)
            .OrderBy(c => c.CityName)
            .Select(c => new CityDto(
                c.CityId,
                c.CityName,
                c.CityDescription,
                c.Latitude,
                c.Longitude))
            .ToListAsync(ct);

        return Ok(cities);
    }

    // ── GET /api/persona-types ────────────────────────────────────────────────
    // Used for: "Traveling as" filter chip group on the explore page.
    // Derived from distinct PeopleType values on platform template wishlists
    // so the list stays in sync with actual content rather than a hardcoded enum.

    [HttpGet("api/persona-types")]
    [ResponseCache(Duration = 600)]
    public async Task<IActionResult> GetPersonaTypes(CancellationToken ct = default)
    {
        var types = await _db.Wishlists
            .AsNoTracking()
            .Where(w => w.IsTemplate && !string.IsNullOrEmpty(w.PeopleType))
            .Select(w => w.PeopleType)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(ct);

        return Ok(types);
    }

    // ── GET /api/filter-options ───────────────────────────────────────────────
    // Single aggregated bootstrap call for the explore page.
    // The frontend fires ONE request on page load and gets everything needed
    // to render all filter panels: tags, categories, countries, persona types,
    // price range bounds, luxury rating range, safety level range.
    //
    // Avoids 5 sequential requests that would stall the filter panel render.

    [HttpGet("api/filter-options")]
    [ResponseCache(Duration = 300)]
    public async Task<IActionResult> GetFilterOptions(CancellationToken ct = default)
    {
        // Queries run sequentially on the same DbContext instance.
        //
        // EF Core's DbContext is NOT thread-safe — running multiple async queries
        // concurrently via Task.WhenAll on a shared _db instance causes:
        //   "A second operation was started on this context instance before a
        //    previous operation completed."
        //
        // For a bootstrap endpoint cached for 5 minutes, sequential execution
        // is correct and adds negligible latency (~5-10ms total for six simple queries).
        // Use multiple DbContext instances (via IDbContextFactory) only if you
        // have a genuine need for parallel DB access on this endpoint.

        var tags = await _db.Tags
            .AsNoTracking()
            .OrderBy(t => t.TagName)
            .Select(t => new TagDto(t.TagId, t.Key, t.TagName, t.TargetPersonaType))
            .ToListAsync(ct);

        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.CategoryName)
            .Select(c => new CategoryDto(
                c.CategoryId, c.Key, c.CategoryName,
                c.CategoryDescription, c.IconName, c.ColorHex))
            .ToListAsync(ct);

        var countries = await _db.Countries
            .AsNoTracking()
            .OrderBy(c => c.CountryName)
            .Select(c => new CountryDto(
                c.CountryId, c.CountryName, c.Continent,
                c.CountryFlagEmoji, c.GlobalHeroImage,
                c.Destinations.Count))
            .ToListAsync(ct);

        var personaTypes = await _db.Wishlists
            .AsNoTracking()
            .Where(w => w.IsTemplate && !string.IsNullOrEmpty(w.PeopleType))
            .Select(w => w.PeopleType)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(ct);

        var priceStats = await _db.Destinations
            .AsNoTracking()
            .Where(d => d.AverageCostPerDay > 0)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MinCostPerDay = g.Min(d => d.AverageCostPerDay),
                MaxCostPerDay = g.Max(d => d.AverageCostPerDay),
            })
            .FirstOrDefaultAsync(ct);

        var wishlistPrice = await _db.Wishlists
            .AsNoTracking()
            .Where(w => w.IsTemplate && w.BasePricePerPerson > 0)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MinPrice = g.Min(w => w.BasePricePerPerson),
                MaxPrice = g.Max(w => w.BasePricePerPerson),
            })
            .FirstOrDefaultAsync(ct);

        return Ok(new FilterOptionsDto(
            Tags: tags,
            Categories: categories,
            Countries: countries,
            PersonaTypes: personaTypes,

            // Destination filters
            DestinationPriceRange: new PriceRangeDto(
                priceStats?.MinCostPerDay ?? 0,
                priceStats?.MaxCostPerDay ?? 1000),
            LuxuryRatings: new RangeDto(1, 5),
            SafetyLevels: new RangeDto(1, 5),
            AccessibilityTypes: new[] { "Train", "Boat Only", "Flight", "Road" },

            // Wishlist filters
            WishlistPriceRange: new PriceRangeDto(
                wishlistPrice?.MinPrice ?? 0,
                wishlistPrice?.MaxPrice ?? 10000),
            TripDurations: new RangeDto(1, 30),

            // Sort options exposed to the frontend for both explore pages
            DestinationSortOptions: new[]
            {
                new SortOptionDto("popularity",     "Most Popular"),
                new SortOptionDto("cost_asc",       "Price: Low to High"),
                new SortOptionDto("cost_desc",      "Price: High to Low"),
                new SortOptionDto("safety",         "Safest First"),
                new SortOptionDto("luxury",         "Most Luxurious"),
                new SortOptionDto("family_friendly","Family Friendly"),
                new SortOptionDto("adventure",      "Most Adventurous"),
            },
            WishlistSortOptions: new[]
            {
                new SortOptionDto("saves",     "Most Saved"),
                new SortOptionDto("price_asc", "Price: Low to High"),
                new SortOptionDto("price_desc","Price: High to Low"),
                new SortOptionDto("duration",  "Trip Length"),
                new SortOptionDto("newest",    "Newest"),
            }
        ));
    }
}

// ─── Lookup DTOs ──────────────────────────────────────────────────────────────

public record TagDto(
    Guid   TagId,
    string Key,
    string TagName,
    string TargetPersonaType
);

public record CategoryDto(
    Guid   CategoryId,
    string Key,
    string CategoryName,
    string CategoryDescription,
    string IconName,
    string ColorHex
);

public record CountryDto(
    Guid   CountryId,
    string CountryName,
    string Continent,
    string FlagEmoji,
    string HeroImage,
    int    DestinationCount
);

public record CityDto(
    Guid   CityId,
    string CityName,
    string Description,
    double Latitude,
    double Longitude
);

public record PriceRangeDto(decimal Min, decimal Max);
public record RangeDto(int Min, int Max);
public record SortOptionDto(string Value, string Label);

public record FilterOptionsDto(
    List<TagDto>       Tags,
    List<CategoryDto>  Categories,
    List<CountryDto>   Countries,
    List<string>       PersonaTypes,

    // Destination-specific filter bounds
    PriceRangeDto      DestinationPriceRange,
    RangeDto           LuxuryRatings,
    RangeDto           SafetyLevels,
    string[]           AccessibilityTypes,

    // Wishlist-specific filter bounds
    PriceRangeDto      WishlistPriceRange,
    RangeDto           TripDurations,

    // Sort options for both explore pages
    SortOptionDto[]    DestinationSortOptions,
    SortOptionDto[]    WishlistSortOptions
);
