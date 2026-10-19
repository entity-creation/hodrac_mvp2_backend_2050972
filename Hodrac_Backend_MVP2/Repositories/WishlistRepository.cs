using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.SignalR;
using Hodrac_Backend_MVP2.Infrastructure.SignalR;

namespace Hodrac_Backend_MVP2.Repositories;

// ─── Implementation ───────────────────────────────────────────────────────────

public class WishlistRepository : IWishlistRepository
{
    private readonly HodracDbContext _db;

    public WishlistRepository(HodracDbContext db)
    {
        _db = db;

    }

    // ── Base query — includes everything controllers need ─────────────────────

    private IQueryable<Wishlist> BaseQuery() =>
        _db.Wishlists
           .Include(w => w.ItineraryDays.OrderBy(d => d.DayNumber))
               .ThenInclude(d => d.ItineraryItems.OrderBy(i => i.ItemOrderIndex))
           .Include(w => w.ItineraryDays)
               .ThenInclude(d => d.MorningCity)
           .Include(w => w.ItineraryDays)
               .ThenInclude(d => d.AfternoonCity)
           .Include(w => w.ItineraryDays)
               .ThenInclude(d => d.EveningCity)
           .Include(w => w.ItineraryDays)
               .ThenInclude(d => d.TransitFromPreviousDayRoute)
                   .ThenInclude(r => r!.OriginCity)
           .Include(w => w.ItineraryDays)
               .ThenInclude(d => d.TransitFromPreviousDayRoute)
                   .ThenInclude(r => r!.DestinationCity)
           .Include(w => w.Collaborators)
           .Include(w => w.CreatorAttributions.Where(a => a.IsActive))
               .ThenInclude(a => a.Creator)
           .AsNoTracking();

    // ── Card query — lighter, no itinerary detail, used for list endpoints ────

    private IQueryable<Wishlist> CardQuery() =>
        _db.Wishlists.AsNoTracking();

    // ── Templates ─────────────────────────────────────────────────────────────

    public async Task<(List<Wishlist> Items, int TotalCount)> GetTemplatesAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = CardQuery()
            .Where(w => w.IsTemplate)
            .OrderByDescending(w => w.TotalGlobalSaveCount);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<List<Wishlist>> GetAllWishlists()
    {
        var wishlists = await CardQuery()
            .Where(w => w.IsTemplate)
            .OrderByDescending(w => w.TotalGlobalSaveCount).ToListAsync();

        if (wishlists != null)
            return wishlists;
        return new List<Wishlist>();
    }

