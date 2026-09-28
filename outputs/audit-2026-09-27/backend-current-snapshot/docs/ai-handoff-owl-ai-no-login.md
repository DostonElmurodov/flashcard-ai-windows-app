# AI Handoff Prompt — Owl AI No-Login iOS + Backend Subscription System

Use this document as the full context prompt for another AI/coding agent. It summarizes the repo, the business goal, the current architecture, what has already been built, what was discussed, the current GitHub state, and the remaining work.

## Repository And Important Paths

Main local repo:

```text
/Users/dostonelmurodov/Projects/Personal/iOSApp/FlashCard AI
```

This is one Git repository that currently contains both projects:

```text
/Users/dostonelmurodov/Projects/Personal/iOSApp/FlashCard AI/apps/ios-native
/Users/dostonelmurodov/Projects/Personal/iOSApp/FlashCard AI/apps/api-dotnet
```

iOS app:

```text
apps/ios-native
apps/ios-native/FlashCardAI.xcodeproj
Bundle ID: com.flashcardai.FlashCardAI
Display/app name goal on phone: Owl AI
App Store name currently: Owl AI: Flash Cards
```

Backend API:

```text
apps/api-dotnet
apps/api-dotnet/FlashCardApi.csproj
.NET 10 ASP.NET Core
EF Core
SQLite locally, PostgreSQL planned for Linux production
JWT auth
Local Development launch profile exists in apps/api-dotnet/Properties/launchSettings.json
```

Backend tests:

```text
apps/api-dotnet.Tests
apps/api-dotnet.Tests/FlashCardApi.Tests.csproj
```

Original long implementation plan/audit file outside the repo:

```text
/Users/dostonelmurodov/.claude/plans/add-to-plan-do-temporal-iverson.md
```

Important rule: do not read or print the contents of Apple `.p8` private key files. The user explicitly said this is a strong rule. The known local path is:

```text
/Users/dostonelmurodov/Documents/AppleStoreCertKey/SubscriptionKey_A89FK3M993.p8
```

It may be referenced by path only. Never open/read/cat it.

Known App Store Server API credentials already provided by the user:

```text
Issuer ID: 54b26633-d6e9-40aa-b090-363d33ce0843
Key ID: A89FK3M993
Private key path: /Users/dostonelmurodov/Documents/AppleStoreCertKey/SubscriptionKey_A89FK3M993.p8
```

These credentials are server-only. They belong in backend configuration/user-secrets/Linux env files only. Do not add them to iOS, StoreKit files, source code, committed `appsettings*.json`, screenshots, logs, or this repo.

## GitHub State

GitHub username:

```text
DostonElmurodov
```

Relevant GitHub repositories:

```text
https://github.com/DostonElmurodov/flashcard-ai-ios
https://github.com/DostonElmurodov/mavrylo
```

Both repositories were checked with GitHub CLI and are private.

Current local repo is connected to:

```text
origin = https://github.com/DostonElmurodov/flashcard-ai-ios.git
branch = main
```

Pushed HEAD before this document-update pass:

```text
2077da0 Update AI handoff with missing context
```

Recent commits pushed:

```text
2077da0 Update AI handoff with missing context
1e07832 Add AI handoff prompt
8d9a741 Implement no-login device entitlement enforcement
bc4f61a Harden local ignore rules
5bfc4e5 Merge remote repository history
```

Important GitHub problem that happened:

- The local repository had no `origin`.
- `flashcard-ai-ios` already existed on GitHub and was private.
- The remote `main` had similar project history but different commit SHAs and an additional ignore-rules commit.
- A normal push would have been rejected because `origin/main` was not an ancestor of local `main`.
- To avoid force-push and avoid destroying remote history, a non-destructive merge was made:

```bash
git merge -s ours --allow-unrelated-histories origin/main -m "Merge remote repository history"
```

- This kept the local tree as the source of truth while preserving the remote history as a parent.
- Then `git push -u origin main` succeeded.
- Do not force-push unless the user explicitly asks.

`mavrylo` exists and is private, but no separate local Git repo for it was found. Do not push this monorepo into `mavrylo` unless the user explicitly confirms that is desired.

## Current Product Goal

The product is Owl AI, an iOS flash card app for translating words/phrases/phrasal verbs with AI, saving them as flash cards, reviewing them, and scheduling study reminders.

The user wants a no-login v1:

- User downloads app.
- User opens app.
- No account creation.
- No email login.
- No Google/Facebook login.
- App automatically registers the device with Apple App Attest.
- Backend issues a device JWT.
- Device JWT + App Attest assertion protect sensitive API routes.
- Backend is the final authority for subscription entitlement.
- StoreKit 2 is used on iOS.
- App Store Server API is used on backend.
- Free users can use AI only for the first 10 saved words.
- Trial/premium/grace users have unlimited AI and add-word access.
- Expired trial: only first 10 words remain active; words 11+ visible but disabled, excluded from review and notifications.
- Expired paid/revoked: all words remain read-only; review and notifications still work; adding new words and AI are blocked.

Future:

- iCloud/device sync may come later.
- Backend should store full word list per deviceId for future sync/account linking.
- In the future, user accounts may be added and linked to existing deviceId.
- For v1, old login UI must be removed because App Store Review can ask why login exists and what data is collected.

Current sync decision:

- iCloud sync is desired later, but is not implemented in this no-login pass.
- Device-based backend sync is the future direction: backend stores words by `DeviceUuid` now, so a future account system can link an account to an existing device inventory.
- Current v1 disables old login-based sync in iOS. Backend legacy user-data endpoints still exist, but the no-login app should not use them.

## Business Rules

Entitlement states:

```text
free
trial
premium
grace
expired_trial
expired_paid
revoked
```

Rules:

- `free`
  - Can add/use AI for first 10 words.
  - 11th add+translate should be blocked.
  - Message should be: "You're on the free plan — it's limited."

- `trial`
  - Unlimited words.
  - Unlimited AI.
  - Full review/notifications.

- `premium`
  - Unlimited words.
  - Unlimited AI.
  - Full review/notifications.

- `grace`
  - Treat like premium while billing retry/grace is active.

- `expired_trial`
  - Trial ended and user never paid.
  - Only first 10 words remain active.
  - Words 11+ should be visible but disabled/read-only.
  - Words 11+ excluded from review queue.
  - Words 11+ excluded from study notification rows.
  - Dashboard/review counts should respect only active first-10 words.
  - AI and adding new words blocked.

- `expired_paid`
  - User was paying, then lapsed/cancelled.
  - No AI.
  - No adding new words.
  - All existing words kept read-only.
  - Review and notifications still work for all existing words.

- `revoked`
  - Refund/revoke.
  - Same practical iOS mode as expired paid: read-only, no adding, no AI, review/notifications still work.

## How No-Login iOS Auth Should Work

The user does not manually get API access. The app gets limited device access automatically.

Flow:

1. User installs and opens app.
2. iOS app creates/loads an App Attest key for this device.
3. iOS calls public endpoint:

```http
POST /flashcardapp/device/app-attest/bootstrap-challenge
```

