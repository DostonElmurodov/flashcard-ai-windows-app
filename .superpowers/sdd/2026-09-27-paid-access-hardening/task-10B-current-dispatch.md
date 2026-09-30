# Task 10B current dispatch requirements

Prepared on 2026-09-29; not authorization to write before root dispatch. Task6B is currently in its first independent-review fix round. A fresh Task10B implementer follows acceptance of the backend source fixes. The Mac gate remains separate and must never be reported as passed.

Read `task-10-local-closure-brief.md`, `task-10-local-closure-preparation.md`, `legacy-migration-preflight.md`, `final-review-carryover.md`, and applicable repository instructions. This document supersedes their historical pending approvals, agent/model instructions, and already-closed foundation work.

## Scope

1. Audit all historical and current writers of exact authenticated device `RequiresAccountSubscription`. Document provenance and recoverable false positives. Implement the already-approved conservative recovery rule for an unresolved server-persisted marker: commercial entitlement/AI returns 503 `subscription_reconciliation_required` before quota/provider. This is a restriction signal, never ownership or paid-history proof. Do not infer authority from UUID equality, caller headers, copied purchase fields or old subscription rows.
2. Preserve credential register/resume/challenge and ordinary paired Apple Restore without Owl login. Applicable owner/mobile proof resolves through ordinary selection, including inactive/revoked results; active independently authenticated account fallback can supply its own authority. Unrelated free account, unverified account header, logout, empty discovery, outage and regenerated credentials do not erase the known marker. Preserve history and private namespaces.
3. Seed a populated genuine old schema and start/dispose TWO actual hosts using the final accepted 6A/6B migrations. Compare exact retained key/environment/counter/owner/library/card/review/account-sync/cursor/tombstone/idempotency/usage/claim fields and row counts. No invented credential, owner binding, grant, paid flag or UUID join. Preserve records; do not delete to satisfy indexes. Malformed historical inventory remains a documented reconciliation gate.
4. Exercise the migrated HTTP matrix with authentic fixture proof and fake providers, explicit zero-provider/zero-operation assertions for denial and positive controls that really reach the fake provider. Preserve accepted 6A accounting-readiness behavior: ambiguous historical paid usage cannot be cleared merely by a later grant. Ensure unrelated quota/budget/readiness settings do not mask the legacy-marker defect being tested.

Foundation preparation sections 2–4 and 6 already passed Task10A review (registration race/full owner delta, hash-bound resume, proof after business rollback, stable bootstrap request validation). Do not repeat or rewrite unchanged accepted work. Populated actual host startup is still required here. Inspect precise `IsApplicable` proof semantics; an authority label alone is not proof.

## Execution and evidence

- Fresh GPT-6 Sol implementer, one product writer. No additional agents from implementer. Root owns independent Astra Medium review and all Git/remote actions.
- Root records actual current base, dirty-source hash manifest and previous accepted freeze before dispatch; HEAD alone does not include accepted uncommitted work.
- Reproduce actual relevant backend RED, then narrow implementation and simplify after each coherent coding iteration. Batch focused cases and one full final affected suite; do not build/test after each edit or rerun unchanged suites.
- Isolated PostgreSQL only: `127.0.0.1:55440`, fixture-created databases, process-local `OWL_TEST_POSTGRES`; never production `5432`. Use the existing offline provider/Apple fakes. No network or live calls, deployment, migration/inventory against production, Mac workflow dispatch, commits or pushes.
- If an iOS lifecycle defect is demonstrated, report the precise dependency before editing it; Mac currently has a billing block. Never mark source-only client coverage GREEN.
- Preserve prior source archives, failed runs and analyses. Freeze new source/diff/hashes and raw verification logs, write `task-10-local-closure-report.md`, and wait for independent review. Root handles final commits/push after feature checks; the narrow iOS test-branch exception does not permit backend publication.

External gates remain physical Apple/iOS17, production inventory/backup/cutover, deployed configuration and explicit AI commercial quotas/monetary budgets. GitHub Actions budget is unrelated to AI-provider spending. Do not claim these external steps were executed by local tests.

Final documentation follow-through: backend/docs/paid-access-policy.md already reflects approved anonymous ownership/restore and first-ten table, but D2 bullet still calls ten-word fixtures noncommercial test inputs. Distinguish the approved ten-active-card policy from still-unapproved daily/minute AIoperation quotas and money budgets; document approved metadata-only materialization restriction and historical-marker recovery after implementation. Do not change commercial numeric AI settings.
