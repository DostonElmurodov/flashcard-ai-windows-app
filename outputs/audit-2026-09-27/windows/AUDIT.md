# Windows commercial access audit, 2026-09-27

Source baseline: `aba59fb8f5e829d2cd10065fd702cff2dc7223b3` (verified with git rev-parse). Read-only production-source audit. Reproduction artifacts are confined to this directory. No live AI requests, purchases, production credentials, or external-service writes were used.

## Finding: normal mode does not enforce a commercial local-word cap

`electron/main.ts:69` sends validated batches of up to 2,000 drafts directly to `Store.addWords`. `electron/store.ts:70-74` accepts them without account status, entitlement, or test-mode input. Manual entry, pasted/file imports (`src/CreateSet.tsx:17-29`), and catalog imports (`electron/main.ts:99`) share this unrestricted path. Repeated batches can grow the library beyond 2,000; that number is a per-operation input-safety cap, not an account quota.

`Store.queue`, `electron/store.ts:80-95`, enforces the user-selected daily new-word allowance, not a subscription word limit. `electron/main.ts:78` lets every workspace set dailyGoal up to 200; it does not inspect entitlement. Existing due reviews are not counted against this allowance. Therefore disabling test mode restores dailyGoal scheduling, but does not turn on a free-account commercial library cap or lock already-stored words.

Reproduction: `windows-audit-repro.test.ts` first test confirms a live stub response with `test_mode:false`, then stores 100 parsed-import words plus one manual word in both guest and account-owned Stores. With dailyGoal=200, both normal-mode queues expose all 101 words. `reproduction.log` contains exact output. This is a confirmed behavioral gap if the product requirement is to cap free/unpaid vocabulary on Windows; README.md:17 explicitly says local study requires no account, so the intended commercial policy needs to be distinguished from the present documented behavior. No billable AI is involved in this reproduction.

## Reviewed cost boundary: Windows AI requests delegate to protected server endpoints

`electron/api.ts:37` requires an authenticated session for every request. `translate` at line 42 posts only to `/owlai/account/ai/word-detail`; `reviewTranslation` at line 43 uses `/owlai/account/ai/review-translation`. It never sends a client test-mode or entitlement value to grant server access, and never embeds an AI provider key. An unpaid signed-in account can attempt ordinary translation without a local paywall, but the server decides authorization. The second reproduction injects a fake local session and stubs fetch: exactly one protected endpoint request is sent, the stub returns 402, and the client surfaces the Premium error. This proves delegation, not a deployed production authorization check.

`backend/Areas/OwlAI/Controllers/AccountAiController.cs:10-14` uses account authentication, account-usage rate limiting, and AccountAiProtectionFilter. The root audit separately covers backend authorization, AI quotas, and legacy endpoints. Windows does not use legacy device-AI routes. File OCR in `electron/files.ts:11-23` uses local Tesseract/PDF tools; normal text/image/PDF imports are not routed through paid backend AI. Filling missing translations in CreateSet uses protected Api.translate once per missing selected row; it stops on thrown rejection.

## Feature-flag/cache/offline observations

`electron/feature-flags.ts:4-29` starts false, ignores old cache files, validates HTTPS/loopback origin, requests no-store/no-redirect, accepts only an actual boolean, binds confirmation to the current origin, and sets false on failed/malformed responses. Existing feature-flags tests verify restart, offline/malformed, late origin response, and false-after-failure behavior. The renderer exposes no direct test-mode setter (`electron/preload.ts:7`, main validators).

`src/App.tsx:43` refreshes flags plus entitlement on account/origin change, focus, and every 60 seconds. There is no age/TTL on the previous in-memory true value (`feature-flags.ts:16`), and it remains true while a refresh is pending. A completed false/failure revokes it. This can leave temporary stale UI/local Review allowance during a suspended/throttled renderer or pending request; it does not alter the current server-side commercial gate. Third reproduction verifies the pending-refresh behavior.

`electron/api.ts:25,28,34` clears entitlement on login/change/logout; generation checks in request/refresh prevent late session responses crossing account transitions. `main.ts:23-40,48-61` selects account-specific workspaces, stops old sync, and uses scope checks around IPC. `workspace.ts:7-24` hashes origin and immutable account ID and validates Store ownership. Signed-in server changes are blocked in main.ts:78. Entitlement is not persisted as a paid offline grant; failed refresh clears it (`api.ts:39`). Status-only client checks do not locally evaluate expires_at, but billable requests are still server protected.

Secondary-language content is intentionally readable from cache before entitlement checks (`electron/review-translations.ts:42-45`), including after revocation. Cache keys include origin, workspace owner, normalized source, source languages, and target (`:19`); misses require sign-in and active premium/trial/grace or confirmed test mode (`shared/secondary-review.ts:16-19`). Pending requests deduplicate by scope/key and guard late results against identity/content changes (`review-translations.ts:46-54`). Fourth reproduction confirms a revoked user reads a previously generated cached response with zero new AI requests. README.md:54-56 documents this offline behavior; it is not an unlimited-new-AI bypass.

## Verification

- All four new audit reproductions passed: reproduction.log.
- Existing Windows test suite: 86 tests, 85 passed, one failed due to EPERM while renaming the temporary SQLite file in the large-sync test's cleanup (`tests/account-sync.test.ts:82`, `Store.close`). The run used isolated TEMP/TMP and --test-concurrency=1: existing-tests.log.
- Focused rerun of that large-sync test hit the same cleanup EPERM: sync-rerun.log. Full-suite green status cannot be claimed; cleanup can obscure an earlier failure in that test.
- Typecheck result is captured in typecheck.log.
- Final root verification: reran the entire existing suite outside the restricted filesystem sandbox, with a separate TEMP/TMP directory and concurrency=1. **86/86 passed**, exit code 0 (`elevated-tests.log`). The earlier EPERM failures were not reproduced in this run; production source was unchanged.
- No applicable AGENTS.md or CLAUDE.md found in the Windows project or searched ancestor directories.

## Reviewed source inventory

Deep reads: electron/feature-flags.ts, api.ts, main.ts, preload.ts, store.ts, workspace.ts, review-translations.ts, imports.ts, files.ts, sync.ts, sync-scheduler.ts; shared/types.ts, secondary-review.ts; src/App.tsx, CreateSet.tsx, SecondaryReview.tsx, Library.tsx; README.md test-mode/secondary-review sections, index.html, package.json, tsconfig.json; backend/Areas/OwlAI/Controllers/AccountAiController.cs and AccountSyncController.cs, backend/src/Mavrylo.Services/Services/AccountSyncService.cs (backend root owner handles full findings).

Focused reads/searches: src/Profile.tsx, Help.tsx, Settings.tsx, Review.tsx, electron/google.ts and supporting test-mode/review/sync tests. Existing suite executed all tests/*.test.ts. All electron/src/shared TypeScript was searched for network, entitlement, test-mode, word-limit/quota, and apiBase references.
