using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;


namespace Hodrac_Backend_MVP2.Infrastructure.Seeder;

/// <summary>
/// Seeds all reference data and migrates existing destinations + wishlists
/// into the new HodracDbContext schema.
///
/// Call order (dependencies must be satisfied before dependents):
///   1. SeedLanguages
///   2. SeedCurrencies
///   3. SeedCountries          (no FK deps)
///   4. SeedCities             (depends on Countries)
///   5. SeedCategories
///   6. SeedTags
///   7. SeedDestinations       (depends on all of the above)
///   8. SeedWishlists          (depends on Destinations, Cities)
///   9. SeedFeaturedPool       (depends on Wishlists)
/// </summary>
//public static class DataSeeder
//{
//    // ─── Entry point ──────────────────────────────────────────────────────────

//    public static async Task SeedAllAsync(HodracDbContext db)
//    {
//        await SeedLanguagesAsync(db);
//        await SeedCurrenciesAsync(db);
//        await SeedCountriesAsync(db);
//        await SeedCitiesAsync(db);
//        await SeedCategoriesAsync(db);
//        await SeedTagsAsync(db);
//        await SeedDestinationsAsync(db);
//        await SeedWishlistsAsync(db);
//        await SeedFeaturedPoolAsync(db);
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 1. LANGUAGES
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedLanguagesAsync(HodracDbContext db)
//    {
//        if (await db.Languages.AnyAsync()) return;

//        db.Languages.AddRange(
//            new Language { LanguageId = Guid.NewGuid(), LanguageName = "Japanese", LanguageCode = "ja", RequiresCertifiedLocalGuide = false },
//            new Language { LanguageId = Guid.NewGuid(), LanguageName = "Spanish", LanguageCode = "es", RequiresCertifiedLocalGuide = false },
//            new Language { LanguageId = Guid.NewGuid(), LanguageName = "English", LanguageCode = "en", RequiresCertifiedLocalGuide = false },
//            new Language
//            {
//                LanguageId = Guid.NewGuid(),
//                LanguageName = "Dulegaya (Guna/Kuna)",
//                LanguageCode = "cuk",
//                RequiresCertifiedLocalGuide = true,
//                HelpfulSurvivalPhrasesJson = JsonSerializer.Serialize(new[]
//                {
//                    new { phrase = "Hello",    local = "Degi" },
//                    new { phrase = "Thank you", local = "Nued" },
//                    new { phrase = "Yes",      local = "Ehe" },
//                    new { phrase = "No",       local = "Suli" },
//                })
//            }
//        );
//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 2. CURRENCIES
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedCurrenciesAsync(HodracDbContext db)
//    {
//        if (await db.Currencies.AnyAsync()) return;

//        db.Currencies.AddRange(
//            new Currency { CurrencyId = Guid.NewGuid(), CurrencyName = "Japanese Yen", CurrencyCode = "JPY", CurrencySymbol = "¥", ExchangeRateToBase = 0.0067m, LastExchangeRateUpdate = DateTimeOffset.UtcNow },
//            new Currency { CurrencyId = Guid.NewGuid(), CurrencyName = "United States Dollar", CurrencyCode = "USD", CurrencySymbol = "$", ExchangeRateToBase = 1.0000m, LastExchangeRateUpdate = DateTimeOffset.UtcNow },
//            new Currency { CurrencyId = Guid.NewGuid(), CurrencyName = "Panamanian Balboa", CurrencyCode = "PAB", CurrencySymbol = "B/.", ExchangeRateToBase = 1.0000m, LastExchangeRateUpdate = DateTimeOffset.UtcNow }
//        );
//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 3. COUNTRIES
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedCountriesAsync(HodracDbContext db)
//    {
//        if (await db.Countries.AnyAsync()) return;

//        var japanese = await db.Languages.FirstAsync(l => l.LanguageName == "Japanese");
//        var spanish = await db.Languages.FirstAsync(l => l.LanguageName == "Spanish");
//        var english = await db.Languages.FirstAsync(l => l.LanguageName == "English");

//        var japan = new Country
//        {
//            CountryId = Guid.NewGuid(),
//            CountryName = "Japan",
//            Continent = "Asia",
//            CountryFlagEmoji = "🇯🇵",
//            GlobalHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tokyo.jpg",
//            VisaRequirementsSummary = "Visa-free for most nationalities for up to 90 days.",
//            PowerPlugType = "Type A/B",
//            DrivingSide = "Left",
//            EstimatedDailyTaxRate = 0.10m,
//        };
//        japan.CountryLanguages = new List<CountryLanguage>
//        {
//            new() { CountryId = japan.CountryId, LanguageId = japanese.LanguageId }
//        };

//        var panama = new Country
//        {
//            CountryId = Guid.NewGuid(),
//            CountryName = "Panama",
//            Continent = "Central America",
//            CountryFlagEmoji = "🇵🇦",
//            GlobalHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/panamacity.jpeg",
//            VisaRequirementsSummary = "Visa-free for US, EU, and most Commonwealth citizens for up to 180 days.",
//            PowerPlugType = "Type A/B",
//            DrivingSide = "Right",
//            EstimatedDailyTaxRate = 0.07m,
//        };
//        panama.CountryLanguages = new List<CountryLanguage>
//        {
//            new() { CountryId = panama.CountryId, LanguageId = spanish.LanguageId },
//            new() { CountryId = panama.CountryId, LanguageId = english.LanguageId },
//        };

//        db.Countries.AddRange(japan, panama);
//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 4. CITIES
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedCitiesAsync(HodracDbContext db)
//    {
//        if (await db.Cities.AnyAsync()) return;

//        var japan = await db.Countries.FirstAsync(c => c.CountryName == "Japan");
//        var panama = await db.Countries.FirstAsync(c => c.CountryName == "Panama");

//        db.Cities.AddRange(
//            new City { CityId = Guid.NewGuid(), CountryId = japan.CountryId, CityName = "Tokyo", CityDescription = "Japan's capital and the world's most populous metropolitan area.", Latitude = 35.6762, Longitude = 139.6503 },
//            new City { CityId = Guid.NewGuid(), CountryId = panama.CountryId, CityName = "Panama City", CityDescription = "The capital and largest city of Panama, on the Pacific coast.", Latitude = 8.9936, Longitude = -79.5197 },
//            new City { CityId = Guid.NewGuid(), CountryId = panama.CountryId, CityName = "Guna Yala", CityDescription = "The autonomous comarca of the Guna people, San Blas archipelago.", Latitude = 9.2477, Longitude = -78.1827 },
//            new City { CityId = Guid.NewGuid(), CountryId = panama.CountryId, CityName = "Caribbean coast of Panama (Northwest)", CityDescription = "Bocas del Toro province on Panama's Caribbean coast.", Latitude = 9.3399, Longitude = -82.2521 }
//        );
//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 5. CATEGORIES
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedCategoriesAsync(HodracDbContext db)
//    {
//        if (await db.Categories.AnyAsync()) return;

//        db.Categories.AddRange(
//            new Category { CategoryId = Guid.NewGuid(), Key = "cultural_site", CategoryName = "Cultural Site", CategoryDescription = "Temples, shrines, mosques, and heritage sites.", IconName = "landmark", ColorHex = "#6366f1" },
//            new Category { CategoryId = Guid.NewGuid(), Key = "neighborhood_district", CategoryName = "Neighborhood & District", CategoryDescription = "Walkable urban areas with local character.", IconName = "map", ColorHex = "#8b5cf6" },
//            new Category { CategoryId = Guid.NewGuid(), Key = "viewpoint_scenic_spot", CategoryName = "Viewpoint & Scenic Spot", CategoryDescription = "Observation decks, hilltops, and panoramic vistas.", IconName = "eye", ColorHex = "#0891b2" },
//            new Category { CategoryId = Guid.NewGuid(), Key = "landmark_monument", CategoryName = "Landmark & Monument", CategoryDescription = "Iconic structures and historic monuments.", IconName = "building", ColorHex = "#f59e0b" },
//            new Category { CategoryId = Guid.NewGuid(), Key = "market_street_life", CategoryName = "Market & Street Life", CategoryDescription = "Food markets, street stalls, and bazaars.", IconName = "shopping-bag", ColorHex = "#10b981" },
//            new Category { CategoryId = Guid.NewGuid(), Key = "activity_experience", CategoryName = "Activity & Experience", CategoryDescription = "Theme parks, guided tours, and immersive activities.", IconName = "star", ColorHex = "#ec4899" },
//            new Category { CategoryId = Guid.NewGuid(), Key = "nature_outdoor", CategoryName = "Nature & Outdoors", CategoryDescription = "Beaches, parks, forests, and natural landscapes.", IconName = "tree", ColorHex = "#22c55e" },
//            new Category { CategoryId = Guid.NewGuid(), Key = "entertainment_nightlife", CategoryName = "Entertainment & Nightlife", CategoryDescription = "Bars, clubs, theaters, and live entertainment.", IconName = "music", ColorHex = "#7c3aed" },
//            new Category { CategoryId = Guid.NewGuid(), Key = "food_experience", CategoryName = "Food Experience", CategoryDescription = "Restaurants, cafes, and culinary destinations.", IconName = "utensils", ColorHex = "#f97316" }
//        );
//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 6. TAGS
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedTagsAsync(HodracDbContext db)
//    {
//        if (await db.Tags.AnyAsync()) return;

//        db.Tags.AddRange(
//            // Style
//            new Tag { TagId = Guid.NewGuid(), Key = "walkable", TagName = "Walkable", TargetPersonaType = "Explorer" },
//            new Tag { TagId = Guid.NewGuid(), Key = "cultural", TagName = "Cultural", TargetPersonaType = "Culture Seeker" },
//            new Tag { TagId = Guid.NewGuid(), Key = "history", TagName = "History", TargetPersonaType = "Culture Seeker" },
//            new Tag { TagId = Guid.NewGuid(), Key = "photography", TagName = "Photography", TargetPersonaType = "Photographer" },
//            new Tag { TagId = Guid.NewGuid(), Key = "architecture", TagName = "Architecture", TargetPersonaType = "Design Lover" },
//            new Tag { TagId = Guid.NewGuid(), Key = "nature", TagName = "Nature", TargetPersonaType = "Nature Lover" },
//            new Tag { TagId = Guid.NewGuid(), Key = "food_focused", TagName = "Food", TargetPersonaType = "Foodie" },
//            new Tag { TagId = Guid.NewGuid(), Key = "shopping", TagName = "Shopping", TargetPersonaType = "Shopper" },
//            new Tag { TagId = Guid.NewGuid(), Key = "nightlife", TagName = "Nightlife", TargetPersonaType = "Night Owl" },
//            new Tag { TagId = Guid.NewGuid(), Key = "educational", TagName = "Educational", TargetPersonaType = "Learner" },
//            // Vibe
//            new Tag { TagId = Guid.NewGuid(), Key = "relaxing", TagName = "Relaxation", TargetPersonaType = "Wellness Seeker" },
//            new Tag { TagId = Guid.NewGuid(), Key = "adventurous", TagName = "Adventure", TargetPersonaType = "Adventurer" },
//            new Tag { TagId = Guid.NewGuid(), Key = "romantic", TagName = "Romance", TargetPersonaType = "Couple" },
//            new Tag { TagId = Guid.NewGuid(), Key = "social", TagName = "Social", TargetPersonaType = "Social Traveler" },
//            // Audience
//            new Tag { TagId = Guid.NewGuid(), Key = "family_friendly", TagName = "Family Friendly", TargetPersonaType = "Family" },
//            new Tag { TagId = Guid.NewGuid(), Key = "couple_friendly", TagName = "Couple Friendly", TargetPersonaType = "Couple" },
//            new Tag { TagId = Guid.NewGuid(), Key = "solo_friendly", TagName = "Solo Friendly", TargetPersonaType = "Solo Traveler" },
//            new Tag { TagId = Guid.NewGuid(), Key = "group_friendly", TagName = "Group Friendly", TargetPersonaType = "Group" },
//            // Crowd / access
//            new Tag { TagId = Guid.NewGuid(), Key = "tourist_hotspot", TagName = "Tourist Hotspot", TargetPersonaType = "First-Timer" },
//            new Tag { TagId = Guid.NewGuid(), Key = "local_favorite", TagName = "Local Favorite", TargetPersonaType = "Authentic Seeker" },
//            new Tag { TagId = Guid.NewGuid(), Key = "hidden_gem", TagName = "Hidden Gem", TargetPersonaType = "Explorer" },
//            new Tag { TagId = Guid.NewGuid(), Key = "crowded", TagName = "Crowded", TargetPersonaType = "First-Timer" },
//            new Tag { TagId = Guid.NewGuid(), Key = "budget_friendly", TagName = "Budget", TargetPersonaType = "Budget Traveler" },
//            new Tag { TagId = Guid.NewGuid(), Key = "premium", TagName = "Luxury", TargetPersonaType = "Luxury Traveler" },
//            // Timing
//            new Tag { TagId = Guid.NewGuid(), Key = "best_morning", TagName = "Best in Morning", TargetPersonaType = "Early Bird" },
//            new Tag { TagId = Guid.NewGuid(), Key = "best_at_night", TagName = "Best at Night", TargetPersonaType = "Night Owl" },
//            new Tag { TagId = Guid.NewGuid(), Key = "best_evening", TagName = "Best at Evening", TargetPersonaType = "Explorer" },
//            new Tag { TagId = Guid.NewGuid(), Key = "good_short_visit", TagName = "Good Short Visit", TargetPersonaType = "Busy Traveler" },
//            new Tag { TagId = Guid.NewGuid(), Key = "seasonal", TagName = "Seasonal", TargetPersonaType = "Culture Seeker" }
//        );
//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 7. DESTINATIONS
//    // Migrates all 29 destinations from the old seeder.
//    // Schema changes handled:
//    //   - AverageCostPerDay = (MinCost + MaxCost) / 2
//    //   - SafetyLevel: old 1-10 → new 1-5 by mapping
//    //   - LuxuryRating: derived from cost and premium tags
//    //   - DescriptionJson: serialized from DescriptionJsonDto
//    //   - PsychographicVibeTagsJson: derived from tag keys
//    //   - FamilyFriendlyScore / AdventurePaceScore: derived from tags
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedDestinationsAsync(HodracDbContext db)
//    {
//        if (await db.Destinations.AnyAsync()) return;

//        // ── Lookups ───────────────────────────────────────────────────────────
//        var japan = await db.Countries.FirstAsync(c => c.CountryName == "Japan");
//        var panama = await db.Countries.FirstAsync(c => c.CountryName == "Panama");

//        var tokyo = await db.Cities.FirstAsync(c => c.CityName == "Tokyo");
//        var panamaCity = await db.Cities.FirstAsync(c => c.CityName == "Panama City");
//        var gunaYala = await db.Cities.FirstAsync(c => c.CityName == "Guna Yala");
//        var bocas = await db.Cities.FirstAsync(c => c.CityName.StartsWith("Caribbean coast"));

//        var japanese = await db.Languages.FirstAsync(l => l.LanguageName == "Japanese");
//        var spanish = await db.Languages.FirstAsync(l => l.LanguageName == "Spanish");
//        var english = await db.Languages.FirstAsync(l => l.LanguageName == "English");
//        var guna = await db.Languages.FirstAsync(l => l.LanguageName == "Dulegaya (Guna/Kuna)");

//        var jpy = await db.Currencies.FirstAsync(c => c.CurrencyCode == "JPY");
//        var usd = await db.Currencies.FirstAsync(c => c.CurrencyCode == "USD");
//        var pab = await db.Currencies.FirstAsync(c => c.CurrencyCode == "PAB");

//        // Category map
//        var cats = await db.Categories.ToDictionaryAsync(c => c.Key);
//        // Tag map
//        var tags = await db.Tags.ToDictionaryAsync(t => t.Key);

//        // ── Helper methods ────────────────────────────────────────────────────

//        // Convert old 1-10 safety (10 = safest) to new 1-5 (1 = safest)
//        static int MapSafety(int old10)
//            => old10 >= 9 ? 1 : old10 >= 7 ? 2 : old10 >= 5 ? 3 : old10 >= 3 ? 4 : 5;

//        // Derive luxury rating 1-5 from average cost and tag keys
//        static int DeriveLuxury(decimal avgCost, bool hasPremiumTag)
//        {
//            if (hasPremiumTag || avgCost > 200) return 5;
//            if (avgCost > 100) return 4;
//            if (avgCost > 40) return 3;
//            if (avgCost > 10) return 2;
//            return 1;
//        }

//        // Score helpers
//        static int FamilyScore(IEnumerable<string> tagKeys)
//        {
//            var k = tagKeys.ToHashSet();
//            int s = 1;
//            if (k.Contains("family_friendly")) s += 2;
//            if (k.Contains("walkable")) s += 1;
//            if (k.Contains("educational")) s += 1;
//            if (k.Contains("adventurous")) s -= 1;
//            if (k.Contains("nightlife")) s -= 1;
//            return Math.Clamp(s, 1, 5);
//        }

//        static int AdventureScore(IEnumerable<string> tagKeys)
//        {
//            var k = tagKeys.ToHashSet();
//            int s = 1;
//            if (k.Contains("adventurous")) s += 2;
//            if (k.Contains("nature")) s += 1;
//            if (k.Contains("hidden_gem")) s += 1;
//            if (k.Contains("relaxing")) s -= 1;
//            if (k.Contains("tourist_hotspot")) s -= 1;
//            return Math.Clamp(s, 1, 5);
//        }

//        Destination Make(
//            string name, string image, DescriptionJsonDto desc,
//            decimal minCost, decimal maxCost, int oldSafety,
//            Country country, List<City> cities,
//            List<Language> langs, List<Currency> currencies,
//            List<Category> categories, List<string> tagKeys,
//            string timeZone)
//        {
//            var avg = (minCost + maxCost) / 2m;
//            var hasPremium = tagKeys.Contains("premium");
//            var vibes = DeriveVibes(tagKeys);
//            var slug = name.ToLowerInvariant().Replace(" ", "_").Replace("(", "").Replace(")", "").Replace("'", "").Replace(",", "");

//            var dest = new Destination
//            {
//                DestinationId = Guid.NewGuid(),
//                DestinationName = name,
//                CleanNormalizedSearchName = name.ToLowerInvariant(),
//                DescriptionJson = JsonSerializer.Serialize(desc),
//                AverageCostPerDay = avg,
//                SafetyLevel = MapSafety(oldSafety),
//                LuxuryRating = DeriveLuxury(avg, hasPremium),
//                FamilyFriendlyScore = FamilyScore(tagKeys),
//                AdventurePaceScore = AdventureScore(tagKeys),
//                AestheticTrendScore = tagKeys.Contains("photography") || tagKeys.Contains("architecture") ? 4 : 2,
//                PsychographicVibeTagsJson = JsonSerializer.Serialize(vibes),
//                TimeZone = timeZone,
//                CountryId = country.CountryId,
//                SearchHitCount = 0,
//                AccessibilityType = "Train",
//                Latitude = country.CountryName == "Japan" ? 35.6762 : 8.9936,
//                Longitude = country.CountryName == "Japan" ? 139.6503 : -79.5197,
//            };

//            // Images
//            dest.Images = new List<DestinationImage>
//            {
//                new()
//                {
//                    DestinationImageId = Guid.NewGuid(),
//                    DestinationId      = dest.DestinationId,
//                    ImageUrl           = image,
//                    ThumbnailUrl       = image,
//                    Caption            = name,
//                    DisplayOrder       = 0,
//                    ImageType          = "Hero",
//                    IsAiGenerated      = false,
//                }
//            };

//            // Cities
//            dest.DestinationCities = cities.Select(c => new DestinationCity
//            {
//                DestinationId = dest.DestinationId,
//                CityId = c.CityId,
//            }).ToList();

//            // Languages
//            dest.DestinationLanguages = langs.Select(l => new DestinationLanguage
//            {
//                DestinationId = dest.DestinationId,
//                LanguageId = l.LanguageId,
//            }).ToList();

//            // Currencies
//            dest.DestinationCurrencies = currencies.Select(c => new DestinationCurrency
//            {
//                DestinationId = dest.DestinationId,
//                CurrencyId = c.CurrencyId,
//            }).ToList();

//            // Categories
//            dest.DestinationCategories = categories.Select(c => new DestinationCategory
//            {
//                DestinationId = dest.DestinationId,
//                CategoryId = c.CategoryId,
//            }).ToList();

//            // Tags
//            dest.DestinationTags = tagKeys
//                .Where(k => tags.ContainsKey(k))
//                .Select(k => new DestinationTag
//                {
//                    DestinationId = dest.DestinationId,
//                    TagId = tags[k].TagId,
//                }).ToList();

//            return dest;
//        }

//        // ── Tokyo destinations ────────────────────────────────────────────────

//        var tokyoLangs = new List<Language> { japanese };
//        var tokyoCities = new List<City> { tokyo };
//        var tokyoCurrs = new List<Currency> { jpy };

//        var destinations = new List<Destination>
//        {
//            Make("Meiji Shrine",        "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/meiji.jpg",
//                BuildDesc("Meiji Shrine is a peaceful Shinto shrine in central Tokyo, surrounded by a 70-hectare forested area. Dedicated to Emperor Meiji and Empress Shoken, it offers a rare moment of calm amid the city's energy.",
//                "The shrine is deeply meaningful to Tokyo residents — most visit not for tourism but for personal prayer, new year ceremonies, and traditional Shinto weddings.",
//                "Best access via Harajuku Station (JR Yamanote Line). Exit Omotesando exit, cross Jingu-bashi bridge, enter through the large wooden torii gate. 10-minute walk to main shrine along shaded forest path.",
//                "Free for main grounds. Inner Garden: 500 yen. Museum: 1,000 yen. Bring 5-yen coins for offerings. Photography forbidden inside main sanctuary.",
//                "Beware of the New Year rush (3M+ visitors Jan 1-3). Thick gravel paths are hard on thin-soled shoes and difficult for strollers.",
//                "Main grounds free; Inner Garden 500 yen (~$3.33); Museum 1,000 yen (~$6.67); Amulets 500-1,500 yen.",
//                new[] { "Yoyogi Park", "Harajuku (Takeshita Street)", "Omotesando", "Shibuya Crossing" },
//                "Spring festival late April–May; mid-June for iris blooms; sunrise for crowds-free experience.",
//                "High (8/10) — avoid Jan 1-3.",
//                "8/10 — wide flat paths but thick gravel is challenging for wheelchairs.",
//                "1.5 to 2.5 hours"),
//                0, 4, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["cultural_site"] },
//                new List<string> { "cultural", "walkable", "history", "budget_friendly", "photography" },
//                "Japan Standard Time"),

//            Make("Takeshita Street",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/takeshita.jpg",
//                BuildDesc("The global epicenter of Kawaii culture — a 350-meter pedestrian shopping street in Harajuku packed with youth fashion boutiques, themed cafes, and street food.",
//                "For Japanese youth this is a space of self-expression, not a tourist attraction. The outfits here are real subcultures, not costumes.",
//                "JR Harajuku Station, Takeshita Exit. Cross the street at the designated crosswalk to the arched gate directly ahead.",
//                "Most snacks cost 600-1,000 yen. Cash-only at many small stalls. No public trash cans — finish snacks at the stall.",
//                "Extremely crowded on weekends. Avoid if claustrophobic. Some animal cafes have questionable ethical standards.",
//                "Gachapon machines 300-500 yen each; giant cotton candy 900+ yen; Purikura photo booths 400-500 yen.",
//                new[] { "Meiji Jingu", "Cat Street", "Daiso Harajuku" },
//                "Weekday mornings (11 AM) for manageable crowds; late afternoon for best photos.",
//                "Very High (10/10) on weekends.",
//                "7/10 — flat but intense crowds make wheelchair navigation stressful.",
//                "1 to 2 hours"),
//                5, 25, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["neighborhood_district"] },
//                new List<string> { "cultural", "walkable", "shopping", "budget_friendly", "tourist_hotspot", "crowded" },
//                "Japan Standard Time"),

//            Make("Cat Street",          "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/cat_street.jpg",
//                BuildDesc("A winding pedestrian lane built over the old Shibuya River bed, connecting Harajuku to Shibuya. Lined with high-end boutiques, vintage shops, and artisan cafes — the sophisticated counterpart to Takeshita's chaos.",
//                "Cat Street is where fashion-forward locals come to refine their taste. No hard sell, no neon — just understated cool.",
//                "From Harajuku: exit Takeshita Exit, cross Route 305, walk straight — Cat Street is the fifth street on the right.",
//                "A basic crepe is affordable but customization pushes to 800-1,000 yen. Lobster rolls near 1,958 yen.",
//                "Many boutiques don't open until 11 AM. Higher-end pricing throughout.",
//                "Gourmet premium for food; designer vintage can still cost hundreds; The Trunk Hotel bar ~1 cocktail = small meal elsewhere.",
//                new[] { "Meiji Jingu Shrine", "Yoyogi Park", "Miyashita Park", "Omotesando" },
//                "Spring/Autumn for outdoor walk; weekday afternoons for relaxed browsing; golden hour for photos.",
//                "Moderate (5/10).",
//                "9/10 — mostly pedestrian promenade, easy to navigate.",
//                "1 to 3 hours"),
//                10, 100, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["neighborhood_district"] },
//                new List<string> { "walkable", "shopping", "premium", "tourist_hotspot" },
//                "Japan Standard Time"),

//            Make("Shibuya Crossing",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/shibuya_crossing.jpg",
//                BuildDesc("The world's busiest pedestrian intersection, where five roads meet and up to 3,000 people cross simultaneously in the famous 'scramble.' An iconic symbol of Tokyo's organized chaos.",
//                "For Tokyoites this is just their daily commute — stay aware of the flow and don't stop in the center for selfies.",
//                "Shibuya Station Hachiko Exit — the crossing is immediately in front of you.",
//                "Crossing is free. Shibuya Sky deck 2,500-3,500 yen; Magnet rooftop view ~1,800 yen including one drink.",
//                "Don't stop in the middle for photos. Avoid pickpockets during extreme rush hour density.",
//                "Shibuya Sky observation deck; Starbucks QFRONT window view (~600 yen drink); 15-minute wait for Hachiko statue photo.",
//                new[] { "Shibuya 109", "Miyashita Park", "Dogenzaka", "Shibuya Hikarie" },
//                "Friday night 7PM for maximum energy; rainy evenings for umbrella-ocean photography; Sunday 8AM for eerily empty crossing.",
//                "Extreme (10/10).",
//                "9/10 — smooth level pavement with curb cuts; overwhelming for sensory sensitivities during rush hour.",
//                "30 to 60 minutes"),
//                0, 5, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["landmark_monument"] },
//                new List<string> { "walkable", "crowded", "tourist_hotspot", "photography" },
//                "Japan Standard Time"),

//            Make("Shibuya Sky",         "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/shibuya_sky.webp",
//                BuildDesc("An open-air 229-meter rooftop observation deck atop Shibuya Scramble Square. Offers 360-degree panoramic views of Tokyo including Skytree, Tokyo Tower, Imperial Palace, and on clear days, Mt. Fuji.",
//                "Locals book exactly 30 minutes before sunset — daylight to blue hour to neon lights flickering on. Visit in winter for Mt. Fuji sightings.",
//                "Shibuya Station East Exit → Shibuya Scramble Square → ticket counter on 14th floor.",
//                "~2,700 yen online (cheaper) or 3,000 yen at counter. After 3PM: 3,400 yen. Mandatory 100-yen locker for bags.",
//                "No bags, tripods, or loose items on the roof. Closes in high winds. Book sunset slots weeks ahead.",
//                "100-yen locker (refundable); professional photo print ~1,500 yen; rooftop bar cocktails 800-1,200 yen.",
//                new[] { "Paradise Lounge (46F bar)", "Shibuya Scramble Square shops", "Hachiko Statue" },
//                "Sunset slot (book online); 10AM opening for short queues; 9PM for romantic quiet atmosphere.",
//                "High (9/10).",
//                "10/10 — fully wheelchair and stroller accessible.",
//                "90 minutes"),
//                14, 18, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["viewpoint_scenic_spot"] },
//                new List<string> { "architecture", "photography", "tourist_hotspot" },
//                "Japan Standard Time"),

