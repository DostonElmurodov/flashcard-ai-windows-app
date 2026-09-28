# Security Implementation Handoff

Date: 2026-06-13 (re-verified and re-prioritized 2026-06-13)

Backend repo: `D:\07 Hobby\Apps\backend\mavrylo`

iOS repo: `D:\07 Hobby\Apps\OwlAI\flashcard-ai-ios`

Status: partially implemented, not committed.

## Update 2026-06-13 (resume session)

- Backend build and both test suites were re-verified on the latest working tree (after the
  final cleanup edits noted under "Backend Gaps"). Result: **Debug 50/50, Release 49/49, 0 failed.**
- One blocker surfaced and was fixed: the test project (`tests/FlashCardApi.Tests.csproj`) pinned
  `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Sqlite`, and
  `Microsoft.Extensions.Logging.Abstractions` at `10.0.2`, below the API project's EF Core `10.0.9`
  runtime. A fresh restore then resolved a downgrade/`CS1705` higher-version conflict. All three
  test packages were bumped to `10.0.9` to match the API project. (This was environmental package
  skew, not a defect in the security code.)
- The remaining work below is now split by what can be done in this Windows environment (backend)
  vs what is blocked on macOS/Xcode + a real Apple Developer account (iOS device/sandbox).

### Completed in this resume session (backend)

- **Section A.3 — missing backend tests added (all feasible-now cases):**
  - `tests/LegacyAuthGuardFilterTests.cs` — disabled → 404, enabled → pass-through, dev-default.
  - `tests/AiControllerTests.cs` — word endpoints reject blank/over-long/unsupported-language
    before the AI call; `extract-words` rejects non-image payload and unsupported language before
    the vision call.
  - `tests/DeviceControllerBootstrapTests.cs` — bootstrap `challenge_id` required, unknown → 401,
    byte mismatch → 401, happy path returns a token, single-use replay → 401.
  - `tests/AppStoreServerClientTests.cs` — disallowed `productId` and `bundleId` mismatch rejected
    (DEBUG local-verify mode; `#if DEBUG`).
  - `tests/ConcurrencyTests.cs` — real-Postgres concurrency: bootstrap challenge consume yields
    exactly one winner; `AiUsageService.TryConsumeAsync` never exceeds quota; free-word reservation
    never exceeds `FreeLimit`.
- **Section A.4 — cleanup:** migrated obsolete `new X509Certificate2(byte[])` to
  `X509CertificateLoader.LoadCertificate(byte[])` in `AppleJws.cs` and `AppAttestVerifier.cs`.
  `SYSLIB0057` warnings are gone (only the unrelated `NU1510` Cbor-pruning notices remain).
- **New baseline: Debug 68/68, Release 65/65, 0 failed** (concurrency tests re-run 3× for stability).

### Still open (backend)

- Section A.5 (OpenAPI regeneration; CI vuln-scan failure validation) — not done.
- Section B items — not unit-testable in this environment (need a signing/integration harness).
- Section C (iOS) — blocked on macOS/Xcode + a real Apple Team ID.

## Source Plan

The latest requested plan was the "Build-Ready Security Implementation Plan" covering:

- Backend Debug/Release StoreKit test behavior.
- Server-authoritative entitlement checks.
- Middleware ordering.
- Legacy endpoint closure.
- App Attest bootstrap challenge binding and atomic challenge consumption.
- App Attest verifier hardening.
- StoreKit/IAP identity hardening.
- AI/upload input hardening and atomic quota/free-word controls.
- Production deployment hardening.
- iOS App Attest entitlement/configuration changes.
- iOS IAP verify contract update.
- Release fail-closed behavior.
- Backend and iOS security tests/checks.

## What Was Built

### Backend

Implemented server-authoritative device context:

- Added `Services/DeviceContextService.cs`.
- Resolves authenticated `keyId` to:
  - registered device row,
  - `DeviceUuid`,
  - current subscription,
  - canonical entitlement from `EntitlementService`.
- Updated AI and device-word paths to use database entitlement instead of trusting JWT `ent`.

Implemented middleware/config hardening:

- Reordered middleware in `Program.cs` to:
  - `UseForwardedHeaders()`
  - `UseCors()`
  - `UseAuthentication()`
  - `UseRateLimiter()`
  - `UseAuthorization()`
- Kept AI rate limiter partitioned by authenticated `keyId`, with IP fallback.
- Added `app-attest` rate limit policy for App Attest bootstrap/register/assertion challenge endpoints.
- Added production startup validation requiring:
  - `AiProtection:Enabled=true`
  - `AiProtection:RequireAssertion=true`
  - JWT key/issuer/audience
  - Apple TeamId/ClientId
  - StoreKit bundle id/environment/product allow-list
  - `Jwt:DeviceExpiresMinutes <= 60`
