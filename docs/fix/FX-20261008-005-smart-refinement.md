---
id: FX-20261008-005
kind: fix
status: validated
area: guided-refinement
stage: manual-review-to-analysis
tasks: [reject-detached-editor-events, preserve-import-options, verify-manual-continuation, reversible-profile-application, profile-editor-clarity]
priority: high
branch: main
pr: null
paired_doc: docs/feature/FT-20261008-004-assisted-import-analysis.md
paths:
  - src/FluoAxis.App/App.xaml.cs
  - src/FluoAxis.App/MainWindow.xaml
  - src/FluoAxis.App/MainWindow.xaml.cs
  - src/FluoAxis.App/MainWindow.SmartImport.cs
  - src/FluoAxis.App/MainWindow.SmartReview.cs
  - src/FluoAxis.App/MainWindow.PlateProfiles.cs
  - src/FluoAxis.Presentation/PlateDraftEditor.cs
  - src/FluoAxis.Presentation/PlateProfileDisplay.cs
  - tests/FluoAxis.GatewayContract.Tests/PlateDraftEditorTests.cs
  - tests/FluoAxis.GatewayContract.Tests/PlateProfileDisplayTests.cs
  - docs/design/Research-User-Guide.md
  - docs/design/Requirements-Acceptance-Matrix-V1.1.md
  - README.md
  - .github/workflows/ci.yml
  - docs/change_index.jsonl
contracts: [explicit-import-options, editable-plate-draft, preserved-old-run, sequential-gateway-actions]
hotspots: [detached-control-event, reset-order, manual-fallback, sample-identity, undo-history, actual-native-flow]
bug:
  repro: "Preview import resets selected parsing options before reading them; applying a profile clears draft undo history. Native manual-continuation coverage is incomplete."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [native-refinement-and-options, draft-undo-and-validation, full-suite, published-startup, path-policy]
  tests_run:
    - "GatewayContract 93/93 passed, 698 ms."
    - "Full solution 970/970 passed, 0 failed, 0 skipped, 28.197 s."
    - "Final App Debug build: 0 warnings and 0 errors, 15.73 s."
    - "Native refinement: PASS across five analyses, old Run and sample identities preserved."
    - "Release publish and asset/DPI/startup gates: passed; 549 resource hashes match."
    - "Repository path policy and scientific source boundary: passed."
  benchmarks_run:
    - "Native five-analysis UI probe including waits and capture: 4660.1 ms, single observation, not SLA."
    - "Release startup empty/current: 473.6505/491.9692 ms, single observations."
search:
  aliases: [smart refinement, preview options, apply profile undo, manual continuation, detached editor, c000027b]
---

# 智能导入后的人工复核与设置保持

## Summary

继续用户授权的工程完成工作。范围为人工预览、补充样本信息、应用孔位方案、继续分析和重新设置；保持科学草案及原始结果不变。本轮分类 fix，不改 Method、阈值或科学公式。

修复已通过工程验证，交付 `artifacts/release/FluoAxis-research-win-x64-20261008-refinement/FluoAxis.App.exe`（必须保留同级资源）。当前分支仍是实查 main，未提交、未推送；既有其他改动保持。此状态不表示科学批准或全部 V1 资格已完成。

## Plan

1. 扩展自关闭原生验证，覆盖重新设置、手工解析选项、元数据复核和历史保留。
2. 修复重置顺序与方案应用撤销；明确未定义孔行为并简化孔位区域显示。
3. 聚焦测试、原生 UI、全套回归和新发布包验证后更新此记录与索引。

## Exploration log

