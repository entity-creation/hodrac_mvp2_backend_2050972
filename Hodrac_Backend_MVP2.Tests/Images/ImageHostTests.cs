using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastucture.Seeder;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Images;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Tests.Images;

/// <summary>ImagesOptions, GET /api/config/images and the --rewrite-image-host rewriter.</summary>
[Collection(IntegrationCollection.Name)]
public class ImageHostTests
{
    private const string Vercel = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/";

    private readonly HodracApiFactory _factory;

    public ImageHostTests(HodracApiFactory factory) => _factory = factory;

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    // ─── Options ──────────────────────────────────────────────────────────────

    [Fact]
    public void Options_default_to_the_vercel_host_with_no_fallbacks()
    {
        var options = new ImagesOptions();

        options.PublicBaseUrl.Should().Be(Vercel);
        options.FallbackBaseUrls.Should().BeEmpty();
        options.NormalizedPublicBaseUrl.Should().Be(Vercel);
        options.NormalizedFallbackBaseUrls.Should().BeEmpty();
    }

    [Fact]
    public void Options_bind_from_the_images_section_and_normalize_trailing_slashes()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Images:PublicBaseUrl"] = "https://images.example-r2.test",
            ["Images:FallbackBaseUrls:0"] = Vercel.TrimEnd('/'),
            ["Images:FallbackBaseUrls:1"] = Vercel,              // duplicate once normalized
            ["Images:FallbackBaseUrls:2"] = "https://images.example-r2.test/", // the primary itself
        }).Build();

        var options = config.GetSection(ImagesOptions.SectionName).Get<ImagesOptions>()!;

        options.NormalizedPublicBaseUrl.Should().Be("https://images.example-r2.test/");
        options.NormalizedFallbackBaseUrls.Should().Equal(Vercel);
    }

    [Theory]
    [InlineData("images.example.test/")]
    [InlineData("/relative/")]
    [InlineData("ftp://images.example.test/")]
    [InlineData("")]
    public void NormalizeBaseUrl_rejects_anything_but_absolute_http_urls(string url)
    {
        var act = () => ImagesOptions.NormalizeBaseUrl(url);
        act.Should().Throw<ArgumentException>();
        ImagesOptions.IsAbsoluteHttpUrl(url).Should().BeFalse();
    }

    // ─── GET /api/config/images ───────────────────────────────────────────────

    [Fact]
    public async Task Config_endpoint_returns_the_defaults_anonymously()
    {
        var response = await _factory.CreateClient().GetAsync("/api/config/images");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("primaryBaseUrl").GetString().Should().Be(Vercel);
        json.GetProperty("fallbackBaseUrls").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Config_endpoint_returns_the_configured_hosts()
    {
        using var configured = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Images:PublicBaseUrl", "https://images.example-r2.test");
            b.UseSetting("Images:FallbackBaseUrls:0", Vercel);
        });

        try
        {
            var json = await configured.CreateClient().GetFromJsonAsync<JsonElement>("/api/config/images");

            json.GetProperty("primaryBaseUrl").GetString().Should().Be("https://images.example-r2.test/");
            json.GetProperty("fallbackBaseUrls").EnumerateArray().Select(e => e.GetString())
                .Should().Equal(Vercel);
            // Program.cs also points the seeders at the configured host.
            SeedImages.BaseUrl.Should().Be("https://images.example-r2.test/");
            SeedImages.LegacyBaseUrls.Should().Equal(Vercel);
        }
        finally
        {
            // Booting the second host reconfigured the static seeder hosts; put the defaults back.
            SeedImages.Configure(new ImagesOptions());
        }
    }

    [Fact]
    public void SeedImages_lists_a_file_under_the_current_and_every_legacy_host()
    {
        try
        {
            SeedImages.Configure(new ImagesOptions
            {
                PublicBaseUrl = "https://images.example-r2.test",
                FallbackBaseUrls = new[] { Vercel },
            });

            SeedImages.BaseUrl.Should().Be("https://images.example-r2.test/");
            SeedImages.UrlsUnderAllBases("calabar_afi.jpg").Should().Equal(
                "https://images.example-r2.test/calabar_afi.jpg", Vercel + "calabar_afi.jpg");
        }
        finally
        {
            SeedImages.Configure(new ImagesOptions());
        }

        SeedImages.BaseUrl.Should().Be(Vercel);
        SeedImages.UrlsUnderAllBases("x.jpg").Should().Equal(Vercel + "x.jpg");
    }

    // ─── ImageHostRewriter ────────────────────────────────────────────────────

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    private Task<RewriteReport> RunAsync(string from, string to, bool apply) => WithDbAsync(db =>
        new ImageHostRewriter(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<ImageHostRewriter>.Instance)
            .RunAsync(from, to, apply));

    private record SeededIds(
        Guid CountryId, Guid DestinationImageId, Guid DecoyImageId, Guid NearMissImageId, Guid WishlistId, Guid DecoyWishlistId,
        Guid ItineraryItemId, Guid CreatorId, string UserId, Guid PartnerId, Guid HostProfileId, Guid EventId,
        Guid BlogPostId, Guid TripPostId);

    /// <summary>
    /// One row per image column whose URL starts with <paramref name="oldBase"/>, plus decoys that
    /// must never change: a picsum URL, a URL that only contains the old base mid-string, and one
    /// that would match if "_" in the base were treated as a LIKE wildcard.
    /// </summary>
    private Task<SeededIds> SeedAsync(string oldBase, string tag) => WithDbAsync(async db =>
    {
        var country = new Country { CountryId = Guid.NewGuid(), CountryName = $"Imageland {tag}", GlobalHeroImage = oldBase + $"country_{tag}.jpg" };
        var city = new City { CityId = Guid.NewGuid(), CityName = $"Image City {tag}", CountryId = country.CountryId };
        var name = $"Image Spot {tag}";
        var destination = new Destination
        {
            DestinationId = Guid.NewGuid(),
            DestinationName = name,
            CleanNormalizedSearchName = DestinationSearchFields.Normalize(name),
            CountryId = country.CountryId,
            TimeZone = "Africa/Lagos",
        };
        var image = new DestinationImage
        {
            DestinationImageId = Guid.NewGuid(), DestinationId = destination.DestinationId, ImageType = "Hero",
            ImageUrl = oldBase + $"dest_{tag}.jpg", ThumbnailUrl = oldBase + $"dest_{tag}_thumb.jpg",
        };
        var decoyImage = new DestinationImage
        {
            DestinationImageId = Guid.NewGuid(), DestinationId = destination.DestinationId, ImageType = "Gallery", DisplayOrder = 1,
            ImageUrl = $"https://picsum.photos/seed/{tag}/800/600",
            ThumbnailUrl = $"https://img.example.test/proxy?u={oldBase}dest_{tag}.jpg",
        };
        // "old_x" in the base: "oldXx" would match an unescaped LIKE pattern.
        var nearMissImage = new DestinationImage
        {
            DestinationImageId = Guid.NewGuid(), DestinationId = destination.DestinationId, ImageType = "Gallery", DisplayOrder = 2,
            ImageUrl = oldBase.Replace("old_", "oldX") + $"near_{tag}.jpg",
            ThumbnailUrl = oldBase.TrimEnd('/') + $"-other/near_{tag}.jpg",
        };

        var wishlist = new Wishlist { WishlistId = Guid.NewGuid(), WishlistName = $"Image Trip {tag}", WishlistHeroImage = oldBase + $"trip_{tag}.jpg" };
        var decoyWishlist = new Wishlist { WishlistId = Guid.NewGuid(), WishlistName = $"Image Trip Decoy {tag}", WishlistHeroImage = $"https://picsum.photos/seed/{tag}-trip/800/600" };
        var day = new ItineraryDay { ItineraryDayId = Guid.NewGuid(), DayNumber = 1, DayTitle = "Day 1", WishlistId = wishlist.WishlistId };
        var item = new ItineraryItem { ItineraryItemId = Guid.NewGuid(), ItemTitle = "Visit", ItineraryDayId = day.ItineraryDayId, ImageUrl = oldBase + $"item_{tag}.jpg" };

        var creator = new Creator
        {
            CreatorId = Guid.NewGuid(), DisplayName = $"Creator {tag}", Handle = $"creator_{tag}", PlatformName = "Instagram",
            ProfileUrl = $"https://instagram.test/creator_{tag}", AvatarUrl = oldBase + $"creator_{tag}.jpg",
        };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(), UserName = $"img-{tag}@hodrac.test", NormalizedUserName = $"IMG-{tag}@HODRAC.TEST".ToUpperInvariant(),
            Email = $"img-{tag}@hodrac.test", NormalizedEmail = $"IMG-{tag}@HODRAC.TEST".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(), AvatarUrl = oldBase + $"user_{tag}.jpg",
        };
        var partner = new Partner { Name = $"Partner {tag}", LogoUrl = oldBase + $"partner_{tag}.png" };
        var host = new HostProfile { UserId = Guid.NewGuid(), DisplayName = $"Host {tag}", Slug = $"host-{tag}", LogoUrl = oldBase + $"host_{tag}.png" };
        var ev = new Event
        {
            Id = Guid.NewGuid(), Name = "Image Fest", Slug = $"image-fest-{tag}", StartsAt = DateTime.UtcNow.AddDays(10),
            CityId = city.CityId, VenueName = "The Yard", ImageUrl = oldBase + $"event_{tag}.jpg",
        };
        var post = new BlogPost
        {
            Id = Guid.NewGuid(), Title = $"Images {tag}", Slug = $"images-{tag}", Excerpt = "x", BodyMarkdown = "x",
            CreatedByUserId = Guid.NewGuid(), CoverImageUrl = oldBase + $"blog_{tag}.jpg",
        };
        var trip = new TripPost { AuthorUserId = Guid.NewGuid(), Caption = $"Trip {tag}", CoverImageUrl = oldBase + $"trippost_{tag}.jpg" };

        db.AddRange(country, city, destination, image, decoyImage, nearMissImage, wishlist, decoyWishlist, day, item,
            creator, user, partner, host, ev, post, trip);
        await db.SaveChangesAsync();

        return new SeededIds(country.CountryId, image.DestinationImageId, decoyImage.DestinationImageId, nearMissImage.DestinationImageId,
            wishlist.WishlistId, decoyWishlist.WishlistId, item.ItineraryItemId, creator.CreatorId, user.Id, partner.PartnerId,
            host.Id, ev.Id, post.Id, trip.Id);
    });

    /// <summary>Every seeded image URL, keyed "Table.Column" (decoys keyed separately).</summary>
    private Task<Dictionary<string, string?>> ReadAsync(SeededIds ids) => WithDbAsync(async db =>
    {
        var image = await db.DestinationImages.AsNoTracking().SingleAsync(i => i.DestinationImageId == ids.DestinationImageId);
        var decoy = await db.DestinationImages.AsNoTracking().SingleAsync(i => i.DestinationImageId == ids.DecoyImageId);
        var nearMiss = await db.DestinationImages.AsNoTracking().SingleAsync(i => i.DestinationImageId == ids.NearMissImageId);
        return new Dictionary<string, string?>
        {
            ["DestinationImages.ImageUrl"] = image.ImageUrl,
            ["DestinationImages.ThumbnailUrl"] = image.ThumbnailUrl,
            ["Countries.GlobalHeroImage"] = (await db.Countries.AsNoTracking().SingleAsync(c => c.CountryId == ids.CountryId)).GlobalHeroImage,
            ["Wishlists.WishlistHeroImage"] = (await db.Wishlists.AsNoTracking().SingleAsync(w => w.WishlistId == ids.WishlistId)).WishlistHeroImage,
            ["ItineraryItems.ImageUrl"] = (await db.ItineraryItems.AsNoTracking().SingleAsync(i => i.ItineraryItemId == ids.ItineraryItemId)).ImageUrl,
            ["Creators.AvatarUrl"] = (await db.Creators.AsNoTracking().SingleAsync(c => c.CreatorId == ids.CreatorId)).AvatarUrl,
            ["AspNetUsers.AvatarUrl"] = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == ids.UserId)).AvatarUrl,
            ["Partners.LogoUrl"] = (await db.Partners.AsNoTracking().SingleAsync(p => p.PartnerId == ids.PartnerId)).LogoUrl,
            ["HostProfiles.LogoUrl"] = (await db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == ids.HostProfileId)).LogoUrl,
            ["Events.ImageUrl"] = (await db.Events.AsNoTracking().SingleAsync(e => e.Id == ids.EventId)).ImageUrl,
            ["BlogPosts.CoverImageUrl"] = (await db.BlogPosts.AsNoTracking().SingleAsync(b => b.Id == ids.BlogPostId)).CoverImageUrl,
            ["TripPosts.CoverImageUrl"] = (await db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == ids.TripPostId)).CoverImageUrl,
            ["decoy.picsum"] = decoy.ImageUrl,
            ["decoy.midString"] = decoy.ThumbnailUrl,
            ["decoy.likeWildcard"] = nearMiss.ImageUrl,
            ["decoy.longerPath"] = nearMiss.ThumbnailUrl,
            ["decoy.wishlist"] = (await db.Wishlists.AsNoTracking().SingleAsync(w => w.WishlistId == ids.DecoyWishlistId)).WishlistHeroImage,
        };
    });

    private static IEnumerable<string> ColumnKeys =>
        ImageHostRewriter.ImageColumns.Select(c => $"{c.Table}.{c.Column}");

    [Fact]
    public void Rewriter_covers_every_image_column()
    {
        ColumnKeys.Should().BeEquivalentTo(
            "DestinationImages.ImageUrl", "DestinationImages.ThumbnailUrl", "Countries.GlobalHeroImage",
            "Wishlists.WishlistHeroImage", "ItineraryItems.ImageUrl", "Creators.AvatarUrl", "AspNetUsers.AvatarUrl",
            "Partners.LogoUrl", "HostProfiles.LogoUrl", "Events.ImageUrl", "BlogPosts.CoverImageUrl", "TripPosts.CoverImageUrl");
    }

    [Fact]
    public async Task Dry_run_counts_matches_in_every_column_and_changes_nothing()
    {
        var tag = Unique();
        var oldBase = $"https://cdn-{tag}.test/old_{tag}/";
        var newBase = $"https://r2-{tag}.test/";
        var ids = await SeedAsync(oldBase, tag);
        var before = await ReadAsync(ids);

        var report = await RunAsync(oldBase, newBase, apply: false);

        report.Applied.Should().BeFalse();
        report.Columns.Select(c => $"{c.Table}.{c.Column}").Should().Equal(ColumnKeys);
        report.Columns.Should().OnlyContain(c => c.Matched == 1 && c.Updated == 0);
        report.TotalMatched.Should().Be(ImageHostRewriter.ImageColumns.Count);
        report.TotalUpdated.Should().Be(0);
        report.Columns.Single(c => c.Table == "Events").Samples.Should()
            .Equal($"{oldBase}event_{tag}.jpg -> {newBase}event_{tag}.jpg");

        (await ReadAsync(ids)).Should().Equal(before);
    }

    [Fact]
    public async Task Apply_rewrites_only_prefix_matches_is_idempotent_and_reverses()
    {
        var tag = Unique();
        var oldBase = $"https://cdn-{tag}.test/old_{tag}/";
        var newBase = $"https://r2-{tag}.test/";
        var ids = await SeedAsync(oldBase, tag);
        var before = await ReadAsync(ids);

        // Bases without a trailing slash are normalized, so no "//" appears.
        var report = await RunAsync(oldBase.TrimEnd('/'), newBase.TrimEnd('/'), apply: true);

        report.Applied.Should().BeTrue();
        report.Columns.Should().OnlyContain(c => c.Matched == 1 && c.Updated == 1);
        var after = await ReadAsync(ids);
        foreach (var key in ColumnKeys)
            after[key].Should().Be(newBase + before[key]![oldBase.Length..], key);
        foreach (var key in before.Keys.Where(k => k.StartsWith("decoy.")))
            after[key].Should().Be(before[key], key);

        var second = await RunAsync(oldBase, newBase, apply: true);
        second.TotalMatched.Should().Be(0);
        second.TotalUpdated.Should().Be(0);
        (await ReadAsync(ids)).Should().Equal(after);

        var reverse = await RunAsync(newBase, oldBase, apply: true);
        reverse.TotalUpdated.Should().Be(ImageHostRewriter.ImageColumns.Count);
        (await ReadAsync(ids)).Should().Equal(before);
    }

    [Theory]
    [InlineData("https://same.test/", "https://same.test/")]
    [InlineData("https://same.test", "https://same.test/")]
    [InlineData("https://a.test/", "https://a.test/sub/")]
    [InlineData("not a url", "https://a.test/")]
    [InlineData("https://a.test/", "/relative/")]
    public async Task Rewriter_refuses_bad_or_identical_bases(string from, string to)
    {
        var act = () => RunAsync(from, to, apply: true);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Rewriter_is_registered_in_di()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ImageHostRewriter>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IOptions<ImagesOptions>>().Value.NormalizedPublicBaseUrl.Should().Be(Vercel);
        await Task.CompletedTask;
    }
}
