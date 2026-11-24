using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Jobs;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Newsletter;
using Hodrac_Backend_MVP2.Tests.Hosts;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Tests.Newsletter;

/// <summary>
/// Weekly newsletter: draft generation, rendering, the admin issue API,
/// scheduling, the sender job (audience, batching, retries), unsubscribe and
/// re-subscribe. Jobs run directly from a DI scope with a controlled clock.
/// Emails only go to the RecordingEmailSender fake.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NewsletterTests
{
    private const string FrontendBase = "https://www.hodrac.com";
    private const string ApiBase = "https://api.hodrac.com";

    private readonly HodracApiFactory _factory;

    public NewsletterTests(HodracApiFactory factory) => _factory = factory;

    private RecordingEmailSender Emails => _factory.Emails;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static int _fridaySeq;

    /// <summary>A Friday at least ten years ahead, never the same twice in a run (IssueDate is unique).</summary>
    private static DateOnly UniqueFriday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstFriday = today.AddDays(((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7);
        return firstFriday.AddDays(7 * (520 + Interlocked.Increment(ref _fridaySeq)));
    }

    private static DateTime Utc(DateOnly day, int hour, int minute = 0) =>
        DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Utc);

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    private Task WithDbAsync(Func<HodracDbContext, Task> action) => WithDbAsync(async db => { await action(db); return 0; });

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<(HttpClient Client, TestUser User)> AdminAsync()
    {
        var user = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        return (_factory.CreateClient().Authenticated(user), user);
    }

    private static NewsletterOptions Options(Action<NewsletterOptions>? configure = null)
    {
        var o = new NewsletterOptions();
        configure?.Invoke(o);
        return o;
    }

    private Task<NewsletterDraft> BuildDraftAsync(DateOnly issueDate, DateTime nowUtc) =>
        WithScopeAsync(sp => ActivatorUtilities
            .CreateInstance<NewsletterDraftService>(sp, Microsoft.Extensions.Options.Options.Create(Options(o => o.MaxItemsPerSection = 500)))
            .BuildAsync(issueDate, nowUtc));

    private Task<NewsletterSendRunResult> RunSenderAsync(DateTime nowUtc, int batchSize = 50, int maxAttempts = 3) =>
        WithScopeAsync(sp => ActivatorUtilities
            .CreateInstance<NewsletterSendJob>(sp, Microsoft.Extensions.Options.Options.Create(Options(o =>
            {
                o.BatchSize = batchSize;
                o.MaxAttempts = maxAttempts;
            })))
            .RunAsync(nowUtc));

    private Task<NewsletterIssue?> RunDraftJobAsync(DateTime nowUtc) =>
        WithScopeAsync(sp => sp.GetRequiredService<NewsletterDraftJob>().RunAsync(nowUtc));

    /// <summary>An issue inserted directly (any status), for sender and draft-window tests.</summary>
    private Task<NewsletterIssue> InsertIssueAsync(
        NewsletterIssueStatus status, DateTime scheduledFor, DateTime? sentAt = null, DateOnly? issueDate = null) =>
        WithDbAsync(async db =>
        {
            var issue = new NewsletterIssue
            {
                Id = Guid.NewGuid(),
                IssueDate = issueDate ?? UniqueFriday(),
                Subject = $"Test issue {Unique()}",
                PreviewText = "Preview",
                BodyMarkdown = "Hi,\n\n[Read this](https://www.hodrac.com/blog/x)\n\nThe Hodrac team",
                Status = status,
                ScheduledFor = scheduledFor,
                GeneratedAt = DateTime.UtcNow,
                SentAt = sentAt,
                SendStartedAt = sentAt,
            };
            db.NewsletterIssues.Add(issue);
            await db.SaveChangesAsync();
            return issue;
        });

    /// <summary>
    /// Takes every existing account and sign-up out of the audience, and stops
    /// any other issue that could be picked up, so a sender test sees only its
    /// own recipients. (Newsletter flags matter to no other test.)
    /// </summary>
    private Task IsolateAudienceAsync() => WithDbAsync(async db =>
    {
        var now = DateTime.UtcNow;
        await db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.NewsletterOptOut, true));
        await db.NewsLetterEmails.Where(n => n.UnsubscribedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.UnsubscribedAt, now));
        await db.NewsletterIssues
            .Where(i => i.Status == NewsletterIssueStatus.Approved || i.Status == NewsletterIssueStatus.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, NewsletterIssueStatus.Cancelled));
    });

    private async Task<TestUser> UserAsync(bool confirmed, string? email = null, bool optOut = false)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), "Reader", email);
        await WithDbAsync(db => db.Users.Where(u => u.Id == user.UserId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.EmailConfirmed, confirmed)
            .SetProperty(u => u.NewsletterOptOut, optOut)));
        return user;
    }

    private async Task SubscribeAsync(string email)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/newletter", new { userEmail = email });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private Task<List<NewsletterDelivery>> DeliveriesAsync(Guid issueId) =>
        WithDbAsync(db => db.NewsletterDeliveries.AsNoTracking().Where(d => d.IssueId == issueId).ToListAsync());

    private Task<NewsletterIssue> IssueAsync(Guid id) =>
        WithDbAsync(db => db.NewsletterIssues.AsNoTracking().FirstAsync(i => i.Id == id));

    private string TokenFor(string email) =>
        _factory.Services.GetRequiredService<NewsletterUnsubscribeTokens>().Create(email);

    private Task<Guid> CreateCityAsync() => WithDbAsync(async db =>
    {
        var tag = Unique();
        var country = new Country { CountryId = Guid.NewGuid(), CountryName = $"Newsland {tag}" };
        var city = new City { CityId = Guid.NewGuid(), CityName = $"Lagos {tag}", CountryId = country.CountryId };
        db.Countries.Add(country);
        db.Cities.Add(city);
        await db.SaveChangesAsync();
        return city.CityId;
    });

    private Task<BlogPost> CreatePostAsync(string title, BlogPostStatus status, DateTime? publishedAt, string excerpt) =>
        WithDbAsync(async db =>
        {
            var post = new BlogPost
            {
                Id = Guid.NewGuid(),
                Title = title,
                Slug = $"news-{Unique()}",
                Excerpt = excerpt,
                BodyMarkdown = "Body",
                Status = status,
                PublishedAt = publishedAt,
                CreatedByUserId = Guid.NewGuid(),
            };
            db.BlogPosts.Add(post);
            await db.SaveChangesAsync();
            return post;
        });

    private Task<Wishlist> CreateWishlistAsync(string name, bool isTemplate, DateTimeOffset createdAt) =>
        WithDbAsync(async db =>
        {
            var w = new Wishlist
            {
                WishlistId = Guid.NewGuid(),
                WishlistName = name,
                ShortStory = $"Story for {name}",
                TotalDays = 4,
                IsTemplate = isTemplate,
                OwnerUserId = isTemplate ? null : Guid.NewGuid(),
                CreatedAt = createdAt,
            };
            db.Wishlists.Add(w);
            await db.SaveChangesAsync();
            return w;
        });

    private Task<Event> CreateEventAsync(Guid cityId, string name, DateTime startsAt, bool published = true) =>
        WithDbAsync(async db =>
        {
            var e = new Event
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = $"event-{Unique()}",
                Description = $"About {name}.",
                StartsAt = startsAt,
                CityId = cityId,
                IsPublished = published,
            };
            db.Events.Add(e);
            await db.SaveChangesAsync();
            return e;
        });

    // ── Schedule ─────────────────────────────────────────────────────────────

    [Fact]
    public void Schedule_Friday_10_WAT_is_09_UTC_and_draft_is_Wednesday_08_UTC()
    {
        var o = new NewsletterOptions();
        var friday = new DateOnly(2026, 11, 27);

        NewsletterSchedule.SendAtUtc(o, friday).Should().Be(new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc));
        NewsletterSchedule.SendAtUtc(o, friday).Kind.Should().Be(DateTimeKind.Utc);
        NewsletterSchedule.DraftDueUtc(o, friday).Should().Be(new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc));

        // Before the send time on Friday it's this Friday; after it, next Friday.
        NewsletterSchedule.NextIssueDate(o, new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc)).Should().Be(friday);
        NewsletterSchedule.NextIssueDate(o, new DateTime(2026, 11, 27, 8, 59, 0, DateTimeKind.Utc)).Should().Be(friday);
        NewsletterSchedule.NextIssueDate(o, new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc)).Should().Be(friday.AddDays(7));

        var custom = new NewsletterOptions { SendDay = DayOfWeek.Monday, SendTimeWat = TimeSpan.FromHours(7), DraftDay = DayOfWeek.Saturday };
        NewsletterSchedule.SendAtUtc(custom, new DateOnly(2026, 11, 30)).Should().Be(new DateTime(2026, 11, 30, 6, 0, 0, DateTimeKind.Utc));
        NewsletterSchedule.DraftLeadDays(custom).Should().Be(2);
    }

    // ── Draft generation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Draft_includes_only_content_new_since_the_last_sent_issue_with_correct_links()
    {
        var now = DateTime.UtcNow;
        var lastFriday = UniqueFriday();
        var friday = UniqueFriday();
        await InsertIssueAsync(NewsletterIssueStatus.Sent, Utc(lastFriday, 9), sentAt: now.AddMinutes(-30), issueDate: lastFriday);

        var tag = Unique();
        var oldPost = await CreatePostAsync($"Old post {tag}", BlogPostStatus.Published, now.AddMinutes(-60), "Old excerpt");
        var newPost = await CreatePostAsync($"Calabar in December {tag}", BlogPostStatus.Published, now.AddMinutes(-5), "What to know before the carnival.");
        var draftPost = await CreatePostAsync($"Draft post {tag}", BlogPostStatus.Draft, null, "Not yet");
        var futurePost = await CreatePostAsync($"Scheduled post {tag}", BlogPostStatus.Scheduled, now.AddHours(2), "Later");

        var oldTrip = await CreateWishlistAsync($"Old trip {tag}", true, now.AddMinutes(-60));
        var newTrip = await CreateWishlistAsync($"Zanzibar week {tag}", true, now.AddMinutes(-2));
        var privateTrip = await CreateWishlistAsync($"Private trip {tag}", false, now.AddMinutes(-2));

        var cityId = await CreateCityAsync();
        var soonEvent = await CreateEventAsync(cityId, $"Afrochella {tag}", now.AddDays(10));
        var hiddenEvent = await CreateEventAsync(cityId, $"Hidden fest {tag}", now.AddDays(10), published: false);
        var farEvent = await CreateEventAsync(cityId, $"Far fest {tag}", now.AddDays(45));

        var draft = await BuildDraftAsync(friday, now);
        var body = draft.BodyMarkdown;

        draft.ContentSinceUtc.Should().BeCloseTo(now.AddMinutes(-30), TimeSpan.FromSeconds(1));
        body.Should().Contain($"[Calabar in December {tag}]({FrontendBase}/blog/{newPost.Slug})");
        body.Should().Contain("What to know before the carnival.");
        body.Should().Contain($"[Zanzibar week {tag}]({FrontendBase}/wishlists/{newTrip.WishlistId})");
        body.Should().Contain($"4 days. Story for Zanzibar week {tag}");
        body.Should().Contain($"[Afrochella {tag}]({FrontendBase}/events/{soonEvent.Slug})");
        body.Should().Contain("## New on the blog").And.Contain("## New trip ideas")
            .And.Contain("## Events in the next 30 days").And.Contain("## New to Hodrac?");

        body.Should().NotContain(oldPost.Title).And.NotContain(draftPost.Title).And.NotContain(futurePost.Title);
        body.Should().NotContain(oldTrip.WishlistName).And.NotContain(privateTrip.WishlistName);
        body.Should().NotContain(hiddenEvent.Name).And.NotContain(farEvent.Name);

        draft.Subject.Should().StartWith("New on Hodrac: ");
        draft.Subject.Length.Should().BeLessOrEqualTo(NewsletterIssue.MaxSubjectLength);
        draft.PreviewText.Length.Should().BeLessOrEqualTo(NewsletterIssue.MaxPreviewTextLength);
        draft.PreviewText.Should().Contain("new article").And.Contain("new trip idea").And.Contain("in the next 30 days");
    }

    [Fact]
    public async Task Draft_for_the_first_issue_uses_the_last_7_days()
    {
        // No Sent issue exists before this (far past) date, so the window is the last 7 days.
        var now = DateTime.UtcNow;
        var draft = await BuildDraftAsync(new DateOnly(1990, 1, 5), now);
        draft.ContentSinceUtc.Should().BeCloseTo(now.AddDays(-7), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Empty_week_says_so_and_leans_on_the_intro_and_upcoming_events()
    {
        // Far in the future so no other test's content falls in the window.
        var now = DateTime.UtcNow.AddDays(400);
        var lastFriday = UniqueFriday();
        var friday = UniqueFriday();
        await InsertIssueAsync(NewsletterIssueStatus.Sent, Utc(lastFriday, 9), sentAt: now.AddHours(-1), issueDate: lastFriday);

        var empty = await BuildDraftAsync(friday, now);
        empty.BlogPostCount.Should().Be(0);
        empty.WishlistCount.Should().Be(0);
        empty.EventCount.Should().Be(0);
        empty.BodyMarkdown.Should().Contain("Nothing new went up on Hodrac this week");
        empty.BodyMarkdown.Should().Contain("## New to Hodrac?");
        empty.BodyMarkdown.Should().Contain($"[hodrac.com]({FrontendBase})");
        empty.BodyMarkdown.Should().NotContain("## New on the blog").And.NotContain("## Events in the next");
        empty.Subject.Should().StartWith("Hodrac weekly, ");
        empty.PreviewText.Should().Be("How to start planning a trip on Hodrac.");

        var cityId = await CreateCityAsync();
        var ev = await CreateEventAsync(cityId, $"Lagos Jazz Night {Unique()}", now.AddDays(5));
        var withEvent = await BuildDraftAsync(friday, now);
        withEvent.BodyMarkdown.Should().Contain("Nothing new went up on Hodrac this week");
        withEvent.BodyMarkdown.Should().Contain($"[{ev.Name}]({FrontendBase}/events/{ev.Slug})");
        withEvent.Subject.Should().Be($"Coming up: {ev.Name}");
        withEvent.PreviewText.Should().Be("1 event in the next 30 days.");
    }

    [Fact]
    public async Task Template_wording_has_no_dashes_or_stock_phrases()
    {
        var now = DateTime.UtcNow.AddDays(800);
        var lastFriday = UniqueFriday();
        var friday = UniqueFriday();
        await InsertIssueAsync(NewsletterIssueStatus.Sent, Utc(lastFriday, 9), sentAt: now.AddHours(-1), issueDate: lastFriday);
        var tag = Unique();
        await CreatePostAsync($"Post {tag}", BlogPostStatus.Published, now.AddMinutes(-5), "Plain excerpt");
        await CreateWishlistAsync($"Trip {tag}", true, now.AddMinutes(-5));
        var cityId = await CreateCityAsync();
        await CreateEventAsync(cityId, $"Fest {tag}", now.AddDays(3));

        var draft = await BuildDraftAsync(friday, now);
        var rendered = NewsletterRenderer.RenderHtml(draft.Subject, draft.PreviewText, draft.BodyMarkdown, "https://x.test/u")
                       + NewsletterRenderer.RenderText(draft.BodyMarkdown, "https://x.test/u");

        foreach (var text in new[] { draft.Subject, draft.PreviewText, draft.BodyMarkdown, rendered,
                     NewsletterDraftService.DefaultIntroMarkdown, NewsletterRenderer.FooterReason })
        {
            text.Should().NotContain("—").And.NotContain("–").And.NotContain(" -- ");
            foreach (var tell in new[] { "delve", "vibrant", "stunning", "breathtaking", "nestled", "must-visit", "Let's dive", "I hope this helps" })
                text.Should().NotContainEquivalentOf(tell);
        }

        var introSentences = NewsletterDraftService.DefaultIntroMarkdown.Split(". ", StringSplitOptions.RemoveEmptyEntries).Length;
        introSentences.Should().BeInRange(2, 3);
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void Rendering_escapes_raw_html_and_drops_unsafe_links()
    {
        var md = "## Hello\n\n<script>alert(1)</script>\n\n[ok](https://www.hodrac.com/x) [bad](javascript:alert(1)) <javascript:alert(2)>\n\n![pic](https://img.example.com/a b.jpg)\n\n![img](https://cdn.hodrac.com/i.jpg)";
        var html = NewsletterRenderer.RenderBodyHtml(md);

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("href=\"https://www.hodrac.com/x\"");
        html.Should().NotContain("javascript:alert(1)\"").And.NotContain("href=\"javascript");
        html.Should().Contain("src=\"https://cdn.hodrac.com/i.jpg\""); // stored URL used as-is
        html.Should().Contain("style=\"");

        var text = NewsletterRenderer.RenderText("Read [the post](https://www.hodrac.com/blog/a) now. \\#1", "https://u.test/x");
        text.Should().Contain("Read the post (https://www.hodrac.com/blog/a) now. #1");
        text.Should().Contain("Unsubscribe: https://u.test/x").And.Contain(HodracContact.SupportEmail);
    }

    // ── Admin API ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_issue_lifecycle_generate_edit_approve_unapprove_test_cancel()
    {
        var (admin, adminUser) = await AdminAsync();
        var friday = UniqueFriday();

        // Generate (201), then regenerate while Draft (200).
        var created = await admin.PostAsJsonAsync("/api/admin/newsletter/issues/generate", new { issueDate = friday });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var issue = await created.ReadJsonAsync();
        var id = issue.GetProperty("id").GetGuid();
        issue.GetProperty("status").GetString().Should().Be("Draft");
        issue.GetProperty("issueDate").GetString().Should().Be(friday.ToString("yyyy-MM-dd"));
        issue.GetProperty("scheduledFor").GetDateTime().ToUniversalTime().Should().Be(Utc(friday, 9));
        issue.GetProperty("previewHtml").GetString().Should().Contain("Hodrac").And.Contain("Unsubscribe")
            .And.Contain(NewsletterRenderer.FooterReason.Replace("'", "&#39;")).And.Contain(HodracContact.SupportEmail);
        issue.GetProperty("bodyMarkdown").GetString().Should().Contain("## New to Hodrac?");

        var regenerated = await admin.PostAsJsonAsync("/api/admin/newsletter/issues/generate", new { issueDate = friday });
        regenerated.StatusCode.Should().Be(HttpStatusCode.OK, await regenerated.Content.ReadAsStringAsync());
        (await regenerated.ReadJsonAsync()).GetProperty("id").GetGuid().Should().Be(id);

        // Validation: not a Friday.
        var notFriday = await admin.PostAsJsonAsync("/api/admin/newsletter/issues/generate", new { issueDate = friday.AddDays(1) });
        notFriday.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await notFriday.ReadJsonAsync()).GetProperty("errors").TryGetProperty("IssueDate", out _).Should().BeTrue();

        // Get + list.
        var get = await admin.GetAsync($"/api/admin/newsletter/issues/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await get.ReadJsonAsync()).GetProperty("deliveries").GetProperty("pending").GetInt32().Should().Be(0);
        var list = await (await admin.GetAsync("/api/admin/newsletter/issues?status=draft&pageSize=100")).ReadJsonAsync();
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
        (await admin.GetAsync("/api/admin/newsletter/issues?status=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Edit.
        var patch = await admin.PatchAsJsonAsync($"/api/admin/newsletter/issues/{id}", new { subject = "Five trips for the long weekend", bodyMarkdown = "Hi,\n\nShort one this week.\n\nThe Hodrac team" });
        patch.StatusCode.Should().Be(HttpStatusCode.OK, await patch.Content.ReadAsStringAsync());
        (await patch.ReadJsonAsync()).GetProperty("subject").GetString().Should().Be("Five trips for the long weekend");
        var tooLong = await admin.PatchAsJsonAsync($"/api/admin/newsletter/issues/{id}", new { subject = new string('a', 151) });
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var empty = await admin.PatchAsJsonAsync($"/api/admin/newsletter/issues/{id}", new { bodyMarkdown = "  " });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Approve: default slot is Friday 10:00 WAT = 09:00 UTC.
        var approve = await admin.PostAsync($"/api/admin/newsletter/issues/{id}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        var approved = await approve.ReadJsonAsync();
        approved.GetProperty("status").GetString().Should().Be("Approved");
        approved.GetProperty("scheduledFor").GetDateTime().ToUniversalTime().Should().Be(Utc(friday, 9));
        approved.GetProperty("approvedByUserId").GetString().Should().Be(adminUser.UserId);

        // Regenerate is refused once approved.
        (await admin.PostAsJsonAsync("/api/admin/newsletter/issues/generate", new { issueDate = friday }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Editing an approved issue sends it back to Draft.
        var edit = await (await admin.PatchAsJsonAsync($"/api/admin/newsletter/issues/{id}", new { previewText = "Short one." })).ReadJsonAsync();
        edit.GetProperty("status").GetString().Should().Be("Draft");
        edit.GetProperty("approvedAt").ValueKind.Should().Be(JsonValueKind.Null);

        // Custom schedule must be in the future.
        var past = await admin.PostAsJsonAsync($"/api/admin/newsletter/issues/{id}/approve", new { scheduledFor = DateTime.UtcNow.AddHours(-1) });
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var customAt = new DateTime(2040, 5, 1, 12, 30, 0, DateTimeKind.Utc);
        var custom = await (await admin.PostAsJsonAsync($"/api/admin/newsletter/issues/{id}/approve", new { scheduledFor = customAt })).ReadJsonAsync();
        custom.GetProperty("scheduledFor").GetDateTime().ToUniversalTime().Should().Be(customAt);

        // Unapprove → Draft; unapprove again → 409.
        var unapprove = await admin.PostAsync($"/api/admin/newsletter/issues/{id}/unapprove", null);
        (await unapprove.ReadJsonAsync()).GetProperty("status").GetString().Should().Be("Draft");
        (await admin.PostAsync($"/api/admin/newsletter/issues/{id}/unapprove", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Test send goes only to the calling admin, with the unsubscribe headers.
        var before = Emails.SentHtml.Count;
        var test = await admin.PostAsync($"/api/admin/newsletter/issues/{id}/test", null);
        test.StatusCode.Should().Be(HttpStatusCode.OK, await test.Content.ReadAsStringAsync());
        (await test.ReadJsonAsync()).GetProperty("sentTo").GetString().Should().Be(HodracApiFactory.AdminEmail);
        var sent = Emails.SentHtml.Skip(before).ToList();
        sent.Should().ContainSingle();
        sent[0].To.Should().Be(HodracApiFactory.AdminEmail);
        sent[0].Subject.Should().Be("[Test] Five trips for the long weekend");
        sent[0].Headers!.Should().ContainKey("List-Unsubscribe");

        // Cancel; cancel again → 409; a never-sent cancelled issue can be regenerated back to Draft.
        var cancel = await admin.PostAsync($"/api/admin/newsletter/issues/{id}/cancel", null);
        (await cancel.ReadJsonAsync()).GetProperty("status").GetString().Should().Be("Cancelled");
        (await admin.PostAsync($"/api/admin/newsletter/issues/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/api/admin/newsletter/issues/{id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var revived = await admin.PostAsJsonAsync("/api/admin/newsletter/issues/generate", new { issueDate = friday });
        revived.StatusCode.Should().Be(HttpStatusCode.OK);
        (await revived.ReadJsonAsync()).GetProperty("status").GetString().Should().Be("Draft");

        // Send-now approves with ScheduledFor = now; then cancel so no sender test picks it up.
        var sendNow = await (await admin.PostAsync($"/api/admin/newsletter/issues/{id}/send-now", null)).ReadJsonAsync();
        sendNow.GetProperty("status").GetString().Should().Be("Approved");
        sendNow.GetProperty("scheduledFor").GetDateTime().ToUniversalTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await admin.PostAsync($"/api/admin/newsletter/issues/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Deliveries list (empty) and filter validation; unknown issue → 404.
        var deliveries = await (await admin.GetAsync($"/api/admin/newsletter/issues/{id}/deliveries?status=Sent")).ReadJsonAsync();
        deliveries.GetProperty("totalCount").GetInt32().Should().Be(0);
        (await admin.GetAsync($"/api/admin/newsletter/issues/{id}/deliveries?status=nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"/api/admin/newsletter/issues/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_endpoints_require_admin()
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient());
        var client = _factory.CreateClient().Authenticated(user);

        (await client.GetAsync("/api/admin/newsletter/issues")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/admin/newsletter/audience")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/admin/newsletter/issues/generate", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/admin/newsletter/issues/{Guid.NewGuid()}/approve", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync("/api/admin/newsletter/issues")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Draft job ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Draft_job_creates_the_coming_Friday_once_after_Wednesday_9_WAT_and_emails_admins()
    {
        var friday = UniqueFriday();
        var wednesday = friday.AddDays(-2);

        (await RunDraftJobAsync(Utc(wednesday, 7, 59))).Should().BeNull("08:00 UTC = 09:00 WAT has not passed yet");
        (await WithDbAsync(db => db.NewsletterIssues.AnyAsync(i => i.IssueDate == friday))).Should().BeFalse();

        var before = Emails.Sent.Count;
        var issue = await RunDraftJobAsync(Utc(wednesday, 8, 0));
        issue.Should().NotBeNull();
        issue!.IssueDate.Should().Be(friday);
        issue.Status.Should().Be(NewsletterIssueStatus.Draft);
        issue.ScheduledFor.Should().Be(Utc(friday, 9));

        var mails = Emails.Sent.Skip(before).Where(m => m.Subject == "Newsletter draft ready to review").ToList();
        mails.Select(m => m.To).Should().BeEquivalentTo(new[] { HodracApiFactory.AdminEmail, HodracApiFactory.SecondAdminEmail });
        mails[0].DeepLink.Should().Be($"{FrontendBase}/admin/newsletter/{issue.Id}");
        mails[0].BodyLines.Should().Contain(l => l.Contains("only if an admin approves it"));
        mails.SelectMany(m => m.BodyLines).Should().NotContain(l => l.Contains('—') || l.Contains('–'));

        // Idempotent: later runs that week (catch-up) don't create or email again.
        var after = Emails.Sent.Count;
        (await RunDraftJobAsync(Utc(wednesday, 12, 0))).Should().BeNull();
        (await RunDraftJobAsync(Utc(friday, 8, 0))).Should().BeNull();
        Emails.Sent.Count.Should().Be(after);
        (await WithDbAsync(db => db.NewsletterIssues.CountAsync(i => i.IssueDate == friday))).Should().Be(1);
    }

    // ── Sender job ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Sender_snapshots_union_of_subscribers_and_confirmed_users_deduped_and_skips_opted_out()
    {
        await IsolateAudienceAsync();
        var tag = Unique();

        var both = await UserAsync(confirmed: true, email: $"Both-{tag}@example.test");
        await SubscribeAsync($"both-{tag}@EXAMPLE.test");                              // same address, other casing
        var userOnly = await UserAsync(confirmed: true);
        var unconfirmed = await UserAsync(confirmed: false);
        var unconfirmedSubscriber = await UserAsync(confirmed: false);
        await SubscribeAsync(unconfirmedSubscriber.Email);                             // gets it via the sign-up
        var optedOut = await UserAsync(confirmed: true, optOut: true);
        var subscriberOnly = $"sub-{tag}@example.test";
        await SubscribeAsync(subscriberOnly);
        var unsubscribed = $"gone-{tag}@example.test";
        await SubscribeAsync(unsubscribed);
        await WithDbAsync(db => db.NewsLetterEmails.Where(n => n.UserEmail == unsubscribed)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.UnsubscribedAt, DateTime.UtcNow)));

        var audience = await (await (await AdminAsync()).Client.GetAsync("/api/admin/newsletter/audience")).ReadJsonAsync();
        audience.GetProperty("totalUniqueRecipients").GetInt32().Should().Be(4);
        audience.GetProperty("activeSubscribers").GetInt32().Should().Be(3);
        audience.GetProperty("optedInUsers").GetInt32().Should().Be(2);

        var now = DateTime.UtcNow;
        var issue = await InsertIssueAsync(NewsletterIssueStatus.Approved, now.AddMinutes(-1));
        var before = Emails.SentHtml.Count;

        var run = await RunSenderAsync(now);
        run.IssuesStarted.Should().Be(1);
        run.IssuesCompleted.Should().Be(1);

        var deliveries = await DeliveriesAsync(issue.Id);
        var expected = new[] { both.Email, userOnly.Email, unconfirmedSubscriber.Email, subscriberOnly }
            .Select(e => e.ToLowerInvariant()).ToList();
        deliveries.Select(d => d.Email).Should().BeEquivalentTo(expected);
        deliveries.Should().OnlyContain(d => d.Status == NewsletterDeliveryStatus.Sent && d.Attempts == 1 && d.SentAt != null);
        deliveries.Single(d => d.Email == both.Email.ToLowerInvariant()).UserId.Should().Be(both.UserId);
        deliveries.Single(d => d.Email == subscriberOnly).UserId.Should().BeNull();
        deliveries.Select(d => d.Email).Should().NotContain(new[] { unconfirmed.Email.ToLowerInvariant(), optedOut.Email.ToLowerInvariant(), unsubscribed });

        var mails = Emails.SentHtml.Skip(before).ToList();
        mails.Select(m => m.To).Should().BeEquivalentTo(expected);

        // List-Unsubscribe headers, pointing at a token for that recipient.
        var mail = mails.Single(m => m.To == subscriberOnly);
        mail.Subject.Should().Be(issue.Subject);
        mail.Headers!["List-Unsubscribe-Post"].Should().Be("List-Unsubscribe=One-Click");
        var header = mail.Headers["List-Unsubscribe"];
        header.Should().StartWith($"<{ApiBase}/api/newsletter/unsubscribe?token=").And.EndWith(">");
        var token = Uri.UnescapeDataString(header[(header.IndexOf("token=", StringComparison.Ordinal) + 6)..^1]);
        _factory.Services.GetRequiredService<NewsletterUnsubscribeTokens>().Read(token).Should().Be(subscriberOnly);
        mail.HtmlBody.Should().Contain("Unsubscribe").And.Contain(NewsletterRenderer.FooterReason.Replace("'", "&#39;"));
        mail.TextBody.Should().Contain("Read this (https://www.hodrac.com/blog/x)").And.Contain("Unsubscribe: " + ApiBase);

        var final = await IssueAsync(issue.Id);
        final.Status.Should().Be(NewsletterIssueStatus.Sent);
        final.RecipientCount.Should().Be(4);
        final.SentCount.Should().Be(4);
        final.FailedCount.Should().Be(0);
        final.SendStartedAt.Should().NotBeNull();
        final.SentAt.Should().NotBeNull();

        // Idempotent re-run: nothing new is sent.
        var count = Emails.SentHtml.Count;
        var again = await RunSenderAsync(DateTime.UtcNow);
        again.Attempted.Should().Be(0);
        again.IssuesStarted.Should().Be(0);
        Emails.SentHtml.Count.Should().Be(count);
    }

    [Fact]
    public async Task Sender_sends_in_batches_across_runs_and_retries_failures_until_Failed()
    {
        await IsolateAudienceAsync();
        var tag = Unique();
        var emails = Enumerable.Range(1, 5).Select(i => $"batch{i}-{tag}@example.test").ToList();
        foreach (var e in emails) await SubscribeAsync(e);
        var failing = emails[2];
        Emails.FailHtmlFor[failing] = true;

        try
        {
            var now = DateTime.UtcNow;
            var issue = await InsertIssueAsync(NewsletterIssueStatus.Approved, now.AddMinutes(-1));
            // Not due yet → nothing happens.
            (await RunSenderAsync(now.AddMinutes(-2), batchSize: 2)).IssuesStarted.Should().Be(0);

            var r1 = await RunSenderAsync(now, batchSize: 2);
            r1.IssuesStarted.Should().Be(1);
            r1.Attempted.Should().Be(2);
            (await IssueAsync(issue.Id)).Status.Should().Be(NewsletterIssueStatus.Sending);
            (await IssueAsync(issue.Id)).RecipientCount.Should().Be(5);

            var r2 = await RunSenderAsync(now, batchSize: 2);   // batch3 fails (attempt 1), batch4 sent
            r2.Attempted.Should().Be(2);
            r2.Sent.Should().Be(1);
            var r3 = await RunSenderAsync(now, batchSize: 2);   // batch3 (attempt 2) + batch5
            r3.Attempted.Should().Be(2);
            var r4 = await RunSenderAsync(now, batchSize: 2);   // batch3 attempt 3 → Failed, issue done
            r4.Attempted.Should().Be(1);
            r4.Failed.Should().Be(1);
            r4.IssuesCompleted.Should().Be(1);

            var deliveries = await DeliveriesAsync(issue.Id);
            deliveries.Should().HaveCount(5);
            var failed = deliveries.Single(d => d.Email == failing);
            failed.Status.Should().Be(NewsletterDeliveryStatus.Failed);
            failed.Attempts.Should().Be(3);
            failed.LastError.Should().Contain("SMTP rejected");
            deliveries.Where(d => d.Email != failing).Should().OnlyContain(d => d.Status == NewsletterDeliveryStatus.Sent && d.Attempts == 1);
            Emails.SentHtml.Count(m => emails.Contains(m.To)).Should().Be(4);

            var final = await IssueAsync(issue.Id);
            final.Status.Should().Be(NewsletterIssueStatus.Sent);
            final.SentCount.Should().Be(4);
            final.FailedCount.Should().Be(1);

            // Admin view of deliveries with a status filter.
            var (admin, _) = await AdminAsync();
            var failedPage = await (await admin.GetAsync($"/api/admin/newsletter/issues/{issue.Id}/deliveries?status=failed")).ReadJsonAsync();
            failedPage.GetProperty("totalCount").GetInt32().Should().Be(1);
            failedPage.GetProperty("items")[0].GetProperty("email").GetString().Should().Be(failing);
            var detail = await (await admin.GetAsync($"/api/admin/newsletter/issues/{issue.Id}")).ReadJsonAsync();
            detail.GetProperty("deliveries").GetProperty("sent").GetInt32().Should().Be(4);
            detail.GetProperty("deliveries").GetProperty("failed").GetInt32().Should().Be(1);

            (await RunSenderAsync(DateTime.UtcNow, batchSize: 2)).Attempted.Should().Be(0);
        }
        finally
        {
            Emails.FailHtmlFor.TryRemove(failing, out _);
        }
    }

    [Fact]
    public async Task Cancel_during_sending_skips_the_rest_and_drafts_never_send()
    {
        await IsolateAudienceAsync();
        var tag = Unique();
        var emails = Enumerable.Range(1, 3).Select(i => $"cancel{i}-{tag}@example.test").ToList();
        foreach (var e in emails) await SubscribeAsync(e);

        var now = DateTime.UtcNow;
        var draft = await InsertIssueAsync(NewsletterIssueStatus.Draft, now.AddMinutes(-10));
        var issue = await InsertIssueAsync(NewsletterIssueStatus.Approved, now.AddMinutes(-1));

        (await RunSenderAsync(now, batchSize: 1)).Attempted.Should().Be(1);
        var (admin, _) = await AdminAsync();
        (await admin.PostAsync($"/api/admin/newsletter/issues/{issue.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RunSenderAsync(now, batchSize: 10)).Attempted.Should().Be(0);

        var deliveries = await DeliveriesAsync(issue.Id);
        deliveries.Count(d => d.Status == NewsletterDeliveryStatus.Sent).Should().Be(1);
        deliveries.Count(d => d.Status == NewsletterDeliveryStatus.Skipped).Should().Be(2);
        (await IssueAsync(issue.Id)).Status.Should().Be(NewsletterIssueStatus.Cancelled);

        (await IssueAsync(draft.Id)).Status.Should().Be(NewsletterIssueStatus.Draft);
        (await DeliveriesAsync(draft.Id)).Should().BeEmpty();
    }

    // ── Unsubscribe / re-subscribe ───────────────────────────────────────────

    [Fact]
    public async Task Unsubscribe_link_marks_subscriber_and_user_and_is_idempotent()
    {
        var user = await UserAsync(confirmed: true);
        await SubscribeAsync(user.Email);
        var anon = _factory.CreateClient();

        // Token made from a differently-cased address still matches.
        var token = TokenFor(user.Email.ToUpperInvariant());
        var page = await anon.GetAsync($"/api/newsletter/unsubscribe?token={Uri.EscapeDataString(token)}");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        page.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var html = await page.Content.ReadAsStringAsync();
        html.Should().Contain("unsubscribed").And.Contain(user.Email.ToLowerInvariant());

        (await WithDbAsync(db => db.NewsLetterEmails.AsNoTracking().SingleAsync(n => n.UserEmail == user.Email)))
            .UnsubscribedAt.Should().NotBeNull();
        (await WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == user.UserId)))
            .NewsletterOptOut.Should().BeTrue();

        // Again: still 200.
        (await anon.GetAsync($"/api/newsletter/unsubscribe?token={Uri.EscapeDataString(token)}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The user sees it in their preferences and can turn it back on.
        var client = _factory.CreateClient().Authenticated(user);
        var prefs = await (await client.GetAsync("/api/auth/me/notification-preferences")).ReadJsonAsync();
        prefs.GetProperty("newsletterOptOut").GetBoolean().Should().BeTrue();
        prefs.GetProperty("emailOptIn").GetBoolean().Should().BeTrue();
        var on = await client.PatchAsJsonAsync("/api/auth/me/notification-preferences", new { newsletterOptOut = false });
        on.StatusCode.Should().Be(HttpStatusCode.OK);
        (await on.ReadJsonAsync()).GetProperty("newsletterOptOut").GetBoolean().Should().BeFalse();
        var me = await (await client.GetAsync("/api/auth/me")).ReadJsonAsync();
        me.GetProperty("newsletterOptOut").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task One_click_post_unsubscribes_and_invalid_tokens_get_400()
    {
        var email = $"oneclick-{Unique()}@example.test";
        await SubscribeAsync(email);
        var anon = _factory.CreateClient();
        var url = $"/api/newsletter/unsubscribe?token={Uri.EscapeDataString(TokenFor(email))}";

        var post = await anon.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" }));
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        (await anon.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" })))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await WithDbAsync(db => db.NewsLetterEmails.AsNoTracking().SingleAsync(n => n.UserEmail == email)))
            .UnsubscribedAt.Should().NotBeNull();

        var badGet = await anon.GetAsync("/api/newsletter/unsubscribe?token=not-a-real-token");
        badGet.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badGet.Content.ReadAsStringAsync()).Should().Contain("unsubscribe link doesn").And.Contain(HodracContact.SupportEmail);
        (await anon.GetAsync("/api/newsletter/unsubscribe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsync("/api/newsletter/unsubscribe?token=tampered", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A token from another Data Protection purpose is rejected too.
        var foreign = _factory.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
            .CreateProtector("something-else").Protect("nl1|" + email);
        (await anon.GetAsync($"/api/newsletter/unsubscribe?token={Uri.EscapeDataString(foreign)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signing_up_again_after_unsubscribing_resubscribes()
    {
        var email = $"again-{Unique()}@example.test";
        await SubscribeAsync(email);

        // Already subscribed: unchanged behaviour (400).
        (await _factory.CreateClient().PostAsJsonAsync("/api/auth/newletter", new { userEmail = email }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _factory.CreateClient().GetAsync($"/api/newsletter/unsubscribe?token={Uri.EscapeDataString(TokenFor(email))}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await WithDbAsync(db => db.NewsLetterEmails.AsNoTracking().SingleAsync(n => n.UserEmail == email)))
            .UnsubscribedAt.Should().NotBeNull();

        var again = await _factory.CreateClient().PostAsJsonAsync("/api/auth/newletter", new { userEmail = email });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await again.ReadJsonAsync();
        body.GetProperty("userEmail").GetString().Should().Be(email);
        body.TryGetProperty("unsubscribedAt", out _).Should().BeFalse();
        (await WithDbAsync(db => db.NewsLetterEmails.AsNoTracking().CountAsync(n => n.UserEmail == email))).Should().Be(1);
        (await WithDbAsync(db => db.NewsLetterEmails.AsNoTracking().SingleAsync(n => n.UserEmail == email)))
            .UnsubscribedAt.Should().BeNull();
    }

    [Fact]
    public async Task Turning_the_newsletter_off_in_preferences_also_unsubscribes_the_signup()
    {
        var user = await UserAsync(confirmed: true);
        await SubscribeAsync(user.Email);
        var client = _factory.CreateClient().Authenticated(user);

        var off = await client.PatchAsJsonAsync("/api/auth/me/notification-preferences", new { newsletterOptOut = true });
        off.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await off.ReadJsonAsync();
        prefs.GetProperty("newsletterOptOut").GetBoolean().Should().BeTrue();
        prefs.GetProperty("emailOptIn").GetBoolean().Should().BeTrue("the digest flag is untouched when omitted");
        (await WithDbAsync(db => db.NewsLetterEmails.AsNoTracking().SingleAsync(n => n.UserEmail == user.Email)))
            .UnsubscribedAt.Should().NotBeNull();

        // The existing digest toggle still works on its own.
        var digest = await client.PatchAsJsonAsync("/api/auth/me/notification-preferences", new { emailOptIn = false });
        var after = await digest.ReadJsonAsync();
        after.GetProperty("emailOptIn").GetBoolean().Should().BeFalse();
        after.GetProperty("newsletterOptOut").GetBoolean().Should().BeTrue();
    }
}
