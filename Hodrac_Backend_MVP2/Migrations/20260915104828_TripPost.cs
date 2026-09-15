using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hodrac_Backend_MVP2.Migrations
{
    /// <inheritdoc />
    public partial class TripPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    TripPostId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TripPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: true),
                    WishlistId = table.Column<Guid>(type: "uuid", nullable: true),
                    FreeTextDestination = table.Column<string>(type: "text", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsDateFlexible = table.Column<bool>(type: "boolean", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    CoverImageUrl = table.Column<string>(type: "text", nullable: true),
                    MaxGroupSize = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripPosts", x => x.Id);
                    table.CheckConstraint("CK_TripPost_NotBothDestinationAndWishlist", "NOT (\"DestinationId\" IS NOT NULL AND \"WishlistId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TripPosts_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "DestinationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TripPosts_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "WishlistId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TripInterests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripInterests_TripPosts_TripPostId",
                        column: x => x.TripPostId,
                        principalTable: "TripPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripThreads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripThreads_TripPosts_TripPostId",
                        column: x => x.TripPostId,
                        principalTable: "TripPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ThreadMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreadMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThreadMessages_TripThreads_TripThreadId",
                        column: x => x.TripThreadId,
                        principalTable: "TripThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripThreadParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripThreadParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripThreadParticipants_TripThreads_TripThreadId",
                        column: x => x.TripThreadId,
                        principalTable: "TripThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_IsRead",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_ThreadMessages_TripThreadId",
                table: "ThreadMessages",
                column: "TripThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_TripInterests_TripPostId_RequesterUserId",
                table: "TripInterests",
                columns: new[] { "TripPostId", "RequesterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripInterests_TripPostId_Status",
                table: "TripInterests",
                columns: new[] { "TripPostId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TripPosts_DestinationId_StartDate",
                table: "TripPosts",
                columns: new[] { "DestinationId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TripPosts_WishlistId_StartDate",
                table: "TripPosts",
                columns: new[] { "WishlistId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TripThreadParticipants_TripThreadId_UserId",
                table: "TripThreadParticipants",
                columns: new[] { "TripThreadId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripThreads_TripPostId",
                table: "TripThreads",
                column: "TripPostId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "ThreadMessages");

            migrationBuilder.DropTable(
                name: "TripInterests");

            migrationBuilder.DropTable(
                name: "TripThreadParticipants");

            migrationBuilder.DropTable(
                name: "TripThreads");

            migrationBuilder.DropTable(
                name: "TripPosts");
        }
    }
}
