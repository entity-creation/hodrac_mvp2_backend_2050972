using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

/// <summary>
/// /api/admin/hosts account actions: DELETE {id}/founding (remove founding
/// status), POST {id}/close (close a host account) and DELETE {id} (delete an
/// unused host profile).
///
/// Founding status is set directly in the DB where the endpoint isn't under
/// test, and every test leaves no founding host behind, so the founding-limit
/// test (FoundingHostLimit = 2) always finds its slots free.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AdminHostAccountTests
{
    private const long Price = 25_000_000;
    private const long Deposit = 5_000_000;
    private const long Instalment = 10_000_000;

    private readonly HodracApiFactory _factory;

    public AdminHostAccountTests(HodracApiFactory factory) => _factory = factory;

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    private async Task<(HttpClient Client, TestUser User)> UserAsync(string name)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), name);
        return (_factory.CreateClient().Authenticated(user), user);
    }

    /// <summary>Profile + payout account + ID document, optionally verified by an admin.</summary>
    private async Task<(HttpClient Client, TestUser User, Guid HostId)> HostAsync(string name, bool verified = true)
    {
        var (client, user) = await UserAsync(name);
        var hostId = await CreatePendingHostAsync(client, name);
        if (verified)
        {
            var verify = await (await AdminAsync()).PostAsync($"/api/admin/hosts/{hostId}/verify", null);
            verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        }
        return (client, user, hostId);
    }

    private static object HostedTripBody(string feeMode = "PassToTraveller") => new
    {
        freeTextDestination = $"Account-{Guid.NewGuid():N}",
        startDate = Day(90),
        endDate = Day(95),
        isDateFlexible = false,
        caption = "Host account test trip",
        maxGroupSize = 6,
        isHostedTrip = true,
        pricePerSeatKobo = Price,
        depositKobo = Deposit,
        instalments = new[]
        {
            new { sequence = 1, amountKobo = Instalment, dueDate = Day(30) },
            new { sequence = 2, amountKobo = Instalment, dueDate = Day(60) },
        },
        feeMode,
        termsText = "Deposit is non-refundable.",
    };

    private static async Task<Guid> CreateTripAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/trips", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    /// <summary>A traveller who requested, was approved and accepted the terms.</summary>
    private async Task<(HttpClient Client, TestUser User, Guid InterestId)> ApprovedTravellerAsync(HttpClient host, Guid tripId)
    {
        var (traveller, user) = await UserAsync("Account Traveller");
        var join = await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests", new { message = "Me please" });
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
        var interestId = (await join.ReadJsonAsync()).GetProperty("id").GetGuid();

        var approve = await host.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve = true });
        approve.StatusCode.Should().Be(HttpStatusCode.NoContent, await approve.Content.ReadAsStringAsync());

        var accept = await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms", new { termsVersion = 1 });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        return (traveller, user, interestId);
    }

    private Task<int> InsertSuccessfulPaymentAsync(Guid tripId, Guid interestId, string payerUserId) =>
        WithDbAsync(async db =>
        {
            db.SeatPayments.Add(new SeatPayment
            {
                TripPostId = tripId,
                TripInterestId = interestId,
                PayerUserId = Guid.Parse(payerUserId),
                Reference = $"HDR-TEST-{Guid.NewGuid():N}"[..30],
                Kind = SeatPaymentKind.Deposit,
                SeatAmountKobo = Deposit,
                TotalChargedKobo = Deposit,
                Status = SeatPaymentStatus.Success,
                PaidAt = DateTime.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

    private static Task<HttpResponseMessage> CheckoutAsync(HttpClient traveller, Guid tripId, Guid interestId) =>
        traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/checkout", new { kind = "Deposit" });

    private Task<int> SetFoundingAsync(Guid hostId, bool founding) =>
        WithDbAsync(db => db.HostProfiles.Where(h => h.Id == hostId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.IsFoundingHost, founding)));

    private Task<HostProfile> HostRowAsync(Guid hostId) =>
        WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId));

    private Task<TripPost> TripRowAsync(Guid tripId) =>
        WithDbAsync(db => db.TripPosts.AsNoTracking().SingleAsync(t => t.Id == tripId));

    private Task<string> SubaccountCodeAsync(Guid hostId) =>
        WithDbAsync(db => db.HostProfiles.Where(h => h.Id == hostId).Select(h => h.PaystackSubaccountCode!).SingleAsync());

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, text);
        JsonDocument.Parse(text).RootElement.GetProperty("error").GetString().Should().Be(message);
    }

    private static Task ShouldBeHostClosedAsync(HttpResponseMessage response) =>
        ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, HostClosure.HostMessage);

    private static Task ShouldBeAdminClosedAsync(HttpResponseMessage response) =>
        ShouldBeProblemAsync(response, HttpStatusCode.Conflict, HostClosure.AdminMessage);

    // ── Access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Non_admin_gets_403_on_remove_founding_close_reopen_and_delete()
    {
        var (hostClient, _, hostId) = await HostAsync("Account Sneaky Host", verified: false);

        (await hostClient.DeleteAsync($"/api/admin/hosts/{hostId}/founding")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hostClient.PostAsJsonAsync($"/api/admin/hosts/{hostId}/close", new { reason = "nope" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hostClient.PostAsync($"/api/admin/hosts/{hostId}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hostClient.DeleteAsync($"/api/admin/hosts/{hostId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().DeleteAsync($"/api/admin/hosts/{hostId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var host = await HostRowAsync(hostId);
        host.ClosedAt.Should().BeNull();
    }

    // ── Remove founding ──────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_founding_loses_an_unused_free_departure_and_notifies_host()
    {
        var (hostClient, hostUser, hostId) = await HostAsync("Founding Unused Host");
        var admin = await AdminAsync();
        await SetFoundingAsync(hostId, true);
        try
        {
            var response = await admin.DeleteAsync($"/api/admin/hosts/{hostId}/founding");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = await response.ReadJsonAsync();
            body.GetProperty("summary").GetProperty("isFoundingHost").GetBoolean().Should().BeFalse();
            body.GetProperty("foundingFreeDepartureTripId").ValueKind.Should().Be(JsonValueKind.Null);

            var hostUserId = Guid.Parse(hostUser.UserId);
            var notification = await WithDbAsync(db => db.Notifications.AsNoTracking()
                .SingleAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostFoundingRemoved));
            notification.EmailedAt.Should().NotBeNull("the host was emailed directly");
            _factory.Emails.Sent.Should().Contain(e => e.To == hostUser.Email
                && e.Subject == "Your Hodrac founding host status was removed"
                && e.BodyLines.Any(l => l.Contains("no longer available")));

            // The next hosted trip is not a free departure.
            var tripId = await CreateTripAsync(hostClient, HostedTripBody());
            (await TripRowAsync(tripId)).IsFoundingFreeDeparture.Should().BeFalse();
            (await HostRowAsync(hostId)).FoundingFreeDepartureTripId.Should().BeNull();

            // Idempotent; unknown host is 404.
            (await admin.DeleteAsync($"/api/admin/hosts/{hostId}/founding")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await WithDbAsync(db => db.Notifications.CountAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostFoundingRemoved)))
                .Should().Be(1);
            (await admin.DeleteAsync($"/api/admin/hosts/{Guid.NewGuid()}/founding")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await SetFoundingAsync(hostId, false);
        }
    }

    [Fact]
    public async Task Remove_founding_keeps_a_used_free_departure_fee_free_and_never_grants_a_second()
    {
        var (hostClient, hostUser, hostId) = await HostAsync("Founding Used Host");
        var admin = await AdminAsync();
        await SetFoundingAsync(hostId, true);
        try
        {
            var freeTripId = await CreateTripAsync(hostClient, HostedTripBody());
            (await TripRowAsync(freeTripId)).IsFoundingFreeDeparture.Should().BeTrue();

            var response = await admin.DeleteAsync($"/api/admin/hosts/{hostId}/founding");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            (await response.ReadJsonAsync()).GetProperty("foundingFreeDepartureTripId").GetGuid().Should().Be(freeTripId);
            _factory.Emails.Sent.Should().Contain(e => e.To == hostUser.Email && e.BodyLines.Any(l => l.Contains("stays fee-free")));

            var host = await HostRowAsync(hostId);
            host.IsFoundingHost.Should().BeFalse();
            host.FoundingFreeDepartureTripId.Should().Be(freeTripId);
            (await TripRowAsync(freeTripId)).IsFoundingFreeDeparture.Should().BeTrue();

            // Checkout on that trip is still fee-free (fee passed to the traveller would otherwise be added).
            var (traveller, _, interestId) = await ApprovedTravellerAsync(hostClient, freeTripId);
            var checkout = await CheckoutAsync(traveller, freeTripId, interestId);
            checkout.StatusCode.Should().Be(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync());
            var json = await checkout.ReadJsonAsync();
            json.GetProperty("totalKobo").GetInt64().Should().Be(Deposit);
            _factory.Paystack.Initialized[json.GetProperty("reference").GetString()!].TransactionChargeKobo.Should().Be(0);

            // Re-marking works normally, but the used free departure isn't granted again.
            var remark = await admin.PostAsync($"/api/admin/hosts/{hostId}/founding", null);
            remark.StatusCode.Should().Be(HttpStatusCode.OK, await remark.Content.ReadAsStringAsync());
            var secondTripId = await CreateTripAsync(hostClient, HostedTripBody());
            (await TripRowAsync(secondTripId)).IsFoundingFreeDeparture.Should().BeFalse();
            (await HostRowAsync(hostId)).FoundingFreeDepartureTripId.Should().Be(freeTripId);
        }
        finally
        {
            await SetFoundingAsync(hostId, false);
        }
    }

    [Fact]
    public async Task Remove_founding_frees_a_place_for_another_host()
    {
        var admin = await AdminAsync();
        var existing = await WithDbAsync(db => db.HostProfiles.CountAsync(h => h.IsFoundingHost));
        var marked = new List<Guid>();
        try
        {
            for (var i = existing; i < HodracApiFactory.FoundingHostLimit; i++)
            {
                var (_, _, id) = await HostAsync($"Founding Slot Host {i}", verified: false);
                await SetFoundingAsync(id, true);
                marked.Add(id);
            }
            var (_, _, waiting) = await HostAsync("Founding Waiting Host", verified: false);
            marked.Add(waiting);

            (await admin.PostAsync($"/api/admin/hosts/{waiting}/founding", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            (await admin.DeleteAsync($"/api/admin/hosts/{marked[0]}/founding")).StatusCode.Should().Be(HttpStatusCode.OK);

            var ok = await admin.PostAsync($"/api/admin/hosts/{waiting}/founding", null);
            ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
            (await ok.ReadJsonAsync()).GetProperty("summary").GetProperty("isFoundingHost").GetBoolean().Should().BeTrue();
        }
        finally
        {
            foreach (var id in marked) await SetFoundingAsync(id, false);
        }
    }

    // ── Close ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Close_removes_badge_and_founding_hides_unpaid_trips_keeps_paid_ones_and_deactivates_subaccount()
    {
        var (hostClient, hostUser, hostId) = await HostAsync("Closing Host");
        var admin = await AdminAsync();
        var adminUserId = await WithDbAsync(db => db.Users.Where(u => u.Email == HodracApiFactory.AdminEmail).Select(u => u.Id).SingleAsync());
        var code = await SubaccountCodeAsync(hostId);
        await SetFoundingAsync(hostId, true);

        // Unpaid trip (an approved traveller who never paid), a trip paid through
        // Hodrac and a trip with only a manual (cash) payment.
        var unpaidTripId = await CreateTripAsync(hostClient, HostedTripBody());
        var (unpaidTraveller, _, _) = await ApprovedTravellerAsync(hostClient, unpaidTripId);
        var paidTripId = await CreateTripAsync(hostClient, HostedTripBody());
        var (paidTraveller, paidUser, paidInterestId) = await ApprovedTravellerAsync(hostClient, paidTripId);
        await InsertSuccessfulPaymentAsync(paidTripId, paidInterestId, paidUser.UserId);
        var manualTripId = await CreateTripAsync(hostClient, HostedTripBody());
        var (_, _, manualInterestId) = await ApprovedTravellerAsync(hostClient, manualTripId);
        var cash = await hostClient.PostAsJsonAsync($"/api/host/trips/{manualTripId}/manual-payments",
            new { interestId = manualInterestId, amountKobo = Deposit, method = "Cash" });
        cash.StatusCode.Should().Be(HttpStatusCode.OK, await cash.Content.ReadAsStringAsync());

        // Before closing: the paid trip's traveller can check out.
        (await CheckoutAsync(paidTraveller, paidTripId, paidInterestId)).StatusCode.Should().Be(HttpStatusCode.OK);
        var updatesBefore = _factory.Paystack.UpdatedSubaccounts.Count(u => u.Code == code);

        var response = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/close", new { reason = "Repeated no-show trips." });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("subaccountDeactivated").GetBoolean().Should().BeTrue();
        body.GetProperty("hiddenTripIds").EnumerateArray().Select(e => e.GetGuid()).Should().BeEquivalentTo(new[] { unpaidTripId });
        var summary = body.GetProperty("host").GetProperty("summary");
        summary.GetProperty("verificationStatus").GetString().Should().Be("Unverified");
        summary.GetProperty("isFoundingHost").GetBoolean().Should().BeFalse();
        summary.GetProperty("closedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        summary.GetProperty("closedReason").GetString().Should().Be("Repeated no-show trips.");
        body.GetProperty("host").GetProperty("closedByUserId").GetGuid().Should().Be(Guid.Parse(adminUserId));

        // DB state.
        var host = await HostRowAsync(hostId);
        host.ClosedAt.Should().NotBeNull();
        host.ClosedByUserId.Should().Be(Guid.Parse(adminUserId));
        host.VerificationStatus.Should().Be(HostVerificationStatus.Unverified);
        host.RejectionReason.Should().Be("Host account closed: Repeated no-show trips.");
        host.IsFoundingHost.Should().BeFalse();
        host.PaystackSubaccountCode.Should().Be(code);

        var unpaid = await TripRowAsync(unpaidTripId);
        unpaid.IsHidden.Should().BeTrue();
        unpaid.HiddenReason.Should().Be(HostClosure.HiddenTripReason);
        unpaid.HiddenByUserId.Should().Be(Guid.Parse(adminUserId));
        (await TripRowAsync(paidTripId)).IsHidden.Should().BeFalse();
        (await TripRowAsync(manualTripId)).IsHidden.Should().BeFalse();

        // Subaccount deactivated, sending only active = false.
        var updates = _factory.Paystack.UpdatedSubaccounts.Where(u => u.Code == code).ToList();
        updates.Should().HaveCount(updatesBefore + 1);
        updates[^1].Update.Should().Be(new PaystackSubaccountUpdate(Active: false));

        // Visibility: the unpaid trip is gone for everyone but its author; the paid one stays.
        (await _factory.CreateClient().GetAsync($"/api/trips/public/{unpaidTripId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unpaidTraveller.GetAsync($"/api/trips/{unpaidTripId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await paidTraveller.GetAsync($"/api/trips/{paidTripId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var publicPaid = await (await _factory.CreateClient().GetAsync($"/api/trips/public/{paidTripId}")).ReadJsonAsync();
        publicPaid.GetProperty("host").GetProperty("verified").GetBoolean().Should().BeFalse();

        // Checkout refused (host no longer verified).
        (await CheckoutAsync(paidTraveller, paidTripId, paidInterestId)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Host notified and emailed.
        var hostUserId = Guid.Parse(hostUser.UserId);
        var notification = await WithDbAsync(db => db.Notifications.AsNoTracking()
            .SingleAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostClosed));
        notification.ActorUserId.Should().Be(Guid.Parse(adminUserId));
        notification.EmailedAt.Should().NotBeNull();
        _factory.Emails.Sent.Should().Contain(e => e.To == hostUser.Email && e.Subject == "Your Hodrac host account was closed"
            && e.BodyLines.Any(l => l.Contains("Repeated no-show trips.")));

        // The host sees the closure on their profile.
        var me = await (await hostClient.GetAsync("/api/host/profile/me")).ReadJsonAsync();
        me.GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.String);
        me.GetProperty("closedReason").GetString().Should().Be("Repeated no-show trips.");
        me.GetProperty("verificationStatus").GetString().Should().Be("Unverified");

        // Admin queue: closed hosts are listed and filterable.
        var search = Uri.EscapeDataString("Closing Host");
        var closedList = await (await admin.GetAsync($"/api/admin/hosts?closed=true&search={search}")).ReadJsonAsync();
        closedList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(hostId);
        var all = await (await admin.GetAsync($"/api/admin/hosts?search={search}")).ReadJsonAsync();
        all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(hostId);
        var openList = await (await admin.GetAsync($"/api/admin/hosts?closed=false&search={search}")).ReadJsonAsync();
        openList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(hostId);

        // Second close: idempotent, nothing re-sent, deactivation retried.
        var again = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/close", new { reason = "Other reason" });
        again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var againBody = await again.ReadJsonAsync();
        againBody.GetProperty("hiddenTripIds").GetArrayLength().Should().Be(0);
        againBody.GetProperty("subaccountDeactivated").GetBoolean().Should().BeTrue();
        againBody.GetProperty("host").GetProperty("summary").GetProperty("closedReason").GetString().Should().Be("Repeated no-show trips.");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostClosed)))
            .Should().Be(1);

        (await admin.PostAsync($"/api/admin/hosts/{Guid.NewGuid()}/close", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Closed_host_cannot_set_up_again_but_can_settle_existing_trips()
    {
        var (hostClient, _, hostId) = await HostAsync("Closed Settling Host");
        var admin = await AdminAsync();

        var paidTripId = await CreateTripAsync(hostClient, HostedTripBody());
        var (_, paidUser, paidInterestId) = await ApprovedTravellerAsync(hostClient, paidTripId);
        await InsertSuccessfulPaymentAsync(paidTripId, paidInterestId, paidUser.UserId);
        var communityTrip = await hostClient.PostAsJsonAsync("/api/trips", new
        {
            freeTextDestination = $"Account-{Guid.NewGuid():N}",
            startDate = Day(60),
            endDate = Day(65),
            isDateFlexible = false,
            caption = "Weekend away",
            maxGroupSize = 6,
        });
        communityTrip.StatusCode.Should().Be(HttpStatusCode.Created);
        var communityTripId = (await communityTrip.ReadJsonAsync()).GetProperty("id").GetGuid();

        (await admin.PostAsync($"/api/admin/hosts/{hostId}/close", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await HostRowAsync(hostId)).RejectionReason.Should().Be("Host account closed by Hodrac.");

        // Setup is refused.
        await ShouldBeHostClosedAsync(await hostClient.PatchAsJsonAsync("/api/host/profile/me", new { bio = "Back again" }));
        await ShouldBeHostClosedAsync(await hostClient.PostAsJsonAsync("/api/host/payout-account",
            new { bankCode = "044", accountNumber = "0123456789", confirm = true }));
        await ShouldBeHostClosedAsync(await hostClient.PostAsync("/api/host/verification", VerificationForm(PdfBytes)));
        await ShouldBeHostClosedAsync(await hostClient.PostAsJsonAsync("/api/host/profile", new { displayName = "Fresh Start" }));
        await ShouldBeHostClosedAsync(await hostClient.PostAsJsonAsync("/api/trips", HostedTripBody()));
        await ShouldBeHostClosedAsync(await hostClient.PatchAsJsonAsync($"/api/trips/{paidTripId}", new { pricePerSeatKobo = Price + 1_000_000 }));
        var convert = new Dictionary<string, object?>
        {
            ["isHostedTrip"] = true,
            ["pricePerSeatKobo"] = Price,
            ["depositKobo"] = Price,
            ["termsText"] = "All sales final.",
        };
        await ShouldBeHostClosedAsync(await hostClient.PatchAsJsonAsync($"/api/trips/{communityTripId}", convert));
        (await TripRowAsync(communityTripId)).IsHostedTrip.Should().BeFalse();
        (await TripRowAsync(paidTripId)).PricePerSeatKobo.Should().Be(Price);

        // Settling with travellers who already paid still works.
        (await hostClient.GetAsync("/api/host/profile/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await hostClient.GetAsync($"/api/host/trips/{paidTripId}/manifest")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await hostClient.GetAsync($"/api/host/trips/{paidTripId}/manifest.csv")).StatusCode.Should().Be(HttpStatusCode.OK);
        var manual = await hostClient.PostAsJsonAsync($"/api/host/trips/{paidTripId}/manual-payments",
            new { interestId = paidInterestId, amountKobo = Instalment, method = "Transfer" });
        manual.StatusCode.Should().Be(HttpStatusCode.OK, await manual.Content.ReadAsStringAsync());
        (await hostClient.GetAsync("/api/host/reports/weekly")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Admins can't set the host up again either.
        await ShouldBeAdminClosedAsync(await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null));
        await ShouldBeAdminClosedAsync(await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/reject", new { reason = "No." }));
        await ShouldBeAdminClosedAsync(await admin.PostAsync($"/api/admin/hosts/{hostId}/founding", null));
        await ShouldBeAdminClosedAsync(await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/operator-plan",
            new { reference = $"INV-{Guid.NewGuid():N}"[..20], amountKobo = 90_000_000, periodStart = "2026-12-01", periodEnd = "2027-11-30" }));
        var adminTrip = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/trips", HostedTripBody());
        adminTrip.StatusCode.Should().Be(HttpStatusCode.Conflict, await adminTrip.Content.ReadAsStringAsync());
        (await HostRowAsync(hostId)).IsFoundingHost.Should().BeFalse();
    }

    [Fact]
    public async Task Close_still_closes_when_paystack_fails_and_a_second_close_retries()
    {
        var (_, _, hostId) = await HostAsync("Paystack Down Host");
        var admin = await AdminAsync();
        var code = await SubaccountCodeAsync(hostId);

        _factory.Paystack.FailUpdateForSubaccount[code] = true;
        try
        {
            var response = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/close", new { reason = "Fraud report." });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            (await response.ReadJsonAsync()).GetProperty("subaccountDeactivated").GetBoolean().Should().BeFalse();
            (await HostRowAsync(hostId)).ClosedAt.Should().NotBeNull();
            _factory.Emails.Sent.Should().Contain(e => e.To == HodracApiFactory.AdminEmail
                && e.Subject == "Paystack subaccount still active" && e.BodyLines.Any(l => l.Contains(code)));
        }
        finally
        {
            _factory.Paystack.FailUpdateForSubaccount.TryRemove(code, out _);
        }

        var retry = await admin.PostAsync($"/api/admin/hosts/{hostId}/close", null);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retry.ReadJsonAsync()).GetProperty("subaccountDeactivated").GetBoolean().Should().BeTrue();

        // No subaccount: nothing to deactivate.
        var (plainClient, _) = await UserAsync("No Payout Closing Host");
        var plainId = (await CreateProfileAsync(plainClient, "No Payout Closing Host")).GetProperty("id").GetGuid();
        var plain = await admin.PostAsync($"/api/admin/hosts/{plainId}/close", null);
        plain.StatusCode.Should().Be(HttpStatusCode.OK);
        (await plain.ReadJsonAsync()).GetProperty("subaccountDeactivated").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Reopen ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reopen_restores_setup_and_close_hidden_trips_but_not_verification_or_founding()
    {
        var (hostClient, hostUser, hostId) = await HostAsync("Reopening Host");
        var admin = await AdminAsync();
        var code = await SubaccountCodeAsync(hostId);
        await SetFoundingAsync(hostId, true);
        try
        {
            // A trip close will hide (approved traveller, no payment), a trip hidden by
            // moderation before the close, and a paid trip close leaves visible.
            var unpaidTripId = await CreateTripAsync(hostClient, HostedTripBody());
            var (traveller, _, interestId) = await ApprovedTravellerAsync(hostClient, unpaidTripId);
            var moderatedTripId = await CreateTripAsync(hostClient, HostedTripBody());
            (await admin.PostAsJsonAsync($"/api/admin/trips/{moderatedTripId}/hide", new { reason = "Spam listing" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            var paidTripId = await CreateTripAsync(hostClient, HostedTripBody());
            var (_, paidUser, paidInterestId) = await ApprovedTravellerAsync(hostClient, paidTripId);
            await InsertSuccessfulPaymentAsync(paidTripId, paidInterestId, paidUser.UserId);

            var close = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/close", new { reason = "Investigating." });
            close.StatusCode.Should().Be(HttpStatusCode.OK, await close.Content.ReadAsStringAsync());
            (await close.ReadJsonAsync()).GetProperty("hiddenTripIds").EnumerateArray().Select(e => e.GetGuid())
                .Should().BeEquivalentTo(new[] { unpaidTripId });

            var response = await admin.PostAsync($"/api/admin/hosts/{hostId}/reopen", null);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = await response.ReadJsonAsync();
            body.GetProperty("subaccountReactivated").GetBoolean().Should().BeTrue();
            body.GetProperty("unhiddenTripIds").EnumerateArray().Select(e => e.GetGuid()).Should().BeEquivalentTo(new[] { unpaidTripId });
            var detail = body.GetProperty("host");
            detail.GetProperty("summary").GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.Null);
            detail.GetProperty("summary").GetProperty("closedReason").ValueKind.Should().Be(JsonValueKind.Null);
            detail.GetProperty("summary").GetProperty("verificationStatus").GetString().Should().Be("Unverified");
            detail.GetProperty("summary").GetProperty("isFoundingHost").GetBoolean().Should().BeFalse();
            detail.GetProperty("closedByUserId").ValueKind.Should().Be(JsonValueKind.Null);
            detail.GetProperty("rejectionReason").ValueKind.Should().Be(JsonValueKind.Null);

            var host = await HostRowAsync(hostId);
            host.ClosedAt.Should().BeNull();
            host.ClosedReason.Should().BeNull();
            host.ClosedByUserId.Should().BeNull();
            host.VerificationStatus.Should().Be(HostVerificationStatus.Unverified);
            host.IsFoundingHost.Should().BeFalse();

            // Only the trip close hid comes back; moderation stays.
            var unpaid = await TripRowAsync(unpaidTripId);
            unpaid.IsHidden.Should().BeFalse();
            unpaid.HiddenReason.Should().BeNull();
            unpaid.HiddenAt.Should().BeNull();
            unpaid.HiddenByUserId.Should().BeNull();
            var moderated = await TripRowAsync(moderatedTripId);
            moderated.IsHidden.Should().BeTrue();
            moderated.HiddenReason.Should().Be("Spam listing");
            (await TripRowAsync(paidTripId)).IsHidden.Should().BeFalse();
            (await _factory.CreateClient().GetAsync($"/api/trips/public/{unpaidTripId}")).StatusCode.Should().Be(HttpStatusCode.OK);

            // Subaccount reactivated, sending only active = true.
            _factory.Paystack.UpdatedSubaccounts.Where(u => u.Code == code).Last().Update
                .Should().Be(new PaystackSubaccountUpdate(Active: true));

            // Host notified and emailed.
            var hostUserId = Guid.Parse(hostUser.UserId);
            var notification = await WithDbAsync(db => db.Notifications.AsNoTracking()
                .SingleAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostReopened));
            notification.EmailedAt.Should().NotBeNull();
            _factory.Emails.Sent.Should().Contain(e => e.To == hostUser.Email && e.Subject == "Your Hodrac host account was reopened"
                && e.BodyLines.Any(l => l.Contains("verifies it again")));

            // Setup works again.
            var me = await hostClient.GetAsync("/api/host/profile/me");
            (await me.ReadJsonAsync()).GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.Null);
            (await hostClient.PatchAsJsonAsync("/api/host/profile/me", new { bio = "Back in business" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await hostClient.PostAsJsonAsync("/api/host/payout-account",
                    new { bankCode = "044", accountNumber = "0123456789", confirm = true }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            var newTripId = await CreateTripAsync(hostClient, HostedTripBody());
            (await TripRowAsync(newTripId)).IsFoundingFreeDeparture.Should().BeFalse("founding isn't restored by reopening");

            // Checkout still refused until an admin verifies again (no resubmission needed).
            (await CheckoutAsync(traveller, unpaidTripId, interestId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            var verify = await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null);
            verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
            var checkout = await CheckoutAsync(traveller, unpaidTripId, interestId);
            checkout.StatusCode.Should().Be(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync());

            // Reopening an open host: no change, no Paystack call, no notification.
            var updates = _factory.Paystack.UpdatedSubaccounts.Count(u => u.Code == code);
            var again = await admin.PostAsync($"/api/admin/hosts/{hostId}/reopen", null);
            again.StatusCode.Should().Be(HttpStatusCode.OK);
            var againBody = await again.ReadJsonAsync();
            againBody.GetProperty("subaccountReactivated").ValueKind.Should().Be(JsonValueKind.Null);
            againBody.GetProperty("unhiddenTripIds").GetArrayLength().Should().Be(0);
            againBody.GetProperty("host").GetProperty("summary").GetProperty("verificationStatus").GetString().Should().Be("Verified");
            _factory.Paystack.UpdatedSubaccounts.Count(u => u.Code == code).Should().Be(updates);
            (await WithDbAsync(db => db.Notifications.CountAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostReopened)))
                .Should().Be(1);
            (await admin.PostAsync($"/api/admin/hosts/{Guid.NewGuid()}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await SetFoundingAsync(hostId, false);
        }
    }

    [Fact]
    public async Task Reopen_still_reopens_when_paystack_fails()
    {
        var (_, _, hostId) = await HostAsync("Reopen Paystack Down Host");
        var admin = await AdminAsync();
        var code = await SubaccountCodeAsync(hostId);
        (await admin.PostAsync($"/api/admin/hosts/{hostId}/close", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Paystack.FailUpdateForSubaccount[code] = true;
        try
        {
            var response = await admin.PostAsync($"/api/admin/hosts/{hostId}/reopen", null);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            (await response.ReadJsonAsync()).GetProperty("subaccountReactivated").GetBoolean().Should().BeFalse();
            (await HostRowAsync(hostId)).ClosedAt.Should().BeNull();
            _factory.Emails.Sent.Should().Contain(e => e.To == HodracApiFactory.AdminEmail
                && e.Subject == "Paystack subaccount still inactive" && e.BodyLines.Any(l => l.Contains(code)));
        }
        finally
        {
            _factory.Paystack.FailUpdateForSubaccount.TryRemove(code, out _);
        }

        // No subaccount: nothing to reactivate.
        var (plainClient, _) = await UserAsync("No Payout Reopen Host");
        var plainId = (await CreateProfileAsync(plainClient, "No Payout Reopen Host")).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/admin/hosts/{plainId}/close", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var plain = await admin.PostAsync($"/api/admin/hosts/{plainId}/reopen", null);
        plain.StatusCode.Should().Be(HttpStatusCode.OK);
        (await plain.ReadJsonAsync()).GetProperty("subaccountReactivated").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Close_reopen_close_works()
    {
        var (hostClient, hostUser, hostId) = await HostAsync("Close Reopen Close Host", verified: false);
        var admin = await AdminAsync();
        var tripId = await CreateTripAsync(hostClient, HostedTripBody());

        // Never closed: reopen is a no-op.
        var noop = await admin.PostAsync($"/api/admin/hosts/{hostId}/reopen", null);
        noop.StatusCode.Should().Be(HttpStatusCode.OK);
        (await noop.ReadJsonAsync()).GetProperty("subaccountReactivated").ValueKind.Should().Be(JsonValueKind.Null);

        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/close", new { reason = "First" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsync($"/api/admin/hosts/{hostId}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TripRowAsync(tripId)).IsHidden.Should().BeFalse();

        var second = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/close", new { reason = "Second" });
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        (await second.ReadJsonAsync()).GetProperty("hiddenTripIds").EnumerateArray().Select(e => e.GetGuid())
            .Should().BeEquivalentTo(new[] { tripId });

        var host = await HostRowAsync(hostId);
        host.ClosedAt.Should().NotBeNull();
        host.ClosedReason.Should().Be("Second");
        (await TripRowAsync(tripId)).IsHidden.Should().BeTrue();
        await ShouldBeHostClosedAsync(await hostClient.PatchAsJsonAsync("/api/host/profile/me", new { bio = "x" }));

        var hostUserId = Guid.Parse(hostUser.UserId);
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostClosed)))
            .Should().Be(2);
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostReopened)))
            .Should().Be(1);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_an_unused_host_and_its_documents_and_the_user_can_start_again()
    {
        var (hostClient, hostUser, hostId) = await HostAsync("Deletable Host", verified: false);
        var admin = await AdminAsync();
        var code = await SubaccountCodeAsync(hostId);
        (await WithDbAsync(db => db.HostDocuments.CountAsync(d => d.HostProfileId == hostId))).Should().Be(1);

        var response = await admin.DeleteAsync($"/api/admin/hosts/{hostId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        (await WithDbAsync(db => db.HostProfiles.AnyAsync(h => h.Id == hostId))).Should().BeFalse();
        (await WithDbAsync(db => db.HostDocuments.AnyAsync(d => d.HostProfileId == hostId))).Should().BeFalse();
        _factory.Paystack.UpdatedSubaccounts.Should().Contain(u => u.Code == code && u.Update.Active == false);
        (await admin.GetAsync($"/api/admin/hosts/{hostId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/admin/hosts/{hostId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The user account stays: they can create a fresh host profile.
        (await hostClient.GetAsync("/api/host/profile/me")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var fresh = await CreateProfileAsync(hostClient, "Deletable Host Again");
        fresh.GetProperty("id").GetGuid().Should().NotBe(hostId);
        (await WithDbAsync(db => db.HostProfiles.CountAsync(h => h.UserId == Guid.Parse(hostUser.UserId)))).Should().Be(1);
    }

    [Fact]
    public async Task Delete_refused_when_the_host_has_a_hosted_trip_even_hidden()
    {
        var (hostClient, _, hostId) = await HostAsync("Trip Owning Host", verified: false);
        var admin = await AdminAsync();
        var tripId = await CreateTripAsync(hostClient, HostedTripBody());
        await WithDbAsync(db => db.TripPosts.Where(t => t.Id == tripId).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsHidden, true)));

        var response = await admin.DeleteAsync($"/api/admin/hosts/{hostId}");
        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict,
            "This host has trips or payments, so it can't be deleted. Close the account instead.");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("hostedTrips").GetInt32().Should().Be(1);
        body.GetProperty("seatPayments").GetInt32().Should().Be(0);
        body.GetProperty("operatorPlanPayments").GetInt32().Should().Be(0);
        (await WithDbAsync(db => db.HostProfiles.AnyAsync(h => h.Id == hostId))).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_refused_when_the_host_has_an_operator_plan_payment()
    {
        var (hostClient, _) = await UserAsync("Plan Paying Host");
        var hostId = (await CreateProfileAsync(hostClient, "Plan Paying Host")).GetProperty("id").GetGuid();
        var admin = await AdminAsync();
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/operator-plan",
                new { reference = $"INV-{Guid.NewGuid():N}"[..20], amountKobo = 90_000_000, periodStart = "2026-12-01", periodEnd = "2027-11-30" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await admin.DeleteAsync($"/api/admin/hosts/{hostId}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("operatorPlanPayments").GetInt32().Should().Be(1);
        body.GetProperty("hostedTrips").GetInt32().Should().Be(0);
        (await WithDbAsync(db => db.HostProfiles.AnyAsync(h => h.Id == hostId))).Should().BeTrue();
    }
}
