using System.Globalization;
using System.Text;
using Philiprehberger.StringSimilarity;

namespace Hodrac_Backend_MVP2.Services
{
    /// <summary>
    /// Derives the search columns on <c>Destination</c> from its display name.
    ///
    /// <list type="bullet">
    /// <item><c>CleanNormalizedSearchName</c> — <see cref="Normalize"/> (same rules as the seeders).</item>
    /// <item><c>MetaphoneCode</c> / <c>DoubleMetaphonePrimary</c> — the seeders' per-word consonant
    ///   skeleton (<see cref="Phonetic"/>), so admin-created rows look like seeded rows.</item>
    /// <item><c>DoubleMetaphoneSecondary</c> — <c>Metaphone.Encode(clean, 20)</c> from
    ///   Philiprehberger.StringSimilarity. <c>GET /api/destinations/similar-name</c> encodes the
    ///   query with <c>Metaphone.Encode(name)</c> (4 chars) and matches with equality / Contains
    ///   on all three phonetic columns, so storing the library's long code here guarantees the
    ///   library's short code is a prefix match. (Seeders leave this column empty.)</item>
    /// </list>
    /// </summary>
    public static class DestinationSearchFields
    {
        public const int PhoneticMaxLength = 20;

        public readonly record struct Fields(
            string CleanNormalizedSearchName,
            string MetaphoneCode,
            string DoubleMetaphonePrimary,
            string DoubleMetaphoneSecondary);

        public static Fields Derive(string destinationName)
        {
            var clean = Normalize(destinationName);
            if (clean.Length > 200) clean = clean[..200].TrimEnd();

            string libraryCode;
            try
            {
                libraryCode = clean.Length == 0 ? string.Empty : Metaphone.Encode(clean, PhoneticMaxLength).ToUpperInvariant();
            }
            catch (ArgumentException)
            {
                libraryCode = string.Empty;
            }

            return new Fields(
                clean,
                Phonetic(clean, primary: false),
                Phonetic(clean, primary: true),
                libraryCode.Length > PhoneticMaxLength ? libraryCode[..PhoneticMaxLength] : libraryCode);
        }

        /// <summary>Lowercase, accents stripped, apostrophes removed, '&amp;' → "and", other punctuation → space.</summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var decomposed = input.Replace("&", " and ").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (ch is '\'' or '’') continue;
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            }
            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Simplified metaphone-style consonant skeleton (uppercase, one token per word, ≤ 20 chars).
        /// primary=true approximates Double Metaphone primary (B → P). Mirrors AfricaPocSeeder.Phonetic.
        /// </summary>
        public static string Phonetic(string clean, bool primary)
        {
            var stopWords = new HashSet<string> { "and", "of", "the" };
            var tokens = new List<string>();
            foreach (var word in clean.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (stopWords.Contains(word)) continue;
                var w = word.ToUpperInvariant();
                var sb = new StringBuilder();
                void Emit(string s)
                {
                    foreach (var c in s)
                        if (sb.Length == 0 || sb[^1] != c) sb.Append(c);
                }
                for (var i = 0; i < w.Length; i++)
                {
                    var c = w[i];
                    var next = i + 1 < w.Length ? w[i + 1] : '\0';
                    if (!char.IsLetter(c)) continue;
                    switch (c)
                    {
                        case 'A' or 'E' or 'I' or 'O' or 'U':
                            if (i == 0) Emit(c.ToString());
                            break;
                        case 'Y' or 'W' or 'H':
                            if (i == 0) Emit(c.ToString());
                            break;
                        case 'C':
                            if (next == 'H') { Emit("X"); i++; }
                            else Emit(next is 'E' or 'I' or 'Y' ? "S" : "K");
                            break;
                        case 'S':
                            if (next == 'H') { Emit("X"); i++; } else Emit("S");
                            break;
                        case 'P':
                            if (next == 'H') { Emit("F"); i++; } else Emit("P");
                            break;
                        case 'T':
                            if (next == 'H') i++;
                            Emit("T");
                            break;
                        case 'G':
                            if (next == 'H') i++;
                            Emit("K");
                            break;
                        case 'Q': Emit("K"); break;
                        case 'Z': Emit("S"); break;
                        case 'X': Emit("KS"); break;
                        case 'D': Emit("T"); break;
                        case 'V': Emit("F"); break;
                        case 'B': Emit(primary ? "P" : "B"); break;
                        default: Emit(c.ToString()); break;
                    }
                }
                if (sb.Length > 0) tokens.Add(sb.ToString());
            }

            // Keep whole tokens while within the 20-char column limit.
            var result = new StringBuilder();
            foreach (var t in tokens)
            {
                var candidateLength = result.Length == 0 ? t.Length : result.Length + 1 + t.Length;
                if (candidateLength > PhoneticMaxLength) break;
                if (result.Length > 0) result.Append(' ');
                result.Append(t);
            }
            if (result.Length == 0 && tokens.Count > 0)
                result.Append(tokens[0].Length > PhoneticMaxLength ? tokens[0][..PhoneticMaxLength] : tokens[0]);
            return result.ToString();
        }
    }
}
