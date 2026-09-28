# Task 4 report — Apple identity, lifecycle and environment validation

Status: implemented and committed locally for independent review. No push, deployment, production configuration/data edit, purchase, Apple API call or AI-provider call was performed. The full suite is intentionally not all green because the three pending ownership audit failures are outside this task.

- Worktree: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`
- Branch: `codex/paid-access-hardening`
- Base: `1c31750305e44914bc3c8c7fbdf453d1e8eec7b4`
- Commit: `4db300428adccf820584aeb0860afd73af3b673f` — `Harden Apple subscription identity lifecycle and environment validation`
- Final worktree status: clean. The tested final source is exactly this commit; only this external report was written after commit.
- Evidence: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task4-results/`

## Resulting behavior

1. A mandatory shared `SubscriptionValidationPolicy` validates original identity, trusted configured environment, allowed product and finite expiry for device/account cached rows and signed transaction projections. There is no allow-all constructor. Signed projections additionally require auto-renewable subscription type. Production rejects Sandbox/LocalTesting/Xcode; explicitly configured Sandbox works with a non-Development server host. Local unsigned verification requires DEBUG + Development + explicit LocalTesting/Xcode. Missing server environment defaults to Production; missing product allow-list allows none.
2. Apple last-transaction candidates are filtered by requested original ID before ranking. The signed ID is checked again after verification. IAP verify/refresh, account refresh/claim and notification persistence all reject unrelated canonical identities. `REFUND A + active B` updates only A; B remains available. A successful IAP refresh reselects an independent active purchase when the previously selected one was revoked.
3. Renewal metadata must match original ID, environment and current product. A future `autoRenewProductId` cannot rewrite the current entitlement. Grace requires matching verified renewal evidence with a finite future grace end; canonical expired/retry/revoked status cannot be overridden by stale grace metadata. Ordinary expiration preserves valid paid history; revocation stays revoked instead of being overwritten by expired_paid.
4. Network/timeout/invalid-JSON Apple API failure returns a failed refresh without extending access. Notifications return non-2xx on failed projection, unavailable/mismatched canonical state, or quarantined stored data; they never ACK those lost changes. This relies on Apple delivery retry, not a newly implemented local durable queue.
5. Invalid existing records are quarantined with zero mutation. Their original metadata, `WasEverPaid`, device link, and claim/tombstone evidence are preserved. Verify/claim returns handled503 with `subscription_reconciliation_required`; cached reads remain inactive and notifications return503 for retry. This requires an explicit pre-enforcement reconciliation gate for potentially legitimate old rows, without forced repurchase.
6. PostgreSQL writes retain the existing advisory lock keyed by original ID and reload previously tracked rows inside it. Newer signed transaction/renewal date wins; old/undated state cannot overwrite established dated state. Matching-date restore may change only device linkage, preserving lifecycle and claim/tombstone fields. Lifecycle/background refresh never relinks a restored device. Valid newer canonical state can clear an older refund for that same original purchase.
7. JWS verification now checks Apple leaf/intermediate purpose OIDs and certificate validity at the authenticated signing date. Runtime root remains embedded Apple Root G3. Test trust injection is internal only and absent from runtime configuration. Certificates/documents are disposed; offline chain verification disables certificate downloads.

`DeviceContextService` obtains the policy through `EntitlementService`; it did not need an independent policy or new constructor. Public DTO shapes remain unchanged. `invalid_subscription` is a new inactive status for an existing invalid device record, distinct from a genuinely absent/free purchase. The controller approved carrying this contract into client Tasks8/9.

## TDD commands and evidence

All commands ran in the backend worktree. Unless noted otherwise:

```powershell
dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter '<filter below>' --logger 'trx;LogFileName=<evidence>.trx' --results-directory ../task4-results
```

The complete console output is the corresponding `.log` file. Test exit codes were reflected by the result summaries/TRX (shell wrapper exit codes are not used as proof of a green test run).

