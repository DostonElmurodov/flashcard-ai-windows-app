# Integration into main — 2026-09-30

The user explicitly requested merging all task code into main/master and pushing to GitHub. All three repositories use main. Fresh fetch showed no remote-main commits missing from the task branches.

- Windows: merged checkpoint/report branch f4b9c0f and implementation branch 40278b2 into main, merge commit fc32bd810329fee77975df57edf08fbe42c99074. No conflicts. Product paths (electron, src, shared, tests, scripts, package files and workflows) compare identically with the validated 40278b2 candidate. Its 116 passing tests, packaged restart smoke and release checksum remain applicable; integration adds reports and history only.
- Backend: clean local main first advanced to origin/main, then fast-forwarded to 5a9d1eb2a2ff2c8107572692c50e3f317070a0b8. No code changes during integration. Prior candidate application tests (811), operations tests (52) and real restored-copy migration evidence remain recorded in this directory. Main push triggers the existing build/image workflow; deploy job requires explicit workflow_dispatch mode=deploy and does not run on push.
- iOS: main fast-forwarded to 5d2d5bebfc66a63e9d12df6a271a51c133ed7be3. No code changes during integration; prior Mac validation passed 694 tests and unsigned release checks. Workflow is manual-only, so this push does not launch another Mac run.

Windows release v0.1.35 continues to point to its original validated source 40278b2, now an ancestor of main; no tag rewrite or duplicate installer build is necessary. Integrating backend source does not deploy it or activate the still-pending lifetime free quota and spending settings. Generated Python __pycache__ in the operations worktree is not source and was not staged.
