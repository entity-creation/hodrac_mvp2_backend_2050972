using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Seeder;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    public class LosAngelesSeeder
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ADD THIS METHOD TO THE EXISTING `DataSeeder` STATIC CLASS
        // (alongside SeedJapanTrip / SeedSeattleMatchDay). Reuses the existing
        // AssignCategoriesAndTagsToDestinationsAsync helper. Includes its own
        // local image/language/currency helpers, same pattern as the Seattle file.
        //
        // DESIGN NOTE: These are built as TWO separate Wishlist rows —
        // "Perfect SoFi Stadium Match Day" (Inglewood-first) and
        // "Downtown LA Before Kickoff" (DTLA-first) — because they're genuinely
        // alternate strategies for the same match day, not sequential days.
        // They share one destination (SoFi Stadium) so a traveler comparing the
        // two sees the same stadium info either way.
        // ═══════════════════════════════════════════════════════════════════════

        public static async Task SeedLosAngelesMatchDayWishlists(HodracDbContext db)
        {
            // ─── Lookups ──────────────────────────────────────────────────────────
            var usa = await db.Countries.FirstAsync(c => c.CountryName == "United States of America");
            var english = await db.Languages.FirstAsync(l => l.LanguageName == "English");
            var usd = await db.Currencies.FirstAsync(c => c.CurrencyCode == "USD");

            var inglewood = await db.Cities.FirstOrDefaultAsync(c => c.CityName == "Inglewood");
            if (inglewood == null)
            {
                inglewood = new City
                {
                    CityId = Guid.NewGuid(),
                    CityName = "Inglewood",
                    CountryId = usa.CountryId,
                    Latitude = 33.9617,
                    Longitude = -118.3531,
                    CityDescription = "Home of SoFi Stadium and the Hollywood Park entertainment district, with a historic downtown core along Market Street."
                };
                db.Cities.Add(inglewood);
            }

            var losAngeles = await db.Cities.FirstOrDefaultAsync(c => c.CityName == "Los Angeles");
            if (losAngeles == null)
            {
                losAngeles = new City
                {
                    CityId = Guid.NewGuid(),
                    CityName = "Los Angeles",
                    CountryId = usa.CountryId,
                    Latitude = 34.0522,
                    Longitude = -118.2437,
                    CityDescription = "Sprawling Southern California metropolis, home to Downtown LA's historic core, Koreatown's round-the-clock food and nightlife scene, and dozens of distinct neighborhoods."
                };
                db.Cities.Add(losAngeles);
            }
            await db.SaveChangesAsync();

            var mapping = new Dictionary<string, (string[] categories, string[] tags)>
            {
                // ── Inglewood / SoFi Stadium day ──
                ["Pann's Restaurant"] = (
                    categories: new[] { "food_experience" },
                    tags: new[] { "local_favorite", "budget_friendly", "historic", "retro_vibe" }
                ),
                ["Hilltop Coffee + Kitchen"] = (
                    categories: new[] { "food_experience" },
                    tags: new[] { "food_focused", "local_favorite", "walkable" }
                ),
                ["Market Street (Inglewood)"] = (
                    categories: new[] { "neighborhood_district", "market_street_life" },
                    tags: new[] { "walkable", "local_favorite", "food_focused", "shopping" }
                ),
                ["Dulan's Soul Food Kitchen"] = (
                    categories: new[] { "food_experience" },
                    tags: new[] { "food_focused", "local_favorite", "cultural" }
                ),
                ["Jamz Creamery"] = (
                    categories: new[] { "food_experience" },
                    tags: new[] { "food_focused", "family_friendly", "local_favorite" }
                ),
                ["SoFi Stadium"] = (
                    categories: new[] { "activity_experience", "landmark_monument" },
                    tags: new[] { "sports_fan", "premium", "crowded", "tourist_hotspot", "architecture", "photography" }
                ),
                ["Tom's Watch Bar (Inglewood)"] = (
                    categories: new[] { "entertainment_nightlife", "food_experience" },
                    tags: new[] { "sports_fan", "social", "nightlife", "premium" }
                ),
                ["Koreatown"] = (
                    categories: new[] { "neighborhood_district", "food_experience", "entertainment_nightlife" },
                    tags: new[] { "food_focused", "nightlife", "social", "cultural", "local_favorite" }
                ),

                // ── Downtown LA day ──
                ["Grand Central Market"] = (
                    categories: new[] { "market_street_life", "food_experience" },
                    tags: new[] { "food_focused", "walkable", "tourist_hotspot", "local_favorite", "budget_friendly" }
                ),
                ["Angels Flight Railway"] = (
                    categories: new[] { "landmark_monument", "activity_experience" },
                    tags: new[] { "quirky", "photography", "historic", "walkable", "budget_friendly" }
                ),
                ["Gloria Molina Grand Park"] = (
                    categories: new[] { "nature_outdoor", "viewpoint_scenic_spot" },
                    tags: new[] { "relaxing", "family_friendly", "walkable", "photography" }
                ),
                ["Walt Disney Concert Hall"] = (
                    categories: new[] { "landmark_monument", "cultural_site" },
                    tags: new[] { "architecture", "photography", "cultural", "tourist_hotspot" }
                ),
                ["The Broad"] = (
                    categories: new[] { "cultural_site", "activity_experience" },
                    tags: new[] { "cultural", "photography", "educational", "tourist_hotspot" }
                ),
                ["The Last Bookstore"] = (
                    categories: new[] { "market_street_life", "cultural_site" },
                    tags: new[] { "quirky", "hidden_gem", "local_favorite", "photography" }
                ),
                ["LA City Hall Observation Deck"] = (
                    categories: new[] { "viewpoint_scenic_spot", "landmark_monument" },
                    tags: new[] { "photography", "budget_friendly", "hidden_gem" }
                ),
            };

            static int DeriveLuxury(decimal avgCost, IEnumerable<string> tagKeys)
            {
                var hasPremium = tagKeys.Contains("premium");
                if (hasPremium || avgCost > 200) return 5;
                if (avgCost > 100) return 4;
                if (avgCost > 40) return 3;
                if (avgCost > 10) return 2;
                return 1;
            }

            static int FamilyScore(IEnumerable<string> tagKeys)
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

            static int AdventureScore(IEnumerable<string> tagKeys)
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

            static int AestheticTrendScore(IEnumerable<string> tagKeys)
            {
                var k = tagKeys.ToHashSet();
                if (k.Contains("photography") || k.Contains("architecture")) return 4;
                return 2;
            }

            // ─── Destination GUIDs ──────────────────────────────────────────────
            var destPannsId = Guid.NewGuid();
            var destHilltopId = Guid.NewGuid();
            var destMarketStreetId = Guid.NewGuid();
            var destDulansId = Guid.NewGuid();
            var destJamzId = Guid.NewGuid();
            var destSoFiId = Guid.NewGuid();
            var destTomsWatchBarId = Guid.NewGuid();
            var destKoreatownId = Guid.NewGuid();

            var destGrandCentralMarketId = Guid.NewGuid();
            var destAngelsFlightId = Guid.NewGuid();
            var destGrandParkId = Guid.NewGuid();
            var destDisneyConcertHallId = Guid.NewGuid();
            var destBroadId = Guid.NewGuid();
            var destLastBookstoreId = Guid.NewGuid();
            var destCityHallId = Guid.NewGuid();

            // ─── Destinations with full JSON descriptions ──────────────────────
            var newDestinations = new[]
            {
        // ── Pann's Restaurant ──
        new Destination
        {
            DestinationId = destPannsId,
            DestinationName = "Pann's Restaurant",
            CleanNormalizedSearchName = "panns restaurant",
            MetaphoneCode = "PNS RSTRNT",
            DoubleMetaphonePrimary = "PNS RSTRNT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Pann's is a genuine piece of Los Angeles history — a coffee-shop diner opened in 1958, famous for its swooping Googie architecture (the mid-century, space-age style built for the car culture of postwar LA). Its neon sign, boomerang roofline, and starburst details have made it a favorite filming location for decades. It sits a few minutes from Inglewood proper in the Ladera Heights/Westchester area, making it a classic, unhurried way to start a match day before the crowds build.",
              "directions": "Best Access: By car or rideshare — it's not directly on a rail line, so this stop works best if you're driving in or catching an early rideshare before match-day surge pricing kicks in.\n\nAddress for Rideshare: 6710 La Tijera Blvd, Los Angeles, CA 90045 (Ladera Heights / Westchester, a short drive from Inglewood and SoFi Stadium).",
              "whatToKnow": "Breakfast/Lunch Only: Pann's keeps daytime hours only — typically 8:00 AM-3:00 PM on weekdays and 7:00 AM-3:00 PM on weekends — so it's built for exactly this kind of early match-day breakfast, not a late start.\n\nGoogie Architecture: The building itself is a landmark of a nearly extinct architectural style; even if you're rushed, take a minute to appreciate the roofline and neon from the parking lot.\n\nDiner Classics: Known for hearty American breakfast plates, milkshakes, and pies alongside the usual diner lineup.",
              "thingsToBeWaryOf": "Closes Early: Because it closes by 3:00 PM daily, this only works as a breakfast or early-lunch stop — don't plan on it for anything later in the day.\n\nWeekend Waits: As a well-known spot, weekend mornings can have a short wait for a table — arriving right at opening helps.\n\nParking: Surface lot parking is available but can fill up during weekend brunch rushes.",
              "localPerspective": "Pann's has appeared in numerous films and TV productions over the decades thanks to its distinctive look, and Angelenos who grew up nearby treat it as a genuine neighborhood institution, not just a retro backdrop.\n\nRegulars order the classic breakfast plates and a slice of pie to go — a nod to the diner's old-school 'coffee shop' roots.",
              "hiddenCost": "Breakfast Plate: $14-$22.\nCoffee: $3-$5.\nMilkshake: $7-$9.\nParking: Free on-site lot.",
              "nearbyComplements": [
                "SoFi Stadium: About a 10-15 minute drive.",
                "Downtown Inglewood / Market Street: A short drive north for the rest of the pre-game itinerary.",
                "LAX: A few minutes away if you're arriving or departing by air."
              ],
              "bestTimeToVisit": "Early Morning (8:00 AM - 9:00 AM) on match day — beats both the breakfast rush and the building match-day traffic into Inglewood.",
              "crowdLevel": "Medium (5/10), higher on weekend mornings.",
              "accessibility": "Rating: 8/10 — flat, single-level diner with standard accessible parking and seating.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 18m,
            LuxuryRating = DeriveLuxury(18m, mapping["Pann's Restaurant"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Pann's Restaurant"].tags),
            AdventurePaceScore = AdventureScore(mapping["Pann's Restaurant"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Pann's Restaurant"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Pann's Restaurant"].tags),
            Latitude = 33.9856,
            Longitude = -118.3765,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Hilltop Coffee + Kitchen ──
        new Destination
        {
            DestinationId = destHilltopId,
            DestinationName = "Hilltop Coffee + Kitchen",
            CleanNormalizedSearchName = "hilltop coffee kitchen",
            MetaphoneCode = "HLTP KF KXN",
            DoubleMetaphonePrimary = "HLTP KF KXN",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Hilltop's flagship location sits right in Downtown Inglewood on La Brea Avenue, just before the main Market Street strip — a modern, plant-filled coffee shop and kitchen that's become a neighborhood anchor. It's the local alternative to Pann's: a shorter, walkable breakfast option that drops you directly into the Market Street/Inglewood itinerary rather than requiring a drive.",
              "directions": "Best Access: On foot within Downtown Inglewood.\n\nThe Direction Walk: Located on La Brea Avenue just before it meets the main Market Street strip — easy to combine with a walk through downtown Inglewood afterward.\n\nAddress for Rideshare: Downtown Inglewood, La Brea Ave near Market Street, Inglewood, CA 90301.",
              "whatToKnow": "Signature Order: The Banging Breakfast Sandwich paired with the Lavender Latte is the most recommended combination.\n\nMultiple Locations: Hilltop has expanded to several LA locations, but the Inglewood spot is the flagship and has the most local character.\n\nSeating: A relatively large interior means it's popular with people working or lingering over coffee, not just a quick grab-and-go.",
              "thingsToBeWaryOf": "Morning Rush: As a local favorite, expect a line during peak breakfast hours, especially on a big match-day weekend.\n\nLimited Parking: Street parking in Downtown Inglewood can be tight; walking or rideshare is often easier than driving and parking here directly.",
              "localPerspective": "This is a genuine neighborhood coffee shop where Inglewood locals come to work, meet, and eat — not a chain, and not built for tourists, which is part of its appeal on a match day when you want a taste of the actual neighborhood.",
              "hiddenCost": "Coffee/Latte: $5-$7.\nBreakfast Sandwich: $9-$13.\nPastries: $4-$6.",
              "nearbyComplements": [
                "Market Street (Inglewood): Directly adjacent — a natural continuation of the morning.",
                "Jamz Creamery: A short walk along Market Street.",
                "Dulan's Soul Food Kitchen: A few minutes' walk for an early lunch option."
              ],
              "bestTimeToVisit": "Early Morning (8:00 AM - 9:00 AM) — as an alternative to Pann's, this keeps you walking distance from the rest of the Inglewood itinerary.",
              "crowdLevel": "Medium (5/10), rising on weekend mornings.",
              "accessibility": "Rating: 9/10 — flat, modern storefront, easy street-level access.",
              "idealDuration": "30 to 45 minutes."
            }
            """,
            AverageCostPerDay = 14m,
            LuxuryRating = DeriveLuxury(14m, mapping["Hilltop Coffee + Kitchen"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Hilltop Coffee + Kitchen"].tags),
            AdventurePaceScore = AdventureScore(mapping["Hilltop Coffee + Kitchen"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Hilltop Coffee + Kitchen"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Hilltop Coffee + Kitchen"].tags),
            Latitude = 33.9598,
            Longitude = -118.3530,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Market Street (Inglewood) ──
        new Destination
        {
            DestinationId = destMarketStreetId,
            DestinationName = "Market Street (Inglewood)",
            CleanNormalizedSearchName = "market street inglewood",
            MetaphoneCode = "MRKT STRT",
            DoubleMetaphonePrimary = "MRKT STRT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Market Street is the heart of Downtown Inglewood — a walkable strip of local restaurants, coffee shops, and small businesses centered around N. Market St and E. Regent St. It's had a genuine renaissance in recent years as Inglewood has grown into a major entertainment hub, and it's the natural connector between a Pann's or Hilltop breakfast and the rest of the pre-match itinerary.",
              "directions": "Best Access: On foot, connecting Hilltop Coffee + Kitchen, Dulan's Soul Food Kitchen, and Jamz Creamery along the same strip.\n\nThe Direction Walk: Center yourself at the intersection of N. Market St and E. Regent St and explore outward — most of the notable stops are within a few blocks.\n\nAddress for Rideshare: N Market St & E Regent St, Inglewood, CA 90301.",
              "whatToKnow": "Local Businesses: The strip is dominated by independent, longtime Inglewood-owned spots rather than chains — a good place to feel the neighborhood's real character before the stadium crowds take over.\n\nWalkability: Everything on Market Street is close enough to cover on foot in under an hour, making it an easy pre-match stroll.\n\nGrowing Fast: Inglewood has changed rapidly since SoFi Stadium and Intuit Dome opened nearby — expect a mix of decades-old institutions and newer businesses catering to game-day crowds.",
              "thingsToBeWaryOf": "Match-Day Crowds: On major event days, Market Street sees a noticeable uptick in foot traffic as fans stage before heading to the stadium.\n\nLimited Parking: Like much of Downtown Inglewood, street parking is limited — plan to walk from a nearby stop or use rideshare.",
              "localPerspective": "Locals have watched Downtown Inglewood transform over the past several years, and Market Street is where that change is most visible — longtime soul food and coffee spots now sit alongside newer businesses drawing crowds from SoFi Stadium events.",
              "hiddenCost": "No entry cost — spending here is entirely at the individual food/shop stops along the strip.",
              "nearbyComplements": [
                "Hilltop Coffee + Kitchen: Right at the start of the strip.",
                "Dulan's Soul Food Kitchen: A short walk along Market Street.",
                "Jamz Creamery: A short walk for dessert."
              ],
              "bestTimeToVisit": "Mid-Morning (9:00 AM - 11:00 AM) on match day — enough time to stroll before heading toward the stadium.",
              "crowdLevel": "Medium (5/10), Higher (7/10) on major SoFi Stadium event days.",
              "accessibility": "Rating: 8/10 — flat sidewalks, generally easy to navigate on foot.",
              "idealDuration": "30 to 60 minutes for a walk-through of the strip."
            }
            """,
            AverageCostPerDay = 5m,
            LuxuryRating = DeriveLuxury(5m, mapping["Market Street (Inglewood)"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Market Street (Inglewood)"].tags),
            AdventurePaceScore = AdventureScore(mapping["Market Street (Inglewood)"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Market Street (Inglewood)"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Market Street (Inglewood)"].tags),
            Latitude = 33.9596,
            Longitude = -118.3534,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Dulan's Soul Food Kitchen ──
        new Destination
        {
            DestinationId = destDulansId,
            DestinationName = "Dulan's Soul Food Kitchen",
            CleanNormalizedSearchName = "dulans soul food kitchen",
            MetaphoneCode = "TLNS SL FT KXN",
            DoubleMetaphonePrimary = "TLNS SL FT KXN",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Dulan's is a Downtown Inglewood staple and an LA soul food institution — the sister restaurant to the original Dulan's on Crenshaw, founded by the late Adolf Dulan, self-proclaimed 'King of Soul Food.' Expect hefty, hot plates of smothered chicken, pork chops, and fried fish alongside classic sides like black-eyed peas, cornbread dressing, and collard greens. It's the go-to option if you skipped a formal breakfast or want a hearty early lunch before the match.",
              "directions": "Best Access: On foot from Market Street or a short drive from elsewhere in Inglewood.\n\nAddress for Rideshare: 202 East Manchester Boulevard, Inglewood, CA 90301.",
              "whatToKnow": "Expect a Line: A line outside is common — it's part of the experience and moves steadily; the food is worth the wait according to regulars.\n\nPortions: Portions are generous — consider sharing sides between two people.\n\nDessert: Save room for sweet potato pie or peach cobbler if you're not rushing to the stadium.",
              "thingsToBeWaryOf": "Timing: Given the potential wait, this works best as an early lunch rather than a quick stop if you're tight on time before kickoff.\n\nHeavy Meal: The portions are substantial — pace yourself if you're planning several more food stops before the match.",
              "localPerspective": "For many South LA and Inglewood locals, Dulan's is considered THE essential soul food experience in the city — a genuine community fixture, not a tourist stop, which is part of why it draws a loyal line of regulars.",
              "hiddenCost": "Entree Plate: $16-$26.\nSides: $4-$7 each.\nDessert (Sweet Potato Pie/Peach Cobbler): $5-$8.",
              "nearbyComplements": [
                "Market Street (Inglewood): The surrounding strip.",
                "Jamz Creamery: A short walk for dessert instead of (or in addition to) Dulan's own desserts.",
                "SoFi Stadium: A short drive or rideshare away."
              ],
              "bestTimeToVisit": "Late Morning (10:30 AM - 12:00 PM) if using this as an early lunch instead of a separate breakfast.",
              "crowdLevel": "High (7/10) — lines are common, especially around lunch and on event days.",
              "accessibility": "Rating: 7/10 — street-level entrance, standard restaurant seating.",
              "idealDuration": "45 minutes to 1 hour, including wait time."
            }
            """,
            AverageCostPerDay = 22m,
            LuxuryRating = DeriveLuxury(22m, mapping["Dulan's Soul Food Kitchen"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Dulan's Soul Food Kitchen"].tags),
            AdventurePaceScore = AdventureScore(mapping["Dulan's Soul Food Kitchen"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Dulan's Soul Food Kitchen"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Dulan's Soul Food Kitchen"].tags),
            Latitude = 33.9597,
            Longitude = -118.3521,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Jamz Creamery ──
        new Destination
        {
            DestinationId = destJamzId,
            DestinationName = "Jamz Creamery",
            CleanNormalizedSearchName = "jamz creamery",
            MetaphoneCode = "JMS KRMR",
            DoubleMetaphonePrimary = "JMS KRMR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A music-themed ice cream shop serving homemade ice cream on Market Street since 2001, Jamz Creamery is Inglewood's classic sweet-treat stop. With 28 flavors available in a cup or cone, the signature order is THE JAZZ — a decadent ice cream sandwich built from two soft homemade cake rounds with a drizzle topping and whipped cream. It's a quick, fun palate-cleanser between the Market Street food stops and heading toward the stadium.",
              "directions": "Best Access: On foot along Market Street, Downtown Inglewood.\n\nAddress for Rideshare: 231 E Manchester Blvd, Inglewood, CA 90301.",
              "whatToKnow": "The Jazz: The signature dessert is genuinely large — consider sharing if you're planning to eat more before the match.\n\n28 Flavors: A rotating, generous flavor lineup, with both classic and creative options.\n\nMusic Theme: The shop leans into a jazz/music aesthetic in its branding and interior, tying into Inglewood's musical heritage.",
              "thingsToBeWaryOf": "Small Space: Seating is limited — most people grab their order and continue walking Market Street.\n\nMelts Fast on Hot Days: If you're heading straight to the stadium after, eat it there rather than trying to carry it far in warm weather.",
              "localPerspective": "A neighborhood fixture for over two decades, Jamz is the kind of spot longtime Inglewood residents bring their kids to, not just a stop invented for game-day crowds.",
              "hiddenCost": "Ice Cream Cup/Cone: $5-$8.\nTHE JAZZ (signature dessert): $8-$12.",
              "nearbyComplements": [
                "Market Street (Inglewood): Directly on the strip.",
                "Dulan's Soul Food Kitchen: A short walk for a savory counterpoint.",
                "SoFi Stadium: A short drive or rideshare away."
              ],
              "bestTimeToVisit": "Late Morning to Early Afternoon (11:00 AM - 1:00 PM) — a good palate-cleanser before heading to the stadium.",
              "crowdLevel": "Medium (5/10), higher on warm weekends.",
              "accessibility": "Rating: 8/10 — small, flat, street-level shop.",
              "idealDuration": "15 to 25 minutes."
            }
            """,
            AverageCostPerDay = 8m,
            LuxuryRating = DeriveLuxury(8m, mapping["Jamz Creamery"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Jamz Creamery"].tags),
            AdventurePaceScore = AdventureScore(mapping["Jamz Creamery"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Jamz Creamery"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Jamz Creamery"].tags),
            Latitude = 33.9597,
            Longitude = -118.3518,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── SoFi Stadium ──
        new Destination
        {
            DestinationId = destSoFiId,
            DestinationName = "SoFi Stadium",
            CleanNormalizedSearchName = "sofi stadium",
            MetaphoneCode = "SF STTM",
            DoubleMetaphonePrimary = "SF STTM",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "SoFi Stadium is one of the most striking venues in world sports — a translucent, oculus-topped stadium in Inglewood that's home to the Rams and Chargers year-round and one of the marquee host venues for the FIFA World Cup 2026, including group-stage, Round of 32, and a Quarterfinal match. Note that FIFA prohibits corporate sponsor names in its own branding, so during the tournament the venue is officially referred to as 'Los Angeles Stadium' — the building, address, and gates are identical, only the name changes on FIFA materials.\n\nWhat to Do: Walk the plaza and take photos with the stadium's massive canopy roof. Browse the official team/tournament stores. Check for pre-match fan activation zones and interactive experiences in the surrounding American Airlines Plaza. Then head in for the match itself.",
              "directions": "Best Access: Metro K Line is the most reliable way in for the World Cup — a $1.75 fare with a TAP card eliminates stadium parking costs and traffic entirely.\n\nMatch Day Direct: A premium bus service runs directly to the stadium's dedicated bus hub from Union Station, Downtown Santa Monica, and North Hollywood, beginning roughly 3 hours before kickoff.\n\nAddress for Rideshare/GPS: 1001 Stadium Drive, Inglewood, CA 90301.",
              "whatToKnow": "Clear Bag Policy: Only clear bags up to 12\" x 6\" x 12\" are allowed inside — plan accordingly, as this is strictly enforced for World Cup matches.\n\nParking: Official Hollywood Park lots require pre-purchase through the SoFi Stadium app or AXS (no walk-up sales); premium lots closest to the gates sell out first and cost more, while mid-distance lots offer better value and last longer into the on-sale period.\n\nGates: Plan to arrive 1.5-2 hours before kickoff for a major match — security lines move faster earlier, and fan-zone activations are worth arriving early for.",
              "thingsToBeWaryOf": "Sell-Out Crowds: World Cup matches will be at or near capacity, meaning longer security lines and busier concourses than a typical event.\n\nSurge Pricing: Rideshare prices spike heavily right around kickoff and immediately after the final whistle — Metro's direct service is explicitly designed to sidestep this.\n\nNo On-Site Parking at Some Transit Points: Certain Metro drop-off locations (like LAX/Metro Transit Center) have no on-site parking or pickup zone — check your specific route in advance via Metro's trip planner.",
              "localPerspective": "Locals who've followed the stadium since it opened in 2020 still marvel at the translucent canopy roof, which lets in natural light while sheltering most seats from LA's sun and occasional rain — a design that also amplifies crowd noise back onto the field.\n\nThe surrounding Hollywood Park development (including Intuit Dome, the Kia Forum, and YouTube Theater) has turned the area into one of the densest sports-and-entertainment districts in the country.",
              "hiddenCost": "Match Ticket: Highly variable — official and resale pricing for World Cup fixtures can range from roughly $150 to $1,000+ depending on the match and tier.\nParking: Hollywood Park lots run $30-$100 pre-purchased; premium official lots can run $240-$400+ for top matches.\nMetro K Line Fare: $1.75 each way with a TAP card.\nConcessions: $8-$15 for food, $10-$16 for beer.\nOfficial Merchandise: $30-$120.",
              "nearbyComplements": [
                "Tom's Watch Bar (Inglewood): Directly across the street in Hollywood Park, ideal before or after the match.",
                "Intuit Dome and the Kia Forum: Part of the same Hollywood Park entertainment district.",
                "Downtown Inglewood / Market Street: A short drive or rideshare north."
              ],
              "bestTimeToVisit": "Arrive 1.5 to 2 hours before kickoff for a major World Cup match — enough time to clear security, browse team stores, and take in the fan-zone atmosphere.",
              "crowdLevel": "Maximum (10/10) for a World Cup match.",
              "accessibility": "Rating: 9/10 — the stadium is fully ADA-compliant with accessible seating, elevators, and wide concourses; the Metro K Line station is also step-free.",
              "idealDuration": "3.5 to 4.5 hours total including pre-match arrival, the full match, and the post-match exit."
            }
            """,
            AverageCostPerDay = 260m,
            LuxuryRating = DeriveLuxury(260m, mapping["SoFi Stadium"].tags),
            AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["SoFi Stadium"].tags),
            AdventurePaceScore = AdventureScore(mapping["SoFi Stadium"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["SoFi Stadium"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["SoFi Stadium"].tags),
            Latitude = 33.9535,
            Longitude = -118.3392,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Tom's Watch Bar (Inglewood) ──
        new Destination
        {
            DestinationId = destTomsWatchBarId,
            DestinationName = "Tom's Watch Bar (Inglewood)",
            CleanNormalizedSearchName = "toms watch bar inglewood",
            MetaphoneCode = "TMS WX BR",
            DoubleMetaphonePrimary = "TMS WX PR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Tom's Watch Bar's Inglewood location sits directly next to Intuit Dome and across the street from SoFi Stadium, right in the heart of the Hollywood Park entertainment district. It's built specifically for exactly this scenario — fans without a ticket, or those meeting up with friends before or after the match — with 360-degree screens, full stadium sound, a rooftop bar and lounge, and dedicated VIP watch-party seating for major World Cup matches.",
              "directions": "Best Access: Directly across the street from SoFi Stadium, an easy walk from the stadium plaza.\n\nAddress for Rideshare: 3900 W Century Blvd, Inglewood, CA 90303.",
              "whatToKnow": "VIP Matchday Seating: For high-demand World Cup matches, reserved tables and premium viewing areas are available and expected to sell out early — booking ahead is worth it if you don't have a match ticket.\n\nRooftop Bar: The venue includes a rooftop bar and lounge in addition to its main floor, with flexible indoor/outdoor seating for game-day crowds.\n\nDedicated Parking: The property includes its own parking, which also supports tailgate-style gatherings on major event days.",
              "thingsToBeWaryOf": "No Reservations for General Seating: Standard seating is walk-in only at most Tom's Watch Bar locations — arrive early for a good spot on a marquee match day.\n\nExtremely Busy Pre/Post-Match: Given its literal doorstep location to SoFi Stadium, expect this to be one of the busiest bars in the area on match day, especially right after the final whistle.",
              "localPerspective": "As one of the newest additions to the Hollywood Park district (opened in late 2025), it's quickly become a default gathering spot for fans who want the stadium energy without a ticket — locals describe it as the go-to option for watching the World Cup right outside SoFi's gates.",
              "hiddenCost": "Beer/Cocktail: $9-$14.\nBar Food: $14-$24 per dish.\nVIP Watch Party Seating: Pricing varies significantly by match — book early for high-demand fixtures.",
              "nearbyComplements": [
                "SoFi Stadium: Directly across the street.",
                "Intuit Dome: Immediately adjacent.",
                "Kia Forum and YouTube Theater: Both within walking distance in the same district."
              ],
              "bestTimeToVisit": "Before kickoff for pre-match energy, or immediately after the final whistle if you want to stay close to the stadium rather than travel to Koreatown.",
              "crowdLevel": "Maximum (10/10) immediately before and after a World Cup match at SoFi.",
              "accessibility": "Rating: 9/10 — modern venue with flat, wide access and dedicated parking.",
              "idealDuration": "1.5 to 3 hours."
            }
            """,
            AverageCostPerDay = 45m,
            LuxuryRating = DeriveLuxury(45m, mapping["Tom's Watch Bar (Inglewood)"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Tom's Watch Bar (Inglewood)"].tags),
            AdventurePaceScore = AdventureScore(mapping["Tom's Watch Bar (Inglewood)"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Tom's Watch Bar (Inglewood)"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Tom's Watch Bar (Inglewood)"].tags),
            Latitude = 33.9553,
            Longitude = -118.3387,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Koreatown ──
        new Destination
        {
            DestinationId = destKoreatownId,
            DestinationName = "Koreatown",
            CleanNormalizedSearchName = "koreatown",
            MetaphoneCode = "KRTN",
            DoubleMetaphonePrimary = "KRTN",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Koreatown (K-Town) is the largest concentration of Korean culture in the United States — a dense, three-square-mile neighborhood west of Downtown LA that genuinely never sleeps. Korean BBQ restaurants, 24-hour karaoke rooms (noraebang), late-night cafés, and Korean spas operate around the clock. For a match-day crowd looking to keep the celebration going well past the final whistle — especially those not headed straight back toward Inglewood — it's one of LA's most electric late-night options.",
              "directions": "Best Access: Metro B/D Lines to Wilshire/Western or Wilshire/Normandie stations put you right in the neighborhood.\n\nFrom SoFi Stadium: Expect roughly 30-45+ minutes by car or rideshare depending on match-day traffic; this stop makes most sense if you're staying in or near Koreatown/DTLA rather than heading back to Inglewood that night.\n\nAddress for Rideshare: Center of the neighborhood is roughly Wilshire Blvd & Western Ave, Los Angeles, CA 90010.",
              "whatToKnow": "Korean BBQ: Parks BBQ (upscale, USDA Prime meats) and Kang Ho-dong Baekjeong (loud, great for groups) are among the most recommended; Hae Jang Chon offers 24-hour all-you-can-eat for late arrivals.\n\nKaraoke: Noraebang (private karaoke rooms) are the classic K-Town late-night activity — Pharaoh Karaoke is a well-reviewed, popular option with thousands of songs.\n\nGetting Around: Everything within the neighborhood itself is very walkable once you arrive; street parking is limited, so valet ($3-5 at many restaurants) or a lot is often easier.",
              "thingsToBeWaryOf": "Distance from SoFi Stadium: This is a genuine commitment, not a quick stop — factor in real travel time and cost if you're planning to return to Inglewood or elsewhere afterward.\n\nAll-You-Can-Eat Etiquette: At Korean BBQ, let the staff do the actual grilling and avoid over-ordering — most spots have per-person minimums and food-waste policies.\n\nLate-Night Crowds: Karaoke rooms and BBQ spots get genuinely packed after 9-10 PM, especially on a weekend match night — reservations where possible are worth it.",
              "localPerspective": "Koreatown is widely considered one of LA's best neighborhoods for night owls — locals treat a Korean BBQ dinner into late-night karaoke as a complete, self-contained night out, not just a quick meal.",
              "hiddenCost": "Korean BBQ (all-you-can-eat): $30-$58 per person.\nKaraoke Room: Priced per hour per room, typically $35-$60/hour split among the group.\nValet Parking: $3-$5 at most restaurants.\nLate-Night Snacks (fried chicken, shaved ice): $10-$20.",
              "nearbyComplements": [
                "Downtown LA: A short drive or Metro ride east.",
                "Liberty Park: A small, quiet green space in the middle of the neighborhood if you need a breather.",
                "K Galleria: A Korean shopping mall with a food court, useful if you want variety beyond BBQ."
              ],
              "bestTimeToVisit": "Evening into late night (7:00 PM onward) — this is genuinely a nighttime neighborhood, and it only gets livelier as the hours go on.",
              "crowdLevel": "High (7/10) most nights, Maximum (10/10) on weekend match nights.",
              "accessibility": "Rating: 7/10 — the neighborhood itself is flat and walkable, though individual restaurants and karaoke venues vary in accessibility.",
              "idealDuration": "2.5 to 4 hours for dinner and karaoke."
            }
            """,
            AverageCostPerDay = 55m,
            LuxuryRating = DeriveLuxury(55m, mapping["Koreatown"].tags),
            AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Koreatown"].tags),
            AdventurePaceScore = AdventureScore(mapping["Koreatown"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Koreatown"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Koreatown"].tags),
            Latitude = 34.0621,
            Longitude = -118.3006,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Grand Central Market ──
        new Destination
        {
            DestinationId = destGrandCentralMarketId,
            DestinationName = "Grand Central Market",
            CleanNormalizedSearchName = "grand central market",
            MetaphoneCode = "KRNT SNTRL MRKT",
            DoubleMetaphonePrimary = "KRNT SNTRL MRKT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Open since 1917, Grand Central Market is a sprawling, historic food hall in the heart of Downtown LA, home to dozens of vendors spanning nearly every cuisine in the city. It sits directly across the street from Angels Flight Railway's lower station, making it the natural first stop of a DTLA day and the connective tissue for the rest of the itinerary. Standout stalls include Eggslut for a breakfast sandwich, Sarita's Pupuseria for Salvadoran pupusas, and G&B Coffee for a proper espresso.",
              "directions": "Best Access: Metro B/D Line to Pershing Square Station, then a short walk north.\n\nThe Direction Walk: The market's Hill Street entrance sits directly across from Angels Flight's lower station, making the two an easy combined stop.\n\nAddress for Rideshare: 317 S Broadway, Los Angeles, CA 90013.",
              "whatToKnow": "Vendor Highlights: Eggslut (breakfast sandwiches, expect a line), Sarita's Pupuseria (a local favorite for pupusas), G&B Coffee (specialty espresso), plus DTLA Cheese, Wexler's Deli, and McConnell's Ice Cream among the roughly three dozen stalls.\n\nMultiple Cuisines: The market spans Italian, Chinese, Thai, Mexican, Mediterranean, Japanese, and Salvadoran food, along with bakeries, juice bars, and a full bar.\n\nSeating: Communal tables are scattered throughout — expect to share space during busy hours.",
              "thingsToBeWaryOf": "Popular Stall Lines: Eggslut in particular can have a real line at peak breakfast hours — arriving right as the market opens helps.\n\nBusy Midday: The market gets genuinely packed for lunch; an earlier visit is more relaxed if your schedule allows.\n\nCash and Card: Most vendors take cards, but it's worth having some cash for smaller stalls.",
              "localPerspective": "This was historically the market that hillside Bunker Hill residents would ride Angels Flight down to visit — a piece of that original relationship still exists today, with the funicular's lower station sitting right across the street.",
              "hiddenCost": "Breakfast Sandwich (Eggslut): $10-$14.\nPupusas: $4-$7 each.\nCoffee (G&B): $4-$7.\nGeneral Meal Budget: $15-$25 per person to sample a couple of stalls.",
              "nearbyComplements": [
                "Angels Flight Railway: Directly across Hill Street.",
                "The Bradbury Building: A historic, ornate office building a block away.",
                "Broadway Historic Theatre District: A short walk south."
              ],
              "bestTimeToVisit": "Early Morning (8:30 AM - 10:00 AM) — freshest food, shortest lines, and a comfortable pace before the rest of the DTLA day.",
              "crowdLevel": "High (7/10) by midday, Medium (5/10) early morning.",
              "accessibility": "Rating: 8/10 — mostly flat, open floor plan with wide walkways between stalls.",
              "idealDuration": "45 minutes to 1.5 hours."
            }
            """,
            AverageCostPerDay = 20m,
            LuxuryRating = DeriveLuxury(20m, mapping["Grand Central Market"].tags),
            AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Grand Central Market"].tags),
            AdventurePaceScore = AdventureScore(mapping["Grand Central Market"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Grand Central Market"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Grand Central Market"].tags),
            Latitude = 34.0505,
            Longitude = -118.2489,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Angels Flight Railway ──
        new Destination
        {
            DestinationId = destAngelsFlightId,
            DestinationName = "Angels Flight Railway",
            CleanNormalizedSearchName = "angels flight railway",
            MetaphoneCode = "ANJLS FLT RLW",
            DoubleMetaphonePrimary = "ANJLS FLT RLW",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Billed as the world's shortest railway, Angels Flight is a Beaux-Arts funicular that has carried passengers up Bunker Hill since 1901. Its two vintage orange-and-black cars climb a steep incline connecting Grand Central Market at the bottom to California Plaza at the top in under a minute — a quick, novel ride that's appeared in numerous films, including La La Land, and remains a beloved LA landmark despite its tiny scale.",
              "directions": "Best Access: Lower station sits directly across Hill Street from Grand Central Market; upper station opens onto California Plaza near The Broad and Walt Disney Concert Hall.\n\nAddress for Rideshare: Lower Station — 351 S Hill St, Los Angeles, CA 90013. Upper Station — 350 S Grand Ave, Los Angeles, CA 90071.",
              "whatToKnow": "Fare: $1.50 each way (or $0.75 one-way with a Metro TAP card loaded with stored value); a $3.00 souvenir round-trip ticket is available if you want a keepsake.\n\nHours: Open daily 6:45 AM-10:00 PM, no reservations needed — just show up and board.\n\nUse It as a Shortcut: Rather than climbing the steep Bunker Hill stairs, riding up is the easiest way to connect Grand Central Market to Grand Park, The Broad, and Walt Disney Concert Hall.",
              "thingsToBeWaryOf": "Cash Preferred: Some sources note it operates cash-first at the ticket booth, though TAP is accepted — having a few dollars in cash as backup is a good idea.\n\nOccasional Closures: The railway occasionally closes for filming or maintenance — check ahead if a specific ride is essential to your plan.\n\nShort Ride: At under a minute, it's a novelty more than a scenic journey — manage expectations accordingly.",
              "localPerspective": "Long-time Angelenos sometimes assume Angels Flight is still closed from a past shutdown — it has, in fact, been operating again since 2017, and locals who rediscover it are often surprised how easy and cheap it is.",
              "hiddenCost": "One-Way Fare: $1.50 (or $0.75 with TAP stored value).\nSouvenir Round-Trip Ticket: $3.00.\nCommuter Ticket Books: $6.00 for 5 rides, $45.00 for 40 rides.",
              "nearbyComplements": [
                "Grand Central Market: Directly across the street from the lower station.",
                "The Broad and Walt Disney Concert Hall: A short walk from the upper station.",
                "California Plaza: Immediately surrounding the upper station."
              ],
              "bestTimeToVisit": "Late Morning (10:00 AM - 11:00 AM), riding up from Grand Central Market as you head toward Grand Park and the Music Center campus.",
              "crowdLevel": "Medium (5/10) — capacity is naturally limited by the small cars, so it rarely feels overwhelming.",
              "accessibility": "Rating: 7/10 — the cars themselves are accessible, though the platforms involve a short incline; an alternative stairway exists for those who prefer or need to walk.",
              "idealDuration": "10 to 15 minutes including the wait."
            }
            """,
            AverageCostPerDay = 3m,
            LuxuryRating = DeriveLuxury(3m, mapping["Angels Flight Railway"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Angels Flight Railway"].tags),
            AdventurePaceScore = AdventureScore(mapping["Angels Flight Railway"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Angels Flight Railway"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Angels Flight Railway"].tags),
            Latitude = 34.0517,
            Longitude = -118.2493,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Gloria Molina Grand Park ──
        new Destination
        {
            DestinationId = destGrandParkId,
            DestinationName = "Gloria Molina Grand Park",
            CleanNormalizedSearchName = "gloria molina grand park",
            MetaphoneCode = "KLR MLN KRNT PRK",
            DoubleMetaphonePrimary = "KLR MLN KRNT PRK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A 12-acre green corridor connecting the Music Center campus (home to Walt Disney Concert Hall) down to LA City Hall and the Civic Center, Grand Park is Downtown LA's main public green space — a colorful, welcoming stretch of lawns, splash pads, and public art that offers a genuine breather between the density of Bunker Hill's cultural venues and the Civic Center's government buildings.",
              "directions": "Best Access: A short walk downhill from California Plaza/Walt Disney Concert Hall, or accessible via the Metro B/D Line to Civic Center/Grand Park Station.\n\nAddress for Rideshare: 200 N Grand Ave, Los Angeles, CA 90012.",
              "whatToKnow": "Free and Open: The park is free to enter with no set hours restricting daytime access, and hosts regular public events and installations.\n\nConnector Route: It functions as much as a walking corridor as a destination — a pleasant way to move from Walt Disney Concert Hall/The Broad toward LA City Hall.\n\nSplash Pad: A popular, colorful pink splash pad area is a favorite with families and photographers alike.",
              "thingsToBeWaryOf": "Exposed to Sun: Much of the park is open lawn with limited shade — worth noting on a hot LA afternoon.\n\nEvent Closures: Portions of the park occasionally close for scheduled public events — check ahead if you want the full open space.",
              "localPerspective": "Named for the late LA County Supervisor Gloria Molina, the park has become a genuine civic gathering space for the city — hosting everything from lunchtime picnics for downtown office workers to large public celebrations and rallies.",
              "hiddenCost": "Free entry.\nCoffee/Snack Cart (if present): $4-$8.",
              "nearbyComplements": [
                "Walt Disney Concert Hall: At the park's upper end.",
                "LA City Hall: At the park's lower end.",
                "The Music Center: Immediately adjacent to the park's upper section."
              ],
              "bestTimeToVisit": "Midday (11:30 AM - 1:00 PM) — a relaxing walk-through between Angels Flight/The Broad and LA City Hall, with good light for photos.",
              "crowdLevel": "Medium (5/10), higher during weekday lunch hours and public events.",
              "accessibility": "Rating: 10/10 — flat, paved paths throughout, fully wheelchair and stroller accessible.",
              "idealDuration": "20 to 30 minutes for a walk-through."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["Gloria Molina Grand Park"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Gloria Molina Grand Park"].tags),
            AdventurePaceScore = AdventureScore(mapping["Gloria Molina Grand Park"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Gloria Molina Grand Park"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Gloria Molina Grand Park"].tags),
            Latitude = 34.0567,
            Longitude = -118.2445,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Walt Disney Concert Hall ──
        new Destination
        {
            DestinationId = destDisneyConcertHallId,
            DestinationName = "Walt Disney Concert Hall",
            CleanNormalizedSearchName = "walt disney concert hall",
            MetaphoneCode = "WLT TSN KNSRT HL",
            DoubleMetaphonePrimary = "WLT TSN KNSRT HL",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Frank Gehry's sweeping, stainless-steel concert hall is one of the most photographed buildings in Los Angeles and home to the LA Philharmonic. Its curved, sail-like exterior catches light dramatically throughout the day, and the rooftop Blue Ribbon Garden offers a quiet, free escape with fountain art and skyline views — a self-guided experience that doesn't require a concert ticket to enjoy.",
              "directions": "Best Access: A short walk from the Angels Flight upper station or California Plaza; also reachable via Metro B/D Line to Civic Center/Grand Park Station.\n\nAddress for Rideshare: 111 S Grand Ave, Los Angeles, CA 90012.",
              "whatToKnow": "Free Self-Guided Tour: The building and Blue Ribbon Garden are free to visit and self-guided, with no reservation required — though blackout dates apply around the concert schedule, so it's worth checking ahead if you have a specific time in mind.\n\nArchitecture Focus: Even without going inside, the exterior curves are worth walking a full loop around for different angles and photos.\n\nBlue Ribbon Garden: A rooftop garden featuring a large rose-shaped fountain (made of Delft porcelain, a tribute to Lillian Disney) — a peaceful spot most visitors miss.",
              "thingsToBeWaryOf": "Blackout Dates: Because it's a working concert hall, self-guided access can be restricted around rehearsals or performances — check the Music Center's website before planning around it.\n\nNo Casual Interior Access on Performance Days: The main auditorium itself typically requires a ticketed performance or guided tour to see; the self-guided access covers public areas and the garden.",
              "localPerspective": "Angelenos consider it one of the city's defining pieces of architecture — locals will often point newcomers here specifically for photos, even if they've never attended a performance inside.",
              "hiddenCost": "Self-Guided Visit: Free.\nGuided Tours (when available): Check current pricing, as these are separate from the free self-guided access.\nConcert Tickets (if attending a performance): Highly variable by program.",
              "nearbyComplements": [
                "The Broad: Directly across the street.",
                "Angels Flight Railway: A short walk to the upper station.",
                "Gloria Molina Grand Park: Adjacent, just downhill."
              ],
              "bestTimeToVisit": "Midday (12:00 PM - 1:30 PM) for the best light on the exterior curves, avoiding rehearsal-related closures more common in the evening.",
              "crowdLevel": "Medium (5/10) around the exterior, lower for the rooftop garden.",
              "accessibility": "Rating: 8/10 — public areas and the garden are wheelchair accessible via elevator.",
              "idealDuration": "30 to 45 minutes for the exterior and Blue Ribbon Garden."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["Walt Disney Concert Hall"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Walt Disney Concert Hall"].tags),
            AdventurePaceScore = AdventureScore(mapping["Walt Disney Concert Hall"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Walt Disney Concert Hall"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Walt Disney Concert Hall"].tags),
            Latitude = 34.0554,
            Longitude = -118.2487,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── The Broad ──
        new Destination
        {
            DestinationId = destBroadId,
            DestinationName = "The Broad",
            CleanNormalizedSearchName = "the broad",
            MetaphoneCode = "0 BRT",
            DoubleMetaphonePrimary = "T PRT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Directly across the street from Walt Disney Concert Hall, The Broad is a contemporary art museum with general admission that's always free — a genuine rarity among major LA museums. Its honeycomb-veiled facade houses works by Basquiat, Lichtenstein, Murakami, and Warhol, along with two of Yayoi Kusama's famous Infinity Mirror Rooms, one of which (Longing for Eternity, on the third floor) requires no reservation at all.",
              "directions": "Best Access: Directly across Grand Avenue from Walt Disney Concert Hall.\n\nAddress for Rideshare: 221 S Grand Ave, Los Angeles, CA 90012.",
              "whatToKnow": "General Admission is Free: No cost for the third-floor collection galleries; timed tickets are released monthly on the last Wednesday at 10:00 AM Pacific for the following month, or you can try the onsite standby line if same-day tickets aren't available.\n\nInfinity Mirrored Room: The larger, walk-in Infinity Mirrored Room—The Souls of Millions of Light Years Away requires a separate free timed reservation (also released monthly) and has very limited capacity — four visitors at a time for about a minute; the smaller Longing for Eternity on the third floor requires no reservation and is included with general admission.\n\nHours: Tuesday, Wednesday, Friday 11 AM-5 PM; Thursday 11 AM-8 PM; Saturday and Sunday 10 AM-6 PM; closed Mondays.",
              "thingsToBeWaryOf": "Infinity Mirrored Room Access is Genuinely Limited: If you didn't book a reservation weeks in advance, getting into the larger room isn't guaranteed — the standby/walk-in system exists but isn't reliable for a tight match-day schedule; don't count on it.\n\nBag Policy: The museum reserves the right to restrict bags and certain items — check current guidelines before arriving with a large bag.\n\nLate Arrivals: Visitors with timed tickets can enter up to 1 hour after their printed time, but admission stops 30 minutes before closing.",
              "localPerspective": "Locally, it's simply called 'The Broad' pronounced like 'road,' not 'broad' — a common visitor mispronunciation. Its free admission model has made it one of the most-visited contemporary art spaces in the city despite being one of the newest.",
              "hiddenCost": "General Admission: Free.\nInfinity Mirrored Room (Souls of Millions of Light Years Away): Free with separate timed reservation.\nSpecial Exhibitions: Paid separately, pricing varies.\nGift Shop: $10-$50+ for prints, books, and merchandise.",
              "nearbyComplements": [
                "Walt Disney Concert Hall: Directly across the street.",
                "Angels Flight Railway: A short walk to the upper station.",
                "MOCA (Museum of Contemporary Art): A few blocks away for more contemporary art."
              ],
              "bestTimeToVisit": "Late Morning (11:00 AM - 1:00 PM) on a weekday if possible, to avoid weekend crowds — though the free-admission draw means it's rarely quiet.",
              "crowdLevel": "High (7/10), especially on weekends and for the Infinity Mirrored Room line.",
              "accessibility": "Rating: 9/10 — modern, fully accessible building with elevators throughout.",
              "idealDuration": "1 to 2 hours for the galleries; add wait time if attempting the Infinity Mirrored Room via standby."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["The Broad"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["The Broad"].tags),
            AdventurePaceScore = AdventureScore(mapping["The Broad"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["The Broad"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["The Broad"].tags),
            Latitude = 34.0541,
            Longitude = -118.2504,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── The Last Bookstore ──
        new Destination
        {
            DestinationId = destLastBookstoreId,
            DestinationName = "The Last Bookstore",
            CleanNormalizedSearchName = "the last bookstore",
            MetaphoneCode = "0 LST BKSTR",
            DoubleMetaphonePrimary = "T LST PKSTR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "California's largest new-and-used bookstore, set inside a former bank building with soaring ceilings, marble columns, and 'haunted' old bank vaults now filled with horror and true crime titles. Its most photographed feature is a spiraling tunnel built entirely from arched books in the upstairs annex. At 22,000 square feet with a record shop built in, it's a beloved detour for visitors and one of the most distinctive independent bookstores in the country.",
              "directions": "Best Access: A roughly 10-minute walk from Grand Central Market or Angels Flight's lower station, heading south on Spring Street.\n\nAddress for Rideshare: 453 S Spring St, Ground Floor, Los Angeles, CA 90013.",
              "whatToKnow": "The Book Tunnel: Located in the upstairs art annex, this spiraling tunnel made of arched books is the most Instagrammed spot in the store — worth the walk up.\n\nBank Vaults: The building's original bank vaults now house horror and mystery sections, a fittingly atmospheric touch.\n\nVinyl and More: Beyond books, there's a substantial vinyl record section and rotating small-press/local artist displays.",
              "thingsToBeWaryOf": "Easy to Lose Track of Time: The maze-like upper floor and browsing culture here can eat more time than planned — set a mental limit if you're on a tight match-day schedule.\n\nWeekend Crowds: Saturdays draw a steady stream of browsers and photographers for the book tunnel specifically.",
              "localPerspective": "Despite its name, locals will tell you it's very much not the last bookstore — Angelenos treat it as a genuine cultural institution and a point of civic pride for DTLA's ongoing revival.",
              "hiddenCost": "No entry cost — browsing is free; purchases vary widely from a $5 used paperback to rarer finds.",
              "nearbyComplements": [
                "Grand Central Market: About a 10-minute walk north.",
                "Angels Flight Railway: A similar distance away.",
                "Broadway Historic Theatre District: Immediately surrounding the store."
              ],
              "bestTimeToVisit": "Early-to-Mid Afternoon (1:00 PM - 2:30 PM) as an optional detour if your schedule has room before heading to the stadium.",
              "crowdLevel": "Medium (5/10), High (7/10) on weekends.",
              "accessibility": "Rating: 6/10 — the ground floor is flat and accessible, but the upper annex with the book tunnel involves stairs.",
              "idealDuration": "20 to 40 minutes (optional stop)."
            }
            """,
            AverageCostPerDay = 5m,
            LuxuryRating = DeriveLuxury(5m, mapping["The Last Bookstore"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["The Last Bookstore"].tags),
            AdventurePaceScore = AdventureScore(mapping["The Last Bookstore"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["The Last Bookstore"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["The Last Bookstore"].tags),
            Latitude = 34.0489,
            Longitude = -118.2503,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── LA City Hall Observation Deck ──
        new Destination
        {
            DestinationId = destCityHallId,
            DestinationName = "LA City Hall Observation Deck",
            CleanNormalizedSearchName = "la city hall observation deck",
            MetaphoneCode = "L ST HL OBSRVXN TK",
            DoubleMetaphonePrimary = "L ST HL OBSRFXN TK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "LA City Hall's Art Deco tower has watched over Downtown LA since 1928 and offers one of the city's best free panoramic views from its public observation deck — a genuine hidden gem, since most visitors don't realize a government building doubles as a viewpoint. Note that access is tied to normal government building hours, which matters for weekend planning (see below).",
              "directions": "Best Access: A short walk downhill through Grand Park, or Metro B/D Line to Civic Center/Grand Park Station.\n\nAddress for Rideshare: 200 N Spring St, Los Angeles, CA 90012.",
              "whatToKnow": "Hours: City Hall is open to the public Monday through Friday, roughly 10:00 AM-5:00 PM — it is generally not open on weekends, so this stop is realistically a weekday-only option; check current hours before planning around it for a weekend match.\n\nFree Docent Tours: Available between roughly 10:00 AM-12:30 PM by reservation on weekdays, covering the building's history and architecture.\n\nSecurity: As a working government building, expect a security screening and to show ID before heading up.",
              "thingsToBeWaryOf": "Weekend Closures: If your match day falls on a Saturday or Sunday, this stop likely won't be accessible — treat it as a bonus for weekday visits rather than a guaranteed part of a weekend itinerary.\n\nSecurity Line Timing: Build in a few extra minutes for the security check, especially if there's a line.",
              "localPerspective": "The tower has appeared as a backdrop in countless films and TV shows over the decades, and many Angelenos have never actually been up to the deck themselves despite passing the building constantly — it's a genuine 'hidden in plain sight' spot.",
              "hiddenCost": "Free admission and observation deck access.\nParking (if driving): Paid public lots nearby, rates vary.",
              "nearbyComplements": [
                "Gloria Molina Grand Park: Directly adjacent, connecting up toward the Music Center.",
                "The Broad and Walt Disney Concert Hall: A short walk north through the park.",
                "Civic Center: Surrounding government and cultural buildings."
              ],
              "bestTimeToVisit": "Weekday Late Morning (10:00 AM - 12:00 PM), ideally paired with a free docent tour reservation if timing allows.",
              "crowdLevel": "Low (3/10) — genuinely underused relative to how good the view is.",
              "accessibility": "Rating: 9/10 — modern elevators and accessible entrances, standard for a major government building.",
              "idealDuration": "20 to 30 minutes (optional, weekdays only)."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["LA City Hall Observation Deck"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["LA City Hall Observation Deck"].tags),
            AdventurePaceScore = AdventureScore(mapping["LA City Hall Observation Deck"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["LA City Hall Observation Deck"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["LA City Hall Observation Deck"].tags),
            Latitude = 34.0537,
            Longitude = -118.2427,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },
    };

            db.Destinations.AddRange(newDestinations);
            await db.SaveChangesAsync();

            var allDestinations = newDestinations.ToList();

            await AssignCategoriesAndTagsToDestinationsAsync(db, allDestinations, mapping);
            await SeedImagesForLosAngelesAsync(db, allDestinations);
            await SeedDestinationLanguageAndCurrencyAsync(db, allDestinations, english, usd);

            // ─── Destination ↔ City links ─────────────────────────────────────────
            db.DestinationCities.AddRange(new[]
            {
        new DestinationCity { DestinationId = destPannsId, CityId = inglewood.CityId },
        new DestinationCity { DestinationId = destHilltopId, CityId = inglewood.CityId },
        new DestinationCity { DestinationId = destMarketStreetId, CityId = inglewood.CityId },
        new DestinationCity { DestinationId = destDulansId, CityId = inglewood.CityId },
        new DestinationCity { DestinationId = destJamzId, CityId = inglewood.CityId },
        new DestinationCity { DestinationId = destSoFiId, CityId = inglewood.CityId },
        new DestinationCity { DestinationId = destTomsWatchBarId, CityId = inglewood.CityId },
        new DestinationCity { DestinationId = destKoreatownId, CityId = losAngeles.CityId },
        new DestinationCity { DestinationId = destGrandCentralMarketId, CityId = losAngeles.CityId },
        new DestinationCity { DestinationId = destAngelsFlightId, CityId = losAngeles.CityId },
        new DestinationCity { DestinationId = destGrandParkId, CityId = losAngeles.CityId },
        new DestinationCity { DestinationId = destDisneyConcertHallId, CityId = losAngeles.CityId },
        new DestinationCity { DestinationId = destBroadId, CityId = losAngeles.CityId },
        new DestinationCity { DestinationId = destLastBookstoreId, CityId = losAngeles.CityId },
        new DestinationCity { DestinationId = destCityHallId, CityId = losAngeles.CityId },
    });
            await db.SaveChangesAsync();

            // ─── Transit Route: Downtown LA → Inglewood/SoFi Stadium ────────────
            var routeDtlaToSoFiId = Guid.NewGuid();
            db.TransitRoutes.Add(new TransitRoute
            {
                TransitRouteId = routeDtlaToSoFiId,
                OriginCityId = losAngeles.CityId,
                DestinationCityId = inglewood.CityId,
                TransitType = "Metro K Line / Match Day Direct Bus",
                EstimatedCostPerPerson = 1.75m,
                DurationInMinutes = 45,
                RecommendedTimeBufferMinutes = 45,
                BookingReferenceUrl = "https://www.metro.net/riding/world-cup/",
                CarbonFootprintKg = "0.8",
                SubSegmentsJson = "[]"
            });
            await db.SaveChangesAsync();

            // ═══════════════════════════════════════════════════════════════════
            // WISHLIST 1: Perfect SoFi Stadium Match Day (Inglewood-first)
            // ═══════════════════════════════════════════════════════════════════
            var wishlist1Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = wishlist1Id,
                WishlistName = "⚽ Perfect SoFi Stadium Match Day",
                WishlistDescription = "Spend the perfect day before and after the match without wasting time in traffic — a compact, Inglewood-first itinerary that keeps you within minutes of SoFi Stadium all day: classic diner breakfast, a stroll down Market Street, stadium plaza and fan activations before kickoff, and a post-match celebration right across the street or a bigger night out in Koreatown.",
                ShortStory = "Diner breakfast, soul food and ice cream on Market Street, the roar of SoFi Stadium, and a celebration that never asks you to fight traffic to get there.",
                TotalDays = 1,
                PeopleType = "World Cup Fans / Sports Travelers who want to stay close to the stadium",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_sofi_hero.jpg",
                GlobalInclusionsJson = @"[""Match Ticket (SoFi Stadium / Los Angeles Stadium)"",""Walking Route Map""]",
                RawContentKeywords = "Los Angeles, Inglewood, SoFi Stadium, World Cup, Market Street, Pann's, Hilltop Coffee, Dulan's Soul Food, Jamz Creamery, Tom's Watch Bar, Koreatown, match day, soccer",
                PsychologicalVibeTagsJson = @"[""Sports Fan"",""Foodie"",""Low-Traffic"",""Social""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 340m,
                CalculatedTotalCost = 680m,
                DepositAmountRequired = 50m,
                AccommodationInclusions = "Day-trip itinerary — no overnight accommodation included",
                TransitInclusions = "Designed to minimize travel: all pre-match stops are within Inglewood, walkable or a short drive from SoFi Stadium",
                ActivityInclusions = "SoFi Stadium match ticket (World Cup fixture)",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "World Cup Match Attendee (traffic-averse)",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();

            var w1Day = new ItineraryDay
            {
                ItineraryDayId = Guid.NewGuid(),
                DayNumber = 1,
                DayTitle = "Perfect SoFi Stadium Match Day",
                MorningCityId = inglewood.CityId,
                AfternoonCityId = inglewood.CityId,
                EveningCityId = inglewood.CityId,
                TransitFromPreviousDayRouteId = null,
                WishlistId = wishlist1Id
            };
            db.ItineraryDays.Add(w1Day);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Breakfast at Pann's Restaurant",
                    ItemDescription = "Classic 1958 LA diner with landmark Googie architecture, a few minutes from Inglewood. Closes at 3 PM daily, so it's built for exactly this early start.",
                    ItemOrderIndex = 1,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_panns.jpeg",
                    SocialProofBadge = "LA Landmark",
                    IndividualCostModifier = 18m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Or: Breakfast at Hilltop Coffee + Kitchen",
                    ItemDescription = "Alternative breakfast right in Downtown Inglewood — modern coffee and kitchen, walkable straight into the rest of the Market Street itinerary.",
                    ItemOrderIndex = 2,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_hilltop.jpeg",
                    SocialProofBadge = "Local Favorite",
                    IndividualCostModifier = 14m,
                    IsOptionalActivity = true,
                    IsSelectedByDefault = false
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Explore Market Street, Inglewood",
                    ItemDescription = "Stroll Downtown Inglewood's walkable main strip of local restaurants and shops.",
                    ItemOrderIndex = 3,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_marketstreet.jpeg",
                    SocialProofBadge = "Neighborhood Stroll",
                    IndividualCostModifier = 0m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Dulan's Soul Food Kitchen (if skipping breakfast / early lunch)",
                    ItemDescription = "Inglewood soul food institution — smothered chicken, pork chops, and classic sides. Expect a line, but it moves.",
                    ItemOrderIndex = 4,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_dulans.jpeg",
                    SocialProofBadge = "LA Institution",
                    IndividualCostModifier = 22m,
                    IsOptionalActivity = true,
                    IsSelectedByDefault = false
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Jamz Creamery",
                    ItemDescription = "Music-themed ice cream shop on Market Street since 2001 — try the signature dessert, THE JAZZ.",
                    ItemOrderIndex = 5,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_jamz.jpeg",
                    SocialProofBadge = "Sweet Treat",
                    IndividualCostModifier = 8m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Walk Around SoFi Stadium: Team Stores, Fan Activations & Photos",
                    ItemDescription = "Arrive early to browse official team/tournament stores, catch fan-zone activations in the plaza, and get your photos before the crowds peak.",
                    ItemOrderIndex = 6,
                    TimeOfDay = "Afternoon",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_sofiplaza.jpeg",
                    SocialProofBadge = "Must-Do",
                    IndividualCostModifier = 0m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Watch the Match",
                    ItemDescription = "Experience the World Cup live at SoFi Stadium (officially 'Los Angeles Stadium' during FIFA events).",
                    ItemOrderIndex = 7,
                    TimeOfDay = "Evening",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_sofistadium.jpeg",
                    SocialProofBadge = "World Cup",
                    IndividualCostModifier = 260m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Celebrate at Tom's Watch Bar (Inglewood)",
                    ItemDescription = "Directly across the street from SoFi Stadium — ideal if you're meeting friends or didn't have tickets. 360° screens, rooftop bar, zero traffic to get there.",
                    ItemOrderIndex = 8,
                    TimeOfDay = "Evening",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_tomswatchbar.jpeg",
                    SocialProofBadge = "No Traffic",
                    IndividualCostModifier = 45m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w1Day.ItineraryDayId,
                    ItemTitle = "Or: Head to Koreatown for Korean BBQ, Late-Night Food & Karaoke",
                    ItemDescription = "For a bigger night out: Korean BBQ, 24-hour spots, and noraebang karaoke rooms. Note this is a real trip from Inglewood (30-45+ min) — best if you're staying in or near DTLA/Koreatown that night.",
                    ItemOrderIndex = 9,
                    TimeOfDay = "Evening",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_koreatown.jpeg",
                    SocialProofBadge = "Big Night Out",
                    IndividualCostModifier = 55m,
                    IsOptionalActivity = true,
                    IsSelectedByDefault = false
                }
            );
            await db.SaveChangesAsync();

            // ═══════════════════════════════════════════════════════════════════
            // WISHLIST 2: Downtown LA Before Kickoff (DTLA-first)
            // ═══════════════════════════════════════════════════════════════════
            var wishlist2Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = wishlist2Id,
                WishlistName = "🌇 Downtown LA Before Kickoff",
                WishlistDescription = "One of the most walkable match-day itineraries in the city — almost everything sits within a few blocks in Bunker Hill and the Civic Center: a historic food hall, a one-minute funicular ride, a park, world-class architecture, free contemporary art, and an optional bookstore and city hall detour, before heading to SoFi Stadium for kickoff.",
                ShortStory = "Pupusas and coffee at a century-old market, a funicular up Bunker Hill, Gehry's curves, Kusama's mirrors, and a walk through the pages of LA's biggest bookstore — all before the roar of the World Cup.",
                TotalDays = 1,
                PeopleType = "World Cup Fans who want a culture-and-food day before the match",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_dtla_hero.jpg",
                GlobalInclusionsJson = @"[""Match Ticket (SoFi Stadium / Los Angeles Stadium)"",""Angels Flight Railway Fare"",""Walking Route Map""]",
                RawContentKeywords = "Downtown Los Angeles, DTLA, World Cup, Grand Central Market, Angels Flight, Grand Park, Walt Disney Concert Hall, The Broad, The Last Bookstore, LA City Hall, SoFi Stadium, match day, soccer",
                PsychologicalVibeTagsJson = @"[""Culture"",""Foodie"",""Walkable"",""Sports Fan""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 300m,
                CalculatedTotalCost = 600m,
                DepositAmountRequired = 50m,
                AccommodationInclusions = "Day-trip itinerary — no overnight accommodation included",
                TransitInclusions = "Almost entirely walkable within DTLA; Metro K Line or Match Day Direct bus service to SoFi Stadium for kickoff",
                ActivityInclusions = "The Broad general admission (free, advance reservation recommended), SoFi Stadium match ticket (World Cup fixture)",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "World Cup Match Attendee (culture-first)",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();

            var w2Day = new ItineraryDay
            {
                ItineraryDayId = Guid.NewGuid(),
                DayNumber = 1,
                DayTitle = "Downtown LA Before Kickoff",
                MorningCityId = losAngeles.CityId,
                AfternoonCityId = losAngeles.CityId,
                EveningCityId = inglewood.CityId,
                TransitFromPreviousDayRouteId = null,
                WishlistId = wishlist2Id
            };
            db.ItineraryDays.Add(w2Day);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "Breakfast at Grand Central Market",
                    ItemDescription = "Historic 1917 food hall — try Eggslut for a breakfast sandwich, Sarita's Pupuseria for pupusas, or G&B Coffee for espresso.",
                    ItemOrderIndex = 1,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_grandcentralmarket.jpg",
                    SocialProofBadge = "DTLA Icon",
                    IndividualCostModifier = 20m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "Ride Angels Flight Railway",
                    ItemDescription = "The world's shortest railway — a 1-minute funicular ride up Bunker Hill from right across the street from the market. $1.50 each way.",
                    ItemOrderIndex = 2,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_angelsflight.jpg",
                    SocialProofBadge = "LA Classic",
                    IndividualCostModifier = 3m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "Relax at Gloria Molina Grand Park",
                    ItemDescription = "A 12-acre green corridor connecting Bunker Hill's cultural venues down to the Civic Center — a free, easy walking breather.",
                    ItemOrderIndex = 3,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_grandpark.jpeg",
                    SocialProofBadge = "Green Space",
                    IndividualCostModifier = 0m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "Walt Disney Concert Hall Architecture",
                    ItemDescription = "Frank Gehry's stainless-steel landmark — free self-guided access to public areas and the rooftop Blue Ribbon Garden.",
                    ItemOrderIndex = 4,
                    TimeOfDay = "Afternoon",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_disneyconcerthall.jpg",
                    SocialProofBadge = "Architectural Icon",
                    IndividualCostModifier = 0m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "The Broad: Contemporary Art & Infinity Mirror Room",
                    ItemDescription = "Always-free general admission across the street from the Concert Hall. The larger Infinity Mirrored Room needs an advance reservation; the third-floor Longing for Eternity doesn't.",
                    ItemOrderIndex = 5,
                    TimeOfDay = "Afternoon",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_thebroad.jpg",
                    SocialProofBadge = "Free Admission",
                    IndividualCostModifier = 0m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "Optional: The Last Bookstore",
                    ItemDescription = "A 10-minute walk south — California's largest new-and-used bookstore, famous for its spiraling book tunnel, inside a former bank.",
                    ItemOrderIndex = 6,
                    TimeOfDay = "Afternoon",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_lastbookstore.jpeg",
                    SocialProofBadge = "Hidden Gem",
                    IndividualCostModifier = 5m,
                    IsOptionalActivity = true,
                    IsSelectedByDefault = false
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "Optional: LA City Hall Observation Deck",
                    ItemDescription = "Free panoramic views if open — note City Hall is generally weekday-only (roughly 10 AM-5 PM, Mon-Fri), so this depends on your match date.",
                    ItemOrderIndex = 7,
                    TimeOfDay = "Afternoon",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_cityhall.jpg",
                    SocialProofBadge = "Weekdays Only",
                    IndividualCostModifier = 0m,
                    IsOptionalActivity = true,
                    IsSelectedByDefault = false
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "Head to SoFi Stadium",
                    ItemDescription = "Take the Metro K Line or Match Day Direct bus service from Downtown LA — about 45 minutes, avoiding stadium parking and match-day traffic entirely.",
                    ItemOrderIndex = 8,
                    TimeOfDay = "Evening",
                    ImageUrl = "",
                    SocialProofBadge = "",
                    IndividualCostModifier = 1.75m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = w2Day.ItineraryDayId,
                    ItemTitle = "Watch the Match",
                    ItemDescription = "Experience the World Cup live at SoFi Stadium (officially 'Los Angeles Stadium' during FIFA events).",
                    ItemOrderIndex = 9,
                    TimeOfDay = "Evening",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_sofistadium.jpeg",
                    SocialProofBadge = "World Cup",
                    IndividualCostModifier = 260m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                }
            );
            await db.SaveChangesAsync();

            // ─── Wishlist ↔ Destination links (both wishlists) ─────────────────
            var wishlist1Destinations = new[] { destPannsId, destHilltopId, destMarketStreetId, destDulansId, destJamzId, destSoFiId, destTomsWatchBarId, destKoreatownId };
            var wishlist2Destinations = new[] { destGrandCentralMarketId, destAngelsFlightId, destGrandParkId, destDisneyConcertHallId, destBroadId, destLastBookstoreId, destCityHallId, destSoFiId };

            foreach (var destId in wishlist1Destinations)
            {
                bool alreadyLinked = await db.WishlistDestinations
                    .AnyAsync(wd => wd.WishlistId == wishlist1Id && wd.DestinationId == destId);
                if (!alreadyLinked)
                    db.WishlistDestinations.Add(new WishlistDestination { WishlistId = wishlist1Id, DestinationId = destId });
            }

            foreach (var destId in wishlist2Destinations)
            {
                bool alreadyLinked = await db.WishlistDestinations
                    .AnyAsync(wd => wd.WishlistId == wishlist2Id && wd.DestinationId == destId);
                if (!alreadyLinked)
                    db.WishlistDestinations.Add(new WishlistDestination { WishlistId = wishlist2Id, DestinationId = destId });
            }
            await db.SaveChangesAsync();
        }

        // ─── Local helper: images (LA-specific file names) ──────────────────────
        private static async Task SeedImagesForLosAngelesAsync(HodracDbContext db, List<Destination> destinations)
        {
            List<string> imageNames = new List<string>
    {
        "panns.jpeg",
        "hilltop.jpeg",
        "marketstreet.jpeg",
        "dulans.jpeg",
        "jamz.jpeg",
        "sofistadium.jpeg",
        "tomswatchbar.jpeg",
        "koreatown.jpeg",
        "grandcentralmarket.jpg",
        "angelsflight.jpg",
        "grandpark.jpeg",
        "disneyconcerthall.jpg",
        "thebroad.jpg",
        "lastbookstore.jpeg",
        "cityhall.jpg"
    };

            foreach (var (dest, image) in destinations.Zip(imageNames, (d, i) => (d, i)))
            {
                db.DestinationImages.Add(new DestinationImage
                {
                    DestinationImageId = Guid.NewGuid(),
                    DestinationId = dest.DestinationId,
                    ImageUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_{Uri.EscapeDataString(image)}",
                    ThumbnailUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/la_{Uri.EscapeDataString(image)}",
                    Caption = $"Hero image of {dest.DestinationName}",
                    DisplayOrder = 1,
                    ImageType = "Hero",
                    ShotContext = "Exterior",
                    IsAiGenerated = false
                });
            }

            await db.SaveChangesAsync();
        }

      
        private static async Task SeedDestinationLanguageAndCurrencyAsync(
            HodracDbContext db,
            List<Destination> destinations,
            Language language,
            Currency currency)
        {
            foreach (var dest in destinations)
            {
                bool langExists = await db.DestinationLanguages
                    .AnyAsync(dl => dl.DestinationId == dest.DestinationId && dl.LanguageId == language.LanguageId);

                if (!langExists)
                {
                    db.DestinationLanguages.Add(new DestinationLanguage
                    {
                        DestinationId = dest.DestinationId,
                        LanguageId = language.LanguageId
                    });
                }

                bool currencyExists = await db.DestinationCurrencies
                    .AnyAsync(dc => dc.DestinationId == dest.DestinationId && dc.CurrencyId == currency.CurrencyId);

                if (!currencyExists)
                {
                    db.DestinationCurrencies.Add(new DestinationCurrency
                    {
                        DestinationId = dest.DestinationId,
                        CurrencyId = currency.CurrencyId
                    });
                }
            }

            await db.SaveChangesAsync();
        }

        private static async Task AssignCategoriesAndTagsToDestinationsAsync(
       HodracDbContext db,
       List<Destination> destinations,
       Dictionary<string, (string[] categories, string[] tags)> mapping)
        {
            // --- 1. Load existing categories and tags from the DB ---
            var categoryDict = await db.Categories.ToDictionaryAsync(c => c.Key);
            var tagDict = await db.Tags.ToDictionaryAsync(t => t.Key);

            // --- 2. Check for missing categories and tags and add them ---
            var allCategoryKeys = mapping.Values.SelectMany(v => v.categories).Distinct().ToList();
            var allTagKeys = mapping.Values.SelectMany(v => v.tags).Distinct().ToList();

            var missingCategoryKeys = allCategoryKeys.Except(categoryDict.Keys).ToList();
            var missingTagKeys = allTagKeys.Except(tagDict.Keys).ToList();

            foreach (var key in missingCategoryKeys)
            {
                var newCategory = new Category
                {
                    CategoryId = Guid.NewGuid(),
                    Key = key,
                    CategoryName = key.Replace('_', ' ').ToTitleCase(),
                    CategoryDescription = "Auto‑added category",
                    IconName = "tag",
                    ColorHex = "#888888"
                };
                db.Categories.Add(newCategory);
                categoryDict[key] = newCategory;
            }

            foreach (var key in missingTagKeys)
            {
                var newTag = new Tag
                {
                    TagId = Guid.NewGuid(),
                    Key = key,
                    TagName = key.Replace('_', ' ').ToTitleCase(),
                    TargetPersonaType = "Explorer"
                };
                db.Tags.Add(newTag);
                tagDict[key] = newTag;
            }

            // --- 3. Assign categories and tags using junction tables ---
            foreach (var dest in destinations)
            {
                if (!mapping.TryGetValue(dest.DestinationName, out var entry))
                    continue;

                foreach (var catKey in entry.categories)
                {
                    if (!categoryDict.TryGetValue(catKey, out var category))
                        continue;

                    bool alreadyExists = await db.DestinationCategories
                        .AnyAsync(dc => dc.DestinationId == dest.DestinationId
                                        && dc.CategoryId == category.CategoryId);

                    if (!alreadyExists)
                    {
                        db.DestinationCategories.Add(new DestinationCategory
                        {
                            DestinationId = dest.DestinationId,
                            CategoryId = category.CategoryId
                        });
                    }
                }

                foreach (var tagKey in entry.tags)
                {
                    if (!tagDict.TryGetValue(tagKey, out var tag))
                        continue;

                    bool alreadyExists = await db.DestinationTags
                        .AnyAsync(dt => dt.DestinationId == dest.DestinationId
                                        && dt.TagId == tag.TagId);

                    if (!alreadyExists)
                    {
                        db.DestinationTags.Add(new DestinationTag
                        {
                            DestinationId = dest.DestinationId,
                            TagId = tag.TagId
                        });
                    }
                }
            }

            await db.SaveChangesAsync();
        }

    }
}
