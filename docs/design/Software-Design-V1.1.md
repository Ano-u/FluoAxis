# FluoAxis 软件设计 V1.1

| 项目 | 约定 |
|---|---|
| 状态 | 工程设计与实现对照；工程工作流已扩展，科学与整机发布门禁未关闭 |
| 日期 | 2026-10-07 |
| 决策 | 保留 Method/Data Contract 草案，先完成工程设计与验证；不代替科学批准 |
| 需求依据 | `BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md`、`plan.md` |
| 科学依据 | `docs/method/Method-Specification-V1.0.md`、`docs/method/Data-Contract-V1.0.md` |
| 关联决策 | `docs/adr/ADR-001-toolchain.md`、`docs/adr/ADR-002-plot-renderer-and-export.md` |
| 完成度证据 | `docs/design/Requirements-Completion-20261006.md`；本轮命令、数量、测量与限制见 `docs/feature/FT-20261007-007-engineering-workflow-completion.md` |

## 1. 目的与交付边界

FluoAxis V1 是 Windows 科研用途双通道荧光分析工作站。Guided Analysis 把原始 FAM/ROX RFU 转换为可追溯的 Calibration、Control QC、Sample Analysis 和 INVALID 结果；Data Explorer 负责显示与研究派生计算。正式判读只由确定性 C# Analysis Engine 产生，UI、PlotHost 和导出器消费不可变结果，不能重新计算正式 QC、阈值或最终结论。

产品界面和报告持续显示“仅供科研使用，不用于临床诊断”。首版不保存患者身份字段，不推断良恶性、不推荐治疗。当前 UI 受控标签只对应 legacy BRAF 阳性/阴性词汇，并非任意 Method 自定义标签能力；其他标签需要版本化契约和批准。计算失败、对照失败和重复孔冲突必须呈现原因及建议动作，不能渲染为阴性。

本文的“当前”仅描述当前工作树已接通的实现。“目标”与“拟新增”是后续实施合同，必须经契约、测试和迁移变更落地后才能宣布可用。科学参数维持草案：端点容差、平台最小点数、控制孔基数、NTC 区间开闭、分母下限、LQC 预期等未决项均保留原状态。工程开发和合成证据验证继续推进；签字 Method、真实仪器 fixture 和双批准 Golden 是科学发布门禁。

## 2. 当前基线与目标差异

| 范围 | 当前实现 | 完整工程目标 |
|---|---|---|
| 桌面工作台 | 中文响应式 Guided；项目分页重开；矩形批量孔板、完整 Sample 元数据、20 步 undo/redo、dirty 确认、分列结果 | 多 Run、草稿持久化、完整键盘/辅助技术与用户流程验收 |
| 前后端 | 修改 Gateway v3；保存 evidence、科学点分页、Raw 双通道页；report/package/view Infrastructure 能力；revision/hash 校验 | 通用能力发现、跨版本完整兼容矩阵 |
| 正式分析 | Draft Method 的 Calibration→Control QC→Sample；已执行阶段输入身份、端点/源坐标、窗口点、trace、INVALID 原因持久化 | 真实仪器科学验收；失败前未执行的阶段和历史缺项保持真实，不补造 |
| 桌面存储 | 固定根 SQLite/WAL、独占租约、单操作槽、重启恢复、会话标记与启动检查、技术诊断 | 旧随机会话迁移、清理、断电恢复和完整发布资格 |
| 持久化/归档库 | schema 3；受检 schema 2 迁移；单 Run 包含源/可选 Explorer 视图、身份/hash/输入证据验证 | 任意历史版本不承诺；恢复/断电/大包/离线发布验收 |
| Explorer | 25,000 行真实双通道页、虚拟表/手工表、列/单位/过滤排序、覆盖/DSL/平滑、2D/3D/误差条、测距与联动、视图保存 | 百万行通用表、参考机性能、完整 Windows 操作验收；固定相机需求偏差 |
| 输出 | 冻结草稿 HTML/XLSX/PDF、端点 SVG/代表孔 Raw 曲线/Explorer PNG 附图、hash；PNG/适用 SVG/离线 HTML 保存 | 附图最终回归及原生PDF视觉、批准科研报告、多设备图形/字体及发布资格 |
| 验证 | 合成算法、候选 Golden、契约/存储/路径/PlotHost 测试 | 真实数据、独立 oracle、Windows UIA/DPI/High Contrast、故障注入、性能与发布证据 |

当前 Run 的已提交内容保存在固定桌面工作区，重开不重算；便携包、技术草稿报告与 Explorer 视图可保存/打开。Explorer 的真实数据与独立手工研究表均不改写 Raw RFU；演示入口不作为实验结果。旧随机会话目录不自动迁移，源副本自动保留/清理策略仍待实现。

## 3. 分层、所有权与数据流

