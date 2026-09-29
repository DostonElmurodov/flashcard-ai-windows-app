# Task 3B independent source review — 544c16e

## Verdict and scope

**Changes required. Task 3B is not accepted, not GREEN, and not release-ready.** This review finds six Important source-supported defects and one Minor test-evidence defect. No Critical defect was established in the bounded review. Mandatory final Mac validation is independently blocked and remains required after repairs.

Candidate: **544c16e1173c3fe2b219be050bd6416d3e2b89b2**. Base: **f6a9e194505dfd27ec28b3fbde11bfcb8eca095e**. Product checkout: D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\ios. Accepted backend handoff recorded by the brief: 3957f3e08c6317ff1495939e5c0cfbe7fea4f110.

This independent Task 3B review uses the approved behavior, frozen contract, brief, diff and saved execution evidence. Implementer explanations were treated as claims to check. It is not the later whole-feature review of all three repositories. No product files were changed, no Git commands or mutations were performed, no suites were rerun, no workflows were dispatched, and no live Apple/AI calls or billing changes were made.

## Evidence and hash checks actually performed

- Read the 19-file frozen diff with extended context. Its measured size is 167,633 bytes and SHA-256 is **7A5E10322081DDFD74BE58257AA6AB8DBAEB0472083D6EE84F30590110030187**.
- Recomputed size and SHA-256 for **all 52 entries** in task-3B-evidence-544c16e.json: zero mismatches. These include frozen requirements/report/diff/index and selected saved run metadata, source artifacts, logs, parsed cases and hash inventories. This does not mean every nested XCResult file was independently rehashed or every log line reread.
- Read actual saved metadata, source-head artifacts, parsed cases and focused raw Xcode result lines. Earlier tested sources matched their saved source-head artifacts. Root's clean-checkout and exact-Git-diff checks are supplied provenance; I did not repeat Git verification.
- Independently checked the two final-candidate run.json files: candidate SHA, failed jobs, **zero steps**. Both artifact listings contain **zero artifacts**. Saved annotations say the jobs did not start because recent account payments failed or the spending limit needs increasing. There is no candidate compiler/test result or source-head artifact.
- Examined targeted unchanged callers/state helpers to resolve concrete risks: launch/foreground ordering, linked-account freshness, Check access, exact request/proof bytes, claim session guards, local expiry/read policy, transaction observation, Keychain cleanup and fixture composition. They are named below.

| Saved source/run | Actual result checked | What it establishes |
| --- | --- | --- |
| 54fa4bb / 36517924836 | 14 cases: 13 failed, 1 passed | Initial missing-feature RED. The passing outage stub is not evidence of the final processor. |
| 3a9aab9 / 36519160205 | 21 cases: 14 failed, 7 passed | Intermediate RED and bounded local behavior only. |
| 63a4716 / 36520917034 and 36520919418 | Compiler error at AccountSessionClient.swift:322; no executed cases | Compile failure, not behavioral RED. |
| bbdbaa2 / 36521130121 | 31 cases: 30 passed, 1 failed | Active mobile versus unrelated account selection was actually RED; seven resume tests passed at this earlier SHA. |
| bbdbaa2 / 36521131761 | 119 cases: 109 passed, 10 failed | Nine mutation-fixture failures and the desktop-denial mobile-selection failure were observed. 28 review tests passed. |
| 544c16e / 36521873497 and 36521875224 | Zero steps and artifacts | External execution block only. Neither final authority selection nor fixture repair has compiled or run. |

The Important findings below are **source-supported defects with explicit reproduction scenarios**, not newly demonstrated runtime failures. Earlier executed failures are separate from these findings and from coverage uncertainty.

## Spec compliance and strengths

