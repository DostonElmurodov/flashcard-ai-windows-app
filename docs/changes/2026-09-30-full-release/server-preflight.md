# Live server metadata — 2026-09-30

Evidence: [read-only run 36743325476](https://github.com/DostonElmurodov/mavrylo/actions/runs/36743325476), workflow source `0c1b99ace1600fdc1205a400019180772d5f9386`. Preflight succeeded; image build/push and deploy jobs were both skipped.

## Observed on the target deployment

- Compose service: `api`, running since `2026-09-26T17:30:33.583781529Z`.
- Image uses mutable `latest`; OCI source revision: `473a39bee299f3df4c7fdcf6f45502f880a9bd8b`.
- Container image ID: `sha256:d7fe5a48d8b805ac33eaa7ea50f27031c8dde2baf7df4b9982ffef0b894b96b2`. This is a local image ID, not a registry manifest digest.
- Environment: `TestMode__Enabled=true`, `LegacyAuth__Enabled=false`, `AiProtection__Enabled=true`, `AiProtection__RequireAssertion=true`.
- Trusted Apple environment: `Sandbox`; bundle `com.mavrylo.owlai`; products `com.flashcardai.owlai.premium.monthly,com.flashcardai.owlai.premium.yearly`.
- AI provider: OpenAI; model `gpt-6-luna`; configured Gemini model `gemini-2.5-flash`.
- Legacy quota env values: free 40/day, account 200/day and 30/minute. These are not approval of the new operation quotas.
- Selected secret fields were present. Their values were not emitted.
- Mounts: `/etc/owl-ai/dp-keys` to `/home/app/.aspnet/DataProtection-Keys` writable; `/etc/owl-ai/secrets` read-only at the same container path; `/var/log/mavrylo-api` to `/app/logs` writable.
- Host `pg_dump` and `psql` are available. `sudo -n -l` succeeds; this does not establish permission for any particular privileged command.

## Limits and next step

No database was queried, no server configuration or container was changed, and no provider call was made. The deployment directory is GitHub-masked in the log and is not inferred from older documentation. No allowlisted AiSpend, AiOperationQuota, AppAppleId or ASPNETCORE_ENVIRONMENT values appeared; absence from container environment is not proof that a value is absent from every configuration source.

Read-only aggregate database inventory is next. Production cutover still requires backup/restore rehearsal, correct environment separation, approved limits and any necessary historical reconciliation. A successful preflight is not release readiness.
