using System.Security.Cryptography;

namespace Hodrac_Backend_MVP2.Services.Payments;

/// <summary>Server-generated Paystack references: <c>HDR-{yyMMdd}-{8 random A-Z0-9}</c> (UTC date).</summary>
public static class PaymentReference
{
    public const string Prefix = "HDR";
    public const int RandomLength = 8;

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string New() => New(DateTime.UtcNow);

    public static string New(DateTime utcNow) =>
        $"{Prefix}-{utcNow:yyMMdd}-{RandomNumberGenerator.GetString(Alphabet, RandomLength)}";
}
