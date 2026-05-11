# Phase 1: Remove broken custom action and improve diagnostics - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-11
**Phase:** 01-remove-broken-custom-action-and-improve-diagnostics
**Mode:** `--auto` (recommended option auto-selected for every gray area, no interactive prompts)
**Areas discussed:** UpdateCheck removal strategy, RegisterTask diagnostics, Build verification, Commit strategy

---

## UpdateCheck removal strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Delete entirely | Remove `installer/UpdateCheck/` directory, .sln entry, and build.ps1 refs in one cleanup. Git history + v0.7.5 release notes carry the rationale. | ✓ |
| Tombstone | Delete code but keep dir with a `TOMBSTONE.md` explaining why the CA was removed. | |
| Keep dir, stop building | Leave the project in tree but unhook it from .sln and build.ps1. | |

**Auto-selected:** Delete entirely (recommended).
**Rationale:** Tombstone files in-tree become confusing dead-text that future maintainers misinterpret. Git log on `installer/UpdateCheck/` + the v0.7.5 release notes are the authoritative record of the "why."

---

## RegisterTask diagnostics (DIAG-01)

| Option | Description | Selected |
|--------|-------------|----------|
| TEMP-path log file | Enrich `Installer.cs::RegisterScheduledTask` to write `%TEMP%\Jaminator-register-task-error-YYYYMMDDhhmmss.log` with full exception, schtasks output, and task XML path. Documented in release notes. | ✓ |
| PowerShell wrapper → MSI session | Wrap the deferred CA in PowerShell that captures stdout/stderr and writes back to the MSI session via WiX `SetProperty`. | |
| New managed CA wrapper | Add a new managed CA (with the proper `CustomAction.config` this time) to host richer diagnostics. | |

**Auto-selected:** TEMP-path log file (recommended).
**Rationale:** Deferred `FileRef` CAs (which `RegisterTask` is) can't write MSI session properties directly — option 2 is technically blocked. Adding a new managed CA (option 3) reintroduces the exact SFXCA surface we're removing in this phase, defeating the architectural goal. The TEMP log is simple, discoverable, uses existing logging infrastructure, and can be documented in the release notes for diagnostic capture.

---

## Build verification

| Option | Description | Selected |
|--------|-------------|----------|
| Build + MSI inspection | `installer/build.ps1` must succeed AND a tool like `lessmsi list` or `wix decompile` confirms zero hits for `UpdateCheckCA`, `CheckForNewerVersion`, `Wix4DTFCustomAction` in the produced MSI. | ✓ |
| Build exit code only | Trust `installer/build.ps1`'s exit code; no artifact inspection. | |
| Install on dev machine | Run actual install/uninstall as part of Phase 1 verification. | |

**Auto-selected:** Build + MSI inspection (recommended).
**Rationale:** Exit code alone doesn't prove the CA was actually stripped from the produced MSI — a partial removal could still produce a "clean" build. Inspection at the artifact level catches that. End-to-end install testing belongs in Phase 2 (it's the explicit goal of that phase).

---

## Commit strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Atomic commits per file-area | (a) installer.wxs edits, (b) .sln + build.ps1 cleanup, (c) `rm -r installer/UpdateCheck/`, (d) Installer.cs diagnostics — each independently revertable and buildable. | ✓ |
| One bundled commit | Single "remove UpdateCheck CA, harden RegisterTask" commit. | |
| Two commits | One for removal, one for diagnostics. | |

**Auto-selected:** Atomic commits per file-area (recommended).
**Rationale:** Surgical commits make Phase 1 trivially bisectable if Phase 2 smoke-testing surfaces an unexpected regression. With one bundled commit we'd lose the ability to roll back just the diagnostics changes (or just the WiX edits) without disturbing the others.

---

## Claude's Discretion

- Exact technique for capturing `schtasks.exe` stdout/stderr in the `RunSchTasks` helper — synchronous vs async reads, `Process.StandardOutput.ReadToEndAsync()` vs `OutputDataReceived` event handlers. The contract is "captured output appears in the catch's exception message"; implementation detail is left to the planner.
- Choice of MSI inspection tool (`lessmsi`, `wix decompile`, `msidump`, Orca CLI) — depends on what is installable / already present on the user's Windows dev environment. Planner picks at phase plan time.
- Whether to extract a shared `WriteDiagnosticLog(area, ex)` helper or inline the TEMP-log write in `RegisterScheduledTask` only. Either is acceptable for this hotfix; generalising across all entry points is a Milestone 3 candidate.

## Deferred Ideas

- **For Phase 2:** Write `docs/INSTALL-LOGGING.md` documenting the `%TEMP%\Jaminator-register-task-error-*.log` filename + the `msiexec /i Jaminator.msi /l*v <path>` capture procedure (DIAG-02).
- **For Phase 2:** End-to-end install verification on clean Windows 11 + Windows 10 boxes (INSTALL-01, INSTALL-02), silent-install regression (INSTALL-04), SelfUpdater chain (INSTALL-05).
- **For Phase 3:** Version bump to `0.7.5` in `Program.cs::ToolVersion`, tag, GitHub Release, release notes.
- **For Milestone 3:** Generalise install-time diagnostics across all entry points; CI / automated installer regression test (HARDEN-05); reconsider whether install-time update check should ever return — only as a native (C++) CA if at all.
