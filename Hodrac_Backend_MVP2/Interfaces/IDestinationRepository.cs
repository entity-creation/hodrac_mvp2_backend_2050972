using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Interfaces
{
    public interface IDestinationRepository
    {
        Task<(List<Destination> Items, int TotalCount)> GetAllAsync(
            int page, int pageSize, CancellationToken ct = default);

        Task<Destination?> GetByIdAsync(Guid id, CancellationToken ct = default);

        Task<List<Destination>> GetByPhoneticCodeAsync(
            string metaphoneCode, CancellationToken ct = default);

        Task<List<Destination>> GetByCountryAsync(
            Guid countryId, int page, int pageSize, CancellationToken ct = default);

        Task<List<Destination>> GetMatchingUserAsync(
            List<string> preferredTags, decimal? maxCostPerDay,
            int limit, CancellationToken ct = default);

        Task<List<Destination>> GetSimilarToWishlistAsync(
            Guid wishlistId, int limit, CancellationToken ct = default);

        Task<List<Destination>> GetDestinationsInWishlistAsync(
            Guid wishlistId,int page, CancellationToken ct = default);

        Task IncrementSearchHitAsync(Guid destinationId, CancellationToken ct = default);

        Task<Destination> CreateAsync(Destination destination, CancellationToken ct = default);
        Task UpdateAsync(Destination destination, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
