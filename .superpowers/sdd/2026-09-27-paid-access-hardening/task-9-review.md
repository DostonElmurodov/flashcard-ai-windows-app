# Task 9 Windows review

## Spec Compliance

- ❌ **Issues found.** An older same-account entitlement refresh can replace a newer revoked result and restore paid mutation authority (`electron/api.ts:66`). Cached preview reads bypass the saved-card eligibility guard (`electron/review-translations.ts:37–44`). Both were reproduced against the final implementation with focused local probes.
- ❌ **Copy mismatch.** The signed-out Profile still promises local card creation without an account (`src/Profile.tsx:19`), contradicting the approved desktop account requirement and the implemented guard.
- ✅ Every product file listed in the brief has its corresponding change. The policy matrix, global chronological first10, ordinary expired-paid saved reads, atomic local/catalog admission, scoped retained history, feature-flag TTL, typed HTTP refusal messages, and main-owned mutation checks are implemented, subject to the findings below (`shared/access-policy.ts:4–30`, `electron/store.ts:14–29,79–99,113–115,144`, `electron/main.ts:31,66–100`).
- ⚠️ Live Apple/production backend behavior, anonymous-owner recovery, D2 monetary decisions, App Review, and deployment compatibility remain outside this local task. The supplied backend contract document supports the new inactive status and typed reconciliation error; local fake responses do not prove a deployed system's behavior.

## Strengths

- Paid mutation freshness is distinct from retained saved-content access; finite expiry, a five-minute confirmation bound, inactive-state denial, and deterministic chronological eligibility are centralized (`shared/access-policy.ts:4–30`). Store history removes freshness and preserves later invalid/free/revoked decisions across offline relaunch (`electron/store.ts:15–25`; `tests/access-policy.test.ts:43–70`).
- Main binds the Store to `Api.state`; renderer schemas cannot supply an entitlement grant. Per-insert admission inside one transaction prevents partially saved batches, and the catalog's deck and cards share that transaction (`electron/main.ts:31,67–70,97–99`; `electron/store.ts:29,61,92–99`).
- The reported deferred error-body account race is fixed in the actual final code: parsing has no entitlement side effects, and error mutation checks the captured account generation after body completion (`electron/api.ts:41,56–63`; `tests/api-test-mode.test.ts:55–66`). Feature flag origin round trips also reject old generations (`electron/feature-flags.ts:12–25`).
- Supplied final evidence is internally consistent: 104/104 tests, typecheck/build, actual Electron fake-server smoke, React smoke, and all four import formats passed. Final output inspected contains no unresolved warning or failure; earlier resolved setup/red-test logs are retained separately.

## Issues

### Critical

- None found.

### Important

1. **[P1] Order concurrent entitlement refreshes before accepting their results.** `C:/Users/ForDo/.codex/worktrees/paid-access-hardening/FlashcardAI/electron/api.ts:66` captures only the account/origin generation; two refreshes for the same account share it. A slow earlier request returning `premium` is therefore accepted after a later request has already returned `revoked`, and its `checked_at` is reset to the current time. The resulting state immediately allows add/edit/AI again. This is reachable through the existing focus and periodic refresh paths (`src/App.tsx:44`), and main will persist the stale result as history (`electron/main.ts:94`). **Focused reproduction:** resolve newer refresh as revoked, confirm add denied at 101 cards, then resolve older refresh as premium; `evaluateAccess(api.state(),'add',101).allow` becomes true. Serialize/coalesce entitlement refreshes or use a refresh revision in addition to the account generation; stale successes and stale failures must not replace a newer authoritative result. Add both out-of-order success and error coverage.

2. **[P2] Apply access checks to cached preview reads as well as saved-card reads.** `C:/Users/ForDo/.codex/worktrees/paid-access-hardening/FlashcardAI/electron/review-translations.ts:37–44` lets `preview()` resolve the same cache key as `get()`, then return cached content before any authorization check. Only `get()` reaches `assertEligible(wordId)` at line 7. The renderer-exposed `previewTranslation` handler accepts word/languages directly (`electron/main.ts:95`). **Focused reproduction:** cache an eleventh card while premium, transition to revoked, verify `get(id)` rejects, then call `preview(word,'ru','en-us')`; it returns that locked card's cached secondary translation with no new request. This violates the bounded cached-read requirement and the claim that every cached read checks card eligibility. Require active authority for draft previews before serving their cache, or make saved-content cache reads resolve an eligible saved card. Preserve ordinary expired-paid cached reading through the eligible saved-card route.

