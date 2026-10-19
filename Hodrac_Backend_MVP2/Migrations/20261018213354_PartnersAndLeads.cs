using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class PartnersAndLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartnerLeads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BusinessType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Interests = table.Column<List<string>>(type: "text[]", nullable: false),
                    Message = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerLeads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    PartnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultCtaLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.PartnerId);
                });

            migrationBuilder.CreateTable(
                name: "PartnerLinks",
                columns: table => new
                {
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: true),
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookingUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CtaLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsSponsored = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerLinks", x => x.LinkId);
                    table.CheckConstraint("CK_PartnerLink_ExactlyOneTarget", "(\"DestinationId\" IS NULL) <> (\"WishlistId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_PartnerLinks_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartnerLinks_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "PartnerId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartnerLinks_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartnerLinkClicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Referrer = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ClickedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerLinkClicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerLinkClicks_PartnerLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "PartnerLinks",
                        principalColumn: "LinkId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerLeads_CreatedAt",
                table: "PartnerLeads",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerLinkClicks_LinkId_ClickedAt",
                table: "PartnerLinkClicks",
                columns: new[] { "LinkId", "ClickedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerLinks_DestinationId_IsActive",
                table: "PartnerLinks",
                columns: new[] { "DestinationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerLinks_PartnerId",
                table: "PartnerLinks",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerLinks_WishlistId_IsActive",
                table: "PartnerLinks",
                columns: new[] { "WishlistId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerLeads");

            migrationBuilder.DropTable(
                name: "PartnerLinkClicks");

            migrationBuilder.DropTable(
                name: "PartnerLinks");

            migrationBuilder.DropTable(
                name: "Partners");
        }
    }
}
