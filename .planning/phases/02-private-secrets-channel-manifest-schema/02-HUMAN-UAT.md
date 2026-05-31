---
status: partial
phase: 02-private-secrets-channel-manifest-schema
source: [02-VERIFICATION.md]
started: 2026-05-31T22:10:00Z
updated: 2026-05-31T22:10:00Z
---

## Current Test

[awaiting human testing on the Windows dev box]

## Tests

### 1. End-to-end dual-fetch + joined-manifest log line
expected: On a Windows dev box, place a real fine-grained PAT in `installer/secrets/wifi-pat.txt` and the GitHub REST API contents URL in `installer/secrets/wifi-secrets-url.txt`. Run `pwsh installer/build.ps1 -Configuration Debug`, launch the EXE, and open `%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log`. The log contains `Joined manifest: N Wi-Fi profile(s) — [<SSID-A>] (PSKs: ***)` with N >= 1 and at least one SSID, and NO plaintext PSK anywhere.
result: [pending]

### 2. Offline cache fallback
expected: After one successful run (cache pair written), block network (firewall rule or disable adapter) and relaunch. Log line reads `Joined manifest: ... (PSKs: ***) (from cache)`, no exception thrown, and both `%ProgramData%\Jaminator\cache\manifest.json` and `secrets.json` exist on disk.
result: [pending]

### 3. Fail-fast guard (stub EXE)
expected: Build directly via `dotnet build src/Jaminator/Jaminator.csproj` with a stub `src/Jaminator/Generated/BuildSecrets.g.cs` holding `WifiPat = ""` (and again `"@@PAT@@"`, and an empty `SecretsUrl`). Launch the EXE. It exits immediately with exit code 1, writes `%TEMP%\Jaminator-fail-fast-*.log`, prints the "missing the Wi-Fi PAT or secrets URL" message to stderr, shows no UI window, and writes no `%ProgramData%\Jaminator\logs\` entry. Confirm across UI, `--login-mode`, `--run-all`, `--install`.
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps
