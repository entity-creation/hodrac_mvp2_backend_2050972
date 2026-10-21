using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.NoSql.Models;
using Hodrac_Backend_MVP2.NoSql.Repositories;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Services;

// ─── Admin insights (aggregate-only) ──────────────────────────────────────────
// Read side for AdminInsightsController. All profile statistics are computed with
// Mongo aggregation pipelines ($match + $facet). Privacy: buckets smaller than
// MinBucketSize are folded into "other", tags used by fewer than MinBucketSize
// users are folded into "other", and vector averages are withheld for audiences
// smaller than MinBucketSize. No per-user fields are ever returned.

public enum InsightSegmentType { TravelerType, Budget, Priority, Prototype, Income }

public class AdminInsightsService
{
    public const int MinBucketSize = 3;
    public const int EventWindowDays = 90;
    private const int MaxTagsScanned = 2000;
    private static readonly TimeSpan QueryMaxTime = TimeSpan.FromSeconds(20);

    private const string Answers = "explicit_onboarding_answers";
    private const string WhoField = Answers + ".who_they_travel_with";
    private const string TripField = Answers + ".trip_type_preference";
    private const string BudgetField = Answers + ".budget_profile";
    private const string PriorityField = Answers + ".primary_priority";
    private const string PrototypeField = "inferred_traveler_prototypes.primary_type";
    private const string IncomeField = "inferred_demographic_matrix.calculated_income_bracket";
    private const string TagCountsField = "interaction_aggregations.tag_counts";

    private readonly IMongoCollection<BsonDocument> _profiles;
    private readonly IMongoCollection<BsonDocument> _popularity;
    private readonly IMongoCollection<InteractionEvent> _events;
    private readonly HodracDbContext _db;

    public AdminInsightsService(IMongoDatabase mongo, HodracDbContext db)
    {
        _profiles = mongo.GetCollection<BsonDocument>(ProfileRepository.CollectionName);
        _popularity = mongo.GetCollection<BsonDocument>(SegmentedPopularityRepository.CollectionName);
        _events = mongo.GetCollection<InteractionEvent>(InteractionEventRepository.CollectionName);
        _db = db;
    }

