using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class CountryCodeAndCurrencyRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "AspNetUsers",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CurrencyRateSnapshots",
                columns: table => new
                {
                    BaseCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RatesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProviderUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyRateSnapshots", x => x.BaseCurrency);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurrencyRateSnapshots");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "AspNetUsers");
        }
    }
}
