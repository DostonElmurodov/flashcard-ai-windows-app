# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ASP.NET Core (**.NET 10**) Web API — the backend for **Owl AI**, a no-login iOS flash-card app
(`DostonElmurodov/flashcard-ai-ios`). It is the **authority** for device registration/integrity,
subscription entitlement, free-tier limits, Apple StoreKit verification, and AI
translation/caching. The iOS app's UI gating is cosmetic; every protected action is re-validated
here.

`README.md` is the exhaustive reference (config, deploy runbook, troubleshooting). This file is the
fast orientation. `docs/*` and `contracts/openapi/openapi.yaml` are **historical/account-era** and
out of date for the no-login routes — **the controllers are the source of truth**, not the OpenAPI
file.

## Commands

The repo root is the API project; `src/` (3 class libraries) and `tests/` are *excluded* from the
API csproj compile and live as separate assemblies. Solution file is `Mavrylo.slnx`.

```bash
dotnet build Mavrylo.csproj                       # build the API host
dotnet run --launch-profile http                  # run locally -> http://localhost:5289
curl http://localhost:5289/health                 # -> {"ok":true}

dotnet test tests/Mavrylo.Tests.csproj            # full suite
dotnet test tests/Mavrylo.Tests.csproj --filter "FullyQualifiedName~EntitlementServiceTests"
dotnet test tests/Mavrylo.Tests.csproj --filter "DisplayName~free word limit"

dotnet ef migrations add YourName --project src/Mavrylo.Data   # after entity changes
```

- **Docker must be running to test.** The suite mixes in-memory SQLite/`ManualTimeProvider` tests
  with integration/concurrency tests that spin up real PostgreSQL via **Testcontainers**; without
  Docker those fail to start.
- Local dev needs PostgreSQL and an `appsettings.Development.json` (copy `appsettings.example.json`).
  Migrations are applied **automatically on startup** (`db.Database.MigrateAsync()`), so a running
  Postgres + connection string is enough — no manual migrate step.

## Architecture

`Program.cs` is the composition root: registers HttpClients/services, validates production-safety
options, configures Npgsql, the two JWT schemes, snake_case JSON, CORS, the `ai` rate limiter,
auto-applies EF migrations, and maps controllers + `/health`.

Project layout: API host at root → `Areas/OwlAI/Controllers/*` and `Areas/Public/Controllers/*`
(all under base path **`/owlai`**), `Filters/*` → `src/Mavrylo.Services` (all business logic),
`src/Mavrylo.Data` (`AppDbContext` + `Migrations`), `src/Mavrylo.Entities` (EF models).

### Two authentication worlds (the central concept)

| World | Audience | Auth | Used by |
| --- | --- | --- | --- |
| **device** (no-login, v1) | `device` | device JWT + a fresh App Attest assertion on sensitive routes | AI, IAP, device-words |
| **legacy** account | `Jwt:Audience` | normal user JWT | old `/auth`,`/words`,`/categories`,`/sync`,`/user-settings` |

Both schemes share one HS256 key (`Jwt:Key`), differing only by audience. The legacy world is
gated by `LegacyAuthGuardFilter` and returns **404** when `LegacyAuth:Enabled=false` (its
production state). New work targets the device world; do not add dependencies on legacy endpoints.

### AI endpoint authorization — read before touching `/owlai/ai/*`

`AiController` is `[AllowAnonymous]`; its **entire** security lives in `Filters/AiProtectionFilter`
(a *resource* filter, so it can hash the **raw request body before model binding** to verify the
App Attest assertion, and to allow the 20 MB multipart `extract-words` upload).

- When `AiProtection:Enabled=true` it enforces the chain: device-JWT (`401`) → App Attest assertion
  (`403`) → entitlement (`402`) → free daily quota (`429`) → action.
- When `AiProtection:Enabled=false` it is a **pass-through no-op** → the AI routes are **fully
  public**. This is intentional for local dev / early rollout.
- Production **cannot** start unprotected: `Program.cs` throws at startup outside `Development`
  unless both `AiProtection:Enabled` and `AiProtection:RequireAssertion` are true.

Do not assume the normal `[Authorize]` pipeline closes these routes — it does not.

### App Attest assertion binding

Sensitive device routes require a single-use server challenge (2-min TTL). The client-data hash is
`SHA256(challenge ‖ 0x1F ‖ SHA256(body) ‖ 0x1F ‖ path ‖ 0x1F ‖ challengeId)`. The backend verifies
challenge/key/path binding, CBOR validity, `rpIdHash` = `Apple:TeamId.Apple:ClientId`, the
signature against the stored device public key, and a **strictly increasing** sign counter. See
`AppAttestVerifier`, `AppAttestRegistrationService`, `ChallengeService`, `AppAttestClientData`.

### Entitlement (backend is the authority)

`EntitlementService` derives state from `SubscriptionEntity` in this order: `RevokedAt`→`revoked`;
active+grace→`grace`; active trial→`trial`; active paid→`premium`; expired+`WasEverPaid`→
`expired_paid`; expired+never-paid→`expired_trial`; none→`free`. `WasEverPaid` is sticky and
`OriginalTransactionId` is the stable subscription identity (and PK of `subscriptions`). Blocked
states return **402** (the iOS app turns that into a paywall). Free tier =
`DeviceWordService.FreeLimit` (10 active words) + `AiProtection:FreeDailyQuota` (default 40 AI
calls/UTC-day).

### Other cross-cutting facts

- **JSON is global `snake_case`** (`JsonNamingPolicy.SnakeCaseLower`) on every request/response.
- **AI providers**: `FallbackAiJsonService` tries `AI:Provider` (default OpenAI) then the other
  (Gemini); all-fail returns null + throttled dev email alert. Responses are cached in
  `translation_cache`, keyed partly by **prompt version** (bump it to invalidate cache).
- **Apple JWS** (`AppleJws`): StoreKit transactions and App Store Server Notifications are trusted
  by verifying the Apple cert chain + ES256 signature, *not* by a bearer token — the notifications
  webhook (`/owlai/app-store-notifications/notifications`) is public by design.
- **No-login tables**: `devices`, `subscriptions`, `device_words`, `challenges`, `ai_usage`,
  `translation_cache`. Legacy tables (`Users`,`Words`,`Categories`,`UserSettings`) are account-era.

## Production safety & secrets

Startup **fails closed** outside `Development` if AI protection is off, `Apple:SkipSignatureValidation`
is true, required Apple fields are missing, `Jwt:Key` < 32 bytes, or `Jwt:DeviceExpiresMinutes` > 60.
Keep these guards intact. Server-only secrets (Apple `.p8`, JWT key, provider API keys, real
connection strings) live in `/etc/mavrylo/api.env` and `/etc/mavrylo/secrets/` — never in the repo,
the iOS app, logs, or commits. `appsettings.example.json` and `ops/linux/owl-api.env.example` are
the safe templates.
