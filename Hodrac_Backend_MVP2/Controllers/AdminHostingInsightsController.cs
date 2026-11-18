using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.Services.Hosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Hodrac Host business numbers (Postgres only, so it keeps working when
/// MongoDB is down; the Mongo audience insights stay in AdminInsightsController).
///
///   GET /api/admin/insights/hosting?from=&amp;to=
///
/// from/to are inclusive WAT (UTC+1) dates; default is the current WAT month
/// and the 11 before it, up to today. At most 60 months. Definitions are on
/// <see cref="AdminHostingInsightsDto"/>.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/insights/hosting")]
public class AdminHostingInsightsController : ControllerBase
{
    private const int MaxMonths = 60;

    private readonly HostingInsightsService _insights;

    public AdminHostingInsightsController(HostingInsightsService insights) => _insights = insights;

    [HttpGet]
    public async Task<ActionResult<AdminHostingInsightsDto>> Get(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var today = HostTripService.TodayWat(DateTime.UtcNow);
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-11);

        if (end < start)
        {
            ModelState.AddModelError(nameof(to), "'to' must be on or after 'from'.");
            return ValidationProblem(ModelState);
        }
        if (new DateOnly(start.Year, start.Month, 1).AddMonths(MaxMonths) <= end)
        {
            ModelState.AddModelError(nameof(from), $"The period can cover at most {MaxMonths} months.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _insights.GetAsync(start, end, ct));
    }
}
