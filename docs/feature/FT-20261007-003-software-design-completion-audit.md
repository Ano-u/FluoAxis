---
id: FT-20261007-003
kind: feature
status: validated
area: software-design-completion
stage: audit-and-closure
tasks: [requirements-audit, algorithm-correctness, ui-contract-refinement, validation]
priority: high
branch: main
pr: null
paired_doc: null
paths: [BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md, docs/design/Requirements-Completion-20261006.md, docs/design/Software-Design-V1.1.md, README.md, .github/workflows/ci.yml, src/FluoAxis.Analysis/, src/FluoAxis.Application/, src/FluoAxis.Contracts/, src/FluoAxis.Domain/, src/FluoAxis.Import/, src/FluoAxis.Infrastructure/, src/FluoAxis.Presentation/, src/FluoAxis.App/, tests/]
contracts: [method-analysis, gateway, workbench-ui, relative-path]
hotspots: [BrafAnalysisEngine, ApplicationGateway, WorkbenchViewModel, MainWindow]
bug:
  repro: "The checkout contained a UI/Gateway constructor mismatch, an illegal mixed-LQC state path, permissive legacy input edges, and a design matrix that overstated the implemented UI scope."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [solution-test, focused-analysis, focused-workflow, architecture, repository-policy, scientific-source-boundary, plothost-node]
  tests_run: ["dotnet test FluoAxis.slnx --no-restore: 699 passed", "dotnet test --test-modules ... --minimum-expected-tests 699: passed", "dotnet build FluoAxis.slnx --no-restore: 0 warnings, 0 errors", "dotnet build src/FluoAxis.App/FluoAxis.App.csproj --no-restore -p:SelfContained=false -p:WindowsAppSDKSelfContained=false -p:RuntimeIdentifier= -p:Platform=x64: 0 warnings, 0 errors", "Verify-RepositoryPolicy.ps1: 284 files passed", "Verify-ScientificSourceBoundary.ps1: 11 files passed", "node --test web/FluoAxis.PlotHost/plot-host.test.js: 7 passed", "live WinUI smoke: workbench, mapping page, and native picker displayed", "git diff --check: passed"]
  benchmarks_run: ["ManagedFileWorkflowTests: 30 sequential fresh synthetic runs; mean 2.78-3.00 ms/run across two measurements, 16.6 MB process-wide managed allocation, no SLA asserted; artifact is ignored under artifacts/.", "Existing PlotHost spike evidence remains provisional and unchanged."]
search:
  aliases: [FluoAxis, BRAF, design-closure, requirements-audit, UI, gateway, dr-prime]
---

# FluoAxis 软件设计完成度审查与闭环优化

## Summary

本轮把需求审查结论、科学计算 fail-closed 规则、Gateway 修订号契约和 WinUI Guided Analysis 纵向切片收敛到当前 checkout。工程实现已验证；Method-Spec V1.0 仍是未签字草案，Golden manifest 仍为 candidate-unapproved，因此不能把本轮结果称为科学方法发布验收。

## Plan

1. 读取当前 checkout 的需求、方法、数据契约、设计文档、实现和测试。
2. 建立需求到代码/测试/文档的完成度矩阵，记录未完成项与风险。
3. 先修正影响正式判读正确性的算法或契约缺口，再细化 UI 和接口。
4. 运行聚焦测试、全量相关测试、构建和相对路径审计。
5. 更新本文件、change index，并完成手工 review 清单。

## Exploration log

当前分支为 `main`，工作树在本轮开始时已有未提交改动；本轮保留并基于它继续工作。已核对需求基线、`plan.md`、Method/Data Contract、Software Design、Analysis、Application、Contracts、Presentation、App、Import、Infrastructure、Persistence 和相关测试。审查确认：方法文件明确写着 `Implementation draft`，多个 Q-SCI 参数仍 provisional；因此工程默认值保持不变，没有擅自修改阈值或采样规则。原先 UI 调用已废弃的 `ManagedResourceIdentity`/旧命令构造器且 Window 有重复事件处理器；LQC mixed 路径从 `ControlQc` 直接走 `ReplicateConflict`，不符合领域状态机；必需角色、Sample 元数据、双通道 Run/time-axis 和极端时间值的 fail-closed 保护不足。

