using Hodrac_Backend_MVP2.NoSql.Models;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Services;

// ─── Starter tag_inference_rules ──────────────────────────────────────────────
// Seeded idempotently by MongoIndexInitializer after the indexes are ensured:
// insert-if-missing by RuleId ($setOnInsert upsert), so an existing rule document
// (including one an admin has edited) is never overwritten.
//
// All source_tag / source_value values are normalised keys (ProfileInference.NormalizeKey).
// Tag keys come from the seeders' destination Tag.Key values and the wishlist vibe
// tags ("Sports Fan" → sports_fan, "Teen-Approved" → teen_approved), plus legacy
// display-name aliases ("Adventure", "Food", "Luxury" …) that normalise differently.
// The same weights are seeded for wishlist_save and destination_click; the live
// handler applies ProfileInference.ClickVectorMultiplier (0.5) to clicks.
// Negative weights push a vector down (e.g. "flexible" → more spontaneous).

public static class TagInferenceRuleSeed
{
    public const string CollectionName = "tag_inference_rules";

    private const int DecayDays = 90;   // matches the interaction_event_stream TTL

    // canonical key, aliases, (vector, weight)…
    private static readonly (string Key, string[] Aliases, (string Vector, double Weight)[] Effects)[] TagEffects =
    [
        ("adventurous", ["adventure"], [("adventure_seeker", 5), ("openness_to_experience", 3)]),
        ("nature", ["nature_outdoor", "outdoors"], [("adventure_seeker", 2), ("openness_to_experience", 2)]),
        ("hidden_gem", [], [("openness_to_experience", 4), ("adventure_seeker", 2), ("cultural_immersion_index", 1)]),
        ("cultural", ["culture", "cultural_site"], [("cultural_immersion_index", 5), ("openness_to_experience", 2)]),
        ("history", ["historic", "landmark_monument", "landmarks"], [("cultural_immersion_index", 4), ("conscientiousness", 1)]),
        ("photography", ["viewpoint_scenic_spot"], [("teen_validation_priority", 3), ("openness_to_experience", 1)]),
        ("aesthetic", ["retro_vibe"], [("teen_validation_priority", 4), ("openness_to_experience", 1)]),
        ("food_focused", ["foodie", "food", "food_experience"], [("cultural_immersion_index", 3), ("openness_to_experience", 2), ("social_butterfly", 1)]),
        ("market_street_life", ["street_culture"], [("cultural_immersion_index", 3), ("social_butterfly", 1)]),
        ("nightlife", ["entertainment_nightlife"], [("social_butterfly", 4), ("extraversion", 4)]),
        ("social", ["group_friendly"], [("social_butterfly", 5), ("extraversion", 3)]),
        ("sports_fan", [], [("social_butterfly", 4), ("extraversion", 4)]),
        ("music", [], [("social_butterfly", 2), ("extraversion", 2), ("cultural_immersion_index", 2)]),
        ("urban", [], [("social_butterfly", 1), ("extraversion", 1), ("openness_to_experience", 1)]),
        ("relaxing", ["relaxation"], [("planner_vs_spontaneous", 1), ("extraversion", -1)]),
        ("beach", [], [("openness_to_experience", 1), ("social_butterfly", 1), ("extraversion", 1)]),
        ("romantic", ["romance", "couple_friendly"], [("planner_vs_spontaneous", 1), ("social_butterfly", 1)]),
        ("family_friendly", ["family"], [("conscientiousness", 3), ("planner_vs_spontaneous", 3), ("teen_validation_priority", 1)]),
        ("teen_approved", [], [("teen_validation_priority", 5), ("social_butterfly", 1)]),
        ("premium", ["luxury"], [("conscientiousness", 2), ("planner_vs_spontaneous", 2)]),
        ("budget_friendly", ["budget"], [("planner_vs_spontaneous", 2), ("adventure_seeker", 1)]),
        ("art", [], [("cultural_immersion_index", 3), ("openness_to_experience", 3)]),
        ("architecture", [], [("cultural_immersion_index", 3), ("openness_to_experience", 1)]),
        ("educational", [], [("cultural_immersion_index", 3), ("conscientiousness", 2)]),
        ("walkable", [], [("openness_to_experience", 1), ("cultural_immersion_index", 1)]),
        ("local_favorite", [], [("cultural_immersion_index", 3), ("openness_to_experience", 2)]),
        ("quirky", [], [("openness_to_experience", 4), ("adventure_seeker", 1)]),
        ("tourist_hotspot", ["crowded"], [("teen_validation_priority", 2), ("planner_vs_spontaneous", 1), ("social_butterfly", 1)]),
        ("sightseeing", [], [("cultural_immersion_index", 2), ("planner_vs_spontaneous", 1)]),
        ("once_in_a_lifetime", [], [("adventure_seeker", 3), ("openness_to_experience", 2)]),
        ("road_trip", [], [("adventure_seeker", 3), ("openness_to_experience", 1)]),
        ("flexible", ["spontaneous"], [("planner_vs_spontaneous", -3), ("openness_to_experience", 2)]),
        ("low_traffic", [], [("planner_vs_spontaneous", 1), ("extraversion", -1)]),
        ("activity_experience", [], [("adventure_seeker", 3), ("extraversion", 1)]),
        ("solo_friendly", [], [("adventure_seeker", 2), ("openness_to_experience", 2)]),
        ("shopping", [], [("social_butterfly", 1), ("teen_validation_priority", 1)]),
        ("good_short_visit", [], [("planner_vs_spontaneous", 1)]),
    ];

