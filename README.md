# Owl AI for Windows

An Electron desktop app for local vocabulary sets, FSRS study, imports and account-owned Premium shared with Owl AI on iPhone.

## Run the app

Previously built Windows x64 artifacts are in `release/` (0.1.27 does not include the new account-sync source changes):

- `Owl-AI-Setup-0.1.27-x64.exe` — install with a selectable destination and desktop shortcut.
- `Owl-AI-Portable-0.1.27-x64.exe` — run without installing.
- `win-unpacked/Owl AI.exe` — unpacked application for local verification.

After installation, **Run Owl AI after Finish** remains available. When checked, Finish expands to fit its caption at the current Windows font/display scale, disables itself and shows **Opening Owl AI...** while a separate launch helper opens the application. The visible installer remains responsive; repeated clicks cannot start extra launches. It closes when Windows accepts the launch. If the helper fails or exceeds 20 seconds, Finish becomes available again with instructions to open the desktop or Start menu shortcut. The original hang was not captured directly; Windows shell activation is now isolated from the installer UI thread.

The builds are not code-signed. A release signing certificate is not configured in this workspace.

The first launch asks for native and learning languages. No account is required for local study. Create a set using its name, description and languages. Then open the set and choose Add cards. Enter a word and translation and choose Save cards to save immediately; the fields clear and the screen stays open for your next word. AI and imported cards use a preview and Save selected cards; this also keeps the screen open. The daily queue uses active sets matching the selected languages. Change the new-word goal and review direction in Settings.

## Implemented

- Ink & Indigo visual theme with a navy sidebar, saturated blue actions, stronger contrast and coordinated light/dark appearances.
- Compact Learn dashboard with Words collected, Ready to review, Practiced today and Day streak at the top, set management, vocabulary search/edit/delete, responsive layout, light/dark/system appearance and three accents.
- Local SQLite persistence, migrations, CSV export, whole-database backup/restore. Logout and account deletion preserve local cards.
- FSRS-6 with the iOS project's 21 weights, learning/relearning steps, four grades, retention preference, independent forward/reverse state and daily new-word limits.
- Review shortcuts: Space reveals the answer; 1–4 grade it. Ctrl+F opens vocabulary search.
- Manual cards, AI translation, pasted text, CSV/TSV/TXT, PDF text, image OCR and scanned PDF OCR. Words/Pairs/Auto modes and editable previews. Imports above 2,000 unique cards are rejected explicitly. Files are limited to 20 MB and PDFs to 100 pages.
- Speech through installed Windows voices; repeated playback alternates normal and slow speed.
- Configurable local reminders while running, optional tray background mode and opt-in Windows startup.
- Existing iPhone account login through Google system-browser OAuth or email, logout/deletion, encrypted account credentials, automatic session refresh and server-authoritative Premium status.
- Account catalog search/import, publishing and unpublishing sets. Published sets require backend approval before they appear in the public catalog.
- An existing Apple subscription can be linked to the same account on iPhone and used on Windows, using the accompanying backend/iOS changes.

OCR downloads the selected learning language's recognition data on first use, then caches it under the app data directory. The selected file itself is processed locally. Windows speech voices are installed in Windows Settings, not downloaded by this app.

## Server compatibility and shared subscription

Default API origin: `https://api.mavrylo.com`. It must be deployed with the accompanying changes in `../mavrylo` before the new account AI/catalog/entitlement routes work. This implementation did **not** deploy the backend or release the iPhone app.

The completed sharing flow in code is:

1. Purchase or restore an Apple subscription on iPhone.
2. Sign in to Owl AI on iPhone and explicitly link the verified purchase in Profile.
3. Sign in to the same account on Windows. Premium is refreshed from the server.
4. Apple renewals, expiry, grace and refunds update the account's shared entitlement. Existing valid sources are considered together.

The current desktop app does not sell a second subscription. Apple remains the payment source. Direct Windows checkout is not included. Account-scoped collection/card/review-schedule sync is implemented in the current source; see `docs/account-sync.md` for its rollout and validation requirements. Account AI requires verified shared trial/Premium/grace; free local study does not.

