using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Seeder;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    public class SeattleSeeder
    {
      
        public static async Task SeedSeattleMatchDay(HodracDbContext db)
        {
            // ─── Lookups ──────────────────────────────────────────────────────────
            var english = await db.Languages.FirstAsync(l => l.LanguageName == "English");
            var usd = await db.Currencies.FirstAsync(c => c.CurrencyCode == "USD");

            var usa = new Country
            {
                CountryId = Guid.NewGuid(),
                CountryName = "United States of America",
                Continent = "North America",
                CountryFlagEmoji = "🇺🇸",
                GlobalHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/washingtondc.jpg",
                VisaRequirementsSummary = "Visa required for most nationalities",
                PowerPlugType = "Type A/B",
                DrivingSide = "Right",
                EstimatedDailyTaxRate = 0.10m,
            };
            usa.CountryLanguages = new List<CountryLanguage>
        {
            new() { CountryId = usa.CountryId, LanguageId = english.LanguageId }
        };

            db.Countries.Add(usa);
            await db.SaveChangesAsync();

            var seattle = await db.Cities.FirstOrDefaultAsync(c => c.CityName == "Seattle");
            if (seattle == null)
            {
                seattle = new City
                {
                    CityId = Guid.NewGuid(),
                    CityName = "Seattle",
                    CountryId = usa.CountryId,
                    Latitude = 47.6062,
                    Longitude = -122.3321,
                    CityDescription = "Pacific Northwest port city known for its waterfront, coffee culture, and passionate sports fanbase."
                };
                db.Cities.Add(seattle);
                await db.SaveChangesAsync();
            }

            var mapping = new Dictionary<string, (string[] categories, string[] tags)>
            {
                ["Pike Place Market"] = (
                    categories: new[] { "market_street_life", "food_experience", "viewpoint_scenic_spot" },
                    tags: new[] { "food_focused", "walkable", "tourist_hotspot", "photography", "local_favorite" }
                ),
                ["Beecher's Handmade Cheese"] = (
                    categories: new[] { "food_experience" },
                    tags: new[] { "food_focused", "tourist_hotspot", "local_favorite", "budget_friendly" }
                ),
                ["Seattle Waterfront"] = (
                    categories: new[] { "viewpoint_scenic_spot", "nature_outdoor" },
                    tags: new[] { "photography", "walkable", "family_friendly", "relaxing", "tourist_hotspot" }
                ),
                ["Pioneer Square"] = (
                    categories: new[] { "neighborhood_district", "historical_tour", "landmark_monument" },
                    tags: new[] { "walkable", "history", "architecture", "cultural", "photography" }
                ),
                ["Seattle Underground Tour"] = (
                    categories: new[] { "historical_tour", "activity_experience" },
                    tags: new[] { "educational", "history", "hidden_gem", "quirky" }
                ),
                ["Lumen Field"] = (
                    categories: new[] { "activity_experience", "entertainment_nightlife" },
                    tags: new[] { "sports_fan", "crowded", "premium", "social", "tourist_hotspot" }
                ),
                ["Pioneer Square Pubs"] = (
                    categories: new[] { "entertainment_nightlife", "food_experience" },
                    tags: new[] { "nightlife", "social", "sports_fan", "local_favorite", "crowded" }
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
            var destPikePlaceId = Guid.NewGuid();
            var destBeechersId = Guid.NewGuid();
            var destWaterfrontId = Guid.NewGuid();
            var destPioneerSquareId = Guid.NewGuid();
            var destUndergroundTourId = Guid.NewGuid();
            var destLumenFieldId = Guid.NewGuid();
            var destPioneerPubsId = Guid.NewGuid();

            // ─── Destinations with full JSON descriptions ──────────────────────
            var newDestinations = new[]
            {
        // ── Pike Place Market ──
        new Destination
        {
            DestinationId = destPikePlaceId,
            DestinationName = "Pike Place Market",
            CleanNormalizedSearchName = "pike place market",
            MetaphoneCode = "PK PLS MRKT",
            DoubleMetaphonePrimary = "PK PLS MRKT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Pike Place Market is the beating heart of Seattle — a sprawling, century-old public market perched above the waterfront. It's equal parts farmers market, fishmonger theater, craft bazaar, and breakfast spot. The fishmongers famously toss whole salmon over the counter for the crowd, the original Starbucks draws a permanent line around the block, and the lower levels hide a maze of vintage shops, comic stores, and the infamous gum wall. On a match day morning, it's the perfect low-key, high-energy way to start a big day in the city.\n\nWhat to Do: Watch the fish-throwing show at Pike Place Fish Market. Grab a pastry or coffee and sit by the market's western overlook for a water view. Browse the flower stalls and farm produce. Duck downstairs into the Down Under level for oddities and record shops. Snap a photo at the original Starbucks storefront (skip the line — there's a to-go window around the corner most mornings).",
              "directions": "Best Access: Downtown Seattle, corner of Pike Street and 1st Avenue.\n\nKey Entrance: The main entrance arch is at Pike Street & Pike Place, right across from the giant neon 'Public Market Center' clock and sign.\n\nThe Direction Walk: From most downtown hotels, it's a 5-15 minute walk east along Pike or Pine Street, downhill toward the water. From the waterfront, look for the Pike Street Hillclimb stairs leading straight up into the market.\n\nAddress for Rideshare: 85 Pike St, Seattle, WA 98101.",
              "whatToKnow": "Fish Throwing Show: Pike Place Fish Market puts on its famous salmon-tossing performance every 10-15 minutes when it's busy — just wait near the counter and a crowd will gather naturally.\n\nOriginal Starbucks: The very first Starbucks (opened 1971) is on the corner. Expect a line most of the day; the coffee itself is the same as any other location, so many locals skip it and hit a market café instead.\n\nLayout: The market has multiple levels connected by ramps and stairs — the main arcade (produce, flowers, crafts) is at street level, with the Down Under levels below housing smaller specialty shops.",
              "thingsToBeWaryOf": "Pickpockets: Like any dense tourist crowd, keep bags zipped and phones in front pockets, especially near the main arcade and Starbucks line.\n\nSlippery Floors: The fish and produce stalls mean wet, sometimes fish-scented floors — watch your step, especially in dress shoes on match day.\n\nCrowds on Weekends: Saturdays are packed shoulder-to-shoulder by mid-morning. If your match is on a weekend, an early breakfast (before 9:30 AM) is far more pleasant.",
              "localPerspective": "The Real Market: Locals tend to visit early (7-9 AM) before the tour buses arrive, buying fresh flowers and produce for the week rather than souvenirs.\n\nThe Gum Wall: Down in Post Alley, a wall completely covered in used chewing gum has become a bizarre local landmark — visitors are welcome (encouraged, even) to add their own piece.\n\nFirst-in-Line Culture: Regulars know which bakery stalls sell out of the best pastries by 10 AM, so early risers get first pick.",
              "hiddenCost": "Coffee: $4-$7 for espresso drinks.\nPastries: $4-$8 at bakery stalls like Three Girls Bakery or Piroshky Piroshky.\nBreakfast Sandwich/Bowl: $10-$16 at market cafés.\nFresh Flowers: $10-$20 for a bouquet.\nSouvenirs: $5-$30 for market-branded merchandise.",
              "nearbyComplements": [
                "Seattle Waterfront: A 5-minute walk down the Pike Street Hillclimb.",
                "Beecher's Handmade Cheese: Directly across the street from the market's main arcade.",
                "Post Alley: A narrow, atmospheric alley with the gum wall and small boutique shops."
              ],
              "bestTimeToVisit": "Early Morning (7:00 AM - 9:30 AM) — freshest produce, thinnest crowds, best light for photos.\nMatch Day Mornings — arrive early to grab breakfast and still have plenty of time before kickoff.",
              "crowdLevel": "High (7/10) by mid-morning, Maximum (10/10) on weekends after 10 AM.",
              "accessibility": "Rating: 7/10\n\nThe main arcade is flat and wheelchair-accessible via ramps, but the Down Under levels and Post Alley involve stairs and narrow passages. The Pike Street Hillclimb down to the waterfront has stairs, though an elevator alternative exists nearby.",
              "idealDuration": "45 minutes to 1.5 hours — enough time for breakfast, the fish-throwing show, and a browse through the main arcade."
            }
            """,
            AverageCostPerDay = 15m,
            LuxuryRating = DeriveLuxury(15m, mapping["Pike Place Market"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Pike Place Market"].tags),
            AdventurePaceScore = AdventureScore(mapping["Pike Place Market"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Pike Place Market"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Pike Place Market"].tags),
            Latitude = 47.6097,
            Longitude = -122.3422,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Beecher's Handmade Cheese ──
        new Destination
        {
            DestinationId = destBeechersId,
            DestinationName = "Beecher's Handmade Cheese",
            CleanNormalizedSearchName = "beechers handmade cheese",
            MetaphoneCode = "BXRS HNTMT XS",
            DoubleMetaphonePrimary = "PXRS HNTMT XS",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Right in the heart of the market, Beecher's is a working cheese-making shop with floor-to-ceiling windows where you can watch the entire process — from vats of milk to finished wheels — happening live behind the counter. It's most famous for its 'World's Best Mac and Cheese' (a genuine award-winner) and its signature Flagship cheddar. It's a quick, satisfying stop that pairs perfectly with a Pike Place breakfast.",
              "directions": "Best Access: Inside the Pike Place Market complex, on Pike Place street itself, directly across from the market's main arcade entrance.\n\nThe Direction Walk: From the market's main arch, cross Pike Place street — Beecher's storefront windows are impossible to miss, with cheesemakers visible at work.\n\nAddress for Rideshare: 1600 Pike Pl, Seattle, WA 98101.",
              "whatToKnow": "Live Cheese-Making: Cheesemakers work most mornings and early afternoons — check the front windows for the day's schedule.\n\nMac and Cheese: The mac and cheese is the signature order and can get a genuine line at peak lunch hours; mid-morning is much quicker.\n\nCheese Curds: Fresh, squeaky cheese curds are sold by the cup and are a popular grab-and-go snack.",
              "thingsToBeWaryOf": "Small Seating Area: Indoor seating is limited — many people eat standing at the window counter or take food outside to the market's overlook.\n\nCash/Card Lines: At peak breakfast rush, expect a short wait; ordering ahead via their app can help if your schedule is tight before kickoff.",
              "localPerspective": "Seattleites treat Beecher's as a genuine grocery stop, not just a tourist novelty — many buy blocks of Flagship cheddar to take home for the week.\n\nThe mac and cheese recipe is closely guarded, and regulars debate whether the small or large cup is the better value (large, according to most).",
              "hiddenCost": "Mac and Cheese Cup: $6-$10.\nCheese Curds: $5-$7.\nCheese by the Pound: $12-$20.\nGrilled Cheese Sandwich: $8-$12.",
              "nearbyComplements": [
                "Pike Place Market Main Arcade: Directly adjacent.",
                "Post Alley: A 2-minute walk for the gum wall and boutique shops.",
                "Seattle Waterfront: A short downhill walk via the Pike Street Hillclimb."
              ],
              "bestTimeToVisit": "Mid-Morning (9:30 AM - 11:00 AM) — cheese-making is usually in progress and lines are shorter than at lunch.",
              "crowdLevel": "Medium (5/10), rising to High (7/10) around lunchtime.",
              "accessibility": "Rating: 9/10 — flat, street-level storefront with easy access.",
              "idealDuration": "15 to 25 minutes."
            }
            """,
            AverageCostPerDay = 8m,
            LuxuryRating = DeriveLuxury(8m, mapping["Beecher's Handmade Cheese"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Beecher's Handmade Cheese"].tags),
            AdventurePaceScore = AdventureScore(mapping["Beecher's Handmade Cheese"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Beecher's Handmade Cheese"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Beecher's Handmade Cheese"].tags),
            Latitude = 47.6093,
            Longitude = -122.3417,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Seattle Waterfront ──
        new Destination
        {
            DestinationId = destWaterfrontId,
            DestinationName = "Seattle Waterfront",
            CleanNormalizedSearchName = "seattle waterfront",
            MetaphoneCode = "STL WTRFRNT",
            DoubleMetaphonePrimary = "STL WTRFRNT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Seattle's Elliott Bay waterfront stretches along the edge of downtown, offering open views of the harbor, ferries gliding to Bainbridge Island, and — on a clear day — the Olympic Mountains across the water. Recently redeveloped, the promenade now includes the Seattle Great Wheel (a 175-foot Ferris wheel), the aquarium, ferry terminals, and a string of seafood shacks. It's a breezy, scenic reset between the market crowds and the walk toward the stadiums.",
              "directions": "Best Access: Walk down the Pike Street Hillclimb (stairs) from Pike Place Market, or take the elevator inside the market's west side.\n\nThe Direction Walk: From the base of the Hillclimb, you're directly on Alaskan Way — turn right (south) to head toward the Seattle Great Wheel and eventually Pioneer Square.\n\nAddress for Rideshare: Alaskan Way & Pike St, Seattle, WA 98101 (Pier 57 area).",
              "whatToKnow": "Seattle Great Wheel: A 12-minute ride offering great skyline and mountain views on clear days — climate-controlled gondolas make it comfortable year-round.\n\nFerry Views: Washington State Ferries depart regularly from the nearby Colman Dock — watching one pull out is a classic only-in-Seattle moment.\n\nWalking Route: The waterfront promenade runs roughly parallel to your route toward Pioneer Square, so you don't need to backtrack — it naturally connects the two stops.",
              "thingsToBeWaryOf": "Wind and Weather: The waterfront is exposed to Puget Sound breezes — bring a light jacket even on a sunny day.\n\nSeagulls: Aggressive around anyone eating outdoors, especially near the fish-and-chips shacks — keep an eye on your food.\n\nConstruction Zones: Parts of the waterfront have been under active redevelopment in recent years; some sections may have temporary detours.",
              "localPerspective": "Locals use the waterfront mostly as a through-route or a spot to bring out-of-town guests rather than a lingering destination — the real draw is the view and the walk itself.\n\nThe original Ivar's seafood bar near the piers is a Seattle institution, famous for its clam chowder and its long-running 'Keep Clam' slogan on local billboards.",
              "hiddenCost": "Seattle Great Wheel: $18-$25 per person.\nFish and Chips: $12-$18 at waterfront seafood shacks.\nFerry Ride (round trip, walk-on): $10-$15 if you want to extend the water views.\nSouvenir Photo/Trinkets: $5-$15.",
              "nearbyComplements": [
                "Pike Place Market: A 5-minute walk up the Hillclimb.",
                "Pioneer Square: A 10-15 minute walk south along Alaskan Way.",
                "Seattle Aquarium: Right on the waterfront promenade."
              ],
              "bestTimeToVisit": "Late Morning (10:00 AM - 12:00 PM) — good light, moderate crowds, and enough time to keep moving toward Pioneer Square before the match.",
              "crowdLevel": "Medium (5/10), higher near the Great Wheel on weekends.",
              "accessibility": "Rating: 9/10 — the promenade is flat, wide, and wheelchair/stroller friendly. An elevator connects the market level down to the waterfront for those avoiding stairs.",
              "idealDuration": "30 to 45 minutes for a walk-through; add 20 minutes if riding the Great Wheel."
            }
            """,
            AverageCostPerDay = 10m,
            LuxuryRating = DeriveLuxury(10m, mapping["Seattle Waterfront"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Seattle Waterfront"].tags),
            AdventurePaceScore = AdventureScore(mapping["Seattle Waterfront"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Seattle Waterfront"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Seattle Waterfront"].tags),
            Latitude = 47.6062,
            Longitude = -122.3435,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Pioneer Square ──
        new Destination
        {
            DestinationId = destPioneerSquareId,
            DestinationName = "Pioneer Square",
            CleanNormalizedSearchName = "pioneer square",
            MetaphoneCode = "PNR SKR",
            DoubleMetaphonePrimary = "PNR SKR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Pioneer Square is Seattle's original downtown core — a National Historic District of Romanesque red-brick buildings, cobblestone-lined streets, and cast-iron pergolas dating back to the city's 1889 rebuild after the Great Seattle Fire. Today it's a mix of art galleries, historic saloons, and — critically for match day — the closest walkable neighborhood to Lumen Field, making it the default staging ground for pre- and post-game gatherings.",
              "directions": "Best Access: Walk south along Alaskan Way from the waterfront, then east on Yesler Way, or take the walk directly down 1st Avenue S from downtown.\n\nThe Direction Walk: From the waterfront, cut inland at Yesler Way and walk about 3 blocks east — you'll know you've arrived when the buildings turn to red brick and you see the iconic pergola at Pioneer Place Park.\n\nAddress for Rideshare: Occidental Ave S & S Main St, Seattle, WA 98104 (Occidental Square).",
              "whatToKnow": "Pioneer Place Park: Home to the historic iron-and-glass pergola (built 1909) and a totem pole — the unofficial photo-op center of the neighborhood.\n\nOccidental Square: A pedestrian-only cobblestone plaza lined with galleries and bars, often used for pre-match gatherings and public viewing events on big game days.\n\nArt Walk Culture: The neighborhood hosts a monthly First Thursday Art Walk with galleries open late — worth knowing if your trip lines up with one.",
              "thingsToBeWaryOf": "Uneven Cobblestones: Some streets and Occidental Square itself have brick and cobblestone paving — flat, sturdy shoes are a good idea, especially before hours of standing at a match.\n\nMatch Day Crowds: On game days the neighborhood fills up fast with fans; if you want a table at a specific pub, arrive well before kickoff.\n\nDaytime vs. Nighttime: Like many historic downtown districts, the vibe shifts after dark on non-event nights — on a match day, though, the crowds and energy make it lively and safe well into the evening.",
              "localPerspective": "Sports Culture Hub: Pioneer Square has been Seattle's unofficial game-day headquarters for decades, thanks to its proximity to Lumen Field — locals call the pre-game gathering here 'the march' when Sounders FC plays.\n\nHistoric Rebuild: The entire neighborhood sits about a story above the original 1880s street level, which is exactly what the Underground Tour reveals.",
              "hiddenCost": "Coffee/Snack: $5-$10.\nGallery Browsing: Free.\nPre-Game Pint: $7-$10 (before happy hour ends and match-day pricing kicks in).",
              "nearbyComplements": [
                "Seattle Underground Tour: Starts right in Pioneer Square, at Doc Maynard's Public House.",
                "Lumen Field: A 10-15 minute walk south.",
                "Seattle Waterfront: A 10-minute walk northwest."
              ],
              "bestTimeToVisit": "Early Afternoon (12:00 PM - 2:00 PM) on match day — timed to fit a walk-through, the Underground Tour, and still leave a buffer before heading to the stadium.",
              "crowdLevel": "Medium (5/10) normally, Maximum (10/10) on major match days.",
              "accessibility": "Rating: 7/10 — mostly flat, but cobblestone sections in Occidental Square can be tricky for wheelchairs and strollers.",
              "idealDuration": "45 minutes to 1 hour for a walk-through (excluding the Underground Tour)."
            }
            """,
            AverageCostPerDay = 12m,
            LuxuryRating = DeriveLuxury(12m, mapping["Pioneer Square"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Pioneer Square"].tags),
            AdventurePaceScore = AdventureScore(mapping["Pioneer Square"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Pioneer Square"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Pioneer Square"].tags),
            Latitude = 47.6015,
            Longitude = -122.3327,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Seattle Underground Tour ──
        new Destination
        {
            DestinationId = destUndergroundTourId,
            DestinationName = "Seattle Underground Tour",
            CleanNormalizedSearchName = "seattle underground tour",
            MetaphoneCode = "STL UNTRKRNT TR",
            DoubleMetaphonePrimary = "STL UNTRKRNT TR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "After the Great Seattle Fire of 1889, the city rebuilt Pioneer Square's streets a full story higher than the original ground level — leaving the old storefronts, sidewalks, and passageways buried underneath the modern streets. The Underground Tour takes you below the current sidewalks into these preserved subterranean corridors, combining genuine local history with a good dose of dark humor from the guides. It's one of Seattle's most distinctive, only-here experiences and a great mid-day activity that's fully indoors if the weather turns.",
              "directions": "Best Access: Tours depart from Doc Maynard's Public House in Pioneer Square.\n\nThe Direction Walk: Right in the heart of Pioneer Square — look for the tour's ticket office and starting point on 1st Avenue S near Yesler Way.\n\nAddress for Rideshare: 608 1st Ave, Seattle, WA 98104.",
              "whatToKnow": "Book Ahead: Tours run on a set schedule and can sell out on weekends and event days — booking online in advance is strongly recommended, especially with a match-day time crunch.\n\nTour Length: Standard tours run about 75 minutes and involve walking on uneven, sometimes low-clearance underground surfaces.\n\nGuide Style: The tours lean into dry, irreverent humor alongside real history — expect laughs, not just a dry lecture.",
              "thingsToBeWaryOf": "Tight Timing: With an average 75-minute runtime, factor this carefully into your match-day schedule — don't book a start time that leaves you cutting it close to kickoff.\n\nLow Clearance & Stairs: Some underground sections have low ceilings and uneven steps — not ideal for claustrophobia or mobility limitations.\n\nNot Wheelchair Accessible: Much of the underground portion is not accessible to wheelchairs; ask about the above-ground alternative options if needed.",
              "localPerspective": "This is one of the few tours locals genuinely recommend to visiting friends and family — it's regularly cited as one of the best 'weird but essential' things to do in the city.\n\nThe tour also touches on Seattle's Gold Rush-era history as a supply hub for prospectors heading to the Klondike, a period that shaped much of Pioneer Square's early wealth.",
              "hiddenCost": "Adult Ticket: $25-$30.\nChild Ticket: $15-$20.\nSouvenir Photos: $10-$15 if purchased on-site.",
              "nearbyComplements": [
                "Pioneer Square: The tour starts and ends here.",
                "Occidental Square: A 3-minute walk for a coffee or snack before or after.",
                "Klondike Gold Rush National Historical Park visitor center: A few blocks away, free entry."
              ],
              "bestTimeToVisit": "Early-to-Mid Afternoon on match day — book the earliest slot that fits your morning market visit, leaving a comfortable buffer before heading to Lumen Field.",
              "crowdLevel": "Medium (5/10) — capped by tour group size, so it never feels overcrowded once you're on the tour itself.",
              "accessibility": "Rating: 3/10 — narrow underground passageways, stairs, and low clearance make this a poor fit for wheelchairs or significant mobility limitations.",
              "idealDuration": "75 to 90 minutes including check-in."
            }
            """,
            AverageCostPerDay = 28m,
            LuxuryRating = DeriveLuxury(28m, mapping["Seattle Underground Tour"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Seattle Underground Tour"].tags),
            AdventurePaceScore = AdventureScore(mapping["Seattle Underground Tour"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Seattle Underground Tour"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Seattle Underground Tour"].tags),
            Latitude = 47.6017,
            Longitude = -122.3331,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Lumen Field ──
        new Destination
        {
            DestinationId = destLumenFieldId,
            DestinationName = "Lumen Field",
            CleanNormalizedSearchName = "lumen field",
            MetaphoneCode = "LMN FLT",
            DoubleMetaphonePrimary = "LMN FLT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Lumen Field is Seattle's open-air stadium on the southern edge of downtown, home to the Seahawks (NFL) and Sounders FC, and one of the host venues for major international soccer matches, including World Cup fixtures. Known for having one of the loudest crowds in world sports thanks to its partially-covered bowl design that traps and amplifies noise, it's a genuinely electric place to watch a match. The surrounding plaza (Lumen Field Event Center and Compass Health stadium neighbor) buzzes with fan zones and vendors for hours before kickoff.",
              "directions": "Best Access: Walk south from Pioneer Square along 1st Avenue S or Occidental Avenue S — about 10-15 minutes on foot.\n\nBy Transit: Link light rail's Stadium Station drops you right at the north plaza entrance — a good option if you want to avoid the post-match walking crowd crush.\n\nAddress for Rideshare: 800 Occidental Ave S, Seattle, WA 98134 (note: rideshare drop-off/pickup zones shift significantly on event days — check the stadium's event-day traffic map).",
              "whatToKnow": "Bag Policy: Clear-bag policies are strictly enforced at major events — check current size and style restrictions before you head over, as this changes for high-security matches like World Cup fixtures.\n\nGates and Security: Gates typically open 1.5-2 hours before kickoff; security lines move faster earlier, so arriving with plenty of buffer is worth it for a match of this scale.\n\nFan Zones: Pre-match fan festivals and viewing zones often pop up in the plaza and nearby parking lots with food trucks, music, and giant screens.",
              "thingsToBeWaryOf": "Sell-Out Crowds: World Cup matches will be at or near capacity — expect long security lines, packed concourses, and limited concession availability during peak pre-match and halftime windows.\n\nWeather: The stadium is open-air with a partial roof — check forecasts and dress accordingly, as Seattle can shift from sun to drizzle within a match.\n\nPost-Match Exit Crush: Leaving the stadium after a sold-out match takes real time — budget 30-45 minutes just to clear the plaza before you can comfortably walk anywhere.",
              "localPerspective": "The 'Twelfth Man': Seattle's fanbase is famous for its noise — the stadium's roof structure is specifically engineered to reflect crowd sound back onto the field, and past matches have registered seismic-level activity from fan noise and jumping.\n\nSounders Match-Day Tradition: For Sounders games, supporters groups host a 'March to the Match' from Occidental Square through Pioneer Square to the stadium — expect a similar high-energy atmosphere for major international fixtures.",
              "hiddenCost": "Match Ticket: Highly variable — official resale/market pricing for World Cup fixtures can range from $150 to $1,000+ depending on the match and seating tier.\nConcessions: $8-$15 for food, $10-$16 for beer.\nOfficial Merchandise: $30-$120.\nClear Bag (if needed): $10-$20 if you need to buy a compliant bag on-site.",
              "nearbyComplements": [
                "Pioneer Square: A 10-15 minute walk north, the natural pre/post-match gathering spot.",
                "T-Mobile Park: Right next door, home of the Mariners — worth a glance even if you're not attending a game there.",
                "Stadium Station (Link Light Rail): Direct transit access to downtown and beyond."
              ],
              "bestTimeToVisit": "Arrive 90 minutes to 2 hours before kickoff for a major match — enough time to clear security, soak in the fan-zone atmosphere, and find your seat without rushing.",
              "crowdLevel": "Maximum (10/10) for a World Cup match — expect a full sell-out atmosphere.",
              "accessibility": "Rating: 9/10 — the stadium and surrounding plaza are fully ADA-compliant with dedicated accessible seating, elevators, and wide concourses.",
              "idealDuration": "3.5 to 4.5 hours total, including pre-match arrival, the full match, and a brief post-match exit window."
            }
            """,
            AverageCostPerDay = 250m,
            LuxuryRating = DeriveLuxury(250m, mapping["Lumen Field"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Lumen Field"].tags),
            AdventurePaceScore = AdventureScore(mapping["Lumen Field"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Lumen Field"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Lumen Field"].tags),
            Latitude = 47.5952,
            Longitude = -122.3316,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Pioneer Square Pubs ──
        new Destination
        {
            DestinationId = destPioneerPubsId,
            DestinationName = "Pioneer Square Pubs",
            CleanNormalizedSearchName = "pioneer square pubs",
            MetaphoneCode = "PNR SKR PBS",
            DoubleMetaphonePrimary = "PNR SKR PPS",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Once the final whistle blows, Pioneer Square's dense cluster of historic bars and sports pubs becomes the default place to keep the energy going. Brick-lined taverns, some dating back over a century, spill out onto Occidental Square and the surrounding streets, packed with fans in kit still buzzing from the match. It's loud, celebratory, and distinctly Seattle — craft beer taps everywhere alongside the usual match-day chants.",
              "directions": "Best Access: Walk back north from Lumen Field the same way you came — 10-15 minutes to Occidental Square and the surrounding pub streets.\n\nThe Direction Walk: Occidental Square and 1st Avenue S between Yesler Way and S Main St are the densest concentration of bars — just follow the crowd noise after a match.\n\nAddress for Rideshare: Occidental Ave S & S Main St, Seattle, WA 98104.",
              "whatToKnow": "Post-Match Rush: Expect every pub in the neighborhood to fill up fast right after the final whistle — if you want a specific spot, consider sending someone ahead or having a backup in mind.\n\nCraft Beer Scene: Seattle's pub culture leans heavily into Pacific Northwest craft breweries — most pubs have rotating local taps alongside the standard lagers.\n\nOutdoor Seating: Several bars have patio or sidewalk seating around Occidental Square, which becomes an informal open-air celebration zone on big match days.",
              "thingsToBeWaryOf": "Long Waits: Popular spots can have 30+ minute waits right after a match lets out — patience (or a reservation where possible) helps.\n\nSurge Pricing on Rideshare: If you're not staying downtown, expect elevated rideshare prices for an hour or two after the match as thousands of fans disperse at once.\n\nNoise Level: These pubs get genuinely loud on match nights — plan accordingly if you're looking for a quiet conversation.",
              "localPerspective": "This is where Seattle's sports culture is most visible — regulars will tell you the neighborhood has been the city's default post-game gathering spot since long before Lumen Field existed in its current form.\n\nSeveral pubs display decades of local sports memorabilia, giving the celebration a genuine sense of place rather than a generic sports-bar feel.",
              "hiddenCost": "Beer: $7-$12 per pint (match-day pricing may run higher).\nBar Food: $12-$22 per dish.\nCover Charge: $0-$15 at a few pubs with live music or DJ sets on major event nights.",
              "nearbyComplements": [
                "Lumen Field: A 10-15 minute walk south.",
                "Occidental Square: The central gathering plaza itself.",
                "Seattle Underground Tour starting point: Right in the same few blocks."
              ],
              "bestTimeToVisit": "Immediately after the match through the evening — the celebratory energy is at its peak in the first 2-3 hours post-final-whistle.",
              "crowdLevel": "Maximum (10/10) immediately post-match, easing to High (7/10) by late evening.",
              "accessibility": "Rating: 6/10 — mostly flat sidewalks, but some historic pub interiors have narrow doorways or a few steps at the entrance.",
              "idealDuration": "2 to 3 hours to properly celebrate."
            }
            """,
            AverageCostPerDay = 45m,
            LuxuryRating = DeriveLuxury(45m, mapping["Pioneer Square Pubs"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Pioneer Square Pubs"].tags),
            AdventurePaceScore = AdventureScore(mapping["Pioneer Square Pubs"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Pioneer Square Pubs"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Pioneer Square Pubs"].tags),
            Latitude = 47.6017,
            Longitude = -122.3330,
            SearchHitCount = 0,
            TimeZone = "Pacific Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },
    };

            db.Destinations.AddRange(newDestinations);
            await db.SaveChangesAsync();

            var allDestinations = newDestinations.ToList();

            // Reuses the private helpers already defined in DataSeeder from the Japan trip seeding
            await AssignCategoriesAndTagsToDestinationsAsync(db, allDestinations, mapping);
            await SeedImagesForSeattleAsync(db, allDestinations);
            await SeedDestinationLanguageAndCurrencyAsync(db, allDestinations, english, usd);

            // ─── Destination ↔ City links ─────────────────────────────────────────
            var destCityLinks = new[]
            {
        new DestinationCity { DestinationId = destPikePlaceId, CityId = seattle.CityId },
        new DestinationCity { DestinationId = destBeechersId, CityId = seattle.CityId },
        new DestinationCity { DestinationId = destWaterfrontId, CityId = seattle.CityId },
        new DestinationCity { DestinationId = destPioneerSquareId, CityId = seattle.CityId },
        new DestinationCity { DestinationId = destUndergroundTourId, CityId = seattle.CityId },
        new DestinationCity { DestinationId = destLumenFieldId, CityId = seattle.CityId },
        new DestinationCity { DestinationId = destPioneerPubsId, CityId = seattle.CityId },
    };
            db.DestinationCities.AddRange(destCityLinks);
            await db.SaveChangesAsync();

            // ─── Wishlist ─────────────────────────────────────────────────────────
            var wishlistId = Guid.NewGuid();

            var wishlist = new Wishlist
            {
                WishlistId = wishlistId,
                WishlistName = "Match Day in Seattle (Most Valuable)",
                WishlistDescription = "A 6-8 hour World Cup match day, built around Seattle's icons: Pike Place breakfast, Beecher's mac and cheese, the waterfront, a dive into the Seattle Underground, and a walk to Lumen Field for the match — ending with the city's loudest celebration in the Pioneer Square pubs.",
                ShortStory = "Market mornings, buried history at noon, and 68,000 voices roaring by kickoff — a full day of Seattle before the final whistle sends everyone to the pubs.",
                TotalDays = 1,
                PeopleType = "World Cup Fans / Sports Travelers",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_hero.jpg",
                GlobalInclusionsJson = @"[""Seattle Underground Tour Ticket"",""Match Ticket (Lumen Field)"",""Walking Route Map""]",
                RawContentKeywords = "Seattle, World Cup, Lumen Field, Pike Place Market, Beecher's Cheese, Seattle Waterfront, Pioneer Square, Seattle Underground Tour, match day, soccer",
                PsychologicalVibeTagsJson = @"[""Sports Fan"",""Foodie"",""Culture"",""Social""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 320m,
                CalculatedTotalCost = 640m,
                DepositAmountRequired = 50m,
                AccommodationInclusions = "Day-trip itinerary — no overnight accommodation included",
                TransitInclusions = "All stops are walkable in downtown Seattle; Link light rail available as an alternative for the Lumen Field leg",
                ActivityInclusions = "Seattle Underground Tour ticket, Lumen Field match ticket (World Cup fixture)",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "World Cup Match Attendee",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            };
            db.Wishlists.Add(wishlist);
            await db.SaveChangesAsync();

            // ─── Itinerary Day & Items ──────────────────────────────────────────
            var day1 = new ItineraryDay
            {
                ItineraryDayId = Guid.NewGuid(),
                DayNumber = 1,
                DayTitle = "Match Day in Seattle (Most Valuable)",
                MorningCityId = seattle.CityId,
                AfternoonCityId = seattle.CityId,
                EveningCityId = seattle.CityId,
                TransitFromPreviousDayRouteId = null,
                WishlistId = wishlistId
            };
            db.ItineraryDays.Add(day1);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day1.ItineraryDayId,
                    ItemTitle = "Breakfast at Pike Place Market",
                    ItemDescription = "Start the day with coffee, pastries, and the famous fish-throwing show at the market that put Seattle on the map.",
                    ItemOrderIndex = 1,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_pikeplace.jpg",
                    SocialProofBadge = "Iconic Spot",
                    IndividualCostModifier = 15m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day1.ItineraryDayId,
                    ItemTitle = "Eat Beecher's Cheese",
                    ItemDescription = "Watch the cheesemakers at work and grab the award-winning mac and cheese, right across from the market's main arcade.",
                    ItemOrderIndex = 2,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_breecherscheese.jpeg",
                    SocialProofBadge = "Local Favorite",
                    IndividualCostModifier = 8m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day1.ItineraryDayId,
                    ItemTitle = "Seattle Waterfront",
                    ItemDescription = "Walk down to Elliott Bay for harbor views, ferries, and a breezy reset before the afternoon's history dive.",
                    ItemOrderIndex = 3,
                    TimeOfDay = "Morning",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_waterfront.jpg",
                    SocialProofBadge = "Scenic Spot",
                    IndividualCostModifier = 0,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day1.ItineraryDayId,
                    ItemTitle = "Walk to Pioneer Square",
                    ItemDescription = "A scenic 10-15 minute stroll from the waterfront into Seattle's historic red-brick district.",
                    ItemOrderIndex = 4,
                    TimeOfDay = "Afternoon",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_pioneersquare.jpeg",
                    SocialProofBadge = "Historic District",
                    IndividualCostModifier = 0,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day1.ItineraryDayId,
                    ItemTitle = "Seattle Underground Tour",
                    ItemDescription = "Descend below the modern sidewalks into the city's buried original storefronts — history with a side of dark humor.",
                    ItemOrderIndex = 5,
                    TimeOfDay = "Afternoon",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_underground.jpeg",
                    SocialProofBadge = "Only in Seattle",
                    IndividualCostModifier = 28m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day1.ItineraryDayId,
                    ItemTitle = "Head to Lumen Field",
                    ItemDescription = "Walk (or take Link light rail) south from Pioneer Square, arriving 90 minutes to 2 hours before kickoff for security and fan-zone time.",
                    ItemOrderIndex = 6,
                    TimeOfDay = "Afternoon",
                    ImageUrl = "",
                    SocialProofBadge = "",
                    IndividualCostModifier = 0,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day1.ItineraryDayId,
                    ItemTitle = "Watch the Match",
                    ItemDescription = "Experience the World Cup live in one of the loudest stadiums in world sports.",
                    ItemOrderIndex = 7,
                    TimeOfDay = "Evening",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_lumenfield.jpeg",
                    SocialProofBadge = "Must-Do",
                    IndividualCostModifier = 250m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                },
                new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day1.ItineraryDayId,
                    ItemTitle = "Celebrate at Pioneer Square Pubs",
                    ItemDescription = "Ride the post-match energy back to Pioneer Square's historic taverns for craft beer and the loudest celebration in town.",
                    ItemOrderIndex = 8,
                    TimeOfDay = "Evening",
                    ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_flatstickpub.jpeg",
                    SocialProofBadge = "Celebration",
                    IndividualCostModifier = 45m,
                    IsOptionalActivity = false,
                    IsSelectedByDefault = true
                }
            );
            await db.SaveChangesAsync();

            // ─── Wishlist ↔ Destination links ──────────────────────────────────
            foreach (var dest in allDestinations)
            {
                bool alreadyLinked = await db.WishlistDestinations
                    .AnyAsync(wd => wd.WishlistId == wishlistId && wd.DestinationId == dest.DestinationId);

                if (!alreadyLinked)
                {
                    db.WishlistDestinations.Add(new WishlistDestination
                    {
                        WishlistId = wishlistId,
                        DestinationId = dest.DestinationId
                    });
                }
            }
            await db.SaveChangesAsync();
        }

        // ─── Local helper: images (Seattle-specific file names) ────────────────
        private static async Task SeedImagesForSeattleAsync(HodracDbContext db, List<Destination> destinations)
        {
            List<string> imageNames = new List<string>
    {
        "pikeplace.jpg",
        "breecherscheese.jpeg",
        "waterfront.jpg",
        "pioneersquare.jpeg",
        "underground.jpeg",
        "lumenfield.jpeg",
        "flatstickpub.jpeg"
    };

            foreach (var (dest, image) in destinations.Zip(imageNames, (d, i) => (d, i)))
            {
                db.DestinationImages.Add(new DestinationImage
                {
                    DestinationImageId = Guid.NewGuid(),
                    DestinationId = dest.DestinationId,
                    ImageUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_{Uri.EscapeDataString(image)}",
                    ThumbnailUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/seattle_{Uri.EscapeDataString(image)}",
                    Caption = $"Hero image of {dest.DestinationName}",
                    DisplayOrder = 1,
                    ImageType = "Hero",
                    ShotContext = "Exterior",
                    IsAiGenerated = false
                });
            }

            await db.SaveChangesAsync();
        }

        // ─── Local helper: language/currency (English/USD instead of Japanese/JPY) ──
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
