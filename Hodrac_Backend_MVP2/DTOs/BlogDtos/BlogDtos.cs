namespace Hodrac_Backend_MVP2.DTOs.BlogDtos
{
    // ── Shared pieces ─────────────────────────────────────────────────────────

    public record BlogTagDto(string Key, string Name);

    public record BlogPlaceDto(Guid Id, string Name);

    /// <summary>Destination card (linked destinations and {{destination:…}} embeds).</summary>
    public record BlogDestinationCardDto(
        Guid Id,
        string Name,
        string? City,
        string Country,
        string? HeroImageUrl,
        string? ShortDescription);

    /// <summary>Template wishlist card (linked wishlists and {{wishlist:…}} embeds).</summary>
    public record BlogWishlistCardDto(
        Guid Id,
        string Name,
        int TotalDays,
        decimal BasePricePerPerson,
        string? HeroImage);

    /// <summary>Event card (linked events and {{event:…}} embeds). Times are UTC.</summary>
    public record BlogEventCardDto(
        Guid Id,
        string Slug,
        string Name,
        DateTime StartsAt,
        DateTime? EndsAt,
        string TimeZone,
        string? VenueName,
        string City,
        bool IsFree,
        string? ImageUrl);

    /// <summary>
    /// One embed marker found in the body. Marker is the exact text to replace in the Markdown.
    /// Type is "destination" | "wishlist" | "event". When Found is false every card is null and the
    /// frontend should drop the marker. Exactly one of Destination/Wishlist/Event is set when found.
    /// </summary>
    public record BlogEmbedDto(
        string Marker,
        string Type,
        string Ref,
        bool Found,
        BlogDestinationCardDto? Destination,
        BlogWishlistCardDto? Wishlist,
        BlogEventCardDto? Event);

    // ── Public ────────────────────────────────────────────────────────────────

    public record BlogPostSummaryDto(
        Guid Id,
        string Title,
        string Slug,
        string Excerpt,
        string? CoverImageUrl,
        string? CoverImageAlt,
        string? AuthorName,
        DateTime? PublishedAt,
        DateTime UpdatedAt,
        int ReadingTimeMinutes,
        List<BlogTagDto> Tags,
        BlogPlaceDto? City,
        BlogPlaceDto? Country);

    /// <summary>GET /api/blog/{slug}. MetaTitle/MetaDescription already fall back to Title/Excerpt.</summary>
    public record BlogPostDto(
        Guid Id,
        string Title,
        string Slug,
        string Excerpt,
        string? CoverImageUrl,
        string? CoverImageAlt,
        string? AuthorName,
        DateTime? PublishedAt,
        DateTime UpdatedAt,
        int ReadingTimeMinutes,
        List<BlogTagDto> Tags,
        BlogPlaceDto? City,
        BlogPlaceDto? Country,
        string BodyMarkdown,
        string MetaTitle,
        string MetaDescription,
        string? CanonicalUrl,
        List<BlogDestinationCardDto> Destinations,
        List<BlogWishlistCardDto> Wishlists,
        List<BlogEventCardDto> Events,
        List<BlogEmbedDto> Embeds,
        List<BlogPostSummaryDto> RelatedPosts);

    /// <summary>Body of the 301 answer for a retired slug.</summary>
    public record BlogRedirectDto(string RedirectToSlug);

    public record BlogSitemapEntryDto(string Slug, DateTime UpdatedAt, DateTime? PublishedAt);

    public record BlogTagCountDto(string Key, string Name, int PostCount);

    // ── Admin ─────────────────────────────────────────────────────────────────

    /// <summary>Status is the effective one: a Scheduled post whose time has come reads "Published".</summary>
    public record AdminBlogPostSummaryDto(
        Guid Id,
        string Title,
        string Slug,
        string Excerpt,
        string? CoverImageUrl,
        string? CoverImageAlt,
        string? AuthorName,
        DateTime? PublishedAt,
        DateTime UpdatedAt,
        int ReadingTimeMinutes,
        List<BlogTagDto> Tags,
        BlogPlaceDto? City,
        BlogPlaceDto? Country,
        string Status,
        DateTime CreatedAt,
        bool IsVisible);

    /// <summary>
    /// GET /api/admin/blog/{id}: the full post whether visible or not (doubles as the draft preview).
    /// MetaTitle/MetaDescription are the stored values (null = falls back to Title/Excerpt).
    /// Linked cards include unpublished events; embeds resolve with the public rules.
    /// </summary>
    public record AdminBlogPostDto(
        Guid Id,
        string Title,
        string Slug,
        string Excerpt,
        string BodyMarkdown,
        string? CoverImageUrl,
        string? CoverImageAlt,
        string? AuthorName,
        Guid CreatedByUserId,
        string Status,
        bool IsVisible,
        DateTime? PublishedAt,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string? MetaTitle,
        string? MetaDescription,
        string? CanonicalUrl,
        int ReadingTimeMinutes,
        BlogPlaceDto? City,
        BlogPlaceDto? Country,
        List<BlogTagDto> Tags,
        List<string> TagKeys,
        List<Guid> DestinationIds,
        List<Guid> WishlistIds,
        List<Guid> EventIds,
        List<BlogDestinationCardDto> Destinations,
        List<BlogWishlistCardDto> Wishlists,
        List<BlogEventCardDto> Events,
        List<BlogEmbedDto> Embeds,
        List<string> PreviousSlugs);

    /// <summary>
    /// POST /api/admin/blog. Status: "Draft" (default) | "Scheduled" | "Published".
    /// Published without PublishedAt = now; a future PublishedAt makes it Scheduled.
    /// Arrays define the links and their order. CountryId defaults to the city's country.
    /// </summary>
    public record CreateBlogPostRequest(
        string? Title,
        string? Slug,
        string? Excerpt,
        string? BodyMarkdown,
        string? CoverImageUrl,
        string? CoverImageAlt,
        string? AuthorName,
        string? MetaTitle,
        string? MetaDescription,
        string? CanonicalUrl,
        Guid? CityId,
        Guid? CountryId,
        List<string>? TagKeys,
        List<Guid>? DestinationIds,
        List<Guid>? WishlistIds,
        List<Guid>? EventIds,
        string? Status,
        DateTimeOffset? PublishedAt);

    /// <summary>
    /// PATCH /api/admin/blog/{id}. Null = unchanged; "" clears an optional text field;
    /// arrays, when sent, replace the whole set (and order). ClearCity/ClearCountry reset those to null.
    /// A slug change records the old slug for redirects.
    /// </summary>
    public record UpdateBlogPostRequest(
        string? Title,
        string? Slug,
        string? Excerpt,
        string? BodyMarkdown,
        string? CoverImageUrl,
        string? CoverImageAlt,
        string? AuthorName,
        string? MetaTitle,
        string? MetaDescription,
        string? CanonicalUrl,
        Guid? CityId,
        bool? ClearCity,
        Guid? CountryId,
        bool? ClearCountry,
        List<string>? TagKeys,
        List<Guid>? DestinationIds,
        List<Guid>? WishlistIds,
        List<Guid>? EventIds,
        string? Status,
        DateTimeOffset? PublishedAt);

    /// <summary>POST /api/admin/blog/{id}/publish. Body optional; a future PublishedAt schedules the post.</summary>
    public record PublishBlogPostRequest(DateTimeOffset? PublishedAt);
}
