## Changes

- Added card/word search and an Add cards button to the Learn page header.
- Automatic card synchronization runs at startup/sign-in, every 20 minutes with pending changes while active, every 6 hours while active without changes, and every 12 hours while inactive. Manual Sync now remains available.
- Incremental synchronization retains server acknowledgements and requests changes since the last saved cursor.
- Add cards remembers the selected entry method, including AI translation, across reopening and app restart. Selecting AI does not automatically send an AI request.
- Includes client-side paid-access checks and account/workspace protections from the security-hardening work.

## Installation and verification

Download **Owl-AI-Setup-0.1.35-x64.exe** for Windows x64. This installer is unsigned.

Source: `40278b212553dd3d01109c5e37d1a15c9b89b319`.

Validated with 116 passing tests, a production build, independent code review, and an Add cards persistence smoke test against the packaged executable, including a restart.

SHA-256: `6CC66913334DF3D9F780F95737BEB0FFDF780E1DD7A364680089E321D64EE909`.

## Server rollout status

This release contains the Windows client changes. The separate backend paid-access, quota and spending-control rollout is not deployed by this release. The requested lifetime allowance of 10 free AI operations is not yet active on the server. This release does not claim completion of that server rollout or Apple purchase/device validation.
