# Backend shared subscription implementation
1. Test immutable ownership, tombstones, account entitlement selection and device denial first.
2. Add additive owner/claim columns; atomic first claim after canonical Apple verification.
3. Bind account and device proofs through a resource filter; reuse active account-session validation.
4. Add account entitlement, AI and catalog; account quota shared with bound device AI.
5. Preserve restore/logout/deletion semantics and verify builds, focused tests and PostgreSQL concurrency when available.

## Verification completed
- `dotnet test tests/Mavrylo.Tests.csproj --no-restore`: 180 passed, 0 failed, real PostgreSQL 16.15 fixture databases.
- `dotnet build Mavrylo.csproj --no-restore -c Release`: succeeded, 0 errors.
- EF `migrations has-pending-model-changes`: no pending model changes.
- Tests cover full concurrent first claims (200/409), atomic ownership, tombstone deletion, independent publication owners, shared device/account quota, 20 concurrent cost reservations, active-session logout rejection, source precedence/refresh, actual ECDSA assertion body/path/key/audience/replay binding, and unsigned transaction rejection in both Development and Production.
- Existing NuGet warnings remain: test transitive SQLitePCLRaw.lib.e_sqlite3 2.1.11 and SSH.NET 2025.1.0 advisory warnings; System.Formats.Cbor direct reference pruning warning. No dependency versions were changed in this feature.
- Not run: real Apple Sandbox subscription on attested iPhone, live refund notification, production deployment. See docs/shared-subscription-api.md.
