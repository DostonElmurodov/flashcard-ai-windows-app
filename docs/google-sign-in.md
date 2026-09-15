# Google sign-in for Windows

The desktop app opens the system browser, receives a response on a short-lived `127.0.0.1` listener, and exchanges the authorization code with PKCE. State and nonce bind the response to this attempt. Tokens never enter the renderer. The existing backend endpoint `POST /owlai/account/google/session` verifies the Google ID token and creates or resumes the account by Google subject. The Owl AI session is stored using Windows credential encryption.

## Configure a release

1. In the same Google Cloud project used for Owl AI, create an OAuth client with application type **Desktop app**. An iOS client ID cannot be reused for the Windows loopback flow.
2. The public Windows client ID is stored in `build/google-oauth.json`. For local credentials, save Google's downloaded **Desktop app** JSON as `build/google-oauth.local.json`; the build accepts its `installed.client_id` and `installed.client_secret` fields. The original flat `clientId`/`clientSecret` format is also accepted. Never use a Web application client secret here. Desktop clients are public clients; distributed applications cannot keep their client configuration confidential.
3. Add the desktop client ID to the backend's comma-separated `Account:GoogleClientIds` allowlist (`Account__GoogleClientIds` environment variable), retaining existing iOS audiences. Restart/redeploy the backend configuration. No new backend endpoint is needed.
4. Build with `npm run installer` or `npm run package`. Build-time environment variables `OWL_GOOGLE_CLIENT_ID` and `OWL_GOOGLE_CLIENT_SECRET` can replace the local JSON file. The local configuration is ignored by Git and is not included as a file in the application package.
5. If the consent screen is in testing mode, allow the test Google account. Verify sign-in, cancellation, restart, logout, and the same Google account on both devices. Apple purchase ownership still needs to be linked on iPhone.

Both `build/google-oauth.local.json` and downloaded `build/client_secret_*.json` files are ignored by Git. Never commit them. Environment variables override local configuration, which overrides the public default client ID. Automated tests use only local fixtures. Real Google sign-in must be tested after configuration.

The Windows audience that must be appended to the production allowlist is `1006264545231-d0chn9chu91nskfr5lq0sl3sp3bmiq9p.apps.googleusercontent.com`. Keep every existing trusted audience, including the iOS ID. Updating this desktop repository does not change the production server environment.

Google and email identities are **not** automatically merged because they share an email address. Use the same sign-in method on iPhone and Windows to access the same shared subscription account.

## Verification

```powershell
npm test
node scripts/google-session-smoke.mjs
node scripts/settings-flow-smoke.mjs
```

References: [Google installed-app OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Google loopback support for desktop clients](https://developers.google.com/identity/protocols/oauth2/resources/loopback-migration).
