using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services
{
    public record CurrencyRatesDto(
        string Base,
        DateTime UpdatedAt,
        IReadOnlyDictionary<string, decimal> Rates
    );

    public interface ICurrencyRateService
    {
        /// <summary>Rates as units of each currency per 1 <paramref name="baseCurrency"/>.
        /// Null when no rates have ever been loaded or the base is unknown.</summary>
        Task<CurrencyRatesDto?> GetRatesAsync(string baseCurrency, CancellationToken ct = default);

        bool IsKnownCurrency(string code);

        /// <summary>Fetch fresh USD rates from the provider, then cache + persist them.</summary>
        Task RefreshAsync(CancellationToken ct = default);

        /// <summary>Loads the last persisted snapshot into memory if nothing is cached yet.</summary>
        Task<DateTime?> LoadPersistedAsync(CancellationToken ct = default);
    }

    // Singleton. Rates are fetched for USD only; other bases are cross rates
    // (rate[x] / rate[base]). Provider: open.er-api.com (ExchangeRate-API open
    // access — free, no key, daily updates, attribution required on the site).
    // Override with Currency:ProviderUrl, e.g. a paid ExchangeRate-API URL.
    public class CurrencyRateService : ICurrencyRateService
    {
        private const string StoredBase = "USD";
        private const string DefaultProviderUrl = "https://open.er-api.com/v6/latest/USD";

        public const string HttpClientName = "currency-rates";

        private readonly IHttpClientFactory _httpFactory;
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _config;
        private readonly ILogger<CurrencyRateService> _logger;

        private volatile CurrencyRatesDto? _usd;

        public CurrencyRateService(IHttpClientFactory httpFactory, IServiceScopeFactory scopes, IConfiguration config, ILogger<CurrencyRateService> logger)
        {
            _httpFactory = httpFactory;
            _scopes = scopes;
            _config = config;
            _logger = logger;
        }

        public bool IsKnownCurrency(string code) =>
            _usd?.Rates.ContainsKey(code.ToUpperInvariant()) ?? false;

        public async Task<CurrencyRatesDto?> GetRatesAsync(string baseCurrency, CancellationToken ct = default)
        {
            var usd = _usd;
            if (usd is null)
            {
                await LoadPersistedAsync(ct);
                usd = _usd;
                if (usd is null) return null;
            }

            var code = baseCurrency.ToUpperInvariant();
            if (code == StoredBase) return usd;
            if (!usd.Rates.TryGetValue(code, out var baseRate) || baseRate <= 0) return null;

            var cross = usd.Rates.ToDictionary(
                kv => kv.Key,
                kv => kv.Key == code ? 1m : Math.Round(kv.Value / baseRate, 6, MidpointRounding.AwayFromZero));

            return new CurrencyRatesDto(code, usd.UpdatedAt, cross);
        }

        public async Task<DateTime?> LoadPersistedAsync(CancellationToken ct = default)
        {
            if (_usd is not null) return _usd.UpdatedAt;

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();

            var snapshot = await db.CurrencyRateSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.BaseCurrency == StoredBase, ct);
            if (snapshot is null) return null;

            var rates = JsonSerializer.Deserialize<Dictionary<string, decimal>>(snapshot.RatesJson);
            if (rates is null || rates.Count == 0) return null;

            _usd = new CurrencyRatesDto(StoredBase, snapshot.ProviderUpdatedAt, rates);
            return snapshot.FetchedAt;
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            var url = _config["Currency:ProviderUrl"] ?? DefaultProviderUrl;

            var response = await _httpFactory.CreateClient(HttpClientName).GetFromJsonAsync<ProviderResponse>(url, ct)
                ?? throw new InvalidOperationException("Empty response from currency provider.");

            if (!string.Equals(response.Result, "success", StringComparison.OrdinalIgnoreCase)
                || response.Rates is null || response.Rates.Count == 0
                || !string.Equals(response.BaseCode, StoredBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Currency provider returned an unusable response (result={response.Result}).");

            var rates = response.Rates
                .Where(kv => kv.Key.Length == 3 && kv.Value > 0)
                .ToDictionary(kv => kv.Key.ToUpperInvariant(), kv => kv.Value);
            rates[StoredBase] = 1m;

            var updatedAt = response.TimeLastUpdateUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(response.TimeLastUpdateUnix).UtcDateTime
                : DateTime.UtcNow;

            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
                var snapshot = await db.CurrencyRateSnapshots.FirstOrDefaultAsync(s => s.BaseCurrency == StoredBase, ct);
                if (snapshot is null)
                {
                    snapshot = new CurrencyRateSnapshot { BaseCurrency = StoredBase };
                    db.CurrencyRateSnapshots.Add(snapshot);
                }

                snapshot.RatesJson = JsonSerializer.Serialize(rates);
                snapshot.ProviderUpdatedAt = updatedAt;
                snapshot.FetchedAt = DateTime.UtcNow;
                snapshot.Provider = new Uri(url).Host;
                await db.SaveChangesAsync(ct);
            }

            _usd = new CurrencyRatesDto(StoredBase, updatedAt, rates);
            _logger.LogInformation("Refreshed {Count} currency rates (provider time {UpdatedAt:o})", rates.Count, updatedAt);
        }

        private sealed class ProviderResponse
        {
            [JsonPropertyName("result")] public string? Result { get; set; }
            [JsonPropertyName("base_code")] public string? BaseCode { get; set; }
            [JsonPropertyName("time_last_update_unix")] public long TimeLastUpdateUnix { get; set; }
            [JsonPropertyName("rates")] public Dictionary<string, decimal>? Rates { get; set; }
        }
    }

    // Loads the persisted snapshot at startup, fetches immediately if it is
    // missing or older than a day, then refreshes daily at 06:00 UTC. A failed
    // fetch keeps serving the last good rates and retries hourly.
    public class CurrencyRateRefresher : BackgroundService
    {
        private static readonly TimeSpan RefreshTimeUtc = TimeSpan.FromHours(6);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

        private readonly ICurrencyRateService _rates;
        private readonly ILogger<CurrencyRateRefresher> _logger;

        public CurrencyRateRefresher(ICurrencyRateService rates, ILogger<CurrencyRateRefresher> logger)
        {
            _rates = rates;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DateTime? lastFetched = null;
            try
            {
                lastFetched = await _rates.LoadPersistedAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to load persisted currency rates");
            }

            var nextRun = lastFetched is null || DateTime.UtcNow - lastFetched > TimeSpan.FromDays(1)
                ? DateTime.UtcNow
                : NextOccurrence(DateTime.UtcNow);

            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = nextRun - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, stoppingToken);

                try
                {
                    await _rates.RefreshAsync(stoppingToken);
                    nextRun = NextOccurrence(DateTime.UtcNow);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Currency rate refresh failed; retrying in {Delay}", RetryDelay);
                    nextRun = DateTime.UtcNow + RetryDelay;
                }
            }
        }

        private static DateTime NextOccurrence(DateTime fromUtc)
        {
            var candidate = fromUtc.Date + RefreshTimeUtc;
            return candidate > fromUtc ? candidate : candidate.AddDays(1);
        }
    }
}
