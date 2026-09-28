# Account enrollment and sync API

## Single-account repair (2026-09-18)

`RestoreRequestedGoogleEnrollment` performs an explicitly requested administrative repair for one existing Google account whose owner confirmed iPhone sign-in. A fixed SHA-256 digest of the normalized address selects that account; it is an exact target selector, not authentication proof. The migration also requires an existing Google subject and server session, refuses ambiguous Google identities, preserves existing enrollment timestamps, and neither creates nor merges accounts. Other legacy accounts and email-only identities remain unchanged. This is not a general legacy-account enrollment exception.

Startup applies the repair transactionally. An empty database or an absent target is a no-op. Rollback does not clear the admission marker because subsequent verified iOS enrollment may exist. Deploy through the existing API CI/CD workflow after migration tests pass; the affected user can then retry Windows Google sign-in without reinstalling Windows. All ordinary iPhone-first enrollment and sync checks remain in force.

Registration is available through an attested iOS installation. The account `register` route and legacy auth creation routes require `X-Device-Authorization: Bearer <device JWT>` plus the existing `X-App-Attest-Key-Id`, `X-App-Attest-Challenge-Id`, and `X-App-Attest-Assertion` headers. Issue the existing assertion challenge for the exact endpoint path; hash the exact serialized request body bytes. Simulator bypass remains development-only under the existing verifier configuration.

`POST /owlai/account/google/session` may sign in an existing legacy account without a device proof. A new account requires verified proof; supplied device proof must verify even for an existing account. Verified registration or Google proof sets `Users.IosEnrolledAt`. Existing email accounts enroll with `POST /owlai/account/ios/enroll`, using account bearer plus the device proof described above. Windows uses `/owlai/account/desktop/email/session` or `/owlai/account/desktop/google/session`. Unknown or unenrolled accounts receive HTTP 403 with `code: ios_account_required`; these routes never create or enroll users. Existing generic email login remains compatible with legacy iOS accounts.

Sync uses an active account bearer; the server obtains owner and session IDs from validated JWT claims. Neither request body ownership fields nor query parameters grant ownership. Every database lookup is owner-scoped; the composite primary key permits the same client entity IDs in different accounts. Sync transactions lock the account and session rows, so revocation and writes serialize across API instances.

- `GET /owlai/account/sync`: `{owner_id,records,conflicts:[]}` (full snapshot).
- `POST /owlai/account/sync`: `{changes:[{kind,id,base_version,deleted,data}]}`; response `{owner_id,records,conflicts}` (full snapshot).
- Kinds: `deck`, `word`. New IDs require `base_version:0`. Existing records require their exact server version. A successful change increments that record's version. Deletions preserve tombstones with null data.
- **Any version conflict applies zero changes in the entire batch.** The response contains the unchanged full snapshot and all conflicting records. Clients keep their local edits and resolve explicitly; do not silently retry stale content against a newer version.
- A word requires a live deck in the same resulting account snapshot. Deleting a deck requires deleting or moving its words in the same batch. Input ordering is irrelevant.
- IDs: nonempty strings up to 128 characters, no control characters. Maximum 500 changes/request; 10,000 total records including tombstones; 64 KiB UTF-8 per data object; 8 MiB request and conservative account budget (`data UTF8 + id UTF8 + 256 bytes` per record). Capacity errors are HTTP 413 `sync_capacity_exceeded`, never truncated snapshots.
- Core deck fields: name, description, active, native_language, learning_language, created_at. Word fields: deck_id, word, translation, optional pronunciation/examples/notes, created_at, card, reverse. UTC date strings use `Z` or `+00:00`. Review card fields follow the shared plan and support optional last_review and learning_steps. State is 0–3, all numeric counters nonnegative, difficulty at most 10.
- Bounded extra metadata is preserved (including `metadata.ios_set_ids` and `metadata.ios_reverse_direction_enabled`). Owner/user/account ID fields are rejected recursively, including camelCase variants. Top-level request/change unknown fields are rejected. Clients must never send credentials, subscriptions, account settings, or private preferences in data.

Deployment requires applying migration `AddIosAccountSync` after the existing shared-subscription migrations. This implementation has not been deployed. Real App Attest hardware and a two-device iPhone/Windows lifecycle test remain release requirements. The automated suite uses real PostgreSQL for account ownership, concurrent updates, rollback, orphan prevention, and session revocation. App Attest filter tests separately verify real ECDSA assertion binding and replay rejection; API registration fixtures use the explicit development simulator path.

## Rollout configuration and proof details

Use the existing production environment values: `ASPNETCORE_ENVIRONMENT=Production`, `LegacyAuth__Enabled=false`, `Apple__ClientId=com.mavrylo.owlai`, the actual `Apple__TeamId`, `Apple__SkipSignatureValidation=false`, existing `Jwt__Key`/`Jwt__Issuer`, `Account__Audience=owl-ai-account` (distinct from device and legacy audiences), and `Account__GoogleClientIds` containing the intended Google client audiences. Keep `ConnectionStrings__Default` pointed to the intended PostgreSQL database. There are no new sync secrets or sync feature flags. Do not infer environment or enrollment from client-provided platform headers.

For register, Google creation, and enrollment, request `/owlai/app-attest/assertion-challenge` with header `X-App-Attest-Key-Id` and JSON `{request_path:"/owlai/account/register"}` (substitute `/owlai/account/google/session` or `/owlai/account/ios/enroll` exactly). Compute:

`SHA256(challengeBytes || 0x1F || SHA256(rawBody) || 0x1F || UTF8(requestPath) || 0x1F || UTF8(challengeId))`

