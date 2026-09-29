# Task 2B.2 independent restoration review

**Spec compliance: FAIL. Task quality: Needs fixes. Ready to merge/accept this chunk: No.**

Reviewed the complete 35-file range `72c072d8d6a0cf798a831a96ed00d2023103879c..faeec095aed5ca49af3e80404ae95abd9bee6341`, including the paused `fae7af6` work, against the restoration brief, frozen anonymous-restore contract sections 1–6, umbrella brief, named-risk preparation, foundation/fix1 reviews, and final-review carryover. The completed frozen implementation report and 117-file manifest arrived before this verdict. This is a local backend review, not a physical Apple, whole-feature, or release verdict.

## Strengths

- Production authorization callers no longer use `FindForDeviceAsync(DeviceUuid)`. New registration namespaces remain server assigned; mapped token ownership, owner bindings, and mobile grants are distinct. Both old UUID relink branches were removed. The historical helper remains but its remaining callers are tests.
- Strong verify rejects copied-only foreign/legacy proof before purchase writes. The B1/B2 safe 403/no-transfer expectations remain intact; lawful positive fixtures generally now seed or issue actual server tokens and owner bindings.
- Ordinary restore does not require Owl login or the original key. Each local device hash is checked separately using fixed-time comparison, explicit AppTransaction decoding reuses pinned-chain/ES256 verification, and a mobile grant does not alter installation ownership. Existing binding/grant FKs and transaction boundaries preserve tombstones and owner identity.
- The shared exact-byte App Attest preimage and CAS counter implementation are unchanged. HTTP restore replay coverage uses a real synthetic ECDSA assertion, not just an always-true verifier. The new raw-body validation runs before model binding, and pre-auth middleware covers no-store on early failures and trailing route forms.
- Event ordering protects the actual subscription state and mobile restore from stale revocation reversal. State-only notifications/refreshes do not manufacture grants. Deletion preserves grants/bindings/claim timestamps; actual account sessions still gate private account data.
- The final report distinguishes original RED runs, post-implementation/reversion checks, fake canonical Apple responses, synthetic certificates/assertions, and physical-device limits. Its warnings and remaining B4 skip are accurately reported.

## Issues

### Critical

None demonstrated. The Important findings below must be corrected before accepting this task; no owner-transfer or private-data exploit is claimed from the local probes.

### Important I1 — first claim can persist after a newer revocation has already won

**Location:** [AccountEntitlementService.cs:76](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AccountEntitlementService.cs:76>), and the ownership/device writes at lines 81–84. The early active check is at lines 54–57.

**Failing scenario:** a legitimate original-owner first claim fetches active canonical Apple state at T1. Before its purchase transaction, a newer revocation at T2 is persisted. Under the lock, `EntitlementService.UpsertAsync` correctly returns the newer revoked row, but ClaimAsync discards that return value. It then writes `OwnerAccountId`, `ClaimedAt`, and `RequiresAccountSubscription=true`, commits, and returns 200 with a revoked entitlement.

**Observed evidence:** the in-memory probe in Appendix A invoked the final compiled service with this stale/newer ordering and observed exactly `200`, response `revoked`, persisted owner, non-null claim timestamp, and changed device flag. Revocation itself stayed intact. This is a deterministic saved-state/service reproduction on SQLite, not a PostgreSQL concurrent-race test; inspection of the PostgreSQL reload-under-advisory-lock path shows the same discarded return value.

**Impact:** an inactive purchase becomes permanently account-linked despite the required `402 subscription_inactive`/no-first-claim boundary. No paid AI is revived by this probe, but a subsequent account deletion leaves an ownership tombstone for a claim that should have been denied.

**Minimum correction:** use the persisted row returned by UpsertAsync; while retaining user-before-purchase lock order, recompute activity from that row immediately before first ownership/device mutation. Deny inactive state without those writes, while preserving an appropriate already-linked-account idempotent path. Add a real RED/GREEN test that holds an older canonical result while a separate PostgreSQL context commits a newer revoke, then verifies status and all non-mutation fields.

### Important I2 — verify/restore mix a selected entitlement with a different request purchase

