---
id: FT-20260903-011
kind: feature
status: reviewed
area: import-infrastructure
stage: phase-1
tasks:
  - transient-picker-boundary
  - bounded-atomic-managed-copy
  - staged-source-sha256
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Infrastructure/
  - src/FluoAxis.Domain/ErrorCodes.cs
  - tests/FluoAxis.Domain.Tests/AppErrorTests.cs
  - tests/FluoAxis.Infrastructure.Tests/
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - transient-external-picker-boundary-v1
  - managed-staged-resource-v1
hotspots:
  - external-path-lifetime
  - path-redaction
  - destination-containment
  - atomic-copy-and-cleanup
  - bounded-streaming-hash
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - successful bounded copy and SHA-256 metadata
    - collision-safe parallel staging
    - cancellation and failed-copy cleanup
    - source mutation and unsupported input rejection
    - cwd independence containment and path-free errors
    - full solution build and repository policy gates
  tests_run:
    - Infrastructure focused tests 14 passed
    - solution build passed with 0 errors
    - repository MTP gate 522 passed
    - repository path-policy gate passed for 194 text files
    - git diff check passed with only the pre-existing gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; bounded streaming behavior is tested and no throughput optimization is claimed
search:
  aliases:
    - managed import staging
    - picker copy boundary
    - atomic source staging
    - import sha256
---

# Stage external import selections into managed relative resources

## Summary

Add an Infrastructure-only boundary that receives an operating-system picker path transiently, streams one supported source into a uniquely named managed relative resource, calculates SHA-256 during the copy, and returns path-free metadata. This slice does not persist official import data, mutate a Run, or decide the unresolved `New -> Imported` submission workflow.

## Plan

### Contract

- Keep `FluoAxis.Contracts`, `IFluoAxisGateway`, and Run state transitions unchanged.
- Keep the external picker value inside Infrastructure and return only a safe display name, relative managed identity, size, hash, and media type.

### Runtime

- Stream with a configured byte ceiling and end-to-end cancellation.
- Write to a unique sibling temporary file, flush it, then publish with a no-overwrite atomic move.

### Data/path

- Resolve destinations under an injected fully qualified managed root for one call only; reject traversal, reparse points, unsupported extensions, and unsafe source metadata.
- Remove partial temporary files on cancellation or failure without exposing either runtime path in errors.

### Tests

- Cover success/hash fidelity, cwd independence, parallel uniqueness, cancellation cleanup, bounds, collisions, source mutation, containment, and path redaction.

### Docs

- Update the phase audit without claiming official import or real instrument qualification.

### Review

- Use manual `contract/runtime/data-path/tests/docs/review` decomposition as requested; do not start subagents.

## Exploration log

- FT-010 can inspect an existing managed relative resource, but no adapter currently converts a transient file-picker selection into that resource.
- `ImportMetadataFactory` can hash a seekable stream, but using it after a separate copy would add a second full read; staging should hash during the bounded copy.
- `SourceFileMetadata` describes an officially imported source. Reusing it for an uncommitted staging artifact would overstate lifecycle state, so this slice needs a staging-specific result owned by Infrastructure.
- The formal import transaction and `New -> Imported` command boundary remain a product decision and are explicitly outside this slice.

## Root cause

Not applicable. This is the next planned import capability.

## Implementation

### Contract

- Kept Contracts, Gateway commands, and Run state unchanged. `StagedImportResource` is Infrastructure-owned and explicitly represents an uncommitted staging fact, not an imported Run source.
- Added stable path-free errors for unavailable or changing sources and no-overwrite publication collisions. Unsupported extensions use the existing import format-mismatch code.

### Runtime

- Added `ManagedImportStagingAdapter`, which consumes a fully qualified picker result only inside one Infrastructure call. It supports XLSX, CSV, TSV, and TXT and returns only the bare display name, managed-relative identity, SHA-256, byte count, and media type.
- The adapter reads in 64 KiB pooled chunks, enforces the byte ceiling before and during copy, calculates SHA-256 in the same pass, flushes the unique sibling temporary file to disk, and publishes via a no-overwrite atomic move.
- The source is held read-only during copying. Cancellation is checked before access, on every read/write iteration, and after progress notification. An owned partial file is removed on cancellation or failure.

### Data/path

- Managed destinations are derived from an injected fully qualified root plus validated `RelativePath` values; cwd changes do not affect resolution. Root and destination segments are checked for reparse points before and after preparation and again before publication.
- Generated names use unique identifiers and preserve only the allowlisted extension. A collision cannot overwrite an existing resource, and cleanup never deletes a temporary file the call did not create.
- Results and normalized errors contain no external source path, managed root, or resolved destination path.

### Tests and documentation

- Added eight staging tests covering content/hash fidelity, media types, eight-way parallel uniqueness, deterministic collision/no-overwrite behavior, missing/empty/unsupported/oversized input, pre-cancellation, cancellation after the first chunk, source mutation denial, cwd independence, cleanup, and path redaction.
- Registered the new error codes in the Domain construction suite and raised the repository fail-closed discovery floor to 522.
- Updated the criterion-level completion audit conservatively: staging improves G3 cancellation evidence but does not close official import persistence, real-file qualification, or any complete gate.

## Validation

- `dotnet build FluoAxis.slnx --no-restore`: passed, 0 errors. The latest incremental run emitted 0 warnings; an earlier full-source rebuild in this change emitted 142 existing analyzer warnings, so no global zero-warning claim is made.
- Focused Infrastructure suite: 14 passed, 0 failed, 0 skipped.
- Repository Microsoft.Testing.Platform gate with `--minimum-expected-tests 522`: 522 passed, 0 failed, 0 skipped.
- Repository path-policy verifier: passed for 194 text files.
- `git diff --check`: passed; Git emitted only the pre-existing `.gitignore` LF-to-CRLF advisory.
- Benchmark: not applicable. This is a correctness and bounded-I/O slice, not a throughput optimization; the implementation performs one 64 KiB chunked read/write/hash pass instead of a post-copy second read.

## Review outcomes

- Contract: the public Gateway wire shape and state machine are untouched; staged metadata cannot be mistaken for a persisted official source type.
- Runtime: publication is no-overwrite and temporary-file ownership is tracked, so a colliding call cannot delete or replace another call's file. Mid-copy cancellation cleanup is deterministically tested.
- Data/path: external absolute identity is transient at the Infrastructure boundary; all returned identity is managed-relative and failures are path-free. Cwd independence, unique parallel names, containment, and reparse rejection were reviewed.
- Security/memory: memory is bounded to one pooled 64 KiB buffer plus hashing state; no full source payload is retained or logged. Source write sharing is denied while copying.
- Compatibility: this capability is standalone and does not change persisted schema, project archives, Contracts V2, or existing gateway construction.
- Tests/docs: focused and repository-wide checks pass. Synthetic files prove the engineering boundary only and are not evidence for the five required real instrument layouts.
- Manual primary-agent decomposition was used as explicitly requested. GitHub and external Codex review were not required or invoked.
- Self-review found no unresolved correctness defect in this slice.

## Follow-ups

- Confirm whether mapping confirmation atomically performs official import plus `New -> Imported -> Mapped`, or whether a separate import-confirmation command owns `New -> Imported`.
- Persist the accepted source, mapping snapshot, long-form signals, revision, and audit in one transaction after that decision.
