---
id: FT-20260903-012
kind: feature
status: reviewed
area: visualization-protocol
stage: phase-1
tasks:
  - plotspec-contract
  - closed-bridge-message-vocabulary
  - bounded-message-validation
  - stale-generation-filter
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.PlotProtocol/
  - tests/FluoAxis.PlotProtocol.Tests/
  - tests/FluoAxis.Architecture.Tests/ProjectDependencyTests.cs
  - FluoAxis.slnx
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - plot-spec-v1
  - plot-bridge-envelope-v1
hotspots:
  - protocol-versioning
  - untrusted-web-message-validation
  - stale-generation-suppression
  - point-id-lineage
  - path-free-payloads
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - PlotSpec construction and source-generated JSON roundtrip
    - closed message direction and payload vocabulary
    - bounded envelope parsing and malformed input rejection
    - stale generation and request correlation filtering
    - PointId-only interaction payload validation
    - architecture build full-suite and path-policy gates
  tests_run:
    - PlotProtocol focused suite 14 passed
    - solution build passed with 0 warnings and 0 errors
    - repository MTP gate 544 passed
    - repository path-policy gate passed for 207 text files
    - change registry JSONL parsed successfully
    - git diff check passed with only the pre-existing gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; no renderer, transfer path, or visualization performance is implemented or claimed
search:
  aliases:
    - plot protocol foundation
    - PlotSpec V1
    - WebView2 bridge messages
    - stale plot generation
---

# Establish the versioned plot protocol foundation

## Summary

Introduce the renderer-neutral `PlotSpec` and a strictly validated, versioned host/PlotHost message protocol. This slice establishes contracts only. It does not claim a WebView2 host, local Plotly rendering, SharedBuffer transfer, GPU fallback, screenshot fidelity, PDF export, or visualization performance evidence. `Surface3D` remains unresolved and unsupported in V1 rather than being silently frozen.

## Plan

### Contract

- Define only requirement-backed 2D, `Scatter3D`, and `Line3D` plot kinds, axis/series/camera configuration, and immutable snapshots.
- Define the closed handshake, host-to-script, and script-to-host message vocabulary with `schemaVersion`, `requestId`, `plotId`, and `generation` on every envelope.

### Runtime

- Add bounded UTF-8 JSON parsing through a source-generated serialization context.
- Add a stateful host-side correlation gate that accepts only the active plot generation and outstanding request.

### Data/path

- Keep bridge data renderer-neutral and path-free. Interaction events carry stable `PointId` values, not formal numeric results or source paths.
- Model SharedBuffer capability only in the ready handshake; defer buffer layout and WebView2 ownership until the runtime spike.

### Tests

- Prove valid roundtrips and fail-closed rejection of unknown message types, wrong directions, malformed IDs, invalid plot shapes, oversized messages, unexpected JSON members, and stale responses.

### Docs

- Update the completion audit conservatively: protocol groundwork is not runnable Plotly/WebView2 evidence and closes no G0-G8 gate.

### Review

- Perform the required concern review manually because the user explicitly requested primary-agent-only execution.

## Exploration log

- The current solution has no `FluoAxis.PlotProtocol`, `FluoAxis.Visualization`, or PlotHost project.
- The baseline specifies a shared renderer-neutral `PlotSpec`, while `plan.md` defines the bridge vocabulary and mandatory envelope fields.
- `Surface3D` is described as optional and remains an unresolved product decision, so enabling it in the initial protocol would improperly freeze behavior.
- Current evidence cannot support claims about WebView2 security, SharedBuffer lifetime, Plotly rendering, static export, GPU behavior, or frame-time budgets.

## Root cause

Not applicable. This is a planned protocol capability.

## Implementation

### Contract

- Added a dependency-free `FluoAxis.PlotProtocol` production project and mirrored test project. The architecture gate now recognizes PlotProtocol as a leaf library with no dependency on UI, filesystem, database, Analysis, or Plotly implementation types.
- Added renderer-neutral `PlotSpec`, `AxisSpec`, `SeriesSpec`, and `CameraSpec`. V1 supports `Scatter2D`, `Line2D`, `ScatterLine2D`, `Scatter3D`, and `Line3D`; undefined enum values fail closed, dimensional invariants require Z/camera only for 3D, and the root PlotSpec now carries the required schema version after FX-20260903-007.
- Deliberately omitted `Surface3D`: the baseline calls it optional and `plan.md` leaves it to a Phase 0 product decision.
- Added the exact closed message vocabulary from `plan.md` and mandatory `schemaVersion`, `requestId`, `plotId`, `generation`, and object payload fields on every envelope.

