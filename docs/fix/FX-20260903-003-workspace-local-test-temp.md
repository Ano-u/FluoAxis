---
id: FX-20260903-003
kind: fix
status: validated
area: test-infrastructure
stage: phase-1
tasks:
  - enforce-workspace-local-test-temp
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - tests/FluoAxis.ProjectFormat.Tests/PackageTestFactory.cs
  - tests/FluoAxis.ProjectFormat.Tests/PortabilityAndAtomicTests.cs
  - docs/method/Data-Contract-V1.0.md
contracts:
  - relative-path-policy
  - parallel-test-temp-uniqueness
hotspots:
  - test-cleanup
  - cwd-independence
bug:
  repro: ProjectFormat tests create temporary files under the operating-system global temporary directory.
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - dotnet run --project tests/FluoAxis.ProjectFormat.Tests/FluoAxis.ProjectFormat.Tests.csproj --no-restore
    - dotnet build FluoAxis.slnx --no-restore
  tests_run:
    - dotnet run --project tests/FluoAxis.ProjectFormat.Tests/FluoAxis.ProjectFormat.Tests.csproj --no-restore (35 passed)
    - dotnet build FluoAxis.slnx --no-restore (passed, 0 errors)
  benchmarks_run: []
search:
  aliases:
    - test temp root
    - artifacts test-tmp
    - path policy
---

# Enforce workspace-local test temporary paths

## Summary

Move ProjectFormat test files from the global operating-system temporary directory to a unique repository-local `artifacts/test-tmp` root, with containment-checked cleanup.

## Plan

1. Resolve the repository root independently of the process current working directory.
2. Allocate every test root under `artifacts/test-tmp/FluoAxis.ProjectFormat.Tests/<process>/<unique-id>`.
3. Validate cleanup containment before recursive deletion.
4. Update the Data Contract so it no longer permits the conflicting global-temp alternative.
5. Run ProjectFormat tests, a solution build, and relative-path/cwd-independence review.

## Exploration log

- `TempRoot` currently starts from `Path.GetTempPath()`, contrary to the repository-wide hard release gate.
- Cleanup recursively deletes `Root` without proving that it belongs to an approved repository-local artifact root.
- The existing GUID suffix provides uniqueness, but no explicit process/test-worker segment or repository containment guarantee.
- The draft Data Contract still permits operating-system temporary paths and conflicts with the current repository instructions.

## Root cause

The earlier draft Data Contract allowed global temporary storage. ProjectFormat tests implemented that superseded option before the stricter repository-local path policy became authoritative.

## Implementation

- Added assembly-location-based repository discovery guarded by the repository solution and instruction files.
- Allocated test roots under `artifacts/test-tmp/FluoAxis.ProjectFormat.Tests/<process>/<test>/<unique-id>`.
- Added a full-path containment check before recursive cleanup.
- Removed the global operating-system temporary directory allowance from the Data Contract.
- Updated the package leakage assertion to check that repository-local test artifact identifiers are not serialized.

## Validation

- ProjectFormat tests: 35 passed, 0 failed.
- The test run left no files below the project-specific artifact root.
- Full solution build: passed with 0 errors; existing analyzer warnings remain in test projects.
- Source search found no remaining global-temp API usage outside the preserved exploration history.
- `git diff --check`: passed.
- No benchmark was required because this affects test fixture placement only.

## Review outcomes

- Manual review passed for cwd independence, parallel uniqueness, cleanup containment, repository-relative policy, and package path leakage.
- GitHub and external Codex review were not requested for this local uncommitted baseline.

## Follow-ups

- Reuse the same tested helper pattern when additional test projects begin creating filesystem fixtures.
