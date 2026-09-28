# Task 8 independent review

**Spec compliance: FAIL — five actionable behavior gaps.**

**Task quality: NEEDS FIXES.** No Critical findings; three P1 and two P2 findings below. This is the task-scoped review of base `9096c1e3b20fb920726bd471ed4a092783ee3cc7` through head `4bb69cb20a82a14a4c0510bbefbadf5308f4bfae`, not final feature or release acceptance.

All source paths below are relative to `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios`. The checked-out HEAD matched the requested SHA and the checkout was clean at inspection. Review used the supplied requirements/checklist, report, complete diff package and saved Mac artifacts. No product, index, branch or workflow mutation, subagent, network call or test rerun was performed. This report is the only written file.

## Strengths

- `Infrastructure/Purchases/EntitlementStore.swift:4–21,208` publishes the entitlement body and ephemeral confirmation together under a lock; repository guards consume that tuple, rather than mixing a persisted body with a new receipt. Freshness is strictly less than five minutes and does not survive store reconstruction.
- `Infrastructure/Repositories/WordRepository.swift:390,946,2690,2763,2851,3499–3525` applies global count and edit checks in the actual write transactions for Anki, staged Save, manual, imported and catalog additions. The existing language statistics query is preserved. The new contention test exercises two writers competing for the last slot.
- `Infrastructure/Repositories/WordRepository.swift:2902–2910,3690–3730` retries the saved snapshot and preserves original detail JSON; denial metadata reaches dashboard rows and review locks. `ReviewCardRepository.swift:464–481` rechecks eligibility inside grade persistence.
- `EntitlementStore.swift:139–174` keeps account read history by account ID, without restoring confirmation on account selection. `StoreKitService.swift:57–79` confines hosted-test transaction suppression to DEBUG; release defaults remain live. The new test file has explicit PBX Sources membership (`FlashCardAI.xcodeproj/project.pbxproj:10,980`).

## Important findings

### R1 — P1: An older account refresh can reopen access after a newer denial

**Location:** `Presentation/Features/Settings/ViewModels/AccountProfileModel.swift:37–41` (also its error application at 43–48).

Start an account entitlement refresh while the account has confirmed premium. Before that delayed response returns, a direct AI request receives an accepted 402 or structured reconciliation 503. `APIClient` closes confirmation and advances `EntitlementStore.authorityRevision`. The old account response can then return premium: the guards here only compare the model's own `revision`, `refreshRevision` and account identity, none of which the AI denial changes. `setAccountEntitlement` stamps a fresh receipt and reopens paid SQLite/AI mutations for five minutes. Similarly, a stale account error can clear confirmation granted by another newer source.

This violates the required shared ordering across account/device/JWS/AI sources. Make account success and failure application participate in the same accepted-authority ordering before changing the model, stored history or confirmation. Add a deferred account-premium reply released after an actual fake AI denial, plus the converse stale-error/newer-success control. Existing account-order tests compare account refreshes with each other; the passing cross-source test only delays IAP.

**Evidence bound:** Direct source trace; not a new executed Swift regression.

### R2 — P1: A newer AI denial is discarded when an older IAP success finishes first

**Location:** `Infrastructure/Networking/APIClient.swift:408–414`; request capture at 427–429 and multipart capture at 123–125.

Start IAP refresh A, then AI request B while both observe authority revision R. Release A's premium response first, after B has already started: `StoreKitService.swift:205–208` accepts A and `EntitlementStore.update` changes R. When newer B returns 402 or reconciliation 503, the equality check at line 413 silently ignores that denial. The request fails visibly, but the newly stamped premium receipt remains usable for local additions/edits and further AI attempts.

The local StoreKit `refreshRevision` orders IAP calls only; it does not register the newer AI request. A revision changed by an earlier request's completion is not proof that B is obsolete. Use request ordering shared across the relevant sources, preserving identity boundaries and protection against genuinely older denials. Cover both completion orders with the existing deferred IAP/AI transport. The current test releases B's denial first and therefore does not detect this order.

**Evidence bound:** Deterministic source interleaving; no runtime execution claimed.