4. Backend returns challenge.
5. iOS calls Apple `DCAppAttestService.attestKey`.
6. iOS sends attestation to backend:

```http
POST /flashcardapp/device/app-attest/register
```

7. Backend verifies:
   - Apple App Attest certificate chain.
   - Bundle ID / app identity.
   - Challenge nonce.
   - Public key.
8. Backend stores device record.
9. Backend returns device JWT with `ent=free`.
10. iOS stores device JWT in Keychain.
11. For AI/IAP/device-word endpoints, iOS:
   - Ensures it has a valid device/subscription JWT.
   - Fetches assertion challenge:

```http
POST /flashcardapp/device/app-attest/assertion-challenge
```

   - Builds App Attest assertion over request body/path/challenge.
   - Sends:

```http
Authorization: Bearer <device JWT>
X-App-Attest-Key-Id: <key id>
X-App-Attest-Assertion: <base64 assertion>
X-App-Attest-Challenge-Id: <challenge id>
```

Backend validates both:

- The JWT proves this is a registered device and carries current entitlement.
- The App Attest assertion proves this request is from the genuine app/device and prevents replay.

Device JWT details from current code:

- Device token scheme name: `device`.
- Device JWT audience: `device`.
- Claims:
  - `sub` = App Attest `keyId`.
  - `keyId` = App Attest key id.
  - `ent` = entitlement state.
  - optional `otid` = Apple original transaction id.
- Signed with HS256 using `Jwt:Key`.
- `Jwt:Key` must be a long random server secret with at least 32 UTF-8 bytes.
- Default device token lifetime in code is 30 minutes, but Linux env template sets `Jwt__DeviceExpiresMinutes=43200` unless changed.
- Device JWT and old user JWT use the same signing secret but different audiences, so they cannot be substituted if validation is configured correctly.

Simulator/dev:

- Simulator cannot do real App Attest.
- Development bypass uses `SIMULATOR-*` keys.
- This must never be enabled in production.
- The assertion filters skip real assertion verification for simulator only when the verifier development bypass is enabled and `keyId` starts with `SIMULATOR-`.

## Backend Routes And Protection Model

Public endpoints:

```http
GET  /health
GET  /public/legal/terms
GET  /public/legal/privacy
POST /flashcardapp/device/app-attest/bootstrap-challenge
POST /flashcardapp/device/app-attest/register
POST /flashcardapp/device/app-attest/assertion-challenge
POST /flashcardapp/iap/notifications
```

Device-protected endpoints:

```http
POST /flashcardapp/iap/verify
POST /flashcardapp/iap/token
GET  /flashcardapp/iap/entitlement
GET  /flashcardapp/device/words/count
POST /flashcardapp/device/words/upsert
POST /flashcardapp/device/words/delete
```

These require:

- Device JWT.
- App Attest assertion.

AI endpoints:

```http
POST /flashcardapp/ai/analyze-word
POST /flashcardapp/ai/word-detail
POST /flashcardapp/ai/extract-words
```

When `AiProtection:Enabled=true`, AI requires:

- Valid device JWT.
- App Attest assertion.
- Active entitlement.
- Free 10-word backend enforcement for `ent=free`.
- Rate limiting.

Current AI protection details:

- `AiProtectionFilter` is an `IAsyncResourceFilter`; it runs before model binding so it can read and rewind the exact raw body used for App Attest hashing.
- It validates the device JWT manually against audience `device`.
- It consumes a single-use assertion challenge bound to `keyId` + request path.
- Active AI states in code are only `free`, `trial`, `premium`, and `grace`.
- `expired_trial`, `expired_paid`, and `revoked` receive 402 for AI.
- For `ent=free`, backend tries to reserve a `device_words` slot before running expensive AI.
- The current pre-reserve parser only reads `word`, `native_language`/`nativeLanguage`, and `learning_language`/`learningLanguage` from JSON bodies.
- Multipart image extraction cannot currently reserve a concrete word before AI because it has no single `word` field; it is still protected by device JWT, assertion, entitlement, daily quota, and rate limiting.

Old user-data endpoints:

```http
/flashcardapp/words/*
/flashcardapp/categories/*
/flashcardapp/sync/*
/flashcardapp/user-settings/*
```

These are legacy/login-user oriented. For no-login v1, iOS should not call them. They are currently not the main sync system. Future sync can be redesigned around deviceId/account linking.

## Important Backend Files

App Attest:

```text
apps/api-dotnet/Services/AppAttestVerifier.cs
apps/api-dotnet/Services/AppAttestClientData.cs
apps/api-dotnet/Services/ChallengeService.cs
apps/api-dotnet/Areas/FlashCardApp/Controllers/DeviceController.cs
apps/api-dotnet/Filters/AppAttestAssertionFilter.cs
apps/api-dotnet/Models/DeviceEntity.cs
apps/api-dotnet/Models/ChallengeEntity.cs
```

Device JWT:

```text
apps/api-dotnet/Services/JwtTokenService.cs
apps/api-dotnet/Services/DeviceAuth.cs
```

Entitlement/IAP/App Store:

```text
apps/api-dotnet/Services/AppStoreServerClient.cs
apps/api-dotnet/Services/AppleJws.cs
apps/api-dotnet/Services/EntitlementService.cs
apps/api-dotnet/Models/SubscriptionEntity.cs
apps/api-dotnet/Areas/FlashCardApp/Controllers/IapController.cs
apps/api-dotnet/Areas/FlashCardApp/Controllers/AppStoreNotificationsController.cs
apps/api-dotnet/Dtos/IapDtos.cs
```

AI protection:

```text
apps/api-dotnet/Filters/AiProtectionFilter.cs
apps/api-dotnet/Services/AiUsageService.cs
apps/api-dotnet/Models/AiUsageEntity.cs
apps/api-dotnet/Services/AiProtectionOptions.cs
```

Backend per-device word storage:

```text
apps/api-dotnet/Models/DeviceWordEntity.cs
apps/api-dotnet/Dtos/DeviceWordDtos.cs
apps/api-dotnet/Services/DeviceWordService.cs
apps/api-dotnet/Areas/FlashCardApp/Controllers/DeviceWordsController.cs
apps/api-dotnet/Migrations/20260606174330_AddDeviceWords.cs
apps/api-dotnet/Migrations/20260606174330_AddDeviceWords.Designer.cs
```

`device_words` current schema details:

- Entity: `DeviceWordEntity`.
- Fields:
  - `Id`
  - `DeviceUuid`
  - `ClientWordId`
  - `NormalizedWord`
  - `DisplayWord`
  - `NativeLanguage`
  - `LearningLanguage`
  - `Translation`
  - `Pronunciation`
  - `PartOfSpeech`
  - `DetailJson`
  - `IsActive`
  - `CreatedAt`
  - `UpdatedAt`
- Unique index:
  - `(DeviceUuid, NormalizedWord, NativeLanguage, LearningLanguage)`
- Other indexes:
  - `(DeviceUuid, ClientWordId)`
  - `(DeviceUuid, IsActive)`
