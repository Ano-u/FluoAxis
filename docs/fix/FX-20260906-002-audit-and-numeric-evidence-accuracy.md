---
id: FX-20260906-002
kind: fix
status: reviewed
area: documentation-and-verification
stage: phase-0
tasks:
  - current-topology-documentation
  - floating-point-boundary-evidence
  - gate-blocking-language
priority: P1
branch: main
pr: null
paired_doc: docs/feature/FT-20260903-002-plan-completion-audit.md
paths:
  - README.md
  - docs/adr/ADR-001-toolchain.md
  - docs/method/Method-Specification-V1.0.md
  - docs/method/Data-Contract-V1.0.md
  - docs/method/Golden-Dataset-Design.md
  - docs/devlog/2026-09-01-phase0-baseline.md
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - tests/FluoAxis.Analysis.Tests/AnalysisMathTests.cs
  - tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs
  - docs/change_index.jsonl
contracts:
  - repository-topology-description
  - binary64-boundary-verification-wording
  - phase-gate-blocking-semantics
hotspots:
  - frozen-scientific-threshold-preservation
  - evidence-claim-accuracy
bug:
  repro: Current docs describe an already-present WinUI shell as future work and incorrectly call decimal 0.4 and 0.01 exactly representable binary64 values
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - focused Analysis tests
    - full repository MTP gate
    - repository path-policy gate
    - change registry JSONL parse
    - documentation consistency search
  tests_run:
    - Analysis focused tests 63 passed
    - Golden focused tests 16 passed
    - Repository MTP gate 576 passed with 0 failed and 0 skipped
    - Relative-path policy gate passed for 232 text files
    - Change registry JSONL parse passed for 23 entries
    - Documentation consistency search passed
    - Diff check passed apart from the pre-existing .gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; documentation and verification-fixture correction only
search:
  aliases:
    - floating point exact boundary
    - stale repository topology
    - Q-SCI-001 gate blocking
---

# Correct audit and numeric evidence claims

## Summary

Align repository-facing documentation with the current solution topology and correct binary64 boundary evidence language without changing any approved threshold, comparison operator, scientific formula, or result label.

## Plan

- Describe the existing App/Presentation/Visualization spike accurately and count current projects from the checked-out solution.
- Replace false claims that decimal `0.4` and `0.01` are exactly representable in binary64 with operational equality language.
- Use an integer-valued plateau fixture whose computed binary64 ratio equals the configured `0.01` threshold value.
- Clarify that Q-SCI-001 permits compatibility infrastructure but blocks G0 and formal Method/Analysis completion.
- Re-run focused and full validation plus path and registry gates.

## Exploration log

- `README.md` reports 10 production and 12 test projects and says the WinUI shell is future work; the current checkout contains 12 production and 13 test projects, including App, Presentation, and Visualization.
- ADR-001 likewise describes the already-added Windows projects and version qualification in future tense.
- Method-Specification §12 calls decimal `0.4` a representable double and calls the `{0.995, 1.005}` RR construction exact. Neither decimal has a finite binary representation.
- Existing tests already partially acknowledge that the RR inputs are not exact binary values, but their names and adjacent comments retain the contradictory claim.
- `{199, 201}` provides exact integer subtraction and averaging, and binary64 division `2 / 200` produces the same runtime value as the configured `0.01` double threshold.
- Data-Contract says Q-SCI-001 does not block implementation, while plan.md and the completion audit correctly treat it as a G0 decision blocker. These statements refer to different scopes and need an explicit distinction.
- The same stale `exact` and non-blocking wording also appears in Golden Dataset design and the Phase 0 devlog, so consistency requires correcting those records without rewriting their historical scientific decision status.

## Root cause

Documentation and verification prose was not updated after the Windows spike landed, and mathematical decimal equality was conflated with equality between rounded binary64 runtime values.

## Implementation

- Updated README and ADR-001 to describe the current 12-production-project, 13-test-project topology and the existing WinUI/Visualization technical spike without presenting it as a production Guided UI.
- Corrected Method and Golden Dataset language so decimal thresholds are not described as exactly representable binary64 values. The approved `dr' >= 0.4` and `RR <= 0.01` rules remain unchanged.
- Replaced the RR boundary fixture with `{199, 201}`: integer subtraction and averaging are exact, and the final division produces the same binary64 value as the configured `0.01` threshold.
- Updated the distance boundary test to assert equality with the configured binary64 threshold value and retained the inclusive positive result assertion.
- Clarified Q-SCI-001 consistently: compatible infrastructure may proceed, but G0, formal Method freeze, and an Analysis Core completion claim remain blocked pending user decision.
- Refined G4 wording to distinguish the existing visualization spike from the absent production Guided UI, Excel export, and end-to-end database integration.

## Validation

- `dotnet test tests/FluoAxis.Analysis.Tests/FluoAxis.Analysis.Tests.csproj --no-restore`: 63 passed.
- `dotnet test tests/FluoAxis.Golden.Tests/FluoAxis.Golden.Tests.csproj --no-restore`: 16 passed.
- Repository Microsoft Testing Platform fail-closed gate: 576 passed, 0 failed, 0 skipped.
- Repository relative-path gate: 232 text files passed.
- `docs/change_index.jsonl`: all 23 nonblank entries parsed as JSON objects.
- Consistency search found no remaining stale project-count, future-WinUI, decimal-exactness, or unqualified Q-SCI-001 non-blocking claims in repository-owned docs/tests.
- `git diff --check` found no new whitespace error; only the pre-existing `.gitignore` line-ending advisory remained.
- No benchmark was applicable because no runtime algorithm or performance behavior changed.

## Review outcomes

- Concern decomposition: an independent requirements/audit review identified the drift; the primary agent implemented and verified the correction.
- Contract/science: no threshold, operator, formula, Method parameter, result label, or wire/data shape changed.
- Runtime: only test fixture values and assertions changed; production Analysis code is untouched.
- Data/path: no persisted field or filesystem behavior changed, and the path-policy gate passed.
- Tests: focused Analysis/Golden and the full repository suite passed.
- Docs: topology, numerical evidence, gate wording, audit, Golden design, and historical devlog now use consistent scoped claims.
- Review: manual self-review found no unresolved defect in this correction. GitHub review was unavailable and not required for this narrow fix; the broader product gates remain open.

## Follow-ups

- Do not treat these evidence corrections as approval of unresolved Q-SCI or Q-PRD decisions.
