# Roadmap: Jaminator — Milestone 1 (Installer Reliability Hotfix)

## Overview

v0.7.4's MSI fails interactive double-click installs on every modern Windows machine because the `UpdateCheck` custom action's SFXCA bundle is missing `CustomAction.config`, so the CLR refuses to host the managed CA (`SFXCA: Failed to get requested CLR info. Error code 0x80131700` → `CustomAction CheckForNewerVersion returned actual error code 1603` → rollback). The root cause is locked: the CA is being **removed entirely** rather than patched, because in-app `SelfUpdater.cs` already covers the same capability on every EXE launch, and network I/O inside an MSI custom action is a known anti-pattern.

The milestone ships v0.7.5 as a tagged GitHub Release. Three phases: **fix** the WiX source and rebuild, **verify** end-to-end on clean Win11 + Win10 boxes with documented verbose-log capture, **release** the signed MSI with notes that explain the bug, the fix, and the silent-install (`/qn`) workaround for users still stuck on v0.7.4. Goal-backward: at milestone end, a school technician can be pointed at the v0.7.5 GitHub Release URL, double-click the MSI on any classroom laptop, and watch Jaminator install and launch successfully.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (1.1, 1.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Remove broken custom action and improve diagnostics** - Strip `UpdateCheck` from installer.wxs and build, harden RegisterTask CA logging, produce a clean local MSI
- [ ] **Phase 2: Smoke-test the rebuilt MSI and document log capture** - Verify double-click, silent install, and SelfUpdater on clean Win11 + Win10 boxes; commit verbose-log capture docs
- [ ] **Phase 3: Tag and ship v0.7.5** - Version bump in `Program.cs`, git tag, GitHub Release with MSI asset and release notes

## Phase Details

### Phase 1: Remove broken custom action and improve diagnostics
**Goal**: The MSI builds cleanly with no `UpdateCheck` custom action surface, and the remaining `RegisterTask` CA emits actionable log output on any failure.
**Mode:** mvp
**Depends on**: Nothing (first phase)
**Requirements**: INSTALL-03, DIAG-01
**Success Criteria** (what must be TRUE):
  1. `installer/installer.wxs` contains no `CheckForNewerVersion` `CustomAction`, no `UpdateCheckCA` `Binary`, and no `InstallUISequence` entry referencing it
  2. The `installer/UpdateCheck/` project is removed from `Jaminator.sln` and from `installer/build.ps1` (the `$caDll` reference and the `-d UpdateCheckCaDll=...` argument no longer exist)
  3. `installer/build.ps1` runs to completion on Windows and produces a `build/Jaminator.msi` file
  4. The `RegisterTask` deferred CA wraps its `Jaminator.exe --register-task` invocation with logging that surfaces stderr/exit-code context in the MSI verbose log on failure (no more silent return-value-3-with-no-context)
**Plans**: TBD

### Phase 2: Smoke-test the rebuilt MSI and document log capture
**Goal**: The rebuilt MSI is proven to install end-to-end on the canonical target environments via every entry path, and a documented procedure exists for capturing a verbose log when future failures are reported.
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: INSTALL-01, INSTALL-02, INSTALL-04, INSTALL-05, DIAG-02, RELEASE-01
**Success Criteria** (what must be TRUE):
  1. Double-clicking the rebuilt MSI on a clean Windows 11 64-bit machine completes the wizard without rollback, places `Jaminator.exe` under `C:\Program Files\Jaminator\`, creates the Start Menu shortcut, and registers the `Jaminator-Login` scheduled task
  2. Double-clicking the same MSI on a Windows 10 fleet-target laptop completes with the same outcome
  3. Silent install (`msiexec /i Jaminator.msi /qn`) continues to succeed end-to-end on Windows 11 (regression check — this was the v0.7.4 escape hatch and must not break)
  4. Launching the installed `Jaminator.exe` triggers `SelfUpdater` on a machine running an older Jaminator version and upgrades cleanly via msiexec hand-off (the capability replacing the removed install-time CA)
  5. A `docs/INSTALL-LOGGING.md` (or equivalent location — README section or release-notes appendix) documents the `msiexec /i Jaminator.msi /l*v <path>` verbose-log procedure with an example invocation
**Plans**: TBD

### Phase 3: Tag and ship v0.7.5
**Goal**: v0.7.5 is published as a tagged GitHub Release with the MSI attached and release notes that explain the bug, the fix, and the silent-install workaround for users still stuck on v0.7.4.
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: RELEASE-02, RELEASE-03
**Success Criteria** (what must be TRUE):
  1. `Program.cs::ToolVersion` reads `"0.7.5"` (the single source of truth that `installer/build.ps1` parses for the MSI version)
  2. A `v0.7.5` annotated tag exists in the git repo, pushed to origin
  3. A GitHub Release named `v0.7.5` exists at `github.com/zachlagden/jaminator/releases/tag/v0.7.5` with `Jaminator.msi` attached as a downloadable asset
  4. The release notes body explains: (a) what the bug was (missing `CustomAction.config` in the SFXCA bundle → CLR couldn't host the managed CA → return code 1603 → rollback), (b) what the fix is (CA removed; `SelfUpdater` on EXE launch covers the same capability), and (c) the silent-install workaround (`msiexec /i Jaminator.msi /qn`) for anyone still on the broken v0.7.4 MSI
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Remove broken CA and improve diagnostics | 0/TBD | Not started | - |
| 2. Smoke-test and document log capture | 0/TBD | Not started | - |
| 3. Tag and ship v0.7.5 | 0/TBD | Not started | - |
