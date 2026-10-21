using Hodrac_Backend_MVP2.Events;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.NoSql.Models;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Services;

/// <summary>
/// Implements the bidirectional tag inference loop described in Part 4 of the spec.
///
/// Triggered by: WishlistSavedEvent, DestinationClickedEvent, SearchExecutedEvent,
/// OnboardingCompletedEvent (via BehaviorEventWorker).
/// Reads: tag_inference_rules collection.
/// Writes: interaction_event_stream (raw events), user_psychographic_profiles
/// (tag counts, event-type counters, clamped vectors, derived fields) and
/// wishlist_segmented_popularity (segment counters on save).
///
/// Every handler first ensures the profile exists (minimal upsert), so events for
/// users whose register-time profile creation failed are not lost.
/// It must NOT have a dependency on HodracDbContext — it only touches MongoDB.
/// </summary>
public class TagInferenceService
{
    // Rules are a small, rarely-changing collection: cache them process-wide briefly
    // so each event doesn't re-query, while admin edits still apply within minutes.
    private static readonly TimeSpan RuleCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim RuleCacheLock = new(1, 1);
    private static List<TagInferenceRule>? _ruleCache;
    private static DateTime _ruleCacheLoadedAt;

    private const int MaxSearchPhraseLength = 200;

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
        _rules = db.GetCollection<TagInferenceRule>(TagInferenceRuleSeed.CollectionName);
        _profiles = profiles;
        _popularity = popularity;
        _events = events;
    }

    // ─── WishlistSaved ────────────────────────────────────────────────────────

    public async Task HandleWishlistSavedAsync(WishlistSavedEvent evt)
    {
        if (string.IsNullOrEmpty(evt.UserId)) return;
        var at = evt.OccurredAt.UtcDateTime;
        var tags = ProfileInference.NormalizeTags(evt.WishlistTags);

        // 1. Log raw event (normalised tag keys)
        await _events.InsertAsync(new InteractionEvent
        {
            EventId = $"evt_{Guid.NewGuid():N}",
            UserId = evt.UserId,
            EventType = ProfileInference.EventWishlistSave,
            EntityId = evt.WishlistId,
            EntityTags = tags,
            Timestamp = at,
            Context = new EventContext { Page = "wishlist_detail", Position = 0 }
        });

        // 2. Record every interacted tag (regardless of rules) + event-type counter
        await _profiles.EnsureExistsAsync(evt.UserId, at);
        await _profiles.RecordInteractionAsync(evt.UserId, ProfileInference.EventWishlistSave,
            tags.ToDictionary(t => t, _ => ProfileInference.SaveTagWeight), at);

        // 3. Rule-driven vector updates (clamped 0–100)
        var rules = await GetRulesAsync();
        await _profiles.ApplyVectorDeltasAsync(evt.UserId,
            ProfileInference.TagRuleDeltas(rules, ProfileInference.EventWishlistSave, tags));

        // 4. Derived fields
        var profile = await RecomputeDerivedAsync(evt.UserId);

        // 5. Segment counters — always incremented ("unknown" for missing answers).
        //    WishlistId is Guid.ToString() ("D" format), the same form PopularWishlistService reads.
        var answers = profile?.ExplicitOnboardingAnswers ?? new OnboardingAnswers();
        var budget = string.IsNullOrWhiteSpace(answers.BudgetProfile) ? evt.BudgetProfile : answers.BudgetProfile;
        await _popularity.IncrementSegmentsAsync(evt.WishlistId, new Dictionary<string, string>
        {
            [SegmentFields.TravelerTypes] = ProfileInference.TravelerTypeKey(answers.WhoTheyTravelWith),
            [SegmentFields.BudgetTiers] = ProfileInference.BudgetTierKey(budget),
            [SegmentFields.Priorities] = ProfileInference.PriorityKey(answers.PrimaryPriority),
            [SegmentFields.PersonaClusters] = ProfileInference.PersonaClusterKey(profile?.InferredTravelerPrototypes?.PrimaryType),
        });
    }

    // ─── DestinationClicked ───────────────────────────────────────────────────

    public async Task HandleDestinationClickedAsync(DestinationClickedEvent evt)
    {
        if (string.IsNullOrEmpty(evt.UserId)) return;
        var at = evt.OccurredAt.UtcDateTime;
        var tags = ProfileInference.NormalizeTags(evt.DestinationTags);

        await _events.InsertAsync(new InteractionEvent
        {
            EventId = $"evt_{Guid.NewGuid():N}",
            UserId = evt.UserId,
            EventType = ProfileInference.EventDestinationClick,
            EntityId = evt.DestinationId,
            EntityTags = tags,
            Timestamp = at,
            Context = new EventContext { Page = "explore", Position = 0 }
        });

        await _profiles.EnsureExistsAsync(evt.UserId, at);
        await _profiles.RecordInteractionAsync(evt.UserId, ProfileInference.EventDestinationClick,
            tags.ToDictionary(t => t, _ => ProfileInference.ClickTagWeight), at);

        // Destination clicks get half the weight of a save — weaker signal
        var rules = await GetRulesAsync();
        await _profiles.ApplyVectorDeltasAsync(evt.UserId,
            ProfileInference.TagRuleDeltas(rules, ProfileInference.EventDestinationClick, tags,
                ProfileInference.ClickVectorMultiplier));

        await RecomputeDerivedAsync(evt.UserId);
    }

    // ─── SearchExecuted ───────────────────────────────────────────────────────

    public async Task HandleSearchExecutedAsync(SearchExecutedEvent evt)
    {
        if (string.IsNullOrEmpty(evt.UserId)) return;
        var phrase = (evt.CanonicalPhrase ?? string.Empty).Trim();
        if (phrase.Length == 0) return;
        if (phrase.Length > MaxSearchPhraseLength) phrase = phrase[..MaxSearchPhraseLength];
        var at = evt.OccurredAt.UtcDateTime;

        // Only the canonical phrase is stored (not the raw query).
        await _events.InsertAsync(new InteractionEvent
        {
            EventId = $"evt_{Guid.NewGuid():N}",
            UserId = evt.UserId,
            EventType = ProfileInference.EventSearch,
            EntityId = phrase,
            EntityTags = new List<string>(),
            Timestamp = at,
            Context = new EventContext { Page = "search", Position = 0 }
        });

        await _profiles.EnsureExistsAsync(evt.UserId, at);
        await _profiles.PushSearchPhraseAsync(evt.UserId, phrase, at);
        await RecomputeDerivedAsync(evt.UserId);
    }

    // ─── OnboardingCompleted ──────────────────────────────────────────────────

    public async Task HandleOnboardingCompletedAsync(OnboardingCompletedEvent evt)
    {
        if (string.IsNullOrEmpty(evt.UserId)) return;
        var at = evt.OccurredAt.UtcDateTime;

        await _profiles.EnsureExistsAsync(evt.UserId, at);
        var existing = await _profiles.GetByAuthUserIdAsync(evt.UserId);
        var oldAnswers = existing?.ExplicitOnboardingAnswers ?? new OnboardingAnswers();

        // Empty answers (older clients send no body) never wipe answers already given.
        static string Pick(string? incoming, string current)
            => string.IsNullOrWhiteSpace(incoming) ? current ?? string.Empty : incoming.Trim();

        var newAnswers = new OnboardingAnswers
        {
            WhoTheyTravelWith = Pick(evt.WhoTheyTravelWith, oldAnswers.WhoTheyTravelWith),
            TripTypePreference = Pick(evt.TripTypePreference, oldAnswers.TripTypePreference),
            BudgetProfile = Pick(evt.BudgetProfile, oldAnswers.BudgetProfile),
            PrimaryPriority = Pick(evt.PrimaryPriority, oldAnswers.PrimaryPriority),
        };
        await _profiles.SetOnboardingAnswersAsync(evt.UserId, newAnswers, at);

        // Apply only the difference between the new and previous answers' rule effects,
        // so re-submitting onboarding doesn't double-count.
        var rules = await GetRulesAsync();
        var deltas = ProfileInference.Combine(
            ProfileInference.OnboardingRuleDeltas(rules, newAnswers),
            ProfileInference.OnboardingRuleDeltas(rules, oldAnswers),
            bSign: -1);
        await _profiles.ApplyVectorDeltasAsync(evt.UserId, deltas);

        await RecomputeDerivedAsync(evt.UserId);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Reloads the profile, recomputes derived fields, writes them and returns the updated profile.</summary>
    private async Task<UserPsychographicProfile?> RecomputeDerivedAsync(string userId)
    {
        var profile = await _profiles.GetByAuthUserIdAsync(userId);
        if (profile is null) return null;

        var derived = ProfileInference.ComputeDerived(profile);
        await _profiles.SetDerivedFieldsAsync(userId, derived);

        profile.InteractionAggregations.TopInteractedWishlistTags = derived.TopInteractedTags;
        profile.InteractionAggregations.ConversionProbabilityIndex = derived.ConversionProbabilityIndex;
        profile.InferredTravelerPrototypes = derived.Prototypes;
        profile.InferredDemographicMatrix = derived.Demographics;
        return profile;
    }

    private async Task<List<TagInferenceRule>> GetRulesAsync()
    {
        var cached = _ruleCache;
        if (cached is not null && DateTime.UtcNow - _ruleCacheLoadedAt < RuleCacheTtl) return cached;

        await RuleCacheLock.WaitAsync();
        try
        {
            if (_ruleCache is not null && DateTime.UtcNow - _ruleCacheLoadedAt < RuleCacheTtl) return _ruleCache;
            var rules = await _rules.Find(FilterDefinition<TagInferenceRule>.Empty).ToListAsync();
            _ruleCache = rules;
            _ruleCacheLoadedAt = DateTime.UtcNow;
            return rules;
        }
        finally
        {
            RuleCacheLock.Release();
        }
    }
}
