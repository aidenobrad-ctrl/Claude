using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pulsebreak.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    EmailNormalized = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Username = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UsernameNormalized = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Disabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "loadout_states",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquippedId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EquippedUpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loadout_states", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_loadout_states_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loadouts",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoadoutId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loadouts", x => new { x.AccountId, x.LoadoutId });
                    table.ForeignKey(
                        name: "FK_loadouts_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_settings",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_settings", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_player_settings_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_profiles_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_EmailNormalized",
                table: "accounts",
                column: "EmailNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_UsernameNormalized",
                table: "accounts",
                column: "UsernameNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loadout_states");

            migrationBuilder.DropTable(
                name: "loadouts");

            migrationBuilder.DropTable(
                name: "player_settings");

            migrationBuilder.DropTable(
                name: "profiles");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
