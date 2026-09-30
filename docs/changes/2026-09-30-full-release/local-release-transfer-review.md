# Local release / transfer independent review

## Phase 1: frozen local rehearsal integration

Reviewed 2026-09-30 against backend baseline `3716736cccbf5102eed60b8428240c2ab454ce8e`. Scope: new `ops/linux/local-rehearsal.py`, `ops/linux/test_local_rehearsal.py`, and the shared private-artifact validation / optional no-pull changes in `ops/linux/isolated-rehearsal.py`. Read the local implementation report. Existing server-runner internals were consulted only to verify new integration, not independently re-audited in full. Export/workflow phase is pending a separate freeze.

### Actionable finding

**P2 — Make both local container creations explicitly refuse image pulls.** `ops/linux/local-rehearsal.py:156` and `ops/linux/local-rehearsal.py:221` build `docker create` commands without `--pull=never`. Inspection establishes that an image was local at inspection time, but Docker create's default missing-image policy can still fetch a registry digest if it disappears before creation (for example, concurrent image cleanup). This breaks the local-only/no-pull contract; the PostgreSQL create also precedes construction of the in-container storage monitor. Add `--pull=never` to both local creation commands and a focused mocked assertion for both commands. Exact digest selection prevents a mutable-tag substitution, but does not itself prevent a registry fetch. This is a boundary failure under that concrete image-removal race, not evidence that any pull occurred.

### Verified from source

- Trusted plaintext SHA verification precedes manifest parsing and archive listing/restoration. Shared validation retains canonical paths, caller ownership, 0700 directory / 0600 single-link regular artifacts, size/hash/major/manifest checks.
- The local path does not call Compose, live_identity, host pg_restore, or the pull-enabled exact_image helper. Candidate identity is exact RepoDigest plus config ID and OCI revision, or explicitly pinned config ID plus revision. Offline digest-to-config mapping remains a coordinator/export trust prerequisite, correctly disclosed rather than asserted by this runner.
- Fresh PostgreSQL uses no network, no published ports, resource limits and retained Docker volume. API shares only its namespace, has no mounts or ports, and explicitly supplies the required dummy/disabled provider configuration and enabled production protection settings.
- Capacity probes run inside PostgreSQL against both overlay and volume paths, avoiding Docker Desktop VM-path interpretation by WSL. They are polling reserves, not quotas.
- Restore uses the in-container client, transactional restore, fixed read-only aggregate checks, 14 migrations / expected head, preserved aggregate counts, and observed Ready. No code forces Ready.
- Cleanup reuses exact random-name/labels/image/network ownership checks and protected cancellation cleanup. Private evidence retains identifiers and aggregates, with no raw backup/token output.

### Verification and limits

Independently ran `python -B -m unittest ops/linux/test_local_rehearsal.py ops/linux/test_isolated_rehearsal.py` on Windows: 17 tests, success, 2 Linux-only skips. Attempted the same through Ubuntu WSL; the service returned `Wsl/Service/E_ACCESSDENIED`, so this reviewer did not independently reproduce the report's Linux zero-skip run. No Docker command, image load, backup decrypt/read, restore, live-server access, or provider request was executed. The existing tests largely cover helper boundaries and shared lifecycle; they are not an end-to-end simulation of local_rehearsal. Actual local execution remains unverified.

Frozen reviewed SHA-256 values (raw file bytes):

| File | SHA-256 |
| --- | --- |
| ops/linux/local-rehearsal.py | 1783820e43e581b0235bacc701a8b3a0010415b68a41fd1ea96fd0b16ced6e2d |
| ops/linux/test_local_rehearsal.py | 143497e2d3c3be8c539fe4d870d60f4f6ff1fbce77b769617a3ffca0aab23392 |
| ops/linux/isolated-rehearsal.py | 5f381d558354673488222984ededcf9b1f636a0a5e64172b95fab4f4df05f565 |

Source pins supplied for review, not independently read from production: backup `20260930T175711Z-a7f928dfbe434473`, 257032 bytes, plaintext SHA `3861e0d70cd81e3d73db910def8cb4fd1b2c58cecd585f9b65898859f73a2150`; candidate registry manifest `sha256:bacebddb25d6afa9771982f5ce2a5da5113a6c1ced70a9098fb7f7100c463c29`, source revision `aa5fda0bc9718e82540fd32f177a4e4eadbf035c`.

Disposition: address the P2 no-pull boundary before execution; phase 2 transfer/export review remains pending. No live migration or original-server health claim is made.

## Phase 2 and phase 1 remediation review

Reviewed the frozen export workflow/helpers/tests and the revised local runner. The phase 1 P2 is **resolved**: `local_create` injects `--pull=never`, both PostgreSQL and API call it, and the new focused mock checks both commands. Final reviewed local runner SHA-256 is `8f9b0b0a219d7ed8cf71a53acee69cffec90c173c94cc6bcb123f45ab79b075c`; local tests SHA-256 is `869eee7834c5da5a5bce5641d55b51d7fdbf3c5314bb9e8672df8129b34d8715`. Earlier phase 1 hashes describe the superseded version.

