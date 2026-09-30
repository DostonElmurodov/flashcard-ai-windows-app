**Source acceptance: changes required.** I reviewed the supplied Task6B delta and current amendments without tools. The approved materialization cap is correctly placed under the library mutation lock, counts ID-less reservations, and preserves separate first-ten eligibility and inactive-access checks. Five issues remain.

Locations below use current source lines, including the five-line backend policy insertion.

1. **P2 — Repeated AI requests with an unknown explicit ID create ambiguous reservations.**  
   `src/Mavrylo.Services/Services/DeviceWordService.cs:290–294, 315–326`, `TryReserveAiSlotAsync`.  
   With room available, request AI twice using the same previously unknown `ClientWordId` and semantic identity. Lookup requires that client ID, but the newly created reservation has a null ID. The second request therefore creates another placeholder. A subsequent explicit save encounters two matching placeholders and fails with `word_identity_ambiguous`.  
   **Fix:** deliberately resolve repeated requests against an unambiguous reservation, or reject unsupported unknown explicit IDs before reservation/provider work. Never select another saved ID. Add a repeated-request-then-save regression.

2. **P2 — Incomplete AI ordering returns a payment denial instead of the required reconciliation conflict.**  
   `Filters/AiProtectionFilter.cs:175–177`; originating conditions in `DeviceWordService.cs:275–277, 351–352`.  
   A true-free library above ten with missing creation metadata returns `402 {"error":"word_order_reconciliation_required"}`. The specified contract is **409 with code `word_order_reconciliation_required`**. Clients receive a commercial denial instead of an actionable reconciliation result. Provider/quota work is correctly avoided.  
   **Fix:** distinguish reconciliation conflicts from paid-access denials in the reservation result and HTTP mapping; assert status, code, and zero spending through HTTP.

3. **P2 — Explicit blank delete IDs fall back to another saved identity.**  
   `src/Mavrylo.Services/Services/DeviceWordService.cs:230–236, 397–410`, `DeleteAsync` / `FindExistingAsync`.  
   Delete does not validate supplied IDs. Sending `ClientWordId = " "` with the semantic fields of an existing card takes the semantic fallback and deletes that card, despite supplying a different explicit ID. Upsert correctly rejects this input.  
   **Fix:** validate every non-null delete ID and distinguish null legacy identity from invalid explicit identity. Test empty/whitespace IDs preserve the saved row.

4. **P2 — First-ten exclusion becomes a persistent denial that also blocks expired-paid retained review.**  
   `Infrastructure/Repositories/WordRepository.swift:3662–3665`, interacting with `3488–3528`.  
   A locally eligible retained card can be outside the server’s first ten when the server retains additional older rows. Its queued save writes `access_denial`. `lockedWordIDs` then locks that card regardless of whether first-ten review limits apply—for example, after transition to expired-paid retained access. The marker also survives when deleting an older card moves it into the first ten.  
   **Fix:** keep temporary first-ten eligibility separate from persistent save-denial state, and recheck snapshot/access after reconciliation before applying any denial. Test subsequent eligibility recovery and expired-paid retained review. This finding concerns the local preflight branch, **not** HTTP 402 decoding.

5. **P2 — Local lock membership still merges byte-distinct saved IDs.**  
   `Infrastructure/Repositories/WordRepository.swift:3490, 3516, 3528, 3542`.  
   SQLite orders IDs by bytes, but `Set<String>` uses Swift canonical Unicode equality. With nine earlier cards followed by decomposed and composed forms of the same ID text, one can rank tenth and the other eleventh. Inserting the eleventh ID into the lock set also makes the tenth appear locked. The new byte-exact server eligibility and mutation keys do not fix this local boundary.  
   **Fix:** use byte-exact ID membership throughout lock computation and consumers, with a tenth/eleventh composed-versus-decomposed regression.

**Policy and preservation assessment:** The current materialization patch addresses the approved capacity rule without fabricating paid history or changing AccountSync capacity. Real upsert HTTP 402 throws before decoding and does not execute the Boolean-false denial-marker branch. The supplied changes do not establish a regression in accepted 10A/6A accounting; the historical-schema test adjustments preserve those controls.

**Runtime/external limits:**

- Supplied backend evidence reports **796/796**, zero skips, build zero errors, and no pending EF changes; I did not independently execute or hash-check it.
- Last executed iOS candidate `45c45ee` passed **685/686**, including **28/28 UI**. The `d66f73de` fixture correction and dirty HTTP402 regression remain unexecuted; run `36622781045` executed zero steps because of billing.
- Final current-candidate Mac full tests and Release remain gates.
- Production legacy identity inventory, deployed configuration, Apple/provider behavior, and production migration remain unverified. Source acceptance would not establish release readiness.