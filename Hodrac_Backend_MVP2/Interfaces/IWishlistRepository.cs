using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Interfaces
{
    public interface IWishlistRepository
    {
        // Templates (platform-curated, IsTemplate = true)
        Task<(List<Wishlist> Items, int TotalCount)> GetTemplatesAsync(
            int page, int pageSize, CancellationToken ct = default);

        Task<Wishlist?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default);

        Task<List<Wishlist>> GetPopularTemplatesAsync(
            List<Guid> rankedIds, int limit, CancellationToken ct = default);

        Task<List<Wishlist>> GetFeaturedAsync(
            List<Guid> featuredIds, CancellationToken ct = default);

        Task<List<Wishlist>> GetSimilarAsync(
            Guid wishlistId, int limit, CancellationToken ct = default);

        Task<List<Wishlist>> GetWishlistWithDestinationAsync(
           Guid destinationId, int limit, CancellationToken ct = default);

        // User wishlists (IsTemplate = false, OwnerUserId = current user)
        Task<List<Wishlist>> GetUserWishlistsAsync(
            Guid userId, CancellationToken ct = default);

        Task<Wishlist?> GetUserWishlistByIdAsync(
            Guid wishlistId, Guid userId, CancellationToken ct = default);

        Task<Wishlist> ForkAsync(
            Guid templateId, Guid newOwnerId, CancellationToken ct = default);

        Task<Wishlist> UpdateAsync(
            Wishlist wishlist, CancellationToken ct = default);

        Task DeleteUserWishlistAsync(
            Guid wishlistId, Guid userId, CancellationToken ct = default);

        // Save / bookmark
        Task<bool> SaveWishlistAsync(Guid wishlistId, Guid userId, CancellationToken ct = default);
        Task<bool> UnsaveWishlistAsync(Guid wishlistId, Guid userId, CancellationToken ct = default);
        Task<bool> IsWishlistSavedAsync(Guid wishlistId, Guid userId, CancellationToken ct = default);

        // Pricing snapshot
        Task<WishlistPricingSnapshot?> GetActiveSnapshotAsync(
            Guid wishlistId, CancellationToken ct = default);

        Task<WishlistPricingSnapshot> CreateSnapshotAsync(
            WishlistPricingSnapshot snapshot, CancellationToken ct = default);

        // Increment save count (called after successful save)
        Task IncrementSaveCountAsync(Guid wishlistId, CancellationToken ct = default);
        Task DecrementSaveCountAsync(Guid wishlistId, CancellationToken ct = default);

        /// <summary>
        /// Stamps the client-supplied xmin value onto the tracked entity so EF Core
        /// includes it in the WHERE clause of the next UPDATE, enabling optimistic
        /// concurrency detection. If another session modified the row between the
        /// client's read and this write, PostgreSQL's xmin will have changed and
        /// EF will throw DbUpdateConcurrencyException (mapped to HTTP 409).
        /// </summary>
        void SetRowVersion(Wishlist wishlist, uint xmin);
    }
}
