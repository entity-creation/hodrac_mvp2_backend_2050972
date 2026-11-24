using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hodrac_Backend_MVP2.Infrastucture.Http;

/// <summary>
/// The one set of CSV export rules (manifest, host dashboard, partner clicks,
/// newsletter): UTF-8 with BOM so Excel detects the encoding, CRLF line ends,
/// RFC 4180 quoting with a formula-injection guard, naira with two decimals,
/// ISO 8601 dates (UTC for times), and a slugged file name.
/// </summary>
public static class CsvExport
{
    public const string ContentType = "text/csv";
    public const string LineEnd = "\r\n";

    /// <summary>
    /// RFC 4180 quoting, plus a leading apostrophe for values starting with
    /// = + - @ (tab/CR) so spreadsheets don't evaluate them as formulas.
    /// </summary>
    public static string Field(string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            value = "'" + value;

        var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value != value.Trim();
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    /// <summary>Kobo as naira with two decimals. Numbers are written raw (a negative amount is a number, not a formula).</summary>
    public static string Naira(long kobo) => (kobo / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static string Naira(long? kobo) => kobo is { } k ? Naira(k) : "";

    /// <summary>A UTC timestamp as yyyy-MM-ddTHH:mm:ssZ; empty when null.</summary>
    public static string Time(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "";

    public static string Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>The CSV text as bytes, UTF-8 with BOM.</summary>
    public static byte[] ToBytes(StringBuilder csv) =>
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();

    /// <summary>Lower-case a-z0-9 runs joined by "-"; cut to <paramref name="maxLength"/> when given. May be empty.</summary>
    public static string Slug(string value, int? maxLength = null)
    {
        var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (maxLength is { } max && slug.Length > max) slug = slug[..max].Trim('-');
        return slug;
    }
}
