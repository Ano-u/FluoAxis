# 需求完成度矩阵（2026-10-07）

本次按当前 `main` 未提交工作树核对实现。精确构建、测试数量、性能与视觉条件集中维护于 `docs/feature/FT-20261007-007-engineering-workflow-completion.md`；71 项原始 FR 的逐项判断见 `docs/design/Requirements-Acceptance-Matrix-V1.1.md`。“工程实现”不表示科学方法或整机发布获准。

| 领域 | 当前工程能力 | 可定位证据 | 尚缺门禁/范围 |
|---|---|---|---|
| 分层与接口 | Gateway v3、保存证据、科学点分页、双通道 Raw 页、报告/工程包/Explorer 视图 | `src/FluoAxis.Contracts/`、`tests/FluoAxis.GatewayContract.Tests/` | 通用 capability discovery、跨历史版本完整矩阵 |
| BRAF Analysis Engine | 草案规则、顺序/数值/重复孔 fail-closed、全精度追溯 | `src/FluoAxis.Analysis/`、Analysis tests、Golden candidate tests | Method/Data Contract 签字、真实仪器 fixture、独立参考与 Golden 双批准 |
| 全阶段来源 | 已执行阶段的端点、源行列、窗口/逐孔值、source/mapping/input/Method/algorithm 身份；计算点独立分页 | `src/FluoAxis.Application/ScientificEvidenceCapture.cs`、`src/FluoAxis.Contracts/ScientificEvidenceDto.cs`、`src/FluoAxis.App/MainWindow.Evidence.cs` | 失败前未执行的阶段不补造；历史缺项仍部分；科学和实际用户复核 |
| 导入 | XLSX/XLS/CSV/TSV/TXT；单文件拖放/选择；显式编码、分隔符、小数文化与时间单位；FAM/ROX 区域结束行 | `src/FluoAxis.Import/`、`src/FluoAxis.App/MainWindow.Import.cs`、`tests/FluoAxis.Import.Tests/ImportOptionsTests.cs` | 真实仪器布局/异常 fixture、上限输入内存、原生拖放/取消/映射整机验收 |
| 96 孔 Plate Map | 全 Sample 元数据、矩形/批量编辑、20 步撤销/重做、dirty 确认，与 Explorer 点/表联动 | `src/FluoAxis.Presentation/PlateDraftEditor.cs`、`src/FluoAxis.App/MainWindow.Explorer.cs` | 满板真实数据、屏幕阅读器、完整键盘/DPI/High Contrast |
| Guided UI | 响应式布局、分列结果、项目分页重开、全阶段来源 viewer、后端动作驱动 | `src/FluoAxis.App/MainWindow.xaml`、`src/FluoAxis.Presentation/WorkbenchViewModel.cs` | 独立工作区新建/picker及经验证Debug合成项目的UIA结果/来源已读取；截图不一致，完整点击/视觉验收未完成 |
| 持久工作区与恢复 | SQLite/WAL、独占租约、单操作槽、事务持久重开、关闭等待、会话标记与启动完整性检查 | `src/FluoAxis.Infrastructure/PersistentDesktopStore.cs`、`src/FluoAxis.Infrastructure/PersistentDesktopStore.Diagnostics.cs` | 旧随机会话迁移、自动清理、真实断电/磁盘满恢复资格；编辑草稿不跨进程保存 |
| 保存证据与 schema | schema 3；schema 2 工程迁移；新来源可 storedComplete，旧记录保留 partial/null | `src/FluoAxis.Persistence/SqliteSchema.cs`、`tests/FluoAxis.Persistence.Tests/ScientificSchemaMigrationTests.cs` | 完整性是工程字段标记，不认证科学真实性；不支持任意历史/未来结构 |
| Explorer | 真实双通道页、虚拟表、独立手工表、列/单位、排序筛选/覆盖、DSL/平滑/统计、2D/3D、误差条/测距/点身份 | `src/FluoAxis.Presentation/ExplorerDataModel.cs`、`src/FluoAxis.Presentation/ExplorerExpression.cs`、`src/FluoAxis.App/MainWindow.Explorer.cs` | 当前页有界处理；百万行通用表、整机性能/长时泄漏和相机需求偏差仍待验收 |
| Explorer 视图与输出 | 视图定义/hash 持久化及包往返；PNG/适用 SVG 保存；自包含离线 HTML | `src/FluoAxis.Infrastructure/PersistentDesktopStore.Explorer.cs`、`src/FluoAxis.Visualization/OfflinePlotDocument.cs` | 视图仅描述研究显示；独立浏览器/WebGL/保存 picker 的完整实机矩阵 |
| 可携带工程包 | 所选项目/Run+源副本+可选视图；验证输入/计算点 hash；受检 schema 2→3，拒重复身份 | `src/FluoAxis.Infrastructure/PersistentDesktopStore.Packages.cs`、`tests/FluoAxis.Infrastructure.Tests/PortableProjectPackageTests.cs` | 大包/故障/发布资格；既往 Windows 嵌套目录 rename code 5 未被便携包往返证明已修复 |
| 技术草稿报告 | 冻结证据/身份、HTML/XLSX/PDF、端点 SVG/代表孔 Raw 曲线、可选真实 Explorer PNG 附图、快照/输出 hash | `src/FluoAxis.Reporting/`、`src/FluoAxis.Contracts/ReportPlotFigureDto.cs`、`src/FluoAxis.Infrastructure/PersistentDesktopStore.Reports.cs` | 附图unit/backend最终测试及原生附图PDF视觉证据以FT007为准；正式科学批准、长报表/字体/设备资格待验 |
| 诊断与支持 | 诊断页、恢复状态、最多 100 个白名单事件、受限支持 ZIP、外部保存 | `src/FluoAxis.Infrastructure/PersistentDesktopStore.Diagnostics.cs`、`src/FluoAxis.App/MainWindow.Diagnostics.cs` | 全应用 crash/telemetry 接线与真实异常恢复矩阵；支持包不含 Raw/源/项目名/异常堆栈 |
| 内部发布 | 相对输出根、固定依赖、无 PDB、自包含科研目录、hash manifest、依赖证据与用户说明 | `build/Publish-ResearchBuild.ps1`、`docs/design/Research-User-Guide.md` | 未签名；MSIX、干净机、WebView2 分发/离线安装升级与正式发布验收 |

