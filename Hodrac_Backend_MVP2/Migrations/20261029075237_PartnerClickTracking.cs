using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class PartnerClickTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AppendUtm",
                table: "PartnerLinks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBot",
                table: "PartnerLinkClicks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "PartnerLinkClicks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "VisitorId",
                table: "PartnerLinkClicks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerLinkClicks_LinkId_IsBot_ClickedAt",
                table: "PartnerLinkClicks",
                columns: new[] { "LinkId", "IsBot", "ClickedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PartnerLinkClicks_LinkId_IsBot_ClickedAt",
                table: "PartnerLinkClicks");

            migrationBuilder.DropColumn(
                name: "AppendUtm",
                table: "PartnerLinks");

            migrationBuilder.DropColumn(
                name: "IsBot",
                table: "PartnerLinkClicks");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PartnerLinkClicks");

            migrationBuilder.DropColumn(
                name: "VisitorId",
                table: "PartnerLinkClicks");
        }
    }
}
