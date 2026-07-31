using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Seeder;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    public class DallasSeeder
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ADD THIS METHOD TO THE EXISTING `DataSeeder` STATIC CLASS.
        // Reuses AssignCategoriesAndTagsToDestinationsAsync. Includes local
        // image/language/currency helpers (guard against duplicate method names
        // if SeedSeattleMatchDay / SeedLosAngelesMatchDayWishlists are already
        // in the same file — only one copy of SeedDestinationLanguageAndCurrencyAsync
        // should exist in the class).
        //
        // NOTE ON DescriptionJson KEYS: per request, all keys are camelCase in
        // this file (overview, directions, whatToKnow, thingsToBeWaryOf,
        // localPerspective, hiddenCost, nearbyComplements, bestTimeToVisit,
        // crowdLevel, accessibility, idealDuration) — a deliberate departure
        // from the TitleCase used in the Japan/Seattle/LA seeders.
        //
        // DESIGN NOTE ON WISHLIST 3: "The Ultimate Texas BBQ Tour" is NOT a
        // single-day itinerary — it's a "choose your stop" reference guide
        // spanning Arlington, Dallas, Fort Worth, and greater DFW. It's modeled
        // as a Wishlist with FIVE ItineraryDays, one per geographic cluster
        // (Arlington / Dallas / Fort Worth / Around DFW / Bonus Texas Flavors),
        // not as five literal calendar days. TotalDays reflects that it's meant
        // to be sampled across a multi-day trip, and almost every item is
        // optional so travelers pick what fits their route.
        // ═══════════════════════════════════════════════════════════════════════

        public static async Task SeedDallasFortWorthWishlists(HodracDbContext db)
        {
            // ─── Lookups ──────────────────────────────────────────────────────────
            var usa = await db.Countries.FirstAsync(c => c.CountryName == "United States of America");
            var english = await db.Languages.FirstAsync(l => l.LanguageName == "English");
            var usd = await db.Currencies.FirstAsync(c => c.CurrencyCode == "USD");

            async Task<City> GetOrCreateCityAsync(string name, double lat, double lng, string description)
            {
                var city = await db.Cities.FirstOrDefaultAsync(c => c.CityName == name);
                if (city == null)
                {
                    city = new City
                    {
                        CityId = Guid.NewGuid(),
                        CityName = name,
                        CountryId = usa.CountryId,
                        Latitude = lat,
                        Longitude = lng,
                        CityDescription = description
                    };
                    db.Cities.Add(city);
                    await db.SaveChangesAsync();
                }
                return city;
            }

            var arlington = await GetOrCreateCityAsync("Arlington", 32.7357, -97.1081,
                "Home to AT&T Stadium and Globe Life Field, sitting almost exactly between Dallas and Fort Worth with no direct rail connection to either.");
            var dallas = await GetOrCreateCityAsync("Dallas", 32.7767, -96.7970,
                "Texas's second-largest city, anchored by a walkable downtown core, the Arts District, and Deep Ellum's music and food scene.");
            var fortWorth = await GetOrCreateCityAsync("Fort Worth", 32.7555, -97.3308,
                "Known as Cowtown, home to the historic Stockyards, a fast-growing barbecue and Tex-Mex scene, and a more laid-back pace than Dallas.");
            var aledo = await GetOrCreateCityAsync("Aledo", 32.6957, -97.6142,
                "Small town just west of Fort Worth, increasingly known as a barbecue destination in its own right.");
            var coppell = await GetOrCreateCityAsync("Coppell", 32.9546, -97.0150,
                "Suburb between Dallas and Fort Worth near DFW Airport, home to one of the region's most popular barbecue institutions.");

            var mapping = new Dictionary<string, (string[] categories, string[] tags)>
            {
                ["The Henry"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "premium" }),
                ["AT&T Stadium"] = (new[] { "activity_experience", "landmark_monument" }, new[] { "sports_fan", "premium", "crowded", "tourist_hotspot", "architecture", "photography" }),
                ["FIFA Fan Festival (Fair Park, Dallas)"] = (new[] { "activity_experience", "entertainment_nightlife" }, new[] { "sports_fan", "social", "crowded", "budget_friendly" }),
                ["Texas Live!"] = (new[] { "entertainment_nightlife", "food_experience" }, new[] { "sports_fan", "social", "nightlife", "walkable" }),
                ["Hurtado Barbecue"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "budget_friendly" }),
                ["Cooper's Old Time Pit Bar-B-Que"] = (new[] { "food_experience" }, new[] { "food_focused", "historic", "local_favorite" }),

                ["The Giant Eyeball"] = (new[] { "landmark_monument" }, new[] { "quirky", "photography", "hidden_gem" }),
                ["Dallas Arts District"] = (new[] { "cultural_site", "neighborhood_district" }, new[] { "cultural", "walkable", "photography", "architecture" }),
                ["Nasher Sculpture Center"] = (new[] { "cultural_site" }, new[] { "cultural", "photography", "relaxing" }),
                ["Crow Museum of Asian Art"] = (new[] { "cultural_site" }, new[] { "cultural", "educational", "hidden_gem", "budget_friendly" }),
                ["Dallas Farmers Market"] = (new[] { "market_street_life", "food_experience" }, new[] { "food_focused", "walkable", "local_favorite" }),
                ["Reunion Tower GeO-Deck"] = (new[] { "viewpoint_scenic_spot", "landmark_monument" }, new[] { "photography", "tourist_hotspot", "architecture" }),
                ["Sixth Floor Museum at Dealey Plaza"] = (new[] { "historical_tour", "cultural_site" }, new[] { "history", "educational", "cultural" }),
                ["Dealey Plaza"] = (new[] { "historical_tour", "landmark_monument" }, new[] { "history", "photography", "walkable" }),
                ["Downtown Dallas Historic District"] = (new[] { "neighborhood_district", "historical_tour" }, new[] { "walkable", "history", "architecture", "photography" }),

                ["Terry Black's Barbecue"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "tourist_hotspot" }),
                ["Dayne's Craft Barbecue"] = (new[] { "food_experience" }, new[] { "food_focused", "hidden_gem", "local_favorite" }),
                ["Panther City BBQ"] = (new[] { "food_experience" }, new[] { "food_focused", "local_favorite", "cultural" }),
                ["Hard Eight BBQ"] = (new[] { "food_experience" }, new[] { "food_focused", "family_friendly", "local_favorite" }),
                ["Joe T. Garcia's"] = (new[] { "food_experience" }, new[] { "food_focused", "historic", "local_favorite", "social" }),
                ["Mariano's Hacienda Ranch"] = (new[] { "food_experience" }, new[] { "food_focused", "historic", "local_favorite" }),
                ["The Original Mexican Eats Cafe"] = (new[] { "food_experience" }, new[] { "food_focused", "historic", "local_favorite" }),
                ["Pulido's Kitchen & Cantina"] = (new[] { "food_experience" }, new[] { "food_focused", "historic", "local_favorite" }),
                ["Vidorra"] = (new[] { "food_experience", "entertainment_nightlife" }, new[] { "food_focused", "social", "nightlife", "premium" }),
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
            var destHenryId = Guid.NewGuid();
            var destATTStadiumId = Guid.NewGuid();
            var destFanFestivalId = Guid.NewGuid();
            var destTexasLiveId = Guid.NewGuid();
            var destHurtadoId = Guid.NewGuid();
            var destCoopersId = Guid.NewGuid();

            var destEyeballId = Guid.NewGuid();
            var destArtsDistrictId = Guid.NewGuid();
            var destNasherId = Guid.NewGuid();
            var destCrowMuseumId = Guid.NewGuid();
            var destFarmersMarketId = Guid.NewGuid();
            var destReunionTowerId = Guid.NewGuid();
            var destSixthFloorMuseumId = Guid.NewGuid();
            var destDealeyPlazaId = Guid.NewGuid();
            var destHistoricDistrictId = Guid.NewGuid();

            var destTerryBlacksId = Guid.NewGuid();
            var destDaynesId = Guid.NewGuid();
            var destPantherCityId = Guid.NewGuid();
            var destHardEightId = Guid.NewGuid();
            var destJoeTGarciasId = Guid.NewGuid();
            var destMarianosId = Guid.NewGuid();
            var destOriginalMexEatsId = Guid.NewGuid();
            var destPulidosId = Guid.NewGuid();
            var destVidorraId = Guid.NewGuid();

            // ─── Destinations with full JSON descriptions (camelCase keys) ─────
            var newDestinations = new[]
            {
        // ── The Henry ──
        new Destination
        {
            DestinationId = destHenryId,
            DestinationName = "The Henry",
            CleanNormalizedSearchName = "the henry",
            MetaphoneCode = "0 HNR",
            DoubleMetaphonePrimary = "T HNR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A modern American restaurant in Dallas's Oak Lawn neighborhood, The Henry has become one of the city's go-to breakfast and brunch spots — known for dishes like smoked salmon bagels and cinnamon sugar French toast, served in a bright, greenhouse-style dining room. It works equally well as the anchor breakfast for a Downtown Dallas day or as a quick stop if you're already staying in the Arlington area and want something familiar nearby.",
              "directions": "bestAccess: Oak Lawn neighborhood, a short drive or rideshare from Downtown Dallas.\n\naddressForRideshare: Check current address via OpenTable/Visit Dallas listing — the Oak Lawn location is the primary one referenced here; complimentary self-parking is available on-site.",
              "whatToKnow": "hours: Breakfast runs weekdays roughly 8:00-11:00 AM, with weekend brunch 11:00 AM-4:00 PM; the restaurant is open daily 8:00 AM into the evening.\n\nsignatureDishes: Smoked salmon bagel and cinnamon sugar French toast are the most-recommended breakfast items.\n\nparking: Complimentary self-parking is available seven days a week, with validated lunch and dinner parking as well.",
              "thingsToBeWaryOf": "weekendCrowds: Saturday and Sunday brunch gets genuinely busy — reservations are recommended, especially on a match-day weekend.\n\nreservationPolicy: Some visitors have reported being turned away without a reservation despite open tables — booking ahead online removes this risk entirely.",
              "localPerspective": "Dallas locals treat The Henry as a reliable, elevated brunch spot rather than a tourist destination — it's a genuine neighborhood favorite in Oak Lawn, a residential area known for tree-lined streets and easy access to the Dallas Arts District.",
              "hiddenCost": "breakfastEntree: $12-$20.\ncoffee: $4-$6.\nBrunch cocktails: $10-$15.",
              "nearbyComplements": [
                "Dallas Museum of Art and Nasher Sculpture Center: A short drive toward the Arts District.",
                "Downtown Dallas: A quick rideshare for the rest of a Downtown Dallas itinerary."
              ],
              "bestTimeToVisit": "Early Morning (8:00-9:00 AM) on a weekday for the quietest breakfast service before a match-day itinerary; expect a wait on weekend brunch.",
              "crowdLevel": "Medium (5/10) on weekday mornings, High (7/10) for weekend brunch.",
              "accessibility": "rating: 9/10 — modern restaurant with standard accessible seating and on-site parking.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 20m,
            LuxuryRating = DeriveLuxury(20m, mapping["The Henry"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["The Henry"].tags),
            AdventurePaceScore = AdventureScore(mapping["The Henry"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["The Henry"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["The Henry"].tags),
            Latitude = 32.8065,
            Longitude = -96.8058,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── AT&T Stadium ──
        new Destination
        {
            DestinationId = destATTStadiumId,
            DestinationName = "AT&T Stadium",
            CleanNormalizedSearchName = "att stadium",
            MetaphoneCode = "AT&T STTM",
            DoubleMetaphonePrimary = "AT&T STTM",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "AT&T Stadium is the largest single venue at the FIFA World Cup 2026, hosting nine matches — more than any other stadium in the tournament — including group-stage games, two Round of 32 matches, a Round of 16 match, and a semifinal. Capacity runs close to 93,000. As with other NFL-branded venues used for the tournament, FIFA does not permit corporate sponsor names in official branding, so the venue appears in FIFA materials as 'Dallas Stadium' — the building and gates are identical, only the official name differs.\n\nwhatToDo: Walk the plaza and take photos of the massive retractable-roof structure. Browse official team and tournament stores. Check the schedule for fan activations near the stadium entrances before heading in for the match.",
              "directions": "noDirectRail: AT&T Stadium has no direct rail connection — every route involves a drive, a transfer, or a walk, and this is the single most important planning fact for the venue.\n\ntreRoute: The Trinity Railway Express (TRE) connects Dallas Union Station and Fort Worth's Intermodal Transportation Center to the CentrePort/DFW Airport station, roughly 6-8 miles from the stadium; from there, a 15-25 minute Uber/Lyft covers the last leg (as of early 2026, no official FIFA shuttle from CentrePort has been confirmed — monitor FIFA.com/transportation for updates).\n\naddressForRideshare/GPS: 1 AT&T Way, Arlington, TX 76011 (also referred to as 'Dallas Stadium' in official FIFA materials).",
              "whatToKnow": "parking: Ticketed parking must be pre-purchased through official channels (e.g., JustPark) — there are no on-site parking sales on match days, and inventory sells out, especially for high-demand fixtures.\n\nbagPolicy: A clear bag policy is enforced for World Cup matches — check current size limits before you arrive.\n\ntiming: Allow up to 1.5 hours for the journey from Dallas or Fort Worth depending on traffic and crowd volumes; heavy inbound traffic on Stadium Drive and Collins Street typically starts about 3 hours before kickoff.",
              "thingsToBeWaryOf": "noPublicTransitInArlington: Arlington itself has no public mass transit system — rideshare, driving, or the TRE-plus-rideshare combination are essentially your only options.\n\nHighDemandMatches: Matches involving marquee teams and the semifinal are expected to generate the highest parking and transport demand — book parking and transport in advance for these.\n\nPostMatchExit: Official lots closest to the stadium typically stay congested for 45-60 minutes after the final whistle.",
              "localPerspective": "Arlington sits deliberately between Dallas and Fort Worth, and locals describe the Entertainment District (AT&T Stadium, Globe Life Field, and Texas Live! together) as effectively walkable once you're actually there — the challenge is entirely in the last-mile journey from wherever you're staying.",
              "hiddenCost": "matchTicket: Highly variable by fixture — official and resale pricing for World Cup matches at this venue can range widely, with the semifinal commanding the highest prices.\nparking: Approximately $75 for group-stage matches, $100 for the Round of 16 match, and up to $175 for the semifinal, per current confirmed pricing.\ntreFare: Approximately $2.50-$5.00, purchased via the GoPass app.\nRideshareFromCentrePort: Approximately $15-$25 pre-surge.",
              "nearbyComplements": [
                "Texas Live!: A short walk within the Entertainment District.",
                "Globe Life Field: Immediately adjacent, home of the Texas Rangers.",
                "Downtown Arlington: About a half-mile away, home to Hurtado Barbecue's original location."
              ],
              "bestTimeToVisit": "Arrive 2 to 2.5 hours before kickoff for a major match, allowing for parking, security, and walking time.",
              "crowdLevel": "Maximum (10/10) for a World Cup match, especially marquee fixtures and the semifinal.",
              "accessibility": "rating: 9/10 — full ADA-compliant seating, elevators throughout, and designated accessible escalators; fans with mobility needs may use all elevators during the match.",
              "idealDuration": "3.5 to 4.5 hours total including arrival, the match, and the post-match exit."
            }
            """,
            AverageCostPerDay = 280m,
            LuxuryRating = DeriveLuxury(280m, mapping["AT&T Stadium"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["AT&T Stadium"].tags),
            AdventurePaceScore = AdventureScore(mapping["AT&T Stadium"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["AT&T Stadium"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["AT&T Stadium"].tags),
            Latitude = 32.7473,
            Longitude = -97.0945,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── FIFA Fan Festival (Fair Park, Dallas) ──
        new Destination
        {
            DestinationId = destFanFestivalId,
            DestinationName = "FIFA Fan Festival (Fair Park, Dallas)",
            CleanNormalizedSearchName = "fifa fan festival fair park dallas",
            MetaphoneCode = "FF FN FSTFL",
            DoubleMetaphonePrimary = "FF FN FSTFL",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "important: the official FIFA Fan Festival for this host region is located at Fair Park in Dallas — not in Arlington — roughly 20 miles from AT&T Stadium. It's a free, ticketed (but no-cost) event running the full 39 days of the tournament (June 11-July 19), with giant screens broadcasting all 104 World Cup matches, live concerts, food, and interactive fan experiences across Fair Park's 1-million-square-foot footprint, centered on the Dos Equis Pavilion and two adjacent lots.",
              "directions": "addressForRideshare: 1818 1st Avenue, Dallas, TX 75210 (Fair Park); entrances at S. Fitzhugh Ave & 1st Ave, and Pennsylvania Ave & Lagow St.\n\nfromArlington: This is a real trip, not a quick add-on — budget significant drive/rideshare time each way if you're trying to combine it with an Arlington match day.",
              "whatToKnow": "tickets: Free but must be claimed in advance through the official festival website; paid upgrades (lounges, private bars) are also available.\n\nhours: Vary by day — some days run 10:00 AM-12:00 AM, others as late as 3:00 PM-5:00 PM; check the daily schedule before planning around it.\n\nscale: Roughly one million fans are expected across the festival's full run, with three concerts scheduled during the tournament.",
              "thingsToBeWaryOf": "distanceFromArlington: Given the roughly 20-mile gap and Arlington's lack of direct transit, this is not a realistic same-day pairing with an AT&T Stadium match unless you have significant time built in — treat it as an alternative Dallas-based World Cup activity rather than an add-on to an Arlington match day.\n\nlocalAlternative: If you want fan-zone energy without leaving Arlington, Texas Live!'s Live! Arena (100-foot LED screen) runs its own organized watch parties during major matches and is a genuinely comparable experience within the Entertainment District.",
              "localPerspective": "Fair Park is also the home of the State Fair of Texas and the largest collection of Art Deco exhibition buildings in the world — locals see the Fan Festival as a natural extension of Fair Park's long history as North Texas's biggest gathering space.",
              "hiddenCost": "generalAdmission: Free with advance reservation.\nGA+ and Legend tickets: Paid upgrades for enhanced experiences — pricing set by the festival.\nFoodAndDrink: $10-$25 per person at festival vendors.",
              "nearbyComplements": [
                "Cotton Bowl and other Fair Park venues: Within the same 277-acre complex.",
                "Deep Ellum: A short drive for dinner and nightlife afterward."
              ],
              "bestTimeToVisit": "On a non-match day if you're staying in Arlington, given the distance — or as the centerpiece of a separate Dallas-based day.",
              "crowdLevel": "Maximum (10/10) during marquee match broadcasts.",
              "accessibility": "rating: 8/10 — Fair Park is a large, mostly flat public event ground designed for major crowds.",
              "idealDuration": "2 to 4 hours, or longer if catching a full match broadcast plus a concert."
            }
            """,
            AverageCostPerDay = 15m,
            LuxuryRating = DeriveLuxury(15m, mapping["FIFA Fan Festival (Fair Park, Dallas)"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["FIFA Fan Festival (Fair Park, Dallas)"].tags),
            AdventurePaceScore = AdventureScore(mapping["FIFA Fan Festival (Fair Park, Dallas)"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["FIFA Fan Festival (Fair Park, Dallas)"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["FIFA Fan Festival (Fair Park, Dallas)"].tags),
            Latitude = 32.7815,
            Longitude = -96.7602,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Texas Live! ──
        new Destination
        {
            DestinationId = destTexasLiveId,
            DestinationName = "Texas Live!",
            CleanNormalizedSearchName = "texas live",
            MetaphoneCode = "TKSS LF",
            DoubleMetaphonePrimary = "TKSS LF",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A 200,000-square-foot dining and entertainment complex sitting directly between AT&T Stadium and Globe Life Field, Texas Live! is the natural gathering point for the entire Arlington Entertainment District — voted Best Entertainment District in DFW by the Dallas Morning News. It houses 22 bars, 8 dining concepts (including Lockhart Smokehouse and Troy Aikman's namesake restaurant), the flagship PBR Texas bar with mechanical bulls, and the Live! Arena, a multilevel sports bar built around a 100-foot LED screen that runs organized watch parties for major matches, including the 2026 World Cup.",
              "directions": "addressForRideshare: 1650 E Randol Mill Road, Arlington, TX 76011 — sits directly between Globe Life Field and AT&T Stadium, connected by Cowboys Way and Nolan Ryan Expressway, walkable without a car.\n\nwalkTime: The two stadiums are roughly a 5-13 minute walk apart depending on gate, with Texas Live! sitting right in the middle.",
              "whatToKnow": "hours: Generally open daily 11:00 AM-10:00 PM (later Friday/Saturday), though it becomes 21-and-over only after 9:00 PM.\n\nliveArena: The central watch-party hub, built around the 100-foot LED screen — during the World Cup, this is where the complex's biggest match-day energy concentrates.\n\nworldCupEvents: During the tournament, Texas Live! runs ticketed watch parties and table reservations for marquee matches (e.g., 'know before you go' notices for specific matchups) — check texas-live.com and their event calendar before a specific date.",
              "thingsToBeWaryOf": "parking: Not controlled by Texas Live! itself — pricing is set by individual lot owners and typically ranges $15-$30 per car on event days; Lot B is free on non-event days.\n\nEntryLogistics: For major matches, download the AXS app and have your QR code ready for fastest entry; re-entry policies vary by event and date, so check specifics for your match.\n\nFamiliesAfter9PM: The complex shifts to 21-and-over after 9:00 PM, so plan family visits earlier in the day.",
              "localPerspective": "Locals treat Texas Live! as the connective tissue of the district — the default plan for a Rangers-and-Cowboys doubleheader trip is dinner or drinks here between events, and it holds its own as a nightlife destination even with nothing scheduled at either stadium.",
              "hiddenCost": "coverCharge: Typically only for home-game nights at specific venues within the complex.\nFoodAndDrink: $15-$40 per person depending on venue.\nParking: $15-$30 per car on event days.\nWatchPartyTables/VIPSeating: Pricing varies significantly by match — book early for high-demand World Cup fixtures.",
              "nearbyComplements": [
                "AT&T Stadium: A short walk.",
                "Globe Life Field: A short walk in the opposite direction.",
                "Hurtado Barbecue's original location: About a half-mile in Downtown Arlington."
              ],
              "bestTimeToVisit": "Before kickoff for pre-match energy, or after the final whistle to keep celebrating without leaving the district.",
              "crowdLevel": "Maximum (10/10) immediately before and after a World Cup match.",
              "accessibility": "rating: 9/10 — modern, flat, wide walkways connecting the whole complex.",
              "idealDuration": "1.5 to 3 hours."
            }
            """,
            AverageCostPerDay = 45m,
            LuxuryRating = DeriveLuxury(45m, mapping["Texas Live!"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Texas Live!"].tags),
            AdventurePaceScore = AdventureScore(mapping["Texas Live!"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Texas Live!"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Texas Live!"].tags),
            Latitude = 32.7508,
            Longitude = -97.0827,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Hurtado Barbecue ──
        new Destination
        {
            DestinationId = destHurtadoId,
            DestinationName = "Hurtado Barbecue",
            CleanNormalizedSearchName = "hurtado barbecue",
            MetaphoneCode = "HRTT BRBK",
            DoubleMetaphonePrimary = "HRTT PRPK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Hurtado Barbecue's original brick-and-mortar restaurant sits in Downtown Arlington, about a half-mile from AT&T Stadium — the flagship location of one of the fastest-growing independently owned barbecue brands in Texas, and now the official barbecue restaurant of the Texas Rangers. Its signature 'Tex-Mex barbecue' style blends traditional smoked meats with Mexican rice, street corn, birria tacos, and brisket tostadas.",
              "directions": "addressForRideshare: 205 E Front St, Arlington, TX 76010 — about a half-mile walk from the AT&T Stadium/Texas Live! area.\n\nadditionalLocations: A concession stand inside Globe Life Field (Section 141) and further locations in Fort Worth, Dallas, and Mansfield.",
              "whatToKnow": "hours: Generally 11:00 AM-9:00 PM daily.\n\nsignatureFusion: Birria tacos, brisket tostadas, and Mexican street corn alongside traditional brisket, ribs, and sausage — a genuine hybrid rather than standard Central Texas barbecue.\n\norigin: Opened in Downtown Arlington in February 2020, just before the pandemic forced a pivot to online ordering, pre-orders, and curbside — a strategy that helped fuel its rapid regional growth.",
              "thingsToBeWaryOf": "matchDayLines: As the closest well-known barbecue stop to AT&T Stadium, expect a longer wait on major match days — arriving well before your planned kickoff window helps.\n\nSellOuts: Popular cuts can sell out by mid-afternoon on busy days, as is typical for Texas barbecue.",
              "localPerspective": "Locals consider Hurtado a genuine Arlington success story — from a 2019 food truck to brick-and-mortar locations across three DFW cities and an official Texas Rangers partnership in just a few years.",
              "hiddenCost": "meatByThePound: $18-$28 per pound depending on cut.\nTacos/Tostadas: $6-$12 each.\nCombo Plates: $16-$24.",
              "nearbyComplements": [
                "Texas Live!: A half-mile walk toward the stadiums.",
                "AT&T Stadium: A half-mile walk.",
                "Downtown Arlington: The surrounding neighborhood."
              ],
              "bestTimeToVisit": "Early-to-mid afternoon (11:30 AM-1:30 PM) for an early lunch before heading toward the stadium, ahead of peak dinner-hour lines.",
              "crowdLevel": "High (7/10) on match days.",
              "accessibility": "rating: 8/10 — standard restaurant seating, flat street-level entrance.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 22m,
            LuxuryRating = DeriveLuxury(22m, mapping["Hurtado Barbecue"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Hurtado Barbecue"].tags),
            AdventurePaceScore = AdventureScore(mapping["Hurtado Barbecue"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Hurtado Barbecue"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Hurtado Barbecue"].tags),
            Latitude = 32.7368,
            Longitude = -97.1086,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Cooper's Old Time Pit Bar-B-Que ──
        new Destination
        {
            DestinationId = destCoopersId,
            DestinationName = "Cooper's Old Time Pit Bar-B-Que",
            CleanNormalizedSearchName = "coopers old time pit bar b que",
            MetaphoneCode = "KPRS OLT TM PT",
            DoubleMetaphonePrimary = "KPRS OLT TM PT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Located in the historic Fort Worth Stockyards, a stone's throw from Billy Bob's Texas (the world's largest honky-tonk), Cooper's is a Texas Hill Country-style barbecue institution — order at a butcher counter where meat is sliced fresh in front of you, then move down the line for sides. Famous for its 'Big Chop,' plus beef ribs, brisket, and sausage, all cooked over mesquite coals.",
              "directions": "location: Fort Worth Stockyards National Historic District, near Billy Bob's Texas.\n\naddressForRideshare: Check current Fort Worth Stockyards address — the restaurant sits within easy walking distance of the Stockyards' main strip.\n\nfromArlington: This is genuinely a Fort Worth stop, not an Arlington one — best paired with a Fort Worth day rather than squeezed into an Arlington match day.",
              "whatToKnow": "orderingStyle: Walk in, get greeted by a butcher and a full display of barbecued meat, ask for recommendations, and get your meat sliced to order — then choose sides down the line (green beans, potato salad, and cobbler cups are popular).\n\nfreeExtras: Pickles, sliced onions, jalapeños, and pinto beans are free and unlimited at most Texas barbecue counters, including here.\n\nbeerSelection: Regional beers on tap, including Shiner Bock.",
              "thingsToBeWaryOf": "noFrillsAtmosphere: This is a classic, utilitarian Texas barbecue hall, not a polished restaurant — go for the food and the experience, not the ambiance.\n\nStockyardsCrowds: The surrounding Stockyards district draws heavy tourist traffic, especially around cattle-drive times and weekend evenings.",
              "localPerspective": "Cooper's is honored within the Texas barbecue community as 'Best of the Best' for its Big Chop, and its Stockyards location makes it a natural stop for anyone combining barbecue with Fort Worth's Old West heritage sites.",
              "hiddenCost": "meatByThePound: $18-$26 per pound depending on cut.\nSides: $4-$7 each.\nCobblerCup: $4-$6.\nBeer: $5-$8.",
              "nearbyComplements": [
                "Billy Bob's Texas: Immediately adjacent.",
                "Fort Worth Stockyards National Historic District: The surrounding neighborhood, including the twice-daily cattle drive."
              ],
              "bestTimeToVisit": "Midday (11:30 AM-1:30 PM) to combine with Stockyards sightseeing before the district gets busy in the evening.",
              "crowdLevel": "Medium (5/10) midday, High (7/10) weekend evenings.",
              "accessibility": "rating: 7/10 — flat, order-at-the-counter format typical of classic Texas barbecue halls.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 20m,
            LuxuryRating = DeriveLuxury(20m, mapping["Cooper's Old Time Pit Bar-B-Que"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Cooper's Old Time Pit Bar-B-Que"].tags),
            AdventurePaceScore = AdventureScore(mapping["Cooper's Old Time Pit Bar-B-Que"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Cooper's Old Time Pit Bar-B-Que"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Cooper's Old Time Pit Bar-B-Que"].tags),
            Latitude = 32.7897,
            Longitude = -97.3477,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── The Giant Eyeball ──
        new Destination
        {
            DestinationId = destEyeballId,
            DestinationName = "The Giant Eyeball",
            CleanNormalizedSearchName = "the giant eyeball",
            MetaphoneCode = "0 JNT ABL",
            DoubleMetaphonePrimary = "T KNT APL",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A 30-foot-tall, hyper-realistic sculpture of a human eyeball sitting on a downtown Dallas sidewalk — quirky, unmissable, and genuinely one of the most photographed pieces of public art in the city. It's a quick, fun photo stop rather than a destination to build a schedule around.",
              "directions": "location: Downtown Dallas, adjacent to a restaurant on Main Street.\n\naddressForRideshare: Main Street, Downtown Dallas — check current cross streets, as it sits near a specific restaurant frontage.",
              "whatToKnow": "photoOp: The sculpture is fenced, so you can't get right up to it, but it photographs well from the sidewalk.\n\nquickStop: Most visitors spend just a few minutes here as part of a longer downtown walk.",
              "thingsToBeWaryOf": "notMuchElse: There's no real 'experience' beyond the photo — set expectations accordingly and treat it as a five-minute detour, not a planned stop.",
              "localPerspective": "Dallas locals often stumble onto it while eating at the restaurant next door rather than seeking it out deliberately — it's a beloved oddity more than a headline attraction.",
              "hiddenCost": "free.",
              "nearbyComplements": [
                "Downtown Dallas Historic District: The surrounding area.",
                "Dallas Farmers Market: A short walk or drive."
              ],
              "bestTimeToVisit": "Any time during a downtown walk — daylight for the best photos.",
              "crowdLevel": "Low (3/10).",
              "accessibility": "rating: 10/10 — flat, open sidewalk.",
              "idealDuration": "5 to 10 minutes."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["The Giant Eyeball"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["The Giant Eyeball"].tags),
            AdventurePaceScore = AdventureScore(mapping["The Giant Eyeball"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["The Giant Eyeball"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["The Giant Eyeball"].tags),
            Latitude = 32.7809,
            Longitude = -96.8025,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Dallas Arts District ──
        new Destination
        {
            DestinationId = destArtsDistrictId,
            DestinationName = "Dallas Arts District",
            CleanNormalizedSearchName = "dallas arts district",
            MetaphoneCode = "TLS ARTS TSTRKT",
            DoubleMetaphonePrimary = "TLS ARTS TSTRKT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "At 68 acres and 19 blocks, the Dallas Arts District is the largest contiguous urban arts district in the nation — home to the Dallas Museum of Art, Nasher Sculpture Center, Crow Museum of Asian Art, the AT&T Performing Arts Center, and the Winspear Opera House, all within an easily walkable footprint. Klyde Warren Park, a 5.2-acre urban green space, connects the district to the Uptown neighborhood and hosts free daily programming.",
              "directions": "location: North of Downtown Dallas proper, centered along Flora Street.\n\naddressForRideshare: Flora Street & Harwood Street, Dallas, TX 75201 is a good central point.",
              "whatToKnow": "freeMuseums: The Dallas Museum of Art's permanent collection and the Crow Museum of Asian Art are always free; the Nasher charges general admission.\n\nklydeWarrenPark: Free daily programming including yoga, fitness classes, and weekend concerts — a good midday rest stop between museums.\n\nwalkability: Everything in the district is within a comfortable walk of everything else.",
              "thingsToBeWaryOf": "canEatAFullDay: With multiple major museums here, it's easy to over-schedule — pick two or three stops rather than attempting everything in one visit.\n\nSummerHeat: Texas summer heat is real; Klyde Warren Park has shade structures, but plan museum time during the hottest midday hours.",
              "localPerspective": "Locals describe Klyde Warren Park as one of the most important pieces of pedestrian infrastructure in recent Dallas history — it physically stitched together the Arts District and Uptown, two neighborhoods that used to be separated by a freeway.",
              "hiddenCost": "free to walk the district itself; individual museum admissions vary (see Nasher and Crow Museum entries).",
              "nearbyComplements": [
                "Nasher Sculpture Center: Within the district.",
                "Crow Museum of Asian Art: Within the district.",
                "Klyde Warren Park: The district's connective green space."
              ],
              "bestTimeToVisit": "Late morning into early afternoon, giving time to combine a museum visit with a walk through Klyde Warren Park.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 9/10 — modern, flat, well-paved streets and plazas throughout.",
              "idealDuration": "1.5 to 3 hours depending on how many museums you include."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["Dallas Arts District"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Dallas Arts District"].tags),
            AdventurePaceScore = AdventureScore(mapping["Dallas Arts District"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Dallas Arts District"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Dallas Arts District"].tags),
            Latitude = 32.7877,
            Longitude = -96.8000,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Nasher Sculpture Center ──
        new Destination
        {
            DestinationId = destNasherId,
            DestinationName = "Nasher Sculpture Center",
            CleanNormalizedSearchName = "nasher sculpture center",
            MetaphoneCode = "NXR SKLPTR SNTR",
            DoubleMetaphonePrimary = "NXR SKLPTR SNTR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Opened in 2003 and designed by Pritzker Prize-winning architect Renzo Piano, the Nasher Sculpture Center houses the Raymond and Patsy Nasher collection of over 300 modern and contemporary sculptures, including works by Rodin, Calder, Matisse, Picasso, and Giacometti — split between indoor galleries and a serene outdoor sculpture garden that terraces down to an open-air auditorium.",
              "directions": "addressForRideshare: 2001 Flora St, Dallas, TX 75201, adjacent to the Dallas Museum of Art in the Arts District.",
              "whatToKnow": "hours: Closed Mondays; check current hours for other days, as they can shift seasonally.\n\nadmission: Adults $10, DART riders $8 with valid proof of ticket, seniors 65+ $7, students with ID $5, children under 12 free, members/military/first responders free.\n\ngarden: A large portion of the collection is outdoors in the sculpture garden — genuinely one of the most relaxing spots in the district on a nice day.",
              "thingsToBeWaryOf": "smallerThanExpected: At 2.4 acres, it's compact compared to major encyclopedic museums — budget your time accordingly if you're also visiting the Dallas Museum of Art or Crow Museum the same day.\n\nWeatherDependent: Since much of the experience is the outdoor garden, extreme heat or rain will affect the visit.",
              "localPerspective": "Some locals describe it as underrated relative to its quality — a compact but genuinely world-class collection that doesn't always get the recognition of Dallas's larger museums, with the garden serving as an unofficial midday retreat for downtown office workers.",
              "hiddenCost": "adultAdmission: $10.\nDartRiderDiscount: $8.\nSeniorAdmission: $7.\nStudentAdmission: $5.\nChildrenUnder12/Members/Military/FirstResponders: Free.",
              "nearbyComplements": [
                "Dallas Museum of Art: Directly adjacent.",
                "Crow Museum of Asian Art: A short walk within the Arts District.",
                "Klyde Warren Park: A short walk."
              ],
              "bestTimeToVisit": "Late morning on a weekday for a quieter garden visit; closed Mondays.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 9/10 — modern building with elevator access; garden paths are paved and level.",
              "idealDuration": "1 to 2 hours."
            }
            """,
            AverageCostPerDay = 10m,
            LuxuryRating = DeriveLuxury(10m, mapping["Nasher Sculpture Center"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Nasher Sculpture Center"].tags),
            AdventurePaceScore = AdventureScore(mapping["Nasher Sculpture Center"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Nasher Sculpture Center"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Nasher Sculpture Center"].tags),
            Latitude = 32.7885,
            Longitude = -96.8009,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Crow Museum of Asian Art ──
        new Destination
        {
            DestinationId = destCrowMuseumId,
            DestinationName = "Crow Museum of Asian Art",
            CleanNormalizedSearchName = "crow museum of asian art",
            MetaphoneCode = "KR MSM AXN ART",
            DoubleMetaphonePrimary = "KR MSM AXN ART",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "One of only a handful of U.S. museums dedicated solely to the arts and cultures of Japan, China, India, and Southeast Asia, the Crow Museum houses the finest collection of Asian art in North Texas — including Chinese jade and bronzes spanning 3,000 years, Japanese netsuke and folding screens, and Indian temple sculpture — in a compact, beautifully lit building designed by Edward Larrabee Barnes. General admission is always free.",
              "directions": "addressForRideshare: 2010 Flora St, Dallas, TX 75201, in the Arts District near the Nasher Sculpture Center.",
              "whatToKnow": "alwaysFree: General admission is free year-round, making it one of the best-value stops in the entire Arts District.\n\ncollectionHighlight: The Chinese jade collection spans roughly 3,000 years of production, from Neolithic implements to Qing Dynasty decorative objects — widely considered the most technically extraordinary objects in the museum.\n\nLessCrowded: Consistently less crowded than its Arts District neighbors despite comparable quality, making it a good midday breather.",
              "thingsToBeWaryOf": "SmallerScale: Some visitors coming directly from the Dallas Museum of Art find the collection feels more limited by comparison — it rewards a focused, unhurried visit rather than being treated as an afterthought.\n\nRotatingFocus: The current on-view collection sometimes leans heavily toward one culture (e.g., predominantly Japanese pottery) depending on rotating exhibitions — check what's currently on view if a specific culture's art is the draw for you.",
              "localPerspective": "Frequently cited by local guides as 'the most undervisited excellent museum in Dallas' — locals who know the Arts District well often name it as their personal favorite over the larger, more crowded institutions nearby.",
              "hiddenCost": "free.",
              "nearbyComplements": [
                "Nasher Sculpture Center: A short walk.",
                "Dallas Museum of Art: A short walk.",
                "Klyde Warren Park: A short walk."
              ],
              "bestTimeToVisit": "Midday, as a quieter, air-conditioned break between other Arts District stops.",
              "crowdLevel": "Low (3/10).",
              "accessibility": "rating: 9/10 — compact, modern, single-building layout with elevator access.",
              "idealDuration": "30 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["Crow Museum of Asian Art"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Crow Museum of Asian Art"].tags),
            AdventurePaceScore = AdventureScore(mapping["Crow Museum of Asian Art"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Crow Museum of Asian Art"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Crow Museum of Asian Art"].tags),
            Latitude = 32.7884,
            Longitude = -96.8003,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Dallas Farmers Market ──
        new Destination
        {
            DestinationId = destFarmersMarketId,
            DestinationName = "Dallas Farmers Market",
            CleanNormalizedSearchName = "dallas farmers market",
            MetaphoneCode = "TLS FRMRS MRKT",
            DoubleMetaphonePrimary = "TLS FRMRS MRKT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Operating since 1941, the Dallas Farmers Market is a genuine daily market split into two parts: The Shed, a permanent food hall and vendor market with restaurants and specialty foods, and the weekend farmers market (Saturday and Sunday) with local, seasonal produce, flowers, and artisanal goods.",
              "directions": "addressForRideshare: 920 S Harwood St, Dallas, TX 75201, on the edge of downtown.",
              "whatToKnow": "twoParts: The Shed operates daily with restaurants and vendors; the outdoor farmers market specifically runs Saturday and Sunday.\n\nsouvenirShopping: Bullzerk and Lone Chimney are popular local vendors for one-of-a-kind Dallas trinkets and gifts.\n\nfreeEntry: No cost to browse the market itself — you only pay for what you buy.",
              "thingsToBeWaryOf": "WeekdayLimitedOfferings: If you're visiting on a weekday, you'll get The Shed's permanent vendors but miss the full outdoor farmers market experience — plan for a weekend if the produce market itself is the draw.\n\nParking: Free entry, but parking availability varies; check current lot options nearby.",
              "localPerspective": "This is one of Dallas's genuinely old institutions — locals have been coming here for fresh, seasonal food for over 80 years, and it remains a real working market rather than a purely tourist-oriented food hall.",
              "hiddenCost": "freeEntry.\nFoodAndSnacks: $8-$18 per item/meal at Shed vendors.\nProduceAndGoods: Varies by vendor.",
              "nearbyComplements": [
                "Downtown Dallas Historic District: A short walk.",
                "Deep Ellum: A short drive for dinner and nightlife."
              ],
              "bestTimeToVisit": "Saturday or Sunday morning for the full farmers market experience; any day for The Shed's permanent vendors.",
              "crowdLevel": "Medium (5/10) weekdays, High (7/10) weekend mornings.",
              "accessibility": "rating: 9/10 — flat, open-air and indoor market halls with wide aisles.",
              "idealDuration": "45 minutes to 1.5 hours."
            }
            """,
            AverageCostPerDay = 15m,
            LuxuryRating = DeriveLuxury(15m, mapping["Dallas Farmers Market"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Dallas Farmers Market"].tags),
            AdventurePaceScore = AdventureScore(mapping["Dallas Farmers Market"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Dallas Farmers Market"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Dallas Farmers Market"].tags),
            Latitude = 32.7746,
            Longitude = -96.7861,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Reunion Tower GeO-Deck ──
        new Destination
        {
            DestinationId = destReunionTowerId,
            DestinationName = "Reunion Tower GeO-Deck",
            CleanNormalizedSearchName = "reunion tower geo deck",
            MetaphoneCode = "RNN TWR JD DK",
            DoubleMetaphonePrimary = "RNN TWR K TK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The most recognizable element of the Dallas skyline since 1978, Reunion Tower's illuminated geodesic sphere sits atop a concrete shaft and houses the GeO-Deck, an indoor/outdoor observation level roughly 460-470 feet up. The deck includes an outdoor walkway and Halo, a hands-on digital screen system for learning about Dallas landmarks, plus Chef Wolfgang Puck's rotating restaurant Five Sixty and the more casual Cloud Nine Café.",
              "directions": "addressForRideshare: 300 Reunion Blvd E, Dallas, TX 75207.",
              "whatToKnow": "views: On clear days you can see the Trinity River corridor, the Fair Park dome, and even Fort Worth's skyline in the distance.\n\ncityPass: One of the attractions included in the CityPASS discount program, offering roughly 40% off admission when bundled.\n\ndining: Five Sixty (Wolfgang Puck) offers a dinner-with-a-view option if you want to combine the observation deck with a meal.",
              "thingsToBeWaryOf": "WeatherDependentViews: The outdoor walkway portion is exposed — check forecasts, as haze or rain will limit visibility.\n\nSunsetCrowds: Popular sunset time slots can draw lines; consider a mid-afternoon visit for a quieter experience.",
              "localPerspective": "Dallas locals treat the tower as the city's defining visual landmark — it appears in virtually every skyline photo of Dallas, and locals will often send out-of-town guests here first for orientation before exploring the rest of downtown.",
              "hiddenCost": "geODeckAdmission: Check current pricing; CityPASS bundling offers roughly 40% savings.\nFiveSixtyDinner: Fine-dining pricing, reservations recommended.",
              "nearbyComplements": [
                "Downtown Dallas Historic District: A short walk.",
                "Dallas Farmers Market: A short walk or drive."
              ],
              "bestTimeToVisit": "Late afternoon into sunset for the best light, though this is also the most popular window — a clear midday visit trades some atmosphere for a quieter deck.",
              "crowdLevel": "Medium (5/10) midday, High (7/10) at sunset.",
              "accessibility": "rating: 9/10 — elevator access to the deck; the outdoor walkway is level and wheelchair accessible.",
              "idealDuration": "45 minutes to 1.5 hours."
            }
            """,
            AverageCostPerDay = 25m,
            LuxuryRating = DeriveLuxury(25m, mapping["Reunion Tower GeO-Deck"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Reunion Tower GeO-Deck"].tags),
            AdventurePaceScore = AdventureScore(mapping["Reunion Tower GeO-Deck"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Reunion Tower GeO-Deck"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Reunion Tower GeO-Deck"].tags),
            Latitude = 32.7754,
            Longitude = -96.8088,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Sixth Floor Museum at Dealey Plaza ──
        new Destination
        {
            DestinationId = destSixthFloorMuseumId,
            DestinationName = "Sixth Floor Museum at Dealey Plaza",
            CleanNormalizedSearchName = "sixth floor museum at dealey plaza",
            MetaphoneCode = "SKS FLR MSM",
            DoubleMetaphonePrimary = "SKS FLR MSM",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Housed on the sixth floor of the former Texas School Book Depository, this museum offers one of the most in-depth accounts in existence of the assassination of President John F. Kennedy on November 22, 1963. Detailed artifacts, photographs, and film exhibits walk visitors through the events, the investigation, and JFK's legacy, with the window overlooking Dealey Plaza — the actual vantage point in question — as its most sobering exhibit.",
              "directions": "addressForRideshare: 411 Elm St, Dallas, TX 75202, at the edge of Dealey Plaza.",
              "whatToKnow": "audioGuide: A self-guided audio tour is included with admission and is highly recommended for the depth of context it provides.\n\ntiming: The exhibit is contained entirely on the sixth floor — when busy, expect to wait to move through certain sections at your own pace.\n\nparking: Nearby lots run around $15; metered street parking with a parking app is a cheaper alternative if available.",
              "thingsToBeWaryOf": "EmotionalWeight: This is a serious historical site, not a casual attraction — looking out over Commerce Street from the exhibit floor, where Lee Harvey Oswald is understood to have been positioned, is described by many visitors as genuinely unsettling.\n\nBusyWeekends: Memorial Day and other major holiday weekends can be significantly more crowded than average.",
              "localPerspective": "Locals consider it an essential, if heavy, stop for understanding a pivotal moment in American history that happened in their city — many combine it with a walk through the rest of Dealey Plaza and the nearby Old Red Courthouse (built 1892) and Kennedy Memorial.",
              "hiddenCost": "generalAdmission: Check current pricing, which includes the audio guide.\nParking: Approximately $15 in nearby lots.",
              "nearbyComplements": [
                "Dealey Plaza: Immediately surrounding the museum.",
                "Old Red Courthouse: A block away, built in 1892.",
                "John F. Kennedy Memorial Plaza: A short walk."
              ],
              "bestTimeToVisit": "Weekday mornings for a quieter, more contemplative visit; avoid major holiday weekends if possible.",
              "crowdLevel": "Medium (5/10) weekdays, High (7/10) weekends and holidays.",
              "accessibility": "rating: 8/10 — elevator access to the sixth floor; exhibit floor is wheelchair navigable.",
              "idealDuration": "1.5 to 2 hours."
            }
            """,
            AverageCostPerDay = 20m,
            LuxuryRating = DeriveLuxury(20m, mapping["Sixth Floor Museum at Dealey Plaza"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Sixth Floor Museum at Dealey Plaza"].tags),
            AdventurePaceScore = AdventureScore(mapping["Sixth Floor Museum at Dealey Plaza"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Sixth Floor Museum at Dealey Plaza"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Sixth Floor Museum at Dealey Plaza"].tags),
            Latitude = 32.7799,
            Longitude = -96.8085,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Dealey Plaza ──
        new Destination
        {
            DestinationId = destDealeyPlazaId,
            DestinationName = "Dealey Plaza",
            CleanNormalizedSearchName = "dealey plaza",
            MetaphoneCode = "TL PLS",
            DoubleMetaphonePrimary = "TL PLS",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A National Historic Landmark District and the site of President Kennedy's assassination, Dealey Plaza is free and open 24 hours — the grassy knoll, the white X marks on Elm Street noting the approximate shooting location, and the surrounding historic buildings make it a place many visitors experience in solemn silence before or after the Sixth Floor Museum next door.",
              "directions": "addressForRideshare: Elm St & Houston St, Dallas, TX 75202, directly adjacent to the Sixth Floor Museum.",
              "whatToKnow": "freeAndOpen: Unlike the museum, the plaza itself is free to walk through 24 hours a day.\n\npairing: Almost universally visited alongside the Sixth Floor Museum, since the museum's exhibits directly reference the plaza's landmarks.\n\nnearbyLandmarks: The Old Red Courthouse (1892) and the John F. Kennedy Memorial are within a short walk.",
              "thingsToBeWaryOf": "TrafficSafety: Elm Street remains an active road — be mindful of traffic when viewing or photographing the roadway markers.\n\nSolemnity: This is a historically significant and somber site; keep this in mind when planning photos or group activities here.",
              "localPerspective": "Dallas locals generally treat Dealey Plaza with quiet respect — it's woven into the city's identity as a place of genuine historical weight, not a typical tourist photo-op.",
              "hiddenCost": "free.",
              "nearbyComplements": [
                "Sixth Floor Museum: Directly adjacent.",
                "Old Red Courthouse: A short walk.",
                "John F. Kennedy Memorial Plaza: A short walk."
              ],
              "bestTimeToVisit": "Paired directly with a Sixth Floor Museum visit, any time of day.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 9/10 — flat, open plaza and sidewalks.",
              "idealDuration": "20 to 30 minutes."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["Dealey Plaza"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Dealey Plaza"].tags),
            AdventurePaceScore = AdventureScore(mapping["Dealey Plaza"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Dealey Plaza"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Dealey Plaza"].tags),
            Latitude = 32.7800,
            Longitude = -96.8089,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Downtown Dallas Historic District ──
        new Destination
        {
            DestinationId = destHistoricDistrictId,
            DestinationName = "Downtown Dallas Historic District",
            CleanNormalizedSearchName = "downtown dallas historic district",
            MetaphoneCode = "TNTN TLS HSTRK",
            DoubleMetaphonePrimary = "TNTN TLS HSTRK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The oldest section of Downtown Dallas, anchored around the Old Red Courthouse (1892), the John Neely Bryan log cabin replica (marking the city's first settler), and a compact grid of historic buildings within a two-block radius of Dealey Plaza. It's a natural final walking stop that ties together the morning's market visit and the afternoon's history stops before heading to Arlington.",
              "directions": "location: Centered around the Old Red Courthouse, near Dealey Plaza and the Sixth Floor Museum.\n\naddressForRideshare: Old Red Courthouse, 100 S Houston St, Dallas, TX 75202 is a good central point.",
              "whatToKnow": "compactArea: Most of the notable historic sites sit within about two blocks of each other, making this an easy final stroll.\n\noldRedCourthouse: The 1892 building itself is worth a look even without going inside — a striking piece of Romanesque Revival architecture.\n\njohnNeelyBryanCabin: A replica log cabin honoring Dallas's first permanent settler sits near the courthouse.",
              "thingsToBeWaryOf": "HeatInSummer: Little shade in parts of this area — plan for midday sun if visiting in summer.\n\nTimingForArlington: If this is your last downtown stop before heading to AT&T Stadium, remember the drive/rideshare could take up to 90 minutes with match-day traffic — build in a real buffer.",
              "localPerspective": "Locals describe this compact stretch as genuinely dense with history for its size — 'so much history in a two-block area,' as one longtime visitor guide put it.",
              "hiddenCost": "free to walk; individual site admissions vary.",
              "nearbyComplements": [
                "Dealey Plaza and Sixth Floor Museum: Immediately adjacent.",
                "Dallas Farmers Market: A short walk.",
                "Reunion Tower: A short walk."
              ],
              "bestTimeToVisit": "Late afternoon, as the final stop before heading toward Arlington and AT&T Stadium.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 8/10 — flat sidewalks with some historic, uneven paving in spots.",
              "idealDuration": "30 to 45 minutes."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["Downtown Dallas Historic District"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Downtown Dallas Historic District"].tags),
            AdventurePaceScore = AdventureScore(mapping["Downtown Dallas Historic District"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Downtown Dallas Historic District"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Downtown Dallas Historic District"].tags),
            Latitude = 32.7801,
            Longitude = -96.8078,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Terry Black's Barbecue ──
        new Destination
        {
            DestinationId = destTerryBlacksId,
            DestinationName = "Terry Black's Barbecue",
            CleanNormalizedSearchName = "terry blacks barbecue",
            MetaphoneCode = "TR BLKS BRBK",
            DoubleMetaphonePrimary = "TR PLKS PRPK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A premier destination for smoked meats in the DFW Metroplex, Terry Black's Deep Ellum location brings Lockhart, Texas-style Central Texas barbecue to Dallas — a long-standing staple known for its showstopping beef ribs and fan-favorite creamed corn, in an authentic, relaxed atmosphere with the smoke visible from the busy 7th Street/Deep Ellum corridor.",
              "directions": "addressForRideshare: 3025 Main St, Dallas, TX 75226, in Deep Ellum.\n\nfortWorthLocation: A second location also opened at 2926 W 7th St, Fort Worth, in December — check which is closer to your route.",
              "whatToKnow": "signatureDishes: Beef ribs and creamed corn are the most consistently recommended orders; the standard brisket, pork ribs, sausage, and chopped beef round out the menu.\n\ncentralTexasStyle: Meat is smoked simply with salt and pepper rub, in the Lockhart tradition, rather than heavily sauced.\n\nsides: Mac and cheese, green beans, potato salad, and coleslaw are the standard side lineup.",
              "thingsToBeWaryOf": "LunchLines: As a legendary DFW barbecue stop, expect real lines at peak lunch hours — arriving right at opening minimizes the wait.\n\nSellOuts: Popular cuts like beef ribs can sell out on busy days.",
              "localPerspective": "Deep Ellum locals consider Terry Black's the anchor of the neighborhood's culinary identity — a genuine institution that predates much of the area's more recent restaurant boom.",
              "hiddenCost": "meatByThePound: $20-$32 per pound depending on cut (beef ribs run highest).\nSides: $4-$7 each.\nComboPlates: $18-$28.",
              "nearbyComplements": [
                "Deep Ellum's music venues and bars: Immediately surrounding.",
                "Downtown Dallas: A short drive."
              ],
              "bestTimeToVisit": "Right at opening (typically 11:00 AM) for the shortest lines and best selection.",
              "crowdLevel": "High (7/10) at peak lunch hours.",
              "accessibility": "rating: 8/10 — order-at-the-counter format with standard restaurant seating.",
              "idealDuration": "45 minutes to 1 hour."
            }
            """,
            AverageCostPerDay = 24m,
            LuxuryRating = DeriveLuxury(24m, mapping["Terry Black's Barbecue"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Terry Black's Barbecue"].tags),
            AdventurePaceScore = AdventureScore(mapping["Terry Black's Barbecue"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Terry Black's Barbecue"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Terry Black's Barbecue"].tags),
            Latitude = 32.7845,
            Longitude = -96.7838,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Dayne's Craft Barbecue ──
        new Destination
        {
            DestinationId = destDaynesId,
            DestinationName = "Dayne's Craft Barbecue",
            CleanNormalizedSearchName = "daynes craft barbecue",
            MetaphoneCode = "TNS KRFT BRBK",
            DoubleMetaphonePrimary = "TNS KRFT PRPK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "important: Dayne's Craft Barbecue's brick-and-mortar restaurant is located in Aledo, just west of Fort Worth, not in Fort Worth proper — a genuine word-of-mouth barbecue powerhouse that started as a backyard smoker hobby in 2017 and grew into one of the most acclaimed barbecue spots in Texas, landing on Texas Monthly's Top 10 statewide barbecue list. The 'Fort Worth the Wait' platter (a half-pound each of brisket, turkey, pork ribs, and pulled pork, plus two house-made sausages and five sides) is the signature order.",
              "directions": "addressForRideshare: 100 S Front St, Aledo, TX 76008 — roughly a 20-25 minute drive west of downtown Fort Worth.\n\nweekendOnly: Open Friday through Sunday only — this is not a weekday option.",
              "whatToKnow": "signatureSausages: Blueberry-Gouda and jalapeño-Havarti sausages are standout, creative options alongside traditional beef.\n\nogBurger: Made from brisket trim — a favorite alongside the classic 'Texas trinity' of brisket, ribs, and sausage.\n\nsellOutRisk: A genuinely small operation — arrive early, as they regularly sell out before closing.",
              "thingsToBeWaryOf": "LimitedDays: Open Friday-Sunday only, so this won't work for a weekday visit.\n\nDistance: The 20-25 minute drive from Fort Worth is worth factoring into your day's routing, especially if combining with other Fort Worth stops.",
              "localPerspective": "North Texas barbecue fans travel specifically to Aledo for this spot — it's earned a reputation as one of the best barbecue destinations in the entire state, not just DFW, through Texas Monthly's rigorous statewide rankings.",
              "hiddenCost": "fortWorthTheWaitPlatter: $95 (serves a group).\nMeatByThePound: $20-$30 depending on cut.\nOgBurger: $12-$16.",
              "nearbyComplements": [
                "Fort Worth Stockyards: About a 25-minute drive.",
                "Downtown Fort Worth: About a 20-minute drive."
              ],
              "bestTimeToVisit": "Friday-Sunday, arriving as close to opening as possible to beat sell-outs.",
              "crowdLevel": "High (7/10) — genuine lines are part of the experience.",
              "accessibility": "rating: 7/10 — indoor dining area plus a shaded outdoor patio.",
              "idealDuration": "1 to 1.5 hours including wait time."
            }
            """,
            AverageCostPerDay = 30m,
            LuxuryRating = DeriveLuxury(30m, mapping["Dayne's Craft Barbecue"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Dayne's Craft Barbecue"].tags),
            AdventurePaceScore = AdventureScore(mapping["Dayne's Craft Barbecue"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Dayne's Craft Barbecue"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Dayne's Craft Barbecue"].tags),
            Latitude = 32.6958,
            Longitude = -97.6013,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Panther City BBQ ──
        new Destination
        {
            DestinationId = destPantherCityId,
            DestinationName = "Panther City BBQ",
            CleanNormalizedSearchName = "panther city bbq",
            MetaphoneCode = "PN0R ST BBK",
            DoubleMetaphonePrimary = "PNTR ST PPK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Named in honor of Fort Worth's 'Panther City' nickname, this Southside Fort Worth spot started as a food truck in 2014 and has grown into a Michelin Guide-recognized, Texas Monthly Top 50 institution. It blends Central Texas-style barbecue (brisket slow-cooked on offset pits over post oak wood) with Tex-Mex flourishes like brisket burritos, brisket elote, and quesabirria-style street tacos.",
              "directions": "addressForRideshare: 201 E Hattie St, Fort Worth, TX 76104, in Southside Fort Worth.",
              "whatToKnow": "hours: Wednesday-Sunday, roughly 11:00 AM-8:00 PM; closed Monday and Tuesday.\n\nsignatureItems: Pepper-heavy brisket, pork belly burnt ends, spicy smoked mac and cheese, and the Tex-Mex-leaning brisket tacos and quesabirria tacos.\n\nbarWhileWaiting: A full bar lets you order a drink while someone else in your group holds the (often long) line.",
              "thingsToBeWaryOf": "LongLines: Lines regularly wrap around the building, especially since the Michelin Guide recognition — arrive early to minimize the wait.\n\nParking: Can be genuinely challenging near the restaurant on busy days.",
              "localPerspective": "D Magazine described the brisket's smoke ring as looking 'painted by Monet' — a nod to how seriously Fort Worth's barbecue community takes this spot, which locals see as a genuine hometown success story from food truck to Michelin recognition.",
              "hiddenCost": "meatByThePound: $20-$30 depending on cut.\nTacos: $4-$7 each.\nSides: $4-$6 each.",
              "nearbyComplements": [
                "Fort Worth Stockyards: About a 10-minute drive.",
                "Downtown Fort Worth: A short drive."
              ],
              "bestTimeToVisit": "Right at opening (11:00 AM) on a Wednesday-Sunday visit to beat the line.",
              "crowdLevel": "High (7/10) — long lines are a known part of the experience.",
              "accessibility": "rating: 7/10 — indoor and large covered outdoor seating.",
              "idealDuration": "1 to 1.5 hours including wait time."
            }
            """,
            AverageCostPerDay = 26m,
            LuxuryRating = DeriveLuxury(26m, mapping["Panther City BBQ"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Panther City BBQ"].tags),
            AdventurePaceScore = AdventureScore(mapping["Panther City BBQ"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Panther City BBQ"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Panther City BBQ"].tags),
            Latitude = 32.7307,
            Longitude = -97.3277,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Hard Eight BBQ ──
        new Destination
        {
            DestinationId = destHardEightId,
            DestinationName = "Hard Eight BBQ",
            CleanNormalizedSearchName = "hard eight bbq",
            MetaphoneCode = "HRT AT BBK",
            DoubleMetaphonePrimary = "HRT AT PPK",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A family-run Texas institution since firing up its first pit in Stephenville in 2003, Hard Eight BBQ's Coppell location (roughly midway between Dallas and Fort Worth, near DFW Airport) is the quintessential big-crowd Texas barbecue experience — walk-the-pits, point-at-what-you-want ordering with a live smoke show up front, large portions, and a rustic, cafeteria-style dining hall.",
              "directions": "addressForRideshare: 688 Freeport Pkwy, Coppell, TX 75019.",
              "whatToKnow": "hours: Monday-Thursday 10:30 AM-9:00 PM, Friday-Saturday 10:30 AM-10:00 PM, Sunday 10:30 AM-9:00 PM.\n\norderingStyle: Walk along the display pits and point at what you want — brisket, ribs, sausage, pork chops, shrimp poppers, sirloin, and even smoked bologna are all available by the pound.\n\nfamilyFriendly: Large cafeteria-style seating makes it genuinely easy for bigger groups and families.",
              "thingsToBeWaryOf": "PeakWaits: Popular time slots (Friday evenings especially) can mean waits of up to an hour, even with the high-volume operation.\n\nQualityVariance: Some visitors note quality can vary meal to meal on certain cuts (particularly pork chops and turkey) given the sheer volume served — the brisket and ribs are generally the safest bets.",
              "localPerspective": "Often compared locally to Austin's famous Salt Lick — the North Texas version of the 'required Texas barbecue experience' that locals take out-of-town guests to specifically for the spectacle of the live pits as much as the food itself.",
              "hiddenCost": "meatByThePound: $16-$25 depending on cut.\nComboPlates: $18-$30.\nPorkChop/Sirloin: Priced higher, around $20-$25.",
              "nearbyComplements": [
                "DFW Airport: A short drive, convenient if arriving/departing by air.",
                "Grapevine: A short drive for additional dining and entertainment."
              ],
              "bestTimeToVisit": "Weekday early evening (before 6:00 PM) to avoid the longest weekend waits.",
              "crowdLevel": "High (7/10), especially Friday-Saturday evenings.",
              "accessibility": "rating: 8/10 — spacious, flat cafeteria-style dining hall, easy for groups.",
              "idealDuration": "45 minutes to 1.5 hours including line time."
            }
            """,
            AverageCostPerDay = 22m,
            LuxuryRating = DeriveLuxury(22m, mapping["Hard Eight BBQ"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Hard Eight BBQ"].tags),
            AdventurePaceScore = AdventureScore(mapping["Hard Eight BBQ"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Hard Eight BBQ"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Hard Eight BBQ"].tags),
            Latitude = 32.9629,
            Longitude = -96.9903,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Joe T. Garcia's ──
        new Destination
        {
            DestinationId = destJoeTGarciasId,
            DestinationName = "Joe T. Garcia's",
            CleanNormalizedSearchName = "joe t garcias",
            MetaphoneCode = "J T KRSS",
            DoubleMetaphonePrimary = "J T KRSS",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "No restaurant in Fort Worth carries more name recognition than Joe T. Garcia's, which opened as a sandwich and barbecue joint in 1930 and became a go-to Tex-Mex spot for anyone visiting the Fort Worth Stockyards. Its famous garden patio, added in the 1960s, is one of the best celebrity-spotting locations in the city — recent visitors have reportedly included Billy Bob Thornton, Harrison Ford, Matt LeBlanc, and Miranda Lambert.",
              "directions": "addressForRideshare: Near the Fort Worth Stockyards National Historic District — check current address, as it's a well-known local landmark with straightforward navigation once in the Stockyards area.",
              "whatToKnow": "signatureOrder: The enchilada dinner, nachos, and margarita are consistently the most recommended.\n\ngardenPatio: The outdoor garden patio, added in the 1960s, is the restaurant's most beloved feature and the reason for its celebrity-spotting reputation.\n\nnoMenus: Historically the restaurant has served a simplified, largely fixed menu rather than an extensive à la carte one — check current format before visiting.",
              "thingsToBeWaryOf": "LongLines: As one of Fort Worth's most famous restaurants, lines here can rival or exceed those at the city's top barbecue joints — arrive early or expect a wait, especially on weekends.\n\nCashConsiderations: Some legacy Tex-Mex institutions in the area have varying card/cash policies — check current payment options ahead of your visit.",
              "localPerspective": "Fort Worth locals consider it a genuine rite of passage — nearly a century of serving the same neighborhood, with the garden patio functioning as an unofficial local landmark independent of the food itself.",
              "hiddenCost": "enchiladaDinner: $18-$28.\nMargarita: $10-$14.\nNachos: $12-$18.",
              "nearbyComplements": [
                "Fort Worth Stockyards National Historic District: Immediately nearby.",
                "Cooper's Old Time Pit Bar-B-Que: Also in the Stockyards area."
              ],
              "bestTimeToVisit": "Early dinner (5:00-6:00 PM) on a weekday to minimize the wait for the garden patio.",
              "crowdLevel": "High (7/10), especially weekend evenings.",
              "accessibility": "rating: 7/10 — mix of indoor dining and the outdoor garden patio.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 32m,
            LuxuryRating = DeriveLuxury(32m, mapping["Joe T. Garcia's"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Joe T. Garcia's"].tags),
            AdventurePaceScore = AdventureScore(mapping["Joe T. Garcia's"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Joe T. Garcia's"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Joe T. Garcia's"].tags),
            Latitude = 32.7842,
            Longitude = -97.3467,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Mariano's Hacienda Ranch ──
        new Destination
        {
            DestinationId = destMarianosId,
            DestinationName = "Mariano's Hacienda Ranch",
            CleanNormalizedSearchName = "marianos hacienda ranch",
            MetaphoneCode = "MRNS HSNT RNX",
            DoubleMetaphonePrimary = "MRNS HSNT RNX",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A genuine piece of drinking history: Mariano Martinez opened his first Dallas Tex-Mex restaurant in the early 1970s, and after guests complained about inconsistent margarita quality, he adapted a 7-Eleven Slurpee machine in 1971 to invent the world's first frozen margarita machine — a Dallas legend that changed bar culture nationwide. Beyond the margaritas, tender beef fajitas and stuffed poblanos are the standout dishes.",
              "directions": "addressForRideshare: Check current Mariano's Hacienda Ranch location — the brand has operated multiple North Texas locations over its decades in business.",
              "whatToKnow": "theFrozenMargarita: The original frozen margarita machine (Mariano's 1971 invention) is preserved and recognized as a genuine piece of American drinking history — Smithsonian-level trivia for Dallas.\n\nsignatureDishes: Beef fajitas and stuffed poblanos are the most-cited standout orders.\n\ndecadesOfHistory: The Hacienda/La Hacienda Ranch brand has been a North Texas staple since the early 1970s.",
              "thingsToBeWaryOf": "CheckCurrentLocation: As with many longtime restaurant groups, confirm the current operating location before visiting, since North Texas restaurant footprints shift over time.",
              "localPerspective": "Dallas locals treat a visit here as tasting a genuine piece of the city's food-and-drink history — the frozen margarita alone is a point of civic pride, credited with reshaping bar menus across the country.",
              "hiddenCost": "fajitaPlate: $18-$28.\nFrozenMargarita: $9-$14.\nStuffedPoblanos: $16-$22.",
              "nearbyComplements": [
                "Downtown Dallas: Central reference point depending on current location.",
                "Deep Ellum: A common nearby dining/nightlife pairing."
              ],
              "bestTimeToVisit": "Early dinner or happy hour to sample the signature frozen margarita without a long wait.",
              "crowdLevel": "Medium (5/10), higher on weekend evenings.",
              "accessibility": "rating: 8/10 — standard full-service Tex-Mex restaurant seating.",
              "idealDuration": "1 to 1.5 hours."
            }
            """,
            AverageCostPerDay = 26m,
            LuxuryRating = DeriveLuxury(26m, mapping["Mariano's Hacienda Ranch"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Mariano's Hacienda Ranch"].tags),
            AdventurePaceScore = AdventureScore(mapping["Mariano's Hacienda Ranch"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Mariano's Hacienda Ranch"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Mariano's Hacienda Ranch"].tags),
            Latitude = 32.8600,
            Longitude = -96.7900,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── The Original Mexican Eats Cafe ──
        new Destination
        {
            DestinationId = destOriginalMexEatsId,
            DestinationName = "The Original Mexican Eats Cafe",
            CleanNormalizedSearchName = "the original mexican eats cafe",
            MetaphoneCode = "0 ORJNL MKSKN TS KF",
            DoubleMetaphonePrimary = "T ORJNL MKSKN TS KF",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Fort Worth's oldest restaurant, opened in 1926 (some sources cite 1928-1930 for the formal Original Mexican Eats Cafe name) by the Pineda family. Locals simply call it 'the Old Original.' President Franklin D. Roosevelt visited on trips to see his son Elliot in Fort Worth in the 1930s, inspiring 'The Roosevelt Special' — a cheese enchilada con carne, beef taco, and bean chalupa still on the menu today.",
              "directions": "addressForRideshare: 4713 Camp Bowie Blvd, Fort Worth, TX 76107 (original/flagship location).\n\nsecondLocation: A newer location, The Original Del Norte, opened combining this restaurant's recipes with the beloved (now-closed) El Rancho Grande.",
              "whatToKnow": "theRooseveltSpecial: A cheese enchilada con carne, beef taco, and bean chalupa, created in tribute to FDR's visits.\n\nvintageAtmosphere: Original tin ceiling tiles and nearly a century of family memories give the space genuine historic character.\n\nclassicOrders: Raw onions on cheese enchiladas and a silky sour cream sauce on the chicken enchilada plate are frequently cited local favorites.",
              "thingsToBeWaryOf": "GenerationalInstitution: This is a beloved, old-school spot rather than a trendy one — go for the history and comfort-food classics, not a modern dining experience.\n\nMultipleLocations: With the newer Original Del Norte location now open, confirm which location (and menu) you're visiting.",
              "localPerspective": "Fort Worth locals describe it as a place where 'generations have enjoyed The Original, bringing their kids, who are now bringing their kids' — a rare five-generation dining tradition in the city.",
              "hiddenCost": "enchiladaPlate: $12-$18.\nRooseveltSpecial: $14-$20.\nMargarita: $8-$12.",
              "nearbyComplements": [
                "Camp Bowie Boulevard shopping and dining strip: Immediately surrounding.",
                "Fort Worth Cultural District: A short drive."
              ],
              "bestTimeToVisit": "Lunch or early dinner on a weekday for the most relaxed, classic experience.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 7/10 — older building with standard restaurant seating.",
              "idealDuration": "1 hour."
            }
            """,
            AverageCostPerDay = 20m,
            LuxuryRating = DeriveLuxury(20m, mapping["The Original Mexican Eats Cafe"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["The Original Mexican Eats Cafe"].tags),
            AdventurePaceScore = AdventureScore(mapping["The Original Mexican Eats Cafe"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["The Original Mexican Eats Cafe"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["The Original Mexican Eats Cafe"].tags),
            Latitude = 32.7274,
            Longitude = -97.3835,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Pulido's Kitchen & Cantina ──
        new Destination
        {
            DestinationId = destPulidosId,
            DestinationName = "Pulido's Kitchen & Cantina",
            CleanNormalizedSearchName = "pulidos kitchen cantina",
            MetaphoneCode = "PLTS KXN KNTN",
            DoubleMetaphonePrimary = "PLTS KXN KNTN",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Founded by the Pulido family in 1966, Pulido's is a longtime Fort Worth Tex-Mex institution that closed a location in 2023 before being revived in 2024 under new ownership (the Westland Restaurant Group) — good news for a beloved local brand that locals feared might disappear entirely.",
              "directions": "addressForRideshare: Pulido Street location, off University Drive and Interstate 30 in west Fort Worth; a second location operates in Hurst at 1224 Precinct Line Road.",
              "whatToKnow": "revivalStory: The brand's 2024 revival under new ownership means it's worth checking current reviews/menu, as the transition may have brought changes from the original decades-long formula.\n\ntwoLocations: West Fort Worth (Pulido Street) and Hurst — pick whichever fits your route better.\n\nclassicTexMex: Standard Tex-Mex combo plates, enchiladas, and fajitas in the traditional style.",
              "thingsToBeWaryOf": "RecentOwnershipChange: Given the 2023 closure and 2024 revival, expect some variability compared to the restaurant's historic reputation — check recent reviews before visiting.",
              "localPerspective": "Long-time Fort Worth residents remember Pulido's as a decades-long neighborhood staple, and the 2024 revival was genuinely welcomed locally as the return of a beloved brand rather than a new restaurant borrowing an old name.",
              "hiddenCost": "comboPlate: $12-$18.\nFajitas: $16-$24.\nMargarita: $8-$12.",
              "nearbyComplements": [
                "University Drive corridor: Surrounding dining and shopping.",
                "TCU (Texas Christian University) area: Nearby if visiting the west Fort Worth location."
              ],
              "bestTimeToVisit": "Lunch or early dinner on a weekday.",
              "crowdLevel": "Medium (5/10).",
              "accessibility": "rating: 8/10 — standard full-service restaurant seating.",
              "idealDuration": "1 hour."
            }
            """,
            AverageCostPerDay = 18m,
            LuxuryRating = DeriveLuxury(18m, mapping["Pulido's Kitchen & Cantina"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Pulido's Kitchen & Cantina"].tags),
            AdventurePaceScore = AdventureScore(mapping["Pulido's Kitchen & Cantina"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Pulido's Kitchen & Cantina"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Pulido's Kitchen & Cantina"].tags),
            Latitude = 32.7188,
            Longitude = -97.3792,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },

        // ── Vidorra ──
        new Destination
        {
            DestinationId = destVidorraId,
            DestinationName = "Vidorra",
            CleanNormalizedSearchName = "vidorra",
            MetaphoneCode = "FTR",
            DoubleMetaphonePrimary = "FTR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "A modern Mexican restaurant and rooftop bar in Deep Ellum, Vidorra ('the good life') is built around bold, shareable Mexican flavors and a massive tequila program — 50+ tequila options, numerous margarita flavors, and large-format cocktails designed for groups. Its standout feature is a 2,000-square-foot rooftop patio with unobstructed Downtown Dallas skyline views and a retractable roof.",
              "directions": "addressForRideshare: 2642 Main St, Dallas, TX 75226, in Deep Ellum.",
              "whatToKnow": "hours: Monday-Thursday and Sunday 11:00 AM-11:00 PM, Friday-Saturday 11:00 AM-2:00 AM.\n\nsignatureItems: The Flaming Fundido (melted Oaxaca cheese in a 400-degree molcajete bowl), barrel nachos, and a wide tequila flight selection.\n\nrooftopHours: The rooftop specifically opens around 7:00 PM and is the primary draw for skyline views and evening energy.",
              "thingsToBeWaryOf": "PartyAtmosphereAtNight: Reviews describe a lively, high-energy scene after dark — a great fit for celebrating, less ideal if you want a quiet dinner.\n\nLateNightWeekends: Stays open until 2:00 AM Friday and Saturday, making it a genuine late-night option after a match, not just a dinner stop.",
              "localPerspective": "Deep Ellum locals view Vidorra as part of the neighborhood's broader nightlife identity — a rooftop-and-tequila destination that pairs naturally with the area's live music venues for a full night out.",
              "hiddenCost": "entreesAndSharedPlates: $14-$28.\nTequilaFlight: $18-$35.\nLargeFormatMargarita: $30-$45 (serves 2-4).",
              "nearbyComplements": [
                "Terry Black's Barbecue: A short walk in Deep Ellum.",
                "Deep Ellum music venues (Three Links, Trees): A short walk."
              ],
              "bestTimeToVisit": "Evening (7:00 PM onward) specifically for the rooftop patio and skyline views.",
              "crowdLevel": "High (7/10) evenings, Maximum (10/10) weekend nights.",
              "accessibility": "rating: 8/10 — elevator access to the rooftop; ground floor and patio are flat.",
              "idealDuration": "1.5 to 3 hours."
            }
            """,
            AverageCostPerDay = 40m,
            LuxuryRating = DeriveLuxury(40m, mapping["Vidorra"].tags),
            AccessibilityType = "Walk",
            FamilyFriendlyScore = FamilyScore(mapping["Vidorra"].tags),
            AdventurePaceScore = AdventureScore(mapping["Vidorra"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Vidorra"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Vidorra"].tags),
            Latitude = 32.7842,
            Longitude = -96.7839,
            SearchHitCount = 0,
            TimeZone = "Central Standard Time",
            SafetyLevel = 1,
            CountryId = usa.CountryId
        },
    };

            db.Destinations.AddRange(newDestinations);
            await db.SaveChangesAsync();

            var allDestinations = newDestinations.ToList();

            await AssignCategoriesAndTagsToDestinationsAsync(db, allDestinations, mapping);
            await SeedImagesForDFWAsync(db, allDestinations);
            await SeedDestinationLanguageAndCurrencyAsync(db, allDestinations, english, usd);

            // ─── Destination ↔ City links ─────────────────────────────────────────
            db.DestinationCities.AddRange(new[]
            {
        new DestinationCity { DestinationId = destHenryId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destATTStadiumId, CityId = arlington.CityId },
        new DestinationCity { DestinationId = destFanFestivalId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destTexasLiveId, CityId = arlington.CityId },
        new DestinationCity { DestinationId = destHurtadoId, CityId = arlington.CityId },
        new DestinationCity { DestinationId = destCoopersId, CityId = fortWorth.CityId },
        new DestinationCity { DestinationId = destEyeballId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destArtsDistrictId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destNasherId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destCrowMuseumId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destFarmersMarketId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destReunionTowerId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destSixthFloorMuseumId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destDealeyPlazaId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destHistoricDistrictId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destTerryBlacksId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destDaynesId, CityId = aledo.CityId },
        new DestinationCity { DestinationId = destPantherCityId, CityId = fortWorth.CityId },
        new DestinationCity { DestinationId = destHardEightId, CityId = coppell.CityId },
        new DestinationCity { DestinationId = destJoeTGarciasId, CityId = fortWorth.CityId },
        new DestinationCity { DestinationId = destMarianosId, CityId = dallas.CityId },
        new DestinationCity { DestinationId = destOriginalMexEatsId, CityId = fortWorth.CityId },
        new DestinationCity { DestinationId = destPulidosId, CityId = fortWorth.CityId },
        new DestinationCity { DestinationId = destVidorraId, CityId = dallas.CityId },
    });
            await db.SaveChangesAsync();

            // ─── Transit Route: Downtown Dallas → Arlington/AT&T Stadium ────────
            var routeDallasToArlingtonId = Guid.NewGuid();
            db.TransitRoutes.Add(new TransitRoute
            {
                TransitRouteId = routeDallasToArlingtonId,
                OriginCityId = dallas.CityId,
                DestinationCityId = arlington.CityId,
                TransitType = "TRE + Rideshare (no direct rail to Arlington)",
                EstimatedCostPerPerson = 20m,
                DurationInMinutes = 90,
                RecommendedTimeBufferMinutes = 45,
                BookingReferenceUrl = "https://www.dallasfwc26.com/our-venues/match-schedule/",
                CarbonFootprintKg = "4.5",
                SubSegmentsJson = "[]"
            });
            await db.SaveChangesAsync();

            // ═══════════════════════════════════════════════════════════════════
            // WISHLIST 1: The Ultimate AT&T Stadium Match Day (single day)
            // ═══════════════════════════════════════════════════════════════════
            var wishlist1Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = wishlist1Id,
                WishlistName = "The Ultimate AT&T Stadium Match Day",
                WishlistDescription = "Spend the day exploring Arlington before heading to the World Cup match — minimal driving, maximum atmosphere. This itinerary keeps you within the Arlington Entertainment District as much as possible, since Arlington has no public transit and sits between Dallas and Fort Worth with real drive-time costs either direction.",
                ShortStory = "Breakfast, a beat-the-traffic arrival, fan atmosphere, real Texas barbecue, and the loudest stadium bowl in the tournament — all without leaving Arlington once you get there.",
                TotalDays = 1,
                PeopleType = "World Cup Fans / Sports Travelers attending a match at AT&T Stadium",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_att_hero.jpeg",
                GlobalInclusionsJson = @"[""Match Ticket (AT&T Stadium / Dallas Stadium)"",""Parking Pass (pre-purchased)""]",
                RawContentKeywords = "Arlington, AT&T Stadium, Dallas Stadium, World Cup, Texas Live!, Hurtado Barbecue, The Henry, match day, soccer, FIFA",
                PsychologicalVibeTagsJson = @"[""Sports Fan"",""Foodie"",""Low-Traffic"",""Social""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 360m,
                CalculatedTotalCost = 720m,
                DepositAmountRequired = 50m,
                AccommodationInclusions = "Day-trip itinerary — no overnight accommodation included",
                TransitInclusions = "Designed to minimize driving: most stops are within the Arlington Entertainment District once you arrive",
                ActivityInclusions = "AT&T Stadium match ticket (World Cup fixture)",
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
                DayTitle = "The Ultimate AT&T Stadium Match Day",
                MorningCityId = dallas.CityId,
                AfternoonCityId = arlington.CityId,
                EveningCityId = arlington.CityId,
                TransitFromPreviousDayRouteId = null,
                WishlistId = wishlist1Id
            };
            db.ItineraryDays.Add(w1Day);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Breakfast at The Henry", ItemDescription = "Or, if you're already staying in Arlington, grab breakfast nearby to avoid the morning drive entirely — Arlington has no direct rail, so every mile matters.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_thehenry.jpeg", SocialProofBadge = "Dallas Favorite", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Head to Arlington Early", ItemDescription = "Beat the traffic by arriving before the crowds — allow up to 90 minutes for the drive/rideshare, since Arlington has no direct rail connection.", ItemOrderIndex = 2, TimeOfDay = "Morning", ImageUrl = "", SocialProofBadge = "", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "FIFA Fan Festival (if operating that day)", ItemDescription = "Note: the official Fan Festival is at Fair Park in Dallas, about 20 miles away — not realistic to combine with an Arlington match day. For local fan-zone energy, Texas Live!'s Live! Arena runs its own watch parties instead.", ItemOrderIndex = 3, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_fanfestival.jpeg", SocialProofBadge = "Check Distance First", IndividualCostModifier = 0m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Texas Live!", ItemDescription = "The Entertainment District's central gathering spot — 22 bars, live music, and the 100-foot LED screen at Live! Arena, right between the two stadiums.", ItemOrderIndex = 4, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_texaslive.jpeg", SocialProofBadge = "Entertainment Hub", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Early Lunch: Hurtado Barbecue", ItemDescription = "Half a mile from the stadium in Downtown Arlington — Tex-Mex barbecue and the official BBQ of the Texas Rangers.", ItemOrderIndex = 5, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_hurtado.jpeg", SocialProofBadge = "Closest to Stadium", IndividualCostModifier = 22m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Or: Cooper's Old Time Pit Bar-B-Que (if coming from Fort Worth)", ItemDescription = "Fort Worth Stockyards institution — a better fit if your morning routing already has you on the Fort Worth side of DFW.", ItemOrderIndex = 6, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_coopers.jpeg", SocialProofBadge = "Fort Worth Route", IndividualCostModifier = 20m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Before Kickoff: Walk Around AT&T Stadium", ItemDescription = "Team shops, fan activations, and stadium photos — arrive 2 to 2.5 hours before kickoff for the full pre-match experience.", ItemOrderIndex = 7, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_attstadiumplaza.jpeg", SocialProofBadge = "Must-Do", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Watch the Match", ItemDescription = "Experience the World Cup at AT&T Stadium — officially 'Dallas Stadium' in FIFA materials, hosting more matches (nine) than any other tournament venue.", ItemOrderIndex = 8, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_attstadium.jpeg", SocialProofBadge = "World Cup", IndividualCostModifier = 280m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w1Day.ItineraryDayId, ItemTitle = "Return to Texas Live! to Celebrate", ItemDescription = "Head back to the Entertainment District's central hub to celebrate with other fans — no additional travel required.", ItemOrderIndex = 9, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_texaslive.jpeg", SocialProofBadge = "No Traffic", IndividualCostModifier = 30m, IsOptionalActivity = false, IsSelectedByDefault = true }
            );
            await db.SaveChangesAsync();

            // ═══════════════════════════════════════════════════════════════════
            // WISHLIST 2: Downtown Dallas Before Kickoff (single day)
            // ═══════════════════════════════════════════════════════════════════
            var wishlist2Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = wishlist2Id,
                WishlistName = "Downtown Dallas Before Kickoff",
                WishlistDescription = "Almost everything here is within a compact downtown area, making this ideal before the drive to Arlington — a culture-and-history-forward day through the Dallas Arts District, Dealey Plaza, and Downtown Dallas Historic District, ending with the trip out to AT&T Stadium.",
                ShortStory = "A giant eyeball, world-class sculpture and Asian art, a farmers market, the tallest view in Dallas, and a sobering walk through American history — all before the roar of the World Cup.",
                TotalDays = 1,
                PeopleType = "World Cup Fans who want a culture-and-food day before the match",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_hero.jpg",
                GlobalInclusionsJson = @"[""Match Ticket (AT&T Stadium / Dallas Stadium)"",""Nasher Sculpture Center Admission"",""Sixth Floor Museum Admission""]",
                RawContentKeywords = "Downtown Dallas, Dallas Arts District, Nasher, Crow Museum, Sixth Floor Museum, Dealey Plaza, Reunion Tower, AT&T Stadium, World Cup, match day",
                PsychologicalVibeTagsJson = @"[""Culture"",""Foodie"",""Walkable"",""Sports Fan""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 340m,
                CalculatedTotalCost = 680m,
                DepositAmountRequired = 50m,
                AccommodationInclusions = "Day-trip itinerary — no overnight accommodation included",
                TransitInclusions = "Almost entirely walkable within Downtown Dallas; TRE plus rideshare (or a roughly 90-minute drive) to Arlington for kickoff",
                ActivityInclusions = "Nasher Sculpture Center admission, Sixth Floor Museum admission, AT&T Stadium match ticket (World Cup fixture)",
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
                DayTitle = "Downtown Dallas Before Kickoff",
                MorningCityId = dallas.CityId,
                AfternoonCityId = dallas.CityId,
                EveningCityId = arlington.CityId,
                TransitFromPreviousDayRouteId = null,
                WishlistId = wishlist2Id
            };
            db.ItineraryDays.Add(w2Day);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Breakfast at The Henry", ItemDescription = "Modern American breakfast in Oak Lawn — smoked salmon bagels and cinnamon sugar French toast.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_thehenry.jpeg", SocialProofBadge = "Dallas Favorite", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Explore: The Giant Eyeball", ItemDescription = "A quirky, 30-foot photo stop on a Downtown Dallas sidewalk.", ItemOrderIndex = 2, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_gianteyeball.jpg", SocialProofBadge = "Quirky Photo Op", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Dallas Arts District", ItemDescription = "The largest contiguous urban arts district in the nation — the anchor for the rest of the morning's stops.", ItemOrderIndex = 3, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_artsdistrict.jpeg", SocialProofBadge = "Cultural Hub", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Nasher Sculpture Center (worth adding)", ItemDescription = "Over 300 modern sculptures by Rodin, Calder, Matisse, Picasso, and more, in a Renzo Piano-designed building and garden.", ItemOrderIndex = 4, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_nasher.jpeg", SocialProofBadge = "World-Class", IndividualCostModifier = 10m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Crow Museum of Asian Art (worth adding)", ItemDescription = "North Texas's finest Asian art collection — always free, and consistently less crowded than its neighbors.", ItemOrderIndex = 5, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_crowmuseum.jpeg", SocialProofBadge = "Free Admission", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Food Break: Dallas Farmers Market", ItemDescription = "Operating since 1941 — The Shed's food hall daily, plus the full outdoor market on weekends.", ItemOrderIndex = 6, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_farmersmarket.jpeg", SocialProofBadge = "Dallas Institution", IndividualCostModifier = 15m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Skyline Views: Reunion Tower GeO-Deck", ItemDescription = "Dallas's defining skyline landmark since 1978 — indoor/outdoor observation roughly 460 feet up.", ItemOrderIndex = 7, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_reuniontower.jpg", SocialProofBadge = "Iconic View", IndividualCostModifier = 25m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "History: Sixth Floor Museum at Dealey Plaza", ItemDescription = "An in-depth account of JFK's assassination, viewed from the actual sixth-floor vantage point.", ItemOrderIndex = 8, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_sixthfloormuseum.jpeg", SocialProofBadge = "Essential History", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Dealey Plaza (worth adding)", ItemDescription = "Free and open 24 hours, directly adjacent to the museum — a solemn, historically significant walk.", ItemOrderIndex = 9, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_dealeyplaza.jpg", SocialProofBadge = "National Landmark", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Final Walk: Downtown Dallas Historic District", ItemDescription = "The Old Red Courthouse (1892) and the John Neely Bryan cabin replica — a compact final stroll before heading to Arlington.", ItemOrderIndex = 10, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_historicdistrict.jpg", SocialProofBadge = "Compact History", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Head to AT&T Stadium", ItemDescription = "Allow up to 90 minutes for the drive/rideshare or TRE-plus-rideshare combination — Arlington has no direct rail connection, so build in a real buffer.", ItemOrderIndex = 11, TimeOfDay = "Evening", ImageUrl = "", SocialProofBadge = "", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w2Day.ItineraryDayId, ItemTitle = "Watch the Match", ItemDescription = "Experience the World Cup at AT&T Stadium — officially 'Dallas Stadium' in FIFA materials.", ItemOrderIndex = 12, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_attstadium.jpeg", SocialProofBadge = "World Cup", IndividualCostModifier = 280m, IsOptionalActivity = false, IsSelectedByDefault = true }
            );
            await db.SaveChangesAsync();

            // ═══════════════════════════════════════════════════════════════════
            // WISHLIST 3: The Ultimate Texas BBQ Tour
            // NOT a single day — a "choose your stop" guide across DFW, modeled
            // as 5 ItineraryDays representing geographic clusters, not calendar days.
            // ═══════════════════════════════════════════════════════════════════
            var wishlist3Id = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistName = "The Ultimate Texas BBQ Tour",
                WishlistId = wishlist3Id,
                WishlistDescription = "A 'Choose Your BBQ Stop Before the Match' guide, not a single-day itinerary — this is designed to be sampled across your whole DFW trip, organized by cluster: Arlington (closest to the stadium), Dallas, Fort Worth, greater DFW, and a bonus round of Texas Tex-Mex institutions once the barbecue is done. Pick the stop (or stops) that fit your route on any given day.",
                ShortStory = "From the stadium's front door to a century-old Fort Worth cantina, this is DFW's barbecue map — brisket, smoke, and a few legendary margaritas along the way.",
                TotalDays = 5,
                PeopleType = "World Cup Fans / Barbecue Travelers spending multiple days in DFW",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_bbqtour_hero.jpg",
                GlobalInclusionsJson = "[]",
                RawContentKeywords = "Texas barbecue, DFW BBQ, Hurtado Barbecue, Terry Black's, Cooper's, Dayne's, Panther City, Hard Eight, Joe T Garcia's, Mariano's, Original Mexican Eats, Pulido's, Vidorra, Arlington, Dallas, Fort Worth",
                PsychologicalVibeTagsJson = @"[""Foodie"",""Flexible"",""Local Favorite"",""Road Trip""]",
                DefaultTravelersCount = 2,
                BasePricePerPerson = 0m,
                CalculatedTotalCost = 0m,
                DepositAmountRequired = 0m,
                AccommodationInclusions = "Not applicable — this is a flexible dining guide, not a booked itinerary",
                TransitInclusions = "Not applicable — requires a car or rideshare between clusters; some stops (Dayne's in Aledo, Hard Eight in Coppell) are meaningful drives from DFW's core cities",
                ActivityInclusions = "None — this guide covers dining stops only",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "Barbecue Enthusiast / Multi-Day DFW Visitor",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();

            var w3ArlingtonDay = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Arlington (Closest to the Stadium)", MorningCityId = arlington.CityId, AfternoonCityId = arlington.CityId, EveningCityId = arlington.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlist3Id };
            var w3DallasDay = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 2, DayTitle = "Dallas", MorningCityId = dallas.CityId, AfternoonCityId = dallas.CityId, EveningCityId = dallas.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlist3Id };
            var w3FortWorthDay = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 3, DayTitle = "Fort Worth", MorningCityId = fortWorth.CityId, AfternoonCityId = fortWorth.CityId, EveningCityId = fortWorth.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlist3Id };
            var w3AroundDFWDay = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 4, DayTitle = "Around DFW", MorningCityId = coppell.CityId, AfternoonCityId = coppell.CityId, EveningCityId = coppell.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlist3Id };
            var w3BonusDay = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 5, DayTitle = "Bonus Texas Flavors", MorningCityId = fortWorth.CityId, AfternoonCityId = fortWorth.CityId, EveningCityId = dallas.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlist3Id };
            db.ItineraryDays.AddRange(w3ArlingtonDay, w3DallasDay, w3FortWorthDay, w3AroundDFWDay, w3BonusDay);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3ArlingtonDay.ItineraryDayId, ItemTitle = "Hurtado Barbecue", ItemDescription = "Half a mile from AT&T Stadium — the closest legendary barbecue stop to kickoff, with a Tex-Mex barbecue twist.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_hurtado.jpeg", SocialProofBadge = "Closest to Stadium", IndividualCostModifier = 22m, IsOptionalActivity = false, IsSelectedByDefault = true },

                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3DallasDay.ItineraryDayId, ItemTitle = "Terry Black's Barbecue", ItemDescription = "Deep Ellum's Lockhart-style Central Texas barbecue anchor — beef ribs and creamed corn are the signature order.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_terryblacks.jpeg", SocialProofBadge = "Deep Ellum Icon", IndividualCostModifier = 24m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3DallasDay.ItineraryDayId, ItemTitle = "Cooper's Old Time Pit Bar-B-Que", ItemDescription = "Note: this is genuinely a Fort Worth Stockyards stop — listed here as an alternate if your Dallas day routes through Fort Worth.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_coopers.jpeg", SocialProofBadge = "Stockyards Classic", IndividualCostModifier = 20m, IsOptionalActivity = true, IsSelectedByDefault = false },

                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3FortWorthDay.ItineraryDayId, ItemTitle = "Dayne's Craft Barbecue", ItemDescription = "Technically in Aledo, 20-25 minutes west of Fort Worth — Texas Monthly Top 10 statewide barbecue. Friday-Sunday only.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_daynes.jpeg", SocialProofBadge = "Texas Monthly Top 10", IndividualCostModifier = 30m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3FortWorthDay.ItineraryDayId, ItemTitle = "Panther City BBQ", ItemDescription = "Michelin Guide-recognized Southside Fort Worth spot blending Central Texas barbecue with Tex-Mex flourishes.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_panthercity.jpeg", SocialProofBadge = "Michelin Recognized", IndividualCostModifier = 26m, IsOptionalActivity = true, IsSelectedByDefault = false },

                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3AroundDFWDay.ItineraryDayId, ItemTitle = "Hard Eight BBQ", ItemDescription = "The Coppell location, roughly midway between Dallas and Fort Worth — walk-the-pits ordering and a genuine Texas barbecue spectacle for groups.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_hardeight.jpeg", SocialProofBadge = "Group Favorite", IndividualCostModifier = 22m, IsOptionalActivity = true, IsSelectedByDefault = false },

                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3BonusDay.ItineraryDayId, ItemTitle = "Joe T. Garcia's", ItemDescription = "Fort Worth's most famous restaurant since 1930 — enchilada dinner, nachos, and a margarita on the legendary garden patio.", ItemOrderIndex = 1, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_joetgarcias.jpeg", SocialProofBadge = "Fort Worth Legend", IndividualCostModifier = 32m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3BonusDay.ItineraryDayId, ItemTitle = "Mariano's Hacienda Ranch", ItemDescription = "Birthplace of the world's first frozen margarita machine, invented by Mariano Martinez in 1971.", ItemOrderIndex = 2, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_marianos.jpeg", SocialProofBadge = "Margarita History", IndividualCostModifier = 26m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3BonusDay.ItineraryDayId, ItemTitle = "The Original Mexican Eats Cafe", ItemDescription = "Fort Worth's oldest restaurant (1926) — try the Roosevelt Special, named for FDR's visits in the 1930s.", ItemOrderIndex = 3, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_originalmexeats.jpeg", SocialProofBadge = "Est. 1926", IndividualCostModifier = 20m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3BonusDay.ItineraryDayId, ItemTitle = "Pulido's Kitchen & Cantina", ItemDescription = "Founded 1966, revived in 2024 under new ownership — classic Fort Worth Tex-Mex in west Fort Worth or Hurst.", ItemOrderIndex = 4, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_pulidos.jpeg", SocialProofBadge = "Recently Revived", IndividualCostModifier = 18m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = w3BonusDay.ItineraryDayId, ItemTitle = "Vidorra", ItemDescription = "Deep Ellum's modern Mexican rooftop — 50+ tequilas and Downtown Dallas skyline views, open late Friday-Saturday.", ItemOrderIndex = 5, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_vidorra.jpeg", SocialProofBadge = "Rooftop Views", IndividualCostModifier = 40m, IsOptionalActivity = true, IsSelectedByDefault = false }
            );
            await db.SaveChangesAsync();

            // ─── Wishlist ↔ Destination links (all three wishlists) ────────────
            var wishlist1Destinations = new[] { destHenryId, destATTStadiumId, destFanFestivalId, destTexasLiveId, destHurtadoId, destCoopersId };
            var wishlist2Destinations = new[] { destHenryId, destEyeballId, destArtsDistrictId, destNasherId, destCrowMuseumId, destFarmersMarketId, destReunionTowerId, destSixthFloorMuseumId, destDealeyPlazaId, destHistoricDistrictId, destATTStadiumId };
            var wishlist3Destinations = new[] { destHurtadoId, destTerryBlacksId, destCoopersId, destDaynesId, destPantherCityId, destHardEightId, destJoeTGarciasId, destMarianosId, destOriginalMexEatsId, destPulidosId, destVidorraId };

            async Task LinkDestinationsAsync(Guid wishlistId, IEnumerable<Guid> destIds)
            {
                foreach (var destId in destIds)
                {
                    bool alreadyLinked = await db.WishlistDestinations
                        .AnyAsync(wd => wd.WishlistId == wishlistId && wd.DestinationId == destId);
                    if (!alreadyLinked)
                        db.WishlistDestinations.Add(new WishlistDestination { WishlistId = wishlistId, DestinationId = destId });
                }
            }

            await LinkDestinationsAsync(wishlist1Id, wishlist1Destinations);
            await LinkDestinationsAsync(wishlist2Id, wishlist2Destinations);
            await LinkDestinationsAsync(wishlist3Id, wishlist3Destinations);
            await db.SaveChangesAsync();
        }

        // ─── Local helper: images (DFW-specific file names) ─────────────────────
        private static async Task SeedImagesForDFWAsync(HodracDbContext db, List<Destination> destinations)
        {
            List<string> imageNames = new List<string>
    {
        "thehenry.jpeg", "attstadium.jpeg", "fanfestival.jpeg", "texaslive.jpeg", "hurtado.jpeg", "coopers.jpeg",
        "gianteyeball.jpg", "artsdistrict.jpeg", "nasher.jpeg", "crowmuseum.jpeg", "farmersmarket.jpeg",
        "reuniontower.jpg", "sixthfloormuseum.jpeg", "dealeyplaza.jpg", "historicdistrict.jpg",
        "terryblacks.jpeg", "daynes.jepg", "panthercity.jpeg", "hardeight.jpeg", "joetgarcias.jpeg",
        "marianos.jpeg", "originalmexeats.jpeg", "pulidos.jpeg", "vidorra.jpeg"
    };

            foreach (var (dest, image) in destinations.Zip(imageNames, (d, i) => (d, i)))
            {
                db.DestinationImages.Add(new DestinationImage
                {
                    DestinationImageId = Guid.NewGuid(),
                    DestinationId = dest.DestinationId,
                    ImageUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_{Uri.EscapeDataString(image)}",
                    ThumbnailUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dallas_{Uri.EscapeDataString(image)}",
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
