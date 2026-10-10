---
id: FX-20261008-003
kind: fix
status: validated
area: desktop-startup
stage: release-validation
tasks: [publish-winui-resources, distinguish-startup-failures, actual-release-startup, existing-workspace-preservation]
priority: critical
branch: main
pr: null
paired_doc: docs/feature/FT-20261008-002-workbench-visual-hierarchy.md
paths:
  - src/FluoAxis.App/FluoAxis.App.csproj
  - src/FluoAxis.App/App.xaml.cs
  - src/FluoAxis.App/MainWindow.StartupVerification.cs
  - src/FluoAxis.Infrastructure/DesktopWorkspace.Windows.cs
  - src/FluoAxis.Infrastructure/DesktopStartupVerification.Windows.cs
  - src/FluoAxis.Infrastructure/StartupFailure.cs
  - tests/FluoAxis.Architecture.Tests/PublishedAssetPolicyTests.cs
  - tests/FluoAxis.Infrastructure.Tests/StartupFailureTests.cs
  - build/Verify-PublishedAssets.ps1
  - build/Verify-PublishedStartup.ps1
  - build/Publish-ResearchBuild.ps1
  - .github/workflows/ci.yml
contracts: [published-xbf-pri-assets, safe-startup-errors, workspace-preservation, published-executable]
hotspots: [publish-item-metadata, release-versus-debug, window-initialization, workspace-lease, old-schema-migration]
bug:
  repro: "User double-clicks the published executable and sees SYSTEM_WORKSPACE_OPEN_FAILED instead of the workbench."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [published-release-startup, empty-workspace-startup, existing-workspace-preservation, focused-regression, full-suite, path-audit]
  tests_run:
    - "App Release build: 0 warnings, 0 errors."
    - "Actual Release publish: exit 0; five generated XBF/PRI resources present."
    - "Published asset gate: 10 resources pass; missing-resource release rejected."
    - "Architecture: 38/38 passed, including 20 published-asset regressions."
    - "Infrastructure: 89/89 passed, including 6 bounded startup-diagnostic cases."
    - "Same Release executable: missing resources fail at window/LoadComponent; complete empty/current workspaces pass."
    - "Current workspace schema 2 to 3: all 19 original tables retain identical columns, rows, and hashes."
    - "Embedded executable DPI gate and scoped diff/path/document checks passed."
  benchmarks_run: []
search:
  aliases: [SYSTEM_WORKSPACE_OPEN_FAILED, SYSTEM_WINDOW_INITIALIZATION_FAILED, release startup, missing XBF, missing PRI, NETSDK1152, LoadComponent]
---

# 修复发布版启动失败并区分失败阶段

## Summary

用户双击 FT002 发布程序只看到 `SYSTEM_WORKSPACE_OPEN_FAILED`。已确认发布目录漏带 WinUI 的 XBF/PRI 资源，窗口 `LoadComponent` 失败又被统一工作区错误文案掩盖。修复发布项和失败阶段诊断后，同一 Release 程序在完整资源目录中通过隔离空工作区及现有工作区启动；现有实验记录保留。

本记录状态为 `validated`，表示这项启动修复已获得直接工程证据。FT004 整合包已再次完成发布启动验证和完整工程回归，见下方补充；科学批准和完整发布资格不由本修复替代。

## Plan

1. 比较 Debug、Release 构建输出与实际发布目录，分离窗口构造、存储初始化及发布配置。
2. 补齐项目拥有的 XBF/PRI，保留 SDK 重复输出错误检查；错误诊断仅输出白名单阶段、代码、HRESULT 和操作名。
3. 用实际发布 EXE 自关闭验证空工作区和当前工作区，并比较已有数据；将资产/真实启动门禁加入后续发布。

## Exploration log

