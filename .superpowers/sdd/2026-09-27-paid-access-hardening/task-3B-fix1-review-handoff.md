# Task 3B fix round 1 scoped re-review handoff

Prepared for a fresh clean-context GPT-6 Astra xhigh reviewer. Dispatch only after final evidence and the frozen manifest exist. This file is not a review verdict.

Repository: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios`.
Previous reviewed source: `544c16e1173c3fe2b219be050bd6416d3e2b89b2`.
Candidate: `79d1a83d4868719db969dc4739183398cff5681b`.
Frozen fix diff: `task-3B-fix1-review-package-544c16e-79d1a83.txt` (9 files, 127907 bytes; SHA256 `33D4F617FADDDE3B5AE0008546FA200D5A01D745928724F7660AEA64964B83E6`).

Read the original `task-3B-source-review-544c16e.md`, `task-3B-fix1-brief.md`, original `task-3B-brief.md`, `anonymous-restore-contract.md`, and `anonymous-restore-approved-behavior.md`. The fix brief's opening prepared/billing-block status is historical. Current execution is documented in the final frozen fix report and evidence index. The old approved contract remains binding.

## Findings to verdict individually

- I1 — Invalid keys discovered during normal protected requests cannot enter recovery.
- I2 — Old unfinished product evidence persistently blocks the current valid product.
- I3 — Foreground and Check access no longer refresh linked-account entitlement.
- I4 — Token success erases failed Apple and reconciliation checks.
- I5 — Preprocessor waits/errors guard identity but not original operation order; old errors overwrite newer success.
- I6 — Fake coordinator tests clear real token/purchase-context Keychain despite injected session storage.
- M1 — Semaphore ordering test passes through timeout without proving newer success before old failure release.

Use the full original report for exact scenarios and evidence. Inspect the complete fix diff for new breakage. In particular, verify the composition repaired after actual Mac failures: sequential same-operation evidence, old foreground after newer accepted grant, key-replacement rediscovery versus stale work, and current ACK followed by same-original reconciliation denial. These are not separate new feature requirements. The current/account test expectation changed because the frozen contract gives an independent active mobile purchase priority over an unrelated healthy account; logout/relaunch assertions remain.

Do not reopen unchanged accepted backend ownership/reconciliation or Windows policy. Task 6A/6B, local legacy server migration closure and final whole-feature review are still pending. Whole paired-envelope replay is the approved bounded mobile residual; ordinary restore must not become dependent on Owl login, old installation credentials or support. Do not treat fake simulator coverage or an unsigned Release build as physical App Attest/Apple/iOS17 proof.

## Evidence verification and limits

Treat the implementer's report as claims. Verify the frozen manifest, read the actual selected source artifacts and results, and reconcile claims with the diff. Historical setup/compiler failures must not be called behavioral RED. I6's isolation fix deliberately has no destructive real-Keychain baseline run. The b0 regression was observed and repaired; the final focused runs must cover it.

Read the diff package once rather than re-deriving the Git range. Read surrounding unchanged callers only to answer concrete questions raised by the fix. Do not rerun suites to duplicate saved evidence. If a specific doubt cannot be answered by source or existing evidence, report it or run only an authorized local focused check. No Mac dispatch, network/provider/Apple call, production access, Git mutation or product edits. Do not spawn agents.

Write the full review to `task-3B-fix1-review-79d1a83.md`. Verdict every finding ADDRESSED/NOT ADDRESSED with file:line evidence; list new Critical/Important/Minor breakage in the fix diff separately; list unchanged out-of-scope observations separately. Distinguish source-established defects, observed runtime failures, and unverified external gates. State overall scoped verdict and exact checks performed. Return only the verdict, open findings and report path to root.
