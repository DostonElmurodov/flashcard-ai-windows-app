# Task 6B scoped review fix round 1 — implementation and evidence

Status: backend fix verified locally; iOS source and tests are unexecuted pending Mac availability. This is not Task 6B acceptance or release readiness. Backend HEAD remains `3957f3e08c6317ff1495939e5c0cfbe7fea4f110` with prior accepted 10A/6A and Task 6B dirty source preserved. iOS HEAD remains `d66f73de2f3fca8fe86f95a970d2df1e3626d13e`; no commit, push, live Apple/provider call, production database, or network CI was used.

## Five reviewed defects

1. **Repeated explicit AI ID:** a new free AI reservation now stores a validated explicit `ClientWordId`. A repeated request reuses the same exact row, and a later save fills it rather than meeting duplicate null-ID placeholders. An existing exact ID with different word/languages returns stable `409 word_identity_conflict` before provider/quota, without changing that row. Null-ID legacy reservation behavior remains. Service RED reproduced two placeholders, semantic reuse, and blank explicit AI IDs; focused GREEN covers repeat, save, conflicting word/language, and invalid ID. Actual HTTP conflict control checks zero provider calls and zero quota rows.
2. **Incomplete AI order:** both primary word-detail and secondary review return `409 {code:"word_order_reconciliation_required",error:...}` before quota/provider when a true-free library above ten has incomplete exact order. Actual HTTP RED returned 402 on both routes; GREEN checks code, zero provider calls, no quota reservation, and retained rows. Existing genuine out-of-ten payment denial remains 402.
3. **Blank delete ID:** any non-null delete ID receives the same validation as upsert; `""` and whitespace can no longer fall into semantic lookup. Service RED deleted the saved row; GREEN retains it. Null legacy semantic deletion and exact nonblank deletion remain covered by existing tests.
4. **iOS transient eligibility:** queued free save no longer persists `access_denial` merely because an order response excludes its ID. It rechecks the saved snapshot and current access after awaited reconciliation; expired/inactive access cannot launch a new save. A later eligible retry can save after an older card is removed. A true server `accepted:false` still records a denial under current active access, and actual HTTP 402 still throws before that branch. Source-only regressions cover recovery after deletion and expiry during reconciliation. Mac compilation/execution is not claimed.
5. **iOS byte-exact locks:** `ExactWordIDSet` stores UTF-8 bytes, and WordRepository plus ReviewCardRepository use it for all review-lock membership checks. The source-only regression puts decomposed/composed IDs at tenth/eleventh positions and checks distinct lock results on dashboard rows. No local cards or review state are deleted. Mac compilation/execution is not claimed.

## Verification

All PostgreSQL tests used process-only `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`; the fixture created and dropped per-fixture `owl_test_*` databases.

| Evidence | Result |
| --- | --- |
| `backend-fix1-red.log` | Service behavioral RED for explicit reservation, semantic conflict, blank AI/delete IDs. Two HTTP tests in this first run did not execute because the initial connection used the wrong test username; those errors are environmental, not behavioral RED. |
| `backend-fix1-http-red.log` | Actual HTTP RED: both incomplete-order AI routes returned 402 instead of 409. |
| `backend-fix1-focused-green-1.log` | New focused cases 10/10 pass. |
| `backend-fix1-affected-focused.log` | 134/135; existing fixture queried `card-11` across all libraries and collided with the new test row. The assertion was scoped to its own device library. |
| `backend-fix1-affected-final.log` | Affected DeviceWordService, PaidAccessBoundaryHttp, and ReviewTranslation tests 135/135 pass. |
| `backend-fix1-full-release.log` | 804/807: two TestModeRoute expectations still asserted 402 after test mode created an incomplete >10 free library; one account word-detail happy path returned 503. |
| `backend-fix1-account-503-diagnostic.log` | Same spend route matrix 10/10 passes in isolation. |
| `backend-fix1-testmode-account-focused.log` | Corrected TestMode expected 409/code only after >10 incomplete order; original cap and expired-paid 402 controls remain. Focused 20/20 pass. |
| `backend-fix1-full-release-final.log` | 806/807; account word-detail 503 recurred with `{"error":"ai_translation_unavailable"}`. |
| `backend-fix1-spend-class-final.log` | After normal-route fixture deadline adjustment, entire spend class including explicit timeout tests 24/24 passes. |
| `backend-fix1-full-release-final2.log` | Final full Release 807/807 pass, zero skipped. |
| `backend-fix1-build-release.log` | Release build succeeds with 0 errors and inherited NU1510 warning. |
| `backend-fix1-ef-model.log` | Pinned EF 10.0.9 reports no pending model changes. |

The recurring account-route 503 is not attributed to a proven exception reason. Source shows the earlier happy-path fixture inherited a 1,000 ms provider deadline that includes PostgreSQL spend reservation; under the parallel full suite the case took 9–16 seconds, while the isolated route matrix passed. The narrow test Factory now uses a synthetic 10,000 ms deadline for ordinary fake-provider cases. Dedicated 500 ms and 5 s timeout cases retain their explicit settings and passed in the 24-case class run. Failure-body and provider-request-count diagnostics remain in the positive assertion. This is a fixture-stability inference, not a production timeout change.

After each coding iteration the touched diff was reviewed for scope and redundant behavior. The backend correction uses existing service validation and one filter mapping; it adds no schema or quota changes. The iOS change uses one byte-exact membership type and keeps transient order exclusion separate from server rejection. `git diff --check` has no whitespace errors. All 44 earlier frozen backend source paths remain present; 39 hashes still match and the other five are the reviewed fix-round files. Earlier raw logs remain intact. `test-results/paid-access-hardening/task6b-results/fix1-review-freeze/` holds exact source copies, statuses, hashes, and incremental review diffs; all 10 copied source hashes match the live files. Fresh independent review remains required; current iOS source has no Mac GREEN or Release compile evidence because GitHub billing stopped the last attempted run before any step.
