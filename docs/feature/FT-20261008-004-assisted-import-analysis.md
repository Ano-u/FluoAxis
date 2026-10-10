---
id: FT-20261008-004
kind: feature
status: validated
area: guided-automation
stage: import-to-analysis
tasks: [automatic-layout-recognition, role-profile, guided-auto-analysis, manual-refinement]
priority: high
branch: main
pr: null
paired_doc: docs/fix/FX-20261008-003-release-startup.md
paths: [src/FluoAxis.Import/, src/FluoAxis.Contracts/, src/FluoAxis.Infrastructure/, src/FluoAxis.Presentation/, src/FluoAxis.App/, tests/, docs/design/, docs/method/Data-Contract-V1.0.md, README.md, .github/workflows/ci.yml, docs/change_index.jsonl]
contracts: [recognition-proposal, explicit-role-evidence, plate-profile-v1, sequential-gateway-actions, raw-only-analysis]
hotspots: [ambiguous-layout, time-units, role-inference, partial-sample-identity, cancellation, stale-revision]
bug:
  repro: "Import requires manual project creation, mapping, assignments and repeated stage clicks even when a file/profile already provides unambiguous metadata."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [recognition-positive-and-ambiguous, profile-reuse, automatic-chain, qc-fail-stop, cancellation-and-stale, published-startup, path-policy]
  tests_run:
    - "Import: 63/63 passed; GatewayContract: 85/85 passed."
    - "Full solution: 962 passed, 0 failed, 0 skipped; 23.679 s."
    - "App Debug build: 0 warnings, 0 errors; final 16.58 s."
    - "PlotHost: 9/9 passed; 114.6199 ms."
    - "Native smart workflow: PASS, revision 5, two saved samples."
    - "Integrated Release publish: exit 0; asset/DPI and empty/current startup passed."
    - "Release: 549 listed resources verified; 550 files including manifest."
    - "Repository path and scientific-source gates passed; registry and requirement coverage checked."
  benchmarks_run:
    - "Synthetic single-run observation: recognition 1.8848 ms, automatic stages 549.6725 ms; not an SLA."
    - "Release startup observation: empty 504.9879 ms, current 499.9054 ms; not a cold/hot benchmark."
search:
  aliases: [smart import, automatic analysis, plate profile, direct file judgment]
---

# 文件识别、自动科研分析与后续人工调整

## Summary

用户授权主流程“直接导入文件，然后直接识别加判断，同时提供后续选择”。默认入口从文件开始，在信息完整、无歧义时自动依次调用既有映射、角色、校准、QC和样本分析；保留逐阶段复核及手工选择。

已交付用户确认的四角固定方案、自动识别与分析、可编辑方案及新运行重新设置。当前分支实查为 `main`，沿用既有脏工作区，未提交或推送；不把历史改动全部归入本 feature。成品为 `artifacts/release/FluoAxis-research-win-x64-20261008-smart/FluoAxis.App.exe`，需保留完整同级目录。科学 Method/Data Contract 继续为未批准草案，`validated` 只表示本次工程范围已验证。

## Plan

1. 识别实际表头/工作表中的通道、孔号和显式时间单位，返回可解释的识别结果。
2. 从明确的角色/样本标记或用户保存并选择的孔位方案取得assignment；不根据RFU高低猜对照身份。
3. 完整条件满足后用既有Gateway串行执行；QC失败、缺信息、歧义、取消或stale时停止并定位到需要处理的页面。
4. 原始数据/Method不变；已完成Run后续更改通过新Run/新项目分析，保留旧结果。
5. 聚焦回归与真实发布EXE启动验证后更新文档/索引。

## Exploration log

当前主链要求先建项目、逐步映射、逐孔角色、三个计算按钮。旧RegionDiscovery只看纯孔号/数值递增列，单表带FAM/ROX后缀和角色metadata不够表达。已确认软件启动失败源于发布缺XBF/PRI，FX003修复仍同步收尾；新增智能功能不能替代发布启动门禁。

