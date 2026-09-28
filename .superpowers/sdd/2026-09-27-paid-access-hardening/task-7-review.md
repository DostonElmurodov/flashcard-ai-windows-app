# Task 7 independent scoped review

Reviewed on 2026-09-27 America/Chicago (2026-09-28 UTC), as the separately dispatched clean-context GPT-6 Astra xhigh reviewer. This review covers provider-expenditure hardening only.

## Target and verdict

- Worktree: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`
- Base: `77e67f4886586e9e60c680717d696f3e09c11ab3`
- Reviewed head: `414bc25cf76e5a433e872f71e0a172008475e010`
- Independently verified HEAD and clean status before and after review. `git diff --check BASE..HEAD` passed. The supplied complete diff package matches the Git diff line-for-line: 29 changed files.
- **Spec verdict: needs two corrections before Task 7 acceptance.** The core persistent spending boundary meets the scoped design, but valid Gemini zero-usage omissions permanently consume global concurrency, and internal provider deadlines escape the established unavailable-response mapping.
- **Code-quality verdict: needs changes for the two findings below.** No additional actionable defect was established in the other reviewed paths. This is not a whole-feature or deployment approval.

## Actionable findings

### P1 — Normalize Gemini's protocol-defined zero components before retaining a successful attempt

**Location:** `src/Mavrylo.Services/Services/AiProviderTransport.cs:84-88`, particularly mandatory reads of `thoughtsTokenCount` at line 85 and `cachedContentTokenCount` at line 88. Consequence is enforced by `AiSpendGuard.cs:94` and its persistent pending-attempt checks.

**Trigger:** A successful `gemini-2.5-flash` response has the matching model and standard tier, a complete consistent aggregate usage record (`promptTokenCount=100`, `candidatesTokenCount=10`, `totalTokenCount=110`), and omits the two zero-valued fields `thoughtsTokenCount` and `cachedContentTokenCount`. Requests deliberately set `thinkingBudget=0`; uncached responses are also a supported ordinary case.

Google's published [GenerateContentResponse.UsageMetadata proto](https://raw.githubusercontent.com/googleapis/googleapis/master/google/ai/generativelanguage/v1beta/generative_service.proto) defines these components as plain proto3 `int32` without explicit presence (lines 573-592 as fetched). [ProtoJSON's presence/default rules](https://protobuf.dev/programming-guides/json/#presence-and-default-values) permit default-valued fields without presence to be omitted. The [current REST usage contract](https://ai.google.dev/api/generate-content#v1beta.UsageMetadata) documents these counters and total usage. Thus omitted zero components in an otherwise complete, consistent usage record are distinguishable from absent usage or unknown totals. This is contract evidence, not an observed live provider response. The public proto is behind the REST page in some other fields, including service-tier metadata; the reproduction explicitly supplies the current REST tier and does not infer a tier default from the older proto.

**Observed consequence:** `GetProperty` throws, `ReadUsage` returns null, and the usable response is still returned to the caller. Settlement retains the full reservation and its global pending slot. With a synthetic concurrency limit of 1, the next call is denied despite no active provider request and ample budget. With a larger limit, repeated ordinary successes eventually disable all new provider attempts, including OpenAI, until separately reviewed manual reconciliation. Restart does not clear these slots.

**Independent reproduction:** `task-7-review-probes/Task7ReviewProbes.cs` calls the exact existing Release Gemini adapter and real PostgreSQL guard. It uses a fake HTTP response, no remote provider:

```text
OMITTED_ZERO_SUCCESS calls=1 result=Hello charged=568536 settled=False pending=1
OMITTED_ZERO_NEXT reason=budget_or_concurrency calls=1
EXPLICIT_ZERO_CONTROL calls=2 eachCharged=99 settled=2 pending=0
```

The explicit-zero control differs only by adding the two `:0` components. Its charge is `ceil((100*540 + 10*4500)/1000) = 99` micro-USD. Existing settlement fixtures supply nonzero or explicit counters, so their green results do not exercise this valid wire representation.

**Expected correction:** Apply Gemini-specific documented default semantics only to these zero-capable components in a complete usage record, then keep the existing model/tier, required aggregate counts, nonnegative bounds, total consistency and checked-cost validation. Do not broadly default missing usage, missing totals, unknown models/tiers, malformed values, or missing OpenAI cache-write data to zero. Add both omitted-zero success and explicit-zero controls through the real guard, including proof that repeated successes release the concurrency slot. Preserve negative/inconsistent/missing-entire-usage retention tests. There is no need to weaken the conservative accounting policy or create a live repair tool.

### P2 — Map an internal provider deadline to the established unavailable response

**Location:** `src/Mavrylo.Services/Services/FallbackAiJsonService.cs:53-57` rethrows every `OperationCanceledException`, including cancellation from the transport's own deadline at `AiProviderTransport.cs:27`. `WordAiService.cs:87-94` (and the other three operations / Batch) handles spend/provider exceptions but not that internal-deadline case; the controllers and Program add no exception mapping for it.

**Trigger:** An authorized AI request has an active caller connection, the provider sends headers but stalls its body, and `AiSpend:DeadlineMilliseconds` expires. The linked internal token is canceled while the original request token remains active.

**Observed consequence:** The new terminal cancellation branch is correct about preventing fallback but also bypasses `WordAiService`'s `503 { error: "ai_translation_unavailable" }` contract. An actual local Kestrel request to `/owlai/account/ai/word-detail`, with `CancellationToken.None` on the caller, returned HTTP 500 without the established error code. This presents a normal provider timeout as an unhandled server failure and prevents callers from receiving the normal AI-unavailable response. The money reservation remains retained; this finding is response/error handling, not an unreserved-spend issue.

```text
DEADLINE_HTTP_ROUTE status=500 responseHasEstablishedCode=False calls=1 alerts=0 settled=False callerCancellation=None
```

The reproduction is in `DeadlineRoute` in the same standalone probe, using actual `ApiFactory.UseKestrel(0)`, trusted synthetic account/test-mode authentication, the real provider adapter/guard and local PostgreSQL, and only a fake stalled provider stream. `probe-with-route-final.log` is the successful final harness run. No real provider/email/Apple call was made.

**Expected correction:** Distinguish caller cancellation from the transport's own deadline expiry. Keep caller cancellation as cancellation; keep internal deadline expiry terminal with no fallback or outage alert, and retain any uncertain reservation. Translate the internal deadline into a deliberate exception/result that all WordAiService paths map to the established 503 code. Add a bounded route-level timeout regression with the caller token active, plus the existing client-cancellation controls. Do not fix this by re-enabling fallback for arbitrary `OperationCanceledException`.
## What was checked

**Authorization and call paths.** Both `Areas/OwlAI/Controllers/AiController.cs` and `AccountAiController.cs` expose analyze-word, word-detail, review-translation and extract-words. `WordAiService` and its single-word primary/secondary partial all flow through `FallbackAiJsonService` into the two registered provider adapters and the shared transport. A source search found no other OpenAI/Gemini HTTP call site. The accepted filters grant the scoped spend context after their existing authentication/proof and applicable entitlement/quota gates; authenticated Development test mode also receives context and still hits the guard. Account hints are independently authenticated in `DeviceContextService`/`SharedAccountAuthentication`. Cache returns precede provider invocation and create no provider reservation. Existing anonymous-purchase ownership flaws remain separate below.

**Persistence and arithmetic.** Reservation and completion use the same PostgreSQL transaction advisory lock. Bucket changes and reservation changes commit atomically; HTTP is outside the transaction. Global plus guest/account aggregate costs are cumulative, with no clock/identity reset. Amount subtraction before comparison avoids allowance overflow; monetary products and sums are checked. The stored rates/caps are frozen; completion is idempotent under the same lock. Unknown outcome and failed completion retain money and pending slots. The API chooses an internal reviewed profile and checks that the reservation equals its computed upper bound; synthetic constructors are inaccessible to ordinary external application assemblies.

**Provider cost assumptions.** Official OpenAI model, pricing, Responses and cache documentation and Google's model/pricing/usage pages were refreshed during this review. The reviewed full-input ceilings and nano-USD envelopes are conservative under the documented requested modalities/tier: OpenAI 1,050,000 input at maximum 550 nano/token plus output at 1650; Gemini 1,048,576 input at maximum 540 plus output at 4500. With the test-only output cap 512, upward rounding gives 578,345 and 568,536 micro-USD. OpenAI cache reads/writes are disjoint alternatives and reasoning is already within output; Gemini candidates plus thoughts form normalized output. These are usage-based ceilings, not invoice measurements. No live numeric budget was selected. Sources: [OpenAI model](https://developers.openai.com/api/docs/models/gpt-6-luna), [pricing](https://developers.openai.com/api/docs/pricing), [cache accounting](https://developers.openai.com/api/docs/guides/prompt-caching#monitor-cache-performance), [Responses](https://developers.openai.com/api/reference/cli/resources/responses/methods/create), [Gemini model](https://ai.google.dev/gemini-api/docs/models/gemini-2.5-flash), [Gemini pricing](https://ai.google.dev/gemini-api/docs/pricing#gemini-2.5-flash).

**HTTP and fallback.** Inspected the actual named-client registration and shared handler: exact HTTP/1.1, POST with non-null content, no Expect-Continue, redirects, proxy, automatic credentials, cookies, decompression or draining. The byte cap is incremental under ResponseHeadersRead; a linked cancellation deadline also covers body consumption. Provider 401/402/403/429, spend denials and cancellation terminate fallback; permitted failure fallback gets a separate reservation. I inspected the pinned [.NET 10.0.12 HTTP/1 send/retry condition](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpConnection.cs) and [pool retry handling](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/ConnectionPool/HttpConnectionPool.cs), plus the actual-handler loopback tests. This supports the reviewed configuration on that runtime; it is not proof for arbitrary future runtimes.

**Migrations and privacy.** The migration adds only the two spend tables, constraints and pending-state index. A complete comparison showed the new Designer's model body equals the snapshot, and removing the two new entities makes the snapshot exactly equal to the base snapshot. Existing tables are unchanged. New provider/spend log and alert paths use fixed categories, sanitized failure summaries and redacted named-client headers; adapter/Fallback exception objects containing provider text are no longer logged. The reviewed metrics, persistent pending counters, threshold events and reconciliation runbook expose retained outcomes without automatic forgiveness.

## Evidence executed by this reviewer

Product source, index and HEAD were not modified. Only this report and probe artifacts under the SDD directory were written. No product build, complete-suite rerun, push, remote workflow, live provider/Apple/email call or remote database connection was performed.

1. Git HEAD/status/diff checks and the complete diff/generated-model comparisons described above.
2. Compiled the standalone probe with the locally installed compiler, referencing the already-built Release assemblies; no restore or package download:
   `dotnet C:/Program Files/dotnet/sdk/10.0.401/Roslyn/bincore/csc.dll @D:/07 Hobby/FlashcardAI/.superpowers/sdd/2026-09-27-paid-access-hardening/task-7-review-probes/compile.rsp`
   The response file preserves the exact quoted paths/references. Compilation succeeded with one CS1702 assembly-reference compatibility warning for the existing Npgsql EF dependency; no errors. Both initial and final compiler logs are preserved. The final source added opening the PostgreSQL connection before reading its version; there was no failed product run or claimed behavioral RED from that probe-only adjustment.
3. Executed `dotnet <SDD>/task-7-review-probes/Task7ReviewProbes.dll`, with process-only `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`. The existing `PostgresContainerFixture` created and dropped its unique `owl_test_GUID` database. Runtime reported **.NET 10.0.12**, PostgreSQL **13.23**. Exit code 0 means all six diagnostic checks completed, including the reproductions of both defects; it does not mean the implementation passes review.

| Probe | Actual result |
| --- | --- |
| Valid Gemini omitted-zero usage | Reproduced permanent pending slot and next-call denial |
| Same response with explicit zeros | Two successful settlements at 99 micro-USD each; zero pending |
| Client cancellation after HTTP dispatch, real ledger, newly constructed DB context | Full 568,536 retained; pending 1; no false settlement |
| Unavailable ledger at loopback port 1 | Controlled `ledger_unavailable`; zero HTTP calls |
| Body deadline with fake transport/guard | One call, terminal cancellation, no alert, unknown completion |
| Actual Kestrel account word-detail route, internal body deadline, caller not canceled | Reproduced HTTP 500 without the established unavailable code; one attempt, no alert, retained reservation |

Raw source, compile response file and results are in `task-7-review-probes/`. `probe-results.log` preserves the original five checks; `probe-with-route-final.log` preserves the final six-check run. The final run was launched with the probe directory as cwd so the copied `MvcTestingAppManifest.json` supplied the existing test host content root; `Mavrylo.deps.json` was also copied from the existing Release output into the probe directory. Earlier route-harness attempts failed before reaching the endpoint because those standalone test-host files/content root were missing. A subsequent probe-only compilation failed because the two unhandled-error probe processes still held the probe DLL; only processes mapping that exact probe DLL were stopped, then compilation and execution completed. Those harness/setup/compile errors are preserved and are not product failures or behavioral RED. No product files were copied back or edited. These use synthetic limits and credentials, not production examples.

## Existing evidence inspected, not rerun by this reviewer

- `task-7-controller-focused.log` and controller TRX record the independent controller's Release **95/95** on the reviewed source. `task-7-release-focused-final.log` separately records the implementer's **95/95**.
- `task-7-release-build.log`: Release build succeeds, **0 errors**, 1 existing NU1510 warning.
- `task-7-release-full.log`: **545 total = 541 passed, 3 failed, 1 skipped**. Read the original failure messages and the unchanged `PaidAccessAuditRegressionTests` source. B1 UUID inheritance still gets `(200,1)` instead of `(429,0)`; the two B2 receipt-owner cases still get 200 instead of 403. These tests are unchanged in this diff. B4 shared-owner/key-rotation remains skipped and unresolved; none is waived by the spending guard.
- Read the provider/guard/route/transport/fallback test changes in the complete package, including the real-handler submitted-body disconnect, pooled disconnect, redirect, chunked oversize and stalled-body tests. Their saved successes supplement source inspection; my fake-response probes are not additional socket-retry proof.
- The saved startup migration test upgrades `20260926142908_AddIncrementalAccountSync` through actual API startup and preserves one seeded `Users` row. That is the exact recorded data-preservation test, not a production restore drill or proof that every historical row was checked. My probe separately applied current migrations to its fresh fixture database.
- Inspected RED/GREEN logs. The unconfigured calls, terminal statuses, missing payload caps, private-exception leakage, unknown provider and missing keys have genuine recorded behavioral failures. RED-5 also contains two fixture errors; those are not product RED. Reviewed the implementer's per-iteration manual simplify account, but intermediate source states were not individually committed, so the logs do not independently reconstruct every historical edit or prove every additional test had its own RED.

## Declined to judge / remaining limits

- Immutable anonymous purchase ownership, optional desktop linkage, key rotation/recovery and shared-owner operation quotas belong to Tasks 2/3/6. D1=B still requires iPhone purchase/use without mandatory Owl login; an account enables desktop. The current authenticated device reference is not immutable owner proof. The known B1/B2/B4 cases remain whole-feature gates.
- Numeric live money budgets, D2 operation quotas and any reset/renewal policy remain unapproved. Example spend configuration remains disabled/empty/zero. The cumulative machinery does not choose those product values.
- All-credentials-lost recovery and expiry UX/saved review across Windows/iOS are separate later integration concerns. This bounded backend review neither implements nor waives them.
- No live response frequency, live model access, deployed configuration/runtime, provider invoice, key-sharing outside this backend, production replica configuration, or provider-side adherence to published caps was measured. The model/profile/runtime assumptions must be revalidated before enabling and when they change; the floating container tag does not pin the reviewed runtime.
- Actual process killing and cross-host failures were not exercised. Existing tests simulate restart with new contexts/connections; the conservative persistence design and local tests were reviewed. Genuine unknown outcomes still intentionally retain concurrency and can stop availability; that policy is distinct from the documented valid-zero parsing defect.
- Strict model/tier and genuinely missing usage checks can still retain otherwise successful responses. This review does not authorize relaxing unknown-result accounting beyond the narrow protocol-supported correction above.

**Acceptance boundary:** Fix P1 and P2, retain the safety controls, add the indicated regression coverage and run focused verification plus a fresh scoped re-review. Whole-feature review across Windows/backend/iOS is still required later.

