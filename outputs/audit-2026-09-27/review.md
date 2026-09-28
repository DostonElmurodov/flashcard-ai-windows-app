# Independent audit-report review

Reviewed `AUDIT-RU.md`, original backend authorization/payment sources, Windows local storage/API sources, and the updated local reproductions. No production changes or external purchases/requests were performed by this reviewer.

## Required correction

- In "Что уже защищено", the assertion that AI requires fresh App Attest is overbroad. Device AI requires device authentication and fresh App Attest under production settings. Account AI, including Windows requests, uses account authentication, entitlement checks, and shared quotas; it does not require App Attest. Account purchase claim separately requires device proof.

## Delivery check

- At review time, `ios/ios-audit.md` and its inventory still described the superseded June checkout. Replace the linked documents with the corrected current-version review before delivering. The root report already identifies the correct iOS baselines and does not repeat the obsolete absence-of-test-mode conclusion.

## Findings accepted

- B1/B2: ownership gaps are supported, with legitimate App Attest/purchase-proof prerequisites and existing claimed-owner protection stated explicitly. Updated same-original relink and first-claim reproductions support B2.
- B3/B4: accurately described as reservation/key-scope bypasses, with daily limits and Apple key-issuance limitations preserved.
- B5: accurately conditional on an existing test-environment record; account environment filtering distinguished from device behavior.
- B6: supported by the real selector characterization plus notification orchestration test, and correctly limited to a different selectable original transaction. Same-original historical refund semantics are not overclaimed.
- Trial/grace, local study, missing expiry, and certificate hardening are appropriately distinguished from demonstrated external payment bypasses.
- Successful characterization tests are correctly described as evidence of existing problems, not proof of remediation or deployment security.

No further backend factual correction identified. Current iOS details require the refreshed specialist report; the superseded checkout cannot substantiate current-client findings.
