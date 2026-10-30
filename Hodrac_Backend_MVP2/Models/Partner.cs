using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.Models;

// ─── Partner ──────────────────────────────────────────────────────────────────
// A booking/affiliate partner (hotel group, tour operator, OTA...). The brand
// itself — where it's shown is decided by PartnerLink rows.

public class Partner
{
    public Guid PartnerId { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? LogoUrl { get; set; }

    /// <summary>Fallback CTA when a link doesn't set its own, e.g. "Book with Acme".</summary>
    [MaxLength(100)]
    public string DefaultCtaLabel { get; set; } = "Book now";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PartnerLink> Links { get; set; } = new List<PartnerLink>();
}

// ─── PartnerLink ──────────────────────────────────────────────────────────────
// Places a partner on exactly ONE destination or wishlist (DB check
// constraint). When several active links target the same item, the highest
// Priority wins. LinkId is the public id used by GET /api/out/{linkId}.

public class PartnerLink
{
    public Guid LinkId { get; set; } = Guid.NewGuid();

    public Guid PartnerId { get; set; }
    public Partner Partner { get; set; } = null!;

    public Guid? DestinationId { get; set; }
    public Destination? Destination { get; set; }

    public Guid? WishlistId { get; set; }
    public Wishlist? Wishlist { get; set; }

    [Required, MaxLength(2000)]
    public string BookingUrl { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? CtaLabel { get; set; }

    public bool IsSponsored { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Optional campaign window. Null = open-ended.</summary>
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    /// <summary>
    /// Append utm_source=hodrac &amp; co. to the redirect. Turn off for partners whose
    /// booking URL breaks with extra query parameters.
    /// </summary>
    public bool AppendUtm { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PartnerLinkClick> Clicks { get; set; } = new List<PartnerLinkClick>();
}

// ─── PartnerLinkClick ─────────────────────────────────────────────────────────
// One row per outbound click through /api/out/{linkId}.

public class PartnerLinkClick
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LinkId { get; set; }
    public PartnerLink Link { get; set; } = null!;

    /// <summary>
    /// The logged-in user, from the signed <c>c</c> token PartnerService puts in
    /// trackingUrl (or a JWT if one was sent). Null for anonymous visitors.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>Anonymous first-party visitor id (hodrac_vid cookie on the API domain). No IP is stored.</summary>
    [MaxLength(64)]
    public string? VisitorId { get; set; }

    /// <summary>Where the click came from — one of <see cref="PartnerClickSources.All"/>, or "unknown".</summary>
    [Required, MaxLength(30)]
    public string Source { get; set; } = PartnerClickSources.Unknown;

    /// <summary>Crawler / link-preview / prefetch hit. Still redirected, excluded from stats by default.</summary>
    public bool IsBot { get; set; }

    [MaxLength(2000)]
    public string? Referrer { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public DateTime ClickedAt { get; set; } = DateTime.UtcNow;
}

public static class PartnerClickSources
{
    public const string Unknown = "unknown";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = new[]
    {
        "destination_detail", "destination_card", "wishlist_detail", "wishlist_card",
        "featured", "search", "share", "trip", Other
    };

    /// <summary>Missing → "unknown"; a whitelisted value (case-insensitive) → itself; anything else → "other".</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Unknown;
        var trimmed = value.Trim();
        return All.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase)) ?? Other;
    }
}

// ─── PartnerLead ──────────────────────────────────────────────────────────────
// Submissions from the public "partner with us" form.

public class PartnerLead
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string ContactName { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Website { get; set; }

    [MaxLength(100)]
    public string? BusinessType { get; set; }

    public List<string> Interests { get; set; } = new();

    [MaxLength(5000)]
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Admin triage ──────────────────────────────────────────────────────────

    /// <summary>One of <see cref="PartnerLeadStatuses.All"/>.</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = PartnerLeadStatuses.New;

    [MaxLength(5000)]
    public string? InternalNotes { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public static class PartnerLeadStatuses
{
    public const string New = "New";
    public const string Contacted = "Contacted";
    public const string Qualified = "Qualified";
    public const string Won = "Won";
    public const string Lost = "Lost";

    public static readonly IReadOnlyList<string> All = new[] { New, Contacted, Qualified, Won, Lost };

    /// <summary>Case-insensitive match; returns the canonical spelling or null.</summary>
    public static string? Normalize(string? value)
        => value is null ? null : All.FirstOrDefault(s => string.Equals(s, value.Trim(), StringComparison.OrdinalIgnoreCase));
}