    // user_profile_field rules: (field, value key, vector, weight, destination tag boost)
    // Values match when the normalised answer equals or contains the value as a token
    // sequence (see ProfileInference.OnboardingRuleDeltas), so "family_with_teens"
    // fires both "family" and "teens". Weights are larger than per-event rules
    // because onboarding is applied once.
    private static readonly (string Field, string Value, string Vector, double Weight, string? BoostTag)[] FieldEffects =
    [
        ("who_they_travel_with", "solo", "adventure_seeker", 8, "solo_friendly"),
        ("who_they_travel_with", "solo", "openness_to_experience", 6, null),
        ("who_they_travel_with", "friends", "social_butterfly", 12, "social"),
        ("who_they_travel_with", "friends", "extraversion", 10, null),
        ("who_they_travel_with", "group", "social_butterfly", 10, "group_friendly"),
        ("who_they_travel_with", "partner", "planner_vs_spontaneous", 4, "romantic"),
        ("who_they_travel_with", "couple", "planner_vs_spontaneous", 4, "romantic"),
        ("who_they_travel_with", "family", "conscientiousness", 10, "family_friendly"),
        ("who_they_travel_with", "family", "planner_vs_spontaneous", 10, null),
        ("who_they_travel_with", "kids", "conscientiousness", 6, "family_friendly"),
        ("who_they_travel_with", "teens", "teen_validation_priority", 15, "teen_approved"),
        ("who_they_travel_with", "teen", "teen_validation_priority", 15, "teen_approved"),

        ("trip_type_preference", "adventure", "adventure_seeker", 15, "adventurous"),
        ("trip_type_preference", "adventure", "openness_to_experience", 5, null),
        ("trip_type_preference", "culture", "cultural_immersion_index", 15, "cultural"),
        ("trip_type_preference", "cultural", "cultural_immersion_index", 15, "cultural"),
        ("trip_type_preference", "history", "cultural_immersion_index", 10, "history"),
        ("trip_type_preference", "food", "cultural_immersion_index", 6, "food_focused"),
        ("trip_type_preference", "foodie", "cultural_immersion_index", 6, "food_focused"),
        ("trip_type_preference", "relaxation", "planner_vs_spontaneous", 4, "relaxing"),
        ("trip_type_preference", "relaxing", "planner_vs_spontaneous", 4, "relaxing"),
        ("trip_type_preference", "beach", "openness_to_experience", 3, "beach"),
        ("trip_type_preference", "city", "social_butterfly", 6, "walkable"),
        ("trip_type_preference", "nightlife", "extraversion", 10, "nightlife"),
        ("trip_type_preference", "nature", "adventure_seeker", 8, "nature"),

        ("budget_profile", "budget", "planner_vs_spontaneous", 5, "budget_friendly"),
        ("budget_profile", "luxury", "conscientiousness", 5, "premium"),
        ("budget_profile", "premium", "conscientiousness", 3, "premium"),

        ("primary_priority", "adventure", "adventure_seeker", 10, "adventurous"),
        ("primary_priority", "culture", "cultural_immersion_index", 10, "cultural"),
        ("primary_priority", "instagram_worthy", "teen_validation_priority", 15, "photography"),
        ("primary_priority", "saving_money", "planner_vs_spontaneous", 10, "budget_friendly"),
        ("primary_priority", "saving_money", "conscientiousness", 5, null),
        ("primary_priority", "stress_free", "planner_vs_spontaneous", 10, "family_friendly"),
        ("primary_priority", "food", "cultural_immersion_index", 6, "food_focused"),
        ("primary_priority", "relaxation", "planner_vs_spontaneous", 4, "relaxing"),
    ];

