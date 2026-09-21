---
id: FT-20260903-002
kind: feature
status: reviewed
area: governance-and-delivery
stage: cross-phase
tasks:
  - audit-plan-completion
  - define-execution-waves
  - record-decision-gates
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - docs/feature/FT-20260903-002-plan-completion-audit.md
contracts:
  - plan-stage-gates
  - evidence-based-completion
hotspots:
  - scientific-approval
  - delivery-sequencing
  - gate-claims
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - verify every G0-G8 criterion against current repository evidence
    - verify phase plan respects plan.md dependencies and unresolved decisions
    - run relative-path and markdown consistency audits
  tests_run:
    - current source/test/document inventory reviewed against plan.md phases 0-8
    - all 44 G0-G8 exit criteria classified below
    - dotnet restore FluoAxis.slnx --locked-mode (passed)
    - dotnet build FluoAxis.slnx --no-restore (passed, 128 analyzer warnings, 0 errors)
    - repository solution test gate after Workbench Shell integration (602 passed, 0 failed, 0 skipped)
    - focused relative-path audit (passed)
  benchmarks_run: []
search:
  aliases:
    - plan completion audit
    - G0 G8 gate matrix
    - phased execution plan
---

# Audit implementation completion against plan.md

## Summary

Produce an evidence-backed, criterion-level audit of the current checkout against `plan.md`, then define the next execution waves without treating implemented components as completed phase gates.

## Plan

1. Inventory current source, tests, documentation, project boundaries, and runnable validation.
2. Evaluate every G0-G8 exit criterion as proven, partial, missing, or decision-blocked.
3. Separate scientific/product decisions requiring owner confirmation from engineering work that can continue safely.
4. Define dependency-ordered execution waves with explicit entry evidence and exit evidence.
5. Validate the audit against the current checkout and update this document to reviewed status.

## Exploration log

- Current implemented layers are Domain, Analysis, Contracts, Import, ProjectFormat, PlotProtocol, and the first Application `CreateProject/GetRun` slice.
- Persistence has a validated baseline and Golden has 16 unapproved candidate tests. Presentation contains a capability-honest Fake Gateway, Infrastructure contains managed inspection/staging adapters, and PlotProtocol contains renderer-neutral specifications plus bounded bridge parsing. A self-contained WinUI technical-spike App, Visualization adapter, and local PlotHost now exist; the guided production App workflow, Reporting, installer, and release packaging remain absent.
- The repository solution test gate now passes 602 tests, with one additional path-gate regression validated in the focused Architecture suite. This closes local test execution infrastructure only, not Golden scientific approval, Raw point lineage, complete visualization qualification, or any phase gate.
- No G0-G8 gate can currently be claimed complete; implemented components are evidence toward individual criteria only.
- The user explicitly requested primary-agent-only continuation, so the required concern decomposition is performed manually rather than through subagents.
- One transient Windows access-denied result in the WAL movement proof is now guarded by a narrowly coded, cancellation-aware, 500 ms maximum test-only retry; 20 consecutive focused process runs passed. This improves test reliability but does not strengthen the production packaging completion claim.

## Root cause

Not applicable. This is a delivery-governance capability and completion audit.

## Implementation

### Status vocabulary

- **Proven**: direct implementation plus proportionate executable evidence exists.
- **Partial**: meaningful implementation exists, but full scope or approval evidence is absent.
- **Missing**: required implementation or evidence is absent.
- **Decision-blocked**: owner approval or representative research evidence is required.

### Gate audit

The plan defines **44 exit criteria** across G0-G8. No complete gate is currently proven. Component tests must not be promoted into a phase-gate claim.

