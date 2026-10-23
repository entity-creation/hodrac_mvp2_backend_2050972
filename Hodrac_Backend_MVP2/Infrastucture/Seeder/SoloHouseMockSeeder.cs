using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.Infrastructure.Seeder;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// SALES MOCKUP — DEV DATABASES ONLY. Seeds a "Solo House" destination and a
    /// "A December Sunday in Lagos" wishlist built around it, plus a partner
    /// booking link, so the app can be screenshotted for the pitch.
    ///
    /// All Solo House specifics (brunch, prices, dishes, booking number, photos)
    /// are SAMPLE placeholders, not facts about the business. Photos use
    /// picsum.photos seeds. Program.cs refuses to run this against the Render
    /// (production) database.
    ///
    /// Run:    dotnet run -- --seed-solohouse
    /// Remove: dotnet run -- --remove-solohouse
    /// Idempotent: re-running changes nothing.
    /// </summary>
    public static class SoloHouseMockSeeder
    {
        public const string SoloHouseName = "Solo House";
        public const string WishlistName = "A December Sunday in Lagos";
        private const string PartnerName = "Solo House (mockup)";

        // Clearly invalid number — this is a mockup, it must not reach a real person.
        private const string SampleBookingUrl = "https://wa.me/2340000000000?text=Hi%20Solo%20House%2C%20I%27d%20like%20to%20book%20Sunday%20brunch";

        private static string Photo(string seed, int w = 1600, int h = 1000) => $"https://picsum.photos/seed/{seed}/{w}/{h}";

        private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private sealed record DestSpec(
            string Name, double Lat, double Lng, decimal Cost, int Luxury, int Family, int Adventure, int Aesthetic,
            int Safety, string Access, string[] Categories, string[] Tags, string[] Alternates, string[] ImageSeeds,
            DescriptionJsonDto Description);

        public static async Task SeedAsync(HodracDbContext db, CancellationToken ct = default)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // ── Reference data (lookup-or-create) ────────────────────────────
            var nigeria = await db.Countries.FirstOrDefaultAsync(c => c.CountryName == "Nigeria", ct);
            if (nigeria is null)
            {
                nigeria = new Country
                {
                    CountryId = Guid.NewGuid(),
                    CountryName = "Nigeria",
                    Continent = "Africa",
                    CountryFlagEmoji = "🇳🇬",
                    GlobalHeroImage = Photo("lagos-skyline"),
                    VisaRequirementsSummary = "Visa required for most nationalities (e-visa / visa on arrival approval via Nigeria Immigration Service); check official requirements.",
                    PowerPlugType = "Type D/G",
                    DrivingSide = "Right",
                    EstimatedDailyTaxRate = 0.075m,
                };
                db.Countries.Add(nigeria);
            }

            var lagos = await db.Cities.FirstOrDefaultAsync(c => c.CityName == "Lagos" && c.CountryId == nigeria.CountryId, ct);
            if (lagos is null)
            {
                lagos = new City
                {
                    CityId = Guid.NewGuid(),
                    CityName = "Lagos",
                    CountryId = nigeria.CountryId,
                    Latitude = 6.5244,
                    Longitude = 3.3792,
                    CityDescription = "Nigeria's largest city, spanning the mainland and the islands around the Lagos Lagoon."
                };
                db.Cities.Add(lagos);
            }

            var english = await db.Languages.FirstOrDefaultAsync(l => l.LanguageName == "English", ct);
            if (english is null)
            {
                english = new Language { LanguageId = Guid.NewGuid(), LanguageName = "English", LanguageCode = "en" };
                db.Languages.Add(english);
            }

            var ngn = await db.Currencies.FirstOrDefaultAsync(c => c.CurrencyCode == "NGN", ct);
            if (ngn is null)
            {
                ngn = new Currency
                {
                    CurrencyId = Guid.NewGuid(),
                    CurrencyName = "Nigerian Naira",
                    CurrencyCode = "NGN",
                    CurrencySymbol = "₦",
                    ExchangeRateToBase = 1500m,
                    LastExchangeRateUpdate = DateTimeOffset.UtcNow
                };
                db.Currencies.Add(ngn);
            }
            await db.SaveChangesAsync(ct);

            // ── Destinations ─────────────────────────────────────────────────
            var specs = Specs();
            var destinations = new Dictionary<string, Destination>();
            foreach (var spec in specs)
                destinations[spec.Name] = await EnsureDestinationAsync(db, spec, nigeria, lagos, english, ngn, ct);

            var soloHouse = destinations[SoloHouseName];
            if (!soloHouse.IsFeatured)
            {
                soloHouse.IsFeatured = true;
                soloHouse.FeaturedUntil = null;
                soloHouse.FeaturedOrder = 1;
            }
            await db.SaveChangesAsync(ct);

            // ── Wishlist ─────────────────────────────────────────────────────
            var wishlist = await db.Wishlists.FirstOrDefaultAsync(w => w.IsTemplate && w.WishlistName == WishlistName, ct);
            if (wishlist is null)
            {
                wishlist = BuildWishlist(lagos);
                db.Wishlists.Add(wishlist);
                await db.SaveChangesAsync(ct);
                AddItinerary(db, wishlist, lagos, destinations);
                await db.SaveChangesAsync(ct);
            }

            foreach (var dest in destinations.Values)
            {
                var linked = await db.WishlistDestinations
                    .AnyAsync(wd => wd.WishlistId == wishlist.WishlistId && wd.DestinationId == dest.DestinationId, ct);
                if (!linked)
                    db.WishlistDestinations.Add(new WishlistDestination { WishlistId = wishlist.WishlistId, DestinationId = dest.DestinationId });
            }
            await db.SaveChangesAsync(ct);

            // ── Partner booking CTA (shows as `partner` on both) ─────────────
            var partner = await db.Partners.FirstOrDefaultAsync(p => p.Name == PartnerName, ct);
            if (partner is null)
            {
                partner = new Partner
                {
                    Name = PartnerName,
                    LogoUrl = Photo("solohouse-logo", 400, 400),
                    DefaultCtaLabel = "Reserve on WhatsApp",
                    IsActive = true
                };
                db.Partners.Add(partner);
                await db.SaveChangesAsync(ct);
            }

            if (!await db.PartnerLinks.AnyAsync(l => l.PartnerId == partner.PartnerId && l.DestinationId == soloHouse.DestinationId, ct))
                db.PartnerLinks.Add(new PartnerLink
                {
                    PartnerId = partner.PartnerId,
                    DestinationId = soloHouse.DestinationId,
                    BookingUrl = SampleBookingUrl,
                    CtaLabel = "Book Sunday brunch",
                    IsSponsored = true,
                    Priority = 100
                });

            if (!await db.PartnerLinks.AnyAsync(l => l.PartnerId == partner.PartnerId && l.WishlistId == wishlist.WishlistId, ct))
                db.PartnerLinks.Add(new PartnerLink
                {
                    PartnerId = partner.PartnerId,
                    WishlistId = wishlist.WishlistId,
                    BookingUrl = SampleBookingUrl,
                    CtaLabel = "Reserve your brunch table",
                    IsSponsored = true,
                    Priority = 100
                });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        /// <summary>Removes everything SeedAsync created that is unique to the mockup.</summary>
        public static async Task RemoveAsync(HodracDbContext db, CancellationToken ct = default)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var partner = await db.Partners.FirstOrDefaultAsync(p => p.Name == PartnerName, ct);
            if (partner is not null) db.Partners.Remove(partner);   // cascades links + clicks

            var wishlist = await db.Wishlists.FirstOrDefaultAsync(w => w.IsTemplate && w.WishlistName == WishlistName, ct);
            if (wishlist is not null)
            {
                db.WishlistCreatorAttributions.RemoveRange(db.WishlistCreatorAttributions.Where(a => a.WishlistId == wishlist.WishlistId));
                db.Wishlists.Remove(wishlist);                      // cascades days, items, links
            }

            // Only Solo House itself — the other stops are real places other wishlists may use.
            var solo = await db.Destinations.FirstOrDefaultAsync(d => d.DestinationName == SoloHouseName, ct);
            if (solo is not null && !await db.TripPosts.AnyAsync(t => t.DestinationId == solo.DestinationId, ct))
                db.Destinations.Remove(solo);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static async Task<Destination> EnsureDestinationAsync(
            HodracDbContext db, DestSpec spec, Country country, City city, Language language, Currency currency, CancellationToken ct)
        {
            var existing = await db.Destinations.FirstOrDefaultAsync(d => d.DestinationName == spec.Name && d.CountryId == country.CountryId, ct);
            if (existing is not null) return existing;

            var fields = DestinationSearchFields.Derive(spec.Name);
            var dest = new Destination
            {
                DestinationId = Guid.NewGuid(),
                DestinationName = spec.Name,
                CleanNormalizedSearchName = fields.CleanNormalizedSearchName,
                MetaphoneCode = fields.MetaphoneCode,
                DoubleMetaphonePrimary = fields.DoubleMetaphonePrimary,
                DoubleMetaphoneSecondary = fields.DoubleMetaphoneSecondary,
                CommonAlternateSpellingsJson = JsonSerializer.Serialize(spec.Alternates, Json),
                DescriptionJson = JsonSerializer.Serialize(spec.Description, Json),
                AverageCostPerDay = spec.Cost,
                LuxuryRating = spec.Luxury,
                AccessibilityType = spec.Access,
                FamilyFriendlyScore = spec.Family,
                AdventurePaceScore = spec.Adventure,
                AestheticTrendScore = spec.Aesthetic,
                PsychographicVibeTagsJson = JsonSerializer.Serialize(spec.Tags, Json),
                Latitude = spec.Lat,
                Longitude = spec.Lng,
                TimeZone = "W. Central Africa Standard Time",
                SafetyLevel = spec.Safety,
                CountryId = country.CountryId,
            };
            db.Destinations.Add(dest);

            for (var i = 0; i < spec.ImageSeeds.Length; i++)
                db.DestinationImages.Add(new DestinationImage
                {
                    DestinationImageId = Guid.NewGuid(),
                    DestinationId = dest.DestinationId,
                    ImageUrl = Photo(spec.ImageSeeds[i]),
                    ThumbnailUrl = Photo(spec.ImageSeeds[i], 800, 500),
                    Caption = i == 0 ? $"{spec.Name}" : $"{spec.Name} — sample photo {i + 1}",
                    DisplayOrder = i + 1,
                    ImageType = i == 0 ? "Hero" : "Gallery",
                    ShotContext = "Sample",
                    IsAiGenerated = false
                });

            db.DestinationCities.Add(new DestinationCity { DestinationId = dest.DestinationId, CityId = city.CityId });
            db.DestinationLanguages.Add(new DestinationLanguage { DestinationId = dest.DestinationId, LanguageId = language.LanguageId });
            db.DestinationCurrencies.Add(new DestinationCurrency { DestinationId = dest.DestinationId, CurrencyId = currency.CurrencyId });

            foreach (var key in spec.Categories)
            {
                var cat = await db.Categories.FirstOrDefaultAsync(c => c.Key == key, ct)
                          ?? db.Categories.Local.FirstOrDefault(c => c.Key == key);
                if (cat is null)
                {
                    cat = new Category
                    {
                        CategoryId = Guid.NewGuid(), Key = key, CategoryName = key.Replace('_', ' ').ToTitleCase(),
                        CategoryDescription = "Auto-added category", IconName = "tag", ColorHex = "#888888"
                    };
                    db.Categories.Add(cat);
                }
                db.DestinationCategories.Add(new DestinationCategory { DestinationId = dest.DestinationId, CategoryId = cat.CategoryId });
            }

            foreach (var key in spec.Tags)
            {
                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Key == key, ct)
                          ?? db.Tags.Local.FirstOrDefault(t => t.Key == key);
                if (tag is null)
                {
                    tag = new Tag { TagId = Guid.NewGuid(), Key = key, TagName = key.Replace('_', ' ').ToTitleCase(), TargetPersonaType = "Explorer" };
                    db.Tags.Add(tag);
                }
                db.DestinationTags.Add(new DestinationTag { DestinationId = dest.DestinationId, TagId = tag.TagId });
            }

            return dest;
        }

        private static Wishlist BuildWishlist(City lagos) => new()
        {
            WishlistId = Guid.NewGuid(),
            WishlistName = WishlistName,
            WishlistDescription = "A slow, sun-warmed Sunday on the Lagos mainland, built around brunch at Solo House in Mangoro: a morning stroll in the park, a long brunch of reinvented Nigerian classics, an afternoon with Fela's legacy, and live music as the day winds down.",
            ShortStory = "Green-park mornings, a brunch menu that rewrites the classics, and Afrobeat by nightfall — one easy December Sunday, all on the mainland.",
            TotalDays = 1,
            PeopleType = "Couples & Friends",
            WishlistHeroImage = Photo("solohouse-brunch-spread"),
            GlobalInclusionsJson = JsonSerializer.Serialize(new[] { "Sunday brunch at Solo House", "Kalakuta Republic Museum entry", "Mainland day route" }, Json),
            RawContentKeywords = "Lagos, mainland, Mangoro, Ikeja, Solo House, Sunday brunch, December, JJT Park, Kalakuta Republic Museum, Fela, New Afrika Shrine, Afrobeat",
            PsychologicalVibeTagsJson = JsonSerializer.Serialize(new[] { "Foodie", "Culture", "Social", "Relaxing" }, Json),
            DefaultTravelersCount = 2,
            BasePricePerPerson = 45m,
            CalculatedTotalCost = 90m,
            DepositAmountRequired = 0m,
            AccommodationInclusions = "Day itinerary — no accommodation included",
            TransitInclusions = "All stops are within a short ride of each other around Ikeja and Mangoro; rideshare recommended",
            ActivityInclusions = "Solo House Sunday brunch (sample price), museum entry",
            IsTemplate = true,
            IsFeatured = true,
            PrimaryPersonaTarget = "Lagos Weekend Brunch Crowd",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        private static void AddItinerary(HodracDbContext db, Wishlist wishlist, City lagos, IReadOnlyDictionary<string, Destination> d)
        {
            var day = new ItineraryDay
            {
                ItineraryDayId = Guid.NewGuid(),
                DayNumber = 1,
                DayTitle = "A December Sunday on the Mainland",
                MorningCityId = lagos.CityId,
                AfternoonCityId = lagos.CityId,
                EveningCityId = lagos.CityId,
                WishlistId = wishlist.WishlistId
            };
            db.ItineraryDays.Add(day);

            var items = new (string Title, string Description, string TimeOfDay, string Image, string Badge, decimal Cost, bool Optional)[]
            {
                ("Morning stroll at Johnson Jakande Tinubu Park",
                 "Ease into the day with a walk among the lawns, fountains and shaded paths of the green park in Alausa, Ikeja, before the midday heat.",
                 "Morning", Photo("jjt-park-morning"), "Easy Start", 0m, false),
                ("Short ride to Solo House, Mangoro",
                 "A quick rideshare down to 17 Sule Street, Mangoro. Reservations are recommended for December Sundays.",
                 "Morning", Photo("lagos-mainland-ride"), "Quick Transfer", 3m, false),
                ("Sunday Brunch at Solo House",
                 "The heart of the day: a long, unhurried brunch where familiar Nigerian flavours are reworked — suya-spiced chicken and waffles with honey-pepper drizzle, a plantain and smoked-fish benedict, and ofada-stew shakshuka with warm sourdough — with bottomless zobo mimosas and a live acoustic set. Sample package: ₦35,000 per person, 11am–3pm.",
                 "Afternoon", Photo("solohouse-brunch-spread"), "Featured Brunch", 25m, false),
                ("Kalakuta Republic Museum",
                 "Walk off brunch at Fela Anikulapo-Kuti's former home in Ikeja, now a museum of his life, music and activism — rooms, costumes, instruments and rooftop views.",
                 "Afternoon", Photo("kalakuta-museum"), "Lagos Legend", 5m, false),
                ("Evening at the New Afrika Shrine",
                 "End the Sunday with Afrobeat at the home of Fela's legacy in Ikeja. Check the week's line-up before you go — or keep it calm and head home from the museum.",
                 "Evening", Photo("new-afrika-shrine-night"), "Live Music", 7m, true),
            };

            for (var i = 0; i < items.Length; i++)
            {
                var it = items[i];
                db.ItineraryItems.Add(new ItineraryItem
                {
                    ItineraryItemId = Guid.NewGuid(),
                    ItineraryDayId = day.ItineraryDayId,
                    ItemTitle = it.Title,
                    ItemDescription = it.Description,
                    ItemOrderIndex = i + 1,
                    TimeOfDay = it.TimeOfDay,
                    ImageUrl = it.Image,
                    SocialProofBadge = it.Badge,
                    IndividualCostModifier = it.Cost,
                    IsOptionalActivity = it.Optional,
                    IsSelectedByDefault = !it.Optional
                });
            }
        }

        private static DestSpec[] Specs() =>
        [
            new(SoloHouseName, 6.6105, 3.3428, 25m, 3, 4, 1, 5, 2, "Road",
                ["food_experience"],
                ["food_focused", "brunch", "photography", "social", "local_favorite", "relaxing"],
                ["Solo House Lagos", "Solo House Mangoro", "SoloHouse"],
                ["solohouse-brunch-spread", "solohouse-dining-room", "solohouse-chicken-waffles", "solohouse-benedict", "solohouse-shakshuka", "solohouse-mimosas", "solohouse-garden"],
                new DescriptionJsonDto
                {
                    Overview = "Solo House is a mainland brunch spot in Mangoro where Nigerian classics get a playful rework. Sundays are the main event: a long, sociable brunch with an inventive menu, bottomless zobo mimosas and a live acoustic set, in a calm space away from the island crowds.\n\nWhat to Order: Suya-spiced chicken and waffles with a honey-pepper drizzle; plantain and smoked-fish benedict; ofada-stew shakshuka with warm sourdough; and zobo-poached pear pancakes to finish.",
                    Directions = "Address: 17 Sule Street, Mangoro, Lagos (mainland).\n\nGetting There: Easiest by rideshare — Mangoro sits between Ikeja and Oshodi, a short drive from Ikeja GRA and Alausa. Allow extra time on December Sundays.",
                    WhatToKnow = "Sunday Brunch (sample): 11:00am – 3:00pm.\nPrice (sample): ₦35,000 per person — three courses, bottomless zobo mimosas or Chapman, and a live acoustic set.\nBooking (sample): reserve on WhatsApp; tables for groups of 6+ should be booked ahead.",
                    ThingsToBeWaryOf = "December Sundays fill up — book ahead.\nTraffic around Ikeja and Oshodi can be heavy; leave buffer time.",
                    LocalPerspective = "A mainland alternative to island brunch: regulars come for the reinvented menu and the slower pace, and tend to stay well into the afternoon.",
                    HiddenCost = "Brunch package (sample): ₦35,000 per person.\nExtra drinks beyond the package: priced separately.\nRideshare from Ikeja: short hop.",
                    NearbyComplements = new List<string>
                    {
                        "Johnson Jakande Tinubu Park: green space in Alausa for a morning stroll.",
                        "Kalakuta Republic Museum: Fela's former home in Ikeja.",
                        "New Afrika Shrine: Afrobeat venue in Ikeja for the evening."
                    },
                    BestTimeToVisit = "Sundays, 11:00am – 3:00pm (sample hours). Arrive early for the best tables.",
                    CrowdLevel = "Busy on Sundays, especially in December.",
                    Accessibility = "Ground-floor dining (sample detail — confirm with the venue).",
                    IdealDuration = "2 – 3 hours for the full brunch."
                }),

            new("Johnson Jakande Tinubu Park", 6.6119, 3.3576, 3m, 1, 5, 2, 3, 2, "Road",
                ["nature_outdoor"],
                ["nature", "relaxing", "family_friendly", "walkable", "photography"],
                ["JJT Park", "JJT Park Alausa", "Jakande Tinubu Park"],
                ["jjt-park-morning", "jjt-park-fountain"],
                new DescriptionJsonDto
                {
                    Overview = "A landscaped public park in Alausa, Ikeja, beside the Lagos State Secretariat — lawns, fountains and walking paths that make for an easy, green start to a mainland Sunday.",
                    Directions = "Location: Alausa, Ikeja, by the Lagos State Secretariat. Easiest by rideshare.",
                    WhatToKnow = "Go early for cooler temperatures. Check opening hours and any entry fee before visiting.",
                    ThingsToBeWaryOf = "Midday sun is strong — bring water and shade.",
                    LocalPerspective = "Popular with families and photographers, especially on weekends.",
                    HiddenCost = "Entry: small or free — check on arrival.",
                    NearbyComplements = new List<string> { "Kalakuta Republic Museum, Ikeja", "Solo House, Mangoro" },
                    BestTimeToVisit = "Early morning.",
                    CrowdLevel = "Moderate on weekends.",
                    Accessibility = "Mostly flat paved paths.",
                    IdealDuration = "45 minutes – 1 hour."
                }),

            new("Kalakuta Republic Museum", 6.5967, 3.3441, 5m, 2, 3, 2, 4, 3, "Road",
                ["museum_gallery", "historical_tour"],
                ["history", "cultural", "music", "educational", "local_favorite"],
                ["Kalakuta Museum", "Fela Museum"],
                ["kalakuta-museum"],
                new DescriptionJsonDto
                {
                    Overview = "The former home of Afrobeat pioneer Fela Anikulapo-Kuti on Gbemisola Street, Ikeja, preserved as a museum of his life, music and activism — personal rooms, stage costumes, instruments and a rooftop.",
                    Directions = "Location: Gbemisola Street, Ikeja. Easiest by rideshare.",
                    WhatToKnow = "Check opening hours and the entry fee ahead of your visit.",
                    ThingsToBeWaryOf = "Weekend traffic around Ikeja.",
                    LocalPerspective = "A must for music lovers and anyone curious about Lagos' cultural history.",
                    HiddenCost = "Entry fee: modest — confirm locally.",
                    NearbyComplements = new List<string> { "New Afrika Shrine, Ikeja", "Solo House, Mangoro" },
                    BestTimeToVisit = "Afternoon.",
                    CrowdLevel = "Low to moderate.",
                    Accessibility = "Multi-storey house with stairs.",
                    IdealDuration = "1 – 1.5 hours."
                }),

            new("New Afrika Shrine", 6.6060, 3.3520, 7m, 2, 2, 3, 4, 3, "Road",
                ["entertainment_nightlife"],
                ["nightlife", "music", "cultural", "social"],
                ["Afrika Shrine", "The Shrine Ikeja"],
                ["new-afrika-shrine-night"],
                new DescriptionJsonDto
                {
                    Overview = "The open-air Afrobeat venue in Ikeja that carries on Fela's legacy, known for live performances and big cultural nights.",
                    Directions = "Location: Agidingbi area, Ikeja. Easiest by rideshare.",
                    WhatToKnow = "Line-ups vary week to week — check the schedule before going.",
                    ThingsToBeWaryOf = "Can get loud and crowded on event nights; keep valuables secure.",
                    LocalPerspective = "A Lagos institution for live music.",
                    HiddenCost = "Entry varies by event.",
                    NearbyComplements = new List<string> { "Kalakuta Republic Museum, Ikeja" },
                    BestTimeToVisit = "Evening, on event nights.",
                    CrowdLevel = "High on event nights.",
                    Accessibility = "Open-air venue; uneven ground in places.",
                    IdealDuration = "2 – 3 hours."
                }),
        ];
    }
}
