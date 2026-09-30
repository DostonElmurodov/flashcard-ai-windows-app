# Learn header controls — 30 September 2026

Approved change: place search and Add cards on the same header row as Learn. Search continues to match saved words, translations and notes; Ctrl+F focuses it. Add cards opens the selected set, offers a set chooser when All sets is selected, and opens creation when there are no sets. The lower collection filter remains. The existing empty-state Create a set action remains direct creation.

Implementation changes only App.tsx and styles.css, with a focused Electron smoke script. It reuses the existing modal and card editor. Workspace resets close the picker; no IPC, subscription or server behavior changes. A simplification pass removed duplicate lower-toolbar controls and kept existing navigation paths.

## Verification

- Final npm run build: exit0 (TypeScript, Vite and Electron build).
- Final `node node_modules/tsx/dist/cli.mjs scripts/learn-header-smoke.ts`: exit0 against an isolated synthetic profile and loopback API. It exercises word/translation/notes search, a negative query excluding the fixture word, Ctrl+F from a different focused control, selected Travel destination, chooser Food destination, Escape cancellation, two-empty-set Create a set regression, zero-set creation and 940px row/overflow geometry. Final raw output: runtime-accepted.log.
- Screenshots inspected at 940px and 1400px window widths. An initial 140px compact search clipped its placeholder; the final compact field is220px and retains a single outer border. Final 940px screenshot confirms readable search and aligned controls.
- Fresh Astra Medium review found one P2: the lower Create a set action had accidentally adopted chooser routing. Its original handler was restored and accepted by a separate fresh review; the smoke also exercises that case. Final CSS/script review is recorded separately.
- An initial smoke failed because exact `Learn` button matching ignored its due-card count. Test navigation now scopes to the sidebar and matches the Learn prefix. No product change was used to bypass the test failure. Earlier executor output without an exit marker was not accepted as evidence.
- Script review strengthened cleanup on setup/close failures and search assertions. The new Ctrl+F assertion initially raced the existing requestAnimationFrame handler; it now awaits actual focus before asserting. A real rerun passed with no product changes. Startup/close failure cleanup is source-reviewed, not fault-injection tested. Workspace-change picker reset is also source-reviewed rather than a claimed smoke scenario.

Raw build/runtime logs remain in `D:/07 Hobby/FlashcardAI/test-results/learn-header`; screenshots and synthetic profiles remain in the Windows worktree's ignored test-results directory. No real user cards, external AI calls, or Mac builds were used. These checks validate the built working copy; no installer/release is published by this change.

This task uses a fresh Terra implementer and scoped Astra Medium reviews under the existing authorization of40 agents. All earlier paid-access product work remains on the existing feature branch. Only one final push is used for this UI change.
