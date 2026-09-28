using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations
{
    /// <inheritdoc />
    public partial class AddIncrementalAccountSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SyncRevision",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ChangeRevision",
                table: "account_sync_records",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing clients bootstrap with since=0. Include all pre-upgrade
            // records (including tombstones) in that first incremental download.
            migrationBuilder.Sql("""
                UPDATE account_sync_records SET "ChangeRevision" = 1;
                UPDATE "Users" SET "SyncRevision" = 1
                WHERE EXISTS (SELECT 1 FROM account_sync_records r WHERE r."UserId" = "Users"."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_account_sync_records_UserId_ChangeRevision",
                table: "account_sync_records",
                columns: new[] { "UserId", "ChangeRevision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_account_sync_records_UserId_ChangeRevision",
                table: "account_sync_records");

            migrationBuilder.DropColumn(
                name: "SyncRevision",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ChangeRevision",
                table: "account_sync_records");
        }
    }
}
