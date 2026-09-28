# Native iOS migration — overview

This document is the **index** for the split migration plans. Implementation details live in the linked files below.

## Purpose of the migration

Move from the current **React** client (`apps/web-react`) to a **native SwiftUI** app under `apps/ios-native`, while keeping the same **.NET API** (`apps/api-dotnet`) and **OpenAPI** contract (`contracts/openapi/openapi.yaml`).

Target properties:

- **Offline-first** vocabulary and metadata in **SQLite** on iOS.
- **Local-first reads**; network only when the app must fetch something not yet stored locally (e.g. a new word + language pair).
- **Server-side shared translation + TTS cache** to reduce AI cost for all clients.
- **Optional guest mode** (no cloud vocabulary sync for the personal dictionary until registration).
- **Sync and guest→account migration** after the local model and auth are stable.

## Why the plan was split into four documents

The original roadmap combined **backend caching**, **iOS SQLite/UI**, **guest JWT and policy**, and **sync/conflict resolution** in one place. Those areas have different:

- **Risk profiles** (cost/abuse, data loss, security).
- **Dependencies** (OpenAPI shape, schema migrations, auth claims).
- **Acceptance tests** (metrics vs UI vs multi-device).

Splitting keeps each plan **focused**, reviewable, and shippable on its own timeline without losing the overall architecture.

## Summary of each plan

### [01-backend-cache-plan.md](./01-backend-cache-plan.md) — Backend translation + audio cache

- **`translation_cache`** (or equivalent): lookup **before** OpenAI/TTS; persist on success.
- **Server audio** storage (disk / object storage) + metadata; stable or regenerable URLs in API responses.
- **Integration** in `apps/api-dotnet` (e.g. `AiController`, `OpenAiJsonService`) at `analyze-word`, `word-detail`, optionally `extract-words`.
- **Normalization rules** for cache keys (shared with all clients).
- **OpenAPI**: additive, backward-compatible responses.
- **Metrics**: `translation_cache_hit` / `translation_cache_miss` (optional TTS miss).
- **Guest + shared cache**: guest-originated translations may populate the **shared** cache (not the personal dictionary); optional product flag to disable persistence for guests (privacy).

### [02-ios-mvp-local-first.md](./02-ios-mvp-local-first.md) — iOS MVP (local-first, no sync)

- **SwiftUI** structure, **SQLite v1** for words/categories/settings/local audio metadata.
- **`WordRepository`**: same normalization as Plan 01; local hit → no translation request; miss → API → persist + **local** `AudioCacheManager` under `Application Support/AudioCache`.
- **Screens** for MVP: minimal registered login, Home, Review, History, Settings, CRUD, categories, onboarding (per checklist).
- **Review/SRS** parity with `apps/web-react/src/pages/Review.jsx` (with tests).
- **Explicitly out of scope**: `SyncCoordinator`, `sync_queue` processing, guest JWT, `GuestPolicyGate`, guest→account migration.

### [03-auth-and-guest-mode.md](./03-auth-and-guest-mode.md) — Auth and guest mode

- **Registered**: email/password + Apple/Google per backend; JWT in **Keychain**; **`SessionState`**.
- **Guest**: short-lived **guest JWT** (`guest` claim, anonymous `sub`); **quotas** (rate + daily) **server-enforced**.
- **`GuestPolicyGate`** on client; **server** denies writes/sync of **user-owned** dictionary data for guests.
- **`AiController` / `WordsController` / `SyncController`** remain `[Authorize]`; no `[AllowAnonymous]` on expensive AI routes.
- Alternative: **local-only TTS** for guest (no server AI) if product requires it.
- **Privacy / App Store**: Policy updates for guest JWT, phrase caching, and metrics if shipped.

### [04-sync-and-account-migration.md](./04-sync-and-account-migration.md) — Sync and account migration

- **`SyncCoordinator`**: registered → flush, pull, merge; guest → **local queue only**, no flush of personal dictionary to server.
- **`sync_queue`**, cursors, **`app_session`** fields (`is_guest`, `user_id`, `last_sync_utc`, optional `guest_issued_at`).
- **Conflicts**: default **LWW** on `updated_at` (UTC); **soft-delete / tombstones**; tie-break and web alignment documented before coding.
- **Guest → account**: controlled **batch push**, then pull/merge; guest **`sub`** must not become user data without explicit binding.
- **Web alignment**: `apps/web-react/src/lib/syncService.js`, `SyncController`, OpenAPI.

## Dependency order between the plans

```text
Normalization + OpenAPI MVP surface (agreed in writing)
        │
        ├──────────────────┬──────────────────┐
        ▼                  ▼                  │
   [01 Backend]      [02 iOS MVP]             │
   cache             local-first               │
        │                  │                    │
        └────────┬────────┘                    │
                 ▼                             │
            [03 Auth + Guest]                   │
                 │                             │
                 ▼                             │
            [04 Sync + Migration] ◄────────────┘
```

