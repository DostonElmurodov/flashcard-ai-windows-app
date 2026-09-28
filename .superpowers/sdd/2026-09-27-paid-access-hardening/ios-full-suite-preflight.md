# iOS full-suite diagnostic notes

Observed unrestricted run36354318795 tested c4efebcf9157cd03746d316c92284dabfbc0f185 on Xcode26.6 (17F113), iPhone17Pro simulator iOS26.4.1. Actual summary595 tests:558pass33fail4skip,57 assertion failures. Logs are in task8-results/ci-full-red6-*.

One passing terminal message for WordRepositoryPublicSetTests.testDeleteWaitsForOlderUpsertWhenSameClientIdentityChanges is split by SQLite logging across full-log lines6792–6793. The parsed cases artifact explicitly annotates this repair; do not mislabel it an interrupted/crashed test.

## Hosted UI harness

AccountViewTests already skips four cases at line208 when in-process SwiftUI accessibility containers are unavailable, with an explicit request for external UI automation. Other hosted Library/Root/Paywall/Badge tests fail while traversing the same empty NSObject accessibility tree. The full log contains empty labels and missing-element unwraps, not evidence that all those app controls disappeared. Fixed-source baseline run36355230240 now proves these failures preexist this feature: actual checkout9096c1e3b20fb920726bd471ed4a092783ee3cc7 (workflow head0279384), same Xcode26.6 (17F113)/iPhone17Pro/iOS26.4.1, successful build and545 cases=517passed24failed4skipped (34 assertions,4 unexpected). All24 failed cases also occurred in c4efebc. Exact failure groups: DashboardAddMenu1, LibraryViewHosted2, ProductOnboarding3, ProductPaywall1, PublicFlashcardSetAPIClient4, RootTabShell12, SetGridTileBadge1. This establishes baseline attribution; it does not by itself prove the root cause or waive the whole-feature full-suite gate.

Apple's [XCTest documentation](https://developer.apple.com/documentation/xctest) describes UI flow testing with XCUIAutomation. [UIHostingController](https://developer.apple.com/documentation/swiftui/uihostingcontroller) integrates SwiftUI into UIKit; this is not a documented guarantee that every internal SwiftUI accessibility node can be enumerated by hosted unit tests. Do not fabricate accessibility objects, replace UI assertions with unconditional success, or add blanket skips. External UI automation may be needed for these scenarios; physical/device evidence remains separate.

The official [macOS15 arm64 image inventory](https://github.com/actions/runner-images/blob/main/images/macos/macos-15-arm64-Readme.md) lists Xcode16.4/iOS18.5 as another available toolchain at the inspected snapshot. No alternate-runner test has been performed, and choosing an older runner is not evidence of current-runtime UI compatibility.

## Other failures and isolation

- Four PublicFlashcardSetAPIClientTests fail with credentialsUnavailable/InvalidTransition. Inspect injected account/device/session/integrity dependencies; do not weaken production authentication to satisfy old fakes.
- WordRepositoryV10OwnershipTests.testPremiumImportAtExactFreeLimitAllowsNewIdentity needs a real accepted finite-expiry/fresh local premium fixture under the changed policy, not status-only authority.
- Existing PublicSet/V10 fixtures unlink SQLite databases while queued fake sync still owns them. Fix fixture lifetime/draining instead of hiding SQLite errors or changing real repository persistence to suit cleanup.
- FlashCardAIApp later suppresses feature-flag/sync tasks in XCTest, but its default AppRootModel constructor immediately starts external services with externalRefreshEnabled=true. AppComposition.make defaults to APIClient and live StoreKit transaction updates. Verify earlier startup and fixture dependencies are also isolated; a later task guard alone does not prove zero live traffic. Any test-only suppression must preserve real app and Release authorization behavior.

Controller requested a fixed-source baseline-full workflow choice, preserving default unrestricted current-head full testing. The workflow must report actual checked-out source SHA separately from GitHub's workflow headSha; no arbitrary source input, deployment, new skips or baseline source fixes.

Baseline evidence is saved as task8-results/ci-baseline-full-run.json, ci-baseline-full-full.log and ci-baseline-full-cases.json. Four API fixture corrections are local and await current Mac results. The20 unrelated hosted failures and4 existing AccountView skips require a proper harness/automation resolution in Task11; no new skips or fabricated accessibility objects are accepted. Task8 paid-access behavior still requires its own passing cases and independent review.
