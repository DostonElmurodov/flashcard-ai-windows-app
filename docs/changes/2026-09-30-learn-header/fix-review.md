# Learn header fix source review

Reviewed 2026-09-30 against HEAD ac37325c3cae2cef5d2adaddbaa214f213a25892 in C:\Users\ForDo\.codex\worktrees\paid-access-hardening\FlashcardAI. Scope: current uncommitted src/App.tsx and src/styles.css; bounded follow-up to review.md. No product edits, builds, runtime tests, or Git mutations performed. The changing smoke script was not reviewed.

## Result

The previous P2 finding is closed. At src/App.tsx:69 the vocabulary empty-state button again uses `()=>selectedDeck?addCards(selectedDeck):go('create')`. With All sets selected and multiple empty sets, its Create a set label therefore navigates directly to creation. A selected empty set still opens Add cards for that set.

The new openLearnAddCards dispatcher is used only by the Learn header button. The Learn header contains both Search cards and Add cards; the lower vocabulary toolbar retains the set filter. The search keeps its controlled query and searchRef, and the filtering expression remains unchanged. The workspace revision reset still clears the query and existing transient state and now also closes the set picker. Workspace-scoped bridge handling remains unchanged.

No additional blocking source finding in this narrow follow-up. The CSS diff remains limited to header/search/picker layout and the compact Learn header rule. This is a source review only; screenshot, responsive layout, and smoke-test verification remain with the coordinator.
