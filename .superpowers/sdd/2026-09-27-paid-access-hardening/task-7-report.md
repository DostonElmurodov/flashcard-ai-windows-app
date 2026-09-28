# Task 7 implementer report

Review-fix round 1 is recorded separately in [task-7-fix1-report.md](task-7-fix1-report.md). The implementation/results below remain the historical 414bc25 checkpoint; the later fix report identifies its own source, evidence and acceptance boundary.

Local implementation ready for controller verification and the separate clean-context review. This is not approval to enable production traffic or completion of the paid-access feature.

- Worktree: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`
- Branch: `codex/paid-access-hardening`
- Accepted base: `77e67f4886586e9e60c680717d696f3e09c11ab3`
- Commit: `414bc25cf76e5a433e872f71e0a172008475e010` (`Guard provider expenditure with persistent bounded attempt reservations`)
- Personal directory-scoped Git identity was verified and preserved. Worktree clean after commit.
- Final source is associated with `task-7-release-focused-final.log`, `task-7-release-build.log`, and `task-7-release-full.log`. No source edits occurred between these final checks and commit.
- No subagents, live provider/Apple/email/database calls, provider-account probes, push, PR, remote workflow, release or deployment. Official documentation reads and local PostgreSQL/fake/loopback tests only.

## Delivered behavior

One shared provider transport now reserves before each OpenAI/Gemini HTTP attempt. All four registered operations and word-detail's primary/secondary single-word bundle pass through it. PostgreSQL serializes cumulative global and guest/account aggregate reservations with a short transaction advisory lock. Integer micro-USD reservations and persistent concurrency slots survive new contexts/service instances and missing completion. No daily/monthly reset, TTL release or identity-based budget reset exists.

Both accepted filters establish a trusted scoped subject after their existing authorization gates, including the authenticated Development test-mode path. Missing context, unsupported provider/model/profile, missing credentials, invalid limits, overflow, unavailable storage and budget/concurrency exhaustion are terminal controlled denials. WordAiService maps local spend denials to its established503 AI-unavailable response on every route including Batch. Provider401/402/403/429 and cancellation also stop fallback. Each permitted fallback gets a separate reservation. Valid service cache hits do not reserve or send.

Inputs are bounded before Base64/JSON serialization. Both request formats explicitly select standard processing and output caps; Gemini selects one candidate and zero thinking budget. Exact HTTP/1.1 non-null POST/no ExpectContinue plus disabled redirect/proxy/credentials/cookies/decompression/draining constrain transport behavior. Named-client header values are redacted. Streaming responses have both incremental byte caps and a linked deadline covering reservation, send, headers and body; settlement has its own five-second deadline. Local actual-handler tests cover submitted-body disconnect, pooled-connection disconnect,307 redirect, oversized chunked body, and headers-first body stall.

Only validated usage can reduce the frozen reservation: matching model/tier, positive bounded input, bounded nonnegative output/reasoning, consistent totals and cache components, checked arithmetic. OpenAI reasoning is a subset of output; ordinary/cache-read/cache-write inputs are disjoint. Gemini output normalizes candidates + thoughts; cached content remains within prompt input. Missing fields (including OpenAI cache_write_tokens), malformed usage, over-cap counts, model/tier mismatch or unknown outcome retain full money and the slot. Valid settlement is idempotent under concurrent connections. Settled cost is a conservative usage ceiling, not the provider invoice.

Fixed-category internal events/metrics cover allowed/denied operations, reserved amounts, attempts, fallback, usage ceiling,80% thresholds and retained/failed settlement. Actual enforcement rejects spending beyond100%. Fallback and adapter log/alert text excludes raw exception messages and JSON paths; sentinel regressions cover this.

## Price calculation and rulings

Controller explicitly accepted conservative validated-usage settlement rather than exact invoice settlement. Only reviewed profile `2026-09-27-model-ceilings-v1` is live-capable; examples leave profile unset, budgets/limits zero and Enabled=false. Synthetic unit-test profiles are constructed only from the friend test assembly; the public factory exposes only reviewed profiles. No numeric live budget, D2 quota or reset policy was chosen.

- OpenAI full-context input ceiling1,050,000; maximum input rate $0.125/M cache-write ×2 long context ×1.10 regional ×2 fast = $0.55/M. Ordinary/read/write rates440/44/550 nano-USD per token; output $0.50/M ×1.5 ×1.10 ×2 = $1.65/M, or1650 nano/token. Although standard is requested and required on settlement, the broader price envelope remains conservative. The connector documented max input922,000 as well as context1,050,000; choosing the higher context is deliberate over-reservation.
- Gemini input ceiling1,048,576; requested standard text/image covered by priority envelope ordinary/cache/output $0.54/$0.054/$4.50 per million, or540/54/4500 nano/token. No audio/video/tools/grounding/explicit persistent cache creation is requested. Exact returned version/tier are required to settle; absent fields remain unknown.
- Reserve `ceil((max_input_tokens * maximum_input_nano_rate + max_output_tokens * output_nano_rate) /1000)` micro-USD. Synthetic output cap512 gives OpenAI578,345 and Gemini568,536 micro-USD. These are ceiling calculations, not approved budget examples.
- Hand-checked settlement fixture:100 OpenAI input =70 ordinary+20 cache read+10 cache write;30 output includes10 reasoning. `ceil((70*440+20*44+10*550+30*1650)/1000)=87` micro-USD. Gemini100 input,20 cached,10 candidates+7 thoughts gives `ceil((80*540+20*54+17*4500)/1000)=121`.
- Conservative cumulative accounting with no automatic renewal follows the dispatch ruling. Unknown slots may exhaust availability; the runbook requires disabling new spend on all replicas and separately reviewed, evidence-based reconciliation. No live repair/reset tool was added.
- Unknown configured primary provider and missing provider credentials are terminal configuration failures; silently choosing another paid provider could conceal invalid configuration.

Official-source URLs, request assumptions, runtime caveat and reconciliation procedure are in `docs/ai-provider-spend.md`. Source/rate documentation was refreshed during this implementation. The HTTP evidence is .NET10.0.12 locally, not a deployed-runtime guarantee; floating images/runtime changes require revalidation. Other services sharing API keys are outside this backend ledger.

## Changed files (29)

Authorization/DI/config: `Filters/AccountAiProtectionFilter.cs`, `Filters/AiProtectionFilter.cs`, `Program.cs`, `appsettings.example.json`, `ops/linux/owl-api.env.example`.

Persistence: `src/Mavrylo.Data/AppDbContext.cs`, `src/Mavrylo.Entities/Models/AiSpendReservationEntity.cs`, `src/Mavrylo.Data/Migrations/20260928020241_AddAiSpendReservations.cs`, corresponding `.Designer.cs`, `src/Mavrylo.Data/Migrations/AppDbContextModelSnapshot.cs`. Migration is additive only.

Services: `AiCostProfile.cs`, `AiProviderTransport.cs`, `AiSpendContext.cs`, `AiSpendDeniedException.cs`, `AiSpendGuard.cs`, `FallbackAiJsonService.cs`, `GeminiJsonService.cs`, `OpenAiJsonService.cs`, `WordAiService.cs`, `WordAiService.Batch.cs`, all under `src/Mavrylo.Services/Services`.

Tests: `tests/AiProviderBoundaryTests.cs`, `tests/AiProviderServiceTests.cs`, `tests/AiProviderTransportTests.cs`, `tests/AiSpendGuardTests.cs`, `tests/AiSpendRouteTests.cs`, `tests/FallbackAiJsonServiceTests.cs`, `tests/TestSupport/AiSpendTestSupport.cs`.

Docs: `docs/ai-provider-spend.md`, `docs/paid-access-rollout.md`.

## RED/GREEN evidence and source association

Raw logs are preserved in this SDD directory; original failed outputs were not overwritten. All commands ran in the worktree above. Common command unless noted:

`dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter '<filter below>' --logger 'console;verbosity=normal'`

Database runs used process-only `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`. The fixture creates and drops only exact owned owl_test_GUID databases. No shared/live DB was used.

| Cycle/log | Filter / source association | Observed result |
|---|---|---|
| red-1 | `FullyQualifiedName~MissingSpendConfigurationAndTrustedContext\|FullyQualifiedName~ProviderAuthorizationOrQuotaFailure`; original adapter/fallback code at accepted base plus the new tests |5 genuine behavioral failures: unconfigured adapters made HTTP and401/403/429 fell back |
| green-1 | same tests after initial adapter deny check and terminal fallback branch |5/5; temporary direct checks were subsequently replaced by shared transport |
| red-2 | `FullyQualifiedName~AiSpendGuardTests`; new interface scaffold with TryReserve returning null and Complete no-op, replica/restart test |1 genuine behavioral failure: expected one successful60 reserve, got none |
| green-2-3 | `FullyQualifiedName~AiSpendGuardTests\|FullyQualifiedName~AiProviderServiceTests\|FullyQualifiedName~FallbackAiJsonServiceTests`; persistent ledger/migration + guarded adapters and payload caps |20/20 |
| red-3 | `FullyQualifiedName~EveryTextRequestSpecifiesOutputLimitAndStandardTier`; original direct adapters with Enabled=true temporary check; body captured by real adapter test |2 genuine failures: missing output-limit/tier request properties |
| red-4 | `FullyQualifiedName~AiSpendGuardTests\|FullyQualifiedName~AiProviderBoundaryTests\|FullyQualifiedName~PrivateExceptionText`; guarded transport + accounting, original fallback raw exception logging still present |40 passed,1 genuine privacy failure: sentinel exception text leaked |
| green-4-transport | `FullyQualifiedName~AiProviderTransportTests\|FullyQualifiedName~PrivateExceptionText`; fixed logging plus actual Program named-handler loopback |6/6 |
| routes | `FullyQualifiedName~AiSpendRouteTests`; actual filters, adapters, guard and local Postgres under authenticated TestMode |13/13, including4 operations on2 surfaces plus Batch, cache, exhausted budgets and fallback |
| red-5 | `FullyQualifiedName~NormalAuthorizationGrantsContext\|FullyQualifiedName~UnknownPrimaryProviderFailsClosed`; fallback still silently selected OpenAI for unknown primary |1 genuine unknown-primary RED,1 passed,2 fixture errors (untranslatable token-claim expression and null required DeviceUuid); fixture errors are NOT product RED |
| green-5 | above plus `FullyQualifiedName~PrivateExceptionText`; corrected fixtures and terminal unknown-primary behavior |5/5; normal free-device/paid-device/account allowed paths reserve, quota/entitlement denials create no further spend |
| ledger-transport-migration | `FullyQualifiedName~SyntheticHundredBudget\|FullyQualifiedName~StartupAppliesAdditiveMigration` |3/3:100 synthetic budget admits60+40 and stops next HTTP, rejects60+60's second HTTP; actual startup upgrades previous schema preserving seeded row |
| release-focused | Release; filter `FullyQualifiedName~AiSpend\|FullyQualifiedName~AiProvider\|FullyQualifiedName~FallbackAiJsonServiceTests` |92 passed,1 test setup failure: Gemini-only slow-body test still selected default OpenAI after unknown-primary became terminal; corrected provider selection, not production denial behavior |
| red-6 | Release; `FullyQualifiedName~AiProviderBoundaryTests.UnsafeRequest`; missing API keys still threw fallback-eligible provider errors |10 passed,2 genuine missing-credential configuration failures |
| release-focused-final | final Release source, full focused filter above, terminal missing-key fix and final simplify |95/95 |

The early RED cycles were not committed individually; their source association is the accepted base plus the described introduced test/scaffold or intermediate behavior, and the raw logs identify the exact tests/line positions. Final verified source is immutable at the commit above. Additional negative/transport/accounting cases were added as integration hardening while the core RED/GREEN cycles were in progress; no claim is made that every additional passing case had its own pre-implementation RED.

Migration command: task-local `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/tools/dotnet-ef.exe migrations add AddAiSpendReservations --project src/Mavrylo.Data/Mavrylo.Data.csproj --startup-project Mavrylo.csproj --output-dir Migrations`. Initial tool build failed (`task-7-migration.log`); after the test build succeeded, the same command with `--no-build` generated the additive migration (`task-7-migration-retry.log`). Subsequent migration/actual-startup tests passed. This is a tooling/setup failure, not RED behavior.

## Manual simplify after iterations

No standalone simplify tool was installed. Manual passes were performed after every coding/fix iteration:

1. Initial denial/fallback fix: kept one terminal exception branch and no ownership/schema changes; temporary direct config checks were explicitly transitional.
2. Ledger iteration: one lock protocol and one two-scope budget loop, frozen reservation rates, no HTTP under the transaction, no automatic reset/TTL or extra owner tables.
3. Adapter/payload iteration: consolidated HTTP, response caps, deadline and settlement into AiProviderTransport; removed both duplicated unbounded HTTP implementations and their mutable client timeout/header setup.
4. Expanded boundary/logging iteration: fixed-category errors replaced arbitrary exception text; test-only fake handlers/stream helpers remain only in tests. Maintained one normalized usage shape for provider differences.
5. Normal-route/unknown-primary iteration: corrected test fixtures at their data source; unknown primary now fails immediately. Cleaned inserted indentation and kept all mappings to existing503 behavior.
6. Final missing-credential fix/self-review: used the same local denial type, removed the hidden response-draining path, restricted price-profile construction/mutation to the service assembly, and checked all provider call sites still use the single transport. No unrelated refactors/dependency upgrades.

Manual diff review and `git diff --check`/staged check were clean (line-ending/global ignore-file permission warnings only). Existing personal identity remained in use.

## Final verification

- `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AiSpend|FullyQualifiedName~AiProvider|FullyQualifiedName~FallbackAiJsonServiceTests' --logger 'console;verbosity=normal'`:95 passed,0 failed. `task-7-release-focused-final.log`, UTC2026-09-28T02:23:27.
- `dotnet build Mavrylo.csproj -c Release --no-restore`: succeeded,0 errors,1 existing NU1510 warning. `task-7-release-build.log`, UTC02:24:12.
- `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-build --no-restore --logger 'console;verbosity=normal'`:545 total,541 passed,3 failed,1 skipped. `task-7-release-full.log`, UTC02:24:35. These are fresh results on this exact source, not the earlier462-pass baseline.

