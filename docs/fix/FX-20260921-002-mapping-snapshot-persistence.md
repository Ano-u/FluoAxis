---
id: FX-20260921-002
kind: fix
status: validated
area: persistence
stage: phase-2
tasks:
  - persist-confirmed-channel-mapping
  - reopen-mapping-snapshot
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Application/RunWorkflowData.cs
  - src/FluoAxis.Application/ApplicationGatewayWorkflow.cs
  - src/FluoAxis.Persistence/SqliteProjectRunRepository.Workflow.cs
  - tests/FluoAxis.Integration.Tests/
  - docs/fix/FX-20260921-002-mapping-snapshot-persistence.md
contracts:
  - confirmed-channel-mapping
  - workspace-reopen
  - relative-source-coordinates
hotspots:
  - mapping-lineage
  - sqlite-roundtrip
  - atomic-import-commit
bug:
  repro: confirm a mapping, close the repository, reopen the workspace, and find no persisted channel mapping snapshot
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - confirmed FAM and ROX mappings are persisted with source coordinates
    - reopening the workspace reconstructs the exact mapping
    - mapping rows commit with imported source and signal rows
  tests_run: []
  benchmarks_run: []
search:
  aliases:
    - mapping snapshot
    - channel_mapping
    - well_column_mapping
    - reopen mapping
---

# Persist the confirmed FAM and ROX mapping snapshot

## Summary

The schema already contains `channel_mapping` and `well_column_mapping`, but the ConfirmMapping workflow only persists source metadata and signals. Persist the Application-owned mapping so a reopened workspace can reproduce the exact source coordinates used for import.

## Plan

1. Carry the confirmed mapping through the workflow payload.
2. Write both channel rows and their well-column rows in the same import transaction.
3. Add a read port and a reopen test that compares the complete mapping.

## Exploration log

- `PreparedDataMapping` is created and consumed during signal preparation.
- `RunWorkflowPayload` has no mapping member, and the SQLite writer never uses the existing mapping tables.

## Root cause

The persistence schema was added ahead of the workflow payload contract; the import path dropped the mapping after parsing.

## Implementation

`RunWorkflowPayload` now carries `PreparedDataMapping`; the ConfirmMapping mutation writes `channel_mapping` and `well_column_mapping` in the same transaction as source metadata and signal points. `IRunWorkflowReader` and SQLite now expose `GetMappingAsync`, and the in-memory repository preserves the same payload shape.

## Validation

Passed `dotnet build FluoAxis.slnx --no-restore` and `dotnet test tests/FluoAxis.Integration.Tests/FluoAxis.Integration.Tests.csproj --no-build` (3/3). The reopen test verifies both channel regions, header/time coordinates, and well-column identity after a fresh repository instance. Repository path policy and scientific source boundary gates also passed.

## Review outcomes

Manual review completed: mapping rows are relative logical coordinates only, use the caller transaction, and are written before the state mutation commits. The reopened object is reconstructed through the same invariant-checked `PreparedDataMapping` constructor.

## Follow-ups

- Include mapping snapshot identity in formal lineage and project archive manifests.
