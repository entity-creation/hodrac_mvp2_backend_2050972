using System.Collections.Concurrent;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.NoSql.Models;
using Hodrac_Backend_MVP2.NoSql.Repositories;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Services;

// ─── Insights rebuild / backfill ──────────────────────────────────────────────
// POST /api/admin/insights/rebuild starts one background job (in-memory state,
// one at a time). Phases:
//   0. ensure_profiles  — create a minimal Mongo profile for every Postgres user
//                         that has none (register-time creation is best-effort).
//   1. profiles         — per profile, recompute tag_counts, event-type counters,
//                         historical_search_phrases, personality vectors (from the
//                         rules) and all derived fields. Sources:
//                           • wishlist saves  → Postgres SavedWishlists (durable,
//                             all-time; recovers saves whose events were dropped)
//                           • clicks/searches → interaction_event_stream (90-day TTL)
//                           • onboarding answers → user_profile_field rules
//                         Profiles with no saves and no events in the window keep
//                         their counters; only their derived fields are recomputed
//                         (vectors are recomputed only if they were all zero).
//   2. wishlist_popularity — rebuild wishlist_segmented_popularity from Postgres
//                         SavedWishlists × each saver's profile answers/prototype
//                         (docs replaced, last_computed_at = now); docs not touched
//                         by the rebuild (wishlists with no saves) are deleted.

public sealed class InsightsRebuildJobState
{
    public string JobId { get; } = $"rebuild_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public string? RequestedBy { get; init; }

    public volatile string Status = "running";   // running | completed | failed | cancelled
    public volatile string Phase = "starting";
    public DateTime? FinishedAt;
    public string? Error;

    public int ProfilesCreated;
    public long ProfilesTotal;
    public int ProfilesProcessed;
    public int ProfilesUpdated;
    public int ProfilesFailed;
    public int EventsScanned;
    public int SavesScanned;
    public int WishlistsTotal;
    public int WishlistsProcessed;
    public int WishlistsFailed;
    public long StaleWishlistDocsDeleted;

    public bool IsRunning => Status == "running";

    public InsightsRebuildStatusDto Snapshot() => new(
        JobId, Status, Phase, StartedAt, FinishedAt, RequestedBy,
        Volatile.Read(ref ProfilesCreated),
        Interlocked.Read(ref ProfilesTotal),
        Volatile.Read(ref ProfilesProcessed),
        Volatile.Read(ref ProfilesUpdated),
        Volatile.Read(ref ProfilesFailed),
        Volatile.Read(ref EventsScanned),
        Volatile.Read(ref SavesScanned),
        Volatile.Read(ref WishlistsTotal),
        Volatile.Read(ref WishlistsProcessed),
        Volatile.Read(ref WishlistsFailed),
        Interlocked.Read(ref StaleWishlistDocsDeleted),
        Error);
}

/// <summary>Singleton: owns the in-memory job registry and runs at most one job at a time.</summary>
public sealed class InsightsRebuildJobManager
{
    private const int KeepJobs = 20;

    private readonly IServiceScopeFactory _scopes;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<InsightsRebuildJobManager> _logger;
    private readonly ConcurrentDictionary<string, InsightsRebuildJobState> _jobs = new();
    private readonly object _gate = new();
    private InsightsRebuildJobState? _current;

