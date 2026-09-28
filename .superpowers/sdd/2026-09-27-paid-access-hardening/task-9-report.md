# Task9 report — DONE

## Scope and exact commit

- Worktree: C:/Users/ForDo/.codex/worktrees/paid-access-hardening/FlashcardAI
- Branch: codex/paid-access-hardening
- Clean implementation base: aba59fb8f5e829d2cd10065fd702cff2dc7223b3
- Final local commit: c05b03c2abd011b7232adde395350c36dbe3a231 — feat: enforce Windows paid access and retained content policy
- Final worktree status: clean; local commit only. No push, deploy, release, production login or real provider calls.
- Backend contract reference: accepted Task4 commit5c875dfe0f05b076abcda6da86e5ebd151652970 and backend/docs/apple-subscription-validation.md in the plan test-results backend worktree.
- Original D:/07 Hobby/FlashcardAI product files were not edited. Only the requested report/evidence under the source checkout were written.

## Implemented behavior

Shared evaluateAccess checks desktop account identity, effective entitlement expiry, and a bounded five-minute live confirmation. A true test flag is trusted only from the main-process live selected server, has the same bounded lifetime, is never persisted, and is invalidated on origin transitions/flag request failures. Feature flag and API responses are generation-protected across origin round trips; HTTPS/custom and loopback selection remain supported without an invented allowlist.

Fresh premium/trial/grace or live test access with an account permit mutations. Free allows local additions below the global10-card count and content edits to eligible first10. Expired_trial, revoked, invalid_subscription, unknown inactive statuses deny add/edit/AI. Ordinary expired_paid keeps all saved paid reading/review, forbids AI/add/content edits, and allows deletion/export. Nonpaid saved eligibility is globally ordered by chronological createdAt then ordinal ID, independent of deck/language. Excess content is preserved rather than deleted.

Store binding comes only from main-process Api.state; IPC schemas do not accept entitlement/testMode as authority. Manual, pasted/file staged saves and catalog imports reuse guarded addWords; batch failures roll back every insert, and catalog deck+cards now share one transaction. Deck language/content edits cannot change locked cards. Study activation is allowed as review configuration. Queue, previews, review replay, and cached second-language reads check card eligibility. Late AI results must still match store/scope/card identity and current AI authority before cache write.

Saved read-only entitlement history lives in the existing origin+immutable-account hashed Store.owner workspace. Relaunch uses history without a fresh confirmation, so paid content survives offline while no paid mutation grant is restored. Fresh revoked/invalid/free responses replace older paid history and remain authoritative offline. Malformed active expiry cannot become a paid read grant. Restore preserves the current workspace's access history rather than adopting a backup's stale grant; sync/backup content and review data remain intact.

HTTP errors preserve status/code/retry metadata in ApiError. Retry-After parses delta seconds or supplied HTTP dates; missing timing gets generic later-retry copy.402 directs subscription linking;429 states supplied wait;503 temporary failure differs from subscription_reconciliation_required (verification/recovery/support, preserved data, no repurchase prompt). AI401 refusals are not automatically replayed; no provider-affecting retry loop was added. Response parsing is pure, and error-side entitlement updates occur only after the captured generation matches even after awaiting a delayed body.

Client checked_at is stamped when an entitlement response is received and accepted. This is local confirmation freshness, deliberately distinct from backend checked_at that can describe an older Apple validation. expires_at remains the authoritative period bound. React uses shared policy for disabled forms, hidden locked card content, review eligibility and Premium display. Profile distinguishes an ended period from stale confirmation requiring refresh; it does not claim Apple expiry solely from stale freshness. Desktop onboarding copy reflects the account requirement, while mobile purchase requirements were not changed.

## TDD and manual simplify record

Detailed initial actual RED/GREEN excerpts are archived in task9-results/initial-tdd-evidence.md; subsequent regression logs are retained in full.

