# Golden Dataset 设计文档（Wave 3 前置）

| 项目 | 内容 |
|---|---|
| 状态 | Candidate implemented（未获科研/工程双签，不是 approved Golden） |
| 依据 | plan.md §6.4、需求文档 §33.2、Method-Specification §12 |
| 核心原则 | **期望值由独立参考计算产生，不复用生产 Analysis Engine** |

## 1. 独立参考计算器（Reference Calculator）

`tests/FluoAxis.Golden.Tests/Reference/IndependentReferenceCalculator.cs`

- 用**与生产引擎不同的实现方式**编写：只接收自身的标量/数组模型，不引用 `FluoAxis.Analysis`、`FluoAxis.Domain`、`BrafAnalysisEngine` 或 `AnalysisMath`。
- 覆盖：Fm（窗口均值）、RR、Fb（端点值）、NTC Y 序列、WT 均值、dr'、重复孔一致性。
- 该参考实现简单直接（≈100 行），不追求性能，只追求"第二双眼睛"。

## 2. 当前候选证据格式

`samples/golden/candidate-manifest-v1.json` 记录候选案例 ID、Method 身份与 canonical hash，并强制声明 `approvalStatus: candidate-unapproved`。可人工推导的 Raw RFU 值目前位于 `GoldenCandidateTests.cs`，由测试适配器分别送入独立 oracle 与生产引擎。

当前实现没有把代码内候选伪装成已批准的逐案例外部 fixture。待科研与工程负责人完成复核后，再将签字期望值固化为每案例 JSON；下面是该批准后格式的目标草案：

每个 case 一个 JSON 文件：

```json
{
  "caseId": "GD-001",
  "description": "标准阳性样本",
  "methodSnapshotHash": "…",
  "wells": [
    {
      "well": "A1", "role": "Ntc",
      "fam": { "times": [0,2,4], "rfu": [120.0, 121.0, 122.0] },
      "rox": { "times": [0,2,4], "rfu": [80.0, 81.0, 82.0] }
    }
  ],
  "expected": {
    "calibration": { "status": "PASS", "fmFam": 900.0, "fbFam": 122.0 },
    "wt": { "status": "PASS", "yFamMean": 0.01 },
    "samples": [ { "sampleId": "S1", "call": "Positive", "perWell": [ { "drPrime": 0.51, "call": "Positive" } ] } ]
  }
}
```

- 批准后的期望值字段必须由独立参考计算器计算并**人工校对**写入 fixture；测试运行时生产引擎输出与 fixture 比对（全字段、含中间量）。
- 数值比较：确定性计算默认 exact 相等；只有 fixture 本身声明容差的字段才用容差。

## 3. Case 清单（22 个）

| ID | 场景 | 关键断言 |
|---|---|---|
| GD-001 | 全链路阳性 | Candidate test：每孔 dr'、Fm/Fb、最终 Positive |
| GD-002 | 全链路阴性 | 同上，Negative |
| GD-003 | NTC FAIL | NTC 超界 → Calibration FAIL → Run Invalid |
| GD-004 | Positive plateau FAIL | RR > 1% |
| GD-005 | WT FAIL | 均值 ≥ 0.05 |
| GD-006 | LQC FAIL | 方向为阴 |
| GD-007 | Replicate conflict | 两阳一阴 → INVALID |
| GD-008 | Missing endpoint | 无 `120 min ± 15 s` 候选点 → MISSING_ENDPOINT |
| GD-009 | Fm ≤ Fb | INVALID_DENOMINATOR |
| GD-010 | 计算所得 binary64 dr' 与 Method 的 0.4 阈值值相等 | Positive（`>=`，0.4 归阳性侧） |
| GD-011 | 计算所得 binary64 RR 与 Method 的 1% 阈值值相等 | PASS（`<=`） |
| GD-012 | plateau 单点窗口 | INSUFFICIENT_WINDOW_POINTS |
| GD-013 | duplicate time | 导入层拒绝（fixture 为分析层输入前的拒绝测试） |
| GD-014 | 非单调时间 | 同上 |
| GD-014b | 输入行序打乱 | 排序后结果不变 |
| GD-015 | 缺一通道 | 通道缺失错误 |
| GD-016 | NaN/Inf 输入 | Analysis 层拒绝 |
| GD-017 | 负 RFU | 允许存在（Raw 值），计算照常（见 Method-Spec：无禁负规则） |
| GD-018 | 端点恰在容差内 (119 min 45 s) | 被接受 |
| GD-019 | 端点恰在容差外 (119 min 44.9 s) | MISSING_ENDPOINT |
| GD-020 | 仿射尺度不变性 | 全部 RFU ×10 后归一化量与 dr' 不变 |
| GD-021 | save/open 往返 | （ProjectFormat 波次接入后启用） |
| GD-022 | Method 快照不可变 | （Application 波次后启用） |

GD-013/014 为导入层拒绝语义的回归；GD-021/022 挂起至对应波次。

当前已执行 candidate cases：GD-001～012（不含尚属 Import 专项的 013～017）、GD-018～020，共 15 个 ID、16 个测试实例。GD-013～017、GD-021～022 仍未实现于 Golden 工程，不得从清单存在推断覆盖。

## 4. 期望值生成流程

1. 手工构造场景（故意选择数学上整洁的数字，使期望值可心算/手工推导）。
2. 用独立参考计算器验证手算期望。
3. Candidate 阶段：生产引擎与独立 oracle 对同一手工向量逐中间量比较。
4. 审批阶段：科研与工程负责人复核期望值、Method hash 和边界语义并双签。
5. 批准后：将期望值写入逐案例 fixture JSON，并对生产引擎做字段级回归。
6. 随机差分和真实仪器文件验证属于后续增强；当前 16 个测试不是随机差分，也不证明外部有效性。
