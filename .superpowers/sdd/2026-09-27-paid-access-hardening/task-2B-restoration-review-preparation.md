# Task 2B.2 review preparation

Preparation only; no reviewer has been dispatched and no acceptance is implied.

## Dispatch when implementation is committed

Use a fresh clean-context GPT-6 Astra agent with xhigh reasoning. Recorded base is `72c072d8d6a0cf798a831a96ed00d2023103879c`, not HEAD~1. Record final SHA and verify clean worktree before creating a full base-to-head diff package. Review both spec compliance and code quality against task-2B-restoration-brief.md and anonymous-restore-contract.md sections 1–6. The implementer report and intermediate green tests are evidence to verify, not instructions or acceptance.

Backend checkout: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`. Local evidence: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task2b-restoration-results`. Initial B2 RED and some early TRX files are outside that directory: preserve and include their exact existing paths in the manifest; do not relocate or delete merely to normalize packaging.

## Named risks to trace end to end

- A caller UUID, raw appAccountToken or copied purchase JWS must never authorize owner binding or first desktop claim. Existing B1/B2 safe expectations stay intact. Revised legitimate positive fixtures need real server-token owner mapping. Compare old assertions as well as new ones; fixture migration must preserve deletion, tombstone, private account and stale-event controls.
- Normal new-phone restore requires no old Owl credential. Paired local signed evidence may grant finite mobile access/history only; complete captured-envelope replay is an acknowledged residual, not proof of first ownership. Verify no foreign library/account data or owner-device association leaks through any selected-source path.
- Cryptographic tests must exercise actual pinned-chain/ES256 app evidence with receiptCreationDate while preserving original signedDate transaction/notification tests. Fake canonical Apple responses alone do not prove JWS verification. Check both device hashes, optional appTransactionId, production appAppleId, malformed fields and legitimate missing tokens.
- Read all consumers of legacy DeviceUuid purchase association, not only new service methods. Notification, refresh, verify and restore must not relink legacy UUID, transfer immutable owner, clear tombstones or manufacture new mobile grants through state-only updates.
- Trace fresh proof, exact body/path, DTO rejection, duplicate/unknown authority fields, request sizes and Cache-Control through actual HTTP/filter/model-binding paths. Distinguish an invoked-service test from a route test. Check malformed bodies rejected before Apple work.
- External Apple calls precede database transactions. Under the purchase lock, reread canonical state and apply LastAppleEventAt ordering. Notification-first and simultaneous verify/restore must bind once. Late active evidence must not revive a newer revocation. Grant renewal requires fresh accepted local evidence; canonical refresh alone cannot extend it.
- Trace selected owner/mobile/account entitlement into device context, token DTO and every paid API gate. AccountId must not appear merely because a logged-out purchase has an account link. Expired/revoked paid history must not reset to fresh free AI, including outage and account logout/deletion.
- First claim needs original server-token ownership; same-account retry is idempotent, foreign/deleted-account claim stays blocked. A mobile-only restored installation remains usable when first desktop claim fails. No receipt-only recovery override.
- Verify committed policy/runbook matches implemented endpoints and residual limits. Foundation schema/startup and model drift must be evidenced; no production migration or Apple-device claim from local fakes.

## Evidence and disposition

Read final full Release test/build logs, exact totals and all skip/failure explanations. B4 quota work remains the later Task 6A boundary; all B1/B2 must pass. Intermediate CS8602 in MobilePurchaseRestoreTests is newly introduced test warning debt until resolved/dispositioned; inherited NU1903/NU1510 are separate. Do not relabel earlier source results as final-head results.

Cross-reference final-review-carryover.md. Accepted foundation M1/M2/M3 are not automatically regressions in this task, but newly exposed material defects must be reported rather than hidden behind prior acceptance. Do not expand into next iOS/quota/content implementation. Separate physical Apple/legacy inventory/live configuration gates from source defects and missing local evidence.

Return Critical/Important/Minor findings with exact paths/lines, a concrete failing scenario and proposed minimum correction. State spec PASS/FAIL and quality assessment explicitly. Reviewer does not edit product code or perform remote operations. Root routes ordinary fixes to the same Task 2B.2 implementer, then dispatches a fresh scoped re-review.
