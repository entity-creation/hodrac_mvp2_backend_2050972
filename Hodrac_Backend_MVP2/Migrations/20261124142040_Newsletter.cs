using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class Newsletter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UnsubscribedAt",
                table: "NewsLetterEmails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NewsletterOptOut",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "NewsletterIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PreviewText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Draft"),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SendStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    SentCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsletterIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsletterDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsletterDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsletterDeliveries_NewsletterIssues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "NewsletterIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterDeliveries_IssueId_Email",
                table: "NewsletterDeliveries",
                columns: new[] { "IssueId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterDeliveries_IssueId_Status",
                table: "NewsletterDeliveries",
                columns: new[] { "IssueId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterIssues_IssueDate",
                table: "NewsletterIssues",
                column: "IssueDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterIssues_Status_ScheduledFor",
                table: "NewsletterIssues",
                columns: new[] { "Status", "ScheduledFor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsletterDeliveries");

            migrationBuilder.DropTable(
                name: "NewsletterIssues");

            migrationBuilder.DropColumn(
                name: "UnsubscribedAt",
                table: "NewsLetterEmails");

            migrationBuilder.DropColumn(
                name: "NewsletterOptOut",
                table: "AspNetUsers");
        }
    }
}