1. Policy/flags cycle: wrote stale/expired/global10 and origin-roundtrip tests first; observed assertion failures after compilable interface scaffold, then10/10 green. Manual simplify: one shared TTL and active/retained/eligible helpers; one current-generation pending flags request, no durable flag cache.
2. Store cycle: missing bindAccess proved guards absent; implemented transaction-bound admission and durable read-only history. A fixed-clock integration fixture initially failed because production store uses current time; corrected fixture clock, then42/42 store/domain/sync/review tests. Manual simplify: reuse addWords/saveDeck in one outer catalog transaction, count only new deduplicated words, preserve restore policy metadata.
3. Error cycle: error.status expected429 but undefined before implementation. Added ApiError, standard header timing and no AI replay; fake-server tests green. Existing premium review fixtures were updated with finite expiry/live confirmation; an assignment typo was corrected and typecheck passed. Manual simplify: centralized response/error formatting; no provider retry helper.
4. Main/renderer cycle: inactive below10, locked cached reads and expired-paid form tests were red first. Added main binding, atomic import, eligibility guard and shared UI policy;17/17 focused green. Manual simplify: renderer remains presentation only; reused the established owner hash, account epoch and review cache key.
5. Saved-history self-review cycle: malformed expiry relaunch, locked deck edit, and revoked late-AI tests failed as expected. Fixed those cases and explicit reconciliation state persistence;29/29 focused green. Manual simplify: reuse shared eligibility/AI checks; remove checked_at from review request dependencies to prevent periodic refresh replay of denied AI requests.
6. Simplify-only pass: tightened paid-history boolean validation, removed duplicate mutation branching, reused queue snapshot words for eligibility, avoided duplicate history writes, and hid newly locked cards in an open review. Focused29/29 and full101/101/typecheck/build passed.
7. Local smoke cycle: added one reproducible loopback-only Electron paid-access smoke, preseeded isolated workspace settings before startup. Passed forged-renderer denial/global10/atomic saves/paid101/offline relaunch. Manual simplify: shared IPC call helper and fake-only transport; no production credentials or special product test hook.
8. Controller race cycle: deferred-body-red.log reproduced an old503 reconciliation mutating a new account. Moved state mutation out of parsing into generation-guarded request handling; deferred-body-green.log13/13 passed. Existing smoke harnesses initially failed due mismatched local browser version/cold navigation timeout/unactivated IPC scope; corrected navigation wait and explicit scoped bridge, then React and all4 file formats passed. Manual simplify: pure result parser, one captured generation around success/error bodies; reuse normal snapshot/activate/forWorkspace APIs in smoke.
9. Chronological order cycle: creation-order-red-scoped-temp.log8 pass/1 expected fail across timezone offsets; replaced lexical date ordering with instant ordering and ID tie. A first run omitted isolated TEMP/TMP and also hit known Windows temp EPERM; rerun before fix isolated the assertion. Manual simplify: one timestamp normalization helper; focused23/23 and full103/103/typecheck/build green.
10. Profile display cycle: profile-red.log3 pass/1 fail before adjacent helper reuse; profile-green.log4/4 passed. Manual simplify: activeAccess drives actual access display, only period/refresh copy is separately classified; no duplicated authority rule. Final full104/104, typecheck, build and final actual Electron smoke passed.

## Final commands and retained evidence

All final npm tests/builds used TEMP and TMP=D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/windows-temp. Evidence directory is D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task9-results.

- npm test:104/104 pass,0 failures/skips — final-full-tests.log
- npm run typecheck: exit0 — final-typecheck.log
- npm run build: exit0, renderer and Electron bundles produced — final-build.log
- npx tsx --test tests/access-policy.test.ts tests/api-test-mode.test.ts tests/review-translations.test.ts:23/23 pass — final-focused.log (subsequent Profile-specific4/4 in profile-green.log; full104 includes all)
- npx tsx scripts/paid-access-smoke.ts with OWL_ACCESS_RESULTS=<evidence>/electron-smoke-verified: PASS — electron-smoke-verified.log. Actual Electron scoped IPC denied forged grants, enforced global10 across languages, rolled back manual/staged/catalog saves, preserved101 paid cards, allowed expired-paid cached reading/review/deletion/export/backup, relaunched offline with100 after authorized deletion and denied additions. Exactly one local fake AI request; no real AI. Profiles/backup/export/screenshot under electron-smoke-verified.
- npm run test:secondary-ui with OWL_TEST_BROWSER=C:/Users/ForDo/AppData/Local/ms-playwright/chromium_headless_shell-1228/chrome-headless-shell-win64/chrome-headless-shell.exe: PASS — secondary-ui-final.log. Real React deterministic IPC: settings, reveal-only second-language requests, nonblocking grading, user retry, stale card/language/disabled result isolation, AI preview before save and late save handling.
- npm run test:imports with isolated import-profile preseeded to http://127.0.0.1:9: PASS CSV/text PDF/PNG OCR/scanned PDF,2 drafts each — import-smoke-final.log. No production login or provider call. The earlier scope error and browser/cold navigation failures remain in separate logs; they were resolved rather than omitted.
- git diff --check / git diff --cached --check: exit0. Git emitted normal LF/CRLF conversion notices; final test/build outputs contained no failing warnings.
- Local commit c05b03c2abd011b7232adde395350c36dbe3a231; git status --short empty after commit.

