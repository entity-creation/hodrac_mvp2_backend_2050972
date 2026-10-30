using Hodrac_Backend_MVP2.Controllers;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.WishlistDtos;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Hodrac_Backend_MVP2.Services
{
    public record AdminHeroDto(
        Guid WishlistId,
        string WishlistName,
        string WishlistHeroImage,
        DateTimeOffset? HeroUntil,
        DateTimeOffset SetAt,
        string? SetByEmail,
        bool IsActive);

    public interface IHeroWishlistService
    {
        /// <summary>The active hero card (partner NOT attached), or null when none applies.</summary>
        Task<WishlistCardDto?> GetActiveCardAsync(CancellationToken ct = default);

        Task<AdminHeroDto?> GetAdminAsync(CancellationToken ct = default);

        /// <summary>Replaces the hero. Returns field → message errors when invalid (nothing saved).</summary>
        Task<(AdminHeroDto? Hero, Dictionary<string, string> Errors)> SetAsync(
            Guid? wishlistId, DateTimeOffset? heroUntil, Guid setByUserId, CancellationToken ct = default);

        Task ClearAsync(CancellationToken ct = default);
    }

    // The public card is cached in memory for 60s and evicted whenever an admin
    // changes the hero. The partner block is attached per request by the caller
    // because its trackingUrl is personalised for logged-in users.
    public class HeroWishlistService : IHeroWishlistService
    {
        private const string CacheKey = "hero-wishlist-card";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private readonly HodracDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<HeroWishlistService> _logger;

        public HeroWishlistService(HodracDbContext db, IMemoryCache cache, ILogger<HeroWishlistService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        public async Task<WishlistCardDto?> GetActiveCardAsync(CancellationToken ct = default)
        {
            if (_cache.TryGetValue(CacheKey, out HeroCacheEntry? cached) && cached is not null)
                return cached.Card;

            var setting = await _db.HeroWishlistSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == HeroWishlistSetting.SingletonId, ct);

            WishlistCardDto? card = null;
            var ttl = CacheTtl;
            if (setting?.HeroWishlistId is Guid id && !IsExpired(setting.HeroUntil))
            {
                var wishlist = await _db.Wishlists.AsNoTracking()
                    .FirstOrDefaultAsync(w => w.WishlistId == id, ct);
                if (wishlist is not null && IsPublicTemplate(wishlist) && HasHeroImage(wishlist))
                    card = WishlistsController.MapToCard(wishlist);

                // Don't serve a hero past its end time just because it's cached.
                if (setting.HeroUntil is DateTimeOffset until && until - DateTimeOffset.UtcNow < ttl)
                    ttl = until - DateTimeOffset.UtcNow;
            }

            if (ttl > TimeSpan.Zero)
                _cache.Set(CacheKey, new HeroCacheEntry(card), ttl);
            return card;
        }

        public async Task<AdminHeroDto?> GetAdminAsync(CancellationToken ct = default)
        {
            var setting = await _db.HeroWishlistSettings.AsNoTracking()
                .Include(s => s.HeroWishlist)
                .FirstOrDefaultAsync(s => s.Id == HeroWishlistSetting.SingletonId, ct);
            if (setting?.HeroWishlistId is null || setting.HeroWishlist is null) return null;

            var setById = setting.SetByUserId.ToString();
            var email = await _db.Users.AsNoTracking()
                .Where(u => u.Id == setById)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);

            var w = setting.HeroWishlist;
            return new AdminHeroDto(
                w.WishlistId,
                w.WishlistName,
                w.WishlistHeroImage,
                setting.HeroUntil,
                setting.SetAt,
                email,
                IsActive: !IsExpired(setting.HeroUntil) && IsPublicTemplate(w) && HasHeroImage(w));
        }

        public async Task<(AdminHeroDto? Hero, Dictionary<string, string> Errors)> SetAsync(
            Guid? wishlistId, DateTimeOffset? heroUntil, Guid setByUserId, CancellationToken ct = default)
        {
            var errors = new Dictionary<string, string>();

            Wishlist? wishlist = null;
            if (wishlistId is null || wishlistId == Guid.Empty)
                errors["wishlistId"] = "wishlistId is required.";
            else
            {
                wishlist = await _db.Wishlists.AsNoTracking().FirstOrDefaultAsync(w => w.WishlistId == wishlistId, ct);
                if (wishlist is null)
                    errors["wishlistId"] = "Wishlist not found.";
                else if (!IsPublicTemplate(wishlist))
                    errors["wishlistId"] = "Only public template wishlists can be the hero.";
                else if (!HasHeroImage(wishlist))
                    errors["wishlistId"] = "The wishlist has no hero image.";
            }

            if (heroUntil is not null && heroUntil <= DateTimeOffset.UtcNow)
                errors["heroUntil"] = "heroUntil must be in the future.";

            if (errors.Count > 0) return (null, errors);

            var setting = await _db.HeroWishlistSettings.FirstOrDefaultAsync(s => s.Id == HeroWishlistSetting.SingletonId, ct);
            if (setting is null)
            {
                setting = new HeroWishlistSetting();
                _db.HeroWishlistSettings.Add(setting);
            }

            setting.HeroWishlistId = wishlist!.WishlistId;
            setting.HeroUntil = heroUntil?.ToUniversalTime();
            setting.SetByUserId = setByUserId;
            setting.SetAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _cache.Remove(CacheKey);
            _logger.LogInformation("Hero wishlist set to {WishlistId} until {HeroUntil} by {UserId}",
                wishlist.WishlistId, setting.HeroUntil?.ToString("o") ?? "(no end)", setByUserId);

            return (await GetAdminAsync(ct), errors);
        }

        public async Task ClearAsync(CancellationToken ct = default)
        {
            await _db.HeroWishlistSettings
                .Where(s => s.Id == HeroWishlistSetting.SingletonId)
                .ExecuteDeleteAsync(ct);
            _cache.Remove(CacheKey);
            _logger.LogInformation("Hero wishlist cleared");
        }

        private static bool IsExpired(DateTimeOffset? until) => until is not null && until <= DateTimeOffset.UtcNow;

        private static bool IsPublicTemplate(Wishlist w) => w.IsTemplate && w.OwnerUserId == null;

        private static bool HasHeroImage(Wishlist w) => !string.IsNullOrWhiteSpace(w.WishlistHeroImage);

        // Wrapper so "no hero" (null card) can be cached too.
        private sealed record HeroCacheEntry(WishlistCardDto? Card);
    }
}
