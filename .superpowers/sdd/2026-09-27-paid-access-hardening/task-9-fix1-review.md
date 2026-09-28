# Task 9 fix round 1 — scoped review

**Spec verdict: Needs fixes. Code-quality verdict: Needs fixes.** P2 and P3 are addressed. P1 correctly orders concurrent entitlement refreshes, including deferred error bodies, but an earlier refresh can still restore paid authority after a newer direct AI denial. This remaining shared-state path was explicitly requested as a named integration check by the controller and reproduced with fake responses.

## Scope and review basis

- Fix base: `c05b03c2abd011b7232adde395350c36dbe3a231`.
- Fix head: `f0a08c0da30f695f7a5cbe5ed80efafb70d177fa`.
- Read the task brief first, then the original three findings and the appended fix-round report. Read `task-9-fix1-review-package.md` once: six files, 56 insertions and eight deletions. The report's assertions were compared with that diff and retained evidence.
- D1 remains desktop account required, iPhone account optional. D3 preserves eligible saved-card reads/review after ordinary paid expiry, without new AI/add/content edit. Draft previews require current authority.
- The isolated product checkout was read-only. No Git commands, product edits, full-suite reruns, subagents, real network/provider calls, production credentials, push, deploy or release were used.
- This is a review of the three fixes and their immediate shared-state integration, not a whole-feature re-review.

## Per-finding assessment

### P1 — NOT ADDRESSED (partially fixed)

**Addressed part:** `electron/api.ts:66–76` advances `entitlementRevision` at refresh entry, drops refreshes superseded while feature flags are pending, and accepts success or catch-side invalidation only for the current account generation and current refresh revision. The `Api.request` error mutation at `electron/api.ts:59–62` also checks the captured entitlement-route revision after parsing the body. Therefore an older entitlement success, temporary 503, or reconciliation 503 cannot overwrite a newer entitlement refresh result. Existing account/origin checks remain in place.

The new parameterized tests receive the older headers, suspend its body, accept the newer refresh result, and then finish the older body. They assert both unchanged entitlement and the resulting add decision. The supplied focused run passes all three cases. Main persists `api.state()` in its refresh handler (`electron/main.ts:94`), so these protected refresh results are not reintroduced from a stale return value.

**Remaining P1 shared-authority gap:** current direct AI 402 and reconciliation 503 responses still mutate the same entitlement without invalidating an already-inflight entitlement refresh. In `electron/api.ts:47`, non-entitlement requests capture `authorityRevision = null`; at lines 59–61 their accepted denial clears or replaces `this.entitlement` but leaves `entitlementRevision` unchanged. The earlier refresh's success check at line 71 then still passes and stamps the old premium result as freshly confirmed.

Concrete sequence, reproduced against the fix head:

1. The same account has premium; refresh A starts and receives a premium response whose body is delayed.
2. A newer direct AI response is accepted: 402 clears entitlement, or reconciliation 503 sets `invalid_subscription`. Add at 101 cards is denied.
3. Refresh A's older premium body completes. Its revision remains current, so it restores premium; add at 101 cards is allowed again.

The probe reproduced both statuses with in-process fake fetch responses and no actual network. This preserves neither the later accepted denial nor the required inactive mutation restriction. The backend may still refuse AI, but local paid additions/content edits can regain authority. This is the explicitly requested residual P1 integration path, not a claim that the fix introduced a new unrelated defect.

**Required correction:** when a current direct authority-denying response is accepted, invalidate entitlement refreshes already in flight, using a shared authority revision/epoch or an equivalent ordering rule. Preserve legitimate direct refusal handling. Add focused cases for an older refresh with a deferred success body completing after a newer direct 402 and reconciliation 503; the final state must remain denied in both cases.

Evidence: `fix1-p1-red-all.log` records the three original failures; `fix1-p1-green.log` is 15/15, `fix1-p1-deferred-body-green.log` is 8/8, and the current affected run includes all three refresh ordering cases. The new remaining-gap reproduction is `test-results/paid-access-hardening/task9-results/fix1-reviewer-authority-probe.ts` with output in `fix1-reviewer-authority-probe.log` (exit 0, both gaps reproduced).