| 层 | 唯一职责 | 不得承担的职责 |
|---|---|---|
| `src/FluoAxis.App/` | WinUI 组合根、窗口生命周期、依赖注入 | SQL、正式公式、持久化外部文件路径 |
| `src/FluoAxis.Presentation/` | ViewModel、导航、编辑草稿、显示状态；只依赖 Contracts | 分配业务 Run 状态、直接调用 Domain/Analysis/Persistence |
| `src/FluoAxis.Contracts/` | 版本化命令、查询、DTO、错误、能力和进度 | 操作系统文件句柄、机器文件路径、可执行算法对象 |
| `src/FluoAxis.Application/` | 用例、Run revision、状态机、幂等与事务边界 | 具体 IO、UI 控件、绕过端口的存储实现 |
| `src/FluoAxis.Domain/` | Run、Method Snapshot、Well/Plate 和数值输入不变量 | UI、数据库、WebView2、导入格式 |
| `src/FluoAxis.Analysis/` | 纯确定性计算、结果和 Calculation Trace | IO、时钟驱动的数值、并行非确定性归约 |
| Import / Infrastructure | 不可信文件检查、流式读取、受管源与平台边界 | 静默补缺值、插值或修改 Raw RFU |
| Persistence / ProjectFormat / Reporting | SQLite、归档迁移、快照渲染 | 从活跃 ViewModel 取数据或重算结论 |
| PlotProtocol / Visualization / PlotHost | 有界 PlotSpec、本地渲染、精确 Point ID 交互 | 正式判读、路径、数据库句柄、host objects |

正式数据流固定为：文件选择器 → Infrastructure managed copy → 有界 Import/映射验证 → Run 事务提交 → immutable Analysis input → Analysis Engine → 结果/trace 同一事务持久化 → Run Snapshot → UI/Report。展示派生数据从 Raw RFU 分支生成，永远不回流到正式 Analysis input。

2026-10-09 发布边界补充：PlotHost 和 PDF WebView 统一通过 Infrastructure 提供的环境工厂创建，在平台应用数据根下保存浏览器状态，不写 EXE/MSIX 目录。PlotHost 允许 Plotly 的动态内联样式，脚本仍仅 self，connect/object 仍禁用。图形保持 ≥320 逻辑像素，短窗口滚动查看；正式数值不变。MSIX 由锁定 MakeAppx 构建，先验证精确发布文件清单，再 pack/unpack 并逐文件比对。当前无签名证书，工程包构建与正式分发资格分别记录，见 Release-Qualification-Guide 和 FT-20261008-006。

所有项目拥有的路径都是 repository-relative、workspace-relative 或 archive-relative。Infrastructure 仅在边界调用内从批准的根解析路径，操作结束后不保留外部选择器路径。Contracts、日志、审计、报告和归档仅保存 display name、size、SHA-256、managed entry 与逻辑 worksheet/row/column/point identity。应用正确性不依赖 cwd。

## 4. Guided Analysis 页面合同

### 4.1 工作区结构

当前视觉层级按 `docs/feature/FT-20261008-002-workbench-visual-hierarchy.md` 实现：项目名/dirty 提示常驻，切换/新建/工程包在项目管理折叠区；当前阶段使用青蓝边框，流程/QC成功为绿色文字，错误和建议紧邻摘要，样本阳性/阴性不使用红绿作价值判断。侧边方法hash、逐孔完整trace按需展开，查看计算会主动展开对应trace。Light/Dark/HighContrast共享语义资源，动态控件从所在窗口的ThemeResource获取颜色。

报告页按运行核对、快照复核、输出三层排列，输出操作固定页底；同Run修订或附图变化后旧快照提示失效。Explorer把坐标显示、派生处理、表格整理分组，图形导出收纳于更多菜单；高级设置受可用高度约束，避免将工具栏/状态移出窗口。

本节是完整页面的目标合同，当前已实现范围以 §4.2 第二列为准。

左栏呈现七个阶段及其“未开始/可操作/完成/失败/不可用”文本标记；中栏呈现阶段编辑器、结果与当前动作；右栏固定呈现项目/Run、revision、Method ID/version/hash、RUO、数据来源和阶段问题。颜色是辅助线索，不能是状态的唯一编码。

当前顶部提供新项目名称、新建、已保存项目选择与打开，以及每页 50 项的上一页/下一页；目录接口单页最多 100 项。启动先读取目录，有记录时打开首个保存 Run，无记录时显示空工作区，不静默新建替代历史内容。

导航允许查看已有阶段证据，修改动作取决于后端 `allowedActions`。同 Run/revision/mapping 的失败刷新保留孔位草稿；切换、重开和关闭先确认未提交编辑，丢弃后恢复已提交映射/角色。草稿尚不跨进程持久化。复核已分析 Run 不隐式重算；“从此文件重新设置”复用同一 managed source/hash 建立新项目/Run，保留旧 Method 和结果，目前不保存单独的父 Run 关系字段。

用户已授权无歧义时自动识别并分析。顶部主入口从文件开始，识别器只读验证源大小/SHA、通道/孔标签及显式时间单位，ViewModel 顺序调用既有五个 Gateway 命令，每步刷新 revision 并遵守 allowedActions。固定方案默认 A1=Positive100、A12=NTC、H1=WT、H12=LQC、B2–G11=Sample、其余 Ignore；只在没有已存方案时初始化。方案可编辑保存，不复制旧 SampleId。完整文件样本元数据优先保留；只含孔号时用孔号和 replicate 1，显式元数据残缺或冲突则停下复核。无科学公式进入识别器或 UI。具体合同、边界和最终证据见 `docs/feature/FT-20261008-004-assisted-import-analysis.md`。

### 4.2 各页面的当前范围与目标

