using Hodrac_Backend_MVP2.NoSql.Models;

namespace Hodrac_Backend_MVP2.NoSql.Interfaces
{
    public interface IInteractionEventRepository
    {
        Task InsertAsync(InteractionEvent evt);
        Task<List<InteractionEvent>> GetRecentByUserAsync(string userId, int limit = 50);
    }
}
