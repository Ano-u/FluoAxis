---
id: FT-20260913-001
kind: feature
status: validated
area: governance-and-delivery
stage: cross-phase
tasks:
  - audit-requirements-completion
  - reconcile-documentation-with-checkout
  - select-next-implementation-slice
priority: P0
branch: main
pr: null
paired_doc: docs/fix/FX-20260913-001-workflow-command-atomicity.md
paths:
  - BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md
  - README.md
  - plan.md
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/feature/FT-20260912-001-application-engine-orchestration.md
  - docs/feature/FT-20260913-001-requirements-implementation-audit.md
contracts:
  - requirements-traceability
  - evidence-based-completion
  - phase-gate-claims
hotspots:
  - stale-documentation
  - scientific-approval
  - implementation-overclaim
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - compare requirement sections and acceptance criteria with current source tests and documents
    - run the focused workflow and full repository validation gates
    - run the relative-path and scientific-source policy gates
  tests_run: []
  benchmarks_run: []
search:
  aliases:
    - requirements implementation audit
    - completion truth check
    - next implementation slice
---

# Reconcile the BRAF requirements baseline with the current implementation

## Summary

Audit the current checkout against the frozen requirements baseline, distinguish implemented components from accepted product capabilities, correct stale completion claims, and select the highest-value implementation slice that can be closed without inventing unresolved scientific rules.

## Plan

1. Compare the requirement baseline, plan, Method/Data Contracts, ADRs, source, and executable tests.
2. Classify each major capability as implemented, partial, missing, or decision-blocked.
3. Record documentation claims that no longer match the checkout.
4. Complete the selected Application-to-Analysis workflow slice and its paired atomicity fix.
5. Re-run focused and repository-wide validation, then update the evidence matrix.

## Exploration log

- The prior 44-criterion audit correctly prevents phase-level overclaiming but predates the current Application workflow implementation.
- Current source includes mapping, assignment, calibration, control QC, sample analysis, SQLite workflow persistence, and synthetic end-to-end tests while the README and related change documents still describe those mutations as unavailable or stub-only.
- The working tree is mostly untracked relative to the initial `main` commit; completion must be based on current files and executable evidence rather than commit history.

## Root cause

Not applicable. This is a traceability and delivery-governance change.

## Implementation

The audit selected the Application-to-Analysis workflow as the next executable slice. The current checkout now records the implementation evidence, action contract alignment, source identity revalidation, and mapping snapshot persistence in the paired changes.

## Validation

Current focused evidence: solution build passes with SDK 10.0.400; Application 33/33, Gateway Contract 22/22, Infrastructure 29/29, and SQLite Integration 3/3 pass. Repository path policy and scientific source boundary gates pass. Scientific Method and Golden approval remain open by design.

## Review outcomes

Manual review completed. The repository still must not claim a production-ready Guided UI, approved Golden Dataset, report/export completion, or closed stage-command atomicity until those separate gates are implemented and validated.

## Follow-ups

_To be completed._
