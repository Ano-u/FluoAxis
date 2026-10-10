---
id: FT-20261007-001
kind: feature
status: validated
area: guided-ui-import
stage: phase-1
tasks:
  - add-file-picker-boundary
  - call-managed-staging
  - call-inspect-import
  - display-preview-candidates
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Infrastructure/DesktopWorkspace.Windows.cs
  - src/FluoAxis.App/MainWindow.xaml
  - src/FluoAxis.App/MainWindow.xaml.cs
  - src/FluoAxis.Presentation/WorkbenchViewModel.cs
  - tests/FluoAxis.GatewayContract.Tests/WorkbenchViewModelTests.cs
contracts:
  - transient-picker-boundary
  - managed-staging-adapter
  - inspect-import-gateway
hotspots:
  - external-path-lifetime
  - path-redaction
  - async-cancellation
  - ui-thread-dispatch
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - WinUI file picker returns transient path
    - staging adapter creates managed resource
    - inspect returns preview candidates
    - ViewModel state transitions and error handling
    - UI dispatch and cancellation
  tests_run:
    - "FT-20261007-003 integrated validation: dotnet test FluoAxis.slnx --no-restore; 699 passed"
    - "FT-20261007-003 integrated validation: solution and App builds; 0 warnings, 0 errors"
    - "FX-20261007-004 live smoke: workbench, mapping empty state, and native picker displayed; complete file-to-analysis UI path unverified"
    - "FT-20261007-003 integrated validation: repository policy, scientific source boundary and git diff checks passed"
  benchmarks_run:
    - "FT-20261007-003 synthetic file workflow benchmark covers backend import-to-analysis; it is not a picker/UI performance measurement"
search:
  aliases:
    - import file picker
    - managed staging ui
    - inspect import preview
    - guided import workflow
---

# Add Import file selection and preview UI

## Summary

The file-selection and preview slice is implemented through the Infrastructure picker/staging boundary and the real Gateway. Its final command-contract wiring, candidate editors, and focused validation were completed by `docs/fix/FX-20261007-004-ui-gateway-contract.md` and integrated into `docs/feature/FT-20261007-003-software-design-completion-audit.md`. This `validated` status covers the engineering slice; a complete native GUI journey and scientific release acceptance remain open.

## Plan

### Contract

- Keep `StorageFile` and its transient external path inside the Infrastructure boundary, then reuse the existing staging adapter
- Call `ManagedImportStagingAdapter.StageAsync` to create a managed relative resource
- Call `IFluoAxisGateway.InspectImportAsync` with the managed resource identity
- Display returned `ImportPreviewDto` candidates in the Import stage surface

### Runtime

- File picker launch and stream opening are async; use existing cancellation infrastructure
- Display staging progress with `WorkbenchProgress`
- Dispatch preview candidate display to UI thread
- Handle picker cancellation, staging failures, and inspection errors gracefully

### Data/path

- Never persist the `StorageFile.Path` absolute location
- Permit the existing staging adapter to receive the transient picker path only within Infrastructure; it returns bare display name, content hash, size, and a validated managed-relative identity
- Managed resource identity from staging is the only persisted reference
- Preview candidates contain only safe display data and candidate indices

### Tests

- ViewModel: staging success, cancellation, errors, stale correlation
- GatewayContract: inspect returns expected preview shape
- Manual: file picker dialog appears, staging completes, candidates display

### Docs

- Update README implementation matrix with Import UI completion
- Record the implemented import slice and link the later mapping/plate/analysis work rather than leaving it as an unimplemented promise

## Exploration log

- Current `MainWindow` Import stage shows placeholder text; `PrimaryStageAction` button ("Inspect source") is wired but has no implementation
- `FakeFluoAxisGateway` supports `InspectImportAsync` when configured with fixtures
- Real `ApplicationGateway` integration already exists in the Shell composition root
- `ManagedImportStagingAdapter` and `ManagedSignalPreparationAdapter` are implemented and tested in Infrastructure
- WinUI `StorageFile` from `FileOpenPicker` provides the transient external boundary
- These observations describe the initial stub. The subsequent review found obsolete ViewModel command signatures and a duplicate primary action event handler; FX-20261007-004 records that failed implementation path and its repair.
- The first plan proposed a stream-only staging call. The checked-out repository-native staging abstraction already accepts a transient external path within Infrastructure, so the final implementation reused it instead of adding a parallel adapter. No picker path is passed to Presentation or Contracts.

## Root cause

Not applicable. This is an intentional Phase 1 guided-workflow capability.

## Implementation

- `DesktopWorkspace` owns native file selection and sends the transient selection to managed staging. The UI receives `SourceFileDto` with a bare display name, size, SHA-256, and project-relative resource identity.
- `WorkbenchViewModel.InspectImportAsync` builds the current `InspectImportCommand`, exposes candidate/source DTOs, and handles busy, cancellation, failure, and stale-response state.
- `MainWindow` renders source metadata and FAM/ROX candidates, then requires explicit mapping confirmation. Mapping, plate assignment, and analysis controls were subsequently implemented in FX-20261007-004/FT-20261007-003, so they are no longer follow-up stubs here.
- Later persistent store, saved-project selection, and evidence viewer additions belong to FT-20261007-005; the validation recorded below is the earlier integrated slice.

## Validation

The prior integrated checkout passed `dotnet test FluoAxis.slnx --no-restore` with 699 tests and solution/App builds with 0 warnings / 0 errors, as recorded by FT-20261007-003. ViewModel contract tests cover managed source identity, current revision commands, busy suppression, errors, and cancellation. Live WinUI smoke in FX-20261007-004 displayed the workbench, mapping empty state, and native picker. Full file selection, candidate confirmation, every analysis stage, and final result review were not automated in that GUI smoke; do not treat them as fully exercised native UI acceptance.

## Review outcomes

Static review confirmed that external picker paths stay inside Infrastructure, UI consumes returned DTOs, no scientific formulas were added to the view, and command availability is driven by backend actions. The obsolete constructors and duplicate event were resolved in FX-20261007-004. External GitHub/Codex review was not triggered. Accessibility, DPI, and complete cancellation interaction still need native UI verification.

## Follow-ups

- Run the entire native GUI journey against a representative de-identified instrument fixture, including canceled selection/inspection, mapping correction, all required controls, and INVALID output.
- Complete UI Automation, High Contrast and DPI validation; measure picker/preview responsiveness separately from backend benchmarks.
- Track persistent project reopen and evidence viewing under FT-20261007-005. Scientific Method approval, Golden dual approval, and report export remain separate gates.