- `DeviceWordService.FreeLimit = 10`.
- `UpsertAsync` treats `free` and `expired_trial` as free-limited for mutation.
- `AiProtectionFilter` currently only calls reservation for `ent=free`, because `expired_trial` is not active for AI.
- `DeleteAsync` currently removes rows instead of soft-disabling them.

Database:

```text
apps/api-dotnet/Data/AppDbContext.cs
apps/api-dotnet/Migrations/
```

Deployment:

```text
.github/workflows/api-ci-cd.yml
ops/linux/README.md
ops/linux/owl-api.env.example
ops/linux/owl-api.service
ops/linux/nginx-owl-api.conf.example
```

## Important iOS Files

App startup/no-login:

```text
apps/ios-native/App/FlashCardAIApp.swift
apps/ios-native/App/AppDelegate.swift
apps/ios-native/App/Root/AppRootModel.swift
apps/ios-native/App/Session/AppLaunchState.swift
```

Networking:

```text
apps/ios-native/Infrastructure/Networking/APIClient.swift
apps/ios-native/Infrastructure/Networking/APIConfiguration.swift
apps/ios-native/Infrastructure/Networking/APIError.swift
```

App Attest:

```text
apps/ios-native/Infrastructure/Security/AppAttest/AIIntegrityCoordinator.swift
apps/ios-native/Infrastructure/Security/AppAttest/AppAttestKeychainStore.swift
apps/ios-native/Infrastructure/DTOs/DeviceAttestDTOs.swift
```

Purchases/subscription:

```text
apps/ios-native/Infrastructure/Purchases/AppAccountTokenStore.swift
apps/ios-native/Infrastructure/Purchases/StoreKitService.swift
apps/ios-native/Infrastructure/Purchases/EntitlementStore.swift
apps/ios-native/Infrastructure/Purchases/TokenStore.swift
apps/ios-native/Infrastructure/DTOs/IapDTOs.swift
apps/ios-native/Resources/Owl AI.storekit
```

Free limit:

```text
apps/ios-native/Domain/Entitlement/FreeLimitPolicy.swift
```

Words/local DB:

```text
apps/ios-native/Infrastructure/Persistence/LocalDatabase.swift
apps/ios-native/Infrastructure/Repositories/WordRepository.swift
apps/ios-native/Infrastructure/DTOs/DeviceWordDTOs.swift
```

Expired-trial/review/notification areas still needing work:

```text
apps/ios-native/Presentation/Features/Home/ViewModels/DashboardViewModel.swift
apps/ios-native/Presentation/Features/Home/ViewModels/ReviewSessionViewModel.swift
apps/ios-native/Infrastructure/Notifications/StudyReminderScheduler.swift
apps/ios-native/Presentation/Features/Home/Views/WordHistoryView.swift
apps/ios-native/Presentation/Features/Home/Components/DashboardWordCard.swift
```

## What Has Already Been Built

### Backend

Already built:

- App Attest verifier.
- DB-backed challenge service.
- Device registration endpoints.
- Device JWT minting.
- IAP verify/token/entitlement endpoints.
- App Store Server API client.
- ES256 Apple request JWT signer.
- App Store notification controller with partial handling.
- Subscription entity and entitlement service.
- AI protection filter attached to AI controller.
- App Attest assertion filter for protected routes.
- Rate limiter policy named `ai`.
- Production hardening:
  - Outside Development, backend refuses to start unless `AiProtection:Enabled=true`.
  - Outside Development, backend refuses to start if `Apple:SkipSignatureValidation=true`.
- Legacy auth disabled outside Development unless `LegacyAuth:Enabled=true`.
- Backend per-device word storage:
  - `device_words` table/entity.
  - Stores full word data per `DeviceUuid`.
  - Used now for active word count and future sync.
  - Device-word endpoints require device JWT + App Attest assertion.
- Backend AI free-limit enforcement:
  - For `ent=free`, backend tries to reserve a device-word slot before expensive AI.
  - If active device word count is already 10, backend returns 402.
  - This prevents a custom script/client from bypassing the free limit by never syncing local words.

Important backend caveats already known:

- Backend full word storage exists, but full sync APIs are not finished; current endpoints are count/upsert/delete only.
- The old user tables/endpoints still exist for login-era data.
- The current backend does not yet implement a complete device/account merge story.
- The current backend has only limited tests; the existing test project passed, but new security/device-word/subscription edge cases still need tests.
- `appsettings.json` and `appsettings.Development.json` are ignored by `.gitignore` and are not tracked now.
- The default checked behavior in ignored local appsettings may have `Apple:SkipSignatureValidation=true`; production must override this to `false` or startup will fail outside Development.

### iOS

Already built:

- App Attest registration at app launch.
- App Attest key/session storage in Keychain.
- StoreKit 2 service for products, purchase, restore, transaction updates.
- Token and entitlement storage.
- Product onboarding purchase/restore/terms/privacy wiring.
- APIClient prefers subscription/device JWT.
- App Attest assertion headers are attached for AI/IAP/device-word routes.
- iOS no-login flow:
  - Product onboarding only.
  - No sign-in gate.
  - Goes straight into app after onboarding.
- Old login UI deleted:
  - FirstLaunchAuth view/model removed.
  - EmailAuth view/model removed.
  - AuthCredentialStore removed.
  - Auth DTOs removed.
- Google/Facebook sign-in dependencies removed:
  - Imports/hooks removed from `AppDelegate`.
  - Swift Package dependencies removed from Xcode project.
  - `Package.resolved` removed.
- Old sync disabled:
  - `AppLaunchState.syncEnabled = false`.
  - Settings/theme remote load/save guarded by `syncEnabled`.
  - WordSyncService removed.
- Device word sync-ish mirror:
  - On successful local word detail insert/update, iOS calls `/flashcardapp/device/words/upsert`.
  - On local delete, iOS calls `/flashcardapp/device/words/delete`.
  - These are best-effort and protected by device JWT + App Attest assertion.

Important iOS caveats already known:

- `APIClient` still contains old methods for `/flashcardapp/sync/push` and `/flashcardapp/user-settings`; they are not active in v1 because `AppLaunchState.syncEnabled = false` gates the settings/theme remote sync calls.
- `apps/ios-native/Resources/Info.plist` still contains old Google/Facebook metadata keys and URL schemes even though the code dependencies were removed. This should be cleaned before App Review.
- `apps/ios-native/Resources/FlashCardAI.entitlements` still contains `com.apple.developer.applesignin`. Since no-login v1 no longer uses Sign in with Apple, remove the capability/entitlement later if it is not needed.
- `ProductOnboardingView` still has a stale code comment saying it is shown before a required Google sign-in page. The UI flow is no-login now, so update/remove that comment later.
- iOS local UX gating is not security. Backend enforcement is the real boundary.

### Linux/CI

Already added:

- GitHub Actions API build/test/publish/deploy workflow.
- Linux env template.
- systemd service template.
- nginx HTTPS reverse proxy template.
- Linux README with public/protected API explanation and secrets setup.

CI/CD details:

