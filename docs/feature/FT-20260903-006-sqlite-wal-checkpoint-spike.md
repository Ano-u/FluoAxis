---
id: FT-20260903-006
kind: feature
status: validated
area: persistence
stage: phase-0
tasks:
  - file-backed-sqlite-workspace
  - wal-checkpoint
  - portability-and-measurement-spike
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Persistence/
  - tests/FluoAxis.Persistence.Tests/
contracts:
  - sqlite-workspace-lifecycle
  - packaging-checkpoint-precondition
hotspots:
  - wal-sidecars
  - connection-lifetime
  - workspace-portability
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - focused Persistence build and tests
    - file-backed WAL checkpoint and move/reopen test
    - local timing observation without an unapproved SLA
    - repository path and MTP gates
  tests_run:
    - focused Persistence build passed with 0 warnings and 0 errors
    - focused Persistence tests passed 17 of 17
    - locked restore completed successfully
    - full solution build passed with 0 errors
    - repository MTP gate passed 480 of 480 with 0 failed and 0 skipped
    - repository path-policy verification passed for 168 text files from repository and parent cwd
  benchmarks_run:
    - local spike observation of 50 transactional creates plus truncate checkpoint was 313.1 ms; no SLA claimed
search:
  aliases:
    - sqlite wal spike
    - checkpoint for packaging
    - file backed workspace
---

# Validate file-backed SQLite WAL and checkpoint behavior

## Summary

Add a narrow persistence lifecycle primitive and executable spike proving that the current schema and repository work against a file-backed WAL database, that an explicit truncate checkpoint produces a package-ready database, and that the database remains readable after its workspace directory moves.

## Plan

1. Keep filesystem resolution outside Persistence by accepting an already configured SQLite connection factory.
2. Add an explicit WAL initializer and exclusive checkpoint operation with fail-closed busy/result validation.
3. Exercise the existing repository against a unique workspace-local database and verify checkpoint sidecars and move/reopen behavior.
4. Record a local timing observation without converting it into an unapproved performance threshold.
5. Run focused and repository-wide validation and review path/runtime implications.

## Exploration log

- The current repository opens and disposes a connection per operation and is already compatible with file-backed SQLite, but no code establishes WAL mode or verifies a packaging checkpoint.
- `plan.md` requires workspace WAL and checkpoint/closed connections before packaging. Full ProjectFormat integration belongs to Phase 5 and would be premature while signal/result persistence and workspace ownership are not frozen.
- The existing schema covers only project, Run, Method snapshot, request identity, and audit facts. This spike must not invent signal, mapping, or result tables.
- The user requested primary-agent-only execution; contract, runtime, data/path, tests, docs, and review are decomposed manually.

## Root cause

Not applicable. This is a bounded architecture spike.

## Implementation

- Added `SqliteWalWorkspace`, a path-free lifecycle primitive that accepts a caller-owned connection factory rather than a filesystem path.
- Initialization requires a closed, file-backed, non-pooled connection, establishes WAL mode and `synchronous=NORMAL`, and runs the existing forward-only schema initializer.
- Packaging checkpoint uses `PRAGMA wal_checkpoint(TRUNCATE)` and fails closed if SQLite reports a busy connection or incomplete page checkpoint.
- Invalid factory outputs are disposed, including already-open, pooled, in-memory, empty-data-source, and `:memory:` connections.
- Added file-backed tests for WAL mode, persisted repository data, move/reopen portability, live-reader checkpoint refusal, connection lifecycle guards, and a bounded local timing observation.
- Raised the fail-closed repository discovery floor from 475 to 480 tests.

## Validation

- `dotnet build tests/FluoAxis.Persistence.Tests/FluoAxis.Persistence.Tests.csproj --no-restore`: passed, 0 warnings, 0 errors.
- Focused in-process Persistence run: 17 passed, 0 failed, 0 skipped.
- Live output measurement: 50 transactional creates plus truncate checkpoint took 313.1 ms on this development machine. This is a spike observation, not a performance contract or release SLA.
- `dotnet restore FluoAxis.slnx --locked-mode`: passed after network access was permitted.
- `dotnet build FluoAxis.slnx --no-restore`: passed. A clean incremental build reported 0 warnings; the preceding restored full build exposed 121 existing test-analyzer warnings and 0 errors, so no repository-wide zero-warning claim is made.
- Repository MTP gate with minimum 480: 480 passed, 0 failed, 0 skipped.
- `build/Verify-RepositoryPolicy.ps1`: passed for 168 text files from repository-root and parent-working-directory invocation.

## Review outcomes

- Contract: no filesystem path was added to a public Persistence, Application, or Contracts type. The caller still owns approved-root resolution.
- Runtime: repository operations dispose their per-call connections, pooling is prohibited, and a real older read snapshot proves that a busy truncate checkpoint fails closed.
- Data/path: the database survives a workspace-directory move after checkpoint. Test roots remain unique under `artifacts/test-tmp/FluoAxis.Persistence.Tests/<process>/<guid>/` and cleanup verifies containment.
- Scope: this proves a narrow SQLite lifecycle primitive only. It does not prove that every future workspace owner has closed all connections, nor does it implement `.fluoaxisproj` packaging.
- Compatibility: no schema migration or stored contract changed. Existing repositories continue to operate through the same connection factory.
- Manual primary-agent review covered correctness, lifecycle, path leakage, bounded work, backward compatibility, and documentation. GitHub and external Codex review were not invoked.

## Follow-ups

- Integrate checkpoint, closed-connection enforcement, archive reopen validation, and atomic replacement only when the Application workspace lifecycle contract is frozen.
- Add a single workspace owner that can quiesce operations and prove all handles are closed before packaging; this spike cannot infer external connection ownership.
- Establish representative database-size and concurrency fixtures before setting any throughput or checkpoint latency budget.
