# Task 5 fix round 1 — independent scoped re-review

## Verdict

- **Spec compliance: PASS for the two requested corrections.**
- **Quality: ACCEPT this fix round.** No new Critical or Important defect found in the supplied fix diff.
- Important encoding/spending finding: **ADDRESSED**.
- Minor test-mode coverage finding: **ADDRESSED**.

Reviewed the supplied `83be21be8d2bd3e28eeb287ab53d82a9b83c7027` → `1c31750305e44914bc3c8c7fbdf453d1e8eec7b4` package against `task-5-brief.md` and the original `task-5-review.md`. Source references below are relative to `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`.

## Original Important finding — ADDRESSED

`AiRequestContextReader.ReadAsync` maps the existing three operations to their actual DTO types, obtains MVC metadata, selects the registered `SystemTextJsonInputFormatter` through `CanRead`, and uses that formatter's serializer options and supported encodings (`src/Mavrylo.Services/Services/AiRequestContextReader.cs:17-35`). The small `TextInputFormatter` adapter reuses `SelectCharacterEncoding` rather than maintaining another charset rule (`:56-64`). The inspected MVC configuration uses the normal JSON formatter with snake-case serializer settings; no competing formatter was present in that registration (`Program.cs:177-183`).

Unsupported media types/charsets now fail before interpretation. Supported non-UTF-8 input is transcoded on an independent memory stream, invalid decoding becomes an input error, and JSON stream parsing preserves MVC-compatible BOM handling (`AiRequestContextReader.cs:32-50,69-78`). Both filters await this shared reader and return on its errors before word reservation or usage consumption (`Filters/AiProtectionFilter.cs:89-105,134,153,169`; `Filters/AccountAiProtectionFilter.cs:25-40,54-60`). This closes the previously demonstrated valid-UTF-8/declared-UTF-16 and unsupported-charset spending paths.

The HTTP regression matrix exercises device and account word-detail routes with UTF-8, supported UTF-16, a mismatched UTF-16 declaration, unsupported iso-8859-1, and supported BOM-prefixed bodies (`tests/AiRequestInterpretationTests.cs:49-74`). Assertions check status, provider calls, reserved device-word rows and exact usage-row counts; valid account requests require both normal usage buckets, while rejections require zero (`:75-83`). The non-ASCII accepted device word is checked against its persisted value. These are meaningful HTTP/DB assertions, not reader-only tests.

The encoding matrix directly covers word-detail on both route families. The other two JSON operations share the same formatter/encoding branch and retain their existing typed validation. This is a coverage boundary, not evidence that each charset was separately exercised against all six routes.

## Original Minor finding — ADDRESSED

Both altered loops now make 65 distinct valid analyze-word requests and require HTTP 200, with a separate empty-word 400 assertion (`tests/TestModeRouteTests.cs:49-51,120-122`). They also require zero usage rows and retain valid off-mode rejection checks (`:54,62,123-126`). The existing fixture installs the fixed synthetic provider and sets device/account quotas to zero (`:175-187`). Consequently these loops reach the test-mode policy and would fail if the relevant bypass were removed; the earlier empty-word early-return blind spot is gone.

## Binding constraints and quality

- **Original-byte App Attest preserved.** Buffering rewinds the original request stream; proof verification hashes the original byte array before the reader is called (`Filters/AiProtectionFilter.cs:64-94,245-246,266-275`). Transcoding cannot mutate that array or request stream. The real ECDSA/CBOR test signs and sends UTF-16 with/without BOM, and the tampering variants require 403 plus zero provider calls, words and usage (`tests/AiRequestInterpretationTests.cs:190-229`).
- **Account authentication preserved.** Account routes still use `AccountAuth.Scheme` and `AccountAiProtectionFilter` (`Areas/OwlAI/Controllers/AccountAiController.cs:11-13`). The fix adds no App Attest/account ownership requirement, and the account filter still rejects a missing account before parsing.
- **Implementation quality.** One request-type mapping drives metadata and interpretation. Framework charset selection and built-in transcoding/BOM support keep the added code small and avoid a competing handwritten decoder. The unchanged 16,000-byte input cap bounds the extra interpretation allocation. The fix does not alter reservation locks, ownership, quotas or lifecycle policy.

## Verification evidence and review boundaries

- Inspected the actual fix diff, relevant current source, red/final logs, and final TRX rather than accepting the implementation report's conclusions. The first combined output was truncated; only unread package segments were subsequently retrieved in bounded portions.
- `task5-results/fix1-red.log:84`: seven failures / 114 passes. The unsupported-charset failures show unexpected persisted word/usage state; supported UTF-16 cases failed to succeed. Some mismatched-UTF-16 cases returned 500 in that red run, so those individual failures alone do not prove their persisted state.
- `task5-results/fix1-bom-red.log:64`: five failures / 13 passes, specifically valid BOM cases returning 400.
- `task5-results/fix1-focused-final.log:14` and the parsed `fix1-focused-final.trx` agree: 127 executed / 127 passed / zero failed or skipped. The TRX includes the 12 encoding cases and six real-signature cases. These are the supplied implementation-run results; this review did not rerun them.
- `task5-results/fix1-release-final.log:7-11` records a successful Release build with zero errors and one existing NU1510 warning. The focused log also retains the disclosed NU1903 advisories. No warning-free claim is made.
- Focused extra source lookup addressed only named doubts: actual MVC formatter registration, reader exception handling, validation-before-spending order, original-body proof/rewind handling, unchanged account authentication, and whether valid test-mode loops use the synthetic provider with real quota pressure. No new unresolved code doubt warranted a probe.
- No product edits, Git commands/mutations, new tests, subagents, provider calls, purchases, push or deployment were performed. Only this requested review artifact was written.
- This acceptance is scoped to the fix round. It does not independently re-clear the unchanged concurrency implementation or the whole hardening program. The previously disclosed malformed multipart limitation, seven unrelated audit failures, deferred B4 case and existing dependency warnings remain outside this review; the earlier full-suite result is not a fresh result for this commit.
