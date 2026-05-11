# Phase 1: Remove broken custom action and improve diagnostics - Context

**Gathered:** 2026-05-11
**Status:** Ready for planning
**Mode:** --auto (recommended option selected for every gray area, logged inline)

<domain>
## Phase Boundary

**In scope:**
- Strip the `installer/UpdateCheck/` custom action surface from the MSI build pipeline (WiX source, solution file, build script) so the SFXCA shim is no longer invoked during install.
- Harden the remaining `RegisterTask` deferred custom action (`installer/installer.wxs` line 150-155; backed by `src/Jaminator/Services/Installer.cs::RegisterScheduledTask`) so its failure path produces a discoverable, actionable log on the target machine rather than vanishing into MSI's generic "return value 3" rollback.
- Produce a clean local rebuild of `build/Jaminator.msi` on Windows that contains no `UpdateCheckCA` binary or `CheckForNewerVersion` action.

**Out of scope (handled by Phase 2 and 3):**
- End-to-end install/uninstall verification on clean Win11 / Win10 boxes — Phase 2.
- Verbose-log capture documentation (`docs/INSTALL-LOGGING.md`) — Phase 2 (folded with smoke-test workstream because the doc is written while running `msiexec /l*v` during testing).
- Version bump, git tag, GitHub Release, release notes — Phase 3.

**Covers requirements:** INSTALL-03, DIAG-01

</domain>

<decisions>
## Implementation Decisions

### UpdateCheck removal strategy

- **D-01:** Delete `installer/UpdateCheck/` directory entirely (`UpdateCheckCA.cs`, `UpdateCheck.csproj`). Do NOT leave a tombstone README or empty directory — git history (`git log -- installer/UpdateCheck/`) plus the v0.7.5 release notes (Phase 3) preserve the rationale. A tombstone in-tree just becomes confusing dead-text.
  - **[auto] Q: How thoroughly remove the UpdateCheck surface? → Selected: Delete entirely (recommended). Alternatives considered: keep dir with TOMBSTONE.md / keep dir and stop building. Rejected because git + release notes already document the why, and dead code in-tree is a footgun for future maintainers.**

- **D-02:** Remove the `installer/UpdateCheck/UpdateCheck.csproj` reference from `Jaminator.sln` so the solution builds cleanly without the project.

- **D-03:** Remove from `installer/build.ps1`:
  - The `$caDll` variable (line 29)
  - The `Test-Path $caDll` build-gate check (line 30, 34)
  - The `-d "UpdateCheckCaDll=$caDll"` argument passed to `wix build` (line 45)

- **D-04:** Remove from `installer/installer.wxs`:
  - The `<Binary Id="UpdateCheckCA" .../>` element (line 70)
  - The `<CustomAction Id="CheckForNewerVersion" .../>` element (lines 71-76)
  - The `<InstallUISequence><Custom Action="CheckForNewerVersion" .../></InstallUISequence>` block (lines 86-90)
  - **Keep:** `LaunchApplication`, `RegisterTask`, `UnregisterTask`, all WixUI UI elements, all ComponentGroups. Only the UpdateCheck-related elements are removed.

### RegisterTask diagnostics (DIAG-01)

- **D-05:** Enrich `src/Jaminator/Services/Installer.cs::RegisterScheduledTask` (line 149) so that on the `catch (Exception ex)` branch (line 210), it writes a discoverable error file to `%TEMP%\Jaminator-register-task-error-YYYYMMDDhhmmss.log` containing:
  - Timestamp + run mode (`--register-task`)
  - Full exception type, message, and stack trace
  - The `schtasks.exe` command line that was attempted and its captured stdout/stderr (currently `RunSchTasks` swallows these — needs to surface them)
  - Path to the failing task XML (which today is deleted in the `finally`; the diagnostics path should defer deletion when an error occurs)
  - **[auto] Q: How do we surface --register-task failure context for the MSI log? → Selected: TEMP-path log file (recommended). Alternatives considered: PowerShell wrapper writing to MSI session via SetProperty / add a new managed CA. Rejected because deferred FileRef CAs can't write MSI session properties directly, and adding a new managed CA reintroduces the SFXCA surface we're removing. The TEMP log is simple, discoverable, documented in release notes, and uses existing logging infra.**

- **D-06:** Modify `RunSchTasks` (the helper that wraps `schtasks.exe` invocation in `Installer.cs`) to capture stdout and stderr instead of letting them go to console. On non-zero exit, throw with the captured output included in the exception message — so the catch in `RegisterScheduledTask` has the actual schtasks failure reason to write into the TEMP log.

- **D-07:** Keep the existing `log.Error("Failed to register scheduled task", ex)` call (which writes to `%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log`). The TEMP log is **additive** — ProgramData log stays as the canonical record; the TEMP log exists because at MSI install time the rollback may delete ProgramData artifacts before the user can read them.

- **D-08:** The CA's existing `Return="check"` semantics (installer.wxs line 155) stay unchanged — non-zero exit still propagates as MSI failure. We're only making the failure *legible*, not changing the rollback behavior.

### Build verification