| 页面 | 当前范围 | 下一步目标与输入/输出 |
|---|---|---|
| Import Preview | 单文件选择/拖放、受管 staging、XLSX/XLS/文本候选、安全源身份及自动识别；显式解析参数/可保存孔位方案 | 明确格式和方案可自动提交；歧义、缺项和超识别预算转人工；原生 picker 完整交互、大表/取消、真实格式资格待验收 |
| Data Mapping | FAM/ROX 独立工作表、表头/时间/孔列/结束行，支持上下分区和无候选手填 | 实际数据逐字段核对与 UIA；UI 行列 1-based，契约 source index 0-based；source identity 与 mapping/options 一并保存 |
| Plate Map | 8×12、矩形/键盘选择、完整单孔/批量元数据、20 步 undo/redo、Explorer 联动 | 实际满板/辅助技术验收；后端验证完整 assignment，Sample ID 必填且重复序号正整数 |
| Calibration | 已保存 Positive 窗口 count/min/max/mean/RR/Fm；NTC own-Fb/范围/失败点与来源；计算点分页 | 真实科学输入与失败现场复核；历史缺项不补算 |
| Control QC | WT 每孔双通道端点/Y、参考均值/判据；LQC 逐孔 trace/来源与Engine均值，Sample 提交继续保留 | 负向一致组保留失败均值、RunInvalid；混合组均值为空；真实科学验收，未计算值不显示默认零 |
| Sample Analysis | 逐孔/重复组、INVALID 原因、Trace ID、source/mapping/input 身份和双通道来源 | 真实数据/报告验收；冲突组 call/mean 为空 |
| Results / Export | G17 全精度 viewer、阶段来源与点分页、冻结 HTML/XLSX/PDF/端点/Raw 图和可选Explorer附图、包与图形保存 | 正式批准、附图原生渲染、多设备/字体/长报告验收；显示精度不改变结果 |

映射和孔板的字段级错误定位到控件、孔号或通道，并保留用户编辑草稿。错误清单可以定位/高亮字段；修正后重新提交完整命令。严重错误以稳定错误码及推荐动作呈现，UI 不拼接底层异常信息。

### 4.3 统一页面状态

| 状态 | 页面行为 | 数据与操作规则 |
|---|---|---|
| Empty | 说明尚缺的前置条件，提供一个明确动作 | 未计算值显示“未计算”，不用阴性占位；合法 control-only Run 可完成，明确无样本，不能伪造判读 |
| Ready / Editing | 展示已提交摘要和独立编辑草稿 | enabled = 后端 action 可用且当前页面不 busy；UI 输入校验只用于反馈，后端仍验证 |
| Loading / Running | 阶段名、已处理量/总量或 indeterminate、取消入口 | 每 Run 一项修改；不显示伪造百分比；前次结果只读并标注 revision |
| Success | 更新阶段数据并读取新的 Run Snapshot | 采用最新 revision 和 allowedActions，不猜测下一业务状态 |
| Scientific FAIL / INVALID | 测量值、判据、孔/通道、原因、建议 | 保留失败证据；阻断依赖阶段；INVALID 永不映射为阴性 |
| Operation Error | 安全文案、字段位置、建议和 correlation ID | 失败 envelope 不携带可用结果；保留上个已提交快照；明确重试或重新选择源 |
| Cancelled | 停止 busy，说明已取消或操作已完成 | 取消可能与 commit 竞争，用独立有界读请求刷新真实 Run；读取失败显示“状态待刷新”，不能假定回滚 |
| Stale Revision | 保留草稿，提示后端已有更新 | 刷新并显示冲突，基于新 revision 重新提交；不自动覆盖、不复用旧命令写新版本 |
| Stale Response | 后台响应不更新当前页 | 用 Run ID + 请求 generation + revision 过滤旧响应；取消旧查询并释放引用 |
| Capability Unavailable | 说明具体能力与可继续工作 | 无业务提交；如 WebGL/矢量导出不适用、超出页面预算或未支持版本，明确原因和回退 |

键盘目标：Tab 顺序跟随视觉顺序，孔板可用方向键移动，批量编辑可用键盘完成；控件具备 AutomationName，表格有可读表头及状态。覆盖 125/150/200% DPI、High Contrast、Narrator、深浅色和窗口缩放，禁止裁掉判读与推荐动作。现有 AutomationName 不能代替 UIA 与人工辅助技术验收。

## 5. 科学执行合同与数值保护

### 5.1 固定计算顺序

1. 校验源/映射/孔位/双通道和 Method 身份，选择实际端点及窗口。
2. 每个 100% Positive 孔、每通道用 90–120 min 实际点计算 `RR=(max-min)/mean`；草案 `RR <= 0.01` 且点数满足 Method 才通过。任一关键孔失败则 Calibration 失败，不能排除后继续。
3. 每孔通过的窗口均值得到 `Fm_i`；所有 Positive 通过后，按通道确定性顺序求 `Fm_run`。
4. 每个 NTC 孔取自身 endpoint `Fb_i`，用 `(F(t)-Fb_i)/(Fm_run-Fb_i)` 检查 0–endpoint 全课程实际点；草案区间为 `[-0.05, 0.05]`。全部 NTC 独立通过后才形成 `Fb_run`。
5. WT 各孔使用 Run Fm/Fb 计算 endpoint Y，草案要求 `mean(Y_FAM)<0.05` 且 `mean(Y_ROX)<0.05`，组均值形成二维参考点。
6. LQC 每孔计算二维 dr'、单孔方向、重复孔一致性及弱阳性预期。Engine 仅在方向一致后计算组均值；一致阴性仍保存该均值但 QC FAIL/RunInvalid，混合方向的均值为空；只有通过才允许 Sample。Application 不另算均值，历史缺项不回填。
7. Sample 每孔独立归一化和计算二维距离。`dr' >= 0.4` 为阳性侧；方向混合为 INVALID、组 call/mean 留空；方向一致后才给组结论与展示均值。
8. 数值、判据、来源和 Method/algorithm 身份组成不可变证据，在一次提交中与状态/revision 一致保存。