**Locations:** [AnonymousPurchaseService.cs:79](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AnonymousPurchaseService.cs:79>)–85 and [AnonymousPurchaseService.cs:168](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AnonymousPurchaseService.cs:168>)–177. Compare the selected record at [MobilePurchaseAccessResolver.cs:42](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/MobilePurchaseAccessResolver.cs:42>).

**Failing scenario:** the installation owns expired purchase A and has an active, linked mobile grant for purchase B. Verifying/restoring A legitimately selects B as the effective entitlement. Both actions nevertheless mint JWT `otid=A` and use A's `ClaimedAt`. Verify hard-codes authority `owner`/source `device` and omits B's grant expiry; restore uses A's local `validUntil` for `mobile_grant_expires_at`.

**Observed evidence:** Appendix B produced B's premium product and expiry in both responses. Verify reported `owner/device`, no grant expiry, `purchase_is_linked=false`, and `otid=A`; the actual selected source was a linked mobile grant B. Restore reported `mobile_restore` but supplied A's already-past grant expiry, false linked status and A's JWT identity while the selected B grant was valid for another five days.

**Impact:** the frozen wire contract provides inconsistent authority, link and renewal hints to the mobile client. A client can suppress necessary mobile-grant renewal because verify falsely says owner, display an expired grant alongside premium entitlement, or offer the wrong desktop-link state. JWT entitlement/otid are not currently authorization proof, so this does not by itself bypass the server gate; it is still incorrect source selection and a dangerous basis for the next client integration.

**Minimum correction:** build entitlement, authority, selected purchase ID/link flag, and grant expiry together from the same selected access/context record for all IAP responses. Use the processed purchase only for its persistence outcome. Cover owner A versus granted B, granted A versus owner B, and two grants, including expired/revoked A, for verify/restore/token consistency.

### Important I3 — verified token drift is silently discarded instead of reconciled

**Locations:** [EntitlementService.cs:94](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/EntitlementService.cs:94>)–97, [DeviceContextService.cs:54](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/DeviceContextService.cs:54>)–57, and [AnonymousPurchaseService.cs:64](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AnonymousPurchaseService.cs:64>)–74.

**Failing scenario:** purchase P is bound with token T1. A newer signed local transaction and matching canonical response both contain an unknown T2. The local/canonical equality check passes, and existing owner binding authorizes verify. State upsert updates the period but keeps the old non-null metadata T1, with no error, conflict record, or reconciliation signal.

**Observed evidence:** Appendix C returned 200/owner, advanced lifecycle expiry, retained T1 in both subscription metadata and binding, and lost the observation that current Apple evidence carried T2. Full source inspection found no separate drift audit/quarantine path. Binding ownership does remain immutable; this finding is not a claim that T2 transfers it.

**Contract/impact:** frozen section 2 explicitly requires unknown token changes to be recorded/quarantined for reconciliation, never treated as transfer; the restoration brief likewise requires new binding conflicts to reconcile. Silent retention disguises conflicting Apple correlation as ordinary success and removes the evidence needed to diagnose it. The existing test only covers local token != canonical token, not both differing from stored metadata/binding.

**Minimum correction:** detect conflicting non-null verified token metadata against the persisted token/binding under the purchase lock, preserve the original owner/token, and implement an explicit, durable reconciliation disposition (and stable code where the operation cannot proceed). Apply it consistently to verify, restore, notification and refresh; missing legitimate tokens must remain allowed. Test T1/T2 drift, ordinary repeated T1, missing tokens, and legacy-unproven bindings without upgrading authority.

### Minor M1 — two remaining fixtures no longer exercise their intended contract

- [SubscriptionLifecycleTests.cs:243](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/tests/SubscriptionLifecycleTests.cs:243>) `IapCachedRefreshCannotPersistUnrelatedCanonicalPurchase` creates only a UUID-associated purchase. The new resolver selects no purchase, so RefreshIfNeededAsync returns before the fake mismatched response is consumed. The assertions pass even if the mismatch guard were removed. Seed the legitimate owner mapping and assert a refresh call as well as no B persistence. **Evidence: source-only control-flow inspection; the recorded final test passes, but its intended boundary is not reached.**
- [AppStoreServerClientTests.cs:36](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/tests/AppStoreServerClientTests.cs:36>) in the DEBUG-only successful local projection fixture still sends `appAccountToken="device-from-token"` and expects it in `Subscription.DeviceUuid` at line 44. New projection rejects this non-GUID and deliberately clears DeviceUuid. Migrate the lawful fixture to a GUID and assert the separate AppAccountToken metadata/empty legacy namespace. **Evidence: source-confirmed contradictory assertions; the supplied Release run excludes this branch, and no fresh Debug run was performed.**