- **D-09:** Phase 1 completion is gated by an artifact-level check: after `installer/build.ps1` succeeds, run `lessmsi list build\Jaminator.msi` (or `wix --no-extract-binaries` equivalent — planner picks the exact command) and confirm zero hits for `UpdateCheckCA`, `CheckForNewerVersion`, `Wix4DTFCustomAction`. Build script exit-code alone is necessary but not sufficient.
  - **[auto] Q: How do we verify Phase 1 is complete at artifact level? → Selected: build + MSI inspection (recommended). Alternatives: trust build exit-code / run install on dev machine. Rejected because exit-code doesn't prove the CA was actually stripped, and end-to-end install belongs in Phase 2.**

- **D-10:** No version bump in Phase 1. `Program.cs::ToolVersion` stays at `"0.7.4"` until Phase 3 — the local rebuild produced here is a *verification artifact*, not a release. This keeps Phase 1 atomic and reversible without polluting the version history.

### Commit strategy

- **D-11:** Atomic commits per file-area, in this order:
  1. `fix(installer): remove CheckForNewerVersion custom action from WiX source` — installer.wxs edits only
  2. `chore(installer): remove UpdateCheck project from solution and build script` — Jaminator.sln + installer/build.ps1
  3. `chore(installer): delete UpdateCheck custom-action project` — `rm -r installer/UpdateCheck/`
  4. `feat(installer): emit register-task failure diagnostics to TEMP log` — Installer.cs changes (D-05, D-06, D-07)
  - **[auto] Q: How to structure commits? → Selected: atomic per file-area (recommended). Alternative: one bundled commit. Rejected because surgical commits make Phase 1 trivially revertable if Phase 2 smoke-test catches a regression — we'd want to bisect, not roll back the entire phase.**

- **D-12:** Each commit is independently buildable. After commits 1-3, `installer/build.ps1` must produce a working MSI; after commit 4, `dotnet build Jaminator.sln` must succeed and the EXE's `--register-task` path must still register successfully on a clean machine (the diagnostics change is *additive*, never alters happy-path behavior).

### Claude's Discretion

- Exact technique for capturing schtasks.exe stdout/stderr in `RunSchTasks` (D-06) — `Process.StandardOutput.ReadToEndAsync()` vs synchronous reads vs `ProcessStartInfo.RedirectStandardOutput`. Planner picks; the contract is "captured output ends up in the catch's exception message."
- Exact MSI-inspection tool for D-09 (`lessmsi`, `wix decompile`, `msidump`, Orca CLI) — planner picks based on what's installable on the user's Windows dev box.
- Whether to introduce a small helper (e.g., `Installer.WriteDiagnosticLog(string area, Exception ex)`) shared between `RegisterScheduledTask` and `UnregisterScheduledTask`, or inline the TEMP-log write in `RegisterScheduledTask` only. Either is fine — the v0.7.5 hotfix doesn't require generalized diagnostics across every entry point.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents (researcher, planner, executor) MUST read these before planning or implementing.**

### Project planning artifacts

- `.planning/PROJECT.md` — Project context, especially the Key Decisions table (remove-CA-not-patch, hotfix scope, no automated installer test) and the Constraints block (Windows-only build, manual smoke test).
- `.planning/REQUIREMENTS.md` — Specifically INSTALL-03 (line 14) and DIAG-01 (line 20) which define Phase 1's deliverables.
- `.planning/ROADMAP.md` — Phase 1 success criteria (lines 28-32).

### Codebase reference

- `.planning/codebase/STACK.md` — .NET 4.8 / WiX v4 / Newtonsoft.Json stack confirmation; build prerequisites.
- `.planning/codebase/ARCHITECTURE.md` — Component table and layered architecture; `Installer` and `Logger` responsibilities.
- `.planning/codebase/INTEGRATIONS.md` — MSI integration section (lines 143-149), scheduled-task integration (lines 125-137).
- `.planning/codebase/CONCERNS.md` — Background security/perf concerns; not directly in scope for Phase 1 but useful to avoid accidentally reverting hardening.

### Files being edited in this phase

- `installer/installer.wxs` (lines 66-90 — the CheckForNewerVersion block to remove; lines 148-162 — the RegisterTask/UnregisterTask deferred CAs that stay).
- `installer/UpdateCheck/UpdateCheckCA.cs` — to delete (the broken managed CA).
- `installer/UpdateCheck/UpdateCheck.csproj` — to delete.
- `installer/build.ps1` (lines 27-50 — `$caDll` reference and the `-d UpdateCheckCaDll=...` build arg).
- `Jaminator.sln` — project reference to `installer/UpdateCheck/UpdateCheck.csproj` to remove.
- `src/Jaminator/Services/Installer.cs` (lines 149-215 — `RegisterScheduledTask` body and the `RunSchTasks` helper).
- `src/Jaminator/Services/Logger.cs` — existing logging API; reused, not modified.

### Live evidence (root-cause logs)

- `/mnt/c/Users/Zach/AppData/Local/Temp/jaminator-install.log` (line 144 onward) — user's Win11 machine; SFXCA CLR-load failure signature.
- `/mnt/c/Users/Zach/OneDrive/Desktop/jaminator-install.log.txt` (line 145 onward) — boss's failing school laptop; identical signature, different user/machine, confirming this is a packaging defect not an environment issue.

