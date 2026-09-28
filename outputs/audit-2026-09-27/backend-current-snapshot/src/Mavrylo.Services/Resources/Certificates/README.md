# Embedded Apple Certificates

This folder contains public Apple root certificates used as trust anchors by the backend.

These files are **not secrets**. They are intentionally committed and embedded into the API
assembly through `Mavrylo.csproj`.

Do not place private keys in this folder. Apple `.p8` App Store Server API keys must stay outside
the repository, normally under `/etc/owl-ai/secrets/SubscriptionKey.p8` on the server.

## Files

| File | Used by | Purpose |
| --- | --- | --- |
| `Apple_App_Attestation_Root_CA.pem` | `AppAttestVerifier` | verifies Apple App Attest attestation certificate chains |
| `AppleRootCA-G3.pem` | `AppleJws` | verifies StoreKit JWS and App Store Server Notifications v2 chains |

## App Attest Root

`Apple_App_Attestation_Root_CA.pem` is the public Apple App Attestation Root CA.

The backend uses it when a device registers through:

```http
POST /owlai/app-attest/register
```

During registration, the backend validates the attestation certificate chain, checks the Apple
nonce extension, verifies the `rpIdHash`, and extracts the App Attest public key.

## Apple Root CA - G3

`AppleRootCA-G3.pem` is the public Apple Root CA - G3.

The backend uses it for:

- StoreKit signed transaction JWS values sent to `/owlai/iap/verify`
- signed renewal info returned by App Store Server API
- App Store Server Notifications v2 sent to `/owlai/app-store-notifications/notifications`

In production, `AppleJws` walks the `x5c` certificate chain to this root and verifies the ES256
JWS signature. In DEBUG + Development, local StoreKit testing can decode StoreKitTest payloads
without this production chain.

## Maintenance

Before a production launch:

1. Re-download both public certificates from Apple's official certificate authority page.
2. Compare the fingerprints with the files in this folder.
3. Rebuild and run tests.
4. Deploy the updated build if Apple has rotated a root certificate.

Example fingerprint command:

```bash
openssl x509 -in AppleRootCA-G3.pem -noout -fingerprint -sha256
openssl x509 -in Apple_App_Attestation_Root_CA.pem -noout -fingerprint -sha256
```

Operational note: if Apple rotates one of these roots and the backend is not updated, affected
Apple verification paths will fail closed.