    public static bool TryParseSegmentType(string? raw, out InsightSegmentType type)
    {
        type = default;
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "travelertype": case "traveler_type": case "who": type = InsightSegmentType.TravelerType; return true;
            case "budget": type = InsightSegmentType.Budget; return true;
            case "priority": type = InsightSegmentType.Priority; return true;
            case "prototype": type = InsightSegmentType.Prototype; return true;
            case "income": type = InsightSegmentType.Income; return true;
            default: return false;
        }
    }

    // ── Endpoints ─────────────────────────────────────────────────────────────

    public async Task<AdminAudienceInsightsDto> GetAudienceAsync(CancellationToken ct)
        => new(DateTime.UtcNow, MinBucketSize, await ComputeAudienceAsync(new BsonDocument(), 20, ct));

    /// <summary>Null when the wishlist does not exist.</summary>
    public async Task<AdminWishlistAudienceDto?> GetWishlistAudienceAsync(Guid wishlistId, CancellationToken ct)
    {
        var wishlist = await _db.Wishlists.AsNoTracking()
            .Where(w => w.WishlistId == wishlistId)
            .Select(w => new { w.WishlistId, w.WishlistName, w.TotalGlobalSaveCount })
            .FirstOrDefaultAsync(ct);
        if (wishlist is null) return null;

        var saverIds = await _db.SavedWishlists.AsNoTracking()
            .Where(s => s.WishlistId == wishlistId)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);

        var doc = await _popularity
            .Find(new BsonDocument("_id", wishlistId.ToString()))
            .FirstOrDefaultAsync(ct);

        WishlistSegmentCountersDto? counters = null;
        if (doc is not null)
        {
            var seg = doc.GetValue(SegmentFields.Root, new BsonDocument()).AsBsonDocument;
            List<InsightBucketDto> Dict(string field) => Suppress(
                seg.GetValue(field, new BsonDocument()).AsBsonDocument.Elements
                    .Select(e => ((string?)e.Name, ToInt(e.Value))));
            counters = new WishlistSegmentCountersDto(
                doc.TryGetValue("last_computed_at", out var lc) && lc.IsValidDateTime ? lc.ToUniversalTime() : null,
                Dict(SegmentFields.TravelerTypes),
                Dict(SegmentFields.BudgetTiers),
                Dict(SegmentFields.Priorities),
                Dict(SegmentFields.PersonaClusters));
        }

        var savers = await ComputeAudienceAsync(UserIdFilter(saverIds.Select(g => g.ToString())), 20, ct);
        return new AdminWishlistAudienceDto(wishlist.WishlistId, wishlist.WishlistName, wishlist.TotalGlobalSaveCount,
            saverIds.Count, MinBucketSize, counters, savers);
    }

    public async Task<AdminSegmentWishlistsDto> GetTopWishlistsForSegmentAsync(
        InsightSegmentType type, string segment, int limit, CancellationToken ct)
    {
        var (field, key) = type switch
        {
            InsightSegmentType.TravelerType => (SegmentFields.TravelerTypes, ProfileInference.TravelerTypeKey(segment)),
            InsightSegmentType.Budget => (SegmentFields.BudgetTiers, ProfileInference.BudgetTierKey(segment)),
            InsightSegmentType.Priority => (SegmentFields.Priorities, ProfileInference.PriorityKey(segment)),
            InsightSegmentType.Prototype => (SegmentFields.PersonaClusters, PrototypeClusterKey(segment)),
            _ => throw new ArgumentException("Segment type not supported for wishlists.", nameof(type)),
        };
        var path = $"{SegmentFields.Root}.{field}.{key}";

        var top = await _popularity
            .Find(new BsonDocument(path, new BsonDocument("$gte", MinBucketSize)))
            .Sort(new BsonDocument(path, -1))
            .Limit(limit)
            .Project(new BsonDocument { { "_id", 1 }, { path, 1 } })
            .ToListAsync(ct);

        var suppressedAgg = await (await _popularity.AggregateAsync<BsonDocument>(
            PipelineDefinition<BsonDocument, BsonDocument>.Create(new[]
            {
                new BsonDocument("$match", new BsonDocument(path, new BsonDocument { { "$gt", 0 }, { "$lt", MinBucketSize } })),
                new BsonDocument("$group", new BsonDocument { { "_id", BsonNull.Value }, { "s", new BsonDocument("$sum", "$" + path) } }),
            }), new AggregateOptions { MaxTime = QueryMaxTime }, ct)).FirstOrDefaultAsync(ct);
        var suppressed = suppressedAgg is null ? 0 : ToInt(suppressedAgg["s"]);

        var counts = new List<(Guid Id, int Count)>();
        foreach (var d in top)
        {
            if (!Guid.TryParse(d["_id"].ToString(), out var id)) continue;
            counts.Add((id, ToInt(GetPath(d, path))));
        }

        var ids = counts.Select(c => c.Id).ToList();
        var names = await _db.Wishlists.AsNoTracking()
            .Where(w => ids.Contains(w.WishlistId))
            .Select(w => new { w.WishlistId, w.WishlistName, w.TotalGlobalSaveCount })
            .ToDictionaryAsync(w => w.WishlistId, ct);

        var items = counts
            .Where(c => names.ContainsKey(c.Id))   // skip docs for deleted wishlists
            .Select(c => new SegmentWishlistDto(c.Id, names[c.Id].WishlistName, c.Count, names[c.Id].TotalGlobalSaveCount))
            .ToList();

        return new AdminSegmentWishlistsDto(TypeName(type), segment, key, MinBucketSize, items, suppressed);
    }

    /// <summary>Null when the destination does not exist.</summary>
    public async Task<AdminDestinationAudienceDto?> GetDestinationAudienceAsync(Guid destinationId, CancellationToken ct)
    {
        var dest = await _db.Destinations.AsNoTracking()
            .Where(d => d.DestinationId == destinationId)
            .Select(d => new { d.DestinationId, d.DestinationName })
            .FirstOrDefaultAsync(ct);
        if (dest is null) return null;

        var f = Builders<InteractionEvent>.Filter;
        var filter = f.And(
            f.Eq(e => e.EventType, ProfileInference.EventDestinationClick),
            f.Eq(e => e.EntityId, destinationId.ToString()),
            f.Gte(e => e.Timestamp, DateTime.UtcNow.AddDays(-EventWindowDays)));

        var clicks = await _events.CountDocumentsAsync(filter, new CountOptions { MaxTime = QueryMaxTime }, ct);
        var users = await (await _events.DistinctAsync(e => e.UserId, filter, cancellationToken: ct)).ToListAsync(ct);

        var audience = await ComputeAudienceAsync(UserIdFilter(users), 20, ct);
        return new AdminDestinationAudienceDto(dest.DestinationId, dest.DestinationName, EventWindowDays,
            clicks, users.Count, MinBucketSize, audience);
    }

    public async Task<AdminTagInsightsDto> GetTagsAsync(InsightSegmentType? type, string? segment, int limit, CancellationToken ct)
    {
        var match = type is null || string.IsNullOrWhiteSpace(segment)
            ? new BsonDocument()
            : await SegmentProfileFilterAsync(type.Value, segment!, ct);

        var facet = await RunFacetAsync(match, new BsonDocument
        {
            { "total", new BsonArray { new BsonDocument("$count", "n") } },
            { "tags", TagFacet() },
        }, ct);

        return new AdminTagInsightsDto(type is null ? null : TypeName(type.Value), segment,
            FacetCount(facet, "total"), MinBucketSize, ReadTags(facet, limit));
    }

    // ── Aggregation core ──────────────────────────────────────────────────────

    private async Task<AudienceBreakdownDto> ComputeAudienceAsync(BsonDocument match, int tagLimit, CancellationToken ct)
    {
        static BsonArray GroupBy(string field) => new()
        {
            new BsonDocument("$group", new BsonDocument { { "_id", "$" + field }, { "n", new BsonDocument("$sum", 1) } }),
        };

        var answered = new BsonArray(new[] { WhoField, TripField, BudgetField, PriorityField }
            .Select(fld => new BsonDocument(fld, new BsonDocument("$nin", new BsonArray { "", BsonNull.Value }))));

        var vectorGroup = new BsonDocument("_id", BsonNull.Value);
        foreach (var k in ProfileInference.VectorKeys)
            vectorGroup[k] = new BsonDocument("$avg", "$personality_vector_scores." + k);

        var facet = await RunFacetAsync(match, new BsonDocument
        {
            { "total", new BsonArray { new BsonDocument("$count", "n") } },
            { "onboarded", new BsonArray { new BsonDocument("$match", new BsonDocument("$or", answered)), new BsonDocument("$count", "n") } },
            { "who", GroupBy(WhoField) },
            { "trip", GroupBy(TripField) },
            { "budget", GroupBy(BudgetField) },
            { "priority", GroupBy(PriorityField) },
            { "prototype", GroupBy(PrototypeField) },
            { "income", GroupBy(IncomeField) },
            { "vectors", new BsonArray { new BsonDocument("$group", vectorGroup) } },
            { "tags", TagFacet() },
        }, ct);

        var total = FacetCount(facet, "total");
        InsightVectorAveragesDto? vectors = null;
        if (total >= MinBucketSize && facet.GetValue("vectors", new BsonArray()).AsBsonArray.FirstOrDefault() is BsonDocument v)
        {
            double Avg(string k) => v.TryGetValue(k, out var x) && x.IsNumeric ? Math.Round(x.ToDouble(), 2) : 0;
            vectors = new InsightVectorAveragesDto(
                Avg("adventure_seeker"), Avg("social_butterfly"), Avg("planner_vs_spontaneous"),
                Avg("cultural_immersion_index"), Avg("teen_validation_priority"), Avg("openness_to_experience"),
                Avg("conscientiousness"), Avg("extraversion"));
        }

        return new AudienceBreakdownDto(
            total,
            FacetCount(facet, "onboarded"),
            ReadBuckets(facet, "who"),
            ReadBuckets(facet, "trip"),
            ReadBuckets(facet, "budget"),
            ReadBuckets(facet, "priority"),
            ReadBuckets(facet, "prototype"),
            ReadBuckets(facet, "income"),
            ReadTags(facet, tagLimit),
            vectors);
    }

    private static BsonArray TagFacet() => new()
    {
        new BsonDocument("$project", new BsonDocument("t",
            new BsonDocument("$objectToArray", new BsonDocument("$ifNull", new BsonArray { "$" + TagCountsField, new BsonDocument() })))),
        new BsonDocument("$unwind", "$t"),
        new BsonDocument("$group", new BsonDocument
        {
            { "_id", "$t.k" },
            { "total", new BsonDocument("$sum", "$t.v") },
            { "users", new BsonDocument("$sum", 1) },
        }),
        new BsonDocument("$sort", new BsonDocument { { "total", -1 }, { "_id", 1 } }),
        new BsonDocument("$limit", MaxTagsScanned),
    };

    private async Task<BsonDocument> RunFacetAsync(BsonDocument match, BsonDocument facets, CancellationToken ct)
    {
        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(new[]
        {
            new BsonDocument("$match", match),
            new BsonDocument("$facet", facets),
        });
        var cursor = await _profiles.AggregateAsync(pipeline, new AggregateOptions { MaxTime = QueryMaxTime }, ct);
        return await cursor.FirstOrDefaultAsync(ct) ?? new BsonDocument();
    }

    private static BsonDocument UserIdFilter(IEnumerable<string> userIds)
        => new("auth_user_reference_id", new BsonDocument("$in", new BsonArray(userIds.Distinct())));

    /// <summary>
    /// Profile filter for a segment. Answers are stored as free text, so the field's
    /// distinct raw values are mapped through the same key function used for the
    /// segment counters and every raw value with the requested key is matched.
    /// </summary>
    private async Task<BsonDocument> SegmentProfileFilterAsync(InsightSegmentType type, string segment, CancellationToken ct)
    {
        Func<string?, string> keyFn = type switch
        {
            InsightSegmentType.TravelerType => ProfileInference.TravelerTypeKey,
            InsightSegmentType.Budget => ProfileInference.BudgetTierKey,
            InsightSegmentType.Priority => ProfileInference.PriorityKey,
            _ => ProfileInference.SegmentKeyOrUnknown,
        };
        var field = type switch
        {
            InsightSegmentType.TravelerType => WhoField,
            InsightSegmentType.Budget => BudgetField,
            InsightSegmentType.Priority => PriorityField,
            InsightSegmentType.Prototype => PrototypeField,
            _ => IncomeField,
        };

        var wanted = keyFn(segment);
        var raws = await (await _profiles.DistinctAsync<BsonValue>(field, new BsonDocument(), cancellationToken: ct)).ToListAsync(ct);
        var matching = new BsonArray(raws.Where(r => r.IsString && keyFn(r.AsString) == wanted));

        var or = new BsonArray { new BsonDocument(field, new BsonDocument("$in", matching)) };
        if (wanted == ProfileInference.Unknown)
        {
            or.Add(new BsonDocument(field, BsonNull.Value));   // also matches a missing field
            or.Add(new BsonDocument(field, ""));
        }
        return new BsonDocument("$or", or);
    }

    // ── Result shaping + suppression ──────────────────────────────────────────

    private static int FacetCount(BsonDocument facet, string name)
        => facet.GetValue(name, new BsonArray()).AsBsonArray.FirstOrDefault() is BsonDocument d && d.TryGetValue("n", out var n)
            ? ToInt(n)
            : 0;

    private static List<InsightBucketDto> ReadBuckets(BsonDocument facet, string name)
        => Suppress(facet.GetValue(name, new BsonArray()).AsBsonArray
            .OfType<BsonDocument>()
            .Select(d => (Key: d.GetValue("_id", BsonNull.Value) is { IsString: true } s ? s.AsString : null, Count: ToInt(d["n"]))));

    /// <summary>
    /// Merges buckets case-insensitively (empty/null → "unknown"), folds every bucket
    /// with fewer than MinBucketSize members into "other", sorts by count ("other" last).
    /// </summary>
    public static List<InsightBucketDto> Suppress(IEnumerable<(string? Key, int Count)> raw)
    {
        var merged = new Dictionary<string, (string Display, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, count) in raw)
        {
            var k = string.IsNullOrWhiteSpace(key) ? ProfileInference.Unknown : key.Trim();
            merged[k] = merged.TryGetValue(k, out var cur) ? (cur.Display, cur.Count + count) : (k, count);
        }

        var kept = merged.Values.Where(b => b.Count >= MinBucketSize && !b.Display.Equals("other", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Count).ThenBy(b => b.Display, StringComparer.Ordinal)
            .Select(b => new InsightBucketDto(b.Display, b.Count))
            .ToList();
        var other = merged.Values.Where(b => b.Count < MinBucketSize || b.Display.Equals("other", StringComparison.OrdinalIgnoreCase))
            .Sum(b => b.Count);
        if (other > 0) kept.Add(new InsightBucketDto("other", other));
        return kept;
    }

    /// <summary>
    /// Top tags used by at least MinBucketSize users; the rest are folded into one
    /// "other" entry (its Users value is a sum and may count a user more than once).
    /// </summary>
    private static List<InsightTagDto> ReadTags(BsonDocument facet, int limit)
    {
        var rows = facet.GetValue("tags", new BsonArray()).AsBsonArray.OfType<BsonDocument>()
            .Select(d => new InsightTagDto(d["_id"].ToString() ?? string.Empty, ToLong(d["total"]), ToInt(d["users"])))
            .ToList();

        var visible = rows.Where(r => r.Users >= MinBucketSize).Take(limit).ToList();
        var hidden = rows.Where(r => r.Users < MinBucketSize).ToList();
        if (hidden.Count > 0)
            visible.Add(new InsightTagDto("other", hidden.Sum(h => h.Interactions), hidden.Sum(h => h.Users)));
        return visible;
    }

    private static string PrototypeClusterKey(string segment)
    {
        var s = segment.Trim();
        if (s.StartsWith("cluster_", StringComparison.OrdinalIgnoreCase)) return ProfileInference.SegmentKeyOrUnknown(s);
        var name = ProfileInference.PrototypeNames.FirstOrDefault(n => n.Equals(s, StringComparison.OrdinalIgnoreCase)
                                                                    || ProfileInference.NormalizeKey(n) == ProfileInference.NormalizeKey(s));
        return ProfileInference.PersonaClusterKey(name ?? s);
    }

    private static string TypeName(InsightSegmentType t) => t switch
    {
        InsightSegmentType.TravelerType => "travelerType",
        InsightSegmentType.Budget => "budget",
        InsightSegmentType.Priority => "priority",
        InsightSegmentType.Prototype => "prototype",
        _ => "income",
    };

    private static BsonValue GetPath(BsonDocument doc, string dottedPath)
    {
        BsonValue cur = doc;
        foreach (var part in dottedPath.Split('.'))
        {
            if (cur is not BsonDocument d || !d.TryGetValue(part, out cur)) return BsonNull.Value;
        }
        return cur;
    }

    private static int ToInt(BsonValue v) => v.IsNumeric ? (int)Math.Min(int.MaxValue, v.ToInt64()) : 0;
    private static long ToLong(BsonValue v) => v.IsNumeric ? v.ToInt64() : 0;
}
