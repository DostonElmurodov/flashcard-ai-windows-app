using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedSubscriptionOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerAccountId",
                table: "subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_OwnerAccountId",
                table: "subscriptions",
                column: "OwnerAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_Users_OwnerAccountId",
                table: "subscriptions",
                column: "OwnerAccountId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_Users_OwnerAccountId",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_OwnerAccountId",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "OwnerAccountId",
                table: "subscriptions");
        }
    }
}
