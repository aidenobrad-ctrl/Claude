using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pulsebreak.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Progression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "challenges",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChallengeId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenges", x => new { x.AccountId, x.Period, x.ChallengeId });
                    table.ForeignKey(
                        name: "FK_challenges_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Playlist = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ranked = table.Column<bool>(type: "boolean", nullable: false),
                    ServerId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.MatchId);
                });

            migrationBuilder.CreateTable(
                name: "ratings",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Playlist = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Matches = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    Draws = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ratings", x => new { x.AccountId, x.Playlist });
                    table.ForeignKey(
                        name: "FK_ratings_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weapon_progress",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeaponId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weapon_progress", x => new { x.AccountId, x.WeaponId });
                    table.ForeignKey(
                        name: "FK_weapon_progress_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_players",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Team = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Deaths = table.Column<int>(type: "integer", nullable: false),
                    Headshots = table.Column<int>(type: "integer", nullable: false),
                    Objective = table.Column<int>(type: "integer", nullable: false),
                    RoundsWon = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    XpGained = table.Column<int>(type: "integer", nullable: false),
                    LevelBefore = table.Column<int>(type: "integer", nullable: false),
                    LevelAfter = table.Column<int>(type: "integer", nullable: false),
                    RatingBefore = table.Column<int>(type: "integer", nullable: true),
                    RatingAfter = table.Column<int>(type: "integer", nullable: true),
                    ChallengesCompleted = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_players", x => new { x.MatchId, x.AccountId });
                    table.ForeignKey(
                        name: "FK_match_players_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_match_players_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_match_players_AccountId_EndedAt",
                table: "match_players",
                columns: new[] { "AccountId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_Playlist_Rating",
                table: "ratings",
                columns: new[] { "Playlist", "Rating" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenges");

            migrationBuilder.DropTable(
                name: "match_players");

            migrationBuilder.DropTable(
                name: "ratings");

            migrationBuilder.DropTable(
                name: "weapon_progress");

            migrationBuilder.DropTable(
                name: "matches");
        }
    }
}
