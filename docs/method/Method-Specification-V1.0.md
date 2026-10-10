# FluoAxis Method Specification — BRAF V600E Research Method V1.0（实现基线草案）

| 项目 | 内容 |
|---|---|
| 文档状态 | Implementation draft（实现基线草案，未签字冻结） |
| 版本 | Method-Spec V1.0-draft-2 |
| 日期 | 2026-09-01 |
| 角色 | 把 `BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md` 的判读规则收敛为无歧义、可编码、可测试的实现契约；作为本仓库内所有代码与测试的科学真源 |
| 冻结依据 | 需求文档 V1.0（需求冻结基线）；签字冻结需实验负责人批准（待办） |
| 产品边界 | Research Use Only。不输出恶性/良性、治疗、预后、药物推荐、临床诊断结论 |

> 本文档不发明科学规则。所有规则直接来自需求文档 V1.0 的已确认结论（§2.3、§7、FR-CAL/FR-QC/FR-SAM）。凡需求文档留白且影响数值结果的字段，本文档显式标注 `provisional`（工程默认值），并列入 `Data-Contract-V1.0.md` 的待确认问题清单，等待实验负责人签字关闭。

---

## 1. 范围与对象

- 适用于 96 孔板（A1–H12）双通道（FAM、ROX）动力学荧光时间序列数据。
- 时间轴来自导入数据的实际采集时间（Elapsed Time），第一条有效采集记录映射为 t=0。
- 不假设行数、不假设采样间隔、不假设 90–120 min 窗口内的点数。
- 重复孔数量、角色孔数量不做硬性假设（Q-SCI-005 未决），按导入数据实际存在的孔位处理；缺孔处理见 §9。

## 2. 计算顺序（不可重排）

```text
Select endpoint/window points
  -> 100% Positive per-well plateau RR QC
  -> 100% Positive per-well Fm
  -> Run-level Fm per channel (mean of passing wells)
  -> NTC per-well Fb at endpoint
  -> NTC per-well full-course Y QC
  -> Run-level Fb per channel (mean of passing wells)
  -> WT per-well endpoint Y
  -> WT reference mean (FAM, ROX)
  -> LQC per-well dr' / call / replicate consistency
  -> Sample per-well dr' / call / replicate consistency
  -> immutable analysis summary and trace
```

## 3. 数据精度与比较

1. RFU 与归一化值内部统一使用 `double`（IEEE 754 binary64）。
2. 任何中间步骤不做显示级四舍五入。
3. 阈值比较使用原始 `double` 值；显示四舍五入仅为表现层行为。
4. `dr'` 判读运算符为 `>= 0.4`：`0.4` 本身归阳性侧。不使用 `Math.Round` 后比较。
5. `RR` 判读运算符为 `<= 1%`（0.01）：`RR == 0.01` 时 PASS。
6. 所有中间量与最终量必须先通过 `double.IsFinite` 检查；NaN/±Infinity 构成结构化 Calculation Error，不以异常形式逃逸出分析引擎。
7. 均值运算采用顺序求和（确定性归约），不引入随线程顺序变化的并行归约。
8. Analysis 只接受已验证、按 Elapsed 严格递增的输入；导入层拒绝重复或倒序时间，不通过静默排序修复源文件。该表述与 Data Contract 的时间校验契约一致。

## 4. 时间与端点

1. Endpoint = 120 min（Method 参数 `endpointMinutes = 120.0`）。
2. 端点容差 `endpointToleranceSeconds = 15`，单位为秒。该值是需求基线给出的 **provisional** 工程默认值（Q-SCI-003），须在首轮真实双通道数据验证后锁定 Method Version。
3. 端点候选采用闭区间 `|t − endpoint| <= tolerance`，默认即 `119 min 45 s <= t <= 120 min 15 s`。存在多个候选点时选择距 120 min 最近者；等距时选择较晚者作为确定性 tie-break。若无候选点，返回结构化 Invalid，错误代码 `MISSING_ENDPOINT`；不得插值、外推或使用窗口外的点。
4. 平台窗口 = 90–120 min，闭区间（`plateauStartMinutes = 90.0`，`plateauEndMinutes = 120.0`）。**provisional**（Q-SCI-004 部分）：窗口端点闭区间为工程默认，待确认。
5. 窗口内点数下限：**fail closed**。窗口内有效点数 < 2 时，该孔平台 QC 返回结构化 Invalid，错误代码 `INSUFFICIENT_WINDOW_POINTS`，不得单点判定 RR。**provisional**：点数下限 2 为工程默认（防单点 RR=0 假 PASS）；是否应为 3 待实验负责人确认。
6. 重复时间戳：同一孔同一通道存在两个相同 elapsed 值时，数据在导入层拒绝（`IMPORT_DUPLICATE_TIME`）；分析引擎不接收含重复时间的输入。
7. 非等间隔时间：不要求均匀采样，按每个实际点计算，不插值。
8. **四点采集 vs 全时程（Q-SCI-001）**：生产流程文档写仪器在 2/5/30/120 min 四点扫描，需求文档要求 NTC 0–120 min 全时程逐点 QC。本规范的规则不依赖点数——一律按数据实际存在的点计算。四点数据照常逐点检查（每孔 4 点，全部必须满足 −0.05 ≤ Y ≤ +0.05）。此矛盾本身仍需实验负责人裁决，裁决不影响本规范的计算规则。

