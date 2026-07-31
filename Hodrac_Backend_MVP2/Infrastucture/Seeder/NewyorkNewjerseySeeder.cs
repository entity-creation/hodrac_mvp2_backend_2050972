using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Seeder;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    public class NewyorkNewjerseySeeder
    {
        

        public static async Task SeedNewYorkNewJerseyWishlists(HodracDbContext db)
        {
            var usa = await db.Countries.FirstAsync(c => c.CountryName == "United States of America");
            var english = await db.Languages.FirstAsync(l => l.LanguageName == "English");
            var usd = await db.Currencies.FirstAsync(c => c.CurrencyCode == "USD");

            async Task<City> GetOrCreateCityAsync(string name, double lat, double lng, string description)
            {
                var city = await db.Cities.FirstOrDefaultAsync(c => c.CityName == name);
                if (city == null)
                {
                    city = new City { CityId = Guid.NewGuid(), CityName = name, CountryId = usa.CountryId, Latitude = lat, Longitude = lng, CityDescription = description };
                    db.Cities.Add(city);
                    await db.SaveChangesAsync();
                }
                return city;
            }

            var newYork = await GetOrCreateCityAsync("New York", 40.7128, -74.0060, "The five boroughs, principal fan base for a World Cup Final hosted just across the river in New Jersey.");
            var brooklyn = await GetOrCreateCityAsync("Brooklyn", 40.6782, -73.9442, "Waterfront neighborhoods with Manhattan skyline views, historic pizza rivalries, and the Brooklyn Bridge as its front door.");
            var eastRutherford = await GetOrCreateCityAsync("East Rutherford", 40.8128, -74.0742, "Home to MetLife Stadium ('New York New Jersey Stadium' for the World Cup) in the Meadowlands Sports Complex, with no direct rail connection from NYC.");
            var hoboken = await GetOrCreateCityAsync("Hoboken", 40.7439, -74.0324, "Walkable waterfront city across the Hudson, birthplace of Frank Sinatra and the modern rules of baseball, with PATH access to Manhattan.");
            var jerseyCity = await GetOrCreateCityAsync("Jersey City", 40.7178, -74.0431, "Dense, transit-connected city facing Lower Manhattan across the Hudson, home to a strong New York-style pizza scene.");
            var secaucus = await GetOrCreateCityAsync("Secaucus", 40.7895, -74.0565, "Meadowlands town whose Secaucus Junction rail station is the mandatory transfer point for every NJ Transit trip to MetLife Stadium.");
            var edison = await GetOrCreateCityAsync("Edison", 40.5187, -74.4121, "Central New Jersey township with a strong old-school Italian-American food scene.");
            var orange = await GetOrCreateCityAsync("Orange", 40.7712, -74.2323, "Essex County city, home to one of New Jersey's most decorated thin-crust pizza institutions.");
            var tomsRiver = await GetOrCreateCityAsync("Toms River", 39.9537, -74.1979, "Jersey Shore township, roughly 70 miles south of NYC — a genuine detour, not a casual add-on.");

            var mapping = new Dictionary<string, (string[] categories, string[] tags)>
            {
                ["New York Pizza Suprema"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "budget_friendly" }),
                ["Joe's Pizza"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "tourist_hotspot" }),
                ["Football Factory at Legends"] = (new[] { "entertainment_nightlife", "food_experience" }, new[] { "sports_fan", "social", "nightlife" }),
                ["Koreatown (NYC)"] = (new[] { "neighborhood_district", "food_experience" }, new[] { "food_focused", "walkable", "nightlife", "cultural" }),
                ["Telemundo Fan Village (Rockefeller Center)"] = (new[] { "activity_experience", "entertainment_nightlife" }, new[] { "sports_fan", "crowded", "budget_friendly", "social" }),
                ["Hoboken Waterfront"] = (new[] { "neighborhood_district", "viewpoint_scenic_spot" }, new[] { "walkable", "food_focused", "photography", "history" }),
                ["MetLife Stadium"] = (new[] { "activity_experience", "landmark_monument" }, new[] { "sports_fan", "premium", "crowded", "tourist_hotspot", "architecture" }),
                ["Madd Hatter"] = (new[] { "entertainment_nightlife" }, new[] { "sports_fan", "social", "nightlife" }),
                ["Ed & Mary's"] = (new[] { "entertainment_nightlife", "food_experience" }, new[] { "sports_fan", "social", "local_favorite" }),
                ["HopsScotch Tavern"] = (new[] { "entertainment_nightlife" }, new[] { "sports_fan", "social", "nightlife" }),

                ["Empire State Building"] = (new[] { "viewpoint_scenic_spot", "landmark_monument" }, new[] { "photography", "tourist_hotspot", "architecture", "history" }),
                ["SUMMIT One Vanderbilt"] = (new[] { "viewpoint_scenic_spot", "activity_experience" }, new[] { "photography", "tourist_hotspot", "premium", "quirky" }),
                ["Top of the Rock"] = (new[] { "viewpoint_scenic_spot", "landmark_monument" }, new[] { "photography", "tourist_hotspot", "architecture" }),
                ["Edge (Hudson Yards)"] = (new[] { "viewpoint_scenic_spot", "activity_experience" }, new[] { "photography", "adventurous", "premium", "tourist_hotspot" }),
                ["Bryant Park"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "relaxing", "walkable", "family_friendly" }),
                ["New York Public Library"] = (new[] { "cultural_site", "landmark_monument" }, new[] { "architecture", "cultural", "budget_friendly", "photography" }),
                ["Grand Central Terminal"] = (new[] { "landmark_monument", "activity_experience" }, new[] { "architecture", "photography", "tourist_hotspot", "walkable" }),
                ["Times Square"] = (new[] { "landmark_monument", "entertainment_nightlife" }, new[] { "tourist_hotspot", "photography", "crowded", "social" }),
                ["Broadway Theater District"] = (new[] { "entertainment_nightlife", "cultural_site" }, new[] { "cultural", "premium", "tourist_hotspot" }),

                ["One World Observatory"] = (new[] { "viewpoint_scenic_spot", "landmark_monument" }, new[] { "photography", "tourist_hotspot", "history" }),
                ["9/11 Memorial & Museum"] = (new[] { "historical_tour", "cultural_site" }, new[] { "history", "educational", "cultural" }),
                ["The Oculus"] = (new[] { "landmark_monument", "activity_experience" }, new[] { "architecture", "photography", "shopping" }),
                ["Statue of Liberty"] = (new[] { "landmark_monument", "historical_tour" }, new[] { "history", "photography", "tourist_hotspot" }),
                ["Ellis Island"] = (new[] { "historical_tour", "cultural_site" }, new[] { "history", "educational", "cultural" }),
                ["Brooklyn Bridge"] = (new[] { "landmark_monument", "activity_experience" }, new[] { "walkable", "photography", "history", "tourist_hotspot" }),
                ["DUMBO"] = (new[] { "neighborhood_district", "viewpoint_scenic_spot" }, new[] { "photography", "walkable", "tourist_hotspot" }),
                ["Brooklyn Bridge Park"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "relaxing", "walkable", "family_friendly", "photography" }),
                ["Juliana's Pizza"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "tourist_hotspot" }),

                ["Prince Street Pizza"] = (new[] { "food_experience" }, new[] { "food_focused", "tourist_hotspot", "local_favorite" }),
                ["Lombardi's"] = (new[] { "food_experience" }, new[] { "food_focused", "historic", "tourist_hotspot" }),
                ["John's of Bleecker Street"] = (new[] { "food_experience" }, new[] { "food_focused", "historic", "local_favorite" }),
                ["Brooklyn Boys Pizza & Deli"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "budget_friendly" }),
                ["Razza"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "premium" }),
                ["Roman Gourmet"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "budget_friendly" }),
                ["Amadeo Pizzeria"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "budget_friendly" }),
                ["Star Tavern"] = (new[] { "food_experience" }, new[] { "food_focused", "historic", "local_favorite" }),
                ["Rudy's Ristorante & Pizzeria"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite" }),
                ["Sal's Pizza"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite" }),
                ["Wahizza"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "quirky" }),
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
                var k = tagKeys.ToHashSet(); int s = 1;
                if (k.Contains("family_friendly")) s += 2;
                if (k.Contains("walkable")) s += 1;
                if (k.Contains("educational")) s += 1;
                if (k.Contains("adventurous")) s -= 1;
                if (k.Contains("nightlife")) s -= 1;
                return Math.Clamp(s, 1, 5);
            }
            static int AdventureScore(IEnumerable<string> tagKeys)
            {
                var k = tagKeys.ToHashSet(); int s = 1;
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

            // ─── GUIDs ────────────────────────────────────────────────────────────
            var dNYPS = Guid.NewGuid(); var dJoes = Guid.NewGuid(); var dFF = Guid.NewGuid(); var dKtown = Guid.NewGuid();
            var dFanVillage = Guid.NewGuid(); var dHoboken = Guid.NewGuid(); var dMetLife = Guid.NewGuid();
            var dMaddHatter = Guid.NewGuid(); var dEdMarys = Guid.NewGuid(); var dHopsScotch = Guid.NewGuid();

            var dESB = Guid.NewGuid(); var dSummit = Guid.NewGuid(); var dTotr = Guid.NewGuid(); var dEdge = Guid.NewGuid();
            var dBryant = Guid.NewGuid(); var dNYPL = Guid.NewGuid(); var dGCT = Guid.NewGuid(); var dTimesSq = Guid.NewGuid(); var dBroadway = Guid.NewGuid();

            var dOWO = Guid.NewGuid(); var d911 = Guid.NewGuid(); var dOculus = Guid.NewGuid(); var dSoL = Guid.NewGuid();
            var dEllis = Guid.NewGuid(); var dBB = Guid.NewGuid(); var dDumbo = Guid.NewGuid(); var dBBP = Guid.NewGuid(); var dJulianas = Guid.NewGuid();

            var dPrince = Guid.NewGuid(); var dLombardis = Guid.NewGuid(); var dJohns = Guid.NewGuid(); var dBKBoys = Guid.NewGuid();
            var dRazza = Guid.NewGuid(); var dRoman = Guid.NewGuid(); var dAmadeo = Guid.NewGuid(); var dStar = Guid.NewGuid();
            var dRudys = Guid.NewGuid(); var dSals = Guid.NewGuid(); var dWahizza = Guid.NewGuid();

            var newDestinations = new[]
            {
        new Destination { DestinationId = dNYPS, DestinationName = "New York Pizza Suprema", CleanNormalizedSearchName = "new york pizza suprema", MetaphoneCode = "NY PS SPRM", DoubleMetaphonePrimary = "NY PS SPRM", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A Penn Station-adjacent NYC slice institution, prized for a crisp, well-baked crust and a rich, slightly tangy sauce that fans single out over sweeter competitors. Its location — steps from Madison Square Garden and the Amtrak/NJ Transit concourse — makes it a genuinely practical breakfast before a trip out to MetLife Stadium, since you're already at the departure point.",
              "directions": "addressForRideshare: 413 8th Ave, New York, NY 10001, between W 30th and W 31st St.",
              "whatToKnow": "location: Directly tied to Penn Station's foot traffic — a natural stop before boarding NJ Transit toward Secaucus Junction.\nsignatureOrder: Regular cheese slice and the Sicilian square are both well regarded; the sauce is the differentiator most reviewers cite.",
              "thingsToBeWaryOf": "smallSeating: Limited seating means many people eat standing or take slices to go for the train.\ngameDayRush: Expect a busier-than-usual line on match mornings given the Penn Station foot traffic.",
              "localPerspective": "Long treated as a reliable, no-nonsense Knicks/Rangers-night pizza stop given the Madison Square Garden proximity — locals recommend it specifically for the pre-event fuel-up role it plays here.",
              "hiddenCost": "slice: $4-$6.\nWholePie: $20-$28.",
              "nearbyComplements": ["Penn Station / NJ Transit concourse: Directly adjacent.", "Madison Square Garden: A short walk."],
              "bestTimeToVisit": "Early morning, timed right before catching an NJ Transit train toward Secaucus Junction.",
              "crowdLevel": "Medium (5/10), higher around Penn Station rush hours and match mornings.",
              "accessibility": "rating: 8/10 — street-level storefront, standard counter-service layout.",
              "idealDuration": "20 to 30 minutes."
            }
            """,
            AverageCostPerDay = 8m, LuxuryRating = DeriveLuxury(8m, mapping["New York Pizza Suprema"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["New York Pizza Suprema"].tags), AdventurePaceScore = AdventureScore(mapping["New York Pizza Suprema"].tags), AestheticTrendScore = AestheticTrendScore(mapping["New York Pizza Suprema"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["New York Pizza Suprema"].tags), Latitude = 40.7505, Longitude = -73.9944, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dJoes, DestinationName = "Joe's Pizza", CleanNormalizedSearchName = "joes pizza", MetaphoneCode = "JS PS", DoubleMetaphonePrimary = "JS PS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Opened in 1975 by Naples-born Joe Pozzuoli, Joe's Pizza is a Greenwich Village institution serving the classic thin, crispy New York slice — famous for its balanced sweet-and-spicy tomato sauce and a cameo in Sam Raimi's original Spider-Man film. A better fit than New York Pizza Suprema if you're staying or starting your day downtown rather than near Penn Station.",
              "directions": "addressForRideshare: 7 Carmine St, New York, NY 10014, Greenwich Village.",
              "whatToKnow": "signatureOrder: A plain cheese slice, fresh from the oven, is the standard recommendation.\nfilmHistory: Featured in Spider-Man (2002) — a point of pride for regulars and a photo-op for visitors.\nhours: Generally open late — Sunday-Thursday 10 AM-3 AM, Friday-Saturday 10 AM-5 AM at the original location.",
              "thingsToBeWaryOf": "limitedSeating: Standing/counter eating is the norm; nearby benches fill the gap.\nMultipleLocations: Several Joe's Pizza locations exist around NYC — confirm you're headed to the original Carmine Street spot if the history matters to you.",
              "localPerspective": "Greenwich Village locals treat it as a genuine neighborhood fixture that's remained consistent through decades of NYC pizza trend cycles — Joe Pozzuoli, now in his mid-70s, still owns and operates it.",
              "hiddenCost": "slice: $4-$6.\nWholePie: $22-$28.",
              "nearbyComplements": ["John's of Bleecker Street: A short walk.", "Washington Square Park: A short walk."],
              "bestTimeToVisit": "If starting downtown, any time — the original spot runs very late hours.",
              "crowdLevel": "High (7/10) — this is one of the most-visited pizza counters in the city.",
              "accessibility": "rating: 7/10 — narrow storefront, standing/counter service.",
              "idealDuration": "20 to 30 minutes."
            }
            """,
            AverageCostPerDay = 8m, LuxuryRating = DeriveLuxury(8m, mapping["Joe's Pizza"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Joe's Pizza"].tags), AdventurePaceScore = AdventureScore(mapping["Joe's Pizza"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Joe's Pizza"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Joe's Pizza"].tags), Latitude = 40.7307, Longitude = -74.0023, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dFF, DestinationName = "Football Factory at Legends", CleanNormalizedSearchName = "football factory at legends", MetaphoneCode = "FTBL FKTR AT LJNTS", DoubleMetaphonePrimary = "FTPL FKTR AT LJNTS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Widely regarded as the premier soccer bar in New York City, Football Factory at Legends screens over 100 live matches a week from leagues worldwide across multiple floors, more than 30 beers on tap, and a genuinely international, supporters-club atmosphere — it's the home bar for several official South American and European club supporters groups. Located in the Koreatown neighborhood, making it a natural single stop with the rest of that area.",
              "directions": "addressForRideshare: Koreatown, Manhattan (West 32nd Street corridor) — check current exact address.",
              "whatToKnow": "noReservations: No reservations are taken during World Cup matches — arrive early for a marquee fixture.\nmultiFloor: Multiple floors and dozens of screens mean even a packed house rarely feels claustrophobic.\nsupportersClubs: Home base for supporters clubs including River Plate New York — expect passionate, organized chanting sections during big matches.",
              "thingsToBeWaryOf": "capacityOnBigMatchDays: Marquee fixtures fill the venue completely — arrive well before kickoff.\nNoisyEnvironment: This is a loud, high-energy sports bar, not a place for a quiet conversation.",
              "localPerspective": "International soccer fans across NYC treat this as the default 'find your country's supporters section' bar — a genuine institution for exactly this kind of pre-match atmosphere-building.",
              "hiddenCost": "beer: $8-$12.\nBarFood: $12-$22 per dish.",
              "nearbyComplements": ["Koreatown: Directly in the same neighborhood.", "Herald Square/Macy's: A short walk."],
              "bestTimeToVisit": "2-3 hours before a marquee match to secure a spot before it fills.",
              "crowdLevel": "Maximum (10/10) for marquee World Cup matches.",
              "accessibility": "rating: 7/10 — multi-level venue; ask about elevator access if needed.",
              "idealDuration": "1 to 2 hours."
            }
            """,
            AverageCostPerDay = 30m, LuxuryRating = DeriveLuxury(30m, mapping["Football Factory at Legends"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Football Factory at Legends"].tags), AdventurePaceScore = AdventureScore(mapping["Football Factory at Legends"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Football Factory at Legends"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Football Factory at Legends"].tags), Latitude = 40.7477, Longitude = -73.9857, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dKtown, DestinationName = "Koreatown (NYC)", CleanNormalizedSearchName = "koreatown nyc", MetaphoneCode = "KRTN", DoubleMetaphonePrimary = "KRTN", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A dense, vertical stretch of West 32nd Street in Midtown Manhattan packed with Korean BBQ, karaoke rooms, cafes, and late-night spots — genuinely one of the highest-energy blocks in the city, and the same neighborhood that's home to Football Factory at Legends, making the two a natural single stop.",
              "directions": "addressForRideshare: W 32nd St between Broadway and 5th Ave, New York, NY 10001.",
              "whatToKnow": "verticalDensity: Many venues are stacked on multiple floors of the same buildings — look up, not just at street level.\nLateNightKaraoke: Karaoke rooms here run genuinely late, making it a strong pairing with a pre-match bar crawl.",
              "thingsToBeWaryOf": "PeakDinnerLines: Popular BBQ spots have real waits at peak dinner hours.",
              "localPerspective": "NYC's most concentrated Korean food-and-nightlife strip — locals treat it as a complete night out in itself, not just a quick meal stop.",
              "hiddenCost": "koreanBBQ: $30-$55 per person.\nKaraokeRoom: $30-$50/hour split among the group.",
              "nearbyComplements": ["Football Factory at Legends: In the same block.", "Herald Square: A short walk."],
              "bestTimeToVisit": "Late afternoon into evening for the fullest atmosphere.",
              "crowdLevel": "High (7/10), especially evenings.",
              "accessibility": "rating: 7/10 — flat sidewalks; individual venues vary.",
              "idealDuration": "1 to 2 hours."
            }
            """,
            AverageCostPerDay = 35m, LuxuryRating = DeriveLuxury(35m, mapping["Koreatown (NYC)"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Koreatown (NYC)"].tags), AdventurePaceScore = AdventureScore(mapping["Koreatown (NYC)"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Koreatown (NYC)"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Koreatown (NYC)"].tags), Latitude = 40.7480, Longitude = -73.9862, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dFanVillage, DestinationName = "Telemundo Fan Village (Rockefeller Center)", CleanNormalizedSearchName = "telemundo fan village rockefeller center", MetaphoneCode = "TLMNT FLJ RKFLR", DoubleMetaphonePrimary = "TLMNT FLJ RKFLR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "important: the region's originally announced centralized FIFA Fan Festival at Liberty State Park was cancelled in February 2026, with New Jersey redirecting funding to smaller community fan zones instead. The genuinely Manhattan-based, FIFA-sanctioned replacement is the Telemundo Fan Village at Rockefeller Center — free, running July 4-19 for the knockout stages, with a FIFA Museum exhibit and match broadcasts. If your plan is a Manhattan morning before heading to MetLife, this is the real 'FIFA Fan Festival' stop, not Liberty State Park.",
              "directions": "addressForRideshare: 50 Rockefeller Plaza, New York, NY 10020.\nfromKoreatown: About a 10-15 minute walk or short subway ride north.",
              "whatToKnow": "dates: July 4-19, 2026, covering the knockout stages through the Final.\nfree: No cost to attend.\nAlternative: If you're visiting during the earlier group stage (before July 4), Fan Zone Queens at the USTA Billie Jean King National Tennis Center (Flushing Meadows) is the flagship group-stage option instead — a 7 train ride from Times Square, about 30 minutes.",
              "thingsToBeWaryOf": "DatesMatter: This specific site only operates July 4-19 — outside that window, check the Fan Zone Queens or Hudson Yards Backyard options instead.\nCrowds: Rockefeller Center draws huge foot traffic even without a fan village; expect real density during marquee matches.",
              "localPerspective": "New Yorkers describe the last-minute shift away from a single mega-fan-zone toward a five-borough network as genuinely improving access — more neighborhoods get a real fan zone instead of concentrating everything at one distant NJ park.",
              "hiddenCost": "free entry.\nFoodAndDrink: Variable, from nearby Rockefeller Center vendors.",
              "nearbyComplements": ["Top of the Rock: In the same complex.", "Radio City Music Hall: A short walk.", "Fifth Avenue: A short walk."],
              "bestTimeToVisit": "During a knockout-stage match broadcast, July 4-19.",
              "crowdLevel": "Maximum (10/10) during marquee knockout matches.",
              "accessibility": "rating: 9/10 — flat, open plaza with standard midtown accessibility.",
              "idealDuration": "1 to 3 hours depending on whether you're there for a full match."
            }
            """,
            AverageCostPerDay = 10m, LuxuryRating = DeriveLuxury(10m, mapping["Telemundo Fan Village (Rockefeller Center)"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Telemundo Fan Village (Rockefeller Center)"].tags), AdventurePaceScore = AdventureScore(mapping["Telemundo Fan Village (Rockefeller Center)"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Telemundo Fan Village (Rockefeller Center)"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Telemundo Fan Village (Rockefeller Center)"].tags), Latitude = 40.7587, Longitude = -73.9787, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dHoboken, DestinationName = "Hoboken Waterfront", CleanNormalizedSearchName = "hoboken waterfront", MetaphoneCode = "HBKN WTRFRNT", DoubleMetaphonePrimary = "HPKN WTRFRNT", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A walkable waterfront city directly across the Hudson from Midtown, Hoboken offers Manhattan skyline views, a lively restaurant and match-viewing scene along Washington Street, and genuine history — birthplace of Frank Sinatra (1915) and the site where the modern rules of baseball were codified in 1846. A good stop if you have extra time before continuing on to the stadium.",
              "directions": "addressForRideshare: Washington Street & Hudson Place, Hoboken, NJ 07030.\ntransit: PATH train from 33rd Street (Manhattan) or World Trade Center directly to Hoboken; NJ Transit rail also stops here en route toward Secaucus Junction.",
              "whatToKnow": "washingtonStreet: The main commercial spine, lined with bars and restaurants that show matches on big screens, including the Madd Hatter.\nwaterfrontViews: The Hoboken waterfront path offers some of the best straight-on Midtown Manhattan skyline views in the region.",
              "thingsToBeWaryOf": "TimingBuffer: If you're continuing on to MetLife Stadium afterward, Hoboken doesn't sit directly on the Meadowlands Rail Line — factor in the connection back through Secaucus Junction or a rideshare.",
              "localPerspective": "Hoboken locals describe Washington Street on a big match day as feeling like a genuine European high street — flags, jerseys, and packed sidewalk seating outside every bar.",
              "hiddenCost": "free to walk; individual bar/restaurant spending varies.",
              "nearbyComplements": ["Madd Hatter: On Washington Street.", "Hoboken waterfront path: A short walk from downtown."],
              "bestTimeToVisit": "Late morning to early afternoon if using it as a stop en route to the stadium.",
              "crowdLevel": "Medium (5/10), High (7/10) on major match days.",
              "accessibility": "rating: 9/10 — flat, walkable downtown grid.",
              "idealDuration": "45 minutes to 1.5 hours."
            }
            """,
            AverageCostPerDay = 15m, LuxuryRating = DeriveLuxury(15m, mapping["Hoboken Waterfront"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Hoboken Waterfront"].tags), AdventurePaceScore = AdventureScore(mapping["Hoboken Waterfront"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Hoboken Waterfront"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Hoboken Waterfront"].tags), Latitude = 40.7439, Longitude = -74.0324, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dMetLife, DestinationName = "MetLife Stadium", CleanNormalizedSearchName = "metlife stadium", MetaphoneCode = "MTLF STTM", DoubleMetaphonePrimary = "MTLF STTM", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "MetLife Stadium hosts eight World Cup 2026 matches — five group-stage games, a Round of 32, a Round of 16, and the Final on July 19 — more than any other venue in the tournament, with a capacity around 82,500. Under FIFA's no-sponsor-names policy, it operates officially as 'New York New Jersey Stadium.' A new natural-grass surface (Tahoma 31 bermudagrass, grown near Charlotte, NC) replaced the usual turf specifically for the tournament.\n\nwhatToDo: Team stores and merchandise shops, fan photo areas around the plaza, and pre-match activations fill the concourse in the hours before kickoff.",
              "directions": "noDirectSubway: There is no direct subway connection from NYC — every route needs a transfer, drive, or dedicated shuttle.\nnjTransitRoute: From Penn Station NYC, any NJ Transit rail service to Secaucus Junction (~10 min), then transfer to the dedicated Meadowlands Rail Line shuttle direct to the stadium. Round-trip combined fare runs roughly $98-105 for World Cup match days; purchase only via the NJ Transit app in advance (40,000-ticket daily cap, no day-of sales at stations).\nofficialShuttle: Round-trip shuttle buses (~$20) run from designated NYC hubs and at least one NJ park-and-ride/garage location — must be booked in advance, non-transferable.\naddressForGPS: One MetLife Stadium Drive, East Rutherford, NJ 07073.",
              "whatToKnow": "arriveEarly: Real attendee advice: aim to be at the stadium roughly 90 minutes before kickoff to find food and locate your section with time to spare — staff directions and signage can be unreliable, and finding a specific section has taken some fans 30+ minutes.\nfoodLines: Middle-level concourses get most crowded; upper-level food lines tend to be noticeably shorter.\nfreeWater: Free water is typically distributed outside the stadium in giant coolers; security generally allows multiple sealed bottles in.\nsouvenirCups: Official souvenir cups (around $6) draw long lines — many fans leave theirs behind in their seats post-match, so scouting empty sections after the final whistle can get you one free.\nfreebiesAtBooths: Sponsor activation booths (e.g., past Bank of America presence) have handed out free branded items like bracelets — bring a small tote bag if you want to collect them without juggling.\nfanFestPostMatch: Fan-fest-style activations outside the stadium often stay open roughly 2 hours after the match with shorter lines than pre-match.\nseatsEarly: Be in your seat at least 20 minutes before kickoff for team introductions and anthems; FIFA has run kickoffs precisely on schedule.\nhalftimeLength: Halftime runs about 15 minutes — budget food/bathroom runs accordingly.\nsunExposure: North-facing upper sections (roughly 308-320 and 208-219) sit in direct, blazing sun for day matches — dress and hydrate accordingly.",
              "thingsToBeWaryOf": "clearBagPolicy: A clear bag policy is enforced for World Cup matches — check current size limits before arriving.\nNoGeneralParking: There is no general public parking at the stadium; American Dream Mall parking (~5,000 spots, ~$225/match) is ticket-holder-only and has sold out for the Final. Buy any parking or transit tickets only through official channels — everything else is scam risk until official sales open.\nExitCrush: With ~82,500 people leaving at once, the exit is genuinely rough. Real attendee advice: if you can, leave around the 80th minute to beat the crowd through the escalators, bathrooms, and shuttle queue — otherwise, plan to wait out most of the crowd before moving. Shuttles typically keep running for up to 3 hours post-match.\nSurgePricing: Rideshare surge pricing peaks 2-3 hours before kickoff and for at least 2 hours after the final whistle.",
              "localPerspective": "The stadium has hosted Super Bowls, outdoor hockey, and some of the biggest concerts in the world (Taylor Swift, Beyoncé, the Rolling Stones) — locals describe the World Cup buildup as unlike any prior event here, with an earlier start, a more international crowd, and a broader supporter culture than a typical NFL Sunday.",
              "hiddenCost": "matchTicket: Highly variable by fixture; knockout tickets from roughly $150, with Final pricing significantly higher.\nNjTransitRoundTrip: Approximately $98-105 for the combined Penn Station-Secaucus-stadium World Cup service.\nOfficialShuttle: Approximately $20 round trip.\nAmericanDreamParking: Approximately $225/match (ticket holders only, subject to availability).\nSouvenirCup: About $6.\nConcessions: $8-$18 for food, $10-$16 for beer.",
              "nearbyComplements": ["Secaucus Junction: The mandatory rail transfer point.", "American Dream Mall: Connected via walkway, ticket-holder parking only.", "Meadowlands Racing & Entertainment: The designated rideshare pickup/drop-off point."],
              "bestTimeToVisit": "Arrive with a real buffer — 90 minutes to 2+ hours before kickoff for a major match, both for security and to actually find your section with time to eat.",
              "crowdLevel": "Maximum (10/10) — especially for the Final.",
              "accessibility": "rating: 9/10 — full ADA-compliant seating and elevators; note the lack of direct rail can complicate accessible transport planning specifically.",
              "idealDuration": "4 to 5 hours total including a real transit buffer, the match, and a deliberate exit strategy."
            }
            """,
            AverageCostPerDay = 320m, LuxuryRating = DeriveLuxury(320m, mapping["MetLife Stadium"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["MetLife Stadium"].tags), AdventurePaceScore = AdventureScore(mapping["MetLife Stadium"].tags), AestheticTrendScore = AestheticTrendScore(mapping["MetLife Stadium"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["MetLife Stadium"].tags), Latitude = 40.8135, Longitude = -74.0745, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dMaddHatter, DestinationName = "Madd Hatter", CleanNormalizedSearchName = "madd hatter", MetaphoneCode = "MT HTR", DoubleMetaphonePrimary = "MT HTR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A large Hoboken sports bar with 75+ screens on Washington Street — one of the region's designated NJ soccer watch venues for the World Cup, and genuinely built for exactly this kind of crowd viewing.",
              "directions": "addressForRideshare: Washington St, Hoboken, NJ 07030 — check current cross street.",
              "whatToKnow": "screenCount: 75+ screens means every seat has a sightline, even during a packed match.\nWashingtonStreetLocation: Sits along Hoboken's main bar-and-restaurant strip, easy to combine with other Washington Street stops.",
              "thingsToBeWaryOf": "MarqueeMatchCrowds: Expect a full house for high-profile fixtures — arrive early or consider a reservation if available.",
              "localPerspective": "One of Hoboken's default big-game venues generally, not just for the World Cup — locals already treat it as the neighborhood's biggest sports-viewing room.",
              "hiddenCost": "beer: $7-$11.\nBarFood: $12-$20.",
              "nearbyComplements": ["Hoboken Waterfront: Same downtown area.", "PATH station: A short walk."],
              "bestTimeToVisit": "Well before kickoff for a marquee match to secure a seat.",
              "crowdLevel": "Maximum (10/10) for marquee matches.",
              "accessibility": "rating: 8/10 — standard bar/restaurant accessibility.",
              "idealDuration": "1.5 to 3 hours."
            }
            """,
            AverageCostPerDay = 25m, LuxuryRating = DeriveLuxury(25m, mapping["Madd Hatter"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Madd Hatter"].tags), AdventurePaceScore = AdventureScore(mapping["Madd Hatter"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Madd Hatter"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Madd Hatter"].tags), Latitude = 40.7440, Longitude = -74.0295, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dEdMarys, DestinationName = "Ed & Mary's", CleanNormalizedSearchName = "ed and marys", MetaphoneCode = "AT ANT MRS", DoubleMetaphonePrimary = "AT ANT MRS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A Jersey City soccer watch venue named among the region's designated NJ spots for catching World Cup matches — note this is genuinely in Jersey City, not East Rutherford near the stadium, so it fits best as a post-match celebration for those staying on the Jersey City/Hoboken side rather than a stadium-adjacent stop.",
              "directions": "addressForRideshare: Jersey City, NJ — check current exact address.",
              "whatToKnow": "designatedWatchVenue: Officially listed among the region's NJ soccer bars and watch venues for the tournament.",
              "thingsToBeWaryOf": "DistanceFromStadium: Budget real travel time back from MetLife Stadium if this is your post-match destination — it's not a stadium-adjacent bar.",
              "localPerspective": "Part of Jersey City's broader sports-bar scene, which locals lean on heavily given the city's easy PATH access back into Manhattan.",
              "hiddenCost": "beer: $7-$10.\nBarFood: $10-$18.",
              "nearbyComplements": ["HopsScotch Tavern: Also in Jersey City.", "PATH stations: Multiple nearby options depending on exact location."],
              "bestTimeToVisit": "Evening, especially post-match if you're staying in Jersey City.",
              "crowdLevel": "High (7/10) on match nights.",
              "accessibility": "rating: 7/10 — standard bar accessibility.",
              "idealDuration": "1.5 to 2.5 hours."
            }
            """,
            AverageCostPerDay = 20m, LuxuryRating = DeriveLuxury(20m, mapping["Ed & Mary's"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Ed & Mary's"].tags), AdventurePaceScore = AdventureScore(mapping["Ed & Mary's"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Ed & Mary's"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Ed & Mary's"].tags), Latitude = 40.7178, Longitude = -74.0500, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dHopsScotch, DestinationName = "HopsScotch Tavern", CleanNormalizedSearchName = "hopsscotch tavern", MetaphoneCode = "HPSKX TFRN", DoubleMetaphonePrimary = "HPSKX TFRN", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Another Jersey City entry on the region's official list of NJ soccer bars and watch venues — a solid option for a post-match beer if you're staying on the Jersey City side rather than heading back toward Manhattan or the stadium area directly.",
              "directions": "addressForRideshare: Jersey City, NJ — check current exact address.",
              "whatToKnow": "designatedWatchVenue: Officially listed among the region's NJ soccer bars for the tournament.",
              "thingsToBeWaryOf": "DistanceFromStadium: Like Ed & Mary's, this is a Jersey City venue, not a stadium-adjacent one — plan travel time accordingly.",
              "localPerspective": "Part of the same Jersey City sports-bar cluster that benefits from strong PATH connectivity into Manhattan, making it a practical base for fans without cars.",
              "hiddenCost": "beer: $7-$10.\nBarFood: $10-$18.",
              "nearbyComplements": ["Ed & Mary's: Also in Jersey City.", "PATH stations: Nearby depending on exact location."],
              "bestTimeToVisit": "Evening, post-match, if based in Jersey City.",
              "crowdLevel": "High (7/10) on match nights.",
              "accessibility": "rating: 7/10 — standard bar accessibility.",
              "idealDuration": "1.5 to 2.5 hours."
            }
            """,
            AverageCostPerDay = 20m, LuxuryRating = DeriveLuxury(20m, mapping["HopsScotch Tavern"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["HopsScotch Tavern"].tags), AdventurePaceScore = AdventureScore(mapping["HopsScotch Tavern"].tags), AestheticTrendScore = AestheticTrendScore(mapping["HopsScotch Tavern"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["HopsScotch Tavern"].tags), Latitude = 40.7200, Longitude = -74.0450, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dESB, DestinationName = "Empire State Building", CleanNormalizedSearchName = "empire state building", MetaphoneCode = "MPR ST BLTNK", DoubleMetaphonePrimary = "MPR ST PLTNK", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A 102-story Art Deco landmark completed in 1931 in just 410 days, the Empire State Building was the world's tallest for nearly 40 years and remains NYC's most-visited observation deck, drawing over 3.5 million visitors a year. Two decks: the open-air 86th floor (1,050 ft) and the enclosed 102nd floor (1,250 ft).",
              "directions": "addressForRideshare: 350 Fifth Ave, New York, NY 10118.",
              "whatToKnow": "tickets: 86th-floor tickets start around $44; the 86th+102nd combo starts around $79; Express Pass (skip all lines) starts around $89; a $5 booking fee applies per transaction.\nfreeLobby: The restored 1930s Art Deco lobby murals are free to view without an observatory ticket.\nkingKongExhibit: The 2nd-floor galleries include an immersive walk-through recreating the 1933 film's iconic scene.",
              "thingsToBeWaryOf": "TimedEntry: All tickets require booking a timed entry slot online in advance.\nWeatherDependence: The 86th floor is open-air; check forecasts, especially in a hot NYC summer.",
              "localPerspective": "Despite newer, flashier decks opening in recent years, many New Yorkers still consider this the essential first-timer's observatory — the building itself, not just the view, is the attraction.",
              "hiddenCost": "eightySixthFloor: From $44.\nComboTicket: From $79.\nExpressPass: From $89.\nChildrenUnder6: Free.",
              "nearbyComplements": ["Bryant Park: A short walk.", "Herald Square: A short walk."],
              "bestTimeToVisit": "Early morning or late evening for shorter lines and dramatic light.",
              "crowdLevel": "High (7/10) year-round, Maximum (10/10) at sunset.",
              "accessibility": "rating: 9/10 — elevator access throughout; open-air deck is level.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 44m, LuxuryRating = DeriveLuxury(44m, mapping["Empire State Building"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Empire State Building"].tags), AdventurePaceScore = AdventureScore(mapping["Empire State Building"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Empire State Building"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Empire State Building"].tags), Latitude = 40.7484, Longitude = -73.9857, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dSummit, DestinationName = "SUMMIT One Vanderbilt", CleanNormalizedSearchName = "summit one vanderbilt", MetaphoneCode = "SMT ON FNTRPLT", DoubleMetaphonePrimary = "SMT ON FNTRPLT", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Atop the tallest commercial building in Midtown, SUMMIT One Vanderbilt reimagines the observation deck as an immersive art experience — three floors of floor-to-ceiling mirrored rooms, suspended glass skyboxes at 1,063 ft, and the world's largest exterior glass elevator (the 'Ascent') rising to 1,210 ft, plus an open-air Nordic café terrace designed by Snøhetta.",
              "directions": "addressForRideshare: 45 E 42nd St, New York, NY 10017. Enter via the Grand Central Transit Hall at Vanderbilt Ave & 43rd St, street level at 45 E 42nd St, or the Grand Central Main Concourse via Vanderbilt Passage — not through One Vanderbilt's main office lobby.",
              "whatToKnow": "tickets: Starts around $44; the Ascent glass-elevator upgrade costs extra; timed entry required, book ahead as sunset and weekend slots sell out.\ndressNote: Mirrored floors reflect everything — wear pants, shorts, or tights, and flat non-marking shoes.\ncomparisonPoint: The standout view here is looking directly across at the Empire State Building rather than up at it.",
              "thingsToBeWaryOf": "SellOutRisk: Sunset and weekend slots sell out regularly — book well ahead.\nNotThroughMainLobby: The entrance is genuinely separate from One Vanderbilt's office lobby — follow the Grand Central-based directions specifically.",
              "localPerspective": "Frequently cited by first-time and repeat NYC visitors alike as the most talked-about of the five current observation decks, thanks to the mirror rooms and glass skyboxes rather than the view alone.",
              "hiddenCost": "generalAdmission: From roughly $44.\nAscentUpgrade: Additional cost, check current pricing.",
              "nearbyComplements": ["Grand Central Terminal: One-minute walk, shares an entrance point.", "Bryant Park: A short walk."],
              "bestTimeToVisit": "Sunset for the best light, booked well in advance.",
              "crowdLevel": "High (7/10), Maximum (10/10) at sunset/weekends.",
              "accessibility": "rating: 8/10 — elevator access; mirrored floors can be visually disorienting for some visitors.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 44m, LuxuryRating = DeriveLuxury(44m, mapping["SUMMIT One Vanderbilt"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["SUMMIT One Vanderbilt"].tags), AdventurePaceScore = AdventureScore(mapping["SUMMIT One Vanderbilt"].tags), AestheticTrendScore = AestheticTrendScore(mapping["SUMMIT One Vanderbilt"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["SUMMIT One Vanderbilt"].tags), Latitude = 40.7527, Longitude = -73.9772, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dTotr, DestinationName = "Top of the Rock", CleanNormalizedSearchName = "top of the rock", MetaphoneCode = "TP OF 0 RK", DoubleMetaphonePrimary = "TP OF T RK", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Rockefeller Center's observation deck across three tiers (67th, 69th, and 70th floors), widely considered to have the best direct view of Central Park among Manhattan's observatories, plus an unobstructed sightline to the Empire State Building without it blocking your own photos.",
              "directions": "addressForRideshare: 30 Rockefeller Plaza, New York, NY 10112.",
              "whatToKnow": "threeTiers: Each level offers a different vantage — the top tier is fully open-air.\ncentralParkView: The most-cited reason to choose this deck over others if Central Park is your priority shot.",
              "thingsToBeWaryOf": "TimedEntry: Book a timed slot in advance, especially for sunset.\nCombinedWithFanVillage: If visiting during the Telemundo Fan Village dates (July 4-19), expect extra crowds in the surrounding plaza.",
              "localPerspective": "Locals frequently recommend this over the Empire State Building specifically for the Central Park sightline and slightly shorter average lines.",
              "hiddenCost": "generalAdmission: Check current pricing, generally comparable to other major NYC decks (roughly $40+).",
              "nearbyComplements": ["Telemundo Fan Village: In the same complex during knockout stages.", "Radio City Music Hall: A short walk."],
              "bestTimeToVisit": "Late afternoon into sunset, booked ahead.",
              "crowdLevel": "High (7/10), Maximum (10/10) at sunset.",
              "accessibility": "rating: 9/10 — elevator access to all tiers.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 40m, LuxuryRating = DeriveLuxury(40m, mapping["Top of the Rock"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Top of the Rock"].tags), AdventurePaceScore = AdventureScore(mapping["Top of the Rock"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Top of the Rock"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Top of the Rock"].tags), Latitude = 40.7590, Longitude = -73.9787, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dEdge, DestinationName = "Edge (Hudson Yards)", CleanNormalizedSearchName = "edge hudson yards", MetaphoneCode = "AJ HTSN YRTS", DoubleMetaphonePrimary = "AJ HTSN YRTS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The highest outdoor sky deck in the Western Hemisphere at 1,100+ ft, Edge puts you on a cantilevered glass-floor platform extending 80 ft beyond the building's face, with angled glass walls leaning outward for the 'floating above NYC' photo. The optional City Climb adds a harnessed ascent to roughly 1,271 ft with a hands-free lean-out at the top.",
              "directions": "addressForRideshare: 30 Hudson Yards, New York, NY 10001.",
              "whatToKnow": "tickets: From roughly $39; Champagne Admission (~$66) and Sunset VIP (~$109) bundles include a drink; City Climb is a separate ~$185 ticket, 90-120 minutes.\nfullyAccessible: Elevators and ramps throughout; the outdoor deck itself is flat and wheelchair accessible; City Climb is not (involves outdoor stairs/climbing).\nviewRange: Up to 80 miles on a clear day — Empire State Building and Midtown to the east, Statue of Liberty and the harbor to the south, Central Park to the north, and Hoboken/Jersey City/the Palisades to the west.",
              "thingsToBeWaryOf": "BagSizeLimit: Bags over 9\"x14\"x22\" aren't permitted, with no on-site storage.\nWindExposure: This is a genuinely outdoor, exposed platform — dress for wind at height even on a warm day.",
              "localPerspective": "Locals note Edge is the most physically dramatic of NYC's observation decks specifically because there's no enclosure — you're standing in open air rather than behind glass, which is a meaningfully different sensation than the other decks.",
              "hiddenCost": "standardAdmission: From roughly $39.\nChampagneAdmission: Around $66.\nSunsetVIP: Around $109.\nCityClimb: Around $185, separate ticket.",
              "nearbyComplements": ["The Shops & Restaurants at Hudson Yards: In the same building.", "Hudson Yards Backyard Fan Zone: Nearby, free full-tournament fan viewing."],
              "bestTimeToVisit": "Sunset for the most dramatic light, booked in advance.",
              "crowdLevel": "High (7/10), Maximum (10/10) at sunset.",
              "accessibility": "rating: 9/10 for the standard deck (fully accessible); City Climb is not wheelchair accessible.",
              "idealDuration": "1 to 1.5 hours for the deck; add 90-120 minutes for City Climb."
            }
            """,
            AverageCostPerDay = 39m, LuxuryRating = DeriveLuxury(39m, mapping["Edge (Hudson Yards)"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Edge (Hudson Yards)"].tags), AdventurePaceScore = AdventureScore(mapping["Edge (Hudson Yards)"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Edge (Hudson Yards)"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Edge (Hudson Yards)"].tags), Latitude = 40.7538, Longitude = -74.0022, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dBryant, DestinationName = "Bryant Park", CleanNormalizedSearchName = "bryant park", MetaphoneCode = "BRNT PRK", DoubleMetaphonePrimary = "PRNT PRK", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A genteel, tree-lined green space behind the New York Public Library's main branch — a quiet counterpoint to the density of Midtown, with lawn chairs, a carousel, and seasonal programming (an ice rink in winter, outdoor movies and reading rooms in summer).",
              "directions": "addressForRideshare: 42nd St & 6th Ave, New York, NY 10018.",
              "whatToKnow": "freeToEnter: No admission cost; it functions as Midtown's default outdoor breather.\nseasonalProgramming: Check what's currently running — summer often includes free reading room hours and occasional film screenings.",
              "thingsToBeWaryOf": "LunchRushCrowding: Weekday lunch hours draw heavy office-worker foot traffic for the limited seating.",
              "localPerspective": "Office workers throughout Midtown treat it as their default outdoor lunch spot — genuinely a working park, not just a tourist pass-through.",
              "hiddenCost": "free.",
              "nearbyComplements": ["New York Public Library: Directly adjacent.", "Grand Central Terminal and SUMMIT One Vanderbilt: A short walk."],
              "bestTimeToVisit": "Late morning to avoid the heaviest lunch crowds.",
              "crowdLevel": "Medium (5/10), High (7/10) at lunch.",
              "accessibility": "rating: 10/10 — flat, paved paths throughout.",
              "idealDuration": "20 to 40 minutes."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Bryant Park"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Bryant Park"].tags), AdventurePaceScore = AdventureScore(mapping["Bryant Park"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Bryant Park"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Bryant Park"].tags), Latitude = 40.7536, Longitude = -73.9832, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dNYPL, DestinationName = "New York Public Library", CleanNormalizedSearchName = "new york public library", MetaphoneCode = "NY PBLK LBRR", DoubleMetaphonePrimary = "NY PPLK LPRR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The Beaux-Arts main branch (Stephen A. Schwarzman Building), guarded by the famous marble lions Patience and Fortitude, houses the ornate Rose Main Reading Room — one of the most striking free interiors in Manhattan, open to the public without a ticket.",
              "directions": "addressForRideshare: 476 Fifth Ave, New York, NY 10018, at 42nd St.",
              "whatToKnow": "freeEntry: General entry and access to the Rose Main Reading Room is free.\nGuidedTours: Free docent-led tours run regularly — check the schedule for a specific time slot.",
              "thingsToBeWaryOf": "QuietSpaceEtiquette: The Reading Room is a working library space — keep noise down.",
              "localPerspective": "A genuine working research library as much as a landmark — locals and students actually use it, not purely a photo stop.",
              "hiddenCost": "free.",
              "nearbyComplements": ["Bryant Park: Directly behind the building.", "SUMMIT One Vanderbilt: A short walk."],
              "bestTimeToVisit": "Late morning on a weekday for a quieter Reading Room visit.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 9/10 — elevator access; grand staircases have ramp alternatives.",
              "idealDuration": "30 to 45 minutes."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["New York Public Library"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["New York Public Library"].tags), AdventurePaceScore = AdventureScore(mapping["New York Public Library"].tags), AestheticTrendScore = AestheticTrendScore(mapping["New York Public Library"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["New York Public Library"].tags), Latitude = 40.7532, Longitude = -73.9822, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dGCT, DestinationName = "Grand Central Terminal", CleanNormalizedSearchName = "grand central terminal", MetaphoneCode = "KRNT SNTRL TRMNL", DoubleMetaphonePrimary = "KRNT SNTRL TRMNL", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A Beaux-Arts masterpiece and the world's largest train station by number of platforms, Grand Central's main concourse — with its celestial ceiling mural and the iconic four-faced clock — is a free architectural landmark in its own right, and also the closest transit hub to SUMMIT One Vanderbilt (which shares an entrance point via the Vanderbilt Passage).",
              "directions": "addressForRideshare: 89 E 42nd St, New York, NY 10017.",
              "whatToKnow": "freeToVisit: The main concourse is open and free to explore even without taking a train.\nDiningConcourse: A lower-level food hall (the Dining Concourse) offers a wide range of quick-bite options.\nsummitEntrance: SUMMIT One Vanderbilt's entrance is accessible via the Vanderbilt Passage from the main concourse.",
              "thingsToBeWaryOf": "RushHourCrowds: Weekday commuter rush hours (roughly 7-9:30 AM and 4:30-7 PM) are extremely dense.",
              "localPerspective": "Commuters pass through daily without a second glance at the ceiling mural — a classic case of a world-famous landmark hiding in plain sight of its own daily users.",
              "hiddenCost": "free to visit.\nDiningConcourseFood: $8-$18 per item.",
              "nearbyComplements": ["SUMMIT One Vanderbilt: Shares an entrance via Vanderbilt Passage.", "Bryant Park and NYPL: A short walk."],
              "bestTimeToVisit": "Midday, outside rush hour windows, for the calmest photos of the concourse.",
              "crowdLevel": "High (7/10) midday, Maximum (10/10) at rush hour.",
              "accessibility": "rating: 9/10 — elevators and ramps throughout the main concourse.",
              "idealDuration": "20 to 30 minutes."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Grand Central Terminal"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Grand Central Terminal"].tags), AdventurePaceScore = AdventureScore(mapping["Grand Central Terminal"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Grand Central Terminal"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Grand Central Terminal"].tags), Latitude = 40.7527, Longitude = -73.9772, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dTimesSq, DestinationName = "Times Square", CleanNormalizedSearchName = "times square", MetaphoneCode = "TMS SKR", DoubleMetaphonePrimary = "TMS SKR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The neon-saturated heart of Midtown, permanently crowded and permanently lit — the epicenter of the Broadway Theater District and a defining, if chaotic, NYC photo stop.",
              "directions": "addressForRideshare: Broadway & 7th Ave, New York, NY 10036 (roughly W 42nd to W 47th St).",
              "whatToKnow": "freeToVisit: No cost to walk through; the TKTS booth here sells same-day discounted Broadway tickets.\ncostumedCharacters: Street performers and costumed characters will ask for tips for photos — this is optional, not required.",
              "thingsToBeWaryOf": "PickpocketRisk: Dense crowds mean standard big-city caution applies — keep bags secured.\nOverstimulation: Extremely loud, bright, and crowded — plan a shorter visit if that's not your scene.",
              "localPerspective": "Most New Yorkers actively avoid it day-to-day — it's built entirely for visitors, which is fine, just don't expect a 'local' experience here.",
              "hiddenCost": "free to visit; individual shopping/dining varies widely.",
              "nearbyComplements": ["Broadway Theater District: Overlapping footprint.", "Bryant Park: A short walk."],
              "bestTimeToVisit": "Evening for the full neon effect, though it's crowded at any hour.",
              "crowdLevel": "Maximum (10/10), essentially always.",
              "accessibility": "rating: 8/10 — flat, wide pedestrian plazas, though extremely dense.",
              "idealDuration": "20 to 40 minutes."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Times Square"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Times Square"].tags), AdventurePaceScore = AdventureScore(mapping["Times Square"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Times Square"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Times Square"].tags), Latitude = 40.7580, Longitude = -73.9855, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dBroadway, DestinationName = "Broadway Theater District", CleanNormalizedSearchName = "broadway theater district", MetaphoneCode = "PRTW 0TR TSTRKT", DoubleMetaphonePrimary = "PRTW TTR TSTRKT", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A cluster of roughly 41 professional theaters around Times Square staging the world's most prestigious commercial theater productions — musicals, plays, and everything between, with same-day discount options at the TKTS booth for the budget-conscious.",
              "directions": "addressForRideshare: Centered around Broadway & 7th Ave, roughly W 41st to W 54th St.",
              "whatToKnow": "sameDayDiscounts: The TKTS booth in Times Square sells discounted same-day tickets — arrive early for the best selection.\nshowTimingWithMatchDay: Most evening shows start around 7-8 PM; if you're also headed to a World Cup match at MetLife the same day, this realistically only works before a match, not after one that runs into the evening.",
              "thingsToBeWaryOf": "SoldOutHits: The most popular current shows can sell out well in advance — don't count on TKTS availability for the hottest tickets.",
              "localPerspective": "Broadway remains a genuine economic and cultural engine for the city, not just a tourist activity — many productions run for years and become part of the city's identity.",
              "hiddenCost": "ticketPrice: Roughly $50-$200+ depending on show and seat, with premium seats for hit shows running considerably higher.\nTKTSDiscount: Typically 20-50% off face value for same-day tickets.",
              "nearbyComplements": ["Times Square: Overlapping footprint.", "Restaurant Row (W 46th St): A short walk for pre-theater dining."],
              "bestTimeToVisit": "Only realistic to pair with a match day if the show is a matinee or your kickoff is later in the evening.",
              "crowdLevel": "High (7/10) around show times.",
              "accessibility": "rating: 7/10 — accessibility varies by individual theater; many older venues have limited options.",
              "idealDuration": "2.5 to 3 hours for a full show, including arrival."
            }
            """,
            AverageCostPerDay = 120m, LuxuryRating = DeriveLuxury(120m, mapping["Broadway Theater District"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Broadway Theater District"].tags), AdventurePaceScore = AdventureScore(mapping["Broadway Theater District"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Broadway Theater District"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Broadway Theater District"].tags), Latitude = 40.7590, Longitude = -73.9845, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dOWO, DestinationName = "One World Observatory", CleanNormalizedSearchName = "one world observatory", MetaphoneCode = "ON WRLT OPSRFTR", DoubleMetaphonePrimary = "ON WRLT OPSRFTR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Atop One World Trade Center, the tallest building in the Western Hemisphere, this observatory on floors 100-102 uses high-speed 'Sky Pod' elevators with a video time-lapse of NYC's growth playing during the 47-second ascent, followed by 360-degree views over the harbor, Statue of Liberty, and both rivers.",
              "directions": "addressForRideshare: 285 Fulton St, New York, NY 10007.",
              "whatToKnow": "tickets: Check current pricing; typically comparable to other major decks (roughly $40+).\nseeForeverTheater: An immersive short film before the elevator ride sets up the city's history.",
              "thingsToBeWaryOf": "TimedEntry: Book in advance, especially in peak summer season.",
              "localPerspective": "Positioned as much as a symbol of the World Trade Center site's rebuilding as a standard observation deck — many visitors pair it directly with the 9/11 Memorial & Museum next door.",
              "hiddenCost": "generalAdmission: Roughly $40+, check current pricing.",
              "nearbyComplements": ["9/11 Memorial & Museum: Immediately adjacent.", "The Oculus: A short walk."],
              "bestTimeToVisit": "Late afternoon into sunset for harbor views.",
              "crowdLevel": "High (7/10).",
              "accessibility": "rating: 9/10 — fully modern, elevator-based access.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 40m, LuxuryRating = DeriveLuxury(40m, mapping["One World Observatory"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["One World Observatory"].tags), AdventurePaceScore = AdventureScore(mapping["One World Observatory"].tags), AestheticTrendScore = AestheticTrendScore(mapping["One World Observatory"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["One World Observatory"].tags), Latitude = 40.7127, Longitude = -74.0134, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = d911, DestinationName = "9/11 Memorial & Museum", CleanNormalizedSearchName = "911 memorial museum", MetaphoneCode = "911 MMRL MSM", DoubleMetaphonePrimary = "911 MMRL MSM", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Twin reflecting pools mark the footprints of the original Twin Towers, inscribed with the names of nearly 3,000 victims of the September 11, 2001 and 1993 World Trade Center attacks. The adjacent museum houses artifacts, oral histories, and exhibits documenting the day and its aftermath in sobering detail.",
              "directions": "addressForRideshare: 180 Greenwich St, New York, NY 10007.",
              "whatToKnow": "memorialIsFree: The outdoor memorial plaza with the reflecting pools is free and open to the public.\nmuseumTicketed: The museum requires a separate paid ticket; check current pricing and hours.",
              "thingsToBeWaryOf": "EmotionalWeight: This is a genuinely heavy historical site — plan accordingly and allow real time to process the exhibits rather than rushing through.",
              "localPerspective": "New Yorkers consider it one of the city's most solemn essential visits — locals who lived through the day often describe visiting as a personally significant, not purely touristic, experience.",
              "hiddenCost": "memorialPlaza: Free.\nMuseumAdmission: Check current pricing, typically $30+ for adults.",
              "nearbyComplements": ["One World Observatory: Immediately adjacent.", "The Oculus: A short walk."],
              "bestTimeToVisit": "Morning on a weekday for a quieter, more contemplative visit.",
              "crowdLevel": "Medium (5/10) at the outdoor memorial, High (7/10) inside the museum.",
              "accessibility": "rating: 9/10 — modern, fully accessible plaza and museum.",
              "idealDuration": "30 minutes for the memorial plaza alone; 2-3 hours including the museum."
            }
            """,
            AverageCostPerDay = 30m, LuxuryRating = DeriveLuxury(30m, mapping["9/11 Memorial & Museum"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["9/11 Memorial & Museum"].tags), AdventurePaceScore = AdventureScore(mapping["9/11 Memorial & Museum"].tags), AestheticTrendScore = AestheticTrendScore(mapping["9/11 Memorial & Museum"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["9/11 Memorial & Museum"].tags), Latitude = 40.7115, Longitude = -74.0134, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dOculus, DestinationName = "The Oculus", CleanNormalizedSearchName = "the oculus", MetaphoneCode = "0 OKLS", DoubleMetaphonePrimary = "T OKLS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Santiago Calatrava's striking, rib-like white transit hub above the WTC PATH station — part functioning train station, part upscale shopping mall (Westfield World Trade Center), and part architectural landmark in its own right. The interior's soaring skeletal ceiling opens on September 11th each year to admit natural light directly onto the memorial below.",
              "directions": "addressForRideshare: 185 Greenwich St, New York, NY 10006, connected to the WTC PATH station.",
              "whatToKnow": "freeToVisit: No cost to walk through and admire the architecture.\ntransitHub: Also a working PATH station connecting to Hoboken and Jersey City — genuinely useful, not just decorative.",
              "thingsToBeWaryOf": "BusyDuringCommuteHours: As an active transit hub, expect real foot traffic during weekday rush hours.",
              "localPerspective": "New Yorkers appreciate it as one of the few genuinely striking pieces of modern civic architecture built downtown post-9/11 — often photographed even by people just passing through on their commute.",
              "hiddenCost": "free to visit the architecture; shopping costs vary by store.",
              "nearbyComplements": ["9/11 Memorial & Museum: A short walk.", "One World Observatory: A short walk."],
              "bestTimeToVisit": "Midday on a weekend for the fullest light without peak commuter crowds.",
              "crowdLevel": "Medium (5/10), High (7/10) at commute hours.",
              "accessibility": "rating: 9/10 — modern, fully accessible transit hub.",
              "idealDuration": "20 to 30 minutes."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["The Oculus"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["The Oculus"].tags), AdventurePaceScore = AdventureScore(mapping["The Oculus"].tags), AestheticTrendScore = AestheticTrendScore(mapping["The Oculus"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["The Oculus"].tags), Latitude = 40.7115, Longitude = -74.0099, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dSoL, DestinationName = "Statue of Liberty", CleanNormalizedSearchName = "statue of liberty", MetaphoneCode = "STT OF LPRT", DoubleMetaphonePrimary = "STT OF LPRT", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A gift from France dedicated in 1886, Lady Liberty stands on her own island in New York Harbor — visible for free from Battery Park or the Staten Island Ferry, but best experienced up close via the official ferry, with options to access the pedestal or climb to the crown (very limited, book far ahead).",
              "directions": "addressForRideshare: Battery Park, New York, NY 10004, for the ferry departure point (Statue Cruises).",
              "whatToKnow": "ferryRequired: Only the official Statue Cruises ferry from Battery Park (or Liberty State Park, NJ) provides access to the island; tickets include round-trip transport and generally combine with Ellis Island.\ncrownAccess: Crown tickets are extremely limited and must be booked months in advance.\nfreeAlternative: The Staten Island Ferry passes near the statue for free, without landing.",
              "thingsToBeWaryOf": "SecurityScreening: Airport-style security screening is required before boarding the ferry — arrive with time to spare.\nWeatherAndWind: The harbor crossing can be windy and cool even on a warm day.",
              "localPerspective": "Many lifelong New Yorkers have never actually landed on Liberty Island, relying instead on the free Staten Island Ferry pass-by view — the full visit is treated as more of a deliberate, planned outing.",
              "hiddenCost": "ferryAndPedestalAccess: Roughly $24-$30 for adults, combined with Ellis Island.\nCrownAccess: Small additional fee, subject to extremely limited availability.",
              "nearbyComplements": ["Ellis Island: Included on the same ferry route.", "Battery Park: The departure point."],
              "bestTimeToVisit": "Morning for shorter lines and cooler harbor conditions.",
              "crowdLevel": "High (7/10) in summer.",
              "accessibility": "rating: 7/10 — ferry and pedestal are accessible; crown access involves stairs and is not wheelchair accessible.",
              "idealDuration": "3 to 4 hours including ferry time and both islands."
            }
            """,
            AverageCostPerDay = 27m, LuxuryRating = DeriveLuxury(27m, mapping["Statue of Liberty"].tags), AccessibilityType = "Ferry",
            FamilyFriendlyScore = FamilyScore(mapping["Statue of Liberty"].tags), AdventurePaceScore = AdventureScore(mapping["Statue of Liberty"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Statue of Liberty"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Statue of Liberty"].tags), Latitude = 40.6892, Longitude = -74.0445, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dEllis, DestinationName = "Ellis Island", CleanNormalizedSearchName = "ellis island", MetaphoneCode = "ALS ALNT", DoubleMetaphonePrimary = "ALS ALNT", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The primary U.S. immigration processing station from 1892-1954, through which over 12 million immigrants passed. The restored Great Hall and the National Museum of Immigration tell that history in detail, and many visitors can search historical passenger records for their own family names.",
              "directions": "addressForRideshare: Included on the same Statue Cruises ferry route from Battery Park (or Liberty State Park, NJ).",
              "whatToKnow": "sameFerryTicket: Included with the standard Statue of Liberty ferry ticket — no separate purchase needed.\nfamilyRecordsSearch: The museum offers access to searchable historical immigration records.",
              "thingsToBeWaryOf": "TimeManagement: Between the ferry, both islands, and security screening, a full visit realistically takes half a day — don't underestimate the time needed.",
              "localPerspective": "For many American families, this carries genuine personal weight — a significant number of New Yorkers can trace direct ancestry through this specific building.",
              "hiddenCost": "includedWithFerryTicket: No separate charge beyond the Statue of Liberty ferry/pedestal ticket.",
              "nearbyComplements": ["Statue of Liberty: Same ferry route.", "Battery Park: The departure point."],
              "bestTimeToVisit": "Combine with an early Statue of Liberty ferry departure.",
              "crowdLevel": "Medium (5/10), High (7/10) in summer.",
              "accessibility": "rating: 8/10 — the Great Hall and main museum areas are wheelchair accessible.",
              "idealDuration": "1.5 to 2 hours, as part of the combined half-day ferry trip."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Ellis Island"].tags), AccessibilityType = "Ferry",
            FamilyFriendlyScore = FamilyScore(mapping["Ellis Island"].tags), AdventurePaceScore = AdventureScore(mapping["Ellis Island"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Ellis Island"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Ellis Island"].tags), Latitude = 40.6995, Longitude = -74.0396, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dBB, DestinationName = "Brooklyn Bridge", CleanNormalizedSearchName = "brooklyn bridge", MetaphoneCode = "BRKLN BRTJ", DoubleMetaphonePrimary = "PRKLN PRTJ", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Completed in 1883, this Gothic Revival suspension bridge connecting Lower Manhattan to Brooklyn remains one of NYC's most iconic free walks — a wooden-planked pedestrian promenade elevated above traffic, with sweeping views of both skylines, the harbor, and the Statue of Liberty in the distance.",
              "directions": "addressForRideshare: Manhattan entrance near City Hall (Centre St & Park Row); Brooklyn entrance near Cadman Plaza.",
              "whatToKnow": "walkDirection: Walking from Manhattan to Brooklyn is generally preferred for the DUMBO/Brooklyn Bridge Park payoff at the end.\nfreeAndOpen: No cost, no ticket — just walk across.",
              "thingsToBeWaryOf": "BikeLaneConflicts: A separate bike lane runs alongside the pedestrian path — stay in your designated lane, as collisions with distracted photographers are a real, recurring issue.\nCrowdedMidday: Peak midday hours get genuinely dense with tourists.",
              "localPerspective": "Commuting cyclists use this bridge daily and get visibly frustrated with pedestrians wandering into the bike lane for photos — a small but real point of local friction worth being mindful of.",
              "hiddenCost": "free.",
              "nearbyComplements": ["DUMBO: Directly at the Brooklyn end.", "City Hall Park: At the Manhattan end."],
              "bestTimeToVisit": "Early morning or just before sunset for the best light and thinner crowds.",
              "crowdLevel": "High (7/10) midday, Medium (5/10) early morning.",
              "accessibility": "rating: 7/10 — a gentle, paved incline; manageable for most, though the full length is a real walk.",
              "idealDuration": "30 to 45 minutes one-way."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Brooklyn Bridge"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Brooklyn Bridge"].tags), AdventurePaceScore = AdventureScore(mapping["Brooklyn Bridge"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Brooklyn Bridge"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Brooklyn Bridge"].tags), Latitude = 40.7061, Longitude = -73.9969, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dDumbo, DestinationName = "DUMBO", CleanNormalizedSearchName = "dumbo", MetaphoneCode = "TMB", DoubleMetaphonePrimary = "TMP", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "'Down Under the Manhattan Bridge Overpass' — a converted warehouse district turned one of Brooklyn's most photographed neighborhoods, anchored by the famous Washington Street view framing the Manhattan Bridge between cobblestone rowhouses, plus the Juliana's/Grimaldi's pizza rivalry along the waterfront.",
              "directions": "addressForRideshare: Washington St & Water St, Brooklyn, NY 11201.",
              "whatToKnow": "iconicPhotoSpot: The Washington Street/Manhattan Bridge shot is one of the most-recreated photos in NYC — expect a small line of people waiting to take it at peak times.\nwalkableFromBridge: Sits directly at the Brooklyn end of the Brooklyn Bridge walk.",
              "thingsToBeWaryOf": "CobblestoneStreets: Uneven, historic cobblestones throughout — comfortable shoes recommended.",
              "localPerspective": "Once a genuinely industrial warehouse district, DUMBO's transformation into a high-end residential and gallery neighborhood is one of Brooklyn's most-cited gentrification case studies — locals have mixed feelings about how touristy the photo spot has become.",
              "hiddenCost": "free to walk; individual shop/restaurant spending varies.",
              "nearbyComplements": ["Brooklyn Bridge: Directly adjacent.", "Brooklyn Bridge Park: A short walk.", "Juliana's Pizza: In the neighborhood."],
              "bestTimeToVisit": "Early morning for the famous photo spot without a line.",
              "crowdLevel": "High (7/10), especially at the photo spot.",
              "accessibility": "rating: 6/10 — cobblestone streets in parts; main sidewalks are manageable.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 10m, LuxuryRating = DeriveLuxury(10m, mapping["DUMBO"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["DUMBO"].tags), AdventurePaceScore = AdventureScore(mapping["DUMBO"].tags), AestheticTrendScore = AestheticTrendScore(mapping["DUMBO"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["DUMBO"].tags), Latitude = 40.7033, Longitude = -73.9894, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dBBP, DestinationName = "Brooklyn Bridge Park", CleanNormalizedSearchName = "brooklyn bridge park", MetaphoneCode = "BRKLN BRTJ PRK", DoubleMetaphonePrimary = "PRKLN PRTJ PRK", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "An 85-acre waterfront park stretching along the Brooklyn shoreline beneath the bridge, offering some of the best straight-on views of the Lower Manhattan skyline anywhere in the city — a genuinely great spot to sit down with a pizza-to-go and watch the harbor traffic.",
              "directions": "addressForRideshare: Old Fulton St & Furman St, Brooklyn, NY 11201, at the DUMBO waterfront.",
              "whatToKnow": "freeAndOpen: No cost, open year-round.\npizzaAndPark: A well-known local move is grabbing a slice or pie from Juliana's or Grimaldi's and eating it in the park with the skyline view.",
              "thingsToBeWaryOf": "WeekendCrowds: Warm-weather weekends draw large crowds, especially near Pier 1 and the carousel.",
              "localPerspective": "Brooklynites treat this as their default 'show visiting friends the skyline' spot — arguably a better view than most paid observation decks, and completely free.",
              "hiddenCost": "free.",
              "nearbyComplements": ["DUMBO: Directly adjacent.", "Juliana's Pizza: A short walk.", "Brooklyn Bridge: A short walk."],
              "bestTimeToVisit": "Golden hour before sunset for the best skyline light.",
              "crowdLevel": "Medium (5/10), High (7/10) on warm weekends.",
              "accessibility": "rating: 9/10 — flat, paved waterfront paths.",
              "idealDuration": "30 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Brooklyn Bridge Park"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Brooklyn Bridge Park"].tags), AdventurePaceScore = AdventureScore(mapping["Brooklyn Bridge Park"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Brooklyn Bridge Park"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Brooklyn Bridge Park"].tags), Latitude = 40.7024, Longitude = -73.9903, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dJulianas, DestinationName = "Juliana's Pizza", CleanNormalizedSearchName = "julianas pizza", MetaphoneCode = "JLNS PS", DoubleMetaphonePrimary = "JLNS PS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Founded in 2012 by Patsy Grimaldi — the same Patsy who founded the original Grimaldi's next door before selling that name and business in 1999 — Juliana's occupies Grimaldi's original 19 Old Fulton Street location and, according to many longtime fans, makes the more authentic version of that original coal-fired pie. Named after Patsy's mother, Maria 'Juliana' Lancieri.",
              "directions": "addressForRideshare: 19 Old Fulton St, Brooklyn, NY 11201, Fulton Ferry Historic District, DUMBO.",
              "whatToKnow": "insiderTip: Call ahead for pickup, then eat your pizza in Brooklyn Bridge Park under the bridge — genuinely better than fighting for the small, often-crowded indoor seating.\nsignatureOrder: The classic Margherita is the benchmark order; the No. 4 (tomato, mozzarella, arugula, prosciutto) is also frequently recommended.\nwholePiesOnly: Coal-fired thin crust, generally sold as whole pies rather than individual slices.",
              "thingsToBeWaryOf": "RealLines: Saturday evenings especially draw a genuinely long wait — arriving right at opening or calling ahead for pickup avoids most of it.\nSmallDiningRoom: The indoor space is cozy and can feel crowded when full.",
              "localPerspective": "There's a well-known, good-natured local rivalry between Juliana's and neighboring Grimaldi's (which Patsy no longer owns) — regulars have strong, specific opinions about dough, sauce, and which is the 'real' Grimaldi's experience now.",
              "hiddenCost": "wholePie: $25-$35 depending on size and toppings.\nSides: $6-$12.",
              "nearbyComplements": ["Brooklyn Bridge Park: A short walk for the take-it-to-the-park move.", "DUMBO: Same neighborhood.", "Grimaldi's: Right next door, for comparison."],
              "bestTimeToVisit": "Right at opening, or call ahead for pickup to skip the line entirely.",
              "crowdLevel": "High (7/10), especially Saturday evenings.",
              "accessibility": "rating: 6/10 — small, cozy dining room; can be tight when full.",
              "idealDuration": "45 minutes to 1 hour, including wait."
            }
            """,
            AverageCostPerDay = 22m, LuxuryRating = DeriveLuxury(22m, mapping["Juliana's Pizza"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Juliana's Pizza"].tags), AdventurePaceScore = AdventureScore(mapping["Juliana's Pizza"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Juliana's Pizza"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Juliana's Pizza"].tags), Latitude = 40.7025, Longitude = -73.9936, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dPrince, DestinationName = "Prince Street Pizza", CleanNormalizedSearchName = "prince street pizza", MetaphoneCode = "PRNS STRT PS", DoubleMetaphonePrimary = "PRNS STRT PS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A NoLIta institution famous for near-constant lines and its signature Spicy Spring — Sicilian square pizza with tomato sauce, fresh mozzarella, spicy pepperoni cups, and pecorino romano. Once frequented by organized-crime figures, it's since become an internationally known must-visit, with celebrity photos covering the walls.",
              "directions": "addressForRideshare: 27 Prince St, New York, NY 10012, NoLIta.",
              "whatToKnow": "signatureOrder: The Spicy Spring Sicilian slice, with its charred, crispy pepperoni cups, is the reason most people visit.\nconsistency: Some regulars note the quality can vary day to day — a genuinely good visit is excellent, but it's not universally flawless every time.",
              "thingsToBeWaryOf": "PersistentLines: The line here rarely disappears entirely, even outside peak hours — budget real wait time.",
              "localPerspective": "Once a low-key neighborhood spot with an underworld reputation, it's now a bucket-list stop for pizza tourists worldwide — a genuine transformation locals have watched happen in real time.",
              "hiddenCost": "slice: $5-$7.\nWholePie: $25-$32.",
              "nearbyComplements": ["Lombardi's: A short walk in Little Italy.", "SoHo shopping district: Immediately surrounding."],
              "bestTimeToVisit": "Off-peak weekday afternoon for a shorter (though rarely non-existent) line.",
              "crowdLevel": "High (7/10) most of the day.",
              "accessibility": "rating: 6/10 — small storefront, mostly standing/takeaway.",
              "idealDuration": "20 to 40 minutes including the line."
            }
            """,
            AverageCostPerDay = 10m, LuxuryRating = DeriveLuxury(10m, mapping["Prince Street Pizza"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Prince Street Pizza"].tags), AdventurePaceScore = AdventureScore(mapping["Prince Street Pizza"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Prince Street Pizza"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Prince Street Pizza"].tags), Latitude = 40.7233, Longitude = -73.9958, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dLombardis, DestinationName = "Lombardi's", CleanNormalizedSearchName = "lombardis", MetaphoneCode = "LMPRTS", DoubleMetaphonePrimary = "LMPRTS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "America's first licensed pizzeria, opened by Gennaro Lombardi in 1905 in Little Italy — the founding restaurant of New York-style pizza itself: dark, thick coal-oven crust, mozzarella, tomato sauce, and basil. No slices, whole pies only, in an interior of red-and-white checked tablecloths and rustic murals that hasn't changed much since it opened.",
              "directions": "addressForRideshare: 32 Spring St, New York, NY 10012, Little Italy (note: the original 1905 location was a few blocks away; the current spot has operated at this address for decades).",
              "whatToKnow": "signatureOrder: The original Margherita (fresh mozzarella, tomato sauce, romano, basil); the Clam Pie (shucked clams, garlic, oregano, romano, black pepper, parsley) is the adventurous alternative.\nwholePiesOnly: No individual slices — order a whole pie per group, or one each if you're hungry.",
              "thingsToBeWaryOf": "TouristPerception: Some visitors dismiss it as a tourist trap resting on historical fame — most food writers who've been disagree, but set expectations for a historic rather than cutting-edge experience.",
              "localPerspective": "Considered the literal origin point of New York-style pizza — food historians and pizza obsessives treat a visit here as visiting the source, not just another good pizzeria.",
              "hiddenCost": "wholePie: $22-$32 depending on toppings.",
              "nearbyComplements": ["Prince Street Pizza: A short walk.", "Little Italy's Mulberry Street: Immediately surrounding."],
              "bestTimeToVisit": "Lunch on a weekday for a calmer, more historic-feeling visit.",
              "crowdLevel": "High (7/10), especially weekend dinners.",
              "accessibility": "rating: 7/10 — classic, older restaurant layout with standard seating.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 26m, LuxuryRating = DeriveLuxury(26m, mapping["Lombardi's"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Lombardi's"].tags), AdventurePaceScore = AdventureScore(mapping["Lombardi's"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Lombardi's"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Lombardi's"].tags), Latitude = 40.7223, Longitude = -73.9955, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dJohns, DestinationName = "John's of Bleecker Street", CleanNormalizedSearchName = "johns of bleecker street", MetaphoneCode = "JNS OF BLKR STRT", DoubleMetaphonePrimary = "JNS OF PLKR STRT", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A Greenwich Village landmark dating to 1929 (the location itself traces to 1915), John's coal-fired brick oven has been continuously burning for close to a century. No slices — only whole pies (14\" or 16\") and calzones, baked at 850°F, plus the original graffiti-carved wooden booths where patrons can carve their own names.",
              "directions": "addressForRideshare: 278 Bleecker St, New York, NY 10014, between 6th and 7th Ave.",
              "whatToKnow": "signatureOrder: 'John's Original' (mozzarella, tomato sauce) is the foundation; the 'Sasso' adds parmigiano, oregano, and black pepper.\nnoSlices: Whole pies only, cooked at 850°F in the coal-fired oven.\ncashHistory: Cash-only until May 2016 — now accepts cards, but the coal-oven tradition itself remains unchanged.",
              "thingsToBeWaryOf": "GroupSizing: Since it's whole pies only, this works best with 2+ people so you're not stuck eating an entire pie solo.\nWeekendWaits: A short wait is common even for large groups on weekend evenings.",
              "localPerspective": "One of the most consistently ranked pizzerias in the U.S. by outlets like TripAdvisor and USA Today — locals treat the carved wooden booths as a genuine piece of only-in-New-York history.",
              "hiddenCost": "wholePie14Inch: $22-$28.\nWholePie16Inch: $28-$36.\nCalzone: $14-$18.",
              "nearbyComplements": ["Joe's Pizza: A short walk.", "Washington Square Park: A short walk."],
              "bestTimeToVisit": "Early dinner on a weekday for the shortest wait.",
              "crowdLevel": "High (7/10) weekend evenings, Medium (5/10) weekday lunch.",
              "accessibility": "rating: 6/10 — classic, older building layout; wooden booths are fixed seating.",
              "idealDuration": "1 hour."
            }
            """,
            AverageCostPerDay = 25m, LuxuryRating = DeriveLuxury(25m, mapping["John's of Bleecker Street"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["John's of Bleecker Street"].tags), AdventurePaceScore = AdventureScore(mapping["John's of Bleecker Street"].tags), AestheticTrendScore = AestheticTrendScore(mapping["John's of Bleecker Street"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["John's of Bleecker Street"].tags), Latitude = 40.7317, Longitude = -74.0027, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dBKBoys, DestinationName = "Brooklyn Boys Pizza & Deli", CleanNormalizedSearchName = "brooklyn boys pizza deli", MetaphoneCode = "BRKLN BS PS TL", DoubleMetaphonePrimary = "PRKLN PS PS TL", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "important: despite the name, Brooklyn Boys is actually a family-owned Italian pizzeria and deli in Edison, New Jersey, not Brooklyn — a 20-plus-year local institution known for thin, Brooklyn-style pies made with fresh plum tomato sauce rather than canned, plus hot/cold subs, calzones, and stromboli.",
              "directions": "addressForRideshare: 9 Lincoln Hwy, Edison, NJ 08820.",
              "whatToKnow": "signatureItems: Classic Margherita and tomato pies, plus a notable chicken pizza topped with onion rings and a 'nice and clean' white pizza.\nfamilyFriendly: Casual, counter-plus-seating setup, good for groups and families.",
              "thingsToBeWaryOf": "NotInBrooklyn: A genuinely common point of confusion given the name — plan the drive to Edison, not a Brooklyn neighborhood.\nClosedSundays: Closed on Sundays at this location — check current hours before visiting.",
              "localPerspective": "A genuine 20+ year Edison institution — locals describe it as a 'go-to spot for families,' with the founders having set out in the late '90s to bring an authentic Brooklyn-style pizza experience to central Jersey.",
              "hiddenCost": "wholePie: $16-$24.\nSubs: $10-$15.\nCalzones/Stromboli: $10-$14.",
              "nearbyComplements": ["Menlo Park Mall area: Nearby in Edison.", "Metuchen: A short drive."],
              "bestTimeToVisit": "Lunch or early dinner Monday-Saturday.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 8/10 — standard casual-dining accessibility.",
              "idealDuration": "45 minutes."
            }
            """,
            AverageCostPerDay = 16m, LuxuryRating = DeriveLuxury(16m, mapping["Brooklyn Boys Pizza & Deli"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Brooklyn Boys Pizza & Deli"].tags), AdventurePaceScore = AdventureScore(mapping["Brooklyn Boys Pizza & Deli"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Brooklyn Boys Pizza & Deli"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Brooklyn Boys Pizza & Deli"].tags), Latitude = 40.5372, Longitude = -74.3646, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dRazza, DestinationName = "Razza", CleanNormalizedSearchName = "razza", MetaphoneCode = "RS", DoubleMetaphonePrimary = "RS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "An artisanal, ingredient-obsessed pizzeria in Jersey City, frequently cited among the best pizza in the entire NYC metro area (not just New Jersey) — known for house-milled flour, a naturally leavened dough, and a wood-fired, blistered crust that draws devoted pilgrimage-style visits from Manhattan.",
              "directions": "addressForRideshare: Jersey City, NJ — check current exact address, easily reached via PATH.",
              "whatToKnow": "artisanalFocus: House-made ingredients (including cultured butter and house-milled flour) set this apart from a standard NY-style slice shop — it's a sit-down, ingredient-driven experience rather than a quick counter stop.\nreservationsRecommended: Given its reputation, booking ahead is worth it, especially on weekends.",
              "thingsToBeWaryOf": "HigherPriceForPizza: This runs noticeably more expensive than a typical NY slice — it's a different category of experience, priced accordingly.\nLimitedSeating: A relatively small, sought-after dining room.",
              "localPerspective": "Frequently mentioned in the same breath as Manhattan's most acclaimed pizzerias despite sitting across the river — a genuine point of Jersey City pride.",
              "hiddenCost": "wholePie: $22-$30.\nAppetizers/Salads: $10-$18.",
              "nearbyComplements": ["Roman Gourmet: Also in Jersey City.", "PATH stations: Easy access back to Manhattan."],
              "bestTimeToVisit": "Weeknight dinner with a reservation for the calmest experience.",
              "crowdLevel": "High (7/10), especially weekends.",
              "accessibility": "rating: 8/10 — standard sit-down restaurant accessibility.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 26m, LuxuryRating = DeriveLuxury(26m, mapping["Razza"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Razza"].tags), AdventurePaceScore = AdventureScore(mapping["Razza"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Razza"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Razza"].tags), Latitude = 40.7220, Longitude = -74.0445, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dRoman, DestinationName = "Roman Gourmet", CleanNormalizedSearchName = "roman gourmet", MetaphoneCode = "RMN KRMT", DoubleMetaphonePrimary = "RMN KRMT", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A well-reviewed Jersey City Italian spot and sandwich counter, regularly appearing on 'best New York-style pizza' lists for the city — a solid, unpretentious option distinct from Razza's more elevated, artisanal approach.",
              "directions": "addressForRideshare: Jersey City, NJ — check current exact address.",
              "whatToKnow": "styleNote: A classic NY-style pizza and Italian sandwich shop rather than a sit-down artisanal restaurant — quicker, more casual than Razza.\nsandwichReputation: Also well regarded specifically for its Italian sandwiches, not just pizza.",
              "thingsToBeWaryOf": "MixedReviewsOnSpecifics: Some reviewers note occasional inconsistency — a generally reliable stop rather than a guaranteed transcendent one.",
              "localPerspective": "A genuine neighborhood go-to for Jersey City residents wanting solid NY-style pizza without the wait or price of some of the more hyped spots nearby.",
              "hiddenCost": "slice: $3-$5.\nWholePie: $16-$22.\nSandwiches: $9-$14.",
              "nearbyComplements": ["Razza: Also in Jersey City.", "Rudy's Ristorante & Pizzeria: Also in Jersey City."],
              "bestTimeToVisit": "Lunch or casual dinner, any day.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 8/10 — standard counter-service pizzeria layout.",
              "idealDuration": "30 to 45 minutes."
            }
            """,
            AverageCostPerDay = 12m, LuxuryRating = DeriveLuxury(12m, mapping["Roman Gourmet"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Roman Gourmet"].tags), AdventurePaceScore = AdventureScore(mapping["Roman Gourmet"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Roman Gourmet"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Roman Gourmet"].tags), Latitude = 40.7230, Longitude = -74.0500, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dAmadeo, DestinationName = "Amadeo Pizzeria", CleanNormalizedSearchName = "amadeo pizzeria", MetaphoneCode = "AMT PSR", DoubleMetaphonePrimary = "AMT PSR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Secaucus's only brick-oven pizzeria and a 30-plus-year family-run institution serving hand-tossed pizza, chicken parm, and homemade Italian classics — genuinely convenient given Secaucus Junction is the mandatory rail transfer point for every NJ Transit trip to MetLife Stadium.",
              "directions": "addressForRideshare: 1536 Paterson Plank Rd, Secaucus, NJ 07094.",
              "whatToKnow": "onlyBrickOven: The only brick-oven pizzeria in Secaucus, a genuine local distinction.\nsignatureItems: The Sweet Ricotta Hot Honey Roni pizza is a local favorite; chicken parm is also frequently praised.\nhours: Closed Mondays; Tuesday-Thursday 11 AM-9 PM, Friday-Saturday 11 AM-10 PM, Sunday 12-9 PM.",
              "thingsToBeWaryOf": "TimingAroundTransit: If combining with a MetLife Stadium trip, this works well as a stop either before boarding the Meadowlands Rail shuttle at Secaucus Junction, or on the way back.",
              "localPerspective": "A genuine neighborhood fixture for over three decades — locals describe it as a family-run gem, distinct from the chain-heavy food options typically found near a major transit hub.",
              "hiddenCost": "wholePie: $16-$24.\nChickenParm: $16-$20.\nSides: $6-$10.",
              "nearbyComplements": ["Secaucus Junction: A short walk — the mandatory MetLife Stadium rail transfer point.", "Meadowlands Racing & Entertainment: A short drive."],
              "bestTimeToVisit": "Before or after a Secaucus Junction transfer en route to/from MetLife Stadium.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 8/10 — standard sit-down restaurant with outdoor seating option.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 18m, LuxuryRating = DeriveLuxury(18m, mapping["Amadeo Pizzeria"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Amadeo Pizzeria"].tags), AdventurePaceScore = AdventureScore(mapping["Amadeo Pizzeria"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Amadeo Pizzeria"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Amadeo Pizzeria"].tags), Latitude = 40.7825, Longitude = -74.0587, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dStar, DestinationName = "Star Tavern", CleanNormalizedSearchName = "star tavern", MetaphoneCode = "STR TFRN", DoubleMetaphonePrimary = "STR TFRN", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Established in 1945 in the City of Orange, 'The Star' is one of New Jersey's most decorated pizza institutions — an ultra-thin, bar-style pie that The New York Times called 'superior' and that New Jersey Monthly named the state's best pizza for nearly 20 consecutive years. Five generations of the same families have been coming here since childhood.",
              "directions": "addressForRideshare: 400 High St, City of Orange, NJ 07050.\ndistanceNote: This is genuinely a detour from the NYC/Jersey City/Secaucus core of a MetLife-focused trip — closer to Montclair/West Orange than to the stadium.",
              "whatToKnow": "signatureStyle: Ultra-thin, bar-style crust — order the plain cheese first to understand why it's considered special before adding toppings.\nnoReservations: First-come, first-served; the line moves quickly even on busy weekends.\ndessertPizza: The Apple Pie pizza (thin apples, walnuts, butter, sugar, cinnamon) is a well-regarded specialty dessert option.",
              "thingsToBeWaryOf": "CashPreferred: Some reports suggest cash is strongly preferred, and prices for very large orders can add up quickly — bring enough cash and tip well.\nRealDistance: Factor in genuine drive time if you're coming from Secaucus/Jersey City/NYC — this is closer to 30-40 minutes depending on traffic, not a quick add-on.",
              "localPerspective": "Owner Gary Vayianos, whose family bought the tavern in 1980, has said it isn't a secret ingredient that makes the pizza special — it's the consistency and the generations of regulars who've made it a genuine community fixture for 80 years.",
              "hiddenCost": "wholePie: $16-$24 (large orders can run higher, up to $100 for elaborate versions per some reviewer accounts).\nWings/Appetizers: $10-$16.",
              "nearbyComplements": ["Montclair and Glen Ridge: Nearby towns worth combining with the trip.", "West Orange: Adjacent."],
              "bestTimeToVisit": "Early evening on a weekday to avoid the heaviest weekend crowds.",
              "crowdLevel": "High (7/10) on weekends.",
              "accessibility": "rating: 7/10 — classic tavern layout with bar and table seating.",
              "idealDuration": "45 minutes to 1.5 hours."
            }
            """,
            AverageCostPerDay = 20m, LuxuryRating = DeriveLuxury(20m, mapping["Star Tavern"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Star Tavern"].tags), AdventurePaceScore = AdventureScore(mapping["Star Tavern"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Star Tavern"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Star Tavern"].tags), Latitude = 40.7712, Longitude = -74.2323, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dRudys, DestinationName = "Rudy's Ristorante & Pizzeria", CleanNormalizedSearchName = "rudys ristorante pizzeria", MetaphoneCode = "RTS RSTRNT PSR", DoubleMetaphonePrimary = "RTS RSTRNT PSR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A well-regarded Jersey City Italian restaurant and pizzeria, consistently appearing on 'best New York-style pizza in Jersey City' rankings — a full-menu Italian spot rather than a slice-only counter, good for a sit-down meal beyond just pizza.",
              "directions": "addressForRideshare: Jersey City, NJ — check current exact address.",
              "whatToKnow": "fullMenu: Offers a broader Italian menu (salads, entrees) alongside pizza, distinguishing it from quicker slice shops in the area.\nlocalReputation: Regularly cited alongside Razza and Roman Gourmet in Jersey City 'best pizza' roundups.",
              "thingsToBeWaryOf": "CheckCurrentHoursAndMenu: As with any full-service restaurant, confirm current hours before a specific visit.",
              "localPerspective": "Part of the same strong Jersey City pizza cluster that draws comparisons to Manhattan's best — a genuine point of pride for the city's food scene relative to its more famous neighbor across the Hudson.",
              "hiddenCost": "wholePie: $18-$26.\nEntrees: $16-$26.",
              "nearbyComplements": ["Razza: Also in Jersey City.", "Roman Gourmet: Also in Jersey City."],
              "bestTimeToVisit": "Dinner, any day.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 8/10 — standard full-service restaurant accessibility.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 22m, LuxuryRating = DeriveLuxury(22m, mapping["Rudy's Ristorante & Pizzeria"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Rudy's Ristorante & Pizzeria"].tags), AdventurePaceScore = AdventureScore(mapping["Rudy's Ristorante & Pizzeria"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Rudy's Ristorante & Pizzeria"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Rudy's Ristorante & Pizzeria"].tags), Latitude = 40.7250, Longitude = -74.0480, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dSals, DestinationName = "Sal's Pizza", CleanNormalizedSearchName = "sals pizza", MetaphoneCode = "SLS PS", DoubleMetaphonePrimary = "SLS PS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "important: 'Sal's Pizza' is a common name across New Jersey, and multiple unrelated pizzerias share it — including a Sal's Italian Restaurant and Pizzeria in Toms River, roughly 70 miles south of NYC/the Meadowlands, which is a genuine detour rather than a convenient add-on to a MetLife-focused trip. Confirm which specific 'Sal's' matches your route before committing, as the name alone doesn't guarantee a close, convenient stop.",
              "directions": "addressForRideshare: Varies significantly by location — verify the specific Sal's you're targeting before planning around it. One confirmed location: 1606 Route 37 E, Toms River, NJ (roughly 70 miles / 1.5+ hours from the Meadowlands area).",
              "whatToKnow": "genericNameCaveat: This is less a single confirmed destination and more a category — treat any specific 'Sal's Pizza' as needing its own verification before you build travel time around it.",
              "thingsToBeWaryOf": "DistanceMismatch: If the Toms River location is what you find, recognize it's a genuine day-trip-level detour from the NYC/NJ core of this pizza trail, not a quick stop.",
              "localPerspective": "New Jersey has many beloved, unrelated pizzerias that happen to share the name 'Sal's' — a good reminder to verify the specific address rather than assuming.",
              "hiddenCost": "wholePie: Roughly $16-$24, varies by location.",
              "nearbyComplements": ["Varies entirely by which Sal's Pizza you're visiting."],
              "bestTimeToVisit": "Only after confirming which specific location fits your actual route.",
              "crowdLevel": "Varies by location.",
              "accessibility": "rating: Varies by location.",
              "idealDuration": "45 minutes, plus verify travel time separately."
            }
            """,
            AverageCostPerDay = 18m, LuxuryRating = DeriveLuxury(18m, mapping["Sal's Pizza"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Sal's Pizza"].tags), AdventurePaceScore = AdventureScore(mapping["Sal's Pizza"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Sal's Pizza"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Sal's Pizza"].tags), Latitude = 39.9537, Longitude = -74.1979, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        new Destination { DestinationId = dWahizza, DestinationName = "Wahizza", CleanNormalizedSearchName = "wahizza", MetaphoneCode = "WHS", DoubleMetaphonePrimary = "WHS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Billed as NJ/NY's only Dominican-Italian artisan craft pizzeria, family-run since 2016 — bold Caribbean flavors meet Italian pizza-making craftsmanship, including signature 'chimi' pies. Locations include Jersey City and Englewood, NJ.",
              "directions": "addressForRideshare: Multiple NJ locations, including Jersey City and Englewood — confirm which is closer to your route.",
              "whatToKnow": "signatureStyle: Brick-oven pizzas with thin crusts, plus the distinctive Dominican-influenced 'chimi' pies not found at a standard Italian-American pizzeria.\nUniqueConcept: A genuinely different flavor profile from the rest of this pizza trail's classic NY/NJ-style stops.",
              "thingsToBeWaryOf": "CheckLocationHours: With multiple locations, confirm hours and menu specifics for the one you're visiting.",
              "localPerspective": "Reviewers frequently cite it as a standout specifically because it doesn't taste like every other pizzeria on the list — a genuine flavor detour within the trail.",
              "hiddenCost": "wholePie: $18-$26.\nChimiPie: Priced similarly, check current menu.",
              "nearbyComplements": ["Razza, Roman Gourmet, Rudy's: If visiting the Jersey City location.", "Englewood's Palisades Avenue dining strip: If visiting the Englewood location."],
              "bestTimeToVisit": "Dinner, any day — check current hours for the specific location.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 8/10 — standard casual-dining accessibility.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 20m, LuxuryRating = DeriveLuxury(20m, mapping["Wahizza"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Wahizza"].tags), AdventurePaceScore = AdventureScore(mapping["Wahizza"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Wahizza"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Wahizza"].tags), Latitude = 40.7215, Longitude = -74.0430, SearchHitCount = 0, TimeZone = "Eastern Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },
    };

            db.Destinations.AddRange(newDestinations);
            await db.SaveChangesAsync();
            var allDestinations = newDestinations.ToList();

            await AssignCategoriesAndTagsToDestinationsAsync(db, allDestinations, mapping);
            await SeedImagesForNYNJAsync(db, allDestinations);
            await SeedDestinationLanguageAndCurrencyAsync(db, allDestinations, english, usd);

            db.DestinationCities.AddRange(new[]
            {
        new DestinationCity { DestinationId = dNYPS, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dJoes, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dFF, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dKtown, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dFanVillage, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dHoboken, CityId = hoboken.CityId },
        new DestinationCity { DestinationId = dMetLife, CityId = eastRutherford.CityId },
        new DestinationCity { DestinationId = dMaddHatter, CityId = hoboken.CityId },
        new DestinationCity { DestinationId = dEdMarys, CityId = jerseyCity.CityId },
        new DestinationCity { DestinationId = dHopsScotch, CityId = jerseyCity.CityId },
        new DestinationCity { DestinationId = dESB, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dSummit, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dTotr, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dEdge, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dBryant, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dNYPL, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dGCT, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dTimesSq, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dBroadway, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dOWO, CityId = newYork.CityId },
        new DestinationCity { DestinationId = d911, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dOculus, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dSoL, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dEllis, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dBB, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dDumbo, CityId = brooklyn.CityId },
        new DestinationCity { DestinationId = dBBP, CityId = brooklyn.CityId },
        new DestinationCity { DestinationId = dJulianas, CityId = brooklyn.CityId },
        new DestinationCity { DestinationId = dPrince, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dLombardis, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dJohns, CityId = newYork.CityId },
        new DestinationCity { DestinationId = dBKBoys, CityId = edison.CityId },
        new DestinationCity { DestinationId = dRazza, CityId = jerseyCity.CityId },
        new DestinationCity { DestinationId = dRoman, CityId = jerseyCity.CityId },
        new DestinationCity { DestinationId = dAmadeo, CityId = secaucus.CityId },
        new DestinationCity { DestinationId = dStar, CityId = orange.CityId },
        new DestinationCity { DestinationId = dRudys, CityId = jerseyCity.CityId },
        new DestinationCity { DestinationId = dSals, CityId = tomsRiver.CityId },
        new DestinationCity { DestinationId = dWahizza, CityId = jerseyCity.CityId },
    });
            await db.SaveChangesAsync();

            var routeNYToMetLife = Guid.NewGuid();
            db.TransitRoutes.Add(new TransitRoute
            {
                TransitRouteId = routeNYToMetLife,
                OriginCityId = newYork.CityId,
                DestinationCityId = eastRutherford.CityId,
                TransitType = "NJ Transit + Meadowlands Rail Shuttle (no direct subway)",
                EstimatedCostPerPerson = 100m,
                DurationInMinutes = 45,
                RecommendedTimeBufferMinutes = 90,
                BookingReferenceUrl = "https://www.njtransit.com/",
                CarbonFootprintKg = "3.5",
                SubSegmentsJson = "[]"
            });
            await db.SaveChangesAsync();

            // ═══════════ WISHLIST 1: The Perfect MetLife Stadium Match Day ═══════════
            var w1Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = w1Id,
                WishlistName = "The Perfect MetLife Stadium Match Day",
                WishlistDescription = "Everything you need before and after today's World Cup match — breakfast, pre-match atmosphere in Manhattan, the trip out to New Jersey, the stadium itself (with real attendee tips baked in), and where to celebrate afterward.",
                ShortStory = "Pizza, a packed soccer bar in Koreatown, a rail transfer at Secaucus, 82,500 voices at New York New Jersey Stadium, and a Jersey City nightcap.",
                TotalDays = 1,
                PeopleType = "World Cup Fans attending a match at MetLife Stadium",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_metlife_hero.jpg",
                GlobalInclusionsJson = @"[""Match Ticket (MetLife Stadium / New York New Jersey Stadium)"",""NJ Transit Round-Trip Rail Ticket""]",
                RawContentKeywords = "MetLife Stadium, New York New Jersey Stadium, World Cup, Koreatown, Football Factory at Legends, Hoboken, Secaucus Junction, match day, soccer",
                PsychologicalVibeTagsJson = @"[""Sports Fan"",""Foodie"",""Social""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 420m,
                CalculatedTotalCost = 840m,
                DepositAmountRequired = 50m,
                AccommodationInclusions = "Day-trip itinerary — no overnight accommodation included",
                TransitInclusions = "NJ Transit rail via Secaucus Junction to the Meadowlands Rail Line shuttle; no direct subway to the stadium exists",
                ActivityInclusions = "MetLife Stadium match ticket (World Cup fixture)",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "World Cup Match Attendee",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();

            var w1Day = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "The Perfect MetLife Stadium Match Day", MorningCityId = newYork.CityId, AfternoonCityId = newYork.CityId, EveningCityId = eastRutherford.CityId, TransitFromPreviousDayRouteId = null, WishlistId = w1Id };
            db.ItineraryDays.Add(w1Day);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Breakfast: New York Pizza Suprema", ItemDescription = "Right by Penn Station, where you'll depart for New Jersey — practical as well as delicious.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_newyorkpizzasuprema.jpg", SocialProofBadge = "Convenient Classic", IndividualCostModifier = 8m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Or: Joe's Pizza (if you're downtown)", ItemDescription = "The Greenwich Village classic, better suited if your morning starts further downtown.", ItemOrderIndex = 2, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_joespizza.jpg", SocialProofBadge = "Village Icon", IndividualCostModifier = 8m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Football Factory at Legends", ItemDescription = "NYC's premier soccer bar, in Koreatown — build pre-match energy with supporters clubs from around the world.", ItemOrderIndex = 3, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_footballfactoryatlegends.jpg", SocialProofBadge = "Soccer Bar Icon", IndividualCostModifier = 30m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Koreatown", ItemDescription = "Explore the same block as Football Factory — BBQ, cafes, and late-night energy.", ItemOrderIndex = 4, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_koreatownnyc.jpg", SocialProofBadge = "Neighborhood Stroll", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "FIFA Fan Festival (Telemundo Fan Village, Rockefeller Center)", ItemDescription = "Note: the original Liberty State Park Fan Festival was cancelled — this Rockefeller Center site (knockout stages, July 4-19) is the real Manhattan option.", ItemOrderIndex = 5, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_telemundofanvillagerockefellercenter.jpg", SocialProofBadge = "Check Dates First", IndividualCostModifier = 0m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Travel to New Jersey via Hoboken", ItemDescription = "If you have extra time before the stadium, stop in Hoboken for waterfront views and Washington Street energy.", ItemOrderIndex = 6, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_hobokenwaterfront.jpg", SocialProofBadge = "Optional Detour", IndividualCostModifier = 15m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Around the Stadium: MetLife Stadium Plaza", ItemDescription = "Team stores, fan photo areas, and merchandise shops. Real attendee tip: arrive ~90 min early — finding your section can genuinely take longer than expected.", ItemOrderIndex = 7, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_metlifeplaza.jpg", SocialProofBadge = "Must-Do", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Watch the Match", ItemDescription = "MetLife Stadium — officially 'New York New Jersey Stadium.' Be seated 20 min before kickoff; consider leaving around the 80th minute to beat the exit crush.", ItemOrderIndex = 8, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_metlifestadium.jpg", SocialProofBadge = "World Cup", IndividualCostModifier = 320m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Celebrate Afterwards: Madd Hatter (Hoboken)", ItemDescription = "75+ screens on Washington Street if you're heading back toward Hoboken/Manhattan.", ItemOrderIndex = 9, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_maddhatter.jpg", SocialProofBadge = "Big Screens", IndividualCostModifier = 25m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Or: Ed & Mary's / HopsScotch Tavern (Jersey City)", ItemDescription = "Alternate celebration spots if you're staying on the Jersey City side rather than Hoboken.", ItemOrderIndex = 10, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_edandmarys.jpg", SocialProofBadge = "Jersey City Alternative", IndividualCostModifier = 20m, IsOptionalActivity = true, IsSelectedByDefault = false }
            );
            await db.SaveChangesAsync();

            // ═══════════ WISHLIST 2: Midtown Manhattan Before Kickoff ═══════════
            var w2Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = w2Id,
                WishlistName = "Midtown Manhattan Before Kickoff",
                WishlistDescription = "Everything here is within walking distance — a Midtown day of skylines, architecture, and iconic streets before the trip out to MetLife Stadium.",
                ShortStory = "Pizza, four different ways to see the skyline, a library ceiling worth the detour, and the brightest, loudest crossroads in the world — all before kickoff.",
                TotalDays = 1,
                PeopleType = "World Cup Fans who want a Midtown sightseeing day before the match",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_midtown_hero.jpg",
                GlobalInclusionsJson = @"[""Match Ticket (MetLife Stadium / New York New Jersey Stadium)"",""One Observation Deck Admission""]",
                RawContentKeywords = "Midtown Manhattan, Empire State Building, SUMMIT One Vanderbilt, Top of the Rock, Edge, Times Square, Broadway, MetLife Stadium, World Cup",
                PsychologicalVibeTagsJson = @"[""Sightseeing"",""Walkable"",""Sports Fan""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 400m,
                CalculatedTotalCost = 800m,
                DepositAmountRequired = 50m,
                AccommodationInclusions = "Day-trip itinerary — no overnight accommodation included",
                TransitInclusions = "Fully walkable within Midtown; NJ Transit via Secaucus Junction to MetLife Stadium for kickoff",
                ActivityInclusions = "One observation deck admission (choose based on preference), MetLife Stadium match ticket",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "World Cup Match Attendee (sightseeing-first)",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();

            var w2Day = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Midtown Manhattan Before Kickoff", MorningCityId = newYork.CityId, AfternoonCityId = newYork.CityId, EveningCityId = eastRutherford.CityId, TransitFromPreviousDayRouteId = null, WishlistId = w2Id };
            db.ItineraryDays.Add(w2Day);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Breakfast: New York Pizza Suprema", ItemDescription = "Penn Station-adjacent classic slice shop to start the day.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_newyorkpizzasuprema.jpg", SocialProofBadge = "Convenient Classic", IndividualCostModifier = 8m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Skyline Views: Empire State Building", ItemDescription = "The essential first-timer's deck — 86th-floor open-air views from $44.", ItemOrderIndex = 2, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_empirestatebuilsing.jpg", SocialProofBadge = "NYC Icon", IndividualCostModifier = 44m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Or: SUMMIT One Vanderbilt", ItemDescription = "Immersive mirrored art rooms and glass skyboxes — a different kind of observation deck experience.", ItemOrderIndex = 3, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_summitonevanderbilt.jpg", SocialProofBadge = "Most Talked About", IndividualCostModifier = 44m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Or: Top of the Rock", ItemDescription = "The best Central Park sightline among Manhattan's observation decks.", ItemOrderIndex = 4, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_topoftherock.jpg", SocialProofBadge = "Central Park View", IndividualCostModifier = 40m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Or: Edge (Hudson Yards)", ItemDescription = "The highest outdoor sky deck in the Western Hemisphere, with a glass floor and angled glass walls.", ItemOrderIndex = 5, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_edgehudsonyards.jpg", SocialProofBadge = "Most Dramatic", IndividualCostModifier = 39m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Walk Through: Bryant Park", ItemDescription = "A quiet green break behind the Public Library.", ItemOrderIndex = 6, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_bryantpark.jpg", SocialProofBadge = "Midtown Oasis", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "New York Public Library", ItemDescription = "Free entry to the Beaux-Arts main branch and the Rose Main Reading Room.", ItemOrderIndex = 7, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_newyorkpubliclibrary.jpg", SocialProofBadge = "Free Landmark", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Grand Central Terminal", ItemDescription = "The celestial ceiling mural and the four-faced clock — free to explore.", ItemOrderIndex = 8, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_grandcentralterminal.jpg", SocialProofBadge = "Architectural Icon", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Times Square", ItemDescription = "The neon, chaotic heart of Midtown.", ItemOrderIndex = 9, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_timessquare.jpg", SocialProofBadge = "Iconic Crossroads", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Broadway Theater District", ItemDescription = "Walk through the theater district; realistically only pairs with a match day if kickoff is late or you catch a matinee.", ItemOrderIndex = 10, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_broadwaytheatredistrict.jpg", SocialProofBadge = "Cultural Icon", IndividualCostModifier = 0m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Head to MetLife Stadium", ItemDescription = "NJ Transit via Secaucus Junction — no direct subway exists, so build in a real buffer (90+ minutes).", ItemOrderIndex = 11, TimeOfDay = "Evening", ImageUrl = "", SocialProofBadge = "", IndividualCostModifier = 100m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Watch the Match", ItemDescription = "MetLife Stadium — officially 'New York New Jersey Stadium' for the tournament.", ItemOrderIndex = 12, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_metlifestadium.jpg", SocialProofBadge = "World Cup", IndividualCostModifier = 320m, IsOptionalActivity = false, IsSelectedByDefault = true }
            );
            await db.SaveChangesAsync();

            // ═══════════ WISHLIST 3: Lower Manhattan & Brooklyn Explorer ═══════════
            var w3Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = w3Id,
                WishlistName = "Lower Manhattan & Brooklyn Explorer",
                WishlistDescription = "One of the best walking routes in NYC — from the tallest building in the Western Hemisphere, through 9/11 history, out to the harbor, and across the Brooklyn Bridge into DUMBO.",
                ShortStory = "A skyline view from the top of One World Trade, a solemn walk through history, a harbor crossing to Liberty and Ellis Islands, and a walk across the bridge into Brooklyn's most photographed corner.",
                TotalDays = 1,
                PeopleType = "History and Skyline Enthusiasts, World Cup visitors with a full free day",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_lowermanhattan_hero.jpg",
                GlobalInclusionsJson = @"[""One World Observatory Admission"",""Statue Cruises Ferry Ticket (Statue of Liberty + Ellis Island)""]",
                RawContentKeywords = "One World Observatory, 9/11 Memorial, Oculus, Statue of Liberty, Ellis Island, Brooklyn Bridge, DUMBO, Brooklyn Bridge Park, Juliana's Pizza",
                PsychologicalVibeTagsJson = @"[""History"",""Walkable"",""Photography""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 180m,
                CalculatedTotalCost = 360m,
                DepositAmountRequired = 30m,
                AccommodationInclusions = "Day-trip itinerary — no overnight accommodation included",
                TransitInclusions = "Walkable core, plus the Statue Cruises ferry from Battery Park",
                ActivityInclusions = "One World Observatory admission, Statue of Liberty + Ellis Island ferry ticket",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "History & Skyline Enthusiast",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();

            var w3Day = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Lower Manhattan & Brooklyn Explorer", MorningCityId = newYork.CityId, AfternoonCityId = newYork.CityId, EveningCityId = brooklyn.CityId, TransitFromPreviousDayRouteId = null, WishlistId = w3Id };
            db.ItineraryDays.Add(w3Day);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "Start: One World Observatory", ItemDescription = "The tallest building in the Western Hemisphere, with 360-degree harbor views.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_oneworldobservatory.jpg", SocialProofBadge = "Tallest View", IndividualCostModifier = 40m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "History: 9/11 Memorial & Museum", ItemDescription = "The twin reflecting pools are free; the museum is a separate ticket.", ItemOrderIndex = 2, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_911memorialmuseum.jpg", SocialProofBadge = "Essential History", IndividualCostModifier = 30m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "The Oculus", ItemDescription = "Calatrava's striking transit hub and shopping center.", ItemOrderIndex = 3, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_theoculus.jpg", SocialProofBadge = "Architectural Marvel", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "Harbor: Statue of Liberty", ItemDescription = "Ferry from Battery Park — book crown access far in advance if that's a priority.", ItemOrderIndex = 4, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_statueofliberty.jpg", SocialProofBadge = "Icon", IndividualCostModifier = 27m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "Ellis Island", ItemDescription = "Included with the same ferry ticket — immigration history for over 12 million arrivals.", ItemOrderIndex = 5, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_ellisisland.jpg", SocialProofBadge = "Included", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "Walk: Brooklyn Bridge", ItemDescription = "The 1883 pedestrian crossing from Manhattan into Brooklyn — stay out of the bike lane.", ItemOrderIndex = 6, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_brooklynbridge.jpg", SocialProofBadge = "Free Icon", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "Brooklyn: DUMBO", ItemDescription = "The famous Washington Street/Manhattan Bridge photo spot.", ItemOrderIndex = 7, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_dumbo.jpg", SocialProofBadge = "Photo Spot", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "Brooklyn Bridge Park", ItemDescription = "Some of the best free skyline views in the city.", ItemOrderIndex = 8, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_brooklynbridgepark.jpg", SocialProofBadge = "Best Free View", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3Day.ItineraryDayId, ItemTitle = "Juliana's Pizza (great addition)", ItemDescription = "Patsy Grimaldi's original spot — call ahead and eat it in Brooklyn Bridge Park.", ItemOrderIndex = 9, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_julianaspizza.jpg", SocialProofBadge = "Local Legend", IndividualCostModifier = 22m, IsOptionalActivity = false, IsSelectedByDefault = true }
            );
            await db.SaveChangesAsync();

            // ═══════════ WISHLIST 4: The Ultimate NYC Pizza Trail ═══════════
            var w4Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = w4Id,
                WishlistName = "The Ultimate NYC Pizza Trail",
                WishlistDescription = "A food adventure, not a restaurant list — organized by borough and state so you can pick your route: Manhattan's landmark slices, Brooklyn's coal-fired rivalry, and New Jersey's own deep pizza bench, from Secaucus (right by the stadium transfer point) out to a 1945 thin-crust legend in Orange.",
                ShortStory = "From Lombardi's 1905 original to a Dominican-Italian 'chimi' pie in Jersey City — this is the whole NYC/NJ pizza map, built to be sampled across your trip, not finished in one sitting.",
                TotalDays = 3,
                PeopleType = "Pizza Enthusiasts / Multi-Day NYC-NJ Visitors",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_pizzatrail_hero.jpg",
                GlobalInclusionsJson = "[]",
                RawContentKeywords = "NYC pizza, Joe's Pizza, Prince Street Pizza, Lombardi's, John's of Bleecker Street, New York Pizza Suprema, Brooklyn Boys, Razza, Star Tavern, Amadeo Pizzeria, Wahizza",
                PsychologicalVibeTagsJson = @"[""Foodie"",""Flexible"",""Local Favorite""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 0m,
                CalculatedTotalCost = 0m,
                DepositAmountRequired = 0m,
                AccommodationInclusions = "Not applicable — a flexible dining guide, not a booked itinerary",
                TransitInclusions = "Manhattan stops are walkable/subway-accessible; NJ stops require a car or PATH/rail plus rideshare, and vary significantly in distance from the NYC core",
                ActivityInclusions = "None — this guide covers dining stops only",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "Pizza Enthusiast",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();
            // NOTE: If "ForkedFontId2" is not a real property on Wishlist, remove that
            // line and use ForkedFromId = null instead — kept here as a flag in case a
            // copy/paste typo slipped in; verify against your actual Wishlist entity
            // before running this migration.

            var w4ManhattanDay = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Manhattan", MorningCityId = newYork.CityId, AfternoonCityId = newYork.CityId, EveningCityId = newYork.CityId, TransitFromPreviousDayRouteId = null, WishlistId = w4Id };
            var w4BrooklynDay = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 2, DayTitle = "Brooklyn", MorningCityId = brooklyn.CityId, AfternoonCityId = brooklyn.CityId, EveningCityId = brooklyn.CityId, TransitFromPreviousDayRouteId = null, WishlistId = w4Id };
            var w4NJDay = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 3, DayTitle = "New Jersey", MorningCityId = secaucus.CityId, AfternoonCityId = jerseyCity.CityId, EveningCityId = orange.CityId, TransitFromPreviousDayRouteId = null, WishlistId = w4Id };
            db.ItineraryDays.AddRange(w4ManhattanDay, w4BrooklynDay, w4NJDay);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4ManhattanDay.ItineraryDayId, ItemTitle = "Joe's Pizza", ItemDescription = "Greenwich Village's classic thin, crispy slice since 1975.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_joespizza.jpg", SocialProofBadge = "Village Icon", IndividualCostModifier = 8m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4ManhattanDay.ItineraryDayId, ItemTitle = "Prince Street Pizza", ItemDescription = "The Spicy Spring Sicilian slice, with its famous crispy pepperoni cups.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_princestreetpizza.jpg", SocialProofBadge = "Cult Favorite", IndividualCostModifier = 6m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4ManhattanDay.ItineraryDayId, ItemTitle = "Lombardi's", ItemDescription = "America's first licensed pizzeria, 1905 — the origin point of New York-style pizza.", ItemOrderIndex = 3, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_lombardis.jpg", SocialProofBadge = "The Original", IndividualCostModifier = 26m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4ManhattanDay.ItineraryDayId, ItemTitle = "John's of Bleecker Street", ItemDescription = "A near-century-old coal-fired oven, whole pies only.", ItemOrderIndex = 4, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_johnsofbleeckerstreet.jpg", SocialProofBadge = "Historic Landmark", IndividualCostModifier = 25m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4ManhattanDay.ItineraryDayId, ItemTitle = "New York Pizza Suprema", ItemDescription = "The Penn Station-adjacent classic.", ItemOrderIndex = 5, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_newyorkpizzasuprema.jpg", SocialProofBadge = "Convenient Classic", IndividualCostModifier = 8m, IsOptionalActivity = true, IsSelectedByDefault = false },

                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4BrooklynDay.ItineraryDayId, ItemTitle = "Brooklyn Boys Pizza & Deli", ItemDescription = "Despite the name, this is actually in Edison, NJ — a 20+ year family institution, not a Brooklyn stop.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_brooklynboyspizzadeli.jpg", SocialProofBadge = "Name Is Misleading", IndividualCostModifier = 16m, IsOptionalActivity = true, IsSelectedByDefault = false },

                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4NJDay.ItineraryDayId, ItemTitle = "Amadeo Pizzeria (Secaucus)", ItemDescription = "The only brick-oven pizzeria in the town that hosts your mandatory MetLife Stadium rail transfer.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_amadeopizzeria.jpg", SocialProofBadge = "Right by the Transfer", IndividualCostModifier = 18m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4NJDay.ItineraryDayId, ItemTitle = "Razza (Jersey City)", ItemDescription = "Artisanal, house-milled-flour pizza frequently ranked among the best in the entire metro area.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_razza.jpg", SocialProofBadge = "Metro-Area Best", IndividualCostModifier = 26m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4NJDay.ItineraryDayId, ItemTitle = "Roman Gourmet (Jersey City)", ItemDescription = "A reliable NY-style slice-and-sandwich counter, less hype than Razza, still well regarded.", ItemOrderIndex = 3, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_romangourmet.jpg", SocialProofBadge = "Local Go-To", IndividualCostModifier = 12m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4NJDay.ItineraryDayId, ItemTitle = "Rudy's Ristorante & Pizzeria (Jersey City)", ItemDescription = "Full-menu Italian restaurant and pizzeria, regularly named among the city's best.", ItemOrderIndex = 4, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_rudysristorantepizzeria.jpg", SocialProofBadge = "Full Menu Option", IndividualCostModifier = 22m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4NJDay.ItineraryDayId, ItemTitle = "Star Tavern (Orange)", ItemDescription = "Est. 1945, NYT-called 'superior' thin-crust bar pizza — a genuine detour from the NYC/Jersey City core, worth the drive.", ItemOrderIndex = 5, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_startavern.jpg", SocialProofBadge = "NJ Legend", IndividualCostModifier = 20m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w4NJDay.ItineraryDayId, ItemTitle = "Sal's Pizza (verify location first)", ItemDescription = "Common name across NJ — confirm which specific Sal's fits your route before planning around it; one confirmed location is in Toms River, a genuine 70-mile detour.", ItemOrderIndex = 6, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_salspizza.jpg", SocialProofBadge = "Verify Address", IndividualCostModifier = 18m, IsOptionalActivity = true, IsSelectedByDefault = false }
            );
            await db.SaveChangesAsync();

            var w1Dests = new[] { dNYPS, dJoes, dFF, dKtown, dFanVillage, dHoboken, dMetLife, dMaddHatter, dEdMarys, dHopsScotch };
            var w2Dests = new[] { dNYPS, dESB, dSummit, dTotr, dEdge, dBryant, dNYPL, dGCT, dTimesSq, dBroadway, dMetLife };
            var w3Dests = new[] { dOWO, d911, dOculus, dSoL, dEllis, dBB, dDumbo, dBBP, dJulianas };
            var w4Dests = new[] { dJoes, dPrince, dLombardis, dJohns, dNYPS, dBKBoys, dRazza, dRoman, dAmadeo, dStar, dRudys, dSals, dWahizza };

            async Task LinkAsync(Guid wl, IEnumerable<Guid> ids)
            {
                foreach (var id in ids)
                {
                    bool exists = await db.WishlistDestinations.AnyAsync(wd => wd.WishlistId == wl && wd.DestinationId == id);
                    if (!exists) db.WishlistDestinations.Add(new WishlistDestination { WishlistId = wl, DestinationId = id });
                }
            }
            await LinkAsync(w1Id, w1Dests);
            await LinkAsync(w2Id, w2Dests);
            await LinkAsync(w3Id, w3Dests);
            await LinkAsync(w4Id, w4Dests);
            await db.SaveChangesAsync();
        }

        private static async Task SeedImagesForNYNJAsync(HodracDbContext db, List<Destination> destinations)
        {
            var imageNames = destinations.Select(d => d.CleanNormalizedSearchName.Replace(" ", "") + ".jpg").ToList();
            foreach (var (dest, image) in destinations.Zip(imageNames, (d, i) => (d, i)))
            {
                db.DestinationImages.Add(new DestinationImage
                {
                    DestinationImageId = Guid.NewGuid(),
                    DestinationId = dest.DestinationId,
                    ImageUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_{Uri.EscapeDataString(image)}",
                    ThumbnailUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/nynj_{Uri.EscapeDataString(image)}",
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
                    db.DestinationLanguages.Add(new DestinationLanguage { DestinationId = dest.DestinationId, LanguageId = language.LanguageId });

                bool currencyExists = await db.DestinationCurrencies
                    .AnyAsync(dc => dc.DestinationId == dest.DestinationId && dc.CurrencyId == currency.CurrencyId);
                if (!currencyExists)
                    db.DestinationCurrencies.Add(new DestinationCurrency { DestinationId = dest.DestinationId, CurrencyId = currency.CurrencyId });
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
