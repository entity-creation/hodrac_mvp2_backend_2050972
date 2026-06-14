using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AggregatedSearchRegistry",
                columns: table => new
                {
                    AggregatedSearchRegistryId = table.Column<Guid>(type: "uuid", nullable: false),
                    MasterSearchPhrase = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    KnownVariantsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SemanticClusterId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CanonicalSemanticPhrase = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SemanticEmbedding = table.Column<Vector>(type: "vector(384)", nullable: true),
                    TotalGlobalSearchCount = table.Column<long>(type: "bigint", nullable: false),
                    YoungCoupleSearchCount = table.Column<long>(type: "bigint", nullable: false),
                    FamilyPlannerSearchCount = table.Column<long>(type: "bigint", nullable: false),
                    AdventureDadSearchCount = table.Column<long>(type: "bigint", nullable: false),
                    LastSearchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AggregatedSearchRegistry", x => x.AggregatedSearchRegistryId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: false),
                    HasCompletedOnboarding = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategoryDescription = table.Column<string>(type: "text", nullable: false),
                    IconName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ColorHex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Continent = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CountryFlagEmoji = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    GlobalHeroImage = table.Column<string>(type: "text", nullable: false),
                    VisaRequirementsSummary = table.Column<string>(type: "text", nullable: false),
                    PowerPlugType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DrivingSide = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EstimatedDailyTaxRate = table.Column<decimal>(type: "numeric(5,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.CountryId);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    CurrencySymbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExchangeRateToBase = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    LastExchangeRateUpdate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.CurrencyId);
                });

            migrationBuilder.CreateTable(
                name: "Languages",
                columns: table => new
                {
                    LanguageId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    HelpfulSurvivalPhrasesJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequiresCertifiedLocalGuide = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Languages", x => x.LanguageId);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TagName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetPersonaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.TagId);
                });

            migrationBuilder.CreateTable(
                name: "Wishlists",
                columns: table => new
                {
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: false),
                    WishlistName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WishlistDescription = table.Column<string>(type: "text", nullable: false),
                    ShortStory = table.Column<string>(type: "text", nullable: false),
                    TotalDays = table.Column<int>(type: "integer", nullable: false),
                    PeopleType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WishlistHeroImage = table.Column<string>(type: "text", nullable: false),
                    GlobalInclusionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RawContentKeywords = table.Column<string>(type: "text", nullable: false),
                    PsychologicalVibeTagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DefaultTravelersCount = table.Column<int>(type: "integer", nullable: false),
                    BasePricePerPerson = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    CalculatedTotalCost = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    DepositAmountRequired = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    AccommodationInclusions = table.Column<string>(type: "text", nullable: false),
                    TransitInclusions = table.Column<string>(type: "text", nullable: false),
                    ActivityInclusions = table.Column<string>(type: "text", nullable: false),
                    TotalGlobalSaveCount = table.Column<long>(type: "bigint", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    FeaturedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrimaryPersonaTarget = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastInteractedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsTemplate = table.Column<bool>(type: "boolean", nullable: false),
                    ForkedFromId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wishlists", x => x.WishlistId);
                    table.ForeignKey(
                        name: "FK_Wishlists_Wishlists_ForkedFromId",
                        column: x => x.ForkedFromId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId");
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRefreshTokens",
                columns: table => new
                {
                    UserRefreshTokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeviceHint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRefreshTokens", x => x.UserRefreshTokenId);
                    table.ForeignKey(
                        name: "FK_UserRefreshTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CityDescription = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.CityId);
                    table.ForeignKey(
                        name: "FK_Cities_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "CountryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Destinations",
                columns: table => new
                {
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CleanNormalizedSearchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MetaphoneCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DoubleMetaphonePrimary = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DoubleMetaphoneSecondary = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CommonAlternateSpellingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DescriptionJson = table.Column<string>(type: "jsonb", nullable: false),
                    AverageCostPerDay = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    LuxuryRating = table.Column<int>(type: "integer", nullable: false),
                    AccessibilityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FamilyFriendlyScore = table.Column<int>(type: "integer", nullable: false),
                    AdventurePaceScore = table.Column<int>(type: "integer", nullable: false),
                    AestheticTrendScore = table.Column<int>(type: "integer", nullable: false),
                    PsychographicVibeTagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    SearchHitCount = table.Column<long>(type: "bigint", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SafetyLevel = table.Column<int>(type: "integer", nullable: false),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinations", x => x.DestinationId);
                    table.ForeignKey(
                        name: "FK_Destinations_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "CountryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CountryLanguages",
                columns: table => new
                {
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryLanguages", x => new { x.CountryId, x.LanguageId });
                    table.ForeignKey(
                        name: "FK_CountryLanguages_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "CountryId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CountryLanguages_Languages_LanguageId",
                        column: x => x.LanguageId,
                        principalTable: "Languages",
                        principalColumn: "LanguageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeaturedWishlistPool",
                columns: table => new
                {
                    FeaturedWishlistPoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: false),
                    PoolType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    DailyImpressionLimit = table.Column<int>(type: "integer", nullable: false),
                    CurrentImpressionsToday = table.Column<int>(type: "integer", nullable: false),
                    RandomSelectionWeight = table.Column<double>(type: "double precision", nullable: false),
                    LastRotationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeaturedWishlistPool", x => x.FeaturedWishlistPoolId);
                    table.ForeignKey(
                        name: "FK_FeaturedWishlistPool_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedWishlists",
                columns: table => new
                {
                    SavedWishlistId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedWishlists", x => x.SavedWishlistId);
                    table.ForeignKey(
                        name: "FK_SavedWishlists_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WishlistCollaborators",
                columns: table => new
                {
                    WishlistCollaboratorId = table.Column<Guid>(type: "uuid", nullable: false),
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SharedUserEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistCollaborators", x => x.WishlistCollaboratorId);
                    table.ForeignKey(
                        name: "FK_WishlistCollaborators_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WishlistPricingSnapshots",
                columns: table => new
                {
                    WishlistPricingSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: false),
                    TravelersCount = table.Column<int>(type: "integer", nullable: false),
                    BasePricePerPerson = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    OptionalActivitiesTotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    SeasonalSurchargePercent = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    DepositAmountRequired = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistPricingSnapshots", x => x.WishlistPricingSnapshotId);
                    table.ForeignKey(
                        name: "FK_WishlistPricingSnapshots_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransitRoutes",
                columns: table => new
                {
                    TransitRouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginCityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationCityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransitType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EstimatedCostPerPerson = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    DurationInMinutes = table.Column<int>(type: "integer", nullable: false),
                    RecommendedTimeBufferMinutes = table.Column<int>(type: "integer", nullable: false),
                    BookingReferenceUrl = table.Column<string>(type: "text", nullable: false),
                    CarbonFootprintKg = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubSegmentsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransitRoutes", x => x.TransitRouteId);
                    table.ForeignKey(
                        name: "FK_TransitRoutes_Cities_DestinationCityId",
                        column: x => x.DestinationCityId,
                        principalTable: "Cities",
                        principalColumn: "CityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransitRoutes_Cities_OriginCityId",
                        column: x => x.OriginCityId,
                        principalTable: "Cities",
                        principalColumn: "CityId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DestinationCategories",
                columns: table => new
                {
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationCategories", x => new { x.DestinationId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_DestinationCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationCategories_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DestinationCities",
                columns: table => new
                {
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationCities", x => new { x.DestinationId, x.CityId });
                    table.ForeignKey(
                        name: "FK_DestinationCities_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "CityId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationCities_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DestinationCurrencies",
                columns: table => new
                {
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationCurrencies", x => new { x.DestinationId, x.CurrencyId });
                    table.ForeignKey(
                        name: "FK_DestinationCurrencies_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "CurrencyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationCurrencies_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DestinationImages",
                columns: table => new
                {
                    DestinationImageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ImageType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ShotContext = table.Column<string>(type: "text", nullable: false),
                    IsAiGenerated = table.Column<bool>(type: "boolean", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationImages", x => x.DestinationImageId);
                    table.ForeignKey(
                        name: "FK_DestinationImages_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DestinationLanguages",
                columns: table => new
                {
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationLanguages", x => new { x.DestinationId, x.LanguageId });
                    table.ForeignKey(
                        name: "FK_DestinationLanguages_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationLanguages_Languages_LanguageId",
                        column: x => x.LanguageId,
                        principalTable: "Languages",
                        principalColumn: "LanguageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DestinationTags",
                columns: table => new
                {
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationTags", x => new { x.DestinationId, x.TagId });
                    table.ForeignKey(
                        name: "FK_DestinationTags_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedDestinations",
                columns: table => new
                {
                    SavedDestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedDestinations", x => x.SavedDestinationId);
                    table.ForeignKey(
                        name: "FK_SavedDestinations_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WishlistDestinations",
                columns: table => new
                {
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistDestinations", x => new { x.WishlistId, x.DestinationId });
                    table.ForeignKey(
                        name: "FK_WishlistDestinations_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WishlistDestinations_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItineraryDays",
                columns: table => new
                {
                    ItineraryDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayNumber = table.Column<int>(type: "integer", nullable: false),
                    DayTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MorningCityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AfternoonCityId = table.Column<Guid>(type: "uuid", nullable: true),
                    EveningCityId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransitFromPreviousDayRouteId = table.Column<Guid>(type: "uuid", nullable: true),
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItineraryDays", x => x.ItineraryDayId);
                    table.ForeignKey(
                        name: "FK_ItineraryDays_Cities_AfternoonCityId",
                        column: x => x.AfternoonCityId,
                        principalTable: "Cities",
                        principalColumn: "CityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItineraryDays_Cities_EveningCityId",
                        column: x => x.EveningCityId,
                        principalTable: "Cities",
                        principalColumn: "CityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItineraryDays_Cities_MorningCityId",
                        column: x => x.MorningCityId,
                        principalTable: "Cities",
                        principalColumn: "CityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItineraryDays_TransitRoutes_TransitFromPreviousDayRouteId",
                        column: x => x.TransitFromPreviousDayRouteId,
                        principalTable: "TransitRoutes",
                        principalColumn: "TransitRouteId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ItineraryDays_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItineraryItems",
                columns: table => new
                {
                    ItineraryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemDescription = table.Column<string>(type: "text", nullable: false),
                    ItemOrderIndex = table.Column<int>(type: "integer", nullable: false),
                    TimeOfDay = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    SocialProofBadge = table.Column<string>(type: "text", nullable: false),
                    IndividualCostModifier = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    IsOptionalActivity = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelectedByDefault = table.Column<bool>(type: "boolean", nullable: false),
                    ItineraryDayId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItineraryItems", x => x.ItineraryItemId);
                    table.ForeignKey(
                        name: "FK_ItineraryItems_ItineraryDays_ItineraryDayId",
                        column: x => x.ItineraryDayId,
                        principalTable: "ItineraryDays",
                        principalColumn: "ItineraryDayId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AggregatedSearchRegistry_MasterSearchPhrase",
                table: "AggregatedSearchRegistry",
                column: "MasterSearchPhrase");

            migrationBuilder.CreateIndex(
                name: "IX_AggregatedSearchRegistry_SemanticClusterId",
                table: "AggregatedSearchRegistry",
                column: "SemanticClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_AggregatedSearchRegistry_SemanticEmbedding",
                table: "AggregatedSearchRegistry",
                column: "SemanticEmbedding")
                .Annotation("Npgsql:IndexMethod", "ivfflat")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:lists", 100);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CreatedAt",
                table: "AspNetUsers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DisplayName",
                table: "AspNetUsers",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_CountryId",
                table: "Cities",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_CountryLanguages_LanguageId",
                table: "CountryLanguages",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationCategories_CategoryId",
                table: "DestinationCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationCities_CityId",
                table: "DestinationCities",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationCurrencies_CurrencyId",
                table: "DestinationCurrencies",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationImages_DestinationId",
                table: "DestinationImages",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationLanguages_LanguageId",
                table: "DestinationLanguages",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_AverageCostPerDay",
                table: "Destinations",
                column: "AverageCostPerDay");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_CleanNormalizedSearchName",
                table: "Destinations",
                column: "CleanNormalizedSearchName");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_CountryId",
                table: "Destinations",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_DoubleMetaphonePrimary",
                table: "Destinations",
                column: "DoubleMetaphonePrimary");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_MetaphoneCode",
                table: "Destinations",
                column: "MetaphoneCode");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_SearchHitCount",
                table: "Destinations",
                column: "SearchHitCount");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationTags_TagId",
                table: "DestinationTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedWishlistPool_WishlistId",
                table: "FeaturedWishlistPool",
                column: "WishlistId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryDays_AfternoonCityId",
                table: "ItineraryDays",
                column: "AfternoonCityId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryDays_EveningCityId",
                table: "ItineraryDays",
                column: "EveningCityId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryDays_MorningCityId",
                table: "ItineraryDays",
                column: "MorningCityId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryDays_TransitFromPreviousDayRouteId",
                table: "ItineraryDays",
                column: "TransitFromPreviousDayRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryDays_WishlistId_DayNumber",
                table: "ItineraryDays",
                columns: new[] { "WishlistId", "DayNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_ItineraryDayId",
                table: "ItineraryItems",
                column: "ItineraryDayId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedDestinations_DestinationId",
                table: "SavedDestinations",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedDestinations_UserId_DestinationId",
                table: "SavedDestinations",
                columns: new[] { "UserId", "DestinationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedWishlists_UserId_WishlistId",
                table: "SavedWishlists",
                columns: new[] { "UserId", "WishlistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedWishlists_WishlistId",
                table: "SavedWishlists",
                column: "WishlistId");

            migrationBuilder.CreateIndex(
                name: "IX_TransitRoutes_DestinationCityId",
                table: "TransitRoutes",
                column: "DestinationCityId");

            migrationBuilder.CreateIndex(
                name: "IX_TransitRoutes_OriginCityId",
                table: "TransitRoutes",
                column: "OriginCityId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_ExpiresAt",
                table: "UserRefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_TokenHash",
                table: "UserRefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_UserId_IsRevoked",
                table: "UserRefreshTokens",
                columns: new[] { "UserId", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "IX_WishlistCollaborators_WishlistId_UserId",
                table: "WishlistCollaborators",
                columns: new[] { "WishlistId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WishlistDestinations_DestinationId",
                table: "WishlistDestinations",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistPricingSnapshots_WishlistId",
                table: "WishlistPricingSnapshots",
                column: "WishlistId");

            migrationBuilder.CreateIndex(
                name: "IX_Wishlists_CreatedAt",
                table: "Wishlists",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Wishlists_ForkedFromId",
                table: "Wishlists",
                column: "ForkedFromId");

            migrationBuilder.CreateIndex(
                name: "IX_Wishlists_IsFeatured",
                table: "Wishlists",
                column: "IsFeatured");

            migrationBuilder.CreateIndex(
                name: "IX_Wishlists_IsTemplate",
                table: "Wishlists",
                column: "IsTemplate");

            migrationBuilder.CreateIndex(
                name: "IX_Wishlists_OwnerUserId",
                table: "Wishlists",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Wishlists_TotalGlobalSaveCount",
                table: "Wishlists",
                column: "TotalGlobalSaveCount");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AggregatedSearchRegistry");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CountryLanguages");

            migrationBuilder.DropTable(
                name: "DestinationCategories");

            migrationBuilder.DropTable(
                name: "DestinationCities");

            migrationBuilder.DropTable(
                name: "DestinationCurrencies");

            migrationBuilder.DropTable(
                name: "DestinationImages");

            migrationBuilder.DropTable(
                name: "DestinationLanguages");

            migrationBuilder.DropTable(
                name: "DestinationTags");

            migrationBuilder.DropTable(
                name: "FeaturedWishlistPool");

            migrationBuilder.DropTable(
                name: "ItineraryItems");

            migrationBuilder.DropTable(
                name: "SavedDestinations");

            migrationBuilder.DropTable(
                name: "SavedWishlists");

            migrationBuilder.DropTable(
                name: "UserRefreshTokens");

            migrationBuilder.DropTable(
                name: "WishlistCollaborators");

            migrationBuilder.DropTable(
                name: "WishlistDestinations");

            migrationBuilder.DropTable(
                name: "WishlistPricingSnapshots");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "Languages");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "ItineraryDays");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Destinations");

            migrationBuilder.DropTable(
                name: "TransitRoutes");

            migrationBuilder.DropTable(
                name: "Wishlists");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
