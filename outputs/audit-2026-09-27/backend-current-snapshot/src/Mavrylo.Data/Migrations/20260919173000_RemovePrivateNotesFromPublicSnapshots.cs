using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mavrylo.Migrations;

public partial class RemovePrivateNotesFromPublicSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Public copies must not retain personal notes. Private Words/account sync are untouched.
        // Invalid legacy snapshots remain subject to the existing catalog validation failure.
        migrationBuilder.Sql("""
            DO $purge_public_notes$
            DECLARE
                publication record;
                cards jsonb;
                cleaned jsonb;
                note_keys text[] := ARRAY['Notes', 'notes', 'UserNotes', 'userNotes', 'user_notes'];
            BEGIN
                FOR publication IN SELECT "Id", "SnapshotJson" FROM public_flashcard_sets FOR UPDATE LOOP
                    BEGIN
                        cards := publication."SnapshotJson"::jsonb;
                    EXCEPTION WHEN data_exception THEN
                        CONTINUE;
                    END;
                    IF jsonb_typeof(cards) IS DISTINCT FROM 'array' THEN
                        CONTINUE;
                    END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM jsonb_array_elements(cards) AS item(card)
                        WHERE jsonb_typeof(card) = 'object' AND card ?| note_keys
                    ) THEN
                        CONTINUE;
                    END IF;
                    SELECT jsonb_agg(
                        CASE WHEN jsonb_typeof(card) = 'object' THEN card - note_keys ELSE card END
                        ORDER BY ordinal
                    ) INTO cleaned
                    FROM jsonb_array_elements(cards) WITH ORDINALITY AS item(card, ordinal);
                    UPDATE public_flashcard_sets SET "SnapshotJson" = cleaned::text
                    WHERE "Id" = publication."Id";
                END LOOP;
            END
            $purge_public_notes$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Personal notes deliberately cannot be restored to public storage on downgrade.
    }
}
