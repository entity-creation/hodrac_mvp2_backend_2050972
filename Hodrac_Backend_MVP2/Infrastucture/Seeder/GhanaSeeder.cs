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
    /// "Ghana: Castles, Canopies and Accra Culture": a 7-day template wishlist pitched at Nigerian
    /// travellers. Accra (Kwame Nkrumah Memorial Park, Jamestown, Makola, Labadi, Artists Alliance
    /// Gallery), the Central Region coast (Cape Coast Castle, Kakum canopy walkway, Elmina Castle) and
    /// a day in the Akuapem hills (Aburi Botanical Gardens, Boti Falls), with optional Akosombo/Volta
    /// and Kumasi (Manhyia Palace Museum, Kejetia Market) extensions.
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// Ghana, GHS and Twi (Akan) are new; English is reused by its existing natural key.
    /// All prices are rough USD approximations; entrance fees are quoted in cedis as published
    /// (2023-2026 sources) and converted at roughly GH₵12 per US$. Confirm before travel.
    /// </summary>
    public static class GhanaSeeder
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

        private const string Ghana = "Ghana";

        private const string English = "English";      // existing natural key (AfricaPocSeeder)
        private const string Twi = "Twi (Akan)";

        private const string Accra = "Accra";
        private const string CapeCoast = "Cape Coast";
        private const string Elmina = "Elmina";
        private const string Aburi = "Aburi";
        private const string Koforidua = "Koforidua";
        private const string Akosombo = "Akosombo";
        private const string Kumasi = "Kumasi";

        // Names checked against every other seeder (grep for Ghana/Accra/Kumasi/Elmina): none collide.
        private const string NkrumahParkName = "Kwame Nkrumah Memorial Park";
        private const string JamestownName = "Jamestown & Jamestown Lighthouse";
        private const string MakolaName = "Makola Market";
        private const string LabadiName = "Labadi Beach";
        private const string ArtistsAllianceName = "Artists Alliance Gallery";
        private const string CapeCoastCastleName = "Cape Coast Castle";
        private const string ElminaCastleName = "Elmina Castle";
        private const string KakumName = "Kakum National Park Canopy Walkway";
        private const string AburiGardensName = "Aburi Botanical Gardens";
        private const string BotiFallsName = "Boti Falls";
        private const string AkosomboName = "Akosombo Dam & Volta Lake";
        private const string ManhyiaName = "Manhyia Palace Museum";
        private const string KejetiaName = "Kejetia Market";

        private static readonly LanguageSpec[] Languages =
        {
            // Same spec as AfricaPocSeeder / KenyaNorthSeeder; only created if missing.
            new(English, "en", Array.Empty<(string, string)>()),
            new(Twi, "tw", new[]
            {
                ("Welcome", "Akwaaba"),
                ("Good morning", "Maakye"),
                ("Thank you", "Medaase"),
                ("Please", "Mesrɛ wo"),
                ("How much is it?", "Ɛyɛ sɛn?"),
                ("Yes / No", "Aane / Daabi")
            }),
        };

        // Approximate rate, mid-November 2026: interbank ~GH₵11.7 per US$, forex bureaus ~GH₵12.2-12.4
        // (ModernGhana daily forex reports, Nov 2026; exchangerate.guru 17 Nov 2026: 1 USD = 11.72 GHS).
        private static readonly CurrencySpec[] Currencies =
        {
            new("GHS", "Ghanaian Cedi", "GH₵", 11.8m),
        };

        // Visa: ECOWAS Free Movement Protocol. Citizens of ECOWAS states (incl. Nigeria) enter visa-free
        // for up to 90 days; yellow fever certificate required (sources: legit.ng, intercity.ng, entrybrief.com).
        // Tax: VAT 15% + NHIL 2.5% + GETFund levy 2.5% ≈ 20% (approximate effective consumer rate).
        private static readonly CountrySpec[] Countries =
        {
            new(Ghana, "ghana", "Africa", "🇬🇭",
                "Nigerians and other ECOWAS citizens don't need a visa for short visits. Under the ECOWAS Free Movement Protocol you can stay up to 90 days with a valid passport or an ECOWAS travel certificate; don't rely on a national ID card alone. You need a yellow fever vaccination certificate at entry, and officers may ask for a return ticket and where you're staying. Visa-free entry doesn't allow you to work. Most non-ECOWAS visitors need a visa, though Ghana has been widening visa-free access for other Africans. Requirements change, so check with the Ghana Immigration Service or a Ghanaian embassy before you travel.",
                "Type D/G", "Right", 0.20m, new[] { English, Twi }, "GHS", "Africa/Accra"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Accra, Ghana, 5.6037, -0.1870,
                "Ghana's capital, on the Gulf of Guinea, about an hour's flight from Lagos. Old Accra (Jamestown and Ussher Town) is by the sea, Osu and Labone have most of the restaurants and nightlife, and Kotoka International Airport is close to the centre. Uber, Bolt and Yango operate across the city."),
            new(CapeCoast, Ghana, 5.1053, -1.2466,
                "A fishing and university town in the Central Region, roughly 140-145 km west of Accra (about 2.5-3.5 hours by road, more in traffic). Cape Coast Castle is on the waterfront, and Kakum National Park is about 30 km inland."),
            new(Elmina, Ghana, 5.0847, -1.3509,
                "A fishing town about 12 km west of Cape Coast, built around a lagoon full of painted canoes. Elmina Castle, begun by the Portuguese in 1482, faces Fort St. Jago on the hill across the water. Beach resorts line the coast on either side of town."),
            new(Aburi, Ghana, 5.8483, -0.1750,
                "A hill town on the Akuapem ridge about 40 km north of Accra, cooler than the coast. It has the botanical gardens and, on the road up from the city, a woodcarvers' craft village."),
            new(Koforidua, Ghana, 6.0940, -0.2591,
                "The capital of the Eastern Region, about 80 km north of Accra. It is the nearest large town to Boti Falls (around 17 km away) and has a well-known bead market."),
            new(Akosombo, Ghana, 6.2833, 0.0500,
                "A planned town on the Volta River about 100 km north-east of Accra, built for the Akosombo Dam, which holds back Lake Volta. Accra residents come to the riverside hotels around Akosombo and Atimpoku for weekends away."),
            new(Kumasi, Ghana, 6.6885, -1.6244,
                "Ghana's second city and the capital of the Asante (Ashanti) kingdom, about 250 km north-west of Accra. The Asantehene's Manhyia Palace and the Kejetia / Kumasi Central Market are here. Domestic flights from Accra take under an hour; the road takes roughly 4-6 hours."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] GhLangs = { English, Twi };

        private const string Cash =
            "Money: Naira isn't accepted. Change money at licensed forex bureaus or use ATMs (Visa cards work at most bank ATMs). Small sites, markets and taxis take cash or mobile money (MoMo), so carry small cedi notes.";

        private const string Heat =
            "Heat & Sun: It is hot and humid all year. Carry water, sunscreen and a hat.";

        private static readonly DestSpec[] Destinations =
        {
            // ─── Accra ─────────────────────────────────────────────────────────
            // Sources: knmp.gov.gh/visiting-hours-rates (fees GH₵100 non-Ghanaian adult; Mon-Sat 9:00-19:00,
            // Sun 10:00-19:00; Prof. Atta Mills High Street; page modified Oct 2025); yen.com.gh & citinewsroom.com
            // (July 2023 reopening and fee increase). Other official pages list longer hours, hedged below.
            new("ghana", NkrumahParkName, Ghana, Accra, GhLangs, 5.5442, -0.2050,
                15m, "Car", 2,
                new[] { "landmark_monument", "museum_gallery", "cultural_site" },
                new[] { "history", "educational", "cultural", "photography", "tourist_hotspot" },
                new[] { "Nkrumah Mausoleum", "Kwame Nkrumah Mausoleum", "KNMP", "Nkrumah Memorial Park", "Kwame Nkrumah Park", "Old Polo Grounds" }, "Exterior",
                Desc(
                    "The Kwame Nkrumah Memorial Park in central Accra honours Ghana's first president. It is on the old Polo Grounds, where Nkrumah declared Ghana's independence on 6 March 1957. The marble mausoleum holds the graves of Nkrumah and his wife Fathia, and the museum displays his photographs, books and personal belongings. The park reopened in July 2023 after a major renovation.\n\nWhat to Do: Walk past the fountains and statues to the mausoleum, then allow time for the museum, which covers the independence era and Nkrumah's pan-African politics.",
                    "Ghanaians treat the park as a national memorial; school groups visit and families take photos at the statue. Many Nigerians learn about Nkrumah in school history lessons, and his pan-Africanism is part of how Ghana presents itself to other West Africans.",
                    "Address: Prof. Atta Mills High Street (High Street), central Accra, near Parliament House and the old city centre.\n\nGetting There: Uber, Bolt or a taxi from Osu takes about 10-20 minutes depending on traffic. Trotros and Metro Mass buses also stop nearby.",
                    "Entry: Non-Ghanaian adults pay GH₵100 (tertiary students GH₵60, children GH₵30); Ghanaian adults pay GH₵25. These rates were set at the July 2023 reopening.\n\nHours: The official visiting-hours page lists Monday-Saturday 9:00 AM-7:00 PM and Sunday 10:00 AM-7:00 PM, but other official pages show later closing times. Confirm before you go (info@knmp.gov.gh).\n\nDress: Dress respectfully; it is a mausoleum.",
                    Heat + "\n\nPhotography: Ask staff before taking photos inside the museum.",
                    "Entry (non-Ghanaian adult): GH₵100 (approx. US$8-$9).\nRide-hail from Osu: approx. GH₵40-80 (US$3-$7).",
                    new[]
                    {
                        "Jamestown & Jamestown Lighthouse: Old Accra by the sea, 5-10 minutes away.",
                        "Makola Market: Accra's largest market, a short drive north.",
                        "Independence Square (Black Star Square): The parade ground, a short drive east."
                    },
                    "Year-round; mornings are cooler. November-March is the drier season.",
                    "Medium (5/10), busier on weekends and public holidays.",
                    "Rating: 7/10. Flat paved grounds; the museum is mostly on ground level.",
                    "1 to 2 hours.")),

            // Sources: timeout.com/accra/attractions/jamestown-lighthouse (pay to climb; narrow spiral stairs);
            // freetour.com & guruwalk.com (Jamestown walking tours meeting at Jamestown Café by Ussher Fort);
            // airial.travel & guruwalk.com (Ussher Fort fee reports of GH₵40-80, conflicting). UNESCO WHC 34
            // ("Forts and Castles, Volta, Greater Accra, Central and Western Regions", 1979) includes Ussher and James forts.
            new("ghana", JamestownName, Ghana, Accra, GhLangs, 5.5329, -0.2124,
                15m, "Car", 3,
                new[] { "neighborhood_district", "historical_tour", "viewpoint_scenic_spot" },
                new[] { "history", "cultural", "photography", "walkable", "local_favorite", "art" },
                new[] { "James Town", "Jamestown Accra", "Jamestown Lighthouse", "James Town Lighthouse", "Old Accra", "Ussher Town", "Ussher Fort", "James Fort", "Chale Wote" }, "Exterior",
                Desc(
                    "Jamestown is the oldest part of Accra, a crowded Ga fishing community by the sea that grew up around two colonial forts, James Fort and Ussher Fort. Both are part of the UNESCO World Heritage listing of Ghana's forts and castles. The red-and-white Jamestown Lighthouse stands above the neighbourhood, and from the top you can see the fishing harbour, the canoes and the city. The area is also known for its boxing gyms and, every August, the Chale Wote Street Art Festival.\n\nWhat to Do: Take a guided walk through the lanes, climb the lighthouse, visit Ussher Fort, and watch the canoes come into the harbour.",
                    "People live and work here, and residents expect visitors to ask before taking photos. Guides from the neighbourhood know the families, gyms and history. During Chale Wote the main street fills with murals, performances and parties, and much of Accra's art scene turns up.",
                    "Getting There: Ride-hail or taxi to Jamestown Café / Ussher Fort on High Street, 15-25 minutes from Osu depending on traffic.\n\nOn Foot: The lighthouse, the forts and the harbour are a short walk from each other.",
                    "Lighthouse: Caretakers charge a small fee to climb. The amount isn't published, so agree it before you go up.\n\nUssher Fort: Recent visitor reports quote GH₵40-80 for foreigners. Confirm on site.\n\nTours: Guided walks start near Jamestown Café. Some are tip-based ('free' tours) and others charge a fixed fee.",
                    "Lighthouse Stairs: The spiral stairs are narrow and steep, so skip the climb if you're uneasy with heights.\n\nPhotography: Ask before photographing people, especially at the harbour.\n\nValuables: Keep phones and cash secure in crowded lanes.\n\n" + Cash,
                    "Lighthouse Climb: small fee, negotiated (approx. GH₵20-50).\nUssher Fort: approx. GH₵40-80 (US$3-$7).\nGuided Walk: tip-based or approx. US$10-$25.",
                    new[]
                    {
                        "Kwame Nkrumah Memorial Park: 5-10 minutes away.",
                        "Makola Market: A short drive north.",
                        "Artists Alliance Gallery: East along the coast road."
                    },
                    "Mornings, when the harbour is busy and walking is cooler; August for Chale Wote (dates vary).",
                    "Medium-High (6/10); very busy during Chale Wote.",
                    "Rating: 4/10. Uneven lanes, and the lighthouse is stairs only.",
                    "2 to 3 hours.")),

            // Sources: theculturetrip.com/africa/ghana/articles/the-essential-guide-to-accras-makola-market (pickpockets,
            // bargaining); citylifeaccra.com (wax prints; ~6am-7pm); timeout.com/accra/shopping/makola-market;
            // accesstravel.com (Sundays quieter/many stalls closed; 2025 redevelopment may close parts in 2026).
            new("ghana", MakolaName, Ghana, Accra, GhLangs, 5.5497, -0.2085,
                10m, "Car", 3,
                new[] { "market_street_life", "cultural_site" },
                new[] { "shopping", "cultural", "crowded", "local_favorite", "budget_friendly", "photography" },
                new[] { "Makola", "Makola Market Accra", "Makola Shopping Mall", "Kantamanto" }, "Exterior",
                Desc(
                    "Makola is Accra's biggest and busiest market, a maze of lanes in the old city centre. Traders sell wax-print fabric, beads, shea butter, black soap, provisions and household goods, and it is one of the best places in Ghana to buy fabric. Kantamanto, nearby, is the hub for second-hand clothing.\n\nWhat to Do: Shop for wax prints, beads and shea butter. If you know Balogun or Oshodi in Lagos, the crowds and the bargaining will feel familiar.",
                    "Makola is known for its market queens, the women traders who run whole sections. Prices are negotiable, and a few Twi phrases help. Nigerians used to bargaining at home will manage fine.",
                    "Getting There: Ride-hail or taxi to Makola (Kojo Thompson Road / Kinbu area), 10-20 minutes from the centre and longer in traffic. Get dropped close and walk in.",
                    "Hours: Listings give roughly 6:00 AM-7:00 PM daily, but many stalls close or are quieter on Sundays, so go Monday to Saturday.\n\nRedevelopment: Parts of the market may be closed or fenced off for redevelopment. Ask locally.\n\nBargaining: Expected. Start well below the first price and keep it friendly.",
                    "Pickpockets: Common in the crush. Carry a small bag in front and keep your phone away.\n\nHeat & Crowds: Go early in the morning, and consider a guide for a first visit.\n\n" + Cash,
                    "Entry: free.\nWax-print fabric: approx. GH₵150-500+ (US$12-$40+) for six yards depending on brand and quality; haggle.\nOptional Guide: approx. US$10-$20.",
                    new[]
                    {
                        "Kwame Nkrumah Memorial Park: A short drive south.",
                        "Jamestown & Jamestown Lighthouse: Old Accra by the sea.",
                        "Artists Alliance Gallery: Ghanaian art and crafts at fixed prices."
                    },
                    "Weekday mornings, Monday to Saturday. Avoid Sunday and the mid-afternoon heat.",
                    "Very High (9/10).",
                    "Rating: 2/10. Narrow, crowded, uneven lanes.",
                    "1.5 to 3 hours.")),

            // Sources: en.wikipedia.org/wiki/Labadi_Beach (fee for non-hotel guests); timeout.com/accra (entry ~GH₵3
            // weekdays / GH₵5 weekends at main gate, dated); theculturetrip.com (approx. GH₵10); wanderlog user (GH₵20).
            new("ghana", LabadiName, Ghana, Accra, GhLangs, 5.5600, -0.1490,
                15m, "Car", 3,
                new[] { "beach_coast", "entertainment_nightlife" },
                new[] { "beach", "social", "music", "nightlife", "tourist_hotspot", "group_friendly" },
                new[] { "La Beach", "La Pleasure Beach", "Labadi Pleasure Beach", "Labadi", "La Beach Accra" }, "Landscape",
                Desc(
                    "Labadi Beach, officially La Pleasure Beach, is Accra's best-known beach, a long stretch of sand in the La area east of Osu. At weekends it fills with music, drumming and dancing, with beach bars, horse rides, acrobats and football games along the sand.\n\nWhat to Do: Take a table at a beach bar, order grilled tilapia or kebabs and a cold drink, and stay for the music at sunset.",
                    "Weekends at La beach are a big social outing for young people in Accra, much like Elegushi or Landmark beach in Lagos. The beach is in the traditional area of the La people, whose fishing community lives nearby.",
                    "Getting There: Ride-hail or taxi to La Pleasure Beach / Labadi Beach Hotel, about 10-15 minutes from Osu.\n\nEntrances: The main paid entrance is near the Labadi Beach Hotel. Several beach bars have their own access.",
                    "Entry: Non-hotel guests pay a small gate fee. Older guides list GH₵3-5 and recent visitors report around GH₵10-20, so confirm at the gate.\n\nDecember: During 'Detty December', beach parties and festivals run most weekends and prices go up.\n\nNews: Some structures behind the beach were reported demolished in late 2026. Check locally what's open.",
                    "Swimming: Strong currents and rip tides make swimming risky. Stay in the shallows or stay out.\n\nHustlers: Horse-ride owners and vendors can be pushy. Agree prices first and say no firmly.\n\nValuables: Leave them at the hotel.\n\n" + Cash,
                    "Gate Fee: approx. GH₵10-20 (about US$1-$2).\nDrinks & Grilled Fish: approx. GH₵150-300 (US$12-$25) per person.\nHorse Ride: negotiated.",
                    new[]
                    {
                        "Artists Alliance Gallery: On the beach road a few minutes away.",
                        "Osu (Oxford Street): Restaurants and nightlife, 10-15 minutes west.",
                        "Kwame Nkrumah Memorial Park: Central Accra."
                    },
                    "Weekend afternoons into the evening; November-March is drier.",
                    "High (8/10) at weekends; quiet on weekdays.",
                    "Rating: 4/10. Soft sand; some beach bars are at street level.",
                    "2 to 4 hours.")),

            // Sources: timeout.com/accra/art/galleries/artists-alliance-gallery (Omanye House, Accra-Tema Beach Road,
            // admission free); en.wikipedia.org/wiki/Artists_Alliance_Gallery_(Omanye_House) (founded by Ablade Glover);
            // citylifeaccra.com (phone 024 525 1404); rehlat.ma (hours ~9:00-17:00 Mon-Sat, 12:00-17:00 Sun; single source).
            new("ghana", ArtistsAllianceName, Ghana, Accra, GhLangs, 5.5653, -0.1418,
                10m, "Car", 1,
                new[] { "museum_gallery", "cultural_site" },
                new[] { "art", "cultural", "shopping", "relaxing", "educational", "hidden_gem" },
                new[] { "Artists Alliance", "Omanye House", "Artist Alliance Gallery", "Ablade Glover Gallery", "Artists Alliance Gallery Accra" }, "Interior",
                Desc(
                    "The Artists Alliance Gallery fills several floors of Omanye House on the Accra-Tema beach road in Labadi. The painter Ablade Glover founded it. It shows contemporary Ghanaian painting and sculpture alongside antique furniture, carvings, textiles and crafts, and almost everything is for sale.\n\nWhat to Do: Work up through the floors (the top floor has the larger paintings) and buy crafts at fixed prices, which is a relief after bargaining at Makola.",
                    "Ablade Glover is one of Ghana's best-known painters, and the gallery has been part of Accra's art scene for decades. Work by established and newer Ghanaian artists hangs side by side.",
                    "Address: Omanye House, Accra-Tema Beach Road, Labadi, Accra, close to Labadi Beach.\n\nGetting There: Ride-hail or taxi, about 10-15 minutes from Osu.",
                    "Entry: Free.\n\nHours: One listing gives about 9:00 AM-5:00 PM Monday-Saturday and from noon on Sundays. Confirm before visiting (artistsalliancegallery.com, 024 525 1404).\n\nShipping: Ask the gallery about packing and export paperwork for larger pieces.",
                    "Budget: Original artworks can cost thousands of dollars; browsing is free.\n\nFlights Home: Check your airline's baggage rules before buying large carvings.",
                    "Entry: free.\nSmall crafts: approx. US$10-$60.\nOriginal paintings: hundreds to thousands of US$.",
                    new[]
                    {
                        "Labadi Beach: A few minutes along the coast.",
                        "Osu (Oxford Street): Dinner and nightlife.",
                        "Makola Market: For haggled fabric and beads."
                    },
                    "Any time; a good place to spend the hottest part of the day.",
                    "Low (2/10).",
                    "Rating: 4/10. Several floors; ask staff about access.",
                    "1 to 1.5 hours.")),

            // ─── Cape Coast & Elmina ───────────────────────────────────────────
            // Sources: gmmb.gov.gh/cape-coast-castle-cape-coast-1653 (non-Ghanaian adult GH₵80, students GH₵30-60,
            // Ghanaian adult GH₵20; fees reviewed 1 July 2023; open 9:00-16:30 daily; Swedish fort 1653, British 1665;
            // slave trade until 1807). UNESCO WHC 34 (1979). The Door of No Return and the 2009 Obama visit are widely reported.
            new("ghana", CapeCoastCastleName, Ghana, CapeCoast, GhLangs, 5.1033, -1.2413,
                10m, "Car", 2,
                new[] { "landmark_monument", "historical_tour", "museum_gallery" },
                new[] { "history", "educational", "cultural", "tourist_hotspot", "architecture", "photography" },
                new[] { "Cape Coast Castle Museum", "Carolusborg", "Carlsborg", "Cape Coast Slave Castle", "Door of No Return Cape Coast" }, "Exterior",
                Desc(
                    "Cape Coast Castle was a major site of the transatlantic slave trade. The Swedes built the first permanent fort here in 1653, and the British held it from 1665 and made it the seat of their government on the Gold Coast. Enslaved Africans were held in the underground dungeons before being forced through the 'Door of No Return' onto ships bound for the Americas, until Britain banned the trade in 1807. The castle is part of the UNESCO World Heritage listing of Ghana's forts and castles and now houses a museum.\n\nWhat to Do: Take the guided tour through the male and female dungeons, the condemned cell, the governor's quarters and the Door of No Return, then visit the museum.",
                    "Many visitors, especially Africans and people from the diaspora, come here as a pilgrimage, and the guides tell the history carefully. Nigerians may think of Badagry, where the same trade passed through. The 2019 Year of Return brought record numbers of diaspora visitors.",
                    "Getting There: Cape Coast is about 140-145 km west of Accra, around 2.5-3.5 hours by private car, or by Intercity STC or VIP buses from Accra. The castle is on the seafront in the town centre; walk or take a taxi.",
                    "Entry: Non-Ghanaian adults GH₵80, non-Ghanaian tertiary students/volunteers with ID GH₵60, JHS/SHS students GH₵30; Ghanaian adults GH₵20. These are the Ghana Museums and Monuments Board rates (reviewed July 2023); confirm on arrival.\n\nHours: 9:00 AM-4:30 PM daily. Tours run regularly and last about an hour.\n\nRespect: The dungeons are treated as a memorial, so keep your voice low.",
                    "Emotional Weight: Many people find the tour upsetting; leave time afterwards.\n\nTouts: Young men outside may ask for your name and address to 'write you a letter' and later ask for money. Politely decline.\n\n" + Heat,
                    "Entry (non-Ghanaian adult): GH₵80 (approx. US$7).\nTip for guide: optional, approx. GH₵20-50.\nPhotography: ask whether a camera fee applies.",
                    new[]
                    {
                        "Elmina Castle: 20 minutes west along the coast.",
                        "Kakum National Park Canopy Walkway: About 30-45 minutes inland.",
                        "Fort William Lighthouse: On the hill above Cape Coast."
                    },
                    "Year-round; mornings are cooler and quieter. November-March is the drier season.",
                    "Medium-High (6/10); busiest in December-January and around Emancipation Day (1 August).",
                    "Rating: 4/10. Uneven floors, steps and dark dungeons.",
                    "1.5 to 2 hours.")),

            // Sources: gmmb.gov.gh St. George's Castle (Elmina Castle) Museum page (9:00-16:30 daily; listed fee GH₵40
            // for adult foreigners but marked "reviewed January 2015", likely superseded by the July 2023 GMMB revision);
            // UNESCO WHC 34. Portuguese founding (1482, São Jorge da Mina) and Dutch capture (1637) are standard history.
            new("ghana", ElminaCastleName, Ghana, Elmina, GhLangs, 5.0827, -1.3483,
                10m, "Car", 2,
                new[] { "landmark_monument", "historical_tour", "museum_gallery" },
                new[] { "history", "educational", "cultural", "architecture", "photography", "tourist_hotspot" },
                new[] { "St. George's Castle", "St George's Castle", "São Jorge da Mina", "Sao Jorge da Mina", "Elmina Slave Castle", "Elmina Castle Museum" }, "Exterior",
                Desc(
                    "Elmina Castle, or St. George's Castle, was begun by the Portuguese in 1482 as São Jorge da Mina, which makes it one of the oldest European buildings in sub-Saharan Africa. The Dutch captured it in 1637, and it became a major slave-trading post. The tour covers the dungeons, the female courtyard with the governor's balcony above it, the chapel and the Door of No Return. Like Cape Coast Castle, it is on the UNESCO World Heritage list.\n\nWhat to Do: Join the guided tour, then walk along the lagoon to see Elmina's fishing fleet and Fort St. Jago on the hill opposite.",
                    "Elmina is still a working fishing town. The harbour beside the castle is packed with painted canoes, and the fish market is one of the busiest on the coast.",
                    "Getting There: About 12 km (20 minutes) west of Cape Coast by taxi or tro-tro, or about 3-3.5 hours from Accra by road.\n\nOn Foot: The castle is on the spit between the lagoon and the sea, a short walk from the town centre.",
                    "Entry: The Ghana Museums and Monuments Board runs the castle. Its Elmina page still shows a 2015 fee of GH₵40 for adult foreigners; after the 2023 revision the rate is probably in line with Cape Coast Castle's GH₵80. Confirm at the gate.\n\nHours: 9:00 AM-4:30 PM daily.\n\nTour: Guided, around an hour.",
                    "Touts: As at Cape Coast, decline requests to 'write letters' or for donations outside.\n\nHarbour Photos: Fishermen may object to cameras, so ask first.\n\n" + Heat,
                    "Entry (non-Ghanaian adult): approx. GH₵40-80 (US$4-$7); confirm.\nTip for guide: optional.\nTaxi from Cape Coast: approx. GH₵50-100 (US$4-$8).",
                    new[]
                    {
                        "Cape Coast Castle: 20 minutes east.",
                        "Kakum National Park Canopy Walkway: About 40 minutes inland.",
                        "Fort St. Jago: On the hill across the lagoon."
                    },
                    "Year-round; the late-afternoon light over the harbour is good for photos.",
                    "Medium (5/10).",
                    "Rating: 4/10. Stairs and uneven stone floors.",
                    "1.5 to 2 hours.")),

            // Sources: en.wikipedia.org/wiki/Kakum_National_Park (375 km²; seven bridges ~40 m up; 330-350 m long;
            // ~33 km north of Cape Coast near Abrafo; visitor centre opened 1997); fees: kakumnationalpark.info/pricing
            // (Jan 2026: non-Ghanaian adult canopy GH₵130), explorewithkojo.com (GH₵100), dreamtripguide.com 2026
            // (~GH₵100 + ~GH₵35 gate), conflicting; hours ~7:00/8:00-16:00 reported by blogs, unverified.
            new("ghana", KakumName, Ghana, CapeCoast, GhLangs, 5.3536, -1.3833,
                15m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience", "viewpoint_scenic_spot" },
                new[] { "nature", "adventurous", "photography", "family_friendly", "tourist_hotspot" },
                new[] { "Kakum", "Kakum National Park", "Kakum Canopy Walk", "Kakum Canopy Walkway", "Kakum Forest", "Abrafo" }, "Landscape",
                Desc(
                    "Kakum National Park covers about 375 km² of tropical rainforest north of Cape Coast. Its canopy walkway is a chain of seven rope-and-plank bridges strung between tall trees, around 40 m above the forest floor and roughly 330-350 m long. The park has forest elephants, bongo, duikers, monkeys and more than 260 bird species, though you are unlikely to see much large wildlife.\n\nWhat to Do: Walk the canopy early in the morning, when it is cooler and the birds are active, and add a guided forest walk if you have time.",
                    "The visitor centre near Abrafo village opened in 1997 and is run with Ghana's Wildlife Division. School groups and Ghanaian families visit in large numbers, and many Ghanaians did the walkway on a school trip.",
                    "Getting There: The visitor centre is near Abrafo, about 33 km north of Cape Coast, roughly 30-45 minutes by car or taxi. From Accra, allow about 3-3.5 hours.\n\nOn Site: A short uphill walk through the forest leads to the first platform.",
                    "Entry: You pay a park gate fee plus a separate canopy walkway ticket. Published non-Ghanaian adult canopy prices range from about GH₵100 to GH₵130, and 2025-2026 sources disagree, so confirm at the visitor centre.\n\nHours: Blogs report the walkway opening around 7:00-8:00 AM and closing mid-afternoon (around 4:00 PM). Confirm before setting off.\n\nGroups: Walks go in groups with a park guide.",
                    "Heights: The bridges sway, so this is hard for anyone with a serious fear of heights.\n\nRain: Paths and planks get slippery; wear shoes with grip.\n\nCrowds: School groups can make the queue long by late morning. Arrive at opening.",
                    "Canopy Walkway (non-Ghanaian adult): approx. GH₵100-130 (US$8-$11).\nGate Fee: approx. GH₵35 (US$3); confirm.\nTaxi from Cape Coast (return with waiting): approx. GH₵300-500 (US$25-$40).",
                    new[]
                    {
                        "Cape Coast Castle: 30-45 minutes south.",
                        "Elmina Castle: About 40 minutes south-west.",
                        "Hans Cottage Botel: Crocodile pond and restaurant on the road to Kakum."
                    },
                    "Early morning, year-round. The drier months (roughly November-March) are best for walking.",
                    "Medium-High (6/10); busy with school groups on weekday mornings.",
                    "Rating: 2/10. Uphill forest trail and high bridges that sway.",
                    "2 to 3 hours.")),

            // ─── Eastern Region ────────────────────────────────────────────────
            // Sources: airial.travel (non-Ghanaian GH₵20 vs Ghanaian GH₵10, aggregated); chris-toone.com 2026 guide
            // (Ghanaian adult GH₵20, cash only); citinewsroom.com May 2025 (gardens in need of maintenance);
            // modernghana.com 2026 (revenue). Opened 1890 per Ghana Tourism Authority / visitghana.com.
            new("ghana", AburiGardensName, Ghana, Aburi, GhLangs, 5.8484, -0.1757,
                10m, "Car", 1,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "nature", "relaxing", "family_friendly", "photography", "history", "budget_friendly" },
                new[] { "Aburi Gardens", "Aburi Botanic Gardens", "Aburi Botanical Garden", "Aburi Botanic Park" }, "Landscape",
                Desc(
                    "Aburi Botanical Gardens are on the Akuapem ridge about 40 km north of Accra, where the air is cooler than on the coast. The gardens opened in 1890 and have large old trees, palm avenues, spice plants and lawns, with views back down to the plains.\n\nWhat to Do: Walk the avenues and the forest trail, picnic on the lawns, and stop at the Aburi woodcarvers' village on the way up.",
                    "Families from Accra come up at weekends for picnics, wedding photos and the cooler air. Local reports in 2025 said parts of the gardens need maintenance, so expect some untidy paths and buildings.",
                    "Getting There: About 1-1.5 hours from central Accra by car (longer in traffic), up the Aburi road via Madina. Tro-tros also run from Accra to Aburi.\n\nOn to Boti: Boti Falls is roughly 1.5-2 hours further north via Koforidua.",
                    "Entry: Small fee, cash only. Recent reports give about GH₵20 for non-Ghanaian adults (GH₵10-20 for Ghanaian adults); confirm at the gate.\n\nCraft Village: Carvings, drums and stools are sold along the road. Bargain, and be sceptical of 'antique' claims.",
                    "Weekends: Busy with picnics and events.\n\nWeather: It can be misty and cooler than Accra, so bring a light layer in the rainy season.",
                    "Entry: approx. GH₵20 (US$2).\nCar & Driver from Accra (full day incl. Boti): approx. US$60-$100.",
                    new[]
                    {
                        "Boti Falls: The next stop on a day trip.",
                        "Akosombo Dam & Volta Lake: An alternative day trip to the north-east.",
                        "Aburi Craft Village: Woodcarvers on the road up."
                    },
                    "Year-round; mornings are clearest, and the gardens are greenest during and after the rains.",
                    "Medium (5/10) at weekends; quiet on weekdays.",
                    "Rating: 6/10. Mostly flat paths and lawns.",
                    "1.5 to 2 hours.")),

            // Sources: en.wikipedia.org/wiki/Boti_Falls (~17 km NE of Koforidua; twin 'male and female' falls);
            // wanderlog.com (GH₵150 non-Ghanaian / GH₵50 Ghanaian, single source); myjoyonline.com (poor access road);
            // tour listings (Umbrella Rock, three-headed palm); ~250 steps reported by visitors.
            new("ghana", BotiFallsName, Ghana, Koforidua, GhLangs, 6.1925, -0.2195,
                20m, "Car", 2,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "nature", "adventurous", "photography", "hidden_gem", "seasonal" },
                new[] { "Boti Waterfall", "Boti Waterfalls", "Boti Twin Falls", "Umbrella Rock", "Boti Umbrella Rock", "Three-Headed Palm" }, "Landscape",
                Desc(
                    "Boti Falls is a twin waterfall in the Yilo Krobo area, about 17 km north-east of Koforidua in the Eastern Region. Locals call the two streams 'male' and 'female', and they drop into a pool in the forest. Trails nearby lead to Umbrella Rock, a large overhanging boulder you can climb for views, and to a palm tree with three heads.\n\nWhat to Do: Walk down to the falls, then hike with a guide to Umbrella Rock and the three-headed palm.",
                    "A local legend says that when the two falls meet in the rainy season they make a rainbow, and couples come to make wishes. Groups from Accra and Koforidua often visit at weekends.",
                    "Getting There: From Accra, about 2-2.5 hours by car via Aburi or Koforidua. From Koforidua it is about 17 km, but local reports say the road is poor and can take over an hour. A private driver is the easiest option.\n\nOn Site: About 250 steps lead down to the falls, and you climb them again on the way out.",
                    "Entry: One 2026 listing gives GH₵150 for non-Ghanaians and GH₵50 for Ghanaians; older sources are lower. Confirm at the gate.\n\nGuides: You need a guide for Umbrella Rock (one visitor paid about GH₵100).\n\nSeason: The falls are strongest during and just after the rains. In the dry season (roughly December-March) they can shrink to a trickle.",
                    "Slippery Steps: The steps and rocks are wet and slick; wear shoes with grip.\n\nSwimming: Ask the guides before entering the pool, especially after heavy rain.",
                    "Entry (non-Ghanaian): approx. GH₵150 (US$13); confirm.\nGuide to Umbrella Rock: approx. GH₵100 (US$8).",
                    new[]
                    {
                        "Aburi Botanical Gardens: On the way from Accra.",
                        "Koforidua: Bead market and regional capital.",
                        "Akosombo Dam & Volta Lake: Further east."
                    },
                    "During or just after the rainy seasons (roughly May-October).",
                    "Medium (5/10) at weekends.",
                    "Rating: 2/10. About 250 steep steps and rocky trails.",
                    "2 to 3 hours on site.")),

            // Sources: ghana-net.com/akosombo-dam (entrance fee applies); wanderlog visitor review (fee paid at the VRA
            // office in Akosombo township, guide assigned; undated GH₵5/GH₵10); dam completed 1965 creating Lake Volta
            // (Volta River Authority / standard references). Cruise schedules not verified.
            new("ghana", AkosomboName, Ghana, Akosombo, GhLangs, 6.2997, 0.0597,
                60m, "Car", 2,
                new[] { "landmark_monument", "nature_outdoor", "activity_experience" },
                new[] { "nature", "relaxing", "educational", "photography", "couple_friendly" },
                new[] { "Akosombo", "Akosombo Dam", "Volta Lake", "Lake Volta", "Volta River", "Adomi Bridge", "Atimpoku" }, "Landscape",
                Desc(
                    "The Akosombo Dam, completed in 1965, holds back the Volta River to form Lake Volta, one of the largest man-made lakes in the world by surface area. The dam produces much of Ghana's electricity and was one of Kwame Nkrumah's major projects. The river towns of Akosombo and Atimpoku nearby have riverside hotels, boat trips and the arched Adomi Bridge.\n\nWhat to Do: Take a guided visit to the dam, then spend the afternoon on the river with a boat trip, lunch by the water or a night at a riverside resort.",
                    "Akosombo is a common weekend trip from Accra, and on Saturdays the riverside resorts fill with families and groups of friends. Most visitors eat grilled tilapia from the Volta.",
                    "Getting There: About 100 km (2-2.5 hours) north-east of Accra by car, via Tema and the Akosombo road.\n\nDam Visits: Arranged through the Volta River Authority visitor office in Akosombo township, which assigns a guide. Bring ID.",
                    "Dam Visit: The VRA visitor office charges a fee; the amounts in online reviews are old, so confirm locally. You may have to wait for a guide.\n\nBoat Cruises: Lake and river cruises run from Akosombo, mainly at weekends. Check current operators and schedules.\n\nOptional Extension: Combined with Aburi, this makes a long day from Accra, so an overnight works better.",
                    "Security Rules: Photography may be restricted at the dam. Follow your guide.\n\nWater: Bilharzia can be present in Lake Volta, so ask locals before swimming.",
                    "Dam Visit: small fee; confirm.\nBoat Cruise: approx. US$20-$60 per person depending on operator.\nRiverside Hotel: approx. US$60-$200 per room per night.",
                    new[]
                    {
                        "Aburi Botanical Gardens: In the hills on the way back to Accra.",
                        "Boti Falls: West towards Koforidua.",
                        "Accra: About 2-2.5 hours south-west."
                    },
                    "Year-round; weekends for boat trips, weekdays if you want it quiet.",
                    "Low-Medium (4/10); busier on weekends.",
                    "Rating: 5/10. The dam visit involves walking, and boats need steps.",
                    "A full day or an overnight.")),

            // ─── Kumasi (optional extension) ───────────────────────────────────
            // Sources: manhyiapalacemuseum.org (foreign adults GH₵100, students GH₵80; hours listed as 8:00 or 9:00-17:00
            // daily on different pages; tel +233 256071700); en.wikipedia.org/wiki/Manhyia_Palace_Museum (palace built by
            // the British in 1925; museum opened 1995); Akwasidae is held every six weeks (42-day Akan calendar).
            new("ghana", ManhyiaName, Ghana, Kumasi, GhLangs, 6.7056, -1.6130,
                15m, "Flight", 2,
                new[] { "museum_gallery", "cultural_site", "historical_tour" },
                new[] { "history", "cultural", "educational", "architecture", "hidden_gem" },
                new[] { "Manhyia Palace", "Manhyia Museum", "Manhyia", "Asantehene's Palace", "Manhyia Palace Kumasi" }, "Exterior",
                Desc(
                    "The Manhyia Palace Museum in Kumasi is the former residence of the Asantehene, the king of the Asante (Ashanti). The British built it in 1925 for Asantehene Prempeh I when he returned from exile, and it became a museum in 1995. Guided tours show royal furniture, regalia, photographs and life-size wax figures of past kings and queen mothers, and explain the Golden Stool and the history of the Asante kingdom. The current Asantehene's palace is next door.\n\nWhat to Do: Take the guided tour. If your dates line up, attend the Akwasidae festival, when the Asantehene receives chiefs and the public in full regalia.",
                    "The Asantehene is still a highly respected traditional ruler, with a standing similar to the Oba of Benin or the Ooni of Ife in Nigeria. Akwasidae falls every six weeks on the Akan calendar.",
                    "Getting There: Fly Accra-Kumasi (under an hour) or drive about 250 km (roughly 4-6 hours). The palace is in the Manhyia district, a short taxi ride from central Kumasi.",
                    "Entry: The museum's website lists GH₵100 for foreign adults and GH₵80 for foreign students (Ghanaian adults GH₵20). Confirm before you go (+233 256071700).\n\nHours: Daily, roughly 9:00 AM-5:00 PM; the museum's own pages disagree on the opening time.\n\nPhotography: Restricted inside. Ask your guide.",
                    "Akwasidae: Dates follow the Akan calendar, so confirm the next one locally. Crowds are large.\n\nEtiquette: Don't point or gesture with your left hand, and don't cross in front of chiefs. Follow the guide's lead.",
                    "Entry (foreign adult): GH₵100 (approx. US$8-$9).\nAccra-Kumasi flight: approx. US$60-$120 one way.",
                    new[]
                    {
                        "Kejetia Market: Kumasi's main market, 10-15 minutes away.",
                        "Kumasi Fort & Military Museum: In the city centre.",
                        "Bonwire: Kente weaving village about 30 minutes from Kumasi."
                    },
                    "Year-round; plan around an Akwasidae date if you can.",
                    "Low-Medium (3/10); very busy on Akwasidae.",
                    "Rating: 5/10. Mostly ground-level rooms, with some steps.",
                    "1 to 1.5 hours.")),

            // Sources: myjoyonline.com, gna.org.gh, ghanaweb.com (2026: Phase I of the Kumasi Central Market / Kejetia
            // redevelopment complete with ~8,420 shops; Phase II stalled at ~58%, work expected to resume Oct 2026);
            // graphic.com.gh (KMA stall premiums).
            new("ghana", KejetiaName, Ghana, Kumasi, GhLangs, 6.6977, -1.6247,
                10m, "Flight", 3,
                new[] { "market_street_life", "cultural_site" },
                new[] { "shopping", "crowded", "cultural", "local_favorite", "budget_friendly" },
                new[] { "Kejetia", "Kumasi Central Market", "Kejetia Market Kumasi", "Kumasi Market", "Kejetia Mall" }, "Exterior",
                Desc(
                    "Kejetia and the Kumasi Central Market together make up one of the largest markets in West Africa, covering much of central Kumasi. The first phase of the redevelopment, a multi-storey market with around 8,400 shops, is open. The second phase stalled for years and was due to restart in late 2026, so parts of the older market are still a crowded maze.\n\nWhat to Do: Go with a guide to find kente and adinkra cloth, beads, leather, spices and everyday goods.",
                    "Kumasi traders have a reputation across Ghana for business sense, and much of the city's trade runs through the market. Nigerians who know Onitsha Main Market or Kano's Kurmi Market will recognise the scale.",
                    "Getting There: In central Kumasi beside the Kejetia transport terminal. Take a taxi or ride-hail from anywhere in the city.",
                    "Hours: Roughly early morning to early evening, Monday to Saturday; quieter on Sunday.\n\nGuide: A local guide makes it much easier to find your way and judge prices.\n\nBargaining: Expected.\n\n" + Cash,
                    "Pickpockets: Keep valuables secure in the crowds.\n\nConstruction: Parts of the market area are building sites, so watch your step.\n\n" + Heat,
                    "Entry: free.\nGuide: approx. US$10-$20.\nKente strip or cloth: prices vary widely; haggle.",
                    new[]
                    {
                        "Manhyia Palace Museum: 10-15 minutes away.",
                        "Kumasi Fort & Military Museum: Nearby in the centre.",
                        "Bonwire Kente Village: Weavers about 30 minutes out of town."
                    },
                    "Weekday mornings.",
                    "Very High (9/10).",
                    "Rating: 2/10. Crowded, uneven ground; the new market building has stairs and ramps.",
                    "2 to 3 hours.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlist
        // ═══════════════════════════════════════════════════════════════════════

        // Getting there. Sources: flightconnections.com / flightsfrom.com / trip.com (Lagos-Accra nonstop ~1h-1h20,
        // operated by Africa World Airlines, Air Peace and others; schedules change); pulse.ng, intercity.ng,
        // rome2rio.com (Lagos-Accra by road via Seme [Benin], Togo and Aflao: roughly 10-15+ hours, ABC Transport
        // and Chisco among bus operators). 1map.com (Intercity STC Accra-Cape Coast, ~2h+).
        // ItineraryItem.TimeOfDay is a controlled value (Morning/Afternoon/Evening, validated by the
        // admin and user itinerary endpoints), so any specific clock times go in the item titles.
        private static readonly WishlistSpec[] Wishlists =
        {
            new("ghana", "Ghana: Castles, Canopies and Accra Culture", Ghana,
                "A 7-day trip to Ghana for Nigerian travellers. Accra is about an hour's flight from Lagos, ECOWAS citizens don't need a visa, and English is spoken everywhere. The trip starts in Accra with the Kwame Nkrumah Memorial Park, a guided walk through Jamestown and up its lighthouse, Makola Market, the Artists Alliance Gallery and an evening at Labadi Beach. It then follows the coast west to Cape Coast and Elmina, whose castles were centres of the transatlantic slave trade, and inland to the Kakum canopy walkway above the rainforest. The last full day is in the Akuapem hills at Aburi Botanical Gardens and Boti Falls. Optional extensions add a night at Akosombo on the Volta, or a flight north to Kumasi for the Manhyia Palace Museum and Kejetia Market. Bring your passport and yellow fever card, change money to cedis on arrival, and use Uber or Bolt in Accra. All prices are rough USD estimates, and flights from Nigeria are extra.",
                "An hour from Lagos: Accra's beaches and markets, the slave castles at Cape Coast and Elmina, and a walk above the Kakum rainforest.",
                "Friends & Couples",
                new[] { "6 Nights in Hotels (4 Accra, 2 Cape Coast/Elmina)", "Private Car & Driver Accra-Cape Coast-Accra", "Aburi & Boti Day Trip with Driver", "Cape Coast & Elmina Castle Tours", "Kakum Canopy Walkway", "Jamestown Guided Walk" },
                "Ghana, Accra, Kotoka, Kwame Nkrumah Memorial Park, Nkrumah Mausoleum, Jamestown, James Town Lighthouse, Ussher Fort, Chale Wote, Makola Market, Labadi Beach, La Pleasure Beach, Osu, Oxford Street, Artists Alliance Gallery, Cape Coast, Cape Coast Castle, Door of No Return, Elmina, Elmina Castle, St. George's Castle, Kakum, canopy walkway, Aburi Botanical Gardens, Boti Falls, Umbrella Rock, Akosombo, Volta Lake, Kumasi, Manhyia Palace, Kejetia, Year of Return, Detty December, jollof, kente, wax print, ECOWAS, visa-free, Lagos to Accra, Nigeria to Ghana, West Africa",
                new[] { "Culture", "History", "Beach", "Nature" },
                1100m, 250m,
                "6 nights (shared double): 4 in a mid-range Accra hotel (Osu/Labone/Airport area), 2 in a Cape Coast or Elmina beach hotel",
                "Private car with driver for the coast loop and the Aburi/Boti day; Uber/Bolt in Accra; return flights from Lagos or Abuja to Accra not included (or go by road via Benin and Togo, 10-15+ hours)",
                "Kwame Nkrumah Memorial Park, Jamestown guided walk and lighthouse, Makola Market, Labadi Beach evening, Cape Coast Castle tour, Kakum canopy walkway, Elmina Castle tour, Aburi Botanical Gardens, Boti Falls",
                false, "Culture Seeker",
                new[]
                {
                    new DaySpec(1, "Arrive in Accra", Accra, Accra, Accra, new[]
                    {
                        new ItemSpec("Morning", "Fly Lagos or Abuja to Accra", "Nonstop Lagos-Accra flights take about an hour (Africa World Airlines, Air Peace and others; check current schedules). Abuja has fewer direct options. ECOWAS citizens don't need a visa, but bring your passport and yellow fever card. Change money or withdraw cedis at Kotoka.", 200m, "Arrival"),
                        new ItemSpec("Morning", "Or: Lagos to Accra by Road", "Buses (ABC Transport, Chisco and others) cross into Benin at Seme, go through Togo and enter Ghana at Aflao. That is three borders and roughly 10-15+ hours, so leave a day earlier and carry your passport and yellow card.", 50m, "Road Trip", null, true),
                        new ItemSpec("Afternoon", "Kwame Nkrumah Memorial Park", "The site where Ghana's independence was declared in 1957, with the mausoleum, statues and museum of Ghana's first president. Non-Ghanaian adults pay GH₵100.", 9m, "Top Sight", NkrumahParkName),
                        new ItemSpec("Evening", "Dinner in Osu", "Jollof, kelewele and grilled tilapia around Oxford Street.", 20m, "Local Food"),
                    }),
                    new DaySpec(2, "Old Accra, Makola and the Gallery", Accra, Accra, Accra, new[]
                    {
                        new ItemSpec("Morning", "9:00 AM · Jamestown Walking Tour & Lighthouse", "Walk Old Accra with a local guide past the fishing harbour, Ussher Fort and the boxing gyms, then climb the striped lighthouse for views over the city.", 20m, "Local Favorite", JamestownName),
                        new ItemSpec("Afternoon", "Makola Market", "Bargain for wax prints, beads and shea butter in Accra's largest market. Go Monday to Saturday and keep your phone close.", 0m, "Shopping", MakolaName),
                        new ItemSpec("Afternoon", "Artists Alliance Gallery", "Ghanaian paintings, carvings and crafts on several floors of Omanye House. Entry is free.", 0m, "Hidden Gem", ArtistsAllianceName),
                        new ItemSpec("Evening", "Accra Nightlife", "Bars and live music in Osu or East Legon; ask locally what's on that week.", 25m, "Nightlife"),
                    }),
                    new DaySpec(3, "Coast Road to Cape Coast", Accra, CapeCoast, CapeCoast, new[]
                    {
                        new ItemSpec("Morning", "7:00 AM · Drive West to Cape Coast", "About 2.5-3.5 hours along the coast road with your driver, or by Intercity STC or VIP bus from Accra. Leave early to avoid the Kasoa traffic.", 60m, "Road Trip"),
                        new ItemSpec("Afternoon", "Cape Coast Castle Guided Tour", "The dungeons, the condemned cell and the Door of No Return. Many visitors find it upsetting. Non-Ghanaian adults pay GH₵80; open 9:00 AM-4:30 PM.", 7m, "Top Sight", CapeCoastCastleName),
                        new ItemSpec("Evening", "Seafront Dinner", "Fresh fish and a quiet evening by the sea.", 20m, "Quiet Evening"),
                    }),
                    new DaySpec(4, "Kakum Canopy and Elmina", CapeCoast, Elmina, Elmina, new[]
                    {
                        new ItemSpec("Morning", "8:00 AM · Kakum Canopy Walkway", "Seven bridges about 40 m above the rainforest. Arrive at opening, when it is cooler, the birds are active and queues are short. Canopy plus gate fee is roughly GH₵135-165; confirm.", 14m, "Bucket List", KakumName),
                        new ItemSpec("Afternoon", "Elmina Castle Guided Tour", "Begun by the Portuguese in 1482, one of the oldest European buildings in sub-Saharan Africa. Afterwards, walk along the lagoon past the painted fishing canoes.", 7m, "Top Sight", ElminaCastleName),
                        new ItemSpec("Evening", "Sunset at an Elmina Beach Resort", "Drinks and dinner on the Elmina coast.", 25m, "Sunset"),
                    }),
                    new DaySpec(5, "Back to Accra and Labadi Beach", Elmina, Accra, Accra, new[]
                    {
                        new ItemSpec("Morning", "Drive Back to Accra", "About 3-3.5 hours east along the coast road. Leave mid-morning to miss the rush hour.", 60m, "Road Trip"),
                        new ItemSpec("Afternoon", "Downtime or Shopping", "Rest at the hotel, or go back for fabric and souvenirs you missed at Makola.", 0m, "Free Time"),
                        new ItemSpec("Evening", "Labadi Beach Evening", "Music, drumming and grilled fish at La Pleasure Beach, busiest at weekends. The currents are strong, so don't swim.", 15m, "Local Favorite", LabadiName),
                    }),
                    new DaySpec(6, "Aburi and Boti Falls", Aburi, Koforidua, Accra, new[]
                    {
                        new ItemSpec("Morning", "Aburi Botanical Gardens", "Drive 1-1.5 hours into the hills for old trees, palm avenues and views, stopping at the Aburi woodcarvers on the way.", 2m, "Nature", AburiGardensName),
                        new ItemSpec("Afternoon", "Boti Falls & Umbrella Rock", "About 250 steps down to the twin 'male and female' falls, then a guided hike to Umbrella Rock. The falls are fullest during and after the rainy season.", 21m, "Hidden Gem", BotiFallsName),
                        new ItemSpec("Afternoon", "Or: Akosombo Dam & the Volta", "Swap Boti for a guided dam visit and lunch by the Volta, or stay the night at a riverside resort.", 40m, "Optional", AkosomboName, true),
                        new ItemSpec("Evening", "Back in Accra", "Farewell dinner in Osu or Labone.", 30m, "Last Night"),
                    }),
                    new DaySpec(7, "Fly Home or Continue to Kumasi", Accra, Accra, Accra, new[]
                    {
                        new ItemSpec("Morning", "Last Shopping", "Buy shito, shea butter, fabric or art before you fly.", 20m, "Souvenirs"),
                        new ItemSpec("Afternoon", "Fly Home to Lagos or Abuja", "About an hour to Lagos. Leave plenty of time for Accra traffic on the way to Kotoka.", 200m, "Departure"),
                        new ItemSpec("Afternoon", "Extension: Fly to Kumasi", "Add 2 nights in the Asante capital. The domestic flight takes under an hour; the road takes 4-6 hours.", 90m, "Optional Extension", null, true),
                        new ItemSpec("Evening", "Extension: Manhyia Palace Museum", "Royal regalia, wax figures of Asante kings and the history of the Golden Stool. Time it with an Akwasidae festival if you can.", 9m, "Optional Extension", ManhyiaName, true),
                        new ItemSpec("Evening", "Extension: Kejetia Market", "One of West Africa's largest markets, selling kente, beads and almost everything else. Go with a guide.", 0m, "Optional Extension", KejetiaName, true),
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

        // Same scoring rules as BaliSeeder / AfricaPocSeeder / KenyaNorthSeeder.
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
