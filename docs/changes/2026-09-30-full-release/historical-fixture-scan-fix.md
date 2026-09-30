# Historical fixture scan fix

## Scope

This change adds three exact Gitleaks full-history fingerprints to
`test-results/paid-access-hardening/backend/.gitleaksignore`. Each fingerprint is
limited to commit `4db300428adccf820584aeb0860afd73af3b673f`, one historical
fixture path, the `jwt` rule, and line 1. It does not disable the JWT rule, ignore
a directory, modify the workflow, alter application code, remove fixtures, or
rewrite history.

## CI evidence

The sanitized log `backend-build-36752906181.log` contains these three and only
these intended historical fixture fingerprints:

```text
4db300428adccf820584aeb0860afd73af3b673f:tests/Fixtures/Apple/renewalInfo:jwt:1
4db300428adccf820584aeb0860afd73af3b673f:tests/Fixtures/Apple/testNotification:jwt:1
4db300428adccf820584aeb0860afd73af3b673f:tests/Fixtures/Apple/transactionInfo:jwt:1
```

## Upstream public-fixture proof

The fixture blobs at the historical commit are byte-identical to the public mock
signed data in `apple/app-store-server-library-node` commit
`bb0c0f874494321ea2d005329c3dc2188e893d41`. The upstream repository's README
and MIT license identify the source as public test/mock data.

| Historical fixture | Git blob SHA-1 | Upstream mock file |
| --- | --- | --- |
| `tests/Fixtures/Apple/renewalInfo` | `d14a20dc86e0d1feb17775017279c76f239d28d3` | `tests/resources/mock_signed_data/renewalInfo` |
| `tests/Fixtures/Apple/testNotification` | `7bb78cf115ff2e0ca6ca61853e5c8e01304f528f` | `tests/resources/mock_signed_data/testNotification` |
| `tests/Fixtures/Apple/transactionInfo` | `3ddf0b022716df9617e3ce163c936245968c3bc2` | `tests/resources/mock_signed_data/transactionInfo` |

## Verification

- Verified the historical commit exists locally.
- Verified each historical file resolves to the listed blob SHA-1.
- Verified each exact fingerprint is present in the sanitized CI log.
- Gitleaks is not available on this machine, so the full-history scan must be
  confirmed by CI.