- `.github/workflows/api-ci-cd.yml` runs backend restore/build/test/publish on pushes that touch the API/test/workflow paths.
- Deployment runs only on manual `workflow_dispatch`, not automatically on every push.
- Deployment needs GitHub environment/repository secrets:
  - `DEPLOY_SSH_HOST`
  - `DEPLOY_SSH_PORT`
  - `DEPLOY_SSH_USER`
  - `DEPLOY_SSH_PRIVATE_KEY`
  - `DEPLOY_PATH`
  - `DEPLOY_SERVICE_NAME`
- Linux env template expects backend to listen on `http://127.0.0.1:5289` behind nginx.
- Planned public API URL: `https://api.mavrylo.com`.
- Production env must include:
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `AiProtection__Enabled=true`
  - `AiProtection__RequireAssertion=true`
  - `Apple__SkipSignatureValidation=false`
  - `LegacyAuth__Enabled=false`
  - Postgres connection string.
  - Apple App Store Server API credentials.
  - AI provider API key.
- `.p8` must be copied manually to the Linux server, e.g. `/etc/owl-ai/secrets/SubscriptionKey.p8`, and referenced via `Apple__AppStoreServer__PrivateKeyPath`.

## Verification Already Run

These passed:

```bash
dotnet build apps/api-dotnet/FlashCardApi.csproj --no-restore
dotnet test apps/api-dotnet.Tests/FlashCardApi.Tests.csproj --no-restore
plutil -lint apps/ios-native/FlashCardAI.xcodeproj/project.pbxproj
jq empty 'apps/ios-native/Resources/Owl AI.storekit'
xcodebuild -project apps/ios-native/FlashCardAI.xcodeproj -scheme FlashCardAI -destination 'platform=iOS Simulator,name=iPhone 17 Pro' build
```

Backend build warnings seen:

- `System.Formats.Cbor` package prune warning.
- `X509Certificate2(byte[])` obsolete warning in certificate loading code.

Those warnings did not block build/test.

## Apple Developer / App Store Connect Progress

Apple Developer Program:

- User paid/enrolled.
- Account initially showed pending/complete purchase confusion.
- Eventually App Store Connect access worked.
- Payment total shown in the conversation was `$105.93` including tax.
- Apple order page showed enrollment complete and order placed on May 31, 2026.

App in App Store Connect:

```text
App name: Owl AI: Flash Cards
Bundle ID: com.flashcardai.FlashCardAI
SKU used: owl-ai-ios
Primary language: English (U.S.)
```

Naming decisions:

- App Store listing name is `Owl AI: Flash Cards` so users understand the purpose.
- Phone home-screen display name should be `Owl AI` via `CFBundleDisplayName`.
- Bundle ID `com.flashcardai.FlashCardAI` is okay even though the web/legal domain is `mavrylo.com`; Bundle ID does not need to match the website domain.
- SKU is an internal Apple/App Store Connect identifier only; users do not see it.
- `Full Access` was the right choice for App Store Connect user access on this app because the app should be available to all App Store users after release.

Subscription group:

```text
Owl AI Premium
```

Subscription products:

```text
com.flashcardai.owlai.premium.monthly
com.flashcardai.owlai.premium.yearly
```

These product IDs match the current iOS `StoreKitService` constants and the local StoreKit file.

Subscription display names:

```text
Owl AI Premium Monthly
Owl AI Premium Yearly
```

Subscription description used:

```text
AI translations in 25 languages, cards, reminders.
```

The subscription description is user-facing in App Store subscription UI/purchase/manage-subscription surfaces. It should be short and clear.

Pricing:

- Yearly price discussed/created around `$59.99`.
- Monthly price target: `$6.99`.
- Confirm monthly price in App Store Connect.
- App Store shows country/currency-adjusted prices; this is normal.

Introductory offer:

- 7-day free trial was added for yearly and monthly.
- App Store Connect displays it as:

```text
Free for the first week
Jun 6, 2026 to No End Date
175 Countries or Regions
```

Missing metadata:

- Subscriptions still showed `Missing Metadata` earlier because subscription review info/screenshot was skipped.
- This can be completed later.
- Need paywall screenshot and review notes for each subscription before App Review.

ASSA/App Store Server API:

- User created/downloaded the `.p8` private key.
- Do not read it.
- Need configure backend secrets locally/server-side only.
- Never put `.p8`, IssuerId, or KeyId into iOS.
- App Store Connect API access page initially showed "Request Access"; user later downloaded the key.
- Real Apple sandbox credentials and live App Store Server API calls have not yet been verified end-to-end.

## Current App Architecture In Plain English

### On First Launch

1. App starts.
2. `AppRootModel` creates `LocalDatabase`, `APIClient`, and `WordRepository`.
3. It configures `AIIntegrityCoordinator`.
4. It attempts App Attest/device registration.
5. It refreshes StoreKit token/entitlement.
6. User sees product onboarding if not completed.
7. After product onboarding, user enters app without account.

### AI Word Detail Flow

1. User asks to translate/add word.
2. iOS checks local free-limit policy before AI.
3. If allowed, iOS calls backend AI endpoint using:
   - device/subscription JWT.
   - App Attest assertion headers.
4. Backend `AiProtectionFilter`:
   - Validates device JWT.
   - Validates App Attest assertion.
   - Checks entitlement.
   - If free, reserves/checks device word count.
   - Checks daily free quota/rate limit.
5. AI action executes.
6. iOS saves result into local SQLite.
7. iOS mirrors saved word to backend `device_words`.

### Purchase/Restore Flow

1. User buys/restores through StoreKit 2.
2. iOS receives StoreKit transaction.
3. iOS sends signed transaction JWS to backend `/iap/verify`.
4. Backend verifies with Apple/local JWS logic and App Store Server API.
5. Backend stores subscription state.
6. Backend returns updated entitlement and device JWT.
7. iOS stores token/entitlement.

### Backend Final Authority

The backend must be final authority for:

- Whether AI can be called.
- Whether subscription is active.
- Whether free user has reached 10 words.

iOS gating is for UX, not security. Backend gating is the security boundary.

## What Is Not Done Yet

High priority:

1. Real Sandbox purchase testing on a real/simulator Apple sandbox environment.
   - Purchase monthly/yearly.
   - Free trial.
   - Restore.
   - Cancel/expire.
   - Refund/revoke.
   - Transaction updates.
   - Backend `/iap/verify` and `/iap/token`.

2. Real-device App Attest testing.
   - Register.
   - Assertion.
   - Protected AI call.
   - Reinstall.
   - Restore on a new device.

3. Linux production setup.
   - PostgreSQL.
   - systemd.
   - nginx HTTPS.
   - GitHub Actions deployment secrets.
   - Backend env secrets in `/etc/owl-ai/api.env`.
   - `.p8` copied manually to `/etc/owl-ai/secrets/SubscriptionKey.p8`.
   - Never commit real secrets.
   - Use Postgres database `owl_ai` with an app DB user such as `owl_ai`.
   - Configure `api.mavrylo.com` DNS and nginx HTTPS.
   - Configure GitHub Actions deployment secrets before using manual deploy.

