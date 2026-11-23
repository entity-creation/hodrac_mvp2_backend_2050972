using Hodrac_Backend_MVP2.Services.Images;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// Image hosts the seeders build URLs from. Program.cs sets these from <see cref="ImagesOptions"/>
    /// before any <c>--seed-*</c> command runs; the defaults keep the Vercel Blob host.
    /// </summary>
    public static class SeedImages
    {
        private static string _baseUrl = ImagesOptions.VercelBlobBaseUrl;
        private static IReadOnlyList<string> _legacyBaseUrls = Array.Empty<string>();

        /// <summary>Base for new image URLs (always ends with "/").</summary>
        public static string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = ImagesOptions.NormalizeBaseUrl(value);
        }

        /// <summary>Older hosts that stored rows may still point at (each ends with "/").</summary>
        public static IReadOnlyList<string> LegacyBaseUrls
        {
            get => _legacyBaseUrls;
            set => _legacyBaseUrls = (value ?? Array.Empty<string>())
                .Select(ImagesOptions.NormalizeBaseUrl)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The current base first, then every legacy base.</summary>
        public static IReadOnlyList<string> AllBaseUrls =>
            new[] { BaseUrl }.Concat(LegacyBaseUrls).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>The same file under every known base (for matching rows seeded before a host move).</summary>
        public static List<string> UrlsUnderAllBases(string fileName) =>
            AllBaseUrls.Select(b => b + fileName).ToList();

        public static void Configure(ImagesOptions options)
        {
            BaseUrl = options.NormalizedPublicBaseUrl;
            LegacyBaseUrls = options.NormalizedFallbackBaseUrls;
        }
    }
}
