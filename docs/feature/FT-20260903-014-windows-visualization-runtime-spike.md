---
id: FT-20260903-014
kind: feature
status: in_progress
area: windows-visualization-runtime
stage: phase-0
tasks:
  - winui-shell-spike
  - local-webview2-host-spike
  - plotly-2d-3d-export-spike
  - runtime-security-and-fallback-evidence
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.App/
  - src/FluoAxis.Visualization/
  - web/FluoAxis.PlotHost/
  - tests/FluoAxis.Visualization.Tests/
  - docs/adr/ADR-002-plot-renderer-and-export.md
  - artifacts/spikes/windows-visualization/
  - Directory.Packages.props
  - DEPENDENCIES.md
  - build/dependency-metadata.json
  - FluoAxis.slnx
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - local-plot-host-origin-v1
  - webview2-security-baseline-v1
  - plot-renderer-capability-report-v1
hotspots:
  - windows-app-sdk-build-and-distribution
  - webview-origin-and-navigation
  - local-asset-integrity
  - plotly-license-and-offline-packaging
  - webgl-and-software-fallback
  - screenshot-and-export-fidelity
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - x64 Debug and Release WinUI build
    - local virtual-host navigation and WebMessage origin validation
    - external navigation window download and permission rejection
    - local pinned Plotly 2D and 3D render smoke
    - PNG and eligible SVG export smoke
    - WebGL unavailable readable fallback
    - offline asset and external-request audit
    - full repository build test and path-policy gates
  tests_run:
    - PlotProtocol focused tests 32 passed
    - Visualization security policy tests 14 passed
    - PlotHost node harness tests 7 passed
    - repository MTP gate 582 passed
    - Debug and Release App builds passed with zero warnings and errors
    - JavaScript syntax and diff checks passed
  benchmarks_run:
    - Debug 25k-point 2D visible window 626 ms Plotly render 363 ms end-to-end observation 1707 ms App working set 169.3 MB
    - Debug 25k-point 3D visible window 534 ms Plotly render 1209 ms end-to-end observation 8333 ms App working set 174.9 MB
    - Release 25k-point 2D Plotly render repetitions 287 ms 422 ms 270 ms
    - Release 25k-point 3D Plotly render repetitions 1272 ms 1007 ms 1043 ms
search:
  aliases:
    - WinUI WebView2 Plotly spike
    - local PlotHost
    - ADR 002 plot renderer
    - WebGL fallback
---

# Qualify the Windows visualization runtime

## Summary

Build the Phase 0 runnable evidence for a minimal WinUI shell, local WebView2 PlotHost, pinned offline Plotly 2D/3D rendering, static export, security boundaries, and readable capability fallback. This change is a technical spike, not the production Explorer UI. It must not claim MSIX, SharedBuffer, PDF, accessibility, or performance success unless each item is actually exercised.

## Plan

### Contract

- Consume the existing PlotProtocol envelope and PlotSpec contracts without giving JavaScript scientific authority.
- Record runtime capabilities and failures as path-free, versioned evidence.

### Runtime

- Establish the smallest buildable WinUI 3 shell and a reusable WebView2 adapter.
- Package HTML, CSS, fonts, and Plotly locally; block remote runtime dependencies.
- Reuse one WebView2 instance and exercise 2D, scatter3D/line3D, camera, PointId, and static export where supported.

### Data/path

- Keep all committed and persisted paths relative. Runtime-resolved locations remain inside Infrastructure/Visualization calls and never cross PlotProtocol.
- Reject external navigation, new windows, downloads, permissions, untrusted origins, oversized messages, and host-object exposure.

### Tests

- Separate build evidence from runnable rendering evidence. A successful compile alone must not close the G0 visualization criterion.
- Produce deterministic smoke artifacts under the repository-owned spike artifact root and inspect them for nonblank output.

### Docs

- Complete ADR-002 only with versions, licenses, export limits, fallback behavior, and measured evidence actually obtained.
- Update the gate audit criterion by criterion without promoting partial evidence.

### Review

- Use manual `contract/runtime/data-path/tests/docs/review` decomposition because the user explicitly requested primary-agent-only execution.

## Exploration log

