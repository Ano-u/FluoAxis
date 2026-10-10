---
id: FT-20261007-006
kind: feature
status: validated
area: delivery-and-workbench
stage: guided-product-workflow
tasks: [portable-project, immutable-report-export, responsive-ui, batch-plate-editing, bounded-raw-explorer, correctness-review]
priority: high
branch: main
pr: null
paired_doc: null
paths: [src/FluoAxis.Analysis/, src/FluoAxis.Domain/SignalSeries.cs, src/FluoAxis.Contracts/, src/FluoAxis.Infrastructure/, src/FluoAxis.Persistence/, src/FluoAxis.Reporting/, src/FluoAxis.Presentation/, src/FluoAxis.App/, tests/, docs/design/, README.md, DEPENDENCIES.md, FluoAxis.slnx, .github/workflows/ci.yml]
contracts: [immutable-analysis-input, project-package, report-snapshot, raw-signal-page, explicit-plate-assignment, stored-evidence, relative-path]
hotspots: [PersistentDesktopStore, MainWindow, BrafAnalysisEngine, ReportSnapshot, project-import, raw-page-revision]
bug:
  repro: "The current guided workspace persists runs but exposes no portable project or report workflow; narrow-window layout and plate editing remain cumbersome."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [focused-science, archive-roundtrip, export-injection, report-snapshot, workbench-contract, full-suite, path-audit, live-ui]
  tests_run:
    - "dotnet restore FluoAxis.slnx --locked-mode: passed"
    - "dotnet build FluoAxis.slnx --no-restore: passed, 0 errors, 3 existing test analyzer warnings"
    - "dotnet test FluoAxis.slnx --no-restore --verbosity quiet: 810 passed, 0 failed, 0 skipped, 14.009 s"
    - "dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 810: 810 passed, 0 failed, 0 skipped, 15.226 s"
    - "node --test web/FluoAxis.PlotHost/plot-host.test.js: 7 passed"
    - "./build/Verify-RepositoryPolicy.ps1: passed, 330 repository text files"
    - "./build/Verify-ScientificSourceBoundary.ps1: passed, 14 presentation files"
    - "./build/Generate-DependencyEvidence.ps1: passed, 42 components"
    - "Debug native report smoke: PASS, 5 PDF pages inspected"
    - "git diff --check: passed"
  benchmarks_run:
    - "artifacts/reports/ft006/measurement.json: synthetic report HTML 14.2087 ms, XLSX 136.8385 ms; single iteration, no SLA"
search:
  aliases: [portable project, report export, batch plate editing, responsive workbench, complete software]
---

# 可携带工程、报告输出与工作台交互完善

## Summary

本轮主分类为 feature，包含算法输入/结果不可变性和失败边界修复。持久 Run 已能导出/打开单 Run 工程包、生成冻结技术草稿报告；工作台支持响应式导航、批量孔板编辑、逐孔结果复核和真实 Raw RFU 分页绘图。Method/Data Contract 按用户决定继续保留草案，数值阈值没有更改。

最终源码测试与 CI discovery gate 均为 810/810 通过；这是所述工程范围的验证，不代表全部需求完成、所有潜在缺陷消除或科学方法已批准。71 项 FR 的逐项状态见 `docs/design/Requirements-Acceptance-Matrix-V1.1.md`。本轮继续当前 `main` 未提交工作树，保留已有修改，未创建提交或推送。

## Plan

1. 以当前 main 工作树检查需求、Method/Data Contract、归档库、保存证据和UI。
2. 按 runtime/data-path、report-contract、UI、review 分工；先明确接口，再实现。
3. 复用 ProjectFormat、SQLite、OpenXML 和已保存 evidence；不在 UI/报告重算科学公式。
4. 执行恶意输入、移植/重开、快照不可变、数值与 UI 回归；fresh build/test 后更新本记录。

## Exploration log

开始时已有本机持久重开和只读部分 trace，737 项是上一轮测试证据。首先按需求基线、plan、Method/Data Contract、ADR 和当前代码确认工程包/报告/UI 缺口；按 contract、runtime/data-path、tests、UI、docs/review 分工，复用已有归档、SQLite、OpenXML 和 WebView2。