The other reviewed fixture changes retain inactive 402/provider-zero, quarantine, sticky history, private-account and tombstone assertions or change account-required mobile expectations according to the approved behavior. This note is not a reason to restore UUID authority.

### Minor M2 — restore's transaction branch does not enforce the required signed date

**Locations:** [AppleJws.cs:92](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AppleJws.cs:92>), [AppStoreServerClient.cs:420](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AppStoreServerClient.cs:420>), [AppleMobileRestoreVerifier.cs:27](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AppleMobileRestoreVerifier.cs:27>).

The new AppTransaction path requires receiptCreationDate, but a validly signed transaction with no signedDate still uses current certificate time, projects LastAppleEventAt=null, and is accepted by the paired verifier if its other fields match. Frozen section 4 explicitly says missing signed dates are rejected. There is no signature bypass, and finite canonical expiry/order protections limit the practical impact, hence Minor. Add an explicit required/valid signedDate check for the strict restore transaction evidence and a genuinely signed synthetic missing-date case. Preserve the existing transaction/notification validation strength. **Evidence: source-only; no actual Apple payload lacking signedDate was observed or asserted to exist.**

### Minor M3 — claim string validation still bypasses the stable error body

**Locations:** [PurchaseRequestFields.cs:28](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/Filters/PurchaseRequestFields.cs:28>)–35 and [AccountSubscriptionController.cs:15](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/Areas/OwlAI/Controllers/AccountSubscriptionController.cs:15>).

A claim body with both named string fields but an empty/whitespace account/JWS, account ID longer than 100, or JWS longer than 30,000 (still within the 40,000-byte request cap) passes the raw shape check. Its Required/StringLength validation then returns ApiController's default ProblemDetails instead of the frozen `{code,error}`. Program has no custom invalid-model-state factory. Existing new tests cover missing/null/type/malformed bodies, not these attribute failures. Normalize this narrow claim validation path and test blank/overlength strings before Apple work. **Evidence: source and ASP.NET validation configuration inspection, not a newly executed HTTP reproduction; no authorization bypass is claimed.** This is separate from the unchanged bootstrap M3 carryover.

## Verification and evidence limits

- Independently read all 35 changed paths, affected callers, and the supplied full-range package. Additional tracing covered account authentication/claim proof, account deletion/tombstones, notification/state refresh, device words, AI entitlement/provider gates, additive entity/FK configuration, proof CAS, raw preimage, and the relevant old tests.
- Independently ran read-only Git HEAD/status/diff-check: exact head `faeec095aed5ca49af3e80404ae95abd9bee6341`, no changed product/index files and no whitespace errors. Git emitted only inability to access the user's global ignore file.
- Recomputed all **117 SHA-256 entries: 0 mismatches**, including frozen report and full diff. Parsed every saved TRX outcome set. Final Release has **687 results: 686 Passed, 0 Failed, 1 NotExecuted**, exactly `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`. Read final full log, build log (**0 errors/1 inherited NU1510**) and model-drift output (**no changes since last migration**).
- Read raw behavioral failure messages for initial B2 (403 expected/200 actual), owner mapping/token mismatch, certificate signing-date, valid HTTP body and claim-field tests, and the first full-suite fixture failures. The report accurately preserves that first full result (651 pass/22 fail/1 skip), second (683 pass/1 skip), and final result rather than erasing migration failures.
- Existing NU1903 SQLitePCLRaw/SSH.NET and NU1510 remain visible. No CS8602 occurs in the final log. No dependency update or security-advisory resolution was proved here.
- The supplied PostgreSQL notification-first/verify/restore test uses separate contexts and a barrier at canonical fetch; it proves overlap before the transaction and one binding/grant. It does not force both past a missing-binding read, nor does it test the stale-claim defect above.
- Reviewer executed only the three focused service probes retained below against existing final Release assemblies and process-local in-memory SQLite; no production/test-server database, live network, Apple/provider service, full suite, build, migration, checkout switch or product source mutation was performed. SQLite probes prove the deterministic service outcomes, not PostgreSQL race arbitration.
- Synthetic signed certificate tests cover AppTransaction receiptCreationDate/app identity independently of fake Apple responses. Restore's real synthetic ECDSA HTTP test covers App Attest/body/replay, while canonical Apple status remains fake. These are useful separate layers, not physical StoreKit/App Attest acceptance.

