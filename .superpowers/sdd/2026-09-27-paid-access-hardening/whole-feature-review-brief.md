# Whole-feature independent review brief

Prepared while Task10B is in progress. **Do not dispatch against moving source.** Root must supply final current revisions, uncommitted-source manifests and actual final verification at dispatch. This is a fresh GPT-6 Astra Medium review of all three repositories, not another review limited to the last patch. Do not inherit the implementer's conversation or treat prior review verdicts as proof.

## Requirements and scope

Read the original plan `docs/superpowers/plans/2026-09-27-paid-access-hardening.md`, then its approved replacement ownership/restoration contract, restored-card policy and current final-review carryover under this SDD directory. User decisions supersede historical account-required/xhigh/pending-policy text:

- iPhone purchases and ordinary Apple restore require no Owl login or surviving original App Attest key. Desktop needs an account. Full verified Apple transaction plus app-transaction evidence can grant finite mobile access, not first desktop ownership or private library access. Preserve the documented full-envelope replay/lost-credential limitations rather than claiming impossible person-level identity.
- App Attest proves an installation/request; a client UUID, copied receipt, historical purchase row or marker does not prove purchase ownership. New installations receive server-owned identity; exact-key resume preserves its library and key/counter state.
- Previously paid saved content remains readable/reviewable after expiry. New AI, additions and content edits require appropriate current access; delete/export remain available. Keep history, sync cursors/tombstones and offline pending content.
- Free card allowance is ten within the authenticated library across languages/collections. Exact byte identity and original finite creation time determine first-ten ordering. Unknown metadata is not evidence of historical content or payment; materialization consumes capacity under the mutation lock, including ID-less reservations. Metadata alone invokes no AI and creates no operation charge.
- Authenticated account requests share canonical operation quota across clients; proven mobile purchase subjects share their appropriate quota across installation keys. Logout/rekey/language or endpoint choice cannot bypass it. Preserve unresolved historical paid-usage readiness; new proof does not invent missing old spending data.
- Every actual provider attempt, including fallback/retry, needs an atomic monetary reserve. Authorization/quota/budget/cancellation denial must not trigger fallback. Real commercial daily/minute limits and monetary budgets remain unapproved; missing settings fail closed. GitHub CI spending is a separate authorization.
- A known exact-device historical restriction without applicable owner/mobile proof or independently selected active account must not become fresh free commercial access. Recoverable 503 comes after existing proof/input validation and before quota/provider. Bootstrap and Restore stay usable. Marker does not fabricate `WasEverPaid` or owner, and is not destructively cleared.

## Repository boundaries

| Repository | Actual checkout | Feature base |
| --- | --- | --- |
| Backend | `D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\backend` | `473a39bee299f3df4c7fdcf6f45502f880a9bd8b` |
| iOS | `D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\ios` | `9096c1e3b20fb920726bd471ed4a092783ee3cc7` |
| Windows | `C:\Users\ForDo\.codex\worktrees\paid-access-hardening\FlashcardAI` | `aba59fb8f5e829d2cd10065fd702cff2dc7223b3` |

The root checkout is the documentation workspace. Its `backend` and `ios` siblings are original source checkouts, **not** the worktrees above. Do not review HEAD alone when root supplies accepted but uncommitted source. Never modify any product checkout during review.

## Review method

Independently trace actual request/data paths and cross-repository contracts. Prior task reports may locate evidence, but their PASS labels are not a substitute for inspecting current code and tests. Check:

1. Purchase context, Apple validation/canonical refresh, owner binding, finite restore, claim/deletion/revocation/expiry/outage, request/authority ordering and absence of UUID-derived authority.
2. Device JWT and raw proof binding, challenge/counter rollback boundaries, same-key bootstrap/races, stable HTTP status/code behavior and recovery routes.
3. Shared quota selection/atomicity/UTC/restarts, historical migration inventory/readiness, provider reserve/settle/fallback/timeouts and all alternate AI routes.
4. Library privacy, exact IDs and byte ordering, metadata completeness/revision/retries, reservation/save/delete concurrency, local queued snapshot checks and stale acknowledgment prevention.
5. iOS and Windows paid mutation confirmation, account logout/cold start, saved-empty/pending text/retry and retained read/review, source-only or unexecuted client paths clearly separated from runtime proof.
6. Populated old-schema migration through two actual host startups; exact retained fields, no invented owner/credential/grant, no deletion to satisfy constraints, and real HTTP positive/negative controls.
7. Release/debug/static-bearer/test-mode separation and offline UI fixture isolation, non-publishing validation workflows, source-only SwiftPM cache boundaries and exact candidate linkage to evidence.

Inspect deferred items in `final-review-carryover.md`, including field-isolation coverage and inherited warnings; explicitly disposition them. Do not invent a product bypass from a coverage limitation. Do not silently disregard an actual failed run or claim a later source fix has executed when it has not.

## Constraints and output

No source changes, new agents, commits, pushes, workflow dispatches, production queries/migrations, live Apple/provider requests or permission/financial changes. Do not rerun unchanged broad suites; inspect root's frozen raw evidence and ask for a narrow missing proof only if material.

Write `whole-feature-review.md` with actionable severity, exact file/line, trigger and consequence, plus spec compliance, evidence reviewed, unresolved gates and a clear verdict. Separate current source acceptance, automated runtime acceptance and external release readiness. Report substantial findings promptly so root can coordinate one writer. No release-ready conclusion without the final current Mac full/Release evidence; physical Apple/production gates remain explicit even if local automated checks pass.