//            Make("Senso-ji Temple (Asakusa Kannon)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/sensoji.jpg",
//                BuildDesc("Tokyo's oldest temple, founded in 645 AD. A vibrant spiritual landmark anchored by the famous Kaminarimon thunder gate and 250-meter Nakamise shopping street, offering an immersive Edo-period atmosphere.",
//                "The original Kannon statue is never shown — not even to monks. For locals this is a living place of prayer, not just a landmark.",
//                "Asakusa Station Exit 1 or 3, 2-minute walk to Kaminarimon Gate.",
//                "Entry free. Omamori amulets 500-1,500 yen; omikuji fortunes 100-200 yen; Goshuin temple stamps 300-500 yen.",
//                "11AM-3PM crowds are overwhelming. Bad luck fortunes are common here — it's just tradition.",
//                "Rickshas 4,000-9,000 yen for 30 min; candle offerings a few hundred yen; Nakamise snacks 150-500 yen.",
//                new[] { "Asakusa Culture Tourist Info Center (free 8F view)", "Hoppy Street", "Samurai Ninja Museum Tokyo" },
//                "8AM for quiet and beautiful shutter art on Nakamise; 7PM for illuminated temple with few crowds.",
//                "Extreme (10/10) weekends; Moderate (4/10) weekday early morning.",
//                "8/10 — mostly flat with elevator access to altar area.",
//                "1.5 to 2 hours"),
//                0, 15, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["cultural_site"] },
//                new List<string> { "walkable", "crowded", "tourist_hotspot", "cultural", "shopping", "photography" },
//                "Japan Standard Time"),

//            Make("Kaminarimon Gate",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/kaminarimon.jpg",
//                BuildDesc("The iconic 700kg vermilion lantern gate guarding Senso-ji Temple. One of Tokyo's most photographed landmarks, featuring Shinto gods guarding the front and Buddhist deities at the rear.",
//                "During Sanja Matsuri festival in May, the lantern is collapsed so that massive portable shrines (mikoshi) can pass underneath.",
//                "Asakusa Station, 2-minute walk from any exit. Gate is directly at the entrance to Senso-ji temple complex.",
//                "Passing through is free. First omikuji stalls just past the gate: 100 yen.",
//                "Extremely congested during peak daylight. Rickshaw pullers base here — polite refusal works fine.",
//                "Fortune telling 100 yen; snack temptation from first Nakamise shops immediately after gate.",
//                new[] { "Nakamise-dori", "Senso-ji Temple", "Asakusa Culture Tourist Info Center", "Kamiya Bar" },
//                "7AM for no tourists in frame; sunset for deep crimson glow; late night for illuminated quiet.",
//                "High (10/10) during the day.",
//                "9/10 — flat paved area, easily accessible.",
//                "15 minutes"),
//                0, 1, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["landmark_monument"] },
//                new List<string> { "walkable", "crowded", "tourist_hotspot", "photography", "cultural" },
//                "Japan Standard Time"),

//            Make("Asakusa Streets",     "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/asakusa.jpg",
//                BuildDesc("The heart of Tokyo's Shitamachi (old downtown), where Edo-period atmosphere lingers in the narrow back alleys, incense smoke from Senso-ji, and traditional craft shops. A maze of discovery beyond the main temple.",
//                "Before shops open, the Nakamise shutters are painted with scenes of Japanese history — locals call this the Shutter Art hour. Best at 7:30AM.",
//                "Asakusa Station (Ginza Line, Asakusa Line, Tobu Railway). Most attractions 3-7 min walk from exits.",
//                "Street snacks 150-500 yen. Don Quijote has multi-story options. Cash for most stalls.",
//                "Most Nakamise shops close by 5-6PM. Carry trash until hotel — public bins nearly nonexistent.",
//                "Rickshaw tours ~9,000 yen for 2 people/30 min; Hanayashiki amusement park 1,200 yen entry; Asahi Sky Room beer 800-1,200 yen.",
//                new[] { "Kappabashi Street (kitchen gear)", "Sumida Park", "Tokyo Skytree" },
//                "7:30AM for Shutter Art; late July for Sumida River Fireworks; sunset when Skytree reflects off river.",
//                "High (9/10) especially midday weekends.",
//                "9/10 — very flat with elevator access throughout.",
//                "4 to 6 hours"),
//                10, 30, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["market_street_life"] },
//                new List<string> { "walkable", "crowded", "tourist_hotspot", "shopping", "food_focused" },
//                "Japan Standard Time"),

//            Make("Sumida Riverwalk",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/riverwalk.jpg",
//                BuildDesc("A scenic riverside promenade along the Sumida River, linking ancient Asakusa temples to the futuristic Tokyo Skytree. Features a glass-floored pedestrian bridge, waterfront cafes, and 16 uniquely colored bridges.",
//                "Locals know the Asahi Flame atop Asahi Super Dry Hall as the 'Golden Turd.' The riverside quiet is where Tokyoites decompress from city noise.",
//                "Asakusa Station Exit 5 (7-min walk) or TOBU SKYTREE Line North Exit (3-min walk). Walk toward Azuma Bridge for riverside access.",
//                "Walk is free. Water bus to Odaiba ~2,000 yen; riverside cafes premium; yakatabune dinner cruise 10,000-15,000 yen per person.",
//                "Very little shade — avoid midday heat. Bridge distances are 5-10 min apart; plan energy for return.",
//                "Riverside dining premium; Sumo tournament tickets sell out months in advance; mizumachi bouldering/hosting fees separate.",
//                new[] { "Tokyo Skytree Town", "Senso-ji Temple", "Ryogoku Kokugikan (sumo)", "Sumida Park" },
//                "Sunset for synchronized Skytree lighting; late March/early April for cherry blossoms; last Saturday of July for fireworks.",
//                "Low to Moderate (3/10) weekday mornings; Extreme (10/10) during cherry blossom season.",
//                "9/10 — paved flat paths and wheelchair-friendly bridge.",
//                "45 minutes to 2 hours"),
//                0, 10, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["activity_experience"] },
//                new List<string> { "walkable", "romantic", "photography", "couple_friendly" },
//                "Japan Standard Time"),

//            Make("Shinjuku Gyoen",      "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/gyoen.jpg",
//                BuildDesc("A 144-acre national garden featuring three distinct styles: French Formal, English Landscape, and Japanese Traditional. Famous for 1,000+ cherry trees and the longest sakura season in Tokyo due to early and late-blooming varieties.",
//                "No alcohol allowed — making it the locals' preferred hanami spot for families who want peaceful flower viewing without rowdy parties.",
//                "Shinjuku Gate: 10-min walk from JR Shinjuku Station New South Exit, or 5-min from Shinjukugyoenmae Station.",
//                "500 yen adults; free for under-15. Annual passport 2,000 yen. Teahouse matcha 700-1,000 yen.",
//                "Alcohol strictly prohibited. Last entry 30 min before closing. Weekend sakura requires advance online booking.",
//                "Annual passport pays off in 4 visits; teahouse set 700-1,000 yen; garden shop slightly above convenience store prices.",
//                new[] { "Shinjuku San-chome", "Tokyo Metropolitan Government Building", "Meiji Jingu" },
//                "9AM opening for quiet Japanese Garden; early November for autumn maple colors; late March for cherry blossoms.",
//                "Moderate (5/10) weekdays; High (9/10) blossom season.",
//                "10/10 — extremely flat and paved, accessible restrooms throughout.",
//                "2 to 3 hours"),
//                0, 5, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["nature_outdoor"] },
//                new List<string> { "walkable", "photography", "nature", "history" },
//                "Japan Standard Time"),

//            Make("Tokyo Metropolitan Government Building", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tokyo_metropolitan.jpg",
//                BuildDesc("A free 202-meter observation deck on the 45th floor of Kenzo Tange's postmodern masterpiece. Offers 360-degree city views including Mt. Fuji on clear days, and features evening projection mapping on the building exterior.",
//                "The staff canteen on the 32nd floor serves cheap high-quality food to office workers — locals eat here for 600-800 yen set meals.",
//                "Tocho-mae Station (Oedo Line) in the building basement, or 10-min walk from JR Shinjuku Station West Exit.",
//                "Free admission. Observatory cafe coffee/beer 600-900 yen. Staff canteen lunch 600-800 yen.",
//                "Queue can be 30-45 min on weekends. Glass glare at night requires lens against window. Check which tower is open before visiting.",
//                "Observatory souvenir shop worth browsing; cafe window seat ~600-900 yen; gift shop has limited-edition Tokyo items.",
//                new[] { "Shinjuku Central Park", "Park Hyatt Tokyo", "Omoide Yokocho (Piss Alley)", "Meiji Shrine" },
//                "9:30AM winter for Mt. Fuji; 4:30PM for blue hour city lights; 7:30PM for exterior projection mapping show.",
//                "Moderate (6/10) weekdays; High (9/10) weekends and clear sunsets.",
//                "10/10 — fully ADA compliant with dedicated elevators and wheelchair loans.",
//                "1 to 1.5 hours"),
//                0, 5, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["viewpoint_scenic_spot"] },
//                new List<string> { "photography", "budget_friendly", "architecture" },
//                "Japan Standard Time"),

//            Make("Shinjuku Golden Gai", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/golden_gai.jpg",
//                BuildDesc("Six narrow alleys housing 200+ tiny themed bars, some fitting only five customers. A living relic of 1950s Tokyo that survived developers and arsonists, now beloved by writers, directors, musicians, and curious travelers.",
//                "In the 1980s locals took turns physically guarding these alleys from developers. There is immense community pride in its survival.",
//                "JR Shinjuku Station East Exit, 5-10 min walk. Located between Shinjuku City Office and Hanazono Shrine.",
//                "Cover charges 500-1,000 yen per bar; single cocktails up to 1,500 yen. Most bars don't open until 9-10PM.",
//                "Regulars-only bars exist — closed doors mean closed. Steep narrow stairs. Very cramped spaces.",
//                "Cover charges 500-1,000 yen per bar; tourist-friendly bars may have higher drink prices; late-night taxi adds 20% surcharge.",
//                new[] { "Hanazono Shrine", "Omoide Yokocho", "Kabukicho", "Thermae-Yu (24hr onsen)" },
//                "Weeknights Mon-Thu for fewer tourists; 10PM-1AM for peak energy.",
//                "Extreme (9/10) Friday and Saturday nights.",
//                "2/10 — extremely narrow uneven alleys, steep ladder-like stairs, not wheelchair accessible.",
//                "2 to 4 hours"),
//                15, 45, 9, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["neighborhood_district"] },
//                new List<string> { "nightlife", "best_at_night", "group_friendly", "social" },
//                "Japan Standard Time"),

//            Make("Kabukicho",           "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/kabukicho.jpg",
//                BuildDesc("Japan's largest entertainment district — a hybrid of world-class attractions (Godzilla, namco TOKYO, Sword Art Online Quest) and neon-drenched nightlife bars. Recently transformed by the Tokyu Kabukicho Tower development.",
//                "During the day Kabukicho is actually family-friendly and great for photography. The vibe shifts dramatically after 9PM.",
//                "JR Shinjuku Station East Exit → find Studio Alta screen → cross Yasukuni-dori → look for Don Quijote → Kabukicho Ichibangai red neon arch.",
//                "Godzilla view free; namco TOKYO arcade tokens from 200 yen; dinner 1,500-3,000 yen; clubs 2,000-5,000 yen entry.",
//                "Never enter bars recommended by strangers — bottakuri (rip-off) scams exist. Stick to well-reviewed spots.",
//                "Table charges (otoshi) 500-1,000 yen per bar; midnight taxi 20% surcharge; premium experience costs stack quickly.",
//                new[] { "Golden Gai", "Omoide Yokocho (Piss Alley)", "Thermae-Yu (24hr onsen)" },
//                "8PM-11PM for neon at peak brightness without extreme crowds.",
//                "High (9/10) Friday and Saturday nights.",
//                "7/10 — main streets flat and paved; many bars have steep narrow stairs.",
//                "3 to 5 hours"),
//                5, 100, 7, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["entertainment_nightlife"] },
//                new List<string> { "nightlife", "best_at_night", "social" },
//                "Japan Standard Time"),

//            Make("Tsukiji Outer Market","https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tsukiji.jpg",
//                BuildDesc("Tokyo's surviving seafood and food culture hub — 400+ shops selling fresh fish, professional knives, and street food. Even after the wholesale market moved to Toyosu in 2018, this remains the city's most vibrant morning food district.",
//                "Most shops close by 1-2PM. By 3PM the market is almost entirely shut. This is an early-morning destination only.",
//                "Tsukiji Shijo Station (Oedo Line) or Tsukiji Station (Hibiya Line), short walk from either.",
//                "Sushi breakfast 1,000-3,000 yen; ceviche-style cups 200-600 yen; premium tuna cuts priced professionally.",
//                "Walking and eating (tabearuki) is frowned upon — finish snacks at the stall. 2-hour sushi queues are real.",
//                "Tax-free shopping for tourists with passport; top-floor food court at Tsukiji Uogashi has shorter waits for same quality.",
//                new[] { "Ginza", "Hamarikyu Gardens", "Kabuki-za Theatre" },
//                "8AM-10AM for all shops open without peak lunch crowds.",
//                "High (10/10) — extremely tight aisles.",
//                "6/10 — main areas accessible but narrow alleys difficult for wheelchairs.",
//                "2 to 3 hours"),
//                5, 60, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["market_street_life"] },
//                new List<string> { "crowded", "tourist_hotspot", "food_focused", "walkable" },
//                "Japan Standard Time"),

//            Make("teamLab Borderless: Azabudai Hills", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/teamlab.jpg",
//                BuildDesc("A 10,000 sqm digital art museum without a map — 75+ interconnected installations where art crosses between rooms, dragons fly down hallways, and flowers bloom at your feet. Reopened at Azabudai Hills in 2024.",
//                "Locals wear white or light-colored clothing so the projected art becomes part of their outfit. Visit in April for digital cherry blossom rooms.",
//                "Kamiyacho Station (Hibiya Line) Exit 5, 2-minute walk. Within Azabudai Hills complex.",
//                "Weekday ~3,200 yen; weekend/holiday higher and sells out weeks ahead. EN Tea House extra 600-1,100 yen.",
//                "Light Vortex room has intense flashing — avoid if photosensitive. No bags on floor; mandatory lockers.",
//                "EN TEA HOUSE not included; lockers 100 yen (refundable); battery pack essential as photos drain phone fast.",
//                new[] { "Tokyo Tower (10-min walk)", "Mori Art Museum (Roppongi Hills)", "Janu Tokyo (afternoon tea)" },
//                "9AM opening or after 6PM to avoid school groups; wear light colors.",
//                "High (8/10).",
//                "8/10 — mostly accessible with barrier-free routes; some terrain areas restricted.",
//                "3 to 4 hours"),
//                10, 30, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["viewpoint_scenic_spot"] },
//                new List<string> { "architecture", "photography", "tourist_hotspot" },
//                "Japan Standard Time"),

//            Make("Tokyo Disney",        "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/disney_tokyo.jpg",
//                BuildDesc("The world's most visited Disney resort — Tokyo Disneyland and Tokyo DisneySea. In 2024-2026 celebrating DisneySea's 25th anniversary 'Sparkling Jubilee' with the massive Fantasy Springs expansion (Frozen, Tangled, Peter Pan lands).",
//                "The Yen at 160/$1 makes this one of the best-value Disney experiences globally in 2026. Rope drop means gates open at 8:15AM even if the official time is 9AM.",
//                "JR Maihama Station (JR Keiyo Line from Tokyo Station, ~15 min). Disney Resort Line monorail connects to parks.",
//                "Tickets 7,900-10,900 yen depending on tier. Disney Premier Access line-skipping ~$9-15 per ride.",
//                "Golden Week (April 29-May 5) means max crowds and peak pricing. Tomorrowland under construction for new Space Mountain.",
//                "DPA passes 1,350-2,250 yen per ride; dining premium; late-night return trains are packed.",
//                new[] { "Kasai Rinkai Park (aquarium)", "Resort hotels buffet breakfast", "Ikspiari shopping" },
//                "Tuesdays-Thursdays outside Golden Week; mid-May onwards for 2026.",
//                "10/10 Golden Week; 7/10 standard weekday.",
//                "9/10 — excellent flat surfaces, fully accessible monorail.",
//                "3 to 4 days full resort"),
//                49, 68, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["activity_experience"] },
//                new List<string> { "family_friendly", "premium", "tourist_hotspot", "crowded" },
//                "Japan Standard Time"),

//            Make("Akihabara",           "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/akihabara.jpg",
//                BuildDesc("Tokyo's Electric Town — global center of otaku culture, anime merchandise, retro gaming, maid cafes, and electronics. Evolved from post-WWII radio parts market to multi-floor shrines dedicated to every anime fandom.",
//                "On Sundays 1-5PM Chuo Dori closes to cars — best time for photography and a true Akihabara experience. Side alley stalls have better prices on used figures than main road shops.",
//                "Akihabara Station (JR Yamanote from Tokyo Station — 3 minutes, 160 yen).",
//                "Tax-free shopping with passport at most major stores. Retro games and figurines significantly cheaper with Yen at 160/$1.",
//                "Maid cafe cover charges are mandatory (600-1,000 yen). Street maid cafe promoters sometimes hide fees — stick to known chains.",
//                "Maid cafe cover + drink minimum; UFO catcher crane games easy to lose $20-30; AKB48 Theatre tickets 2,400-3,400 yen.",
//                new[] { "Kanda Myojin Shrine", "mAAch ecute Kanda Manseibashi", "Ochanomizu (music shops)" },
//                "Sundays 1-5PM for car-free streets; weeknight evenings for best neon without weekend crowds.",
//                "High (9/10) weekends; Moderate (6/10) weekday mornings.",
//                "8/10 — modern buildings with elevators but Radio Centre stalls very narrow.",
//                "4 to 6 hours"),
//                5, 100, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
//                new List<Category> { cats["neighborhood_district"] },
//                new List<string> { "cultural", "shopping", "tourist_hotspot" },
//                "Japan Standard Time"),

//            // ── Panama destinations ───────────────────────────────────────────

//            Make("Casco Viejo",         "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/casco.jpg",
//                BuildDesc("Panama's historic walled quarter — a 40-acre peninsula where 17th-century fortifications, neoclassical buildings, and colonial ruins coexist. A UNESCO World Heritage site and the city's most visited neighborhood.",
//                "Casco Cat Community — locals quietly maintain the neighborhood's street cats, considered its unofficial guardians. Look for genuine Kuna Molas beyond the souvenir shops.",
//                "Compact 40-acre district minutes from central Panama City hotels. Best explored on foot via self-guided walking tour.",
//                "Plan 3+ hours for main sights; full day including lunch and museums. Walking is essential for the full experience.",
//                "Night safety: stick to main streets near plazas after dark. Higher pricing than rest of Panama City. Cobblestones can be challenging.",
//                "Daily $63 (budget) to $189+ (mid-range); meals $7-14; Geisha coffee and Kuna Molas are premium purchases.",
//                new[] { "Mercado de Mariscos", "Cinta Costera", "Ancon Hill" },
//                "Late afternoon ~4PM to explore before heat breaks and rooftop nightlife begins; year-round.",
//                "High (8/10) — most visited neighborhood in Panama.",
//                "8/10 — 40 acres walkable but cobblestones challenging for wheelchairs/strollers.",
//                "3+ hours; full day preferred"),
//                65, 190, 7, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["neighborhood_district"] },
//                new List<string> { "walkable", "history", "cultural" },
//                "Eastern Standard Time"),

//            Make("Panama Canal Miraflores Locks", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/panamacanal.jpg",
//                BuildDesc("Panama's most visited landmark — a 4-floor visitor center with IMAX theater, museum, and outdoor viewing decks overlooking the original and expanded canal locks. Watch 100,000-ton vessels lifted by gravity and water alone.",
//                "The name 'Miraflores' means 'Behold the flowers.' Ship transits follow windows: 8-9AM (Atlantic direction) and starting 2PM (return). Arriving at noon means watching an empty concrete bathtub.",
//                "20-30 min drive from Panama City. Use Waze over Google Maps. Large parking lot available.",
//                "Admission: international adults $17.22; residents $3.00. 45-min IMAX film included. Arrive at 8AM for morning transit window and to beat heat.",
//                "Heat on outdoor platforms. No guarantee of ship passage during your slot. Crowds of 450 fill the deck fast when Neo-Panamax ships transit.",
//                "International vs local pricing gap significant; parking fills during peak tour coach times.",
//                new[] { "Metropolitan Natural Park", "Biomuseo", "Agua Clara Locks (Atlantic side)" },
//                "8AM for morning transit window and cooler temperatures.",
//                "High (9/10) — Panama's most visited landmark.",
//                "9/10 — ramps, elevators to all four floors, dedicated wheelchair platform.",
//                "2 to 3 hours"),
//                150, 170, 7, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["landmark_monument"] },
//                new List<string> { "tourist_hotspot", "educational", "family_friendly" },
//                "Eastern Standard Time"),

//            Make("Cinta Costera",       "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/cintacostera.jpg",
//                BuildDesc("A 7-kilometer coastal beltway connecting Paitilla skyscrapers to Casco Viejo's colonial walls. Built on reclaimed land, it's Panama City's communal promenade for cycling, jogging, street food, and spectacular bay views.",
//                "Stand at Mirador del Pacífico at km 2.6 — you can see both the 17th-century ruins and the 21st-century skyline simultaneously. This visual contrast is unique to Panama City.",
//                "Route 1 begins in Paitilla neighborhood, extends 7km along Panama Bay. Free public transport access from multiple points.",
//                "Free entry. Food and extras limited to what you buy. Standard taxi/transit fares to reach either end.",
//                "Midday sun exposure extreme — very little shade. El Chorrillo neighborhood at the end contrasts sharply with luxury Paitilla.",
//                "No entry fee; raspa'o (shaved ice) from vendors; seafood at Sabores de El Chorrillo.",
//                new[] { "Anayansi Square (raspa'o)", "Mirador del Pacífico", "Mercado del Marisco", "Sabores de El Chorrillo" },
//                "5PM daily for golden hour — heat breaks, skyscrapers glow; January-March for consistent breeze.",
//                "High 9/10 weekends.",
//                "10/10 — 26-hectare public space designed for pedestrians and cyclists.",
//                "1.5 to 3 hours"),
//                10, 15, 7, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["neighborhood_district"] },
//                new List<string> { "seasonal", "family_friendly", "walkable" },
//                "Eastern Standard Time"),

//            Make("Mercado de Mariscos", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/mercado.jpeg",
//                BuildDesc("Panama City's seafood market — the sensory epicenter where Pacific salt air meets the culinary pulse of the capital. Built in 1995 with Japanese government assistance, it's a working market serving both professional chefs and food-loving travelers.",
//                "Don't look for a fancy menu. Order corvina (sea bass) or pulpo (octopus) ceviche in a disposable cup with saltine crackers. At $1-2, arguably the best value meal in Central America.",
//                "Avenida Balboa at the pivot between Cinta Costera and Casco Viejo. Take the waterfront boardwalk under the highway from Casco — safer and more pleasant than crossing the main road.",
//                "Market proper opens 5AM. Restaurants serve through lunch. Ceviche cups $1-2; fried fish platters $7-15+.",
//                "It's a fish market — the smell is powerful. Bathrooms are very basic. Expect noise, music, and friendly vendor competition.",
//                "Platter upgrades; side items like fries/patacones separate; informal parking tips; possible bathroom fee $0.25.",
//                new[] { "Cinta Costera", "Casco Viejo", "Biomuseo" },
//                "5-9AM for fresh catch unloading; weekdays at lunch for best vibe without weekend crowds.",
//                "Moderate to High (8/10).",
//                "8/10 — ground level open layout but wet floors; safe waterfront boardwalk route from Casco.",
//                "45 minutes to 1.5 hours"),
//                5, 15, 7, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["market_street_life"] },
//                new List<string> { "food_focused" },
//                "Eastern Standard Time"),

//            Make("Biomuseo",            "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/biomuseo.jpg",
//                BuildDesc("Frank Gehry's only Latin American building — a chaotic, colorful museum on the Amador Causeway telling the story of how the Isthmus of Panama rose from the sea and changed the world's climate and evolution. Features the immersive 'Panamarama' three-level projection space.",
//                "The Biodiversity Park behind the museum offers the best unobstructed photo of the neon Gehry building with the Bridge of the Americas and massive container ships in the background.",
//                "10-15 min ride from downtown. Bus Route C850 from Albrook Metro Station stops directly at museum. Open Tue-Fri 9AM-3PM; Sat-Sun 10AM-3PM.",
//                "International adults from $20; family 4-person package $60. Copa Airlines promo 'BIOMUSEOCOPA' can drop price to ~$12.",
//                "Museum closes at 3PM — arrive by 1PM at the latest for all 8 galleries. Very windy on the causeway.",
//                "International vs resident pricing; parking limited on weekends; no half-day trips unless arriving early.",
//                new[] { "Punta Culebra Nature Center (Smithsonian)", "Amador Causeway cycling", "Taboga Island ferry" },
//                "Wednesday or Thursday 10AM — beat school groups and perfect lighting for exterior photos.",
//                "Moderate (6/10); quieter on weekday mornings.",
//                "10/10 — modern facility fully accessible with elevators, wide corridors, paved park paths.",
//                "1.5 to 2.5 hours"),
//                10, 20, 9, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["landmark_monument"] },
//                new List<string> { "architecture", "nature", "educational" },
//                "Eastern Standard Time"),

//            Make("Bocas Town (Isla Colón)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/bocastown.webp",
//                BuildDesc("The vibrant Caribbean hub of Bocas del Toro archipelago — a kaleidoscopic grid of colorful buildings, international restaurants, and water taxi docks. Base camp for island hopping, surfing, and the legendary Filthy Friday boat party.",
//                "Individual unit buying is normal here — you can buy 2-3 pills from a pharmacy or a single slice of cheese. Grocery runs often combine with early happy hours to escape the heat.",
//                "Bocas del Toro airport (daily flights from Panama City) or water taxi from mainland. Town is a compact grid — most points of interest within 10-min walk.",
//                "Budget $5-50/day depending on activities. Street food from $2; restaurants $8-15; activities add up.",
//                "Water taxis are fast and wet — sit toward the back. Sun is extreme, stores not air-conditioned. Service can be slow.",
//                "Water taxi fees per person per way; imported goods premium at Super Gourmet; hardware store patience tax.",
//                new[] { "Red Frog Beach", "The Pub & Toro Loco", "Bastimentos Town (Old Bank)" },
//                "October, January, March are driest months; mornings for errands before heat; late evening 8PM+ for street food.",
//                "High (8/10).",
//                "9/10 — only island with paved roads; walkable grid but sidewalks inconsistent.",
//                "3 to 5 days"),
//                5, 50, 7, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["neighborhood_district"] },
//                new List<string> { "nightlife", "best_evening", "budget_friendly", "walkable" },
//                "Eastern Standard Time"),

