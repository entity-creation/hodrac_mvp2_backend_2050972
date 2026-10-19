using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.Jobs
{
    /// <summary>
    /// Runs nightly. Merges AggregatedSearchRegistry entries whose embeddings have
    /// cosine similarity > 0.8, assigning them the same SemanticClusterId and
    /// canonical phrase.
    /// Also backfills embeddings for entries inserted while the embedding service
    /// was unavailable (degraded-mode searches), so they can be clustered too.
    /// </summary>
    public class SemanticClusterMergeJob
    {
        private readonly HodracDbContext _db;
        private readonly IEmbeddingService _embeddings;
        private readonly ILogger<SemanticClusterMergeJob> _logger;

        // Cosine distance threshold — same value SearchService uses at insert time.
        private const double MergeDistance = SearchService.ClusterAssignDistance;

        // Cap on embedding backfill calls per run so a long outage can't stall the job.
        private const int MaxBackfillPerRun = 500;

        public SemanticClusterMergeJob(
            HodracDbContext db,
            IEmbeddingService embeddings,
            ILogger<SemanticClusterMergeJob> logger)
        {
            _db = db;
            _embeddings = embeddings;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken ct = default)
        {
            await BackfillMissingEmbeddingsAsync(ct);

            // Fetch all entries with embeddings that have no cluster yet.
            // Oldest first so the earliest phrase seeds the canonical wording of its cluster.
            var unclustered = await _db.AggregatedSearchRegistry
                .Where(r => (r.SemanticClusterId == null || r.SemanticClusterId == "")
                         && r.SemanticEmbedding != null)
                .OrderBy(r => r.LastSearchedAt)
                .ToListAsync(ct);

            int merged = 0, seeded = 0;

            // Each assignment is saved before the next lookup, so an entry clustered
            // earlier in this run is visible (as a cluster member) to later entries.
            foreach (var entry in unclustered)
            {
                var nearest = await SearchService.FindNearestClusterAsync(
                    _db, entry.SemanticEmbedding!, ct, entry.AggregatedSearchRegistryId);

                if (nearest is not null && nearest.Distance < MergeDistance)
                {
                    // Merge into existing cluster
                    entry.SemanticClusterId = nearest.SemanticClusterId;
                    entry.CanonicalSemanticPhrase = nearest.CanonicalSemanticPhrase;
                    merged++;
                }
                else
                {
                    // Create a new cluster with this entry as the seed
                    entry.SemanticClusterId = $"cluster_{Guid.NewGuid():N}";
                    entry.CanonicalSemanticPhrase = entry.MasterSearchPhrase;
                    seeded++;
                }

                await _db.SaveChangesAsync(ct);
            }

            _logger.LogInformation(
                "SemanticClusterMergeJob: {Merged} merged into existing clusters, {Seeded} new clusters.",
                merged, seeded);
        }

        private async Task BackfillMissingEmbeddingsAsync(CancellationToken ct)
        {
            var missing = await _db.AggregatedSearchRegistry
                .Where(r => r.SemanticEmbedding == null)
                .OrderByDescending(r => r.TotalGlobalSearchCount)
                .Take(MaxBackfillPerRun)
                .ToListAsync(ct);

            foreach (var entry in missing)
            {
                var embedding = await _embeddings.GetEmbeddingAsync(entry.MasterSearchPhrase, ct);
                if (embedding is null)
                {
                    // Provider still unavailable — stop and retry on the next run.
                    _logger.LogWarning(
                        "SemanticClusterMergeJob: embedding backfill stopped; provider unavailable.");
                    break;
                }

                entry.SemanticEmbedding = embedding;
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}
