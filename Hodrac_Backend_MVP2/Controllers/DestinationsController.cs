using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.Events;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using Philiprehberger.StringSimilarity;
namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// GET /api/destinations                   — paginated list
/// GET /api/destinations/{id}              — full detail
/// GET /api/destinations/matching-user     — persona-ranked
/// GET /api/destinations/similar-name     — phonetic match
/// GET /api/destinations/similar-to-wishlist/{wishlistId} — wishlist-context destinations
/// </summary>
[ApiController]
[Route("api/destinations")]
public class DestinationsController : ControllerBase
{
    private readonly IDestinationRepository _destinations;
    private readonly IPartnerService _partners;
    private readonly ICurrentUserService _currentUser;
    private readonly IProfileRepository _profiles;
    private readonly IBehaviorEventQueue _events;
    private readonly ILogger<DestinationsController> _logger;

    public DestinationsController(
        IDestinationRepository destinations,
        IPartnerService partners,
        ICurrentUserService currentUser,
        IProfileRepository profiles,
        IBehaviorEventQueue events,
        ILogger<DestinationsController> logger)
    {
        _destinations = destinations;
        _partners = partners;
        _currentUser = currentUser;
        _profiles = profiles;
        _events = events;
        _logger = logger;
    }

    // ── GET /api/destinations?page=1&pageSize=12 ──────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await _destinations.GetAllAsync(page, pageSize, ct);

        return Ok(new
        {
            Page       = page,
            PageSize   = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            Items      = await _partners.AttachAsync(items.Select(item => MapToSummary(item)), ct)
        });
    }

    // ── GET /api/destinations/{id} ────────────────────────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        Destination? dest = await _destinations.GetByIdAsync(id, ct);
        if (dest is null) return NotFound();

        await _destinations.IncrementSearchHitAsync(id, ct);
        PublishDestinationClicked(dest);
        return Ok(await _partners.AttachAsync(MapToDetail(dest), ct));
    }

    /// <summary>Best-effort personalisation signal for authenticated callers; never fails the request.</summary>
    private void PublishDestinationClicked(Destination dest)
    {
        if (!_currentUser.IsAuthenticated) return;
        try
        {
            _events.Publish(new DestinationClickedEvent(
                UserId: _currentUser.UserIdString,
                DestinationId: dest.DestinationId.ToString(),
                DestinationTags: dest.DestinationTags.Select(dt => dt.Tag.TagName).ToList(),
                LuxuryRating: dest.LuxuryRating,
                OccurredAt: DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DestinationClickedEvent for {DestinationId}", dest.DestinationId);
        }
    }

    // ── GET /api/destinations/matching-user?maxCost=...&limit=12[&tags=a,b] ──
    // Ranks destinations by overlap between the JWT caller's Mongo profile tags
    // (interaction-history tags + onboarding answers) and the destination's tags
    // (matched against Tag.TagName or Tag.Key). ?tags= overrides the profile.
    // Anonymous / no profile / too few matches → topped up with most-popular.
    // Any `user_id` query param is ignored: the user always comes from the token.

    [HttpGet("matching-user")]
    public async Task<IActionResult> GetMatchingUser(
        [FromQuery] string? tags,
        [FromQuery] decimal? maxCost,
        [FromQuery] int limit = 12,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        var preferredTags = (tags ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (preferredTags.Count == 0)
        {
            var profile = await ProfileTagSignals.TryLoadProfileAsync(_profiles, _currentUser, _logger);
            preferredTags = ProfileTagSignals.ProfileTags(profile);
        }

        var items = await _destinations.GetMatchingUserAsync(preferredTags, maxCost, limit, ct);
        return Ok(await _partners.AttachAsync(items.Select(item => MapToSummary(item)), ct));
    }

    // ── GET /api/destinations/similar-name?name=tokio ────────────────────────

    [HttpGet("similar-name")]
    public async Task<IActionResult> GetSimilarByName(
        [FromQuery] string name,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("name query parameter is required.");

        var metaphone = Metaphone.Encode(name);
        var metaphoneCodeUpper = metaphone.ToUpperInvariant(); // placeholder — swap with real Metaphone
        var items = await _destinations.GetByPhoneticCodeAsync(metaphoneCodeUpper, ct);
        return Ok(await _partners.AttachAsync(items.Select(item => MapToSummary(item)), ct));
    }

    // ── GET /api/destinations/similar-to-wishlist/{wishlistId}?limit=6 ────────

    [HttpGet("similar-to-wishlist/{wishlistId:guid}")]
    public async Task<IActionResult> GetSimilarToWishlist(
        Guid wishlistId,
        [FromQuery] int limit = 6,
        CancellationToken ct = default)
    {
        var items = await _destinations.GetSimilarToWishlistAsync(wishlistId, limit, ct);
        return Ok(await _partners.AttachAsync(items.Select(item => MapToSummary(item)), ct));
    }

    // ── GET /api/destinations/destinations-in-wishlist/{wishlistId}?limit=6 ────────

    [HttpGet("destinations-in-wishlist/{wishlistId:guid}")]
    public async Task<IActionResult> GetDestinationsInWishlist(
        Guid wishlistId,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        var items = await _destinations.GetDestinationsInWishlistAsync(wishlistId, page, ct);
        return Ok(await _partners.AttachAsync(items.Select(item => MapToSummary(item)), ct));
    }

    // ── DTO mappers ───────────────────────────────────────────────────────────

    private static DestinationSummaryDto MapToSummary(Destination d) => new(
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

    private static DestinationDetailDto MapToDetail(Destination? d)
    {
        var description = string.IsNullOrWhiteSpace(d.DescriptionJson)
            ? new DescriptionJsonDto()
            : JsonSerializer.Deserialize<DescriptionJsonDto>(d.DescriptionJson)
              ?? new DescriptionJsonDto();

        return new DestinationDetailDto(
            DestinationId     : d.DestinationId,
            DestinationName   : d.DestinationName,
            CountryName       : d.Country?.CountryName ?? string.Empty,
            TimeZone          : d.TimeZone,
            AverageCostPerDay : d.AverageCostPerDay,
            SafetyLevel       : d.SafetyLevel,
            LuxuryRating      : d.LuxuryRating,
            FamilyFriendlyScore: d.FamilyFriendlyScore,
            AdventurePaceScore : d.AdventurePaceScore,
            Description       : description,
            Tags              : d.DestinationTags.Select(dt => dt.Tag.TagName).ToList(),
            Categories        : d.DestinationCategories.Select(dc => dc.Category.CategoryName).ToList(),
            Languages         : d.DestinationLanguages.Select(dl => dl.Language.LanguageName).ToList(),
            Currencies        : d.DestinationCurrencies.Select(dc => dc.Currency.CurrencyCode).ToList(),
            Cities            : d.DestinationCities.Select(dc => dc.City.CityName).ToList(),
            Images            : d.Images.Select(i => new DestinationImageDto(
                                    i.ImageUrl, i.ThumbnailUrl, i.Caption,
                                    i.DisplayOrder, i.ImageType)).ToList()
        );
    }
}
