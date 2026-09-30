# Owl AI Windows 0.1.35 release preparation

- Source: `fb8be9a8cc326cef696852dbecebda103f2b5850` in `C:\Users\ForDo\.codex\worktrees\paid-access-hardening\FlashcardAI`.
- Package version: `0.1.35`; executable ProductVersion: `0.1.35.0`.
- Installer: `release/Owl-AI-Setup-0.1.35-x64.exe` — 147,364,863 bytes — SHA-256 `CA610B18254D20398D92F741E56C30AA4821089A184F3EBB27BCD929A270019F`.
- Packaged app: `release/win-unpacked/Owl AI.exe` — 246,201,856 bytes — SHA-256 `F88D62D1E2392F195ADB41B5816D63CAFE1B79A7499611CD4118EF2D7AD45CB8`.
- Signing: unsigned (expected; no release certificate is configured).
- OAuth packaging check: a default desktop client ID is configured; no client secret or Web-client configuration is bundled. The build rejects Web and malformed client configurations.

## Validation

- `npm test`: passed 112 tests.
- `npm run installer`: passed typecheck, production renderer/electron build, and NSIS x64 packaging.
- Packaged-executable Learn-header smoke: passed through `release/win-unpacked/Owl AI.exe`, including word/translation/notes search, Ctrl+F, Add cards routing, set selection, and narrow-layout checks.

Temporary files used `test-results/release-0.1.35/tmp` through the process `TEMP`/`TMP` settings. Logs are kept beside this report.

## Release boundary

The packaged executable was exercised. The visible NSIS installer screens were not UI-tested. No commit, push, deployment, or GitHub release was performed here.