## Root cause

根因不是单一公式错误，而是跨层契约演进没有同步：Contracts v2 已要求 revision/actor/source identity，Presentation 仍使用旧形状；控制 QC 的无效状态复用了只属于样本阶段的状态节点；输入验证把“导入层保证”当成了所有调用路径的充分条件。结果是 UI 不能构建、LQC mixed 无法原子落库、坏输入可能被静默跳过。现有合成分析公式本身在 focused tests 和 Golden candidates 中通过，但真实科学批准和去标识化仪器数据证据仍缺失。

## Implementation

实现内容包括：

- `BrafAnalysisEngine`/`AnalysisMath` 继续按端点、平台、NTC own-Fb、WT、LQC、Sample 的固定顺序计算；增加极端数值 fail-closed、跨 Run/时间轴校验和旧 Method 参数的安全兼容。
- `ApplicationGatewayWorkflow` 校验 persisted calibration 的 Method hash 与完整 Fm/Fb，并拒绝缺通道输入；混合 LQC 统一通过 `ControlQcFailed -> RunInvalid`；未知 per-well call 映射到 `notDetermined`。
- `WorkbenchViewModel` 使用当前版本化 Gateway commands，始终携带 Run revision、actor、UTC issue time；每次 mutation 刷新 Run，busy/取消/过期 revision 可恢复。
- WinUI 工作区提供 FAM/ROX 独立候选选择与列映射编辑、96 孔角色/样本/重复孔编辑、阶段摘要、问题列表、逐孔结果和 Trace ID。文件选择、staging、信号准备和 plot 保存经过 `DesktopWorkspace` Infrastructure 边界；UI 不持久化外部路径，也不重算科学公式。
- 兼容旧 Method 快照中的 `ReplicateSpec(false)` 和零最小孔配置：旧值可读取并保持 hash，但引擎仍强制重复孔一致性和必需角色至少一孔，避免旧数据读取失败或空 LQC 崩溃。

## Validation

验证结果：Solution source build 通过（0 warnings/0 errors）；`dotnet test FluoAxis.slnx --no-restore` 和 fail-closed discovery gate 均通过 699 项；focused workflow、真实合成文件端到端、mapping identity、numeric boundary 和 legacy compatibility 回归通过；Repository policy 284 文件、Scientific source boundary 11 文件、PlotHost Node 7 项通过；`git diff --check` 通过。新增 synthetic-file benchmark 仅作工程测量，不设 SLA，也不能替代真实仪器 fixture、独立实验参考值或科学负责人签字。

## Review outcomes

已完成手工 review：科学公式仍只在 Analysis；UI/PlotHost 只消费后端 DTO；Run action enabled 由 `AllowedActions` 驱动；取消后会用未取消的读请求刷新 Run，避免提交已落库但 UI 保留旧 revision；外部路径只在 Infrastructure picker/staging 边界短暂使用；样本 INVALID 不渲染为阴性。当前环境未触发 GitHub/Codex review，因此本文件状态为 `validated`，不是发布批准。

## Follow-ups

仍需完成：Method-Spec 与 Data Contract 科学签字；真实去标识化仪器 fixture、独立 oracle 和双批准 Golden；per-well Calibration/Control trace 的完整持久化查询；immutable Report Snapshot、Excel/PDF/HTML 导出；工程保存/重开和迁移故障注入；Explorer 接入 Raw RFU、公式/平滑派生数据和距离交互；Windows UI Automation、DPI、High Contrast、WebView2 cold/hot smoke；PlotHost 3D 交互取舍仍需产品确认。当前软件应继续以科研原型/工程验证状态交付。
