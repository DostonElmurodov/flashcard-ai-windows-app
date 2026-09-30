# Task 6A — stable shared operation quota (implementation handoff)

Status: implementation and local verification complete; **pending fresh independent scoped review and root acceptance**. No commit, push, PR, deployment, production migration, live Apple/AI call, or live quota/budget configuration was performed.

## Baseline and frozen source

Backend checkout: `test-results/paid-access-hardening/backend`, HEAD `3957f3e08c6317ff1495939e5c0cfbe7fea4f110`. The checkout began with five accepted, uncommitted Task 10A files. All five final SHA-256 hashes still exactly match `task10a-source-sha256.txt`; see `task6a-results/final-task10a-hash-verification.txt`. Task 6A adds 20 tracked-file changes and eight new source/test files. `task6a-results/final-task6a-incremental-tracked.diff` excludes the accepted Task 10A files; `final-untracked-source/` contains copies of every new file. `final-working-tree.diff`, `final-status.txt`, `final-head.txt`, and `final-source-sha256.txt` freeze the complete state. These evidence files are outside the backend Git checkout at `test-results/paid-access-hardening/task6a-results`.

## Behavior implemented

The new `usage_subjects`, `purchase_usage_scopes`, `usage_buckets`, `usage_imports`, `pre_purchase_usage_attributions`, and singleton readiness state support durable free-device, exact environment/transaction purchase, and account scopes. Purchase scopes attach only to the stored claimed account; account roots remain independent. Mobile restore uses the already-consumed purchase/account scope without importing that phone's unrelated free history. Strongly proven original-device free buckets are attributed once, including when the claim attached before original proof; deleted-account tombstones retain the opaque account root.

Reservation re-resolves lineage under a shared PostgreSQL topology lock and conditionally increments UTC day and applicable minute buckets in one transaction. Claim/initialization uses the exclusive lock. The paid daily/minute dimensions and free daily/optional minute dimension either all commit or all roll back. Exhaustion returns 429 `ai_quota_exceeded` with the ceiling to the latest blocking reset in `Retry-After`. Missing/invalid required synthetic configuration returns 503 `ai_quota_unconfigured`; unready or structurally corrupt accounting returns 503 `usage_reconciliation_required`. Denials precede provider work. Provider failure retains the one logical-operation reservation. Existing Task 7 per-attempt money reservation and guest/account pool selection remain separate.

The migration retains original `ai_usage` rows, imports provable day/minute legacy rows by unique row ID, sums legitimately joined histories, and records ambiguous/invalid/unattributed rows in `usage_reconciliation_issues` with readiness false. Populated migration rerun/restart is idempotent. No production-copy reconciliation was attempted; readiness must be settled before normal-mode activation. All quota values in tests are synthetic; no live D2 values were installed.

## Verification evidence

- `final-full-release-tests.log`: 772 passed, 0 failed, 0 skipped; process exit 0. B4 skip replaced with a meaningful key-rotation quota test.
- `final-release-build.log`: Release backend build succeeded, 0 errors.
- `final-migration-model-check-pinned.log`: pinned EF 10.0.9 reports no pending model changes. The earlier `final-migration-model-check.log` used EF 10.0.2 and is retained for transparency.
- `final-focused-corrected.log`: 11/11 PG lineage/concurrency and provider-failure controls. Earlier `final-focused.log` failed only because the missing-limit test fixture inherited synthetic defaults; corrected with explicit null override before the final full run.
- `migration-populated-first.log`: 2/2 populated migration/import tests. `quota-postgres-lineage-green.log`, `claim-race-rollover-first.log`, `same-owner-independent-first.log`, `late-origin-green.log`, `normal-money-quota-route.log`, `b4-key-rotation.log`, and `focused-legacy-boundary-green.log` preserve focused evidence.
- Earlier RED and correction logs are retained, including the initial mistaken missing-config 429 expectation (`paid-quota-red.log`) and corrected 503 RED (`paid-missing-config-red.log`). The actual late-origin attribution defect has `late-origin-red.log` then `late-origin-green.log`.
- `git diff --check` exited 0; Git emitted only working-copy LF/CRLF conversion notices. All PostgreSQL tests used the isolated `127.0.0.1:55440` `owl_tests` harness with per-fixture databases.

## Review focus and limits

Review the migration's provenance classification and readiness issue inventory against a sanitized production copy before any cutover. Historical ambiguous per-key usage is deliberately retained and fail-closed, never guessed from UUID or current active purchase. The source/tests do not prove that a production copy is fully reconciled. Quota values and live AI money budgets remain separate root/user decisions. Task 6B word identity/order is outside this implementation.

