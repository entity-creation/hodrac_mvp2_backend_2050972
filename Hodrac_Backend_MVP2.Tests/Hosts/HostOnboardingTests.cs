using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

/// <summary>/api/host: profile, banks, payout account and verification upload (fake Paystack).</summary>
[Collection(IntegrationCollection.Name)]
public class HostOnboardingTests
{
    private readonly HodracApiFactory _factory;

    public HostOnboardingTests(HodracApiFactory factory) => _factory = factory;

    private async Task<HttpClient> NewUserClientAsync()
    {
        var anon = _factory.CreateClient();
        var user = await AuthHelper.RegisterAsync(anon, "Host Person");
        return _factory.CreateClient().Authenticated(user);
    }

    private async Task<HostProfile> LoadHostAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
        return await db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == id);
    }

    // ── Profile ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_is_401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/host/profile/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_profile_generates_slug_and_second_create_is_409()
    {
        var client = await NewUserClientAsync();

        (await client.GetAsync("/api/host/profile/me")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var created = await CreateProfileAsync(client, "Lagos Trippers & Co.");
        created.GetProperty("slug").GetString().Should().StartWith("lagos-trippers-co");
        created.GetProperty("verificationStatus").GetString().Should().Be("Unverified");
        created.GetProperty("payoutAccountSet").GetBoolean().Should().BeFalse();
        created.GetProperty("plan").GetString().Should().Be("Host");

        var second = await client.PostAsJsonAsync("/api/host/profile", new { displayName = "Another" });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var me = await client.GetAsync("/api/host/profile/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.ReadJsonAsync()).GetProperty("id").GetGuid().Should().Be(created.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Slug_must_be_lowercase_hyphenated_and_unique()
    {
        var a = await NewUserClientAsync();
        var b = await NewUserClientAsync();
        var slug = $"club-{Guid.NewGuid():N}"[..20];

        (await a.PostAsJsonAsync("/api/host/profile", new { displayName = "Club", slug = "Bad Slug!" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await a.PostAsJsonAsync("/api/host/profile", new { displayName = "Club", slug = "double--hyphen" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await CreateProfileAsync(a, "Club", slug);
        (await b.PostAsJsonAsync("/api/host/profile", new { displayName = "Club B", slug }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Patch_updates_fields_and_validates()
    {
        var client = await NewUserClientAsync();
        (await client.PatchAsJsonAsync("/api/host/profile/me", new { bio = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await CreateProfileAsync(client, "Abuja Explorers");

        var bad = await client.PatchAsJsonAsync("/api/host/profile/me", new { logoUrl = "javascript:alert(1)" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await client.PatchAsJsonAsync("/api/host/profile/me", new
        {
            displayName = "Abuja Explorers Club",
            bio = "Weekend trips from Abuja.",
            instagramHandle = "@abuja.explorers",
            whatsAppNumber = "+234 803 000 0000",
            logoUrl = "https://cdn.example.test/logo.png",
        });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var body = await ok.ReadJsonAsync();
        body.GetProperty("displayName").GetString().Should().Be("Abuja Explorers Club");
        body.GetProperty("instagramHandle").GetString().Should().Be("abuja.explorers");
        body.GetProperty("whatsAppNumber").GetString().Should().Be("+2348030000000");

        var cleared = await client.PatchAsJsonAsync("/api/host/profile/me", new { bio = "" });
        (await cleared.ReadJsonAsync()).GetProperty("bio").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    // ── Banks + payout ───────────────────────────────────────────────────────

    [Fact]
    public async Task Banks_are_proxied_from_paystack()
    {
        var client = await NewUserClientAsync();
        var response = await client.GetAsync("/api/host/banks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var banks = await response.ReadJsonAsync();
        banks.EnumerateArray().Select(b => b.GetProperty("code").GetString()).Should().Contain(new[] { "044", "001" });
    }

    [Fact]
    public async Task Payout_resolve_then_confirm_stores_only_last4()
    {
        const string accountNumber = "0193274682";
        var client = await NewUserClientAsync();
        var profile = await CreateProfileAsync(client, "Payout Host");
        var hostId = profile.GetProperty("id").GetGuid();

        // Resolve only: returns the name, stores nothing.
        var preview = await client.PostAsJsonAsync("/api/host/payout-account", new { bankCode = "044", accountNumber });
        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var previewText = await preview.Content.ReadAsStringAsync();
        var previewBody = await preview.ReadJsonAsync();
        previewBody.GetProperty("confirmed").GetBoolean().Should().BeFalse();
        previewBody.GetProperty("accountName").GetString().Should().Be(FakePaystackClient.ResolvedAccountName);
        previewBody.GetProperty("bankName").GetString().Should().Be("Access Bank");
        previewBody.GetProperty("accountLast4").GetString().Should().Be("4682");
        previewText.Should().NotContain(accountNumber);
        (await LoadHostAsync(hostId)).PaystackSubaccountCode.Should().BeNull();

        // Confirm: creates the subaccount with business_name = display name.
        var confirm = await client.PostAsJsonAsync("/api/host/payout-account", new { bankCode = "044", accountNumber, confirm = true });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        var confirmText = await confirm.Content.ReadAsStringAsync();
        confirmText.Should().NotContain(accountNumber).And.NotContain("ACCT_");
        var confirmBody = await confirm.ReadJsonAsync();
        confirmBody.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        confirmBody.GetProperty("profile").GetProperty("payoutAccountSet").GetBoolean().Should().BeTrue();
        confirmBody.GetProperty("profile").GetProperty("payoutAccount").GetProperty("accountLast4").GetString().Should().Be("4682");

        _factory.Paystack.CreatedSubaccounts.Should().Contain(r =>
            r.BusinessName == "Payout Host" && r.SettlementBankCode == "044" && r.AccountNumber == accountNumber);

        var host = await LoadHostAsync(hostId);
        host.PaystackSubaccountCode.Should().StartWith("ACCT_fake");
        host.SettlementBankCode.Should().Be("044");
        host.SettlementBankName.Should().Be("Access Bank");
        host.SettlementAccountName.Should().Be(FakePaystackClient.ResolvedAccountName);
        host.SettlementAccountLast4.Should().Be("4682");

        // The full number is nowhere in the stored row.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            var rowJson = await db.Database
                .SqlQuery<string>($"SELECT row_to_json(h)::text AS \"Value\" FROM \"HostProfiles\" h WHERE h.\"Id\" = {hostId}")
                .SingleAsync();
            rowJson.Should().Contain("4682").And.NotContain(accountNumber);
        }

        // Changing the account updates the existing subaccount instead of creating another.
        var change = await client.PostAsJsonAsync("/api/host/payout-account", new { bankCode = "001", accountNumber = "1234567890", confirm = true });
        change.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Paystack.UpdatedSubaccounts.Should().Contain(u => u.Code == host.PaystackSubaccountCode && u.Update.AccountNumber == "1234567890");
        (await LoadHostAsync(hostId)).SettlementAccountLast4.Should().Be("7890");
    }

    [Fact]
    public async Task Payout_rejects_bad_input_and_unresolvable_accounts()
    {
        var client = await NewUserClientAsync();
        (await client.PostAsJsonAsync("/api/host/payout-account", new { bankCode = "044", accountNumber = "0123456789" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "no host profile yet");

        await CreateProfileAsync(client, "Bad Payout Host");

        (await client.PostAsJsonAsync("/api/host/payout-account", new { bankCode = "044", accountNumber = "12345" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/host/payout-account", new { bankCode = "777", accountNumber = "0123456789" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "unknown bank");
        (await client.PostAsJsonAsync("/api/host/payout-account", new { bankCode = "044", accountNumber = "0001112223", confirm = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "Paystack could not resolve it");
    }

    // ── Verification ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Verification_rejects_wrong_type_spoofed_type_and_oversize()
    {
        var client = await NewUserClientAsync();
        await CreateProfileAsync(client, "Upload Host");

        // Text file claiming to be a PNG: magic bytes decide.
        var spoofed = await client.PostAsync("/api/host/verification",
            VerificationForm("just some text"u8.ToArray(), "id.png", "image/png"));
        spoofed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await spoofed.Content.ReadAsStringAsync()).Should().Contain("PDF, JPG or PNG");

        // Over 5 MB (valid PDF header).
        var big = new byte[(int)HostDocument.MaxSizeBytes + 1];
        PdfBytes.AsSpan(0, 8).CopyTo(big);
        var tooLarge = await client.PostAsync("/api/host/verification", VerificationForm(big));
        tooLarge.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLarge.Content.ReadAsStringAsync()).Should().Contain("5 MB");

        // Bad links, bad CAC (fields are optional, but whatever is given is validated).
        (await client.PostAsync("/api/host/verification", VerificationForm(PdfBytes, proofUrls: "ftp://nope")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsync("/api/host/verification",
                VerificationForm(PdfBytes, proofUrls: Enumerable.Range(0, 11).Select(i => $"https://x.test/{i}").ToArray())))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsync("/api/host/verification", VerificationForm(null, cacNumber: "RC/123*")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // An invalid file is still refused when it's the only thing sent.
        var onlyBadFile = new MultipartFormDataContent();
        var badPart = new ByteArrayContent("not a document"u8.ToArray());
        badPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        onlyBadFile.Add(badPart, "idDocument", "id.pdf");
        (await client.PostAsync("/api/host/verification", onlyBadFile)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var me = await (await client.GetAsync("/api/host/profile/me")).ReadJsonAsync();
        me.GetProperty("verificationStatus").GetString().Should().Be("Unverified");
    }

    [Fact]
    public async Task Verification_with_nothing_provided_moves_to_pending_and_emails_admins()
    {
        var client = await NewUserClientAsync();
        var profile = await CreateProfileAsync(client, "Nothing To Show Host");
        var hostId = profile.GetProperty("id").GetGuid();

        var response = await client.PostAsync("/api/host/verification", EmptyVerificationForm());
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("verificationStatus").GetString().Should().Be("Pending");
        body.GetProperty("hasIdDocument").GetBoolean().Should().BeFalse();
        body.GetProperty("cacNumber").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("pastTripProofUrls").GetArrayLength().Should().Be(0);

        var host = await LoadHostAsync(hostId);
        host.VerificationStatus.Should().Be(HostVerificationStatus.Pending);
        host.IdDocumentStorageKey.Should().BeNull();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            (await db.HostDocuments.CountAsync(d => d.HostProfileId == hostId)).Should().Be(0);
        }

        _factory.Emails.Sent.Should().Contain(e => e.To == HodracApiFactory.AdminEmail && e.Subject == "Host verification to review"
            && e.BodyLines.Any(l => l.Contains("Nothing To Show Host")));

        // Submitting again while Pending is allowed.
        (await client.PostAsync("/api/host/verification", EmptyVerificationForm()))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Verification_partial_submission_is_stored()
    {
        var client = await NewUserClientAsync();
        var profile = await CreateProfileAsync(client, "Links Only Host");
        var hostId = profile.GetProperty("id").GetGuid();

        // Only a past-trip link: no CAC number, no ID document.
        var response = await client.PostAsync("/api/host/verification",
            VerificationForm(null, cacNumber: null, proofUrls: "https://example.test/our-last-trip"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("verificationStatus").GetString().Should().Be("Pending");
        body.GetProperty("hasIdDocument").GetBoolean().Should().BeFalse();
        body.GetProperty("cacNumber").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("pastTripProofUrls")[0].GetString().Should().Be("https://example.test/our-last-trip");

        // Only a CAC number.
        var cacOnly = new MultipartFormDataContent { { new StringContent("RC 555123"), "cacNumber" } };
        var second = await client.PostAsync("/api/host/verification", cacOnly);
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());

        var host = await LoadHostAsync(hostId);
        host.VerificationStatus.Should().Be(HostVerificationStatus.Pending);
        host.CacNumber.Should().Be("RC 555123");
        host.IdDocumentStorageKey.Should().BeNull();
    }

    [Fact]
    public async Task Verification_stores_document_privately_sets_pending_and_emails_admins()
    {
        var client = await NewUserClientAsync();
        var profile = await CreateProfileAsync(client, "Verified Soon Host");
        var hostId = profile.GetProperty("id").GetGuid();

        // A PNG uploaded with a misleading name/content type is stored as what it really is.
        var response = await client.PostAsync("/api/host/verification",
            VerificationForm(PngBytes, "../../passport.pdf", "application/pdf", "RC 7654321",
                "https://instagram.com/p/zanzibar-2025", "https://example.test/trip-recap"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var text = await response.Content.ReadAsStringAsync();
        var body = await response.ReadJsonAsync();
        body.GetProperty("verificationStatus").GetString().Should().Be("Pending");
        body.GetProperty("hasIdDocument").GetBoolean().Should().BeTrue();
        body.GetProperty("cacNumber").GetString().Should().Be("RC 7654321");
        body.GetProperty("pastTripProofUrls").GetArrayLength().Should().Be(2);
        text.Should().NotContain("storageKey", "storage keys never leave the server")
            .And.NotContain("idDocumentStorageKey");

        var host = await LoadHostAsync(hostId);
        host.VerificationStatus.Should().Be(HostVerificationStatus.Pending);
        host.IdDocumentStorageKey.Should().NotBeNull();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            var doc = await db.HostDocuments.AsNoTracking().SingleAsync(d => d.HostProfileId == hostId);
            doc.Id.ToString().Should().Be(host.IdDocumentStorageKey);
            doc.Kind.Should().Be(HostDocumentKinds.IdDocument);
            doc.ContentType.Should().Be("image/png");
            doc.FileName.Should().Be("passport.png");
            doc.Content.Should().Equal(PngBytes);
            doc.SizeBytes.Should().Be(PngBytes.Length);
            doc.Sha256.Should().HaveLength(64);
        }

        text.Should().NotContain(host.IdDocumentStorageKey!);

        _factory.Emails.Sent.Should().Contain(e => e.To == HodracApiFactory.AdminEmail && e.Subject == "Host verification to review"
            && e.BodyLines.Any(l => l.Contains("Verified Soon Host")));

        // Resubmitting replaces the document rather than accumulating copies.
        var again = await client.PostAsync("/api/host/verification", VerificationForm(PdfBytes));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
            (await db.HostDocuments.CountAsync(d => d.HostProfileId == hostId)).Should().Be(1);
        }
    }
}
