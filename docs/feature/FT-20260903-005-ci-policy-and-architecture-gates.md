---
id: FT-20260903-005
kind: feature
status: validated
area: delivery-governance
stage: phase-1
tasks:
  - windows-ci
  - repository-path-policy-gate
  - architecture-dependency-gate
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - .github/workflows/
  - build/
  - tests/FluoAxis.Architecture.Tests/
  - FluoAxis.slnx
  - README.md
contracts:
  - relative-path-only
  - clean-architecture-dependency-direction
  - locked-build-test-gate
hotspots:
  - path-leak-detection
  - cwd-independence
  - ci-test-discovery
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - run repository policy verifier from repository root and a different cwd
    - build and run architecture tests
    - locked restore, solution build, and repository MTP gate
  tests_run:
    - repository path-policy verifier from repository root (passed, 165 files)
    - repository path-policy verifier from parent cwd with explicit repository root (passed, 165 files)
    - dotnet build tests/FluoAxis.Architecture.Tests/FluoAxis.Architecture.Tests.csproj --no-restore (passed, 0 warnings, 0 errors)
    - dotnet run --project tests/FluoAxis.Architecture.Tests/FluoAxis.Architecture.Tests.csproj --no-build --no-restore (5 passed)
    - dotnet restore FluoAxis.slnx --locked-mode (passed)
    - dotnet build FluoAxis.slnx --no-restore (passed, 110 pre-existing test analyzer warnings, 0 errors)
    - repository Microsoft.Testing.Platform gate with minimum 475 (475 passed, 0 failed, 0 skipped)
  benchmarks_run: []
search:
  aliases:
    - windows ci
    - path policy gate
    - architecture tests
    - dependency direction
---

# Add executable CI, path-policy, and architecture gates

## Summary

Turn the repository's locked build, fail-closed test discovery, relative-path-only policy, and Clean Architecture dependency rules into executable local and Windows CI gates.

## Plan

1. Inventory actual project references and define the currently approved dependency graph.
2. Add a repository-root-derived policy verifier for forbidden filesystem path forms and generated/build metadata.
3. Add architecture tests that parse project files and reject forbidden or circular project references.
4. Add a pinned Windows GitHub Actions workflow for locked restore, policy verification, build, and test discovery.
5. Validate from the repository root and a foreign cwd, then update audit evidence.

## Exploration log

- No `.github/`, `build/`, or architecture-test project exists in the current checkout.
- Local locked restore and the 475-test module gate pass. The final full solution build emits 110 pre-existing test analyzer warnings and no errors; the new architecture project itself builds with zero warnings.
- The user requested primary-agent-only execution. Required decomposition is manual across contract, runtime, data/path, tests, docs, and review.

## Root cause

Not applicable. This is a new governance and delivery capability.

## Implementation

- Added `build/Verify-RepositoryPolicy.ps1`, which derives its root from the script location, excludes generated/tool state and dependency restore trees, scans repository-owned .NET, documentation, JavaScript, HTML, and CSS text, permits HTTPS references, and rejects drive-rooted, UNC, file-URI, and machine-rooted POSIX forms.
- Added five executable architecture tests: exact production project graph, acyclicity, test-reference containment/existence, path-policy acceptance from a foreign cwd, and rejection of four forbidden path classes.
- Added the architecture test project to the solution and generated its locked dependency file.
- Added a Windows 2025 GitHub Actions workflow with immutable commit SHAs for checkout/setup-dotnet, least-privilege contents access, dependency caching, policy verification, locked restore, build, and fail-closed discovery. The maintained floor is now 480 tests.
- Updated README commands to match the executable local/CI contract.

## Validation

- Path verifier passed from the repository root and from its parent cwd over 165 repository text files.
- Architecture focused build passed with 0 warnings and 0 errors; five focused tests passed.
- Locked solution restore passed. Full solution build passed with 110 existing analyzer warnings and 0 errors.
- Repository test gate passed: 475 tests, 0 failed, 0 skipped.
- No benchmark was run because this change affects governance scripts and test discovery, not a production runtime path.

## Review outcomes

- Contract: exact currently approved production references are fail-closed; adding a project or dependency requires an intentional test update.
- Runtime: no product runtime behavior or worker budget changed.
- Data/path: the verifier is cwd-independent and its own negative tests construct forbidden values only at runtime under a contained workspace test root.
- CI security: third-party actions are pinned to immutable commits and workflow permissions are read-only.
- Limit: the workflow has been locally simulated only; no GitHub-hosted run exists because this checkout has not been committed or pushed.
- Manual primary-agent review covered contract, runtime, data/path, tests, docs, and CI. External GitHub/Codex review was not invoked.

## Follow-ups

- A successful local simulation does not substitute for a completed run on a clean GitHub-hosted Windows runner.
