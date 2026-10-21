using System.Security.Claims;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API — identity check and dashboard overview.
///
///   GET /api/admin/me              — { email, displayName, isAdmin: true } (403 for non-admins)
///   GET /api/admin/overview?days=  — platform totals + activity for the last N days (1–365, default 30)
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private const int DefaultOverviewDays = 30;
    private const int MaxOverviewDays = 365;

    private readonly HodracDbContext _db;

    public AdminController(HodracDbContext db)
    {
        _db = db;
    }

    // ══ Me ════════════════════════════════════════════════════════════════════

    /// <summary>Reaching this action means the Admin policy passed.</summary>
    [HttpGet("me")]
    public ActionResult<AdminMeDto> GetMe()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? string.Empty;
        var displayName = User.FindFirstValue("displayName") ?? string.Empty;
        return Ok(new AdminMeDto(email, displayName, true));
    }

    // ══ Overview ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Totals are all-time. The window covers the last <paramref name="days"/> UTC
    /// calendar days including today. Daily series are bucketed by UTC date in SQL
    /// and are sparse: days with zero rows are omitted.
    /// </summary>
    [HttpGet("overview")]
    public async Task<ActionResult<AdminOverviewDto>> GetOverview(
        [FromQuery] int days = DefaultOverviewDays,
        CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxOverviewDays);

        var windowStart = DateTime.UtcNow.Date.AddDays(-(days - 1));            // DateTime (Utc kind)
        var windowStartOffset = new DateTimeOffset(windowStart, TimeSpan.Zero); // for DateTimeOffset columns

        // ── Users: one aggregate query ────────────────────────────────────────
        var users = await _db.Users
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Confirmed = g.Count(u => u.EmailConfirmed),
                Onboarded = g.Count(u => u.HasCompletedOnboarding),
                Active = g.Count(u => u.LastActiveAt >= windowStartOffset)
            })
            .FirstOrDefaultAsync(ct);

        // ── Wishlists: templates vs user-owned ────────────────────────────────
        var wishlists = await _db.Wishlists
            .AsNoTracking()
            .GroupBy(w => w.IsTemplate)
            .Select(g => new { IsTemplate = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var destinations = await _db.Destinations.CountAsync(ct);

        // ── Trips by status ───────────────────────────────────────────────────
        var tripsByStatusRaw = await _db.TripPosts
            .AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var tripsByStatus = Enum.GetValues<TripPostStatus>()
            .ToDictionary(s => s.ToString(), s => tripsByStatusRaw.FirstOrDefault(x => x.Status == s)?.Count ?? 0);

        var pendingInterests = await _db.TripInterests
            .CountAsync(i => i.Status == TripInterestStatus.Requested, ct);

        var threadMessages = await _db.ThreadMessages.CountAsync(ct);
        var newsletter = await _db.NewsLetterEmails.CountAsync(ct);

        // ── Leads by status ───────────────────────────────────────────────────
        var leadsRaw = await _db.PartnerLeads
            .AsNoTracking()
            .GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var leadsByStatus = PartnerLeadStatuses.All
            .ToDictionary(s => s, s => leadsRaw.Where(x => x.Status == s).Sum(x => x.Count));

        var partnerClicks = await _db.PartnerLinkClicks.CountAsync(ct);

        // ── Daily series (UTC date buckets; zero days omitted) ────────────────
        // timestamptz → .Date translates to date_trunc('day', col, 'UTC').
        var newUsersDaily = await _db.Users
            .AsNoTracking()
            .Where(u => u.CreatedAt >= windowStartOffset)
            .GroupBy(u => u.CreatedAt.UtcDateTime.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToListAsync(ct);

        var newTripsDaily = await _db.TripPosts
            .AsNoTracking()
            .Where(t => t.CreatedAt >= windowStart)
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToListAsync(ct);

        var clicksDaily = await _db.PartnerLinkClicks
            .AsNoTracking()
            .Where(c => c.ClickedAt >= windowStart)
            .GroupBy(c => c.ClickedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToListAsync(ct);

        static List<AdminDailyCountDto> ToSeries(IEnumerable<(DateTime Day, int Count)> rows)
            => rows.Select(r => new AdminDailyCountDto(DateOnly.FromDateTime(r.Day), r.Count)).ToList();

        var totals = new AdminOverviewTotalsDto(
            Users: users?.Total ?? 0,
            ConfirmedUsers: users?.Confirmed ?? 0,
            OnboardedUsers: users?.Onboarded ?? 0,
            Templates: wishlists.Where(w => w.IsTemplate).Sum(w => w.Count),
            UserWishlists: wishlists.Where(w => !w.IsTemplate).Sum(w => w.Count),
            Destinations: destinations,
            TripsByStatus: tripsByStatus,
            OpenTrips: tripsByStatus[nameof(TripPostStatus.Open)],
            PendingInterests: pendingInterests,
            ThreadMessages: threadMessages,
            NewsletterSubscribers: newsletter,
            LeadsByStatus: leadsByStatus,
            PartnerClicks: partnerClicks);

        var window = new AdminOverviewWindowDto(
            ActiveUsers: users?.Active ?? 0,
            NewUsers: newUsersDaily.Sum(x => x.Count),
            NewTrips: newTripsDaily.Sum(x => x.Count),
            PartnerClicks: clicksDaily.Sum(x => x.Count),
            NewUsersPerDay: ToSeries(newUsersDaily.Select(x => (x.Day, x.Count))),
            NewTripsPerDay: ToSeries(newTripsDaily.Select(x => (x.Day, x.Count))),
            PartnerClicksPerDay: ToSeries(clicksDaily.Select(x => (x.Day, x.Count))));

        return Ok(new AdminOverviewDto(days, windowStart, totals, window));
    }
}
