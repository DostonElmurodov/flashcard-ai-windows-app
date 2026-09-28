using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedPublicationAndEventSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAppleEventAt",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OwnerDeviceId",
                table: "public_flashcard_sets",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "OwnerAccountId",
                table: "public_flashcard_sets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAccountSubscription",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_public_flashcard_sets_OwnerAccountId_ClientSetId",
                table: "public_flashcard_sets",
                columns: new[] { "OwnerAccountId", "ClientSetId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_public_flashcard_sets_owner",
                table: "public_flashcard_sets",
                sql: "(\"OwnerDeviceId\" IS NOT NULL) <> (\"OwnerAccountId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_public_flashcard_sets_Users_OwnerAccountId",
                table: "public_flashcard_sets",
                column: "OwnerAccountId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_public_flashcard_sets_Users_OwnerAccountId",
                table: "public_flashcard_sets");

            migrationBuilder.DropIndex(
                name: "IX_public_flashcard_sets_OwnerAccountId_ClientSetId",
                table: "public_flashcard_sets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_public_flashcard_sets_owner",
                table: "public_flashcard_sets");

            migrationBuilder.DropColumn(
                name: "LastAppleEventAt",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "OwnerAccountId",
                table: "public_flashcard_sets");

            migrationBuilder.DropColumn(
                name: "RequiresAccountSubscription",
                table: "devices");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerDeviceId",
                table: "public_flashcard_sets",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