这些数值描述当前 draft，不构成新的科学批准；批准 Method 的规则变更需要新版本、Golden 影响分析和历史兼容说明。“分母过小”的科学阈值尚未签字，当前只要求 `Fm-Fb > 0` 且有限，不擅自添加 epsilon。

### 5.2 输入、边界和失败语义

- 正式分析只接收 Raw RFU；平滑、LOD、截断负值、派生公式列和显示舍入不得进入输入。均值顺序求和，分类比较使用原始 binary64 值。
- 阶段前验证上一阶段状态、Method hash 和所需量齐全。Run Fm/Fb 缺通道、非有限或非法分母返回结构化失败，不用默认零值补齐。
- 一孔 FAM/ROX 必须属于同一 Run、相同 Well、相同有效时间轴；缺通道、重复孔、跨 Run 混用不能静默跳过。Sample 缺元数据不能从结果列表消失。
- 导入拒绝重复/倒序时间、非有限数值、缺失必需单元格和孔集合不一致。ticks 转换先验证可表示范围，含 binary64 中 `long.MaxValue` 舍入到 `2^63` 的边界；错误保留安全逻辑行列。
- 端点采用闭区间最近实际点，等距取较晚点，不插值。平台点数不足、均值非正、空 NTC course、归一化/距离/累积均值溢出都失败关闭。
- 全精度值和原判据持久化；viewer 可查看原始值、运算符和 threshold。`0.4`/`0.01` 测试以实际 binary64 路径验证，不能宣称十进制精确表示。

### 5.3 旧 Method 兼容

`Replicates.RequireClassificationConsistency` 是为旧快照保留的字段。历史 `false` 仍可读取并参与原 canonical hash，UI 不提供关闭开关；Analysis 对 LQC/Sample 始终强制方向一致性。不能通过把旧值改为 `true` 来修复，否则会改写 hash 和历史身份。

`RoleCardinalitySpec` 历史 `0` 同样原样读写/hash；执行时必需 NTC、Positive、WT、LQC 各至少一孔，即有效最小值为 `max(1, stored minimum)`。正数高于一时严格尊重该值。旧结果按保存状态可读；重算缺孔输入返回结构化错误，不使旧工程打开崩溃，不把旧缺孔记录悄悄升级为成功。

Algorithm version 与 Method version/hash 分开记录。改变可观察数值或判读时必须审查 algorithm version 与旧结果重现影响；坏输入拒绝边界的修复也记录原行为。旧 trace 不因新代码载入而重算；显式重算建立新的 Run/analysis revision，保存 derived-from 身份。

## 6. Gateway、保存证据与报告契约

### 6.1 当前 v3 合同

`IFluoAxisGateway` 当前提供 CreateProject、InspectImport、ConfirmMapping、AssignWells、RunCalibration、RunControlQc、AnalyzeSamples、GetRun。修改 Run 的命令携带 `requestId/runId/expectedRunRevision/actorId/issuedUtc/contractVersion`；源检查只读。ConfirmMapping 携带完整 source identity 和 FAM/ROX mapping，是导入提交边界。

`GatewayResult<T>` 统一返回成功值或非空错误列表及 correlation ID。QC FAIL/INVALID 是带证据的科学结果；IO、输入或数值无法完成计算是失败 envelope。错误码稳定、文案可本地化。相同 request/payload 重放已提交结果且不新增 revision；同 request ID 的不同 payload 必须拒绝。

独立 `IRunEvidenceGateway.GetRunEvidenceAsync(GetRunQuery, CancellationToken)` 不扩大 v3 修改接口。它读取保存的项目名、Run Snapshot、mapping/options、assignments、calibration/control/sample、traces 与科学阶段摘要，不扫描完整 Raw 时程序列、不调用 Analysis、不重放命令。摘要与 provenance 可携带执行时冻结的 Raw 端点值。读取前后检查 revision/state/Method hash；校准、阶段与 trace 身份失配或损坏时返回结构化错误。

`RunEvidenceDto.ScientificEvidence` 记录已执行的 Calibration/Control/Sample 阶段。每阶段包含 source/mapping/input/Method SHA-256、algorithm 与输入 revision，以及已计算逐孔值和端点原始坐标；Positive 窗口与 NTC course 另有实际计算点 digest。`CalculationTraceDto.Provenance` 同时保存 FAM/ROX 端点和错误码。完整字段可标记 `storedComplete`；旧记录缺项仍为 `storedPartial`/`unavailable`，不从当前 Method 或 Raw 重建。完整性只表示工程来源字段，不表示科学批准或未执行阶段已经完成。LQC trace 在样本提交时合并保留，样本 INVALID 错误码转为可见 issue 与建议动作。

`IScientificEvidenceGateway.GetScientificPointPageAsync` 按 Run/stage/well/channel 读取已保存窗口点，每页 1–250 点，返回原 point index/ticks/Raw RFU、已保存归一化值、源行列及 NTC 带内判定。它与阶段摘要分离，避免 viewer 加载完整 course；空/错误/取消/陈旧 revision 显式处理。用户可用 G17 查看保存值，读取不重新归一化。

### 6.2 当前 Raw、报告与包能力

`IRawSignalGateway.GetRawSignalPageAsync(runId, well, channel, offset, pageSize, token)` 提供 `RunId/Revision/Well/Channel/TotalPoints/Points(PointIndex,ElapsedTicks,Rfu)`。页大小 1–25,000、offset 非负，不静默截断预算；count/page 在同一读事务，前后 revision/state/Method 校验。空 Run/无点为成功空页，不存在 Run 为错误。稳定点 ID 由 Run、孔、通道、PointIndex 构成；不以页内索引代替原索引。

