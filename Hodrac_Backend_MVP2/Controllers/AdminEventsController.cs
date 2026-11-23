using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.EventDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Images;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for events.
///
///   GET    /api/admin/events?page=&amp;pageSize=&amp;search=&amp;cityId=&amp;destinationId=&amp;published=&amp;upcoming=
///   GET    /api/admin/events/{id}
///   POST   /api/admin/events
///   PATCH  /api/admin/events/{id}
///   POST   /api/admin/events/{id}/publish | /unpublish
///   DELETE /api/admin/events/{id}
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/events")]
public class AdminEventsController : ControllerBase
{
    private readonly HodracDbContext _db;
    private readonly ILogger<AdminEventsController> _logger;
    private readonly ImageUrlNormalizer _images;

    public AdminEventsController(HodracDbContext db, ILogger<AdminEventsController> logger, ImageUrlNormalizer images)
    {
        _db = db;
        _logger = logger;
        _images = images;
    }

    /// <summary>
    /// published / upcoming: null = no filter. search matches name, organiser, venue name and slug.
    /// Ordered by StartsAt ascending.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AdminPagedResult<AdminEventDto>>> GetEvents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? cityId = null,
        [FromQuery] Guid? destinationId = null,
        [FromQuery] bool? published = null,
        [FromQuery] bool? upcoming = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var now = DateTime.UtcNow;

