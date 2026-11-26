using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// Starter blog posts, seeded as Drafts for the founder to review and publish.
    ///
    /// Idempotent by Slug: a post is created only when no post uses that slug and it is not in
    /// BlogPostSlugHistory (so a post the founder has since renamed is not recreated). Existing posts
    /// are never touched, so edits made in the admin survive any number of re-runs.
    ///
    /// Run after the content it links to: destinations (DestinationsSeeder, AfricaPocSeeder,
    /// CalabarSeeder), the trip wishlists (Calabar, Ghana, Benin Republic, Zanzibar, Rwanda &amp; Qatar, Mombasa, Diani)
    /// and EventsSeeder. Places, tags, destinations and wishlists are resolved by exact name and events
    /// by slug at seed time; anything missing is skipped with a warning and the post is still created.
    /// After seeding, every embed marker in the bodies is resolved with <see cref="BlogQueries.ResolveEmbedsAsync"/>
    /// and any that don't resolve are logged.
    ///
    /// Facts come only from the seeded destination, wishlist and event content.
    /// The Mombasa posts also use the research cited in MombasaSeeder and in the comments above each post.
    /// </summary>
    public static class BlogSeeder
    {
        /// <summary>CreatedByUserId for seeded posts: no admin user created them.</summary>
        public static readonly Guid SeedAuthorUserId = Guid.Empty;

        public const string AuthorName = "Hodrac Team";

        private sealed record PostSpec(
            string Slug,
            string Title,
            string Excerpt,
            string MetaTitle,
            string MetaDescription,
            string? CityName,
            string? CountryName,
            string[] TagKeys,
            string[] DestinationNames,
            string[] WishlistNames,
            string[] EventSlugs,
            string Body);

        /// <summary>Slugs of every post this seeder owns.</summary>
        public static IReadOnlyList<string> Slugs => Specs.Select(s => s.Slug).ToList();

        /// <returns>Number of posts created (0 on a re-run).</returns>
        public static async Task<int> SeedAsync(HodracDbContext db, ILogger? logger = null, CancellationToken ct = default)
        {
            ValidateSpecs();

            var strategy = db.Database.CreateExecutionStrategy();
            var created = await strategy.ExecuteAsync(async () =>
            {
                var ownsTransaction = db.Database.CurrentTransaction == null;
                await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

                var count = await SeedCoreAsync(db, logger, ct);

                if (tx != null) await tx.CommitAsync(ct);
                return count;
            });

            await ReportEmbedsAsync(db, logger, ct);
            return created;
        }

        /// <summary>The same rules AdminBlogController applies, so the drafts can be saved and published from the admin as-is.</summary>
        private static void ValidateSpecs()
        {
            var problems = new List<string>();
            foreach (var s in Specs)
            {
                if (!BlogQueries.IsValidSlug(s.Slug) || BlogQueries.ReservedSlugs.Contains(s.Slug))
                    problems.Add($"{s.Slug}: invalid or reserved slug");
                if (s.Title.Length > BlogPost.MaxTitleLength) problems.Add($"{s.Slug}: title too long");
                if (s.Excerpt.Length > BlogPost.MaxExcerptLength) problems.Add($"{s.Slug}: excerpt too long ({s.Excerpt.Length})");
                if (s.MetaTitle.Length > BlogPost.MaxMetaTitleLength) problems.Add($"{s.Slug}: meta title too long ({s.MetaTitle.Length})");
                if (s.MetaDescription.Length > BlogPost.MaxMetaDescriptionLength)
                    problems.Add($"{s.Slug}: meta description too long ({s.MetaDescription.Length})");
                if (AuthorName.Length > BlogPost.MaxAuthorNameLength) problems.Add("author name too long");
                if (s.Body.Contains("<script", StringComparison.OrdinalIgnoreCase)) problems.Add($"{s.Slug}: body contains <script");
                if (BlogQueries.ParseMarkers(s.Body).Any(m => m.Type == "wishlist"))
                    problems.Add($"{s.Slug}: wishlist markers need per-database ids; link wishlists instead");
            }
            if (Specs.Select(s => s.Slug).Distinct(StringComparer.Ordinal).Count() != Specs.Length)
                problems.Add("duplicate slugs");

            if (problems.Count > 0)
                throw new InvalidOperationException("BlogSeeder specs are invalid: " + string.Join("; ", problems));
        }

        private static async Task<int> SeedCoreAsync(HodracDbContext db, ILogger? logger, CancellationToken ct)
        {
            var slugs = Slugs.ToList();
            var taken = (await db.BlogPosts.Where(p => slugs.Contains(p.Slug)).Select(p => p.Slug).ToListAsync(ct))
                .Concat(await db.BlogPostSlugHistory.Where(h => slugs.Contains(h.OldSlug)).Select(h => h.OldSlug).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var now = DateTime.UtcNow;
            var created = 0;

            foreach (var spec in Specs)
            {
                if (taken.Contains(spec.Slug))
                {
                    logger?.LogInformation("Blog post {Slug} already exists (or was renamed); skipped.", spec.Slug);
                    continue;
                }

                var (cityId, countryId) = await ResolvePlaceAsync(db, spec, logger, ct);

                var post = new BlogPost
                {
                    Id = Guid.NewGuid(),
                    Title = spec.Title,
                    Slug = spec.Slug,
                    Excerpt = spec.Excerpt,
                    BodyMarkdown = spec.Body,
                    AuthorName = AuthorName,
                    CreatedByUserId = SeedAuthorUserId,
                    Status = BlogPostStatus.Draft,
                    PublishedAt = null,
                    CreatedAt = now,
                    UpdatedAt = now,
                    MetaTitle = spec.MetaTitle,
                    MetaDescription = spec.MetaDescription,
                    ReadingTimeMinutes = BlogQueries.ReadingTimeMinutes(spec.Body),
                    CityId = cityId,
                    CountryId = countryId,
                };

                // Tags (by key).
                var tags = await db.Tags.Where(t => spec.TagKeys.Contains(t.Key))
                    .Select(t => new { t.Key, t.TagId }).ToListAsync(ct);
                foreach (var key in spec.TagKeys.Where(k => tags.All(t => t.Key != k)))
                    logger?.LogWarning("Blog post {Slug}: tag '{Key}' not found; skipped.", spec.Slug, key);
                foreach (var tag in tags)
                    post.Tags.Add(new BlogPostTag { BlogPostId = post.Id, TagId = tag.TagId });

                // Destinations (by exact name; first by id when names repeat).
                var destRows = await db.Destinations.Where(d => spec.DestinationNames.Contains(d.DestinationName))
                    .Select(d => new { d.DestinationName, d.DestinationId }).ToListAsync(ct);
                var order = 0;
                foreach (var name in spec.DestinationNames)
                {
                    var match = destRows.Where(d => d.DestinationName == name).OrderBy(d => d.DestinationId).FirstOrDefault();
                    if (match is null)
                    {
                        logger?.LogWarning("Blog post {Slug}: destination '{Name}' not found; skipped.", spec.Slug, name);
                        continue;
                    }
                    post.Destinations.Add(new BlogPostDestination { BlogPostId = post.Id, DestinationId = match.DestinationId, SortOrder = order++ });
                }

                // Wishlists (template wishlists, by exact name).
                var wishRows = await db.Wishlists.Where(w => w.IsTemplate && spec.WishlistNames.Contains(w.WishlistName))
                    .Select(w => new { w.WishlistName, w.WishlistId }).ToListAsync(ct);
                order = 0;
                foreach (var name in spec.WishlistNames)
                {
                    var match = wishRows.Where(w => w.WishlistName == name).OrderBy(w => w.WishlistId).FirstOrDefault();
                    if (match is null)
                    {
                        logger?.LogWarning("Blog post {Slug}: template wishlist '{Name}' not found; skipped.", spec.Slug, name);
                        continue;
                    }
                    post.Wishlists.Add(new BlogPostWishlist { BlogPostId = post.Id, WishlistId = match.WishlistId, SortOrder = order++ });
                }

                // Events (by slug, which is stable; see EventsSeeder.Slugs).
                var eventRows = await db.Events.Where(e => spec.EventSlugs.Contains(e.Slug))
                    .Select(e => new { e.Slug, e.Id }).ToListAsync(ct);
                order = 0;
                foreach (var slug in spec.EventSlugs)
                {
                    var match = eventRows.FirstOrDefault(e => e.Slug == slug);
                    if (match is null)
                    {
                        logger?.LogWarning("Blog post {Slug}: event '{EventSlug}' not found; skipped.", spec.Slug, slug);
                        continue;
                    }
                    post.Events.Add(new BlogPostEvent { BlogPostId = post.Id, EventId = match.Id, SortOrder = order++ });
                }

                db.BlogPosts.Add(post);
                created++;
                logger?.LogInformation(
                    "Seeding draft blog post {Slug}: {Destinations} destinations, {Wishlists} wishlists, {Events} events, {Tags} tags.",
                    spec.Slug, post.Destinations.Count, post.Wishlists.Count, post.Events.Count, post.Tags.Count);
            }

            await db.SaveChangesAsync(ct);
            return created;
        }

        private static async Task<(Guid? CityId, Guid? CountryId)> ResolvePlaceAsync(
            HodracDbContext db, PostSpec spec, ILogger? logger, CancellationToken ct)
        {
            Guid? countryId = null;
            if (spec.CountryName is not null)
            {
                countryId = await db.Countries.Where(c => c.CountryName == spec.CountryName)
                    .OrderBy(c => c.CountryId).Select(c => (Guid?)c.CountryId).FirstOrDefaultAsync(ct);
                if (countryId is null)
                    logger?.LogWarning("Blog post {Slug}: country '{Name}' not found; left empty.", spec.Slug, spec.CountryName);
            }

            Guid? cityId = null;
            if (spec.CityName is not null)
            {
                var cities = db.Cities.Where(c => c.CityName == spec.CityName);
                if (countryId is not null) cities = cities.Where(c => c.CountryId == countryId);
                cityId = await cities.OrderBy(c => c.CityId).Select(c => (Guid?)c.CityId).FirstOrDefaultAsync(ct);
                if (cityId is null)
                    logger?.LogWarning("Blog post {Slug}: city '{Name}' not found; left empty.", spec.Slug, spec.CityName);
            }

            return (cityId, countryId);
        }

        /// <summary>Logs any embed marker that the public/admin resolver would return as not found.</summary>
        private static async Task ReportEmbedsAsync(HodracDbContext db, ILogger? logger, CancellationToken ct)
        {
            var unresolved = 0;
            foreach (var spec in Specs)
            {
                var embeds = await BlogQueries.ResolveEmbedsAsync(db, spec.Body, ct);
                foreach (var e in embeds.Where(e => !e.Found))
                {
                    unresolved++;
                    logger?.LogWarning("Blog post {Slug}: embed {Marker} does not resolve.", spec.Slug, e.Marker);
                }
            }
            if (unresolved == 0)
                logger?.LogInformation("All blog seed embed markers resolve.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Posts
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly PostSpec[] Specs =
        {
            // ── 1. Benin City ──────────────────────────────────────────────────
            new(
                Slug: "things-to-do-in-benin-city",
                Title: "Things to Do in Benin City: Cinemas, Arcades, Bowling and Leaf Rice",
                Excerpt: "Where to watch a film, bowl, throw an axe or eat leaf rice in Benin City, with the prices and hours we could find and what to confirm before you go. Plus the events coming up at Image Garden.",
                MetaTitle: "Things to Do in Benin City: Cinemas, Bowling, Arcades, Leaf Rice",
                MetaDescription: "Cinemas, a bowling alley, axe throwing, VR, karaoke and where to eat leaf rice in Benin City, with prices, hours and upcoming Image Garden events.",
                CityName: "Benin City",
                CountryName: "Nigeria",
                TagKeys: new[] { "group_friendly", "food_focused", "nightlife", "local_favorite" },
                DestinationNames: new[]
                {
                    "Kada Plaza", "Filmhouse Cinemas Benin", "Genesis Cinemas Benin City", "DeepEnd Cinemas & Fun House",
                    "Space VR Benin", "Jordan's Playworld", "Vitto Lounge", "Amazing Target Zone",
                    "Motorsport Raceway Evbuobanosa (MRE)", "Vice City Restaurant and Lounge", "Sip and Dip Restaurant and Lounge",
                    "Chillingchow", "Barcode Station", "Benin City Leaf Rice (Izoko) Spots", "Mat-Ice Fast Food", "Image Garden",
                },
                WishlistNames: Array.Empty<string>(),
                EventSlugs: new[]
                {
                    "oga-bole-festival-2026", "fungasm-2026", "metro-events-benin-3-days-detty-december-rave-2026",
                },
                Body: """
                    Most of Benin City's cinemas, arcades and lounges sit along a handful of roads: Sapele Road, Airport Road, TV Road and the GRA. This guide covers the cinemas, the arcades, the city's first bowling alley, a new target-sports venue and where to eat leaf rice, with the prices we could find. Many of these places don't publish hours or prices, so treat the figures as a starting point and call ahead.

                    ## Cinemas

                    ### Kada Plaza

                    Kada Plaza on Sapele Road in Oka has been a go-to for a film and a night out since it opened in November 2013. Kada Cinemas shows Hollywood and Nollywood releases, and the same building has a children's arcade (Playtime Arcade), Kung Fu's Chinese restaurant and Elliot's Sports Bar, which has pool tables and shows live Premier League football.

                    A reviewer reported tickets from about ₦4,000, and popcorn costs about the same. Kada has advertised a Monday and Tuesday deal of ₦6,000 for two tickets. Older reviews mention bumper karts and 5D rides, so check they're still running before you promise them to children.

                    {{destination:kada-plaza}}

                    ### Filmhouse and Genesis

                    Filmhouse Cinemas Benin is in Voen Mall, opposite the UNIBEN main gate in Ugbowo. A 2026 guide lists standard tickets at ₦5,000 and 3D Premium at ₦6,000, with a ₦3,000 Wednesday rate for students and corpers. That makes Wednesdays crowded, and the Ugbowo stretch of road is often congested.

                    Genesis Cinemas has four screens and, according to local creators, sits in the Shoprite-anchored Benin City Mall on Sapele Road. It took about ₦435 million at the box office in 2025, one of the ten highest totals in Nigeria. Its Benin City ticket prices aren't published, so check genesiscinemas.com when you book. Listings disagree on the address, so confirm it before you set off.

                    ## Arcades, VR and a haunted house

                    DeepEnd Cinemas & Fun House, off TV Road opposite NTA, pairs a cinema with a walk-through haunted house, a game zone with console, arcade and VR set-ups, and 5D, 7D and 9D rides. The haunted house has become a viral outing on TikTok, and creators put it at about ₦4,000. It's built to scare, so it isn't suitable for young children. Each attraction is paid for separately, and parking is limited.

                    Space VR Benin at Okhoro Junction on New Lagos Road runs headset VR games, booked through a site linked from its Instagram (@spacevrbenin). Instagram gives weekday hours of 10:00 AM to 7:00 PM and weekend hours of 12:00 PM to 7:00 PM. Prices aren't published, so ask before you play.

                    For younger children, Jordan's Playworld is an indoor play centre in Lifestyle Value Mall, off Irhirhi Road near Airport Road. Very little about it is published online, so confirm it's open and ask the entry price first.

                    ## Bowling and target sports

                    Vitto Lounge, off Ugbor-Amagba Road, opened in December 2024 with a rooftop area and a bowling alley it bills as the first in Benin City. It is also a restaurant, lounge and club, with live music, themed nights and Euphoria Sundays. The price per game isn't published, so ask when you book on Instagram (@vitto_lounge) or WhatsApp. Afternoons suit bowling and food; at weekends it runs into the early hours, so plan your ride home.

                    {{destination:vitto-lounge}}

                    Amazing Target Zone opened in August 2026 at 120 Irhirhi Road, off Airport Road. It has archery, axe throwing and an airsoft range, with trainers who guide beginners, and activities start from ₦10,000. Minimum ages and gear rules aren't published, so ask before bringing children. The road name is spelt several ways; search the venue's name on Google Maps or call 0816 234 8933.

                    If you follow motorsport, Motorsport Raceway Evbuobanosa (MRE) is a purpose-built circuit beside the Benin-Asaba highway, east of the city. It opens for race days only, so check the organisers' calendars before making the trip, and travel in daylight.

                    ## Karaoke and nights out

                    Two lounges run karaoke midweek. Vice City on Ihama Road in GRA holds a Wednesday karaoke night from about 8:00 PM. Sip & Dip, inside the Eli Love filling station on Benin-Sapele Road, reportedly runs karaoke on Wednesdays and Fridays from about 6:00 to 9:00 PM, but that comes from social-media posts, so call first. Sip & Dip is small, and one reviewer suggests budgeting ₦15,000 to ₦20,000 a head.

                    Chillingchow, behind De Brit Hotel off Ihama Road, keeps board and card games for diners. Barcode Station on Airport Road is a long-running party spot with indoor and outdoor areas, a DJ and a hype man; people go for the atmosphere more than the food.

                    ## Leaf rice and ice cream

                    Leaf rice, locally called izoko, paper rice or pepper rice, is plain white rice wrapped and rested in green leaves, then served with a hot tomato-and-pepper stew and beef, shaki or ponmo. The popular pots sell out by mid-afternoon, so go late morning or at lunch.

                    {{destination:benin-city-leaf-rice-izoko-spots}}

                    Mama Ebo Pepper Rice on Airport Road in Oka is the best known and has a sit-down dining room. A local creator put a plate with meat at under ₦5,000 in 2024. Mama 12, Madam 2 O'Clock (reportedly behind Image Garden on Boundary Road) and Mama Ivie have little online presence, so ask locally for directions. A reviewer warned of a fake account using Mama Ebo's name; pay only at the counter or through the official number or Instagram.

                    For something cold, Mat-Ice has sold soft-serve ice cream in Benin City since it started as a kiosk on Ekenwan Road in 1994. Ice cream starts from about ₦700 according to the company's website, and a meal is usually under ₦5,000.

                    ## Events at Image Garden

                    Image Garden, at 6 Boundary Road in GRA, is an open-air lawn with a stage and room for about 5,000 people standing. It opens for events rather than on set hours, and December is its busiest month.

                    The Oga Bole Festival, a yearly street-food festival built around bole (roasted plantain), is on Saturday 28 November from 12:00 to 4:00 PM, with free general entry.

                    {{event:oga-bole-festival-2026}}

                    Metro Events runs a three-day Detty December rave from 18 to 20 December 2026. The venue is reported as Image Garden but still needs confirming, and daily times and ticket prices hadn't been announced, so watch @officialmetroevents.

                    {{event:metro-events-benin-3-days-detty-december-rave-2026}}

                    Fungasm, a daytime rave brand, holds its December edition on 6 December from 12 noon at Ogbe Primary School, off Airport Road, rather than at Image Garden. Prices for 2026 weren't announced; the December 2025 edition cost ₦5,000 to ₦8,000.

                    At any big event at Image Garden, buy tickets through the organiser's link, keep your phone and valuables close, and arrange your ride home before it ends, because ride-hail prices surge. Boundary Road has been reported in poor condition, so allow extra time.
                    """),

            // ── 2. Carnival Calabar ────────────────────────────────────────────
            new(
                Slug: "carnival-calabar-2026-peak-week",
                Title: "Carnival Calabar 2026: The Peak Week, Day by Day",
                Excerpt: "What's on each day of Carnival Calabar's peak week, 25 to 30 December 2026, from the Christmas Concert to the Parade of Bands and Nyoro Ekpe, with where to stand and what to book early.",
                MetaTitle: "Carnival Calabar 2026 Peak Week: Day by Day, 25 to 30 December",
                MetaDescription: "A day-by-day guide to Carnival Calabar's peak week, 25 to 30 December 2026: parades, stadium shows, Nyoro Ekpe, where to watch and what to book.",
                CityName: "Calabar",
                CountryName: "Nigeria",
                TagKeys: new[] { "cultural", "music", "seasonal", "crowded" },
                DestinationNames: new[]
                {
                    "Carnival Calabar Route", "Millennium Park Calabar", "U.J. Esuene Stadium", "Calabar Botanic Garden",
                    "Calabar Festival Village (Christmas Village)", "Calabar Museum (Old Residency)", "Marina Resort & Slave History Museum",
                    "Calabar International Convention Centre", "Calabar Drill Ranch", "Tinapa", "Combat Games Zone",
                    "Calabar Local Food Spots", "Kwa Falls", "Hope Waddell Training Institution", "Obudu Mountain Resort",
                    "Afi Mountain Drill Ranch",
                },
                WishlistNames: new[] { "Carnival Calabar 2026" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Carnival Calabar runs as a whole season, with 62 official events between the Christmas Tree Lighting at Millennium Park on 30 November and 1 January 2027. The parades and the big stadium shows are packed into the last week of December, and this guide goes through that week day by day. Times are in WAT and follow the Carnival Calabar Commission's official 2026 schedule, which can still change, so check carnival.crossriverstate.gov.ng before each day.

                    ## Before you go

                    Calabar's hotel stock is small. Rooms sell out by November and parade-week rates are roughly double, so book early. The flight from Lagos to Margaret Ekpo International Airport takes about an hour, and there are flights from Abuja too. The road from Lagos takes more than 10 hours and isn't advised in December.

                    Watching the parades from the street is free. Grandstand seats and the evening shows at U.J. Esuene Stadium are paid and sold separately; buy only through official channels. Roads close on parade days, so expect to walk.

                    ## Friday 25 December: Christmas Day

                    If you're travelling with children, the Calabar Kiddie's Hero Xmas Party brings costumed characters to the Old Residency museum at 10:00 AM. Spend the afternoon settling in and eating. Calabar's kitchens are known across Nigeria for Efik soups such as edikang ikong, afang and fisherman soup.

                    In the evening, the Calabar Festival Christmas Concert is at U.J. Esuene Stadium; its start time hadn't been announced when the schedule came out. The alternative is the Festival Village, which has performances every night from 7:00 PM between 2 December and 1 January. The schedule lists its venue only as "Calabar Municipality" and the site has moved between years, so confirm where it is.

                    ## Saturday 26 December: Cultural Carnival

                    The Cultural Carnival flags off at 9:00 AM from Millennium Park, at the roundabout locals call Eleven Eleven. Cultural troupes parade half the circuit, as far as the Rabana turning.

                    In the afternoon, visit the Calabar Museum in the Old Residency, a prefabricated colonial building put up in 1884 on a hill above the river. Its galleries cover the Efik, the palm-oil trade, the transatlantic slave trade and colonial rule, and the season's arts exhibition is open there from 9:00 AM daily. Locals suggest going before you see the Slave History Museum at Marina Resort, on the site of a former slave-trading warehouse on the waterfront.

                    At 5:00 PM the stadium hosts the Night of Kings and Queens of the Carnival Calabar Bands. For something dressier, the Queen of Humanity pageant starts at 7:00 PM at the Calabar International Convention Centre.

                    ## Sunday 27 December: Junior Carnival

                    Children's bands in full costume set off from the Calabar Botanic Garden at 10:00 AM. This year's Junior Carnival theme is "Future Assured". The garden is busy on event mornings, so keep children close.

                    {{destination:calabar-botanic-garden}}

                    The Calabar Drill Ranch makes a quieter afternoon. It is the city base of Pandrillus, which works to save the drill, an endangered primate found in south-east Nigeria and neighbouring Cameroon, and staff show visitors around. Hours may be limited over Christmas, so contact them ahead. Then eat early and rest, because Monday is long.

                    ## Monday 28 December: Parade of Bands

                    Carnival Calabar and the Parade of Bands, the main event, flags off at 10:00 AM from Eleven Eleven. The route passes the adjudication point at Mary Slessor, continues to Atekong on Marian Road, turns north at Effio Ette Roundabout, reaches the Mobil judging point on Murtala Mohammed Highway and finishes at U.J. Esuene Stadium.

                    {{destination:carnival-calabar-route}}

                    The bands perform for the judges at Mary Slessor, Atekong and Mobil, and the best spots there fill hours before they arrive. A paid grandstand seat gives a better view of the judged performances; the organisers set prices and locations. Either way, carry water, a hat and sunscreen, wear shoes you can stand in for hours, and agree a meeting point with your group in case you get separated.

                    The day ends with the Feather Concert at 7:00 PM at the stadium, the final display after the parade.

                    {{destination:u-j-esuene-stadium}}

                    ## Tuesday 29 December: Bikers Carnival and Insomnia Night

                    The morning is free. Tinapa, the business and leisure resort beside the river north of the city, is a short drive away. Activity there has varied over the years, so check what's open. Combat Games Zone inside Tinapa runs archery, axe throwing and gel-blaster rounds, but only at weekends and on some public holidays, so message @combatgameszone before going on a Tuesday.

                    At 12:00 PM the Bikers Carnival rolls along the route. Insomnia Night starts at 7:00 PM at the stadium, billed with national acts and an international act, and Davido has separately announced an appearance at the stadium that night. Stadium entrances get very crowded, so arrive early and arrange your ride home before you go in.

                    ## Wednesday 30 December: Nyoro Ekpe

                    Nyoro Ekpe, the celebration of the Ekpe society, starts at 10:00 AM at the Botanic Garden. Ekpe masquerades are central to Efik culture and some rites aren't for outsiders, so follow local guidance on where to stand and ask before taking photos. Have a last plate of afang or edikang ikong before heading to the airport.

                    ## Staying longer

                    If you stay on, the Festival Village has fireworks at midnight on 31 December, and the Tinkoriko heritage procession sets off at 12:00 PM on 1 January 2027.

                    Kwa Falls, about 25 km from Calabar at Aningeje in Akamkpa, makes a half-day trip: a steep staircase leads down into a rainforest gorge with a deep plunge pool. The gate fee has been reported at ₦500 to ₦2,000. The steps have missing railings in places, so it isn't suited to young children or anyone unsteady on their feet, and you should only swim if you're confident in deep water.

                    {{destination:kwa-falls}}

                    History visitors can ask to see Hope Waddell Training Institution, founded in 1895 and one of Nigeria's oldest secondary schools, whose alumni include Nnamdi Azikiwe. It is a working school with no published visiting hours, so call ahead and check in at the office first.

                    Longer extensions on the trip include Obudu Mountain Resort, most of a day's drive north, so plan two or more nights. There is also an overnight at the Afi Mountain Drill Ranch, Pandrillus's forest field site, which has no power or mobile network; go as a group and confirm access with Pandrillus first.
                    """),

            // ── 3. Lagos ───────────────────────────────────────────────────────
            new(
                Slug: "weekend-activities-in-lagos",
                Title: "Weekend Activities in Lagos: 12 Things to Do Beyond Brunch",
                Excerpt: "Twelve things to do in Lagos at the weekend, from roller skating at the Federal Palace Hotel to archery in Surulere and Saturday salsa in Victoria Island, with prices and booking details.",
                MetaTitle: "Weekend Activities in Lagos: 12 Things to Do Beyond Brunch",
                MetaDescription: "Skating, archery, paintball, kayaking, theatre, pottery and salsa: 12 weekend activities in Lagos with prices, opening hours and how to book.",
                CityName: "Lagos",
                CountryName: "Nigeria",
                TagKeys: new[] { "group_friendly", "adventurous", "art", "social", "couple_friendly" },
                DestinationNames: new[]
                {
                    "Kriss Kross Skating Rink", "Zen Archery", "Seal Team Paintball", "Kayak In Lagos", "CeraCerni's Art Hub",
                    "The Rube", "Terra Kulture", "National Museum Lagos", "Windsor Gallery Lagos", "Rufus & Bee",
                    "Giwa Gardens Water Park", "Gameland Lagos", "Pitstop Lagos",
                },
                WishlistNames: Array.Empty<string>(),
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Lagos has a growing list of weekend activities that get you moving or making something: skating, archery, paintball, pottery and paddling on the lagoon. Most are on the Island, in Victoria Island, Oniru and Lekki Phase 1, with a couple on the mainland. The prices below are the latest we have. Several of these places change them often, so check when you book.

                    ## Get moving

                    ### Roller skating at Kriss Kross

                    Kriss Kross Skating Rink moved from Lekki Phase 1 to the grounds of the Federal Palace Hotel on Ahmadu Bello Way, Victoria Island, in 2026. It is an open-air rink with a bright blue floor, and beginners can book a one-hour class with an instructor. It opens Tuesday to Thursday from 12 noon to 9:00 PM and Friday to Sunday from 10:00 AM to 10:00 PM, and closes on Mondays.

                    Older Lekki-era prices were ₦15,000 for adults, ₦10,000 for children and ₦5,000 for skate rental, but the rink has posted a new 2026 list, so check its Instagram (@krisskrossskate). Every skater signs an online agreement first, and rain makes the surface slippery.

                    {{destination:kriss-kross-skating-rink}}

                    ### Archery at Zen Archery

                    Zen Archery runs a public range in the Lagos Zen Archery Arena at Teslim Balogun Stadium in Surulere, which it opened with the Lagos State Sports Commission in April 2025. You book a one-hour lane at zenarchery.club, and a coach fits your bow and gives a safety briefing. Adults pay ₦15,000, children aged 7 to 15 pay ₦7,500, and groups of five or more pay ₦10,000 each. Surulere is a long drive from Lekki at rush hour, so book a mid-morning or weekend slot. Arriving more than an hour late forfeits the session.

                    {{destination:zen-archery}}

                    ### Paintball at Seal Team

                    Seal Team Paintball is a speedball arena inside ATV World on Trinity Avenue, Oniru. Standard (25 pellets) costs ₦8,000 online or ₦8,500 at the door, and Premium (100 pellets) costs ₦11,000 online. The marker, mask, body armour and overalls are included, payment is cashless, and the referee's decision is final. Wear old clothes and closed shoes, and keep your mask on the whole time you're in the arena.

                    ### Kayaking on the lagoon

                    Kayak In Lagos rents kayaks on Foreshore Road, Victoria Island, for a paddle on the Lagos Lagoon with the Ikoyi and Victoria Island skyline in view. Staff show first-timers the basics. Prices aren't published: a 2024 guide reported ₦7,500 a session and said it ran at weekends and on public holidays from 9:00 AM to 6:00 PM, so confirm on WhatsApp. Go in the morning or late afternoon, because there's no shade on the water.

                    ## Make something

                    At CeraCerni's Art Hub in Lekki Phase 1, every room of a converted house is covered in murals, and you can book instructor-led sessions. The website prices sip and paint at ₦20,000 a canvas, the pottery wheel at ₦25,000 and tufting from ₦100,000 for a small rug. A drink, small chops and the piece you make are included. Some activities have minimum ages, such as 14 for tufting.

                    The Rube on Adeyemo Alakija Street, Victoria Island, runs floral sessions in which a tutor helps you arrange a bouquet from fresh flowers to take home. It is a premium outing: the standard session costs ₦85,000 a person and the kids' session ₦50,000. It runs by booking only.

                    ## See a show or an exhibition

                    For many Lagosians, Terra Kulture on Tiamiyu Savage Street, Victoria Island, is the home of Lagos theatre. It has a 450-seat arena, an outdoor lawn, a restaurant, a bookshop and a gallery. This season it has "I Wan Blow", an interactive comedy on the lawn at weekends until 13 December (Regular ₦25,000), and "ITORO! The Musical" in the arena from 26 December to 10 January, from ₦20,000. Buy tickets at tickets.terrakulture.com, and bring mosquito repellent for lawn shows.

                    {{destination:terra-kulture}}

                    The National Museum at Onikan, Lagos Island, reopened a refurbished timeline gallery in April 2026 that runs from Nok terracottas to the Igbo-Ukwu and Benin bronzes. A 2026 visitor paid ₦4,000 plus a ₦200 charge. Sources disagree about which days it opens, so call before you go.

                    Windsor Gallery Lagos on Goriola Street, Victoria Island, shows contemporary African art and is free to enter. Its hours are listed as Tuesday to Saturday, 10:00 AM to 7:00 PM, but its Instagram has shown other times and exhibitions change every few weeks, so message ahead.

                    ## Games and water parks

                    Rufus & Bee in the Twinwaters centre, Lekki Phase 1, has around 100 arcade games, four bowling lanes, snooker and a sports bar. You buy a Buzz Card for ₦1,000 and load it with chips; the smallest pack is ₦7,500 for 55 chips. Bowling is about ₦5,000 a person with shoes, Wednesdays have cheaper games, and it's closed on Mondays. Groups get through chips quickly, so set a budget per person.

                    {{destination:rufus-and-bee}}

                    Giwa Gardens in Sangotedo is the big water-park day out, with high slides, a wave pool and a fast river. Standard tickets on the official site started at about ₦53,950 in November 2026, before discounts, plus a required ₦15,000 rental for a towel, life jacket and tube. Outside food isn't allowed. Sessions run 10:00 AM to 3:00 PM and 4:00 to 8:00 PM, mainly at weekends, and riders must be at least 1.4 m tall.

                    {{destination:giwa-gardens-water-park}}

                    Gameland Lagos at The Lekki Coliseum has offered all-day outdoor games for about ₦5,000. Its website was offline in November 2026 and it may have closed, so check @gamelandlagos before you go.

                    ## Saturday night salsa

                    Pitstop Lagos, a cycling-themed café on Younis Bashorun Street, Victoria Island, turns into a salsa night on Saturdays from 7:00 to 10:00 PM, with a live band and instructors who help beginners. A September 2025 guide listed entry as free. Expect VAT and service charges on food and drink, and confirm the session on @pitstoplagos.
                    """),

            // ── 4. Abuja ───────────────────────────────────────────────────────
            new(
                Slug: "things-to-do-in-abuja-beyond-jabi-lake",
                Title: "Things to Do in Abuja Beyond Jabi Lake",
                Excerpt: "Go-karts and axe throwing at Central Park, pottery in a palm garden, a boat ride below Aso Rock and salsa on a Wuse 2 rooftop: things to do in Abuja once you've been out on Jabi Lake.",
                MetaTitle: "Things to Do in Abuja Beyond Jabi Lake: Parks, Pottery and Salsa",
                MetaDescription: "Central Park, Magicland, Sarius Palmetum, the zoo boat ride, Varuna's pool and salsa at Barracuda: things to do in Abuja, with prices and hours.",
                CityName: "Abuja",
                CountryName: "Nigeria",
                TagKeys: new[] { "family_friendly", "group_friendly", "nature", "couple_friendly" },
                DestinationNames: new[]
                {
                    "Amazon Kayak", "Central Park Abuja", "Vitality Clubhouse", "Axes & Roses", "Magicland Amusement Park",
                    "National Children's Park and Zoo", "BMT African Garden", "Sarius Palmetum", "Paint and Pottery Social",
                    "Eli & Lulu Café", "Varuna by The Good Beach", "Barracuda Resto Pub", "Floris Garden",
                },
                WishlistNames: Array.Empty<string>(),
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Jabi Lake is where a lot of Abuja weekends start, and Amazon Kayak on its shore is the easy way onto the water: kayaks cost about ₦3,000 to ₦3,500 a person and a couples' hydrocycle about ₦3,000. This guide covers what else the city has, mostly in the Central Business District, Wuse 2 and Maitama: parks, gardens, creative studios and evenings out. Many of the prices date from 2025, so expect some to have gone up.

                    ## Central Park

                    Central Park Abuja on Kur Mohammed Avenue, beside Kebbi Hotel, is free to enter and you pay per activity. It has a 500 m go-kart track, a paintball arena, the Aqualandia water park, a cycling trail, trampolines and several restaurants. Reported 2025 prices: go-karts ₦15,000 for seven minutes (ages 13 and up), paintball ₦15,000 with 100 pellets, and the water park ₦25,000 for adults and ₦20,000 for children under 12. The park opens daily from 10:00 AM to 8:00 PM, with go-karts and paintball from 12:00 PM.

                    {{destination:central-park-abuja}}

                    Two separate venues sit inside the park, each with its own booking line. Vitality Clubhouse has a restaurant, a roof terrace that looks towards the National Mosque, a nine-hole adventure mini-golf course and a shooting gallery that uses non-live rounds (₦5,600 a session in August 2025). Axes & Roses, which bills itself as Nigeria's first axe-throwing venue, charged ₦3,500 solo or ₦6,000 for two for axe throwing or archery in 2025, and has snooker and darts as well.

                    Book go-karts, paintball and shooting slots ahead at weekends. The website centralparkabuja.com.ng no longer belongs to the park, so book through its WhatsApp (0809 272 2852) or Instagram instead.

                    ## Rides and animals

                    Magicland on Constitution Avenue, formerly Wonderland, is the classic family day out. Entry is about ₦500 and you pay per ride: ₦1,000 for most children's rides, ₦1,500 for the roller coaster, bumper cars and Ferris wheel, and ₦2,000 for the pendulum. The indoor Fun City arcade runs on a rechargeable card. Some rides are old and may be closed for maintenance, so check which are running before buying for a group, and bring cash.

                    {{destination:magicland-amusement-park}}

                    The National Children's Park and Zoo in Asokoro sits behind the Presidential Villa near Aso Rock. The lake is the highlight: a motorboat takes up to 10 people round it twice for ₦3,000 each (late 2024 prices), or you can row yourself for ₦2,000. Older reviews note that there are fewer animals than there used to be. Expect a security presence and follow any restrictions on photography near government buildings.

                    {{destination:national-childrens-park-and-zoo}}

                    BMT African Garden in Wuse 2 is a private garden with a mini zoo, an outdoor library, a pool and a restaurant. The zoo and museum area costs about ₦1,000 for adults, and the animal train ₦7,000 a ride.

                    ## Gardens and studios

                    Sarius Palmetum in Maitama is a botanical garden of about 20 hectares with 400 to 450 palm species, ponds and rock formations. Entry was reported at ₦2,000 in 2025. Monkeys roam the grounds around dawn and dusk; don't feed them, and keep bags closed.

                    {{destination:sarius-palmetum}}

                    Inside the garden, Paint and Pottery Social runs pottery and painting sessions that need no experience. Its 2025 list priced canvas painting at ₦16,000 and pottery making at ₦20,400. Clay pieces are fired before you can take them, so plan to collect later, and you may also pay the garden's gate fee.

                    Eli & Lulu Café in Habo Mall, Wuse 2, is a styled café with bookshelves and a daily breakfast buffet. Its Creative Series includes Bake & Sip, a three-hour session where you learn simple baking over drinks, along with Sip & Paint and jazz nights. Prices for these aren't published, so ask the café. It is a premium spot, with mains from about ₦10,500 to ₦20,000.

                    ## Pool days and evenings

                    Varuna by The Good Beach in Mabushi brings a beach to a landlocked city, with white sand, palm trees, a pool and cabanas. Pool entry has been reported at ₦5,000 on weekdays and ₦10,000 at weekends. Thursday to Sunday nights bring DJs and themed parties. Drinks are pricey, VAT is added, and weekend nights are loud and crowded, so come on a weekday afternoon if you want a calm pool visit.

                    Barracuda Resto Pub, a rooftop in Wuse 2, hosts Dance and Art Alive's Latin nights. Tuesday is Cuban salsa, Thursday kizomba and Saturday bachata, each with a beginner-friendly class at about 8:00 PM. Tuesday and Saturday cost ₦3,000 and Thursday is free, but you must buy food or drink at every session. Arrive by 7:30 PM to catch the class.

                    {{destination:barracuda-resto-pub}}

                    For a cheaper dinner, Floris Garden in Wuse Zone 3 serves mostly Nigerian food among lawns and pergolas, and you can eat for under ₦10,000. Bring insect repellent in the evening.
                    """),

            // ── 5. Kigali + Doha ───────────────────────────────────────────────
            new(
                Slug: "kigali-stopover-on-the-way-to-doha",
                Title: "A Kigali Stopover on the Way to Doha",
                Excerpt: "RwandAir's Lagos to Doha connections run through Kigali, so you can stop for two nights or more on the way. How the routing works, the visas Nigerians need and what to see in each city.",
                MetaTitle: "A Kigali Stopover on the Way to Doha: Visas, Routing, What to See",
                MetaDescription: "How to add a Kigali stopover to a Lagos to Doha trip on RwandAir: Rwanda and Qatar visa rules for Nigerians, and what to see in each city.",
                CityName: null,
                CountryName: null,
                TagKeys: new[] { "cultural", "history", "nature" },
                DestinationNames: new[]
                {
                    "Kigali Genocide Memorial", "Kimironko Market", "Inema Arts Center", "Kandt House Museum",
                    "Nyamirambo & Nyamirambo Women's Center", "Kigali Convention Centre", "Volcanoes National Park",
                    "Museum of Islamic Art", "National Museum of Qatar", "Souq Waqif", "Msheireb Downtown Doha",
                    "Khor Al Adaid (Inland Sea)", "Katara Cultural Village", "The Pearl-Qatar", "Doha Corniche",
                },
                WishlistNames: new[] { "Doha with a Kigali Stopover", "Kigali & Doha" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    RwandAir flies non-stop from Lagos to Kigali and on from Kigali to Doha, so a Lagos to Doha ticket on RwandAir already routes through Rwanda. Ask for a multi-city ticket with a stopover and you can spend a couple of nights in Kigali on the way. We have two trips built on this route: a six-day Doha break with two nights in Kigali, and an eight-day trip with four days in Rwanda, including Volcanoes National Park.

                    ## How the routing works

                    Lagos is the only Nigerian city on RwandAir's current booking site; its Abuja service appears to have been dropped. RwandAir resumed Kigali to Doha in mid-2026 with around six flights a week, and the flight takes roughly six hours. Kigali is one hour ahead of Lagos, and Doha is one hour ahead of Kigali.

                    For the way home, recent schedules show Qatar Airways flying non-stop from Doha to Lagos about twice a day and to Abuja several times a week, or you can route back through Kigali on RwandAir. Our trips budget roughly US$1,000 to $1,400 for flights on the six-day version and US$1,100 to $1,600 on the eight-day one. Schedules and fares change, so confirm both before you book.

                    ## Visas and entry

                    The two countries treat Nigerian passports very differently, so start on the paperwork early.

                    ### Rwanda

                    Nigerians, like other African Union, Commonwealth and Francophonie citizens, get a visa on arrival with the fee waived for a visit of up to 30 days. Carry your yellow fever card, because Nigeria is a yellow fever risk country, and have your onward ticket to Doha and your hotel address ready. Your passport should be valid for at least six months. Plastic bags are banned in Rwanda and may be taken from you at the airport.

                    ### Qatar

                    Nigerian passport holders need a visa before they travel. Apply for the tourist (A1) e-visa on the official Hayya portal (hayya.qa). Since 2025, Nigerian media and travel agents have reported stricter checks on Nigerian applicants, who are asked for a confirmed hotel booking (often made through Discover Qatar), a return ticket and proof of funds. Carry a printed copy of the approved visa on the Kigali to Doha flight. Requirements change, so check hayya.qa or the Embassy of Qatar in Abuja before you book.

                    ## Kigali in two days

                    Start at the Kigali Genocide Memorial and allow two to three hours. Entry is free, the audio guide costs about RWF 20,000 (US$15), and photos aren't allowed inside the exhibitions. The material is graphic, so plan something quiet for afterwards.

                    {{destination:kigali-genocide-memorial}}

                    Kimironko is the city's biggest market. Bargain politely for fabric and crafts, and if you're staying a few days, order a shirt or dress from one of the market tailors. Agree the price, design and collection date first, and bring a cloth bag.

                    {{destination:kimironko-market}}

                    Inema Arts Center in Kacyiru shows contemporary Rwandan art in its resident artists' studios and is free to browse. Kandt House Museum, the home of Kigali's founder Richard Kandt, covers natural and colonial history and keeps live reptiles. In the evening, Nyamirambo's main street has brochettes, chapati and music, and the Nyamirambo Women's Center runs a walking tour of about three hours that you should book ahead.

                    Check the calendar before you fix your dates. On the last Saturday of every month Rwanda holds Umuganda, a community-work morning from roughly 8:00 to 11:00 AM, when traffic stops and many places open late. The memorial opens around 1:00 PM that day, so don't plan a drive or your first visit for that morning.

                    ## With four days: Volcanoes National Park

                    The longer trip adds a drive of two to two and a half hours north to Musanze. The golden monkey trek starts after a briefing at Kinigi park headquarters at about 7:00 AM; the permit is about US$100 and the minimum age is 12. If your budget stretches further, a mountain gorilla permit costs US$1,500 a person (minimum age 15) and should be booked well ahead on the RDB portal. Lake Kivu, Akagera and Nyungwe are optional extras.

                    ## Doha in three or four days

                    Doha is built around a seven-kilometre waterfront Corniche. The Museum of Islamic Art, in I. M. Pei's building on the bay, costs QR 50 for non-resident adults and is free for children 16 and under. It closes one weekday and sources disagree on which, so check mia.org.qa before you go; on Fridays it opens only after prayers. The National Museum of Qatar, in Jean Nouvel's desert-rose building, also costs QR 50.

                    {{destination:museum-of-islamic-art}}

                    Souq Waqif is free to wander and busiest after dark. Many shops close for a long afternoon break and reopen around 4:00 PM. Next door, the Msheireb Museums occupy restored heritage houses, and Bin Jelmood House among them covers the trade in enslaved East Africans around the Gulf.

                    {{destination:souq-waqif}}

                    A half-day desert safari to Khor Al Adaid, the Inland Sea, costs about QR 220 to 330 a person in a shared 4x4 with a licensed operator, with dune bashing, sandboarding and sunset where the sea meets the dunes. The Pearl, Katara Cultural Village and a short dhow ride off the Corniche fill the rest of the time.

                    Qatar asks visitors to dress modestly in public, so cover shoulders and knees in museums, souqs and malls. Alcohol is served only in licensed hotel bars and restaurants. Both trips are planned for November to March.
                    """),

            // ── 6. Ghana ───────────────────────────────────────────────────────
            new(
                Slug: "ghana-for-nigerian-travellers",
                Title: "Ghana for Nigerian Travellers: Accra, Cape Coast and Kakum",
                Excerpt: "Accra is about an hour's flight from Lagos and Nigerians don't need a visa. A practical guide to a week in Ghana: Accra's old town and markets, the castles at Cape Coast and Elmina, and the Kakum canopy walkway.",
                MetaTitle: "Ghana for Nigerian Travellers: Accra, Cape Coast and Kakum",
                MetaDescription: "Visa-free for Nigerians and an hour from Lagos: a week in Ghana covering Accra, the Cape Coast and Elmina castles, Kakum's canopy walk and Aburi.",
                CityName: null,
                CountryName: "Ghana",
                TagKeys: new[] { "history", "cultural", "nature", "beach" },
                DestinationNames: new[]
                {
                    "Kwame Nkrumah Memorial Park", "Jamestown & Jamestown Lighthouse", "Makola Market", "Artists Alliance Gallery",
                    "Labadi Beach", "Cape Coast Castle", "Kakum National Park Canopy Walkway", "Elmina Castle",
                    "Aburi Botanical Gardens", "Boti Falls", "Akosombo Dam & Volta Lake", "Manhyia Palace Museum", "Kejetia Market",
                },
                WishlistNames: new[] { "Ghana: Castles, Canopies and Accra Culture" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Ghana is one of the easiest international trips from Nigeria. Accra is about an hour's flight from Lagos, English is spoken everywhere, and Nigerians don't need a visa. Our seven-day Ghana trip covers Accra, the Central Region coast and a day in the Akuapem hills.

                    ## Getting there and entry

                    Nonstop flights from Lagos to Accra take about an hour, with Africa World Airlines, Air Peace and others on the route. Abuja has fewer direct options. You can also take a bus from Lagos with ABC Transport, Chisco and others, crossing Benin and Togo and entering Ghana at Aflao. That's three borders and roughly 10 to 15 hours or more, so leave a day early.

                    As an ECOWAS citizen you can stay up to 90 days with a valid passport or an ECOWAS travel certificate; don't rely on a national ID card alone. You need a yellow fever certificate at entry, and officers may ask for a return ticket and where you're staying. Visa-free entry doesn't allow you to work. Requirements change, so check with the Ghana Immigration Service before you travel.

                    Change money or withdraw cedis at Kotoka airport, which is close to the centre. Uber, Bolt and Yango operate across Accra. Entrance fees below are in cedis; our trip converts them at roughly GH₵12 to the US dollar.

                    ## Accra: two days

                    Start at the Kwame Nkrumah Memorial Park, where Ghana's independence was declared in 1957. It has the mausoleum, statues and a museum of Ghana's first president, and non-Ghanaian adults pay GH₵100.

                    The next morning, walk Old Accra in Jamestown with a local guide, past the fishing harbour, Ussher Fort and the boxing gyms, then climb the striped lighthouse for a view over the city.

                    {{destination:jamestown-and-jamestown-lighthouse}}

                    From there, Makola Market is Accra's largest, good for wax prints, beads and shea butter. Go Monday to Saturday and keep your phone close. The Artists Alliance Gallery in Omanye House has Ghanaian paintings, carvings and crafts over several floors and is free to enter. Osu and Labone have most of the restaurants and nightlife; try jollof, kelewele and grilled tilapia around Oxford Street.

                    ## Cape Coast and Elmina

                    Cape Coast is roughly 140 to 145 km west of Accra, two and a half to three and a half hours by road and more in traffic. Leave early to get past Kasoa before the traffic builds. You can go with a hired driver or take an Intercity STC or VIP bus.

                    Cape Coast Castle was a centre of the transatlantic slave trade. The guided tour, about an hour, takes you through the dungeons, the condemned cell and the Door of No Return. Many visitors find it upsetting, so leave time afterwards. Non-Ghanaian adults pay GH₵80, and it opens daily from 9:00 AM to 4:30 PM. Outside, young men may ask for your name and address to "write you a letter" and later ask for money; politely decline.

                    {{destination:cape-coast-castle}}

                    Kakum National Park is about 30 km inland. Its canopy walkway has seven bridges about 40 m above the rainforest. Arrive at opening, around 7:00 to 8:00 AM, when it's cooler, the birds are active and the school groups haven't arrived. You pay a park gate fee plus a canopy ticket. Our trip budgets roughly GH₵135 to 165 for both, but sources disagree, so confirm at the visitor centre. The bridges sway, which is hard for anyone with a serious fear of heights.

                    {{destination:kakum-national-park-canopy-walkway}}

                    Elmina Castle, begun by the Portuguese in 1482, is one of the oldest European buildings in sub-Saharan Africa. After the tour, walk along the lagoon past the painted fishing canoes, and spend the night at a beach hotel on the Elmina coast.

                    {{destination:elmina-castle}}

                    ## Labadi and the hills

                    Drive back east, about three to three and a half hours, and spend the evening at Labadi Beach, where La Pleasure Beach has music, drumming and grilled fish and is busiest at weekends. The currents are strong, so don't swim.

                    The last full day is in the hills, an hour to an hour and a half from Accra. Aburi Botanical Gardens has old trees, palm avenues and views, with woodcarvers on the road up. Then go on to Boti Falls, about 250 steps down to twin waterfalls, followed by a guided hike to Umbrella Rock. The falls are fullest during and after the rainy season.

                    ## Optional extensions

                    You can swap Boti for the Akosombo Dam, with a guided visit and lunch by the Volta, or stay a night at a riverside resort. With two more nights, fly north to Kumasi (under an hour; the road takes four to six hours) for the Manhyia Palace Museum, which has royal regalia and the history of the Golden Stool, and Kejetia Market, one of West Africa's largest, best visited with a guide.

                    ## Before you book

                    The trip's prices are rough USD estimates, and flights from Nigeria are extra. On the last day, leave plenty of time for Accra traffic on the way to Kotoka. Shito, shea butter, fabric and art are the usual things to take home.
                    """),

            // ── 7. Benin Republic ──────────────────────────────────────────────
            new(
                Slug: "lagos-to-cotonou-long-weekend-in-benin-republic",
                Title: "Lagos to Cotonou: A Long Weekend in Benin Republic",
                Excerpt: "Cotonou is about 37 km past the Seme-Kraké border, so southern Benin works as a long weekend from Lagos. Ganvié, Ouidah and Porto-Novo, plus the border, money and timing details to sort first.",
                MetaTitle: "Lagos to Cotonou: A Long Weekend in Benin Republic",
                MetaDescription: "Cross at Seme-Kraké or fly 40 minutes, then Ganvié's stilt village, Ouidah's Door of No Return and Porto-Novo: Benin Republic from Lagos.",
                CityName: null,
                CountryName: "Benin",
                TagKeys: new[] { "history", "cultural", "good_short_visit" },
                DestinationNames: new[]
                {
                    "Amazon Monument (Cotonou)", "Ganvié Lake Village", "Temple of Pythons (Ouidah)", "Sacred Forest of Kpassè",
                    "Ouidah Museum of History", "Route des Esclaves & Door of No Return", "Great Mosque of Porto-Novo",
                    "Musée Honmè (King Toffa's Palace)", "Songhai Centre", "Royal Palaces of Abomey", "Grand-Popo Beach & Bouche du Roy",
                },
                WishlistNames: new[] { "Benin Republic: Ganvié, Ouidah and Porto-Novo" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Cotonou is about 37 km past the Seme-Kraké border, which makes southern Benin one of the closest foreign trips you can take from Lagos. Our itinerary runs over five days, but its core (Ganvié, Ouidah and Porto-Novo) fits a long weekend if you're happy to move quickly.

                    ## Entry and money

                    Nigerians and other ECOWAS citizens don't need a visa for stays of up to 90 days. Carry a valid passport, ideally with at least six months left on it, and your yellow fever card, which is checked at the border.

                    Benin uses the West African CFA franc, not the naira, and small sites take cash only. Change some money before you go or at a licensed bureau or bank in Cotonou. ATMs in Cotonou generally take Visa, but Nigerian naira cards may not work abroad, so check with your bank. Street changers work at the border; count your money and change only small amounts with them. Spend or change any leftover CFA before you cross back.

                    ## Road or air

                    By road it's about 85 km from Lagos to the Seme border along the Badagry Expressway, then roughly 40 km to Cotonou. Allow three to five hours or more, depending on Lagos traffic, checkpoints and the queue at the joint border post. Leave early (our trip sets off at 6:00 AM) and have your documents ready.

                    To skip the border queue, Air Peace flies Lagos to Cotonou nonstop in about 40 minutes. Check the current schedule and fares.

                    ## Day 1: Cotonou

                    Start at the Amazon Monument by the Atlantic, a 30-metre bronze of an Agojie warrior, one of Dahomey's women soldiers. Dantokpa Market beside the lagoon is one of West Africa's largest open-air markets, but the government has announced plans to relocate it, so check it's still operating. For dinner, ask your hotel for a busy local spot serving grilled fish or chicken with akassa or pâte.

                    ## Day 2: Ganvié, then Ouidah

                    Drive 30 to 45 minutes to the Abomey-Calavi pier and buy your ticket at the official booth, not from the freelancers on the pier. A pirogue crosses Lake Nokoué to Ganvié, the stilt village known as the "Venice of Africa", and its floating market. About 10,000 CFA a person including the boat is widely reported, though sources range from 5,000 to 10,500 CFA. Ask before photographing people; some expect a small payment.

                    {{destination:ganvie-lake-village}}

                    Then drive about an hour west along the coast road to Ouidah and rest through the hottest part of the day.

                    ## Day 3: Ouidah

                    The Temple of Pythons is an active Vodun sanctuary facing the basilica. You take your shoes off, and you can hold a royal python with the guide. Entry runs about 1,000 to 2,000 CFA, often with a separate photo fee. Nearby, the Sacred Forest of Kpassè has iroko trees and Vodun statues in a grove named after Ouidah's founding king.

                    {{destination:temple-of-pythons-ouidah}}

                    The Ouidah Museum of History, inside the old Portuguese fort, has been under restoration, so confirm it's open before you go.

                    In the late afternoon, walk the 4 km Route des Esclaves with a guide, from the old auction square past the Tree of Forgetting and the Zoungbodji memorial to the Door of No Return on the beach. There's no entrance fee, but agree the guide's fee before you start. If you've visited the slave-route sites in Badagry, this walk completes the story from the other side of the border. Don't swim at the memorial beach, where the currents are strong.

                    {{destination:route-des-esclaves-and-door-of-no-return}}

                    Ouidah's Vodun Days festival is in early January, announced for 2 to 9 January 2027. The town fills up, so book early if your dates overlap.

                    ## Day 4: Porto-Novo

                    Porto-Novo, the capital, is about one and a half to two hours east of Ouidah through Cotonou. The Great Mosque has an Afro-Brazilian facade built by returnees from Brazil, in the same style as buildings in Lagos's Brazilian Quarter. Dress modestly and avoid prayer times. Musée Honmè, King Toffa's palace, runs guided tours of the royal courtyards.

                    {{destination:great-mosque-of-porto-novo}}

                    ## Day 5: Songhai and home

                    The Songhai Centre is a sustainable farm founded by a Nigerian-born priest. Guided tours take about an hour; start early to avoid the heat, then buy juice, honey or yoghurt at the farm shop and eat at the on-site maquis. Porto-Novo is the closest stop to the border, but still allow three to five hours or more to reach central Lagos.

                    ## Fitting it into a long weekend

                    With three days, drop the night in Cotonou. Cross early and go straight to Ganvié, then sleep in Ouidah. Spend the second day on the temple, the forest and the Route des Esclaves, and drive to Porto-Novo on the third morning for the mosque and Songhai before heading back. You'll spend more of the trip in the car, and the border can add hours in either direction.

                    ## Extensions

                    The UNESCO-listed Royal Palaces of Abomey are about three hours inland each way. Restoration work is under way, so check what's open. Grand-Popo, about 40 minutes west of Ouidah, has a beach and boat trips to the Bouche du Roy.

                    Prices on our trip are rough USD estimates, and opening hours in Benin aren't always published, so confirm them locally.
                    """),

            // ── 8. Zanzibar ────────────────────────────────────────────────────
            new(
                Slug: "zanzibar-in-six-days",
                Title: "Zanzibar in Six Days",
                Excerpt: "A six-day route round Zanzibar from Nigeria: two days in Stone Town, the north-coast beaches at Nungwi and Kendwa, snorkelling at Mnemba and the lagoon at Paje, plus the visa and insurance to sort first.",
                MetaTitle: "Zanzibar in Six Days: A Route from Stone Town to Paje",
                MetaDescription: "Six days on Zanzibar for Nigerian travellers: Stone Town, Nungwi, Mnemba snorkelling, Jozani and Paje, with eVisa, insurance and flight tips.",
                CityName: null,
                CountryName: "Tanzania",
                TagKeys: new[] { "beach", "history", "romantic", "relaxing" },
                DestinationNames: new[]
                {
                    "Stone Town of Zanzibar", "Forodhani Gardens Night Food Market", "Anglican Cathedral & Former Slave Market",
                    "Freddie Mercury Museum", "Prison Island (Changuu)", "Kizimbani Spice Farms", "Nungwi Beach", "Mnemba Atoll",
                    "Kendwa Beach", "Jozani Chwaka Bay National Park", "Paje & Jambiani Beaches", "The Rock Restaurant",
                    "Safari Blue, Menai Bay",
                },
                WishlistNames: new[] { "Zanzibar in Six Days: Stone Town to Paje" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Zanzibar takes more planning from Nigeria than Accra or Cotonou: there are no direct flights, and you need a visa and a specific insurance certificate before you leave. Once you're there, Unguja, the main island, is small enough to see a good deal of in six days. This route spends two days in Stone Town, two on the north coast and the last two on the south-east lagoon.

                    ## Before you fly

                    Nigerians need a Tanzanian eVisa. Apply at the official portal (visa.immigration.go.tz) well ahead, since processing can take around 10 days; the single-entry tourist visa costs about US$50. In September 2026 Tanzania took Nigeria off its "referred visa" list, so the extra clearance from the Commissioner General of Immigration is no longer needed.

                    Every foreign visitor must also buy Tanzania's mandatory inbound travel insurance, about US$44 per adult and US$22 per child aged 3 to 17. If your trip starts in Zanzibar, buy it from the Zanzibar Insurance Corporation at inbound.visitzanzibar.go.tz. Your own travel insurance doesn't count. Carry a yellow fever certificate as well. Requirements change, so check official sources before booking.

                    To get there, connect through Nairobi (Kenya Airways), Addis Ababa (Ethiopian), Doha or Dubai to Abeid Amani Karume International Airport, 15 to 20 minutes from Stone Town. The other option is to fly to Dar es Salaam and take a fast ferry (Azam Marine or Kilimanjaro, roughly 1.5 to 2 hours) to Stone Town port.

                    ## Days 1 and 2: Stone Town

                    Stone Town, the old quarter of Zanzibar City, is a UNESCO World Heritage Site of coral-stone houses, carved doors and narrow lanes. A two- to three-hour walk with a licensed guide costs about US$15 to $30 a person. You'll pass the Old Dispensary, Darajani market and the outside of the House of Wonders, closed since part of it collapsed in 2020. Ask your guide whether the Old Fort has reopened after its 2026 renovation.

                    {{destination:stone-town-of-zanzibar}}

                    Most Zanzibaris are Muslim. In Stone Town and the villages, cover shoulders and knees, and keep swimwear for the beach or the hotel. Download an offline map, because the lanes are easy to get lost in.

                    At sunset, go to the Forodhani Gardens night market on the waterfront for seafood skewers, Zanzibar pizza and sugar-cane juice. Agree prices before the vendor starts cooking.

                    {{destination:forodhani-gardens-night-food-market}}

                    On the second morning, visit the Anglican Cathedral, built on Zanzibar's last open slave market, with its slave-trade exhibition and underground holding chambers. Allow time, as many visitors find it hard going. The Freddie Mercury Museum on Kenyatta Road opens around 10:00 AM; the Queen singer was born in Stone Town in 1946. In the afternoon, a 20- to 30-minute boat ride takes you to Prison Island (Changuu) to see giant tortoises, some over 100 years old.

                    ## Days 3 and 4: Nungwi and Kendwa

                    On the way north, stop at a spice farm near Kizimbani to taste cloves, nutmeg, cinnamon and vanilla, often followed by a Swahili lunch. Nungwi, at the northern tip, is an hour to an hour and a half from Stone Town, and the north coast is swimmable at most tides. Take a sunset dhow cruise along the coast that evening.

                    Day 4 starts early with a boat to Mnemba Atoll, a protected reef with coral, turtles and often dolphins. Shared snorkelling trips cost about US$40 to $80, and the Mnemba conservation fee is about US$25 per non-resident adult, so check whether your tour includes it. The crossing can be choppy. Choose an operator whose boats keep their distance from the dolphins.

                    {{destination:mnemba-atoll}}

                    Spend the afternoon on the wide beach at Kendwa, a short ride south of Nungwi. On full-moon weekends there's a beach party after dark.

                    ## Day 5: Jozani and Paje

                    Drive south, about 1.5 to 2 hours, and stop at Jozani forest to see the endangered red colobus, a monkey found only on Zanzibar, and walk the mangrove boardwalk. Entry is about US$10, usually with a guide included. Bring mosquito repellent.

                    {{destination:jozani-chwaka-bay-national-park}}

                    Paje sits on a wide, shallow lagoon used by kitesurfing schools. The wind seasons run roughly June to September and December to February, and a beginner lesson is an option on the trip. The tide goes out a long way on this coast, so check a tide table before planning a swim or a lesson.

                    The Rock, a restaurant on a rock in the sea near Michamvi, runs set seatings. Book well ahead, ideally a couple of weeks in peak season, and expect roughly US$40 to $80 a person with a drink. Many reviewers rate the setting above the food. At high tide the restaurant is surrounded by sea; at low tide you can walk out to it.

                    {{destination:the-rock-restaurant}}

                    ## Day 6: Jambiani and home

                    At low tide, walk south along the lagoon to Jambiani and visit a women's seaweed cooperative. The airport is about an hour away. If you can add a day, Safari Blue is a dhow trip from Fumba into Menai Bay with a sandbank, snorkelling, mangroves and a seafood lunch.

                    ## Money

                    Hotels and tour operators widely accept US dollars. Carry Tanzanian shillings for markets, taxis and food stalls; Stone Town has ATMs, but they're scarcer elsewhere on the island. International flights aren't included in our trip price, and all of its prices are rough USD estimates.
                    """),

            // ── 9. Qatar ───────────────────────────────────────────────────────
            new(
                Slug: "trip-to-qatar-from-nigeria",
                Title: "Trip to Qatar from Nigeria: Visa, Flights and a 4-Day Doha Plan",
                Excerpt: "Planning a trip to Qatar from Nigeria: the Hayya e-visa and the extra checks Nigerian applicants have reported, flights from Lagos and Abuja, when to go, and a four-day Doha plan.",
                MetaTitle: "Trip to Qatar from Nigeria: Visa, Flights and a 4-Day Doha Plan",
                MetaDescription: "Qatar from Nigeria: the Hayya e-visa and stricter checks, Qatar Airways and RwandAir options, the best months and a four-day Doha itinerary.",
                CityName: null,
                CountryName: "Qatar",
                TagKeys: new[] { "cultural", "history", "shopping", "photography" },
                DestinationNames: new[]
                {
                    "Museum of Islamic Art", "National Museum of Qatar", "Souq Waqif", "Doha Corniche",
                    "Msheireb Downtown Doha", "Khor Al Adaid (Inland Sea)", "Katara Cultural Village", "The Pearl-Qatar",
                },
                WishlistNames: new[] { "Doha with a Kigali Stopover", "Kigali & Doha" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Qatar needs more paperwork than most short breaks from Nigeria, because Nigerian passport holders must get a visa before they fly. Once that's done, Doha is compact enough for four days. Most of what you'll want to see is on or near its seven-kilometre Corniche, and the Doha Metro runs from Hamad International Airport to the centre.

                    ## Getting there

                    Recent schedules show Qatar Airways flying non-stop between Doha and Lagos about twice a day, and Abuja several times a week, so you can fly direct from either city. Hamad International Airport is Qatar Airways' hub. Confirm current schedules before you book.

                    The other option is RwandAir, which flies Lagos to Kigali and on to Doha. The Kigali to Doha leg takes roughly six hours, with around six flights a week in 2026, and Doha is one hour ahead of Kigali. Lagos is the only Nigerian city on RwandAir's current booking site.

                    From the airport, take the Doha Metro Red Line or a taxi to your hotel.

                    ## The visa

                    Many nationalities enter Qatar visa-free, but Nigerian passport holders must get a visa before travel. Apply early for the tourist (A1) e-visa on the official Hayya portal, hayya.qa.

                    Since 2025, Nigerian media and travel agents have reported stricter checks on Nigerian applicants: a confirmed hotel booking (often made through Discover Qatar), a return ticket and proof of funds. Reports of extra restrictions on solo male travellers were disputed, so check the current rules. Qatar Airways passengers stopping over in Doha may be eligible for a Discover Qatar transit visa of up to 96 hours instead; check whether your passport qualifies.

                    Your passport should be valid for at least six months, and you should carry a printed copy of the approved visa when you fly. Requirements change, so check hayya.qa, visitqatar.com or the Embassy of Qatar in Abuja before you book.

                    ## When to go

                    The best months are November to March. From roughly June to September temperatures regularly pass 40°C, which makes the Corniche, the souq lanes and the desert hard going by day. Even in the cooler months, plan outdoor time for early morning and evening, and carry water.

                    ## Rules worth knowing

                    Qatar asks visitors to dress modestly in public places. Cover shoulders and knees in museums, souqs, malls and government buildings; women only need to cover their hair in mosques.

                    Friday changes the timetable. The Museum of Islamic Art opens from about 1:30 PM, after Friday prayers, and the National Museum and the Msheireb Museums open only in the afternoon. Many shops in Souq Waqif close for Friday midday prayers.

                    Alcohol is served only in licensed hotel bars and restaurants, so you won't find it in ordinary restaurants or the souq, and public drunkenness is an offence.

                    ## Four days in Doha

                    This plan follows the Qatar days of our "Doha with a Kigali Stopover" trip.

                    ### Day 1: Arrive and Souq Waqif

                    Drop your bags and go to Souq Waqif, Doha's old market, rebuilt in traditional style in the 2000s. It's free to wander and busiest after dark, with spices, oud, textiles, shisha cafés and Middle Eastern restaurants, and the Falcon Souq next door. A late dinner costs roughly QR 50 to 150, and karak tea a few riyals. Bargaining is normal in many shops; check the quality before buying gold or perfume.

                    ### Day 2: Museums and the Corniche

                    Start at the Museum of Islamic Art, I. M. Pei's building on its own island off the Corniche, with more than a thousand years of Islamic art from Spain to Central Asia. Entry is QR 50 for non-resident adults and free for children 16 and under. It closes one weekday and sources disagree on which, so check mia.org.qa before you go. Afterwards, walk out to MIA Park for the view of the West Bay towers.

                    In the afternoon, go to the National Museum of Qatar, Jean Nouvel's cluster of discs modelled on the desert rose crystal. Its galleries follow the country from Bedouin life and pearl diving to oil and gas, and wrap around a restored palace. It also costs QR 50 and it's large, so allow at least two hours. Tuesday is commonly listed as its closing day; confirm on qm.org.qa.

                    {{destination:national-museum-of-qatar}}

                    At sunset, walk a stretch of the Corniche and take a short dhow cruise to see the skyline lit up. Shared rides cost roughly QR 25 to 60 a person and private boats QR 150 to 400. Agree the price and length before you board, and ask for life jackets.

                    {{destination:doha-corniche}}

                    ### Day 3: Msheireb and the Inland Sea

                    In the morning, visit the Msheireb Museums, four restored heritage houses next to Souq Waqif. Bin Jelmood House covers the trade in enslaved East Africans around the Gulf, and Company House tells the story of the first oil workers. The museums have been free to enter, but the houses don't all keep the same hours, so check msheireb.com.

                    {{destination:msheireb-downtown-doha}}

                    At about 2:00 PM, leave for a desert safari to Khor Al Adaid, the Inland Sea, a protected reserve in the far south-east where the sea reaches into large dunes. Safaris let their tyres down near Sealine, just south of the port town of Mesaieed, before crossing the sand. One published example is QR 220 to 330 a person for a four-hour tour in a shared vehicle, and camel rides and sandboarding are often extra, so check what's included. Use a licensed operator with a well-maintained 4x4 and seatbelts. Dune bashing is bumpy; skip it if you're pregnant or have back problems.

                    {{destination:khor-al-adaid-inland-sea}}

                    Back in Doha, have dinner at Katara Cultural Village, a cultural district on the coast with an open-air amphitheatre, tiled mosques and seafront restaurants. It's free to walk around, and dinner costs roughly QR 80 to 250.

                    ### Day 4: The Pearl and home

                    Spend the morning on The Pearl, an artificial island north of West Bay. Walk the Porto Arabia marina and the canals of Qanat Quartier, and stop for coffee by the water, though cafés here cost more than in the souq. Go back to Souq Waqif in the late afternoon for spices, oud and dates to take home; many shops reopen around 4:00 PM after the afternoon break.

                    ## Adding Kigali on the way

                    If you fly RwandAir, you can ask for a multi-city Lagos-Kigali-Doha ticket and spend two nights in Kigali before Doha. Nigerians get a free visa on arrival in Rwanda; bring your yellow fever card. Our "Doha with a Kigali Stopover" trip is built that way, and "Kigali & Doha" spends four days in Rwanda first. Our separate guide to a Kigali stopover covers what to see there.
                    """),
            // ── 10. Mombasa trip ───────────────────────────────────────────────
            // Facts from MombasaSeeder (destinations, wishlist, Kenya spec) and the research cited in
            // its header comment: NMK 2026 fee list (Fort Jesus), KWS October 2025 fees (marine park),
            // etakenya.go.ke (Nigerians eTA-exempt up to 60 days), gov.uk FCDO, the founder and visasnews.com
            // (travel health insurance for every traveller, Oct 2026), CDC (yellow fever), kayak.com and kenya-airways.com
            // (Lagos-Nairobi-Mombasa routings), the-star.co.ke (SGR), travel4purpose.com (ChaQula),
            // wildwaterskenya.com, weatherspark.com. Rates: ~KSh 130/US$ and ~₦1,363/US$ (mid-Nov 2026).
            new(
                Slug: "trip-to-mombasa-kenya-what-to-see-eat-and-do",
                Title: "Trip to Mombasa, Kenya: What to See, Eat and Do",
                Excerpt: "A five-day plan for Mombasa from Nigeria: Fort Jesus and Old Town food, Haller Park and Mamba Village, a glass-bottom boat on the reef and evenings on Mama Ngina Drive, with the prices our founder paid.",
                MetaTitle: "Trip to Mombasa, Kenya: What to See, Eat and Do",
                MetaDescription: "Plan a trip to Mombasa from Nigeria: flights, entry rules, Fort Jesus, Old Town food, Haller Park, the marine park and the prices our founder paid.",
                CityName: "Mombasa",
                CountryName: "Kenya",
                TagKeys: new[] { "beach", "food_focused", "history", "family_friendly", "cultural" },
                DestinationNames: new[]
                {
                    "Mama Ngina Waterfront & Lighthouse", "Fort Jesus Museum", "Mombasa Old Town", "Nalka Café",
                    "Forodhani Restaurant, Mombasa Old Town", "Wild Waters Kenya", "Haller Park", "Mamba Village Centre",
                    "Mombasa Marine National Park and Reserve", "Mombasa Tusks", "Marikiti Market",
                    "Siri Guru Singh Sabha Gurdwara", "CityBlue Creekside Hotel & Suites",
                },
                WishlistNames: new[] { "Mombasa: Old Town, Fort Jesus and the North Coast" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Mombasa is Kenya's main port and its second-largest city, on the Indian Ocean coast. Old Town and Fort Jesus are on Mombasa Island, and the beach hotels, Haller Park and the marine park are a short drive north in Nyali and Bamburi. This guide follows our five-day "Mombasa: Old Town, Fort Jesus and the North Coast" trip, which is built from our founder's own list, and uses the prices our founder paid in Kenyan shillings (KSh). At mid-November 2026 rates, KSh 1,000 is roughly ₦10,500 or US$8.

                    ## Getting there and getting in

                    There are no nonstop flights from Nigeria to Mombasa. Kenya Airways flies nonstop from Lagos to Nairobi in about five hours and 20 minutes and connects to Moi International Airport in Mombasa; the quickest connections take about eight and a half hours in total. Ethiopian flies via Addis Ababa from both Lagos and Abuja. If you want to see Nairobi first, the Madaraka Express train runs from Nairobi to Mombasa in about six hours.

                    Nigerians don't need a Kenyan eTA for visits of 60 days or less. Since October 2026, though, every traveller to Kenya, Nigerians included, must buy government travel health insurance online at insurance.ecitizen.go.ke before arrival. It cost about US$52 including fees at launch, and your own travel insurance doesn't replace it. Kenya also asks travellers coming from Nigeria for a yellow fever certificate.

                    At the airport, buy a Safaricom SIM and register M-Pesa with your passport. Most of Mombasa runs on M-Pesa, and you'll get around by tuk-tuk, Uber and Bolt. Bolt added tuk-tuks to its app in 2026.

                    ## Where to stay

                    Our trip uses CityBlue Creekside Hotel & Suites, formerly the Best Western Plus Creekside, on Tudor Creek beside Nyali Bridge. Fort Jesus is about 5 km away and Haller Park and Mamba Village about 8 km, so neither half of the city is a long ride. Online rates started at about US$40 to US$50 a night in late 2026.

                    ## Evenings on Mama Ngina Drive

                    On your first evening, go to Mama Ngina Waterfront on the south side of the island. It's a free seafront park with a promenade about 2 km long, facing the channel where ships come into the port. Locals call the northern end "Lighthouse", and that's where our founder goes for the sea breeze and the street food: cassava crisps (mihogo), fried cassava and fresh coconut (madafu). Go from late afternoon and stay in the lit, busy stretches. Don't swim; it's a shipping channel.

                    {{destination:mama-ngina-waterfront-and-lighthouse}}

                    ## Fort Jesus and Old Town

                    Start Day 2 at Fort Jesus at 8:30 AM, when it opens and before the heat. The Portuguese built it in 1593, and it is now a museum and a UNESCO World Heritage Site. Our founder paid KSh 550, which is the Kenyan citizen rate; the museum's 2026 price list puts Nigerians and other African visitors at US$9 an adult. Tickets are cashless through eCitizen, so buy yours on your phone and bring your passport.

                    {{destination:fort-jesus-museum}}

                    Old Town starts at the fort and costs nothing to walk. Follow Ndia Kuu, the old main street, past carved doors and balconies down to the Old Port, and eat as you go: mahamri, viazi karai, mishkaki and small cups of Swahili coffee. Old Town is mostly Muslim, so cover your shoulders and knees. Our founder likes Nalka Café, a small café in a coral-stone building near Ndia Kuu. It doesn't publish prices or hours, so check Google Maps first.

                    {{destination:mombasa-old-town}}

                    For dinner, Forodhani Restaurant is about 200 m from the fort by the Old Port, serving seafood and Swahili dishes such as biryani, pilau and samaki wa kupaka (fish in coconut sauce). Mains were about US$9 to US$16 in 2026, and seafood can take 30 to 40 minutes, so order early. The Forodhani night food market you may have heard of is in Stone Town, Zanzibar; in Mombasa the name belongs to this restaurant.

                    If you'd like someone to explain the street food, ChaQula, a Mombasa food creator, leads a two-hour tour sold by Travel4Purpose from about US$30. It starts outside Fort Jesus at about 4:00 PM and ends with coconut water at Mama Ngina Waterfront. Our trip puts it on the evening of Day 4.

                    ## A family day in Nyali and Bamburi

                    Day 3 stays on the mainland north of the island. Start at Wild Waters on Links Road when it opens at 10:00 AM. It has more than 15 slides and a lazy river, and our founder paid KSh 1,700; the current price list shows KSh 2,000 for non-residents. It opens Tuesday to Sunday, and on Mondays only in Kenyan school and public holidays. You can't bring in outside food.

                    Haller Park in Bamburi is a nature park made from an old cement quarry. Our founder paid KSh 1,000 to enter and KSh 100 for pellets to feed the animals. Published gate prices for non-East Africans are higher, at KSh 2,200, so carry more than you plan to spend. Get there by 2:30 PM for the giraffe feed at 3:00 PM, the hippos at 4:00 PM and the crocodiles at 4:30 PM.

                    {{destination:haller-park}}

                    Finish at Mamba Village, a crocodile farm off Links Road in Nyali, in time for the 5:00 PM feeding. Our founder paid KSh 1,800. Camel and horse rides cost extra, and the restaurant serves crocodile meat if you want to try it.

                    ## The reef

                    Mombasa Marine National Park protects the coral reef off Nyali, Bamburi and Shanzu, and glass-bottom boats take you out to snorkel over it. Nigerians pay the Kenya Wildlife Service fee for African citizens, US$15 per adult, online through eCitizen. Boat trips negotiated on the beach run roughly KSh 1,000 to 2,500 a person. Before you set off, agree the price, how long the trip lasts and whether the park fee is included. Go out around low to mid tide, when the water inside the reef is calm and clear.

                    {{destination:mombasa-marine-national-park-and-reserve}}

                    ## The town centre

                    On the last morning, go early to the Mombasa Tusks on Moi Avenue, four crossed elephant tusks that make the city's best-known photo. Locals call them Pembe za Ndovu or Pembeni, which is what our founder calls them. Take your pictures from the pavement, because the road is busy.

                    From there, walk to Marikiti Market on Digo Road for fruit, spices and khangas, and keep your phone close in the crowds. Then walk 10 to 12 minutes to Siri Guru Singh Sabha, the Sikh gurdwara on Mwembe Tayari Road. Visitors of any faith are welcome. Take your shoes off, cover your head, and cover your shoulders and knees. If you arrive at lunchtime you may be offered langar, the free vegetarian meal the gurdwara serves to anyone.

                    ## When to go

                    Mombasa is hot and humid all year. June to October is the coolest and driest stretch. April to June brings the heaviest rain, and the short rains come around November and December. Whenever you go, plan walks for the morning and late afternoon.

                    For what to bring, see our post What to Pack for a Trip to Mombasa. For costs in naira, see How Much to Budget for a Trip to Mombasa from Nigeria.
                    """),

            // ── 11. Mombasa packing ────────────────────────────────────────────
            // Facts from MombasaSeeder and its cited research, plus: gov.uk FCDO Kenya entry requirements
            // (passport validity, travel health insurance); wwwnc.cdc.gov Kenya page (yellow fever
            // certificate for arrivals from risk countries; malaria prophylaxis for Mombasa); Nigeria
            // e-Yellow Card (valid 10 days after vaccination; bal.com, businessday.ng); weatherspark.com
            // (temperatures); paybillke.com/guides/mpesa-for-tourists (SIM and M-Pesa registration, card
            // surcharges); CIA World Factbook Kenya travel facts (Type G, 240 V).
            new(
                Slug: "what-to-pack-for-a-trip-to-mombasa",
                Title: "What to Pack for a Trip to Mombasa",
                Excerpt: "What to bring to Mombasa from Nigeria: your documents and yellow fever card, light clothes for the heat, modest clothes and a scarf for Old Town and the Sikh temple, swimwear and reef shoes, mosquito protection and how to pay with M-Pesa.",
                MetaTitle: "What to Pack for a Trip to Mombasa",
                MetaDescription: "A Mombasa packing list for Nigerian travellers: documents, clothes for the heat and for Old Town, swimwear, reef shoes, plugs, mosquito protection and money.",
                CityName: "Mombasa",
                CountryName: "Kenya",
                TagKeys: new[] { "beach", "cultural", "family_friendly" },
                DestinationNames: new[]
                {
                    "Siri Guru Singh Sabha Gurdwara", "Wild Waters Kenya", "Mombasa Marine National Park and Reserve",
                    "Mombasa Old Town", "Fort Jesus Museum", "Haller Park",
                },
                WishlistNames: new[] { "Mombasa: Old Town, Fort Jesus and the North Coast" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Mombasa is hot and humid all year, and a typical trip takes in Old Town's streets, a Sikh temple, a water park and a boat out to the reef. This list follows our five-day "Mombasa: Old Town, Fort Jesus and the North Coast" trip.

                    ## Documents

                    - A passport with at least six months' validity from the day you arrive and two blank pages.
                    - Your yellow fever card. Kenya asks for one from travellers aged one and over who arrive from Nigeria, and Nigeria issues it as an electronic e-Yellow Card. The certificate only becomes valid 10 days after the vaccination, so don't leave it to the last week.
                    - The Kenyan travel health insurance certificate. Since October 2026 every traveller to Kenya, Nigerians included, must buy this cover online at insurance.ecitizen.go.ke before arrival (about US$52 including fees at launch). Your own travel insurance doesn't count. Keep a copy on your phone and on paper.

                    You don't need an eTA: Nigerians are exempt for visits of 60 days or less. Kenya's entry rules have changed several times since 2024, so check etakenya.go.ke before you book.

                    ## Clothes for the heat

                    Pack light cotton or linen: loose trousers, long skirts, T-shirts and a couple of shirts with sleeves. From roughly December to April, average highs are above 31°C, and even August, the coolest month, averages about 27°C. Humidity is high all year, so clothes that dry quickly help.

                    The covered-up clothes are needed anyway. Old Town is mostly Muslim, and people expect shoulders and knees to be covered, so keep shorts and vests for the hotel and the beach. For the Sikh gurdwara on Mwembe Tayari Road, bring a scarf or a large handkerchief: everyone covers their head in the prayer hall and takes their shoes off at the door. The same scarf keeps the sun off your neck.

                    {{destination:siri-guru-singh-sabha-gurdwara}}

                    ## Swimwear and reef gear

                    Bring swimwear for the hotel pool, the reef and Wild Waters in Nyali. Wild Waters requires proper swimwear, with no denim or street clothes in the water, though leggings and long-sleeved tops are allowed on most slides. It doesn't allow outside food or drink, and a locker costs KSh 200 plus a deposit.

                    {{destination:wild-waters-kenya}}

                    For the marine park, a T-shirt or rash vest keeps the sun off your back while you snorkel. Pack reef shoes or old sandals, because at low tide you may walk across the reef flat to reach the boat, and a waterproof pouch for your phone. Snorkel gear usually comes with the boat trip, but ask.

                    {{destination:mombasa-marine-national-park-and-reserve}}

                    ## Shoes

                    Comfortable sandals will do for most of the trip. Old Town's lanes and the stone stairs at Fort Jesus are uneven, so add closed shoes or trainers with some grip. Haller Park's trails are gravel and sand.

                    ## Sun and mosquitoes

                    - Sunscreen (reef-safe for the marine park), a hat and sunglasses. The ramparts at Fort Jesus have very little shade.
                    - A refillable water bottle. Haller Park may not allow plastic bottles.
                    - Insect repellent. Mombasa is a malaria area, and the US CDC recommends antimalarial tablets for visitors, so talk to a doctor or pharmacist before you go.
                    - Any medicines you take, in their original packaging.

                    ## Plugs and power

                    Kenya uses Type G plugs, the British-style three-pin type, at 240 volts. That's the plug most Nigerian chargers already have; bring an adapter only if yours has round pins. A power bank helps on long days out.

                    ## Money

                    Mombasa mostly pays by M-Pesa. At the airport you can buy a Safaricom SIM for about KSh 100 and ask for M-Pesa registration, which needs your original passport and takes 15 to 30 minutes. Carry small shilling notes and coins for tuk-tuks, street snacks and market stalls, and bring a card for the hotel, though some hotels add 3 to 5% for card payments.

                    Fort Jesus and the marine park don't take cash. You pay online through eCitizen, so set it up on your phone before you go.

                    For prices in naira, see How Much to Budget for a Trip to Mombasa from Nigeria. For the day-by-day plan, see Trip to Mombasa, Kenya: What to See, Eat and Do.
                    """),

            // ── 12. Mombasa budget ─────────────────────────────────────────────
            // Facts from MombasaSeeder and its cited research, plus: kenya-airways.com/en_ng and
            // ethiopianairlines.com fare pages and kayak.com LOS-MBA (fares seen Nov 2026);
            // the-star.co.ke (SGR fares, 4 Dec 2025); Kayak snippet (Jambojet from ~US$58);
            // etakenya.go.ke/faqs (eTA US$30); visasnews.com (insurance ~US$52, Oct 2026);
            // booking.com/region/ke/mombasa (average nightly rates, Nov 2026); nation.co.ke (Bamburi
            // studio KSh 2,500, Aug 2025); Glovo listing (Taleh Food Court: mahamri KSh 30, biryani
            // KSh 660); kenyans.co.ke (Bolt tuk-tuk KSh 190 / boda KSh 160, Aug 2026; KSh ~129.96/US$
            // and ~KSh 175/£, Nov 2026); techeconomy.ng (₦1,363/US$ official, 18 Nov 2026);
            // en.wikipedia.org/wiki/Likoni_Ferry (free for pedestrians).
            // Founder-paid total: 1,800 + 1,000 + 100 + 550 + 1,700 = KSh 5,150 (~₦54,000, ~US$40).
            // Published-rate total: 1,800 + 2,200 + 100 + ~1,170 (US$9) + 2,000 = ~KSh 7,270 (~₦76,000).
            new(
                Slug: "how-much-to-budget-for-a-trip-to-mombasa-from-nigeria",
                Title: "How Much to Budget for a Trip to Mombasa from Nigeria",
                Excerpt: "What a five-day Mombasa trip costs from Nigeria in late 2026: flights from Lagos and Abuja, Kenya's travel health insurance, the attraction prices our founder paid in shillings with naira and dollar conversions, hotels, food and transport.",
                MetaTitle: "How Much to Budget for a Trip to Mombasa from Nigeria",
                MetaDescription: "Rough costs for a Mombasa trip from Nigeria: flights, entry, attraction prices in KSh, naira and dollars, hotels, food and transport, checked November 2026.",
                CityName: "Mombasa",
                CountryName: "Kenya",
                TagKeys: new[] { "budget_friendly", "beach", "family_friendly", "food_focused" },
                DestinationNames: new[]
                {
                    "Fort Jesus Museum", "Mamba Village Centre", "Wild Waters Kenya", "Haller Park",
                    "CityBlue Creekside Hotel & Suites", "Mombasa Marine National Park and Reserve",
                    "Forodhani Restaurant, Mombasa Old Town", "Mama Ngina Waterfront & Lighthouse", "Mombasa Old Town",
                },
                WishlistNames: new[] { "Mombasa: Old Town, Fort Jesus and the North Coast" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    The prices in this post come from our founder's own trip to Mombasa and from published prices we checked in November 2026. Treat every figure as rough, because fares, fees and exchange rates all move. Conversions use about KSh 130 to US$1 (the Central Bank of Kenya rate in early November 2026) and ₦1,363 to US$1 (the official rate on 18 November 2026), which works out at about ₦10.5 per shilling.

                    ## Flights

                    Flights are the biggest cost. There are no nonstop flights from Nigeria to Mombasa, so you'll change in Nairobi on Kenya Airways or in Addis Ababa on Ethiopian. In November 2026, Kenya Airways' own site showed economy returns from Lagos to Mombasa of about ₦1.02 million to ₦1.15 million, and Ethiopian's started at about ₦1.08 million. From Abuja, Ethiopian started at about ₦1.26 million. Kayak's figures were similar in dollars, typically US$700 to US$870 return from Lagos, with April usually the cheapest month and January the dearest.

                    If you fly into Nairobi first, the Madaraka Express train to Mombasa cost KSh 1,500 (about ₦15,750) one way in economy in late 2025, and Jambojet flights start at around US$58 one way.

                    ## Entry costs

                    Nigerians don't pay for a Kenyan eTA, because Nigerians and most other African nationals visiting for 60 days or less are exempt; for nationalities that need one, it costs US$30. Every traveller, Nigerians included, must buy Kenya's government travel health insurance before arrival on insurance.ecitizen.go.ke. It cost about US$52 including fees (roughly ₦71,000) at launch and covers 90 days, and your own travel insurance doesn't replace it. You also need a yellow fever card to arrive from Nigeria, so budget for the vaccine if you don't have one. Ordinary travel insurance for cancellations and theft is optional but worth having.

                    ## Attractions

                    These are the prices our founder paid. Where the published price for a Nigerian visitor is different, it's in the last column.

                    | Place | Founder paid | About ₦ | About US$ | Published rate for Nigerians |
                    |---|---|---|---|---|
                    | Mamba Village | KSh 1,800 | ₦18,900 | US$14 | Older listings show less |
                    | Haller Park entry | KSh 1,000 | ₦10,500 | US$8 | KSh 2,200 (non-East African) |
                    | Haller Park feeding pellets | KSh 100 | ₦1,050 | US$1 | Not published |
                    | Fort Jesus | KSh 550 | ₦5,800 | US$4 | US$9 (rest of Africa) |
                    | Wild Waters | KSh 1,700 | ₦17,900 | US$13 | KSh 2,000 (non-resident) |
                    | Old Town, Mama Ngina Waterfront, Mombasa Tusks, Sikh gurdwara | Free | Free | Free | Free |

                    At our founder's prices that comes to KSh 5,150, about ₦54,000 or US$40. At the published rates it's nearer KSh 7,300, about ₦76,000.

                    {{destination:fort-jesus-museum}}

                    {{destination:mamba-village-centre}}

                    Add the marine park if you plan to snorkel. Nigerians pay the Kenya Wildlife Service fee for African citizens, US$15 per adult (about ₦20,400), through eCitizen, and then pay for the boat. On the beach, glass-bottom and snorkel trips run roughly KSh 1,000 to 2,500 a person (₦10,500 to ₦26,000). Tours booked ahead with the park fee included cost about US$50 to US$70.

                    ChaQula's street food tour of Old Town starts at about US$30 a person (about ₦41,000).

                    {{destination:wild-waters-kenya}}

                    ## Accommodation

                    CityBlue Creekside Hotel & Suites, by Nyali Bridge, started at about US$40 to US$50 a night for a room in online listings in late 2026, roughly ₦55,000 to ₦68,000. Shared between two people, four nights there costs each person about ₦110,000 to ₦136,000.

                    {{destination:cityblue-creekside-hotel-and-suites}}

                    Booking.com's Mombasa averages in November 2026 were about £58 a night for a three-star hotel, £80 for a four-star and £187 for a five-star, or roughly ₦105,000, ₦145,000 and ₦345,000. Many of the beach hotels in Nyali and Bamburi are at the top of that range. At the cheap end, a Nation report in 2025 mentioned a studio flat in Bamburi at KSh 2,500 a night (about ₦26,000). Prices rise in April, August and December, when Kenyan schools are on holiday.

                    ## Food

                    Street food is cheap. One Mombasa food-court listing showed mahamri at KSh 30 and beef biryani at KSh 660, and the snacks on Mama Ngina Drive cost small amounts. A main course at Forodhani Restaurant in Old Town runs about US$9 to US$16, plus drinks. As a rough guide, allow US$15 to US$40 a day per person for food (about ₦20,000 to ₦55,000), depending on how often you sit down to eat.

                    ## Getting around

                    Tuk-tuks, boda bodas and Uber and Bolt cars are the usual ways around. In 2026 a Bolt tuk-tuk from Mwembe Tayari in the town centre to Mama Ngina Waterfront cost about KSh 190 (₦2,000), and a boda boda about KSh 160. Rides between the island and Nyali or Bamburi cost more, so check the price in the app before you book. We'd allow KSh 1,000 to 2,000 a day for rides (₦10,500 to ₦21,000). The Likoni ferry is free for foot passengers.

                    ## A rough total

                    For five days and four nights, sharing a room, one person might spend roughly:

                    | Item | Rough cost per person |
                    |---|---|
                    | Return flight from Lagos | ₦1,000,000 to ₦1,200,000 |
                    | Kenyan travel health insurance | about ₦71,000 |
                    | Four nights at CityBlue Creekside, shared | ₦110,000 to ₦136,000 |
                    | Attractions, including the reef trip | ₦85,000 to ₦125,000 |
                    | Food | ₦100,000 to ₦275,000 |
                    | Local transport | ₦40,000 to ₦85,000 |

                    That adds up to about ₦1.4 million to ₦1.9 million per person, most of it the flight. From Abuja, add the difference in fare.

                    For what to bring, see What to Pack for a Trip to Mombasa. For the day-by-day plan, see Trip to Mombasa, Kenya: What to See, Eat and Do.
                    """),

            // ── 13. Diani: where to stay ───────────────────────────────────────
            // Facts from DianiSeeder (stay destinations, wishlist, Kenya spec) and the research cited in
            // its header comment: boxohotels.com and Cloudbeds; staylala.com and Cloudbeds; expertafrica.com
            // (Soroi Blue, Swahili Beach, Msambweni Beach House); safari.com/soroi-blue-diani (2026-27 rates);
            // go2africa.com (Msambweni 2027 guide price); eastafricanretreats.com (Pandanas and Azhari rates
            // valid to 7 Jan 2028); airbnb.com/rooms/22458043 (Azhari rating); Expedia snippet (Swahili Beach,
            // Feb 2025); lonelyplanet.com (Galu "more secluded and less developed"); bestkenya.ke and
            // Tripadvisor reviews (seaweed months). Rates: ~KSh 130/US$ and ~₦1,363/US$ (mid-Nov 2026).
            new(
                Slug: "where-to-stay-in-diani-beach-kenya",
                Title: "Where to Stay in Diani Beach, Kenya: Hotels, Villas and Beach Houses",
                Excerpt: "Where to stay in Diani Beach, Kenya: Boxo Diani's container rooms and LalaGalu's cabins at Galu, Soroi Blue and Swahili Beach Resort further north, two private villas for couples and Msambweni Beach House, with rough nightly prices checked in November 2026.",
                MetaTitle: "Where to Stay in Diani Beach, Kenya: Hotels, Villas and Beach Houses",
                MetaDescription: "Seven places to stay in Diani Beach and Msambweni, from a container hotel at Galu to private villas for two, with rough 2026 prices and how to choose.",
                CityName: "Diani",
                CountryName: "Kenya",
                TagKeys: new[] { "beach", "couple_friendly", "premium", "budget_friendly", "relaxing" },
                DestinationNames: new[]
                {
                    "Boxo Diani", "LalaGalu", "Swahili Beach Resort", "Soroi Blue", "Pandanas Beach Villa",
                    "Azhari Beach Villa", "Msambweni Beach House",
                },
                WishlistNames: new[] { "48 Hours in Diani Beach" },
                EventSlugs: Array.Empty<string>(),
                Body: """
                    Diani Beach is on Kenya's south coast in Kwale County, roughly an hour by road south of Mombasa's airport or a short flight from Nairobi to Ukunda airstrip. Our founder's list of places to stay there runs from a hotel built out of shipping containers to a clifftop house 30-odd kilometres further south. The prices below are rough nightly figures we found in November 2026, in US dollars because that's how most of these places publish them. At the time, US$100 was about KSh 13,000, or roughly ₦136,000.

                    ## Galu, the quieter south end

                    Galu, often called Galu Kinondo, is the southern end of Diani Beach. Lonely Planet describes it as more secluded and less developed than Diani itself, and two of our founder's picks stand side by side on the same plot there.

                    Boxo Diani is the cheapest place on this list. It's built from 25 recycled shipping containers around a pool, under 100 m from the beach, and Boxo says it was designed for young, independent travellers. Rooms are air-conditioned doubles or quads with queen bunk beds, so four friends can split one room. Booking-site prices in 2026 worked out at about US$50 to US$60 a night for a room before taxes, with breakfast included. The FATFATs bar and restaurant on site is open to the public until 10:00 PM, so don't expect early nights.

                    {{destination:boxo-diani}}

                    LalaGalu, next door, has about 15 timber cabins with king beds, rain showers and private decks, and a pool under the palms a few steps from the sand. One two-bedroom family cabin has a kitchenette. Listings we found worked out at roughly US$100 to US$150 a night for two, but we couldn't confirm a 2026 rate, so check the booking page. It's the base we suggest in our 48-hour Diani plan, because it's quiet, mid-priced and about 1.5 km from The Salty Squid for dinner.

                    {{destination:lalagalu}}

                    ## Central and north Diani

                    The bigger hotels are in the middle and north of the beach, closer to Ukunda airstrip, Ali Barbour's Cave Restaurant and the Kongo River.

                    Swahili Beach Resort is the large five-star option, with about 140 rooms and suites in Swahili and Arabic-style buildings, pools that step down in tiers to the beach, a big spa and a gym. Expert Africa rates it best for children aged 8 and over, because the grounds are spread out and lifeguard cover at the pools is limited, and the beach in front gets narrow at high tide. A February 2025 listing started at about €385 a night for two. We'd budget US$300 to US$450 or more a night on half board and get the quote in writing.

                    {{destination:swahili-beach-resort}}

                    Soroi Blue is much smaller: a boutique hotel of about 11 rooms, run by the Soroi safari lodge group, with gardens that open onto the beach in the northern half of Diani. It has a pool, a spa and a restaurant, and can provide halal and kosher meals. The rooms cover couples, families and a two-room suite for up to four adults and a child. Rates are per person. Safari.com listed US$215 per person sharing per night from November to mid-December 2026, US$420 from January to March 2027 and US$595 over Christmas and New Year. Ask whether lunch is included.

                    {{destination:soroi-blue}}

                    ## Villas for two

                    Pandanas Beach Villa and Azhari Beach Villa are private one-bedroom villas on the beach in Diani, both let through East African Retreats at the same rates. Each has an air-conditioned bedroom, an outdoor jungle shower, a kitchen and its own plunge pool, with a private path to a beach lounge shared with other guests. Azhari's pool has a waterfall, there's a yoga deck, and the owner, Nicole Englefield, manages it on site; it was rated 4.99 from 112 reviews on Airbnb when we checked. Pandanas is listed as new and includes return transfers from Ukunda airstrip.

                    Both cost US$650 a night for the villa self-catering, US$710 with breakfast or US$850 half board, from 8 January to 15 December. Over Christmas and New Year it's US$900 to US$1,120. The minimum stay is two nights. The price is for the villa, not per person, and for a honeymoon these are the most private places on the list.

                    ## Msambweni Beach House

                    Msambweni Beach House is about 30 to 35 km south of Diani, roughly 45 minutes by road from Ukunda airstrip, on a low coral cliff above a quiet beach. There's a main house with three doubles and an Ocean Suite, and three private villas with their own pools, two with two bedrooms and one with three. Meals are full board. Go2Africa's 2027 guide price was about US$484 to US$704 per person sharing per night.

                    It suits a group of friends or a family taking a whole villa, or a couple who want to be far from everything. It's too far to drive up to Diani for dinner every night. Expert Africa says it was renamed Villa MB in 2022, and some booking sites list it as Nuri Boutique Hotel & Villas, so search all three names for recent reviews and confirm who you're booking with.

                    {{destination:msambweni-beach-house}}

                    ## How to choose

                    On a budget, or as a group of friends, Boxo Diani's quad rooms are the cheapest way to sleep near the beach. A couple who want somewhere quiet and mid-priced will do well at LalaGalu, with The Salty Squid down the road. With a bigger budget, couples should compare the Pandanas and Azhari villas, where the price covers two people, with Soroi Blue, where it's per person. Families who want everything on site will be happiest at Swahili Beach Resort, as long as the children are old enough for the spread-out grounds. A group that wants a whole house to itself should look at Msambweni Beach House.

                    As for the area, Galu is quieter and has the cheaper stays, and central and north Diani put you closer to Ali Barbour's, the Kongo River zipline and the Africa Pool at Tiwi.

                    Wherever you stay, check the tide. At high tide some stretches of beach, including the one in front of Swahili Beach Resort, get narrow, and at low tide the reef flats are exposed, so pack reef shoes for the sea urchins. Seaweed can wash up during the south-east monsoon, roughly May to September, and the amount changes from week to week, so read recent reviews if you're travelling then. December to March has the calmest sea. December and Easter are the busiest and most expensive weeks, so book early.

                    For the day-by-day plan, see our 48 Hours in Diani Beach trip. If you're adding Mombasa, our posts Trip to Mombasa, Kenya: What to See, Eat and Do and How Much to Budget for a Trip to Mombasa from Nigeria cover the city.
                    """),
        };
    }
}
