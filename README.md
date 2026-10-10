# FluoAxis

Windows 原生科研荧光数据分析工作站（Research Use Only）——双通道 FAM/ROX 动力学荧光数据的导入、校准、质控、dr' 判读、追溯与可视化。

> 仅供科研使用，不用于临床诊断、治疗决策或患者管理。本软件结果必须结合已批准的研究方案、原始数据和人工复核解释。

## 当前状态

2026-10-10 绘图区更新：当前科研包为 `artifacts/release/FluoAxis-research-win-x64-20261009-viewport/FluoAxis.App.exe`。数据与图形页使用完整可视工作区，设置/数据表按需打开，支持放大/还原；实际 960×720 窗口中画布为 829×461 DIP，放大后为 829×532 DIP。修复三维轴标题裁剪、图表/表格双向选择、清空选择后图点淡化，以及密集图形错误提供 SVG。全套 981/981、PlotHost 12/12、发布版真实 UIA 15 步通过。PNG/SVG 原生保存文件的 3200×1800 尺寸和曲线像素通过验证。资源清单 549 项运行后仍匹配；可签名 MSIX 为 `artifacts/release/FluoAxis-msix-20261010-viewport/FluoAxis.Research_1.0.0.0_x64_unsigned.msix`，MakeAppx 校验与 554 文件 hash 往返通过。保留完整 EXE 同级目录，退出旧窗口后启动新版本。修复记录见 `docs/fix/FX-20261009-002-explorer-selection-export.md` 和 `docs/fix/FX-20261009-003-plot-visible-workspace.md`；当前无证书、方法仍为草案，不能作为正式发布资格通过，签名交接见 `docs/design/Release-Qualification-Guide.md`。

2026-10-09 历史绘图修复保留：Plotly 动态样式 CSP、WebView 缓存写入安装目录和另存为同步 UIA RPC 修复记录见 `docs/fix/FX-20261009-001-plot-layout-picker.md`。其滚动页面高度方案已由上述可视工作区布局替代。

2026-10-08 人工复核修复（历史记录）：此前科研包为 `artifacts/release/FluoAxis-research-win-x64-20261008-refinement/FluoAxis.App.exe`。修复“从此文件重新设置”时旧控件事件引发的崩溃、预览解析选项被重置、方案不能整体撤销及结果页残留处理中提示；孔位区域简写和解析选项展开减少编辑负担。全套 970/970 通过，原生窗口完成自动与人工继续、历史保留、秒单位、元数据复核、方案撤销/重做和样本身份验证。正式发布 EXE 在隔离空工作区和现有工作区均启动通过；549 项资源 hash/尺寸全部匹配，含清单共 550 个文件。保留整个同级目录并退出旧窗口后运行新 EXE。验证不代替 native picker 完整点击或科研方法批准；详情见 `docs/fix/FX-20261008-005-smart-refinement.md`。既往自动流程与资源修复记录为 `docs/feature/FT-20261008-004-assisted-import-analysis.md`、`docs/fix/FX-20261008-003-release-startup.md`。

主入口“导入并自动判读”自动新建项目并执行既有分析链。默认方案为 A1=100% 阳性、A12=NTC、H1=WT、H12=LQC、B2–G11=样本，其余忽略；方案可修改保存。明确时间单位/双通道标签齐备即可直接分析，缺项或冲突转人工；对照不从 RFU 猜测。保留“仅导入预览”“确认设置并继续自动分析”和“从此文件重新设置”，后者建立新运行而保留旧结果。

UI 沿用浅色/深色/高对比语义配色，突出当前步骤和主按钮，项目管理与识别依据按需展开，报告导出固定页底。原生 XAML 布局及真实 WebView 宽窄窗二维/三维像素已有验证；High Contrast 实机和完整辅助技术验收仍待完成，历史布局记录见 `docs/feature/FT-20261008-002-workbench-visual-hierarchy.md`。

高 DPI 修复仍保留：原生窗口从 DPI Unaware / 96 DPI / 1.0 倍变为 Per-Monitor V2 / 144 DPI / 1.5 倍。修复依据见 `docs/fix/FX-20261008-001-native-dpi-clarity.md`，本次发布 EXE 再次通过 DPI 清单检查。

科研工程验证版本。当前解决方案包含 13 个生产项目和 15 个测试项目，正式科学参数仍按未批准的 Method 草案执行：

```text
src/           Domain → Analysis → Contracts → Application
               Import（OpenXML 流式 / BIFF / 显式文本解析）→ Infrastructure（受管资源边界）
               Persistence（SQLite）→ ProjectFormat / Reporting；PlotProtocol → Visualization
               Presentation / App（WinUI Guided 工作台、持久项目目录与保存证据复核）
tests/         每层镜像测试 + Golden.Tests
```

