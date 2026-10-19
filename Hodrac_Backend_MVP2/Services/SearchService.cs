using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Hodrac_Backend_MVP2.NoSql.Repositories;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.DTOs.EmbeddingDtos;

namespace Hodrac_Backend_MVP2.Services;

/// <summary>
/// Implements the three-step search pipeline from Part 3.1:
///   Step 1 → Phonetic + variant matching (AggregatedSearchRegistry)
///   Step 2 → Semantic cluster matching (cosine similarity via pgvector)
///   Step 3 → Fresh phrase insertion + nightly merge job hook
/// If the embedding provider is unavailable, Steps 2 and the insert-time cluster
/// assignment are skipped (degraded mode) — the search itself still succeeds.
/// </summary>
public class SearchService
{
    private readonly HodracDbContext _db;
    private readonly IProfileRepository _profiles;
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<SearchService> _logger;

    // Cosine similarity threshold for semantic cluster matching
    private const double SemanticThreshold = 0.85;

    // Cosine distance below which a fresh phrase joins an existing cluster at insert time.
    // Must match SemanticClusterMergeJob's threshold (similarity > 0.80).
    public const double ClusterAssignDistance = 0.20;

    // Matches [MaxLength(500)] on AggregatedSearchRegistry.MasterSearchPhrase
    private const int MaxPhraseLength = 500;

    public SearchService(
        HodracDbContext db,
        IProfileRepository profiles,
        IEmbeddingService embeddings,
        ILogger<SearchService> logger)
    {
        _db = db;
        _profiles = profiles;
        _embeddings = embeddings;
        _logger = logger;
    }

    // ─── Main search entry point ──────────────────────────────────────────────

    /// <param name="authenticatedUserId">
    /// The authenticated caller's id (from ICurrentUserService), or null for anonymous.
    /// Never pass a client-supplied id — it drives the segmented counters.
    /// </param>
    public async Task<SearchResult> SearchAsync(
        string rawQuery,
        string? authenticatedUserId,
        CancellationToken ct = default)
    {
        var normalized = Normalize(rawQuery);
        var userSegment = await GetUserSegmentAsync(authenticatedUserId);

        // Step 1: Phonetic + variant match
        var existing = await TryPhoneticMatchAsync(normalized, ct);

        if (existing is not null)
        {
            await IncrementCountersAsync(existing, userSegment, ct);
            return BuildResult(existing.CanonicalSemanticPhrase, existing.SemanticClusterId);
        }

        // Step 2: Semantic similarity match (skipped when embeddings are unavailable)
        var embedding = await _embeddings.GetEmbeddingAsync(normalized, ct);

        if (embedding is not null)
        {
            var clustered = await TrySemanticMatchAsync(embedding, ct);

            if (clustered is not null)
            {
                // Add this spelling as a known variant so Step 1 catches it next time
                await AddVariantAsync(clustered, normalized, ct);
                await IncrementCountersAsync(clustered, userSegment, ct);
                return BuildResult(clustered.CanonicalSemanticPhrase, clustered.SemanticClusterId);
            }
        }
        else
        {
            _logger.LogWarning(
                "Search running in degraded mode (no embedding) for phrase of length {Length}.",
                normalized.Length);
        }

        // Step 3: Fresh phrase — insert new entry (joining a nearby cluster if one exists)
        var newEntry = await InsertFreshPhraseAsync(normalized, embedding, userSegment, ct);
        return BuildResult(newEntry.CanonicalSemanticPhrase, newEntry.SemanticClusterId);
    }

    // ─── Step 1: Phonetic + variant matching ─────────────────────────────────

