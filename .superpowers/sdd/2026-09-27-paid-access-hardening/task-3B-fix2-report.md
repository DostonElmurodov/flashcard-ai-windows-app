# Task 3B repair round 2 — execution and repair log

Status: production repair candidate is local and uncommitted atop test checkpoint `68b0ddc7a48336b664734dc1485478395969f20e`. The five new cases have actual Mac behavioral RED; the candidate repair has **not** been compiled or tested and is not GREEN. Task 3B remains unaccepted. Root owns commit, push, manual workflow dispatch and evidence collection.

## Input and exact scope

Read `task-3B-fix2-brief.md` and the complete `task-3B-fix1-review-35ac221.md`. The test-only diff is confined to `FlashCardAITests/AnonymousPurchaseRestoreTests.swift`. Existing `restore-proof` workflow selection already includes the entire `AnonymousPurchaseRestoreTests` class, so no target or workflow selector change is needed. The five new cases cover only the three open findings, with two small existing-helper options for fake token persistence/response tokens and one reusable C/A/B fixture.

| Finding | New offline case | Required schedule and expected result |
| --- | --- | --- |
| I5-A | `testNewerPendingProcessorStillAppliesAfterOlderDiscoveryEntersProcessorLate` | A captures older authority and suspends Apple enumeration. B captures newer authority and waits on a fake backend reply. A then gets an inactive/history acknowledgment; B's premium acknowledgment arrives last. Both are durably finished, and B must still apply premium. |
| I5-B, error | `testLateEnumerationErrorCannotPassAnAuthorityCheckSuspendedOnAccountGeneration` | A's Apple enumeration error suspends inside its final account-generation predicate. B applies a newer grant. A resumes with the same generation; the old error must not replace the newer success. |
| I5-B, token | `testOldProcessorCannotPersistTokenAfterFinalAuthorityCheckSuspends` | A's acknowledged old grant suspends inside the final predicate. B applies/persists a newer token. A resumes with the same generation; only B's token may reach injected storage, while A's backend acknowledgment may still finish. |
| N1/I4, Restore | `testRestoreKeepsEarlierReconciliationGuidanceAfterAnotherOriginalSucceeds` | Public `restore()` sees current C acknowledged, unfinished different-original A denied with `subscription_reconciliation_required`, then unfinished different-original B acknowledged. Restore reports unresolved failure and retains support guidance; independent B remains usable and finished. |
| N1/I4, foreground | `testForegroundRefreshKeepsEarlierReconciliationGuidanceAfterOtherSuccessAndToken` | The same C/A/B chain runs through public `refreshTokenAndEntitlement()` and a succeeding token reply. A's support guidance must survive the later valid B and token success, while B remains usable and automatic discovery does not sync. |

The I5 tests use checked continuations/URLProtocol callback gates and XCTest expectations; a missing gate or expectation is an assertion failure, never a semaphore timeout accepted as success. N1 uses the production discovery loop's deterministic sequential C/A/B fake evidence order, asserts the exact request order, and separately asserts unfinished finish handles. All API responses use `RestoreProofURLProtocol` on an ephemeral offline session. The evidence source, transaction updates, entitlement defaults, account generation and token storage are injected fakes; these cases call no live StoreKit purchase, AppStore sync, DeviceCheck, shared network, Keychain token persistence or AI provider. The explicit-Restore `synchronize()` call is recorded by the fake source only.

## Preparation and simplify record

First test iteration added the I5-A and I5-B schedules and optional fake token recorder. Manual simplify kept the existing `DeferredRestoreHTTP` gate for I5-A and the existing test service constructor for all three cases; no product seam was added. Second test iteration added the two N1 public cases with one shared fake evidence/reply fixture to avoid duplicating C/A/B setup. Manual simplify retained separate public entrypoint assertions because their outcomes differ (`restore()` result versus token refresh). Gate absence now fails explicitly before awaiting a held task. The diff contains five new test methods and helper-only changes, no production or workflow edit.

