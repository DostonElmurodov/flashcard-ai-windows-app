# Verified backend image candidate

[Build-only run 36755060543](https://github.com/DostonElmurodov/mavrylo/actions/runs/36755060543) succeeded on 2026-09-30 at source `aa5fda0bc9718e82540fd32f177a4e4eadbf035c`.

Pinned reference: `ghcr.io/dostonelmurodov/mavrylo@sha256:bacebddb25d6afa9771982f5ce2a5da5113a6c1ced70a9098fb7f7100c463c29`.

The run passed 811 tests with zero skips, the full-history secret scan reported no leaks, and the application-project dependency scan reported no vulnerable packages in its configured sources. Test-project restores still warned about `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 and `SSH.NET` 2025.1.0; this report does not dismiss those warnings or describe the entire dependency tree as vulnerability-free.

The prior attempt passed the same 811 tests but stopped on three unchanged public Apple mock JWT fixtures. Exact historical fingerprints were independently reviewed and added without disabling default scanner rules. The accepted build mode published its commit SHA tag; it did not select deployment or move `latest`.

This digest is a built candidate, not the image observed on the running API. Rehearsal and eventual deployment must verify this digest and source revision explicitly. Later operations-only changes do not require rebuilding this already verified application image unless application inputs change.
