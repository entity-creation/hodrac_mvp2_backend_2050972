using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Tests.Payments;

/// <summary>PaystackClient request building and response parsing, against a fake HttpMessageHandler (no network).</summary>
public class PaystackClientTests
{
    private const string SecretKey = "sk_test_unit_0123456789";

    private sealed record Captured(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _respond;
        public List<Captured> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Captured(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            var (status, json) = _respond(request);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static (PaystackClient client, FakeHandler handler, MemoryCache cache) Create(
        Func<HttpRequestMessage, (HttpStatusCode, string)> respond, string secretKey = SecretKey, string baseUrl = "https://api.paystack.test")
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = PaystackClient.NormalizeBaseUrl(baseUrl) };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new PaystackOptions { SecretKey = secretKey, BaseUrl = baseUrl });
        return (new PaystackClient(http, cache, options, NullLogger<PaystackClient>.Instance), handler, cache);
    }

    private static JsonElement Json(string? body) => JsonDocument.Parse(body!).RootElement;

    [Fact]
    public async Task Initialize_sends_split_fields_in_kobo_with_bearer_auth()
    {
        var (client, handler, _) = Create(_ => (HttpStatusCode.OK,
            """{"status":true,"message":"Authorization URL created","data":{"authorization_url":"https://checkout.paystack.com/abc","access_code":"abc","reference":"HDR-261116-ABCDEFGH"}}"""));

        var result = await client.InitializeAsync(new PaystackInitializeRequest(
            Email: "traveller@example.test",
            AmountKobo: 10_250_000,
            Reference: "HDR-261116-ABCDEFGH",
            CallbackUrl: "https://www.hodrac.com/trips/payment/callback",
            SubaccountCode: "ACCT_host123",
            TransactionChargeKobo: 250_000,
            Bearer: FeeQuote.SubaccountBearer,
            Metadata: new Dictionary<string, string> { ["tripId"] = "t1", ["interestId"] = "i1", ["seatPaymentId"] = "s1" }));

        result.AuthorizationUrl.Should().Be("https://checkout.paystack.com/abc");
        result.AccessCode.Should().Be("abc");
        result.Reference.Should().Be("HDR-261116-ABCDEFGH");

        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.Uri.ToString().Should().Be("https://api.paystack.test/transaction/initialize");
        req.Authorization.Should().Be($"Bearer {SecretKey}");

        var body = Json(req.Body);
        body.GetProperty("email").GetString().Should().Be("traveller@example.test");
        body.GetProperty("amount").GetInt64().Should().Be(10_250_000);
        body.GetProperty("currency").GetString().Should().Be("NGN");
        body.GetProperty("reference").GetString().Should().Be("HDR-261116-ABCDEFGH");
        body.GetProperty("callback_url").GetString().Should().Be("https://www.hodrac.com/trips/payment/callback");
        body.GetProperty("subaccount").GetString().Should().Be("ACCT_host123");
        body.GetProperty("transaction_charge").GetInt64().Should().Be(250_000);
        body.GetProperty("bearer").GetString().Should().Be("subaccount");
        body.GetProperty("metadata").GetProperty("seatPaymentId").GetString().Should().Be("s1");
    }

    [Fact]
    public async Task Initialize_sends_zero_transaction_charge_when_fee_is_waived()
    {
        var (client, handler, _) = Create(_ => (HttpStatusCode.OK,
            """{"status":true,"message":"ok","data":{"authorization_url":"https://x","access_code":"a","reference":"r"}}"""));

        await client.InitializeAsync(new PaystackInitializeRequest("a@b.test", 5000, "r", null, "ACCT_x", 0, "subaccount", null));

        var body = Json(handler.Requests.Single().Body);
        body.GetProperty("transaction_charge").GetInt64().Should().Be(0);
        body.TryGetProperty("callback_url", out _).Should().BeFalse("null fields are omitted");
        body.TryGetProperty("metadata", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Verify_parses_transaction_and_keeps_raw_json()
    {
        const string json = """
        {"status":true,"message":"Verification successful","data":{
          "id":4099260516,"status":"success","reference":"HDR-261116-ABCDEFGH","amount":10250000,
          "gateway_response":"Successful","paid_at":"2026-11-16T10:15:30.000Z","channel":"card","currency":"NGN",
          "fees":153750,"metadata":{"tripId":"t1"},
          "subaccount":{"id":1,"subaccount_code":"ACCT_host123","business_name":"Lagos Trippers"}}}
        """;
        var (client, handler, _) = Create(_ => (HttpStatusCode.OK, json));

        var tx = await client.VerifyAsync("HDR-261116-ABCDEFGH");

        handler.Requests.Single().Uri.ToString().Should().Be("https://api.paystack.test/transaction/verify/HDR-261116-ABCDEFGH");
        tx.IsSuccess.Should().BeTrue();
        tx.Id.Should().Be(4099260516);
        tx.AmountKobo.Should().Be(10_250_000);
        tx.Currency.Should().Be("NGN");
        tx.Channel.Should().Be("card");
        tx.FeesKobo.Should().Be(153_750);
        tx.SubaccountCode.Should().Be("ACCT_host123");
        tx.PaidAt.Should().Be(new DateTime(2026, 11, 16, 10, 15, 30, DateTimeKind.Utc));
        tx.Metadata!.Value.GetProperty("tripId").GetString().Should().Be("t1");
        tx.RawJson.Should().Be(json);
    }

    [Fact]
    public async Task Status_false_throws_with_paystacks_message()
    {
        var (client, _, _) = Create(_ => (HttpStatusCode.OK, """{"status":false,"message":"Transaction reference not found"}"""));

        var act = () => client.VerifyAsync("nope");

        (await act.Should().ThrowAsync<PaystackException>()).Which.Message.Should().Be("Transaction reference not found");
    }

    [Fact]
    public async Task Non_success_status_throws_with_message_and_status_code()
    {
        var (client, _, _) = Create(_ => (HttpStatusCode.UnprocessableEntity,
            """{"status":false,"message":"Could not resolve account name. Check parameters or try again."}"""));

        var act = () => client.ResolveAccountAsync("0000000000", "044");

        var ex = (await act.Should().ThrowAsync<PaystackException>()).Which;
        ex.Message.Should().StartWith("Could not resolve account name");
        ex.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ex.IsClientError.Should().BeTrue();
    }

    [Fact]
    public async Task Non_json_error_body_still_throws_paystack_exception()
    {
        var (client, _, _) = Create(_ => (HttpStatusCode.BadGateway, "<html>bad gateway</html>"));

        var ex = (await ((Func<Task>)(() => client.ListBanksAsync())).Should().ThrowAsync<PaystackException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        ex.IsClientError.Should().BeFalse();
    }

    [Fact]
    public async Task Empty_secret_key_throws_not_configured_without_calling_paystack()
    {
        var (client, handler, _) = Create(_ => (HttpStatusCode.OK, "{}"), secretKey: "");

        await ((Func<Task>)(() => client.ListBanksAsync())).Should().ThrowAsync<PaystackNotConfiguredException>()
            .WithMessage("*not configured*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_builds_query_and_parses_account_name()
    {
        var (client, handler, _) = Create(_ => (HttpStatusCode.OK,
            """{"status":true,"message":"Account number resolved","data":{"account_number":"0001234567","account_name":"DOE JANE","bank_id":9}}"""));

        var account = await client.ResolveAccountAsync("0001234567", "058");

        handler.Requests.Single().Uri.ToString().Should().Be("https://api.paystack.test/bank/resolve?account_number=0001234567&bank_code=058");
        account.AccountName.Should().Be("DOE JANE");
        account.BankId.Should().Be(9);
    }

    [Fact]
    public async Task Create_subaccount_sends_zero_percentage_and_never_returns_account_number()
    {
        var (client, handler, _) = Create(_ => (HttpStatusCode.Created,
            """{"status":true,"message":"Subaccount created","data":{"business_name":"Lagos Trippers","account_number":"0193274682","percentage_charge":0,"settlement_bank":"Access Bank","subaccount_code":"ACCT_4hl4xenwpjy5wb","account_name":"JANE DOE","active":true,"id":55}}"""));

        var sub = await client.CreateSubaccountAsync(new PaystackSubaccountRequest("Lagos Trippers", "044", "0193274682", "Hodrac Host: lagos-trippers"));

        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.Uri.AbsolutePath.Should().Be("/subaccount");
        var body = Json(req.Body);
        body.GetProperty("business_name").GetString().Should().Be("Lagos Trippers");
        body.GetProperty("settlement_bank").GetString().Should().Be("044");
        body.GetProperty("account_number").GetString().Should().Be("0193274682");
        body.GetProperty("percentage_charge").GetDecimal().Should().Be(0);

        sub.SubaccountCode.Should().Be("ACCT_4hl4xenwpjy5wb");
        sub.AccountName.Should().Be("JANE DOE");
        typeof(PaystackSubaccount).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("AccountNumber"));
    }

    [Fact]
    public async Task Update_subaccount_puts_only_given_fields()
    {
        var (client, handler, _) = Create(_ => (HttpStatusCode.OK,
            """{"status":true,"message":"Subaccount updated","data":{"subaccount_code":"ACCT_x","business_name":"New Name","percentage_charge":0}}"""));

        await client.UpdateSubaccountAsync("ACCT_x", new PaystackSubaccountUpdate(BusinessName: "New Name"));

        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Put);
        req.Uri.AbsolutePath.Should().Be("/subaccount/ACCT_x");
        var body = Json(req.Body);
        body.GetProperty("business_name").GetString().Should().Be("New Name");
        body.GetProperty("percentage_charge").GetDecimal().Should().Be(0);
        body.TryGetProperty("account_number", out _).Should().BeFalse();
        body.TryGetProperty("settlement_bank", out _).Should().BeFalse();
    }

    [Fact]
    public async Task List_banks_follows_cursor_filters_inactive_and_caches()
    {
        var (client, handler, _) = Create(req =>
        {
            var q = req.RequestUri!.Query;
            return q.Contains("next=")
                ? (HttpStatusCode.OK, """{"status":true,"message":"Banks retrieved","data":[{"name":"Zenith Bank","code":"057","slug":"zenith-bank","active":true,"is_deleted":false,"currency":"NGN","type":"nuban"}],"meta":{"next":null}}""")
                : (HttpStatusCode.OK, """{"status":true,"message":"Banks retrieved","data":[{"name":"Access Bank","code":"044","slug":"access-bank","active":true,"is_deleted":null,"currency":"NGN","type":"nuban"},{"name":"Old Bank","code":"999","active":false,"is_deleted":false}],"meta":{"next":"YmFuazo1Nw=="}}""");
        });

        var banks = await client.ListBanksAsync();
        var again = await client.ListBanksAsync();

        banks.Select(b => b.Code).Should().Equal("044", "057");
        again.Should().BeSameAs(banks);
        handler.Requests.Should().HaveCount(2, "two pages, then served from cache");
        handler.Requests[0].Uri.Query.Should().Contain("country=nigeria").And.Contain("use_cursor=true");
        handler.Requests[1].Uri.Query.Should().Contain("next=YmFuazo1Nw%3D%3D");
    }

    [Fact]
    public async Task Refund_posts_transaction_reference_and_amount()
    {
        var (client, handler, _) = Create(_ => (HttpStatusCode.OK,
            """{"status":true,"message":"Refund has been queued for processing","data":{"transaction":{"id":1,"reference":"r"},"amount":5000,"currency":"NGN","status":"pending","id":77}}"""));

        var refund = await client.RefundAsync("HDR-1", 5000);

        var body = Json(handler.Requests.Single().Body);
        handler.Requests.Single().Uri.AbsolutePath.Should().Be("/refund");
        body.GetProperty("transaction").GetString().Should().Be("HDR-1");
        body.GetProperty("amount").GetInt64().Should().Be(5000);
        refund.Status.Should().Be("pending");
        refund.AmountKobo.Should().Be(5000);
        refund.Id.Should().Be(77);
    }

    [Fact]
    public async Task Timeout_is_wrapped_in_paystack_exception()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("timeout"));
        var http = new HttpClient(handler) { BaseAddress = PaystackClient.NormalizeBaseUrl(null) };
        var client = new PaystackClient(http, new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new PaystackOptions { SecretKey = SecretKey }), NullLogger<PaystackClient>.Instance);

        (await ((Func<Task>)(() => client.VerifyAsync("r"))).Should().ThrowAsync<PaystackException>())
            .Which.StatusCode.Should().BeNull();
    }
}