`GetExplorerPairPageAsync` 对同孔两通道按原 point index 和 elapsed ticks 配对，要求点数与 revision 一致。`ExplorerViewDefinitionDto` schema 1 保存数据身份、列/单位、公式/平滑、显示覆盖/手工值、过滤排序/页偏移、轴和可选序列/误差列；定义 hash 与源 hash 绑定，保存/读取均限 4 MiB，唯一临时文件校验后原子替换。可选视图随工程包携带，缺少该条目的旧包保持可读。

`PersistentDesktopStore` 已实现 `CreateReportSnapshotAsync`、`CreateReportExportAsync`、`CreateProjectPackageAsync`、`ImportProjectPackageAsync`；Windows Infrastructure 负责 picker 和外部事务文件写入。`ManagedProjectPackageDto` 仅返回安全显示名、相对 entry、SHA-256、size。`ReportSnapshotDto` 冻结保存证据/软件版本/生成时间/审批状态与 canonical hash；最近一个同 Run/revision 且内容相同的快照被复用，跨格式连续输出不更改 hash。该缓存有界且不承诺跨会话报告 ID 一直相同。

### 6.3 后续扩展合同

以下为现有能力之上的后续扩展，不是当前字段或版本范围的承诺。实施前先写 success/failure/empty/cancel/stale fixture，再做 fake/real conformance。当前报告和单 Run 包能力以 §6.2 为准。

| 拟新增入口 | 请求关键字段 | 返回合同 |
|---|---|---|
| evidence 游标分页扩展 | `requestId, runId, revision, stage, cursor, pageSize, contractVersion` | 当前科学点已按 offset 分页；未来大规模检索增加固定 revision 游标，不把拟议字段当作现有接口 |
| 单条 trace 检索 | `requestId, runId, revision, traceId, contractVersion` | 当前完整 trace 随有界 Run evidence 读取；未来按 ID 查询已保存值及来源，不重算 |
| `CreateReportSnapshot` | 标准 mutation 身份 + `templateId/templateVersion, locale, displayPrecisionProfile` | `snapshotId, snapshotHash, schemaVersion, runId, revision, createdUtc, completeness, reportEligibility`；冻结指定 revision，不改变科学结果 |
| `GetReportSnapshot` | `requestId, snapshotId, contractVersion` | 不可变摘要、source/Method/algorithm/software 身份、章节/trace 索引、RUO、失效原因；大表分页 |
| `ExportReport` | `requestId, snapshotId, snapshotHash, format, renderProfile, contractVersion` | operation ID；完成后 artifact ID、display name、relative managed entry、content hash/size、render version；外部保存由 Infrastructure picker 处理 |
| `SaveProject` | `requestId, projectId, expectedProjectRevision, formatVersion, contractVersion` | package identity/hash、Run revisions、migration/recovery 状态、managed entry；无机器目标路径 |
| `OpenProject` | `requestId, managedPackageIdentity, supportedFormatRange, contractVersion` | project summary、Run 列表、只读/编辑能力、迁移说明、结构化损坏/未来版本错误；不重算 |
| `GetOperation/CancelOperation` | `requestId, operationId, contractVersion` | queued/running/committing/completed/cancelled/failed、stage、processed/total、可否取消、correlation、结果身份 |

source reference 至少包含 `sourceFileHash, relativeEntry, worksheetOrRegionId, row, column, well, channel, pointIndex, elapsedTicks`；只填写实际存在的字段，不把内存索引伪称原 Excel 行号。二维 dr' trace 同时保存 FAM/ROX 端点和 WT reference 的来源集合。

Calibration evidence 覆盖 Positive 窗口 point IDs/count/min/max/mean/RR/Fm_i；NTC 覆盖 Fb_i、端点选择、检查点数、上下界和失败点、Run Fm/Fb 聚合；Control evidence 覆盖 WT 各孔/均值和 LQC 各孔/一致性。trace 引用点集或分页查询，不为每个 trace 复制整条 RFU。

当前技术草稿报告只允许终态 Run，验证保存证据/Method 身份，显示 `draftUnapproved` 及实际 storedComplete/storedPartial/unavailable。冻结内容包含新增来源和有界代表孔信号图；缺 trace 不补造，INVALID 报告隐藏有效样本判读。模板不重算。正式批准报告需要科学与数据完整性门禁；当前输出不被称为批准科研报告。

### 6.4 版本、能力与迁移

contract、DTO schema、Method/hash、algorithm、database schema、project format、report template 各自版本化。可选展示字段可兼容新增；必需输入、字段含义、状态词汇或安全行为变化需要契约升级。后续提供 capabilities 与支持版本范围；不支持版本显式拒绝，不自动降为“最接近版本”。

当前数据库 schema 3 从 schema 2 增加源坐标、结束行/导入选项、阶段证据和点表。便携 schema 2 包先比对白名单旧结构，再在隔离副本迁移、按当前规则验证后合入；保留旧 Method hash、结果和缺失 provenance，不重算。未知结构/未来版本拒绝。通用多版本迁移登记、只读元数据预览与回退矩阵仍是后续设计，不冒称已经存在。

## 7. 持久项目、恢复与安全边界

当前 `DesktopWorkspace` 在平台已知应用数据根内使用固定相对命名空间 `FluoAxis/desktop-v1`，`PersistentDesktopStore` 管理 `workspace.db`、`workspace.lock` 和相对 managed entries。每个 store 持有独占文件租约和一个操作槽；初始化、目录查询、staging、Gateway、证据读取、报告/工程包/Raw 查询、图形导出和关闭共享生命周期。同步 SQLite 工作在 dispatcher 之外执行，同一 store 最多一个工作操作，禁止第二实例同时拥有同一 workspace。