### R3 — P1: Current IAP verification denials do not close paid confirmation

**Location:** `Infrastructure/Purchases/StoreKitService.swift:210–215,220–233`.

With a still-fresh premium receipt, let the current `/owlai/iap/verify` return 402, structured 403 `account_required`, or 503 `subscription_reconciliation_required`. `verifyWithBackend` throws without closing confirmation; purchase/restore/update callers only display the error. `APIClient.accessFailure` handles expensive AI paths exclusively (`APIClient.swift:392–393,411`), so it does not close these IAP denials elsewhere. The token-refresh catch also misses reconciliation 503 because it only matches numeric 402/403. Consequently a server verification/reconciliation refusal leaves the old local paid mutation grant active until its existing five-minute deadline.

Apply the existing structured `APIError.isPaidAccessDenial` contract to current verification/token errors before propagating them, guarded by request and identity ordering. Preserve 429 and ordinary transient 503 behavior. Tests should cover fresh premium followed by fake verify denial and token reconciliation 503, with transient controls and an older-error/newer-success control.

**Evidence bound:** Traced both IAP endpoint callers and the API error handler; current passing tests exercise AI reconciliation and token 403, not these missing paths.

### R4 — P2: Saved single-translation content is treated as an uncached AI request

**Location:** `Infrastructure/Repositories/WordRepository.swift:607–615,629–631`.

Save a manual/imported word containing one nonempty translation, then let paid access expire or reopen offline without live confirmation. A normal `wordDetail` request without a secondary language rejects that stored detail as a cache hit because `hasMultipleTranslations` requires at least two entries (`4327–4333`). The new AI guard then throws, even though an eligible saved translation is already available and must remain readable without provider work. The same problem affects eligible first-ten free content at the global cap. Current retained-cache positives explicitly seed two translations and bypass this branch.

Separate availability of saved readable detail from optional enrichment. Return eligible saved content when it already satisfies reading, without requiring an AI grant; check access only for actual additional generation. Add a one-translation retained-cache control with zero requests under expired paid, stale confirmation and capped free access. The multi-translation rule predates this task, but leaving it in the modified retained-content path does not satisfy the task's explicit cached-content requirement.

**Evidence bound:** Inspected the cache branch because the diff cut off its predicate; verified the helper and manual/import shape. This concerns the repository/search-detail path, not a claim that every dashboard preview is broken.

### R5 — P2: Locked cards lose their Delete action in the changed screens

**Location:** `Presentation/Features/Home/Components/DashboardWordCard.swift:34–35,172–185,435–446`; newly blocked row controls at 89–105.

After `accepted:false`, or when a free/revoked/invalid library has more than ten words, the row is locked. Locked rows cannot expand, and the Delete button is inside `expandedSection`, which is only shown for unlocked rows. The new blocked controls expose Retry but no Delete. Both Dashboard and Create Set pass deletion through this same hidden action (`DashboardView.swift:121`; `CreateFlashcardSetPage.swift:427`); neither supplies a row swipe/context deletion action. Users therefore cannot delete these retained blocked cards from these screens despite the required preservation of deletion. The passing deletion test covers ordinary expired-paid content, which is not locked, so it does not cover this scenario.

Keep Delete accessible independently of review/expansion/edit eligibility, including rejected records. Verify the locked-row affordance and actual deletion for a rejected record and an eleventh free record, without making either reviewable/editable.

**Evidence bound:** Static SwiftUI control-flow and caller inspection, not a simulator tap result.

## Minor finding

### M1 — P3: Assert or explicitly consume the new verification result in tests

`FlashCardAITests/SharedAccountSyncTests.swift:392,417` now ignores `Task<Bool, Error>.value`, producing two new unused-expression warnings in the exact-head Mac log at 5287/5290. At 392, capture and assert `false` to verify the stale result contract as well as its side effects. At 417, explicitly consume the result within the expected-throw path. The `@discardableResult` annotation on the underlying function does not suppress an unused task property value.

