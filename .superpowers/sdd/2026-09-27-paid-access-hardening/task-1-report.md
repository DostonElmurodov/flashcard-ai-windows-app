# Task 1 report — DONE_WITH_CONCERNS (partial policy/coverage)

Local backend commit: `68cbce3` — `test: define safe paid access audit regressions and policy baseline`. No push.

## Scope and preflight

Only backend worktree `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, branch `codex/paid-access-hardening`, starting HEAD `473a39bee299f3df4c7fdcf6f45502f880a9bd8b`, was edited. Starting worktree was clean. Parent ledger records source copies clean (Windows has only pre-existing untracked audit/plan artifacts); no source repository or audit snapshot was edited. iOS worktree remains clean at `9096c1e3b20fb920726bd471ed4a092783ee3cc7`.

Files created:
- Backend `docs/paid-access-policy.md`: D1=B (iOS purchase/paid use without mandatory account; desktop account required), approved D3 retention, pending D2, pending anonymous ownership/recovery mechanism, state/operation/platform and denial matrices.
- Backend `tests/PaidAccessAuditRegressionTests.cs`: 18 scenarios, safe expectations and positive controls, explicitly skipped anonymous B4 future contract.
- This controller report. Test artifacts are outside the backend checkout in `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task1-results` and are not committed.

No product implementation, migration, real AI, real purchases, production writes, push or deployment occurred. Apple verification/canonical status is a synthetic signed-result fake, not signature-validation coverage. B3 uses a counting fake AI provider; other AI gates count the synthetic action boundary as one provider attempt. SQLite is in-memory. PostgreSQL is the parent's freshly initialized isolated local cluster on port 55440; the existing fixture creates/drops a fresh `owl_test_<guid>` database per fixture.

## Commands and results

- Before any new tests: `dotnet test tests/Mavrylo.Tests.csproj --logger "trx;LogFileName=task1-baseline.trx" --results-directory artifacts/task1`: **308 passed, zero failed**, verified by parent from TRX. Initial sandbox attempt could not read installed NuGet.Config; approved escalated execution succeeded. Evidence moved to `task1-results/baseline-artifacts/task1/task1-baseline.trx`.
- After iteration 1: `OWL_TEST_POSTGRES` set to the isolated local test server, then `dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter "FullyQualifiedName~PaidAccess" --logger "trx;LogFileName=task1-regressions.trx" --results-directory <task1-results>`: **6 expected failures, 9 passed, 2 skipped, 17 total**. This was before parent's request for two design-independent copied-receipt regressions.
- After iteration 2, final full suite: same isolated `OWL_TEST_POSTGRES`, `dotnet test tests/Mavrylo.Tests.csproj --no-restore --logger "trx;LogFileName=task1-full.trx" --results-directory <task1-results>`: **8 expected failures, 317 passed, 1 skipped, 326 total**, exit 1 intentionally. Existing 308 tests remain green; 9 new controls pass. Evidence `task1-results/task1-full.trx`.
- `git diff --check`: clean. Final review found only the two requested backend files to commit.

Working commands for the three builds:
- Backend: `dotnet build Mavrylo.csproj`; tests above compile the full backend and tests successfully on .NET 10.
- Windows: `npm run typecheck`, `npm run build`, `npm test` in the Windows worktree. Parent reports baseline **86/86**, typecheck and build successful, with TEMP/TMP directed to the isolated task directory. This task did not rerun Windows.
- iOS: repository workflow `.github/workflows/ios-validation.yml` uses `xcodebuild test -project FlashCardAI.xcodeproj -scheme FlashCardAI -destination "platform=iOS Simulator,id=<available simulator>" -parallel-testing-enabled NO ... CODE_SIGNING_ALLOWED=NO`. It requires macOS/Xcode; it was inspected, not run on Windows. No physical-device acceptance claim.

Existing restore/build warnings: NU1510 redundant System.Formats.Cbor; NU1903 SQLitePCLRaw.lib.e_sqlite3 2.1.11 and SSH.NET 2025.1.0 advisories. These were baseline dependency warnings, not introduced by these tests.

## Exact expected failures (final run)

| Scenario | Safe expected | Baseline actual |
|---|---|---|
| B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid | New attestation valid; AI `(429, providerCalls=0)` under zero free quota | `(200, providerCalls=1)` |
| B2_DeviceVerifyRejectsDifferentCanonicalPurchaseWithoutPersistingIt | 503, no unrelated purchase inserted | 200 |
| B2_CopiedReceiptCannotTransferExistingGuestPurchaseToUnrelatedDevice | 403; keep original device owner; no paid AI for unrelated caller | 200 |
| B2_CopiedReceiptCannotFirstClaimDesktopOwnershipWithoutPriorOwnerProof | 403; guest purchase stays unclaimed, original device retained | 200 |
| B3_CaseInsensitiveBindingCannotBypassTenWordReservation, `Word` | Eleventh request 402; no eleventh provider call | Eleventh request 200 (status assertion fails before provider-count assertion) |
| B5_CachedNonProductionPurchaseCannotGrantProductionAi, Sandbox | `(402, providerCalls=0)` | `(200, providerCalls=1)` |
| Same B5, LocalTesting | `(402, providerCalls=0)` | `(200, providerCalls=1)` |
| B6_RefundCannotAcknowledgeSuccessWhileUpdatingUnrelatedPurchase | 503 to retry; unrelated purchase not inserted | 200 |

These failures are assertions against real service/filter/HTTP behavior, not missing dependencies or errors. B2/B6 denial tests assert no persistence after denial; these later assertions are reached once status is fixed. They intentionally do not mechanically apply historical refund over a newer matching canonical transaction.

## Passing positive/negative controls (9)

- Same-device signed-result purchase and repeated restore without an account: both 200, one persisted purchase, AI action `(200,1)` even with synthetic free quota zero.
- Already owned account purchase: other account claim 409, owner unchanged.
- B4 independently authenticated account subject: quota 1 consumed by first consumer; second consumer denied; different authenticated account independent. Synthetic test value is not D2 approval.
- B3 lowercase `word`: first ten provider calls and reservations allowed; eleventh 402, total providerCalls/reservations/usage all ten.
- Matching canonical notification state: active canonical subscription remains premium despite historical REFUND; matching canonical revocation becomes revoked (2 cases).
- Expired-paid/revoked/exhausted-free AI: respectively 402/402/429, each zero action/provider attempts (3 cases).

## Simplify and self-review log

- Iteration 1, before focused checks: reused existing `TestDb`, `TestConfig`, `FakeAppStoreServerClient`, `FakeAppAttestVerifier`, `FakeEnvironment` and `ApiFactory`; extracted only common purchase/device/service/filter setup. No new fixture, production hooks, external clients, UUID subject merging, duplicate quota implementation, or broad redesign. Kept HTTP test only for actual JSON binder divergence; lightweight filter checks for access gates. Manual equivalent of requested simplify pass checked reuse, duplication, unnecessary complexity and efficiency; no installed simplify tool exists.
- Iteration 2, before full checks: added only two bounded B2 tests using existing helpers, removed the B2 empty skipped placeholder, and updated one policy row. Confirmed B4 placeholder remains skipped with explicit reason rather than a false security assertion. Rechecked resource disposal, unchanged fake-only network boundary, assertions of retained owner and no persistence, D1=B compatibility, and no invented D2 amounts. No additional helper extraction would improve the two independently meaningful cases. Full suite then produced only the eight expected new red assertions.
- Final self-review: policy distinguishes saved-card review from AI enrichment; preserves 409; matching canonical lifecycle controls prevent unsafe blanket audit inversion. All newly created files compile, diff whitespace check passes, source snapshots unchanged. No subagents/reviewers spawned; parent performs independent review.

## Concerns / remaining work

Task 1 is **partial / DONE_WITH_CONCERNS**, not overall hardening complete. D2 values, monetary spend settings, free edit/retention details and independent anonymous ownership/recovery design remain pending. B4 `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget` is explicitly skipped until a real proven-subject or global guard API exists; matching client UUIDs must never be used as proof. Copied-receipt denial invariants are design-independent, but legitimate cross-device anonymous restore and desktop link positive controls need the chosen proof contract. The matrix marks unresolved policy rather than inventing defaults. All initial red regressions must become green in later tasks and final full verification.
