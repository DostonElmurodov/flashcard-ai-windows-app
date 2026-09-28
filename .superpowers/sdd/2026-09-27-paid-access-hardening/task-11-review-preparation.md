# Task 11 bounded harness / copy / validation review preparation

Draft requirements for the eventual fresh GPT-6 Astra xhigh reviewer. This is not a review dispatch or an acceptance verdict. Generate the final diff package and record actual final heads before dispatch.

## Scope and bases

- iOS: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios`, base `58dd623c4d63921e8cdd720741c59a0b86526646`.
- Windows: `C:/Users/ForDo/.codex/worktrees/paid-access-hardening/FlashcardAI`, base `43ba1668a4205882b8565fa0720e7fa3fb97d67c`.
- Backend: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, base `03bb0620098cdbe6d2c21b855271e2e8b968e029`.
- Requirements: task-11-brief.md, task-11-dispatch-context.md, ios-hosted-harness-research.md and the explicit rulings in progress.md. The implementer report and runtime artifacts are claims/evidence to inspect, not conclusions to adopt.

## Required contracts

1. Real XCUI queries, taps, typing, focus, geometry and navigation replace the mapped 20 failing hosted accessibility cases and four skipped AccountView cases. No fabricated accessibility objects, new blanket skips, or weakened assertions. Preserve meaningful original state/model tests, including saved-empty edit mode, local search/no extra refresh, saved daily goal, and onboarding completion exactly once or unchanged as applicable.
2. Debug fixtures require the dedicated app bundle before app/defaults initialization, isolate persistence, and replace live account/API/StoreKit paths. Normal launch behavior remains intact. Release excludes the fixture constructor and fake providers; actual Release compilation/configuration evidence is separate from Debug tests. Read-only OS notification status inside the dedicated test container is permitted; real authorization/scheduling/account/purchase/provider operations are not part of these fake tests.
3. R5 covers rejected and eleventh-free saved rows on both actual Dashboard/Create Set routes: content/review remain locked, Delete is reachable, confirmation is real, and deletion persists after reopening the same database without reseeding. Both production call sites explicitly use the homeList card style; review the unused standard branch in source without claiming runtime coverage or adding a product style switch. An unsaved empty Create screen does not by itself replace saved-empty edit-mode coverage.
4. Copy matches known global 10 active free words across languages and D3 retained paid-expiry viewing/review. iPhone purchase/use has no mandatory Owl account; desktop linking uses the account. No unapproved numeric AI quota, money allowance, recovery guarantee or unlimited-AI promise. Existing offer/product semantics are not silently invented.
5. Validation routes run tests without publication/deployment. The backend's existing api-ci-cd.yml is not safe for a test-only dispatch. Windows smoke uses its existing isolated loopback fixture. Exact source/runtime/test outcomes are recorded; previous heads' logs are not relabeled as final evidence.
6. Failures are classified by actual evidence: build/launch harness failures are not behavioral RED; static checks are not simulator GREEN. Only remove old coverage after equivalent rendered contracts actually pass. Physical Apple/App Attest/recovery, owner/quota integration and production migration remain separate gates.
7. Manual simplify is recorded after each coding iteration. Assess the resulting structure on its merits, including dependency seams and shared fixture state. Known preexisting tool warnings do not require unrelated dependency upgrades; newly introduced or actionable warnings need assessment.

## Review boundaries

Review is read-only, with no subagents. Use separate spec-compliance and code-quality verdicts and concrete file/line evidence. Inspect unchanged call sites only for named risks. Run a focused check only for an unresolved doubt; do not repeat whole suites merely to reproduce available logs. The user also requires a separate final clean-context whole-feature three-repository review after owner/quota integration; this bounded review cannot close that gate.

The optional Windows/backend artifact-upload patch was rejected by automatic approval review and was not applied. The controller deferred it; local evidence and CI stdout remain. Do not recreate or work around that rejected action as part of review.

## Runtime/configuration evidence to carry into the eventual package
At exact8e3db01, unsigneddeviceRelease run36456654566 passed and sourceartifact matched. Local evidence task11-results/36456654566-release-8e3db01 includes xcode-build.log/full.log/run.json/source-head.txt/hashes; actual bounded checker completion is full.log4689. This is not signedentitlement or physicalApple proof. Source mapping for staticbearer/transport risk: unchanged Infrastructure/Networking/APIConfiguration.swift17-23 and28-40 confine localhostHTTP and Plist/envbearer overrides toDEBUG. Warnings present in Release: existing APIClient.swift35 redundantawait (alreadyatTask8base58dd623); optionalAppIntentsmetadata extraction skipped. Assess actionable warning implications on their merits without conflating them with a build failure.

At exact8e3db01 UI run36456650439 executed28cases26pass2fail (4assertions,577.664s), sourceartifactverified. Allfour additional Task8R5 reason-by-screen deletion cases passed through actual confirmation, sameDBrelaunch and absence; allthree Pro bothflagsfalse-to-false persistentrelaunch cases passed. Remaining Addgeometry andsavedempty cases are executable realRED, not skipped or waived. Corrected removal sequencing: their duplicated oldhostedAX bodies may be retired with the mappedother22 if these realassertions remain blocking and nextfullsuite verifies the repairs; a wholeextraRED/fullrun solelybecause deadAXbodies remain is unnecessary. Newsearch-reset/statusbar/onboardingexact-once/email-state checks also mustpass in finalfullsuite. Exact laterhead/evidence will supersede this checkpoint for acceptance.