- Added trusted forwarded header proxy support:
  - loopback by default,
  - optional `ForwardedHeaders__KnownProxies` config.

Implemented legacy endpoint closure:

- Added `Filters/LegacyAuthGuardFilter.cs`.
- Applied it to:
  - `AuthController.Me`
  - `WordsController`
  - `CategoriesController`
  - `SyncController`
  - `UserSettingsController`
- Disabled mode returns `404`, matching legacy login/register behavior.

Implemented App Attest bootstrap challenge binding:

- `AppAttestBootstrapChallengeResponse` now returns `challenge` and `challenge_id`.
- `AppAttestRegisterRequest` now accepts `challenge_id`.
- `DeviceController.Register` now:
  - requires `challenge_id` for real devices,
  - consumes bootstrap challenge,
  - verifies the consumed nonce equals the submitted challenge bytes,
  - rejects missing, expired, reused, or mismatched challenges.
- Simulator registration remains Debug/Development-only through existing bypass logic.

Implemented atomic challenge consumption:

- `ChallengeService.TryConsumeAsync` now uses conditional `ExecuteUpdateAsync`.
- Concurrent replay should produce one winner only.

Implemented App Attest verifier hardening:

- `AppAttestVerifier` now validates:
  - user-present flag,
  - attested-credential-data flag on attestation,
  - absence of attested credential data on assertions,
  - App Attest AAGUID.
- Assertion/body buffering is capped in:
  - `AiProtectionFilter`
  - `AppAttestAssertionFilter`

Implemented StoreKit/IAP hardening:

- `IapController` now ignores `IapVerifyRequest.DeviceUuid` for authority.
- Authenticated `keyId` resolves the registered backend `DeviceUuid`.
- Verified subscriptions are linked/relinked to the authenticated device only after Apple verification/canonical refresh logic.
- `AppleJws.Decode` now enforces `alg == ES256` whenever signature verification is required.
- `AppStoreServerClient` now validates:
  - bundle id,
  - StoreKit environment,
  - product id allow-list,
  - `appAccountToken` presence outside local Debug/Development.
- Local unsigned StoreKitTest decode remains Debug + Development only.

Implemented AI/upload hardening:

- Added `Services/SupportedLanguages.cs` using the iOS app language list.
- AI word endpoints now validate:
  - non-empty word,
  - max word length,
  - supported native/learning language.
- Image upload now validates magic bytes for JPEG, PNG, WebP, and HEIC-like payloads instead of trusting MIME header.
- Added request size limits on AI JSON, device word, App Attest, and IAP verify endpoints.

Implemented quota/free word hardening:

- `AiUsageService.TryConsumeAsync` atomically reserves daily free AI quota before provider call.
- `DeviceWordService.UpsertAsync` wraps free-limit count and insert in a serializable transaction.
- `DeviceWordService.TryReserveAiSlotAsync` wraps free AI word reservation in a serializable transaction.

Implemented deployment hardening:

- `appsettings.example.json` and `ops/linux/owl-api.env.example` changed `Jwt__DeviceExpiresMinutes` to `60`.
- Added StoreKit product allow-list config:
  - `com.flashcardai.owlai.premium.monthly`
  - `com.flashcardai.owlai.premium.yearly`
- Production env example now uses StoreKit `Production`.
- GitHub Actions:
  - added Gitleaks secret scan before Docker publish,
  - added dependency vulnerability scan before Docker publish,
  - replaced `ssh-keyscan` trust-on-first-use with pinned `DEPLOY_SSH_KNOWN_HOSTS` secret.

Implemented backend test updates:

- Release unsigned StoreKit JWS expectation changed to rejection.
- Debug Development local unsigned StoreKit JWS remains accepted.
- Added/updated stale premium JWT test so expired DB entitlement blocks AI even when token says premium.
- Updated upload test to use real JPEG magic bytes.
- Existing Debug suite passed once with 50 tests.
- Existing Release suite passed once with 49 tests.

### iOS

Implemented App Attest contract update:

- `AppAttestBootstrapChallengeAPIResponse` now decodes `challengeId`.
- `AppAttestRegisterAPIRequest` now sends `challengeId`.
- `AIIntegrityCoordinator` sends the returned bootstrap challenge id during real-device registration.

Implemented IAP verify contract update:

- Removed `deviceUuid` from `IapVerifyAPIRequest`.
- `APIClient.iapVerify` now sends only the StoreKit JWS.
- `StoreKitService` still uses `.appAccountToken(accountToken)` for purchases.
- Restore/update backend verify calls no longer send client-owned `deviceUuid`.

Implemented Release fail-closed behavior:

