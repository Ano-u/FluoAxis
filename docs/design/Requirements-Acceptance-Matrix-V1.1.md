# FluoAxis 需求逐项验收矩阵 V1.1

| 项目 | 内容 |
|---|---|
| 基线 | `BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md`，重点为 §6 的全部 71 个 FR ID |
| 审查日期与分支 | 2026-10-08；当前 `main` 工作树，包含未提交变更 |
| 工程设计 | `docs/design/Software-Design-V1.1.md` 区分已实现最小范围与后续目标；本矩阵不把拟新增能力记为当前实现 |
| 科学真源 | `docs/method/Method-Specification-V1.0.md`、`docs/method/Data-Contract-V1.0.md`，均保留草案状态 |
| 本轮验证记录 | `docs/feature/FT-20261007-007-engineering-workflow-completion.md`；LQC 均值修复见 `docs/fix/FX-20261007-008-lqc-engine-mean.md`；UI 排版配色见 `docs/feature/FT-20261008-002-workbench-visual-hierarchy.md` |

## 状态与证据解释

- **工程实现**：当前代码中有可定位的实现与相应工程证据，不表示已完成科学验证或整机发布验收。
- **部分**：子能力或库层已实现，但需求包含的 UI、端到端链路、部署或验收尚不完整。
- **未实现**：当前 checkout 没有该需求的可操作实现；设计、依赖声明或 DTO 占位不计实现。
- **需科学批准**：工程规则按 draft 实现，但该参数、数据契约或结论仍需实验负责人批准；可与上述工程状态并列。

当前已接通持久工作区与 Guided、全阶段保存来源/科学点分页、XLS/显式解析/区域结束行、真实 2D/3D Explorer、虚拟页/手工表/DSL/平滑/视图保存、工程包 schema 2→3、报告端点/Raw 图、离线 HTML/图像保存、诊断/支持包与内部发布脚本。下表按具体范围判断，不把页级研究表称为无限通用表，也不把 storedComplete 称为科学批准。

2026-10-08 补充 UI 工程证据：Per-Monitor V2 原生探针确认 144 DPI/1.5 倍；FT-20261008-002 的原生 RenderTargetBitmap 检查覆盖浅色/深色/再次浅色、宽窄窗口、结果/映射/孔板/报告，以及 Explorer 控件和展开设置，十张渲染图已复核。该证据验证 XAML 排版和主题，不包含 WebView 图像、高对比度实机、Narrator 或用户完整点击流程；原有科学和发布门禁不变。

精确工程命令、测试数量、测量和图像证据集中见 FT007，本矩阵不另维护计数。本轮独立工作区新建/picker 已观察，PickerHost 未定位；经工程包验证的 Debug 合成项目 UIA 树读到 revision 5、样本阳性/dr′ 0.4、Input/Mapping/Source hash 及双通道源行列。截图与 UIA 不一致，不据此宣称完整视觉/点击验收；用户按 Esc 停止后已停所有 GUI 输入。既往 FT006 五页 PDF smoke 不代替新图形/PNG 附录的原生 QA。Windows 嵌套目录 rename code 5 未解决；包往返不证明该故障已修。科学/真实数据、辅助技术、参考机性能和发布资格仍待验收。

所有科学条目共同受 **SCI-GATE** 约束：批准 Method/Data Contract、真实去标识化仪器数据、独立 reference calculation 和科研/工程双批准 Golden。当前 `samples/golden/candidate-manifest-v1.json` 明确为 `candidate-unapproved`，不能据工程测试通过宣布方法获批。下文“SCI-GATE”引用这一完整门禁。

2026-10-08 自动流程补充：用户确认固定四角对照与 B2–G11 样本区并授权“导入即识别判读”。实现可解释识别、可编辑默认方案、Gateway 顺序执行、人工继续及保留历史的新运行设置；详见 `docs/feature/FT-20261008-004-assisted-import-analysis.md`。自动分析不要求无歧义文件逐页点击；手工映射能力继续保留，缺项/歧义不能自动通过。发布资源缺失根因及实际 EXE 验证见 `docs/fix/FX-20261008-003-release-startup.md`。新证据不关闭真实仪器/科学批准/完整桌面交互门禁。

## §6.1 数据导入

2026-10-09 增补：最终发布版通过真实 Windows UI Automation 九项流程（取消 picker、文件选取至分析、工程包保存、报告快照、HTML/XLSX/PDF 导出、关闭重开、另一工作区工程包打开）。另有实际 WebView 与 Edge 恒定/变化曲线、宽窄视口、坐标轴、工具栏、点身份和 3D 像素证据。可签名 MSIX 通过 MakeAppx 与解包身份验证，但暂无证书，正式签名/安装升级卸载/干净机及完整辅助技术门禁仍未关闭。精确范围见 FT-20261008-006、FX-20261009-001；不再将已通过的原生 picker 主链列为全无证据。

人工分支的新增原生证据见 `docs/fix/FX-20261008-005-smart-refinement.md`：重新设置不覆盖旧 Run，人工选择秒单位得到正确端点，元数据缺项停下复核，方案可整体撤销/重做，手动样本编号和文件明确编号均进入已保存结果。输入仍由受管测试适配器提供，不把此验证扩展为真实仪器或原生 picker 资格。

