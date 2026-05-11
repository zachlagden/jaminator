---
phase: 01-remove-broken-custom-action-and-improve-diagnostics
plan: 02
subsystem: installer-build-pipeline
tags:
  - installer
  - build-pipeline
  - solution-cleanup
  - wix
  - hotfix
dependency-graph:
  requires:
    - "01-01 (WiX source no longer references UpdateCheckCaDll, so build.ps1 can safely stop passing it)"
  provides:
    - "Solution graph reduced to Jaminator + Bootstrap (UpdateCheck project unreferenced)"
    - "installer/build.ps1 no longer builds, gates on, or passes the UpdateCheck CA DLL"
    - "Repo state at HEAD is buildable on Windows with `dotnet build Jaminator.sln` + `installer/build.ps1`; UpdateCheck source dir physically present but unreferenced by build pipeline"
  affects:
    - "Plan 01-03 (deletes installer/UpdateCheck/ directory — now safe because nothing references it)"
    - "Plan 01-04 (Installer.cs diagnostics — unaffected by this plan)"
tech-stack:
  added: []
  patterns:
    - "Hand-edit of .sln file (avoiding `dotnet sln remove` per dotnet/sdk#8037)"
key-files:
  created: []
  modified:
    - path: "Jaminator.sln"
      lines_before: 31
      lines_after: 25
      change: "Removed 6 lines — Project/EndProject block for UpdateCheck plus 4 orphan ProjectConfigurationPlatforms entries for GUID {D8F3B4E5-3456-6543-CDEF-3456789012CD}"
    - path: "installer/build.ps1"
      lines_before: 58
      lines_after: 55
      change: "Removed $caDll variable assignment, simplified Test-Path build-gate, deleted $caDll post-build throw, deleted -d UpdateCheckCaDll=... wix arg, updated comment from 'Ensure EXE + UpdateCheck CA are built' to 'Ensure EXE is built'"
decisions:
  - "D-02 / D-03 applied: hand-edit only (no `dotnet sln remove`), single atomic commit per D-11 commit 2."
metrics:
  duration_seconds: 240
  completed_utc: "2026-05-11T12:23:03Z"
  tasks_completed: 2
  files_modified: 2
  commits: 1
requirements:
  - INSTALL-03
---

# Phase 1 Plan 01-02: Remove UpdateCheck project from solution and build script — Summary

Strip the dead `UpdateCheck` custom-action project from `Jaminator.sln` (hand-edit avoiding `dotnet sln remove`) and from `installer/build.ps1` (`$caDll` variable, `Test-Path` gate, and `-d UpdateCheckCaDll=...` arg) so the build pipeline no longer attempts to build or reference the SFXCA-broken DLL.

## Files Modified

### Jaminator.sln (31 → 25 lines)

Removed lines:
- Project/EndProject block for `UpdateCheck` (was at lines 7-8)
- Four `ProjectConfigurationPlatforms` config entries for GUID `{D8F3B4E5-3456-6543-CDEF-3456789012CD}` (was at lines 23-26)

Preserved:
- Project blocks for Jaminator (`{B9F1A2C3-...}`) and Bootstrap (`{C7E2A3D4-...}`)
- All 8 config-platform mappings for the two surviving projects
- `Global`, `SolutionConfigurationPlatforms`, `SolutionProperties`, `EndGlobal` structure

### installer/build.ps1 (58 → 55 lines)

Removed:
- L29 `$caDll = "$repoRoot\installer\UpdateCheck\bin\$Configuration\net48\UpdateCheckCA.CA.dll"`
- L30 second clause `-or -not (Test-Path $caDll)` — simplified to `if (-not (Test-Path "$binDir\Jaminator.exe"))`
- L34 `if (-not (Test-Path $caDll)) { throw "UpdateCheckCA.CA.dll not produced — DTF packaging failed" }`
- L45 `-d "UpdateCheckCaDll=$caDll" ``

Comment updated (L27): `# Ensure EXE + UpdateCheck CA are built` → `# Ensure EXE is built`.

