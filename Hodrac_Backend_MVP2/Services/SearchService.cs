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
/// </summary>
public class SearchService
{
    private readonly HodracDbContext _db;
    private readonly IProfileRepository _profiles;
    private readonly IEmbeddingService _embeddings;

    // Cosine similarity threshold for semantic cluster matching
    private const double SemanticThreshold = 0.85;

    public SearchService(
        HodracDbContext db,
        IProfileRepository profiles,
        IEmbeddingService embeddings)
    {
        _db = db;
        _profiles = profiles;
        _embeddings = embeddings;
    }

    // ─── Main search entry point ──────────────────────────────────────────────

    public async Task<SearchResult> SearchAsync(
        string rawQuery,
        string? userId,
        CancellationToken ct = default)
    {
        var normalized = Normalize(rawQuery);
        var userSegment = await GetUserSegmentAsync(userId);

        // Step 1: Phonetic + variant match
        var existing = await TryPhoneticMatchAsync(normalized, ct);

        if (existing is not null)
        {
            await IncrementCountersAsync(existing, userSegment, ct);
            return BuildResult(existing.CanonicalSemanticPhrase, existing.SemanticClusterId);
        }

        // Step 2: Semantic similarity match
        var embedding = await _embeddings.GetEmbeddingAsync(normalized);
        var clustered = await TrySemanticMatchAsync(embedding, ct);

        if (clustered is not null)
        {
            // Add this spelling as a known variant so Step 1 catches it next time
            await AddVariantAsync(clustered, normalized, ct);
            await IncrementCountersAsync(clustered, userSegment, ct);
            return BuildResult(clustered.CanonicalSemanticPhrase, clustered.SemanticClusterId);
        }

        // Step 3: Fresh phrase — insert new entry, nightly job will merge clusters
        var newEntry = await InsertFreshPhraseAsync(normalized, embedding, ct);
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
        return await _db.AggregatedSearchRegistry
            .FromSqlRaw(
                "SELECT * FROM \"AggregatedSearchRegistry\" WHERE \"KnownVariantsJson\" @> {0}::jsonb LIMIT 1",
                $"[\"{normalized}\"]"
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
            .Where(r => r.SemanticEmbedding != null)
            .OrderBy(r => r.SemanticEmbedding!.CosineDistance(embedding))
            .Select(r => new { r, Distance = r.SemanticEmbedding!.CosineDistance(embedding) })
            .Where(x => x.Distance < (1 - SemanticThreshold))
            .Select(x => x.r)
            .FirstOrDefaultAsync(ct);
    }

    // ─── Step 3: Fresh phrase insertion ───────────────────────────────────────

    private async Task<AggregatedSearchRegistry> InsertFreshPhraseAsync(
        string normalized, Vector embedding, CancellationToken ct)
    {
        var entry = new AggregatedSearchRegistry
        {
            AggregatedSearchRegistryId = Guid.NewGuid(),
            MasterSearchPhrase = normalized,
            CanonicalSemanticPhrase = normalized,  // Overwritten by nightly merge job
            SemanticClusterId = string.Empty,       // Assigned by nightly merge job
            SemanticEmbedding = embedding,
            KnownVariantsJson = "[]",
            TotalGlobalSearchCount = 1,
            LastSearchedAt = DateTimeOffset.UtcNow,
        };
        _db.AggregatedSearchRegistry.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

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
        // Append variant to jsonb array using PostgreSQL jsonb_insert
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE \"AggregatedSearchRegistry\" " +
            "SET \"KnownVariantsJson\" = \"KnownVariantsJson\" || {0}::jsonb " +
            "WHERE \"AggregatedSearchRegistryId\" = {1}",
            ct,
            $"[\"{variant}\"]",
            entry.AggregatedSearchRegistryId       
        );
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string Normalize(string input)
        => input.Trim().ToLowerInvariant()
                .Replace("  ", " ");

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

// ─── Embedding service abstraction ───────────────────────────────────────────

/// <summary>
/// Wrap your embedding provider (Azure OpenAI, local MiniLM, etc.) behind this interface.
/// The SearchService only depends on this — swap providers without touching search logic.
/// </summary>
public interface IEmbeddingService
{
    Task<Vector> GetEmbeddingAsync(string text);
}

public class PythonEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;

    public PythonEmbeddingService(HttpClient http)
    {
        _http = http;
    }
    public async Task<Vector> GetEmbeddingAsync(string text)
    {
        var response = await _http.PostAsJsonAsync(
            "/embed",
            new EmbeddingRequest { Text = text}
            );
        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();
        var vector = new Vector(
    result?.Embedding
          .Select(x => (float)x)
          .ToArray());
        return vector;
    }
}
