# Codebase Structure

**Analysis Date:** 2026-05-11

## Directory Layout

```
jaminator/
├── .github/                    # GitHub Actions workflows (if any)
├── .planning/                  # Analysis & planning docs (created by GSD)
│   └── codebase/               # This directory (ARCHITECTURE.md, STRUCTURE.md, etc.)
├── .remember/                  # Internal LLM memory logs
├── assets/                     # Static files
│   └── wallpaper.png           # Canonical wallpaper image (4.1 MB)
├── bootstrap/                  # Setup stub (Jaminator-Setup.exe builder)
│   ├── Bootstrap.csproj        # Project file
│   ├── App.manifest            # UAC manifest (requestedExecutionLevel=requireAdministrator)
│   └── Program.cs              # Single-file bootstrapper; fetches latest MSI from GitHub
├── docs/                       # Documentation
│   └── manifest-schema.md      # Manifest.json structure reference
├── installer/                  # MSI installer builder
│   ├── UpdateCheck/            # Custom action DLL for version check
│   │   └── UpdateCheck.csproj
│   ├── build.ps1               # PowerShell script to build MSI (requires WiX SDK)
│   ├── EULA.rtf                # License agreement shown in installer
│   └── installer.wxs           # WiX source (defines MSI structure, features, custom actions)
├── manifest/                   # Configuration (the "control plane")
│   └── manifest.json           # Live config: folders, programs, commands, cleanup, wallpaper
├── scripts/                    # Utility scripts
│   └── generate-icon.ps1       # PowerShell script to create Jaminator.ico from PNG
├── src/                        # Main application source
│   └── Jaminator/              # C# .NET Framework 4.8 project
│       ├── Jaminator.csproj    # Project file
│       ├── Program.cs          # Entry point; CLI mode detection (UI/LoginMode/RunAll/Install/Uninstall)
│       ├── Polyfills.cs        # .NET 4.8 compatibility shims
│       ├── App.manifest        # UAC manifest (requireAdministrator)
│       ├── Jaminator.ico       # Application icon (72 KB)
│       ├── Models/             # Data Transfer Objects (JSON deserialize targets)
│       │   └── Manifest.cs     # 11 sealed classes: Manifest, WallpaperEntry, FolderEntry, ProgramEntry, ArchEntry, DetectEntry, CommandEntry, CleanupEntry, BrowserCacheEntry, DocumentsAllowlistEntry, ScheduleEntry
│       ├── Services/           # Domain logic; each handles one capability
│       │   ├── CleanupRunner.cs        # Wipe temp, empty recycle, clear browser cache, quarantine documents
│       │   ├── CommandRunner.cs        # Execute PowerShell/CMD scripts; evaluate skipIf conditions
│       │   ├── Detector.cs             # Check if program is installed (registry, file, version, AppX)
│       │   ├── Downloader.cs           # HTTP download with hash verification
│       │   ├── FolderManager.cs        # Create folder structure under Documents/
│       │   ├── HashVerifier.cs         # SHA-256 verification utility
│       │   ├── Installer.cs            # Self-install to ProgramFiles, register task, create shortcuts
│       │   ├── InternetGate.cs         # Probe network availability, wait with timeout
│       │   ├── Logger.cs               # Thread-safe file+event logging
│       │   ├── ManifestFetcher.cs      # HTTP fetch from GitHub with offline cache fallback
│       │   ├── MsiInstaller.cs         # Download & execute MSI/EXE/ZIP with prerequisites
│       │   ├── SelfUpdater.cs          # Check GitHub releases, download newer MSI, hand to msiexec
│       │   ├── State.cs                # Persist welcome-seen flag and last logon run info to ProgramData
│       │   └── WallpaperSetter.cs      # Download, verify, apply canonical wallpaper
│       └── UI/                  # WinForms user interface
│           ├── MainForm.cs             # Main window; loads manifest, renders sections, orchestrates execution
│           ├── MainForm.Designer.cs    # Designer-generated form controls
│           └── SectionPanel.cs         # Visual card for each section (wallpaper, folders, programs, etc.)
├── Jaminator.sln               # Visual Studio solution file (references 3 projects)
├── README.md                   # High-level overview (how it works, CLI modes, security model)
├── RELEASES.md                 # Version history and release notes
├── LICENSE                     # License text
└── .gitignore                  # Standard exclusions (bin/, obj/, *.user, etc.)
```