- `APIConfiguration.bearerToken` only reads `APIBearerToken` and `FLASHCARD_API_TOKEN` in Debug.
- Protected requests in Release throw `APIError.deviceIntegrityUnavailable` if App Attest headers cannot be generated.
- Multipart extract-words path also fails closed in Release when no token or integrity headers are available.

Implemented build-configuration API/App Attest settings:

- `Resources/Info.plist` now uses `$(API_BASE_URL)`.
- App Debug build setting:
  - `API_BASE_URL = "http://127.0.0.1:5289"`
  - `APP_ATTEST_ENVIRONMENT = development`
- App Release build setting:
  - `API_BASE_URL = "https://api.mavrylo.com"`
  - `APP_ATTEST_ENVIRONMENT = production`
- `Resources/FlashCardAI.entitlements` now includes:
  - `com.apple.developer.devicecheck.appattest-environment = $(APP_ATTEST_ENVIRONMENT)`

## What Is Not Built / Still Needs Work

### Backend Gaps

- ~~Final test rerun was interrupted~~ **RESOLVED 2026-06-13:** re-verified green after the final
  cleanup edits (Debug 50/50, Release 49/49). See "Update 2026-06-13" above. The EF test-package
  skew that blocked the rerun was fixed.
- More tests from the original plan still need to be added:
  - legacy endpoints return 404 when `LegacyAuth:Enabled=false`,
  - AI limiter partitions by `keyId`,
  - bootstrap challenge required/single-use/expires via controller/integration tests,
  - concurrent challenge replay allows one winner only,
  - concurrent free AI/word operations cannot exceed quota/free word limit,
  - IAP rejects mismatched `appAccountToken`, wrong bundle, wrong environment, wrong product id.
    - **Note (test reachability):** `productId` allow-list and `bundleId` mismatch are validated
      inside `ProjectTransaction` and ARE reachable with an unsigned local JWS in DEBUG + Development
      (local-verify mode), so they are unit-testable. `environment` mismatch and the
      "`appAccountToken` required" check are deliberately gated behind `!IsLocalVerifyEnabled`, so
      they only fire for signed/production transactions — they are NOT reachable with an unsigned
      JWS and cannot be unit-tested without a real ES256 signing/x5c-chain harness. Cover those two
      via a signed-fixture or end-to-end Apple sandbox test instead.
  - upload rejects oversized/non-image payloads before provider call.
    - **Note:** non-image-payload and unsupported-language rejection happen inside the controller
      and are directly unit-testable (provider not called). The oversized-payload guard is the
      `[RequestSizeLimit(20_000_000)]` MVC attribute, enforced by the pipeline — verify via an
      integration request, not a direct controller call.
- IAP purchase-vs-restore is still modeled through one `/iap/verify` endpoint.
  - The backend ignores client `device_uuid`.
  - It requires canonical refresh for relink scenarios.
  - There is not yet a separate explicit purchase endpoint that rejects mismatched `appAccountToken` before restore semantics.
- `IapVerifyRequest.DeviceUuid` remains in the backend DTO for wire compatibility but is ignored.
- OpenAPI contract was not updated.
  - Existing `contracts/openapi/openapi.yaml` appears stale and describes older `/api/...` routes, not the current `/flashcardapp/...` device flow.
- CI dependency vulnerability scan was added, but should be validated in GitHub Actions to ensure it fails the workflow when vulnerabilities are found.
- X509 constructor warnings remain:
  - `SYSLIB0057` in `AppleJws` / `AppAttestVerifier`.
  - Security behavior is implemented, but cleanup should migrate to `X509CertificateLoader`.

### iOS Gaps

- iOS Debug/Release builds were not run in this Windows environment.
- Real-device App Attest over HTTPS was not tested.
- Sandbox purchase and restore were not tested end-to-end.
- `DEVELOPMENT_TEAM` is still blank in `FlashCardAI.xcodeproj/project.pbxproj`.
  - No real Apple Team ID was present in the repo, so it was intentionally not guessed.
- Release `API_BASE_URL` is configured as `https://api.mavrylo.com`, but this still needs to be validated on device with ATS and production API availability.
- Simulator graceful behavior remains Debug-only, but should be tested after backend changes.

## Verification Already Run

Backend:

- `dotnet test tests\FlashCardApi.Tests.csproj --configuration Debug --no-restore`
  - Passed: 50
  - Failed: 0
  - This pass occurred before the final tiny cleanup edits noted above.
- `dotnet test tests\FlashCardApi.Tests.csproj --configuration Release --no-restore`
  - Passed: 49
  - Failed: 0
  - This pass occurred before the final tiny cleanup edits noted above.

iOS:

- No build/test command was run.

## Current Modified Files

Backend modified files:

- `.github/workflows/api-ci-cd.yml`
- `Areas/FlashCardApp/Controllers/AiController.cs`
- `Areas/FlashCardApp/Controllers/AuthController.cs`
- `Areas/FlashCardApp/Controllers/CategoriesController.cs`
- `Areas/FlashCardApp/Controllers/DeviceController.cs`
- `Areas/FlashCardApp/Controllers/DeviceWordsController.cs`
- `Areas/FlashCardApp/Controllers/IapController.cs`
- `Areas/FlashCardApp/Controllers/SyncController.cs`
- `Areas/FlashCardApp/Controllers/UserSettingsController.cs`
- `Areas/FlashCardApp/Controllers/WordsController.cs`
- `Dtos/DeviceDtos.cs`
- `Filters/AiProtectionFilter.cs`
- `Filters/AppAttestAssertionFilter.cs`
- `Program.cs`
- `Services/AiUsageService.cs`
- `Services/AppAttestVerifier.cs`
- `Services/AppStoreServerClient.cs`
- `Services/AppleJws.cs`
- `Services/ChallengeService.cs`
- `Services/DeviceWordService.cs`
- `appsettings.example.json`
- `ops/linux/SERVER-SETUP.md`
- `ops/linux/owl-api.env.example`
- `tests/AiControllerTests.cs`
- `tests/AiProtectionFilterTests.cs`
- `tests/AppStoreServerClientTests.cs`

Backend new files:

- `Filters/LegacyAuthGuardFilter.cs`
- `Services/DeviceContextService.cs`
- `Services/SupportedLanguages.cs`
- `SECURITY_IMPLEMENTATION_HANDOFF.md`

iOS modified files:

- `FlashCardAI.xcodeproj/project.pbxproj`
- `Infrastructure/DTOs/DeviceAttestDTOs.swift`
- `Infrastructure/DTOs/IapDTOs.swift`
- `Infrastructure/Networking/APIClient.swift`
- `Infrastructure/Networking/APIConfiguration.swift`
- `Infrastructure/Networking/APIError.swift`
- `Infrastructure/Purchases/StoreKitService.swift`
- `Infrastructure/Security/AppAttest/AIIntegrityCoordinator.swift`
- `Resources/FlashCardAI.entitlements`
- `Resources/Info.plist`

## Remaining Work (Prioritized 2026-06-13)

### A. Doable now on this Windows machine (backend)

1. ~~Re-run backend tests on the latest tree~~ — **DONE** (Debug 50/50, Release 49/49).
2. ~~Fix compile/test issues from final edits~~ — **DONE** (EF test-package skew fixed).
3. Add the missing backend tests (highest value, feasible now):
   - `LegacyAuthGuardFilter` unit test: disabled → 404, enabled → pass-through.
   - AI input hardening: word endpoints reject empty/too-long/unsupported-language before the AI
     call; `extract-words` rejects non-image payload + unsupported language before the vision call.
   - Bootstrap challenge via `DeviceController.Register`: required, unknown → 401, mismatch → 401,
     happy path returns token, reuse of the same `challenge_id` → 401 (single-use).
   - IAP `productId` allow-list + `bundleId` mismatch rejection (DEBUG local-verify mode).
   - Concurrency (Postgres via `PostgresContainerFixture`, per-task `DbContext`s):
     bootstrap-challenge consume → exactly one winner; `AiUsageService.TryConsumeAsync` never
     exceeds quota; `DeviceWordService.TryReserveAiSlotAsync` never exceeds `FreeLimit`.
4. Cleanup: migrate obsolete `new X509Certificate2(byte[])` → `X509CertificateLoader.LoadCertificate`
   in `AppleJws` / `AppAttestVerifier` to clear `SYSLIB0057`.
5. Lower priority / heavier:
   - Update or regenerate OpenAPI for the current `/flashcardapp/...` surface (the committed
     `contracts/openapi/openapi.yaml` is stale, describing old `/api/...` routes).
   - Validate the CI dependency-vulnerability scan actually fails the workflow on a known CVE.

### B. Not reachable by unit test here (needs a signing/e2e harness)

- IAP `environment` mismatch and "`appAccountToken` required" — gated to signed/production mode.
- AI rate-limiter partition-by-`keyId` — integration-level and timing-sensitive; defer.
- Oversized-upload rejection — `[RequestSizeLimit]` pipeline attribute; verify via integration.

### C. Blocked on macOS + Xcode + a real Apple Developer account (iOS)

- Build iOS Debug and Release in Xcode.
- Configure `DEVELOPMENT_TEAM` in `FlashCardAI.xcodeproj/project.pbxproj` (needs the real Team ID).
- Test on device/sandbox:
  - Debug simulator local flow,
  - real-device App Attest over HTTPS,
  - Sandbox purchase,
  - Sandbox restore,
  - backend entitlement refresh after purchase/restore.

This environment is Windows with no Xcode, so section C cannot be executed here.