## Files changed

- electron/api.ts, electron/feature-flags.ts, electron/main.ts, electron/review-translations.ts, electron/store.ts
- shared/access-policy.ts (new), shared/secondary-review.ts, shared/types.ts
- src/App.tsx, src/CreateSet.tsx, src/Profile.tsx (adjacent effective-expiry display), src/Review.tsx
- tests/access-policy.test.ts (new), tests/access-ui.test.ts (new), tests/api-test-mode.test.ts, tests/feature-flags.test.ts, tests/fixtures/secondary-review.tsx, tests/review-translations.test.ts, tests/test-mode-ui.test.ts
- scripts/paid-access-smoke.ts (new reproducible loopback fake), scripts/import-smoke.mjs (workspace scope), scripts/secondary-review-ui.mjs (cold navigation timing)

## Self-review, limits and remaining controller work

Self-review covered the complete diff, state matrix, transaction rollback, owner/origin isolation, inactive-history replacement, real cached/offline behavior, response generation before and after body parsing, no AI replay, UI status/disabled controls, and preserved sync/backup data. Findings listed above were fixed with red/green coverage. No outstanding implementation finding known.

This proves the local Windows policy and fake backend error/DTO compatibility, not live Apple/Task4 deployment, App Review, production ownership enforcement, real AI pricing or quota values. Existing main/store files are compact but large; changes followed their current style without an unrelated refactor. Standalone Store users/tests can bind a main-owned policy; the running application always binds it during workspace selection, never from renderer arguments. Backend remains authoritative for production AI. No anonymous-owner redesign, monetary/D2 quota guesses, push/deploy/release, additional subagents or reviewer was used. The controller's separate clean-context Astra xhigh review remains the next step; it was not performed by this implementer.

# Task9 fix round1 — DONE (fresh controller review pending)

## Review findings and exact fix commit

Read task-9-review.md in full plus task9-results/reviewer-focused-probe.ts/.log. Fixed the reviewed P1/P2/P3 only, in the same isolated Windows worktree on codex/paid-access-hardening. Source base for this round was c05b03c2abd011b7232adde395350c36dbe3a231.

Final local fix commit: f0a08c0da30f695f7a5cbe5ed80efafb70d177fa — fix: order entitlement refreshes and guard cached draft previews. git status --short was empty after commit. No push/deploy/release, subagents, real provider calls, production login, quota changes, mobile-account requirements, or unrelated product changes.

## P1: same-account overlapping refresh authority

Implemented one monotonic entitlement refresh revision in addition to account/origin generation. Only the latest-started applicable refresh may accept entitlement success or clear/replace it on error. Api.request also captures the entitlement endpoint's revision and checks it before402/503/reconciliation error-side mutations after reading the body; outer refresh success/catch checks alone are not relied on. Older refreshes superseded while feature flags are pending do not launch another entitlement request. Direct current AI errors still revoke current authorization exactly as before.

TDD:
- npx tsx --test tests/api-test-mode.test.ts: fix1-p1-red.log,5 pass/1 expected stale premium failure.
- Split the three cases so all failed independently before implementation: fix1-p1-red-all.log,5 pass/3 expected failures. Old premium replaced newer revoked; old temporary503 cleared newer premium; old reconciliation503 replaced newer premium with invalid_subscription.
- After implementation, npx tsx --test tests/api-test-mode.test.ts tests/feature-flags.test.ts: fix1-p1-green.log,15/15 pass.
- Extended the same parameterized regressions to receive old headers first, pause the old response body, accept the newer authority, then finish the old success/error body. npx tsx --test tests/api-test-mode.test.ts: fix1-p1-deferred-body-green.log,8/8 pass.
- Existing direct HTTP denial test was strengthened as a positive control: each current AI request starts with premium;402 clears it, reconciliation503 makes it inactive,429 leaves premium intact. All cases passed in final focused evidence.

