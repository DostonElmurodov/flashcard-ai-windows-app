# Task10 independent scoped review

Review date: 2026-09-27 (America/Chicago).

Reviewed backend checkout: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`.
Branch: `codex/paid-access-hardening`.
Base: `5c875dfe0f05b076abcda6da86e5ebd151652970`.
Head: `fa2d6b4164b22c737f6fa846905d38c0a4a33177`.

The head and clean product worktree were independently verified before and after inspection. The base-to-head diff check passed. No product or test source was modified, and no subagent, remote workflow, live database, Apple, AI or email service was used. Reviewer-created artifacts are this report, a local test TRX and the serializer reproduction below.

## Verdicts

**Scoped specification:** The production changes implement the bounded inventory, Development-only TestMode and rollout-documentation behavior. No new runtime authorization or SQL-classification defect was identified. The repeatability/privacy test evidence is partly vacuous, however, so scoped acceptance should wait for the focused P2 correction below. This is not acceptance of the entire paid-access feature or of Task10's deferred live/deployment work.

**Code quality:** Changes requested for one Important/P2 test correctness defect. The implementation otherwise stays appropriately small: one host-aware predicate is used throughout the existing TestMode consumers, startup rejection precedes migration, and the inventory is a single aggregate query with fixed output labels. No Critical/P0 or High/P1 finding. One optional documentation clarification is listed separately.

## Important finding

### P2 — Compare actual inventory values instead of serializing empty tuple objects

Location: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/tests/SubscriptionInventorySqlTests.cs:24-28`; report return type at line 151.

`Report` returns `List<(string Bucket, Dictionary<string, long> Counts)>`, whose value-tuple members are fields. The default `System.Text.Json` options used here omit fields, so the two report serializations are lists of empty objects. Consequently, changing a bucket, a count or a count-column name between the first and second execution while retaining the same number of rows still passes line 25. The three identifier checks also inspect no report values and always pass for these identifiers. A future identifier-bearing bucket/column regression therefore is not checked by these assertions. The independent first-report numeric assertions remain useful; they do not validate the second report or rescue the identifier assertions.

The reviewer reproduced the exact return-type behavior on .NET 10.0.11: a one-row list with bucket `private-test-id` and count `rows=15` serializes as `[{}]`; a one-row list with bucket `DIFFERENT` and count `rows=999` also serializes as `[{}]`. Evidence: `task-10-reviewer-results/tuple-serialization-proof.txt` beside this report.

Requested correction: compare the buckets and complete count dictionaries directly, or serialize an explicit property-bearing projection/record (or deliberately enable field serialization). Ensure the safety assertions examine all returned labels and values. Include a small positive assertion that the representation being checked contains the expected bucket and count key/value, so an empty representation cannot satisfy the test silently. Rerun the focused inventory tests after that correction. This finding is about validation accuracy; source inspection found no current identifier disclosure in the SQL itself.

## Optional documentation clarification

**P3, nonblocking:** `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/docs/paid-access-policy.md:20,27-30` records the ten-card content rule but does not explicitly say the count/order is global across the saved library. The supplied context calls this a global ten-card rule. Adding that word/scope would prevent a future reader from interpreting it per collection. D1/D3 versus recorded Tasks8/9 rulings and unresolved D2 AI-operation limits are otherwise distinguished correctly. This is a wording clarification, not an additional runtime finding.

## Scope checks and reasoning

- **Actual startup:** `Program.cs:18-19` checks the trusted host immediately after `CreateBuilder`; `Database.MigrateAsync` occurs at line 249. The negative tests exercise real `Program` in Production and Staging with an unreachable local database and demand an `InvalidOperationException` mentioning TestMode. They cannot pass merely because migration connection or unrelated required configuration failed.
- **Runtime reload:** `TestModePolicy.cs:6-8` requires `IHostEnvironment.IsDevelopment()` in addition to the current configuration flag. Every production TestMode consumer found in the changed controllers, resource filters, quota/word services and all three rate-limiter bypass callbacks uses that predicate. A missing optional host fails closed. The Production false-to-true runtime test verifies the public flag remains false, an exhausted account quota remains exhausted, an account without entitlement remains denied and the eleventh free word is rejected. Existing Development route tests exercise true-at-start and dynamic bypass behavior with fake providers. No rate-limiter callback has a remaining raw-flag-only bypass.
- **Sandbox independence:** The positive real-host test starts ASP.NET Production with trusted Apple Sandbox configuration, false TestMode, positive synthetic account quotas, protection/assertion enabled, signature skipping disabled and required fake identity/JWT settings. This proves startup compatibility and the tested local behavior, not an actual Apple-signed purchase or a physical-device distribution path.
- **Schema and claims:** Checked the actual `SubscriptionEntity`, EF mappings and current migrations, not only the synthetic fixture. `subscriptions` has `OriginalTransactionId` as primary key, nullable `OwnerAccountId` referencing `Users.Id` with `SetNull`, and the preserved `ClaimedAt` marker. The query's claimed/unclaimed/tombstoned/inconsistent categories match that schema; orphan-owner is an overlapping diagnostic. It makes no claim to inventory a nonexistent immutable anonymous-owner table.
- **Inventory policy parity:** Compared with `SubscriptionValidationPolicy`. Persisted environment/product comparisons are case-exact; configured product values alone are trimmed with the .NET whitespace set. Blank original IDs, null expiry, PostgreSQL positive/negative infinity and finite dates outside the materializable .NET interval are separate overlapping reasons. The upper SQL boundary of year 10000 correctly covers PostgreSQL's microsecond timestamp precision: the last finite microsecond of year 9999 is less than `DateTime.MaxValue`. Duplicate mutable DeviceUuid references and multiple claimed-account references are reported as triage counts, not proven duplicate anonymous owners.
- **Read-only and fail-closed:** The actual SQL has no data/schema mutations and emits fixed environment bucket strings plus counts. The trusted-settings CTE rejects missing/invalid settings; the final cross join also forces validation for empty tables, whose ROLLUP returns an explicit zero total. Synthetic local tests run the actual copied SQL and exercise these settings and historical-data categories. The P2 above limits what the current serialization assertions establish about repeated output and privacy.
- **Rollout/policy:** The runbook preserves quarantined historical metadata, WasEverPaid, established ownership and tombstones; requires precise reconciliation and legitimate-buyer recovery before enforcement; separates Sandbox/Production deployments, databases, credentials, counters and budgets; keeps TestMode Development-only; and expressly leaves backup/restore, old-schema startup migration, distribution routing, device acceptance, immutable ownership, approved numeric quotas/budgets and release authorization open. The old shared-dataset environment-switch advice is removed. D1 remains account-optional iPhone purchasing/paid use with desktop account access, and D3 keeps all ordinarily expired-paid saved content available for viewing/review. No new numerical AI or monetary approval is asserted.

