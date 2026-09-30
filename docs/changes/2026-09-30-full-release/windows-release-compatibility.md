# Windows 0.1.35 publication compatibility — 2026-09-30

User explicitly requested commit, push and update of the Windows GitHub Releases page. A clean-context GPT-6 Astra Medium reviewer compared Windows `40278b212553dd3d01109c5e37d1a15c9b89b319` against backend source `473a39bee299f3df4c7fdcf6f45502f880a9bd8b` and found no API compatibility reason to delay this full Windows client release until the separate backend security rollout.

The old backend already has both desktop sign-in routes and the required snake_case entitlement fields. Windows supplies its own confirmation timestamp, uses existing API routes and sync payloads, and handles newer refusal codes compatibly. Sync scheduling, Learn toolbar and remembered entry method are local changes. All client hardening remains in the installer; no UI-only alternate build was substituted.

Read-only live checks returned health 200 and GET 405 for both existing POST-only email session routes. These checks and source review do not prove authenticated production sign-in, entitlement or synchronization; no user credentials were used.

Known limits: old server null-expiry premium rows are intentionally rejected by the hardened client; malformed records need reconciliation. Old server revoked-to-expired_paid classification cannot be corrected by the client. Server ownership, quota, reconciliation and spending protections still require the separate deployment. Release notes explicitly state that the 10-operation lifetime free allowance is not yet active on the server.

This evidence supersedes the earlier assumption that any Windows publication must wait for the backend cutover. It does not mark the broader server or Apple release work complete. This focused reviewer was agent 44 of the user-authorized 60.
