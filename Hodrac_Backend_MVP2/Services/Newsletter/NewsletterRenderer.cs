using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Hodrac_Backend_MVP2.Models;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Services.Newsletter;

/// <summary>
/// Turns an issue's Markdown into the email: a simple table layout with inline
/// styles (logo text, body, footer with the unsubscribe link and support
/// address) plus a plain-text alternative. Markdown renders with raw HTML
/// disabled, and links other than http(s)/mailto are reduced to their text.
/// Image URLs are used exactly as stored.
/// </summary>
public partial class NewsletterRenderer
{
    public const string FooterReason = "You're getting this because you signed up for Hodrac updates.";
    public const string PreviewUnsubscribeUrl = "#unsubscribe";

    private const string FontStack = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";
    private const string TextColor = "#1f2937";
    private const string MutedColor = "#6b7280";
    private const string AccentColor = "#0f766e";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    private readonly NewsletterOptions _options;
    private readonly IConfiguration _config;
    private readonly NewsletterUnsubscribeTokens _tokens;

    public NewsletterRenderer(IOptions<NewsletterOptions> options, IConfiguration config, NewsletterUnsubscribeTokens tokens)
    {
        _options = options.Value;
        _config = config;
        _tokens = tokens;
    }

    // ── URLs ──────────────────────────────────────────────────────────────────

    public string ApiBaseUrl =>
        (NonEmpty(_options.ApiBaseUrl) ?? NonEmpty(_config["Api:PublicBaseUrl"]) ?? "https://api.hodrac.com").TrimEnd('/');

    public string UnsubscribeUrl(string email) =>
        $"{ApiBaseUrl}/api/newsletter/unsubscribe?token={Uri.EscapeDataString(_tokens.Create(email))}";

    // ── Emails ────────────────────────────────────────────────────────────────

    /// <summary>The email for one recipient, with List-Unsubscribe (one-click) headers.</summary>
    public HtmlEmail BuildEmail(NewsletterIssue issue, string to, bool isTest = false)
    {
        var unsubscribe = UnsubscribeUrl(to);
        var subject = isTest ? $"[Test] {issue.Subject}" : issue.Subject;
        return new HtmlEmail(
            To: to,
            Subject: subject,
            HtmlBody: RenderHtml(subject, issue.PreviewText, issue.BodyMarkdown, unsubscribe),
            TextBody: RenderText(issue.BodyMarkdown, unsubscribe),
            Headers: new Dictionary<string, string>
            {
                ["List-Unsubscribe"] = $"<{unsubscribe}>",
                ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
            });
    }

    /// <summary>Full email HTML for the admin preview (the unsubscribe link is a placeholder).</summary>
    public string RenderPreviewHtml(NewsletterIssue issue) =>
        RenderHtml(issue.Subject, issue.PreviewText, issue.BodyMarkdown, PreviewUnsubscribeUrl);

    public static string RenderHtml(string subject, string previewText, string bodyMarkdown, string unsubscribeUrl)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<title>").Append(E(subject)).Append("</title></head>")
          .Append("<body style=\"margin:0;padding:0;background-color:#f3f4f6;\">");

