using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Infrastructure.Jobs;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostedFlow;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

/// <summary>
/// Part C: POST /api/host/trips/{id}/travellers/{interestId}/remove. Only an
/// approved traveller with an instalment unpaid past its due date can be
/// removed; payments are kept.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class RemoveTravellerTests
{
    private readonly HostedFlow _flow;

    public RemoveTravellerTests(HodracApiFactory factory) => _flow = new HostedFlow(factory);

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, Guid tripId, Guid interestId, string? reason = null) =>
        client.PostAsJsonAsync($"/api/host/trips/{tripId}/travellers/{interestId}/remove", new { reason });

    private static async Task<JsonElement> ManifestRowAsync(Seat seat)
    {
        var manifest = await GetJsonAsync(seat.Host, $"/api/host/trips/{seat.TripId}/manifest");
        return manifest.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("interestId").GetGuid() == seat.InterestId);
    }

    private static async Task ShouldConflictAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.ReadJsonAsync()).GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refused_when_nothing_is_overdue_or_only_the_deposit_is_unpaid()
    {
        // Deposit paid, instalments in the future.
        var onTime = await _flow.SeatAsync(HostSetTripBody());
        await _flow.PayDepositAsync(onTime);
        var response = await RemoveAsync(onTime.Host, onTime.TripId, onTime.InterestId);
        await ShouldConflictAsync(response);
        (await response.ReadJsonAsync()).GetProperty("error").GetString().Should().Be(TravellerRemovalService.NotRemovableMessage);
        (await ManifestRowAsync(onTime)).GetProperty("canRemove").GetBoolean().Should().BeFalse();

        // Deposit unpaid (seat hold running) even with an instalment date passed: the seat hold covers it.
        var noDeposit = await _flow.SeatAsync(HostSetTripBody());
        await _flow.SetHostDueDateAsync(noDeposit.TripId, 1, WatDay(-3));
        await ShouldConflictAsync(await RemoveAsync(noDeposit.Host, noDeposit.TripId, noDeposit.InterestId));
        (await ManifestRowAsync(noDeposit)).GetProperty("canRemove").GetBoolean().Should().BeFalse();

        // Paid in full with past dates.
        var paidUp = await _flow.SeatAsync(HostSetTripBody());
        await RecordOkAsync(paidUp.Host, paidUp.TripId, paidUp.InterestId, Price);
        await _flow.SetHostDueDateAsync(paidUp.TripId, 1, WatDay(-20));
        await _flow.SetHostDueDateAsync(paidUp.TripId, 2, WatDay(-10));
        await ShouldConflictAsync(await RemoveAsync(paidUp.Host, paidUp.TripId, paidUp.InterestId));

        // Due today isn't overdue yet.
        var dueToday = await _flow.SeatAsync(HostSetTripBody());
        await RecordOkAsync(dueToday.Host, dueToday.TripId, dueToday.InterestId, Deposit);
        await _flow.SetHostDueDateAsync(dueToday.TripId, 1, WatDay(0));
        await ShouldConflictAsync(await RemoveAsync(dueToday.Host, dueToday.TripId, dueToday.InterestId));

        // Not approved (still a request).
        var (host, hostUser, hostId) = await _flow.HostAsync("Request Host");
        var tripId = await CreateTripAsync(host, HostSetTripBody());
        var (requester, _) = await _flow.UserAsync("Requester");
        var requestId = await JoinAsync(requester, tripId);
        var notApproved = await RemoveAsync(host, tripId, requestId);
        await ShouldConflictAsync(notApproved);
        (await notApproved.ReadJsonAsync()).GetProperty("error").GetString().Should().Be(TravellerRemovalService.NotApprovedMessage);
    }

    [Fact]
    public async Task Is_404_for_non_owners_travellers_and_community_trips()
    {
        var seat = await _flow.SeatAsync(HostSetTripBody());
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, Deposit);
        await _flow.SetHostDueDateAsync(seat.TripId, 1, WatDay(-2));

        var (otherHost, _, _) = await _flow.HostAsync("Other Host");
        foreach (var client in new[] { otherHost, seat.Traveller })
            (await RemoveAsync(client, seat.TripId, seat.InterestId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RemoveAsync(seat.Host, seat.TripId, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Community trip.
        var (author, _) = await _flow.UserAsync("Community Author");
        var communityId = await CreateTripAsync(author, new
        {
            freeTextDestination = $"Comm-{Guid.NewGuid():N}",
            startDate = WatDay(30),
            endDate = WatDay(32),
            isDateFlexible = false,
            caption = "Community weekend",
            maxGroupSize = 6,
        });
        var (joiner, _) = await _flow.UserAsync("Community Joiner");
        var joinId = await JoinAsync(joiner, communityId);
        await ApproveAsync(author, communityId, joinId);
        (await RemoveAsync(author, communityId, joinId)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Reason too long.
        (await RemoveAsync(seat.Host, seat.TripId, seat.InterestId, new string('x', 451)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _flow.InterestAsync(seat.InterestId)).Status.Should().Be(TripInterestStatus.Approved);
    }

    // ── Removal ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HostSet_removal_frees_the_seat_and_chat_keeps_payments_and_tells_the_traveller()
    {
        var seat = await _flow.SeatAsync(HostSetTripBody(), "Late Payer");
        await _flow.PayDepositAsync(seat);
        (await _flow.InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeTrue();
        var before = await GetJsonAsync(seat.Host, $"/api/trips/{seat.TripId}");
        var seatsLeftBefore = before.GetProperty("seatsLeft").GetInt32();

        await _flow.SetHostDueDateAsync(seat.TripId, 1, WatDay(-3));
        var row = await ManifestRowAsync(seat);
        row.GetProperty("canRemove").GetBoolean().Should().BeTrue();

        var response = await RemoveAsync(seat.Host, seat.TripId, seat.InterestId, "  Missed the first instalment.  ");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("interestStatus").GetString().Should().Be("Removed");
        body.GetProperty("paymentStatus").GetString().Should().Be("DepositPaid");
        body.GetProperty("amountPaidKobo").GetInt64().Should().Be(Deposit);
        body.GetProperty("removedReason").GetString().Should().Be("Missed the first instalment.");
        var overdue = body.GetProperty("overdueInstalments");
        overdue.GetArrayLength().Should().Be(1);
        overdue[0].GetProperty("sequence").GetInt32().Should().Be(1);
        overdue[0].GetProperty("dueDate").GetString().Should().Be(WatDay(-3).ToString("yyyy-MM-dd"));

        // Interest, seat, chat, payments.
        var interest = await _flow.InterestAsync(seat.InterestId);
        interest.Status.Should().Be(TripInterestStatus.Removed);
        interest.RemovedAt.Should().NotBeNull();
        interest.RespondedAt.Should().Be(interest.RemovedAt);
        interest.AmountPaidKobo.Should().Be(Deposit);
        (await _flow.InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeFalse();
        (await _flow.InThreadAsync(seat.TripId, seat.HostUser.UserId)).Should().BeTrue();
        (await _flow.WithDbAsync(db => db.SeatPayments.CountAsync(p => p.TripInterestId == seat.InterestId && p.Status == SeatPaymentStatus.Success)))
            .Should().Be(1);
        var after = await GetJsonAsync(seat.Host, $"/api/trips/{seat.TripId}");
        after.GetProperty("seatsLeft").GetInt32().Should().Be(seatsLeftBefore + 1);

        // Notification + email.
        (await _flow.NotificationCountAsync(seat.TripId, NotificationType.RemovedForMissedPayment, seat.TravellerUser.UserId)).Should().Be(1);
        var email = _flow.EmailsTo(seat.TravellerUser.Email, "You've been removed from").Should().ContainSingle().Subject;
        email.BodyLines[0].Should().Contain("instalment 1").And.Contain("₦100,000");
        email.BodyLines.Should().Contain(l => l.Contains("Missed the first instalment."));
        var notification = await _flow.WithDbAsync(db => db.Notifications.AsNoTracking()
            .FirstAsync(n => n.TripPostId == seat.TripId && n.Type == NotificationType.RemovedForMissedPayment));
        notification.EmailedAt.Should().NotBeNull();

        // Visibility: manifest row, the traveller's own view.
        row = await ManifestRowAsync(seat);
        row.GetProperty("interestStatus").GetString().Should().Be("Removed");
        row.GetProperty("removedReason").GetString().Should().Be("Missed the first instalment.");
        row.GetProperty("canRemove").GetBoolean().Should().BeFalse();
        row.GetProperty("owesKobo").GetInt64().Should().Be(0);
        var mine = await GetJsonAsync(seat.Traveller, $"/api/trips/{seat.TripId}");
        mine.GetProperty("myInterestStatus").GetString().Should().Be("Removed");
        mine.GetProperty("myNextPayment").ValueKind.Should().Be(JsonValueKind.Null);

        // Checkout, request-again and a second removal are refused.
        (await seat.Traveller.PostAsJsonAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/checkout",
            new { kind = "Instalment", instalmentSequence = 1 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await seat.Traveller.PostAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/request-again", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await ShouldConflictAsync(await RemoveAsync(seat.Host, seat.TripId, seat.InterestId));

        // Reminders stop.
        await _flow.RunJobAsync<InstalmentReminderJob, int>(j => j.RunAsync(EightAmWat(WatDay(-1))));
        (await _flow.WithDbAsync(db => db.PaymentReminderLogs.CountAsync(r => r.TripInterestId == seat.InterestId))).Should().Be(0);

        // The host can still record a refund.
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, -1_000_000, "Refund");
    }

    [Fact]
    public async Task TravellerChooses_removal_uses_the_travellers_own_dates()
    {
        var seat = await _flow.SeatAsync(TravellerChoosesTripBody(), travellerDates: Dates((1, WatDay(10)), (2, WatDay(40))));
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, Deposit);

        await ShouldConflictAsync(await RemoveAsync(seat.Host, seat.TripId, seat.InterestId));
        (await ManifestRowAsync(seat)).GetProperty("canRemove").GetBoolean().Should().BeFalse();

        await _flow.SetTravellerDueDateAsync(seat.InterestId, 1, WatDay(-1));
        (await ManifestRowAsync(seat)).GetProperty("canRemove").GetBoolean().Should().BeTrue();

        var response = await RemoveAsync(seat.Host, seat.TripId, seat.InterestId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.ReadJsonAsync()).GetProperty("removedReason").ValueKind.Should().Be(JsonValueKind.Null);
        (await _flow.InterestAsync(seat.InterestId)).Status.Should().Be(TripInterestStatus.Removed);

        // A removed traveller can't move dates; they may send a fresh request.
        (await seat.Traveller.PutAsJsonAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}/instalment-dates",
            new { instalmentDates = Dates((1, WatDay(5)), (2, WatDay(40))) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await seat.Traveller.PostAsJsonAsync($"/api/trips/{seat.TripId}/interests", new { message = "Sorry, again?" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var interest = await _flow.InterestAsync(seat.InterestId);
        interest.Status.Should().Be(TripInterestStatus.Requested);
        interest.RemovedReason.Should().BeNull();
        (await _flow.TravellerDatesAsync(seat.InterestId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_payment_landing_after_removal_is_credited_and_flagged()
    {
        var seat = await _flow.SeatAsync(HostSetTripBody(), "Very Late Payer");
        await _flow.PayDepositAsync(seat);
        // The traveller opens a checkout for instalment 1, then the host removes them.
        var (reference, total) = await _flow.CheckoutAsync(seat, "Instalment", 1);
        await _flow.SetHostDueDateAsync(seat.TripId, 1, WatDay(-2));
        (await RemoveAsync(seat.Host, seat.TripId, seat.InterestId)).StatusCode.Should().Be(HttpStatusCode.OK);

        await _flow.PostChargeSuccessAsync(reference, total);

        var interest = await _flow.InterestAsync(seat.InterestId);
        interest.Status.Should().Be(TripInterestStatus.Removed);
        interest.AmountPaidKobo.Should().Be(Deposit + Instalment);
        (await _flow.InThreadAsync(seat.TripId, seat.TravellerUser.UserId)).Should().BeFalse();
        var alert = _flow.EmailsTo(seat.HostUser.Email, "needs attention").Should().ContainSingle().Subject;
        alert.BodyLines.Should().Contain(l => l.Contains("removed"));
    }
}
