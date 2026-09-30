# Manual read-only backend inventory implementation

Implemented in backend checkout `D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\backend` from base `0c1b99ace1600fdc1205a400019180772d5f9386`.

The `workflow_dispatch` workflow now offers `inventory`. It has its own production-environment job, does not depend on `build-test`, and does not build, push, pull, or restart an image. The job bundles the reviewed aggregate SQL and a Python standard-library script, then runs it on the server over SSH stdin. No database inventory has been run against production by this implementation task.

The remote script requires exactly one running Compose `api` container and reads its structured Docker inspection result in memory. It uses only that container's `ConnectionStrings__Default` target and trusted Apple environment/product settings. Ambiguous connection strings and database endpoints fail closed: database hosts must be numeric; loopback is accepted only for host-networked API containers; unspecified, multicast, and link-local addresses are rejected. Passwords remain only in child process environment, never arguments or output. Child stderr and Python exception details are suppressed.

Both inventory queries use PostgreSQL read-only transactions with `ROLLBACK`, server-side statement and lock timeouts, a connection timeout, and a local process timeout. The output parser accepts only fixed aggregate fields, trusted subscription environment buckets, numeric counts, and a validated EF migration name. It reports `AiOperationQuota` and `AiSpend` key presence in the container environment only; absent keys do not establish effective runtime configuration.

Focused local checks passed: `python -B ops/linux/test_read_only_inventory.py` (5 tests), bundled remote source compilation, and `git diff --check`. The tests cover single-container selection, namespace-dependent target rejection, supported and rejected connection-string forms, password placement in environment, inherited `PG*` variable cleansing, read-only/rollback SQL, output validation, and sanitized subprocess failures. A fresh security review found no remaining actionable finding after the endpoint guard was tightened.

Limitations: this has not queried the live database. Deployments whose API connection uses a hostname, a container-only endpoint, an unsupported connection-string option, or missing trusted Apple audit settings stop with a sanitized error. An inventory failure does not imply the database is empty or safe.

## Diagnostic follow-up after first live invocation

Manual run `36745752442` stopped with the generic sanitized failure before any counts were printed; its log did not identify whether Compose, inspection, PostgreSQL client, or either query failed. The script now assigns fixed stage labels to those steps. Query failures request PostgreSQL SQLSTATE-only verbosity and map only known SQLSTATE codes to fixed categories; all other errors remain generic. The PostgreSQL client version check runs before a database query, which can identify a missing host client without exposing its stderr. No database, deployment, or image operation was changed.

Local follow-up verification passed: `python -B ops/linux/test_read_only_inventory.py` (6 tests), bundled remote source compilation, and `git diff --check`. The new regression checks that a secret-bearing PostgreSQL error yields only a fixed stage and category, while an unknown code yields a generic message. The next live run is needed to identify the failing stage; no cause has been inferred from the first failure.

## API network namespace follow-up

Run `36747344639` confirmed `client_error=pg_hba_rejected; exit_status=2` on the base query. This is evidence that the host-origin PostgreSQL connection was rejected by `pg_hba.conf`; it does not prove that a connection from the API network namespace will succeed. No PostgreSQL ACL or password was changed.

For a bridged API, the inventory now validates Docker's existing network namespace path from the exact selected running container and rechecks its ID, PID, start time, network mode, and namespace path before each query. It executes host `psql` through `sudo -n` and `nsenter --net` using only exact PostgreSQL environment key names on argv; the password remains in the process environment. Host-networked APIs continue to use direct host `psql`. Missing namespace metadata or permissions fail closed with a fixed error and no host-network fallback. Read-only transactions, timeouts, aggregate output validation, and error redaction are unchanged.

Local checks passed: `python -B ops/linux/test_read_only_inventory.py` (9 tests), bundled remote source compilation, and `git diff --check`. The next live run is required to verify whether the existing namespace path can reach the configured database under current server permissions.
