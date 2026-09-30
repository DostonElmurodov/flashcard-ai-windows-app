# Independent private backup review - final re-review

Verdict: APPROVE the frozen local changes for the existing Ubuntu test gate. No remaining blocking finding identified in the reviewed revision. This is not evidence of a successful Linux run, live backup, or restore; the Linux test step must pass before SSH, as the workflow requires.

Baseline: 6e462486d4cbde68ad8a34d3649dc9415d671dca. Read-only source review; only this report was written. No source edits, application builds, remote commands, deployments, or Git mutations were performed.

## Resolution of the initial findings

- Initial P1 disk/concurrency finding is addressed. A per-deployment flock now surrounds resource checks, dump execution, validation, and finalization. The namespace-side worker streams 64 KiB chunks, rejects writes beyond its fixed archive ceiling, and checks remaining free space before every chunk. The floor is at least 256 MiB or one measured database size. The lock prevents two cooperative backup runs from consuming the same planned reserve.
- Initial P1 privileged termination finding is addressed for normal timeout/resource/cancellation paths. The privileged worker directly starts pg_dump in a new process session, owns a 600-second deadline plus an alarm backstop, monitors coordinator PID/start identity, and kills the dump process group and waits on its child on failure. The worker owns the archive writer; the outer sudo timeout is now a later fallback, not the normal cancellation mechanism. The dump executable is resolved to an absolute path before its version is probed and that same path is passed to the worker.
- Initial P2 memory finding is addressed. Less than 512 MiB available memory now fails before archive creation and dump execution. The implementation accurately describes this as a start gate, not continuous memory containment.

## Verification

Independently ran `python -B -m unittest ops/linux/test_private_backup.py ops/linux/test_read_only_inventory.py`: 22 tests ran successfully, with 3 Windows skips (19 executed tests passed). The skips cover the real Linux worker/process-group test, flock contention, and symlink capability. `git diff --check` passed, with line-ending warnings only.

New orchestration coverage exercises successful archive/manifest finalization, low disk and zero-memory refusal before dump, dump and restore-list failures, changed API identity after dump, outer worker timeout, and final-name collision. Failures retain incomplete artifacts and do not log success. Static inspection of the Linux worker test confirms it executes the real worker with a fake streaming/sleeping dump, tests size/disk/deadline failures, and checks the dump PID has been reaped. These Linux cases have not executed in this Windows review; the workflow runs them on Ubuntu before configuring SSH.

## Preserved safety properties

Manual backup runs only the backup job and its tests; it neither builds nor deploys. Manual build runs the existing build/test/image pipeline, suppresses latest, emits commit and digest, and cannot select deployment. Credentials and database identity remain in libpq environment variables rather than command arguments. Raw dump errors and restore listings are discarded. The target is parsed from the one running Compose API, with strict endpoint parsing and identity checks before and after dumping. Private directory/file permissions, ownership checks, exclusive artifact creation, namespace validation, nonempty archive check, SHA-256, and successful restore listing remain intact. Existing final paths are not overwritten.

## Remaining limits and declined judgments

- I did not run SSH, sudo/nsenter, a database query, pg_dump, pg_restore, a workflow, image build, or restore. Live policy/credential retention, host capacity, target identity, executable installation, and archive restoreability remain unverified. Linux tests must pass before the live action.
- `pg_restore --list` validates the table of contents, not every data block or a complete restore. SHA-256 identifies archive bytes; it does not establish semantic correctness.
- The disk floor is sampled before each chunk, so unrelated host processes can consume space between checks. This is practical bounded backup behavior, not a filesystem quota or a global resource guarantee. Memory has a start threshold only.
- The worker handles normal signals, deadlines, resource failures, and observed coordinator loss. SIGKILL of the privileged worker itself, kernel-uninterruptible I/O, host failure, or equivalent exceptional conditions are outside the proven cleanup guarantee. The Linux test exercises host-mode worker ownership, not actual sudo topology; the same worker code executes inside the namespace in production.
- Path checks assume the deployment directory and its ancestors cannot be concurrently replaced by an untrusted local actor. Directory-descriptor traversal would be needed for a stronger adversarial race guarantee. Root or a compromised deployment account can observe environment credentials.
- The insecure-existing-directory test name still overstates coverage: it checks mode on newly created directories and symlink rejection but does not deliberately create an existing directory with unsafe permissions. Explicit checks exist in the implementation. Manifest-specific collisions and coordinator-loss/signaled-worker paths also lack direct tests. These are nonblocking residual coverage gaps in this revision.
- No YAML parser was used; workflow selection and ordering were inspected statically.

