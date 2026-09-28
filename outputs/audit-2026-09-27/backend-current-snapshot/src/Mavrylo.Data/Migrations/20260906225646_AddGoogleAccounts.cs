using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Never choose a legacy owner or discard data automatically.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Users" WHERE "GoogleSub" IS NOT NULL GROUP BY "GoogleSub" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Duplicate legacy Google subjects must be resolved by the operator before AddGoogleAccounts; no users have been changed.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_GoogleSub",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_sessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_refresh_tokens",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "text", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_refresh_tokens", x => x.Hash);
                    table.ForeignKey(
                        name: "FK_account_refresh_tokens_account_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "account_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "\"GoogleSub\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleSub",
                table: "Users",
                column: "GoogleSub",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_refresh_tokens_SessionId",
                table: "account_refresh_tokens",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_account_sessions_UserId",
                table: "account_sessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_refresh_tokens");

            migrationBuilder.DropTable(
                name: "account_sessions");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_GoogleSub",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleSub",
                table: "Users",
                column: "GoogleSub");
        }
    }
}
