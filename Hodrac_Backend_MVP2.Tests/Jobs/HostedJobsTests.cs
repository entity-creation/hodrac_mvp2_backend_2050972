using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Jobs;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Jobs;

/// <summary>
/// Phase 6 background jobs, run directly from a DI scope with a controlled
/// clock (the app's own schedulers are removed by the test factory):
/// instalment reminders, seat-hold expiry and warning, payment reconcile,
/// host weekly report and the 90-day document purge.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HostedJobsTests
{
    private const long Price = 25_000_000;   // ₦250,000
    private const long Deposit = 5_000_000;  // ₦50,000
    private const long Instalment = 10_000_000;

    private readonly HodracApiFactory _factory;

    public HostedJobsTests(HodracApiFactory factory) => _factory = factory;

    private FakePaystackClient Paystack => _factory.Paystack;
    private RecordingEmailSender Emails => _factory.Emails;

    // ── Setup helpers ────────────────────────────────────────────────────────

    private record Seat(
        HttpClient Host, TestUser HostUser, Guid HostId,
        HttpClient Traveller, TestUser TravellerUser,
        Guid TripId, Guid InterestId);

    private static DateOnly Day(int daysFromNow) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysFromNow));

    /// <summary>08:00 WAT (07:00 UTC) on the given WAT day.</summary>
    private static DateTime EightAmWat(DateOnly day) => DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(7, 0)), DateTimeKind.Utc);

    private async Task<T> WithDbAsync<T>(Func<HodracDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HodracDbContext>());
    }

    private async Task<TResult> RunJobAsync<TJob, TResult>(Func<TJob, Task<TResult>> run) where TJob : notnull
    {
        using var scope = _factory.Services.CreateScope();
        return await run(scope.ServiceProvider.GetRequiredService<TJob>());
    }

    private async Task<(HttpClient Client, TestUser User)> UserAsync(string name)
    {
        var user = await AuthHelper.RegisterAsync(_factory.CreateClient(), name);
        return (_factory.CreateClient().Authenticated(user), user);
    }

    private async Task<(HttpClient Client, TestUser User, Guid HostId)> HostAsync(string name)
    {
        var (client, user) = await UserAsync(name);
        var hostId = await CreatePendingHostAsync(client, name);
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        var verify = await _factory.CreateClient().Authenticated(admin).PostAsync($"/api/admin/hosts/{hostId}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        return (client, user, hostId);
    }

    private static object HostedTripBody(int maxGroupSize = 6) => new
    {
        freeTextDestination = $"Zanzibar-{Guid.NewGuid():N}",
        startDate = Day(90),
        endDate = Day(95),
        isDateFlexible = false,
        caption = "Zanzibar with the crew",
        maxGroupSize,
        isHostedTrip = true,
        pricePerSeatKobo = Price,
        depositKobo = Deposit,
        instalments = new[]
        {
            new { sequence = 1, amountKobo = Instalment, dueDate = Day(30) },
            new { sequence = 2, amountKobo = Instalment, dueDate = Day(60) },
        },
        feeMode = "PassToTraveller",
        termsText = "Deposit is non-refundable.",
    };

    private static async Task<Guid> CreateTripAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/trips", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> JoinAsync(HttpClient client, Guid tripId)
    {
        var join = await client.PostAsJsonAsync($"/api/trips/{tripId}/interests", new { message = "Me please" });
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
        return (await join.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient host, Guid tripId, Guid interestId) =>
        host.PatchAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}", new { approve = true });

    /// <summary>Verified host, hosted trip, traveller requested + approved + terms accepted.</summary>
    private async Task<Seat> ApprovedSeatAsync(int maxGroupSize = 6)
    {
        var (host, hostUser, hostId) = await HostAsync("Job Host");
        var tripId = await CreateTripAsync(host, HostedTripBody(maxGroupSize));
        var (traveller, travellerUser) = await UserAsync("Job Traveller");
        var interestId = await JoinAsync(traveller, tripId);

        var approve = await ApproveAsync(host, tripId, interestId);
        approve.StatusCode.Should().Be(HttpStatusCode.NoContent, await approve.Content.ReadAsStringAsync());

        var accept = await traveller.PostAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/accept-terms", new { termsVersion = 1 });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());

        return new Seat(host, hostUser, hostId, traveller, travellerUser, tripId, interestId);
    }

    private static async Task<(string Reference, long Total)> CheckoutAsync(Seat seat, string kind = "Deposit")
    {
        var response = await seat.Traveller.PostAsJsonAsync(
            $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/checkout", new { kind, instalmentSequence = (int?)null });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.ReadJsonAsync();
        return (json.GetProperty("reference").GetString()!, json.GetProperty("totalKobo").GetInt64());
    }

    private async Task PostChargeSuccessAsync(string reference, long amount)
    {
        var body = $"{{\"event\":\"charge.success\",\"data\":{{\"id\":4000000001,\"status\":\"success\",\"reference\":\"{reference}\",\"amount\":{amount},\"currency\":\"NGN\"}}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/paystack/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(PaystackSignature.HeaderName,
            PaystackSignature.ComputeHex(Encoding.UTF8.GetBytes(body), HodracApiFactory.PaystackSecretKey));
        var response = await _factory.CreateClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task PayDepositAsync(Seat seat)
    {
        var (reference, total) = await CheckoutAsync(seat);
        await PostChargeSuccessAsync(reference, total);
    }

    private Task<TripInterest> InterestAsync(Guid interestId) =>
        WithDbAsync(db => db.TripInterests.AsNoTracking().FirstAsync(i => i.Id == interestId));

    private Task<SeatPayment> PaymentAsync(string reference) =>
        WithDbAsync(db => db.SeatPayments.AsNoTracking().FirstAsync(p => p.Reference == reference));

    private Task<DateOnly> InstalmentDueAsync(Guid tripId, int sequence) =>
        WithDbAsync(async db => (await db.TripPosts.AsNoTracking().FirstAsync(t => t.Id == tripId))
            .Instalments.Single(i => i.Sequence == sequence).DueDate!.Value);

    private Task<int> NotificationCountAsync(Guid tripId, NotificationType type, string? recipientUserId = null) =>
        WithDbAsync(db => db.Notifications.CountAsync(n => n.TripPostId == tripId && n.Type == type
            && (recipientUserId == null || n.RecipientUserId == Guid.Parse(recipientUserId))));

    private List<RecordingEmailSender.SentEmail> EmailsTo(string email, string? subjectContains = null) =>
        Emails.Sent.Where(e => string.Equals(e.To, email, StringComparison.OrdinalIgnoreCase)
            && (subjectContains == null || e.Subject.Contains(subjectContains, StringComparison.Ordinal))).ToList();

    private Task SetInterestAsync(Guid interestId, InterestPaymentStatus status, long amountPaid) =>
        WithDbAsync(db => db.TripInterests.Where(i => i.Id == interestId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.PaymentStatus, status)
            .SetProperty(i => i.AmountPaidKobo, amountPaid)
            .SetProperty(i => i.SeatHoldExpiresAt, (DateTime?)null)));

    private Task<List<PaymentReminderLog>> RemindersAsync(Guid interestId) =>
        WithDbAsync(db => db.PaymentReminderLogs.AsNoTracking().Where(r => r.TripInterestId == interestId).ToListAsync());

    // ── InstalmentReminderJob ────────────────────────────────────────────────

    [Fact]
    public void Reminder_kind_follows_the_day_offset()
    {
        InstalmentReminderJob.KindFor(3).Should().Be(PaymentReminderKind.DueIn3Days);
        InstalmentReminderJob.KindFor(1).Should().Be(PaymentReminderKind.DueIn1Day);
        InstalmentReminderJob.KindFor(0).Should().Be(PaymentReminderKind.DueToday);
        InstalmentReminderJob.KindFor(-2).Should().Be(PaymentReminderKind.Overdue2Days);
        foreach (var other in new[] { 5, 4, 2, -1, -3, -10 })
            InstalmentReminderJob.KindFor(other).Should().BeNull();
    }

    [Fact]
    public async Task Instalment_reminders_fire_on_3_1_0_and_minus_2_days_once_each()
    {
        var seat = await ApprovedSeatAsync();
        await PayDepositAsync(seat);
        var due = await InstalmentDueAsync(seat.TripId, 1);
        var email = seat.TravellerUser.Email;

        Task<int> RunAt(int daysBeforeDue) =>
            RunJobAsync<InstalmentReminderJob, int>(j => j.RunAsync(EightAmWat(due.AddDays(-daysBeforeDue))));

        await RunAt(4);
        EmailsTo(email, "Instalment").Should().BeEmpty();

        await RunAt(3);
        await RunAt(3); // same day again: no duplicate
        EmailsTo(email, "due in 3 days").Should().HaveCount(1);

        await RunAt(2);
        await RunAt(1);
        EmailsTo(email, "due tomorrow").Should().HaveCount(1);

        await RunAt(0);
        await RunAt(-1);
        await RunAt(-2);
        await RunAt(-2);
        EmailsTo(email, "is overdue").Should().HaveCount(1);

        var sent = EmailsTo(email, "Instalment 1");
        sent.Select(e => e.Subject).Should().HaveCount(4);
        var dueToday = EmailsTo(email, "due today").Should().ContainSingle().Subject;
        dueToday.BodyLines[0].Should().Contain("Instalment 1 of ₦100,000");
        dueToday.BodyLines.Should().Contain(l => l.Contains("Total left to pay for your seat: ₦200,000"));
        dueToday.DeepLink.Should().EndWith($"/trips/{seat.TripId}");

        var logs = await RemindersAsync(seat.InterestId);
        logs.Select(l => l.Kind).Should().BeEquivalentTo(new[]
        {
            PaymentReminderKind.DueIn3Days, PaymentReminderKind.DueIn1Day,
            PaymentReminderKind.DueToday, PaymentReminderKind.Overdue2Days,
        });
        logs.Should().OnlyContain(l => l.InstalmentSequence == 1);
        (await NotificationCountAsync(seat.TripId, NotificationType.InstalmentDue, seat.TravellerUser.UserId)).Should().Be(4);
    }

    [Fact]
    public async Task Instalment_reminders_skip_paid_in_full_expired_cancelled_past_and_community_trips()
    {
        // Control: deposit paid, instalment 1 due tomorrow → reminded.
        var control = await ApprovedSeatAsync();
        await SetInterestAsync(control.InterestId, InterestPaymentStatus.DepositPaid, Deposit);

        var paidInFull = await ApprovedSeatAsync();
        await SetInterestAsync(paidInFull.InterestId, InterestPaymentStatus.PaidInFull, Price);

        var expired = await ApprovedSeatAsync();
        await SetInterestAsync(expired.InterestId, InterestPaymentStatus.Expired, 0);

        var cancelled = await ApprovedSeatAsync();
        await SetInterestAsync(cancelled.InterestId, InterestPaymentStatus.DepositPaid, Deposit);
        await WithDbAsync(db => db.TripPosts.Where(t => t.Id == cancelled.TripId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TripPostStatus.Cancelled)));

        var started = await ApprovedSeatAsync();
        await SetInterestAsync(started.InterestId, InterestPaymentStatus.DepositPaid, Deposit);
        await WithDbAsync(db => db.TripPosts.Where(t => t.Id == started.TripId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, (DateOnly?)Day(-1))));

        // Community trip with an approved traveller.
        var (author, _) = await UserAsync("Community Author");
        var communityTripId = await CreateTripAsync(author, new
        {
            freeTextDestination = $"Community-{Guid.NewGuid():N}",
            startDate = Day(60),
            endDate = Day(62),
            isDateFlexible = false,
            caption = "Weekend away",
            maxGroupSize = 6,
        });
        var (joiner, _) = await UserAsync("Community Joiner");
        var communityInterest = await JoinAsync(joiner, communityTripId);
        (await ApproveAsync(author, communityTripId, communityInterest)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var due = await InstalmentDueAsync(control.TripId, 1);
        await RunJobAsync<InstalmentReminderJob, int>(j => j.RunAsync(EightAmWat(due.AddDays(-1))));

        EmailsTo(control.TravellerUser.Email, "due tomorrow").Should().HaveCount(1);
        foreach (var skipped in new[] { paidInFull, expired, cancelled, started })
        {
            EmailsTo(skipped.TravellerUser.Email, "Instalment").Should().BeEmpty();
            (await RemindersAsync(skipped.InterestId)).Should().BeEmpty();
        }
        (await RemindersAsync(communityInterest)).Should().BeEmpty();
    }

    // ── SeatHoldExpiryJob ────────────────────────────────────────────────────

    private Task SetHoldAsync(Guid interestId, DateTime expiresAt) =>
        WithDbAsync(db => db.TripInterests.Where(i => i.Id == interestId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.SeatHoldExpiresAt, expiresAt)));

    [Fact]
    public async Task Seat_hold_expires_only_after_its_time_and_notifies_both_sides_once()
    {
        var seat = await ApprovedSeatAsync();

        await SetHoldAsync(seat.InterestId, DateTime.UtcNow.AddMinutes(5));
        await RunJobAsync<SeatHoldExpiryJob, SeatHoldExpiryJob.RunResult>(j => j.RunAsync(DateTime.UtcNow));
        (await InterestAsync(seat.InterestId)).PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);

        // Real clock: move the hold into the past.
        await SetHoldAsync(seat.InterestId, DateTime.UtcNow.AddMinutes(-5));
        await RunJobAsync<SeatHoldExpiryJob, SeatHoldExpiryJob.RunResult>(j => j.RunAsync(DateTime.UtcNow));
        await RunJobAsync<SeatHoldExpiryJob, SeatHoldExpiryJob.RunResult>(j => j.RunAsync(DateTime.UtcNow));

        var interest = await InterestAsync(seat.InterestId);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.Expired);
        interest.Status.Should().Be(TripInterestStatus.Approved);
        (await NotificationCountAsync(seat.TripId, NotificationType.SeatHoldExpired, seat.TravellerUser.UserId)).Should().Be(1);
        (await NotificationCountAsync(seat.TripId, NotificationType.SeatHoldExpired, seat.HostUser.UserId)).Should().Be(1);
        EmailsTo(seat.TravellerUser.Email, "has expired").Should().HaveCount(1);

        // An expired deposit can't be checked out.
        var checkout = await seat.Traveller.PostAsJsonAsync(
            $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/checkout", new { kind = "Deposit" });
        checkout.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Seat_hold_is_not_expired_when_the_deposit_was_paid_but_not_recorded()
    {
        var seat = await ApprovedSeatAsync();
        // Paid on Paystack (the fake verifies it as success), but the webhook never came.
        var checkout = await seat.Traveller.PostAsJsonAsync(
            $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/checkout", new { kind = "Deposit" });
        checkout.StatusCode.Should().Be(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync());

        await SetHoldAsync(seat.InterestId, DateTime.UtcNow.AddMinutes(-5));
        var result = await RunJobAsync<SeatHoldExpiryJob, SeatHoldExpiryJob.RunResult>(j => j.RunAsync(DateTime.UtcNow));

        result.Expired.Should().Be(0);
        var interest = await InterestAsync(seat.InterestId);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.DepositPaid);
        interest.SeatHoldExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Seat_hold_expiry_frees_capacity_for_a_new_approval()
    {
        var seat = await ApprovedSeatAsync(maxGroupSize: 2); // author + 1 seat
        var (other, _) = await UserAsync("Waiting Traveller");
        var otherInterest = await JoinAsync(other, seat.TripId);
        (await ApproveAsync(seat.Host, seat.TripId, otherInterest)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await SetHoldAsync(seat.InterestId, DateTime.UtcNow.AddMinutes(-1));
        await RunJobAsync<SeatHoldExpiryJob, SeatHoldExpiryJob.RunResult>(j => j.RunAsync(DateTime.UtcNow));

        (await ApproveAsync(seat.Host, seat.TripId, otherInterest)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Seat_hold_expiry_rechecks_and_never_overwrites_a_payment_that_just_landed()
    {
        var seat = await ApprovedSeatAsync();

        // The deposit lands after the job read its candidates (hold already
        // past) but before it locked the row.
        await PayDepositAsync(seat);
        await SetHoldAsync(seat.InterestId, DateTime.UtcNow.AddMinutes(-1)); // even if the hold were still set

        var expiredIt = await RunJobAsync<SeatHoldExpiryJob, bool>(j => j.TryExpireAsync(seat.TripId, seat.InterestId, DateTime.UtcNow));

        expiredIt.Should().BeFalse();
        var interest = await InterestAsync(seat.InterestId);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.DepositPaid);
        interest.AmountPaidKobo.Should().Be(Deposit);
        (await NotificationCountAsync(seat.TripId, NotificationType.SeatHoldExpired)).Should().Be(0);
    }

    [Fact]
    public async Task Seat_hold_warning_is_sent_once_within_12_hours_of_expiry()
    {
        var seat = await ApprovedSeatAsync();
        Task<SeatHoldExpiryJob.RunResult> RunAsync() =>
            RunJobAsync<SeatHoldExpiryJob, SeatHoldExpiryJob.RunResult>(j => j.RunAsync(DateTime.UtcNow));

        // 13h before: too early.
        await SetHoldAsync(seat.InterestId, DateTime.UtcNow.AddHours(13));
        await RunAsync();
        EmailsTo(seat.TravellerUser.Email, "ends soon").Should().BeEmpty();

        await SetHoldAsync(seat.InterestId, DateTime.UtcNow.AddHours(11));
        await RunAsync();
        await RunAsync();

        var warning = EmailsTo(seat.TravellerUser.Email, "ends soon").Should().ContainSingle().Subject;
        warning.BodyLines.Should().Contain(l => l.Contains("₦50,000 deposit"));
        warning.BodyLines[0].Should().Contain("WAT");
        (await NotificationCountAsync(seat.TripId, NotificationType.SeatHoldExpiring, seat.TravellerUser.UserId)).Should().Be(1);
        (await RemindersAsync(seat.InterestId)).Should().ContainSingle(r =>
            r.Kind == PaymentReminderKind.SeatHoldExpiring && r.InstalmentSequence == null);
        (await InterestAsync(seat.InterestId)).PaymentStatus.Should().Be(InterestPaymentStatus.AwaitingDeposit);
    }

    // ── PaymentReconcileJob ──────────────────────────────────────────────────

    private Task BackdateAsync(string reference, TimeSpan age) =>
        WithDbAsync(db => db.SeatPayments.Where(p => p.Reference == reference)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, DateTime.UtcNow - age)));

    private Task<PaymentReconcileJob.RunResult> ReconcileAsync() =>
        RunJobAsync<PaymentReconcileJob, PaymentReconcileJob.RunResult>(j => j.RunAsync(DateTime.UtcNow));

    [Fact]
    public async Task Reconcile_credits_a_missed_webhook_once()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total) = await CheckoutAsync(seat);
        await BackdateAsync(reference, TimeSpan.FromMinutes(25));

        await ReconcileAsync();
        await ReconcileAsync();
        await PostChargeSuccessAsync(reference, total); // the late webhook is a no-op

        (await PaymentAsync(reference)).Status.Should().Be(SeatPaymentStatus.Success);
        var interest = await InterestAsync(seat.InterestId);
        interest.AmountPaidKobo.Should().Be(Deposit);
        interest.PaymentStatus.Should().Be(InterestPaymentStatus.DepositPaid);
        (await NotificationCountAsync(seat.TripId, NotificationType.PaymentReceived)).Should().Be(1);
        EmailsTo(seat.TravellerUser.Email, "receipt").Should().HaveCount(1);
    }

    [Fact]
    public async Task Reconcile_after_the_webhook_already_applied_it_credits_nothing_more()
    {
        var seat = await ApprovedSeatAsync();
        var (reference, total) = await CheckoutAsync(seat);
        await BackdateAsync(reference, TimeSpan.FromMinutes(25));
        await PostChargeSuccessAsync(reference, total);

        // Even when the job races the webhook (it read the row while still
        // Initialized), ApplyAsync is idempotent on the reference.
        var verified = await Paystack.VerifyAsync(reference);
        var result = await RunJobAsync<PaymentService, ApplyResult>(p => p.ApplyAsync(verified));
        await ReconcileAsync();

        result.Outcome.Should().Be(ApplyOutcome.AlreadyApplied);
        (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(Deposit);
        (await NotificationCountAsync(seat.TripId, NotificationType.PaymentReceived)).Should().Be(1);
    }

    [Fact]
    public async Task Reconcile_marks_failed_abandoned_and_leaves_fresh_attempts_alone()
    {
        var failedSeat = await ApprovedSeatAsync();
        var (failedRef, failedTotal) = await CheckoutAsync(failedSeat);
        Paystack.VerifyResponses[failedRef] = _ => FakePaystackClient.Transaction(failedRef, "failed", failedTotal);
        await BackdateAsync(failedRef, TimeSpan.FromMinutes(25));

        var staleSeat = await ApprovedSeatAsync();
        var (staleRef, staleTotal) = await CheckoutAsync(staleSeat);
        Paystack.VerifyResponses[staleRef] = _ => FakePaystackClient.Transaction(staleRef, "abandoned", staleTotal);
        await BackdateAsync(staleRef, TimeSpan.FromHours(25));

        var pendingSeat = await ApprovedSeatAsync();
        var (pendingRef, pendingTotal) = await CheckoutAsync(pendingSeat);
        Paystack.VerifyResponses[pendingRef] = _ => FakePaystackClient.Transaction(pendingRef, "abandoned", pendingTotal);
        await BackdateAsync(pendingRef, TimeSpan.FromHours(2));

        var freshSeat = await ApprovedSeatAsync();
        var (freshRef, _) = await CheckoutAsync(freshSeat); // would verify "success" if checked

        var erroringSeat = await ApprovedSeatAsync();
        var (errorRef, _) = await CheckoutAsync(erroringSeat);
        Paystack.VerifyResponses[errorRef] = _ => throw new PaystackException("Paystack is down", HttpStatusCode.BadGateway);
        await BackdateAsync(errorRef, TimeSpan.FromMinutes(40));

        var result = await ReconcileAsync();

        result.Errors.Should().BeGreaterThanOrEqualTo(1);
        (await PaymentAsync(failedRef)).Status.Should().Be(SeatPaymentStatus.Failed);
        (await PaymentAsync(staleRef)).Status.Should().Be(SeatPaymentStatus.Abandoned);
        (await PaymentAsync(pendingRef)).Status.Should().Be(SeatPaymentStatus.Initialized);
        (await PaymentAsync(freshRef)).Status.Should().Be(SeatPaymentStatus.Initialized);
        (await PaymentAsync(errorRef)).Status.Should().Be(SeatPaymentStatus.Initialized);
        foreach (var seat in new[] { failedSeat, staleSeat, pendingSeat, freshSeat, erroringSeat })
            (await InterestAsync(seat.InterestId)).AmountPaidKobo.Should().Be(0);

        Paystack.VerifyResponses.TryRemove(errorRef, out _);
    }

    // ── HostWeeklyReportJob ──────────────────────────────────────────────────

    private static DateTime NextMondayEightAmWat()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)).AddDays(1);
        while (day.DayOfWeek != DayOfWeek.Monday) day = day.AddDays(1);
        return EightAmWat(day);
    }

    [Fact]
    public async Task Weekly_report_is_emailed_once_per_host_per_week_and_only_to_hosts_with_hosted_trips()
    {
        var seat = await ApprovedSeatAsync();
        await PayDepositAsync(seat);
        var (_, noTripsHost, _) = await HostAsync("Idle Host");

        // Move the payment into the reported week (the week before "Monday").
        var monday = NextMondayEightAmWat();
        await WithDbAsync(db => db.SeatPayments.Where(p => p.TripInterestId == seat.InterestId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaidAt, monday.AddDays(-3))));

        HostWeeklyReportJob.IsSendDay(monday.AddDays(3)).Should().BeFalse(); // Thursday
        (await RunJobAsync<HostWeeklyReportJob, int>(j => j.RunAsync(monday.AddDays(3)))).Should().Be(0);
        await RunJobAsync<HostWeeklyReportJob, int>(j => j.RunAsync(monday));
        await RunJobAsync<HostWeeklyReportJob, int>(j => j.RunAsync(monday.AddDays(1))); // catch-up day: already sent

        var report = EmailsTo(seat.HostUser.Email, "Your Hodrac week").Should().ContainSingle().Subject;
        report.DeepLink.Should().EndWith("/host");
        report.BodyLines.Should().Contain(l => l.Contains("₦50,000 collected (₦50,000 through Hodrac)"));
        report.BodyLines.Should().Contain(l => l.Contains("1 new paid seat"));
        EmailsTo(noTripsHost.Email, "Your Hodrac week").Should().BeEmpty();

        (await WithDbAsync(db => db.HostReportLogs.CountAsync(l => l.HostProfileId == seat.HostId))).Should().Be(1);
    }

    // ── HostDocumentPurgeJob ─────────────────────────────────────────────────

    [Fact]
    public async Task Documents_are_purged_only_90_days_after_the_decision()
    {
        var (_, _, hostId) = await HostAsync("Purge Host");
        Task<int> DocsAsync() => WithDbAsync(db => db.HostDocuments.CountAsync(d => d.HostProfileId == hostId));
        Task SetDecisionAsync(int daysAgo) => WithDbAsync(db => db.HostProfiles.Where(h => h.Id == hostId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.VerifiedAt, DateTime.UtcNow.AddDays(-daysAgo))));
        Task<HostDocumentPurgeJob.RunResult> PurgeAsync() =>
            RunJobAsync<HostDocumentPurgeJob, HostDocumentPurgeJob.RunResult>(j => j.RunAsync(DateTime.UtcNow));

        (await DocsAsync()).Should().BeGreaterThan(0);

        await PurgeAsync();
        (await DocsAsync()).Should().BeGreaterThan(0);

        await SetDecisionAsync(89);
        await PurgeAsync();
        (await DocsAsync()).Should().BeGreaterThan(0);

        await SetDecisionAsync(91);
        await PurgeAsync();
        await PurgeAsync(); // nothing left: no-op

        (await DocsAsync()).Should().Be(0);
        var host = await WithDbAsync(db => db.HostProfiles.AsNoTracking().FirstAsync(h => h.Id == hostId));
        host.IdDocumentStorageKey.Should().BeNull();
        host.VerificationStatus.Should().Be(HostVerificationStatus.Verified);
        host.VerifiedAt.Should().NotBeNull();
        host.VerifiedByUserId.Should().NotBeNull();
    }
}