| Requirement | Source assessment |
| --- | --- |
| Guest purchase uses a server token; no mandatory Owl login/local recovery credential | Implemented in purchase-context/cache. The cache is installation-key scoped; registration sends no purchase-derived DeviceUuid. |
| Optional account lookup failure does not gate verify/restore; healthy header retains §5 fallback | Implemented by route-specific header selection. Purchase context is device-only. This does not prove every invalid-account HTTP response case. |
| Full local Apple envelope and exact JWS | Live source uses verified transaction/AppTransaction results and nonnil local device ID. Optional Apple fields are forwarded opaquely. No new-only appTransactionID property access was introduced. |
| No automatic Apple authentication; explicit Restore may synchronize | AppStore.sync remains explicit; AppTransaction.refresh permission is scoped to the call. Selection/error integration has I2/I4 below. |
| Bounded history and durable finish | Latest lookup is restricted to the two allowed products. Finish follows backend acknowledgment and is separate from applying UI state. I2 prevents reaching current valid evidence in a supported history case. |
| Resume lost/expired JWT without rotating a valid key | Resume uses a fresh assertion over exact empty-object bytes and preserves the key on ordinary failure. I1 leaves proof-time invalidation unrecovered. |
| Finite grants and retained paid history | Processor checks finite active mobile grant/entitlement expiry and ordering. Store recognizes mobile_restore, prioritizes active independent mobile authority and retains known paid history over free projection. Saved snapshots are not fresh mutation confirmation. |
| Claim/private-account separation | Old verify-association hop is removed. Claim uses verified JWS plus existing account/device proof. Success checks account/session/authority. Desktop-owner denial is distinct from mobile access. |
| Support | Both surfaces use mailto:dostone2100@gmail.com plus copy. No receipts/IDs/keys are attached and no message is sent. This does not implement owner recovery. |
| Async ordering | Durable-response processor has useful key/session/order guards. Earlier asynchronous phases remain unsafe under I5. |
| iOS 17 / offline / Release | Deployment settings remain 17.0. New fixture reset is DEBUG-guarded with hosted-XCTest preconditions, but I6 violates Keychain isolation. Final Release exclusion remains unverified. |
| Task11 M1 guidance | README includes exact OWL_DEBUG_BUNDLE_ID=com.mavrylo.owlai.uitest. restore-proof selects both added classes. Final executed scope evidence remains required. |

DTO changes are additive. Purchase/restore encode once and give proof generation and transmission the same bytes; claim preserves its sorted exact body. Missing evidence fails rather than downgrading to raw-JWS restore. The durable-acknowledgment/current-session split is a useful improvement. No implementation here makes the approved complete-envelope replay limitation disappear.

## Critical findings

None established within this bounded source review. This is not proof that broader paid-access, quotas, migrations or deployment are safe.

## Important findings

### I1 — Invalid keys discovered during normal protected requests cannot enter recovery

**Primary:** [AIIntegrityCoordinator.swift:184](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Security/AppAttest/AIIntegrityCoordinator.swift:184>).

With a locally unexpired JWT, performDeviceRegistration returns early. headersForAIRequest can then encounter DeviceCheck invalidKey or the contract's unknown-key response at assertion-challenge. Its catch only logs and returns empty headers. APIClient turns that into deviceIntegrityUnavailable; APIError does not classify that error as requiring recovery. Key invalidation/rotation exists only inside resumeDeviceSession, which this path does not reach while the JWT remains locally valid.

**Reproduction:** retain an unexpired JWT, invalidate its local App Attest key (or return unknown key at assertion-challenge), and invoke public Restore/protected refresh. Every attempt takes the valid-JWT fast path, cannot construct proof, and retains the same key. No new installation is registered and ordinary Apple restore cannot repair access. JWT expiry or a separate recovery event might eventually change the path; pressing Restore does not.

**Impact:** blocks the approved ordinary recovery after genuine key invalidation. This is not a recommendation to rotate keys on network/proof-order failures.

**Repair/check:** propagate typed invalid-key/unknown-key outcomes from ordinary assertion construction; perform bounded coordinated replacement only for these conditions; restore full Apple evidence under the new installation. Add an offline public-request test starting with a valid JWT, plus network/proof-rejection controls that preserve the key. Inspected API boundary: APIClient.swift:500 and APIError.swift:33–55. Existing direct-resume tests do not exercise this path.

