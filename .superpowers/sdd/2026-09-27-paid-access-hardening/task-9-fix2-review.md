# Task 9 fix round 2 — scoped review

**Spec verdict: PASS. Code-quality verdict: PASS.** Residual P1 is addressed. No fix-introduced issue was found in the two-file change. P2 and P3 remain accepted from the preceding review and were not reopened.

## Scope and basis

- Fix base: `f0a08c0da30f695f7a5cbe5ed80efafb70d177fa`.
- Fix head: `43ba1668a4205882b8565fa0720e7fa3fb97d67c`.
- Reviewed the Task 9 brief, preceding scoped review, appended round-2 report and exact round-2 package. The package was read once: `electron/api.ts` and `tests/api-test-mode.test.ts`, 24 insertions and two deletions.
- Checked current API source and the missing existing API-test context against the package, then inspected the supplied red/green, typecheck and build logs. Report assertions were not treated as independent evidence.
- Product checkout `C:/Users/ForDo/.codex/worktrees/paid-access-hardening/FlashcardAI` remained read-only. No Git commands, product mutations, subagents, test reruns, real network/provider calls, credentials, push, deploy or release were used. Only this review artifact was written outside that checkout.

## Per-finding verdicts

### P1 — ADDRESSED

At `electron/api.ts:59–63`, an accepted authority-denying response must still belong to the current account/origin generation. Entitlement-route errors must additionally match their captured entitlement revision. For direct responses, the existing denial branch now increments `entitlementRevision` before clearing entitlement or replacing it with `invalid_subscription`.

That increment closes the reported sequence. A refresh started before the accepted direct denial holds an older revision. When its delayed premium body completes, the success guard at `electron/api.ts:74` rejects that result and returns current state. The stale response therefore cannot receive a fresh confirmation timestamp or restore add/edit authority. A refresh still awaiting feature flags is also superseded by the entry guard at line 71.

The added parameterized regression uses a suspended premium response body, accepts a direct payment 402, reconciliation 503 or temporary 503, and checks that adding at 101 cards is denied. It then finishes the earlier body and checks both exact preservation of the denied entitlement and continued denial. Finally, each case starts a genuinely later entitlement refresh and verifies that lawful Premium and add authority can return. This covers the residual finding and a meaningful recovery control; the fix does not permanently latch access off.

Retained evidence confirms the intended distinction:

- `fix2-p1-red.log`: eight tests passed and all three new cases failed because the old premium result replaced the accepted null/invalid entitlement. The failures occur at the explicit state-preservation assertion.
- `fix2-p1-green.log`: 27 tests passed, zero failures, cancellations or skips. All three new deferred-body cases pass, including their later-refresh controls.

Existing protections remain intact in the inspected code and retained focused run. Older entitlement error bodies still require the captured revision before changing authority; both temporary and reconciliation stale-error regressions pass. The account-generation check still encloses the revision increment and mutation, and the delayed old-account error test passes. Ordinary 429 handling does not enter the changed denial branch or advance this revision; its existing test preserves Premium and retry timing. No request replay was added.

### P2 — ACCEPTED, UNCHANGED

The accepted draft-preview authority fix is outside the two-file change and was not reopened. This round does not modify its saved-card/draft distinction or ordinary expired-paid retained reads.

### P3 — ACCEPTED, UNCHANGED

The accepted desktop sign-in wording is outside the two-file change and was not reopened. Desktop account-required and iPhone account-optional behavior remain the applicable requirements; this fix adds no platform/account requirement.

## Fix-introduced issues

None found. The production change reuses the existing revision and existing denial semantics, with the increment inside the same generation-protected synchronous mutation block. Entitlement-route errors do not increment that revision themselves, so their existing current-refresh reconciliation handling remains valid. Temporary 503 already removed current authority before this fix; invalidating an older confirmation now prevents that existing removal from being undone.

No concrete unanswered question remained that justified an additional fake-only probe. No full suite or prior smoke was repeated.

## Evidence limits and out-of-scope

- The 27/27 result is verified from the retained implementer log, not a fresh reviewer execution. Its existing policy cases also cover desktop account-required mutation, ordinary expired-paid saved reading/review, scoped offline history and inactive mutation denial.
- `fix2-typecheck.log` contains the TypeScript invocation and no diagnostics. `fix2-build.log` includes the typecheck stage and successful renderer asset generation. Both are consistent with the report's recorded exit 0; those exit statuses are report claims rather than independently captured status fields in the text logs. The Electron bundling command is silent in that output, so the log alone is not separate runtime smoke evidence.
- Earlier full-suite and Electron smoke results were not relabeled as round-2 runs. Live Apple/backend deployment, real provider behavior, release compatibility, D2/monetary decisions and unrelated whole-feature behavior remain outside this scoped review.
- No unrelated defect or new restriction on retained saved content was identified or introduced into the review scope.

## Final assessment

**P1: ADDRESSED. P2/P3: previously accepted, unchanged. Spec and code quality: PASS for this fix round.** The residual direct-denial/older-refresh blocker is resolved; no further correction is required within this scoped review.
