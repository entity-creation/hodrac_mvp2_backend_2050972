using MongoDB.Bson;
using MongoDB.Driver;
using Hodrac_Backend_MVP2.NoSql.Models;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.Services;

namespace Hodrac_Backend_MVP2.NoSql.Repositories;

// ─── ProfileRepository ────────────────────────────────────────────────────────

public class ProfileRepository : IProfileRepository
{
    public const string CollectionName = "user_psychographic_profiles";

    private const string Agg = "interaction_aggregations";
    private const string LastInteraction = "system_telemetry.last_interaction_delta_at";

    private readonly IMongoCollection<UserPsychographicProfile> _col;

    public ProfileRepository(IMongoDatabase db)
        => _col = db.GetCollection<UserPsychographicProfile>(CollectionName);

    private static FilterDefinition<UserPsychographicProfile> ByUser(string authUserId)
        => Builders<UserPsychographicProfile>.Filter.Eq(p => p.AuthUserReferenceId, authUserId);

    public async Task<UserPsychographicProfile?> GetByAuthUserIdAsync(string authUserId)
        => await _col.Find(ByUser(authUserId)).FirstOrDefaultAsync();

    public async Task UpsertAsync(UserPsychographicProfile profile)
    {
        var options = new ReplaceOptions { IsUpsert = true };
        await _col.ReplaceOneAsync(ByUser(profile.AuthUserReferenceId), profile, options);
    }

    /// <summary>Same shape as the register-time profile (AuthController.Register).</summary>
    public static UserPsychographicProfile NewMinimalProfile(string authUserId, DateTime at) => new()
    {
        Id = $"prof_{authUserId}",
        AuthUserReferenceId = authUserId,
        AnonymizedMarketplaceId = $"anon_{Guid.NewGuid():N}",
        InferredDemographicMatrix = new DemographicMatrix
        {
            CalculatedIncomeBracket = ProfileInference.Unknown,
            InferredAgeBracket = ProfileInference.Unknown,
        },
        InferredTravelerPrototypes = new TravelerPrototypes { PrimaryType = ProfileInference.UnknownPrototype },
        SystemTelemetry = new SystemTelemetry { FirstSeenAt = at, LastInteractionDeltaAt = at },
    };

    public async Task EnsureExistsAsync(string authUserId, DateTime at)
    {
        var p = NewMinimalProfile(authUserId, at);
        var update = Builders<UserPsychographicProfile>.Update
            .SetOnInsert(x => x.Id, p.Id)
            .SetOnInsert(x => x.AnonymizedMarketplaceId, p.AnonymizedMarketplaceId)
            .SetOnInsert(x => x.ExplicitOnboardingAnswers, p.ExplicitOnboardingAnswers)
            .SetOnInsert(x => x.InferredDemographicMatrix, p.InferredDemographicMatrix)
            .SetOnInsert(x => x.PersonalityVectorScores, p.PersonalityVectorScores)
            .SetOnInsert(x => x.InferredTravelerPrototypes, p.InferredTravelerPrototypes)
            .SetOnInsert(x => x.InteractionAggregations, p.InteractionAggregations)
            .SetOnInsert(x => x.SystemTelemetry, p.SystemTelemetry);   // first_seen_at only on insert
        try
        {
            await _col.UpdateOneAsync(ByUser(authUserId), update, new UpdateOptions { IsUpsert = true });
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Created concurrently (e.g. register racing the first event) — it exists now.
        }
    }

    public async Task RecordInteractionAsync(string authUserId, string eventType,
        IReadOnlyDictionary<string, int> tagIncrements, DateTime at)
    {
        var u = Builders<UserPsychographicProfile>.Update;
        var updates = new List<UpdateDefinition<UserPsychographicProfile>>
        {
            u.Max(LastInteraction, at),
        };

        var eventKey = ProfileInference.NormalizeKey(eventType);
        if (eventKey is not null)
            updates.Add(u.Inc($"{Agg}.interaction_click_count_by_persona_layer.{eventKey}", 1));

        foreach (var (tag, weight) in tagIncrements)
        {
            var key = ProfileInference.NormalizeKey(tag);   // defensive: never an invalid path
            if (key is null || weight == 0) continue;
            updates.Add(u.Inc($"{Agg}.tag_counts.{key}", weight));
        }

        await _col.UpdateOneAsync(ByUser(authUserId), u.Combine(updates));
    }

