---
id: FX-20260906-003
kind: fix
status: reviewed
area: delivery-governance
stage: phase-1
tasks:
  - scan-web-source-paths
  - reject-web-path-leaks
  - refresh-test-and-dependency-evidence
priority: P0
branch: main
pr: null
paired_doc: docs/feature/FT-20260903-005-ci-policy-and-architecture-gates.md
paths:
  - build/Verify-RepositoryPolicy.ps1
  - tests/FluoAxis.Architecture.Tests/ProjectDependencyTests.cs
  - .github/workflows/ci.yml
  - README.md
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/feature/FT-20260903-005-ci-policy-and-architecture-gates.md
  - docs/change_index.jsonl
contracts:
  - relative-path-only-ci-gate
  - fail-closed-test-discovery
  - dependency-evidence-accuracy
hotspots:
  - javascript-regex-false-positives
  - vendored-plotly-source
  - test-discovery-glob
bug:
  repro: The repository path gate omits JS HTML and CSS while README and the completion audit retain obsolete 552-test and 26-component evidence
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - Architecture focused tests
    - repository path-policy gate from repository and foreign cwd
    - repository MTP gate with 576-test floor
    - dependency evidence generation
    - registry JSONL and diff checks
  tests_run:
    - Architecture focused tests 8 passed
    - Repository MTP gate 577 passed with 0 failed and 0 skipped
    - PlotHost Node harness 5 passed
    - Repository path-policy gate passed for 235 text files from repository and foreign cwd
    - Dependency evidence generated for 42 resolved components
    - Change registry JSONL parse passed for 24 entries
    - Diff check passed apart from the pre-existing .gitignore line-ending advisory
  benchmarks_run:
    - Not applicable; policy and evidence correction only
search:
  aliases:
    - web path policy coverage
    - JavaScript absolute path scan
    - stale test floor
    - stale SBOM component count
---

# Cover web sources in the path gate

## Summary

Extend the relative-path policy gate to repository-owned JavaScript, HTML, and CSS, add regression coverage that proves those files are scanned, and refresh current test/dependency evidence without changing product or scientific behavior.

## Plan

- Include `.js`, `.html`, and `.css` while excluding generated dependency trees such as `node_modules`.
- Calibrate drive/UNC recognition so pinned minified JavaScript regular expressions are not mistaken for filesystem paths.
- Add an architecture regression using generated forbidden content in a `.js` fixture.
- Synchronize README's Windows-aware MTP glob and 576-test floor with CI.
- Correct only current-state evidence in the completion audit; preserve historical per-change counts.

## Exploration log

- The verifier currently scans 232 files but its extension allowlist omits all PlotHost source formats.
- The checked-in PlotHost contains four first-party JS/HTML/CSS files plus pinned `vendor/plotly.min.js`.
- Applying the old patterns naively to JavaScript yields false positives on regex source such as escaped whitespace and a ternary followed by a regex literal.
- JavaScript string source needs four textual backslashes to encode a two-backslash UNC prefix, matching the existing C#/PowerShell calibration.
- A drive-root pattern should require a plausible first path character after the separator; this preserves ordinary drive-path rejection while avoiding a regex literal beginning with `[` after a ternary variable.
- CI already uses the correct Windows-capable module glob and 576 floor; README is stale. Dependency evidence tests and generation currently prove 42 resolved components.

## Root cause

The initial policy allowlist covered only the .NET/documentation tree. The later local PlotHost and Windows test target were added without widening that gate or refreshing all current-state prose.

## Implementation

- Added `.js`, `.html`, and `.css` to the repository-owned text allowlist and excluded restored `node_modules` dependency trees. The checked-in pinned Plotly bundle remains scanned.
- Calibrated UNC recognition for JavaScript source escaping and tightened drive-path matching to require a plausible path-segment character after the separator. This avoids misclassifying regular-expression source while still rejecting machine-local paths.
- Added an executable architecture regression proving that JS/HTML/CSS leaks fail the verifier while representative JavaScript regular expressions pass.
- Updated README and CI to use the Windows-capable `net10.0*` module glob and the new 577-test discovery floor.
- Refreshed current-state completion-audit evidence from 552 to 577 tests and from 26 to 42 dependency components. Historical evidence in older change records was preserved.
- Updated the original CI/path-gate feature record to state the now-current web-source coverage.

## Validation

- `dotnet test tests/FluoAxis.Architecture.Tests/FluoAxis.Architecture.Tests.csproj --no-restore`: 8 passed.
- Repository Microsoft Testing Platform gate with `--minimum-expected-tests 577`: 577 passed, 0 failed, 0 skipped, including the Windows-targeted Visualization assembly.
- `node --check web/FluoAxis.PlotHost/plot-host.js`: passed.
- `node --test web/FluoAxis.PlotHost/plot-host.test.js`: 5 passed.
- `build/Verify-RepositoryPolicy.ps1`: passed for 235 repository-owned text files from both the repository root and a foreign cwd.
- `build/Generate-DependencyEvidence.ps1`: generated deterministic evidence for 42 resolved components.
- `docs/change_index.jsonl`: all 24 entries parsed as JSON objects.
- `git diff --check`: no new whitespace error; only the pre-existing `.gitignore` line-ending advisory remained.
- No benchmark was applicable because runtime product behavior did not change.

## Review outcomes

- An independent engineering-gates review found the coverage and evidence drift; the primary agent implemented and verified the repair.
- Contract/science: no frontend-backend, Method, Data, calculation, or result contract changed.
- Path/security: first-party web sources and the pinned vendor bundle are now covered; restored dependency and generated build trees remain excluded.
- False positives: representative regex escapes and a drive-like regex literal are covered by a passing fixture, while generated JS/HTML/CSS filesystem leaks are rejected.
- Tests/docs: the executable floor, README command, CI command, dependency count, and audit prose agree with current evidence.
- Review: manual self-review found no unresolved issue in this policy repair. Clean hosted CI and release qualification remain pending.

## Follow-ups

- This fix does not close G1, G7, or G8; clean hosted CI, packaging, release artifact, and final attestation evidence remain absent.
