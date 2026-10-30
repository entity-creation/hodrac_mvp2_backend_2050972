using System.Security.Cryptography;
using System.Text;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Events;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// GET  /api/out/{linkId}?c=&amp;src=  — logs a partner click, then 302s to the partner
///                                (c = signed user token from trackingUrl, src = click source)
/// POST /api/partner-leads    — public "partner with us" form
/// </summary>
[ApiController]
[AllowAnonymous]
public class PartnersController : ControllerBase
{
    private const string VisitorCookie = "hodrac_vid";

    private readonly HodracDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IBehaviorEventQueue _events;
    private readonly ILogger<PartnersController> _logger;

    public PartnersController(
        HodracDbContext db,
        ICurrentUserService currentUser,
        IDataProtectionProvider dataProtection,
        IBehaviorEventQueue events,
        ILogger<PartnersController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _dataProtection = dataProtection;
        _events = events;
        _logger = logger;
    }

    // ── GET /api/out/{linkId} ─────────────────────────────────────────────────
    // Deliberately still redirects for paused links so cached pages never
    // dead-end; the click is logged either way. Bots / prefetches are logged
    // with IsBot=true (and still redirected).

    [HttpGet("api/out/{linkId:guid}")]
    public async Task<IActionResult> Out(
        Guid linkId,
        [FromQuery(Name = "c")] string? clickToken = null,
        [FromQuery] string? src = null,
        CancellationToken ct = default)
    {
        var link = await _db.PartnerLinks
            .AsNoTracking()
            .Where(l => l.LinkId == linkId)
            .Select(l => new
            {
                l.LinkId,
                l.PartnerId,
                l.BookingUrl,
                l.AppendUtm,
                l.DestinationId,
                l.WishlistId,
                PartnerName = l.Partner.Name
            })
            .FirstOrDefaultAsync(ct);

        if (link is null) return NotFound();

        if (!Uri.TryCreate(link.BookingUrl, UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
        {
            _logger.LogWarning("Partner link {LinkId} has an invalid booking URL", linkId);
            return NotFound();
        }

        var source = PartnerClickSources.Normalize(src);
        var isBot = BotDetector.IsNonHuman(Request);
        var userId = PartnerClickToken.TryRead(_dataProtection, clickToken)
                     ?? (_currentUser.IsAuthenticated && _currentUser.UserId != Guid.Empty ? _currentUser.UserId : null);
        var visitorId = EnsureVisitorId();

        _db.PartnerLinkClicks.Add(new PartnerLinkClick
        {
            LinkId = linkId,
            UserId = userId,
            VisitorId = visitorId,
            Source = source,
            IsBot = isBot,
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

        if (userId is not null && !isBot)
            await PublishPartnerClickedAsync(userId.Value, link.LinkId, link.PartnerId, link.DestinationId, link.WishlistId, source, ct);

        var location = link.AppendUtm
            ? AppendUtm(target, Slugify(link.PartnerName), source)
            : target.AbsoluteUri;

        Response.Headers.CacheControl = "no-store";
        return Redirect(location);
    }

    /// <summary>
    /// Returns the visitor's hodrac_vid (random 128-bit, hex), issuing a new
    /// first-party cookie when it is missing or malformed. No IP is stored.
    /// </summary>
    private string EnsureVisitorId()
    {
        if (Request.Cookies.TryGetValue(VisitorCookie, out var existing)
            && existing is { Length: 32 } && existing.All(Uri.IsHexDigit))
            return existing.ToLowerInvariant();

        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Response.Cookies.Append(VisitorCookie, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/api/out",
            MaxAge = TimeSpan.FromDays(365)
        });
        return id;
    }

    /// <summary>
    /// Best-effort behaviour signal for logged-in, non-bot clicks: one small tag
    /// lookup, then an in-memory enqueue. Never fails the redirect.
    /// </summary>
    private async Task PublishPartnerClickedAsync(
        Guid userId, Guid linkId, Guid partnerId, Guid? destinationId, Guid? wishlistId, string source, CancellationToken ct)
    {
        try
        {
            List<string> tags;
            if (destinationId is not null)
            {
                tags = await _db.DestinationTags
                    .AsNoTracking()
                    .Where(dt => dt.DestinationId == destinationId)
                    .Select(dt => dt.Tag.TagName)
                    .ToListAsync(ct);
            }
            else
            {
                var json = await _db.Wishlists
                    .AsNoTracking()
                    .Where(w => w.WishlistId == wishlistId)
                    .Select(w => w.PsychologicalVibeTagsJson)
                    .FirstOrDefaultAsync(ct);
                tags = ProfileTagSignals.ParseTagJson(json);
            }

            _events.Publish(new PartnerClickedEvent(
                UserId: userId.ToString(),
                LinkId: linkId.ToString(),
                PartnerId: partnerId.ToString(),
                Source: source,
                TargetTags: tags,
                OccurredAt: DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish PartnerClickedEvent for link {LinkId}", linkId);
        }
    }

    /// <summary>
    /// Adds utm_source=hodrac, utm_medium=referral, utm_campaign=&lt;partner slug&gt;
    /// and utm_content=&lt;source&gt;, keeping the partner's existing parameters and
    /// never overwriting a key the partner already set. The fragment is preserved.
    /// </summary>
    private static string AppendUtm(Uri target, string campaign, string source)
    {
        var url = target.AbsoluteUri;
        var hashIndex = url.IndexOf('#');
        var fragment = hashIndex >= 0 ? url[hashIndex..] : string.Empty;
        var withoutFragment = hashIndex >= 0 ? url[..hashIndex] : url;

        var existing = QueryHelpers.ParseQuery(target.Query);
        var sb = new StringBuilder(withoutFragment);
        var hasQuery = withoutFragment.Contains('?');
        var needsSeparator = hasQuery && !withoutFragment.EndsWith('?') && !withoutFragment.EndsWith('&');

        void Add(string key, string value)
        {
            if (existing.ContainsKey(key)) return;
            if (!hasQuery) { sb.Append('?'); hasQuery = true; }
            else if (needsSeparator) sb.Append('&');
            sb.Append(key).Append('=').Append(Uri.EscapeDataString(value));
            needsSeparator = true;
        }

        Add("utm_source", "hodrac");
        Add("utm_medium", "referral");
        Add("utm_campaign", campaign);
        Add("utm_content", source);

        return sb.Append(fragment).ToString();
    }

    /// <summary>"Solo House" → "solo-house" (lowercase ASCII letters/digits, '-' separated, max 60).</summary>
    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "partner";
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug.Length == 0 ? "partner" : slug;
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
