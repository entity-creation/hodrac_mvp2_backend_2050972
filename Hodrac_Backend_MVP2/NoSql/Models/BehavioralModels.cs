using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.NoSql.Models;

// ─── user_psychographic_profiles ─────────────────────────────────────────────

[BsonIgnoreExtraElements]
public class UserPsychographicProfile
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;   // e.g. "prof_user_99122"

    [BsonElement("auth_user_reference_id")]
    public string AuthUserReferenceId { get; set; } = string.Empty;

    [BsonElement("anonymized_marketplace_id")]
    public string AnonymizedMarketplaceId { get; set; } = string.Empty;

    [BsonElement("explicit_onboarding_answers")]
    public OnboardingAnswers ExplicitOnboardingAnswers { get; set; } = new();

    [BsonElement("inferred_demographic_matrix")]
    public DemographicMatrix InferredDemographicMatrix { get; set; } = new();

    [BsonElement("personality_vector_scores")]
    public PersonalityVectorScores PersonalityVectorScores { get; set; } = new();

    [BsonElement("inferred_traveler_prototypes")]
    public TravelerPrototypes InferredTravelerPrototypes { get; set; } = new();

    [BsonElement("interaction_aggregations")]
    public InteractionAggregations InteractionAggregations { get; set; } = new();

    [BsonElement("system_telemetry")]
    public SystemTelemetry SystemTelemetry { get; set; } = new();
}

public class OnboardingAnswers
{
    [BsonElement("who_they_travel_with")]
    public string WhoTheyTravelWith { get; set; } = string.Empty;

    [BsonElement("trip_type_preference")]
    public string TripTypePreference { get; set; } = string.Empty;

    [BsonElement("budget_profile")]
    public string BudgetProfile { get; set; } = string.Empty;

    [BsonElement("primary_priority")]
    public string PrimaryPriority { get; set; } = string.Empty;
}

public class DemographicMatrix
{
    [BsonElement("calculated_income_bracket")]
    public string CalculatedIncomeBracket { get; set; } = string.Empty;

    [BsonElement("confidence_score")]
    public double ConfidenceScore { get; set; }

    [BsonElement("inferred_age_bracket")]
    public string InferredAgeBracket { get; set; } = string.Empty;
}

public class PersonalityVectorScores
{
    [BsonElement("adventure_seeker")]
    public double AdventureSeeker { get; set; }

    [BsonElement("social_butterfly")]
    public double SocialButterfly { get; set; }

    [BsonElement("planner_vs_spontaneous")]
    public double PlannerVsSpontaneous { get; set; }

    [BsonElement("cultural_immersion_index")]
    public double CulturalImmersionIndex { get; set; }

    [BsonElement("teen_validation_priority")]
    public double TeenValidationPriority { get; set; }

    [BsonElement("openness_to_experience")]
    public double OpennessToExperience { get; set; }

    [BsonElement("conscientiousness")]
    public double Conscientiousness { get; set; }

    [BsonElement("extraversion")]
    public double Extraversion { get; set; }
}

public class TravelerPrototypes
{
    [BsonElement("primary_type")]
    public string PrimaryType { get; set; } = string.Empty;

    [BsonElement("secondary_types")]
    public List<string> SecondaryTypes { get; set; } = new();

    [BsonElement("confidence_score")]
    public double ConfidenceScore { get; set; }
}

public class InteractionAggregations
{
    [BsonElement("top_interacted_wishlist_tags")]
    public List<string> TopInteractedWishlistTags { get; set; } = new();

    [BsonElement("interaction_click_count_by_persona_layer")]
    public Dictionary<string, int> InteractionClickCountByPersonaLayer { get; set; } = new();

    [BsonElement("historical_search_phrases")]
    public List<string> HistoricalSearchPhrases { get; set; } = new();

    [BsonElement("conversion_probability_index")]
    public double ConversionProbabilityIndex { get; set; }
}

public class SystemTelemetry
{
    [BsonElement("first_seen_at")]
    public DateTime FirstSeenAt { get; set; }

    [BsonElement("last_interaction_delta_at")]
    public DateTime LastInteractionDeltaAt { get; set; }
}

// ─── tag_inference_rules ──────────────────────────────────────────────────────

[BsonIgnoreExtraElements]
public class TagInferenceRule
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string RuleId { get; set; } = string.Empty;    // e.g. "inf_001"

    /// <summary>"wishlist_save" | "user_profile_field" | "destination_click"</summary>
    [BsonElement("source_type")]
    public string SourceType { get; set; } = string.Empty;

    // ── Source: wishlist_save / destination_click ─────────────────────────────
    [BsonElement("source_tag")]
    public string? SourceTag { get; set; }

    /// <summary>Which personality vector dimension to update.</summary>
    [BsonElement("target_user_vector")]
    public string? TargetUserVector { get; set; }

    /// <summary>How much to add to the target vector dimension on trigger.</summary>
    [BsonElement("increment_weight")]
    public double IncrementWeight { get; set; }

    /// <summary>Days after which this inference's contribution decays to zero.</summary>
    [BsonElement("decay_days")]
    public int DecayDays { get; set; }

    // ── Source: user_profile_field ────────────────────────────────────────────
    [BsonElement("source_field")]
    public string? SourceField { get; set; }

    [BsonElement("source_value")]
    public string? SourceValue { get; set; }

    /// <summary>Destination tag to boost in recommendations when this rule fires.</summary>
    [BsonElement("target_destination_tag")]
    public string? TargetDestinationTag { get; set; }

    [BsonElement("boost_multiplier")]
    public double BoostMultiplier { get; set; }
}

