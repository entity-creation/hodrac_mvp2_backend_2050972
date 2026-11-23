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
    /// "Carnival Calabar 2026" — a 6-day template wishlist for the peak week of Carnival Calabar
    /// (Fri 25 – Wed 30 Dec 2026) in Calabar, Cross River State, Nigeria, with optional
    /// extensions (New Year's Eve, the 1 Jan Tinkoriko procession, Obudu Mountain Resort and
    /// the Afi Mountain Drill Ranch).
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// Nigeria, NGN and the Nigerian languages use exactly the same natural keys as
    /// <see cref="AfricaPocSeeder"/>, so they are reused rather than duplicated.
    /// All prices are rough USD approximations (same convention as AfricaPocSeeder's Nigerian wishlists).
    /// Event dates, times (WAT) and venues follow the Carnival Calabar Commission's official 2026
    /// schedule and route (carnival.crossriverstate.gov.ng/schedule, /route) and can still change.
    /// </summary>
    public static class CalabarSeeder
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

        // Natural keys below match AfricaPocSeeder exactly (country name, currency code,
        // language names, time zone) so existing rows are found and reused.
        private const string Nigeria = "Nigeria";

        private const string English = "English";
        private const string Yoruba = "Yoruba";
        private const string Hausa = "Hausa";
        private const string Edo = "Edo (Bini)";
        private const string Efik = "Efik";

        private const string Calabar = "Calabar";
        private const string Obudu = "Obudu";
        private const string Buanchor = "Buanchor";

        private const string CarnivalRouteName = "Carnival Calabar Route";
        private const string MillenniumParkName = "Millennium Park Calabar"; // "Millennium Park" alone is Abuja's (AfricaPocSeeder)
        private const string StadiumName = "U.J. Esuene Stadium";
        private const string FestivalVillageName = "Calabar Festival Village (Christmas Village)";
        private const string ConventionCentreName = "Calabar International Convention Centre";
        private const string MuseumName = "Calabar Museum (Old Residency)";
        private const string MarinaName = "Marina Resort & Slave History Museum";
        private const string DrillRanchName = "Calabar Drill Ranch";
        private const string TinapaName = "Tinapa";
        private const string ObuduResortName = "Obudu Mountain Resort";
        private const string AfiName = "Afi Mountain Drill Ranch"; // formerly "Afi Mountain Drill Ranch & Canopy Walkway" (kept as an alternate)
        private const string BotanicGardenName = "Calabar Botanic Garden";
        private const string ArtsCraftsVillageName = "Arts and Crafts Village Calabar";
        private const string CulturalCentreName = "Calabar Cultural Centre";
        private const string GolfCourseName = "Calabar Golf Course";
        private const string ObongPalaceName = "Obong of Calabar's Palace";

        private static readonly LanguageSpec[] Languages =
        {
            // Same specs as AfricaPocSeeder — only created if missing.
            new(English, "en", Array.Empty<(string, string)>()),
            new(Yoruba, "yo", new[] { ("Thank you", "Ẹ ṣé"), ("How much?", "Ẹ̀lò ni?") }),
            new(Hausa, "ha", new[] { ("Hello", "Sannu"), ("Thank you", "Na gode") }),
            new(Edo, "bin", new[] { ("Greeting", "Kóyo") }),
            // New: the main local language of Calabar.
            new(Efik, "efi", new[] { ("How are you?", "Abadie?"), ("Thank you", "Sosongo") }),
        };

        private static readonly CurrencySpec[] Currencies =
        {
            new("NGN", "Nigerian Naira", "₦", 1500m),
        };

        private static readonly CountrySpec[] Countries =
        {
            new(Nigeria, "nigeria", "Africa", "🇳🇬",
                "Visa required for most nationalities (e-visa / visa on arrival approval via the Nigeria Immigration Service); check official requirements before travel.",
                "Type D/G", "Right", 0.075m, new[] { English, Yoruba, Hausa, Edo, Efik }, "NGN", "Africa/Lagos"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Calabar, Nigeria, 4.9767, 8.3383,
                "Capital of Cross River State in Nigeria's south-east, on the Calabar River — a green, laid-back city with colonial-era heritage, the Efik culture of the Ekpe society, and every December the Carnival Calabar, billed as 'Africa's Biggest Street Party'."),
            new(Obudu, Nigeria, 6.3867, 9.3739,
                "The Obudu Plateau in the far north of Cross River State near the Cameroon border — cool, green highlands around 1,600 m, home to Obudu Mountain Resort and a long drive from Calabar."),
            new(Buanchor, Nigeria, 6.3113, 8.9907,
                "A village in Boki, northern Cross River State, at the edge of the Afi Mountain Wildlife Sanctuary and the gateway to Pandrillus' Afi Mountain Drill Ranch field site."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] CalabarLangs = { English, Efik };

        private const string OfficialSchedule =
            "Official Schedule: Dates, times (WAT) and venues are from the Carnival Calabar Commission's official 2026 schedule and can still change — check carnival.crossriverstate.gov.ng before you go.";

        private static readonly DestSpec[] Destinations =
        {
            new("calabar", CarnivalRouteName, Nigeria, Calabar, CalabarLangs, 4.9700, 8.3350,
                10m, "Walk", 3,
                new[] { "activity_experience", "entertainment_nightlife", "market_street_life" },
                new[] { "cultural", "music", "nightlife", "social", "photography", "tourist_hotspot", "seasonal", "crowded" },
                new[] { "Calabar Carnival Route", "Carnival Calabar Parade Route", "Calabar Carnival", "Carnival Calabar", "Africa's Biggest Street Party" }, "Exterior",
                Desc(
                    "Carnival Calabar, billed as 'Africa's Biggest Street Party', takes over the streets of Calabar every December. The official route flags off at Eleven Eleven (Millennium Park), passes the adjudication point at Mary Slessor, continues to Atekong on Marian Road, turns north at the carnival point at Effio Ette Roundabout, reaches the adjudication point at Mobil on Murtala Mohammed Highway and ends at U.J. Esuene Stadium. Costumed bands, dancers, floats and music trucks are judged at Mary Slessor, Atekong and Mobil, and huge crowds line the roads.\n\nWhat to Do: Pick a roadside spot near one of the adjudication points to see the bands perform for the judges, follow a band for a stretch, or book a grandstand seat.",
                    "For Calabar residents, December is carnival month — the whole city prepares for it, the bands rehearse for weeks, and families come out together on parade days. Locals say the best spots near the judging points fill hours before the bands arrive, and that the atmosphere along the route is as much a part of the day as the parade itself.",
                    "Best Access: On foot — roads along the route are closed to traffic on parade days, so get dropped nearby early and walk in.\n\nOfficial Route: Eleven Eleven (Millennium Park) → Mary Slessor (adjudication) → Atekong, Marian Road (adjudication) → Effio Ette Roundabout (carnival point, northern turn) → Mobil, Murtala Mohammed Highway (adjudication) → U.J. Esuene Stadium (end).\n\nGetting Back: Arrange trusted transport in advance, as taxis are scarce when the parade ends.",
                    OfficialSchedule + "\n\nFree to Watch: Street viewing along the route is free; grandstand seats are paid, and VIP or ticketed shows are sold separately.\n\nOn the Route in 2026: Sat 26 Dec 9:00 AM Cultural Carnival (half the circuit, from Millennium Park through the Rabana turning); Mon 28 Dec 10:00 AM Carnival Calabar and Parade of Bands (the main event); Tue 29 Dec 12:00 PM Bikers Carnival. The Junior Carnival on Sun 27 Dec flags off at the Botanic Garden.\n\nDry Runs: Adult carnival dry run Sun 6 Dec 1:00 PM on the route; Junior Carnival dry run Sun 13 Dec 12:00 PM at Millennium Park.",
                    "Heat & Hydration: Parades are long and the December sun is strong — carry water, a hat and sunscreen.\n\nCrowds: The crowds are very dense; agree a meeting point with your group and keep children close.\n\nValuables: Carry only what you need and keep phones and cash secured.\n\nComfortable Shoes: Expect hours of standing and walking.\n\nNight Transport: Use trusted, pre-arranged transport after dark. Security is heavy along the route and the event is family-friendly, but normal big-crowd care applies.",
                    "Street Viewing: Free.\nGrandstand Seat: Price set by the organisers — confirm closer to the date.\nWater & Snacks from Vendors: $1-$5.\nTaxi / Driver for the Day: $15-$40 (higher in carnival week).",
                    new[]
                    {
                        "Millennium Park Calabar: Eleven Eleven, where the parade flags off.",
                        "U.J. Esuene Stadium: Where the route ends.",
                        "Calabar Festival Village: Nightly shows at 7:00 PM through the season."
                    },
                    "Parade days in the last week of December — arrive early in the morning to claim a spot near an adjudication point.",
                    "Very High (10/10) on parade days.",
                    "Rating: 3/10 — level city roads, but extremely crowded, hot and with long periods of standing.",
                    "A full day per parade.")),

            new("calabar", MillenniumParkName, Nigeria, Calabar, CalabarLangs, 4.9830, 8.3400,
                5m, "Walk", 3,
                new[] { "landmark_monument", "nature_outdoor" },
                new[] { "cultural", "photography", "family_friendly", "walkable", "budget_friendly", "seasonal" },
                new[] { "Millennium Park", "Calabar Millennium Park", "Eleven Eleven", "Eleven-Eleven Roundabout", "11/11 Roundabout", "Millennium Park Roundabout" }, "Exterior",
                Desc(
                    "Millennium Park, at the roundabout locals call Eleven Eleven, is one of Calabar's best-known open spaces and the official flag-off point of the Carnival Calabar parades. The season opens here with the Christmas Tree Lighting on 30 November, and on Christmas Eve the park hosts the Festival of Nine Lessons and Carols. On parade days the area fills with bands in full costume getting ready to set off along the route.\n\nWhat to Do: Watch the bands gather and flag off at the start of a parade, see the tree lighting or the carol service, and come by on December evenings for the festive lights.",
                    "For locals, Eleven Eleven is a familiar landmark and meeting point, and in December it becomes the symbolic starting line of the carnival. Residents say the start is the place to see costumes up close before the bands spread out along the route.",
                    "Best Access: In central Calabar — reach it by taxi or on foot from nearby hotels.\n\nParade Days: Roads around the park close to traffic, so get dropped off early and walk in.",
                    OfficialSchedule + "\n\n2026 Events Here: Mon 30 Nov 7:00 PM Christmas Tree Lighting; Sun 13 Dec 12:00 PM Junior Carnival dry run; Thu 24 Dec 6:00 PM Festival of Nine Lessons and Carols; Sat 26 Dec 9:00 AM Cultural Carnival flag-off; Mon 28 Dec 10:00 AM Parade of Bands flag-off.\n\nOpen Space: The park is free to visit.",
                    "Crowds at the Start: The area around the flag-off is very crowded — keep your group together.\n\nHeat: There is limited shade on parade days.\n\nValuables: Keep phones and cash secured in the crowd.",
                    "Entry: Free.\nDrinks & Snacks: $1-$5.\nTaxi in Calabar: $2-$6 (higher in carnival week).",
                    new[]
                    {
                        "Carnival Calabar Route: Starts here.",
                        "Calabar Museum (Old Residency): A short drive away.",
                        "U.J. Esuene Stadium: At the other end of the parade route."
                    },
                    "Early morning on parade days to see the bands flag off; 30 Nov and 24 Dec evenings for the tree lighting and carols.",
                    "Medium (4/10) normally, Very High (10/10) on parade days.",
                    "Rating: 6/10 — open, mostly level ground, but very crowded on event days.",
                    "1 to 2 hours, or longer on parade days.")),

            new("calabar", StadiumName, Nigeria, Calabar, CalabarLangs, 4.9677, 8.3269,
                25m, "Car", 3,
                new[] { "entertainment_nightlife", "activity_experience", "landmark_monument" },
                new[] { "music", "nightlife", "social", "crowded", "seasonal" },
                new[] { "UJ Esuene Stadium", "U. J. Esuene Stadium", "Esuene Stadium", "Calabar Stadium" }, "Exterior",
                Desc(
                    "U.J. Esuene Stadium is Calabar's main stadium, with a capacity of around 16,000, and the end of the official Carnival Calabar route. During peak week its main bowl hosts the carnival's big evening shows: the Calabar Festival Christmas Concert on Christmas Day, the Night of Kings and Queens of the Carnival Calabar Bands, the Feather Concert — the final display at the stadium after the Parade of Bands — and Insomnia Night. Davido has separately announced an appearance at the stadium on 29 December 2026.\n\nWhat to Do: Catch the bands arriving at the end of the route, and come back in the evening for a ticketed show.",
                    "Locals know the stadium as the finish line of the carnival and the home of Calabar's biggest December shows. They advise arriving early for concerts and planning your ride home before you go in.",
                    "Best Access: By taxi or pre-arranged driver; roads nearby can be closed or jammed on parade and concert nights.\n\nOn Foot: On parade days, walk the last stretch of the route to the stadium.",
                    OfficialSchedule + "\n\n2026 Events Here: Fri 25 Dec Calabar Festival Christmas Concert (time to be announced); Sat 26 Dec 5:00 PM Night of Kings and Queens of the Carnival Calabar Bands (with a Father Christmas poolside grotto); Mon 28 Dec 7:00 PM Feather Concert; Tue 29 Dec 7:00 PM Insomnia Night (billed with national acts by popular demand and an international act).\n\nTickets: Shows are ticketed separately from the free street parades — buy only from official channels.\n\nLine-ups: Artist appearances are as announced and can change.",
                    "Crowd Crushes at Gates: Entrances get very busy — arrive early and avoid pushing crowds.\n\nNight Transport: Arrange trusted transport home before the show.\n\nValuables: Carry the minimum and keep phones secured.",
                    "Show Ticket: Price set by the organisers — confirm closer to the date.\nDrinks inside: $2-$6.\nDriver / Taxi at Night: $10-$30 (carnival-week rates).",
                    new[]
                    {
                        "Carnival Calabar Route: The parade ends here.",
                        "Millennium Park Calabar: The other end of the route.",
                        "Calabar Festival Village: Nightly shows at 7:00 PM."
                    },
                    "Peak-week evenings (25-29 Dec) for the shows; late afternoon on parade days to see the bands finish.",
                    "Very High (9/10) on concert and parade days.",
                    "Rating: 5/10 — stadium stands with steps; very crowded entrances on event nights.",
                    "3 to 5 hours for a show.")),

            new("calabar", FestivalVillageName, Nigeria, Calabar, CalabarLangs, 4.9767, 8.3383,
                20m, "Car", 3,
                new[] { "entertainment_nightlife", "food_experience", "market_street_life" },
                new[] { "music", "nightlife", "social", "food_focused", "local_favorite", "seasonal" },
                new[] { "Calabar Christmas Village", "Christmas Village Calabar", "Calabar Festival Village", "Festival Village", "Xmas Village Calabar" }, "Exterior",
                Desc(
                    "The Calabar Festival Village — often called the Christmas Village — is the nightly hub of the carnival season. It officially opens on 1 December 2026 at 9:00 AM, and from 2 December to 1 January there are musical performances every night from 7:00 PM, including Calabar Got Talent, Night of a Thousand Dance and a Play-DJ competition, alongside food and traders' stalls. On New Year's Eve it hosts the Fireworks and Declaration of a New Year at midnight. The official schedule gives the venue only as 'Calabar Municipality' and the site has moved between years, so confirm the current location before you go.\n\nWhat to Do: Spend an evening on live music, talent shows and local food stalls, and if you're still in town, see in the New Year here.",
                    "For Calabar residents the village is where December evenings happen — families, friends and traders all turn out. Locals recommend trying regional dishes like edikang ikong and afang from the food stalls.",
                    "Best Access: By taxi or pre-arranged driver — confirm the current site with your hotel or the official carnival channels.\n\nNight Return: Arrange your ride home in advance.",
                    OfficialSchedule + "\n\n2026 Events Here: Tue 1 Dec 9:00 AM opening; nightly musical performances 7:00 PM, 2 Dec – 1 Jan; Thu 31 Dec midnight Fireworks and Declaration of a New Year.\n\nLocation: Listed only as 'Calabar Municipality' — confirm the address for 2026.\n\nEvents: Some concerts and VIP areas are ticketed separately.",
                    "Night Crowds: Busy and loud late in the evening — keep your group together.\n\nValuables: Keep phones and cash secured.\n\nTransport: Use trusted transport at night.",
                    "Entry: Often free or low cost; ticketed shows extra — confirm closer to the date.\nFood Stall Meal: $3-$10.\nDrinks: $1-$5.\nTaxi at Night: $5-$15.",
                    new[]
                    {
                        "Millennium Park Calabar: Festive lights and the parade start.",
                        "U.J. Esuene Stadium: Big peak-week shows.",
                        "Marina Resort: Waterfront events."
                    },
                    "Evenings from 7:00 PM, 2 December to 1 January; busiest in the last week of December and on New Year's Eve.",
                    "High (8/10) in the evenings, Very High (9/10) in carnival week.",
                    "Rating: 5/10 — open-air grounds, crowded and uneven in places.",
                    "2 to 4 hours.")),

            new("calabar", ConventionCentreName, Nigeria, Calabar, CalabarLangs, 4.9600, 8.3300,
                10m, "Car", 2,
                new[] { "activity_experience", "entertainment_nightlife" },
                new[] { "cultural", "art", "social", "seasonal" },
                new[] { "Calabar International Conference Centre", "CICC Calabar", "Calabar Convention Centre", "CICC" }, "Exterior",
                Desc(
                    "The Calabar International Convention Centre (CICC) is the city's main venue for conferences, exhibitions and formal events. During the carnival season it hosts evening shows on the official programme, including the African Fashion Fiesta and the Queen of Humanity pageant.\n\nWhat to Do: Catch a fashion show or pageant evening during the season.",
                    "Locals know the centre as the venue for the city's larger formal events — the carnival-season shows here are a dressier alternative to the street parties.",
                    "Best Access: By taxi or driver from central Calabar.\n\nEvents Only: Visits are usually tied to a scheduled event.",
                    OfficialSchedule + "\n\n2026 Events Here: Wed 23 Dec 5:00 PM African Fashion Fiesta; Sat 26 Dec 7:00 PM Queen of Humanity pageant.\n\nAccess: Some events require tickets or invitations.",
                    "Event-Only Access: Without a scheduled event there may be little to see — check ahead.\n\nDress: Smart-casual or dressier is usual for evening events.",
                    "Entry: Depends on the event — confirm closer to the date.\nTaxi in Calabar: $2-$6.",
                    new[]
                    {
                        "Calabar Museum (Old Residency): History of the city.",
                        "Calabar Festival Village: Nightly shows.",
                        "Millennium Park Calabar: The parade start."
                    },
                    "Scheduled evening events in December.",
                    "Medium (5/10) on event nights.",
                    "Rating: 7/10 — a modern indoor venue.",
                    "2 to 3 hours for an event.")),

            new("calabar", MuseumName, Nigeria, Calabar, CalabarLangs, 4.9757, 8.3417,
                5m, "Car", 2,
                new[] { "museum_gallery", "historical_tour", "landmark_monument" },
                new[] { "history", "cultural", "educational", "architecture", "art", "family_friendly", "budget_friendly" },
                new[] { "Old Residency Museum", "Calabar Museum", "National Museum Calabar", "Old Residency Calabar" }, "Exterior",
                Desc(
                    "The Calabar Museum — the National Museum at the Old Residency — occupies a prefabricated colonial building shipped from Britain and put up in 1884 on a hill above the Calabar River. Once the seat of the colonial administration, it later became a national monument and opened as a national museum in 1986. Its galleries cover the history of Calabar and the Efik people, the palm-oil trade and the transatlantic slave trade, colonial rule and traditional artefacts. During the carnival season it also hosts an arts exhibition and, on Christmas morning, a Christmas party for children.\n\nWhat to Do: Tour the galleries and the season's arts exhibition, then take in the views over the river from the hilltop grounds.",
                    "Calabar residents see the Old Residency as the keeper of the city's story — from the Efik trading houses to the colonial era. Locals suggest visiting before the Marina's Slave History Museum to understand the wider context.",
                    "Best Access: By taxi to the hilltop site above the river.\n\nCombine: The Marina Resort and Slave History Museum are on the waterfront below.",
                    OfficialSchedule + "\n\n2026 Events Here: Arts Exhibition opens Tue 1 Dec 4:00 PM and runs to 1 Jan, from 9:00 AM daily; Fri 25 Dec 10:00 AM Calabar Kiddie's Hero Xmas Party (costumed characters, for children).\n\nEntry Fee: A small fee is charged for the museum; check current prices.\n\nPhotography: Rules on photography inside can vary — ask at the desk.",
                    "Holiday Hours: Regular gallery hours may differ over Christmas — check ahead.\n\nHeat: Bring water — the galleries may not all be air-conditioned.",
                    "Entry: $1-$5.\nGuide Tip: $2-$5.\nTaxi in Calabar: $2-$6.",
                    new[]
                    {
                        "Marina Resort & Slave History Museum: On the waterfront below.",
                        "Calabar Drill Ranch: Drill monkey conservation centre in the city.",
                        "Millennium Park Calabar: A short drive."
                    },
                    "Mornings from 9:00 AM during the season-long arts exhibition (1 Dec – 1 Jan).",
                    "Low (3/10), busier on Christmas morning.",
                    "Rating: 5/10 — a historic building with steps and stairs between floors.",
                    "1 to 1.5 hours.")),

            new("calabar", MarinaName, Nigeria, Calabar, CalabarLangs, 4.9730, 8.3380,
                10m, "Car", 2,
                new[] { "museum_gallery", "historical_tour", "viewpoint_scenic_spot", "food_experience" },
                new[] { "history", "cultural", "educational", "photography", "food_focused" },
                new[] { "Slave History Museum", "Marina Resort Calabar", "Calabar Marina", "Slave History Museum Calabar", "Marina Resort Water Front" }, "Exterior",
                Desc(
                    "On the Calabar waterfront, Marina Resort is home to the Slave History Museum, set on the site of a former slave-trading warehouse. Opened in the late 2000s, the museum tells the story of the transatlantic slave trade through Calabar's port with displays and reconstructions. Around it, the resort has a promenade along the Calabar River, and in the carnival season it hosts the Mbuba Ubom boat regatta and the Calabar Festival of Grills.\n\nWhat to Do: Visit the Slave History Museum, walk the riverside promenade, and time your visit for the regatta or the Festival of Grills.",
                    "For many Calabar residents, the museum is an important place of memory for a history that shaped the city. The Mbuba Ubom regatta is traditional boat racing by the riverine houses of Old Calabar. Reports in recent years have described the museum as in need of maintenance, so expectations should be modest — but the story it tells is powerful.",
                    "Best Access: By taxi to the waterfront, below the Old Residency hill.\n\nCombine: Easily paired with the Calabar Museum.",
                    OfficialSchedule + "\n\n2026 Events Here: Fri 18 Dec 12:00 PM Boat Regatta – Mbuba Ubom; Tue 22 Dec 6:00 PM Calabar Festival of Grills (suya and grilled seafood) on the waterfront.\n\nEntry Fee: A small fee is charged for the museum; check current prices and holiday hours.",
                    "Condition: Some exhibits and facilities may be under repair.\n\nBoat Safety: Use operators who provide life jackets.\n\nSun: The waterfront is exposed — bring a hat and water.",
                    "Museum Entry: $1-$5.\nBoat Ride: $5-$15.\nFestival of Grills Plate: $3-$10.\nDrinks by the River: $2-$5.",
                    new[]
                    {
                        "Calabar Museum (Old Residency): On the hill above.",
                        "Calabar Festival Village: Nightly shows.",
                        "Calabar Drill Ranch: A short drive."
                    },
                    "Morning or late afternoon; 18 Dec for the regatta and 22 Dec evening for the Festival of Grills.",
                    "Low (3/10), High (7/10) during December events.",
                    "Rating: 6/10 — mostly level waterfront paths; some exhibits have steps.",
                    "1 to 2 hours, longer on event days.")),

            new("calabar", DrillRanchName, Nigeria, Calabar, CalabarLangs, 4.9650, 8.3450,
                10m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "educational", "family_friendly", "hidden_gem" },
                new[] { "Drill Ranch Calabar", "Pandrillus Drill Ranch", "Drill Monkey Ranch", "Calabar Drill Monkey Ranch" }, "Exterior",
                Desc(
                    "The Calabar Drill Ranch is the city headquarters of Pandrillus, the conservation organisation working to save the drill — an endangered primate found in south-east Nigeria and neighbouring Cameroon. The Calabar site is the project's office, quarantine and veterinary base, and visitors are shown around by staff to see drills and other rescued primates and learn about the conservation work.\n\nWhat to Do: Take the guided tour, learn about the drill and the project's work, and ask about the Afi Mountain field site if you're planning an extension.",
                    "Pandrillus has worked in Cross River State for decades, and the ranch is a quiet, educational break from the carnival crowds. Locals recommend it for families.",
                    "Best Access: By taxi within Calabar — confirm the current address and visiting arrangements with Pandrillus ahead of time.\n\nGuided Only: Every visit is accompanied by a staff member.",
                    "Contact Ahead: Check visiting hours over the holidays directly with Pandrillus.\n\nDonations: Visits support conservation — donations are welcome.\n\nRules: Follow staff instructions near the enclosures at all times.",
                    "Don't Feed or Touch: The animals are wild and some are rescued — keep your distance as instructed.\n\nHoliday Hours: May be limited around Christmas.",
                    "Entry / Donation: $3-$10.\nTaxi in Calabar: $2-$6.",
                    new[]
                    {
                        "Calabar Museum (Old Residency): A short drive.",
                        "Marina Resort & Slave History Museum: On the waterfront.",
                        "Afi Mountain Drill Ranch: Pandrillus' field site in the north of the state."
                    },
                    "Morning, when the animals are more active and it's cooler.",
                    "Low (2/10).",
                    "Rating: 6/10 — mostly level paths around the enclosures.",
                    "1 to 1.5 hours.")),

            new("calabar", TinapaName, Nigeria, Calabar, CalabarLangs, 5.0500, 8.3175,
                15m, "Car", 2,
                new[] { "activity_experience", "landmark_monument" },
                new[] { "photography", "architecture", "hidden_gem" },
                new[] { "Tinapa Resort", "Tinapa Business Resort", "Tinapa Free Zone", "Tinapa Calabar" }, "Exterior",
                Desc(
                    "Tinapa is a large business and leisure resort beside the Calabar River, a short drive north of the city, next to the Calabar Free Trade Zone. It was built as a shopping, conference and leisure destination, and its scale and architecture are striking — though activity at the complex has varied over the years, so check what is open before you go.\n\nWhat to Do: See the resort complex and riverside setting, and check whether any December events or facilities are open during your visit.",
                    "Calabar residents remember Tinapa's grand launch and have mixed feelings about how much of it is in use today. Locals suggest asking around for any carnival-season events being held there.",
                    "Best Access: Roughly 15-30 minutes' drive north of central Calabar by taxi or driver.\n\nTransport: Arrange your return trip in advance.",
                    "What's Open: Facilities and events vary — confirm what's running before you go.\n\nEntry: Rules and any fees depend on what's open.",
                    "Quiet Areas: Parts of the complex may be closed or quiet.\n\nTransport Back: Taxis may be scarce — keep your driver.",
                    "Driver Round Trip from Calabar: $15-$30.\nDrinks / Snacks: $2-$6.",
                    new[]
                    {
                        "Calabar Museum (Old Residency): Back in the city.",
                        "Marina Resort: The city waterfront.",
                        "Calabar Festival Village: Nightly shows."
                    },
                    "Morning or late afternoon.",
                    "Low (2/10).",
                    "Rating: 6/10 — large paved areas with long walking distances.",
                    "1.5 to 2 hours.")),

            new("calabar", ObuduResortName, Nigeria, Obudu, CalabarLangs, 6.3867, 9.3739,
                120m, "Car", 2,
                new[] { "nature_outdoor", "viewpoint_scenic_spot", "activity_experience" },
                new[] { "nature", "relaxing", "photography", "adventurous" },
                new[] { "Obudu Cattle Ranch", "Obudu Ranch", "Obudu Plateau", "Obudu Mountain Resort Cross River" }, "Landscape",
                Desc(
                    "Obudu Mountain Resort, the former Obudu Cattle Ranch, sits at around 1,600 m on the Obudu Plateau in the far north of Cross River State, near the Cameroon border. The cooler climate, rolling grasslands, forested valleys and mountain views make it a contrast to the coastal heat of Calabar. The resort is known for its cable car and walking trails, though which facilities are running varies — check ahead.\n\nWhat to Do: Walk the trails and viewpoints, enjoy the cooler air, and ask the resort about the cable car and guided walks.",
                    "Nigerians know Obudu as the country's classic highland escape. Locals say it is worth at least two nights because of the long journey there.",
                    "Best Access: A long road trip from Calabar — most of a day's drive to the north of the state. Plan it as an extension, not a day trip.\n\nBy Driver: Hire a driver familiar with the route; the final climb to the plateau is winding.",
                    "Optional Extension: Best added after the carnival week, with two or more nights.\n\nFacilities: Confirm which facilities (cable car, restaurants) are open for your dates.\n\nBooking: Reserve accommodation in advance over the holidays.",
                    "Long Drive: Travel only in daylight and plan rest stops.\n\nCold Nights: It is much cooler than Calabar — bring a warm layer.\n\nMountain Roads: Winding roads up the plateau.",
                    "Driver for the Round Trip: $150-$300.\nResort Room per Night: $60-$150.\nMeals: $10-$25 per day.",
                    new[]
                    {
                        "Afi Mountain Drill Ranch: Another northern Cross River nature stop.",
                        "Obudu Plateau Trails: Around the resort.",
                        "Calabar: Most of a day's drive south."
                    },
                    "The dry season (roughly November to March) for clearer roads; after the carnival week.",
                    "Medium (5/10) over the holidays.",
                    "Rating: 4/10 — hilly terrain with steep paths.",
                    "2 to 3 nights including travel.")),

            new("calabar", AfiName, Nigeria, Buanchor, CalabarLangs, 6.3113, 8.9907,
                60m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "adventurous", "hidden_gem", "educational" },
                new[] { "Afi Mountain Drill Ranch & Canopy Walkway", "Afi Drill Ranch", "Afi Mountain", "Pandrillus Afi", "Afi Mountain Wildlife Sanctuary" }, "Landscape",
                Desc(
                    "Pandrillus' Afi Mountain Drill Ranch near Buanchor village is the field site where rescued drills and chimpanzees live in large forest enclosures at the foot of Afi Mountain, next to the Afi Mountain Wildlife Sanctuary. It is remote and off-grid — there is no electricity and no mobile network at the ranch. Simple guest accommodation has been available on site.\n\nWhat to Do: See the drills and chimpanzees with Pandrillus staff and hike the forest trails — expect a lot of walking. The ranch's canopy walkway: most of it was destroyed in a 2012 landslide — ask whether any section is open.",
                    "Pandrillus has worked with local communities around Afi for many years. Locals and staff ask visitors to arrange their stay in advance rather than turning up unannounced.",
                    "By Car Only: Getting there is by car, and it is a very long, challenging journey — Pandrillus has quoted roughly 4-6 hours north from Calabar, via the road through Olum village to Buanchor. Travel in daylight with a driver who knows the route.\n\nArrange First: Contact Pandrillus directly before travelling for current access, accommodation and security advice.",
                    "Optional Extension: Best done as an overnight trip after the carnival week.\n\nNo Power or Network: There is no electricity and no mobile network at the ranch — tell people your plans before you go and expect to be offline.\n\nPacking: Comfortable hiking shoes (there's a lot of hiking), mosquito repellent, a power bank and offline maps.\n\nGo With Others: Come as a group or with a tour buddy rather than alone.\n\nCanopy Walkway: Most of it was destroyed in a 2012 landslide — ask whether any section is open.\n\nBooking: Accommodation and visits are arranged through Pandrillus.",
                    "Current Access: Check access and security conditions with Pandrillus before you go.\n\nLong, Hard Journey: The drive is very long and challenging — leave early, carry water and snacks, and don't plan it as a day trip.\n\nOff the Grid: No electricity and no mobile network — charge everything before you leave and carry a power bank.\n\nDon't Go Alone: Travel as a group or with a tour buddy.\n\nMosquitoes: Bring and use repellent.\n\nForest Conditions: There's a lot of hiking on paths that can be steep and slippery — wear comfortable, sturdy shoes.\n\nWildlife: Follow staff instructions near the enclosures.",
                    "Driver for the Round Trip: $120-$250.\nAccommodation & Visit Fees: Confirm with Pandrillus.\nMeals: $10-$20 per day.",
                    new[]
                    {
                        "Calabar Drill Ranch: Pandrillus' city headquarters.",
                        "Obudu Mountain Resort: Further north in the state.",
                        "Afi Mountain Wildlife Sanctuary: Surrounding forest."
                    },
                    "The dry season (roughly November to March).",
                    "Low (2/10).",
                    "Rating: 3/10 — rough rural roads and forest paths.",
                    "1 to 2 nights including travel.")),

            new("calabar", BotanicGardenName, Nigeria, Calabar, CalabarLangs, 4.9600, 8.3330,
                5m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "cultural", "family_friendly", "nature", "seasonal", "photography" },
                new[] { "Calabar Botanical Garden", "Botanic Garden Calabar", "Calabar Botanic Garden and Conservation Centre", "Botanical Garden Calabar" }, "Exterior",
                Desc(
                    "The Calabar Botanic Garden is a green space in the city with roots in the colonial-era botanic garden. In the carnival season it is the flag-off point for the Junior Carnival — the children's parade, themed 'Future Assured' — and the venue for Nyoro Ekpe, the celebration of the Ekpe society, whose masquerades are central to Efik culture.\n\nWhat to Do: Watch the children's bands set off on Junior Carnival morning, and come back for the Ekpe masquerades at Nyoro Ekpe.",
                    "Ekpe is one of the most important traditional institutions of the Efik and neighbouring peoples, and its masquerades are treated with great respect. Locals ask visitors to follow guidance about where to stand and what to photograph during Ekpe events.",
                    "Best Access: By taxi within central Calabar; roads nearby may close on event mornings.\n\nArrive Early: Get there before the 10:00 AM starts to find a spot.",
                    OfficialSchedule + "\n\n2026 Events Here: Sun 27 Dec 10:00 AM Junior Carnival (Future Assured) flag-off; Wed 30 Dec 10:00 AM Nyoro Ekpe (Ekpe celebration).\n\nRespect Ekpe: Follow local guidance around the masquerades — some rites are not for outsiders.\n\nCondition: Parts of the garden have been reported as neglected in the past.",
                    "Crowds: Busy on event mornings — keep children close.\n\nPhotography: Ask before photographing Ekpe masquerades and participants.\n\nHeat: Bring water and a hat.",
                    "Entry: Free on event days (confirm).\nDrinks & Snacks: $1-$5.\nTaxi in Calabar: $2-$6.",
                    new[]
                    {
                        "Carnival Calabar Route: The Junior Carnival heads out from here.",
                        "Calabar Museum (Old Residency): History of the Efik and Ekpe.",
                        "Millennium Park Calabar: The main parade start."
                    },
                    "27 Dec and 30 Dec mornings from 10:00 AM.",
                    "High (7/10) on event days.",
                    "Rating: 5/10 — garden paths, some uneven; crowded on event days.",
                    "2 to 3 hours on event days.")),

            new("calabar", ArtsCraftsVillageName, Nigeria, Calabar, CalabarLangs, 4.9790, 8.3380,
                10m, "Car", 2,
                new[] { "market_street_life", "museum_gallery" },
                new[] { "cultural", "art", "local_favorite", "seasonal", "educational" },
                new[] { "Arts and Crafts Village", "Calabar Arts and Crafts Village", "Local Government Village Calabar", "Arts & Crafts Village Calabar" }, "Exterior",
                Desc(
                    "The Arts and Crafts Village, on the grounds of the Calabar Cultural Centre, becomes the Local Government Village during the carnival season — a showcase of crafts, food and heritage from Cross River State's 18 local government areas. It opens in early December, with exhibitions from the North, Central and South senatorial districts on separate days.\n\nWhat to Do: Browse crafts and local produce, and visit on a senatorial-district exhibition day to see each region's heritage.",
                    "Each local government area brings its own crafts, dress, food and performers, so the village is a chance to see Cross River beyond Calabar in one place.",
                    "Best Access: By taxi to the Cultural Centre grounds in central Calabar.\n\nCombine: The Cultural Centre main bowl is on the same grounds.",
                    OfficialSchedule + "\n\n2026 Events Here: Wed 2 Dec 12:00 PM Local Government Village opening; senatorial district exhibitions at 12:00 PM on Thu 3 Dec (North), Tue 8 Dec (Central) and Wed 16 Dec (South).\n\nBargaining: Friendly negotiation is normal at craft stalls.",
                    "Crowds: Busy on exhibition days.\n\nCash: Carry small notes; card payment may not be available.",
                    "Entry: Free (confirm).\nCrafts & Souvenirs: $3-$30.\nLocal Food: $2-$8.",
                    new[]
                    {
                        "Calabar Cultural Centre: Main bowl events on the same grounds.",
                        "Millennium Park Calabar: A short drive.",
                        "Calabar Festival Village: Nightly shows."
                    },
                    "Exhibition days in early-to-mid December from 12:00 PM.",
                    "Medium (5/10).",
                    "Rating: 6/10 — mostly level grounds with stalls.",
                    "1 to 2 hours.")),

            new("calabar", CulturalCentreName, Nigeria, Calabar, CalabarLangs, 4.9790, 8.3385,
                15m, "Car", 2,
                new[] { "entertainment_nightlife", "activity_experience" },
                new[] { "cultural", "music", "art", "nightlife", "seasonal" },
                new[] { "Cultural Centre Calabar", "Calabar Cultural Center", "Cultural Center Main Bowl", "Cultural Centre Main Bowl" }, "Exterior",
                Desc(
                    "The Calabar Cultural Centre in central Calabar is a performance venue whose main bowl hosts cultural shows, concerts and awards during the carnival season. Its grounds also hold the Arts and Crafts Village. On the 2026 programme it hosts the Cross River Movie Award and the Paradise Music and Festival Award.\n\nWhat to Do: Catch an awards night or performance in the main bowl and browse the crafts village next door.",
                    "Locals know the Cultural Centre as a long-standing home for theatre, dance and music in Calabar.",
                    "Best Access: By taxi in central Calabar.\n\nNight Return: Arrange transport home after evening shows.",
                    OfficialSchedule + "\n\n2026 Events Here: Wed 16 Dec 7:00 PM Cross River Movie Award / Paradise Music and Festival Award (Main Bowl).\n\nTickets: Evening events may be ticketed or by invitation.",
                    "Night Transport: Use trusted transport after dark.\n\nCrowds: Entrances get busy on award nights.",
                    "Event Ticket: Confirm closer to the date.\nTaxi at Night: $5-$15.",
                    new[]
                    {
                        "Arts and Crafts Village Calabar: On the same grounds.",
                        "Calabar Festival Village: Nightly shows.",
                        "Millennium Park Calabar: A short drive."
                    },
                    "Scheduled evening events in December.",
                    "Medium (6/10) on event nights.",
                    "Rating: 6/10 — an older venue with steps in the seating areas.",
                    "2 to 3 hours for an event.")),

            new("calabar", GolfCourseName, Nigeria, Calabar, CalabarLangs, 5.0000, 8.3400,
                60m, "Car", 1,
                new[] { "activity_experience", "nature_outdoor" },
                new[] { "relaxing", "nature", "seasonal" },
                new[] { "Calabar Golf Club", "Calabar Golf Course Cross River", "Mary Slessor Golf Tournament" }, "Landscape",
                Desc(
                    "Calabar Golf Club's 18-hole, par-72 course lies north of the city, rolling through valleys above the Calabar River. During the carnival season it hosts the Mary Slessor Golf Tournament.\n\nWhat to Do: Play a round (by arrangement with the club) or follow the Mary Slessor Golf Tournament.",
                    "Golf has a long history in Calabar — the club traces trophies back to the 1890s — and the state has been promoting golf tourism in recent years.",
                    "Best Access: By taxi or driver north of central Calabar; confirm the exact entrance with the club.\n\nBooking: Arrange tee times and equipment hire with the club in advance.",
                    OfficialSchedule + "\n\n2026 Events Here: Thu 17 Dec 9:00 AM Mary Slessor Golf Tournament.\n\nAccess: Playing as a visitor is by arrangement with the club.",
                    "Heat: Tee off early and carry plenty of water.\n\nHilly Course: Expect slopes and walking between holes.",
                    "Green Fee & Caddie: Confirm with the club.\nDriver Round Trip: $15-$30.",
                    new[]
                    {
                        "Tinapa: Also north of the city centre.",
                        "Calabar Festival Village: Evening shows.",
                        "Calabar Museum (Old Residency): City history."
                    },
                    "Early mornings in the dry season; 17 Dec for the tournament.",
                    "Low (2/10).",
                    "Rating: 4/10 — hilly course terrain.",
                    "4 to 5 hours for a round.")),

            new("calabar", ObongPalaceName, Nigeria, Calabar, CalabarLangs, 4.9550, 8.3250,
                5m, "Car", 2,
                new[] { "historical_tour", "landmark_monument" },
                new[] { "cultural", "history", "seasonal" },
                new[] { "Obong's Coliseum", "Obong's Palace Calabar", "Palace of the Obong of Calabar", "Utomo Obong" }, "Exterior",
                Desc(
                    "The Obong of Calabar is the traditional ruler of the Efik Kingdom. Each December, Utomo Obong pays homage to the Obong with a programme of traditional ceremonies, and the official 2026 carnival schedule lists it at Obong's Coliseum. Venues for Utomo Obong events have varied between years — the Obong's palace in Calabar and his country home at Adiabo have both been reported — so confirm the venue for 2026.\n\nWhat to Do: If the ceremony is open to visitors, watch the homage to the Obong with Efik dress, music and dance.",
                    "Utomo Obong is an important occasion for the Efik people and their monarchy; it is a ceremony first and a spectacle second. Locals ask visitors to dress respectfully and follow protocol.",
                    "Best Access: By taxi — confirm the exact venue before you set out.\n\nProtocol: Follow the guidance of organisers and palace officials.",
                    OfficialSchedule + "\n\n2026 Events Here: Sat 19 Dec 10:00 AM Utomo Obong (homage to the Obong of Calabar, Efik Kingdom), listed at Obong's Coliseum.\n\nAccess: Parts of the ceremony may be by invitation only.",
                    "Respect Protocol: Dress modestly, don't walk in front of dignitaries and ask before taking photos.\n\nVenue: Confirm the location — it has varied between years.",
                    "Entry: Free / by invitation (confirm).\nTaxi in Calabar: $2-$6.",
                    new[]
                    {
                        "Calabar Museum (Old Residency): Efik history.",
                        "Marina Resort & Slave History Museum: On the waterfront.",
                        "Calabar Botanic Garden: Venue of Nyoro Ekpe."
                    },
                    "19 Dec from 10:00 AM.",
                    "Medium (6/10) on the day.",
                    "Rating: 5/10 — depends on the venue; expect standing crowds.",
                    "2 to 3 hours.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlist
        // ═══════════════════════════════════════════════════════════════════════

        // ItineraryItem.TimeOfDay is a controlled value (Morning/Afternoon/Evening — validated by the
        // admin and user itinerary endpoints), so the official WAT start times go in the item titles.
        private static readonly WishlistSpec[] Wishlists =
        {
            new("calabar", "Carnival Calabar 2026", Nigeria,
                "Carnival Calabar — 'Africa's Biggest Street Party' — is a whole season in Calabar, Cross River State: 62 official events across the city, from the Christmas Tree Lighting at Millennium Park on 30 November to 1 January 2027. The Festival Village opens on 1 December with shows every night from 7:00 PM, and the season includes an arts exhibition at the Old Residency, the Local Government Village, the Mbuba Ubom boat regatta, Utomo Obong, the Festival of Grills, the African Fashion Fiesta and carols at Millennium Park. This wishlist covers the peak week, Fri 25 – Wed 30 Dec 2026: the Christmas Concert, the Cultural Carnival and Night of Kings and Queens, the Junior Carnival from the Botanic Garden, the main Parade of Bands from Eleven Eleven (Millennium Park) to U.J. Esuene Stadium and the Feather Concert, the Bikers Carnival and Insomnia Night, and Nyoro Ekpe — with Calabar's museums, the Drill Ranch and Tinapa around them. Times (WAT) follow the Carnival Calabar Commission's official 2026 schedule and can still change. Optional extensions: New Year's Eve fireworks at the Festival Village, the 1 January Tinkoriko heritage procession, Obudu Mountain Resort and the Afi Mountain Drill Ranch.",
                "Costumed bands, music trucks and a whole city on the streets — six days of Africa's Biggest Street Party, with Calabar's history, Ekpe masquerades and wildlife in between.",
                "Friends & Festival-Goers",
                new[] { "5 Nights Hotel in Calabar (book by November)", "Airport Transfers", "Grandstand Seat for the Parade of Bands", "Driver for Tinapa & City Sights", "Museum & Drill Ranch Entry" },
                "Carnival Calabar, Calabar Carnival, Africa's Biggest Street Party, Calabar, Cross River, Nigeria, Parade of Bands, Cultural Carnival, Night of Kings and Queens, Junior Carnival, Bikers Carnival, Feather Concert, Insomnia Night, Christmas Concert, Nyoro Ekpe, Ekpe, Utomo Obong, Mbuba Ubom regatta, Festival of Grills, Tinkoriko, Christmas Village, Festival Village, Eleven Eleven, Millennium Park, U.J. Esuene Stadium, Botanic Garden, Davido, Detty December, Old Residency, Slave History Museum, Drill Ranch, Tinapa, Obudu, Afi Mountain, Efik, edikang ikong, afang",
                new[] { "Festival", "Culture", "Music", "Nightlife" },
                900m, 150m,
                "5 nights in a mid-range Calabar hotel (shared double) — stock is small, rooms sell out by November and parade-week rates are roughly double",
                "Return flight to Margaret Ekpo International Airport (about 1 hour from Lagos, also from Abuja) not included; airport transfers and a driver for the Tinapa and sightseeing day; walking on parade days when roads are closed",
                "Christmas Concert or Festival Village show, Cultural Carnival, Night of Kings and Queens, Junior Carnival, Parade of Bands with a grandstand seat, Feather Concert, Bikers Carnival, Insomnia Night, Nyoro Ekpe, Calabar Museum and arts exhibition, Slave History Museum, Calabar Drill Ranch, Tinapa",
                true, "Festival-Loving Friends",
                new[]
                {
                    new DaySpec(1, "Fri 25 Dec · Christmas Day & Festival Village", Calabar, Calabar, Calabar, new[]
                    {
                        new ItemSpec("Morning", "Fly into Calabar", "Arrive at Margaret Ekpo International Airport — about an hour's flight from Lagos, with flights from Abuja too. The road from Lagos takes 10+ hours and isn't advised in December.", 10m, "Arrival"),
                        new ItemSpec("Morning", "10:00 AM · Kiddie's Hero Xmas Party", "Travelling with children? The Calabar Kiddie's Hero Xmas Party brings costumed characters to the Old Residency Museum.", 0m, "Family Option", MuseumName, true),
                        new ItemSpec("Afternoon", "Check In & Christmas Lunch", "Settle in and try Calabar classics — edikang ikong, afang soup and fresh seafood.", 12m, "Foodie Pick"),
                        new ItemSpec("Evening", "Calabar Festival Christmas Concert", "Christmas night at U.J. Esuene Stadium — start time to be announced; tickets from official channels only.", 20m, "Christmas Night", StadiumName),
                        new ItemSpec("Evening", "7:00 PM · Festival Village Show", "Or head to the Festival Village for its nightly 7:00 PM performances — confirm the current site.", 10m, "Optional Night Out", FestivalVillageName, true),
                        new ItemSpec("Evening", "Come Earlier: The Season Before Peak Week", "The official season starts on 30 Nov (7:00 PM Christmas Tree Lighting, Millennium Park). Highlights before Christmas: 1 Dec Festival Village opening (9:00 AM) and arts exhibition at the Old Residency (4:00 PM); 2 Dec Local Government Village (12:00 PM); 6 Dec adult dry run (1:00 PM) and 13 Dec Junior dry run (12:00 PM); 18 Dec Mbuba Ubom regatta; 19 Dec Utomo Obong; 22 Dec Festival of Grills; 24 Dec carols at Millennium Park.", 0m, "Optional Early Season", MillenniumParkName, true),
                    }),
                    new DaySpec(2, "Sat 26 Dec · Cultural Carnival & Night of Kings and Queens", Calabar, Calabar, Calabar, new[]
                    {
                        new ItemSpec("Morning", "9:00 AM · Cultural Carnival", "Cultural troupes parade half the carnival circuit, flagging off at Millennium Park and running through the Rabana turning.", 0m, "Peak Week", CarnivalRouteName),
                        new ItemSpec("Afternoon", "Calabar Museum & Arts Exhibition", "Calabar's story — the Efik, the river trade and colonial rule — in the 1884 Old Residency, plus the season's arts exhibition (open from 9:00 AM daily).", 3m, "History", MuseumName),
                        new ItemSpec("Afternoon", "Slave History Museum at Marina Resort", "Visit the museum on the waterfront and walk the riverside promenade.", 3m, "Must-See", MarinaName),
                        new ItemSpec("Evening", "5:00 PM · Night of Kings and Queens", "The Night of Kings and Queens of the Carnival Calabar Bands at U.J. Esuene Stadium, with a Father Christmas poolside grotto.", 15m, "Carnival Night", StadiumName),
                        new ItemSpec("Evening", "7:00 PM · Queen of Humanity Pageant", "A pageant evening at the Calabar International Convention Centre.", 10m, "Optional Show", ConventionCentreName, true),
                    }),
                    new DaySpec(3, "Sun 27 Dec · Junior Carnival", Calabar, Calabar, Calabar, new[]
                    {
                        new ItemSpec("Morning", "10:00 AM · Junior Carnival Flag-off", "Children's bands in full costume set off from the Botanic Garden — this year's Junior Carnival theme is 'Future Assured'. A family favourite.", 0m, "Family Favorite", BotanicGardenName),
                        new ItemSpec("Afternoon", "Calabar Drill Ranch", "A guided visit to Pandrillus' drill monkey conservation centre — contact them ahead over the holidays.", 8m, "Wildlife", DrillRanchName),
                        new ItemSpec("Evening", "Seafood Dinner", "Fresh fish and seafood, Calabar style — rest up for tomorrow's big parade.", 15m, "Foodie Pick"),
                        new ItemSpec("Evening", "7:00 PM · Festival Village Show", "The nightly performances continue at the Festival Village.", 10m, "Optional Night Out", FestivalVillageName, true),
                    }),
                    new DaySpec(4, "Mon 28 Dec · Parade of the Bands", Calabar, Calabar, Calabar, new[]
                    {
                        new ItemSpec("Morning", "10:00 AM · Flag-off at Eleven Eleven", "The main event — Carnival Calabar and Parade of Bands — flags off at Eleven Eleven (Millennium Park).", 0m, "Parade Start", MillenniumParkName),
                        new ItemSpec("Morning", "Claim a Spot at a Judging Point", "The bands perform for the judges at Mary Slessor, Atekong (Marian Road) and Mobil (Murtala Mohammed Highway) — the best spots there fill hours early. Bring water, a hat and comfortable shoes.", 0m, "Insider Tip", CarnivalRouteName),
                        new ItemSpec("Afternoon", "Parade of Bands", "Costumed bands dance the route past Effio Ette Roundabout to the finish at U.J. Esuene Stadium. Street viewing is free.", 0m, "Must-See", CarnivalRouteName),
                        new ItemSpec("Afternoon", "Grandstand Seat", "A paid grandstand seat for a better view of the judged performances — prices and locations set by the organisers.", 30m, "Best View", CarnivalRouteName),
                        new ItemSpec("Evening", "7:00 PM · Feather Concert", "The final display at U.J. Esuene Stadium, at the end of the route.", 20m, "Grand Finale", StadiumName),
                    }),
                    new DaySpec(5, "Tue 29 Dec · Bikers Carnival & Insomnia Night", Calabar, Calabar, Calabar, new[]
                    {
                        new ItemSpec("Morning", "Tinapa", "A short drive north to the riverside business and leisure resort — check what's open.", 20m, "Off the Beaten Path", TinapaName, true),
                        new ItemSpec("Afternoon", "12:00 PM · Bikers Carnival", "Bikers roll along the carnival route.", 0m, "Peak Week", CarnivalRouteName),
                        new ItemSpec("Evening", "7:00 PM · Insomnia Night", "A late show at U.J. Esuene Stadium, billed with national acts by popular demand and an international act. Davido has separately announced an appearance at the stadium on 29 Dec — buy tickets only from official channels.", 30m, "Headline Show", StadiumName),
                    }),
                    new DaySpec(6, "Wed 30 Dec · Nyoro Ekpe & Departure", Calabar, Calabar, Calabar, new[]
                    {
                        new ItemSpec("Morning", "10:00 AM · Nyoro Ekpe", "The Ekpe celebration at the Botanical Garden — Efik masquerades and tradition. Ask before taking photos.", 0m, "Living Tradition", BotanicGardenName),
                        new ItemSpec("Afternoon", "Last Lunch in Calabar", "One more plate of afang or edikang ikong before you go.", 12m, "Foodie Pick"),
                        new ItemSpec("Evening", "Airport Transfer", "Transfer to Margaret Ekpo International Airport — or stay on for an extension.", 10m, "Departure"),
                        new ItemSpec("Evening", "Stay On: New Year's Eve Fireworks", "Thu 31 Dec at midnight — Fireworks and Declaration of a New Year at the Festival Village.", 10m, "Optional Extension", FestivalVillageName, true),
                        new ItemSpec("Evening", "Stay On: 12:00 PM 1 Jan · Tinkoriko Procession", "Fri 1 Jan 2027 heritage procession via Egerton St, Ekeng Ita, Hawkins Rd, Chambly St, Target Rd, Zoo/Mary Slessor Roundabout, Marian Road and Barracks Road to the Cenotaph.", 0m, "Optional Extension", null, true),
                        new ItemSpec("Evening", "Extension: Obudu Mountain Resort", "Swap the heat for the cool Obudu Plateau — most of a day's drive north, so plan two or more nights.", 250m, "Optional Extension", ObuduResortName, true),
                        new ItemSpec("Evening", "Extension: Afi Mountain Drill Ranch", "An overnight trip to Pandrillus' off-grid forest field site (no power or mobile network) — a long, challenging drive, so go as a group and confirm access with Pandrillus first.", 150m, "Optional Extension", AfiName, true),
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

            // ─── Batch 2b: renamed destinations (already seeded under an old name) ─
            await ApplyRenamesAsync(db, countryByName, ct);

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

                // Wishlist ↔ Destination links (add only if missing) — all Calabar destinations, also for pre-existing wishlists.
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

        // Destinations renamed after the Calabar seed already ran somewhere (production).
        // Old name → current name. The seeder otherwise matches by name only, so without
        // this a re-run would create a second destination and leave the old one behind.
        private static readonly (string OldName, string NewName)[] Renames =
        {
            ("Afi Mountain Drill Ranch & Canopy Walkway", AfiName),
        };

        // Renames the old row in place and refreshes its text from the current spec and the
        // template wishlist's itinerary item for it (users' own wishlist copies keep their
        // text). The image URL only changes if the spec's image file differs from the old
        // name's (Afi keeps its uploaded file via ImageFileOverrides). Idempotent:
        // does nothing once the old name is gone, or if the new name already exists.
        private static async Task ApplyRenamesAsync(
            HodracDbContext db, IReadOnlyDictionary<string, Country> countryByName, CancellationToken ct)
        {
            var specByName = Destinations.ToDictionary(d => d.Name);
            foreach (var (oldName, newName) in Renames)
            {
                var spec = specByName[newName];
                var countryId = countryByName[spec.CountryName].CountryId;
                var old = await db.Destinations
                    .FirstOrDefaultAsync(d => d.DestinationName == oldName && d.CountryId == countryId, ct);
                if (old == null) continue;
                if (await db.Destinations.AnyAsync(d => d.DestinationName == newName && d.CountryId == countryId, ct))
                {
                    Console.WriteLine($"Calabar seed: both \"{oldName}\" and \"{newName}\" exist; left as is — merge by hand.");
                    continue;
                }

                var search = DestinationSearchFields.Derive(newName);
                old.DestinationName = newName;
                old.CleanNormalizedSearchName = search.CleanNormalizedSearchName;
                old.MetaphoneCode = search.MetaphoneCode;
                old.DoubleMetaphonePrimary = search.DoubleMetaphonePrimary;
                old.DoubleMetaphoneSecondary = search.DoubleMetaphoneSecondary;
                old.CommonAlternateSpellingsJson = JsonSerializer.Serialize(spec.Alternates, JsonOpts);
                old.DescriptionJson = JsonSerializer.Serialize(spec.Desc, JsonOpts);

                // The old file under the current host and every legacy host (rows seeded before a host move).
                var oldImageUrls = SeedImages.UrlsUnderAllBases($"{spec.Region}_{DestinationSearchFields.Normalize(oldName).Replace(' ', '-')}.jpg");
                var newImageUrl = DestinationImageUrl(spec);
                foreach (var image in await db.DestinationImages
                             .Where(i => i.DestinationId == old.DestinationId && oldImageUrls.Contains(i.ImageUrl)).ToListAsync(ct))
                {
                    image.ImageUrl = newImageUrl;
                    image.ThumbnailUrl = newImageUrl;
                    image.Caption = $"Hero image of {newName}";
                }

                var itemSpec = Wishlists.SelectMany(w => w.Days).SelectMany(d => d.Items)
                    .FirstOrDefault(i => i.DestinationName == newName);
                var wishlistNames = Wishlists.Select(s => s.Name).ToList();
                var templateIds = await db.Wishlists
                    .Where(w => w.IsTemplate && wishlistNames.Contains(w.WishlistName))
                    .Select(w => w.WishlistId).ToListAsync(ct);
                foreach (var item in await db.ItineraryItems
                             .Where(i => oldImageUrls.Contains(i.ImageUrl)).Include(i => i.ItineraryDay).ToListAsync(ct))
                {
                    item.ImageUrl = newImageUrl;
                    if (itemSpec != null && item.ItineraryDay != null && templateIds.Contains(item.ItineraryDay.WishlistId))
                    {
                        item.ItemTitle = itemSpec.Title;
                        item.ItemDescription = itemSpec.Description;
                    }
                }

                await db.SaveChangesAsync(ct);
                Console.WriteLine($"Calabar seed: renamed \"{oldName}\" to \"{newName}\" and refreshed its text and image.");
            }
        }

        // Destinations whose image was uploaded under an earlier name keep that file.
        private static readonly Dictionary<string, string> ImageFileOverrides = new()
        {
            [AfiName] = "calabar_afi-mountain-drill-ranch-and-canopy-walkway.jpg", // already uploaded in production
        };

        private static string DestinationImageFile(DestSpec spec) =>
            ImageFileOverrides.TryGetValue(spec.Name, out var file)
                ? file
                : $"{spec.Region}_{DestinationSearchFields.Normalize(spec.Name).Replace(' ', '-')}.jpg";

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