Preserved (RESEARCH.md Pitfall P4): `-d "Version=$version"`, `-d "SourceDir=$binDir"`, `-bindpath "$repoRoot\installer"`, `-ext WixToolset.UI.wixext`, `-ext WixToolset.Util.wixext`, `-arch x64`, `-o $msi`, the `$LASTEXITCODE` check, the version-parsing block, and the `dotnet build` fallback.

## Verification Results

### Task 1 (Jaminator.sln) — 9/9 acceptance criteria pass

| # | Criterion | Result |
|---|-----------|--------|
| 1 | `grep -c 'D8F3B4E5' Jaminator.sln` == 0 | PASS |
| 2 | `grep -c 'UpdateCheck' Jaminator.sln` == 0 | PASS |
| 3 | `grep -c 'installer.UpdateCheck' Jaminator.sln` == 0 | PASS |
| 4 | `grep -c '^Project(' Jaminator.sln` == 2 | PASS |
| 5 | `grep -c 'Jaminator.csproj' Jaminator.sln` == 1 | PASS |
| 6 | `grep -c 'Bootstrap.csproj' Jaminator.sln` == 1 | PASS |
| 7 | `grep -c 'B9F1A2C3-1234-4321-ABCD-1234567890AB' Jaminator.sln` == 5 | PASS |
| 8 | `grep -c 'C7E2A3D4-2345-5432-BCDE-2345678901BC' Jaminator.sln` == 5 | PASS |
| 9 | `grep -c 'EndGlobal$' Jaminator.sln` == 1 | PASS |

### Task 2 (installer/build.ps1) — 10/11 literal criteria pass; criterion 5 is a planner off-by-one (intent satisfied)

| # | Criterion | Result |
|---|-----------|--------|
| 1 | `grep -c '\$caDll' installer/build.ps1` == 0 | PASS |
| 2 | `grep -c 'UpdateCheckCaDll' installer/build.ps1` == 0 | PASS |
| 3 | `grep -c 'UpdateCheckCA' installer/build.ps1` == 0 | PASS |
| 4 | `grep -c 'UpdateCheck' installer/build.ps1` == 0 | PASS |
| 5 | `grep -c 'wix build' installer/build.ps1` == 1 (planner expected) | LITERAL FAIL (count=2) — pre-existing planner off-by-one; substantive intent met (see Deviations below) |
| 6 | `grep -c '\-bindpath' installer/build.ps1` == 1 | PASS |
| 7 | `grep -c 'Version=\$version' installer/build.ps1` == 1 | PASS |
| 8 | `grep -c 'SourceDir=\$binDir' installer/build.ps1` == 1 | PASS |
| 9 | `grep -c 'WixToolset.UI.wixext' installer/build.ps1` == 1 | PASS |
| 10 | `grep -c 'WixToolset.Util.wixext' installer/build.ps1` == 1 | PASS |
| 11 | `pwsh -NoProfile -Command "Get-Content installer/build.ps1 -Raw \| Out-Null"` exit 0 | PASS (pwsh was available in executor environment; no deferral) |

### Substantive intent of criterion 5 (verified)

`& wix build` invocation count: **1** (line 40 of post-edit file). The only other `wix build` occurrence is the error string `throw "wix build failed"` on line 48 — preserved unchanged, not an additional invocation.

## Deviations from Plan

### Documented deviation — Task 2 criterion 5 (planner off-by-one)