        var query = _db.Events.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.Name, pattern) ||
                EF.Functions.ILike(e.Slug, pattern) ||
                (e.Organiser != null && EF.Functions.ILike(e.Organiser, pattern)) ||
                (e.VenueName != null && EF.Functions.ILike(e.VenueName, pattern)));
        }

        if (cityId is not null) query = query.Where(e => e.CityId == cityId);
        if (destinationId is not null) query = query.Where(e => e.DestinationId == destinationId);
        if (published is not null) query = query.Where(e => e.IsPublished == published.Value);
        if (upcoming == true) query = query.Upcoming(now);
        if (upcoming == false) query = query.Where(e => (e.EndsAt ?? e.StartsAt) < now);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(EventQueries.ToAdminDto(now))
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminEventDto>(items, page, pageSize, total));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminEventDto>> GetEvent(Guid id, CancellationToken ct = default)
    {
        var dto = await LoadDto(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<AdminEventDto>> CreateEvent(CreateEventRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            ModelState.AddModelError(nameof(request.Name), "Name is required.");

        if (request.StartsAt == default)
            ModelState.AddModelError(nameof(request.StartsAt), "StartsAt is required.");

        var startsAt = request.StartsAt.UtcDateTime;
        var endsAt = request.EndsAt?.UtcDateTime;
        ValidateWindow(startsAt, endsAt);

        var timeZone = ValidateTimeZone(request.TimeZone);
        var currency = ValidateCurrency(request.Currency);
        ValidatePrices(request.PriceMinKobo, request.PriceMaxKobo);

        var ticketUrl = ValidateUrl(nameof(request.TicketUrl), request.TicketUrl);
        var websiteUrl = ValidateUrl(nameof(request.WebsiteUrl), request.WebsiteUrl);
        var imageUrl = ValidateUrl(nameof(request.ImageUrl), _images.Normalize(request.ImageUrl));

        var cityId = await ResolveCityAsync(request.DestinationId, request.CityId, ct);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var name = request.Name.Trim();
        var ev = new Event
        {
            Name = name,
            Slug = await EventQueries.UniqueSlugAsync(_db, EventQueries.BaseSlug(name, startsAt), ct),
            Description = request.Description?.Trim() ?? string.Empty,
            Organiser = NullIfBlank(request.Organiser),
            StartsAt = startsAt,
            EndsAt = endsAt,
            TimeZone = string.IsNullOrEmpty(timeZone) ? Event.DefaultTimeZone : timeZone,
            DestinationId = request.DestinationId,
            VenueName = NullIfBlank(request.VenueName),
            VenueAddress = NullIfBlank(request.VenueAddress),
            CityId = cityId!.Value,
            TicketUrl = ticketUrl,
            PriceMinKobo = request.PriceMinKobo,
            PriceMaxKobo = request.PriceMaxKobo,
            Currency = currency ?? Event.DefaultCurrency,
            PriceNote = NullIfBlank(request.PriceNote),
            InstagramHandle = NormalizeHandle(request.InstagramHandle),
            WebsiteUrl = websiteUrl,
            ImageUrl = imageUrl,
            IsPublished = request.IsPublished,
        };

        _db.Events.Add(ev);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created event {EventId} ({Slug})", ev.Id, ev.Slug);

        var dto = await LoadDto(ev.Id, ct);
        return CreatedAtAction(nameof(GetEvent), new { id = ev.Id }, dto);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<AdminEventDto>> UpdateEvent(Guid id, UpdateEventRequest request, CancellationToken ct = default)
    {
        var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound();

        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
            ModelState.AddModelError(nameof(request.Name), "Name cannot be blank.");

        // Validate the resulting state, not just the fields sent.
        var startsAt = request.StartsAt?.UtcDateTime ?? ev.StartsAt;
        var endsAt = request.ClearEndsAt == true ? null : request.EndsAt?.UtcDateTime ?? ev.EndsAt;
        ValidateWindow(startsAt, endsAt);

        var timeZone = request.TimeZone is null ? null : ValidateTimeZone(request.TimeZone);
        var currency = request.Currency is null ? null : ValidateCurrency(request.Currency);

        var priceMin = request.ClearPrices == true ? null : request.PriceMinKobo ?? ev.PriceMinKobo;
        var priceMax = request.ClearPrices == true ? null : request.PriceMaxKobo ?? ev.PriceMaxKobo;
        ValidatePrices(priceMin, priceMax);

        var ticketUrl = request.TicketUrl is null ? ev.TicketUrl : ValidateUrl(nameof(request.TicketUrl), request.TicketUrl);
        var websiteUrl = request.WebsiteUrl is null ? ev.WebsiteUrl : ValidateUrl(nameof(request.WebsiteUrl), request.WebsiteUrl);
        var imageUrl = request.ImageUrl is null ? ev.ImageUrl : ValidateUrl(nameof(request.ImageUrl), _images.Normalize(request.ImageUrl));

        var destinationId = request.ClearDestination == true ? null : request.DestinationId ?? ev.DestinationId;
        Guid? cityId = ev.CityId;
        if (request.CityId is not null || (request.DestinationId is not null && request.ClearDestination != true))
        {
            // A new destination without an explicit city moves the event to that destination's city.
            cityId = await ResolveCityAsync(
                request.ClearDestination == true ? null : request.DestinationId,
                request.CityId, ct);
        }

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (request.Name is not null) ev.Name = request.Name.Trim();
        if (request.Description is not null) ev.Description = request.Description.Trim();
        if (request.Organiser is not null) ev.Organiser = NullIfBlank(request.Organiser);
        ev.StartsAt = startsAt;
        ev.EndsAt = endsAt;
        if (!string.IsNullOrEmpty(timeZone)) ev.TimeZone = timeZone;
        else if (timeZone == string.Empty) ev.TimeZone = Event.DefaultTimeZone;
        ev.DestinationId = destinationId;
        if (request.VenueName is not null) ev.VenueName = NullIfBlank(request.VenueName);
        if (request.VenueAddress is not null) ev.VenueAddress = NullIfBlank(request.VenueAddress);
        ev.CityId = cityId!.Value;
        ev.TicketUrl = ticketUrl;
        ev.PriceMinKobo = priceMin;
        ev.PriceMaxKobo = priceMax;
        if (currency is not null) ev.Currency = currency;
        if (request.PriceNote is not null) ev.PriceNote = NullIfBlank(request.PriceNote);
        if (request.InstagramHandle is not null) ev.InstagramHandle = NormalizeHandle(request.InstagramHandle);
        ev.WebsiteUrl = websiteUrl;
        ev.ImageUrl = imageUrl;
        if (request.IsPublished is not null) ev.IsPublished = request.IsPublished.Value;
        ev.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(await LoadDto(id, ct));
    }

    [HttpPost("{id:guid}/publish")]
    public Task<IActionResult> Publish(Guid id, CancellationToken ct = default) => SetPublished(id, true, ct);

    [HttpPost("{id:guid}/unpublish")]
    public Task<IActionResult> Unpublish(Guid id, CancellationToken ct = default) => SetPublished(id, false, ct);

    private async Task<IActionResult> SetPublished(Guid id, bool isPublished, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var updated = await _db.Events
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.IsPublished, isPublished)
                .SetProperty(e => e.UpdatedAt, now), ct);

        if (updated == 0) return NotFound();

        _logger.LogInformation("Admin set event {EventId} IsPublished={IsPublished}", id, isPublished);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid id, CancellationToken ct = default)
    {
        var deleted = await _db.Events.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) return NotFound();

        _logger.LogInformation("Admin deleted event {EventId}", id);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<AdminEventDto?> LoadDto(Guid id, CancellationToken ct)
        => _db.Events.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(EventQueries.ToAdminDto(DateTime.UtcNow))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Checks the destination (if any) and the city. With a destination and no city, the city
    /// defaults to the destination's first city (alphabetical). Returns null after adding a
    /// model error when it can't be resolved.
    /// </summary>
    private async Task<Guid?> ResolveCityAsync(Guid? destinationId, Guid? cityId, CancellationToken ct)
    {
        if (destinationId is not null)
        {
            var destination = await _db.Destinations
                .AsNoTracking()
                .Where(d => d.DestinationId == destinationId)
                .Select(d => new
                {
                    FirstCityId = d.DestinationCities
                        .OrderBy(dc => dc.City.CityName)
                        .Select(dc => (Guid?)dc.CityId)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(ct);

            if (destination is null)
            {
                ModelState.AddModelError("DestinationId", "Destination not found.");
                return null;
            }

            if (cityId is null)
            {
                if (destination.FirstCityId is null)
                    ModelState.AddModelError("CityId", "CityId is required: the destination has no city.");
                return destination.FirstCityId;
            }
        }

        if (cityId is null)
        {
            ModelState.AddModelError("CityId", "CityId is required (or give a DestinationId).");
            return null;
        }

        if (!await _db.Cities.AnyAsync(c => c.CityId == cityId, ct))
        {
            ModelState.AddModelError("CityId", "City not found.");
            return null;
        }

        return cityId;
    }

    private void ValidateWindow(DateTime startsAt, DateTime? endsAt)
    {
        if (endsAt is not null && endsAt < startsAt)
            ModelState.AddModelError("EndsAt", "EndsAt must not be before StartsAt.");
    }

    private void ValidatePrices(long? min, long? max)
    {
        if (min < 0) ModelState.AddModelError("PriceMinKobo", "PriceMinKobo must be 0 or more.");
        if (max < 0) ModelState.AddModelError("PriceMaxKobo", "PriceMaxKobo must be 0 or more.");
        if (min is not null && max is not null && min > max)
            ModelState.AddModelError("PriceMinKobo", "PriceMinKobo must not be more than PriceMaxKobo.");
    }

    /// <summary>Canonical IANA id, "" for blank input, or null (with a model error) when not allowed.</summary>
    private string? ValidateTimeZone(string? value)
    {
        var normalized = DestinationReferenceData.NormalizeTimeZone(value);
        if (normalized is null)
            ModelState.AddModelError("TimeZone",
                "Must be one of the time zone IDs from GET /api/admin/destination-options (e.g. \"Africa/Lagos\").");
        return normalized;
    }

    /// <summary>Upper-case ISO code, null for blank input (= default).</summary>
    private string? ValidateCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var code = value.Trim().ToUpperInvariant();
        if (code.Length != Event.CurrencyLength || !code.All(c => c is >= 'A' and <= 'Z'))
        {
            ModelState.AddModelError("Currency", "Currency must be a 3-letter ISO code (e.g. \"NGN\").");
            return null;
        }
        return code;
    }

    /// <summary>Trimmed URL, null for blank input; adds a model error unless absolute http(s).</summary>
    private string? ValidateUrl(string field, string? value)
    {
        var url = NullIfBlank(value);
        if (url is not null && !IsHttpUrl(url))
            ModelState.AddModelError(field, $"{field} must be an absolute http(s) URL.");
        return url;
    }

    private static string? NormalizeHandle(string? value)
    {
        var handle = NullIfBlank(value)?.TrimStart('@').Trim();
        return string.IsNullOrEmpty(handle) ? null : handle;
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
