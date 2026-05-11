# Codebase Concerns

**Analysis Date:** 2026-05-11

## Security Concerns

### Single-Point Manifest Source

**Risk:** The application fetches configuration exclusively from `https://raw.githubusercontent.com/zachlagden/jaminator/main/manifest/manifest.json` hardcoded in `src/Jaminator/Program.cs` line 11. If GitHub becomes unavailable or is compromised, deployments to 100+ student laptops cannot be updated.

**Files:** `src/Jaminator/Program.cs`, `src/Jaminator/Services/ManifestFetcher.cs`

**Current mitigation:** On-disk cache at `ProgramData\Jaminator\cache\manifest.json` falls back to last successful fetch during network failures.

**Recommendations:** 
- Consider DNS-based failover (CNAME record pointing to secondary host)
- Document disaster-recovery procedure for manifest recovery
- Add ability to specify alternate manifest URL via local config file (not committed to repo)

---

### Unsigned Installer Executables

**Risk:** Downloaded program installers (MSI, EXE, ZIP) are downloaded over HTTPS and SHA256-verified by `src/Jaminator/Services/Downloader.cs`, but the SHA256 hashes themselves are stored in the manifest (single source of truth). If the manifest is compromised, attacker can replace hashes to validate malicious installers.

**Files:** `src/Jaminator/Services/Downloader.cs` line 36-51, `manifest/manifest.json`

**Current mitigation:** GitHub URLs are public and HTTPS-verified; manifest is human-reviewed before commit.

**Recommendations:**
- Implement code-signing verification for EXE/MSI files (check Authenticode signature before running)
- Consider pinning manifest to a signed commit (git verify-signature)
- Document hash rotation procedure when installers are updated

---

### Hardcoded Installation Paths

**Risk:** Installation directory is hardcoded to `Program Files\Jaminator` (`src/Jaminator/Services/Installer.cs` line 22-23). In shared lab environments with low-trust users, predictable paths enable privilege escalation attacks if the directory is writable to non-admin users.

**Files:** `src/Jaminator/Services/Installer.cs` lines 22-26

