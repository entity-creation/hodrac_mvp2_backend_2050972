using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Services.Payments;

// ─── Paystack API client ──────────────────────────────────────────────────────
// Typed HttpClient (registered in Program.cs). Field names confirmed against
// Paystack's OpenAPI spec; see docs/PAYSTACK-NOTES.md. All amounts are kobo.
// Every response is the envelope { status, message, data }: a non-2xx status
// or status:false throws PaystackException carrying Paystack's message.

public interface IPaystackClient
{
    /// <summary>GET /bank?country=nigeria (all pages), active banks only, cached 24h.</summary>
    Task<IReadOnlyList<PaystackBank>> ListBanksAsync(CancellationToken ct = default);

    /// <summary>GET /bank/resolve?account_number=&amp;bank_code=.</summary>
    Task<PaystackResolvedAccount> ResolveAccountAsync(string accountNumber, string bankCode, CancellationToken ct = default);

    /// <summary>POST /subaccount with percentage_charge 0 (Hodrac's share comes from transaction_charge).</summary>
    Task<PaystackSubaccount> CreateSubaccountAsync(PaystackSubaccountRequest request, CancellationToken ct = default);

    /// <summary>PUT /subaccount/{code}; null fields are not sent. percentage_charge is always 0.</summary>
    Task<PaystackSubaccount> UpdateSubaccountAsync(string subaccountCode, PaystackSubaccountUpdate request, CancellationToken ct = default);

    /// <summary>POST /transaction/initialize.</summary>
    Task<PaystackInitializeResult> InitializeAsync(PaystackInitializeRequest request, CancellationToken ct = default);

    /// <summary>GET /transaction/verify/{reference}.</summary>
    Task<PaystackTransaction> VerifyAsync(string reference, CancellationToken ct = default);

    /// <summary>POST /refund { transaction: reference, amount? }.</summary>
    Task<PaystackRefund> RefundAsync(string reference, long? amountKobo = null, CancellationToken ct = default);
}

// ─── Typed requests / results ────────────────────────────────────────────────

public record PaystackBank(string Name, string Code, string? Slug, string? Currency, string? Type);

public record PaystackResolvedAccount(string AccountNumber, string AccountName, long? BankId);

public record PaystackSubaccountRequest(
    string BusinessName,
    string SettlementBankCode,
    string AccountNumber,
    string? Description = null,
    string? PrimaryContactEmail = null,
    string? PrimaryContactName = null,
    string? PrimaryContactPhone = null);

/// <summary>Partial update: null = not sent.</summary>
public record PaystackSubaccountUpdate(
    string? BusinessName = null,
    string? SettlementBankCode = null,
    string? AccountNumber = null,
    string? Description = null,
    string? PrimaryContactEmail = null,
    bool? Active = null);

/// <summary>
/// A subaccount as Paystack returns it. The account number Paystack echoes
/// back is deliberately not mapped: Hodrac never keeps it.
/// </summary>
public record PaystackSubaccount(
    string SubaccountCode,
    string BusinessName,
    string? AccountName,
    string? SettlementBank,
    decimal PercentageCharge,
    bool Active);

public record PaystackInitializeRequest(
    string Email,
    long AmountKobo,
    string Reference,
    string? CallbackUrl,
    string? SubaccountCode,
    long? TransactionChargeKobo,
    string? Bearer,
    IReadOnlyDictionary<string, string>? Metadata,
    string Currency = "NGN");

public record PaystackInitializeResult(string AuthorizationUrl, string AccessCode, string Reference);

/// <summary>The verify response's data. <see cref="RawJson"/> is the full response for audit.</summary>
public record PaystackTransaction(
    long Id,
    string Status,
    string Reference,
    long AmountKobo,
    string Currency,
    string? Channel,
    DateTime? PaidAt,
    long? FeesKobo,
    string? GatewayResponse,
    string? SubaccountCode,
    JsonElement? Metadata,
    string RawJson)
{
    public bool IsSuccess => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
}