阶段结果、现有 trace、audit、request replay 与 terminal revision 事务提交。普通关闭先取消并等待窗口初始化、用户操作、绘图和导出任务，再排空存储操作槽、释放仓储与租约；不把 packaging checkpoint 作为正常关闭前置条件。重启只读恢复已提交记录，未提交草稿不被宣称持久化。

既往 FT005/FT006 有新建后重启、文件独占释放和独立工作区包往返证据；Windows 嵌套目录原生 rename code 5 仍未解决，复制/便携迁移不能证明该问题已修复。本轮独立工作区新建/picker 已观察，PickerHost 未定位；经工程包验证的 Debug 合成项目在 UIA 树中显示 revision 5、样本阳性/dr′ 0.4、Input/Mapping/Source hash 与两通道源行列。截图和 UIA 不一致，未以该读侧证据宣称完整视觉或点击验收；用户按 Esc 停止后所有 GUI 输入已停止。最新实机条件以 FT007 为准。

便携包使用唯一临时包→关闭→重开校验→原子发布。导出从固定表/列白名单复制所选 Run 到全新 SQLite，纳入该源副本及可选 Explorer 视图，不混入其他 Run 的空闲页。导入支持受检 schema 2→3；验证结构、拒额外 trigger/view、检查数据库/manifest/Method/source/input/计算点 hash，事务合入且拒已有身份。提交前可取消，提交开始后完成事务，避免误删已提交源。外部文件由 Infrastructure 事务保存；旧随机会话不自动迁移。

启动持独占租约检查 `session.active` 并执行 SQLite `quick_check`，记录非正常关闭恢复标记；正常关闭移除本会话标记。诊断页只展示白名单版本/架构/内存/有界目录计数及最多 100 个技术事件。支持 ZIP 仅有诊断 JSON 和说明，JSON 上限 64 KiB，不含项目名、源文件/路径、Raw RFU、trace 或异常堆栈。这是恢复检测与技术诊断，不等于断电/磁盘满恢复资格。

- 扩展名与签名一致；XLSX 使用流式 OpenXML，XLS 使用只读 BIFF/OLE adapter；均不执行公式/宏/链接/对象。文本编码/分隔符/小数文化/时间单位显式保存，旧 null options 保留旧语义，不静默迁移历史输入。
- archive entry 仅安全相对名称，拒绝 traversal、绝对形式、保留名、大小写碰撞、重复、超限与 hash 不符；保存/打开共用验证。
- 用户文本按 HTML/SVG/Excel/PDF 目标编码；Excel 防公式注入；报告禁止外部导航和资源获取。
- Plotly/HTML/CSS/fonts 本地锁版本，WebView2 禁外部导航、新窗、下载、权限、host object；消息校验 origin/type/schema/size/correlation/generation。
- 错误只输出 path-free code 和 safe metadata，中心 redaction 作第二道防线；支持包默认不含原始实验数据。发布审计覆盖生成 manifest、dependency metadata、日志、报告、归档；源文本门禁通过不代表产物已通过。

## 8. Explorer 与报告呈现

Explorer 读取同孔 FAM/ROX 配对页，每页最多 25,000 行，虚拟化 ListView 按需构建行对象。原 point index、ticks 和 Raw 列只读；可创建独立手工表、研究派生列与单元格覆盖。列标签/单位、过滤排序、数值复制粘贴、描述统计及 X/Y/Z 选择均已接通；统计仅覆盖当前筛选页。图形包括 2D 散点/折线/散点加线和真实 3D 散点/折线，可选第二 Y 序列及非负误差 Y 列、linear/log 轴。

`ExplorerExpression` 只支持数值/列引用、算术和 `sqrt/abs/min/max/mean/median/sd/log/ln/exp/pow` 白名单，不支持任意代码、反射、IO 或动态脚本。长度/树深/节点/每页算力均有限；数值错误变为明确缺失值并从绘图排除。Moving Average 与 SG 2/3 阶作用于当前页行索引，使用奇数窗口 3–51，边缘移动完整窗口；不声称按非等间隔物理时间拟合或跨页连续平滑。派生定义带来源 hash 和版本持久化，正式算法始终只读取 Raw。

图点、当前表行与孔板相互定位，保留 Run/well/原 point index/source hash；过滤排序不重编号来源。两点测量调用 C# `PlotDataSnapshot.Measure`，解释为所选显示坐标的几何距离，不是 BRAF dr′。切换源/视图取消旧请求并抑制过期响应，同一时刻只有一项高级计算操作。未保存视图参与 dirty 确认；手工/演示数据不伪装为当前 Run 原始点。

renderer 返回 2D SVG/WebGL 能力；仅适用 SVG trace 导出矢量，WebGL/3D 输出 PNG。图像和离线 HTML 经 Infrastructure 保存选择器写出；HTML 冻结当前接受的 PlotSpec/数据和 hash 校验的本地 Plotly，设 CSP/无网络连接、转义用户文本，明确标记当前页与研究派生。3D 相机固定，原需求的旋转/平移/缩放/reset 尚未关闭；ADR-002 仍须批准取舍。无 WebGL 等失败提供明确状态，数据表和正式结果继续可读。

