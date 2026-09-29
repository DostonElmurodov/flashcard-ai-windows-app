# Task 2B.2 restoration fix round 3 — independent N1 review

**N1: ADDRESSED. Spec compliance: PASS. Task quality: PASS. Ready to accept this bounded fix chunk: Yes.** No new Critical, Important or Minor finding was identified in the two changed reconciliation call orders.

Reviewed `900c9a50799adb3d991fc97a4adf86045b8c13a9..3957f3e08c6317ff1495939e5c0cfbe7fea4f110` in the read-only backend checkout at `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`. Scope was the remaining Minor N1 and breakage introduced by this three-file diff. The previous fix2 review, frozen fix3 report/package, evidence manifest, contract sections 1–6, restoration brief and both carryover/preflight documents were read. R1–R3 retain their prior scoped acceptance; this does not repeat whole-feature review or authorize release.

## N1 disposition and source reasoning

For null stored token, immutable binding T1, verified signed T2 and same-identity canonical T1, the signed observation now precedes canonical metadata adoption:

- `src/Mavrylo.Services/Services/AnonymousPurchaseService.cs:208–211` records the signed token before canonical upsert in the shared verify/restore disagreement helper. Its transaction and purchase advisory lock remain at `:199–201`; same-identity checks remain before entry at `:66–70` and `:102–106`. The successful reconciliation path commits canonical lifecycle and quarantine together at `:216`, then returns 503 without reaching owner binding or mobile-grant writes.
- `src/Mavrylo.Services/Services/AccountEntitlementService.cs:77–81` makes the same reorder for first and same-account claim. User-before-purchase locking remains at `:61–67`; ownership checks precede observation, and conflict commits/returns at `:86–89` before `TryClaimAsync` and device mutation at `:93–95`. The persisted activity recheck remains intact.
- The unchanged `EntitlementService.cs:55–69` reloads under the purchase lock and saves the marker inside the existing transaction. Thus the subsequent PostgreSQL reload at `:103–108` sees it. `:73–87` compares against the immutable binding and retains the first conflict hash; `:133–135` preserves null metadata once conflict exists. Event ordering at `:115–141` still accepts newer revocation while preventing older evidence from undoing it. Neither reorder changes owner, binding, claim or device authority.

The normal matching-T1 path creates no conflict, so its permitted null-metadata adoption remains available. No helper, transaction, schema or configuration branch was added. Inspection found no newly reachable authority write or exception/rollback regression from the reordered observations.

## Independently checked evidence

All **14/14 manifest entries** matched their recorded byte lengths and SHA-256 hashes. The actual Git diff matched the full frozen package after line-ending normalization: exactly two service files and `tests/RestorationPostgresTests.cs`. `git diff --check` reported no errors. HEAD and its parent matched the stated final/base SHAs; the product checkout was clean.

I parsed individual results and failure details from every saved TRX and read the raw logs under `test-results/paid-access-hardening/task2b-restoration-fix3-results`:

| Saved evidence | Verified result |
| --- | --- |
| `n1-red` | 14 results: four behavioral failures, ten passing controls. Verify, restore, first claim and same-account claim each fail with **expected null, actual token**, at test lines 231/375 after the 503 assertion. These are not compilation or fixture-startup failures. |
| `n1-positive-baseline` | All four normal matching-token operations pass before the reported product reorder. |
| `n1-green` | All 18 cases pass, including the four N1 cases and all four adoption controls. |
| `fix3-covering-release` | 69/69 pass. Retained R1–R3 cases include account fallback and five actual local HTTP AI controls. Their assertions retain zero fake-provider calls on denial and one for healthy/independent-B success (`SharedSubscriptionPostgresTests.cs:422–426`). |
| `fix3-full-release` | 730 individual results: 729 Passed, zero Failed, one NotExecuted: existing B4 `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`. |
| `fix3-release-build.log` | Build succeeded, zero errors, four inherited NU1510/NU1903 warnings. |

`RestorationPostgresTests.cs:186–260,329–405,413–465` uses Npgsql and separate processing/check/retry contexts. Assertions cover preserved null/token metadata, initial conflict digest, newer canonical revocation/event, no new mobile grant, unchanged claim/device fields and bound token, missing/former-token denial, and successful matching-token adoption. Claim retries explicitly retain the first hash; mobile retries verify durable quarantine, preserved metadata and retained newer event. The unchanged observer supplies first-hash immutability on both paths. Source inspection supplies the additional unchanged owner/binding/grant-write boundary; no universal concurrency claim is made.

## Limits and out-of-scope observations

These are independently inspected saved runs, not reviewer reruns. No suite, build, database probe, network or provider operation was run. No new Debug result is claimed: the earlier 7/7 belongs to source `900c9a5`. Only this report was written; source, Git/index/HEAD and saved evidence were preserved.

B4/Task6A, shared and aggregate quotas, Retry-After and unapproved D2 amounts remain open. Historical paid/account-marker fallback still requires local Task3B/Task10/final disposition; production inventory cannot replace it. Physical Apple compatibility, production configuration, exceptional desktop recovery, operational reconciliation and separate whole-feature review remain gates. Ordinary mobile purchase/restore continues to require neither Owl login nor an old key. The accepted complete paired-envelope residual conveys no owner or private-account authority. Existing warning debt and all other routed carryovers remain unchanged; none blocks acceptance of this narrowly corrected N1.