### External docs (informational, not authoritative)

- WiX Toolset v4 docs on `InstallUISequence` and `InstallExecuteSequence` ordering — relevant when verifying the removal doesn't break sequencing.
- WixToolset.Dtf.CustomAction packaging notes — informational only; we're removing this dependency.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets

- **`Logger` class (`src/Jaminator/Services/Logger.cs`)** — Thread-safe append-only logger writing to `%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log`. Already used inside `RegisterScheduledTask`. The diagnostic enhancement (D-05) keeps this call and adds a TEMP-path duplicate write; Logger itself doesn't need changes.
- **`SelfUpdater` (`src/Jaminator/Services/SelfUpdater.cs`)** — The in-app updater that runs on every EXE launch. Already shipped in v0.7.4. **This is the architectural justification for removing the install-time CA** — capability is fully duplicated here and there's nothing else to write to make this work; it's the existing safety net.
- **`bootstrap/Program.cs`** — Standalone downloader that also hits the GitHub releases API. Independent of the MSI custom action; not affected by Phase 1.

### Established Patterns

- **Single-source-of-truth versioning** — `Program.cs::ToolVersion` is parsed by `installer/build.ps1` regex (`'ToolVersion\s*=\s*"([\d.]+)"'`). Phase 1 does NOT touch this; version bump is Phase 3's job (D-10).
- **CLI-mode dispatch** — `Program.cs` parses a `RunMode` enum and dispatches to handlers including `RegisterTask`/`UnregisterTask`. The MSI custom actions invoke `Jaminator.exe --register-task` and `--unregister-task` (installer.wxs line 152, 159). These contracts are stable; Phase 1 doesn't change the CLI surface.
- **Exception-to-log-then-exit-1 pattern** — Used throughout `Installer.cs` (lines 140-144, 210-214). The diagnostics enhancement (D-05) follows this pattern — it adds an extra log target before returning 1, not a new error-handling model.

### Integration Points

- **MSI ↔ EXE contract** — Two deferred CAs (`RegisterTask`, `UnregisterTask`) in `installer.wxs` call `Jaminator.exe --register-task` and `--unregister-task`. Phase 1's diagnostic improvement happens entirely on the EXE side; the WiX deferred-CA wiring (FileRef, Execute="deferred", Impersonate="no", Return="check"/"ignore") stays unchanged.
- **WiX → MSI artifact** — `wix build installer/installer.wxs ... -o build/Jaminator.msi` is the single output gate. After Phase 1, this command must succeed without `-d UpdateCheckCaDll=...`.
- **Self-update fallback** — `SelfUpdater.cs` on first EXE launch closes the loop for users who installed an outdated v0.7.5+ MSI. Not modified in Phase 1, but the milestone *relies* on it working — Phase 2's INSTALL-05 verifies it still does.

</code_context>

<specifics>
## Specific Ideas

- **Failure-log filename format:** `Jaminator-register-task-error-YYYYMMDDhhmmss.log` in `%TEMP%`. Use the install-attempt timestamp so each failed install produces its own file (the user may try multiple times). The release notes (Phase 3, RELEASE-03) and the DIAG-02 doc (Phase 2) should both reference this path.
- **MSI inspection target** (for D-09): The MSI's `Binary` table should contain no row with `Name = UpdateCheckCA`; the `CustomAction` table should contain no row with `Action = CheckForNewerVersion`; the `InstallUISequence` table should contain no row referencing `CheckForNewerVersion`. Any tool that lists these tables (lessmsi, msidump, Orca, `wix decompile`) is fine.

</specifics>

<deferred>
## Deferred Ideas

### For Phase 2

- Documenting the `%TEMP%\Jaminator-register-task-error-*.log` filename and the `msiexec /l*v` procedure in `docs/INSTALL-LOGGING.md` (DIAG-02).
- Verifying that the rebuilt MSI installs cleanly on a clean Windows 11 64-bit machine (INSTALL-01) and a Windows 10 fleet-target laptop (INSTALL-02), including the silent-install regression check (INSTALL-04) and SelfUpdater chain (INSTALL-05).

### For Phase 3

- Bumping `Program.cs::ToolVersion` to `"0.7.5"`.
- Tagging the release and creating the GitHub Release with notes that explain the bug, the fix, and the `/qn` workaround.

### For future milestones (not Milestone 1)

- **Generalized install-time diagnostics** — extending the TEMP-log pattern to all install-time entry points (currently only `--register-task` is enriched). Candidate for Milestone 3 hardening.
- **Reintroducing an install-time update check via a native (C++) custom action** — explicitly rejected for Milestone 1 (PROJECT.md Key Decisions). If reconsidered in Milestone 3 it would be a new design, not a port.
- **CI / automated installer regression test on a clean Windows VM** — listed as HARDEN-05 in REQUIREMENTS.md v2.

</deferred>

---

*Phase: 01-Remove-broken-custom-action-and-improve-diagnostics*
*Context gathered: 2026-05-11*
