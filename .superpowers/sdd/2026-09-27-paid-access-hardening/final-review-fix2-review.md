# Final review fix2 — bounded independent source review

Date: 2026-09-29. Scope: the test-only increment in `test-results/paid-access-hardening/final-review-fix2/incremental-test-only.diff`, reviewed against the P2 in `final-review-fix1-review.md` and the implementer report. Live source reviewed in `test-results/paid-access-hardening/ios`. This was not a repeat of the whole-feature audit.

## Verdict and P2 disposition

**Source accepted for this increment; the fix1 P2 test-contract finding is resolved. No actionable findings in the bounded correction.** This is source acceptance only, not compilation, runtime, release or deployment acceptance. The prior production-selector source acceptance remains unchanged.

- `FlashCardAITests/AnonymousPurchaseRestoreTests.swift:604–611` now expects false from explicit `restore()` for acknowledged `expired_paid` history, consistent with `Infrastructure/Purchases/StoreKitService.swift:498–506`, whose successful active statuses are `premium`, `trial` and `grace`. Both product enumeration orders still run. The test retains canonical yearly JWS submission, selected transaction finish dispatch, applied expired status, paid history, no AI access and nil error assertions. Its request checks also retain paired app-transaction and device evidence.
- `AnonymousPurchaseRestoreTests.swift:645–652` likewise expects false for revoked history and retains canonical JWS, revoked status, paid history and no AI access assertions. The newly added canonical finished-ID and nil error assertions prevent the false-return assertion alone from masking a reconciliation failure. These fake-source assertions establish the intended service dispatch contract at source level; they do not demonstrate real StoreKit transaction completion.
- `AnonymousPurchaseRestoreTests.swift:670–702` exercises the production `latestHistoricalEvidence` selector, injecting only candidate lookup and external app/device evidence. Each tie scenario runs both product orders. Equal purchase dates with differing upgrade flags choose the non-upgraded ID `100` over upgraded ID `900`; equal dates and equal upgrade flags choose numeric ID `10` over `9`. These cases exercise both tie branches at `StoreKitPurchaseEvidenceSource.swift:212–220`, including the distinction between numeric and lexical ordering. Expected arrays also require exactly one selected transaction per scenario.
- The implementer report now accurately distinguishes acknowledged inactive paid history from the active-access Boolean return. No production contract was broadened.

## Provenance and limits

Independently read the frozen increment and manifest and verified the live test SHA-256 as `814277F9E8F02852C99DD177AB4ADEB7EA274E3169F52186BFEF3235E240A7F2` and live production selector SHA-256 as `E7B022425064D6EC54B7035E667D5E6DB84ED6CE0A56909D0A81A95EA5BF7023`, matching the manifest. The increment changes only the test file. Root supplied verification of all 51 live paths against the prior freeze plus increment and both input/final copies; this bounded review did not repeat that complete provenance audit.

No source changes, child agents, compiler, test/build execution, remote operations, commits or pushes were performed. No RED/GREEN result or runtime acceptance is claimed. The exact final candidate still requires the Mac full unit/UI suite and unsigned Release build; the reported billing block supplies no validation of this candidate.

Physical signed-device/App Attest/Apple validation remains open, including iOS 17, reinstall/new-phone restore, upgrade/expiry/revocation, family/lost-credential scenarios and same-timestamp StoreKit behavior. Synthetic equal-date selector coverage does not close those physical gates. Previously documented production inventory, backup/recovery/cutover, quotas/budgets and deployed-source/configuration gates are unchanged. This report grants no release or deployment approval.