- **02** depends on agreed **normalization / language codes** (same as **01**).
- **03** should follow **02** for stable `WordRepository` and schema before full guest E2E (or use registered-only stubs until guest is ready).
- **04** depends on **02** (entities, migrations path) and **03** (guest vs registered, JWT for sync).

## What can be parallelized

- **01 (backend cache)** and **02 (iOS shell + SQLite + core flows)** can proceed **in parallel** once **cache key normalization** and **response shape** are agreed (small written spec; no code required in this doc).
- **Backend metrics / staging validation** for 01 can overlap **02** UI work.
- **03** backend work (guest token endpoint, quotas) can start in parallel with late **02** if contracts are frozen—**client** guest E2E should still wait until **02** word pipeline is stable.

## What must not start too early

- **04 (full sync + migration)** before **local schema and conflict rules** are documented and **03** can distinguish guest vs registered reliably.
- **Production guest JWT** without **server-side quotas** and **write denial** tests.
- **Treating normalization as “obvious”**—01 and 02 must share **one** documented rule before relying on cache hits across client and server.

## Recommended implementation order

1. Agree **normalization + OpenAPI** surface for MVP (short written spec).
2. **01** — server cache (additive, backward-compatible).
3. **02** — iOS MVP (registered-only, no sync, no guest).
4. **03** — auth + guest JWT + `GuestPolicyGate` + quotas.
5. **04** — sync, then guest→account migration.

**Practical note from the original roadmap:** **Guest JWT** should be ready **before** full integration testing of “new word as **guest**,” or the team must **stub registered-only** paths and label tasks accordingly. **Audio:** ship a **basic** `AudioCacheManager` and local audio metadata with **02**; **LRU / prefetch / aggressive cleanup** are later polish (still consistent with **02**’s “later polish” section).

## MVP vs later phases

| Horizon | Scope |
|--------|--------|
| **MVP** | **02** only: native SwiftUI + SQLite + registered user + local-first words + Review/SRS + local audio + minimal API for new words. **No** sync engine, **no** guest JWT complexity. **01** is strongly recommended before or during MVP so server costs stay predictable. |
| **Next** | **03**: guest mode, full provider auth, quotas, privacy updates. |
| **Then** | **04**: incremental sync, conflict handling, guest→account migration, multi-device. |
| **Ongoing / hardening** | **01** retention limits, cache TTL/version policy, operational runbooks; **02** audio LRU and performance polish. |

## End-to-end acceptance (full product, spans all plans)

When **01–04** are complete, the product should satisfy:

- **Xcode** project runs on simulator/device.
- **Known word** (saved locally): **no** translation API call for the same normalized word + language pair; **sync may use the network** but **must not** force a **re-translation** of that pair (aligns **02** + **04**).
- **New word**: one client API call per “get translation for this word+langs” action; later offline OK. **Camera / batch**: up to **N** calls for **N unique** new lemmas (not necessarily one HTTP for the whole flow).
- **Guest**: no **sync** of personal dictionary to server; AI only with **guest JWT** within **quotas** (**03**).
- **After registration**: local data **migrates** to server and **downloads** on another device when **sync** succeeds (**04**).
- **Review/SRS** matches `Review.jsx` on agreed examples (**02**).
- **Server**: repeat request with same normalized word + langs does **not** call OpenAI/TTS (**01**, verify via metrics/logs); **response shape** unchanged for existing clients (**01**).

## Source artifact references (repository)

| Role | Path |
|------|------|
| Current app logic, pages, flows | `apps/web-react/src/` |
| SRS / Review reference | `apps/web-react/src/pages/Review.jsx` |
| Web sync reference | `apps/web-react/src/lib/syncService.js` |
| API contract | `contracts/openapi/openapi.yaml` |
| Backend | `apps/api-dotnet/` |
| Native iOS app | `apps/ios-native/` |
| iOS setup notes | `docs/ios-native.md` |

**Client strategy:** **Web** and optional **Capacitor** shell remain **separate clients** of the same API; behavior is aligned through **OpenAPI** and **shared cache-key normalization** (not by duplicating unrelated logic).

## Plan index (links)

- [00-overview.md](./00-overview.md) — this file  
- [01-backend-cache-plan.md](./01-backend-cache-plan.md)  
- [02-ios-mvp-local-first.md](./02-ios-mvp-local-first.md)  
- [03-auth-and-guest-mode.md](./03-auth-and-guest-mode.md)  
- [04-sync-and-account-migration.md](./04-sync-and-account-migration.md)  

The historical single-file Cursor plan remains at `.cursor/plans/swift_ios_rewrite_plan_cdbdb6a6.plan.md` (if present on your machine); these five docs are the maintained split.
