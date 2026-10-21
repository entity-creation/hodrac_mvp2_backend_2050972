using System.Linq.Expressions;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for destinations, their images, and the taxonomy used by the destination pickers.
///
/// Destinations
///   GET    /api/admin/destinations?page=&amp;pageSize=&amp;search=&amp;countryId=&amp;cityId=&amp;tag=&amp;category=&amp;sort=name|popularity|cost
///   GET    /api/admin/destinations/{id}
///   POST   /api/admin/destinations
///   PATCH  /api/admin/destinations/{id}
///   DELETE /api/admin/destinations/{id}             (409 if in a wishlist or a trip post)
/// Images
///   GET    /api/admin/destinations/{id}/images
///   GET    /api/admin/destinations/{id}/images/{imageId}
///   POST   /api/admin/destinations/{id}/images
///   PATCH  /api/admin/destinations/{id}/images/{imageId}
///   DELETE /api/admin/destinations/{id}/images/{imageId}
///   PUT    /api/admin/destinations/{id}/images/order
/// Taxonomy
///   GET/POST /api/admin/tags,       PATCH/DELETE /api/admin/tags/{id}
///   GET/POST /api/admin/categories, PATCH/DELETE /api/admin/categories/{id}
///   GET/POST /api/admin/countries,  PATCH        /api/admin/countries/{id}
///   GET/POST /api/admin/cities?countryId=&amp;search=, PATCH /api/admin/cities/{id}
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin")]
public class AdminDestinationsController : ControllerBase
{
    private const int MaxListEntries = 50;
    private const int MaxDescriptionJsonLength = 100_000;

    private static readonly string[] ImageTypes = { "Hero", "Gallery", "Activity_Proof", "Map_Overlay" };
    private static readonly string[] SortOptions = { "name", "popularity", "cost" };
    private static readonly Regex SnakeCaseKey = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Same as the seeders: stored in jsonb, never rendered as raw HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HodracDbContext _db;
    private readonly ILogger<AdminDestinationsController> _logger;

    public AdminDestinationsController(HodracDbContext db, ILogger<AdminDestinationsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ══ Destinations ══════════════════════════════════════════════════════════

    [HttpGet("destinations")]
    public async Task<ActionResult<AdminPagedResult<AdminDestinationListItemDto>>> GetDestinations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? countryId = null,
        [FromQuery] Guid? cityId = null,
        [FromQuery] string? tag = null,
        [FromQuery] string? category = null,
        [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var sortKey = string.IsNullOrWhiteSpace(sort) ? "name" : sort.Trim().ToLowerInvariant();
        if (!SortOptions.Contains(sortKey))
        {
            ModelState.AddModelError(nameof(sort), $"Sort must be one of: {string.Join(", ", SortOptions)}.");
            return ValidationProblem(ModelState);
        }

        var query = _db.Destinations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            var normalized = DestinationSearchFields.Normalize(search);
            var normalizedPattern = normalized.Length > 0 ? $"%{EscapeLike(normalized)}%" : pattern;
            query = query.Where(d =>
                EF.Functions.ILike(d.DestinationName, pattern) ||
                EF.Functions.ILike(d.CleanNormalizedSearchName, normalizedPattern));
        }

        if (countryId is not null) query = query.Where(d => d.CountryId == countryId);
        if (cityId is not null) query = query.Where(d => d.DestinationCities.Any(dc => dc.CityId == cityId));

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagKey = tag.Trim().ToLowerInvariant();
            query = query.Where(d => d.DestinationTags.Any(dt => dt.Tag.Key.ToLower() == tagKey));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryKey = category.Trim().ToLowerInvariant();
            query = query.Where(d => d.DestinationCategories.Any(dc => dc.Category.Key.ToLower() == categoryKey));
        }

        var total = await query.CountAsync(ct);

