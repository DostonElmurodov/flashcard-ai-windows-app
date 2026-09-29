# Task 2B.2 restoration fix round 2 — independent review

**Spec compliance: FAIL. Task quality: NeedsFixes. Ready to accept this chunk: No.**

The three original Important failures R1–R3 are corrected in their stated scenarios. R4 now preserves ordered lifecycle evidence and durable quarantine, but its metadata-preservation requirement remains incomplete in the opposite signed/canonical disagreement direction. One **Minor** finding remains below. No new ownership, private-data, or paid-access bypass was demonstrated.

Fresh scoped review of all seven changed files in `b7eed82092508141631ab41d48a758120c41647f..900c9a50799adb3d991fc97a4adf86045b8c13a9`, performed by the assigned GPT-6 Astra Extra High reviewer without subagents. Product checkout: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`. The complete fix1 review, fix2 preparation, frozen contract §§1–6, restoration brief, fix2 report snapshot and full diff package were read. The carryover and legacy-migration preflight remain binding. This is a bounded backend fix review, not whole-feature or release acceptance.

## R1–R4 dispositions

| Item | Verdict | Source and behavioral evidence |
| --- | --- | --- |
| R1: signed-only claim conflict | **PASS for the original Important authorization defect.** The related null-metadata preservation edge is included in N1 below. | `AccountEntitlementService.cs:61–88` keeps user-before-purchase locking, persists canonical state, observes the verified signed token, and commits quarantine before `TryClaimAsync`/device writes at `:93–96`. `RestorationPostgresTests.cs:322–391` covers signed T2 with canonical T1/null for first claim and same-account idempotent claim, rereading in another PostgreSQL context and asserting original token, conflict hash, owner, timestamp, device flag, and bound token. Saved RED is four actual 200-versus-503 failures; GREEN and final focused/full runs pass all four. The independent narrow claim probe below also verifies missing/former-token retries keep the first conflict hash and return 503. |
| R2: common selected context | **PASS for this scope.** | Successful verify/restore use `SelectedAccessResponseAsync` at `AnonymousPurchaseService.cs:174–191`; entitlement/source/link/authority/JWT original ID/grant expiry all come from the final `DeviceContext`. `DeviceContextService.cs:33–50,107–130` authenticates the supplied account and preserves active independent mobile priority. `SharedAccountAuthentication.cs:31–38` requires a validated account JWT and active stored session. `SharedSubscriptionPostgresTests.cs:275–359` uses a real local account session, exercises expired and revoked owned A plus active account B across all three HTTP routes, then removes the account header and checks all routes again. Saved RED reports expired/revoked instead of premium; final cases pass. The retained provenance control at `:105–156` covers unrelated account login with active independent ownership on entitlement/token/verify; restore uses the same final selection by source inspection. Existing owner/grant three-way selection cases also pass in full Release. |
| R3: own-refresh stale AI access | **PASS for this scope.** | `DeviceContextService.cs:34–35` completes authenticated account refresh before mobile candidate reads; `MobilePurchaseAccessResolver.cs:53–56` reads persisted rows without tracking. `AiProtectionFilter.cs:56–62,131–150` consumes that final status before provider authorization. `SharedSubscriptionPostgresTests.cs:361–435` supplies actual local HTTP device/account requests, fake Apple and a fake HTTP AI provider. Conflict/revoke without independent B assert 402 and zero provider calls; healthy and both independent-B controls assert 200 and exactly one provider call, with exactly one Apple refresh. `AiSpendTestSupport.cs:10–18` enables positive synthetic budgets; `TestMode:Enabled=false` is explicit. The corrected-fixture pre-fix RED has two failures and three valid passing controls; fixed final Release and Debug pass all five. |
| R4: lifecycle plus quarantine without metadata/authority change | **PARTIAL / FAIL.** | `AnonymousPurchaseService.cs:66–70,102–106` rejects mismatched signed/canonical identity before the disagreement handler. `:199–216` serializes and atomically commits canonical lifecycle plus conflict, reusing `EntitlementService.cs:115–141` event ordering. Saved PostgreSQL cases at `RestorationPostgresTests.cs:184–264` confirm newer revocation and event retention, no claim/grant/device flag mutation, and older/missing/former-token retry persistence in separate contexts. Six mismatch controls at `:273–315` import neither lifecycle nor conflict. However, the new null-token guard only works when canonical evidence itself detects conflict before metadata adoption; N1 confirms the missed signed-only direction. |

Prior I1 and fix1 M1–M3 remain closed within their prior scope: the persisted first-claim activity recheck remains at `AccountEntitlementService.cs:91–92`, and the final full TRX includes the PostgreSQL interleaving test, cached-refresh identity controls, strict signed-date cases and claim HTTP validation. Final Debug includes both cached-refresh cases. No changed file weakens the synthetic signature/receipt-date or raw-claim parser implementation. Foundation/bootstrap M3 is a different, still-routed item; this statement does not close it.

## Finding N1 — Minor: signed-first conflict is observed after null token metadata has already been filled

**Primary location:** [AnonymousPurchaseService.cs:209](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AnonymousPurchaseService.cs:209>), followed by signed observation at `:214–215`. The adoption guard is [EntitlementService.cs:133](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/EntitlementService.cs:133>). The same ordering is present in the newly changed claim caller at [AccountEntitlementService.cs:79](<D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/src/Mavrylo.Services/Services/AccountEntitlementService.cs:79>) before `:84–85`.

**Trigger:** an existing purchase has `AppAccountToken=null`, its immutable binding contains T1, verified signed evidence contains T2, and newer same-identity canonical evidence contains T1. For verify/restore the canonical evidence also contains a newer revocation. This is the opposite of the new null-metadata test rows, which only set `signedTokenDiffers=false, storedTokenMissing=true` at `RestorationPostgresTests.cs:182–183`.

**Observed independently on the final existing Release assemblies:** verify and restore each return 503, persist the conflict and newer revocation, but change the previously null `AppAccountToken` to canonical T1. First and already-linked claim variants likewise return 503 without changing account/claim/device fields, but populate the token metadata. Subsequent missing/former-token claim retries remain quarantined and preserve the first conflict hash.

**Cause:** `UpsertAsync(canonical)` compares T1 to binding T1 and finds no conflict; `EntitlementService.cs:133–135` therefore fills the null metadata. The signed T2 observation runs afterward and marks quarantine. The transaction correctly commits both changes, including the prohibited metadata adoption. Rechecking only `existing.TokenConflictDetectedAt` during canonical upsert cannot account for a verified signed conflict that has not yet been observed.

**Impact and severity:** this violates the explicitly required unchanged token metadata on reconciliation denial and the fix's own null-preservation invariant. The demonstrated value matches the existing immutable binding, quarantine remains effective, and no paid/owner/private-data escalation was observed. This is **Minor**, not another R1/R3 authorization bypass. It is nevertheless an unresolved R4 requirement and prevents acceptance of this fix chunk.

**Minimum correction:** incorporate both verified token observations into the conflict decision before allowing null token metadata adoption, while retaining same-identity checks, lock ordering, canonical event ordering and one durable commit. Preserve the existing token value, including null, when that decision requires quarantine. Cover the opposite signed T2/canonical T1/null-stored-token direction for verify, restore, first claim and same-account claim, then check persisted state from another context and missing/former retries. Keep normal non-conflicting metadata adoption and healthy/independent controls. No new schema, owner transfer, support override or mobile-login prerequisite is called for.

## Evidence checked and limits

- Independently recomputed every entry in `task-2B-restoration-fix2-evidence-900c9a5.json`: **46 files, zero size/hash mismatches**. The product diff contains exactly four service files and three test files; no model, mapping, migration or snapshot change. `git diff --check` reports no errors. The saved R3 restored-source backup hashes identically to the final `DeviceContextService.cs`.
- Read the full seven-file package once and current affected services/callers. Besides IAP and AI, `DeviceWordsController.cs:33–46,74–77` consumes final entitlement while retaining its installation library namespace; `PublicFlashcardSetsController.cs:24–30,80–83` retains installation identity. No new account/private-data authority join was found in these consumers. Untouched systems were not broadly re-reviewed.
- Parsed every saved TRX result set and failure details, and inspected the raw logs. Final saved `fix2-full-release.trx` contains **722 actual results: 721 Passed, zero Failed, one NotExecuted**, specifically `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`. The aggregate `notExecuted` attribute itself is zero; the individual result and raw log establish the one skip. Final focused is **27/27**; Debug is **7/7**; the solution build log reports **zero errors and four inherited NU1510/NU1903 warnings**. These are independently inspected saved runs, not a reviewer rerun.
- R1 RED: four 200-versus-503 failures. R2 RED: two wrong selected-entitlement failures; first GREEN's two failures were omitted-null JSON-property fixture assumptions, corrected in GREEN2. R3 initial RED is a compilation/setup error (`CS0411`, missing configuration extension-method import), not a behavioral reproduction. RED2 has two behavioral failures but invalid positive-case token binding; it is not relied on as clean positive-control evidence. `r3-final-fixture-red` is the corrected-fixture/pre-R3-source evidence: conflict/revoke deny expectations fail with 200 while healthy and both independent-B controls pass. `r3-http-green`'s healthy 402 is the fixture issue; `r3-http-green2` and later final covering runs pass. R4 initial RED loses an hour-newer event; intermediate GREEN failures are sub-microsecond PostgreSQL precision assertions; matrix failures are unscoped shared-fixture row counts. The separate null-metadata RED has two real null-versus-token failures in the canonical-conflict direction, followed by passing corrected coverage. None of those intermediate labels is silently rewritten.
- The first solution-build attempt targeted nonexistent `Mavrylo.sln`; its MSB1009 is a command-path error. The corrected `.slnx` build is the successful saved build. Warning debt remains open; this is not a warning-free/dependency-security verdict.
- The report records concrete simplify decisions for every iteration: reuse existing lock/transaction/conflict observer, share final-context projection, move account refresh before mobile selection, reuse ordered upsert, and add the null-adoption guard. The changes are small and understandable; N1 shows the last guard is incomplete, rather than a need for broad refactoring.
- Reviewer execution was limited to the specific unanswered null-metadata ordering doubt and the directly related claim caller, using final existing Release assemblies and process-local in-memory SQLite/fake Apple. No test suite/build, PostgreSQL connection, HTTP host, physical Apple, network, provider, production or remote workflow was run by this reviewer. The probe reloads after `ChangeTracker.Clear`; it is not new PostgreSQL fresh-context/concurrency evidence. The saved PostgreSQL tests supply those separate facts for their covered scenarios.
- Probe assembly SHA-256: `Mavrylo.Services.dll = CE98412C758C7B86FCCC4940055A7C547334B9C2DE3AFB1D9B519AA172872841`; `Mavrylo.Tests.dll = 20FC788192FFB61D82FA51111947F39C4F4395E84A32F29526C1BDFB9E6DD05D` under `tests/bin/Release/net10.0`. No product source, Git/index/HEAD or saved evidence was mutated. Only this report was written.
- Final read-only recheck confirms HEAD `900c9a50799adb3d991fc97a4adf86045b8c13a9`, no entries from `git status --short`, no diff-check errors, and all 46 manifest entries still match. Git emitted the existing inaccessible global-ignore-file warning; no checkout change was reported.

## Out-of-scope observations and declined judgments — nonblocking for this bounded repair

These remain explicit gates; none is waived or newly treated as a defect introduced by this seven-file fix.

1. **Task6A/6B/B4 and D2:** stable shared purchase allowance, aggregate guest/global bounds, limiting-window Retry-After and numeric/live budget approval remain unfinished. Synthetic enabled budgets prove only that R3 controls reach the fake provider; they approve no spending amount or release.
2. **Complete captured paired-envelope replay/relay:** the approved bounded-mobile residual remains accepted. App Attest does not prove the submitted StoreKit device ID belongs to its key. No mandatory Owl login, surviving old key, recovery code, one-use receipt or arbitrary evidence-age restriction is imposed. This residual grants no first-desktop owner or private-account authority; aggregate accounting work remains required.
3. **Exceptional first desktop recovery and operational quarantine repair:** total original-key loss with no prior account claim, support prior-owner verification, and trusted reconciliation clearing remain separate unimplemented operational gates. They must not gate ordinary mobile restore.
4. **Physical Apple and production configuration:** real iPhone App Attest/StoreKit, reinstall/two-device restore, Family Sharing, older payload/device fields, download-versus-purchase account compatibility, production app ID/credentials and certificate revocation are unverified here. Simulator, SQLite, synthetic signatures and fake Apple do not prove those facts.
5. **Migration and local historical markers:** `legacy-migration-preflight.md` still requires local Task3B/Task10/final testing and a disposition for a known historical paid/account installation resolving free without an applicable new binding/grant. That unresolved local behavior is not delegated away to production inventory, and no UUID-derived authority is an acceptable substitute. Actual production inventory, rollout, old-client transition and rollback are separate external gates.
6. **D3 and cross-platform completion:** retained paid read/review/delete/export remains binding. Client restoration scheduling, history/tombstone/offline-text/idempotency preservation, AccountSync and true-free excess-row eligibility remain subsequent local/whole-feature work. No iOS/Windows runtime verdict is made by this review.
7. **Existing carryovers:** foundation M1 registration collision/full owner delta, foundation M2 resume/rollback/populated migration coverage, foundation M3 bootstrap framework validation, Task8 O1, Task11 M1/M2 warning/test guidance, independent Task6 field cases and unexercised fixture routes retain their explicit routing. The prior fix1 M1–M3 closure does not rename or close foundation M3.
8. **Other time/concurrency and maintenance limits:** no new requirement for synchronous Apple discovery on every AI call is invented; unknown remote changes are distinct from R3's state discovered by this request. No claim of universal cross-request linearizability, performance/query-count cleanup or removal of dead legacy helpers is made.
9. **External actions:** no live provider/Apple spend, production migration, push, deployment, signing, release or remote workflow execution was authorized or performed. Existing workflow/artifact-upload approval limitations and dependency-warning debt remain unchanged.

## Reproducible narrow probe

Observed output from the actual final-assembly verify/restore probe:

```json
{"Operation":"verify","Status":503,"ConflictRecorded":true,"NullTokenPreserved":false,"AdoptedCanonicalToken":true,"RevocationPersisted":true,"OwnerUnchanged":true,"ClaimUnchanged":true}
{"Operation":"restore","Status":503,"ConflictRecorded":true,"NullTokenPreserved":false,"AdoptedCanonicalToken":true,"RevocationPersisted":true,"OwnerUnchanged":true,"ClaimUnchanged":true}
```

Standalone PowerShell invocation, with only process-local database writes:

```powershell
$ErrorActionPreference='Stop'
$bin='D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend/tests/bin/Release/net10.0'
foreach($dir in @('C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.12',$bin)){
  foreach($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){
    try{[void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($f.FullName)}catch{}
  }
}
[void][System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin 'runtimes/win-x64/native/e_sqlite3.dll'))
$tests=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'Mavrylo.Tests.dll'))
foreach($operation in @('verify','restore')){
  $fixture=$tests.GetType('Mavrylo.Tests.TestSupport.TestDb').GetMethod('Create').Invoke($null,@())
  try{
    $db=$fixture.GetType().GetProperty('Db').GetValue($fixture)
    $apple=[Activator]::CreateInstance($tests.GetType('Mavrylo.Tests.TestSupport.FakeAppStoreServerClient'),$true)
    $apple.IsServerApiConfigured=$false; $apple.IsLocalVerifyEnabled=$false
    $now=[DateTime]::UtcNow; $owner=[Guid]::NewGuid(); $oldToken=[Guid]::NewGuid(); $otherToken=[Guid]::NewGuid()
    $o=[Mavrylo.Models.AnonymousPurchaseOwnerEntity]::new(); $o.Id=$owner; [void]$db.AnonymousPurchaseOwners.Add($o)
    $t=[Mavrylo.Models.AnonymousPurchaseTokenEntity]::new(); $t.AnonymousOwnerId=$owner; $t.AppAccountToken=$oldToken; [void]$db.AnonymousPurchaseTokens.Add($t)
    $d=[Mavrylo.Models.DeviceEntity]::new(); $d.KeyId='review-null-key'; $d.DeviceUuid='private-library'; $d.AnonymousOwnerId=$owner; [void]$db.Devices.Add($d)
    function NewProbePurchase([Guid]$value){
      $p=[Mavrylo.Models.SubscriptionEntity]::new(); $p.OriginalTransactionId='review-null-metadata'
      $p.ProductId='monthly'; $p.Environment='Production'; $p.AppAccountToken=$value
      $p.ExpiresAt=$now.AddDays(7); $p.WasEverPaid=$true; $p.LastAppleEventAt=$now.AddMinutes(-2); return $p
    }
    $stored=NewProbePurchase $oldToken; $stored.AppAccountToken=$null; [void]$db.Subscriptions.Add($stored)
    $b=[Mavrylo.Models.ApplePurchaseBindingEntity]::new(); $b.Environment='Production'; $b.OriginalTransactionId=$stored.OriginalTransactionId
    $b.AnonymousOwnerId=$owner; $b.BindingKind='server_token'; $b.BoundAppAccountToken=$oldToken; [void]$db.ApplePurchaseBindings.Add($b)
    [void]$db.SaveChanges()
    $signed=NewProbePurchase $otherToken; $canonical=NewProbePurchase $oldToken; $canonical.LastAppleEventAt=$now; $canonical.RevokedAt=$now
    $deviceId=[Guid]::NewGuid().ToString('D'); $nonce=[Guid]::NewGuid().ToString('D')
    $digest=[Convert]::ToBase64String([Security.Cryptography.SHA384]::HashData([Text.Encoding]::ASCII.GetBytes($nonce+$deviceId)))
    $apple.VerifyTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedTransaction]::new($true,$signed,$true,$null,$digest,$nonce)
    $apple.SubscriptionStatusesResult=[Mavrylo.Services.AppStoreServerClient+SubscriptionStatusesResult]::new($true,$canonical,$null)
    $apple.AppTransactionResult=[Mavrylo.Services.AppStoreServerClient+VerifiedAppTransaction]::new($true,'Production',$null,$digest,$nonce,$null)
    $iap=$tests.GetType('Mavrylo.Tests.TestSupport.IapTestFactory').GetMethod('Create').Invoke($null,@($db,$apple,$null,$null,$null))
    if($operation -eq 'verify'){
      $r=$iap.VerifyAsync($d.KeyId,[Mavrylo.Dtos.IapVerifyRequest]::new('signed',$null),[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    }else{
      $r=$iap.RestoreAsync($d.KeyId,[Mavrylo.Dtos.IapRestoreRequest]::new('signed','app',$deviceId),[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    }
    $db.ChangeTracker.Clear(); $saved=$db.Subscriptions.Find([object[]]@($stored.OriginalTransactionId))
    [pscustomobject]@{Operation=$operation;Status=$r.Status;ConflictRecorded=($null -ne $saved.TokenConflictDetectedAt);NullTokenPreserved=($null -eq $saved.AppAccountToken);AdoptedCanonicalToken=($saved.AppAccountToken -eq $oldToken);RevocationPersisted=($saved.RevokedAt -eq $now);OwnerUnchanged=($null -eq $saved.OwnerAccountId);ClaimUnchanged=($null -eq $saved.ClaimedAt)}|ConvertTo-Json -Compress
  }finally{$fixture.Dispose()}
}
```

The directly related claim probe used the same null-stored-token/T1-binding/T2-signed/T1-canonical setup, with active canonical state (`RevokedAt=null`) and a stored user. Both `alreadyLinked=false` and `true` were exercised using the real `AccountEntitlementService.ClaimAsync`, `EntitlementService` and `SubscriptionOwnershipService`. The linked variant started with `OwnerAccountId=review-account`, `ClaimedAt=now-2 days` and `RequiresAccountSubscription=true`; the first-claim variant had null/null/false. After the first result, the tracker was cleared, both fake tokens were set to null and then T1, and the same claim was retried after each change. Exact observed output:

```json
{"Operation":"claim","AlreadyLinked":false,"Status":503,"ConflictRecorded":true,"NullTokenPreserved":false,"AdoptedCanonicalToken":true,"OwnerUnchanged":true,"ClaimUnchanged":true,"DeviceUnchanged":true}
{"Operation":"claim-retry","AlreadyLinked":false,"Retry":"missing","Status":503,"ConflictRecorded":true,"FirstHashPreserved":true}
{"Operation":"claim-retry","AlreadyLinked":false,"Retry":"former","Status":503,"ConflictRecorded":true,"FirstHashPreserved":true}
{"Operation":"claim","AlreadyLinked":true,"Status":503,"ConflictRecorded":true,"NullTokenPreserved":false,"AdoptedCanonicalToken":true,"OwnerUnchanged":true,"ClaimUnchanged":true,"DeviceUnchanged":true}
{"Operation":"claim-retry","AlreadyLinked":true,"Retry":"missing","Status":503,"ConflictRecorded":true,"FirstHashPreserved":true}
{"Operation":"claim-retry","AlreadyLinked":true,"Retry":"former","Status":503,"ConflictRecorded":true,"FirstHashPreserved":true}
```

**Required next action:** repair N1 in the existing reconciliation paths and obtain the next scoped verification/review. Keep all saved evidence. R1–R3's corrected authorization/selection behavior should remain intact. Do not call Task2B.2 accepted or start the next client/quota implementation on this report's authority.
