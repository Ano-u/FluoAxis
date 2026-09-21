---
id: FX-20260906-001
kind: fix
status: reviewed
area: windows-visualization-runtime
stage: phase-0
tasks:
  - remove-inapplicable-camera-reset-control
priority: P1
branch: main
pr: null
paired_doc: docs/feature/FT-20260903-014-windows-visualization-runtime-spike.md
paths:
  - src/FluoAxis.App/MainWindow.xaml
  - src/FluoAxis.App/MainWindow.xaml.cs
  - docs/feature/FT-20260903-014-windows-visualization-runtime-spike.md
  - docs/adr/ADR-002-plot-renderer-and-export.md
  - docs/change_index.jsonl
contracts:
  - fixed-camera-spike-interaction
hotspots:
  - camera-lock-ui-consistency
  - plot-protocol-backward-compatibility
bug:
  repro: The fixed-camera WinUI spike still presents a Reset camera command even though all user camera manipulation is disabled
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - Release App build
    - PlotHost deterministic harness
    - exact-window runtime inspection
    - repository path-policy gate
  tests_run:
    - Node syntax check passed
    - PlotHost deterministic harness 5 passed
    - Release App build passed with 0 warnings and 0 errors
    - Debug App build passed with 0 warnings and 0 errors
    - Repository MTP gate 576 passed with 0 failed and 0 skipped
    - Relative-path policy gate passed for 231 text files
    - Exact-window Release runtime inspection passed
    - Change registry JSONL parse passed
    - Diff check passed apart from the pre-existing .gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; command-surface consistency repair only
search:
  aliases:
    - fixed camera reset control
    - camera lock toolbar
    - Reset camera button
---

# Remove the inapplicable fixed-camera reset control

## Summary

Remove the WinUI spike's visible `Reset camera` command after the 3D camera was intentionally locked to keep the axes at stable screen-relative positions. Preserve the protocol command as a compatibility surface; this repair changes only what the current spike offers to users.

## Plan

- Remove the toolbar control and its unused click handler.
- Keep the PlotProtocol reset vocabulary and PlotHost handler intact to avoid an unrelated wire-contract change.
- Rebuild and inspect only the verified FluoAxis Release window.
- Re-run focused PlotHost and relative-path gates.

## Exploration log

- The current Release executable was identified by exact resolved path, process ID, `FluoAxis - Plot rendered` title, and nonzero window handle before capture.
- The captured FluoAxis window shows a nonblank fixed-camera 3D plot and a visible `Reset camera` toolbar command.
- `MainWindow.xaml` exposes the command and `MainWindow.xaml.cs` posts `plot.resetCamera`.
- PlotHost deliberately disables scene drag, wheel zoom, double-click changes, camera modebar controls, and unsolicited relayout camera messages.
- Removing the PlotProtocol message would broaden this UI consistency repair into a compatibility-sensitive contract change, so that path is rejected.

## Root cause

The camera was fully locked after the original turntable approach failed to hold axes at stable screen-relative positions, but the earlier spike toolbar command was not removed with that interaction change.

## Implementation

- Removed the visible fixed-camera `Reset camera` button from `MainWindow.xaml`.
- Removed `OnResetCameraClick` and the now-unused `PostEmpty` helper from `MainWindow.xaml.cs`.
- Preserved `plot.resetCamera` in PlotProtocol and PlotHost so this narrow UI correction does not break the existing wire vocabulary or deterministic harness.
- Updated the visualization spike and ADR to distinguish the fixed-camera UI from the still-compatible protocol command.

## Validation

- `node --check web/FluoAxis.PlotHost/plot-host.js`: passed.
- `node --test web/FluoAxis.PlotHost/plot-host.test.js`: 5 passed, 0 failed.
- Release and Debug `FluoAxis.App` builds: both passed with 0 warnings and 0 errors.
- Repository Microsoft Testing Platform gate: 576 passed, 0 failed, 0 skipped.
- Relative-path policy gate: 231 repository-owned text files passed.
- `docs/change_index.jsonl`: every nonblank line parsed as one JSON object.
- `git diff --check`: no new whitespace failure; only the pre-existing `.gitignore` line-ending advisory remained.
- Runtime inspection used the Release process identified by exact resolved executable path, PID, `FluoAxis - Plot rendered` title, and nonzero window handle. A handle-targeted capture showed nonblank 3D rendering and confirmed that the toolbar no longer exposes `Reset camera`.
- This exact-window capture proves the command-surface correction and nonblank rendering only. It does not prove that a drag reached WebView2 without moving the scene, nor that clicking a rendered point returned the exact PointId.
- No benchmark was required or run for this command-surface-only repair. Scientific rules, Method/Data Contracts, formal calculations, and the PlotProtocol wire shape were not changed.

## Review outcomes

- Manual concern decomposition was used for implementation because the user had requested primary-agent-only execution at that stage.
- Contract: the protocol command and payload shape remain backward compatible.
- Runtime: the visible UI now matches the fully locked camera behavior.
- Data/path: no filesystem, persisted-data, import, archive, or scientific-data path changed; the relative-path gate passed.
- Tests: focused JavaScript checks, both App configurations, and the full repository MTP gate passed.
- Docs: the spike, ADR, registry, and this fix record agree that the protocol reset remains available while the fixed-camera UI does not offer it.
- Review: self-review found no unresolved issue in this narrow repair. GitHub and external Codex review were not required for this change; the broader visualization feature remains in progress.

## Follow-ups

- Obtain stronger current-build pixel evidence for locked-camera drag resistance and exact PointId click behavior before accepting ADR-002.
