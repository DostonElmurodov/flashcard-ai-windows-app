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
