---
id: FT-20260903-010
kind: feature
status: reviewed
area: import-orchestration
stage: phase-1
tasks:
  - application-import-inspection-port
  - infrastructure-managed-resource-adapter
  - fake-real-inspection-conformance
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Application/
  - src/FluoAxis.Contracts/GatewayResult.cs
  - src/FluoAxis.Domain/ErrorCodes.cs
  - src/FluoAxis.Infrastructure/
  - src/FluoAxis.Presentation/FakeFluoAxisGateway.cs
  - tests/FluoAxis.Application.Tests/
  - tests/FluoAxis.Infrastructure.Tests/
  - tests/FluoAxis.GatewayContract.Tests/
  - tests/FluoAxis.Architecture.Tests/
  - samples/contracts/
  - FluoAxis.slnx
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - managed-import-source-port-v1
  - import-preview-v2
  - IFluoAxisGateway-current-capability-v2
hotspots:
  - transient-external-picker-path
  - relative-resource-resolution
  - cwd-independence
  - importer-error-normalization
  - cancellation-and-stream-lifetime
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - Application version state cancellation and port delegation tests
    - Infrastructure traversal cwd independence metadata and importer error tests
    - shared Fake and Application successful inspection conformance
    - architecture dependency and relative-path policy gates
    - full solution build and repository MTP gate
  tests_run:
    - locked solution restore passed
    - solution build passed with 128 analyzer warnings and 0 errors
    - Application focused tests 27 passed
    - Infrastructure focused tests 6 passed
    - Gateway conformance focused tests 15 passed
    - repository MTP gate 514 passed
    - repository path-policy gate passed for 191 text files
    - git diff check passed
  benchmarks_run:
    - Not applicable; this slice adds bounded orchestration and no performance optimization claim
search:
  aliases:
    - managed import inspection
    - import source port
    - infrastructure stream adapter
    - inspect import gateway
---

# Inspect managed import resources through a path-safe port

## Summary

Implement the first truthful import-inspection vertical slice. Application will orchestrate inspection through a port carrying only validated project-relative resource identity; Infrastructure will resolve that identity under an injected managed root for one bounded call and delegate the stream to existing Import adapters. No OS-picker path crosses Contracts or Application, and inspection will not yet mutate the Run or persist official data.

## Plan

### Contract

- Keep Contracts V2 unchanged and use the existing bare display name, declared size, format hint, and managed relative resource path.
- Return existing candidate-region DTOs and normalized structured errors.

### Runtime

- Add an Application-owned inspection port and enforce version, Run existence, Run state, cancellation, and safe progress ordering before delegation.
- Add an Infrastructure adapter that opens one managed resource stream, detects format without trusting the hint alone, invokes the bounded importer, and disposes the stream.

### Data/path

- Resolve only beneath an injected managed workspace root, reject traversal and link/reparse escape, and remain independent of process cwd.
- Treat the file-picker absolute path as outside this slice: a future picker adapter must copy the selected file into the managed root before constructing the command.

### Tests

- Exercise success, missing file, size mismatch, format mismatch, importer failure, cancellation, cwd independence, and stream disposal.
- Expand Fake/real conformance only for inspection; do not enable mapping confirmation or import persistence.

### Docs

- Update the completion audit conservatively and record remaining staging, persistence, and real-file qualification gaps.

### Review

- Perform manual `contract/runtime/data-path/tests/docs/review` decomposition as requested; no subagents are started.

## Exploration log

- Contracts V2 can now carry candidate regions and confirmed FAM/ROX mappings, but `ApplicationGateway.InspectImportAsync` still always returns `SYSTEM_CAPABILITY_UNAVAILABLE`.
- Existing `IImporter` already accepts a stream, applies bounded parsing, and returns structured Domain errors; Application must not depend on a concrete importer or filesystem path.
- The current architecture graph has no Infrastructure project. The intended dependency inversion is Application-owned port, Infrastructure implementation depending on Application and Import.
- `InspectImportCommand.RelativeResourcePath` is already validated, but containment must be revalidated at the filesystem boundary after resolution.
- Inspection is read-only and cannot transition `New -> Imported`; that transition belongs to a later transactional managed-copy/read/persistence slice.

## Root cause

Not applicable. This is a planned vertical capability.

## Implementation

### Contract

- Kept Contracts V2 unchanged. `InspectImportCommand` already admits only a bare display name, declared byte size, normalized project-relative resource identity, and optional format hint.
- Added three stable import-boundary errors: managed resource missing, declared-size mismatch, and unsafe managed resource. All messages and technical references are path-free, and Application maps every import failure to a non-empty actionable recommendation.

