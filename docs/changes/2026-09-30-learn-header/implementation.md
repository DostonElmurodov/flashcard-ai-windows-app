# Learn header implementation

## Changed files

- `C:\Users\ForDo\.codex\worktrees\paid-access-hardening\FlashcardAI\src\App.tsx`
  - Moves vocabulary search into the Learn header and gives it the accessible name `Search cards`.
  - Keeps matching against word, translation, and notes; Ctrl+F still focuses the same input.
  - Adds the always-visible Learn-header Add cards action: selected set opens the existing card editor, no selected set opens a deck chooser, and no decks routes to Create a set.
  - Resets the chooser during a workspace-scope reset. The existing vocabulary empty-state action retains its original Create a set route when no set is selected.
- `C:\Users\ForDo\.codex\worktrees\paid-access-hardening\FlashcardAI\src\styles.css`
  - Adds scoped flex layout and compact minimum-width rules for the header controls, including a compact Help label at narrow desktop widths. The compact search field is 220px wide, keeps its icon fixed, and removes nested input chrome while retaining the outer search border.
- `C:\Users\ForDo\.codex\worktrees\paid-access-hardening\FlashcardAI\scripts\learn-header-smoke.ts`
  - Focused Electron coverage for word, translation, notes, and no-match queries; Ctrl+F after moving focus to the set filter; selected/all/zero-deck Add cards routes; chooser Escape; two-empty-set Create a set route; and 1400px/940px geometry. The isolated HTTP server, SQLite store, and Electron app now close independently on setup or runtime failures.

## Checks

- `npm run build` completed successfully (exit code 0): TypeScript typecheck, Vite production build, and Electron build script.
- `git diff --check` passed before the final one-line empty-state restoration; the final build then passed.
- The focused smoke was launched with `node .\node_modules\tsx\dist\cli.mjs scripts\learn-header-smoke.ts`. Its captured phase output reached `starting`, `signed in`, and `search ready`, then the tool session completed without a process exit marker, pass/fail line, or screenshot. Runtime smoke diagnosis remains with the parent reviewer; no product behavior was changed to work around it.

## Simplification

The Learn page no longer duplicates its search and Add cards controls beside the collection filter. The header uses the existing `Modal` and existing `CreateSet` route; no new persistence, IPC, or access path was added.

## Remaining risk

The successful build establishes static correctness. The focused Electron scenario still needs a definitive completed runtime result and screenshot, especially at 940px width and across a real workspace switch.
