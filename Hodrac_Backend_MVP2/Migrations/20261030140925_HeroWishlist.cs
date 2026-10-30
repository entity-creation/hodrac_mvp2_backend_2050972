using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class HeroWishlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HeroWishlistSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    HeroWishlistId = table.Column<Guid>(type: "uuid", nullable: true),
                    HeroUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SetByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeroWishlistSettings", x => x.Id);
                    table.CheckConstraint("CK_HeroWishlistSetting_Singleton", "\"Id\" = 1");
                    table.ForeignKey(
                        name: "FK_HeroWishlistSettings_Wishlists_HeroWishlistId",
                        column: x => x.HeroWishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HeroWishlistSettings_HeroWishlistId",
                table: "HeroWishlistSettings",
                column: "HeroWishlistId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeroWishlistSettings");
        }
    }
}
