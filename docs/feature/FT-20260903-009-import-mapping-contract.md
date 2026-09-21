---
id: FT-20260903-009
kind: feature
status: validated
area: import-contract
stage: phase-1
tasks:
  - import-preview-region-contract
  - confirmed-channel-mapping-contract
  - contract-serialization-and-validation
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Contracts/
  - tests/FluoAxis.Contracts.Tests/
  - tests/FluoAxis.Application.Tests/
  - tests/FluoAxis.GatewayContract.Tests/
  - samples/contracts/
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
contracts:
  - import-preview-v2
  - confirmed-fam-rox-mapping-v2
  - IFluoAxisGateway-current-capability-v2
hotspots:
  - zero-based-source-coordinates
  - fam-rox-well-set-consistency
  - transient-external-file-boundary
  - contract-version-compatibility
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - DTO construction guards for source coordinates channels and well mappings
    - JSON shape and roundtrip tests for preview and confirmation contracts
    - Fake and Application gateway guard-order conformance
    - full solution build and repository test gate
    - repository relative-path policy verification
  tests_run:
    - focused Contracts tests passed 53 of 53
    - focused GatewayContract tests passed 13 of 13
    - focused Application tests passed 24 of 24 during incremental validation
    - full solution build passed with 0 errors and 51 emitted analyzer warnings in the final incremental run
    - repository MTP gate passed 503 of 503 with 0 failed and 0 skipped
    - repository path-policy verification passed for 183 text files
  benchmarks_run: []
search:
  aliases:
    - import mapping contract
    - region candidate dto
    - fam rox mapping
    - confirm mapping command
---

# Define the import preview and confirmed mapping contract

## Summary

Expose discovered import-region coordinates and an explicit user-confirmed FAM/ROX column mapping across the Presentation-to-Application boundary. This slice freezes only format-independent structural invariants; it does not invent a real workbook layout, persist imported signals, or allow an external absolute path to enter Contracts or Application.

## Plan

### Contract

- Add immutable DTOs for candidate regions, mapped well columns, and per-channel mappings.
- Make `ImportPreviewDto` carry the actual candidates rather than only a count.
- Make `ConfirmMappingCommand` carry exactly one FAM and one ROX mapping.

### Runtime

- Preserve current capability-unavailable behavior until a path-safe transient source owner and import orchestration exist.
- Preserve mutation guard ordering for version, Run existence, revision, and state.

### Data/path

- Enforce non-negative 0-based coordinates, unique column indices, unique wells, no time/well overlap, fixed channel identity, and equal FAM/ROX well sets.
- Keep OS-picker handles and machine paths outside all DTOs.

### Tests

- Add positive and negative construction tests plus deterministic JSON shape/roundtrip coverage.
- Update shared Fake/real conformance calls without claiming successful import or mapping.

### Docs

- Record the contract boundary and update the plan-completion audit conservatively.

### Review

- Manually review contract consistency, path leakage, scientific overreach, compatibility, and test coverage because the user requested primary-agent-only execution.

## Exploration log

- `ConfirmMappingCommand` currently contains only generic mutation metadata, so it cannot represent FR-IMP-006 user confirmation.
- `ImportPreviewDto` currently exposes sheet names and a candidate count, but not the header row, time column, well columns, or suggested channel needed by a mapping UI.
- Import-owned `RegionCandidate`, `ChannelRegionMapping`, and `DataMapping` already establish the structural model; Contracts cannot reference Import because that would reverse the intended dependency direction.
- The current gateway cannot inspect a real selected file because Contracts/Application intentionally have no type carrying the picker path or stream. That lifecycle remains outside this contract-only slice.
- Q-SCI-002 and Q-SCI-012 remain unresolved, so no workbook-layout assumption or numeric-cleaning policy will be added.

## Root cause

Not applicable. This is a planned contract capability.

## Implementation

