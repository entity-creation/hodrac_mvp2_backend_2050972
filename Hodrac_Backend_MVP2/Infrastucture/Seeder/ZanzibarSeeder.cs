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
    /// "Zanzibar in Six Days: Stone Town to Paje", a 6-day template wishlist pitched at Nigerian
    /// travellers: Stone Town (UNESCO) with the Forodhani night market, the Anglican Cathedral and
    /// former slave market and the Freddie Mercury Museum; Prison Island's giant tortoises; a spice
    /// farm; Nungwi and Kendwa with a sunset dhow cruise; snorkelling at Mnemba Atoll; Jozani's red
    /// colobus monkeys; Paje/Jambiani kitesurfing; The Rock restaurant; and an optional Safari Blue
    /// day in Menai Bay.
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// Tanzania and TZS are new; English and Swahili are reused by their existing natural keys
    /// (specs copied character for character from AfricaPocSeeder / KenyaNorthSeeder).
    /// All prices are rough USD approximations checked against 2025-2026 web sources where
    /// available and labelled approximate. Prose follows the humanizer writing guide.
    ///
    /// Sources (checked November 2026):
    ///  - Tanzania visa for Nigerians: Nigeria removed from the "Referred Visa" category by
    ///    Government Gazette No. 246 of 2026 (notice 25 Sep 2026), legit.ng 30 Sep 2026;
    ///    ordinary visa US$50, up to 90 days, apply at visa.immigration.go.tz, ~10 days processing.
    ///  - Inbound insurance: Zanzibar (ZIC) since 1 Oct 2024; Mainland (NIC) from 1 Oct 2026;
    ///    US$44 adult / US$22 child 3-17 / free 0-2, valid 92 days, one policy per trip bought by
    ///    first point of entry (inbound.visitzanzibar.go.tz). atta.travel "Tanzania's mandatory
    ///    travel insurance implementation update" (29 Sep 2026); norway.no; rwandair.com alert.
    ///  - Yellow fever: Air Tanzania-hosted YF notice lists Nigeria among countries whose
    ///    travellers need a certificate; gov.uk Tanzania entry requirements; travelhealthpro.org.uk
    ///    (Nigeria requires a YF certificate for everyone entering or leaving, Aug 2025 update).
    ///  - House of Wonders: partial collapse Dec 2020 (globalvoices.org); restoration stalled, no
    ///    reopening date (lonelyplanet.com, Beit el-Ajaib).
    ///  - Old Fort: closed for renovation from Jan 2026 under a US$12m Zanzibar/Infinity heritage
    ///    deal (dumaexplorer.com blog; ippmedia.com and allafrica.com, Feb 2026).
    ///  - Anglican Cathedral and slave market: ~US$5-$10 incl. guide, ~8 AM-6 PM
    ///    (lonelyplanet.com; travelcostcalculator.org; Wikipedia "Christ Church, Zanzibar").
    ///  - Freddie Mercury Museum: opened 2019, ~US$10, roughly 10:00-18:00 (serenahotels.com;
    ///    queenonline.com 2019; hours differ between sources).
    ///  - Prison Island: Aldabra giant tortoises, used as a quarantine station, ~20-30 min by boat
    ///    (fodors.com; holidify.com; Expedia listings; entry quoted US$4-$22, inconsistent).
    ///  - Jozani: ~US$10 non-resident adult, 7:30 AM-5 PM (altezzatravel.com; jaynevytours.com).
    ///  - Mnemba: Mnemba Island Marine Conservation Area (2002); fees revised 1 Sep 2025, special
    ///    area ~US$25 adult (thecitizen.co.tz; atta.travel 03 Sep 2025; getaway.co.za).
    ///  - The Rock: reservation-only lunch/dinner seatings, tide-dependent access
    ///    (lonelyplanet.com; atlasobscura.com; nomadotravel.app). Coordinates approximate.
    ///  - Safari Blue: dhow day from Fumba, sandbank, snorkelling, Kwale Island lunch;
    ///    ~US$60-$120 shared (Expedia listings; wearetanzania guide).
    ///  - Ferry Dar-Zanzibar: Azam Marine / Kilimanjaro Fast Ferries, daily, roughly 1.5-2 hours,
    ///    economy ~US$35-$45 (azammarine.com; 12go.asia; ferryscanner.com).
    ///  - Flights Lagos-Zanzibar: no direct flights; one stop via Nairobi (Kenya Airways) or Addis
    ///    Ababa (Ethiopian), also Doha/Dubai (skyscanner.net; cheapflights.com; ethiopianairlines.com).
    ///  - TZS: Wise mid-market snapshots ~2,570-2,650 TZS per USD in 2026 (wise.com).
    /// </summary>
    public static class ZanzibarSeeder
    {
        private const string BlobBase = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            // Stored in jsonb, never rendered as raw HTML; keep accents/apostrophes readable.
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

        private const string Tanzania = "Tanzania";

        private const string English = "English";      // existing natural key (AfricaPocSeeder)
        private const string Swahili = "Swahili";      // existing natural key (KenyaNorthSeeder)

        private const string StoneTown = "Stone Town";
        private const string Nungwi = "Nungwi";
        private const string Paje = "Paje";
        private const string Fumba = "Fumba";

        // Names checked against every other seeder: none collide (no Zanzibar/Tanzania data elsewhere).
        private const string StoneTownName = "Stone Town of Zanzibar";
        private const string ForodhaniName = "Forodhani Gardens Night Food Market";
        private const string CathedralName = "Anglican Cathedral & Former Slave Market";
        private const string FreddieName = "Freddie Mercury Museum";
        private const string PrisonIslandName = "Prison Island (Changuu)";
        private const string SpiceFarmName = "Kizimbani Spice Farms";
        private const string NungwiBeachName = "Nungwi Beach";
        private const string KendwaName = "Kendwa Beach";
        private const string MnembaName = "Mnemba Atoll";
        private const string JozaniName = "Jozani Chwaka Bay National Park";
        private const string PajeName = "Paje & Jambiani Beaches";
        private const string TheRockName = "The Rock Restaurant";
        private const string SafariBlueName = "Safari Blue, Menai Bay";

        private static readonly LanguageSpec[] Languages =
        {
            // Same specs as AfricaPocSeeder / KenyaNorthSeeder; only created if missing.
            new(English, "en", Array.Empty<(string, string)>()),
            new(Swahili, "sw", new[] { ("Hello", "Jambo / Habari"), ("Thank you", "Asante"), ("How much?", "Bei gani?"), ("Slowly", "Pole pole") }),
        };

        // Approximate mid-market rate, 2026 (Wise snapshots ~2,570-2,650 TZS per USD); refresh before relying on it.
        private static readonly CurrencySpec[] Currencies =
        {
            new("TZS", "Tanzanian Shilling", "TSh", 2600m),
        };

        private static readonly CountrySpec[] Countries =
        {
            // VAT: Mainland standard rate 18%; Zanzibar levies its own VAT (confirm the current rate).
            new(Tanzania, "tanzania", "Africa", "🇹🇿",
                "Most visitors, including Nigerians, need a visa. Apply online for an eVisa at the official portal (visa.immigration.go.tz) well before travel: the ordinary single-entry tourist visa costs about US$50 for stays of up to 90 days, and processing can take around 10 days. In September 2026 Tanzania took Nigeria off its 'referred visa' list, so Nigerians no longer need the extra clearance from the Commissioner General of Immigration. Every foreign visitor must also buy Tanzania's mandatory inbound travel insurance, about US$44 per adult and US$22 per child aged 3-17, valid for up to 92 days. If your trip starts in Zanzibar, buy it from the Zanzibar Insurance Corporation at inbound.visitzanzibar.go.tz; your own travel insurance does not count. Travellers coming from countries with yellow fever risk, Nigeria included, should carry a valid yellow fever certificate. Requirements change, so check official sources before you travel.",
                "Type D/G", "Left", 0.18m, new[] { English, Swahili }, "TZS", "Africa/Dar_es_Salaam"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(StoneTown, Tanzania, -6.1630, 39.1880,
                "The old quarter of Zanzibar City, on the west coast of Unguja island, and a UNESCO World Heritage Site: coral-stone houses with carved doors, mosques, churches and bazaars along narrow lanes. Abeid Amani Karume International Airport (ZNZ) is about 15-20 minutes' drive away, and fast ferries from Dar es Salaam dock at the port on the edge of the old town."),
            new(Nungwi, Tanzania, -5.7270, 39.2960,
                "A fishing and dhow-building village at the northern tip of Unguja, about 1-1.5 hours' drive from Stone Town. Hotels run along the beach south to Kendwa, the sea here stays swimmable at most tides, and boats leave from here and from Matemwe for Mnemba Atoll."),
            new(Paje, Tanzania, -6.2650, 39.5350,
                "A village on the south-east coast with a wide, shallow lagoon behind the reef, used by kitesurfing schools for much of the year. The tide goes out a long way. Jambiani, Bwejuu and Michamvi (where The Rock restaurant is) are on the same stretch of coast, and Jozani forest is on the road from Stone Town."),
            new(Fumba, Tanzania, -6.3170, 39.2830,
                "A peninsula in the south-west of Unguja, about 30-45 minutes' drive south of Stone Town. Dhow trips into the Menai Bay Conservation Area, including Safari Blue, leave from here."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] ZanzibarLangs = { English, Swahili };

        private const string DressCode =
            "Dress Code: Most Zanzibaris are Muslim. In Stone Town and the villages, men and women should cover shoulders and knees, and swimwear stays on the beach or at the hotel. Kissing in public and drinking in the street are frowned upon. During Ramadan, avoid eating, drinking or smoking in public in daylight.";

        private const string Insurance =
            "Entry Insurance: Every foreign visitor needs Tanzania's mandatory inbound travel insurance (about US$44 per adult). Buy it before you fly at inbound.visitzanzibar.go.tz and keep the certificate with your passport.";

        private const string Tides =
            "Tides: The east coast (Paje, Jambiani, Michamvi) has a large tidal range, and at low tide the sea can pull back hundreds of metres. Check a tide table before planning swimming, kitesurfing or boat trips.";

        private const string Touts =
            "Beach Boys & Touts: Book tours through your hotel or an operator with recent reviews, agree the price before you set off, and turn down unsolicited offers with 'Hapana, asante' (No, thank you).";

        private static readonly DestSpec[] Destinations =
        {
            // ─── Stone Town ────────────────────────────────────────────────────
            new("zanzibar", StoneTownName, Tanzania, StoneTown, ZanzibarLangs, -6.1622, 39.1921,
                40m, "Walk", 2,
                new[] { "neighborhood_district", "cultural_site", "landmark_monument" },
                new[] { "history", "cultural", "architecture", "walkable", "photography", "tourist_hotspot" },
                new[] { "Stone Town", "Stonetown", "Mji Mkongwe", "Zanzibar Stone Town", "Zanzibar Old Town", "House of Wonders", "Beit el-Ajaib", "Old Fort", "Ngome Kongwe" }, "Street",
                Desc(
                    "Stone Town, the old quarter of Zanzibar City, is a UNESCO World Heritage Site. Its lanes are lined with coral-stone merchants' houses and carved wooden doors, mosques, churches, Indian-owned shops and the old Sultans' palaces, built up over centuries of trade between the Swahili coast, Arabia, Persia, India and Europe.\n\nWhat to Do: Walk it with a licensed local guide, then wander on your own. Look for the House of Wonders (Beit el-Ajaib), the Old Fort, the Old Dispensary, Darajani market and the carved doors.\n\nStatus Check: The House of Wonders partly collapsed in December 2020, restoration has stalled, and it can only be seen from outside. The Old Fort closed for a major renovation in early 2026; check whether it has reopened before planning around it.",
                    "People live and work here. Families sit on the stone benches (barazas) outside their homes in the evening, and the call to prayer marks the day. Nigerian visitors who know Badagry may want to read up on the East African slave trade before visiting the cathedral and former slave market.",
                    "Best Access: About 15-20 minutes by taxi from Abeid Amani Karume International Airport (ZNZ), or a short walk from the ferry port if you arrive from Dar es Salaam.\n\nGetting Around: On foot. Most lanes are too narrow for cars.",
                    "Guides: A 2-3 hour guided walk costs about US$15-$30 per person. Ask your hotel for a licensed guide.\n\nMoney: Hotels and tour operators widely accept US dollars. Carry Tanzanian shillings for markets, taxis and food stalls; Stone Town has ATMs, but they are scarcer elsewhere on the island.\n\n" + DressCode + "\n\n" + Insurance,
                    "Getting Lost: Easy to do in the lanes, so download an offline map.\n\nTouts: Men offering to show you around will approach you; decline politely if you have already booked a guide.\n\nAfter Dark: Stick to busier lanes and the waterfront at night.",
                    "Guided Walk: approx. US$15-$30 per person.\nAirport Taxi: approx. US$10-$20.\nMid-Range Boutique Hotel: approx. US$80-$180 per room per night.",
                    new[]
                    {
                        "Forodhani Gardens Night Food Market: On the waterfront at sunset.",
                        "Anglican Cathedral & Former Slave Market: A short walk inland.",
                        "Prison Island (Changuu): 20-30 minutes offshore by boat."
                    },
                    "June-October (cooler and dry) and December-February (hot and dry). March-May is the long rainy season.",
                    "High (7/10) around the waterfront, quieter in the back lanes.",
                    "Rating: 4/10. Narrow, uneven lanes and steps; no cars.",
                    "1 to 2 days.")),

            new("zanzibar", ForodhaniName, Tanzania, StoneTown, ZanzibarLangs, -6.1594, 39.1883,
                15m, "Walk", 2,
                new[] { "market_street_life", "food_experience" },
                new[] { "food_focused", "local_favorite", "budget_friendly", "social", "best_evening" },
                new[] { "Forodhani Gardens", "Forodhani", "Forodhani Night Market", "Forodhani Park", "Jamhuri Gardens" }, "Street",
                Desc(
                    "Forodhani Gardens is the seafront park in front of the Old Fort and the House of Wonders. Around sunset each evening, vendors set up grills and tables for Stone Town's night food market: seafood skewers, chapati, samosas, sugar-cane juice and 'Zanzibar pizza', a stuffed pancake cooked on a griddle.\n\nWhat to Do: Arrive for sunset, watch local boys diving off the sea wall, and eat your way along the stalls.",
                    "Zanzibaris use the gardens to stroll and meet friends in the cooler evening air, and plenty of the customers are local. Nigerian visitors used to suya spots will recognise the grills.",
                    "Best Access: On the Stone Town waterfront (Mizingani Road), a few minutes' walk from most Stone Town hotels.",
                    "Timing: Stalls usually open around sunset (about 6 PM) and run into the evening; confirm on the day.\n\nPrices: Ask the price before the vendor starts cooking, and ask whether the seafood was caught that day.\n\nRamadan: During Ramadan the market fills up after iftar, when the fast is broken.",
                    "Food Safety: Pick busy stalls that cook to order and eat the food hot. Skip anything that has been sitting out.\n\nOverpricing: Some vendors quote tourists high prices, so agree the price first.",
                    "Dinner: approx. US$5-$15 per person.\nZanzibar Pizza: approx. US$2-$4.\nSugar-Cane Juice: approx. US$1.",
                    new[]
                    {
                        "Stone Town of Zanzibar: The old town behind the gardens.",
                        "Freddie Mercury Museum: A few minutes' walk along the waterfront.",
                        "Prison Island (Changuu): Boats leave from the nearby beach."
                    },
                    "Every evening, year-round. The dry months are most comfortable.",
                    "High (8/10) at sunset.",
                    "Rating: 7/10. Flat paved park, crowded at peak times.",
                    "1 to 2 hours.")),

            new("zanzibar", CathedralName, Tanzania, StoneTown, ZanzibarLangs, -6.1660, 39.1923,
                10m, "Walk", 1,
                new[] { "cultural_site", "landmark_monument", "religious_site" },
                new[] { "history", "educational", "cultural", "architecture" },
                new[] { "Christ Church Cathedral", "Christ Church Zanzibar", "Anglican Cathedral Zanzibar", "Slave Market Zanzibar", "Mkunazini", "East Africa Slave Trade Exhibit", "Zanzibar Slave Market" }, "Exterior",
                Desc(
                    "Christ Church Cathedral in Mkunazini was built in the 1870s on the site of Zanzibar's last open slave market, which closed in 1873 under British pressure. The altar is said to stand where the whipping post was. Next door, an exhibition covers the East African slave trade, and visitors can go down into a cramped underground chamber where captives were held before sale. Outside is a sculpture of chained figures standing in a pit.\n\nWhat to Do: Take the guided visit through the exhibition, the holding chambers, the sculpture and the cathedral, and allow time afterwards.",
                    "Zanzibar's role in the East African slave trade is central to the island's history, and this is where most visitors learn about it. Many African visitors, Nigerians among them, compare it with the slave-trade sites at Badagry, Cape Coast and Gorée.",
                    "Best Access: About a 10-minute walk inland from the Stone Town waterfront, near Darajani market.",
                    "Entry: One ticket covers the exhibition, the slave chambers and the cathedral, usually with a guide. Recent sources quote about US$5-$10 per person; confirm at the gate.\n\nHours: Typically about 8 AM-6 PM daily, with services taking priority on Sundays.\n\n" + DressCode,
                    "Emotional Weight: The chambers are small, dark and distressing, so think twice about bringing young children or going in if you are claustrophobic.\n\nRespect: The cathedral is a working church. Keep your voice low and dress modestly.",
                    "Entry with Guide: approx. US$5-$10 per person.\nTip for Guide: optional, approx. US$2-$5.",
                    new[]
                    {
                        "Stone Town of Zanzibar: The old town all around.",
                        "Freddie Mercury Museum: Across the old town.",
                        "Forodhani Gardens Night Food Market: On the waterfront."
                    },
                    "Year-round. Mornings are cooler.",
                    "Medium (5/10).",
                    "Rating: 4/10. The holding chambers are reached by narrow steps.",
                    "1 to 1.5 hours.")),

            new("zanzibar", FreddieName, Tanzania, StoneTown, ZanzibarLangs, -6.1612, 39.1873,
                10m, "Walk", 1,
                new[] { "museum_gallery" },
                new[] { "music", "history", "quirky", "good_short_visit" },
                new[] { "Freddie Mercury House", "Freddie Mercury Museum Zanzibar", "Mercury House", "Farrokh Bulsara", "Freddie Mercury Birthplace" }, "Exterior",
                Desc(
                    "Freddie Mercury, the lead singer of Queen, was born Farrokh Bulsara in Stone Town in 1946 to a Parsi family. The Freddie Mercury Museum opened in 2019 in a house on Kenyatta Road in Shangani where the family lived, and covers his childhood on the island and his career through photos, memorabilia and recordings.\n\nWhat to Do: Stop in if you like Queen, then walk along the Shangani waterfront.",
                    "Mercury said little in public about Zanzibar. Local opinion on him is mixed, since parts of his life sit uneasily with the island's conservative values.",
                    "Best Access: Kenyatta Road, Shangani, Stone Town, a short walk from Forodhani Gardens.",
                    "Entry: About US$10 per adult, less for children; cash or card.\n\nHours: Usually about 10 AM-6 PM daily, but sources differ, so confirm before you go.",
                    "Size: The museum is small; allow under an hour.\n\nQueues: It gets busy when tour groups arrive.",
                    "Entry: approx. US$10 per adult.",
                    new[]
                    {
                        "Forodhani Gardens Night Food Market: Minutes away.",
                        "Stone Town of Zanzibar: All around.",
                        "Anglican Cathedral & Former Slave Market: Across the old town."
                    },
                    "Year-round.",
                    "Medium (5/10).",
                    "Rating: 5/10. An old house with stairs.",
                    "45 minutes to 1 hour.")),

            new("zanzibar", PrisonIslandName, Tanzania, StoneTown, ZanzibarLangs, -6.1180, 39.1650,
                40m, "Boat", 1,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "family_friendly", "photography", "history", "tourist_hotspot" },
                new[] { "Prison Island", "Changuu", "Changuu Island", "Kibandiko", "Quarantine Island", "Prison Island Zanzibar", "Zanzibar Tortoises" }, "Landscape",
                Desc(
                    "Changuu, usually called Prison Island, is a small island a short boat ride off Stone Town. A prison was built there in the 1890s, but it never held prisoners and was used as a quarantine station instead. The island now keeps giant Aldabra tortoises, originally a gift from the Seychelles, some of them more than 100 years old.\n\nWhat to Do: See the tortoises, walk round the old buildings, and swim or snorkel off the beach before the boat back.",
                    "Families from Stone Town often make this a half-day trip, and boat captains on the waterfront run it daily.",
                    "Best Access: About 20-30 minutes by motorboat from the beach near Forodhani Gardens in Stone Town. Book through your hotel or agree a price with a registered boat operator.",
                    "Entry: A fee for the island and tortoise sanctuary is charged on arrival. Sources quote anything from about US$4 to over US$20, so confirm the current rate when booking.\n\nTours: Half-day tours with boat, guide and entry usually cost about US$30-$40 per person.\n\n" + Touts,
                    "Boats: Check there are life jackets on board, and don't go out in rough weather.\n\nTortoises: Don't sit on them or feed them anything except the greens the keepers provide.\n\nSun: The boat has little shade, so bring a hat and sunscreen.",
                    "Boat (shared/half-day tour): approx. US$30-$40 per person.\nIsland Entry: approx. US$4-$20 (confirm).\nSnorkel Gear: often included; otherwise approx. US$5.",
                    new[]
                    {
                        "Stone Town of Zanzibar: Your boat departure point.",
                        "Forodhani Gardens Night Food Market: Back on shore for dinner.",
                        "Safari Blue, Menai Bay: A longer sea day to the south."
                    },
                    "Year-round in calm weather. Mornings are usually calmer.",
                    "Medium-High (6/10).",
                    "Rating: 4/10. Boarding a small boat from the beach.",
                    "Half a day (3-4 hours).")),

            new("zanzibar", SpiceFarmName, Tanzania, StoneTown, ZanzibarLangs, -6.1000, 39.2500,
                25m, "Car", 1,
                new[] { "activity_experience", "food_experience", "nature_outdoor" },
                new[] { "food_focused", "educational", "family_friendly", "cultural", "nature" },
                new[] { "Spice Tour", "Zanzibar Spice Tour", "Spice Farm", "Kizimbani", "Spice Island Tour", "Zanzibar Spice Farm" }, "Landscape",
                Desc(
                    "Zanzibar grew rich on cloves in the 19th century and is still called the Spice Island. Farms around Kizimbani and Kindichi, a short drive north-east of Stone Town, grow cloves, nutmeg, cinnamon, cardamom, vanilla, black pepper and lemongrass alongside tropical fruit.\n\nWhat to Do: Join a guided walk round a working farm, taste spices and fruit from the plant, watch someone climb a coconut palm, and often finish with a Swahili lunch cooked with the spices.",
                    "Many of the farms are family-run and depend on the spice harvest as well as visitors, so buying from the farm shop puts money straight into the farm.",
                    "Best Access: About 20-30 minutes by car from Stone Town. Most visitors book a half-day tour with hotel pickup.",
                    "Tours: Half-day spice tours usually cost about US$15-$30 per person with transport. Some farms charge a small entry or guide fee if you arrive on your own; confirm when booking.\n\nShopping: Farm-shop prices are usually fair, but compare before you buy, and check your home country's customs rules on plant products.",
                    "Sales Pitch: Many tours end at a shop. Buying is optional; tipping the guide and palm climber is customary.\n\nInsects: Wear mosquito repellent. The farms are leafy and humid.",
                    "Spice Tour with Transport: approx. US$15-$30 per person.\nTips (guide, palm climber): approx. US$2-$5.\nSpices to Take Home: approx. US$2-$5 per packet.",
                    new[]
                    {
                        "Stone Town of Zanzibar: 20-30 minutes away.",
                        "Nungwi Beach: On the road north.",
                        "Jozani Chwaka Bay National Park: On the road east."
                    },
                    "Year-round. The clove harvest runs roughly July-December.",
                    "Medium (5/10).",
                    "Rating: 5/10. Farm paths, muddy after rain.",
                    "Half a day.")),

            // ─── Nungwi / Kendwa ──────────────────────────────────────────────
            new("zanzibar", NungwiBeachName, Tanzania, Nungwi, ZanzibarLangs, -5.7270, 39.2960,
                90m, "Car", 1,
                new[] { "beach_coast", "viewpoint_scenic_spot" },
                new[] { "beach", "relaxing", "photography", "couple_friendly", "best_evening", "tourist_hotspot" },
                new[] { "Nungwi", "Nungwi Beach Zanzibar", "Ras Nungwi", "Nungwi Village", "Nungwi Sunset Cruise", "Mnarani Turtle Aquarium" }, "Landscape",
                Desc(
                    "Nungwi, at the northern tip of Unguja, is the busiest beach village on the island. It has white sand, clear water and west-facing sunsets. Dhow builders still work on the beach, and the Mnarani natural aquarium near the lighthouse looks after sea turtles.\n\nWhat to Do: Swim (tides affect the north coast far less than the east), take a sunset dhow cruise, watch the dhow builders, and eat seafood on the beach.",
                    "Behind the hotels, Nungwi is still a fishing and boat-building village, with a fish market in the mornings and dhow yards on the sand.",
                    "Best Access: About 1-1.5 hours by road from Stone Town or the airport (around 60 km). A taxi costs about US$40-$60; dala-dala minibuses cost far less but are slow.",
                    "Sunset Dhow Cruise: About US$25-$50 per person, often with drinks and snacks. Book through your hotel or an operator with recent reviews.\n\nMoney: Nungwi has few ATMs, so bring cash.\n\n" + DressCode + "\n\n" + Touts,
                    "Currents: Swim where locals and hotel staff advise; some points have strong currents.\n\nSea Urchins: Wear water shoes on rocky sections.\n\nSun: UV is strong all year.",
                    "Sunset Dhow Cruise: approx. US$25-$50 per person.\nMid-Range Beach Hotel: approx. US$80-$200 per room per night.\nSeafood Dinner: approx. US$15-$30.",
                    new[]
                    {
                        "Kendwa Beach: A short walk or ride south.",
                        "Mnemba Atoll: Snorkelling trips from Nungwi and Matemwe.",
                        "Kizimbani Spice Farms: On the road from Stone Town."
                    },
                    "June-October and December-February are driest. Beach conditions are good most of the year outside the long rains (March-May).",
                    "High (7/10).",
                    "Rating: 6/10. Soft sand; hotel access varies.",
                    "2 to 3 days.")),

            new("zanzibar", KendwaName, Tanzania, Nungwi, ZanzibarLangs, -5.7530, 39.2900,
                80m, "Car", 1,
                new[] { "beach_coast", "entertainment_nightlife" },
                new[] { "beach", "relaxing", "nightlife", "social", "couple_friendly", "best_at_night" },
                new[] { "Kendwa", "Kendwa Rocks", "Kendwa Beach Zanzibar", "Kendwa Full Moon Party" }, "Landscape",
                Desc(
                    "Kendwa is a few kilometres south of Nungwi on the north-west coast. The beach is wide and white, you can swim at almost any tide, and the sun sets over the sea. Beach bars hold regular parties, including a monthly full-moon party.\n\nWhat to Do: Swim and lie on the beach during the day, then have a drink at sunset.",
                    "On holiday weekends young Tanzanians and visitors from elsewhere in East Africa come here to party alongside international tourists.",
                    "Best Access: About 1-1.5 hours by road from Stone Town. From Nungwi, walk along the beach at low tide or take a short taxi ride.",
                    "Parties: Full-moon parties charge entry and get crowded; ask your hotel for dates.\n\n" + DressCode + "\n\n" + Touts,
                    "Night Safety: Don't walk the beach alone late at night; take a taxi back.\n\nDrinks: Keep an eye on your drink at parties.",
                    "Beach Bar Sundowner: approx. US$3-$8 per drink.\nParty Entry: approx. US$10-$20.\nMid-Range Hotel: approx. US$80-$200 per room per night.",
                    new[]
                    {
                        "Nungwi Beach: Just north.",
                        "Mnemba Atoll: Snorkelling day trip.",
                        "Stone Town of Zanzibar: About 1-1.5 hours south."
                    },
                    "June-October and December-February.",
                    "Medium-High (6/10), busy on party nights.",
                    "Rating: 6/10. Soft sand.",
                    "1 to 3 days.")),

            new("zanzibar", MnembaName, Tanzania, Nungwi, ZanzibarLangs, -5.8200, 39.3830,
                70m, "Boat", 2,
                new[] { "nature_outdoor", "activity_experience", "beach_coast" },
                new[] { "nature", "adventurous", "photography", "tourist_hotspot" },
                new[] { "Mnemba", "Mnemba Island", "Mnemba Reef", "Mnemba Island Marine Conservation Area", "MIMCA", "Mnemba Snorkelling" }, "Landscape",
                Desc(
                    "Mnemba Atoll lies about 3 km off Zanzibar's north-east coast and is the island's main snorkelling and diving area. It has been protected as the Mnemba Island Marine Conservation Area since 2002. The coral holds reef fish, turtles and starfish, and dolphins are often seen nearby. The island itself is private and reserved for guests of its lodge, so visitors snorkel the reef around it.\n\nWhat to Do: Take a half- or full-day boat trip with two or three snorkel stops, often with dolphin-spotting and lunch on a beach.",
                    "Boat crews from Matemwe and Nungwi earn much of their living from these trips, and the conservation fees pay for managing the reef.",
                    "Best Access: By boat from Matemwe (about 20-30 minutes) or from Nungwi or Kendwa (about 1 hour). Most trips include hotel pickup.",
                    "Conservation Fee: Zanzibar revised its marine park fees from 1 September 2025, and the Mnemba special area is quoted at about US$25 per non-resident adult. Check whether your tour includes it.\n\nTours: Shared snorkelling trips usually cost about US$40-$80 per person; diving costs more.\n\nDolphins: Choose an operator whose boats keep their distance, and don't jump in on top of the dolphins.",
                    "Seasickness: The crossing can be choppy, so take something if you are prone to it.\n\nReef Care: Don't stand on or touch coral, and use reef-safe sunscreen.\n\nCrowds: In high season many boats arrive at once. Early departures are quieter.",
                    "Snorkelling Trip: approx. US$40-$80 per person.\nConservation Fee: approx. US$25 per adult (if not included).\nTwo-Dive Trip: approx. US$120-$180.",
                    new[]
                    {
                        "Nungwi Beach: Departure point and base.",
                        "Kendwa Beach: Base to the south-west.",
                        "Paje & Jambiani Beaches: Down the east coast."
                    },
                    "Calmest seas roughly October-March; visibility is good in the dry months.",
                    "High (7/10) in peak season.",
                    "Rating: 3/10. Boarding from the beach and swimming in open water.",
                    "Half a day to a full day.")),

            // ─── Paje / east coast ────────────────────────────────────────────
            new("zanzibar", JozaniName, Tanzania, Paje, ZanzibarLangs, -6.2650, 39.4150,
                15m, "Car", 1,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "educational", "family_friendly", "photography", "hidden_gem" },
                new[] { "Jozani", "Jozani Forest", "Jozani Chwaka Bay", "Jozani National Park", "Red Colobus", "Zanzibar Red Colobus", "Jozani Mangrove Boardwalk" }, "Landscape",
                Desc(
                    "Jozani Chwaka Bay National Park is Zanzibar's only national park. It protects the island's last large area of native forest and its mangroves, and it is the easiest place to see the endangered Zanzibar red colobus, a monkey found only on Zanzibar. Sykes' monkeys, bushbabies and forest birds live here too.\n\nWhat to Do: Take a guided forest walk to watch the red colobus at close range, then walk the mangrove boardwalk.",
                    "Villages around the park receive a share of its revenue. Local guides talk about how farmers once treated the monkeys as crop pests and how that has changed.",
                    "Best Access: On the main road from Stone Town to the south-east coast, about 45-60 minutes from Stone Town and 15-20 minutes from Paje. It fits easily into the transfer to the east coast.",
                    "Entry: About US$10 per non-resident adult, usually with a guide included; confirm the current rate at the gate.\n\nHours: Roughly 7:30 AM-5 PM daily.\n\nMonkeys: Stay a few metres away, don't feed them, and turn off your flash.",
                    "Mosquitoes: Use repellent, especially near the mangroves.\n\nHeat: Go in the morning or late afternoon, when it is cooler and the monkeys are more active.",
                    "Entry with Guide: approx. US$10 per adult.\nGuide Tip: optional, approx. US$2-$5.",
                    new[]
                    {
                        "Paje & Jambiani Beaches: 15-20 minutes east.",
                        "The Rock Restaurant: On the same coast.",
                        "Kizimbani Spice Farms: Back towards Stone Town."
                    },
                    "Year-round. Driest June-October and December-February.",
                    "Medium (5/10).",
                    "Rating: 6/10. Flat forest trails and a boardwalk.",
                    "1.5 to 2 hours.")),

            new("zanzibar", PajeName, Tanzania, Paje, ZanzibarLangs, -6.2680, 39.5370,
                80m, "Car", 1,
                new[] { "beach_coast", "activity_experience" },
                new[] { "beach", "adventurous", "relaxing", "social", "photography" },
                new[] { "Paje", "Paje Beach", "Jambiani", "Jambiani Beach", "Bwejuu", "Paje Kitesurfing", "Paje Kite Beach" }, "Landscape",
                Desc(
                    "Paje and Jambiani, next to each other on the south-east coast, have long white beaches facing a shallow lagoon inside the reef. Steady trade winds have made Paje one of the main kitesurfing spots in East Africa, with many schools teaching beginners. Jambiani, to the south, is quieter and more of a village, and at low tide seaweed farmers work plots in the lagoon.\n\nWhat to Do: Take a beginner kitesurfing lesson, walk the sandbars at low tide, visit a seaweed farm, and spend the evening at a beach bar.",
                    "Seaweed farming, done mostly by women, is a major source of income on this coast. Some cooperatives take visitors and sell seaweed soap and other products.",
                    "Best Access: About 1-1.5 hours by road from Stone Town or the airport, passing Jozani. From Nungwi, allow about 1.5-2 hours.",
                    "Kitesurfing: The main wind seasons are roughly June-September (the stronger 'kusi' wind) and December-February ('kaskazi'). Lessons run about US$70-$100 an hour, and a beginner course of several days costs a few hundred US dollars; compare schools and check they are IKO-certified.\n\n" + Tides + "\n\n" + DressCode,
                    "Low Tide: You may have to walk a long way out to reach swimming depth.\n\nSea Urchins & Seaweed: Wear water shoes in the lagoon.\n\nKite Safety: Learn only with a certified school, and swim away from the kite zone.",
                    "Kitesurf Lesson (beginner): approx. US$70-$100 per hour; multi-day courses approx. US$300-$500.\nBeach Hotel: approx. US$60-$180 per room per night.\nSeaweed Farm Visit: approx. US$5-$15.",
                    new[]
                    {
                        "The Rock Restaurant: A short drive north at Michamvi Pingwe.",
                        "Jozani Chwaka Bay National Park: 15-20 minutes inland.",
                        "Mnemba Atoll: Up the east coast."
                    },
                    "June-October and December-February for dry weather and wind.",
                    "Medium (5/10).",
                    "Rating: 6/10. Flat beach; getting into the water depends on the tide.",
                    "2 to 3 days.")),

            new("zanzibar", TheRockName, Tanzania, Paje, ZanzibarLangs, -6.2110, 39.5450,
                60m, "Car", 1,
                new[] { "food_experience", "viewpoint_scenic_spot" },
                new[] { "food_focused", "romantic", "photography", "premium", "tourist_hotspot" },
                new[] { "The Rock", "The Rock Zanzibar", "Rock Restaurant", "The Rock Michamvi", "The Rock Pingwe", "Rock Restaurant Zanzibar" }, "Exterior",
                Desc(
                    "The Rock is a small seafood restaurant on a coral outcrop off Pingwe beach, near Michamvi on the south-east coast. At low tide you walk across the sand to it. At high tide it is surrounded by water and a boat takes you over. It is one of the most photographed restaurants on the island.\n\nWhat to Do: Book a lunch or early-dinner seating, plan it around the tide, and order the seafood.",
                    "It is a popular stop for East African and Nigerian visitors. Locals often point visitors to the simpler beach grills nearby, which serve similar fish for much less.",
                    "Best Access: About 15-25 minutes' drive north of Paje on the Michamvi peninsula, and about 1-1.5 hours from Stone Town.\n\nAt High Tide: A short boat ride from the beach for a small fee.",
                    "Reservations: Essential. The restaurant runs set seatings (sources mention about 12 PM and 2 PM for lunch and 4 PM and 6 PM for dinner); book well ahead, ideally a couple of weeks in peak season. A deposit may be required.\n\nPrices: Roughly US$40-$80 per person with a drink.\n\n" + Tides,
                    "Value: Many reviewers rate the setting above the food.\n\nTide Timing: Check the tide for your seating. High tide gives the view of the restaurant surrounded by sea; low tide lets you walk out.",
                    "Meal: approx. US$40-$80 per person.\nBoat Transfer at High Tide: approx. US$2-$5.\nTaxi from Paje: approx. US$10-$20.",
                    new[]
                    {
                        "Paje & Jambiani Beaches: Just south.",
                        "Jozani Chwaka Bay National Park: Inland.",
                        "Mnemba Atoll: Further up the coast."
                    },
                    "Year-round. The dry months are best.",
                    "High (8/10). Seatings fill up.",
                    "Rating: 3/10. Wading or a small boat, then steps.",
                    "1.5 to 2 hours.")),

            // ─── Fumba / Menai Bay ────────────────────────────────────────────
            new("zanzibar", SafariBlueName, Tanzania, Fumba, ZanzibarLangs, -6.3200, 39.2900,
                90m, "Boat", 2,
                new[] { "activity_experience", "nature_outdoor", "beach_coast" },
                new[] { "adventurous", "nature", "group_friendly", "photography", "food_focused" },
                new[] { "Safari Blue", "Safari Blue Zanzibar", "Menai Bay", "Menai Bay Conservation Area", "Kwale Island", "Fumba Dhow Trip", "Safari Blue Tour" }, "Landscape",
                Desc(
                    "Safari Blue is a full-day dhow trip from Fumba into the Menai Bay Conservation Area on Zanzibar's south-west coast. The usual route stops at a sandbank, snorkels a coral reef, visits a mangrove lagoon and ends with a lunch of seafood and tropical fruit on Kwale Island. Boats often look for dolphins in the bay, and the trip home is under sail.\n\nWhat to Do: Spend a slow day on the water. It suits groups and families.",
                    "'Safari Blue' was the original operator's name, but many other companies now run similar trips under the same name, so check who you are booking with. Local boat crews and cooks depend on these tours for work.",
                    "Best Access: Hotel pickup to Fumba, about 30-45 minutes from Stone Town and longer from Nungwi or Paje.",
                    "Price: Shared tours usually cost about US$60-$120 per person, including lunch, snorkel gear and often the conservation fee. Private dhows cost more.\n\nTiming: A full day, roughly 9 AM-5 PM including transfers.\n\nDolphins: Choose an operator that doesn't chase or crowd the dolphins.",
                    "Sun: You spend hours on the water with little shade, so bring a hat, sunscreen and a cover-up.\n\nSeasickness: The bay is usually calm, but take something if you are prone to it.\n\nCopycats: Read recent reviews before booking.",
                    "Shared Tour: approx. US$60-$120 per person.\nPrivate Dhow: approx. US$120-$250 per person.\nTips for Crew: approx. US$5-$10.",
                    new[]
                    {
                        "Stone Town of Zanzibar: 30-45 minutes north.",
                        "Prison Island (Changuu): A shorter boat trip off Stone Town.",
                        "Jozani Chwaka Bay National Park: Across the island."
                    },
                    "June-October and December-February for calm, dry weather.",
                    "Medium-High (6/10).",
                    "Rating: 3/10. Boarding dhows from the beach and wading.",
                    "A full day.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlist
        // ═══════════════════════════════════════════════════════════════════════

        // ItineraryItem.TimeOfDay is a controlled value (Morning/Afternoon/Evening, validated by the
        // admin and user itinerary endpoints), so any specific clock times go in the item titles.
        private static readonly WishlistSpec[] Wishlists =
        {
            new("zanzibar", "Zanzibar in Six Days: Stone Town to Paje", Tanzania,
                "A 6-day trip round Unguja, Zanzibar's main island, planned for travellers from Nigeria. The first two days are in Stone Town, the UNESCO-listed old town: a guided walk, the Anglican Cathedral and former slave market, the Freddie Mercury Museum, the giant tortoises on Prison Island, and dinner at the Forodhani Gardens night market. After a morning on a spice farm, the trip moves north to Nungwi and Kendwa for the beach, a sunset dhow cruise and a day snorkelling at Mnemba Atoll. The last stop is the south-east coast, with red colobus monkeys in Jozani forest, an optional kitesurfing lesson in Paje and a seating at The Rock, a restaurant built on a rock in the sea. A Safari Blue dhow day in Menai Bay can be added. There are no direct flights from Nigeria: connect through Nairobi (Kenya Airways), Addis Ababa (Ethiopian), Doha or Dubai, or fly to Dar es Salaam and take the fast ferry (about 1.5-2 hours). Nigerians need a Tanzanian eVisa (about US$50), every visitor must buy Zanzibar's mandatory inbound travel insurance (about US$44), and you should carry a yellow fever certificate. Requirements change, so check official sources before booking. International flights are not included, and all prices are rough USD estimates.",
                "Six days on Zanzibar, from the lanes and night market of Stone Town to a snorkelling trip at Mnemba and a seat at The Rock.",
                "Couples & Friends",
                new[] { "5 Nights in Boutique & Beach Hotels (2 Stone Town, 2 Nungwi/Kendwa, 1 Paje)", "Airport & Inter-Island Road Transfers", "Guided Stone Town Walk", "Prison Island & Spice Farm Tours", "Mnemba Atoll Snorkelling Trip", "Jozani Forest Entry with Guide", "Sunset Dhow Cruise" },
                "Zanzibar, Unguja, Spice Island, Stone Town, UNESCO, House of Wonders, Old Fort, Forodhani Gardens, night food market, Zanzibar pizza, Freddie Mercury, Anglican Cathedral, slave market, Prison Island, Changuu, giant tortoises, spice tour, Kizimbani, Nungwi, Kendwa, sunset dhow cruise, Mnemba Atoll, snorkelling, dolphins, Jozani, red colobus, Paje, Jambiani, kitesurfing, The Rock restaurant, Michamvi, Safari Blue, Menai Bay, Fumba, Tanzania, Swahili coast, beach holiday, honeymoon, Nigerians, Lagos, Abuja, eVisa, travel insurance",
                new[] { "Beach", "Culture", "Romance", "Island Life" },
                1500m, 300m,
                "5 nights (shared double, mid-range): 2 in a Stone Town boutique hotel, 2 in a Nungwi or Kendwa beach hotel, 1 in Paje",
                "Airport pickup and drop-off at Abeid Amani Karume International Airport (ZNZ) and private road transfers Stone Town - Nungwi - Paje; international flights from Lagos/Abuja (via Nairobi, Addis Ababa, Doha or Dubai) not included; Dar es Salaam - Zanzibar fast ferry optional",
                "Guided Stone Town walk, Anglican Cathedral & slave market exhibit, Freddie Mercury Museum, Prison Island boat trip, spice farm tour, sunset dhow cruise in Nungwi, Mnemba snorkelling trip, Jozani forest walk, Paje beach (kitesurf lesson and The Rock optional)",
                false, "Island Romantic",
                new[]
                {
                    new DaySpec(1, "Karibu Zanzibar · Stone Town", StoneTown, StoneTown, StoneTown, new[]
                    {
                        new ItemSpec("Morning", "Arrive at Zanzibar Airport (ZNZ)", "Fly in from Lagos or Abuja through Nairobi, Addis Ababa, Doha or Dubai (there are no direct flights). Have your eVisa, insurance certificate and yellow fever card ready, then transfer 15-20 minutes to Stone Town.", 20m, "Arrival"),
                        new ItemSpec("Morning", "Or: Fast Ferry from Dar es Salaam", "If you fly into Dar es Salaam, take one of the daily fast ferries (Azam Marine or Kilimanjaro, roughly 1.5-2 hours) to Stone Town port.", 40m, "Optional", null, true),
                        new ItemSpec("Afternoon", "Guided Stone Town Walk", "Carved doors, the Old Dispensary, Darajani market and the outside of the House of Wonders, closed since part of it collapsed in 2020. Ask your guide whether the Old Fort has reopened after its 2026 renovation.", 25m, "Must-See", StoneTownName),
                        new ItemSpec("Evening", "6:00 PM · Forodhani Night Food Market", "Sunset on the waterfront, then seafood skewers, Zanzibar pizza and sugar-cane juice. Agree prices before the vendor cooks.", 12m, "Local Favorite", ForodhaniName),
                    }),
                    new DaySpec(2, "Stone Town History & Prison Island", StoneTown, StoneTown, StoneTown, new[]
                    {
                        new ItemSpec("Morning", "Anglican Cathedral & Former Slave Market", "The cathedral built on Zanzibar's last open slave market, with the slave-trade exhibition and the underground holding chambers. Allow time; many visitors find it hard going.", 10m, "Must-See", CathedralName),
                        new ItemSpec("Morning", "Freddie Mercury Museum", "The Queen singer was born in Stone Town in 1946. The small museum on Kenyatta Road opens around 10 AM.", 10m, "Quirky", FreddieName),
                        new ItemSpec("Afternoon", "Prison Island Tortoises", "A 20-30 minute boat ride to Changuu to see giant tortoises, some over 100 years old, and the old quarantine buildings, with a swim off the beach.", 35m, "Family Favorite", PrisonIslandName),
                        new ItemSpec("Evening", "Rooftop Dinner in Stone Town", "Sunset over the harbour from a rooftop restaurant, with Swahili curries, octopus and fresh juice.", 25m, "Sunset"),
                    }),
                    new DaySpec(3, "Spice Farm & North to Nungwi", StoneTown, Nungwi, Nungwi, new[]
                    {
                        new ItemSpec("Morning", "Spice Farm Tour", "Taste cloves, nutmeg, cinnamon, vanilla and pepper on a working farm near Kizimbani, often followed by a Swahili lunch.", 25m, "Spice Island", SpiceFarmName),
                        new ItemSpec("Afternoon", "Transfer North to Nungwi", "About 1-1.5 hours to the northern tip. The north coast is swimmable at most tides, so you can be in the sea soon after check-in.", 50m, "Road Trip", NungwiBeachName),
                        new ItemSpec("Evening", "Sunset Dhow Cruise from Nungwi", "Sail along the coast on a traditional dhow while the sun sets over the Indian Ocean.", 35m, "Must-Do", NungwiBeachName),
                    }),
                    new DaySpec(4, "Mnemba Snorkelling & Kendwa Sunset", Nungwi, Nungwi, Nungwi, new[]
                    {
                        new ItemSpec("Morning", "7:30 AM · Mnemba Atoll Snorkelling", "An early boat to the protected reef around Mnemba for coral, turtles and often dolphins. Check whether the conservation fee is included.", 60m, "Bucket List", MnembaName),
                        new ItemSpec("Afternoon", "Beach Time at Kendwa", "Swim and rest on the wide beach a short ride south of Nungwi.", 0m, "Relax", KendwaName),
                        new ItemSpec("Evening", "Kendwa Sundowner", "Drinks on the sand at sunset. On full-moon weekends the beach party starts after dark.", 10m, "Nightlife", KendwaName),
                    }),
                    new DaySpec(5, "Red Colobus & the East Coast", Nungwi, Paje, Paje, new[]
                    {
                        new ItemSpec("Morning", "Jozani Forest & Red Colobus", "Drive south (about 1.5-2 hours) and stop at Jozani to see the endangered red colobus, found only on Zanzibar, and walk the mangrove boardwalk.", 10m, "Wildlife", JozaniName),
                        new ItemSpec("Afternoon", "Paje Beach & Kitesurf Taster", "Check in at Paje and try a beginner kitesurfing lesson in the shallow lagoon (wind seasons roughly June-September and December-February), or walk the sandbars at low tide.", 90m, "Adventure", PajeName),
                        new ItemSpec("Evening", "4:00 PM · Dinner at The Rock", "The restaurant on a rock in the sea near Michamvi. Book a seating well ahead and check the tide.", 60m, "Bucket List", TheRockName, true),
                        new ItemSpec("Evening", "Beach Bar Dinner in Paje", "Grilled fish at a table on the sand.", 20m, "Laid Back", PajeName),
                    }),
                    new DaySpec(6, "Lagoon Morning & Kwaheri", Paje, Paje, StoneTown, new[]
                    {
                        new ItemSpec("Morning", "Jambiani Village & Seaweed Farms", "Walk south along the lagoon to Jambiani at low tide and visit a women's seaweed cooperative.", 10m, "Local Life", PajeName),
                        new ItemSpec("Morning", "Or: Safari Blue in Menai Bay", "Add a day for the dhow trip from Fumba: a sandbank, snorkelling, mangroves and a seafood lunch on Kwale Island.", 90m, "Optional Extension", SafariBlueName, true),
                        new ItemSpec("Afternoon", "Transfer to the Airport", "About 1 hour across the island to ZNZ for your flight home.", 50m, "Departure"),
                        new ItemSpec("Evening", "Fly Home", "Connect through Nairobi, Addis Ababa, Doha or Dubai back to Lagos or Abuja.", 0m, "Journey's End"),
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
