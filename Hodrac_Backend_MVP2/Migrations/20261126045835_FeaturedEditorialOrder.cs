using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class FeaturedEditorialOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EditorialOrder",
                table: "FeaturedWishlistPool",
                type: "integer",
                nullable: true);

            // Backfill existing Editorial entries in the order the featured section used to
            // show them (most-saved wishlist first, entry id as tie-breaker) so nothing moves on deploy.
            migrationBuilder.Sql(BackfillEditorialOrderSql);
        }

        /// <summary>Exposed so the backfill rule can be tested against a real database.</summary>
        public const string BackfillEditorialOrderSql = """
                UPDATE "FeaturedWishlistPool" AS p
                SET "EditorialOrder" = o.rn
                FROM (
                    SELECT fp."FeaturedWishlistPoolId",
                           ROW_NUMBER() OVER (ORDER BY w."TotalGlobalSaveCount" DESC, fp."FeaturedWishlistPoolId") AS rn
                    FROM "FeaturedWishlistPool" fp
                    JOIN "Wishlists" w ON w."WishlistId" = fp."WishlistId"
                    WHERE fp."PoolType" = 'Editorial'
                ) AS o
                WHERE p."FeaturedWishlistPoolId" = o."FeaturedWishlistPoolId";
                """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditorialOrder",
                table: "FeaturedWishlistPool");
        }
    }
}
