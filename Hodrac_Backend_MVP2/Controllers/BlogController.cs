using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.BlogDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Public (anonymous) blog reads. Only visible posts: Published, or Scheduled with PublishedAt &lt;= now.
///
///   GET /api/blog?tag=&amp;cityId=&amp;countryId=&amp;destinationId=&amp;wishlistId=&amp;eventId=&amp;search=&amp;page=1&amp;pageSize=12
///   GET /api/blog/{slug}      (301 + { redirectToSlug } for a retired slug)
///   GET /api/blog/sitemap
///   GET /api/blog/tags
///
/// Bodies are raw Markdown: render them with raw HTML disabled (or sanitized) and replace each
/// embed marker with its card from "embeds".
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/blog")]
public class BlogController : ControllerBase
{
    public const int MaxRelatedPosts = 4;
    public const int MaxSitemapEntries = 5000;

    private readonly HodracDbContext _db;

    public BlogController(HodracDbContext db) => _db = db;

    /// <summary>Newest first (PublishedAt descending). tag = tag key. search matches title and excerpt.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<BlogPostSummaryDto>>> GetPosts(
        [FromQuery] string? tag,
        [FromQuery] Guid? cityId,
        [FromQuery] Guid? countryId,
        [FromQuery] Guid? destinationId,
        [FromQuery] Guid? wishlistId,
        [FromQuery] Guid? eventId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.BlogPosts.AsNoTracking().Visible(DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var key = tag.Trim();
            query = query.Where(p => p.Tags.Any(t => t.Tag.Key == key));
        }
        if (cityId is not null) query = query.Where(p => p.CityId == cityId);
        if (countryId is not null) query = query.Where(p => p.CountryId == countryId);
        if (destinationId is not null) query = query.Where(p => p.Destinations.Any(d => d.DestinationId == destinationId));
        if (wishlistId is not null) query = query.Where(p => p.Wishlists.Any(w => w.WishlistId == wishlistId));
        if (eventId is not null) query = query.Where(p => p.Events.Any(e => e.EventId == eventId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(p => EF.Functions.ILike(p.Title, pattern) || EF.Functions.ILike(p.Excerpt, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.PublishedAt)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(BlogQueries.ToSummary)
            .ToListAsync(ct);

        return Ok(new PagedResult<BlogPostSummaryDto>(
            Page: page,
            PageSize: pageSize,
            TotalCount: total,
            TotalPages: (int)Math.Ceiling(total / (double)pageSize),
            Items: items));
    }

    /// <summary>
    /// Every visible post (newest first, capped at 5,000) for the frontend's sitemap generator.
    /// </summary>
    [HttpGet("sitemap")]
    public async Task<ActionResult<List<BlogSitemapEntryDto>>> GetSitemap(CancellationToken ct = default)
    {
        var entries = await _db.BlogPosts.AsNoTracking()
            .Visible(DateTime.UtcNow)
            .OrderByDescending(p => p.PublishedAt)
            .ThenBy(p => p.Id)
            .Take(MaxSitemapEntries)
            .Select(p => new BlogSitemapEntryDto(p.Slug, p.UpdatedAt, p.PublishedAt))
            .ToListAsync(ct);
        return Ok(entries);
    }

    /// <summary>Tags used by at least one visible post, most used first.</summary>
    [HttpGet("tags")]
    public async Task<ActionResult<List<BlogTagCountDto>>> GetTags(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tags = await _db.BlogPostTags.AsNoTracking()
            .Where(bt => bt.BlogPost.Status != BlogPostStatus.Draft
                      && bt.BlogPost.PublishedAt != null
                      && bt.BlogPost.PublishedAt <= now)
            .GroupBy(bt => new { bt.Tag.Key, bt.Tag.TagName })
            .Select(g => new BlogTagCountDto(g.Key.Key, g.Key.TagName, g.Count()))
            .ToListAsync(ct);

        return Ok(tags
            .OrderByDescending(t => t.PostCount)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>
    /// 200 = the post. 301 = the slug was retired: Location is /api/blog/{currentSlug} and the
    /// body is { "redirectToSlug": "…" }. 404 = no such visible post.
    /// </summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct = default)
    {
        var key = slug.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        var post = await _db.BlogPosts.AsNoTracking()
            .Visible(now)
            .Where(p => p.Slug == key)
            .Select(p => new
            {
                Summary = new BlogPostSummaryDto(
                    p.Id, p.Title, p.Slug, p.Excerpt, p.CoverImageUrl, p.CoverImageAlt, p.AuthorName,
                    p.PublishedAt, p.UpdatedAt, p.ReadingTimeMinutes,
                    p.Tags.OrderBy(t => t.Tag.TagName).Select(t => new BlogTagDto(t.Tag.Key, t.Tag.TagName)).ToList(),
                    p.City != null ? new BlogPlaceDto(p.City.CityId, p.City.CityName) : null,
                    p.Country != null ? new BlogPlaceDto(p.Country.CountryId, p.Country.CountryName) : null),
                p.BodyMarkdown,
                p.MetaTitle,
                p.MetaDescription,
                p.CanonicalUrl,
            })
            .FirstOrDefaultAsync(ct);

        if (post is null)
        {
            // Retired slug of a post that is still visible → redirect.
            var current = await _db.BlogPostSlugHistory.AsNoTracking()
                .Where(h => h.OldSlug == key)
                .Select(h => h.BlogPost)
                .Visible(now)
                .Select(p => p.Slug)
                .FirstOrDefaultAsync(ct);

            if (current is null) return NotFound();

            Response.Headers.Location = $"/api/blog/{current}";
            return StatusCode(StatusCodes.Status301MovedPermanently, new BlogRedirectDto(current));
        }

        var s = post.Summary;
        var dto = new BlogPostDto(
            s.Id, s.Title, s.Slug, s.Excerpt, s.CoverImageUrl, s.CoverImageAlt, s.AuthorName,
            s.PublishedAt, s.UpdatedAt, s.ReadingTimeMinutes, s.Tags, s.City, s.Country,
            BodyMarkdown: post.BodyMarkdown,
            MetaTitle: string.IsNullOrWhiteSpace(post.MetaTitle) ? s.Title : post.MetaTitle,
            MetaDescription: string.IsNullOrWhiteSpace(post.MetaDescription) ? s.Excerpt : post.MetaDescription,
            CanonicalUrl: post.CanonicalUrl,
            Destinations: await BlogQueries.LinkedDestinationsAsync(_db, s.Id, ct),
            Wishlists: await BlogQueries.LinkedWishlistsAsync(_db, s.Id, ct),
            Events: await BlogQueries.LinkedEventsAsync(_db, s.Id, publishedOnly: true, ct),
            Embeds: await BlogQueries.ResolveEmbedsAsync(_db, post.BodyMarkdown, ct),
            RelatedPosts: await BlogQueries.RelatedToPostAsync(_db, s.Id, MaxRelatedPosts, ct));

        return Ok(dto);
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
