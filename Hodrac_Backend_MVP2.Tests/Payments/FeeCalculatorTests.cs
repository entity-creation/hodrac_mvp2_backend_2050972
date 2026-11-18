using FluentAssertions;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Tests.Payments;

/// <summary>Spec "Fee rules": pure function, no DB.</summary>
public class FeeCalculatorTests
{
    private static readonly DateTime Now = new(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly FeeCalculator _calc = new(Options.Create(new HodracHostOptions()));

    [Fact]
    public void Default_fee_percent_is_2_5()
    {
        _calc.FeePercent.Should().Be(2.5m);
    }

    [Fact]
    public void Host_plan_pass_to_traveller_adds_fee_on_top()
    {
        // ₦100,000 seat = 10,000,000 kobo; 2.5% = 250,000 kobo.
        var q = _calc.Calculate(10_000_000, FeeMode.PassToTraveller, false, HostPlan.Host, null, Now);

        q.SeatAmountKobo.Should().Be(10_000_000);
        q.HodracFeeKobo.Should().Be(250_000);
        q.TravellerFeeKobo.Should().Be(250_000);
        q.TotalChargedKobo.Should().Be(10_250_000);
        q.TransactionChargeKobo.Should().Be(250_000);
        q.Bearer.Should().Be("subaccount");
    }

    [Fact]
    public void Host_plan_host_absorbs_charges_seat_only_and_fee_comes_out_of_host_share()
    {
        var q = _calc.Calculate(10_000_000, FeeMode.HostAbsorbs, false, HostPlan.Host, null, Now);

        q.HodracFeeKobo.Should().Be(250_000);
        q.TravellerFeeKobo.Should().Be(0);
        q.TotalChargedKobo.Should().Be(10_000_000);
        q.TransactionChargeKobo.Should().Be(250_000);
        q.Bearer.Should().Be("subaccount");
    }

    [Theory]
    [InlineData(FeeMode.PassToTraveller)]
    [InlineData(FeeMode.HostAbsorbs)]
    public void Active_operator_plan_pays_no_fee(FeeMode mode)
    {
        var q = _calc.Calculate(10_000_000, mode, false, HostPlan.Operator, Now.AddDays(30), Now);

        q.HodracFeeKobo.Should().Be(0);
        q.TravellerFeeKobo.Should().Be(0);
        q.TransactionChargeKobo.Should().Be(0);
        q.TotalChargedKobo.Should().Be(10_000_000);
        q.Bearer.Should().Be("subaccount");
    }

    [Fact]
    public void Expired_operator_plan_pays_the_normal_fee()
    {
        var q = _calc.Calculate(10_000_000, FeeMode.PassToTraveller, false, HostPlan.Operator, Now.AddSeconds(-1), Now);

        q.HodracFeeKobo.Should().Be(250_000);
        q.TotalChargedKobo.Should().Be(10_250_000);
    }

    [Fact]
    public void Operator_plan_expiring_exactly_now_is_expired()
    {
        _calc.Calculate(10_000_000, FeeMode.PassToTraveller, false, HostPlan.Operator, Now, Now)
            .HodracFeeKobo.Should().Be(250_000);
    }

    [Fact]
    public void Operator_plan_without_expiry_is_not_active()
    {
        _calc.Calculate(10_000_000, FeeMode.PassToTraveller, false, HostPlan.Operator, null, Now)
            .HodracFeeKobo.Should().Be(250_000);
    }

    [Fact]
    public void Expiry_date_on_host_plan_is_ignored()
    {
        _calc.Calculate(10_000_000, FeeMode.PassToTraveller, false, HostPlan.Host, Now.AddDays(30), Now)
            .HodracFeeKobo.Should().Be(250_000);
    }

    [Theory]
    [InlineData(FeeMode.PassToTraveller)]
    [InlineData(FeeMode.HostAbsorbs)]
    public void Founding_free_departure_pays_no_fee(FeeMode mode)
    {
        var q = _calc.Calculate(10_000_000, mode, true, HostPlan.Host, null, Now);

        q.HodracFeeKobo.Should().Be(0);
        q.TravellerFeeKobo.Should().Be(0);
        q.TransactionChargeKobo.Should().Be(0);
        q.TotalChargedKobo.Should().Be(10_000_000);
    }

    [Theory]
    // 2.5% of 4,999,999 = 124,999.975 -> 125,000
    [InlineData(4_999_999L, 125_000L)]
    // 2.5% of 100 = 2.5 -> 3 (half away from zero, not banker's 2)
    [InlineData(100L, 3L)]
    // 2.5% of 20 = 0.5 -> 1
    [InlineData(20L, 1L)]
    // 2.5% of 19 = 0.475 -> 0
    [InlineData(19L, 0L)]
    // 2.5% of 60 = 1.5 -> 2
    [InlineData(60L, 2L)]
    // 2.5% of 140 = 3.5 -> 4 (banker's would also give 4)
    [InlineData(140L, 4L)]
    // 2.5% of 180 = 4.5 -> 5 (banker's would give 4)
    [InlineData(180L, 5L)]
    public void Fee_rounds_half_away_from_zero_in_kobo(long seat, long expectedFee)
    {
        var q = _calc.Calculate(seat, FeeMode.PassToTraveller, false, HostPlan.Host, null, Now);

        q.HodracFeeKobo.Should().Be(expectedFee);
        q.TotalChargedKobo.Should().Be(seat + expectedFee);
    }

    [Fact]
    public void Fee_percent_comes_from_options()
    {
        var calc = new FeeCalculator(Options.Create(new HodracHostOptions { FeePercent = 3m }));

        calc.Calculate(10_000, FeeMode.PassToTraveller, false, HostPlan.Host, null, Now)
            .HodracFeeKobo.Should().Be(300);
    }

    [Fact]
    public void Entity_overload_reads_trip_and_host()
    {
        var trip = new TripPost { FeeMode = FeeMode.HostAbsorbs, IsFoundingFreeDeparture = false };
        var host = new HostProfile { Plan = HostPlan.Host };

        var q = _calc.Calculate(trip, host, 1_000_000, Now);

        q.HodracFeeKobo.Should().Be(25_000);
        q.TotalChargedKobo.Should().Be(1_000_000);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-100L)]
    public void Non_positive_seat_amount_is_rejected(long seat)
    {
        var act = () => _calc.Calculate(seat, FeeMode.PassToTraveller, false, HostPlan.Host, null, Now);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
