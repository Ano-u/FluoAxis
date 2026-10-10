---
id: FT-20261007-005
kind: feature
status: validated
area: persistent-desktop-session
stage: guided-analysis
tasks: [sqlite-composition-root, project-catalog-reopen, saved-evidence-viewer, session-lifecycle, validation]
priority: high
branch: main
pr: null
paired_doc: null
paths: [src/FluoAxis.App/, src/FluoAxis.Infrastructure/, src/FluoAxis.Persistence/, src/FluoAxis.Contracts/, src/FluoAxis.Application/, src/FluoAxis.Presentation/, tests/, docs/design/, README.md, .github/workflows/ci.yml, docs/change_index.jsonl]
contracts: [workspace-lifecycle, project-catalog-pagination, saved-run-evidence, gateway-repository, relative-path]
hotspots: [DesktopWorkspace, SqliteProjectRunRepository, SqliteWalWorkspace]
bug:
  repro: "The original WinUI composition root used an in-memory Run repository; staged source bytes survived, but the Run could not be reopened after process restart."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [solution-build, source-tests, discovery-gate, persistence-reopen, lifecycle-concurrency, evidence-contract, repository-policy, WinUI-restart]
  tests_run:
    - "dotnet restore FluoAxis.slnx --locked-mode --verbosity quiet: exit 0"
    - "dotnet build FluoAxis.slnx --no-restore --verbosity quiet: exit 0, final incremental build 0 warnings / 0 errors"
    - "dotnet test FluoAxis.slnx --no-restore --verbosity quiet: 736 passed before the final TXT case"
    - "dotnet test tests/FluoAxis.Infrastructure.Tests/FluoAxis.Infrastructure.Tests.csproj --no-restore --verbosity quiet: 38 passed including the TXT case"
    - "dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 737 --verbosity quiet: 737 passed / 0 failed / 0 skipped after fresh source build"
    - "pwsh -NoProfile -File build/Verify-RepositoryPolicy.ps1: 296 text files passed"
    - "pwsh -NoProfile -File build/Verify-ScientificSourceBoundary.ps1: 11 presentation files passed"
    - "node --test web/FluoAxis.PlotHost/plot-host.test.js: 7 passed"
    - "git diff --check: exit 0; line-ending advisories only"
    - "WinUI: explicit new project, normal close, restart restores same name, one catalog row, revision 0"
  benchmarks_run:
    - "Persistent synthetic import/analyze/close/copy/reopen: 592.1812 ms, process allocation 3593632 bytes; one measurement, no SLA or before/after speedup claim"
search:
  aliases: [persistent workspace, SQLite desktop session, WAL, DesktopWorkspace, session lifecycle]
---

# 桌面持久工作区会话

## Summary

将 WinUI 组合根接入固定托管 workspace 的 SQLite/WAL，支持分页项目列表、跨进程重新打开已有 Run、查看保存的映射/孔位/结果/逐孔计算值。科学参数保持草案；旧记录缺失的来源证据明确标记，不重新计算或补造。可移植 `.fluoaxisproj` 导出/导入仍待接线。

## Plan

1. 检查现有 SQLite repository、WAL 初始化和相对路径策略。
2. 在 Infrastructure 边界创建可测试的持久存储适配器、相对 entry 解析和文件型连接工厂。
3. 添加只读项目目录与 saved evidence 契约，使窗口先选择项目再创建或读取；关闭前等待已有操作结束。
4. 增加生命周期和回归验证，更新设计文档与索引。

## Exploration log

开始时的 `DesktopWorkspace` 使用 `InMemoryProjectRunRepository`，源副本有磁盘记录但 Run 不可重开。当前分支实测为 `main`，规范 remote 为 FluoAxis；保留上一轮未提交工作，未提交或推送 Git。Method/Data Contract 继续按用户明确选择保留草案。

