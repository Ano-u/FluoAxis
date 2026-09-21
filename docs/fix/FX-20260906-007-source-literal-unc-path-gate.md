---
id: FX-20260906-007
kind: fix
status: reviewed
area: delivery-governance
stage: phase-1
  tasks:
  - detect-source-literal-unc-paths
  - detect-drive-root-variable-and-extended-unc-paths
  - scan-application-manifests
  - preserve-regex-false-positive-control
priority: P0
branch: main
pr: null
paired_doc: docs/fix/FX-20260906-003-web-path-policy-and-evidence-drift.md
paths:
  - build/Verify-RepositoryPolicy.ps1
  - tests/FluoAxis.Architecture.Tests/ProjectDependencyTests.cs
  - .github/workflows/ci.yml
  - README.md
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - relative-path-only-ci-gate
hotspots:
  - source-literal-escaping
  - regular-expression-false-positives
  - unc-path-leakage
bug:
  repro: Literal-preserving source forms can express a UNC path with two textual leading separators while the verifier requires four for source files; application manifest files are not scanned at all
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - reject two-separator UNC literals in PowerShell C# verbatim C# raw and JavaScript String.raw forms
    - reject forbidden paths in application manifest fixtures
    - retain acceptance of representative escaped regular-expression source
    - run Architecture and full repository gates
    - run repository path policy from repository and foreign working directories
  tests_run:
    - Architecture focused suite (15 passed, 0 failed, 0 skipped)
    - repository solution suite before the final path regression (602 passed, 0 failed, 0 skipped)
    - solution build (158 analyzer warnings, 0 errors)
    - repository path policy from repository cwd (251 text files passed)
    - repository path policy from foreign cwd (251 text files passed)
    - scientific source-boundary policy (11 first-party presentation files passed)
  benchmarks_run:
    - not applicable; static repository policy only
search:
  aliases:
    - source literal UNC path gate
    - PowerShell UNC bypass
    - verbatim raw string UNC bypass
---

# Close source-literal UNC path-policy bypass

## Summary

Correct the repository path-policy verifier so source forms that preserve backslashes literally cannot hide a machine-local UNC path behind the existing four-character source-escape assumption.

## Plan

1. Add regression fixtures for PowerShell quoted strings, C# verbatim/raw strings, and JavaScript `String.raw`.
2. Detect an exact two-separator UNC prefix only when it is followed by plausible server and share segments.
3. Preserve the existing four-separator detection for ordinarily escaped C#/JavaScript strings and retain regex false-positive fixtures.
4. Run focused, full, cwd-independent, and registry validation before closing the fix.

## Exploration log

- The verifier currently assigns four textual leading separators to every `.cs`, `.js`, and `.ps1` file.
- That is correct only for ordinary C#/JavaScript escaped strings. PowerShell strings do not treat backslash as an escape, and C# verbatim/raw strings plus JavaScript `String.raw` also preserve a two-separator UNC prefix.
- An isolated PowerShell fixture containing a quoted UNC path passed the current verifier, proving a fail-open release-gate bypass.
- The repository owns `src/FluoAxis.App/app.manifest`, but `.manifest` is absent from the scanned extension allowlist, creating a second fail-open path.
- Matching any pair of backslashes would misclassify regular-expression source such as escaped whitespace. A UNC candidate therefore needs both a plausible server segment and a following share separator.

## Root cause

The earlier false-positive calibration modeled escaping by file extension rather than by the literal form within the source file.

## Implementation

- Added a literal-preserving UNC detector that requires a two-separator prefix, bounded server token, share separator, and non-empty share token. This catches PowerShell strings, C# verbatim/raw strings, and JavaScript `String.raw` without treating isolated regular-expression escapes as paths.
- Preserved the existing four-separator detector for ordinary escaped C#/JavaScript source forms.
- Added `.manifest` to the repository-owned text allowlist so the WinUI application manifest cannot bypass the path gate.
- Expanded drive-root detection to reject a bare root and variable-led first segments, and added explicit extended-UNC detection. Rooted POSIX detection now covers the recognized machine-root families without misclassifying OOXML package part names or scientific notation.
- Added executable fixtures for all four literal-preserving source forms and extended the WinUI markup fixture with an application-manifest UNC leak.

## Validation

- Architecture tests pass 15/15, including four generated source-literal UNC leaks, extended UNC, bare/variable-led drive roots, general rooted POSIX, and manifest/markup cases.
- The solution-wide test run before the final path regression passed 602/602; the solution builds with 0 errors and 158 analyzer warnings. The final CI-style discovery run is recorded after updating the test floor.
- The path policy passes over the current 251 repository-owned text files from both repository and foreign working directories.
- Scientific-boundary policy passes over 11 first-party presentation files.

## Review outcomes

- An independent gate review reproduced the two-separator source-literal bypass and separately found the unscanned application manifest. Both findings are covered by executable regressions.
- False-positive control retains representative JavaScript regex acceptance and requires a plausible server/share shape for two-separator candidates.
- No runtime, frontend-backend, Method, Data, persistence, archive, or scientific calculation contract changed.
- GitHub review was not invoked; primary-agent review and executable checks are complete.

## Follow-ups

- Keep the gate conservative and add a new fixture whenever another source literal form is introduced.
