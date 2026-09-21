---
id: FT-20260903-013
kind: feature
status: reviewed
area: visualization-data
stage: phase-6
tasks:
  - immutable-plot-data-snapshot
  - exact-pointid-lookup
  - csharp-2d-3d-distance
priority: P1
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.PlotProtocol/
  - tests/FluoAxis.PlotProtocol.Tests/
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - plot-data-snapshot-v1
  - exact-point-lookup-v1
hotspots:
  - point-identity
  - dimensional-consistency
  - finite-coordinate-validation
  - bounded-snapshot-memory
  - guided-analysis-separation
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - immutable defensive snapshot and exact PointId lookup
    - duplicate invalid and oversized point rejection
    - exact 2D and 3D Euclidean components
    - source-generated JSON roundtrip
    - full build test path-policy and registry gates
  tests_run:
    - PlotProtocol focused suite 22 passed
    - solution build passed with 0 warnings and 0 errors
    - repository MTP gate 552 passed
    - repository path-policy gate passed for 211 text files
    - change registry JSONL parsed successfully
    - git diff check passed with only the pre-existing gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; bounded construction and exact lookup are covered, but no renderer throughput claim is made
search:
  aliases:
    - exact PointId lookup
    - plot data snapshot
    - 2D 3D distance
    - CSharp plot measurement
---

# Add exact C# plot-point lookup and distance measurement

## Summary

Add a bounded immutable plot-data snapshot that owns exact finite coordinates keyed by opaque `PointId`, then calculate two-dimensional or three-dimensional Euclidean distance in C#. JavaScript remains an untrusted interaction surface and returns identity only. This slice does not establish Raw-RFU lineage, source-row semantics, persistence, rendering, or the formal BRAF `dr'` calculation.

## Plan

### Contract

- Define renderer-neutral 2D/3D point data with stable opaque identifiers and series membership.
- Require one explicit point budget per snapshot and homogeneous dimensionality.
- Return raw coordinate deltas and distance without display formatting.

### Runtime

- Build one immutable array plus ordinal PointId index for constant-time lookup.
- Use numerically stable C# Euclidean magnitude calculation; no JavaScript value participates.

### Data/path

- Carry no filesystem path or source row because the current checkout has not frozen source-row indexing or end-to-end Raw lineage.
- Keep this Explorer measurement separate from Analysis and from formal two-dimensional BRAF `dr'`.

### Tests

- Cover 2D/3D lookup and measurement, identity/dimension/finite-value guards, defensive copying, explicit point limits, and serialization.

### Docs

- Update the completion audit as partial PointId/distance groundwork only; do not claim G2 lineage or G6 completion.

### Review

- Perform manual `contract/runtime/data-path/tests/docs/review` decomposition because the user requested primary-agent-only execution.

## Exploration log

- `SignalPoint` currently contains only elapsed time and RFU; `DatasetBuilder` drops physical source-row provenance when constructing Domain series.
- `CalculationTrace` records a source point index, not the required stable source PointId. Therefore this change cannot honestly close end-to-end Raw lineage.
- The requirement explicitly says Plotly returns only `PointId`, after which C# retrieves exact X/Y/Z and calculates distance.
- The requirement distinguishes Explorer 3D distance from formal BRAF `dr'`; PlotProtocol remains independent from Analysis to preserve that boundary.
- Source-row cardinality and indexing are not frozen in the current Data Contract, so this slice deliberately avoids inventing them.

## Root cause

Not applicable. This is planned Explorer data infrastructure.

## Implementation

### Contract

- Added versioned `PlotDataSnapshot`, `PlotDataPoint`, `PlotDimensions`, and `PlotDistanceMeasurement` contracts to the dependency-free PlotProtocol layer.
- A snapshot defensively copies its points, requires homogeneous 2D or 3D coordinates, rejects non-finite values and duplicate PointIds, and enforces a caller-selected positive point limit capped at 1,000,000.
- The small-data `plot.render` payload now carries both `PlotSpec` and `PlotDataSnapshot`; parsing verifies matching PlotId, matching dimensionality, non-negative data revision, and that every data series is declared by the specification.

### Runtime and data

- Built one ordinal `Dictionary` index per immutable snapshot for exact PointId lookup without trusting coordinates returned from JavaScript.
- Added C# 2D/3D Euclidean measurement with raw deltas. A scale-normalized magnitude calculation avoids intermediate square overflow for large finite coordinates. Display rounding remains outside the contract.
- Kept source row and Raw lineage out of this type because the current importer drops physical row provenance and Domain `SignalPoint` has no stable PointId. This snapshot therefore represents exact coordinates within one plot snapshot, not proof of origin lineage.

### Integration

- Registered all new types in the source-generated JSON context and raised the fail-closed repository test floor from 544 to 552.
- Updated the criterion audit conservatively: local PointId lookup is now present, while G2 end-to-end traceability and G6 Derived-to-Raw lineage remain unproven.

## Validation

- `dotnet build FluoAxis.slnx --no-restore`: passed, 0 warnings, 0 errors.
- Focused PlotProtocol executable: 22 passed, 0 failed, 0 skipped.
- Repository Microsoft.Testing.Platform gate with `--minimum-expected-tests 552`: 552 passed, 0 failed, 0 skipped.
- Repository path-policy verifier: passed for 211 text files.
- `docs/change_index.jsonl`: every line parsed successfully as JSON.
- `git diff --check`: passed; Git emitted only the pre-existing `.gitignore` LF-to-CRLF advisory.
- Benchmark: not applicable. No renderer, shared-buffer encoder, UI interaction, or frame path exists, so visualization throughput is not claimed. Snapshot memory is bounded by the explicit point limit, and lookup behavior is structurally O(1).

## Review outcomes

- Contract: PlotSpec/data PlotIds, dimensionality, and series membership are validated together at the render boundary. Both persisted-capable plot DTO roots now carry schema version 1.
- Runtime: accepted points are immutable snapshots and exact lookup is ordinal. Large-magnitude distance tests exercise the scale-normalized calculation; unknown identities fail without echoing untrusted values.
- Data/path: no filesystem identity, database handle, source path, or source-row claim enters PlotProtocol. The missing Raw lineage is explicit rather than inferred.
- Scientific separation: this generic Explorer distance does not reference Analysis and cannot replace formal BRAF `dr'`; no Method, threshold, classification, or Run state changed.
- Memory/performance: each snapshot holds one point array and one PointId dictionary, bounded by a required caller budget and a hard ceiling. No workers, queues, caches, or CPU concurrency were introduced.
- Compatibility: no existing project archive or Gateway contract contains plot data, so no migration is needed. The render payload is pre-runtime and has no deployed consumer.
- Tests/docs: focused and full gates pass. GitHub and external Codex review were not required or invoked. Manual `contract/runtime/data-path/tests/docs/review` decomposition was used as the user requested.
- Residual risk: finite endpoint coordinates can still produce a mathematically unrepresentable delta when opposite-sign magnitudes approach `double` limits; such a result fails finite-result construction rather than silently producing an official value.

## Follow-ups

- Freeze and implement stable Raw point identity at import/persistence boundaries before claiming lineage to source data.
- Connect this snapshot to the future Visualization adapter and PlotHost click flow.
- Add a renderer benchmark and SharedBuffer layout only after the WebView2 runtime owner and transfer thresholds are qualified.
