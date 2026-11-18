using System.Net;
using System.Text.Json;
using FluentAssertions;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Hodrac_Backend_MVP2.Tests.Hosts.HostTestHelpers;

namespace Hodrac_Backend_MVP2.Tests.Payments;

/// <summary>
/// GET /api/admin/insights/hosting: the business numbers over seeded payments
/// in Jan–Feb 2024 (no other test writes money that far back), including rows
/// either side of the WAT (UTC+1) month boundaries.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HostingInsightsTests
{
    private readonly HodracApiFactory _factory;

    public HostingInsightsTests(HodracApiFactory factory) => _factory = factory;

    private static DateTime Utc(int y, int mo, int d, int h = 12, int mi = 0, int s = 0) =>
        new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    private async Task<HttpClient> AdminAsync()
    {
        var admin = await AuthHelper.GetOrRegisterAsync(_factory.CreateClient(), HodracApiFactory.AdminEmail);
        return _factory.CreateClient().Authenticated(admin);
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();

        HostProfile Host(string name) => new()
        {
            UserId = Guid.NewGuid(),
            DisplayName = name,
            Slug = $"insights-{Guid.NewGuid():N}",
            PaystackSubaccountCode = "ACCT_insights",
        };
        var hostA = Host("Insights Host A");
        hostA.Plan = HostPlan.Operator;
        hostA.OperatorPlanExpiresAt = Utc(2025, 1, 15); // expired: not "active"
        var hostB = Host("Insights Host B");

        TripPost Trip(HostProfile host, DateOnly start) => new()
        {
            AuthorUserId = host.UserId,
            HostProfile = host,
            Caption = "Insights trip",
            IsHostedTrip = true,
            PricePerSeatKobo = 10_000_000,
            DepositKobo = 1_000_000,
            TermsText = "Terms",
            StartDate = start,
            EndDate = start.AddDays(3),
        };
        var tripA = Trip(hostA, new DateOnly(2024, 1, 20));
        var tripB = Trip(hostB, new DateOnly(2024, 2, 10));
        var tripC = Trip(hostA, new DateOnly(2024, 2, 25)); // no money: not a paid departure

        TripInterest Interest(TripPost trip, long paid) => new()
        {
            TripPost = trip,
            RequesterUserId = Guid.NewGuid(),
            Status = TripInterestStatus.Approved,
            PaymentStatus = paid > 0 ? InterestPaymentStatus.DepositPaid : InterestPaymentStatus.AwaitingDeposit,
            AmountPaidKobo = paid,
        };
        var i1 = Interest(tripA, 1_300_000);
        var i2 = Interest(tripA, 1_500_000);
        var i3 = Interest(tripB, 800_000);
        var i4 = Interest(tripC, 0);

        SeatPayment Pay(TripInterest i, SeatPaymentStatus status, long seat, long fee, long refunded, DateTime paidAt) => new()
        {
            TripPost = i.TripPost,
            TripInterest = i,
            PayerUserId = i.RequesterUserId,
            Reference = $"HDR-INS-{Guid.NewGuid():N}"[..30],
            Kind = SeatPaymentKind.Deposit,
            SeatAmountKobo = seat,
            HodracFeeKobo = fee,
            TravellerFeeKobo = fee,
            TotalChargedKobo = seat + fee,
            RefundedKobo = refunded,
            Status = status,
            PaidAt = paidAt,
            CreatedAt = paidAt.AddMinutes(-5),
        };

        db.AddRange(hostA, hostB, tripA, tripB, tripC, i1, i2, i3, i4);
        db.SeatPayments.AddRange(
            // 23:59:59 WAT on 31 Jan → January.
            Pay(i1, SeatPaymentStatus.Success, 1_000_000, 25_000, 0, Utc(2024, 1, 31, 22, 59, 59)),
            // 00:00 WAT on 1 Feb → February. Net seat money 1,500,000.
            Pay(i2, SeatPaymentStatus.PartiallyRefunded, 2_000_000, 50_000, 500_000, Utc(2024, 1, 31, 23, 0, 0)),
            // Fully refunded: no seat money, fee kept, refund counted.
            Pay(i3, SeatPaymentStatus.Refunded, 3_000_000, 75_000, 3_075_000, Utc(2024, 2, 15)),
            // Failed: ignored.
            Pay(i3, SeatPaymentStatus.Failed, 9_999, 250, 0, Utc(2024, 2, 16)),
            // Disputed still counts as collected.
            Pay(i3, SeatPaymentStatus.Disputed, 400_000, 10_000, 0, Utc(2024, 2, 20)),
            // 31 Dec WAT: before the period.
            Pay(i1, SeatPaymentStatus.Success, 777_000, 19_425, 0, Utc(2023, 12, 31, 22, 59, 0)));

        db.ManualPayments.AddRange(
            new ManualPayment { TripInterest = i1, AmountKobo = 300_000, Method = ManualPaymentMethod.Transfer, RecordedByUserId = hostA.UserId, RecordedAt = Utc(2024, 1, 10) },
            new ManualPayment { TripInterest = i3, AmountKobo = 600_000, Method = ManualPaymentMethod.Transfer, RecordedByUserId = hostB.UserId, RecordedAt = Utc(2024, 2, 5) },
            // 23:30 WAT on 29 Feb → February.
            new ManualPayment { TripInterest = i3, AmountKobo = -100_000, Method = ManualPaymentMethod.Refund, RecordedByUserId = hostB.UserId, RecordedAt = Utc(2024, 2, 29, 22, 30, 0) },
            // 00:00 WAT on 1 Mar → after the period.
            new ManualPayment { TripInterest = i2, AmountKobo = 200_000, Method = ManualPaymentMethod.Cash, RecordedByUserId = hostA.UserId, RecordedAt = Utc(2024, 2, 29, 23, 0, 0) });

        db.OperatorPlanPayments.AddRange(
            new OperatorPlanPayment { HostProfile = hostA, Reference = $"INV-INS-{Guid.NewGuid():N}", AmountKobo = 50_000_000, PeriodStart = new DateOnly(2024, 1, 15), PeriodEnd = new DateOnly(2025, 1, 14), CreatedAt = Utc(2024, 1, 15) },
            new OperatorPlanPayment { HostProfile = hostB, Reference = $"INV-INS-{Guid.NewGuid():N}", AmountKobo = 50_000_000, PeriodStart = new DateOnly(2024, 3, 1), PeriodEnd = new DateOnly(2025, 2, 28), CreatedAt = Utc(2024, 2, 29, 23, 30, 0) });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Hosting_insights_totals_and_months_on_WAT_boundaries()
    {
        await SeedAsync();
        var admin = await AdminAsync();

        var response = await admin.GetAsync("/api/admin/insights/hosting?from=2024-01-01&to=2024-02-29");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.ReadJsonAsync();

        json.GetProperty("from").GetString().Should().Be("2024-01-01");
        json.GetProperty("to").GetString().Should().Be("2024-02-29");

        // Totals.
        json.GetProperty("viaHodracKobo").GetInt64().Should().Be(2_900_000);
        json.GetProperty("manualNetKobo").GetInt64().Should().Be(800_000);
        json.GetProperty("gmvKobo").GetInt64().Should().Be(3_700_000);
        json.GetProperty("hodracFeeKobo").GetInt64().Should().Be(160_000);
        json.GetProperty("refundedViaHodracKobo").GetInt64().Should().Be(3_575_000);
        json.GetProperty("percentThroughHodrac").GetDouble().Should().Be(78.4);
        json.GetProperty("seatsPaid").GetInt32().Should().Be(3);
        json.GetProperty("activeHosts").GetInt32().Should().Be(2);
        json.GetProperty("paidDepartures").GetInt32().Should().Be(2);
        json.GetProperty("operatorPlanRevenueKobo").GetInt64().Should().Be(50_000_000);
        json.GetProperty("operatorPlanPayments").GetInt32().Should().Be(1);

        // Months.
        var months = json.GetProperty("months").EnumerateArray().ToList();
        months.Select(m => m.GetProperty("month").GetString()).Should().Equal("2024-01", "2024-02");

        var jan = months[0];
        jan.GetProperty("viaHodracKobo").GetInt64().Should().Be(1_000_000);
        jan.GetProperty("manualNetKobo").GetInt64().Should().Be(300_000);
        jan.GetProperty("gmvKobo").GetInt64().Should().Be(1_300_000);
        jan.GetProperty("hodracFeeKobo").GetInt64().Should().Be(25_000);
        jan.GetProperty("refundedViaHodracKobo").GetInt64().Should().Be(0);
        jan.GetProperty("percentThroughHodrac").GetDouble().Should().Be(76.9);
        jan.GetProperty("seatsPaid").GetInt32().Should().Be(1);
        jan.GetProperty("activeHosts").GetInt32().Should().Be(1);
        jan.GetProperty("paidDepartures").GetInt32().Should().Be(1);
        jan.GetProperty("operatorPlanRevenueKobo").GetInt64().Should().Be(50_000_000);

        var feb = months[1];
        feb.GetProperty("viaHodracKobo").GetInt64().Should().Be(1_900_000);
        feb.GetProperty("manualNetKobo").GetInt64().Should().Be(500_000);
        feb.GetProperty("gmvKobo").GetInt64().Should().Be(2_400_000);
        feb.GetProperty("hodracFeeKobo").GetInt64().Should().Be(135_000);
        feb.GetProperty("refundedViaHodracKobo").GetInt64().Should().Be(3_575_000);
        feb.GetProperty("percentThroughHodrac").GetDouble().Should().Be(79.2);
        feb.GetProperty("seatsPaid").GetInt32().Should().Be(2);
        feb.GetProperty("activeHosts").GetInt32().Should().Be(2);
        feb.GetProperty("paidDepartures").GetInt32().Should().Be(1);
        feb.GetProperty("operatorPlanRevenueKobo").GetInt64().Should().Be(0);
        feb.GetProperty("operatorPlanPayments").GetInt32().Should().Be(0);

        // A single month narrows everything to it.
        var janOnly = await (await admin.GetAsync("/api/admin/insights/hosting?from=2024-01-01&to=2024-01-31")).ReadJsonAsync();
        janOnly.GetProperty("gmvKobo").GetInt64().Should().Be(1_300_000);
        janOnly.GetProperty("months").GetArrayLength().Should().Be(1);

        // An empty period has no percentage.
        var empty = await (await admin.GetAsync("/api/admin/insights/hosting?from=2020-01-01&to=2020-01-31")).ReadJsonAsync();
        empty.GetProperty("gmvKobo").GetInt64().Should().Be(0);
        empty.GetProperty("percentThroughHodrac").ValueKind.Should().Be(JsonValueKind.Null);

        // Snapshots match the database.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
        json.GetProperty("verifiedHosts").GetInt32().Should().Be(
            await db.HostProfiles.CountAsync(h => h.VerificationStatus == HostVerificationStatus.Verified));
        json.GetProperty("foundingHostsUsed").GetInt32().Should().Be(await db.HostProfiles.CountAsync(h => h.IsFoundingHost));
        json.GetProperty("foundingHostLimit").GetInt32().Should().Be(HodracApiFactory.FoundingHostLimit);
        var now = DateTime.UtcNow;
        json.GetProperty("operatorPlansActive").GetInt32().Should().Be(
            await db.HostProfiles.CountAsync(h => h.Plan == HostPlan.Operator && h.OperatorPlanExpiresAt > now));
    }

    [Fact]
    public async Task Hosting_insights_defaults_to_last_12_months_and_validates_the_range()
    {
        var admin = await AdminAsync();

        var defaults = await admin.GetAsync("/api/admin/insights/hosting");
        defaults.StatusCode.Should().Be(HttpStatusCode.OK, await defaults.Content.ReadAsStringAsync());
        (await defaults.ReadJsonAsync()).GetProperty("months").GetArrayLength().Should().Be(12);

        (await admin.GetAsync("/api/admin/insights/hosting?from=2024-02-01&to=2024-01-01"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/admin/insights/hosting?from=2010-01-01&to=2024-01-01"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
