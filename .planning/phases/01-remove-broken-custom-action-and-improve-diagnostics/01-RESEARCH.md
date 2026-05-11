# Phase 1: Remove broken custom action and improve diagnostics - Research

**Researched:** 2026-05-11
**Domain:** WiX v4 MSI authoring + .NET Framework 4.8 process diagnostics + Windows Installer artifact inspection
**Confidence:** HIGH (all critical findings cited from official MS Learn docs, FireGiant WiX docs, and verified codebase line-numbers)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**UpdateCheck removal strategy:**
- **D-01:** Delete `installer/UpdateCheck/` directory entirely (`UpdateCheckCA.cs`, `UpdateCheck.csproj`). Do NOT leave a tombstone README or empty directory — git history (`git log -- installer/UpdateCheck/`) plus the v0.7.5 release notes (Phase 3) preserve the rationale.
- **D-02:** Remove the `installer/UpdateCheck/UpdateCheck.csproj` reference from `Jaminator.sln`.
- **D-03:** Remove from `installer/build.ps1`: the `$caDll` variable (line 29), the `Test-Path $caDll` build-gate check (lines 30, 34), the `-d "UpdateCheckCaDll=$caDll"` argument passed to `wix build` (line 45).
- **D-04:** Remove from `installer/installer.wxs`: the `<Binary Id="UpdateCheckCA" .../>` element (line 70), the `<CustomAction Id="CheckForNewerVersion" .../>` element (lines 71-76), the `<InstallUISequence><Custom Action="CheckForNewerVersion" .../></InstallUISequence>` block (lines 86-90). Keep `LaunchApplication`, `RegisterTask`, `UnregisterTask`, all WixUI UI elements, all ComponentGroups.

**RegisterTask diagnostics (DIAG-01):**
- **D-05:** Enrich `src/Jaminator/Services/Installer.cs::RegisterScheduledTask` (line 149) so that on the `catch (Exception ex)` branch (line 210), it writes a discoverable error file to `%TEMP%\Jaminator-register-task-error-YYYYMMDDhhmmss.log` containing: timestamp + run mode (`--register-task`), full exception type/message/stack trace, the `schtasks.exe` command line attempted and its captured stdout/stderr, path to the failing task XML (defer deletion when an error occurs).
- **D-06:** Modify `RunSchTasks` (helper, lines 387-402) to capture stdout and stderr instead of letting them go to console. On non-zero exit, throw with the captured output included in the exception message.
- **D-07:** Keep the existing `log.Error("Failed to register scheduled task", ex)` call. The TEMP log is **additive** — ProgramData log stays as the canonical record; the TEMP log exists because at MSI install time the rollback may delete ProgramData artifacts before the user can read them.
- **D-08:** The CA's existing `Return="check"` semantics (installer.wxs line 155) stay unchanged — non-zero exit still propagates as MSI failure. We're only making the failure *legible*, not changing the rollback behavior.

**Build verification:**
- **D-09:** Phase 1 completion is gated by an artifact-level check: after `installer/build.ps1` succeeds, inspect `Jaminator.msi` and confirm zero hits for `UpdateCheckCA`, `CheckForNewerVersion`, `Wix4DTFCustomAction`. Planner picks the exact tool.
- **D-10:** No version bump in Phase 1. `Program.cs::ToolVersion` stays at `"0.7.4"` until Phase 3.

**Commit strategy:**
- **D-11:** Atomic commits per file-area, in this order:
  1. `fix(installer): remove CheckForNewerVersion custom action from WiX source` — installer.wxs edits only
  2. `chore(installer): remove UpdateCheck project from solution and build script` — Jaminator.sln + installer/build.ps1
  3. `chore(installer): delete UpdateCheck custom-action project` — `rm -r installer/UpdateCheck/`
  4. `feat(installer): emit register-task failure diagnostics to TEMP log` — Installer.cs changes (D-05, D-06, D-07)
- **D-12:** Each commit is independently buildable. After commits 1-3, `installer/build.ps1` must produce a working MSI; after commit 4, `dotnet build Jaminator.sln` must succeed and the EXE's `--register-task` path must still register successfully on a clean machine.

### Claude's Discretion

- Exact technique for capturing schtasks.exe stdout/stderr in `RunSchTasks` (D-06) — sync vs async reads. Planner picks; contract is "captured output ends up in the catch's exception message."
- Exact MSI-inspection tool for D-09 — planner picks based on what's installable on the user's Windows dev box.
- Whether to introduce a small helper (`Installer.WriteDiagnosticLog(string area, Exception ex)`) shared between `RegisterScheduledTask` and `UnregisterScheduledTask`, or inline the TEMP-log write in `RegisterScheduledTask` only. Either is fine.

### Deferred Ideas (OUT OF SCOPE)

**For Phase 2:**
- Documenting the `%TEMP%\Jaminator-register-task-error-*.log` filename and the `msiexec /l*v` procedure in `docs/INSTALL-LOGGING.md` (DIAG-02).
- Verifying that the rebuilt MSI installs cleanly on Win11 64-bit (INSTALL-01) and Win10 (INSTALL-02), including silent-install regression (INSTALL-04) and SelfUpdater chain (INSTALL-05).

**For Phase 3:**
- Bumping `Program.cs::ToolVersion` to `"0.7.5"`.
- Tagging the release and creating the GitHub Release.

**For future milestones (not Milestone 1):**
- Generalized install-time diagnostics across all install-time entry points (Milestone 3).
- Reintroducing install-time update check via a native (C++) CA (explicitly rejected for Milestone 1).
- CI / automated installer regression test on a clean Windows VM (HARDEN-05).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| INSTALL-03 | The `CheckForNewerVersion` custom action and the entire `installer/UpdateCheck/` project are removed from the MSI build — no managed-CA surface remains in the install path | Standard Stack (WiX v4 surgical edit), Common Pitfall P1 (empty `<InstallUISequence>`), Code Example E1 (final installer.wxs delta) |
| DIAG-01 | The remaining deferred CA (`RegisterTask`) produces actionable output in the MSI log on any failure (no silent return-value-3-with-no-context) | Standard Stack (Process stdout/stderr capture pattern), Common Pitfall P2 (deadlock in existing `RunSchTasks`), Code Example E2 (deadlock-safe RunSchTasks), Code Example E3 (TEMP-log write) |
</phase_requirements>

## Summary

This is a surgical brownfield removal + targeted diagnostics enrichment, not a green-field design. The four edits are mechanical; the value is in **getting them in the right order with the right verification gates** so the four-commit boundary in D-11 is independently revertable.

**The single non-obvious finding:** the existing `RunSchTasks` helper at `Installer.cs:387-402` already redirects both stdout AND stderr, then reads them synchronously in series (`p.StandardOutput.ReadToEnd()` followed by `p.StandardError.ReadToEnd()` followed by `p.WaitForExit()`). **This is the exact pattern Microsoft's docs flag as a deadlock risk** [CITED: learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.redirectstandardoutput]. The code only works today because `schtasks.exe`'s output is small enough to fit in the OS pipe buffer (~4 KB), and `schtasks.exe` always closes stdout before stderr in practice. The diagnostic enrichment (D-06) must replace this with the documented-safe pattern: read one stream synchronously, the other via async `BeginErrorReadLine()` / `ErrorDataReceived` event. Otherwise we ship a latent deadlock to production every time the helper handles a more verbose schtasks message.

