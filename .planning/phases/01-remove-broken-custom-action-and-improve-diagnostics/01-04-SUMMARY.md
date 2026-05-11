---
phase: 01-remove-broken-custom-action-and-improve-diagnostics
plan: 04
subsystem: installer
tags:
  - diagnostics
  - process-deadlock-fix
  - register-task
  - schtasks
  - msi-deferred-ca
requires:
  - .planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-CONTEXT.md
  - .planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-RESEARCH.md
  - .planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-04-PLAN.md
provides:
  - "src/Jaminator/Services/Installer.cs: deadlock-safe schtasks wrapper + TEMP-log diagnostics on RegisterTask failure (DIAG-01)"
affects:
  - Installer.RegisterScheduledTask failure-path observability (3-channel: TEMP log + ProgramData log + MSI verbose log)
  - All five callers of RunSchTasks (RegisterScheduledTask, UnregisterScheduledTask x2, ReconcileDailyTask x2) — free deadlock fix
tech-stack:
  added: []
  patterns:
    - "MS-canonical deadlock-safe Process I/O: async stderr via ErrorDataReceived + BeginErrorReadLine, sync stdout via ReadToEnd, then WaitForExit"
    - "Structured exception (SchTasksException) carrying process forensic data instead of stringified Exception"
    - "Best-effort diagnostic-log writer wrapped in outer catch{} — secondary failures never override primary return code"
key-files:
  created: []
  modified:
    - "src/Jaminator/Services/Installer.cs (+99/-6 lines; 433 -> 526 lines)"
decisions:
  - "Honored D-05: TEMP-log filename Jaminator-register-task-error-YYYYMMDDhhmmss.log under Path.GetTempPath() (resolves to C:\\Windows\\Temp under SYSTEM context)"
  - "Honored D-06: RunSchTasks captures stdout/stderr; on non-zero exit throws structured SchTasksException with all four fields (CommandLine, ExitCode, Stdout, Stderr)"
  - "Honored D-07: log.Error('Failed to register scheduled task', ex) RETAINED — TEMP log is additive, not replacement"
  - "Honored D-08: catch branch still returns 1 — Return='check' MSI rollback semantics unchanged"
  - "Honored D-10: Program.cs::ToolVersion stays at '0.7.4' — no version bump in Phase 1"
  - "Honored D-11 commit 4: single commit 'feat(installer): emit register-task failure diagnostics to TEMP log' spans both file-level tasks of this plan"
  - "Did NOT modify ReconcileDailyTask or UnregisterScheduledTask per plan <action>: they retain their existing catch/finally semantics. They get the deadlock fix transparently because RunSchTasks is shared."
metrics:
  duration_minutes: 2
  completed: 2026-05-11T12:18Z
  tasks_completed: 3
  files_changed: 1
  commits: 1
---

# Phase 1 Plan 04: RegisterTask Failure Diagnostics (DIAG-01) Summary

DIAG-01 closed: Installer.RegisterScheduledTask now produces three-channel actionable failure output (TEMP log + ProgramData log + MSI verbose log breadcrumb) when the deferred RegisterTask custom action fails, replacing the v0.7.4 silent-rollback failure mode. Incidentally fixed a latent stdout/stderr deadlock in RunSchTasks that affected all five schtasks call sites.

## What Was Built

**One commit, one file modified:** `src/Jaminator/Services/Installer.cs` (+99/-6, 433 -> 526 lines).

### 1. Deadlock-safe RunSchTasks (Task 1)
Replaced lines 387-402. The old implementation did `p.StandardOutput.ReadToEnd()` followed by `p.StandardError.ReadToEnd()` — the canonical Microsoft-documented deadlock pattern (RESEARCH.md Pitfall P2). Replaced with:
- Async stderr drain via `p.ErrorDataReceived += ... + p.BeginErrorReadLine()`
- Sync stdout drain via `p.StandardOutput.ReadToEnd()`
- `p.WaitForExit()` last
- On non-zero exit (and `!allowFailure`): `throw new SchTasksException(commandLine, exitCode, stdout, stderr)`

This is a free secondary correctness win: **all five RunSchTasks call sites benefit** — RegisterScheduledTask, UnregisterScheduledTask (x2), ReconcileDailyTask (x2). Per RESEARCH.md Open Question 1.

### 2. SchTasksException class (Task 1)
New `internal sealed class SchTasksException : Exception` placed sibling to `Installer` inside namespace `Jaminator.Services`. Carries `CommandLine`, `ExitCode`, `Stdout`, `Stderr` properties. Base message format: `"{commandLine} exit {exitCode}: {stderr or stdout}"`. Existing `catch (Exception ex)` blocks in UnregisterScheduledTask (line 226) and ReconcileDailyTask (line 318) catch it transparently via polymorphism — no behavioral change for those callers.