## 5. Calibration — 100% Positive（Fm）

对每个 100% Positive 孔、每个通道独立：

1. 取该孔该通道 `90 ≤ t ≤ 120` min 窗口内全部有效点。
2. `RR = (max − min) / mean × 100%`。
3. `RR ≤ 1%` → 该孔该通道平台 QC PASS。
4. PASS 后 `Fm_i = mean(窗口内全部点)`。
5. 任一关键 Positive 重复孔任一通道失败 → 整个 Calibration 失败；不剔除该孔、不静默替换。
6. 所有 Positive 孔 PASS 后：`Fm_run = mean(Fm_i)`（per channel）。
7. 防御性条件：窗口空或点数 < 2 → Invalid（§4.5）；`mean ≤ 0` → Invalid（`NON_POSITIVE_MEAN`）；非有限值 → Calculation Error。

## 6. 分母校验（fail closed）

1. NTC per-well QC 分母 = `Fm_run(该通道) − Fb_i(该孔自身)`。要求 > 0 且有限，否则该孔返回 Calculation Error → Calibration 失败。
2. WT/LQC/Sample 归一化分母 = `Fm_run − Fb_run`（per channel）。要求 > 0 且有限，否则对应阶段返回 Calculation Error → Run Invalid。
3. 分母检查发生在除法之前；绝不除后继续。
4. Run 级分母检查在 Run 级 Fm/Fb 均就绪后立即执行（位于 WT 计算之前）。
5. **provisional**（Q-SCI-007 未决）：本版仅要求分母 > 0 且有限，不设绝对数值下限（ε）。任何更严的下限必须通过 Method 版本变更引入。

## 7. Calibration — NTC（Fb）

对每个 NTC 孔、每个通道独立：

1. `Fb_i = 该孔该通道的 120 min 端点值`（端点选择规则见 §4.3）。
2. 在获得 Fm_run 后，对该孔 `0 ≤ t ≤ 120` min 全部有效点计算 `Y(t) = (F(t) − Fb_i) / (Fm_run − Fb_i)`。
3. **每个 NTC 孔使用自身 Fb_i 归一化做 QC**；不得先对 NTC 求均值再检验。
4. 要求全部点的 `−0.05 ≤ Y(t) ≤ +0.05`。**provisional**：闭区间为工程默认（区间端点 ±0.05 本身算 PASS），开闭待确认。
5. 任一点超界 → 该孔该通道 QC FAIL → Calibration 失败。
6. 全部 NTC 孔 PASS 后，`Fb_run = mean(Fb_i)`（per channel）。
7. NTC 孔内有效点数为 0 → 该孔 Invalid。
8. **NTC ≠ WT**：NTC 是空白（无模板）背景校准孔，Fb 来源；WT 是野生型 DNA 阴性对照，提供 dr' 参考点与阴性质控。二者角色不可互换。

## 8. WT / Negative Control

对每个 WT 孔、每通道独立计算端点归一化值 `Y_FAM_i`、`Y_ROX_i`，使用 Run 级 Fm/Fb（分母 `Fm_run − Fb_run`）。

1. WT 通过条件：`mean(Y_FAM_WT) < 0.05` **且** `mean(Y_ROX_WT) < 0.05`（严格小于，排他阈值）。
2. WT 参考坐标 = `(mean(Y_FAM,WT), mean(Y_ROX,WT))`，是 LQC/Sample dr' 的参考点。
3. WT FAIL → Run Invalid，阻断正式 Sample 判读。
4. **provisional**（Q-SCI-006 未决）：需求文档只定义组均值判据；本版不添加单孔范围规则或 WT 重复孔一致性规则（不加戏）。