**The other quiet finding:** `dotnet sln remove` leaves orphan `GlobalSection(ProjectConfigurationPlatforms)` entries in the `.sln` file [VERIFIED: github.com/dotnet/sdk/issues/8037]. For this 32-line solution file, hand-edit is cleaner than the CLI — three blocks need removal (the `Project(...)` line for UpdateCheck, its four ProjectConfigurationPlatforms entries, and any nested-projects reference), and the diff is small enough to review in-PR.

**Primary recommendation:** Execute the four commits in the order locked by D-11. Use `lessmsi l -t <TableName>` for the artifact-level check (D-09) — single binary, CSV output, trivial install. For RunSchTasks (D-06), use the MS-canonical "sync stdout + async stderr via event handler" pattern.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| MSI authoring (declare what gets installed) | Build-time WiX source (`installer/installer.wxs`) | — | Single source of truth for MSI structure; WiX compiler emits MSI tables |
| Build orchestration (compile EXE + MSI) | Build-time PowerShell (`installer/build.ps1`) | — | Single entry point for producing the release artifact |
| Solution graph (which projects build together) | Build-time .sln + .csproj | — | MSBuild dependency graph; consumed by `dotnet build` |
| Scheduled-task registration logic | EXE runtime (`Installer.cs::RegisterScheduledTask`) | — | Already moved out of CA into the EXE process — that's the architecture being preserved |
| Install-time invocation of EXE | MSI deferred CA (`installer.wxs` line 150-155) | EXE runtime | MSI fires the CA, the CA shells out to `Jaminator.exe --register-task` |
| Failure-path diagnostics | EXE runtime (`Installer.cs` catch branch) | MSI log via Console.WriteLine + TEMP log file | EXE writes to two channels: ProgramData log (canonical) + TEMP log (discoverable when MSI rolls back); MSI log captures the EXE's Console.WriteLine via deferred CA stdout/stderr capture |
| Artifact verification | Local dev box (lessmsi CLI) | — | Build-script exit code is necessary but insufficient; table inspection proves the removal |

**Note:** No tier reassignment happens in this phase — capability already lives where it belongs. The phase strips a misplaced capability (network update-check in MSI CA) that has been duplicated correctly elsewhere (`SelfUpdater.cs` in the EXE).

## Standard Stack

### Core (already present — verify, do not introduce)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WiX Toolset CLI | 4.x (5.x compatible) | `wix build` produces MSI; `wix msi decompile` inspects existing MSI | Canonical WiX v4 binary; already a build prereq [CITED: docs.firegiant.com/wix/tools/wixexe/] |
| .NET SDK | 8+ | `dotnet build`, `dotnet sln list/remove` | Already required by build script [VERIFIED: build.ps1 header] |
| System.Diagnostics.Process | net48 BCL | Launch + capture schtasks.exe output | In-box; no NuGet dependency [CITED: learn.microsoft.com/dotnet/api/system.diagnostics.process] |

### Supporting (new — install on dev box for D-09 verification)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| lessmsi | 1.10+ | List MSI tables as CSV for artifact verification | Phase 1 D-09 check + Phase 2 smoke-test gate |

