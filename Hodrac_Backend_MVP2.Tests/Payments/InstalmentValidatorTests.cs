using FluentAssertions;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;

namespace Hodrac_Backend_MVP2.Tests.Payments;

/// <summary>Deposit + instalments schedule rules. Pure, no DB.</summary>
public class InstalmentValidatorTests
{
    private static readonly DateOnly Start = new(2027, 4, 10);

    private static TripInstalment Line(int seq, long amount, string due) =>
        new() { Sequence = seq, AmountKobo = amount, DueDate = DateOnly.Parse(due) };

    [Fact]
    public void Valid_schedule_has_no_errors()
    {
        var errors = InstalmentValidator.Validate(
            pricePerSeatKobo: 50_000_000,
            depositKobo: 10_000_000,
            new[] { Line(1, 20_000_000, "2027-02-01"), Line(2, 20_000_000, "2027-03-01") },
            Start);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Deposit_only_paid_in_full_is_valid()
    {
        InstalmentValidator.Validate(50_000_000, 50_000_000, Array.Empty<TripInstalment>(), Start)
            .Should().BeEmpty();
    }

    [Fact]
    public void Lines_given_out_of_order_are_validated_by_sequence()
    {
        InstalmentValidator.Validate(
                50_000_000, 10_000_000,
                new[] { Line(2, 20_000_000, "2027-03-01"), Line(1, 20_000_000, "2027-02-01") },
                Start)
            .Should().BeEmpty();
    }

    [Fact]
    public void Sum_mismatch_is_rejected()
    {
        var errors = InstalmentValidator.Validate(
            50_000_000, 10_000_000,
            new[] { Line(1, 20_000_000, "2027-02-01"), Line(2, 19_999_999, "2027-03-01") },
            Start);

        errors.Should().ContainKey(InstalmentValidator.InstalmentsField);
        errors[InstalmentValidator.InstalmentsField].Should().Contain("add up");
    }

    [Fact]
    public void Sum_over_price_is_rejected()
    {
        InstalmentValidator.Validate(
                50_000_000, 10_000_000,
                new[] { Line(1, 40_000_001, "2027-02-01") },
                Start)
            .Should().ContainKey(InstalmentValidator.InstalmentsField);
    }

    [Fact]
    public void Due_dates_not_strictly_increasing_are_rejected()
    {
        var errors = InstalmentValidator.Validate(
            50_000_000, 10_000_000,
            new[] { Line(1, 20_000_000, "2027-03-01"), Line(2, 20_000_000, "2027-02-01") },
            Start);

        errors.Should().ContainKey("instalments[1].dueDate");
    }

    [Fact]
    public void Same_due_date_twice_is_rejected()
    {
        InstalmentValidator.Validate(
                50_000_000, 10_000_000,
                new[] { Line(1, 20_000_000, "2027-02-01"), Line(2, 20_000_000, "2027-02-01") },
                Start)
            .Should().ContainKey("instalments[1].dueDate");
    }

    [Theory]
    [InlineData("2027-04-10")] // on the start date
    [InlineData("2027-05-01")] // after it
    public void Due_date_on_or_after_start_is_rejected(string due)
    {
        var errors = InstalmentValidator.Validate(
            50_000_000, 10_000_000,
            new[] { Line(1, 20_000_000, "2027-02-01"), Line(2, 20_000_000, due) },
            Start);

        errors.Should().ContainKey("instalments[1].dueDate");
        errors["instalments[1].dueDate"].Should().Contain("before the trip starts");
    }

    [Fact]
    public void Without_start_date_late_due_dates_are_allowed()
    {
        InstalmentValidator.Validate(
                50_000_000, 10_000_000,
                new[] { Line(1, 40_000_000, "2030-01-01") },
                startDate: null)
            .Should().BeEmpty();
    }

    [Fact]
    public void Deposit_over_price_is_rejected()
    {
        var errors = InstalmentValidator.Validate(50_000_000, 50_000_001, Array.Empty<TripInstalment>(), Start);

        errors.Should().ContainKey(InstalmentValidator.DepositField);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Non_positive_instalment_amount_is_rejected(long amount)
    {
        InstalmentValidator.Validate(
                50_000_000, 10_000_000,
                new[] { Line(1, 40_000_000, "2027-02-01"), Line(2, amount, "2027-03-01") },
                Start)
            .Should().ContainKey("instalments[1].amountKobo");
    }

    [Fact]
    public void Missing_or_non_positive_price_and_deposit_are_rejected()
    {
        InstalmentValidator.Validate(null, null, null, Start)
            .Should().ContainKeys(InstalmentValidator.PriceField, InstalmentValidator.DepositField);

        InstalmentValidator.Validate(0, 0, null, Start)
            .Should().ContainKeys(InstalmentValidator.PriceField, InstalmentValidator.DepositField);
    }

    [Theory]
    [InlineData(new[] { 1, 3 })]    // gap
    [InlineData(new[] { 1, 1 })]    // duplicate
    [InlineData(new[] { 0, 1 })]    // not 1-based
    public void Sequences_must_be_1_to_n(int[] sequences)
    {
        var lines = sequences
            .Select((s, i) => Line(s, 20_000_000, $"2027-0{i + 1}-15"))
            .ToArray();

        InstalmentValidator.Validate(50_000_000, 10_000_000, lines, Start)
            .Should().ContainKey(InstalmentValidator.InstalmentsField);
    }
}