public record PaystackRefund(long? Id, string? Status, long? AmountKobo, string? Currency, string RawJson);

/// <summary>Paystack said no (status:false or non-2xx), or the call failed/timed out.</summary>
public class PaystackException : Exception
{
    /// <summary>Paystack's HTTP status; null when the request never got a response.</summary>
    public HttpStatusCode? StatusCode { get; }

    public PaystackException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner) => StatusCode = statusCode;

    /// <summary>A 4xx from Paystack: the input was rejected (bad account number, ...).</summary>
    public bool IsClientError => StatusCode is { } s && (int)s >= 400 && (int)s < 500
        && s != HttpStatusCode.Unauthorized && s != HttpStatusCode.Forbidden && s != HttpStatusCode.TooManyRequests;
}

/// <summary>Paystack__SecretKey is not set.</summary>
public class PaystackNotConfiguredException : PaystackException
{
    public PaystackNotConfiguredException()
        : base("Paystack is not configured (Paystack:SecretKey is empty).") { }
}

public class PaystackClient : IPaystackClient
{
    public const string BanksCacheKey = "paystack:banks:nigeria";
    public static readonly TimeSpan BanksCacheDuration = TimeSpan.FromHours(24);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private const int BanksPageSize = 100;
    private const int MaxBankPages = 20;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly PaystackOptions _options;
    private readonly ILogger<PaystackClient> _logger;

    public PaystackClient(HttpClient http, IMemoryCache cache, IOptions<PaystackOptions> options, ILogger<PaystackClient> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Base address with a trailing slash so relative paths append (https://api.paystack.co/).</summary>
    public static Uri NormalizeBaseUrl(string? baseUrl)
    {
        var url = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.paystack.co" : baseUrl.Trim();
        return new Uri(url.TrimEnd('/') + "/");
    }

    // ── Banks ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PaystackBank>> ListBanksAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(BanksCacheKey, out IReadOnlyList<PaystackBank>? cached) && cached is not null)
            return cached;

        var banks = new List<PaystackBank>();
        string? cursor = null;
        for (var page = 0; page < MaxBankPages; page++)
        {
            var path = $"bank?country=nigeria&use_cursor=true&perPage={BanksPageSize}";
            if (!string.IsNullOrEmpty(cursor)) path += $"&next={Uri.EscapeDataString(cursor)}";

            var (data, meta) = await SendAsync<List<BankWire>>(HttpMethod.Get, path, null, ct);
            foreach (var b in data ?? new List<BankWire>())
            {
                if (b.Active == false || b.IsDeleted == true) continue;
                if (string.IsNullOrWhiteSpace(b.Code) || string.IsNullOrWhiteSpace(b.Name)) continue;
                banks.Add(new PaystackBank(b.Name.Trim(), b.Code.Trim(), b.Slug, b.Currency, b.Type));
            }

            cursor = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
            if (string.IsNullOrEmpty(cursor)) break;
        }

        IReadOnlyList<PaystackBank> result = banks
            .GroupBy(b => b.Code)
            .Select(g => g.First())
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cache.Set(BanksCacheKey, result, BanksCacheDuration);
        return result;
    }

    public async Task<PaystackResolvedAccount> ResolveAccountAsync(string accountNumber, string bankCode, CancellationToken ct = default)
    {
        var path = $"bank/resolve?account_number={Uri.EscapeDataString(accountNumber)}&bank_code={Uri.EscapeDataString(bankCode)}";
        var (data, _) = await SendAsync<ResolveWire>(HttpMethod.Get, path, null, ct);
        if (data is null || string.IsNullOrWhiteSpace(data.AccountName))
            throw new PaystackException("Paystack did not return an account name.");
        return new PaystackResolvedAccount(data.AccountNumber ?? accountNumber, data.AccountName.Trim(), data.BankId);
    }

    // ── Subaccounts ──────────────────────────────────────────────────────────

    public async Task<PaystackSubaccount> CreateSubaccountAsync(PaystackSubaccountRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["business_name"] = request.BusinessName,
            ["settlement_bank"] = request.SettlementBankCode,
            ["account_number"] = request.AccountNumber,
            ["percentage_charge"] = 0,
            ["description"] = request.Description,
            ["primary_contact_email"] = request.PrimaryContactEmail,
            ["primary_contact_name"] = request.PrimaryContactName,
            ["primary_contact_phone"] = request.PrimaryContactPhone,
        };
        var (data, _) = await SendAsync<SubaccountWire>(HttpMethod.Post, "subaccount", body, ct);
        return ToSubaccount(data);
    }