// ─── wishlist_segmented_popularity ───────────────────────────────────────────

[BsonIgnoreExtraElements]
public class WishlistSegmentedPopularity
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string WishlistId { get; set; } = string.Empty;    // e.g. "wl_kyoto_2024"

    [BsonElement("segment_popularity_scores")]
    public SegmentPopularityScores SegmentPopularityScores { get; set; } = new();

    [BsonElement("last_computed_at")]
    public DateTime LastComputedAt { get; set; }
}

public class SegmentPopularityScores
{
    /// <summary>Keys are persona cluster strings, values are save counts.</summary>
    [BsonElement("persona_vector_clusters")]
    public Dictionary<string, int> PersonaVectorClusters { get; set; } = new();

    [BsonElement("explicit_traveler_types")]
    public Dictionary<string, int> ExplicitTravelerTypes { get; set; } = new();

    [BsonElement("budget_tiers")]
    public Dictionary<string, int> BudgetTiers { get; set; } = new();

    [BsonElement("primary_priorities")]
    public Dictionary<string, int> PrimaryPriorities { get; set; } = new();
}

// ─── interaction_event_stream ─────────────────────────────────────────────────

[BsonIgnoreExtraElements]
public class InteractionEvent
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string EventId { get; set; } = string.Empty;   // e.g. "evt_89231"

    [BsonElement("user_id")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>"wishlist_save" | "wishlist_view" | "destination_click" | "search"</summary>
    [BsonElement("event_type")]
    public string EventType { get; set; } = string.Empty;

    [BsonElement("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [BsonElement("entity_tags")]
    public List<string> EntityTags { get; set; } = new();

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; }

    [BsonElement("context")]
    public EventContext Context { get; set; } = new();
}

public class EventContext
{
    [BsonElement("page")]
    public string Page { get; set; } = string.Empty;

    [BsonElement("position")]
    public int Position { get; set; }
}

// ─── Index definitions helper ─────────────────────────────────────────────────
// Called once at startup via MongoIndexInitializer.

public static class MongoIndexDefinitions
{
    public static async Task EnsureIndexesAsync(IMongoDatabase db)
    {
        // user_psychographic_profiles
        var profiles = db.GetCollection<UserPsychographicProfile>("user_psychographic_profiles");
        await profiles.Indexes.CreateOneAsync(
            new CreateIndexModel<UserPsychographicProfile>(
                Builders<UserPsychographicProfile>.IndexKeys.Ascending(p => p.AuthUserReferenceId),
                new CreateIndexOptions { Unique = true, Name = "idx_auth_user_ref" }
            )
        );

        // tag_inference_rules — compound: source_type + source_tag (query pattern: "all rules for this tag")
        var rules = db.GetCollection<TagInferenceRule>("tag_inference_rules");
        await rules.Indexes.CreateOneAsync(
            new CreateIndexModel<TagInferenceRule>(
                Builders<TagInferenceRule>.IndexKeys
                    .Ascending(r => r.SourceType)
                    .Ascending(r => r.SourceTag),
                new CreateIndexOptions { Name = "idx_source_type_tag" }
            )
        );

        // wishlist_segmented_popularity — primary key is WishlistId (already _id), no extra index needed.
        // last_computed_at index supports the nightly recompute job's staleness check.
        var popularity = db.GetCollection<WishlistSegmentedPopularity>("wishlist_segmented_popularity");
        await popularity.Indexes.CreateOneAsync(
            new CreateIndexModel<WishlistSegmentedPopularity>(
                Builders<WishlistSegmentedPopularity>.IndexKeys.Ascending(p => p.LastComputedAt),
                new CreateIndexOptions { Name = "idx_last_computed" }
            )
        );

        // interaction_event_stream — compound: (event_type, timestamp) for pipeline queries
        var events = db.GetCollection<InteractionEvent>("interaction_event_stream");
        await events.Indexes.CreateOneAsync(
            new CreateIndexModel<InteractionEvent>(
                Builders<InteractionEvent>.IndexKeys
                    .Ascending(e => e.EventType)
                    .Descending(e => e.Timestamp),
                new CreateIndexOptions { Name = "idx_event_type_ts" }
            )
        );
        // user_id index for per-user event history lookups
        await events.Indexes.CreateOneAsync(
            new CreateIndexModel<InteractionEvent>(
                Builders<InteractionEvent>.IndexKeys.Ascending(e => e.UserId),
                new CreateIndexOptions { Name = "idx_user_id" }
            )
        );
        // TTL index — auto-delete raw events after 90 days
        await events.Indexes.CreateOneAsync(
            new CreateIndexModel<InteractionEvent>(
                Builders<InteractionEvent>.IndexKeys.Ascending(e => e.Timestamp),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(90), Name = "ttl_90d" }
            )
        );
    }
}