Windows static checks: `git diff --check` passed for the iOS checkout; the five method names are present in the class selected by `restore-proof`. This is source inspection, **not** Swift compiler or runtime proof. No Swift toolchain is installed on this Windows host. The Mac RED checkpoint, subsequent bounded production repair, exact-source focused/full suite, unsigned Release fixture-exclusion check and fresh scoped review all remain open. Setup/compiler failures, if any, must be corrected and rerun before interpreting behavioral results.

Historical evidence remains unchanged: exact-79 focused restore-proof 54/54 and paid-access 119/119 passed, unsigned Release passed, but its full run failed 18 cases. The base-35 fixture isolation remains unexecuted in the full suite; its repeated unsigned Release run `36576522177` later acquired a runner and passed, including the configured fixture-exclusion build check. Do not use the historical 79 pass or the base-35 Release build to claim this repair candidate GREEN.

## Actual Mac RED at the test-only checkpoint

Root committed and privately pushed only `AnonymousPurchaseRestoreTests.swift` as `68b0ddc7a48336b664734dc1485478395969f20e`. Manual `restore-proof` run `36584051326` compiled and executed **59 cases: 54 passed, 5 failed, 0 skipped, 0 unexpected exceptions, 7 assertion failures**. The prior 54 cases passed; each new case failed at its intended behavioral assertion. Root retained checked-out source, full log, `cases.json`, XCResult and 1,601 evidence hashes at `test-results/paid-access-hardening/task3b-results/36584051326-restore-proof-fix2-red-68b0ddc`. I read the raw assertion lines in `full.log` myself:

- I5-A: B reported `appliedToCurrentSession=false`; final status stayed `expired_paid` rather than premium and `canUseAI` was false.
- I5-B error: the stale Apple-evidence error replaced nil after B succeeded.
- I5-B token: storage recorded `[new-token, old-token]` rather than only B's token.
- N1/I4: both public Restore and foreground refresh lost support guidance after B/token success.

These are behavioral RED results, not compiler/setup failures. The old base-35 billing-refused full/Release attempts still provide no test evidence; the later base-35 unsigned Release success does not validate this repair candidate.

## Local production repair candidate (not executed)

Only `Infrastructure/Purchases/StoreKitService.swift` changed after RED. I5-A removes the evidence processor's arrival-time write/check of global `refreshRevision`; captured `EntitlementStore` request order, installation key and account generation now decide application and error ownership. The foreground token refresh keeps its own revision guard. I5-B makes `stillCurrent` recheck authority/identity after the awaited account-generation lookup, before token persistence or error effects. Public Restore repeats key/accepted-order checks after its final await; token and busy/error paths repeat their relevant owner checks after awaited predicates. The transaction-update handler explicitly marks when the shared processor has started, preventing duplicate preprocessor error reporting now that evidence processing no longer changes the global revision.

N1/I4 restores `firstFailure` guidance after all C/A/B envelopes, using a same-operation reporting continuation only while the discovery still owns the latest order and busy state. This keeps independent B's valid grant/finish; it does not close B's paid confirmation. A token success sees `discovery.failure` and must leave the restored guidance visible. The existing exact obsolete same-original `apple_identity_mismatch` suppression and cancellation paths are untouched.

Manual simplify after each iteration: kept the authority predicate as the single post-await identity/order check; removed processor-arrival UUID use rather than introducing a replacement global counter; reused `continuingOperation` for N1 instead of a parallel failure owner; kept explicit post-await owner checks only at existing state-effect sites. `git diff --check` passed locally. Windows source inspection is not Swift compilation or runtime proof. Root's next exact-source full suite (which includes the 59-case focused class) and unsigned Release, followed by fresh scoped review, remain mandatory. If the Mac reports a compile/setup failure, correct and rerun before interpreting behavior.
