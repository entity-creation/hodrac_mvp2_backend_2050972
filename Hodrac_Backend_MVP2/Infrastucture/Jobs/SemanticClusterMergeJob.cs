using Hodrac_Backend_MVP2.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.Jobs
{
    /// <summary>
    /// Runs nightly. Merges AggregatedSearchRegistry entries whose embeddings have
    /// cosine similarity > 0.8, assigning them the same SemanticClusterId and
    /// canonical phrase.
    /// </summary>
    public class SemanticClusterMergeJob
    {
        private readonly HodracDbContext _db;
        private const double MergeThreshold = 0.80;

        public SemanticClusterMergeJob(HodracDbContext db) => _db = db;

        public async Task RunAsync(CancellationToken ct = default)
        {
            // Fetch all entries with embeddings that have no cluster yet
            var unclustered = await _db.AggregatedSearchRegistry
                .Where(r => string.IsNullOrEmpty(r.SemanticClusterId) && r.SemanticEmbedding != null)
                .ToListAsync(ct);

            // For each unclustered entry, find the nearest existing cluster centroid via pgvector
            foreach (var entry in unclustered)
            {
                var nearest = await _db.AggregatedSearchRegistry
                    .Where(r => !string.IsNullOrEmpty(r.SemanticClusterId)
                             && r.SemanticEmbedding != null)
                    .OrderBy(r => r.SemanticEmbedding!.CosineDistance(entry.SemanticEmbedding!))
                    .Select(r => new {
                        r.SemanticClusterId,
                        r.CanonicalSemanticPhrase,
                        Distance = r.SemanticEmbedding!.CosineDistance(entry.SemanticEmbedding!)
                    })
                    .FirstOrDefaultAsync(ct);

                if (nearest is not null && nearest.Distance < (1 - MergeThreshold))
                {
                    // Merge into existing cluster
                    entry.SemanticClusterId = nearest.SemanticClusterId;
                    entry.CanonicalSemanticPhrase = nearest.CanonicalSemanticPhrase;
                }
                else
                {
                    // Create a new cluster with this entry as the seed
                    entry.SemanticClusterId = $"cluster_{Guid.NewGuid():N}";
                    entry.CanonicalSemanticPhrase = entry.MasterSearchPhrase;
                }
            }

            await _db.SaveChangesAsync(ct);
        }
    }
}
