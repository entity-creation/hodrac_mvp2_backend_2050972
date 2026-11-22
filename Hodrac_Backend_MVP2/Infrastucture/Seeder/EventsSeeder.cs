using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// Benin City events. Idempotent by Slug: an event whose slug already exists is left untouched,
    /// so running it any number of times converges on the same rows (and never overwrites admin edits).
    ///
    /// Needs the "Benin City" city (AfricaPocSeeder). Events held at Image Garden link to that
    /// destination when it exists (DestinationsSeeder); otherwise they are seeded with a free-text
    /// VenueName/VenueAddress and a warning is logged, and a later run (once Image Garden exists)
    /// links those rows to the destination.
    ///
    /// Times are given in WAT (UTC+1, no DST) and stored as UTC.
    /// </summary>
    public static class EventsSeeder
    {
        public const string CityName = "Benin City";
        public const string ImageGardenName = "Image Garden";
        public const string ImageGardenAddress = "6 Boundary Road, GRA, Benin City";

        private static readonly TimeSpan Wat = TimeSpan.FromHours(1);

        private static DateTime WatToUtc(int year, int month, int day, int hour, int minute = 0)
            => new DateTimeOffset(year, month, day, hour, minute, 0, Wat).UtcDateTime;

        /// <param name="AtImageGarden">Venue is Image Garden: linked to the destination when found.</param>
        private sealed record EventSpec(
            string Name,
            DateTime StartsAt,
            DateTime? EndsAt,
            bool AtImageGarden,
            string? VenueName,
            string? VenueAddress,
            string Organiser,
            long? PriceMinKobo,
            long? PriceMaxKobo,
            string? PriceNote,
            string InstagramHandle,
            string? TicketUrl,
            string? WebsiteUrl,
            string Description);

        private static readonly EventSpec[] Specs =
        {
            // Sources: Threads @ogabole ("13 days to go", 15 Nov 2026); vibescalendar.org listing.
            new(
                Name: "Oga Bole Festival 2026",
                StartsAt: WatToUtc(2026, 11, 28, 12),
                EndsAt: WatToUtc(2026, 11, 28, 16),
                AtImageGarden: true,
                VenueName: null,
                VenueAddress: ImageGardenAddress,
                Organiser: "Oga Bole",
                PriceMinKobo: 0,
                PriceMaxKobo: 0,
                PriceNote: "Free general entry; sponsorship enquiries 0811 181 9404",
                InstagramHandle: "ogabolefestival",
                TicketUrl: null,
                WebsiteUrl: null,
                Description:
                    "Oga Bole Festival is a yearly street-food festival in Benin City celebrating bole — roasted plantain, " +
                    "often served with fish and pepper sauce — together with Edo food, music and games. It is run by the " +
                    "Oga Bole street-food brand. The 2025 edition was held on 23 August 2025."),

            new(
                Name: "Fungasm",
                StartsAt: WatToUtc(2026, 12, 6, 12),
                EndsAt: null,
                AtImageGarden: false,
                VenueName: "Ogbe Primary School",
                VenueAddress: "Off Airport Road, opposite the Vegetable Market, Benin City",
                Organiser: "Fungasm Global",
                PriceMinKobo: null,
                PriceMaxKobo: null,
                PriceNote: "2026 prices not announced; the Dec 2025 edition was ₦5,000–₦8,000",
                InstagramHandle: "fungasmbenin",
                TicketUrl: "https://www.pevent.co/fungasmbenin",
                WebsiteUrl: null,
                Description:
                    "Fungasm is a daytime rave and lifestyle party brand that runs Benin City editions roughly twice a year, " +
                    "around April/May and in December. Starts from 12 noon; end time not announced."),

            new(
                Name: "Metro Events — Benin 3 Days Detty December Rave",
                StartsAt: WatToUtc(2026, 12, 18, 12),
                EndsAt: WatToUtc(2026, 12, 20, 23, 59),
                AtImageGarden: true,
                VenueName: null,
                VenueAddress: ImageGardenAddress,
                Organiser: "Metro Events",
                PriceMinKobo: null,
                PriceMaxKobo: null,
                PriceNote: "Ticket prices not announced",
                InstagramHandle: "officialmetroevents",
                TicketUrl: null,
                WebsiteUrl: null,
                Description:
                    "A three-day Detty December rave in Benin City from 18 to 20 December 2026, run by Metro Events. " +
                    "Venue reported as Image Garden — confirm. Daily start and end times are unconfirmed; check " +
                    "@officialmetroevents before you go."),

            // Past edition (15 Nov 2026), kept for history: never shows as upcoming.
            new(
                Name: "Rave Experience — Playtopia (Benin)",
                StartsAt: WatToUtc(2026, 11, 15, 12),
                EndsAt: null,
                AtImageGarden: true,
                VenueName: null,
                VenueAddress: ImageGardenAddress,
                Organiser: "Rave Experience",
                PriceMinKobo: 400_000,
                PriceMaxKobo: 750_000,
                PriceNote: "Singles ₦4,000–₦7,500; group of 5 ₦33,800; VIP/VVIP tables",
                InstagramHandle: "raveexperience",
                TicketUrl: null,
                WebsiteUrl: "https://www.ravexperience.com",
                Description:
                    "Rave Experience (\"Recreational Adult Vibing Event\") is a touring daytime party that runs editions " +
                    "across Warri, Benin, Uyo, Port Harcourt and Asaba. Playtopia was its Benin City edition."),
        };

        /// <summary>Slugs of every event this seeder owns.</summary>
        public static IReadOnlyList<string> Slugs =>
            Specs.Select(e => EventQueries.BaseSlug(e.Name, e.StartsAt)).ToList();

        /// <returns>Number of events created (0 on a re-run).</returns>
        public static async Task<int> SeedAsync(HodracDbContext db, ILogger? logger = null, CancellationToken ct = default)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var ownsTransaction = db.Database.CurrentTransaction == null;
                await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

                var created = await SeedCoreAsync(db, logger, ct);

                if (tx != null) await tx.CommitAsync(ct);
                return created;
            });
        }

        private static async Task<int> SeedCoreAsync(HodracDbContext db, ILogger? logger, CancellationToken ct)
        {
            var cityId = await db.Cities
                .Where(c => c.CityName == CityName)
                .OrderBy(c => c.CityId)
                .Select(c => (Guid?)c.CityId)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException(
                    $"City '{CityName}' not found. Seed it first (AfricaPocSeeder / --seed-destinations).");

            var imageGardenKey = ImageGardenName.ToLower();
            var imageGarden = await db.Destinations
                .Where(d => d.DestinationName.ToLower() == imageGardenKey)
                .OrderBy(d => d.DestinationId)
                .Select(d => new { d.DestinationId, d.DestinationName })
                .FirstOrDefaultAsync(ct);

            if (imageGarden is null)
                logger?.LogWarning(
                    "Destination '{Name}' not found: Image Garden events are seeded with VenueName/VenueAddress only.",
                    ImageGardenName);
            else
                logger?.LogInformation("Linking Image Garden events to destination {DestinationId}.", imageGarden.DestinationId);

            var slugs = Slugs;
            var existing = (await db.Events.Where(e => slugs.Contains(e.Slug)).Select(e => e.Slug).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var now = DateTime.UtcNow;

            // Events seeded before Image Garden existed carry the free-text venue; link them now.
            // Only rows still in that exact fallback state are touched.
            if (imageGarden is not null)
            {
                var gardenSlugs = Specs.Where(s => s.AtImageGarden)
                    .Select(s => EventQueries.BaseSlug(s.Name, s.StartsAt)).ToList();
                var unlinked = await db.Events
                    .Where(e => gardenSlugs.Contains(e.Slug) && e.DestinationId == null && e.VenueName == ImageGardenName)
                    .ToListAsync(ct);
                foreach (var ev in unlinked)
                {
                    ev.DestinationId = imageGarden.DestinationId;
                    ev.VenueName = null;
                    ev.UpdatedAt = now;
                    logger?.LogInformation("Linked existing event {Slug} to Image Garden.", ev.Slug);
                }
            }

            var created = 0;
            foreach (var spec in Specs)
            {
                var slug = EventQueries.BaseSlug(spec.Name, spec.StartsAt);
                if (existing.Contains(slug))
                {
                    logger?.LogInformation("Event {Slug} already exists; skipped.", slug);
                    continue;
                }

                var linked = spec.AtImageGarden && imageGarden is not null;
                db.Events.Add(new Event
                {
                    Id = Guid.NewGuid(),
                    Name = spec.Name,
                    Slug = slug,
                    Description = spec.Description,
                    Organiser = spec.Organiser,
                    StartsAt = spec.StartsAt,
                    EndsAt = spec.EndsAt,
                    TimeZone = Event.DefaultTimeZone,
                    DestinationId = linked ? imageGarden!.DestinationId : null,
                    VenueName = spec.AtImageGarden && !linked ? ImageGardenName : spec.VenueName,
                    VenueAddress = spec.VenueAddress,
                    CityId = cityId,
                    TicketUrl = spec.TicketUrl,
                    PriceMinKobo = spec.PriceMinKobo,
                    PriceMaxKobo = spec.PriceMaxKobo,
                    Currency = Event.DefaultCurrency,
                    PriceNote = spec.PriceNote,
                    InstagramHandle = spec.InstagramHandle,
                    WebsiteUrl = spec.WebsiteUrl,
                    IsPublished = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                created++;
                logger?.LogInformation("Seeding event {Slug}.", slug);
            }

            await db.SaveChangesAsync(ct);
            return created;
        }
    }
}
