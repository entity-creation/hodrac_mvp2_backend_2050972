using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.DTOs.WishlistDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for platform wishlist templates (IsTemplate = true), their itinerary,
/// featuring and the featured rotation pool. User forks are never touched here.
///
/// Templates
///   GET    /api/admin/wishlists?page=&amp;pageSize=&amp;search=&amp;featured=&amp;sort=newest|saves|name
///   GET    /api/admin/wishlists/{id}
///   POST   /api/admin/wishlists
///   PATCH  /api/admin/wishlists/{id}                       (optional body xmin → 409 on conflict)
///   PUT    /api/admin/wishlists/{id}/destinations
///   DELETE /api/admin/wishlists/{id}                       (409 when forks / trip posts reference it)
///   POST   /api/admin/wishlists/{id}/feature | /unfeature
/// Itinerary (all accept ?xmin=, return AdminWishlistDetailDto)
///   POST   /api/admin/wishlists/{id}/itinerary/days
///   PUT    /api/admin/wishlists/{id}/itinerary/days/{dayId}
///   DELETE /api/admin/wishlists/{id}/itinerary/days/{dayId}
///   POST   /api/admin/wishlists/{id}/itinerary/days/{dayId}/items
///   PUT    /api/admin/wishlists/{id}/itinerary/days/{dayId}/items/{itemId}
///   DELETE /api/admin/wishlists/{id}/itinerary/days/{dayId}/items/{itemId}
///   PUT    /api/admin/wishlists/{id}/itinerary/order
/// Featured pool
///   GET    /api/admin/featured-pool?poolType=
///   POST   /api/admin/featured-pool
///   PATCH  /api/admin/featured-pool/{id}
///   DELETE /api/admin/featured-pool/{id}
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin")]
public class AdminWishlistsController : ControllerBase
{
    private static readonly string[] TimesOfDay = { "Morning", "Afternoon", "Evening" };
    private static readonly string[] PoolTypes = { "Editorial", "Paid", "Random" };
    private static readonly string[] Sorts = { "newest", "saves", "name" };

    private readonly HodracDbContext _db;
    private readonly ILogger<AdminWishlistsController> _logger;

    public AdminWishlistsController(HodracDbContext db, ILogger<AdminWishlistsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ══ Templates ═════════════════════════════════════════════════════════════

    [HttpGet("wishlists")]
    public async Task<ActionResult<AdminPagedResult<AdminWishlistSummaryDto>>> GetWishlists(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? featured = null,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var sortKey = string.IsNullOrWhiteSpace(sort) ? "newest" : sort.Trim().ToLowerInvariant();
        if (!Sorts.Contains(sortKey))
        {
            ModelState.AddModelError(nameof(sort), $"Sort must be one of: {string.Join(", ", Sorts)}.");
            return ValidationProblem(ModelState);
        }

        var query = _db.Wishlists.AsNoTracking().Where(w => w.IsTemplate);

        if (featured is not null) query = query.Where(w => w.IsFeatured == featured.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(w => EF.Functions.ILike(w.WishlistName, pattern));
        }

        var total = await query.CountAsync(ct);

        query = sortKey switch
        {
            "saves" => query.OrderByDescending(w => w.TotalGlobalSaveCount).ThenBy(w => w.WishlistId),
            "name" => query.OrderBy(w => w.WishlistName).ThenBy(w => w.WishlistId),
            _ => query.OrderByDescending(w => w.CreatedAt).ThenBy(w => w.WishlistId)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new AdminWishlistSummaryDto(
                w.WishlistId,
                w.WishlistName,
                w.WishlistHeroImage,
                w.TotalDays,
                w.PeopleType,
                w.BasePricePerPerson,
                w.TotalGlobalSaveCount,
                w.IsFeatured,
                w.FeaturedUntil,
                w.WishlistDestinations.Count,
                _db.Wishlists.Count(f => f.ForkedFromId == w.WishlistId),
                _db.PartnerLinks.Count(l => l.WishlistId == w.WishlistId),
                w.CreatedAt))
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminWishlistSummaryDto>(items, page, pageSize, total));
    }

    [HttpGet("wishlists/{id:guid}")]
    public async Task<ActionResult<AdminWishlistDetailDto>> GetWishlist(Guid id, CancellationToken ct = default)
    {
        var detail = await LoadDetail(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("wishlists")]
    public async Task<ActionResult<AdminWishlistDetailDto>> CreateWishlist(CreateAdminWishlistRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.WishlistName))
            ModelState.AddModelError(nameof(request.WishlistName), "WishlistName is required.");

