# ADR-002: Plot renderer and export limits

| Field | Value |
|---|---|
| Status | Proposed |
| Date | 2026-09-03 |
| Decision owner | Pending technical-spike evidence and product approval |

## Context

FluoAxis requires an offline renderer for shared 2D/3D PlotSpec data, exact PointId interaction, static export, and a readable no-WebGL state. The requirements propose WinUI 3 plus WebView2 and local Plotly, but G0 requires runnable evidence before that proposal becomes an accepted implementation decision.

## Proposed decision

- Keep WinUI as the native shell and isolate WebView2 to `FluoAxis.Visualization`.
- Package a pinned Plotly distribution and all host assets locally.
- Use PlotProtocol as the only host/script data contract; JavaScript renders and returns identity, while C# owns exact values and distance.
- Keep the 3D camera fixed so the X/Y/Z axes retain stable screen-relative positions. Disable scene drag, wheel zoom, double-click camera changes, and 3D camera modebar commands while retaining PointId click and selection.
- Allow vector SVG only for renderer paths proven to be SVG; use high-resolution PNG for WebGL and 3D.
- Show an explicit readable state when WebGL is unavailable.

## Evidence required before acceptance

- Exact stable Windows App SDK, WebView2 SDK/runtime, and Plotly versions, licenses, hashes, and offline distribution plan.
- x64 Debug and Release build plus a runnable WinUI/WebView2 smoke.
- Local-origin/CSP/navigation/message security tests.
- 2D, scatter3D, line3D, camera, PointId, PNG, and eligible SVG evidence.
- Nonblank screenshot inspection and GPU/software/RDP fallback observations.
- Measured startup and fixed 25k-point render results, including hardware/software metadata.

## Current status

Proposed with partial runnable evidence. The self-contained WinUI shell has rendered local pinned Plotly `4.0.0` through WebView2, exercised 2D scatter, 3D scatter, 3D line, PNG, and eligible 2D SVG, and produced a nonblank screenshot. Protocol/security tests and a deterministic PlotHost harness cover bounded messages, origin policy, camera mapping/reset, exact PointId, selection, WebGL1 acceptance, and no-WebGL fallback.

Debug measurements show a 25k 2D renderer acknowledgement of 363 ms and a 25k 3D acknowledgement of 1209 ms after moving payload preparation off the UI thread; the window became visible in 626 ms and 534 ms respectively. Release repetitions measured 287/422/270 ms for 2D and 1272/1007/1043 ms for 3D. The 2D renderer component meets the provisional 500 ms target, while every measured 3D run misses the provisional 800 ms target. These are not reference-PC qualification and do not include combined WebView2 child-process memory.

The prior Z-up turntable constrained roll but could not keep native Plotly axes fixed relative to the screen. The current spike therefore locks the complete 3D camera and removes both Plotly camera controls and the WinUI `Reset camera` command. The protocol reset message remains compatible but is not offered by this fixed-camera UI. Script evidence proves that click/selection remains wired, but current-build pixel evidence of both the lock and click behavior is still pending.

Acceptance remains blocked on that pixel-level locked-camera/click proof, controlled cold/hot startup, combined-process memory, external-request observation, software/RDP behavior, repeated-render memory evidence, and 3D performance remediation. The WebView2 Evergreen versus Fixed Version distribution policy also remains a product/release decision.

## Consequences

No production renderer choice, export guarantee, runtime distribution policy, or performance budget is frozen until the evidence above is recorded. Fixed axes mean users cannot rotate, pan, or zoom the 3D scene in this interaction mode. `Surface3D` remains outside this ADR pending Q-PRD-003.
