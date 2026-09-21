---
id: FX-20260903-005
kind: fix
status: reviewed
area: frontend-backend-contract
stage: phase-1
tasks:
  - preserve-source-media-type-roundtrip
  - validate-source-extension-consistency
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Contracts/SourceFileDto.cs
  - tests/FluoAxis.Contracts.Tests/ConstructionGuardTests.cs
  - tests/FluoAxis.Contracts.Tests/ContractSnapshotStabilityTests.cs
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - SourceFileDto-v2
  - SourceFileMetadata-media-type
hotspots:
  - contract-wire-compatibility
  - multi-format-reopen
  - extension-media-type-consistency
bug:
  repro: SourceFileDto.FromDomain drops MediaType and ToDomain always reconstructs XLSX, including CSV TSV and TXT sources
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - XLSX CSV TSV and TXT domain DTO domain roundtrip tests
    - filename and managed-resource extension mismatch rejection
    - unsupported extension rejection
    - frozen V2 JSON shape remains unchanged
    - full solution build and repository policy gates
  tests_run:
    - Contracts focused tests 62 passed
    - solution build passed with 0 warnings and 0 errors on the final incremental run
    - first repository MTP attempt 529 passed and 1 existing WAL portability test failed with transient directory access denial
    - identical repository MTP rerun 530 passed
    - Persistence focused rerun 17 passed
    - repository path-policy gate passed for 195 text files
    - git diff check passed with only the pre-existing gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; no performance claim
search:
  aliases:
    - source media type roundtrip
    - csv source dto
    - source file format persistence
---

# Preserve source media type across the V2 DTO roundtrip

## Summary

Repair `SourceFileDto` so supported XLSX, CSV, TSV, and TXT source metadata round-trips without being silently converted to XLSX. Preserve the frozen Contract V2 JSON shape by deriving the controlled media type from consistent bare-name and managed-resource extensions rather than adding a new wire field.

## Plan

### Contract

- Keep the V2 JSON shape unchanged.
- Require the original bare filename and managed relative resource to identify the same supported format.
- Derive the corresponding controlled media type deterministically in both conversion directions.

### Runtime

- No gateway or Run-state behavior changes.

### Data/path

- Continue carrying only a bare source name and managed relative path; no external path is introduced.

### Tests

- Add regression coverage for all four supported formats, mismatches, unsupported extensions, and frozen JSON shape.

### Docs

- Update the completion audit and fail-closed test floor after validation.

### Review

- Use manual `contract/runtime/data-path/tests/docs/review` decomposition as requested; do not start subagents.

## Exploration log

- `SourceFileMetadata` stores `MediaType`, but `SourceFileDto` has no media-type wire property.
- `SourceFileDto.FromDomain` currently discards the domain media type, while `ToDomain` hard-codes XLSX. CSV, TSV, and TXT therefore reopen with false metadata.
- Contract V2 is frozen by exact JSON snapshot tests. Adding a media-type property would be a breaking wire change and require Contract V3, but the supported format vocabulary is already closed and encoded by the two existing filenames.
- The narrow compatible repair is to validate matching supported extensions and map them to controlled media types.

## Root cause

The original DTO implementation was written for an XLSX-only source assumption even though the approved first-release data contract supports XLSX, CSV, TSV, and TXT. The DTO omitted media type and then compensated with an unconditional XLSX constant during reconstruction.

## Implementation

### Contract

- `SourceFileDto` now requires its bare original filename and managed relative resource filename to carry the same supported extension. XLSX, CSV, TSV, and TXT map to a closed media-type vocabulary.
- `ToDomain` derives the correct media type rather than hard-coding XLSX. `FromDomain` rejects contradictory domain metadata instead of silently discarding it.
- The V2 wire shape is unchanged: no media-type property was added, so existing valid V2 JSON remains byte-shape compatible.

### Runtime and data/path

- Gateway and Run-state behavior are unchanged. The repair affects only metadata conversion.
- No external filesystem identity is introduced. Existing bare-name and validated managed-relative-path constraints remain in force.

### Tests and documentation

- Added four-format domain-to-DTO-to-domain equality coverage plus mismatch, unsupported-extension, and contradictory-media-type rejection.
- The exact `RunSnapshotDto` JSON snapshot still passes, proving the V2 serialized shape did not change.
- Raised the fail-closed repository discovery floor to 530 and updated the criterion-level audit without claiming official import persistence or reopen completion.

## Validation

- Focused Contracts suite: 62 passed, 0 failed, 0 skipped.
- `dotnet build FluoAxis.slnx --no-restore`: passed with 0 warnings and 0 errors on the final incremental build.
- First full repository run discovered all 530 tests but produced 529 passed and one failure: the existing file-backed WAL portability test received a transient Windows access denial while moving its unique workspace directory.
- The identical repository command then passed 530/530; an independent Persistence project run passed 17/17. The intermittent first result remains recorded as residual test-environment evidence.
- Repository path-policy verifier: passed for 195 text files.
- `git diff --check`: passed with only the pre-existing `.gitignore` line-ending advisory.
- Benchmark: not applicable; this change performs constant-time extension mapping and makes no performance claim.

## Review outcomes

- Correctness: all four approved first-release formats preserve their controlled media type; mismatched or unsupported metadata fails closed.
- Compatibility: Contract V2 JSON shape and version remain unchanged. Previously valid supported source records remain valid.
- Data/path: no new path carrier or absolute path was introduced; conversion continues to validate the managed relative path.
- Runtime/memory: no worker, concurrency, cache, persistence, or materialization behavior changed.
- Tests/docs: focused regression and exact wire snapshot coverage pass; the full-suite transient WAL failure and successful reruns are both documented.
- Manual primary-agent decomposition was used as explicitly requested. GitHub and external Codex review were not required or invoked.
- Residual risk: media type is intentionally derivable only while the supported format set has a one-to-one extension mapping. A future ambiguous format requires a versioned DTO field rather than another implicit guess.

## Follow-ups

- Revisit an explicit wire media-type field only if a future format cannot be represented unambiguously by the managed resource extension; that would require a versioned contract change.
