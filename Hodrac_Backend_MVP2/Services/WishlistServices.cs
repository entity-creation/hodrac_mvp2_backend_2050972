using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using Hodrac_Backend_MVP2.NoSql.Models;
using Hodrac_Backend_MVP2.NoSql.Repositories;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.Data;

namespace Hodrac_Backend_MVP2.Services;

// ─── Popular Wishlists ────────────────────────────────────────────────────────

/// <summary>
/// Returns wishlists ranked by how well they match the requesting user's
/// psychographic vector. Implements Part 3.2 of the spec.
/// </summary>
public class PopularWishlistService
{
    /// <summary>
    /// How many top-saved templates are considered for personalised ranking.
    /// Bounds both the Postgres read and the Mongo popularity-doc fetch per request.
    /// </summary>
    private const int CandidatePoolSize = 200;

    private readonly IMongoCollection<WishlistSegmentedPopularity> _popularity;
    private readonly IProfileRepository _profiles;
    private readonly HodracDbContext _db;
    private readonly ILogger<PopularWishlistService> _logger;

    public PopularWishlistService(
        IMongoDatabase mongo,
        IProfileRepository profiles,
        HodracDbContext db,
        ILogger<PopularWishlistService> logger)
    {
        _popularity = mongo.GetCollection<WishlistSegmentedPopularity>("wishlist_segmented_popularity");
        _profiles = profiles;
        _db = db;
        _logger = logger;
    }

    public async Task<List<Guid>> GetRankedWishlistIdsAsync(string? userId, int limit = 20, CancellationToken ct = default)
    {
        // Candidate pool: most-saved templates. Also the generic ranking used when
        // there is no profile or the personalised ranking yields nothing.
        var candidates = await _db.Wishlists
            .AsNoTracking()
            .Where(w => w.IsTemplate)
            .OrderByDescending(w => w.TotalGlobalSaveCount)
            .Take(Math.Max(limit, CandidatePoolSize))
            .Select(w => w.WishlistId)
            .ToListAsync(ct);

        UserPsychographicProfile? profile = null;
        List<WishlistSegmentedPopularity> docs = new();
        if (!string.IsNullOrEmpty(userId))
        {
            try
            {
                profile = await _profiles.GetByAuthUserIdAsync(userId);
                if (profile is not null && candidates.Count > 0)
                {
                    // Only the popularity docs for the candidate pool, not the whole collection.
                    var idStrings = candidates.Select(id => id.ToString()).ToList();
                    docs = await _popularity
                        .Find(Builders<WishlistSegmentedPopularity>.Filter.In(p => p.WishlistId, idStrings))
                        .ToListAsync(ct);
                }
            }
            catch (Exception ex)
            {
                // Mongo unavailable → fall back to the generic ranking.
                _logger.LogWarning(ex, "Popular wishlists: personalised ranking unavailable for {UserId}", userId);
                profile = null;
            }
        }

        if (profile is null)
            return candidates.Take(limit).ToList();   // unauthenticated / no profile

        var userVector = ExtractUserVector(profile.PersonalityVectorScores);
        var answers = profile.ExplicitOnboardingAnswers ?? new OnboardingAnswers();
        // Same sanitised segment keys TagInferenceService / the rebuild job write.
        // Missing answers → null (never match the "unknown" bucket).
        var travelerType = string.IsNullOrWhiteSpace(answers.WhoTheyTravelWith) ? null : ProfileInference.TravelerTypeKey(answers.WhoTheyTravelWith);
        var budgetTier = string.IsNullOrWhiteSpace(answers.BudgetProfile) ? null : ProfileInference.BudgetTierKey(answers.BudgetProfile);
        var priority = string.IsNullOrWhiteSpace(answers.PrimaryPriority) ? null : ProfileInference.PriorityKey(answers.PrimaryPriority);

        var scored = new List<(Guid WishlistId, double Score)>();
        foreach (var doc in docs)
        {
            if (!Guid.TryParse(doc.WishlistId, out var wishlistId)) continue;
            var seg = doc.SegmentPopularityScores ?? new SegmentPopularityScores();
            var totalScore = 0.0;

            // Persona-cluster saves weighted by similarity to the user's vector.
            foreach (var (clusterKey, saveCount) in seg.PersonaVectorClusters ?? new())
            {
                var clusterVector = ParseClusterKey(clusterKey);
                if (clusterVector is null) continue;
                totalScore += CosineSimilarity(userVector, clusterVector) * saveCount;
            }

            // Saves by people in the user's own segments (the counters TagInferenceService
            // maintains on every save: traveler type, budget tier, primary priority).
            totalScore += SegmentCount(seg.ExplicitTravelerTypes, travelerType);
            totalScore += SegmentCount(seg.BudgetTiers, budgetTier);
            totalScore += SegmentCount(seg.PrimaryPriorities, priority);

            if (totalScore > 0) scored.Add((wishlistId, totalScore));
        }

        // Personalised order first, then the remaining candidates by global saves.
        var saveRank = candidates.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var ranked = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => saveRank.GetValueOrDefault(s.WishlistId, int.MaxValue))
            .Select(s => s.WishlistId)
            .ToList();