| Gate | Exit criterion | Status | Evidence and gap |
|---|---|---|---|
| G0 | All blocking Q-SCI questions have written answers | Decision-blocked | Q-SCI-001 through 010 and 012 retain decisions or validation; only Q-SCI-011 is frozen. |
| G0 | Five representative dual-channel files reproduce | Decision-blocked | No approved real fixture corpus exists. Synthetic importer tests are not equivalent. |
| G0 | Golden covers positive, negative, failures, boundaries | Partial | 16 synthetic candidate tests compare production with an independent oracle across 15 core IDs; no real data or scientific/engineering dual approval exists. |
| G0 | WinUI/Plotly/WebView2/PDF/SQLite/Import spikes run | Partial | Import, ProjectFormat, file-backed SQLite WAL/checkpoint, and a self-contained WinUI/WebView2/local Plotly visualization spike run. Debug 25k 2D/3D measurements now exist, but PDF, controlled startup, software/RDP, network, combined-memory, and leak evidence remain absent; 3D misses its provisional render target. |
| G0 | Stable patches, licenses, runtime distribution recorded | Partial | Current NuGet pins, resolved licenses, sources, hashes, owners, upgrade and replacement policies are executable evidence; planned Windows App SDK, WebView2, Plotly, PDF, and runtime distribution choices remain unqualified. |
| G0 | No guessed technology choices remain | Missing | The shell and PlotHost now have partial runnable evidence, but PDF, buffers, virtualization, installer, runtime distribution, and GPU/software fallback remain unproved. |
| G1 | Fake frontend completes no-calculation golden path | Partial | A WinUI workbench Shell now provides NavigationView, a project Tab, a three-column Guided stage skeleton, global error/progress surfaces, RUO identity, and a Workbench ViewModel driven by Fake create/get plus backend `allowedActions`. Import-to-analysis mutations and the navigable no-calculation golden path remain unavailable. |
| G1 | Fake/Real Gateway conformance suite exists | Partial | One shared 15-test suite proves create/get, empty, failure, cancellation, stale/invalid state, replay/concurrency, unavailable behavior, and configured read-only inspection success; the remaining workflow operations are not implemented and therefore cannot yet conform on success. |
| G1 | Run state changes only through Application | Partial | State policy and project/Run persistence exist; import/mapping/analysis transitions do not. |
| G1 | Clean Windows CI locked restore/build/test | Partial | A pinned Windows workflow exists; local restore/build and 602 solution tests pass, the final path regression passes in the 15-test Architecture suite, and deterministic dependency evidence generation exists. No completed clean GitHub-hosted run is yet available. |
| G1 | Contracts V1 frozen with breaking-change policy | Partial | The current contract is V2 after an explicit breaking mapping-shape change; DTO guards, shape tests, and a V2 current-capability scenario manifest exist, but V1 was never approved/frozen and no V1 migration adapter exists. Full vertical fixtures and owner approval remain absent. |
| G2 | Approved Golden values pass field by field | Missing | No approved vectors or executable Golden tests. |
| G2 | Exact threshold and RR boundaries pass | Partial | Unit and independent-oracle candidate boundaries pass; approved Golden agreement remains absent. |
| G2 | No UI/JS formula copies | Partial | A CI-wired cross-language static gate now rejects Analysis/Method implementation symbols and recognized normalization, plateau-RR, and dr-prime formula copies in current first-party presentation surfaces. It excludes vendored Plotly and permits immutable result rendering and generic plot math. Production Guided UI does not yet exist, so full-surface proof remains unavailable. |
| G2 | Results trace to PointId, Method hash, algorithm version | Partial | Trace/hash and an Explorer-local exact PointId index exist; imported `SignalPoint` still has no stable PointId, so formal result lineage and persisted algorithm version are not end-to-end. |
| G2 | Analysis build/test passes | Proven | Analysis build passes and 63 tests pass. |
| G3 | Five real layouts and all fixtures are explainable | Decision-blocked | Adapters and 31 tests exist, but no approved five-file real corpus. |
| G3 | Mapping errors identify source and action | Partial | Import diagnostics, explicit candidate/confirmed source-coordinate DTOs, and a read-only managed-resource inspection path exist; confirmed-read orchestration, field-level recommended actions, and UI mapping behavior are absent. |
| G3 | Cancellation leaves no partial Run | Partial | Managed staging deterministically removes an in-progress partial copy on cancellation, managed inspection cancellation disposes its stream, and neither capability mutates a Run; transactional official import persistence does not exist. |
| G3 | Source hash/mapping snapshot survive reopen | Missing | No persistence integration or imported-state reopen. |
| G3 | Backend enforces plate rules | Partial | Domain cardinality exists; Application assignment persistence is unavailable. |
| G4 | One real file completes import-to-result | Missing | No real fixture, persistent orchestration, or UI path. |
| G4 | Critical failures block formal classification | Partial | Analysis behavior exists; invalidation and stale-result suppression are incomplete. |
| G4 | UI, Excel, database, trace values agree | Missing | The production Guided UI, Excel export, and end-to-end database integration are absent; the existing WinUI visualization spike does not exercise this criterion. |
| G4 | Scientific end-to-end sign-off | Decision-blocked | No executable end-to-end candidate exists. |
| G5 | Atomic save and recovery fault injection pass | Partial | Package tests and a standalone SQLite truncate-checkpoint/move-reopen spike exist; no unified workspace owner, connection-quiescence proof, packaged-database integration, or recovery orchestration exists. |
| G5 | Reopen preserves Golden results | Missing | No approved Golden or persisted analysis database. |
| G5 | PDF visual/text QA passes | Missing | Reporting/PDF is absent. |
| G5 | Exports have schema/hash/version | Missing | Export implementations are absent. |
| G5 | Support bundle excludes raw data | Missing | Support-bundle implementation is absent. |
| G6 | Derived lineage reaches Raw | Missing | An immutable bounded PlotDataSnapshot provides exact local PointId lookup, but DerivedDataset and Raw provenance are absent; this must not be promoted into a lineage claim. |
| G6 | Formula has no privileged capabilities | Missing | Formula DSL does not exist. |
| G6 | Smoothing/LOD cannot enter AnalysisSnapshot | Missing | Explorer and separation tests do not exist. |
| G6 | 3D has no-GPU fallback | Partial | PlotHost detects WebGL1/2 and has a script-harness-proven readable no-WebGL fallback; current-build WebView2 forced-unavailable and software/RDP observations remain absent. |
| G6 | Grid/plot meets frozen budget | Partial | Debug 25k Plotly measurements are 363 ms for 2D and 1209 ms for 3D; Release repetitions are 287/422/270 ms for 2D and 1272/1007/1043 ms for 3D. The 2D renderer component meets its provisional target, every measured 3D run misses, and grid, reference-PC, frame-time, combined-memory, and leak evidence remain absent. |
| G7 | Zero blocker/critical defects | Missing | P0 gates and scientific decisions remain open. |
| G7 | Performance/stability/accessibility/security pass | Partial | An allowlisted safe-diagnostic event, bounded exception categories, and defense-in-depth machine-path redaction now have executable tests. No persistent sink, retention/rotation policy, support-bundle integration, threat-model closure, accessibility qualification, soak test, or complete security evidence report exists. |
| G7 | Release runs offline on clean machine | Missing | A self-contained technical-spike App exists, but no installer or clean-machine offline qualification exists. |
| G7 | Release Evidence Bundle complete | Missing | Only early design/test evidence exists. |
| G7 | Four-party RC approval | Decision-blocked | There is no RC artifact. |
| G8 | Pilot has no unexplained differences | Decision-blocked | Pilot protocol and RC are absent. |
| G8 | Claims match validated scope | Decision-blocked | RUO wording exists; Intended Use and scope are not approved. |
| G8 | Release documentation delivered | Missing | User/install/validation/known-issues set is absent. |
| G8 | Hash/signature/SBOM/dependencies/Method hash verify | Partial | Method hash and a deterministic 42-component CycloneDX/dependency-license skeleton exist; no release artifact, signing, vulnerability adjudication, bundled runtime inventory, or final attestation exists. |

