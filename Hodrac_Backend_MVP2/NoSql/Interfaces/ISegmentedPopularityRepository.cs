using Hodrac_Backend_MVP2.NoSql.Models;

namespace Hodrac_Backend_MVP2.NoSql.Interfaces
{
    public interface ISegmentedPopularityRepository
    {
        Task<WishlistSegmentedPopularity?> GetByWishlistIdAsync(string wishlistId);
        Task<List<WishlistSegmentedPopularity>> GetAllStaleAsync(DateTime olderThan);
        Task UpsertAsync(WishlistSegmentedPopularity doc);
        Task IncrementSegmentAsync(string wishlistId, string segmentPath, string segmentKey);
    }
}
