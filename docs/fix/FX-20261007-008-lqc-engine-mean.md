---
id: FX-20261007-008
kind: fix
status: reviewed
area: analysis-engine
stage: control-qc
tasks: [lqc-group-mean, algorithm-boundary, persisted-evidence]
priority: high
branch: main
pr: null
paired_doc: docs/feature/FT-20261007-007-engineering-workflow-completion.md
paths: [src/FluoAxis.Analysis/BrafAnalysisEngine.cs, src/FluoAxis.Analysis/ControlSampleModels.cs, src/FluoAxis.Application/ApplicationGatewayWorkflow.cs, src/FluoAxis.Application/RunWorkflowData.cs, src/FluoAxis.Contracts/AnalysisResultDtos.cs, tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs, tests/FluoAxis.Application.Tests/WorkflowInputIntegrityTests.cs, tests/FluoAxis.Application.Tests/RunWorkflowEndToEndTests.cs, tests/FluoAxis.Infrastructure.Tests/PortableProjectPackageTests.cs]
contracts: [ControlQcResult.LqcMeanDrPrime, persisted-control-result, Method-Specification-9.4-9.7]
hotspots: [replicate-consistency, fail-closed, historical-evidence]
bug:
  repro: "一致 Negative LQC 返回失败后均值丢失，且 Application.LqcMeanOf 仍执行正式平均计算。"
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [analysis-lqc-directions-and-means, application-authoritative-mean, failed-control-reopen-package, historical-null-retention]
  tests_run: [Analysis-88-of-88, Application-93-of-93, Infrastructure-82-of-82, relative-path-policy-372-text-files, focused-diff-check]
  benchmarks_run: []
search:
  aliases: [FR-QC-007, LqcMeanOf, LqcMeanDrPrime, negative LQC mean]
---

# LQC 组均值由 Analysis 生成并完整存档

## Summary

恢复 Method §9.4–§9.7 的既有合同：单孔分类一致后由 Analysis 计算均值；一致 Negative LQC 保留方向及已计算均值，同时仍为控制质控失败；冲突组不产生均值。

## Plan

1. 核对引擎、应用映射、持久化和旧记录读取路径。
2. 添加引擎返回均值字段，移除应用层重算及错误的通过状态门控。
3. 验证正向、负向、冲突、无效输入及项目往返；旧空字段保持为空。

## Exploration log

当前 dirty main 中，Method §9.5 明确方向一致后报告均值，§9.7 仅将 Negative LQC 标为 QC FAIL。引擎已返回 LqcDirection，但未返回组均值；Application 的 LqcMeanOf 执行平均，而且 direction/mean 均以 passed 条件隐藏。现有 DTO 与 SQLite 已有 nullable LqcMeanDrPrime，故无需新数据库迁移或历史回算。

排除通过修改阈值、改组判读规则或读取时补算来修复：这些都会改变既有科学含义或历史证据。采用引擎输出字段加直接持久化的窄修，保留现有均值计算原语及精度。

## Root cause

早期适配层补做了组平均，并将可展示的计算证据与可供后续样本使用的 WT 参考资格混用。

## Implementation

- `ControlQcResult` 新增尾部可选参数及只读 `LqcMeanDrPrime`。`BrafAnalysisEngine` 在单孔方向一致后调用既有 `AnalysisMath.Mean`；非有限结果仍返回结构化失败；冲突组不计算均值。
- Application 删除 `LqcMeanOf`，不再筛选或平均单孔值；直接映射引擎方向及均值。Negative 一致组保持 QC Fail / RunInvalid，但保留已计算方向及均值；混合组为 Invalid，方向和均值均为空。
- WT 参考仍只在控制阶段通过后持久化为正式后续输入，失败 Run 无法继续样本分析。
- 沿用现有 `PersistedControl`、`ControlQcResultDto`、`ControlSummaryDto` 和 SQLite nullable 字段。UI/报告从这些保存值读取。历史空值保持为空，没有迁移、读取时回算或历史覆盖。
- 保持 `BRAF-ENGINE-1.0`：单孔公式、阈值、判读顺序以及既有均值原语未改变；这次修复的是计算层归属及曾丢失的结果证据。缺失旧证据仍以 null 区分，不宣称旧记录曾保存新均值。

## Validation

串行运行以下命令，均使用锁定依赖的 `--no-restore`，运行时依赖成功编译：

| 命令 | 实际结果 |
|---|---|
| `dotnet test tests/FluoAxis.Analysis.Tests/FluoAxis.Analysis.Tests.csproj --no-restore --verbosity minimal` | 88/88 通过，0 失败、0 跳过 |
| `dotnet test tests/FluoAxis.Application.Tests/FluoAxis.Application.Tests.csproj --no-restore --verbosity minimal` | 93/93 通过，0 失败、0 跳过 |
| `dotnet test tests/FluoAxis.Infrastructure.Tests/FluoAxis.Infrastructure.Tests.csproj --no-restore --verbosity minimal` | 82/82 通过，0 失败、0 跳过 |
| `./build/Verify-RepositoryPolicy.ps1` | 372 个仓库文本文件通过 |
| 针对此修复已跟踪修改的 `git diff --check` | 通过 |

新增 7 个用例覆盖正/负一致组的手算均值、缺端点失败、Application 保留引擎指定均值及 null 而不重算、Negative 失败 Run 的真实 CSV → SQLite 重新打开 → 项目包往返、历史 null 均值不补填；另增强重复孔冲突均值为空的断言。

无独立性能基准：本修复不作性能声明。平均仅对至多 96 孔进行一次有界遍历；临时数值数组至多 96 个 double，没有线程、缓存或并发预算变化。项目包测试根由仓库定位，并使用测试类、进程号和 GUID，清理前检查批准根；不依赖 cwd，不修改 Raw RFU。

已有测试样式/nullable 警告仍存在，不阻断上述成功结果。最终跨功能全套与原生桌面验收由配对 FT007 记录，不能用本聚焦结果代替整体验收。

## Review outcomes

独立 `ui_review` 代理只读审查未发现阻塞问题，确认均值计算先经过方向一致性检查、Negative 组证据保留、WT 正式输入仍受 Pass 门控。自审检查了公式归属、旧记录兼容、失败状态、不可变原始信号、路径及内存预算；Application 内 `Average/Mean/Distance2D/Normalize` 调用搜索为空。

本任务未创建 PR，未触发 GitHub 外部审查；通过独立代理与手工清单完成本修复审查。状态为 reviewed，等待 FT007 最终集成记录收口。

## Follow-ups

主代理完成 FT007 最终构建、全套测试与桌面验收后补入集成结果。科学批准及真实仪器 Golden 不由此修复替代；历史缺失均值不可推导补填。
