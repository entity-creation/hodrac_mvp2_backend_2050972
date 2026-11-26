using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastucture.Jobs;
using Hodrac_Backend_MVP2.Migrations;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hodrac_Backend_MVP2.Tests.Wishlists;

/// <summary>
/// Editorial featured-wishlist ordering (EditorialOrder + reorder endpoint), the
/// public featured section (Paid → all Editorial → Random) and FeaturedUntil expiry
/// (card label, Editorial exclusion, daily cleanup, unfeature).
/// Every test starts from an empty featured pool (only these tests use it).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class FeaturedWishlistsTests
{
    private readonly HodracApiFactory _factory;

    public FeaturedWishlistsTests(HodracApiFactory factory) => _factory = factory;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    private Task ResetPoolAsync()
        => WithDbAsync(db => db.FeaturedWishlistPool.ExecuteDeleteAsync());

    private async Task<HttpClient> AdminAsync()
    {
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        return _factory.CreateClient().Authenticated(admin);
    }

    private Task<Guid> TemplateAsync(long saves = 0, bool isFeatured = false, DateTimeOffset? featuredUntil = null)
        => WithDbAsync(async db =>
        {
            var w = new Wishlist
            {
                WishlistId = Guid.NewGuid(),
                WishlistName = $"Featured test {Guid.NewGuid():N}",
                TotalDays = 3,
                IsTemplate = true,
                TotalGlobalSaveCount = saves,
                IsFeatured = isFeatured,
                FeaturedUntil = featuredUntil,
            };
            db.Wishlists.Add(w);
            await db.SaveChangesAsync();
            return w.WishlistId;
        });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    /// <summary>POST /api/admin/featured-pool → the created entry.</summary>
    private static async Task<JsonElement> AddToPoolAsync(HttpClient admin, Guid wishlistId, string poolType,
        int dailyImpressionLimit = 0, decimal paidAmount = 0)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/featured-pool", new
        {
            wishlistId,
            poolType,
            paidAmount,
            dailyImpressionLimit,
            randomSelectionWeight = 1.0,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static Guid EntryId(JsonElement entry) => entry.GetProperty("featuredWishlistPoolId").GetGuid();

    private static int? EditorialOrder(JsonElement entry)
        => entry.GetProperty("editorialOrder").ValueKind == JsonValueKind.Null
            ? null
            : entry.GetProperty("editorialOrder").GetInt32();

    private async Task<List<JsonElement>> FeaturedCardsAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/api/wishlists/featured");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).EnumerateArray().ToList();
    }

    private static List<Guid> Ids(IEnumerable<JsonElement> cards)
        => cards.Select(c => c.GetProperty("wishlistId").GetGuid()).ToList();

    private Task<Dictionary<Guid, int?>> EditorialOrdersAsync()
        => WithDbAsync(db => db.FeaturedWishlistPool.AsNoTracking()
            .Where(p => p.PoolType == "Editorial")
            .ToDictionaryAsync(p => p.WishlistId, p => p.EditorialOrder));

    private static Task<HttpResponseMessage> ReorderAsync(HttpClient client, IEnumerable<Guid> entryIds)
        => client.PutAsJsonAsync("/api/admin/featured-pool/editorial/order", new { entryIds });

    // ── Featured section ────────────────────────────────────────────────────

    [Fact]
    public async Task Featured_returns_every_editorial_entry_in_editorial_order_between_paid_and_random()
    {
        await ResetPoolAsync();
        var admin = await AdminAsync();

        // Five Editorial entries (more than the old cap of 3), saves deliberately not matching the order.
        var editorial = new List<Guid>();
        var entries = new List<JsonElement>();
        for (var i = 0; i < 5; i++)
        {
            var id = await TemplateAsync(saves: i * 10);
            editorial.Add(id);
            entries.Add(await AddToPoolAsync(admin, id, "Editorial"));
        }
        entries.Select(EditorialOrder).Should().Equal(1, 2, 3, 4, 5);

        var paid = await TemplateAsync();
        await AddToPoolAsync(admin, paid, "Paid", dailyImpressionLimit: 100, paidAmount: 50);
        var random = await TemplateAsync();
        await AddToPoolAsync(admin, random, "Random");

        // Reverse the curated order.
        var reversed = entries.Select(EntryId).Reverse().ToList();
        (await ReorderAsync(admin, reversed)).StatusCode.Should().Be(HttpStatusCode.OK);

        var ids = Ids(await FeaturedCardsAsync());
        var expected = new List<Guid> { paid };
        expected.AddRange(Enumerable.Reverse(editorial));
        expected.Add(random);
        ids.Should().Equal(expected);
    }

    [Fact]
    public async Task Editorial_entries_without_an_order_come_last_by_saves()
    {
        await ResetPoolAsync();
        var admin = await AdminAsync();

        var ordered = await TemplateAsync(saves: 1);
        await AddToPoolAsync(admin, ordered, "Editorial");
        var unorderedLow = await TemplateAsync(saves: 5);
        var unorderedHigh = await TemplateAsync(saves: 500);
        await AddToPoolAsync(admin, unorderedLow, "Editorial");
        await AddToPoolAsync(admin, unorderedHigh, "Editorial");
        await WithDbAsync(db => db.FeaturedWishlistPool
            .Where(p => p.WishlistId == unorderedLow || p.WishlistId == unorderedHigh)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.EditorialOrder, (int?)null)));

        Ids(await FeaturedCardsAsync()).Should().Equal(ordered, unorderedHigh, unorderedLow);
    }

    // ── Admin pool: order bookkeeping ───────────────────────────────────────

    [Fact]
    public async Task New_editorial_entries_go_last_and_leaving_editorial_clears_and_renumbers()
    {
        await ResetPoolAsync();
        var admin = await AdminAsync();

        var a = await TemplateAsync();
        var b = await TemplateAsync();
        var c = await TemplateAsync();
        var eA = await AddToPoolAsync(admin, a, "Editorial");
        var eB = await AddToPoolAsync(admin, b, "Editorial");
        EditorialOrder(eA).Should().Be(1);
        EditorialOrder(eB).Should().Be(2);

        // Random entry: no order. PATCH it into Editorial → joins at the end.
        var eC = await AddToPoolAsync(admin, c, "Random");
        EditorialOrder(eC).Should().BeNull();
        var toEditorial = await admin.PatchAsJsonAsync($"/api/admin/featured-pool/{EntryId(eC)}", new { poolType = "Editorial" });
        toEditorial.StatusCode.Should().Be(HttpStatusCode.OK, await toEditorial.Content.ReadAsStringAsync());
        EditorialOrder(await JsonAsync(toEditorial)).Should().Be(3);

        // Move A out of Editorial → order cleared, the rest close the gap.
        var toRandom = await admin.PatchAsJsonAsync($"/api/admin/featured-pool/{EntryId(eA)}", new { poolType = "Random", randomSelectionWeight = 1.0 });
        toRandom.StatusCode.Should().Be(HttpStatusCode.OK, await toRandom.Content.ReadAsStringAsync());
        EditorialOrder(await JsonAsync(toRandom)).Should().BeNull();

        var orders = await EditorialOrdersAsync();
        orders.Should().BeEquivalentTo(new Dictionary<Guid, int?> { [b] = 1, [c] = 2 });

        // Admin GET lists Editorial rows in EditorialOrder and exposes editorialOrder.
        var list = await JsonAsync(await admin.GetAsync("/api/admin/featured-pool?poolType=Editorial"));
        list.EnumerateArray().Select(e => e.GetProperty("wishlistId").GetGuid()).Should().Equal(b, c);
        list.EnumerateArray().Select(EditorialOrder).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Deleting_an_editorial_entry_renumbers_the_rest()
    {
        await ResetPoolAsync();
        var admin = await AdminAsync();

        var a = await TemplateAsync();
        var b = await TemplateAsync();
        var c = await TemplateAsync();
        await AddToPoolAsync(admin, a, "Editorial");
        var eB = await AddToPoolAsync(admin, b, "Editorial");
        await AddToPoolAsync(admin, c, "Editorial");

        (await admin.DeleteAsync($"/api/admin/featured-pool/{EntryId(eB)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await EditorialOrdersAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int?> { [a] = 1, [c] = 2 });
    }

    // ── Reorder endpoint ────────────────────────────────────────────────────

    [Fact]
    public async Task Reorder_sets_order_and_rejects_missing_extra_or_duplicate_ids()
    {
        await ResetPoolAsync();
        var admin = await AdminAsync();

        var a = await TemplateAsync();
        var b = await TemplateAsync();
        var c = await TemplateAsync();
        var eA = EntryId(await AddToPoolAsync(admin, a, "Editorial"));
        var eB = EntryId(await AddToPoolAsync(admin, b, "Editorial"));
        var eC = EntryId(await AddToPoolAsync(admin, c, "Editorial"));
        var randomEntry = EntryId(await AddToPoolAsync(admin, await TemplateAsync(), "Random"));

        // Valid: returns the Editorial entries in the new order.
        var ok = await ReorderAsync(admin, new[] { eC, eA, eB });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var body = (await JsonAsync(ok)).EnumerateArray().ToList();
        body.Select(EntryId).Should().Equal(eC, eA, eB);
        body.Select(EditorialOrder).Should().Equal(1, 2, 3);
        (await EditorialOrdersAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int?> { [c] = 1, [a] = 2, [b] = 3 });

        // Missing, extra (a non-Editorial entry / unknown id), duplicate → 400, nothing changes.
        foreach (var bad in new[]
                 {
                     new[] { eA, eB },
                     new[] { eA, eB, eC, randomEntry },
                     new[] { eA, eB, eC, Guid.NewGuid() },
                     new[] { eA, eB, eB },
                     new[] { eA, eA, eB, eC },
                 })
        {
            var response = await ReorderAsync(admin, bad);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("EntryIds");
        }
        (await EditorialOrdersAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int?> { [c] = 1, [a] = 2, [b] = 3 });
    }

    [Fact]
    public async Task Reorder_requires_admin()
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient());
        var response = await ReorderAsync(_factory.CreateClient().Authenticated(user), Array.Empty<Guid>());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var anonymous = await ReorderAsync(_factory.CreateClient(), Array.Empty<Guid>());
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── FeaturedUntil expiry ────────────────────────────────────────────────

    [Fact]
    public async Task Expired_featured_until_hides_the_label_and_drops_the_wishlist_from_editorial()
    {
        await ResetPoolAsync();
        var admin = await AdminAsync();

        var expired = await TemplateAsync(isFeatured: true, featuredUntil: DateTimeOffset.UtcNow.AddHours(-1));
        var live = await TemplateAsync(isFeatured: true, featuredUntil: DateTimeOffset.UtcNow.AddDays(7));
        var indefinite = await TemplateAsync(isFeatured: true);
        await AddToPoolAsync(admin, expired, "Editorial");
        await AddToPoolAsync(admin, live, "Editorial");
        await AddToPoolAsync(admin, indefinite, "Editorial");

        var cards = await FeaturedCardsAsync();
        Ids(cards).Should().Equal(live, indefinite);
        cards.Should().OnlyContain(c => c.GetProperty("isFeatured").GetBoolean());

        // Shown through the Random pool (unchanged behaviour), the expired wishlist's card says not featured.
        await ResetPoolAsync();
        await AddToPoolAsync(admin, expired, "Random");
        var randomCard = (await FeaturedCardsAsync()).Single();
        randomCard.GetProperty("wishlistId").GetGuid().Should().Be(expired);
        randomCard.GetProperty("isFeatured").GetBoolean().Should().BeFalse();

        // Admin sees the raw flag plus isCurrentlyFeatured.
        var detail = await JsonAsync(await admin.GetAsync($"/api/admin/wishlists/{expired}"));
        detail.GetProperty("isFeatured").GetBoolean().Should().BeTrue();
        detail.GetProperty("isCurrentlyFeatured").GetBoolean().Should().BeFalse();
        var liveDetail = await JsonAsync(await admin.GetAsync($"/api/admin/wishlists/{live}"));
        liveDetail.GetProperty("isCurrentlyFeatured").GetBoolean().Should().BeTrue();

        var summaries = await JsonAsync(await admin.GetAsync("/api/admin/wishlists?featured=true&pageSize=100&sort=newest"));
        var expiredRow = summaries.GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("wishlistId").GetGuid() == expired);
        expiredRow.GetProperty("isCurrentlyFeatured").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Cleanup_job_unfeatures_expired_wishlists_removes_editorial_entries_and_renumbers()
    {
        await ResetPoolAsync();
        var admin = await AdminAsync();

        var first = await TemplateAsync(isFeatured: true);
        var expired = await TemplateAsync(isFeatured: true, featuredUntil: DateTimeOffset.UtcNow.AddMinutes(-5));
        var third = await TemplateAsync(isFeatured: true, featuredUntil: DateTimeOffset.UtcNow.AddDays(1));
        await AddToPoolAsync(admin, first, "Editorial");
        await AddToPoolAsync(admin, expired, "Editorial");
        await AddToPoolAsync(admin, third, "Editorial");
        await AddToPoolAsync(admin, expired, "Random");   // non-Editorial entries are left alone

        (int Unfeatured, int Removed) result;
        using (var scope = _factory.Services.CreateScope())
            result = await scope.ServiceProvider.GetRequiredService<ImpressionResetJob>()
                .ExpireFeaturedWishlistsAsync(DateTimeOffset.UtcNow);

        result.Unfeatured.Should().BeGreaterThanOrEqualTo(1);
        result.Removed.Should().Be(1);

        var w = await WithDbAsync(db => db.Wishlists.AsNoTracking().SingleAsync(x => x.WishlistId == expired));
        w.IsFeatured.Should().BeFalse();
        w.FeaturedUntil.Should().BeNull();
        (await EditorialOrdersAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int?> { [first] = 1, [third] = 2 });
        (await WithDbAsync(db => db.FeaturedWishlistPool.CountAsync(p => p.WishlistId == expired && p.PoolType == "Random")))
            .Should().Be(1);

        // Idempotent: a second run changes nothing.
        using (var scope = _factory.Services.CreateScope())
            result = await scope.ServiceProvider.GetRequiredService<ImpressionResetJob>()
                .ExpireFeaturedWishlistsAsync(DateTimeOffset.UtcNow);
        result.Should().Be((0, 0));
        (await EditorialOrdersAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int?> { [first] = 1, [third] = 2 });

        // The full nightly run also works (and keeps resetting paid impressions).
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ImpressionResetJob>().RunAsync();
    }

    [Fact]
    public async Task Unfeature_removes_the_editorial_entry_renumbers_and_is_idempotent()
    {
        await ResetPoolAsync();
        var admin = await AdminAsync();

        var a = await TemplateAsync();
        var b = await TemplateAsync();
        var c = await TemplateAsync();
        foreach (var id in new[] { a, b, c })
        {
            (await admin.PostAsJsonAsync($"/api/admin/wishlists/{id}/feature", new { featuredUntil = (DateTimeOffset?)null }))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            await AddToPoolAsync(admin, id, "Editorial");
        }
        await AddToPoolAsync(admin, b, "Random");

        // Re-featuring keeps the existing Editorial entry (no duplicate, same order).
        (await admin.PostAsJsonAsync($"/api/admin/wishlists/{b}/feature", new { featuredUntil = DateTimeOffset.UtcNow.AddDays(3) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EditorialOrdersAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int?> { [a] = 1, [b] = 2, [c] = 3 });

        (await admin.PostAsync($"/api/admin/wishlists/{b}/unfeature", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await EditorialOrdersAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int?> { [a] = 1, [c] = 2 });
        (await WithDbAsync(db => db.FeaturedWishlistPool.CountAsync(p => p.WishlistId == b && p.PoolType == "Random")))
            .Should().Be(1);
        var w = await WithDbAsync(db => db.Wishlists.AsNoTracking().SingleAsync(x => x.WishlistId == b));
        w.IsFeatured.Should().BeFalse();

        // Again (entry already gone, e.g. the admin frontend deleted it first) → still 204.
        (await admin.PostAsync($"/api/admin/wishlists/{b}/unfeature", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/admin/wishlists/{Guid.NewGuid()}/unfeature", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Migration backfill ──────────────────────────────────────────────────

    [Fact]
    public async Task Migration_backfill_orders_existing_editorial_entries_by_saves_then_id()
    {
        await ResetPoolAsync();

        var low = await TemplateAsync(saves: 1);
        var high = await TemplateAsync(saves: 100);
        var tieA = await TemplateAsync(saves: 50);
        var tieB = await TemplateAsync(saves: 50);
        var random = await TemplateAsync(saves: 1000);

        var entryIds = new Dictionary<Guid, Guid>();
        await WithDbAsync(async db =>
        {
            foreach (var (id, pool) in new[] { (low, "Editorial"), (high, "Editorial"), (tieA, "Editorial"), (tieB, "Editorial"), (random, "Random") })
            {
                var entryId = Guid.NewGuid();
                entryIds[id] = entryId;
                db.FeaturedWishlistPool.Add(new FeaturedWishlistPool
                {
                    FeaturedWishlistPoolId = entryId,
                    WishlistId = id,
                    PoolType = pool,
                    RandomSelectionWeight = 1,
                    LastRotationDate = DateTimeOffset.UtcNow,
                    EditorialOrder = null,   // as on deploy, before the backfill
                });
            }
            return await db.SaveChangesAsync();
        });

        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(FeaturedEditorialOrder.BackfillEditorialOrderSql));

        // Postgres orders uuids bytewise, matching Guid order of the 'N' string form.
        var ties = new[] { tieA, tieB }.OrderBy(id => entryIds[id].ToString("N"), StringComparer.Ordinal).ToList();
        var orders = await WithDbAsync(db => db.FeaturedWishlistPool.AsNoTracking()
            .ToDictionaryAsync(p => p.WishlistId, p => p.EditorialOrder));
        orders.Should().BeEquivalentTo(new Dictionary<Guid, int?>
        {
            [high] = 1,
            [ties[0]] = 2,
            [ties[1]] = 3,
            [low] = 4,
            [random] = null,
        });

        // And the public section shows them in that same order — the old behaviour.
        Ids(await FeaturedCardsAsync()).Take(4).Should().Equal(high, ties[0], ties[1], low);
    }
}
