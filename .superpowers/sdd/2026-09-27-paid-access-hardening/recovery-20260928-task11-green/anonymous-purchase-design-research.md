# Anonymous iPhone purchase ownership and recovery — research for replacement Tasks 2–3

Date: 2026-09-27. Status: architecture research, not an approved implementation specification.

**Product constraint:** buying and using Owl AI on iPhone must not require an Owl AI account. An Owl AI account is optional and enables desktop access. The original account-mandatory Tasks 2–3 are superseded.

**Conclusion:** use a server-owned anonymous purchase owner, with separately authorized installation keys and an optional desktop-account link. A signed purchase or `appAccountToken` is not sufficient authentication for changing that owner. Apple provides useful anonymous correlation and local restore primitives, but the documented APIs examined do not give the server a challenge-bound proof that an arbitrary new App Attest key belongs to the Apple Account named in a copied StoreKit JWS. Therefore unrestricted Apple-only recovery and an unconditional promise that all copied-proof replay is prevented cannot both be claimed on this evidence.

For the required convenient iPhone experience, the recommended candidate is the hybrid model below: strong owner credentials for normal use and ownership-changing actions; automatic StoreKit recovery for iPhone access with explicitly narrower authority and residual risk. If the requirement instead demands cryptographic exclusion of copied-proof recovery, a recoverable credential must be provisioned, and the no-credential loss case needs a recovery process. Neither case requires an Owl account for ordinary iPhone purchases.

## Scope and source snapshot

Read-only inspection used:

- Backend: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, HEAD `473a39bee299f3df4c7fdcf6f45502f880a9bd8b`.
- iOS: `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/ios`, HEAD `9096c1e3b20fb920726bd471ed4a092783ee3cc7`.
- Audit: [AUDIT-RU.md](<D:/07 Hobby/FlashcardAI/outputs/audit-2026-09-27/AUDIT-RU.md>), findings B1/B2.
- Public Apple documentation and Apple WWDC material, accessed 2026-09-27. No purchases, production reads/writes, Apple-account interactions, product-code edits, or device experiments were performed.

This report does not claim to have reproduced Apple cryptography or proved live receipt fields on Owl AI devices. It separates documented contracts from the security inferences derived from them.

## What the current code establishes

| Path and lines in the inspected snapshot | Observed behavior and implication |
|---|---|
| Backend `src/Mavrylo.Services/Services/AppAttestRegistrationService.cs:68–86,90–116` | Attestation authenticates a fresh challenge and key; registration then accepts caller `DeviceUuid`. Re-registration can change the UUID. The UUID is not part of the verified bootstrap challenge preimage. More fundamentally, even signing a caller-selected UUID would not establish purchase ownership. |
| Backend `DeviceContextService.cs:27–37`; `EntitlementService.cs:83–90` | Authenticated key → stored caller-supplied UUID → subscription by UUID. This is the B1 authorization join. |
| Backend `IapService.cs:34–63,110–118`; `EntitlementService.cs:75–76` | A verified JWS and Apple refresh lead to replacing the purchase device association with the requester UUID. Refresh and upsert mix Apple subscription state with authorization changes. |
| Backend `AppStoreServerClient.cs:312–342` | Apple `appAccountToken` is initially projected into `SubscriptionEntity.DeviceUuid`; it is later overwritten by the current requester. There is no stored separate original Apple token, anonymous owner, app-transaction identity, or owner-key proof. |
| Backend `AccountEntitlementService.cs:45–75` | First desktop-account claim verifies active Apple evidence, changes UUID, then atomically claims. It does not prove the claimant controls the purchase's prior owner. |
| Backend `SubscriptionOwnershipService.cs:8–20`; `SubscriptionEntity.cs:12–21` | Conditional claim already serializes ownership, and `ClaimedAt` survives deletion. Preserve these protections. Null account plus a claim timestamp is a tombstone, not an unclaimed purchase. |
| Backend `EntitlementService.cs:105–106`; `DeviceContextService.cs:33–36,49–60` | Account linking currently turns device access into `account_required` unless an appropriate account authorization is present. That behavior conflicts with the revised requirement if linking desktop access must not require continued iPhone login. |
| iOS `Infrastructure/Purchases/StoreKitService.swift:94–120,133–152,178–193` | Purchase already works without an Owl profile. It creates a token locally; restore uses `AppStore.sync()` plus verified current entitlements. Account claim first calls the same backend verify/relink path. |
| iOS `Infrastructure/Purchases/AppAccountTokenStore.swift:8–14,23–31` | UUID is locally generated and saved as `AfterFirstUnlockThisDeviceOnly`. It is not a portable recovery credential; it is also visible inside the signed transaction. |
| iOS `Infrastructure/Security/AppAttest/AIIntegrityCoordinator.swift:112–130,198–212,271–281` | Normal assertions bind challenge, path, challenge ID and request-body hash. Registration binds only the bootstrap nonce; key recovery creates a new App Attest registration. Reusing the purchase UUID cannot authenticate that replacement key. |
| iOS `Infrastructure/Networking/APIClient.swift:222–232` | Device purchase verification sends only transaction JWS in its request body. There is no separate restore/ownership-proof contract. |
| iOS `FlashCardAI.xcodeproj/project.pbxproj:1077,1134,1212,1236` | Current deployment target is iOS 17.0. |

