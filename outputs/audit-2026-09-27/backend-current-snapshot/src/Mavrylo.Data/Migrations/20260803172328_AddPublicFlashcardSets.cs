using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicFlashcardSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "public_flashcard_sets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OwnerDeviceId = table.Column<string>(type: "text", nullable: false),
                    ClientSetId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_flashcard_sets", x => x.Id);
                    table.CheckConstraint("CK_public_flashcard_sets_status", "\"Status\" IN ('pending', 'approved')");
                    table.ForeignKey(
                        name: "FK_public_flashcard_sets_devices_OwnerDeviceId",
                        column: x => x.OwnerDeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_public_flashcard_sets_OwnerDeviceId_ClientSetId",
                table: "public_flashcard_sets",
                columns: new[] { "OwnerDeviceId", "ClientSetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_public_flashcard_sets_Status_UpdatedAt",
                table: "public_flashcard_sets",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "public_flashcard_sets");
        }
    }
}
