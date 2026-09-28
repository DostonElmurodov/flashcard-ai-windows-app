# Shared Apple subscriptions and desktop account routes

Implemented 2026-09-08. No production deployment or real Apple purchase was performed.

## Required server configuration

Retain all existing JWT, Apple App Attest, App Store Server API and AI protection settings. New production startup requirements:

```dotenv
AccountAi__DailyQuota=200
AccountAi__RequestsPerMinute=30
```

These are initial recommended cost ceilings; the operator must choose positive values. Both limits apply per account across account AI and authenticated iOS device AI, atomically in PostgreSQL across API replicas. Reservations count attempts before provider invocation, including malformed/provider-failed requests. Free guest iOS quotas are unchanged. The existing account route IP limiter also applies (20 requests/minute). Development with missing AccountAi limits denies account AI.

## API contract

All JSON uses snake_case. Account routes require `Authorization: Bearer <account-access-token>` and the existing active account session check; a revoked refresh-token family invalidates access immediately on the next request.

- `GET /owlai/account/entitlement` returns a flat object: `status`, optional `product_id`, optional `expires_at`, `is_trial`, `auto_renew`, `was_ever_paid`, optional `source` (`apple`), `checked_at`.
- `POST /owlai/account/subscription/apple/claim` returns that same object. Body: `{ "account_id": "<profile.id>", "jws_transaction": "<StoreKit signed transaction>" }`. Additional headers: `X-Device-Authorization: Bearer <device-token>`, `X-App-Attest-Key-Id`, `X-App-Attest-Challenge-Id`, `X-App-Attest-Assertion`. The assertion binds the exact raw body and `/owlai/account/subscription/apple/claim` path. Get its challenge from the existing device assertion-challenge route.
- Claim: 400 invalid transaction, 401 missing/invalid credential, 403 account/body or assertion mismatch, 402 inactive/nonproduction purchase, 409 `subscription_already_linked`, 503 canonical Apple status unavailable. Repeating a claim for the same account is idempotent; a deleted account leaves a nonclaimable tombstone.
- `POST /owlai/account/ai/analyze-word`, `word-detail`, `extract-words`: same request/response contracts as `/owlai/ai/*`. Extract is multipart `image`, `targetLanguage`, `nativeLanguage`. Confirmed shared trial/Premium/grace is required, otherwise 402. Account quota exhaustion returns 429 with Retry-After.
- `POST /owlai/account/public-flashcard-sets/catalog`, `publish`, `unpublish`; `GET .../mine`: existing public-flashcard-set DTOs, owner is authenticated account ID. Publications enter `pending`; only `approved` rows enter catalog. Account publications cannot adopt existing device rows or other accounts via client_set_id.

Existing iOS protected routes retain `Authorization: Bearer <device-token>` plus App Attest and add `X-Account-Authorization: Bearer <account-token>` for shared access. A claimed purchase without a valid account returns device entitlement status `account_required`; AI and device-word creation reject it. Local cards are unaffected. Devices that observed a claimed purchase retain an account requirement if another restore later moves its DeviceUuid. An independently verified unclaimed purchase retains the existing guest behavior.

Entitlement precedence is premium > grace > trial; inactive status honors paid history, then revoked, expired_trial, free. Grace expiry is the Apple grace period end. Owned sources refresh after 30 minutes or near expiry using canonical Apple status; Apple outages preserve the existing cached expiry without extending it. `checked_at` reflects the selected source's last successful server check, so an outage does not present cached state as freshly verified. Production ignores Sandbox/Xcode/LocalTesting. Claims verify Apple signatures even in Development (local unsigned StoreKit configuration cannot create a shared entitlement).

## Schema and rollout

Startup auto-applies two additive migrations:
- `20260908140957_AddSharedSubscriptionOwnership`: owner account FK (SET NULL on deletion), persistent claim timestamp.
- `20260908141805_AddSharedPublicationAndEventSafety`: notification timestamp, persistent device account marker, independent account publication ownership with database XOR constraint and unique account/client_set_id index.

Existing guest subscriptions stay unowned. Claims and IAP/webhook upserts serialize by transaction identity with PostgreSQL transaction advisory locks; ownership arbitration is a conditional atomic UPDATE. Claim locks account before subscription, matching account deletion lock ordering. Logout revokes the session family; deletion clears profile ownership but leaves purchase identity/claim timestamp. Neither action cancels Apple billing.

Deploy the compatible backend before releasing clients. Do not downgrade to an older backend or remove these migrations after purchases have been claimed: old code does not enforce account ownership. Use a forward fix. No migration was applied to a production database during this work.

## Local verification

The SDK and portable PostgreSQL live outside the repository under `D:/owl-ai/.tools`. PostgreSQL test server listens only on `127.0.0.1:55439`; each fixture creates and drops its own database. No production connection is used.

```powershell
$env:DOTNET_CLI_HOME='D:/owl-ai/.tools/dotnet-home'
$env:NUGET_PACKAGES='D:/owl-ai/.tools/nuget'
$env:OWL_TEST_POSTGRES='Host=127.0.0.1;Port=55439;Database=postgres;Username=owl_tests'
& D:/owl-ai/.tools/dotnet/dotnet.exe test tests/Mavrylo.Tests.csproj --no-restore
& D:/owl-ai/.tools/dotnet/dotnet.exe build Mavrylo.csproj --no-restore -c Release
```

Without OWL_TEST_POSTGRES, the standard suite uses Docker/Testcontainers as before. App Attest claim-binding tests use real ECDSA signatures and the real assertion verifier against test-seeded keys. Apple verification orchestration uses fixtures; an actual Apple sandbox purchase/notification and real attested iPhone end-to-end test remain release prerequisites.

### Device entitlement provenance (additive contract)

`/owlai/iap/verify`, `/token`, and `/entitlement` now also return two optional fields inside `entitlement`:
- `resolution_source`: `account` when the account session supplied the returned access state, otherwise `device`.
- `purchase_is_linked`: whether the device subscription row (the presented transaction on verify) has an ownership claim. This remains false for an independent guest purchase even when resolution_source is account.

Clients must not mark a guest purchase linked merely because shared account entitlement source is apple. Keep account-resolved Premium out of the guest entitlement snapshot. Only explicit claim, purchase_is_linked=true, or status=account_required invalidates its guest baseline. Missing provenance from an older server while an account header was supplied must not be written as a guest snapshot. Existing older readers can ignore these additive fields; no schema migration is needed.
