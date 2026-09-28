# Task 7 review fixes, round 1

Both scoped review findings are implemented and locally verified. Fresh controller verification and the separate clean-context GPT-6 Astra xhigh re-review remain required. This is not whole-feature acceptance or permission to enable production spending.

- Worktree: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`
- Branch: `codex/paid-access-hardening`
- Fix base: `414bc25cf76e5a433e872f71e0a172008475e010` (verified clean before edits).
- Initial R1/R2 fix commit: `2a3fbc70a5306b4a4b6eeb57498e043164f3b8f3` (120/120 focused checks and the first Release build apply to this checkpoint).
- Final commit including the controller-requested connection-timeout compatibility check: `03bb0620098cdbe6d2c21b855271e2e8b968e029` (`Map handler connection deadlines to terminal AI unavailability`). Tree `88ce2dd71703d24dfa8d1925d8680f1c7717594f`. Backend worktree clean after commit.
- Personal directory-scoped Git identity preserved. No agents, live provider/Apple/email/database calls, provider-account probes, remote workflow, push, PR, release or deployment. Official documentation reads and local synthetic PostgreSQL/fake/loopback tests only.

## Corrections and preserved boundaries

**R1 / P1:** `AiProviderTransport.ReadUsage` treats only omitted Gemini `thoughtsTokenCount` and `cachedContentTokenCount` as zero. These plain proto3 integer fields omit default values under the documented protocol. Present malformed/null values still fail parsing; missing usage, required aggregate counts, model or tier remain unknown. Existing nonnegative, checked arithmetic, total consistency, cap and exact metadata checks still control settlement. OpenAI's required cache-write component is unchanged. The official schema and encoding references are recorded in `docs/ai-provider-spend.md`.

Real PostgreSQL guard tests execute two consecutive successful adapter calls with a synthetic concurrency limit of 1, comparing omitted counters with explicit zeros. Both now charge `ceil((100 × 540 + 10 × 4500)/1000) = 99` micro-USD per call, pending slots return to 0, and the second call is admitted. This is a conservative validated-usage ceiling, not an invoice. Fifteen negative controls retain the full synthetic 568,536 micro-USD reservation and slot and deny the next attempt: absent usage/aggregates/model/tier, unknown metadata, negative/malformed components, inconsistent totals, excessive output and overflow. The pre-existing OpenAI missing-cache-write and invalid-usage controls also pass.

**R2 / P2:** During reservation or provider send/body consumption, cancellation is translated to the existing terminal `AiSpendDeniedException("provider_deadline")` only when the linked internal deadline is canceled and the caller token is still active. All WordAiService paths already map this type to `503 {"error":"ai_translation_unavailable"}`. No new exception hierarchy or route-specific catch changes were necessary. Caller cancellation still propagates as cancellation. Unknown post-dispatch outcomes still complete with unknown usage, retaining the full money reservation and persistent slot. No fallback, outage email, TTL release or reset was introduced.

Five actual local Kestrel requests cover analyze-word, word-detail, review-translation, extract-words and Batch, selecting both registered providers across the cases. The caller uses `CancellationToken.None`; each response is 503 with the established error code, exactly one provider request, no alert, one unsettled full reservation and one retained pending slot. Adapter and real configured-handler loopback deadline tests cover the same classification. A stalled reservation control sends no HTTP. Both providers have post-dispatch caller-cancellation controls through the real ledger, then a newly constructed database context verifies full retained money and slot. Restart here means new context/connection, not process killing.

**R2 connection-timeout compatibility extension requested by the controller:** after the initial fix commit, the controller identified the handler's separate 10-second `ConnectTimeout`. The pinned [.NET 10.0.12 pool source](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/ConnectionPool/HttpConnectionPool.cs) represents this with an `OperationCanceledException` whose direct inner exception is `TimeoutException`, independent of the linked overall deadline. Two new actual Kestrel controls kept the registered application handler, asserted its 10-second connect setting, shortened only that timer to a synthetic 80 ms and substituted a cancellation-aware stalled `ConnectCallback`; no remote socket or DNS request was made. With the caller active and the overall deadline 5 seconds, both providers reproduced HTTP 500. A caller-cancellation-during-connect control passed.

The provider-phase catch now also recognizes that exact timeout shape, still requiring an active caller. The reservation phase is unchanged: only its linked deadline receives this mapping. Genuine caller cancellation and arbitrary cancellation without the timeout shape still propagate. Both new route controls now return the established 503, show one connection attempt, no fallback/alert, and retain the full money reservation and slot. This is deliberately conservative even though the test callback proves no socket was opened; production accounting does not infer remote completion from a timeout.

## Behavioral RED and GREEN evidence

All commands ran in the backend worktree with process-only:

```powershell
$env:OWL_TEST_POSTGRES='Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests'
dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter '<filter below>' --logger 'console;verbosity=normal'
```

Raw output is preserved in the corresponding `task-7-fix1-*.log`. Times below are final log-write UTC on 2026-09-28; they are source/evidence ordering, not claimed exact command start times. Each stage's `*-source.patch` was captured before execution: R1/R2 patches are against fix base 414bc25; connect patches are against initial fix commit 2a3fbc7. Intermediate stages are uncommitted patches, not invented commit IDs. `task-7-fix1-final-source.patch` combines both fixes against 414bc25 and was captured before final verification.

| Stage | Filter | Result | UTC log completion |
| --- | --- | --- | --- |
| `r1-red` | `FullyQualifiedName~GeminiProtocolZeroComponents\|FullyQualifiedName~GeminiZeroDefaults` | 17 total: 16 passed, 1 genuine behavioral failure. Omitted counters retained 568536 and no settlement; explicit-zero and all negative controls passed. Exit 1. | 02:51:30 |
| `r1-green` | `FullyQualifiedName~GeminiProtocolZeroComponents\|FullyQualifiedName~GeminiZeroDefaults\|FullyQualifiedName~MissingCacheWriteUsage\|FullyQualifiedName~InvalidUsageRetainsMoney` | 28/28 passed. Exit 0. | 02:54:48 |
| `r2-red` | `FullyQualifiedName~InternalDeadlineReturnsUnavailable\|FullyQualifiedName~SlowBodyHonorsFullDeadline\|FullyQualifiedName~ReservationDeadlineIsTerminal\|FullyQualifiedName~AiProviderTransportTests\|FullyQualifiedName~CallerCancellationAfterDispatch` | 15 total: 6 passed, 9 genuine behavioral failures. Five actual HTTP routes returned 500; two adapter, one reservation and one configured-handler deadline exposed cancellation instead of terminal unavailability. Both caller-cancellation controls passed. Exit 1. | 02:56:58 |
| `r2-green` | Same R2 filter | 15/15 passed. Exit 0. | 02:58:09 |
| `release-focused` | `FullyQualifiedName~AiSpend\|FullyQualifiedName~AiProvider\|FullyQualifiedName~FallbackAiJsonServiceTests` | 120/120 passed. Exit 0. | 02:59:12 |
| `connect-red` | `FullyQualifiedName~HandlerConnectDeadline\|FullyQualifiedName~CallerCancellationDuringConnection` | 3 total: 1 passed, 2 genuine behavioral failures. Both providers returned 500 on the independent handler timer; caller cancellation passed. Exit 1. | 03:03:45 |
| `connect-green` | `FullyQualifiedName~HandlerConnectDeadline\|FullyQualifiedName~CallerCancellationDuringConnection\|FullyQualifiedName~CallerCancellationAfterDispatch\|FullyQualifiedName~InternalDeadlineReturnsUnavailable\|FullyQualifiedName~SlowBodyHonorsFullDeadline\|FullyQualifiedName~ReservationDeadlineIsTerminal` | 13/13 passed. Exit 0. | 03:04:55 |
| `release-focused-final` | Same combined focused filter | 123/123 passed. Exit 0. | 03:05:48 |

The escaped table separators represent ordinary `|` in the actual command filter. There were no build/setup failures mixed into these three RED runs. The original review probes remain unchanged and distinguish their earlier harness problems from product findings.

Final build: `dotnet build Mavrylo.csproj -c Release --no-restore`, log `task-7-fix1-release-build-final.log`, UTC 03:05:49, exit 0, 0 errors and 1 existing NU1510 warning. The earlier identical build command/log at 02:59:14 belongs to the 2a3fbc7 checkpoint. `git diff --check` passed. No source edits followed the final checks; only SDD evidence/report files outside the backend worktree were updated afterward. Source and raw-log SHA-256 associations are recorded in `task-7-fix1-evidence.json`.

No additional full-suite rerun was warranted for the parser/error-classification scope after all provider/guard/transport/fallback/route checks passed. The older full-suite result remains attached to **414bc25**, not this fix: 545 total = 541 passed, the same three B1/B2 ownership failures, one existing B4 skip. Those audit tests were neither changed nor waived. The prior report and raw full-suite log preserve their exact names and outputs.

## Manual simplify after each iteration

1. R1 test iteration: kept one complete Gemini response fixture with only the two optional zero components varied for the success comparison; used the real ledger for settlement/admission outcomes. Negative mutations share the fixture without adding product abstractions.
2. R1 implementation iteration: kept one local Gemini-only component reader; mandatory aggregates and OpenAI decoding retain their original reader. No generic missing-value fallback or settlement-policy rewrite.
3. R2 test iteration: reused the existing bounded slow stream, HTTP fixture and authorization helper. Actual Kestrel tests cover the five distinct WordAiService paths without a duplicate provider/route matrix. Caller-cancellation controls verify durable outcomes through new contexts.
4. R2 implementation iteration: reused the existing terminal denial type and all five existing service mappings. The two catch filters are deliberately local to the separate reservation/provider phases, preserving existing ledger-unavailable handling and guaranteed post-reservation settlement. Final manual diff review found no further useful simplification; no unrelated refactor or dependency upgrade.
5. Connection-timeout test iteration: retained the actual named-client handler using its test configuration hook; the only replaced behavior is local connection establishment and the test timer. Kept the distinct connect/body setup explicit rather than adding a multi-mode transport harness. Both share the established authorization and request helpers.
6. Connection-timeout implementation iteration: extended only the provider catch filter with the pinned runtime's direct inner timeout marker. Caller cancellation remains the first guard; no traversal of arbitrary exception chains, new exception type, retry, fallback, accounting relaxation or reservation-phase widening. Final diff review retained that minimal shape.

## Changed files and remaining gates

Six files changed: `src/Mavrylo.Services/Services/AiProviderTransport.cs`, `docs/ai-provider-spend.md`, `tests/AiSpendGuardTests.cs`, `tests/AiSpendRouteTests.cs`, `tests/AiProviderBoundaryTests.cs`, `tests/AiProviderTransportTests.cs`. No migrations, prices, budgets, authorization policy, profile gates or owner schemas changed.

D1=B remains no mandatory Owl login for mobile purchase/paid use; an account enables desktop. D3 and Tasks2/3/6 ownership, recovery, linking and shared-owner quotas remain pending. Authenticated device-key identity is not immutable owner proof. Live money amounts, D2 quotas and monetary reset policy remain unapproved. Unknown outcomes can still consume persistent slots and require separately approved evidence-based reconciliation. Strict unknown model/tier/usage controls remain intentional. Task7 awaits the requested fresh scoped review; the full feature and rollout remain incomplete.
