# Learn header scoped source review

Reviewed 2026-09-30 against HEAD ac37325c3cae2cef5d2adaddbaa214f213a25892 in the paid-access-hardening worktree. Scope: dirty src/App.tsx and src/styles.css, with necessary existing navigation, Modal, and ink-indigo.css context. No product edits, builds, or Git mutations performed.

## Finding

- P2, src/App.tsx:69: Restore the original vocabulary empty-state handler. With at least two empty sets, All sets selected, and an empty query, the button still says "Create a set" but its new openLearnAddCards handler opens the set chooser. Previously it opened creation directly. The approved header behavior only requires the new dispatcher on the header Add cards button. Root has confirmed this finding and requested restoration; the reviewed snapshot still contained it.

## Other checks

The moved search retains its controlled query, ref, word/translation/notes matching, and Ctrl+F listener; its accessible name improves. Selected-set Add cards uses the existing add route; zero sets uses create; an unselected multi-set state opens the existing native dialog pattern. Picker state resets on workspace revision changes, and card mutations still use the workspace-scoped bridge. The lower set filter remains. No additional blocking functional or access-boundary regression found in this scoped source review.

CSS review included ink-indigo.css, loaded after styles.css. Its existing heading flex rules remain in force; the new 1050px Learn-only compact selectors have higher specificity. The 940px result still needs the coordinator's actual screenshot/runtime check; this report does not claim visual validation or execution of the smoke test.
