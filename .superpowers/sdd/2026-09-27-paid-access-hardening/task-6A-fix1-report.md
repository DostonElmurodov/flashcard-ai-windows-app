# Task 6A fix round 1 — historical usage readiness

Status: the two P1 findings in `task-6A-review.md` have a local implementation and passing verification. **Pending fresh scoped review and root acceptance.** No commit, push, PR, deployment, production migration, production-data inventory, live Apple/AI call, or live quota/budget configuration was performed.

## Narrow change

The `AddStableOperationUsage` migration now treats equality between a legacy purchase's old library UUID and a keyed device's UUID only as an ambiguity signal. When such a `legacy_unproven` purchase exists, it leaves the keyed `ai_usage` row in place, records `unproven_subject`, and does not import that row as free or into a purchase. This check precedes the proven owner-binding branches, so the old UUID cannot override other ambiguity.

Every purchase already present when the migration runs now gets a `historical_paid_usage_unknown` reconciliation issue. Pre-Task-6A paid mobile AI operations were unmetered by `ai_usage`; neither zero legacy rows nor metered desktop rows establish complete paid history. Readiness remains false even after a later restore proof or migration rerun. Existing provable rows still import once and sum without truncation. A database with only genuinely free/empty installations remains ready, and a new proven purchase created after migration is unaffected.

## RED and verification

- `migration-findings-red.log`: both newly added populated migration tests failed on the original implementation at `Ready=true`. The first fixture migrates actual pre-restore device/purchase rows through `AddAnonymousPurchaseRestore` and confirms it generated **different** installation and `legacy_unproven` owner IDs, with no grant. The second fixture has an existing `server_token` purchase with no usage rows and no grant.
- `migration-focused-final.log`: 5/5 passed. The unproven case retains the original keyed row, leaves it unimported, inventories both the ambiguous row and historical paid gap, and returns normal-mode **AI admission** HTTP 503 `usage_reconciliation_required` with zero provider calls after a later verified restore grant is recorded. The test does not call the Apple restore endpoint; usage readiness is checked by AI quota admission, not by the restore/proof endpoint. The server-token empty-history case inventories the gap and denies reservation. The prior four-row account/purchase import still sums 5 day / 2 minute counts exactly once across rerun and restart while remaining unready for unknown historical mobile usage. The free/empty installation and post-migration new-purchase positive controls remain ready.
- `final-full-release-tests.log`: 775 passed, 0 failed, 0 skipped; process exit 0.
- `final-release-build.log`: Release build succeeded, 0 errors.
- `final-migration-model-check-pinned.log`: pinned EF 10.0.9 reports no pending model changes.
- `git diff --check`: exit 0, with only Git LF/CRLF working-copy conversion notices.

All PostgreSQL tests used the isolated `127.0.0.1:55440` `owl_tests` fixture database harness. Focused intermediate compile/fixture errors are retained in `migration-focused-second.log` and `migration-focused-third.log`; they were corrected before the final focused and full runs.

## Frozen review delta

Backend HEAD remains `3957f3e08c6317ff1495939e5c0cfbe7fea4f110`. The original `task6a-results` snapshot and `task-6A-report.md` remain unchanged. The fix changes exactly two formerly untracked Task 6A files: `20260929150632_AddStableOperationUsage.cs` and `UsageMigrationPostgresTests.cs`. `task6a-fix1-results/fix1-incremental.diff` compares both with the original frozen copies. `original-task6a-source-comparison.txt` confirms those are the only two source-hash differences; `final-source-sha256.txt`, `final-untracked-source/`, `final-status.txt`, and `final-head.txt` freeze the complete current source state. All five accepted Task 10A hashes still match `task10a-source-sha256.txt` (`final-task10a-hash-verification.txt`).

A sanitized production-copy reconciliation is still necessary before any normal-mode production cutover. This local fix inventories unknown history; it does not infer old paid operation counts or decide live quota/budget values. Task 6B word identity/order remains separate.

