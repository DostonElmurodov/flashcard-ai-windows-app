# Task 6A — stable purchase/account operation quota and retained accounting lineage

> Fresh implementer/reviewer after accepted 2B.2/3B. The approved B4 policy keeps different authenticated accounts independent. Owner credentials establish authority; they do not imply that every purchase/account reached by one phone has a single budget. This corrected brief supersedes the earlier draft's universal owner/account union. Retained-word identity is the separate `task-6B-word-order-brief.md`.

**Base:** record accepted B/I SHAs descended from e7dfd1a/f6a9e19. Preserve accepted Tasks4/5/7/8/9 and bounded Task6 validation. D2's 40 free/day, 200 paid/day, 30/minute remain UNAPPROVED proposals. Use explicit synthetic limits; install no live quota/budget.

## Canonical accounting scopes

A purchase scope is `(Environment, OriginalTransactionId)`, never a client token/UUID. All owner-authorized and restored iPhones using that purchase select the same durable purchase scope. If that exact purchase has an existing account claim, its mobile and Windows operations select the account's scope, even when the phone is logged out. A deleted-account tombstone retains that accounting scope without retaining a usable session/private account data.

An unrelated account remains independent when the same installation logs into it. Two purchases controlled by the same anonymous owner may have different valid desktop-account claims; their account scopes remain independent. Never merge account roots through a common installation, owner, AppTransaction ID or prior login. Do not prohibit otherwise valid claims just to simplify quota topology. When account and independent mobile entitlements are both available, consume only the canonical source selected by the frozen DeviceContext contract, not both allowances and not an invented combined subject.

Unclaimed purchase scopes may be distinct. A genuinely new purchase by the originating owner retains that original installation's prior free usage through a recorded one-time attribution below. A new phone restoring an existing purchase selects its already-consumed purchase/account scope; its unrelated pre-restore free history remains retained in its own free scope. Restoration does not merge that new installation's other purchases/accounts. Unknown new anonymous installs remain a residual identity case bounded by Task7 aggregate guest/global money limits, not a claim of one quota per person.

## Additive schema and exact services

Create `UsageSubjectEntity(Id text PK, ParentId nullable FK, CreatedAt)`; roots have kind/prefix `free:<Device.Id>`, `purchase:<Environment>:<OriginalTransactionId>`, `account:<accountId>`. Parent pointers are permitted only from an unclaimed purchase scope to its actually claimed account root. Account roots never become another account's child. Create `PurchaseUsageScopeEntity(Environment,OriginalTransactionId composite PK,SubjectId,OriginatingDeviceId nullable,CreatedAt)`; `UsageBucketEntity(SubjectId,WindowKind,WindowStartUtc,Count bigint nonnegative)` composite PK; `UsageImportEntity(LegacyUsageId unique,ImportedCount,SubjectId,ImportedAt)`; `PrePurchaseUsageAttributionEntity(DeviceId,WindowKind,WindowStartUtc composite PK,PurchaseEnvironment,OriginalTransactionId,AttributedCount)`; singleton `UsageAccountingStateEntity(Id=1,Ready,MigrationVersion)`. No account/device cascading deletion. Retain original `ai_usage` rows as audit evidence.

Create `UsageSubjectResolver.cs`:

```text
ResolveDeviceAsync(DeviceContextService.DeviceContext context, CancellationToken ct)
  -> Task<UsageSubject>  // string Id
ResolveAccountAsync(string authenticatedAccountId, CancellationToken ct)
  -> Task<UsageSubject>
InitializePurchaseAsync(string environment, string originalTransactionId,
  string? provenOriginatingDeviceId, CancellationToken ct) -> Task<UsageSubject>
AttachPurchaseToAccountAsync(string environment, string originalTransactionId,
  string provenClaimAccountId, CancellationToken ct) -> Task<UsageSubject>
```

The initialization caller passes originating device only after strong server-token purchase proof. Mobile restore passes null and retrieves/creates purchase scope without importing the new phone's free counters. Account attachment is invoked inside the accepted successful claim transaction after ownership arbitration; it must recheck that exact stored claim. Do not expose a generic merge-arbitrary-source-keys interface. Resolve existing claims created before Task6 identically after migration.

Create `OperationQuotaService.TryReserveAsync(UsageSubject subject, AiQuotaClass quotaClass, CancellationToken ct): Task<OperationQuotaDecision>`, enum Free/Paid, decision `(bool Allowed,string? Code,int? RetryAfterSeconds)`. No request accepts subject/account/owner IDs as quota selectors.

## Atomic counters and binding changes

Use PostgreSQL transaction-scoped advisory lock `hashtextextended('owlai-usage-topology-v1',0)`: shared for root resolution/reservation; exclusive for purchase-to-account attachment/import. User→purchase→topology remains claim/deletion order. Quota never obtains user/purchase locks after topology. Do not hold locks during Apple/provider calls. A reserve resolves the current root again under shared lock and keeps it until commit. Attachment adds the purchase's buckets into its actual account root and redirects only that purchase root, in one transaction. Repeated attachment does nothing; attachment to a different account is rejected by existing claim/tombstone rules. This prevents late quota writes to an abandoned purchase root without merging independent accounts.

Original pre-purchase usage: on the first strongly proven new purchase, under the same exclusive topology lock, copy current free day/minute bucket usage into that purchase scope and insert the unique per-device/window attribution rows. Keep the free counters as evidence; an attribution is counted only once in the new authoritative scope, not copied to subsequent unrelated purchases. Do not return the already-paid originating installation to a fresh free allowance on expiry/logout. If old history cannot identify the originating purchase, do not guess; migration reconciliation is required. A fresh restoring phone never becomes the original purchaser for this attribution.

