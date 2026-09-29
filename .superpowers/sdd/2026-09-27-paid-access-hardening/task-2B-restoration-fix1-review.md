# Task 2B.2 restoration fix 1 — independent review

**Spec compliance: FAIL. Task quality: NeedsFixes. Ready to accept this chunk: No.**

Reviewed all **23 changed files** in `faeec095aed5ca49af3e80404ae95abd9bee6341..b7eed82092508141631ab41d48a758120c41647f` in `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, including the generated migration/model, affected callers, the frozen fix report, full fix package and evidence manifest. This is a fresh clean-context review performed by the assigned GPT-6 Astra xhigh reviewer without subagents. The binding requirements are the prior restoration review, fix1 review preparation, restoration brief and frozen anonymous-restore contract §§1–6. The carryover and legacy-migration preflight documents remain binding.

This is a local backend verdict. It does not approve the whole feature, subsequent client work, deployment or physical Apple behavior.

## Strengths

- I1 now evaluates the persisted row returned by UpsertAsync under the existing user-before-purchase lock order before first ownership/device mutations. The PostgreSQL barrier test genuinely lets a newer revocation commit after the older canonical fetch, and asserts 402 plus preserved revocation and unchanged account/claim/device fields.
- The ordinary mobile owner/grant combinations now project the selected purchase's authority, link flag, JWT identity and grant expiry together. The added three-way matrix covers owned A/granted B, granted A/owned B and two grants; the authenticated token fallback also selects the account purchase correctly. The remaining cross-route gap is R2.
- The reconciliation columns are additive and nullable. Generated designer comparison against its predecessor shows only the migration identity and two intended fields; its model body matches the final snapshot. Neither migration Up nor the model deletes or rewrites purchase/claim history.
- The central conflict observer preserves existing non-null metadata and the immutable binding, records the first observed token digest/time, does not clear on missing/former-token retry, and makes that purchase invalid for entitlement. Restore's PostgreSQL fresh-context test confirms durability. Notification processing can retain a newer revocation while allowing independent purchase B to remain usable. No automatic clearing/support override or new raw JWS/device-ID logging was introduced.
- M1 now reaches the intended refresh and checks the call count, with actual Debug coverage for GUID token projection. M2 uses real synthetic ES256 signatures and preserves the AppTransaction receiptCreationDate path. M3 rejects claim string errors before framework model validation and before Apple verification.
- Saved evidence retains genuine RED runs, intermediate precision/timezone/fixture failures, final full checks and the B4 skip. The supplied report does not claim those fake/synthetic checks prove real StoreKit compatibility.

## Disposition of the prior findings

| Prior item | Disposition | Evidence and remaining boundary |
| --- | --- | --- |
| I1 stale first claim | **Closed for this fix scope** | Persisted-state recheck at AccountEntitlementService.cs:77–88; actual PostgreSQL interleaving RED was 200 versus expected 402, followed by GREEN. Claim/device writes occur only after the recheck. Existing active same-account idempotent arbitration remains. |
| I2 selected-purchase response | **Partially fixed; FAIL** | Mobile combinations and token's authenticated account fallback are fixed, but verify/restore still bypass that account fallback: R2. R3 also shows selection can return pre-refresh access after its own account refresh changes the same purchase. |
| I3 durable token reconciliation | **Partially fixed; FAIL** | Schema, sticky marker, immutable identity, normal upsert/notification paths and PostgreSQL restore durability are implemented. Claim ignores signed-only conflict (R1); a consumer can use access preceding its own quarantine refresh (R3); signed/canonical disagreement drops a newer lifecycle update (R4). |
| M1 refresh/Debug fixtures | **Closed** | Real owner binding plus one-call assertion; actual saved final Debug run is 3/3 and includes the formerly contradictory GUID/empty DeviceUuid branch. |
| M2 required signed date | **Closed** | Valid positive integer signedDate now required for strictly verified transactions/notifications/renewal payloads; receiptCreationDate remains the AppTransaction field. Saved 13/13 JWS suite plus independently rerun synthetic missing-date/type cases. |
| M3 stable claim validation | **Closed** | Actual HTTP cases return invalid_purchase_request without increasing Apple verification calls. Independent raw-parser checks accept exactly 100/30,000 characters and reject blank/empty and one-over-limit values. These narrow parser probes supplement, rather than replace, the saved HTTP test. Foundation bootstrap M3 remains a separate carryover. |

## Issues

### Critical

None demonstrated.

### Important R1 — claim still loses verified signed-token conflicts before permanently linking a purchase

**Location:** [AccountEntitlementService.cs:77](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AccountEntitlementService.cs:77>); signed evidence is obtained at lines 45–49, but only the canonical subscription is passed into reconciliation at lines 75–85.

**Trigger:** an original-owner purchase is stored/bound to T1. The verified submitted transaction carries T2, while the valid same-identity canonical response contains T1 or omits the token. Claim never observes the token in `proof.Subscription`.

**Observed:** Appendix A independently ran both cases against the final Release service. Each returned **200**, wrote OwnerAccountId/ClaimedAt and RequiresAccountSubscription, retained T1, and stored **no conflict marker**. The control where canonical also contains T2 returned the intended **503**, retained a durable marker and made no claim/device mutation. This is not an owner-transfer/private-data exploit; it is a permanently accepted first claim despite the observed identity conflict.

**Minimum correction:** reconcile both verified signed and canonical non-null tokens with stored metadata/binding under the existing user→purchase lock order before first claim/device writes. A missing canonical token must not erase the signed observation. Commit the conflict before returning the stable 503, preserve immutable owner/token/tombstone state, and keep former/missing-token retries quarantined. Cover signed T2/canonical T1 and signed T2/canonical null for first claim and an already-linked same-account retry, checking fresh-context durability and all non-mutation fields. Do not weaken ordinary mobile restore or original-owner proof to solve this.

### Important R2 — verify and restore still contradict token when an authenticated account purchase is the usable source

**Locations:** [AnonymousPurchaseService.cs:83](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AnonymousPurchaseService.cs:83>) and [AnonymousPurchaseService.cs:172](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AnonymousPurchaseService.cs:172>); the shared response builder at lines 176–191 only accepts MobilePurchaseAccess and cannot emit account provenance.

**Trigger:** this installation owns expired A; the supplied, actually authenticated Owl session owns active linked B. The final token path correctly resolves B through DeviceContextService. Verify and restore still call the mobile-only resolver after processing A.

**Observed:** Appendix B used a real locally issued account bearer and stored live session with the normal SharedAccountAuthentication. Verify and restore returned **expired_paid/device/owner, linked=false, JWT otid=A**. Token immediately returned **premium/account/none, linked=true, JWT otid=B**. Removing the account header correctly returned A/expired_paid. Thus no account authority was inferred when logged out.

**Impact:** a legitimate restore/verify response can replace currently available account-backed access with an expired entitlement and an incorrect link hint until a token refresh reverses it. This leaves the frozen common resolution/wire contract inconsistent before client integration.

**Minimum correction:** project all successful verify/restore/token responses from the same final selected context, including actual account fallback, selected subscription/link/JWT identity, authority `none` for account access and null mobile-grant expiry. Keep the processed purchase only as its durable acknowledgement. Add authenticated and logged-out cross-route tests with expired/revoked A and independent active account B, while preserving the rule that unrelated account login does not replace active independent mobile access.

### Important R3 — an account refresh can quarantine a purchase yet the same resolution returns its old premium access

**Locations:** [DeviceContextService.cs:33](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/DeviceContextService.cs:33>)–48 and [DeviceContextService.cs:114](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/DeviceContextService.cs:114>)–119. The consumer at [AiProtectionFilter.cs:56](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/Filters/AiProtectionFilter.cs:56>) captures this one resolution's status and uses it at line 131.

**Trigger:** active owner purchase A is already linked to the authenticated account and due for refresh. ResolveAsync reads independent mobile access first. AccountEntitlementService.GetAsync then refreshes A and persists a token conflict (or newer revocation). ResolveEntitlementAsync nevertheless returns the pre-refresh independent active entitlement because its linked account matches.

**Observed:** Appendix D has exactly one fake canonical refresh and no independent purchase B. The saved purchase was quarantined and projected to **invalid_subscription**, but that same ResolveAsync returned **premium/owner**, selected A, and a subscription object with no conflict marker. This is a deterministic same-request stale-state result, not a hypothetical concurrent update. The pre-existing “active independent wins” branch becomes a missed reconciliation consumer with this fix.

**Impact/limit:** AiProtectionFilter reads this returned premium status once; source tracing shows the commercial entitlement gate may continue after the request's own refresh has already discovered the conflict. The reviewer did not execute an HTTP AI/provider attempt, and makes no claim about actual provider spend. A later request sees the marker; that does not repair the first request's stale authorization.

**Minimum correction:** reselect independent mobile/owner candidates after any account lookup that can refresh their persisted state, and derive the selected row, account/authority and entitlement together. Do not substitute unrelated account rights for active mobile rights. Add a real local HTTP/fake-provider RED→GREEN test for conflict and revocation discovered by that request, with an otherwise identical healthy positive control that reaches the fake provider and denial asserting **providerCalls=0**. Configure the local test spend guard so a default deny-all budget cannot mask this gate. Retain a valid independent B control.

### Minor R4 — signed/canonical token disagreement records quarantine but drops a newer verified revocation

**Locations:** [AnonymousPurchaseService.cs:194](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AnonymousPurchaseService.cs:194>)–205, invoked before upsert at lines 70 and 107.

**Trigger:** stored/signed token T1 and newer same-identity canonical token T2 with RevokedAt/event T2. TokenDisagreementErrorAsync records the token conflict and returns immediately, so neither endpoint reaches state upsert.

**Observed:** Appendix C independently confirmed **503 and a durable conflict marker**, but **no saved newer RevokedAt or LastAppleEventAt**, for both verify and restore. The existing token-disagreement test explicitly expects the old period when canonicalTokenKind=2; it does not test newer revocation retention.

**Impact/severity:** quarantine still blocks that purchase, so this probe does not restore paid access. The defect loses already verified lifecycle evidence and violates the required “newer revocation still applies” reconciliation boundary. It is classified Minor by its demonstrated immediate effect, not waived from I3.

**Minimum correction:** for an existing purchase after same-identity validation, persist the token observation and ordered canonical lifecycle state together before the stable 503, preserving original owner/token and making no binding/grant/claim/device-authority mutation. Test newer revoke versus older retry and missing/former-token retry across a fresh context. Reject mismatched purchase/environment/product evidence without importing it.

## Verification performed and limits

- Independently read the entire 23-file diff and current affected services/callers: claim/ownership, mobile/access/account selection, notification and refresh, Apple strict projection/evidence verifier, IAP/account controllers, raw claim validation, AI entitlement gates, entity mapping and migration/model. Compared the complete new generated designer to the preceding designer and to the final snapshot; differences are limited as described above.
- Read the frozen requirements, original independent review and its probes, fix1 report/preparation, final carryover and local legacy-migration preflight. The original full task range remains contextual; this verdict is on the stated fix range and its callers.
- Independently recomputed **all 85 SHA-256 manifest entries: 0 mismatches**, including frozen report/package/prior review and saved logs/TRX. Rechecked again after probes. Read every TRX outcome set and raw failure messages rather than relying solely on summary counters.
- Final saved `fix1-final5-release.trx` has **700 results: 699 Passed, 0 Failed, 1 NotExecuted**, exactly `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`. Final build is **0 errors, one inherited NU1510**. Actual final Debug is **3/3**. Populated migration2 is **1/1**. Pinned EF 10.0.9 drift output says no changes since the last migration; the earlier global 10.0.2 warning is superseded.
- Genuine RED includes I1's 200-versus-402; I2's wrong owner authority/account link; I3's 200/no durable marker or purchase-context success; M2's accepted genuinely signed missing-date payload; M3's HTTP body without code. The first I1 GREEN attempt only failed sub-microsecond PostgreSQL precision; I2's intermediate GREEN exposed a UTC-kind shift; the notification token fixture and full4 migration row-count issue were fixture defects. Full2 had a CREATE DATABASE stream-read timeout; isolated retry and unchanged-source full3 passed. These are retained and are not recast as all genuine product RED evidence.
- Independently executed **five families of read-only assembly probes** below. They use final existing Release assemblies, TestDb's process-local in-memory SQLite, fake Apple, synthetic signatures and a local stored account session/bearer. No build, full-suite repetition, live HTTP service, PostgreSQL connection, Apple/provider/production call or remote workflow was performed by this reviewer. Runtime: PowerShell 7.6.5 on .NET 10.0.11. Probes establish deterministic service outcomes; they are not PostgreSQL concurrency or physical-device evidence.
- Assembly hashes: `tests/bin/Release/net10.0/Mavrylo.Services.dll = 3BAF6B79DD6725CFB334B6B0F54FA8284ACAE4BDF64BEB38026C8DBCD6184E5A`; `Mavrylo.Tests.dll = 62699107CA9A79980D8A5A7049A89E14B69C9B80876D8EE9D4E7D32929715761`. No product source, checkout, index, HEAD or branch was mutated. Only this review document was written.
- Fresh Git checks before and after probes confirm HEAD `b7eed82092508141631ab41d48a758120c41647f`, clean product status and no diff-check errors. The only Git diagnostic is the inaccessible global ignore file. Saved test-build NU1903 advisories and NU1510 remain open warning debt, not resolved security/dependency work.

## Declined to judge — explicit root disposition required

Nothing below is implicitly waived by this bounded verdict.

1. **Shared purchase allowance/global guest enforcement, Retry-After and D2 amounts:** unfinished Tasks6A/6B/B4. No assertion of aggregate bounded consumption or permission to release follows from the passing tests. Numeric approval and enforcement implementation are separate matters.
2. **Complete captured Apple-envelope replay/relay with fresh valid installation assertions:** explicitly accepted bounded-mobile residual in the approved contract. I did not impose original-key/login/recovery-code/one-use-receipt/age requirements that contradict ordinary restore. No private account or owner/first-desktop authority is thereby approved.
3. **First desktop recovery after total original-owner-key loss with no prior account link, and operational support/reconciliation clearing:** deliberately excluded automatic capability; no support override exists here. Trusted repair/support operational proof remains a separate gate, not an implemented waiver for quarantine.
4. **Physical Apple/iPhone, reinstall/two-device, Family Sharing, real device-verification schema behavior, download-versus-purchase account compatibility, production AppAppleId/credentials and online certificate revocation:** forbidden external/physical work for this review. Synthetic certificates/fake status and saved test totals do not establish those facts.
5. **Production legacy inventory, actual migration rollout, old installed-client transition and rollback operations:** external coordinated-release work. The two new nullable fields are locally reviewed; no production data was examined or altered, and no UUID-based owner upgrade is justified for compatibility.
6. **Known historical paid/account installation markers with no applicable new binding/grant resolving free:** remains an explicit unresolved **local** Task3B/Task10/final-review requirement in legacy-migration-preflight.md. This is not sent away to production inventory. No UUID-derived paid authority was reintroduced; migration/recovery/deny-marker and client history behavior still need the named local implementation/test disposition.
7. **Foundation M1/M2/M3 and listed whole-feature carryovers:** deterministic registration collision/full owner delta; resume/JWT/tamper/proof-after-business-rollback/exhaustive populated preservation; bootstrap framework errors; Task8 O1; Task11 M1/M2; independent Task6 field coverage; unexercised fixture routes. Fix1's narrow populated-column/claim parser/Debug tests do not silently close them.
8. **Cross-platform client UI, renewal scheduling, retained-content and AccountSync behavior:** subsequent client/whole-feature work. R2 is the concrete backend inconsistency now; no uninspected iOS/Windows runtime failure is claimed. True-free excess-row eligibility, review history/tombstones/offline replay preservation remain routed requirements.
9. **Fresh remote refund discovery before any server notification/refresh:** unchanged canonical cache policy. Known state discovered by the current request is inside this review and R3/R4; no requirement to contact Apple synchronously on every AI request was invented.
10. **Dead UUID helper removal, resolver query-count/refactoring and optional clock-injection cleanup:** no current production UUID authorization caller or separate demonstrated defect from those cleanups was found. They do not replace the concrete selection/refresh corrections above.
11. **External publishing/workflows, live provider spend, signing and release approval:** no authorized or executed remote activity. Existing dependency-advisory/warning debt remains tracked and not silently accepted as warning-free readiness.

## Assessment and required next action

**FAIL / NeedsFixes.** I1 and M1–M3 are closed, but I2/I3 are not. Three Important findings and one Minor lifecycle-preservation finding remain. Route R1–R4 to the existing implementer; preserve all original owner/mobile/private-data boundaries and fix each with targeted behavioral evidence, then rerun relevant/full checks and obtain the required fresh review. Do not start next client implementation or call this chunk accepted before that gate closes.

## Reproducible reviewer probes

Each block is standalone PowerShell run from `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend` against the already built Release assemblies. All fixture data lives only in process-local SQLite and is disposed. The reflection used for TestDb/test configuration and account-token issuance does not change product files. Canonical Apple responses are fakes. Every retained invocation below exited 0.

### Appendix A — Claim signed-token conflict versus canonical old/missing token

Observed output:

```json
{"Probe":"canonical-old","HttpStatus":200,"ConflictRecorded":false,"OriginalTokenPreserved":true,"Owner":"review-account","Claimed":true,"DeviceFlag":true}
{"Probe":"canonical-missing","HttpStatus":200,"ConflictRecorded":false,"OriginalTokenPreserved":true,"Owner":"review-account","Claimed":true,"DeviceFlag":true}
{"Probe":"canonical-conflict","HttpStatus":503,"ConflictRecorded":true,"OriginalTokenPreserved":true,"Owner":null,"Claimed":false,"DeviceFlag":false}
```

Command:

```powershell
$ErrorActionPreference='Stop'
$bin=(Resolve-Path 'tests/bin/Release/net10.0').Path
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)) { foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){ try { [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName) } catch {} } }
[void][System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin 'runtimes/win-x64/native/e_sqlite3.dll'))
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))
foreach($kind in @('canonical-old','canonical-missing','canonical-conflict')) {
$fixture=$tests.GetType('Mavrylo.Tests.TestSupport.TestDb').GetMethod('Create').Invoke($null,@())
try {
$db=$fixture.GetType().GetProperty('Db').GetValue($fixture)
$policy=$tests.GetType('Mavrylo.Tests.TestSupport.TestConfig').GetMethod('SubscriptionPolicy').Invoke($null,@('Production'))
$apple=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.TestSupport.FakeAppStoreServerClient'),$true)
$apple.IsServerApiConfigured=$false; $apple.IsLocalVerifyEnabled=$false
$now=[DateTime]::UtcNow; $owner=[Guid]::NewGuid(); $token=[Guid]::NewGuid(); $changedToken=[Guid]::NewGuid()
$u=[Mavrylo.Models.AppUser]::new(); $u.Id='review-account'; $u.Email='review@example.test'; [void]$db.Users.Add($u)
$o=[Mavrylo.Models.AnonymousPurchaseOwnerEntity]::new(); $o.Id=$owner; [void]$db.AnonymousPurchaseOwners.Add($o)
$t=[Mavrylo.Models.AnonymousPurchaseTokenEntity]::new(); $t.AnonymousOwnerId=$owner; $t.AppAccountToken=$token; [void]$db.AnonymousPurchaseTokens.Add($t)
$d=[Mavrylo.Models.DeviceEntity]::new(); $d.KeyId='review-key'; $d.DeviceUuid='review-library'; $d.AnonymousOwnerId=$owner; [void]$db.Devices.Add($d)
function Purchase([Guid]$appleToken) { $p=[Mavrylo.Models.SubscriptionEntity]::new(); $p.OriginalTransactionId='review-purchase'; $p.ProductId='monthly'; $p.Environment='Production'; $p.AppAccountToken=$appleToken; $p.ExpiresAt=$now.AddDays(7); $p.WasEverPaid=$true; $p.LastAppleEventAt=$now.AddMinutes(-2); return $p }
$p=Purchase $token; [void]$db.Subscriptions.Add($p)
$b=[Mavrylo.Models.ApplePurchaseBindingEntity]::new(); $b.Environment='Production'; $b.OriginalTransactionId=$p.OriginalTransactionId; $b.AnonymousOwnerId=$owner; $b.BindingKind='server_token'; $b.BoundAppAccountToken=$token; [void]$db.ApplePurchaseBindings.Add($b)
[void]$db.SaveChanges()
$signed=Purchase $changedToken; $signed.LastAppleEventAt=$now
$canonical=Purchase $token; $canonical.LastAppleEventAt=$now
if($kind -eq 'canonical-missing') {$canonical.AppAccountToken=$null}
if($kind -eq 'canonical-conflict') {$canonical.AppAccountToken=$changedToken}
$apple.VerifyTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedTransaction]::new($true,$signed,$true,$null,$null,$null)
$apple.SubscriptionStatusesResult=[Mavrylo.Services.AppStoreServerClient+SubscriptionStatusesResult]::new($true,$canonical,$null)
$ent=[Mavrylo.Services.EntitlementService]::new($db,[TimeProvider]::System,$policy)
$service=[Mavrylo.Services.AccountEntitlementService]::new($db,$ent,$apple,[Mavrylo.Services.SubscriptionOwnershipService]::new($db,[TimeProvider]::System),[TimeProvider]::System)
$r=$service.ClaimAsync($u.Id,$d.KeyId,'fake-signed-proof',[Threading.CancellationToken]::None).GetAwaiter().GetResult()
$db.ChangeTracker.Clear(); $saved=$db.Subscriptions.Find([object[]]@($p.OriginalTransactionId)); $device=$db.Devices.Find([object[]]@($d.Id))
[pscustomobject]@{Probe=$kind;HttpStatus=$r.Item1;ConflictRecorded=($null -ne $saved.TokenConflictDetectedAt);OriginalTokenPreserved=($saved.AppAccountToken -eq $token);Owner=$saved.OwnerAccountId;Claimed=($null -ne $saved.ClaimedAt);DeviceFlag=$device.RequiresAccountSubscription} | ConvertTo-Json -Compress
} finally { $fixture.Dispose() }
}
```

### Appendix B — Verify/restore/token with a real local authenticated account session

Observed output:

```json
{"Operation":"verify","HttpStatus":200,"Status":"expired_paid","Source":"device","Authority":"owner","Linked":false,"JwtOriginalId":"A-expired-owned"}
{"Operation":"restore","HttpStatus":200,"Status":"expired_paid","Source":"device","Authority":"owner","Linked":false,"JwtOriginalId":"A-expired-owned"}
{"Operation":"token","HttpStatus":200,"Status":"premium","Source":"account","Authority":"none","Linked":true,"JwtOriginalId":"B-active-account"}
{"Operation":"logged-out-token","HttpStatus":200,"Status":"expired_paid","Source":"device","Authority":"owner","Linked":false,"JwtOriginalId":"A-expired-owned"}
```

Command:

```powershell
$ErrorActionPreference='Stop'
$bin=(Resolve-Path 'tests/bin/Release/net10.0').Path
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)) { foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){ try { [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName) } catch {} } }
[void][System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin 'runtimes/win-x64/native/e_sqlite3.dll'))
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))
$fixture=$tests.GetType('Mavrylo.Tests.TestSupport.TestDb').GetMethod('Create').Invoke($null,@())
try {
$db=$fixture.GetType().GetProperty('Db').GetValue($fixture)
$config=$tests.GetType('Mavrylo.Tests.TestSupport.TestConfig').GetMethod('Create').Invoke($null,@($null))
$policy=$tests.GetType('Mavrylo.Tests.TestSupport.TestConfig').GetMethod('SubscriptionPolicy').Invoke($null,@('Production'))
$apple=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.TestSupport.FakeAppStoreServerClient'),$true)
$apple.IsServerApiConfigured=$false; $apple.IsLocalVerifyEnabled=$false
$now=[DateTime]::UtcNow; $owner=[Guid]::NewGuid(); $token=[Guid]::NewGuid()
$u=[Mavrylo.Models.AppUser]::new(); $u.Id='review-account'; $u.Email='review@example.test'; $u.GoogleSub='review-google'; [void]$db.Users.Add($u)
$family=[Mavrylo.Models.AccountSessionEntity]::new(); $family.UserId=$u.Id; $family.ExpiresAt=$now.AddDays(1); [void]$db.AccountSessions.Add($family)
$o=[Mavrylo.Models.AnonymousPurchaseOwnerEntity]::new(); $o.Id=$owner; [void]$db.AnonymousPurchaseOwners.Add($o)
$t=[Mavrylo.Models.AnonymousPurchaseTokenEntity]::new(); $t.AnonymousOwnerId=$owner; $t.AppAccountToken=$token; [void]$db.AnonymousPurchaseTokens.Add($t)
$d=[Mavrylo.Models.DeviceEntity]::new(); $d.KeyId='review-key'; $d.DeviceUuid='review-library'; $d.AnonymousOwnerId=$owner; [void]$db.Devices.Add($d)
function Purchase([string]$id,[int]$days) {$p=[Mavrylo.Models.SubscriptionEntity]::new(); $p.OriginalTransactionId=$id; $p.ProductId='monthly'; $p.Environment='Production'; $p.ExpiresAt=$now.AddDays($days); $p.WasEverPaid=$true; return $p}
$a=Purchase 'A-expired-owned' -1; $a.AppAccountToken=$token; [void]$db.Subscriptions.Add($a)
$b=Purchase 'B-active-account' 7; $b.ProductId='product'; $b.OwnerAccountId=$u.Id; $b.ClaimedAt=$now.AddDays(-2); [void]$db.Subscriptions.Add($b)
$binding=[Mavrylo.Models.ApplePurchaseBindingEntity]::new(); $binding.Environment='Production'; $binding.OriginalTransactionId=$a.OriginalTransactionId; $binding.AnonymousOwnerId=$owner; $binding.BindingKind='server_token'; $binding.BoundAppAccountToken=$token; [void]$db.ApplePurchaseBindings.Add($binding)
$accounts=[Mavrylo.Services.AccountService]::new($db,$config,[TimeProvider]::System)
$session=$accounts.GetType().GetMethod('Issue',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($accounts,@($u,$family))
[void]$db.SaveChanges()
$signed=Purchase $a.OriginalTransactionId -1; $signed.AppAccountToken=$token
$canonical=Purchase $a.OriginalTransactionId -1; $canonical.AppAccountToken=$token
$deviceId=[Guid]::NewGuid().ToString('D'); $nonce=[Guid]::NewGuid().ToString('D')
$digest=[Convert]::ToBase64String([Security.Cryptography.SHA384]::HashData([Text.Encoding]::ASCII.GetBytes($nonce+$deviceId)))
$apple.VerifyTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedTransaction]::new($true,$signed,$true,$null,$digest,$nonce)
$apple.SubscriptionStatusesResult=[Mavrylo.Services.AppStoreServerClient+SubscriptionStatusesResult]::new($true,$canonical,$null)
$apple.AppTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedAppTransaction]::new($true,'Production',$null,$digest,$nonce,$null)
$ent=[Mavrylo.Services.EntitlementService]::new($db,[TimeProvider]::System,$policy)
$accountEnt=[Mavrylo.Services.AccountEntitlementService]::new($db,$ent,$apple,[Mavrylo.Services.SubscriptionOwnershipService]::new($db,[TimeProvider]::System),[TimeProvider]::System)
$auth=[Mavrylo.Services.SharedAccountAuthentication]::new($config,$accounts)
$services=[Microsoft.Extensions.DependencyInjection.ServiceCollection]::new()
$services.Add([Microsoft.Extensions.DependencyInjection.ServiceDescriptor]::Singleton([Mavrylo.Services.SharedAccountAuthentication],$auth))
$services.Add([Microsoft.Extensions.DependencyInjection.ServiceDescriptor]::Singleton([Mavrylo.Services.AccountEntitlementService],$accountEnt))
$provider=[Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions]::BuildServiceProvider($services)
$http=[Microsoft.AspNetCore.Http.DefaultHttpContext]::new(); $http.RequestServices=$provider
$http.Request.Headers['X-Account-Authorization']=[Microsoft.Extensions.Primitives.StringValues]::new('Bearer '+$session.AccessToken)
$accessor=[Microsoft.AspNetCore.Http.HttpContextAccessor]::new(); $accessor.HttpContext=$http
$context=[Mavrylo.Services.DeviceContextService]::new($db,$ent,$accessor)
$tokens=[Mavrylo.Services.JwtTokenService]::new($config,[TimeProvider]::System)
$resolver=[Mavrylo.Services.MobilePurchaseAccessResolver]::new($db,$ent,[TimeProvider]::System)
$purchases=[Mavrylo.Services.AnonymousPurchaseService]::new($db,$apple,$ent,$policy,[Mavrylo.Services.AppleMobileRestoreVerifier]::new($apple),$resolver,$context,$tokens,[TimeProvider]::System)
$iap=[Mavrylo.Services.IapService]::new($apple,$ent,$context,$purchases,$tokens,[TimeProvider]::System,[Microsoft.Extensions.Logging.Abstractions.NullLogger[Mavrylo.Services.IapService]]::Instance)
foreach($operation in @('verify','restore','token','logged-out-token')) {
if($operation -eq 'verify') {$r=$iap.VerifyAsync($d.KeyId,[Mavrylo.Dtos.IapVerifyRequest]::new('signed',$null),[Threading.CancellationToken]::None).GetAwaiter().GetResult()}
elseif($operation -eq 'restore') {$r=$iap.RestoreAsync($d.KeyId,[Mavrylo.Dtos.IapRestoreRequest]::new('signed','app',$deviceId),[Threading.CancellationToken]::None).GetAwaiter().GetResult()}
else {if($operation -eq 'logged-out-token'){[void]$http.Request.Headers.Remove('X-Account-Authorization')}; $r=$iap.TokenAsync($d.KeyId,[Threading.CancellationToken]::None).GetAwaiter().GetResult()}
$dto=$r.Body; $jwt=[System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler]::new().ReadJwtToken($dto.AccessToken)
[pscustomobject]@{Operation=$operation;HttpStatus=$r.Status;Status=$dto.Entitlement.Status;Source=$dto.Entitlement.ResolutionSource;Authority=$dto.PurchaseAuthority;Linked=$dto.Entitlement.PurchaseIsLinked;JwtOriginalId=($jwt.Claims|Where-Object Type -eq 'otid'|Select-Object -ExpandProperty Value)}|ConvertTo-Json -Compress
}
$provider.Dispose()
} finally { $fixture.Dispose() }
```

### Appendix C — Newer canonical revocation during signed/canonical disagreement

Observed output:

```json
{"Operation":"verify","HttpStatus":503,"ConflictRecorded":true,"NewerCanonicalRevokePersisted":false,"NewerEventPersisted":false,"StoredEvent":"2026-09-29T02:24:26.7520816","CanonicalEvent":"2026-09-29T02:26:26.7520816Z"}
{"Operation":"restore","HttpStatus":503,"ConflictRecorded":true,"NewerCanonicalRevokePersisted":false,"NewerEventPersisted":false,"StoredEvent":"2026-09-29T02:24:27.2298039","CanonicalEvent":"2026-09-29T02:26:27.2298039Z"}
```

Command:

```powershell
$ErrorActionPreference='Stop'
$bin=(Resolve-Path 'tests/bin/Release/net10.0').Path
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)) { foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){ try { [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName) } catch {} } }
[void][System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin 'runtimes/win-x64/native/e_sqlite3.dll'))
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))
foreach($operation in @('verify','restore')) {
$fixture=$tests.GetType('Mavrylo.Tests.TestSupport.TestDb').GetMethod('Create').Invoke($null,@())
try {
$db=$fixture.GetType().GetProperty('Db').GetValue($fixture)
$apple=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.TestSupport.FakeAppStoreServerClient'),$true)
$apple.IsServerApiConfigured=$false; $apple.IsLocalVerifyEnabled=$false
$now=[DateTime]::UtcNow; $owner=[Guid]::NewGuid(); $token=[Guid]::NewGuid(); $newToken=[Guid]::NewGuid()
$o=[Mavrylo.Models.AnonymousPurchaseOwnerEntity]::new(); $o.Id=$owner; [void]$db.AnonymousPurchaseOwners.Add($o)
$t=[Mavrylo.Models.AnonymousPurchaseTokenEntity]::new(); $t.AnonymousOwnerId=$owner; $t.AppAccountToken=$token; [void]$db.AnonymousPurchaseTokens.Add($t)
$d=[Mavrylo.Models.DeviceEntity]::new(); $d.KeyId='review-key'; $d.DeviceUuid='private-library'; $d.AnonymousOwnerId=$owner; [void]$db.Devices.Add($d)
function Purchase([Guid]$value) {$p=[Mavrylo.Models.SubscriptionEntity]::new(); $p.OriginalTransactionId='review-revoke'; $p.ProductId='monthly'; $p.Environment='Production'; $p.AppAccountToken=$value; $p.ExpiresAt=$now.AddDays(7); $p.WasEverPaid=$true; $p.LastAppleEventAt=$now.AddMinutes(-2); return $p}
$stored=Purchase $token; [void]$db.Subscriptions.Add($stored)
$b=[Mavrylo.Models.ApplePurchaseBindingEntity]::new(); $b.Environment='Production'; $b.OriginalTransactionId=$stored.OriginalTransactionId; $b.AnonymousOwnerId=$owner; $b.BindingKind='server_token'; $b.BoundAppAccountToken=$token; [void]$db.ApplePurchaseBindings.Add($b)
[void]$db.SaveChanges()
$signed=Purchase $token; $canonical=Purchase $newToken; $canonical.LastAppleEventAt=$now; $canonical.RevokedAt=$now
$deviceId=[Guid]::NewGuid().ToString('D'); $nonce=[Guid]::NewGuid().ToString('D')
$digest=[Convert]::ToBase64String([Security.Cryptography.SHA384]::HashData([Text.Encoding]::ASCII.GetBytes($nonce+$deviceId)))
$apple.VerifyTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedTransaction]::new($true,$signed,$true,$null,$digest,$nonce)
$apple.SubscriptionStatusesResult=[Mavrylo.Services.AppStoreServerClient+SubscriptionStatusesResult]::new($true,$canonical,$null)
$apple.AppTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedAppTransaction]::new($true,'Production',$null,$digest,$nonce,$null)
$iap=$tests.GetType('Mavrylo.Tests.TestSupport.IapTestFactory').GetMethod('Create').Invoke($null,@($db,$apple,$null,$null,$null))
if($operation -eq 'verify') {$r=$iap.VerifyAsync($d.KeyId,[Mavrylo.Dtos.IapVerifyRequest]::new('signed',$null),[Threading.CancellationToken]::None).GetAwaiter().GetResult()}
else {$r=$iap.RestoreAsync($d.KeyId,[Mavrylo.Dtos.IapRestoreRequest]::new('signed','app',$deviceId),[Threading.CancellationToken]::None).GetAwaiter().GetResult()}
$db.ChangeTracker.Clear(); $saved=$db.Subscriptions.Find([object[]]@($stored.OriginalTransactionId))
[pscustomobject]@{Operation=$operation;HttpStatus=$r.Status;ConflictRecorded=($null -ne $saved.TokenConflictDetectedAt);NewerCanonicalRevokePersisted=($null -ne $saved.RevokedAt);NewerEventPersisted=($saved.LastAppleEventAt -eq $canonical.LastAppleEventAt);StoredEvent=$saved.LastAppleEventAt;CanonicalEvent=$canonical.LastAppleEventAt}|ConvertTo-Json -Compress
} finally {$fixture.Dispose()}
}
```

### Appendix D — Same-request account refresh quarantines A but returns old premium

Observed output:

```json
{"Probe":"context-detects-conflict-during-own-account-refresh","ReturnedStatus":"premium","ReturnedAuthority":"owner","ReturnedPurchase":"A-active-owned","ReturnedConflict":false,"PersistedConflict":true,"PersistedEntitlement":"invalid_subscription","RefreshCalls":1}
```

Command:

```powershell
$ErrorActionPreference='Stop'
$bin=(Resolve-Path 'tests/bin/Release/net10.0').Path
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)) { foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){ try { [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName) } catch {} } }
[void][System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin 'runtimes/win-x64/native/e_sqlite3.dll'))
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))
$fixture=$tests.GetType('Mavrylo.Tests.TestSupport.TestDb').GetMethod('Create').Invoke($null,@())
try {
$db=$fixture.GetType().GetProperty('Db').GetValue($fixture)
$config=$tests.GetType('Mavrylo.Tests.TestSupport.TestConfig').GetMethod('Create').Invoke($null,@($null))
$policy=$tests.GetType('Mavrylo.Tests.TestSupport.TestConfig').GetMethod('SubscriptionPolicy').Invoke($null,@('Production'))
$apple=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.TestSupport.FakeAppStoreServerClient'),$true)
$apple.IsServerApiConfigured=$true; $apple.IsLocalVerifyEnabled=$false
$now=[DateTime]::UtcNow; $owner=[Guid]::NewGuid(); $token=[Guid]::NewGuid()
$u=[Mavrylo.Models.AppUser]::new(); $u.Id='review-account'; $u.Email='review@example.test'; $u.GoogleSub='review-google'; [void]$db.Users.Add($u)
$family=[Mavrylo.Models.AccountSessionEntity]::new(); $family.UserId=$u.Id; $family.ExpiresAt=$now.AddDays(1); [void]$db.AccountSessions.Add($family)
$o=[Mavrylo.Models.AnonymousPurchaseOwnerEntity]::new(); $o.Id=$owner; [void]$db.AnonymousPurchaseOwners.Add($o)
$t=[Mavrylo.Models.AnonymousPurchaseTokenEntity]::new(); $t.AnonymousOwnerId=$owner; $t.AppAccountToken=$token; [void]$db.AnonymousPurchaseTokens.Add($t)
$d=[Mavrylo.Models.DeviceEntity]::new(); $d.KeyId='review-key'; $d.DeviceUuid='review-library'; $d.AnonymousOwnerId=$owner; [void]$db.Devices.Add($d)
function Purchase([string]$id,[int]$days) {$p=[Mavrylo.Models.SubscriptionEntity]::new(); $p.OriginalTransactionId=$id; $p.ProductId='monthly'; $p.Environment='Production'; $p.ExpiresAt=$now.AddDays($days); $p.WasEverPaid=$true; return $p}
$a=Purchase 'A-active-owned' 7; $a.AppAccountToken=$token; $a.OwnerAccountId=$u.Id; $a.ClaimedAt=$now.AddDays(-2); $a.LastCheckedAt=$now.AddHours(-1); [void]$db.Subscriptions.Add($a)

$binding=[Mavrylo.Models.ApplePurchaseBindingEntity]::new(); $binding.Environment='Production'; $binding.OriginalTransactionId=$a.OriginalTransactionId; $binding.AnonymousOwnerId=$owner; $binding.BindingKind='server_token'; $binding.BoundAppAccountToken=$token; [void]$db.ApplePurchaseBindings.Add($binding)
$accounts=[Mavrylo.Services.AccountService]::new($db,$config,[TimeProvider]::System)
$session=$accounts.GetType().GetMethod('Issue',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($accounts,@($u,$family))
[void]$db.SaveChanges()
$signed=Purchase $a.OriginalTransactionId -1; $signed.AppAccountToken=$token
$canonical=Purchase $a.OriginalTransactionId 7; $canonical.AppAccountToken=[Guid]::NewGuid(); $canonical.LastAppleEventAt=$now
$deviceId=[Guid]::NewGuid().ToString('D'); $nonce=[Guid]::NewGuid().ToString('D')
$digest=[Convert]::ToBase64String([Security.Cryptography.SHA384]::HashData([Text.Encoding]::ASCII.GetBytes($nonce+$deviceId)))
$apple.VerifyTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedTransaction]::new($true,$signed,$true,$null,$digest,$nonce)
$apple.SubscriptionStatusesResult=[Mavrylo.Services.AppStoreServerClient+SubscriptionStatusesResult]::new($true,$canonical,$null)
$apple.AppTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedAppTransaction]::new($true,'Production',$null,$digest,$nonce,$null)
$ent=[Mavrylo.Services.EntitlementService]::new($db,[TimeProvider]::System,$policy)
$accountEnt=[Mavrylo.Services.AccountEntitlementService]::new($db,$ent,$apple,[Mavrylo.Services.SubscriptionOwnershipService]::new($db,[TimeProvider]::System),[TimeProvider]::System)
$auth=[Mavrylo.Services.SharedAccountAuthentication]::new($config,$accounts)
$services=[Microsoft.Extensions.DependencyInjection.ServiceCollection]::new()
$services.Add([Microsoft.Extensions.DependencyInjection.ServiceDescriptor]::Singleton([Mavrylo.Services.SharedAccountAuthentication],$auth))
$services.Add([Microsoft.Extensions.DependencyInjection.ServiceDescriptor]::Singleton([Mavrylo.Services.AccountEntitlementService],$accountEnt))
$provider=[Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions]::BuildServiceProvider($services)
$http=[Microsoft.AspNetCore.Http.DefaultHttpContext]::new(); $http.RequestServices=$provider
$http.Request.Headers['X-Account-Authorization']=[Microsoft.Extensions.Primitives.StringValues]::new('Bearer '+$session.AccessToken)
$accessor=[Microsoft.AspNetCore.Http.HttpContextAccessor]::new(); $accessor.HttpContext=$http
$context=[Mavrylo.Services.DeviceContextService]::new($db,$ent,$accessor)
$tokens=[Mavrylo.Services.JwtTokenService]::new($config,[TimeProvider]::System)
$resolver=[Mavrylo.Services.MobilePurchaseAccessResolver]::new($db,$ent,[TimeProvider]::System)
$purchases=[Mavrylo.Services.AnonymousPurchaseService]::new($db,$apple,$ent,$policy,[Mavrylo.Services.AppleMobileRestoreVerifier]::new($apple),$resolver,$context,$tokens,[TimeProvider]::System)
$iap=[Mavrylo.Services.IapService]::new($apple,$ent,$context,$purchases,$tokens,[TimeProvider]::System,[Microsoft.Extensions.Logging.Abstractions.NullLogger[Mavrylo.Services.IapService]]::Instance)
$resolved=$context.ResolveAsync($d.KeyId,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
$db.ChangeTracker.Clear(); $saved=$db.Subscriptions.Find([object[]]@($a.OriginalTransactionId))
[pscustomobject]@{Probe='context-detects-conflict-during-own-account-refresh';ReturnedStatus=$resolved.Entitlement.Status;ReturnedAuthority=$resolved.Authority;ReturnedPurchase=$resolved.Subscription.OriginalTransactionId;ReturnedConflict=($null -ne $resolved.Subscription.TokenConflictDetectedAt);PersistedConflict=($null -ne $saved.TokenConflictDetectedAt);PersistedEntitlement=$ent.ToEntitlement($saved).Status;RefreshCalls=$apple.RefreshCallCount}|ConvertTo-Json -Compress
$provider.Dispose()
} finally { $fixture.Dispose() }
```

### Appendix E — Synthetic signed-date checks and exact claim string boundaries

Observed output:

```json
{"Probe":"synthetic-signed-date","InvalidType":false,"Result":"passed"}
{"Probe":"synthetic-signed-date","InvalidType":true,"Result":"passed"}
{"Probe":"boundaries","RawShapeAccepted":true,"Bytes":30138}
{"Probe":"blank-account","RawShapeAccepted":false,"Bytes":30039}
{"Probe":"empty-jws","RawShapeAccepted":false,"Bytes":138}
{"Probe":"long-account","RawShapeAccepted":false,"Bytes":30139}
{"Probe":"long-jws","RawShapeAccepted":false,"Bytes":30139}
{"PowerShell":"7.6.5","Runtime":".NET 10.0.11"}
```

Command:

```powershell
$ErrorActionPreference='Stop'
$bin=(Resolve-Path 'tests/bin/Release/net10.0').Path
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)) { foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){ try { [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName) } catch {} } }
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))
$case=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.AppleJwsTests'))
foreach($invalidType in @($false,$true)) {
$case.StrictSignedTransactionRequiresValidSignedDate($invalidType)
[pscustomobject]@{Probe='synthetic-signed-date';InvalidType=$invalidType;Result='passed'} | ConvertTo-Json -Compress
}
$app=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.dll'))
$method=$app.GetType('Mavrylo.Filters.PurchaseRequestFields').GetMethod('ValidForPath')
foreach($shape in @('boundaries','blank-account','empty-jws','long-account','long-jws')) {
$account='a'*100; $jws='j'*30000
switch($shape) {'blank-account' {$account=' '} 'empty-jws' {$jws=''} 'long-account' {$account='a'*101} 'long-jws' {$jws='j'*30001}}
$body=[Text.Encoding]::UTF8.GetBytes((@{account_id=$account;jws_transaction=$jws}|ConvertTo-Json -Compress))
$valid=$method.Invoke($null,@([Microsoft.AspNetCore.Http.PathString]::new('/owlai/account/subscription/apple/claim'),$body))
[pscustomobject]@{Probe=$shape;RawShapeAccepted=$valid;Bytes=$body.Length}|ConvertTo-Json -Compress
}
[pscustomobject]@{PowerShell=$PSVersionTable.PSVersion.ToString();Runtime=[System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription}|ConvertTo-Json -Compress
```


