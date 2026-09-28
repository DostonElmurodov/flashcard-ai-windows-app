# Task8 clean-context review context

Prepared by controller; do not treat this file or implementer report as an approval. Actual current-head Mac evidence is recorded below.

## Exact scope

iOS worktree D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios, branch codex/paid-access-hardening.
Base9096c1e3b20fb920726bd471ed4a092783ee3cc7; head4bb69cb20a82a14a4c0510bbefbadf5308f4bfae.
Diff package: task-8-review-4bb69cb.txt in this directory, contains commits/stat/full diff with10 lines of context. Requirements: task-8-brief.md and task-8-review-checklist.md. Claims: task-8-report.md. The brief's historical pre-dispatch/no-Mac notes are superseded by the exact actual run evidence below.

This is a task-scoped spec and quality review. Final three-repository feature review is separate. Read the diff once; inspect unchanged code for named concrete risks/call contracts, not a general crawl. No product edits, commits, push, workflow dispatch or subagents. Do not rerun broad suites; a narrow independent reproducer is justified only by a concrete doubt unanswered by available evidence. Write findings to task-8-review.md here and return spec verdict, quality verdict, actionable severity/path/line/scenario and unresolved evidence gates. Judge claims independently.

## Binding behavior

- iPhone purchase/use must not require Owl login; account enables desktop. Server anonymous-owner/recovery protocol is separate pending Tasks2/3. Task8 must neither add a login gate nor weaken existing linked-purchase ownership protection as a shortcut. This scoped task cannot claim B1/B2 are closed.
- Ordinary paid expiry retains all saved reading/review, forbids new AI/add/content edits, and preserves delete/export. Free permits global oldest-ten reading/editing and add below10 across languages. Expired trial/revoked retain first-ten reading/review without new mutations. Invalid/unknown cannot become mutable free or manufacture paid history from a link marker.
- Paid mutations require an accepted process-live response within5 minutes and finite unexpired entitlement. Persisted history permits reading only. Body/freshness must be coherent at actual SQLite guards, and every uncached AI path/staged save/import must recheck. Eligible cached content must remain available without provider work.
- All asynchronous sources must preserve newest applicable authority and account scope: account refresh, device refresh, JWS verify, direct AI402 and structured reconciliation503. Quota429/transient503 must not be interpreted as entitlement revocation. Ignored replies must not persist tokens/grants.
- accepted:false preserves complete user text/detail as visibly blocked with reason and lossless retry after access recovery. No new AI on retry; no active review of rejected records. Dashboard, visible review, grade persistence, reminders and edit sheet must agree with access policy. Stale confirmation asks for a check, without inventing expiry or requiring repurchase.
- Account-scoped read history survives A/logout/B/logout/A without guest/B leakage or restored mutation confirmation. Successful deletion forgets only the captured deleted account; failures and late identity changes must preserve current scope.
- Development/test mocks cannot weaken release defaults. PBX target membership and real Mac compile/test evidence are required. No actual Apple purchase, live AI, deployed migration or physical App Attest claim is authorized by these tests.

## Verification evidence

Results directory: D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task8-results.
Current-head unrestricted run36356755031 actually tested4bb69cb (source-head line111): successful build,599 cases=575passed20failed4skipped,30 assertions/0unexpected. All20 failures match the fixed-source baseline; no new failed cases. EntitlementMutationBoundaryTests33/33, SharedAccountSessionTests46/46 and ReviewSessionViewModelTests28/28 (107/107 included in the full run); PublicFlashcardSetAPIClientTests4/4 and WordRepositoryV10OwnershipTests58/58. No SQLite vnode-unlink warning, queued-owner teardown failure or preserved-hosted-DB warning. Artifacts ci-full-check9-run.json, ci-full-check9-full.log, ci-full-check9-cases.json and ci-full-check9-summary.json. No separate focused rerun at this SHA. Compiler warnings remain; inspect their actual source attribution, including new unused Bool Task.value results at SharedAccountSyncTests392/417. The unrestricted job is not GREEN because of the20 baseline failures/4existing skips.

Earlier unrestricted run36355777170 atfb69896 built598 cases:570passed24failed4skipped,40 assertions/1unexpected. Artifacts ci-full-red8-run.json, ci-full-red8-full.log, ci-full-red8-cases.json, ci-full-red8-summary.json. Three new behavioral RED cases covered test-mode row, A/B/A history and overlapping JWS verification; one older premium-import fixture used a historical clock incompatible with live confirmation. Four previously failing PublicAPI fakes passed. New fixes and SQLite cleanup in4bb require current-head results.

Fixed-source baseline run36355230240 checked out9096c1e (GitHub workflow head0279384 is different), same Xcode26.6/iPhone17Pro/iOS26.4.1:545cases=517passed24failed4skipped,34 assertions/4unexpected. Artifacts ci-baseline-full-run.json/full.log/cases.json. The20 hosted SwiftUI accessibility failures plus4 AccountView skips remain a separate Task11 harness gate; they are not waived for whole-feature acceptance. ci-full-red8-baseline-comparison.json records exact20 unchanged failures and4 API cases now passing. Source baseline attribution does not itself prove a harness root cause. Read ios-full-suite-preflight.md for evidence and external limits.

Historical initial39/39 at712bb1f is not current acceptance. Run36351623120 had a crashing test fixture, not clean behavioral evidence; one cached-positive test at2c3f110 had incorrect language data. Compiler failure at0279384 executed no tests. Keep assertions distinct from failed cases and exact commit labels intact.