The audit's local B1/B2 tests establish a logical authorization failure under their modeled valid-attestation/valid-Apple-proof assumptions. They do not establish that attackers can forge Apple signatures or obtain victims' secrets. The design must specify what copied evidence the attacker may possess.

## What Apple proves, and what it does not

### 1. StoreKit transaction JWS

A valid signature proves Apple issued the purchase information for the specified app, product, environment and transaction. Querying Apple updates status and revocation information. It does not identify the HTTP requester who forwards the bytes. The server must validate the signature/certificate chain and relevant fields, and must refresh the **same** original transaction rather than accidentally select a different subscription.

Apple's [StoreKit API comparison](https://developer.apple.com/documentation/storekit/choosing-a-storekit-api-for-in-app-purchases) documents signed transaction support from iOS 15. Apple's [Get App Transaction Info](https://developer.apple.com/documentation/appstoreserverapi/get-app-transaction-info) accepts a transaction identifier; that developer-authenticated lookup provides app transaction information, not customer authentication.

### 2. `appAccountToken`

This is a developer-generated UUID used to associate a purchase with a customer record. Apple's recommended flow creates it on the server and supplies it to StoreKit. Apple returns it in signed transaction information. It can therefore be a strong, integrity-protected **reference to an existing server mapping**, when the current requester is independently authenticated to that mapping. It is not a secret, password, or proof that a new requester owns the mapped record.

Apple also provides a server endpoint to change this token. Changing it is an administrative action by our server, not proof that the customer authorized a transfer. Family-shared transactions do not contain `appAccountToken`. See [WWDC25: Dive into App Store server APIs](https://developer.apple.com/videos/play/wwdc2025/249/).

Never recover ownership by saving a copied token into Keychain, by setting `device_uuid` equal to it, or by calling Set App Account Token to match the new requester.

### 3. `appTransactionID` / server `appTransactionId`

Apple defines one stable value for the Apple Account and app. It survives redownloads, device changes, refunds, repurchases and storefront changes; family members receive distinct values. It exists without an in-app purchase. Matching app and purchase transaction IDs is useful for correlation, preventing accidental mixing of two different customers' proofs, migration and shared quotas. [Apple: appTransactionID](https://developer.apple.com/documentation/storekit/apptransaction/apptransactionid).

This identifier is present in signed data and can be copied. A stable identifier is not an authentication credential. There must not be a new B1-shaped join where a fresh key chooses an `appTransactionID` and immediately inherits that owner's rights.

### 4. Fresh `AppTransaction`

`AppTransaction.shared` supplies StoreKit's verified app transaction, including at first launch; StoreKit keeps it current. `AppTransaction.refresh()` queries Apple, can show App Store authentication, and should only be called in response to explicit user action. Its documented signature accepts **no server challenge/nonce parameter**. [Apple: shared](https://developer.apple.com/documentation/storekit/apptransaction/shared), [Apple: refresh](https://developer.apple.com/documentation/storekit/apptransaction/refresh()).

Requiring a recent `signedDate` limits the age of accepted copied evidence. It does not make that evidence one-time or prove the caller invoked refresh after our challenge. A copied fresh object can still race or be relayed. There is no documented guarantee in the examined APIs that every refresh produces a distinct value or a server-selected nonce. Do not invent a freshness window and then present it as a cryptographic ownership guarantee.

When forwarding an app transaction to the backend, use its JWS representation and verify it server-side, not unsigned JSON. [Apple: jsonRepresentation](https://developer.apple.com/documentation/storekit/apptransaction/jsonrepresentation).

### 5. Device verification is useful, with a boundary

StoreKit verifies that returned verified transaction information is valid for the local device. Apple's manual check compares the signed `deviceVerification` value with SHA-384 of lowercased nonce UUID followed by lowercased `AppStore.deviceVerificationID`. [Apple: transaction deviceVerification](https://developer.apple.com/documentation/storekit/transaction/deviceverification), [Apple: deviceVerificationID](https://developer.apple.com/documentation/storekit/appstore/deviceverificationid).

**Security inference:** the server can recompute that hash if given the device ID, but cannot independently learn from those fields that the submitted ID belongs to the new App Attest key. The documented App Attest certificate/assertion does not contain an Apple-signed binding to this StoreKit ID. An assertion over a submitted ID proves that key signed the submitted value, not that Apple measured the value for that key.

Thus a stolen purchase JWS alone may be insufficient for an implementation that requires additional matching app/device evidence, and the honest local StoreKit flow rejects another device's evidence. This is a real improvement. It must not be stretched into a claim about an attacker who has copied the complete proof material or can relay an approved app's assertions. The server-returned AppTransaction has no device-verification fields; it cannot substitute for local device evidence. [Apple: decoded server AppTransaction payload](https://developer.apple.com/documentation/appstoreserverapi/jwsapptransactiondecodedpayload).

### 6. App Attest

App Attest verifies a legitimate app instance/key, then lets the server verify signatures over client data using a stored public key. The server supplies a one-time challenge, verifies counters, and associates accepted keys with its users. The documented fields include app identity, counter, environment and key identity, not the customer's Apple Account. [Apple: validating apps](https://developer.apple.com/documentation/devicecheck/validating-apps-that-connect-to-your-server).

Keys are per device and do not sync. They are invalidated by reinstall or device restore; Apple explicitly cautions against rejecting all key rotations. App Attest is a valuable integrity/risk control, not a replacement for recoverable owner authentication. [Apple WWDC26: Secure your apps with App Attest](https://developer.apple.com/videos/play/wwdc2026/201/).

### Concrete replay reasoning

Suppose legitimate user A has purchase `T`, app transaction `AT`, anonymous owner `O`, and old installation key `KA`. A new phone has `KB` and no prior owner credential. A proposed recovery endpoint accepts a signed `T`, recent signed `AT`, a claimed device-verification ID, and a valid new-key assertion over the request.

If attacker B copies the complete accepted evidence and can produce the same class of valid assertion with B's own genuine/abused installation key, the server has no independent old-owner binding to distinguish the two requests. Checking matching Apple IDs, signatures, recency and request integrity remains necessary, but does not supply the missing linkage. This is a protocol inference, not a claim that such a full capture or relay has been demonstrated against Owl AI.

An old owner key, a previously registered recovery key, a secret recovery capability, or an already linked account changes the situation: the server has a prior authenticator to challenge. A copied receipt does not include that private credential.

## Candidate models

| Model | iPhone purchase/login | Fresh-device recovery | Strongest honest security claim | Tradeoff |
|---|---|---|---|---|
| A. Strict anonymous owner with recoverable credential | No Owl account. Server creates owner and purchase token; app proves installation key. | Prior device authorizes new key, synchronized recovery secret/key proves possession, recovery code, or already linked Owl account. | Copied Apple evidence alone cannot acquire owner rights or desktop link. | If every recovery credential is lost, Apple-only automatic recovery cannot meet that same strong claim. Requires assisted recovery with additional evidence. |
| B. Apple restore as authority for anonymous access | No Owl account. Apple identity groups subscriptions/installations. | Automatic current entitlements plus verified app/device evidence, Apple status and App Attest; explicit Restore fallback. | Fixes UUID inheritance, blocks mismatched proofs and raises replay cost. | Accepts residual copied-proof/relay risk. Treating this flow as owner authentication also leaves a first-desktop-claim risk. Cannot honestly mark all B2 cases closed. |
| C. Hybrid owner authentication plus limited Apple restore — recommended candidate | No Owl account. Strong anonymous owner established before new purchases. | Credential path restores full owner authority. Otherwise Apple-only recovery grants iPhone access with explicit narrower authority. | Strong protection of immutable ownership and account linking; better iPhone replay resistance without denying normal restores. | Apple-only access recovery still has residual risk. First desktop link after total credential loss needs owner recovery or assistance. |

Model A can use an ordinary cryptographic recovery key/secret kept separately from the App Attest key, optionally synchronized by iCloud Keychain. App Attest keys themselves cannot be synchronized. Apple's Keychain documentation supports synchronized cryptographic keys from iOS 14 and forbids `ThisDeviceOnly` accessibility for synchronized items. [Apple: kSecAttrSynchronizable](https://developer.apple.com/documentation/security/ksecattrsynchronizable). A secret/seed is also possible, but must never be `appAccountToken`, a device UUID, or transaction ID. Optional passkeys provide another recoverable challenge-response credential from iOS 16, with a visible system ceremony. [Apple: passkeys Q&A](https://developer.apple.com/news/?id=21mnmxow).

Sync is not guaranteed: users can disable iCloud Keychain, use another iCloud account, lose access, or install before sync completes. App Store purchasing identity and the credential's storage context must not be assumed identical. A delayed key must not silently create a second permanent owner. `ThisDeviceOnly` data explicitly does not migrate to a different device during backup restoration. [Apple: keychain accessibility](https://developer.apple.com/documentation/security/restricting-keychain-item-accessibility).

## Recommended candidate behavior and data boundaries

### Anonymous purchase owner

Create a random server `AnonymousOwnerId` before a new purchase. It has no email, password, or required profile UI. Keep separate records for:

- Owner and its registered recovery authenticators.
- Installations/App Attest public keys, each attached by an authorized enrollment to an owner, or initially unowned/free.
- Apple purchase identity `(environment, originalTransactionId)` and immutable anonymous owner.
- Server-generated `appAccountToken` → owner mapping; preserve observed Apple token separately.
- Apple `appTransactionId` correlation, recorded from verified evidence; never accept raw client ID as authentication.
- Optional desktop-account link, claim timestamp, deletion tombstone and history.
- If hybrid chosen, limited restored iPhone access grants, separate from owner-authenticated installations.

One purchase can authorize multiple legitimate phones. Restore should add an authorized installation/grant, not overwrite one `DeviceUuid` and displace the previous phone. All access grants for a purchase must share its server usage/budget accounting; a fresh key must not multiply paid allowances.

Purchase status updates, notifications and Apple refreshes may change expiry/revocation/grace and verified Apple metadata. They must not change owners, enroll devices, clear a tombstone, reset sticky paid history, or claim an account.

### New purchase

1. Register the legitimate installation; server gives it a new anonymous principal, or it proves recovery of an existing one.
2. Server issues an owner-mapped purchase token and records the pre-purchase relationship. If enrolling a recovery key, bind that key and the enrollment intent into the verified challenge/body and require possession of its private key.
3. iOS sends that token to StoreKit. On purchase result/update, the server verifies JWS, correct app/product/environment and current status for the same purchase.
4. The signed token must map to the already authenticated owner. A different requester cannot replace that mapping with a copied JWS. Use atomic conditional insert/link for the original transaction and handle webhook-before-client ordering idempotently.
5. Record owner binding once. Missing or unknown tokens from legitimate historical/external purchases go to an explicit legacy/restore route; they do not become freely claimable purchases.

An optional purchase intent can prevent accidental cross-product/cross-session mixing; it does not turn a public token into a secret or make an old transaction belong to a new owner.

### Reinstall or new iPhone

First enumerate verified `Transaction.currentEntitlements` automatically and prepare the new legitimate installation. Apple states these entitlements are normally available immediately after reinstall/new-device download; `AppStore.sync()` is a user-triggered fallback. [Apple: sync](https://developer.apple.com/documentation/storekit/appstore/sync()).

If recovery credentials are available, challenge them and attach the new key to the existing anonymous owner. Old-device approval must sign the new key and recovery intent, not only a reusable ID. Do not change the Apple purchase owner. Do not require an Owl login.

If no credential survives, the hybrid candidate verifies the Apple evidence and issues a **limited iPhone entitlement grant**, with bounded refresh policy and shared quotas. It does not allow desktop claiming, credential rotation for the owner, changing the owner/account link, or reading the owner's private account data. Existing phones remain valid. A successful full-proof replay could still consume the purchase's shared allowance; limits bound cost but do not eliminate misuse or its effect on the rightful buyer. Never delete the purchase or advise repurchase as the only remedy.

Strict model A would instead require an additional recovery method before issuing access. That is a materially less convenient behavior, so it must not be smuggled into an implementation as a security fix to the user's requirement for ordinary Restore.

### Optional desktop account link

Display an explicit action such as “Use this subscription on Windows.” Show the destination Owl account and require user confirmation in the existing product flow. Backend authorization requires both the authenticated account and a strong owner-authenticated installation/recovery proof, bound to that destination and purchase by a one-time challenge. An account session plus a copied Apple JWS is insufficient.

Retain atomic one-account arbitration and tombstones. A phone with only a limited restore grant must first recover owner authority if the subscription has no desktop link. This is the unavoidable special recovery case under the hybrid security boundary, not a requirement to log in merely to use iPhone.

After linking, signing out of Owl on iPhone must not remove that phone's valid Apple/anonymous entitlement. Signing into another Owl account must not transfer the purchase or expose the prior account's data. Account deletion must not destroy the anonymous Apple purchase owner or legitimate mobile access, and must not erase the historical desktop claim into an “unclaimed” state. Re-linking a deleted account's purchase requires a separately defined recovery policy; a tombstone is preserved until that policy is implemented.

### Suggested user-visible behavior

- Purchase screen: “Subscribe” and “Restore Purchases”; no Owl sign-up gate.
- Normal launch/new phone: restore Apple entitlement automatically where available.
- Restore button: Apple may request App Store authentication; no silent calls that prompt at launch.
- Optional profile action: “Use on Windows” / “Link subscription to your Owl AI account.”
- Missing strong credential during first desktop link: “Your subscription works on this iPhone. To enable Windows access, recover the subscription's access key using your other device or recovery method.” Provide a support path if those methods are unavailable.
- Apple/network failure: distinguish “Could not check purchases” from “No active subscription.” Do not suggest a new charge to repair an outage or a binding conflict.

## Compatibility and verification constraints

| Component | Documented minimum / constraint | Effect on this project |
|---|---|---|
| App Attest | iOS 14+; runtime support check still needed. [Apple security overview](https://developer.apple.com/security/) | Already used; do not assume simulator tests prove real attestation or recovery. |
| StoreKit 2 Transaction/current entitlements/appAccountToken | iOS 15+. [Apple API comparison](https://developer.apple.com/documentation/storekit/choosing-a-storekit-api-for-in-app-purchases) | Already used on iOS 17 target. |
| AppTransaction base type/shared/refresh | Base type metadata states iOS 16.0. [Apple public documentation metadata](https://developer.apple.com/tutorials/data/documentation/storekit/apptransaction.json) | Current iOS 17 target supports the base API. |
| `appTransactionID` fields | Introduced with iOS 18.4 SDK, back-deployed; Apple WWDC25 explicitly says iOS 15. [Apple WWDC25 StoreKit](https://developer.apple.com/videos/play/wwdc2025/241/), [property declaration](https://developer.apple.com/documentation/storekit/apptransaction/apptransactionid) | Do not confuse field back-deployment with AppTransaction base availability. Use a recent SDK; validate Swift compile and actual field presence on iOS 17/18.4/current OS. No deployment-target bump is inherently required for this design. |
| Synchronized cryptographic Keychain keys | iOS 14+; not `ThisDeviceOnly`. [Apple Keychain](https://developer.apple.com/documentation/security/ksecattrsynchronizable) | Requires a separate recovery item and intentional migration policy. Existing purchase token cannot simply be relabeled a secret. |
| Optional passkeys | iOS 16+. [Apple passkeys Q&A](https://developer.apple.com/news/?id=21mnmxow) | Compatible with current target; visible user interaction and recovery semantics must be designed. |
| Server app transaction lookup | Get App Transaction Info added in API 1.17, 2025-10-16. [Apple API changelog](https://developer.apple.com/documentation/appstoreserverapi/app-store-server-api-changelog) | Can correlate historical transaction IDs with app transaction identity; does not authenticate the client and supplies no local device-verification proof. |

Before relying on automatic app-transaction correlation, validate Family Sharing and mixed Apple-account behavior. App/purchase accounts and shared ownership semantics cannot be guessed from a single happy-path device. The current backend's unconditional requirement for `appAccountToken` also means family-shared/external-purchase compatibility needs an explicit decision, not an accidental security denial.

## Work that can proceed independently now

1. Add/retain failing B1/B2 regressions and explicit passing controls for legitimate anonymous use, existing purchase owners and account tombstones. No account-mandatory requirement in those tests.
2. Define the server-owned installation/owner distinction. A new registration must never inherit subscriptions solely from supplied UUID/token/app transaction ID. Same-key retries must not change its established owner or reset protected state. This can be implemented locally behind the replacement contract, but deployment needs the migration/recovery path below.
3. Split status refresh/upsert from authorization mutations. Keep immutable owner, desktop claim/tombstones and original Apple metadata across refresh, verify and notifications. A restore candidate must not write its requester UUID into the subscription before authorization.
4. Fix independent canonical purchase/status/environment issues and preserve existing account claim concurrency protection. Ensure failure paths leave prior owner/access intact.
5. Add a new-purchase protocol with server-issued owner-mapped appAccountToken and authenticated enrollment. This blocks copied new-purchase JWS from creating an arbitrary first owner; it does not retrospectively prove historical ownership.

Do not deploy an isolated “always generate a fresh UUID” change and call the task complete: it can close B1 while stranding legitimate existing buyers on key rotation. Do not deploy “reject every different key/device” as Restore behavior.

## Migration and remaining decisions

Existing subscriptions must remain intact. Keep original transaction, paid history, environment, account claim, tombstones, Apple event ordering and cached valid expiry. Derive authoritative anonymous bindings only from trustworthy available history; do not automatically promote every historical key sharing a client-supplied UUID. A unique matching UUID row is less ambiguous, but is not retroactive cryptographic proof that the enrollment was rightful.

Where old data lacks evidence, mark the association as legacy/ambiguous and use the explicitly chosen recovery path. Do not resolve ambiguity by deleting subscriptions, resetting `ClaimedAt`, changing `appAccountToken`, granting first claimant ownership, or forcing repurchase. Migration cannot reconstruct a missing historical owner credential.

The remaining product/security choice is narrow: **accept residual risk for Apple-only iPhone access recovery (recommended hybrid), or require a recoverable credential for all new-key access, including the all-credentials-lost case.** The original user instruction already settles that an Owl account cannot be required for iPhone purchase/use; it does not by itself select one of these anonymous recovery threat boundaries. A complete replacement implementation plan should state this choice and its acceptance tests explicitly.

No documentation examined establishes a server-verifiable Apple Account ↔ App Attest key binding or server-nonce-bound AppTransaction recovery. Asking Apple for such a supported binding is reasonable if that guarantee is a release requirement; it is not a reason to advertise the current combination as equivalent. Availability under Owl AI's actual distribution, missing legacy IDs, Family Sharing, and loss of all synchronized credentials remain device-test/recovery-policy questions.

## Minimum acceptance matrix for replacement Tasks 2–3

- New iPhone purchase succeeds without Owl account; correct anonymous owner receives access.
- Valid fresh key plus victim UUID/token/raw app transaction ID inherits no access.
- Valid own-device proof plus a victim transaction with mismatched app transaction/device data is rejected without mutating victim state.
- Copied full Apple proof cannot change immutable anonymous ownership or first desktop claim. If hybrid Apple-only access is accepted, test and document its narrower residual-risk boundary rather than asserting impossible rejection for every copied envelope.
- New purchase token belonging to owner A cannot be attached by authenticated owner B, including concurrent requests and webhook-first delivery.
- Same owner can add a second iPhone with recovery proof; first phone stays entitled; quota remains shared.
- Reinstall invalidates App Attest key but owner credential recovery re-enrolls without purchase/account login.
- Fresh phone without synced credential follows the selected Apple-only/assisted flow, without deletion or repurchase.
- Already desktop-linked purchase works on iPhone after Owl sign-out; another Owl account cannot take over the link.
- Account deletion preserves purchase/tombstone and rightful mobile access; a new account cannot re-claim solely from JWS.
- Apple outage, missing app transaction, cancellation, stale proof, concurrent restore/claim/delete, expired/revoked subscription, and environment mismatch have deterministic non-destructive outcomes.
- Real StoreKit/App Attest sandbox testing on at least two devices plus reinstall/credential-loss cases is required before promising production recovery behavior. Local fake-JWS tests prove server state transitions, not platform authentication guarantees.
