---
id: FT-20260903-003
kind: feature
status: validated
area: persistence
stage: phase-1
tasks:
  - sqlite-schema-v1
  - forward-only-migration-runner
  - project-run-repository
  - transactional-audit
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Persistence/
  - tests/FluoAxis.Persistence.Tests/
contracts:
  - IProjectRunRepository
  - sqlite-schema-v1
  - transaction-and-audit
hotspots:
  - optimistic-idempotency
  - method-snapshot-reconstruction
  - forward-only-migrations
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - dotnet build src/FluoAxis.Persistence/FluoAxis.Persistence.csproj --no-restore
    - dotnet run --project tests/FluoAxis.Persistence.Tests/FluoAxis.Persistence.Tests.csproj --no-restore
    - dotnet build FluoAxis.slnx --no-restore
  tests_run:
    - dotnet build src/FluoAxis.Persistence/FluoAxis.Persistence.csproj --no-restore (passed, 0 warnings, 0 errors)
    - dotnet run --project tests/FluoAxis.Persistence.Tests/FluoAxis.Persistence.Tests.csproj --no-restore (12 passed)
    - dotnet build FluoAxis.slnx --no-restore (passed, 0 warnings, 0 errors)
    - repository MTP gate with minimum 454 tests (all non-empty suites passed; exit 8 only because Golden contains zero tests)
  benchmarks_run: []
search:
  aliases:
    - sqlite schema v1
    - migration runner
    - project run repository
    - transactional audit
---

# Establish the SQLite persistence baseline

## Summary

Implement the first production persistence adapter for the Application project/Run port using SQLite, forward-only schema migration, foreign-key enforcement, exact request replay, and audit insertion in the same transaction.

## Plan

1. Define schema V1 for project, Run, immutable Method snapshot, create-request idempotency, and append-only audit events.
2. Implement a migration runner that initializes an open SQLite connection, rejects future schema versions, and never downgrades.
3. Implement `IProjectRunRepository` with atomic create-or-replay semantics and full Method reconstruction.
4. Add in-memory SQLite tests for migration, foreign keys, persistence/reopen semantics, idempotency, conflicts, concurrency, audit atomicity, and future-version rejection.
5. Run focused/solution validation, a relative-path audit, and manual contract/runtime/data/test review.

## Exploration log

- `FluoAxis.Persistence` and its test assembly are empty shells; the latter causes the repository MTP gate to exit 8.
- `plan.md` requires foreign keys, forward-only migrations, transactional writes, immutable input revisions, audit events committed with business state, and version/timestamp/revision fields.
- The current Application port is sufficient for project creation and Run retrieval. It does not yet expose signal batches or state mutation, so this slice must not invent those schemas as implemented behavior.
- Tests can use SQLite in-memory databases and therefore require no filesystem or temporary-path exception.

## Root cause

Not applicable. This is a new persistence capability over an intentionally empty project shell.

## Implementation

- Added forward-only SQLite schema V1 for immutable Method snapshots, projects, Runs, create-request identities, and append-only audit events.
- Enabled foreign keys on every opened connection and rejected unsupported future schema versions without downgrading.
- Added schema/revision/timestamp fields and constrained persisted Run states to the current closed enum range.
- Implemented `SqliteProjectRunRepository` over the existing Application port. Project, Run, Method, idempotency record, and audit event commit in one immediate write transaction.
- Exact request replay returns the original aggregate; reuse with a different identity returns a conflict without adding rows.
- Reconstructed every Method field on read and verified the canonical hash before returning the aggregate.
- Serialized writers within one repository and used SQLite immediate transactions to preserve behavior across repository instances.

## Validation

- Persistence project build passed with 0 warnings and 0 errors.
- Twelve SQLite tests passed with no analyzer warnings: initialization, idempotent migration, future-version rejection, foreign keys, complete Method roundtrip/hash, exact replay, conflict, same-instance and cross-instance concurrency, transaction rollback, Gateway reopen, and pre-cancellation.
- Full solution build passed with 0 warnings and 0 errors.
- Repository test discovery now executes Persistence successfully; only the independently empty Golden assembly keeps the repository MTP gate red.
- `git diff --check` passed apart from the unrelated pre-existing `.gitignore` line-ending warning.
- Focused path audit found no drive-rooted path, file URI, global-temp call, or cwd dependency. Tests use named shared in-memory SQLite databases and create no files.
- No production benchmark is claimed: this slice validates correctness and bounded transaction shape; file-backed WAL/checkpoint throughput belongs to the later workspace spike.

## Review outcomes

- Contract: implements the existing Application repository port without changing Gateway DTOs.
- Runtime: one immediate transaction contains all business and audit writes; no per-row transaction loop or worker pool was introduced.
- Data/path: no filesystem path enters the public adapter contract or database schema.
- Compatibility: schema initialization is forward-only and fails closed on a newer database. No downgrade or destructive migration exists.
- Integrity: foreign keys, Run-state range, non-negative revisions, immutable Method hash verification, and rollback behavior are tested.
- Scope: signal/mapping/result tables and WAL packaging are deliberately not claimed before their contracts and workspace lifecycle exist.
- GitHub/external Codex review was not invoked for this local uncommitted baseline.

## Follow-ups

- Add signal/mapping/result tables only when their Application contracts are frozen.
- Integrate WAL checkpoint/close with ProjectFormat in a later workspace-lifecycle change.
- Add file-backed WAL/checkpoint and bulk prepared-statement benchmarks before claiming the SQLite Phase 0 spike.
