using System.Text.Encodings.Web;
using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.Infrastructure.Seeder; // ToTitleCase extension
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// "Bali Island Essentials" — a 5-day template wishlist across Ubud, Uluwatu, Nusa Penida and Seminyak.
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// All prices are rough USD approximations.
    /// </summary>
    public static class BaliSeeder
    {
        private static string BlobBase => SeedImages.BaseUrl;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            // Stored in jsonb, never rendered as raw HTML — keep accents/apostrophes readable.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Spec records
        // ═══════════════════════════════════════════════════════════════════════

        private sealed record LanguageSpec(string Name, string Code, (string Phrase, string Local)[] Phrases);

        private sealed record CurrencySpec(string Code, string Name, string Symbol, decimal RateToUsd);

        private sealed record CountrySpec(
            string Name, string Slug, string Continent, string Flag, string Visa, string Plugs,
            string DrivingSide, decimal TaxRate, string[] LanguageNames, string CurrencyCode, string TimeZone);

        private sealed record CitySpec(string Name, string CountryName, double Lat, double Lng, string Description);

        private sealed record DestSpec(
            string Region, string Name, string CountryName, string CityName, string[] LanguageNames,
            double Lat, double Lng, decimal AvgCost, string AccessibilityType, int Safety,
            string[] Categories, string[] Tags, string[] Alternates, string ShotContext, DescriptionJsonDto Desc);

        private sealed record ItemSpec(
            string Time, string Title, string Description, decimal Cost, string Badge,
            string? DestinationName = null, bool Optional = false);

        private sealed record DaySpec(int Number, string Title, string MorningCity, string AfternoonCity, string EveningCity, ItemSpec[] Items);

        private sealed record WishlistSpec(
            string Region, string Name, string CountryName, string Description, string ShortStory,
            string PeopleType, string[] Inclusions, string Keywords, string[] Vibes,
            decimal BasePricePerPerson, decimal Deposit, string Accommodation, string Transit,
            string Activities, bool IsFeatured, string Persona, DaySpec[] Days);

        private static DescriptionJsonDto Desc(
            string overview, string localPerspective, string directions, string whatToKnow,
            string thingsToBeWaryOf, string hiddenCost, string[] nearby, string bestTime,
            string crowd, string accessibility, string duration) => new()
            {
                Overview = overview,
                LocalPerspective = localPerspective,
                Directions = directions,
                WhatToKnow = whatToKnow,
                ThingsToBeWaryOf = thingsToBeWaryOf,
                HiddenCost = hiddenCost,
                NearbyComplements = nearby.ToList(),
                BestTimeToVisit = bestTime,
                CrowdLevel = crowd,
                Accessibility = accessibility,
                IdealDuration = duration
            };

        // ═══════════════════════════════════════════════════════════════════════
        //  Reference data
        // ═══════════════════════════════════════════════════════════════════════

        private const string Indonesia = "Indonesia";

        private const string Indonesian = "Indonesian";
        private const string Balinese = "Balinese";
        private const string English = "English";

        private const string Ubud = "Ubud";
        private const string Uluwatu = "Uluwatu";
        private const string Seminyak = "Seminyak";
        private const string NusaPenida = "Nusa Penida";

        private const string TegallalangName = "Tegallalang Rice Terraces";
        private const string MonkeyForestName = "Sacred Monkey Forest Sanctuary";
        private const string TirtaEmpulName = "Tirta Empul Temple";
        private const string UbudPalaceName = "Ubud Palace & Ubud Art Market";
        private const string UluwatuTempleName = "Uluwatu Temple & Kecak Fire Dance";
        private const string PadangPadangName = "Padang Padang Beach";
        private const string SeminyakBeachName = "Seminyak Beach";
        private const string KelingkingName = "Kelingking Beach";

        private static readonly LanguageSpec[] Languages =
        {
            new(Indonesian, "id", new[] { ("Hello", "Halo"), ("Thank you", "Terima kasih"), ("How much?", "Berapa harganya?") }),
            new(Balinese, "ban", new[] { ("Greeting", "Om Swastiastu"), ("Thank you", "Suksma") }),
            new(English, "en", Array.Empty<(string, string)>()),
        };

        private static readonly CurrencySpec[] Currencies =
        {
            new("IDR", "Indonesian Rupiah", "Rp", 16000m),
        };

        private static readonly CountrySpec[] Countries =
        {
            new(Indonesia, "indonesia", "Asia", "🇮🇩",
                "Visa on arrival / e-VOA is available for many nationalities, and foreign visitors to Bali also pay a separate tourist levy; requirements change, so check official requirements before travel.",
                "Type C/F", "Left", 0.11m, new[] { Indonesian, Balinese, English }, "IDR", "Asia/Makassar"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Ubud, Indonesia, -8.5069, 115.2625,
                "Bali's cultural heart in the central foothills — a town of temples, royal palace, art galleries, dance performances and the rice terraces and river valleys that surround it."),
            new(Uluwatu, Indonesia, -8.8291, 115.0849,
                "The limestone cliffs of the Bukit Peninsula in Bali's far south (Pecatu area), known for its clifftop sea temple, surf breaks and small beaches tucked below the cliffs."),
            new(Seminyak, Indonesia, -8.6913, 115.1683,
                "A stylish stretch of Bali's south-west coast with a long sandy beach, boutiques, cafés and some of the island's best-known sunset spots."),
            new(NusaPenida, Indonesia, -8.7275, 115.5444,
                "A rugged island south-east of Bali, reached by fast boat, famous for dramatic sea cliffs, viewpoints and manta-ray dive sites."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] BaliLangs = { Indonesian, Balinese, English };

        private const string TempleDress =
            "Temple Dress: Shoulders and knees should be covered, and a sarong and sash are required inside the temple grounds — they are usually available to borrow or rent at the entrance.";

        private static readonly DestSpec[] Destinations =
        {
            new("bali", TegallalangName, Indonesia, Ubud, BaliLangs, -8.4312, 115.2793,
                15m, "Car", 1,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "nature", "photography", "tourist_hotspot", "cultural" },
                new[] { "Tegalalang", "Tegalalang Rice Terrace", "Tegallalang Rice Terrace", "Ceking Rice Terraces" }, "Landscape",
                Desc(
                    "North of Ubud, the Tegallalang rice terraces step down both sides of a steep valley in layered green curves shaded by coconut palms. They are one of the most photographed views in Bali and a living example of subak, the island's centuries-old cooperative irrigation system that shares water between farmers through canals and water temples. Paths lead down into the terraces and across the valley, and the ridge above is lined with cafés looking out over the fields.\n\nWhat to Do: Walk the paths down through the terraces, photograph the valley from the ridge, and stop for a coffee or fresh coconut with a view.",
                    "For the farmers, these are working fields, not a backdrop — rice is planted and harvested here throughout the year, and the subak system ties farming to temple ceremonies. Locals suggest arriving early, when the light is soft and the tour groups haven't arrived.",
                    "Best Access: About 20-30 minutes' drive north of central Ubud on the main road towards Kintamani.\n\nBy Driver: Most visitors come with a hired driver or on a scooter; there is parking along the ridge road.\n\nOn Foot: Paths start from the cafés and entrances along the main road and descend into the valley.",
                    "Donations & Fees: Expect an entry fee and small donation points along the paths, where farmers ask for a contribution to cross their land — check ahead and carry small notes.\n\nSwings & Photo Spots: Commercial swings and nest photo spots are separate paid add-ons.\n\nHarvest Cycle: The terraces look different depending on where they are in the planting cycle — green, golden or freshly flooded.",
                    "Steep, Slippery Steps: Paths are steep and can be muddy after rain — wear shoes with grip.\n\nHeat: There is little shade on the lower paths by late morning — bring water and a hat.\n\nCrowds: Late morning to mid-afternoon gets very busy around the main viewpoints.",
                    "Entry / Donations: $1-$5.\nSwing or Photo Spot: $15-$35.\nDriver from Ubud (half day): $15-$35.\nCoffee or Coconut with a View: $2-$6.",
                    new[]
                    {
                        "Tirta Empul Temple: About 20-30 minutes' drive north-east.",
                        "Coffee Plantations: Several plantations along the road offer tastings.",
                        "Ubud Centre: About 20-30 minutes' drive south."
                    },
                    "Early Morning (sunrise to 9:00 AM); the drier months (roughly April to October) are the most comfortable.",
                    "Medium (5/10) early, High (8/10) late morning.",
                    "Rating: 4/10 — the ridge viewpoints are easy to reach, but the terrace paths are steep and uneven.",
                    "1 to 2 hours.")),

            new("bali", MonkeyForestName, Indonesia, Ubud, BaliLangs, -8.5188, 115.2585,
                10m, "Walk", 2,
                new[] { "nature_outdoor", "religious_site" },
                new[] { "nature", "family_friendly", "walkable", "tourist_hotspot", "photography" },
                new[] { "Monkey Forest Ubud", "Ubud Monkey Forest", "Mandala Suci Wenara Wana", "Sacred Monkey Forest" }, "Landscape",
                Desc(
                    "At the southern end of Ubud's main streets, the Sacred Monkey Forest Sanctuary is a patch of tall, mossy forest home to several hundred Balinese long-tailed macaques. Stone paths and bridges wind through the trees and down to a stream gorge, past moss-covered statues and Hindu temples that are still used by the local village. The sanctuary is managed by the community as both a conservation area and a spiritual site.\n\nWhat to Do: Walk the forest loops and the ravine path, watch the macaque troops go about their day, and admire the temple architecture from outside.",
                    "To the village of Padangtegal, the forest is a sacred place tied to the Hindu philosophy of harmony between people, nature and the divine. Locals advise treating the monkeys as wild animals, not pets — watch, don't touch.",
                    "Best Access: Walk from central Ubud — the main entrance is at the bottom of Monkey Forest Road.\n\nBy Car: Drivers can drop you near the entrance; parking in central Ubud is limited.",
                    "Entry Fee: A ticket is required; check ahead for current prices and opening hours.\n\nTemples: The temples inside are for worshippers; visitors view them from outside.\n\nStaff: Sanctuary staff are stationed along the paths — ask them if a monkey becomes a problem.",
                    "Grabbing Monkeys: Macaques snatch sunglasses, hats, phones and anything that looks like food — secure loose items and keep bags zipped.\n\nDon't Feed or Touch: Feeding and touching provoke bites and scratches; don't make eye contact or show your teeth.\n\nFood: Don't bring food or plastic bags into the forest.",
                    "Entry Ticket: $5-$10.\nDrinks nearby: $1-$3.\nTaxi in Ubud: $3-$8.",
                    new[]
                    {
                        "Ubud Palace & Art Market: About 15 minutes' walk north along Monkey Forest Road.",
                        "Monkey Forest Road Cafés: Plenty of places for a drink nearby.",
                        "Campuhan Ridge Walk: A short drive or longer walk across town."
                    },
                    "Morning (opening to 10:00 AM) when it's cooler and calmer.",
                    "High (7/10).",
                    "Rating: 5/10 — paved paths, but with steps and slopes down to the ravine.",
                    "1 to 1.5 hours.")),

            new("bali", TirtaEmpulName, Indonesia, Ubud, BaliLangs, -8.4153, 115.3153,
                12m, "Car", 1,
                new[] { "religious_site", "historical_tour" },
                new[] { "cultural", "history", "photography", "tourist_hotspot" },
                new[] { "Pura Tirta Empul", "Tirta Empul", "Tampaksiring Water Temple", "Holy Spring Water Temple" }, "Exterior",
                Desc(
                    "Pura Tirta Empul in Tampaksiring, north-east of Ubud, is one of Bali's most important water temples, built around a holy spring that bubbles up into a clear pool. Its best-known feature is the long purification pool where spring water pours from a row of carved spouts, and Balinese Hindus come here to bathe and pray for cleansing. The temple dates back around a thousand years and is still very much in active use.\n\nWhat to Do: Watch worshippers at the spouts, join the purification ritual respectfully if you wish (following the order and guidance given at the temple), and see the bubbling spring pool in the inner courtyard.",
                    "For Balinese Hindus, Tirta Empul is a deeply sacred place for melukat (spiritual cleansing), especially on important ceremony days. Locals ask visitors to follow the ritual properly — with the right clothing, in silence and with respect — or simply to watch.",
                    "Best Access: About 30-45 minutes' drive north-east of Ubud in Tampaksiring.\n\nBy Driver: Usually combined with the Tegallalang rice terraces on a half-day route.\n\nParking: There is a car park by the entrance, with a market walk to and from the temple.",
                    TempleDress + "\n\nPurification Ritual: Bathers wear a separate sarong in the water; ask at the temple about the correct order of the spouts — some are for specific purposes and are skipped.\n\nEntry Fee: A ticket is required; check ahead for current prices and hours.",
                    "Respect Ceremonies: On ceremony days some areas may be closed to visitors — never walk in front of people praying.\n\nMenstruation Rule: Balinese temples traditionally ask women who are menstruating not to enter.\n\nWet Clothes: If you bathe, bring a change of clothes and a towel.",
                    "Entry Ticket: $3-$6.\nSarong Rental for Bathing: $1-$4.\nLockers: $1-$3.\nDriver from Ubud (half day): $20-$40.",
                    new[]
                    {
                        "Tegallalang Rice Terraces: About 20-30 minutes' drive south-west.",
                        "Gunung Kawi Tampaksiring: Ancient rock-cut shrines a short drive away.",
                        "Coffee Plantations: Several along the road back to Ubud."
                    },
                    "Early Morning (opening to 10:00 AM) before the tour groups.",
                    "High (7/10), Very High on ceremony days.",
                    "Rating: 5/10 — mostly level courtyards with some steps; bathing involves stepping into cold, waist-deep water.",
                    "1 to 1.5 hours.")),

            new("bali", UbudPalaceName, Indonesia, Ubud, BaliLangs, -8.5069, 115.2625,
                15m, "Walk", 1,
                new[] { "landmark_monument", "market_street_life" },
                new[] { "cultural", "history", "art", "walkable", "budget_friendly", "local_favorite" },
                new[] { "Puri Saren Agung", "Ubud Royal Palace", "Puri Saren Ubud", "Pasar Seni Ubud", "Ubud Market" }, "Exterior",
                Desc(
                    "At Ubud's main crossroads, Puri Saren Agung is the palace of Ubud's royal family, whose patronage helped make the town a centre of Balinese art and dance. Visitors can walk into the outer courtyards to see the carved stone gates, gilded doors and pavilions, and in the evenings the courtyard often becomes the stage for traditional dance performances. Directly across the road is the Ubud Art Market (Pasar Seni), a maze of stalls selling sarongs, woven baskets, wood carvings, paintings and silver.\n\nWhat to Do: Explore the palace's outer courtyards, browse and bargain at the art market, and come back in the evening for a dance performance if one is scheduled.",
                    "The royal family still lives in parts of the palace, and it remains a focal point for the town's ceremonies. Locals shop at the market early in the morning, when it's busiest with produce; the souvenir stalls take over later in the day.",
                    "Best Access: In the centre of Ubud, at the junction of Jalan Raya Ubud and Monkey Forest Road — easy to reach on foot.\n\nBy Car: Central Ubud traffic is slow; get dropped off nearby and walk.",
                    "Palace Entry: The outer courtyards are generally free to visit; parts are private.\n\nDance Performances: Evening shows are ticketed and the programme varies — check ahead at the palace or with your accommodation.\n\nBargaining: Market prices start high; friendly negotiation is expected.",
                    "Respect: The palace is a residence and ceremonial site — dress modestly and don't enter roped-off areas.\n\nMarket Pressure: Stallholders can be persistent; a polite 'no thank you' (tidak, terima kasih) works.\n\nTraffic: Cross the main road carefully — it's busy with cars and scooters.",
                    "Palace Entry: Free.\nDance Performance Ticket: $6-$12.\nSouvenirs (sarongs, baskets): $3-$25.\nCarvings / Paintings: $10-$100+.",
                    new[]
                    {
                        "Sacred Monkey Forest Sanctuary: About 15 minutes' walk south.",
                        "Saraswati Temple (Pura Taman Saraswati): A short walk along Jalan Raya Ubud.",
                        "Ubud Restaurants & Cafés: All around the town centre."
                    },
                    "Morning (8:00 AM - 11:00 AM) for the market; early evening for a dance performance.",
                    "High (7/10).",
                    "Rating: 6/10 — courtyards are mostly level but have raised thresholds and steps; market aisles are narrow and crowded.",
                    "1.5 to 2 hours, plus an evening performance.")),

            new("bali", UluwatuTempleName, Indonesia, Uluwatu, BaliLangs, -8.8291, 115.0849,
                20m, "Car", 2,
                new[] { "religious_site", "viewpoint_scenic_spot", "activity_experience" },
                new[] { "cultural", "photography", "tourist_hotspot", "history" },
                new[] { "Pura Luhur Uluwatu", "Uluwatu Temple", "Pura Uluwatu", "Uluwatu Kecak Dance" }, "Exterior",
                Desc(
                    "Pura Luhur Uluwatu is one of Bali's key sea temples, perched on the edge of a sheer limestone cliff high above the Indian Ocean at the island's south-western tip. The inner temple is reserved for worshippers, but the cliff-top paths around it give spectacular views along the coast. At sunset, an open-air amphitheatre on the cliff hosts the Kecak fire dance, in which a large circle of chanting men tells an episode of the Ramayana as the sky changes colour behind them.\n\nWhat to Do: Walk the cliff paths for the views, watch the temple from outside, and stay for the Kecak performance at sunset.",
                    "For Balinese Hindus, Uluwatu is one of the directional temples that protect the island. Locals say the Kecak is touristy but genuinely moving — get there early to choose a seat facing the sunset.",
                    "Best Access: On the southern tip of the Bukit Peninsula; roughly 30-60 minutes' drive from Seminyak or Kuta depending on traffic.\n\nBy Driver: The easiest way, especially for the sunset show; traffic leaving after the Kecak can be slow.",
                    TempleDress + "\n\nKecak Tickets: The dance is ticketed separately from temple entry; seats fill up, so buy early on the day and check ahead for times.\n\nInner Temple: Only worshippers may enter the inner sanctum.",
                    "Monkeys: The macaques here are notorious for snatching sunglasses, hats and phones — keep them out of sight.\n\nCliff Edges: Stay behind the walls and fences — the drop is sheer.\n\nSun: The Kecak amphitheatre faces west, so the late-afternoon sun is strong before sunset.",
                    "Temple Entry: $3-$6.\nKecak Dance Ticket: $10-$20.\nDriver for the Evening: $20-$40.\nDinner nearby: $15-$40.",
                    new[]
                    {
                        "Padang Padang Beach: About 15 minutes' drive.",
                        "Jimbaran Bay: Seafood dinners on the beach, about 30 minutes' drive north.",
                        "Suluban Beach: Cave-framed surf beach nearby."
                    },
                    "Late Afternoon (from about 4:30 PM) to catch the cliff views and the sunset Kecak.",
                    "High (8/10) around sunset.",
                    "Rating: 4/10 — paved paths with many steps and slopes; amphitheatre seating is on stone tiers.",
                    "2.5 to 3 hours including the Kecak.")),

            new("bali", PadangPadangName, Indonesia, Uluwatu, BaliLangs, -8.8110, 115.1000,
                10m, "Car", 2,
                new[] { "beach_coast" },
                new[] { "beach", "relaxing", "photography", "local_favorite" },
                new[] { "Pantai Padang Padang", "Padang Padang", "Labuan Sait Beach" }, "Exterior",
                Desc(
                    "Padang Padang is a small, sheltered cove on the Bukit Peninsula, reached by a staircase that squeezes through a narrow cleft in the rocks. The white-sand beach is framed by limestone cliffs, and the reef off its western end is home to one of Bali's most famous surf breaks, best watched from the sand when the swell is big. On calm days the water near the shore is good for a swim.\n\nWhat to Do: Walk down through the rock passage, swim when conditions are calm, watch the surfers and relax on the sand.",
                    "Surfers know Padang Padang as a serious wave for experienced riders; families and beachgoers come for the pretty cove. Locals tip that it's best early, before day-trippers fill the small beach.",
                    "Best Access: On the coast road between Uluwatu and Bingin; the entrance and parking are by the bridge on the main road.\n\nBy Driver or Scooter: Most visitors arrive by car or scooter; there is little public transport.\n\nThe Stairs: A steep staircase through the rocks leads down to the beach.",
                    "Entry Fee: A small entry fee is collected at the top — check ahead.\n\nFacilities: Basic — small warungs, toilets and sunbed rental.\n\nTide: At high tide the beach becomes very narrow.",
                    "Reef & Currents: Rocks and reef lie close to shore and currents can be strong — swim only when it's calm, and not near the surf break.\n\nMonkeys: Macaques around the entrance may grab bags.\n\nCrowds: The small beach fills up by late morning.",
                    "Entry: $1-$3.\nSunbed / Umbrella: $3-$8.\nDrinks & Snacks at a Warung: $2-$6.\nSurf Lesson (beginner beaches nearby): $30-$50.",
                    new[]
                    {
                        "Uluwatu Temple: About 15 minutes' drive south-west.",
                        "Bingin Beach: Another cliff-backed beach a few minutes away.",
                        "Uluwatu Cafés: Clifftop cafés along the coast road."
                    },
                    "Morning (8:00 AM - 11:00 AM) for space on the sand; the dry season (roughly April-October) brings the big surf.",
                    "Medium (5/10), High (8/10) by midday.",
                    "Rating: 3/10 — reached by a steep, narrow staircase through the rocks.",
                    "2 to 3 hours.")),

            new("bali", SeminyakBeachName, Indonesia, Seminyak, BaliLangs, -8.6913, 115.1583,
                25m, "Walk", 1,
                new[] { "beach_coast", "viewpoint_scenic_spot" },
                new[] { "beach", "relaxing", "photography", "tourist_hotspot", "walkable" },
                new[] { "Pantai Seminyak", "Seminyak Beach Bali", "Petitenget Beach", "Double Six Beach" }, "Exterior",
                Desc(
                    "Seminyak Beach is part of the long, wide sweep of sand that runs up Bali's south-west coast, backed by beach clubs, bean-bag bars and hotels. It faces west over the Indian Ocean, making it one of the island's classic places to watch the sunset, when the beach fills with walkers, surfers, kite-flyers and people lounging with a drink. The surf is consistent, and the beach is great for long walks in either direction.\n\nWhat to Do: Walk the sand towards Petitenget or Double Six, take a beginner surf lesson, and settle onto a bean bag for sunset.",
                    "Locals come at the end of the day too — for football on the sand, a dip and the sunset. Beachside offerings (canang sari) are often placed on the sand, so step around them.",
                    "Best Access: Walk down any of the lanes off Jalan Kayu Aya / Jalan Camplung Tanduk to the beach.\n\nBy Car: Seminyak traffic is slow, especially near sunset — walking is usually faster within the area.",
                    "Public Beach: The beach is free; beach clubs and bars set their own minimum spends.\n\nSurf Lessons: Many operators run beginner lessons on the sand — choose ones with instructors and lifeguard-patrolled spots.\n\nRainy Season: In the wetter months (roughly November-March), rubbish can wash up on the sand.",
                    "Rip Currents: The surf and rips can be strong — swim only between the red-and-yellow flags where lifeguards are on duty.\n\nSun: Very strong midday sun — use sunscreen and seek shade.\n\nValuables: Don't leave belongings unattended on the sand.",
                    "Beach: Free.\nBean Bag & Umbrella: $3-$8.\nSunset Drink: $4-$12.\nSurf Lesson: $25-$45.",
                    new[]
                    {
                        "Petitenget Temple (Pura Petitenget): A beachside temple just to the north.",
                        "Seminyak Boutiques & Cafés: A few minutes' walk inland.",
                        "Double Six Beach: A walk south along the sand."
                    },
                    "Late Afternoon to Sunset (4:30 PM onwards); early morning for quiet walks.",
                    "Medium (5/10) in the day, High (8/10) at sunset.",
                    "Rating: 6/10 — wide, flat sand, though soft and with no boardwalk; beach lanes are narrow.",
                    "2 to 4 hours.")),

            new("bali", KelingkingName, Indonesia, NusaPenida, BaliLangs, -8.7505, 115.4747,
                60m, "Boat", 3,
                new[] { "beach_coast", "viewpoint_scenic_spot", "activity_experience" },
                new[] { "beach", "adventurous", "photography", "nature", "tourist_hotspot" },
                new[] { "Kelingking T-Rex", "Pantai Kelingking", "T-Rex Bay", "Kelingking Cliff" }, "Landscape",
                Desc(
                    "On the south-west coast of Nusa Penida, Kelingking Beach is famous for its headland — a jagged green ridge that drops into turquoise water and looks like a T-Rex head from the clifftop viewpoint. Far below lies a white-sand beach, reached by a very steep, rough path that zig-zags down the spine of the cliff. Most visitors come for the view from the top; only the fit and well-prepared attempt the descent.\n\nWhat to Do: Take in the view from the clifftop viewpoint, and only if you are fit and conditions are dry, tackle the steep path down to the beach.",
                    "Locals on Nusa Penida have watched the island change fast as day-trippers have poured in. They advise visiting outside the midday rush, and say the cliff path down is a serious climb, not a casual walk.",
                    "Best Access: Fast boat from Sanur to Nusa Penida takes roughly 30-45 minutes; from the harbour it's then a drive of around an hour on narrow, bumpy roads to Kelingking.\n\nBy Driver: Most visitors hire a driver on the island for the day — drivers often meet the boat.\n\nOn Foot: The viewpoint is a short walk from the car park; the beach path continues down from there.",
                    "Boat Schedules: Fast-boat times and sea conditions vary — book a reputable operator, check ahead, and allow for delays or cancellations in rough seas.\n\nSmall Fee: A small entry or parking fee is usually collected.\n\nRoads: Road conditions on the island can be rough — expect a slow drive.",
                    "Steep, Demanding Cliff Path: The descent to the beach is very steep, with loose ground and makeshift handrails — wear proper shoes, don't attempt it in the heat of midday or after rain, and turn back if unsure.\n\nDangerous Swimming: Strong currents and waves at the beach — swimming is risky.\n\nCliff Edges: Don't go beyond barriers for photos.\n\nBoat Boarding: Boats usually board from the beach, so expect to wade.",
                    "Fast Boat Sanur–Nusa Penida (return): $20-$40 per person.\nDriver for the Day on the Island: $40-$70 per car.\nEntry / Parking: $1-$3.\nLunch: $5-$15.",
                    new[]
                    {
                        "Broken Beach (Pasih Uug): A natural sea arch about 30-40 minutes' drive away.",
                        "Angel's Billabong: Rock pool next to Broken Beach.",
                        "Crystal Bay: A calmer bay for swimming and snorkelling on the west coast."
                    },
                    "Early Morning or late afternoon; the drier months (roughly April-October) for calmer seas.",
                    "High (8/10) at the viewpoint.",
                    "Rating: 2/10 — a short path to the viewpoint, but the beach descent is very steep and only for fit hikers.",
                    "1 to 3 hours on site, as part of a full-day trip.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlist
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly WishlistSpec[] Wishlists =
        {
            new("bali", "Bali Island Essentials", Indonesia,
                "Five days across Bali's highlights: start in Ubud with the royal palace, the art market and the Sacred Monkey Forest; greet the sunrise over the Tegallalang rice terraces and join the purification ritual at the Tirta Empul water temple; head south to Uluwatu for Padang Padang Beach and the sunset Kecak fire dance at a clifftop temple; take a fast boat to Nusa Penida for the iconic Kelingking 'T-Rex' viewpoint; and finish with a sunset on Seminyak Beach.",
                "Rice terraces at dawn, holy springs, a fire dance on the cliffs and a sunset on the sand — Bali's essentials in five unhurried days.",
                "Couples & Friends",
                new[] { "Private Driver Days", "Nusa Penida Fast Boat & Island Driver", "Temple & Sanctuary Entry Fees", "Kecak Dance Tickets", "4 Nights Mid-Range Accommodation" },
                "Bali, Indonesia, Ubud, Ubud Palace, Puri Saren Agung, Ubud Art Market, Sacred Monkey Forest, Tegallalang, rice terraces, Tirta Empul, water temple, Uluwatu, Pura Luhur Uluwatu, Kecak dance, Padang Padang, Nusa Penida, Kelingking, T-Rex, Seminyak, beach, sunset, temples, culture",
                new[] { "Culture", "Nature", "Beach", "Relaxing" },
                650m, 100m,
                "4 nights in mid-range hotels or villas (shared double): 2 nights in Ubud, 2 nights on the south coast",
                "Private driver for the Ubud, transfer and Seminyak days; return fast boat Sanur–Nusa Penida with a driver on the island",
                "Ubud Palace visit, Monkey Forest entry, Tegallalang entry, Tirta Empul entry with sarong, Uluwatu Temple entry and Kecak ticket, Padang Padang entry, Kelingking viewpoint",
                true, "Culture & Beach Couple",
                new[]
                {
                    new DaySpec(1, "Ubud Arrival & Culture", Ubud, Ubud, Ubud, new[]
                    {
                        new ItemSpec("Morning", "Ubud Palace (Puri Saren Agung)", "Begin at the heart of town, walking the carved courtyards of the royal palace.", 0m, "Royal Heritage", UbudPalaceName),
                        new ItemSpec("Afternoon", "Ubud Art Market", "Browse sarongs, baskets and carvings across the road from the palace — bargain with a smile.", 20m, "Local Favorite", UbudPalaceName),
                        new ItemSpec("Afternoon", "Sacred Monkey Forest Sanctuary", "Walk the mossy temple forest and watch the macaques — keep sunglasses and phones secured.", 8m, "Must-See", MonkeyForestName),
                        new ItemSpec("Evening", "Traditional Dance at the Palace", "If a performance is scheduled, watch Balinese dance in the palace courtyard — check ahead for the programme.", 10m, "Optional Culture", null, true),
                        new ItemSpec("Evening", "Balinese Dinner in Ubud", "Try classic Balinese dishes at a local restaurant in town.", 20m, "Foodie Pick"),
                    }),
                    new DaySpec(2, "Rice Terraces & Water Temple", Ubud, Ubud, Ubud, new[]
                    {
                        new ItemSpec("Morning", "Sunrise at Tegallalang Rice Terraces", "Arrive early for soft light and quiet paths through the terraces.", 5m, "Iconic Spot", TegallalangName),
                        new ItemSpec("Morning", "Tirta Empul Purification", "Join (or respectfully watch) the cleansing ritual at the holy spring — sarong and sash required.", 6m, "Sacred Site", TirtaEmpulName),
                        new ItemSpec("Afternoon", "Lunch with a Rice-Field View", "A relaxed lunch at a warung overlooking the fields.", 12m, "Scenic Lunch"),
                        new ItemSpec("Afternoon", "Coffee Plantation Stop", "Optional tasting of Balinese coffee and teas on the way back to Ubud.", 5m, "Optional Tasting", null, true),
                        new ItemSpec("Evening", "Free Evening in Ubud", "Time to rest, book a massage or wander the town's cafés.", 0m, "Wind Down"),
                    }),
                    new DaySpec(3, "Transfer South to Uluwatu", Ubud, Uluwatu, Uluwatu, new[]
                    {
                        new ItemSpec("Morning", "Drive from Ubud to Uluwatu", "Around 1.5-2 hours south with your driver, depending on traffic.", 35m, "Private Transfer"),
                        new ItemSpec("Afternoon", "Padang Padang Beach", "Walk down through the rock cleft to the cove; swim only when it's calm.", 2m, "Hidden Cove", PadangPadangName),
                        new ItemSpec("Afternoon", "Clifftop Lunch in Uluwatu", "Lunch with ocean views at a café along the coast road.", 15m, "Ocean Views"),
                        new ItemSpec("Evening", "Uluwatu Temple at Sunset", "Walk the cliff paths around the sea temple as the light fades — watch for the monkeys.", 5m, "Sacred Site", UluwatuTempleName),
                        new ItemSpec("Evening", "Kecak Fire Dance", "The chanting circle tells a Ramayana tale against the sunset — buy tickets early on the day.", 15m, "Must-Do", UluwatuTempleName),
                        new ItemSpec("Evening", "Seafood Dinner by the Beach", "A grilled seafood dinner on the sand on the way back.", 25m, "Sunset Feast"),
                    }),
                    new DaySpec(4, "Nusa Penida Day Trip", NusaPenida, NusaPenida, Uluwatu, new[]
                    {
                        new ItemSpec("Morning", "Fast Boat from Sanur", "An early transfer to Sanur and a roughly 30-45 minute fast boat to Nusa Penida.", 30m, "Island Hop"),
                        new ItemSpec("Morning", "Kelingking Viewpoint", "See the famous T-Rex headland from the clifftop before the midday crowds.", 3m, "Iconic View", KelingkingName),
                        new ItemSpec("Morning", "Hike Down to Kelingking Beach", "Only for fit hikers in dry conditions — a very steep, demanding path.", 0m, "Optional Challenge", null, true),
                        new ItemSpec("Afternoon", "Island Lunch", "A simple lunch at a local warung on the island.", 8m, "Local Flavor"),
                        new ItemSpec("Afternoon", "Return Fast Boat to Sanur", "Head back to Bali in the afternoon — sea conditions can affect timings.", 25m, "Island Hop"),
                        new ItemSpec("Evening", "Relaxed Dinner on the South Coast", "Unwind after the big day.", 20m, "Wind Down"),
                    }),
                    new DaySpec(5, "Seminyak Beach & Departure", Seminyak, Seminyak, Seminyak, new[]
                    {
                        new ItemSpec("Morning", "Morning on Seminyak Beach", "A quiet walk or swim between the flags before the day heats up.", 0m, "Beach Time", SeminyakBeachName),
                        new ItemSpec("Morning", "Brunch in Seminyak", "Brunch at one of the area's many cafés.", 15m, "Café Culture"),
                        new ItemSpec("Afternoon", "Free Time & Boutique Shopping", "Browse Seminyak's boutiques or relax by the pool.", 0m, "Free Time"),
                        new ItemSpec("Evening", "Sunset at Seminyak Beach", "End the trip on a bean bag with a drink as the sun drops into the ocean.", 10m, "Sunset Views", SeminyakBeachName),
                        new ItemSpec("Evening", "Airport Transfer", "Transfer to Ngurah Rai International Airport — allow plenty of time for traffic.", 20m, "Departure"),
                    }),
                }),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Every blob filename this seeder references, for upload to blob storage.</summary>
        public static IReadOnlyList<string> ExpectedImageFiles =>
            Countries.Select(c => $"{c.Slug}_hero.jpg")
                .Concat(Wishlists.Select(w => $"{w.Region}_hero.jpg"))
                .Concat(Destinations.Select(DestinationImageFile))
                .Distinct()
                .ToList();

        public static async Task SeedAsync(HodracDbContext db, CancellationToken ct = default)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                var ownsTransaction = db.Database.CurrentTransaction == null;
                await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

                await SeedCoreAsync(db, ct);

                if (tx != null) await tx.CommitAsync(ct);
            });
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Implementation
        // ═══════════════════════════════════════════════════════════════════════

        private static async Task SeedCoreAsync(HodracDbContext db, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;

            // ─── Batch 1: languages, currencies, categories, tags, countries ─────
            var langNames = Languages.Select(l => l.Name).ToList();
            var languages = await db.Languages.Where(l => langNames.Contains(l.LanguageName)).ToListAsync(ct);
            var langByName = languages.GroupBy(l => l.LanguageName).ToDictionary(g => g.Key, g => g.First());
            foreach (var spec in Languages.Where(s => !langByName.ContainsKey(s.Name)))
            {
                var lang = new Language
                {
                    LanguageId = Guid.NewGuid(),
                    LanguageName = spec.Name,
                    LanguageCode = spec.Code,
                    HelpfulSurvivalPhrasesJson = JsonSerializer.Serialize(
                        spec.Phrases.Select(p => new { phrase = p.Phrase, local = p.Local }), JsonOpts),
                    RequiresCertifiedLocalGuide = false
                };
                db.Languages.Add(lang);
                langByName[spec.Name] = lang;
            }

            var currencyCodes = Currencies.Select(c => c.Code).ToList();
            var currencies = await db.Currencies.Where(c => currencyCodes.Contains(c.CurrencyCode)).ToListAsync(ct);
            var currencyByCode = currencies.GroupBy(c => c.CurrencyCode).ToDictionary(g => g.Key, g => g.First());
            foreach (var spec in Currencies.Where(s => !currencyByCode.ContainsKey(s.Code)))
            {
                var cur = new Currency
                {
                    CurrencyId = Guid.NewGuid(),
                    CurrencyCode = spec.Code,
                    CurrencyName = spec.Name,
                    CurrencySymbol = spec.Symbol,
                    ExchangeRateToBase = spec.RateToUsd,
                    LastExchangeRateUpdate = now
                };
                db.Currencies.Add(cur);
                currencyByCode[spec.Code] = cur;
            }

            var categoryKeys = Destinations.SelectMany(d => d.Categories).Distinct().ToList();
            var categoryByKey = (await db.Categories.Where(c => categoryKeys.Contains(c.Key)).ToListAsync(ct))
                .GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First());
            foreach (var key in categoryKeys.Where(k => !categoryByKey.ContainsKey(k)))
            {
                var cat = new Category
                {
                    CategoryId = Guid.NewGuid(),
                    Key = key,
                    CategoryName = key.Replace('_', ' ').ToTitleCase(),
                    CategoryDescription = "Auto-added category",
                    IconName = "tag",
                    ColorHex = "#888888"
                };
                db.Categories.Add(cat);
                categoryByKey[key] = cat;
            }

            var tagKeys = Destinations.SelectMany(d => d.Tags).Distinct().ToList();
            var tagByKey = (await db.Tags.Where(t => tagKeys.Contains(t.Key)).ToListAsync(ct))
                .GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.First());
            foreach (var key in tagKeys.Where(k => !tagByKey.ContainsKey(k)))
            {
                var tag = new Tag
                {
                    TagId = Guid.NewGuid(),
                    Key = key,
                    TagName = key.Replace('_', ' ').ToTitleCase(),
                    TargetPersonaType = "Explorer"
                };
                db.Tags.Add(tag);
                tagByKey[key] = tag;
            }

            var countryByName = new Dictionary<string, Country>();
            foreach (var spec in Countries)
            {
                var country = await db.Countries.FirstOrDefaultAsync(c => c.CountryName == spec.Name, ct);
                if (country == null)
                {
                    country = new Country
                    {
                        CountryId = Guid.NewGuid(),
                        CountryName = spec.Name,
                        Continent = spec.Continent,
                        CountryFlagEmoji = spec.Flag,
                        GlobalHeroImage = BlobBase + $"{spec.Slug}_hero.jpg",
                        VisaRequirementsSummary = spec.Visa,
                        PowerPlugType = spec.Plugs,
                        DrivingSide = spec.DrivingSide,
                        EstimatedDailyTaxRate = spec.TaxRate
                    };
                    db.Countries.Add(country);
                }
                countryByName[spec.Name] = country;
            }
            await db.SaveChangesAsync(ct);

            // CountryLanguage links (add only if missing)
            var countryIds = countryByName.Values.Select(c => c.CountryId).ToList();
            var existingCountryLangs = (await db.CountryLanguages
                    .Where(cl => countryIds.Contains(cl.CountryId))
                    .Select(cl => new { cl.CountryId, cl.LanguageId })
                    .ToListAsync(ct))
                .Select(x => (x.CountryId, x.LanguageId)).ToHashSet();
            foreach (var spec in Countries)
            {
                var countryId = countryByName[spec.Name].CountryId;
                foreach (var langName in spec.LanguageNames)
                {
                    var key = (countryId, langByName[langName].LanguageId);
                    if (existingCountryLangs.Add(key))
                        db.CountryLanguages.Add(new CountryLanguage { CountryId = key.countryId, LanguageId = key.LanguageId });
                }
            }

            // ─── Batch 2: cities ────────────────────────────────────────────────
            var cityByKey = new Dictionary<(string Country, string City), City>();
            foreach (var spec in Cities)
            {
                var countryId = countryByName[spec.CountryName].CountryId;
                var city = await db.Cities.FirstOrDefaultAsync(c => c.CityName == spec.Name && c.CountryId == countryId, ct);
                if (city == null)
                {
                    city = new City
                    {
                        CityId = Guid.NewGuid(),
                        CityName = spec.Name,
                        CountryId = countryId,
                        Latitude = spec.Lat,
                        Longitude = spec.Lng,
                        CityDescription = spec.Description
                    };
                    db.Cities.Add(city);
                }
                cityByKey[(spec.CountryName, spec.Name)] = city;
            }
            await db.SaveChangesAsync(ct);

            // ─── Batch 3: destinations ──────────────────────────────────────────
            var destByName = new Dictionary<string, Destination>();
            foreach (var spec in Destinations)
            {
                var countryId = countryByName[spec.CountryName].CountryId;
                var dest = await db.Destinations
                    .FirstOrDefaultAsync(d => d.DestinationName == spec.Name && d.CountryId == countryId, ct);
                if (dest == null)
                {
                    var search = DestinationSearchFields.Derive(spec.Name);
                    var country = Countries.First(c => c.Name == spec.CountryName);
                    dest = new Destination
                    {
                        DestinationId = Guid.NewGuid(),
                        DestinationName = spec.Name,
                        CleanNormalizedSearchName = search.CleanNormalizedSearchName,
                        MetaphoneCode = search.MetaphoneCode,
                        DoubleMetaphonePrimary = search.DoubleMetaphonePrimary,
                        DoubleMetaphoneSecondary = search.DoubleMetaphoneSecondary,
                        CommonAlternateSpellingsJson = JsonSerializer.Serialize(spec.Alternates, JsonOpts),
                        DescriptionJson = JsonSerializer.Serialize(spec.Desc, JsonOpts),
                        AverageCostPerDay = spec.AvgCost,
                        LuxuryRating = DeriveLuxury(spec.AvgCost, spec.Tags),
                        AccessibilityType = DestinationReferenceData.NormalizeAccessibility(spec.AccessibilityType) ?? spec.AccessibilityType,
                        FamilyFriendlyScore = FamilyScore(spec.Tags),
                        AdventurePaceScore = AdventureScore(spec.Tags),
                        AestheticTrendScore = AestheticTrendScore(spec.Tags),
                        PsychographicVibeTagsJson = JsonSerializer.Serialize(spec.Tags, JsonOpts),
                        Latitude = spec.Lat,
                        Longitude = spec.Lng,
                        SearchHitCount = 0,
                        TimeZone = DestinationReferenceData.NormalizeTimeZone(country.TimeZone) ?? country.TimeZone,
                        SafetyLevel = spec.Safety,
                        CountryId = countryId
                    };
                    db.Destinations.Add(dest);
                }
                destByName[spec.Name] = dest;
            }
            await db.SaveChangesAsync(ct);

            // ─── Batch 4: destination junctions + images (add only if missing) ──
            var destIds = destByName.Values.Select(d => d.DestinationId).ToList();

            var existingCities = (await db.DestinationCities.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.CityId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.CityId)).ToHashSet();
            var existingLangs = (await db.DestinationLanguages.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.LanguageId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.LanguageId)).ToHashSet();
            var existingCurrencies = (await db.DestinationCurrencies.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.CurrencyId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.CurrencyId)).ToHashSet();
            var existingCats = (await db.DestinationCategories.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.CategoryId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.CategoryId)).ToHashSet();
            var existingTags = (await db.DestinationTags.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.TagId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.TagId)).ToHashSet();
            var existingImages = (await db.DestinationImages.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.ImageUrl }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.ImageUrl)).ToHashSet();

            foreach (var spec in Destinations)
            {
                var id = destByName[spec.Name].DestinationId;

                var cityId = cityByKey[(spec.CountryName, spec.CityName)].CityId;
                if (existingCities.Add((id, cityId)))
                    db.DestinationCities.Add(new DestinationCity { DestinationId = id, CityId = cityId });

                foreach (var langName in spec.LanguageNames)
                {
                    var langId = langByName[langName].LanguageId;
                    if (existingLangs.Add((id, langId)))
                        db.DestinationLanguages.Add(new DestinationLanguage { DestinationId = id, LanguageId = langId });
                }

                var currencyCode = Countries.First(c => c.Name == spec.CountryName).CurrencyCode;
                var currencyId = currencyByCode[currencyCode].CurrencyId;
                if (existingCurrencies.Add((id, currencyId)))
                    db.DestinationCurrencies.Add(new DestinationCurrency { DestinationId = id, CurrencyId = currencyId });

                foreach (var catKey in spec.Categories)
                {
                    var catId = categoryByKey[catKey].CategoryId;
                    if (existingCats.Add((id, catId)))
                        db.DestinationCategories.Add(new DestinationCategory { DestinationId = id, CategoryId = catId });
                }

                foreach (var tagKey in spec.Tags)
                {
                    var tagId = tagByKey[tagKey].TagId;
                    if (existingTags.Add((id, tagId)))
                        db.DestinationTags.Add(new DestinationTag { DestinationId = id, TagId = tagId });
                }

                var imageUrl = DestinationImageUrl(spec);
                if (existingImages.Add((id, imageUrl)))
                {
                    db.DestinationImages.Add(new DestinationImage
                    {
                        DestinationImageId = Guid.NewGuid(),
                        DestinationId = id,
                        ImageUrl = imageUrl,
                        ThumbnailUrl = imageUrl,
                        Caption = $"Hero image of {spec.Name}",
                        DisplayOrder = 1,
                        ImageType = "Hero",
                        ShotContext = spec.ShotContext,
                        IsAiGenerated = false
                    });
                }
            }
            await db.SaveChangesAsync(ct);

            // ─── Batch 5: wishlist, days, items, destination links ──────────────
            var specByName = Destinations.ToDictionary(d => d.Name);
            foreach (var spec in Wishlists)
            {
                var wishlist = await db.Wishlists
                    .FirstOrDefaultAsync(w => w.IsTemplate && w.WishlistName == spec.Name, ct);

                if (wishlist == null)
                {
                    wishlist = new Wishlist
                    {
                        WishlistId = Guid.NewGuid(),
                        WishlistName = spec.Name,
                        WishlistDescription = spec.Description,
                        ShortStory = spec.ShortStory,
                        TotalDays = spec.Days.Length,
                        PeopleType = spec.PeopleType,
                        WishlistHeroImage = BlobBase + $"{spec.Region}_hero.jpg",
                        GlobalInclusionsJson = JsonSerializer.Serialize(spec.Inclusions, JsonOpts),
                        RawContentKeywords = spec.Keywords,
                        PsychologicalVibeTagsJson = JsonSerializer.Serialize(spec.Vibes, JsonOpts),
                        DefaultTravelersCount = 2,
                        BasePricePerPerson = spec.BasePricePerPerson,
                        CalculatedTotalCost = spec.BasePricePerPerson * 2,
                        DepositAmountRequired = spec.Deposit,
                        AccommodationInclusions = spec.Accommodation,
                        TransitInclusions = spec.Transit,
                        ActivityInclusions = spec.Activities,
                        IsTemplate = true,
                        IsFeatured = spec.IsFeatured,
                        PrimaryPersonaTarget = spec.Persona,
                        CreatedAt = now,
                        OwnerUserId = null,
                        ForkedFromId = null
                    };
                    db.Wishlists.Add(wishlist);

                    foreach (var daySpec in spec.Days)
                    {
                        var day = new ItineraryDay
                        {
                            ItineraryDayId = Guid.NewGuid(),
                            DayNumber = daySpec.Number,
                            DayTitle = daySpec.Title,
                            MorningCityId = cityByKey[(spec.CountryName, daySpec.MorningCity)].CityId,
                            AfternoonCityId = cityByKey[(spec.CountryName, daySpec.AfternoonCity)].CityId,
                            EveningCityId = cityByKey[(spec.CountryName, daySpec.EveningCity)].CityId,
                            TransitFromPreviousDayRouteId = null,
                            WishlistId = wishlist.WishlistId
                        };
                        db.ItineraryDays.Add(day);

                        var order = 1;
                        foreach (var item in daySpec.Items)
                        {
                            db.ItineraryItems.Add(new ItineraryItem
                            {
                                ItineraryItemId = Guid.NewGuid(),
                                ItineraryDayId = day.ItineraryDayId,
                                ItemTitle = item.Title,
                                ItemDescription = item.Description,
                                ItemOrderIndex = order++,
                                TimeOfDay = item.Time,
                                ImageUrl = item.DestinationName != null
                                    ? DestinationImageUrl(specByName[item.DestinationName])
                                    : "",
                                SocialProofBadge = item.Badge,
                                IndividualCostModifier = item.Cost,
                                IsOptionalActivity = item.Optional,
                                IsSelectedByDefault = !item.Optional
                            });
                        }
                    }
                }

                // Wishlist ↔ Destination links (add only if missing) — all Bali destinations, also for pre-existing wishlists.
                var wishlistId = wishlist.WishlistId;
                var linked = (await db.WishlistDestinations
                        .Where(wd => wd.WishlistId == wishlistId)
                        .Select(wd => wd.DestinationId)
                        .ToListAsync(ct))
                    .ToHashSet();
                foreach (var destSpec in Destinations.Where(d => d.Region == spec.Region))
                {
                    var destId = destByName[destSpec.Name].DestinationId;
                    if (linked.Add(destId))
                        db.WishlistDestinations.Add(new WishlistDestination { WishlistId = wishlistId, DestinationId = destId });
                }

                await db.SaveChangesAsync(ct);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static string DestinationImageFile(DestSpec spec) =>
            $"{spec.Region}_{DestinationSearchFields.Normalize(spec.Name).Replace(' ', '-')}.jpg";

        private static string DestinationImageUrl(DestSpec spec) => BlobBase + DestinationImageFile(spec);

        // Same scoring rules as AfricaPocSeeder / the Seattle seeder.
        private static int DeriveLuxury(decimal avgCost, IEnumerable<string> tagKeys)
        {
            var hasPremium = tagKeys.Contains("premium");
            if (hasPremium || avgCost > 200) return 5;
            if (avgCost > 100) return 4;
            if (avgCost > 40) return 3;
            if (avgCost > 10) return 2;
            return 1;
        }

        private static int FamilyScore(IEnumerable<string> tagKeys)
        {
            var k = tagKeys.ToHashSet();
            int s = 1;
            if (k.Contains("family_friendly")) s += 2;
            if (k.Contains("walkable")) s += 1;
            if (k.Contains("educational")) s += 1;
            if (k.Contains("adventurous")) s -= 1;
            if (k.Contains("nightlife")) s -= 1;
            return Math.Clamp(s, 1, 5);
        }

        private static int AdventureScore(IEnumerable<string> tagKeys)
        {
            var k = tagKeys.ToHashSet();
            int s = 1;
            if (k.Contains("adventurous")) s += 2;
            if (k.Contains("nature")) s += 1;
            if (k.Contains("hidden_gem")) s += 1;
            if (k.Contains("relaxing")) s -= 1;
            if (k.Contains("tourist_hotspot")) s -= 1;
            return Math.Clamp(s, 1, 5);
        }

        private static int AestheticTrendScore(IEnumerable<string> tagKeys)
        {
            var k = tagKeys.ToHashSet();
            if (k.Contains("photography") || k.Contains("architecture")) return 4;
            return 2;
        }
    }
}
