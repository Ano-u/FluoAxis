---
id: FX-20260913-001
kind: fix
status: in_progress
area: application-orchestration
stage: phase-2
tasks:
  - make-stage-commands-atomic
  - preserve-request-replay
  - prevent-intermediate-state-stranding
priority: P0
branch: main
pr: null
paired_doc: docs/feature/FT-20260913-001-requirements-implementation-audit.md
paths:
  - src/FluoAxis.Application/
  - src/FluoAxis.Persistence/
  - tests/FluoAxis.Application.Tests/
  - tests/FluoAxis.Integration.Tests/
contracts:
  - optimistic-revision
  - request-id-idempotency
  - atomic-run-mutation
  - run-state-machine-gate
hotspots:
  - cancellation-after-intermediate-commit
  - exact-request-replay
  - sqlite-transaction-boundary
bug:
  repro: stage command commits an intermediate state before its result and terminal state are committed
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - exact stage-command replay returns the original outcome without another revision
    - cancellation or persistence failure cannot leave a transient run state
    - in-memory and SQLite repositories enforce identical route transitions
    - focused and full repository test gates pass
  tests_run:
    - "dotnet build FluoAxis.slnx --no-restore: not run; .NET 10.0.400 SDK is unavailable on the current machine"
    - "dotnet test focused and repository gates: not run for the same SDK availability limitation"
    - "node --test web/FluoAxis.PlotHost/plot-host.test.js: 7 passed"
    - "build/Verify-RepositoryPolicy.ps1: passed for 269 text files"
    - "build/Verify-ScientificSourceBoundary.ps1: passed for 11 first-party presentation files"
    - "git diff --check: passed"
  benchmarks_run: []
search:
  aliases:
    - workflow command atomicity
    - transient run state stranded
    - stage request replay
---

# Make scientific workflow commands atomic and replayable

## Summary

Repair the Application workflow so each public stage command persists its stage result, audit identity, revision, and externally observable terminal state as one repository transaction. Exact retries must replay the first outcome, and cancellation or a write failure must not strand a Run in an internal processing state.

## Plan

1. Reproduce the intermediate-state and exact-replay defects with focused tests.
2. Extend the repository mutation contract using the existing state-machine transitions, without bypassing state validation.
3. Commit each public stage command as one atomic routed mutation.
4. Apply identical behavior to the in-memory and SQLite repositories.
5. Validate cancellation, replay, stale revisions, scientific failure routing, and persistence round trips.

## Exploration log

- `RunCalibrationAsync`, `RunControlQcAsync`, and `AnalyzeSamplesAsync` each persist an intermediate state and then a second outcome state using a generated request ID.
- Failure outcomes add a third invalidation mutation.
- A cancellation or exception after the first commit leaves a state for which no public recovery command exists.
- An exact retry fails preflight because the original expected state has already advanced, so repository request replay is never reached.

## Root cause

The public command boundary was modeled as a sequence of state-edge transactions. The contract, however, defines idempotency and atomicity at the public command boundary. Internal processing states were persisted independently even though no durable worker or resume protocol owns them.

## Implementation

- Added deterministic `RunStateMachine.FindPath`/`IsValidPath` validation so a public command can prove every intermediate lifecycle edge without persisting an intermediate state.
- Calibration, Control QC, and Sample Analysis now commit one terminal mutation with the stage payload. Scientific failures route directly to `RunInvalid` in that same transaction; successful stages route to `CalibrationPassed`, `ReadyForAnalysis`, or `Analyzed`.
- Added repository mutation lookup and exact request matching for the in-memory and SQLite adapters. Gateway retries replay the stored result and rebuild the corresponding DTO without a second revision or engine call.
- Added focused domain, Application, and SQLite integration regression tests for lifecycle paths, exact replay, and one-revision terminal invalidation.

## Validation

Static validation and non-.NET gates pass in the current environment. C# build/test execution remains pending because the checkout pins SDK `10.0.400`, while the machine has no .NET SDK installed (only runtimes). The added tests are therefore recorded as planned code coverage, not claimed as executed.

## Review outcomes

Manual self-review completed for contract consistency, state-machine coverage, exact replay, SQLite transaction boundaries, relative paths, bounded persistence, and cancellation-before-mutation behavior. GitHub and Codex review were not available in this session. The implementation should remain review-pending until the pinned SDK is available and the focused/full .NET gates run.

## Follow-ups

- Run the focused Application, Domain, Integration, and full Microsoft.Testing.Platform gates with .NET SDK `10.0.400`.
- Add a persistence fault-injection test proving a payload write failure rolls back the terminal state, audit row, idempotency row, and stage result together.
- Update the paired requirements audit after the .NET evidence is available; do not claim the Guided UI, reporting/export, Explorer formula workflow, or scientific Method/Golden approval from this fix.
