# Task 10 local closure — preserved legacy restrictions and foundation proofs

Execute after accepted Tasks 3B, 6A and 6B. Read `remaining-task-execution-constraints.md` first, then `task-10-local-closure-preparation.md`, `legacy-migration-preflight.md` and `final-review-carryover.md`. This is local implementation and verification, not production inventory or rollout. Record the current accepted source bases at dispatch. Use a fresh implementer and one product writer; root does not write product code.

## Recovery rule for a known legacy installation

The approved contract forbids resetting known historical restrictions into new free AI access and also requires ordinary Apple restore without an Owl login. Implement the narrow conservative rule below; it grants no ownership or paid entitlement.

- A server-persisted `Device.RequiresAccountSubscription` marker on the authenticated key's exact device row is a restriction signal. Before using it, audit all current writes and the old migration/claim paths, document provenance and any false-positive cases. Never derive a new marker, owner, paid history or account association from UUID equality, a caller-supplied token, copied purchase fields or an arbitrary old subscription row.
- If that marked installation has no applicable proven owner binding/mobile grant and no selected verified account entitlement, its commercial token/entitlement projection and AI admission must report a recoverable `503 subscription_reconciliation_required`, rather than silently returning a fresh free allowance. Apply the denial before quota/provider work; denied calls must have providerCalls=0 and no operation-usage reservation. Preserve existing authentication/assertion and malformed-request protections.
- Keep credential registration/resume, challenge acquisition and paired Apple restore available. A marker must never prevent the user from obtaining the credential needed to restore. The client retains already established read/review history, closes new paid mutation confirmation, and offers retry/Restore with existing support fallback. No automatic repurchase or mandatory login is introduced.
- A successfully applicable owner proof or finite mobile grant resolves the restriction through ordinary entitlement selection, including inactive/revoked outcomes. An active, independently authenticated account fallback can likewise provide its own permitted access. An unrelated free account or unverified account header does not clear the restriction. Do not erase the marker merely because an Apple check was empty, a network request failed, a key was regenerated, or the user logged out.
- Preserve the marker as historical evidence; prefer resolving the effective restriction from applicable current proof over a destructive clearing migration. Repeated migration/restart/Restore must be idempotent. No marker ever exposes a different installation's library or an account's private data.
- Unmarked genuinely free installations retain the free path. A wholly new anonymous identity with no surviving signal is the already documented residual identity case, bounded by aggregate spend. Do not claim this rule recognizes a person across arbitrary reinstalls.

Root ruling: a known server restriction is sufficient to deny fresh free AI while proof is unresolved, but is insufficient to grant paid authority or infer purchase ownership. The cost of a historical false positive is a recoverable temporary commercial denial; document such cases for migration reconciliation. Do not add a generic unauthenticated reset or invent a production support-clearing procedure.

## Required local evidence

Use the six sections in the preparation document as the exact coverage inventory:

1. Deterministic simultaneous registration after both absent-row reads: same-material winner/retry, conflicting material, exactly one total owner delta, no orphan and preserved key/library/counter.
2. Actual HTTP resume with hash-checking proof, JWT/key/owner/library persistence, zero-byte and empty-object controls, tamper/replay/unknown-key/invalid JSON/body limits/no-store. Fakes remain offline.
3. Challenge and assertion counter survive a later business transaction rollback and a restarted host; old/replayed evidence denies and a fresh higher counter succeeds.
4. Missing/null/malformed register/challenge request shapes produce stable code/error and zero persistent side effects. Restrict any framework model-validation repair to the affected contract unless wider behavior is proven necessary.
5. Populated old-schema migration through two separate host startups preserves exact owner/key/claim/tombstone/card/review/account-sync/usage data and the final 6A/6B schema. No invented owner credential, finite grant or UUID association; no deletion to make constraints pass.
6. Actual migrated HTTP token/entitlement/AI matrix for marked known, claimed, deleted-account, paid/trial, unmarked genuinely free and ambiguous-UUID fixtures. Establish behavioral RED for the known-marker free fallback, then GREEN with zero provider calls and no allowance reset; positive controls must reach the fake provider so unrelated quota/spend defaults cannot mask a defect. Successful paired Restore, later expiry/revocation, restart and empty/outage checks must preserve their appropriate restrictions/history. Cover applicable independent owner/grant/account paths without changing their accepted selection policy.

The marker itself must not fabricate `was_ever_paid`; prove only previously established client/server paid history remains retained. A client change, if required by an uncovered lifecycle scenario, needs actual Mac evidence coordinated by root. Do not rerun already covered passing cases without a new change or concrete doubt.

## Completion contract

Use TDD for product defects; mark fixture, environment and source-only findings honestly. Run simplify after every coding iteration. Record focused commands, actual RED/GREEN, full affected backend checks, populated migration/restart results and known inherited warnings in `task-10-local-closure-report.md`. Preserve all raw outputs. Do not commit or push backend/Windows/docs. Freeze uncommitted source for fresh clean-context Astra xhigh review. Physical Apple tests, production database reconciliation/backup/cutover and deployment remain explicitly unexecuted external gates.
