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
    /// Standalone destinations — places added one at a time, not tied to any wishlist.
    /// Same structure as <see cref="KenyaNorthSeeder"/> / <see cref="CalabarSeeder"/> minus the wishlist:
    /// every row is looked up by its natural key first and only created when missing, so running it
    /// any number of times converges on the same data.
    ///
    /// Reference rows (Nigeria, NGN, Africa/Lagos, English, Yoruba) use exactly the same natural keys
    /// as <see cref="AfricaPocSeeder"/> so existing rows are reused rather than duplicated.
    /// AverageCostPerDay is in USD (same convention as AfricaPocSeeder's Nigerian destinations);
    /// prices inside the description text stay in local currency as supplied.
    ///
    /// To add a destination: append a <c>DestSpec</c> to <see cref="Destinations"/> (template at the
    /// bottom of that array). If its city/country/language/currency is new, add the matching spec to
    /// <see cref="Cities"/> / <see cref="Countries"/> / <see cref="Languages"/> / <see cref="Currencies"/>.
    /// The image is named <c>{Region}_{normalized-name}.jpg</c>, where Region is the city slug.
    /// </summary>
    public static class DestinationsSeeder
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

        /// <param name="Region">Lowercase city slug, used as the image filename prefix (e.g. "ibadan").</param>
        private sealed record DestSpec(
            string Region, string Name, string CountryName, string CityName, string[] LanguageNames,
            double Lat, double Lng, decimal AvgCost, string AccessibilityType, int Safety,
            string[] Categories, string[] Tags, string[] Alternates, string ShotContext, DescriptionJsonDto Desc);

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
        private const string Edo = "Edo (Bini)";
        private const string Hausa = "Hausa";
        private const string Efik = "Efik";

        private const string Ibadan = "Ibadan";
        private const string BeninCity = "Benin City"; // existing natural key (AfricaPocSeeder)
        private const string Abuja = "Abuja";          // existing natural key (AfricaPocSeeder)
        private const string Calabar = "Calabar";      // existing natural key (CalabarSeeder)
        private const string Lagos = "Lagos";          // existing natural key (AfricaPocSeeder)

        // Names checked against every other seeder — none collide.
        private const string IitaName = "IITA Ibadan (International Institute of Tropical Agriculture)";
        private const string MreName = "Motorsport Raceway Evbuobanosa (MRE)";
        private const string TroisOreName = "Trois Ore Residences";
        private const string EdenName = "Eden Botanical Garden";
        private const string SariusName = "Sarius Palmetum";
        private const string KaheraName = "Kahera Country Club";
        private const string SpaceVrName = "Space VR Benin";
        private const string JordansPlayworldName = "Jordan's Playworld";
        private const string KadaPlazaName = "Kada Plaza";
        private const string DeependName = "DeepEnd Cinemas & Fun House";
        private const string VittoName = "Vitto Lounge";
        private const string GenesisBeninName = "Genesis Cinemas Benin City";
        private const string FilmhouseBeninName = "Filmhouse Cinemas Benin";
        private const string AtzName = "Amazing Target Zone";
        private const string CombatGamesZoneName = "Combat Games Zone";
        private const string HopeWaddellName = "Hope Waddell Training Institution";
        private const string KwaFallsName = "Kwa Falls";
        private const string CalabarFoodName = "Calabar Local Food Spots";
        private const string KrissKrossName = "Kriss Kross Skating Rink";
        private const string ZenArcheryName = "Zen Archery";
        private const string RubeName = "The Rube";
        private const string TerraKultureName = "Terra Kulture";
        private const string CeraCerniName = "CeraCerni's Art Hub";
        private const string KayakInLagosName = "Kayak In Lagos";
        private const string NationalMuseumLagosName = "National Museum Lagos";
        private const string PitstopName = "Pitstop Lagos";
        private const string WindsorGalleryName = "Windsor Gallery Lagos";
        private const string SealTeamName = "Seal Team Paintball";
        private const string GamelandName = "Gameland Lagos";
        private const string RufusBeeName = "Rufus & Bee";
        private const string GiwaGardensName = "Giwa Gardens Water Park";
        private const string ImageGardenName = "Image Garden";
        private const string FlorisGardenName = "Floris Garden";
        private const string MagiclandName = "Magicland Amusement Park";
        private const string FunAlleyName = "Fun Alley";
        private const string ChildrensParkZooName = "National Children's Park and Zoo";
        private const string AmazonKayakName = "Amazon Kayak";
        private const string BmtGardenName = "BMT African Garden";
        private const string MonolizaName = "Monoliza Sports & Recreation Center";
        private const string CentralParkAbujaName = "Central Park Abuja";
        private const string TukurSnakeFarmName = "Tukur & Tukur Snake Farm";
        private const string WellnessClubName = "The Wellness Club (TWC)";
        private const string BarracudaName = "Barracuda Resto Pub";
        private const string GoWellnessName = "GoWellness Resort";
        private const string TuftopiaName = "Tuftopia";
        private const string PaintPotteryName = "Paint and Pottery Social";
        private const string VarunaName = "Varuna by The Good Beach";
        private const string SiamsGardenName = "Siam's Garden";
        private const string VitalityClubhouseName = "Vitality Clubhouse";
        private const string AxesRosesName = "Axes & Roses";
        private const string EliLuluName = "Eli & Lulu Café";

        private static readonly LanguageSpec[] Languages =
        {
            // Same specs as AfricaPocSeeder — only created if missing.
            new(English, "en", Array.Empty<(string, string)>()),
            new(Yoruba, "yo", new[] { ("Thank you", "Ẹ ṣé"), ("How much?", "Ẹ̀lò ni?") }),
            new(Edo, "bin", new[] { ("Greeting", "Kóyo") }),
            new(Hausa, "ha", new[] { ("Hello", "Sannu"), ("Thank you", "Na gode") }),
            // Same spec as CalabarSeeder — only created if missing.
            new(Efik, "efi", new[] { ("How are you?", "Abadie?"), ("Thank you", "Sosongo") }),
        };

        private static readonly CurrencySpec[] Currencies =
        {
            // Same spec as AfricaPocSeeder — only created if missing (an existing row's rate is left untouched).
            new("NGN", "Nigerian Naira", "₦", 1500m),
        };

        private static readonly CountrySpec[] Countries =
        {
            // Same spec as AfricaPocSeeder — only created if missing; language links are added only if missing.
            new(Nigeria, "nigeria", "Africa", "🇳🇬",
                "Visa required for most nationalities (e-visa / visa on arrival approval via the Nigeria Immigration Service); check official requirements before travel.",
                "Type D/G", "Right", 0.075m, new[] { English, Yoruba, Edo, Hausa, Efik }, "NGN", "Africa/Lagos"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Ibadan, Nigeria, 7.3775, 3.9470,
                "Capital of Oyo State in south-western Nigeria and one of the country's largest cities — a sprawling Yoruba city of hills and rust-roofed old quarters, home to the University of Ibadan and the International Institute of Tropical Agriculture, and linked to Lagos by the Lagos–Ibadan railway."),
            // Same spec as AfricaPocSeeder — reused if it exists.
            new(BeninCity, Nigeria, 6.3350, 5.6037,
                "Capital of Edo State and heart of the historic Kingdom of Benin, known worldwide for its royal bronze-casting tradition."),
            // Same spec as AfricaPocSeeder — reused if it exists.
            new(Abuja, Nigeria, 9.0765, 7.3986,
                "Nigeria's purpose-built capital in the centre of the country, laid out around Aso Rock with wide boulevards, landmark buildings and green parks."),
            // Same spec as CalabarSeeder — reused if it exists.
            new(Calabar, Nigeria, 4.9767, 8.3383,
                "Capital of Cross River State in Nigeria's south-east, on the Calabar River — a green, laid-back city with colonial-era heritage, the Efik culture of the Ekpe society, and every December the Carnival Calabar, billed as 'Africa's Biggest Street Party'."),
            // Same spec as AfricaPocSeeder — reused if it exists.
            new(Lagos, Nigeria, 6.5244, 3.3792,
                "Nigeria's commercial capital and one of Africa's largest cities — a high-energy sprawl of islands and lagoons famous for its music, art, food and nightlife."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] IbadanLangs = { English, Yoruba };
        private static readonly string[] EdoLangs = { English, Edo };
        private static readonly string[] AbujaLangs = { English, Hausa }; // same as AfricaPocSeeder
        private static readonly string[] CalabarLangs = { English, Efik }; // same as CalabarSeeder
        private static readonly string[] LagosLangs = { English, Yoruba }; // same as AfricaPocSeeder

        private static readonly DestSpec[] Destinations =
        {
            // ─── Ibadan ────────────────────────────────────────────────────────
            // Founder's first-hand facts (booking, 50% deposit, room types, breakfast, 8:45 PM food
            // cut-off, train-then-cab advice, activity prices) are used as given.
            // Coordinates: CGIAR's location page for the IITA Ibadan campus (Oyo Road, Idi Ose).
            // AvgCost: ~US$50/day ≈ ₦68,000 at ~₦1,360 per US$ (official rate, mid-November 2026) —
            // a room, meals and one paid activity per person per day; rough approximation.
            new("ibadan", IitaName, Nigeria, Ibadan, IbadanLangs, 7.4962, 3.9076,
                50m, "Car", 1,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "relaxing", "family_friendly", "couple_friendly", "group_friendly", "local_favorite", "photography" },
                new[] { "IITA", "IITA Ibadan", "International Institute of Tropical Agriculture", "IITA Conference Center", "IITA Conference Centre", "IITA Hospitality", "IITA Hotel", "IITA Forest" }, "Landscape",
                Desc(
                    "The International Institute of Tropical Agriculture (IITA) is an international agricultural research institute founded in 1967, with its headquarters campus on Oyo Road on the northern side of Ibadan. Beyond the labs and research fields, the gated campus is a green retreat — a forest reserve, a lake, a golf course, a swimming pool and sports facilities — and IITA Hospitality runs guest rooms and a conference centre on site, so you can stay the night.\n\nWhere to Stay: Rooms are split between the International House and the Residential Blocks; Residential Block rooms are the more expensive option.\n\nWhat to Do: Many indoor and outdoor activities, some paid — forest walks, fishing on the lake with a lesson and gear, golf lessons and rounds, swimming (free) and cycling around the campus.",
                    "For people in Lagos and Ibadan, IITA is a weekend escape from city traffic and noise — a quiet, green place to walk, cycle, swim and switch off for a night or two. It is often described as one of Ibadan's hidden gems.",
                    "From Lagos (Recommended): Take the train, then a cab. The Lagos–Ibadan train runs from Mobolaji Johnson Station (Ebute Metta, Lagos) to Obafemi Awolowo Station at Moniya, the Ibadan terminus — roughly 2.5 to 3 hours. Moniya is on the northern side of Ibadan, the same side as IITA's Oyo Road campus, so the cab ride on is short. Check the current timetable and ticketing with the Nigerian Railway Corporation before you travel.\n\nBy Road: You can also drive or take a bus up the Lagos–Ibadan Expressway and cross Ibadan to Oyo Road, but journey times vary a lot with traffic.\n\nWithin Ibadan: Take a cab to IITA on Oyo Road.",
                    "Booking: Book through IITA Hospitality's Instagram (@iitahospitality) or website (hospitality.iita.org); reservation requests can also be emailed to iita-reservations@cgiar.org. Visitors are expected to have a confirmed reservation before arriving.\n\nDeposit: A minimum 50% deposit confirms every booking.\n\nRoom Types: Rooms are split into the International House and the Residential Blocks; Residential Block rooms are more expensive.\n\nBreakfast: Some rooms include complimentary breakfast — a standard buffet. Check whether yours does when you book.\n\nFood Cut-Off: Food can't be ordered after 8:45 PM.\n\nSwimming: Free — bring your own swimwear.\n\nCheck-In: Check-in has been reported as 3:00 PM and check-out as 12 noon — confirm when you book.",
                    "Late Food Cut-Off: Food can't be ordered after 8:45 PM — on a late arrival or a late evening, order before then or bring snacks.\n\nPaid Activities Add Up: The forest walk, fishing, golf and bike rental are each charged separately, so for a group the total climbs fast — budget for them (see the hidden costs).\n\nBook Ahead: Rooms fill up, especially at weekends; secure your dates and pay the 50% deposit well before you travel.\n\nLast Leg: Sort out your cab from Moniya station before you arrive, especially on an evening train.",
                    "Deposit: Minimum 50% of the booking, paid upfront to confirm.\nForest Walk: ₦10,000 per person.\nFishing — Transport to the Spot: ₦2,000 per person.\nFishing — Permit: ₦5,000.\nFishing — Activity (lesson and equipment rental): ₦10,000 per person.\nGolf Lesson: ₦10,000 (group tip: one person pays for the lesson and teaches the others).\nGolf Green Fee: ₦8,000–₦15,000.\nGolf Caddy: ₦2,000 per person.\nBike Rental: ₦3,500 per bike.\nSwimming: Free — bring your own swimwear.\nTrain Ticket & Cab from Moniya: Extra — check current NRC fares and agree the cab fare before you set off.",
                    new[]
                    {
                        "University of Ibadan: Nigeria's oldest university, with a zoological garden on campus.",
                        "Bower's Tower: A 1936 memorial tower on Oke-Are hill with wide views over Ibadan.",
                        "Agodi Gardens: A recreation park in central Ibadan with a lake, a water park and wooded walks."
                    },
                    "Year-round; the dry season (roughly November to March) is easiest for forest walks, golf and cycling. Book weekends well ahead.",
                    "Low to Medium (4/10) — busier at weekends and on public holidays.",
                    "Rating: 6/10 — paved campus roads, but forest trails, the golf course and the lake shore are uneven, and the campus is large with little internal transport, so expect to walk or cycle between spots.",
                    "A weekend — 1 to 2 nights.")),

            // ─── Benin City (Edo State) ────────────────────────────────────────
            // No founder input — researched. Sources: autojosh.com (Feb & Nov 2018), tracc.eu (May 2022),
            // bellanaija.com (May 2019), fastestlap9ja.com (+ /contact.html), en.wikipedia.org/wiki/Orhionmwon,
            // finelib.com (Km 32 Benin-Asaba Expressway, Evbuobanosa, Abudu address), fmino.gov.ng (highway works).
            // Coordinates: APPROXIMATE — from the MyRacingCareer track database (6.278632, 5.993760); they
            // reverse-geocode (OSM Nominatim) to Abudu, Orhionmwon LGA, consistent with the Km 32 address.
            // Not confirmed on an official map. City: reuses Benin City (nearest city with a row), as
            // AfricaPocSeeder does for Okomu National Park.
            // AvgCost: ~US$30/day ≈ ₦41,000 at ~₦1,360 per US$ (mid-November 2026) — transport from Benin
            // City, a race-day ticket and food; ticket prices are unpublished, so this is a rough guess.
            new("benin-city", MreName, Nigeria, BeninCity, EdoLangs, 6.2786, 5.9938,
                30m, "Car", 3,
                new[] { "activity_experience" },
                new[] { "adventurous", "hidden_gem", "social", "group_friendly", "seasonal", "photography" },
                new[] { "MRE", "Motorsport Raceway", "Motorsport Raceway Evbuobanosa", "Motorsport Raceway, Evbuobanosa", "MotorSport Raceway Evbuobanosa", "Evbuobanosa Motorsport Raceway", "Evbuobanosa Raceway", "Evbuobanosa Race Track" }, "Landscape",
                Desc(
                    "Motorsport Raceway, Evbuobanosa (MRE) is a privately built motor-racing circuit in Evbuobanosa, a community in Orhionmwon Local Government Area, Edo State, beside the Benin–Asaba highway east of Benin City. Launched in 2017 and billed as Nigeria's first purpose-built car race track, it sits on about 130 acres; the track is about 2.3 km long with 10–11 bends and can be run clockwise or anti-clockwise. Coverage from 2018 listed a clubhouse with an observation deck, a restaurant, a tyre centre, a fuel station, day garages, a swimming pool and a gift shop — confirm what is open now.\n\nWhat to Do: Come on a race day to watch car and superbike racing. The circuit has hosted the annual Motorsport Championship, the RoadX race series (BMW E30 / Mercedes-Benz 190 cars), the Superbike Nigeria Bikes Trophy (BT) championship and, in January 2026, the Fastest Lap Championship. Track days, karting or driving experiences for visitors are not published — ask the organisers.",
                    "With so few purpose-built circuits in Nigeria, MRE is known among Nigerian motorsport fans as the home of track racing in the country, and race days bring car and bike enthusiasts together. Outside events, expect a quiet rural site.",
                    "From Benin City: Head east on the Benin–Asaba highway; listings place Evbuobanosa at about Km 32, near Abudu. Promoters have quoted about 20 minutes' drive from Benin City — allow longer for traffic and road works. Go by private car or a hired car with driver; no shuttle is published.\n\nBy Air: Fly into Benin Airport (BNI) in Benin City, then drive.\n\nFrom Lagos by Road: A long day's drive to Benin City, then on to Evbuobanosa — flying to Benin is usually easier.",
                    "Event-Based Venue: The raceway is mainly used for race days and events; no public opening hours are published, so confirm before you go.\n\nTickets: The Fastest Lap Championship race day at MRE on 25 January 2026 (2:00 PM) sold tickets through fastestlap9ja.com (payment by bank transfer); the organiser takes questions on WhatsApp at +234 706 302 4881. Ticket prices weren't published on the site.\n\nNo Official Venue Site: We couldn't find an official website or Instagram for the raceway itself — follow event organisers for dates.\n\nFacilities: A clubhouse, restaurant and pool were listed in 2018 — confirm what is open on your date.",
                    "Check Before You Travel: Online information is thin and much of it dates from 2017–2019 — confirm the event, start time and access with the organisers before making the trip.\n\nRoad Travel: Travel in daylight. The Benin–Asaba highway is the subject of a 125 km federal reconstruction project, so expect works and delays, and check current travel advice for Edo State.\n\nTrackside Safety: Stay in spectator areas and follow marshals' instructions.\n\nHeat: Bring water, a hat and sunscreen for outdoor race days.",
                    "Race-Day Ticket: Not published — confirm with the organiser.\nCar & Driver from Benin City (round trip): Extra — agree the price in advance.\nFood & Drinks: Extra — confirm whether the clubhouse restaurant is open on the day.\nFlight Lagos/Abuja–Benin City: Extra if you're not driving.",
                    new[]
                    {
                        "Royal Palace of the Oba of Benin: Seat of the Benin monarchy, in Benin City.",
                        "Benin National Museum: Benin bronzes and history at King's Square.",
                        "Igun Street Bronze Casters: Benin City's historic bronze-casting street."
                    },
                    "On a scheduled race day — check the organisers' calendars. Past events ran in November (2018 Motorsport Championship, 2019 Superbike BT finale) and January (2026 Fastest Lap Championship); the dry season (roughly November to March) is the most comfortable.",
                    "Low (2/10) normally; Medium to High (6/10) on race days.",
                    "Rating: 4/10 — a rural site reached only by road; spectator areas may be uneven or have steps — confirm with the organisers.",
                    "Half a day for a race day, plus travel from Benin City.")),

            // ─── Ibadan ────────────────────────────────────────────────────────
            // No founder input — researched. Sources: tileri.com (home, /about-us/, /check-in-guide/),
            // hotels.ng/hotel/hn436347s-trois-ore-residences-green-earth-bistro,
            // trip.com/hotels/ibadan-hotel-detail-111095568/trois-ore-residences-green-earth-bistro/.
            // Official spelling: "Trois Ore Residences" (tileri.com); the brand/domain/handles use "Tileri".
            // Coordinates: APPROXIMATE — OSM Nominatim centroid of Oba Akenzua Crescent, Ibadan
            // (7.4211292, 3.9083660; OSM tags it Old Bodija, Ibadan North). The property's address says
            // Onireke GRA; the exact plot is not mapped in OSM.
            // AvgCost: ~US$110/day ≈ ₦150,000 at ~₦1,360 per US$ (mid-November 2026) — half of a
            // ₦175,000-₦250,000 room shared by two, breakfast and one more meal; rough approximation.
            new("ibadan", TroisOreName, Nigeria, Ibadan, IbadanLangs, 7.4211, 3.9084,
                110m, "Car", 1,
                new[] { "food_experience" },
                new[] { "relaxing", "couple_friendly", "premium", "architecture", "food_focused" },
                new[] { "Trois Ore Residence", "Trois Ore Residences & Green Earth Bistro", "Trois Ore Residences and Green Earth Bistro", "Trois Oré Residences", "Trois Ore", "3 Ore Residences", "Tileri Hotel", "Tileri", "Green Earth Bistro" }, "Exterior",
                Desc(
                    "Trois Ore Residences is a small boutique hotel in Onireke GRA, Ibadan, with nine individually named bedrooms and its own restaurant, Green Earth Bistro. Listings give the property as opened in 2021. The hotel's own site describes a mud-built, adobe-style building designed for natural airflow, set among greenery, and a bistro seating 26 that serves health-focused fine dining. There is also a lounge and meeting space for small business meetings and workshops.\n\nRooms: Nine named rooms — Ìlera (Wellness), Ọgbà (Garden), Ọrẹ (Gift), Ìdèra (Wellbeing), Ọ̀rẹ́ (Friend), Ore (Favour), Loft, Cabin and Cove.",
                    "Trois Ore is a quiet, design-led stay in one of Ibadan's older residential areas. Reviews are few but mostly praise the green setting, the architecture and the staff.",
                    "Address: Plot 7, Commercial Subdivision, Oba Akenzua Crescent, Onireke GRA, Ibadan, Oyo State. Some listings add Bluegate Estate.\n\nGetting There: Take a cab or ride-hail to the address. The property does not offer airport transfers, and free parking is listed for drivers.\n\nFrom Lagos: Take the Lagos–Ibadan train to Moniya, or go by road, then a cab across Ibadan.",
                    "Booking: Book on the hotel's website (tileri.com/bookings) or by phone; the site lists 0700TROISORE and 0705 421 2060. The property is also listed on booking platforms, and its Instagram is @tilerihotel. The hotel's site warns guests to beware of fraudulent booking messages, so book through official channels.\n\nCheck-In/Out: Check-in is from 2:00 PM. Check-out is listed as 11:00 AM on some platforms and 12 noon on others, so confirm when you book. Hotels.ng lists a 50% surcharge for check-out after 12:00 and a full night's charge from 18:00. Bring a government-issued ID matching the booking name.\n\nHouse Rules (per listings): No smoking anywhere on the premises, no pets, and quiet hours from midnight to 7:00 AM.\n\nAmenities (per listings): Restaurant, free Wi-Fi, air conditioning, room service, a 24-hour front desk, free parking and a garden. A spa, laundry and the conference room cost extra. There is no swimming pool.",
                    "Book Officially: Use the hotel's own website or phone lines, or a known booking platform — the hotel warns about fraudulent booking messages.\n\nFew Reviews: Ratings rest on a handful of reviews. Most are very positive, but one guest mentioned unreliable hot water, a small dining area, stairs to reception and value for money.\n\nPrices Vary by Source: Rates differ between the hotel's site and booking platforms — compare before you pay.\n\nNo Pool: The hotel has no swimming pool.",
                    "Room Rate (hotel website): From ₦175,000 (Cove), ₦185,000 (Ọrẹ – Gift) and ₦187,500 (Ọ̀rẹ́ – Friend).\nRoom Rate (Hotels.ng listing, average per night): ₦210,000–₦250,000 depending on the room.\nBreakfast (Trip.com listing): about ₦16,555 per adult if not included.\nSpa, Laundry & Conference Room: Extra.\nLate Check-Out: 50% of the rate after 12:00; a full night from 18:00 (Hotels.ng listing).",
                    new[]
                    {
                        "IITA Ibadan: Forest, lake and golf on the International Institute of Tropical Agriculture campus, on Oyo Road on the northern side of the city.",
                        "University of Ibadan: Nigeria's oldest university, with a zoological garden on campus.",
                        "Agodi Gardens: A recreation park in central Ibadan with a lake, a water park and wooded walks."
                    },
                    "Year-round; book ahead for weekends — there are only nine rooms.",
                    "Low (2/10) — a nine-room boutique property.",
                    "Rating: 5/10 — a guest review mentions stairs to reception; ask the hotel about ground-floor rooms and step-free access.",
                    "1 to 2 nights.")),

            // ─── Abuja ─────────────────────────────────────────────────────────
            // Eden Botanical Garden — no founder input; researched. Thin sources: sabiabuja.com (Oct 2025,
            // the only source for address, hours and fee), Facebook page "Eden Botanical Events | Abuja",
            // TikTok @prettyabuja ("Recreational Park in Abuja: Eden Botanical Garden").
            // Coordinates: APPROXIMATE — OSM Nominatim point on Shehu Yar'Adua Way, Jabi (9.0792015, 7.4266021),
            // near Jabi Lake Mall; the garden itself is not mapped.
            // AvgCost: ~US$8 ≈ ₦11,000 at ~₦1,360 per US$ (Nov 2026) — entry, a drink/snack and a ride.
            new("abuja", EdenName, Nigeria, Abuja, AbujaLangs, 9.0792, 7.4266,
                8m, "Car", 2,
                new[] { "nature_outdoor" },
                new[] { "nature", "relaxing", "hidden_gem", "photography", "budget_friendly" },
                new[] { "Eden Botanicals", "Eden Botanicals Abuja", "Eden Botanical Garden Abuja", "Eden Botanical Gardens", "Eden Botanical Events", "Eden Botanical Jabi" }, "Exterior",
                Desc(
                    "Eden Botanical Garden is a small green space and plant nursery on Shehu Yar'adua Way in Jabi, Abuja, a few minutes from Jabi Lake Mall. Besides the garden, it sells live plants — succulents, tropical houseplants, flowering shrubs, herbs and outdoor plants — and hires out its grounds for events (managed as Eden Botanical Events).\n\nWhat to Do: Walk the garden, buy plants, sit and read or work outdoors, or book it for a birthday, bridal shower, small wedding, exhibition or photoshoot.",
                    "It is described in local guides as a quiet, low-key escape from Abuja's busier spots — somewhere to slow down for an hour among plants.",
                    "Best Access: By cab or ride-hail to Shehu Yar'adua Way, Jabi — a few minutes from Jabi Lake Mall.",
                    "Hours: Reported as Monday–Saturday, 8:00 AM–6:00 PM — confirm before you go.\n\nEntry: Reported as ₦1,500 per person, free if you buy a plant.\n\nEvents: Book through Instagram @edenbotanical_events.\n\nLimited Information: Details come mainly from one 2025 guide — check hours, fees and the exact entrance with the venue.",
                    "Check Before You Go: Opening days, fees and event bookings may have changed — confirm with the venue.\n\nPrivate Events: Parts of the garden may be closed for events.\n\nSimilar Names: Don't confuse it with Eden Garden in Utako, a separate venue.",
                    "Entry: ₦1,500 per person (free with a plant purchase).\nPlants: Priced individually.\nEvent Hire: Ask the venue.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Jabi Lake: Lakeside walks and Jabi Lake Mall, a few minutes away.",
                        "Millennium Park: Abuja's big public park in Maitama."
                    },
                    "Year-round in daylight hours; the dry season (roughly November to March) is most comfortable outdoors.",
                    "Low (2/10).",
                    "Rating: 6/10 — garden paths; confirm step-free access with the venue.",
                    "1 to 2 hours.")),

            // Sarius Palmetum — no founder input; researched. Sources: sabiabuja.com/sarius-palmetum-abuja/
            // (May 2025), placestotour.com/trip/sarius-palmetum-abuja/, nigerianheritage.ng/tour-detail.php?id=417,
            // tools.bgci.org/garden.php?id=4885, hotels.ng/places/parks/5497-sarius-palmetum,
            // instagram.com/sariuspalmetum, sariuspalmetum.com (title only).
            // Coordinates: BGCI GardenSearch listing (9.10372, 7.48889); OSM reverse-geocodes them to
            // Ibrahim Babangida Boulevard, Maitama, matching the published address.
            // AvgCost: ~US$11 ≈ ₦15,000 at ~₦1,360 per US$ (Nov 2026) — entry, café snack and a ride.
            new("abuja", SariusName, Nigeria, Abuja, AbujaLangs, 9.10372, 7.48889,
                11m, "Car", 2,
                new[] { "nature_outdoor" },
                new[] { "nature", "relaxing", "family_friendly", "photography", "educational", "budget_friendly" },
                new[] { "Sarius Palmetum Botanical Garden", "Sarius Palmetum and Botanical Garden", "Sarius Palmetum & Botanical Garden", "SariusPalmetum", "Sarius Palmetum Abuja", "Sarius Palmetum Maitama" }, "Landscape",
                Desc(
                    "Sarius Palmetum is a botanical garden and event venue on Ibrahim Babangida Boulevard in Maitama, Abuja, covering about 20–21 hectares. It holds a large palm collection — sources cite 400 to 450 palm species — alongside around 1,000 other plant species, with ponds, rock formations and green slopes. Free-roaming monkeys are often seen around dawn and dusk.\n\nWhat to Do: Walk the garden, picnic, take photos, watch for monkeys in the early morning or late afternoon, browse the plant nursery, and stop at the on-site cafés. The grounds are also used for weddings, corporate events and paint-and-sip sessions.",
                    "For Abuja residents it is one of the city's best-known green spaces — a place for picnics, walks and photoshoots away from traffic. Hotels.ng says it was started by the wife of a former head of state.",
                    "Best Access: By cab or ride-hail to Plot 3703–3704, Ibrahim Babangida Boulevard, Maitama (some listings give Plot 3259).",
                    "Hours: Reported as daily from 8 or 9 AM to 6 PM — confirm before you go.\n\nEntry: Reported as ₦2,000 per person (2025); older listings show less.\n\nContact: Bookings and enquiries on 0913 714 2755; Instagram @sariuspalmetum; website sariuspalmetum.com.\n\nBring: A picnic mat or blanket, water and a camera.\n\nEvents: Event and photoshoot use is booked with the garden — ask about fees.",
                    "Monkeys: Don't feed them, and keep food and bags closed.\n\nHeat: Large open areas — bring water and a hat in the dry season.\n\nPrices & Hours: Sources disagree — confirm with the garden.\n\nEvents: Areas may be closed for private events.",
                    "Entry: About ₦2,000 per person.\nCafé Food & Drinks: Extra.\nPhotoshoots & Events: Fees set by the garden — ask in advance.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Millennium Park: Abuja's large public park, also in Maitama.",
                        "Paint and Pottery Social: Pottery and painting studio inside the garden."
                    },
                    "Early morning or late afternoon, when it's cooler and the monkeys are most active; the dry season (roughly November to March) is most comfortable.",
                    "Low to Medium (4/10) — busier at weekends and during events.",
                    "Rating: 5/10 — large grounds with slopes and unpaved paths.",
                    "2 to 3 hours.")),

            // Kahera Country Club — no founder input; researched. Sources: kaheracountryclub.com,
            // sabiabuja.com/kahera-country-club-abuja/ (Nov 2024), wakaabuja.com/top-activities-at-kahera-country-club-abuja/
            // (Nov 2026), businessday.ng (Oct 2025 interview with Kechi Ibe, founder of Kahera Global).
            // Coordinates: APPROXIMATE — the club's listed address is "opposite FIRS, Katampe Extension"
            // (Trip.com, via search result); this is the OSM point for FIRS Katampe Extension (9.1222545, 7.4337921).
            // AvgCost: ~US$30 ≈ ₦41,000 at ~₦1,360 per US$ (Nov 2026) — day pass, a meal and a ride.
            new("abuja", KaheraName, Nigeria, Abuja, AbujaLangs, 9.1223, 7.4338,
                30m, "Car", 2,
                new[] { "activity_experience", "food_experience", "entertainment_nightlife" },
                new[] { "family_friendly", "social", "group_friendly", "couple_friendly", "music" },
                new[] { "Kahera", "Kahera Country Club Abuja", "Kahera Club", "KCC Abuja", "Kahera Country Club Katampe" }, "Exterior",
                Desc(
                    "Kahera Country Club is a members' and day-visit leisure club in Katampe Extension, Abuja, run under Kahera Global, the luxury group founded by Kechi Ibe. The club's site describes it as eco-friendly and about 80% solar-powered, with gardens and a restaurant and grill. Listed facilities include a picnic area and private huts, badminton and lawn tennis, a kids' playground, a maze, a petting zoo, VR experiences, an outdoor fitness area and a garden event space. A swimming pool and mini golf were listed as \"coming 2024\" — confirm they're open.\n\nWhat to Do: Spend a day with a day pass, eat at the restaurant, play tennis or badminton, take children to the playground and petting zoo, or come for a themed night: salsa on Wednesdays, movie night on Thursdays, karaoke on Fridays and a live band on Sundays. The gardens are also hired for weddings, birthdays and team retreats.",
                    "The club pitches itself as one of Abuja's first luxury country clubs — a green, family-friendly space for weekends, celebrations and weekly social nights.",
                    "Address: 1003 Patrick Yakowa Street, Katampe Extension, Abuja (listed as opposite FIRS).\n\nBest Access: By car, cab or ride-hail.",
                    "Access: Individual, family and corporate memberships are offered; access is by membership and reservation. Daily passes have been reported at about ₦15,000, including a drink, an appetiser and use of the amenities — confirm current terms.\n\nHours: Sources differ (for example 10 AM–10 PM daily, or opening later on some weekdays) — confirm with the club.\n\nContact: Phone 0816 309 9029; Instagram @kaheracountryclub; website kaheracountryclub.com.\n\nCCTV: The club notes that guests' activities may be recorded.",
                    "Book Ahead: Access is controlled through membership and reservations — don't just turn up.\n\nFacilities: Some amenities (pool, mini golf, indoor gaming) were announced as coming soon — confirm what's open.\n\nPrices & Hours: Published figures are few and vary between sources — confirm before you go.",
                    "Daily Pass: About ₦15,000 (with a drink and an appetiser; reported).\nMembership: Prices not published — ask the club.\nFood & Drinks: Extra.\nSwim Classes & Event Hire: Extra — ask the club.",
                    new[]
                    {
                        "Jabi Lake: Lakeside walks and Jabi Lake Mall, south of Katampe."
                    },
                    "Year-round; come on a themed night (Wednesday to Friday, or Sunday) for the social scene, or a weekend daytime for families.",
                    "Medium (5/10) — busier on themed nights, weekends and event days.",
                    "Rating: 6/10 — landscaped grounds with lawns and paths; confirm step-free access with the club.",
                    "Half a day, or an evening for a themed night.")),


            // ═══ Batch: Benin City entertainment ═══
            // ─── Benin City — entertainment centres, cinemas, bowling ─────────────
            // Space VR Benin — founder listed "Space Ur"; almost certainly Space VR ("VR" misread as "Ur"), confirm.
            // Founder: "I believe all locations have an arcade" (hedged).
            // Sources: instagram.com/spacevrbenin ("Space Virtual Reality", Okhoro Junction, weekdays 10am–7pm,
            // weekends 12pm–7pm, online booking — via search summary); tiktok.com/discover/places-to-play-games-in-benin-city
            // (113 New Lagos Road opp. Okhoro Junction, open daily, on Google Maps/Bolt — via search summary);
            // maroorode.com/portfolio-details/space-vr-benin (booking site, VR and non-VR games, +234 811 294 9474 — via search summary).
            // Coordinates: APPROXIMATE — OSM Nominatim point for Okhoro Junction, New Lagos Road (6.3578973, 5.6226375).
            // AvgCost: ~US$10 ≈ ₦13,600 at ~₦1,360 per US$ (Nov 2026) — a few games and a drink; prices unpublished, guess.
            new("benin-city", SpaceVrName, Nigeria, BeninCity, EdoLangs, 6.3579, 5.6226,
                10m, "Car", 2,
                new[] { "activity_experience", "entertainment_nightlife" },
                new[] { "adventurous", "group_friendly", "social", "quirky", "hidden_gem", "family_friendly" },
                new[] { "Space VR", "SpaceVR Benin", "Space Virtual Reality", "Space VR Okhoro", "Space Ur", "Space UR Benin" }, "Interior",
                Desc(
                    "Space VR Benin (Space Virtual Reality on Instagram) is a virtual-reality gaming centre by Okhoro Junction on New Lagos Road, Benin City. It promotes itself as the city's top VR spot and runs a booking site listing both VR and non-VR games.\n\nWhat to Do: Play headset-based VR games, alone or in a group; it reportedly also has non-VR and arcade games — confirm.",
                    "A newer kind of hangout in Benin City — friends, couples and older kids come for a couple of hours of VR games as an alternative to the cinema or a lounge.",
                    "Address: 113 New Lagos Road, opposite Okhoro Junction, Benin City (per listings). It is listed on Google Maps and Bolt as \"Space VR Benin\" — confirm the address before you go.\n\nGetting There: A Bolt or taxi is easiest; tell the driver Okhoro Junction, New Lagos Road.",
                    "Booking: Book online through the venue's booking site (link on Instagram @spacevrbenin) or call +234 811 294 9474 — confirm the number on Instagram first.\n\nHours (per Instagram): Weekdays 10:00 AM–7:00 PM, weekends 12:00 PM–7:00 PM; one listing says it is open every day — confirm.\n\nPrices: Not published — ask before you play.\n\nArcade: Reportedly has an arcade alongside the VR — confirm.\n\nAge Limits: Not published; ask about minimum ages for headsets.",
                    "Unverified Details: Online information is thin and mostly from the venue's own social media — confirm the address, hours and prices before going.\n\nMotion Sickness: VR can cause dizziness or nausea; take breaks.\n\nBusy Junction: Okhoro Junction is a busy roadside spot — take care getting in and out of cars.",
                    "Games: Not published — confirm the price per game or session.\nTransport: Bolt or taxi fare extra.",
                    new[]
                    {
                        "DeepEnd Cinemas & Fun House: Cinema, haunted house and game zone off TV Road.",
                        "Kada Plaza: Benin City's long-running cinema-and-arcade complex on Sapele Road.",
                        "Benin National Museum: Benin bronzes and history at King's Square."
                    },
                    "Year-round (indoors); weekday afternoons are likely quietest.",
                    "Low to Medium (4/10) — busier at weekends and in holidays.",
                    "Rating: 6/10 — ground-level access unconfirmed; VR headsets need standing and moving — ask the venue about seated options.",
                    "1 to 2 hours.")),

            // Jordan's Playworld — founder-supplied name/area ("Aruogba" spelling confirmed by the mall's Facebook page).
            // Founder: "I believe all locations have an arcade" (hedged). LOW CONFIDENCE — single online source.
            // Sources: tiktok.com/discover/cool-places-to-buy-kids-stuf-in-benin-city (kids' play venue in Lifestyle Value Mall,
            // 1 Kenn Ayere Street, Aruogba, off Irhirhi, Airport Road; open 7 days — via search summary, page updated 2025-09-15);
            // facebook.com/LifestyleValueMall ("Aruogba Community, off Airport Road"; "Ayere Street, Off Irhirhi Road, Iyekogba" — via search summary);
            // theeagleonline.com.ng (Kenn Ayere's Lifestyle Value Mall, inauguration planned 17 Sep 2024, "Ofumwengbe community").
            // Coordinates: VERY APPROXIMATE — OSM Nominatim point on Irhirhi Road, Egbiri (6.2577613, 5.5802017); mall not mapped.
            // AvgCost: ~US$8 ≈ ₦11,000 at ~₦1,360 per US$ (Nov 2026) — a child's play entry and snacks; prices unpublished, guess.
            new("benin-city", JordansPlayworldName, Nigeria, BeninCity, EdoLangs, 6.2578, 5.5802,
                8m, "Car", 1,
                new[] { "activity_experience" },
                new[] { "family_friendly", "local_favorite", "budget_friendly", "shopping", "hidden_gem" },
                new[] { "Jordans Playworld", "Jordan's Play World", "Jordan Playworld", "Jordan's Playworld Aruogba", "Jordan's Playworld Lifestyle Value Mall" }, "Interior",
                Desc(
                    "Jordan's Playworld is a children's play centre inside Lifestyle Value Mall in Aruogba, off Irhirhi Road near Airport Road, Benin City. The mall, owned by businessman Kenn Ayere, was due to be inaugurated in September 2024.\n\nWhat to Do: Indoor play for children; it reportedly has an arcade — confirm.",
                    "A newer family stop on the Airport Road side of Benin City — parents can combine the kids' play time with shopping at the mall.",
                    "Address: 1 Kenn Ayere Street, Aruogba, off Irhirhi Road, Airport Road, Benin City (inside Lifestyle Value Mall). One news report places the mall in Ofumwengbe community — confirm the location before you go.\n\nGetting There: Take a Bolt or taxi to Lifestyle Value Mall, Aruogba.",
                    "Hours: Reportedly open seven days a week — confirm hours.\n\nPrices: Not published — confirm entry price per child.\n\nBooking: No booking channel found; ask at the mall for parties and group visits.\n\nRules: Age limits and supervision rules are not published — expect an adult to stay with young children; confirm.\n\nArcade: Reportedly has an arcade — confirm.",
                    "Unverified Details: Very little is published online — confirm it is open, its hours and its prices before you travel.\n\nSupervision: Keep young children in sight at all times.",
                    "Play Entry: Not published — confirm.\nArcade Games: Extra if available — confirm.\nSnacks & Drinks: Extra.",
                    new[]
                    {
                        "Kada Plaza: Cinema, arcade and restaurants on Sapele Road.",
                        "Royal Palace of the Oba of Benin: Seat of the Benin monarchy, in central Benin City.",
                        "Benin National Museum: Benin bronzes and history at King's Square."
                    },
                    "Year-round (indoors); weekends and school holidays are busiest.",
                    "Low to Medium (4/10) — busier at weekends and in school holidays.",
                    "Rating: 6/10 — inside a mall; step-free access not confirmed.",
                    "1 to 3 hours.")),

            // Kada Plaza (incl. Kada Cinemas) — one destination: same address, phones and complex as "Kada Cinema".
            // Founder: "I believe all locations have an arcade" (the arcade is confirmed by several sources).
            // Sources: granvilleblog.blogspot.com/2013/11 (opened 11 Nov 2013, 111 Sapele Road; children's arcade, bumper,
            // cinemas, Chinese restaurant); afktravel.com/restaurant/elliots-sports-bar (2014: multiplex, Kung Fu's, indoor
            // arcade, Jumps bumper karts, Elliot's); hotels.ng/places/mall/3413-kada-plaza; evendo.com (111 Old Sapele Rd, Oka,
            // +234 805 501 0676, parking); locatecinemas.com; threads.com/@kadacinemas (active Nov 2026, book via bio link,
            // 08055010626/08055010676; Mon/Tue ₦6,000-for-two promo and Playtime Arcade — via search summary);
            // hours via search summary of wanderlog.com (9am–11pm) vs Instagram listing (8am–10pm).
            // Coordinates: APPROXIMATE — Google Maps link via trippin.world/evendo.com (6.3154907, 5.6280627).
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — ticket, popcorn, arcade credit and a meal.
            new("benin-city", KadaPlazaName, Nigeria, BeninCity, EdoLangs, 6.3155, 5.6281,
                15m, "Car", 1,
                new[] { "entertainment_nightlife", "activity_experience", "food_experience" },
                new[] { "family_friendly", "local_favorite", "social", "group_friendly", "couple_friendly" },
                new[] { "Kada Cinemas", "Kada Cinema", "Kada Plaza Cinemas", "Kada Plaza Cinema & Entertainment Centre", "Kada Entertainment Centre", "Kada Cinemas Benin", "Kada Plaza Benin" }, "Exterior",
                Desc(
                    "Kada Plaza is an entertainment complex on Sapele Road in Oka, Benin City, opened in November 2013. It is anchored by Kada Cinemas (current Hollywood and Nollywood releases) and also houses a children's arcade (Playtime Arcade), Kung Fu's Chinese restaurant, Elliot's Sports Bar with pool tables, cafés and beauty outlets. Older reviews also mention an indoor bumper-kart rink and 5D rides — confirm what is running now.",
                    "Kada Plaza has been one of Benin City's go-to spots for a film and a night out for over a decade — families come for the arcade and cinema, and friends for Elliot's on football days.",
                    "Address: Kada Plaza, 111 (Old) Sapele Road, Oka, Benin City.\n\nGetting There: Bolt or taxi; buses heading to Oka/Sapele Road drop nearby, and keke and okada are common. Parking is available on site. It is a short drive from Benin Airport (BNI) and King's Square.",
                    "Tickets: Book via the link in Kada Cinemas' Instagram/Threads bio (@kadacinemas) or at the box office; ticket lines 0805 501 0676 / 0805 501 0626.\n\nHours: Listed as 9:00 AM–11:00 PM daily (some listings say 8:00 AM–10:00 PM) — confirm.\n\nPrices: A reviewer reported tickets from about ₦4,000 and popcorn about ₦4,000; Kada has advertised a Monday–Tuesday deal of ₦6,000 for two tickets — confirm current prices.\n\nArcade: Children's arcade (Playtime Arcade) on site; the bumper karts and 5D experiences were mentioned in older sources — confirm.\n\nFood: Kung Fu's Chinese restaurant, Elliot's Sports Bar (burgers, pasta, grills; live Premier League) and cafés.",
                    "Weekend Crowds: Popular films sell out on weekend evenings — book ahead or arrive early.\n\nTraffic: Sapele Road gets congested at peak hours.\n\nOlder Details: Some facilities (bumper karts, 5D) are reported only in older reviews — check before promising them to kids.",
                    "Cinema Ticket: About ₦4,000 (reported); Mon–Tue promo ₦6,000 for two — confirm.\nPopcorn: About ₦4,000 (reported).\nArcade Games: Extra — pay per game/credit; prices not published.\nFood & Drinks: Extra (Elliot's mains have been reported from about ₦1,700 in 2014 — expect much higher now).",
                    new[]
                    {
                        "Benin National Museum: Benin bronzes and history at King's Square.",
                        "Royal Palace of the Oba of Benin: Seat of the Benin monarchy, in central Benin City.",
                        "Igun Street Bronze Casters: Benin City's historic bronze-casting street."
                    },
                    "Year-round; weekday afternoons are quietest, Friday–Sunday evenings and holidays busiest. Visit Monday or Tuesday for the two-ticket deal (confirm).",
                    "Medium to High (6/10) — busiest on weekend evenings, public holidays and big release weeks.",
                    "Rating: 6/10 — the cinema has been described as upstairs; ask about lifts or step-free access.",
                    "2 to 4 hours (a film plus food or the arcade).")),

            // DeepEnd Cinemas & Fun House — founder: Deep End has a cinema with a haunted house (confirmed by the official site).
            // Founder: "I believe all locations have an arcade" (official site lists a game zone with arcade).
            // Sources: deependentertainment.com (Haunted House Experience, Game Zone — console/arcade/VR, Movie Lounge, food &
            // drinks, hotel/event spaces; "DeepEnd Complex, GRA"); deependentertainment.com/contact.html (No. 10 Uwa Street,
            // Off TV Road; 08163008555); finelib.com/listing/DeepEnd-Cinemas/88996 (10 Nosayaba Street, opp. NTA, off TV Road;
            // 0807 743 3532; 9D/7D/5D games); chrischaris.art.blog/2020/01/22; via search summary: TikTok ("4K Naira" haunted house,
            // basketball, karaoke), worldorgs.com reviews (small affordable cinema, 5D arcade, "prices are high", limited parking),
            // 3 cinema halls + kids' mini cinema, Club Atlanta indoor pool.
            // Coordinates: APPROXIMATE — between OSM Nominatim Uwa Street (6.3521558, 5.6174481) and Nosayaba Street (6.3516197, 5.6169823).
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — haunted house (~₦4,000 reported), a film, games, food.
            new("benin-city", DeependName, Nigeria, BeninCity, EdoLangs, 6.3520, 5.6172,
                15m, "Car", 1,
                new[] { "entertainment_nightlife", "activity_experience" },
                new[] { "quirky", "adventurous", "group_friendly", "social", "couple_friendly", "local_favorite" },
                new[] { "Deep End", "Deepend", "DeepEnd Cinemas", "Deepend Cinemas", "Deepend Fun House", "Deepend Funhouse", "Deepend Entertainment", "Deepend Haunted House", "Depend Haunted House" }, "Interior",
                Desc(
                    "DeepEnd is an entertainment complex off TV Road (opposite NTA), Benin City, run by Deepend Entertainment. It combines DeepEnd Cinemas (listings mention three halls, including a small kids' cinema) with the Deepend Fun House: a walk-through haunted house, a game zone with console, arcade and VR set-ups, and 5D/7D/9D rides. There is also food and drinks on site, and the wider complex has a hotel, event spaces and a recording studio.",
                    "The haunted house has become a viral Benin City outing on TikTok — groups of friends and couples dare each other through it, then stay for games, karaoke or a film.",
                    "Address: No. 10 Uwa Street, off TV Road (opposite NTA), Benin City — older listings give 10 Nosayaba Street, the neighbouring street; the company also describes it as the DeepEnd Complex, GRA.\n\nGetting There: Bolt or taxi to TV Road/NTA Benin; reviewers say parking is limited.",
                    "Tickets: No online ticketing found — buy on site, or contact Deepend Entertainment on 0816 300 8555 or via Instagram @deependfunhouse (an older listing gives 0807 743 3532).\n\nHours: Not published — confirm.\n\nHaunted House: Creators describe it as a roughly ₦4,000 experience — confirm the current price.\n\nGames: Game zone with console, arcade and VR set-ups, plus 5D/7D/9D games.\n\nCinema: Films and private screenings; prices not published.\n\nOther Activities: Creators also mention basketball and karaoke.",
                    "Not for Everyone: The haunted house is designed to scare — not suited to young children, pregnant visitors or anyone with heart conditions; confirm any age limit.\n\nCosts Add Up: Each attraction is paid separately, and some reviewers found prices high.\n\nParking: Reviewers say parking is limited — consider a Bolt.",
                    "Haunted House: About ₦4,000 (reported) — confirm.\nCinema Ticket: Not published — confirm.\n5D/7D/9D Rides & Arcade: Extra per game — not published.\nFood & Drinks: Extra.",
                    new[]
                    {
                        "Space VR Benin: VR gaming by Okhoro Junction on New Lagos Road.",
                        "Benin National Museum: Benin bronzes and history at King's Square.",
                        "Royal Palace of the Oba of Benin: Seat of the Benin monarchy, in central Benin City."
                    },
                    "Year-round; evenings and weekends for the busiest atmosphere, Halloween season for the haunted house buzz.",
                    "Medium (5/10) — busier at weekends and holidays.",
                    "Rating: 5/10 — the haunted house is a dark walk-through maze; ask about step-free access to the cinema and game zone.",
                    "2 to 3 hours.")),

            // Vitto Lounge — founder: has a bowling alley ("bowling ring"); confirmed by the venue's Instagram.
            // Founder: "I believe all locations have an arcade" (hedged; reviews mention games).
            // Sources: instagram.com/vitto_lounge (bio: restaurant, lounge, rooftop, bowling alley, Mon–Sun 12pm till late,
            // reservations, WhatsApp 07078354122); ranked.ng/place/vitto-lounge-iuvp30 (1 Osaro Iwinosa Avenue, off Ugbor-Amagba
            // Road, 300102; 0707 835 4122; Google hours; 4.2★/33 reviews; review themes); tiktok.com/@ekithe7th/video/7450580891937656070
            // (Dec 2024, Ugbor-Amagba Road); via search summary: launched Dec 2024, bowling "first of its kind in Benin City"
            // (venue's own claim), alternative phone 0703 466 3234.
            // Coordinates: APPROXIMATE — ranked.ng listing (6.2502843, 5.6031362).
            // AvgCost: ~US$20 ≈ ₦27,000 at ~₦1,360 per US$ (Nov 2026) — bowling plus food and drinks; prices unpublished, guess.
            new("benin-city", VittoName, Nigeria, BeninCity, EdoLangs, 6.2503, 5.6031,
                20m, "Car", 2,
                new[] { "entertainment_nightlife", "food_experience", "activity_experience" },
                new[] { "nightlife", "social", "music", "couple_friendly", "group_friendly", "food_focused" },
                new[] { "The Vitto Lounge", "Vitto", "Vito Lounge", "Vitto Lounge Benin", "Vitto Bowling" }, "Exterior",
                Desc(
                    "Vitto Lounge is a restaurant, lounge and club off Ugbor-Amagba Road in Benin City, opened in December 2024. It has a rooftop area and a bowling alley, which the venue bills as the first of its kind in Benin City. It serves Nigerian and continental food and cocktails, with live music, themed nights and Euphoria Sundays.\n\nWhat to Do: Bowl a few frames, eat, then stay for cocktails and music; reviewers also mention games — it reportedly has an arcade, confirm.",
                    "A newer, design-led night-out spot in the GRA/Ugbor side of the city — locals praise the ambience, live band and photo spots, and the bowling makes it a popular group and date outing.",
                    "Address: 1 Osaro Iwinosa Avenue, off Ugbor-Amagba Road, Benin City.\n\nGetting There: Bolt or taxi; for late nights, pre-arrange your ride home.",
                    "Reservations: Book via Instagram @vitto_lounge or WhatsApp 0707 835 4122 (another listing gives 0703 466 3234 — confirm).\n\nHours: Instagram says Mon–Sun 12:00 PM till late; Google-listing hours are Mon–Thu 10:00 AM–12:00 AM, Fri 10:00 AM–5:00 AM, Sat 10:00 AM–3:00 AM, Sun 12:00 PM–12:00 AM — confirm.\n\nBowling: Bowling alley on site; price per game or lane not published — ask when you book.\n\nPromotions: Reviewers mention a Tuesday cocktail promo, and the venue runs Euphoria Sunday events.\n\nAge Policy: Late-night club hours — confirm age policy and any dress code.",
                    "Late Nights: It runs into the early hours at weekends — plan safe transport home.\n\nService: A few reviewers mention slow or rude service and a tough dish; most praise the ambience.\n\nPrices Unpublished: Ask for bowling and menu prices before ordering.",
                    "Bowling: Not published — confirm per game/lane.\nFood & Cocktails: Extra — reviewers call it affordable, but no menu prices were found.\nEvents: Themed nights may carry a cover charge — confirm.\nTransport: Bolt or taxi fare extra, more after midnight.",
                    new[]
                    {
                        "Kada Plaza: Cinema, arcade and restaurants on Sapele Road.",
                        "Genesis Cinemas Benin City: Four-screen cinema at Benin City Mall on Sapele Road.",
                        "Royal Palace of the Oba of Benin: Seat of the Benin monarchy, in central Benin City."
                    },
                    "Year-round; afternoons for bowling and food, Friday–Sunday nights for music and the social scene.",
                    "Medium to High (6/10) — busiest on Friday and Saturday nights and event nights.",
                    "Rating: 5/10 — rooftop and multi-level areas may involve stairs; ask about step-free access to the bowling alley.",
                    "2 to 4 hours, or a full evening.")),

            // Genesis Cinemas Benin City — Genesis Deluxe Cinema (GDC) Benin City.
            // Founder: "I believe all locations have an arcade" (hedged; no source mentions one).
            // Sources: genesiscinemas.com/gdc-benin-city (showtimes, online tickets and concessions); partyjolloftv.com/cinemas/
            // genesis-deluxe-cinema-benin (4 screens, Oka, 0703 093 3380, food court, parking); vanguardngr.com/2026/02 (2025 revenue
            // ₦435,216,000, four theatres); tiktok.com/@explorewith_esther/video/7347717832894975238 ("inside shoprite mall, Sapele road");
            // getoccupi.com/malls/benin-city-mall (Sapele Road, Shoprite + cinema anchors, mall hours, map point).
            // Coordinates: APPROXIMATE — Benin City Mall per getoccupi.com maps links (6.27707, 5.6337); not confirmed on an official map.
            // AvgCost: ~US$10 ≈ ₦13,600 at ~₦1,360 per US$ (Nov 2026) — a ticket (~₦5,000–6,000, unconfirmed) and snacks.
            new("benin-city", GenesisBeninName, Nigeria, BeninCity, EdoLangs, 6.2771, 5.6337,
                10m, "Car", 1,
                new[] { "entertainment_nightlife" },
                new[] { "couple_friendly", "family_friendly", "social", "group_friendly", "shopping" },
                new[] { "Genesis Cinemas Benin", "Genesis Cinema Benin", "Genesis Deluxe Cinema Benin", "GDC Benin City", "Genesis Benin City", "Genesis Cinema Shoprite Benin", "Genesis Cinema Benin City Mall" }, "Interior",
                Desc(
                    "Genesis Deluxe Cinemas' Benin City branch is a four-screen cinema showing Hollywood and Nollywood releases. Local creators place it inside the Shoprite-anchored Benin City Mall on Sapele Road. It took about ₦435 million at the box office in 2025, making it one of Nigeria's ten highest-grossing cinemas.\n\nWhat to Do: Catch a new release, then shop or eat in the mall; it reportedly has an arcade — confirm.",
                    "A newer, modern cinema on the south side of the city — local reviewers call it affordable and easy to reach, and it pairs well with a Shoprite run.",
                    "Address: Benin City Mall (Shoprite), Sapele Road, Benin City — one listing gives Oka, Benin City; confirm on Google Maps.\n\nGetting There: Bolt or taxi to Shoprite / Benin City Mall, Sapele Road; parking is listed.",
                    "Tickets: Buy online at genesiscinemas.com (choose GDC Benin City) or at the box office; concessions can also be ordered online. Phone 0703 093 3380 (per a listing).\n\nShowtimes: Typically late morning to about 10:30 PM — check the day's schedule online.\n\nPrices: Benin City ticket prices weren't published — check when booking.\n\nMall Hours: Benin City Mall is listed as Mon–Thu 10:00 AM–9:00 PM, Fri–Sat 10:00 AM–10:00 PM, Sun 11:00 AM–6:00 PM.\n\nArcade: Reportedly has an arcade — confirm.",
                    "Location Confusion: Listings disagree (Sapele Road mall vs Oka) — confirm the address before you set off.\n\nPopular Releases: Weekend evenings and big releases sell out — book online.\n\nSnack Costs: Concessions add up quickly.",
                    "Ticket: Not published for Benin City — check genesiscinemas.com.\nSnacks & Drinks: Extra.\nArcade: Extra if available — confirm.",
                    new[]
                    {
                        "Kada Plaza: Older cinema-and-arcade complex further up Sapele Road.",
                        "Vitto Lounge: Bowling, food and cocktails off Ugbor-Amagba Road.",
                        "Benin National Museum: Benin bronzes and history at King's Square."
                    },
                    "Year-round; weekday daytime shows are quietest.",
                    "Medium (5/10) — busiest on weekend evenings and holiday releases.",
                    "Rating: 7/10 — inside a modern mall; confirm lifts and wheelchair seating with the cinema.",
                    "2 to 3 hours.")),

            // Filmhouse Cinemas Benin — Voen Mall, opposite UNIBEN main gate, Ugbowo.
            // Founder: "I believe all locations have an arcade" (hedged; no source mentions one).
            // Sources: moviebuff.com/filmhouse-cinemas-voen-mall (Voen Mall, opp. UNIBEN, Ugbowo; map 6.399573, 5.596659);
            // chrischaris.art.blog/2020/01/22 (near UNIBEN main gate / Precious Palm Royal; 2020 prices); firstnigerian.com/
            // filmhouse-cinema-locations-contacts-tickets-movies (2026 prices, 0907 036 2350, website/app/walk-in);
            // cinephiles.ng/cinema/filmhouse-cinema-benin-benin-city (book at filmhouseng.com; sample showtimes 11:00–20:25);
            // unibengist.com/movies-currently-availalbe-at-filmhouse-3 (Voen Mall, 2017).
            // Coordinates: APPROXIMATE — moviebuff.com map link (6.399573, 5.596659).
            // AvgCost: ~US$10 ≈ ₦13,600 at ~₦1,360 per US$ (Nov 2026) — a ₦5,000–6,000 ticket and snacks.
            new("benin-city", FilmhouseBeninName, Nigeria, BeninCity, EdoLangs, 6.3996, 5.5967,
                10m, "Car", 1,
                new[] { "entertainment_nightlife" },
                new[] { "couple_friendly", "social", "budget_friendly", "group_friendly", "family_friendly" },
                new[] { "Filmhouse Benin", "Filmhouse Cinema Benin", "Filmhouse Voen Mall", "Filmhouse Ugbowo", "FilmHouse Benin City", "Filmhouse Uniben" }, "Interior",
                Desc(
                    "Filmhouse Cinemas Benin is the Benin City branch of Nigeria's Filmhouse chain, in Voen Mall opposite the University of Benin (UNIBEN) main gate in Ugbowo, beside the Precious Palm Royal hotel. It shows current Hollywood and Nollywood releases, including 3D screenings.\n\nWhat to Do: Watch a new release; it reportedly has an arcade — confirm.",
                    "The students' cinema — popular with UNIBEN students and the Ugbowo crowd, especially on Wednesday discount days.",
                    "Address: Voen Mall, opposite UNIBEN main gate, Ugbowo (Benin–Lagos Road, Uselu area), Benin City.\n\nGetting There: Bolt or taxi to UNIBEN main gate, Ugbowo; it is on the northern side of the city, away from the centre.",
                    "Tickets: Book on filmhouseng.com or the Filmhouse app, or buy at the box office. Phone 0907 036 2350 (per a listing).\n\nPrices (2026 guide): Standard ₦5,000; Blockbuster Premium 2D ₦5,500; 3D Premium ₦6,000; Students/Corpers Wednesday ₦3,000 — confirm when booking.\n\nShowtimes: Typically from about 11:00 AM into the evening — check the day's schedule.\n\nScreens: Sources disagree (one to four screens).\n\nArcade: Reportedly has an arcade — confirm.",
                    "Student Rush: Wednesday discount days and new releases can be crowded.\n\nTraffic: The Ugbowo/Lagos Road stretch near UNIBEN is often congested.\n\nPrices Change: Ticket prices have risen sharply since 2020 — check before you go.",
                    "Ticket: ₦5,000–₦6,000 (2026 guide); students/corpers ₦3,000 on Wednesdays.\nSnacks & Drinks: Extra.\nTransport: Bolt or taxi fare from the city centre extra.",
                    new[]
                    {
                        "University of Benin (Ugbowo campus): Directly across the road.",
                        "Space VR Benin: VR gaming by Okhoro Junction on New Lagos Road.",
                        "Benin National Museum: Benin bronzes and history at King's Square."
                    },
                    "Year-round; Wednesdays for student/corper discounts, weekday daytime for quieter screenings.",
                    "Medium (5/10) — busier on Wednesdays, weekends and big releases.",
                    "Rating: 6/10 — mall cinema; confirm lifts and step-free access.",
                    "2 to 3 hours.")),

            // ═══ Batch: Benin City activities ═══
            // ─── Benin City (Edo State) ────────────────────────────────────────
            // Amazing Target Zone — founder's first-hand facts (archery, axe throwing, airsoft; trainers;
            // prices from ₦10,000) used as given. Sources: Google Search/Maps listing (address, phone 0816 234 8933,
            // hours, owner description), Instagram @amazing_targetzone (bio), creator post by ekithe7th (opened
            // 7 Aug 2026), TikTok tiktok.com/discover/amazing-target-zone-benin-city (brand @amazingtargetzone_ and
            // creator posts: three sections, guided, ~₦10,000).
            // Coordinates: Google Maps place pin (6.2679371, 5.5815576) for 120 Irhirhi Rd, Oka.
            // AvgCost: ~US$18 ≈ ₦25,000 at ~₦1,360 per US$ (Nov 2026) — one or two activities and a ride; rough.
            new("benin-city", AtzName, Nigeria, BeninCity, EdoLangs, 6.2679, 5.5816,
                18m, "Car", 1,
                new[] { "activity_experience" },
                new[] { "adventurous", "social", "group_friendly", "couple_friendly", "quirky", "local_favorite" },
                new[] { "ATZ", "ATZ Benin", "Amazing Targetzone", "Amazing Target Zone Benin City", "Amazing Target Zone (ATZ)", "Target Zone Benin" }, "Interior",
                Desc(
                    "Amazing Target Zone (ATZ) is a target-sports venue at 120 Irhirhi Road, off Airport Road, in Benin City. It opened in August 2026 and calls itself the city's first destination for archery, axe throwing and airsoft shooting. Trainers guide you through each activity, so you don't need any experience.\n\nWhat to Do: Archery, axe throwing (the venue calls its lane the \"axe vault\") and an airsoft shooting range. Creators describe a calm, well-designed space with three sections, good for friends, dates, groups and celebrations.",
                    "Benin City's nightlife is mostly restaurants, lounges and parties, so ATZ quickly became a talked-about new hangout with local content creators — an active, different kind of outing for friends and couples.",
                    "Address: 120 Irhirhi Road (Pink Cowrie Plaza), off Airport Road, Oka, Benin City. Spellings vary (Iriri / Ireri Road).\n\nGetting There: Take a cab or ride-hail from anywhere in Benin City to the plaza off Airport Road. From Benin Airport (BNI), it's a short ride along Airport Road.",
                    "Activities: Archery, axe throwing and airsoft shooting, with trainers on hand.\n\nPrices: From ₦10,000 — confirm the price of each activity when you book.\n\nHours (Google listing): Monday 9:00 AM–8:30 PM; Tuesday–Thursday and Saturday 9:00 AM–9:00 PM; Friday 9:30 AM–9:00 PM; Sunday 3:00–9:00 PM — confirm before you go.\n\nBooking & Contact: Call 0816 234 8933, or message Instagram @amazing_targetzone (TikTok @amazingtargetzone_).\n\nAge Limits: Not published — ask before bringing children.",
                    "Follow the Trainers: Listen to the safety briefing, and only throw or shoot when the trainer tells you to.\n\nAge & Gear Rules: Minimum ages and protective gear rules aren't published — ask, especially for children and the airsoft range.\n\nHard to Find: The road name is spelt several ways — search \"Amazing target zone\" on Google Maps or call ahead.\n\nNew Venue: Open since August 2026, with few reviews so far — confirm hours and prices before you go.",
                    "Activities: From ₦10,000 (founder); confirm the price of each activity.\nExtra Rounds: Likely charged separately — ask.\nRide-hail within Benin City: Extra.",
                    new[]
                    {
                        "Royal Palace of the Oba of Benin: Seat of the Benin monarchy, in central Benin City.",
                        "Benin National Museum: Benin bronzes and history at King's Square.",
                        "Igun Street Bronze Casters: Benin City's historic bronze-casting street."
                    },
                    "Year-round (indoor). Weekday evenings and Sunday afternoons are usually quieter; Google shows Tuesday evenings and Saturday mornings as busier.",
                    "Low to Medium (4/10) — busier on weekends and during group bookings.",
                    "Rating: 6/10 — an indoor venue in a plaza; confirm step-free access with the venue.",
                    "1 to 2 hours.")),

            // ═══ Batch: Benin City food & nightlife ═══
            // ─── Benin City (Edo State) — food, drink & nightlife ──────────────
            // Mat-Ice — founder-nominated; researched. Sources: maticefastfood.com (history 1994/1999/2000/2003,
            // 11–15 outlets, ice cream "from ₦700", phone 0805 348 0420, IG @maticebenin, order link matice.web.app),
            // finelib.com/listing/MatIce/49064 (52 Ihama Rd GRA; 99 Ekenwan Rd; 167b Uselu-Lagos Rd; 9am–9pm daily;
            // 0816 338 5534 / 0906 685 0363), finelib.com/listing/MatIce-Fast-Food/71370 (107 Ekenwan Rd),
            // trippin.world/spot/mat-ice ("best ice cream in Benin City" quote), tiktok.com/@explore.benincity/video/7414564611900460293
            // (GRA branch on Lucky Igbinedion Way; under ₦5,000 per head), vymaps reviews 2017–2019 (via search snippet).
            // Coordinates: APPROXIMATE — trippin.world point for the Ihama Road branch (6.309443, 5.616341).
            // AvgCost: ~US$6 ≈ ₦8,000 at ~₦1,360 per US$ (Nov 2026) — ice cream or a snack meal plus a short ride.
            new("benin-city", "Mat-Ice Fast Food", Nigeria, BeninCity, EdoLangs, 6.3094, 5.6163,
                6m, "Car", 1,
                new[] { "food_experience" },
                new[] { "food_focused", "local_favorite", "budget_friendly", "family_friendly", "good_short_visit" },
                new[] { "Mat-Ice", "Mat Ice", "MatIce", "Matice", "Mat-Ice Benin", "Mat Ice Ice Cream", "Mat-Ice Fast Foods" }, "Exterior",
                Desc(
                    "Mat-Ice is a Benin City fast-food chain best known for its soft-serve ice cream. By the company's own account it began in 1994 as an ice-cream kiosk on Ekenwan Road, was incorporated in 1999, added fast food in 2000 and Nigerian meals in 2003, and now runs somewhere between 11 and 15 outlets across the city.\n\nWhat to Order: Soft-serve ice cream is the signature. The menu also covers burgers and snacks, pastries and cakes, drinks, and Nigerian meals (\"Naija Proper\").",
                    "Many Benin residents grew up on it. Reviewers have called it the best ice cream in Benin City, and local food creators describe it as the city's most consistent and budget-friendly fast-food spot.",
                    "Branches (listed; confirm which are open): Ihama Road, GRA (No. 52, off Airport Road); Ekenwan Road (the original area); Uselu–Lagos Road; and Lucky Igbinedion Way, GRA (before Morzi Hotel, Ugbor Village Road).\n\nGetting There: Take a cab or ride-hail to the nearest branch.",
                    "Prices: Ice cream from about ₦700 (company website). Expect under ₦5,000 per head for a meal.\n\nHours: Listed as 9:00 AM–9:00 PM daily (older directory listing). Confirm with the branch.\n\nOrdering: Order online through the link on maticefastfood.com, or call 0805 348 0420. Instagram: @maticebenin.\n\nDine-in & Takeaway: Both are available at most branches.",
                    "Branch Differences: Menus, hours and ice-cream availability can vary by outlet, so call ahead if you're going for ice cream specifically.\n\nOld Reviews: Many online reviews date from 2017–2019. Check recent posts on Instagram.",
                    "Ice Cream: From about ₦700.\nMeals: Under ₦5,000 per head (local creator estimate).\nDelivery: Extra. Fees depend on the platform or rider.\nRide-hail within Benin City: Extra.",
                    new[]
                    {
                        "Benin National Museum: Benin bronzes and history at King's Square.",
                        "Royal Palace of the Oba of Benin: Seat of the Benin monarchy, in the city centre."
                    },
                    "Year-round; ice cream is best on a hot afternoon.",
                    "Medium (5/10) — busier at lunchtime, after school and at weekends.",
                    "Rating: 7/10 — street-level fast-food outlets; step-free access varies by branch.",
                    "30 minutes to 1 hour.")),

            // Benin City leaf rice (izoko) — founder's list (Madam 2, Madam 12 O'Clock, Mama Ebo, Mama Ivie), combined.
            // Dish sources: cookpad.com/eng/recipes/5658669 (Foodiescene/Pat's Kitchen, Benin, Aug 2018 — "IZOKO and
            // Paper rice", hawked on trays round the stew pot, same leaf as Ofada), ibiene.com/culture/5-meals-to-try-if-you-
            // happen-to-be-between-delta-and-edo-states/ (Jun 2017 — rice wrapped in green leaves, spicy tomato stew with beef
            // and intestines, "not necessarily an indigenous Edo meal", on almost every Benin menu),
            // allnigerianrecipes.com/stews/pepper-rice-stew/.
            // Mama Ebo: ranked.ng/place/mama-ebo-pepper-rice-z1bp1c (88 Airport Rd; 0916 237 7304; Mon–Sat 10–6; 4.0 from ~298
            // reviews; "white rice served on a bed of leaves"), zeperoni.com/listing/mama-ebo-pepper-rice/ (73 Airport Rd; IG
            // mama_ebo_benin; menu), trippin.world/spot/mama-ebo-pepper-rice (6.3229, 5.6113), tiktok.com/@explore.benincity/video/
            // 7434177904285584696 (under ₦5,000/head; sells out fast; slow service; spacious, child-friendly).
            // Mama 12: chowdeck.com/blog/chowdeck-delivers-food-from-restaurants-in-asaba-benin-city-and-abeokuta ("Mama 12 Pepper
            // Rice" on Chowdeck, c. Nov 2023), tiktok.com/@chikethecreator_/video/7426433931819404549. No address found.
            // "Madam 2 o'clock": a single TikTok post (via search snippet) — izoko seller 20+ years, behind Image Garden, Boundary
            // Rd; Mon–Sat; arrive by 2–3 PM. Mama Ivie: founder's local knowledge only.
            // Coordinates: Mama Ebo, ranked.ng/Google point (6.322819, 5.611277), used as the anchor.
            // AvgCost: ~US$6 ≈ ₦8,000 at ~₦1,360 per US$ (Nov 2026) — a leaf-rice plate with protein plus a ride.
            new("benin-city", "Benin City Leaf Rice (Izoko) Spots", Nigeria, BeninCity, EdoLangs, 6.3228, 5.6113,
                6m, "Car", 1,
                new[] { "food_experience" },
                new[] { "food_focused", "local_favorite", "budget_friendly", "hidden_gem", "good_short_visit" },
                new[] { "Leaf Rice Benin", "Benin Leaf Rice", "Izoko", "Izoko Rice", "Paper Rice", "Benin Pepper Rice", "Pepper Rice Benin City", "Mama Ebo Pepper Rice", "Mama Ebo", "Mama 12 Pepper Rice", "Mama 12", "Madam 12 O'Clock", "Madam 2 O'Clock", "Madam 2", "Mama Ivie" }, "Interior",
                Desc(
                    "Leaf rice is one of Benin City's signature meals. Locally it is called izoko, \"paper rice\" or simply \"pepper rice\". Plain boiled white rice is wrapped in cleaned green leaves and rested so it picks up the leaves' aroma. It is served with a fiery tomato-and-pepper stew, usually with beef, tripe (shaki) and cow skin (ponmo), and sometimes goat, chicken or fish. It was traditionally hawked on trays, with the stew pot in the middle and the wrapped rice around it, and is now on almost every local menu. One food writer notes it may not be an indigenous Edo dish, but it has become one of the city's most-eaten meals.\n\nWhere to Eat It: Four spots locals recommend:\n• Mama Ebo Pepper Rice (Airport Road, Oka): the best-known, a sit-down restaurant.\n• Mama 12 (\"Madam 12 O'Clock\"): listed as Mama 12 Pepper Rice on the Chowdeck delivery app. Ask locally for the address.\n• Madam 2 (\"Madam 2 O'Clock\"): reportedly an izoko seller of 20+ years behind Image Garden, Boundary Road, GRA.\n• Mama Ivie: a local favourite. Ask locally for directions.",
                    "For people from Benin, leaf rice with pepper stew is comfort food. Each \"Mama\" or \"Madam\" has loyal regulars and arguments about whose stew is hottest. The best pots sell out by mid-afternoon.",
                    "Mama Ebo Pepper Rice: Airport Road, Oka (listings give No. 72, 73 or 88), near the Airport Road/Ihama Road side of GRA. Take a cab or ride-hail.\n\nMadam 2 O'Clock (reported): From Airport Road, take the street just before Image Garden on the right. From inside GRA, it's just after Image Garden on the left. Ask for directions on arrival.\n\nMama 12 & Mama Ivie: No published addresses. Ask locally, or order Mama 12 on Chowdeck.",
                    "Go Early: The popular spots sell out fast. Aim for late morning or lunch, and for Madam 2 O'Clock arrive by 2–3 PM at the latest (reported).\n\nMama Ebo: Reported hours Monday–Saturday, 10:00 AM–6:00 PM (closed Sunday per Google; some creators say open daily — confirm). Phone 0916 237 7304; Instagram @mama_ebo_benin. Dine-in and takeaway. Besides leaf rice it serves jollof and fried rice, swallow with egusi or ogbono, and grilled chicken, goat meat and fish.\n\nHow It's Served: Rice in leaves with a ladle of pepper stew and your choice of meat. Order sides like plantain or moi-moi where available.\n\nDelivery: Mama Ebo and Mama 12 have been listed on Chowdeck in Benin City (confirm current availability).",
                    "Heat: The stew is very peppery. Ask for less pepper or a drink on the side if you're not used to it.\n\nCrowds & Waits: Reviewers mention queues, slow service at peak times and a hot dining room at Mama Ebo.\n\nScam Accounts: A reviewer warned of a fake account using Mama Ebo's name. Pay only at the counter or through the official number or Instagram.\n\nThin Information: Mama 12, Madam 2 O'Clock and Mama Ivie have little or no online presence, so details may be out of date. Confirm locally.",
                    "Leaf Rice Plate with Meat: Under ₦5,000 per head at Mama Ebo (local creator estimate, 2024). Extra meat and sides cost more.\nDelivery App Fees: Extra.\nRide-hail within Benin City: Extra.",
                    new[]
                    {
                        "Mat-Ice Fast Food: Benin's long-running ice-cream and fast-food chain, with a branch on Ihama Road, GRA.",
                        "Benin National Museum: Benin bronzes and history at King's Square."
                    },
                    "Late morning to early afternoon, Monday to Saturday, before the pots sell out.",
                    "Medium to High (6/10) at lunchtime.",
                    "Rating: 5/10 — Mama Ebo is a spacious sit-down restaurant. The smaller vendors are simple roadside or back-street set-ups.",
                    "30 minutes to 1 hour per stop.")),

            // Chillingchow — founder-nominated; researched. Sources: ranked.ng/place/chillingchow-benin-city (No. 2 Sadoh Lane,
            // Ihama Rd, behind De Brit Hotel, GRA; 0814 449 2179; 24 hrs listed; 4.0 from 97 Google reviews; board & card games;
            // slow service, poor ventilation; newest review Jan 2025), zeperoni.com/listing/chillingchow/ (0811 638 4242;
            // IG @chilling.chow; FB chillingchownigeria; site chillingchow.com.ng — did not resolve; menu incl. jollof, burgers,
            // pasta, sushi; live events/themed nights), tripadvisor listing "Chilling Chow" d23694031 (not opened).
            // Coordinates: APPROXIMATE — ranked.ng/Google point (6.3062153, 5.6196729).
            // AvgCost: ~US$13 ≈ ₦18,000 at ~₦1,360 per US$ (Nov 2026) — a main, a drink and a ride; prices unpublished.
            new("benin-city", "Chillingchow", Nigeria, BeninCity, EdoLangs, 6.3062, 5.6197,
                13m, "Car", 1,
                new[] { "food_experience", "entertainment_nightlife" },
                new[] { "food_focused", "social", "group_friendly", "couple_friendly", "best_evening" },
                new[] { "Chilling Chow", "ChillingChow", "Chillin Chow", "Chilling Chow Benin", "Chilling Chow Lounge", "Chillingchow Restaurant" }, "Interior",
                Desc(
                    "Chillingchow is a restaurant and lounge in GRA, Benin City, tucked behind De Brit Hotel off Ihama Road. Listings describe a mix of Nigerian and international dishes (jollof rice, burgers, pasta, even sushi), indoor and outdoor seating, and live events and themed nights. Reviewers note that board games and card games are available while you eat.\n\nWhat to Do: Come with friends for food, drinks and a round of games, or on a themed night.",
                    "A relaxed GRA hangout for friends and dates. Reviews praise the food, the variety and the polite staff, and many people come for the games as much as the menu.",
                    "Address: No. 2 Sadoh Lane, off Ihama Road, behind De Brit Hotel, GRA, Benin City.\n\nGetting There: Take a cab or ride-hail. Ask for De Brit Hotel on Ihama Road if the driver doesn't know Sadoh Lane.",
                    "Hours: Listed as open 24 hours (Google). Confirm, especially late at night.\n\nContact: 0811 638 4242 or 0814 449 2179 (both listed); Instagram @chilling.chow.\n\nMenu & Prices: Not published in a form we could check. See the Instagram or ask for the menu.\n\nGames: Board and card games are reportedly available. Ask staff.",
                    "Slow Service: Several reviews mention slow service, so don't come in a hurry.\n\nVentilation: One reviewer noted smoke drifting in from the back.\n\nOld Reviews: The newest reviews we saw date from early 2025. Check recent Instagram posts for current menu and events.",
                    "Food & Drinks: Prices not published — expect mid-range GRA restaurant prices; confirm before ordering.\nThemed Nights & Events: May carry extra charges — ask in advance.\nRide-hail within Benin City: Extra.",
                    new[]
                    {
                        "Vice City Restaurant and Lounge: Lounge on Ihama Road with a weekly karaoke night.",
                        "Mat-Ice Fast Food: Ice cream for dessert at the Ihama Road branch."
                    },
                    "Evenings, especially weekends or a themed night.",
                    "Medium (5/10) — busier on weekend evenings.",
                    "Rating: 6/10 — ground-level restaurant with indoor and outdoor seating; confirm step-free access.",
                    "1.5 to 3 hours.")),

            // Sip & Dip — founder-nominated (founder believes it has karaoke); researched. Sources:
            // ranked.ng/place/sip-and-dip-restaurant-zqq_e0 (63/65 Eli Love filling station, Benin–Sapele Rd, opp. NPDC, Oka;
            // Google "Bar"; 8am–9pm daily; 4.3 from 223 reviews; small but well run, good ambience, slow service, pricey),
            // zeperoni.com/listing/sip-and-dip-restaurant/ (+234 805 053 3920; IG @sipanddip_lounge_benin; Mon–Sat 10–9,
            // Sun 12–9), evendo.com/locations/nigeria/benin-kingdom/bar/sip-dip-restaurant (6.3068441, 5.6306819; templated
            // text not used). Karaoke & spend figures from TikTok search snippets only (karaoke Wed & Fri 6–9 PM, free;
            // "budget ₦15–20k per person"; ₦25,000 minimum spend mentioned for photos); sipanddip.ng did not resolve.
            // Coordinates: APPROXIMATE — Google-derived point via ranked.ng/evendo (6.3068441, 5.6306819).
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — a reviewer's ₦15–20k per person plus a ride.
            new("benin-city", "Sip and Dip Restaurant and Lounge", Nigeria, BeninCity, EdoLangs, 6.3068, 5.6307,
                15m, "Car", 1,
                new[] { "food_experience", "entertainment_nightlife" },
                new[] { "food_focused", "social", "couple_friendly", "group_friendly", "music", "best_evening" },
                new[] { "Sip & Dip", "Sip&dip", "Sip and Dip", "Sip n Dip", "Sip 'n' Dip", "Sip&dip Restaurant", "Sip and Dip Lounge", "Sip and Dip Benin" }, "Interior",
                Desc(
                    "Sip & Dip is a small, stylish restaurant and lounge inside the Eli Love filling station on Benin–Sapele Road, opposite NPDC. Listings describe African-inspired and international dishes, cocktails, beer and wine, with indoor and outdoor seating. It is reportedly also a karaoke spot: social-media posts mention karaoke nights on Wednesdays and Fridays.\n\nWhat to Do: Dinner and cocktails, a date night, or a karaoke evening (reportedly). Confirm the karaoke nights before you go.",
                    "A popular date-and-drinks spot that shows up often on Benin food TikTok. Reviewers like the ambience and drinks and find the space small but well managed.",
                    "Address: 63/65 Benin–Sapele Road (inside Eli Love filling station), opposite NPDC, Oka, Benin City.\n\nGetting There: Take a cab or ride-hail and ask for Eli Love filling station on Sapele Road.",
                    "Hours: Listed as 8:00 AM–9:00 PM daily (Google). Another listing gives Monday–Saturday 10:00 AM–9:00 PM and Sunday 12:00 PM–9:00 PM. Confirm before you go.\n\nKaraoke (reportedly): Wednesdays and Fridays, about 6:00–9:00 PM, free to join, per social-media posts. Confirm with the venue.\n\nContact: 0805 053 3920 (WhatsApp, listed); Instagram @sipanddip_lounge_benin.\n\nReservations: Book ahead for weekend evenings; the space is small.",
                    "Small Space: Reviewers note it is smaller than it looks in Instagram photos.\n\nSlow Service: Several reviews mention slow service at busy times.\n\nPrices: Reviewers call food and drinks on the high side. Check the menu prices before ordering.\n\nKaraoke Unconfirmed: Karaoke nights are reported on social media only. Call to check.",
                    "Food & Drinks: A reviewer suggests budgeting about ₦15,000–₦20,000 per person.\nMinimum Spend: A ₦25,000 per-person minimum spend has been mentioned on social media (reportedly linked to photoshoots). Confirm with the venue.\nRide-hail within Benin City: Extra.",
                    new[]
                    {
                        "Barcode Station: Late-night bar and lounge on Airport Road with a DJ.",
                        "Chillingchow: Restaurant-lounge with board games in GRA."
                    },
                    "Evenings; Wednesday or Friday for karaoke (reportedly).",
                    "Medium (5/10) — busier on weekend and karaoke evenings.",
                    "Rating: 6/10 — small ground-level venue; confirm step-free access.",
                    "2 to 3 hours.")),

            // Barcode Station — founder-nominated (type unconfirmed by founder); researched. Sources:
            // ranked.ng/place/barcode-station-benin-city (193 Airport Rd, Oka/Oko; 0815 721 3051; Mon–Sat 24 hrs, Sun 1 PM–12 AM;
            // 4.0 from 838 Google reviews; Google types Bar/Night club/Restaurant/Takeaway; DJ — Afrobeats, old-school R&B,
            // trending; hype man; indoor & outdoor; food-quality complaints; pastries praised; newest review Jan 2026),
            // evendo.com/locations/nigeria/benin-kingdom/restaurant/barcode-station/best-bars (address, hours, "moderate"),
            // guardian.ng/life/afro-vibes-2021-smirnoff-shut-down-benin-city/ (Smirnoff Afro Vibes stop at "Bar Code", Aug 2021).
            // Coordinates: APPROXIMATE — ranked.ng/Google point (6.3080177, 5.5958429).
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — drinks, a snack and a ride home; prices unpublished.
            new("benin-city", "Barcode Station", Nigeria, BeninCity, EdoLangs, 6.3080, 5.5958,
                15m, "Car", 2,
                new[] { "entertainment_nightlife" },
                new[] { "nightlife", "music", "social", "group_friendly", "best_at_night" },
                new[] { "Barcode", "Bar Code", "Barcode Benin", "Barcode Lounge", "Barcode Bar", "Barcode Station Benin City" }, "Interior",
                Desc(
                    "Barcode Station is a bar and lounge on Airport Road, Benin City. Google lists it as a bar, nightclub, restaurant and takeaway. It has indoor and outdoor areas, a DJ playing Afrobeats, old-school R&B and current hits, and a hype man who reviewers say keeps the energy up. In 2021 it hosted a stop on Smirnoff's Afro Vibes tour.\n\nWhat to Do: Drinks, music and a night out with friends. Pastries and food are also served.",
                    "One of Benin's long-running party spots off Airport Road, known more for the atmosphere, DJ and hype man than for the food.",
                    "Address: 193 Airport Road, Oka, Benin City.\n\nGetting There: Take a cab or ride-hail, and arrange your ride home before you go out.",
                    "Hours: Listed as open 24 hours Monday–Saturday and 1:00 PM–midnight on Sunday (Google). Confirm.\n\nContact: 0815 721 3051 (listed).\n\nAge: Nigeria's legal drinking age is 18. Bring ID.\n\nDress Code & Cover Charge: None published. Ask before special or event nights.",
                    "Food: Several reviewers complained about food quality and orders not matching what they paid for. Stick to drinks and snacks or check your order.\n\nNight Safety: Use a trusted cab or ride-hail home, keep valuables close and watch your drink.\n\nOld Reviews: Many reviews are several years old. Check recent posts for current events.",
                    "Drinks & Food: Prices not published (listed as low to moderate). Confirm on arrival.\nEvent Nights: Possible entry or table charges. Ask in advance.\nRide-hail Home: Extra, and late-night fares are higher.",
                    new[]
                    {
                        "Vice City Restaurant and Lounge: Lounge on Ihama Road, about 2 km away, with a weekly karaoke night.",
                        "Sip and Dip Restaurant and Lounge: Cocktails and karaoke (reportedly) on Sapele Road."
                    },
                    "Late evenings, Thursday to Saturday.",
                    "Medium to High (7/10) on weekend nights.",
                    "Rating: 5/10 — bar with indoor and outdoor areas; confirm step-free access.",
                    "An evening — 3 to 5 hours.")),

            // Vice City — founder-nominated (type unconfirmed by founder); researched. Sources: instagram.com/vicecitybenin (bio:
            // restaurant, lounge, kitchen; 20 Ihama Road, GRA; highlights "Friday Nights", Karaoke, Paint & Party; ~2.1k followers),
            // karaokecrowd.com/en/venues/vice-city-restaurant-lounge-benin-city (Wednesday karaoke 8 PM, host MC Chuckination,
            // last confirmed 8 Jul; +234 906 467 8697), linktr.ee/vicecityentertainment, x.com/vicecity_ent (delivery),
            // evendo.com/locations/nigeria/benin-kingdom/bar/vice-city (14 Ihama Rd, Oka; +234 901 022 7308; 24 hrs; 4.2 from 369
            // Google reviews; "jazz and country" text looks templated — not used).
            // Coordinates: APPROXIMATE — evendo/Google point (6.3148217, 5.6091689); address conflict (14 vs 20 Ihama Rd).
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — food, drinks and a ride; prices unpublished.
            new("benin-city", "Vice City Restaurant and Lounge", Nigeria, BeninCity, EdoLangs, 6.3148, 5.6092,
                15m, "Car", 2,
                new[] { "entertainment_nightlife", "food_experience" },
                new[] { "nightlife", "music", "social", "group_friendly", "best_at_night" },
                new[] { "Vice City", "Vice City Benin", "Vice City Lounge", "Vice City Entertainment", "Vice City Restaurant & Lounge", "Vice City Restaurant and Entertainment", "Vicecity" }, "Interior",
                Desc(
                    "Vice City (Vice City Entertainment) is a restaurant, lounge and kitchen on Ihama Road in GRA, Benin City. It runs regular themed nights: a Wednesday karaoke night with an open-stage format, Friday nights, and paint-and-party events. Food and drinks can also be delivered.\n\nWhat to Do: Eat, drink and sing at karaoke on a Wednesday, or come for a Friday night out.",
                    "A GRA lounge known for its social nights, especially karaoke, which draws a regular crowd of singers and spectators.",
                    "Address: 20 Ihama Road, GRA, Benin City (per the venue's Instagram). Some map listings give 14 Ihama Road, Oka. Look for it near the Airport Road end of Ihama Road.\n\nGetting There: Take a cab or ride-hail.",
                    "Karaoke: Wednesdays from about 8:00 PM, hosted by MC Chuckination (listing last confirmed in July — check Instagram).\n\nHours: Listed as open 24 hours (Google). Confirm.\n\nContact: Instagram @vicecitybenin (the menu is posted there). Listed phones: 0906 467 8697 and 0901 022 7308.\n\nAge: Nigeria's legal drinking age is 18. Bring ID.\n\nDress Code & Cover Charge: None published. Ask before event nights.",
                    "Check the Night: Event schedules change. Confirm karaoke or party nights on Instagram before going.\n\nAddress Confusion: Listings give two street numbers on Ihama Road, so call if you can't find it.\n\nNight Safety: Use a trusted cab or ride-hail home and keep valuables close.",
                    "Food & Drinks: Prices not published. Check the menu on Instagram.\nEvent Nights (paint & party etc.): May carry a separate fee. Ask in advance.\nRide-hail Home: Extra.",
                    new[]
                    {
                        "Chillingchow: Restaurant-lounge with board games, a short ride away in GRA.",
                        "Barcode Station: Late-night bar with a DJ on Airport Road."
                    },
                    "Wednesday evening for karaoke; Friday night for a party.",
                    "Medium (6/10) on event nights.",
                    "Rating: 5/10 — lounge venue; confirm step-free access.",
                    "An evening — 2 to 4 hours.")),

            // ═══ Batch: Benin City event venue ═══
            // ─── Benin City ────────────────────────────────────────────────────
            // Image Garden — founder: event/garden venue at 6 Boundary Road, GRA; hosts Rave Experience, Fungasm,
            // Oga Bole Festival and (likely) Metro Events (used as given; no dates — events stored separately).
            // Sources: mapcarta.com/N2154560071 (OSM node, leisure=garden, 6.31831, 5.60895, Oredo; near Lily Hospital);
            // waze.com live-map listing "Image Garden, 6 Boundary Rd, Benin City" (phone 0701 911 9908 — via search summary);
            // venue.eventsnigeria.com/venue/image-gardens/ ("multi purpose event garden with a stage"; banquet 3,000 /
            // theatre 5,000; 6 Boundary Rd, 300102); ogavenue.com.ng listing (30% deposit, balance 3 weeks before, no refunds
            // — via search summary); pevent.co/fungasmbenin (Fungasm at Image Gardens, 7 Dec 2025, ₦5,000–₦8,000);
            // pevent.co/ravers (Metro Events "Rave Party" at Image Garden, 14 Dec 2025, ₦5,000 — confirms Metro Events);
            // pevent.co/ogabolefestival (Oga Bole Festival at Image Garden, 31 Aug 2024, free team tickets); pevent.co/afrospook
            // & afrospook.com (AfroSpook rave at Image Garden, 31 Oct 2025 — via search summary); bellanaija.com/2025/06/
            // jameson-city-takeover-in-benin/ (Jameson City Takeover at Image Garden, GRA, 7 Jun 2025); guardian.ng/?p=2369564
            // (Electro Fest at Image Garden GRA — via search summary); pulse.ng/story/are-events-the-new-crude-oil-
            // 2025080813194082211 (Rave Experience: travelling festival incl. Benin; venue not named — Image Garden link is
            // founder-supplied); ranked.ng/botanical-gardens/benin-city (4.0/5 from 36 Google reviews — via search summary).
            // No official website or Instagram found.
            // Coordinates: OSM node N2154560071 via Mapcarta (6.31831, 5.60895); Open Location Code 6FR78J95+8H decodes to
            // the same point.
            // AvgCost: ~US$10 ≈ ₦13,600 at ~₦1,360 per US$ (Nov 2026) — a typical ₦5,000–₦8,000 event ticket plus drinks/food.
            new("benin-city", ImageGardenName, Nigeria, BeninCity, EdoLangs, 6.31831, 5.60895,
                10m, "Car", 2,
                new[] { "entertainment_nightlife" },
                new[] { "nightlife", "music", "social", "group_friendly", "local_favorite", "seasonal" },
                new[] { "Image Gardens", "Image Garden Benin", "Image Garden GRA", "Image Gardens Benin City", "Image Garden Boundary Road", "Imade Garden" }, "Landscape",
                Desc(
                    "Image Garden is an open-air garden and event venue at 6 Boundary Road, GRA, Benin City. It is a large lawn with a stage, used for concerts, raves, festivals and private functions. Venue listings give a capacity of about 3,000 for a banquet and 5,000 standing or theatre-style, which makes it one of the city's main spaces for big outdoor events.\n\nWhat to Do: Go to one of the events held there. Image Garden regularly hosts Benin's big rave and party brands, including Rave Experience, Fungasm and Metro Events' rave parties, as well as the Oga Bole Festival, a street-food celebration of roasted plantain (bole). Brand activations, such as a Jameson City Takeover in 2025, and themed raves have also been held there. The garden can be hired for weddings, concerts and other events.",
                    "For young people in Benin City, Image Garden is where the big outdoor raves and festivals happen. If a major party is coming to town, there is a good chance it's at Image Garden.",
                    "Address: 6 Boundary Road, GRA, Benin City, Edo State, near Lily Hospital.\n\nGetting There: Take a Bolt or taxi to Image Garden, Boundary Road, GRA. Large events cause heavy traffic and roadside parking on Boundary Road, so ride-hail is easier than driving.",
                    "Event Venue: Image Garden opens for events rather than set daily hours. Times, prices, food and drink depend on each organiser. Event tickets are usually sold online through the organisers or ticketing apps such as Pevent. Recent raves have cost about ₦5,000–₦8,000, and some festivals are free.\n\nFood & Drink: Usually provided by event vendors and bars. There is no published permanent restaurant or bar.\n\nVenue Hire: Call 0701 911 9908 (listed number; confirm before paying). A venue listing describes a 30% deposit with the balance due three weeks before the event, and says payments are non-refundable. Confirm current terms directly.\n\nNo Official Website Found: Check event organisers' pages for what's on.",
                    "Big Crowds: Raves and festivals draw large crowds. Keep phones and valuables secure, and agree a meeting point with your group.\n\nGetting Home: Arrange your ride home before a late event ends, because ride-hail demand surges.\n\nRoad Conditions: Boundary Road and nearby GRA roads have been reported in poor condition and under repair, so allow extra time.\n\nOutdoor Venue: There's little cover if it rains. Check the forecast in the rainy season (roughly April to October).\n\nBuy Tickets Officially: Use the organiser's or ticketing platform's link to avoid fake tickets.",
                    "Event Tickets: Set by each organiser. Recent raves cost about ₦5,000–₦8,000, and some festivals have been free.\nFood & Drinks: Extra. Sold by event vendors.\nVenue Hire: Ask the venue. A 30% deposit is reportedly required to book.\nBolt/Taxi: Extra. Expect surge pricing after events.",
                    new[]
                    {
                        "Benin City Leaf Rice (Izoko) Spots: Benin's leaf-wrapped pepper rice, with sellers around GRA and Airport Road.",
                        "Kada Plaza: A cinema and arcade complex on Sapele Road.",
                        "Royal Palace of the Oba of Benin: Seat of the Benin monarchy, in central Benin City."
                    },
                    "During a scheduled event. December (the \"Detty December\" party season) and the dry season (roughly November to March) are the busiest times for raves and festivals.",
                    "Low (1/10) on non-event days; High (8/10) during raves and festivals.",
                    "Rating: 5/10 — an open lawn, which may be uneven, with no published accessible facilities. Ask the event organiser.",
                    "3 to 6 hours for an event.")),

            // ═══ Batch: Calabar ═══
            // ─── Calabar (Cross River State) ───────────────────────────────────
            // Combat Games Zone — founder's first-hand directions (via Tinapa main entrance, two roundabouts,
            // right turn; hard to find) used as given. Sources: instagram.com/combatgameszone (bio: offerings,
            // Tinapa Resort, Sat & Sun 12pm–6pm), instagram.com/reel/DePl1jeIS5m (price list, 8 Oct 2026),
            // instagram.com/reel/DfcHDGCodTY (hours, 7 Nov 2026), Google Maps place "Combat Games Zone"
            // (Tinapa Resort, Calabar 540211; Plus Code 28X8+XX).
            // Coordinates: Google Maps place pin (5.049992, 8.317423), inside Tinapa near Baze University's campus.
            // AvgCost: ~US$20 ≈ ₦27,000 at ~₦1,360 per US$ (Nov 2026) — two activities and a share of a driver; rough.
            new("calabar", CombatGamesZoneName, Nigeria, Calabar, CalabarLangs, 5.0500, 8.3174,
                20m, "Car", 2,
                new[] { "activity_experience" },
                new[] { "adventurous", "social", "group_friendly", "hidden_gem", "quirky", "budget_friendly" },
                new[] { "Combat Game Zone", "Combat Games Zone Tinapa", "Combat Games Zone Calabar", "Combat Games Calabar", "Rage Room Calabar" }, "Exterior",
                Desc(
                    "Combat Games Zone is a weekend activity spot inside Tinapa Resort, north of central Calabar, offering short, adrenaline-filled games for friends, couples and groups.\n\nWhat to Do: Gel blaster battles, archery, axe throwing, a rage room where you can smash things for 20 minutes, and scooter rides, plus smaller side attractions.",
                    "It's one of the few active things to do at Tinapa on a weekend, and it's still a little-known spot. Calabar creators share it as a different kind of outing from the usual lounges.",
                    "Location: Inside Tinapa Resort, north of central Calabar — roughly 15–30 minutes' drive by taxi or driver.\n\nInside Tinapa (it's known to be hard to find):\n1. Enter through the Tinapa main entrance and keep driving straight to the first roundabout.\n2. Go right until you reach the second roundabout.\n3. Go round the second roundabout and continue straight.\n4. Turn right — you'll find Combat Games Zone there.\n\nTip: Search \"Combat Games Zone\" on Google Maps (it's pinned near Baze University's Tinapa campus), and keep your driver for the trip back.",
                    "Open: Saturdays and Sundays only, 12 noon–6 PM. It has also opened on some public holidays, so check Instagram @combatgameszone.\n\nPrices (Oct 2026 price list): Archery ₦5,000 (1 round, 8 arrows); Axe Throwing ₦5,000 (1 round, 8 throws); Gel Blaster ₦5,000 (1 round, 100 gel balls); Scooter Ride ₦5,000 (5 laps); Rage Room ₦15,000 (20 minutes).\n\nBooking & Contact: Message Instagram @combatgameszone — confirm whether you need to book, especially for groups.\n\nAge Limits & Gear: Not published — ask before bringing children, and ask what protective gear is provided for the gel blasters and rage room.",
                    "Hard to Find: Even locals miss it — follow the directions, use the Google Maps pin, or message ahead.\n\nWeekends Only: It's closed Monday to Friday — don't make the trip on a weekday.\n\nTransport Back: Taxis are scarce at Tinapa — keep your driver or arrange your return in advance.\n\nSafety: Follow the staff's safety instructions, and wear the eye protection and gear provided.",
                    "Each Activity: ₦5,000 a round; Rage Room ₦15,000 per 20 minutes — rounds add up quickly.\nDriver or Taxi Round Trip from Calabar: Extra — agree the price in advance.\nTinapa Entry: Any gate fee is unconfirmed — ask.",
                    new[]
                    {
                        "Tinapa: The riverside business and leisure resort that Combat Games Zone sits inside.",
                        "Calabar Museum (Old Residency): History of the city, back in central Calabar.",
                        "Marina Resort & Slave History Museum: The city waterfront."
                    },
                    "Saturday or Sunday afternoon (12 noon–6 PM). The dry season (roughly November to March) is most comfortable, and December brings carnival crowds to Calabar.",
                    "Low (3/10) — a small weekend-only venue.",
                    "Rating: 5/10 — at a large resort complex with long distances; confirm surfaces and step-free access with the venue.",
                    "1.5 to 2 hours, plus travel to and from Tinapa.")),

            // ─── Calabar ───────────────────────────────────────────────────────
            // Hope Waddell — founder input ("first secondary school in Nigeria") NOT supported: CMS Grammar School,
            // Lagos (1859) is generally recognised as the oldest; Hope Waddell (1895) is described as the first in the
            // former Eastern Region. Sources: en.wikipedia.org/wiki/Hope_Waddell_Training_Institution; premiumtimesng.com
            // (Apr 2026, Museum & Hall of Fame); pmexpressng.com/?p=156662 (Aug 2023, Premier Building restoration);
            // mauricearchibongtravels.blogspot.com (2013, Eastern Region "first", Diamond Hill); en.wikivoyage.org/wiki/Calabar
            // (entry free, 1894 prefab building); news.crossriverstate.gov.ng (130th anniversary, 2025).
            // Coordinates: OSM Nominatim point for the school (Murtala Mohammed Highway), consistent with Wikipedia.
            // AvgCost: ~US$3 ≈ ₦4,000 at ~₦1,360 per US$ (Nov 2026) — entry reported free; a cab within Calabar.
            new("calabar", HopeWaddellName, Nigeria, Calabar, CalabarLangs, 4.9756, 8.3265,
                3m, "Car", 1,
                new[] { "landmark_monument", "historical_tour" },
                new[] { "history", "architecture", "educational", "cultural", "hidden_gem", "photography" },
                new[] { "HOWAD", "Hope Waddell", "Hope Waddell Training Institute", "Hope Waddell College", "Hope Waddell Calabar", "Hope Waddell Training School" }, "Exterior",
                Desc(
                    "Hope Waddell Training Institution (HOWAD) is a secondary school in Calabar founded in 1895 by missionaries of the United Presbyterian Church of Scotland and named after Rev. Hope Masterton Waddell, who led the mission that reached Calabar in 1846. Mary Slessor was among its driving forces. It is one of Nigeria's oldest secondary schools and is widely described as the first in the former Eastern Region (CMS Grammar School, Lagos, 1859, is generally cited as Nigeria's oldest). It began as a vocational school teaching carpentry, tailoring, printing and baking, soon became the largest vocational training institution in West Africa, and its students printed the Calabar Observer, the city's first newspaper. Today it is a state-run secondary school.\n\nHeritage Buildings: The first building, a two-storey structure assembled in 1894 from parts shipped from Glasgow (the Premier Building), was restored by the Foundation for the Preservation of Cultural Heritage in Nigeria (first phase completed in 2023). Other historic blocks include the Duke Town School Building (1899) and the Jubilee Building (1924), and the old printing press is still on site.\n\nMuseum & Hall of Fame: In April 2026 the school announced a Museum and Hall of Fame in the 1894 building, with photographs of inductees, archaeological finds, trophies, books and records — confirm it is open to visitors.\n\nNotable Alumni: Nnamdi Azikiwe (Nigeria's first President), Kingsley Ozumba Mbadiwe, Eyo Ita, Dennis Osadebay, Vice Admiral Edet Akinwale Wey and Eni Njoku (first Vice-Chancellor of the University of Lagos).",
                    "For Calabar people, Hope Waddell is a point of pride — the school that educated a generation of Nigeria's independence leaders. Its old boys remain active, marking the school's 130th anniversary in 2025 with a ₦1 billion legacy fund.",
                    "Address: Diamond Hill, off Murtala Mohammed Highway, Calabar.\n\nBest Access: By cab or ride-hail within Calabar — a few minutes from the Marina and the Old Residency (Calabar Museum).",
                    "Working School: This is an active secondary school, not a museum site — visit on a weekday during school hours and check in at the gate or the principal's office first. No public visiting hours are published.\n\nTours: Older guides say staff will often show visitors around; ask at the office. Entry has been listed as free.\n\nMuseum & Hall of Fame: Announced in April 2026 — confirm whether it is open and on what terms.\n\nPhotos: Ask before photographing students or classrooms.",
                    "Permission First: Don't wander the campus unannounced — students are in session.\n\nCondition: Some older buildings have been reported as run-down despite restoration work; take care around them.\n\nThin Visitor Information: Little is published about access — call ahead or arrive with a local contact.",
                    "Entry: Reported free (confirm).\nGuide or Staff Tip: Discretionary.\nCab within Calabar: Extra — agree the fare first.",
                    new[]
                    {
                        "Calabar Museum (Old Residency): The 1884 colonial residency with Efik and slave-trade history, about 2 km east.",
                        "Marina Resort & Slave History Museum: Waterfront park and museum on the Calabar River, nearby.",
                        "Calabar Botanic Garden: Colonial-era green space a short drive south."
                    },
                    "Weekdays during term time, when the school office is open; the dry season (roughly November to March) is most comfortable.",
                    "Low (2/10) for visitors — a busy school campus on weekdays.",
                    "Rating: 5/10 — hilly campus with older buildings and steps; ask about access to the ground floors.",
                    "1 to 2 hours.")),

            // Kwa Falls — founder input ("a waterfall") confirmed. Sources: wikidata.org/wiki/Q108908735 (coords, plus code
            // 4GV6+23J "Aningeje Town, along Oban Road", height ~29 m); pioneernewsng.com (Feb 2022: 25 km, 234 steps, ₦500,
            // swimming, rainy-season flooding, neglect); nigeriagalleria.com; subomiplumptre.com/calabar (45–60 min drive,
            // guide, missing railings); primeprogressng.com (May 2025, ₦500); news.crossriverstate.gov.ng (Jun 2026 inspection);
            // nairametrics.com (Jul 2025, ₦18bn upgrade incl. Kwa Falls). Coordinates: Wikidata P625; OSM puts Aningeje
            // village 1.5 km away (5.1384, 8.5238). City: Calabar (base city ~25 km away, like Tinapa).
            // AvgCost: ~US$25 ≈ ₦34,000 at ~₦1,360 per US$ (Nov 2026) — gate, guide tip, share of a hired car; rough guess.
            new("calabar", KwaFallsName, Nigeria, Calabar, CalabarLangs, 5.1428, 8.5102,
                25m, "Car", 3,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "nature", "adventurous", "photography", "local_favorite", "seasonal", "budget_friendly" },
                new[] { "Kwa Waterfall", "Kwa River Falls", "Kwa Falls Calabar", "Kwa Falls Aningeje", "Kwa Falls Anegeje", "Aningeje Waterfall" }, "Landscape",
                Desc(
                    "Kwa Falls is a waterfall on the headwaters of the Kwa River at Aningeje (also spelled Anegeje) village in Akamkpa Local Government Area, about 25 km from Calabar. The river rises in the Oban Hills of Cross River National Park and drops through a wide gash in a cliff into a narrow, steep gorge — Wikidata lists the fall at about 29 m — and forms a deep plunge pool, surrounded by rainforest with mahogany and ebony trees. It is one of Cross River State's best-known waterfalls and is included in the state's ₦18 billion tourism upgrade approved in 2025.\n\nWhat to Do: Walk down the staircase into the gorge, scramble over the rocks for the best view of the falls, swim or wade in the pool when the water is calm, picnic at the top and look out for monkeys in the forest.",
                    "Kwa Falls is a classic day trip for Calabar residents and carnival-season visitors — a cool, green escape from the city. Locals say to go with a guide and avoid the peak of the rains.",
                    "From Calabar: About 25–30 km by road to Aningeje village (listed along the Oban road) — roughly 45 minutes to 1 hour, though some guides allow up to 2 hours. Hire a car with a driver who knows the way; there is no published public transport to the falls.\n\nAt the Falls: A staircase — usually given as 234 steps — leads down from the top into the gorge, then you climb over rocks for the best view.",
                    "Entry: A gate fee has been reported at ₦500 (2022 and 2025 guides); one source says ₦1,000 is paid to officials at the top and another says ₦2,000 — confirm on site.\n\nHours: Reported as 7:00 AM–6:00 PM daily (unconfirmed).\n\nGuides: Local guides and a caretaker meet visitors and lead the descent — tip them.\n\nFacilities: Basic — reports mention tables and chairs at the top and a small art museum; bring your own food and drinks.\n\nBring: Swimwear, a towel, shoes with good grip (waterproof hiking boots are ideal), water, sunscreen and insect repellent.",
                    "Steep Steps: The staircase is steep, has needed repair and has missing railings in places — not recommended for young children, older visitors or anyone unsteady on their feet.\n\nRainy Season: In the heart of the rains the river can fill the gorge wall to wall and the site may be hard to reach. Don't swim after heavy rain or in strong current.\n\nSwimming: The pool is deep — swim only if you are a confident swimmer, stay close to the edges and follow the guide's advice.\n\nSlippery Rocks: Wet rocks around the pool are slippery and sharp.\n\nRoad Travel: Travel in daylight and return before dark.",
                    "Gate Fee: About ₦500–₦2,000 (confirm).\nGuide Tip: Discretionary.\nCar & Driver from Calabar (return): Extra — agree the price in advance.\nFood & Drinks: Bring your own.",
                    new[]
                    {
                        "Calabar Drill Ranch: Drill-monkey sanctuary in Calabar, a good stop on the way back to town.",
                        "Calabar Museum (Old Residency): Calabar and Efik history, in town after the trip."
                    },
                    "Just after the rainy season (roughly October to December) for strong flow with safer access; the dry season (to March) is easiest. Avoid the peak of the rains (around July to September), when the gorge can flood.",
                    "Low to Medium (4/10) — busier at weekends and in the December carnival season.",
                    "Rating: 2/10 — a long, steep staircase with missing railings in places, then rough, slippery rocks; not wheelchair accessible.",
                    "Half a day including the drive from Calabar.")),

            // Calabar Local Food Spots — founder named Pkriss Kitchen, Queenth Kitchen, Mustard Seed and Food Villa for local
            // food. Too thin individually, so combined. "Queenth" is taken to be Queenieez Kitchen (The Firewood Girl) — confirm.
            // "Pkriss" = a Facebook page "P-kris Kitchen Calabar" only (unverified). Sources: tripbase.com/destinations/calabar-nigeria/
            // venues/ (Queenieez 8.3/10, 126 reviews; Food Villa 8.8/10); en.wikivoyage.org/wiki/Calabar (Mustard Seed 35 Okoro Agbo St;
            // Queenieez 17 IBB Way); finelib.com (Queenieez 93 Ndidem Iso Rd; Mustard Seed 12 Grace Edet Cres.); evendo.com (Mustard
            // Seed 3.9/274; Food Villa 124 Old Ikang/Marian Rd, 4.7/96, hours); x.com/FoodVilla_NG (124B Old Ikang off Marian Rd, 10am–10pm).
            // Coordinates: APPROXIMATE — a central point between Marian/Old Ikang Road (Food Villa ~4.9705, 8.3403) and Akim Qua Town.
            // AvgCost: ~US$8 ≈ ₦11,000 at ~₦1,360 per US$ (Nov 2026) — a soup-and-swallow meal, drink, short cab; rough guess (no menus found).
            new("calabar", CalabarFoodName, Nigeria, Calabar, CalabarLangs, 4.9740, 8.3390,
                8m, "Car", 2,
                new[] { "food_experience" },
                new[] { "food_focused", "local_favorite", "cultural", "budget_friendly" },
                new[] { "Queenieez Kitchen", "Queenieez Kitchen (The Firewood Girl)", "The Firewood Girl", "Queenth Kitchen", "Queenie's Kitchen", "Mustard Seed", "Mustard Seed Restaurant", "Mustard Seed Calabar", "Food Villa", "Food Villa Calabar", "Food Villa Restaurant and Bar", "P-kris Kitchen", "Pkriss Kitchen", "Pkris Kitchen", "Calabar Food", "Calabar Local Food" }, "Interior",
                Desc(
                    "Calabar is famous across Nigeria for Efik cooking — edikang ikong, afang, fisherman soup (efere ndek iyak), ekpang nkukwo and white soup (afia efere). These are four local spots worth knowing in the city.\n\nQueenieez Kitchen (The Firewood Girl): Known for fisherman soup and stew, afang and ekpang, with banga soup and fast food too. Akim Qua Town (listed at 17 IBB Way, opposite Akim Police Station; another listing gives 93 Ndidem Iso/Marian Road by the Total filling station). Phone 0803 690 1410.\n\nMustard Seed Restaurant: A casual eatery for local soups including fisherman soup, rice and stew, egusi and goat meat, and a popular breakfast stop. Akim Qua Town (listed at 35 Okoro Agbo Street; another listing gives 12 Grace Edet Crescent). Phone 0813 549 8814.\n\nFood Villa: Best known for its shawarma, with local dishes such as moi moi and white soup also advertised. 124B Old Ikang Road, off Marian Road. Phone 0816 838 2530.\n\nP-kris Kitchen: A Calabar food business with a Facebook page (\"P-kris Kitchen Calabar\") — recommended locally; no address published, so contact them through Facebook.",
                    "Ask any Calabar local and they'll have a favourite spot for native soup. These places are local, everyday kitchens rather than restaurants for tourists, so expect simple settings, big portions of soup with swallow (fufu, garri or pounded yam), and dishes that sell out by the evening.",
                    "Getting Around: Take a cab or ride-hail — Food Villa is on Old Ikang Road off Marian Road in central Calabar; Mustard Seed and Queenieez are in the Akim Qua Town area. Listed addresses differ between sources, so call ahead or check Google Maps before you go.",
                    "Hours: Reported hours vary — Mustard Seed roughly 8:00 AM–5:00 PM on weekdays (one listing says until midnight daily); Food Villa about 10 or 11:00 AM–9 or 10:00 PM (Sunday from 3:00 PM). Confirm by phone.\n\nOrdering: Phone the kitchens directly; Queenieez and Food Villa have been listed as offering delivery and takeaway. Food Villa is on X/Twitter as @FoodVilla_NG.\n\nWhat to Order: Edikang ikong, afang, fisherman soup or ekpang nkukwo where available — soups are usually eaten with swallow.\n\nPayment: Carry cash; transfers are common, but cards may not be accepted.",
                    "Check Details First: Addresses, hours and phone numbers differ between listings — confirm before making the trip.\n\nService: Reviews of Queenieez praise the food but mention rude service at times; some Food Villa reviewers found quality inconsistent.\n\nFood Villa is a Shawarma Spot: Go there for shawarma; for Efik soups, the other kitchens are the better bet.\n\nSelling Out: Popular soups can run out later in the day — go at lunchtime.",
                    "Native Soup & Swallow: Prices not published — confirm on arrival.\nShawarma (Food Villa): Prices not published.\nDelivery Fees: Extra.\nCab within Calabar: Extra — agree the fare first.",
                    new[]
                    {
                        "Calabar Museum (Old Residency): Efik history, close to Food Villa's Marian Road area.",
                        "Calabar Drill Ranch: Drill-monkey sanctuary in central Calabar.",
                        "Marina Resort & Slave History Museum: Waterfront park on the Calabar River."
                    },
                    "Lunchtime, when the soups are fresh; especially lively in the December carnival season.",
                    "Medium (5/10) — busiest at lunch and in December.",
                    "Rating: 5/10 — small local eateries; seating and step-free access vary — ask.",
                    "1 hour per meal.")),

            // ═══ Batch: Lagos ═══
            // ─── Lagos ─────────────────────────────────────────────────────────
            // Kriss Kross Skating Rink — founder: [skating]. Sources: krisskrossskate.com (home links, phone
            // 0916 337 9075), krisskrossskate.com/opening-hours/ (image hours.png, uploaded Apr 2026: Tue–Thu
            // 12–9 PM, Fri–Sun 10 AM–10 PM, "at Federal Palace Hotel"), Zoho waiver form linked from the site
            // ("Kriss Kross Skatium Synergy Ltd."; minors' waiver, CCTV, promo photo/video, outdoor/semi-outdoor
            // surfaces), Google Maps listing (6-8 Ahmadu Bello Way, inside Federal Palace Hotel and Casino; 4.5 from
            // 24 reviews; same hours; owner post "Our 2026 Pricelist" 11 Aug 2026; review ~Aug 2026 mentioning a
            // "half price Wednesday deal"; owner reply about a food court), instagram.com/krisskrossskate (bio:
            // "Victoria Island's favourite social skating destination… Private events | Social & Corporate"),
            // wakaabuja.com/kriss-kross-skating-rink (prices table, written for the OLD Lekki site on Hakeem Dickson
            // Link Rd — used only as "reported"). The 2026 price list (Google post / IG highlight) could not be read.
            // Coordinates: Google Maps place pin (6.4314611, 3.4078528) at Federal Palace Hotel. The site's own
            // "Get Directions" short link still resolves to the same place ID, now pinned at the hotel.
            // AvgCost: ~US$18 ≈ ₦25,000 at ~₦1,360 per US$ (Nov 2026) — admission, skate rental and a ride; rough,
            // based on the older published prices.
            new("lagos", KrissKrossName, Nigeria, Lagos, LagosLangs, 6.4315, 3.4079,
                18m, "Car", 1,
                new[] { "activity_experience" },
                new[] { "family_friendly", "social", "group_friendly", "couple_friendly", "photography", "local_favorite" },
                new[] { "Kriss Kross", "Kriss Kross Skate", "Kriss Kross Skating Rink Lagos", "KrissKross Skate", "Kriss Kross Skatium", "Kriss Kross Federal Palace", "Kriss Kross Roller Rink" }, "Exterior",
                Desc(
                    "Kriss Kross Skating Rink is a roller-skating rink in the grounds of the Federal Palace Hotel on Ahmadu Bello Way, Victoria Island. It bills itself as Victoria Island's social skating destination: an open-air rink with a bright blue floor where kids, teens and adults skate, take lessons and hold parties. The rink previously operated in Lekki Phase 1 and moved to the Federal Palace Hotel in 2026.\n\nWhat to Do: Skate a session on rental or your own skates, take a one-hour class with an instructor if you're a beginner, or book the rink for a birthday, social or corporate event.",
                    "For Lagos families and young adults it's a newer kind of hangout — a place to learn to skate, film a few clips and spend an afternoon outdoors on the Island. Google reviewers often mention helpful staff and patient instructors, and it's popular with children.",
                    "Address: Federal Palace Hotel and Casino, 6-8 Ahmadu Bello Way, Victoria Island, Lagos.\n\nGetting There: Take a cab or ride-hail to the Federal Palace Hotel on Ahmadu Bello Way. From Ikoyi or Lekki, allow for Island traffic, especially on weekday evenings.\n\nOld Address: Some guides still list the rink's earlier site on Hakeem Dickson Link Road, Lekki — that address is out of date.",
                    "Hours: Tuesday–Thursday 12 noon–9:00 PM; Friday–Sunday 10:00 AM–10:00 PM; closed Mondays (website and Google listing, 2026) — confirm before you go.\n\nPrices (reported, older Lekki-era guide): Adult admission (13+) ₦15,000; kids (12 and under) ₦10,000; skate rental ₦5,000; one-hour class ₦25,000 (adults) or ₦20,000 (kids); group bundles for 4 or 8 people. The rink posted a new 2026 price list, and a 2026 reviewer mentioned a half-price Wednesday deal — confirm current prices.\n\nWaiver: Every skater signs the online Skating Agreement (linked from krisskrossskate.com); a parent or guardian signs for children.\n\nChildren: Children 12 and under must be supervised by an adult.\n\nWhat to Wear: Comfortable clothes and socks — thick socks if you're renting skates.\n\nContact: Phone 0916 337 9075; Instagram @krisskrossskate; website krisskrossskate.com. Book events through the rink.",
                    "Falls Happen: Skating has a real risk of falls and collisions — beginners should take a class and stay near the rail. Protective gear isn't mentioned in published rules, so ask about knee pads and helmets for children.\n\nWeather: The rink is outdoors or semi-outdoor; rain makes the surface slippery and can interrupt sessions.\n\nOn Camera: The rink films for its social media; tell staff if you don't want to appear.\n\nOld Address & Prices: Many online guides still show the Lekki address and older prices — check the rink's Instagram or call first.\n\nWeekend Crowds: Weekends and holidays are the busiest times.",
                    "Admission: About ₦15,000 per adult / ₦10,000 per child (reported; confirm the 2026 list).\nSkate Rental: About ₦5,000.\nOne-Hour Class: About ₦20,000–₦25,000.\nFood & Drinks: Extra — on-site food has been limited; confirm what's available.\nRide-hail to Victoria Island: Extra.",
                    new[]
                    {
                        "Terra Kulture: Stage plays, an art gallery and a Nigerian restaurant on Tiamiyu Savage Street, Victoria Island.",
                        "The Rube: Floral-arranging sessions on Adeyemo Alakija Street, Victoria Island.",
                        "Kayak In Lagos: Kayaking on the Lagos Lagoon from Foreshore Road, Victoria Island."
                    },
                    "Year-round in dry weather; weekday afternoons are quieter, and the dry season (roughly November to March) has fewer rain interruptions.",
                    "Medium (5/10) — busier at weekends, holidays and during events.",
                    "Rating: 4/10 — a flat rink inside a hotel compound, but skating itself needs balance and mobility; spectators can watch from the side.",
                    "1.5 to 2 hours.")),

            // Zen Archery — founder: [archery]. Sources: zenarchery.club (home: intro pass, kids 8+, groups of 5+,
            // FAQ), zenarchery.club/book-session (Nov 2026: hourly slots 10 AM–6 PM, max 10 shooters per slot,
            // adults ₦15,000, children 7–15 ₦7,500, barebow at 9 m, dress code, cancellation policy), Google Maps
            // listing (Teslim Balogun Stadium, Surulere; phone 0913 478 8226; 4.7 from 18 reviews; review themes:
            // patient coaches, fun, learning), premiumtimesng.com/sports/nigeria-sports-news/790627-... (Apr 2025:
            // Lagos Zen Archery Arena opened inside Teslim Balogun Stadium with the Lagos State Sports Commission;
            // founder Emmanuel Oyeleke), thelagosweekender.com/?p=9955 (Mar 2025: Instagram @zenarchery.lagos,
            // ₦15,000 entry), news coverage of the Zen Archery Lagos International Open at Onikan (2025, 2026).
            // Coordinates: Google Maps place pin (6.4997725, 3.360834), on the Teslim Balogun Stadium grounds.
            // AvgCost: ~US$18 ≈ ₦24,000 at ~₦1,360 per US$ (Nov 2026) — one adult intro session, a drink and a ride.
            new("lagos", ZenArcheryName, Nigeria, Lagos, LagosLangs, 6.4998, 3.3608,
                18m, "Car", 2,
                new[] { "activity_experience" },
                new[] { "social", "group_friendly", "family_friendly", "couple_friendly", "educational", "hidden_gem" },
                new[] { "Zen Archery Club", "Zen Archery Lagos", "Zen Archery Club Lagos", "Lagos Zen Archery Arena", "Zen Archery Arena", "Zen Archery Surulere", "Zen Archery Teslim Balogun" }, "Exterior",
                Desc(
                    "Zen Archery is an archery club and public range at Teslim Balogun Stadium in Surulere, founded by Emmanuel Oyeleke, a Nigerian national archery champion. In April 2025 the club and the Lagos State Sports Commission opened the Lagos Zen Archery Arena in a disused space inside the stadium, and the club runs the Zen Archery Lagos International Open championship. Beginners are welcome: you book a one-hour lane, and a coach fits your bow and gives you a safety briefing before you shoot.\n\nWhat to Do: Book a one-hour intro session with all equipment and coaching included, bring friends or family (group rates from five people), or join as a member for regular training and competitions.",
                    "Archery is a growing sport in Lagos, and Zen Archery is at the centre of it — from first-timers to national-team archers. Google reviewers describe patient, knowledgeable coaches and a fun, calm experience, and say it takes more strength than it looks.",
                    "Address: Teslim Balogun Stadium, Surulere, Lagos (on the Mainland, next to the National Stadium).\n\nGetting There: Take a cab or ride-hail to Teslim Balogun Stadium. From Victoria Island or Ikoyi, cross to the Mainland via Eko Bridge or the Third Mainland Bridge — allow 30–60 minutes depending on traffic. The stadium is also beside the BRT corridor on the Ikorodu Road / Western Avenue side.",
                    "Booking: Book a lane online at zenarchery.club/book-session — choose a date and a one-hour slot (10:00 AM to 6:00 PM starts); a maximum of 10 shooters per slot.\n\nPrices (Nov 2026): Adults ₦15,000; children (ages 7–15) ₦7,500; ₦10,000 per person for groups of 5 or more (website). Kids' membership is ₦25,000 a month.\n\nIncluded: Barebow equipment, arrows, quiver, finger tab, arm guard, a target lane with paper target faces, and a range officer's safety briefing. Walk-in sessions shoot at a 9-metre line.\n\nAge: The website says suitable for adults and kids aged 8+, while the booking page prices children from 7 — confirm for young children.\n\nWhat to Wear: Comfortable athletic clothes and closed-toe flat shoes (sneakers); avoid loose jewellery and baggy sleeves.\n\nCancellation: Free changes or refunds 24 hours ahead (48 hours for groups of 5+); later cancellations are non-refundable but can be rescheduled once; arriving more than an hour late counts as a no-show.\n\nContact: Phone/WhatsApp +234 913 478 8226; email zenarcheryng@gmail.com; Instagram @zenarchery.lagos.",
                    "Follow the Range Officer: Only shoot or collect arrows when told to — this is a real sport with real arrows.\n\nWeather: The range can close for weather or safety; sessions are then rescheduled or refunded.\n\nTraffic: Surulere is a long drive from Lekki at rush hour — book a mid-morning or weekend slot.\n\nBe on Time: Arriving more than an hour late forfeits your session.\n\nBig Events: Tournament days may affect normal sessions — check the club's events page.",
                    "Intro Session: ₦15,000 per adult; ₦7,500 per child (7–15); ₦10,000 per person for groups of 5+.\nExtra Sessions or Membership: Extra — see the club's membership page.\nDrinks & Snacks: Extra — bring water.\nRide-hail to Surulere: Extra.",
                    new[]
                    {
                        "National Museum Lagos: Nigerian art and history at Onikan, Lagos Island.",
                        "Freedom Park Lagos: A former colonial prison turned arts park on Lagos Island."
                    },
                    "Year-round in dry weather; mornings and late afternoons are cooler, and the dry season (roughly November to March) has the fewest rain closures.",
                    "Low to Medium (3/10) — at most 10 shooters per hourly slot.",
                    "Rating: 5/10 — a stadium range on flat ground; confirm step-free access and adaptive equipment with the club (it has entered para-archery events).",
                    "1 to 1.5 hours, plus travel.")),

            // The Rube — founder: [customised flower bouquets — a bouquet-making experience?]. Confirmed: it is a
            // hands-on floral-arranging ("floral bonding") studio. Sources: therube.ng (home: address, phone
            // 0703 396 7194, Instagram @the.rubexperience, packages), therube.ng/about/ (Dr Promise Ovai
            // Kenneth-Odum, CFD), therube.ng/packages-pricing/ (Nov 2026 prices; outdoor 1 h 30 + 30 min setup,
            // 10–50 people, extra hour ₦30,000 pp, custom events 3–5 working days' notice), Google Maps listing
            // (3/5 Adeyemo Alakija St, VI; florist; opens 9 AM; 5.0 from 9 reviews — themes: ambience, beautiful
            // experience, fresh flowers, patient host "Dr Rukkie", bonding), getyourguide.com + expedia
            // ("Lagos: Immersive Floral Bonding Experience", 1 h 30; meeting point opposite Union Bank / Fortune
            // Towers, Victoria Island). Note: one Google review is by a "Kenneth Odum" (same surname as the founder).
            // Coordinates: Google Maps place pin (6.4349963, 3.4250868).
            // AvgCost: ~US$66 ≈ ₦90,000 at ~₦1,360 per US$ (Nov 2026) — Standard ₦85,000 session plus a ride.
            new("lagos", RubeName, Nigeria, Lagos, LagosLangs, 6.4350, 3.4251,
                66m, "Car", 1,
                new[] { "activity_experience" },
                new[] { "couple_friendly", "group_friendly", "relaxing", "social", "premium", "photography" },
                new[] { "The Rube Experience", "Rube Experience", "The Rube Lagos", "The Rube Victoria Island", "The Rube Bloom Bar", "Rube Floral Experience", "The Rube Floral Bonding Experience" }, "Interior",
                Desc(
                    "The Rube is a floral studio on Adeyemo Alakija Street, Victoria Island, that runs hands-on \"floral bonding\" sessions: you pick fresh flowers and, with a tutor, arrange your own hand-tied bouquet or vase arrangement, then take it home. It is led by floral designer Dr Promise Ovai Kenneth-Odum, and sessions come with drinks and snacks in a relaxed, hosted setting.\n\nWhat to Do: Book a session alone, as a couple or with friends — a birthday, bridal shower, anniversary or team outing — and leave with a bouquet you made yourself. There is also a kids' session, outdoor sessions for groups and fully styled private events.",
                    "It's a calm, creative alternative to a brunch or lounge outing on the Island, popular for celebrations and girls' days out. The few reviews so far praise the ambience, the fresh flowers, a patient host and the chance to bond with other guests.",
                    "Address: 3/5 Adeyemo Alakija Street, Victoria Island, Lagos (the website writes \"3/5 Lateef Jakande Adeyemo Alakija\"). Tour listings place it opposite Union Bank and Fortune Towers.\n\nGetting There: Take a cab or ride-hail to Adeyemo Alakija Street, Victoria Island.",
                    "Packages (indoor, per person, Nov 2026): The Petal Experience (Standard) ₦85,000 — hand-tied bouquet, tutor-guided, soft drinks/wine/tea/coffee and light snacks. Premium Petals (VIP) ₦110,000 — hand-tied or vase arrangement, champagne/cocktails/mocktails and small chops. Grand Floral (VVIP) from ₦140,000 — led by the lead floral designer, maxi bouquet, canapés and gifts. Little Sprouts (Kids, ages 4–12, with an adult) ₦50,000. A take-home bouquet is included in each.\n\nOutdoor Sessions: Regular ₦87,000, VIP ₦115,000 or VVIP ₦150,000 per person; 1 hour 30 minutes plus 30 minutes of set-up; 10 to 50 guests.\n\nDuration: About 1.5 hours (tour listings).\n\nBooking: Book through the form at therube.ng, or message on WhatsApp/phone 0703 396 7194; Instagram @the.rubexperience. Custom events need 3–5 working days' notice.",
                    "Pricey: This is a premium experience — the standard session is ₦85,000 per person.\n\nBook Ahead: It runs by booking, not walk-in; confirm the time and group size first.\n\nFew Reviews: Ratings rest on a handful of Google reviews — check recent Instagram posts.\n\nDeposit & Cancellation: Not published — ask before you pay.",
                    "Standard Session: ₦85,000 per person.\nVIP / VVIP: ₦110,000 / from ₦140,000 per person.\nKids' Session: ₦50,000 per child.\nOutdoor Extra Hour: ₦30,000 per person per hour.\nRide-hail within Victoria Island: Extra.",
                    new[]
                    {
                        "Terra Kulture: Stage plays, an art gallery and a Nigerian restaurant on Tiamiyu Savage Street, Victoria Island.",
                        "Kriss Kross Skating Rink: Roller skating at the Federal Palace Hotel, Victoria Island.",
                        "Kayak In Lagos: Kayaking on the Lagos Lagoon from Foreshore Road, Victoria Island."
                    },
                    "Year-round (indoors); weekday daytime sessions are calmest. Popular around Valentine's Day, Mother's Day and wedding season — book early then.",
                    "Low (2/10) — small, booked sessions.",
                    "Rating: 6/10 — a seated indoor activity; confirm step-free access to the studio.",
                    "1.5 to 2 hours.")),

            // Terra Kulture — founder: [stage plays]. Official spelling confirmed "Terra Kulture". Sources:
            // app.terrakulture.com (redirect from terrakulture.com: Plot 1376 Tiamiyu Savage St, VI; phone
            // +234 810 422 4137; info@terrakulture.com; Arena, restaurant, gallery; ITORO! The Musical 26 Dec 2026–
            // 10 Jan 2027, 3 PM & 7 PM, from ₦20,000), tickets.terrakulture.com/tickets/events/i-wan-blow (Terra
            // Kulture Lawn, 21 Nov–13 Dec 2026, Sat & Sun 5:30 PM & 7:30 PM; Regular ₦25,000, Couple ₦35,000 for 2,
            // VIP ₦50,000; QR tickets by email), en.wikipedia.org/wiki/Terra_Kulture (2003, Bolanle Austen-Peters;
            // 450-seat Terra Kulture Arena; restaurant, bookstore, gallery, Yoruba/Igbo/Hausa language classes;
            // Taruwa festival; TAFTA), Google Maps listing (4.5 from 2,728 reviews; opens 9 AM; review themes:
            // stage plays, art gallery, theatre, food, bookshop). Hours "Mon–Sun 9 AM–8 PM" per the official site
            // as quoted in a search result — not seen directly; show times run later.
            // Coordinates: Google Maps place pin (6.4251323, 3.4268301).
            // AvgCost: ~US$33 ≈ ₦45,000 at ~₦1,360 per US$ (Nov 2026) — a ₦25,000 play ticket, a meal and a ride.
            new("lagos", TerraKultureName, Nigeria, Lagos, LagosLangs, 6.4251, 3.4268,
                33m, "Car", 1,
                new[] { "entertainment_nightlife", "museum_gallery", "food_experience" },
                new[] { "art", "cultural", "local_favorite", "couple_friendly", "food_focused", "best_evening" },
                new[] { "Terra Kulture Arena", "Terra Culture", "Terrakulture", "Terra Kulture Lagos", "Terra Kulture Victoria Island", "Terra Kulture Theatre", "Terra Kulture Restaurant" }, "Exterior",
                Desc(
                    "Terra Kulture is an arts and culture centre on Tiamiyu Savage Street, Victoria Island, founded in 2003 by lawyer and theatre producer Bolanle Austen-Peters. It is best known for stage plays and musicals at the Terra Kulture Arena, a 450-seat theatre, and on its outdoor lawn. The centre also has a restaurant serving Nigerian food, a bookshop, an art gallery of contemporary African art, and classes in Yoruba, Igbo and Hausa.\n\nWhat to Do: See a play or musical, browse the gallery and bookshop, and eat Nigerian dishes at the restaurant before the show.\n\nCurrently Showing (Nov 2026): \"I Wan Blow\", an interactive comedy on the Terra Kulture Lawn (weekends, 21 November–13 December 2026), and \"ITORO! The Musical\" at the Arena (26 December 2026–10 January 2027).",
                    "For many Lagosians, Terra Kulture is the home of Lagos theatre — the place for a date night or a family outing to a play. Reviewers often recommend combining the bookshop, gallery, restaurant and a show in one visit.",
                    "Address: Plot 1376, Tiamiyu Savage Street, off Ahmadu Bello Way, Victoria Island, Lagos.\n\nGetting There: Take a cab or ride-hail to Tiamiyu Savage Street, Victoria Island. On show nights, arrive early — Victoria Island traffic and parking can be slow.",
                    "Tickets: Buy online at tickets.terrakulture.com (Terra Tickets); QR tickets are emailed instantly and scanned at the gate.\n\nPrices (examples): I Wan Blow — Regular ₦25,000, Couple ₦35,000 (admits 2), VIP ₦50,000. ITORO! The Musical — from ₦20,000. Prices vary by production.\n\nShow Times: Typically weekend matinées and evenings (for example, I Wan Blow: Saturdays and Sundays, 5:30 PM and 7:30 PM; ITORO!: 3:00 PM and 7:00 PM).\n\nHours: The centre opens daily from 9:00 AM (reported 9:00 AM–8:00 PM); shows may finish later.\n\nContact: Phone +234 810 422 4137; email info@terrakulture.com; website terrakulture.com.",
                    "Book Early: Popular productions and festive-season musicals sell out — buy tickets in advance.\n\nCheck the Venue: Some shows are on the outdoor Lawn rather than in the Arena — check your ticket, and bring something for the mosquitoes outdoors.\n\nNo Show Every Day: Plays run in seasons, mostly at weekends — check what's on before you go.\n\nTraffic: Leave extra time on weekday evenings.",
                    "Play Ticket: About ₦20,000–₦50,000 per person depending on the show and tier.\nRestaurant Meal: Extra.\nBooks & Art: Extra.\nRide-hail within Victoria Island: Extra.",
                    new[]
                    {
                        "The Rube: Floral-arranging sessions on Adeyemo Alakija Street, Victoria Island.",
                        "Kriss Kross Skating Rink: Roller skating at the Federal Palace Hotel, Victoria Island.",
                        "Nike Art Gallery: A huge four-storey gallery of Nigerian art in Lekki."
                    },
                    "During a play season — usually weekends; the December–January festive season brings big musicals.",
                    "Medium (5/10) — busy before and after shows.",
                    "Rating: 6/10 — confirm step-free access to the Arena and accessible seating when booking.",
                    "3 to 4 hours for a meal and a show.")),

            // CeraCerni's Art Hub — founder: [paint and sip, pottery, "shoot and spill", tufting]. Spelling: the
            // hub's own site and Google listing write "CeraCerni's Art Hub" (also "Ceracerni's ArtHub", "Cera
            // Cerni's Art Hub"); Instagram @ceracerni. "Shoot and spill" is not a product name — the hub has
            // "Shoot 'ur' Shot" (air-gun paint-balloon splatter) and "Spin 'n' Spill" (spin painting); the
            // founder's phrase appears to merge the two, so both are listed. Sources: ceracerniarthub.com (Nov 2026:
            // all Artventures, prices, ages, durations; inclusions; hours; address; phone +234 806 117 6101;
            // info@ceracerniarthub.com; book online with a deposit; walk-ins; halls ₦300,000–₦1,000,000; opened 2021),
            // Google Maps listing (65b Akintunde A. Adeyemi Dr, Lekki Phase 1; 4.7 from 246 reviews; themes:
            // painting, pottery, sip and paint, hand sculpture; a 2026 review mentions sip and paint ₦15k and
            // hand-in-hand ₦40k), thisdaylive.com/2025/11/23/an-art-space-where-lagos-lets-its-imagination-breathe
            // (presided over by muralist Sarah Adenike Sanni; operating since Dec 2021; room names),
            // brittanyrose.substack.com/p/nigerian-art-class-review (Feb 2025: house with no signage, AC out in some
            // rooms, slow service, delays; ponchos and snacks provided), bushdrum.com venue page (tufting 14+).
            // Coordinates: Google Maps place pin (6.4419939, 3.480569).
            // AvgCost: ~US$24 ≈ ₦32,000 at ~₦1,360 per US$ (Nov 2026) — one ₦20,000–₦25,000 session, café extra
            // and a ride.
            new("lagos", CeraCerniName, Nigeria, Lagos, LagosLangs, 6.4420, 3.4806,
                24m, "Car", 1,
                new[] { "activity_experience" },
                new[] { "art", "couple_friendly", "group_friendly", "family_friendly", "social", "quirky", "photography" },
                new[] { "CeraCerni", "Ceracerni", "Cera Cerni", "CeraCerni Art Hub", "Ceracerni's ArtHub", "Cera Cerni's Art Hub", "CeraCerni's Art Hub Lekki", "CeraCerni ArtHub", "Ceracerni Paint and Sip" }, "Interior",
                Desc(
                    "CeraCerni's Art Hub is a creative studio, café and gallery in a converted house in Lekki Phase 1, opened in 2021 by Cera Cerni, a mural and aerosol-art company. Every room is covered in murals and humorous artworks, and you can book instructor-led \"Artventures\" — no skill needed — and take home what you make.\n\nWhat to Do: Sip and paint a canvas, throw a pot on the wheel, tuft a rug, try Shoot 'ur' Shot (pop paint-filled balloons with an air gun onto a canvas) or Spin 'n' Spill (spin painting), pedal-powered Art Cycle painting, Drum Roll (drumming plus painting), ceramic painting, couples' hand casting or body casting. The Palette café serves food and drinks.",
                    "It's one of Lagos's best-known creative date and birthday spots, popular with couples, friends, teams and families. Google reviewers praise the colourful, often hilarious murals, a tour of the gallery and fun, patient instructors.",
                    "Address: 65B Akintunde Adeyemi Drive, off Adewole Kuku Street, Lekki Phase 1, Lagos — a quiet residential street; one visitor noted there is no sign outside, so call if you can't find the gate.\n\nGetting There: Take a cab or ride-hail to the address, off Admiralty Way / Freedom Way near Purple Lekki.",
                    "Hours: Monday–Saturday 11:00 AM–9:00 PM; Sunday 1:00 PM–9:00 PM.\n\nBooking: Book online at ceracerniarthub.com (pay a deposit; instant confirmation). Walk-ins are accepted, but booking is safer at weekends. Phone +234 806 117 6101; email info@ceracerniarthub.com; Instagram @ceracerni.\n\nPrices (website, Nov 2026): Sip and Paint ₦20,000 per canvas (about 2 hours; adults & 5+). Pottery wheel ₦25,000 (2 hours; 5+). Table moulding ₦20,000. Ceramic painting ₦15,000. Drum Roll ₦15,000. Art Cycle ₦30,000 (12+). Spin 'n' Spill ₦45,000 (1 hour; 5+). Shoot 'ur' Shot from ₦60,000 for a medium canvas (1 hour; 18+). Tufting from ₦100,000 for a small rug (3.5 hours; 14+). Hand-in-Hand couples' sculpture ₦25,000–₦110,000. Body casting ₦40,000–₦250,000 (most 18+).\n\nIncluded: A complimentary drink and small chops, an instructor, and the piece you make (pottery may be ready after firing).\n\nGroups: 2 to 50 guests; private halls from ₦300,000.",
                    "Heat: A 2025 visitor reported the air-conditioning not working in some rooms — ask before booking a long session.\n\nWaits: The same visitor reported slow service and delays — book a slot and arrive on time.\n\nAge Limits: Several activities have minimum ages (Art Cycle 12+, tufting 14+, Shoot 'ur' Shot 18+) — check before bringing children.\n\nGet Messy: Paint activities can splatter — wear clothes you don't mind ruining (ponchos have been provided).",
                    "Sip and Paint: ₦20,000 per canvas.\nPottery Wheel: ₦25,000.\nShoot 'ur' Shot / Spin 'n' Spill: ₦60,000+ / ₦45,000.\nTufting: From ₦100,000.\nCafé Food & Extra Drinks: Extra.\nRide-hail to Lekki Phase 1: Extra.",
                    new[]
                    {
                        "Lekki Arts & Crafts Market: Nigerian crafts and souvenirs in Lekki Phase 1.",
                        "Nike Art Gallery: A huge four-storey gallery of Nigerian art in Lekki.",
                        "Lekki Conservation Centre: A canopy walkway and nature trails further along the Lekki–Epe Expressway."
                    },
                    "Year-round (indoors); weekday afternoons are quietest, and weekends fill up with birthdays and groups.",
                    "Medium (5/10) — busier at weekends and during private events.",
                    "Rating: 5/10 — a converted two-storey house; the exhibition space is upstairs, so confirm ground-floor activities.",
                    "2 to 3 hours (tufting about 3.5 hours).")),

            // Kayak In Lagos — founder: [Kayak Lagos, Victoria Island — kayaking]. The Victoria Island operator is
            // "Kayak In Lagos" (Instagram @kayakinlagos; WhatsApp catalogue name "Kayak Thrills"). Sources: Google
            // Maps listing (Foreshore Rd, VI; canoe & kayak rental; opens 9 AM; phone 0813 835 4390; 4.7 from 34
            // reviews; themes: first-time kayaking, bring your own food and drinks, safety gear, serene atmosphere,
            // team bonding), instagram.com/kayakinlagos (bio: kayaking, jet ski, boat cruise, team bonding, trips;
            // Victoria Island; DM to book), linktr.ee/kayakinlagos (WhatsApp +234 813 835 4390; rental, Lagos Fun
            // Tour, team-bonding form), guardian.ng "Go kayaking in Lagos" (via search result: Foreshore Road, Lagos
            // Lagoon, "confirm price before booking"; group sessions on 2nd and 4th weekends — UNVERIFIED),
            // medium.com/@plazzaaHQ/kayaking-in-lagos (Aug 2024, via search result: ₦7,500, weekends & public
            // holidays 9 AM–6 PM, same phone, older Lekki address on Farm Avenue/Admiralty Way). Prices not published.
            // Coordinates: Google Maps place pin (6.4368408, 3.4489475), on Foreshore Road by the lagoon.
            // AvgCost: ~US$13 ≈ ₦17,000 at ~₦1,360 per US$ (Nov 2026) — a kayak session (assumed ~₦10,000, since the
            // last published price was ₦7,500 in 2024) plus a ride; rough guess.
            new("lagos", KayakInLagosName, Nigeria, Lagos, LagosLangs, 6.4368, 3.4489,
                13m, "Car", 2,
                new[] { "activity_experience", "nature_outdoor" },
                new[] { "adventurous", "group_friendly", "social", "photography", "hidden_gem", "nature" },
                new[] { "Kayak Lagos", "Kayak in Lagos", "Kayaking In Lagos", "KayakInLagos", "Kayak Thrills", "Kayak In Lagos Victoria Island", "Kayak In Lagos Foreshore Road" }, "Landscape",
                Desc(
                    "Kayak In Lagos is a kayak and canoe rental and tours operator on Foreshore Road, Victoria Island, paddling on the Lagos Lagoon with views of the Ikoyi and Victoria Island skyline. Beginners are welcome — staff show first-timers the basics before they set off. The same team also arranges jet-ski rides, boat cruises, team-bonding days and group trips.\n\nWhat to Do: Rent a kayak for a paddle on the lagoon, book a team-bonding or birthday session for a group, or ask about their jet-ski and boat-cruise packages.",
                    "Lagosians describe it as a low-key, affordable way to get on the water and see the city from the lagoon. Google reviewers often mention that first-timers pick it up quickly, that safety gear is provided, that you can bring your own food and drinks, and that it works well for team bonding.",
                    "Address: Foreshore Road, Victoria Island, Lagos (on the lagoon side, near the Lekki–Ikoyi Link Bridge end of Victoria Island).\n\nGetting There: Take a cab or ride-hail to Foreshore Road, Victoria Island, and call or WhatsApp ahead for the exact launch point. Older guides list a Lekki address (Farm Avenue, Admiralty Way) — confirm where they're launching from.",
                    "Booking: Message on Instagram (@kayakinlagos) or WhatsApp/phone +234 813 835 4390; team bonding is booked through a form on their Linktree (linktr.ee/kayakinlagos).\n\nPrices: Not published — ask when you book. A 2024 guide reported ₦7,500 per session.\n\nHours: Google lists opening from 9:00 AM; a 2024 guide said weekends and public holidays, 9:00 AM–6:00 PM — confirm the days.\n\nNo Experience Needed: First-timers get a quick introduction; reviewers mention safety gear being provided — confirm life jackets for everyone.\n\nBring: Sunscreen, a hat, water, a change of clothes and a waterproof bag for your phone. You can bring your own food and drinks.",
                    "Life Jacket Always: Wear the life jacket for the whole session, and tell the team if you can't swim.\n\nSun & Heat: There's no shade on the water — go in the morning or late afternoon.\n\nTides & Wind: Conditions on the lagoon change — follow the guides and stay within the area they set.\n\nPrices & Days: Published information is thin and some of it is old — confirm price, days and launch point on WhatsApp before you go.\n\nGet Wet: Expect to get splashed; keep phones in a dry bag.",
                    "Kayak Session: Not published — reported ₦7,500 in 2024; confirm.\nJet Ski / Boat Cruise: Extra — ask for a quote.\nFood & Drinks: Bring your own or buy nearby.\nRide-hail within Victoria Island: Extra.",
                    new[]
                    {
                        "The Rube: Floral-arranging sessions on Adeyemo Alakija Street, Victoria Island.",
                        "Terra Kulture: Stage plays, an art gallery and a Nigerian restaurant on Tiamiyu Savage Street, Victoria Island.",
                        "Kriss Kross Skating Rink: Roller skating at the Federal Palace Hotel, Victoria Island."
                    },
                    "Morning or late afternoon in the dry season (roughly November to March), when the water is calmer and it's cooler; avoid stormy rainy-season days.",
                    "Low to Medium (4/10) — busier at weekends and during group bookings.",
                    "Rating: 3/10 — getting in and out of a kayak at the waterside needs balance and some mobility.",
                    "1 to 2 hours on the water.")),

            // National Museum Lagos — founder: [National Museum Lagos, Onikan, Lagos Island]. Sources:
            // en.wikipedia.org/wiki/National_Museum,_Lagos (founded 1957 by Kenneth Murray; NCMM; ~47,000 objects;
            // Nok Jemaa Head, Benin ivories, Igbo-Ukwu bronzes, Murtala Mohammed's car; reopened April 2026 after an
            // overhaul), vanguardngr.com/2026/04/echoes-of-the-past-a-timeline-of-antiquities-in-nigeria/ (15 Apr
            // 2026: refurbished gallery "Echoes of the Past: A Timeline of Antiquities in Nigeria", with IHS Towers;
            // chronological, clockwise route), ntawebsite.acan.group (4 Jun 2026: some wood/metal pieces can be
            // handled; photography rules relaxed in that gallery), Google Maps listing (Onikan Rd; opens 9 AM; phone
            // 01 263 6005; website museum.ng; 4.0 from 1,263 reviews; ~Aug 2026 review: adult entry ₦4,000 plus a
            // ₦200 charge, guides available, no photo restrictions; review themes: history of Nigeria, artifacts,
            // terracotta), wakaabuja.com/the-national-museum-lagos/ (hours Tue–Sun 9 AM–4 PM, closed Mon & public
            // holidays; guides ₦3,000–₦5,000 — low-confidence source), pulse.ng (address "Awolowo Rd, opposite MUSON
            // Centre, Onikan"). Hours conflict between sources (NCMM page reportedly Mon–Fri 9–4) — hedged.
            // Coordinates: Google Maps place pin (6.4442681, 3.4036542); matches Wikipedia's 6°26′40″N 3°24′12″E.
            // AvgCost: ~US$10 ≈ ₦13,000 at ~₦1,360 per US$ (Nov 2026) — ₦4,200 entry, a guide tip and a ride.
            new("lagos", NationalMuseumLagosName, Nigeria, Lagos, LagosLangs, 6.4443, 3.4037,
                10m, "Car", 2,
                new[] { "museum_gallery", "historical_tour" },
                new[] { "history", "cultural", "educational", "art", "budget_friendly" },
                new[] { "Nigerian National Museum", "National Museum Onikan", "National Museum, Lagos", "Nigerian National Museum Lagos", "Onikan Museum", "National Museum Lagos Island", "Lagos National Museum" }, "Exterior",
                Desc(
                    "The Nigerian National Museum at Onikan, Lagos Island, was founded in 1957 by the British archaeologist Kenneth Murray and is run by the National Commission for Museums and Monuments. Its collection of about 47,000 objects spans Nigeria's history: Nok terracottas, including the Jemaa Head, Igbo-Ukwu bronzes, Benin ivories and bronzes, Yoruba carvings, masks, textiles, Egungun costumes and musical instruments. It also displays the car in which head of state Murtala Mohammed was assassinated in 1976. In April 2026 a refurbished gallery reopened — \"Echoes of the Past: A Timeline of Antiquities in Nigeria\" — laid out in chronological order, with some objects you can touch.\n\nWhat to Do: Follow the timeline gallery from the Nok culture onwards, see the Benin and Igbo-Ukwu pieces and the Murtala Mohammed car, and hire a guide for the stories behind the objects.",
                    "For Nigerians it is the place to see the country's history in one building. Recent visitors say the 2026 renovation has made it far more engaging; reviews mention a quiet, clean space, good descriptions and the value of a guide.",
                    "Address: Onikan Road (Awolowo Road, opposite the MUSON Centre), Onikan, Lagos Island.\n\nGetting There: Take a cab or ride-hail to Onikan, Lagos Island. From Victoria Island or Ikoyi it's a short drive via Awolowo Road; from the Mainland, cross via Eko Bridge or the Third Mainland Bridge.",
                    "Entry: A 2026 visitor reported ₦4,000 per adult plus a ₦200 charge; older guides list lower or different fees — confirm at the gate.\n\nHours: Opens from 9:00 AM; usually reported as closing around 4:00 PM. Sources disagree on which days it opens (Tuesday–Sunday or Monday–Friday) — call or check before you go.\n\nGuides: Guides are available for detailed explanations, for an extra fee.\n\nPhotography: Allowed in most areas; rules were relaxed in the refurbished gallery — check at reception.\n\nContact: Website museum.ng (National Commission for Museums and Monuments); listed phone 01 263 6005.",
                    "Opening Days: Hours and days aren't consistently published — confirm before travelling, especially on public holidays.\n\nCash: Bring naira cash in case card payment isn't available.\n\nTraffic: Lagos Island traffic is heavy on weekdays — go mid-morning.\n\nUneven Displays: Some older galleries are less polished than the refurbished timeline gallery.",
                    "Entry: About ₦4,000 per adult plus ₦200 (2026 visitor report).\nGuide: Extra — agree the fee first.\nRide-hail to Onikan: Extra.",
                    new[]
                    {
                        "Freedom Park Lagos: A former colonial prison turned arts park, a few minutes away on Lagos Island.",
                        "Tafawa Balewa Square: The historic parade ground next to Onikan.",
                        "MUSON Centre: Lagos's concert hall, just across the road."
                    },
                    "Weekday mornings for a quiet visit; any time of year, as it's indoors.",
                    "Low (2/10) — busier with school groups on weekdays.",
                    "Rating: 5/10 — mostly ground-level galleries; confirm step-free access to every gallery with the museum.",
                    "1.5 to 2 hours.")),

            // ─── Lagos ─────────────────────────────────────────────────────────
            // Pitstop Lagos — founder: "salsa nights on Saturdays" (used as given).
            // Sources: pulse.ng/story/where-to-dance-salsa-lagos-2025090412545279682 (6 Sep 2025: Saturdays 7–10 PM,
            // free entry, live band, wellness/cycling café by day); italawa.com.ng/place/pitstop-lagos/ (92A Younis
            // Bashorun St; 0807 749 8324 incl. WhatsApp; pitstoplagos.com; hours 6:30 AM–11/11:30 PM, conflicting note
            // "closed Mondays"; salsa & karaoke nights; weekend cycling rides; bikes sold upstairs; ₦1,000 reservation
            // commitment fee; ₦5,000–₦20,000 per person); wakaabuja.com/learn-salsa-in-lagos/ (Latin dance classes);
            // eatdrinklagos.com review (2020: "N5000–N15000", parking for ~4–5 cars, @pitstoplagos, 0802 334 7887;
            // mixed review — weak cocktails, patchy service); foodieinlagos.com/restaurant-review-pitstop-lagos/
            // (Aug 2021: 8/10, prawns ₦4,000, seafood pasta ₦7,500, mocktail ₦2,500, 12.5% VAT+tax, limited street parking);
            // tripadvisor.com listing title "Pitstop Lagos, Victoria Island" (via search summary).
            // Coordinates: APPROXIMATE — OSM Nominatim centroid of Younis Bashorun Street (6.4284314, 3.4367259);
            // No. 92A itself is not mapped.
            // AvgCost: ~US$15 ≈ ₦20,400 at ~₦1,360 per US$ (Nov 2026) — a main, a drink and tax; salsa night is free.
            new("lagos", PitstopName, Nigeria, Lagos, LagosLangs, 6.4284, 3.4367,
                15m, "Car", 2,
                new[] { "food_experience", "entertainment_nightlife" },
                new[] { "nightlife", "music", "social", "food_focused", "couple_friendly", "local_favorite", "best_evening" },
                new[] { "Pitstop", "PitStop Lagos", "Pit Stop Lagos", "Pit Stop", "Pitstop VI", "Pitstop Victoria Island", "Pitstop Restaurant" }, "Interior",
                Desc(
                    "Pitstop Lagos is a cycling-themed café and restaurant on Younis Bashorun Street, Victoria Island. It bills itself as Lagos's first wellness- and cycling-inspired restaurant. By day it serves breakfast, coffee, smoothies and plates, and bikes are sold upstairs. In the evening there are cocktails and dinner, and on Saturday nights the room turns into a salsa night.\n\nWhat to Do: Come on a Saturday evening for salsa, with a live band and instructors who help beginners. On other days, come for brunch, a smoothie after a workout or dinner and cocktails. The venue also hosts karaoke nights, and its cycling community rides out at weekends.",
                    "Pitstop's Saturday salsa is one of the friendlier ways into Lagos's Latin-dance scene. It is more of a relaxed social night than a club party, and beginners are welcome.",
                    "Address: 92A Younis Bashorun Street, Victoria Island, Lagos (some listings give No. 92).\n\nGetting There: Take a cab or ride-hail. Parking is very limited (reviews mention room for only a few cars) so don't count on parking outside.",
                    "Salsa Night: Saturdays, 7:00–10:00 PM, free entry (per a September 2025 guide). Confirm on Instagram @pitstoplagos before you go.\n\nHours: Listed as about 6:30 AM to 11:00 PM. One listing says it is closed on Mondays and another shows it open, so confirm.\n\nReservations: Call or WhatsApp 0807 749 8324. A reservation form lists a ₦1,000 commitment fee. Website: pitstoplagos.com.\n\nDress: Wear shoes you can turn in. There is no published dress code.",
                    "Mixed Reviews: Some reviewers say the food and service are good. Others mention weak cocktails, slow or patchy service and food that was less spicy than ordered. Check your order and your bill.\n\nTax on the Bill: Expect VAT and service charges to be added to menu prices.\n\nSchedule Changes: Salsa nights depend on the venue's calendar, so confirm the Saturday session before you travel.\n\nParking: Street parking on Younis Bashorun is tight.",
                    "Salsa Night: Free entry (reported).\nFood & Drinks: Listed at about ₦5,000–₦20,000 per person. In 2021 a starter was ₦4,000, a seafood pasta ₦7,500 and a mocktail ₦2,500; prices have likely risen since.\nVAT & Tax: About 12.5% was added to the bill in 2021.\nReservation Commitment Fee: ₦1,000 (per listing).\nRide-hail: Extra.",
                    new[]
                    {
                        "Windsor Gallery Lagos: A contemporary African art gallery on Goriola Street, Victoria Island.",
                        "Freedom Park Lagos: A former colonial prison turned arts and events park on Lagos Island.",
                        "Tarkwa Bay Beach: A sheltered beach reached by boat from Victoria Island or Lagos Island."
                    },
                    "Saturday evening, from 7:00 PM, for salsa. Weekday mornings are good for brunch and coffee.",
                    "Medium (5/10) — busiest on Saturday salsa nights.",
                    "Rating: 6/10 — a street-level café; step-free access to the dance area and upstairs bike shop is not confirmed.",
                    "2 to 3 hours for salsa night; about 1 hour for a meal.")),

            // Windsor Gallery Lagos — founder: "Windor Gallery [art gallery]"; spelling confirmed as "Windsor Gallery".
            // Sources: thisdaylive.com/2023/09/14/windsor-gallery-makes-an-entree-into-art-scene-with-inaugural-exhibition
            // (opened 16 Sep 2023 with "State of Africa"; founder/CEO Richard Vedelego; Abidjan, Abuja, Lagos);
            // guardian.ng/?p=2217295 & businessday.ng (2023 debut — via search summary);
            // thisdaylive.com/index.php/2024/11/02/interweaving-nature-and-culture-in-dual-exhibition (Nov 2024 shows:
            // Duke Asidere & Mederic Turay; Didier Viode); themoveee.com (Tunde's "Inferno", 12 Jul–16 Aug 2025 — via
            // search summary); idoff.net/places/windsor-gallery-lagos (4c Goriola St; Tue–Sat 10 AM–7 PM, Sun & Mon closed;
            // free entry; +234 913 901 5506 incl. WhatsApp; lagos@windsor-gallery.com; air-conditioned, child-friendly);
            // instagram.com/windsor.gallery_lagos (bio via search summary: Tue–Thu by appointment, Fri–Sat 10–7, Sun 2–7);
            // africanscolumn.com (Lagos Gallery Weekend, 4–8 Mar 2026, participant) and guardian.ng/?p=2954995 (ART X Lagos
            // 2026 Spotlight) — both via search summary, showing it is active in 2026.
            // Coordinates: APPROXIMATE — OSM Nominatim centroid of Goriola Street, Victoria Island (6.4318982, 3.4133947);
            // No. 4c itself is not mapped.
            // AvgCost: ~US$5 ≈ ₦6,800 at ~₦1,360 per US$ (Nov 2026) — entry is free; covers a ride-hail and a drink nearby.
            new("lagos", WindsorGalleryName, Nigeria, Lagos, LagosLangs, 6.4319, 3.4134,
                5m, "Car", 1,
                new[] { "museum_gallery", "cultural_site" },
                new[] { "art", "cultural", "relaxing", "budget_friendly", "good_short_visit", "solo_friendly", "photography" },
                new[] { "Windsor Gallery", "Windsor Gallery (Lagos)", "Windsor Gallery Victoria Island", "Windsor Art Gallery Lagos", "Windor Gallery", "Windsor Gallery Goriola" }, "Interior",
                Desc(
                    "Windsor Gallery Lagos is a contemporary African art gallery on Goriola Street, Victoria Island. It opened in September 2023 with the group show \"State of Africa\" and is part of a small network founded by Richard Vedelego, with galleries in Abidjan and Abuja. It shows painting, sculpture and installation by established and emerging African artists from Nigeria, Côte d'Ivoire and beyond, in a minimalist white-walled space.\n\nWhat to Do: See the current exhibition. Past shows include Duke Asidere and Mederic Turay's \"The Eighth Continent\" (2024), Tunde's \"Inferno\" (2025) and the 2025 group show \"In Form and Feature, Face and Limb\". You can also ask staff about the works or about buying. The gallery takes part in Lagos Gallery Weekend and ART X Lagos.",
                    "Windsor Gallery is one of the newer names on Victoria Island's gallery circuit and is known for showing Ivorian and Nigerian artists side by side. It is a calm, air-conditioned stop between brunch and the beach.",
                    "Address: 4c Goriola Street, Victoria Island, Lagos.\n\nGetting There: Take a cab or ride-hail to Goriola Street. It is a short drive from most Victoria Island hotels.",
                    "Hours: Listed as Tuesday–Saturday, 10:00 AM–7:00 PM, closed Sunday and Monday. The gallery's Instagram has shown different hours (Tuesday–Thursday by appointment, Friday–Saturday 10–7, Sunday 2–7 PM), so confirm before you go.\n\nEntry: Free.\n\nContact: Phone/WhatsApp +234 913 901 5506; email lagos@windsor-gallery.com; Instagram @windsor.gallery_lagos.\n\nExhibitions: Shows change every few weeks and there may be gaps between them. Check what's on before you visit.",
                    "Between Shows: The gallery may be closed or partly hung while exhibitions are being installed, so check first.\n\nAppointments: Some weekdays may be by appointment only. Message ahead.\n\nPhotography: Ask before photographing the artworks.",
                    "Entry: Free.\nArtworks: For sale. Prices are on request.\nRide-hail within Lagos: Extra.",
                    new[]
                    {
                        "Pitstop Lagos: A cycling-themed café on Younis Bashorun Street with salsa on Saturday nights.",
                        "Nike Art Gallery: A large, multi-floor gallery of Nigerian art in Lekki.",
                        "Freedom Park Lagos: An arts and events park on Lagos Island."
                    },
                    "Tuesday to Saturday during gallery hours. The best times are an opening night or Lagos Gallery Weekend (March) and ART X Lagos (usually November), when the city's art scene is most active.",
                    "Low (2/10) — busier on opening nights.",
                    "Rating: 6/10 — a small indoor gallery; step-free access is not confirmed, so ask the gallery.",
                    "30 minutes to 1 hour.")),

            // Seal Team Paintball — founder: "paintball".
            // Sources: sealteampaintball.com (home: Trinity Ave, inside ATV World, Eti-Osa / Oniru; Standard 25 pellets
            // ₦8,000 online / ₦8,500; Premium 100 pellets ₦11,000 online / ₦11,500; marker, free air, mask, body armour
            // and overalls included; bar & grill; Paystack booking); sealteampaintball.com/about.html ("newest speedball
            // arena"; group/corporate/bachelor rates; tournament-standard course); /contact.html (+234 702 500 7607
            // call/WhatsApp, admin@sealteampaintball.com, @sealteampaintball_ng); /rules.html (masks on at all times,
            // barrel socks ₦500/day, no cash payments, pay at reception, referee final, no close-range shots, ejection
            // without refund); placestotour.com/places-to-play-paintball-game-in-lagos-nigeria/ (Seal Team Package ₦14,500
            // = 100 pellets + 1 ATV lap; beach entrance fee may apply; no outside food/drink except party area with
            // corkage; birthday person plays free with 9+ paying friends); instagram.com/sealteampaintball_ng (~29K
            // followers — via search summary).
            // Coordinates: APPROXIMATE — OSM Nominatim point for Trinity Avenue, Maroko/Oniru (6.4242889, 3.4524413);
            // ATV World / the arena is not mapped.
            // AvgCost: ~US$15 ≈ ₦20,400 at ~₦1,360 per US$ (Nov 2026) — Premium package, a drink/grill snack and a ride.
            new("lagos", SealTeamName, Nigeria, Lagos, LagosLangs, 6.4243, 3.4524,
                15m, "Car", 2,
                new[] { "activity_experience" },
                new[] { "adventurous", "group_friendly", "social", "sports_fan", "local_favorite" },
                new[] { "Seal Team Paintball Lagos", "Seal Team Paintball Oniru", "SealTeam Paintball", "Sealteam Paintball NG", "Seal Team Paintball Arena", "Seal Team Paintball ATV World" }, "Exterior",
                Desc(
                    "Seal Team Paintball is a speedball-style paintball arena inside ATV World on Trinity Avenue, Oniru (Eti-Osa), Lagos. Teams play short, fast rounds on a course built to tournament standard, with inflatable bunkers. A bar and grill with seating sits beside the arena, so groups often make an afternoon of it.\n\nWhat to Do: Play team rounds with friends, colleagues or a birthday group. Upgrade to more pellets, or add an ATV lap with the Seal Team Package. The venue also hosts team-building days, bachelor parties and other group events.",
                    "Paintball is a popular group outing in Lagos for birthdays, office team-bonding and stag days. Seal Team is one of the better-known arenas on the Victoria Island and Oniru side, with a large Instagram following.",
                    "Address: Trinity Avenue, inside ATV World, Oniru, Eti-Osa, Lagos.\n\nGetting There: Take a cab or ride-hail and tell the driver ATV World, Trinity Avenue, Oniru. If the arena is reached through a beach gate, a separate beach entry fee may apply.",
                    "Booking: Book online on sealteampaintball.com (paid via Paystack, slightly cheaper than walk-in) or call/WhatsApp +234 702 500 7607. Email admin@sealteampaintball.com; Instagram @sealteampaintball_ng.\n\nPackages (per player): Standard has 25 pellets and Premium has 100 pellets. A Seal Team Package with 100 pellets and an ATV lap has also been listed. Every package includes the marker, air refills and rental of a face mask, body armour and overalls.\n\nPayment: No cash. Pay at reception before you gear up.\n\nGroups: Group, corporate and party rates are available on request. One listing says the birthday person plays free with at least nine paying friends, so confirm.\n\nHours & Age Limit: Not published. Call ahead, and ask about a minimum age before bringing children.\n\nWhat to Wear: Old clothes and closed shoes you can run in. Overalls are provided.",
                    "Masks Stay On: Keep your mask on at all times inside the arena, even after you're hit. Paintballs can injure eyes.\n\nIt Stings: Hits can bruise. Wear layers, and stay out of point-blank range, which the rules ban anyway.\n\nRules Are Strict: The referee's decision is final. Shooting after you're hit, or after the whistle, can get you and a teammate ejected without a refund.\n\nDamage Charges: Throwing or slamming the rental guns can be charged.\n\nOutside Food: Reportedly not allowed except in the party area, where corkage applies.",
                    "Standard (25 pellets): ₦8,000 online / ₦8,500 walk-in.\nPremium (100 pellets): ₦11,000 online / ₦11,500 walk-in.\nSeal Team Package (100 pellets + 1 ATV lap): ₦14,500 (per a third-party listing).\nExtra Pellets: Charged extra, so ask at reception.\nBarrel Sock Rental: ₦500 per day.\nBeach Entrance Fee: May apply, depending on access.\nFood & Drinks (bar & grill): Extra.\nCorkage (party area): Extra.",
                    new[]
                    {
                        "Gameland Lagos: An outdoor games centre with pool, archery and giant Jenga at The Lekki Coliseum, Lekki Phase 1.",
                        "Rufus & Bee: An arcade, bowling and sports bar at Twinwaters, Lekki Phase 1.",
                        "Lekki Conservation Centre: Nature trails and a canopy walkway in Lekki."
                    },
                    "Late afternoon on a weekend with a group, when it's cooler. The dry season (roughly November to March) is most comfortable outdoors.",
                    "Medium (5/10) — busiest on weekends and when a group has booked the arena.",
                    "Rating: 3/10 — a physical outdoor activity on uneven ground. The bar and grill area is easier to reach.",
                    "2 to 3 hours.")),

            // Gameland Lagos — founder: "field games, pool and billiards".
            // Sources: x.com/gamelandlagos (posts: at The Lekki Coliseum, off Admiralty Way, "12pm to 12am everyday",
            // games + karaoke nights — via search summary); tiktok.com/@lagos_gist/video/7433164159543528710 (soft launch,
            // "premier game and outdoor sports center", with @lagostourism — via search summary); tiktok.com/@seeelagos1
            // video (entry from ₦5k; basketball, giant Jenga, pool, board/card games, bar; book ahead or walk in — via search
            // summary); reviewit.ng/2025/05/24/gameland-is-the-lagos-break-you-need/ (14 Providence St; ₦5,000 for 20 games;
            // pool tables, basketball, giant Jenga, puzzles; cocktails/mocktails/small chops; family-friendly);
            // italawa.com.ng/place/gameland-lagos/ (daily 12 PM–12 AM; ₦5,000 full-day unlimited; snacks ₦2,000–₦5,000;
            // mini-golf, archery; phone 0812 541 6210; ₦1,000 reservation commitment fee); placestotour.com/tour/gameland-
            // lekki-phase-1-tour/ (archery, pool; "over 20" games); tiktok.com/@gamelandlagos (₦4,000 rate, 20% off Mon–Thu
            // for students/NYSC — via search summary); viewlagos/ariiyatickets (Aug 2025 event entry ₦8,000 — via search
            // summary). gamelandlagos.com showed "This Account is Offline" (Nov 2026), so current status is unconfirmed.
            // Coordinates: APPROXIMATE — OSM Nominatim point for The Providence Street, Lekki Phase 1 (6.4318914, 3.4644676);
            // The Lekki Coliseum (Plot 14) itself is not mapped.
            // AvgCost: ~US$8 ≈ ₦10,900 at ~₦1,360 per US$ (Nov 2026) — ₦5,000 entry plus snacks/drinks (listing estimates
            // ₦7,000–₦10,000 per person).
            new("lagos", GamelandName, Nigeria, Lagos, LagosLangs, 6.4319, 3.4645,
                8m, "Car", 2,
                new[] { "activity_experience", "entertainment_nightlife" },
                new[] { "group_friendly", "social", "family_friendly", "budget_friendly", "sports_fan", "local_favorite" },
                new[] { "Gameland", "Game Land", "Game Land Lagos", "Gameland Lekki", "Gameland Lekki Coliseum", "Gameland at The Lekki Coliseum", "Gameland Lagos Lekki Phase 1" }, "Exterior",
                Desc(
                    "Gameland Lagos is an outdoor games centre at The Lekki Coliseum on Providence Street, off Admiralty Way, Lekki Phase 1. One flat entry fee gives you all-day access to more than 20 games. Lawn and field games include basketball, archery, mini-golf and giant Jenga, and there are pool tables and a library of board and card games. Shaded seating areas and a bar serve cocktails, mocktails and small chops.\n\nWhat to Do: Spend an afternoon or evening moving between pool, archery, basketball and giant board games with friends or family. You can also come for a themed night: Gameland has hosted karaoke, game nights and picnic-and-movie events.",
                    "Gameland has become a cheap group hangout in Lekki and an alternative to another lounge or brunch. Reviewers describe it as relaxed and good value for friend groups, families and team outings.",
                    "Address: The Lekki Coliseum, Plot 14 Providence Street, off Admiralty Way, Lekki Phase 1, Lagos.\n\nGetting There: Take a cab or ride-hail to The Lekki Coliseum, off Admiralty Way. Parking is at the Coliseum.",
                    "Hours: Posted as 12:00 PM–12:00 AM daily. Confirm before you go.\n\nEntry: About ₦5,000 per person for unlimited games all day, per 2025 reviews. A cheaper weekday or student rate has been advertised. Special events cost more, for example ₦8,000.\n\nBooking: Walk in or book ahead. Phone 0812 541 6210 (per listing) or +234 704 783 6000 (per its social profiles). A listing mentions a ₦1,000 reservation commitment fee. Instagram/X/TikTok @gamelandlagos. Party and team-bonding packages are priced separately.\n\nWhat to Wear: Comfortable clothes and shoes for light activity.\n\nCheck It's Open: The website (gamelandlagos.com) was offline in November 2026. Confirm on Instagram or by phone that it is still operating before you go.",
                    "Status Unconfirmed: The official website was down when checked. Check recent posts on @gamelandlagos before you travel.\n\nPrices Vary: The entry fee changes for events and promotions, so confirm the day's price.\n\nOutdoor Heat: Most games are outdoors. Bring water and sunscreen, and expect pauses when it rains.\n\nArchery: Follow staff instructions and keep children supervised.",
                    "Entry: About ₦5,000 per person (standard). Event nights cost more (₦8,000 for an August 2025 event).\nFood & Drinks: About ₦2,000–₦5,000 for snacks, more for cocktails.\nReservation Commitment Fee: ₦1,000 (per listing).\nParty & Team-Bonding Packages: Ask for a quote.\nRide-hail: Extra.",
                    new[]
                    {
                        "Rufus & Bee: An indoor arcade, bowling and sports bar at Twinwaters, a short drive away in Lekki Phase 1.",
                        "Seal Team Paintball: A speedball paintball arena in Oniru.",
                        "Lekki Arts & Crafts Market: Crafts and souvenirs in Lekki."
                    },
                    "Late afternoon into the evening, when it's cooler. The dry season (roughly November to March) is best for outdoor games.",
                    "Medium (5/10) — busier on weekend evenings and event nights.",
                    "Rating: 5/10 — open-air grounds with some uneven surfaces. Board games and seating areas are easier to reach.",
                    "2 to 4 hours.")),

            // Rufus & Bee — founder: "Rugus and Bee [arcade]"; spelling confirmed as "Rufus & Bee" (Twinwaters, Lekki).
            // Sources: whitehutchinson.com/news/lenews/2018/june/article106.shtml (22,600 sq ft adult-oriented centre at
            // Twin Waters Entertainment Center, Lekki; 4 boutique bowling lanes, 100-game arcade, sports bar, full-service
            // restaurant); naijalifemagazine.com 2018 / estateintel.com (opened Dec 2017 — via search summary);
            // bestlagos.ng/rufus-and-bee-guide-review/ (22 Aug 2026: off Remi Olowude Rd, Lekki 2nd Roundabout; Tue–Sun
            // 12:00–22:30, closed Mon; Buzz Card ₦1,000, replacement ₦2,000; 3–17 chips per play, most 7.5; chip packages
            // ₦7,500/55 … ₦35,000/450; chips don't expire; bowling ₦5,000 Wednesdays; snooker; Tex-Mex restaurant; sports bar;
            // menu prices; casual dress); italawa.com.ng/place/rufus-bee/ (Tue–Sat 12–10:30 PM, Sun 12:30–10:30 PM; +234 908
            // 007 7221; @rufusandbee_twinwaters; twinwaterslagos.com/rufus-bee/ (404 when fetched); bowling ₦5,000 incl. shoes
            // & socks; average spend ₦10,000–₦20,000; ₦1,000 reservation commitment fee); wanderlog (4.6 from ~8,157 reviews
            // — via search summary); tripadvisor (12–10:30 PM, Monday closed — via search summary).
            // Coordinates: APPROXIMATE — Google Maps link on trippin.world/spot/rufus-and-bee (6.4238735, 3.4645809).
            // AvgCost: ~US$15 ≈ ₦20,400 at ~₦1,360 per US$ (Nov 2026) — card, a ₦10,000–₦13,000 chip pack and a drink/snack;
            // listings put the average at ₦10,000–₦20,000.
            new("lagos", RufusBeeName, Nigeria, Lagos, LagosLangs, 6.4239, 3.4646,
                15m, "Car", 1,
                new[] { "activity_experience", "entertainment_nightlife" },
                new[] { "family_friendly", "group_friendly", "social", "sports_fan", "local_favorite", "food_focused" },
                new[] { "Rufus and Bee", "Rufus & Bee Twinwaters", "Rufus and Bee Twinwaters", "Rufus & Bee Lekki", "Rufus and Bee Lekki", "Twinwaters: Rufus And Bee", "Rugus and Bee", "Rufus n Bee" }, "Interior",
                Desc(
                    "Rufus & Bee is an indoor arcade, bowling alley, sports bar and restaurant in the Twinwaters entertainment centre off Remi Olowude Street, Lekki Phase 1. It opened in December 2017 and covers about 22,600 sq ft. It has around 100 arcade games, including racing, basketball hoops, dance machines and punching bags, plus four boutique bowling lanes, snooker, a sports bar showing live football and a Tex-Mex restaurant. Some games pay out tickets that can be swapped for prizes.\n\nWhat to Do: Load a Buzz Card with chips and work through the arcade, bowl a few frames, then eat or watch a match at the bar. Wednesdays are known for cheaper games and bowling.",
                    "Rufus & Bee is one of Lagos's best-known arcades and is often called the city's biggest. It is a go-to for birthdays, dates, family outings and office hangouts, and reviewers consistently rate it highly.",
                    "Address: Twinwaters Entertainment Centre, off Remi Olowude Street (by Lekki Second Roundabout), Lekki Phase 1, Lagos.\n\nGetting There: Take a cab or ride-hail to Twinwaters, Remi Olowude Street. There is parking at the complex.",
                    "Hours: Tuesday–Sunday from about 12:00 PM to 10:30 PM (Sunday may open 12:30 PM), closed Monday. Some listings show later weekend closing, so confirm.\n\nHow Payment Works: Buy a Buzz Card (₦1,000; ₦2,000 to replace) and load it with chips. Games cost 3–17 chips each, most about 7.5. Chips don't expire, so keep your card for next time.\n\nBowling: About ₦5,000 per person including bowling shoes and socks. Lanes fill up, so pre-book.\n\nBooking & Contact: Phone +234 908 007 7221; Instagram @rufusandbee_twinwaters. A listing mentions a ₦1,000 reservation commitment fee.\n\nDress: Casual.",
                    "Chips Go Fast: Reviewers warn that a group can spend far more than planned topping up cards. Set a budget per person.\n\nBusy Times: Weekends, public holidays and school holidays are crowded, and bowling slots sell out.\n\nCard Fees: Lost cards cost extra to replace. Keep yours safe.",
                    "Buzz Card: ₦1,000 (replacement ₦2,000).\nChip Packs: ₦7,500 (55 chips), ₦10,000 (84), ₦13,000 (115), ₦18,000 (180), ₦23,500 (250), ₦29,000 (325), ₦35,000 (450) (per a 2026 guide).\nBowling: About ₦5,000 per person (Wednesday price; confirm on other days).\nFood: For example wings ₦6,000, nachos ₦9,750, goat pepper soup ₦7,000, fillet and bacon ₦23,000.\nDrinks: Mocktails from about ₦3,500.\nRide-hail: Extra.",
                    new[]
                    {
                        "Gameland Lagos: Outdoor games, pool and archery at The Lekki Coliseum, nearby in Lekki Phase 1.",
                        "Lekki Conservation Centre: Nature trails and a canopy walkway in Lekki.",
                        "Lekki Arts & Crafts Market: Crafts and souvenirs in Lekki."
                    },
                    "Weekday afternoons are quietest. Wednesdays have discounts, and evenings and weekends have the most atmosphere.",
                    "Medium to High (7/10) — busy at weekends and in holidays.",
                    "Rating: 7/10 — an indoor venue in a modern complex. Confirm step-free access to every area with the venue.",
                    "2 to 3 hours.")),

            // Giwa Gardens Water Park — founder: "water slides".
            // Sources: giwagardens.com (two sessions: Morning 10 AM–3 PM, Evening 4 PM–8 PM; WhatsApp +234 906 155 4646;
            // @giwagardenslagos; booking calendar to Jan 2027); giwagardens.com/ParkGuidelines (children supervised; not
            // recommended for over-55s, pregnant women, heart/BP conditions; no alcohol; no phones/jewellery/sunglasses/
            // slippers on slides; closes in strong wind/thunderstorms); giwagardens.com/the-experience/the-tornado,
            // /the-rainbow, /the-waterhouse (attraction pages — titles via search); giwagardens.com/accessories/ (towel,
            // life jacket and tube bundle ₦15,000, rented, extra to ticket — via search summary); official ticket tiers from
            // ₦53,950 standard / ₦66,950 fast-track / ₦116,950 premium (via search summary — confirm); bestlagos.ng/giwa-
            // gardens-water-park-review-and-guide/ (Ejike St, Native Villa, off Ablag Rd, Monastery Way, Sangotedo; mostly
            // weekends; children 3–11 vs adults 12+ tiers; under-2s free; tickets on official site; outside food not allowed;
            // cashless (Paystack/Squad); food ₦3,000–₦39,000; Victoria Waves, River Congo, Pirate Ship, Waterhouse, Python,
            // Rainbow; premium Batuwa Cave/Jacuzzi Bar; min height 1.4 m for rides, ~90 kg limit; lockers; free parking;
            // long queues for non-fast-track); thisdaylive.com/index.php/2024/03/10/giwa-gardens-meticulous-return
            // (reopened Mar 2024 after a Dec 2023 customer injury; Lagos State Safety Commission compliance certificate;
            // founder/CEO Morenikeji Giwa; billed as West Africa's biggest water park); allafrica.com/stories/202511260114.html
            // (Nov 2025 "Detty December" free tickets with Lagos govt — via search summary); connectciti.com listing (plus code
            // FJ5H+M2X — via search summary).
            // Coordinates: APPROXIMATE — decoded from the plus code FJ5H+M2X (full code 6FR5FJ5H+M2X) = 6.45924, 3.62761;
            // consistent with Sangotedo (OSM town centroid 6.4710, 3.6356).
            // AvgCost: ~US$55 ≈ ₦74,800 at ~₦1,360 per US$ (Nov 2026) — a standard adult ticket (~₦50,000–₦54,000),
            // the ₦15,000 accessories bundle and a meal; transport from central Lagos extra.
            new("lagos", GiwaGardensName, Nigeria, Lagos, LagosLangs, 6.4592, 3.6276,
                55m, "Car", 2,
                new[] { "activity_experience" },
                new[] { "family_friendly", "adventurous", "group_friendly", "social", "premium", "photography" },
                new[] { "Giwa Gardens", "Giwa Garden", "Giwa Gardens Lagos", "Giwa Gardens Waterpark", "Giwa Water Park", "Giwa Gardens Sangotedo", "Giwa Gardens Water Park & Resort" }, "Landscape",
                Desc(
                    "Giwa Gardens is a large water park in Sangotedo, on the Lekki–Epe corridor of Lagos. It bills itself as the biggest water park in West Africa, with about 70% of its grounds given over to water. The headline rides are high-speed and winding slides such as the Tornado, the Python and the multi-coloured Rainbow. There are also the Victoria Waves wave pool, the River Congo fast river, a pirate ship with water guns and the Waterhouse, a play structure of slides, jets and waterfalls. Premium tickets add extras such as the Batuwa Cave and a jacuzzi bar.\n\nWhat to Do: Ride the slides, float the river, swim in the wave pool and let children loose on the pirate ship and Waterhouse. Spend a morning or evening session there.",
                    "Giwa Gardens is Lagos's big-ticket water-park day out, popular with families, friend groups and December holiday crowds. It reopened in March 2024 with a state safety compliance certificate after a temporary closure.",
                    "Address: Ejike Street, Native Villa, off Ablag Road, Monastery Way, Sangotedo, Lagos.\n\nGetting There: Drive or take a cab or ride-hail along the Lekki–Epe Expressway to Sangotedo, then turn off at Monastery Road. From Victoria Island allow 45 minutes to well over an hour, depending on traffic. Parking is free.",
                    "Tickets: Buy online at giwagardens.com, in Standard, Fast-Track and Premium tiers with child (3–11) and adult (12+) prices. Under-2s are free with a paying guardian. Promotions and free-ticket campaigns run at times.\n\nSessions & Days: Morning 10:00 AM–3:00 PM and evening 4:00–8:00 PM. A guide says it mainly opens at weekends, with Monday–Tuesday closed and some weekdays kept for private events. Check the booking calendar.\n\nRide Rules: Riders must be at least 1.4 m tall, and riders over about 90 kg are generally not allowed. The park advises against rides for people over 55, pregnant women and anyone with heart or blood-pressure conditions. Children must be supervised by an adult at all times.\n\nAccessories: A rented towel, life jacket and floating tube are required and cost extra. Return them when you leave.\n\nPayment: Cashless (card or transfer). No outside food. Food is sold inside.\n\nContact: WhatsApp +234 906 155 4646; Instagram @giwagardenslagos.",
                    "Total Cost Adds Up: The ticket, the mandatory accessories bundle and in-park food (no outside food allowed) together make this one of Lagos's pricier days out. Budget for all three.\n\nQueues: Standard-ticket holders report long queues for the big slides. Fast-Track skips them.\n\nLoose Items: Phones, jewellery, sunglasses and slippers aren't allowed on the slides. Use the lockers.\n\nNo Alcohol Before Rides: Visibly drunk guests are refused.\n\nWeather: Rides close temporarily in strong wind or thunderstorms.\n\nPrices Change Often: Published prices differ between sources and promotions. Check the official site on the day you book.",
                    "Tickets (official site, Nov 2026, before discounts): from about ₦53,950 Standard, ₦66,950 Fast-Track, ₦116,950 Premium. Child tickets are cheaper. Confirm current prices.\nTowel, Life Jacket & Floating Tube: ₦15,000 (rented, required).\nFood in Park: About ₦3,000 for a snack up to ₦39,000 for a family platter.\nLockers: Included with some tickets. Confirm.\nRide-hail from central Lagos: Extra, and costly in traffic.",
                    new[]
                    {
                        "Lekki Conservation Centre: Nature trails and a canopy walkway on the way back towards Lekki Phase 1.",
                        "Lekki Arts & Crafts Market: Crafts and souvenirs in Lekki."
                    },
                    "A weekend morning session during the dry season (roughly November to March). Book early in December, when it's busiest.",
                    "High (7/10) — busiest at weekends, in school holidays and in December.",
                    "Rating: 3/10 — rides have height, weight and health limits, and much of the park involves stairs, wet surfaces and water. Ask the park about access for wheelchair users.",
                    "Half a day (one session, about 5 hours).")),

            // ═══ Batch: Abuja ═══
            // ─── Abuja ─────────────────────────────────────────────────────────
            // Floris Garden — founder: food for less than ₦10,000 (used as given). Sources:
            // sabiabuja.com/floris-garden-abuja/ (6 Sep 2024: address, hours, phone, IG, full menu price list,
            // snooker/tennis/board games, live band, no outside food), wakaabuja.com/floris-garden-abuja (same address/
            // hours; email info@florisgardenabuja.com; pergolas, water features; reservations by phone/email/website),
            // african.business/2026/02/arts-culture/nightlife-blossoms-in-abujas-garden-economy (botanical decor,
            // weekend jazz — via search summary; page blocked). florisgardenabuja.com did not resolve when checked.
            // Coordinates: APPROXIMATE — OSM Nominatim point for Abidjan Street, Wuse (9.0582310, 7.4716141);
            // Alfayyium Street itself is not in OSM.
            // AvgCost: ~US$11 ≈ ₦15,000 at ~₦1,360 per US$ (Nov 2026) — a meal under ₦10,000, a drink and a ride.
            new("abuja", FlorisGardenName, Nigeria, Abuja, AbujaLangs, 9.0582, 7.4716,
                11m, "Car", 2,
                new[] { "food_experience", "entertainment_nightlife" },
                new[] { "food_focused", "budget_friendly", "relaxing", "social", "group_friendly", "music", "local_favorite" },
                new[] { "Floris Garden Abuja", "Floris Gardens", "Floris Garden Wuse", "Floris Garden Wuse Zone 3", "Floris Abuja", "Florist Garden Abuja" }, "Exterior",
                Desc(
                    "Floris Garden is a garden restaurant and bar on Alfayyium Street, off Abidjan Street, in Wuse Zone 3, Abuja. You can eat outdoors among lawns, flower beds, water features and shaded pergolas, or inside in an air-conditioned dining room. The menu is mostly Nigerian (rice dishes, soups and swallow, pepper soup, peppered meats, grilled fish) with some international dishes, and the bar serves cocktails, wine and local drinks.\n\nWhat to Do: Eat a cheap Nigerian meal in the garden, have drinks with friends, catch live music, or play snooker, tennis or board games. The garden is also hired out for weddings, birthdays and corporate events, with in-house catering.",
                    "Floris Garden is part of Abuja's \"garden\" scene of open-air restaurants and lounges, and is known for good-value local food in a green setting — you can eat well for less than ₦10,000.",
                    "Address: Alfayyium Street, off Abidjan Street, Wuse Zone 3, Abuja.\n\nGetting There: Take a cab or ride-hail to Abidjan Street, Wuse Zone 3, and ask for Floris Garden.",
                    "Food Prices: You can eat for less than ₦10,000. A 2024 price list showed rice ₦1,500, swallow ₦700, soups ₦800–₦1,500, chicken ₦1,000–₦2,000, goat meat pepper soup ₦2,500 and grilled fish ₦10,000–₦12,000 — expect prices to have risen since.\n\nHours (reported): Monday–Friday 10:00 AM–11:00 PM; Saturday–Sunday 9:00 AM–11:00 PM — confirm before you go.\n\nContact & Reservations: Phone +234 913 861 1518; email info@florisgardenabuja.com; Instagram @florisgardenabj.\n\nNo Outside Food: Outside food and drinks are not allowed.\n\nEvents: Weddings, birthdays and corporate events can be booked, with catering.",
                    "Prices Change: The published menu prices are from 2024 — check the current menu before ordering for a group.\n\nPrivate Events: Parts of the garden may be closed or busy during events.\n\nMosquitoes: It's an outdoor garden — bring insect repellent for evenings.",
                    "Meal: Under ₦10,000 per person for a local dish with protein (founder); grilled fish ₦10,000–₦12,000 (2024 list).\nDrinks: Extra.\nEvent Hire & Catering: Ask the venue.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "BMT African Garden: Garden, mini zoo and pool in Wuse 2.",
                        "Jabi Lake: Lakeside walks and Jabi Lake Mall, west of Wuse."
                    },
                    "Year-round; evenings and weekends for the garden atmosphere and live music, the dry season (roughly November to March) for outdoor seating.",
                    "Medium (5/10) — busier in the evenings, at weekends and during events.",
                    "Rating: 6/10 — garden paths and an indoor dining room; confirm step-free access with the venue.",
                    "1 to 3 hours.")),

            // Magicland Amusement Park — founder wrote "fun city magic land". Fun City is the indoor arcade INSIDE
            // Magicland, not a separate park, so it is one row with Fun City as an alternate. Sources:
            // rcdb.com/6301.htm (Magicland, formerly Wonderland Amusement Park, Constitution Avenue; phone
            // +234 909 977 7881; coordinates; two coasters: Roller Coaster and Magic Worm), wakaabuja.com/?p=43
            // (address No. 1 Kukwaba Hills, Constitution Ave, Wuye; entry ₦500 / ₦1,000; per-ride prices ₦1,000–₦2,000),
            // sabiabuja.com/fun-locations-to-visit-in-abuja/ (same address), abujaguide.com.ng 2025 guide via search
            // summary (Fun City arcade uses a rechargeable ₦1,000 card; POS agents charge a fee; weekend hours, gate closes
            // 7 PM; IG @magicland_abuja — unconfirmed), TripAdvisor review via search summary (some old rides, breakdowns,
            // weak food; toilets inside Fun City).
            // Coordinates: rcdb.com park record (9.043341, 7.451998); OSM reverse-geocodes them to Kukwaba, Abuja,
            // matching the Kukwaba Hills address.
            // AvgCost: ~US$13 ≈ ₦18,000 at ~₦1,360 per US$ (Nov 2026) — entry, about five rides, an arcade card top-up,
            // food and a ride-hail; rough.
            new("abuja", MagiclandName, Nigeria, Abuja, AbujaLangs, 9.0433, 7.4520,
                13m, "Car", 2,
                new[] { "activity_experience", "entertainment_nightlife" },
                new[] { "family_friendly", "budget_friendly", "group_friendly", "social", "local_favorite" },
                new[] { "Magicland", "Magic Land", "Magicland Abuja", "Magic Land Abuja", "Magicland Park", "Wonderland Amusement Park", "Wonderland Abuja", "Fun City", "Fun City Magicland", "Fun City Arcade", "Fun City Magic Land" }, "Exterior",
                Desc(
                    "Magicland is Abuja's long-running amusement park on Constitution Avenue, opposite the National Stadium area, formerly known as Wonderland Amusement Park. It has more than 20 rides for children and adults — including a roller coaster, a family coaster, a pendulum ride, a pirate ship, bumper cars, a Ferris wheel, a water splash ride and many smaller kids' rides — and Fun City, an indoor arcade with racing, shooting and sports-simulator games.\n\nWhat to Do: Pay per ride, play the arcade games in Fun City, picnic on the grass or eat at the food stalls.\n\nFun City: The indoor arcade inside Magicland. Games now run on a rechargeable card instead of coins.",
                    "For many Abuja families Magicland is the classic day out with the kids — one of the most affordable amusement parks in Nigeria because you only pay for the rides you take. Reviews often mention that some rides are old and occasionally out of service.",
                    "Address: No. 1, Kukwaba Hills, Constitution Avenue, Wuye/Kukwaba, Abuja (near the National Stadium and City Gate).\n\nGetting There: Take a cab or ride-hail and ask for Magicland, Constitution Avenue.",
                    "Entry: Reported at about ₦500, or ₦1,000 with an event ticket — confirm at the gate.\n\nRides: Paid per ride. A recent list shows ₦1,000 for most kids' rides (carousel, train, mini wheel, bouncing castle, boat ride), ₦1,500 for the roller coaster, bumper cars, pirate ship and Ferris wheel, and ₦2,000 for the pendulum and water splash — prices change with season and promotions.\n\nFun City Arcade: Buy a rechargeable card (reported at ₦1,000) and top it up; games have been listed at about ₦1,000–₦4,000.\n\nHours: Sources differ — roughly 12:00 PM–8:00 PM on weekdays and from 10 or 11 AM at weekends, with the gate reported to close around 7 PM — confirm before you go.\n\nContact: Phone +234 909 977 7881 (per the RCDB listing); Instagram @magicland_abuja (unconfirmed).\n\nPayment: Bring cash. POS agents are on site but charge a fee.",
                    "Ride Condition: Some rides are old and may be closed for maintenance; check which rides are running before buying tickets for a group.\n\nSupervise Children: Watch children on rides and in the arcade, and follow operators' height and age rules for each ride.\n\nFood: Reviews call the food options weak — consider eating before or after.\n\nWeekends & Holidays: Long queues at weekends, during school holidays and at Christmas/Sallah.",
                    "Entry: About ₦500 (₦1,000 with an event ticket).\nRides: ₦1,000–₦2,000 each.\nFun City Card: About ₦1,000, plus top-ups.\nPOS Withdrawal Fee: Extra if you don't bring cash.\nFood & Drinks: Extra.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Moshood Abiola National Stadium: Abuja's national stadium, close by.",
                        "Fun Alley: Axe throwing, pottery and games in nearby Durumi."
                    },
                    "Weekday afternoons for short queues; late afternoon in the dry season (roughly November to March) is coolest. Busiest at weekends and on public holidays.",
                    "Medium to High (7/10) at weekends and holidays; Low (3/10) on weekdays.",
                    "Rating: 5/10 — open grounds with some slopes; most rides need you to climb in, and some are not suitable for people with limited mobility.",
                    "Half a day.")),

            // Fun Alley — no founder input beyond the name; researched. Sources: Facebook "Fun Alley | Abuja"
            // (facebook.com/p/Fun-Alley-61551231266556/ — address Smokey House, Oladipo Diya St, near Sabo Kukwaba-Garki,
            // Durumi; +234 702 552 2546; funalleyy@gmail.com — via search summary), Instagram @funalleyy (bio: "Abuja Fun |
            // Games | Pottery | Art & Crafts"), sabiabuja.com/smokey-house-abuja/ (18 Dec 2024: Smokey House hosts Fun Alley,
            // snooker, basketball court, team bonding; hours 10am till late; food price list), wakaabuja.com/fun-alley-abuja
            // (activity price list: axe/knife throwing, pottery, basket weaving, tie & dye, VR, kids' playground; birthday &
            // team-bonding packages; phone 0702 552 2546; daily 10 AM–9 PM), flightpadi.com/attraction/fun-alley-abuja/
            // (bookings only, no walk-ins — via search summary; page did not load). wakaabuja also claims go-karting and a
            // trampoline park; no other source supports that, so it is left out.
            // Coordinates: APPROXIMATE — OSM Nominatim point for Oladipo Diya Road, Durumi (9.0102388, 7.4587297);
            // Smokey House is not mapped in OSM.
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — one activity (₦4,000–₦12,000), a meal and a ride.
            new("abuja", FunAlleyName, Nigeria, Abuja, AbujaLangs, 9.0102, 7.4587,
                15m, "Car", 2,
                new[] { "activity_experience" },
                new[] { "social", "group_friendly", "couple_friendly", "quirky", "adventurous", "art", "hidden_gem" },
                new[] { "Fun Alley Abuja", "FunAlley", "Fun Alleyy", "Funalleyy", "Fun Alley Durumi", "Fun Alley Smokey House" }, "Interior",
                Desc(
                    "Fun Alley is an activity and games space in Durumi, Abuja, run at Smokey House Grill & Chill on Oladipo Diya Street. It mixes hands-on creative sessions — pottery, basket weaving, tie-and-dye and textured art — with games and throwing sports: axe throwing (regular and projected), knife throwing, virtual reality, snooker, a basketball court and a kids' playground. It also runs birthday and team-bonding packages for groups.\n\nWhat to Do: Book a pottery or art session with friends or a date, try axe or knife throwing, or bring a group for a birthday or office team-bonding day, then eat at Smokey House.",
                    "Fun Alley is one of the spots Abuja's creators and young professionals share for something different from the usual lounge — creative workshops and axe throwing for dates, friends and office outings.",
                    "Address: Smokey House, Oladipo Diya Street, near Sabo Kukwaba–Garki Road, Durumi, Abuja.\n\nGetting There: Take a cab or ride-hail to Smokey House, Durumi.",
                    "Booking: Reported to be by booking only — no walk-ins. Book on WhatsApp/phone 0702 552 2546, email funalleyy@gmail.com, or Instagram @funalleyy.\n\nPrices (reported): Regular axe throwing ₦4,000 (15 throws); projected axe throwing ₦7,000 (20 min); knife throwing ₦3,000 (15 throws); pottery or basket weaving ₦9,000 and textured art ₦12,000 (with complimentary drinks and board games); tie-and-dye ₦16,000; VR ₦3,500–₦5,000; kids' playground ₦1,500 — confirm when you book.\n\nGroup Packages: Birthday packages from about ₦200,000 for 10 people; team-bonding packages from about ₦60,000 for 10 people.\n\nHours: Reported as daily 10:00 AM–9:00 PM; pottery sessions have been run at 2:00 PM — confirm your slot.\n\nFood: Smokey House serves Nigerian dishes, grills, shawarma and burgers (2024 menu: about ₦5,000–₦10,000 for a main).",
                    "Book First: Turning up without a booking may mean no session.\n\nThrowing Safety: Follow the instructor's briefing for axe and knife throwing; ages for these aren't published — ask before bringing children.\n\nMixed Information: Online listings disagree on what's offered and some blog posts mention go-karting and trampolines that we couldn't confirm — check with Fun Alley.\n\nPrices Change: Published prices may be out of date — confirm when booking.",
                    "Activities: About ₦3,000–₦16,000 each (reported).\nGroup Packages: From about ₦60,000 (team bonding, 10 people) or ₦200,000 (birthday, 10 people).\nFood at Smokey House: About ₦5,000–₦10,000 per main (2024).\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Magicland Amusement Park: Rides and the Fun City arcade on nearby Constitution Avenue."
                    },
                    "Year-round (mostly under cover); weekday afternoons are quieter, weekends book up.",
                    "Low to Medium (4/10) — sessions are booked, so it rarely feels crowded.",
                    "Rating: 6/10 — a restaurant compound with ground-level activity areas; confirm step-free access when booking.",
                    "2 to 3 hours.")),

            // National Children's Park and Zoo — founder: "the zoo in Asokoro where you can take a boat ride" (name not
            // known). Confirmed: the zoo in Asokoro is the National Children's Park and Zoo, and boat rides on its lake are
            // confirmed by a creator video. Sources: fcta.gov.ng/ova_por/national-childrens-park-and-zoo/ (Asokoro, behind the
            // Presidential Villa, near Aso Rock; playground; lake), tiktok.com/@prettyabuja/video/7430120031960222982 (late 2024:
            // boat ride ₦3,000 per person, two laps of the lake, up to 10 people; 2-person rowing boat ₦2,000 per person
            // (self-row); entry ₦500; monkeys, camels, horses, tortoises; swings), wakaabuja.com/?p=2869 (₦500 adults / ₦200
            // kids; 9 AM–6 PM; IG @parkandzoong; horse and camel rides, picnic areas), outravelandtour.com/things-to-do-at-the-
            // national-childrens-park-and-zoo-abuja (Jun 2021: ₦400; 8am–6pm; photoshoot ₦1,000; few animals left),
            // leadership.ng/abuja-on-a-budget-three-places-to-visit-this-weekend/ (Nov 2026: about ₦2,000 adults / ₦1,000
            // children, reported; 9 AM–6 PM), wakaabuja.com/national-childrens-park-zoo/ (paddle boats, train ride — via search
            // summary).
            // Coordinates: APPROXIMATE — OSM Nominatim "National Park and Zoo" park polygon point (9.0807378, 7.5250229);
            // OSM also has a "Children's Zoo" point off Murtala Mohammed Expressway at (9.0819510, 7.5184939).
            // AvgCost: ~US$7 ≈ ₦10,000 at ~₦1,360 per US$ (Nov 2026) — entry, a boat ride, snacks and a ride-hail.
            new("abuja", ChildrensParkZooName, Nigeria, Abuja, AbujaLangs, 9.0807, 7.5250,
                7m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "family_friendly", "budget_friendly", "nature", "educational", "relaxing", "local_favorite", "photography" },
                new[] { "Abuja Children's Park and Zoo", "Children's Park and Zoo Abuja", "National Children Park and Zoo", "National Childrens Park and Zoo", "National Park and Zoo Abuja", "Abuja Zoo", "Asokoro Zoo", "Park and Zoo Asokoro", "Children's Zoo Abuja" }, "Landscape",
                Desc(
                    "The National Children's Park and Zoo is a public park and zoo in Asokoro, Abuja, behind the Presidential Villa and close to Aso Rock. Alongside the animal enclosures — visitors report monkeys, camels, horses, tortoises, ostriches, a buffalo and, in some accounts, a lion and hyenas — it has a lake, children's playgrounds, swings and big shady lawns for picnics.\n\nWhat to Do: See the animals, take a boat ride on the lake, picnic, let children use the playground, and take photos with Aso Rock in the background.\n\nBoat Ride: A motorboat takes up to 10 people around the lake twice (₦3,000 per person in late 2024), and there are two-person rowing boats you paddle yourself (₦2,000 per person).",
                    "For Abuja families it's a cheap, simple day out — school groups, picnics and weekend outings. Recent visitors enjoy the boat ride and the lake; older reviews note that the number of animals has fallen over the years.",
                    "Location: Asokoro, Abuja, behind the Presidential Villa; reached off the Murtala Mohammed Expressway. Visitors say the Google Maps pin is accurate.\n\nGetting There: Take a cab or ride-hail and ask for the National Children's Park and Zoo, Asokoro.",
                    "Entry: Sources disagree — reported from ₦500 (adults) and ₦200 (children) up to about ₦2,000 (adults) and ₦1,000 (children) — confirm at the gate.\n\nBoat Ride: Motorboat ₦3,000 per person (two laps, up to 10 people); rowing boat ₦2,000 per person (two people, you row) — late 2024 prices.\n\nOther Paid Extras: Horse and camel rides, a train ride, animal feeding and photoshoots (₦1,000 reported in 2021) are charged separately.\n\nHours: Reported as daily from 8 or 9 AM to 6 PM — confirm before you go.\n\nContact: Instagram @parkandzoong.\n\nBring: A picnic mat, water and snacks.",
                    "Animals: Don't feed or touch the animals unless staff say so, and keep children away from enclosure fences.\n\nOn the Lake: Ask for a life jacket and keep children seated and supervised on the boats; don't take the rowing boat if you can't swim confidently.\n\nSecurity Area: The park is close to the Presidential Villa — expect security presence and follow any restrictions on photography near government buildings.\n\nFacilities: Some enclosures are run-down and the animal collection is smaller than it was — set expectations for children.",
                    "Entry: About ₦500–₦2,000 adults; ₦200–₦1,000 children (sources differ).\nBoat Ride: ₦3,000 per person (motorboat) or ₦2,000 per person (rowing boat).\nHorse/Camel/Train Rides, Animal Feeding: Extra.\nPhotoshoot: About ₦1,000 (2021).\nSnacks: Extra — or bring a picnic.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Millennium Park: Abuja's largest public park, in nearby Maitama.",
                        "Monoliza Sports & Recreation Center: Amusement park, paintball and horse riding on R.B. Dikko Road."
                    },
                    "Mornings or late afternoons, when it's cooler; the dry season (roughly November to March) is most comfortable. Weekends and school holidays are busiest.",
                    "Medium (5/10) — busier at weekends, on public holidays and with school trips.",
                    "Rating: 5/10 — large grounds with unpaved paths and grass; boats need you to step in from the shore.",
                    "2 to 4 hours.")),

            // Amazon Kayak — founder: kayaking. Sources: evendo.com/locations/nigeria/abuja/attraction/amazon-kayak-jabi
            // (address 3C78+W5G, Obafemi Awolowo Way, Jabi; coordinates; phone +234 813 338 0369; 4.1/5 from 399 Google
            // reviews; on Jabi Lake), wakaabuja.com/amazon-kayak-abuja (daily 9 AM–6 PM; kayak ₦3,000, bubble ball ₦3,500,
            // couples hydrocycle ₦3,000; life jackets provided; no experience needed; bubble ball supervised, ≤15 min;
            // IG @amazon_kayak; group packages), abujaguide.com.ng via search summary (kayak ₦3,500, bubble ball ₦4,000,
            // water tricycle/water bike ₦5,000), africahousingnews.com (Sep 2023: price list ₦2,500–₦50,000).
            // wakaabuja's "banks of the Usuma Dam" line conflicts with every listing (Jabi Lake) and is ignored.
            // Not to be confused with "Kayak Abuja" (@kayak.abuja) at the National Stadium (outravelandtour.com, 2021).
            // Coordinates: evendo listing (9.0648138, 7.4154661), consistent with plus code 3C78+W5G (Jabi Lake shore).
            // AvgCost: ~US$9 ≈ ₦12,000 at ~₦1,360 per US$ (Nov 2026) — a kayak session, one more water activity and a ride.
            new("abuja", AmazonKayakName, Nigeria, Abuja, AbujaLangs, 9.0648, 7.4155,
                9m, "Car", 2,
                new[] { "activity_experience", "nature_outdoor" },
                new[] { "adventurous", "group_friendly", "couple_friendly", "family_friendly", "budget_friendly", "photography" },
                new[] { "Amazon Kayak Abuja", "Amazon Kayak Jabi", "Amazon Kayaks", "Amazon Kayaking", "Amazon_Kayak", "Amazon Kayak Jabi Lake" }, "Landscape",
                Desc(
                    "Amazon Kayak is a water-sports operator on the shore of Jabi Lake, off Obafemi Awolowo Way in Jabi, Abuja. It rents kayaks and other water toys — couples' hydrocycles (pedal boats), water tricycles and water bikes — and runs \"bubble ball\" (inflatable water-walking balls) on the lake. No experience is needed: staff give a short briefing and life jackets.\n\nWhat to Do: Paddle a kayak on Jabi Lake, try a hydrocycle with a partner, roll around in a bubble ball, then eat at the lakeside spots or Jabi Lake Mall.",
                    "It's one of the easiest ways to get out on the water in Abuja — a popular weekend activity for friends, couples and birthday groups, with good reviews (about 4.1/5 on Google from roughly 400 reviews).",
                    "Address: Jabi Lake shore, Obafemi Awolowo Way, Jabi, Abuja (plus code 3C78+W5G).\n\nGetting There: Take a cab or ride-hail to Jabi Lake (near Jabi Lake Mall) and ask for Amazon Kayak.",
                    "Prices (reported, per person): Kayak ₦3,000–₦3,500; bubble ball ₦3,500–₦4,000; couples' hydrocycle about ₦3,000; water tricycle or water bike about ₦5,000 — confirm when you arrive.\n\nHours: Reported as daily 9:00 AM–6:00 PM.\n\nContact & Groups: Phone 0813 338 0369; Instagram @amazon_kayak. Packages are offered for groups, events and private parties.\n\nSafety Gear: Life jackets are provided; staff give basic instructions before you go out.\n\nWhat to Wear: Clothes you don't mind getting wet; bring a change of clothes and a waterproof bag for your phone.",
                    "Wear the Life Jacket: Keep it on the whole time, even if you can swim.\n\nChildren: Children must be supervised; bubble ball is supervised and limited to about 15 minutes at a time. A minimum age isn't published — ask.\n\nDon't Swim: Stay in the boat — the lake isn't a swimming area. Shower after if you get wet.\n\nSun & Heat: Little shade on the water — bring sunscreen, a hat and water.\n\nSimilar Names: Don't confuse it with Kayak Abuja at the National Stadium, a separate operator.",
                    "Kayak: ₦3,000–₦3,500 per person.\nBubble Ball: ₦3,500–₦4,000.\nHydrocycle / Water Bike: About ₦3,000–₦5,000.\nFood & Drinks at Jabi Lake: Extra.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Jabi Lake: Lakeside walks, boat trips and Jabi Lake Mall on the same shore.",
                        "Eden Botanical Garden: A small garden and plant nursery on Shehu Yar'adua Way, Jabi."
                    },
                    "Morning or late afternoon, when the sun is lower; the dry season (roughly November to March) has the calmest weather. Weekends are busiest.",
                    "Medium (5/10) — busier at weekends and on holidays.",
                    "Rating: 3/10 — getting into a kayak from the lake shore needs balance and agility; ask staff about assistance.",
                    "1 to 2 hours.")),

            // BMT African Garden — no founder input; researched. Full name per a local creator: Bamanga Tukur Museum and
            // Cultural Park, popularly BMT African Garden. Sources: tiktok.com/@abujacityng/video/7254481217238699269 (mid-2023:
            // full name; adult ₦1,000, children 2+ ₦800; tour guide), outravelandtour.com/bmt-african-garden-zoo-abuja/
            // (Jan 2022: 155 Adetokunbo Ademola Crescent, Wuse 2; monkeys, porcupines, horses, parrots, geese, ducks, ostriches,
            // lions, hyenas; ₦1,000/₦500; ostrich feeding; Kuiknosh restaurant; library; bookstore; event spaces),
            // sabiabuja.com/?p=27312 (Jun 2026: zoo ₦1,000 pp; Shagaya animal train ₦7,000 per ride; outdoor library free with
            // entry; playground included; pool ₦2,000 per adult; IG @bmt_gardens; @kuiknosh), nigerianheritage.ng/tour-detail.php?id=93
            // (pan-African garden, baboons/ostriches/tortoises, gym, library; call ahead for hours), ng.africabz.com listing
            // (phone +234 818 744 8635 — via search summary), search summaries for hours (8am–6pm or 9am–6pm).
            // Coordinates: APPROXIMATE — taken from the map embedded on nigerianheritage.ng (9.0811, 7.4783), which lies near
            // the OSM line for Adetokunbo Ademola Crescent, Wuse II; not confirmed on an official map.
            // AvgCost: ~US$11 ≈ ₦15,000 at ~₦1,360 per US$ (Nov 2026) — zoo entry, a pool swim or the train, a snack, a ride.
            new("abuja", BmtGardenName, Nigeria, Abuja, AbujaLangs, 9.0811, 7.4783,
                11m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience", "food_experience" },
                new[] { "family_friendly", "nature", "educational", "relaxing", "cultural", "hidden_gem", "photography" },
                new[] { "BMT Garden", "BMT Gardens", "BMT Garden Abuja", "BMT Africa Garden", "BMT African Garden Zoo", "BMT Zoo", "BMT Zoo Abuja", "Bamanga Tukur Museum and Cultural Park", "BMT Garden Wuse 2" }, "Exterior",
                Desc(
                    "BMT African Garden — formally the Bamanga Tukur Museum and Cultural Park — is a private garden on Adetokunbo Ademola Crescent in Wuse 2, Abuja, with a pan-African theme. Inside the landscaped grounds are a mini zoo, a museum area, an outdoor library, a children's playground, a swimming pool, wooden huts for relaxing, a restaurant (Kuiknosh) and event spaces. Visitors have reported monkeys and baboons, porcupines, horses, parrots, geese, ducks, ostriches, tortoises, lions and hyenas in the zoo.\n\nWhat to Do: Take a guided walk round the zoo and museum area, feed the ostriches, ride the animal train, read or work in the outdoor library, swim, let children play, and eat at the restaurant.",
                    "A green, quiet spot in the middle of busy Wuse 2 — locals come for a calm afternoon, kids' outings and photos against the colourful buildings.",
                    "Address: 155 Adetokunbo Ademola Crescent, Wuse 2, Abuja.\n\nGetting There: Take a cab or ride-hail to Adetokunbo Ademola Crescent and ask for BMT Garden. Parking is limited.",
                    "Garden Entry: Some visitors report free entry to the garden itself, with the zoo charged separately.\n\nZoo & Museum: About ₦1,000 for adults; children ₦500–₦800 (sources differ). Zoo visits can be guided.\n\nOther Activities (reported 2026): Animal train ₦7,000 per ride; swimming pool ₦2,000 per adult; outdoor library and playground included with entry.\n\nHours: Reported as daily 8 or 9 AM to 6 PM — call ahead to confirm.\n\nContact: Phone +234 818 744 8635 (listing); Instagram @bmt_gardens (restaurant @kuiknosh).\n\nEvents: Spaces can be booked for parties and weddings.",
                    "Animals: Feed only what staff give you and keep children's hands outside the enclosures.\n\nPrices Add Up: The zoo, train and pool are each charged separately.\n\nParking: Limited — a ride-hail is easier.\n\nCheck Hours: Opening times aren't published officially — call before you go.",
                    "Zoo & Museum: About ₦1,000 adults; ₦500–₦800 children.\nAnimal Train: ₦7,000 per ride (2026).\nSwimming Pool: ₦2,000 per adult.\nFood at Kuiknosh: Extra.\nEvent Hire: Ask the garden.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Floris Garden: Good-value Nigerian food in a garden in Wuse Zone 3.",
                        "Millennium Park: Abuja's largest public park, in nearby Maitama."
                    },
                    "Afternoons on weekdays for a quiet visit; the dry season (roughly November to March) is most comfortable outdoors.",
                    "Low to Medium (4/10) — busier at weekends and during events.",
                    "Rating: 6/10 — garden paths and some steps; the large grounds need comfortable shoes.",
                    "2 to 3 hours.")),

            // Monoliza — founder wrote "Monoliza Park (check spelling)". Spelling confirmed: "Monoliza" (official site
            // title "Monoliza – Sports & Recreation Center"); "Monaliza" is a misspelling. Sources: monolizaabuja.com
            // (1 R.B. Dikko Street, Area 11 Garki; Sports Center 7 AM–11 PM; Adventure Zone 9 AM–7 PM; football, padel,
            // tennis, swimming; amusement park, VR, PS5; restaurants, bar/lounge, café, shop, events; info@monolizaabuja.com;
            // +234 817 777 3757; online booking & memberships), abujaguide.com.ng/listing/monoliza-center-abuja (price list:
            // paintball, horse riding, quad biking, skating, courts, amusement park day/night; memberships; indoor temperature-
            // controlled pool; 400 m track; WhatsApp on same number), wakaabuja.com/things-to-enjoy-at-monoliza-park-abuja
            // (rides ₦500 per ride; mini golf; lifeguards; second phone +234 817 777 3751), finelib.com/listing/Monoliza-
            // Amusement-Park/43872 (Area 11, near Force Headquarters; bush bar).
            // Coordinates: APPROXIMATE — the "Get Directions" pin on abujaguide.com.ng (9.0498278, 7.5039344); OSM
            // reverse-geocodes it to R.B. Dikko Road, matching the published address.
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — amusement-park entry, one activity (paintball,
            // horse riding or quad biking), a meal and a ride; rough.
            new("abuja", MonolizaName, Nigeria, Abuja, AbujaLangs, 9.0498, 7.5039,
                15m, "Car", 2,
                new[] { "activity_experience", "entertainment_nightlife" },
                new[] { "family_friendly", "adventurous", "group_friendly", "social", "sports_fan", "local_favorite" },
                new[] { "Monoliza", "Monoliza Park", "Monoliza Park Abuja", "Monoliza Abuja", "Monoliza Center", "Monoliza Center Abuja", "Monoliza Amusement Park", "Monaliza Park", "Monaliza Abuja", "Monoliza Garki" }, "Exterior",
                Desc(
                    "Monoliza is a sports and recreation centre on R.B. Dikko Road in Area 11, Garki, Abuja, near the Force Headquarters. It has two sides: a Sports Center with football pitches, tennis and padel courts, an indoor temperature-controlled swimming pool, a basketball court and a 400 m running track; and an Adventure Zone with a small amusement park, VR, PS5 gaming, paintball, horse riding, quad biking and skating. There are restaurants, a bar/lounge and café, a sports shop and an events hall.\n\nWhat to Do: Bring children for the rides, play paintball or ride quad bikes with friends, take a horse ride, book a pitch or court for a game, swim, then eat or drink at the lounge.",
                    "Monoliza is a long-standing Abuja leisure spot — once known as a park and bush bar, now a membership sports club with a family adventure zone. It's a common pick for kids' birthdays and casual five-a-side football.",
                    "Address: 1 R.B. Dikko Street/Road, Area 11, Garki, Abuja (near the Force Headquarters).\n\nGetting There: Take a cab or ride-hail and ask for Monoliza, Area 11. Secure parking is available.",
                    "Hours (official site): Sports Center 7:00 AM–11:00 PM; Adventure Zone 9:00 AM–7:00 PM.\n\nPrices (listing, confirm): Amusement park day pass listed as ₦2,000 and ₦5,000 (adult/child labels look swapped on the listing), ₦5,000–₦10,000 after 6 PM; paintball ₦5,000 (child), ₦10,000 (adult, 50 balls + gear) or ₦15,000 (100 balls + gear); horse riding ₦5,000 per 30 min; quad biking ₦5,000 per 15 min; skating ₦3,000 per hour; tennis ₦15,000 and padel ₦30,000 per session; football pitch ₦25,000–₦55,000.\n\nMemberships: Individual from ₦180,000 for 3 months; couple and family plans available.\n\nBooking & Contact: Book online at monolizaabuja.com, call or WhatsApp +234 817 777 3757, or email info@monolizaabuja.com. Book paintball ahead at weekends.",
                    "Paintball & Quad Bikes: Wear the gear provided and follow the marshals; ask about minimum ages before booking for children.\n\nHorse Riding: Ride only with an instructor, especially if you're new.\n\nPool: Some pool access is tied to memberships — confirm day-visit access before you go.\n\nPrices: Listings conflict and look partly out of date — confirm prices on the official site or by phone.",
                    "Amusement Park: About ₦2,000–₦5,000 by day; ₦5,000–₦10,000 after 6 PM (listing).\nPaintball: ₦5,000–₦15,000.\nHorse Riding / Quad Biking: ₦5,000 each.\nCourts & Pitches: ₦15,000–₦55,000 per session.\nFood & Drinks: Extra.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "National Children's Park and Zoo: Animals, a lake and boat rides in nearby Asokoro.",
                        "Central Park Abuja: Go-karting, paintball and a water park in the Central Business District."
                    },
                    "Late afternoon into the evening for outdoor activities; the dry season (roughly November to March) is most comfortable. Weekends are busiest.",
                    "Medium (6/10) — busier at weekends, evenings and during birthday parties.",
                    "Rating: 5/10 — flat sports areas, but rides, horse riding and quad bikes aren't suitable for limited mobility; confirm pool access.",
                    "2 to 4 hours.")),

            // Central Park Abuja — founder: shooting range; also has more, like paintball and go-karting (used as given,
            // all confirmed). Sources: tiktok.com/@prettyabuja/video/7491942611574410503 (~Apr 2025: go-kart ₦15,000 for 7 min,
            // 500 m track, ages 13+, 12–8 PM; paintball ₦15,000 full gear + 100 pellets, refill ₦8,500; bike rental ₦2,500 / 20 min,
            // bring-your-own ₦8,000 / 1 hr; mini golf ₦4,000; Aqualandia water park ₦25,000 adults / ₦20,000 kids under 12,
            // 12–6 PM; park open daily 10 AM–8 PM; free entry; WhatsApp 0809 272 2852; Plot 174 Kur Mohammed Ave),
            // tiktok.com/@hotperidot/video/7439786873221893432 (late 2024: playground/trampoline ₦7,500, trampoline only ₦3,000;
            // restaurants), sabiabuja.com/central-park-abuja/ (Mar 2024: beside Kebbi Hotel, former GSM Village; free gate entry
            // except holidays/events; arcade price list; Tapas and Ciao Italia restaurants; IG @centralparkabuja),
            // sabiabuja.com/shooting-range-in-abuja/ (Aug 2025: range inside Vitality Clubhouse in Central Park; ₦5,600 per
            // session incl. gun, bullets, training, safety briefing; "bullets used are not real"; 12–10 PM; 0701 771 8888;
            // IG @vitalitycentralpark), finelib.com/listing/Central-Park-Abuja/69811 (phone 0809 272 2852; centralparkabuja.com),
            // wakaabuja.com/?p=256 (Axes and Roses axe throwing & archery; cycling; flower & art gallery).
            // centralparkabuja.com.ng now redirects to an unrelated site — don't link it.
            // Coordinates: APPROXIMATE — OSM Nominatim "Central Park", Central Business District (9.0640203, 7.4913029),
            // beside the OSM line for Kur Mohammed Avenue (9.0647, 7.4947).
            // AvgCost: ~US$30 ≈ ₦41,000 at ~₦1,360 per US$ (Nov 2026) — go-karting, a shooting session or paintball, a meal
            // and a ride; much higher with the water park.
            new("abuja", CentralParkAbujaName, Nigeria, Abuja, AbujaLangs, 9.0640, 7.4913,
                30m, "Car", 2,
                new[] { "activity_experience", "food_experience", "entertainment_nightlife" },
                new[] { "adventurous", "family_friendly", "group_friendly", "social", "couple_friendly", "local_favorite", "tourist_hotspot" },
                new[] { "Central Park", "Central Park Abuja CBD", "Abuja Central Park", "Central Park Kur Mohammed Avenue", "Aqualandia", "Aqualandia Abuja" }, "Landscape",
                Desc(
                    "Central Park Abuja is a large recreation park on Kur Mohammed Avenue in the Central Business District, beside Kebbi Hotel (the former GSM Village). Entry is free and you pay per activity. It has a 500 m go-kart track, a paintball arena, a water park (Aqualandia), a cycling trail with bike rental, a children's playground and trampolines, an arcade, ponds and gardens, and several restaurants including Tapas on the Green and Ciao Italia.\n\nPlaces Inside the Park: Vitality Clubhouse (restaurant, bar, roof terrace, the shooting gallery and a mini-golf course) and Axes & Roses (axe throwing, archery, snooker and darts) are separate venues inside Central Park, each with its own booking line.\n\nWhat to Do: Race go-karts, play paintball, try the shooting range, spend an afternoon at the water park, play mini golf, cycle the trail, and finish with dinner at one of the restaurants.\n\nShooting Gallery: At Vitality Clubhouse — each session includes the gun, non-live rounds, training and a safety briefing.",
                    "Central Park is one of Abuja's busiest weekend spots — families by day, groups of friends for go-karts and paintball, and diners in the evening. Creators often call it the best all-round fun park in the city.",
                    "Address: Kur Mohammed Avenue, Central Business District, Abuja, beside Kebbi Hotel (former GSM Village). Listings differ on the plot number (Plot 174 or 70) — confirm.\n\nGetting There: Take a cab or ride-hail and ask for Central Park, Kur Mohammed Avenue.",
                    "Entry: Free gate entry (reported charges on some holidays and event days) — you pay per activity.\n\nPark Hours: Daily 10:00 AM–8:00 PM; go-karts and paintball from 12:00 PM–8:00 PM; water park 12:00 PM–6:00 PM (reported). Vitality Clubhouse and Axes & Roses keep their own hours (roughly 11 AM or 12 PM to 10 PM — see their entries).\n\nPrices (2025 creator posts and guides): Go-kart ₦15,000 for 7 minutes (ages 13+); paintball ₦15,000 with full gear and 100 pellets, refill ₦8,500; shooting gallery at Vitality Clubhouse ₦5,600 per session (Aug 2025); axe throwing or archery at Axes & Roses ₦3,500 solo / ₦6,000 duo (2025); mini golf ₦4,000; bike rental ₦2,500 per 20 minutes, own bike ₦8,000 per hour; playground and trampoline ₦7,500 per child, trampoline only ₦3,000; water park ₦25,000 adults and ₦20,000 children under 12 (group rates on weekdays).\n\nContact & Booking: WhatsApp 0809 272 2852; Instagram @centralparkabuja; Vitality Clubhouse 0701 771 8888 (Instagram @vitalitycentralpark); Axes & Roses +234 807 103 1090. Book paintball, go-karts, the shooting gallery and axe throwing ahead at weekends.",
                    "Age & Safety Rules: Go-karts are for ages 13+; follow the marshals' briefings for go-karts, paintball, the shooting gallery and axe throwing, and wear the gear provided. Ask about age limits for the shooting gallery and axe throwing before bringing teenagers.\n\nPrices Add Up: Each activity is paid separately and some are expensive — the water park alone is ₦20,000–₦25,000 per person.\n\nWeekends: Queues for go-karts and paintball at weekends — go early or book.\n\nFake Website: centralparkabuja.com.ng no longer belongs to the park — book through the WhatsApp number or Instagram.",
                    "Go-Kart: ₦15,000 per 7-minute session.\nPaintball: ₦15,000 (gear + 100 pellets); refills ₦8,500.\nShooting Gallery (Vitality Clubhouse): ₦5,600 per session (Aug 2025).\nAxe Throwing / Archery (Axes & Roses): ₦3,500 solo / ₦6,000 duo (2025).\nWater Park (Aqualandia): ₦25,000 adults; ₦20,000 children under 12.\nMini Golf: ₦4,000.\nBike Rental: ₦2,500 per 20 minutes.\nPlayground & Trampoline: ₦3,000–₦7,500 per child.\nRestaurants: Extra.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Vitality Clubhouse: Restaurant, roof terrace, mini golf and the shooting gallery, inside Central Park.",
                        "Axes & Roses: Axe throwing, archery, snooker and darts, inside Central Park.",
                        "Nigerian National Mosque: Abuja's golden-domed landmark, a short drive away.",
                        "National Christian Centre: The striking ecumenical church in the Central District.",
                        "Monoliza Sports & Recreation Center: More paintball, horse riding and rides in Area 11, Garki."
                    },
                    "Weekday afternoons for short queues; late afternoon into the evening in the dry season (roughly November to March) is most comfortable. Weekends are busiest.",
                    "Medium to High (7/10) at weekends; Medium (4/10) on weekdays.",
                    "Rating: 6/10 — paved paths and restaurants are easy to reach, but go-karts, paintball and water slides need mobility.",
                    "Half a day to a full day.")),

            // Tukur & Tukur Snake Farm — no founder input beyond the name; researched. Outside the FCT: Gora/Uke, Karu LGA,
            // Nasarawa State, about 40 km from Abuja on the Abuja–Keffi Expressway. City: reuses Abuja (nearest city with a
            // row), as AfricaPocSeeder does for Zuma Rock (Niger State). Sources: ripplesnigeria.com/a-peep-into-buratais-snakes-farm
            // (Sep 2015: Tukur & Tukur Snakes Farm, subsidiary of Tukur & Tukur Farms Ltd; reportedly owned by Lt-Gen Tukur
            // Buratai; mainly a tourist attraction; snake removal and snakebite treatment; venomous species caged separately;
            // fortified glass cages; children's playground; species down to three), newsbalanced.wordpress.com (17 Dec 2020:
            // Buratai visits farm, Gora), hillyvilla.blogspot.com/2021/07/snakes-farm-abuja-keffi-expressway.html (Jul 2021:
            // entry ₦600; ₦300 to hold snakes for photos; cobra, black mamba, python; ostriches, monkeys, porcupines, camels,
            // horses; 9am–5pm daily; Maraba traffic), wakaabuja.com/tukur-and-tukur-snake-farm/ (Uke; ₦1,000–₦1,500 entry;
            // puff adder, green mamba, python; guides supervise; cash only), nigerianheritage.ng/tour-detail.php?id=114
            // (20-hectare estate; supervised interactions; don't touch or provoke animals; map pin).
            // No recent (2024–2026) visitor posts found — confirm it is open to the public before going.
            // Coordinates: APPROXIMATE — map pin embedded on nigerianheritage.ng (8.883741, 7.744870); OSM reverse-geocodes it
            // to the Abuja–Keffi Expressway at Uke, Karu, Nasarawa.
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — a share of a car and driver from Abuja, entry, a snake
            // photo and drinks; rough.
            new("abuja", TukurSnakeFarmName, Nigeria, Abuja, AbujaLangs, 8.8837, 7.7449,
                15m, "Car", 3,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "quirky", "adventurous", "educational", "hidden_gem", "family_friendly", "nature" },
                new[] { "Tukur Snake Farm", "Tukur and Tukur Snake Farm", "Tukur & Tukur Snakes Farm", "Tukur and Tukur Snakes Farm", "Buratai Snake Farm", "Snake Farm Keffi", "Snake Farm Abuja-Keffi Road", "Gora Snake Farm", "Uke Snake Farm" }, "Exterior",
                Desc(
                    "Tukur & Tukur Snake Farm is a reptile farm and mini wildlife park on a roughly 20-hectare agricultural estate beside the Abuja–Keffi Expressway at Gora/Uke, Karu Local Government Area, Nasarawa State — about 40 km east of central Abuja. Part of Tukur & Tukur Farms (reported to belong to Lt-Gen Tukur Buratai, a former Chief of Army Staff), it keeps pythons — including spotted pythons said to reach 5–10 m — and venomous snakes such as cobras, black mambas, puff adders and green mambas behind glass, alongside ostriches, monkeys, porcupines, camels and horses. The farm also offers snake removal from homes and offices.\n\nWhat to Do: Take a guided look at the snake enclosures, hold a (non-venomous) snake for photos with a handler, see the other animals, and let children use the playground.",
                    "It's an unusual day trip from Abuja — the only farm of its kind most locals know of, built mainly for visitors rather than venom production. Information online is thin and mostly from 2015–2021, so it remains something of a hidden gem.",
                    "Location: Abuja–Keffi Expressway at Gora/Uke, Karu LGA, Nasarawa State — about 40 km from central Abuja.\n\nGetting There: Go by car or hire a car and driver from Abuja; the drive passes through Nyanya and Maraba, where traffic is heavy. Agree the round-trip price with the driver before you leave.",
                    "Entry: Reported between ₦600 (2021) and ₦1,000–₦1,500 per person — confirm at the gate. Payment is mostly cash.\n\nSnake Photos: Holding a snake for photos costs extra (₦300 in 2021).\n\nHours (reported): Daily 9:00 AM–5:00 PM.\n\nGuides: Staff guide you round and supervise all handling.\n\nCheck Before You Go: No official website or social media was found, and recent visitor reports are scarce — confirm it is open to the public before making the trip.",
                    "Snake Handling: Only touch or hold a snake when a handler offers it and is holding it with you — never put hands near the venomous enclosures, and don't tap the glass.\n\nChildren: Keep children close and supervised at all times.\n\nRoad Travel: Travel in daylight and leave early to avoid evening traffic at Maraba/Nyanya; check current travel advice for the Abuja–Keffi corridor.\n\nOutdated Information: Most details are several years old — confirm prices and opening before you go.",
                    "Entry: About ₦600–₦1,500 per person.\nSnake Photo: Extra (₦300 in 2021).\nCar & Driver from Abuja (round trip): Extra — agree the price in advance.\nDrinks & Snacks: Extra — bring water.",
                    new[]
                    {
                        "National Children's Park and Zoo: More animals and a boat ride in Asokoro, on the way back into Abuja.",
                        "Millennium Park: Abuja's largest public park, in Maitama."
                    },
                    "Dry-season mornings (roughly November to March); leave Abuja early to beat expressway traffic and return before dark.",
                    "Low (2/10).",
                    "Rating: 4/10 — a rural farm reached only by road, with uneven ground between enclosures.",
                    "1 to 2 hours, plus 1 to 2 hours' travel each way.")),

            // The Wellness Club (TWC) — founder listed "Wellness Club NG"; matched to thewellnessclub.com.ng
            // (the only Nigerian "Wellness Club" found; confirm it's the one meant). Sources:
            // thewellnessclub.com.ng (home), /about-me/ (TWC is "an extension of DermaRx"; service list),
            // /classes/ (nine services), /elementor-1107/ (address 1 Indus Close, off Euphrates Street, Maitama;
            // phone +234 703 722 5957; hours shown as "24/7" — implausible, treated as unconfirmed).
            // No prices, booking terms or reviews found anywhere.
            // Coordinates: APPROXIMATE — OSM Nominatim centroid of Indus Close, Maitama (9.0882953, 7.4736484).
            // AvgCost: ~US$40 ≈ ₦54,000 at ~₦1,360 per US$ (Nov 2026) — one class or recovery session and a ride;
            // prices are unpublished, so this is a rough guess for a premium Maitama studio.
            new("abuja", WellnessClubName, Nigeria, Abuja, AbujaLangs, 9.0883, 7.4736,
                40m, "Car", 2,
                new[] { "activity_experience" },
                new[] { "relaxing", "premium", "solo_friendly", "couple_friendly", "hidden_gem" },
                new[] { "Wellness Club NG", "The Wellness Club", "The Wellness Club Abuja", "TWC Abuja", "TWC Maitama", "The Wellness Club Nigeria", "DermaRx Wellness Club" }, "Interior",
                Desc(
                    "The Wellness Club (TWC) is a wellness studio in Maitama, Abuja, run as an extension of the DermaRx dermatology brand. Its site lists yoga and guided meditation, reformer Pilates, a recovery lounge (IV vitamin drips, wellness teas and juices, ice-bath rooms), cold/ice plunge therapy, spa treatments and facials, private \"Wellness Escape\" rooms for massages and restorative sleep sessions, counselling for individuals, couples and families, and medical aesthetics and dermatology consultations.\n\nWhat to Do: Book a reformer Pilates or yoga class, a massage or facial, or an ice-plunge recovery session; workshops on nutrition and stress management are also run.",
                    "Cold plunges, reformer Pilates and recovery lounges are a new trend in Abuja's wellness scene; TWC puts them under one roof with a skin clinic, aimed at professionals in Maitama and the central districts. Independent reviews are scarce.",
                    "Address: 1 Indus Close, off Euphrates Street, Maitama, Abuja.\n\nBest Access: By cab or ride-hail.",
                    "Booking: Book through the \"Book Now\" page on thewellnessclub.com.ng or call +234 703 722 5957 — no walk-in policy is published.\n\nHours: The site shows \"24/7\", which is unlikely for classes and treatments — confirm before you go.\n\nPrices: Not published for any service — ask when you book.\n\nMedical Services: IV drips, aesthetics and dermatology are clinical treatments — ask who administers them and mention any health conditions.",
                    "Unpublished Prices: Nothing is priced online, so get a quote before you commit, especially for IV therapy and aesthetics.\n\nCold Plunge: Ice baths aren't advised for people with heart or blood-pressure conditions or who are pregnant — check with a doctor first.\n\nLimited Information: Few independent reviews or listings exist — confirm the address and that the service you want is running.",
                    "Classes, Treatments & Recovery Sessions: Prices not published — ask the club.\nIV Vitamin Therapy & Aesthetics: Extra — ask for a quote.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Millennium Park: Abuja's large public park, also in Maitama.",
                        "Sarius Palmetum: Botanical garden on Ibrahim Babangida Boulevard, Maitama."
                    },
                    "Year-round; weekday mornings or evenings around work hours are typical class times — confirm the timetable.",
                    "Low (2/10) — booked sessions and classes.",
                    "Rating: 5/10 — an indoor studio; ask about step-free access and Pilates/ice-bath suitability before booking.",
                    "1 to 2 hours.")),

            // Barracuda Resto Pub — founder: salsa dancing. Official name per Instagram @barracuda_restopub
            // (bio: "Best Sea food Combination & Salsa | Karaoke Spot in Abuja ... FREE WIFI", 10.6K followers).
            // Salsa schedule: danceandartalive.com/abuja-dance-classes (organiser Dance and Art Alive, +234 805 600 2459):
            // Tue Cuban salsa class + Timba social 7:00–10:30 PM ₦3,000; Thu kizomba class + Afro-Latin social
            // 7:00–10:30 PM free; Sat bachata class + Afro-Latin social 7:00–11:00 PM ₦3,000; class 8:00–8:40 PM;
            // bar patronage compulsory; monthly Tue+Sat package ₦20,000 (stated for September). Also
            // salsavida.com event pages (Tue Wuse 2 ₦3,000; Thu Kubwa branch, 59 Gado Nasco Road, free).
            // Address, hours, menu prices: sabiabuja.com/barracuda-resto-pub-abuja-a-review/ (Jul 2025: 111 Adetokunbo
            // Ademola Crescent, Wuse 2; daily 11 AM–midnight; burgers ₦6,500, chicken shawarma ₦4,500, mocktails ₦4,500;
            // phone 0912 235 6336); salsavida/Dance and Art Alive call it "Yelwa Plaza". wakaabuja.com/?p=22467 (Nov 2026)
            // lists Mon–Sat 10 AM–midnight, Sun noon–midnight, Fri karaoke, Sat DJ night — conflicting, hedged.
            // wakaabuja.com/salsa-in-abuja/ (Nov 2026): weekend nights reliably packed.
            // Coordinates: APPROXIMATE — OSM Nominatim point on Adetokunbo Ademola Crescent, Wuse II (9.0754460, 7.4769755);
            // the building isn't mapped.
            // AvgCost: ~US$15 ≈ ₦20,000 at ~₦1,360 per US$ (Nov 2026) — ₦3,000 salsa class, a burger, a drink and a ride.
            new("abuja", BarracudaName, Nigeria, Abuja, AbujaLangs, 9.0754, 7.4770,
                15m, "Car", 2,
                new[] { "entertainment_nightlife", "food_experience" },
                new[] { "nightlife", "music", "social", "local_favorite", "best_at_night", "group_friendly", "solo_friendly" },
                new[] { "Barracuda", "Barracuda Abuja", "Barracuda Restopub", "Barracuda Resto Pub Abuja", "Barracuda Resto Pub Wuse 2", "Barracuda Wuse 2", "Barracuda Kubwa", "Barracuda Resto Pub Kubwa" }, "Interior",
                Desc(
                    "Barracuda Resto Pub is a rooftop restaurant and bar in Wuse 2, Abuja, known for seafood combinations, burgers and shawarma — and for being one of the city's main salsa venues. Dance and Art Alive, Abuja's best-known Latin-dance group, runs its weekly classes and socials here, and the pub also hosts karaoke and DJ nights.\n\nSalsa & Latin Nights (Wuse 2, per Dance and Art Alive): Tuesday — Cuban salsa class and Timba social, 7:00–10:30 PM. Thursday — kizomba class and Afro-Latin social, 7:00–10:30 PM. Saturday — bachata class and Afro-Latin social, 7:00–11:00 PM. Socials start at 7:00 PM, the beginner-friendly class runs about 8:00–8:40 PM, then open dancing and line dances.\n\nWhat to Do: Take the class, stay for the social dancing, eat and drink on the rooftop, or come back for karaoke.",
                    "For Abuja's salsa crowd, Barracuda is a home base — the Tuesday Cuban salsa night is a fixture, and local guides say weekend nights are reliably packed. Beginners turn up alone and get partnered in class.",
                    "Address: 111 Adetokunbo Ademola Crescent, Wuse 2, Abuja (some listings say Yelwa Plaza) — on the rooftop.\n\nBest Access: By cab or ride-hail.\n\nKubwa Branch: Barracuda Resto Pub Kubwa, 59 Gado Nasco Road (opposite Access Bank), hosts a free salsa class and social on Thursdays, 7:30–10:30 PM.",
                    "Salsa Fees: ₦3,000 for class and social on Tuesday and Saturday; Thursday (kizomba in Wuse 2, salsa in Kubwa) is free. Dance and Art Alive also offers a monthly package (₦20,000 for all Tuesday and Saturday sessions, as published for September) — ask for the current one.\n\nBar Patronage: Buying food or drink is compulsory at all dance sessions.\n\nHours: Reported as daily 11:00 AM–midnight (another listing: Mon–Sat 10 AM–midnight, Sun noon–midnight) — confirm.\n\nContact: Venue — 0912 235 6336, Instagram @barracuda_restopub. Dance classes — Dance and Art Alive, +234 805 600 2459, danceandartalive.com.\n\nWhat to Wear: Comfortable clothes and smooth-soled shoes that turn easily.",
                    "Schedules Change: Class styles and nights rotate — check Dance and Art Alive's page or Instagram before you go.\n\nKaraoke/DJ Nights: Sources disagree on which nights karaoke and DJ parties run — confirm.\n\nLate Nights: Sessions end around 10:30–11 PM; arrange your ride home in advance.",
                    "Salsa Class & Social: ₦3,000 (Tue/Sat); free on Thursday (bar purchase required).\nMonthly Dance Package: ₦20,000 (Tue + Sat, as published).\nBurgers: ₦6,500–₦9,500; Chicken Shawarma: ₦4,500; Mocktails: ₦4,500 (Jul 2025 menu).\nSpirits by the Bottle: ₦25,000–₦360,000.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Jabi Lake: Lakeside walks and Jabi Lake Mall, west of Wuse.",
                        "Millennium Park: Abuja's large public park in Maitama."
                    },
                    "Tuesday evening for Cuban salsa, Saturday for bachata, Thursday for kizomba (or the free salsa class in Kubwa). Arrive by 7:30 PM to catch the 8:00 PM class.",
                    "Medium to High (6/10) on dance nights; busiest at weekends.",
                    "Rating: 3/10 — a rooftop venue; ask whether there is a lift.",
                    "An evening — 3 to 4 hours.")),

            // GoWellness Resort — founder: "Go Wellness Abuja [jacuzzi]" (jacuzzi used as given; no web source
            // mentions one). Sources: statepress.ng/2021/03/go-wellness-resort-heaven-for-your-fun.html (21 Kainji Crescent,
            // off Lake Chad Crescent, Maitama; gym, pool with aqua aerobics, spa/massage/aromatherapy/Aquaspin therapy,
            // healthy kitchen, bakery, game room, dance classes incl. belly dancing, GOLiTe weight-loss programme;
            // founder Dr Gloria Praise; phone 0708 836 8274); prezi.com "Introducing GoWellness Resort, Abuja" (same address);
            // gowellnessresort.com (403 to fetch; search snippet: Mon–Sat 6 AM–8 PM, closed Sunday; phones
            // +234 806 578 3265, +234 708 832 8726; second location in Port Harcourt); Tripadvisor listing
            // (via search: daily 6:30 AM–8 PM, ranked #50 of 58 spas in Abuja); Instagram @gowellness_resort
            // (via search: gym & wellness centre, bakery, aromatherapy, pool, spa, restaurant).
            // Coordinates: APPROXIMATE — OSM Nominatim centroid of Kainji Crescent, Maitama (9.0917274, 7.5051776).
            // AvgCost: ~US$25 ≈ ₦34,000 at ~₦1,360 per US$ (Nov 2026) — a spa/jacuzzi or pool session, a healthy meal
            // and a ride; no prices published, rough guess.
            new("abuja", GoWellnessName, Nigeria, Abuja, AbujaLangs, 9.0917, 7.5052,
                25m, "Car", 2,
                new[] { "activity_experience", "food_experience" },
                new[] { "relaxing", "solo_friendly", "couple_friendly", "hidden_gem" },
                new[] { "Go Wellness Abuja", "Go Wellness", "Go Wellness Resort", "Gowellness Resort", "GoWellness Abuja", "GoWellness Maitama", "Gowellness Resorts" }, "Interior",
                Desc(
                    "GoWellness Resort is a day-use wellness centre on Kainji Crescent in Maitama, Abuja, founded by wellness specialist Dr Gloria Praise (a second branch is in Port Harcourt). It combines a gym with exercise and dance classes, a swimming pool used for aqua aerobics, a spa with massage, aromatherapy and a jacuzzi, a healthy kitchen and bakery serving smoothies, juices and grills, and a game room. It also runs GOLiTe, its own weight-loss programme.\n\nWhat to Do: Book a massage and jacuzzi session, join an aqua-aerobics or dance class, work out in the gym, then eat at the healthy kitchen.",
                    "A quiet, low-key Maitama spot better known to Abuja residents following its fitness and weight-loss programmes than to visitors — come for a relaxed spa-and-pool half day rather than a glossy resort.",
                    "Address: 21 Kainji Crescent, off Lake Chad Crescent, Maitama, Abuja.\n\nBest Access: By cab or ride-hail.",
                    "Hours: The resort's site lists Monday–Saturday 6:00 AM–8:00 PM, closed Sunday; Tripadvisor lists daily 6:30 AM–8:00 PM — confirm before you go.\n\nContact: +234 806 578 3265 or +234 708 832 8726; Instagram @gowellness_resort; website gowellnessresort.com.\n\nJacuzzi: Ask for the jacuzzi when you book and confirm it's working on the day.\n\nPrices: Not published — ask for spa, pool and class rates.\n\nBring: Swimwear and a change of clothes if you plan to use the pool or jacuzzi.",
                    "Unpublished Prices: Get the price of each treatment and session before you start.\n\nThin, Older Information: Most published detail dates from around 2021 — confirm which facilities are open.\n\nSunday: Possibly closed on Sundays — check first.",
                    "Spa, Jacuzzi, Pool & Classes: Prices not published — ask the resort.\nHealthy Kitchen & Bakery: Extra.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Millennium Park: Abuja's large public park, also in Maitama.",
                        "Sarius Palmetum: Botanical garden on Ibrahim Babangida Boulevard, Maitama."
                    },
                    "Weekday mornings or afternoons are quietest; avoid Sunday if it's closed.",
                    "Low (3/10).",
                    "Rating: 5/10 — ask about step-free access to the spa and pool areas.",
                    "2 to 4 hours.")),

            // Tuftopia — founder: rug-tufting workshop. Sources: tuftopia.ng and tuftopia.ng/about/ (via search snippets —
            // the site's TLS certificate had expired so pages couldn't be opened directly): "Abuja's first-ever rug tufting
            // and punch needling studio"; workshops, custom rugs, wall hangings and textile art; private events, group
            // bookings and corporate creativity days; studio on Taoreed Lagbaja Boulevard, Jahi; 0816 575 1235,
            // info@tuftopia.ng. Instagram @tuftopia.ng exists (details not readable). Unrelated: Etsy shop "Tuftopia"
            // (Netherlands), tuftopia.net (Corpus Christi, TX). No prices or session lengths found.
            // Coordinates: APPROXIMATE — OSM Nominatim point for Taoreed A. Lagbaja Boulevard, Jahi (9.1131393, 7.4360176).
            // AvgCost: ~US$65 ≈ ₦88,000 at ~₦1,360 per US$ (Nov 2026) — a tufting workshop and a ride; prices unpublished —
            // guess based on Lagos studios charging from ₦100,000 for tufting.
            new("abuja", TuftopiaName, Nigeria, Abuja, AbujaLangs, 9.1131, 7.4360,
                65m, "Car", 2,
                new[] { "activity_experience" },
                new[] { "quirky", "hidden_gem", "social", "group_friendly", "couple_friendly", "solo_friendly" },
                new[] { "Tuftopia Abuja", "Tuftopia NG", "Tuftopia Studio", "Tuftopia Jahi", "Tuftopia Rug Tufting", "Tuftopia Tufting Studio" }, "Interior",
                Desc(
                    "Tuftopia is a rug-tufting and punch-needle studio in Jahi, Abuja, which bills itself as the city's first. In a workshop you draw a design on stretched backing cloth and fill it with yarn using an electric tufting gun, ending with your own rug or wall hanging; the studio also makes custom rugs and textile art to order.\n\nWhat to Do: Book a tufting or punch-needle workshop solo, as a date or with friends, or hire the studio for a birthday, group booking or corporate creativity day.",
                    "Rug tufting is a newer creative-date and group-outing trend in Nigerian cities, alongside paint-and-sip and pottery; Tuftopia brought it to Abuja.",
                    "Address: Taoreed Lagbaja Boulevard, Jahi, Abuja.\n\nBest Access: By cab or ride-hail.",
                    "Booking: Book ahead by phone (0816 575 1235), email (info@tuftopia.ng) or Instagram @tuftopia.ng — no walk-in sessions are published.\n\nPrices & Session Length: Not published — ask when you book. Tufting a rug usually takes a few hours, and finishing (trimming and backing) may mean collecting your piece later — confirm.\n\nWhat to Wear: Clothes you don't mind getting fluff on; tie back long hair near the tufting gun.",
                    "Unpublished Prices: Tufting is usually one of the pricier craft experiences — get the full price, rug size and any finishing fee upfront.\n\nTufting Gun: It's a powered tool — follow the instructor's safety briefing; ask about a minimum age for children.\n\nSimilar Names: Several unrelated businesses abroad use the name Tuftopia — use the .ng contacts.",
                    "Tufting Workshop: Price not published — ask the studio.\nRug Finishing/Backing or Delivery: May be extra — confirm.\nGroup & Corporate Bookings: Quote on request.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Kahera Country Club: Leisure club in nearby Katampe Extension.",
                        "Jabi Lake: Lakeside walks and Jabi Lake Mall, south of Jahi."
                    },
                    "Any time of year; weekends book up first.",
                    "Low (2/10) — booked sessions.",
                    "Rating: 5/10 — seated workshop at a frame; confirm step-free access with the studio.",
                    "3 to 4 hours.")),

            // Paint and Pottery Social — founder: pottery. Not a duplicate of Sarius Palmetum: it's a separate business
            // operating inside the Sarius Palmetum complex. Sources: sabiabuja.com/?p=22356 (Sep 2025: Plot 3703/3704,
            // Sarius Palmetum, Ibrahim Babangida Way, Maitama; Wed–Sun 11 AM–7 PM; Painting on Canvas ₦16,000, Pottery Painting
            // (acrylic) ₦18,500, Pottery Making ₦20,400, Pot Painting & Planting ₦20,000, Pottery Painting (ceramic) ₦23,500;
            // shuttle bus inside the complex; phone 0813 362 7820; Instagram @paintt.potterysocial);
            // wakaabuja.com/paint-and-pottery-social-abuja/ (Nov 2026; same address/hours; group bookings from ₦60,000 for
            // up to 10; pieces kept after firing; other prices there conflict and are not used); instagram.com/paintt.potterysocial
            // (bio: "Pottery and Art Studio ... DM/WhatsApp for bookings Open everyday", 7,156 followers, booking link
            // kindlybook.me/Painttpotterysocial). Hours conflict (Wed–Sun vs "Open everyday") — hedged.
            // Coordinates: Sarius Palmetum's BGCI point (9.10372, 7.48889), as used for the existing SariusName entry —
            // the studio's exact spot inside the complex isn't mapped.
            // AvgCost: ~US$22 ≈ ₦30,000 at ~₦1,360 per US$ (Nov 2026) — a ₦20,400 pottery-making session, garden entry
            // (~₦2,000), a drink and a ride.
            new("abuja", PaintPotteryName, Nigeria, Abuja, AbujaLangs, 9.10372, 7.48889,
                22m, "Car", 2,
                new[] { "activity_experience" },
                new[] { "social", "group_friendly", "couple_friendly", "family_friendly", "solo_friendly", "relaxing", "local_favorite" },
                new[] { "Paint & Pottery Social", "Paint and Pottery Social Abuja", "Paint Pottery Social", "Paint and Pottery Abuja", "Paintt Pottery Social", "Paint & Pottery Sarius Palmetum" }, "Interior",
                Desc(
                    "Paint and Pottery Social is a pottery and art studio inside the Sarius Palmetum botanical garden in Maitama, Abuja. You can make pottery, paint ready-made pottery or ceramics, paint on canvas, or paint a pot and plant it — no experience needed, and everything you make is yours to keep.\n\nWhat to Do: Book a pottery-making or painting session for a date, a friends' hangout, a family outing or some solo downtime, or a group booking for a birthday, bridal shower or team bonding. Combine it with a walk in the garden.",
                    "A popular low-pressure creative outing for Abuja friend groups and couples, helped by the green Sarius Palmetum setting.",
                    "Address: Plot 3703/3704, Sarius Palmetum, Ibrahim Babangida Way (Boulevard), Maitama, Abuja.\n\nGetting In: Cars can drive into the Sarius Palmetum complex; a shuttle bus runs from inside the complex to the studio — call the studio on arrival and tell the driver you're going to Paint and Pottery.",
                    "Hours: Listed as Wednesday–Sunday, 11:00 AM–7:00 PM; the studio's Instagram says \"open everyday\" — confirm.\n\nBooking: Book via WhatsApp/phone 0813 362 7820, Instagram @paintt.potterysocial, or the Kindlybook link in its bio. Walk-ins are sometimes accepted, but book ahead for weekends and groups.\n\nPrices (2025 listing): Painting on canvas ₦16,000; pottery painting (acrylic) ₦18,500; pot painting and planting ₦20,000; pottery making ₦20,400; pottery painting on ceramic ₦23,500.\n\nCollecting Your Pottery: Clay pieces are kept after firing — ask when yours will be ready to collect.\n\nWhat to Wear: Clothes you don't mind getting clay or paint on.",
                    "Garden Entry: You may also pay Sarius Palmetum's own gate fee — confirm.\n\nPrices Vary by Source: Some guides quote much lower prices — check the current list when booking.\n\nFiring Wait: Pottery you shape can't go home the same day — plan a return trip or ask about pick-up.",
                    "Painting on Canvas: ₦16,000.\nPottery Painting: ₦18,500 (acrylic) – ₦23,500 (ceramic).\nPottery Making: ₦20,400.\nPot Painting & Planting: ₦20,000.\nGroup/Private Booking: From about ₦60,000 for up to 10 (reported).\nSarius Palmetum Entry: About ₦2,000 (confirm).\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Sarius Palmetum: The botanical garden around the studio — palms, ponds and monkeys at dusk.",
                        "Millennium Park: Abuja's large public park, also in Maitama."
                    },
                    "Weekday afternoons are quietest; weekends are busiest. Pair it with a late-afternoon walk in Sarius Palmetum.",
                    "Medium (5/10) at weekends; Low (3/10) on weekdays.",
                    "Rating: 5/10 — the shuttle helps inside the garden; confirm step-free access to the studio.",
                    "2 to 3 hours.")),

            // Varuna by The Good Beach — Abuja sister venue of The Good Beach (Lagos beach club). Sources:
            // wakaabuja.com/?p=19360 "Inside Varuna by The Good Beach Abuja" (Nov 2026: 1516 Ahmadu Bello Way, opposite
            // Los Angeles Mall, Mabushi; typically 11 AM–2 AM daily; 0916 578 7474; Instagram @varunabythegoodbeach;
            // cocktails ₦5,000–₦7,000, meals ₦7,000–₦20,000, bottle service ₦30,000–₦500,000, VIP from ₦100,000;
            // Thu–Sun nightlife incl. "Sunset Sundays"/"Tiki Thursdays"; smart-casual; reservations recommended at weekends;
            // parking fills up; review themes: setting and events praised, pricey, loud, crowded at weekends);
            // abujaguide.com.ng/?p=4060 (via search snippets — page 404s now: 1561 Ahmadu Bello Way, Mabushi 900108; artificial
            // beach with white sand and palms; walk-ins from 11 AM; pool ₦5,000 weekdays / ₦10,000 weekends; cabana ₦250,000
            // per session (12–5 PM or 5 PM–2 AM) for up to 8, incl. pool and towels; cabanas must be reserved; review complaint
            // "pricey drinks and added VAT"). Street number conflicts (1516 vs 1561) — hedged.
            // Coordinates: APPROXIMATE — OSM Nominatim point on Ahmadu Bello Way, Mabushi (9.0837021, 7.4621831);
            // Los Angeles Mall / the venue aren't mapped.
            // AvgCost: ~US$30 ≈ ₦41,000 at ~₦1,360 per US$ (Nov 2026) — weekend pool entry ₦10,000, a meal, a cocktail and a ride.
            new("abuja", VarunaName, Nigeria, Abuja, AbujaLangs, 9.0837, 7.4622,
                30m, "Car", 2,
                new[] { "entertainment_nightlife", "food_experience" },
                new[] { "nightlife", "social", "premium", "group_friendly", "relaxing", "best_at_night", "photography" },
                new[] { "Varuna", "Varuna Abuja", "Varuna by Good Beach", "Varuna by The Good Beach Abuja", "Varuna Beach Club", "The Good Beach Abuja", "Good Beach Abuja", "Varuna Mabushi" }, "Exterior",
                Desc(
                    "Varuna by The Good Beach is a beach-themed pool lounge, restaurant and nightlife venue in Mabushi, Abuja — the capital's offshoot of The Good Beach, the Lagos beach club. Landlocked Abuja has no coastline, so Varuna recreates one: white sand, palm trees, a pool, cabanas and lounge beds, with a menu mixing continental, Nigerian and coastal dishes.\n\nWhat to Do: Swim and lounge by day, book a cabana for a group, have dinner and cocktails at sunset, or come Thursday to Sunday for DJ nights, Afrobeats and themed parties.",
                    "Varuna is Abuja's answer to Lagos beach-club culture — a see-and-be-seen weekend spot for pool days, birthdays and nights out. Reviews praise the setting and events but call it pricey, loud and crowded at weekends.",
                    "Address: Ahmadu Bello Way, opposite Los Angeles Mall, Mabushi, Abuja (listed as No. 1516 or 1561).\n\nBest Access: By cab or ride-hail; on-site and street parking fills quickly on busy nights.",
                    "Hours: Typically 11:00 AM–2:00 AM daily — check Instagram for changes.\n\nPool: Walk-ins from 11 AM. Swimming reported at ₦5,000 on weekdays and ₦10,000 at weekends; free with a cabana.\n\nCabanas: Reported at ₦250,000 per session (12–5 PM or 5 PM–2 AM) for up to 8 people, including pool access and towels — must be booked in advance.\n\nNightlife: Thursday to Sunday — themed parties and DJs; some nights may be ticketed.\n\nContact: 0916 578 7474; Instagram @varunabythegoodbeach. Book tables, cabanas and VIP by phone or Instagram DM.\n\nDress Code: Smart-casual for evenings; bring swimwear for the pool. Age policy isn't published — confirm before bringing under-18s, especially at night.",
                    "Price Creep: Drinks are pricey and VAT is added to bills — check the menu and total before you pay.\n\nWeekend Crowds & Noise: Packed and loud on event nights; come on a weekday daytime for a calmer pool visit.\n\nPrices Change: Pool and cabana figures come from guide sites — confirm with the venue.\n\nLate Nights: Arrange your ride home in advance.",
                    "Pool Entry: ₦5,000 (weekdays) / ₦10,000 (weekends), reported.\nCabana (up to 8): ₦250,000 per session, reported.\nCocktails: ₦5,000–₦7,000; Meals: ₦7,000–₦20,000.\nBottle Service: ₦30,000–₦500,000; VIP Section: From ₦100,000.\nVAT on Bills: Extra.\nEvent-Night Tickets: Sometimes — check.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Jabi Lake: Lakeside walks and Jabi Lake Mall, a short drive west.",
                        "Barracuda Resto Pub: Rooftop salsa nights in Wuse 2."
                    },
                    "Weekday afternoons for a quiet pool day; sunset and Thursday–Sunday nights for the party atmosphere. The dry season (roughly November to March) is best for poolside days.",
                    "High (7/10) Thursday–Sunday nights; Low to Medium (4/10) on weekday daytimes.",
                    "Rating: 5/10 — sand and poolside areas are hard going for wheelchairs; ask about step-free routes.",
                    "Half a day, or an evening.")),

            // Siam's Garden — founder listed "Siams Garden, Maitama". The venue trades as both "Siam City Garden"
            // (Instagram/X @siamcityabuja, Tripadvisor, hotels.ng) and "Siam's Garden" (Trip.com, NexTable) at the
            // same address; we use the founder's name and list the other as an alternate — confirm which is current.
            // Sources: instagram.com/siamcityabuja (bio "Naija-Thai Garden Restaurant", "Maitama's Top Garden",
            // "Abuja's Top Friday Salsa Spot", order line 08188880740, highlight "Live Music Wed"; ~7.7k followers;
            // posts through Nov 2026 incl. Afro Salsa Socials collab — fetched 19 Nov 2026);
            // hotels.ng/places/restaurant/653-siam-city-garden (Plot 2044 Aguiyi Ironsi St, opp. Maitama General
            // Hospital; riverside garden restaurant, bar and lounge, bamboo décor, Thai-Naija menu, run by locals and
            // foreigners); tripadvisor.com ...d10255958 Siam_city_Garden (same address, +234 905 353 5388, 4.5+ from
            // a few reviews — via search summary, page 403); mynextable.com/restaurant/siams-garden-2044-aguiyi-ironsi-st-...
            // (11:00 AM–12:00 AM shown for Mon/Tue, ₦15,000–₦25,000 per person, unclaimed listing);
            // au.trip.com/travel-guide/destination/maitama-1863323 ("Siam's Garden", 2044 Aguiyi Ironsi St, 4.3/5 from 7);
            // abujaguide.com.ng/10-must-try-activities-in-abuja/ (lists Siam City among salsa-night venues).
            // Coordinates: APPROXIMATE — OSM Nominatim point for Aguiyi Ironsi Street, Maitama (9.0819803, 7.4903489);
            // the plot itself and Maitama General Hospital are not mapped in OSM.
            // AvgCost: ~US$18 ≈ ₦25,000 at ~₦1,360 per US$ (Nov 2026) — a ₦15k–₦25k meal and drink (NexTable range)
            // plus a ride; rough approximation.
            new("abuja", SiamsGardenName, Nigeria, Abuja, AbujaLangs, 9.0820, 7.4903,
                18m, "Car", 2,
                new[] { "food_experience", "entertainment_nightlife" },
                new[] { "food_focused", "nightlife", "music", "social", "relaxing", "couple_friendly", "hidden_gem" },
                new[] { "Siams Garden", "Siam City Garden", "Siam-city Garden", "Siam City Garden Abuja", "Siam City Abuja", "Siam's Garden Maitama", "Abuja Thai Garden Restaurant" }, "Exterior",
                Desc(
                    "Siam's Garden — also known as Siam City Garden — is an open-air garden restaurant, bar and lounge on Aguiyi Ironsi Street in Maitama, Abuja, opposite Maitama General Hospital. It describes itself as a \"Naija-Thai\" garden restaurant: the menu mixes Thai dishes with Nigerian food, served in a riverside garden with bamboo décor that reviewers call relaxed and beach-like.\n\nWhat to Do: Eat Thai-Nigerian dishes outdoors, have cocktails at the bar, come for live music on Wednesdays or the Friday salsa night, which the venue bills as one of Abuja's top salsa spots. It is also used for birthdays and small celebrations.",
                    "Siam's is one of Maitama's long-running garden spots — part of Abuja's 'garden economy' of open-air venues where people unwind under the trees in the evening. Locals know it for its Friday salsa crowd and laid-back setting rather than for fine dining.",
                    "Address: Plot 2044 Aguiyi Ironsi Street, Maitama, Abuja — opposite Maitama General Hospital.\n\nBest Access: By cab or ride-hail; give the driver the hospital as a landmark.",
                    "Two Names: The same venue appears as \"Siam City Garden\" and \"Siam's Garden\" — search for both.\n\nHours: Listed as 11:00 AM to midnight — confirm, especially for weeknights.\n\nWeekly Nights: Live music on Wednesdays and salsa on Fridays (per the venue's Instagram) — check the latest posts for times and any cover charge.\n\nContact: Instagram @siamcityabuja (orders on 0818 888 0740); Tripadvisor lists +234 905 353 5388.\n\nPayment: Bring cash or a card.",
                    "Mosquitoes: It's an outdoor riverside garden — bring repellent for evenings.\n\nWeather: Seating is mostly open-air, so heavy rain in the wet season can cut a night short.\n\nNot Purely Thai: The menu is a Thai-Nigerian mix — don't expect a strict Thai restaurant.\n\nThin Information: Online reviews are few and listings are partly outdated — confirm hours and events before you go.",
                    "Meal & Drink: About ₦15,000–₦25,000 per person (NexTable listing; not on an official menu).\nSalsa or Live-Music Night: Any cover charge isn't published — ask the venue.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Millennium Park: Abuja's big public park, also in Maitama.",
                        "Sarius Palmetum: A palm-filled botanical garden on Ibrahim Babangida Boulevard, Maitama."
                    },
                    "Evenings, especially Wednesday (live music) or Friday (salsa); the dry season (roughly November to March) is best for sitting outdoors.",
                    "Medium (5/10) — busiest on salsa and live-music nights.",
                    "Rating: 5/10 — garden seating on uneven ground; confirm step-free access and toilets with the venue.",
                    "2 to 3 hours — dinner and drinks, or a full salsa evening.")),

            // Vitality Clubhouse — founder listed "Vitality Clubhouse"; no other founder input. It sits inside Central
            // Park Abuja (Instagram @vitalitycentralpark — the venue is also called "Vitality Central Park").
            // Sources: profolio.ng/jobs/vitality-central-park/facility-officer-abuja (employer's own description:
            // "restaurant, bar, roof terrace, mini-golf course, shooting gallery, and outdoor garden spaces", location
            // "Vitality Clubhouse, Central Park, Abuja" — posted ~Oct 2026); sabiabuja.com/shooting-range-in-abuja/
            // (13 Aug 2025: Plot 174 Kur Mohammed Ave beside Kebbi Hotel, Mon–Sun 12pm–10pm, ₦5,600 per person per
            // session incl. gun, non-live bullets, training and safety briefing; closed-toe shoes; 07017718888);
            // wakaabuja.com/vitality-clubhouse-abuja/ (Nov 2026; same address/phone; conflicting hours 12–10pm vs
            // 8am–10pm; mentions a "9-hole golf course", spa, events, non-member day passes — promotional, partly
            // unverified, so the spa and "golf course" claims are NOT used; its "9-hole" is likely the mini golf);
            // wakaabuja.com/?p=23166 "Golf in Abuja" (via search summary: English-inspired clubhouse, restaurant, bar,
            // rooftop lounge with National Mosque views, 9-hole adventure mini-golf).
            // Coordinates: APPROXIMATE — OSM Nominatim point for Central Park, Central Business District (9.0640203,
            // 7.4913029); the clubhouse's own position in the park is not mapped.
            // AvgCost: ~US$18 ≈ ₦25,000 at ~₦1,360 per US$ (Nov 2026) — one shooting session (₦5,600), a meal and a
            // drink (~₦15,000) and a ride; mini-golf price unpublished; rough approximation.
            new("abuja", VitalityClubhouseName, Nigeria, Abuja, AbujaLangs, 9.0640, 7.4913,
                18m, "Car", 2,
                new[] { "activity_experience", "food_experience", "entertainment_nightlife" },
                new[] { "social", "group_friendly", "couple_friendly", "family_friendly", "adventurous", "photography" },
                new[] { "Vitality Central Park", "Vitality Club House", "Vitality Clubhouse Abuja", "Vitality Clubhouse Central Park", "Vitality Shooting Range", "Vitality Abuja" }, "Exterior",
                Desc(
                    "Vitality Clubhouse is a social and leisure clubhouse inside Central Park Abuja, on Kur Mohammed Avenue in the Central Business District. Its operator describes a restaurant, a bar, a roof terrace, a mini-golf course, a shooting gallery and outdoor garden spaces; guides describe an English-inspired clubhouse whose rooftop looks out towards the National Mosque.\n\nWhat to Do: Try the shooting gallery (guided sessions with non-live rounds, no experience needed), play the adventure mini-golf course, eat at the restaurant, or have drinks on the roof terrace at sunset. It also hosts birthdays, corporate events and private parties.",
                    "For Abuja residents it's the grown-up corner of Central Park — somewhere to eat, drink and try a shooting session or mini golf while the rest of the park is busy with families.",
                    "Address: Central Park, Plot 174 Kur Mohammed Avenue, Central Business District, Abuja — beside Kebbi Hotel.\n\nBest Access: By cab or ride-hail to Central Park; the clubhouse is inside the park.",
                    "Hours: Reported as Monday–Sunday, 12:00 PM–10:00 PM; one guide says 8:00 AM — confirm before you go.\n\nShooting Gallery: ₦5,600 per person per session (Aug 2025), including the gun, non-live rounds, training and a safety briefing. Sessions are timed; guides suggest 2–3 sessions. Arrive a few minutes early for the briefing and wear comfortable clothes and closed-toe shoes. Age limits aren't published — ask.\n\nMini Golf: A 9-hole adventure course; price not published.\n\nMembership: Guides say non-members can visit but some amenities may need a day pass or booking — confirm.\n\nContact: Phone 0701 771 8888; Instagram @vitalitycentralpark.",
                    "Conflicting Information: Guides disagree on hours and on what's offered (some mention a spa and a golf course we couldn't confirm) — check with the clubhouse.\n\nBook at Peak Times: Weekends and evenings get busy; call ahead for shooting slots and events.\n\nPrices Change: Published prices date from 2025 — expect increases.",
                    "Shooting Gallery: ₦5,600 per person per session (Aug 2025).\nMini Golf: Price not published.\nFood & Drinks: Extra — guides quote roughly ₦5,000–₦15,000 per dish (unverified).\nDay Pass / Membership: Not published — ask.\nOther Central Park Activities: Charged separately.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Axes & Roses: Axe throwing and archery, also in Central Park.",
                        "Abuja National Mosque: The city's landmark mosque, a short drive away in the Central Business District."
                    },
                    "Late afternoon into the evening, for the roof terrace at sunset; the dry season (roughly November to March) is most comfortable.",
                    "Medium (5/10) — busier at weekends, on holidays and during events.",
                    "Rating: 5/10 — a park setting with outdoor areas and a roof terrace; confirm lift or step-free access with the clubhouse.",
                    "2 to 3 hours.")),

            // Axes & Roses — founder listed "Axe and Roses"; exact brand is "Axes & Roses" (also "Axes and Roses").
            // Axe throwing confirmed; archery is the other main activity. It sits inside Central Park Abuja.
            // Sources: pulse.ng/story/11-affordable-date-ideas-in-abuja-under-naira20000-2025031913072444536 (Mar 2025:
            // archery ₦3,500, axe throwing ₦3,500, duos ₦6,000, snooker ₦2,000/head, darts ₦2,500/head);
            // centralparkabuja.com/axes-and-roses/ (via search summary only — site wouldn't load: lone axe throwing
            // 3,500, axe throwing duos 6,000, lone archery 3,500, archery duos 6,000, snooker 2,000, darts 2,500; Plot
            // 174 Kur Mohammed Ave next to the former Kebbi Hotel; calls itself Nigeria's first axe-throwing spot —
            // unverified); tiktok.com/@abujacitytourist/video/7283427256561569030 (caption via search: "Rates start at
            // N3500 per person", Central Park, Abuja; Sep 2023); evendo.com/locations/nigeria/abuja/landmark/axes-roses
            // (70 Kur Mohammed Ave, CBD; +2348071031090; Mon–Fri 12pm–10pm, Sat–Sun 11am–10pm per Google profile; 4.7
            // from 24 Google reviews; gear provided, closed-toe shoes, book ahead at weekends);
            // nigerianheritage.ng/tour-detail.php?id=1031 (same hours; archery, axe throwing, snooker, darts; restaurant,
            // food prices "may be high and include VAT"); search summary of Tripadvisor (one review: staff need more training).
            // Coordinates: APPROXIMATE — from the map embed on nigerianheritage.ng (≈9.0644, 7.4887), inside Central Park
            // (OSM Central Park point 9.0640203, 7.4913029).
            // AvgCost: ~US$10 ≈ ₦14,000 at ~₦1,360 per US$ (Nov 2026) — two activities (~₦7,000), a drink and a ride;
            // based on 2025 prices; rough approximation.
            new("abuja", AxesRosesName, Nigeria, Abuja, AbujaLangs, 9.0644, 7.4887,
                10m, "Car", 2,
                new[] { "activity_experience", "entertainment_nightlife" },
                new[] { "adventurous", "quirky", "social", "group_friendly", "couple_friendly", "budget_friendly" },
                new[] { "Axes and Roses", "Axe and Roses", "Axe & Roses", "Axes & Roses Abuja", "Axes and Roses Central Park", "Axes & Roses Axe Throwing" }, "Interior",
                Desc(
                    "Axes & Roses is an axe-throwing and archery venue inside Central Park Abuja, on Kur Mohammed Avenue in the Central Business District. Besides throwing axes at wooden targets and shooting at an archery range, there's snooker, darts, music and an on-site restaurant and bar. The venue's own page bills it as Nigeria's first axe-throwing spot.\n\nWhat to Do: Book a solo or duo round of axe throwing or archery, then play snooker or darts. Staff give a short demonstration and coach beginners. It's a popular pick for unusual dates, birthdays and group outings.",
                    "Axes & Roses is one of Abuja's go-to 'different' date ideas — Nigerian lifestyle sites list it among affordable dates, and visitors rate it highly for the fun and the atmosphere.",
                    "Address: Central Park, Kur Mohammed Avenue, Central Business District, Abuja (listings give Plot 70 or Plot 174 — the park is beside the former Kebbi Hotel).\n\nBest Access: By cab or ride-hail to Central Park.",
                    "Hours: Reported as Monday–Friday 12:00 PM–10:00 PM, Saturday–Sunday 11:00 AM–10:00 PM — confirm.\n\nPrices (2025): Axe throwing ₦3,500 solo or ₦6,000 for a duo; archery ₦3,500 solo or ₦6,000 for a duo; snooker ₦2,000 and darts ₦2,500 per person. Prices may have risen.\n\nSafety: Equipment is provided and staff brief and supervise you. Wear comfortable clothes and closed-toe shoes. Age limits aren't published — ask before bringing children.\n\nBooking: Book ahead, especially at weekends. Phone +234 807 103 1090.",
                    "Follow the Coach: Throw only when told and stay behind the line — axes and arrows are real.\n\nService: The few online reviews are positive, but one mentions staff who could use more training.\n\nFood Prices: The on-site restaurant is reported as pricey, with VAT added.\n\nOld Prices: Published prices are from 2025 — confirm before you go.",
                    "Axe Throwing: ₦3,500 solo / ₦6,000 duo (2025).\nArchery: ₦3,500 solo / ₦6,000 duo (2025).\nSnooker: ₦2,000 per person.\nDarts: ₦2,500 per person.\nFood & Drinks: Extra, with VAT.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Vitality Clubhouse: Restaurant, roof terrace, mini golf and a shooting gallery, also in Central Park.",
                        "Abuja National Mosque: The city's landmark mosque, a short drive away in the Central Business District."
                    },
                    "Weekday afternoons are quieter; evenings and weekends are livelier but busier — book ahead.",
                    "Medium (5/10) — busier in the evenings and at weekends.",
                    "Rating: 5/10 — inside a park; throwing and archery lanes need standing and arm strength. Confirm step-free access with the venue.",
                    "1 to 2 hours.")),

            // Eli & Lulu Café — founder's note: "sip and bake". Confirmed: the café's own events page lists a
            // "Bake & Sip" in "The Creative Series" (12 Nov, 11:00 AM–2:00 PM, year not stated; no price published).
            // Official name on its site: "Eli & Lulu Café" (Instagram "Eli and Lulu's Cafe and Bistro").
            // Sources: eliandlulu.com/home and eliandlulu.com/events (fetched 19 Nov 2026: Habo Mall, Ademola Adetokunbo
            // Crescent, Wuse 2; 0706 422 3698; hello@eliandlulu.com; Mon–Thu 7:30 AM–11:00 PM, Fri–Sun 7:30 AM–12:00 AM;
            // breakfast till 11 AM; Tea Session, Bake & Sip, Sip & Paint, Jazz Night; recurring movie, karaoke and jazz
            // nights; reservations recommended evenings/weekends; bridal showers and birthdays; free Wi-Fi; menu e.g.
            // Yam & Egg Sauce ₦10,500, Asun Jollof ₦16,500); instagram.com/eliandlulus.cafeandbistro;
            // abujaguide.com.ng/listing/eli-and-lulu-cafe-abuja (breakfast buffet ₦34,000 adults, 30% off ages 3–11,
            // daily till 12 PM; full menu; review themes: ambience/bookshelves, kids' play area, slow service at peak);
            // sabiabuja.com/eli-lulu-cafe-abuja/ (Jul 2024: bookshelves, bar, indoor/outdoor seating, ample parking at Habo
            // Mall); tiktok.com/@seeabuja/video/7466189098147974405 (salad bar, Caesar/Chicken/Quinoa salads ₦12,500,
            // iced cappuccino ₦8,000, Americano ₦3,500, croissant French toast ₦10,500, movie nights);
            // sabiabuja.com/iftar-buffet-in-abuja/ (Ramadan iftar buffet ₦30,000 adults / ₦15,000 under-12 — via search).
            // Coordinates: APPROXIMATE — midpoint of two OSM-geocoded points on Adetokunbo Ademola Crescent, Wuse II
            // (9.0717698, 7.4856441 and 9.0720503, 7.4775273); Habo Mall itself is not mapped in OSM.
            // AvgCost: ~US$20 ≈ ₦27,000 at ~₦1,360 per US$ (Nov 2026) — a main (~₦12,500–₦16,500), a coffee or juice
            // (~₦8,000) and a ride; a Bake & Sip ticket (price unpublished) would be extra; rough approximation.
            new("abuja", EliLuluName, Nigeria, Abuja, AbujaLangs, 9.0719, 7.4816,
                20m, "Car", 1,
                new[] { "food_experience", "activity_experience" },
                new[] { "food_focused", "relaxing", "social", "couple_friendly", "family_friendly", "art", "local_favorite" },
                new[] { "Eli and Lulu", "Eli & Lulu", "Eli and Lulu Cafe", "Eli & Lulu Cafe and Bistro", "Eli and Lulu's Cafe and Bistro", "Eli & Lulu's", "Eli and Lulu Abuja", "Eli & Lulu Habo Mall" }, "Interior",
                Desc(
                    "Eli & Lulu Café (also Eli & Lulu's Café and Bistro) is an all-day café and bistro in Habo Mall on Ademola Adetokunbo Crescent, Wuse 2, Abuja. It's known for its styled interiors — bookshelves you can browse while you eat, plants and colourful seating — and a menu that runs from pastries, pancakes and salads to Nigerian soups, jollof and grills, with coffee, juices and cocktails.\n\nWhat to Do: Come for breakfast, brunch or the daily breakfast buffet, work on the free Wi-Fi, or book one of the café's events: \"Bake & Sip\" (a hands-on session where you learn simple baking with drinks), Sip & Paint, guided tea sessions, jazz nights and movie or karaoke nights.\n\nBake & Sip: Part of the café's Creative Series — a 3-hour daytime session (most recently listed as 12 November, 11:00 AM–2:00 PM). Dates, prices and what's included aren't published, so ask the café.",
                    "Eli & Lulu is one of Wuse 2's most photographed cafés — a go-to for brunch dates, birthday breakfasts and bridal showers, and for anyone who likes reading over coffee.",
                    "Address: Habo Mall, Ademola Adetokunbo Crescent, Wuse 2, Abuja.\n\nBest Access: By cab or ride-hail to Habo Mall; guides report ample parking at the mall.",
                    "Hours: Monday–Thursday 7:30 AM–11:00 PM; Friday–Sunday 7:30 AM–midnight. Breakfast is served until 11:00 AM.\n\nBreakfast Buffet: Reported as ₦34,000 per adult, 30% off for children aged 3–11, daily until 12 noon — confirm.\n\nEvents: Bake & Sip, Sip & Paint, tea sessions and jazz nights have limited places — book ahead and ask the price. Check the events page or Instagram for upcoming dates.\n\nReservations: Recommended for evenings and weekends; the café also hosts birthday dinners, bridal showers and small private events.\n\nContact: Phone 0706 422 3698; email hello@eliandlulu.com; Instagram @eliandlulus.cafeandbistro; website eliandlulu.com.",
                    "Pricey: Mains run from about ₦10,500 to ₦20,000 and drinks ₦3,500–₦10,500 — it's a premium café by Abuja standards.\n\nPeak-Hour Service: Reviews mention slow food and stretched staff when it's busy, and the room can feel crowded.\n\nEvent Details: Prices and inclusions for Bake & Sip aren't published — confirm before you pay.",
                    "Breakfast Buffet: About ₦34,000 per adult (30% off ages 3–11).\nMains: About ₦10,500–₦20,800 (e.g. yam & egg sauce ₦10,500, asun jollof ₦16,500, seafood jollof ₦20,800).\nCoffee & Drinks: About ₦3,500–₦10,500 (e.g. Americano ₦3,500, iced cappuccino ₦8,000).\nBake & Sip / Sip & Paint: Ticket price not published — ask the café.\nRamadan Iftar Buffet: Reported as ₦30,000 per adult, ₦15,000 under-12.\nRide-hail within Abuja: Extra.",
                    new[]
                    {
                        "Siam's Garden: Thai-Nigerian garden restaurant in nearby Maitama.",
                        "Millennium Park: Abuja's big public park in Maitama."
                    },
                    "Weekday mornings for a quiet breakfast; weekends for brunch and events (book ahead).",
                    "Medium to High (6/10) — busy at weekend brunch and during events.",
                    "Rating: 7/10 — inside a mall with parking; confirm step-free access and toilets with the café.",
                    "1 to 2 hours; about 3 hours for a Bake & Sip session.")),

            // ─── Add the next destination here ─────────────────────────────────
            // new("<city-slug>", "<Unique Destination Name>", <Country>, <City>, <Langs>, <lat>, <lng>,
            //     <avgCostUsd>m, "<Walk|Car|Public transit|Train|Boat|Flight>", <safety 1-5>,
            //     new[] { "<category_key>", ... },  // reuse existing keys, e.g. nature_outdoor, activity_experience
            //     new[] { "<tag_key>", ... },       // reuse existing keys, e.g. nature, relaxing, family_friendly
            //     new[] { "<alternate spelling>", ... }, "<Landscape|Exterior|Interior>",
            //     Desc(overview, localPerspective, directions, whatToKnow, thingsToBeWaryOf, hiddenCost,
            //          new[] { "<Nearby: why>" }, bestTime, crowd, accessibility, idealDuration)),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Every blob filename this seeder references, for upload to blob storage.</summary>
        public static IReadOnlyList<string> ExpectedImageFiles =>
            Countries.Select(c => $"{c.Slug}_hero.jpg")
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
