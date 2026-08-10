# FluoAxis Agent Instructions

## Scope and identity

These instructions apply to the entire FluoAxis repository.

- Product name: `FluoAxis`.
- Canonical repository: `https://github.com/Ano-u/FluoAxis`.
- Default user-facing language: zh-CN unless the user explicitly requests another language.
- Code, commands, schema keys, namespaces, and filenames follow the repository's technical style.
- Solution, assembly, root namespace, package, and test project prefixes use `FluoAxis`.
- The first release is a Windows research-use-only fluorescence analysis workstation. It is not a clinical diagnostic product.

A nested `AGENTS.md` may add stricter rules for its subtree. It must not weaken the relative-path policy, scientific traceability, security boundaries, or research-use-only product boundary defined here.

## Primary objective

Deliver correct, reviewable, stable, efficient FluoAxis changes with complete documentation, validation, and change history.

Prefer a smaller change that is scientifically correct and well verified over a larger speculative rewrite. Explore broadly before planning, implement narrowly, and keep uncertainty visible.

## Source of truth

Before making contract-sensitive claims or edits, inspect the checked-out files. The current checkout wins over prior chat context, old screenshots, or memory.

Always inspect these repository-relative sources when relevant:

- `AGENTS.md`
- `BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md`
- `plan.md`
- `docs/method/Method-Specification-V1.0.md` when present
- `docs/method/Data-Contract-V1.0.md` when present
- applicable files under `docs/adr/`
- touched code
- touched tests
- touched documentation

The requirement baseline describes product direction, but unresolved scientific questions in `plan.md` remain unresolved until an approved Method Specification or Data Contract closes them.

Do not assume any of the following without checking the current files and evidence:

- the exact stable .NET or Windows App SDK patch;
- the real FAM/ROX workbook layout;
- the sampling cadence;
- the endpoint tolerance;
- the minimum plateau point count;
- control replicate cardinalities;
- denominator safeguards;
- the scientific validation status of `dr'=0.4` or the `0.1%` LQC claim;
- the set of enabled Explorer capabilities;
- compatibility behavior for older project archives.

## Non-negotiable relative-path policy

All project-owned filesystem paths must be relative.

This policy is a hard release gate, not a style preference.

### Forbidden

Do not introduce absolute filesystem paths into:

- source code;
- project or solution files;
- configuration;
- manifests;
- Method definitions;
- DTOs or contracts;
- database records and fields of every kind;
- tests or fixtures;
- scripts;
- generated plans;
- documentation examples;
- logs, reports, snapshots, or support bundles;
- CI definitions;
- archive entries;
- comments intended to be committed.

Forbidden path forms include drive-rooted paths, UNC paths, rooted POSIX paths, file URIs, developer-home paths, and machine-specific temporary paths.

Do not hide an absolute path inside an environment variable, JSON string, sample command, test snapshot, or generated artifact.

External HTTPS links are URLs, not filesystem paths, and are allowed.

### Required path model

- Express repository paths relative to the repository root.
- Express solution and project references relative to the containing file.
- Resolve application-owned data from an injected workspace root, project root, or platform-known application-data root at runtime.
- Resolve paths through a small, tested path-policy abstraction. Do not scatter path composition across ViewModels, importers, repositories, and report code.
- Store archive content by relative entry name only.
- Store the original source display name, content hash, and project-relative managed copy instead of persisting a developer-machine source path.
- Normalize and validate separators, traversal segments, reserved names, and case collisions at every untrusted file boundary.
- Never depend on the process current working directory for correctness.

### Runtime file-picker exception

An operating-system file picker may return an absolute external file handle or path. Only an Infrastructure boundary adapter may receive it, and only as transient data in memory so the selected file can be opened. Application and Contracts must not define a type that can carry it.

The transient value must not be:

- committed;
- placed in a reusable config or contract;
- persisted as the canonical source identity;
- written to logs, reports, telemetry, diagnostics, audit events, exception messages, stack traces, crash metadata, UI notifications, or the clipboard;
- exposed to PlotHost;
- stored in `.fluoaxisproj`;
- returned by a public application DTO.

