using Hodrac_Backend_MVP2.DTOs.WishlistDtos;
using Hodrac_Backend_MVP2.Infrastructure.SignalR;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
///
/// Itinerary editing (owner or Editor; templates are never editable). Each returns the
/// refreshed WishlistDetailDto and broadcasts "WishlistUpdated" to the wishlist group.
/// All accept an optional ?xmin= (from the last GET) for 409 conflict detection.
/// POST   /api/user-wishlists/{id}/itinerary/days
/// PUT    /api/user-wishlists/{id}/itinerary/days/{dayId}
/// DELETE /api/user-wishlists/{id}/itinerary/days/{dayId}
/// POST   /api/user-wishlists/{id}/itinerary/days/{dayId}/items
/// PUT    /api/user-wishlists/{id}/itinerary/days/{dayId}/items/{itemId}
/// DELETE /api/user-wishlists/{id}/itinerary/days/{dayId}/items/{itemId}
/// PUT    /api/user-wishlists/{id}/itinerary/order
///
/// Public read-only sharing (opt-in, owner only; templates are already public):
/// POST   /api/user-wishlists/{id}/share   — create (or return the existing) share link
/// DELETE /api/user-wishlists/{id}/share   — revoke the share link
/// GET    /api/shared/wishlists/{token}    — anonymous read-only view of a shared wishlist
/// </summary>
[ApiController]
[Route("api/user-wishlists")]
public class UserWishlistsController : ControllerBase
{
    private static readonly string[] TimesOfDay = { "Morning", "Afternoon", "Evening" };

    private readonly IWishlistRepository _wishlists;
    private readonly ICollaboratorRepository _collaborators;
    private readonly ICurrentUserService _currentUser;
    private readonly IHubContext<WishlistHub> _wishlistHub;
    private readonly IPartnerService _partners;
    private readonly IShareService _share;

