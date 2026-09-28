using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations
{
    /// <inheritdoc />
    public partial class AddIosAccountSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IosEnrolledAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_sync_records",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_sync_records", x => new { x.UserId, x.Kind, x.Id });
                    table.ForeignKey(
                        name: "FK_account_sync_records_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_sync_records");

            migrationBuilder.DropColumn(
                name: "IosEnrolledAt",
                table: "Users");
        }
    }
}
