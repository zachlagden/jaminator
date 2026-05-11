# Jaminator

## What This Is

Jaminator is a single-EXE Windows maintenance tool that Jam Coding deploys onto every laptop in its fleet of school-classroom laptops. Configuration lives in a GitHub-hosted JSON manifest (`manifest/manifest.json`), so program installs, cleanup rules, wallpaper, folder layout, and arbitrary PowerShell tasks can be updated for the entire fleet without redeploying the EXE. Two run modes coexist in one binary: a silent login-mode scheduled task that enforces folders + wallpaper at every logon, and an interactive WinForms UI a technician opens to trigger disruptive work (cleanup, program installs, ad-hoc commands).

## Core Value

A technician can change behaviour on every school laptop by editing one JSON file in GitHub — no MSI redeploy, no per-machine login, no manual rollout.

## Requirements

### Validated

<!-- Existing capabilities in v0.7.4 — confirmed working in production. -->

- ✓ Single-EXE WinForms application with section-card UI for selectable maintenance sections — `src/Jaminator/UI/MainForm.cs`, `SectionPanel.cs`
- ✓ Manifest fetched from GitHub at every run with on-disk cache fallback for offline classroom periods — `src/Jaminator/Services/ManifestFetcher.cs`
- ✓ Login-mode scheduled task running only "login-safe" sections (folders + wallpaper) at every user logon — `src/Jaminator/Services/Installer.cs` (task registration)
- ✓ Interactive run-all and CLI silent-install / silent-uninstall entry points (one binary, multiple modes) — `src/Jaminator/Program.cs`
- ✓ Program detection (registry, file presence, version, AppX package) with idempotent skip-if-installed — `src/Jaminator/Services/Detector.cs`, `MsiInstaller.cs`
- ✓ MSI / EXE / ZIP installer support with SHA256 verification of downloads — `src/Jaminator/Services/Downloader.cs`, `MsiInstaller.cs`
- ✓ Cleanup runner for temp paths, recycle bin, browser caches, and document quarantine — `src/Jaminator/Services/CleanupRunner.cs`
- ✓ Wallpaper download + apply, with optional enforcement on every logon — `src/Jaminator/Services/WallpaperSetter.cs`
- ✓ Folder structure provisioning under `Documents\` (8 school folders × 6 year groups in current manifest) — `src/Jaminator/Services/FolderManager.cs`
- ✓ PowerShell / CMD command runner with `skipIf` condition evaluation — `src/Jaminator/Services/CommandRunner.cs`
- ✓ In-app self-updater that checks GitHub releases on every launch and self-upgrades via msiexec — `src/Jaminator/Services/SelfUpdater.cs` (shipped in v0.7.4)
- ✓ ProgramData-based logging (`%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log`) and state persistence — `src/Jaminator/Services/Logger.cs`, `State.cs`
- ✓ Internet gating with timeout + cache fallback for offline-resilient logon runs — `src/Jaminator/Services/InternetGate.cs`
- ✓ Signed MSI installer with WixUI_Minimal welcome+EULA flow, perMachine scope, x64, Start Menu + desktop shortcuts — `installer/installer.wxs`

### Active

<!-- This milestone (Milestone 1: Installer Reliability Hotfix). -->

- [ ] **Installer succeeds on a clean Win11 64-bit machine via double-click of the MSI** (currently fails identically on every interactive install with the generic "ended prematurely" dialog)
- [ ] **Failed `CheckForNewerVersion` custom action removed at root cause** — the SFXCA bundle's missing `CustomAction.config` makes the CLR refuse to host the managed CA on any modern Windows; the entire CA is being removed rather than patched (capability is already provided by in-app `SelfUpdater` on every EXE launch)
- [ ] **Future install-time failures produce actionable diagnostics** — any remaining custom action (e.g., the deferred `RegisterTask` that invokes `Jaminator.exe --register-task`) must either log clearly to the MSI log or surface a usable error rather than the generic wizard message
- [ ] **Pre-release smoke test on a clean target-class machine before any MSI is published to GitHub Releases** — the next install regression must not reach production
- [ ] **Hotfix MSI tagged and published as v0.7.5** with release notes documenting the bug, the fix, and the silent-install (`msiexec /i Jaminator.msi /qn`) workaround for anyone still stuck on the broken v0.7.4 MSI

### Out of Scope

<!-- Tracked elsewhere or deferred to future milestones. Reasons included to prevent re-adding. -->

- **Install-time update prompt ("a newer version is available — install it instead?")** — being removed in this milestone; in-app `SelfUpdater` already handles the case on first EXE launch, and doing network I/O inside an MSI custom action is a known anti-pattern that adds an entire class of failure modes to the install path
- **Wi-Fi password auto-deployment to fleet laptops** — planned as **Milestone 2**; deferred to keep this hotfix laser-focused on shipping a working MSI
- **Hardening pass + UX/UI polish** — planned as **Milestone 3** (described as "light" because the code and UI already work well); deferred for the same reason
- **CI / automated regression testing for installer** — desirable but out of scope for a hotfix; documented as a candidate for Milestone 3
- **Code-signing of downloaded third-party installers** (Authenticode verification before running MSI/EXE assets referenced in manifest) — security recommendation in CONCERNS.md, candidate for Milestone 3
- **Alternate manifest URL / DNS-based failover** — security recommendation in CONCERNS.md, candidate for Milestone 3

## Context

- **Brownfield project.** v0.7.4 already ships and is in active use on a fleet of school-classroom laptops. Codebase fully mapped on 2026-05-11 → `.planning/codebase/` (STACK, STRUCTURE, ARCHITECTURE, INTEGRATIONS, CONVENTIONS, CONCERNS, TESTING).
- **The installer bug is a production incident.** Boss reported "quite a few laptops fail to install with no error message." Initial framing was "no obvious pattern"; investigation revealed the pattern is `interactive install = fails, silent install = succeeds`, which is consistent across machines and OS versions because the root cause is a packaging defect, not an environmental one.
- **Two MSI logs in hand from 2026-05-11 confirm identical failure signature:**
  - User's Win11 64-bit dev machine (`/mnt/c/Users/Zach/AppData/Local/Temp/jaminator-install.log`, line 144)
  - Boss's school laptop, different user account (`/mnt/c/Users/Zach/OneDrive/Desktop/jaminator-install.log.txt`, line 145)
  - Both show: `SFXCA: Failed to get requested CLR info. Error code 0x80131700` → `CustomAction CheckForNewerVersion returned actual error code 1603` → MSI rolls back → `Action ended: INSTALL. Return value 3`
- **Capability duplication.** The install-time update check (`installer/UpdateCheck/UpdateCheckCA.cs`) duplicates what `src/Jaminator/Services/SelfUpdater.cs` does on every EXE launch. Removing the CA loses no real user-visible capability except the niche case of "user double-clicks an outdated MSI before launching the app for the first time" — which `SelfUpdater` resolves on first launch anyway.
- **Codebase concerns surfaced during mapping** are catalogued in `.planning/codebase/CONCERNS.md` (security: single-point manifest, unsigned installers, no TLS pinning; performance: synchronous logon-path I/O, large installer downloads). Most are explicit candidates for Milestone 3 hardening, not for this hotfix.
- **Test surface today** is sparse — there is no automated test project in the solution (per `TESTING.md`), so verification for this milestone is manual smoke testing on a clean Windows machine before tagging the release.

## Constraints

- **Tech stack**: .NET Framework 4.8 / C# / WinForms / WiX Toolset v4 — locked by existing codebase; this milestone touches only WiX (`installer/installer.wxs`, `installer/UpdateCheck/`) and possibly the build script (`installer/build.ps1`).
- **Target platform**: Windows 7 SP1+ baseline via `net48`, with the actual fleet on Windows 10 / 11 64-bit; install must work via double-click on Win10 and Win11 (the canonical technician deployment path).
- **Deployment**: GitHub Releases — the hotfix ships as a tagged GitHub release with the rebuilt MSI as an asset; downstream `SelfUpdater` will pick it up on first launch of v0.7.4 instances.
- **Build environment**: .NET SDK 8+ + WiX 4 CLI installed locally; build is `installer/build.ps1` on Windows. WSL/Linux is fine for code edits and Git, but the actual MSI rebuild must happen on Windows.
- **Privileges**: install requires local admin (perMachine scope, `Program Files\Jaminator\`, scheduled task registration) — no change planned.
- **Timeline**: ASAP — this is a hotfix release; aim to ship within days.
- **No automated test suite** exists in the repo today, so this milestone's verification step is manual smoke install on a clean target machine.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| **Remove `UpdateCheck` custom action entirely (not patch it)** | Capability is duplicated by in-app `SelfUpdater`. Network I/O in MSI custom actions is an anti-pattern. Removing eliminates an entire class of install-time failure modes permanently rather than fixing one symptom. | — Pending (this milestone) |
| **Scope this command to Milestone 1 only** | User wants three independent milestones (installer hotfix → Wi-Fi auto-deploy → hardening/UX). Bundling them would delay the production-blocking fix. | — Pending |
| **No automated installer test in this milestone** | Hotfix urgency precludes building CI scaffolding from scratch. Manual smoke-test on a clean Win11 box is the verification bar for v0.7.5. CI is a Milestone 3 candidate. | — Pending |
| **Document silent-install workaround in v0.7.5 release notes** | Anyone currently blocked on the broken v0.7.4 MSI can install via `msiexec /i Jaminator.msi /qn` because the CA only runs in `InstallUISequence`. This is a real working escape hatch while the hotfix is being prepared. | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-05-11 after initialization (Milestone 1: Installer Reliability Hotfix)*