| FR ID | 原要求 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/实施 |
|---|---|---|---|---|
| FR-IMP-001 | 拖拽文件到主窗口 | 工程实现；原生验收部分 | `src/FluoAxis.App/MainWindow.Import.cs`；`src/FluoAxis.Infrastructure/DesktopWorkspace.Windows.cs` | 单文件 storage drop 经 Infrastructure 安全 staging；多文件/目录/不支持格式拒绝，真实拖放/取消/键盘等价流程待验收 |
| FR-IMP-002 | 文件选择器导入 | 工程实现 | `src/FluoAxis.Infrastructure/DesktopWorkspace.Windows.cs`；`src/FluoAxis.App/MainWindow.xaml.cs`；`tests/FluoAxis.Infrastructure.Tests/ManagedFileWorkflowTests.cs` | native picker 已 live 显示；仍需 picker 实际选取→映射→结果的完整 Windows UI 验收 |
| FR-IMP-003 | XLSX、XLS、CSV、TSV、TXT | 工程实现；真实格式资格部分 | `src/FluoAxis.Import/XlsImporter.cs`；`src/FluoAxis.Import/ImportReadOptions.cs`；`src/FluoAxis.App/MainWindow.Import.cs`；`tests/FluoAxis.Import.Tests/ImportOptionsTests.cs` | BIFF/OLE 签名和只读值、显式编码/分隔符/小数文化/时间单位已接；真实仪器和旧版 XLS 格式矩阵待补 |
| FR-IMP-004 | XLSX 不依赖 Excel/COM | 工程实现 | `src/FluoAxis.Import/XlsxSheetReader.cs`；`src/FluoAxis.Import/FluoAxis.Import.csproj`；`tests/FluoAxis.Import.Tests/XlsxImporterTests.cs` | 无 Office 干净 Windows 上验收；维持公式仅缓存值、不执行对象/宏 |
| FR-IMP-005 | 自动扫描候选区，不假设首行表头 | 工程实现（有界候选与明确标签识别） | `src/FluoAxis.Import/AutomaticImportRecognizer.cs`；`src/FluoAxis.Import/RegionDiscovery.cs`；`tests/FluoAxis.Import.Tests/AutomaticImportRecognizerTests.cs` | 智能识别最多 32 sheet、每 sheet 256 行、65,536 cells；超限/歧义转手工。旧预览候选仍扫描前 50 行。真实 metadata/异常表头布局待验收 |
| FR-IMP-006 | 手工确认 Time、FAM、ROX | 工程实现 | `src/FluoAxis.App/MainWindow.Import.cs`；`src/FluoAxis.Presentation/WorkbenchViewModel.cs`；`tests/FluoAxis.GatewayContract.Tests/WorkbenchViewModelTests.cs` | 独立 sheet/表头/时间/孔列/结束行及无候选手填已接；真实文件逐字段可视核对、完整 UI 自动化待验收 |
| FR-IMP-007 | 多 Sheet 或同 Sheet 多块区域 | 工程实现；真实布局验收部分 | `src/FluoAxis.Import/DataMapping.cs`；`src/FluoAxis.Contracts/ImportMappingDtos.cs`；`tests/FluoAxis.Import.Tests/ImportOptionsTests.cs` | 独立 sheet/列组/上下区域 EndRowIndex 合同与回归已接；候选扫描仍有界，不宣称任意仪器布局无需手工确认 |
| FR-IMP-008 | 空值、非数值、重复时间、倒序、孔冲突检测 | 工程实现 | `src/FluoAxis.Import/DatasetBuilder.cs`；`src/FluoAxis.Import/DataMapping.cs`；`tests/FluoAxis.Import.Tests/XlsxImporterTests.cs`；`tests/FluoAxis.Import.Tests/DataMappingTests.cs` | 补真实导出异常、格式 fuzz、逻辑行列错误在 UI 的定位验收；SCI-GATE 的负 RFU/饱和策略 |
| FR-IMP-009 | FAM/ROX 孔集合匹配 | 工程实现 | `src/FluoAxis.Contracts/GatewayCommands.cs`；`src/FluoAxis.Import/DatasetBuilder.cs`；`tests/FluoAxis.Import.Tests/XlsxImporterTests.cs` | 真实双通道 fixture 与 UI 错误高亮；当前已同时拒绝伪双通道同源列绑定 |
| FR-IMP-010 | 保存源 SHA-256 | 工程实现；持久与便携链已接 | `src/FluoAxis.Infrastructure/ManagedImportStagingAdapter.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Packages.cs`；`tests/FluoAxis.Infrastructure.Tests/PortableProjectPackageTests.cs` | staging/确认重验、重开/包往返保持身份；真实数据及发布迁移仍待验收 |
| FR-IMP-011 | 导入可取消、不阻塞 UI | 工程实现；整机验收部分 | `src/FluoAxis.Import/XlsxImporter.cs`；`src/FluoAxis.Import/DelimitedTextImporter.cs`；`src/FluoAxis.Presentation/WorkbenchViewModel.cs`；`tests/FluoAxis.Infrastructure.Tests/ManagedImportStagingAdapterTests.cs` | 大文件 UI 响应、取消延迟、commit/取消竞争的 live 验收；全进程 CPU 队列预算落地 |
| FR-IMP-012 | 大表流式读取、非完整 Excel DOM | 工程实现；内存验收部分 | `src/FluoAxis.Import/XlsxSheetReader.cs`；`src/FluoAxis.Import/ImportLimits.cs`；`src/FluoAxis.Import/DatasetBuilder.cs`；`tests/FluoAxis.Import.Tests/DelimitedTextImporterTests.cs` | SAX 与解析限制已存在，但 observations/series 同时驻留；需上限输入峰值内存、取消、非 seekable 流与压缩资源测试 |

## §6.2 时间