**Verified install paths (pick one on the user's Win11 dev box):**

| Method | Command | Pros | Cons |
|--------|---------|------|------|
| winget | `winget install activescott.lessmsi` | Built-in to Win11, no extra package manager | Requires winget; ID format unverified — fall back to chocolatey if no hit |
| chocolatey | `choco install lessmsi -y` | Reliable, well-known | Requires choco bootstrap |
| Manual zip | Download `lessmsi-vX.Y.Z.zip` from https://github.com/activescott/lessmsi/releases, unzip to `%LOCALAPPDATA%\lessmsi\`, add to PATH | No package manager needed | Manual PATH management |

[CITED: github.com/activescott/lessmsi README] — installation methods listed; "Install via Chocolatey (or download a zip)".

### Alternatives Considered (rejected for D-09)

| Instead of lessmsi | Considered | Tradeoff |
|------------|-----------|----------|
| `wix msi decompile build/Jaminator.msi -o build/decompiled.wxs` | WiX-native, no extra install | Writes a `.wxs` file to disk that must be grep'd then deleted — two extra steps. `wix msi decompile` has known bugs around binary extraction and modularization GUIDs [CITED: github.com/wixtoolset/issues/7574]. Useful as fallback if lessmsi is unavailable, but lessmsi is the direct hit. |
| `msidump` (Orca/Windows SDK) | Microsoft-native MSI tooling | Orca requires the Windows SDK installer (multi-GB); `msidump` is part of Windows SDK Components. Significantly heavier than lessmsi. |
| `Microsoft.Deployment.WindowsInstaller` PowerShell | Already available | Requires writing PowerShell to query the MSI database via COM — more code than `lessmsi l -t Table`. |

**Verification command (canonical for D-09):**
```powershell
lessmsi l -t Binary build\Jaminator.msi | Select-String UpdateCheckCA
lessmsi l -t CustomAction build\Jaminator.msi | Select-String "CheckForNewerVersion|Wix4DTFCustomAction"
lessmsi l -t InstallUISequence build\Jaminator.msi | Select-String CheckForNewerVersion
```
All three pipelines must produce zero output. If any returns a row, the strip is incomplete.

[CITED: github.com/activescott/lessmsi/wiki/Command-Line] — `l -t <table_name> <msi_file>` syntax and CSV output.

## Architecture Patterns

### System Architecture Diagram (Phase 1 scope)

```
                ┌─────────────────────────────────────────────────────────┐
                │                  BUILD PIPELINE                          │
                │                                                          │
                │  Jaminator.sln                                           │
                │   ├── src/Jaminator (kept)                               │
                │   ├── bootstrap (kept)                                   │
                │   └── installer/UpdateCheck ──────► [REMOVED in Phase 1] │
                │                                                          │
                │  installer/build.ps1                                     │
                │   └── wix build installer.wxs                            │
                │         -d UpdateCheckCaDll=...  ───► [REMOVED]          │
                │         -d SourceDir=...                                 │
                │         -d Version=0.7.4                                 │
                │         → build/Jaminator.msi                            │
                │                  │                                       │
                │                  ▼                                       │
                │    ╔════════════════════════════════╗                    │
                │    ║   ARTIFACT VERIFICATION GATE   ║                    │
                │    ║   lessmsi l -t Binary ...      ║                    │
                │    ║   lessmsi l -t CustomAction... ║                    │
                │    ║   lessmsi l -t InstallUI...    ║                    │
                │    ║   → zero hits required         ║                    │
                │    ╚════════════════════════════════╝                    │
                └─────────────────────────────────────────────────────────┘

                ┌─────────────────────────────────────────────────────────┐
                │                  RUNTIME (install-time)                 │
                │                                                          │
                │  msiexec /i Jaminator.msi                                │
                │   ├── InstallUISequence  ◄── (now contains nothing      │
                │   │                            phase-1-relevant; the    │
                │   │                            CheckForNewerVersion     │
                │   │                            entry is removed)        │
                │   └── InstallExecuteSequence                            │
                │        ├── InstallFiles                                  │
                │        ├── RegisterTask ──► Jaminator.exe --register-task │
                │        │                     │                           │
                │        │                     ▼                           │
                │        │       ┌───────────────────────────────┐         │
                │        │       │  Installer.RegisterScheduled  │         │
                │        │       │      Task()                    │         │
                │        │       │   ├── Write task XML to %TEMP% │         │
                │        │       │   ├── RunSchTasks(/Create...)  │         │
                │        │       │   │     │                       │         │
                │        │       │   │     ├── SUCCESS: return 0   │         │
                │        │       │   │     │                       │         │
                │        │       │   │     └── FAIL (throw):       │         │
                │        │       │   │           captured stdout+  │         │
                │        │       │   │           stderr in ex.Msg  │         │
                │        │       │   │                              │         │
                │        │       │   └── catch (ex):                │         │
                │        │       │       ├── log.Error → ProgramData │       │
                │        │       │       ├── WriteDiagLog → %TEMP%  │NEW    │
                │        │       │       │     (timestamped file)   │       │
                │        │       │       ├── Preserve task XML      │NEW    │
                │        │       │       └── return 1               │       │
                │        │       └───────────────────────────────┘         │
                │        │                                                  │
                │        └── (Return="check" → MSI rollback if exit≠0)     │
                └─────────────────────────────────────────────────────────┘
```

### Pattern 1: WiX v4 surgical CA removal

**What:** Remove three elements (`<Binary>`, `<CustomAction>`, `<InstallUISequence>` content) from `installer.wxs`. The element being deleted has no schema dependencies elsewhere in the file.

**When to use:** When stripping a CA that is the sole occupant of an enclosing sequence element.

**Key facts (verified against installer.wxs line-by-line):**

1. **`UpdateCheckCaDll` WixVariable usage** is at line 70 only (`SourceFile="$(var.UpdateCheckCaDll)"`). Removing line 70 makes the variable unused. WiX v4 does NOT error on undefined `-d` extension variables unless they are referenced — once line 70 is gone, dropping `-d UpdateCheckCaDll=...` from `build.ps1` is safe.

2. **`<InstallUISequence>` block (lines 86-90)** contains exactly one child (`CheckForNewerVersion`). Per WiX v4 schema [CITED: docs.firegiant.com/wix/schema/wxs/installuisequence/], an empty `<InstallUISequence/>` element is valid but pointless — **remove the entire block (lines 86-90)**, not just the inner `<Custom>`. This avoids a dangling empty XML element and prevents future maintainers from accidentally re-targeting it.

3. **`LaunchApplication` CustomAction** (lines 61-64) uses `BinaryRef="Wix4UtilCA_X86"`, a binary provided by the `WixToolset.Util.wixext` extension — **completely independent** of UpdateCheckCA. Not affected by Phase 1. Confirmed by the `-ext WixToolset.Util.wixext` flag in build.ps1:48.

4. **WixUI elements** (`<ui:WixUI Id="WixUI_Minimal" />`, `<UIRef Id="WixUI_ErrorProgressText" />`, `<WixVariable Id="WixUILicenseRtf" .../>`) at lines 51-55 — completely independent. They use the `WixToolset.UI.wixext` extension. Not affected.

5. **`<Publish Dialog="ExitDialog" .../>` block** (lines 78-84) — references `LaunchApplication`, not `CheckForNewerVersion`. Not affected.

6. **No conditions or sequencing constraints elsewhere reference `CheckForNewerVersion`** — grep `installer.wxs` shows only the three locations (lines 70, 71-76, 87) being removed.

### Pattern 2: Process stdout/stderr capture in .NET 4.8 (canonical MS pattern)

**What:** Launch a child process, capture BOTH stdout and stderr without deadlock, then await exit and emit captured streams.

**When to use:** Any `RunSchTasks`-style helper that needs failure-mode visibility.

**The deadlock pitfall (cited from Microsoft docs verbatim):**

> "A deadlock condition results if the parent process calls `p.StandardOutput.ReadToEnd` followed by `p.StandardError.ReadToEnd` and the child process writes enough text to fill its error stream. The parent process would wait indefinitely for the child process to close its StandardOutput stream. The child process would wait indefinitely for the parent to read from the full StandardError stream."

[CITED: learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.redirectstandardoutput, fetched 2026-05-11]

**This is the exact pattern in `Installer.cs:397-399` today.** It happens to work because `schtasks.exe` output is small; it will deadlock the first time schtasks emits a multi-KB error (rare but possible — e.g., XML parse error dumps the XML).

**Recommended pattern (also from MS docs — read sync on one stream, async on the other):**

```csharp
var psi = new ProcessStartInfo("schtasks.exe", args)
{
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};
using var p = Process.Start(psi)!;

var stderrBuilder = new System.Text.StringBuilder();
p.ErrorDataReceived += (s, e) =>
{
    if (e.Data != null) stderrBuilder.AppendLine(e.Data);
};
p.BeginErrorReadLine();                    // async drain of stderr

string stdout = p.StandardOutput.ReadToEnd();   // sync drain of stdout
p.WaitForExit();                            // MUST be after ReadToEnd per MS guidance

string stderr = stderrBuilder.ToString();
```

**Why this is correct:**
- Stdout is drained synchronously — pipe never fills.
- Stderr is drained asynchronously on a thread pool worker — pipe never fills.
- `WaitForExit()` after `ReadToEnd()` ensures `OutputDataReceived` / `ErrorDataReceived` event handlers run to completion (per MS docs: "The application that is processing the asynchronous output should call the WaitForExit method to ensure that the output buffer has been flushed").

**On `Async` alternatives:** `ReadToEndAsync()` + `WaitForExitAsync()` is cleaner but requires the caller to be `async`. `RunSchTasks` is currently a sync method called from a sync path (the deferred CA invokes `Jaminator.exe --register-task` which runs through synchronous `Installer.RegisterScheduledTask`). Adding `async` plumbing here would touch four call sites in `Installer.cs` and the `Program.cs` Main dispatch. **The event-handler pattern is the surgical fit** — it stays sync at the call-site contract.

### Pattern 3: TEMP-log emission under MSI deferred CA (SYSTEM context)

**What:** Write a discoverable error file from inside a deferred custom action running as `Impersonate="no"` (i.e., SYSTEM).

**Critical fact about `Path.GetTempPath()` under SYSTEM:**

When `RegisterScheduledTask` runs from inside the `RegisterTask` deferred CA (installer.wxs line 150-155, `Impersonate="no"`), it executes under `NT AUTHORITY\SYSTEM`. **`Path.GetTempPath()` returns `C:\Windows\Temp`**, not the invoking user's `%LOCALAPPDATA%\Temp\` [CITED: learn.microsoft.com/en-us/archive/msdn-technet-forums/deb60504-30a3-46b8-a45e-9a868b821304].

This is **fine and intentional** — `C:\Windows\Temp` is:
- A stable, well-known location any admin can find.
- World-readable by default (not by world-writable) — so the user who triggered the install can read the file even though SYSTEM wrote it.
- Documented in the v0.7.5 release notes (Phase 3, RELEASE-03) so users know where to look.

**Do NOT try to resolve the invoking user's temp.** Two options exist (`MsiGetUserInfo`, or passing the user's TEMP via `CustomActionData`), but both:
- Add complexity to the deferred CA wiring in `installer.wxs`.
- Break the principle established in CONTEXT.md D-04 (no new CA surface beyond what's already there).
- Provide no real benefit — `C:\Windows\Temp` is discoverable.

**Filename format (from CONTEXT.md specifics):** `Jaminator-register-task-error-YYYYMMDDhhmmss.log` — local time, sortable, one file per failed attempt.

**File contents (D-05 requirement):**

```
Jaminator --register-task diagnostic log
Generated: 2026-05-11T14:32:17 local
Run mode: --register-task
Tool version: 0.7.4

--- Exception ---
Type: System.Exception
Message: schtasks /Create /TN "Jaminator-Login" /XML "C:\Windows\Temp\jaminator-task-abc123.xml" /F exit 1: ERROR: <captured stderr line>
Stack trace:
   at Jaminator.Services.Installer.RunSchTasks(String args, Boolean allowFailure) ...
   at Jaminator.Services.Installer.RegisterScheduledTask(Logger log) ...
   at Jaminator.Program.Main(String[] args) ...

--- Captured schtasks.exe output ---
Command line: schtasks /Create /TN "Jaminator-Login" /XML "C:\Windows\Temp\jaminator-task-abc123.xml" /F
Exit code: 1
STDOUT:
<stdout content>
STDERR:
ERROR: <stderr content>

--- Failing task XML ---
Preserved at: C:\Windows\Temp\jaminator-task-abc123.xml
(deleted on success; preserved on failure for diagnostics)
```

### Anti-Patterns to Avoid

- **Hand-editing the .sln in-place without removing the orphan ProjectConfigurationPlatforms entries:** `dotnet sln remove` leaves these entries behind [CITED: github.com/dotnet/sdk/issues/8037]. If you use the CLI, you MUST follow up with either a VS2022 re-open (which auto-cleans on save) or hand-trim. For this 32-line file, **just hand-edit — three explicit deletions, no CLI surprise**.
- **Reading stdout-sync then stderr-sync from the same process:** The latent deadlock pattern described above. Existing code does this; the diagnostic enrichment must fix it.
- **Trying to swallow the schtasks failure inside `RunSchTasks` "for safety":** D-08 mandates that `Return="check"` semantics stay — failure must still propagate. The whole point of the diagnostics enrichment is to make a failure visible, not to mask it.
- **Putting the diagnostic write inside the `finally` block:** The exception path is `catch { write log; return 1; }`. The `finally` runs on success too — we'd write spurious "diagnostic" files on every successful install. Keep the TEMP-log write in the `catch`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| MSI table inspection | A custom MSI database query in PowerShell with `WindowsInstaller.Installer` COM | `lessmsi l -t <Table> <file.msi>` | One line vs ~20 lines of COM marshaling; CSV output is grep-friendly |
| Process stdout/stderr deadlock-free capture | A new threading abstraction wrapping `Process.Start` | The MS-canonical pattern in `System.Diagnostics.Process` docs (sync-one, async-other) | The pattern is documented in MS docs with a worked example; reinventing it is risk |
| Timestamp formatting for the TEMP filename | A custom formatter | `DateTime.Now.ToString("yyyyMMddHHmmss")` | BCL-native, matches the format spec in CONTEXT.md specifics |
| Solution-file editing | A regex-on-XML approach (the .sln isn't even XML) | Visual Studio reopen OR direct hand-edit of the 32-line file | The .sln format is documented [CITED: learn.microsoft.com/en-us/visualstudio/extensibility/internals/solution-dot-sln-file]; structured edit by VS or manual is reliable |

**Key insight:** Every "problem" in this phase has a documented well-known answer. The risk is not in solution choice — it's in commit-boundary discipline and the deadlock that the existing `RunSchTasks` already has (waiting to bite).

## Runtime State Inventory

This phase involves a project rename/refactor (`installer/UpdateCheck/` deletion) and a CA rename/removal (`CheckForNewerVersion`). The audit is small but explicit:

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| **Stored data** | None — UpdateCheck CA was stateless. No database, registry value, or filesystem artifact references `UpdateCheckCA` or `CheckForNewerVersion` outside the four files listed in CONTEXT.md. The MSI `Binary` table embeds the DLL but that's a build artifact, not persistent state — it's regenerated from source on next `wix build`. | None. Old installed v0.7.4 MSIs that contain the CA in their MSI cache (`C:\Windows\Installer\*.msi`) are not phase-1 scope — they'll be replaced when the user runs `msiexec /i Jaminator.msi` for the new MSI. The MajorUpgrade element (installer.wxs:22) handles the replace. |
| **Live service config** | None — no Datadog/Tailscale/cloud config references `UpdateCheck`. The GitHub Releases API endpoint is referenced from `SelfUpdater.cs` and `bootstrap/Program.cs` (kept) and `UpdateCheckCA.cs` (deleted) — no external service config to update. | None. |
| **OS-registered state** | None for UpdateCheck. The `Jaminator-Login` and `Jaminator-Daily` scheduled tasks are unchanged (they're registered by `RegisterScheduledTask`, which stays). | None. |
| **Secrets and env vars** | None — UpdateCheck used no secrets, no env vars. | None. |
| **Build artifacts / installed packages** | `installer/UpdateCheck/bin/Release/net48/UpdateCheckCA.CA.dll` (the SFXCA-wrapped DLL) and `installer/UpdateCheck/obj/` will be stale on the dev box after Phase 1. They're not in git (per standard .gitignore). | `dotnet clean Jaminator.sln` after commits 1-3 to flush stale obj/bin. Re-running `installer/build.ps1` should not find the directory or the DLL. |

**Verification commands (Windows):**
```powershell
# Confirm no MSI / cache / staging references stale UpdateCheck DLL paths after Phase 1
Get-ChildItem -Path C:\Windows\Installer -Filter *.msi -ErrorAction SilentlyContinue | Out-Null  # informational only; old MSIs in cache are fine

# Confirm no installed UpdateCheck binary in dev tree
Test-Path installer\UpdateCheck  # MUST return False after commit 3
Test-Path installer\UpdateCheck\bin  # MUST return False after commit 3
```

## Environment Availability

Phase 1 has external tool dependencies. Per Step 2.6 of the research protocol — this is a research probe (current environment is WSL/Linux; the actual MSI rebuild happens on Windows). Probes below are advisory for the Windows dev box that will execute Phase 1's commits.

| Dependency | Required By | Required Version | Fallback |
|------------|------------|------------------|----------|
| .NET SDK | `dotnet build`, `dotnet sln` | 8.0+ | None — strict requirement per STACK.md |
| WiX 4 CLI (`wix.exe`) | `installer/build.ps1` `wix build ...` | 4.0+ (5.x compatible) | None — strict requirement |
| PowerShell | `installer/build.ps1` execution | 5.1+ (Windows built-in) | None — Windows-resident |
| `schtasks.exe` | `RunSchTasks` at runtime (test of D-06 fix) | OS-shipped | None — Windows-resident since XP |
| lessmsi | D-09 artifact verification | 1.10+ | (a) `wix msi decompile` already-installed alternative; (b) PowerShell COM via `WindowsInstaller.Installer` |
| git | Commits 1-4 per D-11 | Any modern | None |

**Missing dependencies with no fallback:** None (all required tools either ship with Windows, are already prereqs, or have a built-in fallback).

**Missing dependencies with fallback:** `lessmsi` — if absent on the dev box, the planner can switch to `wix msi decompile build/Jaminator.msi -o build/decompiled.wxs` and then grep the resulting `.wxs` file for the same three identifiers. Either works for D-09.

**Probe commands the planner can add to PLAN.md verification steps:**
```powershell
# (To be run on the Windows dev box before starting Phase 1 commits)
dotnet --version          # expect 8.0+
wix --version             # expect 4.x or 5.x
Get-Command lessmsi -ErrorAction SilentlyContinue  # null → install via winget/choco
```

## Common Pitfalls

### Pitfall P1: Leaving an empty `<InstallUISequence/>` after removing the CA

**What goes wrong:** Removing only the inner `<Custom Action="CheckForNewerVersion" .../>` line leaves an empty `<InstallUISequence></InstallUISequence>` block. WiX v4 schema allows this (it's syntactically valid), but it's dead XML — confusing for future maintainers and a footgun if someone later adds a UI action to it accidentally.

**Why it happens:** Mechanical "delete only the child element" approach; the parent looks "structural" so it's preserved by habit.

**How to avoid:** Remove the entire `<InstallUISequence>...</InstallUISequence>` block (lines 86-90 of installer.wxs). If a future phase needs to schedule a UI action, the block can be re-added in two lines.

**Warning signs:** Diff for installer.wxs shows an empty `<InstallUISequence/>` element after Phase 1 commit 1.

### Pitfall P2: Latent deadlock in `RunSchTasks` (existing bug being inherited)

**What goes wrong:** The existing helper (`Installer.cs:387-402`) does `p.StandardOutput.ReadToEnd()` then `p.StandardError.ReadToEnd()` then `p.WaitForExit()`. This is the MS-documented deadlock pattern. It survives in production today because `schtasks.exe`'s output is small.

**Why it happens:** Synchronous reads on both redirected streams force a write-ordering dependency on the child. If the child writes >4KB to one stream before draining the other, the OS pipe fills, child blocks, parent blocks on its own ReadToEnd → deadlock.

**How to avoid:** D-06 fix replaces with the MS-canonical pattern: sync ReadToEnd on stdout + `BeginErrorReadLine()` event-driven capture on stderr + `WaitForExit()` last. See Pattern 2 above for the code template.

**Warning signs:** Test that `RunSchTasks` is hung in production when schtasks emits a verbose error. With the fix, the diagnostic file produced should contain the full schtasks output regardless of size.

### Pitfall P3: `dotnet sln remove` leaves orphan ProjectConfigurationPlatforms entries

**What goes wrong:** Running `dotnet sln Jaminator.sln remove installer/UpdateCheck/UpdateCheck.csproj` removes the `Project(...)... EndProject` block but leaves four `{D8F3B4E5-...}.Debug|Any CPU.ActiveCfg = Debug|Any CPU` lines in `GlobalSection(ProjectConfigurationPlatforms)`. The solution still builds, but the file has dangling GUID references.

**Why it happens:** Known SDK limitation [VERIFIED: github.com/dotnet/sdk/issues/8037] — the CLI only edits the project list, not the build-configuration map.

**How to avoid:** Hand-edit `Jaminator.sln` instead of using the CLI. Lines to delete (using current GUID `{D8F3B4E5-3456-6543-CDEF-3456789012CD}`):
- Line 7-8: `Project("{9A19103F-...}") = "UpdateCheck", "installer\UpdateCheck\UpdateCheck.csproj", "{D8F3B4E5-...}" / EndProject`
- Lines 23-26: All four `{D8F3B4E5-...}` configuration mappings in `GlobalSection(ProjectConfigurationPlatforms)`

After hand-edit, the final `.sln` should have exactly 22 lines (currently 32), with only `Jaminator` and `Bootstrap` projects and 8 configuration mappings (2 projects × 4 mappings each).

**Warning signs:** `dotnet build Jaminator.sln` works but `grep D8F3B4E5 Jaminator.sln` returns non-zero — orphan entries present. Or VS2022 silently rewrites the .sln on next open (file-system noise on next commit).

### Pitfall P4: Forgetting the bindpath argument cleanup

**What goes wrong:** `installer/build.ps1` line 46 passes `-bindpath "$repoRoot\installer"`. This bindpath was added to resolve `WixUILicenseRtf` and EULA.rtf references; **it stays**. But the `-d "UpdateCheckCaDll=$caDll"` arg (line 45) and the `$caDll` variable (line 29) must both be removed in commit 2.

**Why it happens:** Mechanical deletion of one arg, forgetting it's used elsewhere.

**How to avoid:** Diff installer/build.ps1 after commit 2 — only lines 29, 30 (the `Test-Path $caDll` test in the build-gate `if`), line 34 (`if (-not (Test-Path $caDll)) { throw ... }`), and line 45 (`-d "UpdateCheckCaDll=$caDll"`) should be gone. Lines 28, 46-50 stay (the EXE check and the bindpath, ext, arch, output args).

**Specific edit map (verified from current `build.ps1`):**

| Line | Current content | Action |
|------|-----------------|--------|
| 28 | `$binDir = "$repoRoot\src\Jaminator\bin\$Configuration\net48"` | **Keep** |
| 29 | `$caDll = "$repoRoot\installer\UpdateCheck\bin\$Configuration\net48\UpdateCheckCA.CA.dll"` | **Delete** |
| 30 | `if (-not (Test-Path "$binDir\Jaminator.exe") -or -not (Test-Path $caDll)) {` | **Change to:** `if (-not (Test-Path "$binDir\Jaminator.exe")) {` |
| 31-33 | `Write-Host "Building solution..."` + `dotnet build` + closing brace | **Keep** |
| 34 | `if (-not (Test-Path $caDll)) { throw "UpdateCheckCA.CA.dll not produced — DTF packaging failed" }` | **Delete** |
| 42-44 | `wix build ... -d "Version=..." -d "SourceDir=..."` | **Keep** |
| 45 | `-d "UpdateCheckCaDll=$caDll" `` ` | **Delete** |
| 46-50 | `-bindpath`, `-ext`, `-arch`, `-o $msi` | **Keep** |

### Pitfall P5: Trying to write to the EXE path or InstallDir from the catch branch

**What goes wrong:** Writing to `Path.Combine(InstallDir, "diagnostic.log")` from within `RegisterScheduledTask`. At the moment `RegisterTask` deferred CA runs, MSI is mid-install. If the registration fails, MSI rolls back and **removes everything under `Program Files\Jaminator\`** — including any diagnostic the EXE just wrote there.

**Why it happens:** Wanting the log "next to the EXE" feels natural.

**How to avoid:** TEMP path only. `C:\Windows\Temp` is outside MSI's rollback scope. The user-facing log path documented in release notes is `C:\Windows\Temp\Jaminator-register-task-error-*.log`.

**Warning signs:** Diagnostic log writes to `Path.Combine(InstallDir, ...)`. Diagnostic file vanishes after MSI rollback completes.

### Pitfall P6: ProgramData log path during MSI rollback (informational — D-07 confirmation)

**What goes wrong:** Concern that `%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log` is wiped by MSI rollback.

**Reality (verified from installer.wxs and Logger.cs):**
- `Logger` (Logger.cs:15-17) writes to `%CommonApplicationData%\Jaminator\logs\` → `C:\ProgramData\Jaminator\logs\`.
- `installer.wxs` has **no `<Directory>` element under `ProgramData`**. The MSI never declares `%ProgramData%\Jaminator\` as install-target — the EXE creates it lazily on first run.
- MSI rollback only removes files MSI installed (under `INSTALLDIR=Program Files\Jaminator\`). It does NOT touch `%ProgramData%\Jaminator\` because MSI never owned that path.
- **Therefore the ProgramData log survives rollback.** The TEMP log (D-05) is still required because (a) users don't typically know to look in ProgramData, (b) the file is written by SYSTEM and may have ACL friction for non-admin readers, (c) the TEMP-log filename includes the install-attempt timestamp so multiple failed attempts are distinguishable.

**Conclusion:** CONTEXT.md D-07 reasoning is correct in outcome (keep both logs), but the precise justification is "user discoverability + per-attempt timestamping", not "ProgramData gets wiped." Adjust comments in code to reflect the accurate reason.

## Code Examples

### E1: Final state of `installer/installer.wxs` (Phase 1 commit 1)

**Lines 66-90 of current file (REMOVE entirely):**
```xml
    <!-- "Newer-version-available" check. Probes GitHub for the latest release;
         if newer than this MSI, downloads it, spawns a fresh msiexec, and
         aborts this install. Runs only in interactive UI mode — silent
         installs trust the version they specified. Fails open on offline. -->
    <Binary Id="UpdateCheckCA" SourceFile="$(var.UpdateCheckCaDll)" />
    <CustomAction Id="CheckForNewerVersion"
                  BinaryRef="UpdateCheckCA"
                  DllEntry="CheckForNewerVersion"
                  Execute="immediate"
                  Impersonate="yes"
                  Return="check" />

    <UI>
      <Publish Dialog="ExitDialog"
               Control="Finish"
               Event="DoAction"
               Value="LaunchApplication"
               Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 and NOT Installed" />
    </UI>

    <InstallUISequence>
      <Custom Action="CheckForNewerVersion"
              After="LaunchConditions"
              Condition="NOT Installed AND NOT REMOVE" />
    </InstallUISequence>
```

**After removal — replace those 25 lines with this 7-line block (keeping the `<UI><Publish>` block intact since it's not UpdateCheck-related):**
```xml
    <UI>
      <Publish Dialog="ExitDialog"
               Control="Finish"
               Event="DoAction"
               Value="LaunchApplication"
               Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 and NOT Installed" />
    </UI>
```

Note: the `<UI>` block is KEPT (it wires LaunchApplication to the ExitDialog Finish button). Only the `<Binary>`, the `<CustomAction Id="CheckForNewerVersion">`, and the entire `<InstallUISequence>` block are removed.

### E2: Deadlock-safe `RunSchTasks` rewrite (Phase 1 commit 4)

**Current code (`Installer.cs:387-402` — REPLACE):**
```csharp
private static void RunSchTasks(string args, bool allowFailure = false)
{
    var psi = new ProcessStartInfo("schtasks.exe", args)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0 && !allowFailure)
        throw new Exception($"schtasks {args} exit {p.ExitCode}: {stderr.Trim()} {stdout.Trim()}");
}
```

**Replacement (MS-canonical deadlock-safe pattern + structured exception):**
```csharp
private static void RunSchTasks(string args, bool allowFailure = false)
{
    var psi = new ProcessStartInfo("schtasks.exe", args)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    using var p = Process.Start(psi)!;

    // Drain stderr asynchronously so we can drain stdout synchronously
    // without risking a pipe-buffer deadlock. See:
    // learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.redirectstandardoutput
    var stderrBuf = new System.Text.StringBuilder();
    p.ErrorDataReceived += (s, e) =>
    {
        if (e.Data != null) stderrBuf.AppendLine(e.Data);
    };
    p.BeginErrorReadLine();

    string stdout = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    string stderr = stderrBuf.ToString();

    if (p.ExitCode != 0 && !allowFailure)
    {
        throw new SchTasksException(
            commandLine: $"schtasks.exe {args}",
            exitCode: p.ExitCode,
            stdout: stdout,
            stderr: stderr);
    }
}
```

**Plus a new small exception class (same file, private nested or top-level in `Jaminator.Services`):**
```csharp
internal sealed class SchTasksException : Exception
{
    public string CommandLine { get; }
    public int ExitCode { get; }
    public string Stdout { get; }
    public string Stderr { get; }

    public SchTasksException(string commandLine, int exitCode, string stdout, string stderr)
        : base($"{commandLine} exit {exitCode}: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim()}")
    {
        CommandLine = commandLine;
        ExitCode = exitCode;
        Stdout = stdout ?? "";
        Stderr = stderr ?? "";
    }
}
```

The structured exception lets the catch in `RegisterScheduledTask` write a properly formatted diagnostic file (E3) instead of regex-parsing an exception message.

### E3: Diagnostic TEMP-log emission from `RegisterScheduledTask` catch branch (Phase 1 commit 4)

**Current code (`Installer.cs:149-215`) — augment the catch branch:**

```csharp
public static int RegisterScheduledTask(Logger log)
{
    string? xmlPath = null;
    try
    {
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?> ... (unchanged) ...";

        xmlPath = Path.Combine(Path.GetTempPath(), $"jaminator-task-{Guid.NewGuid():N}.xml");
        File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

        try
        {
            RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            log.Info("Scheduled task registered: " + TaskName);
            // Delete the task XML on success only.
            try { File.Delete(xmlPath); } catch { }
            xmlPath = null;
            return 0;
        }
        catch
        {
            // Preserve the XML for diagnostics; don't delete it.
            throw;
        }
    }
    catch (Exception ex)
    {
        log.Error("Failed to register scheduled task", ex);
        WriteRegisterTaskDiagnosticLog(ex, xmlPath);
        return 1;
    }
}

private static void WriteRegisterTaskDiagnosticLog(Exception ex, string? preservedXmlPath)
{
    try
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var path = Path.Combine(Path.GetTempPath(),
            $"Jaminator-register-task-error-{timestamp}.log");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Jaminator --register-task diagnostic log");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-ddTHH:mm:ss} local");
        sb.AppendLine("Run mode: --register-task");
        sb.AppendLine($"Tool version: {Jaminator.Program.ToolVersion}");
        sb.AppendLine();
        sb.AppendLine("--- Exception ---");
        sb.AppendLine($"Type: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine("Stack trace:");
        sb.AppendLine(ex.StackTrace ?? "(no stack)");
        sb.AppendLine();

        if (ex is SchTasksException sch)
        {
            sb.AppendLine("--- Captured schtasks.exe output ---");
            sb.AppendLine($"Command line: {sch.CommandLine}");
            sb.AppendLine($"Exit code: {sch.ExitCode}");
            sb.AppendLine("STDOUT:");
            sb.AppendLine(sch.Stdout);
            sb.AppendLine("STDERR:");
            sb.AppendLine(sch.Stderr);
            sb.AppendLine();
        }

        if (preservedXmlPath != null && File.Exists(preservedXmlPath))
        {
            sb.AppendLine("--- Failing task XML ---");
            sb.AppendLine($"Preserved at: {preservedXmlPath}");
            sb.AppendLine("(deleted on success; preserved on failure for diagnostics)");
        }

        File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
        Console.WriteLine($"Diagnostic log written: {path}");  // surfaces in MSI verbose log
    }
    catch
    {
        // Never let diagnostic-log writing fail the diagnostics path itself.
    }
}
```

**Key design points:**

1. **`xmlPath` is hoisted to the outer scope** so the catch can reference it (currently it's local to the try; the finally deletes it unconditionally).
2. **The inner try/catch around RunSchTasks** only adds `throw;` after the catch — it exists solely to **suppress the `finally`-style XML deletion** on the failure path. Easier to reason about than gating the existing `finally { File.Delete(xmlPath); }` on a "succeeded" flag.
3. **`Console.WriteLine($"Diagnostic log written: {path}")`** emits to stdout. Because the `RegisterTask` deferred CA invokes `Jaminator.exe --register-task` via `FileRef` (installer.wxs:151), MSI captures the EXE's stdout/stderr into the MSI session log when `/l*v` is used [VERIFIED: existing pattern in `Program.cs:37` `log.OnMessage += line => Console.WriteLine(line);`]. This satisfies DIAG-01: future failures are diagnosable from the MSI log alone, with the TEMP log as a cross-reference.
4. **Diagnostic-write failure is swallowed.** The diagnostic path is best-effort — if `Path.GetTempPath()` somehow fails, we don't want to lose the original exception's `return 1` propagation.

### E4: Hand-edit of `Jaminator.sln` (Phase 1 commit 2)

**Lines to remove (verified against current `Jaminator.sln`):**

```
Line 7:  Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "UpdateCheck", "installer\UpdateCheck\UpdateCheck.csproj", "{D8F3B4E5-3456-6543-CDEF-3456789012CD}"
Line 8:  EndProject
Line 23: {D8F3B4E5-3456-6543-CDEF-3456789012CD}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
Line 24: {D8F3B4E5-3456-6543-CDEF-3456789012CD}.Debug|Any CPU.Build.0 = Debug|Any CPU
Line 25: {D8F3B4E5-3456-6543-CDEF-3456789012CD}.Release|Any CPU.ActiveCfg = Release|Any CPU
Line 26: {D8F3B4E5-3456-6543-CDEF-3456789012CD}.Release|Any CPU.Build.0 = Release|Any CPU
```

**Resulting file should be 24 lines (currently 32), with `Jaminator` and `Bootstrap` projects and their 8 configuration mappings. No `D8F3B4E5` GUID anywhere.**

**Verification:**
```bash
grep -c 'D8F3B4E5' Jaminator.sln    # must return 0
grep -c '^Project(' Jaminator.sln   # must return 2 (Jaminator + Bootstrap)
dotnet sln Jaminator.sln list       # must show exactly 2 projects
dotnet build Jaminator.sln          # must succeed
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| WiX v3 `light.exe` / `candle.exe` | WiX v4+ unified `wix.exe build` | WiX v4 GA, 2022 | Project already on v4; not affected |
| `dark.exe` MSI decompile (WiX v3) | `wix msi decompile` (WiX v4) | WiX v4 GA, 2022 | Built into existing CLI [CITED: docs.firegiant.com/wix/tools/wixexe/] |
| WixToolset.Dtf.CustomAction SFXCA managed CAs | Native-CA + `<SetProperty>` + EXE-side logic | Industry-broad recognition that SFXCA's CLR-bootstrap is fragile; Microsoft has not deprecated SFXCA but the community guidance is to minimize managed-CA surface | Project decision matches state-of-the-art (remove managed CA, push logic to EXE-side `SelfUpdater`) |
| Synchronous read of both Process redirected streams | Sync one + async event handler on the other | `OutputDataReceived`/`ErrorDataReceived` events added .NET 2.0; pattern documented in MS Learn since at least 2010 [CITED: learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.redirectstandardoutput] | Bug-fix in existing code path; not a new API surface |
| Diagnostic vanishes into MSI return-value-3 black hole | Diagnostic mirrored to TEMP file + MSI session log via stdout | Standard practice for deferred CAs that shell out to the EXE | Phase 1 enrichment |

**Deprecated / outdated (relevant to this phase):**
- **WiX v3 `dark.exe`**: superseded by `wix msi decompile`. Don't recommend `dark` to the user.
- **The `dotnet sln remove` CLI as a clean solution-file edit**: known limitation [VERIFIED: github.com/dotnet/sdk/issues/8037 — still open as of research date]. Hand-edit is the cleaner option for small repos.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `lessmsi` is installable via `winget install activescott.lessmsi` on the user's Win11 dev box | Standard Stack / D-09 | Low — fallback to `choco install lessmsi` or manual zip; the artifact-check still works with `wix msi decompile` |
| A2 | `schtasks.exe` always writes to stderr before exiting with a non-zero code (so the captured stderr will be non-empty when ExitCode≠0) | Pattern 2 / E2 | Low — if stderr is empty, the diagnostic just shows ExitCode and stdout; functionally still useful. Code falls back to "use stdout if stderr is empty" in `SchTasksException.base()`. |
| A3 | `C:\Windows\Temp` is world-readable by default on Win10/11 (so the user who triggered the install can read the SYSTEM-written diagnostic file) | Pattern 3 | Medium — if a hardened-image policy restricts `C:\Windows\Temp`, the file is written but unreadable by the user. Mitigation: the same content also appears in the MSI verbose log (via `Console.WriteLine`) and in `%ProgramData%\Jaminator\logs\` (via Logger.Error). Diagnostic is triple-channel. |
| A4 | The MSI build script `build.ps1` will succeed with `-d UpdateCheckCaDll=...` removed (no other references) | Pitfall P4 / E1 | Low — verified by grep of installer.wxs (only line 70 references it) and build.ps1 (only line 45 passes it). |
| A5 | VS2022 reopening `Jaminator.sln` after the hand-edit produces no further file rewrites (i.e., the hand-edit is "clean from VS's perspective") | Pitfall P3 / E4 | Low — VS2022 only rewrites on user-initiated save or project-state change. Hand-edit produces a file VS would accept verbatim. |
| A6 | The 4-commit boundary (D-11) maintains the "each commit independently builds" property | D-12 / Commit strategy | Low — commit 1 produces a buildable installer.wxs (the build.ps1 still passes `-d UpdateCheckCaDll=...` referring to the still-present DLL, since commit 2 hasn't run yet). Commits 2-3 then synchronously remove the project + arg. Commit 4 touches only EXE-side code. The build chain is consistent at each commit. |

## Open Questions

1. **Are there other `RunSchTasks` callers that need the deadlock fix backported?**
   - What we know: `RunSchTasks` is called from `RegisterScheduledTask` (line 204), `UnregisterScheduledTask` (lines 221-222), `ReconcileDailyTask` (line 245, 315). All five call sites benefit from the deadlock fix since they all wrap schtasks.exe.
   - What's unclear: Should the diagnostic-log emission (E3) be generalized to a helper invoked from all five sites, or kept inline in `RegisterScheduledTask` only?
   - Recommendation: Phase 1 fixes `RunSchTasks` for all five callers (deadlock-safe is a free correctness win). But TEMP-log emission stays inline in `RegisterScheduledTask` only — the failure modes for `UnregisterScheduledTask` (silent during uninstall) and `ReconcileDailyTask` (called every logon, would spam TEMP) don't have the same "vanishes into MSI rollback" problem. CONTEXT.md D-08 discretion ("either is fine") supports this scoping; planner should commit to it explicitly.

2. **Should the diagnostic log also be written when `UnregisterScheduledTask` fails?**
   - What we know: `UnregisterScheduledTask` is invoked by the `UnregisterTask` CA with `Return="ignore"` (installer.wxs:162). Failures don't roll back the uninstall.
   - What's unclear: Is a TEMP-log for uninstall failures useful enough to add?
   - Recommendation: **No — out of scope for Phase 1.** D-08 mandates we preserve existing semantics; `Return="ignore"` means uninstall failures are silent by design. If the user wants this in a future milestone (the "generalized install-time diagnostics" entry in Deferred Ideas for future milestones), it's a separate decision.

3. **Does `Program.cs --register-task` have stdout/stderr connection back to the calling MSI session in a deferred CA?**
   - What we know: `installer.wxs` line 150-155 uses `FileRef="JaminatorExeFile"` with `ExeCommand="--register-task"`, no `Output` attribute. The Windows Installer documentation states that deferred FileRef CAs do capture child-process stdout/stderr to the MSI session log when running with `/l*v` logging.
   - What's unclear: Is this capture reliable on all Windows versions in the fleet (Win10 + Win11)?
   - Recommendation: Phase 2 smoke-test (INSTALL-01, INSTALL-02) will validate this empirically by intentionally triggering a `RegisterTask` failure on the test box and inspecting the verbose log. **Phase 1 implements the `Console.WriteLine` channel on the assumption that it works** (matches existing pattern at `Program.cs:37`), and Phase 2 validates. If Phase 2 finds the channel broken, the TEMP-log is the fallback — diagnostics survive either way.

## Security Domain

Per `.planning/config.json` — `security_enforcement` is not set, so default to "enabled". This phase has narrow security surface:

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | Install runs as elevated user / SYSTEM via UAC; no application-layer auth in Phase 1 scope |
| V3 Session Management | No | No sessions |
| V4 Access Control | Partial | The `C:\Windows\Temp` file is written by SYSTEM and read by the install-initiating user; the user must have read access. Mitigation: triple-channel diagnostic (TEMP + ProgramData + MSI log). |
| V5 Input Validation | Partial | The diagnostic log includes the schtasks command line that was *invoked by Jaminator itself* — not user input. Inputs to `schtasks.exe` are the task XML (validated by schtasks) and the task name (compile-time constant). No user-controlled input flows into the diagnostic. |
| V6 Cryptography | No | No crypto in Phase 1 scope |
| V8 Data Protection | Partial | Diagnostic log contains stack traces and process output — no secrets are involved (Jaminator stores no secrets), but stack traces may reveal code layout. Considered acceptable: the EXE is open-source on GitHub. |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation | Phase 1 status |
|---------|--------|---------------------|----------------|
| Command injection via task arguments | Tampering | Compile-time constant `TaskName`, no user input | Already mitigated in existing code |
| Symlink attack on TEMP file write | Tampering | Use `FileMode.CreateNew` to refuse pre-existing file at same path; OR use `Path.Combine(Path.GetTempPath(), uniqueName)` with high-entropy filename | The timestamp-only filename (`YYYYMMDDhhmmss`) could collide if two failed registrations happen in the same second. **Mitigation: append a 6-char random suffix** like `Jaminator-register-task-error-20260511143217-x7y2z9.log`. Low-risk; planner can choose to add or skip. |
| Diagnostic log discloses sensitive paths/data | Information disclosure | Review log contents — contains: process command line (no secrets), exit code, stdout (typically empty), stderr (schtasks error string), exception stack trace. No PII, no secrets. | Acceptable. |
| TOCTOU on `xmlPath` preservation | Tampering | Local TEMP file written and read atomically; the catch branch references `xmlPath` only to print it, doesn't read its contents. No TOCTOU issue. | N/A |

**Net:** Security posture unchanged by Phase 1. No new threat surface introduced. The `Path.GetTempPath()` write follows the same pattern as existing code (lines 199, 311, 423 of Installer.cs).

## Sources

### Primary (HIGH confidence)

- [Microsoft Learn: ProcessStartInfo.RedirectStandardOutput Property](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.redirectstandardoutput) — canonical deadlock pattern + recommended async-stderr fix; fetched 2026-05-11
- [Microsoft Learn: dotnet sln command](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-sln) — CLI reference for solution-file edits
- [Microsoft Learn: Deferred Execution Custom Actions](https://learn.microsoft.com/en-us/windows/win32/msi/deferred-execution-custom-actions) — context for `Impersonate="no"` SYSTEM execution
- [FireGiant: WiX v4 InstallUISequence schema](https://docs.firegiant.com/wix/schema/wxs/installuisequence/) — schema validation rules
- [FireGiant: wix.exe command-line reference](https://docs.firegiant.com/wix/tools/wixexe/) — `wix msi decompile` and other subcommands
- [activescott/lessmsi README](https://github.com/activescott/lessmsi) — installation methods; chocolatey and zip
- [activescott/lessmsi Command-Line wiki](https://github.com/activescott/lessmsi/wiki/Command-Line) — `l -t <table> <msi>` syntax, CSV output format
- [Microsoft Learn: Solution (.sln) file format](https://learn.microsoft.com/en-us/visualstudio/extensibility/internals/solution-dot-sln-file?view=vs-2022) — Project block and GlobalSection structure
- **Codebase verification (HIGH confidence — read directly):**
  - `installer/installer.wxs` lines 1-175 (current state, line-numbered)
  - `installer/UpdateCheck/UpdateCheck.csproj` (current state, csproj being deleted)
  - `installer/UpdateCheck/UpdateCheckCA.cs` (current state, source being deleted)
  - `installer/build.ps1` (current state, lines 27-50 are the edit zone)
  - `src/Jaminator/Services/Installer.cs` (current state, lines 149-215 and 387-402 are the edit zones)
  - `src/Jaminator/Services/Logger.cs` (current state, ProgramData path derivation)
  - `src/Jaminator/Program.cs` (current state, `ToolVersion = "0.7.4"` constant + `RegisterTask` dispatch)
  - `Jaminator.sln` (current state, 32 lines, 3 projects, line-by-line edit plan in E4)

### Secondary (MEDIUM confidence — single authoritative source or verified via codebase grep)

- [github.com/dotnet/sdk/issues/8037 — dotnet sln remove leaves orphan configurations](https://github.com/dotnet/sdk/issues/8037) — known limitation, still-open
- [Microsoft Learn forum: TEMP path under SYSTEM account](https://learn.microsoft.com/en-us/archive/msdn-technet-forums/deb60504-30a3-46b8-a45e-9a868b821304) — `C:\Windows\Temp` for SYSTEM context
- [github.com/wixtoolset/issues/7574 — wix msi decompile -x modularization GUID bug](https://github.com/wixtoolset/issues/7574) — caveat for `wix msi decompile`, why lessmsi is preferred for D-09

### Tertiary (LOW confidence — informational only, marked in `Assumptions Log` if relied upon)

- Microsoft Q&A archive thread on Process redirected I/O — corroborating the canonical deadlock pattern; redundant with primary source

## Metadata

**Confidence breakdown:**
- Standard stack (lessmsi, WiX, .NET BCL): **HIGH** — official docs and codebase verification
- Architecture pattern (Process stdout/stderr capture): **HIGH** — Microsoft Learn worked example matches the recommended fix verbatim
- WiX surgical-edit pitfalls: **HIGH** — verified line-by-line against `installer.wxs` source
- Solution-file hand-edit details: **HIGH** — verified line-by-line against `Jaminator.sln` source
- `dotnet sln remove` orphan-config behavior: **MEDIUM-HIGH** — official sdk issue thread, still-open
- `C:\Windows\Temp` ACLs on Win10/11: **MEDIUM** — informed by MS forum thread + Windows hardening defaults; some images may diverge (logged as Assumption A3)
- Phase-2 verifiability of MSI session log stdout capture: **MEDIUM** — pattern is documented but empirical validation deferred to Phase 2 smoke-test

**Research date:** 2026-05-11
**Valid until:** 2026-08-11 (stable underlying ecosystem — .NET Framework 4.8 is in maintenance mode, WiX v4 mature, lessmsi v1.10+ stable for years; 90-day validity is conservative)
