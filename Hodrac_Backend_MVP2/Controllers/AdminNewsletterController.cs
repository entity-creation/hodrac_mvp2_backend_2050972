using System.Text;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.AdminDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Infrastucture.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for newsletter subscribers.
///
///   GET    /api/admin/newsletter?page=&amp;pageSize=&amp;search=
///   GET    /api/admin/newsletter/export   — CSV download (emailId,userEmail)
///   DELETE /api/admin/newsletter/{id}
///
/// NewsLetterEmail has no timestamp column, so "newest first" is not possible yet;
/// rows are ordered by email (then id) for a stable paging order.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/newsletter")]
public class AdminNewsletterController : ControllerBase
{
    private readonly HodracDbContext _db;
    private readonly ILogger<AdminNewsletterController> _logger;

    public AdminNewsletterController(HodracDbContext db, ILogger<AdminNewsletterController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AdminPagedResult<AdminNewsletterSubscriberDto>>> GetSubscribers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.NewsLetterEmails.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(n => EF.Functions.ILike(n.UserEmail, pattern));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(n => n.UserEmail)
            .ThenBy(n => n.EmailId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new AdminNewsletterSubscriberDto(n.EmailId, n.UserEmail, n.UnsubscribedAt))
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminNewsletterSubscriberDto>(items, page, pageSize, total));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct = default)
    {
        var csv = new StringBuilder();
        csv.Append("emailId,userEmail\r\n");

        await foreach (var row in _db.NewsLetterEmails
                           .AsNoTracking()
                           .OrderBy(n => n.UserEmail)
                           .ThenBy(n => n.EmailId)
                           .Select(n => new { n.EmailId, n.UserEmail })
                           .AsAsyncEnumerable()
                           .WithCancellation(ct))
        {
            csv.Append(row.EmailId.ToString()).Append(',').Append(CsvExport.Field(row.UserEmail)).Append("\r\n");
        }

        var bytes = CsvExport.ToBytes(csv);
        var fileName = $"newsletter-subscribers-{DateTime.UtcNow:yyyy-MM-dd}.csv";

        _logger.LogInformation("Admin exported newsletter subscribers");
        return File(bytes, CsvExport.ContentType, fileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSubscriber(Guid id, CancellationToken ct = default)
    {
        var deleted = await _db.NewsLetterEmails
            .Where(n => n.EmailId == id)
            .ExecuteDeleteAsync(ct);

        if (deleted == 0) return NotFound();

        _logger.LogInformation("Admin deleted newsletter subscriber {EmailId}", id);
        return NoContent();
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
