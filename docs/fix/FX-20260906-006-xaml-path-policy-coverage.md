---
id: FX-20260906-006
kind: fix
status: reviewed
area: delivery-governance
stage: phase-1
tasks:
  - scan-winui-xaml-paths
  - reject-xaml-path-leaks
priority: P0
branch: main
pr: null
paired_doc: docs/fix/FX-20260906-003-web-path-policy-and-evidence-drift.md
paths:
  - build/Verify-RepositoryPolicy.ps1
  - tests/FluoAxis.Architecture.Tests/ProjectDependencyTests.cs
  - docs/feature/FT-20260903-005-ci-policy-and-architecture-gates.md
  - docs/change_index.jsonl
contracts:
  - relative-path-only-ci-gate
hotspots:
  - winui-xaml-path-leak-detection
  - escaped-unc-recognition
bug:
  repro: repository path-policy allowlist omits WinUI XAML source files
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - reject drive UNC and file URI path forms in XAML fixtures
    - run focused architecture tests
    - run repository and foreign-cwd path-policy gates
    - run full repository test discovery
  tests_run:
    - Architecture focused build passed with 0 warnings and 0 errors
    - Architecture focused suite passed 13 of 13
    - repository path-policy gate passed for 242 text files from repository and foreign cwd
    - scientific source-boundary gate passed for 10 first-party presentation files
    - PlotHost Node syntax check passed and suite passed 7 of 7
    - solution build passed with 0 warnings and 0 errors
    - repository MTP gate passed 582 of 582 with 0 failed and 0 skipped
  benchmarks_run: []
search:
  aliases:
    - XAML path policy
    - WinUI absolute path leak
    - repository policy extension coverage
---

# Cover WinUI XAML in the path gate

## Summary

Extend the repository relative-path verifier to WinUI XAML so machine-local paths cannot bypass the release gate through declarative UI files.

## Plan

1. Add `.xaml` to the repository-owned text allowlist.
2. Extend the architecture fixture to prove XAML path leaks fail closed.
3. Run the focused, cwd-independent, and repository-wide validation gates.
4. Update the original policy feature and completion evidence without rewriting historical counts.

## Exploration log

- The current repository contains `src/FluoAxis.App/App.xaml` and `src/FluoAxis.App/MainWindow.xaml`.
- `Verify-ScientificSourceBoundary.ps1` already scans `.xaml`, but `Verify-RepositoryPolicy.ps1` does not.
- Consequently an absolute path in WinUI markup would evade the non-negotiable relative-path gate.
- XAML stores ordinary attribute text and needs the two-character UNC prefix rule used for markup rather than source-language string escaping.

## Root cause

The path verifier's extension allowlist was expanded for first-party web assets after those assets were introduced, but the original WinUI XAML files were never added to the list.

## Implementation

- Added `.xaml` to the repository-owned text extension allowlist.
- Added an architecture regression that constructs drive-rooted, UNC, and file-URI path forms at runtime in XAML fixtures and proves all three fail closed.
- Preserved the markup-specific two-character UNC recognition; source-language escaping remains limited to C#, JavaScript, and PowerShell.
- Raised the documented and CI fail-closed MTP discovery floor to 582.

## Validation

- Architecture project build: passed with 0 warnings and 0 errors.
- Architecture suite: 13 passed, 0 failed, 0 skipped.
- Repository path-policy verification: passed for 242 text files from repository and foreign working directories.
- Scientific source-boundary verification: passed for 10 first-party presentation files.
- PlotHost: Node syntax passed; 7 tests passed.
- Solution build: passed with 0 warnings and 0 errors.
- Repository MTP gate: 582 passed, 0 failed, 0 skipped.
- No benchmark was run because this changes only a static repository scanner and its tests.

## Review outcomes

- Path contract: both current WinUI XAML files now participate in the non-negotiable relative-path release gate.
- False-positive control: current XAML passes unchanged; the new fixture proves only forbidden filesystem forms are rejected.
- Cwd independence: both repository-root and foreign-cwd invocations scan the same 242 files.
- Runtime/science: no UI behavior, PlotHost behavior, Method, Data, or Analysis calculation changed.
- Manual review found no blocking issue; GitHub-hosted review was not invoked.

## Follow-ups

- Reassess the allowlist whenever a new repository-owned declarative source format is introduced.