已按 contract、runtime、data/path、tests、docs/review 分工。复用仓库的 SQLite repository、WAL 初始化、托管导入与相对路径值对象；没有重新实现科学引擎、归档算法或添加依赖版本。首次接线尝试为随机 session root + 关闭时 checkpoint，复核后弃用：没有目录入口仍无法重开，且在 Closed 回调启动异步 dispose 会与运行中的 mutation/取消后刷新竞争。最终使用固定受控根、SQL 分页目录、停止接收操作后等待已有操作结束；正常关闭不以 packaging checkpoint 为前提。

验证途中遇到 NU1301 网络沙箱，按权限流程重新还原成功；CA1001/CA1826 构建问题已修正。目录移动用例最初失败，Win32 code 5；五次有界重试与正常用户权限重跑均未解决。诊断证明数据库、锁文件和源文件属性正常、独占打开/单文件重命名成功、空目录与最内层目录可移动，而含子目录的父目录仍失败。没有证据认定为 SQLite 句柄泄漏，故不延长重试或跳过验证。最终新增关闭后独占读取所有文件、复制至另一个受控根、重开并逐值比对的迁移测试；保留原生目录重命名限制，不声称该操作通过。

## Root cause

桌面壳体先于持久化工作区接线完成，导致 UI 的真实 Gateway 只拥有内存生命周期；算法和 Gateway 的 stage mutation 虽可验证，但退出进程后 Run revision、mapping、signals 和结果没有可恢复的数据源。

## Implementation

实现文件与职责：

| Concern | 主要文件 | 实现与影响 |
|---|---|---|
| runtime/data/path | `src/FluoAxis.Infrastructure/PersistentDesktopStore.cs`、`DesktopWorkspace.Windows.cs` | 固定平台工作区、独占租约、防重解析点、WAL/schema、每次解析相对数据库 entry、Pooling=false、路径无关错误；单操作槽覆盖查询/提交/staging/plot export/关闭 |
| catalog | `src/FluoAxis.Application/ProjectCatalogEntry.cs`、`src/FluoAxis.Persistence/SqliteProjectRunRepository.Catalog.cs` | 单 SQL 分页目录，按更新时间与 Run ID 排序；每页最多100，UI每页50；不加载信号或 Method |
| saved evidence | `src/FluoAxis.Contracts/IRunEvidenceGateway.cs`、`RunEvidenceDto.cs`、`Serialization/ContractJsonContext.cs`、`src/FluoAxis.Application/ApplicationGatewayEvidence.cs` | 独立只读接口，不改变 Gateway v3 命令；读取前后 revision/state/hash 检查；calibration/trace hash 与 Run 一致；没有 Raw RFU 或科学重算 |
| persistence consistency | `src/FluoAxis.Application/RunAggregate.cs`、`ApplicationGatewayWorkflow.cs` | 内存仓储按 TraceId upsert 保留 LQC，匹配 SQLite；样本 INVALID 错误码、原因、建议进入 Run Snapshot |
| presentation | `src/FluoAxis.Presentation/WorkbenchViewModel.cs` | 重开恢复保存快照、映射、分配和结果；切项目清旧数据，忽略旧响应；读取失败禁止继续修改；取消/commit 竞争后刷新真实 revision |
| UI/lifecycle | `src/FluoAxis.App/App.xaml.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs` | 显式新建/分页打开、恢复阶段、G17逐值trace；失败保留同revision编辑草稿；关闭等待初始化、当前操作、绘图与导出任务，再释放存储 |
| import compatibility | `src/FluoAxis.Infrastructure/ManagedImportInspectionAdapter.cs` | 接受既有 TXT media type `text/plain`，仍检查扩展名和签名，修正前端TXT被误拒绝 |
| tests | `ProjectCatalogTests.cs`、`PersistentDesktopStoreTests.cs`、`RunEvidenceTests.cs`、`SavedWorkbenchEvidenceTests.cs` 及既有 Infrastructure tests | 覆盖只读证据、旧缺失trace、hash不一致、分页、存储损坏、重复打开/关闭、关闭竞争、复制迁移、UI状态/取消和TXT |