## Frozen reviewed SHA-256 hashes

- `.github/workflows/api-ci-cd.yml`: E48EF66AAF24F9A31B4228C7271BE442A0C3F8E5368F51E26DE782E40C0A545E
- `ops/linux/read-only-inventory.py`: A1DD6AFAB25729E4E47FC7DA402AC4953E32D3B7F0F25FC3798C029199E17238
- `ops/linux/test_private_backup.py`: F0923B24D1F8DF3EB119A717544CF8BCBAB16CEAAA73CE1CCE7D562208A4E6BF

## Latest bounded re-review: preflight capacity logging

Verdict: APPROVE this logging increment. Scope was limited to the frozen helper and tests relative to the previously reviewed backup implementation; no new framework or live review was performed.

The five new preflight fields at helper lines 547-551 contain only validated numeric database MiB/major version and host free-disk/available-memory/total-memory MiB. They do not interpolate a database name, username, password, filesystem path, or raw database contents. Resource values are checked before printing, including available memory not exceeding total memory. The 512 MiB memory gate remains unchanged at lines 552-554 and still executes before opening an incomplete archive or launching the dump. Disk bounds, worker behavior, and workflow selection were not relaxed by this increment.

Independent local rerun: 22 tests ran successfully, with the same 3 Windows skips (19 executed tests passed). The low-memory orchestration test asserts all five exact output lines, no password/database identifier, no subprocess launch, and no incomplete archive. No Git commands or remote actions were performed for this bounded re-review.

Coordinator-reported context: live run 36752822934 passed 22 Linux tests, then failed at the memory gate before archive creation. That run was not independently inspected by this reviewer and is not claimed as independent evidence here. The logging change itself has only the local verification above; its Ubuntu gate remains required. Previous operational/restoreability limitations continue to apply.

Current frozen SHA-256 hashes (supersede the earlier helper/test hashes above):

- `ops/linux/read-only-inventory.py`: 91901551A22043246F3018CE1D62AAC1057DE62C7012C7BB5CB1145BB4F7CC0E
- `ops/linux/test_private_backup.py`: 9F1E02C167662BE73AA9A1791958FB8564E09BAF7AF4BF3A4C65155FB5E73B43

## Latest bounded re-review: explicit small-database memory tier

Verdict: APPROVE the requested tiered start gate; no blocking issue identified in this bounded change. This supersedes the earlier statement that every database requires 512 MiB available memory.

At helper lines 552-556, databases whose measured size is at most 64 MiB require at least 256 MiB available memory; larger databases retain the 512 MiB requirement. The comparison uses integer bytes without rounded-log values, accepts equality deliberately, and still fails before archive creation or dump execution. The numeric logging remains safe. The unchanged adjacent disk floor/ceiling and worker controls continue to apply. This is an explicit operating policy and initial capacity check, not a guarantee that pg_dump or the database server cannot exceed the available memory during concurrent production activity.

Independent local verification: 23 tests ran successfully, with 3 Windows skips (20 executed tests passed). The new orchestration test covers 64 MiB database/255 MiB RAM refusal, 64/256 acceptance, 65/511 refusal, and 65/512 acceptance; it verifies rejected runs never launch a dump and leave no incomplete archive. Source inspection additionally confirms that a database one byte above 64 MiB selects the larger tier. No source edits, Git commands, or remote actions were performed during this review.

The coordinator supplied capacity evidence from run 36753940924: database 10 MiB, PostgreSQL 16, host 961 MiB total/400 MiB available memory, and 17644 MiB free disk. Those values would pass the new start gate if still current, but this reviewer did not independently inspect that remote run, and they do not establish peak-memory safety or future available capacity. The current revision must still pass its Ubuntu tests before SSH. Earlier restoreability and exceptional-process-failure limits remain applicable.

Current frozen SHA-256 hashes (supersede earlier helper/test hashes):

- `ops/linux/read-only-inventory.py`: 9938FCA666C1583BEBBBBF051A102A75F654F0D2F27B786C50B8B19B7EDC6C27
- `ops/linux/test_private_backup.py`: A2C3FC88387B487A03900CD005323AFD3169DE8AC49009B56C2D1AC6FBF746A6
