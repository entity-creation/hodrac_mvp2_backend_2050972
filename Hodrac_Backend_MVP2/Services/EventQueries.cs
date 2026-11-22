using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.EventDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services
{
    /// <summary>
    /// Shared pieces for the public and admin event endpoints and the events seeder:
    /// slug generation, the "upcoming" rule and EF projections to the DTOs.
    /// </summary>
    public static class EventQueries
    {
        /// <summary>Upcoming = not finished yet: (EndsAt ?? StartsAt) >= now.</summary>
        public static IQueryable<Event> Upcoming(this IQueryable<Event> query, DateTime nowUtc)
            => query.Where(e => (e.EndsAt ?? e.StartsAt) >= nowUtc);

        public static IQueryable<Event> Published(this IQueryable<Event> query)
            => query.Where(e => e.IsPublished);

        public static readonly Expression<Func<Event, EventDto>> ToDto = e => new EventDto(
            e.Id,
            e.Name,
            e.Slug,
            e.Description,
            e.Organiser,
            e.StartsAt,
            e.EndsAt,
            e.TimeZone,
            new EventVenueDto(
                e.DestinationId,
                e.Destination != null ? e.Destination.DestinationName : null,
                e.VenueName ?? (e.Destination != null ? e.Destination.DestinationName : null),
                e.VenueAddress),
            new EventCityDto(e.CityId, e.City.CityName),
            e.TicketUrl,
            e.PriceMinKobo,
            e.PriceMaxKobo,
            e.Currency,
            e.PriceNote,
            e.PriceMaxKobo == 0,
            e.InstagramHandle,
            e.WebsiteUrl,
            e.ImageUrl);

        public static readonly Expression<Func<Event, EventSummaryDto>> ToSummary = e => new EventSummaryDto(
            e.Id,
            e.Name,
            e.Slug,
            e.StartsAt,
            e.EndsAt,
            e.PriceMaxKobo == 0,
            e.PriceMinKobo);

        public static Expression<Func<Event, AdminEventDto>> ToAdminDto(DateTime nowUtc) => e => new AdminEventDto(
            e.Id,
            e.Name,
            e.Slug,
            e.Description,
            e.Organiser,
            e.StartsAt,
            e.EndsAt,
            e.TimeZone,
            new EventVenueDto(
                e.DestinationId,
                e.Destination != null ? e.Destination.DestinationName : null,
                e.VenueName ?? (e.Destination != null ? e.Destination.DestinationName : null),
                e.VenueAddress),
            new EventCityDto(e.CityId, e.City.CityName),
            e.TicketUrl,
            e.PriceMinKobo,
            e.PriceMaxKobo,
            e.Currency,
            e.PriceNote,
            e.PriceMaxKobo == 0,
            e.InstagramHandle,
            e.WebsiteUrl,
            e.ImageUrl,
            e.IsPublished,
            (e.EndsAt ?? e.StartsAt) >= nowUtc,
            e.CreatedAt,
            e.UpdatedAt);

        /// <summary>
        /// Up to <paramref name="limit"/> published, upcoming events at a destination, soonest first.
        /// </summary>
        public static Task<List<EventSummaryDto>> UpcomingAtDestinationAsync(
            HodracDbContext db, Guid destinationId, int limit, CancellationToken ct = default)
            => db.Events
                .AsNoTracking()
                .Where(e => e.DestinationId == destinationId)
                .Published()
                .Upcoming(DateTime.UtcNow)
                .OrderBy(e => e.StartsAt)
                .ThenBy(e => e.Id)
                .Take(limit)
                .Select(ToSummary)
                .ToListAsync(ct);

        // ── Slugs ─────────────────────────────────────────────────────────────

        /// <summary>
        /// "Oga Bole Festival 2026" + 2026 → "oga-bole-festival-2026"; "Fungasm" + 2026 → "fungasm-2026".
        /// Lowercase ASCII letters/digits and single hyphens; the start year is appended unless
        /// the name already ends with it.
        /// </summary>
        public static string BaseSlug(string name, DateTime startsAtUtc)
        {
            var decomposed = (name ?? string.Empty).Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            var lastHyphen = true;
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
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
            if (slug.Length == 0) slug = "event";

            var year = startsAtUtc.Year.ToString(CultureInfo.InvariantCulture);
            if (slug != year && !slug.EndsWith("-" + year, StringComparison.Ordinal))
                slug = $"{slug}-{year}";

            // Leave room for a "-N" uniqueness suffix.
            const int maxBase = Event.MaxSlugLength - 10;
            if (slug.Length > maxBase) slug = slug[..maxBase].TrimEnd('-');
            return slug;
        }

        /// <summary><paramref name="baseSlug"/> if free, else the first free "-2", "-3", ….</summary>
        public static async Task<string> UniqueSlugAsync(HodracDbContext db, string baseSlug, CancellationToken ct = default)
        {
            // Slugs contain only [a-z0-9-], so no LIKE escaping is needed.
            var taken = await db.Events
                .Where(e => e.Slug == baseSlug || EF.Functions.Like(e.Slug, baseSlug + "-%"))
                .Select(e => e.Slug)
                .ToListAsync(ct);

            if (!taken.Contains(baseSlug)) return baseSlug;

            var set = taken.ToHashSet(StringComparer.Ordinal);
            for (var n = 2; ; n++)
            {
                var candidate = $"{baseSlug}-{n}";
                if (!set.Contains(candidate)) return candidate;
            }
        }
    }
}
