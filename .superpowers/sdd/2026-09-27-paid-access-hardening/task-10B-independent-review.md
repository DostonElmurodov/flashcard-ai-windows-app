# Task 10B independent scoped review

Date: 2026-09-29. Read-only review of the actual backend checkout at `D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\backend`, HEAD `3957f3e08c6317ff1495939e5c0cfbe7fea4f110` plus the final 49-path dirty source freeze. Review baseline is the accepted Task6B fix1 45-path freeze, not HEAD alone. Reviewed the eight-path incremental diff, relevant surrounding services/controllers/tests, client recovery source, and archived raw verification outputs. No product edits, builds, tests, remote calls, or agents were run by this reviewer. Root's reported 49 source/17 log hash verification is provenance supplied by root, not independently repeated here.

## Verdict

No actionable product-code defect found in the scoped implementation. The local runtime evidence is credible for the cases exercised, but Task10B verification is incomplete against its explicit inapplicable-proof requirement. One P2 coverage finding below should be closed before unconditional scoped acceptance. This is not a claim of a demonstrated authorization bypass.

## Finding

**[P2] Add the required marked-device inapplicable-proof HTTP control.**

Location: `tests/LegacyMarkerRecoveryTests.cs:188` (migrated HTTP matrix), with the new branch at `src/Mavrylo.Services/Services/DeviceContextService.cs:55`.

The matrix tests an absent candidate and later an applicable restored candidate which expires. It never puts an inapplicable purchase behind an owner binding or exact-device mobile grant on the marked installation. The existing `SubscriptionLifecycleTests.OwnerBindingValidatesClaimedAndTombstonedPurchaseBeforeProjectingHistory` tests invalid Sandbox history on an explicitly unmarked device; it neither exercises the new reconciliation flag nor the HTTP 503/provider/operation boundary. Existing restoration tests with marked devices concern token conflicts or applicable purchases, not this branch.

Consequently, replacing the new `IsApplicable` check with a mere non-null purchase/authority check could leave the existing marker tests green while changing required 503 recovery to ordinary invalid-subscription behavior. Add a migrated marked-device control with an inapplicable owner/mobile candidate, asserting token and entitlement 503 with the stable code, AI 503 after authentic fixture proof, no operation reservation and no provider call. Include an applicable inactive control in the same otherwise-unmasked setup; expired mobile is already covered, while a marked revoked/owner case would strengthen the intended distinction. Preserve the positive free/provider control and accounting readiness isolation. The dispatch explicitly requests applicable-inactive versus inapplicable proof controls.

## Source assessment

- Exact-key marker is a restriction only. Context adds a boolean without fabricating ownership, entitlement or WasEverPaid, and without clearing the marker. Authority comes from server-token bindings/exact-device grants or an independently authenticated account.
- AI resolves context, verifies JWT/request assertion and validates input, then applies the new 503 before saved-word reservation, operation quota or provider authorization. Server TestMode bypass remains conditional on Development plus trusted configuration; caller headers and device environment cannot enable it.
- Token/entitlement refresh then re-resolve before the new denial. Active independent account fallback can resolve the restriction; unrelated/unverified account and logout cannot.
- Applicable expired or revoked owner/mobile candidates pass the marker predicate into existing inactive policy. Inapplicable candidates fail it even when the resolver returns an authority label. Quarantined applicable purchases retain the existing invalid/quarantine policy.
- Candidate ranking is unchanged in this eight-path increment. Current rank orders premium/grace/trial above expired-paid/revoked/expired-trial above invalid. Existing mixed owner/grant selection tests cover active-versus-expired selection; invalid rows cannot outrank ordinary applicable inactive rows.
- DeviceWords upsert and public publication deny unresolved markers. Count/order, exact deletion, retained reads, public mine/catalog/unpublish remain usable. No global context exception blocks proof acquisition.
- Owner verification and paired mobile Restore can acquire proof before their response resolves context. Restore uses the exact device row, full paired evidence, canonical applicability and a finite device grant; it does not relink DeviceUuid or grant account/private-library authority. Registration/resume/challenge are outside the new commercial gate.
- iOS source still runs StoreKit discovery before token/optional account refresh, ordinary restore submits paired evidence, and APIError treats this 503 as paid denial rather than key-reset recovery. This is source evidence only, not executed Mac/device validation.
- Accepted usage readiness and materialization/order implementation are untouched. New migration fixture retains Ready=false for ambiguous history; accepted UsageMigrationPostgresTests retain the later-proof readiness coverage.

## Migration and runtime evidence

The preservation fixture migrates a fresh database only to `20260928020241_AddAiSpendReservations`, seeds real old-schema tables, and starts/disposes two actual ApiFactory hosts. Program startup calls Database.MigrateAsync. The fixture compares complete selected historical column payloads across devices, subscriptions, ai_usage, device_words, Users, account_sync_records and Words after each host. It separately checks old counters/key bytes/environments, row counts, claim tombstone, trial/paid/revoked fields, sync cursor/version/data/tombstone, word/review content, separate installation owners, four legacy-unproven purchase bindings, and no invented token/grant. The old shared library UUID is preserved, not used to create purchase ownership. It includes an unknown-environment historical row without deleting it.

The separate HTTP matrix has nonzero configured quota headroom and a free same-UUID control that reaches the counting provider and reserves one operation. Marked denial asserts provider zero and no operation bucket; 403 missing proof and 400 malformed input precedence are explicit. Restore without account header, expiry, restart and fake Apple outage are exercised.

Raw logs inspected:
- `task10b-red-3.log`: behavioral failure, expected 503 but actual 200.
- Earlier red logs: setup/proof failures, not credited as behavioral RED.
- `task10b-green-1.log`: counter expectation failure retained.
- `task10b-full-release.log`: 808 pass, one old 409 expectation now correctly receiving 503; final expectation change keeps denial and adds no-usage assertion.
- `task10b-snapshots-focused.log`: 2 passed.
- `task10b-full-release-snapshot-final.log`: 809 passed, zero failed/skipped.
- `task10b-release-build.log`: build succeeded, zero errors, one NU1510 warning.
- `task10b-ef-model.log`: no model changes since last migration.

## Limits and external gates

No Mac execution, physical iOS17/Apple restore, production inventory/backup/cutover, deployed configuration validation, signing or release is established. Empty Apple discovery has no server marker-clear writer, but this server matrix is not a client empty-discovery lifecycle run. Historical false-positive marker reconciliation remains operational work. Commercial daily/minute AI quotas and monetary budgets remain unapproved; the documentation correctly distinguishes these from the approved ten-active-card policy. Malformed historical uniqueness/inventory remains a rollout gate. Existing package advisories are not resolved by this scope.

