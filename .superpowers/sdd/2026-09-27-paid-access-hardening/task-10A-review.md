# Task 10A independent scoped review

**Verdict: bounded PASS; no actionable Task 10A finding in the frozen five-file snapshot.** This is an intermediate source and evidence review, not Task 10 or whole-feature acceptance.

## Snapshot and verification

- Backend HEAD remains `3957f3e08c6317ff1495939e5c0cfbe7fea4f110`; the five implementation files are modified and uncommitted. I reviewed `task10a-source.diff` and the matching live files, not HEAD alone.
- All 24 files listed by `task-10A-review-evidence.json` matched their recorded SHA-256 values. All five live backend source files matched `task10a-source-sha256.txt`.
- Preserved `validation-red-detail.log` shows the expected pre-repair 400 responses lacked `code`; `validation-final.log` shows the final 13 focused validation cases passing. `collision-green.log` and the other focused logs support the reported concurrency/resume/rollback outcomes. `backend-release-build.log` reports 0 errors and one NU1510 warning. `backend-release-verified.log` reports 757 passed, 1 skipped, 0 failed, with existing NU1903/NU1510 warnings. The B4 skip remains Task 6A work. I did not rerun unchanged broad tests.

## Scoped assessment

- Registration: `tests/AnonymousInstallationTests.cs:327-423` synchronizes both absent device reads with a database command interceptor and observes one unique-insert loser. It compares the **entire new owner-ID set** with the winning device owner, then checks key material, namespace, counter, library content, same-material retry and conflicting material rejection. The collision fixture's earlier count failure is a test assertion issue, not evidence of a product defect.
- Resume: `tests/AnonymousInstallationTests.cs:134-290` sends real HTTP requests through the controller with a verifier that checks stored public key, assertion bytes and an independently assembled body/path/challenge hash. The tests cover zero-byte and `{}` success, JWT identity, stored owner/key/library/counter, replay, wrong key/path/body, unknown key, authority and malformed bodies, no-store, and known-length/streamed 16,001-byte rejection. The 16,000-byte whitespace case reaches the expected 400 empty-object check.
- Proof persistence: `tests/AppAttestCounterConcurrencyTests.cs:114-180` executes the assertion filter, rolls back a later business transaction, and verifies in a new database context that the challenge remains consumed, counter advanced, and business write absent. Replayed and stale-counter proofs fail; a higher counter reaches the action once. This proves the fresh-context option in the brief; it does not exercise a restarted HTTP host.
- Validation: `Areas/OwlAI/Controllers/DeviceController.cs:31-35,80-89,104-125` places a route-scoped action filter before MVC's automatic invalid-model response. The filter maps malformed binding to the existing `{code,error}` shape without altering proof verification. `tests/DeviceControllerBootstrapTests.cs:209-317` covers missing/null/malformed fields and bodies, successful controls, 403 cryptographic rejection, 401 unknown key, and zero owner/device/challenge/verifier/provider effects on rejected validation requests.

## Remaining limits

The rollback proof uses a fresh context rather than a restarted host. Task 10B still owns the populated migration/restart and historical-marker matrix over final 6A/6B schema. This review does not establish physical Apple behavior, deployed configuration, production database state, or release readiness.
