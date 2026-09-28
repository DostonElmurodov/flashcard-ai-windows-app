# Task12 release-evidence preparation (not dispatched)

Read task-12-brief.md, backend docs/paid-access-rollout.md and docs/ai-provider-spend.md on the eventual final branch. This handoff prepares a concrete checklist; it does not authorize publishing, live inventory/DB changes, real purchases, provider spending or deployment. A future fresh implementer can create the requested release checklist locally once the integrated owner/quota contract is known. Do not mark physical/environment checks as passed from simulator or synthetic evidence.

## Current verified evidence anchors, not future results

- B03bb0620098cdbe6d2c21b855271e2e8b968e029: Task7 final focusedRelease123/123 (implementer and controller), fresh reviewer47/47 and spec/quality approved, build0errors. Historical full545=541pass3B1/B2fail1B4skip is414bc25, not the final fix head.
- W43ba1668a4205882b8565fa0720e7fa3fb97d67c: accepted access policy and full112/112. Later Task11 copy/CI changes will need their own labels.
- I58dd623c4d63921e8cdd720741c59a0b86526646: fullMac36360459409 has610=586pass20baselineUIfail4skip; access118/118 and V10Ownership58/58. Task11 proper XCUI harness is now being implemented; no new UI or Release-isolation proof yet.
- Task4 offline Apple signature fixtures, Task10 synthetic inventory/startup/runtime tests, Task7 local PostgreSQL/HTTP accounting, and future UI fakes are separate evidence categories. None proves a real App Attest key, physical StoreKit recovery, production data migration or provider invoice.

## Checklist shape

Record exact repositorySHA, installed version/build, artifact/digest, backend instance/schema/config version, device/OS, Apple environment, scenario, expected/actual outcome, timestamp and evidence pointer. Pending must be explicit; do not supply invented actual results. Keep raw purchase/account identifiers and secrets out of public reports. Link a controlled private evidence register when actual authorized data is eventually needed.

Trace no-account iPhone purchase/use, owner-authenticated second device/reinstall, all-credentials-lost selected policy, optional desktop link, logout/account switch/purchase completion, account deletion/tombstone, free9/10/11global words, cross-language caps, paid-expiry retained saved review and mutation denial, rejected-card deletion/persistence, cancellation/renewal/grace/refund and competing original IDs. Distinguish cached saved review from new AI. Family Sharing/mixed Apple accounts need explicit applicability based on actual product configuration; do not invent support or prohibition.

For expenditure record application operation, cache hit, adapter attempt/connection and submitted HTTP separately where observable. A local stalled ConnectCallback opens no remote socket; provider_attempt is not invoice proof. Pre-AI denials require0new provider requests; permitted fallback has its own reserve. Body/connection timeout returns503 with an active caller and retains unknown spend/pending slot; caller cancellation remains cancellation. New-context tests are not actual process-kill/restart drills. Retained slots have no automatic TTL refund; reconciliation needs evidence and separate authority. Exercise the new-call kill switch without reviving unsafe entitlement bypasses.

## Configuration / distribution gates

Both checked-in iOS Debug and Release currently resolve to production API; App Attest development/production mode is independent of Apple purchase Sandbox/Production. A changed Debug URL alone does not prove TestFlight/submission/App Review routing. Define and verify the actual signed distribution path to an isolated signed-Sandbox backend/database/budget, with TestModefalse and real proof requirements, before actual Apple acceptance. Do not trust a client header to select server purchase environment.

The example provider-spend configuration is deliberately disabled/zero/profile-empty. Numeric D2 operations and live cumulative money allowances/reset policy remain unapproved. Guest/account spend pools mean authenticated request context, not free/paid commercial status: paid iPhone users without Owl login are in guest. Do not accidentally allocate only a free-user budget to all anonymous paid subscribers or require mobile login to escape it. Backend Task7 guards stay active even in authenticated Development test mode.

Before release verify every deployed API instance's exact image/config and TestModefalse, credentials/model/profile/runtime assumptions and compatible client/backendSHA; one public flag/health response is insufficient. Preserve purchases, history, claims/tombstones and counters through a proven restored-copy migration and explicit legacy recovery path. Existing backend manual api-ci-cd publishes/deploys; do not dispatch it for validation. Future Task11 safe test YAML does not itself authorize any remote push, deployment or publication.

Still-open owner/recovery/operation-quota decisions and B1/B2/B4 tests must close before whole-feature acceptance. Obtain the separate final clean-context GPT-6 Astra xhigh cross-repository review after integration and repairs. Only a concrete checked release candidate and rollback/new-AI-disable procedure can go to the user's separate publication decision.
