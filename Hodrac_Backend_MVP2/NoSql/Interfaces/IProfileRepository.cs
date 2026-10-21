using Hodrac_Backend_MVP2.NoSql.Models;
using Hodrac_Backend_MVP2.Services;

namespace Hodrac_Backend_MVP2.NoSql.Interfaces
{
    public interface IProfileRepository
    {
        Task<UserPsychographicProfile?> GetByAuthUserIdAsync(string authUserId);
        Task UpsertAsync(UserPsychographicProfile profile);

        /// <summary>
        /// Creates a minimal profile if none exists for the user (idempotent; never
        /// touches an existing document). Safe against concurrent creation.
        /// </summary>
        Task EnsureExistsAsync(string authUserId, DateTime at);

        /// <summary>
        /// $inc tag_counts.{key} by each weight, $inc the event-type counter and set
        /// last_interaction_delta_at. Keys must already be normalised (ProfileInference.NormalizeKey).
        /// </summary>
        Task RecordInteractionAsync(string authUserId, string eventType,
            IReadOnlyDictionary<string, int> tagIncrements, DateTime at);

        /// <summary>Adds each delta to its personality vector, clamping the result to 0–100 (atomic).</summary>
        Task ApplyVectorDeltasAsync(string authUserId, IReadOnlyDictionary<string, double> deltas);

        /// <summary>Appends a phrase to historical_search_phrases (keeps the last 20) and counts a search.</summary>
        Task PushSearchPhraseAsync(string authUserId, string phrase, DateTime at);

        Task SetOnboardingAnswersAsync(string authUserId, OnboardingAnswers answers, DateTime at);

        /// <summary>Writes the derived fields (top tags, prototypes, demographics, conversion index).</summary>
        Task SetDerivedFieldsAsync(string authUserId, DerivedProfileFields derived);
    }
}