Backend contract and production configuration: `../mavrylo/docs/shared-subscription-api.md`. Required new production settings are `AccountAi__DailyQuota` and `AccountAi__RequestsPerMinute` (initial recommended values 200 and 30). Keep existing App Attest, StoreKit, JWT and provider protection enabled.

The Connection section is hidden in Settings. The existing internal configuration still accepts an HTTPS origin or loopback HTTP for development tests. Sign out before changing servers. There is no desktop App Attest bypass, bundled provider key, or local Premium switch.

Google setup and release requirements: [Google sign-in](docs/google-sign-in.md). The desktop OAuth client ID must be configured at build time and added to the backend audience allowlist. No real Google sign-in has been verified in this workspace yet.

Subscription status refreshes automatically. The manual refresh and Apple management buttons are hidden in Profile.

## Development

Use Node.js 22.12 or later with npm. Dependencies are locked in `package-lock.json`.

```powershell
npm ci
npm run build
npm start
```

For npm versions that require lifecycle-script approval, approve Electron's official runtime install script and esbuild's platform setup before building. In this workspace npm was bootstrapped under `D:/owl-ai/.tools/npm/package/bin/npm-cli.js`; the build environment includes `D:/owl-ai/.tools/bin` in PATH for child npm commands.

`npm run dev` serves the renderer for inspection; native operations require Electron. Use `npm run build` then `npm start` for the complete desktop app.

```powershell
npm test
npm run typecheck
npm run smoke
npm run test:imports
npm run test:integration
npm run test:ui
npm run test:google
node scripts/security-smoke.mjs
npm run package
npm run installer
```

The integration script expects the companion Development backend on `http://127.0.0.1:5289` and an existing iOS-enrolled test account in `OWL_TEST_ACCOUNT_EMAIL`/`OWL_TEST_ACCOUNT_PASSWORD`. It creates and removes only its own test set, never registers or deletes an account. `node scripts/account-sync-smoke.mjs` verifies real Electron account switching against an isolated local HTTP fixture without credentials. Smoke scripts create isolated profiles under `test-results`. `OWL_TEST_DATA_DIR` overrides the app data location for isolated testing; `OWL_TEST_EXECUTABLE` lets `scripts/smoke.mjs` target an unpacked packaged executable.

## Layout

- `electron/main.ts` — desktop lifecycle, validated IPC, native dialogs, notifications/tray.
- `electron/preload.ts` — narrow typed bridge; renderer has no Node or filesystem access.
- `electron/store.ts` — SQLite, transactional mutations, queue/history and backup/restore.
- `electron/scheduler.ts` — FSRS adapter and calendar study-day boundary.
- `electron/api.ts` — account lifecycle, encrypted credentials, origin checks, HTTP and refresh.
- `electron/imports.ts`, `electron/files.ts` — parsing, native file selection, PDF and OCR.
- `src/` — React feature screens and local UI state.
- `shared/types.ts` — renderer/main contracts and languages.
- `tests/`, `scripts/*smoke.mjs`, `scripts/integration.mjs` — unit, real-window, file and HTTP checks.

Data lives under Electron's per-user application data directory (`owl-ai-windows`): guest `owl.sqlite`, per-origin/account `accounts/<sha256>.sqlite`, encrypted `account.enc`, and the OCR cache. Backups contain cards, review history and settings, never account credentials. Restore preserves the currently selected API origin and startup preference.

## Validation limits

Desktop and backend automated checks do not prove a real Apple purchase flow. Before releasing the shared subscription feature, build/test the iOS changes on macOS/Xcode and exercise a real attested iPhone with Apple Sandbox purchase, claim, renewal, refund, logout and account switching. No actual purchase, production migration or deployment was performed here.

The existing backend test dependencies emit NuGet advisory warnings for SQLitePCLRaw.lib.e_sqlite3 and SSH.NET. They are recorded separately from the desktop npm dependency audit.
