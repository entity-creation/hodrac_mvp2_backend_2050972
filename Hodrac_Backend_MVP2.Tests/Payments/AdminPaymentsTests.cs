using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Payments;

/// <summary>/api/admin/payments (search + detail) and /api/admin/payment-events (webhook audit + reprocess).</summary>
[Collection(IntegrationCollection.Name)]
public class AdminPaymentsTests
{
    private const long Price = 25_000_000;
    private const long Deposit = 5_000_000;
    private const long Instalment = 10_000_000;

    private readonly HodracApiFactory _factory;

    public AdminPaymentsTests(HodracApiFactory factory) => _factory = factory;

    private record Seat(HttpClient Traveller, TestUser TravellerUser, Guid HostId, Guid TripId, Guid InterestId);

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

    private async Task<Seat> ApprovedSeatAsync()
    {
        var hostUser = await AuthHelper.RegisterAsync(_factory.CreateClient(), "Audit Host");
        var host = _factory.CreateClient().Authenticated(hostUser);
        var hostId = await CreatePendingHostAsync(host, "Audit Host");
        var admin = await AdminAsync();
        (await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var create = await host.PostAsJsonAsync("/api/trips", new
        {
            freeTextDestination = $"Audit-{Guid.NewGuid():N}",
            startDate = Day(90),
            endDate = Day(95),
            isDateFlexible = false,
            caption = "Audit trip",
            maxGroupSize = 6,
            isHostedTrip = true,
            pricePerSeatKobo = Price,
            depositKobo = Deposit,
            instalments = new[]
            {
                new { sequence = 1, amountKobo = Instalment, dueDate = Day(30) },
                new { sequence = 2, amountKobo = Instalment, dueDate = Day(60) },
            },
            termsText = "Deposit is non-refundable.",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var tripId = (await create.ReadJsonAsync()).GetProperty("id").GetGuid();

        var travellerUser = await AuthHelper.RegisterAsync(_factory.CreateClient(), "Audit Traveller");
        var traveller = _factory.CreateClient().Authenticated(travellerUser);
        var join = await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests", new { message = "Me" });
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
        var interestId = (await join.ReadJsonAsync()).GetProperty("id").GetGuid();

        (await host.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms", new { termsVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return new Seat(traveller, travellerUser, hostId, tripId, interestId);
    }

    private static async Task<(string Reference, long Total)> CheckoutAsync(Seat seat)
    {
        var response = await seat.Traveller.PostAsJsonAsync(
            $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/checkout", new { kind = "Deposit" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.ReadJsonAsync();
        return (json.GetProperty("reference").GetString()!, json.GetProperty("totalKobo").GetInt64());
    }

    private static string ChargeSuccessBody(string reference, long amount) =>
        $"{{\"event\":\"charge.success\",\"data\":{{\"id\":4000000001,\"status\":\"success\",\"reference\":\"{reference}\",\"amount\":{amount},\"currency\":\"NGN\",\"nonce\":\"{Guid.NewGuid():N}\"}}}}";

    private async Task PostWebhookAsync(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/paystack/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(PaystackSignature.HeaderName,
            PaystackSignature.ComputeHex(Encoding.UTF8.GetBytes(body), HodracApiFactory.PaystackSecretKey));
        var response = await _factory.CreateClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    // ── Payments search ──────────────────────────────────────────────────────

    [Fact]
    public async Task Payments_search_filters_and_detail()
    {
        var seat = await ApprovedSeatAsync();
        var (paidRef, total) = await CheckoutAsync(seat);
        await PostWebhookAsync(ChargeSuccessBody(paidRef, total));

        // Another host's trip, where the traveller's attempt stays Initialized.
        var other = await ApprovedSeatAsync();
        var (pendingRef, _) = await CheckoutAsync(other);

        var admin = await AdminAsync();

        // Reference is exact.
        var byRef = await (await admin.GetAsync($"/api/admin/payments?reference={paidRef}")).ReadJsonAsync();
        byRef.GetProperty("totalCount").GetInt32().Should().Be(1);
        var row = Items(byRef).Single();
        row.GetProperty("reference").GetString().Should().Be(paidRef);
        row.GetProperty("status").GetString().Should().Be("Success");
        row.GetProperty("kind").GetString().Should().Be("Deposit");
        row.GetProperty("trip").GetProperty("id").GetGuid().Should().Be(seat.TripId);
        row.GetProperty("trip").GetProperty("title").GetString().Should().StartWith("Audit-");
        row.GetProperty("host").GetProperty("id").GetGuid().Should().Be(seat.HostId);
        row.GetProperty("host").GetProperty("displayName").GetString().Should().Be("Audit Host");
        row.GetProperty("payer").GetProperty("email").GetString().Should().Be(seat.TravellerUser.Email);
        row.GetProperty("payer").GetProperty("name").GetString().Should().Be("Audit Traveller");
        row.GetProperty("seatAmountKobo").GetInt64().Should().Be(Deposit);
        row.GetProperty("totalChargedKobo").GetInt64().Should().Be(total);
        row.GetProperty("paidAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        row.ToString().Should().NotContain("ACCT_").And.NotContain("subaccount", "no subaccount codes in payment rows");
        var partial = await (await admin.GetAsync($"/api/admin/payments?reference={paidRef[..^2]}")).ReadJsonAsync();
        partial.GetProperty("totalCount").GetInt32().Should().Be(0);

        // Trip filter.
        var byTrip = await (await admin.GetAsync($"/api/admin/payments?tripId={seat.TripId}")).ReadJsonAsync();
        Items(byTrip).Select(i => i.GetProperty("reference").GetString()).Should().Equal(paidRef);

        // Host filter.
        var byHost = await (await admin.GetAsync($"/api/admin/payments?hostId={other.HostId}")).ReadJsonAsync();
        Items(byHost).Select(i => i.GetProperty("reference").GetString()).Should().Equal(pendingRef);

        // Payer email (case-insensitive substring) + status.
        var email = seat.TravellerUser.Email.ToUpperInvariant();
        var byEmail = await (await admin.GetAsync($"/api/admin/payments?payerEmail={Uri.EscapeDataString(email)}")).ReadJsonAsync();
        Items(byEmail).Select(i => i.GetProperty("reference").GetString()).Should().Equal(paidRef);
        var byEmailInitialized = await (await admin.GetAsync(
            $"/api/admin/payments?payerEmail={Uri.EscapeDataString(email)}&status=initialized")).ReadJsonAsync();
        byEmailInitialized.GetProperty("totalCount").GetInt32().Should().Be(0);

        var initialized = await (await admin.GetAsync($"/api/admin/payments?status=Initialized&hostId={other.HostId}")).ReadJsonAsync();
        Items(initialized).Select(i => i.GetProperty("reference").GetString()).Should().Equal(pendingRef);

        // Date range (WAT dates on CreatedAt): today matches, yesterday doesn't.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
        (await (await admin.GetAsync($"/api/admin/payments?tripId={seat.TripId}&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).ReadJsonAsync())
            .GetProperty("totalCount").GetInt32().Should().Be(1);
        (await (await admin.GetAsync($"/api/admin/payments?tripId={seat.TripId}&to={today.AddDays(-1):yyyy-MM-dd}")).ReadJsonAsync())
            .GetProperty("totalCount").GetInt32().Should().Be(0);

        (await admin.GetAsync("/api/admin/payments?status=Nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Detail: raw verify response + the webhook event for the reference.
        var detailResponse = await admin.GetAsync($"/api/admin/payments/{paidRef}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.ReadJsonAsync();
        detail.GetProperty("payment").GetProperty("reference").GetString().Should().Be(paidRef);
        detail.GetProperty("tripInterestId").GetGuid().Should().Be(seat.InterestId);
        detail.GetProperty("verifyResponseJson").GetString().Should().Contain(paidRef);
        var events = detail.GetProperty("webhookEvents").EnumerateArray().ToList();
        events.Should().ContainSingle();
        events[0].GetProperty("eventType").GetString().Should().Be("charge.success");
        events[0].GetProperty("processedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        (await admin.GetAsync("/api/admin/payments/HDR-NOPE")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Non_admin_gets_403_on_payments_and_events()
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), "Curious User");
        var client = _factory.CreateClient().Authenticated(user);

        (await client.GetAsync("/api/admin/payments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/admin/payments/HDR-X")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/admin/payment-events")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/admin/payment-events/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/admin/payment-events/{Guid.NewGuid()}/reprocess", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/admin/insights/hosting")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync("/api/admin/payments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Webhook audit ────────────────────────────────────────────────────────

    [Fact]
    public async Task Failed_event_is_listed_as_unprocessed_and_reprocess_credits_once()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total) = await CheckoutAsync(seat);

        // Paystack's verify is down when the webhook arrives: the event is stored with an error.
        _factory.Paystack.VerifyResponses[reference] = _ => throw new PaystackException("Gateway timeout", HttpStatusCode.GatewayTimeout);
        var body = ChargeSuccessBody(reference, total);
        try
        {
            await PostWebhookAsync(body);
        }
        finally
        {
            _factory.Paystack.VerifyResponses.TryRemove(reference, out _);
        }

        var admin = await AdminAsync();
        var list = await (await admin.GetAsync($"/api/admin/payment-events?unprocessed=true&reference={reference}")).ReadJsonAsync();
        var evt = Items(list).Single();
        evt.GetProperty("eventType").GetString().Should().Be("charge.success");
        evt.GetProperty("processedAt").ValueKind.Should().Be(JsonValueKind.Null);
        evt.GetProperty("error").GetString().Should().Contain("Gateway timeout");
        var eventId = evt.GetProperty("id").GetGuid();

        // eventType filter (case-insensitive) and the processed one isn't "unprocessed".
        (await (await admin.GetAsync($"/api/admin/payment-events?eventType=CHARGE.SUCCESS&reference={reference}")).ReadJsonAsync())
            .GetProperty("totalCount").GetInt32().Should().Be(1);
        (await (await admin.GetAsync($"/api/admin/payment-events?eventType=refund.processed&reference={reference}")).ReadJsonAsync())
            .GetProperty("totalCount").GetInt32().Should().Be(0);

        // Detail includes the raw payload.
        var detail = await (await admin.GetAsync($"/api/admin/payment-events/{eventId}")).ReadJsonAsync();
        // Stored as jsonb: same content, normalised formatting.
        using (var payload = JsonDocument.Parse(detail.GetProperty("payload").GetString()!))
        {
            payload.RootElement.GetProperty("event").GetString().Should().Be("charge.success");
            payload.RootElement.GetProperty("data").GetProperty("reference").GetString().Should().Be(reference);
            payload.RootElement.GetProperty("data").GetProperty("amount").GetInt64().Should().Be(total);
        }
        detail.GetProperty("event").GetProperty("reference").GetString().Should().Be(reference);
        (await admin.GetAsync($"/api/admin/payment-events/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await WithDbAsync(db => db.SeatPayments.AsNoTracking().FirstAsync(p => p.Reference == reference)))
            .Status.Should().Be(SeatPaymentStatus.Initialized);

        // Reprocess: verified now, credited.
        var first = await admin.PostAsync($"/api/admin/payment-events/{eventId}/reprocess", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var firstJson = await first.ReadJsonAsync();
        firstJson.GetProperty("outcome").GetString().Should().Be("Processed");
        firstJson.GetProperty("event").GetProperty("processedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        firstJson.GetProperty("event").GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);

        var payment = await WithDbAsync(db => db.SeatPayments.AsNoTracking().FirstAsync(p => p.Reference == reference));
        payment.Status.Should().Be(SeatPaymentStatus.Success);
        var interest = await WithDbAsync(db => db.TripInterests.AsNoTracking().FirstAsync(i => i.Id == seat.InterestId));
        interest.AmountPaidKobo.Should().Be(Deposit);

        // Again: nothing re-run, still credited once.
        var verifyCalls = _factory.Paystack.VerifyCalls;
        var second = await admin.PostAsync($"/api/admin/payment-events/{eventId}/reprocess", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.ReadJsonAsync()).GetProperty("outcome").GetString().Should().Be("AlreadyProcessed");
        _factory.Paystack.VerifyCalls.Should().Be(verifyCalls);
        (await WithDbAsync(db => db.TripInterests.AsNoTracking().FirstAsync(i => i.Id == seat.InterestId)))
            .AmountPaidKobo.Should().Be(Deposit);

        (await (await admin.GetAsync($"/api/admin/payment-events?unprocessed=true&reference={reference}")).ReadJsonAsync())
            .GetProperty("totalCount").GetInt32().Should().Be(0);
        (await admin.PostAsync($"/api/admin/payment-events/{Guid.NewGuid()}/reprocess", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reprocessing_an_event_for_an_already_credited_payment_does_not_credit_twice()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total) = await CheckoutAsync(seat);
        await PostWebhookAsync(ChargeSuccessBody(reference, total));

        // A second, differently-bodied delivery that failed (e.g. a transient
        // error), stored as unprocessed though the payment is already credited.
        var eventId = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = eventId,
                EventType = "charge.success",
                Reference = reference,
                PayloadSha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                Payload = ChargeSuccessBody(reference, total),
                Error = "Paystack: transient",
            });
            return await db.SaveChangesAsync();
        });

        var admin = await AdminAsync();
        var response = await admin.PostAsync($"/api/admin/payment-events/{eventId}/reprocess", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.ReadJsonAsync()).GetProperty("outcome").GetString().Should().Be("Processed");

        (await WithDbAsync(db => db.TripInterests.AsNoTracking().FirstAsync(i => i.Id == seat.InterestId)))
            .AmountPaidKobo.Should().Be(Deposit);
        (await WithDbAsync(db => db.SeatPayments.CountAsync(p => p.TripInterestId == seat.InterestId && p.Status == SeatPaymentStatus.Success)))
            .Should().Be(1);
    }
}