Immediately convert it into a managed import stream plus safe metadata such as display name, size, content hash, and a project-relative managed entry. Infrastructure adapters must normalize failures to a path-free error code and safe display name before returning across the boundary. Central logging and diagnostics must apply path redaction as defense in depth.

### Test and build paths

- Use a repository-local unique temporary root such as `artifacts/test-tmp/<test-node>/<worker>/<unique-id>/`.
- Parallel tests must include test-node or worker uniqueness.
- Build, benchmark, render, and coverage output belongs under repository-relative `artifacts/` subdirectories.
- Tests must not use a global system temporary directory by accident.
- Cleanup must validate that the resolved target belongs to the approved repository-local artifact root before deletion.

### Path validation gate

Every repository change must include a relative-path audit. CI must fail when a staged diff, repository-owned text or structured artifact, generated manifest, or project archive contains a forbidden absolute filesystem path. The only allowlisted path-like values are external HTTPS URLs and transient values fully contained inside the runtime adapters described in this section.

The audit must check:

- cwd independence;
- traversal rejection;
- archive entry safety;
- project save/open after moving the repository or project directory;
- parallel temporary-path uniqueness;
- no machine-local path leakage in source, configuration, dependency/build metadata, documentation, text or binary manifests, logs, reports, support bundles, and archives;
- no absolute paths in committed diffs.

If a third-party API requires an absolute runtime path, an infrastructure adapter may resolve it only inside one call from an approved root plus a validated relative path. The absolute value must not cross the adapter API, be serialized, cached, persisted, logged, reported, included in telemetry or exception text, or returned to a caller. Discard it when the call completes. The domain, application contracts, persistent model, tests, and documentation remain relative-path-only.

## Product and scientific boundaries

FluoAxis V1 is for research use only.

It must not:

- claim clinical diagnostic approval;
- recommend treatment;
- infer benign or malignant status;
- store patient identity fields in the first release;
- imply that another approved BRAF assay validates this method;
- silently convert an invalid run into a positive or negative result.

Required research result labels remain controlled by the approved Method. The current baseline uses:

- `BRAF V600E 阳性`
- `BRAF V600E 阴性`
- explicit `INVALID` states with reasons and recommended actions

Do not change thresholds, inclusivity, control roles, time windows, replicate rules, or output labels without an approved Method change, updated Golden evidence, and versioned migration impact.

## Scientific single source of truth

All formal calculations must run in the deterministic C# Analysis Engine.

- WinUI, ViewModels, PlotHost, JavaScript, Excel export, and report templates must not recalculate QC, normalization, thresholds, or final calls.
- UI and reports render immutable Calculation Trace and Report Snapshot data returned by the backend.
- Formal analysis uses Raw RFU only.
- Smoothing, filtering, LOD, visual decimation, formula-derived columns, and Explorer transformations remain display/research derivatives and never enter the formal Analysis Snapshot.
- Intermediate display rounding must not influence classification.
- Each Run stores an immutable Method Snapshot and algorithm version.
- Old Run results remain reproducible after the current Method changes.

Preserve these current baseline invariants unless an approved Method supersedes them:

- `Fb` comes from NTC, not WT.
- `Fm` comes from qualified 100% Positive wells.
- Positive plateau qualification precedes NTC normalization QC.
- NTC wells are evaluated independently before Run-level `Fb` is formed.
- Critical control wells are not silently excluded.
- WT defines the two-dimensional FAM/ROX reference point.
- LQC and Sample wells are classified independently before replicate consistency is evaluated.
- Replicate classification conflict produces `INVALID`; a mean must not hide the conflict.
- BRAF `dr'` is a two-dimensional distance. Explorer three-dimensional distance is a different tool.

## Task classification

Classify every repository-changing task before editing. This includes source, tests, configuration, Method and Data Contracts, ADRs, plans, governance, dependencies, and documentation.

- `feature`: new capability, option, workflow, supported optimization, contract, or intentional behavior expansion.
- `fix`: regression repair, bug repair, compatibility repair, security repair, path-policy repair, memory/runtime correctness, or restoration of intended behavior.

For mixed work, choose a primary kind and create a paired change document when the distinction matters.

