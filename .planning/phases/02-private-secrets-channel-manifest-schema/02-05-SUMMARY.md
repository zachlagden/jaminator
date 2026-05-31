---
phase: 02-private-secrets-channel-manifest-schema
plan: 05
subsystem: ui
tags: [csharp, winforms, program-main, fail-fast, observability, psk-masking, net48]

# Dependency graph
requires:
  - phase: 02-private-secrets-channel-manifest-schema
    provides: Manifest.Wifi + WifiProfileEntry.Ssid/Psk DTOs (plan 02-01), ManifestFetcher dual-fetch + SSID→PSK join populating Manifest.Wifi.Profiles[i].Psk (plan 02-03), BuildSecrets.WifiPat / BuildSecrets.SecretsUrl build-time symbols (plan 02-02)
provides:
  - Program.Main fail-fast PAT guard that fires BEFORE ParseMode in every RunMode (UI, login-mode, run-all, install, uninstall, register-task, unregister-task) when BuildSecrets.WifiPat is empty or holds the literal placeholder @@PAT@@
  - "%TEMP%\\Jaminator-fail-fast-{timestamp}.log breadcrumb so a silent login-mode invocation leaves a diagnostic when the PAT is missing"
  - MainForm.OnLoad joined-manifest Info log line — "Joined manifest: N Wi-Fi profile(s) — [SSID-A, SSID-B] (PSKs: ***)" with masked PSKs and a (from cache) suffix when fromCache=true
affects:
  - Phase 3 (Wi-Fi runner — the observable Joined-manifest log line is the first end-to-end confirmation that the dual-fetch join populated PSKs; the runner builds on the same Manifest.Wifi.Profiles surface)
  - Operators (the fail-fast guard + README pointer is the front-line diagnostic for a mis-built EXE)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Launch-time fail-fast guard as the first statement in Program.Main — runs before any mode parse, UI init, Logger instantiation, or network call; complements the build-time PAT-resolution fail-fast from plan 02-02 (D-04)"
    - "Best-effort %TEMP% breadcrumb: File.WriteAllText wrapped in try/catch so a read-only %TEMP% cannot crash the guard itself"
    - "SSID-only LINQ projection for log emission: string.Join over Profiles.Select(p => p.Ssid) guarantees no Psk field can reach the log line (D-15 enforced structurally, not by convention)"

key-files:
  created: []
  modified:
    - "src/Jaminator/Program.cs - +22 LOC: fail-fast PAT guard as first statements in Main + `using System.IO;` import"
    - "src/Jaminator/UI/MainForm.cs - +17 LOC: joined-manifest masked-PSK Info log line in OnLoad after the Manifest-version line + `using System.Linq;` import"

key-decisions:
  - "D-04 complement honoured: the build-time PAT fail-fast (02-02) is paired with a launch-time guard here so an EXE built by bare `dotnet build` (bypassing installer/build.ps1) fails fast with an actionable message instead of 401-ing against GitHub on first fetch"
  - "D-15 honoured: PSKs masked as the literal three asterisks `***` — no length-preserving mask, no first/last-char leak; SSID-only projection enforced by the LINQ Select so the Psk field is never in scope at the emission site"
  - "Guard placement: first executable statements in Main, before `Mode = ParseMode(args)` — confirmed by line-order gate (guard@36 < ParseMode@52). Covers all seven RunModes including the install path (installing a broken EXE only delays the failure)"
  - "Exit code 1 on guard trip — matches the existing headless-failure convention (`_ => 1`)"
  - "Tasks 1 + 2 landed in a single commit (d5fea26) rather than two atomic commits — minor deviation from per-task atomicity, see Deviations; both are additive, single-purpose, and touch disjoint files"

patterns-established:
  - "Launch-time fail-fast guard idiom for build-time-injected secrets — future build-time constants (e.g. a Phase 4 metrics endpoint key) can copy the empty/placeholder check + %TEMP% breadcrumb verbatim"
  - "Structural PSK masking: project to SSID-only via LINQ before the log call so the secret field is never a local at the emission site"

requirements-completed:
  - WIFI-01
  - WIFI-03

# Metrics
duration: ~7 min (executor terminated by a transport error after both code tasks committed; SUMMARY + close-out completed by the orchestrator)
completed: 2026-05-31
---

# Phase 02 Plan 05: MainForm + Program.cs Wi-Fi wiring Summary

**The Phase 2 observable behaviour is now wired in: `Program.Main` fails fast (exit 1 + stderr + `%TEMP%` breadcrumb) in every RunMode when the embedded Wi-Fi PAT is empty or still the `@@PAT@@` placeholder, and `MainForm.OnLoad` emits a single masked-PSK Info log line reporting the joined Wi-Fi profile count and SSID list — the first end-to-end confirmation that the dual-fetch SSID→PSK join (plan 02-03) actually populated the manifest.**