    private async Task<AggregatedSearchRegistry?> TryPhoneticMatchAsync(
        string normalized, CancellationToken ct)
    {
        // Exact match on normalized phrase first (fastest path)
        var exact = await _db.AggregatedSearchRegistry
            .FirstOrDefaultAsync(r => r.MasterSearchPhrase == normalized,
                                    ct);

        if (exact is not null) return exact;

        // Fallback: check KnownVariantsJson containment (PostgreSQL jsonb @> operator)
        // Raw SQL used here because EF doesn't natively translate jsonb array containment.
        // The JSON is produced by System.Text.Json so quotes/backslashes are escaped correctly.
        return await _db.AggregatedSearchRegistry
            .FromSqlRaw(
                "SELECT * FROM \"AggregatedSearchRegistry\" WHERE \"KnownVariantsJson\" @> {0}::jsonb LIMIT 1",
                ToJsonArray(normalized)
            )
            .FirstOrDefaultAsync(ct);
    }

    // ─── Step 2: Semantic cluster match via pgvector ──────────────────────────

    private async Task<AggregatedSearchRegistry?> TrySemanticMatchAsync(
        Vector embedding, CancellationToken ct)
    {
        // Cosine distance < (1 - threshold) is equivalent to cosine similarity > threshold.
        // pgvector operator: <=> = cosine distance.
        return await _db.AggregatedSearchRegistry
            .Where(r => r.SemanticEmbedding != null
                     && r.SemanticEmbedding!.CosineDistance(embedding) < (1 - SemanticThreshold))
            .OrderBy(r => r.SemanticEmbedding!.CosineDistance(embedding))
            .FirstOrDefaultAsync(ct);
    }

    // ─── Step 3: Fresh phrase insertion ───────────────────────────────────────

    private async Task<AggregatedSearchRegistry> InsertFreshPhraseAsync(
        string normalized, Vector? embedding, string userSegment, CancellationToken ct)
    {
        var entry = new AggregatedSearchRegistry
        {
            AggregatedSearchRegistryId = Guid.NewGuid(),
            MasterSearchPhrase = normalized,
            CanonicalSemanticPhrase = normalized,  // Overwritten if a nearby cluster exists
            SemanticClusterId = string.Empty,       // Left for the nightly merge job if no cluster is near
            SemanticEmbedding = embedding,
            KnownVariantsJson = "[]",
            TotalGlobalSearchCount = 1,
            LastSearchedAt = DateTimeOffset.UtcNow,
        };

        switch (userSegment)
        {
            case "young_couple":     entry.YoungCoupleSearchCount = 1;   break;
            case "family_planner":   entry.FamilyPlannerSearchCount = 1; break;
            case "adventure_dad":    entry.AdventureDadSearchCount = 1;  break;
        }

        if (embedding is not null)
        {
            var nearest = await FindNearestClusterAsync(_db, embedding, ct);
            if (nearest is not null && nearest.Distance < ClusterAssignDistance)
            {
                entry.SemanticClusterId = nearest.SemanticClusterId;
                entry.CanonicalSemanticPhrase = nearest.CanonicalSemanticPhrase;
            }
        }

        _db.AggregatedSearchRegistry.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>
    /// Nearest entry that already belongs to a cluster, with its cosine distance.
    /// Shared with SemanticClusterMergeJob so both use identical logic.
    /// </summary>
    internal static Task<NearestCluster?> FindNearestClusterAsync(
        HodracDbContext db, Vector embedding, CancellationToken ct, Guid? excludeId = null)
        => db.AggregatedSearchRegistry
            .AsNoTracking()
            .Where(r => r.SemanticClusterId != null && r.SemanticClusterId != ""
                     && r.SemanticEmbedding != null
                     && (excludeId == null || r.AggregatedSearchRegistryId != excludeId))
            .OrderBy(r => r.SemanticEmbedding!.CosineDistance(embedding))
            .Select(r => new NearestCluster(
                r.SemanticClusterId,
                r.CanonicalSemanticPhrase,
                r.SemanticEmbedding!.CosineDistance(embedding)))
            .FirstOrDefaultAsync(ct);

    // ─── Counter increment ────────────────────────────────────────────────────

    private async Task IncrementCountersAsync(
        AggregatedSearchRegistry entry, string userSegment, CancellationToken ct)
    {
        entry.TotalGlobalSearchCount++;
        entry.LastSearchedAt = DateTimeOffset.UtcNow;

        switch (userSegment)
        {
            case "young_couple":     entry.YoungCoupleSearchCount++;   break;
            case "family_planner":   entry.FamilyPlannerSearchCount++; break;
            case "adventure_dad":    entry.AdventureDadSearchCount++;  break;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task AddVariantAsync(
        AggregatedSearchRegistry entry, string variant, CancellationToken ct)
    {
        // Append variant to the jsonb array (skipped if already present)
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE \"AggregatedSearchRegistry\" " +
            "SET \"KnownVariantsJson\" = \"KnownVariantsJson\" || {0}::jsonb " +
            "WHERE \"AggregatedSearchRegistryId\" = {1} " +
            "AND NOT (\"KnownVariantsJson\" @> {0}::jsonb)",
            ct,
            ToJsonArray(variant),
            entry.AggregatedSearchRegistryId
        );
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Serialises a single string as a JSON array, e.g. ["tokyo food"].</summary>
    private static string ToJsonArray(string value)
        => JsonSerializer.Serialize(new[] { value });

    internal static string Normalize(string input)
    {
        var collapsed = string.Join(' ',
            input.Trim().ToLowerInvariant()
                 .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > MaxPhraseLength ? collapsed[..MaxPhraseLength] : collapsed;
    }

    private static SearchResult BuildResult(string canonical, string clusterId)
        => new(canonical, clusterId);

    private async Task<string> GetUserSegmentAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return "unknown";
        var profile = await _profiles.GetByAuthUserIdAsync(userId);
        if (profile is null) return "unknown";

        // Derive segment from the highest personality vector score
        var scores = profile.PersonalityVectorScores;
        if (scores.TeenValidationPriority > 0.7) return "family_planner";
        if (scores.AdventureSeeker > 0.7)       return "adventure_dad";
        if (scores.SocialButterfly > 0.6)       return "young_couple";
        return "unknown";
    }
}

public record SearchResult(string CanonicalPhrase, string SemanticClusterId);

internal record NearestCluster(string SemanticClusterId, string CanonicalSemanticPhrase, double Distance);

// ─── Embedding service abstraction ───────────────────────────────────────────

/// <summary>
/// Wrap your embedding provider (Azure OpenAI, local MiniLM, etc.) behind this interface.
/// The SearchService only depends on this — swap providers without touching search logic.
/// Implementations must not throw for provider failures: return null instead so callers
/// can continue in degraded mode. Cancellation of <paramref name="ct"/> may still throw.
/// </summary>
public interface IEmbeddingService
{
    Task<Vector?> GetEmbeddingAsync(string text, CancellationToken ct = default);
}

public class PythonEmbeddingService : IEmbeddingService
{
    // Must match the vector(384) column on AggregatedSearchRegistry.SemanticEmbedding
    public const int ExpectedDimensions = 384;

    // Upper bound per call, independent of HttpClient.Timeout (which defaults to 100s)
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly ILogger<PythonEmbeddingService> _logger;

    public PythonEmbeddingService(HttpClient http, ILogger<PythonEmbeddingService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Vector?> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CallTimeout);

        try
        {
            using var response = await _http.PostAsJsonAsync(
                "/embed",
                new EmbeddingRequest { Text = text },
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Embedding service returned {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
                cancellationToken: timeoutCts.Token);
            var values = result?.Embedding;

            if (values is null || values.Length == 0)
            {
                _logger.LogWarning("Embedding service returned an empty embedding.");
                return null;
            }

            if (values.Length != ExpectedDimensions)
            {
                _logger.LogWarning(
                    "Embedding service returned {Actual} dimensions; expected {Expected}.",
                    values.Length, ExpectedDimensions);
                return null;
            }

            if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || values.All(v => v == 0))
            {
                _logger.LogWarning("Embedding service returned an invalid (NaN/Inf/zero) embedding.");
                return null;
            }

            return new Vector(values.Select(x => (float)x).ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancelled (e.g. client disconnected) — propagate
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Embedding service timed out after {Timeout}.", CallTimeout);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Embedding service call failed.");
            return null;
        }
    }
}
