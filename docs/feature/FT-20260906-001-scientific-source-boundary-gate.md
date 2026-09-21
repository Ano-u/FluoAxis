---
id: FT-20260906-001
kind: feature
status: reviewed
area: delivery-governance
stage: phase-2
tasks:
  - scientific-single-source-static-gate
  - ui-js-formula-copy-regression-tests
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - build/
  - tests/FluoAxis.Architecture.Tests/
  - .github/workflows/ci.yml
  - README.md
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - deterministic-csharp-analysis-single-source-of-truth
  - no-formal-analysis-formulas-in-ui-or-javascript
hotspots:
  - scientific-formula-duplication
  - false-positive-control
  - vendored-javascript-exclusion
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - reject Analysis and scientific Method implementation symbols in presentation surfaces
    - reject representative normalization, RR, and dr-prime formula copies in CSharp and JavaScript fixtures
    - accept result rendering, generic plot math, and vendored Plotly
    - run focused architecture tests and full repository gates
  tests_run:
    - scientific source-boundary verifier passed for 10 first-party presentation files from repository cwd
    - scientific source-boundary verifier passed for 10 first-party presentation files from foreign cwd
    - Architecture focused build passed with 0 warnings and 0 errors
    - Architecture focused suite passed 12 of 12
    - solution build passed with 0 warnings and 0 errors
    - repository MTP gate passed 581 of 581 with 0 failed and 0 skipped
    - PlotHost Node suite passed 5 of 5 and node syntax check passed
    - repository path-policy gate passed for 237 text files
  benchmarks_run: []
search:
  aliases:
    - no UI JS formula copies
    - scientific source boundary gate
    - Analysis single source of truth
---

# Enforce the scientific source boundary

## Summary

Add an executable, CI-blocking boundary that prevents formal BRAF analysis implementations from being copied into WinUI, Presentation, Visualization, or first-party PlotHost code. The gate will preserve legitimate rendering of immutable backend results and generic Explorer plotting operations.

## Plan

1. Derive a narrow forbidden-symbol and formula-signature policy from the checked-out Analysis and Method contracts.
2. Implement a repository-root-derived verifier over first-party presentation surfaces, excluding vendored dependencies and generated output.
3. Add positive and negative fixtures for C# and JavaScript, including false-positive controls.
4. Wire the verifier into CI and local documented gates.
5. Validate from repository and foreign working directories, then update the completion audit conservatively.

## Exploration log

- `plan.md` G2 and the requirement baseline require a static PR gate against ViewModel/JavaScript formula copies.
- The existing exact project-reference graph already prevents Presentation, App, and Visualization from referencing `FluoAxis.Analysis`; it does not detect copied arithmetic or copied Method constants.
- Current first-party presentation and PlotHost code contains no identified formal normalization, RR, control-QC, or classification implementation.
- Vendored `plotly.min.js` contains extensive unrelated numerical code and must not be treated as first-party scientific logic.
- Result DTO names and labels may legitimately appear in Presentation, so banning every scientific noun would prevent correct immutable-result rendering.

## Root cause

Not applicable. This is a new executable governance capability required by the planned G2 exit criteria.

## Implementation

- Added `build/Verify-ScientificSourceBoundary.ps1`, rooted from its own repository location and independent of process cwd.
- The verifier scans first-party C#, XAML, JavaScript, and HTML under App, Presentation, Visualization, and PlotHost, while excluding generated output, restored dependencies, and vendored Plotly.
- It rejects direct Analysis, Domain, or Persistence implementation namespace references; checked-out Analysis/Method implementation symbols; and narrow signatures for normalization, plateau relative range, dr-prime distance, and dr-prime threshold comparisons.
- Added four regression tests covering allowed result rendering, allowed generic plot math, vendor exclusion, forbidden namespaces/symbols, and copied formulas in both C# and JavaScript.
- Wired the verifier into Windows CI and raised fail-closed test discovery to 581.

## Validation

- Scientific source-boundary verifier: passed over 10 current first-party presentation files from repository and foreign working directories.
- Architecture tests: 12 passed, 0 failed, 0 skipped.
- Solution build: passed with 0 warnings and 0 errors.
- Repository MTP gate: 581 passed, 0 failed, 0 skipped.
- PlotHost: Node syntax passed; 5 tests passed.
- Relative-path policy: passed over 237 repository text files.
- No benchmark was run because this is a bounded static governance check and changes no product runtime path.

## Review outcomes

- Contract: immutable backend result DTO names and controlled labels remain allowed; the gate targets implementations, not rendering.
- Runtime: no Analysis, UI, WebView2, worker, memory, or CPU behavior changed.
- Data/path: the script derives all paths from an explicit repository root, is cwd-independent, and emits only repository-relative violation locations.
- False-positive control: generic Explorer distance math is allowed, and third-party Plotly code is outside the first-party scientific-source audit.
- Coverage limit: regex signatures are defense in depth alongside the exact project-reference graph; deliberate heavily obfuscated arithmetic may evade a textual gate and still requires review.
- Subagent review was decomposed across requirements, engineering gates, and runtime boundaries. GitHub-hosted and external Codex review were not invoked; manual review found no blocking issue.

## Follow-ups

- This gate can prove the currently scanned first-party sources contain no recognized formal formula implementation; it cannot prove the future production Guided UI exists or that approved Golden evidence passes.
- Re-run and extend the regression vocabulary when Analysis or approved Method contracts introduce a new formal calculation family.
