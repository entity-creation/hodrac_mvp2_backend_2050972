namespace Hodrac_Backend_MVP2.Services
{
    public record TimeZoneOptionDto(string Id, string Label, string? UtcOffset);

    /// <summary>
    /// Controlled values for Destination.AccessibilityType and Destination.TimeZone,
    /// so admins pick from a list instead of typing free text. Writers normalise
    /// through these helpers; anything that isn't recognised is rejected.
    /// </summary>
    public static class DestinationReferenceData
    {
        // ── Accessibility (how travellers get there) ──────────────────────────

        public static readonly IReadOnlyList<string> AccessibilityTypes =
            new[] { "Walk", "Car", "Public transit", "Train", "Boat", "Flight" };

        private static readonly Dictionary<string, string> AccessibilityAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["walk"] = "Walk", ["walking"] = "Walk", ["on foot"] = "Walk",
            ["car"] = "Car", ["road"] = "Car", ["drive"] = "Car", ["driving"] = "Car", ["taxi"] = "Car",
            ["public transit"] = "Public transit", ["transit"] = "Public transit", ["public transport"] = "Public transit", ["bus"] = "Public transit",
            ["train"] = "Train", ["rail"] = "Train",
            ["boat"] = "Boat", ["boat only"] = "Boat", ["ferry"] = "Boat",
            ["flight"] = "Flight", ["plane"] = "Flight", ["air"] = "Flight",
        };

        /// <summary>Canonical accessibility type, "" for blank input, or null when not recognised.</summary>
        public static string? NormalizeAccessibility(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var key = string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace('_', ' ');
            return AccessibilityAliases.TryGetValue(key, out var canonical) ? canonical : null;
        }

        // ── Time zones (IANA IDs) ─────────────────────────────────────────────
        // Curated rather than TimeZoneInfo.GetSystemTimeZones(): that list differs
        // between Windows (dev) and Linux (Render), which would make the dropdown and
        // validation inconsistent across environments. Add zones here as needed.

        private static readonly (string Id, string Label)[] Zones =
        {
            // Africa & Indian Ocean
            ("Africa/Lagos", "Lagos, Abuja (Nigeria)"),
            ("Africa/Accra", "Accra (Ghana)"),
            ("Africa/Abidjan", "Abidjan (Côte d'Ivoire)"),
            ("Africa/Dakar", "Dakar (Senegal)"),
            ("Africa/Casablanca", "Casablanca, Marrakesh (Morocco)"),
            ("Africa/Algiers", "Algiers (Algeria)"),
            ("Africa/Tunis", "Tunis (Tunisia)"),
            ("Africa/Cairo", "Cairo (Egypt)"),
            ("Africa/Addis_Ababa", "Addis Ababa (Ethiopia)"),
            ("Africa/Nairobi", "Nairobi (Kenya)"),
            ("Africa/Dar_es_Salaam", "Dar es Salaam, Zanzibar (Tanzania)"),
            ("Africa/Kampala", "Kampala (Uganda)"),
            ("Africa/Kigali", "Kigali (Rwanda)"),
            ("Africa/Kinshasa", "Kinshasa (DR Congo)"),
            ("Africa/Luanda", "Luanda (Angola)"),
            ("Africa/Douala", "Douala, Yaoundé (Cameroon)"),
            ("Africa/Johannesburg", "Johannesburg, Cape Town (South Africa)"),
            ("Africa/Windhoek", "Windhoek (Namibia)"),
            ("Africa/Gaborone", "Gaborone (Botswana)"),
            ("Africa/Harare", "Harare, Victoria Falls (Zimbabwe)"),
            ("Africa/Lusaka", "Lusaka (Zambia)"),
            ("Africa/Maputo", "Maputo (Mozambique)"),
            ("Atlantic/Cape_Verde", "Praia (Cape Verde)"),
            ("Indian/Mauritius", "Port Louis (Mauritius)"),
            ("Indian/Mahe", "Victoria (Seychelles)"),
            ("Indian/Antananarivo", "Antananarivo (Madagascar)"),
            ("Indian/Reunion", "Saint-Denis (Réunion)"),
            ("Indian/Maldives", "Malé (Maldives)"),
            // Europe
            ("Europe/London", "London (United Kingdom)"),
            ("Europe/Dublin", "Dublin (Ireland)"),
            ("Europe/Lisbon", "Lisbon (Portugal)"),
            ("Europe/Paris", "Paris (France)"),
            ("Europe/Madrid", "Madrid, Barcelona (Spain)"),
            ("Europe/Amsterdam", "Amsterdam (Netherlands)"),
            ("Europe/Brussels", "Brussels (Belgium)"),
            ("Europe/Berlin", "Berlin (Germany)"),
            ("Europe/Zurich", "Zurich (Switzerland)"),
            ("Europe/Rome", "Rome (Italy)"),
            ("Europe/Vienna", "Vienna (Austria)"),
            ("Europe/Prague", "Prague (Czechia)"),
            ("Europe/Stockholm", "Stockholm (Sweden)"),
            ("Europe/Athens", "Athens (Greece)"),
            ("Europe/Istanbul", "Istanbul (Türkiye)"),
            ("Europe/Moscow", "Moscow (Russia)"),
            ("Atlantic/Reykjavik", "Reykjavík (Iceland)"),
            // Middle East & Asia
            ("Asia/Dubai", "Dubai, Abu Dhabi (UAE)"),
            ("Asia/Qatar", "Doha (Qatar)"),
            ("Asia/Riyadh", "Riyadh (Saudi Arabia)"),
            ("Asia/Amman", "Amman (Jordan)"),
            ("Asia/Jerusalem", "Jerusalem (Israel)"),
            ("Asia/Karachi", "Karachi (Pakistan)"),
            ("Asia/Kolkata", "Mumbai, Delhi (India)"),
            ("Asia/Kathmandu", "Kathmandu (Nepal)"),
            ("Asia/Colombo", "Colombo (Sri Lanka)"),
            ("Asia/Dhaka", "Dhaka (Bangladesh)"),
            ("Asia/Bangkok", "Bangkok (Thailand)"),
            ("Asia/Ho_Chi_Minh", "Ho Chi Minh City, Hanoi (Vietnam)"),
            ("Asia/Jakarta", "Jakarta (Indonesia)"),
            ("Asia/Makassar", "Bali (Indonesia)"),
            ("Asia/Kuala_Lumpur", "Kuala Lumpur (Malaysia)"),
            ("Asia/Singapore", "Singapore"),
            ("Asia/Manila", "Manila (Philippines)"),
            ("Asia/Hong_Kong", "Hong Kong"),
            ("Asia/Shanghai", "Beijing, Shanghai (China)"),
            ("Asia/Taipei", "Taipei (Taiwan)"),
            ("Asia/Seoul", "Seoul (South Korea)"),
            ("Asia/Tokyo", "Tokyo, Osaka (Japan)"),
            // Oceania & Pacific
            ("Australia/Perth", "Perth (Australia)"),
            ("Australia/Brisbane", "Brisbane (Australia)"),
            ("Australia/Sydney", "Sydney, Melbourne (Australia)"),
            ("Pacific/Auckland", "Auckland (New Zealand)"),
            ("Pacific/Fiji", "Suva (Fiji)"),
            ("Pacific/Honolulu", "Honolulu (Hawaii, USA)"),
            // Americas
            ("America/Anchorage", "Anchorage (Alaska, USA)"),
            ("America/Los_Angeles", "Los Angeles, Seattle (USA Pacific)"),
            ("America/Vancouver", "Vancouver (Canada)"),
            ("America/Denver", "Denver (USA Mountain)"),
            ("America/Phoenix", "Phoenix (Arizona, USA)"),
            ("America/Chicago", "Chicago, Dallas (USA Central)"),
            ("America/New_York", "New York, Miami (USA Eastern)"),
            ("America/Toronto", "Toronto (Canada)"),
            ("America/Mexico_City", "Mexico City (Mexico)"),
            ("America/Cancun", "Cancún (Mexico)"),
            ("America/Havana", "Havana (Cuba)"),
            ("America/Jamaica", "Kingston (Jamaica)"),
            ("America/Puerto_Rico", "San Juan (Puerto Rico)"),
            ("America/Barbados", "Bridgetown (Barbados)"),
            ("America/Bogota", "Bogotá (Colombia)"),
            ("America/Lima", "Lima (Peru)"),
            ("America/Santiago", "Santiago (Chile)"),
            ("America/Argentina/Buenos_Aires", "Buenos Aires (Argentina)"),
            ("America/Sao_Paulo", "São Paulo, Rio de Janeiro (Brazil)"),
            ("UTC", "Coordinated Universal Time (UTC)"),
        };

        private static readonly Dictionary<string, string> ZoneById =
            Zones.ToDictionary(z => z.Id, z => z.Id, StringComparer.OrdinalIgnoreCase);

        // Windows names already stored in the data (and common ones admins may paste).
        private static readonly Dictionary<string, string> WindowsToIana = new(StringComparer.OrdinalIgnoreCase)
        {
            ["W. Central Africa Standard Time"] = "Africa/Lagos",
            ["Mauritius Standard Time"] = "Indian/Mauritius",
            ["South Africa Standard Time"] = "Africa/Johannesburg",
            ["E. Africa Standard Time"] = "Africa/Nairobi",
            ["Egypt Standard Time"] = "Africa/Cairo",
            ["Greenwich Standard Time"] = "Africa/Abidjan",
            ["Morocco Standard Time"] = "Africa/Casablanca",
            ["GMT Standard Time"] = "Europe/London",
            ["W. Europe Standard Time"] = "Europe/Berlin",
            ["Romance Standard Time"] = "Europe/Paris",
            ["Arabian Standard Time"] = "Asia/Dubai",
            ["India Standard Time"] = "Asia/Kolkata",
            ["Japan Standard Time"] = "Asia/Tokyo",
            ["Eastern Standard Time"] = "America/New_York",
            ["Central Standard Time"] = "America/Chicago",
            ["Mountain Standard Time"] = "America/Denver",
            ["Pacific Standard Time"] = "America/Los_Angeles",
            ["Hawaiian Standard Time"] = "Pacific/Honolulu",
            ["Hawaii-Aleutian Standard Time"] = "Pacific/Honolulu",
            ["Alaskan Standard Time"] = "America/Anchorage",
            ["UTC"] = "UTC",
        };

        public static IReadOnlyList<TimeZoneOptionDto> TimeZones()
        {
            var now = DateTime.UtcNow;
            return Zones
                .Select(z => new { z.Id, z.Label, Offset = TryOffset(z.Id, now) })
                .OrderBy(z => z.Offset ?? TimeSpan.Zero)
                .ThenBy(z => z.Label, StringComparer.Ordinal)
                .Select(z => new TimeZoneOptionDto(
                    z.Id,
                    z.Offset is TimeSpan o ? $"(UTC{FormatOffset(o)}) {z.Label}" : z.Label,
                    z.Offset is TimeSpan off ? FormatOffset(off) : null))
                .ToList();
        }

        /// <summary>Canonical IANA ID from an IANA ID or a known Windows name; "" for blank input; null when not allowed.</summary>
        public static string? NormalizeTimeZone(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var v = value.Trim();
            if (ZoneById.TryGetValue(v, out var canonical)) return canonical;
            if (WindowsToIana.TryGetValue(v, out var iana)) return iana;
            return null;
        }

        private static TimeSpan? TryOffset(string ianaId, DateTime utcNow)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId).GetUtcOffset(utcNow); }
            catch (Exception) { return null; }   // host without tz data for this id
        }

        private static string FormatOffset(TimeSpan o) =>
            (o < TimeSpan.Zero ? "-" : "+") + o.Duration().ToString(@"hh\:mm");
    }
}
