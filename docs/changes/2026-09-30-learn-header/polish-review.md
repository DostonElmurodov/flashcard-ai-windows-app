# Learn header polish review

Reviewed 2026-09-30 in C:\Users\ForDo\.codex\worktrees\paid-access-hardening\FlashcardAI, against the coordinator-provided HEAD ac37325c3cae2cef5d2adaddbaa214f213a25892. Scope: final search CSS polish and frozen scripts/learn-header-smoke.ts. Read-only source review; no product edits, builds, runtime tests, or Git operations performed.

## CSS result

No actionable CSS finding. The 220px compact width is Learn-scoped; the nonshrinking icon selector uses the Learn-only search class. The input reset has greater specificity than the later ink-indigo.css search rule, and its important declarations override the shared input background/border/padding. The changed search rules do not match other pages. Runtime screenshot and minimum-width verification remain with the coordinator's final rerun.

## Actionable smoke-test findings

- P2, scripts/learn-header-smoke.ts:18-21,76: Resource cleanup starts too late and can be skipped. The HTTP server is already listening when Store.open/saveSettings or electron.launch can reject, before entering try/finally. main().catch only sets exitCode, so that failure leaves the listening server alive and the command can hang. Also, rejection from app.close skips server.close. Put resource acquisition under an outer cleanup scope and close the server in a finally independent of app.close; close the store in its own finally.
- P2, scripts/learn-header-smoke.ts:32-36: The search/shortcut checks can pass when the behavior they claim to verify is broken. There is only one matching fixture card, so an unfiltered list passes both search assertions; no direct word query is made despite the final PASS claim. Ctrl+F is pressed immediately after search.fill, which already focuses the search, so removing the shortcut handler still passes. Add a nonmatching-query assertion, explicitly exercise the word query, and focus another control before pressing Ctrl+F. These are corrections to existing claims, not additional feature scope.

The selected Travel route, Food chooser route, Escape dismissal, empty-set creation routes, and 940px header alignment/overflow assertions are meaningful for their stated narrow scenarios. No whole-feature rereview requested.
