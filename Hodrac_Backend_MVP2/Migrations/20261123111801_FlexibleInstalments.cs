using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class FlexibleInstalments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstalmentDatesMode",
                table: "TripPosts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "HostSet");

            migrationBuilder.AddColumn<DateOnly>(
                name: "PayInFullByDate",
                table: "TripPosts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "TripInterests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemovedReason",
                table: "TripInterests",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsInstalmentDatesJson",
                table: "TripInterests",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DueDate",
                table: "TripInstalments",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.CreateTable(
                name: "TripInterestInstalmentDates",
                columns: table => new
                {
                    TripInterestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ChosenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripInterestInstalmentDates", x => new { x.TripInterestId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_TripInterestInstalmentDates_TripInterests_TripInterestId",
                        column: x => x.TripInterestId,
                        principalTable: "TripInterests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripInterestInstalmentDates");

            migrationBuilder.DropColumn(
                name: "InstalmentDatesMode",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "PayInFullByDate",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "RemovedReason",
                table: "TripInterests");

            migrationBuilder.DropColumn(
                name: "TermsInstalmentDatesJson",
                table: "TripInterests");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DueDate",
                table: "TripInstalments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
