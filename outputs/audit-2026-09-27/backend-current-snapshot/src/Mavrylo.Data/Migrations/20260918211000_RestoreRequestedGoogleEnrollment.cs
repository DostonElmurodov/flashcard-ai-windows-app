using Mavrylo.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260918211000_RestoreRequestedGoogleEnrollment")]
public sealed class RestoreRequestedGoogleEnrollment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // One-account administrative repair explicitly requested by the account owner,
        // who confirmed their existing Google login on iPhone. This is not a general
        // legacy enrollment policy. The digest identifies only the requested address;
        // an existing Google identity and prior server session must also be present.
        migrationBuilder.Sql("""
            DO $$
            DECLARE candidates integer;
            BEGIN
                LOCK TABLE "Users" IN SHARE ROW EXCLUSIVE MODE;
                SELECT count(*) INTO candidates FROM "Users" u
                WHERE NULLIF(btrim(u."GoogleSub"), '') IS NOT NULL
                  AND encode(sha256(convert_to(lower(btrim(u."Email")), 'UTF8')), 'hex')
                      = 'c75bc8e5167dfbdab6626d2f9e2968a15f840ea578f9f3116a453191b4f1e3de';
                IF candidates > 1 THEN
                    RAISE EXCEPTION 'Ambiguous requested Google account; no enrollment changed.';
                END IF;
                UPDATE "Users" u SET "IosEnrolledAt" = CURRENT_TIMESTAMP
                WHERE u."IosEnrolledAt" IS NULL
                  AND NULLIF(btrim(u."GoogleSub"), '') IS NOT NULL
                  AND encode(sha256(convert_to(lower(btrim(u."Email")), 'UTF8')), 'hex')
                      = 'c75bc8e5167dfbdab6626d2f9e2968a15f840ea578f9f3116a453191b4f1e3de'
                  AND EXISTS (SELECT 1 FROM account_sessions s WHERE s."UserId" = u."Id");
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never revoke admission on code rollback: subsequent iOS enrollment may exist.
    }
}
