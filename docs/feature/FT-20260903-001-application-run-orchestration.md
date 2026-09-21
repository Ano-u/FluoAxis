---
id: FT-20260903-001
kind: feature
status: validated
area: application-orchestration
stage: phase-1
tasks:
  - create-project
  - get-run
  - run-snapshot-policy
  - request-id-idempotency
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Domain/ErrorCodes.cs
  - src/FluoAxis.Application/
  - tests/FluoAxis.Application.Tests/
  - tests/FluoAxis.Domain.Tests/AppErrorTests.cs
contracts:
  - IFluoAxisGateway
  - RunSnapshotDto
  - optimistic-revision
  - request-id-idempotency
  - application-error-codes
hotspots:
  - application-state-ownership
  - concurrent-project-creation
  - immutable-method-snapshot
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - dotnet build src/FluoAxis.Application/FluoAxis.Application.csproj --no-restore
    - dotnet run --project tests/FluoAxis.Application.Tests/FluoAxis.Application.Tests.csproj --no-restore
    - dotnet build FluoAxis.slnx --no-restore
  tests_run:
    - dotnet build src/FluoAxis.Application/FluoAxis.Application.csproj --no-restore (passed, 0 warnings, 0 errors)
    - dotnet run --project tests/FluoAxis.Application.Tests/FluoAxis.Application.Tests.csproj --no-restore (24 passed)
    - dotnet run --project tests/FluoAxis.Domain.Tests/FluoAxis.Domain.Tests.csproj --no-build --no-restore (243 passed)
    - dotnet run --project tests/FluoAxis.Contracts.Tests/FluoAxis.Contracts.Tests.csproj --no-build --no-restore (46 passed)
    - dotnet build FluoAxis.slnx --no-restore (passed, 0 warnings, 0 errors)
    - dotnet test --test-modules **/bin/Debug/net10.0/*Tests.dll --root-directory . --minimum-expected-tests 438 (exit 8; Golden and Persistence ran zero tests)
  benchmarks_run: []
search:
  aliases:
    - application gateway
    - create project
    - run snapshot
    - optimistic revision
    - request id idempotency
---

# Establish Application-owned Run orchestration

## Summary

Implement the first honest Application vertical slice: project creation, Run retrieval, immutable Method identity, backend-derived allowed actions, contract-version rejection, and request-ID idempotency.

## Plan

1. Define an Application repository port and a concurrency-safe in-memory adapter for the first executable slice.
2. Add an Application-owned Run aggregate/read model and deterministic snapshot mapping.
3. Implement `CreateProjectAsync` and `GetRunAsync`, including contract-version validation, cancellation, not-found errors, and atomic request-ID replay/conflict handling.
4. Return an explicit capability-unavailable error from Gateway operations whose current DTOs cannot carry the data needed for a real implementation.
5. Add focused concurrency, idempotency, contract-version, cancellation, snapshot, and unavailable-capability tests.
6. Run focused and solution validation, relative-path audit, and manual concern-based review.

## Exploration log

- `FluoAxis.Application` and its test project contain no implementation or tests.
- `CreateProjectCommand` and `GetRunQuery` are sufficient for a real first vertical slice.
- `InspectImportCommand` contains safe metadata and a managed relative destination, but no transient import stream/handle boundary.
- `ConfirmMappingCommand` contains no selected mapping, and later analysis commands contain no immutable prepared signal snapshot.
- Claiming those operations as implemented would fabricate missing input; they remain explicit unsupported capabilities in this slice.
- The baseline requires Application ownership of Run state, optimistic revision checks, structured errors, and Fake/Real conformance. This slice establishes the shared backend policy but does not claim the full G1 conformance gate.
- `Mapped` does not encode whether a complete well assignment has been persisted. Therefore the backend must not expose `runCalibration` from that state in this slice; it exposes `assignWells` until a later aggregate revision adds an explicit assignment fact.

## Root cause

Not applicable. This is a new Application capability over an intentionally empty project shell.

## Implementation

- Added `IProjectRunRepository` as the Application persistence port and a lock-protected in-memory adapter with atomic create-or-replay behavior.
- Added an immutable `ProjectRunAggregate` that owns project/Run identity, revision, state, timestamps, and the Method snapshot.
- Implemented `ApplicationGateway.CreateProjectAsync` and `GetRunAsync` with cancellation, contract-version validation, structured not-found failures, and immutable snapshots.
- Added exact request-ID replay and conflict detection. Concurrent identical requests create one project and one Run.
- Centralized Application error codes in `FluoAxis.Domain.ErrorCodes` and covered them through the registry test.
- Added backend-only `allowedActions` derivation for all 14 Run states. `Mapped` deliberately does not expose calibration until persisted well assignment becomes an explicit aggregate fact.
- Added revision-before-state validation for mutating commands. Operations that lack sufficient input contracts return `SYSTEM_CAPABILITY_UNAVAILABLE` instead of fabricating work.

## Validation

- Application project build: passed, 0 warnings, 0 errors.
- Application tests: 24 passed, 0 failed. Coverage includes creation, retrieval, Method identity, replay, 32-way concurrent replay, request-ID conflict, unsupported version, not-found, cancellation, stale revision, state guard, unavailable import capability, and all Run-state action policies.
- Domain tests: 243 passed, 0 failed, including all newly registered error codes.
- Contracts tests: 46 passed, 0 failed.
- Full solution build: passed, 0 warnings, 0 errors in the final no-restore build.
- Repository MTP test gate discovers and passes every non-empty suite, including Application. It still exits 8 because the independently tracked Golden and Persistence assemblies contain zero tests.
- `git diff --check`: passed; the only message is Git's pre-existing line-ending warning for the unrelated modified `.gitignore`.
- Focused relative-path audit found no drive-rooted path, file URI, global-temp call, or cwd dependency in changed code, tests, or this document.
- No benchmark was required: the in-memory adapter is a development/test implementation; its concurrency test verifies atomicity, not production persistence throughput.

## Review outcomes

- Manual concern decomposition was used because the user explicitly requested primary-agent-only execution.
- Contract: no existing DTO wire shape changed. Six new structured error codes were added to the central compatibility registry.
- Runtime: project creation and request replay are atomic under one bounded in-process lock; no worker pool or CPU-budget behavior was introduced.
- Data/path: no filesystem value is accepted or persisted by this slice; Method identity is snapshotted in the aggregate.
- Tests: success, replay, concurrency, failure ordering, cancellation, and all state policies are covered.
- Docs: unsupported import/mapping/analysis input-contract gaps remain explicit rather than being hidden behind mock success.
- Self-review corrected two issues before validation: parallel error-code registries were consolidated, and `Mapped` no longer exposes calibration without evidence of persisted well assignment.
- GitHub/external Codex review was not invoked for this local uncommitted baseline.

## Follow-ups

- Define an Infrastructure-only transient import-source boundary before implementing `InspectImportAsync`.
- Add selected mapping and immutable prepared-analysis input contracts before implementing mapping and Analysis orchestration.
- Add an independently implemented frontend Fake Gateway and run the same conformance suite against Fake and real adapters before claiming G1.
- Implement a transactional persistence adapter; the in-memory adapter is not a release persistence mechanism.
- Resolve the Golden and Persistence empty suites tracked by `FX-20260903-001` before claiming a green repository test gate.
