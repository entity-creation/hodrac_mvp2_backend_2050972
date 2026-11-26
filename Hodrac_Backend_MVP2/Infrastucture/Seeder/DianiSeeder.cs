using System.Text.Encodings.Web;
using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.Infrastructure.Seeder; // ToTitleCase extension
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// "48 Hours in Diani Beach": a 2-day template wishlist built from the founder's own Diani list
    /// (Diani Beach camels, snorkelling and boats; the Kongo River zipline and sunset; the Africa Pool
    /// at Tiwi; Ali Barbour's Cave Restaurant; Nomad; The Salty Squid; "Golden Karafu"; Robinson Island;
    /// and seven places to stay: Boxo Diani, LalaGalu, Soroi Blue, Msambweni Beach House, Pandanas
    /// Beach Villa, Swahili Beach Resort and Azhari Beach Villa).
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// The Kenya country, KES currency and English/Swahili/Samburu language specs are copied
    /// character for character from MombasaSeeder / KenyaNorthSeeder (the visa summary is the shared
    /// KenyaVisaText.Current), so whichever seeder runs first creates the same rows and the others
    /// reuse them by name.
    /// Places to stay: there is no accommodation category, so stays use the existing "beach_coast"
    /// category (all seven are on or above the beach) plus existing tags.
    /// Cities: Diani (which includes Galu Kinondo, the southern end of the same beach, and Ukunda),
    /// Tiwi (a separate beach area north of the Kongo/Mwachema River, Matuga sub-county) and
    /// Msambweni (a separate town about 30-35 km south). Robinson Island is a sandbank off Diani, not
    /// near Funzi, so there is no Funzi city.
    /// All itinerary costs are rough USD approximations at about KSh 130 per US$ (November 2026).
    ///
    /// Corrections to the founder's names: "Salty Quild" is The Salty Squid Beach Bar &amp; Restaurant;
    /// "Boxo hotel" is Boxo Diani; "Lala Galu" is LalaGalu (one word); "Pandanas" is the listing's own
    /// spelling (not "Pandanus"); the Kongo River zipline is reported in 2025 reviews as run by Village
    /// Retreat Diani (unconfirmed). "Golden Karafu" could not be found under that name (the nearest
    /// match is a seafood restaurant called Karafuu in Ukunda), so it is an optional, unlinked
    /// itinerary item rather than a destination.
    ///
    /// Sources (checked 25 November 2026):
    ///   Diani-Chale Marine National Reserve fees: kws.go.ke/park/diani-chale-national-reserve.
    ///   Boat/snorkel and Robinson Island trips: headout.com (Diani glass-bottom boat to Robinson Island);
    ///     Expedia (Kaaribu Experiences); tourradar.com/t/333377 (Trek Afrika Adventures, from US$55).
    ///   Camel rides: theabroadguide.com (US$30 for ~30 min); safaribookings add-on (US$20).
    ///   Beach sellers, tides, seaweed: hostelworld.com Diani guide (2026); bestkenya.ke Diani guide 2026.
    ///   Kongo River zipline: Tripadvisor (Village Retreat Diani, 2025 reviews, snippets only);
    ///     theabroadguide.com (Kongo River cruise and zipline, ~US$82); Expedia sunset canoe listings.
    ///   Africa Pool: komoot.com/highlight/7224498 (location, low tide, US$2 fee report); Expedia tours;
    ///     snorkeling-report.com (currents, no lifeguard).
    ///   Ali Barbour's: alibarbours.com and alibarbours.com/menus.
    ///   Nomad: nomad-beach-resort.com/dining; Fodor's; Lonely Planet.
    ///   The Salty Squid: saltysquidbeachbar.com; habvari.blogspot.com (Dec 2022 prices).
    ///   Boxo Diani: boxohotels.com/diani-beach.php; hotels.cloudbeds.com/reservation/wMcXGs; trip.com; Expedia snippets.
    ///   LalaGalu: staylala.com; hotels.cloudbeds.com/en/reservation/AHB90J; Hotels.com/Expedia snippets.
    ///   Soroi Blue: expertafrica.com (Soroi Blue); safari.com/soroi-blue-diani (2026-27 rates); soroi.com.
    ///   Msambweni Beach House: expertafrica.com; msambweni-beach-house.com (2019 rates); go2africa.com (2027 guide); trip.com (Nuri).
    ///   Pandanas and Azhari: eastafricanretreats.com (rates valid to 7 Jan 2028); airbnb.com/rooms/22458043.
    ///   Swahili Beach Resort: swahilibeach.com; expertafrica.com; trip.com; Expedia snippet (Feb 2025).
    ///   Galu: lonelyplanet.com (best beaches in Kenya); tui.co.uk (Galu Beach).
    ///   Getting there: flysafarilink.com (Wilson-Ukunda schedule); flightconnections.com (Jambojet NBO-UKA);
    ///     standardmedia.co.ke (Dongo Kundu bypass opened Aug 2024).
    ///   Coordinates: OpenStreetMap/Nominatim, Komoot and Expert Africa. Robinson Island, Pandanas and
    ///     Azhari are approximate (no published coordinates).
    ///   Entry: see KenyaVisaText.
    /// </summary>
    public static class DianiSeeder
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
        private const string Samburu = "Samburu (Maa)"; // only because the shared Kenya spec lists it

        private const string Diani = "Diani";
        private const string Tiwi = "Tiwi";
        private const string Msambweni = "Msambweni";

        // Names checked against every other seeder; none collide.
        private const string DianiBeachName = "Diani Beach";
        private const string KongoName = "Kongo River Zipline & Sunset";
        private const string AfricaPoolName = "Africa Pool, Tiwi Beach";
        private const string AliBarboursName = "Ali Barbour's Cave Restaurant";
        private const string NomadName = "Nomad Beach Bar & Restaurant";
        private const string SaltySquidName = "The Salty Squid Beach Bar & Restaurant";
        private const string RobinsonName = "Robinson Island, Diani"; // not the Robinson Island near Malindi
        private const string BoxoName = "Boxo Diani";
        private const string LalaGaluName = "LalaGalu";
        private const string SoroiBlueName = "Soroi Blue";
        private const string MsambweniHouseName = "Msambweni Beach House";
        private const string PandanasName = "Pandanas Beach Villa";
        private const string SwahiliBeachName = "Swahili Beach Resort";
        private const string AzhariName = "Azhari Beach Villa";

        // Copied character for character from KenyaNorthSeeder.
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
                KenyaVisaText.Current, // shared with the other Kenya seeders; see KenyaVisaText
                "Type G", "Left", 0.16m, new[] { English, Swahili, Samburu }, "KES", "Africa/Nairobi"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Diani, Kenya, -4.3100, 39.5750,
                "A beach resort area in Kwale County on Kenya's south coast, about an hour by road south of Mombasa's airport. Diani Beach Road runs behind the beach from the Kongo River at the north end to Galu Kinondo at the south end, with hotels, beach bars and restaurants along it. Ukunda, on the main Mombasa road, is the market town and has the airstrip. Galu is the quieter southern end of the same beach, so places there are listed under Diani."),
            new(Tiwi, Kenya, -4.2400, 39.6000,
                "A quieter beach area in Kwale County just north of Diani, across the Kongo (Mwachema) River. It has a few lodges and campsites, such as Twiga Lodge, and rock pools on the reef at low tide, including the Africa Pool."),
            new(Msambweni, Kenya, -4.4700, 39.4800,
                "A small town in Kwale County about 30-35 km south of Diani by road. The coast here has low coral cliffs above quiet beaches and only a handful of places to stay."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] SwahiliLangs = { English, Swahili };

        private const string Tides =
            "Tides: At high tide the beach narrows and the lagoon is good for swimming. At low tide the reef flats and rock pools are exposed, and some boat trips only run at certain tides. Check a tide table for Diani before you plan the day.";

        private const string Urchins =
            "Sea Urchins: They're common on the reef flats and in the rock pools. Wear reef shoes or old sandals whenever you walk on the reef.";

        private const string BeachSellers =
            "Beach Sellers: Men on the beach, locally called beach boys, sell boat trips, tours, curios and massages, and a few sell drugs. A firm, friendly 'no, asante' usually works. Book trips through your hotel or an operator you can look up online, and agree the price and what it includes before you pay.";

        private const string Seaweed =
            "Seaweed: Seaweed can wash up during the south-east monsoon, roughly May to September, and the amount changes from week to week. Recent reviews are the best guide.";

        private const string ReserveFee =
            "Marine Reserve Fee: Diani's reef is in the Diani-Chale Marine National Reserve. Kenya Wildlife Service charges African citizens, Nigerians included, US$15 per adult and US$10 per child. Ask whether your boat price includes it.";

        private const string VillaRates =
            "Rates: US$650 a night for the villa self-catering, US$710 with breakfast or US$850 half board, from 8 January to 15 December. From 16 December to 7 January it's US$900, US$960 or US$1,120. These rates are published to 7 January 2028.\n\nMinimum Stay: 2 nights, 4 over Christmas and 3 over Easter.";

        private const string VillaBooking =
            "Booking: Through East African Retreats (eastafricanretreats.com contact form, or +254 724 529 421 / +254 712 472 325).";

        private static readonly DestSpec[] Destinations =
        {
            // ─── Things to do ──────────────────────────────────────────────────
            new("diani", DianiBeachName, Kenya, Diani, SwahiliLangs, -4.3100, 39.5790,
                30m, "Walk", 2,
                new[] { "beach_coast", "nature_outdoor", "activity_experience" },
                new[] { "beach", "relaxing", "family_friendly", "photography", "tourist_hotspot" },
                new[] { "Diani", "Diani Beach Kenya", "Diani Beach Ukunda", "Galu Beach", "Galu Kinondo Beach", "Diani camel ride", "Diani snorkelling" }, "Landscape",
                Desc(
                    "Diani Beach is the main beach on Kenya's south coast, in Kwale County. It's a long stretch of white sand with a coral reef offshore, which keeps the lagoon inside it fairly calm. Diani Beach Road runs behind the sand with the hotels, beach bars and restaurants.\n\nWhat to Do: Ride a camel along the sand, swim in the lagoon when the tide is in, and take a glass-bottom boat or a snorkelling trip out to the reef or the Robinson Island sandbank.",
                    "Our founder's picks on the beach are the camel rides, the snorkelling and the boat rides. The south end, Galu Kinondo, is quieter than the stretch in front of the big central hotels.",
                    "Best Access: Through your hotel or one of the public paths off Diani Beach Road. Ukunda airstrip is about 5-10 minutes away by taxi or tuk-tuk.\n\nFrom Mombasa: Roughly 45-60 minutes by road from Moi International Airport on the Dongo Kundu bypass, which opened in 2024 and avoids the Likoni ferry.",
                    "Camel Rides: Tour sites list about US$20-$30 for roughly half an hour. On the beach the price is negotiated, so agree it in shillings before you get on.\n\nBoat Trips: Two-hour glass-bottom boat and snorkel trips to the reef and the Robinson Island sandbank were listed at about US$23-$35 per adult in 2026.\n\n" + ReserveFee + "\n\n" + Tides,
                    BeachSellers + "\n\n" + Urchins + "\n\n" + Seaweed,
                    "Camel ride: about US$20-$30 (negotiate in KSh).\nGlass-bottom boat and snorkel: about US$23-$35.\nKWS reserve fee: US$15 per adult if not included.\nReef shoes: bring your own.",
                    new[]
                    {
                        "Robinson Island, Diani: A short boat ride out from the beach.",
                        "Nomad Beach Bar & Restaurant: Lunch on the sand.",
                        "Kongo River Zipline & Sunset: At the north end of the beach."
                    },
                    "December to March, when the sea is calmest. July to October is also dry but windier. April to June brings the long rains.",
                    "Medium (5/10); busier over Christmas and Easter.",
                    "Rating: 4/10. Soft sand, reached by paths or steps from most hotels.",
                    "Half a day or longer.")),

            // Operator per 2025 Tripadvisor reviews (Village Retreat Diani); price and contacts unconfirmed.
            new("diani", KongoName, Kenya, Diani, SwahiliLangs, -4.2602, 39.5981,
                25m, "Car", 2,
                new[] { "activity_experience", "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "adventurous", "nature", "photography", "best_evening", "couple_friendly" },
                new[] { "Kongo River", "Kongo River Diani", "Mwachema River", "Diani zipline", "Kongo River zipline", "Village Retreat Diani", "Kongo River sunset", "Kongo Mosque" }, "Landscape",
                Desc(
                    "The Kongo River is the local name for the mouth of the Mwachema River at the north end of Diani Beach, where it meets the sea beside the old Kongo Mosque. Mangroves line the river. A zipline crosses it, and canoe and boat trips go upriver in the late afternoon and come back for sunset.\n\nWhat to Do: Ride the zipline in the late afternoon, then stay at the river mouth or take a canoe for the sunset.",
                    "Our founder comes here for the zipline and the sunset. Reviews from 2025 name Village Retreat Diani, on the Mwachema River, as the zipline operator and call it the first zipline in Diani. Confirm the operator and the price when you book.",
                    "Best Access: At the north end of Diani Beach Road, near Kongo Mosque. A tuk-tuk from central Diani takes about 10-20 minutes, and some tours collect you from your hotel.",
                    "Zipline: Reviews from 2025 put it at about KSh 2,000, or KSh 2,500 with a boat tour. These figures come from reviews, not a published price list.\n\nSunset Trips: Canoe and boat trips through the mangroves were listed at about US$32-$55 per person on booking sites in 2026. One package combines a river cruise and the zipline for about US$82.\n\nMosque: If you visit Kongo Mosque, cover your shoulders and knees and ask before taking photos.",
                    "Swimming: Don't swim at the river mouth. Currents near the estuary can be strong.\n\nMosquitoes: Bring repellent for the evening by the mangroves.\n\nGetting Back: Arrange your tuk-tuk back before it gets dark.",
                    "Zipline: about KSh 2,000 (2025 reviews).\nZipline and boat tour: about KSh 2,500.\nSunset canoe trip: about US$32-$55.\nTuk-tuk each way: extra.",
                    new[]
                    {
                        "Africa Pool, Tiwi Beach: Just across the river to the north.",
                        "Soroi Blue: At the northern half of Diani Beach Road.",
                        "Diani Beach: The beach starts here and runs south."
                    },
                    "Late afternoon, from about 4:00 PM, so you finish at sunset (around 6:30 PM).",
                    "Low to medium (4/10).",
                    "Rating: 3/10. Uneven riverbank, boats and a zipline platform.",
                    "2 to 3 hours.")),

            new("diani", AfricaPoolName, Kenya, Tiwi, SwahiliLangs, -4.2353, 39.6052,
                15m, "Walk", 2,
                new[] { "nature_outdoor", "beach_coast", "viewpoint_scenic_spot" },
                new[] { "nature", "photography", "hidden_gem", "adventurous", "beach" },
                new[] { "African Pool", "African Pool Tiwi", "Africa Pool Tiwi", "African Map Pool", "Tiwi rock pool", "Africa-shaped pool", "Tiwi Beach" }, "Landscape",
                Desc(
                    "The Africa Pool is a natural rock pool in the reef at Tiwi Beach, just north of Diani across the Kongo River. Its outline is roughly the shape of the African continent, and you can only reach it at low tide.\n\nWhat to Do: Walk out across the reef at low tide, take the photo of the pool and get in for a swim.",
                    "Our founder's tip is to time it for low tide and go early. Local guides wait near Twiga Lodge and will walk you out across the reef.",
                    "Best Access: Go to Twiga Lodge on Tiwi Beach, then walk north along the shore and the reef. Tours from Diani take you to Twiga Lodge by tuk-tuk or motorbike.",
                    "Tides: Go only at low tide, and get there before the lowest point so you have time before the water comes back. Check a tide table the day before; if low tide falls in the afternoon, go then.\n\nGuides and Fees: One visitor reported a US$2 entry charge, and the local guides ask for a fee or a tip, so agree the amount first. Organised tours from Diani cost about US$30-$90 with transport and a guide.\n\n" + Urchins,
                    "Incoming Tide: The reef covers again as the tide comes in. Watch the time and leave when your guide says so.\n\nNo Lifeguard: There's no lifeguard, and currents are stronger near the river mouth.\n\nFooting: The walk across the reef is rough and slippery.",
                    "Entry: about US$2 reported (informal).\nLocal guide: agree a price.\nTour from Diani: about US$30-$90.\nReef shoes.",
                    new[]
                    {
                        "Kongo River Zipline & Sunset: Just south, across the river.",
                        "Diani Beach: About 20-30 minutes by road.",
                        "Soroi Blue: The nearest of our Diani stays."
                    },
                    "At low tide; check the tide table. December to March usually has the calmest sea.",
                    "Low (3/10).",
                    "Rating: 2/10. A rough walk across the reef.",
                    "2 to 3 hours, including the walk.")),

            new("diani", AliBarboursName, Kenya, Diani, SwahiliLangs, -4.3132, 39.5769,
                50m, "Car", 1,
                new[] { "food_experience" },
                new[] { "romantic", "couple_friendly", "premium", "food_focused", "best_evening" },
                new[] { "Ali Barbours", "Ali Barbour's", "Ali Barbours Cave", "Ali Barbour's Cave", "Ali Barbours Cave Restaurant Diani", "Cave Restaurant Diani" }, "Interior",
                Desc(
                    "Ali Barbour's Cave Restaurant serves dinner inside a natural coral cave, off Diani Beach Road in central Diani. The menu is mostly seafood and grills: fish of the day, chilli crab, prawns, lobster, lamb and beef.\n\nWhat to Do: Book it for a special dinner, such as the last night of your trip.",
                    "Our founder recommends it for a fine-dining night out. Book ahead, especially in December and at Easter.",
                    "Best Access: Ali Barbours Road, off Diani Beach Road in central Diani. Take a taxi or tuk-tuk; it's a short ride from most Diani hotels.",
                    "Hours: Dinner only, every day, 5:30-11:00 PM.\n\nRules: Each diner orders at least two courses, one of them a main. No children under 6, and no under-12s at the later seating from 8:30 PM.\n\nPrices: The restaurant's online menu lists most mains at KSh 2,050-4,450, lobster thermidor at KSh 6,450 and a seafood platter for two at KSh 6,950. Allow roughly KSh 5,000-7,000 (US$40-$55) a person for two courses and a drink.\n\nBooking: Call +254 714 456131 or email reservations@alibarbours.com.",
                    "Dress Code: The restaurant doesn't publish one; smart casual is safest.\n\nGetting Back: Book your taxi or tuk-tuk back before you sit down.",
                    "Two courses and a drink: about KSh 5,000-7,000 (US$40-$55).\nLobster thermidor: KSh 6,450.\nService and tips: check the bill.",
                    new[]
                    {
                        "Nomad Beach Bar & Restaurant: A few minutes south on Diani Beach Road.",
                        "Swahili Beach Resort: A few minutes north.",
                        "Diani Beach: Across the road."
                    },
                    "Dinner, from 7:00 PM.",
                    "Medium (5/10); fully booked on busy nights.",
                    "Rating: 4/10. It's in a cave, so call ahead about steps if you need step-free access.",
                    "2 to 3 hours.")),

            new("diani", NomadName, Kenya, Diani, SwahiliLangs, -4.3269, 39.5727,
                30m, "Car", 1,
                new[] { "food_experience", "beach_coast" },
                new[] { "food_focused", "relaxing", "couple_friendly", "beach" },
                new[] { "Nomad", "Nomad Diani", "Nomad Beach Bar", "Nomad Restaurant Diani", "Nomad Beach Resort", "The Sands at Nomad" }, "Exterior",
                Desc(
                    "Nomad Beach Bar & Restaurant is the beachfront restaurant at Nomad Beach Resort, formerly The Sands at Nomad, on Diani Beach Road. The menu has seafood from local fishermen, wood-fired pizzas and sushi.\n\nWhat to Do: Come for a long lunch after a morning on the beach.",
                    "Our founder goes for the seafood and the Swahili dishes. The online menu doesn't have a separate Swahili section, so ask what's on that day.",
                    "Best Access: Diani Beach Road, in the south-central part of Diani. A tuk-tuk from Galu takes about 10 minutes.",
                    "Hours: Breakfast 8:00-10:30 AM, then lunch and dinner until 10:30 PM (last orders 9:30 PM).\n\nPrices: Not published online. Budget roughly US$20-$35 a person for a main and a drink.\n\nBooking: Call +254 738 333 888, or message @nomadbeachresort on Instagram.",
                    "Weekends: Fodor's mentions a Sunday lunch buffet, so book ahead at weekends.\n\nSun: Ask for a shaded table at midday.",
                    "Main and a drink: roughly US$20-$35 (prices not published).\nSushi: extra.",
                    new[]
                    {
                        "Ali Barbour's Cave Restaurant: A few minutes north.",
                        "Diani Beach: Right in front.",
                        "The Salty Squid Beach Bar & Restaurant: Further south towards Galu."
                    },
                    "Lunch.",
                    "Medium (5/10).",
                    "Rating: 5/10. Sand around the beach tables.",
                    "1.5 to 2 hours.")),

            new("diani", SaltySquidName, Kenya, Diani, SwahiliLangs, -4.3504, 39.5637,
                22m, "Car", 1,
                new[] { "food_experience", "entertainment_nightlife", "beach_coast" },
                new[] { "food_focused", "social", "best_evening", "beach" },
                new[] { "Salty Squid", "The Salty Squid", "Salty Squid Diani", "Salty Squid Beach Bar", "Salty Quild", "Salty Squid Galu" }, "Exterior",
                Desc(
                    "The Salty Squid Beach Bar & Restaurant is a beach bar on the sand at the southern end of Diani, towards Galu Kinondo. The menu is mostly European, with seafood such as salt-and-pepper squid and grilled king prawns, vegan, gluten-free and children's options, and cocktails.\n\nWhat to Do: Come for a relaxed dinner or drinks on the beach.",
                    "Our founder recommends it. It's cheaper and more casual than Ali Barbour's, so it works for the first night.",
                    "Best Access: Diani Beach Road, south Diani (Galu Kinondo end), about 1.5 km from Boxo Diani and LalaGalu. A tuk-tuk from central Diani takes about 10-15 minutes.",
                    "Hours: Breakfast 7:30-11:30 AM, main menu 12:00-8:30 PM, last drinks 9:30 PM, closing at 10:00 PM.\n\nPrices: A 2022 review put mains at about KSh 1,200-1,300. Expect more now, and budget roughly KSh 2,000-3,500 (US$15-$27) a person.\n\nBooking: Reservations are recommended: +254 758 515 609 (phone or WhatsApp) or Booking@saltysquidbeachbar.com.",
                    "Kitchen Hours: Food stops at 8:30 PM, so don't arrive late for dinner.\n\nGetting Back: Book your ride back before you leave.",
                    "Dinner and a drink: roughly KSh 2,000-3,500 (US$15-$27).\nCocktails: extra.",
                    new[]
                    {
                        "LalaGalu: About 1.5 km south.",
                        "Boxo Diani: About 1.5 km south.",
                        "Nomad Beach Bar & Restaurant: Further north on the beach road."
                    },
                    "Sunset and early dinner, 6:00-8:00 PM.",
                    "Medium (5/10).",
                    "Rating: 5/10. Tables on sand.",
                    "1.5 to 2 hours.")),

            // Approximate coordinates: no published position for the sandbank.
            new("diani", RobinsonName, Kenya, Diani, SwahiliLangs, -4.3250, 39.5850,
                30m, "Boat", 2,
                new[] { "nature_outdoor", "beach_coast", "activity_experience" },
                new[] { "beach", "nature", "adventurous", "photography", "family_friendly" },
                new[] { "Robinson Island", "Robinson Island Diani", "Robinson Island sandbank", "Diani sandbank", "Diani glass-bottom boat", "Robinson Island boat trip" }, "Landscape",
                Desc(
                    "Robinson Island is the name Diani boat operators use for a sandbank in the lagoon off Diani Beach. Glass-bottom boats stop for snorkelling over the reef on the way out and leave you on the sand for about 45 minutes. The sandbank is only above water around low tide. It isn't Funzi Island further south, and it isn't the Robinson Island near Malindi.\n\nWhat to Do: Take a two-hour glass-bottom boat and snorkel trip, or a half-day version with a seafood lunch.",
                    "Our founder lists it as the island trip from Diani. Most trips are short, so if you want a full day on the water, look at Funzi Island or the dhow trips to Kisite-Mpunguti from Shimoni.",
                    "Best Access: Boats leave from the beach in front of several Diani hotels, and most operators collect you from your hotel by tuk-tuk.",
                    "Prices: Two-hour glass-bottom boat and snorkel trips were listed at about US$23-$35 per adult in 2026, with snorkel gear and no lunch. A half-day trip with a seafood or Swahili lunch (Trek Afrika Adventures, on TourRadar) started at about US$55.\n\nTides: Trips are timed for low tide, when the sandbank is out, so the departure time changes from day to day.\n\n" + ReserveFee,
                    BeachSellers + "\n\nSun: There's no shade on the sandbank. Take a hat, water and reef-safe sunscreen.",
                    "Boat and snorkel: about US$23-$35.\nWith lunch: from about US$55.\nKWS fee: US$15 if not included.\nTips for the crew: optional.",
                    new[]
                    {
                        "Diani Beach: Where the boats leave from.",
                        "Nomad Beach Bar & Restaurant: Lunch when you're back.",
                        "Africa Pool, Tiwi Beach: Another low-tide trip, north of Diani."
                    },
                    "Around low tide, on a calm morning. December to March usually has the calmest sea.",
                    "Medium (5/10).",
                    "Rating: 3/10. You board from the beach and get into the water.",
                    "2 hours, or half a day with lunch.")),

            // ─── Places to stay (no accommodation category exists; "beach_coast" is the closest) ──
            new("diani", BoxoName, Kenya, Diani, SwahiliLangs, -4.3635, 39.5587,
                30m, "Car", 1,
                new[] { "beach_coast" },
                new[] { "beach", "budget_friendly", "social", "group_friendly", "solo_friendly" },
                new[] { "Boxo", "Boxo Hotel", "Boxo Hotels", "Boxo Diani Beach", "Boxo Galu", "Boxo container hotel" }, "Exterior",
                Desc(
                    "Boxo Diani is a budget boutique hotel at the Galu end of Diani Beach, built from 25 recycled shipping containers. Rooms are air-conditioned doubles and quads with queen bunk beds, set around a pool, and the beach is under 100 m away. FATFATs, the restaurant and bar on site, is open to the public from 10:00 AM to 10:00 PM.\n\nWhat to Do: Use it as a cheap base for Galu and south Diani.",
                    "Boxo says it was designed for young, independent travellers. The quad rooms suit a group of friends splitting the cost, and breakfast (the 'Munch Box') is included.",
                    "Best Access: Plot 663, Diani Beach Road, Galu Kinondo, next to LalaGalu. About 15-20 minutes by road from Ukunda airstrip.",
                    "Rates: Booking-site prices in 2026 worked out at roughly US$50-$60 a night for a room before taxes, and Expedia listed a US$100 deposit. Check current rates before you book.\n\nBooking: boxohotels.com, email info@boxohotels.com, or call or WhatsApp +254 759 253 507. Instagram: @boxo.hotels.\n\nCheck-in and Check-out: 2:00 PM and 10:00 AM.",
                    "Bunk Beds: Quad rooms use queen bunk beds, so book a double if you don't want to climb.\n\nNoise: The bar is open to the public until 10:00 PM.\n\n" + Seaweed,
                    "Room: roughly US$50-$60 a night before taxes (2026 booking-site prices).\nDeposit: about US$100.\nTaxes: extra.",
                    new[]
                    {
                        "LalaGalu: Next door.",
                        "The Salty Squid Beach Bar & Restaurant: About 1.5 km north.",
                        "Diani Beach: Under 100 m away."
                    },
                    "Year-round; book early for December and Easter.",
                    "Medium (5/10).",
                    "Rating: 5/10. Ask about ground-level rooms.",
                    "A base for 2 nights or more.")),

            new("diani", LalaGaluName, Kenya, Diani, SwahiliLangs, -4.3637, 39.5590,
                65m, "Car", 1,
                new[] { "beach_coast" },
                new[] { "beach", "relaxing", "couple_friendly", "family_friendly" },
                new[] { "Lala Galu", "LalaGalu Diani", "Lala Diani", "La La Diani", "Lala Boxo Diani", "Stay Lala Galu" }, "Exterior",
                Desc(
                    "LalaGalu is a small eco-hotel of about 15 timber cabins at Galu, the quieter southern end of Diani Beach. Each cabin has a king bed, a walk-in rain shower and a private deck, and the pool sits under palms a few steps from the beach. There's also a two-bedroom family cabin with a kitchenette, and SunnySide Up restaurant on site.\n\nWhat to Do: Stay here for a quiet, mid-priced base near the beach.",
                    "It's our suggested base for the 48-hour trip. Galu is quieter than central Diani, and the rest of the plan is a short tuk-tuk ride away.",
                    "Best Access: Plot 663, Diani Beach Road, Galu Kinondo, just after Neptune Hotel and next to Boxo Diani. The hotel's airport shuttle costs about US$20 each way.",
                    "Rates: Listings we found worked out at roughly US$100-$150 a night for two. We couldn't confirm a 2026 rate, so check the booking page. The hotel lists a US$200 deposit, and an extra child under 10 costs US$50 a night.\n\nBooking: staylala.com, info@staylala.com, phone +254 797 162 812 or WhatsApp +254 792 057 665. Instagram: @discover.lala.\n\nSpa and Gym: For resident guests only.",
                    Seaweed + "\n\nAirport Shuttle: About US$20 one way, so book it with your room.",
                    "Cabin for two: roughly US$100-$150 a night (not confirmed for 2026).\nDeposit: US$200.\nAirport shuttle: about US$20 one way.\nExtra child under 10: US$50 a night.",
                    new[]
                    {
                        "Boxo Diani: Next door.",
                        "The Salty Squid Beach Bar & Restaurant: About 1.5 km north.",
                        "Diani Beach: A few steps away."
                    },
                    "Year-round; book early for December and Easter.",
                    "Low (3/10).",
                    "Rating: 5/10. Cabins with decks; ask about steps.",
                    "A base for 2 nights or more.")),

            new("diani", SoroiBlueName, Kenya, Diani, SwahiliLangs, -4.2711, 39.5972,
                250m, "Car", 1,
                new[] { "beach_coast" },
                new[] { "beach", "premium", "couple_friendly", "relaxing", "family_friendly" },
                new[] { "Soroi Blue Diani", "Soroi Blue Hotel", "Soroi Blue Diani Beach", "Soroi Diani" }, "Exterior",
                Desc(
                    "Soroi Blue is a boutique beach hotel in the northern half of Diani, run by the Soroi Collection, which also has safari lodges. It has about 11 rooms in a Swahili and Mediterranean style, gardens that open onto the beach, a pool, a spa, a restaurant and a bar. Halal and kosher meals are available.\n\nWhat to Do: Book it for a quiet stay on the beach. The hotel arranges snorkelling, kitesurfing and dhow trips to Kisite-Mpunguti.",
                    "The rooms suit different groups: the Chale room for couples, junior suites for families, and the Bahari Blue suite, two connecting rooms, for up to four adults and a child.",
                    "Best Access: On the beach in the northern half of Diani, about 10 minutes by road from Ukunda airstrip. The Kongo River is a short ride north.",
                    "Rates: Safari.com listed US$215 per person sharing per night from 1 November to 19 December 2026, US$595 over Christmas and New Year, and US$420 from January to March 2027. The listing isn't clear on whether lunch is included, so confirm the meal plan.\n\nBooking: reservations@soroi.com or +254 732 557 714 (phone and WhatsApp).",
                    "Festive Prices: The rate is almost three times higher over Christmas and New Year.\n\n" + Seaweed,
                    "Per person sharing: about US$215-$595 a night, depending on season.\nDhow and snorkel trips: extra.\nSpa: extra.",
                    new[]
                    {
                        "Kongo River Zipline & Sunset: A short ride north.",
                        "Africa Pool, Tiwi Beach: Across the river at Tiwi.",
                        "Swahili Beach Resort: A few minutes south."
                    },
                    "November to mid-December has the lower rates; December to March has the calmest sea.",
                    "Low (3/10).",
                    "Rating: 5/10. Ask the hotel about step-free rooms.",
                    "A base for 2 nights or more.")),

            new("diani", MsambweniHouseName, Kenya, Msambweni, SwahiliLangs, -4.5082, 39.4695,
                500m, "Car", 1,
                new[] { "beach_coast" },
                new[] { "beach", "premium", "romantic", "relaxing", "group_friendly", "hidden_gem" },
                new[] { "Msambweni Beach House & Private Villas", "Msambweni Beach House Kenya", "Villa MB", "Villa MB Msambweni", "Nuri Boutique Hotel & Villas", "Nuri Msambweni" }, "Exterior",
                Desc(
                    "Msambweni Beach House is a small luxury property on a low coral cliff above a quiet beach at Msambweni, about 30-35 km south of Diani. The main house has three double rooms and a separate Ocean Suite, and there are three private villas with their own pools: two with two bedrooms and one with three. Meals are full board, and there's a large infinity pool, a clifftop platform for sundowners and a path down to the beach.\n\nWhat to Do: Book it for a private, very quiet stay, or take a whole villa with friends or family.",
                    "Expert Africa says it was renamed Villa MB in 2022, and some booking sites now list it as Nuri Boutique Hotel & Villas. Confirm the current name and who runs it when you book.",
                    "Best Access: Msambweni, Kwale County, about 45 minutes by road south of Ukunda airstrip. It's too far from Diani for dinners out every night, so plan to eat in.",
                    "Rates: Go2Africa's 2027 guide price was about US$484-$704 per person sharing per night, fully inclusive. The rates on the official site date from 2019 and are out of date.\n\nBooking: msambweni-beach-house.com (call-back form) or +254 723 697 346.\n\nChildren: Balconies aren't suitable for children under 6, and Villa 3 suits families best.",
                    "Distance: You'll need a car or driver to reach Diani's restaurants and the Kongo River.\n\nNames: Search all three names when you look for recent reviews.",
                    "Per person sharing: about US$480-$700 a night, full board (2027 guide price).\nTransfers from Ukunda or Mombasa: extra.\nExcursions: extra.",
                    new[]
                    {
                        "Diani Beach: About 45 minutes north by road.",
                        "LalaGalu: The nearest of our Diani stays.",
                        "Robinson Island, Diani: Boats leave from Diani."
                    },
                    "December to March for the calmest sea.",
                    "Low (2/10).",
                    "Rating: 3/10. A clifftop site with steps down to the beach.",
                    "3 nights or more.")),

            // Approximate coordinates (exact position given on booking).
            new("diani", PandanasName, Kenya, Diani, SwahiliLangs, -4.3050, 39.5800,
                325m, "Car", 1,
                new[] { "beach_coast" },
                new[] { "beach", "romantic", "couple_friendly", "premium", "relaxing" },
                new[] { "Pandanas Villa", "Pandanus Beach Villa", "Pandanus Villa Diani", "Pandanas Beach Villa Diani", "Pandanas Diani" }, "Exterior",
                Desc(
                    "Pandanas Beach Villa is a private one-bedroom villa for two on the beach at Diani, let through East African Retreats. It has an air-conditioned bedroom with a 2 m bed, an outdoor jungle shower, an open-plan living room and kitchen, a deck with loungers and a swing, and its own plunge pool. A private path leads to a beach lounge shared with other guests, and return transfers from Ukunda airstrip are included.\n\nWhat to Do: Book it for a honeymoon or a couple's trip when you want your own space.",
                    "Our founder spells it Pandanas, and so does the listing. East African Retreats also lets Azhari Beach Villa at the same rates.",
                    "Best Access: Beachfront Diani, about 10 minutes from Ukunda airstrip. You get the exact address when you book.",
                    VillaRates + "\n\n" + VillaBooking + " Instagram: @eastafricanretreats.",
                    "Two Guests Only: It sleeps two, with no extra beds.\n\nPower Cuts: They happen on the coast; the villa has a backup generator.",
                    "Villa for two: US$650-$1,120 a night, depending on season and meals.\nTransfers from Ukunda: included.\nDinners out: extra.",
                    new[]
                    {
                        "Azhari Beach Villa: Another villa for two from the same agency.",
                        "Ali Barbour's Cave Restaurant: Dinner in central Diani.",
                        "Diani Beach: Down the path."
                    },
                    "8 January to 15 December has the lower rate; December to March has the calmest sea.",
                    "Low (2/10).",
                    "Rating: 4/10. Ask about steps and the path to the beach.",
                    "2 nights minimum.")),

            new("diani", SwahiliBeachName, Kenya, Diani, SwahiliLangs, -4.2924, 39.5860,
                190m, "Car", 1,
                new[] { "beach_coast" },
                new[] { "beach", "premium", "couple_friendly", "family_friendly", "relaxing" },
                new[] { "Swahili Beach", "Swahili Beach Diani", "Swahili Beach Hotel", "Swahili Beach Resort Diani", "Swahili Beach Ukunda" }, "Exterior",
                Desc(
                    "Swahili Beach Resort is a large five-star resort in central Diani, opened in 2011, with about 140 rooms and suites in Swahili and Arabic-style buildings. Its pools step down in tiers to the beach, and there's a large spa and a gym. Rooms range from standard rooms to suites with kitchenettes and a penthouse.\n\nWhat to Do: Choose it if you want a full-service resort with everything on site.",
                    "It's popular for weddings, conferences and honeymoons. Expert Africa rates it best for children aged 8 and over, because the grounds are spread out and lifeguard cover at the pools is limited.",
                    "Best Access: Diani Beach Road, central Diani, about 5-10 minutes from Ukunda airstrip.",
                    "Rates: A February 2025 Expedia listing started at about €385 a night for two adults. Expect roughly US$300-$450 or more a night for two on half board, and check current prices.\n\nBooking: swahilibeach.com, +254 111 050 140, or Instagram @swahilibeach.",
                    "High Tide: The beach in front is narrower than elsewhere in Diani, and the sea covers part of it at high tide.\n\nQuotes: Get the price and the meal plan in writing before you pay.",
                    "Room for two: roughly US$300-$450 or more a night, half board (not confirmed for 2026).\nSpa: extra.\nDrinks: extra on half board.",
                    new[]
                    {
                        "Ali Barbour's Cave Restaurant: A few minutes south.",
                        "Soroi Blue: A few minutes north.",
                        "Kongo River Zipline & Sunset: A short ride north."
                    },
                    "Year-round; December and Easter are the busiest.",
                    "Medium (6/10).",
                    "Rating: 5/10. Spread-out grounds with steps between the pool levels.",
                    "A base for 2 nights or more.")),

            // Approximate coordinates (exact position given on booking).
            new("diani", AzhariName, Kenya, Diani, SwahiliLangs, -4.3000, 39.5820,
                325m, "Car", 1,
                new[] { "beach_coast" },
                new[] { "beach", "romantic", "couple_friendly", "premium", "relaxing" },
                new[] { "Azhari Villa", "Azhari Cottage", "Azhari Beach Suite", "Azhari Diani", "Azhari Beach Villa Diani" }, "Exterior",
                Desc(
                    "Azhari Beach Villa is a one-bedroom villa for two on a 3-acre beachfront plot in Diani, designed by Nicole and Simon Englefield; Nicole manages it on site. It has an air-conditioned bedroom with a super-king bed, an open-air living and dining area with a kitchen, an infinity plunge pool with a waterfall, a yoga deck and a jungle shower. A private path leads to the beach and a shared beach lounge, and there's daily housekeeping.\n\nWhat to Do: Book it for a honeymoon or a quiet stay for two.",
                    "On Airbnb it was rated 4.99 from 112 reviews in November 2026.",
                    "Best Access: Beachfront Diani, a short drive from Ukunda airstrip. You get the exact location when you book.",
                    VillaRates + "\n\n" + VillaBooking + " It's also on Airbnb (listing 22458043).",
                    "Neighbours: The Airbnb listing says a hotel next door has evening entertainment until about 11:30 PM.\n\nTwo Guests Only: It sleeps two.",
                    "Villa for two: US$650-$1,120 a night, depending on season and meals.\nAirbnb fees: extra if you book there.\nDinners out: extra.",
                    new[]
                    {
                        "Pandanas Beach Villa: Another villa for two from the same agency.",
                        "Ali Barbour's Cave Restaurant: Dinner in central Diani.",
                        "Diani Beach: Down the path."
                    },
                    "8 January to 15 December has the lower rate; December to March has the calmest sea.",
                    "Low (2/10).",
                    "Rating: 4/10. Ask about steps and the path to the beach.",
                    "2 nights minimum.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlist
        // ═══════════════════════════════════════════════════════════════════════

        // ItineraryItem.TimeOfDay is a controlled value (Morning/Afternoon/Evening — validated by the
        // admin and user itinerary endpoints), so any specific clock times go in the item titles.
        // Costs are rough USD per person at ~KSh 130 per US$; stays are per person for 2 nights sharing.
        // Base price: LalaGalu 2 nights shared ~125, airstrip transfers ~20, camel ~25, zipline ~15,
        // Africa Pool guide and ride ~15, boat and snorkel ~30, Salty Squid ~22, Nomad ~28,
        // Ali Barbour's ~50, tuk-tuks ~15 = ~345, rounded to 350.
        private static readonly WishlistSpec[] Wishlists =
        {
            new("diani", "48 Hours in Diani Beach", Kenya,
                "Two days on Diani Beach, built from our founder's own list. Day 1 is the beach itself, with a camel ride and a swim in the lagoon, then the zipline over the Kongo River, sunset at the river mouth and dinner at The Salty Squid. Day 2 starts at the Africa Pool on Tiwi Beach at low tide, then lunch at Nomad, a boat or snorkel trip off the beach and dinner in the coral cave at Ali Barbour's. If you'd rather spend the morning on the water, swap the Africa Pool for a glass-bottom boat to the Robinson Island sandbank. We suggest LalaGalu, at the quiet Galu end, as the base. Boxo Diani next door is the budget choice, and Soroi Blue, Swahili Beach Resort, the Pandanas and Azhari villas and Msambweni Beach House are there for bigger budgets.\n\nFor a longer coast trip, pair it with our five-day \"Mombasa: Old Town, Fort Jesus and the North Coast\" plan. Diani is roughly an hour's drive south of Mombasa's airport.\n\nGetting there: There are no direct flights from Nigeria. Kenya Airways flies from Lagos to Nairobi, and from Nairobi Jambojet flies nonstop from Jomo Kenyatta International Airport to Ukunda airstrip (Diani) in about 1 hour 15 minutes, while Safarilink flies twice a day from Wilson Airport. One-way fares to Ukunda were roughly US$80-$180 on booking sites in late 2026. Ukunda is 5-10 minutes from the hotels. You can also fly into Mombasa and drive about 45-60 minutes south on the Dongo Kundu bypass, which opened in 2024 and avoids the queues at the Likoni ferry.\n\nEntry: Nigerians visiting Kenya for 60 days or less don't need an eTA. Since October 2026 every traveller, Nigerians included, must buy Kenyan government travel health insurance before arrival on insurance.ecitizen.go.ke. It cost about US$52 including fees at launch (confirm the current price on the portal), and your own travel insurance isn't accepted in its place. Bring your yellow fever card.\n\nOn the beach: Check a tide table before you plan each day. The Africa Pool and the Robinson Island sandbank only work around low tide, and the lagoon is best for swimming when the tide is in. Wear reef shoes on the reef flats, where there are sea urchins. Men on the beach, known locally as beach boys, sell boat trips and tours; buy from your hotel or an operator you can look up, and agree the price and what it includes before you go.\n\nWhen to go: December to March is dry, with the calmest sea. July to October is also dry but windier, which kitesurfers like, and seaweed can wash up between about May and September. April to June is the long rains and the cheapest time. Prices are rough US$ estimates per person for two days sharing a room, at about KSh 130 per US$, and exclude flights.",
                "Camels on the sand, a zipline over the Kongo River, the Africa Pool at low tide and dinner in a coral cave: two days on Diani Beach.",
                "Couples & Friends",
                new[] { "2 Nights at LalaGalu (shared cabin)", "Camel Ride on Diani Beach", "Kongo River Zipline", "Africa Pool Guide at Tiwi", "Glass-Bottom Boat & Snorkel Trip", "Tuk-Tuk and Airstrip Transfers" },
                "Diani, Diani Beach, Kenya, Kenyan south coast, Kwale, Ukunda, Galu, Galu Kinondo, Tiwi, Tiwi Beach, Msambweni, camel ride, snorkelling, glass-bottom boat, Robinson Island, sandbank, Kongo River, Mwachema River, zipline, Village Retreat Diani, sunset, Africa Pool, African Pool, Twiga Lodge, low tide, Ali Barbour's Cave Restaurant, Nomad Beach Bar, The Salty Squid, Golden Karafu, Karafuu, Boxo Diani, LalaGalu, Soroi Blue, Msambweni Beach House, Pandanas Beach Villa, Swahili Beach Resort, Azhari Beach Villa, beach boys, sea urchins, reef shoes, Diani-Chale Marine Reserve, Jambojet, Safarilink, Dongo Kundu bypass, Likoni ferry, 48 hours, weekend, Nigerians, Lagos to Diani",
                new[] { "Beach", "Food", "Adventure", "Romance" },
                350m, 75m,
                "2 nights (shared cabin) at LalaGalu, Galu Beach. Other options: Boxo Diani (budget), Soroi Blue, Swahili Beach Resort, Pandanas Beach Villa, Azhari Beach Villa and Msambweni Beach House",
                "Airstrip or airport transfers, and tuk-tuks around Diani and to Tiwi. Flights from Lagos or Abuja to Nairobi, and on to Ukunda or Mombasa, are not included.",
                "Diani Beach camel ride and lagoon swim, Kongo River zipline and sunset, Africa Pool at Tiwi at low tide, glass-bottom boat and snorkel trip, dinners at The Salty Squid and Ali Barbour's Cave Restaurant, lunch at Nomad; optional Robinson Island sandbank trip",
                false, "Coastal Explorer",
                new[]
                {
                    new DaySpec(1, "Arrive · Diani Beach & a Kongo River Sunset", Diani, Diani, Diani, new[]
                    {
                        new ItemSpec("Morning", "Fly into Ukunda (Diani) Airstrip", "Jambojet from Nairobi's main airport or Safarilink from Wilson takes about 1 hour 15 minutes. Ukunda is 5-10 minutes from the hotels. Coming from Mombasa instead? Allow 45-60 minutes by road on the Dongo Kundu bypass.", 10m, "Arrival"),
                        new ItemSpec("Morning", "Check In at LalaGalu", "Timber cabins with private decks and a pool, a few steps from Galu Beach. Leave your bags if the cabin isn't ready. Roughly US$100-$150 a night for two.", 125m, "Your Base", LalaGaluName),
                        new ItemSpec("Morning", "Or: Boxo Diani (Budget)", "Shipping-container rooms with a pool next door to LalaGalu, roughly US$50-$60 a night for a room. Quad rooms with bunk beds suit groups.", 55m, "Budget Option", BoxoName, true),
                        new ItemSpec("Morning", "Or: Soroi Blue (Boutique)", "About 11 rooms on the beach in the northern half of Diani, from about US$215 per person a night outside the festive season.", 430m, "Boutique Option", SoroiBlueName, true),
                        new ItemSpec("Morning", "Or: Swahili Beach Resort (Five-Star)", "A large resort in central Diani with tiered pools down to the beach, roughly US$300-$450 or more a night for two on half board.", 375m, "Resort Option", SwahiliBeachName, true),
                        new ItemSpec("Morning", "Or: Pandanas or Azhari Beach Villa (Couples)", "Private one-bedroom beach villas for two with plunge pools, both let through East African Retreats from US$650 a night for the villa, 2-night minimum.", 650m, "Couples' Villa", PandanasName, true),
                        new ItemSpec("Morning", "Or: Msambweni Beach House (Private, Further South)", "A clifftop beach house and private villas about 45 minutes south of Ukunda, full board, about US$480-$700 per person a night.", 1000m, "Splurge", MsambweniHouseName, true),
                        new ItemSpec("Afternoon", "Diani Beach: Camel Ride & a Swim", "Agree the camel ride price in shillings before you get on (tour sites list about US$20-$30 for half an hour). Swim in the lagoon while the tide is in, and wear reef shoes on the reef flats.", 25m, "Founder's Pick", DianiBeachName),
                        new ItemSpec("Evening", "4:00 PM · Zipline over the Kongo River", "At the north end of Diani, by Kongo Mosque. Reviews from 2025 put the zipline at about KSh 2,000, or KSh 2,500 with a boat tour; confirm when you book.", 15m, "Founder's Pick", KongoName),
                        new ItemSpec("Evening", "6:00 PM · Sunset at the River Mouth", "Stay by the river mouth for sunset, or book a sunset canoe trip through the mangroves (about US$32-$55 on booking sites). Don't swim at the river mouth.", 0m, "Sunset", KongoName),
                        new ItemSpec("Evening", "7:30 PM · Dinner at The Salty Squid", "Seafood and cocktails on the sand at the south end of Diani, about 1.5 km from LalaGalu. The kitchen closes at 8:30 PM, so book ahead on +254 758 515 609.", 22m, "Founder's Pick", SaltySquidName),
                        new ItemSpec("Evening", "Or: Golden Karafu", "Another of our founder's picks. We couldn't find it listed under that name (there's a seafood restaurant called Karafuu in Ukunda), so check Google Maps or ask at your hotel before you go.", 20m, "Optional", null, true),
                    }),
                    new DaySpec(2, "Africa Pool, Nomad & Dinner in a Cave", Tiwi, Diani, Diani, new[]
                    {
                        new ItemSpec("Morning", "Low Tide · Africa Pool at Tiwi Beach", "Check the tide table the night before and get there before low tide; if low tide falls in the afternoon, swap this with the boat trip. Ride to Twiga Lodge, then walk north along the reef with a local guide, and wear reef shoes.", 15m, "Founder's Pick", AfricaPoolName),
                        new ItemSpec("Morning", "Or: Glass-Bottom Boat to Robinson Island", "About two hours of snorkelling over the reef and time on the sandbank, which is only out around low tide. About US$23-$35 on booking sites; a half-day trip with a seafood lunch starts at about US$55.", 30m, "Optional", RobinsonName, true),
                        new ItemSpec("Afternoon", "1:00 PM · Lunch at Nomad Beach Bar & Restaurant", "Seafood from local fishermen, sushi and wood-fired pizza on the beach. Our founder goes for the seafood and the Swahili dishes. Prices aren't published; budget about US$20-$35.", 28m, "Founder's Pick", NomadName),
                        new ItemSpec("Afternoon", "Snorkel or Boat Ride off Diani Beach", "Book a glass-bottom boat and snorkel trip through your hotel or a known operator, timed for the tide. Ask whether the KWS marine reserve fee (US$15 per African adult) is included.", 30m, "On the Water", DianiBeachName),
                        new ItemSpec("Evening", "7:30 PM · Dinner at Ali Barbour's Cave Restaurant", "Dinner inside a coral cave. Book ahead on +254 714 456131. Each diner orders at least two courses, most mains are KSh 2,050-4,450, and children under 6 aren't admitted.", 50m, "Dinner in a Cave", AliBarboursName),
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

        public static async Task SeedAsync(HodracDbContext db, CancellationToken ct = default, ILogger? logger = null)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                var ownsTransaction = db.Database.CurrentTransaction == null;
                await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

                await SeedCoreAsync(db, logger, ct);

                if (tx != null) await tx.CommitAsync(ct);
            });
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Implementation
        // ═══════════════════════════════════════════════════════════════════════

        private static async Task SeedCoreAsync(HodracDbContext db, ILogger? logger, CancellationToken ct)
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
                else if (spec.Name == Kenya)
                {
                    // Narrow refresh: only replaces a visa text an earlier seeder version wrote.
                    KenyaVisaText.RefreshIfUnchanged(country, logger);
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