- The machine has .NET SDK 10.0.400 and Windows Desktop Runtime 10.0.11, but no Visual Studio instance, WinUI template, Windows workload, Windows App SDK cache, WebView2 SDK cache, or CommunityToolkit cache was detected.
- Live NuGet inspection on 2026-09-03 reports latest stable versions matching the existing candidate pins: Windows App SDK 2.4.0, Windows SDK BuildTools 10.0.28000.2705, WebView2 SDK 1.0.4191.47, and CommunityToolkit.Mvvm 8.4.2. This proves package publication, not compatibility or runtime qualification.
- Microsoft Learn pages for Windows App SDK downloads and WebView2 Runtime distribution were reachable. Exact runtime distribution choice remains unqualified until the spike runs.
- Plotly version, package source, hash, and license are not yet selected. No runtime asset may be copied from a CDN.
- Plotly `4.0.0` is now pinned in `package-lock.json`, copied into the application as a local asset, and recorded with its MIT license. No CDN runtime asset is used.
- `Surface3D` remains Q-PRD-003 and is outside this spike; required `Scatter3D` and `Line3D` remain in scope.
- Runtime inspection on 2026-09-05 found Microsoft Edge WebView2 Runtime `137.0.3296.83` already installed. The download prompt was reproduced as .NET host error `0x80008096`: the generated apphost searched the machine-wide .NET root, which contained only .NET 6/8, while .NET 10.0.11 was installed under the current-user .NET root.
- Launching the same build with the current-user .NET root explicitly selected kept the process responsive and advanced the window title to `FluoAxis - Plot rendered`. This proves the local WebView2/Plotly ready-rendered handshake, but does not yet prove nonblank pixels or export fidelity.
- `WindowsAppSDKSelfContained=true` copies Windows App SDK binaries but does not make the .NET runtime self-contained. The application project therefore still required a separately installed .NET 10 runtime.
- The original benchmark launch switch used `LaunchActivatedEventArgs.Arguments`, but unpackaged process launches did not propagate ordinary command-line arguments there. Environment-variable launches proved the benchmark path while process arguments still selected the six-point fixture.

## Root cause

Not applicable. This is a required Phase 0 technology qualification.

## Implementation

- Mark the executable as .NET self-contained in addition to Windows App SDK self-contained so the unpackaged application does not request a machine-wide .NET installation.
- Normalize WebView2 initialization failures to path-free error codes and expose a readable fallback state instead of silently hiding the renderer.
- Extract the PlotHost URI trust decision into a deterministic policy and cover exact local-document acceptance plus scheme, authority, port, path, query, fragment, and malformed-input rejection.
- Add a Windows-targeted Visualization test project to the solution and CI discovery gate; the tests exercise policy logic without constructing a WebView2 control.
- Replace the placeholder empty export messages with bounded PNG/SVG request and response payloads. Render commands and export responses have direction-specific byte limits so 25k-point JSON and high-resolution static images are possible without making the WebMessage boundary unbounded.
- Map renderer-neutral `CameraSpec` fields explicitly to Plotly's nested camera shape, restore the initial camera on reset, and correlate reset/selection acknowledgements without accumulating outstanding requests.
- Keep the 3D coordinate frame at a fixed screen-relative position by disabling scene drag, wheel zoom, double-click camera changes, and all 3D camera modebar controls. Retain the configured Z-up camera and fixed 1:1:1 scene aspect ratio. Point click and selection remain enabled, but interactive 3D rotation and zoom are intentionally unavailable.
- Accept WebGL 1 or 2 for Plotly 3D while reporting WebGL2 separately as a capability; fail closed to a readable state when neither is available.
- Preserve exact PointId values for click/selection events, convert logarithmic axis bounds to Plotly exponent ranges, and keep camera state stable across data revisions with plot identity as `uirevision`.
- Reject a ready handshake whose protocol version, pinned Plotly version, or export capability does not match the host requirement.
- Add a dependency-free Node harness for camera, log axis, PointId, selection, correlated reset, WebGL1, and no-WebGL behavior, and run it in CI.
- Parse spike-only benchmark switches from the process command line, wait until the WinUI window has activated, and prepare/serialize the 25k-point payload on a cancellable worker task. This keeps first presentation responsive and prevents stale preparation from overwriting a newer render request.
- Remove the WinUI `Reset camera` command after fully locking the camera. The protocol command remains available for compatibility and deterministic harness coverage, but the current spike no longer advertises an inapplicable user action.
- Require every non-render PlotHost command to match the active `plotId` and generation exactly; disposal clears script-side active identity and camera state so delayed or cross-plot commands cannot operate on the purged DOM plot.
- Serialize all asynchronous PlotHost operations against the single Plotly DOM target so overlapping WebMessage callbacks cannot reverse active render identity or mutate Plotly concurrently.

