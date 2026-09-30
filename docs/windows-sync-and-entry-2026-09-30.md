# Windows sync cadence and Add cards preference — 2026-09-30

Approved scope: sync at startup/sign-in; focused visible window with pending additions, edits, deletions or review progress every 20 minutes; focused clean window every 6 hours; unfocused/minimized/tray every 12 hours. Manual Sync now remains available. Focus and review completion no longer force an additional request. Existing subscription/feature-flag requests are separate and were not changed.

The active pending deadline starts at the first unacknowledged change or last attempt, whichever is later; later edits do not postpone it. Inactive checks anchor to the last attempt regardless of later edits. A due scheduled tick may run after regaining focus, but focus itself does not force a sync. Failed attempts preserve pending work and use the same cadence. Conflicts pause automatic checks. The existing persisted cursor, exact acknowledgements and incremental record exchange remain unchanged.

Add cards remembers manual/AI/paste/file selection per workspace on this computer, including restart. Invalid or unavailable storage defaults to manual. Draft text is transient; restoring AI selection performs no AI request and does not bypass access checks.

Verification: full suite 116/116 passed after the final fix; typecheck passed. Initial system-TEMP test run had two EPERM rename failures in existing storage tests; the full suite passed with TEMP/TMP scoped to a task directory. Electron UI smoke passed reopening and process restart, checked draft text is empty and zero automatic AI calls. Build verification and packaging status are recorded in the parent release report.

Independent clean-context GPT-6 Astra Medium review found one P2: a late edit postponed the inactive check from hour 12 to hour 17. Fixed by anchoring inactive cadence solely to the last attempt. The reviewer independently ran the regression and closed the finding; no remaining blocking findings. Both implementation tasks received a simplification pass. No server deployment or GitHub release was performed by this change.
