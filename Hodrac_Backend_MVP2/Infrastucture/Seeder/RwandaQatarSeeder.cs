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
    /// Two template wishlists for Nigerian travellers that route Nigeria to Kigali to Doha and home:
    /// "Kigali &amp; Doha" (8 days: about 4 in Rwanda including Volcanoes National Park, then about 3.5 in Qatar)
    /// and "Doha with a Kigali Stopover" (6 days: a 2-day Kigali stopover, then Doha).
    /// Both wishlists span two countries and two regions ("rwanda" and "qatar"), so unlike the
    /// single-country seeders:
    ///   * a wishlist lists the regions it visits (WishlistSpec.Regions) and is linked to every
    ///     destination in all of them;
    ///   * itinerary-day cities are resolved by city name alone via CityKey, which looks up the
    ///     city's own country in Cities (city names are unique within this seeder).
    /// Fully idempotent: every row is looked up by its natural key first and only created when missing.
    /// Rwanda, Qatar, RWF, QAR, Kinyarwanda and Arabic are new; English and Swahili reuse
    /// KenyaNorthSeeder's specs exactly and French reuses AfricaPocSeeder's spec exactly.
    /// All prices are rough USD approximations; fees and schedules were checked against published
    /// 2025-2026 sources where available (listed below) and are labelled "confirm".
    ///
    /// Sources (checked Nov 2026):
    ///   Rwanda visa/AU fee waiver: migration.gov.rw (General Information), visitrwanda.com/facts/immigration-visas,
    ///     UK FCDO gov.uk/foreign-travel-advice/rwanda/entry-requirements (yellow fever)
    ///   RwandAir: flights.rwandair.com/en-ng/flights-from-nigeria (Lagos is the only Nigerian city listed),
    ///     aeroroutes.com/eng/260610-wbjun26me (6 weekly Kigali-Doha from 1 Jul 2026),
    ///     rwandair.com Middle East flight resumption statement (Doha resumed from 1 Jun 2026)
    ///   Qatar Airways Doha-Lagos/Abuja: qatarairways.com/en/destinations/flights-to-abuja.html, flightconnections.com
    ///   Qatar visa: hayya.qa (Tourist A1 e-visa), discoverqatar.qa/qatar-transit-visa, allafrica.com/stories/202509110032.html
    ///   Kigali Genocide Memorial: kgm.rw/visit/know-before-you-go
    ///   Gorilla permits: newtimes.co.rw (promotional rates extended for 2026), visitrwandabookings.rdb.rw
    ///   Akagera: africanparks.org Akagera NP Pricing 2025 (PDF); Nyungwe: visitnyungwe.org Pricing Scheme 2025-2026 (PDF)
    ///   Kandt House: rwandaheritage.gov.rw/en/visit-national-museums; MIA: mia.org.qa/en/visit, qatarliving.com
    ///   Desert safari prices: qatarliving.com evening desert safari guide; FX: wise.com / xe.com (Nov 2026)
    /// </summary>
    public static class RwandaQatarSeeder
    {
        private const string BlobBase = "https://wangq4yhmf94epv8.public.blob.vercel-storage.com/";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            // Stored in jsonb, never rendered as raw HTML. Keep accents/apostrophes readable.
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

        // Cities are plain city names; each one's country is resolved through CityKey, so a single
        // day can start in Kigali (Rwanda) and end in Doha (Qatar).
        private sealed record DaySpec(int Number, string Title, string MorningCity, string AfternoonCity, string EveningCity, ItemSpec[] Items);

        // Multi-country variant of the usual spec: HeroSlug names the wishlist hero image
        // ({HeroSlug}_hero.jpg) and Regions lists every destination region the wishlist visits.
        private sealed record WishlistSpec(
            string HeroSlug, string[] Regions, string Name, string Description, string ShortStory,
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

        private const string Rwanda = "Rwanda";
        private const string Qatar = "Qatar";

        private const string RegionRwanda = "rwanda";
        private const string RegionQatar = "qatar";

        private const string English = "English";       // existing natural key (AfricaPocSeeder / KenyaNorthSeeder)
        private const string Swahili = "Swahili";       // same spec as KenyaNorthSeeder
        private const string French = "French";         // same spec as AfricaPocSeeder
        private const string Kinyarwanda = "Kinyarwanda";
        private const string Arabic = "Arabic";

        // Rwanda cities
        private const string Kigali = "Kigali";
        private const string Musanze = "Musanze";
        private const string Rubavu = "Rubavu";
        private const string Nyamagabe = "Nyamagabe";
        private const string Kayonza = "Kayonza";

        // Qatar cities
        private const string Doha = "Doha";
        private const string Mesaieed = "Mesaieed";

        // Names checked against every other seeder (grep for Kigali/Rwanda/Doha/Qatar); none collide.
        private const string GenocideMemorialName = "Kigali Genocide Memorial";
        private const string KandtHouseName = "Kandt House Museum";
        private const string InemaName = "Inema Arts Center";
        private const string KimironkoName = "Kimironko Market";
        private const string NyamiramboName = "Nyamirambo & Nyamirambo Women's Center";
        private const string ConventionCentreName = "Kigali Convention Centre";
        private const string VolcanoesName = "Volcanoes National Park";
        private const string LakeKivuName = "Lake Kivu at Rubavu (Gisenyi)";
        private const string NyungweName = "Nyungwe National Park";
        private const string AkageraName = "Akagera National Park";

        private const string IslamicArtName = "Museum of Islamic Art";
        private const string NationalMuseumName = "National Museum of Qatar";
        private const string SouqWaqifName = "Souq Waqif";
        private const string KataraName = "Katara Cultural Village";
        private const string PearlName = "The Pearl-Qatar";
        private const string CornicheName = "Doha Corniche";
        private const string MsheirebName = "Msheireb Downtown Doha";
        private const string InlandSeaName = "Khor Al Adaid (Inland Sea)";

        private static readonly LanguageSpec[] Languages =
        {
            // English and Swahili: same specs as KenyaNorthSeeder; French: same spec as AfricaPocSeeder. Only created if missing.
            new(English, "en", Array.Empty<(string, string)>()),
            new(Swahili, "sw", new[] { ("Hello", "Jambo / Habari"), ("Thank you", "Asante"), ("How much?", "Bei gani?"), ("Slowly", "Pole pole") }),
            new(French, "fr", new[] { ("Hello", "Bonjour"), ("Thank you", "Merci") }),
            // New.
            new(Kinyarwanda, "rw", new[] { ("Hello", "Muraho"), ("Good morning", "Mwaramutse"), ("How are you?", "Amakuru?"), ("Thank you", "Murakoze") }),
            new(Arabic, "ar", new[] { ("Hello", "As-salamu alaykum / Marhaba"), ("Thank you", "Shukran"), ("How much?", "Bikam?"), ("Please", "Min fadlak") }),
        };

        // RWF: about 1,465-1,495 per USD in early-mid November 2026 (wise.com / currency-converter quotes); 1,470 used.
        // QAR: pegged at 3.64 per USD.
        private static readonly CurrencySpec[] Currencies =
        {
            new("RWF", "Rwandan Franc", "FRw", 1470m),
            new("QAR", "Qatari Riyal", "QR", 3.64m),
        };

        private static readonly CountrySpec[] Countries =
        {
            new(Rwanda, "rwanda", "Africa", "🇷🇼",
                "Citizens of African Union, Commonwealth and Francophonie member states, Nigerians included, get a visa on arrival with the fee waived for a visit of up to 30 days, according to Rwanda's Directorate General of Immigration and Emigration (migration.gov.rw). Most other nationalities can get a visa on arrival or apply online via irembo.gov.rw. You need a yellow fever vaccination certificate if you arrive from a country with yellow fever risk, and Nigeria is one, so carry the card. Your passport should be valid for at least 6 months. Plastic bags are banned in Rwanda and may be taken from you on arrival. Requirements change, so check official sources before you travel.",
                "Type C/J", "Right", 0.18m, new[] { Kinyarwanda, English, French, Swahili }, "RWF", "Africa/Kigali"),
            new(Qatar, "qatar", "Asia", "🇶🇦",
                "Many nationalities enter Qatar visa-free, but Nigerian passport holders must get a visa before travel. Tourist (A1) e-visas are applied for on the official Hayya portal (hayya.qa). Since 2025, Nigerian media and travel agents have reported stricter checks on Nigerian applicants: a confirmed hotel booking (often made through Discover Qatar), a return ticket and proof of funds. Reports of extra restrictions on solo male travellers were disputed, so check the current rules. Qatar Airways passengers stopping over in Doha may be eligible for a Discover Qatar transit visa of up to 96 hours instead; check eligibility for your passport. Your passport should be valid for at least 6 months. Requirements change, so check hayya.qa, visitqatar.com or the Embassy of Qatar in Abuja before you book.",
                "Type D/G", "Right", 0m, new[] { Arabic, English }, "QAR", "Asia/Qatar"),
        };

        private static readonly CitySpec[] Cities =
        {
            new(Kigali, Rwanda, -1.9441, 30.0619,
                "Rwanda's capital, built over hills in the centre of the country, with a growing food, art and coffee scene. It has the Kigali Genocide Memorial, Kimironko Market, the Nyamirambo quarter and the domed Kigali Convention Centre, and it is RwandAir's hub, with flights to Lagos and the Gulf."),
            new(Musanze, Rwanda, -1.4998, 29.6345,
                "A town in north-west Rwanda, formerly called Ruhengeri, at the foot of the Virunga volcanoes and roughly 2-2.5 hours' drive from Kigali. It is the base for Volcanoes National Park: gorilla and golden monkey treks start from the park headquarters at Kinigi, about 30 minutes away."),
            new(Rubavu, Rwanda, -1.6792, 29.2597,
                "A town on the northern shore of Lake Kivu, still widely called Gisenyi, on the border with Goma in the DR Congo. It has sandy lake beaches, lakeside hotels and boat trips, and is about 1.5 hours from Musanze or 3-3.5 hours from Kigali."),
            new(Nyamagabe, Rwanda, -2.4794, 29.5658,
                "A district town in southern Rwanda, formerly Gikongoro, on the road to Nyungwe National Park. The park's Uwinka visitor centre and canopy walkway are in the forest to the west, a long day's drive from Kigali."),
            new(Kayonza, Rwanda, -1.8939, 30.5042,
                "A small crossroads town in eastern Rwanda on the road from Kigali towards Tanzania and Uganda, and the turn-off for the southern gate of Akagera National Park."),
            new(Doha, Qatar, 25.2854, 51.5310,
                "Qatar's capital on the Gulf coast, built around a seven-kilometre waterfront Corniche. It has the Museum of Islamic Art, the National Museum of Qatar, Souq Waqif and the island district of The Pearl. Hamad International Airport is Qatar Airways' hub, and the Doha Metro runs from the airport to the centre."),
            new(Mesaieed, Qatar, 24.9910, 51.5500,
                "An industrial port town about 45 km south of Doha. Desert safaris let their tyres down near Sealine, just south of town, before crossing the dunes to Khor Al Adaid, the Inland Sea."),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Destinations
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] RwandaLangs = { Kinyarwanda, English, French, Swahili };
        private static readonly string[] QatarLangs = { Arabic, English };

        private const string Umuganda =
            "Umuganda: On the last Saturday of every month Rwanda holds Umuganda, a national community-work morning (roughly 8:00-11:00 AM). Traffic stops and most shops and many attractions close or open late, so don't plan drives or visits for that morning.";

        private const string RwandaMoney =
            "Paying: Cards work in Kigali hotels and many restaurants, and MTN MoMo mobile money is accepted almost everywhere. Carry some Rwandan francs for markets and motos.";

        private const string QatarDress =
            "Dress Code: Qatar asks visitors to dress modestly in public places. Cover shoulders and knees in museums, souqs, malls and government buildings; women only need to cover their hair in mosques.";

        private const string QatarHeat =
            "Heat: From roughly June to September temperatures regularly pass 40°C, and the best months to visit are November to March. Carry water and plan outdoor time for early morning and evening.";

        private static readonly DestSpec[] Destinations =
        {
            // ─── Kigali ────────────────────────────────────────────────────────
            new(RegionRwanda, GenocideMemorialName, Rwanda, Kigali, RwandaLangs, -1.9306, 30.0606,
                15m, "Car", 1,
                new[] { "historical_tour", "museum_gallery", "landmark_monument" },
                new[] { "history", "educational", "cultural", "tourist_hotspot" },
                new[] { "Gisozi Memorial", "Kigali Memorial Centre", "Kigali Genocide Memorial Centre", "Gisozi Genocide Memorial", "KGM" }, "Exterior",
                Desc(
                    "The Kigali Genocide Memorial at Gisozi is the burial place of more than 250,000 victims of the 1994 Genocide against the Tutsi. Its exhibitions explain what led to the genocide, what happened and how the country rebuilt afterwards, and there is a children's memorial and gardens above the mass graves.\n\nWhat to Do: Allow at least two hours with the audio guide, walk the gardens and lay a flower at the graves. Visit early in the trip, since it explains much of what you will see in Rwanda.",
                    "Every Rwandan family was affected by 1994, and people come here to remember relatives. Visitors are welcome and asked to stay quiet and respectful. Kwibuka, the national commemoration period, begins on 7 April each year.",
                    "Best Access: About 10-15 minutes by taxi or moto from central Kigali to Gisozi, north of the city centre.\n\nBooking: Walk-ins are generally accepted. Booking a time slot on the memorial's website (kgm.rw) helps on busy mid-mornings.",
                    "Entry: Free, with donations welcome. The audio guide costs extra; about RWF 20,000 (US$15) per device is quoted for 2026, so confirm at the desk.\n\nHours: Usually daily 8:00 AM-5:00 PM with last entry around 4:00 PM (some sources give 9:00 AM). On Umuganda Saturdays it opens around 1:00 PM, and it is reported closed on 25 December and 1 January. It can close at short notice for official visits, so check kgm.rw.\n\nPhotography: Not allowed inside the exhibitions.\n\n" + Umuganda,
                    "Graphic Content: The exhibitions include graphic material and personal stories of children. Prepare younger travellers and plan something quiet for afterwards.\n\nDress & Behaviour: Dress respectfully and keep voices low.",
                    "Entry: Free.\nAudio Guide: approx. RWF 20,000 (US$15).\nTaxi from Central Kigali: approx. RWF 5,000-8,000 (US$4-$6) each way.",
                    new[]
                    {
                        "Kandt House Museum: Kigali's colonial-era history, in the city centre.",
                        "Inema Arts Center: Contemporary Rwandan art in Kacyiru.",
                        "Kimironko Market: Kigali's biggest market."
                    },
                    "Any day. Mornings are quieter; avoid Umuganda Saturday mornings, and late March to April if you would rather not visit during the commemoration period.",
                    "Medium (5/10), busier mid-morning.",
                    "Rating: 7/10. The exhibitions are indoors on level floors; the gardens have slopes and steps.",
                    "2 to 3 hours.")),

            new(RegionRwanda, KandtHouseName, Rwanda, Kigali, RwandaLangs, -1.9459, 30.0617,
                5m, "Car", 1,
                new[] { "museum_gallery", "historical_tour" },
                new[] { "history", "educational", "family_friendly", "nature" },
                new[] { "Kandt House", "Natural History Museum Kigali", "Kandt House Museum of Natural History", "Richard Kandt House" }, "Exterior",
                Desc(
                    "Kandt House was the home of Richard Kandt, the German explorer and colonial resident who founded Kigali in 1907. It is now one of Rwanda's national museums, covering natural history and the colonial period, with a small reptile section of live snakes and crocodiles and a view over the city.\n\nWhat to Do: Visit after the Genocide Memorial to see how Kigali grew from a colonial post into the capital.",
                    "Kigali schools bring classes here, and children usually head straight for the reptiles. The garden has a good view over the old centre.",
                    "Best Access: In Nyarugenge near the city centre, a short taxi or moto ride from most hotels.",
                    "Entry: A fee is charged. Around RWF 6,000 for non-residents has been quoted, with a separate charge for the reptile section; confirm on rwandaheritage.gov.rw.\n\nHours: Reported as daily from around 8:00 AM to 5:00-6:00 PM, opening after Umuganda on the last Saturday. Sources differ, so check ahead.\n\n" + RwandaMoney,
                    "Small Museum: It is a modest, older museum and an hour is usually enough.\n\nReptiles: Keep children with you around the snake and crocodile enclosures.",
                    "Entry: approx. RWF 6,000 (US$4), confirm.\nTaxi in Kigali: approx. RWF 3,000-6,000 (US$2-$4).",
                    new[]
                    {
                        "Kigali Genocide Memorial: A short drive north.",
                        "Nyamirambo & Nyamirambo Women's Center: The busy quarter to the south-west.",
                        "Kigali Convention Centre: The newer business district."
                    },
                    "Weekday mornings.",
                    "Low (3/10).",
                    "Rating: 5/10. An old house with some steps; the garden is on a slope.",
                    "1 hour.")),

            new(RegionRwanda, InemaName, Rwanda, Kigali, RwandaLangs, -1.9437, 30.0915,
                10m, "Car", 1,
                new[] { "museum_gallery", "activity_experience" },
                new[] { "art", "cultural", "local_favorite", "photography" },
                new[] { "Inema Art Center", "Inema Arts Centre", "Inema Gallery", "Inema Kigali" }, "Interior",
                Desc(
                    "Inema Arts Center in Kacyiru is a privately run gallery and studio collective that opened in 2012. Resident Rwandan artists paint, sculpt and exhibit there, mostly large, brightly coloured contemporary work, and the centre also runs workshops, music nights and events.\n\nWhat to Do: Browse the galleries and talk to the artists in their studios. If you buy a large piece, ask about shipping or packing it for your flight.",
                    "Inema belongs to a young Kigali art scene, and the artists are usually happy to talk about their work and about Rwanda today.",
                    "Best Access: In Kacyiru, a short taxi or moto ride from the city centre and close to the Kigali Convention Centre.",
                    "Entry: Generally free to browse. Some sources quote about US$10 for workshops; confirm hours and events on the centre's social media before you go.\n\nBuying Art: Prices are in US$ or RWF, and cards and MoMo are usually accepted.",
                    "Hours: Opening times aren't consistently published, so message ahead.\n\nShipping: Ask the gallery to roll and pack large canvases for the plane.",
                    "Entry: Free (confirm).\nWorkshop: approx. US$10-$30.\nArtwork: from approx. US$50 for small pieces.",
                    new[]
                    {
                        "Kigali Convention Centre: Nearby in Kimihurura.",
                        "Kimironko Market: Fabrics and crafts to the east.",
                        "Kigali Genocide Memorial: A short drive."
                    },
                    "Weekday afternoons, or evenings when events are on.",
                    "Low (3/10).",
                    "Rating: 6/10. Gallery rooms with some steps between levels.",
                    "1 to 1.5 hours.")),

            new(RegionRwanda, KimironkoName, Rwanda, Kigali, RwandaLangs, -1.9497, 30.1255,
                10m, "Car", 2,
                new[] { "market_street_life", "food_experience" },
                new[] { "local_favorite", "cultural", "budget_friendly", "food_focused", "photography" },
                new[] { "Kimironko", "Kimironko Market Kigali", "Isoko rya Kimironko" }, "Interior",
                Desc(
                    "Kimironko is Kigali's biggest market: covered halls of fruit, vegetables, spices, baskets and household goods, with tailors and bolts of African-print fabric upstairs and around the edges. Many visitors choose a fabric and have a tailor here make a shirt or dress within a day or two.\n\nWhat to Do: Walk the produce halls, buy agaseke baskets and fabric, and order clothes early in the trip so they're ready before you fly on.",
                    "Most of Kigali shops here. Nigerian visitors will find it calmer than a big market at home, with gentler bargaining and fewer touts.",
                    "Best Access: In Kimironko, north-east Kigali, about 15-20 minutes by taxi or moto from the centre.",
                    "Bargaining: Bargain politely for crafts and fabric; food prices are mostly fixed.\n\nTailoring: Agree the price, design and collection date up front. Tailors often finish quickly, but don't count on it.\n\n" + RwandaMoney + "\n\n" + Umuganda,
                    "Pickpockets: Keep your phone and cash secure in the crowded aisles.\n\nPlastic Bags: Banned in Rwanda, so bring a cloth bag for your shopping.",
                    "Fabric: approx. RWF 10,000-25,000 (US$7-$17) per piece.\nTailored Shirt/Dress: approx. RWF 15,000-40,000 (US$10-$27) plus fabric.\nBaskets & Crafts: approx. US$5-$30.",
                    new[]
                    {
                        "Inema Arts Center: Contemporary art in Kacyiru.",
                        "Kigali Convention Centre: On the way back to the centre.",
                        "Nyamirambo & Nyamirambo Women's Center: Street life and food on the other side of the city."
                    },
                    "Mornings Monday to Saturday, except Umuganda morning.",
                    "High (7/10).",
                    "Rating: 5/10. Crowded aisles and uneven floors.",
                    "1.5 to 2 hours.")),

            new(RegionRwanda, NyamiramboName, Rwanda, Kigali, RwandaLangs, -1.9790, 30.0440,
                25m, "Walk", 2,
                new[] { "market_street_life", "activity_experience", "food_experience" },
                new[] { "cultural", "local_favorite", "walkable", "food_focused", "social", "nightlife" },
                new[] { "Nyamirambo", "Nyamirambo Women's Centre", "NWC", "Nyamirambo Walking Tour", "Biryogo" }, "Exterior",
                Desc(
                    "Nyamirambo is Kigali's historic Muslim quarter and its busiest neighbourhood after dark, with mosques, barbers, tailors, music from the shopfronts and much of the city's street food and nightlife. The Nyamirambo Women's Center, a women-run cooperative, leads walking tours of the area and sells crafts made by its members.\n\nWhat to Do: Take the Women's Center walking tour by day, then come back in the evening for brochettes and chapati on the main street.",
                    "People in Kigali say Nyamirambo stays up later than the rest of the city. Its street life is closer to what Nigerian visitors know from home than the quiet city centre.",
                    "Best Access: South-west of the city centre, about 10-15 minutes by taxi or moto, then on foot.\n\nWalking Tour: Book ahead with the Nyamirambo Women's Center.",
                    "Walking Tour: Around US$25 per person for roughly three hours has been quoted; confirm with the Women's Center, which also runs cooking and weaving sessions.\n\nRespect: Ask before photographing people, especially around mosques, and dress modestly in the Muslim quarter.\n\n" + RwandaMoney,
                    "Evenings: The streets get crowded, so keep valuables secure and get home by registered taxi or by moto with a helmet.\n\nHills: Kigali is steep, so wear comfortable shoes.",
                    "Women's Center Walking Tour: approx. US$25.\nStreet Food Dinner: approx. RWF 5,000-12,000 (US$3-$8).\nDrinks: approx. RWF 1,500-4,000 (US$1-$3).",
                    new[]
                    {
                        "Kandt House Museum: Back towards the centre.",
                        "Kimironko Market: A bigger market in the north-east.",
                        "Kigali Genocide Memorial: The city's recent history."
                    },
                    "Daytime for the walking tour; Friday and Saturday evenings for the street food.",
                    "Medium (6/10), busier at night.",
                    "Rating: 4/10. Steep, uneven streets.",
                    "3 hours for the tour, plus an evening.")),

            new(RegionRwanda, ConventionCentreName, Rwanda, Kigali, RwandaLangs, -1.9536, 30.0928,
                30m, "Car", 1,
                new[] { "landmark_monument", "entertainment_nightlife", "viewpoint_scenic_spot" },
                new[] { "architecture", "photography", "nightlife", "social" },
                new[] { "KCC", "Kigali Convention Center", "Kigali Convention Centre Dome", "Kigali Heights", "Radisson Blu Kigali" }, "Exterior",
                Desc(
                    "The Kigali Convention Centre in Kimihurura has a domed tower lit in changing colours at night and appears on most postcards of the city. The complex hosts conferences and large events next to the Radisson Blu hotel, and the Kigali Heights mall across the road has restaurants, cafés and rooftop bars.\n\nWhat to Do: See the dome lit up after dark, then have dinner or drinks at Kigali Heights.",
                    "Kigali Heights and the area around the KCC are where many office workers go for after-work drinks and weekend dinners.",
                    "Best Access: In Kimihurura, about 10 minutes by taxi from the city centre and 15-20 minutes from Kigali International Airport.",
                    "Entry: The building is only open to the public for events. The exterior, the lights and Kigali Heights are free.\n\nEvents: Concerts and exhibitions are sometimes open to the public, so check what's on.\n\n" + RwandaMoney,
                    "Security Checks: Expect bag checks at the convention centre and large hotels, which is normal in Kigali.\n\nTraffic: Roads around the KCC may close during major summits.",
                    "Exterior & Lights: Free.\nDinner around Kigali Heights: approx. US$15-$35.\nCocktail: approx. US$6-$10.",
                    new[]
                    {
                        "Inema Arts Center: Nearby in Kacyiru.",
                        "Kigali Genocide Memorial: A short drive.",
                        "Kimironko Market: East of Kimihurura."
                    },
                    "After dark, when the dome is lit.",
                    "Medium (5/10).",
                    "Rating: 8/10. Modern, level and step-free around the complex.",
                    "1 to 2 hours for an evening.")),

            // ─── Musanze ───────────────────────────────────────────────────────
            new(RegionRwanda, VolcanoesName, Rwanda, Musanze, RwandaLangs, -1.4316, 29.5869,
                400m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "adventurous", "premium", "photography", "tourist_hotspot" },
                new[] { "Volcanoes NP", "Parc National des Volcans", "PNV", "Virunga Volcanoes", "Kinigi", "Gorilla Trekking Rwanda", "Golden Monkey Trekking" }, "Landscape",
                Desc(
                    "Volcanoes National Park covers the Rwandan side of the Virunga volcanoes and is home to habituated mountain gorilla families and the rare golden monkey. A gorilla trek climbs through bamboo and montane forest to spend one hour with a family. Golden monkey treks, the hike to Dian Fossey's grave and volcano climbs cost much less.\n\nWhat to Do: Book a gorilla permit if your budget allows, or a golden monkey trek, and be at the Kinigi park headquarters for the early-morning briefing.",
                    "Gorilla permit money pays for conservation and for projects in the villages around the park. Many porters and guides come from families that once poached in the forest, and hiring a porter is a direct way to pay them.",
                    "Best Access: Drive from Kigali to Musanze (about 2-2.5 hours on good tarmac), then about 30 minutes to the park headquarters at Kinigi. Stay the night before in Musanze or Kinigi.\n\nBriefing: Report to Kinigi around 7:00 AM; treks start after the briefing.",
                    "Gorilla Permit: US$1,500 per person for international visitors, covering one hour with a gorilla family and park entry; minimum age 15. Promotional rates for Rwandans and East African citizens run through 2026 and don't apply to Nigerian passport holders on a standard tourist visit. Book early on the RDB portal (visitrwandabookings.rdb.rw) or through an operator.\n\nGolden Monkeys: About US$100 for international visitors (minimum age 12); confirm on the RDB portal.\n\nFitness: Treks take from 1 to over 6 hours on steep, muddy ground at altitude.",
                    "Health: You can't trek with a cold or flu, because gorillas catch human illnesses. Masks may be required.\n\nGear: Waterproof jacket, long trousers tucked into socks, gloves for nettles and boots with grip.\n\nRefunds: Permits are non-refundable, so read the cancellation terms before you pay.",
                    "Gorilla Permit: US$1,500.\nGolden Monkey Permit: approx. US$100.\nPorter: approx. US$10-$20 plus tip.\nDriver & Vehicle from Kigali: approx. US$100-$150 per day.\nLodge in Musanze/Kinigi: approx. US$60-$300+ per night.",
                    new[]
                    {
                        "Lake Kivu at Rubavu (Gisenyi): About 1.5 hours west.",
                        "Kigali Genocide Memorial: Back in Kigali.",
                        "Nyungwe National Park: Montane rainforest in the far south-west."
                    },
                    "Year-round. The drier months, roughly June-September and December-February, make for less muddy trekking.",
                    "Medium (5/10). Each gorilla family takes at most 8 visitors a day.",
                    "Rating: 2/10. Steep, muddy forest trails at altitude; porters and sedan-chair carriers can be arranged.",
                    "1 to 2 days including the drive.")),

            // ─── Rubavu (optional extension) ───────────────────────────────────
            new(RegionRwanda, LakeKivuName, Rwanda, Rubavu, RwandaLangs, -1.6997, 29.2563,
                60m, "Car", 2,
                new[] { "beach_coast", "nature_outdoor", "viewpoint_scenic_spot" },
                new[] { "relaxing", "nature", "photography", "hidden_gem" },
                new[] { "Lake Kivu", "Gisenyi", "Rubavu", "Gisenyi Beach", "Rubavu Beach", "Lake Kivu Rubavu" }, "Landscape",
                Desc(
                    "Lake Kivu is one of Africa's Great Lakes, a deep lake with many islands on Rwanda's western border with the DR Congo. Rubavu (Gisenyi), on the northern shore, has sandy beaches, lakeside hotels and boat trips to islands and fishing villages.\n\nWhat to Do: Watch the sunset over the lake, take a boat trip, and listen for the fishermen singing as they row out in their three-hulled boats in the evening.",
                    "Rwandans come to Rubavu for weekends and holidays, and the beachfront is full on Sundays and public holidays.",
                    "Best Access: About 1.5 hours by road from Musanze, or 3-3.5 hours from Kigali.\n\nOnward: The Congo Nile Trail runs south along the lake towards Karongi and Rusizi.",
                    "Boat Trips: Agree the price and route before you board and ask for life jackets.\n\nBorder: Goma in the DR Congo is next door. Do not cross the border, and check current UK, US or your own government's travel advice for the border area before you go.\n\n" + RwandaMoney,
                    "Swimming: Swim only at hotel or public beaches. The lake is deep and has no lifeguards.\n\nBorder Area: Stay on the Rwandan side and follow local advice.",
                    "Boat Trip: approx. US$30-$60 per boat.\nLakeside Hotel: approx. US$50-$200 per night.\nDriver from Musanze/Kigali: approx. US$100-$150 per day.",
                    new[]
                    {
                        "Volcanoes National Park: 1.5 hours north-east.",
                        "Kigali: 3-3.5 hours east.",
                        "Nyungwe National Park: South along the lake roads."
                    },
                    "The drier months, roughly June-September and December-February.",
                    "Low-Medium (4/10), busier on weekends.",
                    "Rating: 6/10. Hotel beaches are mostly level; boats need a step down.",
                    "1 to 2 nights.")),

            // ─── Nyamagabe (optional extension) ────────────────────────────────
            new(RegionRwanda, NyungweName, Rwanda, Nyamagabe, RwandaLangs, -2.4785, 29.2003,
                180m, "Car", 2,
                new[] { "nature_outdoor", "viewpoint_scenic_spot", "activity_experience" },
                new[] { "nature", "adventurous", "photography", "hidden_gem" },
                new[] { "Nyungwe", "Nyungwe Forest", "Nyungwe Canopy Walk", "Nyungwe Canopy Walkway", "Uwinka", "Nyungwe Forest National Park" }, "Landscape",
                Desc(
                    "Nyungwe National Park in south-west Rwanda protects one of Africa's oldest montane rainforests. It has chimpanzees, colobus monkeys and hundreds of bird species, and a canopy walkway hangs high above a forested ravine near the Uwinka visitor centre.\n\nWhat to Do: Walk the canopy walkway, hike a forest trail and, with an early start, track chimpanzees.",
                    "Streams that rise in Nyungwe feed both the Nile and Congo basins and supply much of Rwanda's water. The tea estates on the forest edge grow some of the country's best-known tea.",
                    "Best Access: A drive of around 5-6 hours from Kigali via Huye and Nyamagabe; plan at least two nights.\n\nUwinka: The visitor centre and canopy walkway trailhead are on the main road through the forest.",
                    "Fees: Nyungwe's 2025-2026 pricing scheme lists international-visitor packages from around US$150. Confirm on visitnyungwe.org whether your package includes the canopy walk. Some activities have age limits.\n\nBooking: Through visitnyungwe.org, the RDB portal or an operator.\n\n" + Umuganda,
                    "Rain: It rains often in the forest, so bring a rain jacket and good shoes.\n\nHeights: The canopy walkway is high and sways, which some people find hard.",
                    "Park Package: from approx. US$150.\nChimp Tracking: extra, confirm.\nLodging: approx. US$80-$500+ per night.\nDriver from Kigali: approx. US$100-$150 per day.",
                    new[]
                    {
                        "Lake Kivu at Rubavu (Gisenyi): North along Lake Kivu.",
                        "Kigali: A long day's drive back.",
                        "Volcanoes National Park: Gorillas and golden monkeys in the north."
                    },
                    "The drier months, roughly June-September and December-February.",
                    "Low (3/10).",
                    "Rating: 3/10. Steep forest trails and a suspended walkway.",
                    "2 nights including the drive.")),

            // ─── Kayonza (optional extension) ──────────────────────────────────
            new(RegionRwanda, AkageraName, Rwanda, Kayonza, RwandaLangs, -1.8750, 30.7100,
                150m, "Car", 2,
                new[] { "nature_outdoor", "activity_experience" },
                new[] { "nature", "photography", "adventurous", "family_friendly" },
                new[] { "Akagera", "Akagera Park", "Akagera Game Park", "Lake Ihema" }, "Landscape",
                Desc(
                    "Akagera National Park in eastern Rwanda is managed by African Parks with the Rwanda Development Board. It is savannah, lakes and papyrus swamp, and since lions and black rhinos were reintroduced it has all of the Big Five, along with giraffes, hippos and many birds.\n\nWhat to Do: Go for a long day safari from Kigali, or stay a night and take a sunset boat trip on Lake Ihema.",
                    "The park was heavily poached after 1994 and has since been restocked. It is the closest safari to Kigali.",
                    "Best Access: About 2.5 hours by road from Kigali via Kayonza to the southern gate. Leave at dawn for a day trip.",
                    "Entry: African Parks' 2025 price list gives an international-visitor day rate of about US$100 per adult, with lower daily rates for longer stays. Boat trips, guides and self-drive vehicle fees are extra; confirm the current tariff on africanparks.org.\n\nVehicle: Go with a driver-guide or join a park safari.\n\n" + RwandaMoney,
                    "Heat & Tsetse: It is hotter than Kigali and tsetse flies bite, so avoid dark blue and black clothing.\n\nWildlife: Stay in the vehicle except at designated points.",
                    "Park Entry: approx. US$100 per adult per day.\nBoat Trip on Lake Ihema: extra, confirm.\nDriver & Vehicle from Kigali: approx. US$120-$180 per day.",
                    new[]
                    {
                        "Kigali: 2.5 hours west.",
                        "Kimironko Market: On the way out of Kigali.",
                        "Volcanoes National Park: Gorilla trekking in the north-west."
                    },
                    "The dry months, roughly June-September and December-February.",
                    "Low (3/10).",
                    "Rating: 5/10. Game viewing from a vehicle on dirt roads.",
                    "A long day, or 1 night.")),

            // ─── Doha ──────────────────────────────────────────────────────────
            new(RegionQatar, IslamicArtName, Qatar, Doha, QatarLangs, 25.2955, 51.5390,
                15m, "Car", 1,
                new[] { "museum_gallery", "landmark_monument", "viewpoint_scenic_spot" },
                new[] { "art", "history", "architecture", "photography", "educational", "tourist_hotspot" },
                new[] { "MIA", "MIA Doha", "Museum of Islamic Art Doha", "MIA Park" }, "Exterior",
                Desc(
                    "The Museum of Islamic Art, designed by I. M. Pei and opened in 2008, sits on its own island off the Corniche. The collection covers more than a thousand years of Islamic art from Spain to Central Asia, including manuscripts, ceramics, metalwork, textiles and jewellery. A tall atrium window looks out over the Gulf, and MIA Park next door has a clear view of the West Bay towers.\n\nWhat to Do: See the galleries in the morning, then walk out to MIA Park for the skyline.",
                    "Doha families picnic in MIA Park at weekends, and the museum café is a common meeting place.",
                    "Best Access: On the Corniche. Take a taxi or ride-hail, or the Doha Metro to the nearest station (Souq Waqif or the Corniche area) and walk.\n\nCombine: Souq Waqif is a short walk or ride away.",
                    "Entry: QR 50 (about US$14) for non-resident adults; free for children 16 and under. Tickets are sold at the museum or through Qatar Museums.\n\nHours: Roughly 9:00 AM-7:00 PM, later on Thursdays and from about 1:30 PM on Fridays. The museum closes one weekday, and sources disagree on which, so check mia.org.qa/en/visit.\n\n" + QatarDress,
                    "Closing Day: The weekly closing day has varied, so check before you go.\n\nFriday: Opens only after Friday prayers.\n\nBags: Large bags may have to go in the cloakroom.",
                    "Entry: QR 50 (US$14).\nCafé: approx. QR 30-70 (US$8-$19).\nTaxi/Ride-hail in Doha: approx. QR 15-40 (US$4-$11).",
                    new[]
                    {
                        "Souq Waqif: A short walk or ride away.",
                        "National Museum of Qatar: Along the Corniche to the south-east.",
                        "Doha Corniche: Right outside."
                    },
                    "Weekday mornings; November to March for the park.",
                    "Medium (5/10).",
                    "Rating: 8/10. Modern and step-free, with lifts.",
                    "2 to 3 hours.")),

            new(RegionQatar, NationalMuseumName, Qatar, Doha, QatarLangs, 25.2878, 51.5480,
                15m, "Car", 1,
                new[] { "museum_gallery", "landmark_monument" },
                new[] { "history", "architecture", "photography", "educational", "family_friendly", "cultural" },
                new[] { "NMoQ", "Qatar National Museum", "Desert Rose Museum", "National Museum Doha" }, "Exterior",
                Desc(
                    "The National Museum of Qatar, designed by Jean Nouvel and opened in 2019, is a cluster of interlocking discs modelled on the desert rose crystal. Its galleries follow Qatar from its geology and Bedouin life through pearl diving to oil, gas and the modern state, and they wrap around the restored palace of Sheikh Abdullah bin Jassim Al Thani.\n\nWhat to Do: Follow the galleries in order, see the restored palace and watch the large film projections on the gallery walls.",
                    "Qatar went from pearling and fishing to oil and gas within one lifetime, and older Qatari visitors often recognise objects from their own childhoods in the displays.",
                    "Best Access: At the eastern end of the Corniche. Take the Doha Metro Gold Line to National Museum station, or a short taxi ride.",
                    "Entry: QR 50 (about US$14) for non-resident adults, with free entry for young children; confirm on qm.org.qa.\n\nHours: Roughly 9:00 AM-7:00 PM, opening in the afternoon on Fridays and closed one weekday (Tuesday is commonly listed). Check before you go.\n\n" + QatarDress,
                    "Size: The museum is large, so allow at least two hours and wear comfortable shoes.\n\nFriday: Afternoon opening only.",
                    "Entry: QR 50 (US$14).\nCafé/Restaurant: approx. QR 40-150 (US$11-$41).\nMetro Fare: approx. QR 2-6.",
                    new[]
                    {
                        "Museum of Islamic Art: Along the Corniche.",
                        "Souq Waqif: A short ride away.",
                        "Msheireb Downtown Doha: The rebuilt old town nearby."
                    },
                    "Weekday mornings.",
                    "Medium (5/10).",
                    "Rating: 8/10. Modern and step-free, with lifts.",
                    "2 to 3 hours.")),

            new(RegionQatar, SouqWaqifName, Qatar, Doha, QatarLangs, 25.2867, 51.5333,
                25m, "Walk", 1,
                new[] { "market_street_life", "food_experience", "historical_tour" },
                new[] { "cultural", "food_focused", "photography", "tourist_hotspot", "walkable", "social" },
                new[] { "Souk Waqif", "Souq Waqif Doha", "Falcon Souq", "Gold Souq Doha", "Souq Al Wakra" }, "Exterior",
                Desc(
                    "Souq Waqif is Doha's old market, rebuilt in traditional style in the 2000s on the site of the trading souq by the creek. Its mud-rendered lanes sell spices, perfume, textiles and gold, with shisha cafés and restaurants throughout, and the Falcon Souq and its falcon hospital are next door.\n\nWhat to Do: Come in the evening. Browse spices and oud, watch the falcons and have dinner at one of the Middle Eastern restaurants.",
                    "Qatari families and Doha's large expatriate population fill the souq in the evenings, especially on Thursday and Friday nights.",
                    "Best Access: Doha Metro Gold Line to Souq Waqif station, or a taxi. It is a short walk from the Museum of Islamic Art.",
                    "Entry: Free.\n\nHours: Shops typically open in the morning, close for a long afternoon break and reopen until late evening. Many close for Friday midday prayers.\n\nBargaining: Normal in many shops; keep it polite.\n\nAlcohol: The souq doesn't serve it. In Qatar alcohol is only served in licensed hotel bars and restaurants, and public drunkenness is an offence.\n\n" + QatarDress,
                    "Afternoons: Many shops shut in the afternoon heat, so come after about 4:00 PM.\n\nFake Goods: Check quality before buying gold, perfume or textiles.",
                    "Entry: Free.\nDinner: approx. QR 50-150 (US$14-$41).\nSpices/Oud: from approx. QR 20 (US$5).\nKarak Tea: approx. QR 1-5.",
                    new[]
                    {
                        "Museum of Islamic Art: A short walk.",
                        "Msheireb Downtown Doha: Next door.",
                        "Doha Corniche: Across the road."
                    },
                    "Evenings, especially November to March.",
                    "High (7/10) in the evenings.",
                    "Rating: 6/10. Mostly level lanes, with some uneven paving and steps into shops.",
                    "2 to 3 hours.")),

            new(RegionQatar, KataraName, Qatar, Doha, QatarLangs, 25.3600, 51.5260,
                30m, "Car", 1,
                new[] { "landmark_monument", "beach_coast", "food_experience" },
                new[] { "architecture", "cultural", "photography", "art", "food_focused", "family_friendly" },
                new[] { "Katara", "Katara Village", "Katara Cultural Village Doha", "Katara Beach", "Katara Amphitheatre", "Katara Mosque" }, "Exterior",
                Desc(
                    "Katara Cultural Village is a purpose-built cultural district on the coast between West Bay and The Pearl. It has galleries, theatres, an open-air Greek-style amphitheatre, tiled mosques, pigeon towers, restaurants and a public beach, and many of Doha's festivals are held here.\n\nWhat to Do: Walk to the amphitheatre and the gold and blue-tiled mosques, see an exhibition and have dinner by the sea.",
                    "Doha families come to Katara on cool evenings and weekends for the restaurants, the beach and the festivals. Check its events calendar for your dates.",
                    "Best Access: By taxi or ride-hail, or the Doha Metro Red Line to Katara station, then walk or take the shuttle.",
                    "Entry: Free to walk around; the beach and some exhibitions charge a fee.\n\nEvents: Festivals and concerts run through the cooler months; check katara.net.\n\n" + QatarDress,
                    "Distances: The district is spread out, so wear comfortable shoes.\n\nBeach Rules: Modest swimwear and family rules apply on the public beach.",
                    "Entry: Free.\nBeach: small fee, confirm.\nDinner: approx. QR 80-250 (US$22-$69).",
                    new[]
                    {
                        "The Pearl-Qatar: Just north.",
                        "Doha Corniche: South along the coast.",
                        "Museum of Islamic Art: Across the bay."
                    },
                    "Late afternoon into evening, November to March.",
                    "Medium (5/10), busier during festivals.",
                    "Rating: 7/10. Paved, mostly level paths with some steps.",
                    "2 to 3 hours.")),

            new(RegionQatar, PearlName, Qatar, Doha, QatarLangs, 25.3700, 51.5500,
                40m, "Car", 1,
                new[] { "neighborhood_district", "food_experience", "viewpoint_scenic_spot" },
                new[] { "architecture", "photography", "premium", "relaxing", "social" },
                new[] { "The Pearl", "Pearl Qatar", "The Pearl Qatar", "Porto Arabia", "Qanat Quartier" }, "Exterior",
                Desc(
                    "The Pearl-Qatar is an artificial island district north of West Bay. Porto Arabia has a yacht marina and Mediterranean-style promenades, Qanat Quartier has canals and pastel-coloured buildings, and both have cafés, restaurants and designer shops.\n\nWhat to Do: Walk the Porto Arabia marina, take photos in Qanat Quartier and stop for coffee or dinner by the water.",
                    "Many Doha expatriates live on The Pearl and spend weekend mornings at its brunch spots and promenades.",
                    "Best Access: By taxi or ride-hail, about 20-30 minutes from central Doha. Alternatively take the Doha Metro Red Line to Legtaifiya, then a tram or shuttle (confirm current service).",
                    "Entry: Free.\n\nAlcohol: Served only in licensed hotel venues, not in ordinary restaurants.\n\n" + QatarDress,
                    "Prices: Cafés and restaurants cost more than in the souq.\n\nHeat: The promenades are hot at midday, so go later in the day.",
                    "Entry: Free.\nCoffee: approx. QR 20-35 (US$5-$10).\nDinner: approx. QR 100-300 (US$27-$82).",
                    new[]
                    {
                        "Katara Cultural Village: Just south.",
                        "Doha Corniche: Back towards the centre.",
                        "Museum of Islamic Art: Across the bay from West Bay."
                    },
                    "Late afternoon and evening.",
                    "Medium (5/10).",
                    "Rating: 8/10. Wide, level promenades.",
                    "2 to 3 hours.")),

            new(RegionQatar, CornicheName, Qatar, Doha, QatarLangs, 25.3000, 51.5250,
                15m, "Walk", 1,
                new[] { "viewpoint_scenic_spot", "beach_coast", "activity_experience" },
                new[] { "photography", "walkable", "relaxing", "family_friendly", "budget_friendly" },
                new[] { "Corniche", "Doha Corniche Promenade", "Corniche Doha", "Dhow Cruise Doha" }, "Landscape",
                Desc(
                    "The Doha Corniche is a seven-kilometre promenade around Doha Bay, running from the Museum of Islamic Art to the towers of West Bay. It is lined with palms and lawns, and wooden dhows moored along it take visitors out on the water.\n\nWhat to Do: Walk or cycle a stretch at sunset and take a short dhow cruise to see the skyline lit up.",
                    "On cool evenings the Corniche fills with joggers, families and people walking after work.",
                    "Best Access: Doha Metro Red Line to Corniche station, or walk from the Museum of Islamic Art or Souq Waqif.",
                    "Entry: Free.\n\nDhow Cruises: Operators wait along the Corniche. Agree the price and length before you board; shared rides are usually cheap and private boats cost more.\n\n" + QatarHeat,
                    "Shade: There is little shade by day, so go at sunrise or sunset.\n\nBoats: Ask for life jackets.",
                    "Promenade: Free.\nShared Dhow Cruise: approx. QR 25-60 (US$7-$16) per person, confirm.\nPrivate Dhow: approx. QR 150-400 (US$41-$110) per boat.",
                    new[]
                    {
                        "Museum of Islamic Art: At the southern end.",
                        "Souq Waqif: Across the road.",
                        "Katara Cultural Village: North beyond West Bay."
                    },
                    "Sunset and evening, November to March.",
                    "Medium (5/10).",
                    "Rating: 9/10. Wide, level promenade.",
                    "1 to 2 hours.")),

            new(RegionQatar, MsheirebName, Qatar, Doha, QatarLangs, 25.2860, 51.5260,
                15m, "Walk", 1,
                new[] { "neighborhood_district", "museum_gallery", "historical_tour" },
                new[] { "history", "architecture", "walkable", "educational", "hidden_gem" },
                new[] { "Msheireb", "Msheireb Museums", "Bin Jelmood House", "Company House Doha", "Mohammed Bin Jassim House", "Radwani House" }, "Exterior",
                Desc(
                    "Msheireb Downtown Doha is a rebuilt district next to Souq Waqif, designed as a modern version of traditional Qatari architecture. Msheireb Museums occupy four restored heritage houses: Bin Jelmood House, on slavery in the Indian Ocean world; Company House, on the first oil workers; Mohammed Bin Jassim House; and Radwani House, on family life.\n\nWhat to Do: Visit Bin Jelmood House, which covers the trade in enslaved East Africans around the Gulf, then walk the shaded lanes and cafés.",
                    "Msheireb was rebuilt on the site of Doha's old town centre. Bin Jelmood House is one of the few museums in the Gulf that deals directly with the history of slavery.",
                    "Best Access: Msheireb is the Doha Metro's main interchange station for all lines, or walk from Souq Waqif.",
                    "Entry: Msheireb Museums have been free to enter; confirm current hours and any fees on msheireb.com. The houses usually open from morning to early evening, with afternoon-only hours on Fridays.\n\n" + QatarDress,
                    "Hours: The houses don't all keep the same hours, so check before going.\n\nHeat: Use the shaded walkways in the middle of the day.",
                    "Entry: Free (confirm).\nCoffee: approx. QR 15-30 (US$4-$8).",
                    new[]
                    {
                        "Souq Waqif: Next door.",
                        "National Museum of Qatar: A short ride east.",
                        "Museum of Islamic Art: On the Corniche."
                    },
                    "Mornings or late afternoons.",
                    "Low (3/10).",
                    "Rating: 8/10. Modern, level streets; some museum houses have lifts or ramps.",
                    "1.5 to 2 hours.")),

            // ─── Mesaieed / southern desert ───────────────────────────────────
            new(RegionQatar, InlandSeaName, Qatar, Mesaieed, QatarLangs, 24.6167, 51.3333,
                80m, "Car", 3,
                new[] { "nature_outdoor", "activity_experience", "viewpoint_scenic_spot", "beach_coast" },
                new[] { "adventurous", "nature", "photography", "tourist_hotspot" },
                new[] { "Khor Al Udeid", "Khor Al-Adaid", "Inland Sea", "Inland Sea Qatar", "Qatar Desert Safari", "Sealine", "Sealine Beach" }, "Landscape",
                Desc(
                    "Khor Al Adaid, the Inland Sea, is a tidal inlet of the Gulf that reaches into the sand dunes of Qatar's far south-east, on the border with Saudi Arabia. It is one of the few places where the sea meets large dunes, and it is a protected nature reserve that can only be reached across the sand.\n\nWhat to Do: Take a half-day or sunset desert safari by 4x4, with dune bashing, sandboarding, a camel ride and a swim or sunset at the water's edge.",
                    "Qatari families camp in the dunes in winter, and most Doha residents make the 4x4 trip to the Inland Sea at least once.",
                    "Best Access: Only by 4x4 across the dunes. Tours collect you from your Doha hotel, drive about an hour south to near Sealine (south of Mesaieed), let the tyres down and cross the dunes to the water.\n\nDon't Self-Drive: Go with a licensed operator unless you have desert-driving experience and recovery gear.",
                    "Tour Prices: These vary widely. One published example is QR 220-330 per person for a four-hour dunes and Inland Sea tour in a shared vehicle; sunset trips with dinner cost more, and camel rides and sandboarding are often extra. Check what's included.\n\nRough Ride: Dune bashing is bumpy. Ask the driver for a gentler ride if you prefer, and skip it if you're pregnant or have back problems.\n\n" + QatarHeat,
                    "Operators: Use a licensed operator with a well-maintained 4x4 and seatbelts.\n\nSun & Sand: Bring sunglasses, a hat and water, and protect phones and cameras from sand.\n\nBorder: Saudi Arabia is across the water, so follow your guide on where you can go.",
                    "Half-Day Safari: approx. QR 220-330 (US$60-$90) per person shared.\nCamel Ride: approx. QR 30 (US$8).\nSunset Safari with Dinner: approx. US$90-$140.",
                    new[]
                    {
                        "Doha Corniche: Back in the city.",
                        "Souq Waqif: For dinner after the desert.",
                        "Katara Cultural Village: Restaurants and beach on Doha's coast."
                    },
                    "November to March; afternoon or sunset trips in the cooler months.",
                    "Medium (5/10).",
                    "Rating: 2/10. Soft sand and a very bumpy 4x4 ride.",
                    "Half a day.")),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Wishlists
        // ═══════════════════════════════════════════════════════════════════════

        private const string FlyHomeNote =
            "Recent schedules show Qatar Airways flying non-stop from Doha to Lagos (about twice daily) and Abuja (several times a week). You can also route home via Kigali on RwandAir. Confirm current schedules.";

        // ItineraryItem.TimeOfDay is a controlled value (Morning/Afternoon/Evening, validated by the
        // admin and user itinerary endpoints), so any specific clock times go in the item titles.
        private static readonly WishlistSpec[] Wishlists =
        {
            new("kigali-doha", new[] { RegionRwanda, RegionQatar }, "Kigali & Doha",
                "An 8-day trip through two countries on a route many Nigerians already fly: RwandAir from Lagos to Kigali, four days in Rwanda, then on to Doha before flying home. In Kigali you start at the Kigali Genocide Memorial, visit the artists at Inema, have clothes made from African-print fabric at Kimironko Market and walk Nyamirambo with a guide from the women's cooperative. Then you drive north to Volcanoes National Park for golden monkeys, or a gorilla trek if your budget stretches to the US$1,500 permit. Lake Kivu, Akagera and Nyungwe are optional add-ons. From Kigali it is about six hours' flight to Doha for the Museum of Islamic Art, the National Museum of Qatar, evenings in Souq Waqif, a dhow on the Corniche and a 4x4 desert safari to the Inland Sea. Nigerians get a free visa on arrival in Rwanda (bring your yellow fever card) but need a Qatar visa in advance, so apply early. Best from November to March. All prices are rough USD estimates.",
                "Four days in Rwanda with golden monkeys or gorillas, Kigali's markets and art, then museums, souqs and the desert in Doha.",
                "Couples & Friends",
                new[] { "7 Nights in Mid-Range Hotels (4 in Rwanda, 3 in Doha)", "Driver & Vehicle for Kigali and Volcanoes National Park", "Golden Monkey Trek Permit", "Kigali Genocide Memorial Audio Guide", "Nyamirambo Women's Center Walking Tour", "Museum of Islamic Art & National Museum of Qatar Tickets", "Half-Day Desert Safari to the Inland Sea" },
                "Kigali, Rwanda, Doha, Qatar, Kigali Genocide Memorial, Gisozi, Kandt House, Inema Arts Center, Kimironko Market, Nyamirambo, Nyamirambo Women's Center, Kigali Convention Centre, Volcanoes National Park, gorilla trekking, golden monkeys, Musanze, Kinigi, Lake Kivu, Gisenyi, Rubavu, Akagera, Nyungwe, canopy walk, RwandAir, Museum of Islamic Art, MIA, National Museum of Qatar, Souq Waqif, Katara, The Pearl, Corniche, dhow cruise, Msheireb, Bin Jelmood House, desert safari, Khor Al Adaid, Inland Sea, dune bashing, Qatar visa, Hayya, Nigeria, Lagos, Abuja, multi-country",
                new[] { "Culture", "Wildlife", "City Break", "Two Countries" },
                2800m, 500m,
                "7 nights (shared double): 3 in Kigali, 1 in Musanze/Kinigi, 3 in Doha, mid-range hotels",
                "Flights Lagos-Kigali and Kigali-Doha on RwandAir and Doha-Lagos/Abuja home (budget approx. US$1,100-$1,600 for the multi-city routing; confirm fares); private driver in Rwanda including the Volcanoes trip; airport transfers; Doha Metro and ride-hail in Qatar",
                "Kigali Genocide Memorial, Kandt House, Inema Arts Center, Kimironko Market, Nyamirambo walking tour, golden monkey trek (gorilla trek optional), Museum of Islamic Art, National Museum of Qatar, Souq Waqif, Msheireb Museums, Inland Sea desert safari, Corniche dhow cruise, The Pearl",
                false, "Curious Explorer",
                new[]
                {
                    new DaySpec(1, "Lagos to Kigali", Kigali, Kigali, Kigali, new[]
                    {
                        new ItemSpec("Morning", "Fly Lagos to Kigali on RwandAir", "RwandAir flies Lagos-Kigali non-stop. Lagos is the only Nigerian city on its current booking site, and its Abuja service appears to have been dropped. Ask for a multi-city Lagos-Kigali-Doha ticket. Kigali is one hour ahead of Lagos.", 450m, "Arrival"),
                        new ItemSpec("Afternoon", "Arrive at Kigali International Airport", "Nigerians get a free 30-day visa on arrival. Have your yellow fever card, return or onward ticket and hotel address ready, and leave plastic bags at home because they're banned. You can buy an MTN SIM with MoMo at the airport.", 0m, "Free Visa on Arrival"),
                        new ItemSpec("Evening", "Kigali Convention Centre Lights & Dinner", "See the KCC dome lit up after dark, then have dinner at Kigali Heights.", 30m, "Night View", ConventionCentreName),
                    }),
                    new DaySpec(2, "Kigali: Genocide Memorial and Art", Kigali, Kigali, Kigali, new[]
                    {
                        new ItemSpec("Morning", "9:00 AM · Kigali Genocide Memorial", "Allow 2-3 hours with the audio guide to learn what happened in 1994. Entry is free and photos aren't allowed inside. On the last Saturday of the month (Umuganda) it opens around 1:00 PM.", 15m, "First Stop", GenocideMemorialName),
                        new ItemSpec("Afternoon", "Kandt House Museum", "The house of Kigali's founder, Richard Kandt, with natural and colonial history, live reptiles and a view over the old town.", 5m, "History", KandtHouseName),
                        new ItemSpec("Afternoon", "Inema Arts Center", "Contemporary Rwandan art from the resident artists' studios in Kacyiru. Free to browse.", 0m, "Local Favorite", InemaName),
                        new ItemSpec("Evening", "Dinner in Nyamirambo", "Brochettes, chapati and music on Nyamirambo's main street. Take a moto with a helmet or a taxi home.", 10m, "Street Food", NyamiramboName),
                    }),
                    new DaySpec(3, "Kimironko Market and North to Musanze", Kigali, Musanze, Musanze, new[]
                    {
                        new ItemSpec("Morning", "Kimironko Market & a Tailor", "Choose African-print fabric and order a shirt or dress from a market tailor, ready for collection on Day 5 before your flight.", 25m, "Local Life", KimironkoName),
                        new ItemSpec("Afternoon", "Drive to Musanze", "About 2-2.5 hours north on good tarmac through terraced hills towards the Virunga volcanoes.", 0m, "Road Trip"),
                        new ItemSpec("Afternoon", "Or: Akagera Day Safari Instead", "Swap the volcanoes for a Big Five safari in the east, about 2.5 hours from Kigali each way. Day entry is around US$100.", 100m, "Optional", AkageraName, true),
                        new ItemSpec("Evening", "Lodge Night in Musanze or Kinigi", "Go to bed early. The trek briefing at Kinigi is at about 7:00 AM.", 0m, "Early Night"),
                    }),
                    new DaySpec(4, "Volcanoes National Park", Musanze, Musanze, Kigali, new[]
                    {
                        new ItemSpec("Morning", "7:00 AM · Golden Monkey Trek", "After the briefing at Kinigi park headquarters, walk into the bamboo forest to find the rare golden monkeys. The permit is about US$100 and the minimum age is 12.", 100m, "Wildlife", VolcanoesName),
                        new ItemSpec("Morning", "Upgrade: Mountain Gorilla Trek", "One hour with a habituated gorilla family. The permit is US$1,500 per person (minimum age 15) and should be booked well ahead on the RDB portal. Treks take from 1 to over 6 hours.", 1500m, "Gorilla Permit", VolcanoesName, true),
                        new ItemSpec("Afternoon", "Drive Back to Kigali", "About 2-2.5 hours back to the capital.", 0m, "Road Trip"),
                        new ItemSpec("Afternoon", "Extension: Lake Kivu at Rubavu", "Continue 1.5 hours west to the beaches of Lake Kivu for sunsets, boat trips and the singing fishermen (add 1-2 nights).", 60m, "Optional Extension", LakeKivuName, true),
                        new ItemSpec("Evening", "Extension: Nyungwe Canopy Walk", "Add 2 nights in the south-west rainforest for the canopy walkway, chimpanzees and tea estates.", 150m, "Optional Extension", NyungweName, true),
                        new ItemSpec("Evening", "Back in Kigali", "A quiet last night in Rwanda.", 25m, "Last Night"),
                    }),
                    new DaySpec(5, "Kigali to Doha", Kigali, Kigali, Doha, new[]
                    {
                        new ItemSpec("Morning", "Nyamirambo Women's Center Walking Tour", "About three hours around Nyamirambo with a guide from the women-run cooperative. Collect your tailoring from Kimironko afterwards.", 25m, "Community Tourism", NyamiramboName),
                        new ItemSpec("Afternoon", "Fly Kigali to Doha", "RwandAir resumed Kigali-Doha in mid-2026 with around 6 flights a week. The flight takes roughly 6 hours, and Doha is one hour ahead of Kigali. Carry a printed copy of your approved Qatar visa.", 350m, "Onward Flight"),
                        new ItemSpec("Evening", "Late Arrival in Doha", "Take the Doha Metro Red Line or a taxi from Hamad International Airport to your hotel, or go straight to Souq Waqif for a late meal.", 15m, "Arrival", SouqWaqifName),
                    }),
                    new DaySpec(6, "Doha's Museums and Souq Waqif", Doha, Doha, Doha, new[]
                    {
                        new ItemSpec("Morning", "Museum of Islamic Art", "I. M. Pei's museum on the bay holds more than a thousand years of Islamic art; afterwards, walk out to MIA Park for the skyline. QR 50; check the weekly closing day and cover shoulders and knees.", 14m, "Top Museum", IslamicArtName),
                        new ItemSpec("Afternoon", "National Museum of Qatar", "Jean Nouvel's desert-rose building tells Qatar's story from Bedouin life and pearl diving to gas wealth. QR 50.", 14m, "Architecture", NationalMuseumName),
                        new ItemSpec("Evening", "Souq Waqif by Night", "Spices, oud, falcons and dinner in the old market, which is busiest after dark.", 30m, "Local Favorite", SouqWaqifName),
                    }),
                    new DaySpec(7, "Old Doha and the Inland Sea", Doha, Mesaieed, Doha, new[]
                    {
                        new ItemSpec("Morning", "Msheireb Museums", "Four restored heritage houses. Bin Jelmood House covers the trade in enslaved East Africans around the Gulf.", 0m, "Heritage Houses", MsheirebName),
                        new ItemSpec("Afternoon", "2:00 PM · Desert Safari to Khor Al Adaid", "Dune bashing and sandboarding in a 4x4 with a licensed operator, then sunset where the sea meets the dunes. About QR 220-330 per person in a shared vehicle.", 75m, "Desert", InlandSeaName),
                        new ItemSpec("Evening", "Dinner at Katara Cultural Village", "Back in Doha for the amphitheatre, the mosques and a seafront restaurant.", 40m, "Culture", KataraName),
                    }),
                    new DaySpec(8, "The Pearl, the Corniche and Home", Doha, Doha, Doha, new[]
                    {
                        new ItemSpec("Morning", "Walk the Doha Corniche", "Seven kilometres of waterfront from MIA to West Bay. Go early, before the heat.", 0m, "Skyline", CornicheName),
                        new ItemSpec("Afternoon", "The Pearl-Qatar", "The Porto Arabia marina and the canals of Qanat Quartier, with coffee by the water.", 30m, "Photo Spot", PearlName),
                        new ItemSpec("Evening", "Sunset Dhow Cruise", "A short ride on a wooden dhow to see the skyline lit up before you leave for the airport.", 15m, "Sunset", CornicheName, true),
                        new ItemSpec("Evening", "Fly Home to Nigeria", FlyHomeNote, 0m, "Journey's End"),
                    }),
                }),

            new("doha-kigali-stopover", new[] { RegionRwanda, RegionQatar }, "Doha with a Kigali Stopover",
                "A 6-day Doha city break with two days in Kigali on the way. RwandAir's Lagos-Doha connections already route through Kigali, so you can ask for a multi-city ticket with a stopover and spend two nights in Rwanda's capital. Nigerians get a free visa on arrival in Rwanda; bring your yellow fever card. In Kigali you see the Kigali Genocide Memorial, the artists at Inema, Kimironko Market and Nyamirambo. Then you fly on to Doha for four days: the Museum of Islamic Art, the National Museum of Qatar, Souq Waqif, Msheireb, The Pearl, the Corniche and a desert safari to the Inland Sea. Gorilla trekking is an optional add-on if you extend your time in Rwanda. You need a Qatar visa before you travel, so apply early. Best from November to March. All prices are rough USD estimates.",
                "Two days in Kigali on the way to Doha, then four days of museums, souqs and the desert.",
                "Couples & Friends",
                new[] { "5 Nights in Mid-Range Hotels (2 in Kigali, 3 in Doha)", "Kigali Driver for a Day", "Kigali Genocide Memorial Audio Guide", "Museum of Islamic Art & National Museum of Qatar Tickets", "Half-Day Desert Safari to the Inland Sea" },
                "Doha, Qatar, Kigali, Rwanda, stopover, layover, multi-city, RwandAir, Kigali Genocide Memorial, Kimironko Market, Inema Arts Center, Nyamirambo, Kandt House, Kigali Convention Centre, Volcanoes National Park, gorilla trekking, Museum of Islamic Art, MIA, National Museum of Qatar, Souq Waqif, Msheireb, Bin Jelmood House, Katara, The Pearl, Corniche, dhow cruise, desert safari, Khor Al Adaid, Inland Sea, Qatar visa, Hayya, Nigeria, Lagos, Abuja",
                new[] { "City Break", "Culture", "Stopover", "Two Countries" },
                2100m, 400m,
                "5 nights (shared double): 2 in Kigali, 3 in Doha, mid-range hotels",
                "Flights Lagos-Kigali-Doha on RwandAir with a Kigali stopover and Doha-Lagos/Abuja home (budget approx. US$1,000-$1,400; confirm fares); a driver for a day in Kigali; airport transfers; Doha Metro and ride-hail in Qatar",
                "Kigali Genocide Memorial, Inema Arts Center, Kimironko Market, Nyamirambo, Museum of Islamic Art, National Museum of Qatar, Souq Waqif, Msheireb Museums, Inland Sea desert safari, Katara, The Pearl, Corniche",
                false, "City Explorer",
                new[]
                {
                    new DaySpec(1, "Stopover in Kigali", Kigali, Kigali, Kigali, new[]
                    {
                        new ItemSpec("Morning", "Fly Lagos to Kigali on RwandAir", "Book Lagos-Doha on RwandAir as a multi-city ticket with two nights in Kigali. Lagos is RwandAir's only current Nigerian gateway; confirm.", 450m, "Stopover Ticket"),
                        new ItemSpec("Afternoon", "Arrive in Kigali", "Nigerians get a free 30-day visa on arrival. Have your yellow fever card, onward ticket to Doha and hotel address ready, and don't bring plastic bags.", 0m, "Free Visa on Arrival"),
                        new ItemSpec("Evening", "Kigali Convention Centre & Kigali Heights", "The lit dome, then dinner or drinks at Kigali Heights.", 30m, "Night View", ConventionCentreName),
                    }),
                    new DaySpec(2, "Kigali in a Day", Kigali, Kigali, Kigali, new[]
                    {
                        new ItemSpec("Morning", "9:00 AM · Kigali Genocide Memorial", "Allow 2-3 hours; entry is free and the audio guide is recommended. It opens around 1:00 PM on the last Saturday of the month (Umuganda).", 15m, "First Stop", GenocideMemorialName),
                        new ItemSpec("Afternoon", "Kimironko Market", "Fabric, baskets and tailors who can make clothes within a day or two in Kigali's biggest market.", 20m, "Local Life", KimironkoName),
                        new ItemSpec("Afternoon", "Inema Arts Center", "Contemporary Rwandan art in the resident artists' studios.", 0m, "Local Favorite", InemaName),
                        new ItemSpec("Afternoon", "Or: Kandt House Museum", "Kigali's colonial and natural history, with live reptiles, instead of the gallery.", 5m, "Optional", KandtHouseName, true),
                        new ItemSpec("Evening", "Nyamirambo Night", "Street food and music on Nyamirambo's main street.", 10m, "Street Food", NyamiramboName),
                        new ItemSpec("Evening", "Extension: Gorillas in Volcanoes National Park", "Add 2 nights to trek mountain gorillas from Musanze (permit US$1,500) or golden monkeys (about US$100).", 1500m, "Optional Extension", VolcanoesName, true),
                    }),
                    new DaySpec(3, "Kigali to Doha", Kigali, Kigali, Doha, new[]
                    {
                        new ItemSpec("Morning", "Nyamirambo Women's Center Walking Tour", "About three hours around Nyamirambo with the women's cooperative before your flight. Book ahead.", 25m, "Community Tourism", NyamiramboName, true),
                        new ItemSpec("Afternoon", "Fly Kigali to Doha", "RwandAir's flight takes roughly 6 hours, with around 6 flights a week in 2026; confirm. Doha is one hour ahead of Kigali. Carry a printed copy of your Qatar visa.", 0m, "Onward Flight"),
                        new ItemSpec("Evening", "Souq Waqif Late Dinner", "Drop your bags and go to the old souq for a late dinner and karak tea.", 30m, "Local Favorite", SouqWaqifName),
                    }),
                    new DaySpec(4, "Doha's Museums and the Corniche", Doha, Doha, Doha, new[]
                    {
                        new ItemSpec("Morning", "Museum of Islamic Art", "More than a thousand years of Islamic art in I. M. Pei's building. QR 50; check the weekly closing day and cover shoulders and knees.", 14m, "Top Museum", IslamicArtName),
                        new ItemSpec("Afternoon", "National Museum of Qatar", "The desert-rose building, with Qatar's history from pearl diving to gas. QR 50.", 14m, "Architecture", NationalMuseumName),
                        new ItemSpec("Evening", "Corniche Sunset & Dhow Cruise", "Walk the waterfront, then take a short dhow ride to see the skyline lit up.", 15m, "Sunset", CornicheName),
                    }),
                    new DaySpec(5, "Msheireb and the Inland Sea", Doha, Mesaieed, Doha, new[]
                    {
                        new ItemSpec("Morning", "Msheireb Museums", "Restored heritage houses next to Souq Waqif. Bin Jelmood House covers the trade in enslaved East Africans around the Gulf.", 0m, "Heritage Houses", MsheirebName),
                        new ItemSpec("Afternoon", "2:00 PM · Desert Safari to Khor Al Adaid", "Dune bashing, sandboarding and sunset at the Inland Sea with a licensed 4x4 operator.", 75m, "Desert", InlandSeaName),
                        new ItemSpec("Evening", "Katara Cultural Village", "Dinner by the sea and a walk to the amphitheatre and mosques.", 40m, "Culture", KataraName),
                    }),
                    new DaySpec(6, "The Pearl and Home", Doha, Doha, Doha, new[]
                    {
                        new ItemSpec("Morning", "The Pearl-Qatar", "The Porto Arabia marina and the canals of Qanat Quartier, with coffee by the water.", 30m, "Photo Spot", PearlName),
                        new ItemSpec("Afternoon", "Last Shopping at Souq Waqif", "Spices, oud and dates to take home. Many shops reopen around 4:00 PM after the afternoon break.", 30m, "Souvenirs", SouqWaqifName),
                        new ItemSpec("Evening", "Fly Home to Nigeria", FlyHomeNote, 0m, "Journey's End"),
                    }),
                }),
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Every blob filename this seeder references, for upload to blob storage.</summary>
        public static IReadOnlyList<string> ExpectedImageFiles =>
            Countries.Select(c => $"{c.Slug}_hero.jpg")
                .Concat(Wishlists.Select(w => $"{w.HeroSlug}_hero.jpg"))
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

            // ─── Batch 5: wishlists, days, items, destination links ─────────────
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
                        WishlistHeroImage = BlobBase + $"{spec.HeroSlug}_hero.jpg",
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
                        // Each city resolves to its own country, so a Kigali → Doha travel day works.
                        var day = new ItineraryDay
                        {
                            ItineraryDayId = Guid.NewGuid(),
                            DayNumber = daySpec.Number,
                            DayTitle = daySpec.Title,
                            MorningCityId = cityByKey[CityKey(daySpec.MorningCity)].CityId,
                            AfternoonCityId = cityByKey[CityKey(daySpec.AfternoonCity)].CityId,
                            EveningCityId = cityByKey[CityKey(daySpec.EveningCity)].CityId,
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

                // Wishlist ↔ Destination links (add only if missing): all destinations in every region the
                // wishlist visits (Rwanda and Qatar), also for pre-existing wishlists.
                var wishlistId = wishlist.WishlistId;
                var linked = (await db.WishlistDestinations
                        .Where(wd => wd.WishlistId == wishlistId)
                        .Select(wd => wd.DestinationId)
                        .ToListAsync(ct))
                    .ToHashSet();
                foreach (var destSpec in Destinations.Where(d => spec.Regions.Contains(d.Region)))
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

        // Resolves a day's city name to the (CountryName, CityName) key used by cityByKey.
        // City names are unique across Cities in this seeder (Single throws if that ever changes).
        private static (string Country, string City) CityKey(string cityName) =>
            (Cities.Single(c => c.Name == cityName).CountryName, cityName);

        private static string DestinationImageFile(DestSpec spec) =>
            $"{spec.Region}_{DestinationSearchFields.Normalize(spec.Name).Replace(' ', '-')}.jpg";

        private static string DestinationImageUrl(DestSpec spec) => BlobBase + DestinationImageFile(spec);

        // Same scoring rules as BaliSeeder / AfricaPocSeeder / KenyaNorthSeeder.
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
