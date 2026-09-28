# Task10 bounded backend implementation report

**Status:** DONE_WITH_CONCERNS — local preparation committed; release acceptance
and Task10's live/deployment gates remain open.

**Checkout:** `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`,
branch `codex/paid-access-hardening`. Clean base before edits:
`5c875dfe0f05b076abcda6da86e5ebd151652970`.
**Commit / exact source SHA:** `fa2d6b4164b22c737f6fa846905d38c0a4a33177`.
Git status after commit was clean (`## codex/paid-access-hardening...origin/main [ahead 6]`).
Existing directory-scoped personal Git identity was used.

## Delivered contract

- `ops/audit-subscription-state.sql` is an actual read-only aggregate query for
  the current `subscriptions` / `"Users"` schema. It requires explicit trusted
  Production or Sandbox environment and allowed products; invalid or absent
  settings fail even on empty data. It uses exact case-sensitive persisted
  environment/product comparisons, only trims configured products, matches .NET
  whitespace-only original IDs, identifies null/infinite/out-of-.NET-range
  expiry, claim/unclaimed/tombstone/inconsistent/orphan states, and labels
  duplicate legacy `DeviceUuid` references without claiming anonymous ownership.
  Its fixed bucket/count columns contain no raw IDs or personal text. A
  `ROLLUP` gives explicit zero totals on empty data. `tests/SubscriptionInventorySqlTests.cs`
  runs the actual copied script repeatedly against synthetic PostgreSQL data
  in a read-only transaction and verifies output/counts and invalid settings.
- `Program.cs` rejects `TestMode:Enabled=true` outside Development before
  `Database.MigrateAsync`. Every existing TestMode consumer (AI/account usage,
  words, controllers, public flag, filters and rate limiter callbacks) calls the
  same `TestModePolicy.IsEnabled(configuration, trustedHost)` predicate, which
  remains false outside Development even if reloadable configuration later
  changes. Development dynamic behavior remains. Real-host tests use full fake
  valid security settings and signed-Sandbox false-mode controls; the negative
  startup test uses an intentionally unreachable local database to establish
  pre-migration rejection.
- `docs/paid-access-rollout.md`, Linux ops guidance/example and
  `docs/paid-access-policy.md` record the existing-content client matrix, safe
  aggregate usage, historical quarantine/reconciliation, separated data/budgets,
  physical-device distribution routing and compatible backend/client gates.
  The misleading Docker command that printed all environment secrets was
  removed from the ops guide.

## RED/GREEN and validation evidence

All commands used process-only
`OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`.
The fixture created/dropped only its unique `owl_test_GUID` databases. No live
database or external provider was contacted.

| Command / scope | Actual result | Evidence |
|---|---|---|
| `dotnet test tests/Mavrylo.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~TestModeHostBoundaryTests --logger 'console;verbosity=normal'` before production guard | RED: 1 passed, 3 failed; Staging and Production started with TestMode true, and runtime config flip advertised true | `task-10-behavioral-red.md` (observed tool output, not a raw log) |
| `dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter FullyQualifiedName~SubscriptionInventorySqlTests --logger 'console;verbosity=normal'` during SQL TDD | RED: first 6/7 due constant-folded invalid-settings guard; then 6/8 with whitespace-only IDs and empty-table totals | `task-10-behavioral-red.md` |
| `dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter 'FullyQualifiedName~TestMode\|FullyQualifiedName~DeviceWord\|FullyQualifiedName~AiProtectionFilter\|FullyQualifiedName~AccountAi\|FullyQualifiedName~SubscriptionInventorySql' --verbosity quiet` | First 83 passed/14 failed from direct Development test fixtures; after explicit host fixtures 97/97 passed | `task-10-affected-debug.log`, `task-10-affected-debug-green.log` |
| `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --verbosity quiet` | 462 passed, 3 known ownership failures, 1 B4 skip; 466 total | `task-10-release-full.log` |
| `dotnet build Mavrylo.csproj -c Release --no-restore --verbosity quiet` | Passed, 0 errors, known NU1510 warning | `task-10-release-build.log` |
| `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~TestModeHostBoundaryTests\|FullyQualifiedName~SubscriptionInventorySqlTests' --verbosity quiet` after final negative-test/output assertion edits | 13/13 passed | `task-10-final-focused-release.log` |
| `git diff --cached --check` | Passed before commit | commit staging output |