### 3. Enriched RegisterScheduledTask (Task 2)
- `string? xmlPath = null;` hoisted to outer scope so the catch can reference it for diagnostics.
- Task-XML body is byte-identical to pre-edit (verbatim copy of the original 45-line `$@""` literal).
- Inner try around `RunSchTasks` call: success path explicitly deletes the XML and sets `xmlPath = null`; inner `catch { throw; }` re-throws WITHOUT deleting the XML (preserves it for forensics).
- Outer catch retains `log.Error("Failed to register scheduled task", ex)` (D-07) and adds `WriteRegisterTaskDiagnosticLog(ex, xmlPath)` before `return 1` (D-08 — rollback semantics unchanged).

### 4. WriteRegisterTaskDiagnosticLog helper (Task 2)
New private static method inside `Installer`. Filename: `Path.Combine(Path.GetTempPath(), $"Jaminator-register-task-error-{yyyyMMddHHmmss}.log")` — resolves to `C:\Windows\Temp\...` under SYSTEM context (which is how the deferred CA runs per `installer.wxs` `Impersonate="no"`). Content sections:
1. Header: timestamp, run mode (`--register-task`), tool version (`Jaminator.Program.ToolVersion` -> `"0.7.4"`).
2. Exception block: type, message, stack trace.
3. Captured schtasks output (only when `ex is SchTasksException sch`): command line, exit code, stdout, stderr.
4. Preserved task-XML reference (only when the file still exists on disk).

Emits `Console.WriteLine($"Diagnostic log written: {path}")` so `msiexec /l*v` captures the path in the MSI verbose log. Entire helper body is wrapped in `try { ... } catch { /* never let diagnostic-log writing fail the diagnostics path itself */ }` — secondary failures cannot override the primary `return 1` propagation.

## Behavior Contract

- **Happy path (byte-identical to pre-edit):** register task -> `log.Info("Scheduled task registered: Jaminator-Login")` -> delete XML -> `return 0`.
- **Failure path (NEW):** RunSchTasks throws SchTasksException -> inner catch re-throws WITHOUT deleting XML -> outer catch: writes ProgramData log line (UNCHANGED) -> writes TEMP-log file (NEW) -> emits `Console.WriteLine` breadcrumb (NEW) -> `return 1` -> MSI rolls back per `Return="check"` (UNCHANGED).

