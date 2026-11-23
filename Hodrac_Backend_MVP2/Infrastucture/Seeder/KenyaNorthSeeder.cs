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
    /// "Northern Kenya: Mountains, Elephants and the Ndotos" — an 8-day template wishlist from
    /// Nanyuki up the A2 to Samburu, the sacred mountain Ololokwe, Reteti Elephant Sanctuary in
    /// Namunyak and Camp Ndotto at Ngurunit in the Ndoto Mountains, with optional extensions
    /// (Lewa, Shaba, Kalama, Marsabit, Lake Turkana at Loiyangalani and the Chalbi Desert).
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// Kenya, KES, Swahili and Samburu are new; English is reused by its existing natural key.
    /// All prices are rough USD approximations; park, conservancy and sanctuary fees were checked
    /// against published 2025-2026 sources where available and are labelled approximate.
    /// </summary>
    public static class KenyaNorthSeeder
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

        private const string Kenya = "Kenya";

        private const string English = "English";      // existing natural key (AfricaPocSeeder)
        private const string Swahili = "Swahili";
        private const string Samburu = "Samburu (Maa)";

        private const string Nanyuki = "Nanyuki";
        private const string ArchersPost = "Archer's Post";
        private const string Wamba = "Wamba";
        private const string Ngurunit = "Ngurunit";
        private const string Marsabit = "Marsabit";
        private const string Loiyangalani = "Loiyangalani";

        // Names checked against every other seeder — none collide.
        private const string OlPejetaName = "Ol Pejeta Conservancy";
        private const string LewaName = "Lewa Wildlife Conservancy";
        private const string SamburuReserveName = "Samburu National Reserve";
        private const string BuffaloSpringsName = "Buffalo Springs National Reserve";
        private const string ShabaName = "Shaba National Reserve";
        private const string KalamaName = "Kalama Community Conservancy";
        private const string OlolokweName = "Mount Ololokwe";
        private const string RetetiName = "Reteti Elephant Sanctuary";
        private const string NamunyakName = "Namunyak Conservancy & Mathews Range";
        private const string CampNdottoName = "Camp Ndotto";
        private const string NdotoName = "Ndoto Mountains & Ngurunit Rock Pools";
        private const string MarsabitParkName = "Marsabit National Park";
        private const string ChalbiName = "Chalbi Desert";
        private const string TurkanaName = "Lake Turkana at Loiyangalani";

        private static readonly LanguageSpec[] Languages =
        {
            // Same spec as AfricaPocSeeder — only created if missing.
            new(English, "en", Array.Empty<(string, string)>()),
            new(Swahili, "sw", new[] { ("Hello", "Jambo / Habari"), ("Thank you", "Asante"), ("How much?", "Bei gani?"), ("Slowly", "Pole pole") }),
            new(Samburu, "saq", new[] { ("Hello (greeting)", "Supa"), ("Thank you", "Ashe oleng") }),
        };

        // Approximate mid-market rate, November 2026 (~129.7-130 KES per USD).
        private static readonly CurrencySpec[] Currencies =
        {
            new("KES", "Kenyan Shilling", "KSh", 130m),
        };

        private static readonly CountrySpec[] Countries =
        {
            new(Kenya, "kenya", "Africa", "🇰🇪",
                "Most visitors need an Electronic Travel Authorisation (eTA), applied for online at etakenya.go.ke before travel — apply at least 2 weeks ahead; fees are roughly US$30-$50 depending on nationality and type. Most African nationals (except Somalia and Libya) and East African Community citizens are exempt. From October 2026 most visitors must also buy Kenyan government travel health insurance as part of the eTA application. Requirements change — check official sources before you travel.",
                "Type G", "Left", 0.16m, new[] { English, Swahili, Samburu }, "KES", "Africa/Nairobi"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Nanyuki, Kenya, 0.0167, 37.0742,
                "A busy market town on the Equator at the foot of Mount Kenya, gateway to the Laikipia plateau and the road north. It has banks, supermarkets and fuel — the last big stock-up before Samburu — and an airstrip with scheduled flights from Nairobi's Wilson Airport."),
            new(ArchersPost, Kenya, 0.6385, 37.6671,
                "A small Samburu town on the A2 by the Ewaso Ng'iro river, about 35 km north of Isiolo. It is the gateway to Samburu, Buffalo Springs and Shaba national reserves and the Kalama conservancy, and the flat-topped bulk of Mount Ololokwe rises on the horizon to the north."),
            new(Wamba, Kenya, 0.9792, 37.3239,
                "A Samburu town at the southern edge of the Mathews Range and the hub of the Namunyak community conservancy. Reteti Elephant Sanctuary and the Sarara camps lie in the conservancy to the north-east, reached by dirt roads from Wamba or from Sereolipi on the A2."),
            new(Ngurunit, Kenya, 1.7414, 37.3129,
                "A remote village at the foot of the Ndoto Mountains in northern Samburu County, about 70 km of dirt road west of Laisamis on the A2. Rock pools, forested gullies and big granite walls make it a base for hiking and climbing, with a handful of simple camps."),
            new(Marsabit, Kenya, 2.3333, 37.9833,
                "The capital of Marsabit County, on the A2 on the slopes of a forested volcanic mountain that rises out of the northern desert. It is the base for Marsabit National Park and a staging post for trips to the Chalbi Desert and Lake Turkana."),
            new(Loiyangalani, Kenya, 2.7577, 36.7187,
                "A small, hot, windswept town on the south-eastern shore of Lake Turkana, home to Turkana, Samburu, Rendille and El Molo communities and a long, rough drive from anywhere. South Island, part of the Lake Turkana National Parks World Heritage Site, lies offshore."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] SwahiliLangs = { English, Swahili };
        private static readonly string[] SamburuLangs = { English, Swahili, Samburu };

        private const string TravelAdvice =
            "Travel Advice: The UK FCDO does not advise against travel to Samburu, Isiolo or Marsabit counties as a whole, but asks travellers to take care in parts of Samburu, Isiolo and neighbouring counties, where security operations against banditry, cattle rustling and intercommunal violence are ongoing and movement restrictions can be introduced at short notice. Check current UK, US or your own government's advice before you go and follow local guidance.";

        private const string RoughRoads =
            "Remote Driving: Use a sturdy 4x4 with an experienced local driver-guide, carry spare fuel, water and a spare tyre, and avoid driving after dark.";

        private static readonly DestSpec[] Destinations =
        {
            // ─── Nanyuki ───────────────────────────────────────────────────────
            new("kenya-north", OlPejetaName, Kenya, Nanyuki, SwahiliLangs, 0.0046, 36.8601,
                150m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "photography", "educational", "family_friendly", "tourist_hotspot" },
                new[] { "Ol Pejeta", "OlPejeta", "Ol Pejeta Wildlife Conservancy", "Sweetwaters", "Sweetwaters Chimpanzee Sanctuary" }, "Landscape",
                Desc(
                    "Ol Pejeta Conservancy, just west of Nanyuki on the Laikipia plateau, is one of Kenya's best-known private conservancies. It is home to a large black rhino population, to Najin and Fatu — the last two northern white rhinos — and to the Sweetwaters Chimpanzee Sanctuary, with lion, elephant and plains game against the backdrop of Mount Kenya.\n\nWhat to Do: Take a game drive, visit the northern white rhino enclosure and the chimpanzee sanctuary, and make this an easy first afternoon before the long road north.",
                    "Ol Pejeta is close enough to Nanyuki that Kenyan families come for a weekend drive, and it is one of the conservancies most people in Laikipia point to first when talking about rhino conservation.",
                    "Best Access: About 20-30 minutes' drive west of Nanyuki town to the conservancy gates.\n\nBy Air: Fly from Nairobi's Wilson Airport to Nanyuki and transfer by road.",
                    "Entry: A daily conservation fee per adult, booked online through the conservancy (gates are cashless); a 2026 non-resident adult rate of about US$110 is widely quoted — confirm on the conservancy's tariff guide.\n\nActivities: Some activities, such as rhino or chimp experiences, are booked and paid separately.",
                    "Wildlife: Stay in your vehicle except at designated points.\n\nTiming: Gates have set opening hours; plan to be out by closing time.",
                    "Daily Entry (non-resident adult): approx. US$110.\nGuided Activities: extra, varies.\nVehicle Hire with Driver from Nanyuki: approx. US$100-$180 per day.",
                    new[]
                    {
                        "Lewa Wildlife Conservancy: Rhino country north-east of Nanyuki.",
                        "Nanyuki: Equator crossing and supplies.",
                        "Samburu National Reserve: The next stop up the A2."
                    },
                    "Year-round; the dry months (roughly June-October and December-March) give the easiest game viewing.",
                    "Medium (5/10).",
                    "Rating: 6/10 — viewing is by vehicle; some facilities have steps or rough paths.",
                    "Half a day to a full day.")),

            new("kenya-north", LewaName, Kenya, Nanyuki, SwahiliLangs, 0.2176, 37.4270,
                250m, "Flight", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "photography", "premium", "educational" },
                new[] { "Lewa", "Lewa Downs", "Lewa Conservancy", "Lewa Wildlife Conservancy Kenya" }, "Landscape",
                Desc(
                    "Lewa Wildlife Conservancy, between Mount Kenya and the northern drylands, is a pioneering rhino sanctuary protecting black and white rhino, Grevy's zebra and much more. Together with the neighbouring Ngare Ndare Forest it was added to the Mount Kenya UNESCO World Heritage Site in 2013.\n\nWhat to Do: Stay a night or two for game drives, guided walks and rhino tracking with Lewa's rangers — a high-end alternative or add-on to Ol Pejeta as the gateway north.",
                    "Lewa is often credited as a model for the community conservancies that now cover much of northern Kenya, and its work with neighbouring communities is part of what you see on the ground.",
                    "Best Access: Fly from Wilson Airport to the Lewa Downs airstrip (scheduled and charter flights), or drive from Nanyuki via Timau.\n\nBy Road: From Nanyuki, allow roughly 1-1.5 hours to the conservancy.",
                    "Overnight Guests: Lewa is set up mainly for guests of its lodges; a nightly conservancy fee is added to the room rate (2026 rack rates list about US$183 per adult) — confirm with your lodge.\n\nBooking: Lodges are small and book up in peak season.",
                    "Cost: This is one of the most expensive stops on the route — budget accordingly.\n\nWildlife Walks: Only with Lewa's guides.",
                    "Conservancy Fee: approx. US$183 per adult per night (2026).\nLodge: typically several hundred US$ per person per night and up.\nScheduled Flight Wilson-Lewa: approx. US$200-$300 one way.",
                    new[]
                    {
                        "Ol Pejeta Conservancy: The other Laikipia gateway.",
                        "Samburu National Reserve: About 1.5-2 hours north.",
                        "Nanyuki: Supplies and flights."
                    },
                    "The dry months (roughly June-October and December-March).",
                    "Low (3/10).",
                    "Rating: 5/10 — lodge-based; game viewing by vehicle.",
                    "1 to 2 nights.")),

            // ─── Archer's Post ─────────────────────────────────────────────────
            new("kenya-north", SamburuReserveName, Kenya, ArchersPost, SamburuLangs, 0.6156, 37.5372,
                120m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "photography", "adventurous", "tourist_hotspot" },
                new[] { "Samburu", "Samburu Game Reserve", "Samburu Reserve", "Samburu NR" }, "Landscape",
                Desc(
                    "Samburu National Reserve, about 165 km² on the north bank of the Ewaso Ng'iro river, is the classic northern Kenya safari. The river draws elephants, lions, leopards and big herds in the dry season, and the reserve is known for the 'Samburu Special Five' — Grevy's zebra, reticulated giraffe, Somali ostrich, beisa oryx and gerenuk — dry-country species you won't see in the Mara. It is one of the areas where George and Joy Adamson raised Elsa the lioness of Born Free.\n\nWhat to Do: Game drives along the river at dawn and late afternoon, with the heat of the day spent in camp.",
                    "The reserve is run by Samburu County and surrounded by Samburu pastoralist land, so you'll see warriors and herds on the way in. Guides take pride in finding the Special Five for visitors who have only seen southern parks.",
                    "Best Access: From Nanyuki take the A2 via Isiolo to Archer's Post (tarmac, roughly 2-3 hours); the reserve's Archer's Post gate is just west of town.\n\nBy Air: Daily scheduled flights from Wilson Airport serve the Samburu-area airstrips (including Kalama and Buffalo Springs); camps meet the plane.",
                    "Entry: A county fee per 24 hours — about US$85 per non-resident adult is widely quoted for 2026, but rates change; confirm at the gate or with your camp.\n\nPairing: Buffalo Springs, across the river, is a separate reserve with its own ticket.\n\n" + TravelAdvice,
                    "Heat: Midday is very hot — carry water and sun protection.\n\nWildlife: Stay in the vehicle except where your guide says otherwise; don't camp or walk near the river without a guide (crocodiles, elephants).\n\n" + RoughRoads,
                    "Reserve Fee: approx. US$85 per non-resident adult per 24 hours.\nMid-Range Camp: approx. US$150-$400 per person per night, often full board.\n4x4 with Driver-Guide: approx. US$150-$250 per day.",
                    new[]
                    {
                        "Buffalo Springs National Reserve: Across the river.",
                        "Mount Ololokwe: The sacred mountain just north.",
                        "Kalama Community Conservancy: Bordering the reserve to the north."
                    },
                    "The dry seasons — roughly June-October and December-March — when animals gather along the river; April-May and November are wettest.",
                    "Medium (5/10), busier around the river in peak season.",
                    "Rating: 5/10 — vehicle-based game viewing on rough tracks.",
                    "2 to 3 days.")),

            new("kenya-north", BuffaloSpringsName, Kenya, ArchersPost, SamburuLangs, 0.5710, 37.6253,
                100m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "photography", "adventurous" },
                new[] { "Buffalo Springs", "Buffalo Springs Reserve", "Buffalo Springs Game Reserve" }, "Landscape",
                Desc(
                    "Buffalo Springs National Reserve sits on the south bank of the Ewaso Ng'iro, facing Samburu across the river, in Isiolo County. It shares the same dry-country wildlife — the Special Five, elephants and big cats — and is named for the clear springs in its south.\n\nWhat to Do: Combine it with Samburu for a different set of tracks and fewer vehicles.",
                    "Many visitors treat Samburu and Buffalo Springs as one park, but they are separate reserves managed by different counties — guides know where the animals have moved on any given day.",
                    "Best Access: Gates near Archer's Post, a few minutes off the A2.\n\nBy Air: The Buffalo Springs airstrip is served by scheduled flights from Wilson Airport.",
                    "Entry: Separate ticket from Samburu, managed by Isiolo County — confirm the current rate at the gate.\n\n" + TravelAdvice,
                    "Heat & Dust: Very hot by midday.\n\nWildlife: Follow your guide's instructions near elephants.\n\n" + RoughRoads,
                    "Reserve Fee: separate county ticket — confirm (often quoted at a similar level to Samburu's).\nDriver-Guide: as for Samburu.",
                    new[]
                    {
                        "Samburu National Reserve: Across the river.",
                        "Shaba National Reserve: East of the A2.",
                        "Archer's Post: Gateway town."
                    },
                    "Dry seasons, roughly June-October and December-March.",
                    "Low-Medium (4/10).",
                    "Rating: 5/10 — vehicle-based game viewing.",
                    "Half a day to a day.")),

            new("kenya-north", ShabaName, Kenya, ArchersPost, SamburuLangs, 0.6636, 37.8295,
                100m, "Car", 2,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "nature", "hidden_gem", "adventurous", "history" },
                new[] { "Shaba", "Shaba Game Reserve", "Shaba Reserve" }, "Landscape",
                Desc(
                    "Shaba National Reserve, east of the A2 and the twin reserves, is rockier and quieter, with springs, volcanic outcrops and riverine forest along the Ewaso Ng'iro. Joy Adamson worked with leopards here and was killed in Shaba in 1980.\n\nWhat to Do: A day of game driving in emptier country, with dramatic scenery around Shaba hill.",
                    "Guides like Shaba for its sense of space — you can drive for long stretches without seeing another vehicle.",
                    "Best Access: Turn east off the A2 near Archer's Post; allow extra time for rough tracks.\n\nCombine: Easy to pair with Samburu and Buffalo Springs.",
                    "Entry: Separate county ticket — confirm the current rate.\n\n" + TravelAdvice,
                    "Remoteness: Few facilities — carry water and go with a guide.\n\n" + RoughRoads,
                    "Reserve Fee: separate ticket — confirm.\nPacked Lunch: from your camp.",
                    new[]
                    {
                        "Buffalo Springs National Reserve: To the west.",
                        "Samburu National Reserve: Across the river.",
                        "Archer's Post: Gateway town."
                    },
                    "Dry seasons, roughly June-October and December-March.",
                    "Low (2/10).",
                    "Rating: 4/10 — rough tracks, few facilities.",
                    "A full day.")),

            new("kenya-north", KalamaName, Kenya, ArchersPost, SamburuLangs, 0.7300, 37.5300,
                150m, "Flight", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "cultural", "hidden_gem", "adventurous" },
                new[] { "Kalama", "Kalama Conservancy", "Kalama Wildlife Conservancy" }, "Landscape",
                Desc(
                    "Kalama Community Conservancy borders Samburu National Reserve to the north and is owned by the local Samburu community. Wildlife moves freely between the two, and camps such as Saruni Samburu sit on its kopjes with long views towards Ololokwe.\n\nWhat to Do: Guided walks and game drives away from the reserve crowds, with Samburu guides — and night or walking activities the national reserve doesn't allow.",
                    "Conservancy fees go to the community that owns the land, paying for rangers and local projects — one of the clearest examples on this route of tourism money staying local.",
                    "Best Access: From Archer's Post by road (the conservancy lies just north of the reserve), or fly to the Kalama airstrip, which is served by scheduled flights from Wilson Airport.",
                    "Conservancy Fee: Paid per night through your camp — around US$116-$130 per adult in recent rate sheets; confirm with the camp.\n\nCommunity Land: Ask before photographing people, homesteads or livestock.\n\n" + TravelAdvice,
                    "Walking: Only with conservancy guides.\n\n" + RoughRoads,
                    "Conservancy Fee: approx. US$116-$130 per adult per night.\nCamp: mid-range to luxury.",
                    new[]
                    {
                        "Samburu National Reserve: Next door.",
                        "Mount Ololokwe: On the northern skyline.",
                        "Archer's Post: Gateway town."
                    },
                    "Dry seasons, roughly June-October and December-March.",
                    "Low (2/10).",
                    "Rating: 4/10 — camps on rocky hills; bush walking.",
                    "1 to 2 nights.")),

            new("kenya-north", OlolokweName, Kenya, ArchersPost, SamburuLangs, 0.8245, 37.5376,
                45m, "Car", 3,
                new[] { "nature_outdoor", "viewpoint_scenic_spot", "religious_site" },
                new[] { "adventurous", "nature", "cultural", "photography", "hidden_gem" },
                new[] { "Ololokwe", "Mt Ololokwe", "Ol Lolokwe", "Ol Donyo Sabache", "Ol Doinyo Sabache", "Ol Doinyo Lengeyo", "Sabache", "Sabache Camp" }, "Landscape",
                Desc(
                    "Mount Ololokwe — Ol Donyo Sabache to many locals — is a flat-topped mountain of about 2,000 m that rises sheer from the plains north of Archer's Post, in the Namunyak conservancy area of Samburu East. It is sacred to the Samburu, who consider it a source of water and food for their livestock; herders still drive cattle up to springs near the top. Its cliffs hold a nesting colony of critically endangered Rüppell's vultures, and the summit plateau has forest with podocarpus, cedar and an endemic giant cycad.\n\nWhat to Do: Hike up from Sabache Camp at the base (about 2-3 hours up, 2 down) with a Samburu guide, and if you can, sleep on top for sunset and sunrise over the plains.",
                    "For the Samburu this is a sacred place, not just a viewpoint — guides from the community lead the hike, explain its meaning and the elephant trails you'll follow, and ask visitors to behave as guests on the mountain.",
                    "Best Access: Drive north from Archer's Post on the A2 for about 27 km, then about 4 km on a dirt track to Sabache Camp at the foot of the mountain (around 1,000 m).\n\nThe Hike: Steep, rocky elephant trails most of the way, levelling off on the tabletop.",
                    "Guides: Hikes go with local guides from Sabache Camp — the camp lists the hike at about US$20 (KES 1,500) per person, guide included; hikes starting before 6:00 AM carry an extra group fee and need two guides.\n\nFees: A Namunyak conservation fee (about KES 1,500 per non-resident) and camping (about US$23 per person at the base; summit camping around US$50, limited space) are reported on top — confirm current rates with Sabache Camp.\n\nSacred Mountain: Follow your guide's lead, take all rubbish down, and ask before photographing people or herders at the springs.\n\n" + TravelAdvice,
                    "Heat: Start early or late — the lower slopes are hot and exposed. Carry plenty of water.\n\nElephants: Elephants use the same trails, especially in the dry season — another reason to go with a guide.\n\nSteep Ground: Good shoes; the descent is hard on knees.",
                    "Hike with Guide: approx. US$20 (KES 1,500) per person.\nNamunyak Conservation Fee: approx. KES 1,000-1,500 (US$8-$12).\nCamping: approx. US$23 per person at base; summit camping approx. US$50.\nMeals at Sabache: approx. KES 600-1,200 (US$5-$10).",
                    new[]
                    {
                        "Samburu National Reserve: About an hour south.",
                        "Reteti Elephant Sanctuary: North via Sereolipi.",
                        "Kalama Community Conservancy: Views of Ololokwe from its kopjes."
                    },
                    "Dry seasons (roughly June-October and December-March); hike at dawn or late afternoon to avoid the heat.",
                    "Low (2/10).",
                    "Rating: 2/10 — a steep, rocky 2-3 hour climb; not suitable for limited mobility.",
                    "A full day, or an overnight with a summit camp.")),

            // ─── Wamba (Namunyak) ──────────────────────────────────────────────
            new("kenya-north", RetetiName, Kenya, Wamba, SamburuLangs, 1.1156, 37.4601,
                90m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "educational", "cultural", "photography", "family_friendly" },
                new[] { "Reteti", "Reteti Elephant Orphanage", "Reteti Sanctuary", "Reteti House" }, "Exterior",
                Desc(
                    "Reteti Elephant Sanctuary, in the Namunyak community conservancy below the Mathews Range, rescues orphaned and abandoned elephant calves, hand-rears them and returns them to the wild herds of the area. Opened in 2016 by the Samburu community, it is billed as the first community-owned elephant sanctuary in Africa, and the keepers are Samburu men and women — many from families who once saw elephants only as a threat.\n\nWhat to Do: Book a feeding visit — you're paired with a keeper who introduces the calves as they come in for their bottles.",
                    "Reteti is a point of pride for the surrounding communities: it employs local keepers and has changed how many Samburu families talk about elephants.",
                    "Best Access: From Isiolo, drive north on the A2 to Sereolipi (about 3 hours, per the sanctuary), cross the river bridge, pass Sereolipi Secondary School and turn left onto the 19 km road to the sanctuary.\n\nBy Air: Fly to the Namunyak airstrip and arrange a vehicle through the sanctuary.",
                    "Book Ahead: Visits must be booked at least 24 hours in advance on the sanctuary's website — no same-day bookings.\n\nVisiting Slots: Midday feed 11:30 AM-1:00 PM (shared, open to all); private morning (8:30-10:00 AM) and afternoon (2:30-4:00 PM) feeds; sunrise and evening feeds are for Sarara and Reteti House guests only. Arrive on time — feeds can't wait.\n\nDonation: The sanctuary's visitor page lists US$75 per international adult and US$50 per child under 15 (older sources quote less) — confirm when booking.\n\nRules: Don't touch or call the elephants; wear dark green, brown or khaki; no food or drink is sold on site.",
                    "Long Approach: The last 19 km is dirt — leave plenty of time to arrive for your slot.\n\nPhotos & Posting: Let the calves set the pace and ask your keeper before posting; filming needs a permit.\n\n" + TravelAdvice,
                    "Visit Donation (international adult): approx. US$75; child under 15 approx. US$50.\nReteti House / Sarara stays: luxury, booked via Sarara.",
                    new[]
                    {
                        "Namunyak Conservancy & Mathews Range: The wider conservancy and forest.",
                        "Mount Ololokwe: South along the A2.",
                        "Camp Ndotto: North via Laisamis."
                    },
                    "Year-round; dry seasons make the roads easier. The midday feed is the main open slot.",
                    "Medium (5/10) at the midday feed.",
                    "Rating: 5/10 — short walks on uneven ground at the sanctuary.",
                    "2 to 3 hours for a visit, plus travel.")),

            new("kenya-north", NamunyakName, Kenya, Wamba, SamburuLangs, 1.0124, 37.3951,
                180m, "Car", 3,
                new[] { "nature_outdoor", "activity_experience", "viewpoint_scenic_spot" },
                new[] { "nature", "cultural", "adventurous", "hidden_gem", "photography" },
                new[] { "Namunyak", "Namunyak Wildlife Conservancy", "Namunyak Wildlife Conservation Trust", "Mathews Range", "Matthews Range", "Lenkiyio Hills", "Sarara", "Sarara Camp", "Singing Wells" }, "Landscape",
                Desc(
                    "The Namunyak conservancy is a huge area of Samburu community land around the Mathews Range, a forested 'sky island' rising from the dry plains, with elephants, wild dogs and ancient Kenyan giant cycads in its montane forest. In the dry season, Samburu herders water cattle at the 'singing wells' — wells dug deep into dry riverbeds, where warriors pass water up by hand while singing to their herds.\n\nWhat to Do: Guided forest walks in the Mathews Range, a visit to the singing wells in the dry season with a guide, and evenings in Samburu country.",
                    "This is lived-in community land, not a park. The singing wells are a working part of daily life, and the community asks that visitors don't photograph them.",
                    "Best Access: From Wamba or from Sereolipi on the A2 on dirt roads — a 4x4 is essential.\n\nBy Air: The Sarara airstrip serves the camps; charters from Wilson Airport.",
                    "Where to Stay: Sarara Camp, Sarara Treehouses and Reteti House are luxury (2026 published rates around US$1,110-$1,515 per person per night); simpler community campsites and camps exist — book through a local operator.\n\nConservancy Fees: A daily fee applies to visitors (around US$185 per adult has been quoted for Sarara stays) — confirm when booking.\n\nSinging Wells: Used mainly in the driest months (the sanctuary lists February, March, September and October) and never guaranteed — after rain there's no need for them. No photography at the wells.\n\n" + TravelAdvice,
                    "Respect: Ask before photographing people; never photograph the singing wells.\n\nWalking: Only with Samburu guides — elephants and buffalo use the forest.\n\n" + RoughRoads,
                    "Conservancy Fee: approx. US$185 per adult per day (for Sarara stays; confirm for other camps).\nLuxury Camp: approx. US$1,110-$1,515 per person per night (2026).\nForest Walk with Guide: approx. US$20-$50.",
                    new[]
                    {
                        "Reteti Elephant Sanctuary: Within the conservancy.",
                        "Wamba: The nearest town.",
                        "Mount Ololokwe: Part of the wider Namunyak area."
                    },
                    "Dry seasons (roughly June-October and December-March); singing wells in the driest months.",
                    "Low (2/10).",
                    "Rating: 3/10 — dirt roads and bush walking.",
                    "1 to 2 days.")),

            // ─── Ngurunit ──────────────────────────────────────────────────────
            new("kenya-north", CampNdottoName, Kenya, Ngurunit, SamburuLangs, 1.7380, 37.3000,
                80m, "Car", 3,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "adventurous", "hidden_gem", "cultural" },
                new[] { "Camp Ndoto", "Ndotto Camp", "Camp Ndotto Ngurunit" }, "Exterior",
                Desc(
                    "Camp Ndotto is a small eco-friendly tented camp beside the river at Ngurunit, at the foot of the Ndoto Mountains, close to the Ngurunit rock pools. It works with the local Samburu community and runs hikes, nature walks and birding with Samburu guides; travellers often use it as a stopover between Lake Turkana and Nanyuki.\n\nWhat to Do: Use it as your base for the Ndotos — hike, swim in the rock pools after the rains and spend the evenings under a huge night sky.",
                    "The camp is tied into the Ngurunit community and its education projects (one reviewer describes revenue going to the Kenya Drylands Education Fund), and its guides are local.",
                    "Best Access: Drive north on the A2 past Merille to Laisamis, then turn west for about 70 km on a dirt road to Ngurunit. Rains can wash out river crossings — check conditions before setting off.\n\nBy Air: A small airstrip near Ngurunit appears in airstrip directories; charter only — confirm with a charter operator.",
                    "Booking: Book directly with the camp well ahead — capacity is small and we couldn't find published rates.\n\nFacilities: Tented rooms with home-cooked meals; listings give check-in from 3:00 PM and check-out by noon.\n\n" + TravelAdvice,
                    "Remoteness: There's little in Ngurunit — bring cash, any medication and supplies.\n\n" + RoughRoads,
                    "Rates: not published — ask the camp (budget roughly US$60-$150 per person per night as a placeholder).\nLocal Guide: approx. KES 1,500 (US$12) per day.",
                    new[]
                    {
                        "Ndoto Mountains & Ngurunit Rock Pools: On the doorstep.",
                        "Marsabit National Park: North up the A2.",
                        "Reteti Elephant Sanctuary: Back south via Laisamis."
                    },
                    "Dry seasons for driving (roughly June-October and December-March); just after the rains for full rock pools.",
                    "Very Low (1/10).",
                    "Rating: 3/10 — a rustic bush camp on uneven ground.",
                    "2 nights.")),

            new("kenya-north", NdotoName, Kenya, Ngurunit, SamburuLangs, 1.7331, 37.2935,
                20m, "Car", 3,
                new[] { "nature_outdoor", "viewpoint_scenic_spot", "activity_experience" },
                new[] { "adventurous", "nature", "hidden_gem", "photography" },
                new[] { "Ndoto Mountains", "Ndoto Range", "Ndotos", "Ngurunit Rock Pools", "Aldera", "Mount Poi", "Mt Poi" }, "Landscape",
                Desc(
                    "The Ndoto Mountains are a roughly 40 km range of granite and forest in northern Samburu County, rising from about 800 m at Ngurunit village. Hikes follow a permanent stream past a 400 m cliff to rock pools — with natural water slides after the rains — and up through bush and forest to viewpoints such as Aldera (about 1,800 m) facing Mount Poi, whose gneiss walls up to around 700 m high draw big-wall climbers.\n\nWhat to Do: A short guided walk to the rock pools, or a full-day hike up Aldera; climbers come for Poi.",
                    "Until recently insecurity kept the Ndotos off most travellers' maps; local guides from Ngurunit now lead hikes and are the best way to understand the mountains and the communities around them.",
                    "Best Access: From Camp Ndotto or the camps in Ngurunit on foot.\n\nThe Aldera Hike: Allow 7-9 hours return, with river crossings, steep rocky sections and a short boulder scramble near the top.",
                    "Guides: Hire a local guide in Ngurunit (around KES 1,500 per day is quoted) — trails aren't marked.\n\nRock Pools: Fullest in and just after the rainy seasons.\n\n" + TravelAdvice,
                    "Heat & Water: Carry plenty of water and start early.\n\nFlash Floods: Avoid riverbeds and gullies when it is raining upstream.\n\nRemoteness: No rescue services nearby — go with a guide.",
                    "Local Guide: approx. KES 1,500 (US$12) per day.\nCommunity Camping: approx. KES 500-1,000 (US$4-$8).",
                    new[]
                    {
                        "Camp Ndotto: Base camp in Ngurunit.",
                        "Namunyak Conservancy & Mathews Range: To the south.",
                        "Marsabit National Park: North up the A2."
                    },
                    "Dry seasons for access; rock pools are best just after the rains.",
                    "Very Low (1/10).",
                    "Rating: 2/10 — rough trails, river crossings and scrambles.",
                    "Half a day to a full day.")),

            // ─── Marsabit (optional extension) ─────────────────────────────────
            new("kenya-north", MarsabitParkName, Kenya, Marsabit, SwahiliLangs, 2.2612, 37.9560,
                50m, "Car", 4,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "nature", "hidden_gem", "adventurous", "photography" },
                new[] { "Marsabit", "Marsabit Park", "Marsabit National Park and Reserve", "Lake Paradise", "Gof Sokorte Guda" }, "Landscape",
                Desc(
                    "Marsabit National Park covers a forested volcanic mountain that rises like an island out of the northern desert, with crater lakes — the best known is Lake Paradise — mist forest, elephants and rich birdlife. The mountain's elephants are famous: Ahmed, a great tusker, was protected by presidential decree in the 1970s.\n\nWhat to Do: Drive and walk (with a ranger) to the crater viewpoints, look for elephants and forest birds, and enjoy the cool after the plains.",
                    "Marsabit town is a crossroads of the Borana, Gabra, Rendille and other communities, and the mountain is its water tower — the forest matters far beyond the park boundary.",
                    "Best Access: North on the A2 (tarmac) from Laisamis to Marsabit town; the park gate is on the edge of town.\n\nFrom Ngurunit: Back to Laisamis, then north on the A2.",
                    "Entry: Kenya Wildlife Service fee — about US$40 per non-resident adult under the 2025 fee regulations; paid cashless via KWS/eCitizen. Confirm before you go.\n\nGuides: Take a KWS ranger for walks.\n\nTravel Advice: The UK FCDO advises against all but essential travel to Moyale town near the Ethiopian border, and the US State Department lists parts of Marsabit County near the Ethiopian border as Do Not Travel. Marsabit town and park are well south of that zone, but check current advice before going this far north.",
                    "Mist & Mud: Forest tracks can be slippery after rain.\n\n" + RoughRoads,
                    "Park Fee: approx. US$40 per non-resident adult (KWS).\nRanger Guide: tip or fee, approx. US$10-$20.",
                    new[]
                    {
                        "Chalbi Desert: West across the plains.",
                        "Lake Turkana at Loiyangalani: The far north-west.",
                        "Camp Ndotto: Back south via Laisamis."
                    },
                    "Dry seasons, roughly June-October and December-March.",
                    "Very Low (1/10).",
                    "Rating: 4/10 — vehicle tracks; crater walks are uneven.",
                    "1 day.")),

            new("kenya-north", ChalbiName, Kenya, Marsabit, SwahiliLangs, 3.1705, 37.3501,
                30m, "Car", 4,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "adventurous", "hidden_gem", "photography", "cultural" },
                new[] { "Chalbi", "Chalbi Salt Desert", "Kalacha", "North Horr" }, "Landscape",
                Desc(
                    "The Chalbi Desert is a vast, shimmering salt flat — an ancient dry lakebed — west of Marsabit, between the oasis settlements of Kalacha and North Horr. It is home to Gabra herders and their camels, and mirages dance across it in the midday heat.\n\nWhat to Do: Cross it as part of an expedition loop between Marsabit and Lake Turkana, with stops at the Kalacha oasis.",
                    "For the Gabra, the Chalbi is grazing country and a trade route, and the oases are meeting places — travellers are a rarity.",
                    "Best Access: Only by 4x4 on desert tracks from Marsabit, typically on a multi-day loop with Loiyangalani — go with an experienced local driver and travel in convoy if you can.",
                    "Expedition Travel: Carry extra fuel, water, food and recovery gear; there is very little support out here.\n\nTravel Advice: The US State Department lists parts of Marsabit County near the Ethiopian border as Do Not Travel; plan your route well south of that zone and check current UK, US or your own government's advice.",
                    "Heat: Extreme heat and glare — sun protection is essential.\n\nRain: After rain the lakebed turns to impassable mud.\n\nNavigation: Tracks are faint; don't go without a driver who knows the route.",
                    "4x4 with Experienced Driver: approx. US$200-$300 per day plus fuel.\nCommunity Camping at Kalacha: small fee.",
                    new[]
                    {
                        "Marsabit National Park: The mountain to the east.",
                        "Lake Turkana at Loiyangalani: To the south-west.",
                        "Camp Ndotto: Far to the south."
                    },
                    "The dry seasons, roughly June-October and December-March.",
                    "Very Low (1/10).",
                    "Rating: 1/10 — expedition travel only.",
                    "A long day crossing, as part of a multi-day loop.")),

            // ─── Loiyangalani (optional extension) ─────────────────────────────
            new("kenya-north", TurkanaName, Kenya, Loiyangalani, SwahiliLangs, 2.7577, 36.7187,
                40m, "Car", 4,
                new[] { "nature_outdoor", "viewpoint_scenic_spot", "beach_coast" },
                new[] { "adventurous", "hidden_gem", "cultural", "photography", "nature" },
                new[] { "Lake Turkana", "Loiyangalani", "Jade Sea", "Lake Rudolf", "South Island", "El Molo" }, "Landscape",
                Desc(
                    "Lake Turkana — the 'Jade Sea' — is the world's largest permanent desert lake, a long, wind-whipped body of green water in a landscape of lava and desert. Loiyangalani, on the south-eastern shore, is the main base, with El Molo, Turkana, Samburu and Rendille communities around it and South Island — part of the Lake Turkana National Parks UNESCO World Heritage Site — offshore.\n\nWhat to Do: Watch the light change over the lake, visit El Molo villages with a local guide, and, if conditions allow, take a boat towards South Island.",
                    "Life here revolves around the lake and livestock; visitors are welcomed but should go with a local guide, ask before photographing anyone and agree any fees up front.",
                    "Best Access: A very long, rough drive — from Laisamis on the A2 via the road towards the Lake Turkana Wind Power project, or from Marsabit via the Chalbi/North Horr side (a 2023 report described Marsabit to Loiyangalani as about eight hours).\n\nBy Air: Charter flights from Wilson Airport to Loiyangalani's airstrip.",
                    "Expedition Travel: Allow at least two nights to make the trip worthwhile; accommodation is basic.\n\nBoats: The lake can get rough fast with strong winds — only go out with experienced boatmen.\n\nTravel Advice: The US State Department lists parts of Turkana and Marsabit counties near the Ethiopian border as Do Not Travel, and the UK FCDO notes insecurity in parts of Turkana; Loiyangalani is far south of the border zone, but check current advice before travelling.",
                    "Heat & Wind: Extremely hot and windy — protect against sun and dust.\n\nWater: Lake Turkana has crocodiles; swim only where locals advise.\n\nPhotography: Always ask first and agree any fee before you start.\n\n" + RoughRoads,
                    "Basic Lodge / Camp: approx. US$20-$80 per person per night.\nBoat Trip: negotiated, approx. US$50-$150 per boat.\nCommunity Visit Fee: approx. US$10-$20.",
                    new[]
                    {
                        "Chalbi Desert: To the north-east.",
                        "Marsabit National Park: The long road east.",
                        "Camp Ndotto: Back south-east via Laisamis."
                    },
                    "The cooler, drier months, roughly June-September and December-February; it is hot all year.",
                    "Very Low (1/10).",
                    "Rating: 1/10 — remote, hot and rough roads.",
                    "2 to 3 nights including travel.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlist
        // ═══════════════════════════════════════════════════════════════════════

        // ItineraryItem.TimeOfDay is a controlled value (Morning/Afternoon/Evening — validated by the
        // admin and user itinerary endpoints), so any specific clock times go in the item titles.
        private static readonly WishlistSpec[] Wishlists =
        {
            new("kenya-north", "Northern Kenya: Mountains, Elephants and the Ndotos", Kenya,
                "A rugged, remote 8-day road trip into Samburu country for travellers comfortable with long drives, heat, dust and simple camps. From Nanyuki on the Equator, the A2 climbs north to Samburu National Reserve for the Special Five, then on to Mount Ololokwe — the Samburu's sacred flat-topped mountain — for a guided hike and a night on the summit. Next comes Reteti Elephant Sanctuary in the Namunyak conservancy, where Samburu keepers raise orphaned elephant calves, and the forests of the Mathews Range, before a long drive to Camp Ndotto at Ngurunit for hiking and rock pools in the Ndoto Mountains. Optional extensions take in Lewa, Shaba and Kalama, or push on north to Marsabit, the Chalbi Desert and Lake Turkana. A 4x4 with an experienced local driver-guide is essential; book camps and the Reteti visit ahead, and check current travel advice for Samburu, Isiolo and Marsabit before you go. All prices are rough USD estimates.",
                "A sacred mountain at sunrise, orphaned elephants at feeding time and rock pools in the Ndotos — eight days on the rough roads of Samburu country.",
                "Adventurous Travellers",
                new[] { "7 Nights in Camps & Lodges (mid-range and community camps)", "4x4 Vehicle with Local Driver-Guide", "Samburu National Reserve Fees", "Guided Ololokwe Hike with Summit Camp", "Reteti Elephant Sanctuary Visit", "2 Nights at Camp Ndotto" },
                "Northern Kenya, Samburu, Samburu National Reserve, Samburu Special Five, Buffalo Springs, Shaba, Kalama, Archer's Post, Isiolo, Nanyuki, Ol Pejeta, Lewa, Mount Ololokwe, Ololokwe, Ol Donyo Sabache, Sabache Camp, Reteti, Reteti Elephant Sanctuary, elephant orphanage, Namunyak, Mathews Range, Sarara, singing wells, Wamba, Sereolipi, Camp Ndotto, Ndoto Mountains, Ngurunit, rock pools, Mount Poi, Marsabit, Chalbi Desert, Lake Turkana, Loiyangalani, El Molo, safari, hiking, overlanding, 4x4, community conservancy, Kenya",
                new[] { "Adventure", "Wildlife", "Culture", "Off the Beaten Path" },
                2400m, 400m,
                "7 nights (shared double): 1 in Nanyuki, 1 at a mid-range camp by Samburu, 1 summit/base camp on Ololokwe, 2 in Namunyak (community camp or campsite — luxury Sarara/Reteti House extra), 2 at Camp Ndotto",
                "4x4 with experienced local driver-guide and fuel from Nanyuki and back; flights to Nanyuki (or the drive from Nairobi, roughly 3-4 hours) not included; optional charter flight back to Wilson",
                "Ol Pejeta game drive, Samburu game drives, guided Ololokwe hike and summit camp, Reteti midday feed, Mathews Range guided walk, singing wells in season, Ndoto Mountains hike and rock pools",
                false, "Rugged Explorer",
                new[]
                {
                    new DaySpec(1, "Nanyuki · Gateway to the North", Nanyuki, Nanyuki, Nanyuki, new[]
                    {
                        new ItemSpec("Morning", "Fly or Drive to Nanyuki", "Fly from Nairobi's Wilson Airport to Nanyuki, or drive (roughly 3-4 hours). Meet your driver-guide and 4x4 for the week.", 150m, "Arrival"),
                        new ItemSpec("Afternoon", "Ol Pejeta Game Drive", "Rhino country under Mount Kenya — the last two northern white rhinos, black rhino and the Sweetwaters chimpanzee sanctuary. Book the entry ticket online (gates are cashless).", 110m, "Rhino Country", OlPejetaName),
                        new ItemSpec("Afternoon", "Or: Lewa Wildlife Conservancy", "Splurge on a night at Lewa instead — rhino tracking and Grevy's zebra in a UNESCO-listed conservancy (nightly conservancy fee on top of the lodge).", 183m, "Premium Option", LewaName, true),
                        new ItemSpec("Evening", "Dinner in Nanyuki", "Stock up on cash, snacks and water — the shops thin out fast north of Isiolo.", 20m, "Last Big Town"),
                    }),
                    new DaySpec(2, "Up the A2 to Samburu", Nanyuki, ArchersPost, ArchersPost, new[]
                    {
                        new ItemSpec("Morning", "Drive North via Isiolo", "The A2 drops from the Mount Kenya foothills into dry country — about 2-3 hours on tarmac to Archer's Post. Fuel up in Isiolo.", 0m, "Road Trip"),
                        new ItemSpec("Afternoon", "Samburu Afternoon Game Drive", "Elephants along the Ewaso Ng'iro and the Special Five — Grevy's zebra, reticulated giraffe, Somali ostrich, beisa oryx and gerenuk. The reserve fee covers 24 hours.", 85m, "Must-See", SamburuReserveName),
                        new ItemSpec("Evening", "Sundowner by the River", "Back at camp by dusk — the reserve doesn't allow driving after dark.", 0m, "Sunset", SamburuReserveName),
                    }),
                    new DaySpec(3, "Samburu Sunrise & Up Ololokwe", ArchersPost, ArchersPost, ArchersPost, new[]
                    {
                        new ItemSpec("Morning", "6:30 AM · Sunrise Game Drive", "The best light and the most active animals — still within yesterday's 24-hour reserve ticket.", 0m, "Early Bird", SamburuReserveName),
                        new ItemSpec("Morning", "Or: Buffalo Springs Across the River", "Swap to the south bank for different tracks — a separate county ticket.", 85m, "Optional", BuffaloSpringsName, true),
                        new ItemSpec("Afternoon", "Hike Mount Ololokwe from Sabache Camp", "About 30 minutes north to Sabache Camp, then 2-3 hours up steep elephant trails with a Samburu guide. Respect the sacred mountain — follow your guide's lead and pack out all rubbish.", 35m, "Sacred Mountain", OlolokweName),
                        new ItemSpec("Evening", "Sunset & Summit Camp", "Sleep on the tabletop for sunset and sunrise over the plains (limited space — book with Sabache), or come down to the base camp before dark.", 50m, "Sleep on the Summit", OlolokweName),
                        new ItemSpec("Evening", "Or: Night in Kalama Conservancy", "Stay on Samburu's northern border with views of Ololokwe, and hike the next morning instead.", 130m, "Optional", KalamaName, true),
                    }),
                    new DaySpec(4, "Sunrise on the Sacred Mountain & into Namunyak", ArchersPost, Wamba, Wamba, new[]
                    {
                        new ItemSpec("Morning", "Sunrise on Ololokwe", "Watch the sun come up over the plains, look for Rüppell's vultures on the cliffs, then descend (about 2 hours).", 0m, "Bucket List", OlolokweName),
                        new ItemSpec("Afternoon", "North to Sereolipi & Namunyak", "Up the A2 to Sereolipi, then 19 km of dirt road into the Namunyak conservancy. Settle in for two nights.", 0m, "Road Trip"),
                        new ItemSpec("Afternoon", "3:00 PM · Reteti Private Afternoon Feed", "Arrived early? Book the private afternoon slot (2:30-4:00 PM) at least 24 hours ahead.", 75m, "Optional", RetetiName, true),
                        new ItemSpec("Evening", "Evening in Namunyak", "A community camp or campsite in Samburu country — or splurge on Sarara or Reteti House.", 0m, "Community Stay", NamunyakName),
                        new ItemSpec("Evening", "Splurge: Sarara Camp", "One of Kenya's most celebrated camps, in the Mathews Range — luxury rates and a daily conservancy fee.", 1110m, "Luxury Upgrade", NamunyakName, true),
                    }),
                    new DaySpec(5, "Reteti Elephants & the Mathews Range", Wamba, Wamba, Wamba, new[]
                    {
                        new ItemSpec("Morning", "Singing Wells (Dry Season)", "In the driest months, watch Samburu warriors sing as they pass water up from deep wells to their cattle. Not guaranteed; no photography — a community request.", 0m, "Living Tradition", NamunyakName, true),
                        new ItemSpec("Morning", "11:30 AM · Reteti Midday Feed", "The open feeding slot (11:30 AM-1:00 PM): calves come in from the bush for their bottles and a Samburu keeper introduces each one. Book at least 24 hours ahead; wear dark green or khaki.", 75m, "Must-See", RetetiName),
                        new ItemSpec("Afternoon", "Mathews Range Forest Walk", "A guided walk into the forested 'sky island' — giant cycads, birds and elephant paths.", 30m, "Hidden Gem", NamunyakName),
                        new ItemSpec("Evening", "Around the Fire", "Stories and stars in Samburu country.", 0m, "Local Life"),
                    }),
                    new DaySpec(6, "North to the Ndotos", Wamba, Ngurunit, Ngurunit, new[]
                    {
                        new ItemSpec("Morning", "Long Drive to Ngurunit", "Back to the A2, north past Merille to Laisamis, then about 70 km of dirt road west to Ngurunit — plan on most of the day and check river crossings after rain.", 0m, "Road Trip"),
                        new ItemSpec("Afternoon", "Check In at Camp Ndotto", "A small eco-camp beside the river at the foot of the Ndoto Mountains. Book direct well ahead — rates aren't published.", 100m, "Hidden Gem", CampNdottoName),
                        new ItemSpec("Evening", "Dinner Under the Ndotos", "Home-cooked dinner and a sky full of stars.", 0m, "Off the Grid", CampNdottoName),
                    }),
                    new DaySpec(7, "Ndoto Mountains & Rock Pools", Ngurunit, Ngurunit, Ngurunit, new[]
                    {
                        new ItemSpec("Morning", "Guided Ndoto Hike", "Up the stream past the 400 m cliff with a local Samburu guide — start early before the heat.", 15m, "Adventure", NdotoName),
                        new ItemSpec("Morning", "Or: Full-Day Aldera Hike", "7-9 hours return to the Aldera viewpoint facing Mount Poi's huge granite walls.", 15m, "For Strong Hikers", NdotoName, true),
                        new ItemSpec("Afternoon", "Ngurunit Rock Pools", "Cool off in the natural pools and water slides — fullest just after the rains.", 0m, "Local Favorite", NdotoName),
                        new ItemSpec("Evening", "Last Night at Camp Ndotto", "One more evening by the river before the long drive south.", 100m, "Off the Grid", CampNdottoName),
                    }),
                    new DaySpec(8, "Return South — or Push On North", Ngurunit, Nanyuki, Nanyuki, new[]
                    {
                        new ItemSpec("Morning", "Drive Back to Nanyuki", "An early start: the dirt road to Laisamis, then about 300 km of tarmac south via Archer's Post and Isiolo — most of the day.", 0m, "Departure"),
                        new ItemSpec("Morning", "Or: Charter Flight to Wilson", "Save the long drive with a light-aircraft charter (priced per plane — confirm the airstrip with the operator).", 600m, "Optional", null, true),
                        new ItemSpec("Evening", "Back in Nanyuki", "Hot showers, a cold drink and an evening flight or drive to Nairobi.", 20m, "Journey's End"),
                        new ItemSpec("Evening", "Extension: Marsabit National Park", "Continue north up the A2 to the forested volcano, its crater lakes and elephants — add 1-2 nights.", 40m, "Optional Extension", MarsabitParkName, true),
                        new ItemSpec("Evening", "Extension: Lake Turkana at Loiyangalani", "The Jade Sea, El Molo villages and South Island — a long, rough expedition; add at least 3 nights.", 40m, "Optional Extension", TurkanaName, true),
                        new ItemSpec("Evening", "Extension: Chalbi Desert", "Cross the salt flats between North Horr and Kalacha on a Marsabit–Turkana loop — expedition 4x4 only.", 0m, "Optional Extension", ChalbiName, true),
                        new ItemSpec("Evening", "Extension: Shaba National Reserve", "Add a quiet day of game driving in Joy Adamson's Shaba on the way back past Archer's Post.", 85m, "Optional Extension", ShabaName, true),
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

                // Wishlist ↔ Destination links (add only if missing) — all destinations in the region, also for pre-existing wishlists.
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

        // Same scoring rules as BaliSeeder / AfricaPocSeeder / the Seattle seeder.
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
