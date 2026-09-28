# Task 5 report — B3 request interpretation and atomic ten-word limit

## Status and checkout

Implemented and locally committed; awaiting the controller's separate fresh-context review.

- Checkout: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`
- Branch: `codex/paid-access-hardening`
- Base: `68cbce3c1e9053145283099e8c44c83d3f3a3ced`
- Commit: `83be21be8d2bd3e28eeb287ab53d82a9b83c7027` — `Harden AI request interpretation and atomic free word reservations`
- Working tree clean after commit. No push, deployment, purchase, real AI-provider invocation, or live configuration change.
- D1=B preserved: no mandatory account for iOS guest purchase/use. D2 remains pending; tests use synthetic quotas only.

## Resulting behavior

All three JSON AI operations (`analyze-word`, `word-detail`, `review-translation`) on device and account routes use `AiRequestContextReader` with the actual MVC JSON serializer options and the exact endpoint DTO. Snake-case fields are case-insensitive, matching MVC. Camel-case language fields remain unknown fields under MVC's existing snake-case contract and therefore use the same existing defaults in both limiter and action. The old conflicting `DeviceWordService.TryReadWordContext` API was replaced with this one typed reader, as permitted by the brief; no callers remain.

Case-insensitive duplicate JSON property names are rejected with 400, including escaped/decoded names handled by System.Text.Json. Missing, null, blank, invalid-type words, malformed/non-object JSON, invalid language fields, and typed secondary-language errors are rejected before reserving words or consuming usage. Shared word/secondary-language validation is reused from WordAiService. Both filters reject unsupported JSON media types before quota; the existing 16,000-byte JSON request limit is enforced during buffering. Multipart `extract-words` is explicitly separate: no word is known yet, its existing free count check and daily quota policy remain, and no word row is reserved by extraction. Its 20,000,000-byte cap remains.

App Attest still hashes the exact original body bytes and path before interpretation. A real ECDSA/CBOR HTTP test signs an uppercase JSON body with whitespace and a mixed-case route: it succeeds unchanged; appending one space returns 403 with zero provider calls, words, and usage.

New-word reservation, upsert, and delete share a PostgreSQL transaction-scoped advisory lock keyed by the device word subject. PostgreSQL READ COMMITTED refreshes the snapshot after waiting for that lock; the former SERIALIZABLE implementation produced SQLSTATE 40001 instead of predictable denial. With nine active words and two concurrent distinct requests in separate EF contexts, precisely one succeeds, one is denied normally, and the final count is ten. Tests cover reservation/reservation, reservation/upsert, and upsert/upsert, each repeated ten times. Inactive upsert reactivation is counted as a new active slot. AI reservation reuses an existing inactive row instead of violating the unique index, and obeys the same remaining-slot check.

Existing-word operations remain allowed at ten active words and spend daily usage even when served from translation cache. HTTP tests for both word-detail and analyze-word cover `word`/`Word`/`WORD`, ten provider calls for ten distinct words, an eleventh denial, and an allowed existing-word repeat with usage increasing to eleven and provider calls staying ten. Review uses only existing active words, consumes quota, and never creates a new row.

## TDD and checks

All logs and TRX files are outside the checkout at `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task5-results`.

Environment used for database checks:

`OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests`

The fixtures create/drop isolated test databases. Initial restore could not read the sandboxed user NuGet.Config; all subsequent checks used the existing restored dependencies with `--no-restore` successfully.

Commands (each test command also used `--logger 'trx;LogFileName=NAME.trx' --results-directory 'D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task5-results'`):

- Red: `dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter 'FullyQualifiedName~AiRequestInterpretationTests|FullyQualifiedName~DeviceWordConcurrencyTests|FullyQualifiedName~B3_CaseInsensitive'`. `red-corrected.log/.trx`: 34 failed, 19 passed, 53 total. Expected evidence includes uppercase reservation bypass, invalid body spending/reserving, duplicate ambiguity, SERIALIZABLE SQLSTATE 40001, and inactive upsert exceeding ten. The first red run included an incorrect test expecting AnalyzeWord's unknown secondary field to be validated; that single expectation was removed to match MVC, then the corrected red was observed before implementation.
- Envelope red with the same core filter: `red-envelope.log/.trx`: 2 failed, 59 passed. Oversized body was accepted; text/plain received MVC 415 after already reserving a word/spending usage. Both failures were observed before the envelope fix.
- Inactive reservation red: `--filter 'FullyQualifiedName~DeviceWordConcurrencyTests|FullyQualifiedName~B3_CaseInsensitive'`. `red-inactive.log/.trx`: 1 failed, 11 passed. At nine active words, reserving an existing inactive word raised the unique-constraint error before its fix.
- Final focused: core filter plus `AiProtectionFilterTests`, `DeviceWordServiceTests`, `TestModeRouteTests`, and `ClaimApiRequiresBothIdentities`. `focused-final2.log/.trx`: **137 passed, 0 failed**.
- Full suite: `dotnet test tests/Mavrylo.Tests.csproj --no-restore`. `full-final2.log/.trx`: **381 passed, 7 expected unrelated failed, 1 deferred skipped, 389 total**. Exit is intentionally nonzero due to the named audit regressions below.
- Release: `dotnet build Mavrylo.csproj --configuration Release --no-restore`. `release-build.log`: **succeeded, 0 errors, 1 existing NU1510 warning**.
- `git diff --check`: clean. Commit completed and `git status --short` was empty.

The first full run found five existing test cases that relied on sending empty words to probe entitlement precedence or expected invalid requests to consume two account usage units. They were adapted without production entitlement changes: entitlement probes now use valid words; empty test-mode probes remain 400; the account/device shared-usage test now makes two legitimate operations against a fixed synthetic provider and retains its exact two-unit usage assertion. Both affected factories explicitly use synthetic AI to prevent any real provider invocation. The corrected full run has only the seven baseline unrelated audit failures.

### Expected unrelated failures and deferred case

All failures belong to `PaidAccessAuditRegressionTests` and remain for subsequent tasks:

1. `B1_NewAttestedKeyCannotInheritPurchaseBySupplyingOwnerUuid`
2. `B2_DeviceVerifyRejectsDifferentCanonicalPurchaseWithoutPersistingIt`
3. `B2_CopiedReceiptCannotTransferExistingGuestPurchaseToUnrelatedDevice`
4. `B2_CopiedReceiptCannotFirstClaimDesktopOwnershipWithoutPriorOwnerProof`
5. `B5_CachedNonProductionPurchaseCannotGrantProductionAi(environment: "Sandbox")`
6. `B5_CachedNonProductionPurchaseCannotGrantProductionAi(environment: "LocalTesting")`
7. `B6_RefundCannotAcknowledgeSuccessWhileUpdatingUnrelatedPurchase`

Skipped: `B4_NewKeysCannotResetProvenOwnerUsageOrEscapeGlobalGuestBudget`; proof/global guest-budget contract and D2 amounts remain pending. B3 now passes for all six analyze-word/word-detail capitalization cases.

## Simplify and self-review

No installed simplify tool was available. Explicit manual reuse/complexity/efficiency passes were logged after every coding/fix iteration in `task5-results/simplify.log`, and followed by focused checks. Eleven numbered passes cover test scaffolding/corrections, shared exact DTO interpretation/validation, shared lock and inactive guards, HTTP envelopes, real assertion proof, concurrency test simplification, inactive row reuse, removal of the serializer fallback/legacy reader, and reconciliation of existing tests. A final self-review entry traces all routes, original-byte proof, lock scope, post-lock snapshots, unique-row reuse, test results, and known unrelated failures.

Efficiency/refactoring details: reuse ApiFactory and real PostgreSQL fixtures; table-driven invalid payload cases; deserialize DTOs from the already parsed JSON root; share validation methods with the actual AI service; share one namespaced lock helper across mutations; avoid retries/exception-as-denial on expected contention. Production never falls back to separate serializer settings. The older legacy raw-reader test was removed because HTTP tests now verify the endpoint contract directly.

## Limits and remaining concerns

- Whole-suite status is intentionally red until the seven unrelated ownership/environment/lifecycle regressions are addressed. This commit is not evidence that all paid-access hardening is complete.
- Concurrency proof is against real local PostgreSQL with separate contexts and all current DeviceWordService mutation paths. Direct SQL writers outside this service would need to honor the same lock; repository inspection found no additional device-word mutation path.
- Multipart extraction retains its existing no-word policy; invalid multipart image/form validation still belongs to the existing action path. Task5's no-spend invalid-body proof is for JSON word operations.
- Tests emit pre-existing NU1903 advisories for SQLitePCLRaw.lib.e_sqlite3 and SSH.NET; release emits existing NU1510 for System.Formats.Cbor. These dependency issues are outside Task5 and were not changed.
- No migrations or live commercial settings were changed. Fresh-context review is still required and will be dispatched by the controller.

# Review fix round 1 of 5 — encoding parity and restored test-mode coverage

## Status

Fixed the confirmed Important encoding finding and the test-mode coverage Minor finding from the complete `task-5-review.md`. Locally committed on the same branch as `1c31750305e44914bc3c8c7fbdf453d1e8eec7b4` (`Match MVC request encoding before AI usage reservation`), based on `83be21be8d2bd3e28eeb287ab53d82a9b83c7027`. Working tree clean. Awaiting the controller's next fresh review; no push/deploy/live settings, purchases, real provider calls, or dependency upgrades.

## Changes

`AiRequestContextReader.ReadAsync` now selects the actual registered MVC SystemTextJsonInputFormatter that accepts the request, uses its actual serializer settings and supported encodings, and reuses the framework's charset-selection implementation. That selection method is protected, so a small TextInputFormatter adapter exposes it using the actual formatter's SupportedEncodings; no charset whitelist/default rules are duplicated and no reflection is used. The exact request-type mapping is shared by metadata selection and typed DTO interpretation.

Supported UTF-16 is transcoded through Encoding.CreateTranscodingStream on a separate interpretation copy. The original signed bytes, request stream, and path remain untouched for App Attest and later MVC binding. Unsupported media types/charsets return 415; invalid declared encodings return 400 before any word reservation or quota consumption. A concrete self-review edge also confirmed that byte-memory JSON parsing rejected supported BOM-prefixed requests although MVC's stream deserializer accepts them. Interpretation now uses built-in JsonDocument stream parsing so UTF-8/UTF-16 BOM handling also matches MVC without handwritten stripping.

HTTP regressions cover device/account UTF-8 controls, valid UTF-16, UTF-8 bytes mismatched with a UTF-16 declaration, unsupported iso-8859-1, and optional supported UTF-8/UTF-16 BOMs. Non-ASCII café payloads verify decoded word fidelity. Every rejection asserts zero provider calls, zero reserved words, and zero usage rows; accepted account calls assert both daily/minute buckets increment normally. Real ECDSA/CBOR assertion tests cover UTF-16 with and without BOM, and byte tampering still returns 403 with no spending.

Both touched 65-request TestModeRouteTests loops now send valid, distinct words to the existing fixed synthetic provider, assert 200 success and zero usage, and retain a separate empty-word 400 check. Those loops now reach the actual test-mode/quota policy and would fail if its bypass were removed. Off-mode valid subscription/word-limit rejection probes remain.

## Red-to-green verification

Same isolated PostgreSQL fixtures and `OWL_TEST_POSTGRES=Host=127.0.0.1;Port=55440;Database=postgres;Username=owl_tests` were used. Logs/TRX files remain in `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task5-results`.

Covering command:

`dotnet test tests/Mavrylo.Tests.csproj --no-restore --filter 'FullyQualifiedName~AiRequestInterpretationTests|FullyQualifiedName~AiProtectionFilterTests|FullyQualifiedName~TestModeRouteTests|FullyQualifiedName~B3_CaseInsensitive' --logger 'trx;LogFileName=NAME.trx' --results-directory 'D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task5-results'`

- `fix1-red.log/.trx`: **7 expected failures, 114 passed, 121 total** before production changes. Both declared-encoding rejections had spent/reserved state; valid UTF-16 and its real signed request returned 400 instead of 200. Restored valid test-mode loops passed.
- `fix1-green.log/.trx`: **121 passed, 0 failed** after encoding selection/transcoding.
- Narrow BOM red command used `--filter 'FullyQualifiedName~DeclaredEncodingMatchesMvc|FullyQualifiedName~RealAssertionStillVerifiesOriginalUppercaseBytes'`: `fix1-bom-red.log/.trx` had **5 expected failures, 13 passed, 18 total** before the stream-parser fix (four valid BOM cases and the valid signed UTF-16/BOM case returned 400).
- Final covering command, `fix1-focused-final.log/.trx`: **127 passed, 0 failed**.
- `dotnet build Mavrylo.csproj --configuration Release --no-restore`, `fix1-release-final.log`: **succeeded, 0 errors, 1 existing NU1510 warning**.
- `git diff --check`: clean; local commit and empty `git status --short` confirmed.

The whole suite was not rerun this fix round, per controller instruction to use covering checks/Release and only broaden on a concrete wider concern. The earlier full-suite result remains the previously reported 381 passed / seven unrelated audit failures / one deferred B4 skip; it is not a fresh full-suite result for this fix commit. Reservation concurrency/mutation code and unrelated ownership/lifecycle policy were unchanged in this fix.

Five additional numbered explicit simplify passes and a final self-review are appended to `simplify.log`. They cover test setup reuse, actual MVC formatter reuse, the protected API adapter, narrow BOM red tests, built-in stream BOM handling, bounded allocations and preservation of original proof/body. Every coding/fix iteration was followed by a logged simplify pass and focused checks. Final review found no remaining round-1 finding.

## Disclosed limits

Existing NU1903 SQLitePCLRaw.lib.e_sqlite3/SSH.NET test advisories and NU1510 release warning remain disclosed and unchanged; checks are successful but are not described as warning-free. The previously documented malformed multipart scope limitation and the unrelated seven audit regressions/B4 deferral remain outside this fix. No account requirement, D1/D2 decision, lock policy, or live commercial setting changed.
