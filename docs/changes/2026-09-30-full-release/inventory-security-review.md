# Read-only database inventory security/correctness review

Date: 2026-09-30
Reviewer: clean-context inventory_security_review
Backend base: 0c1b99ace1600fdc1205a400019180772d5f9386
Result: ACCEPTED for the bounded read-only inventory workflow. No open actionable findings in the frozen source.

## Scope and evidence

Reviewed the inventory workflow addition, bundled Python runner, new pre-migration aggregate SQL, existing subscription audit SQL, and focused boundary tests. Reviewed the prior server-preflight report for deployment context. Implementer explicitly froze source before final acceptance. No remote commands, database queries, Git mutations, product changes, or deployment actions were performed by this reviewer.

## Finding resolved during review

[P1] Host/container endpoint ambiguity: the original implementation passed the API container's connection Host to a host-side psql process. Loopback or container-specific DNS could thereby select a different database. The final implementation reads structured Docker network mode, accepts numeric endpoints only, permits loopback only under host networking, rejects unspecified/multicast/link-local/scoped endpoints, and evaluates IPv4-mapped IPv6 using the embedded IPv4 address. Unsupported targets fail closed before any database query. Focused regressions and independent mapped-address probes passed.

## Verified boundaries

- Inventory runs only for manual dispatch with mode=inventory. Build/image push and deploy job conditions exclude that mode.
- Deployment path is restricted before remote shell interpolation. Bundled source and SQL are repository files, not environment text interpreted as source.
- Container discovery requires exactly one running Compose api container; inspect is structured JSON, and environment entries preserve values and reject duplicates.
- Connection parsing supports a narrow explicit Npgsql key/value subset and rejects unknown or ambiguous options. Credentials and target values enter psql through process environment only. Inherited PG settings are removed before authoritative values are installed.
- Both query batches execute in read-only transactions ending in rollback. Read-only defaults, statement/lock timeouts, connection timeout, and subprocess timeout bound the query path. SQL interpolation is limited to validated audit environment/product alphabets.
- Subprocess stderr is suppressed and failures are mapped to fixed messages. Output is emitted only after both result sets validate; keys/buckets are fixed, counts numeric, and migration names constrained. No raw user/device/account IDs, purchase data, connection values, or credentials are output.
- Inventory script performs no server file writes, container mutation, database data/schema mutation, image build/push, or deployment.

## Validation

After source freeze, independently ran `python -B ops/linux/test_read_only_inventory.py`: 5 tests passed in 0.088s. `git diff --check` passed (only the existing Windows line-ending warning was emitted).

Additional independent local probes verified that TimeoutExpired carrying a secret in argv/stdout/stderr produces a sanitized error with suppressed exception context, and that raw IPv6 unspecified, mapped loopback/unspecified/link-local, and scoped IPv6 endpoints are rejected outside host networking. No remote service was contacted by these probes.

Limits: this review does not establish live DB schema compatibility, connectivity, installed remote Python availability, actual aggregate results, or release readiness. Narrow unsupported connection forms deliberately stop safely. SQL semantics were inspected here; the existing PostgreSQL integration tests were not rerun by this reviewer.

## Frozen file SHA-256

| File | SHA-256 |
|---|---|
| .github/workflows/api-ci-cd.yml | 3AAAA2B692C532EF277F797580AAD52D7AD11F1222BAFF304769BAE8721F582F |
| ops/linux/read-only-inventory.py | 70E5894EF0840FEC71AFD33792BE47C22C71D86122C998BB1D57F25E28FD7038 |
| ops/linux/test_read_only_inventory.py | 8EFA794D3BD78AED276571151108741BFB418727ABFE6E8495379BFEF333CC53 |
| ops/linux/backend-inventory-before.sql | 76371033C1E2C123AD246917C82541069926CDB4230BC3EFF6ADAE22DC073CF0 |
| ops/audit-subscription-state.sql | D5A2173353061E550FD2BA4553C0BD1922356407C150A7E0B6E93F603CC349AD |

## Diagnostic-only addendum after run 36745752442

Reviewed frozen diagnostic diff against d6b75f0 on 2026-09-30. Result: ACCEPTED; no actionable findings. This acceptance covers only the diagnostic change, not a diagnosis of the failed live run.

The patch adds literal stage labels for Compose selection, container inspect, psql availability, base inventory query, and subscription audit query. A bounded `psql --version` check is read-only. Query stderr is now captured in memory, while psql is requested to emit SQLSTATE-only query errors. The classifier emits only constant categories from an explicit SQLSTATE map; unknown codes emit a fixed generic message. It never emits raw stderr, stdout, matched context, argv, targets, or credentials. Non-query stderr remains discarded. Timeout and OS failures retain suppressed exception context.

The final diff changes only the Python runner and boundary tests. Connection target validation, environment cleanup, credential transport, SQL text, read-only transactions, rollback, timeout limits, aggregate output validation, workflow guards, and deployment behavior are unchanged.

After implementer freeze, independently ran `python -B ops/linux/test_read_only_inventory.py`: 6 tests passed in 0.102s. The added known/unknown SQLSTATE regression includes secret-bearing stdout/stderr and asserts exact fixed diagnostic output. Existing tests cover process-error redaction, endpoint safety, environment cleanup, and aggregate output. `git diff --check` passed with only Windows line-ending warnings. No remote actions were performed.

