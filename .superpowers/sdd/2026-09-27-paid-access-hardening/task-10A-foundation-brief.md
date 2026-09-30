# Task 10A — independent local foundation coverage

This bounded Task 10 work may run while Task 3B's Mac gate is externally blocked. It depends on accepted backend 2B.2, not the unfinished iOS restoration, quota or word-order changes. It does not accept Task 10 as a whole. Read `remaining-task-execution-constraints.md` and sections 2, 3, 4 and 6 of `task-10-local-closure-preparation.md`; those sections are the requirements. Do not read the full accumulated plan/history.

Repository: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, branch `codex/paid-access-hardening`, clean accepted base `3957f3e08c6317ff1495939e5c0cfbe7fea4f110`. Verify before edits. Preserve accepted ownership, mobile-grant, canonical reconciliation, quota/spend and content policies. No schema/migration, quota-scope, word-order or historical-marker change belongs to this task.

## Required proofs

1. Deterministic registration collision after both absent-row reads, exactly one total owner delta and no orphan, retained material/library/counter, conflicting material rejection. Prefer a test interceptor/seam over sleeps. Mere simultaneous task starts or counting only the referenced winning owner is insufficient.
2. Actual HTTP resume using a verifier that checks the expected key/path/body hash; successful zero-byte and `{}` requests, stored owner/library/key/counter preservation and JWT identity, no-store, wrong key/path/body, unknown key, reused challenge, nonempty/malformed/authority JSON, null body as appropriate, known-length and streamed size boundaries. Do not substitute an unconditional verifier or only service-level tests.
3. Proof consumption and counter advance survive later business rollback and a fresh context/restarted host. Same challenge and old counter deny; fresh higher counter succeeds once; rolled-back business data stays absent. Do not reframe this as quota reservation rollback.
4. Register and assertion-challenge missing/null fields, JSON null/malformed body and valid controls have stable `{code,error}` contracts with no rejected-request owner/device/challenge/provider side effects. Preserve cryptographic/unknown-key/malformed status distinctions. If framework model validation differs, prove RED then make the narrowest compatible repair.

Use offline fakes for Apple/App Attest/provider boundaries, explicit synthetic settings and actual isolated PostgreSQL where persistence/concurrency matters. A coverage gap does not itself prove a product defect; classify executed failures accurately. Fix only defects these bounded tests substantiate. Root owns cross-task decisions; ask through agent messaging if a repair would change accepted behavior or require schema changes.

Local PostgreSQL is already configured at `127.0.0.1:55440`, database `postgres`, user `owl_tests`, with per-test `owl_test_*` databases. Use process-only `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`. Verify readiness; do not touch port5432, production databases or restart the shared test cluster blindly. Preserve existing raw evidence. Tool location for migration checks only if actually needed: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/tools/dotnet-ef.exe` pinned10.0.9.

## Process and output

Use a fresh implementer, no subagents. You are the sole product writer while assigned this task; iOS source is frozen. Use TDD for actual fixes; run simplify after every coding iteration. Run focused tests for changed behavior, then affected backend Release suite/build and required local checks once. The existing B4 skip remains explicitly unresolved for Task6A; do not remove it or count it as fixed.

Do not commit, push, dispatch Mac jobs, create PRs, deploy, change budgets/configuration outside tests, run live Apple/AI calls or perform production inventory. Keep all code uncommitted. Root will freeze source/diff/untracked files for a fresh clean-context GPT-6 Sol review under the latest user model-routing rule.

Write `task-10A-foundation-report.md` with changes, actual RED/GREEN or coverage-only classifications, commands and output paths, simplify results, final tests/warnings and limits. Save raw logs under `test-results/paid-access-hardening/task10a-foundation-results`. Return status, changed files, test totals and concerns only. The historical-marker matrix, populated migration/restart preservation over final6A/6B schema, production inventory and whole-feature review remain separate.

