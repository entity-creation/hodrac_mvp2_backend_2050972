using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.BlogDtos;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services
{
    /// <summary>
    /// Shared pieces for the public and admin blog endpoints and the destination / wishlist
    /// detail "relatedPosts": visibility, slugs, reading time, projections, linked cards and
    /// embed-marker resolution.
    ///
    /// Visible = Status is Scheduled or Published and PublishedAt &lt;= now (computed at query time,
    /// so a scheduled post goes live by itself).
    /// </summary>
    public static partial class BlogQueries
    {
        public const int MaxShortDescriptionLength = 200;
        public const int MaxEmbedsPerPost = 100;
        public const int WordsPerMinute = 200;

        /// <summary>Path segments under /api/blog that are routes, so never usable as post slugs.</summary>
        public static readonly HashSet<string> ReservedSlugs = new(StringComparer.Ordinal) { "sitemap", "tags" };

        // ── Visibility ────────────────────────────────────────────────────────

        public static IQueryable<BlogPost> Visible(this IQueryable<BlogPost> query, DateTime nowUtc)
            => query.Where(p => p.Status != BlogPostStatus.Draft && p.PublishedAt != null && p.PublishedAt <= nowUtc);

        public static bool IsVisible(BlogPostStatus status, DateTime? publishedAt, DateTime nowUtc)
            => status != BlogPostStatus.Draft && publishedAt is not null && publishedAt <= nowUtc;

        /// <summary>Draft stays Draft; otherwise Published once PublishedAt has passed, else Scheduled.</summary>
        public static BlogPostStatus EffectiveStatus(BlogPostStatus status, DateTime? publishedAt, DateTime nowUtc)
            => status == BlogPostStatus.Draft ? BlogPostStatus.Draft
                : publishedAt is not null && publishedAt <= nowUtc ? BlogPostStatus.Published
                : BlogPostStatus.Scheduled;

        /// <summary>Admin status filter, by effective status.</summary>
        public static IQueryable<BlogPost> WithEffectiveStatus(this IQueryable<BlogPost> query, BlogPostStatus status, DateTime nowUtc)
            => status switch
            {
                BlogPostStatus.Draft => query.Where(p => p.Status == BlogPostStatus.Draft),
                BlogPostStatus.Published => query.Visible(nowUtc),
                _ => query.Where(p => p.Status != BlogPostStatus.Draft && (p.PublishedAt == null || p.PublishedAt > nowUtc)),
            };

        // ── Projections ───────────────────────────────────────────────────────

        public static readonly Expression<Func<BlogPost, BlogPostSummaryDto>> ToSummary = p => new BlogPostSummaryDto(
            p.Id,
            p.Title,
            p.Slug,
            p.Excerpt,
            p.CoverImageUrl,
            p.CoverImageAlt,
            p.AuthorName,
            p.PublishedAt,
            p.UpdatedAt,
            p.ReadingTimeMinutes,
            p.Tags.OrderBy(t => t.Tag.TagName).Select(t => new BlogTagDto(t.Tag.Key, t.Tag.TagName)).ToList(),
            p.City != null ? new BlogPlaceDto(p.City.CityId, p.City.CityName) : null,
            p.Country != null ? new BlogPlaceDto(p.Country.CountryId, p.Country.CountryName) : null);

        public static Expression<Func<BlogPost, AdminBlogPostSummaryDto>> ToAdminSummary(DateTime nowUtc) => p => new AdminBlogPostSummaryDto(
            p.Id,
            p.Title,
            p.Slug,
            p.Excerpt,
            p.CoverImageUrl,
            p.CoverImageAlt,
            p.AuthorName,
            p.PublishedAt,
            p.UpdatedAt,
            p.ReadingTimeMinutes,
            p.Tags.OrderBy(t => t.Tag.TagName).Select(t => new BlogTagDto(t.Tag.Key, t.Tag.TagName)).ToList(),
            p.City != null ? new BlogPlaceDto(p.City.CityId, p.City.CityName) : null,
            p.Country != null ? new BlogPlaceDto(p.Country.CountryId, p.Country.CountryName) : null,
            p.Status == BlogPostStatus.Draft ? "Draft"
                : p.PublishedAt != null && p.PublishedAt <= nowUtc ? "Published" : "Scheduled",
            p.CreatedAt,
            p.Status != BlogPostStatus.Draft && p.PublishedAt != null && p.PublishedAt <= nowUtc);

        // ── Related posts ─────────────────────────────────────────────────────

        /// <summary>Up to <paramref name="limit"/> visible posts linked to the destination, newest first.</summary>
        public static Task<List<BlogPostSummaryDto>> RelatedToDestinationAsync(
            HodracDbContext db, Guid destinationId, int limit, CancellationToken ct = default)
            => db.BlogPosts.AsNoTracking()
                .Visible(DateTime.UtcNow)
                .Where(p => p.Destinations.Any(d => d.DestinationId == destinationId))
                .OrderByDescending(p => p.PublishedAt).ThenBy(p => p.Id)
                .Take(limit)
                .Select(ToSummary)
                .ToListAsync(ct);

        /// <summary>Up to <paramref name="limit"/> visible posts linked to the wishlist, newest first.</summary>
        public static Task<List<BlogPostSummaryDto>> RelatedToWishlistAsync(
            HodracDbContext db, Guid wishlistId, int limit, CancellationToken ct = default)
            => db.BlogPosts.AsNoTracking()
                .Visible(DateTime.UtcNow)
                .Where(p => p.Wishlists.Any(w => w.WishlistId == wishlistId))
                .OrderByDescending(p => p.PublishedAt).ThenBy(p => p.Id)
                .Take(limit)
                .Select(ToSummary)
                .ToListAsync(ct);

        /// <summary>Other visible posts sharing a linked destination, wishlist or tag, newest first.</summary>
        public static async Task<List<BlogPostSummaryDto>> RelatedToPostAsync(
            HodracDbContext db, Guid postId, int limit, CancellationToken ct = default)
        {
            var links = await db.BlogPosts.AsNoTracking()
                .Where(p => p.Id == postId)
                .Select(p => new
                {
                    Destinations = p.Destinations.Select(d => d.DestinationId).ToList(),
                    Wishlists = p.Wishlists.Select(w => w.WishlistId).ToList(),
                    Tags = p.Tags.Select(t => t.TagId).ToList(),
                })
                .FirstOrDefaultAsync(ct);

            if (links is null || links.Destinations.Count + links.Wishlists.Count + links.Tags.Count == 0)
                return new List<BlogPostSummaryDto>();

            return await db.BlogPosts.AsNoTracking()
                .Visible(DateTime.UtcNow)
                .Where(p => p.Id != postId)
                .Where(p => p.Destinations.Any(d => links.Destinations.Contains(d.DestinationId))
                         || p.Wishlists.Any(w => links.Wishlists.Contains(w.WishlistId))
                         || p.Tags.Any(t => links.Tags.Contains(t.TagId)))
                .OrderByDescending(p => p.PublishedAt).ThenBy(p => p.Id)
                .Take(limit)
                .Select(ToSummary)
                .ToListAsync(ct);
        }

        // ── Linked cards ──────────────────────────────────────────────────────

        public static async Task<List<BlogDestinationCardDto>> LinkedDestinationsAsync(HodracDbContext db, Guid postId, CancellationToken ct)
        {
            var ids = await db.BlogPostDestinations.AsNoTracking()
                .Where(x => x.BlogPostId == postId)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.DestinationId)
                .ToListAsync(ct);
            var cards = await DestinationCardsAsync(db.Destinations.Where(d => ids.Contains(d.DestinationId)), ct);
            return OrderBy(ids, cards, c => c.Id);
        }

        public static async Task<List<BlogWishlistCardDto>> LinkedWishlistsAsync(HodracDbContext db, Guid postId, CancellationToken ct)
        {
            var ids = await db.BlogPostWishlists.AsNoTracking()
                .Where(x => x.BlogPostId == postId)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.WishlistId)
                .ToListAsync(ct);
            var cards = await WishlistCardsAsync(db.Wishlists.Where(w => w.IsTemplate && ids.Contains(w.WishlistId)), ct);
            return OrderBy(ids, cards, c => c.Id);
        }

        /// <summary>publishedOnly: the public view hides unpublished events; the admin view shows them.</summary>
        public static async Task<List<BlogEventCardDto>> LinkedEventsAsync(HodracDbContext db, Guid postId, bool publishedOnly, CancellationToken ct)
        {
            var ids = await db.BlogPostEvents.AsNoTracking()
                .Where(x => x.BlogPostId == postId)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.EventId)
                .ToListAsync(ct);
            var query = db.Events.Where(e => ids.Contains(e.Id));
            if (publishedOnly) query = query.Where(e => e.IsPublished);
            var cards = await EventCardsAsync(query, ct);
            return OrderBy(ids, cards, c => c.Id);
        }

        private static List<T> OrderBy<T>(List<Guid> ids, List<T> items, Func<T, Guid> key)
        {
            var byId = items.ToDictionary(key);
            return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        }

        private static async Task<List<BlogDestinationCardDto>> DestinationCardsAsync(IQueryable<Destination> query, CancellationToken ct)
        {
            var rows = await query.AsNoTracking()
                .Select(d => new
                {
                    d.DestinationId,
                    d.DestinationName,
                    City = d.DestinationCities.OrderBy(dc => dc.City.CityName).Select(dc => dc.City.CityName).FirstOrDefault(),
                    Country = d.Country.CountryName,
                    Hero = d.Images
                        .OrderBy(i => i.ImageType == "Hero" ? 0 : 1)
                        .ThenBy(i => i.DisplayOrder)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),
                    d.DescriptionJson,
                })
                .ToListAsync(ct);

            return rows.Select(r => new BlogDestinationCardDto(
                r.DestinationId,
                r.DestinationName,
                r.City,
                r.Country,
                string.IsNullOrWhiteSpace(r.Hero) ? null : r.Hero,
                Snippet(ReadOverview(r.DescriptionJson)))).ToList();
        }

        private static Task<List<BlogWishlistCardDto>> WishlistCardsAsync(IQueryable<Wishlist> query, CancellationToken ct)
            => query.AsNoTracking()
                .Select(w => new BlogWishlistCardDto(
                    w.WishlistId,
                    w.WishlistName,
                    w.TotalDays,
                    w.BasePricePerPerson,
                    w.WishlistHeroImage == "" ? null : w.WishlistHeroImage))
                .ToListAsync(ct);

        private static Task<List<BlogEventCardDto>> EventCardsAsync(IQueryable<Event> query, CancellationToken ct)
            => query.AsNoTracking()
                .Select(e => new BlogEventCardDto(
                    e.Id,
                    e.Slug,
                    e.Name,
                    e.StartsAt,
                    e.EndsAt,
                    e.TimeZone,
                    e.VenueName ?? (e.Destination != null ? e.Destination.DestinationName : null),
                    e.City.CityName,
                    e.PriceMaxKobo == 0,
                    e.ImageUrl))
                .ToListAsync(ct);

        private static string? ReadOverview(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<DescriptionJsonDto>(json)?.Overview;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Whitespace collapsed, cut at a word boundary with "…" when longer than 200 chars.</summary>
        public static string? Snippet(string? text, int max = MaxShortDescriptionLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var s = WhitespaceRegex().Replace(text, " ").Trim();
            if (s.Length <= max) return s;
            var cut = s[..max];
            var space = cut.LastIndexOf(' ');
            if (space > max / 2) cut = cut[..space];
            return cut.TrimEnd(' ', ',', ';', ':', '.', '-') + "…";
        }

        // ── Embeds ────────────────────────────────────────────────────────────

        /// <summary>{{destination:…}}, {{wishlist:…}}, {{event:…}} (type case-insensitive, spaces allowed around parts).</summary>
        [GeneratedRegex(@"\{\{\s*(destination|wishlist|event)\s*:\s*([^{}\r\n]+?)\s*\}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex MarkerRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        public readonly record struct EmbedMarker(string Marker, string Type, string Ref);

        /// <summary>Distinct markers (by exact text) in order of first appearance, capped at 100.</summary>
        public static List<EmbedMarker> ParseMarkers(string? markdown)
        {
            var result = new List<EmbedMarker>();
            if (string.IsNullOrEmpty(markdown)) return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in MarkerRegex().Matches(markdown))
            {
                if (!seen.Add(m.Value)) continue;
                result.Add(new EmbedMarker(m.Value, m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim()));
                if (result.Count >= MaxEmbedsPerPost) break;
            }
            return result;
        }

        /// <summary>
        /// Resolves every marker to a card. destination: id or name slug (matched on the normalized
        /// name); wishlist: id of a template wishlist; event: id or slug of a published event.
        /// Anything else comes back with Found = false.
        /// </summary>
        public static async Task<List<BlogEmbedDto>> ResolveEmbedsAsync(HodracDbContext db, string? markdown, CancellationToken ct = default)
        {
            var markers = ParseMarkers(markdown);
            if (markers.Count == 0) return new List<BlogEmbedDto>();

            // Destinations: by id and by normalized name.
            var destinationIds = new HashSet<Guid>();
            var destinationNames = new HashSet<string>(StringComparer.Ordinal);
            var wishlistIds = new HashSet<Guid>();
            var eventIds = new HashSet<Guid>();
            var eventSlugs = new HashSet<string>(StringComparer.Ordinal);

            foreach (var m in markers)
            {
                var isGuid = Guid.TryParse(m.Ref, out var id);
                switch (m.Type)
                {
                    case "destination":
                        if (isGuid) destinationIds.Add(id);
                        else if (DestinationSearchFields.Normalize(m.Ref) is { Length: > 0 } name) destinationNames.Add(name);
                        break;
                    case "wishlist":
                        if (isGuid) wishlistIds.Add(id);
                        break;
                    case "event":
                        if (isGuid) eventIds.Add(id);
                        else eventSlugs.Add(m.Ref.ToLowerInvariant());
                        break;
                }
            }

            var destinationsById = new Dictionary<Guid, BlogDestinationCardDto>();
            var destinationsByName = new Dictionary<string, BlogDestinationCardDto>(StringComparer.Ordinal);
            if (destinationIds.Count > 0 || destinationNames.Count > 0)
            {
                var idList = destinationIds.ToList();
                var nameList = destinationNames.ToList();
                var matches = await db.Destinations.AsNoTracking()
                    .Where(d => idList.Contains(d.DestinationId) || nameList.Contains(d.CleanNormalizedSearchName))
                    .Select(d => new { d.DestinationId, d.CleanNormalizedSearchName, d.DestinationName })
                    .ToListAsync(ct);
                var matchedIds = matches.Select(x => x.DestinationId).ToList();
                var cards = (await DestinationCardsAsync(
                        db.Destinations.Where(d => matchedIds.Contains(d.DestinationId)), ct))
                    .ToDictionary(c => c.Id);

                foreach (var match in matches.OrderBy(x => x.DestinationName, StringComparer.Ordinal).ThenBy(x => x.DestinationId))
                {
                    if (!cards.TryGetValue(match.DestinationId, out var card)) continue;
                    destinationsById[match.DestinationId] = card;
                    // First (alphabetical) wins when several destinations share a normalized name.
                    destinationsByName.TryAdd(match.CleanNormalizedSearchName, card);
                }
            }

            var wishlists = new Dictionary<Guid, BlogWishlistCardDto>();
            if (wishlistIds.Count > 0)
            {
                var idList = wishlistIds.ToList();
                wishlists = (await WishlistCardsAsync(db.Wishlists.Where(w => w.IsTemplate && idList.Contains(w.WishlistId)), ct))
                    .ToDictionary(c => c.Id);
            }

            var eventsById = new Dictionary<Guid, BlogEventCardDto>();
            var eventsBySlug = new Dictionary<string, BlogEventCardDto>(StringComparer.Ordinal);
            if (eventIds.Count > 0 || eventSlugs.Count > 0)
            {
                var idList = eventIds.ToList();
                var slugList = eventSlugs.ToList();
                var cards = await EventCardsAsync(
                    db.Events.Where(e => e.IsPublished && (idList.Contains(e.Id) || slugList.Contains(e.Slug))), ct);
                foreach (var card in cards)
                {
                    eventsById[card.Id] = card;
                    eventsBySlug[card.Slug] = card;
                }
            }

            var result = new List<BlogEmbedDto>(markers.Count);
            foreach (var m in markers)
            {
                var isGuid = Guid.TryParse(m.Ref, out var id);
                BlogDestinationCardDto? destination = null;
                BlogWishlistCardDto? wishlist = null;
                BlogEventCardDto? ev = null;

                switch (m.Type)
                {
                    case "destination":
                        if (isGuid) destinationsById.TryGetValue(id, out destination);
                        else destinationsByName.TryGetValue(DestinationSearchFields.Normalize(m.Ref), out destination);
                        break;
                    case "wishlist":
                        if (isGuid) wishlists.TryGetValue(id, out wishlist);
                        break;
                    case "event":
                        if (isGuid) eventsById.TryGetValue(id, out ev);
                        else eventsBySlug.TryGetValue(m.Ref.ToLowerInvariant(), out ev);
                        break;
                }

                result.Add(new BlogEmbedDto(
                    m.Marker, m.Type, m.Ref,
                    destination is not null || wishlist is not null || ev is not null,
                    destination, wishlist, ev));
            }
            return result;
        }

        // ── Reading time ──────────────────────────────────────────────────────

        /// <summary>Words (embed markers excluded) / 200, rounded up, minimum 1.</summary>
        public static int ReadingTimeMinutes(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return 1;
            var text = MarkerRegex().Replace(markdown, " ");
            var words = 0;
            foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (token.Any(char.IsLetterOrDigit)) words++;
            return Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerMinute));
        }

        // ── Slugs ─────────────────────────────────────────────────────────────

        [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        private static partial Regex SlugRegex();

        public static bool IsValidSlug(string slug)
            => slug.Length <= BlogPost.MaxSlugLength && SlugRegex().IsMatch(slug);

        /// <summary>"Lagos After Dark: 10 Spots" → "lagos-after-dark-10-spots". ASCII letters/digits and single hyphens.</summary>
        public static string BaseSlug(string title)
        {
            var decomposed = (title ?? string.Empty).Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            var lastHyphen = true;
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (ch is '\'' or '’') continue;
                var c = char.ToLowerInvariant(ch);
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                {
                    sb.Append(c);
                    lastHyphen = false;
                }
                else if (!lastHyphen)
                {
                    sb.Append('-');
                    lastHyphen = true;
                }
            }

            var slug = sb.ToString().Trim('-');
            if (slug.Length == 0) slug = "post";

            // Leave room for a "-N" uniqueness suffix.
            const int maxBase = BlogPost.MaxSlugLength - 10;
            if (slug.Length > maxBase) slug = slug[..maxBase].TrimEnd('-');
            return slug;
        }

        /// <summary>
        /// <paramref name="baseSlug"/> if free, else the first free "-2", "-3", …. A slug is taken when
        /// another post uses it, it is in the slug history (so old links keep redirecting) or it is reserved.
        /// </summary>
        public static async Task<string> UniqueSlugAsync(HodracDbContext db, string baseSlug, Guid? exceptPostId = null, CancellationToken ct = default)
        {
            // Slugs contain only [a-z0-9-], so no LIKE escaping is needed.
            var pattern = baseSlug + "-%";
            var taken = await db.BlogPosts
                .Where(p => p.Id != exceptPostId && (p.Slug == baseSlug || EF.Functions.Like(p.Slug, pattern)))
                .Select(p => p.Slug)
                .ToListAsync(ct);
            taken.AddRange(await db.BlogPostSlugHistory
                .Where(h => h.BlogPostId != exceptPostId && (h.OldSlug == baseSlug || EF.Functions.Like(h.OldSlug, pattern)))
                .Select(h => h.OldSlug)
                .ToListAsync(ct));

            var set = taken.ToHashSet(StringComparer.Ordinal);
            set.UnionWith(ReservedSlugs);

            if (!set.Contains(baseSlug)) return baseSlug;
            for (var n = 2; ; n++)
            {
                var candidate = $"{baseSlug}-{n}";
                if (!set.Contains(candidate)) return candidate;
            }
        }
    }
}