        query = sortKey switch
        {
            "popularity" => query.OrderByDescending(d => d.SearchHitCount).ThenBy(d => d.DestinationName).ThenBy(d => d.DestinationId),
            "cost" => query.OrderBy(d => d.AverageCostPerDay).ThenBy(d => d.DestinationName).ThenBy(d => d.DestinationId),
            _ => query.OrderBy(d => d.DestinationName).ThenBy(d => d.DestinationId)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new AdminDestinationListItemDto(
                d.DestinationId,
                d.DestinationName,
                d.CountryId,
                d.Country.CountryName,
                d.DestinationCities.Select(dc => dc.City.CityName).OrderBy(n => n).ToList(),
                d.Images
                    .OrderBy(i => i.ImageType == "Hero" ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.ThumbnailUrl != "" ? i.ThumbnailUrl : i.ImageUrl)
                    .FirstOrDefault(),
                d.AverageCostPerDay,
                d.SafetyLevel,
                d.LuxuryRating,
                d.SearchHitCount,
                d.DestinationTags.Select(dt => dt.Tag.Key).OrderBy(k => k).ToList(),
                d.DestinationCategories.Select(dc => dc.Category.Key).OrderBy(k => k).ToList(),
                d.Images.Count,
                d.WishlistDestinations.Count,
                _db.PartnerLinks.Count(l => l.DestinationId == d.DestinationId)))
            .AsSplitQuery()
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminDestinationListItemDto>(items, page, pageSize, total));
    }

    [HttpGet("destinations/{id:guid}")]
    public async Task<ActionResult<AdminDestinationDetailDto>> GetDestination(Guid id, CancellationToken ct = default)
    {
        var detail = await LoadDestinationDetail(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("destinations")]
    public async Task<ActionResult<AdminDestinationDetailDto>> CreateDestination(CreateDestinationRequest request, CancellationToken ct = default)
    {
        var name = request.DestinationName?.Trim() ?? string.Empty;
        if (name.Length == 0)
            ModelState.AddModelError(nameof(request.DestinationName), "DestinationName is required.");
        else if (DestinationSearchFields.Normalize(name).Length == 0)
            ModelState.AddModelError(nameof(request.DestinationName), "DestinationName must contain letters or digits.");

        var countryId = request.CountryId ?? Guid.Empty;
        if (!await _db.Countries.AnyAsync(c => c.CountryId == countryId, ct))
            ModelState.AddModelError(nameof(request.CountryId), "Country not found.");

        var description = NormalizeDescription(request.Description ?? new DescriptionJsonDto());
        var descriptionJson = JsonSerializer.Serialize(description, JsonOpts);
        ValidateDescriptionSize(descriptionJson);

        var alternates = CleanStringList(request.CommonAlternateSpellings, nameof(request.CommonAlternateSpellings), 200);
        var vibeTags = CleanStringList(request.PsychographicVibeTags, nameof(request.PsychographicVibeTags), 100);

        var selection = await ResolveTaxonomy(
            request.TagKeys, request.CategoryKeys, request.LanguageIds, request.CurrencyIds, request.CityIds, countryId, ct);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var nameLower = name.ToLower();
        if (await _db.Destinations.AnyAsync(d => d.CountryId == countryId && d.DestinationName.ToLower() == nameLower, ct))
            return Conflict(new { error = $"A destination named '{name}' already exists in this country." });

        var search = DestinationSearchFields.Derive(name);
        var destination = new Destination
        {
            DestinationId = Guid.NewGuid(),
            DestinationName = name,
            CleanNormalizedSearchName = search.CleanNormalizedSearchName,
            MetaphoneCode = search.MetaphoneCode,
            DoubleMetaphonePrimary = search.DoubleMetaphonePrimary,
            DoubleMetaphoneSecondary = search.DoubleMetaphoneSecondary,
            CommonAlternateSpellingsJson = JsonSerializer.Serialize(alternates ?? new List<string>(), JsonOpts),
            DescriptionJson = descriptionJson,
            AverageCostPerDay = request.AverageCostPerDay!.Value,
            LuxuryRating = request.LuxuryRating!.Value,
            AccessibilityType = request.AccessibilityType?.Trim() ?? string.Empty,
            FamilyFriendlyScore = request.FamilyFriendlyScore ?? 3,
            AdventurePaceScore = request.AdventurePaceScore ?? 3,
            AestheticTrendScore = request.AestheticTrendScore ?? 3,
            PsychographicVibeTagsJson = JsonSerializer.Serialize(vibeTags ?? new List<string>(), JsonOpts),
            Latitude = request.Latitude!.Value,
            Longitude = request.Longitude!.Value,
            SearchHitCount = 0,
            TimeZone = request.TimeZone?.Trim() ?? string.Empty,
            SafetyLevel = request.SafetyLevel!.Value,
            CountryId = countryId
        };

        _db.Destinations.Add(destination);
        await ApplyTaxonomy(destination.DestinationId, selection, isNew: true, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created destination {DestinationId} ({Name})", destination.DestinationId, destination.DestinationName);

        var detail = await LoadDestinationDetail(destination.DestinationId, ct);
        return CreatedAtAction(nameof(GetDestination), new { id = destination.DestinationId }, detail);
    }

    [HttpPatch("destinations/{id:guid}")]
    public async Task<ActionResult<AdminDestinationDetailDto>> UpdateDestination(Guid id, UpdateDestinationRequest request, CancellationToken ct = default)
    {
        var destination = await _db.Destinations.FirstOrDefaultAsync(d => d.DestinationId == id, ct);
        if (destination is null) return NotFound();

        string? newName = null;
        if (request.DestinationName is not null)
        {
            newName = request.DestinationName.Trim();
            if (newName.Length == 0)
                ModelState.AddModelError(nameof(request.DestinationName), "DestinationName cannot be blank.");
            else if (DestinationSearchFields.Normalize(newName).Length == 0)
                ModelState.AddModelError(nameof(request.DestinationName), "DestinationName must contain letters or digits.");
        }

        var countryId = request.CountryId ?? destination.CountryId;
        if (request.CountryId is not null && request.CountryId != destination.CountryId)
        {
            if (!await _db.Countries.AnyAsync(c => c.CountryId == countryId, ct))
                ModelState.AddModelError(nameof(request.CountryId), "Country not found.");
            else if (request.CityIds is null &&
                     await _db.DestinationCities.AnyAsync(dc => dc.DestinationId == id && dc.City.CountryId != countryId, ct))
                ModelState.AddModelError(nameof(request.CityIds),
                    "The destination's current cities belong to another country; send CityIds when changing CountryId.");
        }

        string? descriptionJson = null;
        if (request.Description is not null)
        {
            descriptionJson = JsonSerializer.Serialize(NormalizeDescription(request.Description), JsonOpts);
            ValidateDescriptionSize(descriptionJson);
        }

        var alternates = CleanStringList(request.CommonAlternateSpellings, nameof(request.CommonAlternateSpellings), 200);
        var vibeTags = CleanStringList(request.PsychographicVibeTags, nameof(request.PsychographicVibeTags), 100);

        var selection = await ResolveTaxonomy(
            request.TagKeys, request.CategoryKeys, request.LanguageIds, request.CurrencyIds, request.CityIds, countryId, ct);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var finalName = newName ?? destination.DestinationName;
        if (newName is not null || countryId != destination.CountryId)
        {
            var nameLower = finalName.ToLower();
            if (await _db.Destinations.AnyAsync(d => d.DestinationId != id && d.CountryId == countryId && d.DestinationName.ToLower() == nameLower, ct))
                return Conflict(new { error = $"A destination named '{finalName}' already exists in this country." });
        }

        if (newName is not null && newName != destination.DestinationName)
        {
            var search = DestinationSearchFields.Derive(newName);
            destination.DestinationName = newName;
            destination.CleanNormalizedSearchName = search.CleanNormalizedSearchName;
            destination.MetaphoneCode = search.MetaphoneCode;
            destination.DoubleMetaphonePrimary = search.DoubleMetaphonePrimary;
            destination.DoubleMetaphoneSecondary = search.DoubleMetaphoneSecondary;
        }

        destination.CountryId = countryId;
        if (request.Latitude is not null) destination.Latitude = request.Latitude.Value;
        if (request.Longitude is not null) destination.Longitude = request.Longitude.Value;
        if (request.AverageCostPerDay is not null) destination.AverageCostPerDay = request.AverageCostPerDay.Value;
        if (request.LuxuryRating is not null) destination.LuxuryRating = request.LuxuryRating.Value;
        if (request.SafetyLevel is not null) destination.SafetyLevel = request.SafetyLevel.Value;
        if (request.FamilyFriendlyScore is not null) destination.FamilyFriendlyScore = request.FamilyFriendlyScore.Value;
        if (request.AdventurePaceScore is not null) destination.AdventurePaceScore = request.AdventurePaceScore.Value;
        if (request.AestheticTrendScore is not null) destination.AestheticTrendScore = request.AestheticTrendScore.Value;
        if (request.AccessibilityType is not null) destination.AccessibilityType = request.AccessibilityType.Trim();
        if (request.TimeZone is not null) destination.TimeZone = request.TimeZone.Trim();
        if (descriptionJson is not null) destination.DescriptionJson = descriptionJson;
        if (alternates is not null) destination.CommonAlternateSpellingsJson = JsonSerializer.Serialize(alternates, JsonOpts);
        if (vibeTags is not null) destination.PsychographicVibeTagsJson = JsonSerializer.Serialize(vibeTags, JsonOpts);

        await ApplyTaxonomy(id, selection, isNew: false, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin updated destination {DestinationId}", id);
        return Ok(await LoadDestinationDetail(id, ct));
    }

    /// <summary>
    /// Refuses (409) while any wishlist or trip post references the destination. Otherwise removes
    /// its images, taxonomy/city junction rows, saved-by-user rows and partner links (with their
    /// click history), then the destination itself, in one transaction.
    /// </summary>
    [HttpDelete("destinations/{id:guid}")]
    public async Task<IActionResult> DeleteDestination(Guid id, CancellationToken ct = default)
    {
        if (!await _db.Destinations.AnyAsync(d => d.DestinationId == id, ct)) return NotFound();

        var wishlistCount = await _db.WishlistDestinations.CountAsync(wd => wd.DestinationId == id, ct);
        var tripPostCount = await _db.TripPosts.CountAsync(t => t.DestinationId == id, ct);
        if (wishlistCount > 0 || tripPostCount > 0)
            return Conflict(new
            {
                error = $"Destination is in use: {wishlistCount} wishlist(s) and {tripPostCount} trip post(s) reference it. Remove those references first.",
                wishlistCount,
                tripPostCount
            });

        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // All of these FKs are ON DELETE CASCADE in the current schema; deleting explicitly
            // keeps the behaviour independent of the DB constraint definitions.
            var images = await _db.DestinationImages.Where(i => i.DestinationId == id).ExecuteDeleteAsync(ct);
            await _db.DestinationTags.Where(x => x.DestinationId == id).ExecuteDeleteAsync(ct);
            await _db.DestinationCategories.Where(x => x.DestinationId == id).ExecuteDeleteAsync(ct);
            await _db.DestinationLanguages.Where(x => x.DestinationId == id).ExecuteDeleteAsync(ct);
            await _db.DestinationCurrencies.Where(x => x.DestinationId == id).ExecuteDeleteAsync(ct);
            await _db.DestinationCities.Where(x => x.DestinationId == id).ExecuteDeleteAsync(ct);
            var saved = await _db.SavedDestinations.Where(x => x.DestinationId == id).ExecuteDeleteAsync(ct);
            await _db.PartnerLinkClicks.Where(c => c.Link.DestinationId == id).ExecuteDeleteAsync(ct);
            var links = await _db.PartnerLinks.Where(l => l.DestinationId == id).ExecuteDeleteAsync(ct);
            var deleted = await _db.Destinations.Where(d => d.DestinationId == id).ExecuteDeleteAsync(ct);

            if (deleted == 0)
            {
                await tx.RollbackAsync(ct);
                return NotFound();
            }

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Admin deleted destination {DestinationId} ({Images} images, {Saved} saves, {Links} partner links)",
                id, images, saved, links);
            return NoContent();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // A wishlist / trip post started referencing it between the check and the delete.
            _logger.LogWarning(ex, "Destination {DestinationId} delete blocked by a foreign key", id);
            return Conflict(new { error = "Destination became referenced while deleting; reload and try again." });
        }
    }

    // ══ Destination images ════════════════════════════════════════════════════

    [HttpGet("destinations/{id:guid}/images")]
    public async Task<ActionResult<List<AdminDestinationImageDto>>> GetImages(Guid id, CancellationToken ct = default)
    {
        if (!await _db.Destinations.AnyAsync(d => d.DestinationId == id, ct)) return NotFound();
        return Ok(await LoadImages(id, ct));
    }

    [HttpGet("destinations/{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<AdminDestinationImageDto>> GetImage(Guid id, Guid imageId, CancellationToken ct = default)
    {
        var image = await _db.DestinationImages
            .AsNoTracking()
            .Where(i => i.DestinationId == id && i.DestinationImageId == imageId)
            .Select(ToImageDto)
            .FirstOrDefaultAsync(ct);

        return image is null ? NotFound() : Ok(image);
    }

    [HttpPost("destinations/{id:guid}/images")]
    public async Task<ActionResult<AdminDestinationImageDto>> CreateImage(Guid id, CreateDestinationImageRequest request, CancellationToken ct = default)
    {
        if (!await _db.Destinations.AnyAsync(d => d.DestinationId == id, ct)) return NotFound();

        var imageUrl = request.ImageUrl?.Trim() ?? string.Empty;
        if (!IsHttpUrl(imageUrl))
            ModelState.AddModelError(nameof(request.ImageUrl), "ImageUrl must be an absolute http(s) URL.");

        var thumbnailUrl = NullIfBlank(request.ThumbnailUrl);
        if (thumbnailUrl is not null && !IsHttpUrl(thumbnailUrl))
            ModelState.AddModelError(nameof(request.ThumbnailUrl), "ThumbnailUrl must be an absolute http(s) URL.");

        var imageType = NormalizeImageType(request.ImageType);
        if (imageType is null)
            ModelState.AddModelError(nameof(request.ImageType), $"ImageType must be one of: {string.Join(", ", ImageTypes)}.");

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var images = await LoadTrackedImages(id, ct);

        if (imageType == "Hero") DemoteHeroes(images, exceptId: null);

        var image = new DestinationImage
        {
            DestinationImageId = Guid.NewGuid(),
            DestinationId = id,
            ImageUrl = imageUrl,
            ThumbnailUrl = thumbnailUrl ?? imageUrl,
            Caption = request.Caption?.Trim() ?? string.Empty,
            ImageType = imageType!,
            ShotContext = request.ShotContext?.Trim() ?? string.Empty,
            IsAiGenerated = request.IsAiGenerated
        };

        var position = request.DisplayOrder is null
            ? images.Count
            : Math.Clamp(request.DisplayOrder.Value - 1, 0, images.Count);
        images.Insert(position, image);
        Renumber(images);

        _db.DestinationImages.Add(image);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin added {ImageType} image {ImageId} to destination {DestinationId}", image.ImageType, image.DestinationImageId, id);

        return CreatedAtAction(nameof(GetImage), new { id, imageId = image.DestinationImageId }, ToImageDtoCompiled(image));
    }

    [HttpPatch("destinations/{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<AdminDestinationImageDto>> UpdateImage(Guid id, Guid imageId, UpdateDestinationImageRequest request, CancellationToken ct = default)
    {
        var images = await LoadTrackedImages(id, ct);
        var image = images.FirstOrDefault(i => i.DestinationImageId == imageId);
        if (image is null) return NotFound();

        string? imageUrl = null;
        if (request.ImageUrl is not null)
        {
            imageUrl = request.ImageUrl.Trim();
            if (!IsHttpUrl(imageUrl))
                ModelState.AddModelError(nameof(request.ImageUrl), "ImageUrl must be an absolute http(s) URL.");
        }

        string? thumbnailUrl = null;
        if (request.ThumbnailUrl is not null)
        {
            thumbnailUrl = NullIfBlank(request.ThumbnailUrl);
            if (thumbnailUrl is not null && !IsHttpUrl(thumbnailUrl))
                ModelState.AddModelError(nameof(request.ThumbnailUrl), "ThumbnailUrl must be an absolute http(s) URL.");
        }

        string? imageType = null;
        if (request.ImageType is not null)
        {
            imageType = NormalizeImageType(request.ImageType);
            if (imageType is null)
                ModelState.AddModelError(nameof(request.ImageType), $"ImageType must be one of: {string.Join(", ", ImageTypes)}.");
        }

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (imageUrl is not null)
        {
            // Keep a defaulted thumbnail in sync with the image it was defaulted from.
            if (request.ThumbnailUrl is null && image.ThumbnailUrl == image.ImageUrl) image.ThumbnailUrl = imageUrl;
            image.ImageUrl = imageUrl;
        }
        if (request.ThumbnailUrl is not null) image.ThumbnailUrl = thumbnailUrl ?? image.ImageUrl;
        if (request.Caption is not null) image.Caption = request.Caption.Trim();
        if (request.ShotContext is not null) image.ShotContext = request.ShotContext.Trim();
        if (request.IsAiGenerated is not null) image.IsAiGenerated = request.IsAiGenerated.Value;
        if (imageType is not null)
        {
            if (imageType == "Hero") DemoteHeroes(images, exceptId: imageId);
            image.ImageType = imageType;
        }

        if (request.DisplayOrder is not null)
        {
            images.Remove(image);
            images.Insert(Math.Clamp(request.DisplayOrder.Value - 1, 0, images.Count), image);
            Renumber(images);
        }

        await _db.SaveChangesAsync(ct);

        return Ok(ToImageDtoCompiled(image));
    }

    [HttpDelete("destinations/{id:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct = default)
    {
        var images = await LoadTrackedImages(id, ct);
        var image = images.FirstOrDefault(i => i.DestinationImageId == imageId);
        if (image is null) return NotFound();

        images.Remove(image);
        _db.DestinationImages.Remove(image);
        Renumber(images);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin deleted image {ImageId} from destination {DestinationId}", imageId, id);
        return NoContent();
    }

    [HttpPut("destinations/{id:guid}/images/order")]
    public async Task<ActionResult<List<AdminDestinationImageDto>>> ReorderImages(Guid id, ReorderDestinationImagesRequest request, CancellationToken ct = default)
    {
        if (!await _db.Destinations.AnyAsync(d => d.DestinationId == id, ct)) return NotFound();

        var images = await LoadTrackedImages(id, ct);
        var requested = request.ImageIds ?? new List<Guid>();
        var byId = images.ToDictionary(i => i.DestinationImageId);

        var duplicates = requested.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var unknown = requested.Where(x => !byId.ContainsKey(x)).Distinct().ToList();
        var missing = images.Select(i => i.DestinationImageId).Where(x => !requested.Contains(x)).ToList();

        if (duplicates.Count > 0)
            ModelState.AddModelError(nameof(request.ImageIds), $"Duplicate image ids: {string.Join(", ", duplicates)}.");
        if (unknown.Count > 0)
            ModelState.AddModelError(nameof(request.ImageIds), $"Images not on this destination: {string.Join(", ", unknown)}.");
        if (missing.Count > 0)
            ModelState.AddModelError(nameof(request.ImageIds), $"Every image must be listed; missing: {string.Join(", ", missing)}.");

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        Renumber(requested.Select(x => byId[x]).ToList());
        await _db.SaveChangesAsync(ct);

        return Ok(await LoadImages(id, ct));
    }

    // ══ Tags ══════════════════════════════════════════════════════════════════

    [HttpGet("tags")]
    public async Task<ActionResult<List<AdminTagDto>>> GetTags(CancellationToken ct = default)
        => Ok(await _db.Tags.AsNoTracking().OrderBy(t => t.TagName).Select(ToTagDto).ToListAsync(ct));

    [HttpPost("tags")]
    public async Task<ActionResult<AdminTagDto>> CreateTag(CreateTagRequest request, CancellationToken ct = default)
    {
        var key = ValidateKey(request.Key, nameof(request.Key));
        var name = RequireText(request.TagName, nameof(request.TagName));
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (await _db.Tags.AnyAsync(t => t.Key.ToLower() == key, ct))
            return Conflict(new { error = $"A tag with key '{key}' already exists." });

        var tag = new Tag
        {
            TagId = Guid.NewGuid(),
            Key = key!,
            TagName = name!,
            TargetPersonaType = request.TargetPersonaType?.Trim() ?? string.Empty
        };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created tag {TagId} ({Key})", tag.TagId, tag.Key);

        var dto = await _db.Tags.AsNoTracking().Where(t => t.TagId == tag.TagId).Select(ToTagDto).FirstAsync(ct);
        return CreatedAtAction(nameof(GetTags), null, dto);
    }

    [HttpPatch("tags/{id:guid}")]
    public async Task<ActionResult<AdminTagDto>> UpdateTag(Guid id, UpdateTagRequest request, CancellationToken ct = default)
    {
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.TagId == id, ct);
        if (tag is null) return NotFound();

        var key = request.Key is null ? null : ValidateKey(request.Key, nameof(request.Key));
        var name = request.TagName is null ? null : RequireText(request.TagName, nameof(request.TagName));
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (key is not null && await _db.Tags.AnyAsync(t => t.TagId != id && t.Key.ToLower() == key, ct))
            return Conflict(new { error = $"A tag with key '{key}' already exists." });

        if (key is not null) tag.Key = key;
        if (name is not null) tag.TagName = name;
        if (request.TargetPersonaType is not null) tag.TargetPersonaType = request.TargetPersonaType.Trim();
        await _db.SaveChangesAsync(ct);

        return Ok(await _db.Tags.AsNoTracking().Where(t => t.TagId == id).Select(ToTagDto).FirstAsync(ct));
    }

    [HttpDelete("tags/{id:guid}")]
    public async Task<IActionResult> DeleteTag(Guid id, CancellationToken ct = default)
    {
        if (!await _db.Tags.AnyAsync(t => t.TagId == id, ct)) return NotFound();

        var usage = await _db.DestinationTags.CountAsync(dt => dt.TagId == id, ct);
        if (usage > 0)
            return Conflict(new { error = $"Tag is used by {usage} destination(s); remove it from them first.", destinationCount = usage });

        await _db.Tags.Where(t => t.TagId == id).ExecuteDeleteAsync(ct);
        _logger.LogInformation("Admin deleted tag {TagId}", id);
        return NoContent();
    }

    // ══ Categories ════════════════════════════════════════════════════════════

    [HttpGet("categories")]
    public async Task<ActionResult<List<AdminCategoryDto>>> GetCategories(CancellationToken ct = default)
        => Ok(await _db.Categories.AsNoTracking().OrderBy(c => c.CategoryName).Select(ToCategoryDto).ToListAsync(ct));

    [HttpPost("categories")]
    public async Task<ActionResult<AdminCategoryDto>> CreateCategory(CreateCategoryRequest request, CancellationToken ct = default)
    {
        var key = ValidateKey(request.Key, nameof(request.Key));
        var name = RequireText(request.CategoryName, nameof(request.CategoryName));
        var color = ValidateColor(request.ColorHex, nameof(request.ColorHex));
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (await _db.Categories.AnyAsync(c => c.Key.ToLower() == key, ct))
            return Conflict(new { error = $"A category with key '{key}' already exists." });

        var category = new Category
        {
            CategoryId = Guid.NewGuid(),
            Key = key!,
            CategoryName = name!,
            CategoryDescription = request.CategoryDescription?.Trim() ?? string.Empty,
            IconName = request.IconName?.Trim() ?? string.Empty,
            ColorHex = color ?? string.Empty
        };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created category {CategoryId} ({Key})", category.CategoryId, category.Key);

        var dto = await _db.Categories.AsNoTracking().Where(c => c.CategoryId == category.CategoryId).Select(ToCategoryDto).FirstAsync(ct);
        return CreatedAtAction(nameof(GetCategories), null, dto);
    }

    [HttpPatch("categories/{id:guid}")]
    public async Task<ActionResult<AdminCategoryDto>> UpdateCategory(Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.CategoryId == id, ct);
        if (category is null) return NotFound();

        var key = request.Key is null ? null : ValidateKey(request.Key, nameof(request.Key));
        var name = request.CategoryName is null ? null : RequireText(request.CategoryName, nameof(request.CategoryName));
        var color = request.ColorHex is null ? null : ValidateColor(request.ColorHex, nameof(request.ColorHex));
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (key is not null && await _db.Categories.AnyAsync(c => c.CategoryId != id && c.Key.ToLower() == key, ct))
            return Conflict(new { error = $"A category with key '{key}' already exists." });

        if (key is not null) category.Key = key;
        if (name is not null) category.CategoryName = name;
        if (request.CategoryDescription is not null) category.CategoryDescription = request.CategoryDescription.Trim();
        if (request.IconName is not null) category.IconName = request.IconName.Trim();
        if (request.ColorHex is not null) category.ColorHex = color ?? string.Empty;
        await _db.SaveChangesAsync(ct);

        return Ok(await _db.Categories.AsNoTracking().Where(c => c.CategoryId == id).Select(ToCategoryDto).FirstAsync(ct));
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct = default)
    {
        if (!await _db.Categories.AnyAsync(c => c.CategoryId == id, ct)) return NotFound();

        var usage = await _db.DestinationCategories.CountAsync(dc => dc.CategoryId == id, ct);
        if (usage > 0)
            return Conflict(new { error = $"Category is used by {usage} destination(s); remove it from them first.", destinationCount = usage });

        await _db.Categories.Where(c => c.CategoryId == id).ExecuteDeleteAsync(ct);
        _logger.LogInformation("Admin deleted category {CategoryId}", id);
        return NoContent();
    }

    // ══ Countries ═════════════════════════════════════════════════════════════

    [HttpGet("countries")]
    public async Task<ActionResult<List<AdminCountryDto>>> GetCountries(CancellationToken ct = default)
        => Ok(await _db.Countries.AsNoTracking().OrderBy(c => c.CountryName).Select(ToCountryDto).ToListAsync(ct));

    [HttpPost("countries")]
    public async Task<ActionResult<AdminCountryDto>> CreateCountry(CreateCountryRequest request, CancellationToken ct = default)
    {
        var name = RequireText(request.CountryName, nameof(request.CountryName));
        var heroImage = ValidateOptionalUrl(request.GlobalHeroImage, nameof(request.GlobalHeroImage));
        var drivingSide = ValidateDrivingSide(request.DrivingSide, nameof(request.DrivingSide));
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var nameLower = name!.ToLower();
        if (await _db.Countries.AnyAsync(c => c.CountryName.ToLower() == nameLower, ct))
            return Conflict(new { error = $"A country named '{name}' already exists." });

        var country = new Country
        {
            CountryId = Guid.NewGuid(),
            CountryName = name,
            Continent = request.Continent?.Trim() ?? string.Empty,
            CountryFlagEmoji = request.CountryFlagEmoji?.Trim() ?? string.Empty,
            GlobalHeroImage = heroImage ?? string.Empty,
            VisaRequirementsSummary = request.VisaRequirementsSummary?.Trim() ?? string.Empty,
            PowerPlugType = request.PowerPlugType?.Trim() ?? string.Empty,
            DrivingSide = drivingSide ?? string.Empty,
            EstimatedDailyTaxRate = request.EstimatedDailyTaxRate ?? 0m
        };
        _db.Countries.Add(country);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created country {CountryId} ({Name})", country.CountryId, country.CountryName);

        var dto = await _db.Countries.AsNoTracking().Where(c => c.CountryId == country.CountryId).Select(ToCountryDto).FirstAsync(ct);
        return CreatedAtAction(nameof(GetCountries), null, dto);
    }

    [HttpPatch("countries/{id:guid}")]
    public async Task<ActionResult<AdminCountryDto>> UpdateCountry(Guid id, UpdateCountryRequest request, CancellationToken ct = default)
    {
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.CountryId == id, ct);
        if (country is null) return NotFound();

        var name = request.CountryName is null ? null : RequireText(request.CountryName, nameof(request.CountryName));
        var heroImage = request.GlobalHeroImage is null ? null : ValidateOptionalUrl(request.GlobalHeroImage, nameof(request.GlobalHeroImage));
        var drivingSide = request.DrivingSide is null ? null : ValidateDrivingSide(request.DrivingSide, nameof(request.DrivingSide));
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (name is not null)
        {
            var nameLower = name.ToLower();
            if (await _db.Countries.AnyAsync(c => c.CountryId != id && c.CountryName.ToLower() == nameLower, ct))
                return Conflict(new { error = $"A country named '{name}' already exists." });
            country.CountryName = name;
        }

        if (request.Continent is not null) country.Continent = request.Continent.Trim();
        if (request.CountryFlagEmoji is not null) country.CountryFlagEmoji = request.CountryFlagEmoji.Trim();
        if (request.GlobalHeroImage is not null) country.GlobalHeroImage = heroImage ?? string.Empty;
        if (request.VisaRequirementsSummary is not null) country.VisaRequirementsSummary = request.VisaRequirementsSummary.Trim();
        if (request.PowerPlugType is not null) country.PowerPlugType = request.PowerPlugType.Trim();
        if (request.DrivingSide is not null) country.DrivingSide = drivingSide ?? string.Empty;
        if (request.EstimatedDailyTaxRate is not null) country.EstimatedDailyTaxRate = request.EstimatedDailyTaxRate.Value;
        await _db.SaveChangesAsync(ct);

        return Ok(await _db.Countries.AsNoTracking().Where(c => c.CountryId == id).Select(ToCountryDto).FirstAsync(ct));
    }

    // ══ Cities ════════════════════════════════════════════════════════════════

    [HttpGet("cities")]
    public async Task<ActionResult<List<AdminCityDto>>> GetCities(
        [FromQuery] Guid? countryId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var query = _db.Cities.AsNoTracking();
        if (countryId is not null) query = query.Where(c => c.CountryId == countryId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(c => EF.Functions.ILike(c.CityName, pattern));
        }

        return Ok(await query
            .OrderBy(c => c.Country.CountryName)
            .ThenBy(c => c.CityName)
            .Select(ToCityDto)
            .ToListAsync(ct));
    }

    [HttpPost("cities")]
    public async Task<ActionResult<AdminCityDto>> CreateCity(CreateCityRequest request, CancellationToken ct = default)
    {
        var name = RequireText(request.CityName, nameof(request.CityName));
        var countryId = request.CountryId ?? Guid.Empty;
        if (!await _db.Countries.AnyAsync(c => c.CountryId == countryId, ct))
            ModelState.AddModelError(nameof(request.CountryId), "Country not found.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var nameLower = name!.ToLower();
        if (await _db.Cities.AnyAsync(c => c.CountryId == countryId && c.CityName.ToLower() == nameLower, ct))
            return Conflict(new { error = $"A city named '{name}' already exists in this country." });

        var city = new City
        {
            CityId = Guid.NewGuid(),
            CityName = name,
            CountryId = countryId,
            Latitude = request.Latitude!.Value,
            Longitude = request.Longitude!.Value,
            CityDescription = request.CityDescription?.Trim() ?? string.Empty
        };
        _db.Cities.Add(city);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created city {CityId} ({Name})", city.CityId, city.CityName);

        var dto = await _db.Cities.AsNoTracking().Where(c => c.CityId == city.CityId).Select(ToCityDto).FirstAsync(ct);
        return CreatedAtAction(nameof(GetCities), new { countryId }, dto);
    }

    [HttpPatch("cities/{id:guid}")]
    public async Task<ActionResult<AdminCityDto>> UpdateCity(Guid id, UpdateCityRequest request, CancellationToken ct = default)
    {
        var city = await _db.Cities.FirstOrDefaultAsync(c => c.CityId == id, ct);
        if (city is null) return NotFound();

        var name = request.CityName is null ? null : RequireText(request.CityName, nameof(request.CityName));
        var countryId = request.CountryId ?? city.CountryId;
        if (request.CountryId is not null && !await _db.Countries.AnyAsync(c => c.CountryId == countryId, ct))
            ModelState.AddModelError(nameof(request.CountryId), "Country not found.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (name is not null || countryId != city.CountryId)
        {
            var nameLower = (name ?? city.CityName).ToLower();
            if (await _db.Cities.AnyAsync(c => c.CityId != id && c.CountryId == countryId && c.CityName.ToLower() == nameLower, ct))
                return Conflict(new { error = $"A city named '{name ?? city.CityName}' already exists in this country." });
        }

        if (name is not null) city.CityName = name;
        city.CountryId = countryId;
        if (request.Latitude is not null) city.Latitude = request.Latitude.Value;
        if (request.Longitude is not null) city.Longitude = request.Longitude.Value;
        if (request.CityDescription is not null) city.CityDescription = request.CityDescription.Trim();
        await _db.SaveChangesAsync(ct);

        return Ok(await _db.Cities.AsNoTracking().Where(c => c.CityId == id).Select(ToCityDto).FirstAsync(ct));
    }

    // ══ Helpers: destinations ═════════════════════════════════════════════════

    private sealed record TaxonomySelection(
        List<Guid>? TagIds,
        List<Guid>? CategoryIds,
        List<Guid>? LanguageIds,
        List<Guid>? CurrencyIds,
        List<Guid>? CityIds);

    /// <summary>Validates the requested sets (null = not provided). Unknown keys/ids become ModelState errors.</summary>
    private async Task<TaxonomySelection> ResolveTaxonomy(
        List<string>? tagKeys,
        List<string>? categoryKeys,
        List<Guid>? languageIds,
        List<Guid>? currencyIds,
        List<Guid>? cityIds,
        Guid countryId,
        CancellationToken ct)
    {
        List<Guid>? tagIds = null;
        if (tagKeys is not null)
        {
            var keys = NormalizeKeys(tagKeys);
            var found = await _db.Tags
                .Where(t => keys.Contains(t.Key.ToLower()))
                .Select(t => new { t.TagId, Key = t.Key.ToLower() })
                .ToListAsync(ct);
            var missing = keys.Except(found.Select(f => f.Key)).ToList();
            if (missing.Count > 0)
                ModelState.AddModelError("TagKeys", $"Unknown tag keys: {string.Join(", ", missing)}.");
            tagIds = found.Select(f => f.TagId).Distinct().ToList();
        }

        List<Guid>? categoryIds = null;
        if (categoryKeys is not null)
        {
            var keys = NormalizeKeys(categoryKeys);
            var found = await _db.Categories
                .Where(c => keys.Contains(c.Key.ToLower()))
                .Select(c => new { c.CategoryId, Key = c.Key.ToLower() })
                .ToListAsync(ct);
            var missing = keys.Except(found.Select(f => f.Key)).ToList();
            if (missing.Count > 0)
                ModelState.AddModelError("CategoryKeys", $"Unknown category keys: {string.Join(", ", missing)}.");
            categoryIds = found.Select(f => f.CategoryId).Distinct().ToList();
        }

        List<Guid>? langIds = null;
        if (languageIds is not null)
        {
            var ids = languageIds.Distinct().ToList();
            langIds = await _db.Languages.Where(l => ids.Contains(l.LanguageId)).Select(l => l.LanguageId).ToListAsync(ct);
            var missing = ids.Except(langIds).ToList();
            if (missing.Count > 0)
                ModelState.AddModelError("LanguageIds", $"Unknown language ids: {string.Join(", ", missing)}.");
        }

        List<Guid>? curIds = null;
        if (currencyIds is not null)
        {
            var ids = currencyIds.Distinct().ToList();
            curIds = await _db.Currencies.Where(c => ids.Contains(c.CurrencyId)).Select(c => c.CurrencyId).ToListAsync(ct);
            var missing = ids.Except(curIds).ToList();
            if (missing.Count > 0)
                ModelState.AddModelError("CurrencyIds", $"Unknown currency ids: {string.Join(", ", missing)}.");
        }

        List<Guid>? cIds = null;
        if (cityIds is not null)
        {
            var ids = cityIds.Distinct().ToList();
            var found = await _db.Cities
                .Where(c => ids.Contains(c.CityId))
                .Select(c => new { c.CityId, c.CountryId })
                .ToListAsync(ct);
            var missing = ids.Except(found.Select(f => f.CityId)).ToList();
            if (missing.Count > 0)
                ModelState.AddModelError("CityIds", $"Unknown city ids: {string.Join(", ", missing)}.");
            var otherCountry = found.Where(f => f.CountryId != countryId).Select(f => f.CityId).ToList();
            if (otherCountry.Count > 0)
                ModelState.AddModelError("CityIds", $"Cities not in the destination's country: {string.Join(", ", otherCountry)}.");
            cIds = found.Select(f => f.CityId).ToList();
        }

        return new TaxonomySelection(tagIds, categoryIds, langIds, curIds, cIds);
    }

    /// <summary>Replaces each provided set exactly; null sets are left untouched. Caller saves.</summary>
    private async Task ApplyTaxonomy(Guid destinationId, TaxonomySelection selection, bool isNew, CancellationToken ct)
    {
        if (selection.TagIds is not null)
            await SyncJunction(_db.DestinationTags, x => x.DestinationId == destinationId, x => x.TagId,
                selection.TagIds, tagId => new DestinationTag { DestinationId = destinationId, TagId = tagId }, isNew, ct);

        if (selection.CategoryIds is not null)
            await SyncJunction(_db.DestinationCategories, x => x.DestinationId == destinationId, x => x.CategoryId,
                selection.CategoryIds, cId => new DestinationCategory { DestinationId = destinationId, CategoryId = cId }, isNew, ct);

        if (selection.LanguageIds is not null)
            await SyncJunction(_db.DestinationLanguages, x => x.DestinationId == destinationId, x => x.LanguageId,
                selection.LanguageIds, lId => new DestinationLanguage { DestinationId = destinationId, LanguageId = lId }, isNew, ct);

        if (selection.CurrencyIds is not null)
            await SyncJunction(_db.DestinationCurrencies, x => x.DestinationId == destinationId, x => x.CurrencyId,
                selection.CurrencyIds, cId => new DestinationCurrency { DestinationId = destinationId, CurrencyId = cId }, isNew, ct);

        if (selection.CityIds is not null)
            await SyncJunction(_db.DestinationCities, x => x.DestinationId == destinationId, x => x.CityId,
                selection.CityIds, cId => new DestinationCity { DestinationId = destinationId, CityId = cId }, isNew, ct);
    }

    private static async Task SyncJunction<T>(
        DbSet<T> set,
        Expression<Func<T, bool>> forDestination,
        Func<T, Guid> otherKey,
        List<Guid> desired,
        Func<Guid, T> create,
        bool isNew,
        CancellationToken ct) where T : class
    {
        var existing = isNew ? new List<T>() : await set.Where(forDestination).ToListAsync(ct);
        var desiredSet = desired.ToHashSet();

        set.RemoveRange(existing.Where(e => !desiredSet.Contains(otherKey(e))));

        var have = existing.Select(otherKey).ToHashSet();
        foreach (var id in desiredSet.Where(id => !have.Contains(id)))
            set.Add(create(id));
    }

    private async Task<AdminDestinationDetailDto?> LoadDestinationDetail(Guid id, CancellationToken ct)
    {
        var d = await _db.Destinations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Country)
            .Include(x => x.Images)
            .Include(x => x.DestinationTags).ThenInclude(x => x.Tag)
            .Include(x => x.DestinationCategories).ThenInclude(x => x.Category)
            .Include(x => x.DestinationLanguages).ThenInclude(x => x.Language)
            .Include(x => x.DestinationCurrencies).ThenInclude(x => x.Currency)
            .Include(x => x.DestinationCities).ThenInclude(x => x.City)
            .FirstOrDefaultAsync(x => x.DestinationId == id, ct);
        if (d is null) return null;

        var wishlistCount = await _db.WishlistDestinations.CountAsync(x => x.DestinationId == id, ct);
        var tripPostCount = await _db.TripPosts.CountAsync(x => x.DestinationId == id, ct);
        var partnerLinkCount = await _db.PartnerLinks.CountAsync(x => x.DestinationId == id, ct);

        return new AdminDestinationDetailDto(
            d.DestinationId,
            d.DestinationName,
            d.CleanNormalizedSearchName,
            d.MetaphoneCode,
            d.DoubleMetaphonePrimary,
            d.DoubleMetaphoneSecondary,
            d.CountryId,
            d.Country?.CountryName ?? string.Empty,
            NormalizeDescription(DeserializeOrDefault(d.DescriptionJson, () => new DescriptionJsonDto())),
            DeserializeOrDefault(d.CommonAlternateSpellingsJson, () => new List<string>()),
            DeserializeOrDefault(d.PsychographicVibeTagsJson, () => new List<string>()),
            d.AverageCostPerDay,
            d.LuxuryRating,
            d.AccessibilityType,
            d.FamilyFriendlyScore,
            d.AdventurePaceScore,
            d.AestheticTrendScore,
            d.Latitude,
            d.Longitude,
            d.SearchHitCount,
            d.TimeZone,
            d.SafetyLevel,
            d.Images.OrderBy(i => i.DisplayOrder).ThenBy(i => i.DestinationImageId).Select(ToImageDtoCompiled).ToList(),
            d.DestinationTags.Select(x => new AdminRefDto(x.TagId, x.Tag.TagName, x.Tag.Key)).OrderBy(x => x.Name).ToList(),
            d.DestinationCategories.Select(x => new AdminRefDto(x.CategoryId, x.Category.CategoryName, x.Category.Key)).OrderBy(x => x.Name).ToList(),
            d.DestinationLanguages.Select(x => new AdminRefDto(x.LanguageId, x.Language.LanguageName, x.Language.LanguageCode)).OrderBy(x => x.Name).ToList(),
            d.DestinationCurrencies.Select(x => new AdminRefDto(x.CurrencyId, x.Currency.CurrencyName, x.Currency.CurrencyCode)).OrderBy(x => x.Name).ToList(),
            d.DestinationCities.Select(x => new AdminRefDto(x.CityId, x.City.CityName, null)).OrderBy(x => x.Name).ToList(),
            wishlistCount,
            tripPostCount,
            partnerLinkCount);
    }

    private T DeserializeOrDefault<T>(string? json, Func<T> fallback) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback();
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts) ?? fallback();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not deserialize destination jsonb as {Type}", typeof(T).Name);
            return fallback();
        }
    }

    private static DescriptionJsonDto NormalizeDescription(DescriptionJsonDto d) => new()
    {
        Overview = d.Overview?.Trim() ?? string.Empty,
        LocalPerspective = d.LocalPerspective?.Trim() ?? string.Empty,
        Directions = d.Directions?.Trim() ?? string.Empty,
        WhatToKnow = d.WhatToKnow?.Trim() ?? string.Empty,
        ThingsToBeWaryOf = d.ThingsToBeWaryOf?.Trim() ?? string.Empty,
        HiddenCost = d.HiddenCost?.Trim() ?? string.Empty,
        NearbyComplements = (d.NearbyComplements ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList(),
        BestTimeToVisit = d.BestTimeToVisit?.Trim() ?? string.Empty,
        CrowdLevel = d.CrowdLevel?.Trim() ?? string.Empty,
        Accessibility = d.Accessibility?.Trim() ?? string.Empty,
        IdealDuration = d.IdealDuration?.Trim() ?? string.Empty
    };

    private void ValidateDescriptionSize(string json)
    {
        if (json.Length > MaxDescriptionJsonLength)
            ModelState.AddModelError("Description", $"Description is too large (max {MaxDescriptionJsonLength:N0} characters of JSON).");
    }

    /// <summary>Trims, drops blanks, de-duplicates (case-insensitive). Returns null when the list wasn't provided.</summary>
    private List<string>? CleanStringList(List<string>? values, string field, int maxItemLength)
    {
        if (values is null) return null;

        var cleaned = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count > MaxListEntries)
            ModelState.AddModelError(field, $"At most {MaxListEntries} entries are allowed.");
        var tooLong = cleaned.Where(v => v.Length > maxItemLength).ToList();
        if (tooLong.Count > 0)
            ModelState.AddModelError(field, $"Entries must be at most {maxItemLength} characters.");

        return cleaned;
    }

    private static List<string> NormalizeKeys(IEnumerable<string> keys)
        => keys.Where(k => !string.IsNullOrWhiteSpace(k))
               .Select(k => k.Trim().ToLowerInvariant())
               .Distinct()
               .ToList();

    // ══ Helpers: images ═══════════════════════════════════════════════════════

    private static readonly Expression<Func<DestinationImage, AdminDestinationImageDto>> ToImageDto = i => new AdminDestinationImageDto(
        i.DestinationImageId,
        i.ImageUrl,
        i.ThumbnailUrl,
        i.Caption,
        i.DisplayOrder,
        i.ImageType,
        i.ShotContext,
        i.IsAiGenerated);

    private static readonly Func<DestinationImage, AdminDestinationImageDto> ToImageDtoCompiled = ToImageDto.Compile();

    private Task<List<AdminDestinationImageDto>> LoadImages(Guid destinationId, CancellationToken ct)
        => _db.DestinationImages
            .AsNoTracking()
            .Where(i => i.DestinationId == destinationId)
            .OrderBy(i => i.DisplayOrder)
            .ThenBy(i => i.DestinationImageId)
            .Select(ToImageDto)
            .ToListAsync(ct);

    private Task<List<DestinationImage>> LoadTrackedImages(Guid destinationId, CancellationToken ct)
        => _db.DestinationImages
            .Where(i => i.DestinationId == destinationId)
            .OrderBy(i => i.DisplayOrder)
            .ThenBy(i => i.DestinationImageId)
            .ToListAsync(ct);

    /// <summary>DisplayOrder is 1-based (matches the seeders).</summary>
    private static void Renumber(IList<DestinationImage> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
            if (ordered[i].DisplayOrder != i + 1) ordered[i].DisplayOrder = i + 1;
    }

    private static void DemoteHeroes(IEnumerable<DestinationImage> images, Guid? exceptId)
    {
        foreach (var other in images)
            if (other.ImageType == "Hero" && other.DestinationImageId != exceptId)
                other.ImageType = "Gallery";
    }

    private static string? NormalizeImageType(string? value)
        => ImageTypes.FirstOrDefault(t => string.Equals(t, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    // ══ Helpers: taxonomy ═════════════════════════════════════════════════════

    private static readonly Expression<Func<Tag, AdminTagDto>> ToTagDto = t => new AdminTagDto(
        t.TagId, t.Key, t.TagName, t.TargetPersonaType, t.DestinationTags.Count);

    private static readonly Expression<Func<Category, AdminCategoryDto>> ToCategoryDto = c => new AdminCategoryDto(
        c.CategoryId, c.Key, c.CategoryName, c.CategoryDescription, c.IconName, c.ColorHex, c.DestinationCategories.Count);

    private static readonly Expression<Func<Country, AdminCountryDto>> ToCountryDto = c => new AdminCountryDto(
        c.CountryId,
        c.CountryName,
        c.Continent,
        c.CountryFlagEmoji,
        c.GlobalHeroImage,
        c.VisaRequirementsSummary,
        c.PowerPlugType,
        c.DrivingSide,
        c.EstimatedDailyTaxRate,
        c.Cities.Count,
        c.Destinations.Count);

    private static readonly Expression<Func<City, AdminCityDto>> ToCityDto = c => new AdminCityDto(
        c.CityId,
        c.CityName,
        c.CityDescription,
        c.Latitude,
        c.Longitude,
        c.CountryId,
        c.Country.CountryName,
        c.DestinationCities.Count);

    /// <summary>Lowercase snake_case, e.g. "hidden_gem". Returns the trimmed key, or null after adding an error.</summary>
    private string? ValidateKey(string? value, string field)
    {
        var key = value?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            ModelState.AddModelError(field, "Key is required.");
            return null;
        }
        if (key.Length > 50 || !SnakeCaseKey.IsMatch(key))
        {
            ModelState.AddModelError(field, "Key must be lowercase snake_case (a-z, 0-9, single underscores), starting with a letter, max 50 characters.");
            return null;
        }
        return key;
    }

    private string? RequireText(string? value, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            ModelState.AddModelError(field, $"{field} cannot be blank.");
            return null;
        }
        return trimmed;
    }

    /// <summary>"" / null → null (cleared); otherwise must be "#rrggbb". Returned lowercase.</summary>
    private string? ValidateColor(string? value, string field)
    {
        var color = NullIfBlank(value);
        if (color is null) return null;
        if (!HexColor.IsMatch(color))
        {
            ModelState.AddModelError(field, "ColorHex must look like #6366f1.");
            return null;
        }
        return color.ToLowerInvariant();
    }

    private string? ValidateOptionalUrl(string? value, string field)
    {
        var url = NullIfBlank(value);
        if (url is not null && !IsHttpUrl(url))
        {
            ModelState.AddModelError(field, $"{field} must be an absolute http(s) URL.");
            return null;
        }
        return url;
    }

    private string? ValidateDrivingSide(string? value, string field)
    {
        var side = NullIfBlank(value);
        if (side is null) return null;
        if (string.Equals(side, "left", StringComparison.OrdinalIgnoreCase)) return "Left";
        if (string.Equals(side, "right", StringComparison.OrdinalIgnoreCase)) return "Right";
        ModelState.AddModelError(field, "DrivingSide must be \"Left\" or \"Right\".");
        return null;
    }

    // ══ Helpers: shared ═══════════════════════════════════════════════════════

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