    public async Task<Wishlist?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default)
        => await BaseQuery()
            .FirstOrDefaultAsync(w => w.WishlistId == id && w.IsTemplate, ct);

    /// <summary>Card-weight lookup (no itinerary graph). Null if missing or not a template.</summary>
    public async Task<Wishlist?> GetTemplateCardAsync(Guid id, CancellationToken ct = default)
        => await CardQuery()
            .FirstOrDefaultAsync(w => w.WishlistId == id && w.IsTemplate, ct);

    // ── Popular — IDs come pre-ranked from PopularWishlistService ────────────
    // Fetches the wishlists in ID order, then re-sorts to match the ranked order.

    public async Task<List<Wishlist>> GetPopularTemplatesAsync(
        List<Guid> rankedIds, int limit, CancellationToken ct = default)
    {
        var wishlists = await CardQuery()
            .Where(w => rankedIds.Contains(w.WishlistId) && w.IsTemplate)
            .ToListAsync(ct);

        // Restore the ranking order from the service layer
        return rankedIds
            .Take(limit)
            .Select(id => wishlists.FirstOrDefault(w => w.WishlistId == id))
            .Where(w => w is not null)
            .Select(w => w!)
            .ToList();
    }

    // ── Featured — IDs come from FeaturedWishlistService rotation ────────────

    public async Task<List<Wishlist>> GetFeaturedAsync(
        List<Guid> featuredIds, CancellationToken ct = default)
    {
        var wishlists = await CardQuery()
            .Where(w => featuredIds.Contains(w.WishlistId) && w.IsTemplate)
            .ToListAsync(ct);

        // Preserve interleaving order: paid first, then editorial, then random
        return featuredIds
            .Select(id => wishlists.FirstOrDefault(w => w.WishlistId == id))
            .Where(w => w is not null)
            .Select(w => w!)
            .ToList();
    }

    // ── Similar wishlists — same PeopleType, excluding self ──────────────────

    public async Task<List<Wishlist>> GetSimilarAsync(
        Guid wishlistId, int limit, CancellationToken ct = default)
    {
        var source = await _db.Wishlists
            .AsNoTracking()
            .Select(w => new { w.WishlistId, w.PeopleType })
            .FirstOrDefaultAsync(w => w.WishlistId == wishlistId, ct);

        if (source is null) return new List<Wishlist>();

        return await CardQuery()
            .Where(w => w.IsTemplate
                     && w.WishlistId != wishlistId
                     && w.PeopleType == source.PeopleType)
            .OrderByDescending(w => w.TotalGlobalSaveCount)
            .Take(limit)
            .ToListAsync(ct);
    }

    // ── Similar wishlists — same PeopleType, excluding self ──────────────────

    public async Task<List<Wishlist>> GetWishlistWithDestinationAsync(
        Guid destinationId, int limit, CancellationToken ct = default)
    {
        return await CardQuery()
    .Where(w => w.IsTemplate &&
                w.WishlistDestinations.Any(wd =>
                    wd.DestinationId == destinationId))
    .OrderByDescending(w => w.TotalGlobalSaveCount)
    .Take(limit)
    .ToListAsync(ct);
    }

    // ── User wishlists ────────────────────────────────────────────────────────

    public async Task<List<Wishlist>> GetUserWishlistsAsync(
        Guid userId, CancellationToken ct = default)
        => await BaseQuery()
            .Where(w => !w.IsTemplate && w.OwnerUserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

    /// <summary>
    /// Returns the wishlist only if the requesting user is the owner OR a collaborator.
    /// Returns null for any other user — the controller returns 403/404 accordingly.
    /// </summary>
    public async Task<Wishlist?> GetUserWishlistByIdAsync(
        Guid wishlistId, Guid userId, CancellationToken ct = default)
        => await BaseQuery()
            .FirstOrDefaultAsync(w =>
                w.WishlistId == wishlistId &&
                !w.IsTemplate &&
                (w.OwnerUserId == userId ||
                 w.Collaborators.Any(c => c.UserId == userId)),
            ct);

    // ── Fork (copy template → user's editable wishlist) ───────────────────────
    // Deep-copies the template: all ItineraryDays, ItineraryItems, WishlistDestinations.
    // ForkedFromId preserves the lineage. New entity gets fresh IDs throughout.

    public async Task<Wishlist> ForkAsync(
        Guid templateId, Guid newOwnerId, CancellationToken ct = default)
    {
        var template = await _db.Wishlists
            .Include(w => w.ItineraryDays)
                .ThenInclude(d => d.ItineraryItems)
            .Include(w => w.WishlistDestinations)
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.WishlistId == templateId && w.IsTemplate, ct)
            ?? throw new KeyNotFoundException($"Template {templateId} not found.");

        var fork = new Wishlist
        {
            WishlistId = Guid.NewGuid(),
            WishlistName = template.WishlistName,
            WishlistDescription = template.WishlistDescription,
            ShortStory = template.ShortStory,
            TotalDays = template.TotalDays,
            PeopleType = template.PeopleType,
            WishlistHeroImage = template.WishlistHeroImage,
            GlobalInclusionsJson = template.GlobalInclusionsJson,
            RawContentKeywords = template.RawContentKeywords,
            PsychologicalVibeTagsJson = template.PsychologicalVibeTagsJson,
            DefaultTravelersCount = template.DefaultTravelersCount,
            BasePricePerPerson = template.BasePricePerPerson,
            CalculatedTotalCost = template.CalculatedTotalCost,
            DepositAmountRequired = template.DepositAmountRequired,
            AccommodationInclusions = template.AccommodationInclusions,
            TransitInclusions = template.TransitInclusions,
            ActivityInclusions = template.ActivityInclusions,
            PrimaryPersonaTarget = template.PrimaryPersonaTarget,
            // Ownership
            IsTemplate = false,
            OwnerUserId = newOwnerId,
            ForkedFromId = templateId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Deep-copy itinerary days + items
        fork.ItineraryDays = template.ItineraryDays.Select(day => new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            WishlistId = fork.WishlistId,
            DayNumber = day.DayNumber,
            DayTitle = day.DayTitle,
            MorningCityId = day.MorningCityId,
            AfternoonCityId = day.AfternoonCityId,
            EveningCityId = day.EveningCityId,
            TransitFromPreviousDayRouteId = day.TransitFromPreviousDayRouteId,
            ItineraryItems = day.ItineraryItems.Select(item => new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItemTitle = item.ItemTitle,
                ItemDescription = item.ItemDescription,
                ItemOrderIndex = item.ItemOrderIndex,
                TimeOfDay = item.TimeOfDay,
                ImageUrl = item.ImageUrl,
                SocialProofBadge = item.SocialProofBadge,
                IndividualCostModifier = item.IndividualCostModifier,
                IsOptionalActivity = item.IsOptionalActivity,
                IsSelectedByDefault = item.IsSelectedByDefault,
            }).ToList()
        }).ToList();

        // Copy destination links (new join rows pointing to same destination GUIDs)
        fork.WishlistDestinations = template.WishlistDestinations.Select(wd =>
            new WishlistDestination
            {
                WishlistId = fork.WishlistId,
                DestinationId = wd.DestinationId,
            }).ToList();

        // Add the owner as a collaborator with role "Owner"
        fork.Collaborators = new List<WishlistCollaborator>
        {
            new()
            {
                WishlistCollaboratorId = Guid.NewGuid(),
                WishlistId             = fork.WishlistId,
                UserId                 = newOwnerId,
                Role                   = "Owner",
                JoinedAt               = DateTimeOffset.UtcNow,
            }
        };

        _db.Wishlists.Add(fork);
        await _db.SaveChangesAsync(ct);
        return fork;
    }

    // ── Update (Editor or Owner) ──────────────────────────────────────────────
    // Loads the tracked wishlist row only (no related rows), lets the caller apply
    // scalar changes, and saves. Only the changed columns of "Wishlists" are
    // written — itinerary, collaborators etc. are never touched.
    // When clientXmin is supplied it becomes the concurrency token's original value,
    // so a stale edit throws DbUpdateConcurrencyException — caller maps it to 409.

    public async Task<bool> UpdateAsync(
        Guid wishlistId, Action<Wishlist> applyChanges, uint? clientXmin, CancellationToken ct = default)
    {
        var wishlist = await _db.Wishlists
            .FirstOrDefaultAsync(w => w.WishlistId == wishlistId && !w.IsTemplate, ct);
        if (wishlist is null) return false;

        applyChanges(wishlist);

        if (clientXmin is not null)
            _db.Entry(wishlist).Property(w => w.xmin).OriginalValue = clientXmin.Value;

        await _db.SaveChangesAsync(ct);     // throws DbUpdateConcurrencyException on version mismatch
        return true;
    }

    // ── Itinerary editing (user wishlists only) ───────────────────────────────
    // Tracked load of the wishlist + its days + items. Templates are never returned.

    public async Task<Wishlist?> GetForItineraryEditAsync(Guid wishlistId, CancellationToken ct = default)
        => await _db.Wishlists
            .Include(w => w.ItineraryDays)
                .ThenInclude(d => d.ItineraryItems)
            .FirstOrDefaultAsync(w => w.WishlistId == wishlistId && !w.IsTemplate, ct);

    public void AddItineraryDay(ItineraryDay day) => _db.ItineraryDays.Add(day);
    public void RemoveItineraryDay(ItineraryDay day) => _db.ItineraryDays.Remove(day);
    public void AddItineraryItem(ItineraryItem item) => _db.ItineraryItems.Add(item);
    public void RemoveItineraryItem(ItineraryItem item) => _db.ItineraryItems.Remove(item);

    public async Task<bool> CityExistsAsync(Guid cityId, CancellationToken ct = default)
        => await _db.Cities.AnyAsync(c => c.CityId == cityId, ct);

    public async Task<bool> TransitRouteExistsAsync(Guid routeId, CancellationToken ct = default)
        => await _db.TransitRoutes.AnyAsync(r => r.TransitRouteId == routeId, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);

    /// <summary>
    /// Uses the client's xmin as the tracked wishlist's concurrency-token original value,
    /// so the next SaveChanges throws DbUpdateConcurrencyException if the row changed since.
    /// </summary>
    public void SetXminOriginalValue(Wishlist trackedWishlist, uint clientXmin)
        => _db.Entry(trackedWishlist).Property(w => w.xmin).OriginalValue = clientXmin;

    // ── Delete (Owner only) ───────────────────────────────────────────────────

    public async Task DeleteUserWishlistAsync(
        Guid wishlistId, Guid userId, CancellationToken ct = default)
    {
        var wishlist = await _db.Wishlists
            .FirstOrDefaultAsync(w => w.WishlistId == wishlistId
                                   && w.OwnerUserId == userId
                                   && !w.IsTemplate, ct)
            ?? throw new KeyNotFoundException("Wishlist not found or not owned by this user.");

        _db.Wishlists.Remove(wishlist);
        await _db.SaveChangesAsync(ct);
    }

    // ── Save / bookmark ───────────────────────────────────────────────────────

    public async Task<bool> SaveWishlistAsync(
        Guid wishlistId, Guid userId, CancellationToken ct = default)
    {
        var alreadySaved = await _db.SavedWishlists
            .AnyAsync(sw => sw.WishlistId == wishlistId && sw.UserId == userId, ct);
        if (alreadySaved) return false;

        var row = new SavedWishlist
        {
            SavedWishlistId = Guid.NewGuid(),
            UserId = userId,
            WishlistId = wishlistId,
            SavedAt = DateTimeOffset.UtcNow,
        };
        _db.SavedWishlists.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Concurrent double-save hit the (UserId, WishlistId) unique index.
            _db.Entry(row).State = EntityState.Detached;
            return false;
        }
        return true;
    }

    public async Task<bool> UnsaveWishlistAsync(
        Guid wishlistId, Guid userId, CancellationToken ct = default)
    {
        var saved = await _db.SavedWishlists
            .FirstOrDefaultAsync(sw => sw.WishlistId == wishlistId && sw.UserId == userId, ct);
        if (saved is null) return false;

        _db.SavedWishlists.Remove(saved);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IsWishlistSavedAsync(
        Guid wishlistId, Guid userId, CancellationToken ct = default)
        => await _db.SavedWishlists
            .AnyAsync(sw => sw.WishlistId == wishlistId && sw.UserId == userId, ct);

    // ── Pricing snapshot ──────────────────────────────────────────────────────
    // "Active" = ValidUntil is in the future. Returns the most recently generated one.

    public async Task<WishlistPricingSnapshot?> GetActiveSnapshotAsync(
        Guid wishlistId, CancellationToken ct = default)
        => await _db.WishlistPricingSnapshots
            .AsNoTracking()
            .Where(s => s.WishlistId == wishlistId && s.ValidUntil > DateTimeOffset.UtcNow)
            .OrderByDescending(s => s.GeneratedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<WishlistPricingSnapshot> CreateSnapshotAsync(
        WishlistPricingSnapshot snapshot, CancellationToken ct = default)
    {
        snapshot.WishlistPricingSnapshotId = Guid.NewGuid();
        snapshot.GeneratedAt = DateTimeOffset.UtcNow;
        _db.WishlistPricingSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
        return snapshot;
    }

    // ── Save count helpers ────────────────────────────────────────────────────

    public async Task IncrementSaveCountAsync(Guid wishlistId, CancellationToken ct = default)
        => await _db.Wishlists
            .Where(w => w.WishlistId == wishlistId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                w => w.TotalGlobalSaveCount, w => w.TotalGlobalSaveCount + 1), ct);

    public async Task DecrementSaveCountAsync(Guid wishlistId, CancellationToken ct = default)
        => await _db.Wishlists
            .Where(w => w.WishlistId == wishlistId && w.TotalGlobalSaveCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(
                w => w.TotalGlobalSaveCount, w => w.TotalGlobalSaveCount - 1), ct);

    public async Task<List<Wishlist>> SearchByNameAsync(
    string query, Guid? ownerUserId, int limit = 10, CancellationToken ct = default)
    {
        var normalized = query.Trim();
        if (string.IsNullOrEmpty(normalized)) return new List<Wishlist>();

        var containsTerm = $"%{normalized}%";
        var prefixTerm = $"{normalized}%";

        var baseQuery = _db.Wishlists
            .AsNoTracking()
            .Include(w => w.WishlistDestinations)
                .ThenInclude(wd => wd.Destination)
            .Where(w => EF.Functions.ILike(w.WishlistName, containsTerm));

        baseQuery = ownerUserId is not null
            ? baseQuery.Where(w => !w.IsTemplate && w.OwnerUserId == ownerUserId)
            : baseQuery.Where(w => w.IsTemplate);

        return await baseQuery
            .OrderByDescending(w => EF.Functions.ILike(w.WishlistName, prefixTerm))
            .ThenByDescending(w => w.TotalGlobalSaveCount)
            .Take(limit)
            .ToListAsync(ct);
    }


}