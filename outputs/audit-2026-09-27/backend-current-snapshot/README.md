# Owl AI Backend (`mavrylo`)

ASP.NET Core backend for **Owl AI**, a no-login iOS flash-card app for AI translation,
vocabulary storage, subscription enforcement, and future device/account sync.

The backend is the authority for:

- device registration and request integrity
- subscription entitlement
- free-tier AI and word limits
- Apple StoreKit transaction verification
- AI provider fallback and translation caching
- server-side storage of device words for enforcement and future sync

The iOS app may hide or show UI for a better user experience, but every protected action is
validated again on the backend.

- iOS client repository: `DostonElmurodov/flashcard-ai-ios`
- backend repository: `DostonElmurodov/mavrylo`
- production public API: `https://api.mavrylo.com`
- local dev API: `http://localhost:5289`

## Contents

- [Current State](#current-state)
- [Production Status And Manual Setup](#production-status-and-manual-setup)
- [Recent Improvements](#recent-improvements)
- [What This API Does](#what-this-api-does)
- [Stack](#stack)
- [Architecture](#architecture)
- [Repository Layout](#repository-layout)
- [Request Flows](#request-flows)
- [Local Development](#local-development)
- [Configuration](#configuration)
  - [PostgreSQL IP Restrictions And Firewall](#postgresql-ip-restrictions-and-firewall)
- [Database](#database)
  - [Reset Development Test Data](#reset-development-test-data)
- [Security Model](#security-model)
- [Entitlements](#entitlements)
- [API Reference](#api-reference)
- [AI Providers](#ai-providers)
- [Rate Limits And Quotas](#rate-limits-and-quotas)
- [Data Model](#data-model)
- [Deployment](#deployment)
- [CI/CD](#cicd)
- [Tests](#tests)
- [Known Gaps](#known-gaps)
- [Security Rules](#security-rules)
- [Troubleshooting](#troubleshooting)

## Current State

This repository currently contains the backend API only. It is a .NET 10 Web API project with an
xUnit test project under `tests/`.

The no-login backend flow is implemented:

- App Attest bootstrap, registration, assertion challenge, and assertion verification
- device JWT issuance
- protected AI endpoints
- protected IAP endpoints
- protected device-word endpoints
- StoreKit transaction verification and App Store Server API refresh
- App Store Server Notifications v2 webhook
- free 10-word backend enforcement
- daily AI call quota for free users
- translation cache for AI responses
- Docker image build and GHCR publish through GitHub Actions

The legacy account-based endpoints still exist in the codebase. They are retained for possible
future migration/admin work, but the no-login iOS v1 should not call them. In production,
`LegacyAuth:Enabled` should stay `false`.

Production is currently deployed at `https://api.mavrylo.com`. The live path is:

```text
api.mavrylo.com
  -> nginx TLS reverse proxy
  -> http://127.0.0.1:5289
  -> Docker container port 8080
  -> PostgreSQL on the host
```

## Production Status And Manual Setup

The domain and API route are live:

```bash
curl -i https://api.mavrylo.com/health
curl -i -X POST https://api.mavrylo.com/owlai/app-attest/bootstrap-challenge
```

Expected responses are JSON (`{"ok":true}` for health and a `challenge` plus `challenge_id` for
the bootstrap challenge). If nginx returns a text placeholder, the server is not proxying to the
Docker API container.

The current server convention is:

```text
/opt/apps/mavrylo-api/docker-compose.yml
/etc/mavrylo/api.env
/etc/mavrylo/secrets/SubscriptionKey.p8
/var/log/mavrylo-api
```

Manual steps that still matter:

1. **Deploy new images with pull + recreate.** `docker compose restart` only restarts the existing
   container. To get the newest GHCR image, use:

   ```bash
   cd /opt/apps/mavrylo-api
   docker compose pull api
   docker compose up -d --force-recreate
   docker logs --tail=80 mavrylo-api
   ```

2. **Keep the App Store Server Notifications URL current.** In App Store Connect, configure
   `https://api.mavrylo.com/owlai/app-store-notifications/notifications` for both Sandbox and
   Production, using **Version 2** notifications. Until this is done, subscription events
   (renew/refund/revoke) are not delivered to the backend. Full steps are in
   `ops/linux/SERVER-SETUP.md`.

3. **Keep Apple identifiers aligned.** `Apple__ClientId` and
   `Apple__AppStoreServer__BundleId` must match the iOS bundle id (`com.mavrylo.owlai`). Allowed
   product ids must match App Store Connect:
   `com.flashcardai.owlai.premium.monthly` and `com.flashcardai.owlai.premium.yearly`.

4. **Keep the App Store Server API `.p8` file only on the server.** The deployed path is
   `/etc/mavrylo/secrets/SubscriptionKey.p8`, mounted read-only into the container.

## Recent Improvements

Recent production hardening and real-device compatibility work:

- The live server now serves the real API behind nginx instead of the old placeholder response.
- Docker uses bridge networking so the API container can reach host PostgreSQL through
  `172.17.0.1:5432`.
- Production startup now fails closed when required Apple/IAP/App Attest settings are missing.
- App Attest verification was adjusted for real iPhone output while keeping the security checks:
  the server no longer requires the WebAuthn user-present bit, tolerates assertion authenticator
  data that includes Apple's attested-credential flag, and accepts the signature form observed from
  `DCAppAttestService.generateAssertion`.
- The backend still enforces `rpIdHash`, key id, single-use challenge, request path binding, and
  monotonic counters for protected requests.

## What This API Does

Owl AI helps a user translate and study vocabulary. The app can ask the backend to:

- analyze a word or phrase
- return multiple translations, IPA pronunciation, part of speech, examples, and example
  translations
- extract vocabulary from an uploaded image
- store a device's word inventory on the backend
- verify whether the device is free, in trial, premium, grace, expired, or revoked
- enforce the free plan limit before AI money is spent
- verify Apple subscription state even if the client UI is modified

The product is intentionally **no-login** for v1. There is no email/password account requirement.
The device is identified through Apple App Attest and a device-scoped UUID used with StoreKit.

## Stack

| Layer | Current implementation |
| --- | --- |
| Runtime | .NET 10 / ASP.NET Core Web API |
| ORM | Entity Framework Core 10 |
| Database | PostgreSQL through Npgsql |
| Auth | JWT bearer with two audiences: user and device |
| Device integrity | Apple App Attest |
| Subscriptions | StoreKit 2 on iOS, App Store Server API on backend |
| AI providers | OpenAI first by default, Gemini fallback; order configurable |
| JSON format | `snake_case` through global `JsonNamingPolicy.SnakeCaseLower` |
| Email alerts | SMTP through MailKit |
| Tests | xUnit |
| Container | Docker multi-stage build |
| CI/CD | GitHub Actions, GHCR image publish, manual deploy over SSH |

Target framework: `net10.0`.

## Architecture

The API has two authentication worlds:

1. **No-login device world**
   - Used by AI, IAP, and device-word endpoints.
   - Authenticated by a device JWT with audience `device`.
   - Sensitive requests also require a fresh Apple App Attest assertion.
   - This is the main architecture for Owl AI v1.

2. **Legacy user-account world**
   - Used by old `/auth`, `/words`, `/categories`, `/sync`, and `/user-settings` endpoints.
   - Authenticated by a normal JWT with audience `Jwt:Audience`.
   - Disabled outside development unless explicitly enabled.
   - Not used by the no-login iOS app.

Startup composition lives in `Program.cs`:

- registers HTTP clients and services
- validates production safety options
- configures PostgreSQL
- configures the two JWT bearer schemes
- configures JSON naming
- configures CORS for local frontend origins
- configures the `ai` rate limiter
- applies EF migrations automatically
- maps controllers and `/health`

## Repository Layout

```text
mavrylo/
|-- Mavrylo.csproj
|-- Program.cs
|-- appsettings.example.json
|-- Dockerfile
|-- README.md
|-- Areas/
|   |-- OwlAI/Controllers/
|   |   |-- AiController.cs
|   |   |-- AppStoreNotificationsController.cs
|   |   |-- AuthController.cs
|   |   |-- CategoriesController.cs
|   |   |-- DeviceController.cs
|   |   |-- DeviceWordsController.cs
|   |   |-- IapController.cs
|   |   |-- SyncController.cs
|   |   |-- UserSettingsController.cs
|   |   `-- WordsController.cs
|   `-- Public/Controllers/
|       `-- LegalController.cs
|-- Filters/
|   |-- AiProtectionFilter.cs
|   `-- AppAttestAssertionFilter.cs
|-- src/
|   |-- Mavrylo.Data/
|   |   |-- AppDbContext.cs
|   |   `-- Migrations/
|   |-- Mavrylo.Entities/
|   |   `-- Models/
|   `-- Mavrylo.Services/
|       |-- Dtos/
|       |-- Mapping/
|       |-- Resources/Certificates/
|       `-- Services/
|-- contracts/
|-- docs/
|-- ops/linux/
`-- tests/
```

Important notes:

- `tests/**` is excluded from the API project in `Mavrylo.csproj`, because test files belong
  only to `tests/Mavrylo.Tests.csproj`.
- `contracts/openapi/openapi.yaml` currently describes older account-era API paths and should be
  refreshed before being treated as the authoritative public contract. The controllers are the
  current source of truth.
- `docs/ai-handoff-owl-ai-no-login.md` and `docs/migration/*` are historical planning/handoff
  notes. They are useful context, but may contain old paths or old decisions.

## Request Flows

### Device Registration

The first launch of the no-login app follows this shape:

1. iOS creates or loads an App Attest key.
2. iOS calls `POST /owlai/app-attest/bootstrap-challenge`.
3. The backend creates a short-lived bootstrap challenge in `challenges`.
4. iOS calls Apple's `attestKey` with the challenge.
5. iOS calls `POST /owlai/app-attest/register`.
6. The backend verifies:
   - attestation CBOR format
   - Apple App Attest certificate chain
   - nonce embedded in the leaf certificate
   - `rpIdHash` derived from `Apple:TeamId` and `Apple:ClientId`
   - credential id matching the supplied App Attest key id
7. The backend stores a row in `devices`.
8. The backend returns the first device JWT with `ent=free`.

Simulator development uses a `SIMULATOR-*` key id. That bypass is available only in a DEBUG build
and `Development` environment.

### Protected Request

For protected device routes, the app must:

1. Have a valid device JWT.
2. Request an assertion challenge with the target request path.
3. Sign an App Attest assertion over the server challenge, request body hash, path, and challenge id.
4. Send the protected request with:

```http
Authorization: Bearer <device-jwt>
X-App-Attest-Key-Id: <key-id>
X-App-Attest-Assertion: <base64-cbor-assertion>
X-App-Attest-Challenge-Id: <challenge-id>
```

The backend consumes each assertion challenge once and enforces a monotonic App Attest counter.

### AI Request

AI endpoints use `AiProtectionFilter`. When `AiProtection:Enabled=true`, the filter runs before
model binding so it can hash the exact raw request body.

Pipeline:

```text
device JWT -> App Attest assertion -> entitlement -> free word-slot reservation -> daily quota -> controller action
```

Free users are allowed to use AI for their first 10 active saved words. For JSON word-detail calls,
the backend reserves a `device_words` slot before calling the AI provider. This prevents a custom
client from spending backend AI budget without saving the word.

Multipart image extraction cannot reserve a specific word before AI, because the words are unknown
until after the image is processed. It is still protected by device JWT, App Attest, entitlement,
daily quota, and the fixed-window rate limiter.

### Subscription Verification

1. iOS completes a StoreKit 2 purchase or restore.
2. iOS sends the signed transaction JWS to `POST /owlai/iap/verify`.
3. The backend decodes/verifies the JWS through `AppleJws`.
4. If App Store Server API is configured, the backend refreshes canonical status from Apple.
5. `EntitlementService` upserts the subscription and computes the entitlement state.
6. The backend returns the entitlement plus a refreshed device JWT.

The `originalTransactionId` is the stable subscription identity. `WasEverPaid` is sticky and is
used to distinguish `expired_trial` from `expired_paid`.

### App Store Notifications

Apple can call:

```http
POST /owlai/app-store-notifications/notifications
```

This endpoint is public. Trust is not based on a bearer token; trust is based on Apple JWS
verification. Notifications can update refund/revoke, renewal status, grace period, and expiration
state while the app is offline.

### Legacy User Flow

Legacy endpoints support email/password, Google tokeninfo, and development-only Apple token decode.
They use the default JWT audience, not the device audience. New no-login app code should not depend
on these endpoints.

## Local Development

### Requirements

- .NET 10 SDK
- PostgreSQL 14 or newer
- optional: `dotnet-ef` for creating new migrations

### Create Local Database

```sql
CREATE USER owl_ai WITH PASSWORD 'CHANGE_ME';
CREATE DATABASE owl_ai OWNER owl_ai;
GRANT ALL PRIVILEGES ON DATABASE owl_ai TO owl_ai;
```

For PostgreSQL 15 or newer, also grant schema ownership:

```sql
\c owl_ai
GRANT ALL ON SCHEMA public TO owl_ai;
ALTER SCHEMA public OWNER TO owl_ai;
```

### Create Local Configuration

Copy the safe template:

```bash
cp appsettings.example.json appsettings.Development.json
```

Fill in at least:

- `ConnectionStrings:Default`
- `Jwt:Key`
- `Jwt:Issuer`
- `Jwt:Audience`
- `Apple:TeamId`
- `Apple:ClientId`
- one AI provider API key

Generate a JWT key:

```bash
openssl rand -base64 64
```

You can also use environment variables. Use double underscores for nested keys:

```bash
export Jwt__Key="a-long-random-server-secret-at-least-32-bytes"
export OpenAI__ApiKey="your-openai-key"
export Gemini__ApiKey="your-gemini-key"
```

### Run

```bash
dotnet run --launch-profile http
```

The launch profile listens on:

```text
http://localhost:5289
```

Health check:

```bash
curl http://localhost:5289/health
```

Expected response:

```json
{"ok":true}
```

### Build And Test

```bash
dotnet build Mavrylo.csproj
dotnet test tests/Mavrylo.Tests.csproj
```

## Configuration

All configuration can come from:

- `appsettings.example.json` as documentation/template
- `appsettings.Development.json` for local development, gitignored
- .NET user secrets
- environment variables
- `/etc/mavrylo/api.env` in production

Use `__` instead of `:` in environment variables.

On the production server, the PostgreSQL connection string is stored in `/etc/mavrylo/api.env` as
`ConnectionStrings__Default`. Inspect it with:

```bash
sudo grep 'ConnectionStrings__Default' /etc/mavrylo/api.env
```

Do not paste the full value into tickets, chats, screenshots, or commits because it contains the
database password. To inspect the env file with sensitive values masked:

```bash
sudo sed -E \
-e 's/(Password|Key|Secret|Token|ApiKey|PrivateKey|ConnectionStrings__Default)=.*/\1=***HIDDEN***/Ig' \
-e 's/(Jwt__Key|Gemini__ApiKey|OpenAI__ApiKey|Google__ClientSecret|Apple__AppStoreServer__PrivateKeyPath)=.*/\1=***HIDDEN***/Ig' \
/etc/mavrylo/api.env
```

For the current Docker-on-host PostgreSQL setup, the connection string usually points the API
container at host PostgreSQL through Docker bridge networking, for example `Host=172.17.0.1;Port=5432;Database=mavrylo_prod;Username=mavrylo_app;Password=...`.

### PostgreSQL IP Restrictions And Firewall

Production PostgreSQL should not be open to the public internet. For the current server setup,
PostgreSQL is intended to be reachable only from:

- localhost: `127.0.0.1/32` and `::1/128`
- Docker bridge clients: `172.17.0.0/16`
- optionally, one trusted external admin IP as a `/32`

Check what PostgreSQL listens on:

```bash
sudo grep -n "listen_addresses" /etc/postgresql/16/main/postgresql.conf
sudo lsof -nP -iTCP:5432 -sTCP:LISTEN
```

Check database-level client restrictions:

```bash
sudo tail -80 /etc/postgresql/16/main/pg_hba.conf
```

The current safe Docker-only rule is:

```text
host mavrylo_prod mavrylo_app 172.17.0.0/16 scram-sha-256
```

Check the Ubuntu firewall:

```bash
sudo ufw status verbose
```

The current safe firewall shape is:

```text
22/tcp                     ALLOW IN    Anywhere
80/tcp                     ALLOW IN    Anywhere
443/tcp                    ALLOW IN    Anywhere
172.17.0.1 5432/tcp on docker0 ALLOW IN 172.17.0.0/16
```

Do not add `0.0.0.0/0` or `Anywhere` for PostgreSQL port `5432`.

If temporary external database access is needed from one trusted admin IP, add only that exact IP
as a `/32` in both PostgreSQL and UFW. Replace `YOUR_PUBLIC_IP` with the admin machine's current
public IP:

```bash
echo "host mavrylo_prod mavrylo_app YOUR_PUBLIC_IP/32 scram-sha-256" | sudo tee -a /etc/postgresql/16/main/pg_hba.conf
sudo ufw allow from YOUR_PUBLIC_IP to any port 5432 proto tcp comment 'PostgreSQL admin access'
sudo systemctl reload postgresql
```

If PostgreSQL is still listening only on localhost and external admin access is required, update
`listen_addresses` carefully and restart PostgreSQL:

```bash
sudo -u postgres psql -c "ALTER SYSTEM SET listen_addresses = '*';"
sudo systemctl restart postgresql
```

After testing, remove the external UFW rule and the matching `pg_hba.conf` line. Keeping database
access restricted to Docker and localhost is the preferred production posture.

| Key | Required | Purpose |
| --- | --- | --- |
| `ConnectionStrings:Default` | yes | PostgreSQL connection string |
| `Jwt:Key` | yes | HS256 signing key, at least 32 UTF-8 bytes |
| `Jwt:Issuer` | yes | JWT issuer, normally `https://api.mavrylo.com` |
| `Jwt:Audience` | yes | legacy user-token audience |
| `Jwt:ExpiresMinutes` | yes | legacy user-token lifetime |
| `Jwt:DeviceExpiresMinutes` | yes | device-token lifetime |
| `LegacyAuth:Enabled` | prod yes | enables legacy login endpoints when true |
| `AiProtection:Enabled` | prod yes | protects AI endpoints with device JWT and entitlement |
| `AiProtection:RequireAssertion` | prod yes | requires App Attest assertion on AI calls |
| `AiProtection:FreeDailyQuota` | yes | successful AI calls per UTC day for free devices |
| `AI:Provider` | yes | primary provider, `OpenAI` by default, or `Gemini` |
| `OpenAI:ApiKey` | if used | OpenAI API key |
| `OpenAI:Model` | if used | OpenAI model, default is `gpt-6-luna`; text and image requests explicitly use `reasoning.effort=none` |
| `Gemini:ApiKey` | if used | Google Gemini API key |
| `Gemini:Model` | if used | Gemini model, default template is `gemini-2.5-flash` |
| `Apple:ClientId` | yes | iOS bundle id, used in App Attest rp id |
| `Apple:TeamId` | yes | Apple developer team id |
| `Apple:SkipSignatureValidation` | prod yes | must be false outside development |
| `Apple:AppStoreServer:IssuerId` | IAP yes | App Store Server API issuer id |
| `Apple:AppStoreServer:KeyId` | IAP yes | App Store Server API key id |
| `Apple:AppStoreServer:BundleId` | IAP yes | Apple bundle id |
| `Apple:AppStoreServer:PrivateKeyPath` | IAP yes | path to `.p8` private key on server |
| `Apple:AppStoreServer:PrivateKey` | optional | inline private key alternative; avoid unless needed |
| `Apple:AppStoreServer:Environment` | IAP yes | `Sandbox` or `Production` |
| `Apple:AppStoreServer:BaseUrl` | optional | override Apple API URL |
| `Google:ClientIds` | legacy | allowed Google OAuth client ids for legacy auth |

### Server Production Settings

The production server reads configuration from:

```text
/etc/mavrylo/api.env
```

That file is mounted into the Docker container by `docker-compose.yml`. It must exist on the
server before deploy, and it must never be committed to git.

Recommended server files:

```text
/etc/mavrylo/api.env
/etc/mavrylo/secrets/SubscriptionKey.p8
/opt/apps/mavrylo-api/docker-compose.yml
```

Recommended permissions:

```bash
sudo chown root:docker /etc/mavrylo/api.env
sudo chmod 640 /etc/mavrylo/api.env
sudo chown root:docker /etc/mavrylo/secrets/SubscriptionKey.p8
sudo chmod 640 /etc/mavrylo/secrets/SubscriptionKey.p8
```

Production env template:

```env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080

ConnectionStrings__Default=Host=172.17.0.1;Port=5432;Database=mavrylo_prod;Username=mavrylo_app;Password=CHANGE_ME
ForwardedHeaders__KnownProxies=172.17.0.1

Jwt__Key=CHANGE_ME_LONG_RANDOM_64_PLUS_CHARS
Jwt__Issuer=https://api.mavrylo.com
Jwt__Audience=mavrylo
Jwt__ExpiresMinutes=10080
Jwt__DeviceExpiresMinutes=60

LegacyAuth__Enabled=false

AiProtection__Enabled=true
AiProtection__RequireAssertion=true
AiProtection__FreeDailyQuota=40

Apple__ClientId=com.mavrylo.owlai
Apple__TeamId=CHANGE_ME_TEAM_ID
Apple__SkipSignatureValidation=false

Apple__AppStoreServer__IssuerId=CHANGE_ME_ISSUER_ID
Apple__AppStoreServer__KeyId=CHANGE_ME_KEY_ID
Apple__AppStoreServer__BundleId=com.mavrylo.owlai
Apple__AppStoreServer__PrivateKeyPath=/etc/mavrylo/secrets/SubscriptionKey.p8
Apple__AppStoreServer__Environment=Sandbox
Apple__AppStoreServer__AllowedProductIds=com.flashcardai.owlai.premium.monthly,com.flashcardai.owlai.premium.yearly

AI__Provider=OpenAI
OpenAI__ApiKey=CHANGE_ME
OpenAI__Model=gpt-6-luna

Gemini__ApiKey=
Gemini__Model=gemini-2.5-flash

Google__ClientIds=
```

Where each production value comes from:

| Env variable | Where to get it | Notes |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | fixed value | Use `Production` on the server. |
| `ASPNETCORE_URLS` | fixed value for Docker | Use `http://0.0.0.0:8080`; Docker maps host `127.0.0.1:5289` to container `8080`. |
| `ConnectionStrings__Default` | server PostgreSQL setup | If PostgreSQL runs on the host and API runs in Docker bridge mode, host is usually `172.17.0.1`. |
| `ForwardedHeaders__KnownProxies` | Docker bridge gateway | Usually `172.17.0.1` when nginx runs on the host. |
| `Jwt__Key` | generate on server | Run `openssl rand -base64 64`; keep it one line and secret. |
| `Jwt__Issuer` | public API URL | Usually `https://api.mavrylo.com`. |
| `Jwt__Audience` | app/backend contract | Legacy user-token audience; current production uses `mavrylo`. |
| `Jwt__ExpiresMinutes` | app policy | Legacy token lifetime. |
| `Jwt__DeviceExpiresMinutes` | app policy | Must be `60` or less outside Development. |
| `LegacyAuth__Enabled` | app policy | Usually `false` for the no-login app mode. |
| `AiProtection__Enabled` | app policy | Must be `true` outside Development. |
| `AiProtection__RequireAssertion` | app policy | Must be `true` outside Development. |
| `AiProtection__FreeDailyQuota` | product policy | Free AI calls per device per UTC day. |
| `Apple__ClientId` | Apple bundle identifier | Usually `com.mavrylo.owlai`; must match the iOS app. |
| `Apple__TeamId` | Apple Developer account | Apple Developer -> Account -> Membership details -> Team ID. It is a short value like `ABCDE12345`, not the bundle id. |
| `Apple__SkipSignatureValidation` | fixed production safety value | Must be `false` outside Development. |
| `Apple__AppStoreServer__IssuerId` | App Store Connect API | App Store Connect -> Users and Access -> Integrations -> App Store Connect API. |
| `Apple__AppStoreServer__KeyId` | App Store Connect API key | The Key ID shown for the `.p8` key in App Store Connect. |
| `Apple__AppStoreServer__BundleId` | Apple bundle identifier | Usually same as `Apple__ClientId`: `com.mavrylo.owlai`. |
| `Apple__AppStoreServer__PrivateKeyPath` | server file path | Use `/etc/mavrylo/secrets/SubscriptionKey.p8` and mount `/etc/mavrylo/secrets` read-only into Docker. |
| `Apple__AppStoreServer__Environment` | Apple purchase environment | Use `Sandbox` while testing sandbox purchases; switch to `Production` for live App Store transactions. |
| `Apple__AppStoreServer__AllowedProductIds` | App Store Connect products | Comma-separated in-app purchase product ids allowed by the backend. |
| `AI__Provider` | backend policy | `OpenAI` or `Gemini`. |
| `OpenAI__ApiKey` | OpenAI Platform | Required when `AI__Provider=OpenAI`. |
| `OpenAI__Model` | OpenAI model choice | Template uses `gpt-6-luna` with reasoning disabled. Any override must support `reasoning.effort=none`. |
| `Gemini__ApiKey` | Google AI Studio | Required when `AI__Provider=Gemini`. |
| `Gemini__Model` | Gemini model choice | Template uses `gemini-2.5-flash`. |
| `Google__ClientIds` | Google Cloud Console | Legacy auth only; leave empty if unused. |

Docker Compose on the server should point at the env file and private key directory:

```yaml
services:
  api:
    image: ghcr.io/dostonelmurodov/mavrylo:latest
    container_name: mavrylo-api
    restart: unless-stopped
    network_mode: bridge
    env_file:
      - /etc/mavrylo/api.env
    ports:
      - "127.0.0.1:5289:8080"
    volumes:
      - /etc/mavrylo/secrets:/etc/mavrylo/secrets:ro
      - /var/log/mavrylo-api:/app/logs
```

Required one-time server login for a private GHCR package:

```bash
echo YOUR_GITHUB_PAT_WITH_READ_PACKAGES | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin
```

The deploy workflow also performs this login automatically by using GitHub Actions secrets, but
manual server login is still useful for direct server debugging.

Useful server checks:

```bash
cd /opt/apps/mavrylo-api
docker compose ps
docker compose logs --tail=200 api
curl -fsS http://127.0.0.1:5289/health
```

### Production Startup Guards

Outside `Development`, startup fails if:

- `AiProtection:Enabled` is not `true`
- `AiProtection:RequireAssertion` is not `true`
- `Apple:SkipSignatureValidation` is `true`
- required Apple production fields such as `Apple:TeamId`, `Apple:ClientId`,
  `Apple:AppStoreServer:BundleId`, `Apple:AppStoreServer:Environment`, or
  `Apple:AppStoreServer:AllowedProductIds` are missing
- `Jwt:Key` is missing or shorter than 32 UTF-8 bytes
- `Jwt:DeviceExpiresMinutes` is greater than 60

This is intentional. A misconfigured production backend should fail closed.

## Database

The API uses PostgreSQL everywhere. `Program.cs` always configures Npgsql.

Migrations live in `src/Mavrylo.Data/Migrations/` and are applied automatically on startup:

```csharp
await db.Database.MigrateAsync();
```

Create a new migration after model changes:

```bash
dotnet ef migrations add YourMigrationName
```

The main schema includes:

- account-era tables: `Users`, `Words`, `Categories`, `UserSettings`
- no-login tables: `devices`, `subscriptions`, `device_words`, `challenges`, `ai_usage`,
  `public_flashcard_sets`
- AI cache table: `translation_cache`
- EF history table: `__EFMigrationsHistory`

### Reset Development Test Data

Use this only for development or sandbox testing. It removes backend state that the iOS app uses
for App Attest registration, StoreKit entitlement, free-tier limits, and server-side device words.

To fully reset the no-login test backend:

```bash
sudo -u postgres psql -d mavrylo_prod
```

Then run:

```sql
begin;

truncate table
  subscriptions,
  device_words,
  devices,
  challenges,
  ai_usage
restart identity cascade;

commit;
```

Exit `psql`:

```sql
\q
```

After a full reset, reinstall the iOS app so it creates a fresh App Attest registration:

1. Stop the app in Xcode.
2. Delete the app from the iPhone.
3. Run the app again from Xcode.

If you only need to reset subscription and App Attest state while keeping server-side words, use:

```sql
begin;

truncate table
  subscriptions,
  devices,
  challenges,
  ai_usage
restart identity cascade;

commit;
```

If App Store Sandbox still reports an active subscription, the backend may become premium again
after the app calls `/owlai/iap/token` or restore. In that case, expire/reset the sandbox
subscription in App Store Connect or test with another sandbox tester.

## Security Model

### JWT Schemes

Two JWT bearer schemes share the same HS256 signing key but use different audiences.

| Scheme | Audience | Used by |
| --- | --- | --- |
| default | `Jwt:Audience` | legacy account endpoints |
| `device` | `device` | no-login device endpoints |

Device JWT claims:

| Claim | Meaning |
| --- | --- |
| `sub` | App Attest key id |
| `keyId` | App Attest key id |
| `ent` | entitlement state |
| `otid` | optional Apple original transaction id |

### App Attest Assertions

Protected device endpoints require a fresh assertion. The server challenge has a two-minute TTL and
is consumed once.

The assertion client-data hash is:

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

The backend verifies:

- challenge exists, is unexpired, and is bound to the key id and path
- assertion is valid CBOR
- `rpIdHash` matches `Apple:TeamId.Apple:ClientId`
- signature verifies against the stored device public key
- sign counter is strictly increasing

### Apple JWS

StoreKit transactions and App Store Server Notifications are Apple JWS values. In production,
`AppleJws` verifies the certificate chain to the embedded Apple Root CA - G3 and verifies the ES256
signature. Local StoreKit testing can decode without full chain validation only in DEBUG +
Development.

### AI Endpoint Authorization

> [!CAUTION]
> The AI endpoints (`/owlai/ai/*`) are **not** guarded by an `[Authorize]` attribute. Their entire
> security lives **inside `AiProtectionFilter`**, which becomes a no-op when
> `AiProtection:Enabled=false`. Do **not** assume these routes are closed by the normal
> authorization pipeline: in Development with the filter disabled they are **fully public**.
> Production cannot start in that state (see below), but locally they are open by design.

**What this is.** Every other protected controller closes itself with an attribute
(`[Authorize(...)]`). `AiController` instead carries `[AllowAnonymous]` and delegates all
authorization to a single resource filter, `AiProtectionFilter`. It is a resource filter (not a
normal `[Authorize]`) on purpose: it must read the **raw request body before model binding** to
recompute the App Attest assertion hash, and `extract-words` is a multipart upload up to 20 MB.

**How it works.**

- When `AiProtection:Enabled=true`, the filter enforces the full chain on every AI call:
  device-JWT (`401`) → App Attest assertion (`403`) → entitlement (`402`) → free daily quota
  (`429`) → action.
- When `AiProtection:Enabled=false`, the filter is a **pass-through no-op** and the AI endpoints are
  open. This is intentional for local development and the early iOS rollout.
- **Production cannot run unprotected.** `Program.cs` throws at startup outside `Development` unless
  both `AiProtection:Enabled=true` and `AiProtection:RequireAssertion=true`. So in production the AI
  routes are always behind device-JWT + App Attest, even though there is no `[Authorize]` attribute.
- Because the controller is `[AllowAnonymous]`, the fail-closed `RequireAuthorization()` default
  does not apply to it — the filter, using the **device** authentication scheme, stays the single
  source of truth instead of the default user scheme.

**Why we need it this way.**

- A standard `[Authorize]` runs *after* model binding has already consumed the body, so it cannot
  bind the App Attest assertion to the exact raw body + path. The resource filter can.
- The `AiProtection:Enabled` switch lets local development and early iOS phases call AI without App
  Attest, while the production startup guard guarantees the switch can never be left off in
  production.

**Known trade-off.** Adding a hard `[Authorize(AuthenticationSchemes = DeviceAuth.Scheme)]` to
`AiController` would protect it even against a configuration mistake, but it would **break the
intended “public AI” mode in Development**. It is left as-is today because the production startup
guard already makes the unprotected state impossible in production.

## Entitlements

`EntitlementService` computes the current entitlement from `SubscriptionEntity`.

| State | Meaning | AI | Add words | Review/notifications |
| --- | --- | --- | --- | --- |
| `free` | no subscription | allowed for first 10 words | up to 10 active words | allowed |
| `trial` | active free trial | allowed | allowed | allowed |
| `premium` | active paid subscription | allowed | allowed | allowed |
| `grace` | billing retry grace period | allowed | allowed | allowed |
| `expired_trial` | trial expired, never paid | blocked with 402 | blocked | client should restrict to free behavior |
| `expired_paid` | paid subscription lapsed | blocked with 402 | blocked | client can keep existing words read-only |
| `revoked` | refunded or revoked by Apple | blocked with 402 | blocked | client should treat as revoked/read-only |

Resolution order:

1. `RevokedAt` means `revoked`.
2. Active subscription with explicit `grace` status means `grace`.
3. Active trial means `trial`.
4. Active non-trial means `premium`.
5. Expired and `WasEverPaid=true` means `expired_paid`.
6. Expired and never paid means `expired_trial`.
7. No subscription means `free`.

## API Reference

Base path for OwlAI endpoints: `/owlai`.

JSON request and response names are `snake_case`.

### Public

```http
GET  /health
GET  /owlai/legal/terms
GET  /owlai/legal/privacy
POST /owlai/app-attest/bootstrap-challenge
POST /owlai/app-attest/register
POST /owlai/app-attest/assertion-challenge
POST /owlai/app-store-notifications/notifications
```

### Device-Protected

These require device JWT plus App Attest assertion.

```http
POST /owlai/iap/verify
POST /owlai/iap/token
GET  /owlai/iap/entitlement
GET  /owlai/device-words/count
POST /owlai/device-words/upsert
POST /owlai/device-words/delete
POST /owlai/public-flashcard-sets/publish
GET  /owlai/public-flashcard-sets/mine
POST /owlai/public-flashcard-sets/unpublish
POST /owlai/public-flashcard-sets/catalog
```

#### Public flashcard catalog

All four public-flashcard-set routes require a device-audience JWT and a fresh App Attest
assertion. Ownership is bound to the immutable server `devices.Id` resolved from the token's
`keyId`; clients cannot select or impersonate an owner id.

- `publish` creates or replaces the caller's submission by `client_set_id`. Every new or replaced
  submission returns to `pending`, including content that was previously approved. The ASP.NET
  request-body limit is exactly 900,000 bytes, while the serialized card snapshot itself may be at
  most 800,000 UTF-8 bytes.
- `mine` returns both pending and approved submissions owned by the calling device.
- `unpublish` removes the caller's matching `client_set_id` idempotently and returns
  `{"unpublished":true}` even when no row existed.
- `catalog` returns only approved submissions. Its `limit` defaults to 50 and is clamped to the
  inclusive range 1–100; an optional `query` searches normalized title, description, words, and
  translations. Pending submissions are never visible here.

A publication must have 1–500 cards. Limits are: `client_set_id` 100 characters, title 120,
description 1,000, card id 100, word 500, 1–20 translations of 500 characters each,
pronunciation 500, part of speech 100, 0–20 examples and aligned example translations of 2,000
characters each, notes 4,000, and raw language values 64. Language values must resolve to a
supported language. JSON uses `snake_case` throughout.

There is intentionally no public or device approval endpoint. Approval is an administrative
moderation operation outside this API; clients can only publish, inspect their submissions,
unpublish, and browse approved catalog content.

### AI

These are protected when `AiProtection:Enabled=true` and also use the `ai` rate limiter.

```http
POST /owlai/ai/analyze-word
POST /owlai/ai/word-detail
POST /owlai/ai/extract-words
```

`extract-words` accepts multipart form data:

- `image`: file, required, up to 20 MB at ASP.NET level
- `targetLanguage`: optional
- `nativeLanguage`: optional

Production nginx should allow at least 25 MB with `client_max_body_size 25m`.

### Legacy Account Endpoints

These use default user JWT and are not part of no-login v1.

```http
POST   /owlai/auth/register
POST   /owlai/auth/login
POST   /owlai/auth/google
POST   /owlai/auth/apple
GET    /owlai/auth/me
GET    /owlai/words
POST   /owlai/words
PATCH  /owlai/words/{id}
DELETE /owlai/words/{id}
GET    /owlai/categories
POST   /owlai/categories
PATCH  /owlai/categories/{id}
DELETE /owlai/categories/{id}
GET    /owlai/sync/changes
POST   /owlai/sync/push
GET    /owlai/user-settings
POST   /owlai/user-settings
PATCH  /owlai/user-settings/{id}
```

## AI Providers

`FallbackAiJsonService` chains the registered providers.

Current behavior:

1. Read `AI:Provider`.
2. If missing, use `OpenAI`.
3. Try the configured primary provider first.
4. If the primary fails, returns null, or is not registered, try the other configured provider.
5. If all providers fail, return `null` and trigger a throttled developer alert.

Default template:

```json
"AI": {
  "Provider": "OpenAI"
}
```

Examples:

| Config | Actual order |
| --- | --- |
| missing or empty | OpenAI, then Gemini |
| `OpenAI` | OpenAI, then Gemini |
| `Gemini` | Gemini, then OpenAI |
| unknown value | warning, then OpenAI, then Gemini |

AI response caching:

- `analyze-word` cache kind: `analyze_word`
- `word-detail` cache kind: `word_detail`
- cache key includes normalized word, native language, learning language, cache kind, and prompt
  version
- current prompt version: `2026-05-v5`

## Rate Limits And Quotas

The named rate limiter `ai` is a fixed-window limiter:

- 60 requests per minute
- partitioned by `keyId` when authenticated
- otherwise partitioned by remote IP
- no queue

Free-tier business limits:

- `DeviceWordService.FreeLimit = 10`
- `AiProtection:FreeDailyQuota` defaults to `40`
- daily quota counts successful free AI calls
- the word limit is enforced through `device_words`

## Data Model

| Entity | Table | Purpose |
| --- | --- | --- |
| `DeviceEntity` | `devices` | App Attest key id, public key, sign counter, device UUID |
| `SubscriptionEntity` | `subscriptions` | canonical Apple subscription state |
| `DeviceWordEntity` | `device_words` | device-scoped word inventory and free limit enforcement |
| `ChallengeEntity` | `challenges` | App Attest bootstrap/assertion challenges |
| `AiUsageEntity` | `ai_usage` | per-device daily AI quota counter |
| `TranslationCacheEntity` | `translation_cache` | persisted AI response cache |
| `AppUser` | `Users` | legacy account user |
| `WordEntity` | `Words` | legacy user word |
| `CategoryEntity` | `Categories` | legacy user category |
| `UserSettingsEntity` | `UserSettings` | legacy user settings |

Important indexes:

- `devices.KeyId` is unique
- `subscriptions.OriginalTransactionId` is the primary key
- `ai_usage` is unique by `KeyId` and UTC date
- `device_words` is unique by device UUID, normalized word, native language, and learning language
- `translation_cache` is unique by normalized lookup fields and prompt version

## Deployment

Current CI/CD is Docker-first:

1. GitHub Actions builds and tests the project.
2. It builds a Docker image.
3. It pushes the image to GHCR.
4. Manual deploy SSHes to the server and runs:

```bash
docker compose pull api
docker compose up -d --force-recreate
```

Recommended production shape:

```text
Internet
  -> HTTPS 443
  -> nginx on host
  -> http://127.0.0.1:5289
  -> Docker container port 8080
  -> PostgreSQL on host, reachable from container through docker0
```

Important production facts:

- bind the published container port to `127.0.0.1`, not `0.0.0.0`
- only nginx should be public
- keep `/etc/mavrylo/api.env` and `/etc/mavrylo/secrets/SubscriptionKey.p8` on the server only
- do not put Apple `.p8` content into the repo or iOS app
- for Docker, the app should listen on `http://0.0.0.0:8080`
- if PostgreSQL runs on the host and the API runs in Docker, the container usually reaches the host
  through `172.17.0.1` when using bridge networking

The detailed Linux runbook is in `ops/linux/README.md`.

## CI/CD

Workflow: `.github/workflows/api-ci-cd.yml`

On push to `main`:

1. restore test project
2. build API in Release
3. run tests
4. build Docker image
5. push image to GHCR with `latest` and `sha-<commit>` tags

On manual `workflow_dispatch`:

1. do all build/test/image steps
2. SSH to the server
3. run Docker Compose pull/up
4. call local `/health`

Required GitHub Actions secrets:

| Secret | Example | Purpose |
| --- | --- | --- |
| `DEPLOY_SSH_HOST` | `api.mavrylo.com` or server IP | SSH host for deploy. |
| `DEPLOY_SSH_PORT` | `22` | SSH port. |
| `DEPLOY_SSH_USER` | `deploy` | Linux user that can run Docker Compose in `DEPLOY_PATH`. |
| `DEPLOY_SSH_PRIVATE_KEY` | full private key text | Private key for the deploy SSH user. |
| `DEPLOY_SSH_KNOWN_HOSTS` | output of `ssh-keyscan -H api.mavrylo.com` | Trusted host key for non-interactive SSH. |
| `DEPLOY_PATH` | `/opt/apps/mavrylo-api` | Directory on the server that contains `docker-compose.yml`. |
| `GHCR_USERNAME` | `DostonElmurodov` | GitHub username used for `docker login ghcr.io` on the server. |
| `GHCR_TOKEN` | GitHub PAT with `read:packages` | Lets the server pull the private GHCR image. Add `repo` scope too if the package/repo requires it. |

The workflow deploy job uses the `production` environment.

## Tests

Current test project:

```text
tests/Mavrylo.Tests.csproj
```

The suite mixes fast in-memory tests (SQLite, `ManualTimeProvider`) with integration and
concurrency tests that spin up a real PostgreSQL container through Testcontainers. **Docker must be
running** for the Postgres-backed tests; without it those tests fail to start.

Current coverage:

- AI provider fallback order, all-provider failure alerting, and alert cooldown.
- `EntitlementService` state transitions and `DeviceWordService` free-limit behavior.
- App Attest challenge single-use, key/path binding, and expiry; daily AI usage counting/quota.
- AI protection filter 401/402/429 paths, including DB entitlement overriding a stale premium JWT.
- AI input hardening: word endpoints reject blank/over-long/unsupported-language input, and
  `extract-words` rejects non-image payloads and unsupported languages, all before any provider call.
- App Attest bootstrap registration: `challenge_id` required, unknown/mismatched/reused challenges
  rejected, happy-path device JWT issuance.
- Legacy endpoint closure: `LegacyAuthGuardFilter` returns 404 when `LegacyAuth:Enabled=false`.
- StoreKit/IAP: ES256 enforcement, disallowed `productId`, and `bundleId` mismatch rejection.
- Real-Postgres concurrency: bootstrap challenge consume yields exactly one winner, and the free
  AI-quota and free-word reservations never exceed their limits under concurrent load.
- App startup migrates and serves `/health` against a Postgres container.

Run:

```bash
dotnet test tests/Mavrylo.Tests.csproj
```

Both build configurations are green (Debug 68, Release 65; some StoreKit local-verify tests are
`#if DEBUG`-only, which accounts for the difference).

Recommended future tests:

- IAP `environment` mismatch and `appAccountToken`-required checks (need a signed-JWS fixture; they
  are gated to non-local mode and cannot be reached with an unsigned local JWS).
- Apple App Store Server Notification v2 semantics (renewal/refund/revoke/grace transitions).
- Oversized-upload rejection through the `[RequestSizeLimit]` pipeline (integration-level).
- AI rate-limiter partition-by-`keyId` behavior.

## Known Gaps

- OpenAPI contract is not fully current for the no-login routes.
- Full device sync is not implemented yet; current device-word API is count/upsert/delete.
- Legacy account endpoints still exist.
- Security-heavy flows now have automated coverage (App Attest bootstrap, AI input hardening, IAP
  product/bundle validation, legacy closure, and Postgres concurrency); remaining gaps are the
  signed-JWS IAP cases and Apple notification semantics listed under Tests.
- Legal pages are implemented as HTML in `LegalController`, but should be reviewed before App
  Store submission.
- Language-name mapping in `AiController` supports common codes but not every language the iOS app
  may expose.

## Security Rules

Never commit or log:

- `appsettings.json`
- `appsettings.Development.json`
- real `.env` files
- JWT signing keys
- OpenAI or Gemini API keys
- Apple App Store Server API `.p8` private key
- Apple issuer/key secrets
- database dumps

Safe to commit:

- `appsettings.example.json`
- `ops/linux/owl-api.env.example`
- public Apple root CA certificates in `Resources/Certificates`
- code, migrations, tests, templates, and docs

Apple App Store Server API credentials are server-only. The iOS app should never contain the `.p8`
key, issuer id, key id, or AI provider keys.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Startup fails with `Jwt:Key is missing` | set `Jwt__Key` or local `Jwt:Key` |
| Startup fails with `Jwt:Key is too short` | use at least 32 UTF-8 bytes |
| Startup fails outside Development on `AiProtection` | set `AiProtection__Enabled=true` |
| Startup fails outside Development on Apple signature validation | set `Apple__SkipSignatureValidation=false` |
| PostgreSQL `permission denied for schema public` | grant schema ownership to `owl_ai` |
| AI returns 503 | all configured providers failed or keys are missing |
| AI returns 401 | missing/invalid device JWT |
| AI returns 403 | App Attest assertion missing, invalid, expired, or replayed |
| AI returns 402 | entitlement is expired/revoked or free word limit reached |
| AI returns 429 | fixed rate limit or free daily quota reached |
| `/iap/verify` fails in production | Apple JWS did not verify or App Store Server API is not configured |
| App Store notifications do nothing | Apple webhook URL not configured or JWS decode failed |
| Docker health check fails | container port/env mismatch or database connection failure |

### Xcode Debug AI word access

Real devices registered with Apple's verified development App Attest AAGUID can use AI and sync more than ten words without an active subscription. The exemption is scoped to that device key and does not change subscription status. Device tokens, request assertions, rate limits, and the daily AI cost quota remain enforced. Production App Attest keys (including TestFlight/App Store builds) retain the normal entitlement and word limits.

Deploy this backend change before testing the iOS Debug build against the hosted API. Existing keys previously stored as production need fresh attestation registration; the iOS Debug key namespace creates a fresh key for that purpose. Simulator registration remains available only with a Debug backend running in Development.