**Mitigation:** Directory permissions are inherited from `Program Files\` which defaults to admin-only write access on Windows.

**Recommendations:**
- Verify ACLs on install directory after MSI completes
- Log directory permissions on startup as health check
- Consider randomized install path (though MSI Start Menu uninstall assumes standard location)

---

### PowerShell Execution Policy Bypass

**Risk:** CommandRunner and other code use `-ExecutionPolicy Bypass` when launching PowerShell scripts (`src/Jaminator/Services/CommandRunner.cs` line 42, `src/Jaminator/Services/Installer.cs` line 427). This bypasses system security policies in Group Policy-managed environments.

**Files:** `src/Jaminator/Services/CommandRunner.cs` lines 36-46, `src/Jaminator/Services/Installer.cs` line 427

**Current mitigation:** Tool runs as SYSTEM (elevated) with scheduled task, not as untrusted user.

**Recommendations:**
- Document that Bypass is intentional and required for scheduled task context
- Consider using `-ExecutionPolicy RemoteSigned` if manifests are signed
- Add audit logging of all PowerShell invocations to event log

---

### Manifest Schema Validation

**Risk:** The manifest JSON has no schema version enforcement. If manifest structure breaks (e.g., renames fields), deserialization silently fails with `null` properties, and behavior becomes undefined.

**Files:** `src/Jaminator/Models/Manifest.cs` line 8, `src/Jaminator/Services/ManifestFetcher.cs` line 42-43

**Current mitigation:** Manifest version field exists but is not validated against tool version.

**Recommendations:**
- Add strict version check: reject manifests with unsupported `schemaVersion`
- Add detailed validation error messages instead of silent nulls
- Test manifest upgrade path (old tool reading new manifest, vice versa)

---

### No TLS Certificate Pinning

**Risk:** All network requests (manifest, wallpaper, installer downloads) use standard HTTPS without certificate pinning. A compromised CA or MITM on school network could serve malicious content.

**Files:** `src/Jaminator/Services/SelfUpdater.cs` line 18, `src/Jaminator/Services/ManifestFetcher.cs` line 19, `src/Jaminator/Services/Downloader.cs` line 10

**Current mitigation:** GitHub infrastructure is assumed trusted; school network is assumed to have no MITM appliances.

**Recommendations:**
- Monitor for unusual network errors (potential MITM sign)
- Consider implementing certificate pinning for github.com API endpoints
- Log all HTTPS failures with details for debugging

---

## Performance Bottlenecks

### Synchronous File I/O on Logon Path

**Risk:** The login-mode scheduled task (`--login-mode`) runs manifest fetch, wallpaper download, and cleanup operations synchronously. On slow school networks or during offline lessons, students wait 30+ seconds at logon before desktop appears.

**Files:** `src/Jaminator/Services/ManifestFetcher.cs` line 36-67 (async but blocks UI), `src/Jaminator/Services/InternetGate.cs` line 33-64 (10s polling intervals), `src/Jaminator/Services/CleanupRunner.cs` line 28-71

**Current mitigation:** Network wait times out after 30s with manifest cache fallback; cleanup operations are per-directory graceful-skip on access denied.

**Recommendations:**
- Profile logon time on real school network (different ISP, latency, packet loss)
- Consider parallel downloads (manifest + wallpaper concurrently)
- Move cleanup to off-peak hours (daily auto-run at 03:00) instead of logon time
- Add per-operation timeout and skip-on-timeout logic

---

### Large Installer Files Downloaded On-Demand

**Risk:** Programs like Minecraft Education (848 MB) and MakeCode Arcade (285 MB) are downloaded from GitHub on first run or daily auto-run. Over slow/metered school networks, this consumes bandwidth and time.

**Files:** `manifest/manifest.json` lines 70-200 (installer URLs), `src/Jaminator/Services/Downloader.cs`

**Current mitigation:** Downloader caches locally to `ProgramData\Jaminator\cache\`; subsequent runs skip download if installer already cached.

**Recommendations:**
- Document expected bandwidth usage (≈1.4 GB for full program suite)
- Consider distributing installers via school file server (SMB) instead of GitHub
- Implement download prioritization (install X before Y to unblock students faster)
- Monitor cache size and implement cleanup if it exceeds quota

---

## Fragile Areas

### Version Comparison Logic

**Risk:** SelfUpdater and Detector use custom semver parsing that pads versions to 4-part form (`src/Jaminator/Services/SelfUpdater.cs` line 118-121). Edge cases like `1.0.0-alpha` or `2.0` may compare incorrectly.

**Files:** `src/Jaminator/Services/SelfUpdater.cs` lines 108-122, `src/Jaminator/Services/Detector.cs` lines 61-83

**Current mitigation:** Fallback to string comparison if Version.TryParse fails; semver tags in releases are well-formed (e.g., `v0.7.4`).

**Recommendations:**
- Add unit tests for edge cases (pre-release versions, mismatched part counts)
- Document version format requirements in RELEASES.md
- Consider using NuGet.Versioning library for robust semver handling

---

### Registry Key Hardcoding

**Risk:** Detector.cs and Installer.cs hardcode registry paths for uninstall detection, wallpaper settings, and program detection. Changes to Windows registry structure (unlikely but possible in major OS updates) will break silently.

**Files:** `src/Jaminator/Services/Detector.cs` lines 52-56, `src/Jaminator/Services/WallpaperSetter.cs` line 61, `src/Jaminator/Services/Installer.cs` lines 78-79

**Current mitigation:** Code uses well-documented registry paths (Uninstall, Control Panel\Desktop) unlikely to change.

**Recommendations:**
- Log registry read failures explicitly (not silently caught)
- Add fallback detection methods (e.g., if DisplayVersion missing, check file version on disk)
- Document registry assumptions in code comments

---

### Win32 API Assumptions

**Risk:** CleanupRunner.cs uses `SHEmptyRecycleBin` DllImport (`src/Jaminator/Services/CleanupRunner.cs` lines 13-17). WallpaperSetter.cs uses `SystemParametersInfo` DllImport (`src/Jaminator/Services/WallpaperSetter.cs` lines 16-17). Both assume specific Win32 signatures that may change or fail in containerized/virtual environments.

**Files:** `src/Jaminator/Services/CleanupRunner.cs` lines 13-17, `src/Jaminator/Services/WallpaperSetter.cs` lines 16-17

**Current mitigation:** All P/Invoke calls are wrapped in try-catch; failures log warnings but don't crash.

**Recommendations:**
- Test on latest Windows Server versions and Windows 11 IoT
- Document minimum Windows version requirement (currently tested on Windows 10)
- Monitor for "function not found" or AV false-positive blocks on P/Invoke calls

---

### Time-Based Logic for Daily Scheduler

**Risk:** Installer.cs schedules daily tasks using hardcoded date `2026-01-01T{hhmm}:00` as StartBoundary (`src/Jaminator/Services/Installer.cs` line 272). If a laptop is never turned on from Jan-Dec 2026, the boundary will never occur and the task won't run.

**Files:** `src/Jaminator/Services/Installer.cs` line 272

**Current mitigation:** Scheduled Task Scheduler automatically recalculates; boundary is merely advisory.

**Recommendations:**
- Use current date instead of hardcoded 2026-01-01
- Test that daily task runs within 24h of creation even if skipped dates
- Log when task is scheduled so we can verify it fired

---

### Network Share Path Assumptions

**Risk:** FolderManager creates paths relative to `%USERPROFILE%\Documents` (e.g., `Documents/St Augustines/Year 1`). If school uses network-mapped drives or OneDrive, Documents folder location may differ or be inaccessible during offline lessons.

**Files:** `src/Jaminator/Services/FolderManager.cs` (path expansion logic)

**Current mitigation:** User profile paths use environment variables (`%USERPROFILE%`, `%LOCALAPPDATA%`) which adapt to folder redirects in some cases.

**Recommendations:**
- Validate that Documents path is actually writable before trying to create subfolders
- Log folder creation failures with remediation steps
- Support UNC paths in manifest (e.g., `\\school-server\shared\Student Docs`) as fallback

---

## Data Integrity Risks

### Manifest Cache Corruption Recovery

**Risk:** If `ProgramData\Jaminator\cache\manifest.json` becomes corrupted (partial write, encoding mismatch), the JSON deserialization fails and no fallback exists. ManifestFetcher.cs line 54-55 catches and rethrows with both errors.

**Files:** `src/Jaminator/Services/ManifestFetcher.cs` lines 49-62

**Current mitigation:** Error message includes both network and cache failure reasons.

**Recommendations:**
- Implement atomic writes (write to `.tmp`, rename on success)
- Periodically validate cached manifest on startup
- Provide manual cache clear command (`--clear-cache`) in help

---

### Installer Download TOCTOU

**Risk:** Downloader.cs downloads to `{path}.part`, verifies hash, then moves to final path. If file is deleted between verification and move, behavior is undefined.

**Files:** `src/Jaminator/Services/Downloader.cs` lines 26-54

**Current mitigation:** File.Move with overwrite=true is atomic on NTFS; lock on cache dir during install would help.

**Recommendations:**
- Add explicit file-exists checks after move
- Log move operations with success/failure
- Consider locking cache directory during active installs to prevent concurrent downloads

---

## Test Coverage Gaps

### No End-to-End Testing

**Risk:** Code is tested with unit tests only. No end-to-end test verifies that manifest fetch → program install → cleanup → wallpaper → scheduled task all work together in real logon scenario.

**Files:** `src/Jaminator/` (entire codebase)

**Current risk:** Changes to one service (e.g., ManifestFetcher) may break others silently until deployed to production.

**Recommendations:**
- Create e2e test image with Windows 10/11 VM
- Test manifest update flow (old version → new version)
- Test network failure recovery (offline for 30s, then online)
- Automate nightly test runs before release

---

### No Load Testing

**Risk:** Cleanup wipes browser caches, temp folders, and recycle bin in a blocking loop. No testing verifies this completes in <10 minutes (ExecutionTimeLimit in scheduled task).

**Files:** `src/Jaminator/Services/CleanupRunner.cs` lines 75-115

**Recommendations:**
- Profile cleanup on a laptop with large temp folder (>1 GB)
- Add progress reporting (% done) to scheduled task log
- Consider parallel cleanup (Thread pool) if cleanup exceeds time limit

---

## Missing Critical Features

### No Installer Rollback

**Risk:** If a program install fails partway through, there's no rollback. Scheduled task will retry the same installer at next logon and fail again.

**Files:** `src/Jaminator/Services/MsiInstaller.cs` lines 71-118

**Recommendations:**
- Track which programs were attempted in state.json
- Add `--skip-program ID` mode to skip failing programs
- Consider system restore point before bulk installs

---

### No Audit Logging to Event Log

**Risk:** All logging is in-app (console or UI). School IT cannot audit logon runs via Windows Event Log, making compliance/troubleshooting difficult.

**Files:** `src/Jaminator/Services/Logger.cs` (logs to console/UI only)

**Recommendations:**
- Write critical events (install success/fail, cleanup stats) to Windows Event Log
- Use consistent event IDs (e.g., 1000 = install OK, 1001 = install fail)
- Document event log location for school IT monitoring

---

### No Remote Diagnostics or Phone-Home

**Risk:** If a laptop fails silently (install never attempts, scheduled task never fires), nobody knows until student complains. No way to probe fleet health without logging into each machine.

**Files:** (Feature not implemented)

**Recommendations:**
- Consider optional telemetry (machine ID, last logon date, last install success/fail)
- Send daily health beacon to optional webhook (with opt-out via manifest setting)
- Implement `--status` command to dump current state (last run, installed programs, logs)

---

### No Manifest Signing or Version Pinning

**Risk:** Teachers/admins cannot verify that manifest changes are approved before they roll out. Student could theoretically MITM and inject malicious programs into the manifest.

**Files:** `manifest/manifest.json` (no signature), `src/Jaminator/Program.cs` line 11 (no version check)

**Recommendations:**
- Implement GPG/ECDSA signing of manifest.json
- Pin minimum tool version and manifest version in code
- Provide signed release tarballs alongside MSI

---

## Scaling Limitations

### Single GitHub Release Tag for All Installers

**Risk:** RELEASES.md documents uploading 1.4 GB of installers to one GitHub release tag (`installers-v1`). Updating a single installer requires re-uploading the entire release.

**Files:** `RELEASES.md`, `manifest/manifest.json`

**Current approach:** Cut new tag (`installers-v2`) and update all manifest URLs at once.

**Recommendations:**
- Consider hosting installers on school file server (SMB) instead of GitHub
- Implement per-installer versioning (e.g., `installers-v1-kodu-1.6.18`, `installers-v1-scratch-3.9.0`)
- Use GitHub Releases as distribution (CDN fallback) but host primary on school infrastructure

---

### No Support for Multiple Manifests per School

**Risk:** Tool fetches a single hardcoded manifest. If Jam Coding serves multiple schools, each needs separate GitHub branch or fork.

**Files:** `src/Jaminator/Program.cs` line 11-12

**Recommendations:**
- Allow manifest URL to be specified via registry or local config file (`C:\ProgramData\Jaminator\config.json`)
- Support multiple manifests with priority/merge logic
- Document per-school configuration procedure

---

## Deployment & Update Risks

### MSI Version Number Locked at 0.1.0

**Risk:** `src/Jaminator/Jaminator.csproj` line 10 hardcodes `<Version>0.1.0</Version>`. MSI build uses this as ProductVersion; all releases are branded as 0.1.0 internally even though tool advertises v0.7.4.

**Files:** `src/Jaminator/Jaminator.csproj` line 10

**Impact:** Windows Add/Remove Programs shows incorrect version; MSI MajorUpgrade detection may fail.

**Recommendations:**
- Update .csproj Version before each release build
- Automate version bumping from git tags (CI/CD)
- Verify ProductVersion in built MSI matches release tag

---

### No Automatic Rollback on Update Failure

**Risk:** SelfUpdater.cs launches msiexec for self-update and exits immediately. If msiexec fails partway, the process is broken and manual repair is needed.

**Files:** `src/Jaminator/Services/SelfUpdater.cs` lines 72-106

**Current mitigation:** msiexec MajorUpgrade is generally atomic; reboot can recover many failures.

**Recommendations:**
- Wait for msiexec to complete instead of exiting immediately
- Detect and report update failures to UI
- Implement "rollback to previous MSI" if health check fails post-update

---

### No Installer Cache Cleanup

**Risk:** Installers are cached at `ProgramData\Jaminator\cache\` forever. Over years, cache can grow to 10+ GB on a laptop.

**Files:** `src/Jaminator/Services/MsiInstaller.cs` lines 86-101

**Recommendations:**
- Implement cache quota (max 5 GB)
- Delete oldest cached installer when quota exceeded
- Periodically validate cached files (re-verify hashes)

---

## Known Limitations

### Offline Lessons Cannot Trigger Installs

**Risk:** If students are in offline lessons, logon-mode skips programs (waiting for network times out). Installs only happen on next login with internet.

**Files:** `src/Jaminator/Services/InternetGate.cs` line 33-64

**Current mitigation:** Daily auto-run at 03:00 (if enabled) ensures installs happen eventually.

**Recommendations:**
- Document offline lesson behavior in manual
- Consider "deferred install" mode (download next internet-available, then run)
- Add manifest setting to control logon network wait timeout (currently hardcoded to 30s)

---

### No Rollout Staging or Canary

**Risk:** Manifest changes affect all 100+ laptops immediately on next logon. If manifest has a typo or broken installer URL, all students are impacted.

**Files:** `manifest/manifest.json`, RELEASES.md

**Current mitigation:** Manifest is reviewed before commit; can be hotfixed and re-committed within minutes.

**Recommendations:**
- Implement manifest versioning with canary rollout (10% of fleet for 1h, then 100%)
- Add feature flag support (e.g., `"enabled": false` on new programs)
- Provide per-laptop manifest override via registry (for testing)

---

*Concerns audit: 2026-05-11*