- 实查分支 main，保留前轮脏工作区与成品。FT004 已验证直接自动链，但未覆盖人工继续的真实窗口分支。
- StartSmartImportAsync 在 ResetDraftEditors 后读取 ReadImportOptions；ResetDraftEditors 会恢复分钟等默认值，因此人工已选解析规则可能丢失。
- OnApplyPlateProfileClick 逐孔调用 PlateDraftEditor.Update，每次 Update 清空 undo/redo；整体方案不能撤销。摘要把 remaining=false 一律写成“区域外忽略”，而合同实际要求未定义孔人工分配。
- 代理当前均返回 403 INSUFFICIENT_BALANCE，不能追加独立代理工作。按 contract、runtime、data/path、tests、docs、review 分项由主代理继续复核。
- 新原生测试首次退出但没有结果文件，隔离工作区仍有 session.active。增加安全阶段记录后稳定停于 refine-new-run，并出现 Windows 1000/Microsoft.UI.Xaml.dll/c000027b；不能把进程退出当作验证成功。
- Debug 验证模式增加失败捕获后，得到 ArgumentException/80070057，调用链 Update → CaptureWellDraft → BuildWellEditors 的事件回调。根因是旧孔编辑器的延迟事件在新 Run 清理或重建后到达，导致更新不存在的孔；同名孔复用时还有写错新草稿的风险。
- 修复在 CaptureWellDraft 检查当前字典中的对象身份，不吞异常、不改变 Engine；原生回归同时保留旧编辑器引用，明确验证其迟到事件不能修改新 Run 草稿。Debug 异常捕获仅写受控类型/HRESULT/方法名并以失败退出，不进入正常发布行为。
- 修复后的前两次测试在 manual-sample-edit 断言失败：控件属性已改变而草稿仍未更新。仅延时 50 ms 不足；排查发现测试在未进入可见布局的编辑器上直接设置 Text，未产生与用户操作等价的 TextChanged。测试先执行原生布局并将编辑器滚动到可见位置，再设置 Text；没有绕过控件直接改草稿来获得通过。之后完整链通过。
- 首张通过图显示结果页仍保留上一步的滚动位置，底部仍显示“分析样本…”。补齐状态结束提示，按 Run/步骤变化回到页顶，普通同页刷新不重置滚动；最终原生断言和截图均通过。

## Root cause

选项采集时机晚于重置；方案整体应用复用了单字段编辑接口；UI 未拒绝已脱离当前孔板的旧控件事件。三者分别修复选项保持、整体撤销和重新设置崩溃，不涉及科学计算规则。

## Implementation

- `CaptureWellDraft` 除更新锁外校验当前 well→editor 对象身份，拒绝旧控件迟到事件；相同孔号的新运行也不接受旧引用。
- 在清理编辑器前捕获人工解析选项，并恢复到新预览界面及传给识别器；保留显式分钟/秒等用户设置，不改变文件单位冲突规则。
- `PlateDraftEditor.ApplyAssignments` 对 ≤96 项且全部属于当前映射的唯一分配先完整校验，然后作为一次编辑写入既有 20 步历史；错误批次不部分写入、不破坏历史。按钮应用方案接入该接口，保留全部可编辑元数据的撤销能力。
- `PlateProfileDisplay` 仅格式化已确认几何：完整矩形显示 B2-G11，稀疏集合不会显示成填满矩形，数值列自然排序；摘要分别显示 Sample、Ignore 与未定义孔。解析下拉框只在勾选使用规则后展开。
- 新文件/继续动作清除过时错误提示；人工继续结束后写入明确完成/停止说明。StageContentScroll 只在新 Run 或不同步骤时置顶。
- Debug-only `--native-smart-refinement` 使用真实 MainWindow 入口/按钮处理、控件和同一 Gateway/Store/Engine，覆盖人工分支；失败返回非零并保留受控诊断。没有加入生产异常吞噬或改变科学算法。

## Validation

执行命令：

```powershell
dotnet test tests/FluoAxis.GatewayContract.Tests/FluoAxis.GatewayContract.Tests.csproj --no-restore --verbosity quiet
dotnet test FluoAxis.slnx --no-restore --verbosity quiet
dotnet build src/FluoAxis.App/FluoAxis.App.csproj --no-restore --verbosity quiet
./src/FluoAxis.App/bin/Debug/net10.0-windows10.0.22621.0/win-x64/FluoAxis.App.exe --native-smart-refinement artifacts/test-tmp/native-ui/refinement-final artifacts/ui-evidence/smart-refinement artifacts/ui-layout/refinement-final
./build/Publish-ResearchBuild.ps1 -OutputDirectory artifacts/release/FluoAxis-research-win-x64-20261008-refinement
./build/Verify-PublishedStartup.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261008-refinement -UseCurrentWorkspace
./build/Verify-RepositoryPolicy.ps1
./build/Verify-ScientificSourceBoundary.ps1
git diff --check
```

