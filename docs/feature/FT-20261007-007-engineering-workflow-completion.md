---
id: FT-20261007-007
kind: feature
status: validated
area: engineering-workflow-completion
stage: integrated-research-workbench
tasks: [complete-provenance, explorer-workflow, import-compatibility, report-figures, diagnostics, release-validation, ui-validation]
priority: high
branch: main
pr: null
paired_doc: docs/fix/FX-20261007-008-lqc-engine-mean.md
paths: [src/, tests/, build/, docs/design/, README.md]
contracts: [stage-evidence, raw-and-derived-isolation, explorer-view, import-options, report-snapshot, portable-project]
hotspots: [analysis-provenance, schema-migration, expression-budget, source-boundary, native-ui]
bug:
  repro: "The integrated workstation still lacks full stage evidence and operable advanced Explorer/import/delivery workflows."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [focused-contracts, independent-oracle, persistence-migration, malformed-import, bounded-explorer, report-render, full-suite, path-policy, release-build, native-smoke]
  tests_run: [source-suite-890-of-890, Analysis-88-of-88, Application-93-of-93, Infrastructure-82-of-82, Persistence-30-of-30, Reporting-after-final-fix-32-of-32, App-Debug-build-0-errors-0-warnings, relative-path-policy, scientific-source-boundary, partial-native-ui]
  benchmarks_run: [synthetic-96-wells-23232-points-single-run, synthetic-report-html-xlsx-single-run]
search:
  aliases: [complete software, complete trace, real 3D, formula builder, smoothing, XLS import, report charts]
---

# 完整科研工程工作流与桌面验收

## Summary

继续用户“完整全部完成”的工程授权，在当前 `main` 未提交工作树中完成全阶段科学来源证据、真实 Explorer、XLS/显式导入选项、报告图形、诊断恢复及科研构建链。保留其他既有修改，没有提交或推送。完整需求状态见 `docs/design/Requirements-Acceptance-Matrix-V1.1.md`，本记录不把所有 71 个 FR 及整机资格宣称为完成。

最终源码套件为 890/890 通过、0 失败、0 跳过；其后的报告负时间/极端时间显示修复通过 Reporting 32/32 聚焦测试。状态为 `validated`，表示本文工程范围已有验证；Method/Data Contract 仍是未批准草案，原生视觉、完整 picker 点击链、真实仪器 Golden 双批准和发布资格仍未关闭。

## Plan

1. 按当前 main 未提交工作树核对需求、科学合同与71项FR矩阵，保留既有修改。
2. 由独立代理分别负责全阶段证据/持久化、Explorer计算/UI、导入兼容；主代理负责集成、报告/诊断/交付及审查。
3. 每条纵向功能明确资源预算、空/错误/取消/陈旧响应和旧记录兼容；正式Analysis仅消费Raw RFU。
4. 聚焦验证后运行最终构建、全套测试、路径与科学边界审计，记录真实GUI和性能证据，再更新本记录和矩阵。

## Exploration log

基线为 FT006：810 项测试通过，工程包、技术草稿报告和真实二维 Raw 页已接通。对照需求、Method/Data Contract、plan、当前代码和测试确认 Calibration/WT 来源追溯、真实三维/公式/平滑/数据表、XLS/解析选项、报告图形、恢复诊断及发布验证的缺口，未把旧计划的拟实施状态当成当前实现。

按 contract/science、runtime、data/path、tests、UI 和 docs/review 拆分，复用现有 Analysis、Gateway、SQLite、ProjectFormat 与 WebView2。审查期间发现并修复：负时间前缀导致 NTC 检查点索引偏移、来源导入时间随 Run 更新时间漂移、来源中间点缺坐标仍可能标完整、trace well/point/algorithm 身份不一致、同表 FAM/ROX 部分信号列重叠、Explorer 错误码不在契约词汇中、报告图形允许空孔/通道、LQC 正式均值落在 Application 且阴性失败证据丢失。最后一项独立记录在配对 FX008。

