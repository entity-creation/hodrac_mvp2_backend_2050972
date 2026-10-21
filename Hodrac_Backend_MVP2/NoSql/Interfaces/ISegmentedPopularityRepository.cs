using Hodrac_Backend_MVP2.NoSql.Models;

namespace Hodrac_Backend_MVP2.NoSql.Interfaces
{
    public interface ISegmentedPopularityRepository
    {
        Task<WishlistSegmentedPopularity?> GetByWishlistIdAsync(string wishlistId);
        Task<List<WishlistSegmentedPopularity>> GetAllStaleAsync(DateTime olderThan);
        Task UpsertAsync(WishlistSegmentedPopularity doc);

        /// <summary>
        /// Atomically increments one counter per segment dictionary for a wishlist
        /// (upserting the doc). Keys are sanitised; empty keys become "unknown".
        /// segmentKeys: dictionary field name (e.g. "explicit_traveler_types") → segment key.
        /// </summary>
        Task IncrementSegmentsAsync(string wishlistId, IReadOnlyDictionary<string, string> segmentKeys);

        /// <summary>Replaces (upserts) many docs in one unordered bulk write.</summary>
        Task ReplaceManyAsync(IReadOnlyCollection<WishlistSegmentedPopularity> docs, CancellationToken ct = default);

        /// <summary>Deletes docs whose last_computed_at is older than the cutoff. Returns the count.</summary>
        Task<long> DeleteComputedBeforeAsync(DateTime cutoff, CancellationToken ct = default);
    }
}