## Directory Purposes

**assets/**
- Purpose: Static files bundled or referenced by the app
- Contains: wallpaper.png (the canonical Jam Coding wallpaper)
- Key files: `wallpaper.png` (embedded in manifest as URL, but also stored here for source control)
- Notes: Image is 4.1 MB; served from GitHub raw content URL in manifest.json

**bootstrap/**
- Purpose: Lightweight stub EXE that tech runs on first install; always downloads and runs the latest MSI
- Contains: Single C# console app that queries GitHub releases API and launches msiexec
- Key files: `Program.cs` (implementation), `Bootstrap.csproj`, `App.manifest` (UAC)
- Why separate: Stays small and never needs updating; always points to latest release
- Output: `Jaminator-Setup.exe` (the user-facing installer stub)

**docs/**
- Purpose: User and developer documentation
- Contains: manifest-schema.md (reference for all JSON fields)
- Key files: `manifest-schema.md` (describes every property of Manifest.json)

**installer/**
- Purpose: MSI package definition and custom actions
- Contains: WiX XML source, EULA, version-check DLL, build script
- Key files:
  - `installer.wxs` (defines MSI structure, file copy, shortcuts, task registration, custom actions)
  - `EULA.rtf` (license shown in MSI dialog)
  - `build.ps1` (invoked after dotnet build to create final MSI)
  - `UpdateCheck/UpdateCheck.csproj` (custom action DLL for version-check on install)
- Output: `build/Jaminator.msi` (distributed as GitHub release asset)
- Notes: Requires WiX SDK 4.0+; must run on Windows with MSBuild

**manifest/**
- Purpose: The control plane; all fleet configuration lives here
- Contains: Single manifest.json file
- Key files: `manifest.json` (JSON with schema version, wallpaper config, folder structure, program installs, admin commands, cleanup rules)
- Notes: This file is fetched by every running Jaminator EXE. Edit + commit = zero-code-deployment fleet update. GitHub serves it at `https://raw.githubusercontent.com/zachlagden/jaminator/main/manifest/manifest.json`

**scripts/**
- Purpose: Build and utility scripts
- Contains: PowerShell and batch files for developers
- Key files: `generate-icon.ps1` (creates Jaminator.ico from PNG or SVG)

**src/Jaminator/**
- Purpose: Main application source code
- Contains: C# .NET Framework 4.8 WinForms project
- Key subdirectories:
  - `Models/` — JSON DTOs, no logic
  - `Services/` — Domain logic, each service handles one maintenance capability
  - `UI/` — WinForms MainForm, SectionPanel, visual orchestration
- Output: `src/Jaminator/bin/Release/net48/Jaminator.exe` (built by `dotnet build`)

## Key File Locations

**Entry Points:**
- `src/Jaminator/Program.cs` (Main method, CLI mode parsing)
  - Classifies execution mode (UI, LoginMode, RunAll, Install, Uninstall, RegisterTask, UnregisterTask)
  - Launches WinForms or runs headless operations
- `bootstrap/Program.cs` (Setup stub entry)
  - Queries GitHub API, downloads latest MSI, invokes msiexec
- `installer/installer.wxs` (MSI definition)
  - Defines file copy, shortcuts, custom actions, task registration

**Configuration:**
- `manifest/manifest.json` (The source of truth for all fleet state)
  - Programs to install (per-arch, with prerequisites, detection rules)
  - Commands to run (PowerShell scripts, skip conditions)
  - Folders to create (school structure)
  - Wallpaper config (URL, hash, enforcement flag)
  - Cleanup rules (temp paths, browser caches, documents quarantine)
- `Jaminator.sln` (Visual Studio solution)
- `src/Jaminator/Jaminator.csproj` (Main app project)
- `bootstrap/Bootstrap.csproj` (Setup stub project)
- `installer/UpdateCheck/UpdateCheck.csproj` (Version-check DLL)

**Core Logic:**
- `src/Jaminator/Models/Manifest.cs` (JSON DTOs)
- `src/Jaminator/Services/CleanupRunner.cs` (Cleanup logic)
- `src/Jaminator/Services/MsiInstaller.cs` (Program installation)
- `src/Jaminator/Services/CommandRunner.cs` (Script execution)
- `src/Jaminator/Services/WallpaperSetter.cs` (Wallpaper handling)
- `src/Jaminator/Services/ManifestFetcher.cs` (Config fetch + offline cache)
- `src/Jaminator/Services/SelfUpdater.cs` (Tool self-update)

**User Interface:**
- `src/Jaminator/UI/MainForm.cs` (Main window; orchestrates all operations)
- `src/Jaminator/UI/SectionPanel.cs` (Visual card for each section)

**Utilities:**
- `src/Jaminator/Services/Logger.cs` (Logging to ProgramData\Jaminator\logs\)
- `src/Jaminator/Services/State.cs` (Persistent state in ProgramData\Jaminator\state.json)
- `src/Jaminator/Services/Detector.cs` (Program detection via registry/file/AppX)
- `src/Jaminator/Services/Downloader.cs` (HTTP download with hash check)

## Naming Conventions

**Files:**
- C# source files: PascalCase (e.g., `MainForm.cs`, `CleanupRunner.cs`, `Manifest.cs`)
- JSON config: lowercase with hyphens (e.g., `manifest.json`, but manifest.json uses camelCase keys)
- Script files: snake_case.ps1 (e.g., `generate-icon.ps1`, `build.ps1`)
- Manifests / configs: lowercase (e.g., `App.manifest`)

**Directories:**
- PascalCase for source code (e.g., `Models/`, `Services/`, `UI/`)
- lowercase for asset/doc dirs (e.g., `assets/`, `docs/`, `scripts/`)
- Special: `.github/`, `.planning/`, `.remember/` (dotdirs for tooling)

**C# Classes:**
- Public (interface-facing): PascalCase sealed classes (e.g., `CleanupRunner`, `Logger`, `Manifest`)
- Suffix pattern: Runners are `*Runner` (CleanupRunner, CommandRunner), Setters are `*Setter` (WallpaperSetter), Fetchers are `*Fetcher` (ManifestFetcher)
- Models: DTOs suffixed with `Entry` (WallpaperEntry, ProgramEntry, FolderEntry, CommandEntry, etc.) or plain (Manifest, StateData)

**C# Methods:**
- Public: camelCase (e.g., `RunAsync()`, `FetchAsync()`, `IsInstalled()`)
- Private: camelCase (e.g., `WipeContents()`, `CompareVersions()`)
- Async convention: `*Async` suffix (e.g., `RunAsync()`, `FetchAsync()`)

**JSON (manifest.json):**
- Keys: camelCase (e.g., `schemaVersion`, `manifestVersion`, `minimumToolVersion`, `wallpaper`, `folders`, `programs`, `commands`, `cleanup`, `schedule`)
- Enum-like strings: lowercase (e.g., `"kind": "msi"`, `"shell": "powershell"`)

## Where to Add New Code

**New Feature (e.g., New Maintenance Capability):**
- Primary code: Create a new service in `src/Jaminator/Services/MyFeatureRunner.cs`
  - Implement async method(s) taking Logger and manifest config
  - Example: `public async Task RunAsync(MyFeatureEntry cfg, Logger log)`
- Model: Add DTO to `src/Jaminator/Models/Manifest.cs`
  - Add property to root `Manifest` class
  - Create sealed class for config (e.g., `MyFeatureEntry`)
- UI Integration: Update `src/Jaminator/UI/MainForm.cs`
  - Instantiate runner in ctor
  - Add section to `SectionStyle` dictionary (color + description)
  - Add to `LoginSafeSections` if it's safe to run at logon
  - Call runner from `RunSelectedSectionsAsync()` when section selected
- Manifest: Add example config to `manifest/manifest.json`
- Docs: Update `docs/manifest-schema.md` to document new JSON fields

**New Component/Module:**
- Implementation: Create new file in `src/Jaminator/Services/` or `src/Jaminator/UI/`
- Follow naming: `*Runner.cs` for capability implementations, `*Setter.cs` for state-changing operations, `*Fetcher.cs` for remote operations
- Dependencies: Accept Logger and other services in ctor; never create service instances in methods (violates inversion of control)
- Async: Always expose async methods; use `Task.Run()` for CPU-bound work to avoid UI blocking

**Utilities/Helpers:**
- Shared helpers: `src/Jaminator/Services/` if cross-cutting (e.g., Downloader, Detector, Logger)
- Internal helpers: Keep inside the service that uses them (don't create separate utility class unless 3+ services use it)
- No static utility classes except for small functions (Detector methods, Polyfills)

**Configuration (manifest.json):**
- Structure: Add entry to appropriate section (programs, commands, cleanup, folders, wallpaper, schedule)
- Example: To add a new program install:
  ```json
  {
    "id": "python",
    "name": "Python 3.11",
    "x64": {
      "kind": "msi",
      "url": "https://...",
      "sha256": "...",
      "args": "/quiet",
      "detect": {
        "registryKey": "HKLM\\Software\\Python\\PythonCore\\3.11\\InstallPath",
        "minVersion": "3.11.0"
      }
    }
  }
  ```
- Testing: Commit, push, and test on a VM. No code deployment needed.

**Tests:**
- Test type: Not currently present in repo. When adding:
  - Location: Create `src/Jaminator.Tests/` (sibling to `src/Jaminator/`)
  - Framework: Use xUnit or NUnit
  - Pattern: One test class per service (CleanupRunnerTests, MsiInstallerTests, etc.)
  - Don't test UI (MainForm); test services in isolation with mock Logger

**Build Scripts:**
- Location: `installer/build.ps1` (post-build step for MSI)
- Add to: scripts/ if standalone, or as ps1 comment block if embedded

## Special Directories

**assets/**
- Purpose: Static files
- Generated: No
- Committed: Yes
- Mutable: wallpaper.png can be updated; it's served from GitHub raw URL

**bootstrap/**
- Purpose: Setup stub; part of release artifacts
- Generated: No (source code)
- Committed: Yes
- Output: `Jaminator-Setup.exe` built by `dotnet build bootstrap/Bootstrap.csproj -c Release`

**installer/**
- Purpose: MSI definition
- Generated: No (source code); `build/Jaminator.msi` is generated output
- Committed: Yes (source only; MSI not committed)
- Output: `Jaminator.msi` built by `installer/build.ps1` (requires WiX SDK)

**manifest/**
- Purpose: Live configuration; the control plane
- Generated: No
- Committed: Yes
- Mutable: Yes — edit JSON, commit, deploy (zero-code-deployment)

**.planning/codebase/**
- Purpose: Analysis & planning docs created by GSD
- Generated: Yes (by `/gsd-map-codebase`, `/gsd-plan-phase`, etc.)
- Committed: Yes
- Mutable: No (overwritten by GSD on refresh)

**.remember/**
- Purpose: Internal LLM memory logs
- Generated: Yes (by agent execution)
- Committed: No (in .gitignore)
- Used by: Future agent runs for context/reasoning

**.github/**
- Purpose: GitHub Actions workflows (if any)
- Generated: No
- Committed: Yes
- Note: Currently empty or minimal; no CD pipeline

---

*Structure analysis: 2026-05-11*