### Current interpretation

- Strong foundations exist in Domain, Analysis, Contracts, Import, and ProjectFormat, plus a validated first Application slice.
- This is cross-phase component progress, not Phase 0, Phase 1, or Phase 2 completion. G0 remains the earliest unclosed gate.
- The solution build and 602-test solution gate are green, with the final path regression separately green in the focused Architecture suite. This is local engineering evidence only; G0 remains blocked by unresolved scientific decisions, real files, remaining technology spikes, and approval.

### Dependency-ordered execution waves

| Wave | Scope | Work now | Exit evidence |
|---|---|---|---|
| A | Trustworthy baseline | Add Golden candidates/oracle, minimum SQLite schema/migrations, canonical MTP command, CI/path/architecture gates | Locked restore/build/test passes; Golden/Persistence non-empty; no path leak |
| B | Usable contracts | Import-source boundary, selected mapping DTO, assignment fact, immutable Analysis input, Fake Gateway/conformance fixtures | Fake/Real success/failure/empty/cancel/stale agreement |
| C | Persistent guided backend | Transactional import/mapping/assignment, transitions/revisions/idempotency, analysis orchestration/audit | Synthetic end-to-end; cancellation/races leave no partial state |
| D | Real-data qualification | Five approved exports, importer variants, differential Golden review, scientific sign-off | G0 scientific and G2/G3 candidate evidence |
| E | Windows shell and guided UX | App/Presentation/Infrastructure, Fake-first pages, then Real Gateway | G1 candidate, then representative G4 evidence |
| F | Project/report/recovery | SQLite packaging, recovery, immutable reports, PDF/Excel, support bundle | G5 fault, roundtrip, render/text, injection evidence |
| G | Explorer/PlotHost | Plot protocol, pinned local Plotly, WebView security, formulas/lineage, 2D/3D fallback | G6 security, lineage, screenshot, performance evidence |
| H | Hardening/release | Installer, offline qualification, threat model, SBOM, accessibility, soak, pilot | G7 then G8 approvals and evidence bundle |