### P2 — ADDRESSED

`electron/review-translations.ts:37–42` checks `reviewTranslationAccess(workspace.account)` inside the draft-preview resolver, before `load()` can look up or return cached content. The shared helper requires an account and the current AI policy, covering inactive statuses, elapsed expiry and stale confirmation. The same resolver is called again on late response completion, so the new guard participates in the existing revalidation rather than applying only at request entry.

The saved-card `get()` path is unchanged: its resolver calls `store.assertEligible(wordId)` before cache lookup (`electron/review-translations.ts:6–11`). Its eligible cached reads still return before the new-AI gate. Ordinary expired-paid saved reads therefore remain available; a caller cannot use the draft endpoint to read a locked saved card by supplying its word/languages.

The regression primes one saved translation while premium, proves expired-paid `get()` retains it, rejects the corresponding draft preview after paid expiry, rejects revoked eleventh-card `get()` and preview, and rejects elapsed-premium and signed-out draft previews. It finishes with exactly one fake AI request. The supplied P2 run is 19/19, and the regression passes in the final affected run. The actual Electron smoke also reports retained expired-paid cached reading/review and offline relaunch. No fix-introduced saved-cache regression was found.

### P3 — ADDRESSED

`src/Profile.tsx:19` replaces the signed-out creation promise with: “Sign in to create or add cards on desktop. You can still review eligible saved local cards in this separate workspace.” It states the desktop requirement and qualifies saved-card review accurately. It adds no iPhone account requirement for general mobile purchase/use.

The new static rendering test asserts the desktop sign-in wording, eligible saved-review wording, and absence of the old unconditional creation promise. The supplied P3 run is 6/6, and the same test passes in the final affected run. No new component state or behavioral change accompanies the copy correction.

## Fix-introduced breakage

No separate new breakage was found in the six-file fix. The remaining blocker is incomplete ordering of the existing entitlement authority shared by refreshes and current direct refusals, described under P1. The P2 saved/draft split and P3 copy change are narrowly scoped and consistent with the approved requirements.

## Evidence reviewed and execution limits

- `fix1-final-focused.log`: 40 tests passed, zero failures, cancellations or skips. Includes API ordering/refusal behavior, feature flags, shared policy/history, translation caching and Profile/UI assertions.
- `fix1-typecheck.log`: the retained typecheck output contains no diagnostics. The implementer records exit 0.
- `fix1-build.log`: the retained build output includes its successful typecheck stage and produced renderer assets; the implementer records successful Electron bundling/exit 0.
- `fix1-electron-smoke.log`: PASS for actual Electron scoped IPC, forged renderer denial, atomic manual/staged/catalog saves, global 10, preserved paid 101, expired-paid cached read/review/delete/export/backup, and offline relaunch with 100 remaining cards and mutation denial; one fake AI call.
- Inspected P1/P2/P3 red-failure excerpts and red/green result counts, including deferred-body green evidence. These are retained implementer outputs, not independently repeated TDD chronology.
- The prior 104-test full run and earlier import/React smokes were not represented as fresh fix-head executions and were not rerun.
- The sole additional execution was `fix1-reviewer-authority-probe.ts`, stored and run outside the isolated product checkout. It patches fetch in-process, fails on unexpected routes, uses fake account values, and writes only its source/log under the permitted evidence directory.
- To resolve context cut off by the supplied diff, read only the missing translation resolver/load sections and the API-test setup. Named unchanged integration reads were the shared translation access helper, feature-flag refresh lifecycle, and the main refresh/preview handlers. No second broad diff or Git pass was made.

## Out-of-scope observations

No additional unrelated defect was elevated in this scoped review. Live Apple/production behavior, deployment compatibility, monetary/D2 decisions, broad ownership/sync behavior and whole-feature integration remain for their designated checks. The fake-backend evidence does not establish deployed backend behavior.

## Final assessment

**P1: NOT ADDRESSED (partial); P2: ADDRESSED; P3: ADDRESSED.**

The targeted fixes and their existing regression evidence are otherwise sound. Do not accept Task 9 until the older-refresh/newer-direct-denial authority resurrection is corrected and the two focused regression cases pass.
