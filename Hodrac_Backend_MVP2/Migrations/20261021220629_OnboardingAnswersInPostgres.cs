using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingAnswersInPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnboardingBudgetProfile",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingCompletedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnboardingPrimaryPriority",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnboardingTripTypePreference",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnboardingWhoTheyTravelWith",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingBudgetProfile",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingPrimaryPriority",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingTripTypePreference",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OnboardingWhoTheyTravelWith",
                table: "AspNetUsers");
        }
    }
}
