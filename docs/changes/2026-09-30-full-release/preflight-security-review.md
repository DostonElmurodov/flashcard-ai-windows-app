# Read-only preflight security review

Reviewed pending `.github/workflows/api-ci-cd.yml` and `ops/linux/read-only-preflight.sh` in `D:/07 Hobby/FlashcardAI/test-results/paid-access-hardening/backend`, based on `a6e2362c8e624338c564bfd88499cd1835c956c1`, plus `preflight-workflow-report.md`. No remote actions, source edits, or Git mutations were performed.

## Finding

**[P2] Preserve environment record boundaries before applying the output allowlist** — `ops/linux/read-only-preflight.sh:89-93`.

The Docker template prints each environment entry with a newline and the Bash loop treats every newline as a new environment entry. Environment values may themselves contain newlines. Consequently, a secret value such as `OpenAI__ApiKey=first-secret-line\nOpenAI__Model=second-secret-line` is reported as both a secret-presence flag and `config OpenAI__Model=second-secret-line`. The second line remains part of the secret's value, but the parser emits it as an allowlisted non-secret setting. This breaks the stated guarantee that secret values never leave the host and can also forge reported configuration values. This is a conditional parser defect; no actual deployed secret was inspected or shown to contain such content.

Local validation extracted and ran the actual helper functions and environment-filter loop using Git Bash with synthetic lines representing that single multiline environment entry. Output was:

```text
secret OpenAI__ApiKey=present
config OpenAI__Model=second-secret-line
```

Use an unambiguous record format, such as NUL-delimited Docker template output with `read -r -d '' entry`, or parse Docker JSON while retaining each complete string. Keep newline-containing values intact until their original key is classified. Add a local synthetic regression check for a secret containing a newline followed by an allowlisted key.

## Accepted aspects

- Manual mode defaults to `preflight`. Its job requires both `workflow_dispatch` and that exact mode. It has no build-job dependency.
- Build/test/image publication remains enabled for main pushes and explicit deploy mode. The deployment job requires manual dispatch plus explicit deploy mode and a successful build dependency.
- Preflight requests only `contents: read` for its GitHub token and uses the existing production environment and SSH secrets. It does not receive GHCR credentials.
- The deployment-path alphabet excludes quotes, shell expansions, control characters, and command separators; the absolute path is single-quoted in the remote command and subsequently quoted in the script. Spaces remain supported.
- The new remote script uses metadata/config inspection and availability checks. It contains no container execution/restart, image pull/push, database command, migration, health HTTP request, or application/provider request.
- Docker/Compose diagnostic stderr and raw inspection output are suppressed. Secret values are classified by exact keys, subject to the boundary defect above. State/revision/image output has format checks; mount paths are shell-escaped.
- Local Bash syntax verification passed.

## Limits

Acceptance is withheld until the environment-record defect is corrected. This was a source review and local synthetic filter probe, not an SSH or deployment verification. Existing deploy-command quoting was not changed by this patch and is outside the new preflight implementation. Production environment approval/branch rules, actual SSH host keys and account restrictions, installed Docker/Compose behavior, live config completeness, and deployed source identity remain unverified. SSH and sudo access may produce ordinary host audit records even though the script does not intentionally change deployment or database state. The reported sudo availability only establishes that `sudo -n -l` succeeds, not that any particular future privileged command is permitted.

## Correction assessment (pending final probe freeze)

The revised script at lines 89-95 now emits NUL-delimited Docker environment entries and consumes each full entry with `read -r -d ''`. This fixes the reported record-boundary defect. An independent local probe extracted the corrected actual helper functions and filter, supplied one multiline secret entry followed by a legitimate model entry, and produced only:

```text
secret OpenAI__ApiKey=present
config OpenAI__Model=legitimate-model
```

Neither synthetic secret line was emitted. Bash syntax verification passed again. Final acceptance remains pending inspection of the frozen focused regression probe and its report.

## Final correction acceptance

**Accepted after correction; no remaining actionable findings in the reviewed preflight change.** This conclusion supersedes the earlier withheld-acceptance statements, while preserving their defect history and the stated scope limits.

The final probe now requires the exact NUL-delimited Docker environment Go template, closing the mock-fidelity gap identified during review. The final workflow report describes the defect, correction, and validation accurately. Independently reran the full-script focused regression probe successfully (`preflight environment redaction probe passed`), both Bash syntax checks, and `git diff --check`. The local Git Bash invocation needed `/usr/bin:/bin` added to its shell PATH for standard utilities; initial attempts lacking those utilities did not exercise the probe. The corrected invocation succeeded. No remote actions were taken.
