---
id: FT-20260906-003
kind: feature
status: reviewed
area: windows-workbench-shell
stage: phase-1
tasks:
  - navigation-shell
  - project-tab-workspace
  - guided-stage-skeleton
  - fake-gateway-state-binding
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.App/
  - src/FluoAxis.Presentation/
  - tests/FluoAxis.GatewayContract.Tests/
  - FluoAxis.slnx
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - IFluoAxisGateway-current-capability-baseline
  - RunSnapshotDto-allowed-actions
  - presentation-ui-state
  - research-use-only-product-boundary
hotspots:
  - fake-real-capability-honesty
  - viewmodel-state-ownership
  - ui-thread-responsiveness
  - visualization-lifecycle
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - Presentation ViewModel initial loading ready error cancellation and stale-response behavior
    - allowedActions drive guided-stage availability without frontend state derivation
    - WinUI Debug build and XAML compile
    - focused Presentation tests and full repository gate
    - path and scientific-source-boundary gates
  tests_run:
    - solution restore completed for all projects using the existing local package cache
    - solution Debug build completed with 0 errors and 158 analyzer warnings
    - repository test suite passed 602 of 602 before the path-gate regression was added
    - GatewayContract focused suite passed 20 of 20 including five Workbench ViewModel tests
    - Architecture focused suite passed 15 of 15 after the path-gate regression was added
    - repository and foreign-cwd path-policy gates passed over 251 text files
    - scientific source-boundary gate passed over 11 first-party presentation files
    - PlotHost Node suite passed 7 of 7
  benchmarks_run:
    - no new timing benchmark; benchmark launch compatibility was restored and prior Release evidence remains unchanged
search:
  aliases:
    - Phase 1 workbench shell
    - NavigationView TabView three column
    - Fake Gateway guided skeleton
---

# Build the Phase 1 FluoAxis workbench shell

## Summary

Replace the visualization-only technical-spike window with a capability-honest FluoAxis research workbench shell: primary navigation, project tab, guided-stage rail, three-column workspace, global operation/error surfaces, RUO identity, and an embedded Explorer backed by the existing PlotHost.

## Plan

### Contract

- Consume `IFluoAxisGateway`, `RunSnapshotDto`, and backend-derived `allowedActions`; do not reproduce Run transition or scientific policy in Presentation.
- Keep unsupported import-to-analysis mutations visibly unavailable rather than inventing Fake success.

### Runtime

- Move project/Run loading and UI state into a testable Presentation ViewModel. Use the BCL notification contract for this offline slice; the centrally pinned MVVM Toolkit is not locally restored and remains a later integration item.
- Keep visualization initialization lazy to the Explorer workspace and preserve cancellation/stale-response suppression.

### Data/path

- Show only supplied safe names and contract values. Do not introduce file pickers, external paths, persistence, or archive choices in this slice.

### Tests

- Cover ViewModel success, failure, cancellation, action availability, selection, and stale asynchronous completion.
- Build the real WinUI project and run the repository path and scientific-boundary gates.

### Docs/review

- Update the completion matrix without claiming the full Fake golden path or G1 completion.
- Perform concern-based contract/runtime/data-path/tests/docs review; delegated review is advisory and the primary agent owns implementation.

## Exploration log

- The current `MainWindow` is a full-window PlotHost spike with plot/export controls. It has no `NavigationView`, `TabView`, project/Run workspace, guided stages, global `InfoBar`, or Presentation ViewModel.
- `FakeFluoAxisGateway` truthfully supports create/get and read-only inspection only when configured; later workflow mutations remain explicit `SYSTEM_CAPABILITY_UNAVAILABLE`.
- The existing PlotHost is useful evidence and should become the Explorer workspace rather than remain the product's only screen.
- Phase 1 requires Shell, navigation, tabs, three-column layout, global progress/error patterns, Fake-backed page skeletons, and UI state foundations. This slice addresses that surface without closing the missing backend vertical workflow.
- Scientific questions Q-SCI-001 through Q-SCI-010 and Q-SCI-012 remain unresolved. The `NEW -> IMPORTED` command owner also remains unconfirmed, so this slice will not mutate Run state.
- A locked restore proved `CommunityToolkit.Mvvm` is listed centrally but absent from the local package cache. Network access is restricted, so this slice uses `INotifyPropertyChanged` without downloading a new dependency; this is recorded as an explicit follow-up rather than concealed.

