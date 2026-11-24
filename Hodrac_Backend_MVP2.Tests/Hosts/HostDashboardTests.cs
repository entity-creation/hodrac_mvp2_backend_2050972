using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Hosts;
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostedFlow;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

/// <summary>
/// Part A: the host's all-trips dashboard (GET /api/host/dashboard/summary,
/// /trips, /travellers and the two CSV exports).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HostDashboardTests
{
    private readonly HodracApiFactory _factory;
    private readonly HostedFlow _flow;

    public HostDashboardTests(HodracApiFactory factory)
    {
        _factory = factory;
        _flow = new HostedFlow(factory);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private record Place(Guid CountryId, Guid CityId, Guid DestinationId, string DestinationName, string CountryName);

    /// <summary>HostSet trip: deposit + two instalments due in 30 and 60 days.</summary>
    private static object TripBody(
        string feeMode = "PassToTraveller", Guid? destinationId = null, Guid? wishlistId = null,
        string? freeText = null, int startInDays = 90, int maxGroupSize = 6) => new
    {
        destinationId,
        wishlistId,
        freeTextDestination = destinationId is null && wishlistId is null ? freeText ?? $"Zanzibar-{Guid.NewGuid():N}" : null,
        startDate = WatDay(startInDays),
        endDate = WatDay(startInDays + 5),
        isDateFlexible = false,
        caption = "Dashboard trip",
        maxGroupSize,
        isHostedTrip = true,
        pricePerSeatKobo = Price,
        depositKobo = Deposit,
        instalments = new[]
        {
            new { sequence = 1, amountKobo = Instalment, dueDate = WatDay(30) },
            new { sequence = 2, amountKobo = Instalment, dueDate = WatDay(60) },
        },
        feeMode,
        termsText = "Deposit is non-refundable.",
    };

    private Task<Place> PlaceAsync(string tag) => _flow.WithDbAsync(async db =>
    {
        var country = new Country { CountryId = Guid.NewGuid(), CountryName = $"Dashland {tag}" };
        var city = new City { CityId = Guid.NewGuid(), CityName = $"Dash City {tag}", CountryId = country.CountryId };
        var destination = new Destination
        {
            DestinationId = Guid.NewGuid(),
            DestinationName = $"Dash Spot {tag}",
            CountryId = country.CountryId,
            TimeZone = "Africa/Lagos",
        };
        db.Countries.Add(country);
        db.Cities.Add(city);
        db.Destinations.Add(destination);
        db.DestinationCities.Add(new DestinationCity { DestinationId = destination.DestinationId, CityId = city.CityId });
        await db.SaveChangesAsync();
        return new Place(country.CountryId, city.CityId, destination.DestinationId, destination.DestinationName, country.CountryName);
    });

    private Task<Guid> WishlistAsync(string name, params Guid[] destinationIds) => _flow.WithDbAsync(async db =>
    {
        var wishlist = new Wishlist { WishlistId = Guid.NewGuid(), WishlistName = name, IsTemplate = true };
        db.Wishlists.Add(wishlist);
        foreach (var d in destinationIds)
            db.WishlistDestinations.Add(new WishlistDestination { WishlistId = wishlist.WishlistId, DestinationId = d });
        await db.SaveChangesAsync();
        return wishlist.WishlistId;
    });

    private Task SetTripStatusAsync(Guid tripId, TripPostStatus status) => _flow.WithDbAsync(db =>
        db.TripPosts.Where(t => t.Id == tripId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status)));

    private Task SetTripStartAsync(Guid tripId, DateOnly start) => _flow.WithDbAsync(db =>
        db.TripPosts.Where(t => t.Id == tripId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.StartDate, start)
            .SetProperty(t => t.EndDate, start.AddDays(2))));

    private Task SetInterestStatusAsync(Guid interestId, TripInterestStatus status) => _flow.WithDbAsync(db =>
        db.TripInterests.Where(i => i.Id == interestId).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, status)));

    private static async Task<List<JsonElement>> ItemsAsync(HttpClient client, string url)
    {
        var page = await GetJsonAsync(client, url);
        return page.GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task<List<Guid>> TripIdsAsync(HttpClient client, string query = "") =>
        (await ItemsAsync(client, $"/api/host/dashboard/trips?pageSize=100{query}"))
            .Select(t => t.GetProperty("id").GetGuid()).ToList();

    private static async Task<List<Guid>> TravellerIdsAsync(HttpClient client, string query = "") =>
        (await ItemsAsync(client, $"/api/host/dashboard/travellers?pageSize=100{query}"))
            .Select(t => t.GetProperty("interestId").GetGuid()).ToList();

    private static JsonElement Row(List<JsonElement> rows, string idProperty, Guid id) =>
        rows.Single(r => r.GetProperty(idProperty).GetGuid() == id);

    private static long Kobo(JsonElement e, string property) => e.GetProperty(property).GetInt64();

    private static string D(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ── Totals and fees ──────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_and_trip_rows_total_money_and_fees_across_trips()
    {
        var (host, hostUser, hostId) = await _flow.HostAsync("Money Host");
        var tripA = await CreateTripAsync(host, TripBody());                       // PassToTraveller
        var tripB = await CreateTripAsync(host, TripBody(feeMode: "HostAbsorbs")); // HostAbsorbs

        var a1 = await _flow.AddSeatAsync(host, hostUser, hostId, tripA, "Ada One");
        await _flow.PayDepositAsync(a1);                                                     // via Hodrac
        var a2 = await _flow.AddSeatAsync(host, hostUser, hostId, tripA, "Bola Two");
        await RecordOkAsync(host, tripA, a2.InterestId, Deposit + Instalment);               // manual
        var a3 = await _flow.AddSeatAsync(host, hostUser, hostId, tripA, "Chidi Three");
        await RecordOkAsync(host, tripA, a3.InterestId, Deposit);
        await SetInterestStatusAsync(a3.InterestId, TripInterestStatus.Withdrawn);           // money stays, owes nothing
        var b1 = await _flow.AddSeatAsync(host, hostUser, hostId, tripB, "Dayo Four");
        await _flow.PayDepositAsync(b1);
        await RecordOkAsync(host, tripB, b1.InterestId, -1_000_000, "Refund");                // manual refund ₦10,000

        await _flow.WithDbAsync(db => db.SeatPayments.Where(p => p.TripPostId == tripA)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaystackFeeKobo, 7_000L)));
        await _flow.WithDbAsync(db => db.SeatPayments.Where(p => p.TripPostId == tripB)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaystackFeeKobo, 9_000L)));

        var payments = await _flow.WithDbAsync(db => db.SeatPayments.AsNoTracking()
            .Where(p => (p.TripPostId == tripA || p.TripPostId == tripB) && p.Status == SeatPaymentStatus.Success)
            .ToListAsync());
        var payA = payments.Single(p => p.TripPostId == tripA);
        var payB = payments.Single(p => p.TripPostId == tripB);
        payA.TravellerFeeKobo.Should().Be(payA.HodracFeeKobo).And.BePositive();
        payB.TravellerFeeKobo.Should().Be(0);
        payB.HodracFeeKobo.Should().BePositive();

        var via = 2 * Deposit;
        var total = via + (Deposit + Instalment) + Deposit - 1_000_000;
        var owed = (Price - Deposit) + (Price - Deposit - Instalment) + (Price - (Deposit - 1_000_000));

        var summary = await GetJsonAsync(host, "/api/host/dashboard/summary");
        summary.GetProperty("trips").GetInt32().Should().Be(2);
        summary.GetProperty("seatsTotal").GetInt32().Should().Be(12);
        summary.GetProperty("seatsSold").GetInt32().Should().Be(3);
        summary.GetProperty("seatsWithDeposit").GetInt32().Should().Be(3);
        summary.GetProperty("seatsPaidInFull").GetInt32().Should().Be(0);
        Kobo(summary, "collectedViaHodracKobo").Should().Be(via);
        Kobo(summary, "collectedManualKobo").Should().Be(total - via);
        Kobo(summary, "collectedTotalKobo").Should().Be(total);
        Kobo(summary, "owedKobo").Should().Be(owed);
        Kobo(summary, "overdueKobo").Should().Be(0);
        summary.GetProperty("overdueTravellers").GetInt32().Should().Be(0);
        summary.GetProperty("timeZone").GetString().Should().Be("WAT");

        var fees = summary.GetProperty("fees");
        Kobo(fees, "hodracFeeTravellerPaidKobo").Should().Be(payA.TravellerFeeKobo);
        Kobo(fees, "hodracFeeHostAbsorbedKobo").Should().Be(payB.HodracFeeKobo);
        Kobo(fees, "paystackFeeKobo").Should().Be(16_000);
        Kobo(fees, "refundedViaHodracKobo").Should().Be(0);
        Kobo(fees, "refundedManualKobo").Should().Be(1_000_000);
        Kobo(fees, "refundedTotalKobo").Should().Be(1_000_000);
        Kobo(fees, "estimatedNetToHostKobo").Should().Be(via - payB.HodracFeeKobo - 16_000);

        // Trip rows: the trips-list fields plus fees per trip.
        var rows = await ItemsAsync(host, "/api/host/dashboard/trips");
        var list = (await GetJsonAsync(host, "/api/host/trips")).EnumerateArray().ToList();
        foreach (var tripId in new[] { tripA, tripB })
        {
            var row = Row(rows, "id", tripId);
            var listed = Row(list, "id", tripId);
            foreach (var field in new[] { "approved", "seatsWithDeposit", "seatsPaidInFull", "seatsTotal" })
                row.GetProperty(field).GetInt32().Should().Be(listed.GetProperty(field).GetInt32(), field);
            foreach (var field in new[] { "collectedKobo", "collectedManualKobo", "collectedTotalKobo", "owedKobo", "pricePerSeatKobo" })
                Kobo(row, field).Should().Be(Kobo(listed, field), field);
            row.GetProperty("title").GetString().Should().Be(listed.GetProperty("title").GetString());
            row.GetProperty("currency").GetString().Should().Be("NGN");
        }

        rows.Count(r => r.GetProperty("isNextDeparture").GetBoolean()).Should().Be(1);
        var rowA = Row(rows, "id", tripA);
        rowA.GetProperty("approved").GetInt32().Should().Be(2);
        Kobo(rowA, "collectedTotalKobo").Should().Be(Deposit + (Deposit + Instalment) + Deposit);
        rowA.GetProperty("feeMode").GetString().Should().Be("PassToTraveller");
        Kobo(rowA.GetProperty("fees"), "hodracFeeTravellerPaidKobo").Should().Be(payA.TravellerFeeKobo);
        Kobo(rowA.GetProperty("fees"), "hodracFeeHostAbsorbedKobo").Should().Be(0);
        Kobo(rowA.GetProperty("fees"), "estimatedNetToHostKobo").Should().Be(Deposit - 7_000);
        rowA.GetProperty("nextDueDate").GetString().Should().Be(D(WatDay(30)));

        var rowB = Row(rows, "id", tripB);
        rowB.GetProperty("feeMode").GetString().Should().Be("HostAbsorbs");
        Kobo(rowB.GetProperty("fees"), "hodracFeeTravellerPaidKobo").Should().Be(0);
        Kobo(rowB.GetProperty("fees"), "hodracFeeHostAbsorbedKobo").Should().Be(payB.HodracFeeKobo);
        Kobo(rowB.GetProperty("fees"), "refundedManualKobo").Should().Be(1_000_000);
        Kobo(rowB.GetProperty("fees"), "estimatedNetToHostKobo").Should().Be(Deposit - payB.HodracFeeKobo - 9_000);

        // Travellers: the withdrawn traveller keeps their money and owes nothing.
        var travellers = await ItemsAsync(host, "/api/host/dashboard/travellers?pageSize=100");
        travellers.Should().HaveCount(4);
        var withdrawn = Row(travellers, "interestId", a3.InterestId);
        withdrawn.GetProperty("interestStatus").GetString().Should().Be("Withdrawn");
        Kobo(withdrawn, "paidKobo").Should().Be(Deposit);
        Kobo(withdrawn, "owesKobo").Should().Be(0);
        withdrawn.GetProperty("nextDueDate").ValueKind.Should().Be(JsonValueKind.Null);
        var ada = Row(travellers, "interestId", a1.InterestId);
        ada.GetProperty("travellerName").GetString().Should().Be("Ada One");
        ada.GetProperty("email").GetString().Should().Be(a1.TravellerUser.Email);
        Kobo(ada, "viaHodracKobo").Should().Be(Deposit);
        Kobo(ada, "nextDueAmountKobo").Should().Be(Instalment);
        ada.GetProperty("nextDueKind").GetString().Should().Be("Instalment");
        ada.GetProperty("instalmentDatesMode").GetString().Should().Be("HostSet");
    }

    // ── Overdue ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overdue_counts_unpaid_instalments_past_due_in_both_date_modes()
    {
        var (host, hostUser, hostId) = await _flow.HostAsync("Overdue Host");

        // HostSet trip, instalment 1 moved 5 days into the past.
        var hostSet = await CreateTripAsync(host, TripBody());
        var late = await _flow.AddSeatAsync(host, hostUser, hostId, hostSet, "Late Payer");
        await RecordOkAsync(host, hostSet, late.InterestId, Deposit);
        var noDeposit = await _flow.AddSeatAsync(host, hostUser, hostId, hostSet, "No Deposit");
        var paidUp = await _flow.AddSeatAsync(host, hostUser, hostId, hostSet, "Paid Up");
        await RecordOkAsync(host, hostSet, paidUp.InterestId, Price);
        await _flow.SetHostDueDateAsync(hostSet, 1, WatDay(-5));

        // TravellerChooses trip, the traveller's own instalment 1 two days late.
        var chooses = await CreateTripAsync(host, TravellerChoosesTripBody());
        var own = await _flow.AddSeatAsync(host, hostUser, hostId, chooses, "Own Dates", travellerChooses: true);
        await RecordOkAsync(host, chooses, own.InterestId, Deposit);
        await _flow.SetTravellerDueDateAsync(own.InterestId, 1, WatDay(-2));

        // Cancelled trip: nothing owed or overdue.
        var cancelled = await CreateTripAsync(host, TripBody());
        var gone = await _flow.AddSeatAsync(host, hostUser, hostId, cancelled, "Cancelled Seat");
        await RecordOkAsync(host, cancelled, gone.InterestId, Deposit);
        await _flow.SetHostDueDateAsync(cancelled, 1, WatDay(-5));
        await SetTripStatusAsync(cancelled, TripPostStatus.Cancelled);

        var summary = await GetJsonAsync(host, "/api/host/dashboard/summary");
        Kobo(summary, "overdueKobo").Should().Be(3 * Instalment);
        summary.GetProperty("overdueTravellers").GetInt32().Should().Be(3);
        summary.GetProperty("seatsSold").GetInt32().Should().Be(4);   // the cancelled trip's seat isn't sold
        Kobo(summary, "owedKobo").Should().Be((Price - Deposit) + Price + (Price - Deposit));

        var trips = await ItemsAsync(host, "/api/host/dashboard/trips");
        Kobo(Row(trips, "id", hostSet), "overdueKobo").Should().Be(2 * Instalment);
        Row(trips, "id", hostSet).GetProperty("overdueTravellers").GetInt32().Should().Be(2);
        Kobo(Row(trips, "id", chooses), "overdueKobo").Should().Be(Instalment);
        Row(trips, "id", chooses).GetProperty("nextDueDate").GetString().Should().Be(D(WatDay(-2)));
        Row(trips, "id", chooses).GetProperty("instalmentDatesMode").GetString().Should().Be("TravellerChooses");
        Kobo(Row(trips, "id", cancelled), "owedKobo").Should().Be(0);
        Kobo(Row(trips, "id", cancelled), "overdueKobo").Should().Be(0);
        Kobo(Row(trips, "id", cancelled), "collectedTotalKobo").Should().Be(Deposit);

        var travellers = await ItemsAsync(host, "/api/host/dashboard/travellers?pageSize=100");
        var lateRow = Row(travellers, "interestId", late.InterestId);
        Kobo(lateRow, "overdueKobo").Should().Be(Instalment);
        lateRow.GetProperty("overdueInstalments").GetInt32().Should().Be(1);
        lateRow.GetProperty("daysOverdue").GetInt32().Should().Be(5);
        lateRow.GetProperty("nextDueDate").GetString().Should().Be(D(WatDay(-5)));
        lateRow.GetProperty("canRemove").GetBoolean().Should().BeTrue();
        lateRow.GetProperty("datesChosenByTraveller").GetBoolean().Should().BeFalse();

        var noDepositRow = Row(travellers, "interestId", noDeposit.InterestId);
        Kobo(noDepositRow, "overdueKobo").Should().Be(Instalment);           // the instalment is past due
        noDepositRow.GetProperty("canRemove").GetBoolean().Should().BeFalse(); // but the seat hold covers the deposit
        noDepositRow.GetProperty("nextDueKind").GetString().Should().Be("Deposit");

        Kobo(Row(travellers, "interestId", paidUp.InterestId), "overdueKobo").Should().Be(0);
        Row(travellers, "interestId", paidUp.InterestId).GetProperty("daysOverdue").ValueKind.Should().Be(JsonValueKind.Null);

        var ownRow = Row(travellers, "interestId", own.InterestId);
        Kobo(ownRow, "overdueKobo").Should().Be(Instalment);
        ownRow.GetProperty("daysOverdue").GetInt32().Should().Be(2);
        ownRow.GetProperty("datesChosenByTraveller").GetBoolean().Should().BeTrue();
        ownRow.GetProperty("canRemove").GetBoolean().Should().BeTrue();
        ownRow.GetProperty("instalmentDatesMode").GetString().Should().Be("TravellerChooses");

        var goneRow = Row(travellers, "interestId", gone.InterestId);
        Kobo(goneRow, "owesKobo").Should().Be(0);
        Kobo(goneRow, "overdueKobo").Should().Be(0);
        goneRow.GetProperty("tripStatus").GetString().Should().Be("Cancelled");

        // The manifest carries the same overdue amount per row and in total.
        var manifest = await GetJsonAsync(host, $"/api/host/trips/{hostSet}/manifest");
        var manifestRows = manifest.GetProperty("rows").EnumerateArray().ToList();
        Kobo(Row(manifestRows, "interestId", late.InterestId), "overdueKobo").Should().Be(Instalment);
        Kobo(Row(manifestRows, "interestId", paidUp.InterestId), "overdueKobo").Should().Be(0);
        Kobo(manifest.GetProperty("totals"), "overdueKobo").Should().Be(2 * Instalment);
        var csv = Encoding.UTF8.GetString(await (await host.GetAsync($"/api/host/trips/{hostSet}/manifest.csv")).Content.ReadAsByteArrayAsync());
        csv.Split("\r\n").Should().Contain(l => l.Contains("Late Payer") && l.EndsWith(",100000.00"));

        // Removing the late traveller: Removed with the reason, money kept, nothing owed or overdue.
        var remove = await host.PostAsJsonAsync($"/api/host/trips/{hostSet}/travellers/{late.InterestId}/remove", new { reason = "Missed instalment 1" });
        remove.StatusCode.Should().Be(HttpStatusCode.OK, await remove.Content.ReadAsStringAsync());
        var removed = Row(await ItemsAsync(host, "/api/host/dashboard/travellers?pageSize=100"), "interestId", late.InterestId);
        removed.GetProperty("interestStatus").GetString().Should().Be("Removed");
        removed.GetProperty("removedReason").GetString().Should().Be("Missed instalment 1");
        removed.GetProperty("removedAt").ValueKind.Should().Be(JsonValueKind.String);
        removed.GetProperty("canRemove").GetBoolean().Should().BeFalse();
        Kobo(removed, "paidKobo").Should().Be(Deposit);
        Kobo(removed, "owesKobo").Should().Be(0);
        Kobo(removed, "overdueKobo").Should().Be(0);
        var after = await GetJsonAsync(host, "/api/host/dashboard/summary");
        Kobo(after, "overdueKobo").Should().Be(2 * Instalment);
        Kobo(after, "collectedTotalKobo").Should().Be(Kobo(summary, "collectedTotalKobo"));
    }

    // ── Filters ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_filter_narrows_trips_travellers_and_summary()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var p1 = await PlaceAsync($"{tag}-a");
        var p2 = await PlaceAsync($"{tag}-b");
        var p3 = await PlaceAsync($"{tag}-c");
        var wishlistId = await WishlistAsync($"Loop {tag}", p3.DestinationId, p2.DestinationId);

        var (host, hostUser, hostId) = await _flow.HostAsync("Filter Host");
        var dest = await CreateTripAsync(host, TripBody(destinationId: p1.DestinationId, startInDays: 90));
        var wish = await CreateTripAsync(host, TripBody(wishlistId: wishlistId, startInDays: 100));
        var free = await CreateTripAsync(host, TripBody(freeText: $"Lagos weekend Zq{tag}", startInDays: 80));
        await SetTripStatusAsync(free, TripPostStatus.Closed);
        var past = await CreateTripAsync(host, TripBody(freeText: $"Old trip {tag}", startInDays: 75));
        await SetTripStartAsync(past, WatDay(-10));

        var destPaid = await _flow.AddSeatAsync(host, hostUser, hostId, dest, "Dest Deposit");
        await RecordOkAsync(host, dest, destPaid.InterestId, Deposit);
        var destWaiting = await _flow.AddSeatAsync(host, hostUser, hostId, dest, "Dest Waiting");
        var wishFull = await _flow.AddSeatAsync(host, hostUser, hostId, wish, $"Wish Full {tag}");
        await RecordOkAsync(host, wish, wishFull.InterestId, Price);

        (await TripIdsAsync(host)).Should().BeEquivalentTo(new[] { dest, wish, free, past });
        (await TripIdsAsync(host, $"&tripId={dest}")).Should().Equal(dest);
        (await TripIdsAsync(host, "&status=closed")).Should().Equal(free);
        (await TripIdsAsync(host, "&upcoming=true")).Should().BeEquivalentTo(new[] { dest, wish, free });
        (await TripIdsAsync(host, "&upcoming=false")).Should().Equal(past);
        (await TripIdsAsync(host, $"&destinationId={p1.DestinationId}")).Should().Equal(dest);
        (await TripIdsAsync(host, $"&destinationId={p2.DestinationId}")).Should().Equal(wish);
        (await TripIdsAsync(host, $"&cityId={p1.CityId}")).Should().Equal(dest);
        (await TripIdsAsync(host, $"&cityId={p3.CityId}")).Should().Equal(wish);
        (await TripIdsAsync(host, $"&countryId={p1.CountryId}")).Should().Equal(dest);
        (await TripIdsAsync(host, $"&countryId={p2.CountryId}")).Should().Equal(wish);
        (await TripIdsAsync(host, $"&countryId={Guid.NewGuid()}")).Should().BeEmpty();
        (await TripIdsAsync(host, $"&startFrom={D(WatDay(85))}&startTo={D(WatDay(95))}")).Should().Equal(dest);
        (await TripIdsAsync(host, $"&startFrom={D(WatDay(85))}")).Should().BeEquivalentTo(new[] { dest, wish });
        (await TripIdsAsync(host, $"&search=zq{tag}")).Should().Equal(free);
        (await TripIdsAsync(host, $"&search={Uri.EscapeDataString(p2.DestinationName)}")).Should().Equal(wish); // a wishlist stop
        (await TripIdsAsync(host, $"&status=Open&countryId={p3.CountryId}&upcoming=true")).Should().Equal(wish);

        // Location label and ids.
        var rows = await ItemsAsync(host, "/api/host/dashboard/trips");
        var destRow = Row(rows, "id", dest);
        destRow.GetProperty("locationLabel").GetString().Should().Be($"{p1.DestinationName}, {p1.CountryName}");
        destRow.GetProperty("destinationId").GetGuid().Should().Be(p1.DestinationId);
        var wishRow = Row(rows, "id", wish);
        wishRow.GetProperty("locationLabel").GetString().Should().Be($"{p2.DestinationName} · {p3.DestinationName}");
        wishRow.GetProperty("wishlistId").GetGuid().Should().Be(wishlistId);
        wishRow.GetProperty("destinationIds").EnumerateArray().Select(e => e.GetGuid())
            .Should().Equal(p2.DestinationId, p3.DestinationId);
        Row(rows, "id", free).GetProperty("locationLabel").GetString().Should().Be($"Lagos weekend Zq{tag}");

        // Invalid filters.
        (await host.GetAsync("/api/host/dashboard/trips?status=Bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.GetAsync("/api/host/dashboard/trips?status=1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.GetAsync($"/api/host/dashboard/trips?startFrom={D(WatDay(10))}&startTo={D(WatDay(5))}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.GetAsync($"/api/host/dashboard/summary?search={new string('x', 201)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.GetAsync("/api/host/dashboard/travellers?paymentStatus=Nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.GetAsync("/api/host/dashboard/travellers?interestStatus=Nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Travellers: shared filters plus their own.
        (await TravellerIdsAsync(host)).Should().BeEquivalentTo(new[] { destPaid.InterestId, destWaiting.InterestId, wishFull.InterestId });
        (await TravellerIdsAsync(host, $"&countryId={p1.CountryId}")).Should().BeEquivalentTo(new[] { destPaid.InterestId, destWaiting.InterestId });
        (await TravellerIdsAsync(host, $"&cityId={p2.CityId}")).Should().Equal(wishFull.InterestId);
        (await TravellerIdsAsync(host, "&paymentStatus=paidInFull")).Should().Equal(wishFull.InterestId);
        (await TravellerIdsAsync(host, "&paymentStatus=AwaitingDeposit")).Should().Equal(destWaiting.InterestId);
        (await TravellerIdsAsync(host, "&owing=true")).Should().BeEquivalentTo(new[] { destPaid.InterestId, destWaiting.InterestId });
        (await TravellerIdsAsync(host, "&owing=false")).Should().Equal(wishFull.InterestId);
        (await TravellerIdsAsync(host, "&overdue=true")).Should().BeEmpty();
        (await TravellerIdsAsync(host, "&overdue=false")).Should().HaveCount(3);
        (await TravellerIdsAsync(host, "&interestStatus=Approved")).Should().HaveCount(3);
        (await TravellerIdsAsync(host, $"&search={Uri.EscapeDataString($"wish full {tag}")}")).Should().Equal(wishFull.InterestId);           // name
        (await TravellerIdsAsync(host, $"&search={Uri.EscapeDataString(destPaid.TravellerUser.Email)}")).Should().Equal(destPaid.InterestId);
        (await TravellerIdsAsync(host, $"&search={Uri.EscapeDataString(p1.DestinationName)}"))                    // trip title
            .Should().BeEquivalentTo(new[] { destPaid.InterestId, destWaiting.InterestId });

        // Summary follows the same filters.
        var summary = await GetJsonAsync(host, $"/api/host/dashboard/summary?countryId={p2.CountryId}");
        summary.GetProperty("trips").GetInt32().Should().Be(1);
        Kobo(summary, "collectedTotalKobo").Should().Be(Price);
        Kobo(summary, "owedKobo").Should().Be(0);
        var destSummary = await GetJsonAsync(host, $"/api/host/dashboard/summary?destinationId={p1.DestinationId}");
        destSummary.GetProperty("seatsSold").GetInt32().Should().Be(2);
        Kobo(destSummary, "owedKobo").Should().Be((Price - Deposit) + Price);
        (await GetJsonAsync(host, "/api/host/dashboard/summary?status=Closed")).GetProperty("trips").GetInt32().Should().Be(1);
    }

    // ── Paging and sorting ───────────────────────────────────────────────────

    [Fact]
    public async Task Trips_and_travellers_page_and_sort()
    {
        var (host, hostUser, hostId) = await _flow.HostAsync("Paging Host");
        var t1 = await CreateTripAsync(host, TripBody(startInDays: 70));
        var t2 = await CreateTripAsync(host, TripBody(startInDays: 80));
        var t3 = await CreateTripAsync(host, TripBody(startInDays: 90, maxGroupSize: 4));

        var s1 = await _flow.AddSeatAsync(host, hostUser, hostId, t1, "Sola First");
        await RecordOkAsync(host, t1, s1.InterestId, Deposit);
        var s2 = await _flow.AddSeatAsync(host, hostUser, hostId, t2, "Tobi Second");
        var s3 = await _flow.AddSeatAsync(host, hostUser, hostId, t2, "Uche Third");

        (await TripIdsAsync(host)).Should().Equal(t1, t2, t3);                          // start date (default)
        (await TripIdsAsync(host, "&sort=startDate")).Should().Equal(t1, t2, t3);
        (await TripIdsAsync(host, "&sort=owed")).Should().Equal(t2, t1, t3);
        (await TripIdsAsync(host, "&sort=collected")).Should().Equal(t1, t2, t3);

        var first = await GetJsonAsync(host, "/api/host/dashboard/trips?sort=owed&page=1&pageSize=2");
        first.GetProperty("page").GetInt32().Should().Be(1);
        first.GetProperty("pageSize").GetInt32().Should().Be(2);
        first.GetProperty("totalCount").GetInt32().Should().Be(3);
        first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(t2, t1);
        var second = await GetJsonAsync(host, "/api/host/dashboard/trips?sort=owed&page=2&pageSize=2");
        second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(t3);
        (await GetJsonAsync(host, "/api/host/dashboard/trips?page=9")).GetProperty("items").GetArrayLength().Should().Be(0);
        var clamped = await GetJsonAsync(host, "/api/host/dashboard/trips?page=0&pageSize=500");
        clamped.GetProperty("page").GetInt32().Should().Be(1);
        clamped.GetProperty("pageSize").GetInt32().Should().Be(100);
        (await GetJsonAsync(host, "/api/host/dashboard/trips")).GetProperty("pageSize").GetInt32().Should().Be(20);
        (await host.GetAsync("/api/host/dashboard/trips?sort=price")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await GetJsonAsync(host, "/api/host/dashboard/summary")).GetProperty("seatsTotal").GetInt32().Should().Be(16);

        // Travellers: next due date (default) puts the seat holds (≈ 2 days) before instalment 1 (30 days).
        var byDue = await TravellerIdsAsync(host);
        byDue.Last().Should().Be(s1.InterestId);
        byDue.Take(2).Should().BeEquivalentTo(new[] { s2.InterestId, s3.InterestId });
        (await TravellerIdsAsync(host, "&sort=owes")).Last().Should().Be(s1.InterestId);

        await _flow.SetHostDueDateAsync(t1, 1, WatDay(-3));
        (await TravellerIdsAsync(host, "&sort=overdue")).First().Should().Be(s1.InterestId);
        (await TravellerIdsAsync(host, "&sort=nextDueDate")).First().Should().Be(s1.InterestId);
        (await TripIdsAsync(host, "&sort=overdue")).First().Should().Be(t1);

        var travellerPage = await GetJsonAsync(host, "/api/host/dashboard/travellers?page=2&pageSize=2");
        travellerPage.GetProperty("totalCount").GetInt32().Should().Be(3);
        travellerPage.GetProperty("items").GetArrayLength().Should().Be(1);
        (await host.GetAsync("/api/host/dashboard/travellers?sort=owed")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Monthly chart ────────────────────────────────────────────────────────

    [Fact]
    public async Task Monthly_buckets_split_hodrac_and_manual_money_by_wat_month()
    {
        var seat = await _flow.SeatAsync(TripBody());
        await _flow.PayDepositAsync(seat);
        var other = await _flow.AddSeatAsync(seat.Host, seat.HostUser, seat.HostId, seat.TripId, "Manual Payer");
        await RecordOkAsync(seat.Host, seat.TripId, other.InterestId, Deposit);
        await RecordOkAsync(seat.Host, seat.TripId, other.InterestId, -1_000_000, "Refund");

        var today = WatDay(0);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        DateTime MidMonthUtc(int monthsAgo) =>
            DateTime.SpecifyKind(thisMonth.AddMonths(-monthsAgo).AddDays(14).ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
        string Key(int monthsAgo) => thisMonth.AddMonths(-monthsAgo).ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var twoMonthsAgo = MidMonthUtc(2);
        var oneMonthAgo = MidMonthUtc(1);
        await _flow.WithDbAsync(db => db.SeatPayments.Where(p => p.TripInterestId == seat.InterestId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaidAt, twoMonthsAgo)));
        await _flow.WithDbAsync(db => db.ManualPayments.Where(m => m.TripInterestId == other.InterestId && m.AmountKobo > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RecordedAt, oneMonthAgo)));
        // A manual refund at 23:30 UTC on the last day of a month is the 1st of the next month in WAT.
        var lastDayLateUtc = DateTime.SpecifyKind(thisMonth.AddMonths(-3).AddDays(-1).ToDateTime(new TimeOnly(23, 30)), DateTimeKind.Utc);
        await _flow.WithDbAsync(db => db.ManualPayments.Where(m => m.TripInterestId == other.InterestId && m.AmountKobo < 0)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RecordedAt, lastDayLateUtc)));

        var summary = await GetJsonAsync(seat.Host, "/api/host/dashboard/summary");
        summary.GetProperty("paidTo").GetString().Should().Be(D(today));
        summary.GetProperty("paidFrom").GetString().Should().Be(D(thisMonth.AddMonths(-11)));
        var months = summary.GetProperty("monthly").EnumerateArray().ToList();
        months.Should().HaveCount(12);
        months.First().GetProperty("month").GetString().Should().Be(Key(11));
        months.Last().GetProperty("month").GetString().Should().Be(Key(0));
        JsonElement Month(int ago) => months.Single(m => m.GetProperty("month").GetString() == Key(ago));
        Kobo(Month(2), "viaHodracKobo").Should().Be(Deposit);
        Kobo(Month(2), "manualKobo").Should().Be(0);
        Kobo(Month(1), "manualKobo").Should().Be(Deposit);
        Kobo(Month(1), "totalKobo").Should().Be(Deposit);
        Kobo(Month(3), "manualKobo").Should().Be(-1_000_000);     // the WAT month, not the UTC one
        Kobo(Month(4), "manualKobo").Should().Be(0);
        months.Sum(m => Kobo(m, "totalKobo")).Should().Be(Kobo(summary, "collectedTotalKobo"));

        // A custom period.
        var custom = await GetJsonAsync(seat.Host, $"/api/host/dashboard/summary?paidFrom={D(thisMonth.AddMonths(-1))}&paidTo={D(today)}");
        var customMonths = custom.GetProperty("monthly").EnumerateArray().ToList();
        customMonths.Select(m => m.GetProperty("month").GetString()).Should().Equal(Key(1), Key(0));
        customMonths.Sum(m => Kobo(m, "viaHodracKobo")).Should().Be(0);

        var onlyFrom = await GetJsonAsync(seat.Host, $"/api/host/dashboard/summary?paidFrom={D(thisMonth.AddMonths(-2))}");
        onlyFrom.GetProperty("monthly").GetArrayLength().Should().Be(3);

        var max = await seat.Host.GetAsync($"/api/host/dashboard/summary?paidFrom={D(thisMonth.AddMonths(-35))}&paidTo={D(today)}");
        max.StatusCode.Should().Be(HttpStatusCode.OK);
        (await max.ReadJsonAsync()).GetProperty("monthly").GetArrayLength().Should().Be(36);
        (await seat.Host.GetAsync($"/api/host/dashboard/summary?paidFrom={D(thisMonth.AddMonths(-36))}&paidTo={D(today)}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await seat.Host.GetAsync($"/api/host/dashboard/summary?paidFrom={D(today)}&paidTo={D(today.AddDays(-1))}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Csv_exports_have_header_bom_rows_and_guard_formulas()
    {
        var (host, hostUser, hostId) = await _flow.HostAsync("Csv Host");
        var tripId = await CreateTripAsync(host, TripBody(freeText: "=HYPERLINK(\"x\"), Lagos"));
        var seat = await _flow.AddSeatAsync(host, hostUser, hostId, tripId, "Csv Person");
        await _flow.WithDbAsync(db => db.Users.Where(u => u.Id == seat.TravellerUser.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, "=SUM(1,2)")));
        await RecordOkAsync(host, tripId, seat.InterestId, Deposit, "Cash");

        static async Task<string[]> LinesAsync(HttpResponseMessage response, string prefix)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
            response.Content.Headers.ContentDisposition!.FileName!.Trim('"')
                .Should().Be($"{prefix}-csv-host-{HostTripService.TodayWat(DateTime.UtcNow):yyyy-MM-dd}.csv");
            var bytes = await response.Content.ReadAsByteArrayAsync();
            bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
            var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            text.Should().EndWith("\r\n");
            return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        }

        var trips = await LinesAsync(await host.GetAsync("/api/host/dashboard/trips.csv"), "trips");
        trips[0].Should().Be("tripId,title,location,startDate,endDate,status,currency,feeMode,instalmentDatesMode,seatsTotal,pricePerSeatNgn,"
            + "approved,seatsWithDeposit,seatsPaidInFull,collectedViaHodracNgn,collectedManualNgn,collectedTotalNgn,"
            + "owedNgn,overdueNgn,overdueTravellers,nextDueDate,hodracFeeTravellerPaidNgn,hodracFeeHostAbsorbedNgn,"
            + "paystackFeeNgn,refundedViaHodracNgn,refundedManualNgn,estimatedNetToHostNgn");
        trips.Should().HaveCount(2);
        trips[1].Should().Be($"{tripId},\"'=HYPERLINK(\"\"x\"\"), Lagos\",\"'=HYPERLINK(\"\"x\"\"), Lagos\",{D(WatDay(90))},{D(WatDay(95))},"
            + $"Open,NGN,PassToTraveller,HostSet,6,250000.00,1,1,0,0.00,50000.00,50000.00,200000.00,0.00,0,{D(WatDay(30))},"
            + "0.00,0.00,0.00,0.00,0.00,0.00");

        var travellers = await LinesAsync(await host.GetAsync("/api/host/dashboard/travellers.csv"), "travellers");
        travellers[0].Should().Be("tripId,tripTitle,tripStartDate,interestId,name,email,phone,requestStatus,paymentStatus,"
            + "paidNgn,viaHodracNgn,manualNgn,owesNgn,nextDueDate,nextDueNgn,overdueNgn,daysOverdue,"
            + "lastReminderAt,instalmentDatesMode,datesChosenByTraveller,canRemove,removedReason,removedAt,requestedAt");
        travellers.Should().HaveCount(2);
        travellers[1].Should().StartWith($"{tripId},\"'=HYPERLINK(\"\"x\"\"), Lagos\",{D(WatDay(90))},{seat.InterestId},"
            + $"\"'=SUM(1,2)\",{seat.TravellerUser.Email},,Approved,DepositPaid,50000.00,0.00,50000.00,200000.00,"
            + $"{D(WatDay(30))},100000.00,0.00,,,HostSet,false,false,,,");

        // Same filters as the JSON endpoints.
        (await LinesAsync(await host.GetAsync("/api/host/dashboard/travellers.csv?owing=false"), "travellers")).Should().HaveCount(1);
        (await LinesAsync(await host.GetAsync($"/api/host/dashboard/trips.csv?tripId={Guid.NewGuid()}"), "trips")).Should().HaveCount(1);
        (await host.GetAsync("/api/host/dashboard/trips.csv?sort=nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Another_hosts_trips_and_community_trips_never_appear()
    {
        var mine = await _flow.SeatAsync(TripBody());
        await RecordOkAsync(mine.Host, mine.TripId, mine.InterestId, Deposit);

        var (other, _, _) = await _flow.HostAsync("Other Host");
        var otherTrip = await CreateTripAsync(other, TripBody());
        var community = await CreateTripAsync(other, new
        {
            freeTextDestination = $"Comm-{Guid.NewGuid():N}",
            startDate = WatDay(60),
            endDate = WatDay(65),
            isDateFlexible = false,
            caption = "Weekend away",
            maxGroupSize = 6,
        });

        (await TripIdsAsync(other)).Should().Equal(otherTrip);
        (await TripIdsAsync(other, $"&tripId={mine.TripId}")).Should().BeEmpty();
        (await TripIdsAsync(other, $"&tripId={community}")).Should().BeEmpty();
        (await TravellerIdsAsync(other)).Should().BeEmpty();
        (await TravellerIdsAsync(other, $"&tripId={mine.TripId}")).Should().BeEmpty();
        var summary = await GetJsonAsync(other, "/api/host/dashboard/summary");
        summary.GetProperty("trips").GetInt32().Should().Be(1);
        Kobo(summary, "collectedTotalKobo").Should().Be(0);
        var csv = Encoding.UTF8.GetString(await (await other.GetAsync("/api/host/dashboard/travellers.csv")).Content.ReadAsByteArrayAsync());
        csv.Should().NotContain(mine.InterestId.ToString());

        (await TripIdsAsync(mine.Host)).Should().Equal(mine.TripId);

        // A user without a host profile sees an empty dashboard.
        var (nobody, _) = await _flow.UserAsync("Not A Host");
        (await GetJsonAsync(nobody, "/api/host/dashboard/summary")).GetProperty("trips").GetInt32().Should().Be(0);
        (await GetJsonAsync(nobody, "/api/host/dashboard/trips")).GetProperty("totalCount").GetInt32().Should().Be(0);
        (await nobody.GetAsync("/api/host/dashboard/trips.csv")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static readonly string[] DashboardUrls =
    {
        "/api/host/dashboard/summary",
        "/api/host/dashboard/trips",
        "/api/host/dashboard/travellers",
        "/api/host/dashboard/trips.csv",
        "/api/host/dashboard/travellers.csv",
    };

    [Fact]
    public async Task Closed_host_can_read_the_dashboard_and_anonymous_is_401()
    {
        var seat = await _flow.SeatAsync(TripBody());
        await RecordOkAsync(seat.Host, seat.TripId, seat.InterestId, Deposit);

        var admin = _factory.CreateClient().Authenticated(
            await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail));
        var close = await admin.PostAsJsonAsync($"/api/admin/hosts/{seat.HostId}/close", new { reason = "Closed for the test." });
        close.StatusCode.Should().Be(HttpStatusCode.OK, await close.Content.ReadAsStringAsync());

        foreach (var url in DashboardUrls)
            (await seat.Host.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, url);
        (await TripIdsAsync(seat.Host)).Should().Equal(seat.TripId);
        Kobo(await GetJsonAsync(seat.Host, "/api/host/dashboard/summary"), "collectedTotalKobo").Should().Be(Deposit);

        var anonymous = _factory.CreateClient();
        foreach (var url in DashboardUrls)
            (await anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
    }
}
