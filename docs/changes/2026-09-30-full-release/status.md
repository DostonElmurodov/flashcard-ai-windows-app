# Full release preparation — 2026-09-30

The user authorized deploying all paid-access/security changes and publishing the Windows release, including Learn search and Add cards. A UI-only release is not the chosen scope.

## Verified candidate

- Backend feature head before deployment tooling: `a6e2362c8e624338c564bfd88499cd1835c956c1`, 811 local tests passed.
- Windows release commit: `5ddeede`, version 0.1.35; 112 tests, installer build and packaged Learn smoke passed. Independent candidate review has no blocking scoped finding. See the saved report and review.
- iOS source: `5d2d5bebfc66a63e9d12df6a271a51c133ed7be3`; Mac run `36730138820` passed 694 tests and unsigned Release checks. No signed-device or App Store release is claimed.

## Deployment progress

Last successful API deployment workflow remains `36259043891` at source `473a39bee299f3df4c7fdcf6f45502f880a9bd8b`. Public health answered successfully and public feature flags reported `test_mode: true`. These observations do not prove the actual container digest or effective database state.

A read-only SSH preflight mode was committed and pushed as `0c1b99a` after correcting a multiline environment-value boundary defect and passing independent re-review. Run `36743325476` succeeded, with build/push and deploy skipped. It confirmed the running source revision above and Apple Sandbox plus test mode enabled. See [live metadata](server-preflight.md). No server cutover, migration, or Windows release publication has occurred in this preparation step.

Outstanding: runtime/configuration inventory, aggregate database inventory, backup/restore rehearsal, evidence-based handling of any historical usage/ownership issues, approved AI limits and spending envelope, exact-image deployment verification, then Windows publication. Existing subscriptions cause the new usage migration to require reconciliation; never bypass that by erasing issues or blindly changing Ready.

## Agent accounting

This continuation used agent 31 for the clean preflight review and agent 32 for the aggregate database inventory implementation, within the user's allowance of up to 40. Agents 27–30 covered Windows packaging, backend preflight, read-only workflow implementation, and Windows review. Follow-ups reuse those agents. Root owns commits, pushes, remote execution and publication.
