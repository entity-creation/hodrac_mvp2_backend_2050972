using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.Infrastructure.Seeder; // ToTitleCase extension
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// Proof-of-concept content for Mauritius and Nigeria (Lagos, Abuja, Edo / Benin City).
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// All prices are rough USD approximations.
    /// </summary>
    public static class AfricaPocSeeder
    {
        private const string BlobBase = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/";

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

        private const string Mauritius = "Mauritius";
        private const string Nigeria = "Nigeria";

        private const string English = "English";
        private const string French = "French";
        private const string Creole = "Mauritian Creole";
        private const string Yoruba = "Yoruba";
        private const string Hausa = "Hausa";
        private const string Edo = "Edo (Bini)";

        private static readonly LanguageSpec[] Languages =
        {
            new(English, "en", Array.Empty<(string, string)>()),
            new(French, "fr", new[] { ("Hello", "Bonjour"), ("Thank you", "Merci") }),
            new(Creole, "mfe", new[] { ("Hello", "Bonzour"), ("Thank you", "Mersi") }),
            new(Yoruba, "yo", new[] { ("Thank you", "Ẹ ṣé"), ("How much?", "Ẹ̀lò ni?") }),
            new(Hausa, "ha", new[] { ("Hello", "Sannu"), ("Thank you", "Na gode") }),
            new(Edo, "bin", new[] { ("Greeting", "Kóyo") }),
        };

        private static readonly CurrencySpec[] Currencies =
        {
            new("MUR", "Mauritian Rupee", "Rs", 45m),
            new("NGN", "Nigerian Naira", "₦", 1500m),
        };

        private static readonly CountrySpec[] Countries =
        {
            new(Mauritius, "mauritius", "Africa", "🇲🇺",
                "Visa-free entry or visa on arrival for many nationalities; check official requirements before travel.",
                "Type C/G", "Left", 0.15m, new[] { English, French, Creole }, "MUR", "Mauritius Standard Time"),
            new(Nigeria, "nigeria", "Africa", "🇳🇬",
                "Visa required for most nationalities (e-visa / visa on arrival approval via the Nigeria Immigration Service); check official requirements before travel.",
                "Type D/G", "Right", 0.075m, new[] { English, Yoruba, Hausa, Edo }, "NGN", "W. Central Africa Standard Time"),
        };

        private static readonly CitySpec[] Cities =
        {
            new("Port Louis", Mauritius, -20.1609, 57.5012,
                "Mauritius' compact harbour capital — a mix of colonial stone buildings, a busy Victorian-era market, mosques, temples and the UNESCO-listed Aapravasi Ghat."),
            new("Grand Baie", Mauritius, -20.0130, 57.5800,
                "The north coast's lively resort town, built around a sheltered bay with restaurants, bars, boat trips and shopping."),
            new("Black River", Mauritius, -20.3600, 57.3720,
                "The wild south-west district (Rivière Noire) taking in the Black River Gorges, the Chamarel plateau and a coastline known for sunsets and dolphin trips."),
            new("Le Morne", Mauritius, -20.4560, 57.3220,
                "The south-west peninsula at the foot of Le Morne Brabant, with long lagoon beaches and a world-famous kitesurfing spot."),
            new("Trou d'Eau Douce", Mauritius, -20.2400, 57.7870,
                "A fishing village on the east coast and the main departure point for boats to Île aux Cerfs."),
            new("Lagos", Nigeria, 6.5244, 3.3792,
                "Nigeria's commercial capital and one of Africa's largest cities — a high-energy sprawl of islands and lagoons famous for its music, art, food and nightlife."),
            new("Abuja", Nigeria, 9.0765, 7.3986,
                "Nigeria's purpose-built capital in the centre of the country, laid out around Aso Rock with wide boulevards, landmark buildings and green parks."),
            new("Benin City", Nigeria, 6.3350, 5.6037,
                "Capital of Edo State and heart of the historic Kingdom of Benin, known worldwide for its royal bronze-casting tradition."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] MuLangs = { English, French, Creole };
        private static readonly string[] LagosLangs = { English, Yoruba };
        private static readonly string[] AbujaLangs = { English, Hausa };
        private static readonly string[] EdoLangs = { English, Edo };

        private static readonly DestSpec[] Destinations =
        {
            // ─────────────────────────────── MAURITIUS ───────────────────────────────
            new("mauritius", "Le Morne Brabant", Mauritius, "Le Morne", MuLangs, -20.4555, 57.3134,
                50m, "Road", 2,
                new[] { "nature_outdoor", "historical_tour", "viewpoint_scenic_spot", "activity_experience" },
                new[] { "nature", "adventurous", "history", "photography", "cultural" },
                new[] { "Le Morne", "Le Morne Mountain", "Morne Brabant" }, "Landscape",
                Desc(
                    "Le Morne Brabant is the dramatic basalt mountain (about 556 m) that rises out of the lagoon on Mauritius' south-west tip. It is a UNESCO World Heritage cultural landscape: in the 18th and early 19th centuries enslaved people who escaped (maroons) took refuge on its steep, near-inaccessible slopes, and the mountain has become a powerful symbol of resistance and freedom. Today the hike up gives some of the most spectacular views in the Indian Ocean — turquoise lagoon, the reef edge and, on clear days, the 'underwater waterfall' optical illusion off the coast.\n\nWhat to Do: Hike the trail to the cross viewpoint (and beyond with a guide), visit the International Slave Route Monument at the base, then cool off on Le Morne public beach.",
                    "For Mauritians, Le Morne is a place of memory as much as a hike — the abolition of slavery is commemorated here every 1 February. Locals hike it early on weekends, and many will tell you the mountain is best treated with respect rather than as a selfie backdrop.",
                    "Best Access: Drive along the coastal road (B9) to the Le Morne peninsula; the trailhead is signposted off the road near the base of the mountain.\n\nBy Taxi: Most visitors come by hired car or taxi from Flic en Flac, Black River or the Le Morne hotels — there is little public transport to the trailhead.\n\nThe Walk: The first section is a gentle track; the upper section becomes steep with hands-on scrambling.",
                    "Guides: Guided hikes are widely recommended and are the norm for going beyond the main viewpoint; access arrangements for the upper section have changed over the years, so confirm locally.\n\nStart Early: The trail has very little shade — sunrise or early-morning starts avoid the worst heat.\n\nUNESCO Site: Inscribed as a cultural landscape for its link to marronage, not just for its scenery.",
                    "Heat & Sun: Carry at least 1.5 L of water per person, sunscreen and a hat.\n\nSlippery Rock: After rain the upper section is slick and genuinely dangerous — turn back if conditions look poor.\n\nFootwear: Trail shoes with grip, not sandals.\n\nValuables: Don't leave belongings visible in parked cars at the trailhead.",
                    "Guided Hike: $40-$70 per person.\nTaxi/Car Hire for the Day: $40-$80.\nWater & Snacks: $3-$8.\nBeach Lunch afterwards: $10-$25.",
                    new[]
                    {
                        "Le Morne Public Beach: A long lagoon beach at the foot of the mountain, minutes from the trailhead.",
                        "International Slave Route Monument: At the base of the mountain, free to visit.",
                        "Chamarel Seven Coloured Earth: Roughly a 30-minute drive inland."
                    },
                    "Early Morning (sunrise to 9:00 AM) year-round; May to November (the cooler, drier winter) is the most comfortable hiking season.",
                    "Low to Medium (3/10) on the trail; busier on weekend mornings.",
                    "Rating: 2/10 — a steep, rocky hike with scrambling; the Slave Route Monument and the beach at the base are easily accessible.",
                    "3 to 4 hours for the guided hike, plus time on the beach afterwards.")),

            new("mauritius", "Chamarel Seven Coloured Earth", Mauritius, "Black River", MuLangs, -20.4402, 57.3735,
                18m, "Road", 1,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "nature", "photography", "family_friendly", "tourist_hotspot" },
                new[] { "Chamarel Coloured Earth", "Seven Coloured Earths", "Terres des 7 Couleurs", "Chamarel Geopark" }, "Landscape",
                Desc(
                    "On the Chamarel plateau, a patch of bare, rolling dunes shows distinct bands of red, brown, violet, blue-green and yellow sand. The colours come from volcanic rock that weathered into clay, with different minerals giving each band its hue — and the colours stay separated rather than blending. The site sits inside a small nature park that also includes a viewpoint over Chamarel Waterfall (one of the tallest single drops on the island, roughly 100 m) and an enclosure of giant tortoises.\n\nWhat to Do: Walk the fenced viewing platform around the dunes, see the waterfall viewpoint, meet the tortoises and pick up a souvenir test-tube of coloured sand.",
                    "Locals see the coloured earth as a classic 'show the visitors' stop and pair it with the drive through the cool, misty Chamarel highlands — and with a long lunch nearby. The surrounding village is known for its rum, coffee and table d'hôte style restaurants.",
                    "Best Access: Drive up from the west coast via Chamarel village; the park entrance is signposted beyond the village.\n\nFrom Le Morne / Black River: About 25-40 minutes by car on winding mountain roads.\n\nAddress for Taxi: Seven Coloured Earth Geopark, Chamarel.",
                    "Entry Fee: A single ticket covers the dunes, waterfall viewpoint and tortoises.\n\nViewing Only: The dunes are fenced off — walking on them is not allowed, to protect the formations.\n\nLight: The colours look most vivid in soft morning or late-afternoon light, and after a little rain.",
                    "Tour-Bus Peaks: Late morning gets crowded when coach tours arrive.\n\nWinding Roads: The roads up to Chamarel are narrow and twisty — take care if self-driving.\n\nExpectations: It's a small site — a lovely stop, not a half-day attraction on its own.",
                    "Entry Ticket: $10-$20 per adult.\nSand Souvenirs: $3-$10.\nLunch in Chamarel: $15-$35.\nRum Distillery Tasting nearby: $10-$25.",
                    new[]
                    {
                        "Chamarel Waterfall Viewpoint: Inside the same park, a few minutes' drive from the dunes.",
                        "Black River Gorges Viewpoint: About 15-20 minutes' drive along the plateau road.",
                        "Rum Distillery in Chamarel: Tastings and lunch a short drive away."
                    },
                    "Early Morning (opening to 10:00 AM) or mid-afternoon after the tour buses leave.",
                    "Medium (5/10), High (7/10) late morning.",
                    "Rating: 7/10 — paved paths and a viewing platform; some slopes and steps at the waterfall viewpoint.",
                    "1 to 1.5 hours including the waterfall and tortoises.")),

            new("mauritius", "Black River Gorges National Park", Mauritius, "Black River", MuLangs, -20.4166, 57.4450,
                10m, "Road", 2,
                new[] { "nature_outdoor", "viewpoint_scenic_spot", "activity_experience" },
                new[] { "nature", "adventurous", "hidden_gem", "photography", "budget_friendly" },
                new[] { "Black River Gorges", "Gorges de la Rivière Noire", "Riviere Noire National Park" }, "Landscape",
                Desc(
                    "Mauritius' largest national park protects much of what remains of the island's native forest, spread across the deep gorges and highland plateau of the south-west. It is the refuge of the endemic Mauritius kestrel, pink pigeon and echo parakeet — all brought back from the brink by conservation programmes — and it holds the island's highest point, Black River Peak (828 m). Roadside viewpoints look down the gorge to the coast, while marked trails range from short walks to full-day hikes.\n\nWhat to Do: Stop at the Black River Gorges and Alexandra Falls viewpoints, walk a section of a marked trail from the Pétrin area, and watch for endemic birds and fruit bats.",
                    "Mauritians hike here at weekends to escape the coastal heat — the plateau can be several degrees cooler and often misty. Birdwatchers treat a sighting of the kestrel or a pink pigeon as the prize of the day.",
                    "Best Access: The main plateau road between Chamarel and Grand Bassin passes the key viewpoints and the Pétrin information area.\n\nLower Entrance: There is also a visitor area on the Black River (west coast) side for walks up the valley.\n\nBy Car: Self-drive or taxi is by far the easiest way; buses don't serve the trailheads well.",
                    "Free Entry: There is no charge to enter the park.\n\nTrail Info: Information points at Pétrin and the lower visitor centre can advise on which trails are open.\n\nWeather Shifts: Rain and mist roll in quickly on the plateau — bring a light rain jacket.",
                    "Getting Lost: Some longer trails are poorly signposted — stick to well-known routes or go with a guide.\n\nMud: Trails get very muddy and slippery after rain — wear shoes with good grip.\n\nNo Facilities: There are few shops or toilets inside the park — carry water and snacks.",
                    "Park Entry: Free.\nGuided Nature Walk: $30-$60 per person.\nTaxi/Car Hire for the Day: $40-$80.\nPicnic Supplies: $5-$15.",
                    new[]
                    {
                        "Chamarel Seven Coloured Earth: About 15-20 minutes' drive.",
                        "Alexandra Falls Viewpoint: A short walk from the plateau road.",
                        "Grand Bassin (Ganga Talao): The sacred crater lake, a short drive east of the park."
                    },
                    "Morning (8:00 AM - 12:00 PM) for clearer views and active birdlife; May to November for cooler hiking.",
                    "Low (2/10) — busy only at the main roadside viewpoints.",
                    "Rating: 5/10 — roadside viewpoints are easy to reach; trails are uneven and often steep.",
                    "1 hour for the viewpoints; 3 to 6 hours for a proper hike.")),

            new("mauritius", "Île aux Cerfs", Mauritius, "Trou d'Eau Douce", MuLangs, -20.2720, 57.8040,
                60m, "Boat Only", 1,
                new[] { "beach_coast", "activity_experience" },
                new[] { "beach", "relaxing", "family_friendly", "tourist_hotspot", "photography" },
                new[] { "Ile aux Cerfs", "Ile aux Cerf", "Isle aux Cerfs", "Deer Island" }, "Exterior",
                Desc(
                    "Île aux Cerfs is a small island in the east-coast lagoon off Trou d'Eau Douce, ringed by white-sand beaches and some of the clearest, calmest water in Mauritius. Much of the island is given over to a championship golf course, while the beaches and lagoon are the main draw for day-trippers: swimming, snorkelling, parasailing and lazy lunches under the casuarina trees. Many boat trips combine it with a stop at the Grand River South East waterfall on the way.\n\nWhat to Do: Swim and snorkel in the lagoon, walk round to the quieter beaches away from the jetty, try a water sport, and have a grilled seafood lunch.",
                    "Mauritian families come at weekends with picnics and music, so weekdays are calmer. Locals know that the beaches a 10-15 minute walk from the main landing are far quieter than the strip by the jetty.",
                    "Best Access: Boats leave from Trou d'Eau Douce and nearby jetties on the east coast; the crossing takes roughly 15-20 minutes.\n\nBoat Options: Public shuttle boats, private speedboats or full-day catamaran trips.\n\nGetting to the Jetty: About 1 hour by car or taxi from Port Louis.",
                    "Boat Only: There is no road access; agree the return time with your boatman before you leave.\n\nFacilities: Restaurants, bars, toilets and water-sports kiosks are clustered near the main landing.\n\nGolf: The island's course is typically booked through the resort that operates it.",
                    "Unlicensed Boatmen: Agree the full price (including the return) up front and use reputable operators.\n\nSun Exposure: Shade is limited on the beaches — reef-safe sunscreen, hats and water are essential.\n\nWater-Sport Pressure: Vendors can be persistent; confirm prices and safety gear before you pay.\n\nWeekend Crowds: The main beach gets busy and loud on Sundays.",
                    "Shuttle/Speedboat Return: $10-$40 per person.\nCatamaran Day Trip with Lunch: $60-$120 per person.\nBeach Lunch: $20-$45.\nParasailing / Water Sports: $30-$80.",
                    new[]
                    {
                        "Grand River South East Waterfall: Often included as a boat stop on the way.",
                        "Trou d'Eau Douce Village: The departure point, with small local restaurants.",
                        "Belle Mare Beach: A long public beach about 20 minutes' drive north."
                    },
                    "Weekdays, arriving by 9:30 AM; October to April for warm water (avoid cyclone warnings, typically January-March).",
                    "High (7/10) near the jetty, Low (3/10) on the far beaches.",
                    "Rating: 4/10 — boarding boats involves steps and wading in some cases; sandy paths on the island.",
                    "Half a day to a full day (5-7 hours including transfers).")),

            new("mauritius", "Port Louis Central Market", Mauritius, "Port Louis", MuLangs, -20.1606, 57.5025,
                12m, "Walk", 2,
                new[] { "market_street_life", "food_experience" },
                new[] { "local_favorite", "budget_friendly", "walkable", "cultural", "photography" },
                new[] { "Port Louis Bazaar", "Central Market Port Louis", "Marché Central" }, "Interior",
                Desc(
                    "Port Louis Central Market — 'the bazaar' to locals — has been the capital's food hub since the Victorian era, housed in a historic iron-framed building in the heart of town. The ground floor is a riot of tropical fruit, vegetables, spices and herbal-remedy stalls, while the upper level sells textiles, baskets and souvenirs. Around the edges, street-food sellers serve the island's favourite snacks: dholl puri, gateaux piments and glasses of sweet, milky alouda.\n\nWhat to Do: Taste dholl puri and alouda, browse the spice and herb stalls, pick up vanilla or local tea, and haggle gently for souvenirs upstairs.",
                    "This is where Port Louis really shops — Mauritians of every background come here, and the mix of Indian, Creole, Chinese and French influences in the food tells the island's whole story in a few stalls. Regulars have their favourite dholl puri vendor and will happily point you to it.",
                    "Best Access: In central Port Louis, a few minutes' walk from the waterfront and the main bus stations.\n\nBy Car: Park at the Caudan Waterfront or a central car park and walk — street parking in the centre is tight.\n\nAddress for Taxi: Central Market, Farquhar Street area, Port Louis.",
                    "Opening Hours: Busy from early morning; hours are shorter on Sundays — go earlier rather than later.\n\nPrices: Food stalls have fixed prices; souvenir prices upstairs are negotiable.\n\nCash: Carry small rupee notes; many stalls don't take cards.",
                    "Pickpockets: Crowded aisles — keep bags closed and in front.\n\nSouvenir Mark-ups: Upstairs, opening prices for visitors can be high — compare a few stalls.\n\nWeekday Traffic: Central Port Louis is congested at rush hour; allow extra time.",
                    "Street Snacks (dholl puri, gateaux piments): $1-$3.\nAlouda Drink: $1-$3.\nSpices / Vanilla / Tea: $3-$15.\nSouvenirs: $5-$30.",
                    new[]
                    {
                        "Aapravasi Ghat: A 5-minute walk towards the harbour.",
                        "Caudan Waterfront: Shops and cafés on the harbour, a short walk away.",
                        "Chinatown: Just north of the market, with small eateries."
                    },
                    "Morning (8:00 AM - 11:00 AM) on weekdays or Saturday for the liveliest atmosphere.",
                    "High (7/10) on weekday mornings and Saturdays.",
                    "Rating: 5/10 — ground floor is mostly flat but crowded; upper level is reached by stairs.",
                    "45 minutes to 1.5 hours.")),

            new("mauritius", "Aapravasi Ghat", Mauritius, "Port Louis", MuLangs, -20.1586, 57.5030,
                5m, "Walk", 1,
                new[] { "historical_tour", "landmark_monument", "museum_gallery" },
                new[] { "history", "cultural", "educational", "walkable", "budget_friendly" },
                new[] { "Apravasi Ghat", "Coolie Ghat", "Aapravasi Ghat World Heritage Site" }, "Exterior",
                Desc(
                    "Aapravasi Ghat is the former immigration depot on Port Louis harbour where, from 1849 into the early 20th century, almost half a million indentured labourers — mostly from India — first set foot in Mauritius after slavery was abolished. It was the starting point of the 'great experiment' in indentured labour that spread across the British Empire, and it became a UNESCO World Heritage Site in 2006. The surviving stone steps, walls and buildings are small in scale but deeply moving, and an interpretation centre next door tells the story through personal records and exhibits.\n\nWhat to Do: Walk the preserved depot and the original landing steps, then spend time in the interpretation centre.",
                    "For a large share of Mauritians this is the place their ancestors arrived, and many families have traced records connected to it. Arrival of the indentured labourers is commemorated each year on 2 November.",
                    "Best Access: On the Port Louis waterfront, near the bus station and a short walk from the Central Market.\n\nBy Car: Use the Caudan Waterfront or central car parks and walk across.\n\nAddress for Taxi: Aapravasi Ghat, Quay Street, Port Louis.",
                    "Free Entry: The site and interpretation centre are generally free.\n\nOpening Days: Opening hours vary by day and are shorter at weekends; it may be closed on some public holidays — check before going.\n\nGuided Explanations: Staff are often available to explain the site — ask at the entrance.",
                    "Easy to Miss: The site is compact and tucked among busy roads — look for the signage.\n\nHeat: The open courtyard has limited shade at midday.\n\nTraffic: Cross the waterfront roads carefully.",
                    "Entry: Free.\nDonations / Books at the centre: $5-$20.\nTaxi in Port Louis: $5-$15.",
                    new[]
                    {
                        "Port Louis Central Market: A 5-minute walk.",
                        "Caudan Waterfront: Across the road for coffee or lunch.",
                        "Blue Penny Museum: On the Caudan Waterfront, home to rare Mauritian stamps."
                    },
                    "Weekday mornings, combined with the Central Market.",
                    "Low (2/10).",
                    "Rating: 7/10 — mostly flat; some original steps and uneven historic surfaces.",
                    "45 minutes to 1 hour.")),

            new("mauritius", "Sir Seewoosagur Ramgoolam Botanical Garden", Mauritius, "Port Louis", MuLangs, -20.1047, 57.5800,
                12m, "Road", 1,
                new[] { "nature_outdoor", "historical_tour" },
                new[] { "nature", "family_friendly", "photography", "relaxing", "educational" },
                new[] { "Pamplemousses Botanical Garden", "Pamplemousses Garden", "SSR Botanical Garden", "Jardin de Pamplemousses" }, "Landscape",
                Desc(
                    "In the village of Pamplemousses north of Port Louis, this is one of the oldest botanical gardens in the Southern Hemisphere, dating back to the 18th century under French colonial rule. It is famous for its pond of giant Victoria water lilies, an extraordinary collection of palms (including the talipot palm, which flowers only once in its lifetime), spice trees, and an enclosure of giant tortoises and deer. The historic Château de Mon Plaisir stands in the grounds, and the garden is named after the country's first prime minister.\n\nWhat to Do: See the giant water lilies, walk the palm avenues, find the spice trees and visit the tortoises.",
                    "Mauritians know it simply as 'Pamplemousses Garden' and come for shady family walks. Locals advise hiring one of the official guides at the gate — the plants are much more interesting with someone pointing out what you're looking at.",
                    "Best Access: About 11 km north of Port Louis; 20-30 minutes by car or taxi.\n\nBy Bus: Buses between Port Louis and the north stop in Pamplemousses village.\n\nAddress for Taxi: SSR Botanical Garden, Pamplemousses.",
                    "Entry Fee: Visitors pay a modest entry fee; residents' rates differ.\n\nGuides: Official guides are available at the entrance — agree a price beforehand.\n\nWater Lilies: The giant lilies are at their most impressive in the warmer months.",
                    "Unofficial Guides: Use the official guides at the ticket office rather than freelancers.\n\nMosquitoes: Bring insect repellent, especially near the ponds.\n\nMidday Heat: Many paths are shaded, but it still gets hot — carry water.",
                    "Entry Ticket: $5-$10.\nGuided Tour: $5-$15 per person.\nTaxi from Port Louis: $15-$30 return.\nDrinks & Snacks: $2-$6.",
                    new[]
                    {
                        "Port Louis Central Market: About 25 minutes' drive south.",
                        "L'Aventure du Sucre: A sugar-heritage museum a short drive away.",
                        "Grand Baie: About 20-25 minutes' drive north for dinner."
                    },
                    "Morning (8:30 AM - 11:00 AM) for cooler temperatures; November to April for the water lilies.",
                    "Medium (4/10).",
                    "Rating: 7/10 — largely flat paths, some gravel and uneven sections.",
                    "1.5 to 2 hours.")),

            // ─────────────────────────────── LAGOS ───────────────────────────────
            new("lagos", "Lekki Conservation Centre", Nigeria, "Lagos", LagosLangs, 6.4416, 3.5357,
                12m, "Road", 3,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "adventurous", "photography", "family_friendly", "educational" },
                new[] { "Lekki Conservation Center", "LCC Lekki", "Lekki Canopy Walkway" }, "Landscape",
                Desc(
                    "Run by the Nigerian Conservation Foundation, the Lekki Conservation Centre protects a patch of wetland, swamp forest and savannah right beside the Lekki–Epe Expressway — a green escape in the middle of one of Africa's busiest cities. Its headline attraction is the canopy walkway, roughly 400 m long and billed as one of the longest in Africa, which sways high above the treetops. Boardwalks wind through the forest, where you can spot mona monkeys, monitor lizards and plenty of birdlife.\n\nWhat to Do: Walk the canopy walkway, follow the nature boardwalks, look for monkeys near the main trail, and relax at the treehouse and picnic areas.",
                    "For Lagosians the centre is a weekend breather from traffic and noise — couples, families and school groups all come here. Locals say to go early on a weekday if you want the walkway (and the monkeys) to yourself.",
                    "Best Access: On the Lekki–Epe Expressway, Lekki Peninsula, roughly 20-30 minutes from Victoria Island without traffic.\n\nBy Car / Ride-hailing: The easiest option; traffic on the expressway can stretch the journey to over an hour at peak times.\n\nAddress for Ride-hailing: Lekki Conservation Centre, Km 19 Lekki–Epe Expressway, Lekki.",
                    "Two Tickets: There is a general entry fee and a separate, higher fee for the canopy walkway.\n\nWalkway Rules: Numbers on the walkway are controlled; it can close in bad weather.\n\nFeeding Wildlife: Don't feed the monkeys.",
                    "Heights: The canopy walkway is high and sways — not for anyone uncomfortable with heights.\n\nHeat & Mosquitoes: Bring water and insect repellent.\n\nTraffic: Plan around Lagos rush hours or you'll lose much of the day in the car.",
                    "General Entry: $2-$5.\nCanopy Walkway: $5-$12.\nRide-hailing from Victoria Island: $5-$15 each way.\nDrinks & Snacks: $2-$5.",
                    new[]
                    {
                        "Lekki Arts & Crafts Market: About 15-20 minutes' drive towards Lekki Phase 1.",
                        "Nike Art Gallery: About 20 minutes' drive west along the Lekki corridor.",
                        "Elegushi Beach: A popular private beach a short drive away."
                    },
                    "Weekday mornings (opening to 11:00 AM) for cooler air and active wildlife; the dry season (November-March) is most comfortable.",
                    "Medium (5/10), High (7/10) on weekends and holidays.",
                    "Rating: 4/10 — boardwalks are mostly level but the canopy walkway involves climbs and narrow, moving bridges.",
                    "2 to 3 hours.")),

            new("lagos", "Nike Art Gallery", Nigeria, "Lagos", LagosLangs, 6.4380, 3.4850,
                10m, "Road", 3,
                new[] { "museum_gallery", "activity_experience" },
                new[] { "art", "cultural", "photography", "local_favorite", "budget_friendly" },
                new[] { "Nike Gallery", "Nike Art Gallery Lekki", "Nike Centre for Art and Culture" }, "Interior",
                Desc(
                    "Founded by the celebrated artist and textile designer Chief Nike Davies-Okundaye, Nike Art Gallery is one of the largest art galleries in West Africa — a multi-storey building in Lekki packed floor to ceiling with thousands of works. Paintings, sculpture, beadwork, bronze, wood carvings and adire (indigo resist-dyed) textiles from artists across Nigeria cover every wall, from established names to emerging talent. Almost everything is for sale, but browsing is free.\n\nWhat to Do: Explore all the floors at your own pace, look for the adire and textile collections, and if you're lucky meet 'Mama Nike' herself, who often greets visitors.",
                    "Lagos artists consider the gallery an institution, and Nike Davies-Okundaye is a much-loved figure in the art world. Locals tip visitors that prices are negotiable and that the upper floors often hold the most interesting pieces.",
                    "Best Access: In the Lekki Phase 1 / Elegushi area along the Lekki corridor, about 15-20 minutes from Victoria Island without traffic.\n\nBy Car / Ride-hailing: The easiest way; enter 'Nike Art Gallery Lekki' in your app.",
                    "Free Entry: Browsing is free; staff may show you around and explain works.\n\nBuying Art: Prices are negotiable; ask about packing and export paperwork for larger pieces.\n\nPhotography: Ask before photographing specific artworks.",
                    "Hard Sell: Staff are helpful but may encourage purchases — it's fine to just browse.\n\nPacking Large Works: Factor in shipping or luggage limits before buying big canvases.\n\nTraffic: Lekki traffic can be heavy in the evening rush.",
                    "Entry: Free.\nSmall Artworks / Prints: $20-$100.\nAdire Fabric: $10-$50.\nLarger Original Paintings: $150-$1,000+.",
                    new[]
                    {
                        "Lekki Arts & Crafts Market: About 10-15 minutes' drive east.",
                        "Lekki Conservation Centre: About 20 minutes' drive east.",
                        "Victoria Island Restaurants: 15-20 minutes away for dinner."
                    },
                    "Late morning or early afternoon on weekdays.",
                    "Low to Medium (4/10).",
                    "Rating: 4/10 — several floors connected by stairs; ground floor is the easiest to access.",
                    "1 to 2 hours.")),

            new("lagos", "Tarkwa Bay Beach", Nigeria, "Lagos", LagosLangs, 6.4000, 3.3940,
                30m, "Boat Only", 3,
                new[] { "beach_coast", "activity_experience" },
                new[] { "beach", "relaxing", "adventurous", "local_favorite", "photography" },
                new[] { "Tarkwa Bay", "Takwa Bay", "Tarqua Bay" }, "Exterior",
                Desc(
                    "Tarkwa Bay is a sheltered beach near the entrance to Lagos harbour, protected by the long breakwater, which makes its water noticeably calmer than the open Atlantic beaches elsewhere in the city. It can only be reached by boat, and the short ride across the lagoon past the port and the city skyline is part of the fun. Sun loungers, umbrellas and simple food stalls make it an easy day out, and the surf break nearby attracts Lagos' small surfing community.\n\nWhat to Do: Take the boat across, rent an umbrella and loungers, swim in the calmer water, try a surf lesson, and eat grilled fish on the sand.",
                    "Tarkwa Bay is a Lagos weekend classic — groups of friends charter boats and spend the day with music and food. Locals warn that the trip is best done with a reputable boat operator and a firm return time.",
                    "Best Access: Boats leave from jetties on the Lagos waterfront (including terminals around Lekki/Victoria Island); the ride takes roughly 15-30 minutes.\n\nBoat Types: Shared public boats or chartered private speedboats.\n\nGetting to the Jetty: Ride-hailing to the jetty is easiest; allow extra time for traffic.",
                    "Boat Only: There is no road access to the beach.\n\nLife Jackets: Insist on wearing one for the crossing.\n\nFacilities: Basic — expect simple shacks for food, drinks and loungers rather than full beach clubs.",
                    "Boat Safety: Use registered operators and avoid overloaded boats; don't travel in rough weather.\n\nSwimming: Even in the bay, currents can be strong — swim where others are and don't go out alone.\n\nAgree Prices: Confirm boat, lounger and food prices before using them.\n\nValuables: Keep phones in waterproof bags.",
                    "Return Boat (shared): $5-$15 per person.\nPrivate Boat Charter: $80-$200 per boat.\nLoungers & Umbrella: $5-$15.\nGrilled Fish & Drinks: $8-$20.\nSurf Lesson: $20-$40.",
                    new[]
                    {
                        "Lighthouse Beach: Further along the breakwater, reachable by boat.",
                        "Freedom Park Lagos: On Lagos Island, a short ride from the mainland jetties.",
                        "Victoria Island Restaurants: For dinner after you return."
                    },
                    "Weekday mornings in the dry season (November-March); depart early and return by mid-afternoon.",
                    "Medium (4/10) on weekdays, High (8/10) on weekends and public holidays.",
                    "Rating: 2/10 — boarding small boats and walking on soft sand; no accessible facilities.",
                    "4 to 6 hours including boat transfers.")),

            new("lagos", "Freedom Park Lagos", Nigeria, "Lagos", LagosLangs, 6.4508, 3.3925,
                8m, "Road", 3,
                new[] { "historical_tour", "landmark_monument", "activity_experience" },
                new[] { "history", "cultural", "art", "local_favorite", "walkable" },
                new[] { "Freedom Park", "Freedom Park Broad Street", "Broad Street Prison" }, "Exterior",
                Desc(
                    "On Broad Street in the heart of Lagos Island, Freedom Park was created on the site of the colonial-era Broad Street Prison, where prisoners — including figures connected to Nigeria's independence struggle — were once held. Opened around Nigeria's 50th anniversary of independence, it is now a memorial, a leafy public park and one of the city's busiest cultural venues, with an amphitheatre, art spaces, a food court and regular concerts, theatre, film nights and book events. Parts of the old prison structure are preserved alongside modern additions.\n\nWhat to Do: Walk the memorial and history displays, check the events board for live music or theatre, and have a drink or meal in the food court.",
                    "For Lagosians it's one of the best places to catch live performance and the city's creative crowd, especially on weekend evenings. It's also a calm break from the frenetic streets of Lagos Island.",
                    "Best Access: Broad Street, Lagos Island, near Tinubu Square and the old business district.\n\nBy Car / Ride-hailing: The easiest option; Lagos Island traffic is heavy on weekdays.\n\nAddress for Ride-hailing: Freedom Park, Hospital Road / Broad Street, Lagos Island.",
                    "Entry: A small entry fee applies; ticketed events cost extra.\n\nEvents: Concerts, plays and festivals are frequent — check the park's social channels for the schedule.\n\nHistory: Look for the information boards about the former prison.",
                    "Lagos Island Traffic: Weekday 'go-slow' can be intense — travel outside rush hours.\n\nAfter Dark: The park itself is lively during events; use ride-hailing directly to and from the gate at night.\n\nCash: Some vendors prefer cash or bank transfer.",
                    "Entry: $1-$3.\nEvent Tickets: $5-$25.\nFood Court Meal: $5-$15.\nRide-hailing from Victoria Island: $5-$12.",
                    new[]
                    {
                        "Tinubu Square: A short walk in the old business district.",
                        "Cathedral Church of Christ, Marina: A historic Anglican cathedral nearby.",
                        "Tarkwa Bay Beach: Reachable by boat from the waterfront jetties."
                    },
                    "Late afternoon into the evening, especially when an event is scheduled.",
                    "Medium (5/10), High (8/10) during major events.",
                    "Rating: 7/10 — mostly level paths; some steps around the old prison structures.",
                    "1 to 3 hours depending on events.")),

            new("lagos", "Lekki Arts & Crafts Market", Nigeria, "Lagos", LagosLangs, 6.4440, 3.5240,
                25m, "Road", 3,
                new[] { "market_street_life" },
                new[] { "art", "cultural", "local_favorite", "photography", "budget_friendly" },
                new[] { "Lekki Market", "Jakande Arts and Crafts Market", "Lekki Arts and Crafts Market", "Oba Elegushi Market" }, "Exterior",
                Desc(
                    "This sprawling open-air market in the Jakande area of Lekki is Lagos' best place to shop for Nigerian and West African crafts. Lanes of small stalls overflow with wood carvings, masks, bronze and brass figures, beads, adire and aso-oke fabrics, leatherwork, paintings and drums. It's colourful, chaotic and very much a bargaining experience — the joy is in browsing slowly and chatting with traders.\n\nWhat to Do: Browse the textile and bead stalls, haggle for carvings and paintings, and pick up adire fabric to take home.",
                    "Lagosians come here for gifts and décor and treat bargaining as part of the conversation, not a confrontation. Traders often specialise, so locals advise walking the whole market before buying.",
                    "Best Access: Off the Lekki–Epe Expressway in the Jakande area, Lekki; about 25-35 minutes from Victoria Island without traffic.\n\nBy Car / Ride-hailing: The easiest way; enter 'Lekki Arts and Crafts Market' or 'Jakande Market'.",
                    "Bargaining: Expect to negotiate — opening prices for visitors are often high.\n\nPayment: Cash (naira) and bank transfers are common; cards less so.\n\nAuthenticity: Many 'antique' pieces are new reproductions — buy because you like them, not as investments.",
                    "Hard Sell: Traders can be persistent; a polite 'just looking' works.\n\nExport Rules: Genuine antiquities can't be exported without permits — be wary of anything sold as 'old'.\n\nHeat & Dust: The market is open-air; bring water and wear comfortable shoes.",
                    "Small Souvenirs (beads, small carvings): $3-$15.\nAdire / Aso-Oke Fabric: $10-$40.\nMasks & Large Carvings: $20-$150.\nRide-hailing from Victoria Island: $5-$12.",
                    new[]
                    {
                        "Lekki Conservation Centre: About 15 minutes' drive east.",
                        "Nike Art Gallery: About 10-15 minutes' drive west.",
                        "Lekki Phase 1 Restaurants: Nearby for lunch."
                    },
                    "Late morning on weekdays; Saturdays are livelier but busier.",
                    "Medium (5/10).",
                    "Rating: 3/10 — narrow, uneven lanes and steps between stalls.",
                    "1 to 2 hours.")),

            new("lagos", "Kalakuta Republic Museum", Nigeria, "Lagos", LagosLangs, 6.6080, 3.3460,
                10m, "Road", 3,
                new[] { "museum_gallery", "historical_tour" },
                new[] { "history", "cultural", "hidden_gem", "educational", "art" },
                new[] { "Kalakuta Museum", "Fela Kuti Museum", "Fela's House Ikeja" }, "Interior",
                Desc(
                    "The former home of Fela Anikulapo-Kuti — Afrobeat pioneer, activist and Nigeria's most famous musician — has been turned into a museum in Ikeja on the Lagos mainland. It takes its name from 'Kalakuta Republic', the commune Fela declared independent of the military government in the 1970s. Inside you'll find his bedroom, stage costumes, instruments, album covers and personal belongings kept much as they were, while his grave lies in the front courtyard. The rooftop has a bar and small restaurant with views over Ikeja.\n\nWhat to Do: Tour the rooms and memorabilia, pay respects at Fela's grave, and finish with a drink on the rooftop.",
                    "Fela remains a towering figure in Lagos, and fans from around the world come here on pilgrimage. Locals often pair the museum with a night at the New Afrika Shrine nearby, the music venue run by Fela's family.",
                    "Best Access: Gbemisola Street, Ikeja, on the Lagos mainland — around 45-90 minutes from Victoria Island depending heavily on traffic.\n\nBy Car / Ride-hailing: The easiest way; it's in a residential street, so search 'Kalakuta Republic Museum'.",
                    "Entry Fee: A modest entry fee; guided explanations are usually available.\n\nOpening Times: Hours can vary — check before making the trip across the city.\n\nThe Shrine: Live shows at the New Afrika Shrine run on certain nights, typically late into the evening.",
                    "Cross-City Traffic: The drive from the islands to Ikeja can be very slow — leave plenty of time.\n\nNight Travel: Use ride-hailing door to door after dark.\n\nPhotography: Ask staff where photography is allowed.",
                    "Museum Entry: $2-$5.\nRooftop Drinks / Food: $5-$15.\nNew Afrika Shrine entry (if open): $2-$10.\nRide-hailing from Victoria Island: $10-$20 each way.",
                    new[]
                    {
                        "New Afrika Shrine: A short drive away in Ikeja for live Afrobeat.",
                        "Ikeja City Mall: Nearby for food and shopping.",
                        "Murtala Muhammed International Airport: About 20-30 minutes away — a good stop before an evening flight."
                    },
                    "Late afternoon, so you can stay for rooftop drinks or head to the Shrine afterwards.",
                    "Low (3/10).",
                    "Rating: 3/10 — a converted house with stairs to upper floors and the rooftop.",
                    "1 to 1.5 hours (longer with the rooftop).")),

            // ─────────────────────────────── ABUJA ───────────────────────────────
            new("abuja", "Millennium Park", Nigeria, "Abuja", AbujaLangs, 9.0700, 7.5030,
                3m, "Road", 2,
                new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "nature", "family_friendly", "relaxing", "budget_friendly", "photography", "local_favorite" },
                new[] { "Millennium Park Abuja", "Abuja Millennium Park", "Millenium Park" }, "Landscape",
                Desc(
                    "Millennium Park in the Maitama district is Abuja's largest public park, designed by Italian architect Manfredi Nicoletti and opened in the early 2000s. A river-fed stream runs through landscaped lawns, fountains and themed gardens, with Aso Rock rising dramatically in the background. It's the capital's favourite place for weekend picnics, photo shoots, jogging and simply escaping the heat under the trees.\n\nWhat to Do: Stroll the main water axis and gardens, have a picnic on the lawns, and take photos with Aso Rock as the backdrop.",
                    "Abuja residents treat the park as their shared back garden — families, couples, wedding and graduation shoots fill it at weekends. Locals say the late-afternoon 'golden hour' is when it's at its best.",
                    "Best Access: Maitama district, close to the Three Arms Zone; about 10-15 minutes by car from the Central Business District.\n\nBy Taxi / Ride-hailing: The easiest option; there's parking at the main gate.\n\nAddress for Ride-hailing: Millennium Park, Maitama, Abuja.",
                    "Entry: Entry is free or very low cost; there may be small fees for parking or some activities.\n\nSecurity: Being near government areas, expect security presence — follow any instructions.\n\nVendors: Drinks and snacks are sold near the entrance.",
                    "Sun: Bring water and a hat in the dry season.\n\nWeekend Crowds: Sundays are busy with events and photo shoots.\n\nPhotography of Government Areas: Don't point cameras at security installations beyond the park.",
                    "Entry: Free-$2.\nSnacks & Drinks: $2-$6.\nRide-hailing from Central Area: $3-$8.",
                    new[]
                    {
                        "Aso Rock: Visible from the park; the restricted area lies beyond.",
                        "Nigerian National Mosque: About 10 minutes' drive.",
                        "Maitama Restaurants: For lunch or dinner nearby."
                    },
                    "Late afternoon (4:00 PM - sunset) for cooler air and soft light; the dry season (November-March) is most pleasant.",
                    "Medium (4/10), High (7/10) on weekends.",
                    "Rating: 8/10 — wide paved paths across mostly level ground.",
                    "1 to 2 hours.")),

            new("abuja", "Nigerian National Mosque", Nigeria, "Abuja", AbujaLangs, 9.0605, 7.4893,
                2m, "Road", 2,
                new[] { "religious_site", "landmark_monument" },
                new[] { "cultural", "photography", "history", "tourist_hotspot", "budget_friendly" },
                new[] { "Abuja National Mosque", "National Mosque Abuja", "Abuja Central Mosque" }, "Exterior",
                Desc(
                    "Completed in 1984, the Nigerian National Mosque is one of the capital's defining landmarks — a vast complex topped by a gleaming golden dome and four tall minarets, set on Independence Avenue in the Central District. It serves as the main mosque for Abuja's Muslim community and also houses a conference centre and library. Its scale and symmetry, especially when lit up in the evening, make it one of the most photographed buildings in Nigeria.\n\nWhat to Do: Admire and photograph the exterior and grounds, and — where permitted — join a short visit outside prayer times.",
                    "For Abuja residents the mosque and the National Christian Centre nearby are symbols of the capital's aim to bring Nigeria's faiths together in one city. Friday prayers draw very large congregations.",
                    "Best Access: Independence Avenue, Central District, Abuja.\n\nBy Taxi / Ride-hailing: Easiest; there is parking in the grounds.\n\nAddress for Ride-hailing: Abuja National Mosque, Independence Avenue.",
                    "Visitor Access: Non-Muslim visitors are sometimes allowed in outside prayer times; ask respectfully at the gate and follow staff guidance.\n\nDress Code: Modest dress — long sleeves and trousers/long skirts; women should cover their hair.\n\nShoes: Remove shoes before entering prayer areas.",
                    "Prayer Times: Avoid visiting during prayers, especially Friday midday.\n\nPhotography: Ask before photographing people or the interior.\n\nUnofficial 'Guides': Only accept guidance from mosque staff.",
                    "Entry: Free.\nDonations: Optional.\nRide-hailing from Wuse/Maitama: $2-$6.",
                    new[]
                    {
                        "National Christian Centre: A short drive away on the same central axis.",
                        "Millennium Park: About 10 minutes' drive.",
                        "Eagle Square: Abuja's ceremonial square nearby."
                    },
                    "Mid-morning on weekdays, outside prayer times; evening for photographs of the illuminated dome.",
                    "Low (3/10) outside prayer times; very high during Friday prayers.",
                    "Rating: 6/10 — large level grounds; some steps at the building entrances.",
                    "30 minutes to 1 hour.")),

            new("abuja", "National Christian Centre", Nigeria, "Abuja", AbujaLangs, 9.0560, 7.4930,
                2m, "Road", 2,
                new[] { "religious_site", "landmark_monument" },
                new[] { "cultural", "photography", "history", "budget_friendly" },
                new[] { "National Ecumenical Centre", "Nigerian National Christian Centre", "Ecumenical Centre Abuja" }, "Exterior",
                Desc(
                    "The National Christian Centre (also known as the National Ecumenical Centre) is the striking interdenominational church in Abuja's Central District, completed in the mid-2000s. Its soaring Gothic-inspired spires and modern stained-glass façade make it an architectural counterpart to the National Mosque nearby, and it hosts national Christian services and events. It's a short, rewarding stop for anyone interested in Abuja's architecture and civic symbolism.\n\nWhat to Do: View and photograph the exterior, and if a visit is possible, see the large main sanctuary.",
                    "Locals point out that the mosque and the church face each other across the capital's central area — a deliberate statement of national unity in a country with large Muslim and Christian populations.",
                    "Best Access: Central District, Abuja, near Independence Avenue and a short drive from the National Mosque.\n\nBy Taxi / Ride-hailing: Easiest; ask for 'National Christian Centre' or 'Ecumenical Centre'.",
                    "Visitor Access: Interior access depends on services and events — ask at the gate.\n\nDress Code: Respectful, modest clothing.\n\nServices: Sunday and special services can be large and formal.",
                    "Security Checks: Expect gate security; carry ID.\n\nPhotography: Ask before photographing services or people.\n\nMidday Sun: Little shade around the forecourt.",
                    "Entry: Free.\nDonations: Optional.\nRide-hailing within Central Area: $2-$5.",
                    new[]
                    {
                        "Nigerian National Mosque: A few minutes' drive away.",
                        "Eagle Square: Nearby ceremonial square.",
                        "Millennium Park: About 10-15 minutes' drive."
                    },
                    "Weekday mornings; late afternoon for warm light on the façade.",
                    "Low (2/10) outside major services.",
                    "Rating: 6/10 — level forecourt; steps at some entrances.",
                    "30 to 45 minutes.")),

            new("abuja", "Jabi Lake", Nigeria, "Abuja", AbujaLangs, 9.0700, 7.4250,
                15m, "Road", 2,
                new[] { "nature_outdoor", "activity_experience", "food_experience" },
                new[] { "relaxing", "family_friendly", "local_favorite", "photography", "walkable" },
                new[] { "Jabi Lake Abuja", "Jabi Lake Park", "Jabi Lake Mall" }, "Exterior",
                Desc(
                    "Jabi Lake is a large artificial lake in Abuja's Jabi district and one of the capital's most popular leisure spots. Along its shore you'll find a lakeside promenade, open-air bars and grills, horse rides and paddle-boat or motorboat trips, while Jabi Lake Mall sits right on the waterfront with restaurants and cafés overlooking the water. It's at its best around sunset, when the city comes out to relax.\n\nWhat to Do: Take a boat ride on the lake, walk the shoreline, and have dinner or drinks at a lakeside spot or in the mall.",
                    "Abuja residents come here after work and at weekends to unwind — it's one of the few places in the capital where you can sit by the water. Lakeside suya and grilled fish are local favourites.",
                    "Best Access: Jabi district, about 15 minutes by car from the Central Business District.\n\nBy Taxi / Ride-hailing: Ask for 'Jabi Lake Mall' for the easiest drop-off and parking.",
                    "Boat Rides: Short boat trips are offered from the shore — agree price and duration first.\n\nThe Mall: Jabi Lake Mall has supermarkets, cinemas and restaurants if you want air-conditioning.\n\nLife Jackets: Ask for one on any boat trip.",
                    "Swimming: Don't swim in the lake.\n\nAgree Prices: Confirm boat and horse-ride prices before you start.\n\nEvening Travel: Use ride-hailing back to your hotel after dark.",
                    "Boat Ride: $3-$10 per person.\nLakeside Grill / Suya Dinner: $5-$15.\nRestaurant Meal in the Mall: $10-$25.\nRide-hailing from Central Area: $3-$8.",
                    new[]
                    {
                        "Jabi Lake Mall: On the lakeshore.",
                        "Wuse 2 Restaurants & Bars: About 10-15 minutes' drive.",
                        "Millennium Park: About 20 minutes' drive."
                    },
                    "Late afternoon to sunset (4:30 PM - 7:00 PM).",
                    "Medium (5/10), High (7/10) on weekend evenings.",
                    "Rating: 7/10 — the mall and promenade are accessible; boats involve stepping down from the shore.",
                    "1.5 to 3 hours.")),

            new("abuja", "Zuma Rock", Nigeria, "Abuja", AbujaLangs, 9.1300, 7.2280,
                15m, "Road", 3,
                new[] { "viewpoint_scenic_spot", "landmark_monument", "nature_outdoor" },
                new[] { "nature", "photography", "history", "adventurous" },
                new[] { "Zuma Rock Madalla", "Zuma", "Gateway to Abuja" }, "Landscape",
                Desc(
                    "Zuma Rock is a colossal granite monolith rising sharply from the plains beside the Abuja–Kaduna Expressway near Madalla, just over the border in Niger State. Its sheer face, which some say resembles a human face, has made it one of Nigeria's most recognisable natural landmarks — it appears on the 100 naira note and is often called the 'Gateway to Abuja'. For most visitors it's a roadside landmark and photo stop rather than a climb.\n\nWhat to Do: Stop at a safe roadside viewpoint for photos, learn about its significance to the local Gbagyi people, and see how the rock's face changes with the light.",
                    "For people living around Madalla and Suleja, Zuma Rock has long been a place of cultural and spiritual significance to the Gbagyi people. Abuja residents mostly know it as the landmark that tells you you're nearly home on the drive from the north.",
                    "Best Access: Drive north-west from Abuja on the Abuja–Kaduna Expressway towards Madalla/Suleja; roughly 45 minutes to 1 hour from central Abuja depending on traffic.\n\nBy Car: Go with a driver who knows a safe place to pull over — don't stop on the expressway shoulder itself.",
                    "Viewing, Not Climbing: Climbing isn't a standard visitor activity — treat it as a viewpoint stop.\n\nLocal Customs: The rock has traditional significance; be respectful.\n\nPhotography: Morning or late-afternoon light shows the 'face' best.",
                    "Highway Safety: The expressway is busy with fast-moving trucks — only stop in safe, designated spots.\n\nTravel Advisories: Check current local security advice for the Abuja–Kaduna corridor before travelling and travel by daylight.\n\nTouts: Decline anyone demanding 'fees' to take photos from the roadside.",
                    "Driver for the Round Trip: $25-$50.\nFuel (if self-driving): $5-$15.\nDrinks & Snacks: $2-$5.",
                    new[]
                    {
                        "Suleja Town: Just beyond the rock, known for its pottery tradition nearby.",
                        "Jabi Lake: About an hour back towards Abuja for lunch.",
                        "Millennium Park: Abuja's main park, back in the city."
                    },
                    "Morning (8:00 AM - 11:00 AM) for good light and safer daytime travel.",
                    "Low (1/10) — a roadside viewpoint.",
                    "Rating: 6/10 — viewed from the roadside; uneven ground at pull-offs.",
                    "2 to 2.5 hours round trip including the drive; 20-30 minutes at the rock.")),

            new("abuja", "Aso Rock", Nigeria, "Abuja", AbujaLangs, 9.0857, 7.5303,
                0m, "Road", 3,
                new[] { "landmark_monument", "viewpoint_scenic_spot" },
                new[] { "history", "photography", "nature", "budget_friendly" },
                new[] { "Aso Rock Abuja", "Aso Villa", "Aso Hill" }, "Landscape",
                Desc(
                    "Aso Rock is the massive granite outcrop, around 400 m tall, that dominates Abuja's skyline and gave the capital its layout — the city was planned around it. At its base lie the Presidential Villa and the Three Arms Zone (the Presidency, National Assembly and Supreme Court), so 'Aso Rock' is also shorthand in Nigeria for the seat of government. The rock itself is within a restricted government area and cannot be visited or climbed; it is enjoyed from outside, with good views from places such as Millennium Park and the surrounding avenues.\n\nWhat to Do: View and photograph the rock from public places outside the restricted zone, especially at sunset when the granite glows.",
                    "In everyday Nigerian conversation, 'Aso Rock' means the presidency itself — 'Aso Rock says…' is how people talk about government announcements. Locals enjoy the rock as the backdrop to the city rather than as a destination.",
                    "Best Access: Visible across much of Abuja; good public viewpoints include Millennium Park and areas around Maitama and the Central District.\n\nBy Car: There's no visitor entrance — don't attempt to approach the restricted Three Arms Zone or the Presidential Villa.",
                    "Restricted Area: The rock and the Presidential Villa are in a high-security government zone; there is no public access.\n\nBest View: From Millennium Park or other public spaces at a respectful distance.\n\nNational Symbol: The rock is closely associated with Nigeria's government and history.",
                    "Photography Near Security Sites: Photographing government buildings, checkpoints or security personnel can lead to questioning or confiscation — keep cameras on the scenery and follow instructions.\n\nRoad Closures: Roads around the Three Arms Zone may close without notice.\n\nCarry ID: Expect checkpoints in the area.",
                    "Viewing: Free.\nRide-hailing to a Viewpoint: $3-$8.",
                    new[]
                    {
                        "Millennium Park: One of the best public spots to view the rock.",
                        "Nigerian National Mosque: About 10 minutes' drive.",
                        "National Christian Centre: About 10 minutes' drive."
                    },
                    "Late afternoon to sunset, viewed from Millennium Park.",
                    "Low (1/10) — a view rather than a visit.",
                    "Rating: 8/10 as a view from accessible public parks; no access to the rock itself.",
                    "15 to 30 minutes, combined with Millennium Park.")),

            // ─────────────────────────────── EDO (BENIN CITY) ───────────────────────────────
            new("edo", "Benin National Museum", Nigeria, "Benin City", EdoLangs, 6.3381, 5.6258,
                4m, "Road", 3,
                new[] { "museum_gallery", "historical_tour" },
                new[] { "history", "cultural", "educational", "art", "budget_friendly" },
                new[] { "Benin City Museum", "National Museum Benin City", "National Museum Benin" }, "Exterior",
                Desc(
                    "The Benin National Museum stands at King's Square (Ring Road), the roundabout at the very centre of Benin City. Its galleries introduce the history and art of the ancient Kingdom of Benin, with bronze and brass castings, terracotta, ivory-style carvings, iron objects and royal regalia. Much of the kingdom's greatest art was taken during the British punitive expedition of 1897 and is still held abroad, though some pieces have been returned in recent years — which makes the museum both a showcase and a reminder of that history.\n\nWhat to Do: Tour the galleries, look for the bronze plaques and commemorative heads, and ask staff to explain the royal symbolism.",
                    "For Benin people, the bronzes are not just art but historical records of the kingdom, and their return is a subject of real pride and debate. Locals recommend seeing the museum first so the rest of the city — the palace, Igun Street, the moat — makes more sense.",
                    "Best Access: At King's Square (Ring Road), central Benin City — every major road in the city leads to it.\n\nBy Taxi / Ride-hailing: Ask for 'National Museum, Ring Road'.\n\nOn Foot: Igun Street and the palace area are within a short walk or ride.",
                    "Entry Fee: A small entry fee; an additional fee may apply for cameras.\n\nGuided Explanations: Staff guides are often available and add a lot of context.\n\nOpening Hours: Check ahead — hours and gallery availability can vary.",
                    "Traffic Circle: King's Square is a busy roundabout — cross carefully and use designated crossings.\n\nPhotography: Ask before photographing exhibits.\n\nExpectations: Collections are smaller than you might expect given Benin's fame, because so many works remain abroad.",
                    "Entry: $1-$5.\nCamera Fee (if charged): $1-$5.\nGuide Tip: $2-$5.",
                    new[]
                    {
                        "King's Square & Emotan Statue: Right outside.",
                        "Igun Street Bronze Casters: A short walk or ride away.",
                        "Royal Palace of the Oba of Benin: Close by, viewed from outside."
                    },
                    "Weekday mornings (9:00 AM - 12:00 PM).",
                    "Low (2/10).",
                    "Rating: 5/10 — some steps at the entrance and between galleries.",
                    "1 to 1.5 hours.")),

            new("edo", "Igun Street Bronze Casters", Nigeria, "Benin City", EdoLangs, 6.3330, 5.6260,
                20m, "Walk", 3,
                new[] { "activity_experience", "market_street_life", "historical_tour" },
                new[] { "art", "cultural", "history", "hidden_gem", "educational" },
                new[] { "Igun Street", "Igun Eronmwon", "Benin Bronze Casters Street" }, "Interior",
                Desc(
                    "Igun Street is home to the guild of royal bronze casters, a centuries-old guild whose members have served the Oba's court for generations. Along the street, family workshops still make bronze and brass pieces using the traditional lost-wax casting method — sculpting in wax, encasing it in clay, then pouring molten metal. Visitors can watch artisans at work and buy pieces directly from the makers, from small figurines to large commemorative heads.\n\nWhat to Do: Watch a casting demonstration, talk to the casters about the techniques and symbols, and buy a piece directly from the workshop.",
                    "Casters on Igun Street see themselves as custodians of a royal tradition, not just craftspeople, and many trace their family's role back many generations. A respectful interest in the process opens a lot of doors.",
                    "Best Access: Just off King's Square area in central Benin City; walkable from the National Museum.\n\nBy Taxi / Ride-hailing: Ask for 'Igun Street'.",
                    "Watching Work: Casting isn't happening every hour — asking politely usually gets you a demonstration of at least part of the process.\n\nBuying: Prices are negotiable; buying directly supports the makers.\n\nExport: New castings are fine to buy, but get a receipt; antiques require permits to export.",
                    "Hot Metal: Keep a safe distance from furnaces and pouring.\n\nPhotography: Ask before photographing artisans or workshops; a small tip is appreciated.\n\nPrice Starting Points: Visitor prices start high — negotiate respectfully.",
                    "Small Bronze Pieces: $15-$60.\nMedium Sculptures: $60-$250.\nLarge Commemorative Heads: $250-$1,000+.\nTip for a Demonstration: $2-$10.",
                    new[]
                    {
                        "Benin National Museum: A short walk away at King's Square.",
                        "Royal Palace of the Oba of Benin: Nearby.",
                        "King's Square & Emotan Statue: A few minutes' walk."
                    },
                    "Weekday mornings, when workshops are most active.",
                    "Low (2/10).",
                    "Rating: 3/10 — uneven street surfaces and small workshops with steps.",
                    "1 to 1.5 hours.")),

            new("edo", "Royal Palace of the Oba of Benin", Nigeria, "Benin City", EdoLangs, 6.3345, 5.6233,
                5m, "Road", 3,
                new[] { "landmark_monument", "historical_tour" },
                new[] { "history", "cultural", "photography", "educational" },
                new[] { "Oba's Palace Benin", "Benin Royal Palace", "Oba of Benin Palace", "Ogbe Palace" }, "Exterior",
                Desc(
                    "The palace of the Oba of Benin is the seat of one of Africa's oldest surviving monarchies and the spiritual heart of the Benin Kingdom. The palace complex was sacked and burned during the British punitive expedition of 1897 and later rebuilt; today it remains a working royal residence and the centre of court ceremonies, including the annual Igue festival. Visitors usually see it from outside — interior visits and audiences are possible only by prior arrangement through the palace's protocol office.\n\nWhat to Do: View the palace frontage and walls, learn about royal court traditions, and if you've arranged a visit in advance, follow palace protocol carefully.",
                    "The Oba is deeply respected by Benin people, and the palace is treated with great reverence. Locals advise visitors to dress modestly, behave quietly nearby and never assume you can simply walk in.",
                    "Best Access: Close to King's Square in central Benin City.\n\nBy Taxi / Ride-hailing: Ask for 'Oba's Palace'.\n\nArranged Visits: Contact the palace's protocol office well ahead (often through a local guide or hotel).",
                    "Exterior / By Arrangement: Most visitors only see the exterior; interior access requires prior permission.\n\nFestivals: The Igue festival (typically in December) is the most significant royal ceremonial period.\n\nProtocol: Follow any instructions from palace officials exactly.",
                    "Photography: Do not photograph the palace interior, ceremonies or officials without explicit permission.\n\nUnofficial Fixers: Be wary of people offering paid 'access' — go through the proper protocol channels.\n\nDress: Modest clothing is expected.",
                    "Viewing from Outside: Free.\nLocal Guide: $10-$25.\nCustomary Gifts (for an arranged visit): Ask your guide; varies.",
                    new[]
                    {
                        "Benin National Museum: A short ride away at King's Square.",
                        "Igun Street Bronze Casters: Nearby.",
                        "Benin Moat (Iya): Sections can be seen around the old city."
                    },
                    "Weekday mornings; December around the Igue festival for ceremonies (access limited).",
                    "Low (2/10), much higher during festivals.",
                    "Rating: 5/10 for viewing from outside; interior access depends on arrangements.",
                    "30 minutes from outside; 1-2 hours for an arranged visit.")),

            new("edo", "Benin Moat (Iya)", Nigeria, "Benin City", EdoLangs, 6.3270, 5.6150,
                15m, "Road", 4,
                new[] { "historical_tour", "landmark_monument" },
                new[] { "history", "hidden_gem", "educational", "adventurous" },
                new[] { "Benin Moat", "Iya", "Benin City Walls", "Benin Earthworks" }, "Landscape",
                Desc(
                    "The Iya are the vast system of moats and earthen walls dug around Benin City and the surrounding countryside over several centuries, before European contact. Built entirely by hand, they are often cited as among the largest earthworks ever made in the pre-mechanical era. Much of the system has since been eroded, overgrown or built over, but deep sections of the inner moat can still be seen in parts of the city, giving a sense of the scale and organisation of the old Benin Kingdom.\n\nWhat to Do: Visit a preserved section with a local guide, see the depth of the ditch and banks, and hear how the moat defined the old city.",
                    "Many Benin residents grew up near sections of the moat without realising their global significance; local historians and heritage advocates are working to protect what remains from dumping and construction.",
                    "Best Access: Sections lie in several parts of the city — go with a local guide or knowledgeable driver who knows the safest, best-preserved viewing points.\n\nBy Car: Most viewing points are reached by road and a short walk.",
                    "Guide Recommended: Sections aren't well signposted; a guide makes the visit meaningful and safer.\n\nHeritage Site: Parts are recognised as national heritage — don't climb down the banks.\n\nScale: You'll see sections, not the whole system.",
                    "Steep Edges: The moat's banks are steep and can be unstable — keep well back from the edge.\n\nRemote Sections: Some viewpoints are in quiet areas — visit in daylight with a guide.\n\nWaste: Some sections are affected by dumping; be prepared for that.",
                    "Local Guide: $10-$25.\nTaxi / Driver: $5-$20.\nWater: $1-$2.",
                    new[]
                    {
                        "Benin National Museum: For context on the old kingdom.",
                        "King's Square & Emotan Statue: The heart of the city.",
                        "Royal Palace of the Oba of Benin: Within the old city."
                    },
                    "Morning or late afternoon in the dry season (November-March), with a guide.",
                    "Very Low (1/10).",
                    "Rating: 2/10 — uneven ground and steep edges; viewing from roadside points only for limited mobility.",
                    "1 to 1.5 hours with a guide.")),

            new("edo", "Okomu National Park", Nigeria, "Benin City", EdoLangs, 6.3500, 5.2500,
                60m, "Road", 3,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "adventurous", "hidden_gem", "photography", "educational" },
                new[] { "Okomu Forest", "Okomu Wildlife Sanctuary", "Okomu Park" }, "Landscape",
                Desc(
                    "Okomu National Park protects one of the last remaining blocks of lowland rainforest in south-western Nigeria, west of Benin City. It's an important refuge for the white-throated monkey (a primate found only in Nigeria), as well as other monkeys, forest elephants (present but rarely seen), many bird species and butterflies. Guided forest walks, a tall observation platform with views over the canopy and the chance of hearing monkeys calling make it a rewarding — if rustic — day trip.\n\nWhat to Do: Take a ranger-guided forest walk, climb the observation platform, look for monkeys and birds, and learn about conservation efforts in the park.",
                    "Conservationists and park rangers consider Okomu one of the most important forest reserves in the region; local communities around it are part of ongoing conservation work. Edo residents often mention it as the state's natural treasure.",
                    "Best Access: Drive west from Benin City; the trip typically takes around 2 hours each way, with the last stretch on rougher rural roads.\n\nBy Car: Hire a car and driver from Benin City — public transport isn't practical.\n\nArrange Ahead: Contact the park in advance to arrange a ranger guide.",
                    "Ranger Guides: Walks are done with park rangers; book ahead if possible.\n\nWildlife Viewing: Sightings are never guaranteed — early morning is best.\n\nFacilities: Basic; bring your own food and water for the day.",
                    "Long Drive: Leave early and return before dark; road conditions vary, especially in the rainy season.\n\nInsects: Bring strong repellent, long sleeves and closed shoes.\n\nTravel Advisories: Check current local security advice and travel with a reputable driver.",
                    "Park Entry & Ranger Fee: $10-$30 per person.\nCar & Driver for the Day: $60-$120.\nPacked Lunch & Water: $5-$15.",
                    new[]
                    {
                        "Benin City: The base for the trip, about 2 hours away.",
                        "Benin National Museum: Back in the city for an afternoon visit on another day.",
                        "King's Square & Emotan Statue: In central Benin City."
                    },
                    "Dry season (November-March); start at dawn for the best wildlife activity.",
                    "Very Low (1/10).",
                    "Rating: 2/10 — unpaved forest trails and a high observation platform with stairs.",
                    "Full day (8-10 hours including travel).")),

            new("edo", "King's Square (Ring Road)", Nigeria, "Benin City", EdoLangs, 6.3383, 5.6256,
                3m, "Walk", 4,
                new[] { "landmark_monument", "neighborhood_district", "market_street_life" },
                new[] { "history", "cultural", "walkable", "photography", "budget_friendly" },
                new[] { "Kings Square Benin", "Ring Road Benin", "Oba Ovonramwen Square", "Emotan Statue" }, "Exterior",
                Desc(
                    "King's Square, the great roundabout known locally as Ring Road, is the hub of Benin City — the city's main roads radiate out from it like spokes, echoing the plan of the old royal capital. The National Museum stands at its centre, and nearby is the statue of Emotan, a 15th-century market woman honoured for helping Oba Ewuare during a time of crisis; she remains a revered figure in Benin history. Around the square, the busy markets and streets give a vivid snapshot of everyday city life.\n\nWhat to Do: See the Emotan statue and hear her story, take in the layout of the radiating roads, and browse the nearby market streets.",
                    "Emotan is remembered with deep affection in Benin, and her statue is a meeting point and symbol of loyalty and kindness. Locals see Ring Road as the place where all of the city converges.",
                    "Best Access: The centre of Benin City — every taxi driver knows 'Ring Road'.\n\nOn Foot: Walk the square's edge to the Emotan statue; use crossings carefully.",
                    "Emotan Statue: Located close to the market area by the square.\n\nCity Layout: The radiating roads are a modern reflection of the historic city plan.\n\nMarkets: Nearby markets are busiest in the morning and evening.",
                    "Heavy Traffic: Ring Road is a busy, fast-moving roundabout — take great care crossing.\n\nPickpockets: Keep valuables secure in crowded market areas.\n\nTouts: Politely decline unsolicited offers of 'guiding' or exchange.",
                    "Viewing: Free.\nStreet Snacks: $1-$3.\nLocal Guide: $5-$15.",
                    new[]
                    {
                        "Benin National Museum: At the centre of the square.",
                        "Igun Street Bronze Casters: A short walk away.",
                        "Royal Palace of the Oba of Benin: Nearby."
                    },
                    "Morning (8:00 AM - 11:00 AM) before the heat and heaviest traffic.",
                    "High (7/10) — busy traffic and markets.",
                    "Rating: 4/10 — busy roads and uneven pavements.",
                    "30 to 45 minutes.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlists
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly WishlistSpec[] Wishlists =
        {
            new("mauritius", "Mauritius Island Escape", Mauritius,
                "Four days across Mauritius: hike UNESCO-listed Le Morne Brabant and explore the wild south-west — Chamarel's coloured earth and the Black River Gorges — then sail to Île aux Cerfs on the east coast and finish in Port Louis with the bustling market, the Aapravasi Ghat and the historic Pamplemousses botanical garden.",
                "Sunrise on a mountain of freedom, a lagoon the colour of glass, and a market where the whole island's story is told in a dholl puri.",
                "Couples / Nature Lovers",
                new[] { "Guided Le Morne Hike", "Île aux Cerfs Boat Transfer", "Park & Garden Entry Fees", "Private Driver Days", "3 Nights Mid-Range Accommodation" },
                "Mauritius, Le Morne Brabant, Chamarel, Seven Coloured Earth, Black River Gorges, Île aux Cerfs, Ile aux Cerfs, Port Louis, Central Market, Aapravasi Ghat, Pamplemousses, botanical garden, beach, hiking, UNESCO, island",
                new[] { "Nature", "Beach", "Culture", "Relaxing" },
                900m, 150m,
                "3 nights in a mid-range hotel or guesthouse (shared double), south-west coast",
                "Private driver for the south-west and Port Louis days; return transfer to Trou d'Eau Douce and shuttle boat to Île aux Cerfs",
                "Guided Le Morne Brabant hike, Seven Coloured Earth entry, Black River Gorges viewpoints, Île aux Cerfs boat, Pamplemousses garden entry with guide",
                true, "Nature-Loving Couple",
                new[]
                {
                    new DaySpec(1, "Le Morne Sunrise & South-West Coast", "Le Morne", "Le Morne", "Black River", new[]
                    {
                        new ItemSpec("Morning", "Guided Hike up Le Morne Brabant", "An early start to beat the heat on the UNESCO-listed mountain, with sweeping lagoon views from the top viewpoint.", 55m, "UNESCO Site", "Le Morne Brabant"),
                        new ItemSpec("Morning", "Slave Route Monument", "A quiet stop at the memorial at the foot of the mountain to understand Le Morne's meaning.", 0m, "Must-See"),
                        new ItemSpec("Afternoon", "Beach Lunch at Le Morne", "Grilled fish or a Creole curry by the lagoon after the hike.", 20m, "Local Favorite"),
                        new ItemSpec("Afternoon", "Le Morne Public Beach", "Swim, rest and watch the kitesurfers on one of the island's most beautiful beaches.", 0m, "Scenic Spot"),
                        new ItemSpec("Evening", "Sunset Dinner in Black River", "A seafood dinner on the west coast, famous for its sunsets.", 35m, "Sunset Views"),
                    }),
                    new DaySpec(2, "Chamarel Highlands & Black River Gorges", "Black River", "Black River", "Black River", new[]
                    {
                        new ItemSpec("Morning", "Chamarel Seven Coloured Earth", "See the striped dunes, the Chamarel waterfall viewpoint and the giant tortoises before the tour buses arrive.", 18m, "Iconic Spot", "Chamarel Seven Coloured Earth"),
                        new ItemSpec("Morning", "Coffee Stop in Chamarel Village", "A short break in the cool highland village.", 5m, "Local Favorite"),
                        new ItemSpec("Afternoon", "Lunch in Chamarel", "A long table d'hôte style lunch of Mauritian dishes in the highlands.", 25m, "Foodie Pick"),
                        new ItemSpec("Afternoon", "Black River Gorges Viewpoints & Nature Walk", "Gaze down the gorge, then walk a marked trail looking for endemic birds.", 0m, "Hidden Gem", "Black River Gorges National Park"),
                        new ItemSpec("Evening", "Dinner on the West Coast", "A relaxed dinner back near your accommodation.", 30m, "Relaxed Evening"),
                    }),
                    new DaySpec(3, "Island Day: Île aux Cerfs", "Trou d'Eau Douce", "Trou d'Eau Douce", "Black River", new[]
                    {
                        new ItemSpec("Morning", "Transfer to Trou d'Eau Douce", "A scenic cross-island drive to the east coast jetty.", 40m, "Private Transfer"),
                        new ItemSpec("Morning", "Boat to Île aux Cerfs", "Cross the turquoise lagoon to the island and claim a quiet beach away from the jetty.", 25m, "Traveler Favorite", "Île aux Cerfs"),
                        new ItemSpec("Afternoon", "Seafood Lunch on the Island", "Grilled fish and cold drinks under the casuarina trees.", 35m, "Beach Lunch"),
                        new ItemSpec("Afternoon", "Snorkelling or Optional Water Sports", "Snorkel in the calm lagoon, or try parasailing if you want an extra thrill.", 40m, "Optional Thrill", null, true),
                        new ItemSpec("Evening", "Return Drive & Casual Dinner", "Head back west for a casual evening.", 25m, "Wind Down"),
                    }),
                    new DaySpec(4, "Port Louis & Pamplemousses", "Port Louis", "Port Louis", "Grand Baie", new[]
                    {
                        new ItemSpec("Morning", "Port Louis Central Market", "Taste dholl puri and alouda and browse the spice and fruit stalls.", 10m, "Local Favorite", "Port Louis Central Market"),
                        new ItemSpec("Morning", "Aapravasi Ghat", "Walk the UNESCO-listed immigration depot and its interpretation centre.", 0m, "UNESCO Site", "Aapravasi Ghat"),
                        new ItemSpec("Afternoon", "Lunch at the Caudan Waterfront", "A harbourside lunch in the capital.", 20m, "Harbour Views"),
                        new ItemSpec("Afternoon", "Pamplemousses Botanical Garden", "Giant water lilies, towering palms and the garden's tortoises, with an official guide.", 15m, "Historic Garden", "Sir Seewoosagur Ramgoolam Botanical Garden"),
                        new ItemSpec("Evening", "Farewell Dinner in Grand Baie", "End the trip in the north coast's liveliest resort town.", 40m, "Farewell Dinner"),
                    }),
                }),

            new("lagos", "Lagos Culture & Coast Weekend", Nigeria,
                "Two high-energy days in Lagos: walk the treetop canopy at Lekki Conservation Centre, lose yourself in thousands of artworks at Nike Art Gallery and the Lekki Arts & Crafts Market, then take a boat to Tarkwa Bay, soak up history and live culture at Freedom Park, and end at Fela's home, the Kalakuta Republic Museum.",
                "Treetops before breakfast, adire and Afrobeat by night — Lagos at full volume, with a boat ride to the beach in between.",
                "Culture Seekers / Friends",
                new[] { "Canopy Walkway Ticket", "Tarkwa Bay Boat Transfer", "Museum & Park Entry", "Ride-hailing Budget", "2 Nights Hotel on Victoria Island or Lekki" },
                "Lagos, Nigeria, Lekki Conservation Centre, canopy walkway, Nike Art Gallery, Lekki Arts and Crafts Market, Tarkwa Bay, beach, Freedom Park, Kalakuta Republic Museum, Fela Kuti, Afrobeat, art, culture",
                new[] { "Culture", "Art", "Music", "Beach" },
                420m, 80m,
                "2 nights in a mid-range hotel on Victoria Island or Lekki (shared double)",
                "Ride-hailing between stops; return boat transfer to Tarkwa Bay with a registered operator",
                "Lekki Conservation Centre entry and canopy walk, Nike Art Gallery visit, Tarkwa Bay loungers, Freedom Park entry, Kalakuta Republic Museum entry",
                true, "Culture-Curious Traveler",
                new[]
                {
                    new DaySpec(1, "Canopies, Crafts & Galleries", "Lagos", "Lagos", "Lagos", new[]
                    {
                        new ItemSpec("Morning", "Lekki Conservation Centre Canopy Walk", "Beat the heat and the crowds on the long treetop walkway, then look for monkeys on the boardwalks.", 12m, "Must-Do", "Lekki Conservation Centre"),
                        new ItemSpec("Morning", "Lekki Arts & Crafts Market", "Browse carvings, beads and adire, and practise your bargaining.", 25m, "Local Favorite", "Lekki Arts & Crafts Market"),
                        new ItemSpec("Afternoon", "Jollof & Suya Lunch", "A classic Nigerian lunch in Lekki Phase 1.", 12m, "Foodie Pick"),
                        new ItemSpec("Afternoon", "Nike Art Gallery", "Floor after floor of Nigerian art, with textiles and sculpture from across the country.", 0m, "Art Lover's Pick", "Nike Art Gallery"),
                        new ItemSpec("Evening", "Dinner on Victoria Island", "Modern Nigerian cuisine in one of the city's lively dining districts.", 30m, "Nightlife Hub"),
                    }),
                    new DaySpec(2, "Boat, Beach, Freedom & Fela", "Lagos", "Lagos", "Lagos", new[]
                    {
                        new ItemSpec("Morning", "Boat to Tarkwa Bay Beach", "Cross the harbour by boat for a morning on Lagos' calmest beach.", 30m, "Boat Ride", "Tarkwa Bay Beach"),
                        new ItemSpec("Morning", "Grilled Fish on the Beach", "Fresh fish and cold drinks on the sand.", 12m, "Beach Lunch"),
                        new ItemSpec("Afternoon", "Freedom Park Lagos", "History at the former colonial prison, plus whatever's on the stage that day.", 5m, "Cultural Hub", "Freedom Park Lagos"),
                        new ItemSpec("Afternoon", "Ride to Ikeja", "Cross to the mainland — leave time for Lagos traffic.", 15m, "Transfer"),
                        new ItemSpec("Evening", "Kalakuta Republic Museum & Rooftop", "See Fela's home and belongings, then end with drinks on the rooftop.", 15m, "Music Legend", "Kalakuta Republic Museum"),
                        new ItemSpec("Evening", "Optional Night at the New Afrika Shrine", "If there's a show on, catch live Afrobeat at the famous venue nearby.", 10m, "Optional Night Out", null, true),
                    }),
                }),

            new("abuja", "Abuja Capital Highlights", Nigeria,
                "Two days in Nigeria's purpose-built capital: the golden dome of the National Mosque and the spires of the National Christian Centre, sunset in Millennium Park with Aso Rock as the backdrop, a morning drive-by of Zuma Rock — the 'Gateway to Abuja' — and lakeside evenings at Jabi Lake.",
                "Golden domes, granite giants and a lakeside sunset — Abuja's landmarks in one easy weekend.",
                "First-Time Visitors / Business Travelers",
                new[] { "Driver for Zuma Rock Trip", "Jabi Lake Boat Ride", "Ride-hailing Budget", "2 Nights Hotel in Central Abuja" },
                "Abuja, Nigeria, National Mosque, National Christian Centre, Millennium Park, Aso Rock, Zuma Rock, Jabi Lake, capital city, landmarks, architecture",
                new[] { "Landmarks", "Culture", "Relaxing", "Photography" },
                380m, 70m,
                "2 nights in a mid-range hotel in central Abuja (shared double)",
                "Ride-hailing within the city; private driver for the Zuma Rock morning",
                "Landmark visits, Millennium Park, Zuma Rock viewpoint stop, Jabi Lake boat ride",
                false, "Weekend Explorer",
                new[]
                {
                    new DaySpec(1, "Landmarks of the Capital", "Abuja", "Abuja", "Abuja", new[]
                    {
                        new ItemSpec("Morning", "Nigerian National Mosque", "Admire the golden dome and minarets; dress modestly and ask about visiting outside prayer times.", 0m, "Iconic Landmark", "Nigerian National Mosque"),
                        new ItemSpec("Morning", "National Christian Centre", "The soaring spires of the interdenominational centre nearby.", 0m, "Architecture", "National Christian Centre"),
                        new ItemSpec("Afternoon", "Lunch in Wuse 2", "Nigerian and international options in Abuja's restaurant district.", 15m, "Foodie Pick"),
                        new ItemSpec("Afternoon", "Millennium Park", "Stroll the capital's largest park through the late afternoon.", 0m, "Local Favorite", "Millennium Park"),
                        new ItemSpec("Evening", "Aso Rock at Sunset (View from Outside)", "Watch the granite glow from the park — the rock itself sits in a restricted government zone.", 0m, "Sunset Views", "Aso Rock"),
                        new ItemSpec("Evening", "Suya Dinner", "Spicy grilled suya, an Abuja evening staple.", 10m, "Street Food"),
                    }),
                    new DaySpec(2, "Zuma Rock & Jabi Lake", "Abuja", "Abuja", "Abuja", new[]
                    {
                        new ItemSpec("Morning", "Drive to Zuma Rock", "Head out on the Abuja–Kaduna road in daylight with a driver who knows the route.", 35m, "Private Driver"),
                        new ItemSpec("Morning", "Zuma Rock Viewpoint Stop", "A drive-by and photo stop at the monolith near Madalla — the face on the 100 naira note.", 0m, "Gateway to Abuja", "Zuma Rock"),
                        new ItemSpec("Afternoon", "Lunch back in Abuja", "Refuel after the drive back to the city.", 15m, "Local Favorite"),
                        new ItemSpec("Afternoon", "Jabi Lake Boat Ride", "A relaxed boat trip and stroll along the lakeshore.", 8m, "Traveler Favorite", "Jabi Lake"),
                        new ItemSpec("Evening", "Lakeside Dinner at Jabi", "Grilled fish or dinner overlooking the water at sunset.", 20m, "Sunset Views"),
                    }),
                }),

            new("edo", "Benin City Royal Heritage", Nigeria,
                "Two days in the heart of the ancient Kingdom of Benin: King's Square and the Emotan statue, the Benin National Museum, the centuries-old bronze casters of Igun Street, the Oba's palace (viewed from outside) and the great Benin Moat — then a long day trip into the rainforest of Okomu National Park.",
                "Bronze, royalty and a thousand years of history — then a day deep in one of Nigeria's last great rainforests.",
                "History Buffs / Culture Travelers",
                new[] { "Local Heritage Guide", "Museum Entry", "Okomu Park Entry & Ranger", "Car & Driver for Okomu", "2 Nights Hotel in Benin City" },
                "Benin City, Edo State, Nigeria, Benin Kingdom, Oba of Benin, Benin bronzes, Igun Street, bronze casting, Benin National Museum, King's Square, Ring Road, Emotan, Benin Moat, Iya, Okomu National Park, rainforest",
                new[] { "History", "Culture", "Art", "Nature" },
                340m, 60m,
                "2 nights in a mid-range hotel in Benin City (shared double)",
                "Taxi / ride-hailing within the city; car and driver for the Okomu day trip (around 2 hours each way)",
                "Local heritage guide, Benin National Museum entry, Igun Street visit, Benin Moat guided stop, Okomu ranger-guided walk",
                false, "History Enthusiast",
                new[]
                {
                    new DaySpec(1, "The Royal City", "Benin City", "Benin City", "Benin City", new[]
                    {
                        new ItemSpec("Morning", "King's Square & the Emotan Statue", "Start at the city's heart and hear the story of Emotan.", 0m, "City Heart", "King's Square (Ring Road)"),
                        new ItemSpec("Morning", "Benin National Museum", "An introduction to the art and history of the Benin Kingdom.", 4m, "Must-See", "Benin National Museum"),
                        new ItemSpec("Afternoon", "Igun Street Bronze Casters", "Watch the lost-wax tradition in action and buy straight from the makers.", 20m, "Living Tradition", "Igun Street Bronze Casters"),
                        new ItemSpec("Afternoon", "Royal Palace of the Oba (Exterior)", "See the palace from outside — interior visits only by prior arrangement.", 0m, "Royal Heritage", "Royal Palace of the Oba of Benin"),
                        new ItemSpec("Afternoon", "Benin Moat (Iya) with a Guide", "Stand at the edge of one of the great earthworks of the pre-mechanical world.", 15m, "Hidden Gem", "Benin Moat (Iya)"),
                        new ItemSpec("Evening", "Edo Dinner", "Try black soup and pounded yam at a local restaurant.", 12m, "Local Flavor"),
                    }),
                    new DaySpec(2, "Okomu Rainforest Day Trip", "Benin City", "Benin City", "Benin City", new[]
                    {
                        new ItemSpec("Morning", "Early Drive to Okomu", "Leave at dawn for the roughly 2-hour drive west through Edo's countryside.", 45m, "Private Driver"),
                        new ItemSpec("Morning", "Ranger-Guided Forest Walk", "Look for white-throated monkeys and forest birds with a park ranger.", 20m, "Wildlife Spotting", "Okomu National Park"),
                        new ItemSpec("Afternoon", "Picnic Lunch in the Park", "A packed lunch in the forest — facilities are basic.", 8m, "Off the Grid"),
                        new ItemSpec("Afternoon", "Canopy Observation Platform", "Climb up for views across the rainforest canopy.", 0m, "Scenic Spot"),
                        new ItemSpec("Evening", "Return to Benin City & Dinner", "Drive back before dark and unwind over dinner.", 15m, "Wind Down"),
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

            // ─── Batch 3: destinations + hero images ────────────────────────────
            var destByName = new Dictionary<string, Destination>();
            foreach (var spec in Destinations)
            {
                var countryId = countryByName[spec.CountryName].CountryId;
                var dest = await db.Destinations
                    .FirstOrDefaultAsync(d => d.DestinationName == spec.Name && d.CountryId == countryId, ct);
                if (dest == null)
                {
                    var clean = Normalize(spec.Name);
                    var country = Countries.First(c => c.Name == spec.CountryName);
                    dest = new Destination
                    {
                        DestinationId = Guid.NewGuid(),
                        DestinationName = spec.Name,
                        CleanNormalizedSearchName = clean,
                        MetaphoneCode = Phonetic(clean, primary: false),
                        DoubleMetaphonePrimary = Phonetic(clean, primary: true),
                        DoubleMetaphoneSecondary = "",
                        CommonAlternateSpellingsJson = JsonSerializer.Serialize(spec.Alternates, JsonOpts),
                        DescriptionJson = JsonSerializer.Serialize(spec.Desc, JsonOpts),
                        AverageCostPerDay = spec.AvgCost,
                        LuxuryRating = DeriveLuxury(spec.AvgCost, spec.Tags),
                        AccessibilityType = spec.AccessibilityType,
                        FamilyFriendlyScore = FamilyScore(spec.Tags),
                        AdventurePaceScore = AdventureScore(spec.Tags),
                        AestheticTrendScore = AestheticTrendScore(spec.Tags),
                        PsychographicVibeTagsJson = JsonSerializer.Serialize(spec.Tags, JsonOpts),
                        Latitude = spec.Lat,
                        Longitude = spec.Lng,
                        SearchHitCount = 0,
                        TimeZone = country.TimeZone,
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
                var dest = destByName[spec.Name];
                var id = dest.DestinationId;

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

            // ─── Batch 5+: one batch per wishlist ───────────────────────────────
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

                // Wishlist ↔ Destination links (add only if missing), also for pre-existing wishlists.
                var wishlistId = wishlist.WishlistId;
                var linked = (await db.WishlistDestinations
                        .Where(wd => wd.WishlistId == wishlistId)
                        .Select(wd => wd.DestinationId)
                        .ToListAsync(ct))
                    .ToHashSet();
                var usedDestIds = spec.Days.SelectMany(d => d.Items)
                    .Where(i => i.DestinationName != null)
                    .Select(i => destByName[i.DestinationName!].DestinationId)
                    .Distinct();
                foreach (var destId in usedDestIds)
                {
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
            $"{spec.Region}_{Normalize(spec.Name).Replace(' ', '-')}.jpg";

        private static string DestinationImageUrl(DestSpec spec) => BlobBase + DestinationImageFile(spec);

        /// <summary>Lowercase, accents stripped, apostrophes removed, '&amp;' → "and", other punctuation → space.</summary>
        private static string Normalize(string input)
        {
            var decomposed = input.Replace("&", " and ").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (ch is '\'' or '’') continue;
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            }
            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Simplified metaphone-style consonant skeleton (uppercase, one token per word, ≤ 20 chars).
        /// primary=true approximates Double Metaphone primary (B → P), matching the Seattle seeder's style.
        /// </summary>
        private static string Phonetic(string clean, bool primary)
        {
            var stopWords = new HashSet<string> { "and", "of", "the" };
            var tokens = new List<string>();
            foreach (var word in clean.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (stopWords.Contains(word)) continue;
                var w = word.ToUpperInvariant();
                var sb = new StringBuilder();
                void Emit(string s)
                {
                    foreach (var c in s)
                        if (sb.Length == 0 || sb[^1] != c) sb.Append(c);
                }
                for (var i = 0; i < w.Length; i++)
                {
                    var c = w[i];
                    var next = i + 1 < w.Length ? w[i + 1] : '\0';
                    if (!char.IsLetter(c)) continue;
                    switch (c)
                    {
                        case 'A' or 'E' or 'I' or 'O' or 'U':
                            if (i == 0) Emit(c.ToString());
                            break;
                        case 'Y' or 'W' or 'H':
                            if (i == 0) Emit(c.ToString());
                            break;
                        case 'C':
                            if (next == 'H') { Emit("X"); i++; }
                            else Emit(next is 'E' or 'I' or 'Y' ? "S" : "K");
                            break;
                        case 'S':
                            if (next == 'H') { Emit("X"); i++; } else Emit("S");
                            break;
                        case 'P':
                            if (next == 'H') { Emit("F"); i++; } else Emit("P");
                            break;
                        case 'T':
                            if (next == 'H') i++;
                            Emit("T");
                            break;
                        case 'G':
                            if (next == 'H') i++;
                            Emit("K");
                            break;
                        case 'Q': Emit("K"); break;
                        case 'Z': Emit("S"); break;
                        case 'X': Emit("KS"); break;
                        case 'D': Emit("T"); break;
                        case 'V': Emit("F"); break;
                        case 'B': Emit(primary ? "P" : "B"); break;
                        default: Emit(c.ToString()); break;
                    }
                }
                if (sb.Length > 0) tokens.Add(sb.ToString());
            }

            // Keep whole tokens while within the 20-char column limit.
            var result = new StringBuilder();
            foreach (var t in tokens)
            {
                var candidateLength = result.Length == 0 ? t.Length : result.Length + 1 + t.Length;
                if (candidateLength > 20) break;
                if (result.Length > 0) result.Append(' ');
                result.Append(t);
            }
            if (result.Length == 0 && tokens.Count > 0)
                result.Append(tokens[0].Length > 20 ? tokens[0][..20] : tokens[0]);
            return result.ToString();
        }

        // Same scoring rules as the Seattle seeder.
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