//            Make("Red Frog Beach",      "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/redfrog.jpeg",
//                BuildDesc("Named for the Strawberry Poison Dart Frogs in its jungle, Red Frog Beach offers the balance of wild Caribbean power and resort-style comfort. Two access routes: rustic $5 jungle shortcut or $45 resort day pass with pool and golf carts.",
//                "Turn LEFT away from the sea at the end of the houses to find a secret deserted beach that is even better for frog spotting. The red frogs are typically found along the jungle path and near wetland areas.",
//                "15-min water taxi from Bocas Town to Red Frog Marina or shortcut entrance. $5 per person jungle path fee. OR specify resort drop-off for $45 day pass.",
//                "Shortcut entry: $5 per person; Resort day pass: $45 (credited to food/drinks); chair rentals separate; island-priced snacks and drinks.",
//                "Strong rip tides — swim only in designated areas, never deeper than hip. Extremely hot sand — wear water shoes. Boat rides can be very wet.",
//                "Water taxi fees both ways; equipment rentals; lobster surcharge; if you miss marina panga private launch is premium.",
//                new[] { "Nature trails and bat caves", "Mangrove transit (part of the journey)" },
//                "Morning for wildlife spotting; driest months Oct, Jan, March for less muddy trails.",
//                "Moderate (5/10) — entry fee and boat ride keep it quieter.",
//                "7/10 — 10-min groomed boardwalk; resort offers golf cart transport.",
//                "2 hours to full day"),
//                5, 190, 7, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["nature_outdoor"] },
//                new List<string> { "good_short_visit", "premium", "budget_friendly", "nature" },
//                "Eastern Standard Time"),

//            Make("Starfish Beach (Bocas del Toro)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/starfishbeach.jpeg",
//                BuildDesc("Playa Estrella — a calm, still-water Caribbean beach on Isla Colón's north end, home to giant orange cushion starfish in crystal-clear shallow water. Reached via $2.50 colectivo bus and 20-min coastal trail from Boca del Drago.",
//                "The sand flies (sandflies) are nearly invisible but leave itchy red welts. Stay in the water — they can't reach you there. Sit neck-deep with a beer for maximum protection.",
//                "Colectivo minibus 'Boca del Drago' from Parque Simón Bolívar, Bocas Town (~$2.50-3.00 each way, 45 min). Then 1.5km flat coastal walk to beach.",
//                "Colectivo $2.50-3 each way; shaded beach chair ~$5; cash only for all vendors.",
//                "Never touch or lift starfish from water — they suffocate within seconds of air exposure. Coconut hazard under palm trees. Sandflies most active at dusk.",
//                "Private water taxi much more expensive than colectivo; chair rental; cash-only vendors.",
//                new[] { "Boca del Drago (quieter entry beach)", "Bird Island (Isla Pájaros)", "Bocas Town for dinner" },
//                "Before 10AM to beat crowds and find more starfish in shallows; weekdays preferred.",
//                "High (8/10) on weekends.",
//                "7/10 — flat well-defined trail; can get muddy after rain; direct water taxi option avoids walk.",
//                "4 to 6 hours"),
//                5, 10, 8, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["nature_outdoor"] },
//                new List<string> { "nature", "group_friendly", "solo_friendly" },
//                "Eastern Standard Time"),

//            Make("Wizard Beach",        "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/wizardbeach.jpeg",
//                BuildDesc("Playa Primera — Isla Bastimentos's raw, untamed soul. A vast golden expanse backed by dense jungle with legendary surf (3-10ft waves) and almost zero visitors. High reward, high risk: the trail is steep and muddy, currents can be lethal, and theft from the treeline is a real concern.",
//                "Locals know thieves watch from the treeline and wait for everyone in a group to swim before striking. Total theft is common — including clothes and shoes. Never leave bags unattended.",
//                "Water taxi to Old Bank on Isla Bastimentos, then 30-45 min jungle hike. Follow signs behind local houses.",
//                "Only bring cash needed for water taxi in a waterproof pouch on your body. Leave all valuables at hotel.",
//                "Lethal rip tides directly in front of trail entrance. Targeted theft from jungle treeline. Steep muddy trail destroys flip-flops. No amenities whatsoever.",
//                "Water taxi; possible shoe replacement after mud; no safety net if anything goes wrong.",
//                new[] { "Old Bank (Afro-Caribbean village)", "Up in the Hill (cacao farm)", "Red Frog Beach" },
//                "December-March and June-August for consistent surf; April-June and September-October for safer swimming.",
//                "Very Low (2/10).",
//                "3/10 — 30-45 min steep muddy jungle hike required; not suitable for limited mobility.",
//                "2 to 3 hours"),
//                10, 15, 4, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["nature_outdoor"] },
//                new List<string> { "adventurous", "best_morning", "family_friendly" },
//                "Eastern Standard Time"),

//            Make("Cayos Zapatilla",     "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/cayos.jpeg",
//                BuildDesc("Pristine uninhabited UNESCO World Heritage islands within Bastimentos National Marine Park. White powder sand, the healthiest coral gardens in the region, nesting Hawksbill sea turtles, and nurse sharks in underwater caves. Accessed via organized tours with stepping-stone stops.",
//                "Standard tours stop for lunch at noon but you won't eat until 3:30PM. The food is expensive. Pack your own lunch and skip the tourist trap.",
//                "Organized tours depart Bocas Town 9-10AM. 1.5-hour boat ride passing Sloth Island and Cayo Coral snorkeling stop. Wet landing — no docks.",
//                "ANAM park entrance fee (cash at ranger station). Tour typically includes boat and some snorkel gear. Lobster upgrade extra.",
//                "Wet landing — carry electronics above your head. Chitras (sand flies) active at dusk. No food or water on the island.",
//                "Park entrance fee; snorkel gear rental if not included; ANAM ranger station cash only; tour lunch overpriced.",
//                new[] { "Sloth Island", "Cayo Coral (snorkeling)", "Hollywood/Starfish Island" },
//                "September-October for calmest seas and best coral visibility; arrange private boat for 9AM arrival before tour crowds.",
//                "Moderate (6/10) — 11AM-3PM peak tour windows.",
//                "4/10 — wet landing requires mobility; interpretive trail in good condition once on island.",
//                "2-3 hours on island; full day with transit"),
//                30, 100, 8, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
//                new List<Category> { cats["nature_outdoor"] },
//                new List<string> { "tourist_hotspot", "nature" },
//                "Eastern Standard Time"),

//            Make("San Blas Islands (Via Carti)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/carti.jpg",
//                BuildDesc("The gateway to Guna Yala — the autonomous Guna people's territory. The Cartí port is reached via the El Llano-Cartí Road through primary rainforest, a 30km 4x4-only mountain route that acts as a natural filter keeping mass tourism out of the archipelago.",
//                "For the Guna, the checkpoint isn't just a toll — it's a symbol of the 1925 Revolution. The difficult road is intentional: it protects their sovereignty by limiting mass encroachment.",
//                "Panama City → Panamericana → El Llano-Cartí Road (Texaco station turnoff). 4x4 mandatory. Stop at checkpoint for passports and $20 international fee. Use Waze not Google Maps.",
//                "Guna Yala entrance: $20/person. Port access: $2/person. Vehicle fee: $3. Parking: ~$3/day. Bring quarters and small bills.",
//                "4x4 is mandatory — checkpoints enforce this. Motion sickness common on mountain rollercoaster road. Take Dramamine before Texaco stop.",
//                "Entrance fees rarely included in tour prices; boat shuttles $30-50/person separate; island landing fees $3/person per island visited.",
//                new[] { "El Llano-Cartí Road (scenic drive)", "Texaco Station (last supplies)", "The Checkpoint" },
//                "Early morning 8:30AM for first boat departures; dry season December-April for stable road.",
//                "High (8/10) at port 8:30-10:30AM.",
//                "2/10 — extremely difficult access road; not wheelchair accessible.",
//                "30 to 60 minutes transit point"),
//                30, 50, 7, panama, new List<City> { gunaYala }, new List<Language> { spanish, english, guna }, new List<Currency> { usd, pab },
//                new List<Category> { cats["neighborhood_district"] },
//                new List<string> { "crowded", "tourist_hotspot" },
//                "Eastern Standard Time"),

//            Make("Isla Perro",          "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/isla-perro.jpg",
//                BuildDesc("The most iconic snorkeling spot in San Blas — a small Guna-managed island where a sunken Army freighter rests just yards from the white sand beach. The wreck is now a thriving artificial reef teeming with tropical fish, carefully preserved by restricting large-scale development.",
//                "To the Guna families who protect it, Isla Perro is the gateway to underwater history. They offer a simple traditional lunch of fish and rice before the island returns to silence once day-trippers depart.",
//                "4x4 transport from Panama City to Cartí port (2.5-3 hrs), then 20-30 min lancha through the Lemon Cays.",
//                "Guna Yala entrance $20. Port docking fees ~$2. Equipment rental if needed. Lobster surcharge over standard fish lunch. Transport from Panama City separate.",
//                "No ATMs or card machines anywhere. Bring filtered water and snacks. Sunscreen essential — very little shade.",
//                "4x4 vehicle transport to coast; island fees; snorkel gear rental; lobster surcharge.",
//                new[] { "The Pool (sandbar)", "Isla Diablo (neighbor)", "Island hopping (2-4 islands/day)" },
//                "January-March for peak water clarity; weekdays for quieter experience; morning for snorkeling before afternoon winds.",
//                "Medium (6/10).",
//                "4/10 — long 4x4 mountain drive + boat transfer; not wheelchair accessible.",
//                "4-6 hours day trip; 1 night for true quiet after day-trippers leave"),
//                80, 250, 8, panama, new List<City> { gunaYala }, new List<Language> { spanish, guna }, new List<Currency> { usd, pab },
//                new List<Category> { cats["nature_outdoor"] },
//                new List<string> { "photography", "adventurous", "group_friendly", "nature", "good_short_visit" },
//                "Eastern Standard Time"),

//            Make("Ibin's Beach Restaurant (Isla Banedup)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/ibins-restaurant.jpg",
//                BuildDesc("A remote 'pirate-chic' beach restaurant on a tiny outer island of San Blas — accessible only by catamaran charter. Ibin, the owner, returned from cooking for Panama City celebrities to run this hidden gem serving fresh lobster, octopus curry, and cocktails with his feet in the sand.",
//                "Ibin sometimes rows fresh coconut rolls or focaccia directly to anchored boats in the morning. He treats guests like family because on an island this small, everyone is.",
//                "Only accessible via catamaran charter of 5+ nights through the outer Dutch Cays. Look for rustic wooden pier on Banedup Island.",
//                "Charter costs; Guna territory entrance; food priced on what the ocean provides — lobster, tuna, octopus. Cash only.",
//                "Remote — no medical facilities, rough boat rides to reach, strong sun, sand flies at evening, basic bathrooms.",
//                "Guna territory fees not included in charter; drinks extra; overnight hut fees; snorkel rentals.",
//                new[] { "The Pool (sandbar)", "Dutch Cays snorkeling", "Starfish island" },
//                "December-April dry season for comfortable sailing; January-March for best water clarity.",
//                "Very Low (3/10) — far into the archipelago.",
//                "4/10 — remote access via catamaran only; wet landings.",
//                "2 to 4 hours or half day"),
//                30, 50, 8, panama, new List<City> { gunaYala }, new List<Language> { spanish, guna }, new List<Currency> { usd, pab },
//                new List<Category> { cats["food_experience"] },
//                new List<string> { "local_favorite", "hidden_gem", "food_focused" },
//                "Eastern Standard Time"),

//            Make("Isla Diablo",         "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/isla-diablo.jpg",
//                BuildDesc("The social hub of San Blas — a Guna-managed island with more energy than its neighbors. Young travelers, hammocks over water, and a snack bar. Just minutes from Isla Perro's famous shipwreck snorkeling.",
//                "The Guna people aren't 'staff' — they are the owners and protectors of their ancestral land. The fee culture funds their community sovereignty directly.",
//                "30-min lancha from Cartí port through the Lemon Cays. After 2.5-hr 4x4 from Panama City.",
//                "Guna territory $20+; port fee ~$2; boat transfer separate; island hopping small fees; drinks and lobster upgrades.",
//                "Not for 'peace and quiet' — this is the social island. Basic facilities and shared bathrooms.",
//                "Entrance fees; boat transfer; island hopping; lobster/drink upgrades; transport from Panama City.",
//                new[] { "Isla Perro (shipwreck)", "Achutupu (Guna village)", "Lemon Cays" },
//                "December-April dry season; weekdays for lively not crowded vibe; morning for calmer boat crossing.",
//                "High (8/10) for San Blas standards.",
//                "4/10 — 2.5-hr 4x4 + 30-min boat; not wheelchair accessible.",
//                "Day trip 4-6 hrs; overnight 1-2 nights sweet spot"),
//                135, 250, 7, panama, new List<City> { gunaYala }, new List<Language> { spanish, guna }, new List<Currency> { usd, pab },
//                new List<Category> { cats["nature_outdoor"] },
//                new List<string> { "couple_friendly", "relaxing", "nature", "group_friendly" },
//                "Eastern Standard Time"),

//            Make("Cayos Holandeses",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dutch-cays.jpg",
//                BuildDesc("The edge of the San Blas archipelago — the northernmost outer cays where sand is whiter, water is clearer, and silence is heavier. Accessible only by overnight expedition, the Dutch Cays offer the most vibrant coral in the archipelago and total digital detox.",
//                "Locals check the 'Brisa' (North Trade Winds) before the outer cays crossing. If your Guna captain says the water is too 'bravo,' respect it. The crossing involves real open-sea swells.",
//                "4x4 to Cartí port, then 1-1.5 hour open-water boat crossing to outer cays like Wegodub or Diadub. No centralized pier — land directly on sand.",
//                "Guna territory $20+; port and transfer fees; overnight stay packages; lobster upgrade extra. Base prices often increase by $30-50 after all fees.",
//                "Isolation — no medical facilities. Extreme UV. Weather can cancel crossings. Basic huts and shared bathrooms. Bugs at evening.",
//                "All fees; real total often $30-50 more than advertised; lobster surcharge; transport from Panama City.",
//                new[] { "The Pool (sandbar)", "Highest-quality snorkeling in San Blas", "Kayaking in protected lagoons" },
//                "December-April for comfortable sailing; January-March for best coral visibility.",
//                "Very Low (3/10) — distance and overnight requirement keeps it exclusive.",
//                "4/10 — long mountain drive + bumpy open-sea crossing; not wheelchair accessible.",
//                "2-3 nights minimum; 5+ days for full sailing charter immersion"),
//                200, 385, 8, panama, new List<City> { gunaYala }, new List<Language> { spanish, guna }, new List<Currency> { usd, pab },
//                new List<Category> { cats["nature_outdoor"] },
//                new List<string> { "best_morning", "premium", "good_short_visit", "nature" },
//                "Eastern Standard Time"),
//        };

//        db.Destinations.AddRange(destinations);
//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 8. WISHLISTS
//    // Migrates the 5-day Tokyo wishlist with all itinerary days and items.
//    // ═══════════════════════════════════════════════════════════════════════════


//    // ═══════════════════════════════════════════════════════════════════════════
//    // 8. WISHLISTS
//    // Four wishlists:
//    //   1. 5-Day Tokyo First-Time Trip
//    //   2. San Blas Relax Trip (3 days, Guna Yala)
//    //   3. Panama City Highlights (2 days, Pacific side)
//    //   4. Bocas del Toro Party & Surf (4 days, Caribbean)
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedWishlistsAsync(HodracDbContext db)
//    {
//        if (await db.Wishlists.AnyAsync()) return;

//        // ── City lookups ──────────────────────────────────────────────────────
//        var tokyo = await db.Cities.FirstAsync(c => c.CityName == "Tokyo");
//        var panamaCity = await db.Cities.FirstAsync(c => c.CityName == "Panama City");
//        var gunaYala = await db.Cities.FirstAsync(c => c.CityName == "Guna Yala");
//        var bocas = await db.Cities.FirstAsync(c => c.CityName.StartsWith("Caribbean coast"));

//        // ── Destination ID lookups ────────────────────────────────────────────
//        var japanId = await db.Countries.Where(c => c.CountryName == "Japan")
//                                         .Select(c => c.CountryId).FirstAsync();
//        var panamaId = await db.Countries.Where(c => c.CountryName == "Panama")
//                                         .Select(c => c.CountryId).FirstAsync();

//        var tokyoDestIds = await db.Destinations
//            .Where(d => d.CountryId == japanId)
//            .Select(d => d.DestinationId).ToListAsync();

//        // Panama destinations resolved by name prefix for precise wishlist linking
//        var panamaDestByName = await db.Destinations
//            .Where(d => d.CountryId == panamaId)
//            .Select(d => new { d.DestinationId, d.DestinationName })
//            .ToListAsync();

//        Guid PId(string prefix) => panamaDestByName
//            .First(d => d.DestinationName.StartsWith(prefix)).DestinationId;

//        // ── Wishlist 1: Tokyo ─────────────────────────────────────────────────
//        var tokyoWlId = Guid.NewGuid();
//        var tw1 = Guid.NewGuid(); var tw2 = Guid.NewGuid();
//        var tw3 = Guid.NewGuid(); var tw4 = Guid.NewGuid(); var tw5 = Guid.NewGuid();

//        var tokyoWishlist = new Wishlist
//        {
//            WishlistId = tokyoWlId,
//            WishlistName = "5-Day Tokyo First-Time Trip (Perfect split: Shibuya, Shinjuku, Asakusa)",
//            WishlistDescription = "Tokyo in 5 days: Experience the perfect mix of chaos and calm with neon lights, ancient temples, street food, and skyline views.",
//            ShortStory = "A 5-day Tokyo itinerary perfect for first timers — from relaxing shrines and culturally rich streets to high-octane nightlife and immersive art. Each day is planned to include exploration and recovery so you never feel overwhelmed.",
//            TotalDays = 5,
//            PeopleType = "Solo travelers, couples, and first-time visitors",
//            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tokyo.jpg",
//            IsTemplate = true,
//            OwnerUserId = null,
//            TotalGlobalSaveCount = 0,
//            IsFeatured = true,
//            DefaultTravelersCount = 2,
//            BasePricePerPerson = 1400,
//            CalculatedTotalCost = 2800,
//            DepositAmountRequired = 280,
//            PrimaryPersonaTarget = "First-Time Japan Traveler",
//            AccommodationInclusions = "Mid-range hotel in Shinjuku or Shibuya (~$80-120/night)",
//            TransitInclusions = "7-day IC card (Suica/Pasmo) for all metro and JR trains (~$50)",
//            ActivityInclusions = "Senso-ji, Meiji Shrine, Golden Gai, teamLab Borderless, Tsukiji Outer Market",
//            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "7-Day IC Transit Card", "Pocket WiFi rental", "Luggage forwarding between hotels", "City map with QR codes" }),
//            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Culture", "Food", "Adventure", "Urban" }),
//            RawContentKeywords = "Tokyo Japan first time Shibuya Shinjuku Asakusa Harajuku temple shrine street food nightlife anime",
//            CreatedAt = DateTimeOffset.UtcNow,
//        };

//        tokyoWishlist.ItineraryDays = new List<ItineraryDay>
//        {
//            new() { ItineraryDayId = tw1, WishlistId = tokyoWlId, DayNumber = 1, DayTitle = "Harajuku & Shibuya — Tokyo Energy Introduction", MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
//            new() { ItineraryDayId = tw2, WishlistId = tokyoWlId, DayNumber = 2, DayTitle = "Asakusa — Culture & Slow Exploration",           MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
//            new() { ItineraryDayId = tw3, WishlistId = tokyoWlId, DayNumber = 3, DayTitle = "Shinjuku — City Balance & Nightlife",            MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
//            new() { ItineraryDayId = tw4, WishlistId = tokyoWlId, DayNumber = 4, DayTitle = "Central Tokyo — Food & Immersive Experience",    MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
//            new() { ItineraryDayId = tw5, WishlistId = tokyoWlId, DayNumber = 5, DayTitle = "Flex Day — Personalize Your Tokyo Experience",   MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
//        };

//        tokyoWishlist.ItineraryDays.ToList()[0].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Meiji Shrine",        ItemDescription = "Visit Meiji Shrine — quiet forest shrine dedicated to Emperor Meiji.",                               ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Takeshita Street",    ItemDescription = "Explore Takeshita Street — street food, kawaii fashion, and youth culture.",                        ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 15 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Cat Street",          ItemDescription = "Walk through Cat Street — local boutiques, artisan cafes, and high-end streetwear.",                 ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Shibuya Crossing",    ItemDescription = "Experience Shibuya Crossing — the world's busiest pedestrian intersection.",                         ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Shibuya Sky",         ItemDescription = "Shibuya Sky — sunset to night 229-meter rooftop city view. Book online in advance.",                 ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = true,  IsSelectedByDefault = true,  IndividualCostModifier = 18, SocialProofBadge = "Traveler Favorite" },
//        };
//        tokyoWishlist.ItineraryDays.ToList()[1].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw2, ItemTitle = "Senso-ji Temple",     ItemDescription = "Visit Senso-ji Temple — Tokyo's oldest temple, founded 645 AD.",                                      ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0, SocialProofBadge = "Must-See" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw2, ItemTitle = "Kaminarimon Gate",    ItemDescription = "Walk through Kaminarimon Gate — the iconic 700kg thunder lantern gate.",                               ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw2, ItemTitle = "Asakusa Streets",     ItemDescription = "Explore Asakusa streets — souvenirs, food stalls, and Edo-period atmosphere.",                        ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 20 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw2, ItemTitle = "Sumida Riverwalk",    ItemDescription = "Relax along the Sumida River walk with views of Tokyo Skytree at sunset.",                            ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
//        };
//        tokyoWishlist.ItineraryDays.ToList()[2].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw3, ItemTitle = "Shinjuku Gyoen",                             ItemDescription = "Walk through Shinjuku Gyoen — 144 acres of Japanese, English, and French gardens.",         ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 4 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw3, ItemTitle = "Tokyo Metropolitan Government Building",      ItemDescription = "Free 202-meter skyline view. Bonus: Mt. Fuji on clear days.",                              ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0, SocialProofBadge = "Best Free View in Tokyo" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw3, ItemTitle = "Shinjuku Golden Gai",                        ItemDescription = "Explore Golden Gai — 200+ tiny themed bars in narrow alleys. Cover charge per bar.",         ItemOrderIndex = 2, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 30 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw3, ItemTitle = "Kabukicho",                                  ItemDescription = "Kabukicho — neon nightlife district with Godzilla, namco TOKYO, and themed bars.",           ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = true,  IsSelectedByDefault = false, IndividualCostModifier = 20 },
//        };
//        tokyoWishlist.ItineraryDays.ToList()[3].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw4, ItemTitle = "Tsukiji Outer Market",                       ItemDescription = "Food crawl at Tsukiji Outer Market — fresh sushi breakfast and seafood stalls from 8AM.",     ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 25, SocialProofBadge = "Eat Like a Local" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw4, ItemTitle = "teamLab Borderless: Azabudai Hills",          ItemDescription = "Immersive digital art museum without a map. Book tickets online in advance.",                 ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 30, SocialProofBadge = "Most Visited Art Museum in Japan" },
//        };
//        tokyoWishlist.ItineraryDays.ToList()[4].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw5, ItemTitle = "Tokyo Disney (Option A)",   ItemDescription = "Option A: Tokyo DisneySea — 25th Anniversary Sparkling Jubilee with Fantasy Springs.",       ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = true,  IsSelectedByDefault = false, IndividualCostModifier = 68 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw5, ItemTitle = "Akihabara (Option B)",      ItemDescription = "Option B: Explore Akihabara — anime merchandise, retro gaming, maid cafes, and arcades.",    ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = true,  IsSelectedByDefault = false, IndividualCostModifier = 20 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw5, ItemTitle = "Last-minute shopping",      ItemDescription = "Don Quijote in Shinjuku or Shibuya 109 for final souvenirs and snacks.",                     ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = true,  IsSelectedByDefault = true,  IndividualCostModifier = 30 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw5, ItemTitle = "Farewell dinner in Tokyo",  ItemDescription = "Farewell dinner — Omoide Yokocho for yakitori or Ramen Alley in Shinjuku.",                  ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 25 },
//        };

//        tokyoWishlist.WishlistDestinations = tokyoDestIds.Select(id => new WishlistDestination { WishlistId = tokyoWlId, DestinationId = id }).ToList();
//        tokyoWishlist.Collaborators = new List<WishlistCollaborator>();

//        // ── Wishlist 2: San Blas Relax Trip ───────────────────────────────────
//        var sanBlasWlId = Guid.NewGuid();
//        var sb1 = Guid.NewGuid(); var sb2 = Guid.NewGuid(); var sb3 = Guid.NewGuid();

//        var sanBlasWishlist = new Wishlist
//        {
//            WishlistId = sanBlasWlId,
//            WishlistName = "San Blas Relax Trip",
//            WishlistDescription = "Turquoise water, hammocks over the sea, and fresh lobster on the sand — no wifi, no worries.",
//            ShortStory = "Spend three days island-hopping through the San Blas archipelago, sleeping in rustic cabins over crystal-clear water, snorkeling shipwrecks, and eating lobster on the sand.",
//            TotalDays = 3,
//            PeopleType = "Couples and slow travelers",
//            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/san-blas-hero.jpg",
//            IsTemplate = true,
//            OwnerUserId = null,
//            TotalGlobalSaveCount = 0,
//            IsFeatured = true,
//            DefaultTravelersCount = 2,
//            BasePricePerPerson = 350,
//            CalculatedTotalCost = 700,
//            DepositAmountRequired = 70,
//            PrimaryPersonaTarget = "Couple Seeking Remote Nature Escape",
//            AccommodationInclusions = "Overwater cabin on Isla Diablo (~$80/night)",
//            TransitInclusions = "4x4 transfer from Panama City to Cartí + lanchas between islands",
//            ActivityInclusions = "Isla Perro shipwreck snorkeling, Ibin's beach lunch, Dutch Cays day trip",
//            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "4x4 transport Panama City → Cartí", "Inter-island lanchas", "Daily meals (fish, coconut rice)", "Snorkel gear rental", "Guna Yala entrance fee ($20)" }),
//            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Relaxation", "Nature", "Romance", "Adventure" }),
//            RawContentKeywords = "San Blas Panama Guna Yala islands snorkeling shipwreck overwater cabin lobster slow travel couple",
//            CreatedAt = DateTimeOffset.UtcNow,
//        };

//        sanBlasWishlist.ItineraryDays = new List<ItineraryDay>
//        {
//            new() { ItineraryDayId = sb1, WishlistId = sanBlasWlId, DayNumber = 1, DayTitle = "Arrival & First Island Escape",   MorningCityId = gunaYala.CityId, AfternoonCityId = gunaYala.CityId, EveningCityId = gunaYala.CityId },
//            new() { ItineraryDayId = sb2, WishlistId = sanBlasWlId, DayNumber = 2, DayTitle = "Sandbars & Lobster Lunch",         MorningCityId = gunaYala.CityId, AfternoonCityId = gunaYala.CityId, EveningCityId = gunaYala.CityId },
//            new() { ItineraryDayId = sb3, WishlistId = sanBlasWlId, DayNumber = 3, DayTitle = "Slow Morning & Return",            MorningCityId = gunaYala.CityId, AfternoonCityId = gunaYala.CityId, EveningCityId = gunaYala.CityId },
//        };