首次 Infrastructure 聚焦结果 78/79：Explorer 篡改视图用例期望专用错误码，却得到 `SYSTEM_WORKSPACE_OPERATION_FAILED`。原因是 `EXPLORER_*` 不属于既有错误码前缀，改用 `PLOT_EXPLORER_*` 后再次聚焦通过；没有扩大错误码词汇。源码全套通过后又发现报告图形错误拒绝合法负 ElapsedTicks，以及 long 差值可能溢出，做显示边界窄修并重新运行 Reporting。上述失败与后续验证均保留，不用先前绿色结果覆盖晚期改动。

## Root cause

早期交付按最小 Guided 路径分阶段实现，部分模型/库能力未接入 UI 或持久证据。阶段摘要不足以说明每个计算点实际来自哪里；单孔计算成功、组 QC 状态和后续输入资格需要分别表达。旧记录缺字段必须兼容读取，不能通过重算补造历史结果；显示派生数据不能回流正式 Analysis。

## Implementation

| Concern | 主要文件 | 已实现行为与边界 |
|---|---|---|
| science/contract | `src/FluoAxis.Analysis/EvaluatedPoint.cs`、`BrafAnalysisEngine.cs`、`CalibrationModels.cs`、`ControlSampleModels.cs`、`CalculationTrace.cs`；`src/FluoAxis.Contracts/ScientificEvidenceDto.cs`；`src/FluoAxis.Application/ScientificEvidenceCapture.cs` | 保存实际 Positive 窗口、NTC Raw/Y/带内状态、WT 已计算参考、LQC/Sample 双通道端点及源行列；流式绑定 source/mapping/input/Method/algorithm 身份；组失败状态与数值 trace 状态分别展示 |
| evidence/query | `src/FluoAxis.Application/ApplicationGatewayEvidence.cs`；`src/FluoAxis.Persistence/SqliteProjectRunRepository.ScientificEvidence.cs`；`src/FluoAxis.App/MainWindow.Evidence.cs` | 三阶段只读摘要及每页最多 250 个保存检查点，revision 一致性校验；所有实际参与点均有合规源坐标才可完整；查询不重跑算法、不加载全 Run Raw |
| persistence/package | `src/FluoAxis.Persistence/SqliteSchema.cs`、`SqliteProjectRunRepository.Workflow.cs`、`SqliteProjectRunRepository.Package.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Packages.cs` | schema 3 增加实际源坐标、结束行、显式解析选项、阶段及 trace 证据和检查点；对确切已知 schema 2 先验证再升级私有解包数据库；包验证输入身份和检查点 digest，并可携带受检 Explorer 视图 |
| import/data-path | `src/FluoAxis.Import/XlsImporter.cs`、`ImportReadOptions.cs`、`DatasetBuilder.cs`；`src/FluoAxis.Contracts/ImportOptionsDto.cs`；`src/FluoAxis.Infrastructure/ManagedSignalPreparationAdapter.cs`；`src/FluoAxis.App/MainWindow.Import.cs` | XLS/BIFF 与 XLSX/文本格式边界，显式编码/分隔符/小数文化/时间单位，零基结束行和双通道区域；拒绝重叠来源信号单元、非法数值/轴/公式输入；实际 Raw 行列随点保留，不执行导入公式/宏/外链 |
| Explorer/UI | `src/FluoAxis.Contracts/ExplorerContracts.cs`；`src/FluoAxis.Presentation/ExplorerDataModel.cs`、`ExplorerExpression.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Explorer.cs`；`src/FluoAxis.App/MainWindow.Explorer.cs`；`web/FluoAxis.PlotHost/plot-host.js` | 真实双通道页、虚拟表、独立手工表、列名/单位、页内排序筛选/数值复制粘贴、编辑覆盖、受限公式、Moving Average/低阶 SG、2D/3D 列映射、第二序列/误差条、点/表/孔联动与视图保存；Raw 不可编辑，派生仅研究显示，相机仍固定 |
| report/delivery | `src/FluoAxis.Contracts/ReportSignalFigureDto.cs`、`ReportPlotFigureDto.cs`、`ReportSnapshotDto.cs`；`src/FluoAxis.Reporting/ResearchReportRenderer.Figures.cs`、`ResearchReportRenderer.Html.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Reports.cs`；`src/FluoAxis.App/MainWindow.PlotDelivery.cs` | 冻结来源表、端点 SVG、有限代表孔 Raw 曲线和可选 Explorer PNG 附录；HTML/PDF 只呈现保存值，XLSX 保留完整精度表；PNG 绑定 Run/revision，错误/过期输入拒绝；负时间可显示，坐标映射避免 long 差值溢出 |
| diagnostics/recovery | `src/FluoAxis.Contracts/WorkspaceDiagnosticsDto.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Diagnostics.cs`、`DesktopWorkspace.Diagnostics.Windows.cs`；`src/FluoAxis.App/MainWindow.Diagnostics.cs` | 会话标记和 SQLite 检查识别未正常退出，正常关闭清理会话状态；支持包只有白名单技术元数据及至多 100 个事件码，不带原始实验/项目内容 |
| release/docs | `build/Publish-ResearchBuild.ps1`；`build/dependency-metadata.json`；`.github/workflows/ci.yml`；`README.md`；`docs/design/` | 内部未签名科研构建、相对文件条目/大小/SHA-256 清单、机器路径扫描、依赖/许可证/SBOM 记录；CI 发现下限 890；用户指南及 71 项 FR 逐项实现/验收状态 |