### Minor

3. **[P3] Correct the signed-out creation promise.** `C:/Users/ForDo/.codex/worktrees/paid-access-hardening/FlashcardAI/src/Profile.tsx:19` still says, “You can still create and review local cards without signing in.” The newly enforced desktop policy denies signed-out creation (`shared/access-policy.ts:18`; `electron/store.ts:80,96`), while onboarding now correctly tells the user to sign in. Replace the creation promise with the desktop account requirement and accurate retained-card wording; keep iPhone account-optional purchase/use unchanged. Add a copy assertion alongside the Profile rendering tests.

## Evidence Reviewed

- Exact base `aba59fb8f5e829d2cd10065fd702cff2dc7223b3`, head `c05b03c2abd011b7232adde395350c36dbe3a231`, and all 22 changed files from the supplied `task-9-review-package.md` (351 insertions, 70 deletions). No Git commands, product edits, index/branch mutations, full-suite reruns, subagents, live credentials, or real AI calls were used.
- Task brief and implementer report under `D:/07 Hobby/FlashcardAI/.superpowers/sdd/2026-09-27-paid-access-hardening/`; the report was treated as claims and compared with the diff.
- Existing evidence under `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task9-results/`: `initial-tdd-evidence.md`, final test summary and warning/error scan in `final-full-tests.log` (104 pass, 0 fail/skip), `final-typecheck.log`, `final-build.log`, `final-focused.log`, `profile-green.log`, `electron-smoke-verified.log`, `secondary-ui-final.log`, and `import-smoke-final.log`. Initial TDD evidence is an archived record of earlier outputs, not an independently repeated chronology.
- **Only additional execution:** `reviewer-focused-probe.ts` against the worktree's final source, exit 0, with output saved as `reviewer-focused-probe.log` in that evidence directory. The probe asserts the two reproduced defects described above. It uses a local isolated Store and in-process fake HTTP responses; no production service or provider is contacted.
- The first combined UI diff output was truncated by the tool; the missing App/CreateSet segment was recovered from the same review package. No second broad diff pass was made. A small extraction from the package supplied exact finding line numbers.
- Hunk context was incomplete in the functions that had to be judged: `App.tsx:14–49` was read to inspect the missing refresh/reload part of `App`; `review-translations.ts:20–39` supplied the truncated cache-key/preview entry context; `tests/api-test-mode.test.ts:1–15` supplied the test setup before choosing the focused probe. Other changed product files were not reread separately.

## Named Outside-Diff Integration Checks

- **Retained history must remain isolated by server and account.** Checked `electron/workspace.ts:6–20`: normalized origin plus immutable account ID is hashed into the account database owner/path, and switching closes the previous account Store. The new history stays in that established database. No independent workspace scheme was introduced.
- **Access-state refreshes must not cause automatic AI refusal loops.** Checked unchanged `src/SecondaryReview.tsx:10–15`: it requests on its dependency changes or explicit user retry. The change avoids using the periodically refreshed `checked_at` as an effect dependency. This confirms no timer-based AI retry was added, while the separate same-account entitlement overlap defect remains as finding 1.
- **Windows interpretation must match the Task4 status/error contract.** Checked `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/docs/apple-subscription-validation.md` (and the Task4 report's contract summary): unchanged DTO shape, inactive `invalid_subscription`, missing purchase `free`, and HTTP503 `subscription_reconciliation_required` requiring verification/recovery rather than repurchase. Windows has matching branches and local contract tests; deployment remains unverified.
- **Sync/backup changes must not erase prior recovery metadata or content.** Compared the Store applySync/restore context in the supplied diff with the existing recovery requirement from `MEMORY.md:31–32`; the sync recovery code is preserved, restore retains the current Store's policy history, and supplied preservation tests cover content. No broader sync-code crawl was needed.

## Assessment

**Spec verdict: Needs fixes. Code-quality verdict: Needs fixes.**

The core separation between fresh mutation rights and retained reads is sound and the supplied validation is useful. The two reproduced alternate-path/ordering gaps still allow authoritative policy to be bypassed, so Task 9 should not be marked accepted until those paths are corrected and focused regression coverage passes.
