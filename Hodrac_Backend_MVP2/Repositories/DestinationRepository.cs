using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Repositories;

// ─── Implementation ───────────────────────────────────────────────────────────

public class DestinationRepository : IDestinationRepository
{
    private readonly HodracDbContext _db;

    public DestinationRepository(HodracDbContext db) => _db = db;

    // ── Base query with all navigation needed for DTOs ────────────────────────

    private IQueryable<Destination> BaseQuery() =>
        _db.Destinations
           .AsSplitQuery()
           .Include(d => d.Country)
           .Include(d => d.Images.OrderBy(i => i.DisplayOrder))
           .Include(d => d.DestinationTags).ThenInclude(dt => dt.Tag)
           .Include(d => d.DestinationCategories).ThenInclude(dc => dc.Category)
           .Include(d => d.DestinationLanguages).ThenInclude(dl => dl.Language)
           .Include(d => d.DestinationCurrencies).ThenInclude(dc => dc.Currency)
           .Include(d => d.DestinationCities).ThenInclude(dc => dc.City)
           .AsNoTracking();

    // ── Paginated list ────────────────────────────────────────────────────────

    public async Task<(List<Destination> Items, int TotalCount)> GetAllAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = BaseQuery().OrderByDescending(d => d.SearchHitCount);
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    // ── Single destination (full detail) ──────────────────────────────────────

    public async Task<Destination?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await BaseQuery().FirstOrDefaultAsync(d => d.DestinationId == id, ct);

    // ── Phonetic fuzzy lookup — used in step 1 of search pipeline ─────────────
    // Returns destinations whose MetaphoneCode or DoubleMetaphonePrimary matches.
    // The search service normalizes the query to a Metaphone code before calling this.

    public async Task<List<Destination>> GetByPhoneticCodeAsync(
        string metaphoneCode, CancellationToken ct = default)
        => await BaseQuery()
            .Where(d => d.MetaphoneCode == metaphoneCode
                     || d.DoubleMetaphonePrimary == metaphoneCode
                     || d.DoubleMetaphoneSecondary == metaphoneCode)
            .OrderByDescending(d => d.SearchHitCount)
            .Take(20)
            .ToListAsync(ct);

    // ── Country-scoped list ───────────────────────────────────────────────────

    public async Task<List<Destination>> GetByCountryAsync(
        Guid countryId, int page, int pageSize, CancellationToken ct = default)
        => await BaseQuery()
            .Where(d => d.CountryId == countryId)
            .OrderByDescending(d => d.SearchHitCount)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    // ── Persona-matched destinations ──────────────────────────────────────────
    // Filters by tag names and optionally caps cost. Used by GET /destinations/matching-user.
    // The calling service derives preferredTags from the user's psychographic profile.

    public async Task<List<Destination>> GetMatchingUserAsync(
        List<string> preferredTags, decimal? maxCostPerDay,
        int limit, CancellationToken ct = default)
    {
        var query = BaseQuery()
            .Where(d => d.DestinationTags
                .Any(dt => preferredTags.Contains(dt.Tag.TagName)));

        if (maxCostPerDay.HasValue)
            query = query.Where(d => d.AverageCostPerDay <= maxCostPerDay.Value);

        // Order by how many preferred tags match (EF translates Count with predicate)
        return await query
            .OrderByDescending(d =>
                d.DestinationTags.Count(dt => preferredTags.Contains(dt.Tag.TagName)))
            .ThenByDescending(d => d.SearchHitCount)
            .Take(limit)
            .ToListAsync(ct);
    }

    // ── Destinations similar to a wishlist ────────────────────────────────────
    // Returns destinations already linked to the wishlist's sibling wishlists
    // (same PeopleType), excluding destinations already in the target wishlist.

    public async Task<List<Destination>> GetSimilarToWishlistAsync(
        Guid wishlistId, int limit, CancellationToken ct = default)
    {
        var wishlist = await _db.Wishlists
            .AsNoTracking()
            .Include(w => w.WishlistDestinations)
            .FirstOrDefaultAsync(w => w.WishlistId == wishlistId, ct);


        if (wishlist is null) return new List<Destination>();

        var alreadyIncludedIds = wishlist.WishlistDestinations
            .Select(wd => wd.DestinationId)
            .ToHashSet();


        // Find destinations that appear in wishlists with the same PeopleType
        return await BaseQuery()
            .Where(d =>
                !alreadyIncludedIds.Contains(d.DestinationId) &&
                d.WishlistDestinations.Any(wd =>
                    wd.Wishlist.PeopleType == wishlist.PeopleType &&
                    wd.Wishlist.IsTemplate))
            .OrderByDescending(d => d.SearchHitCount)
            .Take(limit)
            .ToListAsync(ct);
    }

    // ── Destinations that is in a wishlist ────────────────────────────────────
    // Returns destinations already linked to the wishlist's sibling wishlists
    // (same PeopleType), excluding destinations already in the target wishlist.
    public async Task<List<Destination>> GetDestinationsInWishlistAsync(
        Guid wishlistId,int page, CancellationToken ct = default)
    {
        var wishlist = await _db.Wishlists
            .AsNoTracking()
            .Include(w => w.WishlistDestinations)
            .FirstOrDefaultAsync(w => w.WishlistId == wishlistId, ct);


        if (wishlist is null) return new List<Destination>();

        var alreadyIncludedIds = wishlist.WishlistDestinations
            .Select(wd => wd.DestinationId)
            .ToHashSet();


        // Find destinations that appear in wishlists
        const int pageSize = 6;

        return await BaseQuery()
            .Where(d =>
                alreadyIncludedIds.Contains(d.DestinationId) &&
                d.WishlistDestinations.Any(wd =>
                    wd.Wishlist.IsTemplate))
            .OrderByDescending(d => d.SearchHitCount)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    // ── Analytics: increment search hit counter ───────────────────────────────
    // Uses ExecuteUpdateAsync — no entity load, single UPDATE statement.

    public async Task IncrementSearchHitAsync(Guid destinationId, CancellationToken ct = default)
        => await _db.Destinations
            .Where(d => d.DestinationId == destinationId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                d => d.SearchHitCount, d => d.SearchHitCount + 1), ct);

    // ── Write operations ──────────────────────────────────────────────────────

    public async Task<Destination> CreateAsync(Destination destination, CancellationToken ct = default)
    {
        destination.DestinationId = Guid.NewGuid();
        _db.Destinations.Add(destination);
        await _db.SaveChangesAsync(ct);
        return destination;
    }

    public async Task UpdateAsync(Destination destination, CancellationToken ct = default)
    {
        _db.Destinations.Update(destination);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _db.Destinations
            .Where(d => d.DestinationId == id)
            .ExecuteDeleteAsync(ct);
    }
}
