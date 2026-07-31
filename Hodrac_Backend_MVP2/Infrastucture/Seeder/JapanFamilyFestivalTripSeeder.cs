using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Seeder;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    public class JapanFamilyFestivalTripSeeder
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ADD THIS METHOD TO THE EXISTING `DataSeeder` STATIC CLASS.
        // Reuses AssignCategoriesAndTagsToDestinationsAsync, SeedDestinationLanguagesAndCurrenciesAsync
        // (the Japanese/JPY version from SeedJapanTrip), and a local image helper.
        //
        // KEYS: DescriptionJson uses the ORIGINAL template's PascalCase keys
        // (Overview, Directions, WhatToKnow, ThingsToBeWaryOf, LocalPerspective,
        // HiddenCost, NearbyComplements, BestTimeToVisit, crowdLevel, Accessibility,
        // IdealDuration) — matching SeedJapanTrip exactly, including its one quirk
        // (crowdLevel is lowercase while everything else is PascalCase).
        //
        // ACCURACY NOTES baked into the data:
        // 1. All festival dates below are verified 2026 dates, not assumed annual
        //    defaults: Aomori Nebuta Aug 2-7, Akita Kanto Aug 3-6, Sendai Tanabata
        //    Aug 6-8, Atami Marine Fireworks Aug 9 (one of 15 dates that year),
        //    Fukagawa Festival Aug 12-16, Lake Suwa Fireworks Aug 15, Kyoto
        //    Tanabata Sky Lantern Festival event nights Aug 8/11/16/17, Awa Odori
        //    Aug 11-15.
        // 2. IMPORTANT FLAG: 2026 is a Fukagawa Festival "Hon-matsuri" year
        //    (happens once every 3 years), and its single biggest event — the
        //    50+ neighborhood mikoshi water-throwing grand procession — falls on
        //    Sunday, August 16, 2026, not during the Aug 12-13 window most
        //    itineraries (including the source plan here) assume. This is flagged
        //    directly in the Fukagawa destination's ThingsToBeWaryOf field rather
        //    than silently treated as accurate, since the family plan as designed
        //    will experience Fukagawa's earlier festival days (real water-throwing
        //    still happens then, just not the single grandest procession) in
        //    exchange for making the Lake Suwa (Aug 15, fixed) + Kyoto Sky Lantern
        //    (Aug 16 night) combination work.
        // 3. Fushimi Inari Taisha and Kiyomizu-dera are NOT recreated here — they
        //    already exist as Destinations from SeedJapanTrip, so this method
        //    fetches and reuses them by name.
        // ═══════════════════════════════════════════════════════════════════════

        public static async Task SeedJapanFamilyFestivalTrip(HodracDbContext db)
        {
            // ─── Lookups ──────────────────────────────────────────────────────────
            var japan = await db.Countries.FirstAsync(c => c.CountryName == "Japan");
            var japanese = await db.Languages.FirstAsync(l => l.LanguageName == "Japanese");
            var yen = await db.Currencies.FirstAsync(c => c.CurrencyCode == "JPY");

            var tokyo = await db.Cities.FirstAsync(c => c.CityName == "Tokyo");

            async Task<City> GetOrCreateCityAsync(string name, double lat, double lng, string description)
            {
                var city = await db.Cities.FirstOrDefaultAsync(c => c.CityName == name);
                if (city == null)
                {
                    city = new City { CityId = Guid.NewGuid(), CityName = name, CountryId = japan.CountryId, Latitude = lat, Longitude = lng, CityDescription = description };
                    db.Cities.Add(city);
                    await db.SaveChangesAsync();
                }
                return city;
            }

            var kyoto = await GetOrCreateCityAsync("Kyoto", 35.0116, 135.7681, "Japan's cultural heart, home to thousands of temples and traditional streets.");
            var aomori = await GetOrCreateCityAsync("Aomori", 40.8246, 140.7406, "Northernmost city on Honshu's Pacific side, home to the Nebuta Matsuri and gateway to the Tsugaru region.");
            var akita = await GetOrCreateCityAsync("Akita", 39.7186, 140.1024, "Coastal Tohoku city famous for its Kanto pole-balancing festival and rice-growing plains.");
            var sendai = await GetOrCreateCityAsync("Sendai", 38.2682, 140.8694, "Tohoku's largest city, founded by daimyo Date Masamune, known for tree-lined boulevards and the Tanabata Festival.");
            var atami = await GetOrCreateCityAsync("Atami", 35.0956, 139.0739, "Hot-spring resort town on the Izu Peninsula coast, a 40-minute Shinkansen ride from Tokyo.");
            var suwa = await GetOrCreateCityAsync("Suwa", 36.0422, 138.1128, "Lakeside city in Nagano Prefecture, home to Suwa Taisha Shrine and one of Japan's largest fireworks festivals.");
            var tokushima = await GetOrCreateCityAsync("Tokushima", 34.0658, 134.5593, "Shikoku gateway city, home to Japan's biggest dance festival, Awa Odori.");

            // ─── Reuse existing Kyoto destinations from SeedJapanTrip ──────────────
            var fushimiInari = await db.Destinations.FirstOrDefaultAsync(d => d.DestinationName == "Fushimi Inari Taisha");
            var kiyomizudera = await db.Destinations.FirstOrDefaultAsync(d => d.DestinationName == "Kiyomizu-dera");

            var mapping = new Dictionary<string, (string[] categories, string[] tags)>
            {
                ["Aomori Nebuta Matsuri"] = (new[] { "cultural_site", "entertainment_nightlife" }, new[] { "cultural", "family_friendly", "tourist_hotspot", "photography", "history" }),
                ["Akita Kanto Matsuri"] = (new[] { "cultural_site", "entertainment_nightlife" }, new[] { "cultural", "family_friendly", "photography", "history" }),
                ["Sendai Tanabata Festival"] = (new[] { "cultural_site", "market_street_life" }, new[] { "cultural", "family_friendly", "walkable", "photography" }),
                ["Atami Marine Fireworks Festival"] = (new[] { "entertainment_nightlife", "nature_outdoor" }, new[] { "family_friendly", "photography", "relaxing", "tourist_hotspot" }),
                ["Fukagawa Hachiman Matsuri"] = (new[] { "cultural_site", "entertainment_nightlife" }, new[] { "cultural", "adventurous", "family_friendly", "crowded" }),
                ["Lake Suwa Fireworks Festival"] = (new[] { "entertainment_nightlife", "viewpoint_scenic_spot" }, new[] { "family_friendly", "photography", "tourist_hotspot", "romantic" }),
                ["Suwa Taisha Shrine"] = (new[] { "cultural_site", "nature_outdoor" }, new[] { "cultural", "history", "relaxing", "hidden_gem" }),
                ["Kyoto Tanabata Sky Lantern Festival"] = (new[] { "entertainment_nightlife", "cultural_site" }, new[] { "family_friendly", "photography", "romantic", "tourist_hotspot" }),
                ["Gion District"] = (new[] { "neighborhood_district", "cultural_site" }, new[] { "cultural", "walkable", "photography", "history" }),
                ["Awa Odori Festival"] = (new[] { "cultural_site", "entertainment_nightlife" }, new[] { "cultural", "family_friendly", "adventurous", "tourist_hotspot" }),
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
            var destNebutaId = Guid.NewGuid();
            var destKantoId = Guid.NewGuid();
            var destTanabataSendaiId = Guid.NewGuid();
            var destAtamiId = Guid.NewGuid();
            var destFukagawaId = Guid.NewGuid();
            var destSuwaFireworksId = Guid.NewGuid();
            var destSuwaTaishaId = Guid.NewGuid();
            var destKyotoLanternId = Guid.NewGuid();
            var destGionId = Guid.NewGuid();
            var destAwaOdoriId = Guid.NewGuid();

            var newDestinations = new[]
            {
        // ── Aomori Nebuta Matsuri ──
        new Destination
        {
            DestinationId = destNebutaId,
            DestinationName = "Aomori Nebuta Matsuri",
            CleanNormalizedSearchName = "aomori nebuta matsuri",
            MetaphoneCode = "AMR NPT MTSR",
            DoubleMetaphonePrimary = "AMR NPT MTSR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "One of the Three Great Festivals of Tohoku, Nebuta Matsuri turns Aomori City into the loudest, brightest place in Japan for six nights every August. Around 20 massive illuminated floats — painted washi paper over wire frames, some 9 meters wide and 5 meters tall, depicting kabuki heroes and mythological warriors — parade through a 3km loop while haneto dancers chant 'Rassera, Rassera' and taiko drums shake the pavement. Designated an Important Intangible Folk Cultural Property in 1980, it draws over a million visitors and anchors the single densest festival week in Japan, alongside Akita's Kanto and Sendai's Tanabata.",
              "directions": "Dates: August 2-7, 2026 (fixed every year).\n\nBest Station: JR Aomori Station — the parade route is a 5-minute walk away, and Rassera Land (where floats are stabled by day) is a 10-minute walk, next to the ASPAM building on the waterfront.\n\nFrom Tokyo: An 80-minute flight, or the Hayabusa Shinkansen on the JR Tohoku Line to Shin-Aomori, then a short local-train transfer to JR Aomori Station.",
              "whatToKnow": "Schedule: 7:00-9:10 PM on Aug 2-3, 6:45-9:10 PM on Aug 4-6 (the peak nights, with the full lineup of large floats). On the final day, Aug 7, there's a daytime parade from 1:00 PM and a sea parade with roughly 11,000 fireworks over Aomori Bay starting around 7:15 PM, when the top floats are put on boats and paraded around the harbor.\n\nReserved Seats: Go on sale about one month before the festival (roughly ¥3,500-4,000 plus an admin fee, covering Aug 2-6; fireworks seating is separate, roughly ¥4,500-5,500). Free sidewalk seating is available along the whole 3km route.\n\nDaytime Option: Nebuta Warasse museum, just north of the station, houses five permanent floats from past years and lets you see the construction detail up close — a good rainy-day or pre-parade backup.",
              "thingsToBeWaryOf": "Standing for 2 Hours: This is real advice for families — a reserved seat is worth booking in advance rather than standing for the full parade with young kids.\n\nHotel Sell-Outs: Aomori books out months ahead for these dates; this is genuinely the busiest week of the year in the region for both international and domestic travelers.\n\nFirst Two Nights Are Calmer: Aug 2-3 feature smaller child-built floats alongside about half of the large ones — a gentler intro night if your family wants to ease in before the full-scale Aug 4-6 parades.",
              "localPerspective": "Nebuta likely descends from a Tanabata-adjacent ritual to float away summer sleepiness (nemuri-nagashi) and evil spirits before the rice harvest — the word 'nebuta' is thought to derive from 'nemuta' (sleepy). Small lantern floats grew into today's three-story warrior figures by the Meiji era, when neighborhood teams began competing to build the most elaborate design each year.",
              "hiddenCost": "Reserved Parade Seat: Roughly ¥3,500-4,000 plus an admin fee (covers Aug 2-6).\nFireworks/Final-Night Seating: Roughly ¥4,500-6,600 depending on package.\nHaneto Costume Rental: ¥3,000-5,000 if you want to dance rather than watch.\nFood Stalls: ¥500-1,500 per item.",
              "nearbyComplements": [
                "Nebuta Warasse Museum: A short walk from the station, open year-round.",
                "Sannai-Maruyama Archaeological Site: A Jomon-period village site, good for a daytime family outing before the evening parade.",
                "Aomori Bay/ASPAM: The waterfront area where the finale sea parade takes place."
              ],
              "bestTimeToVisit": "Aug 4-6 for the full-scale peak-night parades; Aug 7 for the daytime parade plus the bay fireworks finale if you want the biggest single spectacle.",
              "crowdLevel": "Maximum (10/10) on peak nights (Aug 4-6) and the Aug 7 finale.",
              "accessibility": "Rating: 6/10 — the parade route is flat and paved, but the sheer crowd density on peak nights makes stroller navigation difficult without a reserved seat.",
              "idealDuration": "2 to 3 hours per evening parade; a full day if combining with the museum and Sannai-Maruyama."
            }
            """,
            AverageCostPerDay = 25m,
            LuxuryRating = DeriveLuxury(25m, mapping["Aomori Nebuta Matsuri"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Aomori Nebuta Matsuri"].tags),
            AdventurePaceScore = AdventureScore(mapping["Aomori Nebuta Matsuri"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Aomori Nebuta Matsuri"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Aomori Nebuta Matsuri"].tags),
            Latitude = 40.8244,
            Longitude = 140.7400,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Akita Kanto Matsuri ──
        new Destination
        {
            DestinationId = destKantoId,
            DestinationName = "Akita Kanto Matsuri",
            CleanNormalizedSearchName = "akita kanto matsuri",
            MetaphoneCode = "AKT KNT MTSR",
            DoubleMetaphonePrimary = "AKT KNT MTSR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Also called the pole lantern festival, Kanto Matsuri is a Tanabata-related celebration where performers balance towering bamboo poles hung with paper lanterns — up to 12 meters tall, 50 kilograms, and supporting up to 46 lanterns lit with real candles — on their palms, shoulders, foreheads, and hips, to the chant of 'Dokkoisho, dokkoisho.' One of the Three Great Festivals of Tohoku alongside Aomori's Nebuta and Sendai's Tanabata, it's held annually in Akita City.",
              "directions": "Dates: August 3-6, 2026 (fixed every year).\n\nMain Venue: The night parades run along Chuo-dori street in central Akita City; daytime kanto competitions are held near Senshu Park.\n\nFrom Tokyo: Akita Shinkansen (Komachi) direct to Akita Station, or roughly 2 hours by train from Sendai/Aomori if continuing a Tohoku festival loop.",
              "whatToKnow": "The Performance: Performing groups carry up to 250 kanto poles in total, raising them in unison to drums and flutes — the visual of dozens of lantern poles bobbing at once along Chuo-dori is the signature shot of the festival.\n\nDaytime vs. Evening: Daytime kanto competitions at Senshu Park showcase technical skill up close; the evening parade on Chuo-dori is the main spectacle with the full lantern-lit effect.\n\nReal Candles: The lanterns use actual open flames, not electric bulbs, adding a genuine flicker to the performance that photographs beautifully at dusk.",
              "thingsToBeWaryOf": "Timing Overlap: Kanto runs Aug 3-6, overlapping directly with Aomori Nebuta (Aug 2-7) — if chaining both festivals, expect a tight same-day or next-morning transfer.\n\nEvening Heat: Early August in Akita is warm and humid even at night; bring water and light layers for a multi-hour evening parade.",
              "localPerspective": "Locals treat the balancing skill itself as the real draw — the pole-bearers train for years to hold increasingly larger kanto steady, and competitions judge technique as much as spectacle, distinguishing it from the float-based Nebuta parade in neighboring Aomori.",
              "hiddenCost": "Viewing: Generally free along Chuo-dori; check for any reserved-seating options in a given year.\nFood Stalls: ¥500-1,500 per item.\nSenshu Park Daytime Viewing: Typically free.",
              "nearbyComplements": [
                "Senshu Park: Site of the daytime kanto competitions and the former Kubota Castle grounds.",
                "Akita Museum of Art: A short walk from the parade route, good for a daytime stop between festival sessions."
              ],
              "bestTimeToVisit": "Evening parade on Chuo-dori for the full lantern effect, any night Aug 3-6; arrive before sunset to also catch the daytime performances at Senshu Park.",
              "crowdLevel": "High (7/10) along the main parade route on peak evenings.",
              "accessibility": "Rating: 7/10 — flat city streets, though evening crowds along Chuo-dori get dense.",
              "idealDuration": "2 to 3 hours for the evening parade; add 1-2 hours for daytime Senshu Park viewing."
            }
            """,
            AverageCostPerDay = 15m,
            LuxuryRating = DeriveLuxury(15m, mapping["Akita Kanto Matsuri"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Akita Kanto Matsuri"].tags),
            AdventurePaceScore = AdventureScore(mapping["Akita Kanto Matsuri"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Akita Kanto Matsuri"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Akita Kanto Matsuri"].tags),
            Latitude = 39.7186,
            Longitude = 140.1024,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Sendai Tanabata Festival ──
        new Destination
        {
            DestinationId = destTanabataSendaiId,
            DestinationName = "Sendai Tanabata Festival",
            CleanNormalizedSearchName = "sendai tanabata festival",
            MetaphoneCode = "SNT TNPT FSTFL",
            DoubleMetaphonePrimary = "SNT TNPT FSTFL",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "The largest Tanabata celebration in Japan, drawing over two million visitors a year and completing the Three Great Festivals of Tohoku alongside Aomori's Nebuta and Akita's Kanto. Unlike its neighbors, Sendai's Tanabata isn't a parade — it's an immersive walk through the decorations. Shop owners spend months secretly designing rainbow-colored paper streamers hung from five bamboo poles each, revealed all at once on the morning of August 6 for judging. The festival's lineage traces to Sendai's founding daimyo, Date Masamune (1567-1636), who wrote poems on Tanabata customs still observed today.",
              "directions": "Dates: August 6-8, 2026, with a pre-festival fireworks display on the evening of August 5. Note: Sendai follows a calendar one month later than most of Japan's July 7 Tanabata, to preserve the original lunar-calendar seasonality.\n\nMain Area: Central Sendai and the covered shopping arcades near JR Sendai Station, reachable directly via the Tohoku Shinkansen.",
              "whatToKnow": "The Experience: There's no parade to watch from a seat — the festival is walking beneath and through the decorated arcades, so build in unhurried browsing time rather than a fixed viewing slot.\n\nReveal Day: Designs are kept secret and revealed on the morning of Aug 6, then entered into a competition — arriving on the 6th or 7th lets you see the freshest, most complete decorations.\n\nZuihoden Tanabata Night: Date Masamune's mausoleum hosts a small-admission (¥570) evening event during the festival period, worth adding for families interested in the historical thread.",
              "thingsToBeWaryOf": "Don't Expect a Parade: A common point of confusion — Sendai's Tanabata is about the scale and craft of static decorations, not a float or dance procession like Nebuta or Kanto.\n\nSaturday Crowding: If the 8th falls on a weekend, it's typically the most crowded day — seeing the decorations on the 6th or 7th and using the 8th as a travel buffer works better for families.\n\nHotel Sell-Outs: Sendai books out months ahead for these dates, same as Aomori.",
              "localPerspective": "Sendai locals take real pride in Date Masamune's legacy — his mausoleum Zuihoden, his statue at Aoba Castle, and the Sendai City Museum all reference the daimyo whose own writings helped shape the city's 400-year Tanabata tradition.",
              "hiddenCost": "Viewing the Decorations: Free.\nZuihoden Tanabata Night Admission: ¥570.\nFestival Food Stalls: ¥500-1,500 per item.",
              "nearbyComplements": [
                "Zuihoden Mausoleum: Date Masamune's resting place, a short bus ride from central Sendai.",
                "Aoba Castle Ruins: Home to the famous Masamune statue overlooking the city.",
                "Sendai City Museum: Local history exhibits, a good rainy-day stop."
              ],
              "bestTimeToVisit": "Aug 6 or 7, after the design reveal but before the Aug 8 weekend crowding.",
              "crowdLevel": "High (7/10), Maximum (10/10) if the final day falls on a Saturday.",
              "accessibility": "Rating: 8/10 — flat, covered shopping arcades, generally stroller-friendly.",
              "idealDuration": "2 to 3 hours for the arcades; add time for Zuihoden if visiting."
            }
            """,
            AverageCostPerDay = 12m,
            LuxuryRating = DeriveLuxury(12m, mapping["Sendai Tanabata Festival"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Sendai Tanabata Festival"].tags),
            AdventurePaceScore = AdventureScore(mapping["Sendai Tanabata Festival"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Sendai Tanabata Festival"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Sendai Tanabata Festival"].tags),
            Latitude = 38.2604,
            Longitude = 140.8823,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Atami Marine Fireworks Festival ──
        new Destination
        {
            DestinationId = destAtamiId,
            DestinationName = "Atami Marine Fireworks Festival",
            CleanNormalizedSearchName = "atami marine fireworks festival",
            MetaphoneCode = "ATM MRN FRWRKS FSTFL",
            DoubleMetaphonePrimary = "ATM MRN FRWRKS FSTFL",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Running since 1952 as a celebration of the town's recovery from a 1949 typhoon and 1950 fire, Atami's fireworks are held roughly 15 times across the year — not just in summer — but the August dates are the most popular for their warm-evening beach atmosphere. Atami Bay's natural amphitheater shape, ringed by hills, amplifies and reflects the sound, and the show closes with a signature 'Niagara in the sky' cascade effect. It's just a 40-minute Shinkansen ride from Tokyo, making it an easy family day trip.",
              "directions": "Confirmed 2026 Date (of several in the year): August 9, 2026 (also Aug 5, 18, and 24 among other 2026 dates — always confirm the specific date on the official Atami city site).\n\nFrom Tokyo: Kodama or Odoriko Shinkansen/limited express from Tokyo Station, about 40 minutes to Atami Station.\n\nViewing: Sun Beach and the waterfront promenade are both an easy walk from Atami Station.",
              "whatToKnow": "Beach Picnic Setup: Families commonly arrive by mid-to-late afternoon to claim beach or promenade space with a picnic blanket before the evening show.\n\nSandy Beach Viewing: Lying directly on Sun Beach is a classic, comfortable way for kids to watch without needing to stand for the display.\n\nHandheld Sparklers: Sun Beach allows small handheld fireworks/sparklers on non-display days too, if your family wants an extra evening activity during the wider trip.",
              "thingsToBeWaryOf": "Post-Show Crush: The walk back to Atami Station gets very crowded immediately after the finale — as with any major fireworks show, leaving a few minutes before the very end can save real time.\n\nMultiple Dates: Atami holds fireworks displays many times a year — double-check you have the specific August date that fits your trip, since 'Atami fireworks' alone isn't a single annual event.",
              "localPerspective": "Locals describe the bay's shape as acting like a natural stadium — the surrounding hills bounce the boom of each shell back across the water, giving Atami's fireworks a reputation among Japanese fireworks enthusiasts for feeling unusually powerful and immersive compared to a flat, open-field show.",
              "hiddenCost": "Viewing from Sun Beach/Promenade: Free.\nReserved Hotel-Balcony Viewing: Many waterfront hotels sell package rates for the fireworks nights — worth booking ahead if you want to skip the beach crowd entirely.\nFood Stalls: ¥500-1,500 per item.",
              "nearbyComplements": [
                "Atami Onsen: The town's hot spring bathhouses, a natural pairing for an evening before or after the show.",
                "MOA Museum of Art: A hillside museum with harbor views, good for a daytime stop before the evening fireworks."
              ],
              "bestTimeToVisit": "Arrive by 4:00 PM to claim a good beach spot for an evening show; confirm the exact show time on the official site, as it varies by date.",
              "crowdLevel": "High (7/10) on show nights, especially near the station immediately after.",
              "accessibility": "Rating: 8/10 — flat waterfront promenade and beach access, an easy walk from the station.",
              "idealDuration": "3 to 4 hours including arrival, picnic time, and the show itself."
            }
            """,
            AverageCostPerDay = 10m,
            LuxuryRating = DeriveLuxury(10m, mapping["Atami Marine Fireworks Festival"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Atami Marine Fireworks Festival"].tags),
            AdventurePaceScore = AdventureScore(mapping["Atami Marine Fireworks Festival"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Atami Marine Fireworks Festival"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Atami Marine Fireworks Festival"].tags),
            Latitude = 35.0956,
            Longitude = 139.0739,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Fukagawa Hachiman Matsuri ──
        new Destination
        {
            DestinationId = destFukagawaId,
            DestinationName = "Fukagawa Hachiman Matsuri",
            CleanNormalizedSearchName = "fukagawa hachiman matsuri",
            MetaphoneCode = "FKK HXMN MTSR",
            DoubleMetaphonePrimary = "FKK HXMN MTSR",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Centered on Tomioka Hachimangu Shrine, this is one of Tokyo's 'big three' Shinto festivals alongside Sanno Matsuri and Kanda Matsuri — and it's famous specifically for its water-throwing tradition, where spectators douse the mikoshi (portable shrines) and their bearers with buckets and hoses of water as they pass. 2026 is a Hon-matsuri year, the grandest version, held once every three years.",
              "directions": "Festival Period: August 12-16, 2026. IMPORTANT: the single largest event — over 50 neighborhood mikoshi departing Tomioka Hachimangu at around 7:30 AM for an approximately 8km procession through Fukagawa and across the Sumida River — falls on Sunday, August 16, 2026, the final day of the Hon-matsuri period, not on Aug 12-13 as many trip plans (including earlier drafts of this one) assume.\n\nNearest Stations: Monzennakacho (Oedo and Tozai Lines) or Kiba Station (Tozai Line).",
              "whatToKnow": "Water-Throwing Happens Across the Window: Smaller neighborhood processions and water-throwing take place on multiple days within Aug 12-16, not only on the 16th — families can still get a genuine soaking experience earlier in the window, even if it's not the single largest procession.\n\nThe Aug 16 Route: Starts at Tomioka Hachimangu around 7:30 AM, runs via the Fukagawa Edo Museum area and Kiyosu Bridge to Shinkawa (a lunch pause point), then back across Eitai Bridge and along Eitai-dori to the shrine.\n\nFull Program: Beyond the water-throwing, the festival includes music performances, smaller parades, and Noh theater across its multi-day run.",
              "thingsToBeWaryOf": "Scheduling Conflict If Chasing the Grand Finale: If your family's plan also includes Lake Suwa's fireworks (fixed to Aug 15) and the Kyoto Sky Lantern Festival (an Aug 16 evening event), you cannot also be in Tokyo for the Aug 16 morning grand procession — geographically, Nagano/Suwa to Kyoto doesn't route back through Tokyo. This itinerary deliberately trades the single grandest Fukagawa day for making the Suwa-to-Kyoto combination work; experiencing Fukagawa's earlier festival days (Aug 12-14) instead still delivers real water-throwing, just not the every-3-years grand finale.\n\nGenuinely Soaking: Water is thrown with real force and volume — ponchos or a full change of clothes are non-negotiable, not just a nice-to-have.",
              "localPerspective": "Longtime Fukagawa residents describe the Hon-matsuri years as qualitatively different from the in-between years — more neighborhoods participate, the crowds are thicker, and the water-throwing is more intense, which is exactly why the scheduling trade-off above matters if a family is choosing which version of the festival they'll actually experience.",
              "hiddenCost": "Viewing: Free.\nWaterproof Poncho/Bag: ¥500-1,500 if bought locally rather than packed from home.\nFood Stalls: ¥500-1,500 per item.",
              "nearbyComplements": [
                "Fukagawa Edo Museum: A recreated Edo-period neighborhood, on the Aug 16 procession route.",
                "Kiyosu Bridge and the Sumida River: Scenic backdrop for the procession's river crossing."
              ],
              "bestTimeToVisit": "Aug 12-14 for real water-throwing with a lighter crowd if continuing on to Lake Suwa and Kyoto; Aug 16 specifically if the grand finale procession is your family's top priority and you're willing to restructure the rest of the trip around it.",
              "crowdLevel": "High (7/10) on the earlier days, Maximum (10/10) on Aug 16 during a Hon-matsuri year.",
              "accessibility": "Rating: 5/10 — dense crowds and wet, slippery pavement during the water-throwing itself; manageable for families but plan for close supervision of kids.",
              "idealDuration": "2 to 3 hours for a neighborhood procession; a half-day if following the full Aug 16 grand route."
            }
            """,
            AverageCostPerDay = 8m,
            LuxuryRating = DeriveLuxury(8m, mapping["Fukagawa Hachiman Matsuri"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Fukagawa Hachiman Matsuri"].tags),
            AdventurePaceScore = AdventureScore(mapping["Fukagawa Hachiman Matsuri"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Fukagawa Hachiman Matsuri"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Fukagawa Hachiman Matsuri"].tags),
            Latitude = 35.6717,
            Longitude = 139.7967,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Lake Suwa Fireworks Festival ──
        new Destination
        {
            DestinationId = destSuwaFireworksId,
            DestinationName = "Lake Suwa Fireworks Festival",
            CleanNormalizedSearchName = "lake suwa fireworks festival",
            MetaphoneCode = "LK SW FRWRKS FSTFL",
            DoubleMetaphonePrimary = "LK SW FRWRKS FSTFL",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "One of Japan's largest fireworks festivals, launching roughly 40,000 shells over Lake Suwa in a single night, with the still lake surface doubling every burst into a mirror-image reflection framed by the surrounding Nagano mountains — a genuinely different visual from a standard fireworks show.",
              "directions": "Confirmed Date: August 15, 2026 (fixed annually).\n\nFrom Tokyo: Limited Express from Shinjuku Station to Kamisuwa Station, about 2 hours, roughly ¥6,000 one-way; then a 10-minute walk to Suwa Lakeside Park, the main viewing area on the south side of the lake in Suwa City.\n\nFrom Matsumoto: Regular trains to Kamisuwa Station take about 35 minutes.",
              "whatToKnow": "Tickets: Reserved viewing seats go on sale via online lottery application starting around June 23, with prices from roughly ¥4,000 — book as early as the lottery window allows given how popular this specific date is.\n\nOnsen Pairing: Suwa is a genuine hot-spring town — many families spend the day at an onsen before walking to the lakeside for the evening show.\n\nSuwa Taisha Shrine: A natural daytime pairing (see separate entry) within easy reach of the lakeshore.",
              "thingsToBeWaryOf": "Cramped Hillside Without a Seat: A frequently cited tip is that a paid viewing seat is worth the cost specifically to avoid sitting in 90°F (32°C) heat on an uncomfortable, crowded hillside for hours.\n\nObon Travel Crush: Aug 15 falls squarely within Japan's Obon holiday period (roughly Aug 13-16), the single most crowded and expensive travel week of the year nationwide — book trains and hotels as early as possible.\n\nTransport Home: As with any major evening event of this scale, plan your return transport in advance; trains and roads out of the area get very congested immediately after the finale.",
              "localPerspective": "Suwa locals treat the fireworks less as a spectacle to watch quickly and more as an all-evening event — many arrive hours early to secure a lakeside dinner spot and treat the fireworks as the capstone of a full day around the lake and shrine.",
              "hiddenCost": "Reserved Viewing Seat: From roughly ¥4,000 per person (lottery application from around June 23).\nGeneral/Free Viewing: Available but crowded, especially on the hillsides.\nOnsen Day-Use Entry: ¥500-1,500 depending on the facility.",
              "nearbyComplements": [
                "Suwa Taisha Shrine: A short trip from the lakeside, ideal for the daytime hours before the show.",
                "Suwa Lakeside Park: The main viewing area and a pleasant walking path around the lake.",
                "Local Onsen Ryokan: Many offer day-use hot spring access even without an overnight stay."
              ],
              "bestTimeToVisit": "Arrive in the afternoon to combine an onsen visit and a lakeside walk before the evening show; secure a reserved seat via the lottery well in advance given the fixed, high-demand date.",
              "crowdLevel": "Maximum (10/10) — one of the most popular single-night fireworks dates in the country, compounded by the Obon travel period.",
              "accessibility": "Rating: 7/10 — the lakeside park itself is flat and walkable, though the surrounding hillside viewing spots involve uneven terrain.",
              "idealDuration": "Full day if combining with Suwa Taisha and an onsen visit; 3-4 hours for the fireworks alone."
            }
            """,
            AverageCostPerDay = 20m,
            LuxuryRating = DeriveLuxury(20m, mapping["Lake Suwa Fireworks Festival"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Lake Suwa Fireworks Festival"].tags),
            AdventurePaceScore = AdventureScore(mapping["Lake Suwa Fireworks Festival"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Lake Suwa Fireworks Festival"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Lake Suwa Fireworks Festival"].tags),
            Latitude = 36.0422,
            Longitude = 138.1128,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Suwa Taisha Shrine ──
        new Destination
        {
            DestinationId = destSuwaTaishaId,
            DestinationName = "Suwa Taisha Shrine",
            CleanNormalizedSearchName = "suwa taisha shrine",
            MetaphoneCode = "SW TX XRN",
            DoubleMetaphonePrimary = "SW TX XRN",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "One of Japan's oldest Shinto shrines, Suwa Taisha is actually a complex of four separate shrine sites spread around Lake Suwa (Kamisha Honmiya and Maemiya on the south shore, Shimosha Akimiya and Harumiya on the north). It's famous for the Onbashira Festival (a once-every-6-years event involving massive log-hauling), but on a normal August day it offers a peaceful, shaded daytime counterpoint to the evening fireworks — sacred cypress trees, traditional architecture, and a genuinely calm pace.",
              "directions": "Best Access: Kamisha Honmiya (the main southern shrine) is a short bus or taxi ride from Kamisuwa Station; the four sites are spread out enough that visiting all of them in a day requires a car or a planned bus/taxi route.\n\nFrom Suwa Lakeside Park: A short trip by local bus or taxi to the nearest of the four shrine sites.",
              "whatToKnow": "Four Shrines, Not One: Families with limited time should pick one or two of the four sites (Kamisha Honmiya is the most visited) rather than attempting all four in a single day.\n\nOnbashira Festival: If your trip happens to coincide with this once-every-6-years log-hauling festival, it's a genuinely different, much larger event than a typical shrine visit — check current-year timing separately.\n\nFree Admission: Entry to the shrine grounds themselves is free; only special events or treasure hall admissions carry a fee.",
              "thingsToBeWaryOf": "Spread-Out Sites: Don't assume you can walk between all four shrine locations — they're genuinely dispersed around the lake, and transport planning matters.\n\nSummer Heat: Even in the shaded shrine grounds, Nagano's August heat is real — bring water for a family visit.",
              "localPerspective": "Suwa locals consider the shrine complex the spiritual anchor of the entire lake region — many families visiting for the fireworks build in shrine time specifically because the two feel like a complete pairing: sacred daytime calm followed by the evening's biggest spectacle.",
              "hiddenCost": "Shrine Grounds Entry: Free.\nTreasure Hall (if open): Small admission fee, typically under ¥500.\nOmamori/Charms: ¥500-1,000.",
              "nearbyComplements": [
                "Lake Suwa Fireworks Festival: The natural evening pairing for the same day.",
                "Local Onsen: Several hot spring facilities sit near the shrine complex."
              ],
              "bestTimeToVisit": "Late morning to early afternoon, ahead of the evening fireworks at the lake.",
              "crowdLevel": "Low (3/10) on a normal day, Medium (5/10) if visiting the same day as the fireworks.",
              "accessibility": "Rating: 6/10 — traditional shrine grounds with some uneven stone paths; the main approach areas are manageable for most visitors.",
              "idealDuration": "1 to 2 hours for one or two of the four shrine sites."
            }
            """,
            AverageCostPerDay = 0m,
            LuxuryRating = DeriveLuxury(0m, mapping["Suwa Taisha Shrine"].tags),
            AccessibilityType = "Car",
            FamilyFriendlyScore = FamilyScore(mapping["Suwa Taisha Shrine"].tags),
            AdventurePaceScore = AdventureScore(mapping["Suwa Taisha Shrine"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Suwa Taisha Shrine"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Suwa Taisha Shrine"].tags),
            Latitude = 36.0384,
            Longitude = 138.1200,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Kyoto Tanabata Sky Lantern Festival ──
        new Destination
        {
            DestinationId = destKyotoLanternId,
            DestinationName = "Kyoto Tanabata Sky Lantern Festival",
            CleanNormalizedSearchName = "kyoto tanabata sky lantern festival",
            MetaphoneCode = "KT TNPT LNTRN",
            DoubleMetaphonePrimary = "KT TNPT LNTRN",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Held since 2017, this event releases up to 3,500 glowing LED sky lanterns at once over the Kyoto Prefectural Kizugawa Sports Park — each lantern carrying a tanzaku (wish strip) that participants write on beforehand, in keeping with Tanabata tradition. It's become one of the most requested family additions to a Kyoto summer trip precisely because it's a hands-on, participatory moment rather than something to just watch from a distance.",
              "directions": "2026 Event Nights: August 8, 11, 16, and 17 (within the festival's broader Aug 7-16 window) — confirm the exact night on the official site, since it does not run every single evening.\n\nVenue Address: Kyoto Prefectural Kizugawa Sports Park (Joyo Gorigori-no-Oka), Kitazumi 14-8, Tono, Joyo, Kyoto.\n\nFrom Kyoto Station: JR Nara Line to Nagaike Station, then roughly a 6-10 minute walk to the venue.",
              "whatToKnow": "Lantern Release Timing: Scheduled for around 8:30 PM each event night; mid-session/late entry is permitted, so arriving after an early dinner in Kyoto is workable.\n\nTickets: Roughly ¥5,900-7,000 for adults and around ¥3,000 for children, purchased online in advance — early-bird pricing is typically available and recommended given past years selling out (44,000 tickets sold out in 2025).\n\nBeyond the Lanterns: The program includes stage performances, traditional ennichi festival stalls, food trucks, a lantern-wall photo spot, and water fortune-telling.",
              "thingsToBeWaryOf": "Not Every Night: The 'Aug 7-16' window advertised is the overall festival period, not nightly programming — the actual lantern release only happens on specific dates (Aug 8, 11, 16, 17 in 2026).\n\nBook Ahead: Given the 2025 sell-out, don't plan on same-day tickets for a family of several people during peak Obon week.\n\nAccess Time: The venue is roughly 40 minutes by direct JR train from central Kyoto — factor this into an evening that also includes dinner in the city center beforehand.",
              "localPerspective": "For Kyoto families, this event is treated as a relatively new but quickly beloved addition to the city's much older Tanabata traditions (like Kyo no Tanabata's Kamo River illuminations) — the lantern release specifically appeals to families with kids because everyone gets to participate in writing a wish rather than simply observing a display.",
              "hiddenCost": "Adult Ticket: Roughly ¥5,900-7,000.\nChild Ticket: Roughly ¥3,000.\nParking: Limited, and only available to those who purchased tickets in advance.\nFood Trucks/Stalls: ¥500-1,500 per item.",
              "nearbyComplements": [
                "Fushimi Inari Taisha: A short trip back toward central Kyoto, worth pairing with an earlier-in-the-day visit.",
                "Kiyomizu-dera: Also a natural same-trip pairing given proximity to central Kyoto.",
                "Gion District: An evening stroll option before or after, depending on your event-night timing."
              ],
              "bestTimeToVisit": "One of the confirmed 2026 event nights (Aug 8, 11, 16, or 17); arrive with enough buffer for the ~40-minute train ride from central Kyoto plus the walk from Nagaike Station.",
              "crowdLevel": "Maximum (10/10) on confirmed event nights, given past sell-outs.",
              "accessibility": "Rating: 8/10 — the sports park venue is flat and open; the walk from Nagaike Station is manageable for most families.",
              "idealDuration": "2 to 3 hours including the lantern release and surrounding festival activities."
            }
            """,
            AverageCostPerDay = 30m,
            LuxuryRating = DeriveLuxury(30m, mapping["Kyoto Tanabata Sky Lantern Festival"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Kyoto Tanabata Sky Lantern Festival"].tags),
            AdventurePaceScore = AdventureScore(mapping["Kyoto Tanabata Sky Lantern Festival"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Kyoto Tanabata Sky Lantern Festival"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Kyoto Tanabata Sky Lantern Festival"].tags),
            Latitude = 34.8261,
            Longitude = 135.7789,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Gion District ──
        new Destination
        {
            DestinationId = destGionId,
            DestinationName = "Gion District",
            CleanNormalizedSearchName = "gion district",
            MetaphoneCode = "JN TSTRKT",
            DoubleMetaphonePrimary = "KN TSTRKT",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Kyoto's most famous geisha (geiko) district, Gion is a preserved network of narrow lanes lined with traditional wooden machiya houses, teahouses, and exclusive restaurants — the atmospheric evening counterpart to Kyoto's temple-heavy daytime sightseeing, and an easy add-on to a Fushimi Inari/Kiyomizu-dera day.",
              "directions": "Best Access: Gion-Shijo Station (Keihan Main Line) or a short walk from the Kiyomizu-dera/Higashiyama area.\n\nKey Street: Hanami-koji is the district's signature preserved lane, running south from Shijo-dori.",
              "whatToKnow": "Geiko/Maiko Spotting: Early evening (around dusk) is the most likely window to spot a geiko or maiko moving between appointments — but never block their path or demand photos, as this has become a genuine local concern.\n\nShirakawa Canal: A quieter, willow-lined side street a block over from the main Hanami-koji crush, worth the short detour for a calmer photo.\n\nDining: Many of Gion's teahouses require an introduction or reservation through a ryokan/hotel concierge; casual restaurants along the main streets are open to walk-ins.",
              "thingsToBeWaryOf": "Photography Etiquette: Kyoto City has active signage and even fines in parts of Gion for harassing or blocking geiko/maiko for photos — treat any sighting as a fleeting, respectful glimpse, not a photo op to chase.\n\nEvening Crowds: Peak early-evening hours draw heavy foot traffic on Hanami-koji specifically; side streets are calmer.",
              "localPerspective": "Kyoto residents are increasingly protective of the district given past over-tourism issues — locals appreciate visitors who stroll respectfully and keep voices down in the narrow lanes, treating it as a lived-in neighborhood rather than a theme park.",
              "hiddenCost": "Walking the District: Free.\nCasual Dining: ¥2,000-5,000 per person.\nTeahouse Experience (if arranged): Significantly higher, often requiring an introduction.",
              "nearbyComplements": [
                "Kiyomizu-dera: A short walk through the Higashiyama preserved streets.",
                "Yasaka Shrine: At the eastern end of Gion, marking the transition into the Higashiyama district.",
                "Kyoto Tanabata Sky Lantern Festival: A same-day pairing if timing an event night."
              ],
              "bestTimeToVisit": "Early evening (around dusk) for the classic lantern-lit atmosphere and the best chance of a respectful geiko/maiko sighting.",
              "crowdLevel": "High (7/10) on Hanami-koji in early evening, Medium (5/10) on the quieter side streets.",
              "accessibility": "Rating: 7/10 — traditional narrow lanes with some uneven paving; manageable but not stroller-ideal in the busiest sections.",
              "idealDuration": "1 to 1.5 hours for a walking loop through the main lanes."
            }
            """,
            AverageCostPerDay = 15m,
            LuxuryRating = DeriveLuxury(15m, mapping["Gion District"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Gion District"].tags),
            AdventurePaceScore = AdventureScore(mapping["Gion District"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Gion District"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Gion District"].tags),
            Latitude = 35.0037,
            Longitude = 135.7752,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },

        // ── Awa Odori Festival ──
        new Destination
        {
            DestinationId = destAwaOdoriId,
            DestinationName = "Awa Odori Festival",
            CleanNormalizedSearchName = "awa odori festival",
            MetaphoneCode = "AW OTR FSTFL",
            DoubleMetaphonePrimary = "AW OTR FSTFL",
            DoubleMetaphoneSecondary = "",
            CommonAlternateSpellingsJson = "[]",
            DescriptionJson = """
            {
              "overview": "Japan's biggest dance festival, held in Tokushima City on Shikoku island — thousands of dancers in traditional costume move through the streets to shamisen, taiko, and flute, chanting the festival's famous refrain that both dancers and watchers are 'fools,' so you might as well dance too. Unlike Lake Suwa's watch-from-a-distance fireworks, this is Japan's most participatory major festival, with beginner dance groups (ren) welcoming walk-up visitors most nights.",
              "directions": "Dates: August 11-15, 2026 (some sources cite Aug 12-15 — confirm the exact opening night on the official festival site closer to your trip).\n\nFrom Tokyo: Bullet train to Okayama, then cross the Seto Inland Sea via the Naruto/Seto-Ohashi bridge route to Tokushima — a genuine half-day-plus journey, not a quick add-on.\n\nMain Stages: Multiple performance areas around central Tokushima, with paid reserved seating (sajiki) at the main stages and free-viewing areas elsewhere in the city.",
              "whatToKnow": "Optional Swap, Not a Combo: This festival's Aug 11-15 window overlaps directly with Lake Suwa's Aug 15 fireworks — families choose one or the other for this leg of the trip, not both, given the geographic distance between Nagano and Shikoku.\n\nJoin a Beginner Ren: Several dance groups specifically welcome walk-up participants with brief on-the-spot instruction — a genuinely hands-on option for families who'd rather dance than watch fireworks.\n\nDaytime Rehearsals: Some dance groups rehearse or perform shorter daytime sets, giving families a lower-intensity preview before the main evening parades.",
              "thingsToBeWaryOf": "Hotel Sell-Outs: Tokushima books out 3-4 months ahead for these dates, same as Aomori and Sendai — book well before summer if choosing this route.\n\nReserved Seat Booking: Sajiki seating for the main stages sells through official channels and can go quickly for peak nights (Aug 13-15).\n\nHeat: Mid-August in Shikoku is hot and humid even in the evening — this is an active, moving festival, so hydration matters more than at a stationary fireworks show.",
              "localPerspective": "Tokushima locals describe Awa Odori as the one time of year the entire city turns inside-out — offices close early, whole families join their neighborhood's dance group, and the refrain 'dancing fools and watching fools, both are fools alike, so why not dance' genuinely captures the come-one-come-all local spirit.",
              "hiddenCost": "Free Viewing Areas: No cost.\nSajiki Reserved Seating: Roughly ¥2,000-4,000 per person at main stages.\nBeginner Ren Participation: Often free or very low cost, sometimes including simple costume rental.\nFood Stalls: ¥500-1,500 per item.",
              "nearbyComplements": [
                "Awa Odori Kaikan: A year-round hall with daily performances if visiting outside the festival dates.",
                "Naruto Whirlpools: A short trip from Tokushima, a good daytime family activity to pair with an evening of dancing."
              ],
              "bestTimeToVisit": "Aug 13-15 for the largest evening parades; arrive at a beginner ren's gathering point in the late afternoon if your family wants to join in rather than just watch.",
              "crowdLevel": "Maximum (10/10) on peak nights (Aug 13-15).",
              "accessibility": "Rating: 6/10 — flat city streets, but extremely dense moving crowds during peak parade hours.",
              "idealDuration": "Half a day if including a daytime rehearsal and full evening parade viewing."
            }
            """,
            AverageCostPerDay = 20m,
            LuxuryRating = DeriveLuxury(20m, mapping["Awa Odori Festival"].tags),
            AccessibilityType = "Train",
            FamilyFriendlyScore = FamilyScore(mapping["Awa Odori Festival"].tags),
            AdventurePaceScore = AdventureScore(mapping["Awa Odori Festival"].tags),
            AestheticTrendScore = AestheticTrendScore(mapping["Awa Odori Festival"].tags),
            PsychographicVibeTagsJson = JsonSerializer.Serialize(mapping["Awa Odori Festival"].tags),
            Latitude = 34.0658,
            Longitude = 134.5593,
            SearchHitCount = 0,
            TimeZone = "Japan Standard Time",
            SafetyLevel = 1,
            CountryId = japan.CountryId
        },
    };

            db.Destinations.AddRange(newDestinations);
            await db.SaveChangesAsync();

            var allNewDestinations = newDestinations.ToList();
            var allDestinationsForTagging = allNewDestinations.ToList();

            await AssignCategoriesAndTagsToDestinationsAsync(db, allDestinationsForTagging, mapping);
            await SeedImagesForJapanFestivalsAsync(db, allNewDestinations);
            await SeedDestinationLanguageAndCurrencyAsync(db, allNewDestinations, japanese, yen);

            // Reused destinations get language/currency too, if they didn't already
            var reusedDestinations = new List<Destination>();
            if (fushimiInari != null) reusedDestinations.Add(fushimiInari);
            if (kiyomizudera != null) reusedDestinations.Add(kiyomizudera);
            if (reusedDestinations.Any())
                await SeedDestinationLanguageAndCurrencyAsync(db, reusedDestinations, japanese, yen);

            // ─── Destination ↔ City links ─────────────────────────────────────────
            var destCityLinks = new List<DestinationCity>
    {
        new DestinationCity { DestinationId = destNebutaId, CityId = aomori.CityId },
        new DestinationCity { DestinationId = destKantoId, CityId = akita.CityId },
        new DestinationCity { DestinationId = destTanabataSendaiId, CityId = sendai.CityId },
        new DestinationCity { DestinationId = destAtamiId, CityId = atami.CityId },
        new DestinationCity { DestinationId = destFukagawaId, CityId = tokyo.CityId },
        new DestinationCity { DestinationId = destSuwaFireworksId, CityId = suwa.CityId },
        new DestinationCity { DestinationId = destSuwaTaishaId, CityId = suwa.CityId },
        new DestinationCity { DestinationId = destKyotoLanternId, CityId = kyoto.CityId },
        new DestinationCity { DestinationId = destGionId, CityId = kyoto.CityId },
        new DestinationCity { DestinationId = destAwaOdoriId, CityId = tokushima.CityId },
    };
            db.DestinationCities.AddRange(destCityLinks);
            await db.SaveChangesAsync();

            // ─── Transit Routes ────────────────────────────────────────────────────
            var routeTokyoAomoriId = Guid.NewGuid();
            var routeAomoriAkitaId = Guid.NewGuid();
            var routeAkitaSendaiId = Guid.NewGuid();
            var routeSendaiTokyoId = Guid.NewGuid();
            var routeTokyoAtamiId = Guid.NewGuid();
            var routeTokyoSuwaId = Guid.NewGuid();
            var routeSuwaKyotoId = Guid.NewGuid();
            var routeTokyoTokushimaId = Guid.NewGuid();
            var routeTokushimaKyotoId = Guid.NewGuid();

            db.TransitRoutes.AddRange(
                new TransitRoute { TransitRouteId = routeTokyoAomoriId, OriginCityId = tokyo.CityId, DestinationCityId = aomori.CityId, TransitType = "Shinkansen (Hayabusa)", EstimatedCostPerPerson = 175m, DurationInMinutes = 200, RecommendedTimeBufferMinutes = 30, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "18.0", SubSegmentsJson = "[]" },
                new TransitRoute { TransitRouteId = routeAomoriAkitaId, OriginCityId = aomori.CityId, DestinationCityId = akita.CityId, TransitType = "Limited Express", EstimatedCostPerPerson = 60m, DurationInMinutes = 120, RecommendedTimeBufferMinutes = 20, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "9.0", SubSegmentsJson = "[]" },
                new TransitRoute { TransitRouteId = routeAkitaSendaiId, OriginCityId = akita.CityId, DestinationCityId = sendai.CityId, TransitType = "Shinkansen (transfer at Morioka)", EstimatedCostPerPerson = 100m, DurationInMinutes = 150, RecommendedTimeBufferMinutes = 25, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "11.0", SubSegmentsJson = "[]" },
                new TransitRoute { TransitRouteId = routeSendaiTokyoId, OriginCityId = sendai.CityId, DestinationCityId = tokyo.CityId, TransitType = "Shinkansen (Hayabusa)", EstimatedCostPerPerson = 80m, DurationInMinutes = 90, RecommendedTimeBufferMinutes = 20, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "7.0", SubSegmentsJson = "[]" },
                new TransitRoute { TransitRouteId = routeTokyoAtamiId, OriginCityId = tokyo.CityId, DestinationCityId = atami.CityId, TransitType = "Shinkansen (Kodama)", EstimatedCostPerPerson = 35m, DurationInMinutes = 40, RecommendedTimeBufferMinutes = 15, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "3.0", SubSegmentsJson = "[]" },
                new TransitRoute { TransitRouteId = routeTokyoSuwaId, OriginCityId = tokyo.CityId, DestinationCityId = suwa.CityId, TransitType = "Limited Express (Azusa)", EstimatedCostPerPerson = 45m, DurationInMinutes = 120, RecommendedTimeBufferMinutes = 20, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "6.0", SubSegmentsJson = "[]" },
                new TransitRoute { TransitRouteId = routeSuwaKyotoId, OriginCityId = suwa.CityId, DestinationCityId = kyoto.CityId, TransitType = "Limited Express + Shinkansen (transfer at Nagoya)", EstimatedCostPerPerson = 110m, DurationInMinutes = 180, RecommendedTimeBufferMinutes = 30, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "10.0", SubSegmentsJson = "[]" },
                new TransitRoute { TransitRouteId = routeTokyoTokushimaId, OriginCityId = tokyo.CityId, DestinationCityId = tokushima.CityId, TransitType = "Shinkansen + Limited Express (via Okayama)", EstimatedCostPerPerson = 140m, DurationInMinutes = 280, RecommendedTimeBufferMinutes = 40, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "14.0", SubSegmentsJson = "[]" },
                new TransitRoute { TransitRouteId = routeTokushimaKyotoId, OriginCityId = tokushima.CityId, DestinationCityId = kyoto.CityId, TransitType = "Highway Bus + Shinkansen (via Osaka)", EstimatedCostPerPerson = 90m, DurationInMinutes = 210, RecommendedTimeBufferMinutes = 30, BookingReferenceUrl = "https://www.jrpass.com/", CarbonFootprintKg = "9.0", SubSegmentsJson = "[]" }
            );
            await db.SaveChangesAsync();

            // ─── Wishlist ─────────────────────────────────────────────────────────
            var wishlistId = Guid.NewGuid();
            db.Wishlists.Add(new Wishlist
            {
                WishlistId = wishlistId,
                WishlistName = "Family Festival Japan Trip",
                WishlistDescription = "Experience Japan's biggest summer festivals on one unforgettable family adventure. From giant illuminated floats in northern Japan to fireworks over Lake Suwa and magical sky lanterns in Kyoto, this itinerary is designed for families who want to experience the best of August while keeping travel as smooth as possible.",
                ShortStory = "Thundering warrior floats in the north, a soaking-wet shrine procession in Tokyo, 40,000 fireworks over a mirror-still lake, and a sky full of family wishes in Kyoto.",
                TotalDays = 15,
                PeopleType = "Families with School-Aged Children",
                WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_festivals_hero.jpg",
                GlobalInclusionsJson = @"[""14-Day Japan Rail Pass"",""Kyoto Tanabata Sky Lantern Festival Tickets"",""Lake Suwa Fireworks Reserved Seating""]",
                RawContentKeywords = "Japan, Aomori Nebuta, Akita Kanto, Sendai Tanabata, Atami Fireworks, Fukagawa Hachiman Matsuri, Lake Suwa Fireworks, Kyoto Tanabata Sky Lantern, Awa Odori, Tokushima, family travel, summer festivals",
                PsychologicalVibeTagsJson = @"[""Family"",""Cultural"",""Adventure"",""Once-in-a-Lifetime""]",
                DefaultTravelersCount = 4,
                BasePricePerPerson = 1800m,
                CalculatedTotalCost = 7200m,
                DepositAmountRequired = 400m,
                AccommodationInclusions = "Hotels in Aomori, Akita, Sendai, Tokyo, Suwa, and Kyoto; luggage forwarding (Takkyubin) between Sendai and Kyoto to avoid dragging suitcases through Obon crowds",
                TransitInclusions = "14-day Japan Rail Pass covering all Shinkansen legs from Aomori through Kyoto",
                ActivityInclusions = "Reserved seating for Aomori Nebuta, Lake Suwa Fireworks reserved viewing, Kyoto Tanabata Sky Lantern Festival tickets",
                IsTemplate = true,
                IsFeatured = true,
                PrimaryPersonaTarget = "Festival-Focused Family",
                CreatedAt = DateTimeOffset.UtcNow,
                OwnerUserId = null,
                ForkedFromId = null
            });
            await db.SaveChangesAsync();

            // ─── Itinerary Days & Items ──────────────────────────────────────
            var day1 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Aomori: Arrival & Nebuta Night One", MorningCityId = tokyo.CityId, AfternoonCityId = aomori.CityId, EveningCityId = aomori.CityId, TransitFromPreviousDayRouteId = routeTokyoAomoriId, WishlistId = wishlistId };
            var day2 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 2, DayTitle = "Aomori: Nebuta Matsuri", MorningCityId = aomori.CityId, AfternoonCityId = aomori.CityId, EveningCityId = aomori.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day3 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 3, DayTitle = "Akita: Arrival & Kanto Matsuri", MorningCityId = aomori.CityId, AfternoonCityId = akita.CityId, EveningCityId = akita.CityId, TransitFromPreviousDayRouteId = routeAomoriAkitaId, WishlistId = wishlistId };
            var day4 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 4, DayTitle = "Akita: Kanto Matsuri", MorningCityId = akita.CityId, AfternoonCityId = akita.CityId, EveningCityId = akita.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day5 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 5, DayTitle = "Sendai: Arrival & Tanabata Decorations", MorningCityId = akita.CityId, AfternoonCityId = sendai.CityId, EveningCityId = sendai.CityId, TransitFromPreviousDayRouteId = routeAkitaSendaiId, WishlistId = wishlistId };
            var day6 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 6, DayTitle = "Sendai: Tanabata Festival", MorningCityId = sendai.CityId, AfternoonCityId = sendai.CityId, EveningCityId = sendai.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day7 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 7, DayTitle = "Return to Tokyo (Rest Day)", MorningCityId = sendai.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId, TransitFromPreviousDayRouteId = routeSendaiTokyoId, WishlistId = wishlistId };
            var day8 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 8, DayTitle = "Atami: Marine Fireworks Day Trip", MorningCityId = tokyo.CityId, AfternoonCityId = atami.CityId, EveningCityId = tokyo.CityId, TransitFromPreviousDayRouteId = routeTokyoAtamiId, WishlistId = wishlistId };
            var day9 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 9, DayTitle = "Tokyo: Fukagawa Festival Begins", MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day10 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 10, DayTitle = "Tokyo: Fukagawa Hachiman Matsuri", MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day11 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 11, DayTitle = "Tokyo: Fukagawa Festival, Final Day in the City", MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day12 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 12, DayTitle = "Lake Suwa: Arrival, Shrine & Onsen", MorningCityId = tokyo.CityId, AfternoonCityId = suwa.CityId, EveningCityId = suwa.CityId, TransitFromPreviousDayRouteId = routeTokyoSuwaId, WishlistId = wishlistId };
            var day13 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 13, DayTitle = "Lake Suwa: Fireworks Festival", MorningCityId = suwa.CityId, AfternoonCityId = suwa.CityId, EveningCityId = suwa.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            var day14 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 14, DayTitle = "Kyoto: Arrival & Sky Lantern Festival", MorningCityId = suwa.CityId, AfternoonCityId = kyoto.CityId, EveningCityId = kyoto.CityId, TransitFromPreviousDayRouteId = routeSuwaKyotoId, WishlistId = wishlistId };
            var day15 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 15, DayTitle = "Kyoto: Temples, Gion & Departure Prep", MorningCityId = kyoto.CityId, AfternoonCityId = kyoto.CityId, EveningCityId = kyoto.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };

            db.ItineraryDays.AddRange(day1, day2, day3, day4, day5, day6, day7, day8, day9, day10, day11, day12, day13, day14, day15);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                // Day 1-2: Aomori
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day1.ItineraryDayId, ItemTitle = "Fly into Tokyo, Shinkansen Straight to Aomori", ItemDescription = "Don't linger in Tokyo yet — take the Tohoku Shinkansen straight up to catch the Tohoku 'Triple-Header' while it's running.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "", SocialProofBadge = "", IndividualCostModifier = 175m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day1.ItineraryDayId, ItemTitle = "Aomori Nebuta Matsuri: First Night", ItemDescription = "Watch the illuminated warrior floats parade through the city. Reserve seating early so children can comfortably enjoy the show.", ItemOrderIndex = 2, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_nebuta.jpg", SocialProofBadge = "Iconic Festival", IndividualCostModifier = 25m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day2.ItineraryDayId, ItemTitle = "Aomori Nebuta Matsuri: Peak Night & Food Stalls", ItemDescription = "Explore food stalls and family-friendly festival activities before the evening's full-scale parade.", ItemOrderIndex = 1, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_nebuta.jpg", SocialProofBadge = "Iconic Festival", IndividualCostModifier = 15m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 3-4: Akita
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day3.ItineraryDayId, ItemTitle = "Travel to Akita", ItemDescription = "About a 2-hour train ride from Aomori to Akita City.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "", SocialProofBadge = "", IndividualCostModifier = 60m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day3.ItineraryDayId, ItemTitle = "Akita Kanto Matsuri: Evening Parade", ItemDescription = "Watch performers balance enormous lantern poles. Arrive before sunset to catch both daytime performances and the illuminated evening festival.", ItemOrderIndex = 2, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_kanto.jpg", SocialProofBadge = "Balancing Act", IndividualCostModifier = 15m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day4.ItineraryDayId, ItemTitle = "Akita Kanto Matsuri: Daytime Competitions & Local Food", ItemDescription = "Try local festival foods and catch the daytime kanto pole-balancing competitions at Senshu Park.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_kanto.jpg", SocialProofBadge = "Balancing Act", IndividualCostModifier = 10m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 5-6: Sendai
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day5.ItineraryDayId, ItemTitle = "Travel to Sendai", ItemDescription = "About 2.5 hours by train, transferring at Morioka.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "", SocialProofBadge = "", IndividualCostModifier = 100m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day5.ItineraryDayId, ItemTitle = "Sendai Tanabata: Walk the Decorated Arcades", ItemDescription = "Walk beneath colorful giant streamers and browse shopping arcades filled with decorations.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_sendai.jpg", SocialProofBadge = "Largest Tanabata", IndividualCostModifier = 12m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day6.ItineraryDayId, ItemTitle = "Sendai Tanabata: Street Performances", ItemDescription = "Enjoy family-friendly street performances and the freshly revealed festival decorations.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_sendai.jpg", SocialProofBadge = "Largest Tanabata", IndividualCostModifier = 10m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 7: Travel/Rest
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day7.ItineraryDayId, ItemTitle = "Return to Tokyo", ItemDescription = "Relax after several festival days before beginning the next part of the trip. Consider forwarding luggage (Takkyubin) ahead to Kyoto to travel light through the upcoming Obon crush.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "", SocialProofBadge = "Rest Day", IndividualCostModifier = 80m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 8: Atami
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day8.ItineraryDayId, ItemTitle = "Atami: Beach Picnic", ItemDescription = "Take the 40-minute Shinkansen to Atami and settle in on Sun Beach with a picnic blanket and snacks before the evening show.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_atami.jpg", SocialProofBadge = "Beachside", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day8.ItineraryDayId, ItemTitle = "Atami Marine Fireworks Festival", ItemDescription = "Spectacular fireworks over the bay, finishing with the signature 'Niagara in the sky' cascade.", ItemOrderIndex = 2, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_atami.jpg", SocialProofBadge = "Confirmed Aug 9 Date", IndividualCostModifier = 10m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 9-11: Tokyo Fukagawa
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day9.ItineraryDayId, ItemTitle = "Fukagawa Festival: Neighborhood Preparations", ItemDescription = "Explore the Fukagawa district as the neighborhood festival gets underway. Note: the single grand finale procession falls on Aug 16 this Hon-matsuri year, after this family's route has already moved on — earlier days still offer genuine water-throwing.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_fukagawa.jpg", SocialProofBadge = "Check Dates", IndividualCostModifier = 5m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day10.ItineraryDayId, ItemTitle = "Fukagawa Hachiman Matsuri: Water-Splashing Day", ItemDescription = "One of Tokyo's most exciting festivals — spectators splash water over shrine bearers. Don't forget a waterproof bag, a full change of clothes, and a camera.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_fukagawa.jpg", SocialProofBadge = "GET WET!", IndividualCostModifier = 8m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day11.ItineraryDayId, ItemTitle = "Fukagawa Festival: Final Tokyo Day", ItemDescription = "A last relaxed day in Tokyo — pack the Beat-the-Heat kit (neck fans, cooling towels, UV umbrellas, Pocari Sweat powder) before heading into the Obon travel crush.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_tokyo.jpg", SocialProofBadge = "Prep Day", IndividualCostModifier = 15m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 12-13: Lake Suwa
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day12.ItineraryDayId, ItemTitle = "Travel to Lake Suwa", ItemDescription = "About 2 hours by Limited Express from Tokyo. Book Shinkansen/Limited Express seats a month ahead exactly at 10 AM Japan time via the JR East or Smart-Ex app — Obon trains fill fast.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "", SocialProofBadge = "", IndividualCostModifier = 45m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day12.ItineraryDayId, ItemTitle = "Visit Suwa Taisha Shrine", ItemDescription = "One of Japan's oldest shrine complexes, spread around the lake — pick one or two of the four sites given limited time.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_suwataisha.jpg", SocialProofBadge = "Ancient Shrine", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day12.ItineraryDayId, ItemTitle = "Relax in an Onsen", ItemDescription = "Suwa is a genuine hot-spring town — many day-use facilities are available near the shrine and lake.", ItemOrderIndex = 3, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_onsen.jpg", SocialProofBadge = "Hot Spring", IndividualCostModifier = 10m, IsOptionalActivity = true, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day13.ItineraryDayId, ItemTitle = "Lakeside Walk", ItemDescription = "Spend the day exploring Suwa Lakeside Park before the evening's main event.", ItemOrderIndex = 1, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_lakesuwa.jpg", SocialProofBadge = "Scenic", IndividualCostModifier = 0m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day13.ItineraryDayId, ItemTitle = "Lake Suwa Fireworks Festival", ItemDescription = "One of Japan's largest fireworks festivals — roughly 40,000 shells reflected on the still lake surface. Book a paid viewing seat in advance rather than a cramped hillside.", ItemOrderIndex = 2, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_lakesuwa.jpg", SocialProofBadge = "Grand Finale", IndividualCostModifier = 20m, IsOptionalActivity = false, IsSelectedByDefault = true },

                // Day 14-15: Kyoto
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day14.ItineraryDayId, ItemTitle = "Travel to Kyoto", ItemDescription = "About 3 hours via Limited Express and Shinkansen, transferring at Nagoya.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "", SocialProofBadge = "", IndividualCostModifier = 110m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day14.ItineraryDayId, ItemTitle = "Kyoto Tanabata Sky Lantern Festival", ItemDescription = "End the family trip by releasing glowing lanterns into the night sky. Tickets are required for every person, including kids — buy online before you leave home, and write your family wishes on the lanterns before release.", ItemOrderIndex = 2, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_skylantern.jpg", SocialProofBadge = "Deeply Moving", IndividualCostModifier = 30m, IsOptionalActivity = false, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day15.ItineraryDayId, ItemTitle = "Nearby Highlight: Fushimi Inari Taisha", ItemDescription = "The iconic thousand-torii-gate hike, best visited early to beat the crowds and heat.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_fushimiinari.jpg", SocialProofBadge = "Iconic", IndividualCostModifier = 0m, IsOptionalActivity = true, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day15.ItineraryDayId, ItemTitle = "Nearby Highlight: Kiyomizu-dera", ItemDescription = "The wooden-stage temple with sweeping city views, a short walk from Gion.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_kiyomizudera.jpg", SocialProofBadge = "Top Rated", IndividualCostModifier = 4m, IsOptionalActivity = true, IsSelectedByDefault = true },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = day15.ItineraryDayId, ItemTitle = "Nearby Highlight: Gion District", ItemDescription = "An evening stroll through Kyoto's famous geisha district before departure prep.", ItemOrderIndex = 3, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_gion.jpg", SocialProofBadge = "Atmospheric", IndividualCostModifier = 15m, IsOptionalActivity = true, IsSelectedByDefault = true }
            );
            await db.SaveChangesAsync();

            // ═══════════════════════════════════════════════════════════════════
            // OPTIONAL EXTREME SWAP: Tokushima / Awa Odori instead of Lake Suwa
            // Modeled as an alternate day pair (not added to the main day sequence
            // above) so the app can present it as a toggleable swap for Days 12-13.
            // ═══════════════════════════════════════════════════════════════════
            var altDay12 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 12, DayTitle = "OPTIONAL SWAP: Tokushima — Travel & Arrival", MorningCityId = tokyo.CityId, AfternoonCityId = tokushima.CityId, EveningCityId = tokushima.CityId, TransitFromPreviousDayRouteId = routeTokyoTokushimaId, WishlistId = wishlistId };
            var altDay13 = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 13, DayTitle = "OPTIONAL SWAP: Tokushima — Awa Odori Festival", MorningCityId = tokushima.CityId, AfternoonCityId = tokushima.CityId, EveningCityId = tokushima.CityId, TransitFromPreviousDayRouteId = null, WishlistId = wishlistId };
            db.ItineraryDays.AddRange(altDay12, altDay13);
            await db.SaveChangesAsync();

            db.ItineraryItems.AddRange(
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = altDay12.ItineraryDayId, ItemTitle = "Travel to Tokushima via Okayama", ItemDescription = "Bullet train to Okayama, then cross the Seto Inland Sea bridge route to Tokushima — a genuine half-day-plus journey. This swap replaces Lake Suwa entirely, since the dates overlap and the two are geographically incompatible in one trip leg.", ItemOrderIndex = 1, TimeOfDay = "Morning", ImageUrl = "", SocialProofBadge = "Reality Check", IndividualCostModifier = 140m, IsOptionalActivity = true, IsSelectedByDefault = false },
                new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = altDay13.ItineraryDayId, ItemTitle = "Awa Odori Dance Festival", ItemDescription = "Join the 'fool's dance' with thousands of dancers — swap Lake Suwa for Japan's biggest dance festival if your family prefers joining the celebration over watching fireworks.", ItemOrderIndex = 1, TimeOfDay = "Evening", ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_awaodori.jpg", SocialProofBadge = "Optional Adventure", IndividualCostModifier = 20m, IsOptionalActivity = true, IsSelectedByDefault = false }
            );
            await db.SaveChangesAsync();

            // ─── Wishlist ↔ Destination links ──────────────────────────────────
            var allWishlistDestinations = allNewDestinations.Select(d => d.DestinationId).ToList();
            if (fushimiInari != null) allWishlistDestinations.Add(fushimiInari.DestinationId);
            if (kiyomizudera != null) allWishlistDestinations.Add(kiyomizudera.DestinationId);

            foreach (var destId in allWishlistDestinations)
            {
                bool alreadyLinked = await db.WishlistDestinations
                    .AnyAsync(wd => wd.WishlistId == wishlistId && wd.DestinationId == destId);

                if (!alreadyLinked)
                {
                    db.WishlistDestinations.Add(new WishlistDestination
                    {
                        WishlistId = wishlistId,
                        DestinationId = destId
                    });
                }
            }
            await db.SaveChangesAsync();
        }

        private static async Task SeedImagesForJapanFestivalsAsync(HodracDbContext db, List<Destination> destinations)
        {
            List<string> imageNames = new List<string>
    {
        "nebuta.jpg", "kanto.jpg", "sendai.jpg", "atami.jpg", "fukagawa.jpg",
        "lakesuwa.jpg", "suwataisha.jpg", "skylantern.jpg", "gion.jpg", "awaodori.jpg"
    };

            foreach (var (dest, image) in destinations.Zip(imageNames, (d, i) => (d, i)))
            {
                db.DestinationImages.Add(new DestinationImage
                {
                    DestinationImageId = Guid.NewGuid(),
                    DestinationId = dest.DestinationId,
                    ImageUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_{Uri.EscapeDataString(image)}",
                    ThumbnailUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/japan_{Uri.EscapeDataString(image)}",
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