| Evidence | Filter | Result |
|---|---|---|
| iteration1-red | `FullyQualifiedName~SubscriptionLifecycleTests\|FullyQualifiedName~PaidAccessAuditRegressionTests.B5\|FullyQualifiedName~PaidAccessAuditRegressionTests.B6\|FullyQualifiedName~PaidAccessAuditRegressionTests.B2_DeviceVerify` | 15 expected failures,10 passed |
| iteration1-apple-red | `FullyQualifiedName~AppStoreServerClientTests` | 8 expected failures,8 passed |
| iteration1-green; iteration1-simplified | `FullyQualifiedName~SubscriptionLifecycleTests\|FullyQualifiedName~AppStoreServerClientTests\|FullyQualifiedName~EntitlementServiceTests\|FullyQualifiedName~SharedSubscriptionTests\|FullyQualifiedName~PaidAccessAuditRegressionTests.B5\|FullyQualifiedName~PaidAccessAuditRegressionTests.B6\|FullyQualifiedName~PaidAccessAuditRegressionTests.B2_DeviceVerify` | 60/60 twice |
| iteration2-red; iteration2-green | `FullyQualifiedName~AppStoreServerClientTests\|FullyQualifiedName~SubscriptionLifecycleTests` | 6 expected failures/40 passed →46/46 |
| crypto-red | `FullyQualifiedName~AppleJwsTests` | 5 expected failures,6 positive/negative controls passed |
| crypto-green | `FullyQualifiedName~AppleJwsTests\|FullyQualifiedName~AppStoreServerClientTests` | 34/34 |
| quarantine-red | `FullyQualifiedName~SubscriptionLifecycleTests` | 2 expected failures,22 passed |
| quarantine-green | `FullyQualifiedName~SubscriptionLifecycleTests\|FullyQualifiedName~SharedSubscriptionTests` | 35/35 |
| full-first | No filter | 436 passed,18 failed,1 skipped;15 failures were old incomplete/misconfigured fixtures,3 pending ownership audits |
| fixtures-green | `FullyQualifiedName~AiProtectionFilterTests\|FullyQualifiedName~AiRequestInterpretationTests\|FullyQualifiedName~SharedSubscriptionPostgresTests\|FullyQualifiedName~TestModeRouteTests\|FullyQualifiedName~ReviewTranslationTests` | 149 passed/8 failed; intended ApiFactory settings had not been applied by the failed multi-file patch |
| fixtures-config-green | Same fixture filter | 157/157 after applying explicit test-host product/environment settings |
| ordering-red | `FullyQualifiedName~StaleTrackedRow\|FullyQualifiedName~LifecycleNotificationPreserves\|FullyQualifiedName~UndatedUpdate` | 3/3 expected failures |
| ordering-green | `FullyQualifiedName~SubscriptionLifecycleTests\|FullyQualifiedName~SharedSubscriptionPostgresTests\|FullyQualifiedName~PaidAccessAuditRegressionTests.PositiveControl` | 37/37 |
| replay-restore-red | `FullyQualifiedName~SameDatedCanonicalRestore` | 1/1 expected failure |
| replay-restore-green | `FullyQualifiedName~SubscriptionLifecycleTests\|FullyQualifiedName~SharedSubscriptionTests` | 38/38 |
| selection-red | `FullyQualifiedName~RefreshRevokingSelectedPurchase` | 2/2 expected failures |
| selection-green | `FullyQualifiedName~SubscriptionLifecycleTests` | 29/29 |

In the table, `\|` is Markdown escaping of the ordinary test-filter separator `|`; the executed filter argument uses `|`.

### Final checks on the committed source

```powershell
$env:OWL_TEST_POSTGRES = 'Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests'
dotnet test tests/Mavrylo.Tests.csproj --no-restore --logger 'trx;LogFileName=verified-final.trx' --results-directory ../task4-results
dotnet build Mavrylo.csproj -c Release --no-restore
dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AppleJwsTests|FullyQualifiedName~AppStoreServerClientTests|FullyQualifiedName~SubscriptionLifecycleTests' --logger 'trx;LogFileName=verified-release-tests.trx' --results-directory ../task4-results
git diff --check
git diff --cached --check
```

- Full Debug suite: **457 passed,3 failed,1 skipped,461 total**,16 seconds. `verified-final.trx` / `.log`.
- Release build: **success,0 errors,1 existing NU1510 warning** for redundant `System.Formats.Cbor`. `verified-release.log`. A new Release-only CS9113 warning was removed by moving the unchanged host guard outside the DEBUG conditional; these final checks ran afterward.
- Release Apple/JWS/lifecycle selection: **45/45 passed**,4 seconds. `verified-release-tests.trx` / `.log`. This also exercises the Release rejection of unsigned Development receipts.
- Diff whitespace checks passed. Git reports existing local ignore-file access warnings and LF/CRLF normalization notices; these did not prevent a clean commit.
- Existing test restore warnings remain: SQLitePCLRaw.lib.e_sqlite3 2.1.11 and SSH.NET 2025.1.0 advisories. No dependency upgrades were mixed into this task.

