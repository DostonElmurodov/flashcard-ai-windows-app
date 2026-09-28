# Current iOS entitlement audit — 2026-09-27

## Verdict and exact scope

`test_mode=false` removes the current iOS test bypass, but does **not** imply “only actively paying subscribers have unlimited local access.” The current product deliberately permits unlimited `trial`/`grace`, retains all existing `expired_paid` words, and has the local boundary gaps below. None of these iOS findings alone demonstrates unlimited billable AI calls: every uncached AI route still goes to backend authorization/quota enforcement. The backend audit must establish the provider-spend conclusion separately.

Current audited baseline: `D:/07 Hobby/FlashcardAI/test-results/ios-batch`, clean HEAD `547dcb32b07c07291811c4a7e386ccb05344e4c2`. Latest main `9096c1e3b20fb920726bd471ed4a092783ee3cc7` was covered by reviewing all 11 downloaded changed files and comparison patches under `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta`. Entitlement, purchases, feature flags, account/session, API and WordRepository files are unchanged by this delta. Latest focused-review code retains lock filtering.

The previous report using June clone `D:/07 Hobby/Apps/OwlAI/flashcard-ai-ios` is superseded. Its evidence is preserved only as `superseded-june-*` files; its account-inheritance, feature-flag-refresh and ignored-accepted-response conclusions do not describe the current source.

## Findings

### IOS-01 — P2: stale guest subscription grants local additions indefinitely

`D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Purchases/EntitlementStore.swift:31` loads the device snapshot without checking `expiresAt`; `:24` returns persisted status without checking expiry. `D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/Entitlement/FreeLimitPolicy.swift:12` and `:24` grant unlimited add/AI intent to `trial`, `premium`, `grace` solely by status. `D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Purchases/StoreKitService.swift:155` leaves prior device status on token/entitlement refresh failure (`:162`). The nominal 14-day `hasOfflineReadGrace` at EntitlementStore.swift:67 has no caller in any Swift source.

Condition: a genuine unlinked/guest active entitlement was cached, then expired/revoked while the device cannot refresh (offline/backend failure). Relaunch loads the old active status; manual/import additions remain allowed. A successful authoritative response corrects it. Account entitlements have stronger safeguards: they are not inherited on launch; inactive/failed shared-account refresh clears account state. Once a valid shared DTO is copied into EntitlementStore, its local status also has no independent expiry timer during a long foreground session.

Impact: local access/adding limits fail open; provider calls still encounter server entitlement and finite device-token checks. Do not call this a demonstrated paid AI bypass. Repro: existing active guest snapshot with past expiry, restart offline, add >10 manual words; no purchase required during this sequence after the historical grant.

### IOS-02 — P2: free limit is per language pair while excess locking is global

`D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Repositories/WordRepository.swift:4165` counts `WHERE native_language = ? AND learning_language = ?`. The production manual add gate at `:2702`, imported save at `:2806`, and public-set import at `:2910` use this count. A free user with flags false can create ten English→Spanish cards and ten English→French cards; each pair passes its own limit. The limit can grow with more pairs. Local inserts commit before asynchronous device upsert; `:3703` and `:3739` now check `accepted:false`, but just return and do not roll back or lock the local insertion.

`D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/Entitlement/FreeLimitPolicy.swift:53` does not lock status `free`; the global oldest-ten lock query at WordRepository.swift:3531 is therefore not run for those 20 free cards. Source-extracted SQLite characterization executed with 20 rows confirms each pair count=10, global count=20.

Impact: reproducible local manual/import limit bypass and server/local disagreement. Backend free AI quota may still be global and deny further paid generation. Changing languages by itself does not establish server AI quota bypass.

### IOS-03 — P2 / policy mismatch: turning test mode off does not cap previously generated free words

`D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/Entitlement/FreeLimitPolicy.swift:53` only locks `expired_trial` and `revoked`. `free`, `expired_paid`, and `account_required` skip the excess-word lock query at WordRepository.swift:3531. A free account/device can collect >10 local cards while test mode is true, then receive false; add/uncached AI gates revert but all existing free cards remain active and reviewable. Trial/grace unlimited behavior and expired-paid retention are explicitly documented at FreeLimitPolicy.swift:46.