    public async Task<PaystackSubaccount> UpdateSubaccountAsync(string subaccountCode, PaystackSubaccountUpdate request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subaccountCode))
            throw new ArgumentException("Subaccount code is required.", nameof(subaccountCode));

        var body = new Dictionary<string, object?>
        {
            ["business_name"] = request.BusinessName,
            ["settlement_bank"] = request.SettlementBankCode,
            ["account_number"] = request.AccountNumber,
            ["percentage_charge"] = 0,
            ["description"] = request.Description,
            ["primary_contact_email"] = request.PrimaryContactEmail,
            ["active"] = request.Active,
        };
        var (data, _) = await SendAsync<SubaccountWire>(
            HttpMethod.Put, $"subaccount/{Uri.EscapeDataString(subaccountCode)}", body, ct);
        return ToSubaccount(data);
    }

    private static PaystackSubaccount ToSubaccount(SubaccountWire? data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.SubaccountCode))
            throw new PaystackException("Paystack did not return a subaccount code.");
        return new PaystackSubaccount(
            data.SubaccountCode,
            data.BusinessName ?? string.Empty,
            data.AccountName,
            data.SettlementBank,
            data.PercentageCharge ?? 0m,
            data.Active ?? true);
    }

    // ── Transactions ─────────────────────────────────────────────────────────

    public async Task<PaystackInitializeResult> InitializeAsync(PaystackInitializeRequest request, CancellationToken ct = default)
    {
        if (request.AmountKobo <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Amount must be greater than 0.");

        var body = new Dictionary<string, object?>
        {
            ["email"] = request.Email,
            ["amount"] = request.AmountKobo,
            ["currency"] = request.Currency,
            ["reference"] = request.Reference,
            ["callback_url"] = string.IsNullOrWhiteSpace(request.CallbackUrl) ? null : request.CallbackUrl,
            ["subaccount"] = request.SubaccountCode,
            // Sent even when 0 so Paystack never falls back to the subaccount's percentage.
            ["transaction_charge"] = request.SubaccountCode is null ? null : request.TransactionChargeKobo,
            ["bearer"] = request.SubaccountCode is null ? null : request.Bearer,
            ["metadata"] = request.Metadata,
        };
        var (data, _) = await SendAsync<InitializeWire>(HttpMethod.Post, "transaction/initialize", body, ct);
        if (data is null || string.IsNullOrWhiteSpace(data.AuthorizationUrl))
            throw new PaystackException("Paystack did not return an authorization URL.");
        return new PaystackInitializeResult(data.AuthorizationUrl, data.AccessCode ?? string.Empty, data.Reference ?? request.Reference);
    }

    public async Task<PaystackTransaction> VerifyAsync(string reference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Reference is required.", nameof(reference));

        var (raw, root) = await SendRawAsync(HttpMethod.Get, $"transaction/verify/{Uri.EscapeDataString(reference)}", null, ct);
        if (!root.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object)
            throw new PaystackException("Paystack verify response had no data.");

        return new PaystackTransaction(
            Id: GetLong(d, "id") ?? 0,
            Status: GetString(d, "status") ?? string.Empty,
            Reference: GetString(d, "reference") ?? reference,
            AmountKobo: GetLong(d, "amount") ?? 0,
            Currency: GetString(d, "currency") ?? string.Empty,
            Channel: GetString(d, "channel"),
            PaidAt: GetDate(d, "paid_at") ?? GetDate(d, "paidAt"),
            FeesKobo: GetLong(d, "fees"),
            GatewayResponse: GetString(d, "gateway_response"),
            SubaccountCode: d.TryGetProperty("subaccount", out var sub) && sub.ValueKind == JsonValueKind.Object
                ? GetString(sub, "subaccount_code")
                : null,
            Metadata: d.TryGetProperty("metadata", out var meta) && meta.ValueKind is JsonValueKind.Object or JsonValueKind.String
                ? meta.Clone()
                : null,
            RawJson: raw);
    }

    public async Task<PaystackRefund> RefundAsync(string reference, long? amountKobo = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Reference is required.", nameof(reference));
        if (amountKobo is <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountKobo), "Refund amount must be greater than 0.");

        var body = new Dictionary<string, object?>
        {
            ["transaction"] = reference,
            ["amount"] = amountKobo,
        };
        var (raw, root) = await SendRawAsync(HttpMethod.Post, "refund", body, ct);
        var d = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data : default;
        return d.ValueKind == JsonValueKind.Object
            ? new PaystackRefund(GetLong(d, "id"), GetString(d, "status"), GetLong(d, "amount"), GetString(d, "currency"), raw)
            : new PaystackRefund(null, null, null, null, raw);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private async Task<(T? Data, JsonElement Meta)> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var (_, root) = await SendRawAsync(method, path, body, ct);
        T? data = default;
        if (root.TryGetProperty("data", out var d) && d.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            try
            {
                data = d.Deserialize<T>(Json);
            }
            catch (JsonException ex)
            {
                throw new PaystackException("Unexpected response from Paystack.", null, ex);
            }
        }
        var meta = root.TryGetProperty("meta", out var m) && m.ValueKind == JsonValueKind.Object ? m.Clone() : default;
        return (data, meta);
    }

    /// <summary>Sends the request and returns the raw body plus its parsed root, after the envelope checks.</summary>
    private async Task<(string Raw, JsonElement Root)> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
            throw new PaystackNotConfiguredException();

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            // WhenWritingNull doesn't apply to dictionary entries: drop null fields here.
            if (body is Dictionary<string, object?> fields)
                body = fields.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PaystackException("Paystack did not respond in time.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaystackException("Could not reach Paystack.", null, ex);
        }

        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            JsonElement root = default;
            var parsed = false;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                root = doc.RootElement.Clone();
                parsed = root.ValueKind == JsonValueKind.Object;
            }
            catch (JsonException)
            {
                // handled below
            }

            var message = parsed ? GetString(root, "message") : null;
            var status = parsed && root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.True;

            if (!response.IsSuccessStatusCode || !status)
            {
                _logger.LogWarning("Paystack {Method} {Path} failed: {StatusCode} {Message}",
                    method, path.Split('?')[0], (int)response.StatusCode, message);
                throw new PaystackException(
                    string.IsNullOrWhiteSpace(message) ? $"Paystack request failed ({(int)response.StatusCode})." : message,
                    response.StatusCode);
            }

            return (raw, root);
        }
    }

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            }
            : null;

    private static long? GetLong(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }

    private static DateTime? GetDate(JsonElement e, string name)
    {
        var s = GetString(e, name);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto.UtcDateTime
            : null;
    }

    // Wire shapes (snake_case via the naming policy).
    private sealed record BankWire(string Name, string Code, string? Slug, bool? Active, bool? IsDeleted, string? Currency, string? Type);
    private sealed record ResolveWire(string? AccountNumber, string? AccountName, long? BankId);
    private sealed record SubaccountWire(string? SubaccountCode, string? BusinessName, string? AccountName, string? SettlementBank, decimal? PercentageCharge, bool? Active);
    private sealed record InitializeWire(string? AuthorizationUrl, string? AccessCode, string? Reference);
}
