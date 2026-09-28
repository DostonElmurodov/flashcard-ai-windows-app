# Task 2B.1 — immutable installation foundation and atomic App Attest proof

> Fresh implementer, then fresh scoped reviewer. Authorized local work. This is the first bounded slice of `task-2B-brief.md`; `anonymous-restore-contract.md` sections 1–6 remain wire/schema authority. No product approval or behavior redesign.

**Base:** backend `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, `e7dfd1aa3874a724b51af126c4979d50ca8f0992`. Root verified clean. Exact full baseline 628 total / 624 pass / three B1/B2 failures / one B4 skip, evidence in `test-results/paid-access-hardening/task2b-baseline-e7dfd1a`.

**Deliverable:** new immutable server installation identity, additive schema for the next slice, working same-key session resume, and a single atomic assertion verification boundary. **B2 purchase transfer/first desktop claim and B4 remain open after this slice.** Existing UUID-based resolver/IAP paths are replaced in 2B.2; this intermediate commit is not deployable.

## Exact scope

Create all four entities/tables and Device/Subscription metadata additions from contract section 2, `AddAnonymousPurchaseRestore` migration/snapshot and populated-copy migration tests. Backfill independent installation owners and legacy-unproven purchase owners/bindings. Do not enroll a device into a purchase owner through legacy UUID, import overwritten UUID as token, populate mobile grants, or clear existing account claims/history. Schema is staged here so 2B.2 adds no competing migration for these fields.

Modify `src/Mavrylo.Services/Services/AppAttestRegistrationService.cs`: ignore caller DeviceUuid for new registrations; server generates compatibility namespace and AnonymousOwnerId once in the same transaction. Preserve exact public key/environment/owner/UUID/SignCount on authenticated same-key retry; reject mismatched material. Concurrent insert race must reread winner and compare immutable data. Challenge replay cannot create another owner or mint an unauthenticated resume token.

Create `src/Mavrylo.Services/Services/AppAttestProofService.cs` with frozen signature:

```text
VerifyAsync(string keyId, byte[] rawBody, string path, string challengeId,
            string assertionBase64, CancellationToken ct)
  -> Task<AppAttestProofResult>  // bool Ok, string? Code
```

Reuse `ChallengeService`, `IAppAttestVerifier`, `AppAttestClientData`; do not change canonical v1 preimage or two-minute challenge TTL. Atomically consume challenge, cryptographically verify stored public key/counter and conditional-update counter from observed old value to greater new value. A losing CAS returns `invalid_device_proof`; no business action. Do not wrap proof counter consumption in a later business transaction that might roll it back.

`Filters/AppAttestAssertionFilter.cs` and the private assertion path in `Filters/AiProtectionFilter.cs` both call this service, preserving existing bounded exact-body buffering, multipart validation and required headers. `AccountClaimProofFilter` continues to reuse the IAP assertion filter; verify it reaches the shared service once. Keep existing DEBUG+Development simulator isolation. Do not alter commercial gates or accepted Task6 validation ordering in this slice.

Add `POST /owlai/app-attest/resume` in `Areas/OwlAI/Controllers/DeviceController.cs`: exact `{}` request, existing key/challenge/assertion headers, no JWT required because assertion supplies authentication. Known registered key + fresh proof returns existing `AppAttestRegisterResponse`; no new device/owner. Unknown key: 401 `invalid_device`; rejected proof: 403 `invalid_device_proof`; no-store. Add `ResumeAsync(string keyId, byte[] rawBody, string path, string challengeId, string assertionBase64, CancellationToken ct): Task<AppAttestRegistrationService.DeviceResult>` to registration service; controller reads/rewinds max 16,000-byte raw body before interpreting `{}`, rejects nonempty fields/duplicate fields, and passes exact bytes. Register retains 128,000-byte existing limit. Register/result maps use frozen error contract.

Modify `src/Mavrylo.Entities/Models/{DeviceEntity,SubscriptionEntity}.cs`, `src/Mavrylo.Data/AppDbContext.cs`, appropriate DI in `Program.cs`. Do not modify iOS, Windows, workflows, IapService, AccountEntitlementService or Apple lifecycle behavior. The next slice owns those integrations. No new dependencies or live calls.

## Checkable steps and acceptance

- [ ] Write `tests/AnonymousInstallationTests.cs`: real bootstrap-service path with accepted attestation creates server UUID unequal to victim UUID and separate owner; rerun existing `B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid`. Assert original purchase owner/device data unchanged and victim rights not inherited. Run RED at base before implementation.
- [ ] In the same suite add same-key retry, concurrent registration, changed public key/environment, lost response + assertion resume, unknown key resume, reused bootstrap and supplied token/raw-ID variants. Assert exactly one device/installation owner, unchanged high-water, no grants/claims and no usage reset.
- [ ] Add `tests/AppAttestCounterConcurrencyTests.cs` using separate PostgreSQL contexts: two assertions with same new counter/different fresh challenges authorize at most one; a delayed smaller counter cannot lower a larger committed counter; repeated challenge authorizes once; forged body/path/key authorizes zero. Exercise both filter consumers and resume, not just the helper. Confirm RED for current read/write race.
- [ ] Add `tests/AnonymousPurchaseMigrationTests.cs` populated fixtures: applicable and quarantined subscriptions, multiple devices sharing old UUID, linked/tombstoned accounts, day/minute usage, saved words. Assert independent owners, legacy-unproven bindings only, no grants, no deleted rows/reset counters, preserved original IDs/environment/claim timestamps/paid history. Apply migration/startup again idempotently. Migration connection must be isolated local test PostgreSQL.
- [ ] Implement schema/registration, shared proof CAS and resume; execute targeted GREEN, existing App Attest/raw-body and multipart boundary tests. The observed B1 regression must turn green without editing its safe expectation.
- [ ] Run full Release suite/build; record all remaining failures by name. Expected pre-existing B2 failures and B4 skip stay explicitly open; do not call this slice “all tests green” if they remain. Any additional failures need correction or a precise safe fixture contract update, never an assertion inversion.
- [ ] Simplify changes, obtain fresh scoped review, fix findings, rerun affected checks and commit only this slice. Hand off schema/DTO/proof service source SHA to 2B.2.

Commands from B (existing isolated test DB setup; do not print secrets):

```powershell
dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AnonymousInstallation|FullyQualifiedName~AppAttestCounterConcurrency|FullyQualifiedName~AnonymousPurchaseMigration|FullyQualifiedName~B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid|FullyQualifiedName~AccountClaimProof"
dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore
dotnet build Mavrylo.csproj -c Release --no-restore
```

Use existing local dotnet-ef 10.0.9 only if needed: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/tools/dotnet-ef.exe`; Data project, Web startup. No global configuration changes, production, push or deploy. Root owns any authorized remote steps. Rough local effort 4–7 hours; schema correctness/counter races determine readiness, not elapsed time.

Frozen for foundation dispatch. Review must explicitly verify raw bytes unchanged, registration preserves high-water, consumed proofs never roll back with business failure, migration makes no legacy ownership inference, and B2/B4 remain honestly open.