- Added `WellColumnMappingDto`, `ChannelRegionMappingDto`, and `RegionCandidateDto` as Contracts-owned representations independent of the Import assembly.
- `ChannelRegionMappingDto` enforces a closed FAM/ROX vocabulary, non-negative 0-based coordinates, at least one well, unique source columns, unique logical wells, and no time-column overlap.
- `RegionCandidateDto` permits genuinely ambiguous discovery while rejecting invalid coordinates, channel suggestions, duplicate bindings, and time/well overlap. Candidate data remains advisory.
- `ConfirmMappingCommand` now carries exactly one FAM and one ROX mapping and rejects swapped channels or unequal logical-well sets. It does not impose unresolved role/replicate cardinality rules.
- `ImportPreviewDto` now carries immutable candidate-region details rather than only a scalar count; its count is derived from the candidate collection. Caller-owned collections are snapshotted and exposed read-only.
- Registered all new DTOs in `ContractJsonContext`, added source-generated serialization coverage, and froze a V2 command shape plus `import-mapping-v2.json`.
- Raised `ContractVersion.Current` from 1 to 2 because both `ConfirmMappingCommand` and `ImportPreviewDto` changed wire shape. The gateway capability manifest is now `gateway-current-capability-v2.json`.
- Updated Fake/Application guard-order fixtures without enabling import or mapping success. Both adapters remain capability-honest until source ownership and persistence exist.
- Raised the repository discovery floor to 503 and updated the completion audit without closing G1 or G3.

## Validation

- `dotnet build FluoAxis.slnx --no-restore`: passed with 0 errors. The final incremental build emitted 51 existing/new analyzer warnings, primarily established test-name and serializer-overload warnings; no zero-warning claim is made.
- Focused Contracts suite: 53 passed, 0 failed, 0 skipped. This includes coordinate/channel/well-set guards, immutable snapshots, reflection roundtrip, source-generated roundtrip, frozen V2 JSON shape, and fixture deserialization.
- Focused GatewayContract suite: 13 passed, 0 failed, 0 skipped. Fake and Application adapters preserve version, stale revision, invalid state, and capability-unavailable behavior with the new command shape.
- Focused Application suite passed 24 of 24 during incremental validation.
- Repository Microsoft.Testing.Platform gate with `--minimum-expected-tests 503`: 503 passed, 0 failed, 0 skipped.
- `build/Verify-RepositoryPolicy.ps1`: passed for 183 repository text files.
- No benchmark was run because this slice adds bounded DTO construction and no importer/runtime data path.

## Review outcomes

- Contract: the V1-to-V2 bump is intentional and required; V1 was not approved or released. Current gateways reject older versions explicitly. A V1 compatibility adapter is not claimed.
- Runtime: no worker, file IO, persistence, or state-transition behavior was added. Existing capability-unavailable behavior remains explicit.
- Data/path: DTOs contain only safe display/logical coordinates and project-relative metadata already present in `InspectImportCommand`; no picker path, stream, filesystem handle, or machine-local identity was introduced.
- Scientific scope: structural FAM/ROX equality is required by FR-IMP-009, but workbook layout, time sampling, numeric cleaning, roles, and replicate cardinality remain unresolved and untouched.
- Memory: inputs are copied into bounded read-only collections; no retained file buffer or imported row set exists in this contract slice.
- Compatibility: the breaking impact is isolated and versioned before a released contract freeze. The audit now states explicitly that Contracts V1 was never approved/frozen.
- Manual primary-agent review covered contract shape, validation gaps, mutability, source generation, adapter guard ordering, path leakage, scientific overreach, and documentation consistency. GitHub and external Codex review were not invoked.

## Follow-ups

- Define and implement the Infrastructure-owned transient picker-source lifecycle without exposing an absolute path across the application boundary.
- Add importer orchestration, managed-copy creation, SHA-256 verification, persistence, and state transition only after that ownership contract is approved.
- Obtain at least five de-identified real FAM/ROX files before qualifying candidate discovery against actual layouts.