审查确认 SignalSeries 原先暴露可强制转回数组的 Points，部分分析输入/输出保留调用方集合引用；校验后仍可能被调用方修改。直接调用引擎时，重复孔可能被重复加权，伪造 Passed 但缺 Fm/Fb 的 Calibration 可引发非结构化错误。新增回归后做防御性冻结、阶段唯一性/Run 一致性和缺项检查，没有改动有效数据的数学顺序。

工程包最初考虑复制整个数据库再删除无关记录，审查认为 SQLite freelist 仍可能携带其他项目内容，故改为新建数据库并仅复制白名单表中的所选身份行。报告首次预览与导出分别创建时间戳，导致同一 Run/revision 的 Report ID 不一致，改为有界最近快照复用，并验证已落盘 canonical JSON。进一步审查发现只限制单文本/序列化后大小不足以约束冻结前的总文本，新增序列化前 1 MiB 总 UTF-8 文本预算及回归。

原生 PDF 首次实测失败于 NavigateToString：WebView2 内部导航使用 data 文档，被外部导航禁用规则误拦截。修正为只允许关联的首个受控 about:blank/data:text/html 文档，以 NavigationId 匹配完成事件，后续导航持续阻止；再次原生 smoke 输出 PASS，五页 PDF 逐页检查。UI 审查还修复跨项目批量字段残留、替换来源时草稿静默丢弃、同 revision 失败刷新清草稿及元数据截断问题。

用户在本轮 GUI 验证中按 Escape 停止 Computer Use，此后未继续桌面自动化。保留此前有限页面检查与 PDF 证据，未把后端测试宣称为完整点击流程验收。

## Root cause

工程组件先于桌面交付链实现，导致存储、报告、归档及真实信号未被统一接入操作界面。只读集合接口并不保证底层不可变，分析输入边界需要自行冻结；界面草稿和异步返回缺少完整的项目/请求身份隔离。报告快照身份必须由冻结内容定义，不能让每种导出格式重新获取当前时间或重算结果。

## Implementation

