---
id: FT-20260912-001
kind: feature
status: validated
area: application-orchestration
stage: phase-2
tasks:
  - signal-preparation-port
  - confirm-mapping-orchestration
  - assign-wells-orchestration
  - calibration-orchestration
  - control-qc-orchestration
  - sample-analysis-orchestration
  - run-state-machine-as-gate
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Application/
  - src/FluoAxis.Infrastructure/
  - src/FluoAxis.Contracts/
  - tests/FluoAxis.Application.Tests/
contracts:
  - IFluoAxisGateway
  - ConfirmMappingCommand
  - RunSnapshotDto
  - run-state-machine-gate
  - optimistic-revision
hotspots:
  - engine-reachability
  - state-transition-atomicity
  - import-analysis-boundary
  - scientific-decision-provenance
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - dotnet build FluoAxis.slnx --no-restore
    - dotnet test tests/FluoAxis.Application.Tests --no-restore
    - end-to-end synthetic dataset reaches Analyzed with engine-derived values
    - each failure path routes CalibrationFailed/ControlQcFailed/ReplicateConflict to the correct Failure state
    - stale revision, request-id replay, and cancellation leave no partial state
    - RunStateMachine.CanTransition is the actual gate for every persisted transition
  tests_run: []
  benchmarks_run: []
search:
  aliases:
    - vertical science closure
    - application engine orchestration
    - run state machine gate
    - signal preparation port
---

# Wire the deterministic Analysis Engine into the Application run workflow

## Summary

Close the largest verified gap between the frozen requirements and the implementation: `ApplicationGateway` currently returns `SYSTEM_CAPABILITY_UNAVAILABLE` for every workflow mutation and never calls `BrafAnalysisEngine`, so the complete, correct scientific core is unreachable from the product. This change makes one dataset flow `New -> Imported -> Mapped -> Calibrating -> CalibrationPassed -> ControlQc -> ReadyForAnalysis -> Analyzing -> Analyzed` with real engine calls and real `RunStateMachine`-enforced transitions.

## Plan

1. Define an Application-owned signal-preparation port so the Application layer can obtain role-grouped `WellSignals` without referencing `FluoAxis.Import` (which the architecture test forbids).
2. Implement the port in `FluoAxis.Infrastructure` by composing the existing `XlsxImporter`/`DelimitedTextImporter` over a managed-relative resource.
3. Add an Application-owned atomic state-advance operation to `IProjectRunRepository` so state + revision + audit commit in one transaction, with `RunStateMachine.CanTransition` as the gate.
4. Implement the five workflow methods against the engine, mapping engine `Result<T>` to `GatewayResult<T>` and engine diagnostics to `WellDiagnosticDto`/`IssueDto`.
5. Close the engine's defense-in-depth gap: `AnalyzeSamples` must not classify against a WT reference from a control stage that did not pass.
6. Prove the closure with Application-level end-to-end and failure-path tests.
7. Validate the full repository gate and record the evidence.

## Exploration log

- Verified by build and execution: the solution builds with 0 errors and the repository test gate passes 603/603 tests.
- `FluoAxis.Application` already references `FluoAxis.Analysis`, and `Architecture.Tests.ProjectDependencyTests` explicitly allows `Application -> {Analysis, Contracts, Domain}`. Engine orchestration therefore belongs in Application with no dependency-graph change.
- `FluoAxis.Application` does not reference `FluoAxis.Import` and the architecture test forbids it (`Import` may reference only `Domain`). The read step must therefore be an Application-owned port implemented by Infrastructure, which is already allowed to reference both `Application` and `Import`.
- The contract layer already carries most required DTOs (`CalibrationResultDto`, `ControlQcResultDto`, `SampleAnalysisDto`, `PerWellResultDto`, `WellAssignmentDto` with `ToDomain()`, `WellRoleNames`, `ChannelRegionMappingDto`, `WellDiagnosticDto`, `IssueDto`) and the mapping helpers (`Result<T>.ToGatewayResult`, `AppError.ToDto`/`ToDomain`).
- The frozen Gateway surface (plan.md §5.1) has eight methods and no separate commit-import command; `InspectImportAsync` is read-only. The `New -> Imported` transition must therefore be carried by `ConfirmMappingAsync`, which already receives the confirmed FAM/ROX region mapping.
- `AppError.ToDto()` always emits `wellId: null` and `channel: null`, so per-well diagnostics must cross as `WellDiagnosticDto` rather than being flattened into `AppError`.

## Root cause

Not applicable. This is a new Application capability over a verified-but-unreachable scientific core.

## Implementation

The Application workflow now reaches the deterministic Analysis engine through the signal preparation port, persists source/signals/mapping/assignments and stage outcomes through repository mutations, and enforces the run-state gate. The current action vocabulary is aligned with command preconditions: `New` exposes import and confirm mapping, `Imported` exposes assignment, and `Mapped` exposes calibration.

## Validation

Validated with `dotnet build FluoAxis.slnx --no-restore`; Application tests 33/33 and SQLite Integration tests 3/3 pass. The approved Golden candidate suite remains 16/16 from the current checkout, while the candidate manifest is explicitly unapproved and does not establish external scientific validity.

## Review outcomes

Manual review completed. Source hash revalidation and mapping snapshot persistence are tracked in paired fixes `FX-20260921-001` and `FX-20260921-002`. Public stage command atomicity across intermediate states remains a separate open P0 fix tracked by `FX-20260913-001`.

## Follow-ups

_To be completed._
