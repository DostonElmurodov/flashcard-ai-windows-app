-- Pre-migration aggregate inventory. Run with an authorized read-only role.
-- This file selects only counts/migration names; it does not emit user, device,
-- purchase, prompt, receipt, or credential values. Stop on SQL errors.
BEGIN READ ONLY;

SELECT count(*) AS applied_migrations,
       max("MigrationId") AS latest_applied_migration
FROM "__EFMigrationsHistory";

SELECT (SELECT count(*) FROM devices) AS devices,
       (SELECT count(*) FROM subscriptions) AS subscriptions,
       (SELECT count(*) FROM ai_usage) AS legacy_usage_rows,
       (SELECT count(*) FROM device_words) AS saved_word_rows,
       (SELECT count(*) FROM devices d
        WHERE to_jsonb(d)->>'RequiresAccountSubscription' = 'true') AS legacy_account_markers;

SELECT count(*) AS duplicate_client_id_groups,
       coalesce(sum(group_size), 0) AS affected_rows
FROM (
    SELECT count(*) AS group_size
    FROM device_words
    WHERE "ClientWordId" IS NOT NULL
    GROUP BY "DeviceUuid", "ClientWordId"
    HAVING count(*) > 1
) groups;

SELECT count(*) FILTER (WHERE "Count" < 0) AS negative_legacy_usage_rows,
       count(*) FILTER (WHERE "Date" !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}(T[0-9]{2}:[0-9]{2})?$')
           AS malformed_legacy_window_shape_rows,
       count(*) FILTER (WHERE "KeyId" LIKE 'account:%') AS account_usage_rows
FROM ai_usage;

ROLLBACK;