WinUI 3 中文响应式工作台已接通文件选择/单文件拖入、托管 staging、XLSX/XLS/CSV/TSV/TXT、编码/分隔符/小数文化/时间单位显式选择、FAM/ROX 区域与结束行映射、96 孔角色和完整 Sample 元数据编辑、校准、质控及逐孔结果。孔板支持矩形选择、批量赋值、20 步撤销/重做及未提交编辑确认。桌面使用 SQLite/WAL、独占租约与单操作槽；目录分页并可重启读取已保存 Run。关闭等待活动操作后释放存储，动作由后端 `allowedActions` 驱动。

可携带 `.fluoaxisproj` 只打包所选项目/Run、源副本及可选 Explorer 视图，验证 manifest、SQLite schema、Method/source/input hash，打开时不覆盖现有身份。当前 schema 3 支持受检 schema 2 的工程迁移，保留旧结果和缺失证据。技术草稿报告使用不可变快照，支持 HTML、XLSX 与原生 WebView2 PDF；同一当前 Run/revision 的连续输出复用最近快照。HTML/PDF 呈现已保存端点 SVG、有界代表孔 Raw 曲线及可选 Explorer 冻结 PNG 附图。附图绑定 Run/revision，限 1 MiB/1,600 万像素，只缓存一张；XLSX 保留冻结表格，正式判读不受附图影响。

保存证据覆盖 Calibration、WT、LQC 与 Sample 的实际已计算值、双通道端点、逻辑源行列及 source/mapping/input/Method 身份。Positive 窗口与 NTC 检查点每页最多 250 点，只读展示保存值。新记录可标记 `storedComplete`；旧记录保持 `storedPartial`/`unavailable`，不补造历史来源。该完整性标记不代表科学批准。

Explorer 按孔读取真实双通道 Raw 页（最多 25,000 行），提供虚拟化表格、独立手工表、列/单位、排序筛选、编辑覆盖、受限公式、Moving Average/低阶 SG、二维/三维列映射、第二序列、误差条、测距及点/表/孔联动。派生数据不进入正式 Analysis；平滑和统计只作用于当前页。视图可持久保存，图形可经保存选择器输出 PNG/适用 SVG 或包含固定本地资源的离线 HTML。三维相机保持固定，原始需求的相机交互尚未关闭。

诊断页和支持包只输出白名单技术元数据及最多 100 个事件码；启动通过会话标记和 SQLite 检查识别非正常关闭。`build/Publish-ResearchBuild.ps1` 提供未签名内部科研包，说明见 `docs/design/Research-User-Guide.md`。工程包总量限 128 MiB、单项限 64 MiB。科学签字、真实仪器 fixture、Golden 双批准、完整可访问性/参考机性能、签名及干净机离线安装仍是独立门禁。

较早的 Debug 注入与 picker 定位限制保留在 `docs/feature/FT-20261007-007-engineering-workflow-completion.md`；当前原生文件选择→结果→导出→重开链路已以发布版 UIA 独立验证，仍使用合成去标识化 fixture。原生附图 PDF 视觉验收及其他需求缺项以 `docs/design/Requirements-Acceptance-Matrix-V1.1.md` 为准，不由此次绘图区修复自动关闭。

## 文档地图

| 文档 | 角色 |
|---|---|
| `BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md` | 需求冻结基线 |
| `plan.md` | 实施总计划（阶段、门禁、验证矩阵） |
| `CLAUDE.md` / `AGENTS.md` | 工程宪法（架构不变量、相对路径 P0 政策） |
| `docs/method/Method-Specification-V1.0.md` | 判读规则实现契约（代码与测试的科学真源） |
| `docs/method/Data-Contract-V1.0.md` | 数据契约与未决科学问题状态表 |
| `docs/adr/` | 架构决策记录 |
| `DEPENDENCIES.md` | 依赖登记（固定版本） |
| `docs/design/Research-User-Guide.md` | 科研工程版操作与故障处理 |

## 构建与测试

需要 .NET 10 SDK（`global.json` 锁定 10.0.400）。

```powershell
dotnet restore FluoAxis.slnx --locked-mode
dotnet build FluoAxis.slnx --no-restore
./build/Verify-RepositoryPolicy.ps1
./build/Verify-ScientificSourceBoundary.ps1
dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 981
```

测试命令使用 .NET 10 Microsoft.Testing.Platform 的模块发现模式；`--minimum-expected-tests` 防止空测试程序集或发现回归被误判为成功。新增或删除测试时必须同步更新该下限。

## 核心不变量

1. Raw RFU 不可变；2. C# Analysis Engine 是唯一科学真源；3. 当前草案 `dr' >= 0.4` 为阳性侧；4. 重复孔冲突为 INVALID；5. 项目身份和归档路径只能相对；6. 运行资产本地锁定、发布需完成离线验收。完整清单见 `CLAUDE.md` §47。