## Unresolved prior carryover

Foundation M1 deterministic registration collision/complete owner-count delta remains open. Foundation M2 resume/JWT/body/tamper/size, proof-after-business-rollback, and exhaustive populated preservation coverage remains open; the added populated host startup is a partial improvement. Foundation M3 unchanged registration framework validation remains open. Accepted foundation I1 service error mapping remains accepted and was not reverted.

The final-review routing list also retains Task8 O1, Task11 M1/M2, Task6 independent-field coverage, unexercised profile fixture injection, true-free retained excess-row handling/AccountSync preservation, cross-platform/physical-device limits and external workflow/release boundaries. This backend slice neither closes nor silently drops those items.

## Declined to judge — explicit root disposition required

1. **Stable shared purchase allowance/global guest enforcement and Retry-After:** subsequent Task6A/B4; this slice provides references only. The visible B4 skip remains a whole-feature blocker; D2 numerical approval is a separate issue, not an excuse to omit enforcement.
2. **Complete captured-envelope replay/relay, including reusing the Apple envelope with fresh assertions:** explicitly accepted residual in the approved contract. I did not require an original key, account login, age window, one-use Apple receipt, or rejection test that contradicts ordinary restore. The grant still must have coherent selected metadata (I2) and current finite canonical limits.
3. **Automatic first desktop recovery after total original-owner-key loss/no previous account link:** deliberately excluded product capability. Existing linked-account sessions and bounded mobile restore remain available; support operational readiness is an external gate, not an implemented override.
4. **Live Apple schemas/real iPhone, reinstall/two-device, Family Sharing, download-versus-purchase account behavior, app ID/credentials, online certificate revocation:** remote/physical operations were forbidden for this review. Local implementation/test findings do not establish these release facts.
5. **Production legacy inventory/migration rollout and old installed-client transition:** external coordinated-release work. UUID-only historical associations cannot be promoted to strong owner authority; direct old-helper behavior is not a legitimate compatibility fix.
6. **Legacy device with no new binding/grant resolving free despite a historical purchase/RequiresAccountSubscription marker elsewhere:** no UUID-derived history join was reintroduced. The frozen migration deliberately quarantines that unproven association; how to retain a legacy recovery/deny marker before new proof needs root's migration/content disposition. This review proves sticky history after an applicable owner/grant association, not a universal migration-era free-fallback policy.
7. **Foundation M1/M2/M3 and other final-review carryovers listed above:** already routed debt, mostly outside this changed slice. They remain open rather than counted as new regressions or closed by final Release totals.
8. **Cross-platform client UI/renewal scheduling and retained-content/AccountSync changes:** next task/whole-feature work. I2 identifies the backend contract defect now; no uninspected iOS/Windows behavior is asserted as already broken.
9. **Fresh live refund discovery before server notification/refresh arrives:** existing canonical-cache policy. The reviewed guarantee is enforcement of known persisted revocation and finite expiry, not synchronous Apple contact for every AI call.
10. **Removing the now-unused UUID subscription helper or refactoring resolver query count/TimeProvider construction:** no current production authorization caller or demonstrated production failure was found. These cleanups do not replace the concrete corrections above.

## Assessment and next action

**FAIL / Needs fixes.** The main owner/mobile separation is substantially implemented and the saved Release evidence is credible, but first-claim state ordering, selected response consistency, and token reconciliation are incomplete. Route I1–I3 to the original implementer, preserve the B1/B2 denial assertions, repair the identified fixture/validation gaps, run meaningful RED/GREEN and covering checks, then obtain a fresh scoped re-review. No release acceptance follows from this report.

## Reproducible reviewer probes

