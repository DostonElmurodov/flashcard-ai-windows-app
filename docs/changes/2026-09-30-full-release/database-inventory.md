# Live aggregate database inventory — 2026-09-30

[Run 36748418193](https://github.com/DostonElmurodov/mavrylo/actions/runs/36748418193) succeeded at tooling commit `6e462486d4cbde68ad8a34d3649dc9415d671dca`. It queried the configured database of the one running API container through that container's existing network namespace. Image build/push and deployment were skipped. Transactions were read-only and rolled back. No access rule, password, container configuration or application record was changed.

## Observed aggregates

| Item | Result |
|---|---:|
| Applied migrations | 9 |
| Latest migration | 20260926142908_AddIncrementalAccountSync |
| Devices | 143 |
| Subscription records | 0 |
| Legacy AI usage records | 10 |
| Saved server word records | 65 |
| Legacy account markers | 0 |
| Duplicate client-word ID groups | 0 |
| Negative usage records | 0 |
| Malformed usage-window string shapes | 0 |
| Account-prefixed usage records | 0 |

The subscription audit also reported total zero. Consequently, the migration's `historical_paid_usage_unknown` issue caused by *existing subscription rows* is not a demonstrated blocker for this snapshot. This does not establish that there are no Apple purchases outside this database or that every other migration/usage mapping will succeed. Calendar validity and exact identity reconciliation still require the restored-copy migration check.

All checked `AiOperationQuota` and `AiSpend` environment keys were absent/empty. This is container-environment evidence, not inspection of every possible configuration source. Trusted Apple environment in the inspected container is Sandbox.

## Resolved inspection issue

The initial host-side PostgreSQL connection was rejected by `pg_hba.conf` (run `36747344639`, fixed category `pg_hba_rejected`, exit 2). The API-namespace path succeeded without changing database ACLs. The helper validates the exact container and namespace and checks identity before each query; credentials remain in process environment and output is restricted to aggregate fields. Independent review and nine local tests passed before this successful live run.

## Next boundary

Create and validate a private backup; restore it to an isolated copy and exercise the actual candidate migrations. No backup, restored-copy test or production migration is claimed by this report. [Structured aggregate evidence](database-inventory-36748418193.json) is saved separately; raw identifiers and credentials were not collected.
