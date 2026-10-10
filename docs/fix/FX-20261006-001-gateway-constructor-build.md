---
id: FX-20261006-001
kind: fix
status: validated
area: application-build
stage: guided-analysis
tasks: [restore-build, preserve-engine-injection]
priority: P0
branch: main
pr: null
paired_doc: docs/feature/FT-20261006-001-software-design-closure.md
paths: [src/FluoAxis.Application/ApplicationGateway.cs, tests/FluoAxis.Integration.Tests/SqliteWorkflowRoundTripTests.cs, tests/FluoAxis.Application.Tests/ApplicationGatewayTests.cs, docs/fix/FX-20261006-001-gateway-constructor-build.md, docs/change_index.jsonl]
contracts: [application-gateway-construction, explicit-engine-injection]
hotspots: [optional-parameter-order, existing-callers, null-engine-guard]
bug:
  repro: dotnet build FluoAxis.slnx --no-restore reports CS1737 in ApplicationGateway constructor
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [application-tests, integration-tests, solution-build, repository-policy]
  tests_run:
    - "FT-20261007-003 integrated validation: dotnet build FluoAxis.slnx --no-restore; 0 warnings, 0 errors"
    - "FT-20261007-003 integrated validation: dotnet test FluoAxis.slnx --no-restore; 699 passed, including Application and Integration"
    - "FT-20261007-003 integrated validation: App build and repository policy passed"
  benchmarks_run: ["Not applicable; constructor signature repair changes no runtime algorithm or workload"]
search:
  aliases: [CS1737, gateway constructor, required engine after optional parameters]
---

# 修复 Gateway 引擎注入构造函数的编译阻断

## Summary

Fresh 全解决方案构建发现必需 engine 参数位于可选参数之后，C# 报 CS1737。保留默认引擎重载和显式非空引擎重载，修复参数声明并明确调用点，不改变科学计算。

## Plan

核对全部构造调用；最小修复参数顺序约束；验证默认引擎与 null guard；运行 Application/Integration 和全解决方案门禁。

## Exploration log

- 系统 SDK host 未装 SDK，核验已有本地 SDK 为 10.0.400 后执行 fresh build。
- Build 失败：0 warnings / 1 error，CS1737，位置为显式引擎重载。
- 工作区已将默认引擎和显式引擎拆为两个重载，但第二重载前三个参数仍为 optional。
- 不能仅依赖已有构建产物或旧记录；它们没有验证这个未提交声明。

## Root cause

必需参数不能出现在可选参数之后。去掉显式重载的前置参数默认值即可保留原有五参数顺序；缺省 Infrastructure adapter 的测试调用需显式传 null。

## Implementation

`ApplicationGateway` 保留四参数默认引擎重载，并委托给五参数显式 `IAnalysisEngine` 重载。显式重载的 `timeProvider`、`importInspection`、`signalPreparation` 不再声明默认值，因此必需 engine 参数不再出现在可选参数之后；对应测试调用显式传入 null adapter。构造器继续拒绝 null repository/engine，科学公式与 Method 未改变。整合范围见配对设计记录及 `docs/feature/FT-20261007-003-software-design-completion-audit.md`。

## Validation

启动时的 CS1737 构建失败保留于 Exploration log。修复后的上一轮 fresh 证据由 FT-20261007-003 记录：solution build 为 0 warnings / 0 errors；`dotnet test FluoAxis.slnx --no-restore` 为 699 passed（包含 Application/Integration）；App build 和 repository policy 通过。此记录只关闭构造声明阻断，不声称后续持久化/证据查询新增功能已经由这一轮结果验证。

## Review outcomes

静态复核确认两个重载没有歧义，现有默认调用仍使用生产引擎，显式测试注入仍可用，null-engine guard 保留。FX-20261007-004 修复的是后续 UI 命令调用和事件接线问题，与本构造签名修复分开记录。无已知未解的构造器 review 项；未触发外部 GitHub/Codex review。

## Follow-ups

本修复无未解项。无科学 Method 变更；性能 benchmark 不适用。产品范围与科学批准的后续事项见配对设计文档和 FT-20261007-003。