## Root cause

Not applicable. This is an intentional Phase 1 product-surface capability.

## Implementation

- Added a Presentation-owned `WorkbenchViewModel` with explicit Initial, Loading, Ready, Empty, recoverable-error, and fatal-error states; project/Run loading, cancellation, correlation data, and stale-response suppression are covered without reproducing Domain transition rules.
- Replaced the PlotHost-only window with a `NavigationView`, non-closable project `TabView`, Guided stage rail, central stage surface, Run context panel, global `InfoBar`, progress state, and persistent RUO identity.
- Bound the Shell to the existing capability-honest Fake Gateway. Stage inspection remains navigable, while unsupported mutations stay disabled even when the backend advertises an upstream action that lacks picker/orchestration support.
- Retained the existing PlotHost as a lazy Explorer surface and preserved 2D/3D/export controls. Benchmark launch modes now select Explorer and initialize PlotHost automatically, preventing the Shell default view from suppressing benchmark execution.
- Kept the slice path-free: it adds no picker, external source path, persistence field, archive entry, report, or scientific calculation.
- Used BCL `INotifyPropertyChanged` rather than adding a new package download. The current code-behind still owns navigation and PlotHost lifecycle and is explicitly a transitional Shell, not a completed MVVM production surface.

## Validation

- Existing local package cache: solution restore completed for every project with failed network sources ignored and NuGet audit disabled for the offline validation environment.
- Debug solution build: 0 errors; 158 existing analyzer warnings. XAML compilation succeeded.
- Full solution test before the final path-gate regression: 602 passed, 0 failed, 0 skipped.
- GatewayContract focused suite: 20 passed, including five new Workbench ViewModel behaviors.
- Architecture focused suite after the path-gate regression: 15 passed.
- Repository path policy: 251 files passed from both repository and foreign working directories.
- Scientific source boundary: 11 first-party presentation files passed.
- PlotHost Node suite: 7 passed, including fixed camera/screen-relative axes and serialized command handling.
- No new performance number is claimed. Prior 25k Release evidence remains 287/422/270 ms for 2D and 1272/1007/1043 ms for 3D; 3D remains above the provisional 800 ms target.
- A live program-window observation is still required for visual layout, keyboard, DPI, accessibility, and actual WebView interaction evidence. No screenshot was taken.

## Review outcomes

- Contract: no Method, Data, import/mapping transition, persistence, report, or archive contract changed. `allowedActions` remains backend-derived.
- Runtime: PlotHost initialization is lazy for ordinary launches and eager only for explicit benchmark modes; workbench and render cancellation are disposed on window close.
- Capability honesty: stage navigation is a skeleton; no unavailable workflow reports success. Reports and diagnostics explicitly state their incomplete scope.
- Data/path: no path-bearing application DTO or persisted value was added; strengthened path gates remain green.
- Tests: ViewModel state, cancellation, stale completion, error/correlation, action availability, and navigation-only state are covered. WinUI automation and live interaction are not yet covered.
- Architecture: `MainWindow.xaml.cs` remains larger than the desired production ownership boundary. Splitting navigation, operation surfaces, and Plot control is a follow-up before the Shell is treated as production MVVM.
- Delegated concern reviews and primary-agent self-review are complete. GitHub review was not invoked.

## Follow-ups

- Implement the import/mapping vertical workflow only after the `NEW -> IMPORTED` command ownership is confirmed.
- Extend the shell from page skeletons to a complete Fake no-calculation golden path as each real gateway operation becomes contractually truthful.
- Add WinUI smoke/automation coverage for navigation, keyboard, High Contrast, DPI, and WebView interaction.
- Move navigation and PlotHost orchestration out of the growing Window code-behind before implementing the full Guided workflow.