1. 当前分支为 `main`，保留前轮未提交工作。原 `App.OnLaunched` 把 `DesktopWorkspace` 和 `MainWindow` 构造放在同一 catch，所有异常都显示工作区打开失败；错误页不能证明权限、数据库损坏或多开。既往 Debug 种入合成工程后的原生截图没有覆盖真正发布目录启动。
2. 先只读检查 Release/Debug 差异。ThemeResource 代理、回调注册、CSWinRT 优化和资源加载均为初始候选；没有据猜测关闭优化或修改用户数据库。
3. 构建目录存在 `App.xbf`、`MainWindow.xbf`、`Themes/WorkbenchTheme.xbf`、`FluoAxis.App.pri`、`FluoAxis.Visualization.pri`；旧 UI 发布目录只有最后一个项目 PRI，前三个 XBF 和 App PRI 全部缺失，构建 hash 清单也没有登记这些缺项。清单只能校验已列文件的字节身份，不能证明所需运行资源齐全。
4. WinUI SDK 的 `CopyGeneratedXaml` 面向构建输出/AppX 资源收集；当前 unpackaged SDK publish 没有完整登记这些项目资源。首版修复在创建 MSBuild 项时直接写未限定元数据，实际 `-getItem` 显示五项 `RelativePath` 均为空，造成 `NETSDK1152`。这是修复尝试中的失败，未通过关闭重复输出检查绕过。
5. 改为先 Include 文件，再对既有项目执行带项目名限定的 Update，得到五个唯一、非空相对路径。实际 Release publish 随即 exit 0，资产/DPI 门禁通过。
6. 从同一新 Release 程序构造缺资源副本并使用隔离空工作区复现：`window / LoadComponent / 802B000A`。完整目录使用同类隔离工作区成功，再验证现有工作区成功，证据排除“必须清空用户数据才能启动”的假设。
7. 首次启动验证的证据写出顺序曾有风险：先关闭最后窗口可能让进程提前退出。最终顺序为等待 store 关闭、写出结果 JSON、再关闭窗口；当前成功证据确实落盘。

## Root cause

根因为发布资源缺失，并有错误分类缺陷叠加。编译成功、Debug 原生渲染成功、文件 hash 清单自洽都不能证明 unpackaged Release 目录含可加载的窗口与主题资源。缺项导致 `MainWindow.InitializeComponent` 内 `LoadComponent` 失败；原广域 catch 将窗口错误错误地描述成工作区故障。

受控缺资源副本与完整目录使用同一 Release 可执行程序、不同受控资源集合，分别得到失败和成功；因此不把主题配色、系统权限、CSWinRT Release 优化或已有实验数据认作已证实根因。

## Implementation

- `FluoAxisIncludeWinUiResourcesInPublish` 在 `ComputeFilesToPublish` 后收集 `$(TargetDir)` 下 XBF 及 `FluoAxis*.pri`，按 `RecursiveDir/Filename/Extension` 形成相对发布路径，按 `RelativePath` 去重。排除发布子目录，保留 `PreserveNewest`、独立资源文件和 SDK 重复输出门禁；不额外重复框架 PRI。
- `Verify-PublishedAssets.ps1` 检查 10 项必需资源，拒绝缺失、空文件、重解析目录、越界路径，以及 XBF/PRI 签名或声明长度不符。该脚本只证明资产存在及基本格式，不声称完整解析、可启动或科学算法正确。
- `StartupFailure` 区分 workspace、window、activate、ready、arguments；公开信息仅为受控代码、阶段、8 位 HRESULT 和白名单操作。异常文本、完整堆栈位置、原始实验内容和机器路径不输出。
- `DesktopStartupVerification` 与 `MainWindow.StartupVerification` 在真实发布程序中等待窗口 Loaded、工作区 ready/empty 和非零布局，再有序关闭并写相对目录证据。默认是独立空工作区，显式 `UseCurrentWorkspace` 才读取当前数据；没有删除/重建用户工作区的恢复逻辑。
- `Publish-ResearchBuild.ps1` 在 hash manifest 前运行资源门禁，并默认运行自关闭的真实 Release 启动门禁；`SkipNativeStartup` 仅为显式跳过选项，不能被写成通过。CI 新增 Release publish、资源和成品 DPI 检查，不假定 CI 已完成原生桌面交互验收。

## Validation

实际验证目录为 `artifacts/release/fx003-publish-probe`。执行的发布命令：

```powershell
dotnet publish src/FluoAxis.App/FluoAxis.App.csproj --no-restore -c Release -o artifacts/release/fx003-publish-probe -p:DebugType=None -p:DebugSymbols=false
./build/Verify-PublishedAssets.ps1 -PublishDirectory artifacts/release/fx003-publish-probe
./build/Verify-WindowsDpi.ps1 -Executable artifacts/release/fx003-publish-probe/FluoAxis.App.exe
```

发布 exit 0，App Release 构建 0 warning / 0 error。五项实际资源分别为：`App.xbf` 763 bytes、`MainWindow.xbf` 26,656 bytes、`Themes/WorkbenchTheme.xbf` 4,071 bytes、`FluoAxis.App.pri` 2,216,784 bytes、`FluoAxis.Visualization.pri` 712 bytes；10 项资产门禁与成品 PerMonitorV2 检查通过。

