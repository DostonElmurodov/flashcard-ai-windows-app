# Task10 behavioral RED observations

These are contemporaneous results transcribed from the tool output; the earliest
focused runs were not redirected to a raw log file. Subsequent affected-suite
RED and GREEN raw logs are in this directory.

1. Before the Program/startup and runtime host changes, with the newly added
   `TestModeHostBoundaryTests` and the narrowly extended `ApiFactory`:
   `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`
   (process-only), then
   `dotnet test tests/Mavrylo.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~TestModeHostBoundaryTests --logger 'console;verbosity=normal'`.
   Result: **4 total, 1 passed, 3 failed**. `NonDevelopmentTrueFailsSpecificallyBeforeMigration` failed for both Staging and Production because *no InvalidOperationException was thrown*. `SignedSandboxFalseRemainsUsableAndReloadCannotEnableTestMode` failed because the public `test_mode` flag became `true` after runtime configuration changed. `DevelopmentCanEnableAfterStartup` passed. The test configuration supplied positive quotas, enabled assertion/protection, signed Sandbox policy and fake required identity/JWT settings; the failures were the intended behavior, not startup-configuration or database errors.

2. After adding initial inventory SQL tests, `dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter FullyQualifiedName~SubscriptionInventorySqlTests --logger 'console;verbosity=normal'` first produced **7 total, 6 passed, 1 failed**: PostgreSQL folded the constant invalid-settings cast and caused a valid-settings query to fail. The guard was made dependent on the runtime setting.

3. After adding whitespace and empty-table cases, the same inventory command
   produced **8 total, 6 passed, 2 failed**. Whitespace-only original IDs were
   undercounted by PostgreSQL's default `btrim` (expected 4, actual 2), and the
   empty-table total returned NULL counts. The implementation now uses the .NET
   whitespace character set and `ROLLUP`; **8/8 passed** in the subsequent run.

4. The first affected Debug suite after requiring a trusted host had **97 total,
   83 passed, 14 failed**. The failures were direct unit constructions which
   intentionally test Development behavior but had no host fixture. They were
   corrected with an explicit `FakeEnvironment("Development")`. Raw output:
   `task-10-affected-debug.log`. Corrected run: **97/97 passed**, raw output:
   `task-10-affected-debug-green.log`.

The final startup-negative test additionally uses an intentionally inaccessible
127.0.0.1:1 database, so its specific TestMode exception proves the guard ran
before `Database.MigrateAsync` attempted a connection.
