---
id: FX-20260903-007
kind: fix
status: reviewed
area: visualization-protocol
stage: phase-1
tasks:
  - restore-plotspec-schema-version
priority: P0
branch: main
pr: null
paired_doc: docs/feature/FT-20260903-013-exact-plot-point-distance.md
paths:
  - src/FluoAxis.PlotProtocol/PlotSpec.cs
  - tests/FluoAxis.PlotProtocol.Tests/PlotProtocolTests.cs
  - docs/feature/FT-20260903-012-plot-protocol-foundation.md
  - docs/change_index.jsonl
contracts:
  - plot-spec-v1
hotspots:
  - schema-versioning
  - serialization-compatibility
bug:
  repro: PlotSpec JSON lacks the required schemaVersion member
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - PlotSpec JSON contains schemaVersion 1
    - unsupported schema versions fail construction and deserialization
    - focused and repository-wide regression gates
  tests_run:
    - PlotProtocol focused suite 22 passed
    - solution build passed with 0 warnings and 0 errors
    - repository MTP gate 552 passed
    - repository path-policy gate passed for 211 text files
    - git diff check passed with only the pre-existing gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; wire-contract correction only
search:
  aliases:
    - PlotSpec schemaVersion
    - plot contract version
---

# Restore the required PlotSpec schema version

## Summary

Correct the newly introduced `PlotSpec` contract so its serialized DTO carries the `schemaVersion` required by the architecture baseline. This is addressed before adding `PlotDataSnapshot`, preventing two inconsistent versioning conventions from becoming established.

## Plan

- Add an immutable `SchemaVersion` property whose only accepted V1 value is `1`.
- Freeze its camel-case JSON shape and reject unsupported versions through construction and source-generated deserialization tests.
- Re-run focused and repository-wide protocol gates.

## Exploration log

- The bridge envelope has `schemaVersion`, but `PlotSpec` itself does not.
- The requirement baseline section 29 explicitly states DTOs must include `schemaVersion` and names `PlotSpec` as a source-generated JSON type.
- No PlotSpec has yet been persisted or consumed by a renderer, so the correction can be made without an archive migration or runtime compatibility adapter.

## Root cause

FT-20260903-012 interpreted the envelope schema as sufficient for all nested payload types. That conflicts with the explicit DTO-level version requirement.

## Implementation

- Added immutable `PlotSpec.SchemaVersion` with a default/current value of 1.
- Centralized the current plot-protocol version and fail-closed validation in `PlotProtocolVersions`.
- Added source-generated JSON regression coverage proving camel-case `schemaVersion: 1` output and rejection of unsupported construction/deserialization values.
- Applied the same version convention to the paired `PlotDataSnapshot` feature.

## Validation

- Focused PlotProtocol suite: 22 passed.
- Full solution build: passed with 0 warnings and 0 errors.
- Full repository MTP gate: 552 passed.
- Path-policy verifier: passed for 211 text files.
- Registry JSONL parse and `git diff --check`: passed; only the pre-existing `.gitignore` line-ending advisory was emitted.
- No benchmark was needed for this serialization-contract repair.

## Review outcomes

- The root PlotSpec DTO now satisfies the baseline's DTO-level schema-version requirement independently of the bridge-envelope version.
- Unsupported versions fail at construction and source-generated deserialization rather than being silently interpreted as V1.
- No deployed renderer, persisted PlotSpec, archive entry, or migration exists, so this correction has no backward-compatibility migration burden.
- Manual self-review found no remaining schema-version defect in the PlotSpec root. GitHub and external Codex review were not required or invoked.

## Follow-ups

- Apply the same explicit version rule to the new PlotDataSnapshot contract.
