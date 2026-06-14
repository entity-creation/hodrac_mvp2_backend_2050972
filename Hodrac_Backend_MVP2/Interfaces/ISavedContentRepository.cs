using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Interfaces
{
    public interface ISavedContentRepository
    {
        Task<List<Wishlist>> GetSavedWishlistsAsync(Guid userId, CancellationToken ct = default);
        Task<List<Destination>> GetSavedDestinationsAsync(Guid userId, CancellationToken ct = default);

        Task<bool> SaveDestinationAsync(Guid destinationId, Guid userId, CancellationToken ct = default);
        Task<bool> UnsaveDestinationAsync(Guid destinationId, Guid userId, CancellationToken ct = default);
        Task<bool> IsDestinationSavedAsync(Guid destinationId, Guid userId, CancellationToken ct = default);
    }
}
