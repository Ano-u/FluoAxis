---
id: FX-20260903-006
kind: fix
status: reviewed
area: persistence-test-infrastructure
stage: phase-1
tasks:
  - stabilize-windows-workspace-move-proof
  - preserve-bounded-failure-evidence
priority: P1
branch: main
pr: null
paired_doc: null
paths:
  - tests/FluoAxis.Persistence.Tests/FileBackedWalWorkspaceTests.cs
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - workspace-portability-test
  - workspace-local-temporary-path
hotspots:
  - windows-file-handle-release
  - bounded-retry
  - test-cleanup-containment
bug:
  repro: One full-suite run failed moving a checkpointed unique SQLite workspace with Windows access denied; identical full and focused reruns passed
  root_cause_status: probable
  root_cause_confidence: medium
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - repeated focused WAL portability test
    - full Persistence suite
    - full repository MTP gate
    - repository path-policy and diff gates
  tests_run:
    - focused WAL portability test passed in 20 consecutive process runs
    - Persistence suite 17 passed
    - solution build passed with 0 warnings and 0 errors
    - repository MTP gate 530 passed
    - repository path-policy gate passed for 196 text files
    - git diff check passed with only the pre-existing gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; bounded test retry only
search:
  aliases:
    - WAL workspace move access denied
    - Windows SQLite handle release
    - portability test retry
---

# Stabilize the Windows WAL workspace movement proof

## Summary

Make the existing Windows-only filesystem movement assertion tolerate a short, bounded post-close handle-release delay while preserving a hard failure after the retry budget. Production persistence and packaging behavior remain unchanged.

## Plan

### Contract

- Do not change SQLite schema, repository ports, packaging preconditions, or production retry behavior.

### Runtime

- Convert the test fixture move helper to async and retry only Windows access-denied or sharing-violation `IOException` results.
- Bound total attempts and delay; propagate cancellation and rethrow the final original exception.

### Data/path

- Keep source and destination under the existing unique repository-local approved root and preserve containment-checked cleanup.

### Tests

- Stress the focused portability test repeatedly, then run focused and repository-wide gates.

### Docs

- Preserve the initial failure, successful reproductions, root-cause confidence, and exact retry policy.

### Review

- Use manual `contract/runtime/data-path/tests/docs/review` decomposition; no subagents are started per user instruction.

## Exploration log

- The first failure occurred after all explicit SQLite connections and checkpoint reader scopes had ended, at `Directory.Move`, with Windows access denied.
- The identical 530-test command immediately passed, and a focused Persistence run passed 17/17, so a deterministic open-connection defect is not reproduced.
- The fixture uses pooling disabled and unique repository-local directories. A short OS/security-scanner handle-release race is therefore the leading hypothesis, but not proven.
- An unbounded retry or broad `IOException` suppression would hide real lifecycle defects and is rejected.

## Root cause

Probable transient Windows handle-release latency after the SQLite checkpoint/connection lifecycle. Confidence is medium because the failure was observed once and did not reproduce in identical or focused reruns. The repair is deliberately confined to the test movement probe.

## Implementation

- Changed only the test fixture movement helper from synchronous `MoveWorkspace` to cancellation-aware `MoveWorkspaceAsync`; production SQLite lifecycle and repository code are untouched.
- `Directory.Move` now retries only native Windows access-denied, sharing-violation, and lock-violation codes. Other `IOException` values fail immediately.
- The policy allows at most five attempts with 50/100/150/200 ms waits before attempts two through five, for a maximum 500 ms wait. Cancellation is checked before every attempt and during every delay.
- If the fifth attempt fails, its original exception propagates. The helper updates its cleanup root only after a successful move, preserving containment-checked cleanup.

## Validation

- Focused portability test: 20 consecutive independent `dotnet test` process runs passed, 20/20.
- Focused Persistence suite: 17 passed, 0 failed, 0 skipped.
- `dotnet build FluoAxis.slnx --no-restore`: passed with 0 warnings and 0 errors.
- Repository Microsoft.Testing.Platform gate with floor 530: 530 passed, 0 failed, 0 skipped.
- Repository path-policy verifier: passed for 196 text files.
- `git diff --check`: passed with only the pre-existing `.gitignore` line-ending advisory.
- Benchmark: not applicable. This is a bounded test reliability repair, not a production performance change.

## Review outcomes

- Correctness: retry scope is restricted to the three Windows native codes consistent with transient handle contention; arbitrary I/O defects are not masked.
- Runtime: no production retry, connection, checkpoint, schema, or packaging behavior changed.
- Data/path: both move endpoints remain unique children of the repository-local approved test root; cleanup containment remains unchanged.
- Cancellation/bounds: total delay is at most 500 ms and cancellation interrupts both attempts and waits. The final failure remains observable.
- Evidence: the original one-off failure and medium-confidence root-cause assessment remain recorded; 20 green stress runs reduce recurrence risk but do not prove which process held the original handle.
- Manual primary-agent decomposition was used as explicitly requested. GitHub and external Codex review were not required or invoked.
- Residual risk: if a clean CI host repeatedly exhausts the budget, native handle-owner evidence is required rather than increasing the retry window.

## Follow-ups

- If bounded retries still exhaust in CI, capture the final native error and inspect live handle ownership before changing production lifecycle behavior.
