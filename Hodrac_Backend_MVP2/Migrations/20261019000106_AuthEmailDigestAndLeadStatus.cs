using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class AuthEmailDigestAndLeadStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InternalNotes",
                table: "PartnerLeads",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PartnerLeads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "New");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "PartnerLeads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailedAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActiveAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotificationEmailOptIn",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerLeads_Status_CreatedAt",
                table: "PartnerLeads",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_IsRead_EmailedAt",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "IsRead", "EmailedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PartnerLeads_Status_CreatedAt",
                table: "PartnerLeads");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RecipientUserId_IsRead_EmailedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "InternalNotes",
                table: "PartnerLeads");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PartnerLeads");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PartnerLeads");

            migrationBuilder.DropColumn(
                name: "EmailedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "LastActiveAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotificationEmailOptIn",
                table: "AspNetUsers");
        }
    }
}
