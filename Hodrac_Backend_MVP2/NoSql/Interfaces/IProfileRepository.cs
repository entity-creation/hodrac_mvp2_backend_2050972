using Hodrac_Backend_MVP2.NoSql.Models;

namespace Hodrac_Backend_MVP2.NoSql.Interfaces
{
    public interface IProfileRepository
    {
        Task<UserPsychographicProfile?> GetByAuthUserIdAsync(string authUserId);
        Task UpsertAsync(UserPsychographicProfile profile);
        Task UpdateVectorScoresAsync(string authUserId, string vectorKey, double increment);
        Task AppendTagAsync(string authUserId, string tag);
    }
}