`storedComplete` 表示已保存工程来源字段完整，不认证输入真实性或科学批准。旧 schema/旧 Run 缺 trace、坐标或均值仍为 `storedPartial`/`unavailable`/null；不补造身份或重算历史结果。schema 2 归档迁移只写私有暂存副本，原包不被改写；未来/恶意 schema、trigger、冲突身份仍拒绝。真实文件 picker 的外部路径仅在 Infrastructure 内瞬态使用，DTO 和包持久身份只保留安全名称、hash 与相对条目。

配对 FX008 将 `LqcMeanDrPrime` 移到 Analysis 作为输出：单孔方向一致后计算，一致阴性组保留 mean/direction 但 Run 仍 Invalid；冲突组不计算均值，WT 正式后续参考仍按 QC Pass 门控。应用、UI 和报告只保存/呈现。没有改科学阈值、时间容差、重复孔规则或受控标签。

资源预算继续使用 store 单操作槽，不新增无界 worker 或共享计算缓存。Raw 配对页最多 25,000 行，独立手工表最多 10,000 行；公式最多 1,024 字符/128 节点/16 层和每页 2,000,000 次求值，平滑奇数窗口 3–51 且只作用当前页。科学点分页最多 250；阶段 JSON 上限 2 MiB、单 trace 来源 JSON 65,536 字符。工程包总量 128 MiB、单 entry 64 MiB、Raw 1,000,000 点、总数据库 2,000,000 行，现有缓冲受限但不宣称全流式。报告 Raw 图最多 6 个代表孔、共 12 条通道曲线、每曲线前 500 点并明确显示范围；Explorer 附图仅缓存一张，上限 1 MiB/1,600 万像素。PDF 输入/输出/超时等原有预算继续生效；显式超限报错，不静默扩大用户预算。

## Validation

验证采用串行 SDK 构建，聚焦通过后再全套。最终源码测试命令为 `dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 890`，实际 890/890 通过、0 失败、0 跳过，耗时 28.699 s。此后唯一生产代码变更是报告合法负 ElapsedTicks 与极端时间坐标缩放修复，执行 `dotnet test tests/FluoAxis.Reporting.Tests/FluoAxis.Reporting.Tests.csproj --no-restore --verbosity minimal`，32/32 通过；不把此前全套表述为最后这处改动之后再次执行。

已记录聚焦命令及实际结果：