收尾复核发现：显式 SampleId 缺少 Replicate 时，自动链原先把此问题列为可由固定方案补齐，可能丢弃文件的部分身份。改为停止并保留可见原因；仅完全无角色标记可由方案直接补齐。人工继续的默认填充也需保持完整文件身份并拒绝冲突，显式点击应用方案或编辑孔位后才采用人工决定。

## Root cause

预览候选只服务手工映射，没有完整且可解释的自动识别合同或角色方案复用；科学计算自身已有正确有序Gateway，不需要复制算法。

## Implementation

| Concern | 主要变更文件 | 行为与边界 |
|---|---|---|
| contract | `src/FluoAxis.Contracts/AutomaticImportProposalDto.cs`、`PlateProfileDto.cs`、`Serialization/ContractJsonContext.cs` | 只读识别建议包含解析选项、FAM/ROX 映射、明确分配、问题和依据；不可变方案只保存孔角色与显式解析选项，不保存历史样本身份 |
| recognition | `src/FluoAxis.Import/AutomaticImportRecognizer.cs`、`.Prefix.cs`、`.Layout.cs`、`.Roles.cs`、`AutomaticRecognitionModels.cs` | XLSX SAX、XLS reader、严格文本编码；识别 Time/Elapsed 单位、FAM/ROX sheet/metadata/列前后缀、Well/Role/PlateMap；无 RFU 角色推断 |
| data/path | `src/FluoAxis.Infrastructure/ManagedAutomaticImportAdapter.cs`、`PersistentDesktopStore.Recognition.cs`、`PersistentDesktopStore.PlateProfiles.cs`、`DesktopWorkspace.Recognition.Windows.cs`、`DesktopWorkspace.Explorer.Windows.cs` | 从受管相对源重新核验大小/SHA/签名；配置最多 100 个、每个 64 KiB、hash 验证与唯一临时写入；现有单操作槽串行执行 |
| orchestration | `src/FluoAxis.Presentation/WorkbenchViewModel.Automatic.cs`、`WorkbenchViewModel.cs`、`PlateProfileApplication.cs`、`UserPlateDefaults.cs` | 依次使用既有 ConfirmMapping、AssignWells、RunCalibration、RunControlQc、AnalyzeSamples；每步刷新 revision，活动期间防重入，取消/stale/QC 失败即停止 |
| UI/runtime | `src/FluoAxis.App/MainWindow.SmartImport.cs`、`MainWindow.PlateProfiles.cs`、`MainWindow.SmartReview.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`MainWindow.Import.cs`、`App.xaml.cs`、`FluoAxis.App.csproj` | 文件主入口、默认方案说明、识别依据折叠、仅预览、确认后继续、新运行重新设置；Debug 原生自关闭测试接入同一真实流程 |
| tests/docs | `tests/FluoAxis.Import.Tests/AutomaticImportRecognizerTests.cs`、`tests/FluoAxis.GatewayContract.Tests/AutomaticAnalysisTests.cs`、`UserPlateDefaultsTests.cs`、`tests/FluoAxis.Infrastructure.Tests/FixedPlateAutomaticWorkflowTests.cs`、`PlateProfileStoreTests.cs`、`tests/FluoAxis.Contracts.Tests/PlateProfileContractTests.cs` | 正负/缺项/取消/stale/源篡改/方案持久化回归；Infrastructure tests 增加 Presentation 引用与锁文件；README/CI 发现下限同步至 962 |

固定布局恰为 96 项：A1 Positive100、A12 NTC、H1 WT、H12 LQC，B2–G11 60 个可选 Sample，其他 32 个 Ignore。应用时只取本文件映射的孔，但每个声明对照都必须存在。首次没有方案才创建默认，不覆盖用户已保存方案。

无标签样本以孔号作为技术 ID、replicate 1；文件完整 SampleId/Replicate 保留，未知部分不能被方案自动掩盖。此次复核将样本元数据缺项移出可自动解决清单，并共用方案应用逻辑防止“自动”和“人工继续”的默认填充结果不同；人工明确应用方案或编辑孔位后采用该决定。任何正式计算仍来自既有 Analysis Engine，不改阈值、时间窗、Method identity 或 Raw RFU。

识别最多 32 sheets、256 rows/sheet、65,536 cells、1,048,576 文本字符、4096 chars/cell；源 hash 使用 64 KiB 缓冲。前缀截断、通道不唯一、单位冲突、BOM 冲突或数据非有限值停止自动映射。完整导入随后重新使用既有验证，前缀建议不能绕过正式校验。沿用工作区一个活动操作及串行科学阶段，不添加 worker 池、并行解析或缓存。

## Validation

本轮最终命令与直接结果：

```powershell
dotnet test tests/FluoAxis.GatewayContract.Tests/FluoAxis.GatewayContract.Tests.csproj --no-restore --verbosity quiet
dotnet test tests/FluoAxis.Import.Tests/FluoAxis.Import.Tests.csproj --no-restore --verbosity quiet
dotnet test FluoAxis.slnx --no-restore --verbosity quiet
dotnet build src/FluoAxis.App/FluoAxis.App.csproj --no-restore --verbosity quiet
node --test web/FluoAxis.PlotHost/plot-host.test.js
./build/Publish-ResearchBuild.ps1 -OutputDirectory artifacts/release/FluoAxis-research-win-x64-20261008-smart
./build/Verify-PublishedStartup.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261008-smart -UseCurrentWorkspace
./build/Verify-RepositoryPolicy.ps1
./build/Verify-ScientificSourceBoundary.ps1
git diff --check
```

- GatewayContract 85/85（849 ms）、Import 63/63（1.008 s）；新增部分样本身份回归均通过。
- 全套 962/962，0 failed/0 skipped，23.679 s；含数值/阈值、Golden candidate、导入、fake/real Gateway、取消/stale、保存/打开/迁移、报表、架构与路径回归。tests 的既有分析器/空引用警告仍存在，不声称整套零警告。
- 最后 App Debug build 为 0 warnings / 0 errors、16.58 s；此构建包含结果页文字简化。PlotHost 为 9/9、114.6199 ms。
- 原生 `--native-smart-smoke` 以 `artifacts/ui-evidence/smart-corners/source.csv` 为受管输入，实际调用 `MainWindow.StartSmartImportAsync`，自动默认方案及完整 revision 0→5 通过。`artifacts/ui-layout/smart-20261008-final/status.txt` 为 PASS；同目录 `smart-analysis.png` 已目视复核：B2=0.6 阳性、G11=0.2 阴性、阶段全完成、后续设置按钮可见，无明显遮挡。该证据来自 WinUI 实际布局，源由测试适配器供给，不证明 native picker 选取或完整鼠标/键盘操作链。
- 真 Store/Engine 场景证明：A2 Ignore 不产生样本结果；LQC 落阴性时 RunInvalid 且无样本结果；缺 A12 时在 revision 0 停止；识别不创建项目/修改源，等长内容篡改被 SHA 校验拒绝。
- 最终 Release publish exit 0；10 项必需 XBF/PRI/本地 PlotHost 资产通过；嵌入清单 PerMonitorV2；源/文本/二进制构建机器身份扫描通过。43 个依赖组件生成 compliance/SBOM 清单。`research-build.json` 中 549 项大小和 SHA-256 全匹配，含 manifest 实际 550 个文件。
- 最终 EXE 从异 cwd 自关闭启动：空工作区 pass/empty（504.9879 ms），证据 `artifacts/startup-validation/94b15849fd57437fa7e563eb93a6be5c/startup.json`；当前工作区 pass/ready（499.9054 ms），证据 `artifacts/startup-validation/8c6ee330709b4b8ca215d6b4221e1c86/startup.json`。未清空或重建用户数据库。FX003 另保存 schema 2→3、19 张原表内容不变的对比证据。
- 最后路径审计覆盖 413 个文本文件并通过，科学源边界覆盖 31 个一方呈现文件并通过；索引 51 个唯一 ID、所需键和文档链接全部有效，冻结基线 71 个 FR 在矩阵中零缺项。按仓库原设置执行 `git diff --check` exit 0。一次临时命令覆盖 `core.autocrlf=false` 将 CRLF 当成新增尾空白而误报；撤去命令覆盖后通过，未为消除误报批量改写历史文件或修改 Git 配置。

本地测量使用 i7-13650HX（14 cores / 20 logical processors）、Windows 10.0.22631.0、.NET 10.0.11、SDK 10.0.400。合成 7 孔/3 时间点/2 通道源 SHA-256 为 `ed783ca45013e34f9d791f791f3fe09ec104ba0441fa544c0bdd62129994c369`，单次识别 1.8848 ms、五阶段及持久化 549.6725 ms，concurrency=1；证据 `artifacts/benchmarks/FixedPlateAutomaticWorkflowTests/62140/affa40b0d3f641fc97d989b28a377c43/measurement.json`。这是新增工作流的观测，无等价旧版自动流程 baseline，未宣称加速；无 controlled cold/hot、p95、完整内存峰值或 SLA。前一工程运行使用 .NET 10.0.12，因此不作跨 runtime 性能对比。超识别预算回退人工映射，资源上限不因追求自动化放宽。

## Review outcomes

按 contract、runtime、data/path、tests、docs、review 拆分。三个代理已提供前段契约/UI/测试工作，后续因工具返回 `403 INSUFFICIENT_BALANCE` 无法继续；主代理接续完成回归与人工分项自审，未冒称获得新的独立最终复审。无关联 PR 或可触发的一般 GitHub/Codex review，本次不要求外部 review 门禁。

| 自审项 | 结论及剩余边界 |
|---|---|
| 科学/合同 | UI 只编排原 Gateway，未加入判读公式；缺对照、冲突、QC 失败均停止；当前文件样本身份与历史分离 |
| 并发/取消 | 自动链持续 busy、刷新 expected revision；单槽操作，无新增 worker pool；已提交阶段保留，取消不伪造结果 |
| 路径/临时目录 | 源为 managed 相对引用；测试根按测试类/进程/GUID 唯一并检查清理归属；发布异 cwd 启动通过；路径、archive、便携重开现有全套回归通过 |
| 内存/缓存 | 前缀、文本、配置、hash 缓冲有界；不保持额外整文件副本/全运行表；无共享缓存或固定 slice dataloader；实际峰值/大文件基准仍未验收 |
| 兼容/追溯 | 不覆盖用户配置或旧 Run；只存新增独立 profile 文件，正式 Method 与旧 evidence 不重算；本 feature 不新增数据库 schema |
| UI/可访问性 | 主操作突出、次级选项保留、识别问题可见；原生渲染与执行通过；Narrator/High Contrast/完整 picker 交互继续列待验收 |
| 测试/文档 | 962 discovery 下限、操作手册、工程设计、验收矩阵与 Data Contract 工程说明一致；需求冻结基线不静默改写，用户后续授权在本记录留痕 |

最终仅更新工程能力状态，不把合成测试或 hash 清单当作科学批准。发布缺资源修复、先前 DPI 修复和既有配色均保留，并重新由真实成品启动而非仅 Debug 构建验证。

## Follow-ups

1. Method/Data Contract 科学签字、真实仪器 fixture 与独立参考/双批准 Golden 待科研侧证据；默认 120 min、15 s 容差、至少 2 平台点和各类至少 1 对照仍为草案工程默认。
2. 用户真实文件若只有 `Time` 而未标单位，先明确保存解析选项；若格式不在识别范围，显示原因并人工映射，不静默猜单位或占用孔。
3. 已完成工程修复和本地验证；完整 native picker/拖放交互、辅助技术、真实 WebView 图像、参考机性能/泄漏、签名和干净机离线安装仍待专项验收。旧缺资源 UI 发布目录不能继续作为当前交付。
