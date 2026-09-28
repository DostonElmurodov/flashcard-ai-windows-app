# Shared test mode

Set one server environment variable to enable test access for all clients of that API:

```text
TestMode__Enabled=true
```

The default is `false`. On the current deployment, keep a single entry in `/etc/owl-ai/api.env`, then recreate the API service from `/opt/apps/mavrylo-api` with `docker compose up -d --no-deps api`. An environment change needs a container recreation; restarting the existing container does not reload the env file.

`GET /owlai/config/feature-flags` is anonymous, returns `{"test_mode":true}` (or `false`), and sets `Cache-Control: no-store`. Updated iOS and Windows clients start with test access disabled, read it at launch and while active, and use the current response independently of the real subscription. No persisted `true` value is restored on startup. This supports launching from the iPhone home screen without Xcode. A failed, missing, or malformed response disables test access until a successful response enables it again.

When enabled, word-count and expired-plan restrictions, automatic purchase paywalls, device/account AI subscription checks, daily/minute AI quotas, and the API's AI/account-usage/assertion-challenge request throttles are bypassed. Both guest iPhone word operations and signed-in desktop/iPhone AI requests use the setting. No subscription rows, purchase ownership, paid status, or usage counters are fabricated or reset.

Authentication, App Attest request proofs, account/data ownership, input validation, request-size bounds, sync batch/conflict handling, password-attempt throttles, and the App Attest bootstrap throttle continue to apply. AI provider failures and provider-side quotas can still stop a request. The flag grants test access to all users of this server, not only one phone or account.

To restore normal behavior, set `TestMode__Enabled=false` and recreate the container. The server immediately enforces normal access after recreation; connected updated clients apply `false` on their next successful refresh. While active, iOS refreshes every 30 seconds with a 5-second request timeout and Windows every 60 seconds with a 10-second request timeout; startup and activation also refresh. UI changes therefore have a refresh delay, while the server checks its current configuration on every protected request. Clients also disable test access if their refresh fails; old cached values, local flags, and client headers cannot override server authorization. Old clients need an update to use the shared control. The former Xcode-only `OWL_AI_DISABLE_WORD_LIMITS` launch flag is no longer supported.

`TestMode__Enabled=false` (also missing or invalid configuration) takes priority over every legacy commercial bypass. Development App Attest keys and simulator keys receive normal subscription and word-limit checks. The deprecated `AiProtection:Enabled` rollout setting is retained for configuration compatibility but is ignored by the AI filter: setting it to `false` no longer makes AI public or disables subscription/quota checks. Production startup still requires it to be `true`. Local simulator registration and the existing `RequireAssertion` development configuration remain available without granting commercial test access.

With test mode off, real trial/premium/grace access remains valid; free word creation observes the normal ten-word limit, and expired-trial/expired-paid/revoked/unknown states cannot create new words. Existing-word updates remain available. No device environment or stale premium token hint grants an entitlement.

Verify the public flag and `/health`, then open each updated client. A release build should respect the same server flag. Test paid/expired/free behavior with the flag set to `false`; real StoreKit purchase verification and ownership rules remain unchanged in both modes.
