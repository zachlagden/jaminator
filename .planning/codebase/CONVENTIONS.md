# Coding Conventions

**Analysis Date:** 2026-05-11

## Language & Framework

**Primary Language:** C# (.NET Framework 4.8 and .NET 8.0)
- Desktop application targeting Windows 10+
- WinForms for UI components
- PowerShell 5+ for scripting and build automation

**Framework Details:**
- Target Framework: `net48` (Windows Forms, WinForms)
- LangVersion: `latest` (enables C# 9+ features)
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- JSON serialization via Newtonsoft.Json (v13.0.3)

## Naming Patterns

**Files:**
- PascalCase for C# files: `Logger.cs`, `CommandRunner.cs`, `MainForm.cs`
- PascalCase for directories: `Services/`, `Models/`, `UI/`
- PowerShell scripts: kebab-case: `generate-icon.ps1`, `build.ps1`

**Classes & Types:**
- PascalCase for class names: `Logger`, `Manifest`, `ManifestFetcher`
- Sealed classes used extensively: `public sealed class Logger`
- Generic pattern names: `ArchEntry`, `CommandEntry`, `DetectEntry`, `FolderEntry`
- Property names PascalCase with auto-properties: `public string Name { get; set; }`

**Methods:**
- PascalCase: `FetchAsync()`, `RunAsync()`, `IsInstalled()`
- Action methods: `Install()`, `Uninstall()`, `RegisterTask()`
- Query methods: `IsInstalled`, `IsRunningFromInstallDir`
- Async methods explicitly suffixed with `Async`: `FetchAsync()`, `RunOneAsync()`, `EvaluateSkipIfAsync()`

**Variables:**
- Private fields: camelCase with underscore prefix: `_log`, `_fileLock`, `_manifest`
- Local variables: camelCase: `log`, `cmd`, `psi`, `json`
- Constants: PascalCase: `TaskName`, `DailyTaskName`, `InstallDir`

**Enums:**
- PascalCase for enum names and values: `RunMode.Ui`, `RunMode.LoginMode`, `RunMode.Install`

## Code Style

**Formatting:**
- No editorconfig file detected; conventions inferred from source
- 4-space indentation (standard C#)
- Braces on same line (1TBS style): `if (condition) { code }`
- No unnecessary blank lines between method declarations
- Single line method bodies acceptable: `public void Info(string msg) => Write("INFO", msg);`

**Linting:**
- No `.editorconfig` file present
- No StyleCop or Roslyn analyzer configuration file
- No explicit code style enforcement configured

## Import Organization

**Order (from `Program.cs`, `CommandRunner.cs`, `Logger.cs`):**
1. System namespaces: `using System;`, `using System.IO;`, `using System.Collections.Generic;`
2. System.Net / System.Diagnostics: `using System.Diagnostics;`, `using System.Net.Http;`
3. System.Windows: `using System.Windows.Forms;`, `using System.Drawing;`
4. Local project namespaces: `using Jaminator.Services;`, `using Jaminator.Models;`

**Path organization:**
- No path aliases configured; uses full namespace paths
- Models in `Jaminator.Models` namespace
- Services in `Jaminator.Services` namespace
- UI components in `Jaminator.UI` namespace

## Error Handling

**Pattern: Fail-Open with Logging**
- Broad exception catches with inline comments explaining the rationale
- Example from `Logger.cs`: `catch { /* never let logging failure crash a run */ }`
- Example from `ManifestFetcher.cs`: `try { File.WriteAllText(...); } catch { /* cache write best-effort */ }`
- Errors logged but often non-fatal — operations continue gracefully

**Try/Catch Blocks:**
- Multiple exception handlers for hierarchical fallback
- Example from `ManifestFetcher.cs` (lines 38-66):
  ```csharp
  try {
    // attempt fresh fetch
  } catch (Exception netEx) {
    if (File.Exists(CachePath)) {
      try {
        // fallback to cached copy
      } catch (Exception cacheEx) {
        throw new InvalidOperationException("...network failed AND cache corrupt...", netEx);
      }
    }
    throw new InvalidOperationException("...network failed, no cache...", netEx);
  }
  ```

**Exit Codes:**
- Headless modes return `int` from `Main()`: 0 for success, 1 for failure
- Example: `Installer.Install(log)` returns 0 on success, 1 on exception

**Specific Exceptions:**
- `InvalidOperationException` for logic failures: deserialization returning null, missing manifest
- Base `Exception` caught when operation non-critical

## Logging

**Framework:** Custom `Logger` class at `src/Jaminator/Services/Logger.cs`

**Architecture:**
- Public event `OnMessage: Action<string>` for UI subscriptions
- Writes to rotating daily logfiles: `jaminator-YYYYMMDD.log` in `%CommonApplicationData%\Jaminator\logs\`
- Thread-safe via `lock (_fileLock)` object
- Silent failures: `catch { }` block prevents logging failures from crashing the application

**Log Levels:**
- `Info(string msg)` — normal flow
- `Warn(string msg)` — non-fatal issues, non-zero exit codes
- `Error(string msg)` — failure events
- `Error(string msg, Exception ex)` — exception + message combined

**Format:**
```
[HH:mm:ss] [LEVEL] Message text
[14:32:15] [INFO] Command: DisableCortana
[14:32:16] [WARN]   -> exit 1
[14:32:17] [ERROR] Install failed
```

**Usage Pattern:**
- Injected into services: `Logger log` parameter in constructors
- Called at operation boundaries (start of command, exit codes, errors)
- Prefixed indentation for sub-operations: `  | output line` for process stdout/stderr

## Comments

**When to Comment:**
- XML documentation on public types and key private methods
- Inline comments explain *why* a broad exception is caught, not *what* the code does
- Comments above complex logic blocks (PowerShell expression evaluation, version comparison, registry parsing)

**JSDoc/TSDoc (C# Equivalent - XML Documentation):**
- `/// <summary>` blocks on public classes, public methods, public properties
- Example from `State.cs`:
  ```csharp
  /// <summary>
  /// Tiny key/value state file at ProgramData\Jaminator\state.json. Used for
  /// first-run welcome flag and last-login-run summary so the UI can surface
  /// "what happened during the silent logon run".
  /// </summary>
  public sealed class State
  ```
- Used on significant private methods too: `CommandRunner.EvaluateSkipIfAsync()` has XML docs

## Function Design

**Size Guidelines:**
- Average function 15-40 lines
- Utilities commonly 5-10 lines: `Write()`, `ParseMode()`
- Async operations 50+ lines acceptable: `RunOneAsync()` in `CommandRunner.cs`

**Parameters:**
- Single responsibility per parameter
- Nullable reference types used: `public void Error(string msg, Exception ex)`
- Configuration passed as immutable data classes: `CommandEntry cmd`

**Return Values:**
- Exit code integers from headless operations: `Install()` returns `int`
- Tuples for multi-value returns: `FetchAsync()` returns `(Manifest manifest, bool fromCache)`
- Nullable returns explicit: `string?`, `DetectEntry?`
- Async always returns `Task` or `Task<T>`: `async Task RunAsync()`, `async Task<bool> EvaluateSkipIfAsync()`

## Module Design

**Exports:**
- Public static classes for utility operations: `Detector`, `Installer`
- Sealed classes for stateful services: `Logger`, `ManifestFetcher`, `CommandRunner`
- No public constructors on static utility classes

**Barrel Files:**
- Not used; namespaces are fine-grained per responsibility

**File Organization Pattern:**
- One public class per file (rare exceptions: `Manifest.cs` contains 8 nested classes all related to the manifest schema)
- Namespace per directory: `Jaminator.Services`, `Jaminator.Models`, `Jaminator.UI`

## JSON Configuration

**Schema (Newtonsoft.Json):**
- `[JsonProperty("key")]` attributes on properties to map to snake_case JSON keys
- Example from `Models/Manifest.cs`:
  ```csharp
  [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
  [JsonProperty("folders")] public List<FolderEntry> Folders { get; set; } = new();
  ```
- Defaults used: `string Url = ""`, `List<T> = new()`

**Source of Truth:**
- `manifest/manifest.json` in repository root
- Lives in version control; fetched at runtime by every installation
- Schema documented in `docs/manifest-schema.md`

## Security Patterns

**No Hardcoded Secrets:**
- URLs are public (GitHub raw content links)
- Environment variables for ProgramData paths (standard Windows)
- No API keys or auth tokens in source

**SHA256 Verification:**
- Every downloaded file (installer, wallpaper) verified against manifest checksum
- `HashVerifier.cs` service handles validation
- Missing/mismatched hashes cause operation to fail

## Assembly Configuration

**csproj Settings (`src/Jaminator/Jaminator.csproj`):**
- OutputType: `WinExe` (GUI application)
- AssemblyName: `Jaminator`
- RootNamespace: `Jaminator`
- ApplicationIcon: `Jaminator.ico`
- Deterministic: `true` (reproducible builds)
- DebugType: `embedded` (debug info in assembly)
- Company: `Jam Coding`, Product: `Jaminator`
- LangVersion: `latest` (C# 9+ features allowed)

---

*Convention analysis: 2026-05-11*