    public InsightsRebuildJobManager(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime,
        ILogger<InsightsRebuildJobManager> logger)
    {
        _scopes = scopes;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>Starts a job, or returns (null, running job) when one is already running.</summary>
    public (InsightsRebuildJobState? Started, InsightsRebuildJobState? Running) TryStart(string? requestedBy)
    {
        lock (_gate)
        {
            if (_current is { IsRunning: true }) return (null, _current);

            var state = new InsightsRebuildJobState { RequestedBy = requestedBy };
            _current = state;
            _jobs[state.JobId] = state;
            foreach (var old in _jobs.Values.OrderByDescending(j => j.StartedAt).Skip(KeepJobs).ToList())
                _jobs.TryRemove(old.JobId, out _);

            _ = Task.Run(() => RunAsync(state, _lifetime.ApplicationStopping));
            return (state, null);
        }
    }

    public InsightsRebuildJobState? Get(string jobId) => _jobs.TryGetValue(jobId, out var s) ? s : null;

    private async Task RunAsync(InsightsRebuildJobState state, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var rebuilder = scope.ServiceProvider.GetRequiredService<InsightsRebuilder>();
            await rebuilder.RunAsync(state, ct);
            state.Status = "completed";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            state.Status = "cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Insights rebuild {JobId} failed in phase {Phase}", state.JobId, state.Phase);
            state.Error = ex.Message;
            state.Status = "failed";
        }
        finally
        {
            state.FinishedAt = DateTime.UtcNow;
            state.Phase = "done";
            _logger.LogInformation(
                "Insights rebuild {JobId} {Status}: profiles created={Created} processed={Processed}/{Total} updated={Updated} failed={Failed}; " +
                "events={Events} saves={Saves}; wishlists processed={Wl}/{WlTotal} failed={WlFailed} staleDeleted={Stale}",
                state.JobId, state.Status, state.ProfilesCreated, state.ProfilesProcessed, state.ProfilesTotal, state.ProfilesUpdated,
                state.ProfilesFailed, state.EventsScanned, state.SavesScanned, state.WishlistsProcessed, state.WishlistsTotal,
                state.WishlistsFailed, state.StaleWishlistDocsDeleted);
        }
    }
}

/// <summary>Scoped worker that performs the rebuild (Postgres + Mongo).</summary>
public sealed class InsightsRebuilder
{
    private const int UserBatchSize = 500;
    private const int ProfileBatchSize = 200;
    private const int WishlistBatchSize = 200;

    private readonly HodracDbContext _db;
    private readonly IProfileRepository _profileRepo;
    private readonly ISegmentedPopularityRepository _popularity;
    private readonly IMongoCollection<UserPsychographicProfile> _profiles;
    private readonly IMongoCollection<InteractionEvent> _events;
    private readonly IMongoCollection<TagInferenceRule> _rules;
    private readonly ILogger<InsightsRebuilder> _logger;

    public InsightsRebuilder(HodracDbContext db, IMongoDatabase mongo, IProfileRepository profileRepo,
        ISegmentedPopularityRepository popularity, ILogger<InsightsRebuilder> logger)
    {
        _db = db;
        _profileRepo = profileRepo;
        _popularity = popularity;
        _profiles = mongo.GetCollection<UserPsychographicProfile>(ProfileRepository.CollectionName);
        _events = mongo.GetCollection<InteractionEvent>(InteractionEventRepository.CollectionName);
        _rules = mongo.GetCollection<TagInferenceRule>(TagInferenceRuleSeed.CollectionName);
        _logger = logger;
    }

    public async Task RunAsync(InsightsRebuildJobState state, CancellationToken ct)
    {
        _db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        state.Phase = "ensure_profiles";
        await EnsureProfilesAsync(state, ct);

        state.Phase = "profiles";
        var rules = await _rules.Find(FilterDefinition<TagInferenceRule>.Empty).ToListAsync(ct);
        await RebuildProfilesAsync(state, rules, ct);

        state.Phase = "wishlist_popularity";
        await RebuildPopularityAsync(state, ct);
    }

    // ── Phase 0 ───────────────────────────────────────────────────────────────

