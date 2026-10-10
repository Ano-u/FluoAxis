---
title: "FluoAxis - BRAF V600E 双通道荧光数据分析与科研判读软件"
subtitle: "软件需求规格、设计原理与现代化实现架构 V1.0"
author: "项目需求整理"
date: "2026-08-10"
lang: zh-CN
---

# 文档控制

| 项目 | 内容 |
|---|---|
| 文档名称 | FluoAxis - BRAF V600E 双通道荧光数据分析与科研判读软件 - 软件需求规格、设计原理与现代化实现架构 |
| 文档版本 | V1.0 |
| 状态 | 需求冻结基线 / 可进入原型与开发 |
| 编制日期 | 2026-08-10 |
| 产品名称 | [FluoAxis](https://github.com/Ano-u/FluoAxis) |
| 规范仓库 | [Ano-u/FluoAxis](https://github.com/Ano-u/FluoAxis) |
| 第一阶段产品定位 | Windows 科研用途数据分析与判读软件，不作为正式临床诊断软件发布 |
| 核心对象 | 双通道 FAM / ROX 动力学荧光时间序列、质控、归一化、dr' 判读、二维与三维可视化 |
| 主要输入 | 仪器导出的 Excel / CSV / TXT 等宽表数据 |
| 主要输出 | 工程文件、Excel、PDF Research Report、PNG、SVG、可选交互式 HTML |

> 本文档把前期讨论、用户提供的科研材料和最终意见汇总收敛为一份可直接用于产品设计、软件开发、测试与验收的技术基线。实验判读规则和工程技术选型在文中明确区分：前者以已确认规则为准；后者采用当前稳定、现代、可维护的 Windows 技术栈作为默认实现。

# 1. 执行摘要

**FluoAxis** 不是简单的 Excel 计算器，也不是只完成一次 BRAF V600E 公式运算的小工具。产品应被定义为一套 **Windows 原生科研数据分析工作站**，包含两个互相隔离但共享底层数据模型的工作区：

1. **Guided Analysis**：把 BRAF V600E 的实验数据处理、Calibration、质控、重复孔一致性、标准化、dr' 计算和最终阳性/阴性判读固化成可复现 Method。
2. **Data Explorer**：提供类似 Origin 的自由科研数据能力，包括拖入表格、手工数据、二维散点/折线、三维笛卡尔坐标图、低失真平滑、公式计算、两点距离、基础统计和图形导出。

第一阶段的正式科研判读结果只输出：

- `BRAF V600E 阳性`：`dr' >= 0.4`
- `BRAF V600E 阴性`：`dr' < 0.4`

不设置灰区，不根据软件结果直接输出“良性/恶性”，不做自动动力学强判据，不把第一阶段软件包装为正式临床诊断软件。

软件底层仍按未来产品化要求设计：原始数据只读、关键计算可追溯、Method 版本化、工程文件可复现、关键依赖锁定版本、分析核心与 UI 解耦、Golden Dataset 回归测试、崩溃恢复和完整错误状态。

# 2. 需求来源与已确认结论

## 2.1 科研原理来源

用户提供的科研材料表明，该方法通过双荧光通道同时表征不同反应信息，并定义新的参数用于分析性能表征。前期汇报材料明确包含：

- 同时监测中间体变化和催化产物累积；
- 整合双荧光通道；
- 定义 `dr'` 参数；
- 对 BRAF V600E 低丰度检测进行验证；
- 方法思想具备向 KRAS、EGFR 等其他 SNV 扩展的空间。

因此软件架构不能把 BRAF V600E 写死在 UI 或存储层，而应采用 **通用 Analysis Engine + Method Definition** 的结构。

## 2.2 已确认的数据格式

当前实验文件属于“宽表”形式。典型逻辑结构为：

```text
Time / ElapsedTime | Temperature(optional) | A1 | A2 | A3 | ... | H12
0                  | ...                   | .. | .. | .. | ... | ...
1                  | ...                   | .. | .. | .. | ... | ...
2                  | ...                   | .. | .. | .. | ... | ...
...
120                | ...                   | .. | .. | .. | ... | ...
```

双通道情况下，Excel 中存在 FAM 和 ROX 两个数据区域或工作表。第一版应采用：

> 自动发现候选数据区 + 用户最终确认 Time、FAM、ROX 与孔列映射。

程序不得假设“第一行必然是表头”，因为真实仪器文件顶部可能有仪器参数、方法信息、实验设置和其他分析结果。

## 2.3 已确认的关键判读规则

### Calibration

- `Fb` 只来自 NTC，不来自 WT/阴性质控。
- 100% Positive 提供 `Fm`。
- NTC 和 WT/阴性质控不是同一个概念。
- NTC 各重复孔必须先独立运算、独立使用自身 `Fb` 完成 NTC 归一化 QC，通过后才允许求均值生成 Run 级 `Fb`。
- 关键校准重复孔出现异常，按严格规则处理，不静默剔除。

### NTC

每个 NTC 孔在 120 min 取得本孔 `Fb`。在获得 `Fm` 后，将该 NTC 孔 0-120 min 的数据代入：

$$
Y(t)=\frac{F(t)-F_b}{F_m-F_b}
$$

要求该孔、该通道全时程：

$$
-0.05 \le Y(t) \le 0.05
$$

才视为 NTC 合格。

### 100% Positive / Fm

2026-10-10 用户修订（Method 1.1 草案）：以下平台逻辑仅针对 **FAM**。ROX 单孔 Fm 直接取本阳性孔的 **120 min 实际终点荧光强度**，不使用平台均值或平台 RR 判定；多阳性孔按各自 ROX 端点求 Run 均值。旧 Method 1.0 继续按原双通道平台规则复现。具体端点/追溯规则见 `docs/method/Method-Specification-V1.1.md`。

FAM 平台窗口为 90-120 min。使用窗口内实际存在的所有采集点，计算相对极差：

$$
RR=\frac{F_{max}-F_{min}}{\bar F}\times 100\%
$$

平台合格条件：

$$
RR \le 1\%
$$

单孔 `Fm`：

$$
F_{m,i}=mean(F_{i,90-120})
$$

全部关键重复孔通过后，计算 Run 级 `Fm` 均值。

### WT / Negative Control

Calibration 完成后，对 WT 重复孔分别计算 120 min 的归一化值，再求两个通道的组均值：

$$
\bar Y_{FAM,WT}=mean(Y_{FAM,WT1},...,Y_{FAM,WTn})
$$

$$
\bar Y_{ROX,WT}=mean(Y_{ROX,WT1},...,Y_{ROX,WTn})
$$

阴性质控通过条件：

$$
\bar Y_{FAM,WT}<0.05
$$

且

$$
\bar Y_{ROX,WT}<0.05
$$

### LQC 0.1%

每个 LQC 孔独立计算：

$$
dr'_i=\sqrt{(Y_{FAM,i}-\bar Y_{FAM,WT})^2+(Y_{ROX,i}-\bar Y_{ROX,WT})^2}
$$

单孔分类：

- `dr'_i >= 0.4` -> Positive
- `dr'_i < 0.4` -> Negative

关键重复孔方向必须一致；一致后再报告平均 `dr'`。0.1% LQC 应满足阳性预期。

### Sample

样品与 LQC 使用同一二维 WT 参考坐标：

$$
P_{WT}=(\bar Y_{FAM,WT},\bar Y_{ROX,WT})
$$

每个 Sample 重复孔独立计算 `dr'`，先检查重复孔分类方向是否一致，再求平均值。

- `dr' >= 0.4` -> `BRAF V600E 阳性`
- `dr' < 0.4` -> `BRAF V600E 阴性`

如果同一样品出现“两阳一阴”或“两阴一阳”，即使平均 `dr'` 越过阈值，也不能按平均值给出阳/阴性；应直接标记：

```text
Replicate consistency = FAIL
Analysis status = INVALID
Reason = Replicate classification inconsistency
Recommended action = Repeat experiment
```

# 3. 产品设计原则

## 3.1 科学计算与显示严格分离

所有正式计算必须在 C# Analysis Engine 中完成。图表前端只负责显示和交互，绝不允许 JavaScript 图表层自行决定 QC、阈值、归一化或最终分类。

这样做的原因：

- 保证单一计算真源；
- 避免 C# 与 JavaScript 两套公式逐渐不一致；
- 便于单元测试和 Golden Dataset；
- 便于未来更换图形库而不影响算法结果。

## 3.2 原始数据只读

Raw Data 一旦导入就不可覆盖。所有变换产生 Derived Dataset：

```text
Raw RFU
  |
  +-- Normalized Yield
  +-- Smoothed View
  +-- Formula Derived Column
  +-- Filtered View
  +-- Plot Specification
```

“删除异常点”默认实现为 `Excluded` 标记，不从原始数据中物理删除。

## 3.3 Method 版本化

任何会影响结果的参数变更都必须产生新的 Method Version。例如：

```text
BRAF V600E Research Method 1.0
Endpoint = 120 min
Plateau Window = 90-120 min
Plateau RR = <= 1%
NTC Y = [-0.05, +0.05]
WT Y mean = < 0.05 per channel
Threshold = 0.4
```

未来修改阈值或 QC 标准时，新 Run 使用新 Method；旧 Run 永远保持原 Method 快照。

## 3.4 研究自由度与正式判读隔离

Data Explorer 可以平滑、变换、绘图、建立公式，但这些操作不能自动写回 Guided Analysis 的正式结果。

## 3.5 严格失败优先

在 Calibration、Control、重复孔一致性和关键输入校验中，默认策略是：

> 发现无法证明数据有效时，宁可输出 Invalid，也不默默修复并继续判读。

## 3.6 相对路径与项目可移植性

所有项目自有文件夹和文件路径必须使用相对路径。这是架构合同和发布硬门禁，不是实现偏好。

- 源码、项目文件、配置、测试、脚本、文档、日志、报告、DTO、数据库、manifest、支持包和 `.fluoaxisproj` 中严禁保存绝对文件系统路径；
- 仓库路径相对仓库根，项目引用相对其包含文件，工程包 entry 只使用安全的相对名称；
- 系统文件选择器返回的外部绝对路径只允许在基础设施边界内瞬时存在，用于打开流后立即转换为安全显示名、大小、内容 hash 和项目相对托管 entry；
- 瞬时外部路径不得进入日志、异常消息、遥测、审计、DTO、剪贴板、PlotHost、报告或持久化数据；
- 第三方 API 若需要绝对运行时路径，只能由基础设施 adapter 在单次调用内部从受控 root 与相对路径解析，调用结束立即丢弃；
- CI 必须扫描已提交内容中的 drive-rooted、UNC、rooted POSIX、文件 URI、用户主目录和系统临时目录路径并阻断泄漏；HTTPS 外部链接不属于文件系统路径。

# 4. 用户角色与使用场景

第一阶段只定义科研角色，不建立临床 RBAC：

- Research Operator：导入数据、映射孔位、运行 Method、查看结果。
- Research Reviewer：复核数据和计算过程、导出报告。
- Project Administrator：管理 Method、项目模板和软件设置。

第一版可以允许同一 Windows 用户拥有全部权限，但数据模型应保留 `CreatedBy`、`ModifiedBy`、`ReviewedBy` 字段。

# 5. 信息架构与核心工作流

## 5.1 主导航

```text
Home
  |
  +-- Project
  +-- Import
  +-- Plate Map
  +-- Calibration
  +-- Analysis
  +-- Data Explorer
  +-- Report / Export
  +-- Settings
```

## 5.2 Guided Analysis 黄金路径

```text
[1 Import]
    |
[2 Data Mapping]
    |
[3 Plate Mapping]
    |
[4 Calibration: NTC + 100% Positive]
    |
[5 WT / LQC QC]
    |
[6 Sample Analysis]
    |
[7 Result + View Calculation]
    |
[8 Export / Save Project]
```

任何关键 QC Fail 后，正式 Sample Classification 被禁用，但 Data Explorer 仍可访问。

# 6. 功能需求总表

## 6.1 数据导入 FR-IMP

| ID | 要求 |
|---|---|
| FR-IMP-001 | 支持拖拽文件到主窗口导入 |
| FR-IMP-002 | 支持文件选择器导入 |
| FR-IMP-003 | 第一版支持 XLSX、XLS、CSV、TSV、TXT |
| FR-IMP-004 | XLSX 解析不得依赖 Excel 安装，不使用 COM/Interop |
| FR-IMP-005 | 自动扫描候选数据区，不假设首行为表头 |
| FR-IMP-006 | 用户必须可以手工确认 Time 列、FAM 区域、ROX 区域 |
| FR-IMP-007 | 支持同一文件多个 Sheet 或同 Sheet 多块区域 |
| FR-IMP-008 | 检测空值、非数值、重复时间、时间倒序、孔名冲突 |
| FR-IMP-009 | 检测 FAM/ROX 孔集合是否匹配 |
| FR-IMP-010 | 导入后保存源文件 SHA-256 |
| FR-IMP-011 | 导入过程必须可取消，不阻塞 UI 线程 |
| FR-IMP-012 | 导入大表时使用流式读取，不要求一次构建完整 Excel DOM |

## 6.2 时间 FR-TIME

| ID | 要求 |
|---|---|
| FR-TIME-001 | 使用 Elapsed Time，不用固定行号表示时间 |
| FR-TIME-002 | 第一条有效采集记录映射为 t=0 |
| FR-TIME-003 | 内部使用整数 ticks / duration 表示时间，避免浮点时间相等问题 |
| FR-TIME-004 | Method 使用时间窗口，如 90-120 min，不写死“16 个点” |
| FR-TIME-005 | 默认不对正式 Endpoint 进行插值 |
| FR-TIME-006 | Endpoint 允许配置小范围设备时间戳容差 |
| FR-TIME-007 | 找不到合法 120 min Endpoint 时返回 Invalid |

## 6.3 Plate Map FR-PLATE

| ID | 要求 |
|---|---|
| FR-PLATE-001 | 第一版提供 96 孔板 A1-H12 |
| FR-PLATE-002 | 表格列与 Plate Map 双向联动高亮 |
| FR-PLATE-003 | 可框选多个孔批量指定角色 |
| FR-PLATE-004 | 角色至少包括 NTC、100% Positive、WT、LQC 0.1%、Sample、Ignore |
| FR-PLATE-005 | Sample 具有 Sample ID、Group、Replicate、Target、Notes |
| FR-PLATE-006 | 同一 Well 的 FAM/ROX 必须绑定为同一个逻辑孔 |
| FR-PLATE-007 | 不允许同一 Run 内重复占用同一 Well 的互斥角色 |

## 6.4 Calibration FR-CAL

| ID | 要求 |
|---|---|
| FR-CAL-001 | 先计算 100% Positive 平台与 Fm，再完成 NTC 全程 Y QC |
| FR-CAL-002 | 90-120 min 使用实际存在的全部采集点 |
| FR-CAL-003 | 相对极差 RR <= 1% 才认为单孔平台合格 |
| FR-CAL-004 | 单孔 Fm 为 90-120 min 平均值 |
| FR-CAL-005 | 任一关键 100% Positive 重复孔 Fail -> Calibration Fail |
| FR-CAL-006 | 每个 NTC 单独以自身 120 min 值作为该孔 Fb |
| FR-CAL-007 | 每个 NTC 使用自身 Fb 和 Run 级 Fm 做 0-120 min Y QC |
| FR-CAL-008 | 任一 NTC 任一通道存在 Y 超出 [-0.05,+0.05] -> 该孔 Fail |
| FR-CAL-009 | 任一关键 NTC Fail -> Calibration Fail |
| FR-CAL-010 | 所有 NTC Pass 后，才计算 Run 级 Fb_FAM 和 Fb_ROX 均值 |
| FR-CAL-011 | Fm <= Fb 或分母过小必须返回 Calculation Error |

## 6.5 Control FR-QC

| ID | 要求 |
|---|---|
| FR-QC-001 | WT 各孔分别计算 120 min Y_FAM/Y_ROX |
| FR-QC-002 | 计算 WT 两通道组均值作为 WT 参考坐标 |
| FR-QC-003 | WT 通过要求 mean(Y_FAM)<0.05 且 mean(Y_ROX)<0.05 |
| FR-QC-004 | LQC 每孔单独计算 dr' |
| FR-QC-005 | LQC 单孔 dr'>=0.4 为 Positive |
| FR-QC-006 | LQC 重复孔分类方向不一致 -> Run Invalid |
| FR-QC-007 | LQC 一致后报告 mean dr'，并要求符合弱阳性预期 |

## 6.6 Sample FR-SAM

| ID | 要求 |
|---|---|
| FR-SAM-001 | Sample 每个重复孔独立归一化 |
| FR-SAM-002 | Sample 每孔以 WT 组平均二维坐标计算 dr' |
| FR-SAM-003 | 单孔 dr'>=0.4 -> BRAF V600E Positive |
| FR-SAM-004 | 单孔 dr'<0.4 -> BRAF V600E Negative |
| FR-SAM-005 | 重复孔必须先检查分类方向一致性 |
| FR-SAM-006 | 出现两阳一阴/两阴一阳 -> INVALID，不先按均值判读 |
| FR-SAM-007 | 方向一致后计算并显示 mean dr' |
| FR-SAM-008 | 最终措辞只用“BRAF V600E 阳性/阴性” |

## 6.7 Data Explorer FR-EXP

| ID | 要求 |
|---|---|
| FR-EXP-001 | 支持导入数据后以表格查看 |
| FR-EXP-002 | 支持用户新建空表并手工输入变量 |
| FR-EXP-003 | 支持列重命名、单位、排序、筛选、复制、粘贴 |
| FR-EXP-004 | 支持二维散点图、折线图、散点+线 |
| FR-EXP-005 | 支持三维笛卡尔坐标系 scatter3D 与 line3D |
| FR-EXP-006 | 支持任意列映射到 X/Y/Z |
| FR-EXP-007 | 支持多序列、图例、误差条、线性/对数坐标 |
| FR-EXP-008 | 支持移动平均和低阶 Savitzky-Golay 视觉平滑 |
| FR-EXP-009 | 正式判读永远不使用平滑数据 |
| FR-EXP-010 | 支持安全 Formula Builder |
| FR-EXP-011 | 支持二维和三维两点距离 |
| FR-EXP-012 | 图上点选必须能够回到原始 Point ID |

## 6.8 输出 FR-OUT

| ID | 要求 |
|---|---|
| FR-OUT-001 | 导出 Excel |
| FR-OUT-002 | 导出 PDF Research Analysis Report |
| FR-OUT-003 | 二维图支持 PNG 与 SVG |
| FR-OUT-004 | 三维 WebGL 图支持高分辨率 PNG；PDF 中嵌入高分辨率图像 |
| FR-OUT-005 | 可选导出交互式 HTML 图表 |
| FR-OUT-006 | 保存软件自有工程文件 `.fluoaxisproj` |
| FR-OUT-007 | 报告必须记录 Method Version 和软件版本 |

# 7. 详细算法规范

## 7.1 数据精度

- RFU 和归一化值内部统一使用 IEEE 754 `double`。
- 不在中间步骤进行显示级四舍五入。
- 阈值比较使用原始 `double` 结果。
- UI 可以显示 3-6 位小数，但显示精度不得改变正式分类。
- `0.400000` 必须按照 `>= 0.4` 判 Positive。

## 7.2 平台相对极差

算法：

```text
input = values where 90 min <= t <= 120 min
mean = arithmetic mean(input)
max  = maximum(input)
min  = minimum(input)
RR   = (max - min) / mean * 100
PASS if RR <= 1.0
```

防御性条件：

- 点数为 0 -> Invalid Window
- mean <= 0 且实验协议不允许 -> Invalid Plateau
- NaN/Infinity -> Calculation Error

## 7.3 NTC 计算顺序

```text
100% POS -> per-well plateau QC -> per-well Fm -> Run Fm
                                      |
                                      v
NTC -> per-well Fb(120min) -> per-well full-course Y QC
                                      |
                            all wells PASS?
                              /      \
                            no        yes
                            |          |
                     Calibration    Run Fb mean
                        FAIL           |
                                       v
                             Calibration PASS
```

## 7.4 WT 参考点

```text
WT well 1 -> Y_FAM_1, Y_ROX_1
WT well 2 -> Y_FAM_2, Y_ROX_2
...

WT reference = (
  mean(Y_FAM_i),
  mean(Y_ROX_i)
)
```

## 7.5 Sample / LQC dr'

二维几何距离：

$$
dr'_i=\sqrt{(Y_{FAM,i}-\bar Y_{FAM,WT})^2+(Y_{ROX,i}-\bar Y_{ROX,WT})^2}
$$

注意：三维 Data Explorer 的通用距离功能与 BRAF 正式 `dr'` 是不同概念。BRAF V1.0 的 `dr'` 仍严格是 FAM/ROX 二维距离。三维功能只用于通用科研可视化和未来 Method 扩展。

# 8. Run 状态机

```text
NEW
 |
IMPORTED
 |
MAPPED
 |
CALIBRATING
 |------> CALIBRATION_FAILED ------> RUN_INVALID
 |
CALIBRATION_PASSED
 |
CONTROL_QC
 |------> CONTROL_QC_FAILED -------> RUN_INVALID
 |
READY_FOR_ANALYSIS
 |
ANALYZING
 |------> REPLICATE_CONFLICT ------> RUN_INVALID
 |
ANALYZED
 |
EXPORTED
```

状态要求：

- 状态转换由 Application Layer 管理，不由 ViewModel 任意修改。
- 失败原因保存结构化 ErrorCode 和可读 Message。
- `RUN_INVALID` 后仍可进入 Data Explorer，但不能显示正式 Sample Classification 为有效结果。

# 9. UI / UX 规范

## 9.1 视觉语言

- 使用 Fluent Design，适配 Windows 11。
- 主界面采用三栏：Project/Navigation、Workspace、Context/Parameters。
- 深色/浅色模式跟随系统并允许手工切换。
- 不堆砌大量 16x16 老式工具栏图标。
- 重要状态使用图标 + 文字，不只依赖颜色。
- 所有交互支持 125%、150%、200% DPI。

## 9.2 主工作区草图

```text
+-------------------------------------------------------------------+
| Project | Import | Method | Plot | Export                 Settings |
+--------------+--------------------------------+-------------------+
| PROJECT      |                                | PARAMETERS        |
| Run 001      |           Plot / Plate         | Method            |
|  Raw         |                                | Threshold         |
|  Calibration |                                | Window            |
|  QC          +--------------------------------+ Channel           |
|  Samples     |          Data Table            | Selection         |
|  Report      |                                |                   |
+--------------+--------------------------------+-------------------+
| RUN VALID | FAM + ROX | Endpoint 120 min | Method 1.0            |
+-------------------------------------------------------------------+
```

## 9.3 Calibration 页面

需要同时展示：

- NTC per-well Fb；
- NTC 全程 Y 曲线与 `[-0.05,+0.05]` 带状区；
- Positive per-well Fm；
- 90-120 min 平台窗口；
- Relative Range；
- Run 级 Fb/Fm；
- PASS/FAIL 和失败原因。

## 9.4 View Calculation

每个最终结果旁必须提供“查看计算过程”。显示：

```text
Fb_FAM
Fm_FAM
Fb_ROX
Fm_ROX
F_FAM_120
F_ROX_120
Y_FAM
Y_ROX
WT_Y_FAM_mean
WT_Y_ROX_mean
dr'
Threshold
Result
```

# 10. 现代化技术选型

## 10.1 总体结论

第一版推荐技术栈：

| 层 | 选择 |
|---|---|
| OS 基线 | Windows 11 22H2+，x64 主目标；ARM64 可作为第二目标 |
| 语言 | C# 14 |
| Runtime | .NET 10 LTS |
| Native UI | Windows App SDK stable + WinUI 3 |
| MVVM | CommunityToolkit.Mvvm stable，使用 Source Generators |
| Host / DI / Config | Microsoft.Extensions.Hosting + built-in DI + System.Text.Json |
| Excel XLSX | DocumentFormat.OpenXml 3.x，SAX/streaming 读取 |
| Legacy XLS | ExcelDataReader，仅放在兼容适配层 |
| CSV/TXT | CsvHelper 或经过测试的流式 parser，统一映射到内部 schema |
| Persistence | Microsoft.Data.Sqlite + SQLite |
| Project Archive | System.IO.Compression.ZipArchive + manifest + SHA-256 |
| Charts | WebView2 + 本地固定版本 Plotly.js 3.x |
| 2D 高点数 | Plotly `scattergl` WebGL 路径 |
| 3D | Plotly `scatter3d` / `surface`，GPU WebGL |
| C# -> Chart 高速传输 | WebView2 Shared Buffer + TypedArray；小数据 JSON fallback |
| Report | HTML/CSS template + WebView2 `PrintToPdfAsync` |
| Unit Test | xUnit v3 或 MSTest current stable，项目统一一种 |
| Benchmark | BenchmarkDotNet |
| Packaging | MSIX 优先；内部测试可使用 self-contained WinUI build |
| CI | `dotnet build/test` + Windows CI runner + locked dependencies |

## 10.2 为什么选择 .NET 10 LTS

截至 2026-08，.NET 10 是微软处于 Active support 的 LTS 版本，支持期到 2028-11。它同时提供当前 JIT、GC、C# 14、性能与诊断能力。项目不应新建在 .NET Framework、.NET 6 或其他已退出/临近退出支持的 Runtime 上。

工程基线建议：

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

SDK 通过 `global.json` 锁定到团队验证过的 .NET 10 patch，允许 `latestPatch`，不自动跳到 .NET 11 Preview。

## 10.3 为什么选择 WinUI 3

WinUI 3 是 Microsoft 对全新原生 Windows 桌面应用的推荐 UI 框架。项目明确不采用以下作为主 UI：

- WinForms；
- UWP；
- .NET Framework WPF；
- Electron 主壳；
- 旧式 MFC；
- 浏览器-only PWA。

WPF 仍受支持，但本项目是从零开始的新 Windows 原生产品，因此首选 WinUI 3。

Windows App SDK 仅使用 Stable Channel。编写本文时官方下载页列出的稳定版本包括 2.3.1（2026-07-16）。项目要把具体 patch 锁定在 `Directory.Packages.props`，升级必须经过回归测试，不引用 Preview / Experimental。

## 10.4 不使用已归档 DataGrid

Windows Community Toolkit 旧 DataGrid 已被官方文档标记为 Archived，不应在新项目中直接拿 7.x DataGrid 作为核心表格控件。

第一版建议实现受控的 `VirtualDataSheet`：

- 基于 WinUI `ItemsRepeater` / recycling；
- 分离 RowHeader、ColumnHeader、Viewport、SelectionLayer、EditorLayer；
- 只实例化当前 viewport 附近的可见 cell；
- 单元格编辑使用一个复用 TextBox overlay，而不是为每个 cell 常驻 TextBox；
- 96x121 的当前数据规模很轻；
- 未来扩展到更大科研表格时仍具备虚拟化能力。

这比引入已归档控件更符合“现代化且长期维护”的要求。

# 11. 解决方案代码架构

采用 **模块化单体 + Clean Architecture 思想**，而不是微服务。该软件是本地桌面科研软件，使用微服务只会增加部署、故障面和数据同步复杂度。

## 11.1 Solution Tree

```text
FluoAxis.sln
|
+-- src/
|   +-- FluoAxis.App/                WinUI 3 shell, XAML, composition root
|   +-- FluoAxis.Presentation/       ViewModels, navigation, UI models
|   +-- FluoAxis.Contracts/          versioned frontend-backend DTOs and gateway contracts
|   +-- FluoAxis.Application/        Use cases, orchestration, state machine
|   +-- FluoAxis.Domain/             Domain entities, value objects, rules
|   +-- FluoAxis.Analysis/           Pure deterministic analysis engine
|   +-- FluoAxis.Import/             XLSX/XLS/CSV/TXT adapters
|   +-- FluoAxis.Persistence/        SQLite repositories, migrations
|   +-- FluoAxis.PlotProtocol/       versioned PlotSpec and WebView2 bridge messages
|   +-- FluoAxis.Visualization/      PlotSpec + WebView2/Plotly adapter
|   +-- FluoAxis.Reporting/          HTML report + PDF renderer
|   +-- FluoAxis.ProjectFormat/      .fluoaxisproj packaging/hash/manifest
|   +-- FluoAxis.Infrastructure/     file system, logging, clock, diagnostics
|
+-- tests/
|   +-- FluoAxis.Analysis.Tests/
|   +-- FluoAxis.Application.Tests/
|   +-- FluoAxis.Contract.Tests/
|   +-- FluoAxis.Import.Tests/
|   +-- FluoAxis.Persistence.Tests/
|   +-- FluoAxis.Visualization.Tests/
|   +-- FluoAxis.Golden.Tests/
|   +-- FluoAxis.Benchmarks/
|
+-- web/
|   +-- FluoAxis.PlotHost/           local HTML/TypeScript/Plotly host source
|
+-- assets/
|   +-- plots/plotly.min.js
|   +-- plots/plot-host.html
|   +-- reports/research-report.html
|   +-- reports/report.css
|
+-- samples/
|   +-- golden/
|
+-- Directory.Build.props
+-- Directory.Packages.props
+-- global.json
+-- .editorconfig
+-- README.md
```

## 11.2 依赖方向

```text
App / Presentation
       |
       v
Application ---------> Visualization abstractions
       |
       v
Domain <------------ Analysis
       ^
       |
Import / Persistence / Reporting / ProjectFormat
       ^
       |
Infrastructure implementations
```

硬性规则：

- `Domain` 不引用 WinUI、SQLite、WebView2、Excel。
- `Analysis` 不引用 Presentation。
- `Visualization` 不参与正式判读。
- `Application` 只通过接口调用持久化、导入和图形实现。
- UI 不直接执行 SQL。

## 11.3 前后端协作边界

前后端分离在首发桌面版本中表现为同一进程内的稳定契约边界，而不是 localhost 微服务。`FluoAxis.Presentation` 只能通过 `IFluoAxisGateway` 调用 `FluoAxis.Application`，不得直接访问 Domain、Analysis、SQLite 或文件系统。

- Gateway 命令和查询使用版本化 DTO、Result/Error 和不可变 Snapshot；
- 修改 Run 的命令必须携带 request ID、Run ID、期望 revision、actor、issue time 和 contract version；
- 后端返回 allowed actions、阻断/警告问题、Method 版本和分析摘要，UI 不自行决定按钮状态或业务 Run 状态；
- Fake Gateway 与真实 Gateway 必须通过同一套契约一致性测试；
- PlotHost 只接收版本化 PlotSpec/只读数据，不接收文件系统路径、数据库连接或算法对象。

# 12. Domain 模型

推荐使用 `record` / `readonly record struct` 表达不可变值对象。

```csharp
public readonly record struct WellId(char Row, int Column)
{
    public override string ToString() => $"{Row}{Column}";
}

public enum FluorescenceChannel
{
    Fam,
    Rox
}

public enum WellRole
{
    Ntc,
    Positive100,
    WildType,
    Lqc01Percent,
    Sample,
    Ignore
}

public readonly record struct SignalPoint(
    TimeSpan Elapsed,
    double Rfu);

public sealed record SignalSeries(
    Guid RunId,
    WellId Well,
    FluorescenceChannel Channel,
    IReadOnlyList<SignalPoint> Points);
```

避免把整个系统建立在 `DataTable`、`Dictionary<string, object>` 或大量弱类型 JSON 上。

# 13. Analysis Engine 架构

## 13.1 接口

```csharp
public interface IAnalysisEngine
{
    CalibrationResult Calibrate(
        CalibrationInput input,
        BrafMethodSnapshot method);

    ControlQcResult EvaluateControls(
        ControlQcInput input,
        CalibrationResult calibration,
        BrafMethodSnapshot method);

    SampleAnalysisResult AnalyzeSample(
        SampleAnalysisInput input,
        WtReference wtReference,
        CalibrationResult calibration,
        BrafMethodSnapshot method);
}
```

`BrafMethodSnapshot` 是每个 Run 的不可变方法快照，而不是读取全局“当前设置”。

## 13.2 核心函数

```csharp
public static double Normalize(double f, double fb, double fm)
{
    var denominator = fm - fb;
    if (!double.IsFinite(denominator) || denominator <= 0d)
        throw new InvalidCalibrationException(fb, fm);

    var y = (f - fb) / denominator;
    if (!double.IsFinite(y))
        throw new NonFiniteCalculationException();

    return y;
}

public static double Distance2D(
    double x1, double y1,
    double x2, double y2)
{
    return Math.Sqrt(
        Math.Pow(x2 - x1, 2) +
        Math.Pow(y2 - y1, 2));
}

public static double Distance3D(
    double x1, double y1, double z1,
    double x2, double y2, double z2)
{
    return Math.Sqrt(
        Math.Pow(x2 - x1, 2) +
        Math.Pow(y2 - y1, 2) +
        Math.Pow(z2 - z1, 2));
}
```

## 13.3 Plateau 计算

```csharp
public static PlateauResult EvaluatePlateau(
    ReadOnlySpan<double> values,
    double maxRelativeRange)
{
    if (values.IsEmpty)
        return PlateauResult.Invalid("EMPTY_WINDOW");

    var min = double.PositiveInfinity;
    var max = double.NegativeInfinity;
    var sum = 0d;

    foreach (var value in values)
    {
        if (!double.IsFinite(value))
            return PlateauResult.Invalid("NON_FINITE_VALUE");

        min = Math.Min(min, value);
        max = Math.Max(max, value);
        sum += value;
    }

    var mean = sum / values.Length;
    if (mean <= 0d)
        return PlateauResult.Invalid("NON_POSITIVE_MEAN");

    var rr = (max - min) / mean;
    return new PlateauResult(
        IsValid: rr <= maxRelativeRange,
        Mean: mean,
        RelativeRange: rr,
        ErrorCode: null);
}
```

Method 中 `maxRelativeRange` 存为 `0.01`，UI 显示为 `1%`。

## 13.4 重复孔一致性优先于均值

```csharp
public static ReplicateResult EvaluateReplicates(
    IReadOnlyList<double> drValues,
    double threshold)
{
    var calls = drValues
        .Select(v => v >= threshold)
        .ToArray();

    if (calls.Distinct().Count() != 1)
    {
        return ReplicateResult.Invalid(
            "REPLICATE_CLASSIFICATION_INCONSISTENCY");
    }

    return ReplicateResult.Valid(
        meanDr: drValues.Average(),
        isPositive: calls[0]);
}
```

# 14. Application Layer 与状态编排

UI 不应直接调用 `AnalysisEngine` 的多个函数拼接业务流程。由 Use Case 统一控制：

```text
RunCalibrationUseCase
RunControlQcUseCase
AnalyzeSamplesUseCase
ExportResearchReportUseCase
SaveProjectUseCase
```

示例：

```csharp
public sealed class AnalyzeSamplesUseCase(
    IRunRepository runs,
    IAnalysisEngine analysis,
    IClock clock)
{
    public async Task<RunAnalysisSummary> ExecuteAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await runs.LoadAsync(runId, cancellationToken);

        run.RequireState(RunState.ReadyForAnalysis);

        var snapshot = run.CreateImmutableAnalysisSnapshot();
        var result = AnalyzeSnapshot(snapshot, analysis);

        await runs.SaveAnalysisAsync(
            runId,
            result,
            clock.UtcNow,
            cancellationToken);

        return result;
    }
}
```

关键点：开始分析时创建不可变输入快照。用户即使同时在 UI 改孔映射，也不能改变正在执行中的计算。

# 15. WinUI 3 实现

## 15.1 App Shell

使用：

- `NavigationView` 做一级导航；
- `TabView` 做项目/数据页标签；
- `InfoBar` 展示非阻塞错误与警告；
- `ContentDialog` 只用于需要明确确认的破坏性操作；
- `TeachingTip` 可用于首次使用引导；
- `CommandBar` 只保留高频命令。

## 15.2 MVVM

采用 `CommunityToolkit.Mvvm` Source Generators：

```csharp
public partial class CalibrationViewModel : ObservableObject
{
    private readonly RunCalibrationUseCase _useCase;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private CalibrationResultView? result;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync(CancellationToken token)
    {
        IsBusy = true;
        try
        {
            Result = await _useCase.ExecuteAsync(CurrentRunId, token);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRun() => !IsBusy && CurrentRunId != Guid.Empty;
}
```

禁止：

- 巨量 code-behind；
- View 直接读数据库；
- `async void` 业务函数；
- ViewModel 内复制分析公式。

# 16. Host、DI、配置与日志

使用 `.NET Generic Host`：

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IAnalysisEngine, BrafAnalysisEngine>();
builder.Services.AddSingleton<IPlotRenderer, PlotlyWebViewRenderer>();
builder.Services.AddSingleton<IProjectArchive, FluoAxisProjectArchive>();
builder.Services.AddSingleton<IRunRepository, SqliteRunRepository>();

builder.Services.AddTransient<RunCalibrationUseCase>();
builder.Services.AddTransient<AnalyzeSamplesUseCase>();

builder.Services.AddTransient<MainWindow>();
builder.Services.AddTransient<MainViewModel>();

using var host = builder.Build();
await host.StartAsync();
```

日志使用 `Microsoft.Extensions.Logging` 抽象。文件日志 provider 可以选择成熟、维护活跃的实现，但必须：

- 结构化 JSON；
- 日志滚动；
- 不写患者敏感信息；
- Error 包含 Correlation ID / Run ID；
- 所有字段在进入 provider 前执行集中式路径脱敏；不得写入绝对路径、外部源文件系统位置或含路径的异常消息/堆栈；
- 文件只记录安全显示名、项目相对托管 entry、内容 hash 和 path-free error code；
- Release 构建不记录完整原始表内容。

# 17. Excel / 文本导入架构

## 17.1 接口

```csharp
public interface IDataImporter
{
    bool CanImport(string extension);

    Task<ImportPreview> InspectAsync(
        Stream stream,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ImportedSeriesRow> ReadAsync(
        Stream stream,
        DataMapping mapping,
        CancellationToken cancellationToken);
}
```

## 17.2 XLSX

主实现采用 Open XML SDK。大文件使用 SAX / `OpenXmlReader`，避免：

```text
SpreadsheetDocument -> 全工作簿 DOM -> 全部 Cell 对象常驻内存
```

而使用：

```text
Workbook metadata
   -> candidate sheets
   -> streaming rows
   -> normalize cell values
   -> ImportPreview / Internal Schema
```

不得使用：

- `Microsoft.Office.Interop.Excel`；
- 机器必须安装 Office；
- UI 进程启动隐藏 Excel.exe；
- OLE Automation。

## 17.3 XLS 兼容

`.xls` 是旧二进制格式。第一版如果保留兼容，应把 ExcelDataReader 放在：

```text
FluoAxis.Import.LegacyXlsAdapter
```

它只负责把数据转成内部 `ImportPreview`/`ImportedSeriesRow`，其他模块完全不知道 BIFF 格式。

## 17.4 CSV/TXT

必须支持：

- UTF-8；
- UTF-8 BOM；
- 常见本地编码需要显式选择，不默默猜测；
- comma / tab / semicolon 可配置；
- quoted cell；
- decimal point 文化设置。

内部数值解析默认使用 InvariantCulture，用户明确选择其他 locale 时才改变。

# 18. Persistence 与 SQLite

## 18.1 为什么用 SQLite

当前产品是离线本地科研工作站，不需要部署 PostgreSQL/SQL Server 服务。SQLite 足以存放：

- Project；
- Run；
- 孔映射；
- 时间序列；
- 计算结果；
- 图形配置；
- Audit Event；
- Method Snapshot。

使用 `Microsoft.Data.Sqlite` 直接控制 SQL 和事务，避免在核心时序批量插入路径上叠加不必要 ORM 开销。

## 18.2 推荐表

```text
project
run
source_file
channel
well_assignment
signal_point
calibration_well_result
run_calibration
control_result
sample
sample_well_result
plot_spec
derived_column
method_snapshot
audit_event
```

## 18.3 Signal Point Schema

```sql
CREATE TABLE signal_point (
    run_id        TEXT    NOT NULL,
    channel       INTEGER NOT NULL,
    well_row      INTEGER NOT NULL,
    well_column   INTEGER NOT NULL,
    point_index   INTEGER NOT NULL,
    elapsed_ticks INTEGER NOT NULL,
    rfu           REAL    NOT NULL,
    PRIMARY KEY (
        run_id,
        channel,
        well_row,
        well_column,
        point_index
    )
);
```

写入时：

- 单事务；
- prepared statement；
- 批量 commit；
- 不做每个 point 一个 transaction。

## 18.4 SQLite 运行模式

工作区数据库可启用 WAL 以提高正常操作期间的读写并发。打包工程前执行 checkpoint、关闭连接，再进行 ZIP 打包，避免把未合并 WAL 状态直接打包。

# 19. `.fluoaxisproj` 工程格式

采用 ZIP64 容器，不创建神秘私有二进制格式。

```text
example.fluoaxisproj
|
+-- manifest.json
+-- project.sqlite
+-- raw/
|   +-- source-001.xlsx
|   +-- source-001.sha256
+-- plots/
|   +-- plot-specs.json
+-- reports/
|   +-- last-report.html
+-- audit/
    +-- events.jsonl
```

`manifest.json` 示例：

```json
{
  "format": "fluoaxisproj",
  "formatVersion": 1,
  "projectId": "...",
  "createdUtc": "2026-08-10T08:00:00Z",
  "appVersion": "1.0.0",
  "methodVersions": ["BRAF-V600E-Research-1.0"],
  "hashAlgorithm": "SHA-256"
}
```

保存采用：

```text
write temporary package
 -> flush
 -> validate manifest
 -> reopen and verify
 -> atomic replace destination
```

不得在用户正式工程文件上原地半写半改。

# 20. 二维与三维可视化实现

## 20.1 为什么采用 WebView2 + Plotly.js

该组合的目的不是把整个桌面软件变成网页，而是把“高交互科研图形引擎”封装成一个独立 Renderer：

- WinUI 3 仍是原生主壳；
- C# 仍是数据与算法真源；
- Plotly.js 只负责绘图；
- WebView2 在 Windows 中是微软官方嵌入式 Web 运行时；
- Plotly `scattergl` 和全部 3D 图使用 WebGL/GPU；
- 2D 与 3D 由同一 PlotSpec 管理，降低双图形引擎维护成本。

## 20.2 Plot Renderer 抽象

```csharp
public interface IPlotRenderer
{
    Task RenderAsync(
        PlotSpec spec,
        PlotDataSnapshot data,
        CancellationToken cancellationToken);

    Task<byte[]> ExportPngAsync(
        PlotExportOptions options,
        CancellationToken cancellationToken);

    Task<string?> ExportSvgAsync(
        PlotExportOptions options,
        CancellationToken cancellationToken);
}
```

业务层只认识 `PlotSpec`，不直接保存 Plotly JSON。这样未来需要替换 SciChart、Direct3D 或其他 renderer 时，不需要改项目文件和算法层。

## 20.3 PlotSpec

```csharp
public sealed record PlotSpec(
    Guid PlotId,
    PlotKind Kind,
    AxisSpec X,
    AxisSpec Y,
    AxisSpec? Z,
    IReadOnlyList<SeriesSpec> Series,
    CameraSpec? Camera,
    bool ShowLegend,
    string Title);
```

`PlotKind` 至少包括：

```text
Scatter2D
Line2D
ScatterLine2D
Scatter3D
Line3D
Surface3D
```

## 20.4 2D 渲染

小数据可使用 SVG trace；高点数自动使用 `scattergl`。切换规则由 Renderer 根据数据量决定，不改变 PlotSpec。

二维导出：

- SVG：论文图优先；
- PNG：PPT/报告；
- HTML：可选交互版。

## 20.5 3D 渲染

三维必须实现：

- X/Y/Z 任意列映射；
- scatter3D；
- line3D；
- optional surface3D；
- 鼠标 orbit；
- pan；
- zoom；
- camera reset；
- perspective / orthographic 可选；
- axis range；
- linear/log 轴；
- marker size；
- series visibility；
- legend；
- point hover；
- point selection；
- 两点 3D distance。

三维正式数据结构示例：

```text
PointId | X | Y | Z | SeriesId | Label | SourceRow
```

Plotly `customdata` 中只保存轻量 `PointId`。用户点击后：

```text
Plotly click event -> PointId -> C# host
 -> C# 查原始数据
 -> C# 计算距离/显示精确数值
```

不相信 JavaScript 传回的格式化字符串作为正式数值。

## 20.6 C# 到 WebView2 的高速数据桥

当前实验数据很小，JSON 已足够快。但为了支持未来更大表格和 3D 点云，Renderer 设计双通道：

```text
small data  -> PostWebMessageAsJson
large data  -> CoreWebView2SharedBuffer -> ArrayBuffer -> Float64Array
```

共享内存布局建议：

```text
header
  version:int32
  pointCount:int32
  dimension:int32
  reserved:int32

payload
  x[pointCount] float64
  y[pointCount] float64
  z[pointCount] float64   // only for 3D
```

C#：

```csharp
var bytes = checked((ulong)(pointCount * dimensions * sizeof(double)));
using var buffer = webViewEnvironment.CreateSharedBuffer(bytes);

await using var stream = buffer.OpenStream();
// Write x/y/z doubles into shared buffer.

webView.CoreWebView2.PostSharedBufferToScript(
    buffer,
    CoreWebView2SharedBufferAccess.ReadOnly,
    metadataJson);
```

JavaScript 端将其视为 `ArrayBuffer` / `Float64Array`，绘制后立即 release buffer。

安全要求：Shared Buffer 只发送给本地可信 plot host，不允许 WebView2 导航到外部站点。

## 20.7 WebView2 安全边界

Plot host 使用本地虚拟域，例如：

```text
https://appassets.local/plot-host.html
```

要求：

- Plotly.js 本地打包，不从 CDN 加载；
- Content Security Policy 禁止外部 network；
- 禁止未授权 navigation；
- Release 构建默认关闭 DevTools；
- 不把文件系统对象直接暴露给 JavaScript；
- C# / JS 只交换 schema-versioned DTO 或只读 shared buffer。

# 21. 图形更新策略

不得每次鼠标操作都销毁重建 WebView2。

```text
create WebView2 once
 -> initialize local plot host
 -> Plotly.react for dataset/style updates
 -> preserve camera with uirevision
```

大数据绘图：

- 原始数据永远保留完整；
- 视觉 overview 可使用 LOD / decimation；
- 任何降采样必须标记 `visualization only`；
- 点击精确点时允许切换局部 full-resolution 数据；
- 正式计算绝不基于降采样图形数据。

# 22. 三维距离工具

交互流程：

```text
Measure -> Distance 3D
 -> click point A
 -> click point B
 -> C# retrieves exact X/Y/Z
 -> C# computes Euclidean distance
 -> overlay result
```

显示：

```text
Point A: (x1, y1, z1)
Point B: (x2, y2, z2)
Dx = x2-x1
Dy = y2-y1
Dz = z2-z1
Distance = sqrt(Dx^2 + Dy^2 + Dz^2)
```

# 23. Formula Builder

## 23.1 不执行任意脚本

禁止以下实现：

- C# Roslyn Scripting 直接执行用户字符串；
- Python `eval`；
- JavaScript `eval`；
- PowerShell；
- 任意 DLL 加载。

## 23.2 安全表达式引擎

实现一个小型 Pratt Parser：

```text
Tokenizer
  -> Parser
  -> AST
  -> Semantic validation
  -> Symbol binding
  -> System.Linq.Expressions compile
  -> cached delegate
```

白名单函数：

```text
sqrt abs min max mean median sd
log ln exp pow
```

白名单运算符：

```text
+ - * / ^ ( )
```

禁止 IO、反射、网络、进程调用。

## 23.3 变量绑定

```text
F   -> DataColumn(A1_FAM_120)
Fb  -> Calibration.FbFam
Fm  -> Calibration.FmFam
```

Formula Definition 与 Formula Evaluation 分离，便于版本化和缓存。

# 24. 平滑算法

第一版仅允许：

## Moving Average

- 用户设置窗口；
- 默认 centered；
- 明确边界处理策略。

## Savitzky-Golay

- 默认多项式阶数 2；
- 允许 2 或 3；
- 窗口必须为奇数；
- 不开放高阶多项式；
- UI 显示“仅用于可视化”。

正式 BRAF 计算只取 Raw RFU。

# 25. VirtualDataSheet 实现

## 25.1 组件结构

```text
VirtualDataSheet
  +-- ColumnHeaderPresenter
  +-- RowHeaderPresenter
  +-- CellViewport
  |    +-- ItemsRepeater / RecyclingElementFactory
  +-- SelectionLayer
  +-- ActiveCellLayer
  +-- CellEditorOverlay(TextBox reuse)
```

## 25.2 性能策略

- 可见区虚拟化；
- 行列数据与 visual element 分离；
- 复制粘贴批量处理；
- 大范围选择仅存 range，不创建 N 个 Selection 对象；
- background parse / sort；
- UI 线程只做 visual update；
- 统计计算使用不可变 snapshot。

# 26. Report Engine

使用 HTML/CSS 作为报告模板，然后在隐藏的 WebView2 中渲染并调用 `PrintToPdfAsync`。

优势：

- 同一套排版可以输出 HTML 和 PDF；
- 中文字体和复杂表格更稳定；
- 可复用 Plotly 导出的图像；
- 不需要引入额外 PDF 商业授权库。

Research Report 至少包含：

```text
Cover
Project / Run / Date / Software Version
Method Version
Source Files + SHA-256
Plate Map
Calibration Summary
NTC QC
100% Positive Plateau QC
WT QC
LQC QC
Sample Results
Per-well dr'
Mean dr'
2D reference scatter
Relevant kinetic curves
Notes
```

三维图如果加入 PDF，使用高 DPI PNG，因为 WebGL 三维不是 SVG 矢量图。交互式三维可另导出 HTML。

# 27. 可靠性与崩溃恢复

## 27.1 Autosave

活跃项目在 workspace SQLite 中持续保存，不需要每一步都重新打包 ZIP。

```text
user action
 -> transaction commit
 -> autosave checkpoint
 -> update recovery marker
```

启动时检测未正常关闭的 workspace：

```text
Recovery available for Project X / Run 003
[Open recovery] [Discard]
```

## 27.2 Atomic Save

正式 `.fluoaxisproj`：

```text
project.tmp.fluoaxisproj
 -> write
 -> close
 -> reopen validation
 -> fsync/flush where applicable
 -> replace project.fluoaxisproj
```

## 27.3 错误模型

统一结构：

```csharp
public sealed record AppError(
    string Code,
    ErrorSeverity Severity,
    string UserMessage,
    string? SafeDetailCode,
    Guid CorrelationId,
    Guid? RunId,
    string? SafeDisplayName);
```

用户界面只看到可行动建议、安全显示名和 Correlation ID。原始 `Exception` 不进入 DTO、日志、遥测、审计、报告或支持包；基础设施 adapter 在边界内把异常归一化为 path-free error code。若诊断构建需要崩溃调用链，必须先移除源文件路径、外部目标路径、用户主目录和原始实验内容，再写入受控的 redacted crash event。

# 28. 并发与异步

规则：

- 文件 IO 使用真正 async API；
- Excel parsing 在 background pipeline；
- 计算任务支持 CancellationToken；
- UI 线程只做 UI；
- 不用 `Task.Run` 包裹所有事情当作“异步”；
- CPU 计算只有在数据规模足够大时才并行；
- BRAF 正式算法保持确定性，不依赖线程执行顺序。

推荐 pipeline：

```text
File Stream
 -> Importer
 -> Validator
 -> Normalizer to internal schema
 -> Transactional Repository
 -> Analysis Snapshot
 -> Pure Analysis Engine
 -> Persist Result
 -> UI notification
```

# 29. System.Text.Json 与 DTO

项目 manifest、PlotSpec、WebView2 bridge DTO 使用 `System.Text.Json` source generation，减少运行时反射和 schema 漂移。

```csharp
[JsonSerializable(typeof(ProjectManifest))]
[JsonSerializable(typeof(PlotSpec))]
[JsonSerializable(typeof(PlotMessage))]
internal partial class AppJsonContext : JsonSerializerContext;
```

DTO 必须包含 `schemaVersion`。

# 30. 性能预算

以下是工程目标，不是当前测量值。应在 CI Benchmark 中验证。

| 场景 | 目标 |
|---|---|
| 冷启动到可操作 | <= 4 s on reference lab PC |
| 热启动 | <= 2 s |
| 96 well x 121 points x 2 channels XLSX 导入 | <= 2 s typical |
| 单 Run 全量 BRAF 计算 | <= 100 ms typical |
| 2D 25k points 首次渲染 | <= 500 ms target |
| 3D 25k points 首次渲染 | <= 800 ms target |
| 2D/3D pan/orbit | interactive, target 30-60 FPS |
| 普通项目内存 | target < 500 MB including WebView2 |
| Autosave 小变更 | target < 200 ms perceived blocking |

不要为当前 96x121 小数据过早使用复杂 SIMD/unsafe。先保持代码正确、清晰和可测；如果 Benchmark 发现瓶颈，再对局部使用 `Span<T>`、`ArrayPool<T>`、CommunityToolkit.HighPerformance 或向量化。

# 31. 版本锁定与供应链稳定性

## 31.1 Central Package Management

所有 NuGet 版本集中到：

```text
Directory.Packages.props
```

使用：

```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
```

Release CI 使用 locked mode。

## 31.2 禁止浮动依赖

禁止：

```text
Microsoft.WindowsAppSDK = 2.*
CommunityToolkit.Mvvm = *
plotly = latest
```

必须固定测试过的 patch。

## 31.3 升级策略

```text
Dependency update PR
 -> build
 -> unit tests
 -> Golden Dataset
 -> import fixtures
 -> plot screenshot regression
 -> PDF regression
 -> performance benchmark
 -> manual smoke
 -> merge
```

# 32. 不采用的旧式或高风险实现

以下方案明确不作为本项目主路径：

| 不采用 | 原因 |
|---|---|
| .NET Framework 4.x | 不是新项目现代 Runtime 基线 |
| WinForms 主界面 | 不符合目标现代 Fluent UX |
| UWP | 新开发不推荐，WinUI 3 为当前路径 |
| 旧 WCT DataGrid 7.x | 已归档，不作为核心长期依赖 |
| Excel COM Interop | 依赖 Office、进程不稳定、难测试、难部署 |
| OLE DB Excel provider | 驱动依赖和平台差异，不作为主 importer |
| GDI+/System.Drawing 科研绘图 | DPI、GPU、三维和交互能力不符合目标 |
| Embedded Python 作为正式算法真源 | 增加部署和双语言一致性问题 |
| Electron 作为整套桌面壳 | 本项目明确追求原生 Windows |
| Preview/Experimental SDK | 稳定性与回归风险不可接受 |
| 高阶自动平滑 | 容易造成科学曲线失真 |
| JavaScript 决定 BRAF 结果 | 破坏单一算法真源 |
| 用户公式直接 eval | 安全和可复现性差 |

# 33. 测试架构

## 33.1 Unit Test

Analysis Engine 的每个数学函数必须独立测试。

重点边界：

```text
dr' = 0.399999 -> Negative
dr' = 0.400000 -> Positive
dr' = 0.400001 -> Positive
```

```text
RR = 0.009999 -> PASS
RR = 0.010000 -> PASS
RR = 0.010001 -> FAIL
```

## 33.2 Golden Dataset

建立不可随意修改的验证数据：

```text
GD-001 Positive
GD-002 Negative
GD-003 NTC Fail
GD-004 Positive plateau Fail
GD-005 WT Fail
GD-006 LQC Fail
GD-007 Replicate conflict
GD-008 Missing endpoint
GD-009 Fm <= Fb
```

每次 Release 对：

- Fb；
- Fm；
- Y；
- dr'；
- per-well call；
- final call；
- Run State

全部做数值回归。

## 33.3 Import Fixtures

至少保存：

- 当前真实 XLSX；
- 双通道 XLSX（获得后加入）；
- 带 metadata 前缀；
- 多 Sheet；
- 空白行；
- 合并单元格；
- 缺失时间点；
- 非 UTF-8 TXT；
- legacy XLS。

## 33.4 Visualization Test

- 2D scatter screenshot regression；
- scattergl screenshot regression；
- 3D default camera screenshot regression；
- 3D custom camera restore；
- point click -> PointId；
- 3D distance exact value；
- PlotSpec save/open roundtrip；
- WebView2 runtime upgrade smoke test。

## 33.5 Report Test

PDF 需要测试：

- 中文字体；
- 多页表格；
- 2D SVG/PNG；
- 3D PNG；
- 页边距；
- Method Version；
- 文件 hash；
- 不出现裁切或空白图。

# 34. CI/CD

推荐 pipeline：

```text
restore --locked-mode
 -> build Release
 -> analyzers
 -> unit tests
 -> golden tests
 -> import integration tests
 -> visualization smoke
 -> report generation smoke
 -> benchmark comparison
 -> package MSIX
 -> sign
 -> archive symbols + SBOM + test report
```

分支策略保持简单：

```text
main
feature/*
release/*
hotfix/*
```

正式版本标签：

```text
v1.0.0-research
v1.0.1-research
```

Method Version 与 App Version 分开管理。

# 35. Packaging 与部署

## 35.1 第一选择：MSIX

优点：

- 干净安装/卸载；
- 包身份；
- 可签名；
- 企业部署更规范；
- Windows App SDK 依赖管理路径清晰。

## 35.2 内部科研测试

CI artifact 可以使用 self-contained WinUI build，减少测试机前置依赖。

## 35.3 WebView2

科研阶段建议 WebView2 Evergreen Runtime：

- Windows 11 通常已有；
- 安装器仍要做 runtime presence check；
- 离线实验室提供官方 offline installer；
- 应用只使用经过验证的 baseline API；
- SharedBuffer 等可做 feature detection 和 JSON fallback。

Plotly.js 不使用 CDN，而是把经过验证的固定版本随软件打包，所以科研图形行为不会因为网络 CDN 自动升级。

未来如果进入严格受控临床版本，可以重新评估 WebView2 Fixed Version 或制定 Evergreen Qualification Matrix。

# 36. 编译与发布设置

Release 重点：

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <DebugType>portable</DebugType>
  <Optimize>true</Optimize>
  <PublishReadyToRun>true</PublishReadyToRun>
  <PublishTrimmed>false</PublishTrimmed>
</PropertyGroup>
```

第一版不激进启用 trimming/NativeAOT。理由不是技术老旧，而是 WinUI/XAML/WebView2/反射型第三方库组合下，稳定性优先。待有完整 AOT compatibility test 后再评估。

# 37. 诊断与可观察性

软件内置 Diagnostics 页面，显示：

```text
App Version
Method Version
.NET Runtime
Windows App SDK
WebView2 Runtime
Plotly Bundle Version
OS Build
CPU Architecture
Project Safe Display Name / Project-relative Managed Entry
Database Schema Version
Last Autosave
```

支持一键导出 `Support Bundle`：

```text
logs/
app-info.json
project-manifest-redacted.json
crash-redacted.json
```

默认不包含原始实验数据。所有日志、manifest、crash event 和 app info 在打包前再次执行路径扫描与脱敏；Support Bundle 中严禁出现绝对路径、用户主目录、系统临时目录或外部源文件系统位置。若用户明确选择附加原始实验数据，仍只使用安全的工程包相对 entry。

# 38. 数据完整性

- 源文件保存 SHA-256；
- Project 打开时可验证 raw file hash；
- Method Snapshot 保存 hash；
- Report 保存分析结果 hash；
- Audit Event append-only；
- Project 保存采用原子替换；
- SQLite 外键开启；
- 关键写操作全部 transaction。

# 39. 数据隐私

第一阶段只要求 Sample ID，不存姓名、身份证、住院号。

如果研究团队后续导入敏感字段：

- 默认本地保存；
- 不上传云；
- WebView2 禁止外网；
- 日志不记录内容；
- Export 只提示安全文件名或项目相对托管 entry；操作系统文件选择器可以独立显示目标位置，但应用 UI、日志和 DTO 不显示或记录目标绝对路径。

# 40. 无障碍与现代 Windows 体验

- 键盘可以完成全部主要流程；
- UI Automation Name 完整；
- 状态不能只用红/绿；
- 支持系统缩放；
- 支持触摸板和鼠标；
- 3D 图必须有“Reset Camera”；
- Tooltip 不能承载唯一信息；
- Error 提示给出下一步动作。

# 41. MVP 范围

## 必须完成

```text
WinUI 3 Shell
Project / Run
XLSX/CSV/TXT import
FAM/ROX mapping
96-well Plate Map
Calibration
NTC QC
Positive plateau QC
WT QC
LQC QC
Sample dr'
replicate consistency
BRAF positive/negative
View Calculation
2D plots
3D scatter/line
2D/3D distance
Formula Builder
safe smoothing
Project save/open
Excel export
PDF report
PNG/SVG export
Golden tests
```

## 延后

```text
384-well polished UI
LIS/HIS
patient identity
clinical electronic signature
cloud account
instrument live communication
AI diagnosis
advanced statistics package
plugin marketplace
arbitrary Python scripting
```

# 42. 未来扩展到其他 SNV

架构目标：

```text
Analysis Engine
   ^
   |
Method Definition
   |
   +-- BRAF V600E
   +-- KRAS G12A
   +-- KRAS G12C
   +-- EGFR L858R
   +-- future SNV
```

Method 可以定义：

- required channels；
- control roles；
- endpoint；
- normalization；
- thresholds；
- QC gates；
- result labels；
- plotting templates。

第一版不要做可执行插件系统，先用受控 schema。这样更安全，也更容易验证。

# 43. 默认 Method JSON 建议

```json
{
  "id": "BRAF-V600E-Research",
  "version": "1.0",
  "endpointMinutes": 120.0,
  "endpointToleranceSeconds": 15.0,
  "plateau": {
    "startMinutes": 90.0,
    "endMinutes": 120.0,
    "relativeRangeMax": 0.01
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
  }
}
```

`endpointToleranceSeconds=15` 是软件工程默认值，不是已确认实验规则。产品负责人可在首轮真实双通道数据验证后调整并锁定 Method Version。

# 44. 验收标准

## 44.1 科学正确性

- 所有 Golden Dataset 数值与人工基准一致；
- 阈值边界正确；
- 重复孔冲突绝不被均值掩盖；
- 图形平滑不影响正式结果；
- 3D 工具不改变 BRAF 二维 dr' 定义。

## 44.2 稳定性

- 典型数据连续导入 100 次无资源泄漏异常；
- WebView2 图表页面重复创建/销毁无明显内存持续增长；
- 项目保存过程中强制终止后原工程仍可打开；
- 非法 Excel 不导致整个应用崩溃；
- PDF export failure 不破坏 Run 结果。

## 44.3 UI

- 常规页面 60 Hz 显示无明显卡顿；
- 大表滚动不创建全部 cell 控件；
- 导入和分析期间 UI 保持响应；
- 3D orbit/zoom 可用；
- 高 DPI 不重叠；
- 深色模式文本对比度合格。

# 45. 开发阶段建议

## Phase 0 - Technical Spike

1-2 周内验证：

- WinUI 3 + .NET 10；
- XLSX streaming importer；
- WebView2 + local Plotly；
- 3D scatter；
- SharedBuffer；
- SQLite；
- WebView2 PDF。

Spike 的目标不是做漂亮 UI，而是提前消灭技术不确定性。

## Phase 1 - Analysis Core

- Domain；
- Method；
- Calibration；
- QC；
- Sample；
- Golden Test。

## Phase 2 - Guided UI

- Import；
- Plate；
- Calibration；
- Result；
- View Calculation。

## Phase 3 - Explorer / 3D

- VirtualDataSheet；
- 2D；
- 3D；
- Formula；
- Distance；
- Smoothing。

## Phase 4 - Project / Report / Hardening

- `.fluoaxisproj`；
- PDF；
- crash recovery；
- installer；
- performance；
- accessibility；
- release CI。

# 46. Definition of Done

一个功能只有满足以下全部条件才算完成：

```text
[ ] requirement implemented
[ ] unit tests
[ ] error path tested
[ ] cancellation handled if async
[ ] logging added where necessary
[ ] no UI-thread blocking
[ ] no duplicate formula in ViewModel/JS
[ ] project save/open roundtrip
[ ] accessibility label
[ ] dark/light mode
[ ] reviewer code review
[ ] no preview dependency
[ ] documentation updated
```

# 47. 关键代码审查规则

禁止 PR 出现：

```text
Excel.Application
Microsoft.Office.Interop
System.Drawing based scientific charts
Thread.Sleep in UI path
.Result / .Wait() on async UI code
dynamic for core domain data
JavaScript eval
C# scripting for user formula
hard-coded A1/A2 role inside engine
hard-coded row index for 120 min
hard-coded 16 points for 90-120 min
Math.Round before threshold comparison
catch (Exception) { }
```

# 48. 参考实现的依赖治理

建议仓库根目录建立 `DEPENDENCIES.md`，每个外部包写明：

```text
Package
Purpose
License
Pinned Version
Owner
Upgrade Policy
Security Source
Replacement Plan
```

特别关注：

- Windows App SDK；
- WebView2；
- Plotly.js；
- ExcelDataReader；
- CsvHelper；
- 任何 PDF / export 库。

# 49. 技术选型依据 - 截至 2026-08-10

以下外部来源用于说明“当前现代技术栈”的选择。工程启动时应再次检查稳定版 patch，但不改变本文架构原则。

1. Microsoft .NET Support Policy：.NET 10 为 LTS，2025-11-11 发布，支持到 2028-11-14。  
   https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

2. Microsoft .NET 10 What's New：.NET 10 是 LTS，并包含当前 Runtime/JIT 等改进。  
   https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview

3. Microsoft Windows Apps：对于新的原生 Windows 应用，推荐 Windows App SDK + WinUI 3。  
   https://learn.microsoft.com/en-us/windows/apps/

4. Windows App SDK Downloads：本文编写时 Stable 列表包含 2.3.1，发布日期 2026-07-16。  
   https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads

5. CommunityToolkit.Mvvm：Microsoft/.NET Foundation 维护的现代 MVVM Toolkit，支持 Source Generators。  
   https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/

6. .NET Generic Host：统一 DI、Logging、Configuration、Lifetime。  
   https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host

7. Open XML SDK：Microsoft 官方 Office Open XML SDK；大表可使用 SAX/streaming 路径。  
   https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/how-to-parse-and-read-a-large-spreadsheet

8. Microsoft.Data.Sqlite：轻量 SQLite ADO.NET provider。  
   https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/

9. WebView2：在原生 Windows 应用嵌入受控 Web 技术，官方提供 WinUI 3 sample。  
   https://learn.microsoft.com/en-us/microsoft-edge/webview2/landing/

10. WebView2 Shared Buffer：可把 shared-memory buffer 提供给 script 作为 ArrayBuffer。  
    https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2sharedbuffer

11. WebView2 PrintToPdf：可把本地 HTML Report 直接输出 PDF。  
    https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/print

12. Plotly.js：`scattergl` 使用 WebGL；3D charts 使用 WebGL/GPU。  
    https://plotly.com/javascript/  
    https://plotly.com/javascript/3d-charts/

13. Windows Community Toolkit archived DataGrid：旧 DataGrid 已归档，新项目不应把旧 7.x 控件作为核心依赖。  
    https://learn.microsoft.com/en-us/dotnet/communitytoolkit/archive/windows/datagrid

14. Windows App SDK self-contained deployment：支持把 Windows App SDK dependencies 随应用部署。  
    https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps

# 50. 用户提供资料基线

本规范同时依据以下项目材料形成：

- `意见汇总(2).docx`：最终实验规则、Calibration、NTC 与阴性质控关系、重复孔规则、WT 阈值、LQC、科研软件第一阶段定位等。
- `意见汇总(1).docx` / `意见汇总.docx`：数据格式、相对极差定义、历史规则收敛过程。
- `20260516 补 1S-10 nM Comp-1 nM.xlsx`：真实仪器宽表结构和时间序列输入样例。
- `中国化学会 20260413(2).pptx`：双通道参数与 dr' 科研背景、低丰度和多 SNV 扩展背景。
- `核心原理部分.pdf`：相关方法原理和荧光归一化背景。

# 51. 最终冻结结论

第一阶段正式开发应按以下一句话执行：

> 使用 .NET 10 LTS + Windows App SDK Stable + WinUI 3 构建原生 Windows 科研工作站；以纯 C# 确定性 Analysis Engine 作为唯一算法真源；以 Open XML 流式导入、SQLite 工程存储、WebView2 + 固定本地 Plotly.js 实现现代 GPU 二维/三维科研可视化；以安全 Formula Engine、虚拟化 Data Sheet、Golden Dataset 和原子工程保存保证可维护性、性能和稳定性；绝不依赖 .NET Framework、COM Excel、旧归档 DataGrid、GDI+ 科研绘图或任意脚本 eval。

该架构既足够现代，也避免为了“新”而使用 Preview/Experimental 组件。其核心原则是：**稳定版优先、版本锁定、算法纯净、UI 可替换、图形 GPU 化、三维完整、数据可追溯、失败显式、持续回归。**
