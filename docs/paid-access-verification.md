# Paid-access hardening: verification record

**Status, 30 September 2026: implementation, independent source review and automated verification are complete for the working-branch candidate. Mac run 36730138820 passed all 694 tests and unsigned Release checks at 5d2d5bebfc66a63e9d12df6a271a51c133ed7be3. This is not release approval.** This record separates executed checks from source review and external checks. Historical runs and snapshots remain preserved; a passing older revision is not a passing current candidate.

## Agreed behavior

- iPhone purchase and Apple restoration do not require an Owl account. Desktop paid access requires an account. A restored mobile grant does not itself confer initial desktop ownership or another installation's private library.
- Previously paid saved content stays readable and reviewable after expiry. New AI, additions and content edits require appropriate current access.
- The free card allowance is ten eligible active cards across languages/collections within the authenticated library. Exact saved ID and original creation time determine ordering. Metadata-only inventory is not proof of past content or payment; uploading newly seen content consumes a slot, including when its alleged old timestamp places it among the first ten. ID-less AI reservations also occupy slots.
- Daily/minute AI-operation quotas and monetary production budgets remain separate unapproved configuration choices. Synthetic local-test settings are not commercial defaults. The user's GitHub Actions budget does not authorize AI-provider spending.

## Repository and evidence boundaries

| Repository | Feature starting revision | Latest recorded candidate | Executed evidence / remaining gate |
| --- | --- | --- | --- |
| Backend (`mavrylo`) | `473a39bee299f3df4c7fdcf6f45502f880a9bd8b` | `a6e2362c8e624338c564bfd88499cd1835c956c1`, committed and remote-verified | **811/811, zero skipped**, Release build zero errors, EF no drift. Two-host historical preservation and owner/mobile inapplicable-versus-revoked controls are accepted. All 131 changed feature paths match the tested/reviewed freeze; whole-feature reviewer found no additional backend defect. |
| iOS (`flashcard-ai-ios`) | `9096c1e3b20fb920726bd471ed4a092783ee3cc7` | `5d2d5bebfc66a63e9d12df6a271a51c133ed7be3`, clean and pushed to the iOS test branch | **694/694: 666 unit and 28 UI**, unsigned generic-device Release and bounded Release configuration checks passed. Historical restore, queued order/expiry and synchronous free-eleventh-card denial controls all passed. Fresh whole-feature and scoped source reviews are complete. |
| Windows (`flashcard-ai-windows-app`) | `aba59fb8f5e829d2cd10065fd702cff2dc7223b3` | `ac37325c3cae2cef5d2adaddbaa214f213a25892` | Build, 112 tests and actual Electron loopback smoke passed at this revision. No new Windows product change has been made in the current 6B block. |

Backend tests use isolated PostgreSQL at `127.0.0.1:55440` and fixture databases with offline Apple/AI fakes. They do not exercise production PostgreSQL or live provider spending. The root verified the prior 44-file backend freeze, six evidence-log hashes and the iOS HTTP402 regression hash before the independent review; any later fix needs a new freeze and verification.

Fix1 preserves the earlier failed full runs (804/807 and 806/807). Two assertions expected the old payment status for incomplete ordering and now check the required 409 while preserving the ten-card cap/expired-paid 402 controls. A normal account AI positive test returned `ai_translation_unavailable` under the full suite but passed focused runs. Its fixture inherited a one-second deadline; only the normal-route test factory now uses ten seconds. All 24 provider-route tests, including separately configured timeout cases, and the final full suite pass. Deadline contention is the supported working explanation, not an independently captured exception cause; production settings were unchanged.

Current detailed sources: [progress ledger](../.superpowers/sdd/2026-09-27-paid-access-hardening/progress.md), [Task6B report](../.superpowers/sdd/2026-09-27-paid-access-hardening/task-6B-report.md), [independent 6B review](../.superpowers/sdd/2026-09-27-paid-access-hardening/task-6B-current-policy-review.md), and [final-review carryover](../.superpowers/sdd/2026-09-27-paid-access-hardening/final-review-carryover.md). Raw logs, manifests and immutable source archives remain under local `test-results/paid-access-hardening`.

## Mac execution and cost control