4. iOS expired_trial UI enforcement.
   - Only first 10 words active.
   - Words 11+ visible but disabled.
   - Words 11+ excluded from review.
   - Words 11+ excluded from notifications.
   - Dashboard/review counts should respect active words.

5. expired_paid/revoked read-only mode.
   - Block adding new words.
   - Block AI.
   - Allow reading all words.
   - Allow review and notifications.

6. Subscription metadata in App Store Connect.
   - Add subscription review screenshot.
   - Add review notes.
   - Add app screenshots.
   - Finish App Privacy, rating, app info, support URLs, etc.
   - Attach created subscriptions to the first app version before App Review.

7. Backend tests.
   - App Attest attestation success/failure if feasible.
   - Assertion replay rejected.
   - StoreKit JWS verification success/failure.
   - Notification refund -> revoked.
   - Entitlement state transitions.
   - Rate limit 429.
   - expired_trial vs expired_paid behavior.
   - `/iap` and `/device/words` require assertion.
   - AI returns 402 for expired/revoked/free-over-limit entitlement.

Medium priority:

8. Remove or fully isolate old backend user auth if no longer needed.
   - Currently legacy auth is disabled outside Development by default.
   - Keep only if needed for future account linking/admin/backward compatibility.

9. Remove remaining old sync DTO/client methods if not needed.
   - Some settings/theme DTOs remain for future sync but gated off.
   - Decide later whether to delete or redesign around device sync.

10. Clean App Review metadata leftovers from old login implementation.
   - Remove `com.apple.developer.applesignin` from `apps/ios-native/Resources/FlashCardAI.entitlements` if unused.
   - Remove old Google/Facebook keys and URL schemes from `apps/ios-native/Resources/Info.plist` if unused.
   - Remove stale Google sign-in wording in comments.

11. Better race handling in `DeviceWordService`.
   - Concurrent same-word reservation could theoretically hit unique constraint.
   - Could catch `DbUpdateException` and treat as allowed if row already exists.

12. Rename StoreKit file without space maybe:
   - Current: `Owl AI.storekit`.
   - Optional: `OwlAI.storekit`.

## Known Safety Rules

- Never read or print `.p8` private key contents.
- Never commit:
  - `.p8`
  - `.env`
  - appsettings real files
  - database files
  - API keys
  - Apple private credentials
  - GoogleService-Info.plist
- `.gitignore` has been hardened with:

```text
*.p8
AuthKey_*.p8
GoogleService-Info.plist
*.db
*.db-shm
*.db-wal
.env
.env.*
appsettings.json
appsettings.Development.json
DerivedData/
.build/
.swiftpm/
xcuserdata/
```

Note:

- `/Users/dostonelmurodov/Documents/AppleStoreCertKey/SubscriptionKey_A89FK3M993.p8` is outside the repo, so repo `.gitignore` does not apply directly to that external path.
- If a `.p8` is accidentally copied into the repo, Git should ignore it.
- `apps/api-dotnet/Data/Migrations/` was discussed because it appeared in ignore checks, but the ignored file there was only `.DS_Store`. Real EF migrations live in `apps/api-dotnet/Migrations/` and are tracked.
- `apps/api-dotnet/appsettings.json` and `apps/api-dotnet/appsettings.Development.json` are ignored and not tracked now. Use user-secrets or environment variables for local secrets.
- `apps/ios-native/Resources/Info.plist` and `apps/ios-native/Resources/FlashCardAI.entitlements` are tracked app metadata files; do not put secrets in them.

## Commands To Verify After Changes

Backend:

```bash
dotnet build apps/api-dotnet/FlashCardApi.csproj --no-restore
dotnet test apps/api-dotnet.Tests/FlashCardApi.Tests.csproj --no-restore
```

iOS:

```bash
plutil -lint apps/ios-native/FlashCardAI.xcodeproj/project.pbxproj
jq empty 'apps/ios-native/Resources/Owl AI.storekit'
xcodebuild -project apps/ios-native/FlashCardAI.xcodeproj -scheme FlashCardAI -destination 'platform=iOS Simulator,name=iPhone 17 Pro' build
```

Git/GitHub:

```bash
git status --short --branch
git remote -v
gh repo view DostonElmurodov/flashcard-ai-ios --json name,isPrivate,url,defaultBranchRef
gh repo view DostonElmurodov/mavrylo --json name,isPrivate,url,defaultBranchRef
```

## Suggested Next Work Order

Recommended next sequence:

1. Configure local backend secrets for Apple ASSA without committing anything.
2. Run backend locally with `AiProtection:Enabled=true` in a safe dev setup.
3. Run iOS on simulator with dev App Attest bypass.
4. Test local StoreKit purchases against backend `/iap/verify` and `/iap/token`.
5. Deploy backend to Linux with PostgreSQL and HTTPS.
6. Configure App Store Server API secrets on Linux.
7. Test real sandbox purchase with public HTTPS backend.
8. Test real device App Attest.
9. Implement expired_trial UI filtering.
10. Implement expired_paid/revoked read-only mode.
11. Add backend tests for security/entitlement edge cases.
12. Complete App Store metadata/screenshots/review notes.

## Deep Context / Audit Addendum

This section was added after a second audit pass through the handoff, both codebases, and the conversation. It intentionally repeats some facts with more operational detail so another AI can continue work without guessing.

### Conversation Timeline / Decisions

High-level conversation flow:

1. User first wanted to continue the no-login subscription plan from `/Users/dostonelmurodov/.claude/plans/add-to-plan-do-temporal-iverson.md`.
2. We paused coding because Apple Developer/App Store Connect setup was blocking real sandbox credentials.
3. User enrolled in the Apple Developer Program and paid. The checkout total shown was `$105.93`. Apple order page showed enrollment complete, order placed May 31, 2026.
4. User created the App Store Connect app:
   - Name: `Owl AI: Flash Cards`.
   - Bundle ID: `com.flashcardai.FlashCardAI`.
   - SKU: `owl-ai-ios`.
   - Primary language: English (U.S.).
   - User access: Full Access.
5. We clarified app naming:
   - App Store name should explain purpose: `Owl AI: Flash Cards`.
   - Phone display name should stay short: `Owl AI`.
6. We clarified Bundle ID:
   - It is an Apple app identifier, not a web URL.
   - It does not need to match `mavrylo.com`.
   - Current `com.flashcardai.FlashCardAI` is acceptable because it already matches the iOS bundle id in code.
7. We clarified SKU:
   - SKU is internal to App Store Connect.
   - Users do not see it.
8. User created subscription group and products:
   - Group: `Owl AI Premium`.
   - Monthly: `com.flashcardai.owlai.premium.monthly`.
   - Yearly: `com.flashcardai.owlai.premium.yearly`.
9. We discussed subscription descriptions:
   - User wanted under 55 characters.
   - The App Store Connect description used was `AI translations in 25 languages, cards, reminders.`
10. User added introductory offers:
   - 7-day free trial.
   - Start date: Jun 6, 2026.
   - End date: No End Date.
   - 175 countries/regions.
   - Added for yearly and monthly in App Store Connect.