    private async Task EnsureProfilesAsync(InsightsRebuildJobState state, CancellationToken ct)
    {
        string lastId = string.Empty;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ids = await _db.Users
                .Where(u => string.Compare(u.Id, lastId) > 0)
                .OrderBy(u => u.Id)
                .Select(u => u.Id)
                .Take(UserBatchSize)
                .ToListAsync(ct);
            if (ids.Count == 0) break;
            lastId = ids[^1];

            var existing = await _profiles
                .Find(Builders<UserPsychographicProfile>.Filter.In(p => p.AuthUserReferenceId, ids))
                .Project(p => p.AuthUserReferenceId)
                .ToListAsync(ct);
            var have = existing.ToHashSet();

            foreach (var id in ids.Where(i => !have.Contains(i)))
            {
                try
                {
                    await _profileRepo.EnsureExistsAsync(id, DateTime.UtcNow);
                    Interlocked.Increment(ref state.ProfilesCreated);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Rebuild: could not create profile for {UserId}", id);
                }
            }
        }
    }

    // ── Phase 1 ───────────────────────────────────────────────────────────────

    private sealed record SaveRow(Guid UserId, Guid WishlistId, string Tags);

    private async Task RebuildProfilesAsync(InsightsRebuildJobState state, List<TagInferenceRule> rules, CancellationToken ct)
    {
        Interlocked.Exchange(ref state.ProfilesTotal,
            await _profiles.CountDocumentsAsync(FilterDefinition<UserPsychographicProfile>.Empty, cancellationToken: ct));

        var window = DateTime.UtcNow.AddDays(-90);
        var eventTypes = new[] { ProfileInference.EventDestinationClick, ProfileInference.EventSearch, ProfileInference.EventWishlistView };
        string lastId = string.Empty;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await _profiles
                .Find(Builders<UserPsychographicProfile>.Filter.Gt(p => p.Id, lastId))
                .SortBy(p => p.Id)
                .Limit(ProfileBatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;

            var userIds = batch.Select(p => p.AuthUserReferenceId).Where(id => !string.IsNullOrEmpty(id)).ToList();

            // Onboarding answers: Postgres is the source of truth; fill the profile copy from it.
            var pgAnswers = await OnboardingProfileSync.LoadAnswersAsync(_db, userIds, ct);
            foreach (var profile in batch)
                if (pgAnswers.TryGetValue(profile.AuthUserReferenceId, out var a))
                    profile.ExplicitOnboardingAnswers = OnboardingProfileSync.Merge(a, profile.ExplicitOnboardingAnswers);

            // Saves (Postgres, all-time)
            var guidByUser = new Dictionary<string, Guid>();
            foreach (var id in userIds)
                if (Guid.TryParse(id, out var g)) guidByUser[id] = g;
            var guids = guidByUser.Values.Distinct().ToList();
            var saves = guids.Count == 0
                ? new List<SaveRow>()
                : await _db.SavedWishlists
                    .Where(s => guids.Contains(s.UserId))
                    .Select(s => new SaveRow(s.UserId, s.WishlistId, s.Wishlist.PsychologicalVibeTagsJson))
                    .ToListAsync(ct);
            Interlocked.Add(ref state.SavesScanned, saves.Count);
            var savesByUser = saves.ToLookup(s => s.UserId);

            // Clicks / searches / views (Mongo, last 90 days)
            var events = await _events
                .Find(Builders<InteractionEvent>.Filter.And(
                    Builders<InteractionEvent>.Filter.In(e => e.UserId, userIds),
                    Builders<InteractionEvent>.Filter.In(e => e.EventType, eventTypes),
                    Builders<InteractionEvent>.Filter.Gte(e => e.Timestamp, window)))
                .ToListAsync(ct);
            Interlocked.Add(ref state.EventsScanned, events.Count);
            var eventsByUser = events.ToLookup(e => e.UserId);

            var models = new List<WriteModel<UserPsychographicProfile>>();
            foreach (var profile in batch)
            {
                try
                {
                    var uid = profile.AuthUserReferenceId;
                    var userSaves = guidByUser.TryGetValue(uid, out var g) ? savesByUser[g].ToList() : new List<SaveRow>();
                    var update = BuildProfileUpdate(profile, userSaves, eventsByUser[uid].ToList(), rules);
                    models.Add(new UpdateOneModel<UserPsychographicProfile>(
                        Builders<UserPsychographicProfile>.Filter.Eq(p => p.Id, profile.Id), update));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref state.ProfilesFailed);
                    _logger.LogWarning(ex, "Rebuild: failed to compute profile {ProfileId}", profile.Id);
                }
            }

            if (models.Count > 0)
            {
                try
                {
                    var result = await _profiles.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false }, ct);
                    Interlocked.Add(ref state.ProfilesUpdated, (int)result.MatchedCount);
                }
                catch (MongoBulkWriteException<UserPsychographicProfile> ex)
                {
                    Interlocked.Add(ref state.ProfilesUpdated, (int)ex.Result.MatchedCount);
                    Interlocked.Add(ref state.ProfilesFailed, ex.WriteErrors.Count);
                    _logger.LogWarning(ex, "Rebuild: {Count} profile writes failed in batch", ex.WriteErrors.Count);
                }
            }

            Interlocked.Add(ref state.ProfilesProcessed, batch.Count);
        }
    }

    /// <summary>Pure-ish: computes the $set update for one profile from its sources.</summary>
    private static UpdateDefinition<UserPsychographicProfile> BuildProfileUpdate(
        UserPsychographicProfile profile, List<SaveRow> saves, List<InteractionEvent> events, List<TagInferenceRule> rules)
    {
        var u = Builders<UserPsychographicProfile>.Update;
        var updates = new List<UpdateDefinition<UserPsychographicProfile>>();
        var agg = profile.InteractionAggregations ??= new InteractionAggregations();

        var clicks = events.Where(e => e.EventType == ProfileInference.EventDestinationClick).ToList();
        var views = events.Count(e => e.EventType == ProfileInference.EventWishlistView);
        var searches = events.Where(e => e.EventType == ProfileInference.EventSearch)
            .OrderBy(e => e.Timestamp).ToList();
        var hasData = saves.Count > 0 || events.Count > 0;

        var vectorDeltas = ProfileInference.OnboardingRuleDeltas(rules, profile.ExplicitOnboardingAnswers);
        updates.Add(u.Set(p => p.ExplicitOnboardingAnswers, profile.ExplicitOnboardingAnswers ?? new OnboardingAnswers()));

        if (hasData)
        {
            var tagCounts = new Dictionary<string, int>();
            void AddTags(IEnumerable<string> tags, int weight)
            {
                foreach (var t in ProfileInference.NormalizeTags(tags))
                    tagCounts[t] = tagCounts.TryGetValue(t, out var c) ? c + weight : weight;
            }

            foreach (var s in saves)
            {
                var tags = ProfileInference.NormalizeTags(ProfileTagSignals.ParseTagJson(s.Tags));
                AddTags(tags, ProfileInference.SaveTagWeight);
                vectorDeltas = ProfileInference.Combine(vectorDeltas,
                    ProfileInference.TagRuleDeltas(rules, ProfileInference.EventWishlistSave, tags));
            }
            foreach (var e in clicks)
            {
                var tags = ProfileInference.NormalizeTags(e.EntityTags);
                AddTags(tags, ProfileInference.ClickTagWeight);
                vectorDeltas = ProfileInference.Combine(vectorDeltas,
                    ProfileInference.TagRuleDeltas(rules, ProfileInference.EventDestinationClick, tags,
                        ProfileInference.ClickVectorMultiplier));
            }

            var layer = new Dictionary<string, int>
            {
                [ProfileInference.EventWishlistSave] = saves.Count,
                [ProfileInference.EventDestinationClick] = clicks.Count,
                [ProfileInference.EventSearch] = searches.Count,
            };
            if (views > 0) layer[ProfileInference.EventWishlistView] = views;

            agg.TagCounts = tagCounts;
            agg.InteractionClickCountByPersonaLayer = layer;
            updates.Add(u.Set(p => p.InteractionAggregations.TagCounts, tagCounts));
            updates.Add(u.Set(p => p.InteractionAggregations.InteractionClickCountByPersonaLayer, layer));

            if (searches.Count > 0)
            {
                var phrases = searches.Select(e => e.EntityId).Where(p => !string.IsNullOrWhiteSpace(p))
                    .TakeLast(ProfileInference.SearchPhraseLimit).ToList();
                agg.HistoricalSearchPhrases = phrases;
                updates.Add(u.Set(p => p.InteractionAggregations.HistoricalSearchPhrases, phrases));
            }

            var lastAt = events.Select(e => e.Timestamp)
                .Concat(new[] { profile.SystemTelemetry?.LastInteractionDeltaAt ?? DateTime.MinValue })
                .Max();
            if (lastAt > DateTime.MinValue) updates.Add(u.Max("system_telemetry.last_interaction_delta_at", lastAt));
        }

        var currentVectors = ProfileInference.VectorsToDictionary(profile.PersonalityVectorScores);
        if (hasData || currentVectors.Values.All(v => v == 0))
        {
            profile.PersonalityVectorScores = ProfileInference.VectorsFromDictionary(vectorDeltas);   // clamped 0–100
            updates.Add(u.Set(p => p.PersonalityVectorScores, profile.PersonalityVectorScores));
        }

        updates.Add(ProfileRepository.DerivedUpdate(ProfileInference.ComputeDerived(profile)));
        return u.Combine(updates);
    }

    // ── Phase 2 ───────────────────────────────────────────────────────────────

    private sealed record SaverInfo(OnboardingAnswers Answers, string? Prototype);

    private async Task RebuildPopularityAsync(InsightsRebuildJobState state, CancellationToken ct)
    {
        // App clock vs Mongo $currentDate skew tolerance for the stale-doc cleanup.
        var cutoff = DateTime.UtcNow.AddMinutes(-1);

        var wishlistIds = await _db.SavedWishlists.Select(s => s.WishlistId).Distinct().ToListAsync(ct);
        state.WishlistsTotal = wishlistIds.Count;

        foreach (var chunk in wishlistIds.Chunk(WishlistBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var ids = chunk.ToList();
                var saves = await _db.SavedWishlists
                    .Where(s => ids.Contains(s.WishlistId))
                    .Select(s => new { s.WishlistId, s.UserId })
                    .ToListAsync(ct);

                var userIds = saves.Select(s => s.UserId.ToString()).Distinct().ToList();
                var savers = (await _profiles
                        .Find(Builders<UserPsychographicProfile>.Filter.In(p => p.AuthUserReferenceId, userIds))
                        .Project(p => new { p.AuthUserReferenceId, p.ExplicitOnboardingAnswers, p.InferredTravelerPrototypes })
                        .ToListAsync(ct))
                    .Where(p => Guid.TryParse(p.AuthUserReferenceId, out _))
                    .GroupBy(p => Guid.Parse(p.AuthUserReferenceId))
                    .ToDictionary(g => g.Key, g => new SaverInfo(
                        g.First().ExplicitOnboardingAnswers ?? new OnboardingAnswers(),
                        g.First().InferredTravelerPrototypes?.PrimaryType));

                var now = DateTime.UtcNow;
                var docs = saves.GroupBy(s => s.WishlistId).Select(g =>
                {
                    var seg = new SegmentPopularityScores();
                    foreach (var s in g)
                    {
                        var info = savers.TryGetValue(s.UserId, out var i) ? i : new SaverInfo(new OnboardingAnswers(), null);
                        Inc(seg.ExplicitTravelerTypes, ProfileInference.TravelerTypeKey(info.Answers.WhoTheyTravelWith));
                        Inc(seg.BudgetTiers, ProfileInference.BudgetTierKey(info.Answers.BudgetProfile));
                        Inc(seg.PrimaryPriorities, ProfileInference.PriorityKey(info.Answers.PrimaryPriority));
                        Inc(seg.PersonaVectorClusters, ProfileInference.PersonaClusterKey(info.Prototype));
                    }
                    // Guid.ToString() ("D") — the form PopularWishlistService and the live handler use.
                    return new WishlistSegmentedPopularity
                    {
                        WishlistId = g.Key.ToString(),
                        SegmentPopularityScores = seg,
                        LastComputedAt = now,
                    };
                }).ToList();

                await _popularity.ReplaceManyAsync(docs, ct);
                Interlocked.Add(ref state.WishlistsProcessed, chunk.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Add(ref state.WishlistsFailed, chunk.Length);
                _logger.LogWarning(ex, "Rebuild: wishlist popularity batch of {Count} failed", chunk.Length);
            }
        }

        // Only clean up when every batch succeeded, so a partial failure never wipes counters.
        if (state.WishlistsFailed == 0)
            Interlocked.Exchange(ref state.StaleWishlistDocsDeleted, await _popularity.DeleteComputedBeforeAsync(cutoff, ct));
    }

    private static void Inc(Dictionary<string, int> d, string key)
        => d[key] = d.TryGetValue(key, out var c) ? c + 1 : 1;
}