### I2 — Old unfinished product evidence persistently blocks the current valid product

**Primary:** [StoreKitService.swift:188](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Purchases/StoreKitService.swift:188>).
**Related:** [StoreKitPurchaseEvidenceSource.swift:111](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Purchases/StoreKitPurchaseEvidenceSource.swift:111>).

Discovery processes unfinished + current + historical, deduplicated by originalTransactionId. The live unfinished scan includes allowed verified products without excluding upgraded items. An older unfinished monthly transaction can therefore precede the valid current yearly transaction for the same original purchase.

The frozen backend contract permits canonical/product mismatch to reject that monthly envelope with apple_identity_mismatch. The processor throws before finish; discovery's single catch aborts the loop. Yearly evidence is never submitted. Monthly remains unfinished and first on subsequent automatic/explicit Restore passes. Even when the first item succeeds, original-ID deduplication skips the distinct current transaction on that pass.

**Reproduction:** fake current evidence Y = yearly; unfinished M = monthly; both use original O. Return the contract's 503 identity mismatch for M and a valid mobile_restore result for Y. Invoke Restore twice. Source flow sends M twice, never Y, and never finishes M. A separately arriving transaction update might recover independently, but the public discovery path remains blocked.

**Impact:** valid paid proof cannot restore access because obsolete evidence in the same purchase chain wins selection. This is source reasoning against the frozen contract, not a runtime or physical upgrade reproduction.

**Repair/check:** separate selection of current/historical restoration proof from unfinished transaction completion. Prefer valid current evidence; preserve failed unfinished handles; do not abort eligible current proof merely because an older item fails. Use transaction identity where per-transaction acknowledgment/finish is required. Add the two-pass mismatch regression and same-original multiple-unfinished completion coverage.

### I3 — Foreground and Check access no longer refresh linked-account entitlement

**Primary:** [StoreKitService.swift:379](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Purchases/StoreKitService.swift:379>).

The previous account.refresh was removed and no optional account refresh replaces it. accountProfile only identifies the account. An iapToken account-source result is deliberately not installed as device authority: EntitlementStore.update recomputes without renewing accountEntitlement/accountCheckedAt. Only the separate account model refresh renews that confirmation.

**Reproduction:** sign in to an already linked active Owl account on an installation with no local Apple evidence/independent mobile authority. Let its account confirmation become older than five minutes; foreground or press Check access. Discovery is empty and iapToken can return active account entitlement, yet accountCheckedAt stays stale and canUseAI remains false. Opening Profile and completing its separate refresh can recover.

**Impact:** a successful public access refresh cannot restore legitimate account fallback. Removing a mandatory account gate for mobile work should not remove refreshing the separate account source.

**Repair/check:** complete independent mobile work first, then refresh the applicable account source best-effort without making its outage gate mobile purchase/restore. Add a public-refresh case with no Apple evidence, an existing paid account and expired confirmation; retain the mobile/account-outage control.

**Unchanged boundaries checked:** AppRootModel.swift:143–152 and FlashCardAIApp.swift:135–138 route foreground here; AccountProfileModel.swift:23–54 owns account refresh; EntitlementStore.swift:168–176/226–233 separates account state; WordSearchView.swift:549–554 checks canUseAI immediately after this method. Initial sign-in/session notifications do refresh the account; they do not repair the later foreground path.

### I4 — Token success erases failed Apple and reconciliation checks

**Primary:** [StoreKitService.swift:382](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Purchases/StoreKitService.swift:382>) and [StoreKitService.swift:394](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Purchases/StoreKitService.swift:394>).

refreshTokenAndEntitlement discards discovery's result and clears errorText after token success. Failed AppTransaction.shared, restore evidence or reconciliation can thus become a quiet successful refresh.

**Reproduction:** make currentEvidence throw, then return valid free iapToken for an installation without an established server grant. Invoke public refreshTokenAndEntitlement, rather than the isolated discoverApplePurchases tested today. The Apple failure is set and then erased; free access is displayed without the required failed-check/retry explanation. With known paid history, local paid reads remain retained, but failed-check/support state still disappears.