## 科学计算与追溯

正式计算只消费 Raw RFU。Positive 平台先于 NTC；NTC 用各自 Fb 独立检查；WT 给二维参考；LQC/Sample 先单孔分类后复核一致性。数值溢出、缺端点、缺通道、非法分母与重复孔冲突失败关闭，显示舍入和研究派生列不影响分类。

保存来源新增 ScientificEvidenceDto 与双通道 trace provenance，输入身份包含源、映射选项、孔位赋值、原始点及 Method。Positive 窗口与 NTC 计算点持久化后按最多 250 点读取，包含实际 Raw RFU、已保存归一化值、源行列和 NTC 带内状态；读取不重算。数据不足、失败前未执行的阶段和旧 schema 缺项保持真实状态。storedComplete 不能解释为已获得科学批准。

## 工程预算与兼容

工程包传输限总量 128 MiB、单项 64 MiB、1,000,000 signal points、2,000,000 总行；普通文本值限 8,192 字符，阶段证据 JSON 和 trace provenance 各有独立较宽上限。受检 schema 2 包在隔离工作副本中迁移到 schema 3，旧 Method hash、结果及来源缺项保持；未知结构、额外触发器/表、hash 不符和身份冲突拒绝。

Explorer 原始页限 25,000 行，双序列图限 50,000 点；手工表限 10,000 行，派生列限 8，覆盖单元格限 5,000。公式限 1,024 字符、128 节点/16 层和每页 2,000,000 次求值；平滑窗口为奇数 3–51，SG 为 2/3 阶。平滑按当前页行序列处理，不是跨页或真实物理时间拟合；统计只覆盖当前筛选页。视图限 4 MiB；离线图最多 50,000 点、16 序列、8 MiB JSON、16 MiB HTML。

报告曲线冻结每类角色代表孔的前最多 500 点，最多 12 条，明确显示本页/总点数，不能当作全部实验曲线。端点图仅渲染保存的 Y 坐标。“报告附图”将当前真实 Explorer 图输出为 PNG 后冻结到报告，限一张、1 MiB/1,600 万像素且绑定 Run/revision；手工或派生图作为研究显示附录，不产生新判定。连续同 Run/revision 输出复用最近快照是有界缓存，不承诺跨所有会话永久复用同一 Report ID。

## 验收状态

本轮工程回归、构建、路径与科学边界审计的最终结果以 FT007 为准。独立工作区已观察到新建项目及原生 picker，自动化未定位 PickerHost；随后用经工程包验证的合成项目进行 Debug 注入，UIA 树读取到 revision 5、样本阳性/dr′ 0.4、完整 Input/Mapping/Source hash 和双通道源行列。该读侧证据不替代选择文件→映射→校准→结果→导出的完整点击链。截图与 UIA 不一致，未声称视觉验收完成；用户按 Esc 停止后已停所有 GUI 输入。既往 FT006 五页 PDF 检查不代替本轮新增图形/PNG 附录的原生视觉验收；附图 unit/backend 新测试等待最终验证记录。

继续工程使用可按 `docs/design/Research-User-Guide.md` 复核工作流；宣布正式完成前仍需签字 Method/Data Contract、真实去标识化仪器数据与独立参考、Golden 双批准、辅助技术、参考机性能、故障恢复、签名与干净机离线安装证据。当前实现不能据此宣称临床适用或方法已验证。