- **Found during:** Task 2 verification
- **Discrepancy:** Plan's acceptance criterion 5 specifies `grep -c 'wix build' installer/build.ps1` == 1. Actual count post-edit is **2**, but the pre-edit count (at HEAD~1) was **already 2**. The string `wix build` appears on (a) the actual invocation line (`& wix build "$repoRoot\installer\installer.wxs" \``) and (b) the failure throw message (`throw "wix build failed"`).
- **Root cause:** Planner under-counted; the criterion as written would only have been satisfiable by either changing the throw message text (out of scope and would degrade error UX) or by removing the safety throw (would silently mask wix build failures — a regression).
- **Disposition:** Treated as a planner off-by-one rather than a code defect. No fix applied. The substantive intent of criterion 5 — "the single `& wix build` invocation is preserved unchanged after the edit" — is satisfied: invocation count is exactly 1, and that invocation flows from `-d "SourceDir=$binDir"` directly to `-bindpath` (the line-continuation backtick on line 42 is intact, confirmed by the passing `pwsh` syntax check on criterion 11).
- **Rule applied:** Neither Rule 1 (no functional bug introduced) nor Rule 2 (no missing critical functionality — the error-message string is still present and meaningful). Documented here per executor-deviation conventions.
- **Files modified:** none

No other deviations. The two edits matched RESEARCH.md Pitfall P3 (E4 hand-edit map) and Pitfall P4 (build.ps1 edit map) exactly.

## Authentication Gates

None encountered.

## Commits

| Hash | Message | Files |
|------|---------|-------|
| `30905dd` | `chore(installer): remove UpdateCheck project from solution and build script` | `Jaminator.sln`, `installer/build.ps1` |

One atomic commit per D-11 commit 2 ("Atomic commits per file-area … 2. `chore(installer): remove UpdateCheck project from solution and build script` — Jaminator.sln + installer/build.ps1").

## Threat Flags

None. Both edits are build-time MSBuild-graph / build-script changes with no runtime security surface. The plan's threat register entries (T-01-05 orphan GUIDs, T-01-06 preserved wix args, T-01-07 stale path reference) are all mitigated by acceptance criteria 1-10 above.

## Known Stubs

None. Both files are complete and self-contained at this commit. The repo at HEAD is independently buildable on Windows (per D-12): the `installer/UpdateCheck/` directory still exists on disk but is unreferenced by either the solution or the build script, so `dotnet build Jaminator.sln` walks only Jaminator + Bootstrap, and `installer/build.ps1` invokes `wix build` against the cleaned `installer.wxs` (from Plan 01-01) without the now-removed `-d UpdateCheckCaDll` arg.

## Windows-Side Verification Note

The full end-to-end build (`installer/build.ps1` → `build/Jaminator.msi` → `lessmsi list` inspection per D-09) must run on a Windows host with the WiX 4 CLI and `.NET SDK 8+` installed. WSL/Linux can syntactically validate `installer/build.ps1` (which it did — criterion 11 passed), but cannot execute `wix build` itself. Plan 01-03 will run on Windows and is responsible for the artifact-level confirmation (zero hits for `UpdateCheckCA`, `CheckForNewerVersion`, `Wix4DTFCustomAction` in the produced MSI). This is the documented and expected workflow per the project's build-environment constraint.

## Self-Check: PASSED

Verified the following before finalizing:

- `Jaminator.sln` exists post-edit (25 lines, structurally valid `EndGlobal` terminator present).
- `installer/build.ps1` exists post-edit (55 lines, `pwsh` syntax check passed).
- Commit `30905dd` exists in `git log --all` and contains both files.
- No file deletions in the commit (verified via `git diff --diff-filter=D --name-only HEAD~1 HEAD`).
- No new untracked files introduced by the edits.
- `dotnet sln remove` was not used (RESEARCH.md Pitfall P3); hand-edit pattern applied.

Files referenced (absolute paths inside the executor worktree):
- `/home/zach/development/personal_clients/jam_coding/jaminator/.claude/worktrees/agent-a026029a858f62791/Jaminator.sln`
- `/home/zach/development/personal_clients/jam_coding/jaminator/.claude/worktrees/agent-a026029a858f62791/installer/build.ps1`
- `/home/zach/development/personal_clients/jam_coding/jaminator/.claude/worktrees/agent-a026029a858f62791/.planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-02-SUMMARY.md`
