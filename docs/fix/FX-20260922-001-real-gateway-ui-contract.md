---
id: FX-20260922-001
kind: fix
status: validated
area: windows-workbench-shell
stage: phase-1
tasks:
  - replace-fake-composition-root-gateway
  - bind-guided-stage-availability-to-backend-actions
  - preserve-explicit-unavailable-import-operation
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.App/App.xaml.cs
  - src/FluoAxis.App/FluoAxis.App.csproj
  - src/FluoAxis.App/packages.lock.json
  - src/FluoAxis.App/MainWindow.xaml.cs
  - src/FluoAxis.App/MainWindow.xaml
  - tests/FluoAxis.GatewayContract.Tests/WorkbenchViewModelTests.cs
  - tests/FluoAxis.Architecture.Tests/ProjectDependencyTests.cs
  - README.md
  - docs/feature/FT-20260906-003-phase1-workbench-shell.md
  - docs/feature/FT-20260913-001-requirements-implementation-audit.md
  - docs/fix/FX-20260922-001-real-gateway-ui-contract.md
  - docs/change_index.jsonl
contracts:
  - IFluoAxisGateway
  - RunSnapshotDto.allowedActions
  - guided-stage-availability
  - research-use-only-ui-boundary
hotspots:
  - fake-real-gateway-drift
  - frontend-action-overclaim
  - composition-root-lifetime
bug:
  repro: "Launch the WinUI shell and inspect the workbench stage rail; the composition root constructs FakeFluoAxisGateway and the window enables every stage button after load."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - prove the ViewModel exposes only backend-allowed stage actions
    - prove unavailable mutation actions remain disabled in the shell state
    - run focused GatewayContract tests
    - run repository path and scientific-source policy gates
  tests_run:
    - node --test web/FluoAxis.PlotHost/plot-host.test.js (7 passed)
    - build/Verify-RepositoryPolicy.ps1 (270 text files passed)
    - build/Verify-ScientificSourceBoundary.ps1 (11 first-party presentation files passed)
    - JSONL and lock-file parse checks passed
    - git diff --check passed
  benchmarks_run:
    - none; no runtime behavior benchmark was added
search:
  aliases:
    - real application gateway in winui
    - guided stage allowed actions
    - fake gateway composition root
---

# Make the WinUI workbench honor the real Gateway action contract

## Summary

The WinUI shell currently constructs the development Fake Gateway and then enables every Guided stage after initialization. That makes the visible UI diverge from the Application-owned Run snapshot and can suggest operations that the backend cannot execute. This fix makes the composition root use the real Application Gateway and makes stage controls reflect `allowedActions`, while keeping unsupported import mutation visibly unavailable until the picker and staging boundary are wired.

## Plan

1. Inspect the current composition root, ViewModel contract, stage UI, and focused tests.
2. Replace the Fake Gateway at the production composition root with an Application Gateway backed by the existing in-process repository.
3. Expose backend-derived availability and explicit operation-unavailable text to the Window without reimplementing state transitions in UI code.
4. Add focused regression coverage and run the available validation gates.

## Exploration log

- The current checkout contains a complete `ApplicationGateway` workflow implementation and a separate `FakeFluoAxisGateway` capability baseline.
- `App.xaml.cs` constructs only `FakeFluoAxisGateway`, so the WinUI path does not exercise the implemented Application workflow.
- `MainWindow.xaml.cs` sets every Guided stage button to enabled after loading, even when `RunSnapshotDto.AllowedActions` contains only `import` and `confirmMapping`.
- The import command is still read-only inspection and requires a managed-resource adapter; the shell has no file picker or staging operation yet.

## Root cause

The Phase 1 shell was intentionally built against a Fake Gateway, but the composition root and stage button update logic were not advanced when the Application workflow became available. The Window also had no presentation property for the distinction between a backend-allowed action and a UI operation whose required picker/staging boundary is still absent.

## Implementation

- Added the `FluoAxis.Application` project reference to the WinUI composition root and replaced the production launcher's `FakeFluoAxisGateway` with `ApplicationGateway` over the existing in-process repository. This exercises the real Application contract without introducing a machine-specific persistence path.
- Kept the Guided rail navigable after a Run is loaded, as required by the Phase 1 shell contract, and made the selected stage's operation surface derive from `RunSnapshotDto.AllowedActions` plus the currently connected Infrastructure capability. The Window no longer presents a clickable operation for a missing picker/staging boundary.
- Added stage-specific state text so a locked stage explains the backend prerequisite, while an allowed but not yet executable operation explains the missing picker/staging or orchestration boundary.
- Updated the approved project dependency graph, App lock file, README capability statement, and focused ViewModel assertions.

## Validation

- `node --test web/FluoAxis.PlotHost/plot-host.test.js`: 7 passed, 0 failed.
- `build/Verify-RepositoryPolicy.ps1`: passed for 270 text files.
- `build/Verify-ScientificSourceBoundary.ps1`: passed for 11 first-party presentation files.
- `docs/change_index.jsonl` and `src/FluoAxis.App/packages.lock.json`: parsed successfully as their respective JSON formats.
- `git diff --check`: passed.
- XAML/code-behind name and App project-reference structural checks passed.
- `dotnet build src/FluoAxis.App/FluoAxis.App.csproj --no-restore --property:Platform=x64`: passed, 0 warnings, 0 errors.
- `dotnet test tests/FluoAxis.GatewayContract.Tests/FluoAxis.GatewayContract.Tests.csproj --no-restore`: 22 passed, 0 failed.
- `dotnet test tests/FluoAxis.Architecture.Tests/FluoAxis.Architecture.Tests.csproj --no-restore`: 15 passed, 0 failed.
- `dotnet build FluoAxis.slnx --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test FluoAxis.slnx --no-restore`: 621 passed, 0 failed, 0 skipped.

## Review outcomes

- Contract review: stage availability is now backend-derived; no scientific formula or Run transition logic was added to Presentation or the Window.
- UI review: the shell keeps the RUO label, global error surface, and explicit disabled state for missing Infrastructure boundaries. Stage copy now follows the selected stage instead of repeating import text.
- Architecture review: the App may reference Application through the approved composition-root edge; Presentation remains Contracts-only. No absolute path entered the App or contract.
- Validation limitation: the pinned .NET build/test and XAML compile now pass. A live WinUI interaction/screenshot smoke remains pending because this turn has no controlled desktop automation evidence. GitHub/Codex review was not invoked.

## Follow-ups

- Add the transient picker to managed staging and connect read-only import inspection to a visible preview surface.
- Move stage command orchestration out of the Window code-behind as the Guided workflow grows.
- Add Windows UI automation and live accessibility/DPI evidence.
- Replace the in-process repository in the desktop composition root with a path-policy-backed SQLite workspace once the persistence owner is wired.
