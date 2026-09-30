# Mac 2f45 fix 2 — independent narrow source review

Reviewed 2026-09-30. Scope: only the fix1-to-fix2 defer relocation in `FlashCardAITests/EntitlementMutationBoundaryTests.swift`, against live iOS HEAD `2f45a6c4725f67030f5d816bb47736e711e345a2`. No source edits, build, CI dispatch, runtime execution, or Git mutation performed.

**Accepted: the prior P2 cleanup finding is closed at source level. No new actionable finding in this increment.**

- Live line 306 registers `defer { Task { await order.releaseSecondGet() } }` immediately after gate creation, before fixture construction, retry scheduling, or suspension. Normal exit, the timeout guard return, and thrown errors now all schedule the release.
- `MutationOrderRetryGate` retains its sticky release flag (lines 1105, 1111, 1131). Release before a late second GET makes that GET skip suspension; release after arrival resumes the stored continuation. Actor isolation serializes flag/waiter access. Clearing the waiter after resuming (line 1133) makes the explicit success-path release plus deferred release idempotent.
- Independent SHA-256 checks of the live source and fix2 frozen after-copy both produced `3372fc37c78c3e014e51f8fb106aedac10d80afbfb9d2743c50e383235c000d9`. The fix2 before-copy and prior fix1 after-copy both produced `9db8ec02ea453349b1311ac5413e407c26a7d53973e9d78266c73f9cd6c3b556`.
- An independently generated comparison of fix1 and fix2 frozen after-copies contains only this defer relocation, matching the supplied incremental diff. Live checkout has only the test source modified. `git diff --check` passed, with only a line-ending warning. No applicable AGENTS.md was found in the checked ancestor paths or source/report trees.

This accepts only the cleanup relocation and its static reasoning. The deferred task is asynchronous; this review does not establish execution timing, compilation, or passing tests. Mac verification remains required.
