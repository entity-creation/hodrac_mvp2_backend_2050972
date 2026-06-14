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
public static class DataSeeder
{
    // ─── Entry point ──────────────────────────────────────────────────────────

    public static async Task SeedAllAsync(HodracDbContext db)
    {
        await SeedLanguagesAsync(db);
        await SeedCurrenciesAsync(db);
        await SeedCountriesAsync(db);
        await SeedCitiesAsync(db);
        await SeedCategoriesAsync(db);
        await SeedTagsAsync(db);
        await SeedDestinationsAsync(db);
        await SeedWishlistsAsync(db);
        await SeedFeaturedPoolAsync(db);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 1. LANGUAGES
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedLanguagesAsync(HodracDbContext db)
    {
        if (await db.Languages.AnyAsync()) return;

        db.Languages.AddRange(
            new Language { LanguageId = Guid.NewGuid(), LanguageName = "Japanese", LanguageCode = "ja", RequiresCertifiedLocalGuide = false },
            new Language { LanguageId = Guid.NewGuid(), LanguageName = "Spanish", LanguageCode = "es", RequiresCertifiedLocalGuide = false },
            new Language { LanguageId = Guid.NewGuid(), LanguageName = "English", LanguageCode = "en", RequiresCertifiedLocalGuide = false },
            new Language
            {
                LanguageId = Guid.NewGuid(),
                LanguageName = "Dulegaya (Guna/Kuna)",
                LanguageCode = "cuk",
                RequiresCertifiedLocalGuide = true,
                HelpfulSurvivalPhrasesJson = JsonSerializer.Serialize(new[]
                {
                    new { phrase = "Hello",    local = "Degi" },
                    new { phrase = "Thank you", local = "Nued" },
                    new { phrase = "Yes",      local = "Ehe" },
                    new { phrase = "No",       local = "Suli" },
                })
            }
        );
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 2. CURRENCIES
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedCurrenciesAsync(HodracDbContext db)
    {
        if (await db.Currencies.AnyAsync()) return;

        db.Currencies.AddRange(
            new Currency { CurrencyId = Guid.NewGuid(), CurrencyName = "Japanese Yen", CurrencyCode = "JPY", CurrencySymbol = "¥", ExchangeRateToBase = 0.0067m, LastExchangeRateUpdate = DateTimeOffset.UtcNow },
            new Currency { CurrencyId = Guid.NewGuid(), CurrencyName = "United States Dollar", CurrencyCode = "USD", CurrencySymbol = "$", ExchangeRateToBase = 1.0000m, LastExchangeRateUpdate = DateTimeOffset.UtcNow },
            new Currency { CurrencyId = Guid.NewGuid(), CurrencyName = "Panamanian Balboa", CurrencyCode = "PAB", CurrencySymbol = "B/.", ExchangeRateToBase = 1.0000m, LastExchangeRateUpdate = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 3. COUNTRIES
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedCountriesAsync(HodracDbContext db)
    {
        if (await db.Countries.AnyAsync()) return;

        var japanese = await db.Languages.FirstAsync(l => l.LanguageName == "Japanese");
        var spanish = await db.Languages.FirstAsync(l => l.LanguageName == "Spanish");
        var english = await db.Languages.FirstAsync(l => l.LanguageName == "English");

        var japan = new Country
        {
            CountryId = Guid.NewGuid(),
            CountryName = "Japan",
            Continent = "Asia",
            CountryFlagEmoji = "🇯🇵",
            GlobalHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tokyo.jpg",
            VisaRequirementsSummary = "Visa-free for most nationalities for up to 90 days.",
            PowerPlugType = "Type A/B",
            DrivingSide = "Left",
            EstimatedDailyTaxRate = 0.10m,
        };
        japan.CountryLanguages = new List<CountryLanguage>
        {
            new() { CountryId = japan.CountryId, LanguageId = japanese.LanguageId }
        };

        var panama = new Country
        {
            CountryId = Guid.NewGuid(),
            CountryName = "Panama",
            Continent = "Central America",
            CountryFlagEmoji = "🇵🇦",
            GlobalHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/panamacity.jpeg",
            VisaRequirementsSummary = "Visa-free for US, EU, and most Commonwealth citizens for up to 180 days.",
            PowerPlugType = "Type A/B",
            DrivingSide = "Right",
            EstimatedDailyTaxRate = 0.07m,
        };
        panama.CountryLanguages = new List<CountryLanguage>
        {
            new() { CountryId = panama.CountryId, LanguageId = spanish.LanguageId },
            new() { CountryId = panama.CountryId, LanguageId = english.LanguageId },
        };

        db.Countries.AddRange(japan, panama);
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 4. CITIES
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedCitiesAsync(HodracDbContext db)
    {
        if (await db.Cities.AnyAsync()) return;

        var japan = await db.Countries.FirstAsync(c => c.CountryName == "Japan");
        var panama = await db.Countries.FirstAsync(c => c.CountryName == "Panama");

        db.Cities.AddRange(
            new City { CityId = Guid.NewGuid(), CountryId = japan.CountryId, CityName = "Tokyo", CityDescription = "Japan's capital and the world's most populous metropolitan area.", Latitude = 35.6762, Longitude = 139.6503 },
            new City { CityId = Guid.NewGuid(), CountryId = panama.CountryId, CityName = "Panama City", CityDescription = "The capital and largest city of Panama, on the Pacific coast.", Latitude = 8.9936, Longitude = -79.5197 },
            new City { CityId = Guid.NewGuid(), CountryId = panama.CountryId, CityName = "Guna Yala", CityDescription = "The autonomous comarca of the Guna people, San Blas archipelago.", Latitude = 9.2477, Longitude = -78.1827 },
            new City { CityId = Guid.NewGuid(), CountryId = panama.CountryId, CityName = "Caribbean coast of Panama (Northwest)", CityDescription = "Bocas del Toro province on Panama's Caribbean coast.", Latitude = 9.3399, Longitude = -82.2521 }
        );
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 5. CATEGORIES
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedCategoriesAsync(HodracDbContext db)
    {
        if (await db.Categories.AnyAsync()) return;

        db.Categories.AddRange(
            new Category { CategoryId = Guid.NewGuid(), Key = "cultural_site", CategoryName = "Cultural Site", CategoryDescription = "Temples, shrines, mosques, and heritage sites.", IconName = "landmark", ColorHex = "#6366f1" },
            new Category { CategoryId = Guid.NewGuid(), Key = "neighborhood_district", CategoryName = "Neighborhood & District", CategoryDescription = "Walkable urban areas with local character.", IconName = "map", ColorHex = "#8b5cf6" },
            new Category { CategoryId = Guid.NewGuid(), Key = "viewpoint_scenic_spot", CategoryName = "Viewpoint & Scenic Spot", CategoryDescription = "Observation decks, hilltops, and panoramic vistas.", IconName = "eye", ColorHex = "#0891b2" },
            new Category { CategoryId = Guid.NewGuid(), Key = "landmark_monument", CategoryName = "Landmark & Monument", CategoryDescription = "Iconic structures and historic monuments.", IconName = "building", ColorHex = "#f59e0b" },
            new Category { CategoryId = Guid.NewGuid(), Key = "market_street_life", CategoryName = "Market & Street Life", CategoryDescription = "Food markets, street stalls, and bazaars.", IconName = "shopping-bag", ColorHex = "#10b981" },
            new Category { CategoryId = Guid.NewGuid(), Key = "activity_experience", CategoryName = "Activity & Experience", CategoryDescription = "Theme parks, guided tours, and immersive activities.", IconName = "star", ColorHex = "#ec4899" },
            new Category { CategoryId = Guid.NewGuid(), Key = "nature_outdoor", CategoryName = "Nature & Outdoors", CategoryDescription = "Beaches, parks, forests, and natural landscapes.", IconName = "tree", ColorHex = "#22c55e" },
            new Category { CategoryId = Guid.NewGuid(), Key = "entertainment_nightlife", CategoryName = "Entertainment & Nightlife", CategoryDescription = "Bars, clubs, theaters, and live entertainment.", IconName = "music", ColorHex = "#7c3aed" },
            new Category { CategoryId = Guid.NewGuid(), Key = "food_experience", CategoryName = "Food Experience", CategoryDescription = "Restaurants, cafes, and culinary destinations.", IconName = "utensils", ColorHex = "#f97316" }
        );
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 6. TAGS
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedTagsAsync(HodracDbContext db)
    {
        if (await db.Tags.AnyAsync()) return;

        db.Tags.AddRange(
            // Style
            new Tag { TagId = Guid.NewGuid(), Key = "walkable", TagName = "Walkable", TargetPersonaType = "Explorer" },
            new Tag { TagId = Guid.NewGuid(), Key = "cultural", TagName = "Cultural", TargetPersonaType = "Culture Seeker" },
            new Tag { TagId = Guid.NewGuid(), Key = "history", TagName = "History", TargetPersonaType = "Culture Seeker" },
            new Tag { TagId = Guid.NewGuid(), Key = "photography", TagName = "Photography", TargetPersonaType = "Photographer" },
            new Tag { TagId = Guid.NewGuid(), Key = "architecture", TagName = "Architecture", TargetPersonaType = "Design Lover" },
            new Tag { TagId = Guid.NewGuid(), Key = "nature", TagName = "Nature", TargetPersonaType = "Nature Lover" },
            new Tag { TagId = Guid.NewGuid(), Key = "food_focused", TagName = "Food", TargetPersonaType = "Foodie" },
            new Tag { TagId = Guid.NewGuid(), Key = "shopping", TagName = "Shopping", TargetPersonaType = "Shopper" },
            new Tag { TagId = Guid.NewGuid(), Key = "nightlife", TagName = "Nightlife", TargetPersonaType = "Night Owl" },
            new Tag { TagId = Guid.NewGuid(), Key = "educational", TagName = "Educational", TargetPersonaType = "Learner" },
            // Vibe
            new Tag { TagId = Guid.NewGuid(), Key = "relaxing", TagName = "Relaxation", TargetPersonaType = "Wellness Seeker" },
            new Tag { TagId = Guid.NewGuid(), Key = "adventurous", TagName = "Adventure", TargetPersonaType = "Adventurer" },
            new Tag { TagId = Guid.NewGuid(), Key = "romantic", TagName = "Romance", TargetPersonaType = "Couple" },
            new Tag { TagId = Guid.NewGuid(), Key = "social", TagName = "Social", TargetPersonaType = "Social Traveler" },
            // Audience
            new Tag { TagId = Guid.NewGuid(), Key = "family_friendly", TagName = "Family Friendly", TargetPersonaType = "Family" },
            new Tag { TagId = Guid.NewGuid(), Key = "couple_friendly", TagName = "Couple Friendly", TargetPersonaType = "Couple" },
            new Tag { TagId = Guid.NewGuid(), Key = "solo_friendly", TagName = "Solo Friendly", TargetPersonaType = "Solo Traveler" },
            new Tag { TagId = Guid.NewGuid(), Key = "group_friendly", TagName = "Group Friendly", TargetPersonaType = "Group" },
            // Crowd / access
            new Tag { TagId = Guid.NewGuid(), Key = "tourist_hotspot", TagName = "Tourist Hotspot", TargetPersonaType = "First-Timer" },
            new Tag { TagId = Guid.NewGuid(), Key = "local_favorite", TagName = "Local Favorite", TargetPersonaType = "Authentic Seeker" },
            new Tag { TagId = Guid.NewGuid(), Key = "hidden_gem", TagName = "Hidden Gem", TargetPersonaType = "Explorer" },
            new Tag { TagId = Guid.NewGuid(), Key = "crowded", TagName = "Crowded", TargetPersonaType = "First-Timer" },
            new Tag { TagId = Guid.NewGuid(), Key = "budget_friendly", TagName = "Budget", TargetPersonaType = "Budget Traveler" },
            new Tag { TagId = Guid.NewGuid(), Key = "premium", TagName = "Luxury", TargetPersonaType = "Luxury Traveler" },
            // Timing
            new Tag { TagId = Guid.NewGuid(), Key = "best_morning", TagName = "Best in Morning", TargetPersonaType = "Early Bird" },
            new Tag { TagId = Guid.NewGuid(), Key = "best_at_night", TagName = "Best at Night", TargetPersonaType = "Night Owl" },
            new Tag { TagId = Guid.NewGuid(), Key = "best_evening", TagName = "Best at Evening", TargetPersonaType = "Explorer" },
            new Tag { TagId = Guid.NewGuid(), Key = "good_short_visit", TagName = "Good Short Visit", TargetPersonaType = "Busy Traveler" },
            new Tag { TagId = Guid.NewGuid(), Key = "seasonal", TagName = "Seasonal", TargetPersonaType = "Culture Seeker" }
        );
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 7. DESTINATIONS
    // Migrates all 29 destinations from the old seeder.
    // Schema changes handled:
    //   - AverageCostPerDay = (MinCost + MaxCost) / 2
    //   - SafetyLevel: old 1-10 → new 1-5 by mapping
    //   - LuxuryRating: derived from cost and premium tags
    //   - DescriptionJson: serialized from DescriptionJsonDto
    //   - PsychographicVibeTagsJson: derived from tag keys
    //   - FamilyFriendlyScore / AdventurePaceScore: derived from tags
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedDestinationsAsync(HodracDbContext db)
    {
        if (await db.Destinations.AnyAsync()) return;

        // ── Lookups ───────────────────────────────────────────────────────────
        var japan = await db.Countries.FirstAsync(c => c.CountryName == "Japan");
        var panama = await db.Countries.FirstAsync(c => c.CountryName == "Panama");

        var tokyo = await db.Cities.FirstAsync(c => c.CityName == "Tokyo");
        var panamaCity = await db.Cities.FirstAsync(c => c.CityName == "Panama City");
        var gunaYala = await db.Cities.FirstAsync(c => c.CityName == "Guna Yala");
        var bocas = await db.Cities.FirstAsync(c => c.CityName.StartsWith("Caribbean coast"));

        var japanese = await db.Languages.FirstAsync(l => l.LanguageName == "Japanese");
        var spanish = await db.Languages.FirstAsync(l => l.LanguageName == "Spanish");
        var english = await db.Languages.FirstAsync(l => l.LanguageName == "English");
        var guna = await db.Languages.FirstAsync(l => l.LanguageName == "Dulegaya (Guna/Kuna)");

        var jpy = await db.Currencies.FirstAsync(c => c.CurrencyCode == "JPY");
        var usd = await db.Currencies.FirstAsync(c => c.CurrencyCode == "USD");
        var pab = await db.Currencies.FirstAsync(c => c.CurrencyCode == "PAB");

        // Category map
        var cats = await db.Categories.ToDictionaryAsync(c => c.Key);
        // Tag map
        var tags = await db.Tags.ToDictionaryAsync(t => t.Key);

        // ── Helper methods ────────────────────────────────────────────────────

        // Convert old 1-10 safety (10 = safest) to new 1-5 (1 = safest)
        static int MapSafety(int old10)
            => old10 >= 9 ? 1 : old10 >= 7 ? 2 : old10 >= 5 ? 3 : old10 >= 3 ? 4 : 5;

        // Derive luxury rating 1-5 from average cost and tag keys
        static int DeriveLuxury(decimal avgCost, bool hasPremiumTag)
        {
            if (hasPremiumTag || avgCost > 200) return 5;
            if (avgCost > 100) return 4;
            if (avgCost > 40) return 3;
            if (avgCost > 10) return 2;
            return 1;
        }

        // Score helpers
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

        Destination Make(
            string name, string image, DescriptionJsonDto desc,
            decimal minCost, decimal maxCost, int oldSafety,
            Country country, List<City> cities,
            List<Language> langs, List<Currency> currencies,
            List<Category> categories, List<string> tagKeys,
            string timeZone)
        {
            var avg = (minCost + maxCost) / 2m;
            var hasPremium = tagKeys.Contains("premium");
            var vibes = DeriveVibes(tagKeys);
            var slug = name.ToLowerInvariant().Replace(" ", "_").Replace("(", "").Replace(")", "").Replace("'", "").Replace(",", "");

            var dest = new Destination
            {
                DestinationId = Guid.NewGuid(),
                DestinationName = name,
                CleanNormalizedSearchName = name.ToLowerInvariant(),
                DescriptionJson = JsonSerializer.Serialize(desc),
                AverageCostPerDay = avg,
                SafetyLevel = MapSafety(oldSafety),
                LuxuryRating = DeriveLuxury(avg, hasPremium),
                FamilyFriendlyScore = FamilyScore(tagKeys),
                AdventurePaceScore = AdventureScore(tagKeys),
                AestheticTrendScore = tagKeys.Contains("photography") || tagKeys.Contains("architecture") ? 4 : 2,
                PsychographicVibeTagsJson = JsonSerializer.Serialize(vibes),
                TimeZone = timeZone,
                CountryId = country.CountryId,
                SearchHitCount = 0,
                AccessibilityType = "Train",
                Latitude = country.CountryName == "Japan" ? 35.6762 : 8.9936,
                Longitude = country.CountryName == "Japan" ? 139.6503 : -79.5197,
            };

            // Images
            dest.Images = new List<DestinationImage>
            {
                new()
                {
                    DestinationImageId = Guid.NewGuid(),
                    DestinationId      = dest.DestinationId,
                    ImageUrl           = image,
                    ThumbnailUrl       = image,
                    Caption            = name,
                    DisplayOrder       = 0,
                    ImageType          = "Hero",
                    IsAiGenerated      = false,
                }
            };

            // Cities
            dest.DestinationCities = cities.Select(c => new DestinationCity
            {
                DestinationId = dest.DestinationId,
                CityId = c.CityId,
            }).ToList();

            // Languages
            dest.DestinationLanguages = langs.Select(l => new DestinationLanguage
            {
                DestinationId = dest.DestinationId,
                LanguageId = l.LanguageId,
            }).ToList();

            // Currencies
            dest.DestinationCurrencies = currencies.Select(c => new DestinationCurrency
            {
                DestinationId = dest.DestinationId,
                CurrencyId = c.CurrencyId,
            }).ToList();

            // Categories
            dest.DestinationCategories = categories.Select(c => new DestinationCategory
            {
                DestinationId = dest.DestinationId,
                CategoryId = c.CategoryId,
            }).ToList();

            // Tags
            dest.DestinationTags = tagKeys
                .Where(k => tags.ContainsKey(k))
                .Select(k => new DestinationTag
                {
                    DestinationId = dest.DestinationId,
                    TagId = tags[k].TagId,
                }).ToList();

            return dest;
        }

        // ── Tokyo destinations ────────────────────────────────────────────────

        var tokyoLangs = new List<Language> { japanese };
        var tokyoCities = new List<City> { tokyo };
        var tokyoCurrs = new List<Currency> { jpy };

        var destinations = new List<Destination>
        {
            Make("Meiji Shrine",        "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/meiji.jpg",
                BuildDesc("Meiji Shrine is a peaceful Shinto shrine in central Tokyo, surrounded by a 70-hectare forested area. Dedicated to Emperor Meiji and Empress Shoken, it offers a rare moment of calm amid the city's energy.",
                "The shrine is deeply meaningful to Tokyo residents — most visit not for tourism but for personal prayer, new year ceremonies, and traditional Shinto weddings.",
                "Best access via Harajuku Station (JR Yamanote Line). Exit Omotesando exit, cross Jingu-bashi bridge, enter through the large wooden torii gate. 10-minute walk to main shrine along shaded forest path.",
                "Free for main grounds. Inner Garden: 500 yen. Museum: 1,000 yen. Bring 5-yen coins for offerings. Photography forbidden inside main sanctuary.",
                "Beware of the New Year rush (3M+ visitors Jan 1-3). Thick gravel paths are hard on thin-soled shoes and difficult for strollers.",
                "Main grounds free; Inner Garden 500 yen (~$3.33); Museum 1,000 yen (~$6.67); Amulets 500-1,500 yen.",
                new[] { "Yoyogi Park", "Harajuku (Takeshita Street)", "Omotesando", "Shibuya Crossing" },
                "Spring festival late April–May; mid-June for iris blooms; sunrise for crowds-free experience.",
                "High (8/10) — avoid Jan 1-3.",
                "8/10 — wide flat paths but thick gravel is challenging for wheelchairs.",
                "1.5 to 2.5 hours"),
                0, 4, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["cultural_site"] },
                new List<string> { "cultural", "walkable", "history", "budget_friendly", "photography" },
                "Japan Standard Time"),

            Make("Takeshita Street",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/takeshita.jpg",
                BuildDesc("The global epicenter of Kawaii culture — a 350-meter pedestrian shopping street in Harajuku packed with youth fashion boutiques, themed cafes, and street food.",
                "For Japanese youth this is a space of self-expression, not a tourist attraction. The outfits here are real subcultures, not costumes.",
                "JR Harajuku Station, Takeshita Exit. Cross the street at the designated crosswalk to the arched gate directly ahead.",
                "Most snacks cost 600-1,000 yen. Cash-only at many small stalls. No public trash cans — finish snacks at the stall.",
                "Extremely crowded on weekends. Avoid if claustrophobic. Some animal cafes have questionable ethical standards.",
                "Gachapon machines 300-500 yen each; giant cotton candy 900+ yen; Purikura photo booths 400-500 yen.",
                new[] { "Meiji Jingu", "Cat Street", "Daiso Harajuku" },
                "Weekday mornings (11 AM) for manageable crowds; late afternoon for best photos.",
                "Very High (10/10) on weekends.",
                "7/10 — flat but intense crowds make wheelchair navigation stressful.",
                "1 to 2 hours"),
                5, 25, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["neighborhood_district"] },
                new List<string> { "cultural", "walkable", "shopping", "budget_friendly", "tourist_hotspot", "crowded" },
                "Japan Standard Time"),

            Make("Cat Street",          "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/cat_street.jpg",
                BuildDesc("A winding pedestrian lane built over the old Shibuya River bed, connecting Harajuku to Shibuya. Lined with high-end boutiques, vintage shops, and artisan cafes — the sophisticated counterpart to Takeshita's chaos.",
                "Cat Street is where fashion-forward locals come to refine their taste. No hard sell, no neon — just understated cool.",
                "From Harajuku: exit Takeshita Exit, cross Route 305, walk straight — Cat Street is the fifth street on the right.",
                "A basic crepe is affordable but customization pushes to 800-1,000 yen. Lobster rolls near 1,958 yen.",
                "Many boutiques don't open until 11 AM. Higher-end pricing throughout.",
                "Gourmet premium for food; designer vintage can still cost hundreds; The Trunk Hotel bar ~1 cocktail = small meal elsewhere.",
                new[] { "Meiji Jingu Shrine", "Yoyogi Park", "Miyashita Park", "Omotesando" },
                "Spring/Autumn for outdoor walk; weekday afternoons for relaxed browsing; golden hour for photos.",
                "Moderate (5/10).",
                "9/10 — mostly pedestrian promenade, easy to navigate.",
                "1 to 3 hours"),
                10, 100, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["neighborhood_district"] },
                new List<string> { "walkable", "shopping", "premium", "tourist_hotspot" },
                "Japan Standard Time"),

            Make("Shibuya Crossing",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/shibuya_crossing.jpg",
                BuildDesc("The world's busiest pedestrian intersection, where five roads meet and up to 3,000 people cross simultaneously in the famous 'scramble.' An iconic symbol of Tokyo's organized chaos.",
                "For Tokyoites this is just their daily commute — stay aware of the flow and don't stop in the center for selfies.",
                "Shibuya Station Hachiko Exit — the crossing is immediately in front of you.",
                "Crossing is free. Shibuya Sky deck 2,500-3,500 yen; Magnet rooftop view ~1,800 yen including one drink.",
                "Don't stop in the middle for photos. Avoid pickpockets during extreme rush hour density.",
                "Shibuya Sky observation deck; Starbucks QFRONT window view (~600 yen drink); 15-minute wait for Hachiko statue photo.",
                new[] { "Shibuya 109", "Miyashita Park", "Dogenzaka", "Shibuya Hikarie" },
                "Friday night 7PM for maximum energy; rainy evenings for umbrella-ocean photography; Sunday 8AM for eerily empty crossing.",
                "Extreme (10/10).",
                "9/10 — smooth level pavement with curb cuts; overwhelming for sensory sensitivities during rush hour.",
                "30 to 60 minutes"),
                0, 5, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["landmark_monument"] },
                new List<string> { "walkable", "crowded", "tourist_hotspot", "photography" },
                "Japan Standard Time"),

            Make("Shibuya Sky",         "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/shibuya_sky.webp",
                BuildDesc("An open-air 229-meter rooftop observation deck atop Shibuya Scramble Square. Offers 360-degree panoramic views of Tokyo including Skytree, Tokyo Tower, Imperial Palace, and on clear days, Mt. Fuji.",
                "Locals book exactly 30 minutes before sunset — daylight to blue hour to neon lights flickering on. Visit in winter for Mt. Fuji sightings.",
                "Shibuya Station East Exit → Shibuya Scramble Square → ticket counter on 14th floor.",
                "~2,700 yen online (cheaper) or 3,000 yen at counter. After 3PM: 3,400 yen. Mandatory 100-yen locker for bags.",
                "No bags, tripods, or loose items on the roof. Closes in high winds. Book sunset slots weeks ahead.",
                "100-yen locker (refundable); professional photo print ~1,500 yen; rooftop bar cocktails 800-1,200 yen.",
                new[] { "Paradise Lounge (46F bar)", "Shibuya Scramble Square shops", "Hachiko Statue" },
                "Sunset slot (book online); 10AM opening for short queues; 9PM for romantic quiet atmosphere.",
                "High (9/10).",
                "10/10 — fully wheelchair and stroller accessible.",
                "90 minutes"),
                14, 18, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["viewpoint_scenic_spot"] },
                new List<string> { "architecture", "photography", "tourist_hotspot" },
                "Japan Standard Time"),

            Make("Senso-ji Temple (Asakusa Kannon)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/sensoji.jpg",
                BuildDesc("Tokyo's oldest temple, founded in 645 AD. A vibrant spiritual landmark anchored by the famous Kaminarimon thunder gate and 250-meter Nakamise shopping street, offering an immersive Edo-period atmosphere.",
                "The original Kannon statue is never shown — not even to monks. For locals this is a living place of prayer, not just a landmark.",
                "Asakusa Station Exit 1 or 3, 2-minute walk to Kaminarimon Gate.",
                "Entry free. Omamori amulets 500-1,500 yen; omikuji fortunes 100-200 yen; Goshuin temple stamps 300-500 yen.",
                "11AM-3PM crowds are overwhelming. Bad luck fortunes are common here — it's just tradition.",
                "Rickshas 4,000-9,000 yen for 30 min; candle offerings a few hundred yen; Nakamise snacks 150-500 yen.",
                new[] { "Asakusa Culture Tourist Info Center (free 8F view)", "Hoppy Street", "Samurai Ninja Museum Tokyo" },
                "8AM for quiet and beautiful shutter art on Nakamise; 7PM for illuminated temple with few crowds.",
                "Extreme (10/10) weekends; Moderate (4/10) weekday early morning.",
                "8/10 — mostly flat with elevator access to altar area.",
                "1.5 to 2 hours"),
                0, 15, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["cultural_site"] },
                new List<string> { "walkable", "crowded", "tourist_hotspot", "cultural", "shopping", "photography" },
                "Japan Standard Time"),

            Make("Kaminarimon Gate",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/kaminarimon.jpg",
                BuildDesc("The iconic 700kg vermilion lantern gate guarding Senso-ji Temple. One of Tokyo's most photographed landmarks, featuring Shinto gods guarding the front and Buddhist deities at the rear.",
                "During Sanja Matsuri festival in May, the lantern is collapsed so that massive portable shrines (mikoshi) can pass underneath.",
                "Asakusa Station, 2-minute walk from any exit. Gate is directly at the entrance to Senso-ji temple complex.",
                "Passing through is free. First omikuji stalls just past the gate: 100 yen.",
                "Extremely congested during peak daylight. Rickshaw pullers base here — polite refusal works fine.",
                "Fortune telling 100 yen; snack temptation from first Nakamise shops immediately after gate.",
                new[] { "Nakamise-dori", "Senso-ji Temple", "Asakusa Culture Tourist Info Center", "Kamiya Bar" },
                "7AM for no tourists in frame; sunset for deep crimson glow; late night for illuminated quiet.",
                "High (10/10) during the day.",
                "9/10 — flat paved area, easily accessible.",
                "15 minutes"),
                0, 1, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["landmark_monument"] },
                new List<string> { "walkable", "crowded", "tourist_hotspot", "photography", "cultural" },
                "Japan Standard Time"),

            Make("Asakusa Streets",     "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/asakusa.jpg",
                BuildDesc("The heart of Tokyo's Shitamachi (old downtown), where Edo-period atmosphere lingers in the narrow back alleys, incense smoke from Senso-ji, and traditional craft shops. A maze of discovery beyond the main temple.",
                "Before shops open, the Nakamise shutters are painted with scenes of Japanese history — locals call this the Shutter Art hour. Best at 7:30AM.",
                "Asakusa Station (Ginza Line, Asakusa Line, Tobu Railway). Most attractions 3-7 min walk from exits.",
                "Street snacks 150-500 yen. Don Quijote has multi-story options. Cash for most stalls.",
                "Most Nakamise shops close by 5-6PM. Carry trash until hotel — public bins nearly nonexistent.",
                "Rickshaw tours ~9,000 yen for 2 people/30 min; Hanayashiki amusement park 1,200 yen entry; Asahi Sky Room beer 800-1,200 yen.",
                new[] { "Kappabashi Street (kitchen gear)", "Sumida Park", "Tokyo Skytree" },
                "7:30AM for Shutter Art; late July for Sumida River Fireworks; sunset when Skytree reflects off river.",
                "High (9/10) especially midday weekends.",
                "9/10 — very flat with elevator access throughout.",
                "4 to 6 hours"),
                10, 30, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["market_street_life"] },
                new List<string> { "walkable", "crowded", "tourist_hotspot", "shopping", "food_focused" },
                "Japan Standard Time"),

            Make("Sumida Riverwalk",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/riverwalk.jpg",
                BuildDesc("A scenic riverside promenade along the Sumida River, linking ancient Asakusa temples to the futuristic Tokyo Skytree. Features a glass-floored pedestrian bridge, waterfront cafes, and 16 uniquely colored bridges.",
                "Locals know the Asahi Flame atop Asahi Super Dry Hall as the 'Golden Turd.' The riverside quiet is where Tokyoites decompress from city noise.",
                "Asakusa Station Exit 5 (7-min walk) or TOBU SKYTREE Line North Exit (3-min walk). Walk toward Azuma Bridge for riverside access.",
                "Walk is free. Water bus to Odaiba ~2,000 yen; riverside cafes premium; yakatabune dinner cruise 10,000-15,000 yen per person.",
                "Very little shade — avoid midday heat. Bridge distances are 5-10 min apart; plan energy for return.",
                "Riverside dining premium; Sumo tournament tickets sell out months in advance; mizumachi bouldering/hosting fees separate.",
                new[] { "Tokyo Skytree Town", "Senso-ji Temple", "Ryogoku Kokugikan (sumo)", "Sumida Park" },
                "Sunset for synchronized Skytree lighting; late March/early April for cherry blossoms; last Saturday of July for fireworks.",
                "Low to Moderate (3/10) weekday mornings; Extreme (10/10) during cherry blossom season.",
                "9/10 — paved flat paths and wheelchair-friendly bridge.",
                "45 minutes to 2 hours"),
                0, 10, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["activity_experience"] },
                new List<string> { "walkable", "romantic", "photography", "couple_friendly" },
                "Japan Standard Time"),

            Make("Shinjuku Gyoen",      "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/gyoen.jpg",
                BuildDesc("A 144-acre national garden featuring three distinct styles: French Formal, English Landscape, and Japanese Traditional. Famous for 1,000+ cherry trees and the longest sakura season in Tokyo due to early and late-blooming varieties.",
                "No alcohol allowed — making it the locals' preferred hanami spot for families who want peaceful flower viewing without rowdy parties.",
                "Shinjuku Gate: 10-min walk from JR Shinjuku Station New South Exit, or 5-min from Shinjukugyoenmae Station.",
                "500 yen adults; free for under-15. Annual passport 2,000 yen. Teahouse matcha 700-1,000 yen.",
                "Alcohol strictly prohibited. Last entry 30 min before closing. Weekend sakura requires advance online booking.",
                "Annual passport pays off in 4 visits; teahouse set 700-1,000 yen; garden shop slightly above convenience store prices.",
                new[] { "Shinjuku San-chome", "Tokyo Metropolitan Government Building", "Meiji Jingu" },
                "9AM opening for quiet Japanese Garden; early November for autumn maple colors; late March for cherry blossoms.",
                "Moderate (5/10) weekdays; High (9/10) blossom season.",
                "10/10 — extremely flat and paved, accessible restrooms throughout.",
                "2 to 3 hours"),
                0, 5, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["nature_outdoor"] },
                new List<string> { "walkable", "photography", "nature", "history" },
                "Japan Standard Time"),

            Make("Tokyo Metropolitan Government Building", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tokyo_metropolitan.jpg",
                BuildDesc("A free 202-meter observation deck on the 45th floor of Kenzo Tange's postmodern masterpiece. Offers 360-degree city views including Mt. Fuji on clear days, and features evening projection mapping on the building exterior.",
                "The staff canteen on the 32nd floor serves cheap high-quality food to office workers — locals eat here for 600-800 yen set meals.",
                "Tocho-mae Station (Oedo Line) in the building basement, or 10-min walk from JR Shinjuku Station West Exit.",
                "Free admission. Observatory cafe coffee/beer 600-900 yen. Staff canteen lunch 600-800 yen.",
                "Queue can be 30-45 min on weekends. Glass glare at night requires lens against window. Check which tower is open before visiting.",
                "Observatory souvenir shop worth browsing; cafe window seat ~600-900 yen; gift shop has limited-edition Tokyo items.",
                new[] { "Shinjuku Central Park", "Park Hyatt Tokyo", "Omoide Yokocho (Piss Alley)", "Meiji Shrine" },
                "9:30AM winter for Mt. Fuji; 4:30PM for blue hour city lights; 7:30PM for exterior projection mapping show.",
                "Moderate (6/10) weekdays; High (9/10) weekends and clear sunsets.",
                "10/10 — fully ADA compliant with dedicated elevators and wheelchair loans.",
                "1 to 1.5 hours"),
                0, 5, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["viewpoint_scenic_spot"] },
                new List<string> { "photography", "budget_friendly", "architecture" },
                "Japan Standard Time"),

