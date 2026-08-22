using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Seeder;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    public class HawaiiSeeder
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ADD THIS METHOD TO THE EXISTING `DataSeeder` STATIC CLASS.
        // Follows the same PascalCase DescriptionJson template as SeedJapanTrip /
        // SeedJapanFamilyFestivalTrip (Overview, Directions, WhatToKnow,
        // ThingsToBeWaryOf, LocalPerspective, HiddenCost, NearbyComplements,
        // BestTimeToVisit, crowdLevel, Accessibility, IdealDuration).
        //
        // ACCURACY CORRECTIONS baked into the data (not just this comment):
        // 1. USS Arizona Memorial reservations via Recreation.gov open 8 weeks
        //    (56 days) in advance, released daily at 3:00 PM HST in two windows
        //    (24-hour and 8-week) — NOT "30 days at 7:00 AM" as commonly assumed.
        // 2. Mauka Warriors Luau is located in Kapolei (Coral Crater Adventure
        //    Park / Hawaii Country Club), roughly 24 miles from Waikiki — it is
        //    NOT at the Hilton Hawaiian Village. Day 3's evening plan is adjusted
        //    accordingly: the rental car is kept through dinner rather than
        //    returned beforehand, since this venue is not walkable from Waikiki.
        // 3. Diamond Head requires its own separate 30-day-advance reservation
        //    for non-residents (opens at midnight HST); this is distinct from,
        //    and in addition to, the Hanauma Bay 48-hour reservation.
        // 4. Hanauma Bay Nature Preserve is closed every Monday and Tuesday —
        //    flagged directly since the source day-by-day plan doesn't account
        //    for this when picking a Day 3 date.
        // ═══════════════════════════════════════════════════════════════════════

        public static async Task SeedOahuUltimateIslandWishlist(HodracDbContext db)
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

            var honolulu = await GetOrCreateCityAsync("Honolulu", 21.3069, -157.8583, "Oahu's urban core, spanning Waikiki, downtown, Pearl Harbor, and the southeastern coastline hikes and snorkel spots.");
            var kapolei = await GetOrCreateCityAsync("Kapolei", 21.3356, -158.0578, "Oahu's leeward 'Second City,' home to Ko Olina's resort lagoons and several of the island's luaus.");
            var kailua = await GetOrCreateCityAsync("Kailua", 21.4022, -157.7394, "Windward-side beach town home to two of the world's most photographed white-sand beaches.");
            var kaneohe = await GetOrCreateCityAsync("Kaneohe", 21.4180, -157.8025, "Lush windward valley town ringed by the Koolau mountains, home to Kualoa Ranch and Byodo-In Temple.");
            var haleiwa = await GetOrCreateCityAsync("Haleiwa", 21.5928, -158.1039, "North Shore surf town, home to legendary big-wave beaches, shrimp trucks, and shark tour operators.");

            var mapping = new Dictionary<string, (string[] categories, string[] tags)>
            {
                ["Iolani Palace"] = (new[] { "cultural_site", "landmark_monument" }, new[] { "history", "cultural", "educational", "architecture" }),
                ["Bishop Museum"] = (new[] { "cultural_site", "activity_experience" }, new[] { "educational", "family_friendly", "cultural", "history" }),
                ["Leonard's Bakery"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "budget_friendly" }),
                ["Pearl Harbor & USS Arizona Memorial"] = (new[] { "historical_tour", "cultural_site" }, new[] { "history", "educational", "tourist_hotspot" }),
                ["USS Bowfin Submarine Museum & Park"] = (new[] { "historical_tour", "activity_experience" }, new[] { "history", "educational", "family_friendly" }),
                ["USS Missouri Battleship"] = (new[] { "historical_tour", "landmark_monument" }, new[] { "history", "educational", "tourist_hotspot" }),
                ["Ko Olina Lagoons"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "relaxing", "family_friendly", "photography" }),
                ["Germaine's Luau"] = (new[] { "entertainment_nightlife", "cultural_site" }, new[] { "cultural", "family_friendly", "social", "tourist_hotspot" }),
                ["Diamond Head State Monument"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "adventurous", "photography", "tourist_hotspot", "family_friendly" }),
                ["Hanauma Bay Nature Preserve"] = (new[] { "nature_outdoor", "activity_experience" }, new[] { "relaxing", "family_friendly", "photography", "tourist_hotspot" }),
                ["Makapuʻu Lighthouse Trail"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "family_friendly", "photography", "relaxing" }),
                ["Koko Head Crater Trail"] = (new[] { "nature_outdoor", "activity_experience" }, new[] { "adventurous", "hidden_gem", "photography" }),
                ["Mauka Warriors Luau"] = (new[] { "entertainment_nightlife", "cultural_site" }, new[] { "cultural", "family_friendly", "social", "hidden_gem" }),
                ["Lanikai Pillbox Trail"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "photography", "adventurous", "tourist_hotspot" }),
                ["Kailua & Lanikai Beaches"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "relaxing", "family_friendly", "photography", "tourist_hotspot" }),
                ["Rainbow Drive-In"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "budget_friendly" }),
                ["Byodo-In Temple"] = (new[] { "cultural_site", "nature_outdoor" }, new[] { "cultural", "relaxing", "photography", "hidden_gem" }),
                ["Hoʻomaluhia Botanical Garden"] = (new[] { "nature_outdoor" }, new[] { "relaxing", "family_friendly", "photography", "budget_friendly" }),
                ["Kualoa Ranch"] = (new[] { "activity_experience", "nature_outdoor" }, new[] { "adventurous", "family_friendly", "tourist_hotspot", "premium" }),
                ["North Shore Shark Adventures"] = (new[] { "activity_experience" }, new[] { "adventurous", "tourist_hotspot" }),
                ["Waimea Bay"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "family_friendly", "photography", "relaxing" }),
                ["Sunset Beach"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "photography", "tourist_hotspot", "relaxing" }),
                ["Giovanni's Shrimp Truck"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "tourist_hotspot" }),
                ["Matsumoto Shave Ice"] = (new[] { "food_experience" }, new[] { "food_focused", "family_friendly", "local_favorite" }),
                ["Waikiki Beach"] = (new[] { "nature_outdoor", "viewpoint_scenic_spot" }, new[] { "relaxing", "family_friendly", "tourist_hotspot", "walkable" }),
                ["Ala Moana Center"] = (new[] { "market_street_life" }, new[] { "shopping", "walkable", "family_friendly" }),
                ["House Without a Key"] = (new[] { "entertainment_nightlife", "food_experience" }, new[] { "romantic", "relaxing", "premium" }),
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

            // ─── Destination GUIDs ──────────────────────────────────────────────
            var dIolani = Guid.NewGuid(); var dBishop = Guid.NewGuid(); var dLeonards = Guid.NewGuid();
            var dPearlHarbor = Guid.NewGuid(); var dBowfin = Guid.NewGuid(); var dMissouri = Guid.NewGuid();
            var dKoOlina = Guid.NewGuid(); var dGermaines = Guid.NewGuid(); var dDiamondHead = Guid.NewGuid();
            var dHanauma = Guid.NewGuid(); var dMakapuu = Guid.NewGuid(); var dKokoHead = Guid.NewGuid();
            var dMaukaWarriors = Guid.NewGuid(); var dLanikaiPillbox = Guid.NewGuid(); var dKailuaLanikaiBeaches = Guid.NewGuid();
            var dRainbowDriveIn = Guid.NewGuid(); var dByodoIn = Guid.NewGuid(); var dHoomaluhia = Guid.NewGuid();
            var dKualoa = Guid.NewGuid(); var dSharkTour = Guid.NewGuid(); var dWaimeaBay = Guid.NewGuid();
            var dSunsetBeach = Guid.NewGuid(); var dGiovannis = Guid.NewGuid(); var dMatsumoto = Guid.NewGuid();
            var dWaikikiBeach = Guid.NewGuid(); var dAlaMoana = Guid.NewGuid(); var dHouseWithoutAKey = Guid.NewGuid();

            var newDestinations = new[]
            {
        // ── ʻIolani Palace ──
        new Destination { DestinationId = dIolani, DestinationName = "Iolani Palace", CleanNormalizedSearchName = "iolani palace", MetaphoneCode = "ILN PLS", DoubleMetaphonePrimary = "ILN PLS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The only official state residence of royalty on U.S. soil, ʻIolani Palace was home to the Hawaiian Kingdom's last two monarchs, King Kalakaua and Queen Liliʻuokalani, until the 1893 overthrow. Completed in 1882, it was remarkably modern for its era — electric lights before the White House, indoor plumbing, and a telephone. Today it's a National Historic Landmark and museum with restored royal furnishings, portraits, and the basement gallery of crown jewels and royal regalia.",
              "directions": "Best Access: Downtown Honolulu, a short walk from the Hawaii State Capitol.\n\nAddress for Rideshare: 364 S King St, Honolulu, HI 96813.",
              "whatToKnow": "Guided Tour: The 60-minute guided tour is the most recommended way to experience the palace's history in depth; a self-guided audio tour is also excellent and more flexible for families.\n\nFootwear Rule: Visitors are required to wear surgical-style booties over their shoes (provided on-site) to protect the original floors on the guided upstairs tour.\n\nBasement Galleries: Included with most ticket types, housing crown jewels, royal portraits, and artifacts from the overthrow era.",
              "thingsToBeWaryOf": "Photography Restrictions: Interior photography is limited in the state rooms — check current rules at the ticket counter.\n\nTiming: Guided tours run on a set schedule and can sell out on busy days; booking ahead online is worth it if you have a tight morning schedule.",
              "localPerspective": "For Native Hawaiians, the palace carries real weight beyond a museum visit — it's the site of Queen Liliʻuokalani's house arrest following the 1893 overthrow, and locals regard a respectful visit here as an essential piece of understanding the islands' actual history, not just a tourist stop.",
              "hiddenCost": "Guided Tour: Roughly $30 for adults, less for children.\nAudio Tour: Roughly $25.\nGrounds-Only Admission: A lower-cost option if you just want to see the exterior and gardens.",
              "nearbyComplements": [
                "Hawaii State Capitol: Directly adjacent.",
                "Kawaiahaʻo Church: A short walk, Hawaii's oldest Christian church.",
                "Bishop Museum: A short bus or rideshare away."
              ],
              "bestTimeToVisit": "Morning on a weekday for the shortest tour wait times.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "Rating: 7/10 — the ground floor and grounds are wheelchair accessible; the upper-floor guided tour involves a grand staircase, though alternate arrangements can be made.",
              "idealDuration": "1 to 1.5 hours for the guided tour and basement galleries."
            }
            """,
            AverageCostPerDay = 30m, LuxuryRating = DeriveLuxury(30m, mapping["Iolani Palace"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Iolani Palace"].tags), AdventurePaceScore = AdventureScore(mapping["Iolani Palace"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Iolani Palace"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Iolani Palace"].tags), Latitude = 21.3059, Longitude = -157.8583, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Bishop Museum ──
        new Destination { DestinationId = dBishop, DestinationName = "Bishop Museum", CleanNormalizedSearchName = "bishop museum", MetaphoneCode = "BXP MSM", DoubleMetaphonePrimary = "PXP MSM", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Hawaii's largest museum and the world's premier repository of Hawaiian and Pacific cultural artifacts, founded in 1889 by Charles Reed Bishop in memory of his wife, Princess Bernice Pauahi Bishop, the last direct descendant of the Kamehameha dynasty. The Hawaiian Hall houses feather cloaks, royal artifacts, and a full-scale sperm whale skeleton, while the planetarium and Science Adventure Center add a hands-on layer for kids.",
              "directions": "Best Access: A short bus or rideshare ride from Waikiki, in the Kalihi neighborhood.\n\nAddress for Rideshare: 1525 Bernice St, Honolulu, HI 96817.",
              "whatToKnow": "Planetarium Shows: Run on a set daily schedule — check show times when you arrive to plan the rest of your visit around them.\n\nScience Adventure Center: A hands-on volcano and Pacific ecosystems exhibit hall, genuinely engaging for younger kids alongside the more historical Hawaiian Hall.\n\nTiming: Budget 2-3 hours minimum to see the Hawaiian Hall, planetarium, and Science Adventure Center without rushing.",
              "thingsToBeWaryOf": "Not Waterfront: Unlike many Oahu stops, this is an inland, indoor-focused museum — a good rainy-day or midday-heat option rather than a beach-adjacent activity.\n\nParking: On-site parking is available but can fill up on busy days; allow extra time.",
              "localPerspective": "Local families treat Bishop Museum as a genuine educational cornerstone — many Oahu schoolchildren visit on field trips, and it's widely regarded as the most authoritative single place to understand pre-contact and monarchy-era Hawaiian culture in depth.",
              "hiddenCost": "Adult Admission: Roughly $28-33.\nChild Admission: Roughly $20-25.\nPlanetarium Show: Often included with general admission.",
              "nearbyComplements": [
                "ʻIolani Palace: A short drive toward downtown.",
                "Kalihi neighborhood: Local eateries nearby for lunch."
              ],
              "bestTimeToVisit": "Late morning to early afternoon, especially as a midday heat break between outdoor activities.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "Rating: 9/10 — modern, fully accessible museum halls with elevators.",
              "idealDuration": "2 to 3 hours."
            }
            """,
            AverageCostPerDay = 28m, LuxuryRating = DeriveLuxury(28m, mapping["Bishop Museum"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Bishop Museum"].tags), AdventurePaceScore = AdventureScore(mapping["Bishop Museum"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Bishop Museum"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Bishop Museum"].tags), Latitude = 21.3269, Longitude = -157.8721, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Leonard's Bakery ──
        new Destination { DestinationId = dLeonards, DestinationName = "Leonard's Bakery", CleanNormalizedSearchName = "leonards bakery", MetaphoneCode = "LNRTS BKR", DoubleMetaphonePrimary = "LNRTS PKR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Family-owned since 1952, Leonard's is the originator of the Hawaiian malasada — a Portuguese-style hole-less doughnut, deep-fried to order and rolled in sugar (or filled with custard, chocolate, or haupia coconut cream). It's a genuine Oahu institution just outside Waikiki in the Kapahulu neighborhood, close enough to visit twice on a weeklong trip without much detour.",
              "directions": "Best Access: Kapahulu Avenue, a short drive or 15-20 minute walk from central Waikiki.\n\nAddress for Rideshare: 933 Kapahulu Ave, Honolulu, HI 96816.",
              "whatToKnow": "Made to Order: Malasadas are fried fresh, so there's often a short wait even when the line looks short — worth it for the warm, fresh result.\n\nHaupia Filling: The custard-filled haupia (coconut) malasada is the most-cited must-try flavor beyond the classic sugar-rolled original.\n\nBuy Extra: A popular local tip is buying an extra box to keep in the hotel room for the next couple of days, since they hold up reasonably well.",
              "thingsToBeWaryOf": "Line Length: A near-constant line forms, especially on weekend mornings — factor in 10-20 minutes even for a 'quick' stop.\n\nParking: The small Kapahulu Avenue lot fills fast; street parking or walking from Waikiki is often easier.",
              "localPerspective": "Locals and tourists alike treat Leonard's as an essential Oahu stop — it's common to see the same visitors return two or three times over a single trip, and the malasada has become as iconic to Oahu as shave ice.",
              "hiddenCost": "Single Malasada: $2-3.\nFilled Malasada: $3-4.\nBox of a Dozen: Roughly $25-35.",
              "nearbyComplements": [
                "Waikiki Beach: A 15-20 minute walk or short drive.",
                "Diamond Head: A short drive further along Kapahulu/Monsarrat Ave."
              ],
              "bestTimeToVisit": "Weekday mid-morning or late afternoon to avoid the longest weekend lines.",
              "crowdLevel": "High (7/10), especially weekend mornings.",
              "accessibility": "Rating: 8/10 — flat, small storefront with outdoor waiting area.",
              "idealDuration": "20 to 30 minutes."
            }
            """,
            AverageCostPerDay = 10m, LuxuryRating = DeriveLuxury(10m, mapping["Leonard's Bakery"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Leonard's Bakery"].tags), AdventurePaceScore = AdventureScore(mapping["Leonard's Bakery"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Leonard's Bakery"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Leonard's Bakery"].tags), Latitude = 21.2887, Longitude = -157.8171, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Pearl Harbor & USS Arizona Memorial ──
        new Destination { DestinationId = dPearlHarbor, DestinationName = "Pearl Harbor & USS Arizona Memorial", CleanNormalizedSearchName = "pearl harbor uss arizona memorial", MetaphoneCode = "PRL HRBR ARSN MMRL", DoubleMetaphonePrimary = "PRL HRBR ARSN MMRL", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Oahu's most-visited attraction and the site of the December 7, 1941 attack that brought the U.S. into World War II. The core experience is the USS Arizona Memorial Program: a 45-minute visit including a short film and a Navy shuttle boat ride out to the white memorial structure spanning the sunken battleship, still the final resting place for more than 1,000 sailors. The visitor center, museums, and grounds are free to explore beyond the memorial program itself.",
              "directions": "Best Access: About 20-25 minutes by car or rideshare from Waikiki; TheBus routes also serve Pearl Harbor from Waikiki, though a rental car is faster for an early arrival.\n\nAddress for Rideshare: Pearl Harbor National Memorial Visitor Center, 1 Arizona Memorial Pl, Honolulu, HI 96818.",
              "whatToKnow": "Reservation Reality Check: Official USS Arizona Memorial Program reservations are handled exclusively through Recreation.gov and are released daily at 3:00 PM HST in two separate windows — one batch 8 weeks (56 days) ahead, and a smaller batch 24 hours ahead. This is a meaningfully different system than the '30 days out at 7:00 AM' timing sometimes assumed — set your alarm for 3:00 PM HST, 8 weeks before your visit date, not 30 days at 7 AM.\n\n$1 Fee: The memorial program itself is free, but Recreation.gov charges a non-refundable $1 booking fee per ticket.\n\nBag Rules: Strict — no bags allowed beyond small exceptions (clear stadium bags, medical needs); a privately run bag storage facility near the visitor center charges roughly $6/bag if you need it.\n\nCombine with Bowfin/Missouri: The visitor center is also the gateway to the separately ticketed USS Bowfin submarine and USS Missouri battleship (reached via a Ford Island shuttle) — budget real transition time between all three if doing a full Pearl Harbor day.",
              "thingsToBeWaryOf": "Sells Out in Minutes: Popular dates and time slots go within minutes of the 3:00 PM HST release — have your Recreation.gov account and payment info ready in advance, not created on the fly.\n\nArrive Early Regardless: Even with a confirmed reservation, arrive 45-60 minutes before your program time; standby-only visitors should reach the visitor center by 6:00-6:30 AM.\n\nNo Public Restrooms at the Memorial Itself: Use facilities at the visitor center before boarding the shuttle boat.",
              "localPerspective": "For many Oahu residents, Pearl Harbor remains a place of genuine reverence rather than a standard tourist stop — locals often note that visitors rushing through without watching the orientation film or reading the exhibits miss the historical weight the site is built to convey.",
              "hiddenCost": "USS Arizona Memorial Program: Free, plus $1 Recreation.gov booking fee.\nUSS Bowfin Submarine: Separate paid admission (see its own entry).\nUSS Missouri Battleship: Separate paid admission, including Ford Island shuttle (see its own entry).\nBag Storage: Roughly $6/bag if needed.",
              "nearbyComplements": [
                "USS Bowfin Submarine Museum & Park: Immediately adjacent to the visitor center.",
                "USS Missouri Battleship: A short Ford Island shuttle ride away.",
                "Pearl Harbor Aviation Museum: Also reachable via the Ford Island shuttle."
              ],
              "bestTimeToVisit": "First program of the day (8:00 AM) for the coolest temperatures and calmest crowds; the visitor center opens at 7:00 AM.",
              "crowdLevel": "High (7/10) most of the day, easing slightly toward the last boat departure (3:30 PM).",
              "accessibility": "Rating: 9/10 — the visitor center, museums, and memorial shuttle boats are all ADA accessible.",
              "idealDuration": "1.5 hours for the Arizona Memorial Program alone; a full day (5-6 hours) if combining with Bowfin and Missouri."
            }
            """,
            AverageCostPerDay = 5m, LuxuryRating = DeriveLuxury(5m, mapping["Pearl Harbor & USS Arizona Memorial"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Pearl Harbor & USS Arizona Memorial"].tags), AdventurePaceScore = AdventureScore(mapping["Pearl Harbor & USS Arizona Memorial"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Pearl Harbor & USS Arizona Memorial"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Pearl Harbor & USS Arizona Memorial"].tags), Latitude = 21.3649, Longitude = -157.9500, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── USS Bowfin Submarine Museum & Park ──
        new Destination { DestinationId = dBowfin, DestinationName = "USS Bowfin Submarine Museum & Park", CleanNormalizedSearchName = "uss bowfin submarine museum park", MetaphoneCode = "AS BFN SPMRN MSM PRK", DoubleMetaphonePrimary = "AS PFN SPMRN MSM PRK", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A fully restored WWII fleet submarine nicknamed 'The Pearl Harbor Avenger' for launching exactly one year after the attack, USS Bowfin sits at the same complex as the Arizona Memorial visitor center — no separate reservation window required, and no Ford Island shuttle needed, making it the easiest of Pearl Harbor's three ship museums to add on.",
              "directions": "Best Access: Immediately adjacent to the Pearl Harbor National Memorial Visitor Center, no shuttle needed.\n\nAddress for Rideshare: Same as the Pearl Harbor Visitor Center, 1 Arizona Memorial Pl, Honolulu, HI 96818.",
              "whatToKnow": "Self-Guided Tour: Walk through the actual submarine's cramped interior at your own pace, included with admission.\n\nMuseum Exhibits: The adjacent museum covers submarine warfare history and includes an outdoor park with missiles and memorial exhibits.\n\nNo Reservation Needed: Unlike the Arizona Memorial, Bowfin tickets are typically available same-day at the site.",
              "thingsToBeWaryOf": "Tight Interior: The submarine tour involves narrow passageways, steep ladders, and low clearances — not ideal for claustrophobia or significant mobility limitations.\n\nNot Included with Arizona Memorial: This is a separate paid ticket from the free Arizona Memorial program.",
              "localPerspective": "Often the most memorable stop of a Pearl Harbor day for kids specifically, since it's the one experience where you're physically inside a piece of WWII history rather than viewing it from a distance.",
              "hiddenCost": "Adult Admission: Roughly $20-25.\nChild Admission: Roughly $12-15.\nCombo tickets with Missouri: Often available at a discount.",
              "nearbyComplements": [
                "Pearl Harbor Visitor Center & Arizona Memorial: Immediately adjacent.",
                "USS Missouri Battleship: A short Ford Island shuttle ride."
              ],
              "bestTimeToVisit": "Right after or before your Arizona Memorial Program time slot, since it's on-site.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "Rating: 4/10 for the submarine interior (narrow, ladders); the museum and outdoor park are fully accessible.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 22m, LuxuryRating = DeriveLuxury(22m, mapping["USS Bowfin Submarine Museum & Park"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["USS Bowfin Submarine Museum & Park"].tags), AdventurePaceScore = AdventureScore(mapping["USS Bowfin Submarine Museum & Park"].tags), AestheticTrendScore = AestheticTrendScore(mapping["USS Bowfin Submarine Museum & Park"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["USS Bowfin Submarine Museum & Park"].tags), Latitude = 21.3634, Longitude = -157.9538, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── USS Missouri Battleship ──
        new Destination { DestinationId = dMissouri, DestinationName = "USS Missouri Battleship", CleanNormalizedSearchName = "uss missouri battleship", MetaphoneCode = "AS MSR BTLXP", DoubleMetaphonePrimary = "AS MSR PTLXP", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "'Mighty Mo' is the battleship on whose deck Japan formally surrendered on September 2, 1945, ending WWII — and it's now permanently moored on Ford Island, symbolically facing the USS Arizona Memorial so the war's beginning and end sit within view of each other. Self-guided and guided tour options let visitors walk the deck, the surrender site, and below-decks quarters.",
              "directions": "Best Access: Reached via a free shuttle bus from the Pearl Harbor Visitor Center across the Ford Island bridge — factor in real transition time.\n\nAddress for Rideshare: Board the shuttle at the Pearl Harbor National Memorial Visitor Center, 1 Arizona Memorial Pl, Honolulu, HI 96818.",
              "whatToKnow": "Surrender Deck: A marked plaque shows the exact spot where the surrender documents were signed — the single most-photographed spot on the ship.\n\nGuided Tours: Available for an upgrade fee and go into more depth on the ship's WWII and later Gulf War service history.\n\nShuttle Timing: The Ford Island shuttle runs on a schedule — check current departure times so you don't miss your window back.",
              "thingsToBeWaryOf": "Shuttle Bottleneck: Getting to and from Missouri (and Bowfin/Aviation Museum) adds real time to a Pearl Harbor day — this is the attraction most likely to get rushed if you've underestimated your schedule.\n\nSun Exposure: Much of the deck tour is outdoors and unshaded — hats and sunscreen matter here as much as anywhere on the island.",
              "localPerspective": "Veterans' groups and military families make regular pilgrimages here specifically for the surrender deck — it's treated with the same gravity as the Arizona Memorial by those with a personal connection to WWII service.",
              "hiddenCost": "Adult Admission: Roughly $34-45 depending on tour level.\nGuided Tour Upgrade: Additional $10-25.\nFord Island Shuttle: Included with admission.",
              "nearbyComplements": [
                "USS Arizona Memorial: Symbolically and physically nearby on Ford Island's approach.",
                "Pearl Harbor Aviation Museum: Also on Ford Island, reachable via the same shuttle."
              ],
              "bestTimeToVisit": "Mid-morning, after the Arizona Memorial program, allowing time for the shuttle transition.",
              "crowdLevel": "Medium (5/10), High (7/10) midday.",
              "accessibility": "Rating: 6/10 — the main deck is accessible, but some interior spaces involve stairs and hatches.",
              "idealDuration": "1.5 to 2 hours including shuttle transit."
            }
            """,
            AverageCostPerDay = 40m, LuxuryRating = DeriveLuxury(40m, mapping["USS Missouri Battleship"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["USS Missouri Battleship"].tags), AdventurePaceScore = AdventureScore(mapping["USS Missouri Battleship"].tags), AestheticTrendScore = AestheticTrendScore(mapping["USS Missouri Battleship"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["USS Missouri Battleship"].tags), Latitude = 21.3556, Longitude = -157.9536, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Ko Olina Lagoons ──
        new Destination { DestinationId = dKoOlina, DestinationName = "Ko Olina Lagoons", CleanNormalizedSearchName = "ko olina lagoons", MetaphoneCode = "K OLN LKNS", DoubleMetaphonePrimary = "K OLN LKNS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Four man-made, crescent-shaped lagoons on Oahu's leeward coast, each protected from ocean swells by a rock barrier — calm, family-friendly water that's ideal for young kids or anyone wanting a break from bigger surf. Public beach access exists at each lagoon despite the surrounding resort development.",
              "directions": "Best Access: A short drive from Kapolei, roughly 40-45 minutes from Waikiki.\n\nAddress for Rideshare: Ko Olina, Kapolei, HI 96707 (each lagoon has its own numbered public access point).",
              "whatToKnow": "Public Access: Despite being surrounded by resorts, all four lagoons have designated public beach access with parking — though the lots are small and fill early.\n\nCalm Water: The rock breakwaters make these some of the calmest swimming spots on the island, genuinely good for young children.\n\nFree Activity: No entry fee for the lagoons or public beach access themselves.",
              "thingsToBeWaryOf": "Limited Public Parking: Each lagoon's public lot is small and fills by mid-morning on weekends — arrive early or expect to circle.\n\nResort Crowds: Weekend and holiday crowds include both resort guests and locals, so space can be tight at peak times.",
              "localPerspective": "Local families with young kids specifically seek out Ko Olina's calm lagoons over rougher Oahu beaches — it's a well-known 'safe swimming' recommendation among Oahu parents.",
              "hiddenCost": "Free entry and parking (public access points).\nResort Day-Passes (optional): Vary significantly if wanting resort amenities beyond the public beach.",
              "nearbyComplements": [
                "Germaine's Luau: A short drive away.",
                "Kapolei: The surrounding town for casual lunch options."
              ],
              "bestTimeToVisit": "Late morning to early afternoon, arriving early for parking.",
              "crowdLevel": "Medium (5/10), High (7/10) weekends.",
              "accessibility": "Rating: 8/10 — sandy but generally flat beach access at each lagoon.",
              "idealDuration": "1.5 to 2.5 hours."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Ko Olina Lagoons"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Ko Olina Lagoons"].tags), AdventurePaceScore = AdventureScore(mapping["Ko Olina Lagoons"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Ko Olina Lagoons"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Ko Olina Lagoons"].tags), Latitude = 21.3339, Longitude = -158.1219, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Germaine's Luau ──
        new Destination { DestinationId = dGermaines, DestinationName = "Germaine's Luau", CleanNormalizedSearchName = "germaines luau", MetaphoneCode = "JRMNS LW", DoubleMetaphonePrimary = "KRMNS LW", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "One of Oahu's longest-running luaus, held right on the beach on the island's leeward (west) side — a genuine imu ceremony (the underground pig roast) takes place before dinner, followed by a full Polynesian revue and an oceanfront sunset setting that Waikiki's hotel-ballroom luaus can't match.",
              "directions": "Best Access: Leeward Oahu, near Kapolei; round-trip transportation from Waikiki hotels is typically included or available as an add-on.\n\nAddress for Rideshare: Check current Germaine's Luau address in the Kapolei/Leeward area — most guests use the included shuttle rather than driving themselves.",
              "whatToKnow": "Imu Ceremony: The underground pig-roasting ceremony happens before the dinner service begins — arrive with your transportation/entry time in mind so you don't miss it.\n\nBeachfront Setting: Unlike ballroom-style luaus, this one is directly on the sand, with the sunset as a natural backdrop to the show.\n\nTransportation Included: Most packages include round-trip shuttle from Waikiki, which is worth taking given the leeward-side distance.",
              "thingsToBeWaryOf": "Distance from Waikiki: Roughly 45 minutes to an hour each way — factor this into your evening if self-driving instead of using the included shuttle.\n\nWeather: As an outdoor, beachfront event, an unusually windy or rainy evening can affect the experience — check the forecast.",
              "localPerspective": "Long-time Oahu luau-goers often rate the imu ceremony and oceanfront setting here above the more polished but less 'authentic-feeling' luaus concentrated in Waikiki itself.",
              "hiddenCost": "Package Pricing: Varies by seating tier (general, table service, VIP), typically $130-220 per adult including dinner, show, and transportation.\nChildren's Pricing: Usually discounted.",
              "nearbyComplements": [
                "Ko Olina Lagoons: A short drive, good for an afternoon beach stop before the evening luau.",
                "Kapolei: The surrounding town."
              ],
              "bestTimeToVisit": "Evening, timed to catch the imu ceremony before dinner and the sunset during the show.",
              "crowdLevel": "High (7/10) — a popular, well-attended nightly event.",
              "accessibility": "Rating: 7/10 — beachfront/sand seating with some accessible seating options; check ahead for specific mobility needs.",
              "idealDuration": "3 to 4 hours including transportation."
            }
            """,
            AverageCostPerDay = 175m, LuxuryRating = DeriveLuxury(175m, mapping["Germaine's Luau"].tags), AccessibilityType = "Transit",
            FamilyFriendlyScore = FamilyScore(mapping["Germaine's Luau"].tags), AdventurePaceScore = AdventureScore(mapping["Germaine's Luau"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Germaine's Luau"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Germaine's Luau"].tags), Latitude = 21.3389, Longitude = -158.1178, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Diamond Head State Monument ──
        new Destination { DestinationId = dDiamondHead, DestinationName = "Diamond Head State Monument", CleanNormalizedSearchName = "diamond head state monument", MetaphoneCode = "TMNT HT STT MNMNT", DoubleMetaphonePrimary = "TMNT HT STT MNMNT", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Oʻahu's most famous volcanic crater and skyline landmark, known in Hawaiian as Lēʻahi. The hike to the summit is a moderate 0.8-mile paved-and-stepped trail (round trip about 1.6 miles) climbing roughly 560 feet, passing through a WWII-era military bunker tunnel and up a steep final staircase to a lookout with sweeping views over Waikiki, Honolulu, and the south shore.",
              "directions": "Best Access: Gates open at 6:00 AM; the crater is a short drive or rideshare from Waikiki (about 10-15 minutes).\n\nAddress for Rideshare: Diamond Head State Monument, 3600 Diamond Head Rd, Honolulu, HI 96815.",
              "whatToKnow": "Reservation Required for Non-Residents: Book online up to 30 days in advance via the official Go State Parks Hawaii site; reservations open at midnight HST 30 days out, and popular sunrise slots (6:00-7:00 AM) sell out within hours. Hawaii residents with valid state ID get free entry and don't need a reservation.\n\nNot a True Sunrise Spot: The crater walls block the actual eastern horizon, so you won't see the sun rise from the summit — but the early golden-hour light over Waikiki is still spectacular.\n\nLast Entry: 4:00 PM, with the park closing at 4:30 PM — this isn't a sunset hike.",
              "thingsToBeWaryOf": "Exposed, Shadeless Trail: Bring at least a liter of water per person, a hat, and reef-safe sunscreen — there's no shade for most of the climb.\n\nSteep Final Stretch: The last section involves a steep staircase and a low-clearance tunnel — manageable for most families but not ideal for strollers or significant mobility limitations.\n\nSeparate from Hanauma Bay's Reservation System: Diamond Head's 30-day booking window is entirely separate from Hanauma Bay's 48-hour window — don't confuse the two timelines when planning.",
              "localPerspective": "Despite being Oahu's most touristed hike, locals still recommend it as genuinely worth the crowds — the summit view is one of the few spots that captures Waikiki's full coastal sweep in a single frame.",
              "hiddenCost": "Non-Resident Entry: Roughly $5 per person plus a $10 parking fee (if driving) — paid at time of online reservation.\nResident Entry: Free with valid Hawaii ID.",
              "nearbyComplements": [
                "Waikiki Beach: A short drive, ideal for a post-hike swim.",
                "Kapiolani Community College Farmers Market: On Saturdays, just east of the crater.",
                "Leonard's Bakery: A short drive for a post-hike malasada."
              ],
              "bestTimeToVisit": "The 6:00-7:00 AM slots for the coolest temperatures and best light, booked the moment your date opens 30 days out.",
              "crowdLevel": "High (7/10) even with the reservation cap, Maximum (10/10) informally at the summit lookout.",
              "accessibility": "Rating: 4/10 — a genuine uphill hike with stairs and a tunnel; not stroller or wheelchair accessible.",
              "idealDuration": "1.5 to 2 hours round trip including summit time."
            }
            """,
            AverageCostPerDay = 15m, LuxuryRating = DeriveLuxury(15m, mapping["Diamond Head State Monument"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Diamond Head State Monument"].tags), AdventurePaceScore = AdventureScore(mapping["Diamond Head State Monument"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Diamond Head State Monument"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Diamond Head State Monument"].tags), Latitude = 21.2620, Longitude = -157.8058, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Hanauma Bay Nature Preserve ──
        new Destination { DestinationId = dHanauma, DestinationName = "Hanauma Bay Nature Preserve", CleanNormalizedSearchName = "hanauma bay nature preserve", MetaphoneCode = "HNM B NTR PRSRF", DoubleMetaphonePrimary = "HNM P NTR PRSRF", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A volcanic crater bay turned protected marine conservation area, Hanauma Bay is Oahu's best easily accessible snorkeling — calm, shallow, coral-reef water with hundreds of tropical fish, a short walk from the parking area down to the sand. Once nearly loved to death by tourism, it's now carefully managed with a strict daily visitor cap.",
              "directions": "Best Access: A roughly 20-25 minute drive from Waikiki along the southeast coast.\n\nAddress for Rideshare: 100 Hanauma Bay Rd, Honolulu, HI 96825.",
              "whatToKnow": "Reservation Required: Book online exactly 48 hours (2 days) in advance, with the booking window opening at 7:00 AM HST — tickets for popular dates sell out within 15-20 minutes. About 400 online tickets are released per day on top of a small standby allotment.\n\nClosed Monday and Tuesday: The bay is closed those two days every week to let the reef rest — this is a critical scheduling detail, not optional, so confirm your target visit day isn't a Monday or Tuesday before booking anything else around it.\n\nHours: Open 6:45 AM-4:00 PM, with no entry after roughly 1:30-2:00 PM depending on the current policy.",
              "thingsToBeWaryOf": "Two Separate Fees: A $25 entry fee (non-residents) plus a separate $3 parking fee — budget for both.\nLimited Parking: Only about 300 stalls; even with a valid entry reservation, arrive with buffer time in case the lot is temporarily full.\nNo Feeding Fish/Touching Coral: Actively enforced conservation rules — staff will intervene if you touch the reef.",
              "localPerspective": "Longtime residents remember Hanauma Bay before the reservation system, when the reef was visibly deteriorating from overcrowding — many now support the visitor cap as a genuine conservation success, even though it makes spontaneous visits impossible.",
              "hiddenCost": "Entry Fee (non-resident, 13+): $25.\nParking: $3.\nChildren 12 and under, Hawaii residents, active military: Free entry.\nSnorkel Gear Rental (if needed): Available on-site for an additional fee.",
              "nearbyComplements": [
                "Makapuʻu Lighthouse Trail: A short drive further east.",
                "Koko Head Crater Trail: A short drive.",
                "Diamond Head: A short drive back toward Waikiki."
              ],
              "bestTimeToVisit": "The earliest available slot (7-8 AM) for the calmest water and best underwater visibility before the wind picks up.",
              "crowdLevel": "High (7/10) even with the cap, given the small overall park footprint.",
              "accessibility": "Rating: 6/10 — a tram is available for the steep path down to the beach for those who need it; the beach itself is sandy and uneven.",
              "idealDuration": "2 to 3 hours."
            }
            """,
            AverageCostPerDay = 28m, LuxuryRating = DeriveLuxury(28m, mapping["Hanauma Bay Nature Preserve"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Hanauma Bay Nature Preserve"].tags), AdventurePaceScore = AdventureScore(mapping["Hanauma Bay Nature Preserve"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Hanauma Bay Nature Preserve"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Hanauma Bay Nature Preserve"].tags), Latitude = 21.2690, Longitude = -157.6938, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Makapuʻu Lighthouse Trail ──
        new Destination { DestinationId = dMakapuu, DestinationName = "Makapuʻu Lighthouse Trail", CleanNormalizedSearchName = "makapuu lighthouse trail", MetaphoneCode = "MKP LTHS TRL", DoubleMetaphonePrimary = "MKP LTHS TRL", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A wide, fully paved 1.5-mile (one-way) trail climbing gently to a lighthouse overlook at Oahu's easternmost point — genuinely accessible compared to most Oahu hikes, with sweeping ocean views, seabird colonies on the offshore islets, and (in winter) a real chance of spotting migrating humpback whales from the trail itself.",
              "directions": "Best Access: A short drive east from Hanauma Bay, along the coastal highway.\n\nAddress for Rideshare: Makapuʻu Point Lighthouse Trail parking lot, Kalanianaʻole Hwy, Waimanalo, HI 96795.",
              "whatToKnow": "Paved and Wide: Unlike most Oahu hikes, this one is stroller- and wheelchair-manageable given its gentle, paved uphill grade — a rarity for a viewpoint this good.\n\nWinter Whale Watching: December through April is prime humpback whale season; bring binoculars if visiting in this window.\n\nSide Trail: A shorter spur trail leads to a lower sea-level overlook if you want a shorter option than the full lighthouse climb.",
              "thingsToBeWaryOf": "No Shade: Like Diamond Head, this is a fully exposed trail — water and sun protection matter.\n\nWindy at the Top: The lighthouse overlook point can be genuinely gusty; hold onto hats and loose items.",
              "localPerspective": "Families with young kids or grandparents in the group specifically favor this over Diamond Head or Koko Head for exactly this reason — it delivers a genuinely great Oahu coastal view without requiring a strenuous climb.",
              "hiddenCost": "Free entry and parking.",
              "nearbyComplements": [
                "Hanauma Bay: A short drive back toward Honolulu.",
                "Sea Life Park: Nearby, a paid marine park option if traveling with young kids.",
                "Sandy Beach: A short drive, a popular local bodysurfing spot."
              ],
              "bestTimeToVisit": "Early-to-mid afternoon, after Hanauma Bay, for good light and manageable heat.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "Rating: 9/10 — paved, gently graded, genuinely one of Oahu's most accessible viewpoint trails.",
              "idealDuration": "1 to 1.5 hours round trip."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Makapuʻu Lighthouse Trail"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Makapuʻu Lighthouse Trail"].tags), AdventurePaceScore = AdventureScore(mapping["Makapuʻu Lighthouse Trail"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Makapuʻu Lighthouse Trail"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Makapuʻu Lighthouse Trail"].tags), Latitude = 21.3106, Longitude = -157.6494, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Koko Head Crater Trail ──
        new Destination { DestinationId = dKokoHead, DestinationName = "Koko Head Crater Trail", CleanNormalizedSearchName = "koko head crater trail", MetaphoneCode = "KK HT KRTR TRL", DoubleMetaphonePrimary = "KK HT KRTR TRL", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Also called the Koko Head Railway Trail, this is Oahu's most punishing hike: over 1,000 steep, uneven, and often gapped former railway ties climbing directly up the crater's outer slope at a brutal grade, gaining roughly 1,000 feet in under a mile. The reward is a genuinely spectacular 360-degree summit view — but this is strictly for fit, motivated hikers, not a casual family outing.",
              "directions": "Best Access: Koko Head District Park, a short drive from Hanauma Bay.\n\nAddress for Rideshare: 423 Kaumakani St, Honolulu, HI 96825 (Koko Head District Park parking).",
              "whatToKnow": "Sunrise Attempts: Many hikers start around 5:30 AM specifically to beat both the heat and the crowds, timing the climb for sunrise light at the top.\n\nUneven Steps: The railway ties have real gaps and inconsistent heights — hiking poles and good grip shoes make a genuine difference.\n\nGenuine Workout: Expect real leg burn and a workout-level effort — this is closer to a stair-climbing challenge than a scenic walk.",
              "thingsToBeWaryOf": "Not for Everyone: This is explicitly not recommended for young children, anyone with knee or heart concerns, or casual hikers — Sandy Beach nearby is the better relaxed alternative if the group isn't up for it.\n\nHeat Risk: Because there's no shade and the climb is strenuous, heat exhaustion is a real risk on a hot day — bring more water than you think you need.\n\nDescending Is Harder Than It Looks: The uneven steps make the way down almost as demanding as the climb, given the strain on knees.",
              "localPerspective": "Local fitness enthusiasts use Koko Head as a genuine training hike, sometimes doing repeat climbs — it has a real reputation on the island as the toughest short trail on Oahu, treated almost like a badge of honor among regular hikers.",
              "hiddenCost": "Free entry and parking.",
              "nearbyComplements": [
                "Hanauma Bay: A short drive, a good lower-effort alternative or pairing.",
                "Sandy Beach: A short drive, the relaxed alternative for anyone skipping the climb."
              ],
              "bestTimeToVisit": "Sunrise (starting around 5:30 AM) for the coolest temperatures and best light — only for the fittest members of the group.",
              "crowdLevel": "Medium (5/10) — self-selecting given the difficulty.",
              "accessibility": "Rating: 2/10 — a genuinely strenuous, uneven climb; not suitable for most mobility levels.",
              "idealDuration": "1.5 to 2.5 hours round trip for a fit hiker."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Koko Head Crater Trail"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Koko Head Crater Trail"].tags), AdventurePaceScore = AdventureScore(mapping["Koko Head Crater Trail"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Koko Head Crater Trail"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Koko Head Crater Trail"].tags), Latitude = 21.2814, Longitude = -157.6997, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Mauka Warriors Luau ──
        new Destination { DestinationId = dMaukaWarriors, DestinationName = "Mauka Warriors Luau", CleanNormalizedSearchName = "mauka warriors luau", MetaphoneCode = "MK WRRRS LW", DoubleMetaphonePrimary = "MK WRRRS LW", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "important: Mauka Warriors Luau is located at Coral Crater Adventure Park / Hawaii Country Club in Kapolei — roughly 24 miles from Waikiki — not at the Hilton Hawaiian Village as sometimes assumed. It stands out among Oahu luaus for its specific focus on Polynesia's warrior culture and martial history (including a re-enactment tied to the historic Battle of Kīpapa), alongside the usual hula, fire-knife dancing, and imu-roasted feast.",
              "directions": "Best Access: Kapolei, on Oahu's leeward side; round-trip transportation is available from multiple Waikiki hotel pickup points, or drive yourself with free on-site parking.\n\nAddress for Rideshare: 91-1780 Midway St, Kapolei, HI 96707 (Hawaii Country Club / Coral Crater Adventure Park).",
              "whatToKnow": "Doors Open 5:00 PM: Showtime follows sunset rather than a fixed clock time, so it shifts slightly through the year — always begins the moment darkness falls.\n\nWarrior Culture Focus: A genuinely distinct angle among Oahu luaus, centered on Hawaiian and broader Polynesian warrior traditions that were historically suppressed and are being actively revived here.\n\nPre-Show Activities: Guests can try mini-golf and disc golf on the property before the show — Oahu's only championship disc golf course is on-site.\n\nTransportation Options: Waikiki hotel pickup runs roughly $30/person; Ko Olina-area resort shuttle is free given the closer proximity.",
              "thingsToBeWaryOf": "Genuinely Not Walkable from Waikiki: If you're combining this with an earlier activity in Honolulu the same day and returned your rental car, you'll need to book the paid shuttle or a rideshare — this venue is a real 40-45 minute drive from Waikiki, not a stroll from any Waikiki hotel.\n\nSchedule Around It: If pairing with an earlier full day (e.g., Diamond Head or Hanauma Bay), keep your rental car through the evening rather than returning it early, since getting to Kapolei afterward otherwise requires the paid shuttle or rideshare.",
              "localPerspective": "Locals and repeat visitors specifically single out the warrior-culture theme as setting it apart from more generic luau formats elsewhere on the island — reviewers consistently note it feels less like a standard tourist show and more like an actual cultural lesson with entertainment built around it.",
              "hiddenCost": "KOA Package: Entry-level dinner/show pricing.\nAliʻi/Mōʻī Packages: Higher tiers with better seating and additional inclusions.\nWaikiki Shuttle: Roughly $30/person round trip.\nKoʻOlina Resort Shuttle: Free.",
              "nearbyComplements": [
                "Ko Olina Lagoons: A short drive, good for an afternoon beach stop beforehand.",
                "Germaine's Luau: Also in the general Kapolei/leeward area, for comparison if choosing between the two."
              ],
              "bestTimeToVisit": "Arrive by 5:00 PM for doors and pre-show activities; the show itself begins at nightfall.",
              "crowdLevel": "Medium (5/10) — smaller and more intimate than some of Waikiki's larger luau productions.",
              "accessibility": "Rating: 7/10 — mostly flat outdoor venue; check current accommodations for specific mobility needs.",
              "idealDuration": "3 to 4 hours including transportation and pre-show activities."
            }
            """,
            AverageCostPerDay = 160m, LuxuryRating = DeriveLuxury(160m, mapping["Mauka Warriors Luau"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Mauka Warriors Luau"].tags), AdventurePaceScore = AdventureScore(mapping["Mauka Warriors Luau"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Mauka Warriors Luau"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Mauka Warriors Luau"].tags), Latitude = 21.3467, Longitude = -158.0894, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Lanikai Pillbox Trail ──
        new Destination { DestinationId = dLanikaiPillbox, DestinationName = "Lanikai Pillbox Trail", CleanNormalizedSearchName = "lanikai pillbox trail", MetaphoneCode = "LNK PLPKS TRL", DoubleMetaphonePrimary = "LNK PLPKS TRL", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A short, moderately steep hike up to a pair of WWII-era concrete bunkers (pillboxes) overlooking Kailua and Lanikai, with the postcard view of the twin Mokulua Islands sitting just offshore — one of Oahu's most photographed sunrise and daytime viewpoints, achievable in under an hour round trip.",
              "directions": "Best Access: Residential Lanikai neighborhood in Kailua, about 30-40 minutes from Waikiki or a short drive from Koko Head/Hanauma Bay.\n\nAddress for Rideshare: Trailhead near Kaelepulu Dr & Mokulua Dr, Kailua, HI 96734.",
              "whatToKnow": "Two Pillboxes: The first is a shorter, easier climb; the second bunker a bit further along offers an even better angle on the Mokulua Islands for those willing to continue.\n\nSunrise Popularity: A well-known sunrise spot — arrive very early if that's your goal, as the small trailhead area gets busy.\n\nResidential Parking: The trailhead sits in a quiet residential neighborhood — park respectfully and quietly, as this is a real community, not a tourist facility.",
              "thingsToBeWaryOf": "Steep, Loose Dirt Sections: Short but genuinely steep in parts, with loose dirt/gravel — sturdy shoes matter more than the short distance might suggest.\n\nNeighborhood Etiquette: Residents have real concerns about noise, trespassing, and parking congestion — stay on the marked trail and be mindful of the neighborhood.",
              "localPerspective": "Kailua residents have mixed feelings about the trail's viral popularity — many ask visitors to be extra respectful of the residential streets leading to the trailhead, given how much foot and car traffic the spot now draws.",
              "hiddenCost": "Free entry; no formal parking lot, so plan for street parking.",
              "nearbyComplements": [
                "Lanikai Beach: A short walk from the trailhead area.",
                "Kailua Beach: A short drive."
              ],
              "bestTimeToVisit": "Sunrise for the classic shot, or any clear morning before the heat builds.",
              "crowdLevel": "High (7/10) at sunrise, Medium (5/10) other times.",
              "accessibility": "Rating: 4/10 — short but steep and uneven; not suitable for strollers or significant mobility limitations.",
              "idealDuration": "45 minutes to 1 hour round trip."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Lanikai Pillbox Trail"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Lanikai Pillbox Trail"].tags), AdventurePaceScore = AdventureScore(mapping["Lanikai Pillbox Trail"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Lanikai Pillbox Trail"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Lanikai Pillbox Trail"].tags), Latitude = 21.3928, Longitude = -157.7147, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Kailua & Lanikai Beaches ──
        new Destination { DestinationId = dKailuaLanikaiBeaches, DestinationName = "Kailua & Lanikai Beaches", CleanNormalizedSearchName = "kailua lanikai beaches", MetaphoneCode = "KL LNK BXS", DoubleMetaphonePrimary = "KL LNK PXS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Two adjoining stretches of powder-soft white sand and turquoise, calm water repeatedly ranked among the best beaches in the world — Lanikai is the quieter, more residential, photogenic stretch with the Mokulua Islands view; Kailua Beach Park is larger, with actual facilities (restrooms, parking, picnic areas) and a wider stretch of sand for a full beach day.",
              "directions": "Best Access: Kailua town, windward Oahu, about 30-40 minutes from Waikiki.\n\nAddress for Rideshare: Kailua Beach Park, 526 Kawailoa Rd, Kailua, HI 96734 (for facilities); Lanikai Beach access points are along Mokulua Dr.",
              "whatToKnow": "Kailua Beach Park for Facilities: If you need restrooms, showers, and easier parking, Kailua Beach Park is the better base; Lanikai has essentially no facilities and very limited street parking.\n\nKayak/Paddleboard Rentals: Widely available near Kailua Beach Park, popular for exploring toward the Mokulua Islands.\n\nCombine with the Pillbox Trail: A natural pairing for a morning hike followed by an afternoon beach day in the same neighborhood.",
              "thingsToBeWaryOf": "Lanikai Parking Is Genuinely Limited: Street parking only, and it fills early — arrive before mid-morning or plan on a short walk from Kailua Beach Park instead.\n\nWind: Kailua/Lanikai's steady trade winds make it a popular windsurfing/kitesurfing spot, but can also mean a breezier beach day than expected.",
              "localPerspective": "Windward-side locals consider Kailua their own genuine hometown beach, distinct from the more tourist-dense Waikiki — many actively prefer it and treat weekend beach days here as a real community tradition, not just a visitor attraction.",
              "hiddenCost": "Free entry to both beaches.\nKailua Beach Park Parking: Free, limited.\nKayak/Paddleboard Rental (optional): $50-100 for a few hours.",
              "nearbyComplements": [
                "Lanikai Pillbox Trail: Immediately adjacent.",
                "Kailua town: A short drive for casual dining and shopping."
              ],
              "bestTimeToVisit": "Morning through early afternoon for the calmest water before the trade winds pick up.",
              "crowdLevel": "Medium (5/10) at Kailua Beach Park, Medium-High at Lanikai given limited space.",
              "accessibility": "Rating: 7/10 at Kailua Beach Park (facilities, flatter access); 5/10 at Lanikai (limited parking, less infrastructure).",
              "idealDuration": "2 to 4 hours."
            }
            """,
            AverageCostPerDay = 15m, LuxuryRating = DeriveLuxury(15m, mapping["Kailua & Lanikai Beaches"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Kailua & Lanikai Beaches"].tags), AdventurePaceScore = AdventureScore(mapping["Kailua & Lanikai Beaches"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Kailua & Lanikai Beaches"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Kailua & Lanikai Beaches"].tags), Latitude = 21.3972, Longitude = -157.7394, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Rainbow Drive-In ──
        new Destination { DestinationId = dRainbowDriveIn, DestinationName = "Rainbow Drive-In", CleanNormalizedSearchName = "rainbow drive in", MetaphoneCode = "RNB TRF N", DoubleMetaphonePrimary = "RNP TRF N", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A Kapahulu neighborhood institution since 1961 and one of Oahu's definitive plate lunch spots — the classic 'mixed plate' (usually two scoops of rice, mac salad, and a choice of protein like loco moco, mahi mahi, or BBQ chicken/beef) served fast and cheap under the iconic rainbow-arched sign.",
              "directions": "Best Access: Kapahulu Avenue, near Leonard's Bakery, a short drive or 15-20 minute walk from Waikiki.\n\nAddress for Rideshare: 3308 Kanaina Ave, Honolulu, HI 96815.",
              "whatToKnow": "Plate Lunch Format: Order at the counter, get a genuinely generous portion for the price — this is casual, quick, and meant to be eaten at outdoor picnic tables, not a sit-down restaurant experience.\n\nLoco Moco: A hamburger patty over rice, topped with gravy and a fried egg, is one of the most-recommended orders here specifically.\n\nCash and Card: Accepted, though cash can move faster during a lunch rush.",
              "thingsToBeWaryOf": "Lunch Rush Lines: Weekday lunch hours draw a real line of both locals and visitors — a little patience is part of the experience.\n\nLimited Seating: Outdoor picnic-style seating can fill up; some people get their order to go.",
              "localPerspective": "This is a genuine local institution, not a tourist-oriented plate lunch spot — Oahu residents have been eating here for generations, and it's frequently cited as the benchmark 'plate lunch done right' on the island.",
              "hiddenCost": "Mixed Plate: $12-16.\nLoco Moco: $10-13.\nSides: $3-5.",
              "nearbyComplements": [
                "Leonard's Bakery: A short walk for dessert.",
                "Waikiki Beach: A 15-20 minute walk or short drive."
              ],
              "bestTimeToVisit": "Slightly before or after the core noon-1 PM lunch rush.",
              "crowdLevel": "High (7/10) at peak lunch hours.",
              "accessibility": "Rating: 7/10 — counter service with outdoor picnic-style seating.",
              "idealDuration": "30 to 45 minutes."
            }
            """,
            AverageCostPerDay = 14m, LuxuryRating = DeriveLuxury(14m, mapping["Rainbow Drive-In"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Rainbow Drive-In"].tags), AdventurePaceScore = AdventureScore(mapping["Rainbow Drive-In"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Rainbow Drive-In"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Rainbow Drive-In"].tags), Latitude = 21.2871, Longitude = -157.8168, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Byodo-In Temple ──
        new Destination { DestinationId = dByodoIn, DestinationName = "Byodo-In Temple", CleanNormalizedSearchName = "byodo in temple", MetaphoneCode = "BT N TMPL", DoubleMetaphonePrimary = "PT N TMPL", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A non-practicing Japanese Buddhist temple built in 1968 to commemorate the 100th anniversary of the first Japanese immigrants' arrival in Hawaii, modeled after the 950-year-old Byodo-in Temple in Uji, Japan. Tucked into a valley in the Valley of the Temples Memorial Park, it's a serene setting with koi ponds, black swans, wild peacocks, and a massive brass Peace Bell you're invited to ring.",
              "directions": "Best Access: Kaneohe, windward Oahu, about 35-40 minutes from Waikiki.\n\nAddress for Rideshare: 47-200 Kahekili Hwy, Kaneohe, HI 96744.",
              "whatToKnow": "Ring the Peace Bell: Visitors are welcome to ring the large brass bell before entering — said to bring happiness and a long life.\n\nKoi Feeding: Fish food is often available for purchase to feed the large koi population in the ponds.\n\nMountain Backdrop: The temple sits directly beneath the dramatic, often cloud-wreathed Koolau mountain range — one of the most photogenic combinations on the island.",
              "thingsToBeWaryOf": "Located Within a Cemetery: The temple is part of the Valley of the Temples Memorial Park, an active cemetery — visitors should keep a respectful, quiet tone throughout the grounds.\n\nHumidity: The valley setting traps moisture; expect a noticeably muggier feel than the coast.",
              "localPerspective": "The temple holds genuine significance for Oahu's Japanese-American community as a tribute to plantation-era immigrants — locals treat it as a place of quiet reflection rather than just a photo backdrop, even though its striking looks make it a popular one.",
              "hiddenCost": "Admission: Roughly $5-6 per person.\nParking: Free.\nKoi Food: A small additional cost if purchased on-site.",
              "nearbyComplements": [
                "Hoʻomaluhia Botanical Garden: A short drive.",
                "Kualoa Ranch: A short drive further north."
              ],
              "bestTimeToVisit": "Late morning, when the light hits the pond and Koolau mountain backdrop well.",
              "crowdLevel": "Low (3/10), Medium (5/10) on weekends.",
              "accessibility": "Rating: 8/10 — flat paved paths throughout the temple grounds.",
              "idealDuration": "30 to 45 minutes."
            }
            """,
            AverageCostPerDay = 6m, LuxuryRating = DeriveLuxury(6m, mapping["Byodo-In Temple"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Byodo-In Temple"].tags), AdventurePaceScore = AdventureScore(mapping["Byodo-In Temple"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Byodo-In Temple"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Byodo-In Temple"].tags), Latitude = 21.4319, Longitude = -157.8394, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Hoʻomaluhia Botanical Garden ──
        new Destination { DestinationId = dHoomaluhia, DestinationName = "Hoʻomaluhia Botanical Garden", CleanNormalizedSearchName = "hoomaluhia botanical garden", MetaphoneCode = "HML BTNKL KRTN", DoubleMetaphonePrimary = "HML PTNKL KRTN", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A 400-acre county botanical garden built in part as a flood-control project, organized by geographic plant region (Africa, tropical America, Malaysia, Philippines, Hawaii) around a 32-acre lake, with the dramatic Koolau mountain range rising directly behind it — genuinely lush, quiet, and free.",
              "directions": "Best Access: Kaneohe, a short drive from Byodo-In Temple.\n\nAddress for Rideshare: 45-680 Luluku Rd, Kaneohe, HI 96744.",
              "whatToKnow": "Free Admission: One of the few major Oahu attractions with no entry fee at all.\n\nScenic Drive Loop: You can drive the garden's paved loop road for an easy overview, or park and walk the network of short nature trails for a closer look.\n\nCamping: The garden also offers a free county campground for those on a longer, more adventurous trip — permits required in advance.",
              "thingsToBeWaryOf": "Rain: This windward-side valley gets notably more rain than Waikiki — check the forecast and bring a light rain layer even on an otherwise sunny Oahu day.\n\nMosquitoes: The lush, wet environment means bug spray is worth packing.",
              "localPerspective": "Kaneohe locals use the garden as a genuine everyday green space — for walking, picnicking, and fishing at the lake (catch-and-release) — rather than treating it as a tourist attraction, which keeps it refreshingly uncrowded even on a nice day.",
              "hiddenCost": "Free entry and parking.\nCamping Permit (optional): Small fee if applying in advance.",
              "nearbyComplements": [
                "Byodo-In Temple: A short drive.",
                "Kualoa Ranch: A short drive further along the windward coast."
              ],
              "bestTimeToVisit": "Late morning, allowing time for the humidity to settle after any overnight rain.",
              "crowdLevel": "Low (3/10).",
              "accessibility": "Rating: 8/10 — the loop road and main trails are flat and paved; some side nature trails are less developed.",
              "idealDuration": "1 to 2 hours."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Hoʻomaluhia Botanical Garden"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Hoʻomaluhia Botanical Garden"].tags), AdventurePaceScore = AdventureScore(mapping["Hoʻomaluhia Botanical Garden"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Hoʻomaluhia Botanical Garden"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Hoʻomaluhia Botanical Garden"].tags), Latitude = 21.3897, Longitude = -157.8083, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Kualoa Ranch ──
        new Destination { DestinationId = dKualoa, DestinationName = "Kualoa Ranch", CleanNormalizedSearchName = "kualoa ranch", MetaphoneCode = "KL RNX", DoubleMetaphonePrimary = "KL RNX", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A 4,000-acre private nature reserve and working cattle ranch on Oahu's windward side, famous as a filming location for Jurassic Park, Jurassic World, Godzilla, Lost, and dozens of other productions. Multiple tour formats explore the valley's dramatic mountain scenery, movie sites, and coastline, most popularly via multi-passenger UTV Raptor off-road vehicles.",
              "directions": "Best Access: Windward Oahu, near Kaneohe, about 45 minutes to an hour from Waikiki.\n\nAddress for Rideshare: 49-560 Kamehameha Hwy, Kaaawa, HI 96730.",
              "whatToKnow": "Book 2-3 Weeks Ahead: The most popular tours — the UTV Raptor Jurassic Valley tour especially — genuinely sell out 2-3 weeks in advance during peak season, matching the source itinerary's own advice.\n\nTour Options: Beyond the UTV tour, options include horseback riding, a jungle expedition e-bike tour, an ocean voyage catamaran tour, and a Movie Sites & Ranch tour by bus for those who don't want to self-drive an off-road vehicle.\n\nVehicle Sharing: UTV Raptor vehicles seat 2-6 people and drivers can swap mid-tour, letting everyone in the group get a turn.",
              "thingsToBeWaryOf": "Full-Day Commitment if Combining Activities: Between the drive out, the tour itself, and time at the on-site marketplace/food trucks, this genuinely fills most of a day.\n\nWeather-Dependent: Off-road tours can be affected by heavy rain — check current conditions before your date.",
              "localPerspective": "Windward-side locals have mixed feelings about the ranch's fame — some appreciate the economic benefit to the area, while others note it's become one of the most commercially tour-heavy stops on an otherwise quiet stretch of coastline.",
              "hiddenCost": "UTV Raptor Tour: Roughly $150-220 per person depending on tour length (2-3 hours).\nMovie Sites & Ranch Bus Tour: Generally less expensive, roughly $60-90 per person.\nOther Tour Add-Ons: Horseback riding, e-bike, and catamaran tours priced separately.",
              "nearbyComplements": [
                "Hoʻomaluhia Botanical Garden: A short drive.",
                "Byodo-In Temple: A short drive."
              ],
              "bestTimeToVisit": "Morning tour slots for cooler temperatures during an active outdoor tour.",
              "crowdLevel": "High (7/10) — a genuinely popular, well-attended attraction.",
              "accessibility": "Rating: 6/10 for UTV tours (requires getting in/out of an off-road vehicle); the bus-based Movie Sites tour is more broadly accessible.",
              "idealDuration": "2 to 3 hours for the UTV tour; a half-day if combining with the marketplace and food trucks."
            }
            """,
            AverageCostPerDay = 180m, LuxuryRating = DeriveLuxury(180m, mapping["Kualoa Ranch"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Kualoa Ranch"].tags), AdventurePaceScore = AdventureScore(mapping["Kualoa Ranch"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Kualoa Ranch"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Kualoa Ranch"].tags), Latitude = 21.5228, Longitude = -157.8397, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── North Shore Shark Adventures ──
        new Destination { DestinationId = dSharkTour, DestinationName = "North Shore Shark Adventures", CleanNormalizedSearchName = "north shore shark adventures", MetaphoneCode = "NR0 XR XRK ATFNTRS", DoubleMetaphonePrimary = "NR0 XR XRK ATFNTRS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A roughly 3-mile boat ride off Haleiwa Harbor drops you into open water inside a floating surface cage (no scuba certification needed) alongside wild Galapagos and sandbar sharks — genuinely one of the most talked-about Oahu adventure activities, framed by operators as safe, well-managed, and surprisingly calming once underway rather than purely adrenaline-driven.",
              "directions": "Best Access: Departs from Haleiwa Harbor on the North Shore, about an hour to 90 minutes from Waikiki depending on traffic.\n\nAddress for Rideshare: Haleiwa Boat Harbor, 66-105 Haleiwa Rd, Haleiwa, HI 96712.",
              "whatToKnow": "No Certification Needed: The floating surface cage requires no diving experience or certification — you snorkel at the surface rather than descending.\n\nWeather Dependent: Trips can be postponed or cancelled for rough seas or high winds — book with some flexibility in your morning schedule.\n\nBooking Ahead: A few days' advance booking is generally sufficient, though popular time slots can fill during peak season.",
              "thingsToBeWaryOf": "Motion Sickness: The boat ride out and the time spent bobbing in open water can affect those prone to seasickness — consider motion sickness remedies in advance if this is a concern.\n\nNot Predictable Sightings: While sightings are common, wildlife encounters are never guaranteed — operators are upfront that this is real ocean wildlife, not a controlled exhibit.",
              "localPerspective": "North Shore watermen have mixed views on shark tourism generally, but most operators emphasize genuine conservation education alongside the thrill — reframing sharks as a species to respect and protect rather than fear, which resonates with the surf community's broader relationship with the ocean here.",
              "hiddenCost": "Tour Price: Roughly $120-150 per adult.\nGoPro Rental (optional): $20-40 if you want your own footage rather than photos included in the package.",
              "nearbyComplements": [
                "Waimea Bay: A short drive along the North Shore.",
                "Haleiwa town: A short walk from the harbor for shopping and food after the tour."
              ],
              "bestTimeToVisit": "Morning trips, both for calmer water conditions and to leave the rest of the day free for North Shore beaches.",
              "crowdLevel": "Medium (5/10) — capped by boat capacity per trip.",
              "accessibility": "Rating: 5/10 — requires getting into open water from a boat; not suitable for non-swimmers or significant mobility limitations.",
              "idealDuration": "2 to 3 hours including the boat ride."
            }
            """,
            AverageCostPerDay = 135m, LuxuryRating = DeriveLuxury(135m, mapping["North Shore Shark Adventures"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["North Shore Shark Adventures"].tags), AdventurePaceScore = AdventureScore(mapping["North Shore Shark Adventures"].tags), AestheticTrendScore = AestheticTrendScore(mapping["North Shore Shark Adventures"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["North Shore Shark Adventures"].tags), Latitude = 21.5928, Longitude = -158.1067, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Waimea Bay ──
        new Destination { DestinationId = dWaimeaBay, DestinationName = "Waimea Bay", CleanNormalizedSearchName = "waimea bay", MetaphoneCode = "WM B", DoubleMetaphonePrimary = "WM P", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "One of the most famous big-wave surf spots on Earth, drawing pro surfers to genuinely massive winter swells (30+ feet is not unheard of); in summer, the same bay transforms into a calm, family-friendly swimming spot with a rope swing off Jump Rock as a bonus attraction.",
              "directions": "Best Access: Along Kamehameha Highway on the North Shore, a short drive from Haleiwa.\n\nAddress for Rideshare: 61-031 Kamehameha Hwy, Haleiwa, HI 96712.",
              "whatToKnow": "Season Matters Enormously: Summer (roughly May-September) is calm and swimmable; winter brings genuinely dangerous surf that's spectator-only, even for strong swimmers.\n\nJump Rock: A cliff-jumping spot at the bay's edge, popular in calm summer conditions — assess your own comfort and ability before jumping.\n\nParking: A small lot fills quickly; roadside parking along the highway is common but walk carefully given traffic.",
              "thingsToBeWaryOf": "Winter Danger Is Real: Lifeguards close swimming access during big winter swells — respect posted flags and warnings without exception.\n\nShore Break: Even in calmer conditions, the shore break can be powerful — this isn't a lazy wading beach even in summer.",
              "localPerspective": "North Shore locals treat winter Waimea as a genuine spectator sport — crowds gather specifically to watch professional surfers tackle the huge swells, a completely different local experience than the summer swim-and-picnic version of the same beach.",
              "hiddenCost": "Free entry and parking (though limited).",
              "nearbyComplements": [
                "Haleiwa town: A short drive for lunch.",
                "Sunset Beach: A short drive further along the coast."
              ],
              "bestTimeToVisit": "Summer mornings for swimming; any winter day (safely, from shore) for watching big-wave surfing.",
              "crowdLevel": "Medium (5/10) summer, High (7/10) during notable winter swells.",
              "accessibility": "Rating: 7/10 — sandy beach access, generally flat.",
              "idealDuration": "1 to 2 hours."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Waimea Bay"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Waimea Bay"].tags), AdventurePaceScore = AdventureScore(mapping["Waimea Bay"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Waimea Bay"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Waimea Bay"].tags), Latitude = 21.6392, Longitude = -158.0656, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Sunset Beach ──
        new Destination { DestinationId = dSunsetBeach, DestinationName = "Sunset Beach", CleanNormalizedSearchName = "sunset beach", MetaphoneCode = "SNST BX", DoubleMetaphonePrimary = "SNST PX", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A long, wide stretch of golden sand on the North Shore's famous surf corridor, home to major professional surfing competitions in winter and, true to its name, one of the best unobstructed sunset views on the island year-round.",
              "directions": "Best Access: Along Kamehameha Highway, a short drive north of Waimea Bay.\n\nAddress for Rideshare: 59-104 Kamehameha Hwy, Haleiwa, HI 96712.",
              "whatToKnow": "Winter Surf Season: Home to major competition events (part of the North Shore's 'Triple Crown of Surfing' corridor alongside Waimea and Pipeline) — expect bigger crowds and road congestion during contest windows.\n\nWide, Open Sand: Genuinely spacious even when busy, making it a good photo stop even if you're not swimming.\n\nSunset Timing: True to its name, arriving in the final hour before sunset delivers reliably great light and color.",
              "thingsToBeWaryOf": "Strong Currents/Shore Break: Like much of the North Shore, ocean conditions can be genuinely powerful — check conditions and heed posted lifeguard warnings before swimming.\n\nRoad Congestion During Contests: If a surf competition is happening, expect slower traffic along Kamehameha Highway.",
              "localPerspective": "For the North Shore surf community, this beach is treated with the same reverence as a sports arena — locals plan their entire winter schedule around the contest windows here and at neighboring breaks.",
              "hiddenCost": "Free entry and parking.",
              "nearbyComplements": [
                "Waimea Bay: A short drive south.",
                "Haleiwa town: A short drive for food and shopping."
              ],
              "bestTimeToVisit": "The hour before sunset for the namesake view; winter for watching professional surf competitions from shore.",
              "crowdLevel": "Medium (5/10), High (7/10) during major surf contests.",
              "accessibility": "Rating: 7/10 — wide, generally flat sand access.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Sunset Beach"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Sunset Beach"].tags), AdventurePaceScore = AdventureScore(mapping["Sunset Beach"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Sunset Beach"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Sunset Beach"].tags), Latitude = 21.6656, Longitude = -158.0403, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Giovanni's Shrimp Truck ──
        new Destination { DestinationId = dGiovannis, DestinationName = "Giovanni's Shrimp Truck", CleanNormalizedSearchName = "giovannis shrimp truck", MetaphoneCode = "JFNS XRMP TRK", DoubleMetaphonePrimary = "JFNS XRMP TRK", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The original, most famous of the North Shore's garlic shrimp trucks, operating out of a graffiti-covered converted van since 1993 — the scampi-style shrimp plate (a dozen shrimp, shell-on, swimming in garlic butter, over two scoops of rice) is the definitive North Shore lunch stop.",
              "directions": "Best Access: Multiple North Shore locations; the original and most iconic is in Kahuku.\n\nAddress for Rideshare: 56-505 Kamehameha Hwy, Kahuku, HI 96731 (original truck location).",
              "whatToKnow": "Scampi Plate: The classic order — shell-on shrimp swimming in garlic butter sauce, genuinely messy to eat and worth the wet wipes.\n\nSpicy Option: A hot-and-spicy version is available for those who want more kick than the classic garlic butter.\n\nCash Recommended: Some food trucks in the area are cash-preferred — worth having some on hand.",
              "thingsToBeWaryOf": "Real Lines: As the most famous of the North Shore shrimp trucks, expect a genuine line, especially around lunchtime.\n\nMultiple 'Giovanni's': Be aware of imitators/similarly named trucks nearby — confirm you're at the original if that matters to you.",
              "localPerspective": "Oahu locals and North Shore surfers treat this as a genuine lunch staple, not just a tourist photo-op — the graffiti-covered truck itself has become a landmark in its own right after three decades in the same spot.",
              "hiddenCost": "Shrimp Plate: $17-20.\nDrinks: $2-4.",
              "nearbyComplements": [
                "Sunset Beach: A short drive.",
                "Matsumoto Shave Ice: A short drive for dessert after."
              ],
              "bestTimeToVisit": "Just before or after the core lunch rush (11:30 AM-1:30 PM) to minimize the wait.",
              "crowdLevel": "High (7/10) at lunch.",
              "accessibility": "Rating: 6/10 — roadside truck with outdoor picnic-table seating.",
              "idealDuration": "20 to 40 minutes."
            }
            """,
            AverageCostPerDay = 18m, LuxuryRating = DeriveLuxury(18m, mapping["Giovanni's Shrimp Truck"].tags), AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Giovanni's Shrimp Truck"].tags), AdventurePaceScore = AdventureScore(mapping["Giovanni's Shrimp Truck"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Giovanni's Shrimp Truck"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Giovanni's Shrimp Truck"].tags), Latitude = 21.6789, Longitude = -157.9539, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Matsumoto Shave Ice ──
        new Destination { DestinationId = dMatsumoto, DestinationName = "Matsumoto Shave Ice", CleanNormalizedSearchName = "matsumoto shave ice", MetaphoneCode = "MTSMT XF AS", DoubleMetaphonePrimary = "MTSMT XF AS", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Operating in the same Haleiwa storefront since 1951, Matsumoto's is the North Shore's most famous shave ice stand — genuinely finer, fluffier ice than a mainland snow cone, piled with a rainbow of syrup flavors and classic add-ins like sweetened azuki (red bean) paste, mochi balls, or a scoop of vanilla ice cream at the base.",
              "directions": "Best Access: Central Haleiwa town, walkable from the harbor area.\n\nAddress for Rideshare: 66-087 Kamehameha Hwy, Haleiwa, HI 96712.",
              "whatToKnow": "Classic Add-Ins: Azuki bean paste and mochi balls are the most traditional, most-recommended extras beyond the flavored syrups themselves.\n\nRainbow Flavor: The classic three-flavor 'Rainbow' is the most iconic order for first-timers.\n\nEat It Fast: Hawaii's heat means shave ice melts quickly — plan to eat it on-site rather than carrying it far.",
              "thingsToBeWaryOf": "Tourist-Season Lines: A near-constant line during peak season, especially midday — a quick stop can take longer than expected.\n\nSugar Content: A genuinely sweet treat — pace accordingly if visiting with young kids right before a meal.",
              "localPerspective": "Multiple generations of the same local families have been coming here since childhood — it's considered the definitive Oahu shave ice experience, with several competitor stands nearby explicitly measuring themselves against it.",
              "hiddenCost": "Regular Shave Ice: $5-7.\nWith Ice Cream/Azuki/Mochi Add-Ins: $7-10.",
              "nearbyComplements": [
                "Haleiwa Harbor: A short walk, departure point for North Shore Shark Adventures.",
                "Giovanni's Shrimp Truck: A short drive."
              ],
              "bestTimeToVisit": "Late afternoon after a day of North Shore beaches, or early to beat the midday line.",
              "crowdLevel": "High (7/10) midday and afternoon.",
              "accessibility": "Rating: 8/10 — flat, small storefront, easy walk-up counter service.",
              "idealDuration": "15 to 30 minutes."
            }
            """,
            AverageCostPerDay = 8m, LuxuryRating = DeriveLuxury(8m, mapping["Matsumoto Shave Ice"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Matsumoto Shave Ice"].tags), AdventurePaceScore = AdventureScore(mapping["Matsumoto Shave Ice"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Matsumoto Shave Ice"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Matsumoto Shave Ice"].tags), Latitude = 21.5964, Longitude = -158.1044, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Waikiki Beach ──
        new Destination { DestinationId = dWaikikiBeach, DestinationName = "Waikiki Beach", CleanNormalizedSearchName = "waikiki beach", MetaphoneCode = "WKK BX", DoubleMetaphonePrimary = "WKK PX", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Oahu's most famous shoreline and the birthplace of modern surf tourism, a roughly 2-mile stretch of hotels, beach bars, surf schools, and calm, swimmable water framed by Diamond Head to the east — the natural anchor for arrival and departure days when you don't want to plan anything more ambitious.",
              "directions": "Best Access: Central to most Waikiki hotels, entirely walkable within the neighborhood.\n\nAddress for Rideshare: Waikiki Beach, Honolulu, HI 96815 (multiple public access points along Kalakaua Ave).",
              "whatToKnow": "Beginner Surf Lessons: Widely available directly on the beach from independent instructors and surf schools — a good low-commitment way to try surfing for the first time.\n\nCatamaran Sails: Several operators run short sunset sail excursions departing directly from the sand.\n\nFree Public Beach: Despite the hotel-lined shore, the beach itself is fully public with no entry fee.",
              "thingsToBeWaryOf": "Crowded, Touristy Stretch: This is Oahu's busiest beach by a wide margin — don't expect a quiet, secluded experience.\n\nBeach Vendor Persistence: Some vendors selling lessons, rentals, or photos can be persistent — polite, firm 'no thank you' works fine if not interested.",
              "localPerspective": "Most Oahu locals head to quieter beaches for an actual relaxed day, treating Waikiki more as a place to work, shop, or take visiting friends — but it remains genuinely useful precisely because of how much is packed within walking distance.",
              "hiddenCost": "Free beach access.\nSurf Lesson: $75-120 for a group lesson.\nBeach Chair/Umbrella Rental: $15-30/day.",
              "nearbyComplements": [
                "Ala Moana Center: A short walk or trolley ride.",
                "House Without a Key: A short walk for sunset cocktails.",
                "Diamond Head: A short drive."
              ],
              "bestTimeToVisit": "Any day you want a low-effort beach day, ideally morning before the sand gets crowded.",
              "crowdLevel": "Maximum (10/10) — Oahu's busiest beach.",
              "accessibility": "Rating: 8/10 — flat, sandy, with multiple accessible entry points and beach mat pathways in places.",
              "idealDuration": "2 to 4 hours."
            }
            """,
            AverageCostPerDay = 20m, LuxuryRating = DeriveLuxury(20m, mapping["Waikiki Beach"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Waikiki Beach"].tags), AdventurePaceScore = AdventureScore(mapping["Waikiki Beach"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Waikiki Beach"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Waikiki Beach"].tags), Latitude = 21.2761, Longitude = -157.8269, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── Ala Moana Center ──
        new Destination { DestinationId = dAlaMoana, DestinationName = "Ala Moana Center", CleanNormalizedSearchName = "ala moana center", MetaphoneCode = "AL MN SNTR", DoubleMetaphonePrimary = "AL MN SNTR", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "One of the largest open-air shopping centers in the world, with over 350 stores spanning from local Hawaii-made goods to major international luxury brands, plus a genuinely excellent food court/dining scene — the default souvenir and last-minute-shopping stop for a Waikiki-based trip.",
              "directions": "Best Access: A short walk, trolley, or rideshare from most Waikiki hotels.\n\nAddress for Rideshare: 1450 Ala Moana Blvd, Honolulu, HI 96814.",
              "whatToKnow": "Local Goods Section: Look specifically for locally made products (Hawaiian-grown coffee, macadamia nuts, local artisan goods) rather than generic mainland-brand souvenirs.\n\nOpen-Air Design: Unlike a mainland mall, much of the center is open to the sky, with tropical landscaping throughout.\n\nFood Options: A wide range from casual food-court counters to full-service restaurants, good for a lunch break during shopping.",
              "thingsToBeWaryOf": "Genuinely Large: The mall spans multiple large buildings — a specific shopping list or map plan helps avoid wasted time wandering.\n\nParking: Multi-level parking structures can be confusing; note your level/section when you park.",
              "localPerspective": "Oahu residents use Ala Moana as a genuine everyday mall, not just a tourist shopping stop — it's the busiest shopping center in the state and reflects the island's actual retail habits, not a manufactured tourist experience.",
              "hiddenCost": "Free entry and browsing.\nParking: Free for the first few hours in most structures, then metered.",
              "nearbyComplements": [
                "Waikiki Beach: A short walk or trolley ride.",
                "Ala Moana Beach Park: Directly adjacent, a quieter local beach alternative to Waikiki."
              ],
              "bestTimeToVisit": "Weekday mornings for the calmest shopping experience.",
              "crowdLevel": "High (7/10), Maximum (10/10) on weekends.",
              "accessibility": "Rating: 9/10 — modern, fully accessible open-air mall with elevators and wide walkways.",
              "idealDuration": "1.5 to 3 hours."
            }
            """,
            AverageCostPerDay = 0m, LuxuryRating = DeriveLuxury(0m, mapping["Ala Moana Center"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Ala Moana Center"].tags), AdventurePaceScore = AdventureScore(mapping["Ala Moana Center"].tags), AestheticTrendScore = AestheticTrendScore(mapping["Ala Moana Center"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Ala Moana Center"].tags), Latitude = 21.2911, Longitude = -157.8434, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },

        // ── House Without a Key ──
        new Destination { DestinationId = dHouseWithoutAKey, DestinationName = "House Without a Key", CleanNormalizedSearchName = "house without a key", MetaphoneCode = "HS W0T K", DoubleMetaphonePrimary = "HS WTT K", DoubleMetaphoneSecondary = "", CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "An open-air oceanfront lounge at the Halekulani hotel, named after a 1925 Charlie Chan mystery novel set in Waikiki. It's built around one of the most classic Waikiki sunset traditions on the island: cocktails and pupus under a century-old kiawe tree, with a Hawaiian trio playing live music and a solo hula dancer performing at sunset most evenings.",
              "directions": "Best Access: The Halekulani hotel, central Waikiki, walkable from most Waikiki accommodations.\n\nAddress for Rideshare: 2199 Kalia Rd, Honolulu, HI 96815.",
              "whatToKnow": "Sunset Hula Show: A single hula dancer performs most evenings around sunset, accompanied by a live Hawaiian music trio — genuinely one of the more understated, elegant versions of this Waikiki tradition rather than a big luau-style production.\n\nNo Reservations for the Bar: Walk-in seating is typical for the outdoor lounge area, though dinner reservations at the adjacent restaurant can be made.\n\nDress Code: Smart casual is generally expected given the upscale hotel setting.",
              "thingsToBeWaryOf": "Prime Sunset Seats Fill Fast: Arrive at least 30-45 minutes before sunset if you want a table with an unobstructed ocean view.\n\nPremium Pricing: This is an upscale hotel lounge, priced accordingly — not a budget sunset-drinks option.",
              "localPerspective": "Longtime Oahu visitors and residents alike consider this one of the last genuinely classic, unhurried Waikiki sunset experiences — a deliberate contrast to the louder beach bars elsewhere along Kalakaua Avenue.",
              "hiddenCost": "Cocktails: $18-24.\nPupus (appetizers): $16-32.\nNo cover charge for the music/hula.",
              "nearbyComplements": [
                "Waikiki Beach: Directly adjacent.",
                "Diamond Head: A short walk or drive for a daytime view of the same coastline."
              ],
              "bestTimeToVisit": "30-45 minutes before sunset for the best table and the full hula/music experience.",
              "crowdLevel": "High (7/10) at sunset.",
              "accessibility": "Rating: 9/10 — flat, modern hotel-grounds accessibility.",
              "idealDuration": "1.5 to 2.5 hours."
            }
            """,
            AverageCostPerDay = 45m, LuxuryRating = DeriveLuxury(45m, mapping["House Without a Key"].tags), AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["House Without a Key"].tags), AdventurePaceScore = AdventureScore(mapping["House Without a Key"].tags), AestheticTrendScore = AestheticTrendScore(mapping["House Without a Key"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["House Without a Key"].tags), Latitude = 21.2870, Longitude = -157.8322, SearchHitCount = 0, TimeZone = "Hawaii-Aleutian Standard Time", SafetyLevel = 1, CountryId = usa.CountryId },
    };

            db.Destinations.AddRange(newDestinations);
            await db.SaveChangesAsync();
            var allDestinations = newDestinations.ToList();

            await AssignCategoriesAndTagsToDestinationsAsync(db, allDestinations, mapping);
            await SeedImagesForOahuAsync(db, allDestinations);
            await SeedDestinationLanguageAndCurrencyAsync(db, allDestinations, english, usd);

            db.DestinationCities.AddRange(new[]
            {
        new DestinationCity { DestinationId = dIolani, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dBishop, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dLeonards, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dPearlHarbor, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dBowfin, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dMissouri, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dKoOlina, CityId = kapolei.CityId },
        new DestinationCity { DestinationId = dGermaines, CityId = kapolei.CityId },
        new DestinationCity { DestinationId = dDiamondHead, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dHanauma, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dMakapuu, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dKokoHead, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dMaukaWarriors, CityId = kapolei.CityId },
        new DestinationCity { DestinationId = dLanikaiPillbox, CityId = kailua.CityId },
        new DestinationCity { DestinationId = dKailuaLanikaiBeaches, CityId = kailua.CityId },
        new DestinationCity { DestinationId = dRainbowDriveIn, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dByodoIn, CityId = kaneohe.CityId },
        new DestinationCity { DestinationId = dHoomaluhia, CityId = kaneohe.CityId },
        new DestinationCity { DestinationId = dKualoa, CityId = kaneohe.CityId },
        new DestinationCity { DestinationId = dSharkTour, CityId = haleiwa.CityId },
        new DestinationCity { DestinationId = dWaimeaBay, CityId = haleiwa.CityId },
        new DestinationCity { DestinationId = dSunsetBeach, CityId = haleiwa.CityId },
        new DestinationCity { DestinationId = dGiovannis, CityId = haleiwa.CityId },
        new DestinationCity { DestinationId = dMatsumoto, CityId = haleiwa.CityId },
        new DestinationCity { DestinationId = dWaikikiBeach, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dAlaMoana, CityId = honolulu.CityId },
        new DestinationCity { DestinationId = dHouseWithoutAKey, CityId = honolulu.CityId },
    });
            await db.SaveChangesAsync();

            // ─── Wishlist ─────────────────────────────────────────────────────────
            var wishlistId = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = wishlistId,
                WishlistName = "Hawaii for Beginners: Oahu",
                WishlistDescription = "A complete 7-day Oahu itinerary balancing history, hiking, luaus, and North Shore adventure — built around real reservation windows (Pearl Harbor, Hanauma Bay, Diamond Head, Kualoa Ranch) so you're not left scrambling day-of, with a smart car-rental strategy that keeps you walking or rideshare-only in Waikiki and driving only on true exploration days.",
                ShortStory = "Malasadas and monarchy history, a solemn morning at Pearl Harbor, a beach-torch luau, sunrise on a volcanic crater, a windward valley loop through Jurassic Park's real backdrop, and a cage full of sharks on the North Shore.",
                TotalDays = 7,
                PeopleType = "Active Travelers & Families Wanting to See 'All of Oahu'",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_hero.jpg",
                GlobalInclusionsJson = @"[""Compact Rental Car (Days 2-6)"",""USS Arizona Memorial Reservation"",""Hanauma Bay Reservation"",""Diamond Head Reservation"",""Kualoa Ranch UTV Tour""]",
                RawContentKeywords = "Oahu, Waikiki, Pearl Harbor, USS Arizona, Diamond Head, Hanauma Bay, Koko Head, Lanikai, Kailua, Kualoa Ranch, Byodo-In Temple, North Shore, Haleiwa, shark tour, luau, Germaine's, Mauka Warriors",
                PsychologicalVibeTagsJson = @"[""Adventure"",""Family"",""History"",""Beach""]",
                DefaultTravelersCount = 4,
                BasePricePerPerson = 950m,
                CalculatedTotalCost = 3800m,
                DepositAmountRequired = 200m,
                AccommodationInclusions = "Waikiki hotel for all 7 nights",
                TransitInclusions = "Compact rental car for Days 2-6 only; walking and rideshare in Waikiki on Days 1 and 7",
                ActivityInclusions = "Pearl Harbor USS Arizona Memorial reservation, Hanauma Bay reservation, Diamond Head reservation, Kualoa Ranch UTV tour, Germaine's Luau",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "Active Family / First-Time Oahu Visitor",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();

            // ─── Itinerary Days & Items ──────────────────────────────────────
            var day1 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Arrival + Waikiki Culture & Food (No Car)", MorningCityId = honolulu.CityId, AfternoonCityId = honolulu.CityId, EveningCityId = honolulu.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day2 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 2, DayTitle = "Pearl Harbor + West Side Luau (Rent a Car)", MorningCityId = honolulu.CityId, AfternoonCityId = kapolei.CityId, EveningCityId = kapolei.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day3 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 3, DayTitle = "South/East Coast Classics (Rent a Car)", MorningCityId = honolulu.CityId, AfternoonCityId = honolulu.CityId, EveningCityId = kapolei.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day4 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 4, DayTitle = "Koko Head Redux + Kailua/Lanikai (Rent a Car)", MorningCityId = honolulu.CityId, AfternoonCityId = kailua.CityId, EveningCityId = honolulu.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day5 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 5, DayTitle = "Windward Valley Wonders (Rent a Car)", MorningCityId = kaneohe.CityId, AfternoonCityId = kaneohe.CityId, EveningCityId = honolulu.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day6 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 6, DayTitle = "North Shore Adventure (Rent a Car)", MorningCityId = haleiwa.CityId, AfternoonCityId = haleiwa.CityId, EveningCityId = honolulu.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day7 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 7, DayTitle = "Relax, Souvenirs & Aloha Send-off (No Car)", MorningCityId = honolulu.CityId, AfternoonCityId = honolulu.CityId, EveningCityId = honolulu.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };

            db.ItineraryDays.AddRange(day1, day2, day3, day4, day5, day6, day7);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                // Day 1
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day1.ItineraryDayId, ItemTitle = "ʻIolani Palace", ItemDescription = "Book the 60-minute guided tour, or use the excellent audio tour for more flexibility.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_iolani.jpg", SocialProofBadge = "Royal History", IndividualCostModifier = 30m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day1.ItineraryDayId, ItemTitle = "Bishop Museum", ItemDescription = "Spend 2-3 hours exploring Hawaiian artifacts and the planetarium.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_bishop.jpg", SocialProofBadge = "Cultural Cornerstone", IndividualCostModifier = 28m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day1.ItineraryDayId, ItemTitle = "Leonard's Bakery", ItemDescription = "Get a hot malasada — the custard-filled haupia is a must. Buy an extra box for the next few days.", ItemOrderIndex = 3, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_leonards.jpg", SocialProofBadge = "Oahu Institution", IndividualCostModifier = 10m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day1.ItineraryDayId, ItemTitle = "Secret Food Tour in Waikiki", ItemDescription = "Covers dinner and a walking history lesson — poke, garlic shrimp, shave ice, and more. No car needed.", ItemOrderIndex = 4, TimeOfDay = "Evening", ImageUrl = "", SocialProofBadge = "No Car Needed", IndividualCostModifier = 95m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 2
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day2.ItineraryDayId, ItemTitle = "Pearl Harbor & USS Arizona Memorial", ItemDescription = "Pick up your rental car early (6:30 AM). Reserve via Recreation.gov, released daily at 3:00 PM HST — 8 weeks ahead, not 30 days as sometimes assumed.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_pearlharbor.jpg", SocialProofBadge = "Book 8 Weeks Ahead", IndividualCostModifier = 5m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day2.ItineraryDayId, ItemTitle = "USS Bowfin Submarine Museum & Park", ItemDescription = "Walk the actual submarine's interior — no extra reservation needed, right at the same complex.", ItemOrderIndex = 2, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_bowfin.jpg", SocialProofBadge = "No Reservation Needed", IndividualCostModifier = 22m, IsOptionalActivity = true, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day2.ItineraryDayId, ItemTitle = "USS Missouri Battleship", ItemDescription = "Ford Island shuttle required — the deck where Japan formally surrendered, ending WWII.", ItemOrderIndex = 3, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_missouri.jpg", SocialProofBadge = "Mighty Mo", IndividualCostModifier = 40m, IsOptionalActivity = true, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day2.ItineraryDayId, ItemTitle = "Ko Olina Lagoons", ItemDescription = "A gap before dinner — a calm, protected beach walk or snorkel, free and family-friendly.", ItemOrderIndex = 4, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_koolina.jpg", SocialProofBadge = "Calm Water", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day2.ItineraryDayId, ItemTitle = "Germaine's Luau", ItemDescription = "Right on the beach in Kapolei — the imu ceremony happens before dinner. Enjoy the show, the pig, and the ocean sunset.", ItemOrderIndex = 5, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_germaines.jpg", SocialProofBadge = "Beachfront Luau", IndividualCostModifier = 175m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 3
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day3.ItineraryDayId, ItemTitle = "Diamond Head State Monument", ItemDescription = "Gates open at 6 AM. Beat the heat and secure parking inside the crater — book your 30-day-out reservation the moment it opens.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_diamondhead.jpg", SocialProofBadge = "Book 30 Days Ahead", IndividualCostModifier = 15m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day3.ItineraryDayId, ItemTitle = "Hanauma Bay Nature Preserve", ItemDescription = "Reserved snorkel session, 2 hours in the calm, protected bay. Note: closed Mondays and Tuesdays — confirm your date works before booking.", ItemOrderIndex = 2, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_hanauma.jpg", SocialProofBadge = "Closed Mon/Tue", IndividualCostModifier = 28m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day3.ItineraryDayId, ItemTitle = "Makapuʻu Lighthouse Trail", ItemDescription = "A paved, easy 1.5-mile uphill walk with stunning views of the seabird colonies and (in winter) humpback whales.", ItemOrderIndex = 3, TimeOfDay = "Early Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_makapuu.jpg", SocialProofBadge = "Easiest Great View", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day3.ItineraryDayId, ItemTitle = "Optional: Koko Head Crater Trail", ItemDescription = "1,000+ steep railroad steps — only for the very fit; otherwise skip and relax at Sandy Beach.", ItemOrderIndex = 4, TimeOfDay = "Late Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_kokohead.jpg", SocialProofBadge = "Very Strenuous", IndividualCostModifier = 0m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day3.ItineraryDayId, ItemTitle = "Mauka Warriors Luau", ItemDescription = "Correction: this is in Kapolei, not walkable from Waikiki — keep your rental car through dinner instead of returning it beforehand. Excellent kālua pig and live warrior-culture storytelling.", ItemOrderIndex = 5, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_maukawarriors.jpg", SocialProofBadge = "Keep the Rental Car!", IndividualCostModifier = 160m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 4
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day4.ItineraryDayId, ItemTitle = "Optional: Koko Head Sunrise Hike", ItemDescription = "If you skipped it Day 3 — drive to Koko Head and conquer the stairs at sunrise. The 360° view is worth the burn.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_kokohead.jpg", SocialProofBadge = "Sunrise Option", IndividualCostModifier = 0m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day4.ItineraryDayId, ItemTitle = "Lanikai Pillbox Trail", ItemDescription = "A shorter, easier hike with postcard-perfect views of the Mokulua Islands — perfect for photos.", ItemOrderIndex = 2, TimeOfDay = "Late Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_lanikaipillbox.jpg", SocialProofBadge = "Iconic View", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day4.ItineraryDayId, ItemTitle = "Kailua & Lanikai Beaches", ItemDescription = "Two of the world's best white-sand beaches. Rent a kayak or paddleboard if you like.", ItemOrderIndex = 3, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_kailuabeach.jpg", SocialProofBadge = "World-Class Beach", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day4.ItineraryDayId, ItemTitle = "Dinner: Rainbow Drive-In", ItemDescription = "A casual, classic Oahu plate lunch (or dinner) spot back in Waikiki/Kapahulu.", ItemOrderIndex = 4, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_rainbowdrivein.jpg", SocialProofBadge = "Local Favorite", IndividualCostModifier = 14m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 5
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day5.ItineraryDayId, ItemTitle = "Byodo-In Temple", ItemDescription = "Feed the koi, ring the sacred Peace Bell, and enjoy the mountain backdrop.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_byodoin.jpg", SocialProofBadge = "Serene Setting", IndividualCostModifier = 6m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day5.ItineraryDayId, ItemTitle = "Hoʻomaluhia Botanical Garden", ItemDescription = "Drive the scenic loop, stop for picnic photos, and walk the short nature trails. Lush, green, and Jurassic-esque.", ItemOrderIndex = 2, TimeOfDay = "Mid-Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_hoomaluhia.jpg", SocialProofBadge = "Free Entry", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day5.ItineraryDayId, ItemTitle = "Kualoa Ranch: UTV Raptor or Movie Sites Tour", ItemDescription = "Book at least 1-2 weeks ahead (ATV/UTV go first). See the Jurassic Park valley and dramatic coastline.", ItemOrderIndex = 3, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_kualoa.jpg", SocialProofBadge = "Book 1-2 Weeks Ahead", IndividualCostModifier = 180m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 6
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day6.ItineraryDayId, ItemTitle = "North Shore Shark Adventures", ItemDescription = "Most operators leave from Haleiwa harbor. You'll be in a cage (or free-dive) with Galapagos sharks — safe, thrilling, and humbling.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_sharktour.jpg", SocialProofBadge = "Weather Dependent", IndividualCostModifier = 135m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day6.ItineraryDayId, ItemTitle = "Waimea Bay", ItemDescription = "Watch the big-wave surfers (winter) or swim in summer.", ItemOrderIndex = 2, TimeOfDay = "Late Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_waimeabay.jpg", SocialProofBadge = "Legendary Surf Spot", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day6.ItineraryDayId, ItemTitle = "Sunset Beach", ItemDescription = "An iconic North Shore photo stop.", ItemOrderIndex = 3, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_sunsetbeach.jpg", SocialProofBadge = "Photo Stop", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day6.ItineraryDayId, ItemTitle = "Lunch at Giovanni's Shrimp Truck", ItemDescription = "The classic North Shore garlic shrimp plate.", ItemOrderIndex = 4, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_giovannis.jpg", SocialProofBadge = "North Shore Classic", IndividualCostModifier = 18m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day6.ItineraryDayId, ItemTitle = "Matsumoto Shave Ice", ItemDescription = "Dessert stop — add azuki beans and mochi.", ItemOrderIndex = 5, TimeOfDay = "Late Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_matsumoto.jpg", SocialProofBadge = "Since 1951", IndividualCostModifier = 8m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 7
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day7.ItineraryDayId, ItemTitle = "Leonard's Bakery (Revisit)", ItemDescription = "One last malasada — bring a box home as gifts.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_leonards.jpg", SocialProofBadge = "One Last Malasada", IndividualCostModifier = 10m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day7.ItineraryDayId, ItemTitle = "Waikiki Beach", ItemDescription = "Swim, relax, and shop along Kalakaua Avenue.", ItemOrderIndex = 2, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_waikikibeach.jpg", SocialProofBadge = "Easy Beach Day", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day7.ItineraryDayId, ItemTitle = "Ala Moana Center", ItemDescription = "One of the largest open-air shopping centers in the world — good for last souvenirs.", ItemOrderIndex = 3, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_alamoana.jpg", SocialProofBadge = "Souvenirs", IndividualCostModifier = 0m, IsOptionalActivity = true, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day7.ItineraryDayId, ItemTitle = "House Without a Key", ItemDescription = "Sunset cocktails and a live hula show under the historic kiawe tree, then a final casual dinner in Waikiki.", ItemOrderIndex = 4, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_housewithoutakey.jpg", SocialProofBadge = "Classic Sunset", IndividualCostModifier = 45m, IsOptionalActivity = false, IsSelectedByDefault = true }
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

        private static async Task SeedImagesForOahuAsync(HodracDbContext db, List<Destination> destinations)
        {
            List<string> imageNames = new List<string>
    {
        "iolani.jpg", "bishop.jpg", "leonards.jpg", "pearlharbor.jpg", "bowfin.jpg", "missouri.jpg",
        "koolina.jpg", "germaines.jpg", "diamondhead.jpg", "hanauma.jpg", "makapuu.jpg", "kokohead.jpg",
        "maukawarriors.jpg", "lanikaipillbox.jpg", "kailuabeach.jpg", "rainbowdrivein.jpg", "byodoin.jpg",
        "hoomaluhia.jpg", "kualoa.jpg", "sharktour.jpg", "waimeabay.jpg", "sunsetbeach.jpg", "giovannis.jpg",
        "matsumoto.jpg", "waikikibeach.jpg", "alamoana.jpg", "housewithoutakey.jpg"
    };

            foreach (var (dest, image) in destinations.Zip(imageNames, (d, i) => (d, i)))
            {
                db.DestinationImages.Add(new DestinationImage
                {
                    DestinationImageId = Guid.NewGuid(),
                    DestinationId = dest.DestinationId,
                    ImageUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_{Uri.EscapeDataString(image)}",
                    ThumbnailUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/oahu_{Uri.EscapeDataString(image)}",
                    Caption = $"Hero image of {dest.DestinationName}",
                    DisplayOrder = 1,
                    ImageType = "Hero",
                    ShotContext = "Exterior",
                    IsAiGenerated = false
                });
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
    }
}
