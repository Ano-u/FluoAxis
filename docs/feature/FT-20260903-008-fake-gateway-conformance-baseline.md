---
id: FT-20260903-008
kind: feature
status: validated
area: frontend-backend-contract
stage: phase-1
tasks:
  - presentation-fake-gateway
  - contract-scenario-fixtures
  - fake-real-conformance
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Presentation/
  - tests/FluoAxis.GatewayContract.Tests/
  - samples/contracts/
  - FluoAxis.slnx
contracts:
  - IFluoAxisGateway-current-capability-baseline
  - gateway-result-envelope
  - optimistic-revision-errors
hotspots:
  - fake-real-behavior-drift
  - capability-overclaim
  - cancellation-semantics
  - deterministic-fixtures
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - fixture JSON schema and serialization roundtrip
    - shared Fake and Application gateway conformance for create get failure cancel stale and unavailable states
    - architecture dependency and path-policy gates
    - full solution build and repository MTP gate
  tests_run:
    - focused GatewayContract build passed with 0 errors
    - focused Fake and Application conformance tests passed 13 of 13
    - full solution build passed with 0 errors and 53 emitted test-analyzer warnings in the incremental run
    - repository MTP gate passed 495 of 495 with 0 failed and 0 skipped
    - repository path-policy verification passed for 180 text files
    - dependency evidence regenerated for 26 resolved components
  benchmarks_run: []
search:
  aliases:
    - fake gateway
    - gateway conformance
    - contract scenario fixtures
    - current capability baseline
---

# Establish the current Fake and real Gateway conformance baseline

## Summary

Add a Presentation-owned Fake Gateway and executable contract scenarios for the capabilities the real Application Gateway actually supports today. Exercise both adapters through one conformance suite without fabricating import, mapping, calibration, control, or sample-analysis success.

## Plan

1. Freeze current shared behaviors as relative, deterministic JSON scenario fixtures: create/get success, not-found/unsupported-version failure, empty new Run, cancellation, stale revision, invalid state, and unavailable capability.
2. Add a Presentation project that depends only on Contracts and provides an independently implemented in-memory Fake Gateway for frontend work.
3. Run the same adapter-agnostic assertions against Fake and Application gateways.
4. Keep all unsupported vertical operations explicitly unavailable until their real contracts and persistence exist.
5. Validate architecture, serialization, paths, build, and full discovery; update the G1 audit conservatively.

## Exploration log

- `IFluoAxisGateway` declares the planned guided workflow, but the checked-out Application adapter currently supports successful `CreateProjectAsync` and `GetRunAsync` only. Remaining operations intentionally return `SYSTEM_CAPABILITY_UNAVAILABLE` after version/revision/state guards.
- A Fake that returns successful import or analysis results now would diverge from the real adapter and falsely imply frozen scientific/data semantics. The conformance boundary must therefore be explicitly scoped to the current capability baseline.
- `plan.md` requires success, failure, empty, cancellation, and stale-revision fixtures before vertical work. These states can be fixed without deciding unresolved scientific thresholds or real workbook layouts.
- The user requested primary-agent-only execution. Contract, runtime, data/path, tests, docs, and review concerns are decomposed manually.

## Root cause

Not applicable. This is a bounded contract and frontend-development capability.

## Implementation

- Added `FluoAxis.Presentation` with a dependency only on Contracts and an independently implemented, concurrency-safe `FakeFluoAxisGateway`.
- Added `FakeMethodIdentity` so frontend development receives a supplied Method identity and hash without Presentation referencing Domain or calculating scientific values.
- Added `GatewayErrorCodes` as the stable Contracts vocabulary for frontend-visible orchestration errors; Presentation no longer reaches through Contracts into Domain constants.
- Fake project creation matches the real adapter for request identity, exact replay, conflicting reuse, atomic concurrent replay, backend-derived IDs/timestamps, empty new-Run snapshots, cancellation, and contract-version rejection.
- Fake `GetRun` matches success and structured not-found behavior. Mutation guards preserve version, not-found, stale-revision, and invalid-state ordering.
- Import, mapping, assignment, calibration, control, and sample-analysis success remain deliberately unavailable where the checked-out real adapter is unavailable. No scientific result or import layout is fabricated.
- Added a nine-scenario relative JSON manifest covering success, replay, conflict, empty state, failure, cancellation, stale revision, invalid state, and unavailable capability.
- Added `FluoAxis.GatewayContract.Tests`; the same adapter-agnostic tests execute against Fake and Application implementations.
- Added Presentation and the conformance project to the solution and extended the exact architecture graph.
- Raised the fail-closed repository test-discovery floor from 482 to 495.

## Validation

- `dotnet restore FluoAxis.slnx --locked-mode`: passed and created lock files for the two new projects.
- Focused GatewayContract build: passed with 0 errors. Test naming analyzers emit warnings because repository test names use the established underscore style.
- Focused conformance suite: 13 passed, 0 failed, 0 skipped. Five shared behavioral groups run against both adapters, one concurrent replay group runs against both, and the fixture manifest has an independent shape check.
- `dotnet build FluoAxis.slnx --no-restore`: passed with 0 errors. The incremental run emitted 53 existing/new test-analyzer warnings; no zero-warning repository claim is made.
- Repository MTP gate with minimum 495: 495 passed, 0 failed, 0 skipped.
- `build/Verify-RepositoryPolicy.ps1`: passed for 180 text files.
- Dependency evidence regenerated successfully and still contains 26 resolved NuGet components; the new production project adds no package dependency.

## Review outcomes

- Contract: both adapters use the same `IFluoAxisGateway`, DTOs, error-code vocabulary, result envelope, Method identity, and state/action vocabulary.
- Runtime: Fake storage and request replay are protected by one lock; cancellation is checked before and inside mutation/read critical sections. No worker pool or unbounded background work was introduced.
- Data/path: the scenario manifest contains no external source path; import metadata uses only a bare display name and managed project-relative destination.
- Scientific boundary: Fake does not calculate, classify, or supply provisional scientific values. It cannot make unsupported vertical operations appear complete.
- Compatibility: this adds a Contracts convenience vocabulary without changing serialized DTO shape or contract version. Existing real behavior is unchanged.
- Scope: conformance is proven only for the current capability baseline, not for the entire planned guided workflow. G1 remains open until real vertical operations and a frontend path exist.
- Manual primary-agent review covered contract drift, state/error ordering, concurrency, cancellation, architecture direction, path safety, and capability claims. GitHub and external Codex review were not invoked.

## Follow-ups

- Expand shared conformance only as each real vertical operation becomes truthful and persisted.
- A complete Fake-driven no-calculation frontend path still requires the WinUI/Presentation screen skeleton.
- Add stale-response suppression at the ViewModel operation-coordinator boundary when asynchronous screens are implemented.
