using System.Linq.Expressions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for partners, partner links, click stats and partner leads.
///
/// Partners
///   GET    /api/admin/partners
///   GET    /api/admin/partners/{id}
///   POST   /api/admin/partners
///   PATCH  /api/admin/partners/{id}
///   POST   /api/admin/partners/{id}/activate | /deactivate
///   GET    /api/admin/partners/{id}/stats?from=&amp;to=
/// Links
///   GET    /api/admin/partner-links?partnerId=&amp;destinationId=&amp;wishlistId=&amp;active=
///   GET    /api/admin/partner-links/{linkId}
///   POST   /api/admin/partner-links
///   PATCH  /api/admin/partner-links/{linkId}
///   DELETE /api/admin/partner-links/{linkId}
///   GET    /api/admin/partner-links/{linkId}/stats?from=&amp;to=
/// Leads
///   GET    /api/admin/partner-leads?page=&amp;pageSize=&amp;search=&amp;status=
///   GET    /api/admin/partner-leads/{id}
///   PATCH  /api/admin/partner-leads/{id}
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin")]
public class AdminPartnersController : ControllerBase
{
    private const int DefaultStatsDays = 30;
    private const int MaxStatsDays = 366;

    private readonly HodracDbContext _db;
    private readonly ILogger<AdminPartnersController> _logger;

    public AdminPartnersController(HodracDbContext db, ILogger<AdminPartnersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ══ Partners ══════════════════════════════════════════════════════════════

    [HttpGet("partners")]
    public async Task<ActionResult<List<AdminPartnerSummaryDto>>> GetPartners(CancellationToken ct = default)
    {
        var partners = await _db.Partners
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new AdminPartnerSummaryDto(
                p.PartnerId,
                p.Name,
                p.LogoUrl,
                p.DefaultCtaLabel,
                p.IsActive,
                p.CreatedAt,
                p.Links.Count,
                p.Links.Count(l => l.IsActive)))
            .ToListAsync(ct);

        return Ok(partners);
    }

    [HttpGet("partners/{id:guid}")]
    public async Task<ActionResult<AdminPartnerDetailDto>> GetPartner(Guid id, CancellationToken ct = default)
    {
        var detail = await LoadPartnerDetail(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("partners")]
    public async Task<ActionResult<AdminPartnerDetailDto>> CreatePartner(CreatePartnerRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            ModelState.AddModelError(nameof(request.Name), "Name is required.");

        var logoUrl = NullIfBlank(request.LogoUrl);
        if (logoUrl is not null && !IsHttpUrl(logoUrl))
            ModelState.AddModelError(nameof(request.LogoUrl), "LogoUrl must be an absolute http(s) URL.");

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var partner = new Partner
        {
            Name = request.Name.Trim(),
            LogoUrl = logoUrl,
            DefaultCtaLabel = NullIfBlank(request.DefaultCtaLabel) ?? "Book now",
            IsActive = request.IsActive
        };

        _db.Partners.Add(partner);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created partner {PartnerId} ({Name})", partner.PartnerId, partner.Name);

        var detail = await LoadPartnerDetail(partner.PartnerId, ct);
        return CreatedAtAction(nameof(GetPartner), new { id = partner.PartnerId }, detail);
    }

    [HttpPatch("partners/{id:guid}")]
    public async Task<ActionResult<AdminPartnerDetailDto>> UpdatePartner(Guid id, UpdatePartnerRequest request, CancellationToken ct = default)
    {
        var partner = await _db.Partners.FirstOrDefaultAsync(p => p.PartnerId == id, ct);
        if (partner is null) return NotFound();

        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
            ModelState.AddModelError(nameof(request.Name), "Name cannot be blank.");

        if (request.LogoUrl is not null)
        {
            var logoUrl = NullIfBlank(request.LogoUrl);
            if (logoUrl is not null && !IsHttpUrl(logoUrl))
                ModelState.AddModelError(nameof(request.LogoUrl), "LogoUrl must be an absolute http(s) URL.");
            else
                partner.LogoUrl = logoUrl;
        }

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (request.Name is not null) partner.Name = request.Name.Trim();
        if (request.DefaultCtaLabel is not null) partner.DefaultCtaLabel = NullIfBlank(request.DefaultCtaLabel) ?? "Book now";
        if (request.IsActive is not null) partner.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync(ct);

        return Ok(await LoadPartnerDetail(id, ct));
    }

    [HttpPost("partners/{id:guid}/activate")]
    public Task<IActionResult> ActivatePartner(Guid id, CancellationToken ct = default)
        => SetPartnerActive(id, true, ct);

    [HttpPost("partners/{id:guid}/deactivate")]
    public Task<IActionResult> DeactivatePartner(Guid id, CancellationToken ct = default)
        => SetPartnerActive(id, false, ct);

    private async Task<IActionResult> SetPartnerActive(Guid id, bool isActive, CancellationToken ct)
    {
        var updated = await _db.Partners
            .Where(p => p.PartnerId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, isActive), ct);

        if (updated == 0) return NotFound();

        _logger.LogInformation("Admin set partner {PartnerId} IsActive={IsActive}", id, isActive);
        return NoContent();
    }

