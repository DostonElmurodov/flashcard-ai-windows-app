# Incremental account sync implementation plan

**Goal:** Reduce idle server work and network traffic without losing offline or concurrent edits across iOS and Windows.

**Architecture:** Preserve owner-scoped record versions and client fingerprint baselines. Add an account cursor and indexed record change revisions; optional `since` selects a delta response. Confirm pending changes only after an exact server acknowledgement. Legacy requests keep full snapshots.

**Tech stack:** ASP.NET Core / EF Core / PostgreSQL; Swift / SQLite; Electron / TypeScript / SQLite.

## Protocol and constraints

- GET `/owlai/account/sync?since=N`; POST `{changes:[...],since:N}`.
- Response adds `cursor` and `is_snapshot`. Missing `since` or a cursor ahead of the account returns a full snapshot. Valid cursors return only later records including tombstones, plus accepted submitted records in POST acknowledgements.
- Cursor and data are owner scoped. Reads use a consistent transaction snapshot and active-session/enrollment checks. Idle reads do not lock account rows or load record payloads.
- Writes retain atomic optimistic concurrency, capacity checks and graph validation. Existing old-client writes also advance the account cursor. Identical incremental retries at exactly `base_version + 1` acknowledge the original write without another version increment.
- Clients merge deltas with persisted baseline; unrelated records remain. Cursor advances only with durable reconciliation. Unresolved conflicts and deferred edits must not disappear behind a cursor.
- AI translation is staged data, not a saved account card. Mark saved cards synchronized only after the account-sync acknowledgement.
- On open/login, check; throttle repeated foreground events for 5 minutes. While active, check hourly; local edits debounce 15 seconds. No idle background polling. Keep manual sync and bounded error backoff.
- No Windows build or app launch. Do not change independent entitlement/feature-flag polling in this change.

## Tasks

- [x] Backend: integration tests for empty reads, deltas, deletions, owner isolation, cursor reset, lost acknowledgements and concurrent writes; add migration with backfill/index; implement optional cursor reads/writes and update API docs.
- [x] iOS: persisted cursor and transport compatibility; transactional delta reconciliation and exact acknowledgements; foreground/dirty scheduling; regression tests and integration documentation.
- [x] Windows: persisted cursor, delta merge and unsafe in-flight rebase fix; foreground/dirty scheduling; portable tests and documentation.
- [x] Verify backend with disposable local PostgreSQL, run iOS targeted tests/build, portable Windows tests only; inspect cross-client contracts. See `docs/account-sync-verification-2026-09-26.md`.
- Delivery: commit and push each repository separately after verification.

## Review focus

Lost response must not duplicate versions; edits made during await must remain pending; multi-batch dependency graphs must remain valid; future cursors must reset safely; stale/foreign/revoked sessions must never expose another account's records.
