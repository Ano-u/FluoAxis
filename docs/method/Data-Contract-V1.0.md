# FluoAxis Data Contract V1.0（数据契约与未决科学问题）

| 项目 | 内容 |
|---|---|
| 文档状态 | Implementation draft |
| 版本 | Data-Contract V1.0-draft-1 |
| 日期 | 2026-09-01 |
| 依据 | 需求文档 V1.0 §2.2/§2.3、手稿、生产流程文档 |
| 关联 | `docs/method/Method-Specification-V1.0.md`（计算规则真源） |

## 1. 输入数据形态

### 1.1 仪器背景（信息性）

- 仪器：BioTek Cytation 5（Gen5 导出），96 孔板，恒温孵育，FAM/ROX 双通道。
- 手稿协议采样：每 2 min 一读，0–120 min，约 61 点/通道。
- 生产流程文档采样：2/5/30/120 min 四点（Q-SCI-001 待裁决；计算规则对两者兼容，见 Method-Spec §4.8）。

### 1.2 外部文件格式

- 首发支持：XLSX（Open XML 流式读取）、CSV、TSV、TXT。XLS 通过隔离适配层后置。
- 宽表逻辑结构：`Time | (Temperature) | A1 | A2 | ... | H12`，双通道可位于不同 Sheet 或同 Sheet 不同区域。
- 顶部可能有仪器元数据行；不得假设第一行是表头（FR-IMP-005）。
- CSV/TSV/TXT：UTF-8、UTF-8 BOM 必须支持；其他编码需用户显式选择；分隔符 comma/tab/semicolon 可配置；数值解析默认 InvariantCulture。
- 导入时保存源文件 SHA-256（FR-IMP-010）。

### 1.3 内部长表模型（Domain 契约）

```text
SignalPoint: runId, channel(FAM|ROX), wellId(A1..H12), pointIndex,
             elapsed(TimeSpan/ticks), rfu(double)
```

- 原仪器宽表只存在于 import adapter 内；Domain/Analysis 不依赖 Sheet 名或 Excel 列位置。
- 每孔每通道的序列按 elapsed 升序、无重复时间戳（导入层保证）。
- 第一条有效采集记录映射 t=0（FR-TIME-002）。

## 2. 未决科学问题状态表（Q-SCI）

依据 plan.md §2.1；本仓库现有材料给出的新证据与当前处理方式：

| ID | 问题 | 现有证据 | 当前实现处理 | 状态 |
|---|---|---|---|---|
| Q-SCI-001 | 四点采集 vs 全时程 NTC QC | 生产流程文档写 2/5/30/120 四点；手稿写每 2 min 全程 | 规则按实际存在的点计算，两者兼容；见 Method-Spec §4.8 | 裁决待用户确认；不阻断兼容性基础设施，但阻断 G0、正式 Method 冻结及 Analysis Core 完成声明 |
| Q-SCI-002 | 双通道文件布局 | 需求文档 §2.2：FAM/ROX 两个数据区或工作表；无真实 fixture | 用户授权明确格式/方案自动识别提交；有歧义保留人工确认（FR-IMP-006/007） | 工程识别已有合成回归；真实 fixture 待收集 |
| Q-SCI-003 | endpoint 容差 15 s | 需求文档 FR-TIME-005~007、§43 明确字段单位为秒并要求小范围容差、无合法端点则 Invalid | 双侧闭区间 ±15 s；选最近点；等距取较晚点；不插值。默认值待真实数据验证后锁定 | 默认值待签字 |
| Q-SCI-004 | 平台窗口最小点数/开闭 | 无 | provisional P-02/P-03：闭区间、≥2 点 fail-closed | 待签字 |
| Q-SCI-005 | 各角色重复孔数 | 生产流程文档：NTC/阳性/0.1%/阴性各 1 管试剂（每孔 5 μL），暗示初期各 1 孔/组 | provisional P-05：各 ≥1 孔 | 待签字 |
| Q-SCI-006 | WT 单孔规则 | 无 | provisional P-09：仅组均值 | 待签字 |
| Q-SCI-007 | Fm−Fb 分母下限 | 无 | provisional P-06：> 0 且有限 | 待签字 |
| Q-SCI-008 | LQC 0.1% 判据 | 手稿：0.1% VAF 可区分（Fig. 5c）；生产流程：dr' > 阈值 | provisional P-07：组方向须 Positive | 待签字 |
| Q-SCI-009 | 0.4 阈值来源 | 手稿：25 NAT 阴性样本 ROC 分析建立，117 临床样本验证（ddPCR 基准，敏感性/特异性 100%） | 软件按已确认规则实现；科学验证属科研侧证据链 | 已有论文支撑；签字留档待用户 |
| Q-SCI-010 | 样本基质/仪器边界 | 手稿：冷冻组织、FFPE、FNAB（gDNA 0.2–50 ng）；仪器 Cytation 5 | 记录为 Research Intended Use 输入 | 待用户确认范围 |
| Q-SCI-011 | 重复孔冲突处置 | 需求文档 §2.3：INVALID + 建议重做 | 已实现为 INVALID（非工程未知项） | 规则已冻结 |
| Q-SCI-012 | 公式单元格/空值/饱和/负 RFU | 无专门材料 | 导入层：公式单元格读缓存值；空值/非数值→拒绝该点或该文件（fail closed，见导入契约 §3） | 待真实数据验证 |