        var inclusions = CleanList(request.GlobalInclusions, nameof(request.GlobalInclusions));
        var vibeTags = CleanList(request.PsychologicalVibeTags, nameof(request.PsychologicalVibeTags));
        var destinationIds = await ValidateDestinationIds(request.DestinationIds, ct);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var wishlist = new Wishlist
        {
            WishlistId = Guid.NewGuid(),
            WishlistName = request.WishlistName.Trim(),
            WishlistDescription = request.WishlistDescription ?? string.Empty,
            ShortStory = request.ShortStory ?? string.Empty,
            TotalDays = request.TotalDays,
            PeopleType = request.PeopleType?.Trim() ?? string.Empty,
            WishlistHeroImage = request.WishlistHeroImage?.Trim() ?? string.Empty,
            GlobalInclusionsJson = JsonSerializer.Serialize(inclusions ?? new List<string>()),
            RawContentKeywords = request.RawContentKeywords ?? string.Empty,
            PsychologicalVibeTagsJson = JsonSerializer.Serialize(vibeTags ?? new List<string>()),
            DefaultTravelersCount = request.DefaultTravelersCount,
            BasePricePerPerson = request.BasePricePerPerson,
            CalculatedTotalCost = request.CalculatedTotalCost ?? request.BasePricePerPerson * request.DefaultTravelersCount,
            DepositAmountRequired = request.DepositAmountRequired,
            AccommodationInclusions = request.AccommodationInclusions ?? string.Empty,
            TransitInclusions = request.TransitInclusions ?? string.Empty,
            ActivityInclusions = request.ActivityInclusions ?? string.Empty,
            PrimaryPersonaTarget = request.PrimaryPersonaTarget?.Trim() ?? string.Empty,
            IsTemplate = true,
            OwnerUserId = null,
            ForkedFromId = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var destinationId in destinationIds ?? new List<Guid>())
            wishlist.WishlistDestinations.Add(new WishlistDestination { WishlistId = wishlist.WishlistId, DestinationId = destinationId });

        _db.Wishlists.Add(wishlist);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created wishlist template {WishlistId} ({Name})", wishlist.WishlistId, wishlist.WishlistName);