| 验证 | 结果 |
|---|---|
| GatewayContract | 93/93，698 ms；新增整体方案撤销/重做、非法批次原子性、矩形/稀疏格式和未定义孔语义回归 |
| 全套 .NET | 970/970，0 fail/0 skip，28.197 s；现有测试分析器/空引用警告保留，不冒称全套零警告 |
| 最终 App Debug | 0 warning/0 error，15.73 s |
| 原生五次分析 | PASS；原有自动运行、新运行重新设置、无标注单位的秒文件、部分元数据停下并人工修正、文件完整身份预览继续全部完成；历史 Run 仍 revision 5/analyzed |
| 控件与草稿 | 方案可整体 undo/redo；旧编辑器不能改新草稿；手动 Reviewed-B2 与文件 File-B2 均按对应阶段进入结果；修正后旧错误提示关闭 |
| 原生 UI | `artifacts/ui-layout/refinement-final/status.txt` PASS；`smart-refinement.png` 已目视复核，结果摘要置顶、两个样本和后续设置可见、完成提示正确 |
| Release | publish exit 0，10 项资源、嵌入 PerMonitorV2、产物构建身份检查通过；43 个依赖组件合规清单随包生成 |
| 发布内容 | 549 个 manifest entries 的大小/SHA-256 全匹配，含清单实际 550 文件；无额外文件 |
| 发布启动 | 隔离空工作区 pass/empty，473.6505 ms；现有工作区 pass/ready，491.9692 ms；均为异 cwd 下真实 EXE 自关闭验证 |

启动证据分别为 `artifacts/startup-validation/ff45d59c2ee748d4a20c382374dad7b8/startup.json` 和 `artifacts/startup-validation/7f8ca7820e36402498fd2944ec78025c/startup.json`。修复前原生诊断为 `artifacts/ui-layout/refinement-before-b/status.txt`（停于 refine-new-run，进程退出码 -1073741189）和 `artifacts/ui-layout/refinement-diagnostic/status.txt`（ArgumentException/80070057）。此修复没有清空用户工作区，测试数据全部在独立 artifacts/test-tmp 下。

合成 fixtures 位于 `artifacts/ui-evidence/smart-refinement/`，SHA-256：

| 文件 | SHA-256 |
|---|---|
| valid.csv | ed783ca45013e34f9d791f791f3fe09ec104ba0441fa544c0bdd62129994c369 |
| seconds.csv | a22c00431be5716d9ca84e8fdbf61eac7261c2cf8e3b6aeb48ea18abff8ece5d |
| partial.csv | 58fe0ead427ba439f1d8b5052372ad9b897c9c6360cbc581a7ef87df8ff3cccf |
| explicit.csv | b59b631573e16fca235f92b8edb45daffb2105fc065fa32dd2f1a0a189366b6f |

本地 i7-13650HX、14 核/20 逻辑处理器、Windows 10.0.22631.0、.NET 10.0.11、SDK 10.0.400。最终五次分析原生 probe 为 4660.1 ms，含初始化、控件布局/等待和截图，不是单 Run 算法时间；修复前崩溃，没有可比的完成耗时。全套中的小文件观察为识别 2.0114 ms、自动阶段及持久化 723.3797 ms，证据 `artifacts/benchmarks/FixedPlateAutomaticWorkflowTests/91424/6f907abe304749dba54aee7b4569aaa2/measurement.json`。不声称加速、冷/热 p95、内存峰值或 SLA；本修复未新增并行工作或放宽预算。

## Review outcomes

本轮采用人工 concern 分解，代理因既有 403 错误不可继续。主代理完成自审和原生结果复核，未请求外部 GitHub/Codex review；本任务无强制外部评审要求、无未处理实质评论。

| Concern | 假设、证据与结论 |
|---|---|
| contract | 既有显式解析选项、全部孔唯一分配和科学 Gateway 顺序不变；恢复原本应保持的 UI 行为，不引入 schema 或 Method 变更 |
| runtime | 迟到事件按对象身份拒绝；真实重设链及同孔号旧引用回归通过；主线程只做 ≤96 孔显示/草稿工作，无额外任务池 |
| data/path | 原始源/hash、Method Snapshot、旧结果不变；新增测试输出均为仓库相对 artifacts；发布异 cwd 验证通过；现有包移动/往返、旧记录兼容、路径/临时唯一性测试随全套通过 |
| memory | 既有 20 步撤销上限；格式化最多读取 97 个孔名后拒绝超限，正式批次上限 96；无额外全量数据保留、固定 slice 对象或新共享缓存 |
| tests/docs | 8 项新增单元/契约案例，970 下限同步 README/CI；原生失败轨迹保留，测试控件须先完成布局的限制已记录；操作手册和验收矩阵已更新 |
| accessibility/RUO | 清晰显示 Sample/Ignore/未定义区别，解析规则按需展开；仅工程窗口渲染检查，未据此关闭 Narrator/High Contrast 门禁；始终科研草案措辞 |

## Follow-ups

真实仪器、科学签字、完整 picker/辅助技术/离线安装资格不由本轮合成工程验证替代。

运行新版 refinement 包，保留全部同级资源；此前 smart 包的直接自动链虽通过，但其重新设置崩溃由本轮修复替代。用户固定四角布局仍为默认；不需要重新批准已确认的布局或草案工程参数。
