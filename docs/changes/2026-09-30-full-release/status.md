# Full release preparation — 2026-09-30

The user authorized deploying all paid-access/security changes and publishing the Windows release with Learn search and Add cards. A UI-only release is not the chosen scope. No production cutover or public Windows 0.1.35 release has happened yet.

## Verified candidates

- Backend application source before operations tooling: a6e2362c8e624338c564bfd88499cd1835c956c1. Build-only run 36755060543 at aa5fda0bc9718e82540fd32f177a4e4eadbf035c passed 811 tests, full-history Gitleaks and the application dependency scan. [Pinned image and warning limits](backend-image.md): sha256:bacebddb25d6afa9771982f5ce2a5da5113a6c1ced70a9098fb7f7100c463c29. Deployment was skipped; latest was not moved by build-only mode.
- Windows commit 40278b2, version 0.1.35: 116 tests, updated installer build and packaged Add cards restart smoke passed; prior packaged Learn smoke passed at 5ddeede967d9ef11dc864b1b14e493822f2e36ed. Independent candidate review found no blocking scoped issue. Installer and source retain the security changes.
- iOS source 5d2d5bebfc66a63e9d12df6a271a51c133ed7be3: Mac run 36730138820 passed 694 tests and unsigned Release checks. No signed-device or App Store release is claimed.

## Verified server and backup

[Read-only preflight](server-preflight.md), run 36743325476, confirmed the running API source 473a39bee299f3df4c7fdcf6f45502f880a9bd8b, Apple Sandbox and TestMode enabled. The new candidate has not replaced it.

[Database inventory](database-inventory.md), run 36748418193, succeeded through the existing API network namespace without modifying ACLs, credentials, containers or data. Snapshot: 9 migrations, 143 devices, 65 saved server words, 10 legacy usage records, no subscription records, no duplicate client-word groups and no negative usage. Zero recorded subscriptions means the migration issue caused by existing subscription rows is not demonstrated in this snapshot; other migration/usage checks still require rehearsal.

[Private backup](private-backup.md), run 36755056922 at aa5fda0, passed 23 Linux boundary tests and produced a 257032-byte server-side archive with SHA-256 3861e0d70cd81e3d73db910def8cb4fd1b2c58cecd585f9b65898859f73a2150. Its catalogue and actual restoration were verified; see [local migration result](local-rehearsal-result.md). Host memory is 961 MiB total, approximately 397 MiB available; database size is 10 MiB.

The first backup runs stopped before archive creation at the original 512 MiB floor. A reviewed small-database tier now requires 256 MiB for databases up to 64 MiB; larger databases retain 512 MiB. Process, locking and disk protections remain active.

## Work in progress and remaining gates

The reviewed server-side rehearsal tool was committed locally as 3716736. Its 4.5 GiB memory requirement exceeds this server's capacity. Local WSL Ubuntu and Docker Desktop are available with approximately 31 GiB Docker memory. Encrypted-only backup export and local rehearsal completed successfully: 9 migrations became 14, aggregate counts were preserved, and usage Ready is true with zero issue rows. The decryption key stays on the user's computer. The local registry login lacks read:packages; the already built image was exported through the existing authorized GitHub build identity and verified locally without rebuilding it.

The prior build attempt stopped after 811 passing tests on three historical JWT findings. Official Apple Git blob metadata proved they are unchanged public mock fixtures. Exact historical fingerprints were reviewed; default scanner rules remain active. The replacement full-history scan passed.

Still required: approved AI request limits and cumulative provider spending cap; actual App Store/TestFlight distribution and real Apple app ID; final deployed configuration and digest verification; coordinated Windows publication. Repository and production-environment variable-name queries returned no GitHub variables, supplying none of those missing settings. Never bypass migration issues by erasing them or blindly setting Ready.

## Agent accounting

43 of the authorized 60 agents have been used. Agent 40 independently reviewed local rehearsal and encrypted export; 52 focused Linux tests passed. Agents 41–43 implemented and independently reviewed the approved Windows sync cadence and Add cards preference. Agents 31/33/35/37/39 reviewed preflight, inventory, backup, rehearsal and fixture-scan work. Agents 32/34/36/38 implemented the corresponding increments; same-task follow-ups reuse their agents. Agents 27–30 covered Windows packaging, backend preflight, workflow preparation and Windows candidate review. Root owns commits, pushes, live execution and publication.

## Additional approved Windows changes

Windows commit 40278b2 adds startup / active-dirty 20-minute / active-clean 6-hour / inactive 12-hour sync, plus workspace-scoped remembered Add cards method. Full suite 116/116, build and Electron restart preference smoke passed. Independent review finding fixed and closed. See [change evidence](windows-sync-and-entry.md). The previously recorded installer hash is superseded. Updated installer packaging succeeded, 147364716 bytes, SHA-256 6CC66913334DF3D9F780F95737BEB0FFDF780E1DD7A364680089E321D64EE909. The Add cards reopen/restart/zero-AI-call smoke also passed against the packaged executable. The installer remains unsigned and unpublished.

The user subsequently approved 10 free AI operations total for the lifetime of the quota subject, not a daily allowance. This decision is not implemented by the Windows scheduling change. Paid limits and cumulative provider-spend cap remain unapproved.