Loaded Release assembly SHA-256 values (read after the probes): `Mavrylo.Services.dll=82A95D7E94216051D2ED522F405053A572062F2432D31A640586B192E57349D7`; `Mavrylo.Tests.dll=CC76B6743C2685A6EBC4EA28E0C890F5ADBD431E2B3B6767D7530F668E9F9AC1`, both under the backend's `tests/bin/Release/net10.0`.

These commands were executed in PowerShell 7.6.5 (.NET 10.0.11) from `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, loading already-built final assemblies. They use only TestDb's `DataSource=:memory:`, test-only fake Apple, and TimeProvider.System. The native DLL preload is the fixture's existing SQLite library. No code compilation or filesystem writes are part of these commands. A first loading attempt stopped on an unmanaged ASP.NET DLL before creating the fixture; the retained commands skip non-managed/already-loaded DLLs and all completed successfully.

### Appendix A — newer persisted revoke versus stale active first claim

Observed JSON:

```json
{"Probe":"stale-active-first-claim-against-newer-revocation","HttpStatus":200,"ResponseStatus":"revoked","PersistedOwner":"review-account","ClaimedAtSet":true,"RevokedStillSet":true,"RequiresAccountSubscription":true}
```

```powershell
$ErrorActionPreference='Stop'
$bin=(Resolve-Path 'tests/bin/Release/net10.0').Path
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)) { foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){ try { [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName) } catch {} } }
[void][System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin 'runtimes/win-x64/native/e_sqlite3.dll'))
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))
$fixture=$tests.GetType('Mavrylo.Tests.TestSupport.TestDb').GetMethod('Create').Invoke($null,@())
try {
$db=$fixture.GetType().GetProperty('Db').GetValue($fixture)
$policy=$tests.GetType('Mavrylo.Tests.TestSupport.TestConfig').GetMethod('SubscriptionPolicy').Invoke($null,@('Production'))
$apple=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.TestSupport.FakeAppStoreServerClient'),$true)
$apple.IsServerApiConfigured=$false; $apple.IsLocalVerifyEnabled=$false
$now=[DateTime]::UtcNow; $owner=[Guid]::NewGuid(); $token=[Guid]::NewGuid()
$u=[Mavrylo.Models.AppUser]::new(); $u.Id='review-account'; $u.Email='review@example.test'; [void]$db.Users.Add($u)
$o=[Mavrylo.Models.AnonymousPurchaseOwnerEntity]::new(); $o.Id=$owner; [void]$db.AnonymousPurchaseOwners.Add($o)
$t=[Mavrylo.Models.AnonymousPurchaseTokenEntity]::new(); $t.AnonymousOwnerId=$owner; $t.AppAccountToken=$token; [void]$db.AnonymousPurchaseTokens.Add($t)
$d=[Mavrylo.Models.DeviceEntity]::new(); $d.KeyId='review-key'; $d.DeviceUuid='review-library'; $d.AnonymousOwnerId=$owner; [void]$db.Devices.Add($d)
$p=[Mavrylo.Models.SubscriptionEntity]::new(); $p.OriginalTransactionId='review-purchase'; $p.ProductId='monthly'; $p.Environment='Production'; $p.AppAccountToken=$token; $p.ExpiresAt=$now.AddDays(7); $p.WasEverPaid=$true; $p.RevokedAt=$now; $p.LastAppleEventAt=$now; [void]$db.Subscriptions.Add($p)
$b=[Mavrylo.Models.ApplePurchaseBindingEntity]::new(); $b.Environment='Production'; $b.OriginalTransactionId='review-purchase'; $b.AnonymousOwnerId=$owner; $b.BindingKind='server_token'; $b.BoundAppAccountToken=$token; [void]$db.ApplePurchaseBindings.Add($b)
[void]$db.SaveChanges()
$old=[Mavrylo.Models.SubscriptionEntity]::new(); $old.OriginalTransactionId='review-purchase'; $old.ProductId='monthly'; $old.Environment='Production'; $old.AppAccountToken=$token; $old.ExpiresAt=$now.AddDays(7); $old.WasEverPaid=$true; $old.LastAppleEventAt=$now.AddMinutes(-1)
$apple.VerifyTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedTransaction]::new($true,$old,$true,$null,$null,$null)
$apple.SubscriptionStatusesResult=[Mavrylo.Services.AppStoreServerClient+SubscriptionStatusesResult]::new($true,$old,$null)
$ent=[Mavrylo.Services.EntitlementService]::new($db,[TimeProvider]::System,$policy)
$ownership=[Mavrylo.Services.SubscriptionOwnershipService]::new($db,[TimeProvider]::System)
$service=[Mavrylo.Services.AccountEntitlementService]::new($db,$ent,$apple,$ownership,[TimeProvider]::System)
$r=$service.ClaimAsync('review-account','review-key','fake-signed-proof',[Threading.CancellationToken]::None).GetAwaiter().GetResult()
$db.ChangeTracker.Clear()
$saved=$db.Subscriptions.Find([object[]]@('review-purchase'))
$device=$db.Devices.Find([object[]]@($d.Id))
[pscustomobject]@{Probe='stale-active-first-claim-against-newer-revocation';HttpStatus=$r.Item1;ResponseStatus=$r.Item2.Status;PersistedOwner=$saved.OwnerAccountId;ClaimedAtSet=($null -ne $saved.ClaimedAt);RevokedStillSet=($null -ne $saved.RevokedAt);RequiresAccountSubscription=$device.RequiresAccountSubscription}|ConvertTo-Json
} finally { $fixture.Dispose() }
```

### Appendix B — effective purchase B versus requested historical purchase A

Observed: both responses are 200/premium with B's product. Verify outputs owner/device, null grant expiry, false linked flag and A otid. Restore outputs mobile_restore but A's past grant expiry, false linked flag and A otid; B's actual grant is five days in the future and B has a preserved claim timestamp.

```powershell
$ErrorActionPreference='Stop'
$bin=(Resolve-Path 'tests/bin/Release/net10.0').Path
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)) { foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){ try { [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName) } catch {} } }
[void][System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin 'runtimes/win-x64/native/e_sqlite3.dll'))
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))

