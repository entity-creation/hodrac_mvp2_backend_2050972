using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.EventDtos;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Public (anonymous) event reads. Only published events are visible.
///
///   GET /api/events?cityId=&amp;destinationId=&amp;from=&amp;to=&amp;upcoming=true&amp;page=1&amp;pageSize=20
///   GET /api/events/{id}
///   GET /api/events/slug/{slug}
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly HodracDbContext _db;

    public EventsController(HodracDbContext db) => _db = db;

    /// <summary>
    /// upcoming=true (default): only events not yet finished ((EndsAt ?? StartsAt) >= now).
    /// from/to: events overlapping the window ((EndsAt ?? StartsAt) >= from, StartsAt &lt;= to).
    /// Ordered by StartsAt ascending.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<EventDto>>> GetEvents(
        [FromQuery] Guid? cityId,
        [FromQuery] Guid? destinationId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool upcoming = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.Events.AsNoTracking().Published();

        if (upcoming) query = query.Upcoming(DateTime.UtcNow);
        if (cityId is not null) query = query.Where(e => e.CityId == cityId);
        if (destinationId is not null) query = query.Where(e => e.DestinationId == destinationId);
        if (from is not null)
        {
            var fromUtc = from.Value.UtcDateTime;
            query = query.Where(e => (e.EndsAt ?? e.StartsAt) >= fromUtc);
        }
        if (to is not null)
        {
            var toUtc = to.Value.UtcDateTime;
            query = query.Where(e => e.StartsAt <= toUtc);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(EventQueries.ToDto)
            .ToListAsync(ct);

        return Ok(new PagedResult<EventDto>(
            Page: page,
            PageSize: pageSize,
            TotalCount: total,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            Items: items));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EventDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var dto = await _db.Events.AsNoTracking().Published()
            .Where(e => e.Id == id)
            .Select(EventQueries.ToDto)
            .FirstOrDefaultAsync(ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<EventDto>> GetBySlug(string slug, CancellationToken ct = default)
    {
        var key = slug.Trim().ToLowerInvariant();
        var dto = await _db.Events.AsNoTracking().Published()
            .Where(e => e.Slug == key)
            .Select(EventQueries.ToDto)
            .FirstOrDefaultAsync(ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}
