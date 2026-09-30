# Windows 0.1.35 release-candidate review

Reviewed 2026-09-30, read-only against `C:\Users\ForDo\.codex\worktrees\paid-access-hardening\FlashcardAI`, HEAD `fb8be9a8cc326cef696852dbecebda103f2b5850`, with the four pending release changes: README.md, package.json, package-lock.json, scripts/learn-header-smoke.ts. No product edits, builds, test reruns, Git mutations, deployment, or publication performed by this reviewer. This report is the only new file.

## Result

No blocking finding in the scoped Windows candidate review. The release version changes are consistent, and executablePath support is confined to the smoke harness. Without OWL_TEST_EXECUTABLE the original Electron launch remains; with it, the harness launches that executable without the development project argument. The Learn change retains search/filter behavior and workspace reset handling, reuses existing add-card routing, and preserves the lower empty-state Create a set action. The full paid-access branch is present; this is not a UI-only port.

## Evidence checked

- Saved `npm-test.log` identifies version 0.1.35 and reports 112 passed, zero failed/cancelled/skipped. It includes paid-access, account/workspace isolation, retained content, late entitlement response/direct denial ordering, and existing synchronization/import/review regressions.
- Saved `npm-run-installer.log` shows TypeScript, renderer/Electron production builds, and NSIS x64 packaging completed. Renderer filenames in that log match the actual archive. Build output predates the archive as expected.
- Saved `learn-header-packaged-smoke.log` ends in PASS for word/translation/notes searches, negative query, Ctrl+F, selected-set and chooser destinations, empty-set creation, and minimum-width geometry. The release report records that run against the packaged executable. The raw smoke log does not itself print the executable path, so that provenance depends on the saved release report; this review did not rerun it.
- `git diff --check` passes. Only the four expected release files remain changed.
- app.asar package.json contains version 0.1.35 and main `dist-electron/main.cjs`; Windows executable ProductVersion is 0.1.35.0 / FileVersion 0.1.35.
- All five first-party build outputs in app.asar match the current local dist/dist-electron files byte for byte: renderer CSS, renderer JS, index.html, main.cjs, and preload.cjs. Paid-access/workspace code and Learn content are present. This checks archived build correspondence; it is not an independent reproducible rebuild from source.
- Actual installer and executable hashes and lengths match release-report.md exactly. The NSIS payload was not independently extracted; association with this unpacked app is supported by the successful packaging log and matching reported artifact hashes.

## Artifact identity

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| release/Owl-AI-Setup-0.1.35-x64.exe | 147364863 | ca610b18254d20398d92f741e56c30aa4821089a184f3ebb27bcd929a270019f |
| release/win-unpacked/Owl AI.exe | 246201856 | f88d62d1e2392f195adb41b5816d63cafe1b79a7499611cd4118ef2d7ad45cb8 |
| release/win-unpacked/resources/app.asar | 98959148 | 8084c4a71823f8d43a936873117d58fd998b77e2628ef684ceef850b6596a27c |

## Package-content check

Archive top-level contents are node_modules, build, dist-electron, dist, and package.json. The only build directory file is icon.png. No .env, .git, test-results, local OAuth configuration, private-key containers, or local database files appeared in the archive filename check. No private-key/OpenAI/Google API-key signatures were found in first-party bundled JS/CJS/HTML/CSS. The actual compiled Google login call has a configured desktop client ID and an empty clientSecret. This is a bounded package-content check, not proof that every dependency is free of secrets or vulnerabilities.

## Explicit limits

- Visible NSIS installer screens and post-install Finish/launch behavior remain UI-unverified. Packaged application smoke coverage does not establish installer UI behavior.
- Installer is unsigned (`NotSigned`), consistent with the release configuration and report.
- Server deployment/preflight and public release/upload verification belong to the coordinator. This report does not claim production readiness or successful release.