## Documentation-first and documentation-last

For every repository-changing task, the first repository artifact after planning and before implementation is a stub change document. The final artifact is an update to the same document after implementation, validation, and review.

Use:

- `docs/feature/` for features;
- `docs/fix/` for fixes;
- `docs/change_index.jsonl` as the retrieval registry.

Do not create a parallel documentation root.

Each registry entry is one JSON object on one line. Required compact fields:

- `id`
- `k`: `feature` or `fix`
- `s`: `stub`, `in_progress`, `validated`, `reviewed`, `done`, or `blocked`
- `a`: area
- `stg`: stage
- `tsk`: tasks
- `sum`: short summary
- `p`: changed repository-relative paths
- `hs`: hotspots
- `doc`: repository-relative change document path
- `aliases`: search aliases

Change document filenames include a stable ID and a short content slug.

Required YAML front matter:

- `id`
- `kind`
- `status`
- `area`
- `stage`
- `tasks`
- `priority`
- `branch`
- `pr`
- `paired_doc`
- `paths`
- `contracts`
- `hotspots`
- `bug.repro`
- `bug.root_cause_status`
- `bug.root_cause_confidence`
- `review.require_github_review`
- `review.require_codex_review`
- `validation.tests_planned`
- `validation.tests_run`
- `validation.benchmarks_run`
- `search.aliases`

Required sections:

- Summary
- Plan
- Exploration log
- Root cause
- Implementation
- Validation
- Review outcomes
- Follow-ups

For fixes, never skip Exploration log. Preserve rejected hypotheses and failed attempts instead of rewriting history.

## Complexity threshold and concern decomposition

Small, local, low-risk tasks may be handled directly. Use subagents when tooling supports them for work that is cross-cutting, high-risk, performance-sensitive, contract-sensitive, or has an unknown root cause.

Subagent-style decomposition is mandatory when one or more conditions apply:

- more than three files will change;
- a data, Method, project archive, frontend-backend, import, report, or persistence contract changes;
- runtime, worker, concurrency, cache, memory, or performance behavior changes;
- root cause is unknown;
- backward compatibility or migration behavior is touched.

Decompose by concern rather than by file. Use the concerns `contract`, `runtime`, `data/path`, `tests`, `docs`, and `review`. Each concern records assumptions, evidence, findings, and handoff risks. If real subagents are unavailable, emulate the same decomposition with separate inline checklists and report that manual decomposition was used.

## Architecture and ownership

Use a modular monolith with clean dependency direction. Do not introduce microservices or a localhost server for the first desktop release.

Canonical project boundaries:

- `src/FluoAxis.App/`: WinUI composition root.
- `src/FluoAxis.Presentation/`: ViewModels, navigation, and UI state.
- `src/FluoAxis.Contracts/`: frontend-backend commands, queries, DTOs, errors, and snapshots.
- `src/FluoAxis.Application/`: use cases and Run state orchestration.
- `src/FluoAxis.Domain/`: entities, value objects, and invariants.
- `src/FluoAxis.Analysis/`: pure deterministic calculations.
- `src/FluoAxis.Import/`: file format adapters.
- `src/FluoAxis.Persistence/`: SQLite and migrations.
- `src/FluoAxis.ProjectFormat/`: `.fluoaxisproj` packaging and migration.
- `src/FluoAxis.Reporting/`: immutable report snapshots and rendering.
- `src/FluoAxis.PlotProtocol/`: versioned plot messages and PlotSpec.
- `src/FluoAxis.Visualization/`: WebView2 adapter.
- `src/FluoAxis.Infrastructure/`: filesystem, logging, clock, hashing, and diagnostics.
- `web/FluoAxis.PlotHost/`: local Plotly host.

Dependency rules:

- Domain references no UI, storage, import, WebView2, or infrastructure implementation.
- Analysis references Domain and the base runtime only.
- Application depends on ports, not concrete adapters.
- Presentation depends on Contracts, not Domain, Analysis, or Persistence.
- UI never executes SQL.
- PlotHost never receives a filesystem path, database handle, or algorithm object.
- Reporting never reaches into an active ViewModel or recalculates a result.
- Circular project references are forbidden and must be guarded by CI architecture tests.

