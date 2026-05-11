# Testing Patterns

**Analysis Date:** 2026-05-11

## Test Framework

**Status:** No automated test framework present.

**Reason:** This is a production Windows client application (WinForms) with tightly coupled OS/registry/file system interactions. Manual verification and release-stage testing replace unit tests.

## Manual Verification Approach

**Release Testing:**
- Manual smoke-test on Windows 10 and Windows 11 (32-bit and 64-bit variants)
- Test modes triggered via CLI flags documented in `README.md`:
  - `Jaminator.exe --install` — self-install flow
  - `Jaminator.exe --login-mode` — login-safe headless mode (wallpaper, folders)
  - `Jaminator.exe --run-all` — full headless mode (all sections)
  - `Jaminator.exe` (no args) — opens full interactive UI

**Release Notes (`RELEASES.md`):**
Documents the manual validation process for installer uploads:
1. Upload new installers to GitHub Releases under `installers-v<N>` tag
2. Update SHA256 hashes in `manifest/manifest.json`
3. Commit manifest change
4. Tag release: `git tag -s vX.Y.Z -m "..."` and push
5. CI workflow (release.yml) automatically builds MSI and attaches to GitHub Release

## CI/CD Pipeline

**Location:** `.github/workflows/release.yml`

**Trigger:** Any git tag matching `v*` (e.g., `v0.7.4`)

**Pipeline Steps:**
1. Checkout code
2. Setup .NET SDK 8.0.x
3. `dotnet build Jaminator.sln -c Release` — restore and compile all projects
4. Install WiX 5 toolset globally
5. Run `./installer/build.ps1` — PowerShell script to build MSI
6. Verify MSI exists and report size
7. Create GitHub Release with generated release notes
8. Attach `build/Jaminator.msi` as release asset

**Build Artifact:** 
- MSI output: `build/Jaminator.msi`
- Runnable standalone installer for deployment to classroom laptops

## Build Automation

**Build Script:** `installer/build.ps1`

**Responsibilities:**
1. Extracts tool version from `src/Jaminator/Program.cs` (single source of truth)
2. Ensures EXE is built at `src/Jaminator/bin/Release/net48/Jaminator.exe`
3. Ensures UpdateCheck custom action DLL is built: `installer/UpdateCheck/bin/Release/net48/UpdateCheckCA.CA.dll`
4. Invokes WiX: `wix build installer/installer.wxs`
5. Outputs MSI to `build/Jaminator.msi`

**Version Single-Source-of-Truth:**
- Tool version defined in `src/Jaminator/Program.cs`: `public const string ToolVersion = "0.7.4"`
- Build script parses this via regex and feeds to WiX for MSI versioning
- `Program.cs` version is referenced by self-update check (MSI CA compares current version against manifest)

## Testing Scenarios

While no automated tests exist, the manifest-driven architecture allows high-confidence testing:

**Manifest Validation:**
- `manifest/manifest.json` is the single source of truth for all actions
- Changes to manifest are deployed to all laptops on next run
- Laptop pulls manifest at startup; each section (folders, cleanup, programs, commands, wallpaper) can be toggled on/off in UI
- Manual inspection of manifest schema: `docs/manifest-schema.md`