| FR ID | 原要求 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/实施 |
|---|---|---|---|---|
| FR-TIME-001 | 使用 Elapsed Time，非固定行号 | 工程实现 | `src/FluoAxis.Domain/SignalPoint.cs`；`src/FluoAxis.Import/DatasetBuilder.cs`；`src/FluoAxis.Analysis/AnalysisMath.cs` | 实际仪器时间单位及非等间隔 fixture；SCI-GATE |
| FR-TIME-002 | 首条有效采集为 t=0 | 工程实现；需科学批准 | `src/FluoAxis.Import/DatasetBuilder.cs`；`tests/FluoAxis.Import.Tests/XlsxImporterTests.cs`；`docs/method/Data-Contract-V1.0.md` | 四点 2/5/30/120 与相对原点/120 min endpoint 的真实输入解释需 SCI-GATE，不静默改变时间语义 |
| FR-TIME-003 | 内部 ticks/duration | 工程实现 | `src/FluoAxis.Domain/SignalPoint.cs`；`src/FluoAxis.Import/DatasetBuilder.cs`；`tests/FluoAxis.Import.Tests/DelimitedTextImporterTests.cs` | ticks 舍入碰撞和极端时间已有拒绝路径；补仪器精度/单位 fixture，保证输出逻辑坐标可追溯 |
| FR-TIME-004 | Method 时间窗口、非固定点数 | 工程实现；需科学批准 | `src/FluoAxis.Domain/BrafMethodSnapshot.cs`；`src/FluoAxis.Analysis/AnalysisMath.cs`；`tests/FluoAxis.Analysis.Tests/AnalysisMathTests.cs` | 平台闭区间与最小点数仍 provisional；SCI-GATE |
| FR-TIME-005 | 正式 endpoint 默认不插值 | 工程实现 | `src/FluoAxis.Analysis/AnalysisMath.cs`；`tests/FluoAxis.Analysis.Tests/AnalysisMathTests.cs` | 真实 endpoint fixture、报告显示选中点和理由；SCI-GATE |
| FR-TIME-006 | 设备时间戳小容差 | 工程实现；需科学批准 | `src/FluoAxis.Domain/BrafMethodSnapshot.cs`；`src/FluoAxis.Analysis/AnalysisMath.cs`；`tests/FluoAxis.Analysis.Tests/AnalysisMathTests.cs` | ±15 s 默认仍待批准；验证真实设备误差、最近点/等距较晚点与闭边界；无任意修改 Method 的 UI |
| FR-TIME-007 | 无合法 120 min endpoint 则 Invalid | 工程实现 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs`；`tests/FluoAxis.Application.Tests/RunWorkflowEndToEndTests.cs` | 真实缺点输入、UI 阻断及推荐动作、失败 trace/报告；SCI-GATE |

## §6.3 Plate Map

| FR ID | 原要求 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/实施 |
|---|---|---|---|---|
| FR-PLATE-001 | 96 孔 A1–H12 | 工程实现 | `src/FluoAxis.Domain/WellId.cs`；`src/FluoAxis.App/MainWindow.xaml.cs`；`tests/FluoAxis.Domain.Tests/WellIdTests.cs` | 96 孔满板、键盘、DPI/High Contrast 与实际仪器顺序验收 |
| FR-PLATE-002 | 表格与 Plate 双向联动高亮 | 工程实现；整机验收部分 | `src/FluoAxis.App/MainWindow.Explorer.cs`；`src/FluoAxis.App/MainWindow.xaml.cs`；`src/FluoAxis.Presentation/PlateDraftEditor.cs` | Raw 表行/图点回到原孔和 point index，孔板选孔加载视图；完整 Ctrl/Shift、分页/筛选及辅助技术 live 验收 |
| FR-PLATE-003 | 框选多个孔批量角色 | 工程实现；整机验收部分 | `src/FluoAxis.Presentation/PlateDraftEditor.cs`；`src/FluoAxis.App/MainWindow.xaml.cs`；`tests/FluoAxis.GatewayContract.Tests/PlateDraftEditorTests.cs` | 已有矩形与键盘选择、批量赋值、20步undo/redo；真实满板、Narrator/触摸板/完整UIA验收待补 |
| FR-PLATE-004 | 六种指定角色 | 工程实现 | `src/FluoAxis.Domain/WellRole.cs`；`src/FluoAxis.Contracts/WellAssignmentDto.cs`；`src/FluoAxis.App/MainWindow.xaml.cs` | 真实满板、缺必需控制孔反馈；控制最小基数仍 SCI-GATE |
| FR-PLATE-005 | Sample ID、Group、Replicate、Target、Notes | 工程实现 | `src/FluoAxis.Contracts/WellAssignmentDto.cs`；`src/FluoAxis.App/MainWindow.xaml.cs`；`src/FluoAxis.Presentation/PlateDraftEditor.cs`；`tests/FluoAxis.GatewayContract.Tests/PlateDraftEditorTests.cs` | 全字段单孔/批量编辑、重开与草稿报告已接；真实元数据/辅助技术/导出边界验收 |
| FR-PLATE-006 | FAM/ROX 绑定同一逻辑孔 | 工程实现 | `src/FluoAxis.Analysis/WellSignals.cs`；`src/FluoAxis.Application/AnalysisInputBinding.cs`；`tests/FluoAxis.Analysis.Tests/WellSignalsTests.cs`；`tests/FluoAxis.Application.Tests/WorkflowInputIntegrityTests.cs` | 真实双通道和跨 Run/时间轴异常 UI 验收；缺通道不能静默掉样本 |
| FR-PLATE-007 | 同一 Run/Well 角色互斥 | 工程实现 | `src/FluoAxis.Application/ApplicationGatewayWorkflow.cs`；`src/FluoAxis.Presentation/PlateDraftEditor.cs`；`tests/FluoAxis.Application.Tests/WorkflowInputIntegrityTests.cs` | 后端拒冲突，UI批量草稿不直接改Run，dirty/stale确认已接；继续真实满板并发与UI错误定位验收 |

## §6.4 Calibration

| FR ID | 原要求 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/实施 |
|---|---|---|---|---|
| FR-CAL-001 | Positive 平台/Fm 先于 NTC QC | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`tests/FluoAxis.Analysis.Tests/ScientificEvidenceTests.cs` | SCI-GATE；阶段实际计算证据已持久化，失败前未执行的步骤不补造；真实顺序/失败输入验收 |
| FR-CAL-002 | 90–120 min 全部实际点 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/AnalysisMath.cs`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`src/FluoAxis.App/MainWindow.Evidence.cs` | 窗口点和源坐标保存，每页≤250点只读；SCI-GATE 的采样/闭区间/最小点数与真实非等间隔输入 |
| FR-CAL-003 | RR ≤1% 合格 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/AnalysisMath.cs`；`tests/FluoAxis.Analysis.Tests/AnalysisMathTests.cs`；`tests/FluoAxis.Golden.Tests/GoldenCandidateTests.cs` | SCI-GATE；RR 等界/两侧在真实输入中的参考值；UI 保留原值判据 |
| FR-CAL-004 | 单孔 Fm 为窗口均值 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Contracts/ScientificEvidenceDto.cs`；`tests/FluoAxis.Infrastructure.Tests/ScientificEvidenceRoundTripTests.cs` | 逐孔 count/min/max/mean/RR/Fm 与窗口来源已接 UI/持久化/报告；SCI-GATE |
| FR-CAL-005 | 任一关键 Positive fail 则 calibration fail | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs`；`tests/FluoAxis.Application.Tests/RunWorkflowEndToEndTests.cs` | SCI-GATE；真实重复孔失败与 UI 不可继续/不可静默排孔验证 |
| FR-CAL-006 | NTC 自身 endpoint 为 Fb_i | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`src/FluoAxis.App/MainWindow.Evidence.cs` | NTC endpoint/FbOwn、源坐标及 identity 已保存显示；真实端点和 SCI-GATE |
| FR-CAL-007 | NTC 自身 Fb 与 Run Fm 做全程 QC | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Persistence/SqliteProjectRunRepository.ScientificEvidence.cs`；`tests/FluoAxis.Analysis.Tests/ScientificEvidenceTests.cs` | 已保存每个实际检查点的 Raw/Y/带内状态并分页；SCI-GATE 的四点/全时程裁决、完整交互曲线与真实输入复核 |
| FR-CAL-008 | NTC 任一通道任一点超带则 fail | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Analysis.Tests/ScientificEvidenceTests.cs`；`src/FluoAxis.App/MainWindow.Evidence.cs` | 保存失败点/原判据；SCI-GATE 的 ±0.05 开闭、ROX 单独失败真实 fixture 与用户反馈验收 |
| FR-CAL-009 | 任一关键 NTC fail 则 calibration fail | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs`；`tests/FluoAxis.Application.Tests/RunWorkflowEndToEndTests.cs` | SCI-GATE；关键孔不可静默排除与失败结果持久查询 |
| FR-CAL-010 | 全 NTC pass 后求 Run Fb 两通道均值 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`tests/FluoAxis.Infrastructure.Tests/ScientificEvidenceRoundTripTests.cs` | 输入/映射/源 hash、逐孔 Fb 与计算点 digest 已冻结；SCI-GATE 与真实聚合参考值 |
| FR-CAL-011 | Fm≤Fb 或分母过小则 Calculation Error | 部分；需科学批准 | `src/FluoAxis.Analysis/AnalysisMath.cs`；`src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`docs/method/Data-Contract-V1.0.md` | 已拒绝≤0/非有限分母及非有限结果；绝对“过小”epsilon 未科学批准，不擅自增加。补经批准下限及 Golden/版本影响 |

## §6.5 Control QC

| FR ID | 原要求 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/实施 |
|---|---|---|---|---|
| FR-QC-001 | WT 每孔 endpoint Y_FAM/Y_ROX | 工程实现；需科学批准 | `src/FluoAxis.Analysis/ControlSampleModels.cs`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`src/FluoAxis.App/MainWindow.Evidence.cs` | 双通道端点/源坐标、Y、Fm/Fb/判据和参考已接持久读取/报告；SCI-GATE |
| FR-QC-002 | WT 两通道组均值为参考 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`tests/FluoAxis.Analysis.Tests/ScientificEvidenceTests.cs` | 参考点、逐孔来源与输入身份已保存；均值溢出失败关闭；SCI-GATE |
| FR-QC-003 | 两通道 WT 均值均严格<0.05 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs`；`tests/FluoAxis.Golden.Tests/GoldenCandidateTests.cs` | SCI-GATE，尤其未新增 WT 单孔规则；严格等界显示与阻断 live 验收 |
| FR-QC-004 | LQC 每孔独立 dr' | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Contracts/ScientificEvidenceDto.cs`；`src/FluoAxis.Application/ApplicationGatewayEvidence.cs` | trace 双通道 source coordinates/input hash 已保存且 Sample 后保留；旧记录仍 partial；SCI-GATE |
| FR-QC-005 | LQC dr'≥0.4 阳性 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Golden.Tests/GoldenCandidateTests.cs`；`docs/method/Data-Contract-V1.0.md` | SCI-GATE 的阈值/LQC科学证据；正式结果不因显示舍入改变 |
| FR-QC-006 | LQC 方向混合→Run Invalid | 工程实现 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Application/ApplicationGatewayWorkflow.cs`；`tests/FluoAxis.Integration.Tests/SqliteWorkflowRoundTripTests.cs` | 当前已修正 `ControlQcFailed -> RunInvalid` 原子路径；补 UI 逐孔方向复核与 SCI-GATE |
| FR-QC-007 | LQC 一致后 mean dr′ 且弱阳性预期 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Analysis/ControlSampleModels.cs`；`tests/FluoAxis.Application.Tests/WorkflowInputIntegrityTests.cs` | Engine 在单孔方向一致后产生均值，负向一致组保留mean但QC失败/RunInvalid，冲突mean为空；Application只持久/转发，旧缺项不补算；SCI-GATE |

