---
id: FX-20260921-001
kind: fix
status: validated
area: import-infrastructure
stage: phase-1
tasks:
  - rehash-managed-source-before-read
  - reject-same-size-source-replacement
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Infrastructure/ManagedSignalPreparationAdapter.cs
  - tests/FluoAxis.Infrastructure.Tests/
  - docs/fix/FX-20260921-001-source-hash-revalidation.md
contracts:
  - managed-source-sha256
  - fail-closed-import
hotspots:
  - source-identity
  - path-free-errors
bug:
  repro: replace a staged source with a different file of the same size before signal preparation
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - same-size source replacement is rejected
    - unchanged staged source remains readable
    - source hash mismatch does not leak a filesystem path
  tests_run: []
  benchmarks_run: []
search:
  aliases:
    - managed source hash
    - source revalidation
    - same-size replacement
---

# Revalidate managed source identity before signal preparation

## Summary

The staging adapter records a SHA-256, but signal preparation previously trusted the recorded value and only checked file size and format. Recompute the digest immediately before parsing so a same-size replacement fails closed.

## Plan

1. Add a bounded streaming hash pass over the validated managed resource.
2. Compare the digest with the staged identity before invoking an importer.
3. Add focused Infrastructure tests and record the validation evidence.

## Exploration log

- The staging adapter computes SHA-256 while publishing the managed copy.
- `ManagedSignalPreparationAdapter` checked existence, size, signature, and extension but did not recompute the digest.

## Root cause

The source identity was treated as metadata rather than a precondition for reading the bytes that become formal Run input.

## Implementation

`ManagedSignalPreparationAdapter` now streams the managed resource through SHA-256 and compares the digest with `PreparedSourceIdentity.Sha256` before format detection and importer invocation. A mismatch returns `IMPORT_SOURCE_CHANGED` with a path-free safe detail code.

## Validation

Passed `dotnet build FluoAxis.slnx --no-restore` and `dotnet test tests/FluoAxis.Infrastructure.Tests/FluoAxis.Infrastructure.Tests.csproj --no-build` (29/29). The focused regression replaces the staged file with a same-size content and verifies that the importer is never called. Repository path policy and scientific source boundary gates also passed.

## Review outcomes

Manual review completed: the hash pass is bounded by the existing stream, resets position before and after hashing, preserves cancellation, and does not include machine paths in returned errors. No benchmark was needed because this adds one sequential read required for source identity integrity.

## Follow-ups

- Keep source hash and mapping hash visible in the eventual Calculation Trace lineage.