    [HttpGet("partners/{id:guid}/stats")]
    public async Task<ActionResult<ClickStatsDto>> GetPartnerStats(
        Guid id,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        if (!await _db.Partners.AnyAsync(p => p.PartnerId == id, ct)) return NotFound();

        if (!TryResolveRange(from, to, out var rangeFrom, out var rangeTo))
            return ValidationProblem(ModelState);

        var clicks = _db.PartnerLinkClicks.Where(c => c.Link.PartnerId == id);
        return Ok(await BuildStats(clicks, rangeFrom, rangeTo, ct));
    }

    // ══ Partner links ═════════════════════════════════════════════════════════

    [HttpGet("partner-links")]
    public async Task<ActionResult<List<AdminPartnerLinkDto>>> GetLinks(
        [FromQuery] Guid? partnerId = null,
        [FromQuery] Guid? destinationId = null,
        [FromQuery] Guid? wishlistId = null,
        [FromQuery] bool? active = null,
        CancellationToken ct = default)
    {
        var query = _db.PartnerLinks.AsNoTracking();

        if (partnerId is not null) query = query.Where(l => l.PartnerId == partnerId);
        if (destinationId is not null) query = query.Where(l => l.DestinationId == destinationId);
        if (wishlistId is not null) query = query.Where(l => l.WishlistId == wishlistId);
        if (active is not null) query = query.Where(l => l.IsActive == active.Value);

        var links = await query
            .OrderBy(l => l.Partner.Name)
            .ThenByDescending(l => l.Priority)
            .ThenByDescending(l => l.CreatedAt)
            .Select(ToLinkDto)
            .ToListAsync(ct);

        return Ok(links);
    }

    [HttpGet("partner-links/{linkId:guid}")]
    public async Task<ActionResult<AdminPartnerLinkDto>> GetLink(Guid linkId, CancellationToken ct = default)
    {
        var link = await LoadLinkDto(linkId, ct);
        return link is null ? NotFound() : Ok(link);
    }

    [HttpPost("partner-links")]
    public async Task<ActionResult<AdminPartnerLinkDto>> CreateLink(CreatePartnerLinkRequest request, CancellationToken ct = default)
    {
        if (!await _db.Partners.AnyAsync(p => p.PartnerId == request.PartnerId, ct))
            ModelState.AddModelError(nameof(request.PartnerId), "Partner not found.");

        var startsAt = ToUtc(request.StartsAt);
        var endsAt = ToUtc(request.EndsAt);
        var bookingUrl = request.BookingUrl?.Trim() ?? string.Empty;

        await ValidateTarget(request.DestinationId, request.WishlistId, ct);
        ValidateBookingUrl(bookingUrl);
        ValidateWindow(startsAt, endsAt);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var link = new PartnerLink
        {
            PartnerId = request.PartnerId,
            DestinationId = request.DestinationId,
            WishlistId = request.WishlistId,
            BookingUrl = bookingUrl,
            CtaLabel = NullIfBlank(request.CtaLabel),
            IsSponsored = request.IsSponsored,
            Priority = request.Priority,
            IsActive = request.IsActive,
            StartsAt = startsAt,
            EndsAt = endsAt
        };

        _db.PartnerLinks.Add(link);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin created partner link {LinkId} for partner {PartnerId}", link.LinkId, link.PartnerId);

        var dto = await LoadLinkDto(link.LinkId, ct);
        return CreatedAtAction(nameof(GetLink), new { linkId = link.LinkId }, dto);
    }

