# Task 7 fix round 1 — independent scoped review

Reviewed 2026-09-27 America/Chicago (2026-09-28 UTC), independently from the implementer. This review covers the two Task 7 corrections and their surrounding provider/ledger/service behavior, including the subsequent handler connection-timeout correction.

## Target and verdict

- Worktree: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`
- Original Task 7 base: `77e67f4886586e9e60c680717d696f3e09c11ab3`
- Fix base: `414bc25cf76e5a433e872f71e0a172008475e010`
- Reviewed HEAD: **`03bb0620098cdbe6d2c21b855271e2e8b968e029`**, including initial fix `2a3fbc70a5306b4a4b6eeb57498e043164f3b8f3` and the connection-timeout extension.
- **Specification verdict: approved for this scoped Task 7 fix review. R1 and R2 are resolved.**
- **Code-quality verdict: approved within this scope; no actionable new finding established.**
- This is not whole-feature, ownership, live-budget, deployment or release approval.

Read the full review context, original/fix briefs, dispatch context, original review, original/fix reports, and complete six-file fix patch. Inspected the actual final transport, both adapters, ledger and cost profile, fallback, all WordAiService provider call sites including Batch, controller mapping, named-client registration, relevant tests and test fixtures. The original review/report conclusions were treated as leads.

The supplied final patch matches the committed Git diff after line-ending/trailing-newline normalization. Independently verified all six source hashes and all 17 recorded artifact hashes in `task-7-fix1-evidence.json`; no mismatch. Both original-base-to-HEAD and fix-base-to-HEAD `git diff --check` checks passed. Source/index/HEAD were unchanged during this review.

## R1 / original P1 — resolved

**Relevant code:** `src/Mavrylo.Services/Services/AiProviderTransport.cs:84-104`; settlement validation at `AiSpendGuard.cs:114-129`; regression at `tests/AiSpendGuardTests.cs:54-126`.

Only absent Gemini `thoughtsTokenCount` and `cachedContentTokenCount` receive a zero default. A present value still passes through `GetInt64`; null, wrong-kind and invalid numeric values cannot use the absence default. The usage object, prompt/candidate/total counts, model and tier remain mandatory. OpenAI still requires `cache_write_tokens`.

I refreshed the exact official contract sources. The [Gemini proto schema](https://raw.githubusercontent.com/googleapis/googleapis/master/google/ai/generativelanguage/v1beta/generative_service.proto) declares the two counters as implicit-presence `int32` fields; the [ProtoJSON presence/default rules](https://protobuf.dev/programming-guides/json/#presence-and-default-values) support omission of default-valued fields without presence. The [current REST usage contract](https://ai.google.dev/api/generate-content#v1beta.UsageMetadata) separately documents the component and aggregate counts. This supports the narrow default; it does not establish that arbitrary missing usage or tier is known. The older public proto does not substitute for the REST tier contract.

The unchanged ledger independently requires matching model/tier, positive bounded input, nonnegative bounded output/reasoning/cache components, consistent totals, cache within input, checked arithmetic and cost within the frozen reservation. Candidate-plus-thought normalization remains checked and reasoning is billed once. Defaulting cache to zero uses the ordinary input rate, without inventing a cache discount.

My independent run passed both real-PostgreSQL repeated-success cases: omitted counters and explicit zeros each execute two adapter requests with concurrency 1, settle **99 micro-USD per request**, restore global pending to 0 and admit the second request. The calculation is `ceil((100*540 + 10*4500)/1000) = 99`; this is the configured conservative usage ceiling, not an invoice measurement. All 15 new unknown/invalid Gemini controls passed, retaining 568536 and the slot and denying the next attempt. The ten existing invalid-usage controls and OpenAI missing-cache-write control also passed. Present-null rejection is established by source inspection, not a newly added null-specific execution in this review.

## R2 / original P2 — resolved, including independent connect timer

**Relevant code:** `AiProviderTransport.cs:28-77`; terminal fallback at `FallbackAiJsonService.cs:53-57`; service mappings at `WordAiService.cs:45,89,133,216,225` and `WordAiService.Batch.cs:65`; actual HTTP regressions at `tests/AiSpendRouteTests.cs:18-91`.

Reservation-phase cancellation becomes `AiSpendDeniedException("provider_deadline")` only when the linked deadline is canceled and the caller remains active. Unrelated reservation cancellation still propagates. HTTP-phase classification additionally recognizes an `OperationCanceledException` with a direct inner `TimeoutException`, again only with an active caller. I refreshed the pinned [.NET 10.0.12 connection-pool source](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/ConnectionPool/HttpConnectionPool.cs): its connection timeout constructs exactly that shape at lines 753-760. This branch is confined to the HTTP phase and does not inspect arbitrary nested exception chains.

Both adapters use this shared transport. The terminal fallback branch rethrows the deliberate denial before another provider or email alert is attempted. All four operations and the Batch partial map it to `503 {"error":"ai_translation_unavailable"}`. Both controller surfaces return that same service result. Caller cancellation is excluded from the new catch filters and remains cancellation; arbitrary provider cancellation without either timeout signal is also still terminal cancellation.

The reservation remains before HTTP. The existing `finally` still performs separately bounded completion with unknown usage after cancellation/timeout; `TryCost(null)` retains both money and pending count. No timeout-based refund, TTL release, retry, additional fallback or logging of private exception details was introduced.

Independently executed evidence on the final source:

- Five actual local Kestrel body-timeout cases pass across analyze-word, word-detail, review-translation, extract-words and Batch, using both providers across the cases. They assert active caller (`CancellationToken.None`), status 503, exact error code, one provider request, no alert, one unsettled full reservation and one retained pending slot.
- Two actual Kestrel connection-timeout cases pass, one for each provider. They retain the Program named-client handler, assert its production 10-second setting, shorten only the test connection timer to 80 ms, and stall a local cancellation-aware ConnectCallback while the overall deadline is 5 seconds. Each asserts the same 503/error/retention boundary and one connection attempt. A connection attempt here is not a submitted provider body; no remote socket was opened by this callback.
- Both adapter body-deadline controls, the stalled-reservation/no-HTTP control, and the configured-handler stalled-body loopback case pass.
- Both post-dispatch caller-cancellation controls pass through the real ledger and verify retained full money/slot using newly constructed database contexts. Caller cancellation during connection and arbitrary provider cancellation with an active caller also pass without fallback or alerts.
- Existing actual-handler redirect, chunked oversize, posted-body disconnect and pooled-connection disconnect controls pass. These supplement the unchanged transport constraints; the connect callback itself is not retry/submitted-body proof.

## Verification executed by this reviewer

One bounded Release test run, without build or restore, from the backend worktree:

```powershell
$env:OWL_TEST_POSTGRES='Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests'
dotnet test tests/Mavrylo.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~GeminiProtocolZeroComponents|FullyQualifiedName~GeminiZeroDefaults|FullyQualifiedName~MissingCacheWriteUsage|FullyQualifiedName~InvalidUsageRetainsMoney|FullyQualifiedName~InternalDeadlineReturnsUnavailable|FullyQualifiedName~HandlerConnectDeadline|FullyQualifiedName~SlowBodyHonorsFullDeadline|FullyQualifiedName~ReservationDeadlineIsTerminal|FullyQualifiedName~AiProviderTransportTests|FullyQualifiedName~CallerCancellationAfterDispatch|FullyQualifiedName~ProviderCancellationIsTerminal' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=task7-fix1-reviewer.trx' --results-directory 'D:\07 Hobby\FlashcardAI\.superpowers\sdd\2026-09-27-paid-access-hardening\task-7-fix1-reviewer-results'
```

**Result: 47 executed, 47 passed, 0 failed, 0 skipped; process exit 0.** Runtime reported .NET 10.0.12. Read the complete normal log and independently parsed the TRX counters. Output: `task-7-fix1-reviewer-focused.log` and `task-7-fix1-reviewer-results/task7-fix1-reviewer.trx` under this SDD directory.

Inspected the fixture before execution: the process-only PostgreSQL setting causes fresh `owl_test_GUID` databases and cleanup of only each fixture's exact generated name. No shared application database was selected. No product source edits, build, new product tests, commits, pushes, live provider/Apple/email calls, remote database calls, remote workflow dispatch or additional agents were used. Only this report and reviewer result artifacts were written under SDD.

## Existing evidence inspected, not re-executed

Read all three raw RED logs and the corresponding GREEN logs. Their failures are product assertions after successful build/test startup, not harness or compiler failures:

| Stage | Raw result and interpretation |
| --- | --- |
| R1 RED | 16 passed / 1 failed: omitted-zero success retained 568536 and was not settled; explicit zero passed. |
| R1 GREEN | 28/28, including existing invalid-usage and missing OpenAI cache-write controls. |
| R2 RED | 6 passed / 9 failed: five actual HTTP 500 responses plus four deadline exception-type failures; caller-cancellation controls passed. |
| R2 GREEN | 15/15. |
| Connect RED | 1 passed / 2 failed: both provider routes returned 500; caller cancellation passed. |
| Connect GREEN | 13/13. |

Checked transport sections of every stage source patch: R1 RED has no parser fix; R2 RED contains the R1 correction but no deadline catch; connect RED adds tests against the first fix with no connect-shape correction. Corresponding GREEN snapshots add the intended narrow behavior. These intermediate snapshots are patch evidence against the stated bases, not separately committed stages or my own RED replay.

The implementer's per-iteration simplify account matches the final small implementation: a Gemini-only component reader, two localized catch filters using the existing terminal exception, and existing service/error mappings. The test additions exercise observable ledger and HTTP outcomes. No unrelated abstraction, migration, dependency, pricing, quota or authorization change appears in the fix diff.

Final implementer focused-log summary records **123/123**; final Release build log records **0 errors, 1 existing NU1510 warning**. Controller log and independently parsed TRX record **123 executed/passed, 0 failed/skipped**. Those are inspected existing executions, distinct from my 47-test run. The earlier 120-test/build checkpoint belongs to `2a3fbc7`, not the final connection fix.

Read the failure/skip blocks and totals in the historical full-suite log: **541 passed / 3 failed / 1 skipped out of 545 at `414bc25`**. B1 still observed `(200,1)` instead of `(429,0)`; both B2 cases observed 200 instead of 403; B4 remained skipped. This is not a full-suite result on `03bb062`, and those gates are not waived. No concrete regression concern warranted a broad suite rerun for these two bounded corrections.

## Findings and remaining limits

**No new actionable findings.** R1 and R2 can be closed for this scoped review; the fix does not require another correction round based on this evidence.

Immutable anonymous purchase ownership, optional desktop linkage, recovery/key rotation and shared-owner operation quotas remain Tasks 2/3/6. D1=B still means mobile purchase/use without a mandatory Owl account; an account enables desktop. Authenticated device-key context is not immutable purchase-owner proof. B1/B2 failures and the B4 skip remain whole-feature gates.

Live D2 operation numbers, monetary allowances and reset/renewal policy remain unapproved; no example defaults were enabled by these changes. Genuine uncertain outcomes deliberately retain money and slots and can exhaust availability until separately authorized evidence-based reconciliation. Strict unknown model/tier/usage accounting remains intact.

This review used synthetic responses, local sockets and local test databases. It did not measure real provider response frequency, invoices, deployed runtime/configuration, external key sharing, live concurrent replicas, or recovery after killing processes. The restart controls use new contexts/connections. The connection exception classification is tied to the verified runtime source and must be revalidated when the runtime/handler changes. No Windows/iOS integration or whole-feature acceptance is implied; the later fresh cross-platform review remains required.
