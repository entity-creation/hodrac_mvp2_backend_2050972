using Hodrac_Backend_MVP2.Models;
using Microsoft.Extensions.Logging;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// The Kenya country visa summary shared by KenyaNorthSeeder and MombasaSeeder, so the two
    /// country specs stay identical, plus a narrow refresh for databases seeded with an older text.
    ///
    /// Sources (checked 24 November 2026): etakenya.go.ke/how-to-apply (Nigeria is on the list of
    /// African nationals exempt for up to 60 days; Somalia and Libya are not); etakenya.go.ke/faqs
    /// (eTA US$30); gov.uk FCDO Kenya entry requirements (insurance from 5 October 2026, bought on
    /// insurance.ecitizen.go.ke or with the eTA, valid 90 days, foreign policies not accepted);
    /// visasnews.com (about US$52 including fees, 7 October 2026). The founder confirms first-hand
    /// that every inbound traveller, Nigerians included, must buy the insurance. FCDO lists
    /// exemptions for Kenyan residents, EAC citizens and airside transit passengers; the founder's
    /// wording is used here.
    /// </summary>
    public static class KenyaVisaText
    {
        public const string Current =
            "Nigerians and most other African nationals (except Somalia and Libya) visiting for 60 days or less don't need an Electronic Travel Authorisation (eTA), and nor do East African Community citizens. Most other visitors apply for an eTA online at etakenya.go.ke at least 2 weeks before travel; fees are roughly US$30-$50 depending on nationality and type. Since October 2026 every traveller, Nigerians included, must also buy Kenyan government travel health insurance before arrival, either on insurance.ecitizen.go.ke or as part of the eTA application. It covers 90 days and cost about US$52 including fees when it launched (confirm the current price on the portal); your own travel insurance isn't accepted in its place. Requirements change, so check official sources before you travel.";

        /// <summary>Texts earlier versions of the seeders wrote, verbatim. Only these are ever replaced.</summary>
        private static readonly string[] KnownOldValues =
        {
            // KenyaNorthSeeder (799b0b7 to ff39de5) and the first MombasaSeeder draft.
            "Most visitors need an Electronic Travel Authorisation (eTA), applied for online at etakenya.go.ke before travel — apply at least 2 weeks ahead; fees are roughly US$30-$50 depending on nationality and type. Most African nationals (except Somalia and Libya) and East African Community citizens are exempt. From October 2026 most visitors must also buy Kenyan government travel health insurance as part of the eTA application. Requirements change — check official sources before you travel.",
        };

        /// <summary>
        /// Replaces the stored Kenya visa summary with <see cref="Current"/> only when it still equals a
        /// text a seeder wrote earlier, so a value edited in the admin is never overwritten.
        /// </summary>
        /// <returns>True when the value was changed (the caller saves).</returns>
        public static bool RefreshIfUnchanged(Country country, ILogger? logger)
        {
            var stored = country.VisaRequirementsSummary;
            if (stored == Current) return false;

            if (stored != null && KnownOldValues.Contains(stored))
            {
                country.VisaRequirementsSummary = Current;
                logger?.LogInformation("Kenya visa summary refreshed from an older seeded text to the current text.");
                return true;
            }

            logger?.LogInformation("Kenya visa summary differs from every seeded text (edited in the admin?); left unchanged.");
            return false;
        }
    }
}
