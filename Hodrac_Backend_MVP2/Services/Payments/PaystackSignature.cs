using System.Security.Cryptography;
using System.Text;

namespace Hodrac_Backend_MVP2.Services.Payments;

/// <summary>
/// Paystack webhook signature check: the <c>x-paystack-signature</c> header is
/// the hex HMAC-SHA512 of the raw request body, keyed with the secret key.
/// Compared in constant time.
/// </summary>
public static class PaystackSignature
{
    public const string HeaderName = "x-paystack-signature";

    private const int HmacSha512Bytes = 64;

    public static bool IsValid(ReadOnlySpan<byte> rawBody, string? signatureHeader, string? secretKey)
    {
        if (string.IsNullOrEmpty(secretKey) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var header = signatureHeader.Trim();
        if (header.Length != HmacSha512Bytes * 2)
            return false;

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(header);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Compute(rawBody, secretKey);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>Raw HMAC-SHA512 of the body (64 bytes).</summary>
    public static byte[] Compute(ReadOnlySpan<byte> rawBody, string secretKey) =>
        HMACSHA512.HashData(Encoding.UTF8.GetBytes(secretKey), rawBody);

    /// <summary>Lower-case hex signature, as Paystack sends it.</summary>
    public static string ComputeHex(ReadOnlySpan<byte> rawBody, string secretKey) =>
        Convert.ToHexString(Compute(rawBody, secretKey)).ToLowerInvariant();
}