Impact: unlimited retention/review after test access, not continued uncapped generation. This may be intentional retention policy, but it invalidates a strict “false means no unlimited local functionality for non-paying users” assurance. The current flag notifications do reload dashboard/review; this is the actual lock policy, not the obsolete clone's stale-flag UI issue.

### IOS-04 — P2: staged AI result can be saved after permission changes

`D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Repositories/WordRepository.swift:576` resolves/stages word details with a local preflight at `:622`; pending responses are staged at `:665`. `setFlashcardSetForWordLanguagePair` at `:871` performs a transaction and inserts a new aggregate at `:973` without checking current entitlement, current count, or flag. It schedules server upsert at `:1016` after commit.

Condition: generate/stage while test mode true or active subscription, then receive false/expired/revoked status or fill the free limit before tapping Save. Save still inserts the staged card. Multiple staged details can create several local words after the grant ends. The manual/import paths have gates; this particular AI-save path does not. Account scope generation checks protect against stale account responses; they do not replace entitlement revalidation at commit.

Impact: local creation boundary bypass. Saving does not make a new AI request, so this sequence has no additional provider cost at Save. Server rejected upsert does not undo local insertion.

### IOS-05 — P2 / policy mismatch: expired-paid “read-only” and revoked locks do not protect all writes

`D:/07 Hobby/FlashcardAI/test-results/ios-batch/Domain/Entitlement/FreeLimitPolicy.swift:46` promises expired-paid words read-only; `D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Repositories/WordRepository.swift:2974` updates card word/translation/example without entitlement/lock guard. Cached word-detail retrieval `:607` bypasses uncached AI permission (reasonable for reading), but the same staged Save path `:871` can persist changes without checking read-only status. `updateWordCard` also has no device-upsert call, so rename/edit can leave server quota identity stale; root backend audit must assess identity consequences.

Impact: local read-only inconsistency. No AI provider charge from local edit. UI entry-point availability must be verified on device; repository boundary itself is unguarded. Do not infer arbitrary new-card creation from edit alone.

### IOS-06 — P2 local UI timing: entitlement transition does not invalidate a displayed review card

EntitlementStore.swift:106 recomputes persisted/published status without posting `.reviewQueueRefreshRequested`. AppRootModel.swift:134 refreshes study state before asynchronously refreshing StoreKit status; its fingerprint at `:199` consists of study/settings/memberships and excludes entitlement. Latest `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Presentation/Features/Home/Views/ReviewSessionView.swift:96` observes queue refresh; latest ReviewSessionViewModel.swift:186 rates the already displayed card before reloading the queue at `:273`. A revoked/expired-trial transition can leave a previously displayed excess card gradeable until queue reload.

Flag transitions specifically do post both notifications (ServerFeatureFlags.swift:87), and fresh queue fetching correctly excludes locked cards. Scope is a stale local screen transition, not a persistent focused-review bypass or AI expense. Runtime timing still needs device verification.

### Dormant boundary gap, not a current screen exploit

`D:/07 Hobby/FlashcardAI/test-results/ios-batch/Infrastructure/Repositories/WordRepository.swift:381` `storeTextualAnkiWord` inserts without count/entitlement checks. Whole-source caller search found only its definition and tests, no production caller. Do not present this as a currently reachable Anki-import exploit. Add a boundary guard before connecting any new importer to it.

## Controls that passed source review