Real PostgreSQL fixtures created/dropped isolated local test databases using the supplied test account. The ordering regression explicitly uses two database contexts and proves that a tracked pre-lock row cannot overwrite newer persisted state. The existing concurrent claim tests also pass. All Apple/AI interactions in tests use fakes or offline fixture verification.

### Remaining known audit failures (not disabled)

- `PaidAccessAuditRegressionTests.B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid` — inherited UUID still gives the pending guest ownership problem.
- `PaidAccessAuditRegressionTests.B2_CopiedReceiptCannotFirstClaimDesktopOwnershipWithoutPriorOwnerProof` — expected403,actual200; pending owner proof/recovery redesign.
- `PaidAccessAuditRegressionTests.B2_CopiedReceiptCannotTransferExistingGuestPurchaseToUnrelatedDevice` — expected403,actual200; pending owner proof/recovery redesign.

Existing skipped test: `PaidAccessAuditRegressionTests.B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`; its prior skip remains because the key-rotation owner proof/global guest budget contract and D2 values are pending. No tests were disabled or weakened to conceal failures. B5/B6 and B2 canonical-ID mismatch now pass, as do same-device guest purchase/restore and already-claimed ownership positive controls. No mandatory iPhone account requirement was introduced.

## Cryptographic references and limits

Pinned official Apple Node repository commit: `bb0c0f874494321ea2d005329c3dc2188e893d41`.

