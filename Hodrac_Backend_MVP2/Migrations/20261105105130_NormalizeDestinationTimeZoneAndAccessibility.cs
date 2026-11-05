using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <summary>
    /// Data-only: converts Destination.TimeZone from Windows names to IANA IDs and
    /// maps AccessibilityType onto the six allowed values (see DestinationReferenceData).
    /// Values not listed here are left as they are and show up under
    /// NonStandard in GET /api/admin/destination-options.
    /// </summary>
    public partial class NormalizeDestinationTimeZoneAndAccessibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Destinations" SET "TimeZone" = CASE trim("TimeZone")
                    WHEN 'W. Central Africa Standard Time' THEN 'Africa/Lagos'
                    WHEN 'Mauritius Standard Time'         THEN 'Indian/Mauritius'
                    WHEN 'South Africa Standard Time'      THEN 'Africa/Johannesburg'
                    WHEN 'E. Africa Standard Time'         THEN 'Africa/Nairobi'
                    WHEN 'Egypt Standard Time'             THEN 'Africa/Cairo'
                    WHEN 'Greenwich Standard Time'         THEN 'Africa/Abidjan'
                    WHEN 'Morocco Standard Time'           THEN 'Africa/Casablanca'
                    WHEN 'GMT Standard Time'               THEN 'Europe/London'
                    WHEN 'W. Europe Standard Time'         THEN 'Europe/Berlin'
                    WHEN 'Romance Standard Time'           THEN 'Europe/Paris'
                    WHEN 'Arabian Standard Time'           THEN 'Asia/Dubai'
                    WHEN 'India Standard Time'             THEN 'Asia/Kolkata'
                    WHEN 'Japan Standard Time'             THEN 'Asia/Tokyo'
                    WHEN 'Eastern Standard Time'           THEN 'America/New_York'
                    WHEN 'Central Standard Time'           THEN 'America/Chicago'
                    WHEN 'Mountain Standard Time'          THEN 'America/Denver'
                    WHEN 'Pacific Standard Time'           THEN 'America/Los_Angeles'
                    WHEN 'Hawaiian Standard Time'          THEN 'Pacific/Honolulu'
                    WHEN 'Hawaii-Aleutian Standard Time'   THEN 'Pacific/Honolulu'
                    WHEN 'Alaskan Standard Time'           THEN 'America/Anchorage'
                    ELSE "TimeZone" END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Destinations" SET "AccessibilityType" = CASE lower(trim("AccessibilityType"))
                    WHEN 'walk'             THEN 'Walk'
                    WHEN 'walking'          THEN 'Walk'
                    WHEN 'car'              THEN 'Car'
                    WHEN 'road'             THEN 'Car'
                    WHEN 'drive'            THEN 'Car'
                    WHEN 'driving'          THEN 'Car'
                    WHEN 'taxi'             THEN 'Car'
                    WHEN 'transit'          THEN 'Public transit'
                    WHEN 'public transit'   THEN 'Public transit'
                    WHEN 'public transport' THEN 'Public transit'
                    WHEN 'bus'              THEN 'Public transit'
                    WHEN 'train'            THEN 'Train'
                    WHEN 'rail'             THEN 'Train'
                    WHEN 'boat'             THEN 'Boat'
                    WHEN 'boat only'        THEN 'Boat'
                    WHEN 'ferry'            THEN 'Boat'
                    WHEN 'flight'           THEN 'Flight'
                    WHEN 'plane'            THEN 'Flight'
                    WHEN 'air'              THEN 'Flight'
                    ELSE "AccessibilityType" END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reverse of the time zone conversion. Accessibility merges
            // (Road→Car, Ferry/Boat Only→Boat, Transit→Public transit) can't be undone exactly.
            migrationBuilder.Sql("""
                UPDATE "Destinations" SET "TimeZone" = CASE "TimeZone"
                    WHEN 'Africa/Lagos'        THEN 'W. Central Africa Standard Time'
                    WHEN 'Indian/Mauritius'    THEN 'Mauritius Standard Time'
                    WHEN 'Asia/Tokyo'          THEN 'Japan Standard Time'
                    WHEN 'America/New_York'    THEN 'Eastern Standard Time'
                    WHEN 'America/Chicago'     THEN 'Central Standard Time'
                    WHEN 'America/Los_Angeles' THEN 'Pacific Standard Time'
                    WHEN 'Pacific/Honolulu'    THEN 'Hawaiian Standard Time'
                    ELSE "TimeZone" END;
                """);
            migrationBuilder.Sql("""
                UPDATE "Destinations" SET "AccessibilityType" = 'Transit' WHERE "AccessibilityType" = 'Public transit';
                """);
        }
    }
}
