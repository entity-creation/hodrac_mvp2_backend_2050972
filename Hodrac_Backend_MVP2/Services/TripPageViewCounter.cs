using Hodrac_Backend_MVP2.Data;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services;

// ─── Trip page views ──────────────────────────────────────────────────────────
// Counts human views of a trip page (GET /api/trips/public/{id} and the
// /share/trip/{id} preview page) in TripPageViewsDaily, one row per trip, WAT
// day and source. Feeds "page views" in the host weekly report.
// Best effort: crawlers / prefetches are skipped and failures are only logged,
// so counting can never fail the request.

public interface ITripPageViewCounter
{
    Task RecordAsync(Guid tripPostId, string source, HttpRequest request);
}

public class TripPageViewCounter : ITripPageViewCounter
{
    // Reports run on West Africa Time (UTC+1, no DST).
    private static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    private readonly HodracDbContext _db;
    private readonly ILogger<TripPageViewCounter> _logger;

    public TripPageViewCounter(HodracDbContext db, ILogger<TripPageViewCounter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public static DateOnly TodayWat(DateTime utcNow) => DateOnly.FromDateTime(utcNow + Wat);

    public async Task RecordAsync(Guid tripPostId, string source, HttpRequest request)
    {
        if (BotDetector.IsNonHuman(request)) return;

        try
        {
            var date = TodayWat(DateTime.UtcNow);
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""TripPageViewsDaily"" (""TripPostId"", ""Date"", ""Source"", ""Count"")
                VALUES ({tripPostId}, {date}, {source}, 1)
                ON CONFLICT (""TripPostId"", ""Date"", ""Source"")
                DO UPDATE SET ""Count"" = ""TripPageViewsDaily"".""Count"" + 1");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record a {Source} page view for trip {TripPostId}", source, tripPostId);
        }
    }
}