    public async Task ApplyVectorDeltasAsync(string authUserId, IReadOnlyDictionary<string, double> deltas)
    {
        // Pipeline update: field = min(100, max(0, ifNull(field, 0) + delta)) — atomic clamp.
        var set = new BsonDocument();
        foreach (var (vector, delta) in deltas)
        {
            if (!ProfileInference.IsVectorKey(vector) || delta == 0 || !double.IsFinite(delta)) continue;
            var path = $"personality_vector_scores.{vector}";
            set[path] = new BsonDocument("$min", new BsonArray
            {
                ProfileInference.VectorMax,
                new BsonDocument("$max", new BsonArray
                {
                    ProfileInference.VectorMin,
                    new BsonDocument("$add", new BsonArray
                    {
                        new BsonDocument("$ifNull", new BsonArray { "$" + path, 0.0 }),
                        delta
                    })
                })
            });
        }
        if (set.ElementCount == 0) return;

        var pipeline = PipelineDefinition<UserPsychographicProfile, UserPsychographicProfile>
            .Create(new[] { new BsonDocument("$set", set) });
        await _col.UpdateOneAsync(ByUser(authUserId), Builders<UserPsychographicProfile>.Update.Pipeline(pipeline));
    }

    public async Task PushSearchPhraseAsync(string authUserId, string phrase, DateTime at)
    {
        var u = Builders<UserPsychographicProfile>.Update;
        var update = u.Combine(
            u.PushEach((FieldDefinition<UserPsychographicProfile>)$"{Agg}.historical_search_phrases",
                new[] { phrase }, slice: -ProfileInference.SearchPhraseLimit),
            u.Inc($"{Agg}.interaction_click_count_by_persona_layer.{ProfileInference.EventSearch}", 1),
            u.Max(LastInteraction, at));
        await _col.UpdateOneAsync(ByUser(authUserId), update);
    }

    public async Task SetOnboardingAnswersAsync(string authUserId, OnboardingAnswers answers, DateTime at)
    {
        var u = Builders<UserPsychographicProfile>.Update;
        await _col.UpdateOneAsync(ByUser(authUserId),
            u.Combine(u.Set(p => p.ExplicitOnboardingAnswers, answers), u.Max(LastInteraction, at)));
    }

    public async Task SetDerivedFieldsAsync(string authUserId, DerivedProfileFields derived)
        => await _col.UpdateOneAsync(ByUser(authUserId), DerivedUpdate(derived));

    public static UpdateDefinition<UserPsychographicProfile> DerivedUpdate(DerivedProfileFields derived)
    {
        var u = Builders<UserPsychographicProfile>.Update;
        return u.Combine(
            u.Set($"{Agg}.top_interacted_wishlist_tags", derived.TopInteractedTags),
            u.Set($"{Agg}.conversion_probability_index", derived.ConversionProbabilityIndex),
            u.Set(p => p.InferredTravelerPrototypes, derived.Prototypes),
            u.Set(p => p.InferredDemographicMatrix, derived.Demographics));
    }
}

// ─── InteractionEventRepository ───────────────────────────────────────────────

public class InteractionEventRepository : IInteractionEventRepository
{
    public const string CollectionName = "interaction_event_stream";

    private readonly IMongoCollection<InteractionEvent> _col;

    public InteractionEventRepository(IMongoDatabase db)
        => _col = db.GetCollection<InteractionEvent>(CollectionName);

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
    public const string CollectionName = "wishlist_segmented_popularity";

    private readonly IMongoCollection<WishlistSegmentedPopularity> _col;

    public SegmentedPopularityRepository(IMongoDatabase db)
        => _col = db.GetCollection<WishlistSegmentedPopularity>(CollectionName);

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

    public async Task IncrementSegmentsAsync(string wishlistId, IReadOnlyDictionary<string, string> segmentKeys)
    {
        var u = Builders<WishlistSegmentedPopularity>.Update;
        var updates = new List<UpdateDefinition<WishlistSegmentedPopularity>> { u.CurrentDate(p => p.LastComputedAt) };
        foreach (var (field, rawKey) in segmentKeys)
        {
            if (!SegmentFields.All.Contains(field)) continue;
            var key = ProfileInference.SegmentKeyOrUnknown(rawKey);   // never an empty / invalid path
            updates.Add(u.Inc($"{SegmentFields.Root}.{field}.{key}", 1));
        }

        var filter = Builders<WishlistSegmentedPopularity>.Filter.Eq(p => p.WishlistId, wishlistId);
        await _col.UpdateOneAsync(filter, u.Combine(updates), new UpdateOptions { IsUpsert = true });
    }

    public async Task ReplaceManyAsync(IReadOnlyCollection<WishlistSegmentedPopularity> docs, CancellationToken ct = default)
    {
        if (docs.Count == 0) return;
        var models = docs.Select(d => new ReplaceOneModel<WishlistSegmentedPopularity>(
            Builders<WishlistSegmentedPopularity>.Filter.Eq(p => p.WishlistId, d.WishlistId), d) { IsUpsert = true });
        await _col.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    public async Task<long> DeleteComputedBeforeAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var filter = Builders<WishlistSegmentedPopularity>.Filter.Lt(p => p.LastComputedAt, cutoff);
        var result = await _col.DeleteManyAsync(filter, ct);
        return result.DeletedCount;
    }
}
