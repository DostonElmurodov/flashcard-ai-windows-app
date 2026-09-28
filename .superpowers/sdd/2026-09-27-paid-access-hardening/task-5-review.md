## Spec compliance — ❌ Issues found

- **Important: JSON interpretation is still not equivalent to MVC for request encodings.** `src/Mavrylo.Services/Services/AiRequestContextReader.cs:13-27` accepts the JSON media type without considering `charset` and then parses the original bytes as UTF-8. MVC's formatter honors the charset. An invalidly encoded request can therefore reserve a word and spend usage before MVC rejects it. This violates Task 5's no-spend invalid-body requirement; details and a confirmed focused reproduction are below.
- The requested capitalization, duplicate rejection, three JSON operations, explicit multipart no-word policy, original-byte signature check, and PostgreSQL last-slot coordination are implemented: `AiRequestContextReader.cs:35-69`; `tests/AiRequestInterpretationTests.cs:34,54,79,102,159,224`; `tests/PaidAccessAuditRegressionTests.cs:279-327`.
- ⚠️ Multipart invalid-image/form validation still happens after the resource-filter quota gate (`Filters/AiProtectionFilter.cs:169-175`; `Areas/OwlAI/Controllers/AiController.cs:38-44`). The report explicitly limits its no-spend guarantee to JSON word operations. The valid extraction policy is covered, but this change does not establish no-spend behavior for malformed multipart requests. This is a scope limitation, not an additional newly introduced defect.
- ⚠️ Whole paid-access hardening remains incomplete: the supplied final full-suite log has seven other audit failures and one deferred case. This task review does not clear those separate requirements (`task5-results/full-final2.log:13-79`).

## Strengths

- DTO deserialization uses MVC's actual serializer options and the endpoint's existing typed DTO; shared validation is reused instead of maintaining another word/language rule set (`src/Mavrylo.Services/Services/AiRequestContextReader.cs:44-69`; `Program.cs:177-183`; `src/Mavrylo.Services/Dtos/Dtos.cs:61-63`). Leaving the DTO declarations unchanged is appropriate here: their contract did not need changing.
- Duplicate detection operates on decoded JSON names and respects the configured case comparison. The request remains untouched for proof verification and subsequent binding (`src/Mavrylo.Services/Services/AiRequestContextReader.cs:27-38`; `Filters/AiProtectionFilter.cs:66-99`). The real ECDSA/CBOR test proves an unchanged uppercase/whitespace body succeeds while adding one space fails with zero calls/word rows/usage (`tests/AiRequestInterpretationTests.cs:159-191`).
- PostgreSQL mutations coordinate on one transaction-scoped device lock, and READ COMMITTED allows the waiting mutation to see its predecessor's committed count. Reservation, upsert and delete all use it; inactive reactivation consumes a slot (`src/Mavrylo.Services/Services/DeviceWordService.cs:35-46,82,116,175-192`). The separate-context PostgreSQL tests cover reservation/reservation, reservation/upsert and upsert/upsert, with one winner and a final count of ten (`tests/AiRequestInterpretationTests.cs:224-245`).
- The HTTP B3 regression now proves both analyze-word and word-detail variants make exactly ten provider calls, reject the eleventh new word, and allow a cached existing-word repeat while increasing daily usage (`tests/PaidAccessAuditRegressionTests.cs:279-327`).
- The shared account/device usage test was legitimately repaired: it now uses valid operations against a fixed provider and retains its exact two-unit usage assertion (`tests/SharedSubscriptionPostgresTests.cs:174,203-209`). Account authentication remains the existing account scheme without an App Attest requirement (`Areas/OwlAI/Controllers/AccountAiController.cs:10-21`).

## Issues

### Critical

- None found within this task's scope.

### Important — should fix before accepting Task 5