Other warning categories were verified in the fixed-source baseline too: unnecessary await in APIClient/ApplicationBehaviorTests, non-Sendable test captures, and AppIntents metadata extraction. They remain visible pre-existing noise, not new Task 8 regressions; do not claim pristine output.

## Verification and unresolved gates

- Read current `ci-full-check9-run.json`, summary, per-case results and full log. Log line 111 proves actual tested source `4bb69cb20a82a14a4c0510bbefbadf5308f4bfae`. Build succeeded; case artifacts independently count **599 = 575 passed + 20 failed + 4 skipped**. Log lines 6959–6961 report **30 assertions, 0 unexpected**, not 30 failed cases. The job conclusion is **failure**.
- Independently compared current failed case names with `ci-baseline-full-cases.json`: no new failed case. Baseline full-log line 111 proves source `9096c1e3b20fb920726bd471ed4a092783ee3cc7`; its 545 cases and Xcode 26.6/iPhone 17 Pro/iOS 26.4.1 attribution are present in the actual log. All 20 current failures are baseline cases. This proves attribution, not their root cause or a waiver.
- Independently grouped current cases: EntitlementMutationBoundaryTests **33/33**, SharedAccountSessionTests **46/46**, ReviewSessionViewModelTests **28/28**, total **107/107** in the unrestricted run. No separate focused run exists at this SHA; the full run does include the focused classes. These positives do not answer the five named gaps above.
- No Swift/Mac tests were rerun during this Windows read-only review. The specific deferred-response/cache/UI regressions listed above need the authorized Mac runner after fixes. No physical Apple purchase, live AI, production backend migration, device App Attest, or simulator tap flow is established by fake transport/unit evidence.
- Static reminder wiring is present: access notifications reload Dashboard (`DashboardView.swift:160`), reload reapplies the scheduler (`735–737`), and reminders consume review queue items (`Infrastructure/Notifications/StudyReminderScheduler.swift:258–285`). Actual notification/UI behavior remains a Task 11/device evidence gate.
- **Unwaived external gates:** 20 hosted UI failures and four existing AccountView skips require Task 11 resolution; full-suite acceptance is not green. Anonymous Apple-owner/recovery B1/B2 remains Tasks 2/3 and cannot be closed by this task. Final three-repository review remains separate.

## Focused checks outside complete diff context

- Named risk: cached single-translation detail might require AI despite retained-read rights. Read the cut-off `wordDetail` cache predicate and `hasMultipleTranslations` helper, yielding R4.
- Named risk: IAP denial might be handled by the shared API path. Checked only IAP endpoint dispatch, protected request wrapper and expensive-path classifier, yielding R3 and confirming R2's side-effect route.
- Named risk: locked-card deletion might remain available through another row action. Checked the cut-off expansion predicate/Delete action and deletion call sites in Dashboard/Create Set, yielding R5.
- Named risk: new StoreKit environment dependencies might be missing in app composition. Verified the existing root injection at `App/FlashCardAIApp.swift:60`; no finding.
- Named risk: late account deletion might clear a newer session. Checked `AccountSessionClient.deleteAccount` at `251–255`; its generation guard rejects a changed session before local clearing. No claim of executed late-deletion UI coverage.
- Named risk: access events might not reach reminder scheduling. Checked the existing Dashboard-to-scheduler call and queue projection listed above; dynamic evidence remains bounded.

## Declined to judge as this task's implementation

- Anonymous-owner replacement, Apple recovery proof and closure of B1/B2: explicitly assigned to separate Tasks 2/3; this review does not authorize weakening linked ownership protection.
- Hosted accessibility harness root cause and device/Sandbox/App Attest behavior: current artifacts establish failure attribution and fake-test results only; assigned external gates remain open.
- Numerical production quotas, money budgets and cross-repository deployment behavior: outside Task 8 and not inferred from local policy tests.

**Assessment:** The transaction checks, retained history and lossless retry implementation are substantial and have real exact-head Mac evidence. Cross-source authority/error handling and two retained-content UI/read paths still miss binding requirements, so Task 8 should not pass its spec or quality gate until these findings are resolved and the covering Mac cases are observed.
