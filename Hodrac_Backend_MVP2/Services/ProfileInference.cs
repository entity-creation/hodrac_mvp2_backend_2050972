using System.Text;
using Hodrac_Backend_MVP2.NoSql.Models;

namespace Hodrac_Backend_MVP2.Services;

// ─── Profile inference (pure helpers) ─────────────────────────────────────────
// Everything here is deterministic and side-effect free so it can be unit tested
// and shared by the live event handler (TagInferenceService), the admin rebuild
// job (InsightsRebuildJob) and the read side (PopularWishlistService, insights).

/// <summary>Derived fields written back to a profile after every event / rebuild.</summary>
public record DerivedProfileFields(
    List<string> TopInteractedTags,
    TravelerPrototypes Prototypes,
    DemographicMatrix Demographics,
    double ConversionProbabilityIndex);

public static class ProfileInference
{
    // ── Event-type keys (interaction_click_count_by_persona_layer) ────────────
    public const string EventWishlistSave = "wishlist_save";
    public const string EventDestinationClick = "destination_click";
    public const string EventSearch = "search";
    public const string EventWishlistView = "wishlist_view";

    /// <summary>tag_counts weight per tag on a wishlist save.</summary>
    public const int SaveTagWeight = 2;
    /// <summary>tag_counts weight per tag on a destination click.</summary>
    public const int ClickTagWeight = 1;
    /// <summary>Rule increment multiplier for destination clicks (weaker signal than a save).</summary>
    public const double ClickVectorMultiplier = 0.5;

    public const int TopTagLimit = 10;
    public const int SearchPhraseLimit = 20;
    public const double VectorMin = 0;
    public const double VectorMax = 100;

    public const string Unknown = "unknown";
    public const string UnknownPrototype = "Unknown";

    /// <summary>BSON field names of personality_vector_scores, in a fixed order.</summary>
    public static readonly string[] VectorKeys =
    [
        "adventure_seeker",
        "social_butterfly",
        "planner_vs_spontaneous",
        "cultural_immersion_index",
        "teen_validation_priority",
        "openness_to_experience",
        "conscientiousness",
        "extraversion",
    ];

    // ── Key normalisation ─────────────────────────────────────────────────────

