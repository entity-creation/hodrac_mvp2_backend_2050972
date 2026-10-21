using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class AdminTripModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAt",
                table: "TripPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HiddenByUserId",
                table: "TripPosts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenReason",
                table: "TripPosts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "TripPosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_TripPosts_Status_IsHidden_StartDate",
                table: "TripPosts",
                columns: new[] { "Status", "IsHidden", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripPosts_Status_IsHidden_StartDate",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "HiddenByUserId",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "HiddenReason",
                table: "TripPosts");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "TripPosts");
        }
    }
}
