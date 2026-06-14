using Hodrac_Backend_MVP2.NoSql.Models;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Infrastucture.Jobs
{
    /// <summary>
    /// Runs nightly. Multiplies all personality vector scores by 0.98 for profiles
    /// whose last interaction was > 30 days ago. Prevents stale signals from
    /// dominating recommendations indefinitely.
    /// </summary>
    public class PersonalityScoreDecayJob
    {
        private readonly IMongoCollection<UserPsychographicProfile> _profiles;

        public PersonalityScoreDecayJob(IMongoDatabase db)
            => _profiles = db.GetCollection<UserPsychographicProfile>("user_psychographic_profiles");

        public async Task RunAsync(CancellationToken ct = default)
        {
            var decayThreshold = DateTime.UtcNow.AddDays(-30);
            var filter = Builders<UserPsychographicProfile>.Filter
                .Lt("system_telemetry.last_interaction_delta_at", decayThreshold);

            // MongoDB doesn't support multiply-all-fields in one update expression natively.
            // Use a pipeline update to multiply each score field individually.
            var pipeline = new EmptyPipelineDefinition<UserPsychographicProfile>()
                .AppendStage<UserPsychographicProfile, UserPsychographicProfile, UserPsychographicProfile>(
                    @"{
                    $set: {
                        'personality_vector_scores.adventure_seeker':         { $multiply: ['$personality_vector_scores.adventure_seeker', 0.98] },
                        'personality_vector_scores.social_butterfly':         { $multiply: ['$personality_vector_scores.social_butterfly', 0.98] },
                        'personality_vector_scores.planner_vs_spontaneous':   { $multiply: ['$personality_vector_scores.planner_vs_spontaneous', 0.98] },
                        'personality_vector_scores.cultural_immersion_index': { $multiply: ['$personality_vector_scores.cultural_immersion_index', 0.98] },
                        'personality_vector_scores.teen_validation_priority': { $multiply: ['$personality_vector_scores.teen_validation_priority', 0.98] },
                        'personality_vector_scores.openness_to_experience':   { $multiply: ['$personality_vector_scores.openness_to_experience', 0.98] },
                        'personality_vector_scores.conscientiousness':        { $multiply: ['$personality_vector_scores.conscientiousness', 0.98] },
                        'personality_vector_scores.extraversion':             { $multiply: ['$personality_vector_scores.extraversion', 0.98] }
                    }
                }"
                );

            await _profiles.UpdateManyAsync(filter, pipeline, cancellationToken: ct);
        }
    }
}