## 9. LQC / Sample 的 dr' 与重复孔一致性

对每个 LQC/Sample 组的每个孔独立：

1. `Y_FAM_i = (F_FAM_i − Fb_run_FAM) / (Fm_run_FAM − Fb_run_FAM)`，ROX 同理（端点值代入）。
2. `dr'_i = sqrt((Y_FAM_i − meanY_FAM_WT)^2 + (Y_ROX_i − meanY_ROX_WT)^2)`。
3. **单孔分类先于组均值**：`dr'_i >= 0.4` → Positive；`dr'_i < 0.4` → Negative。
4. 重复孔方向一致性：全 Positive 或全 Negative → PASS；混合 → `REPLICATE_CLASSIFICATION_INCONSISTENCY` → INVALID，不取均值、不给最终阳/阴。
5. 方向一致后报告 `meanDrPrime = mean(dr'_i)`（展示与存档用途；不作为判读依据）。
6. Sample 判读仅来自单孔方向一致性，绝不基于 mean dr' 判读。
7. **provisional**（Q-SCI-008 未决）：LQC"弱阳性预期"本版实现为：LQC 组分类方向须为 Positive（0.1% LQC 应落于阳性侧）；组方向为 Negative 时 LQC QC FAIL → Run Invalid。是否附加 mean dr' 上限范围由实验负责人确认后以 Method 版本变更引入。
8. 防回归测试要求：构造「单孔方向一致但 mean dr' 落在阈值另一侧」的用例（如三孔 dr' = 0.39/0.39/0.42 → 三孔全 Negative？不——0.42 ≥ 0.4 为 Positive，方向混合为 INVALID）。正确构造：三孔 dr' = 0.39/0.39/0.39 → 全 Negative，mean = 0.39 < 0.4，两侧一致（无法产生分歧）；产生分歧需要均值越阈但单孔一致，如 0.39/0.39/0.39 不足以越阈。真正的分歧用例：**不存在的数学情形**（若全部 < 0.4 则 mean < 0.4；若全部 ≥ 0.4 则 mean ≥ 0.4）——单调性保证两者不可能分居两侧。因此防回归测试的真正价值在于锁定该单调性：断言「全部单孔 Negative ⇒ mean < 0.4；全部单孔 Positive ⇒ mean ≥ 0.4」恒成立，防止未来实现改用 mean 判读后掩盖单孔信息。

## 10. 缺孔与角色基数

**provisional**（Q-SCI-005 未决）：

1. NTC、100% Positive、WT、LQC 每种角色至少 1 孔，缺失 → 结构化 Invalid（`PLATE_ROLE_CARDINALITY`），Run 停留在对应前置状态。更高最小孔数要求（如各至少 2–3 重复）待实验负责人确认后引入。
2. Sample 组孔数 = 0 的组跳过（不产生结果对象）；孔数 ≥ 1 即参与分析。
3. 同一 Run 内同一 Well 只能持有一个角色（互斥）。
4. 缺孔处理 fail closed：不替补、不插值、不降级。
5. 弃用孔 / 重分配 / 排除属 UI 与审计层操作；引擎只消费已确认的映射。

## 11. 结果词汇与状态

1. 正式判读结果词汇：`BRAF V600E 阳性` / `BRAF V600E 阴性`（以 Method 参数 `positiveLabel` / `negativeLabel` 提供，不散落 UI 字符串）。
2. PASS/FAIL/INVALID 用于 QC 与 Run 状态。
3. Run 状态机（NEW → IMPORTED → MAPPED → CALIBRATING → CALIBRATION_FAILED → RUN_INVALID / CALIBRATION_PASSED → CONTROL_QC → … → ANALYZED → EXPORTED）由 Application 层管理；UI 不修改状态。
4. 禁止输出词汇（UI/报告层硬约束）：恶性、良性、治疗、预后、药物推荐及对应英文。

## 12. Golden Dataset 建立原则

