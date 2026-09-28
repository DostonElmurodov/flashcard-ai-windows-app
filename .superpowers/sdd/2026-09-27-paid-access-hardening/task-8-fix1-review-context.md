# Task8 fix round1 scoped re-review context

Prepared for a fresh clean-context GPT-6 Astra xhigh reviewer. This file is not an approval. Dispatch only after current covering Mac results are appended below.

## Scope and inputs

Worktree: D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios.
Fix base is the previous review's head: 4bb69cb20a82a14a4c0510bbefbadf5308f4bfae.
Fix head: 58dd623c4d63921e8cdd720741c59a0b86526646.
Diff package: task-8-fix1-review-58dd623.txt (commits, stat, complete diff with10 lines of context).
Read task-8-fix1-brief.md, findings in task-8-review.md, and the fix sections at the end of task-8-report.md. Original task requirements are task-8-brief.md and task-8-review-checklist.md. Historical task-8-review-context.md describes the earlier4bb review, not current-head test results.

Verdict R1–R5 and M1 individually. Also inspect the fix diff for new breakage, including its changed shared authority ordering and retained-detail path. Read-only checkout: no product/index/HEAD/branch edits, pushes, CI dispatch, live calls, or subagents. Read the supplied diff rather than rebuilding a whole-branch package. Do not rerun broad suites. A focused probe is justified only by a specific unanswered concern. Write task-8-fix1-review.md in this SDD directory; return per-finding ADDRESSED/NOT ADDRESSED with file:line, any new Important/Critical breakage, out-of-scope observations, and explicit scoped spec/quality verdicts.

## Binding behavior

- iPhone purchase/use requires no Owl login. Account enables desktop; anonymous-owner/recovery redesign remains separate Tasks2/3. Preserve linked-owner protections and do not claim B1/B2 solved here.
- Ordinary paid expiry retains all saved reading/review and delete/export; new AI/add/content edits are denied. Free global oldest-ten eligibility, expired-trial/revoked no-new-mutation policy and invalid/unknown fail-closed behavior remain unchanged.
- Mutations require a finite, unexpired, process-live entitlement response within5 minutes. Retained snapshots never restore mutation freshness. Account, IAP, verification, optional claim and direct AI paid-denial responses must respect request ordering and identity; genuine later lawful recovery must work.
- Nil-account no-ops and ordinary transport failures are not new positive server entitlement decisions. An optional account transport failure may invalidate its own account freshness without revoking an independent guest purchase or masking that purchase's pending real paid denial. Stale errors cannot overwrite newer UI/authority state.
- Saved eligible detail remains readable without a new provider attempt when paid/new-AI authority is unavailable. Locked/rejected/excess cards remain blocked from review/edit. Their Delete action must be reachable through the existing confirmation/callback path.
- Preserve real purchase/auth defaults, API session guards, release behavior, per-account read history and lossless accepted:false handling. No synthetic test shortcut may serve as live ownership proof.

## Additional findings and compatibility checked during this fix

The original implementer identified claim's direct linked-marker/account-history write after an awaited backend claim. Root identified two new-ordering paths that can mask an in-flight paid denial: signed-out account refresh calling setAccountEntitlement(nil) while already nil, and ordinary optional-account error advancing shared accepted authority while independent guest freshness survives. Each has actual Mac RED in the recorded run below before its functional fix. Claim now applies marker/body/history under one original accepted ticket; verify this, identity guards, stale error behavior and later claim control from the diff.

The first R4 patch treated every saved single translation as a full cache hit and broke five existing authorized AI enrichment/rename/staging tests. Root traced the app callers: WordSearchView's button explicitly says Translate with AI; ScanWordsSheet also calls the resolver when saving selected generated words. Controller ruled to preserve this explicit authorized enrichment behavior, while returning existing eligible detail when the local new-AI check specifically returns402. Other errors propagate, and secondary generation retains its existing guard. The five prior test expectations were preserved. Evaluate the actual resulting code and requirement compatibility; this ruling is not a direction to suppress a finding.

## Evidence boundaries

All logs below are under D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task8-results.

- Initial review RED: run36358386646 at6c54ed460cb4e74b2cb77117b952a043b9247452, successful build,115cases=110passed5failed0skipped,21assertions0unexpected. Files ci-fix1-red-run/full/cases/summary. R1–R4 reproduced; transient/opposite-order/deletion controls passed.
- Full intermediate run36359667987 atf2caf76ac0c4eb9ca7773a7f29d2990b2dfd5d62 (source-head line111), successful build,610cases=578passed28failed4skipped,49assertions0unexpected. Files ci-fix1-check1-run/full/cases/summary. Earlier R1–R4 regressions passed; Entitlement35/35, Review28/28, Shared52/55. Three additional ordering RED cases and five V10 compatibility failures are listed in summary; other20 failures match baseline hosted UI cases. No new SharedAccountSync unused-result warnings or SQLite unlink warnings were found. This is not GREEN.
- Current full run36360459409 at58dd623c4d63921e8cdd720741c59a0b86526646 is pending when this context is prepared. Its actual result must be appended before dispatch; do not infer success from the prior run.

R5 persistence controls passed on Mac; actual taps/confirmation/disappearance for locked rows in both Dashboard/Create Set remain Task11 XCUI evidence. Root permitted the small UI affordance change to be checked by actual source/callback plus repository controls here because the old in-process hosted AX harness is demonstrably broken. No artificial AX/test skip was introduced as proof. The20 baseline UI failures and4 existing AccountView skips are unwaived full-feature gates, as are physical Apple/App Attest, owner recovery, final three-repository review and deployment acceptance. This scoped re-review cannot certify those.

## Controller-verified current-head validation
Full Mac run36360459409 actually tested58dd623c4d63921e8cdd720741c59a0b86526646 (source-head line111), successful Xcode build. Exact parsed610cases=586passed20failed4existing skips; terminal summary reports30assertions0unexpected. All20 failed cases match fixed baseline; no new failures. EntitlementMutationBoundaryTests35/35, SharedAccountSessionTests55/55, ReviewSessionViewModelTests28/28 (118/118 together), WordRepositoryV10OwnershipTests58/58. The three claim/no-op/optional-account-error regressions and all five V10 enrichment compatibility cases now pass on this head. No new SharedAccountSync unused-result warnings or SQLite vnode/unlink warnings found. Files ci-fix1-check2-run.json/full.log/cases.json/summary.json in task8-results. The unrestricted job remains failure because20baseline hosted AX cases still fail and4AccountView cases remain skipped; Task11 gates are not waived. This covers the current fix diff and is not a separate focused rerun or real-device/Apple/provider acceptance.
