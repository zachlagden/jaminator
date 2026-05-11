---
phase: 01-remove-broken-custom-action-and-improve-diagnostics
plan: 03
status: complete
date: 2026-05-11
tasks_completed: 2
commits:
  - "0d418da: chore(installer): delete UpdateCheck custom-action project"
  - "(this SUMMARY.md commit)"
requirements:
  - INSTALL-03
---

# Plan 01-03 — Delete UpdateCheck/ + verify clean MSI

## What was built

Two-part plan delivering the final gate on INSTALL-03:

1. **Task 1 (autonomous):** Physical deletion of `installer/UpdateCheck/` (the broken managed custom-action project — `UpdateCheck.csproj`, `UpdateCheckCA.cs`, plus untracked `bin/`, `obj/` build artifacts). One atomic commit per CONTEXT.md D-11 commit 3, message: `chore(installer): delete UpdateCheck custom-action project`. All five Task 1 acceptance criteria passed: directory gone, no tracked files under the path, no source/config references in `*.cs`/`*.csproj`/`*.sln`/`*.wxs`/`*.ps1`, commit message matches.
2. **Task 2 (D-09 artifact verification):** Originally scoped as a human Windows checkpoint, but executed on WSL by orchestrating Windows-side toolchain (`dotnet 8.0.420`, `wix 5.0.2`) via `powershell.exe -ExecutionPolicy Bypass` with a Windows-local staging directory (`C:\Users\Zach\Temp\jaminator-build`). UNC mounts (`\\wsl.localhost\…`) work for `dotnet build` but not for `wix build` (Windows Installer service runs as SYSTEM and can't reach the WSL mount — error `MsiException 1631`); staging to a Windows-local drive resolved that. After Windows-local rebuild produced a 1.09 MB `Jaminator.msi`, `wix msi decompile` (the lessmsi fallback documented in the plan, since lessmsi wasn't installed) was run and grepped for the three forbidden identifiers. All three returned zero matches.

## D-09 verification results

| Check | Identifier | Result |
|-------|-----------|--------|
| MSI Binary table | `UpdateCheckCA` | **0 matches** ✓ |
| MSI CustomAction table | `CheckForNewerVersion` | **0 matches** ✓ |
| MSI CustomAction table | `Wix4DTFCustomAction` (SFXCA helper) | **0 matches** ✓ |
| MSI InstallUISequence | `CheckForNewerVersion` | **0 matches** ✓ |

Sanity check confirmed the preserved elements are still present in the rebuilt MSI:

- `<CustomAction Id="LaunchApplication" BinaryRef="Wix4UtilCA_X86" DllEntry="WixShellExec" />` — post-install launch
- `<CustomAction Id="RegisterTask" Impersonate="no" Execute="deferred" FileRef="JaminatorExeFile" ExeCommand="--register-task" />` — scheduled task registration
- `<CustomAction Id="UnregisterTask" Impersonate="no" Execute="deferred" Return="ignore" FileRef="JaminatorExeFile" ExeCommand="--unregister-task" />` — scheduled task removal
- `<Publish Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 and NOT Installed" Event="DoAction" Value="LaunchApplication" />` — UI wiring for launch-app-on-finish
- `<Custom Action="RegisterTask" Condition="NOT Installed OR REINSTALL" After="InstallFiles" />` and `<Custom Action="UnregisterTask" Condition="REMOVE=&quot;ALL&quot;" Before="RemoveFiles" />` — InstallExecuteSequence wiring intact

## Build pipeline output

- `dotnet build Jaminator.sln -c Release` → exit 0; `Bootstrap.exe` + `Jaminator.exe` produced under `src/Jaminator/bin/Release/net48/`. **No `UpdateCheckCA.CA.dll` is produced** (the project no longer exists in the solution graph after Plan 01-02 + Plan 01-03 Task 1).
- `wix build installer\installer.wxs … -o build\Jaminator.msi` → exit 0 from the Windows-local staging dir.
- MSI size: 1.09 MB.

## Files changed

| File | Change |
|------|--------|
| `installer/UpdateCheck/UpdateCheck.csproj` | Deleted (was 979 bytes) |
| `installer/UpdateCheck/UpdateCheckCA.cs` | Deleted (was 7.0 KB) |
| `.planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-03-SUMMARY.md` | Created (this file) |

`build/Jaminator.msi` produced during verification was on a Windows-local staging dir (`C:\Users\Zach\Temp\jaminator-build\`) that has been cleaned up. The release MSI is rebuilt in Phase 3 after the version bump (RELEASE-02).

## Deviations from plan

- **Task 2 ran on WSL instead of being a manual Windows human-checkpoint.** Per the user's instruction ("you can do this, ur on wsl"), the orchestrator drove Windows PowerShell + Windows-side `dotnet`/`wix` via `powershell.exe` invoked from WSL, with a Windows-local staging directory for the MSI build (UNC paths break Windows Installer service). No semantic change — same commands, same gate, same outputs — just no waiting for the user.
- **`lessmsi` was not installed** on the Windows side. Used the documented fallback (`wix msi decompile` + `Get-Content … | Select-String`). This is explicitly listed as the fallback in the plan's `<interfaces>` block and in RESEARCH.md's Standard Stack table; not a deviation that affects the gate.
- **`dotnet clean` returned exit 1 on first invocation** (transient NuGet cache lock), 0 on re-run. Not investigated further; the subsequent `dotnet build` succeeded cleanly, so the failed clean had no real effect.

## INSTALL-03 status

**Closed.** The full requirement chain is now demonstrably satisfied:

| Layer | Plan | Evidence |
|-------|------|----------|
| WiX source | 01-01 (`6a97982`) | `installer/installer.wxs` no longer contains `CheckForNewerVersion`, `UpdateCheckCA`, or `<InstallUISequence>` referencing the CA |
| Solution + build script | 01-02 (`30905dd`) | `Jaminator.sln` no longer references `UpdateCheck.csproj`; `installer/build.ps1` no longer passes `-d UpdateCheckCaDll=…` |
| Project directory | 01-03 Task 1 (`0d418da`) | `installer/UpdateCheck/` does not exist; no source-file references remain |
| MSI artifact | 01-03 Task 2 (this run) | `wix msi decompile` of freshly built MSI produces zero matches for `UpdateCheckCA`, `CheckForNewerVersion`, `Wix4DTFCustomAction` |

Phase 1 success criteria (ROADMAP.md lines 28-32) are all satisfied. DIAG-01 was closed by Plan 01-04 in parallel (Wave 1).

## Next steps

`/gsd-execute-phase` finishes here. Phase 2 (Smoke-test the rebuilt MSI and document log capture) is the next milestone phase — it does the end-to-end install on a clean Win11/Win10 box and writes `docs/INSTALL-LOGGING.md`. Phase 3 then bumps the version and ships v0.7.5.
