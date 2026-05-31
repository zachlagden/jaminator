# Wi-Fi secrets — operator runbook

## What to drop here

- `wifi-pat.txt` — single line, fine-grained PAT (`github_pat_…`), no surrounding whitespace. Gitignored.
- `wifi-secrets-url.txt` — single line, full GitHub REST API contents URL: `https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}`. Example: `https://api.github.com/repos/jamcoding-internal/jaminator-secrets/contents/secrets.json?ref=main`. Gitignored.

## PAT permissions

Generate a **fine-grained** PAT (not classic) scoped to the private secrets repo ONLY. Required permission: `Contents: Read`. `Metadata: Read` is auto-included by GitHub when any other repo permission is granted (no action needed). NO org-level permissions. NO other repo selected.

## Rotation cadence

Each term, aligned with PSK rotation. When the PSK changes, generate a new PAT, replace `wifi-pat.txt`, rebuild the MSI via `installer/build.ps1`, ship a new release. HARDEN-07 (CI automation) is queued for M3.

## Env-var fallback

If `installer/secrets/wifi-pat.txt` is absent, `installer/build.ps1` reads `$env:JAMINATOR_WIFI_PAT`. Same for `$env:JAMINATOR_WIFI_SECRETS_URL`. This enables future CI without changing the script. If both file and env var are absent, the build fails with a clear actionable message naming both sources.

## What is gitignored here

Everything in this directory is gitignored except this `README.md` and an empty `.keep` marker. The gitignore rule is `installer/secrets/*` with `!installer/secrets/.keep` and `!installer/secrets/README.md` negations. Do not weaken these rules.

## Threat-model honesty

The PAT is baked into the MSI as a string constant. An attacker with the MSI can recover it via static analysis. This is **accepted by design** — see `.planning/PROJECT.md` Key Decisions row "WIFI-03" for the full rationale. The threat model is operational opacity (keep PSKs off the public, search-indexable internet), not cryptographic protection. Termly rotation bounds the blast radius.

## Cross-references

- `docs/manifest-schema.md` — full schema and the "Private secrets channel" section
- `.planning/PROJECT.md` — WIFI-03 Key Decisions row
- `installer/build.ps1` — the script that consumes these files
