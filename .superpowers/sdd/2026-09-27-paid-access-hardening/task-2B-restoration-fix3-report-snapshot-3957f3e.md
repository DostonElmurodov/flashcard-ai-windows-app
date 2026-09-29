# Task 2B.2 restoration fix round 3 — N1 report

Scope: the single Minor N1 in `task-2B-restoration-fix2-review.md`. Backend checkout `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, branch `codex/paid-access-hardening`, starting clean SHA `900c9a50799adb3d991fc97a4adf86045b8c13a9`. No schema, client, quota, release, live Apple/provider or remote changes.

**Behavior corrected.** An existing purchase can have null `AppAccountToken` while its immutable server binding carries T1. With signed T2 and same-identity canonical T1, verify/restore and first/idempotent claim already returned 503 and persisted a conflict, but canonical upsert filled the null token field before signed T2 was observed. Both callers now record the signed token under their existing transaction and purchase lock **before** canonical upsert. This lets the existing conflict guard preserve null token metadata while the same transaction still commits newer canonical lifecycle evidence and the durable quarantine marker. Owner, ClaimedAt, binding, grant and device authority remain unchanged on denial. Matching signed/canonical T1 continues to fill null metadata normally.

PostgreSQL test matrix (`RestorationPostgresTests`):

| Evidence | Actual result |
|---|---|
| `n1-red.log/.trx` | 14 cases: four genuine failures, ten controls passed. The four failures are verify, restore, first claim and same-account claim with null stored token/T1 immutable binding, signed T2/canonical T1. Each returned 503 but saved T1 instead of null. |
| `n1-positive-baseline.log/.trx` | Four matching T1 positive controls passed before the product change: verify, restore, first claim, same-account claim. Each adopted T1 without conflict. |
| `n1-green.log/.trx` | 18/18 after the reorder. The opposite-direction conflict cases persist the expected T2 digest, newer revocation/event and unchanged null metadata; missing/former-token retries through fresh contexts remain 503 and preserve the first hash. Positive adoption remains 200. |
| `fix3-covering-release.log/.trx` | 69/69 across `RestorationPostgresTests`, `SharedSubscriptionPostgresTests` and `MobilePurchaseRestoreTests`, retaining R1–R3, fake-provider zero-call/healthy/independent-B controls, mismatched identity, and old R4 cases. |
| `fix3-full-release.log/.trx` | 730 total: **729 passed, 0 failed, 1 skipped**. The sole skip is existing B4 `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`, assigned to later Task 6A. |
| `fix3-release-build.log` | `dotnet build Mavrylo.slnx -c Release --no-restore`: **0 errors**, four inherited NU1510/NU1903 warnings. |

All test commands used process-only `OWL_TEST_POSTGRES='Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests'`, `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore`, the named filter or full suite, `--logger trx` and `--results-directory D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task2b-restoration-fix3-results`. Each PostgreSQL fixture uses its own `owl_test_GUID` database. No physical Apple device or network provider was used; fake Apple and AI evidence cannot establish StoreKit/App Attest compatibility.

Manual simplify after the sole coding iteration: moved the two existing `RecordTokenConflictAsync` calls ahead of their corresponding upserts, with no new helper, flag, schema or second transaction. Extended the existing PostgreSQL theories for the opposite disagreement direction and retries; one four-case theory supplies the normal metadata-adoption control. Self-review verified signed/canonical original ID, environment and product matching still precedes the mobile mismatch transaction; claim retains user-before-purchase lock and the post-upsert activity check; event ordering remains in `EntitlementService.UpsertAsync`; a failed path still commits the marker before returning 503. `git diff --check` found no errors. The current test/fake evidence covers the specified N1 state and does not claim a universal concurrency or release verdict.

Changed backend files: `src/Mavrylo.Services/Services/AccountEntitlementService.cs`, `src/Mavrylo.Services/Services/AnonymousPurchaseService.cs`, `tests/RestorationPostgresTests.cs`. R1–R3 and the closed fix1 M1–M3 remain their prior scoped findings; foundation carryovers, B4/Task 6A, physical Apple and operational rollout gates remain separate. No push or deployment occurred.

Final backend commit/source SHA: `3957f3e08c6317ff1495939e5c0cfbe7fea4f110`. Final backend checkout status: clean after local commit.