11. User downloaded App Store Connect API `.p8` key.
12. User gave server-only Apple API identifiers:
   - Issuer ID: `54b26633-d6e9-40aa-b090-363d33ce0843`.
   - Key ID: `A89FK3M993`.
   - Private key path: `/Users/dostonelmurodov/Documents/AppleStoreCertKey/SubscriptionKey_A89FK3M993.p8`.
13. User explicitly said never read the `.p8` file contents. Respect this absolutely.
14. We shifted architecture:
   - v1: no login, no old login UI.
   - future: iCloud/device sync and later optional account linking.
   - backend should keep full word list per device for future sync.
15. We implemented the no-login/device entitlement direction and pushed it.
16. We created this handoff file and then expanded it with missing context.

Local machine/app state discussed:

- User has Xcode installed.
- Xcode version shown by user: `Xcode 26.3`, build `17C529`.
- `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer` asks for the Mac administrator password, not an Apple password.
- User launched app in Xcode on `iPhone 17 Pro`; Xcode showed `SIGTERM` after app was stopped/paused. The console also showed missing generic haptics plist warnings, which are usually simulator/system noise unless tied to an actual crash.

### Full Backend Endpoint Inventory

Public/anonymous endpoints:

```http
GET  /health
GET  /public/legal/terms
GET  /public/legal/privacy
POST /flashcardapp/device/app-attest/bootstrap-challenge
POST /flashcardapp/device/app-attest/register
POST /flashcardapp/device/app-attest/assertion-challenge
POST /flashcardapp/iap/notifications
```

Important notes:

- `/health` is mapped directly in `Program.cs`.
- `/public/legal/terms` and `/public/legal/privacy` are placeholder HTML pages right now.
- App Attest bootstrap/register/assertion-challenge are public because the app needs them before it has a bearer token.
- `/flashcardapp/iap/notifications` is public because Apple posts App Store Server Notifications v2 there; trust comes from Apple JWS verification, not a bearer token.

AI endpoints:

```http
POST /flashcardapp/ai/analyze-word
POST /flashcardapp/ai/word-detail
POST /flashcardapp/ai/extract-words
```

AI behavior:

- Controller: `apps/api-dotnet/Areas/FlashCardApp/Controllers/AiController.cs`.
- Filter: `AiProtectionFilter`.
- Rate limiter: policy `ai`, fixed window 60/minute per device `keyId` when authenticated, otherwise per IP.
- `extract-words` accepts multipart form field `image`, plus `targetLanguage` and `nativeLanguage`; request size limit is 20 MB.
- AI provider path is through `IAiJsonService` / `FallbackAiJsonService`, with Gemini/OpenAI services registered.
- Translation cache is used for analyze-word and word-detail, keyed by normalized word/languages/cache kind/prompt version.

Device-protected no-login endpoints:

```http
GET  /flashcardapp/device/words/count
POST /flashcardapp/device/words/upsert
POST /flashcardapp/device/words/delete
POST /flashcardapp/iap/verify
POST /flashcardapp/iap/token
GET  /flashcardapp/iap/entitlement
```

All of these require:

- `Authorize(AuthenticationSchemes = DeviceAuth.Scheme)`.
- `AppAttestAssertionFilter`.
- Device JWT audience `device`.
- Matching App Attest `keyId` in JWT and headers.

Legacy user/login endpoints still present:

```http
POST /flashcardapp/auth/register
POST /flashcardapp/auth/login
POST /flashcardapp/auth/google
POST /flashcardapp/auth/apple
GET  /flashcardapp/auth/me
GET  /flashcardapp/words
POST /flashcardapp/words
PATCH /flashcardapp/words/{id}
DELETE /flashcardapp/words/{id}
GET  /flashcardapp/categories
POST /flashcardapp/categories
PATCH /flashcardapp/categories/{id}
DELETE /flashcardapp/categories/{id}
GET  /flashcardapp/sync/changes
POST /flashcardapp/sync/push
GET  /flashcardapp/user-settings
POST /flashcardapp/user-settings
PATCH /flashcardapp/user-settings/{id}
```

Legacy endpoint notes:

- Login/register/social endpoints call `LegacyAuthEnabled()`.
- `LegacyAuth:Enabled` defaults to `env.IsDevelopment()` when not configured.
- In Production, set `LegacyAuth__Enabled=false`; then these login endpoints return 404.
- Old user-data endpoints still use the default user JWT scheme, not device JWT.
- iOS no-login v1 should not call them.
- They can remain temporarily for future admin/backward compatibility, but should be isolated or removed later if not needed.

### Backend Config / Secrets Map

Important backend configuration keys:

```text
ConnectionStrings:Default
Jwt:Key
Jwt:Issuer
Jwt:Audience
Jwt:ExpiresMinutes
Jwt:DeviceExpiresMinutes
LegacyAuth:Enabled
AiProtection:Enabled
AiProtection:RequireAssertion
AiProtection:FreeDailyQuota
Apple:ClientId
Apple:TeamId
Apple:SkipSignatureValidation
Apple:AppStoreServer:IssuerId
Apple:AppStoreServer:KeyId
Apple:AppStoreServer:BundleId
Apple:AppStoreServer:PrivateKeyPath
Apple:AppStoreServer:PrivateKey
Apple:AppStoreServer:Environment
Apple:AppStoreServer:BaseUrl
AI:Provider
Gemini:ApiKey
Gemini:Model
OpenAI:ApiKey
OpenAI:Model
Google:ClientIds
```

Production startup guards:

- If not Development and `AiProtection:Enabled` is false, backend throws at startup.
- If not Development and `Apple:SkipSignatureValidation` is true, backend throws at startup.
- `Jwt:Key` is required in all environments and must be at least 32 UTF-8 bytes.

Recommended Linux env template path:

```text
ops/linux/owl-api.env.example
```

Target deployed env path:

```text
/etc/owl-ai/api.env
```

Do not commit real `api.env`.

### App Store Server API Implementation Details

File:

```text
apps/api-dotnet/Services/AppStoreServerClient.cs
```

Implemented backend Apple calls:

```http
GET /inApps/v1/transactions/{transactionId}
GET /inApps/v1/subscriptions/{originalTransactionId}
```

Base URLs:

```text
Sandbox:    https://api.storekit-sandbox.apple.com
Production: https://api.storekit.apple.com
```

Server request JWT:

- Signed ES256.
- Header:
  - `alg=ES256`
  - `kid=<Apple Key ID>`
  - `typ=JWT`
- Claims:
  - `iss=<Issuer ID>`
  - `iat=now`
  - `exp=now + 5 minutes`
  - `aud=appstoreconnect-v1`
  - `bid=<bundle id>`
- Private key comes from `Apple:AppStoreServer:PrivateKey` or `Apple:AppStoreServer:PrivateKeyPath`.
- Current code will read the private key path at runtime, so never point this at a file inside git.

StoreKit local/dev behavior:

- `AppStoreServerClient.IsLocalVerifyEnabled` is true only in `DEBUG` and `Development`.
- In that mode, local StoreKitTest JWS payloads can be decoded without Apple chain verification.
- In production/non-dev, JWS signature verification is required.