| 命令 | 结果 |
|---|---|
| `dotnet test tests/FluoAxis.Analysis.Tests/FluoAxis.Analysis.Tests.csproj --no-restore --verbosity minimal` | 88/88 通过 |
| `dotnet test tests/FluoAxis.Application.Tests/FluoAxis.Application.Tests.csproj --no-restore --verbosity minimal` | 93/93 通过 |
| `dotnet test tests/FluoAxis.Infrastructure.Tests/FluoAxis.Infrastructure.Tests.csproj --no-restore --verbosity minimal` | 82/82 通过，含 FX008 与 Explorer 错误码修复；晚期新增用例随后纳入 890 全套 |
| `dotnet test tests/FluoAxis.Persistence.Tests/FluoAxis.Persistence.Tests.csproj --no-restore --verbosity minimal` | 30/30 通过，含 schema 2→3 与恶意旧 schema 拒绝 |
| App Debug 构建 | 0 错误、0 警告；测试项目 clean build 共 241 条既有/新增 analyzer 警告，不能宣称全仓零警告 |
| `./build/Verify-RepositoryPolicy.ps1`、`./build/Verify-ScientificSourceBoundary.ps1`、`git diff --check` | 本轮均通过；FX008 时路径扫描为 372 个文本文件，本记录收尾时重新扫描 376 个文本文件通过，registry 48 条合法单行 JSON、ID 均唯一 |

关键回归包括 `ScientificEvidenceTests.cs`、`ScientificInputIdentityTests.cs`、`ScientificSchemaMigrationTests.cs`、`ScientificEvidenceRoundTripTests.cs`、`ExplorerViewStoreTests.cs`、`ExplorerResearchTests.cs`、`ImportOptionsTests.cs`、`WorkspaceDiagnosticsTests.cs`、`FullPlateEngineeringAcceptanceTests.cs` 及既有报告/包/工作流测试。覆盖精确计算来源、输入与 point digest 篡改、负时间原索引、旧结果兼容、同表区域冲突、表达式预算、取消/错误/陈旧响应、真实 CSV 与完整 96 孔持久化往返。合成数据的已知预期不是实验负责人批准的 Golden。

最新 96 孔测量文件为 `artifacts/benchmarks/FullPlateEngineeringAcceptanceTests/176052/918765d841d14901b03a91b097e55417/measurement.json`：一次运行、并发 1，96 孔×121 时点×2 通道 = 23,232 点；44 个双重复样本，22 阳性/22 阴性且全部组通过，三阶段 input hash 一致，跨工作区项目包重开后的完整 evidence JSON 一致。

| 测量阶段 | 实测 ms |
|---|---:|
| 存储启动 | 30.8287 |
| staging/预览/映射 | 353.3226 |
| 全部科学阶段 | 479.1029 |
| 双通道 Raw 页 | 24.9473 |
| 快照/HTML/XLSX | 344.2279 |
| 项目打包 | 590.6957 |
| 导入重开 | 1006.0001 |

该场景 source SHA-256 为 `7b7e51edd92b0d4d3cde53841a01992a59c25f47e045eb754efedf4fb38e7b27`，input snapshot SHA-256 为 `1516b1b5028b9d900be0611b2f3d07990eee6c9d99a23bbb1d9e78758150a651`。运行环境为 .NET 10.0.12、Windows 10.0.22631、X64、20 个逻辑处理器；process-wide allocation 289,748,656 bytes 可能包含其他测试分配，不能当作峰值内存或本场景独占分配。没有可比 before、参考机资格或 SLA 结论。

`artifacts/reports/ft006/measurement.json` 路径已在本轮重新生成，最新单次 HTML 为 23.9295 ms / 32,931 bytes，XLSX 为 132.1836 ms / 18,886 bytes，process-wide allocation 4,102,416 bytes。环境 .NET 10.0.12、Windows 10.0.22631、Intel Family 6 Model 183 Stepping 1、20 逻辑处理器；合成 fixture SHA-256 为 `d927f3ff1f8c14acad1ed79c005d2f9fb4bc7ccb71f930d72bc22af86bb36153`，当前 snapshot SHA-256 为 `ce81f7d2fa8c6a4d71365ebb61493ff3f76c54d417dca354c8507a04a45eeb30`。旧 FT006 五页 PDF 的目视结果仅适用当时快照，不能移用到现在图形/附图版本，也不能认为目录所有格式同 hash。

