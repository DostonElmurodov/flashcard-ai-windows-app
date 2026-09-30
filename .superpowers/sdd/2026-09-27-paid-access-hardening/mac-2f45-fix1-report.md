# Mac 2f45a6c boundary-test fix 1

Status: test-only iOS source patch, awaiting fresh independent review and an actual Mac test run. No product source, backend, Windows, provider, production data, or Apple purchase flow changed. iOS HEAD remains `2f45a6c4725f67030f5d816bb47736e711e345a2`; the test file is uncommitted. The prior Mac run `36725550762` compiled and ran 693 tests: 691 passed, including all 28 UI tests and the three historical restore regressions; the two named boundary tests failed at their first `retryDeviceWordSave("word-11")`. Unsigned Release did not run.

## Root cause and intended boundary

Both failing tests set free access with 11 retained local cards, then synchronously retried the 11th. `retryDeviceWordSave` calls `requireEditPermission` before it schedules asynchronous work. The 11th has zero-based index 10, so `FreeLimitPolicy.canEditWord` correctly denies it with HTTP 402. Those failures show unreachable fixtures, not a failure in order reconciliation. The public free-first-ten edit gate remains unchanged.

The revised exclusion test queues the local 10th card while 12 local cards are retained. The fake authoritative response includes an older server-only card, so its first complete order excludes local `word-10`. A second retry for the same card waits behind the first mutation; arrival at its second GET proves the first operation finished. At that barrier, no upsert or persistent `access_denial` exists, while local `word-11` is review-locked. The test deletes `word-01`, leaving 11 local cards, then releases the second GET. This keeps reconciliation required and makes the second authoritative response include `word-10`. It checks the posted order removed `word-01`, exactly one `word-10` upsert was accepted, and no denial marker remains. A bounded wait and deferred gate release keep a regression from hanging the Mac suite.

The expiry test likewise queues editable `word-10` with 11 retained cards. Its awaited order POST changes access to `expired_paid`; the test checks retained `word-11` remains readable and not editable. The fixture drops its repository reference and waits for repository task lifetime to end before checking that no upsert or denial marker appeared. This drain is test-only: the queued task captures the repository and releases it when complete; the fixture keeps the database alive for the final SQL assertion.

A separate control now asserts that a free user's 11th saved card still gets synchronous HTTP 402, with no order request, no upsert, and retained text.

## Evidence and handoff

`test-results/paid-access-hardening/mac-2f45-fix1/` contains the exact before and after test file, SHA-256 manifest, and incremental Git diff. The after copy matches the live file. `git diff --check` returned no whitespace errors; Git emitted only its working-copy line-ending warning. No local Swift compiler or Mac test execution was used. The previous Mac RED is preserved at `test-results/paid-access-hardening/task6b-results/mac/36725550762-verification-2f45a6c/`; it proves the old fixtures fail before the async path, not that the revised cases pass. Fresh review should verify reachability and Swift compilation before root dispatches one combined Mac run.
