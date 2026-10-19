using Hodrac.DTOs;
using Hodrac_Backend_MVP2.DTOs.WishlistDtos;
using Hodrac_Backend_MVP2.Events;
using Hodrac_Backend_MVP2.Infrastructure.SignalR;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// GET  /api/wishlists                    — all platform templates (paginated)
/// GET  /api/wishlists/{id}               — single template detail
/// GET  /api/wishlists/popular            — segmented-popular ranked list
/// GET  /api/wishlists/featured           — rotation (paid + editorial + random)
/// GET  /api/wishlists/similar/{id}       — same PeopleType, different wishlist
/// POST /api/wishlists/{id}/fork          — create user's editable copy
/// POST /api/wishlists/{id}/save          — bookmark a wishlist
/// DELETE /api/wishlists/{id}/save        — remove bookmark
/// </summary>
[ApiController]
[Route("api/wishlists")]
public class WishlistsController : ControllerBase
{
    private readonly IWishlistRepository _wishlists;
    private readonly PopularWishlistService _popularService;
    private readonly FeaturedWishlistService _featuredService;
    private readonly ICollaboratorRepository _collaborators;
    private readonly ICurrentUserService _currentUser;
    private readonly IHubContext<WishlistHub> _wishlistHub;
    private readonly IPartnerService _partners;
    private readonly IBehaviorEventQueue _events;
    private readonly IProfileRepository _profiles;
    private readonly ILogger<WishlistsController> _logger;

    public WishlistsController(
        IWishlistRepository wishlists,
        PopularWishlistService popularService,
        FeaturedWishlistService featuredService,
        ICollaboratorRepository collaborators,
        ICurrentUserService currentUser,
        IHubContext<WishlistHub> wishlistHub,
        IPartnerService partners,
        IBehaviorEventQueue events,
        IProfileRepository profiles,
        ILogger<WishlistsController> logger)
    {
        _events = events;
        _profiles = profiles;
        _logger = logger;
        _wishlists = wishlists;
        _popularService = popularService;
        _featuredService = featuredService;
        _collaborators = collaborators;
        _currentUser = currentUser;
        _wishlistHub = wishlistHub;
        _partners = partners;
    }

    // ── GET /api/wishlists ─────────────────────────────────
    [HttpGet("full")]
    public async Task<IActionResult> GetFull()
    {
        var wishlists = await _wishlists.GetAllWishlists();
        if (wishlists.IsNullOrEmpty())
            return NotFound();
        return Ok(await _partners.AttachAsync(wishlists.Select(MapToCard)));
    }


    // ── GET /api/wishlists?page=1&pageSize=12 ─────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await _wishlists.GetTemplatesAsync(page, pageSize, ct);

