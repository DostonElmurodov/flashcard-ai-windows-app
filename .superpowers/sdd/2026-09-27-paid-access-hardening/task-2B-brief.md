# Task 2B — backend anonymous owner, safe mobile restore and desktop-claim boundary

> Use the session's selected subagent-driven execution. This brief is authorized engineering work under the approved ordinary Apple restore behavior; historical Task 2A is superseded. One implementation/review chunk; no new product approval gate.

**Goal:** eliminate UUID/copy-only owner inheritance while enabling Apple-only mobile restore without Owl login or original key.
**Spec:** `anonymous-restore-contract.md`, frozen sections 1–6; `anonymous-restore-approved-behavior.md`.
**Base:** B `e7dfd1aa3874a724b51af126c4979d50ca8f0992`, clean at dispatch. Root's fresh full Release baseline: 628 total = 624 pass, three B1/B2 failures, one B4 skip; `test-results/paid-access-hardening/task2b-baseline-e7dfd1a`. Do not use the older 600-test baseline as current.

## Files and exact interfaces

Modify B `src/Mavrylo.Services/Services/{AppAttestRegistrationService,DeviceContextService,EntitlementService,IapService,AccountEntitlementService,SubscriptionOwnershipService,AppStoreServerClient,IAppStoreServerClient,AppleJws}.cs`; `Filters/{AppAttestAssertionFilter,AiProtectionFilter}.cs`; `Areas/OwlAI/Controllers/{DeviceController,IapController,AccountSubscriptionController}.cs`; `src/Mavrylo.Services/Dtos/{DeviceDtos,IapDtos}.cs`; `src/Mavrylo.Entities/Models/{DeviceEntity,SubscriptionEntity}.cs`; `src/Mavrylo.Data/AppDbContext.cs`; DI in `Program.cs`.

Create services `AnonymousPurchaseService.cs`, `MobilePurchaseAccessResolver.cs`, `AppleMobileRestoreVerifier.cs`, `AppAttestProofService.cs`; entities `AnonymousPurchaseOwnerEntity.cs`, `AnonymousPurchaseTokenEntity.cs`, `ApplePurchaseBindingEntity.cs`, `MobilePurchaseGrantEntity.cs`; additive migration `AddAnonymousPurchaseRestore` plus snapshot/designer. These names and the DTO/service signatures are specified in contract sections 2–5. Keep `IapService` public service signatures as delegates so existing controllers/tests can migrate deliberately. Avoid a second independent Apple certificate verifier or receipt framework.

Tests: create `tests/AnonymousPurchaseOwnershipTests.cs`, `tests/MobilePurchaseRestoreTests.cs`, `tests/AppAttestCounterConcurrencyTests.cs`, `tests/AnonymousPurchaseMigrationTests.cs`; extend `PaidAccessAuditRegressionTests`, `AccountClaimProofTests`, `SharedSubscriptionTests`, `SharedSubscriptionPostgresTests`, `AppStoreServerClientTests`, `AppleJwsTests` and fake Apple evidence provider as needed. All real concurrency evidence uses isolated PostgreSQL and separate contexts/process-equivalent connections, never an in-memory substitute.

Produces: stable server owner/binding/grant DTOs; `/app-attest/resume`, `/iap/purchase-context`, `/iap/restore`; mobile resolver record and unchanged exact raw-body assertion v1. Task 3B consumes these names. Task 6 consumes server-resolved device/owner/purchase IDs only; this chunk does not claim to close B4 or introduce quota values.

## RED → GREEN steps