Release 初次科研 publish 完成并记录 545 个文件。后续 `artifacts/release/FluoAxis-research-win-x64-ft007-r2/research-build.json` 包含 545 个文件，标记 `unsigned-engineering-build`、`draft-unapproved` 和 `may-include-local-changes`；r2 publish 进程曾被中断，2026-10-08 已独立逐文件核对清单 hash，全部匹配。包含 DPI 修复的最终科研构建为 `artifacts/release/FluoAxis-research-win-x64-20261008-dpi/`，publish 退出码 0，545 个文件及 EXE DPI 清单验证通过，详见 FX-20261008-001。需要本机 Evergreen WebView2，不声称干净机离线安装、签名、升级或发行资格已验收。

原生桌面只完成有限证据：独立工作区新建项目和系统 picker 打开已观察到；PickerHost 无法被工具定位，未完成真实文件选择→全部分析→导出的点击链。通过工程包往返验证后用 Debug 测试入口注入合成项目，UIA 树读到 revision 5、样本阳性、dr′ 0.4、Input/Mapping/Source hash 与双通道源行列。该注入不改正式 picker，也不替代它的验收。截图与 UIA 状态不一致，未据此判定视觉通过；用户 Esc 后停止所有 GUI 输入。后续“全窗文字发虚”的修复单独进入 `docs/fix/FX-20261008-001-native-dpi-clarity.md`，FT007 不声称该问题已解决。

路径/热点核查：源码与记录仅相对路径；测试根由仓库位置派生、包含类/进程/GUID，清理前 containment 验证；真实跨根 package save/open 保持 Method/input/source 和已有结果；不依赖 cwd。没有 UI/JavaScript/report 正式公式，没有将平滑/公式值写回 Raw 或 Analysis，没有保留额外全量信号/固定分析加载器，没有新增共享缓存或 worker 过订阅。大包峰值内存、长时泄漏、全生成产物路径审计、断电/目录移动系统矩阵仍需单独资格证据。

## Review outcomes

多代理分别负责科学契约/持久化、导入/路径、Explorer/UI 以及报告/恢复/集成，并完成交叉静态审查。已确认的来源、区域重叠、错误码、trace 身份、报告 DTO 和 LQC 均值问题均已有修复及相应回归；FX008 的独立审查确认 Negative 一致组保留 mean/direction，WT 输入资格仍被 QC 门控，旧 null 不回填。

本轮没有创建 PR 或触发 GitHub/外部 Codex review，使用独立代理和手工正确性/边界/兼容/资源/RUO 清单。没有已知未答复的本轮代码审查意见；完整可访问性、视觉、科学批准和整机交付不属于已通过的结论。维持 `validated`，不升级成所有需求或发布完成。

## Follow-ups

1. 科学负责人批准 Method/Data Contract、真实仪器格式/采样/端点/分母下限/LQC 判据、独立参考计算与 Golden 双批准；不从工程合成通过推导临床或方法学有效性。
2. 原生完整导入→映射→孔板→分析→保存/打开→附图报告点击链、图形/长报告/字体视觉验收，以及用户提出的全窗文字清晰度修复。后续 DPI 工作独立留档。
3. 三维相机交互仍为固定模式，未关闭原始交互需求；Narrator、High Contrast、键盘及多 DPI/显示器、RDP/GPU 和目标参考机性能资格。
4. 大输入峰值内存、100 次导入/视图生命周期泄漏、最大包/断电恢复/完整历史迁移矩阵；现有有界回归和 96 孔单次测量不代替这些资格。
5. r2 清单文件/hash 核对与后续 DPI 科研构建已完成；签名、干净机离线安装、升级/卸载、外部发行审查单独验收，实验数据与机密内容不进入默认支持包。
