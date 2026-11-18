using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Controllers;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Trips;

/// <summary>
/// Phase 3: community vs hosted trips on /api/trips (create/edit rules, type
/// changes, edit locks, listing, approval, withdraw), the DTO, the share
/// preview and the page-view counter.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HostedTripTests
{
    private const long Price = 25_000_000;   // ₦250,000
    private const long Deposit = 5_000_000;  // ₦50,000
    private const long Instalment = 10_000_000;

    private readonly HodracApiFactory _factory;

    public HostedTripTests(HodracApiFactory factory) => _factory = factory;

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

    /// <summary>Host profile + payout account (fake Paystack) + verified by an admin.</summary>
    private async Task<(HttpClient Client, TestUser User, Guid HostId)> VerifiedHostAsync(string name)
    {
        var (client, user) = await UserAsync(name);
        var hostId = await CreatePendingHostAsync(client, name);
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        var verify = await _factory.CreateClient().Authenticated(admin).PostAsync($"/api/admin/hosts/{hostId}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        return (client, user, hostId);
    }

    private static object HostedTripBody(string place, int maxGroupSize = 5, bool? isListed = null, object? overrides = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["freeTextDestination"] = place,
            ["startDate"] = Day(90),
            ["endDate"] = Day(95),
            ["isDateFlexible"] = false,
            ["caption"] = "Zanzibar with the crew",
            ["maxGroupSize"] = maxGroupSize,
            ["isHostedTrip"] = true,
            ["pricePerSeatKobo"] = Price,
            ["depositKobo"] = Deposit,
            ["instalments"] = new[]
            {
                new { sequence = 1, amountKobo = Instalment, dueDate = Day(30) },
                new { sequence = 2, amountKobo = Instalment, dueDate = Day(60) },
            },
            ["termsText"] = "Deposit is non-refundable. Instalments refundable until 30 days before.",
            ["inclusions"] = "Flights, hotel",
            ["exclusions"] = "Visa",
        };
        if (isListed is not null) body["isListed"] = isListed;
        if (overrides is not null)
            foreach (var p in overrides.GetType().GetProperties()) body[p.Name] = p.GetValue(overrides);
        return body;
    }

    private static object CommunityTripBody(string place, int maxGroupSize = 6) => new
    {
        freeTextDestination = place,
        startDate = Day(60),
        endDate = Day(65),
        isDateFlexible = false,
        caption = "Weekend away",
        maxGroupSize,
    };

    private static async Task<JsonElement> CreateTripAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/trips", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    private static async Task<Guid> JoinAsync(HttpClient client, Guid tripId)
    {
        var join = await client.PostAsJsonAsync($"/api/trips/{tripId}/interests", new { message = "Me please" });
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
        return (await join.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient author, Guid tripId, Guid interestId) =>
        author.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve = true });

    private Task<bool> InThreadAsync(Guid tripId, string userId) => WithDbAsync(db =>
        db.TripThreadParticipants.AnyAsync(p => p.UserId == Guid.Parse(userId)
            && db.TripThreads.Any(t => t.Id == p.TripThreadId && t.TripPostId == tripId)));

    private Task InsertPaymentAsync(Guid tripId, Guid interestId, string payerUserId, SeatPaymentStatus status) =>
        WithDbAsync(async db =>
        {
            db.SeatPayments.Add(new SeatPayment
            {
                TripPostId = tripId,
                TripInterestId = interestId,
                PayerUserId = Guid.Parse(payerUserId),
                Reference = PaymentReferenceForTest(),
                Kind = SeatPaymentKind.Deposit,
                SeatAmountKobo = Deposit,
                TotalChargedKobo = Deposit,
                Status = status,
                PaidAt = status == SeatPaymentStatus.Success ? DateTime.UtcNow : null,
            });
            return await db.SaveChangesAsync();
        });

    /// <summary>
    /// Marks a host founding directly in the DB (not via the admin endpoint) for
    /// the duration of a test, then unmarks it, so the admin founding-limit test
    /// (FoundingHostLimit = 2) always finds its slots free. Integration tests
    /// in the collection run one at a time.
    /// </summary>
    private async Task AsFoundingHostAsync(Guid hostId, Func<Task> test)
    {
        await SetFoundingAsync(hostId, true);
        try { await test(); }
        finally { await SetFoundingAsync(hostId, false); }
    }

    private Task<int> SetFoundingAsync(Guid hostId, bool founding) =>
        WithDbAsync(db => db.HostProfiles.Where(h => h.Id == hostId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.IsFoundingHost, founding)));

    private static string PaymentReferenceForTest() => $"HDR-TEST-{Guid.NewGuid():N}"[..30];

    private async Task<List<Guid>> BrowseIdsAsync(HttpClient client, string place)
    {
        var response = await client.GetAsync($"/api/trips/browse?destination={Uri.EscapeDataString(place)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.ReadJsonAsync()).EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();
    }

    private async Task<List<Guid>> PublicListIdsAsync(string place)
    {
        var response = await _factory.CreateClient().GetAsync($"/api/trips/public?destination={Uri.EscapeDataString(place)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .Select(t => t.GetProperty("id").GetGuid()).ToList();
    }

    // ── Community trips ─────────────────────────────────────────────────────

    [Fact]
    public async Task Community_trip_is_listed_everywhere_and_has_no_hosted_fields()
    {
        var (author, _) = await UserAsync("Community Author");
        var (viewer, _) = await UserAsync("Community Viewer");
        var place = $"Comm-{Guid.NewGuid():N}";

        var trip = await CreateTripAsync(author, CommunityTripBody(place));
        var id = trip.GetProperty("id").GetGuid();

        trip.GetProperty("tripType").GetString().Should().Be("community");
        trip.GetProperty("isHostedTrip").GetBoolean().Should().BeFalse();
        trip.GetProperty("isListed").GetBoolean().Should().BeTrue();
        foreach (var field in new[] { "pricePerSeatKobo", "currency", "depositKobo", "instalments", "feeMode",
                     "travellerFeePreviewKobo", "seatsLeft", "inclusions", "exclusions", "termsVersion", "host" })
            trip.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null, field);

        (await BrowseIdsAsync(viewer, place)).Should().Contain(id);
        (await PublicListIdsAsync(place)).Should().Contain(id);

        var single = await _factory.CreateClient().GetAsync($"/api/trips/public/{id}");
        single.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicDto = await single.ReadJsonAsync();
        publicDto.GetProperty("tripType").GetString().Should().Be("community");
        publicDto.GetProperty("seatsLeft").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Community_approval_adds_to_thread_immediately_without_payment_or_hold()
    {
        var (author, _) = await UserAsync("Comm Approver");
        var (joiner, joinerUser) = await UserAsync("Comm Joiner");
        var trip = await CreateTripAsync(author, CommunityTripBody($"CommJoin-{Guid.NewGuid():N}"));
        var tripId = trip.GetProperty("id").GetGuid();

        var interestId = await JoinAsync(joiner, tripId);
        (await ApproveAsync(author, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await InThreadAsync(tripId, joinerUser.UserId)).Should().BeTrue();
        var interest = await WithDbAsync(db => db.TripInterests.AsNoTracking().SingleAsync(i => i.Id == interestId));
        interest.Status.Should().Be(TripInterestStatus.Approved);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.NotRequired);
        interest.SeatHoldExpiresAt.Should().BeNull();
    }

    [Theory]
    [InlineData("pricePerSeatKobo", 100000L)]
    [InlineData("depositKobo", 100000L)]
    [InlineData("termsText", "No refunds")]
    [InlineData("feeMode", 1)]
    [InlineData("seatHoldHours", 24)]
    [InlineData("inclusions", "Hotel")]
    public async Task Hosted_fields_on_a_community_trip_are_rejected(string field, object value)
    {
        var (author, _) = await UserAsync("Comm Sneaky");
        var body = new Dictionary<string, object?>
        {
            ["freeTextDestination"] = "Somewhere",
            ["caption"] = "Trip",
            ["isDateFlexible"] = true,
            [field] = value,
        };
        var response = await author.PostAsJsonAsync("/api/trips", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(TripsController.HostedFieldsOnCommunityMessage);

        // Same on PATCH of an existing community trip.
        var trip = await CreateTripAsync(author, CommunityTripBody($"CommPatch-{Guid.NewGuid():N}"));
        var patch = await author.PatchAsJsonAsync($"/api/trips/{trip.GetProperty("id").GetGuid()}",
            new Dictionary<string, object?> { [field] = value });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await patch.Content.ReadAsStringAsync()).Should().Contain(TripsController.HostedFieldsOnCommunityMessage);
    }

    [Fact]
    public async Task Community_trip_cannot_be_unlisted()
    {
        var (author, _) = await UserAsync("Comm Unlister");
        var place = $"CommUnlist-{Guid.NewGuid():N}";

        var create = await author.PostAsJsonAsync("/api/trips",
            new { freeTextDestination = place, caption = "x", isDateFlexible = true, isListed = false });
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await create.Content.ReadAsStringAsync()).Should().Contain(TripsController.CommunityUnlistedMessage);

        var trip = await CreateTripAsync(author, CommunityTripBody(place));
        var id = trip.GetProperty("id").GetGuid();
        (await author.PatchAsJsonAsync($"/api/trips/{id}", new { isListed = false }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // isListed = true (a no-op) is fine.
        (await author.PatchAsJsonAsync($"/api/trips/{id}", new { isListed = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == id))).IsListed.Should().BeTrue();
    }

    // ── Hosted create ───────────────────────────────────────────────────────

    [Fact]
    public async Task Hosted_trip_needs_a_host_profile_with_a_payout_account()
    {
        var (noProfile, _) = await UserAsync("Not A Host");
        var r1 = await noProfile.PostAsJsonAsync("/api/trips", HostedTripBody("Nowhere"));
        r1.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r1.Content.ReadAsStringAsync()).Should().Contain("host profile");

        var (noPayout, _) = await UserAsync("Host Without Payout");
        await CreateProfileAsync(noPayout, "Host Without Payout");
        var r2 = await noPayout.PostAsJsonAsync("/api/trips", HostedTripBody("Nowhere"));
        r2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r2.Content.ReadAsStringAsync()).Should().Contain("payout account");
    }

    [Fact]
    public async Task Unverified_host_with_payout_can_create_a_hosted_trip()
    {
        var (client, _) = await UserAsync("Pending Host Creator");
        await CreatePendingHostAsync(client, "Pending Host Creator");

        var trip = await CreateTripAsync(client, HostedTripBody($"Pending-{Guid.NewGuid():N}"));
        trip.GetProperty("host").GetProperty("verified").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Hosted_trip_is_private_by_default_but_resolves_by_link()
    {
        var (host, _, hostId) = await VerifiedHostAsync("Private Host");
        var (viewer, _) = await UserAsync("Link Viewer");
        var place = $"Private-{Guid.NewGuid():N}";

        var response = await host.PostAsJsonAsync("/api/trips", HostedTripBody(place));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var raw = await response.Content.ReadAsStringAsync();
        var trip = JsonDocument.Parse(raw).RootElement;
        var id = trip.GetProperty("id").GetGuid();

        trip.GetProperty("tripType").GetString().Should().Be("hosted");
        trip.GetProperty("isHostedTrip").GetBoolean().Should().BeTrue();
        trip.GetProperty("isListed").GetBoolean().Should().BeFalse();
        trip.GetProperty("pricePerSeatKobo").GetInt64().Should().Be(Price);
        trip.GetProperty("depositKobo").GetInt64().Should().Be(Deposit);
        trip.GetProperty("currency").GetString().Should().Be("NGN");
        trip.GetProperty("feeMode").GetString().Should().Be("PassToTraveller");
        trip.GetProperty("instalments").GetArrayLength().Should().Be(2);
        trip.GetProperty("termsVersion").GetInt32().Should().Be(1);
        trip.GetProperty("seatsLeft").GetInt32().Should().Be(4);
        trip.GetProperty("inclusions").GetString().Should().Be("Flights, hotel");
        // 2.5% of the full seat price, passed to the traveller.
        trip.GetProperty("travellerFeePreviewKobo").GetInt64().Should().Be(625_000);
        var hostBadge = trip.GetProperty("host");
        hostBadge.GetProperty("displayName").GetString().Should().Be("Private Host");
        hostBadge.GetProperty("verified").GetBoolean().Should().BeTrue();

        // Never payout / verification data.
        raw.Should().NotContain("ACCT_");
        raw.Should().NotContainAny("subaccount", "Subaccount", "settlement", "Settlement", "cacNumber", "idDocument");

        var saved = await WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == id));
        saved.HostProfileId.Should().Be(hostId);
        saved.SeatHoldHours.Should().Be(48);
        saved.TermsUpdatedAt.Should().NotBeNull();

        // Not in browse / public list...
        (await BrowseIdsAsync(viewer, place)).Should().NotContain(id);
        (await PublicListIdsAsync(place)).Should().NotContain(id);

        // ...but link-only access works, logged in or out.
        (await viewer.GetAsync($"/api/trips/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var pub = await _factory.CreateClient().GetAsync($"/api/trips/public/{id}");
        pub.StatusCode.Should().Be(HttpStatusCode.OK);
        var pubRaw = await pub.Content.ReadAsStringAsync();
        pubRaw.Should().NotContain("ACCT_");
        var pubDto = JsonDocument.Parse(pubRaw).RootElement;
        pubDto.GetProperty("tripType").GetString().Should().Be("hosted");
        pubDto.GetProperty("seatsLeft").GetInt32().Should().Be(4);
        pubDto.GetProperty("host").GetProperty("verified").GetBoolean().Should().BeTrue();

        // Listing it puts it in browse.
        (await host.PatchAsJsonAsync($"/api/trips/{id}", new { isListed = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await BrowseIdsAsync(viewer, place)).Should().Contain(id);
        (await PublicListIdsAsync(place)).Should().Contain(id);
    }

    [Fact]
    public async Task Hosted_trip_with_mismatched_schedule_or_missing_terms_is_rejected()
    {
        var (host, _, _) = await VerifiedHostAsync("Sloppy Host");

        var mismatch = await host.PostAsJsonAsync("/api/trips",
            HostedTripBody("Mismatch", overrides: new { depositKobo = 4_000_000L }));
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mismatch.Content.ReadAsStringAsync()).Should().Contain("add up to the price");

        var noTerms = await host.PostAsJsonAsync("/api/trips", HostedTripBody("NoTerms", overrides: new { termsText = (string?)null }));
        noTerms.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noSize = await host.PostAsJsonAsync("/api/trips", HostedTripBody("NoSize", overrides: new { maxGroupSize = (int?)null }));
        noSize.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badHold = await host.PostAsJsonAsync("/api/trips", HostedTripBody("BadHold", overrides: new { seatHoldHours = 0 }));
        badHold.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Hosted approval / capacity / withdraw ───────────────────────────────

    [Fact]
    public async Task Hosted_approval_holds_the_seat_and_does_not_add_to_thread()
    {
        var (host, _, _) = await VerifiedHostAsync("Approving Host");
        var (traveller, travellerUser) = await UserAsync("Hosted Traveller");
        var trip = await CreateTripAsync(host, HostedTripBody($"Approve-{Guid.NewGuid():N}", overrides: new { seatHoldHours = 24 }));
        var tripId = trip.GetProperty("id").GetGuid();

        var interestId = await JoinAsync(traveller, tripId);
        (await ApproveAsync(host, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var interest = await WithDbAsync(db => db.TripInterests.AsNoTracking().SingleAsync(i => i.Id == interestId));
        interest.Status.Should().Be(TripInterestStatus.Approved);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);
        interest.SeatHoldExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(2));
        (await InThreadAsync(tripId, travellerUser.UserId)).Should().BeFalse();

        var interests = await (await host.GetAsync($"/api/trips/{tripId}/interests")).ReadJsonAsync();
        interests.EnumerateArray().Single().GetProperty("paymentStatus").GetString().Should().Be("AwaitingDeposit");
    }

    [Fact]
    public async Task Capacity_ignores_expired_seat_holds()
    {
        var (host, _, _) = await VerifiedHostAsync("Capacity Host");
        var (a, _) = await UserAsync("Traveller A");
        var (b, _) = await UserAsync("Traveller B");
        // Author + 1 seat.
        var trip = await CreateTripAsync(host, HostedTripBody($"Cap-{Guid.NewGuid():N}", maxGroupSize: 2));
        var tripId = trip.GetProperty("id").GetGuid();

        var ia = await JoinAsync(a, tripId);
        var ib = await JoinAsync(b, tripId);
        (await ApproveAsync(host, tripId, ia)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ApproveAsync(host, tripId, ib)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await (await host.GetAsync($"/api/trips/{tripId}")).ReadJsonAsync()).GetProperty("seatsLeft").GetInt32().Should().Be(0);

        await WithDbAsync(db => db.TripInterests.Where(i => i.Id == ia)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PaymentStatus, InterestPaymentStatus.Expired)));

        (await (await host.GetAsync($"/api/trips/{tripId}")).ReadJsonAsync()).GetProperty("seatsLeft").GetInt32().Should().Be(1);
        (await ApproveAsync(host, tripId, ib)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Withdrawing_a_paid_hosted_seat_keeps_payments_and_notifies_host()
    {
        var (host, hostUser, _) = await VerifiedHostAsync("Withdraw Host");
        var (traveller, travellerUser) = await UserAsync("Withdrawing Traveller");
        var trip = await CreateTripAsync(host, HostedTripBody($"Withdraw-{Guid.NewGuid():N}"));
        var tripId = trip.GetProperty("id").GetGuid();
        var interestId = await JoinAsync(traveller, tripId);
        (await ApproveAsync(host, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await InsertPaymentAsync(tripId, interestId, travellerUser.UserId, SeatPaymentStatus.Success);

        (await traveller.DeleteAsync($"/api/trips/{tripId}/interests/{interestId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var interest = await WithDbAsync(db => db.TripInterests.AsNoTracking().SingleAsync(i => i.Id == interestId));
        interest.Status.Should().Be(TripInterestStatus.Withdrawn);
        interest.SeatHoldExpiresAt.Should().BeNull();
        (await WithDbAsync(db => db.SeatPayments.CountAsync(p => p.TripInterestId == interestId))).Should().Be(1);
        (await WithDbAsync(db => db.Notifications.AnyAsync(n =>
            n.RecipientUserId == Guid.Parse(hostUser.UserId)
            && n.Type == NotificationType.HostedSeatWithdrawn
            && n.TripPostId == tripId))).Should().BeTrue();
    }

    // ── Type changes ────────────────────────────────────────────────────────

    [Fact]
    public async Task Community_to_hosted_is_blocked_once_anyone_is_approved()
    {
        var (host, _, _) = await VerifiedHostAsync("Converting Host Blocked");
        var (joiner, _) = await UserAsync("Early Joiner");
        var trip = await CreateTripAsync(host, CommunityTripBody($"ToHostedBlocked-{Guid.NewGuid():N}"));
        var tripId = trip.GetProperty("id").GetGuid();
        var interestId = await JoinAsync(joiner, tripId);
        (await ApproveAsync(host, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var patch = await host.PatchAsJsonAsync($"/api/trips/{tripId}", HostedPatch());
        patch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == tripId))).IsHostedTrip.Should().BeFalse();
    }

    private static object HostedPatch() => new
    {
        isHostedTrip = true,
        pricePerSeatKobo = Price,
        depositKobo = Deposit,
        instalments = new[]
        {
            new { sequence = 1, amountKobo = Instalment * 2, dueDate = Day(30) },
        },
        termsText = "Deposit non-refundable.",
    };

    [Fact]
    public async Task Community_to_hosted_with_pending_requests_succeeds_and_follows_hosted_flow()
    {
        var (host, _, hostId) = await VerifiedHostAsync("Converting Host");
        var (joiner, joinerUser) = await UserAsync("Pending Joiner");
        var place = $"ToHosted-{Guid.NewGuid():N}";
        var trip = await CreateTripAsync(host, CommunityTripBody(place));
        var tripId = trip.GetProperty("id").GetGuid();
        var interestId = await JoinAsync(joiner, tripId);

        // Without a payout profile it's a 400 (different user, no host profile).
        var (stranger, _) = await UserAsync("Not Host Converter");
        var other = await CreateTripAsync(stranger, CommunityTripBody($"Other-{Guid.NewGuid():N}"));
        (await stranger.PatchAsJsonAsync($"/api/trips/{other.GetProperty("id").GetGuid()}", HostedPatch()))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Missing payment setup is a 400.
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { isHostedTrip = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var patch = await host.PatchAsJsonAsync($"/api/trips/{tripId}", HostedPatch());
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent, await patch.Content.ReadAsStringAsync());

        var saved = await WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == tripId));
        saved.IsHostedTrip.Should().BeTrue();
        saved.HostProfileId.Should().Be(hostId);
        saved.IsListed.Should().BeFalse();
        saved.Instalments.Should().ContainSingle();
        saved.TermsVersion.Should().Be(1);

        (await ApproveAsync(host, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.TripInterests.AsNoTracking().SingleAsync(i => i.Id == interestId)))
            .PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);
        (await InThreadAsync(tripId, joinerUser.UserId)).Should().BeFalse();
    }

    [Fact]
    public async Task Hosted_to_community_is_blocked_after_a_successful_payment()
    {
        var (host, _, _) = await VerifiedHostAsync("Paid Host");
        var (traveller, travellerUser) = await UserAsync("Paying Traveller");
        var trip = await CreateTripAsync(host, HostedTripBody($"ToCommBlocked-{Guid.NewGuid():N}"));
        var tripId = trip.GetProperty("id").GetGuid();
        var interestId = await JoinAsync(traveller, tripId);
        (await ApproveAsync(host, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await InsertPaymentAsync(tripId, interestId, travellerUser.UserId, SeatPaymentStatus.Success);

        var patch = await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { isHostedTrip = false });
        patch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == tripId))).IsHostedTrip.Should().BeTrue();
    }

    [Fact]
    public async Task Hosted_to_community_clears_hosted_fields_and_moves_approved_into_the_chat()
    {
        var (host, _, hostId) = await VerifiedHostAsync("Reverting Host");
        await AsFoundingHostAsync(hostId, () => RevertToCommunityAsync(host, hostId));
    }

    private async Task RevertToCommunityAsync(HttpClient host, Guid hostId)
    {
        var (traveller, travellerUser) = await UserAsync("Held Traveller");
        var place = $"ToComm-{Guid.NewGuid():N}";
        var trip = await CreateTripAsync(host, HostedTripBody(place));
        var tripId = trip.GetProperty("id").GetGuid();
        var interestId = await JoinAsync(traveller, tripId);
        (await ApproveAsync(host, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId)))
            .FoundingFreeDepartureTripId.Should().Be(tripId);

        // Hosted fields alongside the switch back are still rejected.
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { isHostedTrip = false, pricePerSeatKobo = Price }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var patch = await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { isHostedTrip = false });
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent, await patch.Content.ReadAsStringAsync());

        var saved = await WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == tripId));
        saved.IsHostedTrip.Should().BeFalse();
        saved.IsListed.Should().BeTrue();
        saved.HostProfileId.Should().BeNull();
        saved.PricePerSeatKobo.Should().BeNull();
        saved.DepositKobo.Should().BeNull();
        saved.Instalments.Should().BeEmpty();
        saved.TermsText.Should().BeNull();
        saved.Inclusions.Should().BeNull();
        saved.IsFoundingFreeDeparture.Should().BeFalse();
        (await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId)))
            .FoundingFreeDepartureTripId.Should().BeNull();

        var interest = await WithDbAsync(db => db.TripInterests.AsNoTracking().SingleAsync(i => i.Id == interestId));
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.NotRequired);
        interest.SeatHoldExpiresAt.Should().BeNull();
        (await InThreadAsync(tripId, travellerUser.UserId)).Should().BeTrue();

        var dto = await (await host.GetAsync($"/api/trips/{tripId}")).ReadJsonAsync();
        dto.GetProperty("tripType").GetString().Should().Be("community");
        dto.GetProperty("pricePerSeatKobo").ValueKind.Should().Be(JsonValueKind.Null);
        (await BrowseIdsAsync(traveller, place)).Should().Contain(tripId);
    }

    // ── Edit locks and terms ────────────────────────────────────────────────

    [Fact]
    public async Task Money_terms_lock_after_a_successful_payment_but_terms_text_can_change()
    {
        var (host, _, _) = await VerifiedHostAsync("Locked Host");
        var (traveller, travellerUser) = await UserAsync("Locking Traveller");
        var trip = await CreateTripAsync(host, HostedTripBody($"Lock-{Guid.NewGuid():N}"));
        var tripId = trip.GetProperty("id").GetGuid();

        // Before any payment the price can move (with a matching schedule).
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { pricePerSeatKobo = Price + 100, depositKobo = Deposit + 100 }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        // A schedule that no longer adds up is a 400.
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { depositKobo = Deposit }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var interestId = await JoinAsync(traveller, tripId);
        (await ApproveAsync(host, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await InsertPaymentAsync(tripId, interestId, travellerUser.UserId, SeatPaymentStatus.Success);

        foreach (object body in new object[]
                 {
                     new { pricePerSeatKobo = Price + 200, depositKobo = Deposit + 200 },
                     new { depositKobo = Deposit + 50 },
                     new { feeMode = 1 },
                     new { instalments = new[] { new { sequence = 1, amountKobo = Instalment * 2, dueDate = Day(30) } } },
                 })
        {
            var locked = await host.PatchAsJsonAsync($"/api/trips/{tripId}", body);
            locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await locked.Content.ReadAsStringAsync()).Should().Contain("once a traveller has paid");
        }

        // Re-sending the same price is not a change.
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { pricePerSeatKobo = Price + 100, caption = "Same price" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var terms = await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { termsText = "Updated terms after payment." });
        terms.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var saved = await WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == tripId));
        saved.TermsVersion.Should().Be(2);
        saved.TermsText.Should().Be("Updated terms after payment.");
        saved.PricePerSeatKobo.Should().Be(Price + 100);
    }

    [Fact]
    public async Task Every_terms_change_bumps_the_version()
    {
        var (host, _, _) = await VerifiedHostAsync("Terms Host");
        var trip = await CreateTripAsync(host, HostedTripBody($"Terms-{Guid.NewGuid():N}"));
        var tripId = trip.GetProperty("id").GetGuid();

        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { termsText = "v2 terms" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { termsText = "v2 terms" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { termsText = "v3 terms" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { termsText = "  " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var dto = await (await host.GetAsync($"/api/trips/{tripId}")).ReadJsonAsync();
        dto.GetProperty("termsVersion").GetInt32().Should().Be(3);
    }

    // ── Founding free departure ─────────────────────────────────────────────

    [Fact]
    public async Task Founding_free_departure_goes_to_the_first_hosted_trip_only()
    {
        var (host, _, hostId) = await VerifiedHostAsync("Founding Host");
        await AsFoundingHostAsync(hostId, () => FoundingDepartureAsync(host, hostId));
    }

    private async Task FoundingDepartureAsync(HttpClient host, Guid hostId)
    {

        var community = await CreateTripAsync(host, CommunityTripBody($"FoundComm-{Guid.NewGuid():N}"));
        var first = await CreateTripAsync(host, HostedTripBody($"Found1-{Guid.NewGuid():N}"));
        var second = await CreateTripAsync(host, HostedTripBody($"Found2-{Guid.NewGuid():N}"));
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();

        var trips = await WithDbAsync(db => db.TripPosts.AsNoTracking()
            .Where(t => t.Id == firstId || t.Id == secondId || t.Id == community.GetProperty("id").GetGuid())
            .ToDictionaryAsync(t => t.Id, t => t.IsFoundingFreeDeparture));
        trips[firstId].Should().BeTrue();
        trips[secondId].Should().BeFalse();
        trips[community.GetProperty("id").GetGuid()].Should().BeFalse();
        (await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId)))
            .FoundingFreeDepartureTripId.Should().Be(firstId);

        // The free departure carries no Hodrac fee.
        first.GetProperty("travellerFeePreviewKobo").GetInt64().Should().Be(0);
        second.GetProperty("travellerFeePreviewKobo").GetInt64().Should().Be(625_000);
    }

    [Fact]
    public async Task Non_founding_host_never_gets_a_free_departure()
    {
        var (host, _, hostId) = await VerifiedHostAsync("Regular Host");
        var trip = await CreateTripAsync(host, HostedTripBody($"Regular-{Guid.NewGuid():N}", overrides: new { feeMode = "HostAbsorbs" }));
        trip.GetProperty("feeMode").GetString().Should().Be("HostAbsorbs");
        trip.GetProperty("travellerFeePreviewKobo").GetInt64().Should().Be(0);
        (await WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == trip.GetProperty("id").GetGuid())))
            .IsFoundingFreeDeparture.Should().BeFalse();
        (await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId)))
            .FoundingFreeDepartureTripId.Should().BeNull();
    }

    // ── Share preview + page views ──────────────────────────────────────────

    [Fact]
    public async Task Share_preview_for_an_unlisted_hosted_trip_shows_price_seats_and_badge()
    {
        var (host, _, _) = await VerifiedHostAsync("Share Host");
        var (traveller, _) = await UserAsync("Share Traveller");
        var trip = await CreateTripAsync(host, HostedTripBody($"Share-{Guid.NewGuid():N}", maxGroupSize: 5));
        var tripId = trip.GetProperty("id").GetGuid();
        var interestId = await JoinAsync(traveller, tripId);
        (await ApproveAsync(host, tripId, interestId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var share = await _factory.CreateClient().GetAsync($"/api/share/trip/{tripId}");
        share.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = (await share.ReadJsonAsync()).GetProperty("text").GetString();
        text.Should().EndWith("₦250,000/seat · 3 seats left · Verified host");

        // Unverified host: no badge.
        var (pendingClient, _) = await UserAsync("Share Pending Host");
        await CreatePendingHostAsync(pendingClient, "Share Pending Host");
        var pendingTrip = await CreateTripAsync(pendingClient, HostedTripBody($"SharePending-{Guid.NewGuid():N}",
            overrides: new { pricePerSeatKobo = Price + 50, depositKobo = Deposit + 50 }));
        var pendingText = (await (await _factory.CreateClient()
            .GetAsync($"/api/share/trip/{pendingTrip.GetProperty("id").GetGuid()}")).ReadJsonAsync()).GetProperty("text").GetString();
        pendingText.Should().EndWith("₦250,000.50/seat · 4 seats left");
    }

    [Fact]
    public async Task Page_views_are_counted_for_people_but_not_bots()
    {
        var (host, _, _) = await VerifiedHostAsync("Views Host");
        var trip = await CreateTripAsync(host, HostedTripBody($"Views-{Guid.NewGuid():N}"));
        var tripId = trip.GetProperty("id").GetGuid();

        async Task<HttpStatusCode> GetAs(string url, string userAgent)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            return (await _factory.CreateClient().SendAsync(request)).StatusCode;
        }

        const string browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0 Safari/537.36";
        const string bot = "WhatsApp/2.23.20.0 A";

        (await GetAs($"/api/trips/public/{tripId}", browser)).Should().Be(HttpStatusCode.OK);
        (await GetAs($"/api/trips/public/{tripId}", browser)).Should().Be(HttpStatusCode.OK);
        (await GetAs($"/api/trips/public/{tripId}", bot)).Should().Be(HttpStatusCode.OK);
        (await GetAs($"/share/trip/{tripId}", browser)).Should().Be(HttpStatusCode.OK);
        (await GetAs($"/share/trip/{tripId}", bot)).Should().Be(HttpStatusCode.OK);

        var counts = await WithDbAsync(db => db.TripPageViewsDaily.AsNoTracking()
            .Where(v => v.TripPostId == tripId)
            .ToDictionaryAsync(v => v.Source, v => v.Count));
        counts.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            [TripPageViewSources.PublicApi] = 2,
            [TripPageViewSources.Share] = 1,
        });
    }
}