## Independently inspected and generated evidence

The reviewer read the task brief, dispatch context, final review context, implementer report, full base-to-head diff, relevant surrounding source, actual SQL/test files and saved verification outputs.

| Evidence | Observed result and precise limit |
|---|---|
| Reviewer fresh focused Release run at unchanged head | `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~TestModeHostBoundaryTests\|FullyQualifiedName~SubscriptionInventorySqlTests' --verbosity quiet --logger 'trx;LogFileName=task10-reviewer.trx' --results-directory <SDD>/task-10-reviewer-results` — **13 passed, 0 failed, 0 skipped**, exit 0. Used the existing Release build with the process-only local PostgreSQL setting. TRX: `task-10-reviewer-results/task10-reviewer.trx`. This green result does not invalidate the P2 vacuous-assertion finding. |
| Controller exact-head focused Release TRX | Independently read `task-10-controller-results/task10-controller.trx`: **13 passed**, including both host-negative cases, runtime controls, empty data and trusted-setting cases. |
| Saved affected Debug log | `task-10-affected-debug-green.log`: **97 passed**, 0 failed, 0 skipped. This is the saved affected-suite run, not a new reviewer run. |
| Saved full Release log | `task-10-release-full.log`: **462 passed / 3 failed / 1 skipped**, 466 total. This was before the final test-only assertion/comment edits. It is not a fresh whole-suite run of the final head; changed classes were subsequently rerun. |
| Saved Release build log | `task-10-release-build.log`: **0 errors**, one recorded NU1510 warning. It is saved build evidence, not a fresh build by this reviewer. |
| Saved final focused Release log | `task-10-final-focused-release.log`: **13 passed** after the final test-only edits. |
| Earliest RED | `task-10-behavioral-red.md` is a contemporaneous transcript summary, not a preserved raw log. It records intended startup/runtime RED and inventory iterations; the reviewer did not reconstruct or rerun those earlier revisions. |
| Independent serializer reproduction | Same concrete tuple/list shape yields identical `[{}]` for different buckets/counts; saved at `task-10-reviewer-results/tuple-serialization-proof.txt`. |
| Git verification | Head remained `fa2d6b4164b22c737f6fa846905d38c0a4a33177`; product status was clean and base-to-head `git diff --check` exited 0. Git emitted an existing inaccessible global-ignore-file warning; it did not report product changes. |

The local test run supplied `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests` for that process only. The inspected fixture creates unique `owl_test_GUID` databases and disposes those exact databases. No live inventory, live database mutation or provider call was authorized or performed.

## Pre-existing/out-of-scope gates retained

The saved full Release log names the same three pre-existing ownership audit failures: `B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid`, `B2_CopiedReceiptCannotFirstClaimDesktopOwnershipWithoutPriorOwnerProof`, and `B2_CopiedReceiptCannotTransferExistingGuestPurchaseToUnrelatedDevice`. The B4 global guest contract remains skipped. This diff does not change those audit tests or the underlying ownership/claim services. They are not waived and are not reclassified as Task10-introduced findings.

Immutable anonymous ownership/recovery, shared quota and provider spend enforcement, numeric D2/monetary approvals, live inventory and reconciliation, backup/restore and old-schema startup migration validation, isolated deployed Sandbox, actual TestFlight/App Review routing and device acceptance all remain whole-feature gates. The final three-repository review is still required. Existing NU1510/NU1903 warnings remain recorded; this review did not broaden scope into unrelated dependency upgrades.

The backend manual CI workflow still publishes images and deploys on manual dispatch; source inspection confirms the runbook warning. No workflow was dispatched. The implementer's manual simplify record was read; no standalone simplify tool is claimed.
