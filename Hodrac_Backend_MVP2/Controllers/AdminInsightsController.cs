using System.Security.Claims;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin behavioural insights (MongoDB profiles + Postgres saves). Aggregate-only:
/// no per-user data is returned; buckets with fewer than 3 members are folded into
/// "other" (see AdminInsightsService / AdminInsightsDtos for the exact rules).
///
///   GET  /api/admin/insights/audience
///   GET  /api/admin/insights/wishlists/{id}/audience
///   GET  /api/admin/insights/wishlists?segmentType=travelerType|budget|priority|prototype&amp;segment=&amp;limit=20
///   GET  /api/admin/insights/destinations/{id}/audience          (destination_click events, last 90 days)
///   GET  /api/admin/insights/tags?segmentType=&amp;segment=&amp;limit=20  (segmentType also accepts income)
///   POST /api/admin/insights/rebuild                             → 202 { jobId, … } | 409 if one is running
///   GET  /api/admin/insights/rebuild/{jobId}                     → job status / progress
///
/// If MongoDB is unreachable every endpoint returns 503 (the driver's 5s server
/// selection timeout bounds the wait).
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/insights")]
[MongoUnavailable]
public class AdminInsightsController : ControllerBase
{
    private const int MaxLimit = 100;

    private readonly AdminInsightsService _insights;
    private readonly InsightsRebuildJobManager _rebuild;

    public AdminInsightsController(AdminInsightsService insights, InsightsRebuildJobManager rebuild)
    {
        _insights = insights;
        _rebuild = rebuild;
    }

    [HttpGet("audience")]
    public async Task<ActionResult<AdminAudienceInsightsDto>> GetAudience(CancellationToken ct = default)
        => Ok(await _insights.GetAudienceAsync(ct));

    [HttpGet("wishlists/{id:guid}/audience")]
    public async Task<ActionResult<AdminWishlistAudienceDto>> GetWishlistAudience(Guid id, CancellationToken ct = default)
    {
        var dto = await _insights.GetWishlistAudienceAsync(id, ct);
        return dto is null ? NotFound(new { error = "Wishlist not found." }) : Ok(dto);
    }

    [HttpGet("wishlists")]
    public async Task<ActionResult<AdminSegmentWishlistsDto>> GetTopWishlistsForSegment(
        [FromQuery] string? segmentType, [FromQuery] string? segment, [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (!AdminInsightsService.TryParseSegmentType(segmentType, out var type) || type == InsightSegmentType.Income)
            return BadRequest(new { error = "segmentType must be one of travelerType, budget, priority, prototype." });
        if (string.IsNullOrWhiteSpace(segment))
            return BadRequest(new { error = "segment is required." });

        return Ok(await _insights.GetTopWishlistsForSegmentAsync(type, segment.Trim(), Math.Clamp(limit, 1, MaxLimit), ct));
    }

    [HttpGet("destinations/{id:guid}/audience")]
    public async Task<ActionResult<AdminDestinationAudienceDto>> GetDestinationAudience(Guid id, CancellationToken ct = default)
    {
        var dto = await _insights.GetDestinationAudienceAsync(id, ct);
        return dto is null ? NotFound(new { error = "Destination not found." }) : Ok(dto);
    }

    [HttpGet("tags")]
    public async Task<ActionResult<AdminTagInsightsDto>> GetTags(
        [FromQuery] string? segmentType, [FromQuery] string? segment, [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        InsightSegmentType? type = null;
        if (!string.IsNullOrWhiteSpace(segmentType))
        {
            if (!AdminInsightsService.TryParseSegmentType(segmentType, out var parsed))
                return BadRequest(new { error = "segmentType must be one of travelerType, budget, priority, prototype, income." });
            if (string.IsNullOrWhiteSpace(segment))
                return BadRequest(new { error = "segment is required when segmentType is given." });
            type = parsed;
        }

        return Ok(await _insights.GetTagsAsync(type, segment?.Trim(), Math.Clamp(limit, 1, MaxLimit), ct));
    }

    // ── Rebuild / backfill ────────────────────────────────────────────────────

    [HttpPost("rebuild")]
    public ActionResult<InsightsRebuildStatusDto> StartRebuild()
    {
        var requestedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var (started, running) = _rebuild.TryStart(requestedBy);
        if (started is null)
            return Conflict(new { error = "A rebuild is already running.", job = running!.Snapshot() });

        return AcceptedAtAction(nameof(GetRebuild), new { jobId = started.JobId }, started.Snapshot());
    }

    [HttpGet("rebuild/{jobId}")]
    public ActionResult<InsightsRebuildStatusDto> GetRebuild(string jobId)
    {
        var job = _rebuild.Get(jobId);
        return job is null ? NotFound(new { error = "Unknown rebuild job (job state is in-memory and lost on restart)." }) : Ok(job.Snapshot());
    }
}

/// <summary>Maps MongoDB connectivity/driver failures to 503 instead of a 500.</summary>
public sealed class MongoUnavailableAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var message = context.Exception switch
        {
            TimeoutException or MongoConnectionException =>
                "The behavioural data store (MongoDB) is unreachable. Try again later.",
            MongoExecutionTimeoutException =>
                "The behavioural data query timed out. Try again later.",
            MongoException =>
                "The behavioural data store (MongoDB) returned an error. Try again later.",
            _ => null
        };
        if (message is null) return;

        var logger = context.HttpContext.RequestServices.GetService<ILogger<MongoUnavailableAttribute>>();
        logger?.LogWarning(context.Exception, "Admin insights: MongoDB unavailable for {Path}", context.HttpContext.Request.Path);

        context.Result = new ObjectResult(new { error = message }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
