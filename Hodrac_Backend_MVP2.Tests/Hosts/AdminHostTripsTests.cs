using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

/// <summary>POST /api/admin/hosts/{id}/trips: a hosted trip built on a host's behalf.</summary>
[Collection(IntegrationCollection.Name)]
public class AdminHostTripsTests
{
    private const long Price = 25_000_000;
    private const long Deposit = 5_000_000;
    private const long Instalment = 10_000_000;

    private readonly HodracApiFactory _factory;

    public AdminHostTripsTests(HodracApiFactory factory) => _factory = factory;

    private static DateOnly Day(int daysFromNow) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysFromNow));

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    private async Task<HttpClient> AdminAsync()
    {
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        return _factory.CreateClient().Authenticated(admin);
    }

    private async Task<(HttpClient Client, TestUser User, Guid HostId)> HostWithPayoutAsync(string name)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), name);
        var client = _factory.CreateClient().Authenticated(user);
        var hostId = await CreatePendingHostAsync(client, name);
        return (client, user, hostId);
    }

    private static Dictionary<string, object?> HostedTripBody(string place) => new()
    {
        ["freeTextDestination"] = place,
        ["startDate"] = Day(90),
        ["endDate"] = Day(95),
        ["isDateFlexible"] = false,
        ["caption"] = "Built for you by Hodrac",
        ["coverImageUrl"] = "https://images.example.test/cover.jpg",
        ["maxGroupSize"] = 8,
        ["pricePerSeatKobo"] = Price,
        ["depositKobo"] = Deposit,
        ["instalments"] = new[]
        {
            new { sequence = 1, amountKobo = Instalment, dueDate = Day(30) },
            new { sequence = 2, amountKobo = Instalment, dueDate = Day(60) },
        },
        ["feeMode"] = "HostAbsorbs",
        ["seatHoldHours"] = 24,
        ["termsText"] = "Deposit is non-refundable.",
        ["inclusions"] = "Hotel, transfers",
        ["exclusions"] = "Flights",
    };

    private Task<int> SetFoundingAsync(Guid hostId, bool founding) =>
        WithDbAsync(db => db.HostProfiles.Where(h => h.Id == hostId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(h => h.IsFoundingHost, founding)
                .SetProperty(h => h.FoundingFreeDepartureTripId, (Guid?)null)));

    [Fact]
    public async Task Admin_creates_private_hosted_trip_authored_by_the_host()
    {
        var (hostClient, hostUser, hostId) = await HostWithPayoutAsync("Built Host");
        var admin = await AdminAsync();
        var place = $"Bali-{Guid.NewGuid():N}";

        var response = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", HostedTripBody(place));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var dto = await response.ReadJsonAsync();
        var tripId = dto.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().EndWith($"/api/trips/{tripId}");

        dto.GetProperty("authorUserId").GetString().Should().Be(hostUser.UserId);
        dto.GetProperty("tripType").GetString().Should().Be("hosted");
        dto.GetProperty("isListed").GetBoolean().Should().BeFalse();
        dto.GetProperty("pricePerSeatKobo").GetInt64().Should().Be(Price);
        dto.GetProperty("depositKobo").GetInt64().Should().Be(Deposit);

        var trip = await WithDbAsync(db => db.TripPosts.AsNoTracking().FirstAsync(t => t.Id == tripId));
        trip.AuthorUserId.Should().Be(Guid.Parse(hostUser.UserId));
        trip.HostProfileId.Should().Be(hostId);
        trip.IsHostedTrip.Should().BeTrue();
        trip.IsListed.Should().BeFalse();
        trip.SeatHoldHours.Should().Be(24);
        trip.TermsVersion.Should().Be(1);
        trip.Inclusions.Should().Be("Hotel, transfers");
        trip.Instalments.Should().HaveCount(2);
        trip.IsFoundingFreeDeparture.Should().BeFalse();

        // The host sees and can manage it as their own trip.
        var mine = await hostClient.GetAsync($"/api/trips/{tripId}");
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        var edit = await hostClient.PatchAsJsonAsync($"/api/trips/{tripId}", new { isListed = true });
        edit.StatusCode.Should().Be(HttpStatusCode.NoContent, await edit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_create_applies_the_founding_free_departure_once()
    {
        var (_, _, hostId) = await HostWithPayoutAsync("Founding Built Host");
        var admin = await AdminAsync();

        await SetFoundingAsync(hostId, true);
        try
        {
            var first = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", HostedTripBody($"Kigali-{Guid.NewGuid():N}"));
            first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
            var firstId = (await first.ReadJsonAsync()).GetProperty("id").GetGuid();

            var second = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", HostedTripBody($"Kigali-{Guid.NewGuid():N}"));
            second.StatusCode.Should().Be(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
            var secondId = (await second.ReadJsonAsync()).GetProperty("id").GetGuid();

            var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().FirstAsync(h => h.Id == hostId));
            host.FoundingFreeDepartureTripId.Should().Be(firstId);
            (await WithDbAsync(db => db.TripPosts.AsNoTracking().FirstAsync(t => t.Id == firstId))).IsFoundingFreeDeparture.Should().BeTrue();
            (await WithDbAsync(db => db.TripPosts.AsNoTracking().FirstAsync(t => t.Id == secondId))).IsFoundingFreeDeparture.Should().BeFalse();
        }
        finally
        {
            await SetFoundingAsync(hostId, false);
        }
    }

    [Fact]
    public async Task Admin_create_is_409_without_payout_account_and_404_for_unknown_host()
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), "No Payout Host");
        var profile = await CreateProfileAsync(_factory.CreateClient().Authenticated(user), "No Payout Host");
        var hostId = profile.GetProperty("id").GetGuid();
        var admin = await AdminAsync();

        var noPayout = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", HostedTripBody("Accra"));
        noPayout.StatusCode.Should().Be(HttpStatusCode.Conflict, await noPayout.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.TripPosts.CountAsync(t => t.HostProfileId == hostId))).Should().Be(0);

        var unknown = await admin.PostAsJsonAsync($"/api/admin/hosts/{Guid.NewGuid()}/trips", HostedTripBody("Accra"));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_create_uses_the_same_validation_as_hosts()
    {
        var (_, _, hostId) = await HostWithPayoutAsync("Validation Built Host");
        var admin = await AdminAsync();

        var noTerms = HostedTripBody("Lagos");
        noTerms["termsText"] = null;
        var r1 = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", noTerms);
        r1.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r1.Content.ReadAsStringAsync()).Should().Contain("written terms");

        // Instalments that don't add up: the InstalmentValidator problem details.
        var badSchedule = HostedTripBody("Lagos");
        badSchedule["depositKobo"] = Deposit + 1;
        var r2 = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", badSchedule);
        r2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r2.ReadJsonAsync()).TryGetProperty("errors", out _).Should().BeTrue();

        // Explicitly community is refused: admin-built trips are hosted.
        var community = HostedTripBody("Lagos");
        community["isHostedTrip"] = false;
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", community)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Someone else's private wishlist (or a missing one) is not found for the host.
        var wishlist = HostedTripBody("Lagos");
        wishlist.Remove("freeTextDestination");
        wishlist["wishlistId"] = Guid.NewGuid();
        var r3 = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", wishlist);
        r3.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r3.Content.ReadAsStringAsync()).Should().Contain("Wishlist not found");

        (await WithDbAsync(db => db.TripPosts.CountAsync(t => t.HostProfileId == hostId))).Should().Be(0);
    }

    [Fact]
    public async Task Non_admin_cannot_create_a_trip_for_a_host()
    {
        var (hostClient, _, hostId) = await HostWithPayoutAsync("Self Serve Host");
        var response = await hostClient.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", HostedTripBody("Abuja"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", HostedTripBody("Abuja")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
