using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Tests.Hosts;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hodrac_Backend_MVP2.Tests.Blog;

/// <summary>/api/blog (public), /api/admin/blog, embeds, related posts, destination/wishlist detail relatedPosts.</summary>
[Collection(IntegrationCollection.Name)]
public class BlogTests
{
    private readonly HodracApiFactory _factory;

    public BlogTests(HodracApiFactory factory) => _factory = factory;

    private record Place(Guid CountryId, Guid CityId, Guid DestinationId, string DestinationName, string CityName);

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private async Task<HttpClient> AdminAsync()
    {
        var user = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        return _factory.CreateClient().Authenticated(user);
    }

    private HttpClient Anon(bool followRedirects = true)
        => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects });

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    /// <summary>A fresh country + city + destination (with a hero image and overview), unique per call.</summary>
    private Task<Place> CreatePlaceAsync(string? destinationName = null) => WithDbAsync(async db =>
    {
        var tag = Unique();
        var country = new Country { CountryId = Guid.NewGuid(), CountryName = $"Blogland {tag}" };
        var city = new City { CityId = Guid.NewGuid(), CityName = $"Blog City {tag}", CountryId = country.CountryId };
        var name = destinationName ?? $"Blog Spot {tag}";
        var destination = new Destination
        {
            DestinationId = Guid.NewGuid(),
            DestinationName = name,
            CleanNormalizedSearchName = DestinationSearchFields.Normalize(name),
            CountryId = country.CountryId,
            TimeZone = "Africa/Lagos",
            DescriptionJson = JsonSerializer.Serialize(new { overview = "A quiet beach with " + string.Join(' ', Enumerable.Repeat("soft white sand", 30)) }),
        };
        db.Countries.Add(country);
        db.Cities.Add(city);
        db.Destinations.Add(destination);
        db.DestinationCities.Add(new DestinationCity { DestinationId = destination.DestinationId, CityId = city.CityId });
        db.DestinationImages.Add(new DestinationImage
        {
            DestinationImageId = Guid.NewGuid(), DestinationId = destination.DestinationId,
            ImageUrl = "https://img.example.com/gallery.jpg", ImageType = "Gallery", DisplayOrder = 0,
        });
        db.DestinationImages.Add(new DestinationImage
        {
            DestinationImageId = Guid.NewGuid(), DestinationId = destination.DestinationId,
            ImageUrl = "https://img.example.com/hero.jpg", ImageType = "Hero", DisplayOrder = 1,
        });
        await db.SaveChangesAsync();
        return new Place(country.CountryId, city.CityId, destination.DestinationId, destination.DestinationName, city.CityName);
    });

    private Task<(Guid Id, string Key)> CreateTagAsync(string? name = null) => WithDbAsync(async db =>
    {
        var key = $"blog_{Unique()}";
        var tag = new Tag { TagId = Guid.NewGuid(), Key = key, TagName = name ?? $"Tag {key}" };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return (tag.TagId, key);
    });

    private Task<Guid> CreateWishlistAsync(bool isTemplate = true) => WithDbAsync(async db =>
    {
        var wishlist = new Wishlist
        {
            WishlistId = Guid.NewGuid(),
            WishlistName = $"Blog Trip {Unique()}",
            TotalDays = 5,
            BasePricePerPerson = 1234.50m,
            WishlistHeroImage = "https://img.example.com/trip.jpg",
            IsTemplate = isTemplate,
            OwnerUserId = isTemplate ? null : Guid.NewGuid(),
        };
        db.Wishlists.Add(wishlist);
        await db.SaveChangesAsync();
        return wishlist.WishlistId;
    });

    private Task<(Guid Id, string Slug)> CreateEventAsync(Guid cityId, bool isPublished = true, Guid? destinationId = null) => WithDbAsync(async db =>
    {
        var ev = new Event
        {
            Id = Guid.NewGuid(),
            Name = "Blog Fest",
            Slug = $"blog-fest-{Unique()}",
            StartsAt = DateTime.UtcNow.AddDays(10),
            CityId = cityId,
            DestinationId = destinationId,
            VenueName = "The Yard",
            PriceMinKobo = 0,
            PriceMaxKobo = 0,
            IsPublished = isPublished,
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return (ev.Id, ev.Slug);
    });

    private static async Task<JsonElement> CreateAsync(HttpClient admin, object body)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/blog", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    /// <summary>A published post (PublishedAt = now - <paramref name="ageMinutes"/>).</summary>
    private static Task<JsonElement> CreatePublishedAsync(
        HttpClient admin, string title, double ageMinutes = 1, Guid[]? destinationIds = null, Guid[]? wishlistIds = null,
        Guid[]? eventIds = null, string[]? tagKeys = null, Guid? cityId = null, string? body = null)
        => CreateAsync(admin, new
        {
            title,
            excerpt = $"Excerpt for {title}",
            bodyMarkdown = body ?? $"Body for {title}.",
            status = "Published",
            publishedAt = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes),
            destinationIds,
            wishlistIds,
            eventIds,
            tagKeys,
            cityId,
        });

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Slug(JsonElement e) => e.GetProperty("slug").GetString()!;

    private static List<Guid> Ids(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(Id).ToList();

    private static List<Guid> IdList(JsonElement array)
        => array.EnumerateArray().Select(Id).ToList();

    private static void ShouldHaveError(JsonElement problem, string field)
        => problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(problem.ToString());

    private static string Words(int count) => string.Join(' ', Enumerable.Range(0, count).Select(i => $"word{i}"));

    // ── Admin CRUD ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_creates_draft_with_slug_reading_time_place_and_ordered_links()
    {
        var place = await CreatePlaceAsync();
        var place2 = await CreatePlaceAsync();
        var tag = await CreateTagAsync("Beaches");
        var wishlist = await CreateWishlistAsync();
        var ev = await CreateEventAsync(place.CityId);
        var admin = await AdminAsync();

        var created = await CreateAsync(admin, new
        {
            title = "  Lagos After Dark: 10 Spots  ",
            excerpt = " Where to go. ",
            bodyMarkdown = Words(450) + "\n\n{{destination:" + place.DestinationId + "}}\n",
            coverImageUrl = "https://img.example.com/cover.jpg",
            coverImageAlt = "Night skyline",
            authorName = "Hodrac Team",
            metaTitle = "Lagos nightlife",
            metaDescription = "The best spots after dark.",
            canonicalUrl = "https://hodrac.com/blog/lagos-after-dark",
            cityId = place.CityId,
            tagKeys = new[] { tag.Key },
            destinationIds = new[] { place2.DestinationId, place.DestinationId, place2.DestinationId },
            wishlistIds = new[] { wishlist },
            eventIds = new[] { ev.Id },
        });

        created.GetProperty("title").GetString().Should().Be("Lagos After Dark: 10 Spots");
        Slug(created).Should().Be("lagos-after-dark-10-spots");
        created.GetProperty("excerpt").GetString().Should().Be("Where to go.");
        created.GetProperty("status").GetString().Should().Be("Draft");
        created.GetProperty("isVisible").GetBoolean().Should().BeFalse();
        created.GetProperty("publishedAt").ValueKind.Should().Be(JsonValueKind.Null);
        created.GetProperty("readingTimeMinutes").GetInt32().Should().Be(3);   // 450 words / 200 wpm
        created.GetProperty("createdByUserId").GetGuid().Should().NotBe(Guid.Empty);
        created.GetProperty("city").GetProperty("id").GetGuid().Should().Be(place.CityId);
        created.GetProperty("country").GetProperty("id").GetGuid().Should().Be(place.CountryId);   // defaulted from the city
        created.GetProperty("tagKeys").EnumerateArray().Select(t => t.GetString()).Should().Equal(tag.Key);
        created.GetProperty("tags")[0].GetProperty("name").GetString().Should().Be("Beaches");
        created.GetProperty("destinationIds").EnumerateArray().Select(x => x.GetGuid()).Should().Equal(place2.DestinationId, place.DestinationId);
        IdList(created.GetProperty("destinations")).Should().Equal(place2.DestinationId, place.DestinationId);
        IdList(created.GetProperty("wishlists")).Should().Equal(wishlist);
        IdList(created.GetProperty("events")).Should().Equal(ev.Id);
        created.GetProperty("embeds").GetArrayLength().Should().Be(1);

        var fetched = await (await admin.GetAsync($"/api/admin/blog/{Id(created)}")).ReadJsonAsync();
        Slug(fetched).Should().Be("lagos-after-dark-10-spots");

        var list = await (await admin.GetAsync($"/api/admin/blog?tag={tag.Key}")).ReadJsonAsync();
        Ids(list).Should().Equal(Id(created));
        list.GetProperty("items")[0].GetProperty("status").GetString().Should().Be("Draft");
        list.GetProperty("items")[0].GetProperty("isVisible").GetBoolean().Should().BeFalse();

        (await Anon().GetAsync($"/api/blog/{Slug(created)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Slugs_are_generated_unique_and_validated()
    {
        var admin = await AdminAsync();
        var title = $"Same Title {Unique()}";
        var expected = BlogQueries.BaseSlug(title);

        var first = await CreateAsync(admin, new { title });
        var second = await CreateAsync(admin, new { title });
        var third = await CreateAsync(admin, new { title = title.ToUpperInvariant() + "!" });
        Slug(first).Should().Be(expected);
        Slug(second).Should().Be(expected + "-2");
        Slug(third).Should().Be(expected + "-3");

        // Route words are never used as slugs.
        var reservedTitle = await CreateAsync(admin, new { title = "Tags" });
        Slug(reservedTitle).Should().NotBe("tags");

        var custom = await CreateAsync(admin, new { title = "Anything", slug = $"custom-{Unique()}" });
        custom.GetProperty("slug").GetString().Should().StartWith("custom-");

        async Task ExpectSlugError(string slug)
        {
            var response = await admin.PostAsJsonAsync("/api/admin/blog", new { title = "x", slug });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, slug);
            ShouldHaveError(await response.ReadJsonAsync(), "Slug");
        }

        await ExpectSlugError(Slug(first));            // taken
        await ExpectSlugError("Upper-Case");
        await ExpectSlugError("double--hyphen");
        await ExpectSlugError("-leading");
        await ExpectSlugError("spaces here");
        await ExpectSlugError("sitemap");
        await ExpectSlugError(new string('a', 221));
    }

    [Fact]
    public async Task Create_and_patch_validate_input()
    {
        var place = await CreatePlaceAsync();
        var otherPlace = await CreatePlaceAsync();
        var userWishlist = await CreateWishlistAsync(isTemplate: false);
        var admin = await AdminAsync();

        async Task ExpectErrors(object body, params string[] fields)
        {
            var response = await admin.PostAsJsonAsync("/api/admin/blog", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            var problem = await response.ReadJsonAsync();
            foreach (var field in fields) ShouldHaveError(problem, field);
        }

        await ExpectErrors(new { title = "   " }, "Title");
        await ExpectErrors(new { title = new string('t', 201) }, "Title");
        await ExpectErrors(new { title = "No body", status = "Published" }, "BodyMarkdown", "Excerpt");
        await ExpectErrors(new { title = "Sched", excerpt = "e", bodyMarkdown = "b", status = "Scheduled" }, "PublishedAt");
        await ExpectErrors(new { title = "Bad status", status = "Live" }, "Status");
        await ExpectErrors(new { title = "Meta", metaTitle = new string('m', 71), metaDescription = new string('d', 161) }, "MetaTitle", "MetaDescription");
        await ExpectErrors(new { title = "Excerpt", excerpt = new string('e', 501) }, "Excerpt");
        await ExpectErrors(new { title = "Urls", canonicalUrl = "/blog/x", coverImageUrl = "ftp://img.example.com/x.jpg" }, "CanonicalUrl", "CoverImageUrl");
        await ExpectErrors(new { title = "Script", bodyMarkdown = "Hi <SCRIPT>alert(1)</script>" }, "BodyMarkdown");
        await ExpectErrors(new { title = "Place", cityId = Guid.NewGuid(), countryId = Guid.NewGuid() }, "CityId", "CountryId");
        await ExpectErrors(new { title = "Mismatch", cityId = place.CityId, countryId = otherPlace.CountryId }, "CityId");
        await ExpectErrors(new { title = "Tags", tagKeys = new[] { "no_such_tag_key" } }, "TagKeys");
        await ExpectErrors(new { title = "Links", destinationIds = new[] { Guid.NewGuid() }, eventIds = new[] { Guid.NewGuid() } }, "DestinationIds", "EventIds");
        await ExpectErrors(new { title = "User wishlist", wishlistIds = new[] { userWishlist } }, "WishlistIds");

        // PATCH validates the resulting state.
        var draft = await CreateAsync(admin, new { title = "Patch me" });
        var patch = await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(draft)}", new { status = "Published" });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await patch.ReadJsonAsync();
        ShouldHaveError(problem, "BodyMarkdown");
        ShouldHaveError(problem, "Excerpt");

        (await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(draft)}", new { title = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(draft)}", new { slug = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PatchAsJsonAsync($"/api/admin/blog/{Guid.NewGuid()}", new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_updates_fields_replaces_links_and_clears_values()
    {
        var place = await CreatePlaceAsync();
        var place2 = await CreatePlaceAsync();
        var tagA = await CreateTagAsync();
        var tagB = await CreateTagAsync();
        var admin = await AdminAsync();

        var created = await CreateAsync(admin, new
        {
            title = "Patchable",
            excerpt = "e",
            authorName = "Someone",
            cityId = place.CityId,
            tagKeys = new[] { tagA.Key },
            destinationIds = new[] { place.DestinationId, place2.DestinationId },
        });

        var response = await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(created)}", new
        {
            bodyMarkdown = Words(201),
            authorName = "",
            tagKeys = new[] { tagB.Key, tagA.Key },
            destinationIds = new[] { place2.DestinationId, place.DestinationId },
            clearCity = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var updated = await response.ReadJsonAsync();

        updated.GetProperty("title").GetString().Should().Be("Patchable");      // unchanged
        updated.GetProperty("excerpt").GetString().Should().Be("e");
        updated.GetProperty("authorName").ValueKind.Should().Be(JsonValueKind.Null);
        updated.GetProperty("readingTimeMinutes").GetInt32().Should().Be(2);
        updated.GetProperty("city").ValueKind.Should().Be(JsonValueKind.Null);
        updated.GetProperty("country").GetProperty("id").GetGuid().Should().Be(place.CountryId);   // kept
        updated.GetProperty("tagKeys").EnumerateArray().Select(t => t.GetString()).Should().BeEquivalentTo(new[] { tagA.Key, tagB.Key });
        updated.GetProperty("destinationIds").EnumerateArray().Select(x => x.GetGuid()).Should().Equal(place2.DestinationId, place.DestinationId);

        // Empty arrays clear the set; omitted arrays leave it alone.
        var cleared = await (await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(created)}", new { destinationIds = Array.Empty<Guid>() })).ReadJsonAsync();
        cleared.GetProperty("destinationIds").GetArrayLength().Should().Be(0);
        cleared.GetProperty("tagKeys").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Slug_change_keeps_old_slug_as_a_redirect()
    {
        var admin = await AdminAsync();
        var anon = Anon(followRedirects: false);
        var suffix = Unique();
        var post = await CreatePublishedAsync(admin, $"Old Name {suffix}");
        var original = Slug(post);

        var renamed = await (await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(post)}", new { slug = $"new-name-{suffix}" })).ReadJsonAsync();
        Slug(renamed).Should().Be($"new-name-{suffix}");
        renamed.GetProperty("previousSlugs").EnumerateArray().Select(s => s.GetString()).Should().Equal(original);

        var redirect = await anon.GetAsync($"/api/blog/{original}");
        redirect.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        redirect.Headers.Location!.ToString().Should().Be($"/api/blog/new-name-{suffix}");
        (await redirect.ReadJsonAsync()).GetProperty("redirectToSlug").GetString().Should().Be($"new-name-{suffix}");

        (await anon.GetAsync($"/api/blog/new-name-{suffix}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // A second rename: both old slugs point at the current one.
        await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(post)}", new { slug = $"third-name-{suffix}" });
        var chained = await anon.GetAsync($"/api/blog/{original}");
        (await chained.ReadJsonAsync()).GetProperty("redirectToSlug").GetString().Should().Be($"third-name-{suffix}");

        // A following client lands on the post itself.
        var followed = await (await Anon().GetAsync($"/api/blog/{original}")).ReadJsonAsync();
        Slug(followed).Should().Be($"third-name-{suffix}");

        // Going back to the original slug takes it out of the history.
        var back = await (await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(post)}", new { slug = original })).ReadJsonAsync();
        back.GetProperty("previousSlugs").EnumerateArray().Select(s => s.GetString())
            .Should().BeEquivalentTo(new[] { $"new-name-{suffix}", $"third-name-{suffix}" });
        (await anon.GetAsync($"/api/blog/{original}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // New posts don't take a retired slug.
        var clash = await CreateAsync(admin, new { title = $"New Name {suffix}" });
        Slug(clash).Should().Be($"new-name-{suffix}-2");

        // A redirect is only given while the post is visible.
        await admin.PostAsync($"/api/admin/blog/{Id(post)}/unpublish", null);
        (await anon.GetAsync($"/api/blog/new-name-{suffix}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Visibility & publishing ──────────────────────────────────────────────

    [Fact]
    public async Task Draft_hidden_scheduled_hidden_until_time_published_shown()
    {
        var tag = await CreateTagAsync();
        var admin = await AdminAsync();
        var anon = Anon();

        var draft = await CreateAsync(admin, new { title = "Hidden Draft", excerpt = "e", bodyMarkdown = "b", tagKeys = new[] { tag.Key } });
        var scheduled = await CreateAsync(admin, new
        {
            title = "Future Post", excerpt = "e", bodyMarkdown = "b", tagKeys = new[] { tag.Key },
            status = "Published", publishedAt = DateTimeOffset.UtcNow.AddDays(1),
        });
        scheduled.GetProperty("status").GetString().Should().Be("Scheduled");
        scheduled.GetProperty("isVisible").GetBoolean().Should().BeFalse();
        var published = await CreatePublishedAsync(admin, "Live Post", tagKeys: new[] { tag.Key });
        published.GetProperty("status").GetString().Should().Be("Published");
        published.GetProperty("isVisible").GetBoolean().Should().BeTrue();

        var list = await (await anon.GetAsync($"/api/blog?tag={tag.Key}")).ReadJsonAsync();
        Ids(list).Should().Equal(Id(published));
        (await anon.GetAsync($"/api/blog/{Slug(draft)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/blog/{Slug(scheduled)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/blog/{Slug(published)}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var adminScheduled = await (await admin.GetAsync($"/api/admin/blog?tag={tag.Key}&status=Scheduled")).ReadJsonAsync();
        Ids(adminScheduled).Should().Equal(Id(scheduled));
        var adminDrafts = await (await admin.GetAsync($"/api/admin/blog?tag={tag.Key}&status=draft")).ReadJsonAsync();
        Ids(adminDrafts).Should().Equal(Id(draft));
        (await admin.GetAsync("/api/admin/blog?status=Live")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The scheduled time passes (status stays "Scheduled" in the row): visible with no job.
        await WithDbAsync(db => db.BlogPosts.Where(p => p.Id == Id(scheduled))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PublishedAt, DateTime.UtcNow.AddMinutes(-1))));
        (await anon.GetAsync($"/api/blog/{Slug(scheduled)}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await (await anon.GetAsync($"/api/blog?tag={tag.Key}")).ReadJsonAsync();
        Ids(after).Should().BeEquivalentTo(new[] { Id(published), Id(scheduled) });
        var adminNow = await (await admin.GetAsync($"/api/admin/blog/{Id(scheduled)}")).ReadJsonAsync();
        adminNow.GetProperty("status").GetString().Should().Be("Published");
        adminNow.GetProperty("isVisible").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Publish_schedule_and_unpublish()
    {
        var admin = await AdminAsync();
        var anon = Anon();
        var post = await CreateAsync(admin, new { title = "To Publish", excerpt = "e", bodyMarkdown = "b" });

        var future = DateTimeOffset.UtcNow.AddDays(2);
        var scheduleResponse = await admin.PostAsJsonAsync($"/api/admin/blog/{Id(post)}/publish", new { publishedAt = future });
        scheduleResponse.StatusCode.Should().Be(HttpStatusCode.OK, await scheduleResponse.Content.ReadAsStringAsync());
        var scheduled = await scheduleResponse.ReadJsonAsync();
        scheduled.GetProperty("status").GetString().Should().Be("Scheduled");
        scheduled.GetProperty("publishedAt").GetDateTime().Should().BeCloseTo(future.UtcDateTime, TimeSpan.FromSeconds(1));
        (await anon.GetAsync($"/api/blog/{Slug(post)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var publishResponse = await admin.PostAsync($"/api/admin/blog/{Id(post)}/publish", null);
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK, await publishResponse.Content.ReadAsStringAsync());
        var published = await publishResponse.ReadJsonAsync();
        published.GetProperty("status").GetString().Should().Be("Published");
        published.GetProperty("publishedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await anon.GetAsync($"/api/blog/{Slug(post)}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var unpublished = await (await admin.PostAsync($"/api/admin/blog/{Id(post)}/unpublish", null)).ReadJsonAsync();
        unpublished.GetProperty("status").GetString().Should().Be("Draft");
        unpublished.GetProperty("publishedAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await anon.GetAsync($"/api/blog/{Slug(post)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Publishing needs an excerpt and a body.
        var empty = await CreateAsync(admin, new { title = "Empty" });
        var bad = await admin.PostAsync($"/api/admin/blog/{Id(empty)}/publish", null);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await bad.ReadJsonAsync();
        ShouldHaveError(problem, "BodyMarkdown");
        ShouldHaveError(problem, "Excerpt");

        (await admin.PostAsync($"/api/admin/blog/{Guid.NewGuid()}/publish", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"/api/admin/blog/{Guid.NewGuid()}/unpublish", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Public reads ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Public_post_has_meta_fallbacks_links_and_shape()
    {
        var place = await CreatePlaceAsync();
        var wishlist = await CreateWishlistAsync();
        var liveEvent = await CreateEventAsync(place.CityId, destinationId: place.DestinationId);
        var draftEvent = await CreateEventAsync(place.CityId, isPublished: false);
        var tag = await CreateTagAsync("Food");
        var admin = await AdminAsync();

        var post = await CreatePublishedAsync(admin, "Shape Check",
            destinationIds: new[] { place.DestinationId }, wishlistIds: new[] { wishlist },
            eventIds: new[] { draftEvent.Id, liveEvent.Id }, tagKeys: new[] { tag.Key }, cityId: place.CityId);

        var dto = await (await Anon().GetAsync($"/api/blog/{Slug(post)}")).ReadJsonAsync();
        dto.GetProperty("metaTitle").GetString().Should().Be("Shape Check");
        dto.GetProperty("metaDescription").GetString().Should().Be("Excerpt for Shape Check");
        dto.GetProperty("canonicalUrl").ValueKind.Should().Be(JsonValueKind.Null);
        dto.GetProperty("bodyMarkdown").GetString().Should().Be("Body for Shape Check.");
        dto.GetProperty("tags")[0].GetProperty("key").GetString().Should().Be(tag.Key);
        dto.GetProperty("city").GetProperty("name").GetString().Should().Be(place.CityName);

        var destination = dto.GetProperty("destinations")[0];
        destination.GetProperty("id").GetGuid().Should().Be(place.DestinationId);
        destination.GetProperty("city").GetString().Should().Be(place.CityName);
        destination.GetProperty("heroImageUrl").GetString().Should().Be("https://img.example.com/hero.jpg");
        destination.GetProperty("shortDescription").GetString()!.Length.Should().BeLessThanOrEqualTo(201);
        destination.GetProperty("shortDescription").GetString().Should().StartWith("A quiet beach").And.EndWith("…");

        var wl = dto.GetProperty("wishlists")[0];
        wl.GetProperty("totalDays").GetInt32().Should().Be(5);
        wl.GetProperty("basePricePerPerson").GetDecimal().Should().Be(1234.50m);
        wl.GetProperty("heroImage").GetString().Should().Be("https://img.example.com/trip.jpg");

        // Unpublished events are left out of the public view (but shown to admins).
        IdList(dto.GetProperty("events")).Should().Equal(liveEvent.Id);
        var evCard = dto.GetProperty("events")[0];
        evCard.GetProperty("venueName").GetString().Should().Be("The Yard");
        evCard.GetProperty("isFree").GetBoolean().Should().BeTrue();
        evCard.GetProperty("slug").GetString().Should().Be(liveEvent.Slug);
        var adminView = await (await admin.GetAsync($"/api/admin/blog/{Id(post)}")).ReadJsonAsync();
        IdList(adminView.GetProperty("events")).Should().Equal(draftEvent.Id, liveEvent.Id);
    }

    [Fact]
    public async Task List_filters_search_paging_and_newest_first()
    {
        var place = await CreatePlaceAsync();
        var place2 = await CreatePlaceAsync();
        var wishlist = await CreateWishlistAsync();
        var ev = await CreateEventAsync(place.CityId);
        var tag = await CreateTagAsync();
        var admin = await AdminAsync();
        var anon = Anon();
        var word = $"zebra{Unique()}";

        var oldest = await CreatePublishedAsync(admin, $"Oldest {word}", ageMinutes: 30, tagKeys: new[] { tag.Key }, cityId: place.CityId,
            destinationIds: new[] { place.DestinationId });
        var middle = await CreatePublishedAsync(admin, "Middle", ageMinutes: 20, tagKeys: new[] { tag.Key }, cityId: place2.CityId,
            wishlistIds: new[] { wishlist });
        var newest = await CreatePublishedAsync(admin, "Newest", ageMinutes: 10, tagKeys: new[] { tag.Key }, eventIds: new[] { ev.Id });

        async Task<List<Guid>> Query(string qs) => Ids(await (await anon.GetAsync($"/api/blog?{qs}")).ReadJsonAsync());

        (await Query($"tag={tag.Key}")).Should().Equal(Id(newest), Id(middle), Id(oldest));
        (await Query($"cityId={place.CityId}")).Should().Equal(Id(oldest));
        (await Query($"countryId={place2.CountryId}")).Should().Equal(Id(middle));
        (await Query($"destinationId={place.DestinationId}")).Should().Equal(Id(oldest));
        (await Query($"wishlistId={wishlist}")).Should().Equal(Id(middle));
        (await Query($"eventId={ev.Id}")).Should().Equal(Id(newest));
        (await Query($"search={word.ToUpperInvariant()}")).Should().Equal(Id(oldest));

        var paged = await (await anon.GetAsync($"/api/blog?tag={tag.Key}&pageSize=2&page=2")).ReadJsonAsync();
        paged.GetProperty("totalCount").GetInt32().Should().Be(3);
        paged.GetProperty("totalPages").GetInt32().Should().Be(2);
        Ids(paged).Should().Equal(Id(oldest));

        var summary = (await (await anon.GetAsync($"/api/blog?tag={tag.Key}")).ReadJsonAsync()).GetProperty("items")[2];
        summary.GetProperty("slug").GetString().Should().Be(Slug(oldest));
        summary.GetProperty("readingTimeMinutes").GetInt32().Should().Be(1);
        summary.GetProperty("city").GetProperty("id").GetGuid().Should().Be(place.CityId);
        summary.GetProperty("country").GetProperty("id").GetGuid().Should().Be(place.CountryId);
        summary.TryGetProperty("bodyMarkdown", out _).Should().BeFalse();

        var big = await (await anon.GetAsync($"/api/blog?tag={tag.Key}&pageSize=500")).ReadJsonAsync();
        big.GetProperty("pageSize").GetInt32().Should().Be(50);

        // Admin filters.
        var adminByDestination = await (await admin.GetAsync($"/api/admin/blog?destinationId={place.DestinationId}")).ReadJsonAsync();
        Ids(adminByDestination).Should().Equal(Id(oldest));
        var adminByWishlist = await (await admin.GetAsync($"/api/admin/blog?wishlistId={wishlist}&search=middle")).ReadJsonAsync();
        Ids(adminByWishlist).Should().Equal(Id(middle));
        var adminByCity = await (await admin.GetAsync($"/api/admin/blog?cityId={place2.CityId}&status=Published")).ReadJsonAsync();
        Ids(adminByCity).Should().Equal(Id(middle));
    }

    // ── Embeds ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Embeds_resolve_markers_and_flag_unresolvable_ones()
    {
        var suffix = Unique();
        var place = await CreatePlaceAsync($"Tarkwa Bay {suffix}");
        var template = await CreateWishlistAsync();
        var userCopy = await CreateWishlistAsync(isTemplate: false);
        var liveEvent = await CreateEventAsync(place.CityId);
        var draftEvent = await CreateEventAsync(place.CityId, isPublished: false);
        var admin = await AdminAsync();

        var body = string.Join("\n\n", new[]
        {
            "Intro paragraph.",
            $"{{{{destination:{place.DestinationId}}}}}",
            $"Inline {{{{ Destination : tarkwa-bay-{suffix} }}}} mention.",
            $"{{{{wishlist:{template}}}}}",
            $"{{{{wishlist:{userCopy}}}}}",
            $"{{{{event:{liveEvent.Slug}}}}}",
            $"{{{{event:{liveEvent.Id}}}}}",
            $"{{{{event:{draftEvent.Slug}}}}}",
            "{{destination:no-such-place-anywhere}}",
            "{{wishlist:not-a-guid}}",
            $"{{{{destination:{place.DestinationId}}}}}",   // duplicate: listed once
            "{{video:abc}} is not a marker",
        });

        var post = await CreatePublishedAsync(admin, "Embeds", body: body);
        var dto = await (await Anon().GetAsync($"/api/blog/{Slug(post)}")).ReadJsonAsync();

        dto.GetProperty("bodyMarkdown").GetString().Should().Be(body);   // stored and served as written
        var embeds = dto.GetProperty("embeds").EnumerateArray().ToList();
        embeds.Select(e => e.GetProperty("marker").GetString()).Should().Equal(
            $"{{{{destination:{place.DestinationId}}}}}",
            $"{{{{ Destination : tarkwa-bay-{suffix} }}}}",
            $"{{{{wishlist:{template}}}}}",
            $"{{{{wishlist:{userCopy}}}}}",
            $"{{{{event:{liveEvent.Slug}}}}}",
            $"{{{{event:{liveEvent.Id}}}}}",
            $"{{{{event:{draftEvent.Slug}}}}}",
            "{{destination:no-such-place-anywhere}}",
            "{{wishlist:not-a-guid}}");
        embeds.Select(e => e.GetProperty("found").GetBoolean()).Should().Equal(
            true, true, true, false, true, true, false, false, false);

        embeds[0].GetProperty("type").GetString().Should().Be("destination");
        embeds[0].GetProperty("destination").GetProperty("name").GetString().Should().Be(place.DestinationName);
        embeds[0].GetProperty("destination").GetProperty("country").GetString().Should().StartWith("Blogland");
        embeds[0].GetProperty("wishlist").ValueKind.Should().Be(JsonValueKind.Null);
        embeds[1].GetProperty("type").GetString().Should().Be("destination");
        embeds[1].GetProperty("ref").GetString().Should().Be($"tarkwa-bay-{suffix}");
        embeds[1].GetProperty("destination").GetProperty("id").GetGuid().Should().Be(place.DestinationId);
        embeds[2].GetProperty("wishlist").GetProperty("id").GetGuid().Should().Be(template);
        embeds[3].GetProperty("wishlist").ValueKind.Should().Be(JsonValueKind.Null);
        embeds[4].GetProperty("event").GetProperty("id").GetGuid().Should().Be(liveEvent.Id);
        embeds[4].GetProperty("event").GetProperty("isFree").GetBoolean().Should().BeTrue();
        embeds[5].GetProperty("event").GetProperty("slug").GetString().Should().Be(liveEvent.Slug);
        embeds[6].GetProperty("event").ValueKind.Should().Be(JsonValueKind.Null);

        // Admin preview resolves with the same rules.
        var preview = await (await admin.GetAsync($"/api/admin/blog/{Id(post)}")).ReadJsonAsync();
        preview.GetProperty("embeds").EnumerateArray().Select(e => e.GetProperty("found").GetBoolean())
            .Should().Equal(true, true, true, false, true, true, false, false, false);

        // Markers don't count towards reading time.
        BlogQueries.ReadingTimeMinutes("{{destination:x}} " + Words(200)).Should().Be(1);
        BlogQueries.ReadingTimeMinutes(Words(201)).Should().Be(2);
        BlogQueries.ReadingTimeMinutes("").Should().Be(1);
    }

    [Fact]
    public async Task Admin_preview_returns_drafts_with_embeds()
    {
        var place = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var draft = await CreateAsync(admin, new
        {
            title = "Preview Me",
            bodyMarkdown = $"Look: {{{{destination:{place.DestinationId}}}}}",
            destinationIds = new[] { place.DestinationId },
        });

        var preview = await admin.GetAsync($"/api/admin/blog/{Id(draft)}");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await preview.ReadJsonAsync();
        dto.GetProperty("status").GetString().Should().Be("Draft");
        dto.GetProperty("bodyMarkdown").GetString().Should().Contain("{{destination:");
        dto.GetProperty("embeds")[0].GetProperty("found").GetBoolean().Should().BeTrue();
        IdList(dto.GetProperty("destinations")).Should().Equal(place.DestinationId);
        dto.GetProperty("metaTitle").ValueKind.Should().Be(JsonValueKind.Null);

        (await admin.GetAsync($"/api/admin/blog/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Related posts ────────────────────────────────────────────────────────

    [Fact]
    public async Task Related_posts_share_a_destination_wishlist_or_tag()
    {
        var place = await CreatePlaceAsync();
        var wishlist = await CreateWishlistAsync();
        var tag = await CreateTagAsync();
        var otherTag = await CreateTagAsync();
        var admin = await AdminAsync();

        var main = await CreatePublishedAsync(admin, "Main", destinationIds: new[] { place.DestinationId },
            wishlistIds: new[] { wishlist }, tagKeys: new[] { tag.Key });
        var byDestination = await CreatePublishedAsync(admin, "By Destination", ageMinutes: 50, destinationIds: new[] { place.DestinationId });
        var byWishlist = await CreatePublishedAsync(admin, "By Wishlist", ageMinutes: 40, wishlistIds: new[] { wishlist });
        var byTag = await CreatePublishedAsync(admin, "By Tag", ageMinutes: 30, tagKeys: new[] { tag.Key, otherTag.Key });
        var alsoByTag = await CreatePublishedAsync(admin, "Also By Tag", ageMinutes: 60, tagKeys: new[] { tag.Key });
        var oldest = await CreatePublishedAsync(admin, "Oldest Related", ageMinutes: 70, tagKeys: new[] { tag.Key });
        await CreatePublishedAsync(admin, "Unrelated", tagKeys: new[] { otherTag.Key });
        await CreateAsync(admin, new { title = "Draft Related", destinationIds = new[] { place.DestinationId } });

        var dto = await (await Anon().GetAsync($"/api/blog/{Slug(main)}")).ReadJsonAsync();
        IdList(dto.GetProperty("relatedPosts")).Should().Equal(Id(byTag), Id(byWishlist), Id(byDestination), Id(alsoByTag));
        _ = oldest;   // 5th match: cut by the limit of 4

        var lonely = await CreatePublishedAsync(admin, "Lonely");
        var lonelyDto = await (await Anon().GetAsync($"/api/blog/{Slug(lonely)}")).ReadJsonAsync();
        lonelyDto.GetProperty("relatedPosts").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Destination_detail_lists_up_to_three_related_posts()
    {
        var place = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var anon = Anon();

        var empty = await (await anon.GetAsync($"/api/destinations/{place.DestinationId}")).ReadJsonAsync();
        empty.GetProperty("relatedPosts").GetArrayLength().Should().Be(0);

        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
            ids.Add(Id(await CreatePublishedAsync(admin, $"Dest Post {i}", ageMinutes: 10 * (i + 1), destinationIds: new[] { place.DestinationId })));
        await CreateAsync(admin, new { title = "Dest Draft", destinationIds = new[] { place.DestinationId } });
        await CreateAsync(admin, new
        {
            title = "Dest Scheduled", excerpt = "e", bodyMarkdown = "b", status = "Scheduled",
            publishedAt = DateTimeOffset.UtcNow.AddDays(3), destinationIds = new[] { place.DestinationId },
        });

        var detail = await (await anon.GetAsync($"/api/destinations/{place.DestinationId}")).ReadJsonAsync();
        var related = detail.GetProperty("relatedPosts");
        IdList(related).Should().Equal(ids[0], ids[1], ids[2]);
        related[0].GetProperty("slug").GetString().Should().NotBeNullOrEmpty();
        detail.GetProperty("upcomingEvents").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Wishlist_detail_lists_up_to_three_related_posts()
    {
        var wishlist = await CreateWishlistAsync();
        var admin = await AdminAsync();
        var anon = Anon();

        var emptyResponse = await anon.GetAsync($"/api/wishlists/{wishlist}");
        emptyResponse.StatusCode.Should().Be(HttpStatusCode.OK, await emptyResponse.Content.ReadAsStringAsync());
        (await emptyResponse.ReadJsonAsync()).GetProperty("relatedPosts").GetArrayLength().Should().Be(0);

        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
            ids.Add(Id(await CreatePublishedAsync(admin, $"Trip Post {i}", ageMinutes: 10 * (i + 1), wishlistIds: new[] { wishlist })));
        await CreateAsync(admin, new { title = "Trip Draft", wishlistIds = new[] { wishlist } });

        var detail = await (await anon.GetAsync($"/api/wishlists/{wishlist}")).ReadJsonAsync();
        IdList(detail.GetProperty("relatedPosts")).Should().Equal(ids[0], ids[1], ids[2]);
    }

    // ── Sitemap & tags ───────────────────────────────────────────────────────

    [Fact]
    public async Task Sitemap_lists_visible_posts_only()
    {
        var admin = await AdminAsync();
        var published = await CreatePublishedAsync(admin, $"Sitemap Live {Unique()}");
        var draft = await CreateAsync(admin, new { title = $"Sitemap Draft {Unique()}" });

        var response = await Anon().GetAsync("/api/blog/sitemap");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = (await response.ReadJsonAsync()).EnumerateArray().ToList();
        var entry = entries.Single(e => e.GetProperty("slug").GetString() == Slug(published));
        entry.GetProperty("updatedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        entry.GetProperty("publishedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        entries.Should().NotContain(e => e.GetProperty("slug").GetString() == Slug(draft));
    }

    [Fact]
    public async Task Tags_endpoint_counts_visible_posts_only()
    {
        var used = await CreateTagAsync("Used Tag");
        var draftOnly = await CreateTagAsync("Draft Only Tag");
        var admin = await AdminAsync();
        await CreatePublishedAsync(admin, "Tagged 1", tagKeys: new[] { used.Key });
        await CreatePublishedAsync(admin, "Tagged 2", tagKeys: new[] { used.Key });
        await CreateAsync(admin, new { title = "Tagged Draft", tagKeys = new[] { used.Key, draftOnly.Key } });

        var tags = (await (await Anon().GetAsync("/api/blog/tags")).ReadJsonAsync()).EnumerateArray().ToList();
        var usedRow = tags.Single(t => t.GetProperty("key").GetString() == used.Key);
        usedRow.GetProperty("name").GetString().Should().Be("Used Tag");
        usedRow.GetProperty("postCount").GetInt32().Should().Be(2);
        tags.Should().NotContain(t => t.GetProperty("key").GetString() == draftOnly.Key);
    }

    // ── Access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Non_admin_gets_403_and_anonymous_401_on_admin_endpoints()
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), "Blog Reader");
        var client = _factory.CreateClient().Authenticated(user);
        var anon = Anon();
        var id = Guid.NewGuid();

        (await client.GetAsync("/api/admin/blog")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/admin/blog/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/admin/blog", new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PatchAsJsonAsync($"/api/admin/blog/{id}", new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/admin/blog/{id}/publish", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/admin/blog/{id}/unpublish", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/api/admin/blog/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await anon.GetAsync("/api/admin/blog")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/admin/blog", new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Public_endpoints_allow_anonymous()
    {
        var admin = await AdminAsync();
        var post = await CreatePublishedAsync(admin, $"Open Post {Unique()}");
        var anon = Anon();

        (await anon.GetAsync("/api/blog")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.GetAsync($"/api/blog/{Slug(post)}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.GetAsync("/api/blog/sitemap")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.GetAsync("/api/blog/tags")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.GetAsync("/api/blog/no-such-post-at-all")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Deletes ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_post_links_and_slug_history()
    {
        var place = await CreatePlaceAsync();
        var wishlist = await CreateWishlistAsync();
        var ev = await CreateEventAsync(place.CityId);
        var tag = await CreateTagAsync();
        var admin = await AdminAsync();

        var post = await CreatePublishedAsync(admin, $"Doomed {Unique()}", destinationIds: new[] { place.DestinationId },
            wishlistIds: new[] { wishlist }, eventIds: new[] { ev.Id }, tagKeys: new[] { tag.Key });
        var oldSlug = Slug(post);
        await admin.PatchAsJsonAsync($"/api/admin/blog/{Id(post)}", new { slug = oldSlug + "-renamed" });

        (await admin.DeleteAsync($"/api/admin/blog/{Id(post)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/admin/blog/{Id(post)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Anon().GetAsync($"/api/blog/{oldSlug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/admin/blog/{Id(post)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var leftovers = await WithDbAsync(async db => new[]
        {
            await db.BlogPostDestinations.CountAsync(x => x.BlogPostId == Id(post)),
            await db.BlogPostWishlists.CountAsync(x => x.BlogPostId == Id(post)),
            await db.BlogPostEvents.CountAsync(x => x.BlogPostId == Id(post)),
            await db.BlogPostTags.CountAsync(x => x.BlogPostId == Id(post)),
            await db.BlogPostSlugHistory.CountAsync(x => x.BlogPostId == Id(post)),
        });
        leftovers.Should().AllBeEquivalentTo(0);

        // The linked things themselves are untouched.
        (await WithDbAsync(db => db.Destinations.AnyAsync(d => d.DestinationId == place.DestinationId))).Should().BeTrue();
        (await WithDbAsync(db => db.Tags.AnyAsync(t => t.TagId == tag.Id))).Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_a_linked_target_removes_the_link_and_city_delete_clears_the_place()
    {
        var place = await CreatePlaceAsync();
        var keep = await CreatePlaceAsync();
        var ev = await CreateEventAsync(keep.CityId);
        var admin = await AdminAsync();
        var post = await CreatePublishedAsync(admin, "Survivor",
            destinationIds: new[] { place.DestinationId, keep.DestinationId }, eventIds: new[] { ev.Id });

        await WithDbAsync(async db =>
        {
            await db.Destinations.Where(d => d.DestinationId == place.DestinationId).ExecuteDeleteAsync();
            await db.Events.Where(e => e.Id == ev.Id).ExecuteDeleteAsync();
            return 0;
        });

        var after = await (await admin.GetAsync($"/api/admin/blog/{Id(post)}")).ReadJsonAsync();
        after.GetProperty("destinationIds").EnumerateArray().Select(x => x.GetGuid()).Should().Equal(keep.DestinationId);
        after.GetProperty("eventIds").GetArrayLength().Should().Be(0);

        // City / country deletes clear the reference rather than the post.
        var cityPlace = await CreatePlaceAsync();
        var cityPost = await CreateAsync(admin, new { title = "City Post", cityId = cityPlace.CityId });
        await WithDbAsync(async db =>
        {
            await db.DestinationCities.Where(dc => dc.CityId == cityPlace.CityId).ExecuteDeleteAsync();
            return await db.Cities.Where(c => c.CityId == cityPlace.CityId).ExecuteDeleteAsync();
        });
        var cleared = await (await admin.GetAsync($"/api/admin/blog/{Id(cityPost)}")).ReadJsonAsync();
        cleared.GetProperty("city").ValueKind.Should().Be(JsonValueKind.Null);
        cleared.GetProperty("country").GetProperty("id").GetGuid().Should().Be(cityPlace.CountryId);
    }
}
