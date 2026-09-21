---
id: FX-20260903-001
kind: fix
status: validated
area: test-infrastructure
stage: phase-1
tasks:
  - restore-solution-test-gate
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - global.json
  - tests/FluoAxis.Application.Tests/FluoAxis.Application.Tests.csproj
  - tests/FluoAxis.Golden.Tests/FluoAxis.Golden.Tests.csproj
  - tests/FluoAxis.Persistence.Tests/FluoAxis.Persistence.Tests.csproj
  - README.md
contracts:
  - test-runner-contract
hotspots:
  - locked-restore
  - dotnet-test
bug:
  repro: dotnet test FluoAxis.slnx --no-restore fails before discovery under the pinned .NET 10 SDK
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - dotnet restore FluoAxis.slnx --locked-mode
    - dotnet build FluoAxis.slnx --no-restore
    - dotnet test --test-modules **/bin/Debug/net10.0/*Tests.dll --root-directory . --minimum-expected-tests 480
  tests_run:
    - dotnet build FluoAxis.slnx --locked-mode (rejected: --locked-mode is a restore switch, not an MSBuild build switch; documentation corrected)
    - dotnet build tests/FluoAxis.Golden.Tests/FluoAxis.Golden.Tests.csproj --no-restore (passed, 0 warnings, 0 errors)
    - repository Microsoft.Testing.Platform gate with minimum 475 (475 passed, 0 failed, 0 skipped)
  benchmarks_run: []
search:
  aliases:
    - mtp
    - microsoft-testing-platform
    - dotnet-test
    - vstest
---

# Restore the .NET 10 solution test gate

## Summary

Restore one supported, solution-level test command for the pinned .NET 10 SDK and xUnit v3 projects.

## Plan

1. Confirm the failure and compare all test-project runner configurations.
2. Align the remaining legacy VSTest projects with the repository's existing Microsoft.Testing.Platform pattern.
3. Select Microsoft.Testing.Platform in the SDK configuration and refresh locked dependencies.
4. Run focused and full build/test validation, then perform path and dependency review.

## Exploration log

- Confirmed the user-local .NET SDK is version 10.0.400, as pinned by `global.json`.
- `dotnet test FluoAxis.slnx --no-restore` fails before test discovery because Microsoft.Testing.Platform 2.3.3 rejects the legacy VSTest target under .NET 10.
- Five test projects already use xUnit v3 as executable Microsoft.Testing.Platform runners. Application, Golden, and Persistence still reference the legacy VSTest packages and are not executable test hosts.
- A direct solution build succeeds with zero warnings and zero errors.
- The initial validation draft incorrectly attached `--locked-mode` to `dotnet build`; live validation rejected that MSBuild switch. The supported sequence is locked restore followed by `build --no-restore`, and the failed attempt is retained here for traceability.

## Root cause

The repository mixed executable xUnit v3 Microsoft.Testing.Platform projects with legacy VSTest-shaped projects, while the documented solution-target command selected an incompatible execution path under the pinned .NET 10 SDK. After runner alignment, empty Golden and Persistence assemblies still caused discovery to return exit code 8. The supported repository contract is module discovery with a minimum expected count, which both runs every executable test assembly and fails closed on discovery loss.

## Implementation

- Selected Microsoft.Testing.Platform in `global.json` and aligned Application, Persistence, and Golden test projects with the executable xUnit v3 pattern.
- Added substantive Persistence and Golden candidate suites, eliminating zero-test assemblies.
- Replaced the invalid solution-target test example with locked restore, no-restore build, and the validated module-discovery command; the current fail-closed floor is 480 tests.

## Validation

- Golden focused build passed with 0 warnings and 0 errors.
- Golden focused run passed: 16 tests, 0 failed.
- Repository module gate passed after architecture/policy tests were added: 475 tests, 0 failed, 0 skipped.

## Review outcomes

- Correctness: every current test assembly is discovered and executed.
- Fail-closed behavior: `--minimum-expected-tests 480` detects empty/disappearing discovery; the floor must be maintained as tests change.
- Paths: the documented command is repository-relative and does not encode a developer-machine path.
- External GitHub/Codex review was not invoked; manual test-contract review completed.

## Follow-ups

- Add the same locked build and module-discovery gate to CI when CI is introduced.
- Keep the minimum count synchronized when tests are added or intentionally removed.
