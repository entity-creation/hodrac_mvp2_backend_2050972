using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Jobs;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Trips;

/// <summary>
/// After an unpaid seat hold expires: the host re-approves (or declines) the
/// expired seat, or the traveller sends the request again; plus the caller's
/// own payment state (my* fields) on the trip DTO.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SeatHoldRecoveryTests
{
    private const long Price = 25_000_000;   // ₦250,000
    private const long Deposit = 5_000_000;  // ₦50,000
    private const long Instalment = 10_000_000;
    private const long DepositFee = 125_000; // 2.5% of the deposit

    private readonly HodracApiFactory _factory;

    public SeatHoldRecoveryTests(HodracApiFactory factory) => _factory = factory;

    private record Seat(
        HttpClient Host, TestUser HostUser,
        HttpClient Traveller, TestUser TravellerUser,
        Guid TripId, Guid InterestId, string Place);

    private static DateOnly Day(int daysFromNow) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysFromNow));

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    private async Task<(HttpClient Client, TestUser User)> UserAsync(string name)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), name);
        return (_factory.CreateClient().Authenticated(user), user);
    }

    private async Task<(HttpClient Client, TestUser User)> VerifiedHostAsync(string name)
    {
        var (client, user) = await UserAsync(name);
        var hostId = await CreatePendingHostAsync(client, name);
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        var verify = await _factory.CreateClient().Authenticated(admin).PostAsync($"/api/admin/hosts/{hostId}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        return (client, user);
    }

    private static object HostedTripBody(string place, int maxGroupSize) => new
    {
        freeTextDestination = place,
        startDate = Day(90),
        endDate = Day(95),
        isDateFlexible = false,
        caption = "Zanzibar with the crew",
        maxGroupSize,
        isHostedTrip = true,
        isListed = true,
        pricePerSeatKobo = Price,
        depositKobo = Deposit,
        instalments = new[]
        {
            new { sequence = 1, amountKobo = Instalment, dueDate = Day(30) },
            new { sequence = 2, amountKobo = Instalment, dueDate = Day(60) },
        },
        feeMode = "PassToTraveller",
        termsText = "Deposit is non-refundable. Instalments refundable until 30 days before departure.",
    };

    private static async Task<Guid> CreateTripAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/trips", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> JoinAsync(HttpClient client, Guid tripId)
    {
        var join = await client.PostAsJsonAsync($"/api/trips/{tripId}/interests", new { message = "Me please" });
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
        return (await join.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> RespondAsync(HttpClient host, Guid tripId, Guid interestId, bool approve) =>
        host.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve });

    private static Task<HttpResponseMessage> RequestAgainAsync(HttpClient client, Guid tripId, Guid interestId) =>
        client.PostAsync($"/api/trips/{tripId}/interests/{interestId}/request-again", null);

    /// <summary>Verified host, listed hosted trip, traveller approved + terms accepted.</summary>
    private async Task<Seat> ApprovedSeatAsync(int maxGroupSize = 6)
    {
        var (host, hostUser) = await VerifiedHostAsync("Recovery Host");
        var place = $"Recovery-{Guid.NewGuid():N}";
        var tripId = await CreateTripAsync(host, HostedTripBody(place, maxGroupSize));
        var (traveller, travellerUser) = await UserAsync("Recovery Traveller");
        var interestId = await JoinAsync(traveller, tripId);

        (await RespondAsync(host, tripId, interestId, approve: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var accept = await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms", new { termsVersion = 1 });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());

        return new Seat(host, hostUser, traveller, travellerUser, tripId, interestId, place);
    }

    /// <summary>Moves the hold into the past and runs the expiry job on it.</summary>
    private async Task ExpireAsync(Guid tripId, Guid interestId)
    {
        await WithDbAsync(db => db.TripInterests.Where(i => i.Id == interestId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.SeatHoldExpiresAt, DateTime.UtcNow.AddMinutes(-5))));
        using var scope = _factory.Services.CreateScope();
        var expired = await scope.ServiceProvider.GetRequiredService<SeatHoldExpiryJob>()
            .TryExpireAsync(tripId, interestId, DateTime.UtcNow);
        expired.Should().BeTrue();
    }

    private Task<TripInterest> InterestAsync(Guid interestId) =>
        WithDbAsync(db => db.TripInterests.AsNoTracking().FirstAsync(i => i.Id == interestId));

    private Task<int> NotificationCountAsync(Guid tripId, NotificationType type, string recipientUserId) =>
        WithDbAsync(db => db.Notifications.CountAsync(n =>
            n.TripPostId == tripId && n.Type == type && n.RecipientUserId == Guid.Parse(recipientUserId)));

    private static Task<HttpResponseMessage> CheckoutDepositAsync(Seat seat) =>
        seat.Traveller.PostAsJsonAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/checkout", new { kind = "Deposit" });

    // ── Host re-approves ─────────────────────────────────────────────────────

    [Fact]
    public async Task Host_reapproves_an_expired_hold_with_a_new_hold_and_the_traveller_can_check_out()
    {
        var seat = await ApprovedSeatAsync();
        _factory.Emails.Sent.Clear();
        await ExpireAsync(seat.TripId, seat.InterestId);

        // The expiry email offers both ways back.
        _factory.Emails.Sent.Should().Contain(e => e.To == seat.TravellerUser.Email && e.Subject.Contains("has expired")
            && e.BodyLines.Any(l => l.Contains("approve you again") && l.Contains("Request again")));
        (await CheckoutDepositAsync(seat)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var before = DateTime.UtcNow;
        (await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var interest = await InterestAsync(seat.InterestId);
        interest.Status.Should().Be(TripInterestStatus.Approved);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);
        interest.SeatHoldExpiresAt.Should().BeCloseTo(before.AddHours(48), TimeSpan.FromMinutes(1));
        interest.TermsVersionAccepted.Should().Be(1, "the terms didn't change, so the acceptance is kept");
        (await NotificationCountAsync(seat.TripId, NotificationType.InterestApproved, seat.TravellerUser.UserId)).Should().Be(2);

        var checkout = await CheckoutDepositAsync(seat);
        checkout.StatusCode.Should().Be(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync());
        (await checkout.ReadJsonAsync()).GetProperty("totalKobo").GetInt64().Should().Be(Deposit + DepositFee);
    }

    [Fact]
    public async Task Reapproval_clears_a_terms_acceptance_only_when_the_terms_changed()
    {
        var seat = await ApprovedSeatAsync();
        await ExpireAsync(seat.TripId, seat.InterestId);
        (await seat.Host.PatchAsJsonAsync($"/api/trips/{seat.TripId}", new { termsText = "New terms: everything is non-refundable." }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var interest = await InterestAsync(seat.InterestId);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);
        interest.TermsVersionAccepted.Should().BeNull();
        interest.TermsAcceptedAt.Should().BeNull();
        interest.TermsTextSha256.Should().BeNull();
        (await CheckoutDepositAsync(seat)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reapproval_respects_capacity()
    {
        var seat = await ApprovedSeatAsync(maxGroupSize: 2); // author + 1 seat
        await ExpireAsync(seat.TripId, seat.InterestId);

        // The freed seat goes to someone else first.
        var (other, _) = await UserAsync("Other Traveller");
        var otherInterest = await JoinAsync(other, seat.TripId);
        (await RespondAsync(seat.Host, seat.TripId, otherInterest, approve: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: true);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.ReadJsonAsync()).GetProperty("detail").GetString().Should().Contain("full");
        (await InterestAsync(seat.InterestId)).PaymentStatus.Should().Be(InterestPaymentStatus.Expired);
    }

    [Fact]
    public async Task Host_can_decline_an_expired_hold_but_not_reanswer_a_live_one()
    {
        var seat = await ApprovedSeatAsync();

        // A live hold is not re-answerable.
        (await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: true)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: false)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await ExpireAsync(seat.TripId, seat.InterestId);
        (await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: false)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var interest = await InterestAsync(seat.InterestId);
        interest.Status.Should().Be(TripInterestStatus.Declined);
        (await NotificationCountAsync(seat.TripId, NotificationType.InterestDeclined, seat.TravellerUser.UserId)).Should().Be(1);

        // Declined is final for both sides.
        (await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: true)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RequestAgainAsync(seat.Traveller, seat.TripId, seat.InterestId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Traveller asks again ─────────────────────────────────────────────────

    [Fact]
    public async Task Traveller_requests_again_after_expiry_and_host_is_notified()
    {
        var seat = await ApprovedSeatAsync();
        await ExpireAsync(seat.TripId, seat.InterestId);

        var before = DateTime.UtcNow;
        var response = await RequestAgainAsync(seat.Traveller, seat.TripId, seat.InterestId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var dto = await response.ReadJsonAsync();
        dto.GetProperty("id").GetGuid().Should().Be(seat.InterestId);
        dto.GetProperty("status").GetString().Should().Be("Requested");
        dto.GetProperty("paymentStatus").GetString().Should().Be("NotRequired");
        dto.GetProperty("seatHoldExpiresAt").ValueKind.Should().Be(JsonValueKind.Null);

        var interest = await InterestAsync(seat.InterestId);
        interest.Status.Should().Be(TripInterestStatus.Requested);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.NotRequired);
        interest.SeatHoldExpiresAt.Should().BeNull();
        interest.RespondedAt.Should().BeNull();
        interest.RequestedAt.Should().BeOnOrAfter(before.AddSeconds(-1));
        // Join + request again.
        (await NotificationCountAsync(seat.TripId, NotificationType.InterestReceived, seat.HostUser.UserId)).Should().Be(2);

        // Only once: it's a pending request now.
        (await RequestAgainAsync(seat.Traveller, seat.TripId, seat.InterestId)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The host approves it through the normal flow: a fresh hold.
        (await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var approved = await InterestAsync(seat.InterestId);
        approved.PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);
        approved.SeatHoldExpiresAt.Should().BeAfter(DateTime.UtcNow.AddHours(47));
        (await CheckoutDepositAsync(seat)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_again_is_404_for_anyone_but_the_requester_and_409_before_expiry()
    {
        var seat = await ApprovedSeatAsync();
        var (stranger, _) = await UserAsync("Stranger");

        (await RequestAgainAsync(seat.Traveller, seat.TripId, seat.InterestId)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await ExpireAsync(seat.TripId, seat.InterestId);
        (await RequestAgainAsync(stranger, seat.TripId, seat.InterestId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RequestAgainAsync(seat.Host, seat.TripId, seat.InterestId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RequestAgainAsync(seat.Traveller, Guid.NewGuid(), seat.InterestId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await InterestAsync(seat.InterestId)).PaymentStatus.Should().Be(InterestPaymentStatus.Expired);
    }

    [Fact]
    public async Task Request_again_is_404_on_community_trips()
    {
        var (author, _) = await UserAsync("Community Author");
        var tripId = await CreateTripAsync(author, new
        {
            freeTextDestination = $"Comm-{Guid.NewGuid():N}",
            startDate = Day(60),
            endDate = Day(65),
            isDateFlexible = false,
            caption = "Weekend away",
            maxGroupSize = 6,
        });
        var (traveller, _) = await UserAsync("Community Traveller");
        var interestId = await JoinAsync(traveller, tripId);
        (await RespondAsync(author, tripId, interestId, approve: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RequestAgainAsync(traveller, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Withdraw_and_rerequest_still_works_after_expiry()
    {
        var seat = await ApprovedSeatAsync();
        await ExpireAsync(seat.TripId, seat.InterestId);

        (await seat.Traveller.DeleteAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rejoin = await seat.Traveller.PostAsJsonAsync($"/api/trips/{seat.TripId}/interests", new { message = "Back again" });
        rejoin.StatusCode.Should().Be(HttpStatusCode.OK, await rejoin.Content.ReadAsStringAsync());
        (await rejoin.ReadJsonAsync()).GetProperty("status").GetString().Should().Be("Requested");

        (await RespondAsync(seat.Host, seat.TripId, seat.InterestId, approve: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InterestAsync(seat.InterestId)).PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);
    }

    // ── my* fields on the trip DTO ───────────────────────────────────────────

    private static JsonElement FindTrip(JsonElement list, Guid tripId) =>
        list.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == tripId);

    [Fact]
    public async Task Trip_dto_carries_the_callers_own_payment_state()
    {
        var seat = await ApprovedSeatAsync();

        var trip = await (await seat.Traveller.GetAsync($"/api/trips/{seat.TripId}")).ReadJsonAsync();
        trip.GetProperty("myInterestId").GetGuid().Should().Be(seat.InterestId);
        trip.GetProperty("myInterestStatus").GetString().Should().Be("Approved");
        trip.GetProperty("myPaymentStatus").GetString().Should().Be("AwaitingDeposit");
        trip.GetProperty("mySeatHoldExpiresAt").ValueKind.Should().Be(JsonValueKind.String);
        trip.GetProperty("myAmountPaidKobo").GetInt64().Should().Be(0);
        trip.GetProperty("myOwesKobo").GetInt64().Should().Be(Price);
        trip.GetProperty("myTermsVersionAccepted").GetInt32().Should().Be(1);
        var next = trip.GetProperty("myNextPayment");
        next.GetProperty("kind").GetString().Should().Be("Deposit");
        next.GetProperty("sequence").ValueKind.Should().Be(JsonValueKind.Null);
        next.GetProperty("totalKobo").GetInt64().Should().Be(Deposit + DepositFee);
        next.GetProperty("dueDate").ValueKind.Should().Be(JsonValueKind.Null);

        // Same fields on browse and mine/requests.
        var browse = FindTrip(await (await seat.Traveller.GetAsync($"/api/trips/browse?destination={seat.Place}")).ReadJsonAsync(), seat.TripId);
        browse.GetProperty("myPaymentStatus").GetString().Should().Be("AwaitingDeposit");
        browse.GetProperty("myNextPayment").GetProperty("totalKobo").GetInt64().Should().Be(Deposit + DepositFee);
        var requests = await (await seat.Traveller.GetAsync("/api/trips/mine/requests")).ReadJsonAsync();
        var mine = requests.EnumerateArray().Single(r => r.GetProperty("interestId").GetGuid() == seat.InterestId).GetProperty("trip");
        mine.GetProperty("myInterestId").GetGuid().Should().Be(seat.InterestId);
        mine.GetProperty("myOwesKobo").GetInt64().Should().Be(Price);
        mine.GetProperty("myNextPayment").GetProperty("kind").GetString().Should().Be("Deposit");

        // Deposit paid (recorded by the host): the next payment is instalment 1.
        using (var scope = _factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<PaymentService>().RecordManualPaymentAsync(
                seat.TripId, seat.InterestId, Deposit, ManualPaymentMethod.Transfer, null, Guid.Parse(seat.HostUser.UserId));
            result.Outcome.Should().Be(ManualPaymentOutcome.Done);
        }
        var paid = await (await seat.Traveller.GetAsync($"/api/trips/{seat.TripId}")).ReadJsonAsync();
        paid.GetProperty("myPaymentStatus").GetString().Should().Be("DepositPaid");
        paid.GetProperty("mySeatHoldExpiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        paid.GetProperty("myAmountPaidKobo").GetInt64().Should().Be(Deposit);
        paid.GetProperty("myOwesKobo").GetInt64().Should().Be(Price - Deposit);
        var nextPaid = paid.GetProperty("myNextPayment");
        nextPaid.GetProperty("kind").GetString().Should().Be("Instalment");
        nextPaid.GetProperty("sequence").GetInt32().Should().Be(1);
        nextPaid.GetProperty("totalKobo").GetInt64().Should().Be(Instalment + Instalment * 25 / 1000);
        nextPaid.GetProperty("dueDate").GetString().Should().Be(Day(30).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Expired_hold_shows_on_the_dto_without_a_next_payment()
    {
        var seat = await ApprovedSeatAsync();
        await ExpireAsync(seat.TripId, seat.InterestId);

        var trip = await (await seat.Traveller.GetAsync($"/api/trips/{seat.TripId}")).ReadJsonAsync();
        trip.GetProperty("myPaymentStatus").GetString().Should().Be("Expired");
        trip.GetProperty("myOwesKobo").GetInt64().Should().Be(Price);
        trip.GetProperty("myNextPayment").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task My_fields_are_null_for_other_callers_and_absent_from_public_dtos()
    {
        var seat = await ApprovedSeatAsync();
        var (stranger, _) = await UserAsync("Dto Stranger");

        foreach (var client in new[] { stranger, seat.Host })
        {
            var trip = await (await client.GetAsync($"/api/trips/{seat.TripId}")).ReadJsonAsync();
            foreach (var field in new[] { "myInterestId", "myPaymentStatus", "mySeatHoldExpiresAt", "myAmountPaidKobo", "myOwesKobo", "myTermsVersionAccepted", "myNextPayment" })
                trip.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null, field);

            var browse = FindTrip(await (await client.GetAsync($"/api/trips/browse?destination={seat.Place}")).ReadJsonAsync(), seat.TripId);
            browse.GetProperty("myInterestId").ValueKind.Should().Be(JsonValueKind.Null);
            browse.GetProperty("myPaymentStatus").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var anonymous = _factory.CreateClient();
        var publicTrip = await (await anonymous.GetAsync($"/api/trips/public/{seat.TripId}")).ReadJsonAsync();
        publicTrip.TryGetProperty("myInterestId", out _).Should().BeFalse();
        publicTrip.TryGetProperty("myPaymentStatus", out _).Should().BeFalse();
        var publicList = await (await anonymous.GetAsync($"/api/trips/public?destination={seat.Place}")).ReadJsonAsync();
        var item = publicList.GetProperty("items").EnumerateArray().Single();
        item.TryGetProperty("myNextPayment", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Community_trip_dto_has_my_interest_id_but_no_payment_fields()
    {
        var (author, _) = await UserAsync("Community Dto Author");
        var tripId = await CreateTripAsync(author, new
        {
            freeTextDestination = $"CommDto-{Guid.NewGuid():N}",
            startDate = Day(60),
            endDate = Day(65),
            isDateFlexible = false,
            caption = "Weekend away",
            maxGroupSize = 6,
        });
        var (traveller, _) = await UserAsync("Community Dto Traveller");
        var interestId = await JoinAsync(traveller, tripId);

        var trip = await (await traveller.GetAsync($"/api/trips/{tripId}")).ReadJsonAsync();
        trip.GetProperty("myInterestId").GetGuid().Should().Be(interestId);
        trip.GetProperty("myInterestStatus").GetString().Should().Be("Requested");
        trip.GetProperty("myPaymentStatus").ValueKind.Should().Be(JsonValueKind.Null);
        trip.GetProperty("myOwesKobo").ValueKind.Should().Be(JsonValueKind.Null);
        trip.GetProperty("myNextPayment").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
