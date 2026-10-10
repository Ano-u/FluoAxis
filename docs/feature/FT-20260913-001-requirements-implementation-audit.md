---
id: FT-20260913-001
kind: feature
status: in_progress
area: governance-and-delivery
stage: cross-phase
tasks:
  - audit-requirements-completion
  - reconcile-documentation-with-checkout
  - select-next-implementation-slice
priority: P0
branch: main
pr: null
paired_doc: docs/fix/FX-20260913-001-workflow-command-atomicity.md
paths:
  - BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md
  - README.md
  - plan.md
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/feature/FT-20260912-001-application-engine-orchestration.md
  - docs/feature/FT-20260913-001-requirements-implementation-audit.md
contracts:
  - requirements-traceability
  - evidence-based-completion
  - phase-gate-claims
hotspots:
  - stale-documentation
  - scientific-approval
  - implementation-overclaim
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - compare requirement sections and acceptance criteria with current source tests and documents
    - run the focused workflow and full repository validation gates
    - run the relative-path and scientific-source policy gates
  tests_run: []
  benchmarks_run: []
search:
  aliases:
    - requirements implementation audit
    - completion truth check
    - next implementation slice
---

# Reconcile the BRAF requirements baseline with the current implementation

## Summary

Audit the current checkout against the frozen requirements baseline, distinguish implemented components from accepted product capabilities, correct stale completion claims, and select the highest-value implementation slice that can be closed without inventing unresolved scientific rules.

## Plan

1. Compare the requirement baseline, plan, Method/Data Contracts, ADRs, source, and executable tests.
2. Classify each major capability as implemented, partial, missing, or decision-blocked.
3. Record documentation claims that no longer match the checkout.
4. Complete the selected Application-to-Analysis workflow slice and its paired atomicity fix.
5. Re-run focused and repository-wide validation, then update the evidence matrix.

## Exploration log

- The prior 44-criterion audit correctly prevents phase-level overclaiming but predates the current Application workflow implementation.
- Current source includes mapping, assignment, calibration, control QC, sample analysis, SQLite workflow persistence, and synthetic end-to-end tests while the README and related change documents still describe those mutations as unavailable or stub-only.
- The working tree is mostly untracked relative to the initial `main` commit; completion must be based on current files and executable evidence rather than commit history.

## Root cause

Not applicable. This is a traceability and delivery-governance change.

## Implementation

The audit selected the Application-to-Analysis workflow as the next executable slice. The current checkout records the implementation evidence, action contract alignment, source identity revalidation, mapping snapshot persistence, the follow-up atomicity fix in `FX-20260913-001`, and the WinUI composition-root/UI contract fix in `FX-20260922-001`. The workflow fix remains review-pending until the pinned .NET gates execute; the WinUI fix is source-validated but lacks a live desktop smoke in this environment.

### Direct requirement-domain snapshot

