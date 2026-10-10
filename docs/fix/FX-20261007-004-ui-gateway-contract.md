---
id: FX-20261007-004
kind: fix
status: validated
area: windows-workbench-shell
stage: phase-1
tasks: [repair-presentation-command-contracts, guided-editor-wiring, keep-unwired-operations-explicit]
priority: P0
branch: main
pr: null
paired_doc: null
paths: [src/FluoAxis.Presentation/WorkbenchViewModel.cs, src/FluoAxis.App/MainWindow.xaml, src/FluoAxis.App/MainWindow.xaml.cs, src/FluoAxis.App/FluoAxis.App.csproj, tests/FluoAxis.GatewayContract.Tests/WorkbenchViewModelTests.cs]
contracts: [IFluoAxisGateway, CommandBase, InspectImportCommand, ConfirmMappingCommand, AssignWellsCommand]
hotspots: [run-revision, actor-and-issued-time, capability-unavailable, transient-picker-boundary]
bug:
  repro: "Build the current WinUI or GatewayContract test project; WorkbenchViewModel uses removed ManagedResourceIdentity and obsolete command constructors, and MainWindow declares OnPrimaryStageActionClick twice."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [GatewayContract focused tests, solution build, solution tests, live WinUI smoke]
  tests_run: ["dotnet build FluoAxis.slnx --no-restore: 0 warnings, 0 errors", "dotnet test FluoAxis.slnx --no-restore: 699 passed", "live WinUI smoke: workbench, mapping empty state, and picker displayed; full file-to-analysis UI path remains unverified"]
  benchmarks_run: ["ManagedFileWorkflowTests: 30 sequential synthetic runs, no SLA asserted"]
search:
  aliases: [ui gateway constructor mismatch, workbench command contract, duplicate primary stage handler]
---

# Repair the Workbench UI gateway contract

## Summary

The current Workbench presentation code no longer matched the checked-out versioned Gateway command contracts. It also contained a duplicate Window event handler. The repair now includes the executable guided editor slice while keeping unimplemented persistence/report capabilities visibly unavailable.

## Plan

1. Inspect the current Contracts and the presentation callers.
2. Use current Run revision, actor, and UTC issue time for every mutating command.
3. Remove duplicate event wiring and unsafe UI-side staging shortcuts.
4. Run focused tests and record any toolchain limitation.

## Exploration log

The existing WorkbenchViewModel references an undefined `ManagedResourceIdentity`; import, mapping, assignment, calibration, control, and analysis commands use obsolete argument lists. `MainWindow.xaml.cs` contains two `OnPrimaryStageActionClick` methods and constructs Infrastructure staging directly from an external path.

## Root cause

The UI slice was advanced after the Contracts v2 command revision/actor and managed-source identity changes, but the Window and ViewModel callers were not migrated together.

## Implementation

- `WorkbenchViewModel` now accepts the checked-out `SourceFileDto`, derives the existing media-type vocabulary from its extension, and constructs the current `InspectImportCommand`.
- Mapping, well assignment, calibration, control QC, and sample analysis commands now carry the current run revision, actor, issue time, and contract version through the current command constructors.
- Every mutating operation refreshes the run snapshot through `GetRunAsync`; stale revisions refresh the visible snapshot and retain the structured error; cancellation restores the last ready state; a busy gate rejects duplicate operations.
- The ViewModel exposes immutable preview/source/result properties for the shell and never constructs domain roles or formulas.
- The WinUI shell was expanded to consume the injected `DesktopWorkspace`, show independent FAM/ROX candidate editors, explicit well role/sample/replicate inputs, stage summaries/issues, per-well sample outputs and trace IDs, and workspace-mediated plot saving. The shell remains responsible for rendering only.

## Validation

- Presentation project built successfully before the final UI rewrite.
- The existing GatewayContract suite passed 22/22 before the new workflow fixtures were added.
- Final App validation passed through the solution build with the new Infrastructure/Import references and WinUI resource dictionary.
- Live smoke confirmed the three-column shell, stage rail, mapping empty state and native file picker. Full selection of the synthetic fixture and completion of every stage was not automated in this environment.

## Review outcomes

- Contract review: command construction follows the current Contracts source of truth; no UI-side scientific formula or role inference was added.
- Path review: the shell no longer writes plot bytes directly; picker/staging and export use the Infrastructure workspace boundary.
- Accessibility review: stage controls, candidate selectors, well editors, source fields, and actions have AutomationProperties names; live UIA/DPI verification remains pending.

## Follow-ups

Add persistent SQLite workspace injection, full trace viewer, Report Snapshot/export, UIA/DPI/High Contrast automation, and a true WinUI end-to-end fixture run.
