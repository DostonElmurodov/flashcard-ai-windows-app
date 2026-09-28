# Task 4 independent review

## Spec compliance

**❌ Needs fixes.** Two concrete gaps remain in the mandatory cached-row validation contract: PostgreSQL infinite expiry is accepted as premium, and the claimed-device projection bypasses validation and exposes unvalidated paid history. Details and focused reproduction evidence follow.

**Code quality: Needs fixes.** The implementation otherwise has a coherent shared policy, bounded canonical identity checks, and focused lifecycle/cryptographic tests. No Critical finding.

Reviewed base `1c31750305e44914bc3c8c7fbdf453d1e8eec7b4` through head `4db300428adccf820584aeb0860afd73af3b673f`. Source references below are relative to `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`.

⚠️ Release dependencies remain unverified here: legacy-data reconciliation, client handling of `invalid_subscription` and `subscription_reconciliation_required`, isolated Sandbox/App Review data and quotas, Linux certificate behavior, and physical-device acceptance. These are explicitly recorded in `docs/apple-subscription-validation.md:5–15`; their deferral is not treated as Task 4 completion. Existing ownership failures and the B4 skip remain separate unfinished work. Offline certificate verification is the accepted scope; no online OCSP equivalence is assumed.

## Strengths

- `AppStoreServerClient.cs:129–143,277–304` filters canonical candidates by requested original ID before ranking and rechecks the verified signed ID. `IapService.cs:46–48,119–121` and `AppStoreNotificationService.cs:61–62` also protect persistence boundaries against an unrelated purchase. The new selection and refund tests exercise this behavior rather than merely testing a helper.
- `SubscriptionValidationPolicy.cs:8–29` uses trusted server settings and an explicit product allow-list; local unsigned verification is restricted by configuration, host and build. `AccountEntitlementService.cs:31–38` derives selected entitlement and paid history from applicable rows and preserves the distinction between ordinary expiry and revocation.
- `EntitlementService.cs:68–104` reloads an existing row under the PostgreSQL purchase lock, quarantines invalid stored evidence before assignment, rejects older/undated overwrites, and retains claim/tombstone fields. `SharedSubscriptionPostgresTests.cs:23–36` specifically covers stale EF tracking across two contexts.
- `AppStoreNotificationService.cs:52,78,87–91` returns non-success when a purchase change cannot be processed; `SubscriptionLifecycleTests.cs:198–209` covers retry followed by matching refund persistence. Background refreshes preserve restored device linkage, and IAP refresh reselects an independent active purchase (`IapService.cs:130–131`).
- `AppleJws.cs:79–100,110–136` checks authenticated signing-time validity, signing-purpose OIDs, pinned trust and signature, while disposing certificates. `AppleJwsTests.cs:15–71` includes positive official chains, negative purpose/date/root cases, tampering and historical signing-date verification. The pinned [Apple reference verifier](https://github.com/apple/app-store-server-library-node/blob/bb0c0f874494321ea2d005329c3dc2188e893d41/jws_verification.ts#L191-L285) supports the stated offline design; documented clock-skew and online-check differences are not hidden.

## Issues

### Critical

None found in this task-scoped review.

### Important

1. **[P1] Infinite database expiry still grants premium.** `src/Mavrylo.Services/Services/SubscriptionValidationPolicy.cs:32–33` requires only `ExpiresAt.HasValue`. PostgreSQL `infinity` is materialized as `DateTime.MaxValue`, so the new policy accepts it; `EntitlementService.cs:154–162` then treats it as active premium indefinitely. This directly misses the finite-expiry requirement for existing database rows and also prevents the intended quarantine from recognizing such a row. The [Npgsql date/time documentation](https://www.npgsql.org/doc/types/datetime.html#infinity-values) documents this mapping, and the focused probe below confirms it with this application's installed provider and compiled implementation. Reject the provider's infinity sentinels in the shared expiry validation, retain raw records through the existing quarantine path, and add device/account cached-row plus zero-mutation refresh coverage. The read-only probe establishes the behavior for such a row; it does not claim that production currently contains one.

2. **[P2] Claimed invalid purchases bypass the new device projection contract.** `src/Mavrylo.Services/Services/EntitlementService.cs:133–134` returns `account_required` directly whenever `ClaimedAt` is present, copying raw `AutoRenew` and `WasEverPaid`, without calling `IsApplicable` or `ToEntitlement`. A claimed or tombstoned Sandbox row under Production therefore projects `account_required / WasEverPaid=true`, while the same row through `ToEntitlement` correctly projects `invalid_subscription / WasEverPaid=false`. `DeviceContextService.cs:60` uses the bypassing method on the actual device path. This is an integration omission in the new shared-policy contract: invalid existing device rows must have the explicit invalid status and must not claim validated paid history. Current server AI is still denied by `AiProtectionFilter.cs:116–124`; the defect is the public status/history contract, including future client D3 behavior, not an active-AI bypass. Apply invalid-row projection before the valid claimed-purchase branch; retain raw claim/tombstone fields and preserve `account_required` for valid claimed rows. Add claimed and tombstoned invalid-row tests through the real device resolver. Existing `SubscriptionLifecycleTests.cs:30–43` uses `ToEntitlement` even after assigning `ClaimedAt`, so it does not catch this bypass.

### Minor / existing validation noise

- `../task4-results/verified-final.log:1–4` and `verified-release-tests.log:1–4` contain existing NU1903 warnings for SQLitePCLRaw.lib.e_sqlite3 and SSH.NET; `verified-release.log:1,9` contains NU1510 for System.Formats.Cbor. The output is not pristine. These are disclosed pre-existing dependency/build warnings, not introduced by Task 4, and are not an additional Task 4 blocker. Track dependency remediation separately and remove the redundant package reference in a suitable cleanup.

## Named out-of-diff risks checked

- **New validation not reaching device authorization / unchanged DeviceContextService:** inspected `DeviceContextService.cs:22–60`, the applicable gate at `Filters/AiProtectionFilter.cs:115–143,257–263`, and `DeviceWordService.cs:35–46`. Entitlement injection propagates without a DeviceContext constructor change; unknown/invalid states do not authorize new AI. This check identified Important finding 2. DeviceContextService was named in the brief, but an independent duplicated policy implementation is unnecessary.
- **Typed quarantine response lost at HTTP boundaries:** inspected `Areas/OwlAI/Controllers/AppStoreNotificationsController.cs:27–33`, `IapController.cs:44–51`, and `AccountSubscriptionController.cs:32–33`. All forward the service's 503, and the latter two retain its body/code. Searched the production upsert call sites: IAP verify/refresh, account claim/refresh, and notifications are the relevant callers and handle the typed quarantine outcome.
- **Claim/tombstone evidence accidentally repaired or reopened:** inspected `SubscriptionOwnershipService.cs:10–20` and `SubscriptionEntity.cs:12–18`. Conditional ownership updates and retained `ClaimedAt` still prevent a tombstone from becoming an unclaimed purchase; no deletion or ownership rewrite was introduced by the refresh changes.
- **Nullable expiry is not equivalent to finite persisted expiry:** inspected `SubscriptionEntity.cs:28–29`, `AppDbContext.cs:88–95`, and searched production source for Npgsql infinity-conversion overrides (none found). Ran the single external probe described below. This check identified Important finding 1.
- **Truncated hunk context:** the first package read was truncated by the tool; only the missing package portions were recovered in bounded chunks. The cut-off transaction-verification wrapper and notification-handler prefix were inspected to evaluate signature-failure/canonical-response handling. No whole changed source file was re-read and no git state/diff commands were rerun.

## Evidence reviewed and focused probe

- Read the task brief, implementation report, supplied review package, and Apple-validation preflight. Compared the offline crypto decisions with the pinned official Apple Node verifier. Did not rely on the implementation report's self-review as proof.
- Inspected final saved logs: Debug full suite **457 passed / 3 failed / 1 skipped / 461 total** (`verified-final.log:13–43`); Release build succeeded (`verified-release.log:7`); Release Apple/JWS/lifecycle selection **45/45 passed** (`verified-release-tests.log:14`). The controller independently parsed the corresponding results. The remaining failures are B1 new-key UUID inheritance and the two B2 copied-receipt ownership cases; the B4 skip is unchanged. No suite was rerun.
- Focused probe lives outside the checkout at `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task4-results/reviewer-probe/`. It references the existing Debug binaries, executes only `SELECT 'infinity'::timestamptz` against the supplied local PostgreSQL, and directly invokes the policy/projection methods. It writes no database rows and makes no Apple/AI request. Final execution exited 0 with no warning; output is saved in `../task4-results/reviewer-probe.log:1–5`:

```text
PostgreSQL infinity reads as DateTime.MaxValue: True
Policy accepts infinite expiry: True
Computed entitlement: premium
Claimed Sandbox general projection: invalid_subscription, WasEverPaid=False
Claimed Sandbox device projection: account_required, WasEverPaid=True
```

## Assessment

**Spec compliance: Needs fixes. Task quality: Needs fixes.** Correct the two validation/projection omissions and add the focused regressions. The identity, lifecycle-ordering, quarantine, retry and offline-certificate work otherwise matches the task's bounded design; this review does not grant whole-feature or release acceptance.