## Frontend-backend collaboration

Frontend-backend separation is contract separation inside one desktop process.

Use `IFluoAxisGateway` as the presentation boundary. Commands that modify a Run include a request ID, Run ID, expected Run revision, actor, issue time, and contract version.

The backend returns a versioned Run Snapshot containing:

- Run revision;
- domain state;
- allowed actions;
- blocking and warning issues;
- Method identity/version;
- source/mapping/calibration/control/analysis summaries;
- update time.

Rules:

- ViewModels do not assign business Run state.
- Button availability is driven by backend `allowedActions`.
- Stale revisions fail explicitly; the frontend refreshes instead of overwriting.
- Long operations expose progress, cancellation, correlation ID, retry behavior, and stale-response suppression.
- Fake and real gateways must pass the same contract conformance tests.
- Every vertical feature begins with success, failure, empty, cancellation, and stale-revision contract fixtures.

## Data, import, and project format

Treat every imported file and project archive as untrusted.

- Detect format using extension plus signature.
- Apply size, row, cell, string, and decompression limits.
- Never execute imported workbook formulas, macros, external links, scripts, or embedded objects. Explorer formulas may use only the approved resource-bounded expression DSL, which has no arbitrary code execution capability.
- Do not require Excel or Office automation.
- Stream large XLSX content instead of materializing the whole workbook DOM.
- Do not silently guess encoding, delimiter, decimal culture, missing values, or time units.
- Preserve source display name, size, SHA-256, mapping snapshot, and logical source coordinates such as worksheet, table, row, column, or cell. Never interpret `source locations` as an external filesystem path; a managed copy is identified only by its project-relative entry.
- Convert instrument-wide tables into a stable internal long-form signal model.
- Reject duplicate/non-monotonic time, missing channels, conflicting wells, and invalid numeric content according to the approved Data Contract.

The project archive extension is `.fluoaxisproj`.

- Archive entries are relative only.
- Validate traversal, duplicate entries, case collisions, decompression ratio, manifest schema, format version, hashes, and database state.
- Save through a unique sibling temporary package, close, reopen, validate, and atomically replace.
- Keep recovery metadata and the last valid package on failed save.
- Workspace SQLite may use WAL, but packaging requires checkpoint and closed connections.

## Security rules

Maintain an explicit threat model for these trust boundaries:

- instrument file to Importer;
- project archive to project opener;
- user text to HTML, SVG, Excel, and PDF exporters;
- trusted C# host to WebView2 PlotHost;
- workspace database to packaged project;
- application to local filesystem.

Required controls:

- prevent archive traversal and decompression bombs;
- prevent spreadsheet formula injection;
- encode user-controlled text for its output context;
- prohibit arbitrary script execution;
- keep Plotly, HTML, CSS, and fonts local and version-pinned;
- block external WebView2 navigation, windows, downloads, and permission prompts;
- validate WebMessage origin, type, schema, size, and correlation;
- do not expose host objects to JavaScript;
- use read-only bounded shared buffers with a JSON fallback;
- keep raw experiment data out of logs and default support bundles;
- generate an SBOM and dependency/license inventory for release.

## Performance and runtime

Measure before optimizing. Preserve correctness and bounded memory.

Budget concurrency across the whole process, including UI dispatch, Analysis, import parsing, SQLite work, report generation, PlotHost data preparation, background operations, and test workers. Do not let each subsystem independently consume all logical CPUs. Record the total CPU, memory, queue, and worker budget when runtime fields change.

- Keep UI-thread work minimal.
- Use genuine async file IO and cancellation.
- Do not wrap all work in `Task.Run` and call it asynchronous design.
- Create immutable analysis snapshots before calculation.
- Batch SQLite writes in transactions with prepared statements.
- Reuse WebView2 and use incremental Plotly updates.
- Feature-detect WebGL and shared-buffer capabilities; provide explicit fallback states.
- Bound parser depth, formula length, row counts, point counts, export size, and buffer size.
- Use bounded queues, backpressure, explicit concurrency limits, and end-to-end cancellation for producer/consumer work.
- Avoid unnecessary full reloads and repeated serialization.
- Add benchmark evidence for changes affecting import, data grid, visualization, persistence, reports, startup, or memory.