1. **[P2] Honor MVC encoding selection before reservation or quota consumption.** Location: `src/Mavrylo.Services/Services/AiRequestContextReader.cs:13-27`, called by `Filters/AiProtectionFilter.cs:91-99` and `Filters/AccountAiProtectionFilter.cs:18-31`.

   `IsJsonContentType` only inspects `parsed.MediaType`; `Read` always passes raw bytes to `JsonDocument.Parse`. For UTF-8 bytes containing a valid word but `Content-Type: application/json; charset=utf-16`, the reader succeeds. The device filter can then reserve a new word and consume daily usage (`Filters/AiProtectionFilter.cs:144-175`), and the account filter can consume usage (`Filters/AccountAiProtectionFilter.cs:55-61`), before MVC rejects the same body. An unsupported charset also passes this precheck and is rejected later. Conversely, a valid UTF-16 request accepted by MVC is now rejected by the reader.

   **Confirmed comparison using the compiled Task 5 reader and the installed ASP.NET Core `SystemTextJsonInputFormatter`, outside the checkout:**

   | Bytes / declared charset | Task 5 reader | MVC formatter |
   |---|---|---|
   | UTF-8 / utf-8 | accepts `alpha` | accepts `alpha` |
   | UTF-8 / utf-16 | accepts `alpha` | `HasError=true`, invalid JSON after transcoding |
   | UTF-8 / iso-8859-1 | accepts `alpha` | `HasError=true`, `UnsupportedContentTypeException` |
   | UTF-16 / utf-16 | rejects `invalid AI JSON request` | accepts `alpha` |

   The probe establishes the parsing mismatch directly; post-reader spending follows from the filter control flow cited above. It was not a full HTTP/database run, and no provider or database was invoked. Reproduction source: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/task5-review-probe/Program.cs`.

   **Recommended correction:** select/reject the encoding using the same MVC formatter contract and transcode a separate interpretation copy after App Attest verifies the untouched original bytes. Keep MVC's original request stream/body intact. Share this interpretation path between both filters. Add focused HTTP cases for supported UTF-16, mismatched UTF-16, and unsupported charsets; rejected cases must leave provider calls, words and usage at zero. Extend the existing signature proof to a supported non-UTF-8 body if that support is retained. An explicit UTF-8-only contract would instead need deliberate enforcement by both MVC and the reader, but would narrow the current MVC contract.

### Minor — worthwhile follow-up

2. **The adapted test-mode AI assertions no longer exercise the policy they name.** `tests/TestModeRouteTests.cs:49-50,119-120` still send 65 empty-word requests. New validation now returns 400 before reaching `TestModePolicy` or quota handling (`Filters/AiProtectionFilter.cs:97-113`; `Filters/AccountAiProtectionFilter.cs:29-46`). Those loops would pass with the AI test-mode bypass removed; the valid off-mode probes only prove that the off-mode gates work. Use the newly installed fixed provider to send valid AI requests in test mode, assert success and zero usage, and retain a separate invalid-input assertion. The valid device-word upsert coverage remains useful; this finding concerns the AI portions.

3. **Reported verification is not warning-free.** `task5-results/focused-final2.log:1-4` contains NU1903 advisories for SQLitePCLRaw.lib.e_sqlite3 2.1.11 and SSH.NET 2025.1.0, plus NU1510; `task5-results/release-build.log:1` contains NU1510. These are disclosed, pre-existing dependency issues and are not attributable to this implementation. They should remain tracked rather than describing the build as pristine.

## Checks and review boundaries

- Reviewed `68cbce3c1e9053145283099e8c44c83d3f3a3ced` → `83be21be8d2bd3e28eeb287ab53d82a9b83c7027` using the supplied package. The initial combined tool output was truncated, so the unread portions were retrieved in bounded chunks. No Git commands or product edits were performed.
- Named risk **DTO/route/formatter parity**: checked the unchanged DTO declarations, MVC JSON configuration, and two AI controllers. Exact DTOs and authentication match; the concrete encoding mismatch is issue 1. Read only the missing continuation of word validation and the service's secondary-language branch because the relevant diff hunks ended mid-function; these match the new shared validation.
- Named risk **shared mutation lock completeness**: searched service/controller device-word mutations. The located writes are the existing DeviceWordService add/remove/reservation paths, which now share the lock. No additional writer was found in those application paths. No repository-wide unrelated review was attempted.
- Named risk **spending before MVC rejection**: read the omitted resource-filter tails because the supplied hunks cut off before quota consumption and `next()`. They confirm the order cited in issue 1.
- Inspected supplied logs instead of rerunning suites: corrected red 34 failed / 19 passed (`red-corrected.log:397`); envelope red 2 failed / 59 passed (`red-envelope.log:34`); inactive-row red 1 failed / 11 passed (`red-inactive.log:48`); final focused 137 passed (`focused-final2.log:14`); full 381 passed / 7 failed / 1 skipped (`full-final2.log:79`). The counts match the implementation report.
- Ran only the focused, independent encoding comparison for the new doubt. The first standalone-project restore encountered the same sandboxed NuGet.Config access restriction mentioned in the report. Direct compilation against the installed framework reference assemblies and existing Task 5 service assembly succeeded; the resulting four-case probe produced the table above. All probe artifacts are outside the reviewed backend checkout.

## Assessment — task quality: Needs fixes

- The core capitalization bypass and last-slot race have convincing, task-specific coverage, and the shared parsing/locking structure is maintainable. The confirmed charset mismatch still violates the central one-interpretation/no-spend requirement, so Task 5 should return to its implementer for that correction and focused fresh review.