        // Inbox preview line, hidden in the body.
        if (!string.IsNullOrWhiteSpace(previewText))
            sb.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;\">")
              .Append(E(previewText)).Append("</div>");

        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background-color:#f3f4f6;\">")
          .Append("<tr><td align=\"center\" style=\"padding:24px 12px;\">")
          .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"max-width:600px;background-color:#ffffff;border-radius:8px;\">")
          .Append("<tr><td style=\"padding:28px 32px 4px;font-family:").Append(FontStack)
          .Append(";font-size:24px;font-weight:700;color:").Append(AccentColor).Append(";\">Hodrac</td></tr>")
          .Append("<tr><td style=\"padding:8px 32px 28px;font-family:").Append(FontStack)
          .Append(";font-size:16px;line-height:1.6;color:").Append(TextColor).Append(";\">")
          .Append(RenderBodyHtml(bodyMarkdown))
          .Append("</td></tr></table>")
          .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"max-width:600px;\">")
          .Append("<tr><td style=\"padding:16px 32px;font-family:").Append(FontStack)
          .Append(";font-size:12px;line-height:1.5;color:").Append(MutedColor).Append(";\">")
          .Append(E(FooterReason)).Append("<br>")
          .Append("<a href=\"").Append(E(unsubscribeUrl)).Append("\" style=\"color:").Append(MutedColor).Append(";\">Unsubscribe</a>")
          .Append(" &middot; Questions? Email <a href=\"mailto:").Append(HodracContact.SupportEmail).Append("\" style=\"color:")
          .Append(MutedColor).Append(";\">").Append(HodracContact.SupportEmail).Append("</a>")
          .Append("</td></tr></table>")
          .Append("</td></tr></table></body></html>");
        return sb.ToString();
    }

    /// <summary>Markdown to an inline-styled HTML fragment. Raw HTML in the Markdown is escaped.</summary>
    public static string RenderBodyHtml(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        // Only web and mail links survive; anything else (javascript:, data:, ...) becomes plain text.
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (IsSafeUrl(link.Url)) continue;
            if (!link.IsImage)
            {
                // Keep the link text in place of the link.
                var child = link.FirstChild;
                while (child is not null)
                {
                    var next = child.NextSibling;
                    child.Remove();
                    link.InsertBefore(child);
                    child = next;
                }
            }
            link.Remove();
        }
        foreach (var auto in document.Descendants<AutolinkInline>().ToList())
        {
            if (auto.IsEmail || IsSafeUrl(auto.Url)) continue;
            auto.ReplaceBy(new LiteralInline(auto.Url));
        }

        foreach (var node in document.Descendants())
        {
            var style = node switch
            {
                HeadingBlock { Level: 1 } => $"margin:28px 0 8px;font-size:22px;line-height:1.3;color:{TextColor};",
                HeadingBlock { Level: 2 } => $"margin:28px 0 8px;font-size:19px;line-height:1.3;color:{TextColor};",
                HeadingBlock => $"margin:20px 0 6px;font-size:17px;line-height:1.3;color:{TextColor};",
                ParagraphBlock => "margin:0 0 14px;",
                ListBlock => "margin:0 0 14px;padding-left:22px;",
                ListItemBlock => "margin:0 0 10px;",
                QuoteBlock => $"margin:0 0 14px;padding-left:12px;border-left:3px solid #e5e7eb;color:{MutedColor};",
                ThematicBreakBlock => "border:0;border-top:1px solid #e5e7eb;margin:24px 0;",
                LinkInline { IsImage: true } => "max-width:100%;height:auto;border:0;display:block;",
                LinkInline => $"color:{AccentColor};",
                _ => null
            };
            if (style is not null) node.GetAttributes().AddProperty("style", style);
        }

        return Markdown.ToHtml(document, Pipeline);
    }

    /// <summary>Plain-text alternative: links become "text (url)", Markdown markers are stripped.</summary>
    public static string RenderText(string? markdown, string unsubscribeUrl)
    {
        var text = markdown ?? string.Empty;
        text = ImageRegex().Replace(text, "$1");
        text = LinkRegex().Replace(text, m =>
            string.Equals(m.Groups[1].Value, m.Groups[2].Value, StringComparison.Ordinal)
                ? m.Groups[2].Value
                : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
        text = HeadingRegex().Replace(text, "");
        text = text.Replace("**", "").Replace("__", "");
        text = EscapeRegex().Replace(text, "$1");
        text = text.Replace("\r\n", "\n").Trim();

        return $"{text}\n\n--\n{FooterReason}\nUnsubscribe: {unsubscribeUrl}\nQuestions? Email {HodracContact.SupportEmail}\n";
    }

    private static bool IsSafeUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto);

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)\s]+)\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[((?:\\.|[^\]\\])*)\]\(([^)\s]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\\([\\`*_\[\]{}()<>#+\-.!|])")]
    private static partial Regex EscapeRegex();
}
