-- Post-migration aggregate inventory. Run only after the target schema exists,
-- preferably first on an isolated restored copy. No case-level references emitted.
BEGIN READ ONLY;

SELECT "Ready", "MigrationVersion" FROM usage_accounting_state WHERE "Id" = 1;

SELECT "Kind", "Reason", count(*) AS issues
FROM usage_reconciliation_issues
GROUP BY "Kind", "Reason"
ORDER BY "Kind", "Reason";

SELECT count(*) AS subscriptions,
       count(*) FILTER (WHERE "TokenConflictDetectedAt" IS NOT NULL) AS token_conflicts,
       count(*) FILTER (WHERE "ClaimedAt" IS NOT NULL AND "OwnerAccountId" IS NULL) AS owner_tombstones
FROM subscriptions;

SELECT "BindingKind", count(*) AS bindings
FROM apple_purchase_bindings
GROUP BY "BindingKind"
ORDER BY "BindingKind";

SELECT "Id" AS bucket, "ChargedMicroUsd", "PendingAttempts"
FROM ai_spend_buckets
ORDER BY "Id";

SELECT "Cohort", "Provider", ("SettledAt" IS NULL) AS pending,
       count(*) AS attempts, coalesce(sum("ChargedMicroUsd"), 0) AS charged_micro_usd
FROM ai_spend_reservations
GROUP BY "Cohort", "Provider", ("SettledAt" IS NULL)
ORDER BY "Cohort", "Provider", pending;

ROLLBACK;
