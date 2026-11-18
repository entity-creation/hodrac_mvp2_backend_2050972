using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class HodracHostV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "TripPosts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NGN");

            migrationBuilder.AddColumn<long>(
                name: "DepositKobo",
                table: "TripPosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Exclusions",
                table: "TripPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeeMode",
                table: "TripPosts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PassToTraveller");

            migrationBuilder.AddColumn<Guid>(
                name: "HostProfileId",
                table: "TripPosts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Inclusions",
                table: "TripPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFoundingFreeDeparture",
                table: "TripPosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHostedTrip",
                table: "TripPosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsListed",
                table: "TripPosts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "PricePerSeatKobo",
                table: "TripPosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeatHoldHours",
                table: "TripPosts",
                type: "integer",
                nullable: false,
                defaultValue: 48);

            migrationBuilder.AddColumn<string>(
                name: "TermsText",
                table: "TripPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsUpdatedAt",
                table: "TripPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TermsVersion",
                table: "TripPosts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "AmountPaidKobo",
                table: "TripInterests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AmountPaidViaHodracKobo",
                table: "TripInterests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "TripInterests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "TripInterests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<DateTime>(
                name: "SeatHoldExpiresAt",
                table: "TripInterests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedAt",
                table: "TripInterests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAcceptedIp",
                table: "TripInterests",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAcceptedUserAgent",
                table: "TripInterests",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsTextSha256",
                table: "TripInterests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TermsVersionAccepted",
                table: "TripInterests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    InstagramHandle = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WhatsAppNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    VerificationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Unverified"),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CacNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IdDocumentStorageKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PastTripProofUrls = table.Column<string>(type: "jsonb", nullable: false),
                    PaystackSubaccountCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SettlementBankCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SettlementBankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SettlementAccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SettlementAccountLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Plan = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Host"),
                    OperatorPlanExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFoundingHost = table.Column<bool>(type: "boolean", nullable: false),
                    FoundingFreeDepartureTripId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripInterestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualPayments_TripInterests_TripInterestId",
                        column: x => x.TripInterestId,
                        principalTable: "TripInterests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReminderLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripInterestId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstalmentSequence = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReminderLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReminderLogs_TripInterests_TripInterestId",
                        column: x => x.TripInterestId,
                        principalTable: "TripInterests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeatPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripInterestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InstalmentSequence = table.Column<int>(type: "integer", nullable: true),
                    SeatAmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    HodracFeeKobo = table.Column<long>(type: "bigint", nullable: false),
                    TravellerFeeKobo = table.Column<long>(type: "bigint", nullable: false),
                    TotalChargedKobo = table.Column<long>(type: "bigint", nullable: false),
                    PaystackFeeKobo = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SubaccountCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifyResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatPayments_TripInterests_TripInterestId",
                        column: x => x.TripInterestId,
                        principalTable: "TripInterests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeatPayments_TripPosts_TripPostId",
                        column: x => x.TripPostId,
                        principalTable: "TripPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TripInstalments",
                columns: table => new
                {
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TripPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripInstalments", x => new { x.TripPostId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_TripInstalments_TripPosts_TripPostId",
                        column: x => x.TripPostId,
                        principalTable: "TripPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripPageViewsDaily",
                columns: table => new
                {
                    TripPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripPageViewsDaily", x => new { x.TripPostId, x.Date, x.Source });
                    table.ForeignKey(
                        name: "FK_TripPageViewsDaily_TripPosts_TripPostId",
                        column: x => x.TripPostId,
                        principalTable: "TripPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostDocuments_HostProfiles_HostProfileId",
                        column: x => x.HostProfileId,
                        principalTable: "HostProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OperatorPlanPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorPlanPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorPlanPayments_HostProfiles_HostProfileId",
                        column: x => x.HostProfileId,
                        principalTable: "HostProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripPosts_HostProfileId",
                table: "TripPosts",
                column: "HostProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TripPosts_IsHostedTrip_Status",
                table: "TripPosts",
                columns: new[] { "IsHostedTrip", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TripInterests_PaymentStatus_SeatHoldExpiresAt",
                table: "TripInterests",
                columns: new[] { "PaymentStatus", "SeatHoldExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HostDocuments_HostProfileId_Kind",
                table: "HostDocuments",
                columns: new[] { "HostProfileId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_HostDocuments_UploadedAt",
                table: "HostDocuments",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HostProfiles_Slug",
                table: "HostProfiles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostProfiles_UserId",
                table: "HostProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostProfiles_VerificationStatus",
                table: "HostProfiles",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ManualPayments_TripInterestId",
                table: "ManualPayments",
                column: "TripInterestId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorPlanPayments_HostProfileId",
                table: "OperatorPlanPayments",
                column: "HostProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorPlanPayments_Reference",
                table: "OperatorPlanPayments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReminderLogs_TripInterestId_InstalmentSequence_Kind",
                table: "PaymentReminderLogs",
                columns: new[] { "TripInterestId", "InstalmentSequence", "Kind" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_PayloadSha256",
                table: "PaymentWebhookEvents",
                column: "PayloadSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_ProcessedAt_ReceivedAt",
                table: "PaymentWebhookEvents",
                columns: new[] { "ProcessedAt", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_Reference",
                table: "PaymentWebhookEvents",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_PayerUserId",
                table: "SeatPayments",
                column: "PayerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_Reference",
                table: "SeatPayments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_Status_CreatedAt",
                table: "SeatPayments",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_TripInterestId_Status",
                table: "SeatPayments",
                columns: new[] { "TripInterestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_TripPostId_Status",
                table: "SeatPayments",
                columns: new[] { "TripPostId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_TripPosts_HostProfiles_HostProfileId",
                table: "TripPosts",
                column: "HostProfileId",
                principalTable: "HostProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TripPosts_HostProfiles_HostProfileId",
                table: "TripPosts");

            migrationBuilder.DropTable(
                name: "HostDocuments");

            migrationBuilder.DropTable(
                name: "ManualPayments");

            migrationBuilder.DropTable(
                name: "OperatorPlanPayments");

            migrationBuilder.DropTable(
                name: "PaymentReminderLogs");

            migrationBuilder.DropTable(
                name: "PaymentWebhookEvents");

            migrationBuilder.DropTable(
                name: "SeatPayments");

            migrationBuilder.DropTable(
                name: "TripInstalments");

            migrationBuilder.DropTable(
                name: "TripPageViewsDaily");

            migrationBuilder.DropTable(
                name: "HostProfiles");

            migrationBuilder.DropIndex(
                name: "IX_TripPosts_HostProfileId",
                table: "TripPosts");

            migrationBuilder.DropIndex(
                name: "IX_TripPosts_IsHostedTrip_Status",
                table: "TripPosts");

            migrationBuilder.DropIndex(
                name: "IX_TripInterests_PaymentStatus_SeatHoldExpiresAt",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "DepositKobo",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "Exclusions",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "FeeMode",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "HostProfileId",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "Inclusions",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "IsFoundingFreeDeparture",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "IsHostedTrip",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "IsListed",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "PricePerSeatKobo",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "SeatHoldHours",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "TermsText",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "TermsUpdatedAt",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "AmountPaidKobo",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "AmountPaidViaHodracKobo",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "SeatHoldExpiresAt",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedIp",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedUserAgent",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "TermsTextSha256",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "TermsVersionAccepted",
                table: "TripInterests");
        }
    }
}
