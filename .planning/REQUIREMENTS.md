# Requirements: Jaminator — Milestone 1 (Installer Reliability Hotfix)

**Defined:** 2026-05-11
**Core Value:** A technician can change behaviour on every school laptop by editing one JSON file in GitHub — no MSI redeploy, no per-machine login, no manual rollout.

## v1 Requirements

Requirements for the v0.7.5 hotfix release. Each maps to roadmap phases.

### INSTALL — installer succeeds again

- [ ] **INSTALL-01**: Installer succeeds via double-click of the MSI on a clean Windows 11 64-bit machine (currently fails with "ended prematurely")
- [ ] **INSTALL-02**: Installer succeeds via double-click of the MSI on a Windows 10 fleet-target laptop (canonical school-classroom environment)
- [ ] **INSTALL-03**: The `CheckForNewerVersion` custom action and the entire `installer/UpdateCheck/` project are removed from the MSI build — no managed-CA surface remains in the install path
- [ ] **INSTALL-04**: Silent install (`msiexec /i Jaminator.msi /qn`) continues to succeed end-to-end (regression check: this is the existing escape hatch for the v0.7.4 break and must not be lost)
- [ ] **INSTALL-05**: Self-update on EXE launch (`SelfUpdater.cs`) continues to work — including the case where a user installs an older MSI and `SelfUpdater` upgrades them on first launch (the capability that's replacing the removed install-time CA)

### DIAG — install-time diagnostics

- [ ] **DIAG-01**: The remaining deferred custom action (`RegisterTask` — invokes `Jaminator.exe --register-task`) produces actionable output in the MSI log on any failure (no silent return-value-3-with-no-context), so future install regressions are diagnosable from the standard MSI verbose log
- [ ] **DIAG-02**: A documented procedure exists for capturing a verbose MSI install log (`msiexec /i Jaminator.msi /l*v <path>`) — either in the README, the release notes, or a `docs/` page — so any future failure report can be triaged immediately from a log instead of a generic dialog screenshot

### RELEASE — shipping the hotfix

- [ ] **RELEASE-01**: A pre-release smoke test of the rebuilt MSI is executed on a clean Windows 11 64-bit machine (and ideally one school-target Win10 laptop) before tagging v0.7.5 — install must complete via double-click, Jaminator must launch from the resulting Start Menu shortcut, and the scheduled task must be registered
- [ ] **RELEASE-02**: Version bumped to v0.7.5 in `Program.cs::ToolVersion` (the single source of truth that `installer/build.ps1` reads); the rebuilt MSI is tagged as `v0.7.5` in git and a GitHub Release is created with the MSI attached as a downloadable asset
- [ ] **RELEASE-03**: Release notes for v0.7.5 explain the bug (interactive MSI install was failing because of a missing `CustomAction.config` in the `UpdateCheckCA` bundle), the fix (removed the custom action; in-app `SelfUpdater` continues to handle update checks on EXE launch), and the silent-install workaround (`msiexec /i Jaminator.msi /qn`) for anyone still stuck on the broken v0.7.4 MSI

## v2 Requirements

Acknowledged but deferred to future milestones. Tracked here so they're not lost.

### Milestone 2 candidates — Wi-Fi auto-deployment

- **WIFI-01**: User can author one or more Wi-Fi profile entries in `manifest/manifest.json` (SSID, authentication mode, password / pre-shared key, hidden flag, auto-connect)
- **WIFI-02**: Jaminator deploys the configured Wi-Fi profile(s) to every laptop the EXE runs on, using `netsh wlan add profile` (or equivalent) with the appropriate scope (all-users for fleet deploy)
- **WIFI-03**: Wi-Fi profile passwords are not stored in plaintext in the public-GitHub manifest (deferred design question: encrypted-at-rest in manifest with key delivered via a separate channel, or per-classroom local config file)
- **WIFI-04**: Wi-Fi profile deployment is idempotent — if the profile is already present with the same settings, the operation skips cleanly
- **WIFI-05**: Wi-Fi profile deployment failures (e.g., interface not present, password rejected, GPO override) are logged with actionable context and do not prevent the rest of the manifest run

### Milestone 3 candidates — hardening + UX polish

- **HARDEN-01**: Code-signing / Authenticode verification of downloaded third-party MSI/EXE installers before execution (currently SHA256-only — manifest compromise would let an attacker swap hashes and binaries together)
- **HARDEN-02**: Manifest schema-version validation — reject manifests whose `schemaVersion` is newer than the tool understands, with a clear log message (currently fails silently to null fields)
- **HARDEN-03**: Alternate manifest URL / fallback host — eliminate the single-point-of-failure on `raw.githubusercontent.com`
- **HARDEN-04**: TLS certificate pinning for `github.com` API endpoints, or at minimum structured logging of HTTPS failures to detect MITM
- **HARDEN-05**: CI / smoke-test automation for installer regressions — automated install on a clean Windows VM as part of every release
- **HARDEN-06**: Parallel logon-path I/O (manifest + wallpaper fetched concurrently) to reduce student-visible wait at logon
- **UX-01**: UX/UI polish across the WinForms section panels and progress display (described as "light" — code and UI already look decent)

## Out of Scope

Explicitly excluded from Milestone 1. Documented to prevent scope creep into the hotfix.

| Feature | Reason |
|---------|--------|
| Wi-Fi password auto-deployment | Planned as **Milestone 2** — out of scope here to keep this hotfix focused on a working MSI |
| Hardening pass + UX/UI polish | Planned as **Milestone 3** — described as "light"; deferred for the same reason |
| Install-time "newer version available" prompt | Architecturally removed in this milestone — capability is duplicated by `SelfUpdater.cs` on every EXE launch, and MSI custom actions doing network I/O is a known anti-pattern that just opened a class of failure modes |
| Patching `UpdateCheckCA` with a `CustomAction.config` to keep the install-time update flow | Considered and rejected — `SelfUpdater` already covers the capability and removing the CA permanently eliminates this class of failure |
| Automated installer regression testing (CI on clean VM) | Listed as **HARDEN-05** for Milestone 3 — building it during a hotfix would add risk and delay; smoke-test discipline (`RELEASE-01`) is the v0.7.5 bar |
| Rewriting `UpdateCheck` as a native (C++) custom action | Considered and rejected — adds maintenance surface; `SelfUpdater` already does the job |

## Traceability

Which phases cover which requirements.

| Requirement | Phase | Status |
|-------------|-------|--------|
| INSTALL-01 | Phase 2 | Pending |
| INSTALL-02 | Phase 2 | Pending |
| INSTALL-03 | Phase 1 | Pending |
| INSTALL-04 | Phase 2 | Pending |
| INSTALL-05 | Phase 2 | Pending |
| DIAG-01    | Phase 1 | Pending |
| DIAG-02    | Phase 2 | Pending |
| RELEASE-01 | Phase 2 | Pending |
| RELEASE-02 | Phase 3 | Pending |
| RELEASE-03 | Phase 3 | Pending |

**Coverage:**
- v1 requirements: 10 total
- Mapped to phases: 10 ✓
- Unmapped: 0 ✓

---
*Requirements defined: 2026-05-11*
*Last updated: 2026-05-11 after roadmap creation — traceability table populated*
