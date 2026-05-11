---
phase: 01-remove-broken-custom-action-and-improve-diagnostics
plan: 01
subsystem: installer
tags: [wix, msi, custom-action, sfxca, installer.wxs]

requires:
  - phase: 00-planning
    provides: Phase 1 plan + research locking D-04 (delete three WiX elements) and D-11 commit 1
provides:
  - WiX source with zero CheckForNewerVersion / UpdateCheckCA / UpdateCheckCaDll surface
  - InstallUISequence table eliminated from the MSI's eventual binary (no empty stub left behind)
  - LaunchApplication, RegisterTask, UnregisterTask, WixUI_Minimal wiring preserved verbatim
affects:
  - 01-02 (build script + solution cleanup — now safe to drop -d UpdateCheckCaDll and the .sln reference)
  - 01-03 (UpdateCheck/ directory deletion — WiX source no longer references it)
  - 01-04 (Installer.cs diagnostics — disjoint file, no interaction)

tech-stack:
  added: []
  patterns:
    - "Surgical WiX-element removal with grep-based acceptance criteria for every preserved element"
    - "XML well-formedness gate via python xml.etree (xmllint-free fallback)"

key-files:
  created:
    - .planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-01-SUMMARY.md
  modified:
    - installer/installer.wxs

key-decisions:
  - "D-04 honoured exactly: removed comment block + <Binary Id='UpdateCheckCA'> + <CustomAction Id='CheckForNewerVersion'> + entire <InstallUISequence> (not an empty stub)"
  - "Collapsed surrounding blank lines so the file reads naturally; line count 157 (1 below plan's 158-165 band — within blank-line tolerance the plan explicitly granted)"
  - "Pre-existing baseline grep for WixUI_Minimal was 3 (lines 34/49/51, two are comments), not 1 as Criterion 6 stated — the criterion's intent (element preserved) is satisfied; relaxed strict equality to >=1"

patterns-established:
  - "WiX-source surgery: use Edit tool with multi-line old_string anchored to elements above and below the deletion target, so the change is self-locating"
  - "Acceptance criteria as grep counts + python xml.etree parse — works on WSL without xmllint"

requirements-completed:
  - INSTALL-03

duration: 1m 30s
completed: 2026-05-11
---

# Phase 1 Plan 01-01: Remove CheckForNewerVersion CA from WiX Source — Summary

**Surgically removed three WiX elements (Binary, CustomAction, InstallUISequence) from `installer/installer.wxs` so future MSI builds will not declare the broken SFXCA managed custom action that rolls back v0.7.4 installs — every unrelated element preserved verbatim.**

## Performance

- **Duration:** 1m 30s
- **Started:** 2026-05-11T12:15:17Z
- **Completed:** 2026-05-11T12:16:44Z
- **Tasks:** 1 / 1
- **Files modified:** 1 (`installer/installer.wxs`)

## Accomplishments

- Deleted the `<!-- "Newer-version-available" check ... -->` comment block + `<Binary Id="UpdateCheckCA" SourceFile="$(var.UpdateCheckCaDll)" />` (Edit A, originally lines 66-70)
- Deleted `<CustomAction Id="CheckForNewerVersion" BinaryRef="UpdateCheckCA" DllEntry="CheckForNewerVersion" ...>` (Edit A continuation, originally lines 71-76)
- Deleted the entire `<InstallUISequence><Custom Action="CheckForNewerVersion".../></InstallUISequence>` block (Edit B, originally lines 86-90) — not left as an empty `<InstallUISequence/>` stub, per RESEARCH Pitfall P1 and threat T-01-03
- Preserved verbatim: `LaunchApplication` CA (lines 61-64), `<UI><Publish Dialog="ExitDialog" .../></UI>` (now lines 66-72) wiring Finish→LaunchApplication, `WixUI_Minimal`, all `<ComponentGroup>` blocks, `RegisterTask`, `UnregisterTask`, and `<InstallExecuteSequence>`
- Confirmed XML well-formedness via `python3 -c "import xml.etree.ElementTree as E; E.parse(...)"` — exit 0
- File size: 175 → 157 lines (-18 lines, net of blank-line collapsing)

## Task Commits

1. **Task 1: Remove CheckForNewerVersion CA elements from installer.wxs** — `6a97982` (fix)

**Plan metadata commit:** committed alongside SUMMARY.md write in the orchestrator-driven flow (see worktree-mode commit below).

## Files Created/Modified

- `installer/installer.wxs` — 18 lines removed (three element groups); `LaunchApplication` / `<UI>` / `<StandardDirectory>` / `RegisterTask` / `UnregisterTask` / `<InstallExecuteSequence>` flow preserved
- `.planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-01-SUMMARY.md` — created (this file)

## Acceptance Criteria Results

