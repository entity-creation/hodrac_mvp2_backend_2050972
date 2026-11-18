using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class HodracHostV1Checkout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorizationUrl",
                table: "SeatPayments",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RefundedKobo",
                table: "SeatPayments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorizationUrl",
                table: "SeatPayments");

            migrationBuilder.DropColumn(
                name: "RefundedKobo",
                table: "SeatPayments");
        }
    }
}
