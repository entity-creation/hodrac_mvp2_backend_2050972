using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Controllers;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

/// <summary>/api/admin/hosts: queue, signed document links, decisions, founding, operator plan.</summary>
[Collection(IntegrationCollection.Name)]
public class AdminHostsTests
{
    private readonly HodracApiFactory _factory;

    public AdminHostsTests(HodracApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, TestUser User)> AdminAsync(string email = HodracApiFactory.AdminEmail)
    {
        var user = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), email);
        return (_factory.CreateClient().Authenticated(user), user);
    }

    private async Task<(HttpClient Client, TestUser User, Guid HostId)> PendingHostAsync(string displayName)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), displayName);
        var client = _factory.CreateClient().Authenticated(user);
        var hostId = await CreatePendingHostAsync(client, displayName);
        return (client, user, hostId);
    }

    private async Task<(TestUser User, Guid HostId)> PlainHostAsync(string displayName)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), displayName);
        var profile = await CreateProfileAsync(_factory.CreateClient().Authenticated(user), displayName);
        return (user, profile.GetProperty("id").GetGuid());
    }

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    // ── Access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Non_admin_gets_403_everywhere()
    {
        var (hostClient, _, hostId) = await PendingHostAsync("Sneaky Host");

        (await hostClient.GetAsync("/api/admin/hosts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hostClient.GetAsync($"/api/admin/hosts/{hostId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hostClient.GetAsync($"/api/admin/hosts/{hostId}/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hostClient.PostAsync($"/api/admin/hosts/{hostId}/verify", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await hostClient.PostAsJsonAsync($"/api/admin/hosts/{hostId}/revoke", new { reason = "nope" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Even holding a validly signed link, a non-admin can't download.
        var (adminClient, _) = await AdminAsync();
        var docs = await (await adminClient.GetAsync($"/api/admin/hosts/{hostId}/documents")).ReadJsonAsync();
        var url = docs[0].GetProperty("url").GetString()!;
        (await hostClient.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Queue ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Queue_lists_pending_host_with_docs_links_and_payout_summary()
    {
        var (_, hostUser, hostId) = await PendingHostAsync("Queue Host Zanzibar");
        var (admin, _) = await AdminAsync();

        var response = await admin.GetAsync("/api/admin/hosts?status=pending&search=Queue%20Host%20Zanzibar&page=1&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var text = await response.Content.ReadAsStringAsync();
        var page = await response.ReadJsonAsync();

        page.GetProperty("totalCount").GetInt32().Should().Be(1);
        var item = page.GetProperty("items")[0];
        item.GetProperty("id").GetGuid().Should().Be(hostId);
        item.GetProperty("displayName").GetString().Should().Be("Queue Host Zanzibar");
        item.GetProperty("userEmail").GetString().Should().Be(hostUser.Email);
        item.GetProperty("verificationStatus").GetString().Should().Be("Pending");
        item.GetProperty("documentCount").GetInt32().Should().Be(1);
        item.GetProperty("pastTripProofUrls")[0].GetString().Should().Be("https://instagram.com/p/past-trip");
        item.GetProperty("payoutAccountSet").GetBoolean().Should().BeTrue();
        item.GetProperty("payoutAccountLast4").GetString().Should().Be("6789");
        item.GetProperty("isFoundingHost").GetBoolean().Should().BeFalse();
        item.GetProperty("plan").GetString().Should().Be("Host");
        text.Should().NotContain("0123456789");

        // Search by the host user's email works too.
        var byEmail = await (await admin.GetAsync($"/api/admin/hosts?search={Uri.EscapeDataString(hostUser.Email)}")).ReadJsonAsync();
        byEmail.GetProperty("totalCount").GetInt32().Should().Be(1);

        (await admin.GetAsync("/api/admin/hosts?status=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"/api/admin/hosts/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var detail = await (await admin.GetAsync($"/api/admin/hosts/{hostId}")).ReadJsonAsync();
        detail.GetProperty("summary").GetProperty("id").GetGuid().Should().Be(hostId);
        detail.GetProperty("payoutAccountName").GetString().Should().Be(FakePaystackClient.ResolvedAccountName);
    }

    // ── Documents ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signed_document_link_downloads_for_admin_and_fails_when_tampered_or_expired()
    {
        var (_, _, hostId) = await PendingHostAsync("Docs Host");
        var (admin, adminUser) = await AdminAsync();

        var listResponse = await admin.GetAsync($"/api/admin/hosts/{hostId}/documents");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var docs = await listResponse.ReadJsonAsync();
        docs.GetArrayLength().Should().Be(1);
        var doc = docs[0];
        doc.GetProperty("contentType").GetString().Should().Be("application/pdf");
        doc.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
        var docId = doc.GetProperty("id").GetGuid();
        var url = doc.GetProperty("url").GetString()!;
        url.Should().StartWith($"/api/admin/hosts/documents/{docId}?exp=").And.Contain("&sig=");

        // Valid link: streams the bytes as an attachment, never cached.
        var download = await admin.GetAsync(url);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(PdfBytes);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Headers.CacheControl!.NoStore.Should().BeTrue();

        // Tampered signature.
        var sigIndex = url.IndexOf("&sig=", StringComparison.Ordinal) + 5;
        var tamperedSig = url[..sigIndex] + (url[sigIndex] == 'A' ? 'B' : 'A') + url[(sigIndex + 1)..];
        (await admin.GetAsync(tamperedSig)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Tampered expiry (pushed later).
        var exp = long.Parse(url.Split("exp=")[1].Split('&')[0]);
        (await admin.GetAsync(url.Replace($"exp={exp}", $"exp={exp + 3600}"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Link signed for another document id.
        (await admin.GetAsync(url.Replace(docId.ToString(), Guid.NewGuid().ToString()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // No signature at all.
        (await admin.GetAsync($"/api/admin/hosts/documents/{docId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Expired link.
        var signer = _factory.Services.GetRequiredService<HostDocumentLinkSigner>();
        var expired = signer.Create(docId, Guid.Parse(adminUser.UserId), DateTimeOffset.UtcNow.AddMinutes(-1));
        (await admin.GetAsync(expired.Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A link issued to one admin doesn't work for another.
        var (otherAdmin, _) = await AdminAsync(HodracApiFactory.SecondAdminEmail);
        (await otherAdmin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Decisions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_sets_status_notifies_and_emails_host()
    {
        var (hostClient, hostUser, hostId) = await PendingHostAsync("Verify Me Host");
        var (admin, adminUser) = await AdminAsync();

        var response = await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("summary").GetProperty("verificationStatus").GetString().Should().Be("Verified");
        body.GetProperty("verifiedByUserId").GetGuid().Should().Be(Guid.Parse(adminUser.UserId));

        var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId));
        host.VerificationStatus.Should().Be(HostVerificationStatus.Verified);
        host.VerifiedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        host.VerifiedByUserId.Should().Be(Guid.Parse(adminUser.UserId));

        var hostUserId = Guid.Parse(hostUser.UserId);
        var notification = await WithDbAsync(db => db.Notifications.AsNoTracking()
            .SingleAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostVerified));
        notification.ActorUserId.Should().Be(Guid.Parse(adminUser.UserId));
        notification.EmailedAt.Should().NotBeNull("the host was emailed directly, so the digest skips it");
        _factory.Emails.Sent.Should().Contain(e => e.To == hostUser.Email && e.Subject == "You're a verified Hodrac host");

        (await hostClient.GetAsync("/api/host/profile/me")).ReadJsonAsync().Result
            .GetProperty("verificationStatus").GetString().Should().Be("Verified");

        // Already verified: no-op transition.
        (await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/reject", new { reason = "Too late." })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/api/admin/hosts/{Guid.NewGuid()}/verify", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reject_requires_reason_sets_status_and_notifies_host()
    {
        var (hostClient, hostUser, hostId) = await PendingHostAsync("Reject Me Host");
        var (admin, _) = await AdminAsync();

        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/reject", new { reason = "  " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var response = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/reject", new { reason = "ID photo is unreadable." });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId));
        host.VerificationStatus.Should().Be(HostVerificationStatus.Rejected);
        host.RejectionReason.Should().Be("ID photo is unreadable.");

        var hostUserId = Guid.Parse(hostUser.UserId);
        (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostRejected)))
            .Should().BeTrue();
        _factory.Emails.Sent.Should().Contain(e => e.To == hostUser.Email && e.BodyLines.Any(l => l.Contains("ID photo is unreadable.")));

        var me = await (await hostClient.GetAsync("/api/host/profile/me")).ReadJsonAsync();
        me.GetProperty("verificationStatus").GetString().Should().Be("Rejected");
        me.GetProperty("rejectionReason").GetString().Should().Be("ID photo is unreadable.");

        // The host can resubmit (back to Pending, reason cleared).
        var resubmit = await hostClient.PostAsync("/api/host/verification", VerificationForm(PdfBytes));
        resubmit.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await resubmit.ReadJsonAsync();
        after.GetProperty("verificationStatus").GetString().Should().Be("Pending");
        after.GetProperty("rejectionReason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Admin_can_verify_an_unverified_host_who_submitted_nothing()
    {
        var (hostUser, hostId) = await PlainHostAsync("Never Submitted Host");
        var (admin, adminUser) = await AdminAsync();

        var response = await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("summary").GetProperty("verificationStatus").GetString().Should().Be("Verified");
        body.GetProperty("summary").GetProperty("documentCount").GetInt32().Should().Be(0);

        var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId));
        host.VerificationStatus.Should().Be(HostVerificationStatus.Verified);
        host.VerifiedByUserId.Should().Be(Guid.Parse(adminUser.UserId));
        var hostUserId = Guid.Parse(hostUser.UserId);
        (await WithDbAsync(db => db.Notifications.AnyAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostVerified)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Reject_allowed_from_unverified_and_verify_allowed_from_rejected()
    {
        var (_, hostId) = await PlainHostAsync("Unverified Reject Host");
        var (admin, _) = await AdminAsync();

        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/reject", new { reason = "No trip history yet." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        // Already rejected: no-op transition.
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/reject", new { reason = "Again." }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var verify = await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId));
        host.VerificationStatus.Should().Be(HostVerificationStatus.Verified);
        host.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task Revoke_removes_badge_notifies_host_and_records_reviewer()
    {
        var (hostClient, hostUser, hostId) = await PendingHostAsync("Revoke Me Host");
        var (admin, adminUser) = await AdminAsync();
        (await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var create = await hostClient.PostAsJsonAsync("/api/trips", new
        {
            freeTextDestination = $"Revoke-{Guid.NewGuid():N}",
            startDate = day.AddDays(90),
            endDate = day.AddDays(95),
            isDateFlexible = false,
            caption = "Revoke test trip",
            maxGroupSize = 6,
            isHostedTrip = true,
            pricePerSeatKobo = 25_000_000,
            depositKobo = 5_000_000,
            instalments = new[] { new { sequence = 1, amountKobo = 20_000_000, dueDate = day.AddDays(30) } },
            feeMode = "PassToTraveller",
            termsText = "Deposit is non-refundable.",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var tripId = (await create.ReadJsonAsync()).GetProperty("id").GetGuid();

        async Task<bool> BadgeAsync() =>
            (await (await _factory.CreateClient().GetAsync($"/api/trips/public/{tripId}")).ReadJsonAsync())
                .GetProperty("host").GetProperty("verified").GetBoolean();
        async Task<string> ShareTextAsync() =>
            (await (await _factory.CreateClient().GetAsync($"/api/share/trip/{tripId}")).ReadJsonAsync())
                .GetProperty("text").GetString()!;

        (await BadgeAsync()).Should().BeTrue();
        (await ShareTextAsync()).Should().EndWith("Verified host");

        var response = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/revoke", new { reason = "Complaints from travellers." });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("summary").GetProperty("verificationStatus").GetString().Should().Be("Unverified");
        body.GetProperty("rejectionReason").GetString().Should().Be("Verification removed: Complaints from travellers.");

        var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId));
        host.VerificationStatus.Should().Be(HostVerificationStatus.Unverified);
        host.VerifiedByUserId.Should().Be(Guid.Parse(adminUser.UserId));
        host.VerifiedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        host.IdDocumentStorageKey.Should().NotBeNull("revoking keeps the documents");

        var hostUserId = Guid.Parse(hostUser.UserId);
        var notification = await WithDbAsync(db => db.Notifications.AsNoTracking()
            .SingleAsync(n => n.RecipientUserId == hostUserId && n.Type == NotificationType.HostVerificationRevoked));
        notification.ActorUserId.Should().Be(Guid.Parse(adminUser.UserId));
        notification.EmailedAt.Should().NotBeNull();
        _factory.Emails.Sent.Should().Contain(e => e.To == hostUser.Email && e.Subject == "Your Hodrac host verification was removed"
            && e.BodyLines.Any(l => l.Contains("Complaints from travellers.")));

        // The badge is gone everywhere it is shown.
        (await BadgeAsync()).Should().BeFalse();
        (await ShareTextAsync()).Should().NotContain("Verified host");
        var me = await (await hostClient.GetAsync("/api/host/profile/me")).ReadJsonAsync();
        me.GetProperty("verificationStatus").GetString().Should().Be("Unverified");

        // Revoking again (not Verified) is a 409; the host can resubmit and be re-verified.
        (await admin.PostAsync($"/api/admin/hosts/{hostId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await hostClient.PostAsync("/api/host/verification", EmptyVerificationForm())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsync($"/api/admin/hosts/{hostId}/verify", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await BadgeAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_without_reason_works_and_non_verified_hosts_are_409()
    {
        var (_, _, pendingId) = await PendingHostAsync("Revoke Pending Host");
        var (_, plainId) = await PlainHostAsync("Revoke Plain Host");
        var (admin, _) = await AdminAsync();

        (await admin.PostAsync($"/api/admin/hosts/{pendingId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{plainId}/revoke", new { reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/api/admin/hosts/{Guid.NewGuid()}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await admin.PostAsync($"/api/admin/hosts/{plainId}/verify", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var revoke = await admin.PostAsync($"/api/admin/hosts/{plainId}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK, await revoke.Content.ReadAsStringAsync());
        var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == plainId));
        host.VerificationStatus.Should().Be(HostVerificationStatus.Unverified);
        host.RejectionReason.Should().Be("Verification removed by an admin.");
    }

    // ── Founding ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Founding_enforces_limit_and_deadline()
    {
        var (admin, _) = await AdminAsync();
        var options = _factory.Services.GetRequiredService<IOptions<HodracHostOptions>>().Value;

        // Only this test marks founding hosts; start from however many exist (should be 0).
        var existing = await WithDbAsync(db => db.HostProfiles.CountAsync(h => h.IsFoundingHost));
        var slotsLeft = HodracApiFactory.FoundingHostLimit - existing;
        slotsLeft.Should().BeGreaterThan(0);

        var hosts = new List<Guid>();
        for (var i = 0; i <= slotsLeft; i++)
            hosts.Add((await PlainHostAsync($"Founding Host {i}")).HostId);

        for (var i = 0; i < slotsLeft; i++)
        {
            var ok = await admin.PostAsync($"/api/admin/hosts/{hosts[i]}/founding", null);
            ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
            (await ok.ReadJsonAsync()).GetProperty("summary").GetProperty("isFoundingHost").GetBoolean().Should().BeTrue();
        }

        // Re-marking an existing founding host is a no-op, not a new slot.
        (await admin.PostAsync($"/api/admin/hosts/{hosts[0]}/founding", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var full = await admin.PostAsync($"/api/admin/hosts/{hosts[slotsLeft]}/founding", null);
        full.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await full.Content.ReadAsStringAsync()).Should().Contain("places are taken");

        // Past the deadline the offer is closed, whatever the count.
        var originalDeadline = options.FoundingDeadline;
        var originalLimit = options.FoundingHostLimit;
        try
        {
            options.FoundingHostLimit = 1000;
            options.FoundingDeadline = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
            var late = await admin.PostAsync($"/api/admin/hosts/{hosts[slotsLeft]}/founding", null);
            late.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await late.Content.ReadAsStringAsync()).Should().Contain("closed");
        }
        finally
        {
            options.FoundingDeadline = originalDeadline;
            options.FoundingHostLimit = originalLimit;
        }

        (await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hosts[slotsLeft])))
            .IsFoundingHost.Should().BeFalse();
    }

    // ── Operator plan ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operator_plan_records_payment_and_sets_plan_until_end_of_period()
    {
        var (admin, adminUser) = await AdminAsync();
        var (_, hostId) = await PlainHostAsync("Operator Host");
        var reference = $"INV-{Guid.NewGuid():N}"[..20];

        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/operator-plan",
                new { reference, amountKobo = 0, periodStart = "2026-12-01", periodEnd = "2027-11-30" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/operator-plan",
                new { reference, amountKobo = 90_000_000, periodStart = "2027-12-01", periodEnd = "2027-11-30" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var response = await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/operator-plan",
            new { reference, amountKobo = 90_000_000, periodStart = "2026-12-01", periodEnd = "2027-11-30" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("summary").GetProperty("plan").GetString().Should().Be("Operator");
        body.GetProperty("operatorPlanPayments").GetArrayLength().Should().Be(1);

        var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == hostId));
        host.Plan.Should().Be(HostPlan.Operator);
        host.OperatorPlanExpiresAt.Should().Be(AdminHostsController.EndOfDayUtc(new DateOnly(2027, 11, 30)));
        host.OperatorPlanExpiresAt!.Value.Date.Should().Be(new DateTime(2027, 11, 30));

        var payment = await WithDbAsync(db => db.OperatorPlanPayments.AsNoTracking().SingleAsync(p => p.HostProfileId == hostId));
        payment.Reference.Should().Be(reference);
        payment.AmountKobo.Should().Be(90_000_000);
        payment.RecordedByUserId.Should().Be(Guid.Parse(adminUser.UserId));

        // Same reference twice → 409.
        (await admin.PostAsJsonAsync($"/api/admin/hosts/{hostId}/operator-plan",
                new { reference, amountKobo = 90_000_000, periodStart = "2026-12-01", periodEnd = "2027-11-30" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