**Impact:** violates the explicit difference between no purchase and failure to check, and removes persistent reconciliation guidance. No server paid grant or retained-text loss is inferred.

**Repair/check:** carry typed discovery outcomes through outer refresh. Clear an Apple failure only after an applicable successful Apple recovery/check, while permitting token state to update separately. Add public startup/foreground cases for evidence error + token/free success, restore/reconciliation failure + token success, and genuinely empty successful discovery.

### I5 — Earlier pre-processor failures can overwrite newer success

**Primary:** [StoreKitService.swift:202](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Purchases/StoreKitService.swift:202>).
**Related:** [StoreKitService.swift:118](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Purchases/StoreKitService.swift:118>) and [StoreKitService.swift:264](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Purchases/StoreKitService.swift:264>).

stillCurrent checks installation key, account generation and authority identity, but not captured authority order. Pre-processor discovery/purchase catches use that predicate plus busyRevision. A newer successful transaction processor changes refreshRevision and accepts newer authority, but leaves busyRevision unchanged.

**Reproduction:** suspend public discovery A in evidence enumeration. Process newer transaction B successfully, installing paid access and clearing errors. Resume A with an evidence error. A has not started its processor; identity/key/account and its busy ID still match, so its stale error overwrites B's successful presentation. The purchase path has the same structure before its processor starts.

**Impact:** Task8 O1 is not fully closed across public entrypoints. Backend-response guards do not cover earlier asynchronous phases. A delayed old operation also gets a newly minted authority request upon entering the processor, so the original snapshot does not carry ordering throughout its lifecycle.

**Repair/check:** preserve original operation ordering across evidence/context/purchase awaits and error application, while still finishing durably acknowledged stale transactions. Add deterministic late-evidence and late-context/purchase failure tests after newer success, not only two already-started backend requests.

The isBusy defers separately check only busyRevision (lines 175/223), not the brief's full captured scope. They do prevent an old public request from clearing a newer public busy request; no separate privilege escalation is claimed. Finalization needs explicit scope invalidation/ownership consistent with the repaired generation model.

### I6 — Fake invalid-key test deletes real purchase/token Keychain items

**Primary:** [AIIntegrityCoordinator.swift:78](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/Infrastructure/Security/AppAttest/AIIntegrityCoordinator.swift:78>).
**Trigger:** [AppAttestSessionResumeTests.swift:173](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/FlashCardAITests/AppAttestSessionResumeTests.swift:173>).

resumeDeviceSession injects AppAttestSessionStorage and assertion generation, but invalid-key/401 cleanup calls global AppAccountTokenStore.clear and TokenStore.clear. testCoordinatorRotatesOnlyGenuinelyInvalidLocalAppAttestKey deliberately enters that branch with fake App Attest storage. It deletes the real purchase/token items despite the fake storage. TokenStore uses ordinary com.mavrylo.owlai.deviceToken; the purchase cache also uses its actual service, with no injected cleanup or snapshot/restore.

**Reproduction/impact:** run this focused test with sentinel values in those app Keychain stores: they are removed. restore-proof uses the ordinary hosted Debug identity, not the UI bundle override. Fresh CI simulators can hide the side effect; local execution is not isolated.

**Evidence limit:** the trigger test passed at bbdbaa2; this review did not inspect that runner's Keychain or execute a sentinel reproduction. The deletion call path is established by source.

**Repair/check:** inject purchase-context/token invalidation, or include it in the storage abstraction; use fake invalidation recorders. Assert exact expected cleanup for real invalidation and no cleanup for network/proof-order failure. Do not use real Keychain deletion as hidden test behavior. Final Release exclusion for the separate hosted fixture reset is still required.

## Minor finding

### M1 — Out-of-order test can pass without the claimed response ordering

**Location:** [AnonymousPurchaseRestoreTests.swift:534](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios/FlashCardAITests/AnonymousPurchaseRestoreTests.swift:534>).

