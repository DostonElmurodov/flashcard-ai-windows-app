# Task 3B — actual Mac evidence

## Initial missing-feature RED

- Run: https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36517924836
- Exact source: `54fa4bb090dc703186ad0b5fab7b2a1999fde101`; remote head, run metadata and downloaded `source-head.txt` all matched.
- Manual `ios-validation.yml`, scope `restore-proof`, selecting exactly `AnonymousPurchaseRestoreTests` and `AppAttestSessionResumeTests`. Test-only; no signing, publishing or deployment.
- Environment: Xcode 26.6 (17F113), iPhone 17 Pro simulator, iOS 26.4.1. This does not establish physical App Attest/StoreKit or iOS 17 runtime compatibility.
- Build succeeded. **14 cases executed: 13 failed, 1 passed, 0 skipped.** XCTest reports 23 assertion failures, including five unexpected thrown exceptions. Do not confuse assertion count with failed case count. Test execution 1.160 seconds, suite elapsed 1.323 seconds.
- New API/evidence methods were explicit missing-feature stubs in this checkpoint; their invalidURL/false outcomes are intended RED, not regressions of an already implemented feature. Existing entitlement behavior also reproduced lost paid history/AI fallback after a free response, lost mobile_restore rights after logout, missing-JWT failure and absent fresh-proof retry.
- The sole passing case was `testBackendOutageLeavesTransactionUnfinishedAndKnownPaidHistoryReadable`. Its discovery method was still a no-op, so this initial pass **does not prove actual backend-outage processing**. Later GREEN must exercise the real shared processor.

Root downloaded complete metadata, job log and workflow artifacts before resuming the implementer. Local evidence folder:

`D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task3b-results/36517924836-restore-proof-red-54fa4bb`

It contains `initial-run.json`, final `run.json`, `watch.log`, `full.log`, downloaded `artifacts/ios-validation` including source head, Xcode log and XCResult, parsed `cases.json`, and `evidence-hashes.json` for all saved files. The watch ended with status 1 because the expected RED tests failed; no watch/download process remains active.

The same Task3B implementer was resumed after this actual RED for the approved feature and the remaining brief matrix. Additional missing coverage, final GREEN, full regression, unsigned Release and independent scoped review remain required. This index is not feature acceptance.

## Second ordering/key-lifecycle RED checkpoint

- Run: https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36519160205
- Exact source: `3a9aab9e215763071da605076f808794e670df7f`; normal feature-branch push, run metadata and downloaded artifact source all verified.
- Same manual `restore-proof` scope and two classes. Build succeeded; **21 cases executed: 7 passed, 14 failed, 0 skipped**. XCTest reports 33 assertion failures, one unexpected thrown error; 7.528 test seconds and 8.826 suite seconds. StoreKit/coordinator stubs still account for absent behavior; the one recoveryCount expectation is explicitly a test defect, not a product regression.
- Includes initial RED-covered API and entitlement behavior changes, plus new neutral ordering/key-lifecycle seams and tests. StoreKit processor and coordinator resume remain explicit stubs at this source.
- Two expectation corrections were identified before implementation: rejected proof retries fresh assertion without calling device-session recovery; optional Owl lookup failure must not gate purchase/restore, while healthy optional account on verify/restore retains server fallback behavior. Fixed-source run is preserved; those test corrections must not be called product fixes.
- Local folder: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task3b-results/36519160205-restore-proof-red2-3a9aab9`.
- Complete metadata/logs/artifacts/XCResult and parsed case results preserved; 1,514 evidence file hashes recorded. Watch session 4847 completed with expected status 1. Root inspected actual assertion output, then resumed the same implementer. No active Mac run/watch/download remains.

## First full lifecycle checkpoint: compiler failure, not behavioral RED

- Source `63a4716ae36e3867dbb660efe2a0f723d2f3d73c`, verified in remote branch, both run metadata and downloaded source artifacts.
- `restore-proof`: https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36520917034
- `paid-access` (includes `SharedAccountSessionTests`): https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36520919418
- Both failed compilation at `AccountSessionClient.swift:322`: `claim` in `send()` resolves to the method rather than the Boolean argument of a different function. **Zero test cases executed.** These runs provide compiler/setup evidence only; the intentionally pending entitlement-selection regression has not yet produced behavioral RED.
- Corresponding local folders end in `36520917034-restore-proof-checkpoint-63a4716` and `36520919418-paid-access-checkpoint-63a4716`; each includes complete downloaded logs/artifacts and 24 evidence file hashes. Watch sessions 55553/8635 completed with status 1.
- Root read both compiler diagnostics and the relevant method scope, then requested a compile-only repair before another Mac run. The unrelated-account/mobile precedence defect must remain unfixed until its actual test result is observed.

## Actual lifecycle/precedence checkpoint after compile repair

- Source `bbdbaa25cc28f373e8229813a24160018b687ecb`; one-line compile correction only. Normal remote head, run metadata and artifact source all matched.
- `restore-proof`: https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36521130121 — build succeeded, **31 executed: 30 passed, 1 failed**. All seven App Attest resume tests pass. The one failed purchase test observes `account`/`account-purchase` instead of the active independent `mobile_restore`/monthly purchase. This is the actual behavioral RED for the newly identified precedence defect.
- `paid-access`: https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36521131761 — build succeeded, **119 executed: 109 passed, 10 failed**. Nine failing `EntitlementMutationBoundaryTests` and one new desktop-denial preservation case. All 28 `ReviewSessionViewModelTests` pass. Eleven assertion failures include seven unexpected throws; distinguish assertion failures from failed test cases.
- Complete per-run metadata/logs/source/XCResult/cases preserved in `36521130121-restore-proof-checkpoint-bbdbaa2` (1,530 file hashes) and `36521131761-paid-access-checkpoint-bbdbaa2` (1,708 file hashes). Both watches (7304, 4411) completed with status 1.
- Root read actual failures and traced the existing mutation fixture's shared-store `.update(.free)` reset. The implementer must diagnose potential retained-history test-state leakage separately from product defects, preserve all true-free and migration/paid-history assertions, and make no production reset shortcut. Selection repair and diagnosed fixture/regression corrections are now authorized after actual evidence; no task acceptance yet.

## Corrected candidate blocked before execution by GitHub

- Clean local and remote source: `544c16e1173c3fe2b219be050bd6416d3e2b89b2`, verified before normal private push. Includes authority selection, explicit server-proven owner retention, and a DEBUG/hosted-XCTest-only fixture reset/restore seam. Final source has not compiled or run on Mac.
- `restore-proof`: https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36521873497
- `paid-access`: https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36521875224
- Both run metadata records name the exact candidate SHA, but GitHub refused both jobs with **zero steps and zero artifacts**. Its annotation states: `The job was not started because recent account payments have failed or your spending limit needs to be increased. Please check the 'Billing & plans' section in your settings`.
- This is an external execution block, not a compile failure or a RED/GREEN test result. There is no `source-head.txt` artifact because checkout never started. No billing setting or spending limit was changed; root asked the user how to restore test execution.
- Metadata, annotation text, empty artifact listings, watch output and five file hashes per run are preserved in `36521873497-restore-proof-green-candidate-544c16e` and `36521875224-paid-access-green-candidate-544c16e` under the same results root.
- Product source is frozen. Independent source review can proceed using the exact diff and prior evidence; actual focused GREEN, full suite and unsigned Release (including test-seam exclusion) remain mandatory acceptance gates.