    [HttpPatch("partner-links/{linkId:guid}")]
    public async Task<ActionResult<AdminPartnerLinkDto>> UpdateLink(Guid linkId, UpdatePartnerLinkRequest request, CancellationToken ct = default)
    {
        var link = await _db.PartnerLinks.FirstOrDefaultAsync(l => l.LinkId == linkId, ct);
        if (link is null) return NotFound();

        // Retarget only when a new target is supplied; validate the resulting pair.
        var destinationId = link.DestinationId;
        var wishlistId = link.WishlistId;
        if (request.DestinationId is not null || request.WishlistId is not null)
        {
            destinationId = request.DestinationId;
            wishlistId = request.WishlistId;
            await ValidateTarget(destinationId, wishlistId, ct);
        }

        var bookingUrl = link.BookingUrl;
        if (request.BookingUrl is not null)
        {
            bookingUrl = request.BookingUrl.Trim();
            ValidateBookingUrl(bookingUrl);
        }

        var startsAt = request.ClearStartsAt ? null : request.StartsAt is not null ? ToUtc(request.StartsAt) : link.StartsAt;
        var endsAt = request.ClearEndsAt ? null : request.EndsAt is not null ? ToUtc(request.EndsAt) : link.EndsAt;
        ValidateWindow(startsAt, endsAt);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        link.DestinationId = destinationId;
        link.WishlistId = wishlistId;
        link.BookingUrl = bookingUrl;
        link.StartsAt = startsAt;
        link.EndsAt = endsAt;
        if (request.CtaLabel is not null) link.CtaLabel = NullIfBlank(request.CtaLabel);
        if (request.IsSponsored is not null) link.IsSponsored = request.IsSponsored.Value;
        if (request.Priority is not null) link.Priority = request.Priority.Value;
        if (request.IsActive is not null) link.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync(ct);

        return Ok(await LoadLinkDto(linkId, ct));
    }

    /// <summary>Hard delete — also cascades the link's click history. Prefer PATCH isActive=false to keep stats.</summary>
    [HttpDelete("partner-links/{linkId:guid}")]
    public async Task<IActionResult> DeleteLink(Guid linkId, CancellationToken ct = default)
    {
        var deleted = await _db.PartnerLinks
            .Where(l => l.LinkId == linkId)
            .ExecuteDeleteAsync(ct);

        if (deleted == 0) return NotFound();

        _logger.LogInformation("Admin deleted partner link {LinkId}", linkId);
        return NoContent();
    }

    [HttpGet("partner-links/{linkId:guid}/stats")]
    public async Task<ActionResult<ClickStatsDto>> GetLinkStats(
        Guid linkId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        if (!await _db.PartnerLinks.AnyAsync(l => l.LinkId == linkId, ct)) return NotFound();

        if (!TryResolveRange(from, to, out var rangeFrom, out var rangeTo))
            return ValidationProblem(ModelState);

        var clicks = _db.PartnerLinkClicks.Where(c => c.LinkId == linkId);
        return Ok(await BuildStats(clicks, rangeFrom, rangeTo, ct));
    }

    // ══ Leads ═════════════════════════════════════════════════════════════════

