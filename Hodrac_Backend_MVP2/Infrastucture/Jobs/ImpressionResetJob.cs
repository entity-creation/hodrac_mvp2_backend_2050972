using Hodrac_Backend_MVP2.Data;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.Jobs
{
    /// <summary>
    /// Runs at midnight UTC. Resets CurrentImpressionsToday to 0 for all paid
    /// entries so the daily impression cap is correctly enforced.
    /// </summary>
    public class ImpressionResetJob
    {
        private readonly HodracDbContext _db;

        public ImpressionResetJob(HodracDbContext db) => _db = db;

        public async Task RunAsync(CancellationToken ct = default)
        {
            await _db.FeaturedWishlistPool
                .Where(p => p.PoolType == "Paid")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.CurrentImpressionsToday, 0)
                    .SetProperty(p => p.LastRotationDate, DateTimeOffset.UtcNow),
                ct);
        }
    }
}