Failure-cause limit: a generic error from the earlier run does not establish which stage failed. SQLSTATE unavailable or outside the allowlist will remain generic within the identified stage; this is intentional fail-closed diagnostic behavior.

Frozen diagnostic SHA-256:

| File | SHA-256 |
|---|---|
| ops/linux/read-only-inventory.py | 15C959A7D492CD18ED036363DD85E757FEB2D517C6DD03F6B0A58E234CFA1218 |
| ops/linux/test_read_only_inventory.py | F35DFDDEF315154D6A6EE7F5A7416CBADDDB0BD625A3E5E80DFD7CEB91C8022B |

## Libpq diagnostic addendum after run 36746671645

Reviewed frozen increment against 46ad0e2 on 2026-09-30. Result: ACCEPTED; no actionable findings. The prior run isolated failure to the base inventory query but did not identify a recognized SQLSTATE; this review does not infer its underlying cause.

The increment processes captured stderr only in memory and requires a psql error prefix before selecting from fixed libpq phrase/category pairs. Output categories cover password rejection, pg_hba rejection, missing password, refused connection, TLS failure, and client option failure. SQLSTATE categories retain precedence. Unknown text remains a fixed unclassified failure. Exit status is rendered only within -255..255; other values become the literal out_of_range. No raw stderr, stdout, addresses, usernames, database names, credentials, or matched text is emitted.

Only the runner and tests changed. Query SQL, credential transport, target validation, inherited PG environment cleanup, read-only transactions, rollback, timeouts, successful output validation, and workflow dispatch guards remain unchanged.

After explicit freeze, independently ran `python -B ops/linux/test_read_only_inventory.py`: 7 tests passed in 0.054s. Known and unknown libpq messages include private strings and assert exact fixed output. Independently probed positive/negative out-of-range exit codes, a negative signal status, and rejection of phrase-only text without the psql error prefix; all passed. `git diff --check` passed with only Windows line-ending warnings. No remote actions were performed.

These labels are coarse diagnostic categories, not a guarantee that every client failure is recognized. Unmatched connection messages remain safely unclassified.

Frozen libpq diagnostic SHA-256:

| File | SHA-256 |
|---|---|
| ops/linux/read-only-inventory.py | 85BCA481E29E51576B5D0BAEDF8DBB3954794C98104E8FFE615310E45DF3504B |
| ops/linux/test_read_only_inventory.py | 37FD247480142E6D5244511F7B7D5C15B1E37FF1A62983A88F0CE25BB68296BD |

## Existing API network namespace addendum after run 36747344639

Reviewed frozen increment against d3422b3 on 2026-09-30. Result: ACCEPTED for the bounded read-only inventory path; no actionable findings. The observed pg_hba_rejected category establishes an access-rule rejection, but does not prove that changing the source network namespace will resolve it.

The selected running Compose API inspection now captures container ID, positive PID, start time, network mode and Docker SandboxKey. Non-host mode requires a narrowly validated /run/docker/netns/ or /var/run/docker/netns/ hexadecimal path from that same inspection; missing, shared-container and none networking are rejected. Before each query, the code rechecks exact selected container identity, running state, PID/start time, network mode, service label, and SandboxKey. Changes fail closed.

For that API namespace, the argv list invokes noninteractive sudo and nsenter with only the validated network namespace, then the existing psql command. It does not enter mount/PID/user namespaces or create a container. sudo's explicit preserve list contains exactly PGHOST, PGPORT, PGDATABASE, PGUSER, PGPASSWORD, PGCONNECT_TIMEOUT, PGOPTIONS and PGAPPNAME. Values remain in the process environment, never argv. Existing PG environment cleanup and psql startup-file suppression remain intact. Host mode is selected only from inspected host networking; it is not a failure fallback.

SQL text, read-only defaults/transactions, rollback, statement/lock/connect/process timeouts, numeric endpoint restrictions, aggregate output validation, and workflow guards are unchanged. Namespace failures become fixed allowlisted categories. A denied or missing namespace tool does not trigger a host retry, ACL change, password change, configuration change, container creation, or deployment.

After explicit freeze, independently ran `python -B ops/linux/test_read_only_inventory.py`: 9 tests passed in 0.072s. New tests verify strict namespace paths, changed namespace rejection, exact sudo PG-name preservation, credential-free argv, and redacted permission failures. An additional independent inventory control-flow probe confirmed that namespace failure produces one query attempt, stops before audit/retry, and emits no partial aggregate output. `git diff --check` passed with only Windows line-ending warnings. No remote actions were performed by this reviewer.

Limits: mock/local validation does not prove remote sudo policy, nsenter availability, pg_hba acceptance, or live query success. The approach requires existing authorization for the noninteractive namespace command; denial safely stops. Identity is rechecked immediately before each query, without asserting atomic coordination with unrelated container lifecycle operations.

Frozen namespace-path SHA-256:

| File | SHA-256 |
|---|---|
| ops/linux/read-only-inventory.py | 1949FFFABC6E32F9FBA6099E5E28D0896BC6EA3269F94DB977B5E718A4D5DAF8 |
| ops/linux/test_read_only_inventory.py | F65F16C408CF6C0BC0D27C6290DBBAA8DAD3B9B4AF05669F6CAB6D451E47DE10 |