HTML/XLSX/PDF 技术草稿呈现冻结的项目/Run/revision、软件/Method/algorithm/source/input 身份、映射/孔板、科学阶段摘要、样本/控制结果、原因和 trace。HTML/PDF 另渲染保存的 Y 端点 SVG，以及每类角色代表孔、每通道前最多 500 点的 Raw 曲线（最多 12 条），标注本页和总数；XLSX 保留冻结表格，未宣称含相同嵌图。HTML/CSP、Excel 公式注入防护和 PDF 外部资源隔离持续适用。既往五页原生 PDF QA 属 FT006，不代替当前新增图形/长报表/字体验收。

Explorer 的“报告附图”把当前已接受的真实图形导出成 PNG，构造 `ReportPlotFigureDto` 并冻结进 Report Snapshot。DTO 带 Run ID/revision、Plot ID、caption 与 PNG，限制 1 MiB、每边≤8,192、总像素≤16,000,000；后端拒陈旧 revision，存储只缓存最近一张附图。HTML/PDF 以研究附录嵌入，说明可以包含手工/派生列且不参与正式判读；报告科学 evidence 与 Run revision 不因附图改变。当前仅有实现与新增 unit/backend 回归，最终运行结果见 FT007；尚未通过原生附图 PDF 视觉验收。

## 9. CPU、内存、队列与保留预算

### 9.1 当前硬限制与缺口

`ImportLimits` 默认源文件 200 MiB、物理行 200,000、单元格 5,000,000、列 512、shared strings 100,000、候选扫描 50 行、metadata 20 行。`PackageLimits` 默认总解压 2 GiB、单项 512 MiB、压缩比 200、entry 10,000、manifest 16 MiB。PlotProtocol 消息上限为 host 16 MiB/script 32 MiB，selection IDs 10,000，plot snapshot point limit 最大 1,000,000。

桌面便携传输另有更窄硬上限：包总量 128 MiB、单项 64 MiB、signal points 1,000,000、总行 2,000,000、普通文本值 8,192 字符；阶段证据 JSON 上限 2 MiB、trace provenance 64 KiB，临时新库也限 64 MiB。报告总文本 UTF-8 1 MiB、单文本 16,384 字符、canonical 内容 4 MiB、HTML/XLSX renderer 16 MiB，PDF 的 HTML UTF-8 输入限 2 MiB、输出 32 MiB。Raw query 单页不超过 25,000 点，科学计算点页不超过 250。

Explorer 手工表最多 10,000 行、派生列 8、覆盖单元格 5,000；公式最多 1,024 字符、128 AST 节点、16 层深度、每页 2,000,000 次求值。第二序列使图形上限为 50,000 点；视图 4 MiB。离线 HTML 最多 50,000 点/16 序列、8 MiB JSON、16 MiB 文档。报告 Raw 图最多 12×500 点，Explorer PNG 附图最多一张/1 MiB/1,600 万像素。支持包 JSON 限 64 KiB、事件队列 100 项。各入口超限明确拒绝，不静默放大用户预算。

这些是拒绝上限，不是峰值内存合格证明。DatasetBuilder 同时保存 observations 与 series，桌面仓储读取、报告与真实数据 Explorer 的叠加峰值尚需 benchmark。当前持久 store 只有一个操作槽/worker；全进程协调目标仍需后续测量。不能扩大限制替代有界管线设计。

### 9.2 目标调度（实施前需基准验证）

| 资源 | 工程设计目标 | 超限/取消策略 |
|---|---|---|
| 修改操作 | 每 Run 1 个；每 workspace 1 个 SQLite writer | 新提交显式 busy/conflict，不设无界 mutation 队列 |
| CPU 重任务 | 全进程最多 2 个槽：最多 1 个 import/analysis 与 1 个 report/plot preparation | 待执行上限 2；低核心机器可降为 1，UI dispatcher 不计后台 worker |
| Analysis | 单 worker 确定性归约 | 冻结输入；安全点检查取消，commit 前二次检查；不并行改变求和次序 |
| Import 管线 | 单 reader + 单批写者；初始批上限 10,000 signal points，队列容量 2 | backpressure；取消回滚未提交批；源副本按引用策略处理 |
| UI 查询 | 当前页 1 个 generation；分页上限 500 行，预取 1 页 | 导航取消旧查询，丢弃晚到响应、释放缓存 |
| Plot | 1 个活动 plot、1 个 pending 最新更新 | 合并中间更新；point/message 预算共同校验；降采样仅显示且保留精确点源 |
| Report/export | 每 workspace 1 个 render，逐节/分页读取 | 按输出预算拒绝超大导出；取消丢弃未发布 artifact，保留上一有效文件 |

UI 只保留 Run 摘要、当前页 DTO、当前 plot 紧凑数组和编辑草稿。Raw RFU 单份归仓储；Analysis input 在阶段结束释放，不能被 ViewModel/report closure 捕获。失败逐点诊断分页，不为固定窗口保留整套解析器或 worksheet DOM。

清理按 ownership：未被 Run 引用的 staging、取消导出、旧 query/plot buffer 可释放；源 managed copy、已冻结结果、trace 和恢复包按项目策略保留，不能因内存压力删除科学证据。自动清理仅在批准 workspace/artifacts 根内验证后执行；历史及敏感数据删除是明确用户操作。

需求性能目标不是当前承诺：参考机冷启动 ≤4 s、热启动 ≤2 s；96×121×2 导入典型 ≤2 s；全 Run BRAF 典型 ≤100 ms；25k 2D 首渲 ≤500 ms、3D ≤800 ms；含 WebView2 子进程的普通项目内存 <500 MB；小 autosave 可感知阻塞 <200 ms。ADR-002 的 3D spike 仍未达标。benchmark 记录硬件、OS/.NET/App SDK/WebView2、fixture hash、构建配置、cold/hot、重复次数、median/p95、峰值私有内存/GC 和取消延迟；同机同 fixture 比较 before/after，未达标显式降级或保留发布阻塞。

