---
id: FX-20260906-004
kind: fix
status: reviewed
area: visualization-runtime
stage: phase-0
tasks:
  - enforce-active-plot-command-correlation
  - reject-cross-plot-and-future-generation-commands
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - web/FluoAxis.PlotHost/plot-host.js
  - web/FluoAxis.PlotHost/plot-host.test.js
  - docs/feature/FT-20260903-014-windows-visualization-runtime-spike.md
  - docs/change_index.jsonl
contracts:
  - plot-bridge-envelope-v1
  - active-plot-generation-correlation
hotspots:
  - stale-command-suppression
  - cross-plot-isolation
  - dispose-state-reset
bug:
  repro: non-render commands with another plotId or a generation greater than the active generation can operate on the current DOM plot
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - reject non-render commands for a different plotId
    - reject non-render commands for older and newer generations
    - preserve correctly correlated resize export selection reset and dispose commands
    - run PlotHost syntax focused tests and repository gates
  tests_run:
    - PlotHost Node syntax check passed
    - PlotHost Node suite passed 6 of 6
    - solution build passed with 0 warnings and 0 errors
    - repository MTP gate passed 581 of 581 with 0 failed and 0 skipped
    - repository path-policy gate passed for 238 text files
    - scientific source-boundary gate passed for 10 first-party presentation files
  benchmarks_run: []
search:
  aliases:
    - PlotHost command correlation
    - cross plot command isolation
    - future generation rejection
---

# Correlate PlotHost commands to the active plot

## Summary

Make every non-render PlotHost command fail closed unless both its `plotId` and `generation` exactly match the currently rendered plot.

## Plan

1. Capture regression evidence for cross-plot and future-generation commands.
2. Add a single active-command correlation guard before command dispatch.
3. Clear all active identity and camera state on disposal.
4. Run focused and repository-wide validation, then update the visualization audit.

## Exploration log

- Host-side `PlotResponseGate` requires exact active generation when registering commands.
- PlotHost currently rejects only `message.generation < activeGeneration` before dispatch.
- Therefore a non-render command for a different `plotId`, or for a greater generation, can mutate/export/dispose the currently active DOM plot.
- This is independent of the fixed-camera behavior and does not alter PlotSpec, scientific logic, or product decisions.

## Root cause

The script uses a global monotonic-generation check for all incoming messages, but a single DOM plot requires identity correlation for non-render operations. The check neither compares `plotId` nor rejects future generations.

## Implementation

- Added an `isActiveCommand` guard that requires a non-render command to match both the active `plotId` and active `generation` exactly.
- Kept render replacement semantics separate: render messages retain the existing monotonic stale-generation rejection.
- Disposal now clears the active plot identity, generation, kind, and initial camera so later commands cannot operate on the purged DOM plot.
- Extended the dependency-free Node harness with counters and a regression covering another plot, older generation, future generation, the exact active command, and command rejection after disposal.

## Validation

- `node --check web/FluoAxis.PlotHost/plot-host.js`: passed.
- `node --test web/FluoAxis.PlotHost/plot-host.test.js`: 6 passed, 0 failed.
- `dotnet build FluoAxis.slnx --no-restore`: passed with 0 warnings and 0 errors.
- Repository MTP gate: 581 passed, 0 failed, 0 skipped. The Node test is a separate CI step and does not change the MTP discovery floor.
- Repository path-policy verification: passed for 238 text files.
- Scientific source-boundary verification: passed for 10 first-party presentation files.
- No benchmark was run because the guard is constant-time message correlation and does not change rendering or transfer work.

## Review outcomes

- Contract: no PlotProtocol schema or message vocabulary changed.
- Runtime: correctly correlated resize, export, selection, camera reset, and dispose behavior remains available; unmatched commands are ignored without mutating the active plot.
- Security: cross-plot and stale/future-generation commands no longer reach Plotly operations.
- State: disposal removes all script-side active identity and camera state.
- Compatibility: render replacement keeps its existing monotonic generation behavior; the legacy reset-camera protocol command remains supported for correlated callers.
- Subagent runtime review identified the defect; manual post-change review found no blocking issue. GitHub-hosted review was not invoked.

## Follow-ups

- Keep render replacement semantics separate from active non-render command correlation.
- Consider an explicit dispose acknowledgement only as a future protocol-versioned change; V1 defines no terminal dispose response.
