using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// GET /api/currency/rates?base=USD — units of each currency per 1 base, refreshed daily.
/// </summary>
[ApiController]
[AllowAnonymous]
public class CurrencyController : ControllerBase
{
    private readonly ICurrencyRateService _rates;

    public CurrencyController(ICurrencyRateService rates) => _rates = rates;

    [HttpGet("api/currency/rates")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetRates([FromQuery(Name = "base")] string? baseCurrency, CancellationToken ct = default)
    {
        var code = string.IsNullOrWhiteSpace(baseCurrency) ? "USD" : baseCurrency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetter))
            return BadRequest(new { error = "base must be a 3-letter currency code, e.g. USD." });

        var rates = await _rates.GetRatesAsync(code, ct);
        if (rates is null)
        {
            return _rates.IsKnownCurrency("USD")
                ? BadRequest(new { error = $"Unsupported currency '{code}'." })
                : StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Exchange rates are not available yet." });
        }

        return Ok(new
        {
            @base = rates.Base,
            updatedAt = rates.UpdatedAt,
            rates = rates.Rates
        });
    }
}