        var rankedSet = ranked.ToHashSet();
        ranked.AddRange(candidates.Where(id => !rankedSet.Contains(id)));
        return ranked.Take(limit).ToList();
    }

    private static double SegmentCount(Dictionary<string, int>? segment, string? key)
        => segment is not null && !string.IsNullOrEmpty(key) && segment.TryGetValue(key, out var count)
            ? count
            : 0;

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static double[] ExtractUserVector(PersonalityVectorScores scores) =>
    [
        scores.AdventureSeeker,
        scores.SocialButterfly,
        scores.PlannerVsSpontaneous,
        scores.CulturalImmersionIndex,
        scores.TeenValidationPriority,
        scores.OpennessToExperience,
        scores.Conscientiousness,
        scores.Extraversion,
    ];

    /// <summary>
    /// Cluster keys encode vector distance inline: "cluster_adventure_family_0.8"
    /// This is a weak heuristic — the nightly job produces these keys.
    /// For a robust implementation, store cluster centroid vectors in MongoDB.
    /// </summary>
    private static double[]? ParseClusterKey(string clusterKey)
    {
        // Minimal implementation: map known cluster names to reference vectors
        return clusterKey switch
        {
            var k when k.Contains("adventure") => [0.8, 0.3, 0.4, 0.6, 0.2, 0.7, 0.3, 0.5],
            var k when k.Contains("luxury_couple") => [0.2, 0.7, 0.6, 0.5, 0.1, 0.5, 0.6, 0.7],
            var k when k.Contains("budget_solo") => [0.5, 0.2, 0.3, 0.4, 0.1, 0.6, 0.5, 0.3],
            var k when k.Contains("family") => [0.3, 0.5, 0.8, 0.7, 0.9, 0.5, 0.8, 0.4],
            _ => null
        };
    }

    private static double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length) return 0;
        var dot = a.Zip(b).Sum(p => p.First * p.Second);
        var magA = Math.Sqrt(a.Sum(x => x * x));
        var magB = Math.Sqrt(b.Sum(x => x * x));
        return (magA == 0 || magB == 0) ? 0 : dot / (magA * magB);
    }
}

// ─── Featured Wishlist Rotation ───────────────────────────────────────────────

/// <summary>
/// Implements the hourly rotation logic from Part 3.3:
///   - 2 Paid (by PaidAmount desc)
///   - 3 Editorial (manually curated, IsFeatured = true)
///   - 5+ Random (weighted by RandomSelectionWeight)
/// Results cached in Redis (see FeaturedCacheService).
/// </summary>
public class FeaturedWishlistService
{
    private readonly HodracDbContext _db;
    private readonly Random _rng = new();

    public FeaturedWishlistService(HodracDbContext db) => _db = db;

    public async Task<List<Guid>> GetFeaturedWishlistIdsAsync(CancellationToken ct = default)
    {
        var result = new List<Guid>();

        // 1. Paid (top 2, ordered by spend)
        var paid = await _db.FeaturedWishlistPool
            .Where(p => p.PoolType == "Paid"
                     && p.Wishlist.IsTemplate
                     && p.CurrentImpressionsToday < p.DailyImpressionLimit)
            .OrderByDescending(p => p.PaidAmount)
            .ThenBy(p => p.FeaturedWishlistPoolId)
            .Take(2)
            .Select(p => p.WishlistId)
            .ToListAsync(ct);
        result.AddRange(paid);

        // 2. Editorial (3 curated) — deterministic: most-saved first, id as tie-breaker
        var editorial = await _db.FeaturedWishlistPool
            .Where(p => p.PoolType == "Editorial" && p.Wishlist.IsTemplate)
            .OrderByDescending(p => p.Wishlist.TotalGlobalSaveCount)
            .ThenBy(p => p.FeaturedWishlistPoolId)
            .Take(3)
            .Select(p => p.WishlistId)
            .ToListAsync(ct);
        result.AddRange(editorial);

        // 3. Random weighted pool (5)
        var randomPool = await _db.FeaturedWishlistPool
            .Where(p => p.PoolType == "Random" && p.Wishlist.IsTemplate)
            .Select(p => new { p.WishlistId, p.RandomSelectionWeight })
            .ToListAsync(ct);

        var selected = WeightedSample(randomPool.Select(r => (r.WishlistId, r.RandomSelectionWeight)), 5);
        result.AddRange(selected);

        // Increment impression counters for paid entries
        foreach (var wishlistId in paid)
        {
            await _db.FeaturedWishlistPool
                .Where(p => p.WishlistId == wishlistId && p.PoolType == "Paid")
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CurrentImpressionsToday,
                    p => p.CurrentImpressionsToday + 1), ct);
        }

        return result.Distinct().ToList();
    }

    private List<Guid> WeightedSample(IEnumerable<(Guid Id, double Weight)> pool, int count)
    {
        var items = pool.ToList();
        var selected = new List<Guid>();
        var totalWeight = items.Sum(i => i.Weight);

        for (int i = 0; i < count && items.Count > 0; i++)
        {
            var threshold = _rng.NextDouble() * totalWeight;
            var cumulative = 0.0;
            foreach (var item in items)
            {
                cumulative += item.Weight;
                if (cumulative >= threshold)
                {
                    selected.Add(item.Id);
                    totalWeight -= item.Weight;
                    items.Remove(item);
                    break;
                }
            }
        }
        return selected;
    }
}