1. 基础九类 GD-001~GD-009（Positive、Negative、NTC Fail、Plateau Fail、WT Fail、LQC Fail、Replicate conflict、Missing endpoint、Fm ≤ Fb）+ 扩展边界 GD-010~GD-022（见 plan.md §6.4）。
2. 期望值由独立参考计算产生（独立脚本/手工计算），不复用生产 Analysis Engine。
3. GD-010（dr' 位于 0.4 阈值）：WT = (0, 0)，Y = (0.4, 0)。十进制 0.4 不能由 binary64 精确表示；边界测试验证引擎得到的 binary64 距离值与 Method 解析出的同一阈值值相等，并验证 `>=` 的包含性，不宣称实数 0.4 被精确存储。
4. GD-011（RR 位于 1% 阈值）：使用窗口值 {199, 201}，使 binary64 运算路径为 `(201−199)/200 = 2/200`。整数差与均值可精确表示，最终除法得到的 binary64 值应与 Method 中 `0.01` 解析出的阈值值相等。测试用运行时值相等和 `<=` PASS 共同验证包含性；不宣称十进制 0.01 可由 binary64 精确表示。
5. 真实数据 fixture 必须去标识化；不可去标识化时不进入 Git。

## 13. 仪器与数据背景（信息性，非规则）

- 仪器：BioTek Cytation 5（Agilent），Gen5 软件导出宽表；激发/发射 485/525 nm（FAM）、585/616 nm（ROX）。
- 手稿协议：每 2 min 读一次，持续 120 min（61 点），37 °C 孵育；生产流程文档版本为恒温 42 °C、2/5/30/120 min 四点采集。两者都是本软件可接受的输入形态（规则按实际存在的点计算）。
- dr' 的科学来源：手稿定义的 2D 坐标（FAM/ROX 归一化产率）中 SNV 样本与 WT 参考点的欧氏距离；0.4 阈值来自 25 个 NAT 阴性样本的 ROC 分析（手稿 Fig. 3：dr > 0.4 阴影区为最优特异性范围）。
- 本软件实现的是软件侧判读规则；0.4 阈值的科学验证状态属于科研侧证据链（Q-SCI-009），不在本规范范围内下结论。

## 14. 待实验负责人确认的 provisional 清单

| ID | provisional 内容 | 当前默认 | 影响面 |
|---|---|---|---|
| P-01 | endpointToleranceSeconds 默认值 | 15 s；双侧闭区间；最近点；等距取较晚点 | 默认值待真实仪器数据验证；单位和选择语义按需求基线实现 |
| P-02 | 平台窗口闭区间 | 闭区间 | RR 点集 |
| P-03 | 平台窗口最小点数 | 2 | INSUFFICIENT_WINDOW_POINTS |
| P-04 | NTC Y 区间开闭 | 闭区间 | NTC QC |
| P-05 | 各角色最小孔数 | 各 1 孔 | PLATE_ROLE_CARDINALITY |
| P-06 | Fm−Fb 分母绝对下限 | 无（仅 > 0 且有限） | INVALID_DENOMINATOR |
| P-07 | LQC 弱阳性判据 | 组方向须为 Positive | LQC QC |
| P-08 | 四点 vs 全时程裁决 | 按实际点数计算（两者兼容） | 无数值影响 |
| P-09 | WT 单孔规则 | 无（仅组均值） | WT QC |

---

## 附：Method JSON Schema（V1.0 实现用）

```json
{
  "id": "BRAF-V600E-Research",
  "version": "1.0",
  "endpointMinutes": 120.0,
  "endpointToleranceSeconds": 15.0,
  "plateau": {
    "startMinutes": 90.0,
    "endMinutes": 120.0,
    "relativeRangeMax": 0.01,
    "minWindowPoints": 2
  },
  "ntc": {
    "normalizedMin": -0.05,
    "normalizedMax": 0.05
  },
  "wt": {
    "famMeanMaxExclusive": 0.05,
    "roxMeanMaxExclusive": 0.05
  },
  "classification": {
    "drPrimeThresholdInclusive": 0.4,
    "positiveLabel": "BRAF V600E 阳性",
    "negativeLabel": "BRAF V600E 阴性"
  },
  "replicates": {
    "requireClassificationConsistency": true
  },
  "roles": {
    "minimumWells": {
      "ntc": 1,
      "positive100": 1,
      "wt": 1,
      "lqc01Percent": 1
    }
  }
}
```

标记 `minWindowPoints` 与 `roles.minimumWells` 为 provisional（P-03/P-05）；其余字段与需求文档 V1.0 §43 一致。
