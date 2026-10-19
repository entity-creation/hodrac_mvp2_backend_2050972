using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// GET  /api/out/{linkId}     — logs a partner click, then 302s to the partner
/// POST /api/partner-leads    — public "partner with us" form
/// </summary>
[ApiController]
[AllowAnonymous]
public class PartnersController : ControllerBase
{
    private readonly HodracDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PartnersController> _logger;

    public PartnersController(HodracDbContext db, ICurrentUserService currentUser, ILogger<PartnersController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── GET /api/out/{linkId} ─────────────────────────────────────────────────
    // Deliberately still redirects for paused links so cached pages never
    // dead-end; the click is logged either way.

    [HttpGet("api/out/{linkId:guid}")]
    public async Task<IActionResult> Out(Guid linkId, CancellationToken ct = default)
    {
        var link = await _db.PartnerLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.LinkId == linkId, ct);

        if (link is null) return NotFound();

        if (!Uri.TryCreate(link.BookingUrl, UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
        {
            _logger.LogWarning("Partner link {LinkId} has an invalid booking URL", linkId);
            return NotFound();
        }

        _db.PartnerLinkClicks.Add(new PartnerLinkClick
        {
            LinkId = linkId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            Referrer = Truncate(Request.Headers.Referer.ToString(), 2000),
            UserAgent = Truncate(Request.Headers.UserAgent.ToString(), 500)
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Never block the user's booking because logging failed.
            _logger.LogError(ex, "Failed to log click for partner link {LinkId}", linkId);
        }

        Response.Headers.CacheControl = "no-store";
        return Redirect(target.ToString());
    }

    // ── POST /api/partner-leads ───────────────────────────────────────────────

    [HttpPost("api/partner-leads")]
    [EnableRateLimiting("partner-leads")]
    public async Task<IActionResult> CreateLead(CreatePartnerLeadRequest request, CancellationToken ct = default)
    {
        var lead = new PartnerLead
        {
            CompanyName = request.CompanyName.Trim(),
            ContactName = request.ContactName.Trim(),
            Email = request.Email.Trim(),
            Website = NullIfBlank(request.Website),
            BusinessType = NullIfBlank(request.BusinessType),
            Interests = (request.Interests ?? new List<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Message = NullIfBlank(request.Message)
        };

        _db.PartnerLeads.Add(lead);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("New partner lead {LeadId} from {Company}", lead.Id, lead.CompanyName);

        return StatusCode(StatusCodes.Status201Created, new { lead.Id, lead.CreatedAt });
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
