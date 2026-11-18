using FluentAssertions;
using Hodrac_Backend_MVP2.Services.Payments;

namespace Hodrac_Backend_MVP2.Tests.Payments;

public class PaymentReferenceTests
{
    [Fact]
    public void Reference_has_prefix_date_and_8_uppercase_alphanumerics()
    {
        var reference = PaymentReference.New(new DateTime(2026, 12, 1, 23, 59, 0, DateTimeKind.Utc));

        reference.Should().MatchRegex("^HDR-261201-[A-Z0-9]{8}$");
    }

    [Fact]
    public void References_are_unique()
    {
        var refs = Enumerable.Range(0, 1000).Select(_ => PaymentReference.New()).ToList();

        refs.Should().OnlyHaveUniqueItems();
        refs.Should().AllSatisfy(r => r.Should().MatchRegex("^HDR-\\d{6}-[A-Z0-9]{8}$"));
    }
}