| 验证 | 实际结果 | 证据 |
|---|---|---|
| 同 Release EXE，缺资源副本，隔离空工作区 | fail；`SYSTEM_WINDOW_INITIALIZATION_FAILED`，stage `window`，HRESULT `802B000A`，operation `LoadComponent` | `artifacts/startup-validation/103695cd37a04ffd8630479264dfe352/startup.json` |
| 完整 Release 目录，隔离空工作区 | pass / empty，469.4266 ms | `artifacts/startup-validation/3545efdd7f034bdd99fd9e7db43d6b61/startup.json` |
| 完整 Release 目录，当前工作区 | pass / ready，599.9107 ms | `artifacts/startup-validation/d8752ed3367e41f6996bb0911b174957/startup.json` |
| 当前工作区兼容 | schema 2→3；1 project、1 Run；19 张原表逐列、行数及内容 hash 均保持，changedOriginalTables 为空 | `artifacts/startup-validation/before-logical.json`、`artifacts/startup-validation/after-logical-comparison.json` |
| Architecture focused tests | 38/38 passed，含 20 个新资产正/负/异 cwd 用例 | `tests/FluoAxis.Architecture.Tests/PublishedAssetPolicyTests.cs`；本轮主代理执行结果 |
| Infrastructure focused tests | 89/89 passed，含 6 个启动阶段/诊断脱敏用例 | `tests/FluoAxis.Infrastructure.Tests/StartupFailureTests.cs`；本轮主代理执行结果 |

本轮 focused 命令及耗时：

```powershell
dotnet test tests/FluoAxis.Architecture.Tests/FluoAxis.Architecture.Tests.csproj --no-restore --verbosity quiet
dotnet test tests/FluoAxis.Infrastructure.Tests/FluoAxis.Infrastructure.Tests.csproj --no-restore --verbosity quiet
dotnet build src/FluoAxis.App/FluoAxis.App.csproj -c Release --no-restore --verbosity quiet
```

Architecture 为 38/38、15.773 s；Infrastructure 为 89/89、26.814 s；App Release 为 0 warning / 0 error、25.70 s。前两项为本轮独立测试代理/主代理执行，文档收尾时未重复运行。

资源负例在剩余文件 hash 全匹配时仍拒绝，覆盖了旧 hash 清单无法发现的缺项。新用例使用 `artifacts/test-tmp/` 下测试类、进程号与 GUID 唯一目录，从异 cwd 调用门禁；清理验证归属后执行。脚本输出只包含相对资源名；实际 Release 的新增 XBF/PRI 经 UTF-8/UTF-16 构建路径检查无当前机器身份，范围内 diff/path 检查通过。

启动耗时是同机单次观察，不是参考机冷/热启动基准，也不外推 p95 或长期性能。未运行独立性能 benchmark。本修复不改变正式 Raw RFU、Method/算法阈值、计算/报告数值或全局并发预算；启动 probe 一次只启动一个受限进程，最长等待有界。完整 FT004 整合版全套测试与最终发布由主代理另行执行，不能沿用本条 focused 结果声称已完成。

## Review outcomes

按 concern 分工：主代理负责真实 Release 启动、安全失败阶段和工作区保存证据；UI/发布代理负责构建资源与发布门禁；契约/测试代理负责资产正负回归。独立检查确认原生主题/回调路径不应在资源缺失根因明确后作无依据重写。

自审覆盖发布项重复/相对路径、未执行不可信资源、白名单诊断、工作区租约/关闭顺序、旧 schema 兼容和历史数据保持。`NETSDK1152` 修复尝试已保留在 Exploration log；没有通过关闭错误门禁或清空数据掩盖失败。GitHub review 未触发，本记录不要求外部审批；尚不把新的整合包或整个软件标记完成。

## Follow-ups

1. 已纳入 `artifacts/release/FluoAxis-research-win-x64-20261008-smart` 整合包。最终全套 962/962、PlotHost 9/9，通过资产/DPI及二进制构建身份扫描；549 个清单资源大小/hash 全匹配，含清单共 550 文件。实际发布 EXE 在异 cwd 下通过空工作区（504.9879 ms，`artifacts/startup-validation/94b15849fd57437fa7e563eb93a6be5c/startup.json`）与当前工作区（499.9054 ms，`artifacts/startup-validation/8c6ee330709b4b8ca215d6b4221e1c86/startup.json`）验证。旧缺资源目录不再作为可用交付依据。
2. 保留用户工作区，后续迁移继续以原表/内容证据验证；禁止清空、重建或覆盖以绕过启动失败。
3. 签名、干净机离线安装、High Contrast/Narrator、真实 WebView 图形和科学批准仍按原门禁验收；本修复的 ready/empty 证据不代替完整科研操作流。
