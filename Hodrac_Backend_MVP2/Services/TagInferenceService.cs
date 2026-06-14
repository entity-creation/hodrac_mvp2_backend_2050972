using Hodrac_Backend_MVP2.Events;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.NoSql.Models;
using Hodrac_Backend_MVP2.NoSql.Repositories;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Services;

/// <summary>
/// Implements the bidirectional tag inference loop described in Part 4 of the spec.
///
/// Triggered by: WishlistSavedEvent, DestinationClickedEvent, OnboardingCompletedEvent.
/// Reads: tag_inference_rules collection.
/// Writes: user_psychographic_profiles (vector scores + tag arrays).
///
/// This service runs in the Behavioral Engine (consumer side of the event bus).
/// It must NOT have a dependency on HodracDbContext — it only touches MongoDB.
/// </summary>
public class TagInferenceService
{
    private readonly IMongoCollection<TagInferenceRule> _rules;
    private readonly IProfileRepository _profiles;
    private readonly ISegmentedPopularityRepository _popularity;
    private readonly IInteractionEventRepository _events;

    public TagInferenceService(
        IMongoDatabase db,
        IProfileRepository profiles,
        ISegmentedPopularityRepository popularity,
        IInteractionEventRepository events)
    {
        _rules = db.GetCollection<TagInferenceRule>("tag_inference_rules");
        _profiles = profiles;
        _popularity = popularity;
        _events = events;
    }

    // ─── WishlistSaved ────────────────────────────────────────────────────────

    public async Task HandleWishlistSavedAsync(WishlistSavedEvent evt)
    {
        // 1. Log raw event
        await _events.InsertAsync(new InteractionEvent
        {
            EventId = $"evt_{Guid.NewGuid():N}",
            UserId = evt.UserId,
            EventType = "wishlist_save",
            EntityId = evt.WishlistId,
            EntityTags = evt.WishlistTags,
            Timestamp = evt.OccurredAt.UtcDateTime,
            Context = new EventContext { Page = "wishlist_detail", Position = 0 }
        });

        // 2. Find all matching inference rules for each tag on the saved wishlist
        foreach (var tag in evt.WishlistTags)
        {
            var matchingRules = await GetRulesForTagAsync("wishlist_save", tag);

            foreach (var rule in matchingRules)
            {
                if (!string.IsNullOrEmpty(rule.TargetUserVector) && rule.IncrementWeight > 0)
                {
                    // 3a. Update user's personality vector
                    await _profiles.UpdateVectorScoresAsync(
                        evt.UserId,
                        rule.TargetUserVector,
                        rule.IncrementWeight
                    );
                }

                // 3b. Track tag in interaction history
                await _profiles.AppendTagAsync(evt.UserId, tag);
            }
        }

        // 4. Update segmented popularity counters for this wishlist
        var profile = await _profiles.GetByAuthUserIdAsync(evt.UserId);
        if (profile is not null)
        {
            // Explicit traveler type counter
            await _popularity.IncrementSegmentAsync(
                evt.WishlistId,
                "segment_popularity_scores.explicit_traveler_types",
                evt.PeopleType
            );

            // Budget tier counter
            await _popularity.IncrementSegmentAsync(
                evt.WishlistId,
                "segment_popularity_scores.budget_tiers",
                MapBudgetToTierKey(evt.BudgetProfile)
            );

            // Primary priority counter
            await _popularity.IncrementSegmentAsync(
                evt.WishlistId,
                "segment_popularity_scores.primary_priorities",
                MapPriorityToKey(profile.ExplicitOnboardingAnswers.PrimaryPriority)
            );
        }
    }

    // ─── DestinationClicked ───────────────────────────────────────────────────