Manual simplify pass after P1: reuse the existing generation checks and add one revision; no parallel cache, queued entitlement transport, retry helper, or renderer authorization parameter. The request-side guard is scoped to the entitlement route, retaining legitimate direct AI error behavior. The deferred-body test extension reuses the existing fake response/session fixtures rather than a new production test hook.

## P2: cached draft-preview bypass

Draft preview now checks the existing shared reviewTranslationAccess in its context resolver before any cache read. The resolver also participates in existing late-response revalidation. Saved-card get still performs assertEligible and may return its cache without new AI when D3 permits; ordinary expired-paid saved cache reads remain available through that route. No word/language matching escape hatch was added.

TDD:
- npx tsx --test tests/review-translations.test.ts: fix1-p2-red.log,9 pass/1 expected missing rejection. A cached draft preview returned despite ended paid access. The supplied reviewer probe independently reproduced the revoked eleventh-card alternate preview path before this fix.
- npx tsx --test tests/review-translations.test.ts tests/access-policy.test.ts: fix1-p2-green.log,19/19 pass.
- Coverage primes one cache while premium, proves expired-paid saved get remains readable, then denies draft-preview cache access for expired_paid, revoked locked content, elapsed active expiry, and signed-out state, with exactly one original fake AI request.

Manual simplify pass after P2: reuse the shared authority helper and existing context/load/cache implementation. Keep saved-card eligibility and active draft authority explicit at their existing entry points; no new cache store, paid-history mechanism, or secondary saved-card search.

## P3: desktop signed-out copy

Profile now says: 'Sign in to create or add cards on desktop. You can still review eligible saved local cards in this separate workspace.' The old unconditional signed-out creation promise was removed. iPhone account-optional purchase/use remains unchanged.

TDD:
- npx tsx --test tests/test-mode-ui.test.ts: fix1-p3-red.log,4 pass/1 expected missing account-required creation copy.
- npx tsx --test tests/test-mode-ui.test.ts tests/access-ui.test.ts: fix1-p3-green.log,6/6 pass, asserting accurate creation/eligible-review copy and absence of the old promise.

Manual simplify pass after P3: replace only the existing sign-in paragraph and use the existing static Profile rendering setup. No new UI state or component.

## Final verification for this fix round

All file-writing, test and build work stayed in the authorized isolated worktree. Tests that use temporary databases ran with TEMP/TMP=D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/windows-temp. Evidence remains outside the worktree in task9-results.

- npx tsx --test tests/api-test-mode.test.ts tests/feature-flags.test.ts tests/review-translations.test.ts tests/access-policy.test.ts tests/test-mode-ui.test.ts tests/access-ui.test.ts:40/40 passed,0 failed/skipped. Exact retained log: fix1-final-focused.log. Covers the changed API/cache/Profile areas, shared policy, flags, scoped/offline history and UI guards.
- npm run typecheck: exit0; fix1-typecheck.log.
- npm run build: exit0, renderer and Electron bundles rebuilt; fix1-build.log.
- npx tsx scripts/paid-access-smoke.ts with OWL_ACCESS_RESULTS=<task9-results>/fix1-electron-smoke: PASS; fix1-electron-smoke.log. This targeted actual Electron rerun was justified because refresh and cached-read paths changed; retained saved paid reads/offline relaunch, atomic saves and forged renderer denial still passed with one fake AI request.
- git diff --check and git diff --cached --check: exit0. Final six-file diff inspected before commit; working tree clean afterwards.

The previous104/104 full-suite evidence in final-full-tests.log and prior React/file-import smoke evidence are preserved, not claimed as fresh runs of this fix commit. Per controller direction, unrelated full-suite/import/React runs were not blindly repeated: affected tests, typecheck/build and the targeted changed-path Electron smoke provide current round evidence. None of the imports/sync storage/scheduler/UI fixture code changed in this round.

