---
id: FX-20260906-005
kind: fix
status: reviewed
area: visualization-runtime
stage: phase-0
tasks:
  - serialize-plothost-message-dispatch
  - prevent-overlapping-render-state-reversal
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
  - stale-generation-suppression
hotspots:
  - asynchronous-message-ordering
  - overlapping-plotly-react
  - active-plot-identity
bug:
  repro: asynchronous WebMessage handlers can overlap Plotly operations and allow an earlier render to finish after a later render
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - prove a second render does not enter Plotly while the first render is pending
    - prove the later render becomes the active event identity after queued completion
    - preserve active command correlation and existing rendering behavior
    - run PlotHost and repository gates
  tests_run:
    - PlotHost Node syntax check passed
    - PlotHost Node suite passed 7 of 7
    - solution build passed with 0 warnings and 0 errors
    - repository MTP gate passed 581 of 581 with 0 failed and 0 skipped
    - repository path-policy gate passed for 239 text files
    - scientific source-boundary gate passed for 10 first-party presentation files
  benchmarks_run: []
search:
  aliases:
    - PlotHost message serialization
    - overlapping Plotly react
    - render completion race
---

# Serialize PlotHost message dispatch

## Summary

Serialize host-to-PlotHost operations in arrival order so overlapping asynchronous renders cannot reverse the active plot state or concurrently mutate the same Plotly DOM node.

## Plan

1. Add a deterministic deferred-render regression harness.
2. Move dispatch into a queue-backed handler while preserving fail-closed validation and command correlation.
3. Verify the later render owns PointId events after both queued operations complete.
4. Run focused and repository-wide gates and update the runtime audit.

## Exploration log

- The WebView2 message callback is `async`, but the event source does not await one callback before delivering the next message.
- `Plotly.react`, export, selection, and camera reset are asynchronous operations against one shared DOM plot.
- Two render callbacks can therefore overlap; an earlier render may commit `activePlotId` and `activeGeneration` after a later render, defeating host-side stale-response suppression at the script state boundary.
- Exact non-render command correlation does not prevent this render-versus-render race.

## Root cause

The callback directly awaits each operation without a shared dispatch queue. JavaScript event delivery starts later callbacks independently, so arrival order is not an execution-order guarantee.

## Implementation

- Added a single promise-backed operation queue around PlotHost command dispatch. Each incoming message begins only after the preceding Plotly operation has settled.
- Kept message validation, render stale-generation handling, and exact active-command correlation inside the serialized dispatch function.
- Added a deferred-`Plotly.react` harness mode that proves a second render cannot enter Plotly while the first render is pending.
- The regression also proves that, after both operations finish, click events carry the later render's `plotId` and generation.

## Validation

- `node --check web/FluoAxis.PlotHost/plot-host.js`: passed.
- `node --test web/FluoAxis.PlotHost/plot-host.test.js`: 7 passed, 0 failed.
- `dotnet build FluoAxis.slnx --no-restore`: passed with 0 warnings and 0 errors.
- Repository MTP gate: 581 passed, 0 failed, 0 skipped.
- Repository path-policy verification: passed for 239 text files.
- Scientific source-boundary verification: passed for 10 first-party presentation files.
- No rendering benchmark was run: serialization removes invalid overlap but does not change the work of an individual Plotly operation.

## Review outcomes

- Correctness: Plotly operations against the one shared DOM target now preserve host message arrival order.
- Correlation: queued non-render commands are evaluated against the active identity at execution time, so commands invalidated by an intervening render fail closed.
- Failure recovery: `dispatchMessage` contains render-local and outer operation error handling, so an operation failure resolves after emitting a safe error and does not poison the queue.
- Runtime budget: no worker or CPU pool was added; one Plotly target has one serialized promise chain.
- Compatibility: protocol fields, message types, responses, and render replacement semantics are unchanged.
- The runtime concern was independently reviewed by a subagent before implementation; manual post-change review found no blocking issue. GitHub-hosted review was not invoked.

## Follow-ups

- Keep queueing bounded by the host's existing command production and cancellation policy; do not add an independent worker pool around the single Plotly DOM node.
- Add explicit host-side coalescing only if measured resize or render bursts demonstrate queue pressure; do not silently drop semantic commands.