**Smoke Test Checklist (Manual):**
1. **Install flow:** `Jaminator.exe --install` on a fresh system
   - Files copied to `C:\Program Files\Jaminator\`
   - Scheduled task registered: `Jaminator-Login` and `Jaminator-Daily`
   - Start Menu shortcut created
   - Exit code 0 on success

2. **Login mode (headless):** Simulate user logon
   - Only "login-safe" sections run: wallpaper, folders
   - No UI shown
   - Output logged to `C:\ProgramData\Jaminator\logs\jaminator-YYYYMMDD.log`
   - Exit code reflects success/failure

3. **Interactive UI:** `Jaminator.exe` (no args)
   - Fetches manifest from GitHub
   - Displays sections with color-coded descriptions
   - Tech can select sections and click Run
   - Output streams to UI + log file simultaneously

4. **Full run mode:** `Jaminator.exe --run-all`
   - Runs all sections (cleanup, programs, commands, wallpaper, folders) without UI
   - Useful for scripted deployment or Tailscale-driven setup
   - Exit code 0 if no failures, 1 if any section errored

5. **Manifest cache fallback:**
   - Disconnect network, restart application
   - Should load cached manifest from last successful pull
   - Log should show `fromCache: true` in diagnostics

6. **SHA256 verification:**
   - Tamper with installer file on disk, re-run install
   - Application should reject mismatched hash
   - Log should show hash verification failure

## Test Data / Fixtures

**Live Test Manifest:**
- `manifest/manifest.json` in repo — this IS the test data
- Contains real educational software: Kodu, Scratch, MakeCode Arcade, Minecraft Education, etc.
- Also used for development: developers deploy to personal devices using this manifest

**Installer Staging:**
- `installers-staging/` directory (locally, git-ignored)
- Contains the actual installer executables and ZIPs
- Uploaded to GitHub Releases under `installers-v<N>` tags
- Downloaded by clients at runtime and verified against SHA256

**Log Output Location:**
- `C:\ProgramData\Jaminator\logs\jaminator-YYYYMMDD.log` — one file per day
- Application creates directory automatically if missing
- Useful for debugging post-deployment issues

## Error Scenarios (No Formal Tests)

**Common Error Paths:**
1. Network unavailable during fetch
   - Code: `ManifestFetcher.cs` lines 38-66 — fallback to cached manifest
   - Logged: network error + fallback to cache details
   - Return value: `(Manifest manifest, fromCache: true)` tuple signals cache use

2. Process exit code non-zero
   - Code: `CommandRunner.cs` — logs exit code from every shell invocation
   - Logged: `[HH:mm:ss] [WARN]   -> exit N`
   - Continues with next command; sets `_anySectionFailed` flag in UI

3. Registry key missing during detection
   - Code: `Detector.cs` — returns false if registry key doesn't exist
   - Logic: fail-open — absence of detection = "not installed, run installer"
   - Prevents false negatives on skipped installs

4. File I/O during logging fails
   - Code: `Logger.cs` lines 29-36 — catch-all exception handler
   - Effect: logging failure is silent; application continues
   - Policy: "never let logging failure crash a run"

## Coverage Assessment

**Untested Areas:**
- Full WinForms UI interaction (button clicks, section toggles, form lifecycle)
- Custom MSI actions (custom action DLL in `UpdateCheck.csproj`)
- Wallpaper setting via GDI+ P/Invoke
- Registry operations on systems with restricted group policy
- Windows Scheduled Task management (register/unregister)

**Risk Assessment:**
- **High:** Scheduled task registration (runs in background at every logon; affects all users)
- **Medium:** Manifest parsing (schema changes could break all deployments)
- **Medium:** File/registry permission errors (customer-specific OS policies)
- **Low:** Individual command script failures (logged and continue; UI shows failure)

**Manual Validation Required:**
1. Test on both Windows 10 and Windows 11
2. Test on both 32-bit and 64-bit systems
3. Test fresh install vs. upgrade from previous version
4. Test network-offline scenario (manifest cache fallback)
5. Test with multiple user accounts on same machine
6. Test scheduled task execution at actual user logon
7. Verify log rotation (24-hour boundary)

## Development Testing

**Local Build + Run:**
```powershell
# Build main application
dotnet build src/Jaminator -c Release

# Run UI locally (uses live manifest from GitHub)
src/Jaminator/bin/Release/net48/Jaminator.exe

# Run headless
src/Jaminator/bin/Release/net48/Jaminator.exe --login-mode

# Build and run installer
./installer/build.ps1
build/Jaminator.msi
```

**Debugging:**
- Visual Studio debugger can attach to running process
- Log file at `C:\ProgramData\Jaminator\logs\jaminator-YYYYMMDD.log` available real-time
- UI streams logs to console via `Logger.OnMessage` event subscriptions

## Version Management

**Release Workflow:**
1. Update `Program.ToolVersion = "X.Y.Z"` in `src/Jaminator/Program.cs`
2. Commit: `git commit -m "chore(version): bump to X.Y.Z"`
3. Tag: `git tag -s vX.Y.Z -m "Release notes here"`
4. Push: `git push && git push --tags`
5. GitHub Actions builds MSI and attaches to auto-created Release
6. All laptops with installed Jaminator auto-check for newer version on next launch

**Self-Update Mechanism:**
- `SelfUpdater.cs` checks GitHub releases API on startup
- If newer version exists, downloads MSI and runs Windows Installer
- Installer custom action (`UpdateCheckCA`) handles upgrade logic
- Restarts application after MSI completes

---

*Testing analysis: 2026-05-11*
