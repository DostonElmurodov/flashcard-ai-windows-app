# Legacy migration: unresolved recovery versus free fallback

Root read-only follow-up to Task2B.2 review Declined item 6. This records a local implementation/coverage gap, not approval of a new ownership policy. Inspected backend faeec095aed5ca49af3e80404ae95abd9bee6341 and iOS f6a9e194505dfd27ec28b3fbde11bfcb8eca095e. The ongoing Task2B.2 fix round is separate.

## Source-supported path

- Foundation migration gives each existing installation its own owner and each historical purchase a separate `legacy_unproven` owner/binding. It intentionally does not infer purchase ownership from historical `DeviceUuid`.
- `MobilePurchaseAccessResolver.ResolveAsync` enumerates proven owner bindings and device grants. With neither, it returns `free`, regardless of the existing device's `RequiresAccountSubscription` marker.
- `DeviceContextService.ResolveAsync` uses that result. Without an authenticated account header, it does not replace free with an account/recovery result.
- `AiProtectionFilter` permits free through its free-word and usage checks. Thus a historical marker alone does not preserve an inactive/recovery denial. This is source tracing, not a newly executed HTTP/provider-call reproduction.
- At the inspected iOS base, `EntitlementStore.update` can replace saved device entitlement with a device-source free response. Task3B already requires empty discovery and outages to preserve confirmed paid history; its tests must include migration-era free responses, not only a thrown network error.

## Required disposition before whole-feature acceptance

1. Reproduce on populated old-schema fixtures through migration, host startup, real authenticated token/AI routes, and iOS refresh. Separate claimed/deleted-account markers, paid/trial history, truly free installs, empty Apple discovery and Apple outage. Count provider calls, grants/bindings, preserved library/history and any recovery marker.
2. Preserve the approved no-login ordinary mobile restore. Do not make account login, a surviving old key, or support a prerequisite for legitimate Apple mobile restore.
3. Never use UUID/token correlation to grant paid authority, import an owner credential, or expose private account/library data. A conservative deny/recovery marker is a different decision from granting authority, but its provenance, false positives, clearing conditions and migration idempotency need an explicit implementation/test decision.
4. Proven new owner/grant history must stay sticky. A successful applicable full-envelope restore may establish finite access/history; a failed or empty check must not silently reset a known paid restriction into new free AI.
5. Preserve legitimate genuinely free installs and retain cards/history. Production inventory is necessary for rollout but cannot substitute for the local behavior/test decision.

This concerns retained server/local evidence on a known installation. It does not claim that a wholly new anonymous identity with no surviving marker and no Apple evidence can be recognized as the same prior person; that residual remains bounded by the approved aggregate spend policy.

Route the client portion to Task3B, the server/migration portion to the unfinished Task10 migration/readiness work, and the final decision/results to the separate whole-feature review. Do not silently label this closed by B1/B2 or the 686 passing tests. Do not expand the current Task2B.2 I1–I3/M1–M3 fix round without a concrete dependency.
