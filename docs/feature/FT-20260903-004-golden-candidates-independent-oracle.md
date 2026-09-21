---
id: FT-20260903-004
kind: feature
status: validated
area: scientific-verification
stage: phase-0
tasks:
  - golden-candidate-fixtures
  - independent-reference-calculator
  - production-oracle-differential-tests
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - tests/FluoAxis.Golden.Tests/
  - samples/golden/
  - docs/method/Golden-Dataset-Design.md
contracts:
  - Method-Specification-V1.0
  - golden-candidate-schema
  - independent-oracle-boundary
hotspots:
  - scientific-threshold-boundaries
  - endpoint-selection
  - replicate-consistency
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - dotnet build tests/FluoAxis.Golden.Tests/FluoAxis.Golden.Tests.csproj --no-restore
    - dotnet run --project tests/FluoAxis.Golden.Tests/FluoAxis.Golden.Tests.csproj --no-restore
    - repository Microsoft.Testing.Platform gate with an updated minimum test count
  tests_run:
    - dotnet build tests/FluoAxis.Golden.Tests/FluoAxis.Golden.Tests.csproj --no-restore (passed, 0 warnings, 0 errors)
    - dotnet run --project tests/FluoAxis.Golden.Tests/FluoAxis.Golden.Tests.csproj --no-build --no-restore (16 passed)
    - repository Microsoft.Testing.Platform gate after governance tests were added (475 passed, 0 failed, 0 skipped)
  benchmarks_run: []
search:
  aliases:
    - golden candidates
    - independent oracle
    - differential analysis tests
    - scientific boundary fixtures
---

# Establish Golden candidates and an independent calculation oracle

## Summary

Create an explicitly unapproved candidate evidence set and a structurally independent reference calculator for differential verification of the deterministic BRAF V600E Analysis Engine. This work supplies engineering evidence only; it does not grant scientific approval to the provisional Method constants.

## Plan

1. Define compact candidate fixtures with hand-computable Raw RFU values and explicit approval metadata.
2. Implement a reference calculator that does not call `BrafAnalysisEngine`, `AnalysisMath`, or production Method calculation helpers.
3. Compare production calibration, control, and sample intermediate values against the independent result.
4. Cover pass, fail, invalid, exact-boundary, endpoint-tolerance, replicate-conflict, and scale-invariance cases.
5. Update Golden design status, run focused and repository gates, then perform a manual concern review.

## Exploration log

- The Golden test project currently contains no source tests, so repository-level Microsoft.Testing.Platform discovery exits non-zero despite all 454 existing tests passing.
- `Golden-Dataset-Design.md` describes an intended implementation as if it already exists; neither `samples/golden/` nor the reference calculator exists in the current checkout.
- Production analysis already has focused unit tests, but those reuse production types and assertions rather than providing a separately expressed calculation oracle.
- Scientific approval for the 15-second endpoint tolerance, plateau policy, LQC interpretation, and inclusive `dr'=0.4` boundary remains unresolved. All new evidence must therefore remain `candidate` and `unapproved`.
- The user requested no subagents. The mandatory decomposition is being performed manually across contract, runtime, data/path, tests, docs, and review concerns.

## Root cause

Not applicable. This is a new scientific-verification capability over an intentionally empty Golden test shell.

## Implementation

- Added a standalone oracle with its own scalar/array data model and inline formulas for endpoint selection, plateau mean/RR, NTC normalization QC, Fm/Fb, WT means, two-dimensional dr', and replicate direction.
- Kept all production-type adaptation in `GoldenCandidateTests`; the oracle does not reference production Analysis or Domain namespaces.
- Added a versioned candidate manifest bound to the current canonical Method hash and marked `candidate-unapproved`.
- Added 16 executable tests over 15 candidate IDs: positive, negative, NTC fail, plateau fail, WT fail, LQC fail, replicate conflict, missing endpoint, invalid denominator, inclusive threshold, exact RR, insufficient window, endpoint tolerance boundaries, and scale invariance.
- Compared production and oracle values for Fm/Fb, WT coordinates, LQC dr', per-well sample dr', direction, and mean where the stage is defined.

## Validation

- Golden build passed with 0 warnings and 0 errors.
- Golden run passed: 16 tests, 0 failed, 0 skipped.
- Repository MTP gate passed after governance tests were added: 475 tests, 0 failed, 0 skipped.
- No benchmark was run: the oracle is test-only and deliberately optimized for clarity rather than throughput.

## Review outcomes

- Contract: candidates are hash-bound to Method V1.0 and explicitly unapproved; no Method constant was changed or approved by this work.
- Runtime: no production runtime path changed.
- Data/path: candidate data contains no filesystem path and all build/test references are repository-relative.
- Independence: the oracle owns its inputs and formulas and does not call production calculation helpers.
- Coverage: deterministic hand-computable cases are covered; random differential tests and real-data external validity are not claimed.
- Manual primary-agent review covered contract, runtime, data/path, tests, docs, and scope. External GitHub/Codex review was not invoked.

## Follow-ups

- Scientific and engineering owners must independently inspect and dual-sign candidate fixtures before any candidate is promoted to an approved Golden Dataset.
- Real de-identified instrument exports are required before these synthetic cases can establish external validity.
- Promote candidates to per-case immutable fixtures only after scientific and engineering dual approval.
