using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Interfaces
{
    public interface IWishlistRepository
    {
        // Templates (platform-curated, IsTemplate = true)
        Task<(List<Wishlist> Items, int TotalCount)> GetTemplatesAsync(
            int page, int pageSize, CancellationToken ct = default);

        //Get All Wishlists
        Task<List<Wishlist>> GetAllWishlists();

        Task<List<Wishlist>> SearchByNameAsync(
    string query, Guid? ownerUserId, int limit = 10, CancellationToken ct = default);

        Task<Wishlist?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default);

        /// <summary>Card-weight template lookup (no itinerary). Null if missing or not a template.</summary>
        Task<Wishlist?> GetTemplateCardAsync(Guid id, CancellationToken ct = default);

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

        /// <summary>
        /// Loads the tracked user wishlist (no related rows), applies <paramref name="applyChanges"/>
        /// and saves only the modified scalar columns. When <paramref name="clientXmin"/> is
        /// supplied it is used as the xmin original value, so a stale edit throws
        /// DbUpdateConcurrencyException (mapped to HTTP 409). Returns false if not found / template.
        /// </summary>
        Task<bool> UpdateAsync(
            Guid wishlistId, Action<Wishlist> applyChanges, uint? clientXmin, CancellationToken ct = default);

        // Itinerary editing (user wishlists only — tracked entities)
        Task<Wishlist?> GetForItineraryEditAsync(Guid wishlistId, CancellationToken ct = default);
        void AddItineraryDay(ItineraryDay day);
        void RemoveItineraryDay(ItineraryDay day);
        void AddItineraryItem(ItineraryItem item);
        void RemoveItineraryItem(ItineraryItem item);
        Task<bool> CityExistsAsync(Guid cityId, CancellationToken ct = default);
        Task<bool> TransitRouteExistsAsync(Guid routeId, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);
        /// <summary>Sets the client xmin as the tracked wishlist's concurrency original value (stale → 409).</summary>
        void SetXminOriginalValue(Wishlist trackedWishlist, uint clientXmin);

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
    }
}
