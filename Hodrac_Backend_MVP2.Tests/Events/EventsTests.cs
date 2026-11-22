using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastucture.Seeder;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Tests.Hosts;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hodrac_Backend_MVP2.Tests.Events;

/// <summary>/api/events (public), /api/admin/events, destination detail upcomingEvents, EventsSeeder.</summary>
[Collection(IntegrationCollection.Name)]
public class EventsTests
{
    private readonly HodracApiFactory _factory;

    public EventsTests(HodracApiFactory factory) => _factory = factory;

    private record Place(Guid CountryId, Guid CityId, Guid DestinationId, string DestinationName);

    private async Task<HttpClient> AdminAsync()
    {
        var user = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        return _factory.CreateClient().Authenticated(user);
    }

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    /// <summary>A fresh country + city + destination (linked to the city), unique per call.</summary>
    private Task<Place> CreatePlaceAsync(string? cityName = null) => WithDbAsync(async db =>
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var country = new Country { CountryId = Guid.NewGuid(), CountryName = $"Eventland {tag}" };
        var city = new City { CityId = Guid.NewGuid(), CityName = cityName ?? $"Event City {tag}", CountryId = country.CountryId };
        var destination = new Destination
        {
            DestinationId = Guid.NewGuid(),
            DestinationName = $"Event Venue {tag}",
            CountryId = country.CountryId,
            TimeZone = "Africa/Lagos",
        };
        db.Countries.Add(country);
        db.Cities.Add(city);
        db.Destinations.Add(destination);
        db.DestinationCities.Add(new DestinationCity { DestinationId = destination.DestinationId, CityId = city.CityId });
        await db.SaveChangesAsync();
        return new Place(country.CountryId, city.CityId, destination.DestinationId, destination.DestinationName);
    });

    private static Dictionary<string, object?> EventBody(
        string name, DateTimeOffset startsAt, Guid? cityId = null, Guid? destinationId = null,
        DateTimeOffset? endsAt = null, bool isPublished = true) => new()
    {
        ["name"] = name,
        ["description"] = "A test event.",
        ["startsAt"] = startsAt,
        ["endsAt"] = endsAt,
        ["cityId"] = cityId,
        ["destinationId"] = destinationId,
        ["isPublished"] = isPublished,
    };

    private static async Task<JsonElement> CreateAsync(HttpClient admin, Dictionary<string, object?> body)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/events", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static DateTimeOffset InDays(double days) => DateTimeOffset.UtcNow.AddDays(days);

    // ── Admin CRUD ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_creates_event_with_defaults_and_city_from_destination()
    {
        var place = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var starts = new DateTimeOffset(2030, 11, 28, 12, 0, 0, TimeSpan.FromHours(1));

        var body = EventBody("Créme Festival", starts, destinationId: place.DestinationId, endsAt: starts.AddHours(4));
        body["organiser"] = "  Oga Test ";
        body["priceMinKobo"] = 0L;
        body["priceMaxKobo"] = 0L;
        body["priceNote"] = "Free general entry";
        body["instagramHandle"] = "@cremefest";
        body["ticketUrl"] = "https://tickets.example.com/creme";
        body["venueAddress"] = "6 Boundary Road";

        var created = await CreateAsync(admin, body);

        created.GetProperty("slug").GetString().Should().Be("creme-festival-2030");
        created.GetProperty("startsAt").GetDateTime().Should().Be(new DateTime(2030, 11, 28, 11, 0, 0, DateTimeKind.Utc));
        created.GetProperty("endsAt").GetDateTime().Should().Be(new DateTime(2030, 11, 28, 15, 0, 0, DateTimeKind.Utc));
        created.GetProperty("timeZone").GetString().Should().Be("Africa/Lagos");
        created.GetProperty("currency").GetString().Should().Be("NGN");
        created.GetProperty("organiser").GetString().Should().Be("Oga Test");
        created.GetProperty("instagramHandle").GetString().Should().Be("cremefest");
        created.GetProperty("isFree").GetBoolean().Should().BeTrue();
        created.GetProperty("isPublished").GetBoolean().Should().BeTrue();
        created.GetProperty("isUpcoming").GetBoolean().Should().BeTrue();
        created.GetProperty("city").GetProperty("id").GetGuid().Should().Be(place.CityId);
        var venue = created.GetProperty("venue");
        venue.GetProperty("destinationId").GetGuid().Should().Be(place.DestinationId);
        venue.GetProperty("destinationName").GetString().Should().Be(place.DestinationName);
        venue.GetProperty("name").GetString().Should().Be(place.DestinationName);
        venue.GetProperty("address").GetString().Should().Be("6 Boundary Road");

        var fetched = await (await admin.GetAsync($"/api/admin/events/{Id(created)}")).ReadJsonAsync();
        fetched.GetProperty("slug").GetString().Should().Be("creme-festival-2030");

        var list = await (await admin.GetAsync($"/api/admin/events?cityId={place.CityId}&search=creme")).ReadJsonAsync();
        list.GetProperty("totalCount").GetInt32().Should().Be(1);
        list.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(Id(created));
    }

    [Fact]
    public async Task Admin_patch_updates_fields_and_keeps_slug()
    {
        var place = await CreatePlaceAsync();
        var otherPlace = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var body = EventBody("Patch Party", InDays(20), cityId: place.CityId, endsAt: InDays(21));
        body["priceMinKobo"] = 100_000L;
        body["priceMaxKobo"] = 200_000L;
        var created = await CreateAsync(admin, body);
        var slug = created.GetProperty("slug").GetString();

        var response = await admin.PatchAsJsonAsync($"/api/admin/events/{Id(created)}", new
        {
            name = "Patch Party Renamed",
            clearEndsAt = true,
            clearPrices = true,
            destinationId = otherPlace.DestinationId,
            websiteUrl = "https://patch.example.com",
            timeZone = "W. Central Africa Standard Time",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var updated = await response.ReadJsonAsync();

        updated.GetProperty("name").GetString().Should().Be("Patch Party Renamed");
        updated.GetProperty("slug").GetString().Should().Be(slug);
        updated.GetProperty("endsAt").ValueKind.Should().Be(JsonValueKind.Null);
        updated.GetProperty("priceMinKobo").ValueKind.Should().Be(JsonValueKind.Null);
        updated.GetProperty("priceMaxKobo").ValueKind.Should().Be(JsonValueKind.Null);
        updated.GetProperty("isFree").GetBoolean().Should().BeFalse();
        updated.GetProperty("websiteUrl").GetString().Should().Be("https://patch.example.com");
        updated.GetProperty("timeZone").GetString().Should().Be("Africa/Lagos");
        updated.GetProperty("venue").GetProperty("destinationId").GetGuid().Should().Be(otherPlace.DestinationId);
        updated.GetProperty("city").GetProperty("id").GetGuid().Should().Be(otherPlace.CityId);

        // Clearing the destination keeps the city.
        var cleared = await (await admin.PatchAsJsonAsync($"/api/admin/events/{Id(created)}", new { clearDestination = true })).ReadJsonAsync();
        cleared.GetProperty("venue").GetProperty("destinationId").ValueKind.Should().Be(JsonValueKind.Null);
        cleared.GetProperty("city").GetProperty("id").GetGuid().Should().Be(otherPlace.CityId);

        (await admin.PatchAsJsonAsync($"/api/admin/events/{Guid.NewGuid()}", new { name = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_create_and_patch_validate_input()
    {
        var place = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var starts = InDays(30);

        async Task ExpectError(Dictionary<string, object?> body, string field)
        {
            var response = await admin.PostAsJsonAsync("/api/admin/events", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            var problem = await response.ReadJsonAsync();
            problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(problem.ToString());
        }

        await ExpectError(EventBody("Backwards", starts, cityId: place.CityId, endsAt: starts.AddHours(-1)), "EndsAt");
        await ExpectError(EventBody("No City", starts, cityId: Guid.NewGuid()), "CityId");
        await ExpectError(EventBody("No City At All", starts), "CityId");
        await ExpectError(EventBody("Ghost Venue", starts, destinationId: Guid.NewGuid()), "DestinationId");
        await ExpectError(EventBody("   ", starts, cityId: place.CityId), "Name");

        var badUrl = EventBody("Bad Url", starts, cityId: place.CityId);
        badUrl["ticketUrl"] = "ftp://tickets.example.com";
        await ExpectError(badUrl, "TicketUrl");

        var relativeUrl = EventBody("Relative Url", starts, cityId: place.CityId);
        relativeUrl["imageUrl"] = "/images/x.jpg";
        await ExpectError(relativeUrl, "ImageUrl");

        var prices = EventBody("Prices", starts, cityId: place.CityId);
        prices["priceMinKobo"] = 500_000L;
        prices["priceMaxKobo"] = 100_000L;
        await ExpectError(prices, "PriceMinKobo");

        var negative = EventBody("Negative", starts, cityId: place.CityId);
        negative["priceMaxKobo"] = -1L;
        await ExpectError(negative, "PriceMaxKobo");

        var tz = EventBody("Bad Tz", starts, cityId: place.CityId);
        tz["timeZone"] = "Mars/Olympus";
        await ExpectError(tz, "TimeZone");

        // PATCH validates the resulting state: a min above the stored max is rejected.
        var okBody = EventBody("Valid Event", starts, cityId: place.CityId, endsAt: starts.AddHours(2));
        okBody["priceMaxKobo"] = 100_000L;
        var created = await CreateAsync(admin, okBody);
        var patch = await admin.PatchAsJsonAsync($"/api/admin/events/{Id(created)}", new { priceMinKobo = 200_000L });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var endBeforeStart = await admin.PatchAsJsonAsync($"/api/admin/events/{Id(created)}", new { startsAt = starts.AddHours(5) });
        endBeforeStart.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PatchAsJsonAsync($"/api/admin/events/{Id(created)}", new { websiteUrl = "not a url" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Slugs_are_unique_with_numeric_suffixes()
    {
        var place = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var name = $"Slug Fest {Guid.NewGuid():N}"[..20];
        var starts = new DateTimeOffset(2031, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var first = await CreateAsync(admin, EventBody(name, starts, cityId: place.CityId));
        var second = await CreateAsync(admin, EventBody(name, starts, cityId: place.CityId));
        var third = await CreateAsync(admin, EventBody(name, starts.AddDays(3), cityId: place.CityId));

        var baseSlug = first.GetProperty("slug").GetString()!;
        baseSlug.Should().EndWith("-2031").And.MatchRegex("^[a-z0-9-]+$");
        second.GetProperty("slug").GetString().Should().Be($"{baseSlug}-2");
        third.GetProperty("slug").GetString().Should().Be($"{baseSlug}-3");

        // A name already ending in the year doesn't repeat it (so it collides with the same base).
        var withYear = await CreateAsync(admin, EventBody($"{name} 2031", starts, cityId: place.CityId));
        withYear.GetProperty("slug").GetString().Should().Be($"{baseSlug}-4");
    }

    [Fact]
    public async Task Delete_removes_event()
    {
        var place = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var created = await CreateAsync(admin, EventBody("Delete Me", InDays(5), cityId: place.CityId));

        (await admin.DeleteAsync($"/api/admin/events/{Id(created)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/admin/events/{Id(created)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.CreateClient().GetAsync($"/api/events/{Id(created)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/admin/events/{Id(created)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Non_admin_gets_403_and_anonymous_401_on_admin_endpoints()
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), "Event Fan");
        var client = _factory.CreateClient().Authenticated(user);
        var anon = _factory.CreateClient();
        var id = Guid.NewGuid();

        (await client.GetAsync("/api/admin/events")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/admin/events/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/admin/events", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PatchAsJsonAsync($"/api/admin/events/{id}", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/admin/events/{id}/publish", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/admin/events/{id}/unpublish", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/api/admin/events/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await anon.GetAsync("/api/admin/events")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Public_endpoints_allow_anonymous()
    {
        var place = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var created = await CreateAsync(admin, EventBody("Open Air Jam", InDays(8), cityId: place.CityId));
        var anon = _factory.CreateClient();

        var list = await anon.GetAsync($"/api/events?cityId={place.CityId}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await list.ReadJsonAsync();
        page.GetProperty("totalCount").GetInt32().Should().Be(1);
        page.GetProperty("totalPages").GetInt32().Should().Be(1);
        var item = page.GetProperty("items")[0];
        item.GetProperty("id").GetGuid().Should().Be(Id(created));
        item.TryGetProperty("isPublished", out _).Should().BeFalse();
        item.GetProperty("city").GetProperty("name").GetString().Should().StartWith("Event City");

        (await anon.GetAsync($"/api/events/{Id(created)}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var bySlug = await anon.GetAsync($"/api/events/slug/{created.GetProperty("slug").GetString()}");
        bySlug.StatusCode.Should().Be(HttpStatusCode.OK);
        (await bySlug.ReadJsonAsync()).GetProperty("id").GetGuid().Should().Be(Id(created));

        (await anon.GetAsync($"/api/events/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync("/api/events/slug/no-such-event-1999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Visibility & filters ─────────────────────────────────────────────────

    [Fact]
    public async Task Publish_and_unpublish_control_public_visibility()
    {
        var place = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var anon = _factory.CreateClient();
        var created = await CreateAsync(admin, EventBody("Draft Rave", InDays(12), cityId: place.CityId, isPublished: false));
        var id = Id(created);
        var slug = created.GetProperty("slug").GetString();
        created.GetProperty("isPublished").GetBoolean().Should().BeFalse();

        async Task ExpectVisible(bool visible)
        {
            var expected = visible ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            (await anon.GetAsync($"/api/events/{id}")).StatusCode.Should().Be(expected);
            (await anon.GetAsync($"/api/events/slug/{slug}")).StatusCode.Should().Be(expected);
            var page = await (await anon.GetAsync($"/api/events?cityId={place.CityId}")).ReadJsonAsync();
            page.GetProperty("totalCount").GetInt32().Should().Be(visible ? 1 : 0);
        }

        await ExpectVisible(false);

        (await admin.PostAsync($"/api/admin/events/{id}/publish", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ExpectVisible(true);

        (await admin.PostAsync($"/api/admin/events/{id}/unpublish", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ExpectVisible(false);

        // Admin still sees it, filterable by published state.
        var drafts = await (await admin.GetAsync($"/api/admin/events?cityId={place.CityId}&published=false")).ReadJsonAsync();
        drafts.GetProperty("totalCount").GetInt32().Should().Be(1);
        (await admin.PostAsync($"/api/admin/events/{Guid.NewGuid()}/publish", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upcoming_city_destination_and_date_filters()
    {
        var place = await CreatePlaceAsync();
        var otherPlace = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var anon = _factory.CreateClient();

        var past = await CreateAsync(admin, EventBody("Past Party", InDays(-10), cityId: place.CityId, endsAt: InDays(-9)));
        // Started yesterday, ends tomorrow: still upcoming (not finished).
        var ongoing = await CreateAsync(admin, EventBody("Ongoing Party", InDays(-1), cityId: place.CityId, endsAt: InDays(1)));
        var later = await CreateAsync(admin, EventBody("Later Party", InDays(40), destinationId: place.DestinationId));
        var soon = await CreateAsync(admin, EventBody("Soon Party", InDays(3), cityId: place.CityId));
        await CreateAsync(admin, EventBody("Elsewhere Party", InDays(4), cityId: otherPlace.CityId));

        static List<Guid> Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(Id).ToList();

        var upcoming = await (await anon.GetAsync($"/api/events?cityId={place.CityId}")).ReadJsonAsync();
        Ids(upcoming).Should().Equal(Id(ongoing), Id(soon), Id(later));

        var all = await (await anon.GetAsync($"/api/events?cityId={place.CityId}&upcoming=false")).ReadJsonAsync();
        Ids(all).Should().Equal(Id(past), Id(ongoing), Id(soon), Id(later));

        var atDestination = await (await anon.GetAsync($"/api/events?destinationId={place.DestinationId}")).ReadJsonAsync();
        Ids(atDestination).Should().Equal(Id(later));

        var from = Uri.EscapeDataString(InDays(2).ToString("o"));
        var to = Uri.EscapeDataString(InDays(10).ToString("o"));
        var window = await (await anon.GetAsync($"/api/events?cityId={place.CityId}&from={from}&to={to}")).ReadJsonAsync();
        Ids(window).Should().Equal(Id(soon));

        var paged = await (await anon.GetAsync($"/api/events?cityId={place.CityId}&pageSize=2&page=2")).ReadJsonAsync();
        paged.GetProperty("totalCount").GetInt32().Should().Be(3);
        paged.GetProperty("totalPages").GetInt32().Should().Be(2);
        Ids(paged).Should().Equal(Id(later));

        var adminPast = await (await admin.GetAsync($"/api/admin/events?cityId={place.CityId}&upcoming=false")).ReadJsonAsync();
        Ids(adminPast).Should().Equal(Id(past));
        adminPast.GetProperty("items")[0].GetProperty("isUpcoming").GetBoolean().Should().BeFalse();
    }

    // ── Destination detail ───────────────────────────────────────────────────

    [Fact]
    public async Task Destination_detail_lists_up_to_five_published_upcoming_events()
    {
        var place = await CreatePlaceAsync();
        var otherPlace = await CreatePlaceAsync();
        var admin = await AdminAsync();
        var anon = _factory.CreateClient();

        // Empty list (not null) when nothing is on.
        var empty = await (await anon.GetAsync($"/api/destinations/{place.DestinationId}")).ReadJsonAsync();
        empty.GetProperty("upcomingEvents").GetArrayLength().Should().Be(0);

        await CreateAsync(admin, EventBody("Gone Already", InDays(-5), destinationId: place.DestinationId));
        await CreateAsync(admin, EventBody("Secret Draft", InDays(1), destinationId: place.DestinationId, isPublished: false));
        await CreateAsync(admin, EventBody("Other Venue", InDays(1), destinationId: otherPlace.DestinationId));
        // Created latest-first so ordering comes from StartsAt, not insertion order.
        var idsByNight = new Dictionary<int, Guid>();
        for (var i = 6; i >= 1; i--)
        {
            var body = EventBody($"Venue Night {i}", InDays(i * 2), destinationId: place.DestinationId);
            if (i == 1) { body["priceMinKobo"] = 0L; body["priceMaxKobo"] = 0L; }
            idsByNight[i] = Id(await CreateAsync(admin, body));
        }
        var expected = Enumerable.Range(1, 5).Select(i => idsByNight[i]).ToList();

        var response = await anon.GetAsync($"/api/destinations/{place.DestinationId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var detail = await response.ReadJsonAsync();
        var events = detail.GetProperty("upcomingEvents").EnumerateArray().ToList();

        events.Should().HaveCount(5);
        events.Select(Id).Should().Equal(expected); // soonest first
        events.Select(e => e.GetProperty("name").GetString()).Should()
            .Equal("Venue Night 1", "Venue Night 2", "Venue Night 3", "Venue Night 4", "Venue Night 5");
        events[0].GetProperty("isFree").GetBoolean().Should().BeTrue();
        events[0].GetProperty("priceMinKobo").GetInt64().Should().Be(0);
        events[1].GetProperty("isFree").GetBoolean().Should().BeFalse();
        events[0].TryGetProperty("slug", out _).Should().BeTrue();
        events[0].TryGetProperty("startsAt", out _).Should().BeTrue();
        events[0].TryGetProperty("endsAt", out _).Should().BeTrue();
    }

    // ── Seeder ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Seeder_is_idempotent_and_falls_back_to_venue_name_without_image_garden()
    {
        var hasCity = await WithDbAsync(db => db.Cities.AnyAsync(c => c.CityName == EventsSeeder.CityName));
        if (!hasCity) await CreatePlaceAsync(EventsSeeder.CityName);

        var first = await WithDbAsync(db => EventsSeeder.SeedAsync(db));
        var second = await WithDbAsync(db => EventsSeeder.SeedAsync(db));
        first.Should().Be(4);
        second.Should().Be(0);

        var slugs = EventsSeeder.Slugs;
        slugs.Should().BeEquivalentTo(
            "oga-bole-festival-2026",
            "fungasm-2026",
            "metro-events-benin-3-days-detty-december-rave-2026",
            "rave-experience-playtopia-benin-2026");

        var rows = await WithDbAsync(db => db.Events.AsNoTracking().Where(e => slugs.Contains(e.Slug)).ToListAsync());
        rows.Should().HaveCount(4);

        var ogaBole = rows.Single(e => e.Slug == "oga-bole-festival-2026");
        ogaBole.StartsAt.Should().Be(new DateTime(2026, 11, 28, 11, 0, 0, DateTimeKind.Utc));
        ogaBole.EndsAt.Should().Be(new DateTime(2026, 11, 28, 15, 0, 0, DateTimeKind.Utc));
        ogaBole.PriceMinKobo.Should().Be(0);
        ogaBole.PriceMaxKobo.Should().Be(0);
        ogaBole.DestinationId.Should().BeNull();               // no Image Garden in the test db
        ogaBole.VenueName.Should().Be(EventsSeeder.ImageGardenName);
        ogaBole.VenueAddress.Should().Be(EventsSeeder.ImageGardenAddress);

        var fungasm = rows.Single(e => e.Slug == "fungasm-2026");
        fungasm.EndsAt.Should().BeNull();
        fungasm.PriceMinKobo.Should().BeNull();
        fungasm.VenueName.Should().Be("Ogbe Primary School");

        rows.Single(e => e.Slug.StartsWith("metro-events")).Description.Should().Contain("Venue reported as Image Garden — confirm");

        // The past Rave Experience edition is not upcoming on the public API.
        var rave = rows.Single(e => e.Slug == "rave-experience-playtopia-benin-2026");
        var anon = _factory.CreateClient();
        var upcoming = await (await anon.GetAsync($"/api/events?cityId={rave.CityId}&pageSize=50")).ReadJsonAsync();
        upcoming.GetProperty("items").EnumerateArray().Select(Id).Should().NotContain(rave.Id);
        (await anon.GetAsync("/api/events/slug/rave-experience-playtopia-benin-2026")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Once Image Garden exists, a re-run links the fallback rows (and still creates nothing).
        var gardenId = await WithDbAsync(async db =>
        {
            var garden = new Destination
            {
                DestinationId = Guid.NewGuid(),
                DestinationName = EventsSeeder.ImageGardenName,
                CountryId = (await db.Cities.SingleAsync(c => c.CityId == rave.CityId)).CountryId,
            };
            db.Destinations.Add(garden);
            await db.SaveChangesAsync();
            return garden.DestinationId;
        });
        (await WithDbAsync(db => EventsSeeder.SeedAsync(db))).Should().Be(0);

        var relinked = await WithDbAsync(db => db.Events.AsNoTracking().Where(e => slugs.Contains(e.Slug)).ToListAsync());
        relinked.Where(e => e.Slug != "fungasm-2026").Should().OnlyContain(e => e.DestinationId == gardenId && e.VenueName == null);
        relinked.Single(e => e.Slug == "fungasm-2026").DestinationId.Should().BeNull();
    }
}