Three log artifacts on failure:
1. `C:\Windows\Temp\Jaminator-register-task-error-YYYYMMDDhhmmss.log` (NEW — discoverable by technician)
2. `%ProgramData%\Jaminator\logs\jaminator-YYYYMMDD.log` (UNCHANGED — survives if rollback doesn't nuke ProgramData)
3. MSI verbose log (via `Console.WriteLine` -> deferred-CA stdout capture; requires `msiexec /l*v <path>`)

## Acceptance Criteria

### Task 1 grep manifest

| Criterion | Pattern | Expected | Actual | Status |
|-----------|---------|----------|--------|--------|
| T1.1 | `BeginErrorReadLine` | 1 | 1 | PASS |
| T1.2 | `ErrorDataReceived` | 1 | 1 | PASS |
| T1.3 | `class SchTasksException` | 1 | 1 | PASS |
| T1.4 | `throw new SchTasksException` | 1 | 1 | PASS |
| T1.5 | python regex for sync-stdout-then-sync-stderr deadlock pattern | no match | no match | PASS |
| T1.6 | `private static void RunSchTasks` | 1 | 1 | PASS |
| T1.7 | `using System.Diagnostics;` | 1 | 1 | PASS |

### Task 2 grep manifest

| Criterion | Pattern | Expected | Actual | Status |
|-----------|---------|----------|--------|--------|
| T2.1 | `WriteRegisterTaskDiagnosticLog` | 2 | 2 (1 def + 1 call) | PASS |
| T2.2 | `Jaminator-register-task-error-` | 1 | 1 | PASS |
| T2.3 | `Failed to register scheduled task` | 1 | 1 (D-07 retained) | PASS |
| T2.4 | `return 1;` | >= 2 | 3 | PASS |
| T2.5 | `string? xmlPath = null;` | 1 | 1 | PASS |
| T2.6 | `Diagnostic log written` | 1 | 1 | PASS |
| T2.7 | `finally { try { File.Delete(xmlPath); } catch { } }` whole-file | 0 | 1 | DEVIATION (see below — semantic intent passes; literal whole-file gate is overly broad) |
| T2.8 | `Jaminator.Program.ToolVersion` | 1 | 1 | PASS |

### Task 3 — dotnet build

```
$ dotnet build src/Jaminator/Jaminator.csproj -c Release
  Restored .../src/Jaminator/Jaminator.csproj (in 336 ms).
  Jaminator -> .../src/Jaminator/bin/Release/net48/Jaminator.exe

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.26
exit code: 0
```

**Status: PASS (preferred path — `dotnet build` succeeded on WSL; no deferral to Plan 3 needed).**

WSL `dotnet 10.0.107` resolved net48 reference assemblies cleanly via the project's existing `PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" v1.0.3`. The Linux/WSL deferral fallback path in Task 3's acceptance criteria was therefore unnecessary.

## Deviations from Plan

### Auto-fixed Issues
None.

### Acceptance-criterion DEVIATION (semantic intent honored; literal gate adjusted)

**1. [Rule 3 — Blocking-issue equivalent — acceptance gate overly broad]** Task 2 criterion #7 says `grep -c 'finally { try { File.Delete(xmlPath); } catch { } }' src/Jaminator/Services/Installer.cs` must return `0`. **Actual: 1.**

- **Why:** The semantic intent of #7 was "the unconditional-delete-in-finally pattern inside `RegisterScheduledTask` is gone" — to prove Pitfall P5/E3 design point #2 (failure path preserves the XML). I verified that scoped check:
  ```
  $ awk '/public static int RegisterScheduledTask/,/^        }$/' src/Jaminator/Services/Installer.cs \
    | grep -c 'finally { try { File.Delete(xmlPath); } catch { } }'
  0
  ```
  Within `RegisterScheduledTask` the pattern is 0. The remaining match (line 377) is inside `ReconcileDailyTask`, which the plan's `<action>` block explicitly tells me NOT to modify ("DO NOT change the other callers"). `ReconcileDailyTask` is a separate method handling the Daily Run All task; it has its own try/catch/finally with a `log.Warn(...)` catch that does NOT need TEMP-log diagnostics (Phase 1 scope is limited to `--register-task` per D-05).
- **Outcome:** Semantic gate passes; literal whole-file gate doesn't because of the unrelated (and correctly preserved) `ReconcileDailyTask` finally. No code change needed — this is an acceptance-criterion scoping issue, not an implementation bug.
- **Files modified:** none (deviation is documentation-only).
- **Commit:** N/A (no code change).

### Auth gates
None.

## Threat Surface Scan

No new security surface introduced beyond what `<threat_model>` in the plan already enumerated. STRIDE register fully covered by T-01-12 through T-01-16:

- T-01-12 (DoS via RunSchTasks deadlock) — **MITIGATED.** Acceptance criterion 5 (regex absence of back-to-back sync ReadToEnd) confirmed.
- T-01-13 (info disclosure via world-readable TEMP log) — accepted per CONTEXT.md (no PII, no secrets; Jaminator stores no secrets).
- T-01-14 (TOCTOU on predictable TEMP-log filename) — accepted for v0.7.5 hotfix; revisit in Milestone 3 hardening.
- T-01-15 (info disclosure via preserved task XML referenced from TEMP log) — accepted (task XML contains EXE path + working dir + LogonTrigger only).
- T-01-16 (silent secondary failure in diagnostic-log writer) — intentional by design (outer `catch { }`).

No threat flags raised — Phase 1's threat model fully covers this plan's surface.

## Known Stubs

None.

## Self-Check: PASSED

- `src/Jaminator/Services/Installer.cs` FOUND (526 lines, includes RunSchTasks rewrite + SchTasksException + RegisterScheduledTask enrichment + WriteRegisterTaskDiagnosticLog).
- Commit `5485d05` FOUND in git log:
  ```
  $ git log --oneline -3
  5485d05 feat(installer): emit register-task failure diagnostics to TEMP log
  87eaec6 docs(state): record phase 1 planning completion
  736254e docs(phase-01): plan installer reliability hotfix (4 plans, 3 waves)
  ```
- `dotnet build` artifact FOUND at `src/Jaminator/bin/Release/net48/Jaminator.exe` (197k).
- `Program.cs::ToolVersion` STILL `"0.7.4"` (D-10 honored — no version bump).
- No untracked files. No unexpected deletions in commit `5485d05`.
- All 15 file-level grep assertions PASS (with the one DEVIATION on criterion T2.7 documented and semantically validated above).
- Task 3 `dotnet build` PASS (preferred path — no deferral needed).

## Notes for Downstream Plans

- **Plan 01-02 (Wave 2):** When it removes the UpdateCheck custom-action project from `Jaminator.sln`, the solution build should still pass — this plan's changes are self-contained in `src/Jaminator/Services/Installer.cs` and don't touch any sln-level project structure.
- **Plan 01-03 (Wave 3):** Its Windows-side checkpoint runs `dotnet build Jaminator.sln -c Release` and a manual MSI smoke install. The TEMP-log emission path is now in place; the smoke install will exercise the happy path. Triggering the failure path requires an intentional failure (e.g., pre-creating the task with no `/F`) — Phase 2's INSTALL-01 work can choose whether to add that as a regression check.
- **Phase 2 (DIAG-02):** When `docs/INSTALL-LOGGING.md` is written, the canonical filename to document is `C:\Windows\Temp\Jaminator-register-task-error-YYYYMMDDhhmmss.log` — discoverable by technicians without admin tooling.
- **Phase 3 (RELEASE-02):** Bump `Program.cs::ToolVersion` to `"0.7.5"` then. This plan deliberately left it at `"0.7.4"` per D-10.