The manual `verification` workflow combines the full unit/UI run with unsigned generic-device Release validation. It uses the dedicated UI-test bundle and source-only SwiftPM caching keyed by runner/Xcode identity and the package lockfile. A real warm-cache hit was observed in run `36621056966`; no benchmark-only run is needed. Tests/builds are batched after coherent changes, not triggered after each edit.

Historical run `36724031737` at 2f45a6c executed zero steps because of GitHub billing limits. After the user increased the budget, run `36725550762` at that same source executed, compiled successfully and passed 691/693 cases (663/665 unit and 28/28 UI). All three historical purchase-selection regressions passed. The two failures came from fixtures trying to queue the locally forbidden eleventh card; they never reached their intended asynchronous boundary. Unsigned Release was not reached. A real warm SwiftPM cache restore was confirmed. These records are preserved; billing is no longer the current blocker.

The test-only correction now queues an eligible card, verifies authoritative exclusion then changed eligibility after deletion, drains work before the expiry assertions, and separately retains the synchronous eleventh-card 402 control. A fresh review found an early-exit cleanup issue; that one-line relocation is closed by a second fresh review. The source reviews and snapshots are in `mac-2f45-fix1-review.md` and `mac-2f45-fix2-review.md`. One combined run [36730138820](https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36730138820) on reviewed 5d2d5be passed: all694 tests, then actual Release build at14:46:23UTC and configuration confirmation at14:46:27UTC. No build was launched between edits. Source-head artifact matches the exact commit; 1459 downloaded evidence files are hashed and preserved locally.

## Completed local closure and delivery

1. Current-candidate iOS Mac/full/Release validation passed. The five 6B findings are closed in the [fresh fix review](../.superpowers/sdd/2026-09-27-paid-access-hardening/task-6B-fix1-review.md), with current runtime evidence recorded above.
2. Task10B local server closure is accepted: unresolved historical restrictions deny fresh free AI while recovery stays available, with two-host populated migration preservation and explicit inapplicable/revoked controls. Real production-data rehearsal remains external.
3. The independent [whole-feature review](../.superpowers/sdd/2026-09-27-paid-access-hardening/whole-feature-review.md) is complete. Its single iOS P2 historical selection defect and follow-up test expectations are corrected. [Fix1 review](../.superpowers/sdd/2026-09-27-paid-access-hardening/final-review-fix1-review.md) and [fix2 review](../.superpowers/sdd/2026-09-27-paid-access-hardening/final-review-fix2-review.md) close those findings. Subsequent boundary-test fixes passed fresh reviews and the current combined Mac run. No actionable source finding remains open in these reviews.
4. Product commit IDs are in the table above. Backend received one final feature-branch push, verified remotely. Windows product branch `codex/paid-access-hardening` and documentation branch `codex/paid-access-checkpoint-20260928` are delivered together in one final push to their shared origin. This report is included in that documentation commit; its own commit identity is determined by Git. Remote receipts are retained locally under `test-results/paid-access-hardening`. No main merge, deployment or release is included.

## External release gates

Physical Apple/App Attest and StoreKit restoration, iOS 17, signed installed builds, real production inventory/backup/restore rehearsal, deployed configuration and commercial AI settings remain unverified. The [release checklist](paid-access-release-checklist.md) records these separately. Local tests and unsigned Release checks cannot substitute for them.

Inherited warnings are retained rather than suppressed. Current resolved backend application dependency metadata contains no SSH.NET; the test dependency graph includes SSH.NET 2025.1.0 via Testcontainers and emits NU1903. NU1510 and documented Swift/build warning debt remain separate from the current functional results.

The temporary PostgreSQL test cluster at 127.0.0.1:55440 was stopped after server verification; its files and all evidence were preserved. No production instance was touched. Agent authorization: 21 of 40 used. iOS intermediate commits/pushes used the test-branch exception; general final delivery followed successful automated verification.

Historical failed-run details remain in [36725550762 evidence](../.superpowers/sdd/2026-09-27-paid-access-hardening/mac-verification-36725550762.md); final evidence and delivery boundaries are in [30 September closure](../.superpowers/sdd/2026-09-27-paid-access-hardening/final-verification-20260930.md).