Files changed: electron/api.ts, electron/review-translations.ts, src/Profile.tsx, tests/api-test-mode.test.ts, tests/review-translations.test.ts, tests/test-mode-ui.test.ts. No additional unresolved implementation finding known. The controller's fresh scoped review remains next; this implementer did not spawn a reviewer.

# Task9 fix round2 — DONE (fresh scoped review pending)

## Scope and commit

Read task-9-fix1-review.md in full, plus task9-results/fix1-reviewer-authority-probe.ts and its log. This round fixes only residual P1; already accepted P2/P3 are unchanged. Isolated worktree/branch remain C:/Users/ForDo/.codex/worktrees/paid-access-hardening/FlashcardAI, codex/paid-access-hardening.

Round base: f0a08c0da30f695f7a5cbe5ed80efafb70d177fa. Local fix commit:43ba1668a4205882b8565fa0720e7fa3fb97d67c — fix: invalidate pending entitlement refreshes on direct denial. Final git status --short empty. Only electron/api.ts and tests/api-test-mode.test.ts changed (24 insertions,2 deletions). No original product checkout edit, subagent, push/deploy/release, production login, real AI/provider request, quota/money change, mobile-account change or unrelated expansion.

## Residual P1 fix and TDD

Accepted current direct authority-denying responses now advance the same entitlement revision before clearing/replacing entitlement. This invalidates every earlier in-flight entitlement confirmation, including a premium body that began arriving before the direct denial was accepted. Direct402, explicit subscription_reconciliation_required503 and temporary503 all use this path because all already revoke current mutation freshness. Entitlement-route stale failures still require their captured revision, and account/origin generation protection remains unchanged.429 does not revoke entitlement or advance this revision. A genuinely later entitlement refresh starts a newer revision and may confirm lawful Premium.

RED before product fix:
- Command: npx tsx --test tests/api-test-mode.test.ts
- Retained exact log: task9-results/fix2-p1-red.log
- Result:8 passed,3 expected failures. New direct payment402, reconciliation503 and temporary503 cases each accepted denial first, then the older delayed premium body restored entitlement instead of preserving null/invalid_subscription. Each failed deep state equality before reaching its later-refresh positive control. This reproduced the reviewer sequence without any network or provider.

GREEN after fix:
- Command: npx tsx --test tests/api-test-mode.test.ts tests/feature-flags.test.ts tests/access-policy.test.ts
- Retained exact log: task9-results/fix2-p1-green.log
- Result:27/27 passed,0 failures/skips. All three delayed-premium/direct-denial cases now preserve denial and add-at101 remains blocked. Each then starts a genuinely later fresh entitlement request and proves premium/add-at101 can be restored lawfully. Previous same-account refresh ordering, delayed error-body account isolation, direct current denial handling,429 retention, flag generation/TTL and saved-content policy/history tests also pass.

Manual simplify pass for this one coding iteration: grouped the existing402/503/reconciliation authority-mutation branches so revision invalidation is written once. Reused the existing entitlementRevision and account-generation checks; introduced no new state store, counter, request retry or authorization source. Tests use the established in-process fake fetch/session and parameterize the three existing denial semantics. The accepted preview/copy files and their tests were not modified.

## Final round verification and evidence

TEMP/TMP=D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/windows-temp for tests/builds.

-27/27 focused tests: fix2-p1-green.log (command above).
- npm run typecheck: exit0, no diagnostics; fix2-typecheck.log.
- npm run build: exit0, renderer and Electron bundles generated; fix2-build.log.
- git diff --check and git diff --cached --check: exit0; complete two-file diff self-reviewed before commit. Git conversion notices only; clean working tree after commit.

Per controller instruction, no unrelated full-suite/import/UI/Electron smoke reruns were performed. Earlier full104/104, fix1 affected40/40 and fix1 Electron smoke evidence remain preserved with their actual source commits; they are not presented as fresh round2 executions. This round changes only direct denial revision invalidation, which is exercised by the three deferred-body regressions and existing direct-denial controls. Live Apple/backend deployment and real provider behavior remain unverified and out of scope.

No unresolved implementation finding known after this bounded fix. Fresh scoped Astra review is the controller's next step, not performed by this implementer.
