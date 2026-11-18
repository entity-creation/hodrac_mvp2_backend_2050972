using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Hodrac_Backend_MVP2.Services.Payments;

namespace Hodrac_Backend_MVP2.Tests.Payments;

/// <summary>Webhook x-paystack-signature: HMAC-SHA512 hex of the raw body.</summary>
public class PaystackSignatureTests
{
    private const string Secret = "sk_test_0123456789abcdef";
    private static readonly byte[] Body =
        Encoding.UTF8.GetBytes("{\"event\":\"charge.success\",\"data\":{\"reference\":\"HDR-261201-ABCD1234\",\"amount\":10250000}}");

    // Independent of the code under test.
    private static string Sign(byte[] body, string secret) =>
        Convert.ToHexString(new HMACSHA512(Encoding.UTF8.GetBytes(secret)).ComputeHash(body)).ToLowerInvariant();

    [Fact]
    public void Valid_signature_is_accepted()
    {
        PaystackSignature.IsValid(Body, Sign(Body, Secret), Secret).Should().BeTrue();
    }

    [Fact]
    public void Upper_case_hex_is_accepted()
    {
        PaystackSignature.IsValid(Body, Sign(Body, Secret).ToUpperInvariant(), Secret).Should().BeTrue();
    }

    [Fact]
    public void Signature_made_with_another_key_is_rejected()
    {
        PaystackSignature.IsValid(Body, Sign(Body, "sk_test_someone_else"), Secret).Should().BeFalse();
    }

    [Fact]
    public void Tampered_body_is_rejected()
    {
        var signature = Sign(Body, Secret);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Body).Replace("10250000", "10250001"));

        PaystackSignature.IsValid(tampered, signature, Secret).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcd")]                       // too short
    public void Missing_or_short_header_is_rejected(string? header)
    {
        PaystackSignature.IsValid(Body, header, Secret).Should().BeFalse();
    }

    [Fact]
    public void Wrong_length_header_is_rejected()
    {
        var valid = Sign(Body, Secret);

        PaystackSignature.IsValid(Body, valid[..^2], Secret).Should().BeFalse();   // 127 bytes of hex
        PaystackSignature.IsValid(Body, valid + "00", Secret).Should().BeFalse();  // 65 bytes
        // A SHA-256-length (64 hex chars) HMAC is not accepted either.
        PaystackSignature.IsValid(Body, valid[..64], Secret).Should().BeFalse();
    }

    [Fact]
    public void Non_hex_header_of_the_right_length_is_rejected()
    {
        PaystackSignature.IsValid(Body, new string('z', 128), Secret).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_secret_key_never_validates(string? secret)
    {
        PaystackSignature.IsValid(Body, Sign(Body, Secret), secret).Should().BeFalse();
    }

    [Fact]
    public void ComputeHex_matches_paystack_format()
    {
        PaystackSignature.ComputeHex(Body, Secret).Should().Be(Sign(Body, Secret));
    }
}
