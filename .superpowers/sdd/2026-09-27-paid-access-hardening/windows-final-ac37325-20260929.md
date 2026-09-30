# Windows verification — 29 September 2026

Exact source: `ac37325c3cae2cef5d2adaddbaa214f213a25892`; worktree clean after checks. No Windows product code was edited. Node24.15.0/npm11.12.1. Raw artifacts: `test-results/paid-access-hardening/windows-final-ac37325-20260929`.

- `npm run build`: PASS, including TypeScript checking, Vite bundle and Electron build.
- Full `npm test`, with process-only TEMP/TMP inside the workspace: **112 passed, 0 failed, 0 skipped**. Same default concurrent test selection; no test assertions or product code changed.
- Actual Electron `scripts/paid-access-smoke.ts`: PASS using an isolated synthetic profile and loopback fake API. Checks forged renderer denial, atomic saves/imports, global ten across languages, 101 paid cards, expired paid read/review/delete/export/backup, then offline relaunch retaining 100 cards after deliberate deletion. One fake AI call, no real AI request.

## Preserved unsuccessful executions and diagnosis

The first full run using Windows system TEMP passed107/112; five failures were EPERM while replacing a temporary SQLite-export file. A repeated full run and sequential run had failures in different tests with the same rename error. Four initially affected account-sync cases passed in isolation. This was not a clean initial test run.

A standalone Node probe, with no application/SQLite code, repeated write+rename on a synthetic file: system TEMP992/1000 replacements passed,8EPERM; workspace TEMP1000/1000 passed. Running the unchanged full tests with process-only TEMP/TMP directed to the workspace then passed112/112. The exact interfering process/root Windows cause is not established; do not claim antivirus diagnosis. No machine security setting or permanent environment configuration changed.

Logs, probe source/output, versions and source SHA are retained; top-level files have an evidence-hashes.json inventory. The fake Electron profile, screenshot, export and backup remain available. This verifies the current unchanged Windows scope; it does not accept unfinished backend/iOS work or physical/production release gates. If later work changes Windows behavior, rerun affected checks before final acceptance.