//        sanBlasWishlist.ItineraryDays.ToList()[0].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Drive Panama City → Cartí",         ItemDescription = "4x4 mountain drive (2.5–3 hrs) to the Cartí port. Last supplies at the Texaco stop. Bring cash, passport, and Dramamine.",    ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Boat transfer into San Blas",       ItemDescription = "Lancha ride (20–30 min) from Cartí through the Lemon Cays to Isla Perro.",                                                     ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Isla Perro — shipwreck snorkeling", ItemDescription = "Swim from the beach directly to the sunken Army freighter, teeming with tropical fish and coral.",                              ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0, SocialProofBadge = "Best Snorkeling in San Blas" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Lunch on the island",               ItemDescription = "Traditional Guna lunch — fresh fish and coconut rice served on the beach.",                                                      ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 12 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Overnight on Isla Diablo",          ItemDescription = "Check into a rustic overwater cabin on Isla Diablo — the social hub of the archipelago.",                                        ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 80 },
//        };
//        sanBlasWishlist.ItineraryDays.ToList()[1].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "The Pool sandbar",                  ItemDescription = "Morning at The Pool — a famous shallow sandbar with waist-deep, crystal-clear water in the middle of the ocean.",                ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0, SocialProofBadge = "Hidden Gem" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "Boat to Banedup Island",            ItemDescription = "Short lancha ride to Isla Banedup — home of Ibin's Beach Restaurant.",                                                          ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "Lunch at Ibin's Beach Restaurant",  ItemDescription = "Seafood lunch at the legendary pirate-chic beach restaurant — lobster, octopus curry, and cocktails with your feet in the sand.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 45, SocialProofBadge = "Must-Eat" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "Snorkeling near the Dutch Cays",    ItemDescription = "Afternoon snorkeling at Cayos Holandeses — the most vibrant coral reefs in San Blas, far from mainland runoff.",                  ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = true,  IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "Overnight overwater cabin",         ItemDescription = "Second night on Isla Diablo — evening drinks, stargazing, and the sound of the Caribbean.",                                      ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 80 },
//        };
//        sanBlasWishlist.ItineraryDays.ToList()[2].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb3, ItemTitle = "Breakfast by the sea",              ItemDescription = "Last morning breakfast on the island — fresh fruit, bread, and coffee as the sun rises over the archipelago.",                   ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb3, ItemTitle = "Visit Kuna village",                ItemDescription = "Optional stop at a nearby Guna village — see traditional molas, meet community members, and understand Guna autonomy.",           ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = true,  IsSelectedByDefault = true, IndividualCostModifier = 5 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb3, ItemTitle = "Return boat to Cartí",              ItemDescription = "Lancha back to the mainland port. Sit low in the boat and keep electronics in a dry bag.",                                       ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb3, ItemTitle = "Drive back to Panama City",         ItemDescription = "Return mountain drive to Panama City. Stop at the Texaco for a cold drink and to decompress after island time.",                  ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//        };

//        var sanBlasDestIds = new[]
//        {
//            PId("San Blas Islands"), PId("Isla Perro"), PId("Ibin's"),
//            PId("Isla Diablo"), PId("Cayos Holandeses"),
//        };
//        sanBlasWishlist.WishlistDestinations = sanBlasDestIds.Select(id => new WishlistDestination { WishlistId = sanBlasWlId, DestinationId = id }).ToList();
//        sanBlasWishlist.Collaborators = new List<WishlistCollaborator>();

//        // ── Wishlist 3: Panama City Highlights ────────────────────────────────
//        var pcWlId = Guid.NewGuid();
//        var pc1 = Guid.NewGuid(); var pc2 = Guid.NewGuid();

//        var panamaCityWishlist = new Wishlist
//        {
//            WishlistId = pcWlId,
//            WishlistName = "Panama City Highlights (Pacific Side)",
//            WishlistDescription = "History, skyline views, the Panama Canal, and fresh seafood — the perfect introduction to Panama City.",
//            ShortStory = "Spend two days exploring Panama City's historic streets, walking along the waterfront skyline, and witnessing one of the greatest engineering feats in the world.",
//            TotalDays = 2,
//            PeopleType = "First-time visitors and city lovers",
//            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/panamacity.jpeg",
//            IsTemplate = true,
//            OwnerUserId = null,
//            TotalGlobalSaveCount = 0,
//            IsFeatured = true,
//            DefaultTravelersCount = 2,
//            BasePricePerPerson = 160,
//            CalculatedTotalCost = 320,
//            DepositAmountRequired = 0,
//            PrimaryPersonaTarget = "First-Time Panama City Visitor",
//            AccommodationInclusions = "Mid-range hotel in Marbella or Bella Vista (~$70-90/night)",
//            TransitInclusions = "Uber/taxi for Canal and Causeway trips (~$25 total)",
//            ActivityInclusions = "Canal IMAX + museum admission, Casco Viejo walking tour, Biomuseo, Mercado de Mariscos",
//            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "Panama Canal IMAX ticket ($17.22)", "Biomuseo admission ($20)", "Casco Viejo self-guided map", "Seafood lunch at Mercado de Mariscos" }),
//            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Culture", "Food", "Educational", "Urban" }),
//            RawContentKeywords = "Panama City Casco Viejo canal Biomuseo Miraflores locks seafood first time history",
//            CreatedAt = DateTimeOffset.UtcNow,
//        };

//        panamaCityWishlist.ItineraryDays = new List<ItineraryDay>
//        {
//            new() { ItineraryDayId = pc1, WishlistId = pcWlId, DayNumber = 1, DayTitle = "Old Town & City Energy",          MorningCityId = panamaCity.CityId, AfternoonCityId = panamaCity.CityId, EveningCityId = panamaCity.CityId },
//            new() { ItineraryDayId = pc2, WishlistId = pcWlId, DayNumber = 2, DayTitle = "Canal & History of Panama",       MorningCityId = panamaCity.CityId, AfternoonCityId = panamaCity.CityId, EveningCityId = panamaCity.CityId },
//        };

//        panamaCityWishlist.ItineraryDays.ToList()[0].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Casco Viejo — morning walk",       ItemDescription = "Arrive at Casco Viejo and walk the cobblestone streets, plazas, and Palacio de las Garzas.",                              ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0, SocialProofBadge = "UNESCO Heritage" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Lunch at Mercado de Mariscos",     ItemDescription = "Fresh ceviche and fried fish at Panama City's working seafood market. Corvina cups from $1–$2.",                           ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 12, SocialProofBadge = "Best $2 Meal in Central America" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Walk along Cinta Costera",         ItemDescription = "Stroll the 7km waterfront promenade connecting Paitilla skyscrapers to Casco Viejo's colonial walls.",                    ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Sunset overlooking Panama Bay",    ItemDescription = "Watch the Pacific sunset from Cinta Costera's Mirador del Pacífico — 17th-century ruins on your right, glass towers left.", ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Rooftop bar in Casco Viejo",       ItemDescription = "End the night at one of Casco's rooftop bars — neon skyline, Geisha coffee cocktails, and warm sea breeze.",               ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = true,  IsSelectedByDefault = true, IndividualCostModifier = 20 },
//        };
//        panamaCityWishlist.ItineraryDays.ToList()[1].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Panama Canal — Miraflores Locks",  ItemDescription = "Arrive at 8AM for the morning transit window. Watch 100,000-ton vessels lifted by gravity alone through the original locks.", ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 17, SocialProofBadge = "Panama's #1 Landmark" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Canal IMAX film + museum",         ItemDescription = "45-minute 3D IMAX narrated by Morgan Freeman, then four floors of exhibits on the canal's history and mechanics.",           ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Lunch near the canal",             ItemDescription = "Lunch at one of the restaurants near the Miraflores visitor center before heading to the Causeway.",                          ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 15 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Biomuseo",                         ItemDescription = "Frank Gehry's only Latin American building — Panama's biodiversity story told through 8 immersive galleries.",               ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 20, SocialProofBadge = "Architectural Icon" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Panoramic city & canal views",     ItemDescription = "Walk the Amador Causeway at golden hour — container ships under the Bridge of the Americas with the city glowing behind you.", ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//        };

//        var pcDestIds = new[]
//        {
//            PId("Casco Viejo"), PId("Panama Canal"), PId("Cinta Costera"),
//            PId("Mercado de Mariscos"), PId("Biomuseo"),
//        };
//        panamaCityWishlist.WishlistDestinations = pcDestIds.Select(id => new WishlistDestination { WishlistId = pcWlId, DestinationId = id }).ToList();
//        panamaCityWishlist.Collaborators = new List<WishlistCollaborator>();

//        // ── Wishlist 4: Bocas del Toro Party & Surf ───────────────────────────
//        var bocasWlId = Guid.NewGuid();
//        var bw1 = Guid.NewGuid(); var bw2 = Guid.NewGuid();
//        var bw3 = Guid.NewGuid(); var bw4 = Guid.NewGuid();

//        var bocasWishlist = new Wishlist
//        {
//            WishlistId = bocasWlId,
//            WishlistName = "Bocas del Toro Party & Surf",
//            WishlistDescription = "Caribbean rhythm, boat parties, and world-class waves — Panama's wild side.",
//            ShortStory = "Spend four days hopping between islands, surfing tropical waves, and partying on boats with travelers from around the world.",
//            TotalDays = 4,
//            PeopleType = "Backpackers, party travelers, and surfers",
//            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/bocas.jpeg",
//            IsTemplate = true,
//            OwnerUserId = null,
//            TotalGlobalSaveCount = 0,
//            IsFeatured = true,
//            DefaultTravelersCount = 2,
//            BasePricePerPerson = 320,
//            CalculatedTotalCost = 640,
//            DepositAmountRequired = 0,
//            PrimaryPersonaTarget = "Budget Adventure Traveler",
//            AccommodationInclusions = "Hostel or budget hotel in Bocas Town (~$25-50/night)",
//            TransitInclusions = "Flights Panama City → Bocas del Toro + inter-island pangas",
//            ActivityInclusions = "Filthy Friday boat party, Red Frog Beach day, Starfish Beach colectivo, Cayos Zapatilla tour",
//            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "Domestic flight or bus Panama City → Bocas", "Daily inter-island panga transfers", "Red Frog Beach entry ($5)", "Starfish Beach colectivo ($3 each way)" }),
//            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Adventure", "Nightlife", "Nature", "Budget" }),
//            RawContentKeywords = "Bocas del Toro Panama Caribbean party surf backpacker hostel island hopping Red Frog beach",
//            CreatedAt = DateTimeOffset.UtcNow,
//        };

//        bocasWishlist.ItineraryDays = new List<ItineraryDay>
//        {
//            new() { ItineraryDayId = bw1, WishlistId = bocasWlId, DayNumber = 1, DayTitle = "Arrival & Island Vibes",   MorningCityId = bocas.CityId, AfternoonCityId = bocas.CityId, EveningCityId = bocas.CityId },
//            new() { ItineraryDayId = bw2, WishlistId = bocasWlId, DayNumber = 2, DayTitle = "Filthy Friday Boat Party", MorningCityId = bocas.CityId, AfternoonCityId = bocas.CityId, EveningCityId = bocas.CityId },
//            new() { ItineraryDayId = bw3, WishlistId = bocasWlId, DayNumber = 3, DayTitle = "Beaches & Surf",           MorningCityId = bocas.CityId, AfternoonCityId = bocas.CityId, EveningCityId = bocas.CityId },
//            new() { ItineraryDayId = bw4, WishlistId = bocasWlId, DayNumber = 4, DayTitle = "Snorkel & Departure",      MorningCityId = bocas.CityId, AfternoonCityId = bocas.CityId, EveningCityId = bocas.CityId },
//        };

//        bocasWishlist.ItineraryDays.ToList()[0].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw1, ItemTitle = "Arrive in Bocas Town",          ItemDescription = "Fly or bus into Bocas Town (Isla Colón). Check into your hostel and drop your bags.",                                          ItemOrderIndex = 0, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw1, ItemTitle = "Explore town and waterfront",   ItemDescription = "Walk the grid, find your panga operator for tomorrow, and get your bearings on Main Street.",                                  ItemOrderIndex = 1, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw1, ItemTitle = "Dinner and drinks in Bocas",    ItemDescription = "Dinner at a waterfront restaurant then drinks at The Pub or Toro Loco. Island time starts now.",                              ItemOrderIndex = 2, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 30 },
//        };
//        bocasWishlist.ItineraryDays.ToList()[1].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw2, ItemTitle = "Filthy Friday boat party",      ItemDescription = "The legendary all-day boat party — island hopping with music, drinks, and swim stops in crystal-clear water.",                  ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 35, SocialProofBadge = "Bocas Institution" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw2, ItemTitle = "Island hopping swim stops",     ItemDescription = "Multiple stops at sandbars and shallow bays. Keep electronics in a dry bag.",                                                  ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw2, ItemTitle = "Recovery evening in town",      ItemDescription = "Return to Bocas Town, shower, grab a cheap dinner at one of the local fondas before an early night.",                          ItemOrderIndex = 2, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 10 },
//        };
//        bocasWishlist.ItineraryDays.ToList()[2].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw3, ItemTitle = "Red Frog Beach — surf & frogs", ItemDescription = "Morning panga to Red Frog Beach. Surf the Caribbean break, walk the jungle trail, and spot the tiny red poison dart frogs.",   ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 5, SocialProofBadge = "Unmissable" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw3, ItemTitle = "Starfish Beach",                ItemDescription = "Colectivo bus ($3) to Boca del Drago then 20-min coastal walk to Playa Estrella. Sit neck-deep with a Balboa beer.",            ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 3, SocialProofBadge = "Most Photographed Beach" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw3, ItemTitle = "Relax in shallow water",        ItemDescription = "Float with the starfish, eat at one of the rustic beach shacks, and let the Caribbean do its thing.",                           ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 8 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw3, ItemTitle = "Sunset at Wizard Beach",        ItemDescription = "Optional: 30-45 min jungle hike from Old Bank for the most dramatic Caribbean sunset. Leave all valuables at the hostel.",      ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = true,  IsSelectedByDefault = false, IndividualCostModifier = 0 },
//        };
//        bocasWishlist.ItineraryDays.ToList()[3].ItineraryItems = new List<ItineraryItem>
//        {
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw4, ItemTitle = "Cayos Zapatilla tour",          ItemDescription = "Full-day tour to Cayos Zapatilla — UNESCO coral gardens, nurse sharks, Hawksbill turtles. Pack your own lunch.",               ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 40, SocialProofBadge = "Best Snorkeling in Bocas" },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw4, ItemTitle = "Snorkel coral reefs",           ItemDescription = "Guided snorkel at Cayo Coral en route — vibrant beds of hard and soft coral with tropical fish.",                               ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw4, ItemTitle = "Beach time on Zapatilla",       ItemDescription = "Walk the perimeter trail (El Bosque Detrás del Arrecife), lounge on powder sand, and look for nesting turtles.",               ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw4, ItemTitle = "Return to Bocas & depart",      ItemDescription = "Return to Bocas Town and catch your evening flight or bus back to Panama City.",                                                 ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
//        };

//        var bocasDestIds = new[]
//        {
//            PId("Bocas Town"), PId("Red Frog"), PId("Starfish Beach"),
//            PId("Wizard Beach"), PId("Cayos Zapatilla"),
//        };
//        bocasWishlist.WishlistDestinations = bocasDestIds.Select(id => new WishlistDestination { WishlistId = bocasWlId, DestinationId = id }).ToList();
//        bocasWishlist.Collaborators = new List<WishlistCollaborator>();

//        // ── Save all four wishlists ────────────────────────────────────────────
//        db.Wishlists.AddRange(tokyoWishlist, sanBlasWishlist, panamaCityWishlist, bocasWishlist);
//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // 9. FEATURED POOL
//    // All four wishlists seeded as editorial featured entries.
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static async Task SeedFeaturedPoolAsync(HodracDbContext db)
//    {
//        if (await db.FeaturedWishlistPool.AnyAsync()) return;

//        var wishlists = await db.Wishlists
//            .Where(w => w.IsTemplate)
//            .Select(w => new { w.WishlistId, w.WishlistName })
//            .ToListAsync();

//        // Assign relative selection weights — Tokyo gets a slightly higher weight
//        // as the most content-rich wishlist, then Panama City, then San Blas, then Bocas.
//        var weights = new Dictionary<string, double>
//        {
//            { "5-Day Tokyo",        1.5 },
//            { "Panama City",        1.2 },
//            { "San Blas",           1.0 },
//            { "Bocas del Toro",     1.0 },
//        };

//        double GetWeight(string name)
//        {
//            foreach (var kv in weights)
//                if (name.Contains(kv.Key)) return kv.Value;
//            return 1.0;
//        }

//        db.FeaturedWishlistPool.AddRange(
//            wishlists.Select(w => new FeaturedWishlistPool
//            {
//                FeaturedWishlistPoolId = Guid.NewGuid(),
//                WishlistId = w.WishlistId,
//                PoolType = "Editorial",
//                PaidAmount = 0,
//                DailyImpressionLimit = 10000,
//                CurrentImpressionsToday = 0,
//                RandomSelectionWeight = GetWeight(w.WishlistName),
//                LastRotationDate = DateTimeOffset.UtcNow,
//            })
//        );

//        await db.SaveChangesAsync();
//    }

//    // ═══════════════════════════════════════════════════════════════════════════
//    // PRIVATE HELPERS
//    // ═══════════════════════════════════════════════════════════════════════════

//    private static DescriptionJsonDto BuildDesc(
//        string overview, string localPerspective, string directions,
//        string whatToKnow, string thingsToBeWaryOf, string hiddenCost,
//        IEnumerable<string> nearbyComplements, string bestTimeToVisit,
//        string crowdLevel, string accessibility, string idealDuration)
//        => new()
//        {
//            Overview = overview,
//            LocalPerspective = localPerspective,
//            Directions = directions,
//            WhatToKnow = whatToKnow,
//            ThingsToBeWaryOf = thingsToBeWaryOf,
//            HiddenCost = hiddenCost,
//            NearbyComplements = nearbyComplements.ToList(),
//            BestTimeToVisit = bestTimeToVisit,
//            CrowdLevel = crowdLevel,
//            Accessibility = accessibility,
//            IdealDuration = idealDuration,
//        };

//    private static string[] DeriveVibes(IEnumerable<string> tagKeys)
//    {
//        var k = tagKeys.ToHashSet();
//        var vibes = new List<string>();
//        if (k.Contains("food_focused")) vibes.Add("Food");
//        if (k.Contains("cultural")) vibes.Add("Culture");
//        if (k.Contains("nightlife")) vibes.Add("Nightlife");
//        if (k.Contains("nature")) vibes.Add("Nature");
//        if (k.Contains("adventurous")) vibes.Add("Adventure");
//        if (k.Contains("relaxing")) vibes.Add("Relaxation");
//        if (k.Contains("premium")) vibes.Add("Luxury");
//        if (k.Contains("budget_friendly")) vibes.Add("Budget");
//        if (k.Contains("romantic")) vibes.Add("Romance");
//        if (k.Contains("shopping")) vibes.Add("Shopping");
//        if (k.Contains("photography")) vibes.Add("Aesthetic");
//        if (k.Contains("educational")) vibes.Add("Educational");
//        if (!vibes.Any()) vibes.Add("Urban");
//        return vibes.ToArray();
//    }
//}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;

