# Owl AI for Windows

An Electron desktop app for local vocabulary sets, FSRS study, imports and account-owned Premium shared with Owl AI on iPhone.

## Run the app

Build the Windows x64 installer with `npm run installer`. Artifacts are written to `release/`:

- `Owl-AI-Setup-0.1.30-x64.exe` — install with a selectable destination and desktop shortcut.
- `Owl-AI-Portable-0.1.30-x64.exe` — run without installing (built separately with `npm run package`).
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
- Optional second language in revealed answers, with an AI translation and short explanation cached locally for repeat display.
- Manual cards, AI translation, pasted text, CSV/TSV/TXT, PDF text, image OCR and scanned PDF OCR. Words/Pairs/Auto modes and editable previews. Imports above 2,000 unique cards are rejected explicitly. Files are limited to 20 MB and PDFs to 100 pages.
- Speech through installed Windows voices; repeated playback alternates normal and slow speed.
- Configurable local reminders while running, optional tray background mode and opt-in Windows startup.
- Existing iPhone account login through Google system-browser OAuth or email, logout/deletion, encrypted account credentials, automatic session refresh and server-authoritative Premium status.
- Account catalog search/import, publishing and unpublishing sets. Published sets require backend approval before they appear in the public catalog.
- An existing Apple subscription can be linked to the same account on iPhone and used on Windows, using the accompanying backend/iOS changes.

OCR downloads the selected learning language's recognition data on first use, then caches it under the app data directory. The selected file itself is processed locally. Windows speech voices are installed in Windows Settings, not downloaded by this app.

### Second language in review

In Settings, turn on **Second language in review**, choose a language, and save preferences. Cancelling the picker keeps it off; switching it off clears the choice. Your native language is excluded, and changing the native language to the selected secondary language clears it. Native and learning languages, cards, and review schedules remain unchanged.

After **Translate with AI**, the preview displays the second-language translation before you save the card. Loading or retrying it does not block saving. Editing the source word hides its old secondary translation.

After **Show answer**, the card displays a compact flag, language name, translation, and explanation. The first uncached request uses the protected account API and requires sign-in plus an active shared subscription or server-enabled test mode. It uses the card's source languages. Loading, errors, and retries never block grading, and there is no secondary audio control.

Successful results live in a separate SQLite cache scoped to the API origin, account workspace, source word, source languages, and secondary language. Cached content remains readable offline without another AI request. Failed or malformed responses are not cached, and results arriving after a card, setting, or account change cannot replace the current content. The cache is included in that workspace's local backup; it is not synced as a new word.

## Server compatibility and shared subscription

Default API origin: `https://api.mavrylo.com`. The backend must include the accompanying changes in `../mavrylo` for the account AI/catalog/entitlement and review-translation routes to work.

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

### Shared test mode

The backend setting `TestMode__Enabled=true` enables test mode for updated desktop and iOS clients. The desktop app reads the public `GET /owlai/config/feature-flags` response (`{"test_mode":true}`) without signing in, on launch, foreground focus, and every minute. The request bypasses HTTP caching. Test mode removes purchase promotion and the backend's commercial word/AI quotas; online account operations still require authentication and retain ownership checks. Actual subscription records are unchanged.

The server is the only authority for test access. Each launch starts in normal mode and ignores legacy cached values; only a successful response containing the boolean `true` enables test mode. An explicit `false` immediately restores normal Premium UI and access rules. Offline, malformed, or failed checks also disable test mode, without changing any real subscription. Periodic/focus refreshes apply server changes to the UI; server operations always enforce the current server setting. There is no local override.

Existing Windows installations need a rebuilt client containing this feature; a server setting alone cannot update their UI. On a Windows build machine, run `npm ci` and `npm run installer`, then distribute the resulting `release/Owl-AI-Setup-<version>-x64.exe`. Users install that build and reopen Owl AI. This source change does not publish a release or change the application version.

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
npm run test:secondary-ui
npm run test:google
npm run test:google-ui
node scripts/security-smoke.mjs
npm run package
npm run installer
```

The integration script expects the companion Development backend on `http://127.0.0.1:5289` and an existing iOS-enrolled test account in `OWL_TEST_ACCOUNT_EMAIL`/`OWL_TEST_ACCOUNT_PASSWORD`. It creates and removes only its own test set, never registers or deletes an account. `node scripts/account-sync-smoke.mjs` verifies real Electron account switching against an isolated local HTTP fixture without credentials. Smoke scripts create isolated profiles under `test-results`. `OWL_TEST_DATA_DIR` overrides the app data location for isolated testing; `OWL_TEST_EXECUTABLE` lets `scripts/smoke.mjs` target an unpacked packaged executable.

`npm run test:secondary-ui` exercises the real Settings and Review React components in headless Chromium with a deterministic desktop bridge fixture, including cancellation, reveal timing, grading during loading, retry, and stale-result rejection. It saves screenshots under `test-results/secondary-review/`. Set `OWL_TEST_BROWSER` to a Chrome/Chromium executable if needed; otherwise it uses installed Chrome on macOS or Playwright's Chromium. This is browser UI verification, not a Windows installer test.

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
