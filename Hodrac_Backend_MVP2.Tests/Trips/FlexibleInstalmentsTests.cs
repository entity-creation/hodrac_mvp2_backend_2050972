using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Infrastructure.Jobs;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Tests.Hosts;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostedFlow;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Trips;

/// <summary>
/// Part B: the host chooses who sets the instalment dates (HostSet or
/// TravellerChooses). Traveller dates are picked at accept-terms, moved with
/// PUT .../instalment-dates, and used by the balance, trip view, manifest,
/// WhatsApp nudge and reminder job.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class FlexibleInstalmentsTests
{
    private readonly HostedFlow _flow;

    public FlexibleInstalmentsTests(HodracApiFactory factory) => _flow = new HostedFlow(factory);

    private static async Task<JsonElement> ProblemErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        return (await response.ReadJsonAsync()).GetProperty("errors");
    }

    private static Task<HttpResponseMessage> ChangeDatesAsync(HttpClient client, Guid tripId, Guid interestId, object dates) =>
        client.PutAsJsonAsync($"/api/trips/{tripId}/interests/{interestId}/instalment-dates", new { instalmentDates = dates });

    private static Dictionary<string, object?> TravellerBody(Action<Dictionary<string, object?>> change)
    {
        var json = JsonSerializer.Serialize(TravellerChoosesTripBody());
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        change(body);
        return body;
    }

    // ── Create / edit validation ─────────────────────────────────────────────

    [Fact]
    public async Task Create_validates_the_dates_mode()
    {
        var (host, _, _) = await _flow.HostAsync("Mode Host");

        // TravellerChooses: no pay-in-full date.
        var errors = await ProblemErrorsAsync(await host.PostAsJsonAsync("/api/trips",
            TravellerBody(b => b.Remove("payInFullByDate"))));
        errors.TryGetProperty("payInFullByDate", out _).Should().BeTrue();

        // TravellerChooses: instalments sent with dates.
        errors = await ProblemErrorsAsync(await host.PostAsJsonAsync("/api/trips", TravellerBody(b => b["instalments"] = new[]
        {
            new { sequence = 1, amountKobo = Instalment, dueDate = WatDay(20) },
            new { sequence = 2, amountKobo = Instalment, dueDate = WatDay(40) },
        })));
        errors.TryGetProperty("instalments[0].dueDate", out _).Should().BeTrue();

        // Deadline on/after the start, or not in the future.
        errors = await ProblemErrorsAsync(await host.PostAsJsonAsync("/api/trips", TravellerBody(b => b["payInFullByDate"] = WatDay(90))));
        errors.GetProperty("payInFullByDate")[0].GetString().Should().Contain("before the trip starts");
        errors = await ProblemErrorsAsync(await host.PostAsJsonAsync("/api/trips", TravellerBody(b => b["payInFullByDate"] = WatDay(0))));
        errors.GetProperty("payInFullByDate")[0].GetString().Should().Contain("after today");

        // HostSet: a pay-in-full date is refused, and every instalment needs a date.
        errors = await ProblemErrorsAsync(await host.PostAsJsonAsync("/api/trips", TravellerBody(b => b["instalmentDatesMode"] = "HostSet")));
        errors.TryGetProperty("payInFullByDate", out _).Should().BeTrue();
        errors.TryGetProperty("instalments[0].dueDate", out _).Should().BeTrue();

        // Community trips can't carry the mode.
        var community = await host.PostAsJsonAsync("/api/trips", new
        {
            freeTextDestination = "Somewhere",
            caption = "Community",
            isDateFlexible = true,
            instalmentDatesMode = "TravellerChooses",
        });
        community.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Valid TravellerChooses trip: mode, deadline, undated instalments.
        var created = await host.PostAsJsonAsync("/api/trips", TravellerChoosesTripBody());
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var dto = await created.ReadJsonAsync();
        dto.GetProperty("instalmentDatesMode").GetString().Should().Be("TravellerChooses");
        dto.GetProperty("payInFullByDate").GetString().Should().Be(WatDay(80).ToString("yyyy-MM-dd"));
        dto.GetProperty("instalments").EnumerateArray().Should().OnlyContain(i => i.GetProperty("dueDate").ValueKind == JsonValueKind.Null);

        // HostSet trips report their mode too, with no deadline.
        var hostSet = await (await host.PostAsJsonAsync("/api/trips", HostSetTripBody())).ReadJsonAsync();
        hostSet.GetProperty("instalmentDatesMode").GetString().Should().Be("HostSet");
        hostSet.GetProperty("payInFullByDate").ValueKind.Should().Be(JsonValueKind.Null);
        var publicView = await GetJsonAsync(host, $"/api/trips/public/{dto.GetProperty("id").GetGuid()}");
        publicView.GetProperty("instalmentDatesMode").GetString().Should().Be("TravellerChooses");
    }

    [Fact]
    public async Task Edit_switches_mode_keeps_the_deadline_before_the_start_and_locks_after_payment()
    {
        var (host, _, _) = await _flow.HostAsync("Edit Mode Host");
        var tripId = await CreateTripAsync(host, HostSetTripBody());

        // HostSet → TravellerChooses without resending the schedule: the host's dates are dropped.
        var missingDeadline = await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { instalmentDatesMode = "TravellerChooses" });
        (await ProblemErrorsAsync(missingDeadline)).TryGetProperty("payInFullByDate", out _).Should().BeTrue();

        var switched = await host.PatchAsJsonAsync($"/api/trips/{tripId}",
            new { instalmentDatesMode = "TravellerChooses", payInFullByDate = WatDay(70) });
        switched.StatusCode.Should().Be(HttpStatusCode.NoContent, await switched.Content.ReadAsStringAsync());
        var trip = await GetJsonAsync(host, $"/api/trips/{tripId}");
        trip.GetProperty("instalmentDatesMode").GetString().Should().Be("TravellerChooses");
        trip.GetProperty("instalments").EnumerateArray().Should().OnlyContain(i => i.GetProperty("dueDate").ValueKind == JsonValueKind.Null);

        // A start-date edit must keep the deadline before the start.
        var tooEarlyStart = await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { startDate = WatDay(60) });
        (await ProblemErrorsAsync(tooEarlyStart)).TryGetProperty("payInFullByDate", out _).Should().BeTrue();

        // Back to HostSet needs dated instalments.
        var noDates = await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { instalmentDatesMode = "HostSet" });
        (await ProblemErrorsAsync(noDates)).TryGetProperty("instalments[0].dueDate", out _).Should().BeTrue();

        // Once someone has paid, the mode and the deadline are locked.
        var seat = await _flow.AddSeatAsync(host, null!, Guid.Empty, tripId, travellerChooses: true);
        await _flow.PayDepositAsync(seat);
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { payInFullByDate = WatDay(75) }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new
        {
            instalmentDatesMode = "HostSet",
            instalments = new[]
            {
                new { sequence = 1, amountKobo = Instalment, dueDate = WatDay(30) },
                new { sequence = 2, amountKobo = Instalment, dueDate = WatDay(60) },
            },
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        // Unrelated edits still work.
        (await host.PatchAsJsonAsync($"/api/trips/{tripId}", new { caption = "New caption" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Accept-terms ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_terms_requires_valid_dates_and_stores_the_snapshot()
    {
        var (host, _, _) = await _flow.HostAsync("Accept Host");
        var tripId = await CreateTripAsync(host, TravellerChoosesTripBody());
        var (traveller, _) = await _flow.UserAsync("Accept Traveller");
        var interestId = await JoinAsync(traveller, tripId);
        await ApproveAsync(host, tripId, interestId);

        var none = await AcceptTermsAsync(traveller, tripId, interestId);
        (await ProblemErrorsAsync(none)).TryGetProperty("instalmentDates", out _).Should().BeTrue();

        var pastDeadline = await AcceptTermsAsync(traveller, tripId, interestId, Dates((1, WatDay(20)), (2, WatDay(81))));
        (await ProblemErrorsAsync(pastDeadline)).TryGetProperty("instalmentDates[1].dueDate", out _).Should().BeTrue();

        var outOfOrder = await AcceptTermsAsync(traveller, tripId, interestId, Dates((1, WatDay(40)), (2, WatDay(40))));
        (await ProblemErrorsAsync(outOfOrder)).TryGetProperty("instalmentDates[1].dueDate", out _).Should().BeTrue();

        var past = await AcceptTermsAsync(traveller, tripId, interestId, Dates((1, WatDay(-1)), (2, WatDay(40))));
        (await ProblemErrorsAsync(past)).TryGetProperty("instalmentDates[0].dueDate", out _).Should().BeTrue();

        var unknown = await AcceptTermsAsync(traveller, tripId, interestId, Dates((1, WatDay(10)), (2, WatDay(40)), (3, WatDay(50))));
        (await ProblemErrorsAsync(unknown)).TryGetProperty("instalmentDates[2].sequence", out _).Should().BeTrue();

        (await _flow.InterestAsync(interestId)).TermsVersionAccepted.Should().BeNull();

        var ok = await AcceptTermsAsync(traveller, tripId, interestId, Dates((1, WatDay(20)), (2, WatDay(80))));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var body = await ok.ReadJsonAsync();
        body.GetProperty("instalmentDates").GetArrayLength().Should().Be(2);

        var interest = await _flow.InterestAsync(interestId);
        interest.TermsVersionAccepted.Should().Be(1);
        interest.TermsInstalmentDatesJson.Should().Be(
            $"[{{\"sequence\":1,\"dueDate\":\"{WatDay(20):yyyy-MM-dd}\"}},{{\"sequence\":2,\"dueDate\":\"{WatDay(80):yyyy-MM-dd}\"}}]");
        (await _flow.TravellerDatesAsync(interestId)).Select(d => (d.Sequence, d.DueDate))
            .Should().Equal((1, WatDay(20)), (2, WatDay(80)));
    }

    [Fact]
    public async Task HostSet_accept_terms_ignores_dates_and_stores_none()
    {
        var seat = await _flow.SeatAsync(HostSetTripBody(), travellerDates: Dates((1, WatDay(5)), (2, WatDay(6))));
        (await _flow.TravellerDatesAsync(seat.InterestId)).Should().BeEmpty();
        (await _flow.InterestAsync(seat.InterestId)).TermsInstalmentDatesJson.Should().BeNull();

        var balance = await GetJsonAsync(seat.Traveller, $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/balance");
        balance.GetProperty("instalmentDatesMode").GetString().Should().Be("HostSet");
        balance.GetProperty("canChangeDates").GetBoolean().Should().BeFalse();
        balance.GetProperty("schedule")[1].GetProperty("dueDate").GetString().Should().Be(WatDay(30).ToString("yyyy-MM-dd"));

        (await ChangeDatesAsync(seat.Traveller, seat.TripId, seat.InterestId, Dates((1, WatDay(10)), (2, WatDay(20)))))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Readers use the traveller's dates ────────────────────────────────────

    [Fact]
    public async Task Balance_trip_view_manifest_and_nudge_use_the_travellers_dates()
    {
        var seat = await _flow.SeatAsync(TravellerChoosesTripBody(), contactPhone: "08031234567",
            travellerDates: Dates((1, WatDay(25)), (2, WatDay(55))));

        var balance = await GetJsonAsync(seat.Traveller, $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/balance");
        balance.GetProperty("instalmentDatesMode").GetString().Should().Be("TravellerChooses");
        balance.GetProperty("payInFullByDate").GetString().Should().Be(WatDay(80).ToString("yyyy-MM-dd"));
        balance.GetProperty("datesChosenByTraveller").GetBoolean().Should().BeTrue();
        balance.GetProperty("canChangeDates").GetBoolean().Should().BeTrue();
        var schedule = balance.GetProperty("schedule");
        schedule[0].GetProperty("dueDate").ValueKind.Should().Be(JsonValueKind.Null); // deposit
        schedule[1].GetProperty("dueDate").GetString().Should().Be(WatDay(25).ToString("yyyy-MM-dd"));
        schedule[2].GetProperty("dueDate").GetString().Should().Be(WatDay(55).ToString("yyyy-MM-dd"));

        await _flow.PayDepositAsync(seat);

        var trip = await GetJsonAsync(seat.Traveller, $"/api/trips/{seat.TripId}");
        var next = trip.GetProperty("myNextPayment");
        next.GetProperty("sequence").GetInt32().Should().Be(1);
        next.GetProperty("dueDate").GetString().Should().Be(WatDay(25).ToString("yyyy-MM-dd"));

        var requests = await GetJsonAsync(seat.Traveller, "/api/trips/mine/requests");
        var mine = requests.EnumerateArray().Single(r => r.GetProperty("trip").GetProperty("id").GetGuid() == seat.TripId);
        mine.GetProperty("trip").GetProperty("myNextPayment").GetProperty("dueDate").GetString()
            .Should().Be(WatDay(25).ToString("yyyy-MM-dd"));

        var manifest = await GetJsonAsync(seat.Host, $"/api/host/trips/{seat.TripId}/manifest");
        var row = manifest.GetProperty("rows")[0];
        row.GetProperty("nextDueDate").GetString().Should().Be(WatDay(25).ToString("yyyy-MM-dd"));
        row.GetProperty("datesChosenByTraveller").GetBoolean().Should().BeTrue();

        var nudge = await GetJsonAsync(seat.Host, $"/api/host/trips/{seat.TripId}/whatsapp-nudge/{seat.InterestId}");
        nudge.GetProperty("message").GetString().Should().Contain(
            "due " + WatDay(25).ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Without_chosen_dates_instalments_fall_back_to_the_pay_in_full_date()
    {
        var seat = await _flow.SeatAsync(TravellerChoosesTripBody(), travellerChooses: true);
        await _flow.WithDbAsync(db => db.TripInterestInstalmentDates
            .Where(d => d.TripInterestId == seat.InterestId).ExecuteDeleteAsync());

        var balance = await GetJsonAsync(seat.Traveller, $"/api/trips/{seat.TripId}/interests/{seat.InterestId}/balance");
        balance.GetProperty("datesChosenByTraveller").GetBoolean().Should().BeFalse();
        balance.GetProperty("schedule")[1].GetProperty("dueDate").GetString().Should().Be(WatDay(80).ToString("yyyy-MM-dd"));
    }

    // ── Change-dates endpoint ────────────────────────────────────────────────

    [Fact]
    public async Task Change_dates_moves_unpaid_lines_only_in_order_up_to_the_deadline()
    {
        var seat = await _flow.SeatAsync(TravellerChoosesTripBody(), travellerDates: Dates((1, WatDay(20)), (2, WatDay(50))));
        // Instalment 1 paid by hand: deposit + instalment 1.
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, Deposit + Instalment);

        // Strangers and the host can't use it.
        var (stranger, _) = await _flow.UserAsync("Stranger");
        (await ChangeDatesAsync(stranger, seat.TripId, seat.InterestId, Dates((2, WatDay(60)))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ChangeDatesAsync(seat.Host, seat.TripId, seat.InterestId, Dates((2, WatDay(60)))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A paid instalment can't move; its current date may be sent back.
        var paidMoved = await ChangeDatesAsync(seat.Traveller, seat.TripId, seat.InterestId, Dates((1, WatDay(25)), (2, WatDay(60))));
        (await ProblemErrorsAsync(paidMoved)).TryGetProperty("instalmentDates[0].dueDate", out _).Should().BeTrue();

        // Past the deadline / before the paid line / missing the unpaid line.
        (await ChangeDatesAsync(seat.Traveller, seat.TripId, seat.InterestId, Dates((2, WatDay(81)))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ChangeDatesAsync(seat.Traveller, seat.TripId, seat.InterestId, Dates((1, WatDay(20)))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Reminder already sent for instalment 2: cleared when it moves.
        await _flow.WithDbAsync(async db =>
        {
            db.PaymentReminderLogs.Add(new PaymentReminderLog
            {
                TripInterestId = seat.InterestId, InstalmentSequence = 2, Kind = PaymentReminderKind.DueIn3Days,
                Channel = PaymentReminderLog.EmailChannel, SentAt = DateTime.UtcNow,
            });
            db.PaymentReminderLogs.Add(new PaymentReminderLog
            {
                TripInterestId = seat.InterestId, InstalmentSequence = 1, Kind = PaymentReminderKind.DueToday,
                Channel = PaymentReminderLog.EmailChannel, SentAt = DateTime.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        var moved = await ChangeDatesAsync(seat.Traveller, seat.TripId, seat.InterestId, Dates((1, WatDay(20)), (2, WatDay(70))));
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        var balance = await moved.ReadJsonAsync();
        balance.GetProperty("schedule")[2].GetProperty("dueDate").GetString().Should().Be(WatDay(70).ToString("yyyy-MM-dd"));

        (await _flow.TravellerDatesAsync(seat.InterestId)).Select(d => (d.Sequence, d.DueDate))
            .Should().Equal((1, WatDay(20)), (2, WatDay(70)));
        var logs = await _flow.WithDbAsync(db => db.PaymentReminderLogs.AsNoTracking()
            .Where(r => r.TripInterestId == seat.InterestId).ToListAsync());
        logs.Should().ContainSingle().Which.InstalmentSequence.Should().Be(1);
        (await _flow.NotificationCountAsync(seat.TripId, NotificationType.InstalmentDatesChanged, seat.HostUser.UserId)).Should().Be(1);

        // The accept-terms snapshot is evidence and doesn't change.
        (await _flow.InterestAsync(seat.InterestId)).TermsInstalmentDatesJson.Should().Contain(WatDay(50).ToString("yyyy-MM-dd"));

        // Sending the same dates again changes nothing and doesn't notify.
        (await ChangeDatesAsync(seat.Traveller, seat.TripId, seat.InterestId, Dates((2, WatDay(70)))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _flow.NotificationCountAsync(seat.TripId, NotificationType.InstalmentDatesChanged, seat.HostUser.UserId)).Should().Be(1);
    }

    [Fact]
    public async Task Change_dates_needs_an_approved_seat()
    {
        var seat = await _flow.SeatAsync(TravellerChoosesTripBody(), travellerChooses: true);
        (await seat.Traveller.DeleteAsync($"/api/trips/{seat.TripId}/interests/{seat.InterestId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ChangeDatesAsync(seat.Traveller, seat.TripId, seat.InterestId, Dates((1, WatDay(10)), (2, WatDay(20)))))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Reminders ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reminders_fire_on_the_travellers_dates()
    {
        var seat = await _flow.SeatAsync(TravellerChoosesTripBody(), travellerDates: Dates((1, WatDay(12)), (2, WatDay(40))));
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, Deposit);

        // Three days before the traveller's date (the pay-in-full date is far later).
        await _flow.RunJobAsync<InstalmentReminderJob, int>(j => j.RunAsync(EightAmWat(WatDay(9))));
        var sent = _flow.EmailsTo(seat.TravellerUser.Email, "due in 3 days");
        sent.Should().ContainSingle().Which.Subject.Should().Contain("Instalment 1");

        // Moving the date re-arms the reminder for the new date.
        (await ChangeDatesAsync(seat.Traveller, seat.TripId, seat.InterestId, Dates((1, WatDay(15)), (2, WatDay(40)))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await _flow.RunJobAsync<InstalmentReminderJob, int>(j => j.RunAsync(EightAmWat(WatDay(12))));
        _flow.EmailsTo(seat.TravellerUser.Email, "due in 3 days").Should().HaveCount(2);
    }

    // ── Resets ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_and_reapproval_after_a_terms_change_clear_the_dates()
    {
        var withdrawn = await _flow.SeatAsync(TravellerChoosesTripBody(), travellerChooses: true);
        (await _flow.TravellerDatesAsync(withdrawn.InterestId)).Should().HaveCount(2);
        (await withdrawn.Traveller.DeleteAsync($"/api/trips/{withdrawn.TripId}/interests/{withdrawn.InterestId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _flow.TravellerDatesAsync(withdrawn.InterestId)).Should().BeEmpty();

        // Expired hold + new terms version → re-approval clears acceptance and dates.
        var seat = await _flow.SeatAsync(TravellerChoosesTripBody(), travellerChooses: true);
        await _flow.WithDbAsync(db => db.TripInterests.Where(i => i.Id == seat.InterestId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PaymentStatus, InterestPaymentStatus.Expired)));
        (await seat.Host.PatchAsJsonAsync($"/api/trips/{seat.TripId}", new { termsText = "New terms." }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ApproveAsync(seat.Host, seat.TripId, seat.InterestId);

        var interest = await _flow.InterestAsync(seat.InterestId);
        interest.TermsVersionAccepted.Should().BeNull();
        interest.TermsInstalmentDatesJson.Should().BeNull();
        (await _flow.TravellerDatesAsync(seat.InterestId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_the_deadline_before_anyone_pays_clears_chosen_dates()
    {
        var seat = await _flow.SeatAsync(TravellerChoosesTripBody(), travellerChooses: true);
        (await seat.Host.PatchAsJsonAsync($"/api/trips/{seat.TripId}", new { payInFullByDate = WatDay(60) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _flow.TravellerDatesAsync(seat.InterestId)).Should().BeEmpty();

        // Converting to a community trip resets the mode.
        (await seat.Host.PatchAsJsonAsync($"/api/trips/{seat.TripId}", new { isHostedTrip = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var trip = await _flow.WithDbAsync(db => db.TripPosts.AsNoTracking().FirstAsync(t => t.Id == seat.TripId));
        trip.InstalmentDatesMode.Should().Be(InstalmentDatesMode.HostSet);
        trip.PayInFullByDate.Should().BeNull();
    }
}