现有 Method、科学阈值、数据库 schema版本和归档格式未变。新 query 是扩展接口；旧 trace 缺失返回 `unavailable`，存在但缺完整来源链标记 `storedPartial`。读取不会补造或重算。旧随机 session 目录不自动迁移，避免无来源依据的合并。

并发预算：整个 store 一项执行中的工作，SQLite provider 的同步 IO/计算放在单 worker 上；窗口一次修改、最多一项当前图形准备，单独的 PlotHost 串行渲染沿用现有机制。UI只持有当前 Run 的有限元数据与trace，不复制全量 RFU；catalog每页受限。后台初始化先完成再开放命令；关闭拒绝后续工作、等待当前任务，不在仍有等待者时销毁 semaphore。

## Validation

精确命令与结果见 front matter。聚焦套件：Persistence 27、Application 81、GatewayContract 36、Infrastructure 38 均通过。最终源码编译后 CI discovery gate 737/737 通过；之前完整源码测试736与最终TXT新增1用例均有实际执行。干净重新编译时既有测试命名/analyzer提示仍存在，最终增量 build 为0警告/0错误，不能把它扩大成仓库没有历史分析器告警。

真实合成字节经过 staging→inspection→mapping→角色→校准→对照→样本→SQLite→关闭→复制→重开，比较前后 evidence JSON、Method hash、revision、源hash与字节、LQC/样本trace，全部一致；测试使用仓库本地 `<test-class>/<process>/<guid>` 临时目录，清理检查 containment。另覆盖导入后继续分配角色、第二实例安全拒绝、关闭排空/重复关闭、损坏数据库不覆盖原文件。

GUI实测仅创建“工程验证-持久重开-20261007”这个无实验数据的验证项目，正常关闭并再次启动，目录仍为1项，名称与revision 0恢复；没有把后端完整分析回归当成GUI全流程验收。验证项目保留在本机工作区中。

基准记录：`artifacts/benchmarks/PersistentDesktopStoreTests/170544/ea9c41db93424c43bdf2d0602c5ce55f/measurement.json`（ignored，不提交）。一次持久化全链含关闭/复制/重开592.1812 ms，process-wide managed allocation 3,593,632 bytes，含断言与并行测试；5孔×2通道×3时间点，fixture SHA-256 `d927f3ff1f8c14acad1ed79c005d2f9fb4bc7ccb71f930d72bc22af86bb36153`，.NET10.0.12、Windows10.0.22631、X64、20逻辑CPU、Intel Family6 Model183 Stepping1。旧桌面无持久化同等流程，不能作加速比例或96孔大表SLA声明。

## Review outcomes

独立代理静态复核与手工清单通过；没有未响应的本轮实质性审查意见。检查科学结果只读、revision/hash一致性、legacy缺项兼容、Raw RFU不可变、目录/trace内存规模、单worker预算、path-free错误、无临床措辞扩展、关闭排空和复制重开。支持在旧trace缺新字段时显示缺项而不重算；正式结果仍受Run有效性控制。

最终文档校验：`docs/design/Requirements-Acceptance-Matrix-V1.1.md` 覆盖需求基线71个FR ID，无缺失或额外条目；change index 44条均为合法单行JSON、ID唯一且必需字段齐全。README/CI discovery门槛同步737。文档收尾后再次通过296文件路径审计、11文件科学边界审计和diff检查。

未触发 GitHub/Codex 外部审查：此工作树尚无PR，本地没有可提交此未发布diff的review触发工具；使用 concern 独立复核和手工清单，因此记录为 `validated`，不宣称外部review或发布批准。脚本路径审计覆盖仓库文本，不能替代所有二进制/安装包产物审计。

## Follow-ups

仍待实现/验收：便携 `.fluoaxisproj` 桌面打包与打开；全部Calibration/WT逐孔证据及输入快照来源hash；Report Snapshot/Excel/PDF；真实数据Explorer；托管文件保留/清理、旧随机session显式导入；故障恢复/断电、多DPI/Narrator/High Contrast/完整GUI分析流程、原生目录重命名code5环境定位。Method/Data Contract科学签字、真实仪器fixture与Golden双批准继续阻断科研方法完成声明。
