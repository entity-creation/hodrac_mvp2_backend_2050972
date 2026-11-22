using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.Models;

/// <summary>
/// Draft = never public. Scheduled = public once PublishedAt &lt;= now (visibility is computed
/// at query time, so no job flips the status). Published = public (PublishedAt &lt;= now).
/// </summary>
public enum BlogPostStatus
{
    Draft,
    Scheduled,
    Published,
}

/// <summary>
/// An editorial article written by an admin. The body is stored as raw Markdown and may contain
/// embed markers ({{destination:…}}, {{wishlist:…}}, {{event:…}}) that the API resolves into cards.
/// </summary>
public class BlogPost
{
    public const int MaxTitleLength = 200;
    public const int MaxSlugLength = 220;
    public const int MaxExcerptLength = 500;
    public const int MaxUrlLength = 500;
    public const int MaxCoverImageAltLength = 200;
    public const int MaxAuthorNameLength = 120;
    public const int MaxMetaTitleLength = 70;
    public const int MaxMetaDescriptionLength = 160;

    public Guid Id { get; set; }

    [Required, MaxLength(MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>URL key. Unique; generated from the title on create, editable by admins (old slugs go to BlogPostSlugHistory).</summary>
    [Required, MaxLength(MaxSlugLength)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(MaxExcerptLength)]
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>Raw Markdown as written (never rewritten). Required to publish.</summary>
    public string BodyMarkdown { get; set; } = string.Empty;

    [MaxLength(MaxUrlLength)]
    public string? CoverImageUrl { get; set; }

    [MaxLength(MaxCoverImageAltLength)]
    public string? CoverImageAlt { get; set; }

    /// <summary>Byline text.</summary>
    [MaxLength(MaxAuthorNameLength)]
    public string? AuthorName { get; set; }

    /// <summary>The admin who created the post.</summary>
    public Guid CreatedByUserId { get; set; }

    public BlogPostStatus Status { get; set; } = BlogPostStatus.Draft;

    /// <summary>UTC. Null for drafts; set for Scheduled (future) and Published.</summary>
    public DateTime? PublishedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── SEO ───────────────────────────────────────────────────────────────────
    [MaxLength(MaxMetaTitleLength)]
    public string? MetaTitle { get; set; }

    [MaxLength(MaxMetaDescriptionLength)]
    public string? MetaDescription { get; set; }

    [MaxLength(MaxUrlLength)]
    public string? CanonicalUrl { get; set; }

    /// <summary>Computed on save from the body's word count (~200 wpm, minimum 1).</summary>
    public int ReadingTimeMinutes { get; set; } = 1;

    // ── Place ─────────────────────────────────────────────────────────────────
    public Guid? CityId { get; set; }
    public City? City { get; set; }

    public Guid? CountryId { get; set; }
    public Country? Country { get; set; }

    // ── Links ─────────────────────────────────────────────────────────────────
    public ICollection<BlogPostDestination> Destinations { get; set; } = new List<BlogPostDestination>();
    public ICollection<BlogPostWishlist> Wishlists { get; set; } = new List<BlogPostWishlist>();
    public ICollection<BlogPostEvent> Events { get; set; } = new List<BlogPostEvent>();
    public ICollection<BlogPostTag> Tags { get; set; } = new List<BlogPostTag>();
    public ICollection<BlogPostSlugHistory> SlugHistory { get; set; } = new List<BlogPostSlugHistory>();
}

public class BlogPostDestination
{
    public Guid BlogPostId { get; set; }
    public BlogPost BlogPost { get; set; } = null!;

    public Guid DestinationId { get; set; }
    public Destination Destination { get; set; } = null!;

    public int SortOrder { get; set; }
}

public class BlogPostWishlist
{
    public Guid BlogPostId { get; set; }
    public BlogPost BlogPost { get; set; } = null!;

    public Guid WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;

    public int SortOrder { get; set; }
}

public class BlogPostEvent
{
    public Guid BlogPostId { get; set; }
    public BlogPost BlogPost { get; set; } = null!;

    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public int SortOrder { get; set; }
}

public class BlogPostTag
{
    public Guid BlogPostId { get; set; }
    public BlogPost BlogPost { get; set; } = null!;

    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

/// <summary>A slug a post used to have. GET /api/blog/{oldSlug} answers 301 to the current slug.</summary>
public class BlogPostSlugHistory
{
    public Guid Id { get; set; }

    [Required, MaxLength(BlogPost.MaxSlugLength)]
    public string OldSlug { get; set; } = string.Empty;

    public Guid BlogPostId { get; set; }
    public BlogPost BlogPost { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
