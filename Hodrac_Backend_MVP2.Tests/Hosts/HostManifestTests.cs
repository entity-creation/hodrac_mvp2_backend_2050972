using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// Phase 5: host trips list, manifest (+ CSV), manual payments and refunds,
/// WhatsApp nudge link, weekly report. Paystack is the in-memory fake.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HostManifestTests
{
    private const long Price = 25_000_000;   // ₦250,000
    private const long Deposit = 5_000_000;  // ₦50,000
    private const long Instalment = 10_000_000;

    private readonly HodracApiFactory _factory;

    public HostManifestTests(HodracApiFactory factory) => _factory = factory;

    // ── Setup helpers (same flow as PaymentFlowTests) ────────────────────────

    private record Seat(
        HttpClient Host, TestUser HostUser, Guid HostId,
        HttpClient Traveller, TestUser TravellerUser,
        Guid TripId, Guid InterestId);

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

    private async Task<(HttpClient Client, TestUser User, Guid HostId)> HostAsync(string name)
    {
        var (client, user) = await UserAsync(name);
        var hostId = await CreatePendingHostAsync(client, name);
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        var verify = await _factory.CreateClient().Authenticated(admin).PostAsync($"/api/admin/hosts/{hostId}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        return (client, user, hostId);
    }

    private static object HostedTripBody(int startInDays = 90) => new
    {
        freeTextDestination = $"Zanzibar-{Guid.NewGuid():N}",
        startDate = Day(startInDays),
        endDate = Day(startInDays + 5),
        isDateFlexible = false,
        caption = "Zanzibar with the crew",
        maxGroupSize = 6,
        isHostedTrip = true,
        pricePerSeatKobo = Price,
        depositKobo = Deposit,
        instalments = new[]
        {
            new { sequence = 1, amountKobo = Instalment, dueDate = Day(30) },
            new { sequence = 2, amountKobo = Instalment, dueDate = Day(60) },
        },
        feeMode = "PassToTraveller",
        termsText = "Deposit is non-refundable.",
    };

    private static async Task<Guid> CreateTripAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/trips", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> JoinAsync(HttpClient client, Guid tripId, string? contactPhone = null)
    {
        var join = await client.PostAsJsonAsync($"/api/trips/{tripId}/interests", new { message = "Me please", contactPhone });
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
        return (await join.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task ApproveAsync(HttpClient host, Guid tripId, Guid interestId, bool approve = true)
    {
        var response = await host.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private async Task<(HttpClient Client, TestUser User, Guid InterestId)> ApprovedTravellerAsync(
        HttpClient host, Guid tripId, string name, string? contactPhone = null, bool acceptTerms = true)
    {
        var (traveller, user) = await UserAsync(name);
        var interestId = await JoinAsync(traveller, tripId, contactPhone);
        await ApproveAsync(host, tripId, interestId);
        if (acceptTerms)
        {
            var accept = await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms", new { termsVersion = 1 });
            accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        }
        return (traveller, user, interestId);
    }

    private async Task<Seat> ApprovedSeatAsync(string travellerName = "Ada Traveller", string? contactPhone = null)
    {
        var (host, hostUser, hostId) = await HostAsync("Manifest Host");
        var tripId = await CreateTripAsync(host, HostedTripBody());
        var (traveller, travellerUser, interestId) = await ApprovedTravellerAsync(host, tripId, travellerName, contactPhone);
        return new Seat(host, hostUser, hostId, traveller, travellerUser, tripId, interestId);
    }

    private static string ChargeSuccessBody(string reference, long amount) =>
        $"{{\"event\":\"charge.success\",\"data\":{{\"id\":4000000001,\"status\":\"success\",\"reference\":\"{reference}\",\"amount\":{amount},\"currency\":\"NGN\"}}}}";

    /// <summary>Deposit checkout + signed charge.success webhook.</summary>
    private async Task<string> PayDepositAsync(HttpClient traveller, Guid tripId, Guid interestId)
    {
        var checkout = await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/checkout", new { kind = "Deposit" });
        checkout.StatusCode.Should().Be(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync());
        var json = await checkout.ReadJsonAsync();
        var reference = json.GetProperty("reference").GetString()!;
        var body = ChargeSuccessBody(reference, json.GetProperty("totalKobo").GetInt64());

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/paystack/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(PaystackSignature.HeaderName,
            PaystackSignature.ComputeHex(Encoding.UTF8.GetBytes(body), HodracApiFactory.PaystackSecretKey));
        var webhook = await _factory.CreateClient().SendAsync(request);
        webhook.StatusCode.Should().Be(HttpStatusCode.OK, await webhook.Content.ReadAsStringAsync());
        return reference;
    }

    private static Task<HttpResponseMessage> RecordAsync(HttpClient host, Guid tripId, Guid interestId, long amountKobo, string method, string? note = null) =>
        host.PostAsJsonAsync($"/api/host/trips/{tripId}/manual-payments", new { interestId, amountKobo, method, note });

    private static async Task<JsonElement> RecordOkAsync(HttpClient host, Guid tripId, Guid interestId, long amountKobo, string method)
    {
        var response = await RecordAsync(host, tripId, interestId, amountKobo, method);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    private Task<TripInterest> InterestAsync(Guid interestId) =>
        WithDbAsync(db => db.TripInterests.AsNoTracking().FirstAsync(i => i.Id == interestId));

    private Task<bool> InThreadAsync(Guid tripId, string userId) => WithDbAsync(db =>
        db.TripThreadParticipants.AnyAsync(p => p.UserId == Guid.Parse(userId)
            && db.TripThreads.Any(t => t.Id == p.TripThreadId && t.TripPostId == tripId)));

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    // ── Access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Host_trip_endpoints_are_404_for_non_authors()
    {
        var seat = await ApprovedSeatAsync();
        var (otherHost, _, _) = await HostAsync("Other Host");

        foreach (var client in new[] { otherHost, seat.Traveller })
        {
            (await client.GetAsync($"/api/host/trips/{seat.TripId}/manifest")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetAsync($"/api/host/trips/{seat.TripId}/manifest.csv")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await RecordAsync(client, seat.TripId, seat.InterestId, Deposit, "Transfer")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetAsync($"/api/host/trips/{seat.TripId}/manual-payments")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetAsync($"/api/host/trips/{seat.TripId}/whatsapp-nudge/{seat.InterestId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(0);
        var list = await GetJsonAsync(otherHost, "/api/host/trips");
        list.EnumerateArray().Should().NotContain(t => t.GetProperty("id").GetGuid() == seat.TripId);
    }

    [Fact]
    public async Task Host_trip_endpoints_are_404_on_community_trips()
    {
        var (author, _, _) = await HostAsync("Community Host");
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
        var interestId = await JoinAsync(traveller, tripId, "08031234567");
        await ApproveAsync(author, tripId, interestId);

        (await author.GetAsync($"/api/host/trips/{tripId}/manifest")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await author.GetAsync($"/api/host/trips/{tripId}/manifest.csv")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RecordAsync(author, tripId, interestId, 100, "Cash")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await author.GetAsync($"/api/host/trips/{tripId}/whatsapp-nudge/{interestId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetJsonAsync(author, "/api/host/trips")).EnumerateArray()
            .Should().NotContain(t => t.GetProperty("id").GetGuid() == tripId);
    }

    // ── Trips list ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Trips_list_counts_seats_and_money_upcoming_first()
    {
        var (host, _, _) = await HostAsync("List Host");
        var laterTrip = await CreateTripAsync(host, HostedTripBody(startInDays: 90));
        var soonerTrip = await CreateTripAsync(host, HostedTripBody(startInDays: 70));

        var (paidTraveller, _, paidInterest) = await ApprovedTravellerAsync(host, laterTrip, "Paid Traveller");
        await PayDepositAsync(paidTraveller, laterTrip, paidInterest);
        await ApprovedTravellerAsync(host, laterTrip, "Unpaid Traveller");
        var (_, _, cashInterest) = await ApprovedTravellerAsync(host, laterTrip, "Cash Traveller", acceptTerms: false);
        await RecordOkAsync(host, laterTrip, cashInterest, Price, "Cash");
        var (requester, _) = await UserAsync("Requester");
        await JoinAsync(requester, laterTrip);

        var list = (await GetJsonAsync(host, "/api/host/trips")).EnumerateArray().ToList();
        list.Select(t => t.GetProperty("id").GetGuid()).Should().Equal(soonerTrip, laterTrip);
        list[0].GetProperty("isNextDeparture").GetBoolean().Should().BeTrue();
        list[1].GetProperty("isNextDeparture").GetBoolean().Should().BeFalse();

        var later = list[1];
        later.GetProperty("isUpcoming").GetBoolean().Should().BeTrue();
        later.GetProperty("seatsTotal").GetInt32().Should().Be(6);
        later.GetProperty("approved").GetInt32().Should().Be(3);
        later.GetProperty("seatsWithDeposit").GetInt32().Should().Be(2);
        later.GetProperty("seatsPaidInFull").GetInt32().Should().Be(1);
        later.GetProperty("collectedKobo").GetInt64().Should().Be(Deposit);
        later.GetProperty("collectedManualKobo").GetInt64().Should().Be(Price);
        later.GetProperty("collectedTotalKobo").GetInt64().Should().Be(Deposit + Price);
        later.GetProperty("owedKobo").GetInt64().Should().Be((Price - Deposit) + Price);
        later.GetProperty("status").GetString().Should().Be("Open");
        later.GetProperty("isListed").GetBoolean().Should().BeFalse();
    }

    // ── Manifest ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Manifest_rows_and_totals_after_hodrac_and_manual_payments()
    {
        var seat = await ApprovedSeatAsync("Ada Paid", contactPhone: "+234 803 123 4567");
        await PayDepositAsync(seat.Traveller, seat.TripId, seat.InterestId);
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, 2_000_000, "Transfer");

        var (_, _, awaitingInterest) = await ApprovedTravellerAsync(seat.Host, seat.TripId, "Bola Awaiting", acceptTerms: false);
        var (requester, _) = await UserAsync("Chidi Requested");
        var requestedInterest = await JoinAsync(requester, seat.TripId);
        var (declined, _) = await UserAsync("Declined Person");
        var declinedInterest = await JoinAsync(declined, seat.TripId);
        await ApproveAsync(seat.Host, seat.TripId, declinedInterest, approve: false);
        var (withdrawer, _, withdrawnInterest) = await ApprovedTravellerAsync(seat.Host, seat.TripId, "Dayo Withdrawn", acceptTerms: false);
        (await withdrawer.DeleteAsync($"/api/trips/{seat.TripId}/interests/{withdrawnInterest}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var manifest = await GetJsonAsync(seat.Host, $"/api/host/trips/{seat.TripId}/manifest");
        var rows = manifest.GetProperty("rows").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("interestId").GetGuid())
            .Should().Equal(seat.InterestId, awaitingInterest, requestedInterest, withdrawnInterest);

        var paid = rows[0];
        paid.GetProperty("travellerName").GetString().Should().Be("Ada Paid");
        paid.GetProperty("email").GetString().Should().Be(seat.TravellerUser.Email);
        paid.GetProperty("phone").GetString().Should().Be("+2348031234567");
        paid.GetProperty("interestStatus").GetString().Should().Be("Approved");
        paid.GetProperty("paymentStatus").GetString().Should().Be("PartPaid");
        paid.GetProperty("termsVersionAccepted").GetInt32().Should().Be(1);
        paid.GetProperty("termsAcceptedAt").ValueKind.Should().Be(JsonValueKind.String);
        paid.GetProperty("paidKobo").GetInt64().Should().Be(Deposit + 2_000_000);
        paid.GetProperty("viaHodracKobo").GetInt64().Should().Be(Deposit);
        paid.GetProperty("manualKobo").GetInt64().Should().Be(2_000_000);
        paid.GetProperty("owesKobo").GetInt64().Should().Be(Price - Deposit - 2_000_000);
        paid.GetProperty("nextDueDate").GetString().Should().Be(Day(30).ToString("yyyy-MM-dd"));
        paid.GetProperty("nextDueAmountKobo").GetInt64().Should().Be(Instalment - 2_000_000);
        paid.GetProperty("seatHoldExpiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        paid.GetProperty("lastReminderAt").ValueKind.Should().Be(JsonValueKind.Null);

        var awaiting = rows[1];
        awaiting.GetProperty("paymentStatus").GetString().Should().Be("AwaitingDeposit");
        awaiting.GetProperty("owesKobo").GetInt64().Should().Be(Price);
        awaiting.GetProperty("nextDueAmountKobo").GetInt64().Should().Be(Deposit);
        awaiting.GetProperty("seatHoldExpiresAt").ValueKind.Should().Be(JsonValueKind.String);
        awaiting.GetProperty("nextDueDate").ValueKind.Should().Be(JsonValueKind.String);

        rows[2].GetProperty("interestStatus").GetString().Should().Be("Requested");
        rows[2].GetProperty("owesKobo").GetInt64().Should().Be(0);
        rows[3].GetProperty("interestStatus").GetString().Should().Be("Withdrawn");
        rows[3].GetProperty("owesKobo").GetInt64().Should().Be(0);

        var totals = manifest.GetProperty("totals");
        totals.GetProperty("travellers").GetInt32().Should().Be(4);
        totals.GetProperty("approved").GetInt32().Should().Be(2);
        totals.GetProperty("seatsWithDeposit").GetInt32().Should().Be(1);
        totals.GetProperty("seatsPaidInFull").GetInt32().Should().Be(0);
        totals.GetProperty("paidKobo").GetInt64().Should().Be(Deposit + 2_000_000);
        totals.GetProperty("viaHodracKobo").GetInt64().Should().Be(Deposit);
        totals.GetProperty("manualKobo").GetInt64().Should().Be(2_000_000);
        totals.GetProperty("owesKobo").GetInt64().Should().Be((Price - Deposit - 2_000_000) + Price);
        manifest.GetProperty("pricePerSeatKobo").GetInt64().Should().Be(Price);
    }

    [Fact]
    public async Task Manifest_csv_has_header_bom_and_guards_formulas()
    {
        var seat = await ApprovedSeatAsync("Formula Person");
        await WithDbAsync(db => db.Users.Where(u => u.Id == seat.TravellerUser.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, "=SUM(1,2)")));
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, Deposit, "Cash");

        var response = await seat.Host.GetAsync($"/api/host/trips/{seat.TripId}/manifest.csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var fileName = response.Content.Headers.ContentDisposition!.FileName!.Trim('"');
        fileName.Should().StartWith("manifest-zanzibar-").And.EndWith($"-{HostTripService.TodayWat(DateTime.UtcNow):yyyy-MM-dd}.csv");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("name,email,phone,requestStatus,paymentStatus,termsVersionAccepted,termsAcceptedAt,"
            + "paidNgn,viaHodracNgn,manualNgn,owesNgn,nextDueDate,nextDueNgn,seatHoldExpiresAt,lastReminderAt,requestedAt,overdueNgn");
        lines.Should().HaveCount(2);
        lines[1].Should().StartWith($"\"'=SUM(1,2)\",{seat.TravellerUser.Email},,Approved,DepositPaid,1,");
        lines[1].Should().Contain(",50000.00,0.00,50000.00,200000.00,");
        lines[1].Should().EndWith(",0.00");
    }

    // ── Manual payments ──────────────────────────────────────────────────────

    [Fact]
    public async Task Manual_payment_validation()
    {
        var seat = await ApprovedSeatAsync();
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, 1_000_000, "Transfer");

        (await RecordAsync(seat.Host, seat.TripId, seat.InterestId, -100, "Transfer")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(seat.Host, seat.TripId, seat.InterestId, 0, "Cash")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(seat.Host, seat.TripId, seat.InterestId, 100, "Refund")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(seat.Host, seat.TripId, seat.InterestId, -1_000_001, "Refund")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(seat.Host, seat.TripId, seat.InterestId, Price, "Transfer")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(seat.Host, seat.TripId, seat.InterestId, 100, "Bitcoin")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(seat.Host, seat.TripId, seat.InterestId, 100, "1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(seat.Host, seat.TripId, seat.InterestId, 100, "Other", new string('x', 501))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (requester, _) = await UserAsync("Not Approved");
        var requested = await JoinAsync(requester, seat.TripId);
        (await RecordAsync(seat.Host, seat.TripId, requested, 100, "Cash")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // An interest on another of the host's trips is not on this one.
        var otherTrip = await CreateTripAsync(seat.Host, HostedTripBody());
        var (_, _, otherInterest) = await ApprovedTravellerAsync(seat.Host, otherTrip, "Elsewhere", acceptTerms: false);
        (await RecordAsync(seat.Host, seat.TripId, otherInterest, 100, "Cash")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(1_000_000);
        (await InterestAsync(otherInterest)).AmountPaidKobo.Should().Be(0);
    }

    [Fact]
    public async Task Manual_payment_recomputes_status_adds_traveller_to_chat_and_refund_lowers_it()
    {
        var seat = await ApprovedSeatAsync();
        (await InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeFalse();

        var recorded = await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, Deposit, "Transfer");
        recorded.GetProperty("paymentStatus").GetString().Should().Be("DepositPaid");
        recorded.GetProperty("amountPaidKobo").GetInt64().Should().Be(Deposit);
        recorded.GetProperty("amountPaidViaHodracKobo").GetInt64().Should().Be(0);
        recorded.GetProperty("payment").GetProperty("method").GetString().Should().Be("Transfer");
        recorded.GetProperty("payment").GetProperty("recordedByUserId").GetString().Should().Be(seat.HostUser.UserId);
        var transferId = recorded.GetProperty("payment").GetProperty("id").GetGuid();

        var interest = await InterestAsync(seat.InterestId);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.DepositPaid);
        interest.SeatHoldExpiresAt.Should().BeNull();
        (await InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeTrue();

        var refund = await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, -Deposit, "Refund");
        refund.GetProperty("paymentStatus").GetString().Should().Be("Refunded");
        var refundId = refund.GetProperty("payment").GetProperty("id").GetGuid();
        interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(0);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.Refunded);

        var listed = await GetJsonAsync(seat.Host, $"/api/host/trips/{seat.TripId}/manual-payments?interestId={seat.InterestId}");
        listed.EnumerateArray().Select(p => p.GetProperty("amountKobo").GetInt64()).Should().Equal(-Deposit, Deposit);

        // Deleting the transfer would leave a refund bigger than what was paid.
        (await seat.Host.DeleteAsync($"/api/host/trips/{seat.TripId}/manual-payments/{transferId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var deleted = await seat.Host.DeleteAsync($"/api/host/trips/{seat.TripId}/manual-payments/{refundId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());
        interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(Deposit);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.DepositPaid);
        (await seat.Host.DeleteAsync($"/api/host/trips/{seat.TripId}/manual-payments/{refundId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Manual_refund_of_hodrac_money_lowers_paid_and_status()
    {
        var seat = await ApprovedSeatAsync();
        await PayDepositAsync(seat.Traveller, seat.TripId, seat.InterestId);

        var refund = await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, -1_000_000, "Refund");
        refund.GetProperty("paymentStatus").GetString().Should().Be("PartPaid");

        var interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(Deposit - 1_000_000);
        interest.AmountPaidViaHodracKobo.Should().Be(Deposit);

        var row = (await GetJsonAsync(seat.Host, $"/api/host/trips/{seat.TripId}/manifest")).GetProperty("rows")[0];
        row.GetProperty("manualKobo").GetInt64().Should().Be(-1_000_000);
        row.GetProperty("owesKobo").GetInt64().Should().Be(Price - Deposit + 1_000_000);
    }

    [Fact]
    public async Task Manual_payment_on_withdrawn_traveller_is_credited_without_chat()
    {
        var seat = await ApprovedSeatAsync();
        (await seat.Traveller.DeleteAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, Deposit, "Cash");
        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(Deposit);
        (await InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeFalse();
    }

    // ── WhatsApp nudge ───────────────────────────────────────────────────────

    [Fact]
    public async Task WhatsApp_nudge_normalises_nigerian_phone_and_prefills_reminder()
    {
        var seat = await ApprovedSeatAsync("Ngozi Okafor", contactPhone: "0803 123 4567");

        var nudge = await GetJsonAsync(seat.Host, $"/api/host/trips/{seat.TripId}/whatsapp-nudge/{seat.InterestId}");
        nudge.GetProperty("phone").GetString().Should().Be("2348031234567");
        var url = nudge.GetProperty("url").GetString()!;
        url.Should().StartWith("https://wa.me/2348031234567?text=");

        var message = Uri.UnescapeDataString(url[(url.IndexOf("?text=", StringComparison.Ordinal) + 6)..]);
        message.Should().Be(nudge.GetProperty("message").GetString());
        message.Should().StartWith("Hi Ngozi,")
            .And.Contain("Manifest Host")
            .And.Contain("Zanzibar-")
            .And.Contain("₦250,000 left to pay")
            .And.Contain("Next payment: ₦50,000 due ")
            .And.EndWith($"/trips/{seat.TripId}");
    }

    [Fact]
    public async Task WhatsApp_nudge_without_phone_is_422()
    {
        var seat = await ApprovedSeatAsync();
        (await seat.Host.GetAsync($"/api/host/trips/{seat.TripId}/whatsapp-nudge/{seat.InterestId}"))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Join_request_rejects_an_invalid_contact_phone()
    {
        var (host, _, _) = await HostAsync("Phone Host");
        var tripId = await CreateTripAsync(host, HostedTripBody());
        var (traveller, _) = await UserAsync("Phone Traveller");
        (await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests", new { message = "hi", contactPhone = "call me" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("08031234567", "2348031234567")]
    [InlineData("0803 123 4567", "2348031234567")]
    [InlineData("+234 803 123 4567", "2348031234567")]
    [InlineData("+234 (0) 803-123-4567", "2348031234567")]
    [InlineData("2348031234567", "2348031234567")]
    [InlineData("002348031234567", "2348031234567")]
    [InlineData("8031234567", "2348031234567")]
    [InlineData("+447911123456", "447911123456")]
    [InlineData("", null)]
    [InlineData("12345", null)]
    [InlineData("call me", null)]
    public void Normalises_whatsapp_phone(string raw, string? expected) =>
        HostTripService.NormalizeWhatsAppPhone(raw).Should().Be(expected);

    // ── Weekly report ────────────────────────────────────────────────────────

    [Fact]
    public async Task Weekly_report_counts_a_seeded_week()
    {
        var seat = await ApprovedSeatAsync();
        await PayDepositAsync(seat.Traveller, seat.TripId, seat.InterestId);
        var inWeek = await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, 1_000_000, "Transfer");
        var afterWeek = await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, 500_000, "Cash");
        var (requester, _) = await UserAsync("Week Requester");
        var requested = await JoinAsync(requester, seat.TripId);

        // A Monday-to-Sunday WAT week two weeks back; WAT midnight is 23:00 UTC the day before.
        var weekStart = HostReportService.MondayOf(HostTripService.TodayWat(DateTime.UtcNow)).AddDays(-14);
        DateTime Wat(int day, int hour) =>
            DateTime.SpecifyKind(weekStart.AddDays(day).ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc).AddHours(-1);

        var (monday0, tuesday9, sunday23, wednesday12, thursday12, nextMonday0) =
            (Wat(0, 0), Wat(1, 9), Wat(6, 23), Wat(2, 12), Wat(3, 12), Wat(7, 0));
        await WithDbAsync(async db =>
        {
            await db.TripInterests.Where(i => i.Id == seat.InterestId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.RequestedAt, monday0)       // Monday 00:00 WAT: in
                .SetProperty(i => i.RespondedAt, tuesday9));
            await db.TripInterests.Where(i => i.Id == requested).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.RequestedAt, sunday23));    // Sunday 23:00 WAT: in
            await db.SeatPayments.Where(p => p.TripInterestId == seat.InterestId).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PaidAt, wednesday12));
            var inWeekId = inWeek.GetProperty("payment").GetProperty("id").GetGuid();
            var afterWeekId = afterWeek.GetProperty("payment").GetProperty("id").GetGuid();
            await db.ManualPayments.Where(m => m.Id == inWeekId).ExecuteUpdateAsync(s => s.SetProperty(m => m.RecordedAt, thursday12));
            await db.ManualPayments.Where(m => m.Id == afterWeekId).ExecuteUpdateAsync(s => s.SetProperty(m => m.RecordedAt, nextMonday0));
            db.TripPageViewsDaily.AddRange(
                new TripPageViewDaily { TripPostId = seat.TripId, Date = weekStart, Source = TripPageViewSources.Share, Count = 5 },
                new TripPageViewDaily { TripPostId = seat.TripId, Date = weekStart.AddDays(6), Source = TripPageViewSources.PublicApi, Count = 3 },
                new TripPageViewDaily { TripPostId = seat.TripId, Date = weekStart.AddDays(7), Source = TripPageViewSources.Share, Count = 11 },
                new TripPageViewDaily { TripPostId = seat.TripId, Date = weekStart.AddDays(-1), Source = TripPageViewSources.Share, Count = 13 });
            return await db.SaveChangesAsync();
        });

        // Any day of the week is moved back to its Monday.
        var report = await GetJsonAsync(seat.Host, $"/api/host/reports/weekly?weekStart={weekStart.AddDays(3):yyyy-MM-dd}");
        report.GetProperty("weekStart").GetString().Should().Be(weekStart.ToString("yyyy-MM-dd"));
        report.GetProperty("weekEnd").GetString().Should().Be(weekStart.AddDays(6).ToString("yyyy-MM-dd"));
        report.GetProperty("hostProfileId").GetGuid().Should().Be(seat.HostId);

        var trips = report.GetProperty("trips").EnumerateArray().ToList();
        trips.Should().ContainSingle();
        var stats = trips[0].GetProperty("stats");
        stats.GetProperty("pageViews").GetInt32().Should().Be(8);
        stats.GetProperty("pageViewsBySource").GetProperty("share").GetInt32().Should().Be(5);
        stats.GetProperty("pageViewsBySource").GetProperty("public-api").GetInt32().Should().Be(3);
        stats.GetProperty("linkClicks").GetInt32().Should().Be(5);
        stats.GetProperty("joinRequests").GetInt32().Should().Be(2);
        stats.GetProperty("approvals").GetInt32().Should().Be(1);
        stats.GetProperty("seatsPaid").GetInt32().Should().Be(1);
        stats.GetProperty("collectedViaHodracKobo").GetInt64().Should().Be(Deposit);
        stats.GetProperty("collectedManualKobo").GetInt64().Should().Be(1_000_000);
        stats.GetProperty("refundedManualKobo").GetInt64().Should().Be(0);
        stats.GetProperty("hodracSharePercentWeek").GetDecimal().Should().Be(83.3m);       // 5 / 6
        stats.GetProperty("hodracSharePercentAllTime").GetDecimal().Should().Be(76.9m);    // 5 / 6.5

        var totals = report.GetProperty("totals");
        totals.GetProperty("pageViews").GetInt32().Should().Be(8);
        totals.GetProperty("collectedViaHodracKobo").GetInt64().Should().Be(Deposit);
        totals.GetProperty("hodracSharePercentAllTime").GetDecimal().Should().Be(76.9m);

        // The week after: only the late cash payment, which isn't a first payment.
        var next = (await GetJsonAsync(seat.Host, $"/api/host/reports/weekly?weekStart={weekStart.AddDays(7):yyyy-MM-dd}"))
            .GetProperty("totals");
        next.GetProperty("pageViews").GetInt32().Should().Be(11);
        next.GetProperty("seatsPaid").GetInt32().Should().Be(0);
        next.GetProperty("collectedManualKobo").GetInt64().Should().Be(500_000);
        next.GetProperty("hodracSharePercentWeek").GetDecimal().Should().Be(0m);

        // Default: the last full Mon–Sun week.
        var latest = await GetJsonAsync(seat.Host, "/api/host/reports/weekly");
        latest.GetProperty("weekStart").GetString().Should()
            .Be(HostReportService.LastFullWeekStart(DateTime.UtcNow).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Weekly_report_is_404_without_a_host_profile()
    {
        var (user, _) = await UserAsync("Not A Host");
        (await user.GetAsync("/api/host/reports/weekly")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetJsonAsync(user, "/api/host/trips")).GetArrayLength().Should().Be(0);
    }
}