The three Release failures are the existing Task1 B1/B2 ownership regressions:
`B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid`,
`B2_CopiedReceiptCannotFirstClaimDesktopOwnershipWithoutPriorOwnerProof`, and
`B2_CopiedReceiptCannotTransferExistingGuestPurchaseToUnrelatedDevice`.
The B4 global-guest case remains skipped. This Release result is for this
commit's worktree (before final test-only assertion/comment edits), and must
not be confused with the older 457/3/1 run at another SHA or the earlier
118/65 Task4 scoped runs. Final changed test classes then passed 13/13 in Release.

## Manual simplify record

- After the startup-test/compile iteration: reused `ApiFactory` with one
  optional host name and one shared signed-Sandbox config dictionary; removed
  an invalid optional-parameter constant. No extra factory class or live
  dependency was needed.
- After host-policy iteration: replaced all independent raw TestMode reads with
  one predicate; null host fails closed. Kept Program's early rejection separate
  only because it must stop startup before migration. Existing Development
  dynamic behavior remains; no extra cache or reload watcher was added.
- After SQL iterations: centralized the .NET whitespace set, used fixed
  environment buckets, and replaced duplicate total summation with `ROLLUP`.
  One read-only query covers all overlapping reason counts; no per-row export,
  schema change or anonymous-owner guess was added. Checked `Users` mapping
  against EF migration/source.
- After fixture fixes: added trusted Development host only to tests that
  directly instantiate services/filters and expect test access. Production
  behavior was not changed to accommodate tests.
- After documentation/final-test iteration: kept one rollout runbook and
  linked existing ops guidance; changed only the negative test to an
  inaccessible local DB and added output-safety assertions. The full broad
  Release suite was not repeated after those test/documentation-only edits;
  the changed classes were rerun 13/13.

## Remaining gates and exclusions

No live inventory, backup/restore drill, old-schema startup migration test,
ownership schema/migration, anonymous-owner recovery proof, D2 numeric quota
or monetary budget approval, isolated deployed Sandbox, TestFlight/App Review
routing, physical-device acceptance, push, PR, workflow dispatch, deployment,
or real Apple/AI/email call occurred. The backend manual CI currently
publishes/deploys and was not dispatched. Task4 quarantine requires real
purchase reconciliation before enforcement; the unresolved B1/B2 failures and
B4 skip remain whole-feature release gates. This bounded commit is ready for
the controller's independent clean-context review, not full Task10 acceptance.

## Fix round 1 after independent scoped review

The original implementation commit above remains `fa2d6b4164b22c737f6fa846905d38c0a4a33177`.
Current clean backend HEAD is `77e67f4886586e9e60c680717d696f3e09c11ab3`.
The independent finding remains recorded in `task-10-review.md`.

The P2 finding was correct: default `System.Text.Json` serialized the
`List<(string Bucket, Dictionary<string,long> Counts)>` to empty objects, so
the original repeatability and identifier checks were vacuous. Before changing
the serialization, I added positive assertions for `"Bucket":"TOTAL"` and
`"rows":15`. The actual inventory-only Release run failed **1/9** on the
first assertion and showed `[{},{},{},{},{}]`; the other eight tests passed.
Raw contemporaneous output: `task-10-fix1-red.log`.

The test now serializes an explicit anonymous projection with `Bucket` and
`Counts` properties for both executions. The positive checks confirm the
representation contains a real bucket and count; equality compares every
bucket and count key/value, and the three identifier checks inspect those
same populated values. The optional P3 wording now says the ten-card count
and first-ten order apply globally across the saved library, including all
collections. Decision provenance and D2-pending wording remain.

Validation used process-only `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`:

- `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SubscriptionInventorySqlTests --logger 'console;verbosity=normal'`: intentional RED **8 passed, 1 failed**; `task-10-fix1-red.log`.
- `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~TestModeHostBoundaryTests|FullyQualifiedName~SubscriptionInventorySqlTests' --logger 'console;verbosity=normal'`: GREEN **13 passed, 0 failed, 0 skipped**; `task-10-fix1-green.log`.
- `git diff --check` and `git diff --cached --check`: passed before the local commit. Git emitted only the existing global-ignore and line-ending warnings. Checkout is clean at the new SHA.

Manual simplify for this fix: one property-bearing projection helper is reused
for both executions; the test keeps its existing tuple result and SQL reader,
avoiding a second report DTO or duplicated comparison logic. The two positive
assertions make an empty serialization impossible to mistake for coverage.
Only the inventory test and policy wording changed; the old full Release log
remains evidence for its earlier SHA and was not rerun or relabeled. No live or
remote operation, ownership change, quota decision, or release gate changed.
