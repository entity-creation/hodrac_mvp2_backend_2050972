
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.Interfaces
{
    public interface ICollaboratorRepository
    {
        Task<List<WishlistCollaborator>> GetCollaboratorsAsync(
            Guid wishlistId, CancellationToken ct = default);

        Task<WishlistCollaborator?> GetCollaboratorAsync(
            Guid wishlistId, Guid userId, CancellationToken ct = default);

        /// <summary>Returns the role of userId on wishlistId, or null if no record exists.</summary>
        Task<string?> GetRoleAsync(Guid wishlistId, Guid userId, CancellationToken ct = default);

        Task<WishlistCollaborator> AddAsync(
            Guid wishlistId, Guid userId, string email, string role, CancellationToken ct = default);

        Task<bool> ChangeRoleAsync(
            Guid wishlistId, Guid userId, string newRole, CancellationToken ct = default);

        Task<bool> RemoveAsync(
            Guid wishlistId, Guid userId, CancellationToken ct = default);

        /// <summary>
        /// Returns all wishlists where userId is a collaborator (Editor or Viewer — not Owner).
        /// Used by GET /api/shared-wishlists.
        /// </summary>
        Task<List<Wishlist>> GetSharedWishlistsAsync(
            Guid userId, CancellationToken ct = default);
    }
}