Within one DB transaction, conditional `INSERT ... ON CONFLICT ... UPDATE Count=Count+1 WHERE Count<limit` reserves UTC day and each applicable minute window. Roll back all applicable dimensions if any denies. Paid has day+minute; free has daily and an optional minute dimension only when explicitly configured. Every permitted free/trial/premium/grace AI route uses this path once per logical operation before provider. Preserve reservation-on-valid-request semantics: provider failure/spend denial does not automatically refund operations; fallback/retry makes a separate Task7 money reservation, not another operation reservation. Cache/same-word calls keep existing logical-operation semantics.

Money pools are independent of quota subjects: device requests without a verified selected linked account, including paid restored phones, use Task7 aggregate guest pool. A verified selected account uses account pool. Preserve cumulative global/pool counters, every-provider-attempt reservation and uncertain-timeout behavior; do not move/reset money when purchase quota joins an account.

Configuration: `AiOperationQuota:FreeDailyLimit`, `PaidDailyLimit`, `PaidMinuteLimit` are explicit positive values for their normal-mode AI class, with no implicit defaults. `FreeMinuteLimit` is optional; absence means no free-minute commercial dimension, not a startup/admission failure or a request for another product decision. If explicitly configured it must be positive. Missing/invalid required values fail the affected AI class with 503 `ai_quota_unconfigured`/zero provider calls; do not block migrations/unrelated routes. Tests use clearly synthetic limits such as2/day,1/minute, including free daily without free-minute configuration. Existing isolated TestMode behavior remains. Live D2 and money amounts are separately controlled by root/user; no answer is inferred from elapsed time.

429 body `{code:"ai_quota_exceeded",error:"AI usage limit reached."}`; Retry-After is ceiling seconds until all actually blocking day/minute windows permit another operation (max reset if both block, minimum1), using server UTC. No fixed60 or client guessed retry. Unready/corrupt usage state returns503 `usage_reconciliation_required`, zero provider attempts.

## Migration, deletion and cutover

Add migration `AddStableOperationUsage`. Import every legacy `ai_usage` row once by Id, preserving both yyyy-MM-dd day and yyyy-MM-ddTHH:mm minute formats. `account:` keys map to the exact account, per-key rows through exact `Device.KeyId` to free/originally attributed scope only when provenance is established. Sum histories legitimately attributed to a claimed purchase/account; never replace with max, truncate counts above limit or discard pre-claim consumption. Import/rerun/restart cannot duplicate counts.

Legacy caller-supplied/overwritten UUID equality is not provenance. Inventory ambiguous original-purchase attribution, missing keys and invalid buckets; preserve rows and leave affected accounting readiness unresolved. Sanitized production-copy reconciliation must settle these before cutover. Do not silently give a legacy restored purchase zero old usage. No production access occurs in this task.

Deleting an account removes private data/sessions normally but retains its opaque quota root, attributed purchase scopes, consumption and subscription tombstones. Mobile restore of a tombstoned purchase uses that retained root; a new unrelated account remains independent and cannot reclaim the purchase. Logout never changes root for an independently proven mobile purchase.

Actual cutover requires bounded admission pause: old replicas must stop writing old counters before imports activate. New admissions fail closed until Ready. No mixed old/new writers or rollback that restores independent allowances. Keep source row counts, per-window sums, import IDs and unresolved inventory in the migration evidence.

## Files, RED/GREEN and review

Modify AiUsageService/AccountAiUsageService into adapters or remove them from admission; both AI filters; resolver integration in DeviceContext and narrow purchase/claim/deletion hooks; entities/AppDbContext/migration/DI. Preserve exact raw-body proof and accepted ExtractionRequestValidator/multipart ordering. No content-library scope changes in6A. Windows normally consumes existing Retry-After contract; no accepted Task9 redesign.

- [ ] RED `UsageSubjectTests`: same purchase across original/new restored keys, linked Windows/account and logged-out phone retains total; key/UUID/language/server restart does not reset it. Original pre-purchase usage is attributed once; new-phone unrelated free usage is not falsely attributed to the old purchase.
- [ ] RED independent-account controls: same phone logs into A/B; same owner has different purchases validly claimed to A/B; neither quota root/count changes through common installation/owner. Invalid headers/raw IDs cannot select a scope.
- [ ] RED `UsageSubjectPostgresTests`:20 parallel requests/two instances with one left admit exactly one; day+minute rollback; reserve racing claim; repeated attachment/import; minute/midnight rollover; account deletion/tombstone; real Retry-After.
- [ ] Implement schema/import/resolver/reservations and limited proven-claim hooks, then focused GREEN. Every paid/free/trial/grace AI route charges once; no denied auth/malformed input reaches quota/provider. Valid provider failure keeps existing operation charge semantics.
- [ ] Run Task7 cumulative-money tests unchanged, paid-device guest pool/fallback/retry controls, populated migration twice, normal-mode AI suites, full backend Release tests/build. Replace B4 skip with meaningful synthetic-limit cases, retaining independent-account control.
- [ ] Simplify, fresh scoped review, fix/rerun, commit exact SHA/test evidence. Root owns production activation and live numbers. Word identity/order remains separate6B, not claimed fixed here.

Use existing isolated PostgreSQL harness; `dotnet test tests/Mavrylo.Tests.csproj -c Release --no-restore` and `dotnet build Mavrylo.csproj -c Release --no-restore`. Focus selectors UsageSubject/OperationQuota/PaidAccessAuditRegression/Extraction/AiSpend. Rough local effort6–10h including concurrency/migration review; real data reconciliation and live limit decisions remain external gates.
