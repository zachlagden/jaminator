<!-- GSD:project-start source:PROJECT.md -->
## Project

**Jaminator**

Jaminator is a single-EXE Windows maintenance tool that Jam Coding deploys onto every laptop in its fleet of school-classroom laptops. Configuration lives in a GitHub-hosted JSON manifest (`manifest/manifest.json`), so program installs, cleanup rules, wallpaper, folder layout, and arbitrary PowerShell tasks can be updated for the entire fleet without redeploying the EXE. Two run modes coexist in one binary: a silent login-mode scheduled task that enforces folders + wallpaper at every logon, and an interactive WinForms UI a technician opens to trigger disruptive work (cleanup, program installs, ad-hoc commands).

**Core Value:** A technician can change behaviour on every school laptop by editing one JSON file in GitHub — no MSI redeploy, no per-machine login, no manual rollout.

### Constraints

- **Tech stack**: .NET Framework 4.8 / C# / WinForms / WiX Toolset v4 — locked by existing codebase; this milestone touches only WiX (`installer/installer.wxs`, `installer/UpdateCheck/`) and possibly the build script (`installer/build.ps1`).
- **Target platform**: Windows 7 SP1+ baseline via `net48`, with the actual fleet on Windows 10 / 11 64-bit; install must work via double-click on Win10 and Win11 (the canonical technician deployment path).
- **Deployment**: GitHub Releases — the hotfix ships as a tagged GitHub release with the rebuilt MSI as an asset; downstream `SelfUpdater` will pick it up on first launch of v0.7.4 instances.
- **Build environment**: .NET SDK 8+ + WiX 4 CLI installed locally; build is `installer/build.ps1` on Windows. WSL/Linux is fine for code edits and Git, but the actual MSI rebuild must happen on Windows.
- **Privileges**: install requires local admin (perMachine scope, `Program Files\Jaminator\`, scheduled task registration) — no change planned.
- **Timeline**: ASAP — this is a hotfix release; aim to ship within days.
- **No automated test suite** exists in the repo today, so this milestone's verification step is manual smoke install on a clean target machine.
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Languages
- C# .NET 4.8 - Main application and MSI installer custom actions
- PowerShell - Configuration commands in manifest; skipIf condition evaluation
- WiX XML (WXS) - Windows Installer package definition
- Windows Batch / CMD - Legacy shell support in CommandRunner
- JSON - Manifest schema
## Runtime
- .NET Framework 4.8 (WinExe desktop application)
- Windows XP SP3+ target (via TargetFramework = net48)
- Build: .NET SDK (Modern; uses Sdk="Microsoft.NET.Sdk")
- NuGet via MSBuild/Visual Studio
- No lockfile pattern detected (standard NuGet package restore)
## Frameworks
- .NET Framework 4.8 - Desktop application framework
- Windows Forms (System.Windows.Forms) - UI for `MainForm.cs`
- WiX Toolset v4 - MSI creation via `wix build` command
- WixToolset.Dtf.CustomAction v5.0.2 - MSI custom actions for pre/post-install hooks
- Windows Installer (msiexec) - Standard MSI deployment
- Scheduled Tasks - Windows Task Scheduler integration for login-time auto-run
## Key Dependencies
- Newtonsoft.Json v13.0.3 - JSON deserialization for manifest.json parsing (`Jaminator.csproj`, `Models/Manifest.cs`)
- System.Net.Http - HTTPS downloads from GitHub (manifest, wallpaper, software packages)
- System.IO.Compression - ZIP extraction for Pivot Animator and other portable installs
- Microsoft.NETFramework.ReferenceAssemblies.net48 v1.0.3 - Reference assemblies for targeting .NET 4.8
- WixToolset.Dtf.CustomAction v5.0.2 - Custom action injection for update checks during MSI install (`installer/UpdateCheck/UpdateCheck.csproj`)
## Configuration
- Registry-based configuration (wallpaper path in `HKEY_CURRENT_USER\Control Panel\Desktop`)
- ProgramData cache: `%ProgramData%\Jaminator\cache\manifest.json` - Offline-resilient manifest fallback
- No .env files; no sensitive config detected
- Solution: `Jaminator.sln` (Visual Studio 2017+)
- Projects:
- MSI build: `wix build installer/installer.wxs -d Version=X.Y.Z.0 -d SourceDir=<bin/Release/net48> -o build/Jaminator.msi`
## Platform Requirements
- .NET SDK (any recent version with .NET 4.8 reference assemblies)
- Visual Studio 2017+ or VS Code + .NET CLI
- WiX Toolset v4+ (for `wix` CLI; not required for application-only builds)
- Windows 7 SP1 or later (net48 baseline)
- Administrator privileges required for:
- Output: Portable EXE (`Jaminator.exe`), standalone installer (`Jaminator-Setup.exe`), signed MSI (`Jaminator.msi`)
- Delivery: GitHub Releases (via GitHub API)
- No Kubernetes, no cloud runtime — pure Windows desktop
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Language & Framework
- Desktop application targeting Windows 10+
- WinForms for UI components
- PowerShell 5+ for scripting and build automation
- Target Framework: `net48` (Windows Forms, WinForms)
- LangVersion: `latest` (enables C# 9+ features)
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- JSON serialization via Newtonsoft.Json (v13.0.3)
## Naming Patterns
- PascalCase for C# files: `Logger.cs`, `CommandRunner.cs`, `MainForm.cs`
- PascalCase for directories: `Services/`, `Models/`, `UI/`
- PowerShell scripts: kebab-case: `generate-icon.ps1`, `build.ps1`
- PascalCase for class names: `Logger`, `Manifest`, `ManifestFetcher`
- Sealed classes used extensively: `public sealed class Logger`
- Generic pattern names: `ArchEntry`, `CommandEntry`, `DetectEntry`, `FolderEntry`
- Property names PascalCase with auto-properties: `public string Name { get; set; }`
- PascalCase: `FetchAsync()`, `RunAsync()`, `IsInstalled()`
- Action methods: `Install()`, `Uninstall()`, `RegisterTask()`
- Query methods: `IsInstalled`, `IsRunningFromInstallDir`
- Async methods explicitly suffixed with `Async`: `FetchAsync()`, `RunOneAsync()`, `EvaluateSkipIfAsync()`
- Private fields: camelCase with underscore prefix: `_log`, `_fileLock`, `_manifest`
- Local variables: camelCase: `log`, `cmd`, `psi`, `json`
- Constants: PascalCase: `TaskName`, `DailyTaskName`, `InstallDir`
- PascalCase for enum names and values: `RunMode.Ui`, `RunMode.LoginMode`, `RunMode.Install`
## Code Style
- No editorconfig file detected; conventions inferred from source
- 4-space indentation (standard C#)
- Braces on same line (1TBS style): `if (condition) { code }`
- No unnecessary blank lines between method declarations
- Single line method bodies acceptable: `public void Info(string msg) => Write("INFO", msg);`
- No `.editorconfig` file present
- No StyleCop or Roslyn analyzer configuration file
- No explicit code style enforcement configured
## Import Organization
- No path aliases configured; uses full namespace paths
- Models in `Jaminator.Models` namespace
- Services in `Jaminator.Services` namespace
- UI components in `Jaminator.UI` namespace
## Error Handling
- Broad exception catches with inline comments explaining the rationale
- Example from `Logger.cs`: `catch { /* never let logging failure crash a run */ }`
- Example from `ManifestFetcher.cs`: `try { File.WriteAllText(...); } catch { /* cache write best-effort */ }`
- Errors logged but often non-fatal — operations continue gracefully
- Multiple exception handlers for hierarchical fallback
- Example from `ManifestFetcher.cs` (lines 38-66):
- Headless modes return `int` from `Main()`: 0 for success, 1 for failure
- Example: `Installer.Install(log)` returns 0 on success, 1 on exception
- `InvalidOperationException` for logic failures: deserialization returning null, missing manifest
- Base `Exception` caught when operation non-critical
## Logging
- Public event `OnMessage: Action<string>` for UI subscriptions
- Writes to rotating daily logfiles: `jaminator-YYYYMMDD.log` in `%CommonApplicationData%\Jaminator\logs\`
- Thread-safe via `lock (_fileLock)` object
- Silent failures: `catch { }` block prevents logging failures from crashing the application
- `Info(string msg)` — normal flow
- `Warn(string msg)` — non-fatal issues, non-zero exit codes
- `Error(string msg)` — failure events
- `Error(string msg, Exception ex)` — exception + message combined
- Injected into services: `Logger log` parameter in constructors
- Called at operation boundaries (start of command, exit codes, errors)
- Prefixed indentation for sub-operations: `  | output line` for process stdout/stderr
## Comments
- XML documentation on public types and key private methods
- Inline comments explain *why* a broad exception is caught, not *what* the code does
- Comments above complex logic blocks (PowerShell expression evaluation, version comparison, registry parsing)
- `/// <summary>` blocks on public classes, public methods, public properties
- Example from `State.cs`:
- Used on significant private methods too: `CommandRunner.EvaluateSkipIfAsync()` has XML docs
## Function Design
- Average function 15-40 lines
- Utilities commonly 5-10 lines: `Write()`, `ParseMode()`
- Async operations 50+ lines acceptable: `RunOneAsync()` in `CommandRunner.cs`
- Single responsibility per parameter
- Nullable reference types used: `public void Error(string msg, Exception ex)`
- Configuration passed as immutable data classes: `CommandEntry cmd`
- Exit code integers from headless operations: `Install()` returns `int`
- Tuples for multi-value returns: `FetchAsync()` returns `(Manifest manifest, bool fromCache)`
- Nullable returns explicit: `string?`, `DetectEntry?`
- Async always returns `Task` or `Task<T>`: `async Task RunAsync()`, `async Task<bool> EvaluateSkipIfAsync()`
## Module Design
- Public static classes for utility operations: `Detector`, `Installer`
- Sealed classes for stateful services: `Logger`, `ManifestFetcher`, `CommandRunner`
- No public constructors on static utility classes
- Not used; namespaces are fine-grained per responsibility
- One public class per file (rare exceptions: `Manifest.cs` contains 8 nested classes all related to the manifest schema)
- Namespace per directory: `Jaminator.Services`, `Jaminator.Models`, `Jaminator.UI`
## JSON Configuration
- `[JsonProperty("key")]` attributes on properties to map to snake_case JSON keys
- Example from `Models/Manifest.cs`:
- Defaults used: `string Url = ""`, `List<T> = new()`
- `manifest/manifest.json` in repository root
- Lives in version control; fetched at runtime by every installation
- Schema documented in `docs/manifest-schema.md`
## Security Patterns
- URLs are public (GitHub raw content links)
- Environment variables for ProgramData paths (standard Windows)
- No API keys or auth tokens in source
- Every downloaded file (installer, wallpaper) verified against manifest checksum
- `HashVerifier.cs` service handles validation
- Missing/mismatched hashes cause operation to fail
## Assembly Configuration
- OutputType: `WinExe` (GUI application)
- AssemblyName: `Jaminator`
- RootNamespace: `Jaminator`
- ApplicationIcon: `Jaminator.ico`
- Deterministic: `true` (reproducible builds)
- DebugType: `embedded` (debug info in assembly)
- Company: `Jam Coding`, Product: `Jaminator`
- LangVersion: `latest` (C# 9+ features allowed)
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## System Overview
```text
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
- **Manifest-driven:** All state (programs, commands, cleanup rules, wallpaper) lives in `manifest/manifest.json` at the repo root, fetched from GitHub at every run. No code deployment needed for config changes.
- **Offline-resilient:** `ManifestFetcher` caches the last successful fetch to `ProgramData\Jaminator\cache\manifest.json`, so logon-time runs survive offline classroom periods.
- **Dual-mode execution:** CLI flags determine whether the tool runs with UI, silently (login-mode), run-all non-interactively, or performs install/uninstall. The same codebase, different entry points.
- **Idempotency:** Every action checks state before executing. Programs use `DetectEntry` to skip if already installed. Commands use `skipIf` PowerShell expressions to avoid redundant runs.
- **Least-privilege at logon:** The scheduled task (registered during install) runs `--login-mode`, executing only "login-safe" sections (folders + wallpaper). Disruptive actions (cleanup, installs, arbitrary commands) require the tech to open the UI and select them.
- **Self-updating:** The EXE checks GitHub releases on launch. If a newer version is available, it downloads the MSI and hands off to Windows Installer, which applies the upgrade.
## Layers
- Purpose: Render sections, let tech select what to run, display live progress
- Location: `src/Jaminator/UI/`
- Contains: WinForms MainForm, SectionPanel controls
- Depends on: Services (all of them), Models (Manifest)
- Used by: Entry point `Program.Main()` in non-headless modes
- Purpose: Manage run flow, invoke services, aggregate results, handle errors
- Location: `src/Jaminator/UI/MainForm.cs` (loads manifest, builds runner instances, executes sections)
- Contains: Section selection logic, run-mode guards (e.g., `LoginModeOnly` skips disruptive sections)
- Depends on: Services, Models
- Used by: Main entry point
- Purpose: Encapsulate each maintenance capability (install, cleanup, wallpaper, commands, etc.)
- Location: `src/Jaminator/Services/`
- Contains: CleanupRunner, MsiInstaller, CommandRunner, WallpaperSetter, FolderManager, SelfUpdater, Detector, Downloader, ManifestFetcher, Logger, State, InternetGate, Installer
- Depends on: Models, System APIs (Registry, HTTP, Process, File I/O, P/Invoke)
- Used by: MainForm / orchestration layer
- Purpose: Deserialize manifest JSON, provide type-safe access to config
- Location: `src/Jaminator/Models/Manifest.cs`
- Contains: 11 sealed classes (Manifest, WallpaperEntry, FolderEntry, ProgramEntry, ArchEntry, DetectEntry, CommandEntry, CleanupEntry, BrowserCacheEntry, DocumentsAllowlistEntry, ScheduleEntry)
- Depends on: Newtonsoft.Json for JSON attributes
- Used by: All services, MainForm
- Purpose: Lightweight download wrapper that fetches the latest MSI from GitHub and launches it
- Location: `bootstrap/`
- Contains: `bootstrap/Program.cs` — queries GitHub API, downloads Jaminator.msi, runs msiexec
- Depends on: System.Net.Http
- Used by: End users downloading `Jaminator-Setup.exe` for the first time; always bootstraps to latest
- Purpose: Build the MSI that tech runs or that bootstrap hands to
- Location: `installer/`
- Contains: `installer/installer.wxs` (WiX XML), custom action DLL for version check (UpdateCheck project)
- Depends on: WiX toolset, .NET SDK to build UpdateCheck DLL
- Used by: Deployment; creates registry entries, schedules tasks, copies files
## Data Flow
### Primary Request Path (UI Mode)
### Logon-Time Run (Scheduled Task)
### Run-All Mode (Scripted / Tailscale)
### Self-Install (--install flag)
- **No persistent config state:** The manifest is the source of truth. If you delete `ProgramData\Jaminator\state.json`, the tool recovers by re-fetching the manifest.
- **Caching:** `manifest.json` cached to `ProgramData\Jaminator\cache\manifest.json` for offline resilience.
- **Logs:** Appended to `ProgramData\Jaminator\logs\jaminator-YYYYMMDD.log`; rotated daily by filename.
- **First-run welcome:** `state.json` tracks `WelcomeSeen` flag; MainForm shows EULA once.
- **Last logon run summary:** `StateData.LastLoginRunUtc`, `LastLoginRunOk` stored so UI can show "logon-time cleanup ran successfully 2 hours ago".
## Key Abstractions
- Purpose: Single source of truth for all fleet configuration
- Examples: `manifest/manifest.json` (the actual file), `Manifest` DTO in `src/Jaminator/Models/Manifest.cs`
- Pattern: JSON-serialized, fetched from GitHub, contains nested DTOs (WallpaperEntry, ProgramEntry with per-arch ArchEntry, CommandEntry, CleanupEntry, etc.)
- Used by: Every service; loaded once at startup, passed to runners
- Purpose: A logical group of actions (wallpaper, folders, programs, commands, cleanup) that tech can select in UI
- Examples: "cleanup" (orange), "programs" (blue), "wallpaper" (green)
- Pattern: Derived from Manifest structure; each section maps to a service runner
- Logic: `LoginSafeSections` set determines which run at logon vs. require UI
- Purpose: Decide if a program is already installed, allowing skip
- Examples: Registry key + version check, file existence, AppX package name
- Pattern: Optional on ProgramEntry and ArchEntry; Detector.IsInstalled() evaluates in order
- Impact: Enables idempotency — running the installer twice doesn't reinstall
- Purpose: Platform-specific installer config (x86 vs. x64)
- Fields: kind (msi/exe/zip-extract), url, sha256, args, prerequisites[]
- Pattern: Nested under ProgramEntry; MsiInstaller picks one based on `Environment.Is64BitOperatingSystem`
- Recursion: prerequisites[] is a list of ArchEntry, allowing cascading installs (e.g., XNA before Kodu)
- Purpose: PowerShell boolean expression that makes a command idempotent
- Example: `(Get-ItemProperty -Path 'HKLM:\...\AllowCortana').AllowCortana -eq 0` → skip if already 0
- Pattern: Evaluated by CommandRunner.EvaluateSkipIfAsync(); if true, command skipped
- Fail-open: Any error during evaluation → run the command anyway (conservative)
## Entry Points
- Location: `src/Jaminator/Program.cs`, method `Main(string[] args)`
- Triggers: User double-click (no args), scheduled task (`--login-mode`), scripts (`--run-all`, `--install`), etc.
- Responsibilities:
- Location: `bootstrap/Program.cs`, method `Main()`
- Triggers: End user runs `Jaminator-Setup.exe` for the first time
- Responsibilities:
- Location: `installer/installer.wxs`
- Triggers: Windows Installer during install/upgrade/uninstall
- Responsibilities:
- Trigger: Every user logon
- Action: Run `C:\Program Files\Jaminator\Jaminator.exe --login-mode`
- Result: Headless execution of folders + wallpaper only
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
### Synchronous HTTP Blocking UI
### Storing Secrets in Manifest
## Error Handling
- **ManifestFetcher:** Network failure → fall back to cached manifest; if cache missing or corrupt, throw InvalidOperationException with context (network error + cache status)
- **Services (Cleanup, MsiInstaller, CommandRunner, etc.):** Catch exception, log at WARN or ERROR level, continue to next item. Never re-throw.
- **Downloader:** Hash mismatch → throw; network failure → throw (caller handles)
- **Detector.IsInstalled():** Catches registry access denied, returns false (assume not installed). Catches version parse errors, compares as strings.
- **CommandRunner.EvaluateSkipIfAsync():** PowerShell eval timeout (15s) → return false (run the command, fail-open). Any exception → return false.
- **WinForms UI:** Catches all exceptions in RunSelectedSectionsAsync, logs, sets `_anySectionFailed` flag, displays final status to user
## Cross-Cutting Concerns
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->
## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->



<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