        return Ok(new
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            Items = await _partners.AttachAsync(items.Select(MapToCard), ct)
        });
    }

    // ── GET /api/wishlists/{id} ───────────────────────────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var wishlist = await _wishlists.GetTemplateByIdAsync(id, ct);
        if (wishlist is null) return NotFound();

        var activeSnapshot = await _wishlists.GetActiveSnapshotAsync(id, ct);
        return Ok(await _partners.AttachAsync(MapToDetail(wishlist, _currentUser.UserId, activeSnapshot), ct));
    }

    // ── GET /api/wishlists/popular?limit=20 ───────────────────────────────────
    // Personalised for the JWT caller when they have a Mongo profile; otherwise
    // (anonymous / no profile) top-saved templates. `user_id` is accepted for
    // backwards compatibility but only used when the request is anonymous.

    [HttpGet("popular")]
    public async Task<IActionResult> GetPopular(
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        var effectiveUserId = _currentUser.IsAuthenticated ? _currentUser.UserIdString : userId;
        var rankedIds = await _popularService.GetRankedWishlistIdsAsync(effectiveUserId, limit, ct);
        var wishlists = await _wishlists.GetPopularTemplatesAsync(rankedIds, limit, ct);
        return Ok(await _partners.AttachAsync(wishlists.Select(MapToCard), ct));
    }

    // ── GET /api/wishlists/featured ───────────────────────────────────────────

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeatured(CancellationToken ct = default)
    {
        var featuredIds = await _featuredService.GetFeaturedWishlistIdsAsync(ct);
        var wishlists = await _wishlists.GetFeaturedAsync(featuredIds, ct);
        return Ok(await _partners.AttachAsync(wishlists.Select(MapToCard), ct));
    }

    // ── GET /api/wishlists/similar/{id}?limit=6 ───────────────────────────────

    [HttpGet("similar/{id:guid}")]
    public async Task<IActionResult> GetSimilar(
        Guid id,
        [FromQuery] int limit = 6,
        CancellationToken ct = default)
    {
        var wishlists = await _wishlists.GetSimilarAsync(id, limit, ct);
        return Ok(await _partners.AttachAsync(wishlists.Select(MapToCard), ct));
    }

    // ── GET /api/wishlist-with-destination/similar/{id}?limit=6 ───────────────────────────────

    [HttpGet("wishlist-with-destination/{id:guid}")]
    public async Task<IActionResult> GetWishlistWithDestination(
        Guid id,
        [FromQuery] int limit = 6,
        CancellationToken ct = default)
    {
        var wishlists = await _wishlists.GetWishlistWithDestinationAsync(id, limit, ct);
        return Ok(await _partners.AttachAsync(wishlists.Select(MapToCard), ct));
    }

    // ── POST /api/wishlists/{id}/fork ─────────────────────────────────────────

    [HttpPost("{id:guid}/fork")]
    public async Task<IActionResult> Fork(Guid id, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        try
        {
            var fork = await _wishlists.ForkAsync(id, _currentUser.UserId, ct);
            return CreatedAtAction(
                nameof(UserWishlistsController.GetById),
                "UserWishlists",
                new { id = fork.WishlistId },
                new ForkWishlistResponseDto(
                    fork.WishlistId,
                    "Wishlist forked successfully. You can now edit your own copy.")
            );
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // ── POST /api/wishlists/{id}/save ─────────────────────────────────────────

    // Only platform templates can be saved. Missing or user-owned (forked)
    // wishlists -> 404 before anything is written.

    [HttpPost("{id:guid}/save")]
    public async Task<IActionResult> Save(Guid id, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (await _wishlists.GetTemplateCardAsync(id, ct) is null) return NotFound("Wishlist not found.");

        var saved = await _wishlists.SaveWishlistAsync(id, _currentUser.UserId, ct);
        if (!saved) return Conflict("Wishlist already saved.");
        await _wishlists.IncrementSaveCountAsync(id, ct);
        var wishlist = await _wishlists.GetTemplateCardAsync(id, ct);
        if (wishlist is null) return NotFound("Wishlist not found.");   // deleted mid-request
        var dto = (await _partners.AttachAsync(new[] { MapToCard(wishlist) }, ct))[0];
        await _wishlistHub.Clients
            .Group(id.ToString())
            .SendAsync(
                "WishlistUpdated",
                dto,
                ct
            );
        await PublishWishlistSavedAsync(wishlist);
        return Ok(new { message = "Wishlist saved." });
    }

    /// <summary>
    /// Feeds the behavioural engine (best-effort — never fails the request).
    /// BudgetProfile comes from the user's Mongo onboarding answers when available.
    /// </summary>
    private async Task PublishWishlistSavedAsync(Wishlist wishlist)
    {
        try
        {
            var profile = await ProfileTagSignals.TryLoadProfileAsync(_profiles, _currentUser, _logger);
            _events.Publish(new WishlistSavedEvent(
                UserId: _currentUser.UserIdString,
                WishlistId: wishlist.WishlistId.ToString(),
                WishlistTags: ProfileTagSignals.ParseTagJson(wishlist.PsychologicalVibeTagsJson),
                PeopleType: wishlist.PeopleType,
                BudgetProfile: profile?.ExplicitOnboardingAnswers?.BudgetProfile ?? string.Empty,
                OccurredAt: DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish WishlistSavedEvent for {WishlistId}", wishlist.WishlistId);
        }
    }

    // ── DELETE /api/wishlists/{id}/save ───────────────────────────────────────

    [HttpDelete("{id:guid}/save")]
    public async Task<IActionResult> Unsave(Guid id, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (await _wishlists.GetTemplateCardAsync(id, ct) is null) return NotFound("Wishlist not found.");

        var removed = await _wishlists.UnsaveWishlistAsync(id, _currentUser.UserId, ct);
        if (!removed) return NotFound("Wishlist was not saved.");
        await _wishlists.DecrementSaveCountAsync(id, ct);
        var wishlist = await _wishlists.GetTemplateCardAsync(id, ct);
        if (wishlist is null) return Ok(new { message = "Wishlist removed from saved." });
        var dto = (await _partners.AttachAsync(new[] { MapToCard(wishlist) }, ct))[0];
        await _wishlistHub.Clients
            .Group(id.ToString())
            .SendAsync(
                "WishlistUpdated",
                dto,
                ct
            );
        return Ok(new { message = "Wishlist removed from saved." });
    }

    // ── GET /api/wishlists/{id}/collaborators ─────────────────────────────────
    // Returns the collaborator list for a wishlist.
    // Visible to the owner and any existing collaborator.

    [HttpGet("{id:guid}/collaborators")]
    public async Task<IActionResult> GetCollaborators(
        Guid id, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        // Verify caller has access (is owner or collaborator)
        var role = await _collaborators.GetRoleAsync(id, _currentUser.UserId, ct);
        if (role is null) return Forbid();

        var collaborators = await _collaborators.GetCollaboratorsAsync(id, ct);
        return Ok(collaborators.Select(c => new CollaboratorDto(
            c.UserId, c.SharedUserEmail, c.Role, c.JoinedAt)));
    }

    // ── GET /api/wishlists/{id}/pricing-snapshot ──────────────────────────────
    // Returns the active (non-expired) pricing snapshot for the frontend countdown timer.
    // Returns 404 if no active snapshot — frontend should prompt user to generate one.

    [HttpGet("{id:guid}/pricing-snapshot")]
    public async Task<IActionResult> GetPricingSnapshot(
        Guid id, CancellationToken ct = default)
    {
        var snapshot = await _wishlists.GetActiveSnapshotAsync(id, ct);
        if (snapshot is null)
            return NotFound(new { message = "No active pricing snapshot. POST to generate one." });

        return Ok(MapSnapshot(snapshot));
    }

    // ── POST /api/wishlists/{id}/pricing-snapshot ─────────────────────────────
    // Generates a new pricing snapshot (starts the "Secure your spot" countdown).
    // ValidUntil = now + 48 hours by default; override with ?hours=N.

    [HttpPost("{id:guid}/pricing-snapshot")]
    public async Task<IActionResult> CreatePricingSnapshot(
        Guid id,
        [FromBody] CreateSnapshotRequest request,
        [FromQuery] int hours = 48,
        CancellationToken ct = default)
    {
        var wishlist = await _wishlists.GetTemplateByIdAsync(id, ct);
        if (wishlist is null) return NotFound("Wishlist not found.");

        var travelers = request.TravelersCount ?? wishlist.DefaultTravelersCount;
        var optionalTotal = request.OptionalActivitiesTotal ?? 0;
        var surcharge = request.SeasonalSurchargePercent ?? 0;

        var snapshot = new Models.WishlistPricingSnapshot
        {
            WishlistId = id,
            TravelersCount = travelers,
            BasePricePerPerson = wishlist.BasePricePerPerson,
            OptionalActivitiesTotal = optionalTotal,
            SeasonalSurchargePercent = surcharge,
            DepositAmountRequired = wishlist.DepositAmountRequired,
            ValidUntil = DateTimeOffset.UtcNow.AddHours(hours),
        };

        var created = await _wishlists.CreateSnapshotAsync(snapshot, ct);
        return CreatedAtAction(nameof(GetPricingSnapshot), new { id }, MapSnapshot(created));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Placeholder — replace with: HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
    // ── DTO mappers ───────────────────────────────────────────────────────────

    internal static WishlistCardDto MapToCard(Wishlist w) => new(
        WishlistId: w.WishlistId,
        WishlistName: w.WishlistName,
        WishlistDescription: w.WishlistDescription,
        ShortStory: w.ShortStory,
        WishlistHeroImage: w.WishlistHeroImage,
        TotalDays: w.TotalDays,
        PeopleType: w.PeopleType,
        BasePricePerPerson: w.BasePricePerPerson,
        CalculatedTotalCost: w.CalculatedTotalCost,
        TotalGlobalSaveCount: w.TotalGlobalSaveCount,
        IsFeatured: w.IsFeatured,
        PsychologicalVibeTags: JsonSerializer.Deserialize<List<string>>(w.PsychologicalVibeTagsJson)
                                ?? new List<string>(),
        PrimaryPersonaTarget: w.PrimaryPersonaTarget
    );

    internal static WishlistDetailDto MapToDetail(
        Wishlist w, Guid callerId, WishlistPricingSnapshot? snapshot)
    {
        var callerCollaborator = w.Collaborators.FirstOrDefault(c => c.UserId == callerId);
        var userRole = callerCollaborator?.Role ?? "None";

        return new WishlistDetailDto(
            WishlistId: w.WishlistId,
            WishlistName: w.WishlistName,
            WishlistDescription: w.WishlistDescription,
            ShortStory: w.ShortStory,
            WishlistHeroImage: w.WishlistHeroImage,
            TotalDays: w.TotalDays,
            PeopleType: w.PeopleType,
            BasePricePerPerson: w.BasePricePerPerson,
            CalculatedTotalCost: w.CalculatedTotalCost,
            DepositAmountRequired: w.DepositAmountRequired,
            TotalGlobalSaveCount: w.TotalGlobalSaveCount,
            GlobalInclusions: JsonSerializer.Deserialize<List<string>>(w.GlobalInclusionsJson)
                                   ?? new List<string>(),
            PsychologicalVibeTags: JsonSerializer.Deserialize<List<string>>(w.PsychologicalVibeTagsJson)
                                   ?? new List<string>(),
            ItineraryDays: w.ItineraryDays.OrderBy(d => d.DayNumber).Select(MapDay).ToList(),
            ActivePricingSnapshot: snapshot is null ? null : MapSnapshot(snapshot),
            IsUserOwner: w.OwnerUserId == callerId,
            IsCollaborator: callerCollaborator is not null,
            UserRole: userRole,
            CreatorAttributions: w.CreatorAttributions
                                    .Where(a => a.IsActive)
                                    .OrderBy(a => a.PermissionGrantedAt)
                                    .Select(a => new WishlistCreatorAttributionDto(
                                        a.WishlistCreatorAttributionId,
                                        a.WishlistId,
                                        new CreatorDto(
                                            a.Creator.CreatorId,
                                            a.Creator.DisplayName,
                                            a.Creator.Handle,
                                            a.Creator.PlatformName,
                                            a.Creator.ProfileUrl,
                                            a.Creator.AvatarUrl,
                                            a.Creator.Bio,
                                            a.Creator.IsVerified
                                        ),
                                        a.OriginalContentUrl,
                                        a.PermissionType,
                                        a.PermissionGrantedAt,
                                        a.PermissionEvidenceUrl,
                                        a.IsActive,
                                        a.AttributionNote,
                                        a.CreatedAt
                                    ))
                                    .ToList(),
            Xmin: w.xmin
        );
    }

    private static ItineraryDayDto MapDay(ItineraryDay day) => new(
        DayId: day.ItineraryDayId,
        DayNumber: day.DayNumber,
        DayTitle: day.DayTitle,
        MorningCity: day.MorningCity?.CityName,
        AfternoonCity: day.AfternoonCity?.CityName,
        EveningCity: day.EveningCity?.CityName,
        TransitFromPreviousDay: day.TransitFromPreviousDayRoute is null ? null : new TransitRouteDto(
            day.TransitFromPreviousDayRoute.TransitType,
            day.TransitFromPreviousDayRoute.OriginCity?.CityName ?? string.Empty,
            day.TransitFromPreviousDayRoute.DestinationCity?.CityName ?? string.Empty,
            day.TransitFromPreviousDayRoute.EstimatedCostPerPerson,
            day.TransitFromPreviousDayRoute.DurationInMinutes),
        Items: day.ItineraryItems.OrderBy(i => i.ItemOrderIndex).Select(MapItem).ToList()
    );

    private static ItineraryItemDto MapItem(ItineraryItem item) => new(
        ItemId: item.ItineraryItemId,
        Title: item.ItemTitle,
        Description: item.ItemDescription,
        TimeOfDay: item.TimeOfDay,
        ImageUrl: item.ImageUrl,
        SocialProofBadge: item.SocialProofBadge,
        CostModifier: item.IndividualCostModifier,
        IsOptional: item.IsOptionalActivity,
        IsSelectedByDefault: item.IsSelectedByDefault,
        OrderIndex: item.ItemOrderIndex
    );

    private static WishlistPricingSnapshotDto MapSnapshot(WishlistPricingSnapshot s) => new(
        TravelersCount: s.TravelersCount,
        BasePricePerPerson: s.BasePricePerPerson,
        OptionalActivitiesTotal: s.OptionalActivitiesTotal,
        DepositAmountRequired: s.DepositAmountRequired,
        ValidUntil: s.ValidUntil,
        TotalEstimate: (s.BasePricePerPerson + s.OptionalActivitiesTotal) * s.TravelersCount
    );
}

/// <summary>Body for POST /api/wishlists/{id}/pricing-snapshot.</summary>
public record CreateSnapshotRequest(
    int? TravelersCount,
    decimal? OptionalActivitiesTotal,
    decimal? SeasonalSurchargePercent
);

