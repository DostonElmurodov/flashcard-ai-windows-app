# Google accounts

The optional account API is independent of App Attest, subscriptions and local words. It does not enable legacy authentication or synchronization.

Configure a Google Cloud OAuth consent screen, an iOS OAuth client matching `com.mavrylo.owlai`, and a Web application OAuth client for backend ID-token audience. The iOS integration needs the iOS client ID, its reversed URL scheme, and the web client ID as its server client ID. No client secret is needed in the app or these endpoints.

Set server environment variables:

```text
Account__GoogleClientIds=<web-client-id>.apps.googleusercontent.com
Account__Audience=owl-ai-account
```

The audience must differ from `Jwt__Audience` and `device`. Multiple explicitly trusted Google audiences can be comma separated. Blank `Account__GoogleClientIds` leaves all unrelated routes available and returns 503 from Google sign-in. The existing JWT key and issuer remain server secrets/configuration. No OAuth client IDs were provisioned by this implementation.

The new migration preserves users and replaces global email uniqueness with uniqueness for non-Google users. Google identities are resolved exclusively by subject; identical emails never link accounts. If historical users contain duplicate non-null Google subjects, migration stops transactionally with an actionable message. Review ownership and resolve the duplicates explicitly before retrying; do not automatically merge or delete users. Downgrade likewise requires resolving duplicate emails introduced after enabling Google accounts, because the old schema requires global uniqueness.

Endpoints under `/owlai/account`: POST `google/session` (`id_token`), POST `session/refresh` and `session/logout` (`refresh_token`), GET `me`, DELETE the base route. GET/DELETE require the dedicated account bearer. Refresh tokens rotate, have a 30-day absolute family lifetime, and replay revokes the family, including previously issued access tokens. Access tokens last at most 15 minutes. Expired/revoked session rows and consumed hashes are deliberately retained for replay detection; future maintenance can purge families only after absolute expiration.

Deployment must apply the migration before serving this feature. The app currently applies EF migrations at startup. Before production rollout, validate real Google sign-in with the configured OAuth clients and test a returning account, sign-out, and deletion on a test account. Fixture-based tests do not verify live Google Cloud configuration.

Provider validation follows [Google's backend verification guidance](https://developers.google.com/identity/sign-in/ios/backend-auth). Public signing keys are cached; ID tokens are verified locally and never placed in provider request URLs.