The first synchronous URLProtocol callback blocks on a five-second semaphore. The test awaits the newer call before signaling. If both callbacks cannot progress concurrently, the old call times out first and the newer success runs afterward. Final assertions still pass: they do not require delivery of the old 503 after newer success, or fail on the timeout fallback.

The saved bbdbaa2 log reports this test passing in **5.024 seconds**. That is consistent with the timeout path; the evidence does not establish which completion order occurred. The pass must not be cited as proving the advertised out-of-order race.

Use asynchronous independently gated responses, assert newer success is applied before releasing the old failure, and fail synchronization timeouts. This is distinct from I5's source-supported pre-processor race.

## Cannot Verify / mandatory gates

1. **Final Mac acceptance:** execute restore-proof with both new classes, paid-access with SharedAccountSessionTests/EntitlementMutationBoundaryTests/ReviewSessionViewModelTests, relevant full scope, and unsigned iOS-device Release on the exact repaired SHA. Save source artifacts, actual class/case counts and logs. Neither billing-refused job is execution. Verify the hosted fixture reset and all test-only guards/types are absent from Release; inspect compiler/concurrency warnings. Older successful builds cannot prove candidate compilation.

2. **Coverage beyond fixed-string fake evidence:** add or identify selected offline tests for nil device ID, missing/unverified AppTransaction, missing appAccountToken/optional appTransactionId forwarding, explicit Restore cancellation, mobile grant expiry/revoke, genuine key replacement followed by automatic restore, duplicate/current/unfinished handling and full public refresh failures. Current fake envelopes do not execute live extraction. Direct processPurchaseEvidence tests also do not prove the actual updates adapter: handleTransactionUpdate requires LiveStoreKitPurchaseEvidenceSource. Establish offline adapter coverage before claiming Transaction.updates is tested.

3. **Account boundaries:** success paths check destination account/session and desktop-owner denial keeps mobile access. Add/review deterministic same-account reauthentication/account-switch error-completion coverage as well as success. A healthy optional header subsequently rejected by the server differs from a lookup throwing. No broader private-account breach is inferred. The unchanged profile sheet still constructs shared account/sync models; inject that carryover route before adding offline UI visits.

4. **Physical/iOS compatibility:** source target remains 17.0 and avoids the new-only correlation property. Final SDK build plus physical matrix must establish evidence availability. Real App Attest; clean install/new iPhone; lost key; ordinary purchase; two-device restore; App Store/iCloud account differences; historical reinstall recovery; upgrades, Family Sharing, expiry/revoke; explicit cancellation; offline-to-online retry remain Apple Sandbox/release gates. Simulator fakes and unsigned Release cannot close them.

5. **Legacy migration and broad policy:** local known-paid/free preservation and an earlier isolated test do not close populated old-schema migration/startup or actual token/AI routes. Keep exact checks from legacy-migration-preflight.md and final-review-carryover.md: paid/trial/claimed/deleted markers, truly free installations, empty/error Apple discovery, preserved library/history/tombstones, accounting identity and providerCalls=0 for denied cases. True-free global-first-ten eligibility and broader retained-content gaps belong to their routed tasks/final review and were not re-audited here.

6. **Security/accounting/operations:** this source review does not re-prove accepted backend ownership, finite mobile grant enforcement, private-account isolation, shared quotas or provider spend protection. D2 numbers/live monetary budget remain unapproved. Complete paired-envelope replay remains the approved bounded mobile-access/shared-allowance residual; no perfect exclusion claim is accepted. The mail action does not close exceptional first-desktop owner recovery; validate the operational support procedure separately.

## Code quality verdict

**Request changes.** The protocol/DTO boundaries, exact-byte request handling, key-scoped context and durable acknowledgment split are useful. The defects occur in composition: obsolete evidence wins selection, discovery outcomes are flattened, account freshness is dropped, operation ordering omits earlier awaits, and injected coordinator storage still reaches global cleanup.

Repair I1–I6, correct M1's evidence, obtain focused regressions, and rerun required Mac scopes/Release on the repaired final SHA. This report does not accept Task 3B or waive the subsequent whole-feature and physical-device review.