    [HttpGet("partner-leads")]
    public async Task<ActionResult<AdminPagedResult<AdminPartnerLeadDto>>> GetLeads(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.PartnerLeads.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = PartnerLeadStatuses.Normalize(status);
            if (normalized is null)
            {
                ModelState.AddModelError(nameof(status), $"Status must be one of: {string.Join(", ", PartnerLeadStatuses.All)}.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(l => l.Status == normalized);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.CompanyName, pattern) ||
                EF.Functions.ILike(l.ContactName, pattern) ||
                EF.Functions.ILike(l.Email, pattern));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToLeadDto)
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminPartnerLeadDto>(items, page, pageSize, total));
    }

    [HttpGet("partner-leads/{id:guid}")]
    public async Task<ActionResult<AdminPartnerLeadDto>> GetLead(Guid id, CancellationToken ct = default)
    {
        var lead = await _db.PartnerLeads
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(ToLeadDto)
            .FirstOrDefaultAsync(ct);

        return lead is null ? NotFound() : Ok(lead);
    }

    [HttpPatch("partner-leads/{id:guid}")]
    public async Task<ActionResult<AdminPartnerLeadDto>> UpdateLead(Guid id, UpdatePartnerLeadRequest request, CancellationToken ct = default)
    {
        var lead = await _db.PartnerLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        if (request.Status is not null)
        {
            var normalized = PartnerLeadStatuses.Normalize(request.Status);
            if (normalized is null)
            {
                ModelState.AddModelError(nameof(request.Status), $"Status must be one of: {string.Join(", ", PartnerLeadStatuses.All)}.");
                return ValidationProblem(ModelState);
            }
            lead.Status = normalized;
        }

        if (request.InternalNotes is not null)
            lead.InternalNotes = NullIfBlank(request.InternalNotes);

        lead.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToLeadDtoCompiled(lead));
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    private static string TrackingPath(Guid linkId) => $"/api/out/{linkId}";

    private static readonly Expression<Func<PartnerLink, AdminPartnerLinkDto>> ToLinkDto = l => new AdminPartnerLinkDto(
        l.LinkId,
        l.PartnerId,
        l.Partner.Name,
        l.DestinationId,
        l.WishlistId,
        l.DestinationId != null ? "destination" : "wishlist",
        l.Destination != null ? l.Destination.DestinationName : l.Wishlist != null ? l.Wishlist.WishlistName : null,
        l.BookingUrl,
        TrackingPath(l.LinkId),
        l.CtaLabel,
        l.IsSponsored,
        l.Priority,
        l.IsActive,
        l.StartsAt,
        l.EndsAt,
        l.CreatedAt);

    private static readonly Expression<Func<PartnerLead, AdminPartnerLeadDto>> ToLeadDto = l => new AdminPartnerLeadDto(
        l.Id,
        l.CompanyName,
        l.ContactName,
        l.Email,
        l.Website,
        l.BusinessType,
        l.Interests,
        l.Message,
        l.Status,
        l.InternalNotes,
        l.CreatedAt,
        l.UpdatedAt);

    private static readonly Func<PartnerLead, AdminPartnerLeadDto> ToLeadDtoCompiled = ToLeadDto.Compile();

    private Task<AdminPartnerLinkDto?> LoadLinkDto(Guid linkId, CancellationToken ct)
        => _db.PartnerLinks
            .AsNoTracking()
            .Where(l => l.LinkId == linkId)
            .Select(ToLinkDto)
            .FirstOrDefaultAsync(ct);

    private async Task<AdminPartnerDetailDto?> LoadPartnerDetail(Guid id, CancellationToken ct)
    {
        var partner = await _db.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartnerId == id, ct);
        if (partner is null) return null;

        var links = await _db.PartnerLinks
            .AsNoTracking()
            .Where(l => l.PartnerId == id)
            .OrderByDescending(l => l.Priority)
            .ThenByDescending(l => l.CreatedAt)
            .Select(ToLinkDto)
            .ToListAsync(ct);

        return new AdminPartnerDetailDto(
            partner.PartnerId,
            partner.Name,
            partner.LogoUrl,
            partner.DefaultCtaLabel,
            partner.IsActive,
            partner.CreatedAt,
            links);
    }

    private async Task ValidateTarget(Guid? destinationId, Guid? wishlistId, CancellationToken ct)
    {
        if ((destinationId is null) == (wishlistId is null))
        {
            ModelState.AddModelError("Target", "Exactly one of DestinationId or WishlistId must be set.");
            return;
        }

        if (destinationId is not null && !await _db.Destinations.AnyAsync(d => d.DestinationId == destinationId, ct))
            ModelState.AddModelError("DestinationId", "Destination not found.");

        if (wishlistId is not null && !await _db.Wishlists.AnyAsync(w => w.WishlistId == wishlistId, ct))
            ModelState.AddModelError("WishlistId", "Wishlist not found.");
    }

    private void ValidateBookingUrl(string bookingUrl)
    {
        if (!IsHttpUrl(bookingUrl))
            ModelState.AddModelError("BookingUrl", "BookingUrl must be an absolute http(s) URL.");
    }

    private void ValidateWindow(DateTime? startsAt, DateTime? endsAt)
    {
        if (startsAt is not null && endsAt is not null && endsAt <= startsAt)
            ModelState.AddModelError("EndsAt", "EndsAt must be after StartsAt.");
    }

    /// <summary>
    /// [from, to) in UTC. Defaults to the last 30 days ending now; ranges longer
    /// than 366 days are clamped by moving <c>from</c> forward.
    /// </summary>
    private bool TryResolveRange(DateTime? from, DateTime? to, out DateTime rangeFrom, out DateTime rangeTo)
    {
        rangeTo = ToUtc(to) ?? DateTime.UtcNow;
        rangeFrom = ToUtc(from) ?? rangeTo.AddDays(-DefaultStatsDays);

        if (rangeFrom >= rangeTo)
        {
            ModelState.AddModelError("from", "'from' must be before 'to'.");
            return false;
        }

        if (rangeTo - rangeFrom > TimeSpan.FromDays(MaxStatsDays))
            rangeFrom = rangeTo.AddDays(-MaxStatsDays);

        return true;
    }

    private static async Task<ClickStatsDto> BuildStats(
        IQueryable<PartnerLinkClick> clicks, DateTime from, DateTime to, CancellationToken ct)
    {
        var inRange = clicks.Where(c => c.ClickedAt >= from && c.ClickedAt < to);

        var total = await inRange.CountAsync(ct);

        var uniqueUsers = await inRange
            .Where(c => c.UserId != null)
            .Select(c => c.UserId)
            .Distinct()
            .CountAsync(ct);

        // ClickedAt is timestamptz; Npgsql translates .Date to date_trunc('day', ..., 'UTC').
        var daily = await inRange
            .GroupBy(c => c.ClickedAt.Date)
            .Select(g => new { Day = g.Key, Clicks = g.Count() })
            .OrderBy(x => x.Day)
            .ToListAsync(ct);

        return new ClickStatsDto(
            from,
            to,
            total,
            uniqueUsers,
            daily.Select(d => new DailyClicksDto(DateOnly.FromDateTime(d.Day), d.Clicks)).ToList());
    }

    /// <summary>Npgsql requires UTC for timestamptz; treat unspecified as UTC.</summary>
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
    };

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
