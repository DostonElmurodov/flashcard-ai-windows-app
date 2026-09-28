# Task10 fix round 1 — independent scoped re-review

Review date: 2026-09-27 (America/Chicago).

Backend checkout: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`.
Branch: `codex/paid-access-hardening`.
Fix base: `fa2d6b4164b22c737f6fa846905d38c0a4a33177`.
Fix head: `77e67f4886586e9e60c680717d696f3e09c11ab3`.

## Verdicts

**Scoped specification: approved for this fix.** The original P2 is addressed, and the optional P3 clarification is addressed. There are no unaddressed findings from the original scoped review and no new actionable breakage identified in this two-file fix. This does not approve the whole paid-access feature, deployment, or Task10's deferred live work.

**Code quality: approved for this fix.** The change is small and directly fixes the defective evidence: one shared property-bearing projection, two positive assertions, and the requested policy clarification. No production behavior changes. No further correction is requested in this scope.

## Findings disposition

### P2 — Addressed: repeatability and safety inspect populated report data

At `tests/SubscriptionInventorySqlTests.cs:167-168`, `SerializeReport` projects every report row into an anonymous object with public `Bucket` and `Counts` properties before serialization. It does not filter rows, discard dictionary entries, or replace values with constants. Unlike the former default serialization of value tuples, this representation includes the actual bucket string and every count key/long value returned by `Report`.

At lines 24-30, both executions use that same helper and the complete serialized strings are compared. A change in any bucket, count key, or count value changes the compared representation. The SQL explicitly orders rows by `environment_bucket`, and `Report` constructs each dictionary from the same ordered result columns, so using the serialized representation here does not introduce an unordered-comparison issue for this query.

The positive assertions require `"Bucket":"TOTAL"` and `"rows":15`; the preserved direct numeric assertion also requires the actual TOTAL `rows` value to equal 15. Thus the original empty-object representation cannot satisfy the test. The three identifier checks examine the same populated representation, and equality applies that checked representation to the second execution as well.

The safety checks remain specific to the three seeded sentinel identifiers (`id-a`, `owner-a`, `shared-ref`); they are not a universal proof against every conceivable disclosure. That is an existing coverage boundary, not a new defect or a reason to reopen the broader inventory design. Inspection of the unchanged actual SQL confirms its output is fixed bucket labels and numeric aggregates.

### P3 — Addressed: global saved-library scope and decision provenance

At `docs/paid-access-policy.md:27-33`, the count and first-ten order now explicitly apply once across the saved library, including all collections. The paragraph still attributes the ten-card content boundary to the scoped Tasks8/9 client implementation ruling and explicitly identifies D1=B and D3 as user decisions. The decision ledger and matrix retain D2 as pending for numeric AI limits, global guest limits and monetary budgets; the clarification grants no new quota or budget approval. Pending ownership, quota, spend and anonymous-recovery enforcement remain explicitly qualified.

## Evidence inspected and exact limits

Read the original `task-10-review.md`, the fix-round addendum in `task-10-report.md`, the full two-file base-to-head diff, the complete changed inventory test and policy file, the unchanged actual SQL, the test project configuration and the local PostgreSQL fixture.

| Evidence | Observation and limit |
|---|---|
| Fresh Git/source inspection | Branch and exact head above verified. The fix changes only `tests/SubscriptionInventorySqlTests.cs` and `docs/paid-access-policy.md`: 11 insertions, 4 deletions. Product status is clean. Base-to-head `git diff --check` exits 0. The existing inaccessible global-ignore warning does not report a source change. |
| Saved `task-10-fix1-red.log` | Independently read raw output: **8 passed, 1 failed**, 9 total. The failing positive bucket assertion shows `[{},{},{},{},{}]`, which directly demonstrates the original vacuous serialization problem. This is preserved implementer evidence, not a reviewer rerun. |
| Saved `task-10-fix1-green.log` | Independently read raw focused Release output: **13 passed**, including the corrected inventory aggregate test, invalid/empty settings cases, and TestMode host-boundary cases. The log records a successful run and no failed or skipped cases. This is preserved implementer evidence, not a fresh test run by this reviewer. The fix addendum associates that run with the corrected source; the raw console log itself does not embed a Git SHA. |
| Earlier broad evidence | The earlier **97/97** affected run and full Release **462 passed / 3 B1-B2 failures / 1 B4 skip** retain the earlier scope/timing described by the original report and review. They were not rerun for this fix and are not fresh evidence at the fix head. |
| Build/dependency warnings | The fix logs retain NU1510 and NU1903 warnings. This review does not reclassify those existing warnings or claim a dependency remediation. |

No fresh test or build was run: the authorized read-only source/log review is sufficient for this bounded test-and-wording correction. No production/test source was edited by the reviewer; the only newly written artifact is this report. No subagent, live database, provider, Apple, email, remote workflow, push or deployment was used.

## Unresolved gates retained

The existing B1/B2 ownership failures and B4 skip are not waived. Immutable anonymous ownership, legitimate-buyer recovery/reconciliation, shared quotas and provider spend enforcement, D2 numeric/budget approvals, live inventory, backup/restore, old-schema startup migration validation, isolated deployed Sandbox, TestFlight/App Review routing, physical-device acceptance, the final three-repository review and release/deployment authorization remain outside this fix and unresolved. Acceptance of this correction changes none of those gates.
