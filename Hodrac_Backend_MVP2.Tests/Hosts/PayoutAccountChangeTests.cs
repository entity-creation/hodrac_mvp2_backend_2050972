using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

/// <summary>
/// POST /api/host/payout-account CHANGES: password re-authentication, same-account
/// no-op, host email + notification, admin alert, and no full account numbers in emails.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PayoutAccountChangeTests
{
    private const string FirstAccount = "0193274682";   // Access Bank (044), last 4 4682
    private const string SecondAccount = "2233445566";  // Test Bank (001), last 4 5566

    private readonly HodracApiFactory _factory;

    public PayoutAccountChangeTests(HodracApiFactory factory) => _factory = factory;

    private record HostCtx(HttpClient Client, TestUser User, Guid HostId);

    private async Task<HostCtx> NewHostAsync(string displayName, bool withAccount = true)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), displayName);
        var client = _factory.CreateClient().Authenticated(user);
        var profile = await CreateProfileAsync(client, displayName);
        var ctx = new HostCtx(client, user, profile.GetProperty("id").GetGuid());
        if (withAccount)
        {
            var setup = await PostAsync(ctx, "044", FirstAccount, confirm: true);
            setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        }
        return ctx;
    }

    private static Task<HttpResponseMessage> PostAsync(HostCtx ctx, string bankCode, string accountNumber, bool confirm, string? password = null) =>
        ctx.Client.PostAsJsonAsync("/api/host/payout-account", new { bankCode, accountNumber, confirm, password });

    private async Task<HostProfile> HostRowAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
        return await db.HostProfiles.AsNoTracking().SingleAsync(h => h.Id == id);
    }

    private async Task<List<Notification>> ChangeNotificationsAsync(string userId)
    {
        var id = Guid.Parse(userId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == id && n.Type == NotificationType.PayoutAccountChanged)
            .ToListAsync();
    }

    private int UpdatesFor(string? code) => _factory.Paystack.UpdatedSubaccounts.Count(u => u.Code == code);

    private List<RecordingEmailSender.SentEmail> HostChangeEmails(HostCtx ctx) =>
        _factory.Emails.Sent.Where(e => e.To == ctx.User.Email && e.Subject == "Your Hodrac payout account was changed").ToList();

    private List<RecordingEmailSender.SentEmail> AdminChangeEmails(HostCtx ctx) =>
        _factory.Emails.Sent.Where(e => e.Subject == "Host payout account changed"
            && e.BodyLines.Any(l => l.Contains(ctx.HostId.ToString()))).ToList();

    private static async Task ShouldBePasswordErrorAsync(HttpResponseMessage response, string message)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("errors").GetProperty("Password").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal(message);
    }

    [Fact]
    public async Task First_setup_needs_no_password_and_sends_no_change_emails()
    {
        var ctx = await NewHostAsync("First Setup Host", withAccount: false);

        var lookup = await PostAsync(ctx, "044", FirstAccount, confirm: false);
        lookup.StatusCode.Should().Be(HttpStatusCode.OK);

        var setup = await PostAsync(ctx, "044", FirstAccount, confirm: true);
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        (await setup.ReadJsonAsync()).GetProperty("confirmed").GetBoolean().Should().BeTrue();

        (await HostRowAsync(ctx.HostId)).PaystackSubaccountCode.Should().NotBeNull();
        _factory.Paystack.CreatedSubaccounts.Should().Contain(r => r.BusinessName == "First Setup Host");
        HostChangeEmails(ctx).Should().BeEmpty();
        AdminChangeEmails(ctx).Should().BeEmpty();
        (await ChangeNotificationsAsync(ctx.User.UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Change_without_or_with_wrong_password_is_400_and_changes_nothing()
    {
        var ctx = await NewHostAsync("Guarded Host");
        var code = (await HostRowAsync(ctx.HostId)).PaystackSubaccountCode;
        var updatesBefore = UpdatesFor(code);

        await ShouldBePasswordErrorAsync(await PostAsync(ctx, "001", SecondAccount, confirm: true),
            "Enter your password to change your payout account.");
        await ShouldBePasswordErrorAsync(await PostAsync(ctx, "001", SecondAccount, confirm: true, password: "not-my-password"),
            "Password is incorrect.");

        UpdatesFor(code).Should().Be(updatesBefore);
        var host = await HostRowAsync(ctx.HostId);
        host.SettlementBankCode.Should().Be("044");
        host.SettlementAccountLast4.Should().Be("4682");
        HostChangeEmails(ctx).Should().BeEmpty();
        AdminChangeEmails(ctx).Should().BeEmpty();
        (await ChangeNotificationsAsync(ctx.User.UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Lookup_on_existing_account_needs_no_password()
    {
        var ctx = await NewHostAsync("Lookup Host");
        var lookup = await PostAsync(ctx, "001", SecondAccount, confirm: false);
        lookup.StatusCode.Should().Be(HttpStatusCode.OK, await lookup.Content.ReadAsStringAsync());
        var body = await lookup.ReadJsonAsync();
        body.GetProperty("confirmed").GetBoolean().Should().BeFalse();
        body.GetProperty("accountLast4").GetString().Should().Be("5566");
        (await HostRowAsync(ctx.HostId)).SettlementAccountLast4.Should().Be("4682");
    }

    [Fact]
    public async Task Change_with_correct_password_updates_emails_host_and_admins_and_notifies()
    {
        var ctx = await NewHostAsync("Changing Host");
        var code = (await HostRowAsync(ctx.HostId)).PaystackSubaccountCode;
        var updatesBefore = UpdatesFor(code);

        var change = await PostAsync(ctx, "001", SecondAccount, confirm: true, password: AuthHelper.DefaultPassword);
        change.StatusCode.Should().Be(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());
        (await change.Content.ReadAsStringAsync()).Should().NotContain(SecondAccount);
        var body = await change.ReadJsonAsync();
        body.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        body.GetProperty("bankName").GetString().Should().Be("Test Bank");
        body.GetProperty("accountLast4").GetString().Should().Be("5566");
        body.GetProperty("profile").GetProperty("payoutAccount").GetProperty("accountLast4").GetString().Should().Be("5566");

        UpdatesFor(code).Should().Be(updatesBefore + 1);
        _factory.Paystack.UpdatedSubaccounts.Should().Contain(u => u.Code == code
            && u.Update.AccountNumber == SecondAccount && u.Update.SettlementBankCode == "001");
        var host = await HostRowAsync(ctx.HostId);
        host.SettlementBankCode.Should().Be("001");
        host.SettlementAccountLast4.Should().Be("5566");

        // Host email: new bank + last 4 + name, time in WAT, support line.
        var hostEmail = HostChangeEmails(ctx).Should().ContainSingle().Subject;
        string.Join("\n", hostEmail.BodyLines).Should()
            .Contain("Test Bank").And.Contain("5566").And.Contain(FakePaystackClient.ResolvedAccountName)
            .And.Contain("WAT").And.Contain("If this wasn't you, contact Hodrac support immediately");

        // In-app notification, already emailed (the digest skips it).
        var notification = (await ChangeNotificationsAsync(ctx.User.UserId)).Should().ContainSingle().Subject;
        notification.EmailedAt.Should().NotBeNull();

        // Every admin: host name + id, old -> new bank and last 4, time.
        var adminEmails = AdminChangeEmails(ctx);
        adminEmails.Select(e => e.To).Should().BeEquivalentTo(new[] { HodracApiFactory.AdminEmail, HodracApiFactory.SecondAdminEmail });
        foreach (var email in adminEmails)
        {
            string.Join("\n", email.BodyLines).Should()
                .Contain("Changing Host").And.Contain(ctx.HostId.ToString())
                .And.Contain("Access Bank").And.Contain("4682")
                .And.Contain("Test Bank").And.Contain("5566").And.Contain("WAT");
        }

        // No full account number in any email about this change.
        foreach (var email in HostChangeEmails(ctx).Concat(adminEmails))
        {
            var all = email.Subject + "\n" + string.Join("\n", email.BodyLines) + "\n" + email.DeepLink;
            all.Should().NotContain(FirstAccount).And.NotContain(SecondAccount);
        }
    }

    [Fact]
    public async Task Resubmitting_the_same_account_is_a_no_op()
    {
        var ctx = await NewHostAsync("Same Account Host");
        var code = (await HostRowAsync(ctx.HostId)).PaystackSubaccountCode;
        var updatesBefore = UpdatesFor(code);
        var createsBefore = _factory.Paystack.CreatedSubaccounts.Count(r => r.BusinessName == "Same Account Host");

        // No password needed: nothing changes.
        var again = await PostAsync(ctx, "044", FirstAccount, confirm: true);
        again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var body = await again.ReadJsonAsync();
        body.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        body.GetProperty("accountLast4").GetString().Should().Be("4682");
        body.GetProperty("profile").ValueKind.Should().Be(JsonValueKind.Object);

        UpdatesFor(code).Should().Be(updatesBefore);
        _factory.Paystack.CreatedSubaccounts.Count(r => r.BusinessName == "Same Account Host").Should().Be(createsBefore);
        HostChangeEmails(ctx).Should().BeEmpty();
        AdminChangeEmails(ctx).Should().BeEmpty();
        (await ChangeNotificationsAsync(ctx.User.UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Closed_host_is_still_403_even_with_the_right_password()
    {
        var ctx = await NewHostAsync("Closed Payout Host");
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        var adminClient = _factory.CreateClient().Authenticated(admin);
        (await adminClient.PostAsync($"/api/admin/hosts/{ctx.HostId}/close", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var change = await PostAsync(ctx, "001", SecondAccount, confirm: true, password: AuthHelper.DefaultPassword);
        change.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await change.Content.ReadAsStringAsync()).Should().Contain(HostClosure.HostMessage);
        (await HostRowAsync(ctx.HostId)).SettlementAccountLast4.Should().Be("4682");
        HostChangeEmails(ctx).Should().BeEmpty();
        AdminChangeEmails(ctx).Should().BeEmpty();
    }
}