Full-suite unresolved cases, unchanged in `tests/PaidAccessAuditRegressionTests.cs`:

1. `B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid`: expected(429,0), actual(200,1).
2. `B2_CopiedReceiptCannotFirstClaimDesktopOwnershipWithoutPriorOwnerProof`: expected403, actual200.
3. `B2_CopiedReceiptCannotTransferExistingGuestPurchaseToUnrelatedDevice`: expected403, actual200.
4. Existing skip `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`: proof/shared-owner contract and D2 are pending. The independent aggregate spend tests do not prove that skipped combined contract.

Known NU1510/NU1903 warnings remain; no unrelated dependency changes. Local runtime logs show .NET10.0.12. Replica/restart ledger tests use independent database connections and newly constructed services/contexts, including omitted completion; this is persisted-state restart simulation, not killing a live production process. Actual API startup/migration and actual socket transport are also exercised locally.

## Pending scope / review handoff

Tasks2/3/6 still own immutable anonymous purchase-owner proof, recovery/key rotation, optional desktop-link binding and shared-owner operation quotas. An authenticated device-key reference is not proof of immutable owner identity. D1=B (mobile purchase/paid use without Owl login) and D3 (ordinary expired paid retains saved reading/review) are not changed by this backend spend machinery.

Live monetary allowances, numeric D2 quotas, reset/renewal policy, production effective configuration/runtime validation and deployment remain unapproved. Retained slots may require independently evidenced reconciliation; no automatic freeing, money reset, production run or notification was performed. Root must perform the requested independent verification and separate fresh GPT-6 Astra xhigh task review before accepting Task7. The full feature and rollout remain incomplete.

## Review-fix round 1 checkpoint

The two scoped findings in `task-7-review.md` and the controller's related handler connection-timeout check were fixed locally in `2a3fbc70a5306b4a4b6eeb57498e043164f3b8f3` and final HEAD `03bb0620098cdbe6d2c21b855271e2e8b968e029`. The backend worktree is clean. Final fresh focused Release checks pass **123/123** and the Release build has **0 errors**, with the existing NU1510 warning. Genuine RED and GREEN logs, per-stage source patches, exact commands, manual simplify records and remaining boundaries are in [task-7-fix1-report.md](task-7-fix1-report.md). The earlier full545 result above remains tied to414bc25; it was not rerun or relabeled for the fix. Audit ownership failures/skip and feature/release gates remain unresolved. Separate fresh scoped re-review is still pending.
