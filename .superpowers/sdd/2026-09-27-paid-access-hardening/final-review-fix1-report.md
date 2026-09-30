# Final review fix 1 — iOS historical StoreKit selection

Date: 2026-09-29. Actual checkout: `D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\ios` at `d66f73de2f3fca8fe86f95a970d2df1e3626d13e` plus the existing four accepted6B dirty files and this two-file increment. No commit, push, workflow, live Apple request, provider call, or production access was made.

## Finding and change

The whole-feature reviewer found one actionable P2 issue: `latestHistoricalEvidence` kept the first `originalID` found in configured product order. An older monthly transaction could suppress a newer yearly transaction in the same chain when current entitlements and unfinished transactions were empty. The backend's canonical product check would reject the monthly proof; `StoreKitService` would retain that first failure and miss expired-paid history.

`LiveStoreKitPurchaseEvidenceSource` now gathers each configured product's locally verified `Transaction.latest(for:)` candidate, compares `purchaseDate` across each original chain, and emits only the selected proof for that chain. Equal purchase dates prefer a transaction that is not marked upgraded, then use numeric transaction-ID ordering for a stable tie break. It retains revoked candidates for backend validation. The selected JWS is still paired with locally verified `AppTransaction` JWS and the device verification ID; only the selected real StoreKit transaction is cached for `finish()` after backend acknowledgement. Current-entitlement and unfinished paths, backend product matching, and unrelated error reporting were not changed.

The `latestCandidate` closure is the narrow test seam around Apple's per-product lookup. Its production default accepts only `.verified` StoreKit results and carries the signed JWS and transaction to the existing actor. Tests replace that external lookup while executing the production historical selector and evidence construction.

Apple documentation describes [`Transaction.latest(for:)`](https://developer.apple.com/documentation/storekit/transaction/latest%28for%3A%29) as the most recent transaction **for one product**, [`purchaseDate`](https://developer.apple.com/documentation/storekit/transaction/purchasedate) as purchase or post-lapse renewal time, and [`isUpgraded`](https://developer.apple.com/documentation/storekit/transaction/isupgraded) as indicating a transaction superseded by an upgrade. The tie break is deterministic but has not been validated against a physical StoreKit same-timestamp upgrade.

## New test inventory

- `testHistoricalUpgradeSelectsNewestProofForEitherProductOrderAndRestoresExpiredReads`: calls the live source selector with monthly/yearly and yearly/monthly order, then routes selected evidence through `StoreKitService.restore()` with empty current and unfinished arrays. It expects exactly one canonical yearly backend request, paired app/device proof, expired-paid history, no AI access, successful explicit restore, and no false restore error. The fake backend rejects an obsolete monthly JWS with `apple_identity_mismatch`, so selecting it would fail the test. The test does not directly exercise a review-screen read.
- `testHistoricalSelectorKeepsNewestRevokedProofAndSeparateChains`: expects a newer revoked yearly transaction to win over an upgraded monthly transaction, sends that canonical proof through explicit restore and checks revoked paid history, then checks that two distinct originals remain independently available.
- Existing `testCurrentGrantDoesNotHideLaterSameOriginalReconciliationFailure` continues to cover visibility of an unrelated reconciliation failure; it was not edited.

The two new tests were written before production code. They were **not executed**: this Windows host has no `swift`, `swiftc`, or `xcodebuild`, and the current remote Mac job is blocked by GitHub billing. No RED, GREEN, compiler, suite, or Release result is claimed. Root owns one final Mac full unit/UI and unsigned Release run after billing is resolved. Physical StoreKit and signed-device validation remain separate gates.

## Frozen evidence and scope

`test-results\paid-access-hardening\final-review-fix1\input-ios-source` and `final-ios-source` contain all 51 feature-source paths. Their SHA-256 manifests are `input-ios-source-sha256.json` and `final-ios-source-sha256.json`; final copied files were rehashed with 51 matches and zero mismatches. `incremental-source-sha256.json` identifies exactly two changed paths. `incremental-ios-fix.diff` contains only this test and source change. `input-ios-existing-dirty.diff`, `input-ios-status.txt`, `input-ios-head.txt`, and `final-ios-status.txt` preserve the incoming four dirty paths and final state. Those four files' hashes are unchanged.

`git diff --check` exited 0. It emitted Git's Windows LF-to-CRLF warnings but no whitespace errors. No Swift syntax or runtime result can be inferred from this check. The current candidate needs fresh independent source review before the final Mac gate.