## 3. 导入层安全规则（工程契约）

1. 任意输入文件视为不可信：大小、行数、列数、解压尺寸全部设上限。
2. XLSX 使用 OpenXmlReader 流式读取，不做全工作簿 DOM 展开。
3. 不执行公式、宏、外部链接；公式单元格只读缓存文本值。
4. 空值不自动转 0；非有限数值（NaN/Inf）拒绝。
5. 重复时间戳、时间倒序 → 导入错误（不静默修复）。
6. FAM/ROX 孔集合不匹配 → 映射验证错误，用户确认后才能导入。
7. 导入可取消；取消不产生半成品 Run。
8. 导入产物：内部长表 + 源文件 SHA-256 + 相对资源引用（原始文件位置不得持久化）。

### 3.1 自动识别与固定孔位（工程扩展）

用户已确认初始方案 A1=100% Positive、A12=NTC、H1=WT、H12=LQC、B2–G11=可选样本、其余 Ignore。这项操作方案不批准 Method 科学参数。默认方案可修改，只在尚无保存方案时初始化。

自动识别依赖明确 FAM/ROX 标签、合法孔号及时间单位；无单位时可使用用户明确保存的解析设置，文件明示值与设置冲突则停止。角色来自显式文件标记或当前选择的方案，绝不从 RFU 推断。只有孔号的文件为每个样本孔使用本次孔号 ID 和 replicate 1；完整文件 SampleId/Replicate 保留，不复制历史样本元数据。部分显式样本信息、角色冲突、缺关键对照或识别超预算均需复核。所有完整数据仍经原导入/Gateway/Analysis 校验；取消后保留已原子提交阶段，未提交阶段不产生结果。

识别建议只读，不改变 Run；自动提交为用户授权的工程操作，仍保存同样的 source/mapping/assignment 身份。人工预览、确认设置后继续和新运行重新设置都保留。合同实现及回归记录见 `docs/feature/FT-20261008-004-assisted-import-analysis.md`。

## 4. 测试临时目录策略（工程契约）

- 所有测试临时文件使用仓库内唯一根：`artifacts/test-tmp/<test-project>/<process-or-worker>/<test-name>/<unique-id>`。
- 测试根必须从测试程序集位置向上发现仓库，不得依赖进程当前工作目录；递归清理前必须验证目标仍位于批准的 `artifacts/test-tmp` 子树内。
- 禁止依赖具体开发者机器路径；禁止跨测试共享固定目录名。
- CI 中的路径泄漏扫描覆盖测试产物。
