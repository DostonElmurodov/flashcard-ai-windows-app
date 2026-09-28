# Task 7 review fix round 1

Original implementer: /root/task7_provider_spend. Backend worktree `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, expected clean HEAD `414bc25cf76e5a433e872f71e0a172008475e010`. Read the complete final `task-7-review.md` and the independent probe source/logs before editing. Resolve only its verified P1 and P2, preserving all accepted safety controls.

## R1: Gemini documented omitted-zero usage

The reviewer reproduced a valid otherwise-complete successful Gemini usage record with prompt100/candidates10/total110, matching model and standard tier, but omitted cachedContentTokenCount/thoughtsTokenCount. Both are implicit-presence proto3 integer fields with protocol zero defaults. Current mandatory GetProperty reads make the successful attempt retain the full568536 and global pending slot; the next request at concurrency1 is denied. Explicit-zero controls settle99 each and release both slots.

Add genuine behavioral RED before the narrow correction. Normalize only these documented Gemini zero-capable components; continue requiring complete aggregate counts/model/tier and correct nonnegative, bounds, total consistency and checked arithmetic. Do not default missing entire usage/totals, unknown model/tier, malformed/negative values or OpenAI cache-write data to zero. Use real local guard tests for repeated omitted-zero and explicit-zero successes with exact99 charge, zero pending and subsequent admission. Preserve/add relevant negative-retention controls. Read reviewer sources, no need for live provider calls or ledger repair tooling.

## R2: Internal provider deadline response mapping

Reviewer actual Kestrel account word-detail request with Caller CancellationToken.None and fake stalled body returned500 without ai_translation_unavailable, one provider attempt/no alerts/full unknown reservation retained. Distinguish the shared transport's own deadline expiry from caller cancellation. Internal deadline remains terminal: no fallback or outage email, retain uncertain submitted reservation, map consistently through all WordAiService and Batch paths to503 ai_translation_unavailable. Caller cancellation must continue propagating as cancellation; never broadly re-enable fallback for OperationCanceledException.

Add genuine behavioral RED with an active caller token at actual HTTP/service boundary before changing product behavior. Verify status/body, provider attempts, alerts and retained persistent reservation. Cover both adapter/shared-boundary behavior as appropriate without unnecessary duplicated matrices; existing caller cancellation controls must remain meaningful.

## Process and evidence

- Only one implementation is active. Do not spawn agents, push, dispatch remote workflows, deploy, call real AI/Apple/email, or select numeric production budgets/operation quotas.
- Process-only OWL_TEST_POSTGRES uses existing local127.0.0.1:55440, userowl_tests/dbpostgres. Unique test DB ownership/drop only. No broad process cleanup; prior reviewer probe is complete.
- Genuine RED/GREEN for each behavior, manual simplify after each coding/test/fix iteration. Preserve raw logs and distinguish harness/compilation errors from product RED.
- Run appropriate focused Release provider/guard/transport/route/fallback tests including regressions and Release build. Prior full suite541/3knownownership/1skip is labelled at414bc25; do not relabel it. Rerun broad suite only if change scope or failures warrant it, and keep unresolved owner failures/skips explicit.
- Commit local reviewable changes, verify clean exactHEAD/diffcheck, update task-7-report.md and add task-7-fix1-report.md with changes, per-iteration simplify, exact commands/results and remaining gates. Root will independently inspect and dispatch a NEW clean-context GPT-6 Astra xhigh re-review.
- User reconfirmed D1=B: mobile purchase/use does not require account; account is for desktop. No owner/D2 design choices are authorized by these fixes.

## Controller R2 compatibility question during implementation
Current catch checks linked deadline.IsCancellationRequested. The production handler separately has10sec ConnectTimeout. Pinned .NET10.0.12 HttpConnectionPool.cs753-760 creates OperationCanceledException with inner TimeoutException for connect timeout, using its separate token. Source: https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/ConnectionPool/HttpConnectionPool.cs . With active caller and longer profile deadline this appears to retain the same500. Implementer asked for one bounded actual-handler/local reproduction (test-only shorter ConnectTimeout and stalled ConnectCallback can avoid remote traffic), genuine RED if confirmed, and a narrow terminal unavailable classification preserving caller cancellation and reservation. This is a source-derived question until tested, not an extra proven finding or permission for generic cancellation fallback.