## Execution note (close-out)

The Wave 3 executor agent committed both code tasks cleanly (commit `d5fea26`) and then terminated on a transport-layer socket error before writing this SUMMARY.md. The orchestrator closed the plan out: validated the committed code against every static acceptance gate for Tasks 1 and 2 (all pass), confirmed a clean `dotnet build` (0 warnings / 0 errors) against a stubbed `BuildSecrets.g.cs`, wrote this SUMMARY, and merged the worktree. No code was re-executed — the committed work was complete and correct; only the metadata write had been lost.

## Accomplishments

- **Task 1 — fail-fast PAT guard** (`src/Jaminator/Program.cs`): added a guard as the first executable statements in `Main`, before `Mode = ParseMode(args)`. If `BuildSecrets.WifiPat` is empty or equals the literal `@@PAT@@`, it writes the canonical actionable message (naming `installer/build.ps1` and `installer/secrets/README.md`) to `Console.Error`, writes the same message + timestamp to `%TEMP%\Jaminator-fail-fast-{yyyyMMddHHmmss}.log` (best-effort, try/catch-wrapped), and returns `1`. Added `using System.IO;`.
- **Task 2 — joined-manifest log line** (`src/Jaminator/UI/MainForm.cs`): immediately after the existing "Manifest version" Info line, emit `Joined manifest: {N} Wi-Fi profile(s) — [{ssidList}] (PSKs: ***)` (or the zero-profile branch), with a ` (from cache)` suffix when `fromCache=true`. SSIDs are projected SSID-only via `Profiles.Select(p => p.Ssid)` so PSKs cannot reach the log. Added `using System.Linq;`.

## Verification

**Static acceptance gates — Task 1 (Program.cs), all pass:**
- `BuildSecrets.WifiPat` referenced exactly 2× (the `IsNullOrEmpty` check + the `@@PAT@@` compare) — negative-leak gate #9 satisfied
- `@@PAT@@`, `installer/build.ps1`, `installer/secrets/README.md`, `Console.Error.WriteLine`, `Jaminator-fail-fast` all present
- `using System.IO;` present exactly once
- Guard precedes `ParseMode` in source order (line 36 < line 52)

**Static acceptance gates — Task 2 (MainForm.cs), all pass:**
- `Joined manifest:` emission present; `(PSKs: ***)` mask present; `using System.Linq;` present once
- Zero `.Psk` references in the file — no plaintext-PSK path to the log

**Build:** `dotnet build src/Jaminator/Jaminator.csproj` succeeds with 0 warnings / 0 errors against a stubbed `BuildSecrets.g.cs` (the real file is build-time generated by `installer/build.ps1` on Windows and is gitignored). Stub removed after the check.

## Verification deferrals (Windows dev box — Task 3, blocking human-verify)

Task 3 is a `checkpoint:human-verify` dev-laptop smoke test that **cannot run in this Linux CI environment** — it requires the Windows build box, a real fine-grained PAT, a private secrets repo, and a firewall-block step. It is recorded here and surfaces as a HUMAN-UAT item at phase verification. The operator must, on the Windows dev box:

1. Drop a real PAT into `installer/secrets/wifi-pat.txt` and the secrets URL into `installer/secrets/wifi-secrets-url.txt`, then run `installer/build.ps1` end-to-end and confirm the EXE inlines `BuildSecrets.WifiPat` / `BuildSecrets.SecretsUrl`.
2. Launch the EXE in UI mode and confirm the `Joined manifest:` line appears in `%ProgramData%\Jaminator\logs\jaminator-{today}.log` with the test SSID(s) and the literal `(PSKs: ***)` mask (never a plaintext PSK).
3. Confirm `cache\manifest.json` + `cache\secrets.json` both exist after the run.
4. Block the network and relaunch — confirm the joined-cache fallback emits `Joined manifest: ... (from cache)`.
5. Set `WifiPat = ""` (and again `"@@PAT@@"`), rebuild, and confirm the fail-fast guard fires (exit 1, stderr message, `%TEMP%` breadcrumb, no UI window, no `%ProgramData%\Jaminator\logs\` entry) — across UI, `--login-mode`, `--run-all`, `--install`.

## Deviations

- **Tasks 1 + 2 committed together** (`d5fea26`) rather than as two atomic commits. The two changes are additive, single-purpose, and touch disjoint files (`Program.cs` vs `MainForm.cs`), so the bundle is reviewable and revert-safe; no behaviour change versus the plan. Recorded for traceability.
- **SUMMARY.md authored by the orchestrator, not the executor**, because the executor agent died on a transport socket error after committing the code. The code itself is byte-for-byte the executor's committed work, validated against every static gate.

## Self-Check: PASSED
