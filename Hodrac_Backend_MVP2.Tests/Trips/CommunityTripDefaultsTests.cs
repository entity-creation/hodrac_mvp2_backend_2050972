using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hodrac_Backend_MVP2.Tests.Trips;

/// <summary>
/// Migration HodracHostV1 guard: existing trips/interests and today's
/// community-trip flow are unchanged (IsHostedTrip=false, IsListed=true,
/// PaymentStatus=NotRequired, still in browse).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CommunityTripDefaultsTests
{
    private readonly HodracApiFactory _factory;

    public CommunityTripDefaultsTests(HodracApiFactory factory) => _factory = factory;

    private record TripBody(Guid Id, string Caption);

    private async Task<List<TripBody>> BrowseAsync(HttpClient client, string destination)
    {
        var response = await client.GetAsync($"/api/trips/browse?destination={Uri.EscapeDataString(destination)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<List<TripBody>>())!;
    }

    [Fact]
    public async Task Community_trip_created_via_api_is_unhosted_listed_and_in_browse()
    {
        var anon = _factory.CreateClient();
        var author = await AuthHelper.RegisterAsync(anon, "Author");
        var joiner = await AuthHelper.RegisterAsync(anon, "Joiner");
        var place = $"Community-{Guid.NewGuid():N}";

        var authorClient = _factory.CreateClient().Authenticated(author);
        var create = await authorClient.PostAsJsonAsync("/api/trips", new
        {
            freeTextDestination = place,
            startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)),
            endDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(65)),
            isDateFlexible = false,
            caption = "Weekend away",
            maxGroupSize = 6,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var trip = (await create.Content.ReadFromJsonAsync<TripBody>())!;

        var joinerClient = _factory.CreateClient().Authenticated(joiner);
        var join = await joinerClient.PostAsJsonAsync($"/api/trips/{trip.Id}/interests", new { message = "Count me in" });
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            var saved = await db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == trip.Id);

            saved.IsHostedTrip.Should().BeFalse();
            saved.IsListed.Should().BeTrue();
            saved.HostProfileId.Should().BeNull();
            saved.PricePerSeatKobo.Should().BeNull();
            saved.DepositKobo.Should().BeNull();
            saved.Instalments.Should().BeEmpty();
            saved.TermsText.Should().BeNull();
            saved.FeeMode.Should().Be(FeeMode.PassToTraveller);
            saved.IsFoundingFreeDeparture.Should().BeFalse();
            saved.Currency.Should().Be("NGN");

            var interest = await db.TripInterests.AsNoTracking().SingleAsync(i => i.TripPostId == trip.Id);
            interest.PaymentStatus.Should().Be(InterestPaymentStatus.NotRequired);
            interest.SeatHoldExpiresAt.Should().BeNull();
            interest.AmountPaidKobo.Should().Be(0);
        }

        (await BrowseAsync(joinerClient, place)).Select(t => t.Id).Should().Contain(trip.Id);
    }

    [Fact]
    public async Task Rows_written_without_the_new_columns_get_community_defaults_and_stay_in_browse()
    {
        // Simulates rows that existed before HodracHostV1: insert with only the
        // pre-migration columns so the database defaults fill in the rest.
        var anon = _factory.CreateClient();
        var viewer = await AuthHelper.RegisterAsync(anon, "Viewer");
        var tripId = Guid.NewGuid();
        var interestId = Guid.NewGuid();
        var place = $"Legacy-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""TripPosts""
                    (""Id"", ""AuthorUserId"", ""FreeTextDestination"", ""IsDateFlexible"", ""Caption"",
                     ""Status"", ""IsHidden"", ""CreatedAt"", ""UpdatedAt"")
                VALUES ({tripId}, {Guid.NewGuid()}, {place}, false, 'Old trip', 0, false, {now}, {now})");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""TripInterests"" (""Id"", ""TripPostId"", ""RequesterUserId"", ""Status"", ""RequestedAt"")
                VALUES ({interestId}, {tripId}, {Guid.NewGuid()}, 1, {now})");

            var trip = await db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == tripId);
            trip.IsHostedTrip.Should().BeFalse();
            trip.IsListed.Should().BeTrue();
            trip.FeeMode.Should().Be(FeeMode.PassToTraveller);
            trip.Currency.Should().Be("NGN");
            trip.SeatHoldHours.Should().Be(48);
            trip.TermsVersion.Should().Be(1);
            trip.Instalments.Should().BeEmpty();

            var interest = await db.TripInterests.AsNoTracking().SingleAsync(i => i.Id == interestId);
            interest.PaymentStatus.Should().Be(InterestPaymentStatus.NotRequired);
            interest.AmountPaidKobo.Should().Be(0);
            interest.AmountPaidViaHodracKobo.Should().Be(0);
        }

        var client = _factory.CreateClient().Authenticated(viewer);
        (await BrowseAsync(client, place)).Select(t => t.Id).Should().Contain(tripId);
    }

    [Fact]
    public async Task Explicit_false_IsListed_is_persisted()
    {
        // IsListed uses a DB default of true with sentinel true, so a private
        // hosted trip (false) must still be written, not replaced by the default.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();

        var trip = new TripPost { AuthorUserId = Guid.NewGuid(), Caption = "Private", IsListed = false };
        db.TripPosts.Add(trip);
        await db.SaveChangesAsync();

        (await db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == trip.Id)).IsListed.Should().BeFalse();
    }
}