| # | Criterion | Expected | Actual | Result |
|---|-----------|----------|--------|--------|
| 1 | `grep -c 'UpdateCheckCA' installer.wxs` | 0 (was 2) | **0** | PASS |
| 2 | `grep -c 'CheckForNewerVersion' installer.wxs` | 0 (was 3) | **0** | PASS |
| 3 | `grep -c 'UpdateCheckCaDll' installer.wxs` | 0 (was 1) | **0** | PASS |
| 4 | `grep -c '<InstallUISequence' installer.wxs` | 0 (was 1) | **0** | PASS (no empty stub) |
| 5 | `grep -c 'LaunchApplication' installer.wxs` | >= 2 | **2** | PASS |
| 6 | `grep -c 'WixUI_Minimal' installer.wxs` | (plan said 1) | **3** | PASS (intent: preserved — see note) |
| 7 | `grep -c 'RegisterTask' installer.wxs` | >= 2 | **2** | PASS |
| 8 | `grep -c 'UnregisterTask' installer.wxs` | >= 2 | **2** | PASS |
| 9 | `grep -c '<InstallExecuteSequence>' installer.wxs` | 1 | **1** | PASS |
| 10 | `python xml.etree.ElementTree.parse` exit 0 | 0 | **0** | PASS |
| 11 | line count 158-165 | 158-165 | **157** | MINOR VARIANCE (-1; within blank-line tolerance) |

Note on Criterion 6: the plan asserts `grep -c 'WixUI_Minimal'` returns 1, but the baseline before edits was already 3 — the token appears in two comment lines (34, 49) plus the actual `<ui:WixUI Id="WixUI_Minimal" />` element (line 51). None of those three lines were touched by this plan. The criterion's stated *intent* is "confirms WixUI element preserved," which is satisfied: the element at line 51 is untouched. Treated as a plan-text typo, not a deviation.

Note on Criterion 11: 157 vs expected 158-165 is a 1-line under-count driven by collapsing the blank line that originally sat between the deleted comment block and the `<UI>` element. The plan explicitly says "exact count depends on blank-line treatment" — within that tolerance, but flagged for transparency.

## Decisions Made

- **None new beyond the plan.** Followed D-04 (delete three WiX elements) and D-11 commit 1 (commit message: `fix(installer): remove CheckForNewerVersion custom action from WiX source`) exactly.

## Deviations from Plan

**None.** Plan executed exactly as written. Two minor observations (Criterion 6 plan-text typo about baseline grep count; Criterion 11 off-by-one within explicit blank-line tolerance) are documented in the Acceptance Criteria Results table above for transparency — neither required corrective code action.

## Issues Encountered

- `Read` on `01-RESEARCH.md` returned a token-limit error (file exceeds 25k tokens). Mitigated by using the verbatim Code Example E1 already inlined into `01-01-PLAN.md`'s `<interfaces>` block (lines 64-103 of the plan), which is the planner's extraction of the same content. No re-read needed — every element to remove and to preserve was specified line-for-line in the plan.

## Security Posture Change

This plan eliminates two attack-surface items from the MSI install path:

1. **Outbound HTTPS call to `api.github.com`** — the removed `CheckForNewerVersion` CA used `WixToolset.Dtf.CustomAction` SFXCA wrapping a managed DLL that hit GitHub during install. Net: zero outbound calls from the WiX-defined install path now (T-01-01 mitigated — RegisterTask/UnregisterTask still call the local EXE).
2. **SFXCA-wrapped managed DLL** in the MSI's `Binary` table — the wrapper required a `CustomAction.config` that v0.7.4 shipped without, producing the CLR-load failure on every interactive install. Gone (T-01-02 mitigated).

No new threats introduced. Threat T-01-03 (accidental empty `<InstallUISequence/>` stub) actively prevented — verified by Criterion 4. Threat T-01-04 (accidental removal of `LaunchApplication` / `WixUI` wiring) prevented — verified by Criteria 5, 6.

## Next Plan (01-02) Readiness

- WiX source is now clean. Plan 01-02 can proceed to remove `installer/build.ps1`'s `$caDll` block + `-d UpdateCheckCaDll=...` arg and strip the `installer/UpdateCheck/UpdateCheck.csproj` reference from `Jaminator.sln` (D-02, D-03).
- **One-line confirmation per plan output spec:** the build script still passes `-d UpdateCheckCaDll=...` until Plan 01-02 removes it, but **WiX v4 tolerates a defined-but-unreferenced extension variable** — so the repo at HEAD `6a97982` is independently buildable per D-12, and Plan 01-02's commit-2 is the right place to remove the now-unused build arg.
- Plan 01-04 (`Installer.cs` diagnostics, parallel wave-1 sibling) is disjoint — no merge conflict surface.

## Self-Check: PASSED

- `installer/installer.wxs` exists and contains the expected post-edit content (verified via Read at end of plan; lines 61-72 show `LaunchApplication` flowing directly into `<UI><Publish>` flowing into `<StandardDirectory>`).
- Commit `6a97982` exists in git log on branch `worktree-agent-ab15e2037fdc99549` (verified via `git log --oneline -3`).
- `.planning/phases/01-remove-broken-custom-action-and-improve-diagnostics/01-01-SUMMARY.md` exists (just written).

---
*Phase: 01-remove-broken-custom-action-and-improve-diagnostics*
*Completed: 2026-05-11*
