using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastucture.Seeder;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Images;
using Hodrac_Backend_MVP2.Tests.Hosts;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Tests.Images;

/// <summary>
/// ImageUrlNormalizer, and every endpoint that saves an image URL storing it on the primary
/// host when an admin pastes a link to an old (fallback) host.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ImageUrlNormalizationTests : IDisposable
{
    private const string Vercel = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/";
    private const string R2 = "https://images.example-r2.test/";

    private readonly WebApplicationFactory<Program> _app;

    public ImageUrlNormalizationTests(HodracApiFactory factory)
    {
        // Same database and test settings as the shared factory, with R2 primary + Vercel fallback.
        _app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Images:PublicBaseUrl", R2);
            b.UseSetting("Images:FallbackBaseUrls:0", Vercel);
        });
    }

    public void Dispose()
    {
        _app.Dispose();
        // Booting the configured host pointed the static seeder hosts at R2; put the defaults back.
        SeedImages.Configure(new ImagesOptions());
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static ImageUrlNormalizer Normalizer(string primary = R2, params string[] fallbacks) =>
        new(Options.Create(new ImagesOptions
        {
            PublicBaseUrl = primary,
            FallbackBaseUrls = fallbacks.Length == 0 ? new[] { Vercel } : fallbacks,
        }));

    // ─── Normalizer ───────────────────────────────────────────────────────────

    [Fact]
    public void Fallback_prefix_is_swapped_for_the_primary_host()
    {
        Normalizer().Normalize(Vercel + "calabar/afi_ridge.jpg").Should().Be(R2 + "calabar/afi_ridge.jpg");
    }

    [Fact]
    public void Value_is_trimmed_before_matching()
    {
        Normalizer().Normalize("  " + Vercel + "x.jpg \n").Should().Be(R2 + "x.jpg");
        Normalizer().Normalize("  https://picsum.photos/800  ").Should().Be("https://picsum.photos/800");
    }

    [Theory]
    [InlineData("https://picsum.photos/seed/x/800/600")]
    [InlineData("https://images.example-r2.test/already.jpg")]
    [InlineData("/relative/path.jpg")]
    [InlineData("not a url")]
    // Same host but a longer one (no trailing "/" match).
    [InlineData("https://wangq4yhmf94epv8.public.blob.vercel-storage.com.evil.test/x.jpg")]
    public void Non_matching_urls_are_left_alone(string url)
    {
        Normalizer().Normalize(url).Should().Be(url);
    }

    [Fact]
    public void Old_host_appearing_mid_string_is_left_alone()
    {
        var url = $"https://img.example.test/proxy?u={Vercel}x.jpg";
        Normalizer().Normalize(url).Should().Be(url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_and_blank_pass_through_unchanged(string? url)
    {
        Normalizer().Normalize(url).Should().Be(url);
    }

    [Fact]
    public void Scheme_and_host_compare_case_insensitively()
    {
        Normalizer().Normalize("HTTPS://WANGQ4YHMF94EPV8.Public.Blob.Vercel-Storage.com/Photo.JPG")
            .Should().Be(R2 + "Photo.JPG");
    }

    [Fact]
    public void A_path_in_the_fallback_base_matches_exactly()
    {
        var normalizer = Normalizer(R2, "https://old.example.test/Images/");
        normalizer.Normalize("https://OLD.example.test/Images/a.jpg").Should().Be(R2 + "a.jpg");
        normalizer.Normalize("https://old.example.test/images/a.jpg").Should().Be("https://old.example.test/images/a.jpg");
    }

    [Fact]
    public void Defaults_have_no_fallbacks_so_only_trimming_happens()
    {
        var normalizer = new ImageUrlNormalizer(Options.Create(new ImagesOptions()));
        normalizer.Normalize(" " + Vercel + "x.jpg ").Should().Be(Vercel + "x.jpg");
    }

    [Theory]
    [InlineData("https://img.example.test/a.jpg", true)]
    [InlineData("http://img.example.test/a.jpg", true)]
    [InlineData("/a.jpg", false)]
    [InlineData("img.example.test/a.jpg", false)]
    [InlineData("ftp://img.example.test/a.jpg", false)]
    public void IsValidImageUrl_accepts_only_absolute_http_urls(string url, bool valid)
    {
        Normalizer().IsValidImageUrl(url).Should().Be(valid);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> AdminAsync()
    {
        var user = await AuthHelper.GetOrRegisterAsync(_app.CreateClient(), HodracApiFactory.AdminEmail);
        return _app.CreateClient().Authenticated(user);
    }

    private async Task<(HttpClient Client, TestUser User)> UserAsync()
    {
        var user = await AuthHelper.RegisterAsync(_app.CreateClient());
        return (_app.CreateClient().Authenticated(user), user);
    }

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    private static async Task<string> ExpectAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        return body;
    }

    /// <summary>Status 400, and the validation-problem "errors" (or plain message) names the field.</summary>
    private static async Task ExpectBadRequestAsync(HttpResponseMessage response, string field)
    {
        var body = await ExpectAsync(response, HttpStatusCode.BadRequest);
        body.Should().Contain(field);
    }

    private Task<Guid> SeedDestinationAsync() => WithDbAsync(async db =>
    {
        var tag = Unique();
        var country = new Country { CountryId = Guid.NewGuid(), CountryName = $"Normland {tag}" };
        var destination = new Destination
        {
            DestinationId = Guid.NewGuid(),
            DestinationName = $"Norm Spot {tag}",
            CountryId = country.CountryId,
            TimeZone = "Africa/Lagos",
        };
        db.AddRange(country, destination);
        await db.SaveChangesAsync();
        return destination.DestinationId;
    });

    private Task<Guid> SeedCityAsync() => WithDbAsync(async db =>
    {
        var tag = Unique();
        var country = new Country { CountryId = Guid.NewGuid(), CountryName = $"Normcity {tag}" };
        var city = new City { CityId = Guid.NewGuid(), CityName = $"Norm City {tag}", CountryId = country.CountryId };
        db.AddRange(country, city);
        await db.SaveChangesAsync();
        return city.CityId;
    });

    private Task<Guid> SeedDayAsync(Guid wishlistId) => WithDbAsync(async db =>
    {
        var day = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Day 1", WishlistId = wishlistId };
        db.ItineraryDays.Add(day);
        await db.SaveChangesAsync();
        return day.ItineraryDayId;
    });

    /// <summary>A user-owned (non-template) wishlist with one day, the user as Owner.</summary>
    private Task<(Guid WishlistId, Guid DayId)> SeedUserWishlistAsync(TestUser user) => WithDbAsync(async db =>
    {
        var userId = Guid.Parse(user.UserId);
        var wishlist = new Wishlist
        {
            WishlistId = Guid.NewGuid(), WishlistName = $"My Trip {Unique()}", IsTemplate = false, OwnerUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var day = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Day 1", WishlistId = wishlist.WishlistId };
        db.AddRange(wishlist, day,
            new WishlistCollaborator { WishlistId = wishlist.WishlistId, UserId = userId, SharedUserEmail = user.Email, Role = "Owner" });
        await db.SaveChangesAsync();
        return (wishlist.WishlistId, day.ItineraryDayId);
    });

    private async Task<Guid> CreateTemplateAsync(HttpClient admin)
    {
        var body = await ExpectAsync(await admin.PostAsJsonAsync("/api/admin/wishlists",
            new { wishlistName = $"Norm Template {Unique()}", totalDays = 1 }), HttpStatusCode.Created);
        return System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("wishlistId").GetGuid();
    }

    private Task<string> ItemImageAsync(Guid dayId) => WithDbAsync(db =>
        db.ItineraryItems.AsNoTracking().Where(i => i.ItineraryDayId == dayId).Select(i => i.ImageUrl).SingleAsync());

    // ─── Endpoints ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_destination_image_create_and_update_store_the_primary_host()
    {
        var admin = await AdminAsync();
        var destinationId = await SeedDestinationAsync();

        var created = await ExpectAsync(await admin.PostAsJsonAsync($"/api/admin/destinations/{destinationId}/images",
            new { imageUrl = Vercel + "dest.jpg", thumbnailUrl = Vercel + "dest_thumb.jpg", imageType = "Hero" }), HttpStatusCode.Created);
        created.Should().Contain(R2 + "dest.jpg").And.Contain(R2 + "dest_thumb.jpg").And.NotContain(Vercel);

        var imageId = System.Text.Json.JsonDocument.Parse(created).RootElement.GetProperty("destinationImageId").GetGuid();
        var updated = await ExpectAsync(await admin.PatchAsJsonAsync($"/api/admin/destinations/{destinationId}/images/{imageId}",
            new { imageUrl = Vercel + "dest2.jpg", thumbnailUrl = Vercel + "dest2_thumb.jpg" }), HttpStatusCode.OK);
        updated.Should().Contain(R2 + "dest2.jpg").And.NotContain(Vercel);

        var stored = await WithDbAsync(db => db.DestinationImages.AsNoTracking().SingleAsync(i => i.DestinationImageId == imageId));
        stored.ImageUrl.Should().Be(R2 + "dest2.jpg");
        stored.ThumbnailUrl.Should().Be(R2 + "dest2_thumb.jpg");
    }

    [Fact]
    public async Task Country_hero_create_and_update_store_the_primary_host()
    {
        var admin = await AdminAsync();

        var created = await ExpectAsync(await admin.PostAsJsonAsync("/api/admin/countries",
            new { countryName = $"Heroland {Unique()}", globalHeroImage = Vercel + "country.jpg" }), HttpStatusCode.Created);
        created.Should().Contain(R2 + "country.jpg").And.NotContain(Vercel);
        var countryId = System.Text.Json.JsonDocument.Parse(created).RootElement.GetProperty("countryId").GetGuid();

        await ExpectAsync(await admin.PatchAsJsonAsync($"/api/admin/countries/{countryId}",
            new { globalHeroImage = Vercel + "country2.jpg" }), HttpStatusCode.OK);

        (await WithDbAsync(db => db.Countries.AsNoTracking().Where(c => c.CountryId == countryId).Select(c => c.GlobalHeroImage).SingleAsync()))
            .Should().Be(R2 + "country2.jpg");
    }

    [Fact]
    public async Task Wishlist_hero_create_and_update_store_the_primary_host_and_reject_relative_paths()
    {
        var admin = await AdminAsync();

        var created = await ExpectAsync(await admin.PostAsJsonAsync("/api/admin/wishlists",
            new { wishlistName = $"Hero Trip {Unique()}", totalDays = 2, wishlistHeroImage = Vercel + "trip.jpg" }), HttpStatusCode.Created);
        created.Should().Contain(R2 + "trip.jpg").And.NotContain(Vercel);
        var wishlistId = System.Text.Json.JsonDocument.Parse(created).RootElement.GetProperty("wishlistId").GetGuid();

        await ExpectAsync(await admin.PatchAsJsonAsync($"/api/admin/wishlists/{wishlistId}",
            new { wishlistHeroImage = Vercel + "trip2.jpg" }), HttpStatusCode.OK);
        Task<string> Stored() => WithDbAsync(db => db.Wishlists.AsNoTracking().Where(w => w.WishlistId == wishlistId).Select(w => w.WishlistHeroImage).SingleAsync());
        (await Stored()).Should().Be(R2 + "trip2.jpg");

        await ExpectBadRequestAsync(await admin.PostAsJsonAsync("/api/admin/wishlists",
            new { wishlistName = $"Bad Hero {Unique()}", totalDays = 1, wishlistHeroImage = "/images/trip.jpg" }), "WishlistHeroImage");
        await ExpectBadRequestAsync(await admin.PatchAsJsonAsync($"/api/admin/wishlists/{wishlistId}",
            new { wishlistHeroImage = "trip.jpg" }), "WishlistHeroImage");
        (await Stored()).Should().Be(R2 + "trip2.jpg");

        // Blank still clears.
        await ExpectAsync(await admin.PatchAsJsonAsync($"/api/admin/wishlists/{wishlistId}", new { wishlistHeroImage = "" }), HttpStatusCode.OK);
        (await Stored()).Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_itinerary_item_create_and_update_store_the_primary_host_and_reject_relative_paths()
    {
        var admin = await AdminAsync();
        var wishlistId = await CreateTemplateAsync(admin);
        var dayId = await SeedDayAsync(wishlistId);
        var itemsUrl = $"/api/admin/wishlists/{wishlistId}/itinerary/days/{dayId}/items";

        await ExpectBadRequestAsync(await admin.PostAsJsonAsync(itemsUrl, new { title = "Hike", imageUrl = "/img/hike.jpg" }), "ImageUrl");

        await ExpectAsync(await admin.PostAsJsonAsync(itemsUrl, new { title = "Hike", imageUrl = Vercel + "hike.jpg" }), HttpStatusCode.OK);
        (await ItemImageAsync(dayId)).Should().Be(R2 + "hike.jpg");

        var itemId = await WithDbAsync(db => db.ItineraryItems.Where(i => i.ItineraryDayId == dayId).Select(i => i.ItineraryItemId).SingleAsync());
        await ExpectAsync(await admin.PutAsJsonAsync($"{itemsUrl}/{itemId}", new { imageUrl = Vercel + "hike2.jpg" }), HttpStatusCode.OK);
        (await ItemImageAsync(dayId)).Should().Be(R2 + "hike2.jpg");

        await ExpectBadRequestAsync(await admin.PutAsJsonAsync($"{itemsUrl}/{itemId}", new { imageUrl = "hike.jpg" }), "ImageUrl");
        (await ItemImageAsync(dayId)).Should().Be(R2 + "hike2.jpg");
    }

    [Fact]
    public async Task User_itinerary_item_create_and_update_store_the_primary_host_and_reject_relative_paths()
    {
        var (client, user) = await UserAsync();
        var (wishlistId, dayId) = await SeedUserWishlistAsync(user);
        var itemsUrl = $"/api/user-wishlists/{wishlistId}/itinerary/days/{dayId}/items";

        await ExpectBadRequestAsync(await client.PostAsJsonAsync(itemsUrl, new { title = "Swim", imageUrl = "/img/swim.jpg" }), "ImageUrl");

        var created = await ExpectAsync(await client.PostAsJsonAsync(itemsUrl, new { title = "Swim", imageUrl = Vercel + "swim.jpg" }), HttpStatusCode.OK);
        created.Should().Contain(R2 + "swim.jpg").And.NotContain(Vercel);
        (await ItemImageAsync(dayId)).Should().Be(R2 + "swim.jpg");

        var itemId = await WithDbAsync(db => db.ItineraryItems.Where(i => i.ItineraryDayId == dayId).Select(i => i.ItineraryItemId).SingleAsync());
        await ExpectAsync(await client.PutAsJsonAsync($"{itemsUrl}/{itemId}", new { imageUrl = Vercel + "swim2.jpg" }), HttpStatusCode.OK);
        (await ItemImageAsync(dayId)).Should().Be(R2 + "swim2.jpg");

        await ExpectBadRequestAsync(await client.PutAsJsonAsync($"{itemsUrl}/{itemId}", new { imageUrl = "swim.jpg" }), "ImageUrl");
        (await ItemImageAsync(dayId)).Should().Be(R2 + "swim2.jpg");
    }

    [Fact]
    public async Task Event_image_create_and_update_store_the_primary_host()
    {
        var admin = await AdminAsync();
        var cityId = await SeedCityAsync();

        var created = await ExpectAsync(await admin.PostAsJsonAsync("/api/admin/events", new Dictionary<string, object?>
        {
            ["name"] = $"Norm Fest {Unique()}",
            ["startsAt"] = DateTimeOffset.UtcNow.AddDays(20),
            ["cityId"] = cityId,
            ["imageUrl"] = Vercel + "event.jpg",
        }), HttpStatusCode.Created);
        created.Should().Contain(R2 + "event.jpg").And.NotContain(Vercel);
        var eventId = System.Text.Json.JsonDocument.Parse(created).RootElement.GetProperty("id").GetGuid();

        await ExpectAsync(await admin.PatchAsJsonAsync($"/api/admin/events/{eventId}", new { imageUrl = Vercel + "event2.jpg" }), HttpStatusCode.OK);
        (await WithDbAsync(db => db.Events.AsNoTracking().Where(e => e.Id == eventId).Select(e => e.ImageUrl).SingleAsync()))
            .Should().Be(R2 + "event2.jpg");
    }

    [Fact]
    public async Task Blog_cover_create_and_update_store_the_primary_host()
    {
        var admin = await AdminAsync();

        var created = await ExpectAsync(await admin.PostAsJsonAsync("/api/admin/blog",
            new { title = $"Norm Post {Unique()}", coverImageUrl = Vercel + "cover.jpg" }), HttpStatusCode.Created);
        created.Should().Contain(R2 + "cover.jpg").And.NotContain(Vercel);
        var postId = System.Text.Json.JsonDocument.Parse(created).RootElement.GetProperty("id").GetGuid();

        await ExpectAsync(await admin.PatchAsJsonAsync($"/api/admin/blog/{postId}", new { coverImageUrl = Vercel + "cover2.jpg" }), HttpStatusCode.OK);
        (await WithDbAsync(db => db.BlogPosts.AsNoTracking().Where(p => p.Id == postId).Select(p => p.CoverImageUrl).SingleAsync()))
            .Should().Be(R2 + "cover2.jpg");
    }

    [Fact]
    public async Task Host_logo_create_and_update_store_the_primary_host()
    {
        var (client, user) = await UserAsync();

        var created = await ExpectAsync(await client.PostAsJsonAsync("/api/host/profile",
            new { displayName = $"Norm Host {Unique()}", logoUrl = Vercel + "logo.png" }), HttpStatusCode.Created);
        created.Should().Contain(R2 + "logo.png").And.NotContain(Vercel);

        await ExpectAsync(await client.PatchAsJsonAsync("/api/host/profile/me", new { logoUrl = Vercel + "logo2.png" }), HttpStatusCode.OK);
        var userId = Guid.Parse(user.UserId);
        (await WithDbAsync(db => db.HostProfiles.AsNoTracking().Where(h => h.UserId == userId).Select(h => h.LogoUrl).SingleAsync()))
            .Should().Be(R2 + "logo2.png");
    }

    [Fact]
    public async Task Partner_logo_create_and_update_store_the_primary_host()
    {
        var admin = await AdminAsync();

        var created = await ExpectAsync(await admin.PostAsJsonAsync("/api/admin/partners",
            new { name = $"Norm Partner {Unique()}", logoUrl = Vercel + "partner.png", isActive = true }), HttpStatusCode.Created);
        created.Should().Contain(R2 + "partner.png").And.NotContain(Vercel);
        var partnerId = System.Text.Json.JsonDocument.Parse(created).RootElement.GetProperty("partnerId").GetGuid();

        await ExpectAsync(await admin.PatchAsJsonAsync($"/api/admin/partners/{partnerId}", new { logoUrl = Vercel + "partner2.png" }), HttpStatusCode.OK);
        (await WithDbAsync(db => db.Partners.AsNoTracking().Where(p => p.PartnerId == partnerId).Select(p => p.LogoUrl).SingleAsync()))
            .Should().Be(R2 + "partner2.png");
    }

    [Fact]
    public async Task User_avatar_stores_the_primary_host_and_rejects_relative_paths()
    {
        var (client, user) = await UserAsync();
        Task<string> Stored() => WithDbAsync(db => db.Users.AsNoTracking().Where(u => u.Id == user.UserId).Select(u => u.AvatarUrl).SingleAsync());

        var body = await ExpectAsync(await client.PatchAsJsonAsync("/api/auth/me", new { avatarUrl = Vercel + "me.jpg" }), HttpStatusCode.OK);
        body.Should().Contain(R2 + "me.jpg").And.NotContain(Vercel);
        (await Stored()).Should().Be(R2 + "me.jpg");

        await ExpectBadRequestAsync(await client.PatchAsJsonAsync("/api/auth/me", new { avatarUrl = "/avatars/me.jpg" }), "AvatarUrl");
        (await Stored()).Should().Be(R2 + "me.jpg");

        // Blank still clears.
        await ExpectAsync(await client.PatchAsJsonAsync("/api/auth/me", new { avatarUrl = "" }), HttpStatusCode.OK);
        (await Stored()).Should().BeEmpty();
    }

    [Fact]
    public async Task Creator_avatar_stores_the_primary_host_and_rejects_relative_paths()
    {
        var admin = await AdminAsync();
        var tag = Unique();
        object Body(string? avatarUrl) => new
        {
            displayName = $"Creator {tag}", handle = $"norm_{tag}", platformName = "Instagram",
            profileUrl = $"https://instagram.test/norm_{tag}", avatarUrl,
        };

        await ExpectBadRequestAsync(await admin.PostAsJsonAsync("/api/creators", Body("avatar.jpg")), "AvatarUrl");

        var created = await ExpectAsync(await admin.PostAsJsonAsync("/api/creators", Body(Vercel + "creator.jpg")), HttpStatusCode.Created);
        created.Should().Contain(R2 + "creator.jpg").And.NotContain(Vercel);
        var creatorId = System.Text.Json.JsonDocument.Parse(created).RootElement.GetProperty("creatorId").GetGuid();

        await ExpectAsync(await admin.PatchAsJsonAsync($"/api/creators/{creatorId}", new { avatarUrl = Vercel + "creator2.jpg" }), HttpStatusCode.OK);
        Task<string?> Stored() => WithDbAsync(db => db.Creators.AsNoTracking().Where(c => c.CreatorId == creatorId).Select(c => c.AvatarUrl).SingleAsync());
        (await Stored()).Should().Be(R2 + "creator2.jpg");

        await ExpectBadRequestAsync(await admin.PatchAsJsonAsync($"/api/creators/{creatorId}", new { avatarUrl = "/c.jpg" }), "AvatarUrl");
        (await Stored()).Should().Be(R2 + "creator2.jpg");
    }

    [Fact]
    public async Task Urls_on_other_hosts_are_stored_as_sent()
    {
        var admin = await AdminAsync();
        var created = await ExpectAsync(await admin.PostAsJsonAsync("/api/admin/countries",
            new { countryName = $"Otherland {Unique()}", globalHeroImage = "https://picsum.photos/seed/x/800/600" }), HttpStatusCode.Created);
        created.Should().Contain("https://picsum.photos/seed/x/800/600");
    }
}
