using System.Net;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Newsletter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Public newsletter unsubscribe (the link and the List-Unsubscribe header in every issue).
///
///   GET  /api/newsletter/unsubscribe?token=   small HTML confirmation page (400 page for a bad token)
///   POST /api/newsletter/unsubscribe?token=   RFC 8058 one-click (body "List-Unsubscribe=One-Click"); 200 / 400
///
/// Both mark the sign-up row(s) for the token's email as unsubscribed and set
/// NewsletterOptOut on the account with that email. Idempotent.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/newsletter")]
public class NewsletterController : ControllerBase
{
    private readonly NewsletterUnsubscribeTokens _tokens;
    private readonly NewsletterAudienceService _audience;
    private readonly ILogger<NewsletterController> _logger;

    public NewsletterController(
        NewsletterUnsubscribeTokens tokens,
        NewsletterAudienceService audience,
        ILogger<NewsletterController> logger)
    {
        _tokens = tokens;
        _audience = audience;
        _logger = logger;
    }

    [HttpGet("unsubscribe")]
    public async Task<IActionResult> UnsubscribePage([FromQuery] string? token, CancellationToken ct = default)
    {
        var email = _tokens.Read(token);
        if (email is null)
            return Page(StatusCodes.Status400BadRequest, "This unsubscribe link doesn't work",
                $"The link may be incomplete. Copy the whole link from the email, or write to {HodracContact.SupportEmail} and we'll take you off the list.");

        await UnsubscribeAsync(email, ct);
        return Page(StatusCodes.Status200OK, "You're unsubscribed",
            $"{email} won't get the Hodrac newsletter any more. If you have a Hodrac account, you can turn it back on in your notification settings.");
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> UnsubscribeOneClick([FromQuery] string? token, CancellationToken ct = default)
    {
        var email = _tokens.Read(token);
        if (email is null) return BadRequest(new { error = "Invalid unsubscribe token." });

        await UnsubscribeAsync(email, ct);
        return Ok(new { unsubscribed = true });
    }

    private async Task UnsubscribeAsync(string email, CancellationToken ct)
    {
        var changed = await _audience.UnsubscribeAsync(email, DateTime.UtcNow, ct);
        if (changed) _logger.LogInformation("Newsletter unsubscribe processed");
    }

    private ContentResult Page(int status, string heading, string message)
    {
        var h = WebUtility.HtmlEncode(heading);
        var m = WebUtility.HtmlEncode(message);
        var html =
            "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<meta name=\"robots\" content=\"noindex\">" +
            $"<title>{h} | Hodrac</title></head>" +
            "<body style=\"margin:0;background:#f3f4f6;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#1f2937;\">" +
            "<div style=\"max-width:480px;margin:48px auto;padding:28px 24px;background:#ffffff;border-radius:8px;\">" +
            "<div style=\"font-size:22px;font-weight:700;color:#0f766e;margin-bottom:16px;\">Hodrac</div>" +
            $"<h1 style=\"font-size:20px;margin:0 0 12px;\">{h}</h1>" +
            $"<p style=\"font-size:15px;line-height:1.6;margin:0;\">{m}</p>" +
            "</div></body></html>";
        return new ContentResult { StatusCode = status, ContentType = "text/html; charset=utf-8", Content = html };
    }
}