### Runtime

- Added Application-owned `IImportInspectionPort` and `ManagedImportRequest`. Application validates contract version, Run existence, and `New` state before delegating.
- Successful inspection returns the existing `ImportPreviewDto` and deliberately does not mutate Run state, revision, source-file facts, or audit records.
- Added optional deterministic inspection providers to both Application and Fake gateways. Their default remains an explicit capability-unavailable response, so unconfigured adapters do not overstate readiness.

### Data/path

- Added `FluoAxis.Infrastructure` with `ManagedImportInspectionAdapter`. It resolves a normalized relative identity under an injected fully qualified managed root only inside the adapter call.
- The adapter rechecks containment, rejects reparse points, validates the declared size before and after opening, enforces the configured byte limit, and keeps the file open without delete sharing while inspection runs.
- Importer selection cross-checks display-name extension, managed-resource extension, optional hint, ZIP signature for XLSX, and a bounded strict UTF-8/UTF-16 text-content probe. Unknown, malformed, binary, or conflicting evidence fails closed.
- The stream is opened read-only with asynchronous sequential access and disposed after the importer completes or fails. No resolved runtime path enters a DTO, result, diagnostic, or log.

### Tests and project graph

- Added six Infrastructure boundary tests covering CSV and real Open XML inspection, candidate conversion, progress ordering, missing/changed/oversized/conflicting resources, binary masquerading as CSV, cwd independence, cancellation, stream disposal, and path-free errors.
- Added three Application tests covering validation order, port delegation, error/cancellation propagation, and proof that successful inspection leaves the Run unchanged.
- Added a two-adapter Fake/Application success-conformance scenario and extended the V2 capability fixture.
- Registered production and test projects in `FluoAxis.slnx`, updated the exact architecture dependency graph, generated locked dependency files, and raised the fail-closed test floor to 514.

## Validation

- `dotnet restore FluoAxis.slnx --locked-mode`: passed.
- `dotnet build FluoAxis.slnx --no-restore`: passed with 128 analyzer warnings and 0 errors. The warnings are existing/test naming and analyzer findings; no zero-warning claim is made.
- Focused Application tests: 27 passed, 0 failed, 0 skipped.
- Focused Infrastructure tests: 6 passed, 0 failed, 0 skipped.
- Focused Gateway conformance tests: 15 passed, 0 failed, 0 skipped.
- Repository Microsoft.Testing.Platform gate with `--minimum-expected-tests 514`: 514 passed, 0 failed, 0 skipped.
- Repository path-policy gate: passed for 191 text files.
- `git diff --check`: passed; Git emitted only the pre-existing `.gitignore` LF-to-CRLF advisory.
- Benchmark: not applicable; no throughput improvement is claimed. Import reads remain bounded by `ImportLimits`, and signature probing reads at most 4096 bytes before the existing importer runs.

## Review outcomes

- Contract: no external picker path, stream, or handle crosses Contracts/Application. Contracts V2 wire shape is unchanged.
- Runtime: inspection is read-only and cannot be confused with official import; no Run transition, persistence, source hash, or audit claim is made.
- Data/path: containment, relative identity, cwd independence, reparse rejection, size recheck, and path-free failures were reviewed. The adapter retains an absolute path only as a local runtime variable required by `FileStream`.
- Tests: focused and full repository gates pass. Synthetic CSV/Open XML fixtures prove engineering behavior only, not compatibility with the required five approved instrument exports.
- Docs: README, CI floor, capability fixture, completion audit, and change registry now match the checked-out implementation.
- Security/memory: probes are bounded; the existing importers enforce file/row/cell/column limits; no raw experiment content is logged or retained by the gateway.
- Compatibility: the new port is optional, so existing gateway construction and capability-unavailable behavior remain valid. No archive or persisted schema changes were introduced.
- Manual primary-agent decomposition was used as explicitly requested. GitHub and external Codex review were not required or invoked.
- Self-review found no unresolved correctness issue in this slice.

## Follow-ups

- Define the OS-picker-to-managed-staging copy adapter and cleanup lifecycle.
- Persist source hash, mapping snapshot, signals, revision, state transition, and audit atomically after inspection and confirmation.
- Qualify discovery against at least five approved de-identified FAM/ROX files.
- Add an explicit Windows reparse-point integration fixture when the clean CI runner capability is established; current code rejects any discovered reparse point, while traversal and cwd-independence are directly covered.
