using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pulsebreak.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Social : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ratings_Playlist_Rating",
                table: "ratings");

            migrationBuilder.AddColumn<int>(
                name: "BestRating",
                table: "ratings",
                type: "integer",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<int>(
                name: "Season",
                table: "ratings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Ratings from before seasons: the best so far is at least where they stand now.
            migrationBuilder.Sql("UPDATE ratings SET \"BestRating\" = GREATEST(\"Rating\", 1000);");

            migrationBuilder.CreateTable(
                name: "blocks",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocks", x => new { x.OwnerId, x.BlockedId });
                    table.ForeignKey(
                        name: "FK_blocks_accounts_BlockedId",
                        column: x => x.BlockedId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_blocks_accounts_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddresseeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Accepted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friendships", x => new { x.RequesterId, x.AddresseeId });
                    table.ForeignKey(
                        name: "FK_friendships_accounts_AddresseeId",
                        column: x => x.AddresseeId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_friendships_accounts_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Chat = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reports_accounts_ReportedId",
                        column: x => x.ReportedId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reports_accounts_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "season_results",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Playlist = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    FinalRating = table.Column<int>(type: "integer", nullable: false),
                    BestRating = table.Column<int>(type: "integer", nullable: false),
                    Matches = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_season_results", x => new { x.AccountId, x.Playlist, x.Season });
                    table.ForeignKey(
                        name: "FK_season_results_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_Playlist_Season_Rating",
                table: "ratings",
                columns: new[] { "Playlist", "Season", "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_blocks_BlockedId",
                table: "blocks",
                column: "BlockedId");

            migrationBuilder.CreateIndex(
                name: "IX_friendships_AddresseeId",
                table: "friendships",
                column: "AddresseeId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_ReportedId",
                table: "reports",
                column: "ReportedId");

            migrationBuilder.CreateIndex(
                name: "IX_reports_ReporterId_CreatedAt",
                table: "reports",
                columns: new[] { "ReporterId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_reports_Status_CreatedAt",
                table: "reports",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blocks");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "season_results");

            migrationBuilder.DropIndex(
                name: "IX_ratings_Playlist_Season_Rating",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "BestRating",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "ratings");

            migrationBuilder.CreateIndex(
                name: "IX_ratings_Playlist_Rating",
                table: "ratings",
                columns: new[] { "Playlist", "Rating" });
        }
    }
}
