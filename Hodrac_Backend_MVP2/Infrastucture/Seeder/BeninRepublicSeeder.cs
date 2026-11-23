using System.Text.Encodings.Web;
using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.DescriptionDtos;
using Hodrac_Backend_MVP2.Infrastructure.Seeder; // ToTitleCase extension
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastucture.Seeder
{
    /// <summary>
    /// "Benin Republic: Ganvié, Ouidah and Porto-Novo", a 5-day template wishlist for Nigerian
    /// travellers, by road from Lagos over the Seme-Kraké border (or a short Lagos-Cotonou flight):
    /// Cotonou and the Amazon Monument, the stilt village of Ganvié on Lake Nokoué, Ouidah's Python
    /// Temple, Sacred Forest of Kpassè and the Route des Esclaves to the Door of No Return, then
    /// Porto-Novo's Great Mosque, Musée Honmè and the Songhai Centre on the way back to the border.
    /// Optional extensions: the Royal Palaces of Abomey (UNESCO) and Grand-Popo's beach and Bouche du Roy.
    /// Fully idempotent: every row is looked up by its natural key first and only created when
    /// missing, so running it any number of times converges on the same data.
    /// The country is "Benin" (the Republic). Nigeria's "Benin City" (a City row in AfricaPocSeeder) is
    /// unrelated and never touched. Benin, XOF and Fon are new; French and Yoruba reuse the exact natural
    /// keys and specs AfricaPocSeeder uses (only created if missing).
    /// All prices are rough USD approximations of CFA fees from travel guides and traveller reports
    /// (no official 2026 tariffs were found) and are labelled approximate; confirm locally.
    /// </summary>
    public static class BeninRepublicSeeder
    {
        private static string BlobBase => SeedImages.BaseUrl;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            // Stored in jsonb, never rendered as raw HTML — keep accents/apostrophes readable.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Spec records
        // ═══════════════════════════════════════════════════════════════════════

        private sealed record LanguageSpec(string Name, string Code, (string Phrase, string Local)[] Phrases);

        private sealed record CurrencySpec(string Code, string Name, string Symbol, decimal RateToUsd);

        private sealed record CountrySpec(
            string Name, string Slug, string Continent, string Flag, string Visa, string Plugs,
            string DrivingSide, decimal TaxRate, string[] LanguageNames, string CurrencyCode, string TimeZone);

        private sealed record CitySpec(string Name, string CountryName, double Lat, double Lng, string Description);

        private sealed record DestSpec(
            string Region, string Name, string CountryName, string CityName, string[] LanguageNames,
            double Lat, double Lng, decimal AvgCost, string AccessibilityType, int Safety,
            string[] Categories, string[] Tags, string[] Alternates, string ShotContext, DescriptionJsonDto Desc);

        private sealed record ItemSpec(
            string Time, string Title, string Description, decimal Cost, string Badge,
            string? DestinationName = null, bool Optional = false);

        private sealed record DaySpec(int Number, string Title, string MorningCity, string AfternoonCity, string EveningCity, ItemSpec[] Items);

        private sealed record WishlistSpec(
            string Region, string Name, string CountryName, string Description, string ShortStory,
            string PeopleType, string[] Inclusions, string Keywords, string[] Vibes,
            decimal BasePricePerPerson, decimal Deposit, string Accommodation, string Transit,
            string Activities, bool IsFeatured, string Persona, DaySpec[] Days);

        private static DescriptionJsonDto Desc(
            string overview, string localPerspective, string directions, string whatToKnow,
            string thingsToBeWaryOf, string hiddenCost, string[] nearby, string bestTime,
            string crowd, string accessibility, string duration) => new()
            {
                Overview = overview,
                LocalPerspective = localPerspective,
                Directions = directions,
                WhatToKnow = whatToKnow,
                ThingsToBeWaryOf = thingsToBeWaryOf,
                HiddenCost = hiddenCost,
                NearbyComplements = nearby.ToList(),
                BestTimeToVisit = bestTime,
                CrowdLevel = crowd,
                Accessibility = accessibility,
                IdealDuration = duration
            };

        // ═══════════════════════════════════════════════════════════════════════
        //  Reference data
        // ═══════════════════════════════════════════════════════════════════════

        // "Benin" is the official short name of the Republic of Benin. No other seeder defines a
        // Country row with this name (Nigeria's "Benin City" is a City row under Nigeria).
        private const string Benin = "Benin";

        private const string French = "French";   // existing natural key (AfricaPocSeeder), same name/code
        private const string Yoruba = "Yoruba";   // existing natural key (AfricaPocSeeder), same name/code
        private const string Fon = "Fon";         // new

        private const string Cotonou = "Cotonou";
        private const string Ouidah = "Ouidah";
        private const string PortoNovo = "Porto-Novo";
        private const string Abomey = "Abomey";
        private const string GrandPopo = "Grand-Popo";

        // Names checked against every other seeder; none collide.
        private const string AmazonName = "Amazon Monument (Cotonou)";
        private const string GanvieName = "Ganvié Lake Village";
        private const string SlaveRouteName = "Route des Esclaves & Door of No Return";
        private const string PythonTempleName = "Temple of Pythons (Ouidah)";
        private const string OuidahMuseumName = "Ouidah Museum of History";
        private const string KpasseName = "Sacred Forest of Kpassè";
        private const string GreatMosqueName = "Great Mosque of Porto-Novo";
        private const string HonmeName = "Musée Honmè (King Toffa's Palace)";
        private const string SonghaiName = "Songhai Centre";
        private const string AbomeyPalacesName = "Royal Palaces of Abomey";
        private const string GrandPopoName = "Grand-Popo Beach & Bouche du Roy";

        private static readonly LanguageSpec[] Languages =
        {
            // French and Yoruba: exactly the AfricaPocSeeder specs, only created if missing.
            new(French, "fr", new[] { ("Hello", "Bonjour"), ("Thank you", "Merci") }),
            new(Yoruba, "yo", new[] { ("Thank you", "Ẹ ṣé"), ("How much?", "Ẹ̀lò ni?") }),
            // Fon greeting exchange as given by lepetitjournal.com/cotonou ("Comment saluer en langue fon").
            new(Fon, "fon", new[] { ("Greeting (did you wake well?)", "A fɔn ganji à?"), ("Reply (I woke well)", "Un fɔn ganji") }),
        };

        // XOF is pegged to the euro (1 EUR = 655.957 XOF). Approximate mid-market rate, November 2026
        // (converters showed roughly 589-596 XOF per USD in early/mid November 2026).
        private static readonly CurrencySpec[] Currencies =
        {
            new("XOF", "West African CFA franc", "CFA", 590m),
        };

        // Visa: ECOWAS free-movement protocol; ECOWAS citizens enter visa-free for up to 90 days
        // (Wikipedia "Visa policy of Benin"; yen.com.gh / legit.ng 2026: "ECOWAS rules govern their entry
        // separately"). Yellow fever card checked at entry (yen.com.gh 2026). Non-ECOWAS e-visa portal:
        // evisa.gouv.bj (listed by the Benin embassy in Japan).
        // Plugs: Type C and E, 220 V / 50 Hz (power-plugs-sockets.com; Wikipedia "Mains electricity by country").
        // Driving side: right. Time zone: Africa/Porto-Novo is not in DestinationReferenceData's curated
        // list; Africa/Lagos is the same zone (WAT, UTC+1, no DST). Tax: VAT (TVA) 18%.
        private static readonly CountrySpec[] Countries =
        {
            new(Benin, "benin", "Africa", "🇧🇯",
                "Nigerians and other ECOWAS citizens don't need a visa for stays of up to 90 days under the ECOWAS free-movement protocol. Carry a valid passport (ideally with at least 6 months' validity) and your yellow fever vaccination card, which is checked at the border. Most other nationalities need an e-visa from Benin's official portal (evisa.gouv.bj) before travel. Requirements change, so check official sources before you travel.",
                "Type C/E", "Right", 0.18m, new[] { French, Fon, Yoruba }, "XOF", "Africa/Lagos"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Cotonou, Benin, 6.3654, 2.4183,
                "Benin's largest city, economic capital and seat of government, on the coast between Lake Nokoué and the Atlantic. It is about 37 km from the Seme-Kraké border with Nigeria and has the country's main international airport, with short flights from Lagos."),
            new(Ouidah, Benin, 6.3631, 2.0851,
                "A small coastal town about 40 km west of Cotonou. It was one of the busiest slave-trading ports on the old Slave Coast and is now a centre of Vodun. Visitors come for the Python Temple, the sacred forest, the history museum and the 4 km Route des Esclaves to the Door of No Return, and the town fills up for Vodun Days every January."),
            new(PortoNovo, Benin, 6.4969, 2.6289,
                "Benin's official capital, on a lagoon about 30 km east of Cotonou and close to the Nigerian border. It is quieter and older than Cotonou, with Afro-Brazilian houses, museums, a royal palace and the Songhai Centre. Many residents are Yoruba or Gun, and the city has long historic ties to Lagos and Badagry."),
            new(Abomey, Benin, 7.1829, 1.9912,
                "An inland town about 140 km north of Cotonou and the former capital of the Kingdom of Dahomey, whose twelve kings ruled from 1625 to 1900. Its earthen royal palaces are a UNESCO World Heritage Site. The road from the coast runs via neighbouring Bohicon."),
            new(GrandPopo, Benin, 6.2800, 1.8200,
                "A beach town on Benin's west coast near the Togo border, about 80 km west of Cotonou, where the Mono River reaches the Atlantic at the Bouche du Roy. Cotonou residents come here for weekends. The ocean currents are strong."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] FonLangs = { French, Fon };
        private static readonly string[] PortoNovoLangs = { French, Fon, Yoruba };

        private const string CashTips =
            "Money: Benin uses the West African CFA franc (XOF), not the naira, and small sites take cash only. Change some money before you go or at a licensed bureau or bank in Cotonou. ATMs in Cotonou generally take Visa, but Nigerian naira cards may not work abroad, so check with your bank. Street changers work at the border; count your money and change only small amounts with them.";

        private const string GuideTip =
            "Guides: An accredited local guide can explain the historic and Vodun sites. Agree the fee before you start, and ask before photographing shrines, ceremonies or people.";

        private static readonly DestSpec[] Destinations =
        {
            // ─── Cotonou ───────────────────────────────────────────────────────
            // Sources: en.wikipedia.org/wiki/Amazon_Monument_(Benin) (30 m, ~150 t, bronze over metal frame,
            // Li Xiangqun, unveiled 30 July 2022, coords 6.34889, 2.40750); gouv.bj/attraction/monument-amazone;
            // gouv.bj/article/1871 (Esplanade des Amazones on Boulevard de la Marina). Access rules: confirm.
            new("benin-republic", AmazonName, Benin, Cotonou, FonLangs, 6.3489, 2.4075,
                5m, "Car", 2,
                new[] { "landmark_monument", "viewpoint_scenic_spot" },
                new[] { "history", "photography", "cultural", "tourist_hotspot", "family_friendly" },
                new[] { "Amazon Statue", "Amazone Statue", "Monument de l'Amazone", "Statue de l'Amazone", "Esplanade des Amazones", "Amazon Monument Benin" }, "Exterior",
                Desc(
                    "The Amazon Monument is a 30-metre bronze statue of a woman warrior on the Esplanade des Amazones, beside the Atlantic in central Cotonou. Unveiled on 30 July 2022, it honours the Agojie, the all-female army of the Kingdom of Dahomey, often called the Dahomey Amazons, whose story was told in the film 'The Woman King'. It is one of the tallest statues in Africa.\n\nWhat to Do: Walk the esplanade and take photos with the ocean behind the statue. It makes a good first stop before the Dahomey history in Ouidah and Abomey.",
                    "There was public debate in Benin over who the statue depicts; the government says it represents an Amazon warrior and not a specific queen.",
                    "Best Access: On the seafront off Boulevard de la Marina, near the Presidential Palace and Independence Square in central Cotonou.\n\nBy Taxi / Zem: Ask for 'Esplanade des Amazones' or 'la statue de l'Amazone'.",
                    "Entry: The esplanade is public and generally free. Access around the presidential district can change, so follow any security instructions.\n\nPhotography: Photos of the statue are fine; don't photograph the Presidential Palace or security staff.\n\n" + CashTips,
                    "Heat: The esplanade has little shade at midday, so go early or in the late afternoon.\n\nSecurity Zone: Government buildings are nearby; stay in public areas.",
                    "Entry: free (approx.).\nTaxi from central Cotonou: approx. 1,000-3,000 CFA (US$2-$5).",
                    new[]
                    {
                        "Ganvié Lake Village: About 45 minutes north on Lake Nokoué.",
                        "Route des Esclaves & Door of No Return: About an hour west in Ouidah.",
                        "Great Mosque of Porto-Novo: About an hour east."
                    },
                    "November to March (the dry season); late afternoon for softer light.",
                    "Medium (5/10).",
                    "Rating: 7/10. Open, paved esplanade.",
                    "30 minutes to 1 hour.")),

            // Sources: en.wikivoyage.org/wiki/Ganvie (coords 6.466667, 2.416667; boats launch from Abomey-Calavi,
            // ~35 min ride; guided tours ~10,000 CFA, 2-3 hrs); saigatours.com "How to visit Ganvié" (ticket booth
            // at the Abomey-Calavi pier, 10,000 CFA incl. pirogue); guardian.ng "floating market in Ganvié"
            // (5,000-10,000 CFA return with guide, varies); takeyourbackpack.com. No official 2026 tariff found.
            new("benin-republic", GanvieName, Benin, Cotonou, FonLangs, 6.4667, 2.4167,
                25m, "Boat", 2,
                new[] { "cultural_site", "activity_experience", "market_street_life" },
                new[] { "cultural", "photography", "tourist_hotspot", "history", "educational" },
                new[] { "Ganvie", "Ganvie Village", "Ganvié", "Venice of Africa", "Ganvie Stilt Village", "Lake Nokoue", "Lac Nokoué", "Abomey-Calavi" }, "Landscape",
                Desc(
                    "Ganvié is a village built on stilts in Lake Nokoué, north of Cotonou, often called the 'Venice of Africa'. Tens of thousands of Tofinu people live in wooden houses above the water and travel by pirogue between homes, schools, churches and a floating market where traders sell from their canoes. According to local tradition, the community moved onto the lake centuries ago to escape slave raiders from the Dahomey kingdom, who would not follow them onto the water.\n\nWhat to Do: Take a pirogue tour from the Abomey-Calavi pier through the village and the floating market, stop at a stilt restaurant or hotel, and hear the village's history from a local guide.",
                    "People live and work here, and many residents dislike being photographed without being asked. For a quieter lake trip, locals suggest the stilt villages of Les Aguégués near Porto-Novo.",
                    "Best Access: Drive or take a taxi from Cotonou to the embarcadère (pier) at Abomey-Calavi, about 30-45 minutes depending on traffic.\n\nBy Boat: Motorised or paddled pirogues leave from the pier. The crossing takes about 35 minutes each way and tours last 2-3 hours.",
                    "Tickets: Buy your boat ticket at the official booth at the Abomey-Calavi pier. Around 10,000 CFA per person including the pirogue is widely reported, though sources range from 5,000 to 10,500 CFA. Confirm the price and route before boarding.\n\nStaying Over: Simple stilt hotels in the village (such as Chez M) have basic rooms if you want to see the lake at dawn.\n\n" + CashTips,
                    "Touts: Ignore freelancers at the pier and pay at the ticket office.\n\nPhotography: Ask before photographing people; some expect a small payment.\n\nWater Safety: Pirogues sit low in the water. Keep phones and cameras in a dry bag and wear a life jacket if one is offered.",
                    "Boat Tour with Guide: approx. 5,000-10,500 CFA (US$8-$18) per person.\nPrivate Group Tour: approx. 20,000 CFA (US$34) per boat, negotiable.\nTaxi Cotonou-Abomey-Calavi: approx. 3,000-6,000 CFA (US$5-$10) each way.\nDrinks / Lunch on Stilts: approx. 2,000-5,000 CFA (US$3-$8).",
                    new[]
                    {
                        "Amazon Monument (Cotonou): Back in central Cotonou.",
                        "Route des Esclaves & Door of No Return: An hour west in Ouidah.",
                        "Songhai Centre: In Porto-Novo, about an hour east."
                    },
                    "November to March (dry season). Early morning has calmer water and the busiest floating market.",
                    "High (7/10). Benin's most visited site.",
                    "Rating: 3/10. Boarding pirogues from a pier; steps and walkways in the village.",
                    "Half a day.")),

            // ─── Ouidah ────────────────────────────────────────────────────────
            // Sources: benin.bj "Tourism in Ouidah: a practical guide" (route ~4 km from the centre to the beach,
            // just over an hour on foot, memorials and statues; gate faces the Atlantic; accredited guide
            // recommended; Ouidah ~40 km / ~1 hr from Cotonou); en.wikivoyage.org/wiki/Ouidah (Door of No Return
            // 6.324275, 2.089619); takeyourbackpack.com (route has no entrance fee). Vodun Days: gouv.bj
            // (8-10 Jan 2026), beninwebtv.bj (2-9 Jan 2027).
            new("benin-republic", SlaveRouteName, Benin, Ouidah, FonLangs, 6.3243, 2.0896,
                10m, "Car", 2,
                new[] { "landmark_monument", "historical_tour", "beach_coast" },
                new[] { "history", "cultural", "educational", "photography", "tourist_hotspot" },
                new[] { "Door of No Return", "Porte du Non-Retour", "Gate of No Return", "Route des Esclaves", "Slave Route Ouidah", "Route of Slaves", "Route des Captifs", "Point of No Return Ouidah" }, "Exterior",
                Desc(
                    "The Route des Esclaves is the road of about 4 km from the old slave market in Ouidah's centre down to the beach, the path along which enslaved Africans were marched to the ships. Memorials and Vodun statues line the way, including the Tree of Forgetting and the Zoungbodji memorial, and the route ends at the Door of No Return, a memorial arch facing the Atlantic. Badagry's slave-route sites lie just across the border in Nigeria.\n\nWhat to Do: Walk or drive the route with a guide who can explain each stop, and end at the Door of No Return in the late afternoon or at dawn.",
                    "The route is a place of remembrance for Beninese and for descendants of the diaspora, some of whom come to mark their return. Ceremonies are held here during Vodun Days.",
                    "Best Access: Ouidah is about 40 km (roughly an hour) west of Cotonou by taxi, bus or hire car on a tarmac road. The route starts in the town centre and runs south to the beach.\n\nOn Foot: Just over an hour one way. Many visitors walk down and take a zem (moto-taxi) or car back.",
                    "Entry: The route and the Door of No Return are open-air memorials with no reported entrance fee; guides charge separately.\n\nVodun Days: Ouidah's Vodun Days festival runs in early January (8-10 January in 2026; announced for 2-9 January 2027). The town fills up, so book early.\n\n" + GuideTip,
                    "Heat: The beach end is exposed. Carry water, a hat and sunscreen.\n\nSwimming: The Atlantic here has strong currents and waves; don't swim off the memorial beach.\n\nRespect: This is a memorial to the dead, so dress and behave accordingly.",
                    "Entry: free (approx.).\nLocal Guide: approx. 5,000-10,000 CFA (US$8-$17), agreed in advance.\nZem back to town: approx. 500-1,000 CFA (US$1-$2).",
                    new[]
                    {
                        "Temple of Pythons (Ouidah): In the town centre.",
                        "Ouidah Museum of History: In the old Portuguese fort.",
                        "Sacred Forest of Kpassè: A short walk from the centre."
                    },
                    "November to March (dry season); late afternoon or dawn at the Door of No Return; early January for Vodun Days.",
                    "Medium (5/10); very busy during Vodun Days.",
                    "Rating: 6/10. Flat tarmac road; the beach end is sandy.",
                    "2 to 3 hours.")),

            // Sources: benin.bj Ouidah guide (temple across a square from the Basilica of the Immaculate Conception;
            // royal pythons kept in a hut behind the wall; active sanctuary, visitors enter barefoot);
            // en.wikivoyage.org/wiki/Ouidah (Basilica 6.360614, 2.084896); traveller reports via wanderlog /
            // takeyourbackpack (fee ~1,000-2,000 CFA + photo fee). No official 2026 fee found.
            new("benin-republic", PythonTempleName, Benin, Ouidah, FonLangs, 6.3604, 2.0855,
                5m, "Car", 2,
                new[] { "religious_site", "cultural_site" },
                new[] { "cultural", "quirky", "photography", "tourist_hotspot", "educational" },
                new[] { "Python Temple", "Temple des Pythons", "Temple of the Pythons", "Dangbé Temple", "Python Temple Ouidah", "Snake Temple Ouidah" }, "Interior",
                Desc(
                    "The Temple of Pythons is an active Vodun sanctuary in the centre of Ouidah, dedicated to Dangbé, the python deity. Royal pythons, which are harmless to people and considered sacred, live in a hut behind the temple walls, and guides often place one around a visitor's shoulders. The temple faces the Catholic Basilica of the Immaculate Conception across the square.\n\nWhat to Do: Take the short guided visit, hear about Dangbé and Vodun belief, and hold a python if you want to.",
                    "Pythons are respected in Ouidah. If one wanders into a home, people traditionally call the temple to collect it.",
                    "Best Access: In Ouidah town centre, opposite the Basilica of the Immaculate Conception, within walking distance of the market and the start of the Route des Esclaves.\n\nBy Zem: Ask for 'le Temple des Pythons'.",
                    "Entry: A small fee. Traveller reports range from about 1,000 to 2,000 CFA per person, often with an extra fee for photos. Confirm at the gate.\n\nEtiquette: It is an active sanctuary. You'll be asked to remove your shoes; follow the guide's instructions.\n\n" + GuideTip,
                    "Snakes: The pythons are non-venomous, but don't handle them without the guide.\n\nTips: Guides expect small tips, so keep coins and small notes ready.",
                    "Entry: approx. 1,000-2,000 CFA (US$2-$4).\nPhoto Fee: approx. 1,000-2,000 CFA extra.\nGuide Tip: approx. 500-1,000 CFA.",
                    new[]
                    {
                        "Sacred Forest of Kpassè: A short walk away.",
                        "Ouidah Museum of History: In the old Portuguese fort nearby.",
                        "Route des Esclaves & Door of No Return: Starts in the town centre."
                    },
                    "Year-round; the dry season (November-March) is most comfortable.",
                    "Medium (5/10).",
                    "Rating: 6/10. Small, flat site; shoes off inside.",
                    "30 minutes to 1 hour.")),

            // Sources: en.wikivoyage.org/wiki/Ouidah (Fort of São João Baptista de Ajudá houses the museum,
            // 6.358889, 2.09; tel +229 21 34 10 21); lonelyplanet.com (fort dates from 1721);
            // gouv.bj article 390/1644 and justiceinfo.net (fort rehabilitation works in the 2020s; reopening dates
            // projected, not confirmed); wanderlog (listed 'temporarily closed'); traveller fees ~1,000-1,500 CFA.
            // Current opening status NOT verified; every mention is hedged.
            new("benin-republic", OuidahMuseumName, Benin, Ouidah, FonLangs, 6.3589, 2.0900,
                5m, "Car", 2,
                new[] { "museum_gallery", "historical_tour" },
                new[] { "history", "educational", "cultural", "architecture" },
                new[] { "Musée d'Histoire de Ouidah", "Ouidah History Museum", "Portuguese Fort Ouidah", "Fort Portugais", "Fort São João Baptista de Ajudá", "Ouidah Museum" }, "Exterior",
                Desc(
                    "The Ouidah Museum of History is in the Portuguese fort of São João Baptista de Ajudá, built in the early 18th century when Ouidah was one of the busiest slave ports in West Africa. Its displays cover the Atlantic slave trade, the Kingdom of Dahomey and the cultural links between Benin and the Americas, especially Brazil, Cuba and Haiti, where Vodun traditions travelled with enslaved people.\n\nWhat to Do: Take the guided tour before you walk the Route des Esclaves.",
                    "The fort is part of a government programme to restore Ouidah's historic sites and build new memorial museums, so the exhibits and access may change.",
                    "Best Access: In Ouidah town, a short walk or zem ride from the Python Temple and the start of the Route des Esclaves.",
                    "Opening Status: The fort has been under restoration and has been listed as temporarily closed in recent years. Confirm that it is open, and the current fee, with your guide or hotel before you go.\n\nEntry: Older traveller reports give about 1,000-1,500 CFA including a guided tour.\n\n" + GuideTip,
                    "Closures: If the museum is closed, the Route des Esclaves and the Python Temple still cover much of the same history.\n\nPhotography: Ask whether photos are allowed inside.",
                    "Entry: approx. 1,000-1,500 CFA (US$2-$3) if open; confirm.\nGuide Tip: approx. 500-1,000 CFA.",
                    new[]
                    {
                        "Route des Esclaves & Door of No Return: Starts nearby.",
                        "Temple of Pythons (Ouidah): A short walk away.",
                        "Sacred Forest of Kpassè: In the town centre."
                    },
                    "Year-round if open; the dry season (November-March) is most comfortable.",
                    "Low (3/10).",
                    "Rating: 5/10. Historic fort with uneven ground and steps.",
                    "1 hour.")),

            // Sources: benin.bj Ouidah guide (grove of centuries-old iroko trees with Vodun statues among the trunks;
            // named after King Kpassè, the town's founding figure; quietest spot in Ouidah); expedia / petitfute
            // listings (open to visitors). No published fee found; coordinates approximate (town centre).
            // The legend of Kpassè becoming a tree is widely told by guides; not checked against a primary source.
            new("benin-republic", KpasseName, Benin, Ouidah, FonLangs, 6.3650, 2.0830,
                5m, "Car", 2,
                new[] { "religious_site", "nature_outdoor", "cultural_site" },
                new[] { "cultural", "nature", "hidden_gem", "history", "photography" },
                new[] { "Kpasse Sacred Forest", "Forêt Sacrée de Kpassè", "Foret Sacree de Kpasse", "Kpassè Forest", "Kpasse Zoun", "Sacred Forest Ouidah" }, "Landscape",
                Desc(
                    "The Sacred Forest of Kpassè is a grove of centuries-old iroko trees in Ouidah, with Vodun sculptures set among the trunks. It is named after King Kpassè, the founding figure of the town, who according to local legend turned into one of the trees. The forest was once reserved for initiates and is now open to visitors. It is the quietest place in Ouidah.\n\nWhat to Do: Walk the shaded paths with a guide who can explain the statues and the deities they represent.",
                    "The forest is still sacred. Ceremonies are held here, especially around Vodun Days, and some areas remain closed to the uninitiated.",
                    "Best Access: In Ouidah, a short walk or zem ride from the Python Temple and the market.",
                    "Entry: A small fee and a guide are usual. No price is published, so confirm on site.\n\nRestricted Areas: Follow any signs or instructions about where visitors may not go.\n\n" + GuideTip,
                    "Photography: Some statues or rituals may need permission to photograph.\n\nMosquitoes: The grove is shady and humid, so bring repellent.",
                    "Entry + Guide: approx. 1,000-3,000 CFA (US$2-$5); unverified, confirm.\nGuide Tip: approx. 500-1,000 CFA.",
                    new[]
                    {
                        "Temple of Pythons (Ouidah): Nearby in the centre.",
                        "Ouidah Museum of History: A short ride away.",
                        "Route des Esclaves & Door of No Return: South to the beach."
                    },
                    "Year-round; the dry season (November-March) is most comfortable.",
                    "Low (3/10).",
                    "Rating: 5/10. Flat earth paths.",
                    "30 minutes to 1 hour.")),

            // ─── Porto-Novo ────────────────────────────────────────────────────
            // Sources: en.wikivoyage.org/wiki/Porto-Novo (Great Mosque 6.470333, 2.626611, "must-see";
            // Cotonou-Porto-Novo ~45 min to 1 hr); audiala.com/fr/benin/porto-novo (built in 1912 by agudá artisans,
            // Afro-Brazilian returnees). Visiting rules and fees not published; hedged.
            new("benin-republic", GreatMosqueName, Benin, PortoNovo, PortoNovoLangs, 6.4703, 2.6266,
                3m, "Car", 2,
                new[] { "religious_site", "landmark_monument" },
                new[] { "architecture", "history", "photography", "cultural", "hidden_gem" },
                new[] { "Grande Mosquée de Porto-Novo", "Porto-Novo Mosque", "Porto Novo Great Mosque", "Great Mosque Porto Novo", "Grande Mosquee Porto Novo" }, "Exterior",
                Desc(
                    "The Great Mosque of Porto-Novo has a brightly painted, church-like facade in the Afro-Brazilian style. Agudá craftsmen built it in the early 20th century (one source gives 1912); the agudá were descendants of formerly enslaved people who returned from Brazil and brought Brazilian Baroque building styles with them. Lagos's Brazilian Quarter has buildings in the same style.\n\nWhat to Do: Look at and photograph the facade, and ask politely whether a caretaker can show you inside outside prayer times.",
                    "It is a working mosque for Porto-Novo's Muslim community, many of whom are Yoruba. Visitors are welcome to look, and prayer times take priority.",
                    "Best Access: In central Porto-Novo, near the Musée da Silva and Musée Honmè. Porto-Novo is about 30 km (45 minutes to an hour) east of Cotonou by taxi or minibus.",
                    "Entry: You can view the mosque from outside for free. Whether you can go in is up to the caretakers. Dress modestly (cover shoulders and knees; women may need a headscarf) and avoid prayer times, especially Friday midday.\n\n" + CashTips,
                    "Photography: Fine from outside; ask before photographing worshippers.\n\nDonation: If someone shows you inside, a small donation is customary.",
                    "Entry: free to view (approx.).\nDonation if shown inside: approx. 1,000-2,000 CFA (US$2-$4).",
                    new[]
                    {
                        "Musée Honmè (King Toffa's Palace): A short walk away.",
                        "Songhai Centre: On the edge of the city.",
                        "Ganvié Lake Village: An hour west via Cotonou."
                    },
                    "Year-round; mornings outside prayer times.",
                    "Low (3/10).",
                    "Rating: 6/10. Viewed from the street.",
                    "30 minutes.")),

            // Sources: en.wikivoyage.org/wiki/Porto-Novo (Musée Honmè / Palais Royal du Roi Toffa, 6.46931, 2.624985,
            // former residence of King Toffa); routard / africa.com Benin guides ("Don't miss the Honmè Palace").
            // King Toffa's 1880s protectorate treaty with France is widely reported. No published fee or hours.
            new("benin-republic", HonmeName, Benin, PortoNovo, PortoNovoLangs, 6.4693, 2.6250,
                5m, "Car", 2,
                new[] { "museum_gallery", "historical_tour", "cultural_site" },
                new[] { "history", "cultural", "architecture", "educational", "hidden_gem" },
                new[] { "Musee Honme", "Honmè Palace", "Honme Palace", "Palais Royal Honmè", "King Toffa Palace", "Palais du Roi Toffa", "Royal Palace Porto-Novo" }, "Exterior",
                Desc(
                    "Musée Honmè is the former royal palace of the kings of Porto-Novo (the Hogbonou kingdom), best known as the home of King Toffa, who signed a protectorate treaty with France in the 1880s. It is now a museum. Guided tours go through the courtyards, the queens' quarters and the ceremonial spaces, and explain court life, royal rituals and the kingdom's links with its Yoruba neighbours.\n\nWhat to Do: Take the guided tour of the palace courtyards and combine it with the Great Mosque and the Musée da Silva nearby.",
                    "Porto-Novo's royal history overlaps with Yoruba history; the city's Yoruba name is Àjàṣẹ́, and Nigerian visitors often recognise names and customs here.",
                    "Best Access: In central Porto-Novo, within walking distance of the Great Mosque.\n\nBy Zem / Taxi: Ask for 'Musée Honmè' or 'Palais du Roi Toffa'.",
                    "Entry: A small fee that includes a guided tour. No current price or opening hours are published; confirm on arrival.\n\n" + GuideTip,
                    "Photography: Some rooms may restrict photos, so ask the guide.\n\nOpening Hours: Small museums may close at lunchtime and on some days.",
                    "Entry with Guide: approx. 1,000-3,000 CFA (US$2-$5); unverified, confirm.\nGuide Tip: approx. 500-1,000 CFA.",
                    new[]
                    {
                        "Great Mosque of Porto-Novo: A short walk away.",
                        "Songhai Centre: On the edge of the city.",
                        "Amazon Monument (Cotonou): An hour west."
                    },
                    "Year-round; mornings are cooler.",
                    "Low (2/10).",
                    "Rating: 5/10. Courtyards, thresholds and some steps.",
                    "1 hour.")),

            // Sources: lonelyplanet.com/points-of-interest/centre-songhai (research, teaching and production centre
            // in sustainable farming; one-hour guided tours of plantations and workshops; two restaurants, older
            // prices ~1,200 CFA maquis mains, 2,500-3,500 CFA restaurant mains); takeyourbackpack.com (book ahead
            // Dec-Feb, start by 8 a.m.). Founded in 1985 by Father Godfrey Nzamujo (widely reported). Tour fee not
            // published; coordinates approximate (Ouando area).
            new("benin-republic", SonghaiName, Benin, PortoNovo, PortoNovoLangs, 6.4990, 2.6200,
                15m, "Car", 1,
                new[] { "activity_experience", "food_experience", "nature_outdoor" },
                new[] { "educational", "food_focused", "family_friendly", "local_favorite", "nature" },
                new[] { "Centre Songhaï", "Centre Songhai", "Songhai Center", "Songhai Farm", "Songhai Porto-Novo", "Songhai Centre Porto Novo" }, "Landscape",
                Desc(
                    "The Songhai Centre in Porto-Novo is a large farm and training centre for sustainable agriculture, founded in 1985 by the Nigerian-born Dominican priest Father Godfrey Nzamujo. Its crops, livestock, fish ponds, food processing and workshops are linked so that waste from one feeds another, and centres on the same model have opened in other African countries, including Nigeria.\n\nWhat to Do: Take the one-hour guided tour of the plantations and workshops, eat at one of the on-site restaurants, and buy juice, yoghurt, honey and other farm products at the shop.",
                    "Porto-Novo families and school groups visit often, and many Nigerian visitors know of its founder.",
                    "Best Access: On the edge of Porto-Novo (Ouando area). Take a zem or taxi from the city centre.\n\nFrom Cotonou: About an hour by road, in the direction of the Seme-Kraké border.",
                    "Tours: Guided tours of about an hour run during the day. Book ahead in peak season (December-February) and start early to avoid the heat. No tour fee is published; confirm on arrival.\n\nFood: There is a cheap open-air maquis and a more formal restaurant on site (older guide prices: mains about 1,200 CFA at the maquis and 2,500-3,500 CFA in the restaurant).\n\n" + CashTips,
                    "Heat: Most of the tour is outdoors, so bring water and a hat.\n\nShop Hours: The farm shop may close earlier than the restaurants.",
                    "Guided Tour: approx. 1,000-3,000 CFA (US$2-$5); unverified, confirm.\nLunch: approx. 1,200-3,500 CFA (US$2-$6) per main.\nFarm Products: from about 500 CFA.",
                    new[]
                    {
                        "Great Mosque of Porto-Novo: In the city centre.",
                        "Musée Honmè (King Toffa's Palace): In the city centre.",
                        "Ganvié Lake Village: An hour west via Cotonou."
                    },
                    "Year-round; mornings for the tour.",
                    "Low-Medium (4/10).",
                    "Rating: 6/10. Flat farm paths, mostly outdoors.",
                    "2 to 3 hours including lunch.")),

            // ─── Abomey (optional extension) ───────────────────────────────────
            // Sources: en.wikipedia.org/wiki/Royal_Palaces_of_Abomey (coords 7°11'11"N 1°59'38"E; UNESCO 1985;
            // 47.6 ha; 12 kings 1625-1900; Ghezo and Glèlè palaces house the Historical Museum; bas-reliefs;
            // 26 statues looted in 1892 returned by France and slated for display in Abomey);
            // en.wikivoyage.org/wiki/Abomey (Musée Historique 2,500 CFA incl. ~1-hr guided tour; Mon-Fri 8:30-18:00,
            // Sat-Sun 8:30-17:00; Cotonou-Bohicon bush taxi ~3 hrs); whc.unesco.org/en/soc (provisional management
            // plan 2025-2026 during palace restoration and new museum construction); AFD project pages.
            // Appliqué banner workshops: Wikivoyage lists Chez Yemadje (family that embroidered for the kings).
            new("benin-republic", AbomeyPalacesName, Benin, Abomey, FonLangs, 7.1865, 1.9940,
                20m, "Car", 2,
                new[] { "historical_tour", "museum_gallery", "landmark_monument" },
                new[] { "history", "cultural", "educational", "architecture", "photography" },
                new[] { "Abomey Royal Palaces", "Palais Royaux d'Abomey", "Musée Historique d'Abomey", "Abomey Historical Museum", "Abomey Museum", "Dahomey Palaces", "Danxomè Palaces" }, "Exterior",
                Desc(
                    "The Royal Palaces of Abomey were the seat of the Kingdom of Dahomey, whose twelve kings ruled from 1625 to 1900 and each built a palace inside a large earthen-walled enclosure. The site has been on the UNESCO World Heritage list since 1985. It is known for its painted earthen bas-reliefs, which recorded the kingdom's history, and for the Historical Museum in the palaces of Kings Ghezo and Glèlè, which displays thrones, regalia and the history of the Agojie women warriors.\n\nWhat to Do: Take the guided museum tour, look at the bas-reliefs, and visit the craft workshops in town, such as Chez Yemadje, run by a family that once made appliqué banners for the kings.",
                    "Abomey is the centre of Fon culture. In 2021 France returned 26 royal objects looted in 1892, and a new museum is being built here to hold them.",
                    "Best Access: About 140 km north of Cotonou, roughly 2.5-3 hours by car via Bohicon or about 3 hours by bush taxi. A hired car and driver is the easiest option.\n\nFrom Ouidah: A similar drive; leave early for a day trip.",
                    "Entry: Wikivoyage lists 2,500 CFA including an hour-long guided tour, with opening hours Mon-Fri 8:30 AM-6:00 PM and Sat-Sun 8:30 AM-5:00 PM. Other sources quote more, so confirm on site.\n\nWorks in Progress: Palace restoration and construction of the new museum are under way (UNESCO notes a provisional management plan for 2025-2026), so some palaces or galleries may be closed. Check before making the long drive.\n\n" + GuideTip,
                    "Long Day: The round trip from the coast is long; consider a night in Abomey or Bohicon.\n\nPhotography: Usually not allowed inside the museum.",
                    "Entry with Guide: approx. 2,500 CFA (US$4), possibly more; confirm.\nCar and Driver for the day from Cotonou/Ouidah: approx. 40,000-60,000 CFA (US$70-$100) plus fuel.\nPhoto Permit (if offered): approx. 2,000 CFA.",
                    new[]
                    {
                        "Temple of Pythons (Ouidah): Back on the coast, about 3 hours south.",
                        "Amazon Monument (Cotonou): A bronze statue of an Agojie warrior.",
                        "Songhai Centre: In Porto-Novo."
                    },
                    "November to February (dry season, before the hottest months).",
                    "Low (3/10).",
                    "Rating: 4/10. Earthen courtyards and uneven ground.",
                    "Half a day on site; a full day including travel.")),

            // ─── Grand-Popo (optional extension) ───────────────────────────────
            // Sources: en.wikivoyage.org/wiki/Grand-Popo (coords 6°16'48"N 1°49'12"E; taxis from Cotonou's Grand
            // Marché; Bouche du Roy by motorboat, longer circuit generally from 40,000 CFA; ocean current 'very
            // strong', swim at your own risk). Distance from Cotonou (~80 km) and drive times approximate.
            new("benin-republic", GrandPopoName, Benin, GrandPopo, FonLangs, 6.2800, 1.8200,
                70m, "Car", 2,
                new[] { "beach_coast", "nature_outdoor", "activity_experience" },
                new[] { "relaxing", "nature", "hidden_gem", "couple_friendly", "photography" },
                new[] { "Grand Popo", "Grand-Popo Beach", "Bouche du Roy", "Bouches du Roy", "Mono River Mouth", "Grand Popo Beach Benin" }, "Landscape",
                Desc(
                    "Grand-Popo is a small beach town near the Togo border with a long, palm-lined Atlantic beach and several beachfront hotels. Nearby, the Mono River meets the sea at the Bouche du Roy, an area of sandbars and lagoon reached by boat past fishing villages and mangroves.\n\nWhat to Do: Spend time on the beach after the historic sites in Ouidah, and take a boat trip to the Bouche du Roy.",
                    "Cotonou families and expats come here at weekends, mostly for grilled fish and cold drinks on the beach.",
                    "Best Access: About 80 km west of Cotonou along the coastal highway (roughly 1.5 hours), or about 40 minutes beyond Ouidah. Shared taxis leave from the Grand Marché area in Cotonou; agree the fare first.\n\nOn Arrival: Zems run from the main junction to the beach hotels.",
                    "Boat Trips: Motorboat circuits to the Bouche du Roy generally start at around 40,000 CFA per boat. Share with others, and agree the route and price in advance.\n\nStay: Beachfront hotels and guesthouses range from simple to mid-range.\n\n" + CashTips,
                    "Swimming: The ocean current is very strong and there are rarely lifeguards. Paddle at most, and never go in alone or after drinking.\n\nSun: The beach has very little shade.",
                    "Bouche du Roy Boat Trip: approx. 40,000 CFA (US$68) per boat.\nBeach Hotel: approx. US$40-$120 per room per night.\nGrilled Fish Lunch: approx. 3,000-6,000 CFA (US$5-$10).",
                    new[]
                    {
                        "Route des Esclaves & Door of No Return: About 40 minutes east in Ouidah.",
                        "Temple of Pythons (Ouidah): In Ouidah town.",
                        "Amazon Monument (Cotonou): About 1.5 hours east."
                    },
                    "November to March (dry season).",
                    "Low (3/10); busier at weekends and holidays.",
                    "Rating: 4/10. Soft sand; getting in and out of boats takes some agility.",
                    "1 to 2 nights.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlist
        // ═══════════════════════════════════════════════════════════════════════

        // Getting there: Lagos to the Seme border ~85 km (rome2rio) and Seme to Cotonou ~37-45 km
        // (dlca.logcluster.org Seme-Krake profile: border open 24/7, ~37 km from Cotonou, car ~4 hrs from
        // Ikeja). Flights: Air Peace Lagos-Cotonou nonstop, ~40 min block time (flightsfrom.com, trip.com);
        // confirm the current schedule. Dantokpa relocation: AFP via kuwaittimes.com (Nov 2024), globalvoices.org (2025).
        // ItineraryItem.TimeOfDay is a controlled value (Morning/Afternoon/Evening, validated by the
        // admin and user itinerary endpoints), so any specific clock times go in the item titles.
        private static readonly WishlistSpec[] Wishlists =
        {
            new("benin-republic", "Benin Republic: Ganvié, Ouidah and Porto-Novo", Benin,
                "A 5-day trip to Benin for Nigerian travellers. ECOWAS citizens need no visa, only a passport and a yellow fever card. Cross from Lagos at the Seme-Kraké border, or take the short Lagos-Cotonou flight, to Cotonou and the 30-metre Amazon Monument to Dahomey's women warriors. Take a pirogue across Lake Nokoué to Ganvié, the stilt village known as the 'Venice of Africa', then spend two days in Ouidah at the Python Temple, the Sacred Forest of Kpassè and the 4 km Route des Esclaves to the Door of No Return, which pairs with the slave-route sites in Badagry. The last stop is Porto-Novo, the capital, for its Afro-Brazilian Great Mosque, King Toffa's palace and the Songhai Centre, close to the border for the drive home. Optional extensions add the UNESCO-listed Royal Palaces of Abomey and the beach at Grand-Popo, and an early-January trip coincides with Vodun Days in Ouidah. Bring CFA cash, allow extra time at the border, and confirm opening hours locally. All prices are rough USD estimates.",
                "Five days just across the border from Lagos: a stilt village on Lake Nokoué, Ouidah's python temple and Door of No Return, and the Afro-Brazilian mosque in Porto-Novo.",
                "Culture & History Lovers",
                new[] { "4 Nights in Mid-Range Hotels (Cotonou, Ouidah x2, Porto-Novo)", "Car with Driver from Lagos and Back (via Seme-Kraké)", "Ganvié Pirogue Tour", "Guided Ouidah Heritage Day", "Porto-Novo Museums & Songhai Centre Tour" },
                "Benin, Benin Republic, Republic of Benin, Cotonou, Ganvié, Ganvie, Lake Nokoué, Venice of Africa, stilt village, Abomey-Calavi, Ouidah, Door of No Return, Porte du Non-Retour, Route des Esclaves, slave route, Python Temple, Temple des Pythons, Kpassè, Sacred Forest, Vodun, Voodoo, Vodun Days, Ouidah Museum of History, Porto-Novo, Great Mosque, Afro-Brazilian, Musée Honmè, King Toffa, Songhai Centre, Abomey, Royal Palaces of Abomey, Dahomey, Amazons, Agojie, Woman King, Amazon Monument, Grand-Popo, Bouche du Roy, Seme border, Seme-Krake, Lagos to Cotonou, ECOWAS, CFA, weekend trip from Lagos",
                new[] { "Culture", "History", "Road Trip", "Off the Beaten Path" },
                650m, 150m,
                "4 nights (shared double) in mid-range hotels: 1 in Cotonou, 2 in Ouidah, 1 in Porto-Novo",
                "Car with driver from Lagos to Cotonou via the Seme-Kraké border, around southern Benin and back to Lagos; or the Lagos-Cotonou flight (about 40 minutes) with a local car and driver. Border delays vary and can add hours.",
                "Amazon Monument, Ganvié pirogue tour, Ouidah Python Temple, Sacred Forest of Kpassè, Route des Esclaves and Door of No Return with a guide, Porto-Novo Great Mosque and Musée Honmè, Songhai Centre guided tour",
                false, "Culture Seeker",
                new[]
                {
                    new DaySpec(1, "Lagos to Cotonou", Cotonou, Cotonou, Cotonou, new[]
                    {
                        new ItemSpec("Morning", "6:00 AM · Drive Lagos to Cotonou via Seme-Kraké", "Leave Lagos early. It is about 85 km to the Seme border along the Badagry Expressway, then roughly 40 km to Cotonou. Allow 3-5 hours or more, depending on Lagos traffic, checkpoints and the queue at the joint border post. Have your passport and yellow fever card ready, and change a little money into CFA.", 60m, "Road Trip"),
                        new ItemSpec("Morning", "Or: Fly Lagos to Cotonou", "Air Peace flies the route nonstop in about 40 minutes, which avoids the border queue. Check the current schedule and fares.", 150m, "Optional", null, true),
                        new ItemSpec("Afternoon", "Amazon Monument & the Marina", "Photos with the 30-metre bronze Agojie warrior beside the Atlantic, and a first look at Dahomey history.", 3m, "Landmark", AmazonName),
                        new ItemSpec("Afternoon", "Dantokpa Market (Check It's Still Operating)", "One of West Africa's largest open-air markets, beside the lagoon. The government has announced plans to relocate it, so check before you go, and keep valuables secure.", 0m, "Optional", null, true),
                        new ItemSpec("Evening", "Dinner in Cotonou", "Grilled fish or chicken with akassa or pâte and a cold drink. Ask your hotel for a busy local spot.", 12m, "Local Food"),
                    }),
                    new DaySpec(2, "Ganvié Stilt Village & on to Ouidah", Cotonou, Cotonou, Ouidah, new[]
                    {
                        new ItemSpec("Morning", "7:30 AM · Pirogue to Ganvié", "Drive to the Abomey-Calavi pier (30-45 minutes), buy your ticket at the official booth and cross Lake Nokoué to the stilt village and its floating market. Ask before photographing people.", 17m, "Lake Village", GanvieName),
                        new ItemSpec("Afternoon", "Drive West to Ouidah", "About an hour along the coast road. Check in and rest through the hottest part of the day.", 0m, "Road Trip"),
                        new ItemSpec("Evening", "Evening in Ouidah", "Walk around the small town centre and eat near the sea.", 10m, "Local Life"),
                    }),
                    new DaySpec(3, "Ouidah: Pythons, Sacred Forest & the Door of No Return", Ouidah, Ouidah, Ouidah, new[]
                    {
                        new ItemSpec("Morning", "Temple of Pythons", "An active Vodun sanctuary facing the basilica. Shoes come off, and you can hold a royal python. Small entry and photo fees.", 4m, "Vodun Sanctuary", PythonTempleName),
                        new ItemSpec("Morning", "Sacred Forest of Kpassè", "Iroko trees and Vodun statues in the grove named after Ouidah's founding king, with a local guide.", 3m, "Sacred Grove", KpasseName),
                        new ItemSpec("Afternoon", "Ouidah Museum of History (If Open)", "Slave-trade history inside the old Portuguese fort. It has been under restoration, so confirm it is open before you go.", 3m, "Optional", OuidahMuseumName, true),
                        new ItemSpec("Afternoon", "4:00 PM · Walk the Route des Esclaves", "Follow the 4 km road from the old auction square to the sea with a guide, past the Tree of Forgetting and the Zoungbodji memorial.", 12m, "Heritage Walk", SlaveRouteName),
                        new ItemSpec("Evening", "Sunset at the Door of No Return", "The memorial arch faces the Atlantic at the end of the route. Don't swim; the currents are strong.", 0m, "Memorial", SlaveRouteName),
                        new ItemSpec("Evening", "Or: Night at Grand-Popo", "Spend your second night at the beach in Grand-Popo, about 40 minutes west, and take a boat to the Bouche du Roy the next morning.", 70m, "Optional Extension", GrandPopoName, true),
                    }),
                    new DaySpec(4, "Porto-Novo, the Capital", Ouidah, PortoNovo, PortoNovo, new[]
                    {
                        new ItemSpec("Morning", "Drive Ouidah to Porto-Novo", "About 1.5-2 hours east through Cotonou, depending on traffic.", 0m, "Road Trip"),
                        new ItemSpec("Morning", "Or: Abomey Royal Palaces Day Trip", "Drive inland instead (about 3 hours each way) to the UNESCO-listed palaces of the Dahomey kings, then on to Porto-Novo late. Restoration work is under way, so check what is open first.", 20m, "Optional Extension", AbomeyPalacesName, true),
                        new ItemSpec("Afternoon", "Great Mosque of Porto-Novo", "An Afro-Brazilian facade built by returnees from Brazil, in the same style as buildings in Lagos's Brazilian Quarter. Dress modestly and avoid prayer times.", 2m, "Architecture", GreatMosqueName),
                        new ItemSpec("Afternoon", "Musée Honmè (King Toffa's Palace)", "A guided tour of the royal courtyards of the Porto-Novo kings.", 4m, "Royal History", HonmeName),
                        new ItemSpec("Evening", "Evening in Porto-Novo", "Dinner near your hotel in the quieter capital.", 10m, "Local Life"),
                    }),
                    new DaySpec(5, "Songhai Centre & Home to Lagos", PortoNovo, PortoNovo, PortoNovo, new[]
                    {
                        new ItemSpec("Morning", "8:00 AM · Songhai Centre Tour", "An hour's guided tour of the sustainable farm founded by a Nigerian-born priest, then the farm shop for juice, honey and yoghurt.", 4m, "Farm Tour", SonghaiName),
                        new ItemSpec("Afternoon", "Lunch at Songhai", "Eat at the on-site maquis or restaurant before the drive home.", 6m, "Local Food", SonghaiName),
                        new ItemSpec("Afternoon", "Drive Back to Lagos via Seme-Kraké", "Porto-Novo is the closest stop to the border, but allow 3-5 hours or more to reach central Lagos. Spend or change leftover CFA before you cross.", 60m, "Departure"),
                        new ItemSpec("Evening", "Back in Lagos", "Arrival time depends on the border and Lagos traffic.", 0m, "Journey's End"),
                    }),
                }),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Every blob filename this seeder references, for upload to blob storage.</summary>
        public static IReadOnlyList<string> ExpectedImageFiles =>
            Countries.Select(c => $"{c.Slug}_hero.jpg")
                .Concat(Wishlists.Select(w => $"{w.Region}_hero.jpg"))
                .Concat(Destinations.Select(DestinationImageFile))
                .Distinct()
                .ToList();

        public static async Task SeedAsync(HodracDbContext db, CancellationToken ct = default)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                var ownsTransaction = db.Database.CurrentTransaction == null;
                await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

                await SeedCoreAsync(db, ct);

                if (tx != null) await tx.CommitAsync(ct);
            });
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Implementation
        // ═══════════════════════════════════════════════════════════════════════

        private static async Task SeedCoreAsync(HodracDbContext db, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;

            // ─── Batch 1: languages, currencies, categories, tags, countries ─────
            var langNames = Languages.Select(l => l.Name).ToList();
            var languages = await db.Languages.Where(l => langNames.Contains(l.LanguageName)).ToListAsync(ct);
            var langByName = languages.GroupBy(l => l.LanguageName).ToDictionary(g => g.Key, g => g.First());
            foreach (var spec in Languages.Where(s => !langByName.ContainsKey(s.Name)))
            {
                var lang = new Language
                {
                    LanguageId = Guid.NewGuid(),
                    LanguageName = spec.Name,
                    LanguageCode = spec.Code,
                    HelpfulSurvivalPhrasesJson = JsonSerializer.Serialize(
                        spec.Phrases.Select(p => new { phrase = p.Phrase, local = p.Local }), JsonOpts),
                    RequiresCertifiedLocalGuide = false
                };
                db.Languages.Add(lang);
                langByName[spec.Name] = lang;
            }

            var currencyCodes = Currencies.Select(c => c.Code).ToList();
            var currencies = await db.Currencies.Where(c => currencyCodes.Contains(c.CurrencyCode)).ToListAsync(ct);
            var currencyByCode = currencies.GroupBy(c => c.CurrencyCode).ToDictionary(g => g.Key, g => g.First());
            foreach (var spec in Currencies.Where(s => !currencyByCode.ContainsKey(s.Code)))
            {
                var cur = new Currency
                {
                    CurrencyId = Guid.NewGuid(),
                    CurrencyCode = spec.Code,
                    CurrencyName = spec.Name,
                    CurrencySymbol = spec.Symbol,
                    ExchangeRateToBase = spec.RateToUsd,
                    LastExchangeRateUpdate = now
                };
                db.Currencies.Add(cur);
                currencyByCode[spec.Code] = cur;
            }

            var categoryKeys = Destinations.SelectMany(d => d.Categories).Distinct().ToList();
            var categoryByKey = (await db.Categories.Where(c => categoryKeys.Contains(c.Key)).ToListAsync(ct))
                .GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First());
            foreach (var key in categoryKeys.Where(k => !categoryByKey.ContainsKey(k)))
            {
                var cat = new Category
                {
                    CategoryId = Guid.NewGuid(),
                    Key = key,
                    CategoryName = key.Replace('_', ' ').ToTitleCase(),
                    CategoryDescription = "Auto-added category",
                    IconName = "tag",
                    ColorHex = "#888888"
                };
                db.Categories.Add(cat);
                categoryByKey[key] = cat;
            }

            var tagKeys = Destinations.SelectMany(d => d.Tags).Distinct().ToList();
            var tagByKey = (await db.Tags.Where(t => tagKeys.Contains(t.Key)).ToListAsync(ct))
                .GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.First());
            foreach (var key in tagKeys.Where(k => !tagByKey.ContainsKey(k)))
            {
                var tag = new Tag
                {
                    TagId = Guid.NewGuid(),
                    Key = key,
                    TagName = key.Replace('_', ' ').ToTitleCase(),
                    TargetPersonaType = "Explorer"
                };
                db.Tags.Add(tag);
                tagByKey[key] = tag;
            }

            var countryByName = new Dictionary<string, Country>();
            foreach (var spec in Countries)
            {
                var country = await db.Countries.FirstOrDefaultAsync(c => c.CountryName == spec.Name, ct);
                if (country == null)
                {
                    country = new Country
                    {
                        CountryId = Guid.NewGuid(),
                        CountryName = spec.Name,
                        Continent = spec.Continent,
                        CountryFlagEmoji = spec.Flag,
                        GlobalHeroImage = BlobBase + $"{spec.Slug}_hero.jpg",
                        VisaRequirementsSummary = spec.Visa,
                        PowerPlugType = spec.Plugs,
                        DrivingSide = spec.DrivingSide,
                        EstimatedDailyTaxRate = spec.TaxRate
                    };
                    db.Countries.Add(country);
                }
                countryByName[spec.Name] = country;
            }
            await db.SaveChangesAsync(ct);

            // CountryLanguage links (add only if missing)
            var countryIds = countryByName.Values.Select(c => c.CountryId).ToList();
            var existingCountryLangs = (await db.CountryLanguages
                    .Where(cl => countryIds.Contains(cl.CountryId))
                    .Select(cl => new { cl.CountryId, cl.LanguageId })
                    .ToListAsync(ct))
                .Select(x => (x.CountryId, x.LanguageId)).ToHashSet();
            foreach (var spec in Countries)
            {
                var countryId = countryByName[spec.Name].CountryId;
                foreach (var langName in spec.LanguageNames)
                {
                    var key = (countryId, langByName[langName].LanguageId);
                    if (existingCountryLangs.Add(key))
                        db.CountryLanguages.Add(new CountryLanguage { CountryId = key.countryId, LanguageId = key.LanguageId });
                }
            }

            // ─── Batch 2: cities ────────────────────────────────────────────────
            var cityByKey = new Dictionary<(string Country, string City), City>();
            foreach (var spec in Cities)
            {
                var countryId = countryByName[spec.CountryName].CountryId;
                var city = await db.Cities.FirstOrDefaultAsync(c => c.CityName == spec.Name && c.CountryId == countryId, ct);
                if (city == null)
                {
                    city = new City
                    {
                        CityId = Guid.NewGuid(),
                        CityName = spec.Name,
                        CountryId = countryId,
                        Latitude = spec.Lat,
                        Longitude = spec.Lng,
                        CityDescription = spec.Description
                    };
                    db.Cities.Add(city);
                }
                cityByKey[(spec.CountryName, spec.Name)] = city;
            }
            await db.SaveChangesAsync(ct);

            // ─── Batch 3: destinations ──────────────────────────────────────────
            var destByName = new Dictionary<string, Destination>();
            foreach (var spec in Destinations)
            {
                var countryId = countryByName[spec.CountryName].CountryId;
                var dest = await db.Destinations
                    .FirstOrDefaultAsync(d => d.DestinationName == spec.Name && d.CountryId == countryId, ct);
                if (dest == null)
                {
                    var search = DestinationSearchFields.Derive(spec.Name);
                    var country = Countries.First(c => c.Name == spec.CountryName);
                    dest = new Destination
                    {
                        DestinationId = Guid.NewGuid(),
                        DestinationName = spec.Name,
                        CleanNormalizedSearchName = search.CleanNormalizedSearchName,
                        MetaphoneCode = search.MetaphoneCode,
                        DoubleMetaphonePrimary = search.DoubleMetaphonePrimary,
                        DoubleMetaphoneSecondary = search.DoubleMetaphoneSecondary,
                        CommonAlternateSpellingsJson = JsonSerializer.Serialize(spec.Alternates, JsonOpts),
                        DescriptionJson = JsonSerializer.Serialize(spec.Desc, JsonOpts),
                        AverageCostPerDay = spec.AvgCost,
                        LuxuryRating = DeriveLuxury(spec.AvgCost, spec.Tags),
                        AccessibilityType = DestinationReferenceData.NormalizeAccessibility(spec.AccessibilityType) ?? spec.AccessibilityType,
                        FamilyFriendlyScore = FamilyScore(spec.Tags),
                        AdventurePaceScore = AdventureScore(spec.Tags),
                        AestheticTrendScore = AestheticTrendScore(spec.Tags),
                        PsychographicVibeTagsJson = JsonSerializer.Serialize(spec.Tags, JsonOpts),
                        Latitude = spec.Lat,
                        Longitude = spec.Lng,
                        SearchHitCount = 0,
                        TimeZone = DestinationReferenceData.NormalizeTimeZone(country.TimeZone) ?? country.TimeZone,
                        SafetyLevel = spec.Safety,
                        CountryId = countryId
                    };
                    db.Destinations.Add(dest);
                }
                destByName[spec.Name] = dest;
            }
            await db.SaveChangesAsync(ct);

            // ─── Batch 4: destination junctions + images (add only if missing) ──
            var destIds = destByName.Values.Select(d => d.DestinationId).ToList();

            var existingCities = (await db.DestinationCities.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.CityId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.CityId)).ToHashSet();
            var existingLangs = (await db.DestinationLanguages.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.LanguageId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.LanguageId)).ToHashSet();
            var existingCurrencies = (await db.DestinationCurrencies.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.CurrencyId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.CurrencyId)).ToHashSet();
            var existingCats = (await db.DestinationCategories.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.CategoryId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.CategoryId)).ToHashSet();
            var existingTags = (await db.DestinationTags.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.TagId }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.TagId)).ToHashSet();
            var existingImages = (await db.DestinationImages.Where(x => destIds.Contains(x.DestinationId))
                .Select(x => new { x.DestinationId, x.ImageUrl }).ToListAsync(ct))
                .Select(x => (x.DestinationId, x.ImageUrl)).ToHashSet();

            foreach (var spec in Destinations)
            {
                var id = destByName[spec.Name].DestinationId;

                var cityId = cityByKey[(spec.CountryName, spec.CityName)].CityId;
                if (existingCities.Add((id, cityId)))
                    db.DestinationCities.Add(new DestinationCity { DestinationId = id, CityId = cityId });

                foreach (var langName in spec.LanguageNames)
                {
                    var langId = langByName[langName].LanguageId;
                    if (existingLangs.Add((id, langId)))
                        db.DestinationLanguages.Add(new DestinationLanguage { DestinationId = id, LanguageId = langId });
                }

                var currencyCode = Countries.First(c => c.Name == spec.CountryName).CurrencyCode;
                var currencyId = currencyByCode[currencyCode].CurrencyId;
                if (existingCurrencies.Add((id, currencyId)))
                    db.DestinationCurrencies.Add(new DestinationCurrency { DestinationId = id, CurrencyId = currencyId });

                foreach (var catKey in spec.Categories)
                {
                    var catId = categoryByKey[catKey].CategoryId;
                    if (existingCats.Add((id, catId)))
                        db.DestinationCategories.Add(new DestinationCategory { DestinationId = id, CategoryId = catId });
                }

                foreach (var tagKey in spec.Tags)
                {
                    var tagId = tagByKey[tagKey].TagId;
                    if (existingTags.Add((id, tagId)))
                        db.DestinationTags.Add(new DestinationTag { DestinationId = id, TagId = tagId });
                }

                var imageUrl = DestinationImageUrl(spec);
                if (existingImages.Add((id, imageUrl)))
                {
                    db.DestinationImages.Add(new DestinationImage
                    {
                        DestinationImageId = Guid.NewGuid(),
                        DestinationId = id,
                        ImageUrl = imageUrl,
                        ThumbnailUrl = imageUrl,
                        Caption = $"Hero image of {spec.Name}",
                        DisplayOrder = 1,
                        ImageType = "Hero",
                        ShotContext = spec.ShotContext,
                        IsAiGenerated = false
                    });
                }
            }
            await db.SaveChangesAsync(ct);

            // ─── Batch 5: wishlist, days, items, destination links ──────────────
            var specByName = Destinations.ToDictionary(d => d.Name);
            foreach (var spec in Wishlists)
            {
                var wishlist = await db.Wishlists
                    .FirstOrDefaultAsync(w => w.IsTemplate && w.WishlistName == spec.Name, ct);

                if (wishlist == null)
                {
                    wishlist = new Wishlist
                    {
                        WishlistId = Guid.NewGuid(),
                        WishlistName = spec.Name,
                        WishlistDescription = spec.Description,
                        ShortStory = spec.ShortStory,
                        TotalDays = spec.Days.Length,
                        PeopleType = spec.PeopleType,
                        WishlistHeroImage = BlobBase + $"{spec.Region}_hero.jpg",
                        GlobalInclusionsJson = JsonSerializer.Serialize(spec.Inclusions, JsonOpts),
                        RawContentKeywords = spec.Keywords,
                        PsychologicalVibeTagsJson = JsonSerializer.Serialize(spec.Vibes, JsonOpts),
                        DefaultTravelersCount = 2,
                        BasePricePerPerson = spec.BasePricePerPerson,
                        CalculatedTotalCost = spec.BasePricePerPerson * 2,
                        DepositAmountRequired = spec.Deposit,
                        AccommodationInclusions = spec.Accommodation,
                        TransitInclusions = spec.Transit,
                        ActivityInclusions = spec.Activities,
                        IsTemplate = true,
                        IsFeatured = spec.IsFeatured,
                        PrimaryPersonaTarget = spec.Persona,
                        CreatedAt = now,
                        OwnerUserId = null,
                        ForkedFromId = null
                    };
                    db.Wishlists.Add(wishlist);

                    foreach (var daySpec in spec.Days)
                    {
                        var day = new ItineraryDay
                        {
                            ItineraryDayId = Guid.NewGuid(),
                            DayNumber = daySpec.Number,
                            DayTitle = daySpec.Title,
                            MorningCityId = cityByKey[(spec.CountryName, daySpec.MorningCity)].CityId,
                            AfternoonCityId = cityByKey[(spec.CountryName, daySpec.AfternoonCity)].CityId,
                            EveningCityId = cityByKey[(spec.CountryName, daySpec.EveningCity)].CityId,
                            TransitFromPreviousDayRouteId = null,
                            WishlistId = wishlist.WishlistId
                        };
                        db.ItineraryDays.Add(day);

                        var order = 1;
                        foreach (var item in daySpec.Items)
                        {
                            db.ItineraryItems.Add(new ItineraryItem
                            {
                                ItineraryItemId = Guid.NewGuid(),
                                ItineraryDayId = day.ItineraryDayId,
                                ItemTitle = item.Title,
                                ItemDescription = item.Description,
                                ItemOrderIndex = order++,
                                TimeOfDay = item.Time,
                                ImageUrl = item.DestinationName != null
                                    ? DestinationImageUrl(specByName[item.DestinationName])
                                    : "",
                                SocialProofBadge = item.Badge,
                                IndividualCostModifier = item.Cost,
                                IsOptionalActivity = item.Optional,
                                IsSelectedByDefault = !item.Optional
                            });
                        }
                    }
                }

                // Wishlist ↔ Destination links (add only if missing) — all destinations in the region, also for pre-existing wishlists.
                var wishlistId = wishlist.WishlistId;
                var linked = (await db.WishlistDestinations
                        .Where(wd => wd.WishlistId == wishlistId)
                        .Select(wd => wd.DestinationId)
                        .ToListAsync(ct))
                    .ToHashSet();
                foreach (var destSpec in Destinations.Where(d => d.Region == spec.Region))
                {
                    var destId = destByName[destSpec.Name].DestinationId;
                    if (linked.Add(destId))
                        db.WishlistDestinations.Add(new WishlistDestination { WishlistId = wishlistId, DestinationId = destId });
                }

                await db.SaveChangesAsync(ct);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static string DestinationImageFile(DestSpec spec) =>
            $"{spec.Region}_{DestinationSearchFields.Normalize(spec.Name).Replace(' ', '-')}.jpg";

        private static string DestinationImageUrl(DestSpec spec) => BlobBase + DestinationImageFile(spec);

        // Same scoring rules as BaliSeeder / AfricaPocSeeder / the Seattle seeder.
        private static int DeriveLuxury(decimal avgCost, IEnumerable<string> tagKeys)
        {
            var hasPremium = tagKeys.Contains("premium");
            if (hasPremium || avgCost > 200) return 5;
            if (avgCost > 100) return 4;
            if (avgCost > 40) return 3;
            if (avgCost > 10) return 2;
            return 1;
        }

        private static int FamilyScore(IEnumerable<string> tagKeys)
        {
            var k = tagKeys.ToHashSet();
            int s = 1;
            if (k.Contains("family_friendly")) s += 2;
            if (k.Contains("walkable")) s += 1;
            if (k.Contains("educational")) s += 1;
            if (k.Contains("adventurous")) s -= 1;
            if (k.Contains("nightlife")) s -= 1;
            return Math.Clamp(s, 1, 5);
        }

        private static int AdventureScore(IEnumerable<string> tagKeys)
        {
            var k = tagKeys.ToHashSet();
            int s = 1;
            if (k.Contains("adventurous")) s += 2;
            if (k.Contains("nature")) s += 1;
            if (k.Contains("hidden_gem")) s += 1;
            if (k.Contains("relaxing")) s -= 1;
            if (k.Contains("tourist_hotspot")) s -= 1;
            return Math.Clamp(s, 1, 5);
        }

        private static int AestheticTrendScore(IEnumerable<string> tagKeys)
        {
            var k = tagKeys.ToHashSet();
            if (k.Contains("photography") || k.Contains("architecture")) return 4;
            return 2;
        }
    }
}
