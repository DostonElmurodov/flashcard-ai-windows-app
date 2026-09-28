# Task7 independent scoped review handoff

Review exact committed source below with clean context on GPT-6 Astra xhigh. Read source and tests independently; implementer/controller reports are evidence leads, not conclusions.

## Scope and source

Backend worktree: D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend
Task base:77e67f4886586e9e60c680717d696f3e09c11ab3
Task final head:414bc25cf76e5a433e872f71e0a172008475e010; root verified clean checkout and diff check.
SDD: D:/07 Hobby/FlashcardAI/.superpowers/sdd/2026-09-27-paid-access-hardening
Requirements: task-7-brief.md, task-7-dispatch-context.md, provider-guard-preflight.md. Final implementer report: task-7-report.md. Complete diff package: task-7-review-414bc25.txt.

User requirements: iPhone purchase/use has no mandatory Owl login; account enables desktop. Expired paid purchases retain saved viewing/review, with no new AI/add/content edit; delete/export allowed. Numeric user-operation quotas, live money budget and all-credentials-lost recovery remain unapproved. This bounded task must not silently settle those product choices or implement a substitute purchase-owner schema.

## Expected boundary

Every actual backend OpenAI/Gemini attempt, including fallback, requires its own persisted atomic reservation. Supported operations: analyze-word, word-detail, review-translation, extract-words; trace all exposed controllers and batch/single-word multi-language paths to those adapters. Authorized cached responses issue zero new provider HTTP. Only accepted authorization filters can establish the trusted spend subject. TestMode in trusted Development cannot bypass monetary protection. Raw client UUID/account hints do not authorize expenditure.

Global plus guest/account aggregate allowances are cumulative with no automatic reset. Immutable owner/shared-operation-quota integration is separately pending Tasks2/3/6. Verify concurrent replicas, restart, duplicate completion, monetary upper bounds, checked overflow, denied/unconfigured paths, cancellation, unavailable ledger and unknown results. Unknown/out-of-range/inconsistent/mismatched usage retains reservation; timeout/crash is not zero cost. No automatic unknown concurrency release based only on age; require visible counters and reconciliation guidance. Verify additive migration on an existing schema and exact data-preservation evidence.

A conservative validated-usage price ceiling is allowed; exact invoice accounting is not required. Immutable reviewed rate/cap profile and explicit output/input/image/body/deadline/concurrency limits must justify the reservation. Inspect integer units/upward rounding and cached/reasoning/thinking semantics for both APIs. Live profile/budgets stay unset or zero; synthetic tests cannot be copied into live config by default. Official source pointers and dated assumptions must be preserved; no claim about outside services using the same keys.

Provider transport must enforce incremental response caps and a deadline including the response body. Redirects/auth negotiation/retries must not produce unreserved attempts or leak credentials. Inspect actual named-client DI, not just fake handlers, and real loopback POST/disconnect/redirect/body-timeout tests. Exact HTTP/1.1/non-null POST/no Expect/proxy/redirect is the current candidate based on pinned .NET10.0.12 source; report runtime-upgrade revalidation limits. Authorization/quota/spend denials and cancellation are terminal; permitted provider failure fallback reserves separately.

No raw card text, JWT/JWS, API keys or provider error bodies in metrics/logs/alerts. Count attempts/reservations/settlement/denial/fallback and threshold warning safely. Inspect exception objects as well as message templates. Alerts use capturing fakes; no email/provider calls/live secrets during validation.

## Current committed-source evidence

Preserve exact command, source head, configuration, PostgreSQL/runtime and test totals. Distinguish behavioral RED from compiler/environment failure. Every coding/fix iteration needs manual simplify evidence. Re-run focused tests independently where useful; do not conflict with active implementation/builds.

Before this task, accepted backend77e67f4 had scoped Task10 Release13/13 and a prior broader run462pass/3known ownership fail/1B4skip. Those unresolved ownership cases remain visible and are not waivers for eventual whole-feature completion. Fresh whole-feature review across W/B/I remains required after later integration; this review can accept only Task7 scope.

## Reporting

Write task-7-review.md with spec/quality verdicts and only actionable findings, priorities, file/line, concrete trigger/consequence and minimal expected correction. Report tests actually run separately from saved logs inspected, exact reviewed head and any limitations. Do not edit product files, commit, push, dispatch backend CI, contact external providers/Apple or spawn agents. The existing backend manual CI publishes/deploys and is prohibited for validation. Local synthetic PostgreSQL test server127.0.0.1:55440, userowl_tests/databasepostgres via OWL_TEST_POSTGRES is available; fixtures create/drop their own unique databases only.


Final implementation evidence at414bc25: task-7-release-focused-final.log95/95; task-7-release-build.log0errors; task-7-release-full.log545total=541pass/3unchangedB1-B2fail/1existingB4skip. No source edits between these runs and commit per report. Root independently reran focused Release95/95 with --no-build --no-restore on clean414bc25; task-7-controller-focused.log and task-7-controller-results/task7-controller.trx. No active implementation/build/test runs remain; useful bounded read-only probes are allowed. Root directly compared all three failed method names with task-10-release-full.log: same B1/B2 cases. These counts are evidence, not an accepted verdict. Review original tests/raw failures and exact code independently. No new subagents or complete-suite rerun needed unless a concrete unresolved concern justifies it.
