using System.Net;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Hodrac_Backend_MVP2.Services
{
    // SMTP-backed IEmailSender (MailKit). Works with any provider that offers
    // SMTP (SendPulse, SendGrid, Resend, Mailgun, Postmark, SES...). Configure via:
    //   Email:Smtp:Host, Email:Smtp:Port (default 587), Email:Smtp:Username,
    //   Email:Smtp:Password, Email:From (default: Email:Smtp:Username),
    //   Email:FromName (default "Hodrac")
    // Port 465 uses implicit TLS; any other port upgrades with STARTTLS.
    // When Email:Smtp:Host is not set (local dev), emails are logged instead
    // of sent so flows like password reset can still be exercised.
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, List<string> bodyLines, string deepLink)
        {
            var host = _config["Email:Smtp:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                _logger.LogWarning(
                    "Email:Smtp:Host not configured; not sending. To={To} Subject={Subject} Link={Link}",
                    to, subject, deepLink);
                return;
            }

            var port = int.TryParse(_config["Email:Smtp:Port"], out var p) ? p : 587;
            var username = _config["Email:Smtp:Username"];
            var password = _config["Email:Smtp:Password"];

            // Most providers (e.g. SendPulse) accept the account login as a
            // verified sender, so fall back to it when Email:From isn't set.
            var from = _config["Email:From"] ?? username
                ?? throw new InvalidOperationException("Email:From (or Email:Smtp:Username) is not configured.");
            var fromName = _config["Email:FromName"] ?? "Hodrac";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new BodyBuilder
            {
                HtmlBody = BuildHtml(bodyLines, deepLink),
                TextBody = BuildText(bodyLines, deepLink)
            }.ToMessageBody();

            using var client = new SmtpClient { Timeout = 20_000 };

            await client.ConnectAsync(host, port,
                port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);

            if (!string.IsNullOrEmpty(username))
            {
                // Throws AuthenticationException with the server's actual reply
                // (e.g. "535: Authentication failed") instead of failing later.
                await client.AuthenticateAsync(username, password ?? string.Empty);
            }
            else
            {
                _logger.LogWarning("Email:Smtp:Username not configured; sending without authentication.");
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private static string BuildHtml(List<string> bodyLines, string deepLink)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:sans-serif;font-size:15px;line-height:1.5\">");
            foreach (var line in bodyLines)
                sb.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
            if (!string.IsNullOrWhiteSpace(deepLink))
            {
                var href = WebUtility.HtmlEncode(deepLink);
                sb.Append("<p><a href=\"").Append(href).Append("\">").Append(href).Append("</a></p>");
            }
            sb.Append("</div>");
            return sb.ToString();
        }

        private static string BuildText(List<string> bodyLines, string deepLink)
        {
            var text = string.Join("\n\n", bodyLines);
            return string.IsNullOrWhiteSpace(deepLink) ? text : $"{text}\n\n{deepLink}";
        }
    }
}
