using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.BlogDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hodrac_Backend_MVP2.Controllers;

/// <summary>
/// Admin API for blog posts.
///
///   GET    /api/admin/blog?search=&amp;status=&amp;tag=&amp;cityId=&amp;destinationId=&amp;wishlistId=&amp;page=&amp;pageSize=
///   GET    /api/admin/blog/{id}           (any status: also the draft preview)
///   POST   /api/admin/blog
///   PATCH  /api/admin/blog/{id}
///   POST   /api/admin/blog/{id}/publish   (optional body { publishedAt }; future = Scheduled)
///   POST   /api/admin/blog/{id}/unpublish (→ Draft)
///   DELETE /api/admin/blog/{id}
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/blog")]
public class AdminBlogController : ControllerBase
{
    private readonly HodracDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AdminBlogController> _logger;

    public AdminBlogController(HodracDbContext db, ICurrentUserService currentUser, ILogger<AdminBlogController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// status: Draft | Scheduled | Published (effective: a scheduled post whose time has come counts as Published).
    /// search matches title, slug, excerpt and author. Most recently updated first.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AdminPagedResult<AdminBlogPostSummaryDto>>> GetPosts(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? tag = null,
        [FromQuery] Guid? cityId = null,
        [FromQuery] Guid? destinationId = null,
        [FromQuery] Guid? wishlistId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var now = DateTime.UtcNow;

        var query = _db.BlogPosts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseStatus(status, out var parsed))
            {
                ModelState.AddModelError("Status", "Status must be Draft, Scheduled or Published.");
                return ValidationProblem(ModelState);
            }
            query = query.WithEffectiveStatus(parsed, now);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Title, pattern) ||
                EF.Functions.ILike(p.Slug, pattern) ||
                EF.Functions.ILike(p.Excerpt, pattern) ||
                (p.AuthorName != null && EF.Functions.ILike(p.AuthorName, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var key = tag.Trim();
            query = query.Where(p => p.Tags.Any(t => t.Tag.Key == key));
        }
        if (cityId is not null) query = query.Where(p => p.CityId == cityId);
        if (destinationId is not null) query = query.Where(p => p.Destinations.Any(d => d.DestinationId == destinationId));
        if (wishlistId is not null) query = query.Where(p => p.Wishlists.Any(w => w.WishlistId == wishlistId));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(BlogQueries.ToAdminSummary(now))
            .ToListAsync(ct);

        return Ok(new AdminPagedResult<AdminBlogPostSummaryDto>(items, page, pageSize, total));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminBlogPostDto>> GetPost(Guid id, CancellationToken ct = default)
    {
        var dto = await LoadDto(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<AdminBlogPostDto>> CreatePost(CreateBlogPostRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) ModelState.AddModelError("Title", "Title is required.");
        CheckLength("Title", title, BlogPost.MaxTitleLength);

        var excerpt = request.Excerpt?.Trim() ?? string.Empty;
        var body = request.BodyMarkdown ?? string.Empty;
        var text = ValidateText(excerpt, body, request.CoverImageUrl, request.CoverImageAlt, request.AuthorName,
            request.MetaTitle, request.MetaDescription, request.CanonicalUrl);

        var status = BlogPostStatus.Draft;
        if (request.Status is not null && !TryParseStatus(request.Status, out status))
            ModelState.AddModelError("Status", "Status must be Draft, Scheduled or Published.");

        var (finalStatus, publishedAt) = ResolvePublication(status, request.PublishedAt?.UtcDateTime, now);
        if (finalStatus != BlogPostStatus.Draft) RequirePublishable(excerpt, body);

        var (cityId, countryId) = await ValidatePlaceAsync(request.CityId, request.CountryId, ct);
        var tagIds = await ValidateTagsAsync(request.TagKeys, ct);
        var destinationIds = await ValidateDestinationsAsync(request.DestinationIds, ct);
        var wishlistIds = await ValidateWishlistsAsync(request.WishlistIds, ct);
        var eventIds = await ValidateEventsAsync(request.EventIds, ct);

        string? slug = null;
        if (!string.IsNullOrWhiteSpace(request.Slug))
            slug = await ValidateExplicitSlugAsync(request.Slug, postId: null, ct);

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        slug ??= await BlogQueries.UniqueSlugAsync(_db, BlogQueries.BaseSlug(title), null, ct);

        var post = new BlogPost
        {
            Id = Guid.NewGuid(),
            Title = title,
            Slug = slug,
            Excerpt = excerpt,
            BodyMarkdown = body,
            CoverImageUrl = text.CoverImageUrl,
            CoverImageAlt = text.CoverImageAlt,
            AuthorName = text.AuthorName,
            CreatedByUserId = _currentUser.UserId,
            Status = finalStatus,
            PublishedAt = publishedAt,
            CreatedAt = now,
            UpdatedAt = now,
            MetaTitle = text.MetaTitle,
            MetaDescription = text.MetaDescription,
            CanonicalUrl = text.CanonicalUrl,
            ReadingTimeMinutes = BlogQueries.ReadingTimeMinutes(body),
            CityId = cityId,
            CountryId = countryId,
        };
        ReplaceLinks(post, tagIds, destinationIds, wishlistIds, eventIds);

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // An explicit slug may reuse another post's retired slug: the live post wins.
            await _db.BlogPostSlugHistory.Where(h => h.OldSlug == slug).ExecuteDeleteAsync(ct);
            _db.BlogPosts.Add(post);
            if (!await TrySaveAsync(ct)) return ValidationProblem(ModelState);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation("Admin created blog post {PostId} ({Slug}) as {Status}", post.Id, post.Slug, post.Status);

        var dto = await LoadDto(post.Id, ct);
        return CreatedAtAction(nameof(GetPost), new { id = post.Id }, dto);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<AdminBlogPostDto>> UpdatePost(Guid id, UpdateBlogPostRequest request, CancellationToken ct = default)
    {
        var post = await _db.BlogPosts
            .Include(p => p.Destinations)
            .Include(p => p.Wishlists)
            .Include(p => p.Events)
            .Include(p => p.Tags)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound();

        var now = DateTime.UtcNow;

        // Validate the resulting state, not just the fields sent.
        var title = request.Title?.Trim() ?? post.Title;
        if (request.Title is not null && title.Length == 0) ModelState.AddModelError("Title", "Title cannot be blank.");
        CheckLength("Title", title, BlogPost.MaxTitleLength);

        var excerpt = request.Excerpt?.Trim() ?? post.Excerpt;
        var body = request.BodyMarkdown ?? post.BodyMarkdown;
        var text = ValidateText(excerpt, body,
            request.CoverImageUrl ?? post.CoverImageUrl,
            request.CoverImageAlt ?? post.CoverImageAlt,
            request.AuthorName ?? post.AuthorName,
            request.MetaTitle ?? post.MetaTitle,
            request.MetaDescription ?? post.MetaDescription,
            request.CanonicalUrl ?? post.CanonicalUrl);

        var status = post.Status;
        if (request.Status is not null && !TryParseStatus(request.Status, out status))
            ModelState.AddModelError("Status", "Status must be Draft, Scheduled or Published.");

        var requestedAt = request.PublishedAt?.UtcDateTime
                          ?? (post.Status == BlogPostStatus.Draft ? null : post.PublishedAt);
        var (finalStatus, publishedAt) = ResolvePublication(status, requestedAt, now);
        if (finalStatus != BlogPostStatus.Draft) RequirePublishable(excerpt, body);

        var cityChanged = request.ClearCity == true || request.CityId is not null;
        var countryChanged = request.ClearCountry == true || request.CountryId is not null;
        Guid? cityId = post.CityId, countryId = post.CountryId;
        if (cityChanged || countryChanged)
        {
            var newCity = request.ClearCity == true ? null : request.CityId ?? post.CityId;
            // A new city without an explicit country moves the post to the city's country.
            var newCountry = request.ClearCountry == true ? null
                : request.CountryId ?? (request.CityId is not null ? null : post.CountryId);
            (cityId, countryId) = await ValidatePlaceAsync(newCity, newCountry, ct);
        }

        var tagIds = request.TagKeys is null ? null : await ValidateTagsAsync(request.TagKeys, ct);
        var destinationIds = request.DestinationIds is null ? null : await ValidateDestinationsAsync(request.DestinationIds, ct);
        var wishlistIds = request.WishlistIds is null ? null : await ValidateWishlistsAsync(request.WishlistIds, ct);
        var eventIds = request.EventIds is null ? null : await ValidateEventsAsync(request.EventIds, ct);

        string? newSlug = null;
        if (request.Slug is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Slug))
                ModelState.AddModelError("Slug", "Slug cannot be blank.");
            else if (request.Slug.Trim() != post.Slug)
                newSlug = await ValidateExplicitSlugAsync(request.Slug, post.Id, ct);
        }

        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        post.Title = title;
        post.Excerpt = excerpt;
        post.BodyMarkdown = body;
        post.CoverImageUrl = text.CoverImageUrl;
        post.CoverImageAlt = text.CoverImageAlt;
        post.AuthorName = text.AuthorName;
        post.MetaTitle = text.MetaTitle;
        post.MetaDescription = text.MetaDescription;
        post.CanonicalUrl = text.CanonicalUrl;
        post.Status = finalStatus;
        post.PublishedAt = publishedAt;
        post.ReadingTimeMinutes = BlogQueries.ReadingTimeMinutes(body);
        post.CityId = cityId;
        post.CountryId = countryId;
        post.UpdatedAt = now;
        ReplaceLinks(post, tagIds, destinationIds, wishlistIds, eventIds);

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            if (newSlug is not null)
            {
                var oldSlug = post.Slug;
                // The new slug may be one of this (or another) post's retired slugs: it is live again.
                await _db.BlogPostSlugHistory.Where(h => h.OldSlug == newSlug).ExecuteDeleteAsync(ct);
                _db.BlogPostSlugHistory.Add(new BlogPostSlugHistory
                {
                    Id = Guid.NewGuid(),
                    OldSlug = oldSlug,
                    BlogPostId = post.Id,
                    CreatedAt = now,
                });
                post.Slug = newSlug;
                _logger.LogInformation("Admin changed blog post {PostId} slug {OldSlug} -> {NewSlug}", post.Id, oldSlug, newSlug);
            }

            if (!await TrySaveAsync(ct)) return ValidationProblem(ModelState);
            await tx.CommitAsync(ct);
        }

        return Ok(await LoadDto(id, ct));
    }

    /// <summary>
    /// Publishes now, or schedules when a future publishedAt is sent (a past one backdates).
    /// Requires an excerpt and a body. Returns the updated post.
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<AdminBlogPostDto>> Publish(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PublishBlogPostRequest? request,
        CancellationToken ct = default)
    {
        var post = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound();

        RequirePublishable(post.Excerpt, post.BodyMarkdown);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var now = DateTime.UtcNow;
        var (status, publishedAt) = ResolvePublication(BlogPostStatus.Published, request?.PublishedAt?.UtcDateTime, now);
        post.Status = status;
        post.PublishedAt = publishedAt;
        post.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin published blog post {PostId} as {Status} at {PublishedAt}", id, status, publishedAt);
        return Ok(await LoadDto(id, ct));
    }

    /// <summary>Back to Draft (PublishedAt cleared). Returns the updated post.</summary>
    [HttpPost("{id:guid}/unpublish")]
    public async Task<ActionResult<AdminBlogPostDto>> Unpublish(Guid id, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var updated = await _db.BlogPosts
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, BlogPostStatus.Draft)
                .SetProperty(p => p.PublishedAt, (DateTime?)null)
                .SetProperty(p => p.UpdatedAt, now), ct);
        if (updated == 0) return NotFound();

        _logger.LogInformation("Admin unpublished blog post {PostId}", id);
        return Ok(await LoadDto(id, ct));
    }

    /// <summary>Deletes the post with its links and slug history.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePost(Guid id, CancellationToken ct = default)
    {
        var deleted = await _db.BlogPosts.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) return NotFound();

        _logger.LogInformation("Admin deleted blog post {PostId}", id);
        return NoContent();
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    private async Task<AdminBlogPostDto?> LoadDto(Guid id, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var p = await _db.BlogPosts.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Title, x.Slug, x.Excerpt, x.BodyMarkdown, x.CoverImageUrl, x.CoverImageAlt, x.AuthorName,
                x.CreatedByUserId, x.Status, x.PublishedAt, x.CreatedAt, x.UpdatedAt,
                x.MetaTitle, x.MetaDescription, x.CanonicalUrl, x.ReadingTimeMinutes,
                City = x.City != null ? new BlogPlaceDto(x.City.CityId, x.City.CityName) : null,
                Country = x.Country != null ? new BlogPlaceDto(x.Country.CountryId, x.Country.CountryName) : null,
                Tags = x.Tags.OrderBy(t => t.Tag.TagName).Select(t => new BlogTagDto(t.Tag.Key, t.Tag.TagName)).ToList(),
                DestinationIds = x.Destinations.OrderBy(d => d.SortOrder).Select(d => d.DestinationId).ToList(),
                WishlistIds = x.Wishlists.OrderBy(w => w.SortOrder).Select(w => w.WishlistId).ToList(),
                EventIds = x.Events.OrderBy(e => e.SortOrder).Select(e => e.EventId).ToList(),
                PreviousSlugs = x.SlugHistory.OrderByDescending(h => h.CreatedAt).Select(h => h.OldSlug).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (p is null) return null;

        return new AdminBlogPostDto(
            p.Id, p.Title, p.Slug, p.Excerpt, p.BodyMarkdown, p.CoverImageUrl, p.CoverImageAlt, p.AuthorName,
            p.CreatedByUserId,
            Status: BlogQueries.EffectiveStatus(p.Status, p.PublishedAt, now).ToString(),
            IsVisible: BlogQueries.IsVisible(p.Status, p.PublishedAt, now),
            p.PublishedAt, p.CreatedAt, p.UpdatedAt,
            p.MetaTitle, p.MetaDescription, p.CanonicalUrl, p.ReadingTimeMinutes,
            p.City, p.Country, p.Tags,
            TagKeys: p.Tags.Select(t => t.Key).ToList(),
            p.DestinationIds, p.WishlistIds, p.EventIds,
            Destinations: await BlogQueries.LinkedDestinationsAsync(_db, id, ct),
            Wishlists: await BlogQueries.LinkedWishlistsAsync(_db, id, ct),
            Events: await BlogQueries.LinkedEventsAsync(_db, id, publishedOnly: false, ct),
            Embeds: await BlogQueries.ResolveEmbedsAsync(_db, p.BodyMarkdown, ct),
            PreviousSlugs: p.PreviousSlugs);
    }

    // ── Publication rules ─────────────────────────────────────────────────────

    /// <summary>
    /// Draft → (Draft, null). Published/Scheduled → PublishedAt defaults to now for Published
    /// (Scheduled needs one); the status then follows the time: future = Scheduled, else Published.
    /// </summary>
    private (BlogPostStatus Status, DateTime? PublishedAt) ResolvePublication(BlogPostStatus status, DateTime? publishedAt, DateTime now)
    {
        if (status == BlogPostStatus.Draft) return (BlogPostStatus.Draft, null);

        if (publishedAt is null)
        {
            if (status == BlogPostStatus.Scheduled)
            {
                ModelState.AddModelError("PublishedAt", "PublishedAt is required to schedule a post.");
                return (status, null);
            }
            publishedAt = now;
        }

        return (publishedAt > now ? BlogPostStatus.Scheduled : BlogPostStatus.Published, publishedAt);
    }

    private void RequirePublishable(string excerpt, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            ModelState.AddModelError("BodyMarkdown", "BodyMarkdown is required to publish.");
        if (string.IsNullOrWhiteSpace(excerpt))
            ModelState.AddModelError("Excerpt", "Excerpt is required to publish.");
    }

    // ── Field validation ──────────────────────────────────────────────────────

    private record TextFields(
        string? CoverImageUrl, string? CoverImageAlt, string? AuthorName,
        string? MetaTitle, string? MetaDescription, string? CanonicalUrl);

    /// <summary>Lengths, URLs and the &lt;script check. Optional text: blank → null.</summary>
    private TextFields ValidateText(
        string excerpt, string body, string? coverImageUrl, string? coverImageAlt, string? authorName,
        string? metaTitle, string? metaDescription, string? canonicalUrl)
    {
        CheckLength("Excerpt", excerpt, BlogPost.MaxExcerptLength);
        if (body.Contains("<script", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError("BodyMarkdown", "BodyMarkdown must not contain <script> tags.");

        var fields = new TextFields(
            ValidateUrl("CoverImageUrl", coverImageUrl),
            NullIfBlank(coverImageAlt),
            NullIfBlank(authorName),
            NullIfBlank(metaTitle),
            NullIfBlank(metaDescription),
            ValidateUrl("CanonicalUrl", canonicalUrl));

        CheckLength("CoverImageAlt", fields.CoverImageAlt, BlogPost.MaxCoverImageAltLength);
        CheckLength("AuthorName", fields.AuthorName, BlogPost.MaxAuthorNameLength);
        CheckLength("MetaTitle", fields.MetaTitle, BlogPost.MaxMetaTitleLength);
        CheckLength("MetaDescription", fields.MetaDescription, BlogPost.MaxMetaDescriptionLength);
        return fields;
    }

    private void CheckLength(string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
            ModelState.AddModelError(field, $"{field} must be at most {max} characters.");
    }

    /// <summary>Trimmed URL, null for blank input; adds a model error unless absolute http(s) and ≤ 500 chars.</summary>
    private string? ValidateUrl(string field, string? value)
    {
        var url = NullIfBlank(value);
        if (url is null) return null;
        if (!IsHttpUrl(url))
            ModelState.AddModelError(field, $"{field} must be an absolute http(s) URL.");
        else
            CheckLength(field, url, BlogPost.MaxUrlLength);
        return url;
    }

    /// <summary>Format, reserved words and uniqueness against other live posts. Returns the trimmed slug.</summary>
    private async Task<string?> ValidateExplicitSlugAsync(string raw, Guid? postId, CancellationToken ct)
    {
        var slug = raw.Trim();
        if (!BlogQueries.IsValidSlug(slug))
        {
            ModelState.AddModelError("Slug",
                $"Slug must be lowercase letters and digits separated by single hyphens (e.g. \"lagos-after-dark\"), at most {BlogPost.MaxSlugLength} characters.");
            return null;
        }
        if (BlogQueries.ReservedSlugs.Contains(slug))
        {
            ModelState.AddModelError("Slug", $"\"{slug}\" is reserved.");
            return null;
        }
        if (await _db.BlogPosts.AnyAsync(p => p.Slug == slug && p.Id != postId, ct))
        {
            ModelState.AddModelError("Slug", "Slug is already used by another post.");
            return null;
        }
        return slug;
    }

    /// <summary>Both must exist; the country defaults to the city's; a city in another country is rejected.</summary>
    private async Task<(Guid? CityId, Guid? CountryId)> ValidatePlaceAsync(Guid? cityId, Guid? countryId, CancellationToken ct)
    {
        Guid? cityCountryId = null;
        if (cityId is not null)
        {
            cityCountryId = await _db.Cities.Where(c => c.CityId == cityId).Select(c => (Guid?)c.CountryId).FirstOrDefaultAsync(ct);
            if (cityCountryId is null) ModelState.AddModelError("CityId", "City not found.");
        }

        if (countryId is not null)
        {
            if (!await _db.Countries.AnyAsync(c => c.CountryId == countryId, ct))
                ModelState.AddModelError("CountryId", "Country not found.");
            else if (cityCountryId is not null && cityCountryId != countryId)
                ModelState.AddModelError("CityId", "The city is not in the given country.");
            return (cityId, countryId);
        }

        return (cityId, cityCountryId);
    }

    private async Task<List<Guid>> ValidateTagsAsync(List<string>? keys, CancellationToken ct)
    {
        var wanted = (keys ?? new List<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (wanted.Count == 0) return new List<Guid>();

        var found = await _db.Tags.Where(t => wanted.Contains(t.Key)).Select(t => new { t.Key, t.TagId }).ToListAsync(ct);
        var missing = wanted.Except(found.Select(f => f.Key), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            ModelState.AddModelError("TagKeys", $"Unknown tag keys: {string.Join(", ", missing)}.");
        return found.Select(f => f.TagId).ToList();
    }

    private async Task<List<Guid>> ValidateDestinationsAsync(List<Guid>? ids, CancellationToken ct)
    {
        var wanted = Distinct(ids);
        if (wanted.Count == 0) return wanted;
        var found = await _db.Destinations.Where(d => wanted.Contains(d.DestinationId)).Select(d => d.DestinationId).ToListAsync(ct);
        ReportMissing("DestinationIds", "destinations", wanted, found);
        return wanted;
    }

    private async Task<List<Guid>> ValidateWishlistsAsync(List<Guid>? ids, CancellationToken ct)
    {
        var wanted = Distinct(ids);
        if (wanted.Count == 0) return wanted;
        var found = await _db.Wishlists.Where(w => wanted.Contains(w.WishlistId) && w.IsTemplate).Select(w => w.WishlistId).ToListAsync(ct);
        ReportMissing("WishlistIds", "template wishlists", wanted, found);
        return wanted;
    }

    private async Task<List<Guid>> ValidateEventsAsync(List<Guid>? ids, CancellationToken ct)
    {
        var wanted = Distinct(ids);
        if (wanted.Count == 0) return wanted;
        var found = await _db.Events.Where(e => wanted.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
        ReportMissing("EventIds", "events", wanted, found);
        return wanted;
    }

    private void ReportMissing(string field, string what, List<Guid> wanted, List<Guid> found)
    {
        var missing = wanted.Except(found).ToList();
        if (missing.Count > 0)
            ModelState.AddModelError(field, $"Not found (or not {what}): {string.Join(", ", missing)}.");
    }

    /// <summary>Distinct ids, first occurrence order kept (the order is the display order).</summary>
    private static List<Guid> Distinct(List<Guid>? ids)
        => (ids ?? new List<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();

    // ── Links ─────────────────────────────────────────────────────────────────

    /// <summary>Sets each link set that is non-null to exactly the given ids (SortOrder = position).</summary>
    private static void ReplaceLinks(BlogPost post, List<Guid>? tagIds, List<Guid>? destinationIds, List<Guid>? wishlistIds, List<Guid>? eventIds)
    {
        if (tagIds is not null)
        {
            foreach (var link in post.Tags.Where(t => !tagIds.Contains(t.TagId)).ToList()) post.Tags.Remove(link);
            foreach (var tagId in tagIds.Where(id => post.Tags.All(t => t.TagId != id)))
                post.Tags.Add(new BlogPostTag { BlogPostId = post.Id, TagId = tagId });
        }

        if (destinationIds is not null)
            Sync(post.Destinations, destinationIds, l => l.DestinationId,
                (id, i) => new BlogPostDestination { BlogPostId = post.Id, DestinationId = id, SortOrder = i },
                (l, i) => l.SortOrder = i);

        if (wishlistIds is not null)
            Sync(post.Wishlists, wishlistIds, l => l.WishlistId,
                (id, i) => new BlogPostWishlist { BlogPostId = post.Id, WishlistId = id, SortOrder = i },
                (l, i) => l.SortOrder = i);

        if (eventIds is not null)
            Sync(post.Events, eventIds, l => l.EventId,
                (id, i) => new BlogPostEvent { BlogPostId = post.Id, EventId = id, SortOrder = i },
                (l, i) => l.SortOrder = i);
    }

    private static void Sync<T>(ICollection<T> links, List<Guid> ids, Func<T, Guid> key, Func<Guid, int, T> create, Action<T, int> setOrder)
    {
        foreach (var link in links.Where(l => !ids.Contains(key(l))).ToList()) links.Remove(link);
        for (var i = 0; i < ids.Count; i++)
        {
            var existing = links.FirstOrDefault(l => key(l) == ids[i]);
            if (existing is null) links.Add(create(ids[i], i));
            else setOrder(existing, i);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Saves; a unique-slug race becomes a Slug model error (returns false).</summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            ModelState.AddModelError("Slug", "Slug is already used by another post.");
            return false;
        }
    }

    private static bool TryParseStatus(string value, out BlogPostStatus status)
        => Enum.TryParse(value.Trim(), ignoreCase: true, out status)
           && Enum.IsDefined(status)
           && !int.TryParse(value.Trim(), out _);

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