* ServerFeatureFlags.swift:27 starts false, deletes legacy persisted true; :41 fetches ephemeral/no-cache with five-second timeout; :56 accepts only latest generation, HTTP200, same HTTPS origin, strict JSON Boolean. Invalid/missing/error data fails false; :81 background disable invalidates in-flight requests. No flag path writes subscription status. App/FlashCardAIApp.swift:112 refreshes foreground and every30 seconds, disables background. A previous true can remain until the next response/background; the client cannot instantaneously observe a remote change.
* Shared accounts: AccountSessionClient.swift:28 requires known active status plus parseable future expiry. AccountProfileModel.swift:23 applies only active results and clears shared state on missing/error/logout. EntitlementStore.swift:72 distinguishes account/device provenance; :106 linked purchases become `account_required` without account rather than guest premium. SharedAccountSyncTests cover expiry, unknown status, logout/relaunch, linked purchase and ambiguous legacy provenance. Independent real guest purchases deliberately survive unrelated account logout.
* AccountSessionClient.swift:122 and APIClient.swift:310 bind requests to account generation before/after response; token rotation is serialized; no stale account-response guest fallback. AccountDatabaseScope in SyncDTOs.swift:123 separates origin/owner databases; coordinator binding and sync validate owner/epoch/session before local application. Account sync can restore already-owned existing content; server decides upload entitlement.
* StoreKitService.swift:94 purchase/restore :133 require verified StoreKit transaction plus backend verification; :178 account claim selects recognized product, not revoked/upgraded, future expiry. Transaction update :196 also verifies with backend. Empty restore does not clear stale guest snapshot (IOS-01). Grace-period claim selection requiring future transaction expiry may be an availability issue, not privilege escalation.
* APIClient.swift:61,65,78,94 covers all four AI routes: review, analyze, word detail, multipart extraction. HTTPS Release protected routes require current device token/assertion, account authority uses separate `X-Account-Authorization`, errors including402 throw; recovery retries401 device session once without entitlement bypass. TokenStore finite expiry/key-bound AppAttest session prevents indefinite cached bearer use. No Release static development bearer.
* Latest focused review: `D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/latest-ios-delta/Domain/SpacedRepetition/ReviewQueueService.swift:108` re-fetches the current snapshot; :206 filters both candidate and word locks and inactive sets. Focused learning/review ignores due time deliberately; new cards still honor daily allowance. DashboardWordCard.swift:139 disables locked review; DashboardView.swift:804 guards locked row. No new lock bypass found in the 11 latest files.
* Project Release uses production AppAttest and HTTPS API; archive scheme uses Release. DEBUG-only local/simulator fallbacks do not apply to a correct Release archive. No committed StoreKit configuration wired into archive scheme was found. Actual uploaded build and App Store Connect configuration are unobserved.

## Validation and limits

Executed `current_static_repro.py` with Python standard library on Windows: **15/15 source/SQL characterizations passed**, stored in `static-repro-results.json`. These inspect current Swift source and run actual extracted count SQL against in-memory SQLite; they are not compilation, application UI, StoreKit, or Sandbox tests. The script and inventory are audit artifacts only; no sources were modified.

Parent verified existing GitHub Actions run [36255768023](https://github.com/DostonElmurodov/flashcard-ai-ios/actions/runs/36255768023) completed successfully at baseline547dcb. Its workflow `.github/workflows/ios-validation.yml` selects seven test classes covering repository translation/notes/public sets/shared session/account sync. It **does not run** ServerFeatureFlagsTests, DevelopmentWordLimitTests or the latest focused-review tests; it does not prove main9096c1e or production Apple purchase behavior. No new remote workflow/purchase was triggered. Windows lacks Xcode/iOS Simulator; no new Swift runtime tests were run here.

Existing source tests cover normal11th rejection; legacy environment bypass ignored; strict flags/failures/delayed true/cancellation; flag true→false gate behavior; cached free detail with no API; batch overlimit; shared-account isolation/expiry/provenance; queue locks. Missing runtime regression cases: free test-created excess relock, multilingual global free cap, stale guest expiry/offline, staged Save after revocation/false, and entitlement-driven review invalidation.

Remaining release evidence: actual signed Release archive/configuration, App Store product/group setup, production AppAttest verification, Apple Sandbox purchase/renewal/expiry/refund/revoke/grace/restore, logout/account switching during pending requests, offline relaunch, backend deployment/runtime flags and durable entitlement/quota state. No claim of “impossible to abuse” can be supported from source and selected CI alone.

See `reviewed-inventory.md` for the complete Swift inventory and depth labels. Backend review was limited here to bridging AI protection and device-word response semantics; parent owns backend security conclusion.
