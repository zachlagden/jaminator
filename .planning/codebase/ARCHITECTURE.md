<!-- refreshed: 2026-05-11 -->
# Architecture

**Analysis Date:** 2026-05-11

## System Overview

Jaminator is a self-contained Windows laptop maintenance tool built as a single EXE that fetches its configuration from a GitHub-hosted JSON manifest and executes login-safe or full maintenance tasks on demand. The architecture is **control-plane-as-manifest**: all fleet configuration lives in `manifest/manifest.json`, not in code, allowing zero-deployment updates.

```text
┌──────────────────────────────────────────────────────────────────┐
│                         UI Layer (WinForms)                       │
│  `src/Jaminator/UI/MainForm.cs` — Section selection + progress   │
│  `src/Jaminator/UI/SectionPanel.cs` — Visual section card        │
└──────────────────────────────┬───────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│                    Orchestration Layer                            │
│  MainForm triggers runners based on CLI mode or user selection    │
│  Runners: CleanupRunner, MsiInstaller, CommandRunner, etc.       │
└──────────────────────────────┬───────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│                  Core Services Layer                              │
│  `src/Jaminator/Services/` — domain logic                        │
│  ManifestFetcher → Downloader → Detector → Installers            │
│  CleanupRunner, WallpaperSetter, CommandRunner, FolderManager    │
│  SelfUpdater, Logger, State, InternetGate                        │
└──────────────────────────────┬───────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Data Model Layer                             │
│  `src/Jaminator/Models/Manifest.cs` — Manifest DTOs              │
│  Manifest, WallpaperEntry, FolderEntry, ProgramEntry,            │
│  ArchEntry, CommandEntry, CleanupEntry, etc.                     │
└──────────────────────────────┬───────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│             External & Persistent Storage                         │
│  GitHub API (manifest.json, releases, MSI downloads)             │
│  Windows Registry (program detection, uninstall)                 │
│  ProgramData\Jaminator\ (cache, logs, state.json)                │
│  File system (Programs, Documents, wallpaper.png)                │
└──────────────────────────────────────────────────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| **MainForm** | Fetch manifest, render section panels, orchestrate execution | `src/Jaminator/UI/MainForm.cs` |
| **SectionPanel** | Visual card for a single maintenance section (cleanup, programs, etc.) | `src/Jaminator/UI/SectionPanel.cs` |
| **ManifestFetcher** | HTTP fetch from GitHub with on-disk cache fallback for offline logons | `src/Jaminator/Services/ManifestFetcher.cs` |
| **CleanupRunner** | Wipe temp paths, empty recycle bin, clear browser cache, quarantine docs | `src/Jaminator/Services/CleanupRunner.cs` |
| **MsiInstaller** | Download and execute MSI/EXE installers, handle prerequisites, skip if already installed | `src/Jaminator/Services/MsiInstaller.cs` |
| **CommandRunner** | Execute PowerShell / CMD scripts from manifest, evaluate skipIf conditions | `src/Jaminator/Services/CommandRunner.cs` |
| **WallpaperSetter** | Download + apply canonical wallpaper, enforce enforcement flag | `src/Jaminator/Services/WallpaperSetter.cs` |
| **FolderManager** | Create folder structure under Documents/ per manifest | `src/Jaminator/Services/FolderManager.cs` |
| **SelfUpdater** | Check GitHub releases for newer Jaminator version, download + hand to msiexec | `src/Jaminator/Services/SelfUpdater.cs` |
| **Detector** | Detect installed programs via registry, file existence, version, or AppX package | `src/Jaminator/Services/Detector.cs` |
| **Downloader** | HTTP download with hash verification | `src/Jaminator/Services/Downloader.cs` |
| **Logger** | Thread-safe logging to `ProgramData\Jaminator\logs\jaminator-YYYYMMDD.log` | `src/Jaminator/Services/Logger.cs` |
| **State** | Persist welcome-seen flag and last login-run summary to `ProgramData\Jaminator\state.json` | `src/Jaminator/Services/State.cs` |
| **InternetGate** | Check network availability before runs; wait up to configurable timeout | `src/Jaminator/Services/InternetGate.cs` |
| **Manifest (Model)** | JSON DTOs for all config sections: wallpaper, folders, programs, commands, cleanup | `src/Jaminator/Models/Manifest.cs` |
| **Installer** | Self-install to `C:\Program Files\Jaminator\`, register scheduled task, create shortcuts | `src/Jaminator/Services/Installer.cs` |
| **Polyfills** | .NET 4.8 compatibility shims (e.g., string nullability) | `src/Jaminator/Polyfills.cs` |

## Pattern Overview

**Overall:** This is a **command-driven, pull-based fleet maintenance tool** with **headless + interactive dual modes**.

**Key Characteristics:**
- **Manifest-driven:** All state (programs, commands, cleanup rules, wallpaper) lives in `manifest/manifest.json` at the repo root, fetched from GitHub at every run. No code deployment needed for config changes.
- **Offline-resilient:** `ManifestFetcher` caches the last successful fetch to `ProgramData\Jaminator\cache\manifest.json`, so logon-time runs survive offline classroom periods.
- **Dual-mode execution:** CLI flags determine whether the tool runs with UI, silently (login-mode), run-all non-interactively, or performs install/uninstall. The same codebase, different entry points.
- **Idempotency:** Every action checks state before executing. Programs use `DetectEntry` to skip if already installed. Commands use `skipIf` PowerShell expressions to avoid redundant runs.
- **Least-privilege at logon:** The scheduled task (registered during install) runs `--login-mode`, executing only "login-safe" sections (folders + wallpaper). Disruptive actions (cleanup, installs, arbitrary commands) require the tech to open the UI and select them.
- **Self-updating:** The EXE checks GitHub releases on launch. If a newer version is available, it downloads the MSI and hands off to Windows Installer, which applies the upgrade.

## Layers

**Presentation (UI):**
- Purpose: Render sections, let tech select what to run, display live progress
- Location: `src/Jaminator/UI/`
- Contains: WinForms MainForm, SectionPanel controls
- Depends on: Services (all of them), Models (Manifest)
- Used by: Entry point `Program.Main()` in non-headless modes

**Orchestration:**
- Purpose: Manage run flow, invoke services, aggregate results, handle errors
- Location: `src/Jaminator/UI/MainForm.cs` (loads manifest, builds runner instances, executes sections)
- Contains: Section selection logic, run-mode guards (e.g., `LoginModeOnly` skips disruptive sections)
- Depends on: Services, Models
- Used by: Main entry point

**Services (Domain Logic):**
- Purpose: Encapsulate each maintenance capability (install, cleanup, wallpaper, commands, etc.)
- Location: `src/Jaminator/Services/`
- Contains: CleanupRunner, MsiInstaller, CommandRunner, WallpaperSetter, FolderManager, SelfUpdater, Detector, Downloader, ManifestFetcher, Logger, State, InternetGate, Installer
- Depends on: Models, System APIs (Registry, HTTP, Process, File I/O, P/Invoke)
- Used by: MainForm / orchestration layer

**Data Model:**
- Purpose: Deserialize manifest JSON, provide type-safe access to config
- Location: `src/Jaminator/Models/Manifest.cs`
- Contains: 11 sealed classes (Manifest, WallpaperEntry, FolderEntry, ProgramEntry, ArchEntry, DetectEntry, CommandEntry, CleanupEntry, BrowserCacheEntry, DocumentsAllowlistEntry, ScheduleEntry)
- Depends on: Newtonsoft.Json for JSON attributes
- Used by: All services, MainForm

**Bootstrap (Self-Updater Stub):**
- Purpose: Lightweight download wrapper that fetches the latest MSI from GitHub and launches it
- Location: `bootstrap/`
- Contains: `bootstrap/Program.cs` — queries GitHub API, downloads Jaminator.msi, runs msiexec
- Depends on: System.Net.Http
- Used by: End users downloading `Jaminator-Setup.exe` for the first time; always bootstraps to latest

**Installer (WiX):**
- Purpose: Build the MSI that tech runs or that bootstrap hands to
- Location: `installer/`
- Contains: `installer/installer.wxs` (WiX XML), custom action DLL for version check (UpdateCheck project)
- Depends on: WiX toolset, .NET SDK to build UpdateCheck DLL
- Used by: Deployment; creates registry entries, schedules tasks, copies files

## Data Flow

### Primary Request Path (UI Mode)

1. **User double-clicks Jaminator.exe** → `Program.Main()` in `src/Jaminator/Program.cs`
2. **Detect CLI mode** → `Program.ParseMode()` returns `RunMode.Ui` (default)
3. **Initialize WinForms** → `Application.Run(new UI.MainForm())`
4. **MainForm.OnLoad()** → `ManifestFetcher.FetchAsync()` queries GitHub, caches result
5. **Render sections** → Loop manifest entries, create `SectionPanel` for each, colorize per type (cleanup=orange, wallpaper/folders=green, programs=blue, commands=red)
6. **Tech selects sections + clicks "Run"** → `MainForm.RunSelectedSectionsAsync()`
7. **Per section:**
   - **Folders:** `FolderManager.EnsureCreated()` → create Documents subfolders
   - **Wallpaper:** `WallpaperSetter.ApplyAsync()` → download, verify hash, set registry
   - **Programs:** `MsiInstaller.InstallAllAsync()` → for each program: detect if installed, download (with prerequisites first), execute, log result
   - **Commands:** `CommandRunner.RunAsync()` → evaluate skipIf, run PowerShell/CMD script, log output
   - **Cleanup:** `CleanupRunner.RunAsync()` → wipe temp, empty recycle, clear browser cache, quarantine Documents (optional allowlist)
8. **Self-update check:** `SelfUpdater.CheckAsync()` on startup; if newer exists, `SelfUpdater.ApplyAsync()` downloads MSI, spawns msiexec, exits
9. **Sections complete** → Log summary, update `State` with run timestamp
10. **Exit or stay open** → Depends on mode; UI mode waits for user, headless modes exit

### Logon-Time Run (Scheduled Task)

1. **User logs in** → Windows Task Scheduler invokes `C:\Program Files\Jaminator\Jaminator.exe --login-mode`
2. **Program.Main()** → `ParseMode()` returns `RunMode.LoginMode`
3. **Headless setup** → `Program.RunAllOnStart = true`, `Program.ExitAfterRun = true`
4. **Load manifest** → `ManifestFetcher.FetchAsync()` with fallback to cached copy
5. **Show invisible form** → MainForm initializes with `ShowInTaskbar=false`, `Opacity=0`, `WindowState=Minimized` (user never sees it)
6. **Run only login-safe sections** → Loop manifest; for each section, check `LoginSafeSections` set (contains "folders" and "wallpaper" only)
7. **Execute:** FolderManager, WallpaperSetter only. Skip cleanup, programs, commands.
8. **Persist state** → Save `StateData` with run timestamp and success flag
9. **Exit** → Process terminates; next user logon will repeat

### Run-All Mode (Scripted / Tailscale)

1. **Script invokes** `Jaminator.exe --run-all`
2. **Program.Main()** → `ParseMode()` returns `RunMode.RunAll`
3. **Same as logon, but:** All sections run, not just login-safe ones. Form is hidden but all actions execute.
4. **Exit with code 0 on success, 1 on any section failure**

### Self-Install (--install flag)

1. **Tech or script runs** `Jaminator.exe --install` (from e.g. a USB stick or unpacked temp)
2. **Program.Main()** → `ParseMode()` returns `RunMode.Install`
3. **Installer.Install()** logic:
   - Create `C:\Program Files\Jaminator\`
   - Copy all files from current directory to install dir
   - Call `Installer.RegisterScheduledTask()` to register logon task
   - Call `CreateStartMenuShortcut()`
   - Log summary
4. **Return exit code 0 on success**

**State Management:**
- **No persistent config state:** The manifest is the source of truth. If you delete `ProgramData\Jaminator\state.json`, the tool recovers by re-fetching the manifest.
- **Caching:** `manifest.json` cached to `ProgramData\Jaminator\cache\manifest.json` for offline resilience.
- **Logs:** Appended to `ProgramData\Jaminator\logs\jaminator-YYYYMMDD.log`; rotated daily by filename.
- **First-run welcome:** `state.json` tracks `WelcomeSeen` flag; MainForm shows EULA once.
- **Last logon run summary:** `StateData.LastLoginRunUtc`, `LastLoginRunOk` stored so UI can show "logon-time cleanup ran successfully 2 hours ago".

## Key Abstractions

**Manifest:**
- Purpose: Single source of truth for all fleet configuration
- Examples: `manifest/manifest.json` (the actual file), `Manifest` DTO in `src/Jaminator/Models/Manifest.cs`
- Pattern: JSON-serialized, fetched from GitHub, contains nested DTOs (WallpaperEntry, ProgramEntry with per-arch ArchEntry, CommandEntry, CleanupEntry, etc.)
- Used by: Every service; loaded once at startup, passed to runners

**Section:**
- Purpose: A logical group of actions (wallpaper, folders, programs, commands, cleanup) that tech can select in UI
- Examples: "cleanup" (orange), "programs" (blue), "wallpaper" (green)
- Pattern: Derived from Manifest structure; each section maps to a service runner
- Logic: `LoginSafeSections` set determines which run at logon vs. require UI

**DetectEntry:**
- Purpose: Decide if a program is already installed, allowing skip
- Examples: Registry key + version check, file existence, AppX package name
- Pattern: Optional on ProgramEntry and ArchEntry; Detector.IsInstalled() evaluates in order
- Impact: Enables idempotency — running the installer twice doesn't reinstall

**ArchEntry:**
- Purpose: Platform-specific installer config (x86 vs. x64)
- Fields: kind (msi/exe/zip-extract), url, sha256, args, prerequisites[]
- Pattern: Nested under ProgramEntry; MsiInstaller picks one based on `Environment.Is64BitOperatingSystem`
- Recursion: prerequisites[] is a list of ArchEntry, allowing cascading installs (e.g., XNA before Kodu)

**CommandEntry.SkipIf:**
- Purpose: PowerShell boolean expression that makes a command idempotent
- Example: `(Get-ItemProperty -Path 'HKLM:\...\AllowCortana').AllowCortana -eq 0` → skip if already 0
- Pattern: Evaluated by CommandRunner.EvaluateSkipIfAsync(); if true, command skipped
- Fail-open: Any error during evaluation → run the command anyway (conservative)

## Entry Points

**Jaminator.exe (Main Application):**
- Location: `src/Jaminator/Program.cs`, method `Main(string[] args)`
- Triggers: User double-click (no args), scheduled task (`--login-mode`), scripts (`--run-all`, `--install`), etc.
- Responsibilities:
  - Parse CLI mode from args (UI, LoginMode, RunAll, Install, Uninstall, RegisterTask, UnregisterTask)
  - For headless ops (install/uninstall), run synchronously and exit with code
  - For UI mode, launch WinForms application loop
  - Set static flags (`RunAllOnStart`, `ExitAfterRun`, `LoginModeOnly`) for MainForm to read

**Bootstrap.exe (Setup Stub):**
- Location: `bootstrap/Program.cs`, method `Main()`
- Triggers: End user runs `Jaminator-Setup.exe` for the first time
- Responsibilities:
  - Query GitHub API for latest release
  - Extract MSI download URL and version
  - Download MSI to temp
  - Launch msiexec to run the MSI
  - Exit

**Installer.wxs (MSI Custom Actions):**
- Location: `installer/installer.wxs`
- Triggers: Windows Installer during install/upgrade/uninstall
- Responsibilities:
  - Extract files to `C:\Program Files\Jaminator\`
  - Create Start Menu and (optionally) desktop shortcuts
  - Invoke UpdateCheck custom action DLL to warn user if a newer version exists
  - Register scheduled task via `Jaminator.exe --register-task`
  - On uninstall, reverse the above

**Scheduled Task (Jaminator-Login):**
- Trigger: Every user logon
- Action: Run `C:\Program Files\Jaminator\Jaminator.exe --login-mode`
- Result: Headless execution of folders + wallpaper only

**Scheduled Task (Jaminator-Daily, optional):**
- Trigger: Daily at time specified in `manifest.schedule.dailyRunAll`
- Action: Run `C:\Program Files\Jaminator\Jaminator.exe --run-all`
- Result: Headless execution of all sections (if internet available)

## Architectural Constraints

- **Threading:** Single-threaded event loop (WinForms on UI thread); services use `async/await` with `Task.Run()` to avoid blocking UI during long operations (downloads, installs, PowerShell). No thread pool or concurrent section execution.
- **Global state:** Logger instance created per-run; Manifest fetched once per app start and passed to services. No module-level singletons beyond Logger. State.json is the only persistent key/value store.
- **Circular imports:** None detected. Dependency graph is acyclic: UI → Services → Models → System APIs.
- **Process elevation:** Entire EXE runs elevated (via UAC prompt or scheduled task as SYSTEM). No privilege boundaries within the process. No subprocess elevation calls needed.
- **Network assumptions:** Assumes internet availability for manifest fetch and MSI downloads unless offline-cached manifest is used. InternetGate probes connectivity and waits up to `schedule.maxNetworkWaitMinutes`.
- **.NET Runtime:** Targets .NET Framework 4.8 (not .NET Core). Compiled with SDK 8+ but produces a .NET 4.8-compatible binary. No external runtime installation required on Windows 10+.
- **Registry access:** Reads HKLM and HKCU for program detection (DisplayVersion, Version). No writes except scheduled task registration. Detector falls back gracefully on access denied.
- **File I/O:** Reads/writes to `ProgramData\Jaminator\` (for cache, logs, state) and user Documents. No temp file cleanup on exit — CleanupRunner handles deliberate temp wipes. Assumes `C:\Program Files\` is writable during install (UAC/SYSTEM).

## Anti-Patterns

### Hardcoded Program Lists in Code

**What happens:** Program installations used to be hardcoded in C# enums or if-chains in CleanupRunner or MainForm.

**Why it's wrong:** Every new program or config change required a recompile, redeploy to all machines, and new MSI release. The whole point of Jaminator is zero-code-deployment fleet updates.

**Do this instead:** Define all programs, cleanup rules, commands, and folder structures in `manifest/manifest.json`. Edit the JSON, commit, and every machine picks up the change next time it runs. See `manifest/manifest.json` and `src/Jaminator/Models/Manifest.cs` for the current data model.

### Synchronous HTTP Blocking UI

**What happens:** If `ManifestFetcher.FetchAsync()` were called synchronously on the UI thread, a slow GitHub connection or offline network would freeze the UI until timeout.

**Why it's wrong:** Users see "Not Responding" in Task Manager. On logon, the user can't use their machine until the timeout expires.

**Do this instead:** All network I/O is `async`, executed via `Task.Run()` on a thread pool thread. See `MainForm.OnLoad()` and `MainForm.RunSelectedSectionsAsync()` for examples. Never block the UI thread.

### Storing Secrets in Manifest

**What happens:** If `manifest.json` ever contained API keys, credentials, or auth tokens, they'd be in a public GitHub repo.

**Why it's wrong:** Security incident. Credentials are immutable once public. Revocation and rotation are painful.

**Do this instead:** Manifest contains only public URLs, checksums, and script bodies. Scripts can read secrets from system sources (environment, registry, local file with restrictive ACLs). See security model in README.md.

## Error Handling

**Strategy:** Fail-open with logging. Services catch exceptions, log them, and let execution continue. A single program install failure doesn't abort the rest of the fleet install. Script evaluation errors don't halt the command runner.

**Patterns:**
- **ManifestFetcher:** Network failure → fall back to cached manifest; if cache missing or corrupt, throw InvalidOperationException with context (network error + cache status)
- **Services (Cleanup, MsiInstaller, CommandRunner, etc.):** Catch exception, log at WARN or ERROR level, continue to next item. Never re-throw.
- **Downloader:** Hash mismatch → throw; network failure → throw (caller handles)
- **Detector.IsInstalled():** Catches registry access denied, returns false (assume not installed). Catches version parse errors, compares as strings.
- **CommandRunner.EvaluateSkipIfAsync():** PowerShell eval timeout (15s) → return false (run the command, fail-open). Any exception → return false.
- **WinForms UI:** Catches all exceptions in RunSelectedSectionsAsync, logs, sets `_anySectionFailed` flag, displays final status to user

## Cross-Cutting Concerns

**Logging:** All services accept a Logger instance in ctor. Logger writes timestamped [LEVEL] lines to daily logfile in `ProgramData\Jaminator\logs\` and raises `OnMessage` event for UI to display. Thread-safe via lock.

**Validation:** Manifest deserialization validates JSON structure. DetectEntry has null-coalescing fallbacks. No explicit user input validation (manifest is the input; it's curated by repo collaborators, not users).

**Authentication:** None. Manifest is public; trust model is "write access to the repo = admin access to all machines." No OAuth, no user auth. Assumes tech running the tool is authorized (elevated process context).

---

*Architecture analysis: 2026-05-11*
