---
id: FX-20260903-004
kind: fix
status: validated
area: path-policy
stage: phase-1
tasks:
  - remove-forbidden-test-literals
  - calibrate-path-policy-verifier
priority: P0
branch: main
pr: null
paired_doc: docs/feature/FT-20260903-005-ci-policy-and-architecture-gates.md
paths:
  - build/Verify-RepositoryPolicy.ps1
  - tests/FluoAxis.Contracts.Tests/ConstructionGuardTests.cs
  - tests/FluoAxis.Domain.Tests/RelativePathTests.cs
  - tests/FluoAxis.ProjectFormat.Tests/PackageSecurityTests.cs
  - tests/FluoAxis.ProjectFormat.Tests/PortabilityAndAtomicTests.cs
contracts:
  - relative-path-only
hotspots:
  - negative-path-fixtures
  - false-positive-control
bug:
  repro: repository path-policy scan finds committed absolute path literals in negative tests and misclassifies escaped separator syntax
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - run policy verifier from repository root and foreign cwd
    - run affected Contracts, Domain, and ProjectFormat tests
    - run repository MTP gate
  tests_run:
    - repository path-policy verifier from repository root (passed, 165 files)
    - repository path-policy verifier from parent cwd (passed, 165 files)
    - architecture path-policy acceptance and rejection tests (passed)
    - repository Microsoft.Testing.Platform gate with minimum 475 (475 passed, 0 failed, 0 skipped)
  benchmarks_run: []
search:
  aliases:
    - forbidden path literals
    - path policy false positives
    - negative fixture construction
---

# Remove forbidden filesystem-path literals from tests

## Summary

Preserve negative path-validation coverage without committing machine-specific absolute filesystem literals, and make the repository verifier distinguish actual path forms from escaped separator syntax and generic policy prose.

## Plan

1. Record every initial verifier finding and classify true violations versus syntax/prose false positives.
2. Construct forbidden negative inputs at runtime from path tokens instead of storing complete absolute literals.
3. Tighten verifier signatures so relative separator tests and generic policy descriptions do not fail.
4. Run focused tests, cwd-independence checks, and the full repository gate.

## Exploration log

- The first full scan reported 20 findings. Five are true committed absolute-path or URI fixtures/assertions; the rest are relative separator literals, character literals, or generic prose such as the name of a forbidden URI scheme.
- Existing tests correctly reject rooted inputs, so the behavioral assertions must remain while their source representation changes.
- The user requested no subagents; contract, data/path, tests, docs, and review are decomposed manually.

## Root cause

Negative tests encoded forbidden machine path forms as complete source literals, conflicting with the repository's stronger rule that fixtures and tests are also relative-path-only. The first verifier regex also treated any escaped separator token or scheme name as a complete path.

## Implementation

- Replaced complete absolute-path literals in Contracts, Domain, and ProjectFormat negative tests with runtime token composition while preserving the same rejected values.
- Replaced the package byte-scan machine marker with a runtime-composed marker.
- Calibrated the verifier by file representation: source files require source-encoded UNC separators, while plain structured/text artifacts use their direct representation.
- Added subprocess tests proving foreign-cwd success for valid relative content and fail-closed detection of drive-rooted, UNC, file-URI, and rooted POSIX inputs.
- Preserved all original traversal and separator-normalization assertions.

## Validation

- Repository verifier passed twice, including from a foreign cwd, over 165 text files.
- Architecture verifier tests passed: five total, including positive and four-class negative path-policy behavior.
- Full repository gate passed: 475 tests, 0 failed, 0 skipped.
- No benchmark was run; the verifier is CI tooling and not used in the product runtime.

## Review outcomes

- Correctness: production path rejection was not weakened; only test-source representation changed.
- False-positive control: relative backslash normalization, separator character literals, and generic scheme-policy prose remain allowed.
- False-negative control: four forbidden classes are generated in a temporary fixture and must all be reported.
- Temp safety: fixture paths are unique beneath `artifacts/test-tmp`, and cleanup validates containment before recursive deletion.
- Manual primary-agent review completed; external GitHub/Codex review was not invoked.

## Follow-ups

- Keep negative validation behavior, but never weaken the production path parser to satisfy the source audit.
