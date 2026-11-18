using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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

/// <summary>
/// Phase 4: terms, balance, checkout, verify, receipts, the Paystack webhook
/// and PaymentService.ApplyAsync (idempotency, races, mismatches, refunds,
/// disputes). Paystack is the in-memory FakePaystackClient.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PaymentFlowTests
{
    private const long Price = 25_000_000;   // ₦250,000
    private const long Deposit = 5_000_000;  // ₦50,000
    private const long Instalment = 10_000_000;
    private const long DepositFee = 125_000; // 2.5% of the deposit

    private readonly HodracApiFactory _factory;

    public PaymentFlowTests(HodracApiFactory factory) => _factory = factory;

    private FakePaystackClient Paystack => _factory.Paystack;

    // ── Setup helpers ────────────────────────────────────────────────────────

    private record Seat(
        HttpClient Host, TestUser HostUser, string SubaccountCode,
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

    private async Task<(HttpClient Client, TestUser User, Guid HostId)> HostAsync(string name, bool verified = true)
    {
        var (client, user) = await UserAsync(name);
        var hostId = await CreatePendingHostAsync(client, name);
        if (verified)
        {
            var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
            var verify = await _factory.CreateClient().Authenticated(admin).PostAsync($"/api/admin/hosts/{hostId}/verify", null);
            verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        }
        return (client, user, hostId);
    }

    private static object HostedTripBody(string feeMode = "PassToTraveller") => new
    {
        freeTextDestination = $"Zanzibar-{Guid.NewGuid():N}",
        startDate = Day(90),
        endDate = Day(95),
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
        feeMode,
        termsText = "Deposit is non-refundable. Instalments refundable until 30 days before departure.",
    };

    private async Task<Guid> CreateTripAsync(HttpClient client, object body)
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

    /// <summary>Verified host, hosted trip, traveller requested + approved (+ terms accepted).</summary>
    private async Task<Seat> ApprovedSeatAsync(
        string feeMode = "PassToTraveller", bool acceptTerms = true, bool verifiedHost = true)
    {
        var (host, hostUser, hostId) = await HostAsync("Pay Host", verifiedHost);
        var tripId = await CreateTripAsync(host, HostedTripBody(feeMode));
        var (traveller, travellerUser) = await UserAsync("Pay Traveller");
        var interestId = await JoinAsync(traveller, tripId);

        var approve = await host.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve = true });
        approve.StatusCode.Should().Be(HttpStatusCode.NoContent, await approve.Content.ReadAsStringAsync());

        if (acceptTerms)
        {
            var accept = await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms", new { termsVersion = 1 });
            accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        }

        var code = await WithDbAsync(db => db.HostProfiles.Where(h => h.Id == hostId).Select(h => h.PaystackSubaccountCode!).FirstAsync());
        return new Seat(host, hostUser, code, traveller, travellerUser, tripId, interestId);
    }

    private static Task<HttpResponseMessage> CheckoutAsync(HttpClient client, Seat seat, string kind, int? sequence = null) =>
        client.PostAsJsonAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/checkout", new { kind, instalmentSequence = sequence });

    private static async Task<(string Reference, long Total, string Url)> CheckoutOkAsync(Seat seat, string kind = "Deposit", int? sequence = null)
    {
        var response = await CheckoutAsync(seat.Traveller, seat, kind, sequence);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.ReadJsonAsync();
        return (json.GetProperty("reference").GetString()!, json.GetProperty("totalKobo").GetInt64(), json.GetProperty("authorizationUrl").GetString()!);
    }

    private static string ChargeSuccessBody(string reference, long amount, string? extra = null) =>
        $"{{\"event\":\"charge.success\",\"data\":{{\"id\":4000000001,\"status\":\"success\",\"reference\":\"{reference}\",\"amount\":{amount},\"currency\":\"NGN\"{extra}}}}}";

    private Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature = "valid")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/paystack/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
        {
            var sig = signature == "valid"
                ? PaystackSignature.ComputeHex(Encoding.UTF8.GetBytes(body), HodracApiFactory.PaystackSecretKey)
                : signature;
            request.Headers.Add(PaystackSignature.HeaderName, sig);
        }
        return _factory.CreateClient().SendAsync(request);
    }

    private async Task WebhookOkAsync(string body)
    {
        var response = await PostWebhookAsync(body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private Task<TripInterest> InterestAsync(Guid interestId) =>
        WithDbAsync(db => db.TripInterests.AsNoTracking().FirstAsync(i => i.Id == interestId));

    private Task<SeatPayment> PaymentAsync(string reference) =>
        WithDbAsync(db => db.SeatPayments.AsNoTracking().FirstAsync(p => p.Reference == reference));

    private Task<bool> InThreadAsync(Guid tripId, string userId) => WithDbAsync(db =>
        db.TripThreadParticipants.AnyAsync(p => p.UserId == Guid.Parse(userId)
            && db.TripThreads.Any(t => t.Id == p.TripThreadId && t.TripPostId == tripId)));

    private Task<int> NotificationCountAsync(Guid tripId, NotificationType type) =>
        WithDbAsync(db => db.Notifications.CountAsync(n => n.TripPostId == tripId && n.Type == type));

    /// <summary>Deposit checkout + charge.success webhook.</summary>
    private async Task<string> PayDepositAsync(Seat seat)
    {
        var (reference, total, _) = await CheckoutOkAsync(seat);
        await WebhookOkAsync(ChargeSuccessBody(reference, total));
        return reference;
    }

    // ── Checkout: Paystack split and fees ────────────────────────────────────

    [Fact]
    public async Task Checkout_sends_split_with_fee_passed_to_traveller()
    {
        var seat = await ApprovedSeatAsync();

        var (reference, total, url) = await CheckoutOkAsync(seat);

        total.Should().Be(Deposit + DepositFee);
        url.Should().Contain(reference);
        var init = Paystack.Initialized[reference];
        init.AmountKobo.Should().Be(Deposit + DepositFee);
        init.TransactionChargeKobo.Should().Be(DepositFee);
        init.Bearer.Should().Be("subaccount");
        init.SubaccountCode.Should().Be(seat.SubaccountCode);
        init.Currency.Should().Be("NGN");
        init.Email.Should().Be(seat.TravellerUser.Email);
        init.Metadata!["tripId"].Should().Be(seat.TripId.ToString());
        init.Metadata["interestId"].Should().Be(seat.InterestId.ToString());
        init.Metadata["kind"].Should().Be("Deposit");

        var payment = await PaymentAsync(reference);
        payment.Status.Should().Be(SeatPaymentStatus.Initialized);
        payment.SeatAmountKobo.Should().Be(Deposit);
        payment.HodracFeeKobo.Should().Be(DepositFee);
        payment.TravellerFeeKobo.Should().Be(DepositFee);
        payment.TotalChargedKobo.Should().Be(Deposit + DepositFee);
        payment.SubaccountCode.Should().Be(seat.SubaccountCode);
        init.Metadata["seatPaymentId"].Should().Be(payment.Id.ToString());
    }

    [Fact]
    public async Task Checkout_when_host_absorbs_fee_charges_seat_amount_only()
    {
        var seat = await ApprovedSeatAsync(feeMode: "HostAbsorbs");

        var (reference, total, _) = await CheckoutOkAsync(seat);

        total.Should().Be(Deposit);
        var init = Paystack.Initialized[reference];
        init.AmountKobo.Should().Be(Deposit);
        init.TransactionChargeKobo.Should().Be(DepositFee);
        init.Bearer.Should().Be("subaccount");
        var payment = await PaymentAsync(reference);
        payment.HodracFeeKobo.Should().Be(DepositFee);
        payment.TravellerFeeKobo.Should().Be(0);
    }

    [Fact]
    public async Task Checkout_reuses_a_fresh_identical_attempt()
    {
        var seat = await ApprovedSeatAsync();

        var first = await CheckoutOkAsync(seat);
        // The traveller is still on Paystack's page: not paid yet.
        Paystack.VerifyResponses[first.Reference] = _ => FakePaystackClient.Transaction(
            first.Reference, "ongoing", first.Total, "NGN", seat.SubaccountCode);
        var second = await CheckoutOkAsync(seat);

        second.Reference.Should().Be(first.Reference);
        second.Url.Should().Be(first.Url);
        (await WithDbAsync(db => db.SeatPayments.CountAsync(p => p.TripInterestId == seat.InterestId))).Should().Be(1);
    }

    [Fact]
    public async Task Checkout_credits_a_paid_but_unrecorded_checkout_instead_of_charging_again()
    {
        var seat = await ApprovedSeatAsync();

        // Paid on Paystack, but no webhook and no callback page yet.
        var (depositRef, _, _) = await CheckoutOkAsync(seat);

        var again = await CheckoutAsync(seat.Traveller, seat, "Deposit");
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "the deposit was already paid");
        (await PaymentAsync(depositRef)).Status.Should().Be(SeatPaymentStatus.Success);

        // Paying the rest charges only what's left, not the deposit again.
        var (_, total, _) = await CheckoutOkAsync(seat, "Full");
        total.Should().Be(Price - Deposit + (long)Math.Round((Price - Deposit) * 0.025m, MidpointRounding.AwayFromZero));
        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(Deposit);
    }

    [Fact]
    public async Task Overpayment_from_two_open_checkouts_is_credited_and_flagged()
    {
        var seat = await ApprovedSeatAsync();
        var (depositRef, depositTotal, _) = await CheckoutOkAsync(seat);
        Paystack.VerifyResponses[depositRef] = _ => FakePaystackClient.Transaction(
            depositRef, "abandoned", depositTotal, "NGN", seat.SubaccountCode);

        // The traveller switches to paying in full and pays it...
        var (fullRef, fullTotal, _) = await CheckoutOkAsync(seat, "Full");
        await WebhookOkAsync(ChargeSuccessBody(fullRef, fullTotal));
        (await InterestAsync(seat.InterestId)).PaymentStatus.Should().Be(InterestPaymentStatus.PaidInFull);

        // ...then also completes the old deposit page.
        _factory.Emails.Sent.Clear();
        Paystack.VerifyResponses.TryRemove(depositRef, out _);
        await WebhookOkAsync(ChargeSuccessBody(depositRef, depositTotal));

        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(Price + Deposit, "the money moved, so it's recorded");
        _factory.Emails.Sent.Should().Contain(e => e.To == HodracApiFactory.AdminEmail && e.Subject == "Payment needs attention"
            && e.BodyLines.Any(l => l.Contains("too much")));
        _factory.Emails.Sent.Should().Contain(e => e.To == seat.HostUser.Email && e.Subject == "A payment on your trip needs attention");
    }

    [Fact]
    public async Task Payment_after_withdrawal_is_flagged()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total, _) = await CheckoutOkAsync(seat);
        await WithDbAsync(db => db.TripInterests.Where(i => i.Id == seat.InterestId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, TripInterestStatus.Withdrawn)));

        _factory.Emails.Sent.Clear();
        await WebhookOkAsync(ChargeSuccessBody(reference, total));

        _factory.Emails.Sent.Should().Contain(e => e.To == HodracApiFactory.AdminEmail && e.Subject == "Payment needs attention"
            && e.BodyLines.Any(l => l.Contains("withdrawn")));
    }

    [Fact]
    public async Task Normal_payments_raise_no_attention_alert()
    {
        var seat = await ApprovedSeatAsync();
        _factory.Emails.Sent.Clear();
        await PayDepositAsync(seat);
        _factory.Emails.Sent.Should().NotContain(e => e.Subject.Contains("needs attention"));
    }

    [Fact]
    public async Task Checkout_Paystack_failure_marks_attempt_failed_and_returns_502()
    {
        var seat = await ApprovedSeatAsync();
        Paystack.FailInitializeForEmail[seat.TravellerUser.Email] = true;
        try
        {
            var response = await CheckoutAsync(seat.Traveller, seat, "Deposit");
            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            var statuses = await WithDbAsync(db => db.SeatPayments
                .Where(p => p.TripInterestId == seat.InterestId).Select(p => p.Status).ToListAsync());
            statuses.Should().Equal(SeatPaymentStatus.Failed);
        }
        finally
        {
            Paystack.FailInitializeForEmail.TryRemove(seat.TravellerUser.Email, out _);
        }
    }

    // ── First payment: credit, chat, hold, receipt, notifications ───────────

    [Fact]
    public async Task Deposit_paid_via_webhook_credits_seat_adds_to_chat_and_clears_hold()
    {
        var seat = await ApprovedSeatAsync();
        (await InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeFalse();
        (await InterestAsync(seat.InterestId)).SeatHoldExpiresAt.Should().NotBeNull();
        _factory.Emails.Sent.Clear();

        var reference = await PayDepositAsync(seat);

        var payment = await PaymentAsync(reference);
        payment.Status.Should().Be(SeatPaymentStatus.Success);
        payment.PaidAt.Should().NotBeNull();
        payment.Channel.Should().Be("card");
        payment.PaystackFeeKobo.Should().Be(7_850);
        payment.VerifyResponseJson.Should().Contain(reference);

        var interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(Deposit);
        interest.AmountPaidViaHodracKobo.Should().Be(Deposit);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.DepositPaid);
        interest.SeatHoldExpiresAt.Should().BeNull();
        (await InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeTrue();

        _factory.Emails.Sent.Should().Contain(e => e.To == seat.TravellerUser.Email && e.Subject.Contains("receipt")
            && e.BodyLines.Any(l => l.Contains(reference)));
        (await NotificationCountAsync(seat.TripId, NotificationType.PaymentReceived)).Should().Be(1);
        (await NotificationCountAsync(seat.TripId, NotificationType.PaymentConfirmed)).Should().Be(1);
    }

    [Fact]
    public async Task Paying_everything_left_marks_paid_in_full()
    {
        var seat = await ApprovedSeatAsync();
        await PayDepositAsync(seat);

        var (reference, total, _) = await CheckoutOkAsync(seat, "Full");
        total.Should().Be(2 * Instalment + 2 * Instalment * 25 / 1000);
        await WebhookOkAsync(ChargeSuccessBody(reference, total));

        var interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(Price);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.PaidInFull);

        var balance = await (await seat.Traveller.GetAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/balance")).ReadJsonAsync();
        balance.GetProperty("owesKobo").GetInt64().Should().Be(0);
        balance.GetProperty("nextPayment").ValueKind.Should().Be(JsonValueKind.Null);
        balance.GetProperty("schedule").EnumerateArray().Select(l => l.GetProperty("status").GetString()).Should().AllBe("Paid");
    }

    // ── Idempotency and races ────────────────────────────────────────────────

    [Fact]
    public async Task Same_charge_success_twice_credits_once()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total, _) = await CheckoutOkAsync(seat);
        var body = ChargeSuccessBody(reference, total);

        await WebhookOkAsync(body);
        await WebhookOkAsync(body);                                                 // exact duplicate: deduped
        await WebhookOkAsync(ChargeSuccessBody(reference, total, ",\"retry\":1"));  // same charge, new payload

        var events = await WithDbAsync(db => db.PaymentWebhookEvents.Where(e => e.Reference == reference).ToListAsync());
        events.Should().HaveCount(2);
        events.Should().OnlyContain(e => e.ProcessedAt != null && e.Error == null && e.EventType == "charge.success");

        var interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(Deposit);
        (await NotificationCountAsync(seat.TripId, NotificationType.PaymentReceived)).Should().Be(1);
    }

    [Fact]
    public async Task Verify_endpoint_racing_webhook_credits_once()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total, _) = await CheckoutOkAsync(seat);

        var responses = await Task.WhenAll(
            seat.Traveller.GetAsync($"/api/payments/verify/{reference}"),
            PostWebhookAsync(ChargeSuccessBody(reference, total)),
            seat.Traveller.GetAsync($"/api/payments/verify/{reference}"));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var verify = await responses[0].ReadJsonAsync();
        verify.GetProperty("status").GetString().Should().Be("Success");
        verify.GetProperty("receipt").GetProperty("reference").GetString().Should().Be(reference);

        var interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(Deposit);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.DepositPaid);
        (await NotificationCountAsync(seat.TripId, NotificationType.PaymentReceived)).Should().Be(1);
        (await WithDbAsync(db => db.TripThreadParticipants.CountAsync(p => p.UserId == Guid.Parse(seat.TravellerUser.UserId)))).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_ApplyAsync_calls_credit_once()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, _, _) = await CheckoutOkAsync(seat);
        var verified = await Paystack.VerifyAsync(reference);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PaymentService>().ApplyAsync(verified);
        }));

        results.Count(r => r.Outcome == ApplyOutcome.Applied).Should().Be(1);
        results.Count(r => r.Outcome == ApplyOutcome.AlreadyApplied).Should().Be(4);
        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(Deposit);
    }

    // ── Mismatches ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("amount")]
    [InlineData("subaccount")]
    [InlineData("currency")]
    public async Task Mismatched_verify_response_is_not_credited(string field)
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total, _) = await CheckoutOkAsync(seat);
        Paystack.VerifyResponses[reference] = _ => FakePaystackClient.Transaction(
            reference, "success",
            field == "amount" ? total - 100 : total,
            field == "currency" ? "USD" : "NGN",
            field == "subaccount" ? "ACCT_someone_else" : seat.SubaccountCode);
        _factory.Emails.Sent.Clear();

        await WebhookOkAsync(ChargeSuccessBody(reference, total));
        var verify = await seat.Traveller.GetAsync($"/api/payments/verify/{reference}");
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        (await verify.ReadJsonAsync()).GetProperty("status").GetString().Should().Be("Failed");

        var payment = await PaymentAsync(reference);
        payment.Status.Should().Be(SeatPaymentStatus.Failed);
        payment.VerifyResponseJson.Should().Contain("hodracMismatch").And.Contain(field);
        var interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(0);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);
        (await InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeFalse();
        _factory.Emails.Sent.Should().Contain(e => e.To == HodracApiFactory.AdminEmail && e.Subject.Contains("mismatch"));
        (await WithDbAsync(db => db.PaymentWebhookEvents.FirstAsync(e => e.Reference == reference))).Error.Should().Contain("Mismatch");
    }

    [Fact]
    public async Task Unpaid_verify_does_not_credit()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total, _) = await CheckoutOkAsync(seat);
        Paystack.VerifyResponses[reference] = _ => FakePaystackClient.Transaction(reference, "abandoned", total, "NGN", seat.SubaccountCode);

        var verify = await (await seat.Traveller.GetAsync($"/api/payments/verify/{reference}")).ReadJsonAsync();
        verify.GetProperty("status").GetString().Should().Be("Initialized");
        verify.GetProperty("receipt").ValueKind.Should().Be(JsonValueKind.Null);
        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(0);
    }

    // ── Webhook signature and payload ────────────────────────────────────────

    [Fact]
    public async Task Webhook_rejects_missing_invalid_and_tampered_signatures()
    {
        var body = ChargeSuccessBody("HDR-000000-NOPE0001", 100);

        (await PostWebhookAsync(body, signature: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostWebhookAsync(body, signature: new string('a', 128))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var signedForOther = PaystackSignature.ComputeHex(Encoding.UTF8.GetBytes(body), HodracApiFactory.PaystackSecretKey);
        var tampered = body.Replace("\"amount\":100", "\"amount\":999");
        (await PostWebhookAsync(tampered, signature: signedForOther)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var wrongKey = PaystackSignature.ComputeHex(Encoding.UTF8.GetBytes(body), "sk_test_someone_else");
        (await PostWebhookAsync(body, signature: wrongKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var hash = PaystackWebhookProcessor.Sha256Hex(Encoding.UTF8.GetBytes(body));
        (await WithDbAsync(db => db.PaymentWebhookEvents.AnyAsync(e => e.PayloadSha256 == hash))).Should().BeFalse();

        // Correctly signed: accepted (unknown reference, nothing to apply).
        (await PostWebhookAsync(body)).StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await WithDbAsync(db => db.PaymentWebhookEvents.FirstAsync(e => e.PayloadSha256 == hash));
        stored.ProcessedAt.Should().NotBeNull();
        stored.Reference.Should().Be("HDR-000000-NOPE0001");
    }

    [Fact]
    public async Task Webhook_with_signed_non_event_body_is_400()
    {
        (await PostWebhookAsync("[1,2,3]")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostWebhookAsync("not json")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Refunds and disputes ─────────────────────────────────────────────────

    [Fact]
    public async Task Refund_processed_webhooks_mark_partial_then_full_refund_and_recompute()
    {
        var seat = await ApprovedSeatAsync();
        var reference = await PayDepositAsync(seat);

        string RefundBody(long amount, string status = "processed") =>
            $"{{\"event\":\"refund.{status}\",\"data\":{{\"status\":\"{status}\",\"transaction_reference\":\"{reference}\",\"refund_reference\":\"R-{Guid.NewGuid():N}\",\"amount\":\"{amount}\",\"currency\":\"NGN\"}}}}";

        await WebhookOkAsync(RefundBody(1_000_000, "pending"));
        (await PaymentAsync(reference)).Status.Should().Be(SeatPaymentStatus.Success);

        await WebhookOkAsync(RefundBody(1_000_000));
        var partial = await PaymentAsync(reference);
        partial.Status.Should().Be(SeatPaymentStatus.PartiallyRefunded);
        partial.RefundedKobo.Should().Be(1_000_000);
        var interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(Deposit - 1_000_000);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.PartPaid);

        await WebhookOkAsync(RefundBody(Deposit + DepositFee - 1_000_000));
        (await PaymentAsync(reference)).Status.Should().Be(SeatPaymentStatus.Refunded);
        interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(0);
        interest.AmountPaidViaHodracKobo.Should().Be(0);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.Refunded);
    }

    [Fact]
    public async Task Dispute_created_marks_payment_disputed_and_alerts_host_and_admins()
    {
        var seat = await ApprovedSeatAsync();
        var reference = await PayDepositAsync(seat);
        _factory.Emails.Sent.Clear();

        await WebhookOkAsync($"{{\"event\":\"charge.dispute.create\",\"data\":{{\"id\":99,\"status\":\"awaiting-merchant-feedback\",\"transaction\":{{\"reference\":\"{reference}\",\"amount\":{Deposit + DepositFee}}}}}}}");

        (await PaymentAsync(reference)).Status.Should().Be(SeatPaymentStatus.Disputed);
        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(Deposit);
        (await NotificationCountAsync(seat.TripId, NotificationType.DisputeOpened)).Should().Be(1);
        _factory.Emails.Sent.Should().Contain(e => e.To == seat.HostUser.Email && e.Subject.Contains("disputed"));
        _factory.Emails.Sent.Should().Contain(e => e.To == HodracApiFactory.AdminEmail && e.Subject.Contains("dispute"));

        await WebhookOkAsync($"{{\"event\":\"charge.dispute.resolve\",\"data\":{{\"id\":99,\"resolution\":\"declined\",\"transaction\":{{\"reference\":\"{reference}\"}}}}}}");
        (await PaymentAsync(reference)).Status.Should().Be(SeatPaymentStatus.Success);
    }

    // ── Checkout guards ──────────────────────────────────────────────────────

    [Fact]
    public async Task Payment_endpoints_are_404_for_non_requesters()
    {
        var seat = await ApprovedSeatAsync();
        var (stranger, _) = await UserAsync("Stranger");

        (await CheckoutAsync(stranger, seat, "Deposit")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CheckoutAsync(seat.Host, seat, "Deposit")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/balance")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.PostAsJsonAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/accept-terms", new { termsVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (reference, _, _) = await CheckoutOkAsync(seat);
        (await stranger.GetAsync($"/api/payments/verify/{reference}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Payment_endpoints_are_404_on_community_trips()
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
        (await author.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await traveller.GetAsync($"/api/trips/{tripId}/terms")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms", new { termsVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await traveller.GetAsync($"/api/trips/{tripId}/interests/{interestId}/balance")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/checkout", new { kind = "Deposit" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Checkout_requires_current_terms_to_be_accepted()
    {
        var seat = await ApprovedSeatAsync(acceptTerms: false);

        var response = await CheckoutAsync(seat.Traveller, seat, "Deposit");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("terms");

        // Accepted, then the host changes the terms: the old acceptance no longer counts.
        (await seat.Traveller.PostAsJsonAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/accept-terms", new { termsVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await seat.Host.PatchAsJsonAsync($"/api/trips/{seat.TripId}", new { termsText = "New terms: everything is non-refundable." }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CheckoutAsync(seat.Traveller, seat, "Deposit")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Accept_terms_rejects_stale_version_and_stores_evidence()
    {
        var seat = await ApprovedSeatAsync(acceptTerms: false);
        (await seat.Host.PatchAsJsonAsync($"/api/trips/{seat.TripId}", new { termsText = "Version two of the terms." }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var terms = await (await seat.Traveller.GetAsync($"/api/trips/{seat.TripId}/terms")).ReadJsonAsync();
        terms.GetProperty("termsVersion").GetInt32().Should().Be(2);
        terms.GetProperty("termsText").GetString().Should().Be("Version two of the terms.");

        var stale = await seat.Traveller.PostAsJsonAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/accept-terms", new { termsVersion = 1 });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/accept-terms")
        {
            Content = JsonContent.Create(new { termsVersion = 2 }),
        };
        request.Headers.UserAgent.ParseAdd("HodracTest/1.0");
        var accepted = await seat.Traveller.SendAsync(request);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Version two of the terms."))).ToLowerInvariant();
        var interest = await InterestAsync(seat.InterestId);
        interest.TermsVersionAccepted.Should().Be(2);
        interest.TermsTextSha256.Should().Be(expectedHash);
        interest.TermsAcceptedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        interest.TermsAcceptedUserAgent.Should().Be("HodracTest/1.0");
    }

    [Fact]
    public async Task Accept_terms_requires_approval()
    {
        var (host, _, _) = await HostAsync("Terms Host");
        var tripId = await CreateTripAsync(host, HostedTripBody());
        var (traveller, _) = await UserAsync("Pending Traveller");
        var interestId = await JoinAsync(traveller, tripId);

        (await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms", new { termsVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Checkout_refused_while_host_is_unverified()
    {
        var seat = await ApprovedSeatAsync(verifiedHost: false);

        var response = await CheckoutAsync(seat.Traveller, seat, "Deposit");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Host isn't verified yet");
        Paystack.Initialized.Values.Should().NotContain(r => r.Email == seat.TravellerUser.Email);
    }

    [Fact]
    public async Task Checkout_refused_after_host_verification_is_revoked()
    {
        var seat = await ApprovedSeatAsync();
        var hostId = await WithDbAsync(db => db.TripPosts.Where(t => t.Id == seat.TripId).Select(t => t.HostProfileId!.Value).FirstAsync());

        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        var revoke = await _factory.CreateClient().Authenticated(admin)
            .PostAsJsonAsync($"/api/admin/hosts/{hostId}/revoke", new { reason = "Under review." });
        revoke.StatusCode.Should().Be(HttpStatusCode.OK, await revoke.Content.ReadAsStringAsync());

        var response = await CheckoutAsync(seat.Traveller, seat, "Deposit");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Host isn't verified yet");
        Paystack.Initialized.Values.Should().NotContain(r => r.Email == seat.TravellerUser.Email);
    }

    [Fact]
    public async Task Deposit_checkout_refused_after_seat_hold_expires()
    {
        var seat = await ApprovedSeatAsync();
        await WithDbAsync(db => db.TripInterests.Where(i => i.Id == seat.InterestId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.SeatHoldExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        var response = await CheckoutAsync(seat.Traveller, seat, "Deposit");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("seat hold");
        (await CheckoutAsync(seat.Traveller, seat, "Full")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await WithDbAsync(db => db.TripInterests.Where(i => i.Id == seat.InterestId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.SeatHoldExpiresAt, (DateTime?)null)
                .SetProperty(i => i.PaymentStatus, InterestPaymentStatus.Expired)));
        (await CheckoutAsync(seat.Traveller, seat, "Deposit")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Checkout_refuses_already_paid_and_out_of_order_lines()
    {
        var seat = await ApprovedSeatAsync();

        var early = await CheckoutAsync(seat.Traveller, seat, "Instalment", 1);
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await PayDepositAsync(seat);

        var again = await CheckoutAsync(seat.Traveller, seat, "Deposit");
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("already");

        (await CheckoutAsync(seat.Traveller, seat, "Instalment", 2)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CheckoutAsync(seat.Traveller, seat, "Instalment", 9)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CheckoutAsync(seat.Traveller, seat, "Bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, total, _) = await CheckoutOkAsync(seat, "Instalment", 1);
        total.Should().Be(Instalment + Instalment * 25 / 1000);
    }

    // ── Balance ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Balance_allocates_deposit_first_then_instalments()
    {
        var seat = await ApprovedSeatAsync();
        var url = $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/balance";

        var before = await (await seat.Traveller.GetAsync(url)).ReadJsonAsync();
        before.GetProperty("priceKobo").GetInt64().Should().Be(Price);
        before.GetProperty("owesKobo").GetInt64().Should().Be(Price);
        before.GetProperty("paymentStatus").GetString().Should().Be("AwaitingDeposit");
        before.GetProperty("seatHoldExpiresAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        before.GetProperty("schedule").EnumerateArray().Select(l => l.GetProperty("status").GetString())
            .Should().Equal("Due", "Upcoming", "Upcoming");
        var next = before.GetProperty("nextPayment");
        next.GetProperty("kind").GetString().Should().Be("Deposit");
        next.GetProperty("seatAmountKobo").GetInt64().Should().Be(Deposit);
        next.GetProperty("feeKobo").GetInt64().Should().Be(DepositFee);
        next.GetProperty("totalKobo").GetInt64().Should().Be(Deposit + DepositFee);

        await PayDepositAsync(seat);
        // Plus ₦20,000 the host recorded by hand: allocated to instalment 1.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();
            db.ManualPayments.Add(new ManualPayment { TripInterestId = seat.InterestId, AmountKobo = 2_000_000, RecordedByUserId = Guid.Parse(seat.HostUser.UserId) });
            await db.SaveChangesAsync();
            var interest = await db.TripInterests.FirstAsync(i => i.Id == seat.InterestId);
            var trip = await db.TripPosts.FirstAsync(t => t.Id == seat.TripId);
            await payments.RecomputeInterestAsync(interest, trip);
            await db.SaveChangesAsync();
        }

        var after = await (await seat.Traveller.GetAsync(url)).ReadJsonAsync();
        after.GetProperty("amountPaidKobo").GetInt64().Should().Be(Deposit + 2_000_000);
        after.GetProperty("owesKobo").GetInt64().Should().Be(Price - Deposit - 2_000_000);
        after.GetProperty("paymentStatus").GetString().Should().Be("PartPaid");
        after.GetProperty("seatHoldExpiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        var lines = after.GetProperty("schedule").EnumerateArray().ToList();
        lines[0].GetProperty("status").GetString().Should().Be("Paid");
        lines[0].GetProperty("paidAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        lines[1].GetProperty("status").GetString().Should().Be("Due");
        lines[1].GetProperty("paidKobo").GetInt64().Should().Be(2_000_000);
        lines[1].GetProperty("sequence").GetInt32().Should().Be(1);
        lines[2].GetProperty("status").GetString().Should().Be("Upcoming");
        var nextAfter = after.GetProperty("nextPayment");
        nextAfter.GetProperty("kind").GetString().Should().Be("Instalment");
        nextAfter.GetProperty("sequence").GetInt32().Should().Be(1);
        nextAfter.GetProperty("seatAmountKobo").GetInt64().Should().Be(Instalment - 2_000_000);
    }

    // ── Receipts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Receipt_visible_to_payer_author_and_admin_only()
    {
        var seat = await ApprovedSeatAsync();
        var reference = await PayDepositAsync(seat);
        var url = $"/api/payments/{reference}/receipt";

        var receipt = await (await seat.Traveller.GetAsync(url)).ReadJsonAsync();
        receipt.GetProperty("reference").GetString().Should().Be(reference);
        receipt.GetProperty("seatAmountKobo").GetInt64().Should().Be(Deposit);
        receipt.GetProperty("feeKobo").GetInt64().Should().Be(DepositFee);
        receipt.GetProperty("totalKobo").GetInt64().Should().Be(Deposit + DepositFee);
        receipt.GetProperty("termsVersionAccepted").GetInt32().Should().Be(1);
        receipt.GetProperty("hostDisplayName").GetString().Should().Be("Pay Host");
        receipt.GetProperty("channel").GetString().Should().Be("card");
        receipt.GetProperty("tripTitle").GetString().Should().StartWith("Zanzibar-");

        (await seat.Host.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        (await _factory.CreateClient().Authenticated(admin).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (stranger, _) = await UserAsync("Receipt Stranger");
        (await stranger.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.CreateClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var mine = await (await seat.Traveller.GetAsync("/api/me/payments")).ReadJsonAsync();
        mine.EnumerateArray().Select(r => r.GetProperty("reference").GetString()).Should().Equal(reference);
        var theirs = await (await stranger.GetAsync("/api/me/payments")).ReadJsonAsync();
        theirs.GetArrayLength().Should().Be(0);
    }
}

/// <summary>Pure rules: schedule allocation and the interest payment status.</summary>
public class PaymentScheduleTests
{
    private static TripPost Trip() => new()
    {
        IsHostedTrip = true,
        PricePerSeatKobo = 300,
        DepositKobo = 100,
        Instalments = new()
        {
            new TripInstalment { Sequence = 2, AmountKobo = 100, DueDate = new DateOnly(2027, 3, 1) },
            new TripInstalment { Sequence = 1, AmountKobo = 100, DueDate = new DateOnly(2027, 2, 1) },
        },
    };

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(50, 50, 0, 0)]
    [InlineData(100, 100, 0, 0)]
    [InlineData(150, 100, 50, 0)]
    [InlineData(300, 100, 100, 100)]
    [InlineData(400, 100, 100, 100)]
    public void Allocates_deposit_first_then_instalments_in_sequence(long paid, long deposit, long first, long second)
    {
        var lines = PaymentSchedule.Allocate(Trip(), paid);

        lines.Select(l => (l.Kind, l.Sequence)).Should().Equal(
            (SeatPaymentKind.Deposit, (int?)null), (SeatPaymentKind.Instalment, 1), (SeatPaymentKind.Instalment, 2));
        lines.Select(l => l.PaidKobo).Should().Equal(deposit, first, second);
    }

    [Theory]
    [InlineData(0, InterestPaymentStatus.AwaitingDeposit, false, InterestPaymentStatus.AwaitingDeposit)]
    [InlineData(0, InterestPaymentStatus.Expired, false, InterestPaymentStatus.Expired)]
    [InlineData(0, InterestPaymentStatus.DepositPaid, true, InterestPaymentStatus.Refunded)]
    [InlineData(50, InterestPaymentStatus.AwaitingDeposit, false, InterestPaymentStatus.PartPaid)]
    [InlineData(100, InterestPaymentStatus.AwaitingDeposit, false, InterestPaymentStatus.DepositPaid)]
    [InlineData(150, InterestPaymentStatus.DepositPaid, false, InterestPaymentStatus.PartPaid)]
    [InlineData(300, InterestPaymentStatus.PartPaid, false, InterestPaymentStatus.PaidInFull)]
    [InlineData(300, InterestPaymentStatus.PartPaid, true, InterestPaymentStatus.PaidInFull)]
    public void Computes_payment_status_from_totals(long paid, InterestPaymentStatus current, bool hadRefund, InterestPaymentStatus expected) =>
        PaymentSchedule.ComputeStatus(paid, 300, 100, current, hadRefund).Should().Be(expected);
}
