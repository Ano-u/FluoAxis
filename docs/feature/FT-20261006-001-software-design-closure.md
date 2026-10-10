---
id: FT-20261006-001
kind: feature
status: validated
area: product-design-and-verification
stage: cross-phase
tasks: [requirements-audit, guided-ui-design, gateway-design, algorithm-verification]
priority: P0
branch: main
pr: null
paired_doc:
  - docs/fix/FX-20261006-001-gateway-constructor-build.md
  - docs/fix/FX-20261007-002-analysis-numeric-fail-closed.md
paths:
  - docs/feature/FT-20261006-001-software-design-closure.md
  - docs/design/Software-Design-V1.1.md
  - docs/design/Requirements-Completion-20261006.md
  - docs/change_index.jsonl
  - README.md
contracts: [requirements-traceability, proposed-guided-workflow, proposed-gateway-evolution]
hotspots: [scientific-approval, raw-rfu-isolation, revision-consistency, relative-path-policy]
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [focused-analysis-and-golden, full-checkout-tests, repository-policy, scientific-source-boundary, native-workbench-smoke]
  tests_run:
    - "Integrated verification recorded by FT-20261007-003: dotnet test FluoAxis.slnx --no-restore; 699 passed"
    - "Integrated verification recorded by FT-20261007-003: dotnet build FluoAxis.slnx --no-restore; 0 warnings, 0 errors"
    - "Integrated verification recorded by FT-20261007-003: repository policy 284 files; scientific source boundary 11 files; PlotHost Node 7 tests passed"
    - "Native WinUI smoke recorded by FX-20261007-004: workbench, mapping empty state and picker displayed; complete UI journey remains unverified"
  benchmarks_run:
    - "FT-20261007-003 records 30 sequential synthetic file workflows; mean 2.78-3.00 ms/run across two measurements, 16.6 MB process-wide managed allocation; no SLA"
search:
  aliases: [software design closure, requirements completion, guided analysis ui, calculation trace interface]
---

# FluoAxis 软件设计闭合与完成情况核验

## Summary

本记录的需求审查与设计细化已由 `docs/feature/FT-20261007-003-software-design-completion-audit.md` 整合验证，UI 契约与可操作工作台由 `docs/fix/FX-20261007-004-ui-gateway-contract.md` 承接。交付为设计文档、完成度矩阵和真实 WinUI 工程切片；整个 V1 产品与科学方法发布验收仍未完成。

## Plan

1. 分别审查科学算法、UI/契约、完成度与交付门禁。
2. 逐项记录实现证据、验收缺口、优先级和依赖。
3. 细化 Guided 页面、错误/取消/旧响应状态、计算追溯和报告边界。
4. 运行当前 checkout 的算法、Golden 候选、集成与策略验证。
5. 独立审查设计与真实 WinUI 工作台，最后更新本记录。原计划的独立 HTML 原型未生成，已被真实 WinUI 页面替代，不计为交付物。

## Exploration log

- 当前分支为 `main`，remote 为规范 FluoAxis 仓库。工作树已有多项未提交修改；本轮保留其内容，不以历史文档中的测试数量代替 fresh 验证。
- Method 为未签字 Implementation draft；Golden manifest 为 candidate-unapproved。
- WinUI 使用真实 ApplicationGateway，但导入/映射/孔位与报告的用户操作仍缺接线。
- 系统 dotnet host 没有 SDK；继续核验已有用户本地 SDK，不修改 global.json。
- 以上为启动时观察。后续检查到可用的锁定 SDK，完成构建与测试；早期 UI 接线缺口和算法防御问题分别由配对 fix 及 FT-20261007-003、FX-20261007-004 收敛。

## Root cause

不适用。设计与实现证据之间存在缺口，需要明确交付层次和可执行验收条件。

## Implementation

- 更新 `docs/design/Software-Design-V1.1.md` 与 `docs/design/Requirements-Completion-20261006.md`，明确已实现工程行为、可执行验收条件和未关闭科学门禁。
- Guided 工作台已连接 managed import、mapping、plate assignment、Calibration、Control QC、Sample Analysis，并显示后端摘要、逐孔结果和 Trace ID；实现证据详见 FT-20261007-003、FX-20261007-004。
- `docs/design/workbench-prototype.html` 没有生成。独立 HTML 原型计划被真实 WinUI 切片替代，未把不存在的文件作为验收证据。
- 配对构造器与数值防御修复保留各自根因历史；持久工作区与完整证据查询的新范围由 FT-20261007-005 单独记录，不纳入本记录的上一轮验证结论。

## Validation

上一轮整合验证已记录于 FT-20261007-003：`dotnet test FluoAxis.slnx --no-restore` 为 699 passed；solution build 和 App build 均为 0 warnings / 0 errors；repository policy、scientific source boundary、PlotHost Node tests 和 diff 检查通过。此处引用该已执行证据，不将后续持久化/证据查询增量误称为已经由这些结果覆盖。原生烟雾验证仅覆盖工作台、映射空态和文件选择器，完整文件到分析的 GUI 操作仍需补验。

## Review outcomes

算法/科学契约、UI/接口、完成度/安全/路径三个 concern 已完成静态复核，关键发现及修复汇总在 FT-20261007-003。GitHub/Codex 外部 review 未触发；本记录为工程 `validated`，不表示 release approval。

## Follow-ups

真实去标识化仪器 fixture、科学负责人 Method 签字和 Golden 双批准不可由工程代理代签。完整逐孔 Calibration/Control 追溯、Report Snapshot、正式报告导出、工程归档和发布级 UI/可访问性验收继续按完成度矩阵推进；当前状态不是全功能软件设计或产品交付完结。