### Runtime and security

- Added a source-generated `System.Text.Json` context with camel-case names, string enums, null omission, and rejection of unmapped members.
- Added a 256 KiB fail-closed parser limit, direction checking, current-schema enforcement, non-empty correlation identifiers, non-negative generations, type-specific payload deserialization, and validation of render identity/revision, capabilities, camera, errors, and selections.
- Point events carry only a bounded safe `PointId`; selection lists are unique and capped at 10,000 identifiers. No bridge type can carry a filesystem path, database handle, algorithm object, or trusted numeric calculation result.
- Added a host-side response gate. Command completions must match an outstanding request in the active generation; unsolicited interaction events are accepted only for the active plot generation; host-to-script message types are never accepted as responses.

### Integration and documentation

- Added both projects and generated dependency lock files to the solution, updated the exact architecture graph, and raised the fail-closed repository test discovery floor from 530 to 544.
- Updated the completion audit without changing any gate to complete. This is contract evidence only, not a runnable WebView2/Plotly spike.

## Validation

- `dotnet restore tests/FluoAxis.PlotProtocol.Tests/FluoAxis.PlotProtocol.Tests.csproj`: passed after network permission was granted; dependency locks were generated. The first sandboxed attempt failed because NuGet network access was denied and is retained here as environment evidence, not a code failure.
- `dotnet build FluoAxis.slnx --no-restore`: passed, 0 warnings, 0 errors.
- Focused PlotProtocol executable: 14 passed, 0 failed, 0 skipped.
- Repository Microsoft.Testing.Platform gate with `--minimum-expected-tests 544`: 544 passed, 0 failed, 0 skipped.
- Repository path-policy verifier: passed for 207 text files.
- `docs/change_index.jsonl`: every line parsed successfully as JSON.
- `git diff --check`: passed; Git emitted only the pre-existing `.gitignore` LF-to-CRLF advisory.
- Benchmark: not applicable. The implementation contains no WebView2, Plotly, SharedBuffer, data encoder, visual output, or frame path, so a visualization performance number would be misleading.

## Review outcomes

- Contract: the message names and four envelope correlation fields match `plan.md`; the model remains renderer-neutral and does not expose Plotly JSON. Empty payloads are accepted only for message types whose detailed runtime payload is not yet approved, avoiding speculative contract fields.
- Runtime: parsing is byte-bounded and reflection-free. Stale-generation suppression is explicit rather than left to UI convention; correlated completions are one-shot. The gate is intentionally host-instance state and makes no thread-safety claim before a Visualization owner exists.
- Data/path: PlotProtocol is a dependency-free leaf and contains no filesystem-bearing property. `PointId` is identity only; exact coordinate lookup and distance remain C# follow-up work and are not claimed here.
- Security: unknown types, wrong directions, old/new schemas, unknown members, malformed identifiers, invalid dimensions, oversized JSON, duplicate selections, stale generations, and unsolicited command completions fail closed in focused tests. WebMessage origin validation remains the future WebView2 adapter's responsibility.
- Compatibility: this adds protocol V1 without changing Contracts V2, Run persistence, project archive schema, Method snapshots, Analysis, or Gateway behavior. No migration is required because PlotSpec is not yet persisted.
- Product/science: no scientific formula, threshold, result label, Method rule, or official Run state changes. `Surface3D` remains explicitly unresolved.
- Documentation/tests: all local gates pass. GitHub review and external Codex review were not required or invoked. The user requested primary-agent-only execution, so the mandated `contract/runtime/data-path/tests/docs/review` decomposition was performed manually.
- No unresolved correctness defect was found in this protocol-only slice.

## Follow-ups

- Implement the local PlotHost and WebView2 adapter as a separate measured security/runtime spike.
- Ask the product owner whether `Surface3D` belongs in an enabled Explorer capability set before adding it.
- Define concrete resize/export/data-transfer payloads only with the runtime owner, renderer constraints, and export limits in hand; V1 currently reserves those message names with strict empty payloads.
- Add exact C# PointId lookup and 2D/3D distance in a separate data-lineage change; JavaScript must never become the formal numeric source.