## Validation

- `dotnet test tests/FluoAxis.PlotProtocol.Tests/FluoAxis.PlotProtocol.Tests.csproj --no-restore`: 32 passed.
- `node --test web/FluoAxis.PlotHost/plot-host.test.js`: 7 passed.
- repository MTP fail-closed gate: 582 passed, 0 failed, 0 skipped.
- Debug and Release `FluoAxis.App` builds: passed with 0 warnings and 0 errors.
- Current Debug 25k 2D run: visible window in 626 ms, renderer acknowledgement in 363 ms, end-to-end observation in 1707 ms, App working set 169.3 MB. The renderer component meets the provisional 500 ms target; this is not a Release/reference-PC SLA result and excludes WebView2 child-process memory.
- Current Debug 25k 3D run: visible window in 534 ms, renderer acknowledgement in 1209 ms, end-to-end observation in 8333 ms, App working set 174.9 MB. It misses the provisional 800 ms renderer target and therefore remains an optimization/qualification gap.
- Repository path-policy verification passed for 230 text files; `git diff --check` passed apart from the pre-existing `.gitignore` line-ending warning.
- Actual WinUI/WebView2 smoke before the latest hardening rendered 2D scatter, 3D scatter, and 3D line; PNG and eligible 2D SVG exports completed. The captured 3D screenshot is nonblank.
- Release 25k repetitions measured 2D renderer acknowledgements of 287/422/270 ms and 3D acknowledgements of 1272/1007/1043 ms. All 2D repetitions meet the provisional 500 ms target; all 3D repetitions miss the provisional 800 ms target. These are local spike observations, not reference-PC qualification.
- Script-level deterministic evidence covers initial camera mapping, locked camera controls, ignored unsolicited camera relayout, reset roundtrip, exact PointId click, selection acknowledgement, WebGL1 acceptance, and no-WebGL readable fallback. The two current axis-check screenshots are nearly identical but do not prove that the drag gesture was delivered, so they are not promoted to pixel-level lock evidence.
- A fresh Release capture targeted the FluoAxis window by its verified native handle after the toolbar repair. It confirms that `Reset camera` is absent and the 3D scene is nonblank. This evidence must not be presented as proof that a drag reached the PlotHost with unchanged pixels; that interaction-level check remains pending.
- Still pending: current-build pixel-level locked-camera/click integration, controlled Release cold/hot startup, combined App plus WebView2 memory, external-request observation, software/RDP behavior, repeated-render leak evidence, and 3D performance remediation.

## Review outcomes

- Contract review found and corrected a renderer-neutral/Plotly camera shape mismatch, incorrect logarithmic range semantics, unstable camera revision identity, and incomplete selection/reset correlation.
- Runtime review found and corrected the WebGL2-only capability gate, which incorrectly rejected systems that support Plotly 3D through WebGL1.
- Verification review confirmed that existing screenshots and exports are real but insufficient for 25k, no-WebGL, offline-network, software-rendering, and leak claims.
- Review of the turntable implementation found that Z-up prevented roll but did not keep axes fixed relative to the screen. The camera is now fully locked; this deliberately trades away 3D view manipulation for stable axis placement.
- Runtime correlation review found that non-render commands rejected only older generations and could target the active DOM plot with another `plotId` or a future generation. Exact active identity correlation and dispose-state clearing now have Node regression coverage.
- Async dispatch review found that WebMessage callbacks could overlap on the shared Plotly DOM node. A serialized operation queue and deferred-render regression now prove arrival-order execution and later-render event identity.
- ADR-002 remains Proposed. No G0 or G6 gate is declared complete.

## Follow-ups

- Confirm the production WebView2 Evergreen versus Fixed Version distribution policy after measured deployment evidence.
- Qualify SharedBuffer and PrintToPdf in separate evidence slices if they cannot be proven without broadening this spike.