Wave A is next. Wave B may proceed only where it does not invent unresolved scientific semantics.

### Decisions requiring confirmation

1. Q-SCI-001: authoritative acquisition contract, four points or full course.
2. Q-SCI-002/012: provide five de-identified real FAM/ROX exports and confirm formula, blank, saturation, and negative-RFU handling.
3. Q-SCI-003: approve or revise the provisional 15-second endpoint tolerance after real-data review.
4. Q-SCI-004: approve plateau-window inclusivity and minimum point count.
5. Q-SCI-005/006: approve replicate cardinalities and any WT per-well rule.
6. Q-SCI-007: approve a minimum usable Fm-Fb safeguard or explicitly retain finite > 0.
7. Q-SCI-008/009: approve LQC semantics and archive evidence for the 0.4 threshold/0.1% claim.
8. Q-SCI-010: approve Research Intended Use specimen, DNA range, and Cytation 5 scope.
9. Q-PRD-002/003/004/005/006/007: confirm legacy XLS, surface3D, Formula timing, report template, Method administration, and raw-input embedding before their waves.

## Validation

- Compared the actual project inventory with every phase deliverable and all 44 explicit gate criteria.
- Rechecked Method/Data Contract status instead of treating provisional defaults as approvals.
- Rechecked executable evidence after UNC-literal and application-manifest path-gate hardening: the solution build completes with 0 warnings and 0 errors, and the repository MTP gate passes all 597 tests with an explicit discovery floor. Repository path policy passes over 248 text files from repository and foreign working directories; scientific-boundary policy passes over 10 first-party presentation files. These engineering changes do not alter scientific calculations or close a phase gate.
- Verified roadmap dependencies and ran the focused relative-path audit.

## Review outcomes

- Contract: Contracts V2 now supports candidate mappings, preserves XLSX/CSV/TSV/TXT source media type without changing its frozen JSON shape, and Application can return a truthful read-only inspection result; this is still not official import, mapping confirmation, or a Run transition.
- Runtime: no production persistence, transaction, audit, or recovery claim is made from the in-memory adapter.
- Data/path: inspection resolves only validated managed-relative identities; staging consumes the external picker path only inside Infrastructure, publishes a unique managed-relative resource with streaming SHA-256, rejects reparse points and unsupported extensions, and keeps runtime paths out of results and errors. Real fixtures still require authorization and de-identification.
- Tests: local discovery is now fail-closed and green, but synthetic candidates cannot substitute for approved Golden vectors and real-file qualification.
- Docs: README's `Phase 0/1` phrase is descriptive, not proof of phase completion.
- Manual primary-agent concern decomposition was used as explicitly requested. GitHub/external review was not invoked.

## Follow-ups

- Start Wave A with separate documented changes for Golden/oracle and Persistence.
- Update this matrix criterion by criterion whenever evidence changes; never close a gate from aggregate test count alone.
