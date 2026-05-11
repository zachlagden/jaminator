# External Integrations

**Analysis Date:** 2026-05-11

## APIs & External Services

**GitHub API (Releases):**
- Manifest fetch: https://raw.githubusercontent.com/zachlagden/jaminator/main/manifest/manifest.json
  - SDK/Client: `System.Net.Http.HttpClient`
  - Called from: `src/Jaminator/Services/ManifestFetcher.cs:36` (main app)
  - Purpose: Fetch JSON configuration for folders, programs, commands, cleanup rules
  - Cache fallback: `%ProgramData%\Jaminator\cache\manifest.json` for offline-resilient logon-time runs

- Release API: https://api.github.com/repos/zachlagden/jaminator/releases/latest
  - SDK/Client: `System.Net.Http.HttpClient` with User-Agent and Accept headers
  - Called from: 
    - `src/Jaminator/Services/SelfUpdater.cs:34` (self-update check in main app)
    - `bootstrap/Program.cs:18` (bootstrap downloader)
    - `installer/UpdateCheck/UpdateCheckCA.cs:19` (WiX MSI custom action)
  - Purpose: Query latest Jaminator release, download new MSI if newer version exists
  - Headers: `User-Agent: Jaminator/1.0`, `Accept: application/vnd.github+json`
  - Timeout: 60s (SelfUpdater), 5min (bootstrap), 5s (MSI action, fail-open)
  - Auth: None (public repo)

**Asset Downloads (GitHub Releases):**
- Wallpaper image (from manifest): Downloaded via `Downloader.DownloadVerifiedAsync()`
  - Called from: `src/Jaminator/Services/WallpaperSetter.cs:37`
  - Verification: SHA256 hash match required (fail if mismatch)
  - Stored locally: `%PicturesFolder%\jaminator-wallpaper.png`

- Software installer MSIs/EXEs (from manifest entries):
  - Called from: `src/Jaminator/Services/Installer.cs` (installation runner)
  - Examples: Kodu, Scratch Desktop, MakeCode Arcade, Pivot Animator, Construct 2, Minecraft Education
  - Verification: SHA256 hash required for each package
  - Detection: Registry keys, file paths, Appx package names (manifest DetectEntry rules)

## Data Storage

**Databases:**
- None — application is stateless