Do not adopt SIMD, unsafe code, NativeAOT, trimming, `torch.compile`-style speculative optimization, or custom memory infrastructure without benchmark evidence and a fallback path.

## Testing requirements

Run the smallest meaningful focused tests first, then broader tests proportional to risk.

Required test categories when applicable:

- Domain and Analysis unit tests;
- exact threshold and inclusivity boundaries;
- approved Golden Dataset regression;
- independent-oracle differential tests;
- contract serialization and fake/real conformance;
- Run state and stale-revision tests;
- importer fixture and malformed-input tests;
- project save/open/migration/fault-injection tests;
- report render and text-extraction tests;
- PlotHost protocol, screenshot, point selection, camera, and export tests;
- WinUI integration smoke;
- keyboard, Automation, Narrator, High Contrast, and DPI review;
- path-policy and cwd-independence tests;
- archive, formula, export, and WebMessage security tests;
- performance and leak tests.

For runtime C# changes, include a syntax/build/import sanity check. For contract changes, add regression tests. For performance changes, record hardware, software versions, fixture hash, before/after numbers, and fallback behavior.

Golden Dataset rules:

- stable case ID;
- approved Method and input hashes;
- expected intermediate values and final state;
- independent reference calculation;
- dual approval by scientific and engineering owners;
- no unapproved silent update of expected results;
- no sensitive raw data committed without authorization and de-identification.

## Known hotspot checklist

Before finalizing a relevant change, explicitly check:

- relative-path-only compliance;
- cwd independence;
- parallel test temporary-path uniqueness;
- immutable Raw RFU;
- no Analysis formula in UI, JavaScript, export, or report layers;
- no unnecessary evaluation-row retention;
- no unnecessary dataloader-like object retention for fixed analysis slices;
- bounded import/archive/report/plot memory;
- shared cache assumptions, if introduced;
- resume/open paths tolerate older successful records missing newer optional keys;
- explicit user budgets and limits are not silently overridden;
- project movement and save/open roundtrip;
- old Method snapshots remain reproducible;
- regression coverage for every changed contract;
- RUO wording and no expanded clinical claim.

## Dependency and tool selection

Use existing wheels first:

1. repository-native abstraction;
2. official stable library/API;
3. custom infrastructure only when necessary.

Pin all dependencies. Do not use floating versions, Preview, Experimental, CDN-hosted runtime assets, or unreviewed binary drops.

Every dependency records:

- purpose;
- owner;
- pinned version;
- source and hash where applicable;
- license;
- security source;
- upgrade policy;
- replacement plan.

Dependency upgrades are isolated changes and must run build, unit, Golden, import, visualization, report, performance, packaging, and offline smoke checks as applicable.

## Review requirements

After implementation and local validation, self-review against:

- scientific correctness;
- contract consistency;
- relative-path discipline;
- security boundaries;
- path and archive handling;
- memory behavior;
- CPU oversubscription risk;
- backward compatibility;
- accessibility;
- tests and documentation completeness;
- research-use-only scope.

Use GitHub review and Codex review when tooling supports them. If unavailable, record the limitation and perform a manual checklist. Do not mark non-trivial work done while required review is pending or substantive comments remain unresolved.

## Git and repository rules

- Canonical remote is `https://github.com/Ano-u/FluoAxis`.
- Do not hard-code a branch name; inspect the current checkout.
- Preserve unrelated user changes in a dirty worktree.
- Do not use destructive reset or checkout operations without explicit authorization.
- Keep commits focused and reviewable.
- Do not commit generated local caches, machine-specific files, secrets, sensitive research data, or absolute paths.
- Do not rewrite published history unless the user explicitly requests it.

## Completion report

Every completed implementation report includes:

- classification: feature or fix;
- change document path;
- changed files;
- branch assumption used;
- contracts touched;
- tests run and exact outcomes;
- benchmarks run and exact outcomes;
- path-policy validation;
- review status;
- unresolved risks;
- follow-up items.

If validation could not run, state why. Do not present an unverified result as complete.