## 10. 验收矩阵

| 门禁 | 必要场景 | 通过标准与证据 |
|---|---|---|
| 科学顺序/边界 | RR界、WT严格界、dr'界、NTC own-Fb、先单孔后组 | Analysis unit+独立oracle；无舍入影响、无前置失败后的Sample结论 |
| 数值与输入 | NaN/Inf、均值/距离/时间溢出、分母非法、空集、缺通道、错Run/时间轴、缺Sample信息 | 结构化失败或合法构造拒绝，经Gateway不崩溃、不部分提交、不漏样本 |
| 科学证据 | 真实去标识化FAM/ROX、GD-001～022、Method/input hashes | 科研/工程双批准、独立参考期望；candidate通过不能替代 |
| Gateway | success/failure/empty/cancel/stale/replay/conflicting replay、fake/real | 同fixture套件；原子revision、稳定错误、cancel/commit竞争后刷新正确 |
| Trace/Report | 各阶段来源/full precision、完整性拒绝、INVALID报告、更新Method后旧报告 | 固定snapshot/hash；UI/Excel/PDF一致；缺trace不伪造完整报告 |
| 导入与资源 | 真实布局、多区域、字段错、超限、取消、100k/上限输入 | 无公式执行/格式猜测；有界峰值、及时取消、无半成品Run |
| Persistence/archive | save/open/move、旧snapshot/schema、未来版、损坏/故障恢复 | 旧结果/hash保持、迁移回退、最后有效包保留；traversal/collision/bomb拒绝 |
| 路径/诊断 | 非cwd启动、并行测试、移动工程、产物审计 | 持久身份相对路径；临时根唯一；无外部路径、原始异常、未授权数据泄漏 |
| UI | 黄金流程、每页异常状态、键盘/UIA/Narrator/DPI/HC | 无阻断/裁切；状态不只靠颜色；不可用明确；结果不依赖演示点 |
| Explorer | 2D/3D、no-WebGL、Point ID、JSON/shared-buffer回退、导出 | 精确点对应；派生隔离；相机/格式能力与ADR一致 |
| 发布/性能 | 离线/干净Windows、安装升级恢复、循环100次重开/渲染 | 锁版本、SBOM/license、产物路径审计；性能达标或批准降级；无累积泄漏 |

测试临时根仅用 `artifacts/test-tmp/<test-project>/<process-or-worker>/<test-name>/<unique-id>`，从程序集/注入配置发现根，不依赖cwd或系统临时目录。build、benchmark、render、coverage置于 `artifacts/`，发布前独立审计绝对路径泄漏。

## 11. 下一阶段顺序与完成定义

| 阶段 | 依赖 | 窄范围交付 | Definition of Done |
|---|---|---|---|
| E1 契约与追溯（工程实现） | Guided slice | 三阶段保存证据、双通道来源/input hash、计算点分页、只读 viewer、旧缺项兼容 | FT007 工程证据；继续真实科学/失败场景与独立参考验收 |
| E2 持久 workspace（工程实现） | 保存结果合同 | SQLite/WAL、独占/操作槽、目录重开、等待关闭、恢复标记/诊断/支持包 | 异常终止/断电、旧随机会话迁移、清理及发布资格仍待验收 |
| E3 工程保存/重开（工程实现） | E2 | 单 Run 隔离包、source/view/科学证据、受检 schema 2→3 | 继续 100 次往返、磁盘满/替换故障与发布验收；未知结构拒绝 |
| E4 不可变报告（技术草稿实现） | E1+E2 | HTML/XLSX/PDF、INVALID/完整性、hash、冻结端点/有界 Raw 曲线、真实Explorer PNG附录 | 附图最终回归和原生PDF/长报告/字体 QA 与批准门禁 |
| E5 真实 Explorer（工程实现） | E2与 Point ID | 虚拟页/手工表、列/过滤排序、DSL/平滑、真实 2D/3D/测距、视图与图形输出 | 参考机/GPU/RDP/长时性能、完整 UIA 及固定相机需求裁决 |
| E6 桌面与发布验证（部分） | E1～E5 | 未签名内部自包含发布脚本、依赖/hash 清单、用户说明；有限 live UI | 全流程 UIA/辅助技术、签名/安装升级/干净机离线、产物审计、参考机性能 |

每阶段先建 feature/fix stub，记录合同、迁移、预算和验收，完成后更新同一文档。科学批准与工程阶段并行：保持草案标识，可用合成与真实去标识化数据验证；科研签字、Golden 双签前不能声称完成方法验证。真实 fixture 不可得时保留门禁与原因，不制造批准或替代实验结果。

`build/Publish-ResearchBuild.ps1` 将未签名自包含 win-x64 内部包输出到新的 `artifacts/release/` 子目录，附用户指南/依赖证据和逐文件 hash，禁 PDB 并检查文本产物机器路径；需要本机 WebView2。该脚本不提供 MSIX 签名、运行时离线分发或干净机资格，执行结果单独记录在 FT007。

只有签字 Method/Data Contract、真实数据/Golden、导入至持久报告纵向验收、工程恢复、UI/性能/安全/安装证据全部满足，才宣布 V1 完成。当前可操作工程范围已扩展；尚未完成的验收及科学门禁不能因实现或测试数量增加而关闭。