- [Verifier source](https://github.com/apple/app-store-server-library-node/blob/bb0c0f874494321ea2d005329c3dc2188e893d41/jws_verification.ts)
- [Official chain tests](https://github.com/apple/app-store-server-library-node/blob/bb0c0f874494321ea2d005329c3dc2188e893d41/tests/unit-tests/jws_verification.test.ts)
- [Signed mock fixtures](https://github.com/apple/app-store-server-library-node/tree/bb0c0f874494321ea2d005329c3dc2188e893d41/tests/resources)
- [MIT license](https://github.com/apple/app-store-server-library-node/blob/bb0c0f874494321ea2d005329c3dc2188e893d41/LICENSE.txt)
- [Apple Python OCSP advisory](https://github.com/apple/app-store-server-library-python/security/advisories/GHSA-8f6j-263m-g72x): affected through3.1.1,patched3.1.2. No Python verifier/package was used.

Comparison: the official offline verifier uses payload signedDate (current time if absent), a trusted chain, leaf OID `1.2.840.113635.100.6.11.1`, intermediate OID `1.2.840.113635.100.6.2.1`, and signature validation. Its online mode adds current-time/OCSP verification and distinguishes retryable online failures. This change implements the offline date/purpose checks, not online revocation. .NET chain date boundaries are strict; Apple's Node verifier permits60 seconds of clock skew. Runtime trust stays at the existing embedded G3 root; the Node library itself accepts a caller-provided root list.

Before changing checks, existing chain/decoder logic was mechanically extracted into internal methods, keeping its old semantics. Crypto red proved that invalid purpose chains were previously accepted, wrong effective dates ignored, and historically valid expired certificates rejected. Tests now cover:

- Official synthetic valid chain and real-Apple certificate chain at fixed reference date1761962975000.
- Wrong leaf purpose, wrong intermediate purpose, mismatched root, before/after validity failures.
- Official signed transaction/renewal/notification mock payloads under the official test root; the runtime pinned-root decoder rejects those test fixtures.
- Signed payload tampering fails signature validation.
- Locally generated cryptographically signed historical JWS verifies at its signed date even when leaf/intermediate are expired now; a date outside their validity fails.

Fixtures are attributed with pinned source and the MIT license in `tests/Fixtures/Apple/`. Tests do not use a real purchase or assert physical Apple acceptance. They ran on Windows/.NET10, not the deployed Linux certificate implementation or a physical iPhone. The isolated App Review/Sandbox/device matrix remains required.

## Explicit simplify and self-review record

No standalone simplify tool is installed. Manual reuse/complexity/efficiency passes were performed after every coding/fix iteration and logged in `task4-results/simplify-log.md`:

1. Consolidated environment/product/expiry validation and status ranking; removed permissive defaults and duplicate account logic; reused original DTO shapes and lock.
2. Reused canonical status switch and renewal decoder; one bounded transport wrapper; no retry loop, cache or background job.
3. One offline chain routine shared by runtime and tests; no new cryptographic dependency; deterministic offline chain building; dispose all loaded certificates/documents.
4. Centralized zero-mutation quarantine via one typed exception handled at all write boundaries; no hidden repair/fallback or new ownership policy.
5. Updated incomplete fixtures without changing MVC formatter or charset assertions. One existing non-UTF8 test file required a byte-preserving ASCII-only replacement; its diff is one added product field. The failed all-files UTF8 patch applied none of its changes and was retried narrowly.
6. Added under-lock reload rather than a second concurrency mechanism; one explicit non-relink option for background refresh; retained valid record history/claim ownership.
7. Same-date restore updates linkage alone; no lifecycle replay or tombstone reset.
8. Reselect an independent purchase after refresh through the existing rank method; one extra query only after successful refresh, no recursive Apple API calls.
9. Final Release warning cleanup preserved behavior; documentation records the exact rollout/contract boundaries. Full final suite, Release build and Release tests were rerun afterward.

Self-review covered all canonical write callers, paid-history selection, invalid-row preservation, source/reference semantics, SQL lock/EF tracking interaction, old/repeated/newer events, independent purchases, guest restore and claim/tombstone preservation. The separately requested clean-context reviewer remains the controller's next step; no subagents/reviewers were spawned by this implementer.

## Remaining boundaries for rollout

- Tasks2/3 guest ownership and recovery proof remain pending. The three audit failures above prevent claiming the whole paid-access hardening effort is complete.
- `invalid_subscription` and the503 reconciliation code need client integration/unknown-inactive handling before release.
- Reconcile invalid/incomplete historical purchase records before enforcing the new policy. A correct new receipt alone intentionally does not rewrite their historical evidence in this task.
- Sandbox/App Review needs separate trusted server configuration, data and usage budgets. No such deployment or production setting was created/changed here; D2 numeric limits remain pending.
- Apple delivery retry has an operational window. There is no local durable queue or historical-notification recovery job in this task; do not claim indefinite eventual recovery after prolonged outages.
- Offline trust does not establish online certificate revocation status. No online OCSP parity, physical device purchase acceptance, App Review acceptance or Linux runtime acceptance is claimed.

The committed `docs/apple-subscription-validation.md` carries the stable backend contract and these rollout boundaries.

## Review fix round 1 — finite expiry and claimed-device projection

Both Important findings in `task-4-review.md` are fixed locally for fresh scoped review.

- Fix base: `4db300428adccf820584aeb0860afd73af3b673f`.
- Fix commit: **`5c875dfe0f05b076abcda6da86e5ebd151652970`** — `Reject infinite subscription expiry and validate claimed device projection`.
- Worktree/branch unchanged; final worktree clean. The focused Debug/Release checks below ran on exactly the source committed in this fix.
- Five changed files: the shared policy, device entitlement projection, two existing test files, and the backend validation document. No crypto, ownership recovery, dependency, deployment or production-data changes.

### Findings reproduced and fixed

**Finding 1 — PostgreSQL infinity:** six new real-PostgreSQL cases failed before the fix. The tests query actual `infinity` and `-infinity` timestamp values and confirm this installed provider maps them to `DateTime.MaxValue` and `DateTime.MinValue`. On the pre-fix source, the device resolver returned premium or expired_paid instead of invalid_subscription, and all four claimed/tombstoned refresh cases failed because no quarantine exception was raised. The central policy now requires the expiry strictly between those sentinels. Existing consumers consequently exclude infinity rows from device/account entitlement and validated paid history. The green tests also execute the account resolver and require free/WasEverPaid=false for accounts with only invalid rows.

Zero-mutation coverage reloads persisted records without tracking and compares the serialized entire row before and after a rejected refresh. Both infinity directions and both claimed-owner/tombstone states preserve raw expiry, paid history, device linkage, event/check timestamps, status and ownership metadata. No automatic repair or deletion was added.

**Finding 2 — claimed device projection:** four new tests exercise `DeviceContextService.ResolveAsync`, including real subscription selection and its claimed-device marker. The two invalid Sandbox cases (claimed and tombstoned) failed with account_required; the two valid Production controls already passed. `ToDeviceEntitlement` now calls the canonical validated projection first. Only a valid claimed row receives the existing account_required state, using a record-with expression rather than rebuilding entitlement fields from raw data. Invalid rows retain invalid_subscription/WasEverPaid=false/AutoRenew=false. Valid claimed and tombstoned records retain account_required, and the raw subscription rows remain identical. The retained device claim marker is preserved; no mandatory iPhone account or new anonymous ownership rule was introduced.

### Exact verification commands and results

Run from `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`. Evidence lives in the existing `task4-results` directory with `fix1-` prefixes.

```powershell
$env:OWL_TEST_POSTGRES = 'Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests'
dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProviderInfinityExpiry' --logger 'trx;LogFileName=fix1-infinity-red.trx' --results-directory ../task4-results
dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProviderInfinityExpiry' --logger 'trx;LogFileName=fix1-infinity-green-local.trx' --results-directory ../task4-results
dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter 'FullyQualifiedName~DeviceResolverValidatesClaimedAndTombstoned' --logger 'trx;LogFileName=fix1-claimed-red.trx' --results-directory ../task4-results
dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter 'FullyQualifiedName~EntitlementServiceTests|FullyQualifiedName~SubscriptionLifecycleTests|FullyQualifiedName~SharedSubscriptionTests|FullyQualifiedName~SharedSubscriptionPostgresTests|FullyQualifiedName~DeviceWordsControllerTests|FullyQualifiedName~AiProtectionFilterTests|FullyQualifiedName~PaidAccessAuditRegressionTests.B5|FullyQualifiedName~PaidAccessAuditRegressionTests.B6|FullyQualifiedName~PaidAccessAuditRegressionTests.PositiveControl' --logger 'trx;LogFileName=fix1-focused-green.trx' --results-directory ../task4-results
dotnet build Mavrylo.csproj -c Release --no-restore
dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EntitlementServiceTests|FullyQualifiedName~SubscriptionLifecycleTests|FullyQualifiedName~SharedSubscriptionTests|FullyQualifiedName~SharedSubscriptionPostgresTests' --logger 'trx;LogFileName=fix1-release-green.trx' --results-directory ../task4-results
git diff --check
git diff --cached --check
```

The two red commands ran before their respective production fixes. The PowerShell test-server variable was explicitly supplied per process for every valid PostgreSQL run.

| Evidence | Result |
|---|---|
| `fix1-infinity-red.trx` / `.log` | 6 expected failures,0 passed; real database sentinel/projection/quarantine reproduction |
| `fix1-infinity-green.trx` / `.log` | Infrastructure-only failed attempt: the command omitted its per-process `OWL_TEST_POSTGRES`, so the fixture tried unavailable Docker and failed before test bodies. Not green evidence; no code change made for this invocation error. |
| `fix1-infinity-green-local.trx` / `.log` | 6/6 passed after explicitly supplying the existing local PostgreSQL connection |
| `fix1-claimed-red.trx` / `.log` | 2 expected invalid-row failures,2 valid claimed/tombstone controls passed |
| `fix1-focused-green.trx` / `.log` | **118/118 passed**,0 skipped; includes all new cases and surrounding device/account selection, quarantine, ownership positive controls, AI gates, and PostgreSQL concurrency/route tests |
| `fix1-release-build.log` | **Release build succeeded,0 errors**,1 existing NU1510 warning |
| `fix1-release-green.trx` / `.log` | **65/65 passed**,0 skipped; changed validation/projection plus lifecycle, shared subscriptions and real PostgreSQL integration under Release |

The focused scopes were chosen for the two changed production paths and their integration consumers. As instructed for this narrow review fix, the unchanged whole suite was not rerun. The last full-suite result remains **457 passed/3 known ownership failures/1 B4 skip at4db3004**, and does **not** constitute a new full-suite run of5c875df. No whole-suite-green claim is made. Existing package warnings and the three pending ownership audit defects remain outside this fix.

### Simplify and self-review

Manual simplify was performed after each production fix; full notes are in `task4-results/fix1-simplify-log.md`.

- Finding 1: reused the mandatory shared policy and existing quarantine path; one strict date predicate, no extra fallback, helper, schema or write path. Test fixtures use the actual provider mapping and reload real persisted evidence.
- Finding 2: reused `ToEntitlement` before branching and a record-with for valid claimed state; no duplicated validity conditions or raw-field reconstruction, no new database mutation.
- Final self-review checked the five-file diff, raw-row preservation assertions, valid claimed/tombstone positive controls, Release behavior and clean whitespace. No additional scope was introduced after the final tests.

The existing rollout reconciliation gate now explicitly includes PostgreSQL non-finite expiry. Client invalid-state handling, legacy reconciliation, isolated Sandbox/App Review, physical-device acceptance, and the pending anonymous ownership work remain as documented above. No subagents, live provider calls, push or deployment occurred in this fix round.