    public static List<TagInferenceRule> StarterRules()
    {
        var rules = new List<TagInferenceRule>();
        foreach (var sourceType in new[] { "wishlist_save", "destination_click" })
        {
            foreach (var (key, aliases, effects) in TagEffects)
            {
                foreach (var tag in aliases.Prepend(key))
                {
                    foreach (var (vector, weight) in effects)
                    {
                        rules.Add(new TagInferenceRule
                        {
                            RuleId = $"seed_{sourceType}_{tag}_{vector}",
                            SourceType = sourceType,
                            SourceTag = tag,
                            TargetUserVector = vector,
                            IncrementWeight = weight,
                            DecayDays = DecayDays,
                        });
                    }
                }
            }
        }

        foreach (var (field, value, vector, weight, boostTag) in FieldEffects)
        {
            rules.Add(new TagInferenceRule
            {
                RuleId = $"seed_user_profile_field_{field}_{value}_{vector}",
                SourceType = "user_profile_field",
                SourceField = field,
                SourceValue = value,
                TargetUserVector = vector,
                IncrementWeight = weight,
                DecayDays = 0,
                TargetDestinationTag = boostTag,
                BoostMultiplier = boostTag is null ? 0 : 1.5,
            });
        }

        return rules;
    }

    /// <summary>Inserts missing starter rules; never modifies existing ones. Returns the number inserted.</summary>
    public static async Task<long> SeedAsync(IMongoDatabase db, CancellationToken ct = default)
    {
        var col = db.GetCollection<TagInferenceRule>(CollectionName);
        var u = Builders<TagInferenceRule>.Update;
        var models = StarterRules().Select(r => new UpdateOneModel<TagInferenceRule>(
            Builders<TagInferenceRule>.Filter.Eq(x => x.RuleId, r.RuleId),
            u.Combine(
                u.SetOnInsert(x => x.SourceType, r.SourceType),
                u.SetOnInsert(x => x.SourceTag, r.SourceTag),
                u.SetOnInsert(x => x.TargetUserVector, r.TargetUserVector),
                u.SetOnInsert(x => x.IncrementWeight, r.IncrementWeight),
                u.SetOnInsert(x => x.DecayDays, r.DecayDays),
                u.SetOnInsert(x => x.SourceField, r.SourceField),
                u.SetOnInsert(x => x.SourceValue, r.SourceValue),
                u.SetOnInsert(x => x.TargetDestinationTag, r.TargetDestinationTag),
                u.SetOnInsert(x => x.BoostMultiplier, r.BoostMultiplier)))
        { IsUpsert = true }).ToList();

        var result = await col.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false }, ct);
        return result.Upserts.Count;
    }
}