Apple JWS verification:

- File: `apps/api-dotnet/Services/AppleJws.cs`.
- Verifies `x5c` chain to embedded Apple Root CA - G3.
- Verifies ES256 signature over `header.payload`.
- Decodes StoreKit transactions, renewal info, and App Store Server Notifications.

Subscription status mapping:

- Apple's `lastTransactions.status` score:
  - 1 active
  - 4 billing grace
  - 3 billing retry
  - 2 expired
  - 5 revoked
- Best transaction is selected by status score.
- `signedRenewalInfo` can update `AutoRenew`, `ProductId`, and grace expiry.
- Notifications handle `REFUND`, `REVOKE`, `DID_FAIL_TO_RENEW`, `EXPIRED`, and `DID_CHANGE_RENEWAL_STATUS` partially.

Refresh behavior:

- `/iap/verify` verifies the presented transaction and then tries to refresh canonical Apple subscription status.
- `/iap/token` and `/iap/entitlement` call `RefreshIfNeededAsync`.
- Refresh is needed when:
  - `LastCheckedAt <= now - 30 minutes`, or
  - `ExpiresAt <= now + 1 day`.
- Apple is not polled on every AI call.

### App Attest Implementation Details

Backend files:

```text
apps/api-dotnet/Services/AppAttestVerifier.cs
apps/api-dotnet/Services/AppAttestClientData.cs
apps/api-dotnet/Services/ChallengeService.cs
apps/api-dotnet/Filters/AppAttestAssertionFilter.cs
apps/api-dotnet/Areas/FlashCardApp/Controllers/DeviceController.cs
```

Challenge model:

- `ChallengeService.Ttl = 2 minutes`.
- Nonce size is 32 bytes.
- Bootstrap challenge is not bound to a key.
- Assertion challenge is bound to:
  - key id
  - request path
  - challenge id
- Challenge is consumed once; replay should fail.

Assertion client-data hash contract:

```text
SHA256(
  challengeBytes
  || 0x1F
  || SHA256(requestBody)
  || 0x1F
  || UTF8(requestPath)
  || 0x1F
  || UTF8(challengeId)
)
```

iOS implementation:

```text
apps/ios-native/Infrastructure/Security/AppAttest/AIIntegrityCoordinator.swift
```

Headers sent on protected requests:

```http
X-App-Attest-Key-Id
X-App-Attest-Assertion
X-App-Attest-Challenge-Id
X-App-Device-Token
X-App-Attest-Client-Data-V: 1
```

Backend currently uses `Authorization`, `X-App-Attest-Key-Id`, `X-App-Attest-Assertion`, and `X-App-Attest-Challenge-Id`. `X-App-Device-Token` is sent by iOS but not needed for backend auth because the same token is in `Authorization: Bearer`.

Simulator path:

- iOS creates `SIMULATOR-<uuid>` key id in DEBUG.
- Backend accepts simulator registration only when development bypass is enabled.
- Simulator device has empty public key and cannot do real assertion.

Real device path:

- iOS generates App Attest key.
- Gets bootstrap challenge.
- Calls `attestKey`.
- Registers with backend.
- Stores key id and device session in Keychain.
- If `attestKey` fails after a key was saved, iOS clears key id because App Attest keys can be one-time attested.

### Device / Subscription Storage Details

Device table:

- Entity: `DeviceEntity`.
- Table: `devices`.
- Unique key: `KeyId`.
- Fields:
  - `Id`
  - `KeyId`
  - `PublicKey`
  - `SignCount`
  - `DeviceUuid`
  - `Environment`
  - `CreatedAt`
  - `LastSeenAt`
- `DeviceUuid` is the StoreKit `appAccountToken` UUID stored in iOS Keychain.

Subscription table:

- Entity: `SubscriptionEntity`.
- Table: `subscriptions`.
- Primary key: `OriginalTransactionId`.
- Linked to current/restored device via `DeviceUuid`.
- Important fields:
  - `ProductId`
  - `Status`
  - `ExpiresAt`
  - `IsTrial`
  - `WasEverPaid`
  - `AutoRenew`
  - `Environment`
  - `LastCheckedAt`
  - `RevokedAt`
- `WasEverPaid` is sticky and distinguishes `expired_trial` vs `expired_paid`.

Entitlement state machine:

- If no subscription: `free`.
- If `RevokedAt` exists: `revoked`.
- If active and explicit status is `grace`: `grace`.
- If active and `IsTrial=true`: `trial`.
- If active and not trial: `premium`.
- If expired and `WasEverPaid=true`: `expired_paid`.
- If expired and never paid: `expired_trial`.

### iOS Runtime Storage / Token Precedence

Keychain stores:

```text
AppAttestKeychainStore
  service: com.flashcardai.FlashCardAI.appAttest
  accounts:
    app_attest.key_id
    app_attest.device_session

TokenStore
  service: com.flashcardai.FlashCardAI.deviceToken
  account:
    iap.access_token

AppAccountTokenStore
  service: com.flashcardai.FlashCardAI.purchases
  account:
    storekit.app_account_token
```

Token precedence in `APIClient.resolvedBearerToken()`:

1. Valid subscription/access token from `TokenStore`.
2. Valid App Attest device session token from `AppAttestKeychainStore`.
3. `APIConfiguration.bearerToken` fallback.

Important:

- `TokenStore` stores the refreshed `/iap/verify` or `/iap/token` access token.
- `AppAttestKeychainStore` stores first device token from registration.
- `EntitlementStore` stores entitlement snapshot in `UserDefaults`.
- `EntitlementStore` has a 14-day offline read grace concept, but full expired-paid/revoked UX is still incomplete.

### iOS Supported Languages

File:

```text
apps/ios-native/Shared/Language/LanguageOption.swift
```

The app currently lists 25 language options:

```text
English (USA) en-us
Spanish es
Turkish tr
Russian ru
Italian it
German de
French fr
Japanese ja
Chinese zh
Cantonese yue
Portuguese pt
Hindi hi
Bengali bn
Indonesian id
Urdu ur
Vietnamese vi
Korean ko
Ukrainian uk
Polish pl
Tajik tg
Uzbek uz
Azerbaijani az
Kazakh kk
Armenian hy
Arabic ar
```

Backend `AiController.LanguageName` currently maps only a subset directly:

```text
en, es, ru, fr, de, it, pt, tr, uz, ar, zh, ja, ko
```

For unsupported codes such as `yue`, `hi`, `bn`, `id`, `ur`, `vi`, `uk`, `pl`, `tg`, `az`, `kk`, `hy`, backend currently falls back to passing the code/name through. This may still work with AI, but polish later by mapping all 25 to friendly names.

### Local StoreKit File vs App Store Connect

Local StoreKit file:

```text
apps/ios-native/Resources/Owl AI.storekit
```

Important values from local StoreKit JSON:

- StoreKit config identifier: `7EBD7E99-0D08-4D7A-A901-0CFA965DB4C0`.
- Subscription group: `Owl AI Premium`.
- Monthly product:
  - Product ID: `com.flashcardai.owlai.premium.monthly`.
  - Display price: `6.99`.
  - Period: `P1M`.
  - Intro offer: free, `P7D`.
  - Local description: `Monthly access to Owl AI Premium`.