$fixture=$tests.GetType('Mavrylo.Tests.TestSupport.TestDb').GetMethod('Create').Invoke($null,@())
try {
$db=$fixture.GetType().GetProperty('Db').GetValue($fixture)
$apple=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.TestSupport.FakeAppStoreServerClient'),$true)
$apple.IsServerApiConfigured=$false; $apple.IsLocalVerifyEnabled=$false
$now=[DateTime]::UtcNow; $owner=[Guid]::NewGuid(); $other=[Guid]::NewGuid(); $token=[Guid]::NewGuid()
foreach($id in @($owner,$other)) { $o=[Mavrylo.Models.AnonymousPurchaseOwnerEntity]::new(); $o.Id=$id; [void]$db.AnonymousPurchaseOwners.Add($o) }
$t=[Mavrylo.Models.AnonymousPurchaseTokenEntity]::new(); $t.AnonymousOwnerId=$owner; $t.AppAccountToken=$token; [void]$db.AnonymousPurchaseTokens.Add($t)
$d=[Mavrylo.Models.DeviceEntity]::new(); $d.KeyId='review-key'; $d.DeviceUuid='review-library'; $d.AnonymousOwnerId=$owner; [void]$db.Devices.Add($d)
function Purchase([string]$id,[string]$product,[int]$days) { $p=[Mavrylo.Models.SubscriptionEntity]::new(); $p.OriginalTransactionId=$id; $p.ProductId=$product; $p.ExpiresAt=$now.AddDays($days); $p.WasEverPaid=$true; return $p }
$a=Purchase 'A-expired-owned' 'monthly' -1; $a.AppAccountToken=$token; [void]$db.Subscriptions.Add($a)
$b=Purchase 'B-active-restored' 'product' 7; $b.ClaimedAt=$now.AddDays(-1); [void]$db.Subscriptions.Add($b)
foreach($p in @($a,$b)) { $binding=[Mavrylo.Models.ApplePurchaseBindingEntity]::new(); $binding.Environment='Production'; $binding.OriginalTransactionId=$p.OriginalTransactionId; $binding.AnonymousOwnerId= if($p -eq $a){$owner}else{$other}; $binding.BindingKind=if($p -eq $a){'server_token'}else{'legacy_unproven'}; if($p -eq $a){$binding.BoundAppAccountToken=$token}; [void]$db.ApplePurchaseBindings.Add($binding) }
$grant=[Mavrylo.Models.MobilePurchaseGrantEntity]::new(); $grant.DeviceId=$d.Id; $grant.Environment='Production'; $grant.OriginalTransactionId=$b.OriginalTransactionId; $grant.ValidUntil=$now.AddDays(5); $grant.EvidenceKind='paired_device'; $grant.LastEvidenceHash='review'; [void]$db.MobilePurchaseGrants.Add($grant)
[void]$db.SaveChanges()
$signed=Purchase 'A-expired-owned' 'monthly' -1; $signed.AppAccountToken=$token
$canonical=Purchase 'A-expired-owned' 'monthly' -1; $canonical.AppAccountToken=$token
$deviceId=[Guid]::NewGuid().ToString('D'); $nonce=[Guid]::NewGuid().ToString('D'); $digest=[Convert]::ToBase64String([Security.Cryptography.SHA384]::HashData([Text.Encoding]::ASCII.GetBytes($nonce+$deviceId)))
$apple.VerifyTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedTransaction]::new($true,$signed,$true,$null,$digest,$nonce)
$apple.SubscriptionStatusesResult=[Mavrylo.Services.AppStoreServerClient+SubscriptionStatusesResult]::new($true,$canonical,$null)
$apple.AppTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedAppTransaction]::new($true,'Production',$null,$digest,$nonce,$null)
$iap=$tests.GetType('Mavrylo.Tests.TestSupport.IapTestFactory').GetMethod('Create').Invoke($null,@($db,$apple,$null,$null,$null))
foreach($operation in @('verify','restore')) {
 if($operation -eq 'verify'){$r=$iap.VerifyAsync('review-key',[Mavrylo.Dtos.IapVerifyRequest]::new('signed',$null),[Threading.CancellationToken]::None).GetAwaiter().GetResult()}
 else{$r=$iap.RestoreAsync('review-key',[Mavrylo.Dtos.IapRestoreRequest]::new('signed','app',$deviceId),[Threading.CancellationToken]::None).GetAwaiter().GetResult()}
 $dto=$r.Body; $jwt=[System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler]::new().ReadJwtToken($dto.AccessToken)
 [pscustomobject]@{Operation=$operation;HttpStatus=$r.Status;Status=$dto.Entitlement.Status;SelectedProduct=$dto.Entitlement.ProductId;EntitlementExpiry=$dto.Entitlement.ExpiresAt;PurchaseAuthority=$dto.PurchaseAuthority;ResolutionSource=$dto.Entitlement.ResolutionSource;MobileGrantExpiresAt=$dto.MobileGrantExpiresAt;PurchaseIsLinked=$dto.Entitlement.PurchaseIsLinked;JwtOriginalId=($jwt.Claims|Where-Object Type -eq 'otid'|Select-Object -ExpandProperty Value);ActualSelectedGrantExpiry=$grant.ValidUntil;ActualSelectedLinked=($null -ne $b.ClaimedAt)}|ConvertTo-Json
}
} finally { $fixture.Dispose() }
```

### Appendix C — unknown verified token change

Observed JSON:

```json
{"Probe":"newer-unknown-token-drift","HttpStatus":200,"ResponseAuthority":"owner","SignedTokenChanged":true,"PersistedMetadataKeptOldToken":true,"BindingKeptOldToken":true,"StateExpiryUpdated":true}
```

This fixture starts from the same saved original-owner row as Appendix A and supplies a later active Apple state. Clearing the earlier revoke is lawful by event ordering; silently discarding the changed token is the issue.

```powershell
$ErrorActionPreference='Stop'
$bin=(Resolve-Path 'tests/bin/Release/net10.0').Path
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)) { foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){ try { [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName) } catch {} } }
[void][System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin 'runtimes/win-x64/native/e_sqlite3.dll'))
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))
$fixture=$tests.GetType('Mavrylo.Tests.TestSupport.TestDb').GetMethod('Create').Invoke($null,@())
try {
$db=$fixture.GetType().GetProperty('Db').GetValue($fixture)
$policy=$tests.GetType('Mavrylo.Tests.TestSupport.TestConfig').GetMethod('SubscriptionPolicy').Invoke($null,@('Production'))
$apple=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.TestSupport.FakeAppStoreServerClient'),$true)
$apple.IsServerApiConfigured=$false; $apple.IsLocalVerifyEnabled=$false
$now=[DateTime]::UtcNow; $owner=[Guid]::NewGuid(); $token=[Guid]::NewGuid()
$u=[Mavrylo.Models.AppUser]::new(); $u.Id='review-account'; $u.Email='review@example.test'; [void]$db.Users.Add($u)
$o=[Mavrylo.Models.AnonymousPurchaseOwnerEntity]::new(); $o.Id=$owner; [void]$db.AnonymousPurchaseOwners.Add($o)
$t=[Mavrylo.Models.AnonymousPurchaseTokenEntity]::new(); $t.AnonymousOwnerId=$owner; $t.AppAccountToken=$token; [void]$db.AnonymousPurchaseTokens.Add($t)
$d=[Mavrylo.Models.DeviceEntity]::new(); $d.KeyId='review-key'; $d.DeviceUuid='review-library'; $d.AnonymousOwnerId=$owner; [void]$db.Devices.Add($d)
$p=[Mavrylo.Models.SubscriptionEntity]::new(); $p.OriginalTransactionId='review-purchase'; $p.ProductId='monthly'; $p.Environment='Production'; $p.AppAccountToken=$token; $p.ExpiresAt=$now.AddDays(7); $p.WasEverPaid=$true; $p.RevokedAt=$now; $p.LastAppleEventAt=$now; [void]$db.Subscriptions.Add($p)
$b=[Mavrylo.Models.ApplePurchaseBindingEntity]::new(); $b.Environment='Production'; $b.OriginalTransactionId='review-purchase'; $b.AnonymousOwnerId=$owner; $b.BindingKind='server_token'; $b.BoundAppAccountToken=$token; [void]$db.ApplePurchaseBindings.Add($b)
[void]$db.SaveChanges()

$changed=[Mavrylo.Models.SubscriptionEntity]::new(); $changed.OriginalTransactionId='review-purchase'; $changed.ProductId='monthly'; $changed.Environment='Production'; $changed.AppAccountToken=[Guid]::NewGuid(); $changed.ExpiresAt=$now.AddDays(8); $changed.WasEverPaid=$true; $changed.LastAppleEventAt=$now.AddMinutes(1)
$apple.VerifyTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedTransaction]::new($true,$changed,$true,$null,$null,$null)
$canonical=[Mavrylo.Models.SubscriptionEntity]::new(); $canonical.OriginalTransactionId='review-purchase'; $canonical.ProductId='monthly'; $canonical.Environment='Production'; $canonical.AppAccountToken=$changed.AppAccountToken; $canonical.ExpiresAt=$now.AddDays(8); $canonical.WasEverPaid=$true; $canonical.LastAppleEventAt=$now.AddMinutes(1)
$apple.SubscriptionStatusesResult=[Mavrylo.Services.AppStoreServerClient+SubscriptionStatusesResult]::new($true,$canonical,$null)
$iap=$tests.GetType('Mavrylo.Tests.TestSupport.IapTestFactory').GetMethod('Create').Invoke($null,@($db,$apple,$null,$null,$null))
$r=$iap.VerifyAsync('review-key',[Mavrylo.Dtos.IapVerifyRequest]::new('signed',$null),[Threading.CancellationToken]::None).GetAwaiter().GetResult()
$db.ChangeTracker.Clear(); $saved=$db.Subscriptions.Find([object[]]@('review-purchase')); $binding=$db.ApplePurchaseBindings.Find([object[]]@('Production','review-purchase'))
[pscustomobject]@{Probe='newer-unknown-token-drift';HttpStatus=$r.Status;ResponseAuthority=$r.Body.PurchaseAuthority;SignedTokenChanged=($changed.AppAccountToken -ne $token);PersistedMetadataKeptOldToken=($saved.AppAccountToken -eq $token);BindingKeptOldToken=($binding.BoundAppAccountToken -eq $token);StateExpiryUpdated=($saved.ExpiresAt -eq $canonical.ExpiresAt)}|ConvertTo-Json
} finally { $fixture.Dispose() }
```
