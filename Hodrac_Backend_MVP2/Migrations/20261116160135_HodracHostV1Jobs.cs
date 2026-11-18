using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class HodracHostV1Jobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostReportLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostReportLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostReportLogs_HostProfiles_HostProfileId",
                        column: x => x.HostProfileId,
                        principalTable: "HostProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostReportLogs_HostProfileId_WeekStart",
                table: "HostReportLogs",
                columns: new[] { "HostProfileId", "WeekStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostReportLogs");
        }
    }
}
