---
id: FX-20261007-002
kind: fix
status: validated
area: deterministic-analysis
stage: analysis-core
tasks: [endpoint-range-safety, non-finite-group-mean, engine-regression]
priority: P0
branch: main
pr: null
paired_doc: docs/feature/FT-20261006-001-software-design-closure.md
paths:
  - src/FluoAxis.Analysis/AnalysisMath.cs
  - src/FluoAxis.Analysis/BrafAnalysisEngine.cs
  - src/FluoAxis.Domain/BrafMethodSnapshot.cs
  - tests/FluoAxis.Analysis.Tests/AnalysisMathTests.cs
  - tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs
  - tests/FluoAxis.Domain.Tests/BrafMethodSnapshotTests.cs
  - docs/fix/FX-20261007-002-analysis-numeric-fail-closed.md
contracts: [structured-numeric-failure, unchanged-scientific-thresholds, timespan-representability]
hotspots: [tick-subtraction-overflow, nullable-mean-dereference, binary64-sum-overflow]
bug:
  repro: WT finite normalized values can overflow the sequential sum and dereference a null Mean result; endpoint signed tick difference can reach long.MinValue
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [analysis-unit-regression, domain-range-tests, golden-candidates, full-checkout-tests, policy-gates]
  tests_run:
    - "FT-20261007-003 integrated validation: dotnet test FluoAxis.slnx --no-restore; 699 passed, including numeric boundary and Golden candidate regressions"
    - "FT-20261007-003 integrated validation: solution build; 0 warnings, 0 errors"
    - "FT-20261007-003 integrated validation: repository policy and scientific source boundary passed"
  benchmarks_run: ["No numeric-fix benchmark required; sequential means and linear endpoint scanning retain their existing complexity"]
search:
  aliases: [WT mean overflow, endpoint ticks overflow, numeric fail closed]
---

# 保证极端数值返回结构化计算失败

## Summary

处理端点 tick 差值与 WT 组均值溢出，避免不可用数值以异常或不可信 QC 结果逃逸。判读阈值、窗口和聚合顺序保持当前 Method 语义。

## Plan

核验代理增量，增加直接触发 Engine 失败路径的回归，运行聚焦和全解决方案验证，保留科学审批缺口。

## Exploration log

- 独立审查发现 `Math.Abs(pointTicks - endpointTicks)` 存在 signed overflow 风险。
- WT mean 代码使用 `Mean(...)!.Value`；Mean 在顺序和非有限时返回 null，无法被 null-forgiving 运算符保护。
- 代理在设计任务 stub 下提交防御性增量；本记录把算法修复独立分类，并补充引擎级复现证据。
- Sample dr' mean 也增加防御性有限性检查；标准 96 孔板中距离先受平方有限性约束，均值溢出通常不可达，不能夸大为已证实的常规缺陷。
- 不采用改变顺序求和算法、增加 epsilon 或放宽 QC 的方案：这些会改变既有科学数值契约。

## Root cause

有限单孔值不保证顺序求和有限；可空 Mean 结果需在解引用前显式校验。时间浮点参数有限也不保证可表示为 TimeSpan。

## Implementation

端点使用无溢出的 tick 距离，异常 Method 时间返回结构化错误；构造时拒绝不可表示时间。WT/Sample 均值不可用时返回 `CalibrationNonFiniteValue`。

## Validation

上一轮 fresh 验证已整合记录在 `docs/feature/FT-20261007-003-software-design-completion-audit.md`：solution 699 项测试通过，包含 Analysis、Domain、Golden candidate 与数值边界回归；solution build 为 0 warnings / 0 errors；repository policy 与 scientific source boundary 通过。测试覆盖端点 tick 距离极值、非法 Method 时间可表示性和 WT 组顺序求和溢出；没有改变 `dr′ >= 0.4`、平台 `RR <= 0.01` 或顺序聚合规则。Golden 仍为 candidate-unapproved，测试通过不是科学批准。

## Review outcomes

独立算法 concern 已复核输入域、端点闭区间、分母先验检查、有限均值和异常映射。保留对 Sample mean 分支可达性的限制说明，不夸大为常规 96 孔板已发生故障。外部 GitHub/Codex review 未触发；当前状态为工程 `validated`。

## Follow-ups

Method init-property 可绕过构造验证的完整治理以及科学审批属于后续独立契约任务；本修复只防护明确计算边界。输入角色、双通道身份/时间轴、Gateway failure path 和持久化 Method 一致性的后续防御由 FT-20261007-003 整合记录；持久证据查询的新范围由 FT-20261007-005 承接。
