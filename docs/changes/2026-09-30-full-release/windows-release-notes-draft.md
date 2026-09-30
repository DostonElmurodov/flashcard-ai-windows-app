# Owl AI 0.1.35 — Learn search and paid-access protection

Publication status: draft preparation only. Publish with the verified installer after the coordinated backend rollout is ready; do not describe the backend as deployed before verification.

## Changes

- Search words, translations and notes directly from the Learn header. Ctrl+F focuses the search field.
- Add cards from the Learn header. A selected set opens directly; All sets lets you choose a set; an empty library offers set creation.
- Updated subscription checks and account boundaries work with the accompanying hardened backend. Account changes and late responses cannot silently apply another account's access state.
- Previously saved cards and review history are retained when paid access ends, under the agreed retained-content policy.

## Download verification

Installer: `Owl-AI-Setup-0.1.35-x64.exe` (147,364,863 bytes).

SHA-256: `ca610b18254d20398d92f741e56c30aa4821089a184f3ebb27bcd929a270019f`.

Windows source commit: `5ddeede967d9ef11dc864b1b14e493822f2e36ed`.

Local validation: 112 tests passed; production packaging and packaged Learn smoke passed; independent release-candidate review found no blocking scoped issue. Installer is unsigned. Visible NSIS installer interaction was not independently exercised in this release check.
