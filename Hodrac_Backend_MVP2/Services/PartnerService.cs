using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.DTOs.PartnerDtos;
using Hodrac_Backend_MVP2.DTOs.WishlistDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services
{
    // Attaches the winning partner placement (if any) to destination and
    // wishlist responses. One query per call regardless of list size.
    public interface IPartnerService
    {
        Task<List<DestinationSummaryDto>> AttachAsync(IEnumerable<DestinationSummaryDto> items, CancellationToken ct = default);
        Task<DestinationDetailDto> AttachAsync(DestinationDetailDto item, CancellationToken ct = default);
        Task<List<WishlistCardDto>> AttachAsync(IEnumerable<WishlistCardDto> items, CancellationToken ct = default);
        Task<WishlistDetailDto> AttachAsync(WishlistDetailDto item, CancellationToken ct = default);
    }

    public class PartnerService : IPartnerService
    {
        private readonly HodracDbContext _db;
        private readonly IHttpContextAccessor _httpContext;
        private readonly IConfiguration _config;

        public PartnerService(HodracDbContext db, IHttpContextAccessor httpContext, IConfiguration config)
        {
            _db = db;
            _httpContext = httpContext;
            _config = config;
        }

        public async Task<List<DestinationSummaryDto>> AttachAsync(IEnumerable<DestinationSummaryDto> items, CancellationToken ct = default)
        {
            var list = items.ToList();
            var partners = await ForDestinationsAsync(list.Select(d => d.DestinationId), ct);
            return list.Select(d => d with { Partner = partners.GetValueOrDefault(d.DestinationId) }).ToList();
        }

        public async Task<DestinationDetailDto> AttachAsync(DestinationDetailDto item, CancellationToken ct = default)
        {
            var partners = await ForDestinationsAsync(new[] { item.DestinationId }, ct);
            return item with { Partner = partners.GetValueOrDefault(item.DestinationId) };
        }

        public async Task<List<WishlistCardDto>> AttachAsync(IEnumerable<WishlistCardDto> items, CancellationToken ct = default)
        {
            var list = items.ToList();
            var partners = await ForWishlistsAsync(list.Select(w => w.WishlistId), ct);
            return list.Select(w => w with { Partner = partners.GetValueOrDefault(w.WishlistId) }).ToList();
        }

        public async Task<WishlistDetailDto> AttachAsync(WishlistDetailDto item, CancellationToken ct = default)
        {
            var partners = await ForWishlistsAsync(new[] { item.WishlistId }, ct);
            return item with { Partner = partners.GetValueOrDefault(item.WishlistId) };
        }

        private async Task<Dictionary<Guid, PartnerDto>> ForDestinationsAsync(IEnumerable<Guid> ids, CancellationToken ct)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return new();

            var links = await ActiveLinks()
                .Where(l => l.DestinationId != null && idList.Contains(l.DestinationId.Value))
                .ToListAsync(ct);

            return Pick(links, l => l.DestinationId!.Value);
        }

        private async Task<Dictionary<Guid, PartnerDto>> ForWishlistsAsync(IEnumerable<Guid> ids, CancellationToken ct)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return new();

            // A user's fork inherits its template's partner unless it has its own.
            var forkedFrom = await _db.Wishlists
                .AsNoTracking()
                .Where(w => idList.Contains(w.WishlistId) && w.ForkedFromId != null)
                .ToDictionaryAsync(w => w.WishlistId, w => w.ForkedFromId!.Value, ct);

            var lookupIds = idList.Concat(forkedFrom.Values).Distinct().ToList();

            var links = await ActiveLinks()
                .Where(l => l.WishlistId != null && lookupIds.Contains(l.WishlistId.Value))
                .ToListAsync(ct);

            var byWishlist = Pick(links, l => l.WishlistId!.Value);

            var result = new Dictionary<Guid, PartnerDto>();
            foreach (var id in idList)
            {
                if (byWishlist.TryGetValue(id, out var own))
                    result[id] = own;
                else if (forkedFrom.TryGetValue(id, out var templateId) && byWishlist.TryGetValue(templateId, out var inherited))
                    result[id] = inherited;
            }
            return result;
        }

        private IQueryable<PartnerLink> ActiveLinks()
        {
            var now = DateTime.UtcNow;
            return _db.PartnerLinks
                .AsNoTracking()
                .Include(l => l.Partner)
                .Where(l => l.IsActive && l.Partner.IsActive
                    && (l.StartsAt == null || l.StartsAt <= now)
                    && (l.EndsAt == null || l.EndsAt > now));
        }

        private Dictionary<Guid, PartnerDto> Pick(List<PartnerLink> links, Func<PartnerLink, Guid> key)
        {
            var baseUrl = TrackingBaseUrl();
            return links
                .GroupBy(key)
                .ToDictionary(
                    g => g.Key,
                    g => ToDto(g.OrderByDescending(l => l.Priority).ThenBy(l => l.CreatedAt).First(), baseUrl));
        }

        private static PartnerDto ToDto(PartnerLink link, string baseUrl) => new(
            PartnerId: link.PartnerId,
            Name: link.Partner.Name,
            LogoUrl: link.Partner.LogoUrl,
            BookingUrl: link.BookingUrl,
            TrackingUrl: $"{baseUrl}/api/out/{link.LinkId}",
            CtaLabel: link.CtaLabel ?? link.Partner.DefaultCtaLabel,
            IsSponsored: link.IsSponsored);

        // Prefer the configured public API URL (correct behind proxies/TLS
        // termination); fall back to the current request's host.
        private string TrackingBaseUrl()
        {
            var configured = _config["Api:PublicBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');

            var request = _httpContext.HttpContext?.Request;
            return request is null ? string.Empty : $"{request.Scheme}://{request.Host}{request.PathBase}";
        }
    }
}
