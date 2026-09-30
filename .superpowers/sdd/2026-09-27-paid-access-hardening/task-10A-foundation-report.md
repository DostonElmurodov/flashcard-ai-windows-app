# Task 10A foundation coverage report

## Scope and base

- Backend: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, branch `codex/paid-access-hardening`, clean starting HEAD `3957f3e08c6317ff1495939e5c0cfbe7fea4f110`.
- Isolated PostgreSQL: process-only `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`; the existing fixture created and dropped per-fixture `owl_test_*` databases. No production database, live Apple, or live AI call was used.
- Scope: sections 2, 3, 4, and 6 of `task-10-local-closure-preparation.md`. No schema, quota, word-order, historical-marker, Tasks 6A/6B, iOS, or production configuration change.

## Changes and evidence classification

| Proof | Result | Classification |
| --- | --- | --- |
| Registration collision | Test intercepts both empty device-key query results before either insert. Two calls return 200, one unique insert fails, and the only new owner ID is the winner's. Retry and conflicting key material preserve owner, library word, UUID, public key, environment, and advanced counter; conflict returns 409. | Coverage-only. The first test attempt counted the winner reread as a third absent read; the test interceptor was corrected to count only readers with no rows. This was a fixture assertion failure, not a product defect. |
| HTTP resume | Real HTTP with a verifier checking stored key bytes, assertion bytes, and independently computed nonce/body/path/challenge hash. Zero-byte and `{}` requests succeed and return a device JWT for the stored key with `ent=free`, no owner claim, and `Cache-Control: no-store`. Wrong known key, challenge path, body hash, and replay deny; unknown key is 401. Nonempty/authority/null/malformed bodies return 400; 16,001-byte known and streamed requests return 413, while 16,000 whitespace bytes reach the empty-object check and return 400. Owner, key, library word, UUID, environment, and counter persist. | Coverage-only. The first combined run used an absolute owner count in a shared fixture and failed when another test had already inserted an owner; corrected to owner delta. This was a fixture assertion failure, not a product defect. |
| Proof vs. business rollback | Filter/action harness consumes a real PostgreSQL challenge and advances the counter, then writes a device word and rolls back its business transaction. A fresh `AppDbContext` sees consumed challenge and counter 5 with no word; replay and a fresh challenge with stale counter return 403. A fresh challenge with counter 6 reaches the action once; replay returns 403. | Coverage-only; first focused run passed. This uses a fresh context rather than a restarted HTTP host. |
| Register/assertion-challenge wire validation | Actual HTTP theory covers missing/null key and path fields, missing/null registration proof fields, JSON `null`, malformed JSON, and empty path, plus valid registration/challenge controls. Every rejected request returns stable `{code,error}` and leaves owner/device/challenge counts and App Attest/AI provider call counts unchanged. Existing cryptographic 403 and unknown-key 401 controls remain green. | Product defect, RED then GREEN. The initial 8-case run had 8 expected 400 responses without `code` because `[ApiController]` intercepted model binding. A controller-scoped filter before the MVC automatic invalid-model filter maps only these two routes to their established validation codes. |

## Files changed

- `backend/Areas/OwlAI/Controllers/DeviceController.cs`: route-scoped invalid-model response mapping.
- `backend/tests/AnonymousInstallationTests.cs`: deterministic collision and hash-bound HTTP resume coverage.
- `backend/tests/AppAttestCounterConcurrencyTests.cs`: persisted proof vs. later rollback coverage.
- `backend/tests/DeviceControllerBootstrapTests.cs`: HTTP validation matrix, successful controls, and rejected-request side-effect checks.
- `backend/tests/TestSupport/FakeAppAttestVerifier.cs`: test-only verifier call counters.

All source remains uncommitted. No untracked backend source file was introduced.
The review snapshot is `task10a-foundation-results/task10a-source.diff`, with
`task10a-source-sha256.txt`, `task10a-git-status.txt`, and
`task10a-untracked-backend.txt` alongside the raw logs. Review should compare
the live files with the hash manifest before acceptance.

## Verification and raw output

The test commands used a process-only `OWL_TEST_POSTGRES` setting above. Raw logs are preserved in `test-results/paid-access-hardening/task10a-foundation-results/`.

| Command | Log | Result |
| --- | --- | --- |
| `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RegistrationErrorContractHttpTests` | `validation-red.log`, `validation-red-detail.log` | Initial 8 missing/null/malformed cases RED: HTTP 400 lacked `code`. |
| Same focused filter after repair | `validation-green.log` | 9/9 pass. |
| Collision focused test | `collision-first.log`, `collision-green.log` | Initial fixture counting error; corrected test 1/1 pass. |
| Resume focused class | `resume-first.log`, `resume-green.log` | Initial shared-fixture absolute-count error; corrected class 15/15 pass. |
| Rollback focused test | `rollback-first.log` | 1/1 pass. |
| Combined focused classes | `focused-final.log` | 32/32 pass before the later six registration field cases. |
| `dotnet build Mavrylo.csproj --configuration Release --no-restore` | `backend-release-build.log` | Success, 0 errors, 1 pre-existing NU1510 warning. |
| `dotnet test tests/Mavrylo.Tests.csproj --configuration Release --no-restore` | `backend-release-verified.log` | Final source: 757 passed, 1 skipped, 0 failed, 758 total. |

Intermediate full logs `backend-release-suite.log`, `backend-release-final.log`, and `backend-release-final-after-review.log` were retained. Reruns followed later test changes: six additional missing/null registration field cases, explicit 403 rollback assertions, and an offline AI provider call counter. The final run is the evidence for the final source.

The final suite reports pre-existing NU1903 advisories for `SQLitePCLRaw.lib.e_sqlite3` and `SSH.NET`, plus NU1510 for `System.Formats.Cbor`. The sole skip remains `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`, pending Task 6A; it is not counted as fixed.

## Simplify and limits

After each coding iteration I reviewed the touched diff for redundant behavior and scope. The production repair uses one small action filter on the two affected routes rather than a global MVC response change. Collision synchronization stays in a test interceptor; HTTP resume uses one hash-checking fake and a nonseekable stream; rollback uses the existing filter and a fresh context. No speculative production refactor was added. `git diff --check` reported no whitespace errors.

This report closes only bounded Task 10A local foundation coverage. It does not prove the populated migration/restart matrix, historical-marker behavior, pending Task 6A/6B work, physical Apple flows, production inventory, or whole-feature release readiness. A clean-context source review and root acceptance remain pending.