- Yearly product:
  - Product ID: `com.flashcardai.owlai.premium.yearly`.
  - Display price: `59.88`.
  - Period: `P1Y`.
  - Intro offer: free, `P7D`.
  - Local description: `Yearly access to Owl AI Premium`.

App Store Connect currently used description:

```text
AI translations in 25 languages, cards, reminders.
```

Potential mismatch:

- App Store Connect yearly price was discussed around `$59.99`, but local StoreKit file has `59.88`.
- This is not fatal for local testing, but decide whether to align local `.storekit` with the real App Store Connect price.

### Public Legal Pages / App Review Cleanup

Current legal controller:

```text
apps/api-dotnet/Areas/Public/Controllers/LegalController.cs
```

Status:

- Terms and Privacy pages are placeholder HTML.
- Privacy page still mentions:
  - account email
  - signing in with Apple
  - vocabulary words/study progress
  - language preferences/reminders
  - OpenAI/Gemini processing
- Because no-login v1 removed login UI, privacy/legal text must be rewritten before App Review.
- Contact emails currently use `support@flashcardai.app` and `privacy@flashcardai.app`; decide whether to keep those or switch to `mavrylo.com` addresses.

Other App Review cleanup:

- `apps/ios-native/Resources/Info.plist` still has:
  - `GIDClientID`
  - `FacebookAppID`
  - `FacebookClientToken`
  - `FacebookDisplayName`
  - Facebook URL schemes
  - Google URL scheme
- `apps/ios-native/Resources/FlashCardAI.entitlements` still has Sign in with Apple entitlement.
- Remove these if no longer needed before App Review because they can trigger review questions about social login/account data.

### Git / Ignore Details

Current tracked/ignored observations:

- `apps/api-dotnet/appsettings.json` is ignored and not tracked.
- `apps/api-dotnet/appsettings.Development.json` is ignored and not tracked.
- `apps/api-dotnet/Migrations/20260606174330_AddDeviceWords.cs` is tracked.
- `apps/api-dotnet/Data/Migrations/.DS_Store` is ignored only because `.DS_Store` is ignored.
- Real migrations are under `apps/api-dotnet/Migrations/`, not `apps/api-dotnet/Data/Migrations/`.
- `apps/ios-native/Resources/Info.plist` is tracked.
- `apps/ios-native/Resources/FlashCardAI.entitlements` is tracked.
- `.p8` files are ignored if accidentally placed inside the repo.

Current remote:

```text
origin = https://github.com/DostonElmurodov/flashcard-ai-ios.git
```

`mavrylo`:

- GitHub repo exists and is private.
- No separate local repo was found.
- `mavrylo.com` is relevant for website/legal/API domain, not the iOS Bundle ID.

### Why Real Sandbox Purchase / Linux Matters

Local StoreKit testing is useful for:

- Paywall UI.
- StoreKit product loading.
- Local purchase/restore flow.
- Local StoreKitTest JWS behavior.

Real Apple sandbox testing is still required because:

- It exercises Apple sandbox transactions.
- It verifies App Store Server API credentials.
- It verifies notification/webhook behavior.
- It verifies real App Attest on an actual device.
- Apple webhooks require a publicly reachable HTTPS endpoint, which localhost cannot provide directly.

Linux server matters because it is the production-like place to run:

- ASP.NET API.
- PostgreSQL.
- nginx HTTPS.
- `/etc/owl-ai/api.env`.
- `/etc/owl-ai/secrets/SubscriptionKey.p8`.
- App Store Server Notifications endpoint.

Do not put Apple `.p8` into GitHub Actions secrets unless explicitly deciding to store private key content there. Current safer direction is file on server referenced by `PrivateKeyPath`.

### Immediate Next Checklist

Most likely next safe tasks:

1. Update this handoff's GitHub state after each commit.
2. Clean iOS old login metadata:
   - remove Google/Facebook keys/schemes from `Info.plist`.
   - remove Sign in with Apple entitlement/capability if unused.
   - update stale onboarding comment.
3. Rewrite placeholder legal pages for no-login/subscription/AI processing.
4. Configure local backend secrets via user-secrets/env without committing:
   - `Jwt:Key`
   - Apple team/bundle/appstore server settings
   - Gemini/OpenAI API key
5. Run backend locally.
6. Run iOS simulator with local StoreKit and dev App Attest bypass.
7. Verify `/iap/verify`, `/iap/token`, and AI protected calls.
8. Align local `.storekit` yearly price if desired.
9. Setup Linux/Postgres/nginx/env/secrets.
10. Test real sandbox purchase and real device App Attest.

## One-Paste Prompt For Another AI

If giving this to another AI, paste this:

```text
You are working in the repo /Users/dostonelmurodov/Projects/Personal/iOSApp/FlashCard AI.

Read docs/ai-handoff-owl-ai-no-login.md fully first, including the "Deep Context / Audit Addendum" section. Then inspect git status and current files. Do not restart from scratch. Continue from the current state.

There are two app areas in one Git repo:
- iOS: apps/ios-native
- Backend: apps/api-dotnet

The product is Owl AI, a no-login iOS flash-card AI app. Backend must be final authority for subscription entitlement and free 10-word AI limit. iOS uses StoreKit 2 + App Attest. Backend uses .NET 10, EF Core, JWT, App Store Server API, App Attest verification.

Never read or print .p8 private key contents. Never commit secrets.

Current state:
- iOS login UI removed.
- Google/Facebook login dependencies removed.
- Old sync disabled in iOS v1.
- Backend has App Attest, IAP, App Store Server API, AI protection, and device_words.
- Backend device_words stores words per deviceId for future sync and currently enforces free 10-word AI limit.
- The handoff addendum contains exact endpoint inventory, config keys, Apple/App Attest details, StoreKit/App Store Connect state, and known App Review cleanup tasks.
- GitHub repo flashcard-ai-ios is private and pushed. The remote history was merged with -s ours to preserve remote history without force-push.

Before editing, run git status. Use rg for search. Use apply_patch for manual edits. Do not revert user changes.

After backend changes run:
dotnet build apps/api-dotnet/FlashCardApi.csproj --no-restore
dotnet test apps/api-dotnet.Tests/FlashCardApi.Tests.csproj --no-restore

After iOS project changes run:
plutil -lint apps/ios-native/FlashCardAI.xcodeproj/project.pbxproj
jq empty 'apps/ios-native/Resources/Owl AI.storekit'
xcodebuild -project apps/ios-native/FlashCardAI.xcodeproj -scheme FlashCardAI -destination 'platform=iOS Simulator,name=iPhone 17 Pro' build

Next recommended work:
1. Configure local/server Apple ASSA secrets without committing anything.
2. Test local StoreKit verify/token flow.
3. Deploy backend to Linux/Postgres/HTTPS.
4. Test real device App Attest and real Sandbox purchase.
5. Finish expired_trial and expired_paid/revoked iOS modes.
6. Add backend security/entitlement tests.
```
