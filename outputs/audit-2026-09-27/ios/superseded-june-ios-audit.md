# Owl AI iOS entitlement audit — 2026-09-27

Repository: `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios`

Verified HEAD: `f4925b74cae9572cd04ea920ad90ed40b51a920e`. Working tree clean before/after audit. No repository changes, purchases, account changes, or live requests performed. No applicable AGENTS.md/CLAUDE.md found in repository or checked ancestors (`D:\`, `D:\07 Hobby`, `Apps`, `OwlAI`).

## Assessment

There is no client TestMode/test_mode implementation. Turning server test mode off cannot, by itself, clear old iOS entitlement snapshots or change the client policy. Client unlimited states are **trial, premium, grace**, not strictly “has paid and currently active.” App Store trial and billing grace are deliberate exceptions. Apple lists subscribed and inGracePeriod subscriptions among current entitlements and excludes refunded/revoked products: [Apple currentEntitlements documentation](https://developer.apple.com/documentation/storekit/transaction/currententitlements).

The Release client does not directly unlock from a locally successful StoreKit purchase: it requires a verified transaction and a successful backend verify response before writing entitlement. Expensive requests go only to the server and require a bearer plus App Attest proof. No iOS path was found that supplies a client test flag or invokes an AI provider directly. **This is source evidence, not proof that the deployed backend is safe.** The parent audit covers backend/deployment independently.

## Findings and concrete conditions

### IOS-1: Persisted full-access entitlement can outlive expiry/revocation indefinitely (P2, local access)

Files and lines:

- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Purchases\EntitlementStore.swift:19-32` loads the persisted status without checking expiresAt, snapshot age, or present StoreKit ownership.
- Same file `:53-55` defines a 14-day hasOfflineReadGrace; it has no callers in any of 49 Swift files.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Domain\Entitlement\FreeLimitPolicy.swift:6-25` grants unconditional local full access for trial/premium/grace based on the status string alone.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Purchases\StoreKitService.swift:90-97` leaves the previous token/snapshot intact on any refresh failure.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\App\Root\AppRootModel.swift:23-30` exposes the repository while refresh is asynchronous; `:44-48` refreshes on foreground without a periodic expiry timer.

Repro condition: obtain a legitimate trial/premium/grace snapshot, let it expire (or revoke it), keep the app offline or make token refresh fail, then reopen. CurrentStatusSnapshot continues returning the old status even after 14 days. Existing local study queues/reminders and cached content are treated as unrestricted. New online AI may be attempted with this stale local state, but the server still authenticates and reads its own current entitlement. The stale snapshot alone is **not** proof of provider spend or server access.

Recommendation: define explicit offline/read-only policy, calculate effective local status from server validity bounds and bounded snapshot age, and distinguish server-confirmed authorization from an offline cache. Preserve grace-period semantics; simply checking transaction expirationDate can incorrectly deny Apple's billing grace.

### IOS-2: Local word accounting differs from server accounting and silently discards mutation outcomes (P2, consistency/UX)

Files and lines:

- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Repositories\WordRepository.swift:47-50,77-82,1431-1448` counts words only for the current native/learning language pair.
- Same file `:61-62` persists first, then best-effort upserts; `:1524-1528` ignores the response's accepted flag and swallows errors.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\DTOs\DeviceWordDTOs.swift:24-28` defines accepted/activeWordCount/freeLimit, but the repository does not act on these values.
- Backend bridge reviewed: `D:\07 Hobby\FlashcardAI\backend\src\Mavrylo.Services\Services\DeviceWordService.cs:20-21` counts all active words for the device, globally.

Repro: 10 local en/es words plus a different en/de pair gives local counts 10/0; the client attempts another AI request although the global device allowance is exhausted. Backend's AI reservation should reject it before AI execution, so this mismatch alone is **not** a server bypass. Conversely, remote reservations or lost upserts can make local/server libraries disagree and produce unexpected paywalls. Source/SQLite harness reproduces the exact count SQL using an 11-row fixture.

Recommendation: align local accounting to the intended server allowance and handle accepted=false/failed mutations explicitly; reconcile identity/count state rather than treating best-effort sync as authoritative.

### IOS-3: “Read-only” expired/locked words can still be edited through local paths (P2, local policy)

Files and lines:

- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Domain\Entitlement\FreeLimitPolicy.swift:37-47` says expired_paid retains all words read-only; only expired_trial/revoked trigger the over-10 lock.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Repositories\WordRepository.swift:41-44` returns cached multi-translation details before any entitlement check.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\WordSearch\ViewModels\WordSearchViewModel.swift:79-101` permits saving notes/category for cached results without checking entitlement/locked status.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Repositories\WordRepository.swift:167-198,1016-1112` updates local notes/card with no entitlement guard.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Components\DashboardWordCard.swift:450-475` enables Edit/Delete on expanded cards; expired_paid never becomes isLocked.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\EditWordSheet.swift:169-185` saves directly.

Repro: after confirmed expired_paid, edit any expanded existing word. For expired_trial/revoked locked words with at least two cached translations, search the exact stored word and use save to edit notes/category. Cached **reading** itself is consistent with read-only ownership; the issue is that edits are allowed despite the stated read-only rule. Neither path creates new provider spend, invokes a network AI fallback, nor grants an active server subscription.

Recommendation: decide whether retained words are editable or truly read-only and enforce that decision at repository mutation entry points.

### IOS-4: Local word rename is not reflected in device-word identity (P2, drift)

- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Repositories\WordRepository.swift:1016-1112` changes normalized_word/display_word in local SQLite without deviceWordUpsert or syncDeviceWordUpsert.
- Same file `:988-1011,1576-1601` later deletes using the unchanged clientWordId plus the locally changed word/languages.

Condition: rename an existing card, then request its detail or delete it. Local cache identity and server reservation identity disagree. Server may treat the renamed word as a new free slot and deny it; current client cannot reliably reconcile this state. Local manual edits can recycle the contents of an existing card, but this does not grant extra online AI slots. A server exploit is not established.

### IOS-5: Restore/Apple account changes do not invalidate an old local snapshot (P2, local stale identity)

- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Purchases\StoreKitService.swift:69-87` returns false on an empty currentEntitlements sequence, without clearing or refreshing the prior snapshot.
- Same file `:36-66,100-104` and `:77-83` reports purchase/restore success after any successful verify response, regardless of response entitlement status.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Purchases\AppAccountTokenStore.swift:8-14,25-30` retains a random device-scoped UUID in Keychain; it is not a detector for a changed Apple Account.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\App\Root\AppRootModel.swift:23-27,44-48` refreshes the backend's device entitlement and does not enumerate current StoreKit entitlements on launch/foreground.

Condition: premium snapshot exists; restore finds no current entitlement (such as expiration/revocation, or testing a different Apple Account). Local premium is not cleared by the empty restore; whether the backend's device subscription intentionally remains valid after Apple Account switching is a product/ownership-policy question. No claim is made that switching accounts alone lets an unentitled device forge purchases. Device-switch restoration ownership/relinking is handled by the server and needs Apple's Sandbox testing.

### IOS-6: Loaded dashboard/history/review state does not react to entitlement changes (P2, local stale view)

- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\DashboardView.swift:107-164` reloads on view appearance/settings/category/session changes, not EntitlementStore updates.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\ViewModels\DashboardViewModel.swift:15-24,31-53` stores already stamped rows/counts with no entitlement subscription.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\Views\ReviewSessionView.swift:78-92` loads once on appearance.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Presentation\Features\Home\ViewModels\ReviewSessionViewModel.swift:64-78,149-174,201-213` retains/retries/rates existing cards without rechecking entitlement; the focusedRow path bypasses repository queue filtering.
- `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios\Infrastructure\Notifications\StudyReminderScheduler.swift:63-74` filters newly loaded locked rows correctly, but foreground scheduling runs independently of the asynchronous entitlement refresh (`App\FlashCardAIApp.swift:74-80`).

Condition: open dashboard/review under trial/premium, then successfully refresh to expired_trial/revoked without leaving/reloading that view. Existing rows/cards and queued reminder content can remain unrestricted. Newly loaded repository queues filter using the updated snapshot. This is local stale UI/study behavior, not new provider spend.

## Policy behavior requiring a deliberate decision

- `trial` and `grace` are full-access states. They do not prove a successful current payment. Server test_mode=false does not remove either.
- `expired_paid` keeps all existing words active for review by explicit policy; it denies new AI/adds. Thus “no unlimited access whatsoever without an active paid subscription” is stricter than the current product.
- `expired_trial` and `revoked` lock words after the oldest **remaining** 10 (`WordRepository.swift:1390-1417`). Deleting an older word promotes the next word into the accessible set. This SQL behavior was reproduced. It is a policy decision, not proof of unlimited new AI.
- Free counts measure a reusable library, not necessarily 10 lifetime AI-generated words. Local deletion is normal UI and device delete is attempted asynchronously. The parent backend audit determines exact quota/cost bounds.

## Positive security evidence

- Purchases select only the two configured product IDs; unverified results throw; pending/cancelled return false (`StoreKitService.swift:36-66,122-128`). Backend verify precedes local grant and transaction.finish.
- Transaction.updates always checks verified and sends JWS to backend before applying entitlement (`StoreKitService.swift:106-118`). Failure leaves stale local state, it does not manufacture a success response.
- All client AI routes are `/owlai/ai/analyze-word`, `/word-detail`, `/extract-words` (`APIClient.swift:19-118`). No direct Gemini/OpenAI key/provider path found.
- Release requests require a bearer and complete App Attest headers (`APIClient.swift:52-55,88-98,206-217,230-233,278-284,318-321`). Debug-only no-bearer requests are compiled out of Release.
- Token preference is valid saved IAP token, valid App Attest session, then DEBUG-only configured token (`APIClient.swift:188-202`; `TokenStore.swift:13-23`; `APIConfiguration.swift:27-38`). Expiration is enforced client-side, and server JWT validation must enforce the real signed expiration.
- Simulator registration exists only under DEBUG (`AIIntegrityCoordinator.swift:160-185`); normal App Attest uses fresh body/path/challenge proof. API error handling throws on non-2xx and does not silently bypass entitlement or fallback to a provider (`APIClient.swift:302-314`).
- Archive configuration is Release (`FlashCardAI.xcscheme:100-103`), Release API is `https://api.mavrylo.com` (`project.pbxproj:1170`). There is no StoreKitConfigurationFileReference in the shared scheme; the packaged `.storekit` resource does not itself grant entitlement.
- Account/login/import/catalog routes are absent from this no-login iOS client. The scan “import” loops wordDetail sequentially and stops on 402 (`ScanWordsSheet.swift:336-363`), so it does not skip the same AI gate for bulk adds. WordDetail itself inserts before the user presses Save; Save assigns notes/category to the already inserted word.
- Reviewed backend bridge `Filters\AiProtectionFilter.cs` resolves database status rather than trusting JWT ent/client snapshot and reserves/quotas free requests before controller execution. Full backend correctness is owned by the parent audit.

## Verification and limitations

`python static_repro.py` completed successfully with nine source/SQL characterizations. Results: `static-repro-results.json`. It reads actual source, executes the actual count SQL and equivalent oldest-10 lock SQL against an in-memory SQLite fixture, verifies metadata syntax and Release archive guards. This is **not Swift/iOS execution** and is not called a passing runtime entitlement test.

Swift and xcodebuild are unavailable on this Windows host. Only `FlashCardAITests/SpacedRepetitionServiceTests.swift` is present; an entitlement/StoreKit/security test search found no existing coverage. The following remain unverified: iOS build/archive, TestFlight/Apple Sandbox receipts, trial conversion, renewal/failed payment/grace/expiry/refund/revoke timing, device restore with original appAccountToken, Apple Account switching, offline clock manipulation/14+ days, real-device App Attest, deployed Release flags/backend overrides and production subscription notifications. No App Store release assurance should rest on these static checks alone.

Recommended macOS/Apple Sandbox scenarios: new free device at 9/10/11 words and multiple pairs; sequential/bulk scan; parallel searches; trial expiry offline/online; refunded/revoked premium with already-open views; grace end; empty restore after prior premium; restore from second device/account; backend timeout and 401/402/403/429/503 paths; local delete/rename with failed sync. Validate server provider invocation counts as well as UI behavior.

File inventory and review depth are in `reviewed-inventory.md`; every Swift file was searched for entitlement/AI/purchase/account/TestMode/cache/fallback references. Critical control paths received full or targeted source reading; visual-only files were searched, not exhaustively reviewed as security code.
