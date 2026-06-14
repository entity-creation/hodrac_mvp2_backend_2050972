using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
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

    public DestinationsController(IDestinationRepository destinations)
        => _destinations = destinations;

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
            Items      = items.Select(item => MapToSummary(item))
        });
    }

    // ── GET /api/destinations/{id} ────────────────────────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        Destination? dest = await _destinations.GetByIdAsync(id, ct);
        if (dest is null) return NotFound();

        await _destinations.IncrementSearchHitAsync(id, ct);
        return Ok(MapToDetail(dest));
    }

    // ── GET /api/destinations/matching-user?user_id=...&maxCost=...&limit=12 ──
    // In production wire user_id to the auth token claim, not a query param.

    [HttpGet("matching-user")]
    public async Task<IActionResult> GetMatchingUser(
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery] decimal? maxCost,
        [FromQuery] int limit = 12,
        CancellationToken ct = default)
    {
        // TODO: replace with tags derived from the user's MongoDB psychographic profile.
        // For now, accept tags directly as a query param for flexibility during dev.
        var tags = Request.Query["tags"]
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var items = await _destinations.GetMatchingUserAsync(tags, maxCost, limit, ct);
        return Ok(items.Select(item => MapToSummary(item)));
    }

    // ── GET /api/destinations/similar-name?name=tokio ────────────────────────

    [HttpGet("similar-name")]
    public async Task<IActionResult> GetSimilarByName(
        [FromQuery] string name,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("name query parameter is required.");

        // Derive metaphone code from the input name.
        // Replace with your Metaphone library call (e.g. Metaphone.NETCore or custom).
        var metaphoneCode = name.ToUpperInvariant(); // placeholder — swap with real Metaphone
        var items = await _destinations.GetByPhoneticCodeAsync(metaphoneCode, ct);
        return Ok(items.Select(item => MapToSummary(item)));
    }

    // ── GET /api/destinations/similar-to-wishlist/{wishlistId}?limit=6 ────────

    [HttpGet("similar-to-wishlist/{wishlistId:guid}")]
    public async Task<IActionResult> GetSimilarToWishlist(
        Guid wishlistId,
        [FromQuery] int limit = 6,
        CancellationToken ct = default)
    {
        var items = await _destinations.GetSimilarToWishlistAsync(wishlistId, limit, ct);
        return Ok(items.Select(item => MapToSummary(item)));
    }

    // ── GET /api/destinations/destinations-in-wishlist/{wishlistId}?limit=6 ────────

    [HttpGet("destinations-in-wishlist/{wishlistId:guid}")]
    public async Task<IActionResult> GetDestinationsInWishlist(
        Guid wishlistId,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        var items = await _destinations.GetDestinationsInWishlistAsync(wishlistId, page, ct);
        return Ok(items.Select(item => MapToSummary(item)));
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