Generate the Apple assertion over that hash; send its base64 in `X-App-Attest-Assertion`, challenge ID in `X-App-Attest-Challenge-Id`, key ID in `X-App-Attest-Key-Id`, and the device JWT in `X-Device-Authorization`. Enrollment additionally sends the account JWT in the standard `Authorization` header. Preserve raw body bytes exactly from hash through transmission. Registration and Google session creation need no account bearer.

Roll out the backend and verify migrations and hardware enrollment before distributing clients that depend on these routes. The old production API does not provide these routes; client integration alone does not enable synchronization. No production credentials or deployment were available in this task.

Wire compatibility details: optional notes/pronunciation and `last_review` accept JSON null as well as omission; untranslated imported words accept an empty required translation string (max 8192). `elapsed_days`/`scheduled_days` may be fractional finite nonnegative numbers, preserving native iOS FSRS progress. Optional word `native_language`/`learning_language` are nonempty strings up to 64 characters; clients fall back to the deck languages only when these fields are absent.

Sync GET and POST also require the verified iOS enrollment marker. An old saved Windows session or a generic legacy email/Google session receives HTTP 403 `ios_account_required` until an authenticated iOS device completes enrollment; generic login cannot bypass the iPhone-first sync gate.


## Bundled word translation and private notes (2026-09-19)

Both authenticated `POST /owlai/ai/word-detail` (device) and `POST /owlai/account/ai/word-detail` (account) accept optional `secondary_language`. Omitting it preserves the original response contract. When present it must be supported and differ from `native_language` (English aliases compare equal).

The response retains primary word-detail fields and adds `secondary_translation: {language_code, translation, explanation}`. The service checks the database cache for each language first. Both hits need no AI call; one or two missing parts are requested in one AI service invocation. The prompt requests only missing parts, and all generated parts are validated before caching. Primary and secondary content stay in independent existing cache rows so subsequent words, language combinations, and Review can reuse them. Existing word-detail and review-translation caches are reused across language roles; English source aliases are recognized. When only a concise cached translation exists, optional rich word-detail fields may be absent.

Authorization, App Attest, entitlement and per-request quotas remain unchanged; a cached response does not bypass endpoint access checks. Old clients continue to work but must be updated to send the secondary language in the first request.

Personal notes are account data (`notes` in account sync), never shared translation data. Public publication discards notes, catalog reads redact legacy notes, and the forward privacy migration removes them from old public snapshots while retaining private word/account records. iPhone public exports omit notes and public imports ignore notes from old servers. Cross-account sync tests use identical record IDs to verify ownership isolation.

## Incremental sync (2026-09-26)

Apply `20260926142908_AddIncrementalAccountSync` before deploying this API. It adds
an account `SyncRevision`, a record `ChangeRevision`, and the composite index
`(UserId, ChangeRevision)`. Existing records, including tombstones, are backfilled
to revision 1; empty accounts remain at 0. Record versions and content are preserved.

- `GET /owlai/account/sync?since=N` returns only records changed after the account
  cursor N. The response adds `cursor` and `is_snapshot` to the existing fields.
- `POST /owlai/account/sync` accepts optional `since` alongside `changes` and
  returns the same delta, plus every accepted submitted record as acknowledgement.
  These acknowledged payloads let the saving device mark its exact submitted
  version synchronized immediately; it does not upload it again on the next check.
- Omitting `since` preserves the old full-snapshot behavior. A cursor greater than
  the account's current revision returns a full snapshot with `is_snapshot:true`
  and applies no submitted changes.
  Clients detect an unexpected snapshot for a cursor-bearing request and require explicit cloud recovery with a
  local backup rather than automatically deleting local cards missing from it.
  A persisted recovery marker blocks subsequent automatic/manual sync until the
  explicit cloud recovery succeeds, including after a client restart.
- A valid cursor yields `is_snapshot:false`; an idle response has `records:[]`,
  `conflicts:[]`, and the unchanged cursor. No record payload query or account row
  write lock occurs on this path. The read transaction provides a consistent
  revision/record snapshot. All reads still validate active session and enrollment.
- A nonempty accepted write increments the account revision once per atomic batch.
  Legacy clients also advance the revision so new clients see their changes.
  Writes retain existing capacity and full relationship validation; payload-heavy
  work is limited to actual uploads and downloads with changes, not idle checks.
- Incremental retry of an already accepted change at exactly `base_version+1`
  with identical JSON content (or the same tombstone) is acknowledged without
  incrementing versions again. Different content or a later version conflicts.
  Atomic conflicts still apply **zero changes** to the batch. Legacy no-cursor
  requests retain their previous strict version-conflict behavior.
- Clients first negotiate via GET, persist cursor+baseline together, and opt in
  to `since` only when both new response fields are present. Old servers continue
  to work with full snapshots. Clients merge deltas by `(kind,id)`, never infer
  deletion from absence in a delta, and retain old versions/cursors for deferred
  or conflicted edits. An edit made during a request is never acknowledged as
  though its newer contents had been sent.

Recommended schedule: check on login/open, throttle repeated foreground events
for five minutes, and check hourly while active/visible. Debounce local mutations
for 15 seconds; keep manual sync and bounded retries with jitter. Do not run idle
polling for hidden Windows or inactive iOS sessions. iOS background execution is
OS-controlled and no exact hourly background execution is promised. Existing
feature-flag/entitlement polling is independent and unchanged.

An AI translation response alone is not an account-card persistence acknowledgement.
The user may cancel, choose a deck, edit notes, or change the translation. The client
first saves the actual card locally, uploads its pending account record, and marks
that exact payload synchronized only when the account sync response confirms it.
The shared translation cache and device allowance ledger are not account libraries.

Rollout order: backend migration/API first, then iOS and Windows. This source change
does not itself confirm a production deployment or on-device end-to-end validation.