### Export findings

No additional actionable source defect identified in the reviewed export scope. The following execution prerequisite remains explicit: the ciphertext contains the archive only. The local runner separately requires `<backup_id>.json` with manifest identity, archive size/hash, PG/client majors and `database_bytes`. The coordinator must provide that private manifest from trusted source metadata; if reconstructing it from the existing backup job's rounded-up database MiB, label the sizing value as a conservative reconstruction, not an exact copied source value. The trusted plaintext checksum remains the preexisting backup-job pin, not a checksum learned only from the download.

### Export boundaries verified from source

- Manual job conditions separate both export modes from build/deploy; image export has no SSH step or build/deploy dependency and grants only contents/package read. Backup export uses the existing production SSH boundary and a validated deployment path.
- Backup request accepts an exact identifier, lowercase SHA-256 and public certificate PEM shape. The source bundler uses validated values and Python repr, avoiding shell interpolation of recipient data. A private key PEM is rejected. Only the public certificate crosses to the runner/server.
- The helper opens private regular single-link artifacts with ownership/mode checks and O_NOFOLLOW, validates the manifest binding, caps the archive at 64 MiB, hashes the opened archive descriptor before encryption, reuses that descriptor and hashes it again before output. A source mutation cannot silently become an accepted local plaintext because the recipient must also compare the independently pinned SHA before parsing/restoration.
- OpenSSL CMS uses streaming AES-256-CBC with RSA-OAEP/SHA-256/MGF1-SHA-256. Key strength is restricted to RSA 3072/4096. The recipient certificate expiration is checked; this is not general PKI chain/not-before validation and none is needed to establish the user-supplied recipient pin. Encryption output has a child file-size limit, 120-second timeout and process-group kill on caught interruption. CMS CBC is not authenticated encryption; the independent local plaintext pin is a required integrity gate.
- Only ciphertext reaches remote stdout and the one-day backup artifact. Fixed identifier/hash/size/envelope fields reach stderr. Ciphertext is finalized privately before streaming; original dumps/manifests are not modified. The runner checks nonempty/size/CMS structure and uploads one explicit path.
- Image export fixes the allowed repository, requires an immutable digest and expected OCI revision, logs in with token stdin and an isolated private Docker config, inspects the exact digest, then saves only the verified config image ID. The streamed gzip has a time/size bound, and provenance contains only the five expected keys. Archive/provenance explicit artifact paths do not include credentials. The runner is ephemeral; no claim of cryptographic signing of the provenance is made.
- Download trust must bind the image metadata to this trusted workflow run and expected source/digest. Verify compressed archive SHA before Docker load and the loaded config ID before the local runner. A matching source label alone does not establish registry provenance, which is why the trusted export mapping is required.

### Combined verification and disposition

This reviewer independently ran all six focused test modules on Windows: **52 tests passed, 8 platform skips**, including a throwaway-key real OpenSSL CLI roundtrip; no production data/key was used. The coordinator separately reported the same six-module suite under Ubuntu WSL: **52 passed, zero skips, 1.916 seconds**. The Linux result is coordinator evidence, not a reviewer-executed run. Tests mock Docker/SSH/database boundaries or use throwaway fixtures; they do not establish a completed real transfer, image load, restore or migration rehearsal.

Frozen phase 2 file hashes independently matched:

| File | SHA-256 |
| --- | --- |
| .github/workflows/api-ci-cd.yml | 93fd4ded7630ce96eade5d1550b531075f6487f15ff19309ff964bbdabee8332 |
| ops/linux/export-private-backup.py | 3088ee8951d7b06d116dda62e3a3f41491f2ec3282b62e6fc49af4562f93ecff |
| ops/linux/test_export_private_backup.py | 22d66c1b52e9739a94d2e1de031ed0adab185d27682458d5b619f9b76468b10b |
| ops/linux/export-image.py | d5aa8112b5f9acdbfc3e78f9fc90d3cdfcd008d55bec54d027c14a007604fdeb |
| ops/linux/test_export_image.py | 44b2651306c75e7836dcc34878d572693d85d5c2cd27be254c0e00164249510f |

Final code-review disposition: phase 1 finding resolved; no remaining actionable code defect found in the reviewed changes. Complete the explicit manifest handoff and trusted download/image verification during execution. Actual rehearsal, original-server stability, Apple/provider behavior, cutover and rollback remain separate gates. Reviewer performed no remote/Git mutation/Docker action and did not read real private backup contents.

Manifest handoff confirmed by coordinator: reconstruct the expected manifest keys from successful trusted backup run `36755056922`: archive size 257032, pinned plaintext SHA above, majors 16/16/16, validation `pg_restore_list_only`, exact ID/basename. Set `database_bytes=10485760` as the conservative upper bound derived from that job's ceil-10-MiB output. This value is used only by sizing gates and will be documented in a companion/operational report as reconstructed, not copied exact metadata. This resolves the handoff prerequisite without extending the export surface.
