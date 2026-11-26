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
    /// "Mombasa: Old Town, Fort Jesus and the North Coast": a 5-day template wishlist built from the
    /// founder's own Mombasa list (Fort Jesus, Old Town, Forodhani Restaurant, Nalka Café, the Mombasa
    /// Tusks ("Pembeni"), Mama Ngina Waterfront and the Lighthouse, Marikiti Market, the Sikh gurdwara,
    /// Wild Waters, Haller Park, Mamba Village, CityBlue Creekside and Mombasa Marine National Park).
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// The Kenya country, KES currency and English/Swahili/Samburu language specs are copied
    /// character for character from KenyaNorthSeeder (the visa summary is the shared
    /// KenyaVisaText.Current), so whichever seeder runs first creates the same rows and the other
    /// reuses them by name.
    /// The founder's KSh prices are first-hand and used as the headline figures; where published
    /// prices differ a lot, the destination text says so. All itinerary costs are rough USD
    /// approximations at about KSh 130 per US$ (mid-November 2026).
    ///
    /// Sources (checked 24 November 2026):
    ///   Fort Jesus fees/hours: National Museums of Kenya fee revision effective 7 May 2026,
    ///     gaa.go.ke/sites/default/files/2026-04/National Museums of Kenya Revision Of Heritage Facilities Admission Fees.pdf;
    ///     museums.or.ke/fort-jesus-museum; eCitizen-only ticketing: eastleighvoice.co.ke (Dec 2025).
    ///   Old Town: en.wikipedia.org/wiki/Mombasa_Old_Town; skratch.world (street food); UK FCDO Kenya safety advice.
    ///   ChaQula tour: travel4purpose.com/t4p_trips/mombasa-food-tour; linktr.ee/chaqula_.
    ///   Forodhani Restaurant: goatsontheroad.com/restaurants-in-mombasa; restaurant Facebook page (forodhanimsa).
    ///   Nalka Café: dianibeachpodcast.wordpress.com (17 May 2026).
    ///   Mombasa Tusks / "Pembeni": en.wikipedia.org/wiki/Mombasa_tusks; abirikenya.com (Mombasa's Tusks).
    ///   Gurdwara: worldgurudwaras.com/?p=3278; tripomatic.com; airial.travel (etiquette, langar).
    ///   Marikiti Market: upkenya.com; tripomatic.com (MacKinnon Market); standardmedia.co.ke.
    ///   Mama Ngina Waterfront / Lighthouse: constructionreviewonline.com; businessdailyafrica.com
    ///     ("The gem that's Mama Ngina Drive"); en.wikipedia.org/wiki/List_of_lighthouses_in_Kenya.
    ///   Bolt tuk-tuk fare: kenyans.co.ke (Aug 2026).
    ///   Mamba Village: travellingfoot.com; thrillophilia.com; takeyourbackpack.com (Nov 2026); wildwaterskenya.com blog.
    ///   Haller Park: en.wikipedia.org/wiki/Haller_Park_(Kenya); victormatara.com (Mar 2026); beyondforest.org (Apr 2025).
    ///   Wild Waters: wildwaterskenya.com/pricing-hours, /rules-regulations, /about-us.
    ///   CityBlue Creekside: citybluehotels.com; frommers.com (Best Western Plus Creekside); Kayak listings.
    ///   Marine park: kws.go.ke/mombasa-marine-national-park-reserve; KWS Conservation Fees October 2025 PDF
    ///     (Legal Notice No. 160 of 2025); travel.padi.com (glass-bottom trips from Bamburi).
    ///   Flights: kayak.com (LOS-MBA); kenya-airways.com/en_ng/flights-from-lagos-to-mombasa;
    ///     ethiopianairlines.com (Lagos/Abuja to Mombasa); SGR fares: the-star.co.ke (4 Dec 2025).
    ///   Entry: see KenyaVisaText (etakenya.go.ke: Nigerians eTA-exempt up to 60 days; FCDO and the founder:
    ///     travel health insurance for every traveller from 5 Oct 2026; visasnews.com: about US$52 in Oct 2026);
    ///     wwwnc.cdc.gov (yellow fever certificate for arrivals from risk countries).
    ///   Money: kenyans.co.ke (KSh ~129.96/US$, Nov 2026); techeconomy.ng (₦1,363/US$ official, 18 Nov 2026);
    ///     paybillke.com/guides/mpesa-for-tourists.
    ///   Weather: weatherspark.com (Mombasa year-round averages).
    /// </summary>
    public static class MombasaSeeder
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

        private const string Mombasa = "Mombasa";

        // Names checked against every other seeder. Zanzibar already has "Forodhani Gardens Night
        // Food Market" (Stone Town), so the Mombasa restaurant carries its own full name.
        private const string FortJesusName = "Fort Jesus Museum";
        private const string OldTownName = "Mombasa Old Town";
        private const string ForodhaniName = "Forodhani Restaurant, Mombasa Old Town";
        private const string NalkaName = "Nalka Café";
        private const string TusksName = "Mombasa Tusks";
        private const string GurdwaraName = "Siri Guru Singh Sabha Gurdwara";
        private const string MarikitiName = "Marikiti Market";
        private const string MamaNginaName = "Mama Ngina Waterfront & Lighthouse";
        private const string MambaName = "Mamba Village Centre";
        private const string HallerName = "Haller Park";
        private const string WildWatersName = "Wild Waters Kenya";
        private const string CreeksideName = "CityBlue Creekside Hotel & Suites";
        private const string MarineParkName = "Mombasa Marine National Park and Reserve";

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
                KenyaVisaText.Current, // shared with the other Kenya seeder; see KenyaVisaText
                "Type G", "Left", 0.16m, new[] { English, Swahili, Samburu }, "KES", "Africa/Nairobi"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Mombasa, Kenya, -4.0435, 39.6682,
                "Kenya's main port and second-largest city, on the Indian Ocean coast. The old centre is on Mombasa Island, between Tudor Creek and the Kilindini harbour: Old Town, Fort Jesus, the town centre around Moi Avenue and the Mama Ngina seafront. Nyali and Bamburi, across Nyali Bridge to the north, have the beach hotels, Haller Park, Wild Waters and the marine park. Moi International Airport is on the mainland to the west. Bamburi and Nyali are part of Mombasa, so every stop in this trip is listed under the city."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] SwahiliLangs = { English, Swahili };

        private const string Dress =
            "Dress: Old Town is mostly Muslim. Cover your shoulders and knees, and keep swimwear for the beach and the pool.";

        private const string Heat =
            "Heat: Mombasa is hot and humid all year. Carry water, sunscreen and a hat, and do your walking in the morning or late afternoon.";

        private const string Payments =
            "Paying: Prices are in Kenyan shillings (KSh). Carry small notes for snacks and tuk-tuks; M-Pesa is accepted almost everywhere, and you can register a Safaricom SIM with M-Pesa at the airport with your passport.";

        private static readonly DestSpec[] Destinations =
        {
            // ─── Mombasa Island: Old Town ──────────────────────────────────────
            new("mombasa", FortJesusName, Kenya, Mombasa, SwahiliLangs, -4.0627, 39.6795,
                10m, "Walk", 1,
                new[] { "landmark_monument", "museum_gallery", "cultural_site" },
                new[] { "history", "architecture", "educational", "photography", "tourist_hotspot" },
                new[] { "Fort Jesus", "Fort Jesus Mombasa", "Fort Jesus National Monument", "Fort Jesus, Mombasa" }, "Exterior",
                Desc(
                    "The Portuguese built Fort Jesus in 1593 to guard the entrance to Mombasa's Old Port. It is now a museum run by the National Museums of Kenya, and UNESCO listed it as a World Heritage Site in 2011. Inside the coral-stone walls you can walk the ramparts, see the old cannons and visit a museum of finds from the coast.\n\nWhat to Do: Get there at opening, walk the ramparts for the view over the harbour entrance, then walk into Old Town next door.",
                    "Fort Jesus is the first stop for most visitors and for Kenyan school trips. The ChaQula street food tour meets outside the gate in the late afternoon.",
                    "Best Access: Nkrumah Road, at the edge of Old Town on Mombasa Island. A tuk-tuk or an Uber or Bolt from Nyali takes 15-25 minutes, depending on traffic on Nyali Bridge.\n\nOn Foot: A few minutes from Old Town and Forodhani Restaurant.",
                    "Tickets: Our founder paid KSh 550 per person. That is the Kenyan and East African citizen rate on the National Museums of Kenya fee list in force since 7 May 2026. The same list charges adults in the 'residents and rest of Africa' group, which includes Nigerians, US$9 (US$6 for a child), so check your category before you pay.\n\nPaying: Tickets are cashless and sold through eCitizen (nmkpay.ecitizen.go.ke). Bring your passport to show at the gate.\n\nHours: 8:30 AM-5:30 PM every day, including public holidays.",
                    "Shade: The ramparts have very little, so go early.\n\nSteps: Stone stairs and uneven floors throughout.",
                    "Entry: KSh 550 (founder's price; the 2026 rest-of-Africa adult rate is US$9).\nFort Jesus and Butterfly House combined ticket: about US$12 for rest-of-Africa adults.\nGuide: optional; agree the price before you start.",
                    new[]
                    {
                        "Mombasa Old Town: Next door.",
                        "Forodhani Restaurant, Mombasa Old Town: About 200 m away by the Old Port.",
                        "Nalka Café: Coffee in an Old Town lane."
                    },
                    "At opening (8:30 AM), before the heat. June to October is the coolest part of the year.",
                    "Medium (5/10).",
                    "Rating: 4/10. The ramparts and upper levels are reached by steps.",
                    "1.5 to 2 hours.")),

            new("mombasa", OldTownName, Kenya, Mombasa, SwahiliLangs, -4.0589, 39.6764,
                15m, "Walk", 2,
                new[] { "neighborhood_district", "food_experience", "cultural_site" },
                new[] { "history", "architecture", "food_focused", "walkable", "cultural", "photography", "budget_friendly" },
                new[] { "Old Town Mombasa", "Mombasa Old Town Walk", "Ndia Kuu", "Old Port Mombasa", "Old Harbour Mombasa", "ChaQula food tour", "Mombasa Food Tour" }, "Street",
                Desc(
                    "Mombasa Old Town covers about 72 hectares on the south-east side of Mombasa Island, beside Tudor Creek. The lanes are narrow, with carved wooden doors, balconies, small mosques and the Old Port, where dhows and fishing boats still tie up. Ndia Kuu, which means 'main street', was the town's first main road and runs to Fort Jesus.\n\nWhat to Do: Walk it for free and eat as you go: mahamri, viazi karai, mishkaki, cassava, urojo and Swahili coffee from the street sellers. If you want someone to explain what you're eating, book ChaQula's Mombasa street food tour.",
                    "Old Town is a Swahili neighbourhood where families have lived for generations, so people are going about their day around you. Our founder goes for the food.",
                    "Best Access: Ask a tuk-tuk or an Uber or Bolt for Fort Jesus, then walk in along Ndia Kuu. From Nyali, allow 15-25 minutes by road.\n\nOn Foot: Tuk-tuks can't get down many of the lanes, so plan to walk.",
                    "Free: There's no ticket to walk the streets.\n\nFood Tour: ChaQula, a Mombasa food creator, guides a street food tour that Travel4Purpose sells as its Mombasa Food Tour. It lasts about two hours, usually 4:00-6:00 PM, starting outside Fort Jesus, with Swahili coffee, street snacks and sugarcane juice, then coconut water at Mama Ngina Waterfront. Prices start at about US$30 per adult; book on travel4purpose.com or by email.\n\n" + Dress,
                    "Crime: The UK FCDO lists Mombasa Old Town as an area where crime happens. Keep your phone and bag close, and walk with a guide or in a group after dark.\n\nPhotos: Ask before photographing people, homes or mosques.\n\n" + Heat,
                    "Walking: free.\nStreet snacks: small amounts in KSh, so carry coins and small notes.\nChaQula food tour (Travel4Purpose): from about US$30 per adult.\nCurios: bargain politely.",
                    new[]
                    {
                        "Fort Jesus Museum: At the end of Ndia Kuu.",
                        "Forodhani Restaurant, Mombasa Old Town: Seafood by the Old Port.",
                        "Nalka Café: A coffee stop near Ndia Kuu."
                    },
                    "Morning for walking; late afternoon and early evening for street food.",
                    "Medium (6/10).",
                    "Rating: 3/10. Narrow, uneven lanes with steps.",
                    "Half a day.")),

            new("mombasa", ForodhaniName, Kenya, Mombasa, SwahiliLangs, -4.0596, 39.6799,
                15m, "Walk", 1,
                new[] { "food_experience" },
                new[] { "food_focused", "local_favorite", "couple_friendly", "best_evening" },
                new[] { "Forodhani Restaurant", "Forodhani Mombasa", "Forodhani Restaurant Mombasa", "Forodhani Old Port" }, "Exterior",
                Desc(
                    "Forodhani Restaurant is on Sir Mbarak Hinawy Road by the Old Port, about 200 m from Fort Jesus, with tables looking over the harbour and the creek. The menu is Swahili food and seafood: jumbo prawns, biryani, pilau, samaki wa kupaka (fish in coconut sauce) and tamarind juice.\n\nWhat to Do: Come for lunch or dinner after Fort Jesus and Old Town, and ask for a table facing the water.",
                    "Forodhani means customs house or wharf in Swahili. In Mombasa it's this restaurant; the evening food market of the same name is in Stone Town, Zanzibar.",
                    "Best Access: Walk down Sir Mbarak Hinawy Road from Fort Jesus towards the Old Port.\n\nBy Road: Take a tuk-tuk or an Uber or Bolt to Fort Jesus, then walk for a few minutes.",
                    "Hours: Listed as opening at 9:00 AM and closing between 9:00 and 11:00 PM daily; listings differ, so call ahead on +254 724 401 551.\n\nPrices: A 2026 guide put starters at about US$2-$4 and mains at about US$9-$16.\n\nAlcohol: Reported as halal with no alcohol; check if it matters to you.",
                    "Waiting: Seafood can take 30-40 minutes or longer, so order early.\n\nGetting Back: Book your ride before you leave at night.",
                    "Starters: approx. US$2-$4.\nMains: approx. US$9-$16.\nDrinks: extra.",
                    new[]
                    {
                        "Fort Jesus Museum: About 200 m away.",
                        "Mombasa Old Town: All around it.",
                        "Mama Ngina Waterfront & Lighthouse: A short ride along the seafront."
                    },
                    "Late lunch, or dinner around sunset.",
                    "Medium (5/10).",
                    "Rating: 5/10. Street level, reached through Old Town lanes.",
                    "1 to 2 hours.")),

            // Exact address, menu, prices and hours not published; kept on the founder's word plus one 2026 write-up.
            new("mombasa", NalkaName, Kenya, Mombasa, SwahiliLangs, -4.0605, 39.6780,
                8m, "Walk", 1,
                new[] { "food_experience" },
                new[] { "food_focused", "hidden_gem", "architecture", "relaxing" },
                new[] { "Nalka Cafe", "Cafe Nalka", "Café Nalka", "Nalka Cafe Mombasa", "Nalka Old Town" }, "Interior",
                Desc(
                    "Nalka Café is a small café in a coral-stone building in Mombasa Old Town, near Ndia Kuu Road, with carved doors and Swahili spices in the food and coffee. It opened recently and has good reviews online.\n\nWhat to Do: Stop for coffee, juice or a light meal in the middle of an Old Town walk.",
                    "Our founder recommends it. It's one of the newer places in Old Town, and Mombasa food creators have been posting about it.",
                    "Best Access: In Old Town near Ndia Kuu Road. Search for Nalka Café on Google Maps and walk in from Fort Jesus.",
                    "Menu and Hours: We couldn't find a published menu, prices or opening hours. Check Google Maps or call before you go.\n\n" + Dress,
                    "Finding It: The Old Town lanes are confusing. Keep a map open or ask a shopkeeper.",
                    "Coffee and a snack: not published; budget roughly US$5-$10 per person until you've checked.",
                    new[]
                    {
                        "Fort Jesus Museum: A short walk away.",
                        "Mombasa Old Town: All around it.",
                        "Forodhani Restaurant, Mombasa Old Town: Lunch by the Old Port."
                    },
                    "Late morning, between Fort Jesus and lunch.",
                    "Low (3/10).",
                    "Rating: 5/10. The Old Town lanes are uneven.",
                    "45 minutes to 1 hour.")),

            // ─── Mombasa Island: town centre ───────────────────────────────────
            // The founder's "Pembeni" is a local nickname for the tusks (Wikipedia: "Mapembeni";
            // abirikenya.com: "Pembeni, Pembe Mbili, and Pembe za Ndovu"). No café or venue called
            // Pembeni was found in Mombasa. Confirm with the founder.
            new("mombasa", TusksName, Kenya, Mombasa, SwahiliLangs, -4.06153, 39.66611,
                0m, "Walk", 1,
                new[] { "landmark_monument", "viewpoint_scenic_spot" },
                new[] { "photography", "history", "tourist_hotspot", "good_short_visit", "budget_friendly" },
                new[] { "Pembeni", "Mapembeni", "Pembe za Ndovu", "Pembe Mbili", "Mombasa Tusks Moi Avenue", "The Tusks", "Elephant Tusks Mombasa" }, "Street",
                Desc(
                    "The Mombasa Tusks are four aluminium elephant tusks that cross over Moi Avenue in the town centre to form an 'M', opposite Uhuru Gardens. The first tusks went up in 1952 for Princess Elizabeth's visit and were rebuilt in aluminium in 1956. A 2017 refurbishment added a model elephant in the middle. Locals call them Pembe za Ndovu, Pembe Mbili or Pembeni.\n\nWhat to Do: Take the classic Mombasa photo, then walk on to Marikiti Market or the Sikh temple.",
                    "This is our founder's 'Pembeni', the stop for pictures. Visitors and plenty of Mombasa people take their photo under the tusks.",
                    "Best Access: Moi Avenue, town centre. Any tuk-tuk, Uber or Bolt driver knows 'Pembe' or 'the Tusks'. From Old Town it's about 1-1.5 km.",
                    "Free: It's open-air and you can visit any time.\n\nPhotos: Early morning and Sundays have the least traffic in the frame.",
                    "Traffic: The tusks span a busy road. Take your photos from the pavement or the central island, never from the road.\n\nPhones: Hold on to your phone in the crowds on Moi Avenue.",
                    "Free.\nTuk-tuk from Old Town: a short ride, or walk.",
                    new[]
                    {
                        "Marikiti Market: A short walk away.",
                        "Siri Guru Singh Sabha Gurdwara: About 10 minutes on foot.",
                        "Fort Jesus Museum: About 1.5 km east."
                    },
                    "Early morning, before the traffic builds.",
                    "Medium (6/10).",
                    "Rating: 7/10. Pavement level.",
                    "15 to 30 minutes.")),

            new("mombasa", GurdwaraName, Kenya, Mombasa, SwahiliLangs, -4.0564, 39.6669,
                0m, "Walk", 1,
                new[] { "religious_site", "cultural_site" },
                new[] { "cultural", "history", "educational", "budget_friendly", "hidden_gem" },
                new[] { "Sikh Temple Mombasa", "Gurdwara Singh Sabha Mombasa", "Sri Guru Singh Sabha Mombassa", "Siri Guru Singh Sabha", "Mombasa Gurdwara", "Sikh Gurdwara Mwembe Tayari" }, "Exterior",
                Desc(
                    "Siri Guru Singh Sabha is a Sikh gurdwara (temple) on Mwembe Tayari Road in Mombasa's town centre. Africa's first gurdwara opened at Kilindini in Mombasa in 1898, and this community also runs a nursery, Guru Nanak Primary School and a clinic.\n\nWhat to Do: Visit quietly, see the prayer hall, and if you're there at mealtime, eat langar, the free community meal served to anyone.",
                    "Visitors of any faith are welcome. At langar everyone sits together and eats the same simple vegetarian meal, whoever they are.",
                    "Best Access: Mwembe Tayari Road. A short tuk-tuk ride, or about 10 minutes on foot, from the Mombasa Tusks. Marikiti Market is 700-800 m away, a 10-12 minute walk.",
                    "Etiquette: Take your shoes off before you go in, cover your head in the prayer hall (bring a scarf or ask for one), and cover your shoulders and knees. No tobacco or alcohol.\n\nLangar: Served daily, usually at lunch and dinner. Times aren't published, so call ahead; the gurdwara lists a contact on +254 724 201 209.\n\nEntry: Free; donations are optional.",
                    "Photos: Ask before photographing people, and don't take photos during services.",
                    "Free.\nDonation: optional.",
                    new[]
                    {
                        "Marikiti Market: A 10-12 minute walk.",
                        "Mombasa Tusks: About 10 minutes on foot.",
                        "Mombasa Old Town: A short tuk-tuk ride east."
                    },
                    "Late morning, so you can stay for langar at lunch.",
                    "Low (3/10).",
                    "Rating: 5/10. The prayer hall is upstairs.",
                    "45 minutes to 1.5 hours.")),

            // The founder's "local market": Marikiti is the main market near the gurdwara (~700-800 m
            // apart, not adjacent), so the two are separate destinations.
            new("mombasa", MarikitiName, Kenya, Mombasa, SwahiliLangs, -4.0602, 39.6722,
                5m, "Walk", 2,
                new[] { "market_street_life" },
                new[] { "shopping", "local_favorite", "food_focused", "crowded", "budget_friendly" },
                new[] { "Marikiti", "MacKinnon Market", "Mackinnon Market Mombasa", "Marikiti Market Mombasa", "Mombasa Market" }, "Street",
                Desc(
                    "Marikiti is Mombasa's main fresh-produce market, on Digo Road in the town centre. Its 1914 market hall is also known as MacKinnon Market. Traders sell fruit, vegetables and spices inside, and khangas, household goods and street food in the surrounding streets.\n\nWhat to Do: Walk through, buy tropical fruit and spices, and look for khangas in the shops around it.",
                    "A lot of Mombasa does its food shopping here. It's a working market, busy and practical, and not set up for tourists.",
                    "Best Access: Digo Road, town centre. A short walk from the Mombasa Tusks, and 700-800 m (10-12 minutes on foot) from the Sikh gurdwara on Mwembe Tayari Road.",
                    "Hours: Not published. Mornings are busiest and the produce is freshest.\n\n" + Payments + "\n\nBargaining: Normal for non-food goods. Ask the price before you pick something up.",
                    "Pickpockets: The aisles get crowded. Keep your phone and wallet in a front pocket or a zipped bag.\n\nPhotos: Ask traders before you photograph them or their stalls.",
                    "Fruit and snacks: small amounts in KSh.\nKhangas: prices vary; bargain.",
                    new[]
                    {
                        "Siri Guru Singh Sabha Gurdwara: A 10-12 minute walk.",
                        "Mombasa Tusks: A short walk.",
                        "Mombasa Old Town: A short tuk-tuk ride east."
                    },
                    "Morning.",
                    "High (8/10).",
                    "Rating: 4/10. Crowded, uneven and often wet underfoot.",
                    "45 minutes to 1 hour.")),

            // The founder's "Lighthouse" and "Mama Ngina Waterfront" are the same seafront strip
            // (the lighthouse end is where the snack sellers gather), so they are one destination.
            new("mombasa", MamaNginaName, Kenya, Mombasa, SwahiliLangs, -4.0715, 39.6790,
                5m, "Car", 2,
                new[] { "viewpoint_scenic_spot", "market_street_life", "nature_outdoor" },
                new[] { "local_favorite", "food_focused", "best_evening", "family_friendly", "budget_friendly", "relaxing" },
                new[] { "Mama Ngina Waterfront", "Mama Ngina Waterfront Park", "Mama Ngina Drive", "Lighthouse Mombasa", "Mombasa Lighthouse", "Ras Serani Lighthouse" }, "Landscape",
                Desc(
                    "Mama Ngina Waterfront Park is a 26-acre seafront park along Mama Ngina Drive on the south side of Mombasa Island, facing the Kilindini Channel where ships come into the port. It reopened in 2019 after a rebuild, with a palm-lined promenade of about 2 km between the Likoni ferry and Mombasa Golf Club, an amphitheatre, food vendors and a jetty. Locals still call the northern end 'Lighthouse', after the Ras Serani lighthouses by the golf club.\n\nWhat to Do: Come in the late afternoon for the sea breeze, walk the promenade, watch the ships, and buy snacks from the sellers at the Lighthouse end: fried cassava, cassava crisps (mihogo) and fresh coconut (madafu).",
                    "This is where Mombasa comes to cool off after work and at weekends. Families sit by the water, and the snack sellers are part of the outing. Our founder comes for the breeze and the street food.",
                    "Best Access: Ask a tuk-tuk, Uber or Bolt for 'Lighthouse' or Mama Ngina Waterfront. A Bolt tuk-tuk from Mwembe Tayari in the town centre was quoted at about KSh 190 in 2026.\n\nOn Foot: About 1-1.5 km from Fort Jesus.",
                    "Entry: Free.\n\nHours: Published hours don't agree. It's busiest from late afternoon until after dark.\n\nSnacks: Cassava crisps, fried cassava and madafu cost small amounts, paid in cash or by M-Pesa; agree the price before you buy.\n\nChairs: Plastic chair hire was reported at about KSh 50.",
                    "After Dark: Stay in the lit, busy parts of the promenade and leave before it empties. The UK FCDO names the area around the Likoni ferry, at the southern end, as a crime hotspot.\n\nHawkers: Some sellers are persistent; 'hapana, asante' (no, thank you) works.\n\nSwimming: This is a shipping channel, not a beach. Don't swim.",
                    "Entry: free.\nCassava crisps, madafu and other snacks: small amounts in KSh.\nChair hire: about KSh 50.",
                    new[]
                    {
                        "Fort Jesus Museum: About 1-1.5 km north.",
                        "Forodhani Restaurant, Mombasa Old Town: Dinner by the Old Port.",
                        "Mombasa Old Town: A short ride away."
                    },
                    "Late afternoon to sunset.",
                    "High at weekends (7/10).",
                    "Rating: 7/10. A paved, level promenade.",
                    "1 to 2 hours.")),

            // ─── Nyali & Bamburi (north mainland, within Mombasa) ──────────────
            new("mombasa", MambaName, Kenya, Mombasa, SwahiliLangs, -4.0330, 39.7110,
                15m, "Car", 1,
                new[] { "activity_experience", "nature_outdoor" },
                new[] { "family_friendly", "nature", "educational", "tourist_hotspot" },
                new[] { "Mamba Village", "Mamba Village Mombasa", "Mamba Village Crocodile Farm", "Mamba Village Nyali", "Mombasa Crocodile Farm" }, "Exterior",
                Desc(
                    "Mamba Village is a crocodile farm and family attraction off Links Road in Nyali. You walk past crocodiles from hatchlings to large adults, and the daily feeding is usually at about 5:00 PM. There are also camel and horse rides, a children's play area, gardens and a restaurant that serves crocodile meat.\n\nWhat to Do: Time your visit for the feeding, and add a ride if you have children with you.",
                    "Our founder goes for the crocodiles. There's also a Mamba Village in Lang'ata, Nairobi, and some websites mix the two up.",
                    "Best Access: Off Links Road at the Beach Road roundabout in Nyali, a few minutes from Wild Waters. It's about 15-20 minutes from Mombasa Island by tuk-tuk, Uber or Bolt.",
                    "Entry: Our founder paid KSh 1,800. Published figures are lower (about KSh 800 for non-residents and KSh 350 for residents in several listings), so the gate price has probably gone up. Bring enough cash or M-Pesa.\n\nHours: Listed as open daily from 9:00 AM; closing times differ between listings.\n\nExtras: Holding a baby crocodile or a snake, and camel, horse and boat rides, cost extra.",
                    "Souvenir Stalls: Some sellers are pushy.\n\nGetting Back: Use a ride-hail app or a registered taxi after dark.",
                    "Entry: KSh 1,800 (founder's price, 2026).\nRides and animal photos: extra.\nRestaurant: extra.",
                    new[]
                    {
                        "Wild Waters Kenya: A few minutes along Links Road.",
                        "Haller Park: About 10 minutes north in Bamburi.",
                        "CityBlue Creekside Hotel & Suites: Back towards Nyali Bridge."
                    },
                    "Late afternoon, for the 5:00 PM feeding.",
                    "Medium (5/10).",
                    "Rating: 6/10. Mostly level paths.",
                    "1.5 to 2 hours.")),

            new("mombasa", HallerName, Kenya, Mombasa, SwahiliLangs, -4.01667, 39.71944,
                10m, "Car", 1,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "family_friendly", "educational", "photography", "walkable" },
                new[] { "Haller Park Mombasa", "Bamburi Haller Park", "Haller Park Bamburi", "Bamburi Nature Trail", "Haller Park Kenya" }, "Landscape",
                Desc(
                    "Haller Park is a nature park in Bamburi, about 10-12 km north of Mombasa Island on the Malindi road. It was an old Bamburi Cement quarry that René Haller replanted from the 1970s. Trails run through forest and past ponds with giraffe, hippos, crocodiles, giant tortoises, eland, oryx, monkeys and birds. Owen and Mzee, the hippo and tortoise pair that became famous, lived here.\n\nWhat to Do: Walk the trails and arrive in time for the afternoon feeds. Our founder buys a bag of pellets to feed the animals.",
                    "Our founder budgets KSh 1,000 for entry and KSh 100 for feeding pellets.",
                    "Best Access: On the Mombasa-Malindi Road in Bamburi, next to the Bamburi Cement works. Allow 20-30 minutes from Mombasa Island by tuk-tuk, Uber, Bolt or matatu, depending on traffic.",
                    "Entry: Our founder paid KSh 1,000, plus KSh 100 for animal-feeding pellets. Published 2025-2026 gate prices are KSh 800 for East African residents and KSh 2,200 for non-East Africans (KSh 1,000 per child), and the higher rate would apply to Nigerians. Check at the gate and bring your passport.\n\nHours: 8:00 AM-5:00 PM, with last entry around 4:30 PM.\n\nFeeding Times: Giraffe at about 3:00 PM, hippo at 4:00 PM and crocodile at 4:30 PM; some sources also list an 11:00 AM giraffe feed. Arrive by 2:30 PM.",
                    "Crocodiles: Stay behind the barriers and don't sit near the water.\n\nPlastic: Plastic bottles may not be allowed, so bring a reusable one.\n\n" + Heat,
                    "Entry: KSh 1,000 (founder's price; the published non-East African rate is KSh 2,200).\nFeeding pellets: KSh 100.",
                    new[]
                    {
                        "Mamba Village Centre: About 10 minutes south.",
                        "Wild Waters Kenya: On Links Road in Nyali.",
                        "Mombasa Marine National Park and Reserve: Boats from the Bamburi and Nyali beaches."
                    },
                    "Afternoon; arrive by 2:30 PM for the feeds.",
                    "Medium (5/10).",
                    "Rating: 5/10. Gravel and sand trails.",
                    "2 to 3 hours.")),

            new("mombasa", WildWatersName, Kenya, Mombasa, SwahiliLangs, -4.0370, 39.7040,
                15m, "Car", 1,
                new[] { "activity_experience" },
                new[] { "family_friendly", "group_friendly", "social", "tourist_hotspot" },
                new[] { "Wild Waters", "Wild Waters Mombasa", "Wild Waters Nyali", "Wildwaters Kenya", "Wild Waters Water Park" }, "Exterior",
                Desc(
                    "Wild Waters is a water park on Links Road in Nyali, with more than 15 slides for adults and children, a lazy river, a splash area for small children, a food court and a bar.\n\nWhat to Do: Spend a hot morning on the slides and the lazy river, then go on to Haller Park or Mamba Village nearby.",
                    "Mombasa families come in the Kenyan school holidays, when the park also opens on Mondays.",
                    "Best Access: Links Road, Nyali. Take a tuk-tuk, Uber or Bolt from Mombasa Island (about 15-20 minutes), or a Nyali matatu from town and ask to be dropped at Wild Waters.",
                    "Tickets: Our founder paid KSh 1,700. The park's current price list shows KSh 1,800 for residents and KSh 2,000 for non-residents using the slides, KSh 400 for non-sliders, and free entry for children under 2.\n\nHours: Tuesday-Sunday, 10:00 AM-6:00 PM. Mondays only in Kenyan school holidays and on public holidays; call 0726 337000 to check.\n\nRules: No outside food, drinks or glass. Wear swimwear, with no denim or street clothes in the water; leggings and long-sleeved tops are allowed on most slides. Children under 13 must be with an adult.\n\nLockers: KSh 200, plus a KSh 500 deposit of which KSh 300 comes back when you return the key.",
                    "Health: The park warns people with heart, back or neck problems, epilepsy or recent surgery to stay off some slides.\n\nPower Cuts: Slides can close during an outage, and tickets aren't refunded.\n\nSun: Reapply sunscreen between slides.",
                    "Entry: KSh 1,700 (founder's price; the current non-resident ticket is KSh 2,000).\nLocker: KSh 200 plus a deposit.\nFood court: extra (no outside food).",
                    new[]
                    {
                        "Mamba Village Centre: A few minutes along Links Road.",
                        "Haller Park: About 15 minutes north in Bamburi.",
                        "CityBlue Creekside Hotel & Suites: Back towards Nyali Bridge."
                    },
                    "Opening time on a weekday outside the Kenyan school holidays.",
                    "Medium (5/10); high in school holidays.",
                    "Rating: 5/10. The slides are reached by stairs.",
                    "3 to 4 hours.")),

            // The founder's "Creekside hotel": the only match found is CityBlue Creekside (formerly
            // Best Western Plus Creekside) on Tudor Creek by Nyali Bridge.
            new("mombasa", CreeksideName, Kenya, Mombasa, SwahiliLangs, -4.0480, 39.6800,
                50m, "Car", 1,
                new[] { "food_experience" },
                new[] { "relaxing", "family_friendly", "couple_friendly" },
                new[] { "CityBlue Creekside", "Creekside Hotel Mombasa", "Best Western Plus Creekside", "Best Western Creekside Mombasa", "Creekside Hotel Nyali" }, "Exterior",
                Desc(
                    "CityBlue Creekside Hotel & Suites, formerly the Best Western Plus Creekside, is a city hotel on Tudor Creek beside Nyali Bridge. Rooms look over the creek, the gardens or the pool. It has an outdoor pool, a children's pool, a gym and a restaurant serving Swahili and continental food.\n\nWhat to Do: Use it as your base. Fort Jesus is about 5 km away, and Haller Park and Mamba Village are about 8 km.",
                    "Our founder recommends it. Because it's by Nyali Bridge, the rides to Old Town and to the north-coast attractions are both short.",
                    "Best Access: Nyali Road, beside Nyali Bridge. Moi International Airport is about 15 km away by road.",
                    "Rates: Recent online listings started at about US$40-$50 a night for a room. Prices rise in the Kenyan peak months of April, August and December. Book direct on WhatsApp (+254 706 111 999) or through booking sites.\n\nOld Name: Some booking sites still list it as Best Western Plus Creekside.",
                    "Traffic: Nyali Bridge gets congested at rush hour, so leave extra time for the airport.\n\nCard Fees: Some Mombasa hotels add a 3-5% charge for card payments. Ask at check-in.",
                    "Room: from about US$40-$50 a night (online listings, late 2026).\nCard surcharge: possible.\nAirport transfer: extra.",
                    new[]
                    {
                        "Mamba Village Centre: About 8 km north.",
                        "Fort Jesus Museum: About 5 km across the bridge.",
                        "Wild Waters Kenya: On Links Road in Nyali."
                    },
                    "Year-round; book early for August and December.",
                    "Low (3/10).",
                    "Rating: 6/10. Ask the hotel about step-free rooms.",
                    "A base for 4 nights.")),

            new("mombasa", MarineParkName, Kenya, Mombasa, SwahiliLangs, -4.0350, 39.7350,
                35m, "Boat", 2,
                new[] { "nature_outdoor", "beach_coast", "activity_experience" },
                new[] { "nature", "beach", "adventurous", "photography", "family_friendly" },
                new[] { "Mombasa Marine Park", "Mombasa Marine National Park", "Mombasa Marine Reserve", "Mombasa glass-bottom boat", "Nyali reef", "Bamburi reef" }, "Landscape",
                Desc(
                    "Mombasa Marine National Park and Reserve, set up in 1986, protects the coral reef, lagoon and seagrass off the beaches of Nyali, Bamburi and Shanzu, north of the island. The fully protected park covers about 10 km², inside a reserve of about 200 km² where regulated fishing is allowed. Kenya Wildlife Service (KWS) runs it.\n\nWhat to Do: Take a glass-bottom boat out to the reef and snorkel among the corals and reef fish. At low tide you may walk across the exposed reef flat to reach the boats.",
                    "Beach operators and hotel water-sports desks along the north coast sell the boat trips, and the KWS fee is paid separately from the boat price.",
                    "Best Access: Boats leave from the north-coast beaches. KWS lists access points at the Serena, Severin, Travellers, Nyali and Voyager beach hotels, and some operators leave from Bamburi beach. From Mombasa Island it's 20-30 minutes by road to the beaches.",
                    "KWS Fee: Nigerians pay the KWS 'African citizen' rate of US$15 per adult and US$10 per child (ages 5 to under 18), under the fees in force since October 2025. Non-Africans pay US$25. Pay online through KWSPay on eCitizen; parks are cashless.\n\nBoat Trip: Glass-bottom and snorkelling trips negotiated on the beach run roughly KSh 1,000-2,500 per person, and pre-booked tours that include the park fee cost about US$50-$70. Ask what's included before you pay.\n\nHours: The beach access gates open 6:00 AM-6:00 PM.\n\nTides: Check a tide table. Trips are best around low to mid tide, when the water inside the reef is calm and clear.",
                    "Beach Sellers: Many people on the beach sell boat trips. Agree the price, how long the trip lasts and whether the KWS fee is included before you set off, and pay the park fee only through KWS.\n\nReef: Don't stand on or touch the coral, and wear reef shoes or sandals on the reef flat.\n\nSun: Wear reef-safe sunscreen and a T-shirt while snorkelling.",
                    "KWS fee: US$15 per adult (African citizen rate).\nBoat and snorkel trip: approx. KSh 1,000-2,500 per person negotiated, or US$50-$70 pre-booked.\nSnorkel gear: usually included; check.",
                    new[]
                    {
                        "Haller Park: Inland in Bamburi.",
                        "Mamba Village Centre: In Nyali.",
                        "Wild Waters Kenya: On Links Road in Nyali."
                    },
                    "Morning, timed for low to mid tide. June to October is the coolest and driest part of the year.",
                    "Medium (5/10).",
                    "Rating: 3/10. You board boats from the beach and get into the water.",
                    "2 to 3 hours.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlist
        // ═══════════════════════════════════════════════════════════════════════

        // ItineraryItem.TimeOfDay is a controlled value (Morning/Afternoon/Evening — validated by the
        // admin and user itinerary endpoints), so any specific clock times go in the item titles.
        // Costs are rough USD per person at ~KSh 130 per US$, using the founder's KSh prices.
        private static readonly WishlistSpec[] Wishlists =
        {
            new("mombasa", "Mombasa: Old Town, Fort Jesus and the North Coast", Kenya,
                "Five days in Mombasa, built from our founder's own list. You stay by Nyali Bridge, between the island and the north coast. The first evening is at Mama Ngina Waterfront for the sea breeze and street snacks. Day 2 is Fort Jesus, a walk through Old Town and dinner by the Old Port. Day 3 is a family day in Nyali and Bamburi: Wild Waters, the afternoon feeds at Haller Park and the crocodiles at Mamba Village. Day 4 is a glass-bottom boat and snorkel in the marine park, with an optional street food tour that evening. Day 5 covers the town centre, with photos at the Mombasa Tusks, Marikiti Market and the Sikh temple, before your flight.\n\nGetting there: There are no nonstop flights from Nigeria. From Lagos, Kenya Airways connects through Nairobi (about 8.5 to 10 hours in total) and Ethiopian through Addis Ababa; from Abuja, Ethiopian via Addis is the main option. Return economy fares from Lagos were roughly ₦1.0 to ₦1.2 million (about US$700-$900) in late 2026. You land at Moi International Airport. From Nairobi you can also take the Madaraka Express train to Mombasa, about six hours.\n\nEntry: Nigerians don't need a Kenyan eTA for visits of 60 days or less. Every traveller, Nigerians included, must buy Kenyan government travel health insurance online at insurance.ecitizen.go.ke before arrival; it cost about US$52 including fees when it launched in October 2026, and your own travel insurance isn't accepted in its place. Kenya asks travellers arriving from Nigeria for a yellow fever certificate, so bring your card.\n\nMoney: In mid-November 2026 a US dollar bought about KSh 130 and a shilling cost about ₦10.5. M-Pesa is used almost everywhere, and you can register a Safaricom SIM with your passport at the airport. Fort Jesus and the marine park only take cashless payment through eCitizen.\n\nWeather and dress: Mombasa is hot and humid all year. June to October is the coolest and driest stretch, and April to June has the heaviest rain. Cover your shoulders and knees in Old Town; at the Sikh temple you also cover your head and take your shoes off. All prices are rough US$ estimates, and our founder's own KSh prices are used where we have them.",
                "Fort Jesus and Old Town food, giraffes and crocodiles in Bamburi, a reef by glass-bottom boat and cassava crisps at sunset on Mama Ngina Drive: five days on the Kenyan coast.",
                "Families & Friends",
                new[] { "4 Nights at CityBlue Creekside Hotel & Suites (shared double)", "Fort Jesus Museum Entry", "Wild Waters, Haller Park and Mamba Village Tickets", "Glass-Bottom Boat & Snorkel Trip with KWS Fee", "Tuk-Tuk and Ride-Hail Budget" },
                "Mombasa, Kenya, Kenyan coast, Fort Jesus, Mombasa Old Town, Ndia Kuu, Old Port, Forodhani Restaurant, Nalka Café, ChaQula, Mombasa food tour, street food, mahamri, viazi karai, mishkaki, cassava crisps, mihogo, madafu, Mama Ngina Waterfront, Mama Ngina Drive, Lighthouse, Mombasa Tusks, Pembeni, Pembe za Ndovu, Marikiti Market, Sikh temple, Siri Guru Singh Sabha, gurdwara, langar, Haller Park, Bamburi, Mamba Village, crocodiles, Wild Waters, water park, Nyali, CityBlue Creekside, Mombasa Marine National Park, glass-bottom boat, snorkelling, KWS, M-Pesa, Moi International Airport, Nigerians, Lagos to Mombasa",
                new[] { "Culture", "Food", "Beach", "Family" },
                480m, 100m,
                "4 nights (shared double) at CityBlue Creekside Hotel & Suites on Nyali Road, by Nyali Bridge",
                "Tuk-tuks and Uber or Bolt around Mombasa, and a ride-hail car or hotel transfer to and from Moi International Airport. Flights from Lagos or Abuja to Mombasa are not included.",
                "Fort Jesus Museum, Old Town walk, Forodhani Restaurant dinner, Wild Waters, Haller Park feeds, Mamba Village crocodile feeding, marine park glass-bottom boat and snorkel, Mama Ngina Waterfront evenings, Mombasa Tusks photos, Marikiti Market, Sikh gurdwara visit; optional ChaQula street food tour",
                false, "Coastal Explorer",
                new[]
                {
                    new DaySpec(1, "Arrive in Mombasa · Sunset on Mama Ngina Drive", Mombasa, Mombasa, Mombasa, new[]
                    {
                        new ItemSpec("Morning", "Land at Moi International Airport", "Most flights from Lagos and Abuja connect through Nairobi or Addis Ababa. At the airport, buy a Safaricom SIM and ask for M-Pesa registration (bring your passport), then take a ride-hail car to the hotel, about 15 km.", 15m, "Arrival"),
                        new ItemSpec("Morning", "Or: Madaraka Express from Nairobi", "If you fly into Nairobi, the SGR train runs to Miritini in Mombasa in about 6 hours. Economy was KSh 1,500 one way in late 2025.", 12m, "Optional", null, true),
                        new ItemSpec("Afternoon", "Check In at CityBlue Creekside", "A city hotel on Tudor Creek by Nyali Bridge, with a pool. Rest through the afternoon heat.", 25m, "Your Base", CreeksideName),
                        new ItemSpec("Evening", "5:00 PM · Mama Ngina Waterfront & the Lighthouse", "Sea breeze, ships coming into Kilindini harbour, and cassava crisps (mihogo), madafu and other snacks from the sellers at the Lighthouse end. Stay in the lit, busy areas.", 3m, "Founder's Pick", MamaNginaName),
                    }),
                    new DaySpec(2, "Fort Jesus & Old Town", Mombasa, Mombasa, Mombasa, new[]
                    {
                        new ItemSpec("Morning", "8:30 AM · Fort Jesus", "Arrive at opening, before the heat. Our founder paid KSh 550; the 2026 museum rate for Nigerians and other Africans is US$9. Buy the ticket on eCitizen and bring your passport.", 4m, "History", FortJesusName),
                        new ItemSpec("Morning", "Walk Mombasa Old Town", "Down Ndia Kuu past carved doors and balconies to the Old Port. It's free. Try mahamri, viazi karai and Swahili coffee on the way, and cover your shoulders and knees.", 3m, "Street Food", OldTownName),
                        new ItemSpec("Afternoon", "Coffee at Nalka Café", "A small café in a coral-stone Old Town building near Ndia Kuu. Prices aren't published.", 7m, "Founder's Pick", NalkaName),
                        new ItemSpec("Afternoon", "Rest Through the Heat", "Back to the hotel pool for the hottest hours.", 0m, "Downtime"),
                        new ItemSpec("Evening", "7:00 PM · Dinner at Forodhani Restaurant", "Seafood and Swahili dishes by the Old Port, about 200 m from Fort Jesus. Order early, because seafood can take 30-40 minutes.", 15m, "Dinner by the Water", ForodhaniName),
                    }),
                    new DaySpec(3, "Nyali & Bamburi: Slides, Giraffes and Crocodiles", Mombasa, Mombasa, Mombasa, new[]
                    {
                        new ItemSpec("Morning", "10:00 AM · Wild Waters", "Slides and the lazy river from opening time (closed on most Mondays). Our founder paid KSh 1,700; the current non-resident ticket is KSh 2,000. No outside food.", 13m, "Family Day", WildWatersName),
                        new ItemSpec("Afternoon", "2:30 PM · Haller Park", "Arrive by 2:30 PM for the giraffe (3:00 PM), hippo (4:00 PM) and crocodile (4:30 PM) feeds. Our founder paid KSh 1,000 entry plus KSh 100 for feeding pellets; the published non-East African rate is KSh 2,200.", 9m, "Wildlife", HallerName),
                        new ItemSpec("Evening", "5:00 PM · Mamba Village Crocodile Feeding", "A short ride back down to Links Road for the 5:00 PM feeding. Our founder paid KSh 1,800; camel and horse rides cost extra. Stay for dinner at the restaurant if you like.", 14m, "Founder's Pick", MambaName),
                    }),
                    new DaySpec(4, "The Reef & a Street Food Evening", Mombasa, Mombasa, Mombasa, new[]
                    {
                        new ItemSpec("Morning", "Glass-Bottom Boat & Snorkel", "Check the tide table and go out around low to mid tide from a north-coast beach. Pay the KWS fee (US$15 for African citizens) on eCitizen, and agree the boat price and what it includes before you set off.", 30m, "On the Water", MarineParkName),
                        new ItemSpec("Afternoon", "Pool Afternoon", "Rest through the hottest part of the day.", 0m, "Downtime"),
                        new ItemSpec("Evening", "4:00 PM · ChaQula Street Food Tour", "About two hours with ChaQula, a Mombasa food creator, starting outside Fort Jesus: Swahili coffee, street snacks and sugarcane juice, ending with coconut water at Mama Ngina Waterfront. Sold by Travel4Purpose from about US$30; book ahead.", 30m, "Optional", OldTownName, true),
                        new ItemSpec("Evening", "Or: Back to Mama Ngina on Your Own", "Return to the Lighthouse end for snacks and the evening breeze.", 3m, "Optional", MamaNginaName, true),
                    }),
                    new DaySpec(5, "Town Centre & Home", Mombasa, Mombasa, Mombasa, new[]
                    {
                        new ItemSpec("Morning", "8:00 AM · Photos at the Mombasa Tusks (Pembeni)", "The tusks over Moi Avenue are the classic Mombasa photo. Go early, before the traffic, and stay on the pavement.", 0m, "Photo Spot", TusksName),
                        new ItemSpec("Morning", "Marikiti Market", "Fruit, spices and khangas at the town's main market on Digo Road. Keep your phone and wallet close.", 3m, "Local Life", MarikitiName),
                        new ItemSpec("Morning", "11:30 AM · Siri Guru Singh Sabha Gurdwara", "A 10-12 minute walk from the market. Take your shoes off, cover your head, and cover your shoulders and knees. If langar is being served you may be invited to eat; call ahead for times.", 0m, "Culture", GurdwaraName),
                        new ItemSpec("Afternoon", "Fly Home from Moi International Airport", "Leave plenty of time for traffic across the island to the airport.", 15m, "Departure"),
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