    /// <summary>
    /// Normalises a tag / segment value to a key safe to use as a Mongo field name:
    /// lower case, trimmed, whitespace and hyphens → '_', every other character that
    /// is not a letter, digit or '_' removed (this strips '.', '$', '&amp;' …),
    /// repeated '_' collapsed, leading/trailing '_' trimmed, max 64 chars.
    /// "Sports Fan" → "sports_fan", "Teen-Approved" → "teen_approved",
    /// "$set.x" → "setx". Returns null when nothing usable remains.
    /// </summary>
    public static string? NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == '_' || ch == '-' || char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            }
            // anything else ('.', '$', '&', quotes, …) is dropped
        }

        var key = sb.ToString().Trim('_');
        if (key.Length > 64) key = key[..64].TrimEnd('_');
        return key.Length == 0 ? null : key;
    }

    /// <summary>Distinct normalised keys of a tag list (empties skipped).</summary>
    public static List<string> NormalizeTags(IEnumerable<string>? tags)
        => (tags ?? Enumerable.Empty<string>())
            .Select(NormalizeKey)
            .Where(k => k is not null)
            .Select(k => k!)
            .Distinct()
            .ToList();

    /// <summary>Like <see cref="NormalizeKey"/> but never null: empty → "unknown".</summary>
    public static string SegmentKeyOrUnknown(string? value) => NormalizeKey(value) ?? Unknown;

    // ── Segment keys (wishlist_segmented_popularity) ──────────────────────────
    // Single source of truth for the writer (TagInferenceService / rebuild) and the
    // reader (PopularWishlistService). Every key is Mongo-field-safe.

    /// <summary>explicit_traveler_types key from the saver's who_they_travel_with answer.</summary>
    public static string TravelerTypeKey(string? whoTheyTravelWith) => SegmentKeyOrUnknown(whoTheyTravelWith);

    /// <summary>budget_tiers key from the saver's budget_profile answer.</summary>
    public static string BudgetTierKey(string? budgetProfile)
    {
        var key = NormalizeKey(budgetProfile);
        return key switch
        {
            null => Unknown,
            "budget" => "budget_under_100_per_day",
            "mid_range" or "midrange" or "moderate" => "mid_range_100_250",
            "premium_comfortable" or "premium" or "comfortable" => "premium_250_500",
            "luxury" => "luxury_500_plus",
            _ => key
        };
    }

    /// <summary>primary_priorities key from the saver's primary_priority answer.</summary>
    public static string PriorityKey(string? primaryPriority)
    {
        var key = NormalizeKey(primaryPriority);
        return key switch
        {
            null => Unknown,
            "saving_money" => "stress_free_logistics",
            "instagram_worthy" => "instagram_worthy",
            "culture" => "cultural_immersion",
            "adventure" => "adventure_activities",
            _ => key
        };
    }

    /// <summary>
    /// persona_vector_clusters key from the saver's primary traveler prototype.
    /// The names are chosen so PopularWishlistService.ParseClusterKey recognises them.
    /// </summary>
    public static string PersonaClusterKey(string? primaryPrototype) => primaryPrototype switch
    {
        "Adventurer" => "cluster_adventure",
        "Luxury Traveller" => "cluster_luxury_couple",
        "Budget Explorer" => "cluster_budget_solo",
        "Family Traveller" => "cluster_family",
        null or "" or UnknownPrototype => "cluster_unknown",
        var p => "cluster_" + SegmentKeyOrUnknown(p)
    };

    // ── Income bracket ────────────────────────────────────────────────────────

    /// <summary>
    /// Self-reported budget → coarse income bracket. Budget is only a proxy for
    /// income, so unknown answers stay "unknown" (we never guess).
    /// </summary>
    public static string IncomeBracket(string? budgetProfile) => NormalizeKey(budgetProfile) switch
    {
        "budget" => "lower",
        "mid_range" or "midrange" or "moderate" => "middle",
        "premium_comfortable" or "premium" or "comfortable" => "upper_middle",
        "luxury" => "high",
        _ => Unknown
    };

    // ── Vectors ───────────────────────────────────────────────────────────────

    public static double Clamp(double v) => Math.Clamp(double.IsFinite(v) ? v : 0, VectorMin, VectorMax);

    public static Dictionary<string, double> VectorsToDictionary(PersonalityVectorScores? s)
    {
        s ??= new PersonalityVectorScores();
        return new Dictionary<string, double>
        {
            ["adventure_seeker"] = s.AdventureSeeker,
            ["social_butterfly"] = s.SocialButterfly,
            ["planner_vs_spontaneous"] = s.PlannerVsSpontaneous,
            ["cultural_immersion_index"] = s.CulturalImmersionIndex,
            ["teen_validation_priority"] = s.TeenValidationPriority,
            ["openness_to_experience"] = s.OpennessToExperience,
            ["conscientiousness"] = s.Conscientiousness,
            ["extraversion"] = s.Extraversion,
        };
    }

    public static PersonalityVectorScores VectorsFromDictionary(IReadOnlyDictionary<string, double> d)
    {
        double Get(string k) => Clamp(d.TryGetValue(k, out var v) ? v : 0);
        return new PersonalityVectorScores
        {
            AdventureSeeker = Get("adventure_seeker"),
            SocialButterfly = Get("social_butterfly"),
            PlannerVsSpontaneous = Get("planner_vs_spontaneous"),
            CulturalImmersionIndex = Get("cultural_immersion_index"),
            TeenValidationPriority = Get("teen_validation_priority"),
            OpennessToExperience = Get("openness_to_experience"),
            Conscientiousness = Get("conscientiousness"),
            Extraversion = Get("extraversion"),
        };
    }

    public static bool IsVectorKey(string? key) => key is not null && VectorKeys.Contains(key);

    // ── Rule application ──────────────────────────────────────────────────────

    /// <summary>
    /// Vector deltas produced by tag rules (wishlist_save / destination_click) for a
    /// set of already-normalised tag keys. Rule source tags are normalised too, so
    /// admin-entered rules like "Sports Fan" still match.
    /// </summary>
    public static Dictionary<string, double> TagRuleDeltas(
        IEnumerable<TagInferenceRule> rules, string sourceType, IEnumerable<string> tagKeys, double multiplier = 1.0)
    {
        var keys = tagKeys.ToHashSet();
        var deltas = new Dictionary<string, double>();
        foreach (var rule in rules)
        {
            if (rule.SourceType != sourceType || !IsVectorKey(rule.TargetUserVector) || rule.IncrementWeight == 0) continue;
            var ruleKey = NormalizeKey(rule.SourceTag);
            if (ruleKey is null || !keys.Contains(ruleKey)) continue;
            Add(deltas, rule.TargetUserVector!, rule.IncrementWeight * multiplier);
        }
        return deltas;
    }

    /// <summary>
    /// Vector deltas from user_profile_field rules for a set of onboarding answers.
    /// A rule fires when the normalised answer equals the rule's normalised
    /// source_value, or contains it as a whole '_'-separated token sequence
    /// ("family_with_teens" fires both "family" and "teens" rules).
    /// </summary>
    public static Dictionary<string, double> OnboardingRuleDeltas(
        IEnumerable<TagInferenceRule> rules, OnboardingAnswers? answers)
    {
        var deltas = new Dictionary<string, double>();
        if (answers is null) return deltas;

        var fieldValues = AnswerFields(answers);
        foreach (var rule in rules)
        {
            if (rule.SourceType != "user_profile_field" || !IsVectorKey(rule.TargetUserVector) || rule.IncrementWeight == 0) continue;
            if (rule.SourceField is null || !fieldValues.TryGetValue(rule.SourceField, out var answer) || answer is null) continue;
            var value = NormalizeKey(rule.SourceValue);
            if (value is null || !AnswerMatches(answer, value)) continue;
            Add(deltas, rule.TargetUserVector!, rule.IncrementWeight);
        }
        return deltas;
    }

    public static Dictionary<string, string?> AnswerFields(OnboardingAnswers a) => new()
    {
        ["who_they_travel_with"] = NormalizeKey(a.WhoTheyTravelWith),
        ["trip_type_preference"] = NormalizeKey(a.TripTypePreference),
        ["budget_profile"] = NormalizeKey(a.BudgetProfile),
        ["primary_priority"] = NormalizeKey(a.PrimaryPriority),
    };

    private static bool AnswerMatches(string answerKey, string valueKey)
        => answerKey == valueKey
        || ("_" + answerKey + "_").Contains("_" + valueKey + "_", StringComparison.Ordinal);

    /// <summary>a + b (missing keys are zero).</summary>
    public static Dictionary<string, double> Combine(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b, double bSign = 1)
    {
        var r = new Dictionary<string, double>(a);
        foreach (var (k, v) in b) Add(r, k, v * bSign);
        return r;
    }

    private static void Add(Dictionary<string, double> d, string key, double v)
        => d[key] = d.TryGetValue(key, out var cur) ? cur + v : v;

    // ── Derived fields ────────────────────────────────────────────────────────

    /// <summary>Top N tag keys by count (ties broken alphabetically).</summary>
    public static List<string> TopTags(IReadOnlyDictionary<string, int>? tagCounts, int limit = TopTagLimit)
        => (tagCounts ?? new Dictionary<string, int>())
            .Where(kv => kv.Value > 0 && !string.IsNullOrEmpty(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>
    /// Conversion probability heuristic: share of "looks" that became saves,
    /// Laplace/Bayes smoothed towards a 25% prior with weight 4:
    ///   (saves + 1) / (saves + views + 4)
    /// where views = destination clicks + wishlist views. With no saves and no views
    /// at all the index is 0 (unknown). Always within [0, 1].
    /// </summary>
    public static double ConversionProbability(int saves, int views)
    {
        saves = Math.Max(0, saves);
        views = Math.Max(0, views);
        if (saves + views == 0) return 0;
        return Math.Round(Math.Clamp((saves + 1.0) / (saves + views + 4.0), 0, 1), 4);
    }

    /// <summary>
    /// Demographic matrix. Only income can be estimated (from the budget answer);
    /// age is never guessed. confidence_score: 0 when nothing is known, 0.4 when the
    /// income bracket is known (one of two dimensions, from a self-reported proxy).
    /// </summary>
    public static DemographicMatrix Demographics(OnboardingAnswers? answers)
    {
        var income = IncomeBracket(answers?.BudgetProfile);
        return new DemographicMatrix
        {
            CalculatedIncomeBracket = income,
            InferredAgeBracket = Unknown,
            ConfidenceScore = income == Unknown ? 0 : 0.4,
        };
    }

    // Prototype definitions: indicative tag keys, contributing vectors and answer keywords.
    private sealed record PrototypeDef(string Name, string[] Tags, (string Vector, double Weight)[] Vectors, string[] AnswerKeywords);

    private static readonly PrototypeDef[] Prototypes =
    [
        new("Adventurer",
            ["adventurous", "adventure", "nature", "nature_outdoor", "hidden_gem", "activity_experience", "road_trip", "once_in_a_lifetime", "quirky", "solo_friendly"],
            [("adventure_seeker", 1.0), ("openness_to_experience", 0.4)],
            ["adventure", "adventurous", "outdoor", "solo"]),
        new("Culture Seeker",
            ["cultural", "culture", "cultural_site", "history", "historic", "art", "architecture", "educational", "landmark_monument", "landmarks", "music", "local_favorite"],
            [("cultural_immersion_index", 1.0)],
            ["culture", "cultural", "history", "heritage", "art"]),
        new("Social Explorer",
            ["social", "nightlife", "entertainment_nightlife", "sports_fan", "group_friendly", "crowded", "music", "urban"],
            [("social_butterfly", 0.8), ("extraversion", 0.6)],
            ["friends", "group", "nightlife", "party", "social"]),
        new("Foodie",
            ["food_focused", "foodie", "food", "food_experience", "market_street_life"],
            [],
            ["food", "foodie", "culinary", "cuisine"]),
        new("Relaxer",
            ["relaxing", "relaxation", "beach", "romantic", "low_traffic", "couple_friendly"],
            [],
            ["relax", "relaxing", "relaxation", "beach", "wellness", "slow"]),
        new("Planner",
            ["good_short_visit", "best_morning", "sightseeing"],
            [("planner_vs_spontaneous", 0.6), ("conscientiousness", 0.6)],
            ["logistics", "stress_free", "planning", "plan"]),
        new("Family Traveller",
            ["family_friendly", "family", "teen_approved", "educational"],
            [("teen_validation_priority", 0.5)],
            ["family", "kids", "children", "teens", "teen"]),
        new("Luxury Traveller",
            ["premium", "luxury"],
            [],
            ["luxury", "premium"]),
        new("Budget Explorer",
            ["budget_friendly", "budget"],
            [],
            ["budget", "saving_money", "cheap", "backpack", "backpacking"]),
    ];

    /// <summary>All prototype names (plus "Unknown"), for admin filters/validation.</summary>
    public static IReadOnlyList<string> PrototypeNames { get; } =
        Prototypes.Select(p => p.Name).Append(UnknownPrototype).ToList();

    /// <summary>Minimum score for a prototype to be assigned at all.</summary>
    private const double MinPrototypeScore = 10;

    /// <summary>
    /// Traveler prototypes. Each prototype scores (roughly 0–200):
    ///   0.6 × share of the user's tag interactions on that prototype's tags (0–100)
    /// + weighted personality vectors (0–100 scale)
    /// + 35 per onboarding answer mentioning that prototype's keywords
    /// The best ≥ 10 becomes primary; up to 2 more scoring ≥ 50% of the primary become
    /// secondary. Confidence = (1 − e^(−signals/15)) × (0.6 + 0.4 × separation),
    /// capped at 0.95, where signals = interactions + 3 per onboarding answer and
    /// separation = (primary − runner-up) / primary. No signals → "Unknown", 0.
    /// </summary>
    public static TravelerPrototypes InferPrototypes(
        PersonalityVectorScores? vectors,
        IReadOnlyDictionary<string, int>? tagCounts,
        OnboardingAnswers? answers,
        int interactionCount)
    {
        var vec = VectorsToDictionary(vectors);
        var counts = tagCounts ?? new Dictionary<string, int>();
        var totalTagWeight = counts.Values.Where(v => v > 0).Sum();
        var answerKeys = answers is null
            ? new List<string>()
            : AnswerFields(answers).Values.Where(v => v is not null).Select(v => v!).ToList();

        var scores = new List<(string Name, double Score)>();
        foreach (var def in Prototypes)
        {
            double score = 0;
            if (totalTagWeight > 0)
            {
                var hits = def.Tags.Sum(t => counts.TryGetValue(t, out var c) && c > 0 ? c : 0);
                score += 0.6 * 100.0 * hits / totalTagWeight;
            }
            foreach (var (vector, weight) in def.Vectors)
                score += weight * (vec.TryGetValue(vector, out var v) ? v : 0);
            foreach (var answer in answerKeys)
                if (def.AnswerKeywords.Any(k => AnswerMatches(answer, k))) score += 35;

            // Budget answer maps directly onto the two spend-based prototypes.
            var bracket = IncomeBracket(answers?.BudgetProfile);
            if (def.Name == "Luxury Traveller" && bracket is "high") score += 15;
            if (def.Name == "Budget Explorer" && bracket is "lower") score += 15;

            scores.Add((def.Name, score));
        }

        var ordered = scores.OrderByDescending(s => s.Score).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        var top = ordered[0];
        var signals = Math.Max(0, interactionCount) + 3 * answerKeys.Count;

        if (top.Score < MinPrototypeScore || signals == 0)
            return new TravelerPrototypes { PrimaryType = UnknownPrototype, SecondaryTypes = new(), ConfidenceScore = 0 };

        var secondary = ordered.Skip(1)
            .Where(s => s.Score >= MinPrototypeScore && s.Score >= 0.5 * top.Score)
            .Take(2)
            .Select(s => s.Name)
            .ToList();

        var runnerUp = ordered.Count > 1 ? ordered[1].Score : 0;
        var separation = top.Score <= 0 ? 0 : Math.Clamp((top.Score - runnerUp) / top.Score, 0, 1);
        var confidence = (1 - Math.Exp(-signals / 15.0)) * (0.6 + 0.4 * separation);

        return new TravelerPrototypes
        {
            PrimaryType = top.Name,
            SecondaryTypes = secondary,
            ConfidenceScore = Math.Round(Math.Clamp(confidence, 0, 0.95), 3),
        };
    }

    /// <summary>Count of a key in the per-event-type counter dictionary.</summary>
    public static int EventCount(IReadOnlyDictionary<string, int>? counts, string eventType)
        => counts is not null && counts.TryGetValue(eventType, out var c) ? c : 0;

    /// <summary>Computes every derived field from a profile's current state.</summary>
    public static DerivedProfileFields ComputeDerived(UserPsychographicProfile profile)
    {
        var agg = profile.InteractionAggregations ?? new InteractionAggregations();
        var layer = agg.InteractionClickCountByPersonaLayer ?? new Dictionary<string, int>();
        var saves = EventCount(layer, EventWishlistSave);
        var clicks = EventCount(layer, EventDestinationClick);
        var views = EventCount(layer, EventWishlistView);
        var searches = EventCount(layer, EventSearch);

        return new DerivedProfileFields(
            TopTags(agg.TagCounts),
            InferPrototypes(profile.PersonalityVectorScores, agg.TagCounts, profile.ExplicitOnboardingAnswers,
                saves + clicks + views + searches),
            Demographics(profile.ExplicitOnboardingAnswers),
            ConversionProbability(saves, clicks + views));
    }

    /// <summary>True when at least one onboarding answer is non-empty.</summary>
    public static bool HasAnyAnswer(OnboardingAnswers? a)
        => a is not null && (!string.IsNullOrWhiteSpace(a.WhoTheyTravelWith)
                          || !string.IsNullOrWhiteSpace(a.TripTypePreference)
                          || !string.IsNullOrWhiteSpace(a.BudgetProfile)
                          || !string.IsNullOrWhiteSpace(a.PrimaryPriority));
}