## §6.6 Sample

| FR ID | 原要求 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/实施 |
|---|---|---|---|---|
| FR-SAM-001 | 每重复孔独立归一化 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`tests/FluoAxis.Infrastructure.Tests/ScientificEvidenceRoundTripTests.cs` | 保存逐孔结果/G17 trace/双通道来源及 input hash；真实矩阵、报告实际复核和 SCI-GATE |
| FR-SAM-002 | 各孔与 WT 二维参考计算 dr' | 工程实现；需科学批准 | `src/FluoAxis.Analysis/AnalysisMath.cs`；`src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Golden.Tests/GoldenCandidateTests.cs` | SCI-GATE；二维正式距离与Explorer三维工具视觉/文案区分 |
| FR-SAM-003 | dr'≥0.4 阳性 | 工程实现；需科学批准 | `tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs`；`tests/FluoAxis.Infrastructure.Tests/ManagedFileWorkflowTests.cs`；`src/FluoAxis.Analysis/BrafAnalysisEngine.cs` | SCI-GATE；阈值等界/两侧经真实证据批准；UI/report一致 |
| FR-SAM-004 | dr'<0.4 阴性 | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs` | SCI-GATE；失败/未计算不得显示阴性；真实阴性fixture |
| FR-SAM-005 | 重复孔方向先于均值 | 工程实现 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`tests/FluoAxis.Analysis.Tests/BrafAnalysisEngineTests.cs`；`src/FluoAxis.Contracts/ScientificEvidenceDto.cs` | legacy false 仍强制一致性；新 trace 保留双通道来源与错误码；SCI-GATE/真实复核 |
| FR-SAM-006 | 两阳一阴/两阴一阳 INVALID | 工程实现 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.Application/ApplicationGatewayWorkflow.cs`；`tests/FluoAxis.Application.Tests/RunWorkflowEndToEndTests.cs` | 组call/mean为空，保存错误码转Run问题与建议动作；真实三重复组和正式报告验收待补 |
| FR-SAM-007 | 一致后计算并显示 mean dr' | 工程实现；需科学批准 | `src/FluoAxis.Analysis/BrafAnalysisEngine.cs`；`src/FluoAxis.App/MainWindow.xaml.cs`；`src/FluoAxis.Reporting/ReportTables.cs` | UI与冻结技术草稿渲染保存均值；SCI-GATE、完整真实数据/正式报告验收 |
| FR-SAM-008 | 最终“BRAF V600E 阳性/阴性”措辞 | 工程实现；草稿报告已接 | `src/FluoAxis.Domain/BrafMethodSnapshot.cs`；`src/FluoAxis.App/MainWindow.xaml.cs`；`src/FluoAxis.Reporting/ReportTables.cs` | 仅legacy BRAF受控词汇，INVALID不输出有效判读；任意Method标签仍需契约与批准，维持RUO |

## §6.7 Data Explorer

| FR ID | 原要求 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/实施 |
|---|---|---|---|---|
| FR-EXP-001 | 导入数据表格查看 | 工程实现（有界虚拟页） | `src/FluoAxis.Presentation/ExplorerDataModel.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Explorer.cs`；`src/FluoAxis.App/MainWindow.Explorer.cs` | 真双通道按原 index/ticks 配对，≤25,000行 ListView 按需行模型；百万行通用表和参考机峰值内存未验收 |
| FR-EXP-002 | 新建空表手工变量 | 工程实现（独立研究表） | `src/FluoAxis.Contracts/ExplorerContracts.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`；`tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs` | ≤10,000行 X/Y/Z/派生列、编辑与视图保存；不修改正式Raw，未宣称Excel等价任意表编辑/无限撤销 |
| FR-EXP-003 | 列重命名、单位、排序筛选、复制粘贴 | 工程实现；整机验收部分 | `src/FluoAxis.Presentation/ExplorerDataModel.cs`；`src/FluoAxis.App/MainWindow.Explorer.cs`；`tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs` | 页面级过滤排序、数值剪贴板、标签/单位与覆盖持久化；Raw/index只读、Point ID保持，真实大表和键盘验收待补 |
| FR-EXP-004 | 2D散点/折线/散点+线 | 工程实现；像素性能验收部分 | `src/FluoAxis.App/MainWindow.Explorer.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`；`web/FluoAxis.PlotHost/plot-host.js` | 真数据三种2D模式/可选第二序列已接；完整像素/DPI/参考机性能资格 |
| FR-EXP-005 | 3D笛卡尔scatter3D/line3D | 工程实现；相机与整机验收部分 | `src/FluoAxis.App/MainWindow.Explorer.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`；`docs/adr/ADR-002-plot-renderer-and-export.md` | 真Raw/派生/手工列映射已接；固定camera仍与原交互需求有偏差，RDP/GPU/性能资格待补 |
| FR-EXP-006 | 任意列映射X/Y/Z | 工程实现（当前表内列） | `src/FluoAxis.Contracts/ExplorerContracts.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`；`src/FluoAxis.App/MainWindow.Explorer.cs` | 列/单位/缺值、线性/对数检查与视图持久化；范围是当前已加载有界表，不宣称任意外部表无需导入 |
| FR-EXP-007 | 多序列/图例/误差条/线性对数 | 工程实现（最多两序列） | `src/FluoAxis.PlotProtocol/PlotDataSnapshot.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`；`web/FluoAxis.PlotHost/plot-host.test.js` | 可选第二Y及给定非负ErrorY、图例/linear/log已接；≤50,000点，更多任意序列/极端域/实机像素待验收 |
| FR-EXP-008 | Moving Average /低阶SG视觉平滑 | 工程实现（页内） | `src/FluoAxis.Presentation/ExplorerDataModel.cs`；`src/FluoAxis.Contracts/ExplorerContracts.cs`；`tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs` | SG 2/3阶、奇数窗口3–51，边缘移动完整窗口；按页内行序、不跨页/不按物理时间拟合；真实曲线可读性验收 |
| FR-EXP-009 | 正式判读不使用平滑数据 | 工程实现 | `src/FluoAxis.Application/AnalysisInputBinding.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`；`build/Verify-ScientificSourceBoundary.ps1` | Raw列只读，派生/手工/覆盖只用于Explorer，Analysis无派生入口；持续数据流/架构回归与发布审计 |
| FR-EXP-010 | 安全Formula Builder | 工程实现（受限数值DSL） | `src/FluoAxis.Presentation/ExplorerExpression.cs`；`src/FluoAxis.App/MainWindow.Explorer.cs`；`tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs` | 白名单函数、1024字符/128节点/16层、每页2m求值；无脚本/反射/IO，错误为显式缺失；真实用户可用性/性能资格 |
| FR-EXP-011 | 2D/3D两点距离 | 工程实现；实机交互部分 | `src/FluoAxis.PlotProtocol/PlotDataSnapshot.cs`；`src/FluoAxis.App/MainWindow.Explorer.cs`；`tests/FluoAxis.PlotProtocol.Tests/PlotDataSnapshotTests.cs` | 真实点选择与C#测量已接，非法/溢出拒绝；显示坐标几何距离不冒充BRAF dr′，单位解释/点击验收 |
| FR-EXP-012 | 点选回到原始Point ID | 工程实现；完整实机验收部分 | `src/FluoAxis.App/MainWindow.Explorer.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`；`tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs` | Run/well/原point index/source hash、表行与孔板联动，排序后不改来源；手工/演示独立，原sheet/cell精确跳转仍需完善 |

## §6.8 输出

| FR ID | 原要求 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/实施 |
|---|---|---|---|---|
| FR-OUT-001 | Excel导出 | 工程输出已实现（RUO技术草稿） | `src/FluoAxis.Reporting/ResearchReportRenderer.Xlsx.cs`；`src/FluoAxis.Reporting/ReportTables.cs`；`tests/FluoAxis.Reporting.Tests/ResearchReportTests.cs` | 冻结科学来源/全精度表、hash、inline text防注入；XLSX未承诺嵌图，实际Excel兼容/批准与发布资格待验收 |
| FR-OUT-002 | PDF Research Analysis Report | 工程输出已实现（RUO技术草稿） | `src/FluoAxis.Infrastructure/PdfReportRenderer.Windows.cs`；`src/FluoAxis.Reporting/ResearchReportRenderer.Figures.cs`；`src/FluoAxis.Infrastructure/ReportRenderingSmoke.Windows.cs` | 新增保存端点SVG与代表Raw曲线；既往五页native smoke不代替当前图形/长报表/设备字体QA，输入2MiB/输出32MiB；SCI-GATE |
| FR-OUT-003 | 2D PNG/SVG | 工程实现；整机验收部分 | `src/FluoAxis.App/MainWindow.PlotDelivery.cs`；`src/FluoAxis.Infrastructure/DesktopWorkspace.PlotDelivery.Windows.cs`；`web/FluoAxis.PlotHost/plot-host.js` | 当前冻结页经保存picker导出；仅适用SVG的trace矢量输出，尺寸/字体/保存失败与实际像素验收 |
| FR-OUT-004 | 3D高分辨率PNG及PDF嵌图 | 工程实现；最终回归与原生QA待验 | `src/FluoAxis.App/MainWindow.PlotDelivery.cs`；`src/FluoAxis.Contracts/ReportPlotFigureDto.cs`；`src/FluoAxis.Reporting/ResearchReportRenderer.Html.cs` | 真实Explorer PNG可冻结为HTML/PDF附录，绑定Run/revision，仅1张缓存、≤1MiB/16M像素；不影响正式结果。新增unit/backend测试以FT007最终运行结果为准，native附图PDF/分辨率/分页/设备QA未通过验收 |
| FR-OUT-005 | 可选交互HTML图表 | 工程实现；独立浏览器验收部分 | `src/FluoAxis.Visualization/OfflinePlotDocument.cs`；`src/FluoAxis.App/MainWindow.PlotDelivery.cs`；`tests/FluoAxis.Visualization.Tests/OfflinePlotDocumentTests.cs` | 当前页/固定资源hash/CSP/文本转义/无网络；≤50k点/16序列/8MiB JSON/16MiB文档，浏览器WebGL/离线实机待验收 |
| FR-OUT-006 | `.fluoaxisproj`工程保存 | 工程实现（单Run，schema 2→3） | `src/FluoAxis.Infrastructure/PersistentDesktopStore.Packages.cs`；`src/FluoAxis.Persistence/SqliteProjectRunRepository.Package.cs`；`tests/FluoAxis.Infrastructure.Tests/PortableProjectPackageTests.cs` | 所选Run/source/可选view/来源身份保持，旧schema迁移不补算；完整历史版本/断电/大包/发布资格待补，拒覆盖 |
| FR-OUT-007 | 报告记录Method与软件版本 | 工程实现（技术草稿） | `src/FluoAxis.Contracts/ReportSnapshotDto.cs`；`src/FluoAxis.Reporting/ReportTables.cs`；`tests/FluoAxis.Reporting.Tests/ResearchReportTests.cs` | 软件/Method/algorithm/source/input及hash冻结；最近同Run/revision复用，旧partial保留；批准版/跨会话报告目录仍待扩展 |

## §10 以后：工程、运行、安全与发布主要条款

以下 `SEC-*` 只是本矩阵检索键，不是新造的原始 FR ID。章节要求有多个子项时拆开列出；未验收的目标方案不能因出现在设计文档中被记为实现。

| 检索键 / 原章节 | 要求主题 | 现状 | 当前证据（仓库相对路径） | 尚缺验收/决策 |
|---|---|---|---|---|
| SEC-10 / §10 | stable .NET/C#/WinUI/依赖锁定 | 工程实现；整机部分 | `global.json`；`Directory.Build.props`；`Directory.Packages.props`；`docs/adr/ADR-001-toolchain.md` | 当前文件为准：SDK 10.0.400/latestPatch、C#14、App SDK 2.4.0、Plotly4.0.0；不能照搬需求旧patch。补参考机/离线/Release资格验证 |
| SEC-11 / §11 | 模块化单体、干净依赖、Gateway边界 | 工程实现；目标模块部分 | `FluoAxis.slnx`；`src/FluoAxis.Reporting/FluoAxis.Reporting.csproj`；`tests/FluoAxis.Architecture.Tests/ProjectDependencyTests.cs` | 当前13生产/15测试项目，Reporting已加入；完整Benchmark与运行数据流/发布资格继续审查 |
| SEC-12 / §12 | Domain、不变Raw/Method/孔角色 | 工程实现；迁移部分 | `src/FluoAxis.Domain/`；`tests/FluoAxis.Domain.Tests/` | legacy false/zero保留hash且执行安全；需旧项目版本矩阵、不可变集合/输入混用与重算来源验证 |
| SEC-13 / §13 | 唯一C#算法真源、完整trace | 工程实现；需科学批准 | `src/FluoAxis.Analysis/`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`src/FluoAxis.App/MainWindow.Evidence.cs` | Calibration/WT/LQC/Sample来源与input identity、点分页已接；失败前未执行阶段不补造，旧partial/null保留；SCI-GATE |
| SEC-14 / §14 | Application状态/原子事务/审计 | 工程实现 | `src/FluoAxis.Application/ApplicationGatewayWorkflow.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.cs`；`tests/FluoAxis.Integration.Tests/SqliteWorkflowRoundTripTests.cs` | 桌面SQLite与独占租约已接；完整故障注入、断电恢复和发布审计仍需验收 |
| SEC-15 / §15 | WinUI/MVVM/allowedActions | 工程实现基线；整机部分 | `src/FluoAxis.App/MainWindow.xaml`；`src/FluoAxis.Presentation/PlateDraftEditor.cs`；`tests/FluoAxis.GatewayContract.Tests/PlateDraftEditorTests.cs` | 响应式/批量/undo/dirty确认/分列结果已接；完整已分析UI流程与辅助技术验收待补 |
| SEC-16 / §16 | Host/DI/config/logging生命周期 | 部分 | `src/FluoAxis.App/App.xaml.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Diagnostics.cs` | 手动组合、等待关闭/操作槽/租约、技术事件与支持包已接；完整Generic Host/config/集中日志与crash接线仍部分 |
| SEC-17-A / §17 | XLSX/CSV安全流式导入 | 工程实现；验收部分 | `src/FluoAxis.Import/XlsxSheetReader.cs`；`src/FluoAxis.Import/ImportReadOptions.cs`；`tests/FluoAxis.Import.Tests/ImportOptionsTests.cs` | 显式解析参数和EndRow已持久；真实fixture、上限内存/恶意压缩/取消延迟与Windows操作验收 |
| SEC-17-B / §17.3 | XLS隔离兼容 | 工程实现；真实格式资格部分 | `src/FluoAxis.Import/XlsImporter.cs`；`tests/FluoAxis.Import.Tests/ImportOptionsTests.cs`；`src/FluoAxis.Infrastructure/ManagedImportInspectionAdapter.cs` | 只读BIFF/OLE、签名/行列数值边界/安全错误已接，无Office执行；真实legacy设备fixture和资源峰值待验 |
| SEC-18 / §18 | SQLite WAL、事务/外键/批量写 | 工程实现；整机部分 | `src/FluoAxis.Persistence/SqliteSchema.cs`；`src/FluoAxis.Persistence/SqliteProjectRunRepository.ScientificEvidence.cs`；`tests/FluoAxis.Persistence.Tests/ScientificSchemaMigrationTests.cs` | 全阶段证据/原始坐标、schema 2→3与旧缺项兼容；大数据、断电和长期运行待验收 |
| SEC-19 / §19 | 安全工程包/manifest/hash/迁移 | 工程实现（受检schema 2→3） | `src/FluoAxis.Infrastructure/PersistentDesktopStore.Packages.cs`；`src/FluoAxis.Persistence/SqliteProjectRunRepository.Package.cs`；`tests/FluoAxis.Infrastructure.Tests/PortableProjectPackageTests.cs` | 128MiB总/64MiB单项、1m点/2m行；source/input/计算点与可选view验证，普通文本/证据JSON分别限额；未知版本/结构拒绝，故障资格待补 |
| SEC-20-A / §20.1–20.5 | 本地Plotly/WebView2、2D/3D、格式能力 | 工程实现；整机部分 | `src/FluoAxis.App/MainWindow.Explorer.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`；`docs/adr/ADR-002-plot-renderer-and-export.md` | 真实2D/3D与图形保存已接；surface未启用，固定camera偏差、参考机/RDP/GPU/像素仍待验收 |
| SEC-20-B / §20.6 | Shared Buffer/TypedArray与JSON回退 | 部分（JSON） | `src/FluoAxis.Visualization/PlotHostWebViewAdapter.cs` 的 `PostJson`；`src/FluoAxis.PlotProtocol/PlotMessageCodec.cs` | Shared Buffer未接；需feature detect、只读有界buffer、生命周期、fallback与峰值内存证据 |
| SEC-20-C / §20.7 | WebView2隔离与消息边界 | 工程实现；整机安全验收部分 | `src/FluoAxis.Visualization/PlotHostSecurityPolicy.cs`；`src/FluoAxis.Visualization/PlotHostWebViewAdapter.cs`；`tests/FluoAxis.Visualization.Tests/PlotHostSecurityPolicyTests.cs` | origin/type/schema/size/correlation有测试；需运行时外部请求观察、资源/CSP/导出攻击、release redaction |
| SEC-21 / §21 | 增量图更新、避免全量重建 | 部分 | `web/FluoAxis.PlotHost/plot-host.js` 使用 `Plotly.react` 和串行操作队列；`web/FluoAxis.PlotHost/plot-host.test.js` | 缩量更新/restyle/relayout预算、背压/队列合并、真实频繁更新与长时内存 |
| SEC-22 / §22 | 精确3D距离和数据身份 | 工程实现；实机交互部分 | `src/FluoAxis.PlotProtocol/PlotDataSnapshot.cs`；`src/FluoAxis.App/MainWindow.Explorer.cs`；`tests/FluoAxis.PlotProtocol.Tests/PlotDataSnapshotTests.cs` | 真点选择/表孔联动、C#距离、view保存已接；单位说明/复杂筛选后的实际点击验收，非BRAF dr′ |
| SEC-23 / §23 | 有界安全表达式DSL | 工程实现 | `src/FluoAxis.Presentation/ExplorerExpression.cs`；`src/FluoAxis.Contracts/ExplorerContracts.cs`；`tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs` | 白名单/AST/每页预算/取消/缺失值、派生定义hash已接；不提供任意脚本，真实可用性/性能资格 |
| SEC-24 / §24 | 可追溯视觉平滑 | 工程实现（页内行序） | `src/FluoAxis.Presentation/ExplorerDataModel.cs`；`tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs` | MA/SG2或3阶/奇数窗口3–51/边缘移动完整窗口，定义/hash保存且Raw隔离；跨页/不等间隔时间拟合不在当前范围 |
| SEC-25 / §25 | VirtualDataSheet | 部分（有界虚拟页及研究编辑） | `src/FluoAxis.Presentation/ExplorerDataModel.cs`；`src/FluoAxis.App/MainWindow.Explorer.cs`；`tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs` | ≤25k行页、≤10k手工行、覆盖/列单位/筛选排序/数值剪贴板已接；不是百万行通用Excel编辑表，真实大表基准待验 |
| SEC-26 / §26 | 不可变Report/HTML/PDF | 工程技术草稿已实现 | `src/FluoAxis.Contracts/ReportSnapshotDto.cs`；`src/FluoAxis.Contracts/ReportPlotFigureDto.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Reports.cs` | 科学来源、端点SVG/代表Raw曲线、Run/revision绑定Explorer PNG附录冻结；附图最终回归、nativePDF/长报告/字体QA和批准门禁未齐 |
| SEC-27-A / §27.1 | autosave/启动恢复 | 部分（事务恢复检测已接） | `src/FluoAxis.Infrastructure/PersistentDesktopStore.Diagnostics.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.cs`；`tests/FluoAxis.Infrastructure.Tests/WorkspaceDiagnosticsTests.cs` | 已提交事务、session.active、quick_check、非正常退出提示已接；未提交草稿不跨进程保存，断电/磁盘满/旧session迁移待验 |
| SEC-27-B / §27.2 | 原子保存与最后有效包 | 工程实现基线；故障资格部分 | `src/FluoAxis.Infrastructure/PersistentDesktopStore.Packages.cs`；`src/FluoAxis.Infrastructure/DesktopWorkspace.Delivery.Windows.cs`；`tests/FluoAxis.Infrastructure.Tests/PortableProjectPackageTests.cs` | 临时包关闭/重验/原子发布与外部事务写入；便携用户路径已通，原生目录rename code5未修；磁盘满/kill/replace恢复矩阵待补 |
| SEC-27-C / §27.3 | 结构化错误/path-free异常 | 工程实现基础 | `src/FluoAxis.Contracts/GatewayResult.cs`；`src/FluoAxis.Infrastructure/SafeDiagnostics.cs`；`tests/FluoAxis.Infrastructure.Tests/SafeDiagnosticsTests.cs` | 全应用异常/telemetry/crash接线、发布产物扫描、错误文案本地化与可执行推荐动作 |
| SEC-28 / §28 | IO/CPU预算、异步/取消 | 部分 | `src/FluoAxis.Infrastructure/PersistentDesktopStore.cs`；`src/FluoAxis.Presentation/WorkbenchViewModel.cs`；`src/FluoAxis.App/MainWindow.xaml.cs` | 当前每store单操作槽/最多1工作操作，离开dispatcher执行且关闭等待；全进程有界队列、取消延迟/超载/内存资格仍待测 |
| SEC-29 / §29 | 版本DTO/source-generated JSON | 工程实现；后续合同部分 | `src/FluoAxis.Contracts/Serialization/ScientificEvidenceJsonContext.cs`；`src/FluoAxis.Contracts/ExplorerContracts.cs`；`src/FluoAxis.Contracts/Serialization/ReportJsonContext.cs` | Gateway v3/Plot v1、科学来源/点页、view schema1、report/package及schema2→3已接；通用能力发现/完整版本矩阵待扩展 |
| SEC-30 / §30 | 启动/导入/计算/绘图/内存性能目标 | 部分（本地工程测量） | `tests/FluoAxis.GatewayContract.Tests/ExplorerResearchTests.cs`；`tests/FluoAxis.Infrastructure.Tests/FullPlateEngineeringAcceptanceTests.cs`；`docs/feature/FT-20261007-007-engineering-workflow-completion.md` | 有界页/公式/平滑/包/报告的本地测量以FT007为准，不作为SLA；参考机p95、WebView2合并内存、3D与长期泄漏待验 |
| SEC-31 / §31 | 依赖锁定、许可证/SBOM、升级回归 | 工程实现基础 | `Directory.Packages.props`；`DEPENDENCIES.md`；`build/Generate-DependencyEvidence.ps1`；`build/Publish-ResearchBuild.ps1` | 内部发布携带依赖证据和文件hash；发布许可证/安全复核、安装与离线升级资格待补 |
| SEC-32 / §32 | 不采用高风险旧技术/任意脚本 | 工程实现当前边界 | `src/FluoAxis.Import/XlsImporter.cs`；`src/FluoAxis.Presentation/ExplorerExpression.cs`；`build/Verify-ScientificSourceBoundary.ps1` | 无Excel COM、JS正式判读或eval；DSL限制与Raw隔离，输出器/依赖升级持续审查 |
| SEC-33-A / §33.1–33.3 | 数学/Golden/导入fixture | 部分；需科学批准 | `tests/FluoAxis.Analysis.Tests/`；`tests/FluoAxis.Golden.Tests/`；`samples/golden/candidate-manifest-v1.json`；`docs/feature/FT-20261007-007-engineering-workflow-completion.md` | 工程算法/来源/满板等最终结果见FT007；SCI-GATE、全部批准Golden和真实仪器fixture未关闭 |
| SEC-33-B / §33.4–33.5 | 绘图截图与PDF回归 | 部分 | `src/FluoAxis.Infrastructure/ReportRenderingSmoke.Windows.cs`；`tests/FluoAxis.Reporting.Tests/ResearchReportTests.cs`；`docs/feature/FT-20261007-007-engineering-workflow-completion.md` | FT006五页PDF是既往证据；新SVG/曲线/PNG附录未完成native视觉QA，UIA与截图不一致且用户Esc后停止GUI；真实长报告/3D/WebGL/字体/DPI矩阵待验 |
| SEC-34 / §34 | CI到签名包的流水线 | 部分（内部科研发布脚本已接） | `.github/workflows/ci.yml`；`build/Publish-ResearchBuild.ps1`；`docs/feature/FT-20261007-007-engineering-workflow-completion.md` | locked restore/build/tests/policy与未签名包、依赖/hash清单；自动UI/perf/nativePDF、签名/MSIX/干净机门禁未齐 |
| SEC-35 / §35 | MSIX、内部自包含、离线Runtime | 部分（内部自包含） | `src/FluoAxis.App/FluoAxis.App.csproj`；`build/Publish-ResearchBuild.ps1`；`docs/design/Research-User-Guide.md` | win-x64内部未签名包/用户说明，仍需本机WebView2；MSIX签名、安装卸载升级与离线Runtime分发未验收 |
| SEC-36 / §36 | Release设置、不激进AOT/trimming | 部分（发布profile已接） | `Directory.Build.props`；`src/FluoAxis.App/FluoAxis.App.csproj`；`build/Publish-ResearchBuild.ps1` | self-contained Release/无PDB/产物文本路径检查已接；不激进裁剪，ReadyToRun收益/回退与干净机资格待验 |
| SEC-37 / §37 | Diagnostics页、脱敏Support Bundle | 工程实现；故障资格部分 | `src/FluoAxis.App/MainWindow.Diagnostics.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Diagnostics.cs`；`tests/FluoAxis.Infrastructure.Tests/WorkspaceDiagnosticsTests.cs` | 白名单运行元数据/≤100事件、JSON≤64KiB、受管ZIP和保存；不含源/Raw/项目名/trace/异常堆栈；全应用crash/实机故障待补 |
| SEC-38 / §38 | source/Method/report hash、审计、事务 | 工程实现；发布资格部分 | `src/FluoAxis.Application/ScientificEvidenceCapture.cs`；`src/FluoAxis.Persistence/SqliteProjectRunRepository.Package.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Reports.cs` | source/mapping/input/Method/计算点/report/view hash、原子事务与旧缺项保持；unsigned hash不认证科学真实性，长期审计/恢复资格待验 |
| SEC-39 / §39 | RUO、本地隐私、无患者字段/外网 | 工程实现范围；输出链部分 | `src/FluoAxis.Domain/SampleAssignment.cs`；`src/FluoAxis.App/MainWindow.xaml`；`src/FluoAxis.Visualization/PlotHostSecurityPolicy.cs` | 固定字段不含患者身份、UI标RUO；自由文本不应宣称自动去标识化，需导出/support bundle/日志检查与本地网络观察 |
| SEC-40 / §40 | 键盘/UIA/DPI/状态文本/可操作错误 | 部分 | `src/FluoAxis.App/MainWindow.xaml`；`src/FluoAxis.Presentation/PlateDraftEditor.cs`；`docs/feature/FT-20261007-007-engineering-workflow-completion.md` | 响应式布局、孔板键盘/批量/dirty确认、研究表/图与状态文字已有；完整点击链、Narrator/High Contrast/125/150/200%DPI/触摸板仍待验收 |
| SEC-40-CAMERA / §40 | 3D Reset Camera | 部分（与当前交互决策冲突） | `docs/adr/ADR-002-plot-renderer-and-export.md`；`docs/fix/FX-20260906-001-remove-fixed-camera-reset-control.md`；`web/FluoAxis.PlotHost/plot-host.js` | 当前固定camera并移除UI reset；原需求未据此静默视为满足。须明确批准保留固定相机或恢复可交互camera/reset，并更新需求/ADR/验收 |

## 验收结论与推进规则

当前工程实现涵盖导入至分析、本机持久重开、全阶段已保存来源复核、单 Run 便携包、技术草稿 HTML/XLSX/PDF/冻结图、真实双通道 2D/3D Explorer 与有界研究编辑/公式/平滑/测距、视图/离线图形输出及诊断。包支持受检 schema 2→3，保留旧结果/缺项；限128 MiB总/64 MiB单项、1m signal points/2m行，普通文本8k且证据JSON有独立限额。拒绝覆盖身份，unsigned hash不认证科学真实性。

后续按设计 E1–E6 的未闭门禁推进：真实科学数据/双批准 Golden、完整桌面点击/辅助技术、参考机性能、固定相机需求裁决、异常恢复与签名/干净机离线安装。每项提升状态须补实现、正负/空/取消/stale证据和产物；类型名或测试数量不替代验收。用户保留科学草案、先推进工程，不等于已批准阈值、采样语义或临床适用性。
