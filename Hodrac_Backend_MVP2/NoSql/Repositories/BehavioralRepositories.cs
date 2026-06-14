using MongoDB.Driver;
using Hodrac_Backend_MVP2.NoSql.Models;
using Hodrac_Backend_MVP2.NoSql.Interfaces;

namespace Hodrac_Backend_MVP2.NoSql.Repositories;

// ─── ProfileRepository ────────────────────────────────────────────────────────

public class ProfileRepository : IProfileRepository
{
    private readonly IMongoCollection<UserPsychographicProfile> _col;

    public ProfileRepository(IMongoDatabase db)
        => _col = db.GetCollection<UserPsychographicProfile>("user_psychographic_profiles");

    public async Task<UserPsychographicProfile?> GetByAuthUserIdAsync(string authUserId)
    {
        var filter = Builders<UserPsychographicProfile>.Filter
            .Eq(p => p.AuthUserReferenceId, authUserId);
        return await _col.Find(filter).FirstOrDefaultAsync();
    }

    public async Task UpsertAsync(UserPsychographicProfile profile)
    {
        var filter = Builders<UserPsychographicProfile>.Filter
            .Eq(p => p.AuthUserReferenceId, profile.AuthUserReferenceId);
        var options = new ReplaceOptions { IsUpsert = true };
        await _col.ReplaceOneAsync(filter, profile, options);
    }

    /// <summary>
    /// Atomically increments a single personality vector dimension.
    /// vectorKey must match one of the BSON field names in personality_vector_scores.
    /// </summary>
    public async Task UpdateVectorScoresAsync(string authUserId, string vectorKey, double increment)
    {
        var filter = Builders<UserPsychographicProfile>.Filter
            .Eq(p => p.AuthUserReferenceId, authUserId);

        var update = Builders<UserPsychographicProfile>.Update
            .Inc($"personality_vector_scores.{vectorKey}", increment)
            .CurrentDate("system_telemetry.last_interaction_delta_at");

        await _col.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = false });
    }

    /// <summary>Adds a tag to the top_interacted_wishlist_tags array if not already present.</summary>
    public async Task AppendTagAsync(string authUserId, string tag)
    {
        var filter = Builders<UserPsychographicProfile>.Filter
            .Eq(p => p.AuthUserReferenceId, authUserId);

        var update = Builders<UserPsychographicProfile>.Update
            .AddToSet("interaction_aggregations.top_interacted_wishlist_tags", tag);

        await _col.UpdateOneAsync(filter, update);
    }
}

// ─── InteractionEventRepository ───────────────────────────────────────────────

public class InteractionEventRepository : IInteractionEventRepository
{
    private readonly IMongoCollection<InteractionEvent> _col;

    public InteractionEventRepository(IMongoDatabase db)
        => _col = db.GetCollection<InteractionEvent>("interaction_event_stream");

    public async Task InsertAsync(InteractionEvent evt)
        => await _col.InsertOneAsync(evt);

    public async Task<List<InteractionEvent>> GetRecentByUserAsync(string userId, int limit = 50)
    {
        var filter = Builders<InteractionEvent>.Filter.Eq(e => e.UserId, userId);
        return await _col.Find(filter)
                         .SortByDescending(e => e.Timestamp)
                         .Limit(limit)
                         .ToListAsync();
    }
}

// ─── SegmentedPopularityRepository ───────────────────────────────────────────

public class SegmentedPopularityRepository : ISegmentedPopularityRepository
{
    private readonly IMongoCollection<WishlistSegmentedPopularity> _col;

    public SegmentedPopularityRepository(IMongoDatabase db)
        => _col = db.GetCollection<WishlistSegmentedPopularity>("wishlist_segmented_popularity");

    public async Task<WishlistSegmentedPopularity?> GetByWishlistIdAsync(string wishlistId)
    {
        var filter = Builders<WishlistSegmentedPopularity>.Filter.Eq(p => p.WishlistId, wishlistId);
        return await _col.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<List<WishlistSegmentedPopularity>> GetAllStaleAsync(DateTime olderThan)
    {
        var filter = Builders<WishlistSegmentedPopularity>.Filter.Lt(p => p.LastComputedAt, olderThan);
        return await _col.Find(filter).ToListAsync();
    }

    public async Task UpsertAsync(WishlistSegmentedPopularity doc)
    {
        var filter = Builders<WishlistSegmentedPopularity>.Filter.Eq(p => p.WishlistId, doc.WishlistId);
        await _col.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true });
    }

    /// <summary>
    /// Atomically increments a single segment counter.
    /// segmentPath: e.g. "segment_popularity_scores.explicit_traveler_types"
    /// segmentKey: e.g. "Family with Teens"
    /// </summary>
    public async Task IncrementSegmentAsync(string wishlistId, string segmentPath, string segmentKey)
    {
        var filter = Builders<WishlistSegmentedPopularity>.Filter.Eq(p => p.WishlistId, wishlistId);
        var update = Builders<WishlistSegmentedPopularity>.Update
            .Inc($"{segmentPath}.{segmentKey}", 1)
            .CurrentDate("last_computed_at");
        await _col.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
    }
}