        var detail = await LoadDetail(wishlist.WishlistId, ct);
        return CreatedAtAction(nameof(GetWishlist), new { id = wishlist.WishlistId }, detail);
    }

    [HttpPatch("wishlists/{id:guid}")]
    public async Task<ActionResult<AdminWishlistDetailDto>> UpdateWishlist(Guid id, UpdateAdminWishlistRequest request, CancellationToken ct = default)
    {
        var wishlist = await _db.Wishlists.FirstOrDefaultAsync(w => w.WishlistId == id && w.IsTemplate, ct);
        if (wishlist is null) return NotFound();

        if (request.WishlistName is not null && string.IsNullOrWhiteSpace(request.WishlistName))
            ModelState.AddModelError(nameof(request.WishlistName), "WishlistName cannot be blank.");

        var inclusions = CleanList(request.GlobalInclusions, nameof(request.GlobalInclusions));
        var vibeTags = CleanList(request.PsychologicalVibeTags, nameof(request.PsychologicalVibeTags));

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // CalculatedTotalCost follows price × travelers only while it is still the derived value.
        var wasDerived = wishlist.CalculatedTotalCost == wishlist.BasePricePerPerson * wishlist.DefaultTravelersCount;

        if (request.WishlistName is not null) wishlist.WishlistName = request.WishlistName.Trim();
        if (request.WishlistDescription is not null) wishlist.WishlistDescription = request.WishlistDescription;
        if (request.ShortStory is not null) wishlist.ShortStory = request.ShortStory;
        if (request.TotalDays is not null) wishlist.TotalDays = request.TotalDays.Value;
        if (request.PeopleType is not null) wishlist.PeopleType = request.PeopleType.Trim();
        if (request.WishlistHeroImage is not null) wishlist.WishlistHeroImage = request.WishlistHeroImage.Trim();
        if (inclusions is not null) wishlist.GlobalInclusionsJson = JsonSerializer.Serialize(inclusions);
        if (request.RawContentKeywords is not null) wishlist.RawContentKeywords = request.RawContentKeywords;
        if (vibeTags is not null) wishlist.PsychologicalVibeTagsJson = JsonSerializer.Serialize(vibeTags);
        if (request.DefaultTravelersCount is not null) wishlist.DefaultTravelersCount = request.DefaultTravelersCount.Value;
        if (request.BasePricePerPerson is not null) wishlist.BasePricePerPerson = request.BasePricePerPerson.Value;
        if (request.DepositAmountRequired is not null) wishlist.DepositAmountRequired = request.DepositAmountRequired.Value;
        if (request.AccommodationInclusions is not null) wishlist.AccommodationInclusions = request.AccommodationInclusions;
        if (request.TransitInclusions is not null) wishlist.TransitInclusions = request.TransitInclusions;
        if (request.ActivityInclusions is not null) wishlist.ActivityInclusions = request.ActivityInclusions;
        if (request.PrimaryPersonaTarget is not null) wishlist.PrimaryPersonaTarget = request.PrimaryPersonaTarget.Trim();

        if (request.CalculatedTotalCost is not null)
            wishlist.CalculatedTotalCost = request.CalculatedTotalCost.Value;
        else if (wasDerived && (request.BasePricePerPerson is not null || request.DefaultTravelersCount is not null))
            wishlist.CalculatedTotalCost = wishlist.BasePricePerPerson * wishlist.DefaultTravelersCount;

        if (request.Xmin is not null)
            _db.Entry(wishlist).Property(w => w.xmin).OriginalValue = request.Xmin.Value;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentEditConflict();
        }

        return Ok(await LoadDetail(id, ct));
    }

    [HttpPut("wishlists/{id:guid}/destinations")]
    public async Task<ActionResult<AdminWishlistDetailDto>> SetDestinations(Guid id, SetWishlistDestinationsRequest request, CancellationToken ct = default)
    {
        var wishlist = await _db.Wishlists
            .Include(w => w.WishlistDestinations)
            .FirstOrDefaultAsync(w => w.WishlistId == id && w.IsTemplate, ct);
        if (wishlist is null) return NotFound();

        var destinationIds = await ValidateDestinationIds(request.DestinationIds ?? new List<Guid>(), ct) ?? new List<Guid>();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var wanted = destinationIds.ToHashSet();
        foreach (var link in wishlist.WishlistDestinations.Where(wd => !wanted.Contains(wd.DestinationId)).ToList())
            _db.WishlistDestinations.Remove(link);

        var existing = wishlist.WishlistDestinations.Select(wd => wd.DestinationId).ToHashSet();
        foreach (var destinationId in destinationIds.Where(d => !existing.Contains(d)))
            _db.WishlistDestinations.Add(new WishlistDestination { WishlistId = id, DestinationId = destinationId });

        await _db.SaveChangesAsync(ct);

        return Ok(await LoadDetail(id, ct));
    }

    /// <summary>
    /// Hard delete. Refused (409) while user forks or trip posts reference the template.
    /// Attributions are removed explicitly (FK is NO ACTION); days/items, destinations,
    /// collaborators, pricing snapshots, saves, featured-pool entries and partner links
    /// (with their clicks) are removed by the database's ON DELETE CASCADE.
    /// </summary>
    [HttpDelete("wishlists/{id:guid}")]
    public async Task<IActionResult> DeleteWishlist(Guid id, CancellationToken ct = default)
    {
        if (!await _db.Wishlists.AnyAsync(w => w.WishlistId == id && w.IsTemplate, ct)) return NotFound();

        var forkCount = await _db.Wishlists.CountAsync(w => w.ForkedFromId == id, ct);
        var tripPostCount = await _db.TripPosts.CountAsync(t => t.WishlistId == id, ct);
        if (forkCount > 0 || tripPostCount > 0)
        {
            return Conflict(new
            {
                error = "This template is still referenced and cannot be deleted. Unfeature it instead.",
                code = "WISHLIST_IN_USE",
                forkCount,
                tripPostCount
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.WishlistCreatorAttributions.Where(a => a.WishlistId == id).ExecuteDeleteAsync(ct);
        var deleted = await _db.Wishlists.Where(w => w.WishlistId == id && w.IsTemplate).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);

        if (deleted == 0) return NotFound();

        _logger.LogInformation("Admin deleted wishlist template {WishlistId}", id);
        return NoContent();
    }

    // ── Featuring ─────────────────────────────────────────────────────────────

    [HttpPost("wishlists/{id:guid}/feature")]
    public async Task<IActionResult> FeatureWishlist(Guid id, FeatureWishlistRequest? request, CancellationToken ct = default)
    {
        var featuredUntil = request?.FeaturedUntil?.ToUniversalTime();
        if (featuredUntil is not null && featuredUntil <= DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(nameof(FeatureWishlistRequest.FeaturedUntil), "FeaturedUntil must be in the future.");
            return ValidationProblem(ModelState);
        }

        var updated = await _db.Wishlists
            .Where(w => w.WishlistId == id && w.IsTemplate)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.IsFeatured, true)
                .SetProperty(w => w.FeaturedUntil, featuredUntil), ct);

        if (updated == 0) return NotFound();

        _logger.LogInformation("Admin featured wishlist {WishlistId} until {FeaturedUntil}", id, featuredUntil);
        return NoContent();
    }

    [HttpPost("wishlists/{id:guid}/unfeature")]
    public async Task<IActionResult> UnfeatureWishlist(Guid id, CancellationToken ct = default)
    {
        var updated = await _db.Wishlists
            .Where(w => w.WishlistId == id && w.IsTemplate)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.IsFeatured, false)
                .SetProperty(w => w.FeaturedUntil, (DateTimeOffset?)null), ct);

        if (updated == 0) return NotFound();

        _logger.LogInformation("Admin unfeatured wishlist {WishlistId}", id);
        return NoContent();
    }

    // ══ Template itinerary editing ════════════════════════════════════════════
    // Mirrors /api/user-wishlists/{id}/itinerary/... (same request DTOs and rules),
    // but only for templates. For Guid? city/route fields on updates:
    // null = leave unchanged, Guid.Empty = clear.

    [HttpPost("wishlists/{id:guid}/itinerary/days")]
    public Task<IActionResult> AddDay(
        Guid id,
        [FromBody] AddItineraryDayRequest request,
        [FromQuery] uint? xmin,
        CancellationToken ct = default)
        => EditItineraryAsync(id, xmin, async wishlist =>
        {
            if (request.DayTitle is { Length: > 200 })
                ModelState.AddModelError(nameof(request.DayTitle), "DayTitle must be at most 200 characters.");
            await ValidateDayRefsAsync(
                request.MorningCityId, request.AfternoonCityId, request.EveningCityId,
                request.TransitFromPreviousDayRouteId, ct);
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var days = OrderedDays(wishlist);
            var position = Math.Clamp(request.DayNumber ?? days.Count + 1, 1, days.Count + 1);

            var day = new ItineraryDay
            {
                ItineraryDayId = Guid.NewGuid(),
                WishlistId = wishlist.WishlistId,
                DayTitle = request.DayTitle ?? string.Empty,
                MorningCityId = NullIfEmpty(request.MorningCityId),
                AfternoonCityId = NullIfEmpty(request.AfternoonCityId),
                EveningCityId = NullIfEmpty(request.EveningCityId),
                TransitFromPreviousDayRouteId = NullIfEmpty(request.TransitFromPreviousDayRouteId),
            };
            _db.ItineraryDays.Add(day);

            days.Insert(position - 1, day);
            Renumber(days);
            SyncTotalDays(wishlist, days.Count);
            return null;
        }, ct);

    [HttpPut("wishlists/{id:guid}/itinerary/days/{dayId:guid}")]
    public Task<IActionResult> UpdateDay(
        Guid id,
        Guid dayId,
        [FromBody] UpdateItineraryDayRequest request,
        [FromQuery] uint? xmin,
        CancellationToken ct = default)
        => EditItineraryAsync(id, xmin, async wishlist =>
        {
            var day = wishlist.ItineraryDays.FirstOrDefault(d => d.ItineraryDayId == dayId);
            if (day is null) return NotFound("Day not found.");

            if (request.DayTitle is { Length: > 200 })
                ModelState.AddModelError(nameof(request.DayTitle), "DayTitle must be at most 200 characters.");
            await ValidateDayRefsAsync(
                request.MorningCityId, request.AfternoonCityId, request.EveningCityId,
                request.TransitFromPreviousDayRouteId, ct);
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            if (request.DayTitle is not null) day.DayTitle = request.DayTitle;
            if (request.MorningCityId.HasValue) day.MorningCityId = NullIfEmpty(request.MorningCityId);
            if (request.AfternoonCityId.HasValue) day.AfternoonCityId = NullIfEmpty(request.AfternoonCityId);
            if (request.EveningCityId.HasValue) day.EveningCityId = NullIfEmpty(request.EveningCityId);
            if (request.TransitFromPreviousDayRouteId.HasValue)
                day.TransitFromPreviousDayRouteId = NullIfEmpty(request.TransitFromPreviousDayRouteId);
            return null;
        }, ct);

    [HttpDelete("wishlists/{id:guid}/itinerary/days/{dayId:guid}")]
    public Task<IActionResult> DeleteDay(
        Guid id,
        Guid dayId,
        [FromQuery] uint? xmin,
        CancellationToken ct = default)
        => EditItineraryAsync(id, xmin, wishlist =>
        {
            var day = wishlist.ItineraryDays.FirstOrDefault(d => d.ItineraryDayId == dayId);
            if (day is null) return Task.FromResult<IActionResult?>(NotFound("Day not found."));

            foreach (var item in day.ItineraryItems.ToList())
                _db.ItineraryItems.Remove(item);
            _db.ItineraryDays.Remove(day);

            var days = OrderedDays(wishlist).Where(d => d.ItineraryDayId != dayId).ToList();
            Renumber(days);
            SyncTotalDays(wishlist, days.Count);
            return Task.FromResult<IActionResult?>(null);
        }, ct);

    [HttpPost("wishlists/{id:guid}/itinerary/days/{dayId:guid}/items")]
    public Task<IActionResult> AddItem(
        Guid id,
        Guid dayId,
        [FromBody] AddItineraryItemRequest request,
        [FromQuery] uint? xmin,
        CancellationToken ct = default)
        => EditItineraryAsync(id, xmin, wishlist =>
        {
            var day = wishlist.ItineraryDays.FirstOrDefault(d => d.ItineraryDayId == dayId);
            if (day is null) return Task.FromResult<IActionResult?>(NotFound("Day not found."));

            if (!ValidateItemFields(request.Title, request.TimeOfDay, titleRequired: true))
                return Task.FromResult<IActionResult?>(ValidationProblem(ModelState));

            var items = OrderedItems(day);
            var position = Math.Clamp(request.OrderIndex ?? items.Count + 1, 1, items.Count + 1);

            var item = new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day.ItineraryDayId,
                ItemTitle = request.Title.Trim(),
                ItemDescription = request.Description ?? string.Empty,
                TimeOfDay = request.TimeOfDay ?? string.Empty,
                ImageUrl = request.ImageUrl ?? string.Empty,
                SocialProofBadge = request.SocialProofBadge ?? string.Empty,
                IndividualCostModifier = request.CostModifier ?? 0m,
                IsOptionalActivity = request.IsOptional ?? false,
                IsSelectedByDefault = request.IsSelectedByDefault ?? true,
            };
            _db.ItineraryItems.Add(item);

            items.Insert(position - 1, item);
            Renumber(items);
            return Task.FromResult<IActionResult?>(null);
        }, ct);

    [HttpPut("wishlists/{id:guid}/itinerary/days/{dayId:guid}/items/{itemId:guid}")]
    public Task<IActionResult> UpdateItem(
        Guid id,
        Guid dayId,
        Guid itemId,
        [FromBody] UpdateItineraryItemRequest request,
        [FromQuery] uint? xmin,
        CancellationToken ct = default)
        => EditItineraryAsync(id, xmin, wishlist =>
        {
            var item = wishlist.ItineraryDays
                .FirstOrDefault(d => d.ItineraryDayId == dayId)?
                .ItineraryItems.FirstOrDefault(i => i.ItineraryItemId == itemId);
            if (item is null) return Task.FromResult<IActionResult?>(NotFound("Item not found."));

            if (!ValidateItemFields(request.Title, request.TimeOfDay, titleRequired: false))
                return Task.FromResult<IActionResult?>(ValidationProblem(ModelState));

            if (request.Title is not null)               item.ItemTitle = request.Title.Trim();
            if (request.Description is not null)         item.ItemDescription = request.Description;
            if (request.TimeOfDay is not null)           item.TimeOfDay = request.TimeOfDay;
            if (request.ImageUrl is not null)            item.ImageUrl = request.ImageUrl;
            if (request.SocialProofBadge is not null)    item.SocialProofBadge = request.SocialProofBadge;
            if (request.CostModifier.HasValue)           item.IndividualCostModifier = request.CostModifier.Value;
            if (request.IsOptional.HasValue)             item.IsOptionalActivity = request.IsOptional.Value;
            if (request.IsSelectedByDefault.HasValue)    item.IsSelectedByDefault = request.IsSelectedByDefault.Value;
            return Task.FromResult<IActionResult?>(null);
        }, ct);

    [HttpDelete("wishlists/{id:guid}/itinerary/days/{dayId:guid}/items/{itemId:guid}")]
    public Task<IActionResult> DeleteItem(
        Guid id,
        Guid dayId,
        Guid itemId,
        [FromQuery] uint? xmin,
        CancellationToken ct = default)
        => EditItineraryAsync(id, xmin, wishlist =>
        {
            var day = wishlist.ItineraryDays.FirstOrDefault(d => d.ItineraryDayId == dayId);
            var item = day?.ItineraryItems.FirstOrDefault(i => i.ItineraryItemId == itemId);
            if (day is null || item is null) return Task.FromResult<IActionResult?>(NotFound("Item not found."));

            _db.ItineraryItems.Remove(item);
            Renumber(OrderedItems(day).Where(i => i.ItineraryItemId != itemId).ToList());
            return Task.FromResult<IActionResult?>(null);
        }, ct);

    [HttpPut("wishlists/{id:guid}/itinerary/order")]
    public Task<IActionResult> Reorder(
        Guid id,
        [FromBody] ReorderItineraryRequest request,
        [FromQuery] uint? xmin,
        CancellationToken ct = default)
        => EditItineraryAsync(id, xmin, wishlist =>
            Task.FromResult(ApplyReorder(wishlist, request)), ct);

    private IActionResult? ApplyReorder(Wishlist wishlist, ReorderItineraryRequest request)
    {
        var daysById = wishlist.ItineraryDays.ToDictionary(d => d.ItineraryDayId);

        if (request.DayOrder is not null)
        {
            if (request.DayOrder.Count != daysById.Count
                || request.DayOrder.Distinct().Count() != request.DayOrder.Count
                || request.DayOrder.Any(d => !daysById.ContainsKey(d)))
                return ReorderError("DayOrder must list every day of this wishlist exactly once.");
        }

        var itemsById = wishlist.ItineraryDays
            .SelectMany(d => d.ItineraryItems)
            .ToDictionary(i => i.ItineraryItemId);

        if (request.ItemOrder is not null)
        {
            if (request.ItemOrder.Any(e => e.ItemIds is null))
                return ReorderError("ItemIds is required for every ItemOrder entry.");
            if (request.ItemOrder.Select(e => e.DayId).Distinct().Count() != request.ItemOrder.Count)
                return ReorderError("Each day may appear only once in ItemOrder.");
            if (request.ItemOrder.Any(e => !daysById.ContainsKey(e.DayId)))
                return ReorderError("ItemOrder references a day that is not in this wishlist.");

            var listed = request.ItemOrder.SelectMany(e => e.ItemIds).ToList();
            if (listed.Distinct().Count() != listed.Count)
                return ReorderError("An item may appear only once in ItemOrder.");
            if (listed.Any(i => !itemsById.ContainsKey(i)))
                return ReorderError("ItemOrder references an item that is not in this wishlist.");

            var listedSet = listed.ToHashSet();
            var orphaned = request.ItemOrder
                .SelectMany(e => daysById[e.DayId].ItineraryItems)
                .Any(i => !listedSet.Contains(i.ItineraryItemId));
            if (orphaned)
                return ReorderError("Each listed day must include all of its items (list moved items under their new day).");
        }

        if (request.DayOrder is not null)
            Renumber(request.DayOrder.Select(d => daysById[d]).ToList());

        if (request.ItemOrder is not null)
        {
            var sourceDays = new HashSet<ItineraryDay>();
            foreach (var entry in request.ItemOrder)
            {
                var target = daysById[entry.DayId];
                var ordered = entry.ItemIds.Select(i => itemsById[i]).ToList();
                foreach (var item in ordered.Where(i => i.ItineraryDayId != target.ItineraryDayId))
                {
                    var source = daysById[item.ItineraryDayId];
                    source.ItineraryItems.Remove(item);
                    target.ItineraryItems.Add(item);
                    item.ItineraryDayId = target.ItineraryDayId;
                    sourceDays.Add(source);
                }
                Renumber(ordered);
            }

            foreach (var source in sourceDays.Where(d => request.ItemOrder.All(e => e.DayId != d.ItineraryDayId)))
                Renumber(OrderedItems(source));
        }

        return null;
    }

    /// <summary>
    /// Tracked load (templates only) → apply → touch parent row so xmin changes →
    /// save (xmin-checked when supplied) → refreshed admin detail.
    /// </summary>
    private async Task<IActionResult> EditItineraryAsync(
        Guid id, uint? clientXmin, Func<Wishlist, Task<IActionResult?>> edit, CancellationToken ct)
    {
        var wishlist = await _db.Wishlists
            .Include(w => w.ItineraryDays)
                .ThenInclude(d => d.ItineraryItems)
            .FirstOrDefaultAsync(w => w.WishlistId == id && w.IsTemplate, ct);
        if (wishlist is null) return NotFound();

        var error = await edit(wishlist);
        if (error is not null) return error;

        wishlist.LastInteractedAt = DateTimeOffset.UtcNow;
        if (clientXmin is not null)
            _db.Entry(wishlist).Property(w => w.xmin).OriginalValue = clientXmin.Value;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentEditConflict();
        }

        var detail = await LoadDetail(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    private IActionResult ReorderError(string message)
    {
        ModelState.AddModelError("Order", message);
        return ValidationProblem(ModelState);
    }

    private async Task ValidateDayRefsAsync(
        Guid? morning, Guid? afternoon, Guid? evening, Guid? transitRoute, CancellationToken ct)
    {
        foreach (var (cityId, field) in new[] { (morning, "MorningCityId"), (afternoon, "AfternoonCityId"), (evening, "EveningCityId") })
        {
            if (cityId is { } c && c != Guid.Empty && !await _db.Cities.AnyAsync(x => x.CityId == c, ct))
                ModelState.AddModelError(field, $"City {c} does not exist.");
        }
        if (transitRoute is { } r && r != Guid.Empty && !await _db.TransitRoutes.AnyAsync(x => x.TransitRouteId == r, ct))
            ModelState.AddModelError("TransitFromPreviousDayRouteId", $"Transit route {r} does not exist.");
    }

    private bool ValidateItemFields(string? title, string? timeOfDay, bool titleRequired)
    {
        if (titleRequired && string.IsNullOrWhiteSpace(title))
            ModelState.AddModelError("Title", "Title is required.");
        else if (title is not null && string.IsNullOrWhiteSpace(title))
            ModelState.AddModelError("Title", "Title cannot be empty.");
        if (title is { Length: > 200 })
            ModelState.AddModelError("Title", "Title must be at most 200 characters.");
        if (!string.IsNullOrEmpty(timeOfDay) && !TimesOfDay.Contains(timeOfDay))
            ModelState.AddModelError("TimeOfDay", "TimeOfDay must be 'Morning', 'Afternoon' or 'Evening'.");
        return ModelState.IsValid;
    }

    /// <summary>TotalDays tracks the itinerary length once it has days; never drops below 1.</summary>
    private static void SyncTotalDays(Wishlist wishlist, int dayCount)
        => wishlist.TotalDays = Math.Max(dayCount, 1);

    private static Guid? NullIfEmpty(Guid? id) => id is null || id == Guid.Empty ? null : id;

    private static List<ItineraryDay> OrderedDays(Wishlist w)
        => w.ItineraryDays.OrderBy(d => d.DayNumber).ToList();

    private static List<ItineraryItem> OrderedItems(ItineraryDay d)
        => d.ItineraryItems.OrderBy(i => i.ItemOrderIndex).ToList();

    private static void Renumber(List<ItineraryDay> days)
    {
        for (var i = 0; i < days.Count; i++)
            if (days[i].DayNumber != i + 1) days[i].DayNumber = i + 1;
    }

    private static void Renumber(List<ItineraryItem> items)
    {
        for (var i = 0; i < items.Count; i++)
            if (items[i].ItemOrderIndex != i + 1) items[i].ItemOrderIndex = i + 1;
    }

    // ══ Featured pool ═════════════════════════════════════════════════════════

    [HttpGet("featured-pool")]
    public async Task<ActionResult<List<AdminFeaturedPoolEntryDto>>> GetFeaturedPool(
        [FromQuery] string? poolType = null,
        CancellationToken ct = default)
    {
        var query = _db.FeaturedWishlistPool.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(poolType))
        {
            var normalized = NormalizePoolType(poolType);
            if (normalized is null)
            {
                ModelState.AddModelError(nameof(poolType), $"PoolType must be one of: {string.Join(", ", PoolTypes)}.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(p => p.PoolType == normalized);
        }

        var entries = await query
            .OrderBy(p => p.PoolType)
            .ThenByDescending(p => p.PaidAmount)
            .ThenBy(p => p.Wishlist.WishlistName)
            .Select(p => new AdminFeaturedPoolEntryDto(
                p.FeaturedWishlistPoolId,
                p.WishlistId,
                p.Wishlist.WishlistName,
                p.PoolType,
                p.PaidAmount,
                p.DailyImpressionLimit,
                p.CurrentImpressionsToday,
                p.RandomSelectionWeight,
                p.LastRotationDate))
            .ToListAsync(ct);

        return Ok(entries);
    }

    [HttpPost("featured-pool")]
    public async Task<ActionResult<AdminFeaturedPoolEntryDto>> CreateFeaturedPoolEntry(CreateFeaturedPoolEntryRequest request, CancellationToken ct = default)
    {
        var poolType = NormalizePoolType(request.PoolType);
        if (poolType is null)
            ModelState.AddModelError(nameof(request.PoolType), $"PoolType must be one of: {string.Join(", ", PoolTypes)}.");

        if (!await _db.Wishlists.AnyAsync(w => w.WishlistId == request.WishlistId && w.IsTemplate, ct))
            ModelState.AddModelError(nameof(request.WishlistId), "Wishlist template not found (only templates can be featured).");

        if (poolType is not null)
            ValidatePoolFields(poolType, request.DailyImpressionLimit, request.RandomSelectionWeight);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (await _db.FeaturedWishlistPool.AnyAsync(p => p.WishlistId == request.WishlistId && p.PoolType == poolType, ct))
            return Conflict(new { error = $"This wishlist is already in the {poolType} pool.", code = "DUPLICATE_POOL_ENTRY" });

        var entry = new FeaturedWishlistPool
        {
            FeaturedWishlistPoolId = Guid.NewGuid(),
            WishlistId = request.WishlistId,
            PoolType = poolType!,
            PaidAmount = request.PaidAmount,
            DailyImpressionLimit = request.DailyImpressionLimit,
            CurrentImpressionsToday = 0,
            RandomSelectionWeight = request.RandomSelectionWeight,
            LastRotationDate = DateTimeOffset.UtcNow,
        };

        _db.FeaturedWishlistPool.Add(entry);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin added wishlist {WishlistId} to {PoolType} pool ({EntryId})", entry.WishlistId, entry.PoolType, entry.FeaturedWishlistPoolId);

        var dto = await LoadPoolEntry(entry.FeaturedWishlistPoolId, ct);
        return CreatedAtAction(nameof(GetFeaturedPool), new { poolType = entry.PoolType }, dto);
    }

    [HttpPatch("featured-pool/{id:guid}")]
    public async Task<ActionResult<AdminFeaturedPoolEntryDto>> UpdateFeaturedPoolEntry(Guid id, UpdateFeaturedPoolEntryRequest request, CancellationToken ct = default)
    {
        var entry = await _db.FeaturedWishlistPool.FirstOrDefaultAsync(p => p.FeaturedWishlistPoolId == id, ct);
        if (entry is null) return NotFound();

        var poolType = entry.PoolType;
        if (request.PoolType is not null)
        {
            var normalized = NormalizePoolType(request.PoolType);
            if (normalized is null)
                ModelState.AddModelError(nameof(request.PoolType), $"PoolType must be one of: {string.Join(", ", PoolTypes)}.");
            else
                poolType = normalized;
        }

        var limit = request.DailyImpressionLimit ?? entry.DailyImpressionLimit;
        var weight = request.RandomSelectionWeight ?? entry.RandomSelectionWeight;
        if (ModelState.IsValid) ValidatePoolFields(poolType, limit, weight);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (poolType != entry.PoolType
            && await _db.FeaturedWishlistPool.AnyAsync(p => p.WishlistId == entry.WishlistId && p.PoolType == poolType, ct))
            return Conflict(new { error = $"This wishlist is already in the {poolType} pool.", code = "DUPLICATE_POOL_ENTRY" });

        entry.PoolType = poolType;
        entry.DailyImpressionLimit = limit;
        entry.RandomSelectionWeight = weight;
        if (request.PaidAmount is not null) entry.PaidAmount = request.PaidAmount.Value;
        if (request.ResetImpressionsToday)
        {
            entry.CurrentImpressionsToday = 0;
            entry.LastRotationDate = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(await LoadPoolEntry(id, ct));
    }

    [HttpDelete("featured-pool/{id:guid}")]
    public async Task<IActionResult> DeleteFeaturedPoolEntry(Guid id, CancellationToken ct = default)
    {
        var deleted = await _db.FeaturedWishlistPool
            .Where(p => p.FeaturedWishlistPoolId == id)
            .ExecuteDeleteAsync(ct);

        if (deleted == 0) return NotFound();

        _logger.LogInformation("Admin removed featured pool entry {EntryId}", id);
        return NoContent();
    }

    private void ValidatePoolFields(string poolType, int dailyImpressionLimit, double randomSelectionWeight)
    {
        // FeaturedWishlistService only serves Paid entries while CurrentImpressionsToday < DailyImpressionLimit.
        if (poolType == "Paid" && dailyImpressionLimit < 1)
            ModelState.AddModelError("DailyImpressionLimit", "Paid entries need DailyImpressionLimit ≥ 1, otherwise they are never shown.");

        // Random entries are drawn by weight; a zero weight is never picked.
        if (poolType == "Random" && randomSelectionWeight <= 0)
            ModelState.AddModelError("RandomSelectionWeight", "Random entries need RandomSelectionWeight > 0.");
    }

    private Task<AdminFeaturedPoolEntryDto?> LoadPoolEntry(Guid id, CancellationToken ct)
        => _db.FeaturedWishlistPool
            .AsNoTracking()
            .Where(p => p.FeaturedWishlistPoolId == id)
            .Select(p => new AdminFeaturedPoolEntryDto(
                p.FeaturedWishlistPoolId,
                p.WishlistId,
                p.Wishlist.WishlistName,
                p.PoolType,
                p.PaidAmount,
                p.DailyImpressionLimit,
                p.CurrentImpressionsToday,
                p.RandomSelectionWeight,
                p.LastRotationDate))
            .FirstOrDefaultAsync(ct);

    private static string? NormalizePoolType(string? value)
        => PoolTypes.FirstOrDefault(p => string.Equals(p, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    private async Task<AdminWishlistDetailDto?> LoadDetail(Guid id, CancellationToken ct)
    {
        var w = await _db.Wishlists
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.WishlistDestinations).ThenInclude(wd => wd.Destination)
            .Include(x => x.ItineraryDays).ThenInclude(d => d.ItineraryItems)
            .Include(x => x.ItineraryDays).ThenInclude(d => d.MorningCity)
            .Include(x => x.ItineraryDays).ThenInclude(d => d.AfternoonCity)
            .Include(x => x.ItineraryDays).ThenInclude(d => d.EveningCity)
            .Include(x => x.CreatorAttributions).ThenInclude(a => a.Creator)
            .Include(x => x.FeaturedPoolEntries)
            .FirstOrDefaultAsync(x => x.WishlistId == id && x.IsTemplate, ct);
        if (w is null) return null;

        var forkCount = await _db.Wishlists.CountAsync(f => f.ForkedFromId == id, ct);
        var partnerLinkCount = await _db.PartnerLinks.CountAsync(l => l.WishlistId == id, ct);
        var tripPostCount = await _db.TripPosts.CountAsync(t => t.WishlistId == id, ct);

        return new AdminWishlistDetailDto(
            w.WishlistId,
            w.WishlistName,
            w.WishlistDescription,
            w.ShortStory,
            w.TotalDays,
            w.PeopleType,
            w.WishlistHeroImage,
            ParseList(w.GlobalInclusionsJson),
            w.RawContentKeywords,
            ParseList(w.PsychologicalVibeTagsJson),
            w.DefaultTravelersCount,
            w.BasePricePerPerson,
            w.CalculatedTotalCost,
            w.DepositAmountRequired,
            w.AccommodationInclusions,
            w.TransitInclusions,
            w.ActivityInclusions,
            w.TotalGlobalSaveCount,
            w.IsFeatured,
            w.FeaturedUntil,
            w.PrimaryPersonaTarget,
            w.CreatedAt,
            w.LastInteractedAt,
            forkCount,
            partnerLinkCount,
            tripPostCount,
            w.xmin,
            w.WishlistDestinations
                .OrderBy(wd => wd.Destination.DestinationName)
                .Select(wd => new AdminWishlistDestinationDto(wd.DestinationId, wd.Destination.DestinationName))
                .ToList(),
            w.ItineraryDays
                .OrderBy(d => d.DayNumber)
                .Select(d => new AdminItineraryDayDto(
                    d.ItineraryDayId,
                    d.DayNumber,
                    d.DayTitle,
                    d.MorningCityId,
                    d.MorningCity?.CityName,
                    d.AfternoonCityId,
                    d.AfternoonCity?.CityName,
                    d.EveningCityId,
                    d.EveningCity?.CityName,
                    d.TransitFromPreviousDayRouteId,
                    d.ItineraryItems
                        .OrderBy(i => i.ItemOrderIndex)
                        .Select(i => new AdminItineraryItemDto(
                            i.ItineraryItemId,
                            i.ItemOrderIndex,
                            i.ItemTitle,
                            i.ItemDescription,
                            i.TimeOfDay,
                            i.ImageUrl,
                            i.SocialProofBadge,
                            i.IndividualCostModifier,
                            i.IsOptionalActivity,
                            i.IsSelectedByDefault))
                        .ToList()))
                .ToList(),
            w.CreatorAttributions
                .OrderBy(a => a.PermissionGrantedAt)
                .Select(a => new AdminAttributionDto(
                    a.WishlistCreatorAttributionId,
                    a.CreatorId,
                    a.Creator.DisplayName,
                    a.Creator.Handle,
                    a.Creator.PlatformName,
                    a.Creator.IsVerified,
                    a.OriginalContentUrl,
                    a.PermissionType,
                    a.PermissionGrantedAt,
                    a.PermissionEvidenceUrl,
                    a.IsActive,
                    a.AttributionNote,
                    a.CreatedAt))
                .ToList(),
            w.FeaturedPoolEntries
                .OrderBy(p => p.PoolType)
                .Select(p => new AdminFeaturedPoolEntryDto(
                    p.FeaturedWishlistPoolId,
                    p.WishlistId,
                    w.WishlistName,
                    p.PoolType,
                    p.PaidAmount,
                    p.DailyImpressionLimit,
                    p.CurrentImpressionsToday,
                    p.RandomSelectionWeight,
                    p.LastRotationDate))
                .ToList());
    }

    /// <summary>Distinct destination ids, each validated to exist. Null in → null out.</summary>
    private async Task<List<Guid>?> ValidateDestinationIds(List<Guid>? ids, CancellationToken ct)
    {
        if (ids is null) return null;

        var distinct = ids.Distinct().ToList();
        if (distinct.Contains(Guid.Empty))
        {
            ModelState.AddModelError("DestinationIds", "DestinationIds cannot contain an empty id.");
            return distinct;
        }
        if (distinct.Count == 0) return distinct;

        var found = await _db.Destinations
            .Where(d => distinct.Contains(d.DestinationId))
            .Select(d => d.DestinationId)
            .ToListAsync(ct);

        var missing = distinct.Except(found).ToList();
        if (missing.Count > 0)
            ModelState.AddModelError("DestinationIds", $"Destination(s) not found: {string.Join(", ", missing)}.");

        return distinct;
    }

    /// <summary>Trims entries, drops blanks and duplicates (case-insensitive). Null in → null out.</summary>
    private List<string>? CleanList(List<string>? values, string field)
    {
        if (values is null) return null;

        var cleaned = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count > 100)
            ModelState.AddModelError(field, "At most 100 entries are allowed.");
        if (cleaned.Any(v => v.Length > 200))
            ModelState.AddModelError(field, "Each entry must be at most 200 characters.");

        return cleaned;
    }

    private static List<string> ParseList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private ObjectResult ConcurrentEditConflict()
        => Conflict(new { error = "Conflict: this wishlist was updated by someone else. Please refresh and reapply your changes.", code = "CONCURRENT_EDIT" });

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
