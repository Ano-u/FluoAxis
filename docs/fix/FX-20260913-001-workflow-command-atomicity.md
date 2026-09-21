---
id: FX-20260913-001
kind: fix
status: stub
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
  tests_run: []
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

_To be completed._

## Validation

_To be completed._

## Review outcomes

_To be completed._

## Follow-ups

_To be completed._