| Concern | 主要修改文件 | 实际行为和契约 |
|---|---|---|
| science/runtime | `src/FluoAxis.Domain/SignalSeries.cs`；`src/FluoAxis.Analysis/WellSignals.cs`、`CalibrationModels.cs`、`ControlSampleModels.cs`、`CalculationTrace.cs`、`BrafAnalysisEngine.cs` | Raw 点、通道字典、输入/结果集合和 trace 防御性冻结；重复孔/跨 Run/重复 Sample ID 及缺 Fm/Fb 失败关闭；合法输入数值规则不变 |
| package/data-path | `src/FluoAxis.Contracts/ManagedProjectPackageDto.cs`；`src/FluoAxis.Persistence/SqliteProjectRunRepository.Package.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Packages.cs` | 新数据库只包含所选项目/Run，保留身份/revision/Method/source；hash/schema/阶段一致性验证、拒绝 trigger/view/未来 schema、冲突身份不覆盖 |
| report/contract | `src/FluoAxis.Contracts/ReportSnapshotDto.cs`、`Serialization/ReportJsonContext.cs`；`src/FluoAxis.Reporting/`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Reports.cs` | evidence 深冻结、canonical JSON/hash 派生 Report ID，连续格式复用最近快照；HTML/XLSX 校验 hash、编码/公式注入防护；INVALID 不显示正式正负结论，缺 trace 明示 |
| desktop delivery | `src/FluoAxis.Infrastructure/DesktopWorkspace.Delivery.Windows.cs`、`PdfReportRenderer.Windows.cs`、`ReportRenderingSmoke.Windows.cs`；`src/FluoAxis.App/App.xaml.cs` | 平台选择器瞬态路径限定在 Infrastructure，安全 display name 回执；外部写入使用 StorageStreamTransaction；真实 WebView2 PDF；原生 smoke 入口仅 Debug |
| raw query | `src/FluoAxis.Contracts/IRawSignalGateway.cs`、`RawSignalPageDto.cs`；`src/FluoAxis.Persistence/SqliteProjectRunRepository.SignalPages.cs`；`src/FluoAxis.Infrastructure/PersistentDesktopStore.Signals.cs` | 单孔单通道 COUNT/SELECT 事务、有序 PointIndex、前后 revision/Method 校验；最多 25,000 点，不加载整 Run RFU，不计算科学结果 |
| UI/presentation | `src/FluoAxis.App/MainWindow.xaml`、`MainWindow.xaml.cs`、`MainWindow.Explorer.cs`；`src/FluoAxis.Presentation/WorkbenchViewModel.cs`、`PlateDraftEditor.cs`、`RawSignalExplorerState.cs` | 窄窗阶段选择、上下文收起、后端阶段状态、44px 孔位、Ctrl/Shift 矩形与键盘选择、全部 Sample 元数据批量赋值、20 步 undo/redo、dirty 确认、对齐结果表和逐孔 trace |
| integration/docs | `FluoAxis.slnx`；相关 csproj/lock；`tests/FluoAxis.Architecture.Tests/ProjectDependencyTests.cs`；`DEPENDENCIES.md`；`.github/workflows/ci.yml`；`README.md`；`docs/design/` | 13 个生产/15 个测试项目；复用固定 OpenXML 依赖，更新架构白名单和锁文件，CI 发现门槛 810，完成度逐项标注 |

Explorer 默认显示当前 Run 的真实数据，明确提供演示切换；实际二维散点/折线按孔/通道分页，Point ID 由 Run/Well/Channel/PointIndex 稳定组成，点击显示 G17 RFU 和真实时间。取消/项目切换会丢弃旧请求结果并清旧图。三维仍是显式演示，未伪装成当前实验数据。

报告只消费已保存 evidence，包含已有端点 RFU 和中间量，不包含完整时间序列 Raw RFU。`storedPartial`/`unavailable` 不被补造成完整来源证据；未分析 Run 返回 `SYSTEM_REPORT_NOT_READY`，失败无样本 Run 明确 INVALID，control-only 合法结果明确无样本。HTML 使用 CSP/编码且无远端资产；XLSX 使用 inline string、保留 G17 数值与确定性 ZIP 顺序/时间戳。hash 是完整性校验，不认证科学真实性。

兼容与预算：保持现有科学阈值、Gateway 修改命令、数据库 schema 和归档格式版本；新增只读 Raw query、Report Snapshot 与平台交付接口。旧 Run 缺 trace 仍可读取/打包，缺项不重算。归档导入仅接受当前 schema，不做隐式历史迁移；导入事务提交后不再把晚到取消伪报为未提交。

- 继续使用 store 单操作槽，不增加无界 worker/任务队列；界面当前图形请求可取消且按 generation 丢弃旧响应。
- 工程包总/压缩输入 128 MiB、单 entry/数据库 64 MiB、信号 1,000,000 行、总 2,000,000 行、文本值 8,192 字符。现有归档会缓冲 byte arrays，有限额但没有大包峰值内存资格，不宣称全流式或零拷贝。
- 报告单文本 16,384 字符，冻结/序列化前总 UTF-8 文本 1 MiB，canonical JSON 4 MiB，HTML/XLSX 输出 16 MiB；仅缓存最近一个报告。PDF HTML 输入 2 MiB、输出 32 MiB、超时 60 s，脚本/host object/web message/外部导航/下载/权限均禁用。
- Raw 每页上限 25,000，显式超限拒绝，不静默改用户预算；不保留不需要的全量信号、后台数据加载器或共享计算缓存。显示派生路径不回流正式分析。

## Validation

精确最终命令和结果见 front matter。聚焦验证包含 Analysis 82、Reporting 25、Infrastructure 70 个用例通过；最终源码套件 810/810（14.009 s），之后执行与 CI 相同的发现门槛 810，再次 810/810（15.226 s），均零失败零跳过。Node PlotHost 7/7。最后 build 为 0 错误、3 条既有测试 analyzer 提示（ManagedFileWorkflowTests 的 CA1869、CA1861、xUnit2031），未宣称全仓无告警。

新增或扩展的核心回归文件：

- `tests/FluoAxis.Analysis.Tests/AnalysisSnapshotImmutabilityTests.cs`、`WellSignalsTests.cs`、`tests/FluoAxis.Domain.Tests/SignalSeriesTests.cs`：调用方突变隔离、重复/跨 Run 和不完整 Calibration 的失败边界。
- `tests/FluoAxis.Infrastructure.Tests/PortableProjectPackageTests.cs`：单 Run 隔离、源字节/Method/schema 篡改、遍历、冲突不覆盖、取消、空 Run、无 trace 旧记录、control-only、跨根重开逐值比对。
- `tests/FluoAxis.Reporting.Tests/ResearchReportTests.cs`、`tests/FluoAxis.Infrastructure.Tests/PersistentReportExportTests.cs`：HTML/Excel 注入、hash 篡改、快照不可变、INVALID/空/部分证据、相同快照复用、文本总预算和导出安全边界。
- `tests/FluoAxis.Infrastructure.Tests/RawSignalPageTests.cs`、`tests/FluoAxis.GatewayContract.Tests/RawSignalExplorerStateTests.cs`：真实分页精确值/负 RFU/稳定身份、显式预算、空/缺 Run/取消/跨 revision/重开、陈旧图形响应隔离。
- `tests/FluoAxis.GatewayContract.Tests/PlateDraftEditorTests.cs`、`WorkbenchViewModelTests.cs`：批量完整元数据、undo/redo、阶段能力和草稿状态。窗口视觉/真实键盘不由这些模型测试代替。

有限 GUI 验证已看到新导航和报告页，未分析 Run 的报告操作显示安全结构化错误。Debug 原生 smoke 使用 `--native-report-smoke artifacts/reports/ft006`，`native-render-status.txt` 为 PASS；`sample.pdf` 五页均已渲染并逐页目视检查，中文可读、未见裁切/重叠。用户随后停止 GUI，未完成批量编辑→分析→所有保存选择器的完整点击验收。PDF 对应当时冻结快照；测试之后重生成了 HTML/XLSX/JSON，不声称当前目录所有格式具有同一个 hash。

最新单次报告测量 `artifacts/reports/ft006/measurement.json`：HTML 14.2087 ms、10,369 bytes；XLSX 136.8385 ms、11,282 bytes；process-wide managed allocation 2,209,280 bytes，可能包含并行测试分配。合成输入 SHA-256 `d927f3ff1f8c14acad1ed79c005d2f9fb4bc7ccb71f930d72bc22af86bb36153`；.NET 10.0.12，Windows 10.0.22631，Intel Family 6 Model 183 Stepping 1，20 逻辑处理器。此前没有同等报告导出流程，没有可比 before 数据；该首次工程基线不证明加速、最大规模或 UI 性能 SLA。包/分页/UI 大规模基准和长时间内存曲线仍待验收，超限已有明确拒绝路径。

路径验证覆盖：330 个仓库文本文件与 diff 检查、14 个 Presentation 科学边界文件；局部临时根包含 test class/process/GUID 并在清理前验证 containment。包在不同受控根重开、路径遍历/源身份/内容校验回归通过。历史原生含子目录目录重命名 Win32 code 5 没有在本轮被解决；便携包 roundtrip 不等于该系统操作通过。源码路径扫描不替代所有生成二进制、安装包或支持包的最终泄漏审计。

## Review outcomes

多代理按契约、数据/路径、UI/测试和文档分工并独立静态复核，最后只读 review 通过，未留下本轮已确认的实质代码意见。复核点包括：正式计算单一 C# 真源；Raw/快照不可变；Run revision/hash；旧缺 trace 兼容；失败和 INVALID 标签；项目包只带所选身份；安全输出；并发/内存显式上限；UI 草稿与过时响应；RUO 文案和测试/设计一致性。

本轮未触发 GitHub/外部 Codex review：未创建 PR，也没有向外部系统提交工作树；采用独立代理与手工检查。状态记录为 `validated`，不宣称远端 review、方法批准或发布批准。README 和 CI 发现门槛同步 810，完成度矩阵覆盖基线 71 个 FR，文档实现路径经过存在性核对，registry 45 条唯一 ID 的单行 JSON 记录均保留。

## Follow-ups

本轮完善了已声明的工程范围，下列仍是明确缺口：

1. 完整 Calibration/WT 逐孔输入点与 source lineage/input hash 持久化、查询和报告；当前保存 trace 保持 partial。
2. 报告嵌入真实图形，真实长报表、多字体/设备的 PDF QA；最大工程包/Raw 页性能和断电故障矩阵。
3. Explorer 任意列映射、可编辑虚拟表、受限公式/平滑和真实三维；当前三维仅演示。
4. 历史 schema 迁移、旧随机工作区导入、源副本保留清理；原生目录 rename code 5 环境定位。
5. 全流程 Windows UIA、Narrator、High Contrast、多 DPI、离线安装/签名包、升级/恢复和参考机性能资格。按用户本轮停止操作要求，GUI 未继续验证。
6. 实验负责人签字的 Method/Data Contract、真实仪器 fixture、独立 oracle 和 Golden 双批准。继续按草案工程默认值验证，不将合成测试转述成临床或方法学有效性。