**File Storage:**
- Local filesystem only:
  - Install directory: `%ProgramFiles%\Jaminator\` (MSI target)
  - Cache directory: `%ProgramData%\Jaminator\cache\` (manifest mirror for offline fallback)
  - Documents: School-specific folder structure per manifest (e.g., `Documents/St Augustines/Year 1`)
  - Wallpaper: `%PicturesFolder%\jaminator-wallpaper.png`
  - Temp: `%TEMP%\Jaminator-*.msi` (self-update artifacts)

**Caching:**
- None (external services) — manifest cache is stored on disk as fallback, not memcached

## Authentication & Identity

**Auth Provider:**
- None — application runs with local user/SYSTEM privileges
- GitHub API: Public endpoint (no token required)
- Registry/scheduled task ops: Elevation via UAC prompt or SYSTEM context during install

## Monitoring & Observability

**Error Tracking:**
- None — no Sentry or external error tracking

**Logs:**
- Console logging: `Logger` class in `src/Jaminator/Services/Logger.cs`
- Output channels:
  - Main UI: Logged to `MainForm` UI panels
  - Headless modes (install/uninstall): Logged to `Console.WriteLine()`
  - MSI custom action: Logged to MSI session log (viewable via `/L*V` flag)
- Example: `Jaminator.msi.log` created by msiexec with `/L*V` argument in `SelfUpdater.ApplyAsync()`
- No structured JSON logging; simple string messages

## CI/CD & Deployment

**Hosting:**
- GitHub Releases (https://github.com/zachlagden/jaminator/releases)

**CI Pipeline:**
- None detected — manual builds and releases
- Build output: `.msi`, `.exe` (bootstrap), `.exe` (main app)

**Delivery:**
- GitHub release assets: `Jaminator.msi`, `Jaminator-Setup.exe`
- Direct end-user download from GitHub release page
- MSI signed: Yes (inferred from production intent, but signing cert not visible in codebase)

## Environment Configuration

**Required env vars:**
- None — application is self-contained

**Secrets location:**
- None — no secrets in config
- GitHub token not required (public repo)
- No API keys or credentials stored

## Webhooks & Callbacks

**Incoming:**
- None

**Outgoing:**
- None

## Windows Registry & OS Integration

**Registry Access:**
- Wallpaper setting: `HKEY_CURRENT_USER\Control Panel\Desktop` (read/write)
  - Called from: `src/Jaminator/Services/WallpaperSetter.cs:60-75`
  - Keys written: `Wallpaper`, `WallpaperStyle`, `TileWallpaper`

- Add/Remove Programs metadata: Various `HKLM\SOFTWARE\...\Uninstall` paths (read-only search)
  - Called from: `src/Jaminator/Services/Installer.cs:74` (find MSI ProductCode)

- GPO/system policies (manifest commands): `HKLM\SOFTWARE\Policies\Microsoft\Windows\...` (write via PowerShell)
  - Examples: Cortana disable, consumer features disable, OneDrive uninstall
  - Paths: Windows Search, CloudContent, OneDrive policies
  - Script runner: `CommandRunner.RunOneAsync()` executes as PowerShell (elevation required)

- Detection rules (manifest programs): Custom registry key detection
  - Example: `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{GUID}`
  - Called from: `src/Jaminator/Services/Detector.cs`

**Scheduled Tasks:**
- Login task: `Jaminator-Login`
  - Created by: `src/Jaminator/Services/Installer.cs:RegisterScheduledTask()`
  - Trigger: User logon (all users)
  - Command: `Jaminator.exe --login-mode`
  - Scope: SYSTEM (runs login-safe cleanup/manifest operations)

- Daily task: `Jaminator-Daily`
  - Created by: Manifest schedule entry (`ScheduleEntry.DailyRunAll`)
  - Trigger: Specified time each day (default 03:00)
  - Command: `Jaminator.exe --run-all`
  - Purpose: Full daily provisioning run

**Win32 APIs:**
- `SystemParametersInfo()` (User32.dll): Set desktop wallpaper
  - Called from: `src/Jaminator/Services/WallpaperSetter.cs:16-17`
  - Action: `SPI_SETDESKWALLPAPER` (0x0014), `SPIF_UPDATEINIFILE`, `SPIF_SENDWININICHANGE`

**Windows Installer (MSI) Integration:**
- MSI package: `Jaminator.msi` (WiX-built)
- Custom actions (pre-install):
  - `CheckForNewerVersion`: GitHub release probe, auto-upgrade if newer available
  - Source: `installer/UpdateCheck/UpdateCheckCA.cs` (WixToolset.Dtf.CustomAction wrapper)
  - Timing: Runs before installation; can trigger download + hand-off to newer MSI

**Application Manifest:**
- File: `src/Jaminator/App.manifest`
- Purpose: Elevation requirements, DPI awareness (inferred from `ApplicationManifest` in `.csproj`)

## Software Package Detection & Installation

**Detection Methods (manifest):**
- Registry key detection: `HKLM\...\Uninstall` lookups
- File path detection: `%ProgramFiles%`, `%LOCALAPPDATA%`, `%ProgramFiles(x86)%` checks
- Appx package detection: `Microsoft.MinecraftEducationEdition` etc. (PowerShell `Get-AppxPackage`)

**Installation Types (manifest):**
- MSI: Run via msiexec with silent args (e.g., `/qn /norestart`)
- EXE: Run directly with installer-specific args (e.g., `/VERYSILENT /SUPPRESSMSGBOXES`)
- ZIP-extract: Extract to target path, create shortcuts

**Pre/Post-Install:**
- Prerequisites: Chain-installed before main app (e.g., XNA Framework before Kodu)
- Shortcut creation: Desktop and Start Menu shortcuts for apps
- Detection: Skip if already installed (registry/file/Appx checks)

---

*Integration audit: 2026-05-11*
