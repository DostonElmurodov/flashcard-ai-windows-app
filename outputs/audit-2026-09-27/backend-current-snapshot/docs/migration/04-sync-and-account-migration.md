# Sync and account migration plan

See [Migration overview](./00-overview.md) for how this document fits the full roadmap.

Part of the native iOS migration split. Related docs: [01-backend-cache-plan](./01-backend-cache-plan.md), [02-ios-mvp-local-first](./02-ios-mvp-local-first.md), [03-auth-and-guest-mode](./03-auth-and-guest-mode.md).

## Goal

After the local-first MVP and auth/guest work, implement **bidirectional synchronization** for **registered** users: push local changes, pull server changes, merge by agreed rules. For **guest**, keep a **local-only** queue that **does not flush** user vocabulary to the server. After **registration/login**, perform a **controlled migration**: batch push local entities, then pull and merge. Align behavior with the existing web client delta sync where required (`apps/web-react/src/lib/syncService.js`, `SyncController` in `apps/api-dotnet`).

## Scope

- **`SyncCoordinator`:** registered—flush outbound queue, pull, merge; guest—local queue **without** server flush for user dictionary.
- **`sync_queue`** (and related persistence): outbound operations, retry state, ordering as designed.
- **Local session / sync metadata** in SQLite (e.g. `app_session`): `is_guest`, `user_id`, `last_sync_utc`, optional `guest_issued_at`—aligned with schema migration from MVP when this plan lands.
- **Conflict resolution:** default **last-write-wins (LWW)** per entity using `updated_at` (UTC).
- **Deletes:** soft-delete with `deleted_at` and/or tombstone flags; propagation rules consistent with web client.
- **Guest → account migration:** after signup/login, **batch push** local entities, then **GET** server changes and **merge**.
- **Identity rule:** guest `sub` does **not** become user-owned data without **explicit** account binding.
- **Batch** push/pull semantics and **retries** (policy to be finalized under Missing decisions).
- **UI:** sync status; diagnostic logging for conflicts (per product).

## Default conflict rule

- **LWW** by entity on **`updated_at` (UTC)**.
- **Tie when timestamps are equal:** choose **server wins** or **client wins** once, document in code and this doc, and stay compatible with web delta sync.
- **Deletes:** soft-delete + tombstone; merge must not resurrect deleted rows incorrectly.

## Guest vs registered (`SyncCoordinator`)

- **Guest:** maintain local `sync_queue` (or equivalent) **without** flushing user dictionary / user-owned entities to the server.
- **Registered:** flush queue, pull remote changes, merge per rules above.

## Account transition

- After registration or login: **controlled** local→server migration—**batch push** of local entities, then pull and merge.
- Reconcile with server state before assuming multi-device consistency.

## SQLite (sync-related)

When this plan is implemented, extend schema to include:

- **`sync_queue`** and fields needed for cursors / last sync.
- **`app_session`** (or equivalent) for `is_guest`, `user_id`, `last_sync_utc`, optional `guest_issued_at`.

Coordinate additive migrations with [02-ios-mvp-local-first](./02-ios-mvp-local-first.md) so existing installs upgrade cleanly.

## Integration references

- Web: `apps/web-react/src/lib/syncService.js`
- API: `apps/api-dotnet/Controllers/SyncController.cs`
- Contract: `contracts/openapi/openapi.yaml`

## Testing

- Unit: merge logic, tombstones, LWW edge cases.
- Integration: registered user push + pull; multi-device scenario per test matrix; guest cannot sync user dictionary; after registration, local data appears on server and can be pulled on another device when sync succeeds.
- Failure cases: network loss mid-batch—document resume, idempotency, and user-visible behavior.

## Acceptance (from overall migration)

- After registration, local data **correctly** migrates to the server and **downloads** on a new device when normal sync runs.
- **Guest:** no sync of personal dictionary to the server before registration.
- **Known words / translation:** once a word+language pair is satisfied in **local SQLite**, **sync must not** cause a **redundant translation** request for that same normalized pair. The network may be used for **sync payloads** (and metadata), but **not** to re-fetch AI translation for content already present locally—consistent with [02-ios-mvp-local-first](./02-ios-mvp-local-first.md) and the end-to-end checklist in [00-overview](./00-overview.md).

## Missing decisions (must be closed before implementation)

- **Tie-break** when `updated_at` is identical (server vs client).
- **Per-entity-type** exceptions (words vs categories vs settings)—confirm all use the same LWW or document differences.
- **Clock skew:** whether ordering trusts server time, device time, or hybrid.
- **Idempotency keys** for batch operations and server deduplication.
- **Retry/backoff** for partial batch failure; strict ordering vs parallel pushes.
- **Conflict UX:** silent resolve vs user-facing prompt (default in original plan: log + silent resolve).
- **Formal mapping** document: current `SyncController` + web client message shapes vs iOS coordinator—complete before coding.

## Dependencies

**Depends on**

- [02-ios-mvp-local-first](./02-ios-mvp-local-first.md): stable local entities and repositories.
- [03-auth-and-guest-mode](./03-auth-and-guest-mode.md): reliable distinction between guest and registered; JWT for authenticated sync.
- Written resolution of **Missing decisions** above.

**Depended on by**

- Production multi-device experience; post-guest account continuity.

## Done criteria (strict)

- **Registered:** outbound changes reach server; inbound changes apply locally; merges match **documented** LWW + tombstone rules; automated tests cover published conflict matrix.
- **Guest:** no server persistence of user dictionary via sync; local queue does not flush user-owned data until registered.
- **Migration:** guest-created local data **batch push** after signup/login completes without incorrect duplication given **idempotency** rules; subsequent pull/merge succeeds.
- **Web + iOS:** equivalent operations yield consistent server state, or deltas are explicitly documented and tested.
- **Failures:** documented behavior for interrupted batches (resume, no silent data loss, no unbounded duplicate creates).
