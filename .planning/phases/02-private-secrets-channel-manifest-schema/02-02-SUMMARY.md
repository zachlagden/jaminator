---
phase: 02-private-secrets-channel-manifest-schema
plan: 02
subsystem: infra
tags: [powershell, gitignore, build-script, secrets, github-pat, source-generation, msi, wix]

# Dependency graph
requires:
  - phase: 02-private-secrets-channel-manifest-schema
    provides: locked decisions D-04, D-05, D-06, D-07, D-08, D-16 from 02-CONTEXT.md and verbatim code patterns from 02-RESEARCH.md
provides:
  - Gitignored installer/secrets/ operator drop directory with .keep + README.md markers tracked
  - .gitignore rules covering installer/secrets/* (negated for .keep + README.md) and src/Jaminator/Generated/
  - Operator runbook (installer/secrets/README.md) covering PAT scope, rotation cadence, env-var fallback, gitignore semantics, threat-model honesty
  - installer/build.ps1 PAT/URL resolution block (file-first, env-var fallback, fail-fast) injected ahead of dotnet build
  - installer/build.ps1 BuildSecrets.g.cs generator emitting Jaminator.BuildSecrets.WifiPat / SecretsUrl as internal const strings into src/Jaminator/Generated/
affects:
  - 02-03 (ManifestFetcher dual-fetch — consumes BuildSecrets.WifiPat and BuildSecrets.SecretsUrl)
  - 02-05 (debug log + fail-fast guard — references BuildSecrets symbols)
  - HARDEN-07 (future CI hardening; env-var fallback path is the CI handoff point)

# Tech tracking
tech-stack:
  added:
    - PowerShell here-string source generation pattern for embedding build-time secrets into compiled .NET binaries
    - Microsoft.NET.Sdk auto-glob compile pickup of generated C# files in src/Jaminator/Generated/ (no csproj edit needed)
  patterns:
    - "Build-time secret injection: PowerShell resolves PAT/URL from gitignored file → env-var fallback → fail-fast, then writes a C# const-only source file before dotnet build invokes the compiler"
    - "Operator drop directory: gitignored dir with negated tracked markers (.keep + README.md) keeping the directory in version control while excluding its contents"
    - "Fail-fast build-script error messages that name both candidate sources (file path + env var name) and point to the operator runbook"

key-files:
  created:
    - installer/secrets/.keep
    - installer/secrets/README.md
    - .planning/phases/02-private-secrets-channel-manifest-schema/02-02-SUMMARY.md
  modified:
    - .gitignore
    - installer/build.ps1

key-decisions:
  - "D-04 enforced: PAT resolution order is installer/secrets/wifi-pat.txt → $env:JAMINATOR_WIFI_PAT → throw with operator-actionable message naming both sources and pointing to installer/secrets/README.md"
  - "D-05 enforced: installer/secrets/ exists in-tree via .keep + README.md negations; trailing /* on installer/secrets/* is mandatory per git's 're-include parent-excluded path' limitation (RESEARCH.md line 748)"
  - "D-06 enforced: generated file path is src/Jaminator/Generated/BuildSecrets.g.cs, namespace Jaminator, internal static class BuildSecrets with internal const string WifiPat and internal const string SecretsUrl"
  - "D-07 enforced: WifiPat and SecretsUrl are internal const (not static readonly) so the C# compiler inlines them at call sites, matching the operational-opacity threat model"
  - "D-08 enforced: SecretsUrl resolves via the same precedence chain as the PAT (installer/secrets/wifi-secrets-url.txt → $env:JAMINATOR_WIFI_SECRETS_URL → fail-fast); never declared in the public manifest"
  - "D-16 step 2 honoured: single atomic commit 'build(installer): add gitignored secrets directory and PAT resolution script' bundles both tasks"

patterns-established:
  - "Pattern 1: Secret values never appear in Write-Host / Write-Output / Write-Verbose — only the generated file path is logged ('Generated <path>' in Cyan). Enforced via the acceptance #9 negative grep gate."
  - "Pattern 2: New PowerShell block lives inside the existing try { … } finally { Pop-Location } envelope so $ErrorActionPreference = 'Stop' makes throws fatal and cleanup still runs."
  - "Pattern 3: New-Item -ItemType Directory -Path $generatedDir -Force | Out-Null for idempotent directory creation before Set-Content."
  - "Pattern 4: Defensive double-quote escaping ($wifiPat -replace '\"', '\\\"') even though a real fine-grained PAT never contains quotes — protects against operator-supplied URLs accidentally including surrounding quotes."

requirements-completed:
  - WIFI-03

# Metrics
duration: ~3 min
completed: 2026-05-31
---

# Phase 02 Plan 02: Build-time secret injection scaffolding Summary

**PowerShell-driven build-time PAT/URL resolution that writes a gitignored `BuildSecrets.g.cs` (internal const strings) before `dotnet build`, plus a gitignored `installer/secrets/` operator drop directory with a runbook.**

## Performance

- **Duration:** ~3 min (Linux worktree — static-only verification; Windows behavioural checks deferred per plan acceptance #13–17 which require a Windows build box)
- **Started:** 2026-05-31T15:14:00Z (approximate, worktree spawn)
- **Completed:** 2026-05-31T15:17:14Z
- **Tasks:** 2 (Task 1: gitignore + markers; Task 2: build.ps1 generator block)
- **Files modified:** 4 (.gitignore, installer/build.ps1, installer/secrets/.keep, installer/secrets/README.md)

## Accomplishments

- `.gitignore` now blocks PAT/URL files (`installer/secrets/*`) and the generated source file (`src/Jaminator/Generated/`) while keeping the operator-facing drop directory tracked via `.keep` + `README.md` negations. Verified with `git check-ignore -v installer/secrets/wifi-pat.txt` and `git check-ignore -v src/Jaminator/Generated/BuildSecrets.g.cs` — both return the expected matching rules.
- `installer/secrets/README.md` documents PAT placement, fine-grained `Contents: Read` scope (no org / no other repo), termly rotation cadence aligned with PSK rotation, `$env:JAMINATOR_WIFI_PAT` / `$env:JAMINATOR_WIFI_SECRETS_URL` env-var fallback for future CI, the gitignore rule semantics (with the "Do not weaken these rules" warning), and the WIFI-03 threat-model honesty cross-reference.
- `installer/build.ps1` now resolves the PAT and secrets URL (file-first, env-var fallback, fail-fast with verbatim messages from RESEARCH.md line 599) and writes `src/Jaminator/Generated/BuildSecrets.g.cs` via a here-string + `Set-Content -Path ... -NoNewline -Encoding UTF8`. The generation block sits inside the existing `try { ... }` envelope and runs BEFORE the conditional `dotnet build` invocation, so the Microsoft.NET.Sdk auto-glob picks up the file without any csproj edit.
- The success log line (`Write-Host "Generated $generatedFile" -ForegroundColor Cyan`) names only the file path. No `Write-Host $wifiPat`, no `Write-Output $secretsUrl`, no `Write-Verbose $escapedPat`. The negative grep gate (acceptance #9) returns zero matches.

## Task Commits

Both tasks shipped as a single atomic commit per D-16 step 2:

1. **Task 1 + Task 2 (bundled per D-16 step 2): `build(installer): add gitignored secrets directory and PAT resolution script`** — `6972db7` (build)

_The plan's `<output>` block explicitly mandates the single combined commit so Phase 2's 5-commit strategy stays intact (one commit per plan)._

## Files Created/Modified

- `.gitignore` — Added two new comment-then-rule blocks after `/installers-staging/`: (1) `installer/secrets/*` + `!installer/secrets/.keep` + `!installer/secrets/README.md` for the operator drop directory, (2) `src/Jaminator/Generated/` for the build-time generated source file. Trailing `/*` on the secrets-dir rule is mandatory per git's "cannot re-include a file under an excluded parent" limitation (RESEARCH.md line 748). Verified via `grep -c` acceptance gates 1–4 — all returned `1`.
- `installer/secrets/.keep` — Empty (0-byte) marker file force-added with `git add -f` so the directory exists on fresh clone. The `installer/secrets/*` rule would otherwise exclude it from the staging set even though the `!installer/secrets/.keep` negation eventually re-includes it (negation only applies once the file is actually enumerated in the index).
- `installer/secrets/README.md` — Operator runbook with seven sections: What to drop here, PAT permissions (fine-grained, `Contents: Read`, no org / no other repo), Rotation cadence (termly, aligned with PSK rotation), Env-var fallback, What is gitignored here (with "Do not weaken these rules" warning), Threat-model honesty (pointer to PROJECT.md WIFI-03), Cross-references.
- `installer/build.ps1` — Inserted a 44-line block after `Write-Host "Building Jaminator MSI v$version"` and before the EXE-presence-check, doing: PAT resolve (file → env var → throw), URL resolve (file → env var → throw), `New-Item -ItemType Directory -Path $generatedDir -Force`, defensive double-quote escape on both values, here-string assembly of the canonical C# file body from RESEARCH.md lines 572–583 with `$escapedPat` / `$escapedUrl` interpolated, `Set-Content -Path $generatedFile -Value $content -NoNewline -Encoding UTF8`, `Write-Host "Generated $generatedFile" -ForegroundColor Cyan`. Entire block lives inside the existing `try { … } finally { Pop-Location }` envelope so `$ErrorActionPreference = 'Stop'` makes throws fatal and cleanup still runs.

## Decisions Made

None new — every decision was pre-locked in `02-CONTEXT.md` `<decisions>` D-04, D-05, D-06, D-07, D-08, D-16, and the plan supplied verbatim code patterns from `02-RESEARCH.md` lines 572–583 and 587–635. Execution was a faithful application of the locked decisions plus copy-from-pattern.

## Deviations from Plan

None — plan executed exactly as written. All canonical patterns from RESEARCH.md were transcribed verbatim into `installer/build.ps1` and `.gitignore`. The README content matches the `<action>` step-3 outline section-for-section.

## Issues Encountered

None.

## Pre-existing Conditions (informational, not deviations)

- The orchestrator note flagged that the user pre-populates `installer/secrets/wifi-pat.txt` and `installer/secrets/wifi-secrets-url.txt` on the main checkout. In this worktree those files were NOT present (as expected — worktrees have their own working tree). Only `.keep` and `README.md` were committed; no PAT-bearing file ever entered the staging set. Verified via `git diff --cached --name-only | grep -E 'wifi-pat\.txt|wifi-secrets-url\.txt'` returning zero matches before commit.

## Threat Surface Review

No new threat surface beyond what `<threat_model>` already lists. The plan-level threat register entries T-01 (PAT in source control), T-02 build-side (PAT in build logs), T-01 residual (force-added generated file), and T-02 PAT-from-MSI are all addressed exactly as specified:

- T-01 mitigated by both gitignore rules + verified absence (acceptance #17 deferred to Windows box; Linux-side `git check-ignore -v` already confirms both rules match the intended files).
- T-02 build-side mitigated by the negative grep gate (acceptance #9 passed locally).
- T-01 residual accepted; documented in README "What is gitignored here" → "Do not weaken these rules."
- T-02 PAT-from-MSI accepted per PROJECT.md WIFI-03; rotation cadence documented in README.

No threat-flag section needed.

## Verification Deferrals (Windows-only acceptance criteria)

Acceptance criteria 12–17 in Task 2 require a Windows build box with .NET SDK + WiX 4 CLI (per CLAUDE.md "WSL/Linux is fine for code edits and Git, but the actual MSI rebuild must happen on Windows"). They are explicitly OUT OF SCOPE for this worktree:

- #12: `dotnet msbuild ... -preprocess` auto-glob check — Windows-only.
- #13: `pwsh installer/build.ps1` end-to-end success with PAT files present — Windows-only.
- #14: Fail-fast behaviour when neither source set — Windows-only.
- #15: Env-var fallback path — Windows-only.
- #16: `git status` shows no `BuildSecrets.g.cs` post-build — Windows-only.
- #17: `git log --all -p -- 'installer/secrets/wifi-pat.txt' ...` empty — partially verified here (no such commits in this worktree's history); the full assertion lands when the operator runs the build on Windows.

These are NOT failures — they are the planned cross-platform handoff per Phase 2 ROADMAP, and Plan 02-05 will exercise the runtime side of the same plumbing.

## User Setup Required

None for this plan's verification. For full end-to-end build verification on the Windows build box, the operator must satisfy the four `preconditions` listed in the plan frontmatter (private repo + fine-grained PAT + test SSID/PSK + drop the PAT and URL into `installer/secrets/wifi-pat.txt` and `installer/secrets/wifi-secrets-url.txt`, OR set the corresponding env vars) before invoking `pwsh installer/build.ps1`. These pre-conditions are explicitly OPERATOR responsibilities (not plan tasks) per the plan frontmatter.

## Next Phase Readiness

- Plan 02-03 (ManifestFetcher dual-fetch) can now compile its references to `Jaminator.BuildSecrets.WifiPat` and `Jaminator.BuildSecrets.SecretsUrl`, provided the operator has run `installer/build.ps1` once on Windows with the PAT and URL sources in place. The Microsoft.NET.Sdk auto-glob picks up the generated file from `src/Jaminator/Generated/BuildSecrets.g.cs` with no csproj edit (verified at plan-time per RESEARCH.md Pitfall 6 / Assumption A4; runtime acceptance #12 lands on Windows).
- Plan 02-05 (debug log + fail-fast guard) similarly unblocked.
- HARDEN-07 (M3 CI workflow) has a clean handoff point: set `$env:JAMINATOR_WIFI_PAT` / `$env:JAMINATOR_WIFI_SECRETS_URL` in the CI environment, no file-system staging needed.

## Self-Check: PASSED

- `.gitignore` modifications committed: `git show 6972db7 -- .gitignore` shows the two new blocks present.
- `installer/secrets/.keep` exists and is 0 bytes: `test -f installer/secrets/.keep && test ! -s installer/secrets/.keep` — both succeeded.
- `installer/secrets/README.md` exists with the seven required sections: verified via `grep -c` for `wifi-pat.txt`, `JAMINATOR_WIFI_PAT`, `Contents: Read`, `WIFI-03` — all returned ≥1.
- `installer/build.ps1` contains all required tokens per acceptance #1–11: all greps returned ≥1.
- Negative grep (acceptance #9, no `Write-Host $wifiPat` etc.): returned 0 matches.
- Commit `6972db7` exists on branch `worktree-agent-ad64d01450565920e`: `git log --oneline | grep 6972db7` matches.
- No PAT-bearing file staged or committed: `git diff --cached --name-only` (pre-commit) and `git show --stat 6972db7` confirm only the four expected files; zero matches for `wifi-pat.txt` / `wifi-secrets-url.txt` / `BuildSecrets.g.cs` in commit contents.

---
*Phase: 02-private-secrets-channel-manifest-schema*
*Plan: 02 — Build-time secret injection scaffolding (gitignore + secrets dir + build.ps1 generator)*
*Completed: 2026-05-31*
