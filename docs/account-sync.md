# iPhone accounts and Windows synchronization

Windows signs in existing iOS-enrolled accounts using `/owlai/account/desktop/email/session` or `/owlai/account/desktop/google/session`. Neither route creates an account. New registration and legacy account enrollment require real iOS App Attest proof on the server. Google identity is linked by its verified provider subject; an email match alone does not merge accounts.

The signed-out library remains in `owl.sqlite`. Each API origin and immutable account ID gets a different database under `accounts/`. Signing in never uploads the signed-out library. iOS offers an explicit, account-named confirmation for copying its guest cards into an account. Signing out returns to the original guest workspace; returning to an account reopens only that account's cache. Backups are owner-marked and restore only into their original account/origin or guest workspace. Account credentials are stored separately using Windows secure storage and never included in backups.

Sync runs on login/startup, every 30 seconds, and Profile → Sync now. Collections, cards, both directional review schedules, repetitions and lapses synchronize. Individual word languages are retained for mixed-language iOS collections. Incomplete words with no translation remain visible but are excluded from Windows review until translated. iOS additional collection memberships are preserved in metadata. Preferences, historical review logs and daily/streak charts remain device-local.

Server changes use optimistic entity versions and persistent deletion tombstones. A conflict rejects the entire submitted batch without overwriting either device. Local edits made during a request survive its response. Up to 500 changes are sent per request, with parent creation before children and parent deletion last. Unsent changes keep their old versions, so later batches cannot silently overwrite concurrent edits. Capacity errors preserve local data; they never truncate a snapshot.

Profile → Use cloud version requires confirmation. A local backup is saved beside the account database as `<hash>.sqlite.before-cloud-<timestamp>.sqlite` before replacing the local library. It can be selected in Settings → Restore backup while signed into the same account. Windows' default data folder is `%APPDATA%/owl-ai-windows`; portable or test profiles may use a different directory. Other accounts and the guest library remain unchanged.

The main process validates account scope on every data IPC call and rejects results that finish after a scope change. The renderer activates a new scope only after replacing private UI state. Sync checks response ownership, aborts before database changes on logout, and separates account-switch generations from the stable preference namespace. Native dialogs pin the workspace until completion.

## Rollout

1. Deploy the companion backend with existing shared-subscription migrations followed by `20260918165609_AddIosAccountSync`. See `../../mavrylo/docs/account-sync-api.md`; keep App Attest and active-session verification enabled.
2. Build and test the companion iOS source on macOS/Xcode. Verify enrollment on a real iPhone, Google/email sign-in, explicit guest import and two-device synchronization.
3. Build Windows with `npm run build`, then package only after the real-device checks. Existing 0.1.27 installers are from the previous build and do not contain this feature.

No production deployment or iOS distribution was performed by this change. A Windows build alone cannot enable the new server routes. Automated tests cover account isolation, versions, null/empty cross-platform values, late responses and UI behavior; they do not replace real App Attest or an iPhone/Windows end-to-end test.
