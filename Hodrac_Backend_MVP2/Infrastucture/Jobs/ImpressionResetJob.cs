using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.Jobs
{
    /// <summary>
    /// Runs at midnight UTC. Resets CurrentImpressionsToday to 0 for all paid
    /// entries so the daily impression cap is correctly enforced, then expires
    /// featured wishlists whose FeaturedUntil has passed.
    /// </summary>
    public class ImpressionResetJob
    {
        private readonly HodracDbContext _db;
        private readonly ILogger<ImpressionResetJob> _logger;

        public ImpressionResetJob(HodracDbContext db, ILogger<ImpressionResetJob> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken ct = default)
        {
            await _db.FeaturedWishlistPool
                .Where(p => p.PoolType == "Paid")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.CurrentImpressionsToday, 0)
                    .SetProperty(p => p.LastRotationDate, DateTimeOffset.UtcNow),
                ct);

            await ExpireFeaturedWishlistsAsync(DateTimeOffset.UtcNow, ct);
        }

        /// <summary>
        /// Wishlists with IsFeatured and FeaturedUntil &lt;= now are unfeatured
        /// (IsFeatured = false, FeaturedUntil = null), their Editorial pool entries
        /// are removed and the remaining Editorial entries renumbered 1..n.
        /// Paid / Random entries are untouched. Idempotent.
        /// </summary>
        /// <returns>(wishlists unfeatured, Editorial entries removed)</returns>
        public async Task<(int Unfeatured, int EditorialRemoved)> ExpireFeaturedWishlistsAsync(
            DateTimeOffset now, CancellationToken ct = default)
        {
            var expiredIds = await _db.Wishlists
                .Where(w => w.IsFeatured && w.FeaturedUntil != null && w.FeaturedUntil <= now)
                .Select(w => w.WishlistId)
                .ToListAsync(ct);

            if (expiredIds.Count == 0) return (0, 0);

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var unfeatured = await _db.Wishlists
                .Where(w => expiredIds.Contains(w.WishlistId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.IsFeatured, false)
                    .SetProperty(w => w.FeaturedUntil, (DateTimeOffset?)null), ct);

            var removed = await _db.FeaturedWishlistPool
                .Where(p => p.PoolType == EditorialPool.PoolType && expiredIds.Contains(p.WishlistId))
                .ExecuteDeleteAsync(ct);

            if (removed > 0)
            {
                await EditorialPool.RenumberAsync(_db, ct);
                await _db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Featured expiry: unfeatured {Unfeatured} wishlist(s), removed {Removed} Editorial pool entr(ies)",
                unfeatured, removed);

            return (unfeatured, removed);
        }
    }
}