    public async Task HandleDestinationClickedAsync(DestinationClickedEvent evt)
    {
        await _events.InsertAsync(new InteractionEvent
        {
            EventId = $"evt_{Guid.NewGuid():N}",
            UserId = evt.UserId,
            EventType = "destination_click",
            EntityId = evt.DestinationId,
            EntityTags = evt.DestinationTags,
            Timestamp = evt.OccurredAt.UtcDateTime,
            Context = new EventContext { Page = "explore", Position = 0 }
        });

        foreach (var tag in evt.DestinationTags)
        {
            var matchingRules = await GetRulesForTagAsync("destination_click", tag);
            foreach (var rule in matchingRules)
            {
                if (!string.IsNullOrEmpty(rule.TargetUserVector) && rule.IncrementWeight > 0)
                {
                    // Destination clicks get half the weight of a save — weaker signal
                    await _profiles.UpdateVectorScoresAsync(
                        evt.UserId,
                        rule.TargetUserVector,
                        rule.IncrementWeight * 0.5
                    );
                }
            }
        }
    }

    // ─── OnboardingCompleted ──────────────────────────────────────────────────

    public async Task HandleOnboardingCompletedAsync(OnboardingCompletedEvent evt)
    {
        // Find rules triggered by explicit profile field values
        var fieldRules = await GetRulesForFieldAsync(
            "user_profile_field",
            "primary_priority",
            evt.PrimaryPriority
        );

        // These rules specify destination tag boosts, not vector updates.
        // Store them in the profile so the recommendation engine can apply boost_multiplier
        // at query time without re-reading the rules collection.
        var profile = await _profiles.GetByAuthUserIdAsync(evt.UserId);

        if (profile is null)
        {
            // First-time onboarding — create the profile document
            profile = new UserPsychographicProfile
            {
                Id = $"prof_user_{Guid.NewGuid():N}",
                AuthUserReferenceId = evt.UserId,
                ExplicitOnboardingAnswers = new OnboardingAnswers
                {
                    WhoTheyTravelWith = evt.WhoTheyTravelWith,
                    TripTypePreference = evt.TripTypePreference,
                    BudgetProfile = evt.BudgetProfile,
                    PrimaryPriority = evt.PrimaryPriority,
                },
                SystemTelemetry = new SystemTelemetry
                {
                    FirstSeenAt = evt.OccurredAt.UtcDateTime,
                    LastInteractionDeltaAt = evt.OccurredAt.UtcDateTime,
                }
            };
            await _profiles.UpsertAsync(profile);
        }
        else
        {
            profile.ExplicitOnboardingAnswers.WhoTheyTravelWith = evt.WhoTheyTravelWith;
            profile.ExplicitOnboardingAnswers.TripTypePreference = evt.TripTypePreference;
            profile.ExplicitOnboardingAnswers.BudgetProfile = evt.BudgetProfile;
            profile.ExplicitOnboardingAnswers.PrimaryPriority = evt.PrimaryPriority;
            await _profiles.UpsertAsync(profile);
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<List<TagInferenceRule>> GetRulesForTagAsync(string sourceType, string sourceTag)
    {
        var filter = Builders<TagInferenceRule>.Filter.And(
            Builders<TagInferenceRule>.Filter.Eq(r => r.SourceType, sourceType),
            Builders<TagInferenceRule>.Filter.Eq(r => r.SourceTag, sourceTag)
        );
        return await _rules.Find(filter).ToListAsync();
    }

    private async Task<List<TagInferenceRule>> GetRulesForFieldAsync(
        string sourceType, string sourceField, string sourceValue)
    {
        var filter = Builders<TagInferenceRule>.Filter.And(
            Builders<TagInferenceRule>.Filter.Eq(r => r.SourceType, sourceType),
            Builders<TagInferenceRule>.Filter.Eq(r => r.SourceField, sourceField),
            Builders<TagInferenceRule>.Filter.Eq(r => r.SourceValue, sourceValue)
        );
        return await _rules.Find(filter).ToListAsync();
    }

    private static string MapBudgetToTierKey(string budgetProfile) => budgetProfile switch
    {
        "Budget" => "budget_under_100_per_day",
        "Mid-range" => "mid_range_100_250",
        "Premium Comfortable" => "premium_250_500",
        "Luxury" => "luxury_500_plus",
        _ => "mid_range_100_250"
    };

    private static string MapPriorityToKey(string priority) => priority switch
    {
        "Saving money" => "stress_free_logistics",
        "Instagram-Worthy" => "instagram_worthy",
        "Culture" => "cultural_immersion",
        "Adventure" => "adventure_activities",
        _ => priority.ToLowerInvariant().Replace(" ", "_")
    };
}
