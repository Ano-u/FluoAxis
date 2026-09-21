---
id: FX-20260903-002
kind: fix
status: validated
area: scientific-analysis
stage: phase-2
tasks:
  - correct-endpoint-selection
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Analysis/AnalysisMath.cs
  - src/FluoAxis.Domain/BrafMethodSnapshot.cs
  - tests/FluoAxis.Analysis.Tests/AnalysisMathTests.cs
  - tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs
  - docs/method/Method-Specification-V1.0.md
  - docs/method/Data-Contract-V1.0.md
  - docs/method/Golden-Dataset-Design.md
  - docs/devlog/2026-09-01-phase0-baseline.md
contracts:
  - FR-TIME-005
  - FR-TIME-006
  - FR-TIME-007
  - endpoint-selection
hotspots:
  - scientific-result-selection
  - missing-endpoint
bug:
  repro: The 15-second Method tolerance is subtracted directly from endpoint minutes, accepting points from 105 minutes onward without an upper bound.
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - dotnet run --project tests/FluoAxis.Analysis.Tests --no-restore
    - dotnet run --project tests/FluoAxis.Domain.Tests --no-restore
    - dotnet build FluoAxis.slnx --no-restore
  tests_run:
    - dotnet build src/FluoAxis.Analysis/FluoAxis.Analysis.csproj --no-restore (passed, 0 warnings, 0 errors)
    - dotnet run --project tests/FluoAxis.Analysis.Tests/FluoAxis.Analysis.Tests.csproj --no-build --no-restore (63 passed)
    - dotnet run --project tests/FluoAxis.Domain.Tests/FluoAxis.Domain.Tests.csproj --no-build --no-restore (243 passed)
    - dotnet build FluoAxis.slnx --no-restore (passed, 110 pre-existing test analyzer warnings, 0 errors)
  benchmarks_run: []
search:
  aliases:
    - endpoint tolerance
    - endpointToleranceSeconds
    - missing endpoint
    - 120 minute endpoint
---

# Correct the 120-minute endpoint tolerance

## Summary

Align endpoint selection with the requirement baseline: the configurable value is measured in seconds, no interpolation is allowed, and a point outside the small tolerance around 120 minutes is not a legal endpoint.

## Plan

1. Replace the one-sided 15-minute interpretation with a closed 15-second window around the configured endpoint.
2. Select the candidate nearest the nominal endpoint, using the later point as the deterministic equal-distance tie-break.
3. Update focused tests, Method/Data/Golden documentation, and the prior exploration history.
4. Run focused analysis/domain validation, solution build, path audit, and manual contract review.

## Exploration log

- The requirement baseline defines `endpointToleranceSeconds: 15.0`, allows a small configurable device timestamp tolerance, forbids default interpolation, and requires Invalid when no legal 120-minute endpoint exists.
- The implementation subtracted the seconds-valued property directly from minutes, producing a 105-minute lower bound.
- The implementation had no upper bound and selected the latest qualifying point, so arbitrarily late acquisitions could become the formal endpoint.
- Existing unit tests and draft Golden documentation encoded the same 105-minute interpretation and therefore did not detect the defect.

## Root cause

The draft Method prose introduced a worked example that treated the numeric tolerance as minutes despite the schema naming and requirement baseline defining seconds. Implementation and tests followed that inconsistent example instead of the higher-priority baseline contract.

## Implementation

- Converted the configured tolerance with `TimeSpan.FromSeconds` instead of subtracting its numeric value from minutes.
- Restricted candidates to the closed interval around the nominal endpoint.
- Selected the nearest candidate and used the later point as the deterministic equal-distance tie-break.
- Replaced the stale 105-minute and unbounded-late-point tests with lower/upper boundary, nearest-candidate, tie-break, and custom-tolerance coverage.
- Corrected Method, Data Contract, Golden design, and prior development-history documentation without erasing the rejected interpretation.

## Validation

- Analysis project build: passed with 0 warnings and 0 errors.
- Analysis tests: 63 passed, 0 failed.
- Domain tests: 243 passed, 0 failed.
- Full solution build: passed with 0 errors; it reports 110 existing analyzer warnings in test projects, outside this scientific fix.
- `git diff --check`: passed.
- Focused absolute-path audit: no match in changed code, tests, or change document.
- Active source/test/Method search found no remaining 105-minute or unbounded-late endpoint semantics.
- No benchmark was required: endpoint selection remains a bounded linear scan over one signal series and this change does not alter its complexity.

## Review outcomes

- Manual self-review completed for scientific contract consistency, seconds/minutes conversion, both tolerance boundaries, deterministic selection, no interpolation, path discipline, and regression coverage.
- GitHub and external Codex review were not requested for this local uncommitted baseline.

## Follow-ups

- The default remains an engineering default pending validation against representative instrument exports, as required by the baseline.
- The repository-wide test command and empty Application/Golden/Persistence suites remain tracked separately by `FX-20260903-001`.