- [ ] Write/execute focused RED at this base: fresh valid key + victim UUID/token/raw original/app ID remains free; copied JWS alone neither transfers guest purchase nor first-claims desktop. Preserve the existing three failing safe B1/B2 expectations. Positive controls must establish real new server token/owner context in fixtures rather than restoring the unsafe UUID join.
- [ ] Add RED for two same-counter valid assertions on distinct challenges across two contexts: at most one accepted; out-of-order lower counter cannot overwrite a higher counter. Repeat registration preserves public key/environment/owner/library/counter and usage references; lost response/resume preserves identity. Malformed/tampered body/path/key fails before business mutation.
- [ ] Implement contract schema and registration/resume/proof CAS. Bootstrap assignments are server-generated. Apply migration against a populated isolated copy: account claims, tombstones, invalid-environment quarantine, paid history and all existing counters survive. No UUID-derived first owner claim.
- [ ] Add RED new purchase and notification ordering: stable context token; valid signed mapped token + matching installation owner binds once; unknown/foreign/missing token cannot strong-bind. Notification-first and simultaneous valid verify/restore cannot move owner; repeated/older Apple events cannot revert revocation. Wrong refreshed original ID persists nothing.
- [ ] Implement owner verify and state-only Apple upsert while preserving Task 4 chain/environment/product/original-ID/expiry/event-order tests. AppTransaction verification uses `receiptCreationDate`, `receiptType`, production appAppleId and paired device hashes. Signed missing token is allowed for mobile/lifecycle only; no generated replacement token is sent to Apple.
- [ ] Add/execute RED mobile restore with new key and no Owl/original secret: matching active Apple envelope grants mobile; original phone stays authorized; same evidence retry needs fresh assertion but is idempotent. Different app/device/env/product evidence fails; missing optional appTransactionId takes documented paired-device legacy route; missing mandatory device fields fails explicitly. Test full-envelope residual as limited authority, not as an impossible-to-pass rejection.
- [ ] Cover historical restore: expired paid returns sticky history with no active grant/AI; revoked does not become free; no Apple response produces no new binding/grant/extension; new canonical revocation wins a stale in-flight restore. Grant never outlives verified canonical period and requires local evidence to extend.
- [ ] Cover desktop isolation: original owner + intended account first claim succeeds once; competing accounts serialize to one winner; linked account idempotent; foreign claim/tombstone returns existing 409; restored-only installation returns 403 `desktop_owner_proof_required`; login/logout does not remove mobile rights. Account deletion preserves mobile binding/tombstone/history and private account data stays inaccessible without session.
- [ ] Run focused checks, relevant existing ownership/Apple/normal-mode regression suites, full Release tests and Release build; document remaining B4 skip only if still pending. Simplify changed code without changing contracts; obtain fresh scoped review, resolve findings, rerun affected checks, commit only this chunk. Report exact source SHA and test counts, not “all secure”.

Commands from B (use root's existing local-test DB environment; never print connection secrets):

```powershell
dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AnonymousPurchase|FullyQualifiedName~MobilePurchaseRestore|FullyQualifiedName~AppAttestCounterConcurrency|FullyQualifiedName~PaidAccessAuditRegression|FullyQualifiedName~AccountClaimProof|FullyQualifiedName~SharedSubscription|FullyQualifiedName~AppleJws|FullyQualifiedName~AppStoreServerClient"
dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore
dotnet build Mavrylo.csproj -c Release --no-restore
```

Migration scaffolding tool already exists at `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/tools/dotnet-ef.exe` (10.0.9), Data project + Web startup. No dependency reinstall, global safe.directory change, production call, new live budget or deployment. Root controls branch push/Mac workflow; this chunk is backend local.

## Review focus / acceptance

1. Authorization joins contain no DeviceUuid/appAccountToken/raw-ID shortcut; restored grant cannot reach owner enrollment/rotation/claim/account data.
2. Migration does not promote historical UUID association to strong owner proof. Unproven legacy first desktop claim remains explicit recovery; mobile restore succeeds.
3. Same-key concurrent/retry registration never lowers assertion high-water or resets identity; all assertion consumers share CAS verification.
4. Signed AppTransaction local fields and app ID are verified; absence of a token or newer optional field has the documented honest fallback; fake/local bypass never reaches Release.
5. State events/claim/delete/restore lock ordering and rereads preserve tombstones, last Apple event, owner and originalID. Full-envelope residual remains accurately stated.

Done means scoped independent review accepts these behaviors with evidence. It does not mean real Apple restore or migration deployment has passed. Local engineering estimate: 12–18 hours including migration/concurrency tests and review; physical Apple evidence and production migration remain separate gates.
