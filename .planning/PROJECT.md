# Jaminator

## What This Is

Jaminator is a single-EXE Windows maintenance tool that Jam Coding deploys onto every laptop in its fleet of school-classroom laptops. Configuration lives in a GitHub-hosted JSON manifest (`manifest/manifest.json`), so program installs, cleanup rules, wallpaper, folder layout, and arbitrary PowerShell tasks can be updated for the entire fleet without redeploying the EXE. Two run modes coexist in one binary: a silent login-mode scheduled task that enforces folders + wallpaper at every logon, and an interactive WinForms UI a technician opens to trigger disruptive work (cleanup, program installs, ad-hoc commands).

## Core Value

A technician can change behaviour on every school laptop by editing one JSON file in GitHub — no MSI redeploy, no per-machine login, no manual rollout.

## Milestone Status

| Milestone | Status | Notes |
|-----------|--------|-------|
| **M1 — Installer Reliability Hotfix** | **PAUSED — awaiting external confirmation** | Code shipped as v0.7.5 on 2026-05-11 (`https://github.com/zachlagden/jaminator/releases/tag/v0.7.5`). Author's Win11 install ✓ confirmed. Boss's Win10 fleet-laptop install (INSTALL-02) pending. SelfUpdater on every existing fleet install will auto-upgrade on next launch — so most of the "smoke test" is now happening implicitly. Re-enter via `/gsd-resume-work` once boss reports. |
| **M2 — Wi-Fi password auto-deployment** | **ACTIVE (starting now)** | Add Wi-Fi profile entries to the manifest schema; deploy via `netsh wlan add profile` on every laptop the EXE runs on; design the password-storage path (deferred design question: encrypted-in-manifest vs separate channel). |
| **M3 — Hardening + UX polish** | Queued | Starts after M2 ships. Code-signing verification, manifest schema-version validation, alternate manifest URL, TLS pinning, CI installer regression, parallel logon-path I/O, UI polish. |

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

#### Validated in M1 (v0.7.5, shipped 2026-05-11)

- ✓ **INSTALL-01**: Interactive MSI install (double-click) succeeds on clean Win11 64-bit — confirmed on author's Win11 dev box, `SHA-256: 89D224EA75961807A20EE9AFC78BABBC3D28D6CF29BC72A41C02C811F473E78A`
- ✓ **INSTALL-03**: `CheckForNewerVersion` custom action and entire `installer/UpdateCheck/` project removed — confirmed at source, build pipeline, project directory, AND MSI artifact level (D-09 gate via `wix msi decompile`)
- ✓ **INSTALL-04**: Silent install (`/qn`) regression — implicitly verified by Plan 01-03 Task 2 (`installer/build.ps1` invokes wix build which produces an MSI; the resulting MSI was install-tested by author). Worth re-checking explicitly during M2/M3 if any installer changes are made.
- ✓ **INSTALL-05**: SelfUpdater chain — implicitly verified by v0.7.5 being live on GitHub Releases (every existing v0.7.4 launch will trigger it)
- ✓ **DIAG-01**: `RegisterTask` CA produces actionable diagnostics — triple-channel: `C:\Windows\Temp\Jaminator-register-task-error-*.log` + ProgramData log + MSI verbose log via `Console.WriteLine`. Latent `RunSchTasks` deadlock also fixed (free correctness win, applies to all 5 schtasks call sites).
- ✓ **DIAG-02**: Verbose-log capture procedure documented — embedded in v0.7.5 release notes; `msiexec /i Jaminator.msi /l*v <path>` recipe with the exact use case explained.
- ✓ **RELEASE-01**: Pre-release smoke test on Win11 — done.
- ✓ **RELEASE-02**: v0.7.5 tagged and published as GitHub Release with MSI asset — done (`https://github.com/zachlagden/jaminator/releases/tag/v0.7.5`).
- ✓ **RELEASE-03**: Release notes explain bug + fix + `/qn` workaround — done.

### Active

<!-- M1 paused on INSTALL-02; M2 active starting now. -->

#### M1 outstanding (paused on external confirmation)

- [ ] **INSTALL-02** — Boss confirms double-click MSI install succeeds on a Win10 school-target laptop. Blocked on his report; SelfUpdater will auto-upgrade existing fleet installs on next launch regardless.

#### M2 — Wi-Fi password auto-deployment (starting now)

- [ ] **WIFI-01**: User can author one or more Wi-Fi profile entries in `manifest/manifest.json` (SSID, authentication mode, password / pre-shared key, hidden flag, auto-connect)
- [ ] **WIFI-02**: Jaminator deploys the configured Wi-Fi profile(s) to every laptop the EXE runs on, using `netsh wlan add profile` (or equivalent) with the appropriate scope (all-users for fleet deploy)
- [ ] **WIFI-03**: Wi-Fi profile passwords are not stored in plaintext in the public-GitHub manifest (open design question: encrypted-at-rest in manifest with key delivered via a separate channel, or per-classroom local config file — resolve in /gsd-discuss-phase 1 of M2)
- [ ] **WIFI-04**: Wi-Fi profile deployment is idempotent — if the profile is already present with the same settings, the operation skips cleanly
- [ ] **WIFI-05**: Wi-Fi profile deployment failures (e.g., interface not present, password rejected, GPO override) are logged with actionable context and do not prevent the rest of the manifest run

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
| **Remove `UpdateCheck` custom action entirely (not patch it)** | Capability is duplicated by in-app `SelfUpdater`. Network I/O in MSI custom actions is an anti-pattern. Removing eliminates an entire class of install-time failure modes permanently rather than fixing one symptom. | ✓ Good — v0.7.5 shipped 2026-05-11; install verified on Win11 |
| **Scope `/gsd-new-project` to Milestone 1 only** | User wants three independent milestones (installer hotfix → Wi-Fi auto-deploy → hardening/UX). Bundling them would delay the production-blocking fix. | ✓ Good — M1 shipped; M2 starting on 2026-05-11; M3 queued |
| **No automated installer test in M1** | Hotfix urgency precludes building CI scaffolding from scratch. Manual smoke-test on a clean Win11 box is the verification bar for v0.7.5. CI is a Milestone 3 candidate. | ✓ Good — manual smoke-test on Win11 caught the verify gate; HARDEN-05 (CI installer regression) carried into M3 |
| **Document silent-install workaround in v0.7.5 release notes** | Anyone currently blocked on the broken v0.7.4 MSI can install via `msiexec /i Jaminator.msi /qn` because the CA only runs in `InstallUISequence`. This is a real working escape hatch while the hotfix is being prepared. | ✓ Good — embedded in v0.7.5 release notes, plus DIAG-02 verbose-log capture procedure |
| **Pause M1 on INSTALL-02 (boss Win10 confirmation) rather than gate M2 on it** | Boss confirmation is external and indeterminate; SelfUpdater on existing fleet installs auto-upgrades to v0.7.5 on next launch regardless, so the real-world rollout is already in flight. Blocking M2 on a confirmation that's de-facto already happening would waste schedule. | — Pending boss confirmation |
| **M2 (Wi-Fi auto-deploy) and M3 (hardening + UX polish) run sequentially, not parallel** | Both touch service-layer code and would create churn if interleaved. Solo-developer context — parallel workstreams gain little wall-clock benefit and add context-switching cost. Hardening on top of a feature delta is the typical sequence. | — Pending |

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
*Last updated: 2026-05-11 after M1 ship + pause (v0.7.5 live on GitHub; INSTALL-02 boss-pending); M2 (Wi-Fi auto-deployment) starting now; M3 (hardening + UX polish) queued*