| 需求域 | 当前事实 | 证据 | 与 V1.0 的差距 |
|---|---|---|---|
| 科学分析与判读 | C# `BrafAnalysisEngine` 已覆盖校准、NTC、WT/LQC、Sample、阈值边界和重复孔冲突；Application workflow 已能在合成板上贯通到 `Analyzed` | `src/FluoAxis.Analysis/`, `src/FluoAxis.Application/`, Analysis/Application/Golden tests, `docs/method/Method-Specification-V1.0.md` | 没有批准的真实 Golden corpus；Q-SCI-001~010、012 仍有未决项，不能宣称科研验收完成 |
| 导入与数据契约 | XLSX 流式 reader、CSV/TSV/TXT reader、格式签名、候选区发现、相对路径托管 staging 和只读 inspection adapter 已有 | `src/FluoAxis.Import/`, `src/FluoAxis.Infrastructure/ManagedImport*`, importer/infrastructure tests | XLS 首发支持缺失；WinUI 没有文件选择器、拖拽、staging 调用和可视化预览；正式 import mutation 尚未从 UI 可达 |
| FAM/ROX mapping | Contracts V2、候选区 DTO、确认 mapping command、Application/SQLite mapping snapshot 持久化已有 | `src/FluoAxis.Contracts/`, `src/FluoAxis.Application/`, `src/FluoAxis.Persistence/`, integration tests | 前端 mapping editor、手工确认界面、字段级错误修复流程尚未实现 |
| 96 孔 Plate Map | Domain assignment、角色互斥、后端 assignment workflow 与原子状态门已实现 | `src/FluoAxis.Domain/`, `src/FluoAxis.Application/`, workflow tests | UI 仍是 stage skeleton；没有 96 孔网格、批量框选、表格双向高亮和 Sample metadata 编辑 |
| Guided UI | NavigationView、TabView、三栏工作区、RUO 标识、InfoBar、阶段说明和 Explorer 已有；当前组合根使用真实 `ApplicationGateway`，阶段 rail 可导航且操作按钮保持能力诚实 | `src/FluoAxis.App/`, `src/FluoAxis.Presentation/`, `docs/fix/FX-20260922-001-real-gateway-ui-contract.md` | 仍是过渡性 code-behind；没有文件 picker、阶段 command、进度/取消的完整 UI、结果页、View Calculation、键盘/高对比度/DPI 自动化证据 |
| Explorer / PlotHost | 本地固定 Plotly、2D/3D scatter/line、PNG/二维 SVG、PointId 选择、2D/3D 距离、WebGL fallback 与安全边界已有 | `web/FluoAxis.PlotHost/`, `src/FluoAxis.PlotProtocol/`, Visualization/Node tests, ADR-002 | VirtualDataSheet、Formula Builder、平滑、任意列映射、多序列/误差条/对数轴未完成；需求要求的 orbit/zoom 与当前固定相机 spike 存在未决取舍；3D 性能和 live pixel evidence 未达标 |
| 报告与导出 | `.fluoaxisproj` 基线和 PlotHost 导出 spike 存在 | `src/FluoAxis.ProjectFormat/`, `src/FluoAxis.App/MainWindow.xaml.cs`, project/Plot tests | Excel、PDF Research Analysis Report、不可变 Report Snapshot、版本/hash 记录、故障恢复和 support bundle 尚未实现 |
| 工程安全与可移植性 | 相对路径门禁、科学 source boundary、归档/导入限制、SQLite WAL/checkpoint、结构化错误已实现并持续验证 | `build/Verify-RepositoryPolicy.ps1`, `build/Verify-ScientificSourceBoundary.ps1`, `src/FluoAxis.Infrastructure/`, `src/FluoAxis.ProjectFormat/` | 真实发布安装器、runtime distribution、完整 CI hosted evidence、100 次重复导入/内存泄漏/崩溃恢复证据仍缺 |

这张表是当前 checkout 的事实快照：它支持“底层工程骨架和分析垂直切片已实现”的判断，不支持“V1.0 产品已完成”或“G0/G1/G4/G5/G8 已通过”的判断。

## Validation

Current checkout evidence: the repository path policy passes for 269 text files, the scientific source-boundary gate passes for 11 first-party presentation files, and the PlotHost Node suite passes 7/7. The pinned C# build/test gates could not run because no .NET SDK is installed on this machine. Existing prior-run evidence reports the earlier workflow slice as green, but it does not validate this new atomicity diff. Scientific Method and Golden approval remain open by design.

## Review outcomes

Manual review completed. The repository still must not claim a production-ready Guided UI, approved Golden Dataset, report/export completion, or a fully validated stage-command atomicity fix until the pinned .NET gates execute and the fault-injection follow-up is covered.

## Follow-ups

- Run the pinned .NET validation for `FX-20260913-001` and refresh this matrix with executed counts.
- Keep the next product slice focused on making the real Application Gateway's import inspection and managed staging reachable from the WinUI shell; do not broaden into Report/Explorer completion before the Guided path is user-operable.
