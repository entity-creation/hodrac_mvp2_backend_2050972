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
    private readonly ISegmentedPopularityRepository _popularity;
    private readonly IProfileRepository _profiles;
    private readonly HodracDbContext _db;

    public PopularWishlistService(
        ISegmentedPopularityRepository popularity,
        IProfileRepository profiles,
        HodracDbContext db)
    {
        _popularity = popularity;
        _profiles = profiles;
        _db = db;
    }

    public async Task<List<Guid>> GetRankedWishlistIdsAsync(string? userId, int limit = 20)
    {
        UserPsychographicProfile? profile = null;
        if (!string.IsNullOrEmpty(userId))
            profile = await _profiles.GetByAuthUserIdAsync(userId);

        // Fetch all popularity documents from MongoDB
        var allPopularity = await _popularity.GetAllStaleAsync(DateTime.MinValue); // all docs

        if (profile is null)
        {
            // Unauthenticated or no profile: rank by total global save count (Postgres)
            return await _db.Wishlists
                .Where(w => w.IsTemplate)
                .OrderByDescending(w => w.TotalGlobalSaveCount)
                .Take(limit)
                .Select(w => w.WishlistId)
                .ToListAsync();
        }

        // Score each wishlist by cosine similarity between user vector and cluster keys
        var userVector = ExtractUserVector(profile.PersonalityVectorScores);
        var scored = new List<(string WishlistId, double Score)>();

        foreach (var doc in allPopularity)
        {
            var totalScore = 0.0;
            foreach (var (clusterKey, saveCount) in doc.SegmentPopularityScores.PersonaVectorClusters)
            {
                var clusterVector = ParseClusterKey(clusterKey);
                if (clusterVector is null) continue;
                var similarity = CosineSimilarity(userVector, clusterVector);
                totalScore += similarity * saveCount;
            }
            scored.Add((doc.WishlistId, totalScore));
        }

        var topWishlistIds = scored
            .OrderByDescending(s => s.Score)
            .Take(limit)
            .Select(s => s.WishlistId)
            .ToList();

        // Resolve string IDs back to Guids (wishlist_id stored as string in MongoDB)
        var guids = topWishlistIds
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse)
            .ToList();

        return guids;
    }

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
                     && p.CurrentImpressionsToday < p.DailyImpressionLimit)
            .OrderByDescending(p => p.PaidAmount)
            .Take(2)
            .Select(p => p.WishlistId)
            .ToListAsync(ct);
        result.AddRange(paid);

        // 2. Editorial (3 curated)
        var editorial = await _db.FeaturedWishlistPool
            .Where(p => p.PoolType == "Editorial")
            .Take(3)
            .Select(p => p.WishlistId)
            .ToListAsync(ct);
        result.AddRange(editorial);

        // 3. Random weighted pool (5)
        var randomPool = await _db.FeaturedWishlistPool
            .Where(p => p.PoolType == "Random")
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
