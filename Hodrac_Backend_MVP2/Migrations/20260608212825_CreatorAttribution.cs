using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class CreatorAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "Creators",
                columns: table => new
                {
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Handle = table.Column<string>(type: "text", nullable: false),
                    PlatformName = table.Column<string>(type: "text", nullable: false),
                    ProfileUrl = table.Column<string>(type: "text", nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Creators", x => x.CreatorId);
                });

            migrationBuilder.CreateTable(
                name: "WishlistCreatorAttributions",
                columns: table => new
                {
                    WishlistCreatorAttributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalContentUrl = table.Column<string>(type: "text", nullable: false),
                    PermissionType = table.Column<string>(type: "text", nullable: false),
                    PermissionGrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PermissionEvidenceUrl = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AttributionNote = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistCreatorAttributions", x => x.WishlistCreatorAttributionId);
                    table.ForeignKey(
                        name: "FK_WishlistCreatorAttributions_Creators_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Creators",
                        principalColumn: "CreatorId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WishlistCreatorAttributions_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Creators_ContactEmail",
                table: "Creators",
                column: "ContactEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Creators_IsVerified",
                table: "Creators",
                column: "IsVerified");

            migrationBuilder.CreateIndex(
                name: "IX_Creators_PlatformName_Handle",
                table: "Creators",
                columns: new[] { "PlatformName", "Handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WishlistCreatorAttributions_CreatorId",
                table: "WishlistCreatorAttributions",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistCreatorAttributions_IsActive",
                table: "WishlistCreatorAttributions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistCreatorAttributions_PermissionGrantedAt",
                table: "WishlistCreatorAttributions",
                column: "PermissionGrantedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistCreatorAttributions_WishlistId_CreatorId",
                table: "WishlistCreatorAttributions",
                columns: new[] { "WishlistId", "CreatorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WishlistCreatorAttributions");

            migrationBuilder.DropTable(
                name: "Creators");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
