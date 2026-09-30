# Isolated rehearsal independent review — final cleanup increment

Verdict: no remaining actionable findings in the bounded cleanup-fix increment. The two prior cleanup findings are addressed. This is source/local-test approval of the frozen runner, not evidence of a successful runtime rehearsal or release authorization. No local WSL mode is included in this review.

## Frozen reviewed files

Under `test-results/paid-access-hardening/backend/ops/linux`:

- `isolated-rehearsal.py`: SHA-256 `B9383FC7F83A16A5144AE0F753B210F0C03C6DAA857B402EDC4358DAE86591A5`.
- `test_isolated_rehearsal.py`: SHA-256 `D6AD3BDAA058106B7E1E759E208E86C2308A00BDB6B24BC2B7EE2AE4260B1F7C`.

## Findings closed

- **Prior P1 evidence-write/stop ordering:** record persistence errors now append a separate error and do not bypass stopping a container whose ownership has been proven. The regression forces every evidence write to fail, including initial PostgreSQL ID recovery, and verifies API then PostgreSQL stop attempts.
- **Prior P2 cancellation lifetime:** `protected_cleanup` sets cleanup mode before executing cleanup, retains the installed handler throughout both stop attempts and the final identity check, and restores original handlers afterward. Cancellation during cleanup is recorded without aborting it and results in a failed rehearsal afterward. The new test invokes the cancellation callback during the actual cleanup function and verifies both stop attempts occur before handler restoration. This is a mocked callback test, not a Linux OS-signal integration test.

Earlier reviewed repairs remain accepted: source-major restore-client restriction before container creation; Docker-root and volume-source storage checks with reserve monitoring and disabled container logs; create/start separation and exact random-name/run-label/image/network recovery; verification of stop state. These repairs address the previous review findings within the stated supported environment.

## Verification

Executed `python -B -m unittest ops/linux/test_isolated_rehearsal.py`: **12 tests ran, 11 passed, one Windows symlink-capability skip**. Read the revised cleanup functions, orchestration and corresponding regression tests, and updated implementation report. The suite made no real Docker or database calls.

No code/test edit, Git/remote mutation, Docker command, image pull, SSH/server operation, live DB access, or workflow edit was performed by this review. Only this report was written.

## Retained limits and declined judgments

- Real restore, migration startup, Docker ownership/network inspection and Linux permission/signal behavior remain untested. Passing local tests does not establish a runtime release gate.
- The coordinator supplied live-host measurements of 961 MiB total and about 400 MiB available memory; this reviewer did not measure them independently. The unchanged 4.5 GiB gate excludes that host. Do not lower the reserve arbitrarily or run this rehearsal there. A different host or explicit WSL mode needs its own assessment.
- Disk protection is a monitored abort strategy, not a hard filesystem quota or guarantee against growth between observations. Docker storage paths must be measurable locally with the executing identity. SIGKILL and inaccessible/unresponsive Docker cannot be made recoverable by ordinary in-process cleanup; private identities support manual recovery. Ownership ambiguity appropriately fails closed instead of stopping unproven containers.
- Source revision labels establish the configured label contract, not independent build provenance. Aggregate preservation is narrower than row-level equality or old-image compatibility. Ready is observed, not forced. No additional provenance or full-release gate is introduced by this review.
- No demonstrated new injection, raw-data publication or live-DB write path was found in these increments. Existing immutable candidate checks, network-none/shared-namespace design, dummy credentials, guarded Production settings, aggregate-only public output and retained artifacts remain as previously reviewed.