            Make("Shinjuku Golden Gai", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/golden_gai.jpg",
                BuildDesc("Six narrow alleys housing 200+ tiny themed bars, some fitting only five customers. A living relic of 1950s Tokyo that survived developers and arsonists, now beloved by writers, directors, musicians, and curious travelers.",
                "In the 1980s locals took turns physically guarding these alleys from developers. There is immense community pride in its survival.",
                "JR Shinjuku Station East Exit, 5-10 min walk. Located between Shinjuku City Office and Hanazono Shrine.",
                "Cover charges 500-1,000 yen per bar; single cocktails up to 1,500 yen. Most bars don't open until 9-10PM.",
                "Regulars-only bars exist — closed doors mean closed. Steep narrow stairs. Very cramped spaces.",
                "Cover charges 500-1,000 yen per bar; tourist-friendly bars may have higher drink prices; late-night taxi adds 20% surcharge.",
                new[] { "Hanazono Shrine", "Omoide Yokocho", "Kabukicho", "Thermae-Yu (24hr onsen)" },
                "Weeknights Mon-Thu for fewer tourists; 10PM-1AM for peak energy.",
                "Extreme (9/10) Friday and Saturday nights.",
                "2/10 — extremely narrow uneven alleys, steep ladder-like stairs, not wheelchair accessible.",
                "2 to 4 hours"),
                15, 45, 9, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["neighborhood_district"] },
                new List<string> { "nightlife", "best_at_night", "group_friendly", "social" },
                "Japan Standard Time"),

            Make("Kabukicho",           "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/kabukicho.jpg",
                BuildDesc("Japan's largest entertainment district — a hybrid of world-class attractions (Godzilla, namco TOKYO, Sword Art Online Quest) and neon-drenched nightlife bars. Recently transformed by the Tokyu Kabukicho Tower development.",
                "During the day Kabukicho is actually family-friendly and great for photography. The vibe shifts dramatically after 9PM.",
                "JR Shinjuku Station East Exit → find Studio Alta screen → cross Yasukuni-dori → look for Don Quijote → Kabukicho Ichibangai red neon arch.",
                "Godzilla view free; namco TOKYO arcade tokens from 200 yen; dinner 1,500-3,000 yen; clubs 2,000-5,000 yen entry.",
                "Never enter bars recommended by strangers — bottakuri (rip-off) scams exist. Stick to well-reviewed spots.",
                "Table charges (otoshi) 500-1,000 yen per bar; midnight taxi 20% surcharge; premium experience costs stack quickly.",
                new[] { "Golden Gai", "Omoide Yokocho (Piss Alley)", "Thermae-Yu (24hr onsen)" },
                "8PM-11PM for neon at peak brightness without extreme crowds.",
                "High (9/10) Friday and Saturday nights.",
                "7/10 — main streets flat and paved; many bars have steep narrow stairs.",
                "3 to 5 hours"),
                5, 100, 7, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["entertainment_nightlife"] },
                new List<string> { "nightlife", "best_at_night", "social" },
                "Japan Standard Time"),

            Make("Tsukiji Outer Market","https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tsukiji.jpg",
                BuildDesc("Tokyo's surviving seafood and food culture hub — 400+ shops selling fresh fish, professional knives, and street food. Even after the wholesale market moved to Toyosu in 2018, this remains the city's most vibrant morning food district.",
                "Most shops close by 1-2PM. By 3PM the market is almost entirely shut. This is an early-morning destination only.",
                "Tsukiji Shijo Station (Oedo Line) or Tsukiji Station (Hibiya Line), short walk from either.",
                "Sushi breakfast 1,000-3,000 yen; ceviche-style cups 200-600 yen; premium tuna cuts priced professionally.",
                "Walking and eating (tabearuki) is frowned upon — finish snacks at the stall. 2-hour sushi queues are real.",
                "Tax-free shopping for tourists with passport; top-floor food court at Tsukiji Uogashi has shorter waits for same quality.",
                new[] { "Ginza", "Hamarikyu Gardens", "Kabuki-za Theatre" },
                "8AM-10AM for all shops open without peak lunch crowds.",
                "High (10/10) — extremely tight aisles.",
                "6/10 — main areas accessible but narrow alleys difficult for wheelchairs.",
                "2 to 3 hours"),
                5, 60, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["market_street_life"] },
                new List<string> { "crowded", "tourist_hotspot", "food_focused", "walkable" },
                "Japan Standard Time"),

            Make("teamLab Borderless: Azabudai Hills", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/teamlab.jpg",
                BuildDesc("A 10,000 sqm digital art museum without a map — 75+ interconnected installations where art crosses between rooms, dragons fly down hallways, and flowers bloom at your feet. Reopened at Azabudai Hills in 2024.",
                "Locals wear white or light-colored clothing so the projected art becomes part of their outfit. Visit in April for digital cherry blossom rooms.",
                "Kamiyacho Station (Hibiya Line) Exit 5, 2-minute walk. Within Azabudai Hills complex.",
                "Weekday ~3,200 yen; weekend/holiday higher and sells out weeks ahead. EN Tea House extra 600-1,100 yen.",
                "Light Vortex room has intense flashing — avoid if photosensitive. No bags on floor; mandatory lockers.",
                "EN TEA HOUSE not included; lockers 100 yen (refundable); battery pack essential as photos drain phone fast.",
                new[] { "Tokyo Tower (10-min walk)", "Mori Art Museum (Roppongi Hills)", "Janu Tokyo (afternoon tea)" },
                "9AM opening or after 6PM to avoid school groups; wear light colors.",
                "High (8/10).",
                "8/10 — mostly accessible with barrier-free routes; some terrain areas restricted.",
                "3 to 4 hours"),
                10, 30, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["viewpoint_scenic_spot"] },
                new List<string> { "architecture", "photography", "tourist_hotspot" },
                "Japan Standard Time"),

            Make("Tokyo Disney",        "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/disney_tokyo.jpg",
                BuildDesc("The world's most visited Disney resort — Tokyo Disneyland and Tokyo DisneySea. In 2024-2026 celebrating DisneySea's 25th anniversary 'Sparkling Jubilee' with the massive Fantasy Springs expansion (Frozen, Tangled, Peter Pan lands).",
                "The Yen at 160/$1 makes this one of the best-value Disney experiences globally in 2026. Rope drop means gates open at 8:15AM even if the official time is 9AM.",
                "JR Maihama Station (JR Keiyo Line from Tokyo Station, ~15 min). Disney Resort Line monorail connects to parks.",
                "Tickets 7,900-10,900 yen depending on tier. Disney Premier Access line-skipping ~$9-15 per ride.",
                "Golden Week (April 29-May 5) means max crowds and peak pricing. Tomorrowland under construction for new Space Mountain.",
                "DPA passes 1,350-2,250 yen per ride; dining premium; late-night return trains are packed.",
                new[] { "Kasai Rinkai Park (aquarium)", "Resort hotels buffet breakfast", "Ikspiari shopping" },
                "Tuesdays-Thursdays outside Golden Week; mid-May onwards for 2026.",
                "10/10 Golden Week; 7/10 standard weekday.",
                "9/10 — excellent flat surfaces, fully accessible monorail.",
                "3 to 4 days full resort"),
                49, 68, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["activity_experience"] },
                new List<string> { "family_friendly", "premium", "tourist_hotspot", "crowded" },
                "Japan Standard Time"),

            Make("Akihabara",           "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/akihabara.jpg",
                BuildDesc("Tokyo's Electric Town — global center of otaku culture, anime merchandise, retro gaming, maid cafes, and electronics. Evolved from post-WWII radio parts market to multi-floor shrines dedicated to every anime fandom.",
                "On Sundays 1-5PM Chuo Dori closes to cars — best time for photography and a true Akihabara experience. Side alley stalls have better prices on used figures than main road shops.",
                "Akihabara Station (JR Yamanote from Tokyo Station — 3 minutes, 160 yen).",
                "Tax-free shopping with passport at most major stores. Retro games and figurines significantly cheaper with Yen at 160/$1.",
                "Maid cafe cover charges are mandatory (600-1,000 yen). Street maid cafe promoters sometimes hide fees — stick to known chains.",
                "Maid cafe cover + drink minimum; UFO catcher crane games easy to lose $20-30; AKB48 Theatre tickets 2,400-3,400 yen.",
                new[] { "Kanda Myojin Shrine", "mAAch ecute Kanda Manseibashi", "Ochanomizu (music shops)" },
                "Sundays 1-5PM for car-free streets; weeknight evenings for best neon without weekend crowds.",
                "High (9/10) weekends; Moderate (6/10) weekday mornings.",
                "8/10 — modern buildings with elevators but Radio Centre stalls very narrow.",
                "4 to 6 hours"),
                5, 100, 10, japan, tokyoCities, tokyoLangs, tokyoCurrs,
                new List<Category> { cats["neighborhood_district"] },
                new List<string> { "cultural", "shopping", "tourist_hotspot" },
                "Japan Standard Time"),

            // ── Panama destinations ───────────────────────────────────────────

            Make("Casco Viejo",         "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/casco.jpg",
                BuildDesc("Panama's historic walled quarter — a 40-acre peninsula where 17th-century fortifications, neoclassical buildings, and colonial ruins coexist. A UNESCO World Heritage site and the city's most visited neighborhood.",
                "Casco Cat Community — locals quietly maintain the neighborhood's street cats, considered its unofficial guardians. Look for genuine Kuna Molas beyond the souvenir shops.",
                "Compact 40-acre district minutes from central Panama City hotels. Best explored on foot via self-guided walking tour.",
                "Plan 3+ hours for main sights; full day including lunch and museums. Walking is essential for the full experience.",
                "Night safety: stick to main streets near plazas after dark. Higher pricing than rest of Panama City. Cobblestones can be challenging.",
                "Daily $63 (budget) to $189+ (mid-range); meals $7-14; Geisha coffee and Kuna Molas are premium purchases.",
                new[] { "Mercado de Mariscos", "Cinta Costera", "Ancon Hill" },
                "Late afternoon ~4PM to explore before heat breaks and rooftop nightlife begins; year-round.",
                "High (8/10) — most visited neighborhood in Panama.",
                "8/10 — 40 acres walkable but cobblestones challenging for wheelchairs/strollers.",
                "3+ hours; full day preferred"),
                65, 190, 7, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["neighborhood_district"] },
                new List<string> { "walkable", "history", "cultural" },
                "Eastern Standard Time"),

            Make("Panama Canal Miraflores Locks", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/panamacanal.jpg",
                BuildDesc("Panama's most visited landmark — a 4-floor visitor center with IMAX theater, museum, and outdoor viewing decks overlooking the original and expanded canal locks. Watch 100,000-ton vessels lifted by gravity and water alone.",
                "The name 'Miraflores' means 'Behold the flowers.' Ship transits follow windows: 8-9AM (Atlantic direction) and starting 2PM (return). Arriving at noon means watching an empty concrete bathtub.",
                "20-30 min drive from Panama City. Use Waze over Google Maps. Large parking lot available.",
                "Admission: international adults $17.22; residents $3.00. 45-min IMAX film included. Arrive at 8AM for morning transit window and to beat heat.",
                "Heat on outdoor platforms. No guarantee of ship passage during your slot. Crowds of 450 fill the deck fast when Neo-Panamax ships transit.",
                "International vs local pricing gap significant; parking fills during peak tour coach times.",
                new[] { "Metropolitan Natural Park", "Biomuseo", "Agua Clara Locks (Atlantic side)" },
                "8AM for morning transit window and cooler temperatures.",
                "High (9/10) — Panama's most visited landmark.",
                "9/10 — ramps, elevators to all four floors, dedicated wheelchair platform.",
                "2 to 3 hours"),
                150, 170, 7, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["landmark_monument"] },
                new List<string> { "tourist_hotspot", "educational", "family_friendly" },
                "Eastern Standard Time"),

            Make("Cinta Costera",       "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/cintacostera.jpg",
                BuildDesc("A 7-kilometer coastal beltway connecting Paitilla skyscrapers to Casco Viejo's colonial walls. Built on reclaimed land, it's Panama City's communal promenade for cycling, jogging, street food, and spectacular bay views.",
                "Stand at Mirador del Pacífico at km 2.6 — you can see both the 17th-century ruins and the 21st-century skyline simultaneously. This visual contrast is unique to Panama City.",
                "Route 1 begins in Paitilla neighborhood, extends 7km along Panama Bay. Free public transport access from multiple points.",
                "Free entry. Food and extras limited to what you buy. Standard taxi/transit fares to reach either end.",
                "Midday sun exposure extreme — very little shade. El Chorrillo neighborhood at the end contrasts sharply with luxury Paitilla.",
                "No entry fee; raspa'o (shaved ice) from vendors; seafood at Sabores de El Chorrillo.",
                new[] { "Anayansi Square (raspa'o)", "Mirador del Pacífico", "Mercado del Marisco", "Sabores de El Chorrillo" },
                "5PM daily for golden hour — heat breaks, skyscrapers glow; January-March for consistent breeze.",
                "High 9/10 weekends.",
                "10/10 — 26-hectare public space designed for pedestrians and cyclists.",
                "1.5 to 3 hours"),
                10, 15, 7, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["neighborhood_district"] },
                new List<string> { "seasonal", "family_friendly", "walkable" },
                "Eastern Standard Time"),

            Make("Mercado de Mariscos", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/mercado.jpeg",
                BuildDesc("Panama City's seafood market — the sensory epicenter where Pacific salt air meets the culinary pulse of the capital. Built in 1995 with Japanese government assistance, it's a working market serving both professional chefs and food-loving travelers.",
                "Don't look for a fancy menu. Order corvina (sea bass) or pulpo (octopus) ceviche in a disposable cup with saltine crackers. At $1-2, arguably the best value meal in Central America.",
                "Avenida Balboa at the pivot between Cinta Costera and Casco Viejo. Take the waterfront boardwalk under the highway from Casco — safer and more pleasant than crossing the main road.",
                "Market proper opens 5AM. Restaurants serve through lunch. Ceviche cups $1-2; fried fish platters $7-15+.",
                "It's a fish market — the smell is powerful. Bathrooms are very basic. Expect noise, music, and friendly vendor competition.",
                "Platter upgrades; side items like fries/patacones separate; informal parking tips; possible bathroom fee $0.25.",
                new[] { "Cinta Costera", "Casco Viejo", "Biomuseo" },
                "5-9AM for fresh catch unloading; weekdays at lunch for best vibe without weekend crowds.",
                "Moderate to High (8/10).",
                "8/10 — ground level open layout but wet floors; safe waterfront boardwalk route from Casco.",
                "45 minutes to 1.5 hours"),
                5, 15, 7, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["market_street_life"] },
                new List<string> { "food_focused" },
                "Eastern Standard Time"),

            Make("Biomuseo",            "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/biomuseo.jpg",
                BuildDesc("Frank Gehry's only Latin American building — a chaotic, colorful museum on the Amador Causeway telling the story of how the Isthmus of Panama rose from the sea and changed the world's climate and evolution. Features the immersive 'Panamarama' three-level projection space.",
                "The Biodiversity Park behind the museum offers the best unobstructed photo of the neon Gehry building with the Bridge of the Americas and massive container ships in the background.",
                "10-15 min ride from downtown. Bus Route C850 from Albrook Metro Station stops directly at museum. Open Tue-Fri 9AM-3PM; Sat-Sun 10AM-3PM.",
                "International adults from $20; family 4-person package $60. Copa Airlines promo 'BIOMUSEOCOPA' can drop price to ~$12.",
                "Museum closes at 3PM — arrive by 1PM at the latest for all 8 galleries. Very windy on the causeway.",
                "International vs resident pricing; parking limited on weekends; no half-day trips unless arriving early.",
                new[] { "Punta Culebra Nature Center (Smithsonian)", "Amador Causeway cycling", "Taboga Island ferry" },
                "Wednesday or Thursday 10AM — beat school groups and perfect lighting for exterior photos.",
                "Moderate (6/10); quieter on weekday mornings.",
                "10/10 — modern facility fully accessible with elevators, wide corridors, paved park paths.",
                "1.5 to 2.5 hours"),
                10, 20, 9, panama, new List<City> { panamaCity }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["landmark_monument"] },
                new List<string> { "architecture", "nature", "educational" },
                "Eastern Standard Time"),

            Make("Bocas Town (Isla Colón)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/bocastown.webp",
                BuildDesc("The vibrant Caribbean hub of Bocas del Toro archipelago — a kaleidoscopic grid of colorful buildings, international restaurants, and water taxi docks. Base camp for island hopping, surfing, and the legendary Filthy Friday boat party.",
                "Individual unit buying is normal here — you can buy 2-3 pills from a pharmacy or a single slice of cheese. Grocery runs often combine with early happy hours to escape the heat.",
                "Bocas del Toro airport (daily flights from Panama City) or water taxi from mainland. Town is a compact grid — most points of interest within 10-min walk.",
                "Budget $5-50/day depending on activities. Street food from $2; restaurants $8-15; activities add up.",
                "Water taxis are fast and wet — sit toward the back. Sun is extreme, stores not air-conditioned. Service can be slow.",
                "Water taxi fees per person per way; imported goods premium at Super Gourmet; hardware store patience tax.",
                new[] { "Red Frog Beach", "The Pub & Toro Loco", "Bastimentos Town (Old Bank)" },
                "October, January, March are driest months; mornings for errands before heat; late evening 8PM+ for street food.",
                "High (8/10).",
                "9/10 — only island with paved roads; walkable grid but sidewalks inconsistent.",
                "3 to 5 days"),
                5, 50, 7, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["neighborhood_district"] },
                new List<string> { "nightlife", "best_evening", "budget_friendly", "walkable" },
                "Eastern Standard Time"),

            Make("Red Frog Beach",      "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/redfrog.jpeg",
                BuildDesc("Named for the Strawberry Poison Dart Frogs in its jungle, Red Frog Beach offers the balance of wild Caribbean power and resort-style comfort. Two access routes: rustic $5 jungle shortcut or $45 resort day pass with pool and golf carts.",
                "Turn LEFT away from the sea at the end of the houses to find a secret deserted beach that is even better for frog spotting. The red frogs are typically found along the jungle path and near wetland areas.",
                "15-min water taxi from Bocas Town to Red Frog Marina or shortcut entrance. $5 per person jungle path fee. OR specify resort drop-off for $45 day pass.",
                "Shortcut entry: $5 per person; Resort day pass: $45 (credited to food/drinks); chair rentals separate; island-priced snacks and drinks.",
                "Strong rip tides — swim only in designated areas, never deeper than hip. Extremely hot sand — wear water shoes. Boat rides can be very wet.",
                "Water taxi fees both ways; equipment rentals; lobster surcharge; if you miss marina panga private launch is premium.",
                new[] { "Nature trails and bat caves", "Mangrove transit (part of the journey)" },
                "Morning for wildlife spotting; driest months Oct, Jan, March for less muddy trails.",
                "Moderate (5/10) — entry fee and boat ride keep it quieter.",
                "7/10 — 10-min groomed boardwalk; resort offers golf cart transport.",
                "2 hours to full day"),
                5, 190, 7, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["nature_outdoor"] },
                new List<string> { "good_short_visit", "premium", "budget_friendly", "nature" },
                "Eastern Standard Time"),

            Make("Starfish Beach (Bocas del Toro)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/starfishbeach.jpeg",
                BuildDesc("Playa Estrella — a calm, still-water Caribbean beach on Isla Colón's north end, home to giant orange cushion starfish in crystal-clear shallow water. Reached via $2.50 colectivo bus and 20-min coastal trail from Boca del Drago.",
                "The sand flies (sandflies) are nearly invisible but leave itchy red welts. Stay in the water — they can't reach you there. Sit neck-deep with a beer for maximum protection.",
                "Colectivo minibus 'Boca del Drago' from Parque Simón Bolívar, Bocas Town (~$2.50-3.00 each way, 45 min). Then 1.5km flat coastal walk to beach.",
                "Colectivo $2.50-3 each way; shaded beach chair ~$5; cash only for all vendors.",
                "Never touch or lift starfish from water — they suffocate within seconds of air exposure. Coconut hazard under palm trees. Sandflies most active at dusk.",
                "Private water taxi much more expensive than colectivo; chair rental; cash-only vendors.",
                new[] { "Boca del Drago (quieter entry beach)", "Bird Island (Isla Pájaros)", "Bocas Town for dinner" },
                "Before 10AM to beat crowds and find more starfish in shallows; weekdays preferred.",
                "High (8/10) on weekends.",
                "7/10 — flat well-defined trail; can get muddy after rain; direct water taxi option avoids walk.",
                "4 to 6 hours"),
                5, 10, 8, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["nature_outdoor"] },
                new List<string> { "nature", "group_friendly", "solo_friendly" },
                "Eastern Standard Time"),

            Make("Wizard Beach",        "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/wizardbeach.jpeg",
                BuildDesc("Playa Primera — Isla Bastimentos's raw, untamed soul. A vast golden expanse backed by dense jungle with legendary surf (3-10ft waves) and almost zero visitors. High reward, high risk: the trail is steep and muddy, currents can be lethal, and theft from the treeline is a real concern.",
                "Locals know thieves watch from the treeline and wait for everyone in a group to swim before striking. Total theft is common — including clothes and shoes. Never leave bags unattended.",
                "Water taxi to Old Bank on Isla Bastimentos, then 30-45 min jungle hike. Follow signs behind local houses.",
                "Only bring cash needed for water taxi in a waterproof pouch on your body. Leave all valuables at hotel.",
                "Lethal rip tides directly in front of trail entrance. Targeted theft from jungle treeline. Steep muddy trail destroys flip-flops. No amenities whatsoever.",
                "Water taxi; possible shoe replacement after mud; no safety net if anything goes wrong.",
                new[] { "Old Bank (Afro-Caribbean village)", "Up in the Hill (cacao farm)", "Red Frog Beach" },
                "December-March and June-August for consistent surf; April-June and September-October for safer swimming.",
                "Very Low (2/10).",
                "3/10 — 30-45 min steep muddy jungle hike required; not suitable for limited mobility.",
                "2 to 3 hours"),
                10, 15, 4, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["nature_outdoor"] },
                new List<string> { "adventurous", "best_morning", "family_friendly" },
                "Eastern Standard Time"),

            Make("Cayos Zapatilla",     "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/cayos.jpeg",
                BuildDesc("Pristine uninhabited UNESCO World Heritage islands within Bastimentos National Marine Park. White powder sand, the healthiest coral gardens in the region, nesting Hawksbill sea turtles, and nurse sharks in underwater caves. Accessed via organized tours with stepping-stone stops.",
                "Standard tours stop for lunch at noon but you won't eat until 3:30PM. The food is expensive. Pack your own lunch and skip the tourist trap.",
                "Organized tours depart Bocas Town 9-10AM. 1.5-hour boat ride passing Sloth Island and Cayo Coral snorkeling stop. Wet landing — no docks.",
                "ANAM park entrance fee (cash at ranger station). Tour typically includes boat and some snorkel gear. Lobster upgrade extra.",
                "Wet landing — carry electronics above your head. Chitras (sand flies) active at dusk. No food or water on the island.",
                "Park entrance fee; snorkel gear rental if not included; ANAM ranger station cash only; tour lunch overpriced.",
                new[] { "Sloth Island", "Cayo Coral (snorkeling)", "Hollywood/Starfish Island" },
                "September-October for calmest seas and best coral visibility; arrange private boat for 9AM arrival before tour crowds.",
                "Moderate (6/10) — 11AM-3PM peak tour windows.",
                "4/10 — wet landing requires mobility; interpretive trail in good condition once on island.",
                "2-3 hours on island; full day with transit"),
                30, 100, 8, panama, new List<City> { bocas }, new List<Language> { spanish, english }, new List<Currency> { usd, pab },
                new List<Category> { cats["nature_outdoor"] },
                new List<string> { "tourist_hotspot", "nature" },
                "Eastern Standard Time"),

            Make("San Blas Islands (Via Carti)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/carti.jpg",
                BuildDesc("The gateway to Guna Yala — the autonomous Guna people's territory. The Cartí port is reached via the El Llano-Cartí Road through primary rainforest, a 30km 4x4-only mountain route that acts as a natural filter keeping mass tourism out of the archipelago.",
                "For the Guna, the checkpoint isn't just a toll — it's a symbol of the 1925 Revolution. The difficult road is intentional: it protects their sovereignty by limiting mass encroachment.",
                "Panama City → Panamericana → El Llano-Cartí Road (Texaco station turnoff). 4x4 mandatory. Stop at checkpoint for passports and $20 international fee. Use Waze not Google Maps.",
                "Guna Yala entrance: $20/person. Port access: $2/person. Vehicle fee: $3. Parking: ~$3/day. Bring quarters and small bills.",
                "4x4 is mandatory — checkpoints enforce this. Motion sickness common on mountain rollercoaster road. Take Dramamine before Texaco stop.",
                "Entrance fees rarely included in tour prices; boat shuttles $30-50/person separate; island landing fees $3/person per island visited.",
                new[] { "El Llano-Cartí Road (scenic drive)", "Texaco Station (last supplies)", "The Checkpoint" },
                "Early morning 8:30AM for first boat departures; dry season December-April for stable road.",
                "High (8/10) at port 8:30-10:30AM.",
                "2/10 — extremely difficult access road; not wheelchair accessible.",
                "30 to 60 minutes transit point"),
                30, 50, 7, panama, new List<City> { gunaYala }, new List<Language> { spanish, english, guna }, new List<Currency> { usd, pab },
                new List<Category> { cats["neighborhood_district"] },
                new List<string> { "crowded", "tourist_hotspot" },
                "Eastern Standard Time"),

            Make("Isla Perro",          "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/isla-perro.jpg",
                BuildDesc("The most iconic snorkeling spot in San Blas — a small Guna-managed island where a sunken Army freighter rests just yards from the white sand beach. The wreck is now a thriving artificial reef teeming with tropical fish, carefully preserved by restricting large-scale development.",
                "To the Guna families who protect it, Isla Perro is the gateway to underwater history. They offer a simple traditional lunch of fish and rice before the island returns to silence once day-trippers depart.",
                "4x4 transport from Panama City to Cartí port (2.5-3 hrs), then 20-30 min lancha through the Lemon Cays.",
                "Guna Yala entrance $20. Port docking fees ~$2. Equipment rental if needed. Lobster surcharge over standard fish lunch. Transport from Panama City separate.",
                "No ATMs or card machines anywhere. Bring filtered water and snacks. Sunscreen essential — very little shade.",
                "4x4 vehicle transport to coast; island fees; snorkel gear rental; lobster surcharge.",
                new[] { "The Pool (sandbar)", "Isla Diablo (neighbor)", "Island hopping (2-4 islands/day)" },
                "January-March for peak water clarity; weekdays for quieter experience; morning for snorkeling before afternoon winds.",
                "Medium (6/10).",
                "4/10 — long 4x4 mountain drive + boat transfer; not wheelchair accessible.",
                "4-6 hours day trip; 1 night for true quiet after day-trippers leave"),
                80, 250, 8, panama, new List<City> { gunaYala }, new List<Language> { spanish, guna }, new List<Currency> { usd, pab },
                new List<Category> { cats["nature_outdoor"] },
                new List<string> { "photography", "adventurous", "group_friendly", "nature", "good_short_visit" },
                "Eastern Standard Time"),

            Make("Ibin's Beach Restaurant (Isla Banedup)", "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/ibins-restaurant.jpg",
                BuildDesc("A remote 'pirate-chic' beach restaurant on a tiny outer island of San Blas — accessible only by catamaran charter. Ibin, the owner, returned from cooking for Panama City celebrities to run this hidden gem serving fresh lobster, octopus curry, and cocktails with his feet in the sand.",
                "Ibin sometimes rows fresh coconut rolls or focaccia directly to anchored boats in the morning. He treats guests like family because on an island this small, everyone is.",
                "Only accessible via catamaran charter of 5+ nights through the outer Dutch Cays. Look for rustic wooden pier on Banedup Island.",
                "Charter costs; Guna territory entrance; food priced on what the ocean provides — lobster, tuna, octopus. Cash only.",
                "Remote — no medical facilities, rough boat rides to reach, strong sun, sand flies at evening, basic bathrooms.",
                "Guna territory fees not included in charter; drinks extra; overnight hut fees; snorkel rentals.",
                new[] { "The Pool (sandbar)", "Dutch Cays snorkeling", "Starfish island" },
                "December-April dry season for comfortable sailing; January-March for best water clarity.",
                "Very Low (3/10) — far into the archipelago.",
                "4/10 — remote access via catamaran only; wet landings.",
                "2 to 4 hours or half day"),
                30, 50, 8, panama, new List<City> { gunaYala }, new List<Language> { spanish, guna }, new List<Currency> { usd, pab },
                new List<Category> { cats["food_experience"] },
                new List<string> { "local_favorite", "hidden_gem", "food_focused" },
                "Eastern Standard Time"),

            Make("Isla Diablo",         "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/isla-diablo.jpg",
                BuildDesc("The social hub of San Blas — a Guna-managed island with more energy than its neighbors. Young travelers, hammocks over water, and a snack bar. Just minutes from Isla Perro's famous shipwreck snorkeling.",
                "The Guna people aren't 'staff' — they are the owners and protectors of their ancestral land. The fee culture funds their community sovereignty directly.",
                "30-min lancha from Cartí port through the Lemon Cays. After 2.5-hr 4x4 from Panama City.",
                "Guna territory $20+; port fee ~$2; boat transfer separate; island hopping small fees; drinks and lobster upgrades.",
                "Not for 'peace and quiet' — this is the social island. Basic facilities and shared bathrooms.",
                "Entrance fees; boat transfer; island hopping; lobster/drink upgrades; transport from Panama City.",
                new[] { "Isla Perro (shipwreck)", "Achutupu (Guna village)", "Lemon Cays" },
                "December-April dry season; weekdays for lively not crowded vibe; morning for calmer boat crossing.",
                "High (8/10) for San Blas standards.",
                "4/10 — 2.5-hr 4x4 + 30-min boat; not wheelchair accessible.",
                "Day trip 4-6 hrs; overnight 1-2 nights sweet spot"),
                135, 250, 7, panama, new List<City> { gunaYala }, new List<Language> { spanish, guna }, new List<Currency> { usd, pab },
                new List<Category> { cats["nature_outdoor"] },
                new List<string> { "couple_friendly", "relaxing", "nature", "group_friendly" },
                "Eastern Standard Time"),

            Make("Cayos Holandeses",    "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/dutch-cays.jpg",
                BuildDesc("The edge of the San Blas archipelago — the northernmost outer cays where sand is whiter, water is clearer, and silence is heavier. Accessible only by overnight expedition, the Dutch Cays offer the most vibrant coral in the archipelago and total digital detox.",
                "Locals check the 'Brisa' (North Trade Winds) before the outer cays crossing. If your Guna captain says the water is too 'bravo,' respect it. The crossing involves real open-sea swells.",
                "4x4 to Cartí port, then 1-1.5 hour open-water boat crossing to outer cays like Wegodub or Diadub. No centralized pier — land directly on sand.",
                "Guna territory $20+; port and transfer fees; overnight stay packages; lobster upgrade extra. Base prices often increase by $30-50 after all fees.",
                "Isolation — no medical facilities. Extreme UV. Weather can cancel crossings. Basic huts and shared bathrooms. Bugs at evening.",
                "All fees; real total often $30-50 more than advertised; lobster surcharge; transport from Panama City.",
                new[] { "The Pool (sandbar)", "Highest-quality snorkeling in San Blas", "Kayaking in protected lagoons" },
                "December-April for comfortable sailing; January-March for best coral visibility.",
                "Very Low (3/10) — distance and overnight requirement keeps it exclusive.",
                "4/10 — long mountain drive + bumpy open-sea crossing; not wheelchair accessible.",
                "2-3 nights minimum; 5+ days for full sailing charter immersion"),
                200, 385, 8, panama, new List<City> { gunaYala }, new List<Language> { spanish, guna }, new List<Currency> { usd, pab },
                new List<Category> { cats["nature_outdoor"] },
                new List<string> { "best_morning", "premium", "good_short_visit", "nature" },
                "Eastern Standard Time"),
        };

        db.Destinations.AddRange(destinations);
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 8. WISHLISTS
    // Migrates the 5-day Tokyo wishlist with all itinerary days and items.
    // ═══════════════════════════════════════════════════════════════════════════


    // ═══════════════════════════════════════════════════════════════════════════
    // 8. WISHLISTS
    // Four wishlists:
    //   1. 5-Day Tokyo First-Time Trip
    //   2. San Blas Relax Trip (3 days, Guna Yala)
    //   3. Panama City Highlights (2 days, Pacific side)
    //   4. Bocas del Toro Party & Surf (4 days, Caribbean)
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedWishlistsAsync(HodracDbContext db)
    {
        if (await db.Wishlists.AnyAsync()) return;

        // ── City lookups ──────────────────────────────────────────────────────
        var tokyo = await db.Cities.FirstAsync(c => c.CityName == "Tokyo");
        var panamaCity = await db.Cities.FirstAsync(c => c.CityName == "Panama City");
        var gunaYala = await db.Cities.FirstAsync(c => c.CityName == "Guna Yala");
        var bocas = await db.Cities.FirstAsync(c => c.CityName.StartsWith("Caribbean coast"));

        // ── Destination ID lookups ────────────────────────────────────────────
        var japanId = await db.Countries.Where(c => c.CountryName == "Japan")
                                         .Select(c => c.CountryId).FirstAsync();
        var panamaId = await db.Countries.Where(c => c.CountryName == "Panama")
                                         .Select(c => c.CountryId).FirstAsync();

        var tokyoDestIds = await db.Destinations
            .Where(d => d.CountryId == japanId)
            .Select(d => d.DestinationId).ToListAsync();

        // Panama destinations resolved by name prefix for precise wishlist linking
        var panamaDestByName = await db.Destinations
            .Where(d => d.CountryId == panamaId)
            .Select(d => new { d.DestinationId, d.DestinationName })
            .ToListAsync();

        Guid PId(string prefix) => panamaDestByName
            .First(d => d.DestinationName.StartsWith(prefix)).DestinationId;

        // ── Wishlist 1: Tokyo ─────────────────────────────────────────────────
        var tokyoWlId = Guid.NewGuid();
        var tw1 = Guid.NewGuid(); var tw2 = Guid.NewGuid();
        var tw3 = Guid.NewGuid(); var tw4 = Guid.NewGuid(); var tw5 = Guid.NewGuid();

        var tokyoWishlist = new Wishlist
        {
            WishlistId = tokyoWlId,
            WishlistName = "5-Day Tokyo First-Time Trip (Perfect split: Shibuya, Shinjuku, Asakusa)",
            WishlistDescription = "Tokyo in 5 days: Experience the perfect mix of chaos and calm with neon lights, ancient temples, street food, and skyline views.",
            ShortStory = "A 5-day Tokyo itinerary perfect for first timers — from relaxing shrines and culturally rich streets to high-octane nightlife and immersive art. Each day is planned to include exploration and recovery so you never feel overwhelmed.",
            TotalDays = 5,
            PeopleType = "Solo travelers, couples, and first-time visitors",
            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/tokyo.jpg",
            IsTemplate = true,
            OwnerUserId = null,
            TotalGlobalSaveCount = 0,
            IsFeatured = true,
            DefaultTravelersCount = 2,
            BasePricePerPerson = 1400,
            CalculatedTotalCost = 2800,
            DepositAmountRequired = 280,
            PrimaryPersonaTarget = "First-Time Japan Traveler",
            AccommodationInclusions = "Mid-range hotel in Shinjuku or Shibuya (~$80-120/night)",
            TransitInclusions = "7-day IC card (Suica/Pasmo) for all metro and JR trains (~$50)",
            ActivityInclusions = "Senso-ji, Meiji Shrine, Golden Gai, teamLab Borderless, Tsukiji Outer Market",
            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "7-Day IC Transit Card", "Pocket WiFi rental", "Luggage forwarding between hotels", "City map with QR codes" }),
            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Culture", "Food", "Adventure", "Urban" }),
            RawContentKeywords = "Tokyo Japan first time Shibuya Shinjuku Asakusa Harajuku temple shrine street food nightlife anime",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        tokyoWishlist.ItineraryDays = new List<ItineraryDay>
        {
            new() { ItineraryDayId = tw1, WishlistId = tokyoWlId, DayNumber = 1, DayTitle = "Harajuku & Shibuya — Tokyo Energy Introduction", MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
            new() { ItineraryDayId = tw2, WishlistId = tokyoWlId, DayNumber = 2, DayTitle = "Asakusa — Culture & Slow Exploration",           MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
            new() { ItineraryDayId = tw3, WishlistId = tokyoWlId, DayNumber = 3, DayTitle = "Shinjuku — City Balance & Nightlife",            MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
            new() { ItineraryDayId = tw4, WishlistId = tokyoWlId, DayNumber = 4, DayTitle = "Central Tokyo — Food & Immersive Experience",    MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
            new() { ItineraryDayId = tw5, WishlistId = tokyoWlId, DayNumber = 5, DayTitle = "Flex Day — Personalize Your Tokyo Experience",   MorningCityId = tokyo.CityId, AfternoonCityId = tokyo.CityId, EveningCityId = tokyo.CityId },
        };

        tokyoWishlist.ItineraryDays.ToList()[0].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Meiji Shrine",        ItemDescription = "Visit Meiji Shrine — quiet forest shrine dedicated to Emperor Meiji.",                               ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Takeshita Street",    ItemDescription = "Explore Takeshita Street — street food, kawaii fashion, and youth culture.",                        ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 15 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Cat Street",          ItemDescription = "Walk through Cat Street — local boutiques, artisan cafes, and high-end streetwear.",                 ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Shibuya Crossing",    ItemDescription = "Experience Shibuya Crossing — the world's busiest pedestrian intersection.",                         ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw1, ItemTitle = "Shibuya Sky",         ItemDescription = "Shibuya Sky — sunset to night 229-meter rooftop city view. Book online in advance.",                 ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = true,  IsSelectedByDefault = true,  IndividualCostModifier = 18, SocialProofBadge = "Traveler Favorite" },
        };
        tokyoWishlist.ItineraryDays.ToList()[1].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw2, ItemTitle = "Senso-ji Temple",     ItemDescription = "Visit Senso-ji Temple — Tokyo's oldest temple, founded 645 AD.",                                      ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0, SocialProofBadge = "Must-See" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw2, ItemTitle = "Kaminarimon Gate",    ItemDescription = "Walk through Kaminarimon Gate — the iconic 700kg thunder lantern gate.",                               ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw2, ItemTitle = "Asakusa Streets",     ItemDescription = "Explore Asakusa streets — souvenirs, food stalls, and Edo-period atmosphere.",                        ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 20 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw2, ItemTitle = "Sumida Riverwalk",    ItemDescription = "Relax along the Sumida River walk with views of Tokyo Skytree at sunset.",                            ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0 },
        };
        tokyoWishlist.ItineraryDays.ToList()[2].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw3, ItemTitle = "Shinjuku Gyoen",                             ItemDescription = "Walk through Shinjuku Gyoen — 144 acres of Japanese, English, and French gardens.",         ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 4 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw3, ItemTitle = "Tokyo Metropolitan Government Building",      ItemDescription = "Free 202-meter skyline view. Bonus: Mt. Fuji on clear days.",                              ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 0, SocialProofBadge = "Best Free View in Tokyo" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw3, ItemTitle = "Shinjuku Golden Gai",                        ItemDescription = "Explore Golden Gai — 200+ tiny themed bars in narrow alleys. Cover charge per bar.",         ItemOrderIndex = 2, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 30 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw3, ItemTitle = "Kabukicho",                                  ItemDescription = "Kabukicho — neon nightlife district with Godzilla, namco TOKYO, and themed bars.",           ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = true,  IsSelectedByDefault = false, IndividualCostModifier = 20 },
        };
        tokyoWishlist.ItineraryDays.ToList()[3].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw4, ItemTitle = "Tsukiji Outer Market",                       ItemDescription = "Food crawl at Tsukiji Outer Market — fresh sushi breakfast and seafood stalls from 8AM.",     ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 25, SocialProofBadge = "Eat Like a Local" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw4, ItemTitle = "teamLab Borderless: Azabudai Hills",          ItemDescription = "Immersive digital art museum without a map. Book tickets online in advance.",                 ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 30, SocialProofBadge = "Most Visited Art Museum in Japan" },
        };
        tokyoWishlist.ItineraryDays.ToList()[4].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw5, ItemTitle = "Tokyo Disney (Option A)",   ItemDescription = "Option A: Tokyo DisneySea — 25th Anniversary Sparkling Jubilee with Fantasy Springs.",       ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = true,  IsSelectedByDefault = false, IndividualCostModifier = 68 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw5, ItemTitle = "Akihabara (Option B)",      ItemDescription = "Option B: Explore Akihabara — anime merchandise, retro gaming, maid cafes, and arcades.",    ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = true,  IsSelectedByDefault = false, IndividualCostModifier = 20 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw5, ItemTitle = "Last-minute shopping",      ItemDescription = "Don Quijote in Shinjuku or Shibuya 109 for final souvenirs and snacks.",                     ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = true,  IsSelectedByDefault = true,  IndividualCostModifier = 30 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = tw5, ItemTitle = "Farewell dinner in Tokyo",  ItemDescription = "Farewell dinner — Omoide Yokocho for yakitori or Ramen Alley in Shinjuku.",                  ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true,  IndividualCostModifier = 25 },
        };

        tokyoWishlist.WishlistDestinations = tokyoDestIds.Select(id => new WishlistDestination { WishlistId = tokyoWlId, DestinationId = id }).ToList();
        tokyoWishlist.Collaborators = new List<WishlistCollaborator>();

        // ── Wishlist 2: San Blas Relax Trip ───────────────────────────────────
        var sanBlasWlId = Guid.NewGuid();
        var sb1 = Guid.NewGuid(); var sb2 = Guid.NewGuid(); var sb3 = Guid.NewGuid();

        var sanBlasWishlist = new Wishlist
        {
            WishlistId = sanBlasWlId,
            WishlistName = "San Blas Relax Trip",
            WishlistDescription = "Turquoise water, hammocks over the sea, and fresh lobster on the sand — no wifi, no worries.",
            ShortStory = "Spend three days island-hopping through the San Blas archipelago, sleeping in rustic cabins over crystal-clear water, snorkeling shipwrecks, and eating lobster on the sand.",
            TotalDays = 3,
            PeopleType = "Couples and slow travelers",
            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/san-blas-hero.jpg",
            IsTemplate = true,
            OwnerUserId = null,
            TotalGlobalSaveCount = 0,
            IsFeatured = true,
            DefaultTravelersCount = 2,
            BasePricePerPerson = 350,
            CalculatedTotalCost = 700,
            DepositAmountRequired = 70,
            PrimaryPersonaTarget = "Couple Seeking Remote Nature Escape",
            AccommodationInclusions = "Overwater cabin on Isla Diablo (~$80/night)",
            TransitInclusions = "4x4 transfer from Panama City to Cartí + lanchas between islands",
            ActivityInclusions = "Isla Perro shipwreck snorkeling, Ibin's beach lunch, Dutch Cays day trip",
            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "4x4 transport Panama City → Cartí", "Inter-island lanchas", "Daily meals (fish, coconut rice)", "Snorkel gear rental", "Guna Yala entrance fee ($20)" }),
            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Relaxation", "Nature", "Romance", "Adventure" }),
            RawContentKeywords = "San Blas Panama Guna Yala islands snorkeling shipwreck overwater cabin lobster slow travel couple",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        sanBlasWishlist.ItineraryDays = new List<ItineraryDay>
        {
            new() { ItineraryDayId = sb1, WishlistId = sanBlasWlId, DayNumber = 1, DayTitle = "Arrival & First Island Escape",   MorningCityId = gunaYala.CityId, AfternoonCityId = gunaYala.CityId, EveningCityId = gunaYala.CityId },
            new() { ItineraryDayId = sb2, WishlistId = sanBlasWlId, DayNumber = 2, DayTitle = "Sandbars & Lobster Lunch",         MorningCityId = gunaYala.CityId, AfternoonCityId = gunaYala.CityId, EveningCityId = gunaYala.CityId },
            new() { ItineraryDayId = sb3, WishlistId = sanBlasWlId, DayNumber = 3, DayTitle = "Slow Morning & Return",            MorningCityId = gunaYala.CityId, AfternoonCityId = gunaYala.CityId, EveningCityId = gunaYala.CityId },
        };

        sanBlasWishlist.ItineraryDays.ToList()[0].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Drive Panama City → Cartí",         ItemDescription = "4x4 mountain drive (2.5–3 hrs) to the Cartí port. Last supplies at the Texaco stop. Bring cash, passport, and Dramamine.",    ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Boat transfer into San Blas",       ItemDescription = "Lancha ride (20–30 min) from Cartí through the Lemon Cays to Isla Perro.",                                                     ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Isla Perro — shipwreck snorkeling", ItemDescription = "Swim from the beach directly to the sunken Army freighter, teeming with tropical fish and coral.",                              ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0, SocialProofBadge = "Best Snorkeling in San Blas" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Lunch on the island",               ItemDescription = "Traditional Guna lunch — fresh fish and coconut rice served on the beach.",                                                      ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 12 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb1, ItemTitle = "Overnight on Isla Diablo",          ItemDescription = "Check into a rustic overwater cabin on Isla Diablo — the social hub of the archipelago.",                                        ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 80 },
        };
        sanBlasWishlist.ItineraryDays.ToList()[1].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "The Pool sandbar",                  ItemDescription = "Morning at The Pool — a famous shallow sandbar with waist-deep, crystal-clear water in the middle of the ocean.",                ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0, SocialProofBadge = "Hidden Gem" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "Boat to Banedup Island",            ItemDescription = "Short lancha ride to Isla Banedup — home of Ibin's Beach Restaurant.",                                                          ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "Lunch at Ibin's Beach Restaurant",  ItemDescription = "Seafood lunch at the legendary pirate-chic beach restaurant — lobster, octopus curry, and cocktails with your feet in the sand.", ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 45, SocialProofBadge = "Must-Eat" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "Snorkeling near the Dutch Cays",    ItemDescription = "Afternoon snorkeling at Cayos Holandeses — the most vibrant coral reefs in San Blas, far from mainland runoff.",                  ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = true,  IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb2, ItemTitle = "Overnight overwater cabin",         ItemDescription = "Second night on Isla Diablo — evening drinks, stargazing, and the sound of the Caribbean.",                                      ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 80 },
        };
        sanBlasWishlist.ItineraryDays.ToList()[2].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb3, ItemTitle = "Breakfast by the sea",              ItemDescription = "Last morning breakfast on the island — fresh fruit, bread, and coffee as the sun rises over the archipelago.",                   ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb3, ItemTitle = "Visit Kuna village",                ItemDescription = "Optional stop at a nearby Guna village — see traditional molas, meet community members, and understand Guna autonomy.",           ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = true,  IsSelectedByDefault = true, IndividualCostModifier = 5 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb3, ItemTitle = "Return boat to Cartí",              ItemDescription = "Lancha back to the mainland port. Sit low in the boat and keep electronics in a dry bag.",                                       ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = sb3, ItemTitle = "Drive back to Panama City",         ItemDescription = "Return mountain drive to Panama City. Stop at the Texaco for a cold drink and to decompress after island time.",                  ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
        };

        var sanBlasDestIds = new[]
        {
            PId("San Blas Islands"), PId("Isla Perro"), PId("Ibin's"),
            PId("Isla Diablo"), PId("Cayos Holandeses"),
        };
        sanBlasWishlist.WishlistDestinations = sanBlasDestIds.Select(id => new WishlistDestination { WishlistId = sanBlasWlId, DestinationId = id }).ToList();
        sanBlasWishlist.Collaborators = new List<WishlistCollaborator>();

        // ── Wishlist 3: Panama City Highlights ────────────────────────────────
        var pcWlId = Guid.NewGuid();
        var pc1 = Guid.NewGuid(); var pc2 = Guid.NewGuid();

        var panamaCityWishlist = new Wishlist
        {
            WishlistId = pcWlId,
            WishlistName = "Panama City Highlights (Pacific Side)",
            WishlistDescription = "History, skyline views, the Panama Canal, and fresh seafood — the perfect introduction to Panama City.",
            ShortStory = "Spend two days exploring Panama City's historic streets, walking along the waterfront skyline, and witnessing one of the greatest engineering feats in the world.",
            TotalDays = 2,
            PeopleType = "First-time visitors and city lovers",
            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/panamacity.jpeg",
            IsTemplate = true,
            OwnerUserId = null,
            TotalGlobalSaveCount = 0,
            IsFeatured = true,
            DefaultTravelersCount = 2,
            BasePricePerPerson = 160,
            CalculatedTotalCost = 320,
            DepositAmountRequired = 0,
            PrimaryPersonaTarget = "First-Time Panama City Visitor",
            AccommodationInclusions = "Mid-range hotel in Marbella or Bella Vista (~$70-90/night)",
            TransitInclusions = "Uber/taxi for Canal and Causeway trips (~$25 total)",
            ActivityInclusions = "Canal IMAX + museum admission, Casco Viejo walking tour, Biomuseo, Mercado de Mariscos",
            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "Panama Canal IMAX ticket ($17.22)", "Biomuseo admission ($20)", "Casco Viejo self-guided map", "Seafood lunch at Mercado de Mariscos" }),
            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Culture", "Food", "Educational", "Urban" }),
            RawContentKeywords = "Panama City Casco Viejo canal Biomuseo Miraflores locks seafood first time history",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        panamaCityWishlist.ItineraryDays = new List<ItineraryDay>
        {
            new() { ItineraryDayId = pc1, WishlistId = pcWlId, DayNumber = 1, DayTitle = "Old Town & City Energy",          MorningCityId = panamaCity.CityId, AfternoonCityId = panamaCity.CityId, EveningCityId = panamaCity.CityId },
            new() { ItineraryDayId = pc2, WishlistId = pcWlId, DayNumber = 2, DayTitle = "Canal & History of Panama",       MorningCityId = panamaCity.CityId, AfternoonCityId = panamaCity.CityId, EveningCityId = panamaCity.CityId },
        };

        panamaCityWishlist.ItineraryDays.ToList()[0].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Casco Viejo — morning walk",       ItemDescription = "Arrive at Casco Viejo and walk the cobblestone streets, plazas, and Palacio de las Garzas.",                              ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0, SocialProofBadge = "UNESCO Heritage" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Lunch at Mercado de Mariscos",     ItemDescription = "Fresh ceviche and fried fish at Panama City's working seafood market. Corvina cups from $1–$2.",                           ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 12, SocialProofBadge = "Best $2 Meal in Central America" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Walk along Cinta Costera",         ItemDescription = "Stroll the 7km waterfront promenade connecting Paitilla skyscrapers to Casco Viejo's colonial walls.",                    ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Sunset overlooking Panama Bay",    ItemDescription = "Watch the Pacific sunset from Cinta Costera's Mirador del Pacífico — 17th-century ruins on your right, glass towers left.", ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc1, ItemTitle = "Rooftop bar in Casco Viejo",       ItemDescription = "End the night at one of Casco's rooftop bars — neon skyline, Geisha coffee cocktails, and warm sea breeze.",               ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = true,  IsSelectedByDefault = true, IndividualCostModifier = 20 },
        };
        panamaCityWishlist.ItineraryDays.ToList()[1].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Panama Canal — Miraflores Locks",  ItemDescription = "Arrive at 8AM for the morning transit window. Watch 100,000-ton vessels lifted by gravity alone through the original locks.", ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 17, SocialProofBadge = "Panama's #1 Landmark" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Canal IMAX film + museum",         ItemDescription = "45-minute 3D IMAX narrated by Morgan Freeman, then four floors of exhibits on the canal's history and mechanics.",           ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Lunch near the canal",             ItemDescription = "Lunch at one of the restaurants near the Miraflores visitor center before heading to the Causeway.",                          ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 15 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Biomuseo",                         ItemDescription = "Frank Gehry's only Latin American building — Panama's biodiversity story told through 8 immersive galleries.",               ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 20, SocialProofBadge = "Architectural Icon" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = pc2, ItemTitle = "Panoramic city & canal views",     ItemDescription = "Walk the Amador Causeway at golden hour — container ships under the Bridge of the Americas with the city glowing behind you.", ItemOrderIndex = 4, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
        };

        var pcDestIds = new[]
        {
            PId("Casco Viejo"), PId("Panama Canal"), PId("Cinta Costera"),
            PId("Mercado de Mariscos"), PId("Biomuseo"),
        };
        panamaCityWishlist.WishlistDestinations = pcDestIds.Select(id => new WishlistDestination { WishlistId = pcWlId, DestinationId = id }).ToList();
        panamaCityWishlist.Collaborators = new List<WishlistCollaborator>();

        // ── Wishlist 4: Bocas del Toro Party & Surf ───────────────────────────
        var bocasWlId = Guid.NewGuid();
        var bw1 = Guid.NewGuid(); var bw2 = Guid.NewGuid();
        var bw3 = Guid.NewGuid(); var bw4 = Guid.NewGuid();

        var bocasWishlist = new Wishlist
        {
            WishlistId = bocasWlId,
            WishlistName = "Bocas del Toro Party & Surf",
            WishlistDescription = "Caribbean rhythm, boat parties, and world-class waves — Panama's wild side.",
            ShortStory = "Spend four days hopping between islands, surfing tropical waves, and partying on boats with travelers from around the world.",
            TotalDays = 4,
            PeopleType = "Backpackers, party travelers, and surfers",
            WishlistHeroImage = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/bocas.jpeg",
            IsTemplate = true,
            OwnerUserId = null,
            TotalGlobalSaveCount = 0,
            IsFeatured = true,
            DefaultTravelersCount = 2,
            BasePricePerPerson = 320,
            CalculatedTotalCost = 640,
            DepositAmountRequired = 0,
            PrimaryPersonaTarget = "Budget Adventure Traveler",
            AccommodationInclusions = "Hostel or budget hotel in Bocas Town (~$25-50/night)",
            TransitInclusions = "Flights Panama City → Bocas del Toro + inter-island pangas",
            ActivityInclusions = "Filthy Friday boat party, Red Frog Beach day, Starfish Beach colectivo, Cayos Zapatilla tour",
            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "Domestic flight or bus Panama City → Bocas", "Daily inter-island panga transfers", "Red Frog Beach entry ($5)", "Starfish Beach colectivo ($3 each way)" }),
            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Adventure", "Nightlife", "Nature", "Budget" }),
            RawContentKeywords = "Bocas del Toro Panama Caribbean party surf backpacker hostel island hopping Red Frog beach",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        bocasWishlist.ItineraryDays = new List<ItineraryDay>
        {
            new() { ItineraryDayId = bw1, WishlistId = bocasWlId, DayNumber = 1, DayTitle = "Arrival & Island Vibes",   MorningCityId = bocas.CityId, AfternoonCityId = bocas.CityId, EveningCityId = bocas.CityId },
            new() { ItineraryDayId = bw2, WishlistId = bocasWlId, DayNumber = 2, DayTitle = "Filthy Friday Boat Party", MorningCityId = bocas.CityId, AfternoonCityId = bocas.CityId, EveningCityId = bocas.CityId },
            new() { ItineraryDayId = bw3, WishlistId = bocasWlId, DayNumber = 3, DayTitle = "Beaches & Surf",           MorningCityId = bocas.CityId, AfternoonCityId = bocas.CityId, EveningCityId = bocas.CityId },
            new() { ItineraryDayId = bw4, WishlistId = bocasWlId, DayNumber = 4, DayTitle = "Snorkel & Departure",      MorningCityId = bocas.CityId, AfternoonCityId = bocas.CityId, EveningCityId = bocas.CityId },
        };

        bocasWishlist.ItineraryDays.ToList()[0].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw1, ItemTitle = "Arrive in Bocas Town",          ItemDescription = "Fly or bus into Bocas Town (Isla Colón). Check into your hostel and drop your bags.",                                          ItemOrderIndex = 0, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw1, ItemTitle = "Explore town and waterfront",   ItemDescription = "Walk the grid, find your panga operator for tomorrow, and get your bearings on Main Street.",                                  ItemOrderIndex = 1, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw1, ItemTitle = "Dinner and drinks in Bocas",    ItemDescription = "Dinner at a waterfront restaurant then drinks at The Pub or Toro Loco. Island time starts now.",                              ItemOrderIndex = 2, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 30 },
        };
        bocasWishlist.ItineraryDays.ToList()[1].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw2, ItemTitle = "Filthy Friday boat party",      ItemDescription = "The legendary all-day boat party — island hopping with music, drinks, and swim stops in crystal-clear water.",                  ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 35, SocialProofBadge = "Bocas Institution" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw2, ItemTitle = "Island hopping swim stops",     ItemDescription = "Multiple stops at sandbars and shallow bays. Keep electronics in a dry bag.",                                                  ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw2, ItemTitle = "Recovery evening in town",      ItemDescription = "Return to Bocas Town, shower, grab a cheap dinner at one of the local fondas before an early night.",                          ItemOrderIndex = 2, TimeOfDay = "Evening",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 10 },
        };
        bocasWishlist.ItineraryDays.ToList()[2].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw3, ItemTitle = "Red Frog Beach — surf & frogs", ItemDescription = "Morning panga to Red Frog Beach. Surf the Caribbean break, walk the jungle trail, and spot the tiny red poison dart frogs.",   ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 5, SocialProofBadge = "Unmissable" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw3, ItemTitle = "Starfish Beach",                ItemDescription = "Colectivo bus ($3) to Boca del Drago then 20-min coastal walk to Playa Estrella. Sit neck-deep with a Balboa beer.",            ItemOrderIndex = 1, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 3, SocialProofBadge = "Most Photographed Beach" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw3, ItemTitle = "Relax in shallow water",        ItemDescription = "Float with the starfish, eat at one of the rustic beach shacks, and let the Caribbean do its thing.",                           ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 8 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw3, ItemTitle = "Sunset at Wizard Beach",        ItemDescription = "Optional: 30-45 min jungle hike from Old Bank for the most dramatic Caribbean sunset. Leave all valuables at the hostel.",      ItemOrderIndex = 3, TimeOfDay = "Evening",   IsOptionalActivity = true,  IsSelectedByDefault = false, IndividualCostModifier = 0 },
        };
        bocasWishlist.ItineraryDays.ToList()[3].ItineraryItems = new List<ItineraryItem>
        {
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw4, ItemTitle = "Cayos Zapatilla tour",          ItemDescription = "Full-day tour to Cayos Zapatilla — UNESCO coral gardens, nurse sharks, Hawksbill turtles. Pack your own lunch.",               ItemOrderIndex = 0, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 40, SocialProofBadge = "Best Snorkeling in Bocas" },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw4, ItemTitle = "Snorkel coral reefs",           ItemDescription = "Guided snorkel at Cayo Coral en route — vibrant beds of hard and soft coral with tropical fish.",                               ItemOrderIndex = 1, TimeOfDay = "Morning",   IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw4, ItemTitle = "Beach time on Zapatilla",       ItemDescription = "Walk the perimeter trail (El Bosque Detrás del Arrecife), lounge on powder sand, and look for nesting turtles.",               ItemOrderIndex = 2, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
            new() { ItineraryItemId = Guid.NewGuid(), ItineraryDayId = bw4, ItemTitle = "Return to Bocas & depart",      ItemDescription = "Return to Bocas Town and catch your evening flight or bus back to Panama City.",                                                 ItemOrderIndex = 3, TimeOfDay = "Afternoon", IsOptionalActivity = false, IsSelectedByDefault = true, IndividualCostModifier = 0 },
        };

        var bocasDestIds = new[]
        {
            PId("Bocas Town"), PId("Red Frog"), PId("Starfish Beach"),
            PId("Wizard Beach"), PId("Cayos Zapatilla"),
        };
        bocasWishlist.WishlistDestinations = bocasDestIds.Select(id => new WishlistDestination { WishlistId = bocasWlId, DestinationId = id }).ToList();
        bocasWishlist.Collaborators = new List<WishlistCollaborator>();

        // ── Save all four wishlists ────────────────────────────────────────────
        db.Wishlists.AddRange(tokyoWishlist, sanBlasWishlist, panamaCityWishlist, bocasWishlist);
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 9. FEATURED POOL
    // All four wishlists seeded as editorial featured entries.
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task SeedFeaturedPoolAsync(HodracDbContext db)
    {
        if (await db.FeaturedWishlistPool.AnyAsync()) return;

        var wishlists = await db.Wishlists
            .Where(w => w.IsTemplate)
            .Select(w => new { w.WishlistId, w.WishlistName })
            .ToListAsync();

        // Assign relative selection weights — Tokyo gets a slightly higher weight
        // as the most content-rich wishlist, then Panama City, then San Blas, then Bocas.
        var weights = new Dictionary<string, double>
        {
            { "5-Day Tokyo",        1.5 },
            { "Panama City",        1.2 },
            { "San Blas",           1.0 },
            { "Bocas del Toro",     1.0 },
        };

        double GetWeight(string name)
        {
            foreach (var kv in weights)
                if (name.Contains(kv.Key)) return kv.Value;
            return 1.0;
        }

        db.FeaturedWishlistPool.AddRange(
            wishlists.Select(w => new FeaturedWishlistPool
            {
                FeaturedWishlistPoolId = Guid.NewGuid(),
                WishlistId = w.WishlistId,
                PoolType = "Editorial",
                PaidAmount = 0,
                DailyImpressionLimit = 10000,
                CurrentImpressionsToday = 0,
                RandomSelectionWeight = GetWeight(w.WishlistName),
                LastRotationDate = DateTimeOffset.UtcNow,
            })
        );

        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private static DescriptionJsonDto BuildDesc(
        string overview, string localPerspective, string directions,
        string whatToKnow, string thingsToBeWaryOf, string hiddenCost,
        IEnumerable<string> nearbyComplements, string bestTimeToVisit,
        string crowdLevel, string accessibility, string idealDuration)
        => new()
        {
            Overview = overview,
            LocalPerspective = localPerspective,
            Directions = directions,
            WhatToKnow = whatToKnow,
            ThingsToBeWaryOf = thingsToBeWaryOf,
            HiddenCost = hiddenCost,
            NearbyComplements = nearbyComplements.ToList(),
            BestTimeToVisit = bestTimeToVisit,
            CrowdLevel = crowdLevel,
            Accessibility = accessibility,
            IdealDuration = idealDuration,
        };

    private static string[] DeriveVibes(IEnumerable<string> tagKeys)
    {
        var k = tagKeys.ToHashSet();
        var vibes = new List<string>();
        if (k.Contains("food_focused")) vibes.Add("Food");
        if (k.Contains("cultural")) vibes.Add("Culture");
        if (k.Contains("nightlife")) vibes.Add("Nightlife");
        if (k.Contains("nature")) vibes.Add("Nature");
        if (k.Contains("adventurous")) vibes.Add("Adventure");
        if (k.Contains("relaxing")) vibes.Add("Relaxation");
        if (k.Contains("premium")) vibes.Add("Luxury");
        if (k.Contains("budget_friendly")) vibes.Add("Budget");
        if (k.Contains("romantic")) vibes.Add("Romance");
        if (k.Contains("shopping")) vibes.Add("Shopping");
        if (k.Contains("photography")) vibes.Add("Aesthetic");
        if (k.Contains("educational")) vibes.Add("Educational");
        if (!vibes.Any()) vibes.Add("Urban");
        return vibes.ToArray();
    }
}