public static class DataSeeder
{
    public static async Task SeedJapanTrip(HodracDbContext db)
    {
        // ─── Lookups ──────────────────────────────────────────────────────────
        var japan = await db.Countries.FirstAsync(c => c.CountryName == "Japan");
        var japanese = await db.Languages.FirstAsync(l => l.LanguageName == "Japanese");
        var yen = await db.Currencies.FirstAsync(c => c.CurrencyCode == "JPY");

        var tokyo = await db.Cities.FirstAsync(c => c.CityName == "Tokyo");
        var asakusaDest = await db.Destinations.FirstAsync(d => d.DestinationName == "Asakusa Streets");
        var sensoJiDest = await db.Destinations.FirstAsync(d => d.DestinationName == "Senso-ji Temple (Asakusa Kannon)");

        var mapping = new Dictionary<string, (string[] categories, string[] tags)>
        {
            // ── Tokyo ──
            ["Shibuya"] = (
                categories: new[] { "neighborhood_district", "entertainment_nightlife", "viewpoint_scenic_spot" },
                tags: new[] { "walkable", "photography", "nightlife", "shopping", "tourist_hotspot" }
            ),
            ["Shinjuku"] = (
                categories: new[] { "neighborhood_district", "entertainment_nightlife" },
                tags: new[] { "nightlife", "tourist_hotspot", "social", "walkable" }
            ),
            ["Ginza Shopping District"] = (
                categories: new[] { "neighborhood_district" },
                tags: new[] { "shopping", "premium", "walkable", "architecture" }
            ),
            ["Tokyo DisneySea"] = (
                categories: new[] { "activity_experience" },
                tags: new[] { "family_friendly", "adventurous", "crowded", "premium" }
            ),
            // ── Fuji Area ──
            ["Chureito Pagoda"] = (
                categories: new[] { "landmark_monument", "viewpoint_scenic_spot" },
                tags: new[] { "photography", "nature", "hidden_gem" }
            ),
            ["Fuji Speedway Museum"] = (
                categories: new[] { "activity_experience" },
                tags: new[] { "educational", "adventurous" }
            ),
            ["Ninja Village (Kawaguchiko)"] = (
                categories: new[] { "activity_experience" },
                tags: new[] { "family_friendly", "adventurous" }
            ),
            // ── Kyoto ──
            ["Fushimi Inari Taisha"] = (
                categories: new[] { "cultural_site", "nature_outdoor" },
                tags: new[] { "cultural", "photography", "history", "walkable", "hidden_gem" }
            ),
            ["Kiyomizu-dera"] = (
                categories: new[] { "cultural_site", "viewpoint_scenic_spot", "landmark_monument" },
                tags: new[] { "cultural", "photography", "history", "architecture", "tourist_hotspot" }
            ),
            ["Nintendo Museum (Uji)"] = (
                categories: new[] { "activity_experience" },
                tags: new[] { "educational", "family_friendly", "hidden_gem" }
            ),
            ["Nishiki Market"] = (
                categories: new[] { "market_street_life", "food_experience" },
                tags: new[] { "food_focused", "walkable", "tourist_hotspot", "shopping" }
            ),
            ["Kyoto Railway Museum"] = (
                categories: new[] { "activity_experience" },
                tags: new[] { "educational", "family_friendly" }
            ),
            // ── Osaka ──
            ["Universal Studios Japan"] = (
                categories: new[] { "activity_experience" },
                tags: new[] { "family_friendly", "adventurous", "crowded", "premium" }
            ),
            ["Super Nintendo World (inside USJ)"] = (
                categories: new[] { "activity_experience" },
                tags: new[] { "family_friendly", "adventurous", "crowded" }
            ),
            ["Dotonbori"] = (
                categories: new[] { "entertainment_nightlife", "food_experience", "neighborhood_district" },
                tags: new[] { "food_focused", "nightlife", "photography", "tourist_hotspot", "social" }
            ),
            ["Osaka Castle"] = (
                categories: new[] { "landmark_monument", "viewpoint_scenic_spot" },
                tags: new[] { "history", "photography", "family_friendly" }
            ),
            ["Shinsekai (Retro District)"] = (
                categories: new[] { "neighborhood_district", "entertainment_nightlife" },
                tags: new[] { "photography", "hidden_gem", "local_favorite", "retro_vibe" }
            ),
            ["Tower Knives Osaka"] = (
                categories: new[] { "food_experience" },
                tags: new[] { "shopping", "educational", "local_favorite" }
            ),
            ["Namba Yasaka Shrine (Lion Head)"] = (
                categories: new[] { "cultural_site" },
                tags: new[] { "quirky", "photography", "local_favorite" }
            ),
            ["Amerikamura (American Village)"] = (
                categories: new[] { "neighborhood_district", "entertainment_nightlife" },
                tags: new[] { "shopping", "walkable", "street_culture" }
            ),
            ["Don Quijote (Discount Store)"] = (
                categories: new[] { "market_street_life" },
                tags: new[] { "shopping", "budget_friendly", "quirky" }
            ),
            ["Umeda Sky Building"] = (
                categories: new[] { "viewpoint_scenic_spot", "landmark_monument" },
                tags: new[] { "photography", "architecture", "romantic" }
            ),
            ["Blue Birds Terrace (Osaka Castle View)"] = (
                categories: new[] { "viewpoint_scenic_spot" },
                tags: new[] { "photography", "relaxing", "hidden_gem" }
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

        var mishima = new City
        {
            CityId = Guid.NewGuid(),
            CityName = "Mishima",
            CountryId = japan.CountryId,
            Latitude = 35.1187,
            Longitude = 138.9182,
            CityDescription = "Gateway city near Mount Fuji, with a historic shrine and easy Shinkansen access."
        };
        var kawaguchiko = new City
        {
            CityId = Guid.NewGuid(),
            CityName = "Kawaguchiko",
            CountryId = japan.CountryId,
            Latitude = 35.4972,
            Longitude = 138.7561,
            CityDescription = "Lake-side town with iconic views of Mount Fuji."
        };
        var kyoto = new City
        {
            CityId = Guid.NewGuid(),
            CityName = "Kyoto",
            CountryId = japan.CountryId,
            Latitude = 35.0116,
            Longitude = 135.7681,
            CityDescription = "Japan's cultural heart, home to thousands of temples and traditional streets."
        };
        var osaka = new City
        {
            CityId = Guid.NewGuid(),
            CityName = "Osaka",
            CountryId = japan.CountryId,
            Latitude = 34.6937,
            Longitude = 135.5023,
            CityDescription = "Vibrant, food-obsessed city with a gritty, playful energy."
        };

        db.Cities.AddRange(mishima, kawaguchiko, kyoto, osaka);
        await db.SaveChangesAsync();

        // ─── Destination GUIDs ──────────────────────────────────────────────
        var destShibuyaId = Guid.NewGuid();
        var destShinjukuId = Guid.NewGuid();
        var destGinzaId = Guid.NewGuid();
        var destDisneySeaId = Guid.NewGuid();
        var destChureitoId = Guid.NewGuid();
        var destFujiSpeedwayId = Guid.NewGuid();
        var destNinjaVillageId = Guid.NewGuid();
        var destFushimiInariId = Guid.NewGuid();
        var destKiyomizuId = Guid.NewGuid();
        var destNintendoMuseumId = Guid.NewGuid();
        var destNishikiMarketId = Guid.NewGuid();
        var destKyotoRailwayId = Guid.NewGuid();
        var destUniversalId = Guid.NewGuid();
        var destSuperNintendoId = Guid.NewGuid();
        var destDotonboriId = Guid.NewGuid();
        var destOsakaCastleId = Guid.NewGuid();
        var destShinsekaiId = Guid.NewGuid();
        var destTowerKnivesId = Guid.NewGuid();
        var destNambaYasakaId = Guid.NewGuid();
        var destAmerikamuraId = Guid.NewGuid();
        var destDonQuijoteId = Guid.NewGuid();
        var destUmedaId = Guid.NewGuid();
        var destBlueBirdsId = Guid.NewGuid();

        // ─── Destinations with full JSON descriptions ──────────────────────
        var newDestinations = new[]
        {
            new Destination
            {
                DestinationId = destShibuyaId,
                DestinationName = "Shibuya",
                CleanNormalizedSearchName = "shibuya",
                MetaphoneCode = "XBY",
                DoubleMetaphonePrimary = "XPY",
                DoubleMetaphoneSecondary = "XBY",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Shibuya is the pulse of Tokyo's youth culture—a sprawling district of neon, fashion, and perpetual motion. Beyond the famous scramble crossing lies a labyrinth of shopping streets (Center-gai, Spain-zaka), iconic department stores (Shibuya 109, Parco), and entertainment complexes that define modern Japan. By day, it is a fashion runway and shopping mecca. By night, it transforms into a pulsing nightlife hub with world‑renowned clubs, intimate izakayas, and the legendary Love Hotel Hill.\n\nWhat to Do: Cross the scramble (it’s a must). Shop for the latest streetwear at Shibuya 109. Visit the Nintendo Tokyo store and Capcom Store inside Parco. Walk the chaotic, narrow Center-gai alley for cheap eats and arcades. Take in the panoramic city views from the rooftop of Shibuya Sky (book ahead). Explore the redeveloped Miyashita Park, which combines a skate park, shopping, and a rooftop food court. And don't miss the tiny bars tucked away in the Dogenzaka side streets for an authentic late‑night experience.",
                  "Directions": "Best Station: Shibuya Station (JR Yamanote Line, Keio Inokashira Line, Tokyo Metro Ginza/Hanzomon/Fukutoshin Lines).\n\nKey Exits: Hachiko Exit (for the crossing and statue), East Exit (for Center-gai and Shibuya 109), West Exit (for Shibuya Stream, Miyashita Park, and the newer developments).\n\nThe Direction Walk: For the classic crossing, take the Hachiko Exit and you are there instantly. For Parco (Nintendo Store), take the East Exit and walk behind Shibuya 109 – it is a 3‑minute walk. For Shibuya Sky, head to the Shibuya Scramble Square building directly above the station (follow the signs for the Observatory).\n\nAddress for Taxi: 渋谷区道玄坂2-2-1 (Shibuya Scramble Square) or 渋谷区宇田川町15-1 (Shibuya Parco).",
                  "WhatToKnow": "Shibuya is divided into clear 'zones'. The north side (Hachiko/Scramble) is the iconic tourist zone. The east side (Center-gai, 109) is the youth shopping and street‑food zone. The west side (Dogenzaka, Bunkamura-dori) is the entertainment, nightclub, and love hotel zone.\n\nShopping Highlights: Shibuya 109 is the epicentre of women's 'gal' fashion. Parco (re‑opened in 2019) houses Nintendo Tokyo, the Capcom Store, and a massive Pokémon Center – it is a geek paradise. Loft and Tokyu Hands are nearby for lifestyle and stationery.\n\nNightlife: Clubs like Womb, Atom, and Circus are world‑famous. Most are hidden in the basement floors of Dogenzaka's backstreets.\n\nViewing Options: Shibuya Sky (¥2,200) on the rooftop of Scramble Square requires advance booking (especially for sunset). The MAGNET by 109 rooftop (¥500) and the Starbucks terrace (buy a drink) are cheaper fallbacks.",
                  "ThingsToBeWaryOf": "The Shibuya Hills: Dogenzaka (Love Hotel Hill) is notoriously steep – walking up it in heels or with heavy bags is a workout. Wear comfortable footwear if you plan to explore the west side.\n\nThe Underpass Maze: The underground walkways connecting the station exits can be disorienting. It is often faster to walk above ground using the large pedestrian bridges.\n\nTouts in Dogenzaka: Like Shinjuku, there are touts in the nightlife alleys. Politely decline any invitations to obscure bars or clubs, as they often come with hidden cover charges.\n\nWeekend Gridlock: Center-gai becomes a walking traffic jam on Saturdays and holidays. Avoid if you dislike dense crowds.",
                  "LocalPerspective": "The 'Hachiko‑mae' Meeting Point: Saying 'Hachiko' is the universal meeting code in Tokyo. Locals also say 'Hachi‑bashi' (Hachiko bridge) for the bus stop area just outside the station.\n\nThe Marathon Spectator Spot: The Tokyo Marathon passes through Shibuya, and the crossing is one of the loudest, most energetic cheering points on the entire route.\n\nFashion Runway: On weekends, the area around the crossing becomes an impromptu catwalk, especially for Halloween and New Year's Eve when all of Tokyo congregates here for the massive, unorganised street parties.",
                  "HiddenCost": "Shibuya Sky: ¥2,200 ($14.50) – book online in advance.\nMAGNET Rooftop: ¥500 ($3.30).\nStarbucks Drink: ¥500–¥800 ($3.30–$5.30) – required for the upstairs window seat.\nShopping Budget: ¥5,000–¥50,000 ($33–$330) – it is dangerously easy to blow your budget on clothes and anime merch.\nClub Cover Charge: ¥2,000–¥4,000 ($13–$26) – usually includes 1‑2 drinks.\nCoin Lockers: ¥300–¥700 ($2–$4.70) – essential if you are carrying shopping bags or luggage.\nParking: ¥1,000/hr – absolutely avoid driving here.",
                  "NearbyComplements": [
                    "Harajuku / Takeshita Street: A 15‑minute walk through the iconic Jingu‑bashi bridge.",
                    "Omotesando: Tree‑lined avenue with luxury boutiques and stunning modern architecture.",
                    "Yoyogi Park: A massive green oasis perfect for picnics and people‑watching.",
                    "Ebisu: A 10‑minute train ride for great craft beer and a quieter, more mature vibe."
                  ],
                  "BestTimeToVisit": "Afternoon (2:00 PM – 5:00 PM) – for the best shopping energy and bright daylight photos.\nTwilight (5:00 PM – 7:00 PM) – to witness the neon transition and catch sunset from Shibuya Sky.\nLate Night (11:00 PM – 3:00 AM) – for the clubbing and chaotic nightlife energy.\nHalloween: Shibuya is the epicentre of Tokyo's Halloween street party – absolutely chaotic but unforgettable.",
                  "crowdLevel": "Maximum (10/10) – it is perpetually packed, especially on weekends, holidays, and during the evening 'golden hour'.",
                  "Accessibility": "Rating: 7/10\n\nThe station and main crossing area are flat and well‑paved. However, the side districts (Dogenzaka, Spain‑zaka, and the backstreets) feature steep inclines and numerous stairs. Wheelchair users should stick to the main crossing and the Scramble Square complex, which has excellent elevator access. Strollers are possible but will struggle in the narrow, congested Center‑gai.",
                  "IdealDuration": "3 to 5 hours – this allows enough time to shop, see the view, cross the scramble a few times, and explore the vibrant side streets."
                }
                """,
                AverageCostPerDay = 0m,
                LuxuryRating = DeriveLuxury(0m, mapping["Shibuya"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Shibuya"].tags),
                AdventurePaceScore = AdventureScore(mapping["Shibuya"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Shibuya"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Shibuya"].tags),
                Latitude = 35.6595,
                Longitude = 139.7004,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Shinjuku ──
            new Destination
            {
                DestinationId = destShinjukuId,
                DestinationName = "Shinjuku",
                CleanNormalizedSearchName = "shinjuku",
                MetaphoneCode = "XNJK",
                DoubleMetaphonePrimary = "XNJK",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Shinjuku is Tokyo's chaotic entertainment hub – a city within a city. By day, it is a massive business and shopping district. By night, the neon signs of Omoide Yokocho (Memory Lane) and Golden Gai flicker to life, casting a nostalgic glow over narrow alleys packed with tiny bars and yakitori stalls.\n\nWhat to Do: Hit the Tokyo Metropolitan Government Building for free 360° views. Get lost in the labyrinth of underground shopping malls. Then, after dark, squeeze into a 5-seat bar in Golden Gai.",
                  "Directions": "Best Station: Shinjuku Station – the world's busiest. It is a maze. \n\nKey Exit: For the free observatory, take the Tochomae exit and walk 10 minutes north. For nightlife, exit via the West Exit or East Exit.\n\nThe Direction Walk: For Golden Gai: Exit via the East Exit, walk past the Uniqlo towards Kabukicho. You will see the archway.\n\nAddress for Taxi: 新宿区歌舞伎町1-1 (Kabukicho entrance) or 新宿区西新宿2-8-1 (Metropolitan Government Bldg).",
                  "WhatToKnow": "Free Observatory: The TMG building is free and open until 11:00 PM. It beats the Tokyo Skytree price.\n\nLabyrinth Warning: Shinjuku Station has 200+ exits. If you miss your intended exit, it may take 15 minutes to walk underground to the correct one.\n\nGolden Gai Etiquette: Many bars charge a cover fee (¥500–¥1,000) and have strict rules – no photos without permission, and usually only 1 drink minimum.",
                  "ThingsToBeWaryOf": "Touts in Kabukicho: They may invite you to bars with cheap drinks. These are often scams with exorbitant surcharges. Politely say \"No\" and walk away.\n\nOmoide Yokocho: Very narrow and crowded. Beware of your elbows near the open grills.\n\nLost Wandering: Getting lost in the station is almost a rite of passage. Give yourself 15 extra minutes to find your exit.",
                  "LocalPerspective": "The \"Shinjuku Divide\": The east side is youth culture (shopping, anime). The west side is salaryman territory (skyscrapers, government). The north side is the red-light district.\n\nGolden Gai Socializing: The bars here are micro-communities. If you speak a little Japanese, the regulars will be incredibly warm and chatty.\n\nGodzilla: Look up at the Hotel Gracery – a giant Godzilla head peers over the building. It roars at certain hours.",
                  "HiddenCost": "Golden Gai Cover Charge: ¥500–¥1,000 ($3.30–$6.70) per bar. It adds up if you bar-hop.\n\nDrinks in Omoide Yokocho: ¥600–¥1,000 ($4–$6.70) per beer or yakitori skewer.\n\nTMG Observatory: Free (¥0) – the best deal in Tokyo.\n\nShinjuku Go-Kart: ¥8,000–¥12,000 ($50–$80) – the Mario Kart street experience, but requires an international driver's permit.",
                  "NearbyComplements": ["Kabukicho: The iconic neon red-light district.", "Omoide Yokocho: A post-WWII alley of tiny smoke-filled eateries.", "Golden Gai: A cluster of over 200 tiny bars.", "Shinjuku Gyoen National Garden: A massive, tranquil park – the perfect daytime contrast."],
                  "BestTimeToVisit": "Nighttime (6:00 PM – 1:00 AM) – the neon is essential.\n\nLate Night (2:00 AM – 4:00 AM) – the alleys get quieter and more atmospheric.\n\nWeekday afternoons – for shopping without the weekend crush.",
                  "crowdLevel": "Maximum (10/10) – Shinjuku Station is the busiest in the world. The nightlife areas are packed until the last train.",
                  "Accessibility": "Rating: 6/10\n\nThe station has stairs and escalators, but the sheer scale is overwhelming. Elevators exist but require a map. The streets are flat, but the alleys are tight and often have steps.",
                  "IdealDuration": "2 to 4 hours – you could spend a full day here, but 2 hours covers the free observatory and a quick wander through the nightlife alleys."
                }
                """,
                AverageCostPerDay = 2000m,
                LuxuryRating = DeriveLuxury(2000m, mapping["Shinjuku"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Shinjuku"].tags),
                AdventurePaceScore = AdventureScore(mapping["Shinjuku"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Shinjuku"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Shinjuku"].tags),
                Latitude = 35.6895,
                Longitude = 139.6917,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 2,
                CountryId = japan.CountryId
            },

            // ── Ginza ──
            new Destination
            {
                DestinationId = destGinzaId,
                DestinationName = "Ginza Shopping District",
                CleanNormalizedSearchName = "ginza shopping",
                MetaphoneCode = "JNSHPNG",
                DoubleMetaphonePrimary = "JNSHPNK",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Ginza is Tokyo's upscale playground – the equivalent of Fifth Avenue or the Champs-Élysées. Luxury flagship stores (Louis Vuitton, Hermès, Dior) occupy architecturally stunning buildings, while historic department stores like Mitsukoshi and Wako anchor the neighborhood. On weekends, the main street (Chuo-dori) closes to traffic, turning it into a massive pedestrian promenade.",
                  "Directions": "Best Station: Ginza Station (Tokyo Metro Ginza, Hibiya, Marunouchi Lines).\n\nKey Exit: Exit A3 (for Wako and Mitsukoshi).\n\nThe Direction Walk: From Exit A3, you are on Chuo-dori. Walk north for the high-end boutiques. Walk south towards the Kabuki-za theatre.\n\nAddress for Taxi: 中央区銀座4-6-16 (Mitsukoshi Ginza).",
                  "WhatToKnow": "Weekend Pedestrian Paradise: On Saturdays, Sundays, and national holidays from 12:00 PM – 5:00 PM (or 6:00 PM in summer), Chuo-dori becomes a car-free zone – perfect for strolling and street photography.\n\nArchitecture: Look for the glass facade of the Hermès store (designed by Renzo Piano) and the art deco Wako building with its famous clock tower.\n\nShopping Tax Refund: Many stores offer tax-free shopping for tourists (over ¥5,500). Bring your passport.",
                  "ThingsToBeWaryOf": "Expensive: This is not budget shopping. A coffee at a department store café can cost ¥1,500.\n\nWeekday Traffic: The streets are busy with cars, so stick to the pavement.\n\nKabuki-za: If you want to see Kabuki, tickets sell out fast. You can buy single-act tickets (makumi) for about ¥1,000–¥2,000 on the day.",
                  "LocalPerspective": "The \"Gin-bura\" Walk: Locals coin the term \"Gin-bura\" (Ginza strolling) as a sophisticated leisure activity. Window shopping here is a legitimate cultural pastime.\n\nMitsukoshi Basement (Depachika): Don't skip the basement food hall. It is a wonderland of beautifully packaged Japanese sweets, pickles, and prepared foods.\n\nAfternoon Tea: The Peninsula or the Ritz-Carlton offer classic high tea with a view – a beloved local indulgence.",
                  "HiddenCost": "Kabuki-za Single Act Ticket: ¥1,000–¥2,000 ($6.70–$13.30) – stand in line early.\n\nTea at a Department Store Salon: ¥1,500–¥3,000 ($10–$20).\n\nTax-Free Processing: Free, but bring your passport.\n\nParking: ¥2,000/hour – don't drive here.",
                  "NearbyComplements": ["Hibiya Park: A 10-minute walk for a green escape.", "Tsukiji Outer Market: Walk 15 minutes south for sushi breakfast.", "Yurakucho: The area under the train tracks is packed with tiny yakitori joints and izakayas."],
                  "BestTimeToVisit": "Weekend Afternoon – for the pedestrian paradise.\n\nWeekday Evenings – for the illuminated flagship stores.\n\nSpring (Sakura) – for the cherry blossom decorations on department store windows.",
                  "crowdLevel": "High (8/10) on weekends, Moderate on weekdays.",
                  "Accessibility": "Rating: 10/10 – wide pavements, accessible stations, elevator-friendly stores.",
                  "IdealDuration": "1.5 to 3 hours – window shop, visit the department store basement, and have a drink."
                }
                """,
                AverageCostPerDay = 5000m,
                LuxuryRating = DeriveLuxury(5000m, mapping["Ginza Shopping District"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Ginza Shopping District"].tags),
                AdventurePaceScore = AdventureScore(mapping["Ginza Shopping District"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Ginza Shopping District"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Ginza Shopping District"].tags),
                Latitude = 35.6715,
                Longitude = 139.7640,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── DisneySea ──
            new Destination
            {
                DestinationId = destDisneySeaId,
                DestinationName = "Tokyo DisneySea",
                CleanNormalizedSearchName = "tokyo disneysea",
                MetaphoneCode = "TKSNS",
                DoubleMetaphonePrimary = "TKSNS",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Tokyo DisneySea is the only Disney park of its kind in the world – entirely nautical and mythologically themed. Instead of a castle, it has a massive volcano (Mt. Prometheus) that erupts in fire. It is designed for adults as much as children, blending high-budget theatrical shows with meticulously detailed \"ports\" (Mysterious Island, Arabian Coast, Mermaid Lagoon, etc.).\n\nWhat to Do: Ride Journey to the Center of the Earth (a thrilling indoor coaster). Watch the evening water spectacle, \"Believe! Sea of Dreams\". And absolutely – ride the Toy Story Mania and Indiana Jones rides.",
                  "Directions": "Best Station: Maihama Station (JR Keiyo Line).\n\nThe Direction Walk: From Maihama Station, exit for the Disney Resort. You can either walk 10 minutes or take the Disney Resort Line monorail (paid). The monorail stops at DisneySea directly.\n\nAddress for Taxi: 千葉県浦安市舞浜1-1 (Tokyo DisneySea).",
                  "WhatToKnow": "Unique Liquor License: Unlike all other Disney parks, DisneySea serves alcohol – try the signature sparkling cocktails in the Mediterranean Harbor.\n\nFood Quality: The dining is far superior to other Disney parks. The Zambini Brothers' Ristorante has decent Italian, and the Teddy Roosevelt Lounge (on the steamship) is a hidden bar.\n\nTicket Strategy: Buy tickets online in advance. The park is often capacity-capped. Arrive 45 minutes before opening to be among the first through the gate.",
                  "ThingsToBeWaryOf": "Insane Lines: Wait times can exceed 2-3 hours for popular rides. Use the Disney Premier Access (paid fastpass) for the big rides.\n\nExhaustion: The park is huge (larger than Magic Kingdom). Wear your most comfortable walking shoes.\n\nWeather: It is right on Tokyo Bay – it can get surprisingly windy and chilly in the evening, even in summer.",
                  "LocalPerspective": "The \"Shokudo\" Quality: Japanese guests are intense about food photography. The popcorn buckets (especially the ride-shaped ones) are collectible items. People queue for 45 minutes just for a specific shaped bucket.\n\nTower of Terror: The story here is different from the American version – it involves a cursed explorer, making it more eerie and less comical.",
                  "HiddenCost": "Park Ticket: ¥8,400–¥9,800 ($56–$65) per adult.\n\nDisney Premier Access: ¥2,000–¥3,500 ($13–$23) per ride – essential for Soaring and Journey.\n\nMonorail Ticket: ¥260 ($1.70) per ride.\n\nPopcorn Buckets: ¥3,000–¥5,000 ($20–$33) – yes, they are that expensive and collectible.\n\nMeals: ¥1,500–¥4,000 ($10–$26) for a meal.\n\nPhoto Service: ¥1,500 for digital downloads.",
                  "NearbyComplements": ["Ikspiari: The shopping mall at Maihama Station – perfect for dinner after the park closes."],
                  "BestTimeToVisit": "Weekdays (Tuesday–Thursday) – slightly less crowded.\n\nMid-January to Mid-February – the off-season.\n\nArrive 1 hour before opening to beat the main wave.",
                  "crowdLevel": "Maximum (10/10) – it is notoriously the most crowded Disney park in the world on weekends and holidays.",
                  "Accessibility": "Rating: 9/10 – the park is extremely wheelchair/stroller friendly, with accessible ride entrances and seamless pathways.",
                  "IdealDuration": "Full Day (10+ hours) – you cannot see it all in one day. Prioritize 3-4 major rides."
                }
                """,
                AverageCostPerDay = 8400m,
                LuxuryRating = DeriveLuxury(8400m, mapping["Tokyo DisneySea"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Tokyo DisneySea"].tags),
                AdventurePaceScore = AdventureScore(mapping["Tokyo DisneySea"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Tokyo DisneySea"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Tokyo DisneySea"].tags),
                Latitude = 35.6263,
                Longitude = 139.8894,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Chureito Pagoda ──
            new Destination
            {
                DestinationId = destChureitoId,
                DestinationName = "Chureito Pagoda",
                CleanNormalizedSearchName = "chureito pagoda",
                MetaphoneCode = "XRT PGT",
                DoubleMetaphonePrimary = "XRT PGT",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "This five-story pagoda is the quintessential postcard image of Japan. Sitting on the slope of a hill in Fujiyoshida, it offers a breathtaking frame of the vermillion pagoda with the iconic snow-capped peak of Mount Fuji directly behind it. The climb up the 398 steps is a pilgrimage for photographers, and the payoff is one of the most rewarding views in the country.\n\nWhat to Do: Climb the stairs, take the classic photo from the main observatory deck, and explore the nearby Arakura Sengen Shrine at the base. In late March, the cherry blossoms make the scene surreal.",
                  "Directions": "Best Station: Fujiyoshida Station (Fujikyuko Line).\n\nThe Direction Walk: From Fujiyoshida Station, walk 15 minutes towards the pagoda. You will see the large torii gate at the base. Alternatively, take a taxi from Kawaguchiko Station (approx. 10 minutes, ¥1,500).\n\nAddress for Taxi: 山梨県富士吉田市浅間2-4-1 (Arakura Sengen Shrine).",
                  "WhatToKnow": "The 398 Steps: It is a steep climb – but there are benches along the way if you need a break.\n\nPhotography Window: The pagoda and Fuji are best captured between November and February when the air is crisp and the snow cap is massive. In summer, Fuji is often bare, and the haze can obscure the mountain.\n\nFestival Time: In late April, the Fuji Shibazakura Festival takes place nearby, adding pink moss phlox to the foreground.",
                  "ThingsToBeWaryOf": "Fuji Not Visible: As Rok noted, the mountain is notoriously shy. If it is cloudy, you may not see it at all – check the live webcams before heading up.\n\nSunset Rush: The platform gets packed with photographers with tripods 2 hours before sunset. Arrive early to claim a spot.\n\nWinter Ice: The steps can be icy in December–February. Wear good boots.",
                  "LocalPerspective": "The \"Chureito\" Name: It means \"memorial pagoda,\" dedicated to war dead. It is a deeply respectful site, so maintain a quiet, solemn demeanor on the grounds.\n\nThe Cherry Blossom Corridor: The path leading to the pagoda is lined with cherry trees. During full bloom, the petals fall like snow, creating a magical pink carpet.",
                  "HiddenCost": "Entry: Free (¥0).\n\nShrine Purchase: Donate ¥100–¥500 for prayer or to purchase an omamori.\n\nParking: ¥500 ($3.30) – if you rent a car.\n\nIce Cream: ¥400 ($2.60) – there is a small vending machine at the base.",
                  "NearbyComplements": ["Fuji-Q Highland: A famous amusement park 10 minutes away with rollercoasters that have views of Fuji.", "Lake Kawaguchiko: A 15-minute drive for the classic lake reflection photos.", "Oshino Hakkai: A spring village 20 minutes away."],
                  "BestTimeToVisit": "Mid-November to Mid-February – highest visibility and snow-capped Fuji.\n\nSunrise (5:00 AM – 7:00 AM) – the sun rises behind Fuji, and there are zero crowds.\n\nSunset (4:00 PM – 5:30 PM) – dramatic orange light, but crowded.",
                  "crowdLevel": "Medium (5/10) on weekdays, High (8/10) on weekends and during autumn.",
                  "Accessibility": "Rating: 4/10 – 398 steep steps. Not suitable for wheelchairs or strollers. There is no alternative path.",
                  "IdealDuration": "1 to 1.5 hours – 30 min climb, 30 min photos, 15 min descent."
                }
                """,
                AverageCostPerDay = 0m,
                LuxuryRating = DeriveLuxury(0m, mapping["Chureito Pagoda"].tags),
                AccessibilityType = "Car",
                FamilyFriendlyScore = FamilyScore(mapping["Chureito Pagoda"].tags),
                AdventurePaceScore = AdventureScore(mapping["Chureito Pagoda"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Chureito Pagoda"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Chureito Pagoda"].tags),
                Latitude = 35.4610,
                Longitude = 138.7971,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Fuji Speedway Museum ──
            new Destination
            {
                DestinationId = destFujiSpeedwayId,
                DestinationName = "Fuji Speedway Museum",
                CleanNormalizedSearchName = "fuji speedway museum",
                MetaphoneCode = "FJSPD MSM",
                DoubleMetaphonePrimary = "FJSPT MSM",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Tucked near the base of Mount Fuji, this museum is a pilgrimage site for motorsport enthusiasts. It houses a stunning collection of historic race cars – from vintage Formula 1 machines to iconic Japanese sports cars (Skyline GT-R, NSX, Supra). The crown jewel is the original 1960s banked track, which you can drive or ride on (simulator).\n\nWhat to Do: Walk through the showroom of pristine vehicles, visit the Hall of Fame, and take a drive on the 1.5km historic circuit in a rental sports car (with a guide).",
                  "Directions": "Best Access: By rental car from Kawaguchiko (15 minutes drive).\n\nBy Bus: Take the Fuji-Q Highland bus from Kawaguchiko Station and get off at Fuji Speedway (check schedule).\n\nAddress for Taxi: 静岡県駿東郡小山町中日向694 (Fuji Speedway).",
                  "WhatToKnow": "The Museum has interactive simulators where you can feel the G-forces. If you have an international driver's license, you can rent a lightweight sports car (e.g., GR86) and drive the actual historic track for a surprisingly affordable fee.\n\nThe cafe on site has a window facing the track – great for watching amateur races.",
                  "ThingsToBeWaryOf": "Event Days: If there is a major race, the museum may be closed to the public or require an event ticket.\n\nLocation: It is in the middle of nowhere – no convenience stores nearby. Bring snacks.\n\nLanguage: English signage is limited, but the staff are helpful with translation apps.",
                  "LocalPerspective": "The \"Fuji 1000km\": This was a legendary endurance race that brought international champions to Japan. Locals revere this track as the spiritual home of Japanese racing.\n\nInitial D Vibe: For fans of the anime, this area is surrounded by the famous Hakone Turnpike and ashinyo (touge) passes.",
                  "HiddenCost": "Museum Entry: ¥1,500 ($10).\n\nSimulator: ¥1,000–¥2,000 ($6.70–$13.30).\n\nTrack Rental (GR86): ¥15,000–¥20,000 ($100–$133) for 20 mins.\n\nParking: Free.",
                  "NearbyComplements": ["Fuji-Q Highland: Combine with a half-day at the amusement park.", "Gotemba Premium Outlets: 20 minutes away for shopping with Fuji views."],
                  "BestTimeToVisit": "Weekday mornings – the track is quiet, and you can get a slot for the rental drive immediately.",
                  "crowdLevel": "Low (2/10) – a niche destination.",
                  "Accessibility": "Rating: 9/10 – flat, spacious, and elevators.",
                  "IdealDuration": "1.5 to 2 hours."
                }
                """,
                AverageCostPerDay = 1500m,
                LuxuryRating = DeriveLuxury(1500m, mapping["Fuji Speedway Museum"].tags),
                AccessibilityType = "Car",
                FamilyFriendlyScore = FamilyScore(mapping["Fuji Speedway Museum"].tags),
                AdventurePaceScore = AdventureScore(mapping["Fuji Speedway Museum"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Fuji Speedway Museum"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Fuji Speedway Museum"].tags),
                Latitude = 35.3723,
                Longitude = 138.8387,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Ninja Village ──
            new Destination
            {
                DestinationId = destNinjaVillageId,
                DestinationName = "Ninja Village (Kawaguchiko)",
                CleanNormalizedSearchName = "ninja village",
                MetaphoneCode = "NJN VLLJ",
                DoubleMetaphonePrimary = "NJN FLJ",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "A purpose-built ninja theme park right by Lake Kawaguchiko. It features obstacle courses, shuriken throwing, and a live ninja show where actors perform high-flying stunts with swords and chain-sickles. It is a bit cheesy, but the kids absolutely love it.\n\nWhat to Do: Throw shuriken at the target range, navigate the maze, watch the 20-minute live action show, and dress up in ninja costumes for a photo shoot.",
                  "Directions": "Best Access: By rental car (10 minutes from Kawaguchiko station).\n\nBus: There is a local sightseeing bus (Retro Bus) that stops nearby – ask for \"Ninja Mura\" stop.\n\nAddress for Taxi: 南都留郡富士河口湖町河口2008.",
                  "WhatToKnow": "Shows run every hour on the hour. The costume rental is included in the ticket price.\n\nThe maze is surprisingly tricky – it takes about 10-15 minutes to escape.",
                  "ThingsToBeWaryOf": "Cheesy Factor: This is not a historical museum. It is a kids' attraction. If you are a serious history buff, you may find it gimmicky.\n\nRain: Much of the attraction is outdoors – check the weather.",
                  "LocalPerspective": "The staff are often retired stuntmen from the big samurai films. The combat choreography is genuinely respectable.\n\nIn Japanese culture, the ninja are more myth than historical reality – this village leans heavily into the pop-culture version.",
                  "HiddenCost": "Entry: ¥2,000 ($13) for adults, ¥1,000 for kids.\n\nNinja Costume Rental: Included.\n\nFood: ¥500–¥1,000 for simple bento boxes.",
                  "NearbyComplements": ["Lake Kawaguchiko: Rent a swan boat or take the ropeway up Mt. Tenjo for a panoramic Fuji shot."],
                  "BestTimeToVisit": "Late morning (10:00 AM – 11:00 AM) to catch the first show before the crowds.",
                  "crowdLevel": "Medium (5/10) – popular with domestic family tourists.",
                  "Accessibility": "Rating: 8/10 – mostly flat gravel paths. Strollers are fine.",
                  "IdealDuration": "1.5 to 2 hours."
                }
                """,
                AverageCostPerDay = 2000m,
               LuxuryRating = DeriveLuxury(2000m, mapping["Ninja Village (Kawaguchiko)"].tags),
                AccessibilityType = "Car",
                FamilyFriendlyScore = FamilyScore(mapping["Ninja Village (Kawaguchiko)"].tags),
                AdventurePaceScore = AdventureScore(mapping["Ninja Village (Kawaguchiko)"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Ninja Village (Kawaguchiko)"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Ninja Village (Kawaguchiko)"].tags),
                Latitude = 35.4983,
                Longitude = 138.7645,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Fushimi Inari ──
            new Destination
            {
                DestinationId = destFushimiInariId,
                DestinationName = "Fushimi Inari Taisha",
                CleanNormalizedSearchName = "fushimi inari",
                MetaphoneCode = "FXM N R",
                DoubleMetaphonePrimary = "FXM N R",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "This is the most iconic shrine in Japan. The path climbs up Mount Inari through a tunnel of over 10,000 vibrant orange torii gates, which are donated by businesses and individuals. The atmosphere is otherworldly – the sun filters through the vermillion gates, casting a warm, mystical glow. It is not just a shrine; it is a 4km forest hike with thousands of fox statues (messengers of Inari) and small sub-shrines along the way.\n\nWhat to Do: Hike the full loop. It takes 2-3 hours to reach the summit and come back. Stop at the halfway point for a panoramic view of Kyoto. Eat some roasted fox-shaped tofu at the tea houses.",
                  "Directions": "Best Station: Inari Station (JR Nara Line).\n\nKey Exit: The station exit is directly opposite the shrine's main entrance.\n\nThe Direction Walk: Cross the street, walk under the massive Romon gate, and you are at the starting point of the torii path.\n\nAddress for Taxi: 京都市伏見区深草薮之内町68.",
                  "WhatToKnow": "Go Early: Rok rated it 9/10 and said \"go early unless you enjoy crowds.\" At 6:00 AM, you have the gates mostly to yourself. At 10:00 AM, you are in a conga line.\n\nThe Loop: The main path is a loop. Most tourists turn back at the halfway point – push to the summit for a peaceful ending.\n\nFox Statues: The kitsune (fox) statues hold a key in their mouth. They are the messengers of the rice god.",
                  "ThingsToBeWaryOf": "Monkeys and Wild Boars: There are monkeys and occasionally wild boars in the woods. Do not stray off the main path.\n\nMosquitoes: There are mosquitoes in the summer. Bring repellent.\n\nSteep Steps: The second half of the hike has steep, uneven stone steps. Wear proper shoes.",
                  "LocalPerspective": "The \"Senbon Torii\" (Thousand Gates): The gates are donated by companies. The cost of a gate starts at ¥400,000. The names of the donors are inscribed on the back.\n\nThe Tea Houses: At the halfway point, there are small teahouses run by local families. They sell \"kitsune soba\" (soba with fried tofu) – a must-eat.",
                  "HiddenCost": "Entry: Free (¥0).\n\nEma (Fox-shaped): ¥500 ($3.30).\n\nOmamori: ¥500–¥1,000.\n\nTea House Snacks: ¥500–¥1,000 ($3.30–$6.70).\n\nParking: ¥500 near the station.",
                  "NearbyComplements": ["Tofukuji Temple: A 15-minute walk for a huge Zen temple with a beautiful garden.", "Fushimi Sake District: A 20-minute walk south for sake breweries."],
                  "BestTimeToVisit": "Sunrise (6:00 AM – 7:00 AM) – absolutely essential.\n\nLate Afternoon (4:00 PM – 6:00 PM) – the evening light is magical, but it will be busier.\n\nAutumn (November) – the foliage contrasts beautifully with the vermillion gates.",
                  "crowdLevel": "Maximum (10/10) between 9 AM – 3 PM. Low (3/10) at sunrise.",
                  "Accessibility": "Rating: 3/10 – steep stairs, rough paths, and a long hike. Not accessible for wheelchairs beyond the first 100 meters.",
                  "IdealDuration": "2 to 3 hours for the full loop. 1 hour if you just want the photo at the beginning."
                }
                """,
                AverageCostPerDay = 0m,
                LuxuryRating = DeriveLuxury(0m, mapping["Fushimi Inari Taisha"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Fushimi Inari Taisha"].tags),
                AdventurePaceScore = AdventureScore(mapping["Fushimi Inari Taisha"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Fushimi Inari Taisha"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Fushimi Inari Taisha"].tags),
                Latitude = 34.9671,
                Longitude = 135.7727,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Kiyomizu-dera ──
            new Destination
            {
                DestinationId = destKiyomizuId,
                DestinationName = "Kiyomizu-dera",
                CleanNormalizedSearchName = "kiyomizu-dera",
                MetaphoneCode = "KYMS TR",
                DoubleMetaphonePrimary = "KYMS TR",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Kiyomizu-dera is the temple that defines Kyoto. Perched on the edge of a hill, its massive wooden stage offers an unparalleled view over the city's cherry and maple trees. The architecture is miraculous – the main hall was built without a single nail. Rok rated it 1000/10, calling it the \"Kyoto they imagined\" – a perfect blend of traditional streets, epic views, and unreal architecture.\n\nWhat to Do: Stand on the wooden stage and look down (it is a 13-meter drop!). Visit the Jishu Shrine within the complex, where you try to walk between two love stones with your eyes closed. Drink from the Otowa Waterfall's three streams for health, longevity, and academic success.",
                  "Directions": "Best Access: Bus #100 or #206 from Kyoto Station to Gojo-zaka or Kiyomizu-michi stop.\n\nThe Direction Walk: If you get off at Gojo-zaka, it is a 15-minute uphill walk through the historic Sannen-zaka slope, which is lined with wooden merchant houses.\n\nAddress for Taxi: 東山区清水1-294.",
                  "WhatToKnow": "The Stage: The stage is 13 meters above the ground. The saying \"to jump from the stage of Kiyomizu\" is the Japanese equivalent of \"taking a leap of faith\".\n\nThe Waterfall: The three streams represent love, health, and success. Choose wisely – you cannot drink from all three, as it is considered greedy.\n\nSannen-zaka: The street leading up to the temple is the most photogenic in Kyoto. It is a steep slope with tea houses and pottery shops.",
                  "ThingsToBeWaryOf": "Crowd Size: It is packed. Truly packed. The wooden stage can feel like a concert crowd.\n\nWear Good Shoes: The climb up Sannen-zaka is steep cobblestone. High heels are a bad idea.\n\nTicket Lines: The ticket queue can be long. Arrive before 8:30 AM to avoid it.",
                  "LocalPerspective": "The \"Love Stones\": At the Jishu Shrine, there are two stones 10 meters apart. If you walk from one to the other with your eyes closed, you will find true love. (Most people have a friend guiding them).\n\nThe Autumn Night Illumination: In November, the temple is lit up at night. The maple leaves against the wooden stage and the city lights below is an unforgettable sight.",
                  "HiddenCost": "Entry: ¥400 ($2.60) for the main hall.\n\nLove Stones: Free.\n\nDrinks from Otowa Waterfall: ¥200 ($1.30) for a cup.\n\nOmamori: ¥500–¥1,000.\n\nTea House on the Slope: ¥800–¥1,500 for matcha and sweets.",
                  "NearbyComplements": ["Sannen-zaka & Ninen-zaka: The iconic slopes.", "Yasaka Pagoda: A 10-minute walk for a classic photo.", "Gion District: A 15-minute walk for evening geisha spotting."],
                  "BestTimeToVisit": "7:00 AM – 8:00 AM (opens at 6:00 AM in summer) – before the tour buses.\n\nLate Autumn (mid-November) – night illuminations.\n\nSpring (late March) – cherry blossoms framing the stage.",
                  "crowdLevel": "Maximum (10/10) – one of the most visited temples in Japan.",
                  "Accessibility": "Rating: 5/10 – steep hills and steps. There is a wheelchair path, but it takes you to a side view, not the main stage.",
                  "IdealDuration": "1.5 to 2 hours – includes the temple, the waterfall, and a stroll down the slope."
                }
                """,
                AverageCostPerDay = 400m,
                LuxuryRating = DeriveLuxury(400m, mapping["Kiyomizu-dera"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Kiyomizu-dera"].tags),
                AdventurePaceScore = AdventureScore(mapping["Kiyomizu-dera"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Kiyomizu-dera"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Kiyomizu-dera"].tags),
                Latitude = 34.9948,
                Longitude = 135.7851,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Nintendo Museum ──
            new Destination
            {
                DestinationId = destNintendoMuseumId,
                DestinationName = "Nintendo Museum (Uji)",
                CleanNormalizedSearchName = "nintendo museum",
                MetaphoneCode = "NNTND MSM",
                DoubleMetaphonePrimary = "NNTNT MSM",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Located in Uji (just south of Kyoto), this museum is a dream for gamers. It chronicles the history of Nintendo from its origins as a playing card company in 1889 to the modern Switch era. The exhibits are heavily interactive, showcasing classic consoles, unreleased prototypes, and a massive display of every game cartridge ever made.\n\nWhat to Do: Play games on the original Famicom, SNES, and N64 in the massive play area. Take a photo with the giant Mario statue. Explore the Hanafuda (flower card) room.",
                  "Directions": "Best Station: Uji Station (JR Nara Line).\n\nThe Direction Walk: From Uji Station, it is a 15-minute walk across the Uji River. Follow the signs towards the Byodo-in Temple area.\n\nAddress for Taxi: 京都府宇治市小倉町神楽田102.",
                  "WhatToKnow": "Ticket Lottery: Rok mentioned hard to get tickets. You must enter a lottery or queue on the official website 2 months in advance. Tickets are not sold at the door.\n\nInteractive Experience: You get a digital card when you enter, which you scan at various interactive games. It tracks your high scores.\n\nThe Cafe: There is a Nintendo-themed cafe with character lattes (Mario, Luigi, Kirby) and themed meals.",
                  "ThingsToBeWaryOf": "Strict Times: You are allocated a specific entry time. If you are late, you lose your slot.\n\nNo Photography in some areas: They strictly prohibit filming in certain prototype halls.\n\nVibe: It is more of a corporate museum than a theme park – expect reading and quiet appreciation.",
                  "LocalPerspective": "The building itself used to be a Nintendo factory. Locals remember the smell of paint and cardboard from the playing card era.\n\nHanafuda Cards: These traditional flower cards are still produced. Locals appreciate that Nintendo respects its heritage.",
                  "HiddenCost": "Entry: ¥3,300 ($22) for adults.\n\nCafe Food: ¥1,000–¥2,000 ($6.70–$13.30).\n\nMerch: ¥2,000–¥10,000 ($13–$66) – exclusive plushes and keychains.",
                  "NearbyComplements": ["Byodo-in Temple: A 5-minute walk – the temple featured on the 10-yen coin.", "Uji River: Cross the bridge for a classic Japanese riverscape."],
                  "BestTimeToVisit": "Whenever you win the lottery! But off-season weekdays (Jan-Feb) are easiest.",
                  "crowdLevel": "Controlled Medium (5/10) – tickets are limited, so it never feels cramped.",
                  "Accessibility": "Rating: 9/10 – modern building with elevators and wide halls.",
                  "IdealDuration": "2 to 3 hours."
                }
                """,
                AverageCostPerDay = 2000m,
                 LuxuryRating = DeriveLuxury(2000m, mapping["Nintendo Museum (Uji)"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Nintendo Museum (Uji)"].tags),
                AdventurePaceScore = AdventureScore(mapping["Nintendo Museum (Uji)"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Nintendo Museum (Uji)"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Nintendo Museum (Uji)"].tags),
                Latitude = 34.8840,
                Longitude = 135.7903,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Nishiki Market ──
            new Destination
            {
                DestinationId = destNishikiMarketId,
                DestinationName = "Nishiki Market",
                CleanNormalizedSearchName = "nishiki market",
                MetaphoneCode = "NXK MRKT",
                DoubleMetaphonePrimary = "NXK MRKT",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Nishiki Market is a 400-year-old covered arcade stretching five blocks. Known as \"Kyoto's Kitchen,\" it features over 100 stalls selling everything from fresh seafood and pickled vegetables to sushi skewers, knives, and sweets. Rok rated it 8/10, calling it touristy but still fun to explore.\n\nWhat to Do: Eat your way through. Try the grilled unagi (eel) skewers, the sharp pickled takuan (daikon), and fresh yuba (tofu skin). Watch the artisans crafting traditional wooden boxes.",
                  "Directions": "Best Station: Karasuma Station (Subway) or Shijo Station (Hankyu Line).\n\nKey Exit: Exit A6 (to the market).\n\nThe Direction Walk: Walk north for 2 minutes from the exit. You will see the covered arcade entrance on your left.\n\nAddress for Taxi: 中京区錦小路通",
                  "WhatToKnow": "Opening Hours: Most stalls open at 9:00 AM and close by 5:00 PM. Some close as early as 4:00 PM. Do not go late.\n\nEating Etiquette: It is acceptable to eat while walking here (tabe-aruki), unlike in many other parts of Japan. However, bins are scarce – buy your food and eat it near the stall or bring a plastic bag for trash.",
                  "ThingsToBeWaryOf": "Crowds: It is a narrow alley. On weekends, you are shuffling shoulder-to-shoulder.\n\nPrices: Some stalls inflate prices for tourists, especially the ones with English menus. Compare a couple of stalls before buying.\n\nCash Only: Many of the smaller mom-and-pop shops are strictly cash. Carry ¥5,000 in small bills.",
                  "LocalPerspective": "The \"Food of the Gods\": Nishiki Market is famous for supplying the Imperial Palace and high-end temples. The quality of the ingredients is excellent.\n\nThe Knife Shops: There are multiple knife sellers (Aritsugu is the famous one) who will engrave your name on the blade for free.",
                  "HiddenCost": "Food: ¥200–¥1,500 ($1.30–$10) per skewer/portion. Budget ¥2,000–¥4,000 to graze properly.\n\nKnife Purchase: ¥10,000–¥100,000 ($66–$660) – Aritsugu is pricey.\n\nPickled Veggies: ¥500–¥1,000 per pack.",
                  "NearbyComplements": ["Teramachi-dori: A covered shopping arcade for anime/manga goods.", "Nijo Castle: A 15-minute walk for a historical castle.", "Daimaru Basement: Across the street for a high-end depachika."],
                  "BestTimeToVisit": "9:00 AM – 11:00 AM – freshest seafood and minimal crowds.",
                  "crowdLevel": "Very High (8/10) – always busy.",
                  "Accessibility": "Rating: 7/10 – flat and covered, but narrow. Strollers might struggle.",
                  "IdealDuration": "1 to 1.5 hours – just enough to walk the full length and grab snacks."
                }
                """,
                AverageCostPerDay = 2000m,
                LuxuryRating = DeriveLuxury(2000m, mapping["Nishiki Market"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Nishiki Market"].tags),
                AdventurePaceScore = AdventureScore(mapping["Nishiki Market"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Nishiki Market"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Nishiki Market"].tags),
                Latitude = 35.0050,
                Longitude = 135.7658,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Kyoto Railway Museum ──
            new Destination
            {
                DestinationId = destKyotoRailwayId,
                DestinationName = "Kyoto Railway Museum",
                CleanNormalizedSearchName = "kyoto railway museum",
                MetaphoneCode = "KT RWY MSM",
                DoubleMetaphonePrimary = "KT RWY MSM",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Right next to Kyoto Station, this is a massive, indoor-outdoor train museum that is way more fun than it has any right to be. It houses 53 retired locomotives, including steam trains, classic diesel units, and sleek Shinkansen bullet trains. You can walk under, around, and even *inside* many of them. Rok rated it 7/10, noting that it's especially fun for families, kids under 3 go free, and you can watch real Shinkansen pass by right outside the window.\n\nWhat to Do: Operate a real train simulator, walk through the cab of a 0-series Shinkansen (the very first bullet train), and watch the diorama of the Kyoto rail network. Check if Thomas the Tank Engine is visiting (they have rotating special exhibits).",
                  "Directions": "Best Access: Kyoto Station (JR Lines, Subway).\n\nThe Direction Walk: From the central exit of Kyoto Station, look for the Umekoji Park sign. It is a 5-minute walk west. You will see the massive water tower and the old red-brick depot.\n\nAddress for Taxi: 京都市下京区観喜寺町 (Umekoji Park area).",
                  "WhatToKnow": "Hands-on Experience: The simulator is very popular – you sign up for a slot at the entrance.\n\nThe Roundhouse: The main exhibition hall is a beautiful, historic roundhouse with a turntable. They rotate the trains onto the turntable for demonstrations.\n\nViewing Deck: The second floor has a large window overlooking the Shinkansen tracks. You can literally see the bullet trains zooming past every few minutes.",
                  "ThingsToBeWaryOf": "School Groups: It is a very popular field trip destination. If you see school buses, expect the simulators to have long waits.\n\nIndoor/Outdoor: The outdoor section has no shade. Bring a hat in summer.",
                  "LocalPerspective": "Japanese Train Obsession: This museum validates the fact that Japan takes trains *very* seriously. The attention to detail in the restorations is unparalleled – the metalwork is polished to a mirror shine.\n\nTwilight Special: On certain weekends, they light up the steam locomotives at dusk with dramatic spotlights – a favorite for local photographers.",
                  "HiddenCost": "Entry: ¥1,200 ($8) for adults. Kids under 3 free.\n\nSimulator: ¥300–¥500 ($2–$3.30) extra.\n\nTrain Ride: ¥200 for a short ride on a miniature steam train.\n\nParking: ¥400/hour.",
                  "NearbyComplements": ["Kyoto Aquarium: Adjacent to the museum – good for a combined ticket.", "Umekoji Park: A large park with a playground, perfect for a picnic."],
                  "BestTimeToVisit": "Weekday mornings – to avoid school groups.",
                  "crowdLevel": "Medium (6/10) – popular with families.",
                  "Accessibility": "Rating: 10/10 – modern, flat, wide spaces, elevators.",
                  "IdealDuration": "1.5 to 2.5 hours."
                }
                """,
                AverageCostPerDay = 1200m,
                 LuxuryRating = DeriveLuxury(1200m, mapping["Kyoto Railway Museum"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Kyoto Railway Museum"].tags),
                AdventurePaceScore = AdventureScore(mapping["Kyoto Railway Museum"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Kyoto Railway Museum"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Kyoto Railway Museum"].tags),
                Latitude = 34.9863,
                Longitude = 135.7567,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Universal Studios Japan ──
            new Destination
            {
                DestinationId = destUniversalId,
                DestinationName = "Universal Studios Japan",
                CleanNormalizedSearchName = "universal studios japan",
                MetaphoneCode = "UNVRSL STDS",
                DoubleMetaphonePrimary = "UNVRSL STTS",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "USJ is Osaka's entertainment heavyweight. It is impeccably themed, with the massive Wizarding World of Harry Potter, the immersive Jurassic Park area, and the crown jewel – Super Nintendo World. Unlike DisneySea, USJ is geared towards thrilling rollercoasters and blockbuster movie immersion. Rok emphasized: \"Arrive early to avoid insane lines.\"\n\nWhat to Do: Ride the Flying Dinosaur (a suspended coaster). Explore Hogwarts Castle. And spend at least 2 hours in Super Nintendo World.",
                  "Directions": "Best Station: Universal City Station (JR Yumesaki Line).\n\nThe Direction Walk: From the station, you are directly at the park entrance. It is a 5-minute walk through CityWalk.\n\nAddress for Taxi: 大阪市此花区桜島2-1-33.",
                  "WhatToKnow": "Express Pass: This is absolutely essential if you only have one day. It costs almost as much as the ticket but saves you 4+ hours of queuing.\n\nNintendo World Entry: Even with a ticket, you may need a timed entry ticket for Super Nintendo World. Get the app and grab a digital slot as soon as you enter the park.\n\nWater Rides: Jurassic Park ride will soak you – bring a poncho or wear quick-dry clothes.",
                  "ThingsToBeWaryOf": "Lines, Lines, Lines: The line for the Flying Dinosaur can hit 180 minutes. Use the single-rider line where available.\n\nRain: Some rides close in heavy rain, and shows get canceled.\n\nFood: The food is mediocre for the price. Eat at CityWalk outside the gates before entering.",
                  "LocalPerspective": "Japanese fans take cosplay seriously. You will see elaborate, homemade Harry Potter robes and Mario costumes.\n\nThe Halloween Horror Nights in Osaka are considered some of the scariest in the world – they go all out with Japanese-style ghost stories.",
                  "HiddenCost": "Ticket: ¥9,800 ($65).\n\nExpress Pass: ¥8,000–¥14,000 ($53–$93).\n\nNintendo Power Band: ¥3,200 ($21) – needed for interactive games in Nintendo World.\n\nMeals: ¥1,500–¥3,000 ($10–$20).\n\nPopcorn Buckets: ¥2,000–¥4,000.",
                  "NearbyComplements": ["CityWalk: Shops and restaurants outside the park.", "Osaka Aquarium: A 15-minute walk for a massive whale shark tank."],
                  "BestTimeToVisit": "Weekdays (Tue-Thu) – avoid Japanese holidays.\n\nJanuary-February – the coldest months are the quietest.\n\nArrive 1 hour before opening.",
                  "crowdLevel": "Maximum (10/10) on weekends/holidays. High (8/10) on weekdays.",
                  "Accessibility": "Rating: 8/10 – flat, but vast. Wheelchair rentals are available.",
                  "IdealDuration": "Full day (10+ hours)."
                }
                """,
                AverageCostPerDay = 9800m,
                LuxuryRating = DeriveLuxury(9800m, mapping["Universal Studios Japan"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Universal Studios Japan"].tags),
                AdventurePaceScore = AdventureScore(mapping["Universal Studios Japan"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Universal Studios Japan"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Universal Studios Japan"].tags),
                Latitude = 34.6624,
                Longitude = 135.4316,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Super Nintendo World ──
            new Destination
            {
                DestinationId = destSuperNintendoId,
                DestinationName = "Super Nintendo World (inside USJ)",
                CleanNormalizedSearchName = "super nintendo world",
                MetaphoneCode = "SPR NNTND WRLD",
                DoubleMetaphonePrimary = "SPR NNTNT WRLT",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "This is the literal translation of the Mushroom Kingdom into real life. You step through a giant green pipe and instantly enter a vibrantly colored, interactive paradise featuring Bowser's Castle, Peach's Castle, and the iconic question mark blocks. Rok gave it 10/10, calling it immersive.\n\nWhat to Do: Ride the Mario Kart: Koopa's Challenge (AR-enhanced race). Hit the question mark blocks to collect coins (via the Power Band). Meet Mario and Luigi (they speak Japanese!)\n\nDirections: Inside Universal Studios Japan. You must have a USJ ticket and a timed entry ticket for this zone.\n\nAddress: Inside USJ, Osaka.",
                  "WhatToKnow": "Power Band: You absolutely must buy the wristband. It connects to the app and lets you collect digital coins, keys, and stamps. Without it, you are just walking around.\n\nThe 1-Up Factory: The gift shop is huge and has exclusive merch not sold anywhere else (including Mario-themed food items).\n\nThe Yoshi Ride: A gentle, family-friendly ride over the heads of the crowd.",
                  "ThingsToBeWaryOf": "Timed Entry: If you don't get a timed entry ticket, you cannot enter. Secure this via the USJ app the moment you pass the main gate.\n\nIntense Lines: Mario Kart can be 2.5 hours. Single-rider line cuts it to 45 mins, but you miss the pre-show.\n\nOverstimulation: The colors and sounds are intense. It can be overwhelming for young kids or sensitive adults.",
                  "LocalPerspective": "The attention to detail is so extreme that the grass is made of fiber optic wires to look exactly like the pixelated grass from the games.\n\nJapanese visitors love the \"Kinopio Cafe\" (Toad's Cafe) – the food is themed to look like Mario characters, and the line can be 1 hour just to get a table.",
                  "HiddenCost": "Power Band: ¥3,200 ($21).\n\nMario Kart Photo: ¥1,500 ($10) for a souvenir photo.\n\nToad's Cafe Meal: ¥2,000–¥3,500 ($13–$23).\n\nMerch: ¥2,000–¥15,000 ($13–$100).",
                  "NearbyComplements": ["Within USJ – combine with Harry Potter area."],
                  "BestTimeToVisit": "First thing in the morning – run to the zone as soon as the park opens. Or late evening (7:00 PM+) – the crowds die down.",
                  "crowdLevel": "Maximum (10/10).",
                  "Accessibility": "Rating: 9/10 – flat, easy to navigate.",
                  "IdealDuration": "3 to 4 hours – enough to ride Mario Kart, collect coins, and eat."
                }
                """,
                AverageCostPerDay = 0m,
                LuxuryRating = DeriveLuxury(0m, mapping["Super Nintendo World (inside USJ)"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Super Nintendo World (inside USJ)"].tags),
                AdventurePaceScore = AdventureScore(mapping["Super Nintendo World (inside USJ)"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Super Nintendo World (inside USJ)"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Super Nintendo World (inside USJ)"].tags),
                Latitude = 34.6624,
                Longitude = 135.4316,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Dotonbori ──
            new Destination
            {
                DestinationId = destDotonboriId,
                DestinationName = "Dotonbori",
                CleanNormalizedSearchName = "dotonbori",
                MetaphoneCode = "DTNBR",
                DoubleMetaphonePrimary = "TTNPR",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Dotonbori is the neon-soaked, food-obsessed heart of Osaka. The canal is lined with giant, flashing mechanical signs – a huge crab, a running man, a giant octopus. The street food is legendary: takoyaki (octopus balls), okonomiyaki (savory pancakes), and kushikatsu (deep-fried skewers). Rok experienced it during Halloween and called it the craziest he had seen in Japan.\n\nWhat to Do: Eat takoyaki while standing on the bridge. Take a photo with the Glico Running Man sign. Walk through the narrow side alleys for hidden ramen shops.",
                  "Directions": "Best Station: Namba Station (Subway Midosuji Line, Nankai Line).\n\nKey Exit: Exit 14 (for Ebisu Bridge).\n\nThe Direction Walk: Take Exit 14 and walk north. You will hit the Dotonbori canal and the iconic Glico sign immediately.\n\nAddress for Taxi: 中央区道頓堀1-6 (Ebisu Bridge).",
                  "WhatToKnow": "The Glico Sign: This is the landmark. It is the finish line of the Osaka marathon. At night, the background changes colors.\n\nHalloween Effect: During October, the whole area transforms into a massive cosplay street party. It is extremely loud, crowded, and purely chaotic fun.\n\nFood Rules: You can eat while walking, but trash bins are absent. Bring a plastic bag or eat near the stall.",
                  "ThingsToBeWaryOf": "Pigeons: They are aggressive near the canal. Don't drop food.\n\nCrowd Surge: On weekends, it is shoulder-to-shoulder. Avoid if you hate claustrophobia.\n\nTourist Traps: Some restaurants on the main drag have inflated prices. Go one street back for better value.",
                  "LocalPerspective": "The \"Kuidaore\" Spirit: There is a saying in Osaka – \"Kuidaore\" (to eat oneself into ruin). Osakans are famous for spending all their money on food. Embrace it!\n\nFugu (Blowfish): There are several fugu restaurants here. They are licensed and safe, but expensive.\n\nThe Drumming Taiko: Some of the mechanical signs have drums that pound out a beat every hour.",
                  "HiddenCost": "Takoyaki: ¥500–¥800 ($3.30–$5.30) for 6-8 pieces.\n\nOkonomiyaki: ¥1,000–¥1,800 ($6.70–$12).\n\nKushikatsu: ¥150–¥300 ($1–$2) per skewer.\n\nFugu Course: ¥5,000–¥15,000 ($33–$100).\n\nRiver Cruise: ¥1,500 ($10) for a 20-minute canal ride.",
                  "NearbyComplements": ["Shinsaibashi: The covered shopping arcade that connects to Dotonbori.", "Namba Yasaka Shrine: A 10-minute walk for the lion head.", "Amerikamura: 10 minutes south for youth streetwear."],
                  "BestTimeToVisit": "Evening (6:00 PM – 10:00 PM) – the neon is essential.\n\nHalloween Night – for a once-in-a-lifetime street party.\n\nLate Night (after 11 PM) – less crowded but still bright.",
                  "crowdLevel": "Maximum (10/10).",
                  "Accessibility": "Rating: 8/10 – flat and paved, but extremely crowded. Strollers are a nightmare.",
                  "IdealDuration": "2 to 3 hours – to eat, shop, and take photos."
                }
                """,
                AverageCostPerDay = 2000m,
                LuxuryRating = DeriveLuxury(2000m, mapping["Dotonbori"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Dotonbori"].tags),
                AdventurePaceScore = AdventureScore(mapping["Dotonbori"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Dotonbori"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Dotonbori"].tags),
                Latitude = 34.6688,
                Longitude = 135.5013,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 2,
                CountryId = japan.CountryId
            },

            // ── Osaka Castle ──
            new Destination
            {
                DestinationId = destOsakaCastleId,
                DestinationName = "Osaka Castle",
                CleanNormalizedSearchName = "osaka castle",
                MetaphoneCode = "OSK KSTL",
                DoubleMetaphonePrimary = "OSK KSTL",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Osaka Castle is a striking five-story structure surrounded by massive stone walls and a moat. The main keep is a modern museum inside, but the exterior is a faithful reconstruction of Toyotomi Hideyoshi's 16th-century fortress. The best part? The view from the observation deck, and the hidden gem – Blue Birds Terrace (a rooftop café adjacent to the castle) that offers a magnificent perspective without the castle entry queue. Rok highlighted the view from Blue Birds Terrace.\n\nWhat to Do: Walk around the Nishinomaru Garden (especially during cherry blossom season). Visit the museum inside the keep to learn about the unification of Japan. Then, head to Blue Birds Terrace for a drink and an unobstructed photo.",
                  "Directions": "Best Station: Osakajokoen Station (JR Loop Line) or Tanimachi 4-chome (Subway).\n\nThe Direction Walk: From the station, walk 10 minutes through the park towards the massive moat and gate.\n\nAddress for Taxi: 中央区大阪城1-1.",
                  "WhatToKnow": "The Castle has an elevator inside the keep, but the views from the 8th floor are spectacular.\n\nThe stone walls are massive – they are called \"ishigaki\" and were built without mortar.\n\nBlue Birds Terrace is located in the JO-TERRACE complex to the west of the castle. It has a terrace directly facing the castle tower.",
                  "ThingsToBeWaryOf": "Weekend Crowds: The queue for the elevator can be 1 hour. Walk up the stairs if you can.\n\nHot Summer: There is very little shade in the outer grounds. Bring a parasol.\n\nThe Interior: It is a concrete museum, not a historical castle. Manage expectations if you are looking for authenticity.",
                  "LocalPerspective": "Toyotomi Hideyoshi is Osaka's favorite historical figure. The castle is a symbol of his ambition. Locals love to visit during the cherry blossom season (late March) when the park is filled with hanami parties.\n\nThe Golden Teapot: At the top of the castle, there is a golden shachihoko (tiger-fish) ornament that is a local icon.",
                  "HiddenCost": "Entry to Keep: ¥600 ($4).\n\nNishinomaru Garden: ¥200 ($1.30) – only worth it during cherry blossom season.\n\nBlue Birds Terrace: Free entry, but coffee is ¥600 ($4).\n\nGozabune Boat Ride: ¥1,500 ($10) – a 20-minute cruise on the inner moat.",
                  "NearbyComplements": ["The Museum of History: Adjacent to the park.", "Tenmangu Shrine: A 20-minute walk north for a vibrant shrine."],
                  "BestTimeToVisit": "Late March (Sakura season) – the garden is packed, but stunning.\n\nLate November (Autumn leaves) – golden foliage around the moat.\n\nEvening – the castle lights up.",
                  "crowdLevel": "High (8/10) – especially on weekends and during Sakura.",
                  "Accessibility": "Rating: 7/10 – the keep has an elevator, but the park is large and involves walking on gravel.",
                  "IdealDuration": "1.5 to 2 hours (including a coffee at Blue Birds)."
                }
                """,
                AverageCostPerDay = 600m,
                LuxuryRating = DeriveLuxury(600m, mapping["Osaka Castle"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Osaka Castle"].tags),
                AdventurePaceScore = AdventureScore(mapping["Osaka Castle"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Osaka Castle"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Osaka Castle"].tags),
                Latitude = 34.6863,
                Longitude = 135.5262,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Shinsekai ──
            new Destination
            {
                DestinationId = destShinsekaiId,
                DestinationName = "Shinsekai (Retro District)",
                CleanNormalizedSearchName = "shinsekai",
                MetaphoneCode = "XNSK",
                DoubleMetaphonePrimary = "XNSK",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Shinsekai was built in 1912 to resemble Paris and New York, but today it is a nostalgic, slightly gritty retro district that feels frozen in the Showa era. The centerpiece is the Tsutenkaku Tower (a 108m observation tower). The streets are filled with cheap kushikatsu stands, pachinko parlors, and old-school photo studios. Rok loved the retro vibes.\n\nWhat to Do: Climb Tsutenkaku for a vintage view of Osaka. Eat kushikatsu (deep-fried skewers) at the famous Daruma restaurant. Visit the many tiny arcades.",
                  "Directions": "Best Station: Dobutsuen-mae Station (Subway Midosuji/Sakaisuji Lines).\n\nKey Exit: Exit 1 or 2.\n\nThe Direction Walk: You will see the Billiken statue and Tsutenkaku immediately upon exiting.\n\nAddress for Taxi: 浪速区恵美須東1-18-6.",
                  "WhatToKnow": "Tsutenkaku is the symbol of Shinsekai. The neon lights at the top change color depending on the weather forecast.\n\nBilliken: A peculiar, smiling god of happiness who resides in the tower. Rubbing his feet brings good luck.\n\nKushikatsu Rule: The classic rule is \"no double-dipping\" – you dip your skewer into the communal sauce before you take a bite, not after.",
                  "ThingsToBeWaryOf": "Gritty Feel: It is not polished. It is old, smoky, and raw. Some travelers find it unsettling at night.\n\nTouts: There are touts outside some bars, but they are not as aggressive as in Kabukicho.\n\nLanguage Barrier: The older shop owners rarely speak English.",
                  "LocalPerspective": "This was once the most modern part of Osaka. Now, it is a time capsule. Locals come here for the cheap eats and the nostalgic charm.\n\nThe tower hosts the annual \"Tower of the Sun\" light-up events.",
                  "HiddenCost": "Tsutenkaku Entry: ¥800 ($5.30).\n\nKushikatsu: ¥100–¥200 ($0.70–$1.30) per skewer.\n\nBilliken Photo: ¥500 ($3.30).",
                  "NearbyComplements": ["Tennoji Zoo: Adjacent to the station.", "Spa World: A massive onsen theme park near the station."],
                  "BestTimeToVisit": "Late afternoon (3:00 PM – 5:00 PM) – the light is good, and the neon starts to glow.\n\nEvening (7:00 PM – 10:00 PM) – the district comes alive for dinner.",
                  "crowdLevel": "Medium (5/10) – mostly locals.",
                  "Accessibility": "Rating: 6/10 – flat but narrow streets.",
                  "IdealDuration": "1 to 2 hours."
                }
                """,
                AverageCostPerDay = 1000m,
                LuxuryRating = DeriveLuxury(1000m, mapping["Shinsekai (Retro District)"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Shinsekai (Retro District)"].tags),
                AdventurePaceScore = AdventureScore(mapping["Shinsekai (Retro District)"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Shinsekai (Retro District)"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Shinsekai (Retro District)"].tags),
                Latitude = 34.6500,
                Longitude = 135.5070,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 2,
                CountryId = japan.CountryId
            },

            // ── Tower Knives ──
            new Destination
            {
                DestinationId = destTowerKnivesId,
                DestinationName = "Tower Knives Osaka",
                CleanNormalizedSearchName = "tower knives osaka",
                MetaphoneCode = "TWR NVS",
                DoubleMetaphonePrimary = "TWR NFS",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Located in the heart of the kitchenware district (Sennichimae), this is the most famous knife shop in Osaka. It is a multi-story temple to Japanese cutlery, selling everything from high-carbon steel Gyutou (chef's knives) to traditional Deba and Yanagiba. The staff are incredibly knowledgeable and will patiently guide you through the selection, often letting you test the knives on vegetables.\n\nWhat to Do: Ask for a recommendation based on your cooking style. Watch the sharpening demonstration. Purchase a knife and get your name or initials engraved on the blade for free.",
                  "Directions": "Best Station: Namba Station. Walk towards Sennichimae shopping street.\n\nThe Direction Walk: From Namba, head east. It is about 10 minutes walk from the station, nestled near the Dotombori canal.\n\nAddress for Taxi: 中央区難波3-4-6.",
                  "WhatToKnow": "Engraving: They offer free engraving in English or Kanji on the blade. It takes about 10 minutes.\n\nSharpening Class: They often have a small room where they demonstrate sharpening techniques.\n\nQuality Levels: Knives range from entry-level (VG-10 steel) to master-level (honyaki carbon steel). Ask about maintenance.",
                  "ThingsToBeWaryOf": "Budget: A good knife is an investment. Entry-level is around ¥10,000, but the beautiful ones are ¥30,000+.\n\nCustoms: Check your home country's regulations on bringing knives into the country (checked luggage only).\n\nSales Pressure: The staff are passionate but not pushy. Take your time.",
                  "LocalPerspective": "Osaka is the kitchen of Japan. Chefs from all over the country come here to buy their knives. The history of the district goes back centuries, supplying blades to the Imperial Palace.",
                  "HiddenCost": "Knife: ¥8,000 – ¥100,000+ ($53–$660).\n\nSharpening Stones: ¥3,000–¥15,000 ($20–$100) if you want to maintain it yourself.\n\nEngraving: Free.",
                  "NearbyComplements": ["Doguyasuji: The main shopping street for restaurant supplies – pots, pans, and plastic food samples.", "Namba Parks: A high-end shopping mall nearby."],
                  "BestTimeToVisit": "Morning (10:00 AM – 12:00 PM) – before the tourist rush.",
                  "crowdLevel": "Medium (5/10) – popular with tourists and professional chefs.",
                  "Accessibility": "Rating: 7/10 – narrow aisles, but they have an elevator.",
                  "IdealDuration": "1 hour – but it's easy to spend 2 hours looking."
                }
                """,
                AverageCostPerDay = 5000m,
                LuxuryRating = DeriveLuxury(5000m, mapping["Tower Knives Osaka"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Tower Knives Osaka"].tags),
                AdventurePaceScore = AdventureScore(mapping["Tower Knives Osaka"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Tower Knives Osaka"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Tower Knives Osaka"].tags),
                Latitude = 34.6666,
                Longitude = 135.5030,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Namba Yasaka Shrine ──
            new Destination
            {
                DestinationId = destNambaYasakaId,
                DestinationName = "Namba Yasaka Shrine (Lion Head)",
                CleanNormalizedSearchName = "namba yasaka shrine",
                MetaphoneCode = "NMB YSK SHN",
                DoubleMetaphonePrimary = "NMP YSK XRN",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Namba Yasaka is a small, unassuming shrine tucked between the skyscrapers of Namba. It is famous for one thing: an enormous, 12-meter-high, 7-ton bronze lion head that serves as the main hall. The lion's mouth is wide open, designed to swallow evil spirits and invite good luck. It is messy, loud, chaotic, and extremely fun – exactly as Rok described.\n\nWhat to Do: Walk right up to the lion's jaw and toss a coin into the offering box. Take a selfie in front of the enormous face. Watch the locals pray and clap.",
                  "Directions": "Best Station: Namba Station (Subway).\n\nThe Direction Walk: It is a 5-minute walk from Namba Station. Walk east past the Namba Parks building and you will see the giant head peering over the fence.\n\nAddress for Taxi: 浪速区敷津西1-2-11.",
                  "WhatToKnow": "The lion's mouth is the building itself. You can actually see the altar inside the mouth.\n\nThis shrine is a major protector of the surrounding entertainment district. Locals visit before hitting the bars for good luck.",
                  "ThingsToBeWaryOf": "It is completely surrounded by modern buildings. Do not expect a natural shrine setting – it is an urban curiosity.\n\nTiny Grounds: You can see everything in 10 minutes. It is a quick photo stop.",
                  "LocalPerspective": "The lion is called the \"Yakuyoke no Shishi\" (evil-dispelling lion). Students and businessmen come here to ward off bad luck before exams or important deals.\n\nThere is a small market on the grounds on weekends with traditional snacks.",
                  "HiddenCost": "Entry: Free.\n\nOmamori: ¥500–¥1,000 ($3.30–$6.70).\n\nEma: ¥500.",
                  "NearbyComplements": ["Namba Parks: A futuristic shopping mall with a rooftop garden – walk 2 minutes.", "Den Den Town: Osaka's version of Akihabara (electronics/anime) is nearby."],
                  "BestTimeToVisit": "Any time of day. Sunset looks great with the city lights behind the lion.",
                  "crowdLevel": "Low (3/10) – a hidden gem.",
                  "Accessibility": "Rating: 10/10 – flat, concrete, easy access.",
                  "IdealDuration": "15 to 20 minutes."
                }
                """,
                AverageCostPerDay = 0m,
                LuxuryRating = DeriveLuxury(0m, mapping["Namba Yasaka Shrine (Lion Head)"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Namba Yasaka Shrine (Lion Head)"].tags),
                AdventurePaceScore = AdventureScore(mapping["Namba Yasaka Shrine (Lion Head)"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Namba Yasaka Shrine (Lion Head)"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Namba Yasaka Shrine (Lion Head)"].tags),
                Latitude = 34.6585,
                Longitude = 135.5040,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Amerikamura ──
            new Destination
            {
                DestinationId = destAmerikamuraId,
                DestinationName = "Amerikamura (American Village)",
                CleanNormalizedSearchName = "amerikamura",
                MetaphoneCode = "AMRKM R",
                DoubleMetaphonePrimary = "AMRKM R",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Amerikamura, or \"Ame-mura,\" is Osaka's youth culture epicenter. It is a small district of narrow streets packed with vintage clothing stores, sneaker resellers, indie record shops, and quirky cafes. The vibe is chaotic, loud, and distinctly anti-establishment – the Japanese version of downtown LA.\n\nWhat to Do: Hunt for vintage Levi's at cheap prices. Eat at the American-style burger joints. Watch the street performers (breakdancers, musicians) near the Triangle Park.",
                  "Directions": "Best Station: Shinsaibashi Station (Subway Midosuji Line).\n\nThe Direction Walk: Exit towards the Midosuji shopping arcade, walk west for 2 minutes. You will see the retro American neon signs.\n\nAddress for Taxi: 中央区西心斎橋2-8-1 (Triangle Park).",
                  "WhatToKnow": "Triangle Park is the heart – a small plaza where people hang out. It is always packed with skateboarders, cosplayers, and tourists.\n\nVintage Shopping: Japan has a huge obsession with American vintage (especially workwear). There are some incredible finds here.\n\nThe food: Go for the massive, messy burgers. They are surprisingly good.",
                  "ThingsToBeWaryOf": "Crowds: Narrow streets + weekend crowds = gridlock.\n\nOverpriced Sneakers: Some resale shops charge exorbitant prices for limited edition Nike/Jordan sneakers.\n\nSkaters: Watch out for skateboards in Triangle Park.",
                  "LocalPerspective": "Ame-mura was the birthplace of Japanese street fashion in the 1970s. It remains a rebellious counter-culture to the conservative Japanese mainstream.\n\nThe mascot is a giant red billboard of a superhero called \"Jumbo-kun.\" Locals use this as a meeting spot.",
                  "HiddenCost": "Vintage Clothing: ¥3,000–¥20,000 ($20–$133).\n\nBurgers: ¥1,000–¥2,500 ($6.70–$16).\n\nSneakers: ¥15,000–¥100,000+ ($100–$660).",
                  "NearbyComplements": ["Shinsaibashi: Adjacent covered arcade.", "Dotonbori: A 10-minute walk for food."],
                  "BestTimeToVisit": "Afternoon (1:00 PM – 5:00 PM) – when the shops open and the street performers come out.",
                  "crowdLevel": "High (7/10) on weekends.",
                  "Accessibility": "Rating: 8/10 – flat, but narrow.",
                  "IdealDuration": "1 to 2 hours."
                }
                """,
                AverageCostPerDay = 1500m,
                LuxuryRating = DeriveLuxury(1500m, mapping["Amerikamura (American Village)"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Amerikamura (American Village)"].tags),
                AdventurePaceScore = AdventureScore(mapping["Amerikamura (American Village)"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Amerikamura (American Village)"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Amerikamura (American Village)"].tags),
                Latitude = 34.6700,
                Longitude = 135.5000,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 2,
                CountryId = japan.CountryId
            },

            // ── Don Quijote ──
            new Destination
            {
                DestinationId = destDonQuijoteId,
                DestinationName = "Don Quijote (Discount Store)",
                CleanNormalizedSearchName = "don quijote",
                MetaphoneCode = "DN QJT",
                DoubleMetaphonePrimary = "TN KJT",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "Don Quijote (or \"Donki\") is a 24-hour discount mega-store that is a complete assault on the senses. The aisles are dangerously packed with absolutely everything: cosmetics, electronics, weird costumes, Japanese snacks, expensive watches, and adult toys. The theme music (a cheerful jingle) loops endlessly. It is messy, loud, chaotic, and extremely fun – exactly as described.\n\nWhat to Do: Navigate the maze-like floors. Buy souvenirs (matcha KitKats, weird Pocky flavors). Get a cheap Hakata ramen kit. Look for the giant cardboard cutouts of the Donki penguin.",
                  "Directions": "Best Station: Namba Station. Most branches (Namba, Shinsaibashi, Umeda) are within walking distance.\n\nThe Direction Walk: The Namba branch is a massive building directly on the Midosuji avenue, near the Dotonbori entrance.\n\nAddress for Taxi: 中央区難波3-3-17 (Namba Branch).",
                  "WhatToKnow": "Tax-Free: They have a dedicated tax-free counter. Bring your passport.\n\nCostumes: Donki is the best place to buy cheap Halloween costumes or funny wigs.\n\nGachapon: They have huge rows of capsule machines near the exit.",
                  "ThingsToBeWaryOf": "Overwhelm: It is extremely cluttered and claustrophobic. Aisles are narrow, and the music is loud.\n\nImpulse Buys: You will buy things you don't need. Bring a small bag or shop near the end of your trip.\n\nCash vs Card: They take cards, but cash is faster for the tax-free process.",
                  "LocalPerspective": "Donki is a Japanese cultural phenomenon. Many locals call it \"the store that never sleeps.\" It's the go-to for last-minute gifts and party supplies.\n\nThe singing penguin, Donpen, is beloved. He has his own merchandise.",
                  "HiddenCost": "Price varies wildly – you can spend ¥500 or ¥50,000. Budget accordingly.",
                  "NearbyComplements": ["Any other Donki branch! They are everywhere."],
                  "BestTimeToVisit": "Late night (11:00 PM – 2:00 AM) – it is quieter and a unique late-night experience.",
                  "crowdLevel": "High (8/10) during daytime, Low (4/10) late night.",
                  "Accessibility": "Rating: 5/10 – cramped aisles, but they have elevators.",
                  "IdealDuration": "1 hour – you will likely get lost in the aisles."
                }
                """,
                AverageCostPerDay = 0m,
                LuxuryRating = DeriveLuxury(0m, mapping["Don Quijote (Discount Store)"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Don Quijote (Discount Store)"].tags),
                AdventurePaceScore = AdventureScore(mapping["Don Quijote (Discount Store)"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Don Quijote (Discount Store)"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Don Quijote (Discount Store)"].tags),
                Latitude = 34.6690,
                Longitude = 135.5020,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Umeda Sky Building ──
            new Destination
            {
                DestinationId = destUmedaId,
                DestinationName = "Umeda Sky Building",
                CleanNormalizedSearchName = "umeda sky building",
                MetaphoneCode = "UMT SK BLDNG",
                DoubleMetaphonePrimary = "UMT SK PLTNK",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "The Umeda Sky Building is a futuristic architectural marvel – two 40-story towers connected at the top by a circular Floating Garden Observatory. This doughnut-shaped platform offers a 360-degree, open-air view of Osaka. The glass floors and the escalator connecting the towers (suspended in mid-air) are thrilling. Rok mentioned the two elevators trick – one has a shorter line, but both lead to the same place!\n\nWhat to Do: Take the transparent escalator (it feels like flying). Walk the circular outdoor Sky Walk. Visit the cafe at the top for a drink with the view.",
                  "Directions": "Best Station: Umeda Station (JR, Hankyu, Hanshin, Subway).\n\nThe Direction Walk: It is a 10-minute walk through an underground pedestrian tunnel. Follow the signs for Umeda Sky Building or the Art Museum.\n\nAddress for Taxi: 北区大淀中1-1-88.",
                  "WhatToKnow": "Two Elevators: There are two elevator banks. One is for general admission, the other is for the restaurant (which is free to ride). If you see a long line at one, the staff often splits the queue.\n\nThe observatory is open until 10:30 PM. It is one of the best night views in Osaka.\n\nThere is a love lock fence at the top – bring a padlock if you want to leave one.",
                  "ThingsToBeWaryOf": "Windy: The outdoor terrace is extremely windy. Hold onto your hats and glasses.\n\nGlass Floor: There is a small glass floor section. It can be disorienting.\n\nElevator Mix-up: The elevators are hard to find. Don't be afraid to ask security.",
                  "LocalPerspective": "The building's design was meant to resemble the ancient ruins of a stadium. Locals love it for its futuristic but nostalgic silhouette.\n\nThe basement floor (Takimi-koji) is a retro recreation of a 1920s Osaka street, with tiny pubs and a Showa-era vibe.",
                  "HiddenCost": "Observatory: ¥1,500 ($10).\n\nLock: ¥500 ($3.30) if you forget to bring one.\n\nDrinks: ¥700 ($4.70) for a coffee at the top.",
                  "NearbyComplements": ["Kuchu-teien Observatory: The actual name of the floating garden.", "Shin-Umeda City: The complex it belongs to."],
                  "BestTimeToVisit": "Sunset (5:00 PM – 6:30 PM) – to see the transition from day to night.",
                  "crowdLevel": "Medium (6/10) – popular but not insane.",
                  "Accessibility": "Rating: 9/10 – modern, elevators, wheelchair accessible.",
                  "IdealDuration": "1 to 1.5 hours."
                }
                """,
                AverageCostPerDay = 1500m,
                LuxuryRating = DeriveLuxury(1500m, mapping["Umeda Sky Building"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Umeda Sky Building"].tags),
                AdventurePaceScore = AdventureScore(mapping["Umeda Sky Building"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Umeda Sky Building"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Umeda Sky Building"].tags),
                Latitude = 34.7053,
                Longitude = 135.4906,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            },

            // ── Blue Birds Terrace ──
            new Destination
            {
                DestinationId = destBlueBirdsId,
                DestinationName = "Blue Birds Terrace (Osaka Castle View)",
                CleanNormalizedSearchName = "blue birds terrace",
                MetaphoneCode = "BL BRDS TRC",
                DoubleMetaphonePrimary = "PL PRTS TRS",
                DoubleMetaphoneSecondary = "",
                CommonAlternateSpellingsJson = "[]",
                DescriptionJson = """
                {
                  "Overview": "This is a hidden gem viewpoint right next to Osaka Castle. Located on the 6th floor of the JO-TERRACE complex, it is a spacious wooden deck with outdoor seating that offers a panoramic, unobstructed view of the majestic castle tower without the crowds of the main keep. Rok noted it as the place for an amazing view.\n\nWhat to Do: Grab a coffee or craft beer from the terrace cafe, sit on the deck, and just stare at the castle. It's the perfect spot to relax after walking around the castle park. The view is particularly stunning during sunset.",
                  "Directions": "Best Station: Osakajokoen Station.\n\nAddress: Inside JO-TERRACE Osaka (west side of the castle moat).\n\nThe Direction Walk: From the station, walk towards the castle, but instead of crossing the moat to the main keep, walk along the west side of the moat until you see the JO-TERRACE building. Take the elevator to the 6th floor.\n\nAddress for Taxi: 中央区大阪城2-1 (JO-TERRACE).",
                  "WhatToKnow": "It is free to access. There is no entry fee.\n\nThe terrace has comfortable lounge chairs and umbrellas for shade.\n\nThey have a bar serving local Osaka craft beers and light snacks.",
                  "ThingsToBeWaryOf": "Limited Seats: The terrace is popular, but not overcrowded. Seats fill up quickly around 4:00 PM.\n\nWeather: It is entirely outdoors. In winter, it's cold; in summer, it's hot.\n\nLocation: It is separate from the castle keep, so you must view the castle from across the moat (which is the best angle anyway).",
                  "LocalPerspective": "This is the spot where local photographers go to take the classic \"castle reflection\" shot (when the water in the moat is still).\n\nThe name \"Blue Birds\" comes from a concept of happiness and peace – they want you to feel like a bluebird overlooking the city.",
                  "HiddenCost": "Coffee: ¥600–¥800 ($4–$5.30).\n\nCraft Beer: ¥1,000 ($6.70).\n\nSnacks: ¥500 ($3.30).\n\nEntry: Free.",
                  "NearbyComplements": ["Osaka Castle Park: The surrounding park is great for walks.", "The Museum of History: Just across the street."],
                  "BestTimeToVisit": "Late afternoon (4:00 PM – 6:00 PM) – to catch the golden hour light hitting the castle. Also beautiful at night when the castle is illuminated.",
                  "crowdLevel": "Low (3/10) – a local secret.",
                  "Accessibility": "Rating: 10/10 – modern building with elevators.",
                  "IdealDuration": "30 to 60 minutes – perfect for a rest stop."
                }
                """,
                AverageCostPerDay = 0m,
                LuxuryRating = DeriveLuxury(0m, mapping["Blue Birds Terrace (Osaka Castle View)"].tags),
                AccessibilityType = "Train",
                FamilyFriendlyScore = FamilyScore(mapping["Blue Birds Terrace (Osaka Castle View)"].tags),
                AdventurePaceScore = AdventureScore(mapping["Blue Birds Terrace (Osaka Castle View)"].tags),
                AestheticTrendScore = AestheticTrendScore(mapping["Blue Birds Terrace (Osaka Castle View)"].tags),
                PsychographicVibeTagsJson =JsonSerializer.Serialize(mapping["Blue Birds Terrace (Osaka Castle View)"].tags),
                Latitude = 34.6863,
                Longitude = 135.5262,
                SearchHitCount = 0,
                TimeZone = "Japan Standard Time",
                SafetyLevel = 1,
                CountryId = japan.CountryId
            }
        };

        db.Destinations.AddRange(newDestinations);
        await db.SaveChangesAsync();

        // Full list (new + pre-existing) used by the tagging/image/language helpers below
        var allDestinations = newDestinations.Concat(new[] { asakusaDest, sensoJiDest }).ToList();

        await AssignCategoriesAndTagsToDestinationsAsync(db, allDestinations, mapping);
        await SeedImagesAsync(db, allDestinations);
        await SeedDestinationLanguagesAndCurrenciesAsync(db, allDestinations);

        // ─── Destination ↔ City links ─────────────────────────────────────────
        var destCityLinks = new[]
        {
            new DestinationCity { DestinationId = destShibuyaId, CityId = tokyo.CityId },
            new DestinationCity { DestinationId = destShinjukuId, CityId = tokyo.CityId },
            new DestinationCity { DestinationId = destGinzaId, CityId = tokyo.CityId },
            new DestinationCity { DestinationId = destDisneySeaId, CityId = tokyo.CityId },
            new DestinationCity { DestinationId = destChureitoId, CityId = kawaguchiko.CityId },
            new DestinationCity { DestinationId = destFujiSpeedwayId, CityId = kawaguchiko.CityId },
            new DestinationCity { DestinationId = destNinjaVillageId, CityId = kawaguchiko.CityId },
            new DestinationCity { DestinationId = destFushimiInariId, CityId = kyoto.CityId },
            new DestinationCity { DestinationId = destKiyomizuId, CityId = kyoto.CityId },
            new DestinationCity { DestinationId = destNintendoMuseumId, CityId = kyoto.CityId },
            new DestinationCity { DestinationId = destNishikiMarketId, CityId = kyoto.CityId },
            new DestinationCity { DestinationId = destKyotoRailwayId, CityId = kyoto.CityId },
            new DestinationCity { DestinationId = destUniversalId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destSuperNintendoId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destDotonboriId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destOsakaCastleId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destShinsekaiId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destTowerKnivesId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destNambaYasakaId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destAmerikamuraId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destDonQuijoteId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destUmedaId, CityId = osaka.CityId },
            new DestinationCity { DestinationId = destBlueBirdsId, CityId = osaka.CityId },
        };
        db.DestinationCities.AddRange(destCityLinks);
        await db.SaveChangesAsync();

        // ─── Transit Routes ────────────────────────────────────────────────────
        var routeTokyoMishimaId = Guid.NewGuid();
        var routeMishimaKawaguchikoId = Guid.NewGuid();
        var routeKawaguchikoKyotoId = Guid.NewGuid();
        var routeKyotoOsakaId = Guid.NewGuid();

        var transitRoutes = new[]
        {
            new TransitRoute
            {
                TransitRouteId = routeTokyoMishimaId,
                OriginCityId = tokyo.CityId,
                DestinationCityId = mishima.CityId,
                TransitType = "Shinkansen",
                EstimatedCostPerPerson = 120m,
                DurationInMinutes = 55,
                RecommendedTimeBufferMinutes = 30,
                BookingReferenceUrl = "https://www.jrpass.com/",
                CarbonFootprintKg = "5.2",
                SubSegmentsJson = "[]"
            },
            new TransitRoute
            {
                TransitRouteId = routeMishimaKawaguchikoId,
                OriginCityId = mishima.CityId,
                DestinationCityId = kawaguchiko.CityId,
                TransitType = "Rental Car",
                EstimatedCostPerPerson = 40m,
                DurationInMinutes = 120,
                RecommendedTimeBufferMinutes = 30,
                BookingReferenceUrl = "https://www.toyota-rentacar.com/",
                CarbonFootprintKg = "10.0",
                SubSegmentsJson = "[]"
            },
            new TransitRoute
            {
                TransitRouteId = routeKawaguchikoKyotoId,
                OriginCityId = kawaguchiko.CityId,
                DestinationCityId = kyoto.CityId,
                TransitType = "Shinkansen",
                EstimatedCostPerPerson = 150m,
                DurationInMinutes = 150,
                RecommendedTimeBufferMinutes = 30,
                BookingReferenceUrl = "https://www.jrpass.com/",
                CarbonFootprintKg = "8.0",
                SubSegmentsJson = "[]"
            },
            new TransitRoute
            {
                TransitRouteId = routeKyotoOsakaId,
                OriginCityId = kyoto.CityId,
                DestinationCityId = osaka.CityId,
                TransitType = "Local Train",
                EstimatedCostPerPerson = 10m,
                DurationInMinutes = 30,
                RecommendedTimeBufferMinutes = 15,
                BookingReferenceUrl = "https://www.jrpass.com/",
                CarbonFootprintKg = "1.0",
                SubSegmentsJson = "[]"
            }
        };
        db.TransitRoutes.AddRange(transitRoutes);
        await db.SaveChangesAsync();

        // ─── Wishlist ─────────────────────────────────────────────────────────
        var wishlistId = Guid.NewGuid();

        var wishlist = new Wishlist
        {
            WishlistId = wishlistId,
            WishlistName = "14-Day Japan Adventure: Tokyo, Fuji, Kyoto & Osaka",
            WishlistDescription = "Based on Rok Mocnik's epic 14-day Japan trip covering the highlights from Tokyo's neon streets to Kyoto's traditional temples and Osaka's chaotic energy.",
            ShortStory = "14 days of neon, temples, and the unexpected—following Rok's journey through Japan's must-see cities.",
            TotalDays = 14,
            PeopleType = "Young Couple / Adventurous Travelers",
            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_japan_hero.jpg",
            GlobalInclusionsJson = @"[""JR Pass (7-day) "",""Hotel Breakfast"",""Pocket WiFi""]",
            RawContentKeywords = "Japan, Tokyo, Mount Fuji, Kyoto, Osaka, Shibuya, Shinjuku, Asakusa, DisneySea, Chureito Pagoda, Nintendo Museum, Fushimi Inari, Universal Studios, Dotonbori",
            PsychologicalVibeTagsJson = @"[""Aesthetic"",""Foodie"",""Adventure"",""Culture""]",
            DefaultTravelersCount = 2,
            BasePricePerPerson = 2500m,
            CalculatedTotalCost = 5000m,
            DepositAmountRequired = 500m,
            AccommodationInclusions = "3-star hotels in Tokyo, Kyoto, Osaka; 1 night ryokan in Kawaguchiko",
            TransitInclusions = "Shinkansen train, car rental for Fuji area, local trains",
            ActivityInclusions = "Entry to DisneySea, Universal Studios (with Super Nintendo World), Nintendo Museum, Kyoto Railway Museum",
            IsTemplate = true,
            IsFeatured = true,
            PrimaryPersonaTarget = "Adventurous Couple",
            CreatedAt = DateTimeOffset.UtcNow,
            OwnerUserId = null,
            ForkedFromId = null
        };
        db.Wishlists.Add(wishlist);
        await db.SaveChangesAsync();

        // ─── Creator ──────────────────────────────────────────────────────────
        var creatorId = Guid.NewGuid();
        var creator = new Creator
        {
            CreatorId = creatorId,
            DisplayName = "Rok Mocnik",
            Handle = "rok_mocnik",
            PlatformName = "TikTok",
            ProfileUrl = "https://www.tiktok.com/@rok_mocnik?_r=1&_t=ZS-97GUeA6Ygop",
            AvatarUrl = null,
            Bio = "Dad filming everything.",
            ContactEmail = "contact@roktraveldad.com",
            IsVerified = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Creators.Add(creator);
        await db.SaveChangesAsync();

        // ─── WishlistCreatorAttribution ───────────────────────────────────────
        var attribution = new WishlistCreatorAttribution
        {
            WishlistCreatorAttributionId = Guid.NewGuid(),
            WishlistId = wishlistId,
            CreatorId = creatorId,
            OriginalContentUrl = "https://vt.tiktok.com/ZSQvNk4Da/",
            PermissionType = "Verbal",
            PermissionGrantedAt = DateTimeOffset.UtcNow,
            PermissionEvidenceUrl = null,
            IsActive = true,
            AttributionNote = "Based on creator's video content; no formal contract."
        };
        db.WishlistCreatorAttributions.Add(attribution);
        await db.SaveChangesAsync();

        // ─── Itinerary Days & Items ──────────────────────────────────────
        // Day 1: Tokyo – Shibuya & Shinjuku
        var day1 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 1,
            DayTitle = "Tokyo: Shibuya & Shinjuku",
            MorningCityId = tokyo.CityId,
            AfternoonCityId = tokyo.CityId,
            EveningCityId = tokyo.CityId,
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
        ItemTitle = "Shibuya Crossing & Hachiko Statue",
        ItemDescription = "Witness the famous scramble crossing and meet Hachiko. Rok says: 'The chaos is the charm.'",
        ItemOrderIndex = 1,
        TimeOfDay = "Morning",
        ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/shibuya_crossing.jpg",
        SocialProofBadge = "Iconic Spot",
        IndividualCostModifier = 0,
        IsOptionalActivity = false,
        IsSelectedByDefault = true
    },
    new ItineraryItem
    {
        ItineraryItemId = Guid.NewGuid(),
        ItineraryDayId = day1.ItineraryDayId,
        ItemTitle = "Tokyo Metropolitan Government Building – Free Observatory",
        ItemDescription = "Get panoramic views of Tokyo for free. A great intro to the city's scale.",
        ItemOrderIndex = 2,   // re‑numbered from 3 to 2
        TimeOfDay = "Evening",
        ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tokyo_metropolitan.jpg",
        SocialProofBadge = "Budget Friendly",
        IndividualCostModifier = 0,
        IsOptionalActivity = true,
        IsSelectedByDefault = true
    }
);
        await db.SaveChangesAsync();

        // Day 2: Tokyo – Asakusa & Ginza
        var day2 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 2,
            DayTitle = "Tokyo: Asakusa & Ginza",
            MorningCityId = tokyo.CityId,
            AfternoonCityId = tokyo.CityId,
            EveningCityId = tokyo.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day2);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day2.ItineraryDayId,
                ItemTitle = "Senso-ji Temple & Nakamise-dori",
                ItemDescription = "Visit Tokyo's oldest temple and stroll the bustling shopping street.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/sensoji.jpg",
                SocialProofBadge = "Cultural Highlight",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day2.ItineraryDayId,
                ItemTitle = "Ginza Shopping & Kabuki-za Theatre",
                ItemDescription = "Luxury shopping and maybe catch a glimpse of Kabuki.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_ginza.jpg",
                SocialProofBadge = "High-End",
                IndividualCostModifier = 0,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day2.ItineraryDayId,
                ItemTitle = "Robot Restaurant Show (optional)",
                ItemDescription = "A wild, over-the-top robot performance – pure Tokyo kitsch.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_robot.jpeg",
                SocialProofBadge = "Only in Tokyo",
                IndividualCostModifier = 80m,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            }
        );
        await db.SaveChangesAsync();

        // Day 3: Tokyo DisneySea
        var day3 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 3,
            DayTitle = "Tokyo DisneySea",
            MorningCityId = tokyo.CityId,
            AfternoonCityId = tokyo.CityId,
            EveningCityId = tokyo.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day3);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day3.ItineraryDayId,
                ItemTitle = "Tokyo DisneySea – Full Day",
                ItemDescription = "Experience the unique DisneySea park. Arrive early to avoid insane lines!",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_disneysea.jpeg",
                SocialProofBadge = "Must-Do",
                IndividualCostModifier = 84m,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day3.ItineraryDayId,
                ItemTitle = "Dinner at Ikspiari (Disney's retail complex)",
                ItemDescription = "End the day with a variety of dining options.",
                ItemOrderIndex = 2,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_ikspiari.jpeg",
                SocialProofBadge = "",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        // Day 4: Tokyo – Chill & Explore
        var day4 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 4,
            DayTitle = "Tokyo – Chill & Explore",
            MorningCityId = tokyo.CityId,
            AfternoonCityId = tokyo.CityId,
            EveningCityId = tokyo.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day4);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day4.ItineraryDayId,
                ItemTitle = "Harajuku – Takeshita Street",
                ItemDescription = "Youth culture and quirky shops.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_harajuku.jpg",
                SocialProofBadge = "Trendy",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day4.ItineraryDayId,
                ItemTitle = "Meiji Shrine",
                ItemDescription = "Peaceful forest shrine opposite Harajuku.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/meiji.jpg",
                SocialProofBadge = "Cultural",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        // Day 5: Tokyo → Fuji Area (Mishima & Kawaguchiko)
        var day5 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 5,
            DayTitle = "Tokyo → Fuji Area (Mishima & Kawaguchiko)",
            MorningCityId = tokyo.CityId,
            AfternoonCityId = mishima.CityId,
            EveningCityId = kawaguchiko.CityId,
            TransitFromPreviousDayRouteId = routeTokyoMishimaId,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day5);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day5.ItineraryDayId,
                ItemTitle = "Shinkansen to Mishima",
                ItemDescription = "Take the bullet train from Tokyo to Mishima (approx. 55 min).",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "",
                SocialProofBadge = "Fast & Scenic",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day5.ItineraryDayId,
                ItemTitle = "Rent a Car & Drive to Kawaguchiko",
                ItemDescription = "Pick up a rental car in Mishima and drive to Lake Kawaguchiko (approx. 2 hours).",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "",
                SocialProofBadge = "Road Trip",
                IndividualCostModifier = 40m,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day5.ItineraryDayId,
                ItemTitle = "Chureito Pagoda (Sunset)",
                ItemDescription = "Visit the iconic pagoda with Mt. Fuji views. Fuji may be shy.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_chureitopagoda.jpg",
                SocialProofBadge = "Photography Spot",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        // Day 6: Fuji Area – Speedway & Ninja
        var day6 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 6,
            DayTitle = "Fuji Area: Speedway & Ninja",
            MorningCityId = kawaguchiko.CityId,
            AfternoonCityId = kawaguchiko.CityId,
            EveningCityId = kawaguchiko.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day6);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day6.ItineraryDayId,
                ItemTitle = "Fuji Speedway Museum",
                ItemDescription = "Explore race cars and simulators – a petrolhead's dream.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_fujispeedway.jpeg",
                SocialProofBadge = "For Petrolheads",
                IndividualCostModifier = 15m,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day6.ItineraryDayId,
                ItemTitle = "Ninja Village",
                ItemDescription = "Fun ninja shows and obstacle courses – great for families.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_ninjavillage.jpg",
                SocialProofBadge = "Family Fun",
                IndividualCostModifier = 20m,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day6.ItineraryDayId,
                ItemTitle = "Relax at Lake Kawaguchiko Onsen",
                ItemDescription = "Soak in a hot spring with views of Fuji (if visible).",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_lakekawaguchioonsen.jpg",
                SocialProofBadge = "Must-Do",
                IndividualCostModifier = 25m,
                IsOptionalActivity = true,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        // Day 7: Kawaguchiko → Kyoto & Fushimi Inari
        var day7 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 7,
            DayTitle = "Kawaguchiko → Kyoto & Fushimi Inari",
            MorningCityId = kawaguchiko.CityId,
            AfternoonCityId = kyoto.CityId,
            EveningCityId = kyoto.CityId,
            TransitFromPreviousDayRouteId = routeKawaguchikoKyotoId,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day7);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day7.ItineraryDayId,
                ItemTitle = "Train to Kyoto (via Mishima)",
                ItemDescription = "Return car, take Shinkansen to Kyoto. About 2.5 hours.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "",
                SocialProofBadge = "",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day7.ItineraryDayId,
                ItemTitle = "Fushimi Inari Taisha",
                ItemDescription = "Hike through the thousand torii gates. Go early to avoid crowds – Rok rated it 9/10.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_fushimiinari.jpg",
                SocialProofBadge = "Iconic",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day7.ItineraryDayId,
                ItemTitle = "Gion District (Evening)",
                ItemDescription = "Wander the historic geisha district. Maybe spot a maiko.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_giondistrict.jpg",
                SocialProofBadge = "Atmospheric",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        // Day 8: Kyoto – Kiyomizu-dera & Higashiyama
        var day8 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 8,
            DayTitle = "Kyoto: Kiyomizu-dera & Higashiyama",
            MorningCityId = kyoto.CityId,
            AfternoonCityId = kyoto.CityId,
            EveningCityId = kyoto.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day8);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day8.ItineraryDayId,
                ItemTitle = "Kiyomizu-dera Temple",
                ItemDescription = "Rok rated it 1000/10! Traditional streets, views, and unreal architecture.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_kiyomizudera.jpg",
                SocialProofBadge = "Top Rated",
                IndividualCostModifier = 4m,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day8.ItineraryDayId,
                ItemTitle = "Sannen-zaka & Ninen-zaka Streets",
                ItemDescription = "Stroll the preserved slopes with tea houses and souvenir shops.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_sannenzaka.jpg",
                SocialProofBadge = "Picturesque",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day8.ItineraryDayId,
                ItemTitle = "Yasaka Pagoda (Evening illumination)",
                ItemDescription = "Beautiful pagoda lit up at night.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_yasakapagoda.jpg",
                SocialProofBadge = "Night View",
                IndividualCostModifier = 0,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            }
        );
        await db.SaveChangesAsync();

        // Day 9: Kyoto – Nintendo Museum & Nishiki Market
        var day9 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 9,
            DayTitle = "Kyoto: Nintendo Museum & Nishiki Market",
            MorningCityId = kyoto.CityId,
            AfternoonCityId = kyoto.CityId,
            EveningCityId = kyoto.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day9);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day9.ItineraryDayId,
                ItemTitle = "Nintendo Museum (Uji)",
                ItemDescription = "Rok loved it (8/10). Tickets are hard to get – book well in advance.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_nintendo.jpeg",
                SocialProofBadge = "Gamer's Paradise",
                IndividualCostModifier = 20m,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day9.ItineraryDayId,
                ItemTitle = "Nishiki Market",
                ItemDescription = "Touristy but fun (8/10). Sample street food and local crafts.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_nishikimarket.jpg",
                SocialProofBadge = "Foodie Spot",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day9.ItineraryDayId,
                ItemTitle = "Pontocho Alley (Evening dining)",
                ItemDescription = "Narrow alley with riverside restaurants – perfect for dinner.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_pontocho.jpg",
                SocialProofBadge = "Romantic",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        // Day 10: Kyoto – Railway Museum & Relax
        var day10 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 10,
            DayTitle = "Kyoto: Railway Museum & Relax",
            MorningCityId = kyoto.CityId,
            AfternoonCityId = kyoto.CityId,
            EveningCityId = kyoto.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day10);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day10.ItineraryDayId,
                ItemTitle = "Kyoto Railway Museum",
                ItemDescription = "Fun for families (7/10). Kids under 3 free. Watch real Shinkansen pass. Thomas the Train was there.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_railwaymuseum.jpeg",
                SocialProofBadge = "Family Friendly",
                IndividualCostModifier = 12m,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day10.ItineraryDayId,
                ItemTitle = "Arashiyama Bamboo Grove (if time)",
                ItemDescription = "Iconic bamboo forest – though often crowded.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_arashiyamabamboogrove.jpg",
                SocialProofBadge = "Photogenic",
                IndividualCostModifier = 0,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day10.ItineraryDayId,
                ItemTitle = "Kinkaku-ji (Golden Pavilion) – optional",
                ItemDescription = "Stunning golden temple – worth a visit if time allows.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_kinkakuji.jpg",
                SocialProofBadge = "UNESCO",
                IndividualCostModifier = 5m,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            }
        );
        await db.SaveChangesAsync();

        // Day 11: Kyoto → Osaka & Dotonbori
        var day11 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 11,
            DayTitle = "Kyoto → Osaka & Dotonbori",
            MorningCityId = kyoto.CityId,
            AfternoonCityId = osaka.CityId,
            EveningCityId = osaka.CityId,
            TransitFromPreviousDayRouteId = routeKyotoOsakaId,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day11);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day11.ItineraryDayId,
                ItemTitle = "Local Train to Osaka",
                ItemDescription = "Quick 30-minute ride from Kyoto.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "",
                SocialProofBadge = "",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day11.ItineraryDayId,
                ItemTitle = "Dotonbori – The Heart of Osaka",
                ItemDescription = "Experience the neon chaos. If it's Halloween, it's even crazier!",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_dotonbori.jpg",
                SocialProofBadge = "Must-See",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day11.ItineraryDayId,
                ItemTitle = "Street Food Tour (Takoyaki, Okonomiyaki)",
                ItemDescription = "Sample Osaka's famous street eats.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_takoyaki.jpg",
                SocialProofBadge = "Foodie",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        // Day 12: Universal Studios Japan
        var day12 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 12,
            DayTitle = "Universal Studios Japan",
            MorningCityId = osaka.CityId,
            AfternoonCityId = osaka.CityId,
            EveningCityId = osaka.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day12);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day12.ItineraryDayId,
                ItemTitle = "Universal Studios – Full Day",
                ItemDescription = "Arrive early to beat the insane lines. Must-do: Super Nintendo World.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_universalstudio.jpeg",
                SocialProofBadge = "Top Attraction",
                IndividualCostModifier = 98m,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day12.ItineraryDayId,
                ItemTitle = "Super Nintendo World",
                ItemDescription = "Rok gave it 10/10 – immersive Mario land.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_supernintendo.jpeg",
                SocialProofBadge = "Must-Do",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        // Day 13: Osaka – Castle, Shinsekai & Retro
        var day13 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 13,
            DayTitle = "Osaka: Castle, Shinsekai & Retro",
            MorningCityId = osaka.CityId,
            AfternoonCityId = osaka.CityId,
            EveningCityId = osaka.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day13);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day13.ItineraryDayId,
                ItemTitle = "Osaka Castle & Blue Birds Terrace",
                ItemDescription = "Explore the castle grounds and enjoy an amazing view from the terrace.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_bluebird.jpeg",
                SocialProofBadge = "Historic",
                IndividualCostModifier = 6m,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day13.ItineraryDayId,
                ItemTitle = "Shinsekai (Retro District)",
                ItemDescription = "Loved the retro vibes – old-school Osaka at its best.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_shinsekai.jpg",
                SocialProofBadge = "Nostalgic",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day13.ItineraryDayId,
                ItemTitle = "Tower Knives Osaka",
                ItemDescription = "Famous knife shop for culinary enthusiasts.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_towerknives.jpg",
                SocialProofBadge = "Unique Souvenir",
                IndividualCostModifier = 50m,
                IsOptionalActivity = true,
                IsSelectedByDefault = false
            }
        );
        await db.SaveChangesAsync();

        // Day 14: Osaka – Namba, Amerikamura & Don Quijote
        var day14 = new ItineraryDay
        {
            ItineraryDayId = Guid.NewGuid(),
            DayNumber = 14,
            DayTitle = "Osaka: Namba, Amerikamura & Don Quijote",
            MorningCityId = osaka.CityId,
            AfternoonCityId = osaka.CityId,
            EveningCityId = osaka.CityId,
            TransitFromPreviousDayRouteId = null,
            WishlistId = wishlistId
        };
        db.ItineraryDays.Add(day14);
        await db.SaveChangesAsync();

        db.ItineraryItems.AddRange(
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day14.ItineraryDayId,
                ItemTitle = "Namba Yasaka Shrine (Lion Head)",
                ItemDescription = "Unique shrine with a giant lion head – chaotic and fun.",
                ItemOrderIndex = 1,
                TimeOfDay = "Morning",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_nambayasakashrine.jpg",
                SocialProofBadge = "Quirky",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day14.ItineraryDayId,
                ItemTitle = "Amerikamura (American Village)",
                ItemDescription = "Youth culture hub – messy, loud, chaotic and extremely fun.",
                ItemOrderIndex = 2,
                TimeOfDay = "Afternoon",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_amerikamura.jpeg",
                SocialProofBadge = "Trendy",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            },
            new ItineraryItem
            {
                ItineraryItemId = Guid.NewGuid(),
                ItineraryDayId = day14.ItineraryDayId,
                ItemTitle = "Don Quijote – The Maze-like Store",
                ItemDescription = "Overwhelming discount store – an experience in itself.",
                ItemOrderIndex = 3,
                TimeOfDay = "Evening",
                ImageUrl = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_donquijote.jpeg",
                SocialProofBadge = "Must-Do",
                IndividualCostModifier = 0,
                IsOptionalActivity = false,
                IsSelectedByDefault = true
            }
        );
        await db.SaveChangesAsync();

        foreach (var dest in allDestinations)
        {
            // Check if the relationship already exists (to avoid duplicates on re‑run)
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

    private static async Task SeedImagesAsync(HodracDbContext db, List<Destination> destinations)
    {
        List<string> imageNames = new List<string>{
        "shibuya.jpg",
        "shinjuku.jpeg",
        "ginza.jpg",
        "disneysea.jpeg",
        "chureitopagoda.jpg",
        "fujispeedway.jpeg",
        "ninjavillage.jpg",
        "fushimiinari.jpg",
        "kiyomizudera.jpg",
        "nintendo.jpeg",
        "nishikimarket.jpg",
        "railwaymuseum.jpeg",
        "universalstudio.jpeg",
        "supernintendo.jpeg",
        "dotonbori.jpg",
        "osakacastle.jpg",
        "shinsekai.jpg",
        "towerknives.jpg",
        "nambayasakashrine.jpg",
        "amerikamura.jpeg",
        "donquijote.jpeg",
        "umedasky.jpg",
        "bluebird.jpeg"
    };

        // Use Zip to pair destinations with images (up to the shorter list)
        foreach (var (dest, image) in destinations.Zip(imageNames, (d, i) => (d, i)))
        {
            db.DestinationImages.Add(new DestinationImage
            {
                DestinationImageId = Guid.NewGuid(),
                DestinationId = dest.DestinationId,
                ImageUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_{Uri.EscapeDataString(image)}",
                ThumbnailUrl = $"https://wangq4yhmf94epv8.public.blob.vercel-storage.com/rox_{Uri.EscapeDataString(image)}",
                Caption = $"Hero image of {dest.DestinationName}",
                DisplayOrder = 1,
                ImageType = "Hero",
                ShotContext = "Exterior",
                IsAiGenerated = false
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedDestinationLanguagesAndCurrenciesAsync(
        HodracDbContext db,
        List<Destination> destinations)
    {
        var japanese = await db.Languages.FirstOrDefaultAsync(l => l.LanguageName == "Japanese");
        if (japanese == null)
            throw new Exception("Language 'Japanese' not found. Seed Languages first.");

        var yen = await db.Currencies.FirstOrDefaultAsync(c => c.CurrencyCode == "JPY");
        if (yen == null)
            throw new Exception("Currency 'JPY' not found. Seed Currencies first.");

        foreach (var dest in destinations)
        {
            bool langExists = await db.DestinationLanguages
                .AnyAsync(dl => dl.DestinationId == dest.DestinationId && dl.LanguageId == japanese.LanguageId);

            if (!langExists)
            {
                db.DestinationLanguages.Add(new DestinationLanguage
                {
                    DestinationId = dest.DestinationId,
                    LanguageId = japanese.LanguageId
                });
            }

            bool currencyExists = await db.DestinationCurrencies
                .AnyAsync(dc => dc.DestinationId == dest.DestinationId && dc.CurrencyId == yen.CurrencyId);

            if (!currencyExists)
            {
                db.DestinationCurrencies.Add(new DestinationCurrency
                {
                    DestinationId = dest.DestinationId,
                    CurrencyId = yen.CurrencyId
                });
            }
        }

        await db.SaveChangesAsync();
    }

    // Helper extension method for title case
    public static string ToTitleCase(this string str)
    {
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(str.ToLower());
    }
}