    public UserWishlistsController(
        IWishlistRepository wishlists,
        ICollaboratorRepository collaborators,
        ICurrentUserService currentUser,
        IHubContext<WishlistHub> wishlistHub,
        IPartnerService partners,
        IShareService share)
    {
        _share = share;
        _wishlists     = wishlists;
        _collaborators = collaborators;
        _currentUser   = currentUser;
        _wishlistHub = wishlistHub;
        _partners = partners;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var wishlists = await _wishlists.GetUserWishlistsAsync(_currentUser.UserId, ct);
        return Ok(await _partners.AttachAsync(wishlists.Select(WishlistsController.MapToCard), ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var wishlist = await _wishlists.GetUserWishlistByIdAsync(id, _currentUser.UserId, ct);
        if (wishlist is null) return NotFound();
        var snapshot = await _wishlists.GetActiveSnapshotAsync(id, ct);
        return Ok(await _partners.AttachAsync(WishlistsController.MapToDetail(wishlist, _currentUser.UserId, snapshot), ct));
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

        try
        {
            // Only the wishlist row's scalar columns are loaded and written; the client's
            // xmin (when supplied) is the concurrency token, so a stale edit → 409.
            var found = await _wishlists.UpdateAsync(id, wishlist =>
            {
                if (request.WishlistName is not null)        wishlist.WishlistName        = request.WishlistName;
                if (request.WishlistDescription is not null) wishlist.WishlistDescription = request.WishlistDescription;
                if (request.ShortStory is not null)          wishlist.ShortStory          = request.ShortStory;
                if (request.DefaultTravelersCount.HasValue)  wishlist.DefaultTravelersCount = request.DefaultTravelersCount.Value;
                if (request.GlobalInclusions is not null)
                    wishlist.GlobalInclusionsJson = JsonSerializer.Serialize(request.GlobalInclusions);

                wishlist.LastInteractedAt = DateTimeOffset.UtcNow;
            }, request.Xmin, ct);

            if (!found) return NotFound();

            var dto = await BroadcastDetailAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentEditConflict();
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
        return Ok(await _partners.AttachAsync(wishlists.Select(wishlist => WishlistsController.MapToCard(wishlist)), ct));
    }

    // ══ Public share link ═════════════════════════════════════════════════════

    // ── POST /api/user-wishlists/{id}/share ──────────────────────────────────
    // Idempotent: returns the existing token if the wishlist is already shared.

    [HttpPost("{id:guid}/share")]
    public async Task<IActionResult> EnableShare(Guid id, CancellationToken ct = default)
    {
        var (state, error) = await GetOwnedShareStateAsync(id, ct);
        if (error is not null) return error;

        if (state!.ShareToken is null)
        {
            state = await _wishlists.EnableShareAsync(id, ShareService.NewShareToken(), ct);
            if (state?.ShareToken is null) return NotFound();
        }

        return Ok(new WishlistShareLinkDto(
            state.ShareToken,
            _share.BuildFrontendUrl(ShareTypes.SharedWishlist, state.ShareToken),
            state.ShareEnabledAt));
    }

    // ── DELETE /api/user-wishlists/{id}/share ────────────────────────────────

    [HttpDelete("{id:guid}/share")]
    public async Task<IActionResult> DisableShare(Guid id, CancellationToken ct = default)
    {
        var (_, error) = await GetOwnedShareStateAsync(id, ct);
        if (error is not null) return error;

        return await _wishlists.DisableShareAsync(id, ct) ? NoContent() : NotFound();
    }

    // ── GET /api/shared/wishlists/{token} ────────────────────────────────────
    // Anonymous, read-only. Viewer-specific flags are always off (role "None")
    // and the share token itself is never echoed back.

    [HttpGet("/api/shared/wishlists/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-browse")]
    public async Task<IActionResult> GetSharedByToken(string token, CancellationToken ct = default)
    {
        if (!ShareService.IsValidShareToken(token)) return NotFound();

        var wishlist = await _wishlists.GetBySharedTokenAsync(token, ct);
        if (wishlist is null) return NotFound();

        var snapshot = await _wishlists.GetActiveSnapshotAsync(wishlist.WishlistId, ct);
        return Ok(await _partners.AttachAsync(WishlistsController.MapToDetail(wishlist, Guid.Empty, snapshot), ct));
    }

    /// <summary>
    /// Owner-only gate for share management: 401 anonymous, 404 missing (or no access at all),
    /// 400 template, 403 collaborator who isn't the owner.
    /// </summary>
    private async Task<(WishlistShareState? State, IActionResult? Error)> GetOwnedShareStateAsync(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return (null, Unauthorized());

        var state = await _wishlists.GetShareStateAsync(id, ct);
        if (state is null) return (null, NotFound());
        if (state.IsTemplate)
            return (null, BadRequest("Platform wishlists are already public; share them via /api/share/wishlist/{id}."));

        if (state.OwnerUserId != _currentUser.UserId)
        {
            var role = await _collaborators.GetRoleAsync(id, _currentUser.UserId, ct);
            return (null, role is null ? NotFound() : Forbid());
        }

        return (state, null);
    }

    // ══ Itinerary editing ═════════════════════════════════════════════════════

    // ── POST /api/user-wishlists/{id}/itinerary/days ─────────────────────────

    [HttpPost("{id:guid}/itinerary/days")]
    public Task<IActionResult> AddDay(
        Guid id,
        [FromBody] AddItineraryDayRequest request,
        [FromQuery] uint? xmin,
        CancellationToken ct = default)
        => EditItineraryAsync(id, xmin, async wishlist =>
        {
            if (request.DayTitle is { Length: > 200 }) return BadRequest("DayTitle must be at most 200 characters.");
            var refError = await ValidateDayRefsAsync(
                request.MorningCityId, request.AfternoonCityId, request.EveningCityId,
                request.TransitFromPreviousDayRouteId, ct);
            if (refError is not null) return refError;

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
            _wishlists.AddItineraryDay(day);

            days.Insert(position - 1, day);
            Renumber(days);
            wishlist.TotalDays = days.Count;
            return null;
        }, ct);

    // ── PUT /api/user-wishlists/{id}/itinerary/days/{dayId} ──────────────────

    [HttpPut("{id:guid}/itinerary/days/{dayId:guid}")]
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
            if (request.DayTitle is { Length: > 200 }) return BadRequest("DayTitle must be at most 200 characters.");

            var refError = await ValidateDayRefsAsync(
                request.MorningCityId, request.AfternoonCityId, request.EveningCityId,
                request.TransitFromPreviousDayRouteId, ct);
            if (refError is not null) return refError;

            if (request.DayTitle is not null) day.DayTitle = request.DayTitle;
            if (request.MorningCityId.HasValue) day.MorningCityId = NullIfEmpty(request.MorningCityId);
            if (request.AfternoonCityId.HasValue) day.AfternoonCityId = NullIfEmpty(request.AfternoonCityId);
            if (request.EveningCityId.HasValue) day.EveningCityId = NullIfEmpty(request.EveningCityId);
            if (request.TransitFromPreviousDayRouteId.HasValue)
                day.TransitFromPreviousDayRouteId = NullIfEmpty(request.TransitFromPreviousDayRouteId);
            return null;
        }, ct);

    // ── DELETE /api/user-wishlists/{id}/itinerary/days/{dayId} ───────────────

    [HttpDelete("{id:guid}/itinerary/days/{dayId:guid}")]
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
                _wishlists.RemoveItineraryItem(item);
            _wishlists.RemoveItineraryDay(day);

            var days = OrderedDays(wishlist).Where(d => d.ItineraryDayId != dayId).ToList();
            Renumber(days);
            wishlist.TotalDays = days.Count;
            return Task.FromResult<IActionResult?>(null);
        }, ct);

    // ── POST /api/user-wishlists/{id}/itinerary/days/{dayId}/items ───────────

    [HttpPost("{id:guid}/itinerary/days/{dayId:guid}/items")]
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

            var error = ValidateItemFields(request.Title, request.TimeOfDay, titleRequired: true);
            if (error is not null) return Task.FromResult<IActionResult?>(error);

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
            _wishlists.AddItineraryItem(item);

            items.Insert(position - 1, item);
            Renumber(items);
            return Task.FromResult<IActionResult?>(null);
        }, ct);

    // ── PUT /api/user-wishlists/{id}/itinerary/days/{dayId}/items/{itemId} ───

    [HttpPut("{id:guid}/itinerary/days/{dayId:guid}/items/{itemId:guid}")]
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

            var error = ValidateItemFields(request.Title, request.TimeOfDay, titleRequired: false);
            if (error is not null) return Task.FromResult<IActionResult?>(error);

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

    // ── DELETE /api/user-wishlists/{id}/itinerary/days/{dayId}/items/{itemId} ─

    [HttpDelete("{id:guid}/itinerary/days/{dayId:guid}/items/{itemId:guid}")]
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

            _wishlists.RemoveItineraryItem(item);
            Renumber(OrderedItems(day).Where(i => i.ItineraryItemId != itemId).ToList());
            return Task.FromResult<IActionResult?>(null);
        }, ct);

    // ── PUT /api/user-wishlists/{id}/itinerary/order ─────────────────────────

    [HttpPut("{id:guid}/itinerary/order")]
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

        // ── Validate days ──
        if (request.DayOrder is not null)
        {
            if (request.DayOrder.Count != daysById.Count
                || request.DayOrder.Distinct().Count() != request.DayOrder.Count
                || request.DayOrder.Any(d => !daysById.ContainsKey(d)))
                return BadRequest("DayOrder must list every day of this wishlist exactly once.");
        }

        // ── Validate items ──
        var itemsById = wishlist.ItineraryDays
            .SelectMany(d => d.ItineraryItems)
            .ToDictionary(i => i.ItineraryItemId);

        if (request.ItemOrder is not null)
        {
            if (request.ItemOrder.Any(e => e.ItemIds is null))
                return BadRequest("ItemIds is required for every ItemOrder entry.");
            if (request.ItemOrder.Select(e => e.DayId).Distinct().Count() != request.ItemOrder.Count)
                return BadRequest("Each day may appear only once in ItemOrder.");
            if (request.ItemOrder.Any(e => !daysById.ContainsKey(e.DayId)))
                return BadRequest("ItemOrder references a day that is not in this wishlist.");

            var listed = request.ItemOrder.SelectMany(e => e.ItemIds).ToList();
            if (listed.Distinct().Count() != listed.Count)
                return BadRequest("An item may appear only once in ItemOrder.");
            if (listed.Any(i => !itemsById.ContainsKey(i)))
                return BadRequest("ItemOrder references an item that is not in this wishlist.");

            var listedSet = listed.ToHashSet();
            var orphaned = request.ItemOrder
                .SelectMany(e => daysById[e.DayId].ItineraryItems)
                .Any(i => !listedSet.Contains(i.ItineraryItemId));
            if (orphaned)
                return BadRequest("Each listed day must include all of its items (list moved items under their new day).");
        }

        // ── Apply ──
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

            // Days that lost items but weren't listed: close the gaps.
            foreach (var source in sourceDays.Where(d => request.ItemOrder.All(e => e.DayId != d.ItineraryDayId)))
                Renumber(OrderedItems(source));
        }

        return null;
    }

    // ── Itinerary helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Shared pipeline for itinerary edits: auth → role (owner/Editor) → tracked load
    /// (templates excluded) → apply → save (xmin-checked) → broadcast refreshed detail.
    /// The edit returns an error result to abort without saving, or null on success.
    /// </summary>
    private async Task<IActionResult> EditItineraryAsync(
        Guid id, uint? clientXmin, Func<Wishlist, Task<IActionResult?>> edit, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var role = await _collaborators.GetRoleAsync(id, _currentUser.UserId, ct);
        if (role is null || role == "Viewer") return Forbid();

        var wishlist = await _wishlists.GetForItineraryEditAsync(id, ct);
        if (wishlist is null) return NotFound();

        var error = await edit(wishlist);
        if (error is not null) return error;

        // Touch the parent row so the wishlist's xmin changes on every itinerary edit;
        // this makes a stale client xmin (here or on PUT) fail with 409.
        wishlist.LastInteractedAt = DateTimeOffset.UtcNow;
        if (clientXmin is not null)
            _wishlists.SetXminOriginalValue(wishlist, clientXmin.Value);

        try
        {
            await _wishlists.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentEditConflict();
        }

        var dto = await BroadcastDetailAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Reloads the detail DTO (partner attached) and pushes it to the wishlist's group.</summary>
    private async Task<WishlistDetailDto?> BroadcastDetailAsync(Guid id, CancellationToken ct)
    {
        var updated = await _wishlists.GetUserWishlistByIdAsync(id, _currentUser.UserId, ct);
        if (updated is null) return null;

        var snapshot = await _wishlists.GetActiveSnapshotAsync(id, ct);
        var dto = await _partners.AttachAsync(WishlistsController.MapToDetail(updated, _currentUser.UserId, snapshot), ct);
        // The group includes collaborators: never broadcast the owner's share token.
        await _wishlistHub.Clients
            .Group(id.ToString())
            .SendAsync(
                "WishlistUpdated",
                dto with { ShareToken = null }, ct
            );
        return dto;
    }

    private ObjectResult ConcurrentEditConflict()
        => Conflict(new { error = "Conflict: this wishlist was updated by another collaborator. Please refresh and reapply your changes.", code = "CONCURRENT_EDIT" });

    private async Task<IActionResult?> ValidateDayRefsAsync(
        Guid? morning, Guid? afternoon, Guid? evening, Guid? transitRoute, CancellationToken ct)
    {
        foreach (var cityId in new[] { morning, afternoon, evening })
        {
            if (cityId is { } c && c != Guid.Empty && !await _wishlists.CityExistsAsync(c, ct))
                return BadRequest($"City {c} does not exist.");
        }
        if (transitRoute is { } r && r != Guid.Empty && !await _wishlists.TransitRouteExistsAsync(r, ct))
            return BadRequest($"Transit route {r} does not exist.");
        return null;
    }

    private IActionResult? ValidateItemFields(string? title, string? timeOfDay, bool titleRequired)
    {
        if (titleRequired && string.IsNullOrWhiteSpace(title)) return BadRequest("Title is required.");
        if (title is not null && string.IsNullOrWhiteSpace(title)) return BadRequest("Title cannot be empty.");
        if (title is { Length: > 200 }) return BadRequest("Title must be at most 200 characters.");
        if (!string.IsNullOrEmpty(timeOfDay) && !TimesOfDay.Contains(timeOfDay))
            return BadRequest("TimeOfDay must be 'Morning', 'Afternoon' or 'Evening'.");
        return null;
    }

    private static Guid? NullIfEmpty(Guid? id) => id is null || id == Guid.Empty ? null : id;

    private static List<ItineraryDay> OrderedDays(Wishlist w)
        => w.ItineraryDays.OrderBy(d => d.DayNumber).ToList();

    private static List<ItineraryItem> OrderedItems(ItineraryDay d)
        => d.ItineraryItems.OrderBy(i => i.ItemOrderIndex).ToList();

    // Only rows whose number actually changes are marked modified.
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
