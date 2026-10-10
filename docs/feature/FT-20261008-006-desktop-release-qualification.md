---
id: FT-20261008-006
kind: feature
status: validated
area: desktop-release
stage: qualification
tasks: [real-desktop-interaction, package-and-signing-gates, release-evidence]
priority: high
branch: main
pr: null
paired_doc: docs/fix/FX-20261009-001-plot-layout-picker.md
paths: [build/Invoke-DesktopAcceptance.ps1, build/New-UnsignedMsix.ps1, build/Verify-ReleaseManifest.ps1, build/Publish-ResearchBuild.ps1, src/FluoAxis.App/App.xaml.cs, src/FluoAxis.App/FluoAxis.App.csproj, src/FluoAxis.App/MainWindow.xaml.cs, src/FluoAxis.Infrastructure/DesktopStartupVerification.Windows.cs, src/FluoAxis.Infrastructure/DesktopWebViewEnvironment.Windows.cs, src/FluoAxis.Infrastructure/PdfReportRenderer.Windows.cs, src/FluoAxis.Visualization/PlotHostWebViewAdapter.cs, tests/FluoAxis.Architecture.Tests/ReleaseManifestTests.cs, docs/design/, README.md, .github/workflows/ci.yml, docs/change_index.jsonl]
contracts: [isolated-desktop-verification, release-package, qualification-evidence]
hotspots: [native-picker, modal-windows, authentic-signature, clean-machine, offline-runtime, workspace-preservation]
bug:
  repro: "Existing native probes supply source files through an adapter and do not qualify real picker interaction or signed installation."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [uia-import-to-report, save-reopen, cancellation, package-integrity, signature-gate, native-startup, path-policy]
  tests_run:
    - "981/981 full solution, 0 failed/0 skipped, 29.736 s."
    - "Architecture 49/49 including 11 exact release manifest cases, 15.473 s."
    - "Actual final Release UIA: nine native interaction steps passed."
    - "Final EXE empty/current workspace startup, DPI/assets and 549 file identities passed."
    - "MSIX MakeAppx validation and 554 payload file roundtrip hashes passed; signature absent."
    - "Actual PDF 24 pages and XLSX 20 sheets checked; samples and numeric results present."
  benchmarks_run:
    - "Nine UIA steps observed in 22767.3 ms including driver waits, not a performance SLA."
search:
  aliases: [desktop qualification, MSIX, signed release, picker interaction, offline installation]
---

# 桌面交互与科研软件发布资格验收

## Summary

用户明确要求继续完成完整桌面交互和正式发布资格。沿用 main 脏工作区，保留历史结果；Method 仍为科研草案。工程签名、安装和科学批准分别记录，不能用自签名或本机成功冒充受信任发布/干净机通过。

2026-10-09 完成可签名候选包及主要桌面链的工程验证。用户确认暂无证书、先完成可签名包和其余验收，本记录 validated 仅覆盖已执行范围，**formalReleaseQualified=false**。未经执行的干净机、辅助技术、签名与科学门禁未关闭；未提交或推送。

2026-10-10 续验：最新 EXE/MSIX 和 15 步发布版交互链已更新，包含放大/还原、视图保存恢复、三种图形导出。当前包为 `artifacts/release/FluoAxis-msix-20261010-viewport/FluoAxis.Research_1.0.0.0_x64_unsigned.msix`，554 文件往返 hash 校验通过；逐项证据见 `docs/fix/FX-20261009-003-plot-visible-workspace.md`。下文 2026-10-09 数字保留为历史，不作为最新包身份。

## Plan

1. 在隔离工作区运行真实发布窗口，以 UI Automation 完成原生 picker、判读、复核、保存重开及报告输出。
2. 建立可复核包身份、MSIX 构建和签名/离线前提门禁；实际执行环境允许的验证。
3. 写出逐项证据和缺项，更新文档与交付；无证书或干净机时保留未通过状态。

## Exploration log

目前 WindowsPackageType=None、自包含 x64；发布资源和普通启动已验证，但无安装包。上一轮 Debug adapter probe 不包含原生 picker。当前可调用工具未提供 Computer Use，因此使用官方 Windows UIA 与屏幕截取；只操作测试实例并避开用户工作区。

当前用户证书库只读检查未找到代码签名证书；WindowsSandbox 未在 PATH 找到。签名所需身份/证书已向用户询问；其他独立工作继续。既有三个代理均因 403 不可用，本轮采用 contract/runtime/data-path/tests/docs/review 人工 concern 分解。

用户明确回答“暂无证书，先完成可签名安装包和其余验收”。无需证书购买/信任导入，不生成自签名冒充正式签名。已从锁定 NuGet SDK BuildTools 找到 MakeAppx/SignTool。

真实发布 UIA 取消 picker 和选择合成 CSV→revision 5 已通过。系统 picker 属于 PickerHost 子进程，但在应用 UIA 子树中可找到，主桌面窗口枚举不能替代该树。第一次屏幕捕获被其他窗口遮挡、第二次为黑帧，均不计视觉通过。

首个 MakeAppx pack/unpack 已成功，但发现原发布目录在启动 WebView 后新增 FluoAxis.App.exe.WebView2 缓存。默认环境在 EXE 目录创建缓存，既污染包又不适合只读 MSIX 安装目录；该试验包不作为交付。修复为 Infrastructure 平台应用数据环境，并增加 research-build.json 清单外文件/大小/hash 检查。

UIA 完整链三次均通过 cancel/import，随后在系统另存为字段定位/模式访问失败。适配了 1148/1001 标识与等待；第三次失效在取得原生 ValuePattern，仍待继续定位，不把工程包/报告步骤标通过。

后续用户截图确认另存为输入同步 RPC 错误。定位到同步 InvokePattern 调用保存：部分保存按钮没有自身 HWND，改用有 HWND 时 BM_CLICK、否则向对话框排队 WM_COMMAND/IDOK 后，同一旧发布程序全九项通过。取消按钮亦改为相同异步方式，最终发布版再次全通过。未绕过文件选择器或直接写输出冒充桌面保存。

WebView WinRT 投影不支持 .NET 示例中的 CreateAsync(userDataFolder)；改用当前 SDK 的 CreateWithOptionsAsync 后编译通过。一次构建因本代理旧 Debug 测试进程锁 DLL 失败，核对进程身份并关闭后解决。清单脚本首版有 PowerShell 换行语法错误，正例测试捕获；修复后 49/49 通过，失败过程不隐藏。

## Root cause

验收覆盖和正式分发链缺失；WebView 的默认缓存位置不适合只读安装目录；UIA 驱动同步触发系统保存按钮产生 RPC 限制。无科学计算规则变更。

## Implementation

- `--verify-desktop-interaction` 仅接受仓库相对的 artifacts/desktop-validation 根，复用正常发布 UI 和 native picker，所有实验记录写入独立 workspace，允许关闭后重开同一隔离记录。
- `Invoke-DesktopAcceptance.ps1` 只查找自己启动实例的 UIA 子树；源限合成 ui-evidence，输出限唯一 desktop-validation；记录逐阶段状态、源/EXE/App DLL hash。超时仅关闭自有进程并报告失败。仅控制必要控件，不枚举用户实验内容。
- `DesktopWebViewEnvironment` 在 Infrastructure 内解析平台应用数据目录，返回 WebView 环境对象；Visualization 通过工厂消费而不接收文件路径，PDF 复用同样策略。无新增并发 worker/缓存数据合同。
- `Verify-ReleaseManifest.ps1` 从任意 cwd 验证精确文件集合、相对路径、遍历、重解析、重复/大小写碰撞、reserved 名、大小/SHA、runtime cache/PDB。Publish 和 MSIX 脚本都调用该门禁。
- `New-UnsignedMsix.ps1` 从恢复的锁定 SDK 获取 x64 MakeAppx；新目录 staging、自包含 payload、最小生成图标、显式 provisional Publisher 和四段版本；pack 校验后 unpack 逐文件 hash，生成未签名/未安装/未科学批准证据。不创建或信任测试证书。
- 发布 README/操作手册/需求矩阵/工程设计和签名交接说明已更新。正式证书到位后需要匹配 Subject 的 Publisher 重建，而不是随意签名已有候选。

## Validation

最终可运行目录：`artifacts/release/FluoAxis-research-win-x64-20261009-plot`，549 个清单资源，含清单共 550 文件。资源校验在完整交互之后仍通过，EXE 同级不再产生 WebView 缓存。

```powershell
dotnet test FluoAxis.slnx --no-restore --verbosity quiet
./build/Publish-ResearchBuild.ps1 -OutputDirectory artifacts/release/FluoAxis-research-win-x64-20261009-plot
./build/Invoke-DesktopAcceptance.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261009-plot -SourceEntry artifacts/ui-evidence/smart-refinement/valid.csv -EvidenceDirectory artifacts/desktop-validation/20261009-final-b
./build/Verify-PublishedStartup.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261009-plot -UseCurrentWorkspace
./build/Verify-ReleaseManifest.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261009-plot
./build/New-UnsignedMsix.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261009-plot -OutputDirectory artifacts/release/FluoAxis-msix-20261009-final -Version 1.0.0.0
```

全套 981/981，0 failed/0 skipped，29.736 s；Architecture focused 49/49，15.473 s。当前测试数包含新清单 gate 11 例；CI/README 下限同步。代码无新增浮动依赖。App 最后 Debug build 0 warning/0 error，13.52 s；Release publish exit 0，DPI/10 项必需资源与文件身份门禁通过。

| 原生步骤 | 最终结果 |
|---|---|
| 取消原生文件选择 | passed，工作台显示取消 |
| 原生文件选择→自动判读 | passed，revision 5/analyzed，两个样本 |
| 另存工程包 | passed，13536 bytes |
| 读取报告快照 | passed，revision 5 |
| HTML / XLSX / PDF 原生保存 | passed，36734 / 19193 / 713979 bytes |
| 关闭并重开相同工作区 | passed，保存结果仍 revision 5 |
| 新隔离工作区打开工程包 | passed，原身份与结果恢复 |

记录：`artifacts/desktop-validation/20261009-final-b/interaction.json`。源 hash `ed783ca45013e34f9d791f791f3fe09ec104ba0441fa544c0bdd62129994c369`，最终 App DLL hash `1db4191d1e12d9674d4f7d2d7d79664a426dee42513ab321693e31b06ecb9879`。整个九步骤含驱动等待为 22767.3 ms，同机单次观察，不是用户操作时间或 SLA。

PDF 用 pypdf 提取确认 B2/G11、0.6/0.2 与 BRAF，24 页经 Poppler 渲染/两张联系图复核：标题、曲线、结果及附录表格存在，没有空白或明显缺字。报告是包含完整证据的长技术草稿，不宣称已验收全部长表/字体组合。XLSX 20 sheets 包含相同样本；证据为同目录 export-check.json 和 pdf-pages。未将合成实验文件当作真实仪器资格。

空工作区启动 pass/empty，498.048 ms：`artifacts/startup-validation/c32cf821c64e41d49ed9d003ad2d8a73/startup.json`；现有工作区 pass/ready，485.8384 ms：`artifacts/startup-validation/8aad74bf908640fab4cd1e73f42e926e/startup.json`。均异 cwd，用户工作区未清空。

最终 MSIX：`artifacts/release/FluoAxis-msix-20261009-final/FluoAxis.Research_1.0.0.0_x64_unsigned.msix`，98,738,878 bytes，SHA-256 `15ee7311a3d7d79bc63655b99a16c8badc6eaedb5e03af2bf1a5af5389fc73da`；MakeAppx 与 554 文件解包 roundtrip 通过，Get-AuthenticodeSignature 为 NotSigned、无 signer。构建工具 10.0.28000.2705；Publisher 为 provisional CN=FluoAxis Research。首个含缓存试验包仅用于根因记录，不交付。

## Review outcomes

按 contract/runtime/data-path/tests/docs/review 自审：单体 Gateway、科学公式、Raw、阈值均未改变；路径在 Infrastructure/构建边界解析，DTO 无新增机器路径；并发单操作槽和资源预算沿用；旧 Run/Method 不改写。验证工作区使用唯一目录，无跨测试固定临时路径。新增 MSIX 门禁有正负回归，运行后文件清单完整证明测试缓存不污染发布内容。

Plotly CSP 与原生绘图的详细复核见 paired fix。GitHub/Codex 外部 review 未触发；现有代理 403 不可用，主代理执行人工 concern 分解和自审，未冒称独立最终复审。无强制外部 review。仍不能宣布完整 V1 或正式分发批准。

收尾路径审计 426 文本通过，科学源边界 33 文件通过，diff whitespace exit 0，索引 54 个唯一记录。主机安装/证书状态未被测试脚本修改。

## Follow-ups

真实科研 fixtures/双批准 Golden 继续受 SCI-GATE 约束；签名证书与发布者身份不能凭空生成。

未完成门禁：受信任签名、Publisher 最终身份、干净 Windows 安装/升级/卸载和离线 WebView2 部署；全量拖放/键盘/Narrator/High Contrast/跨显示器 DPI；参考机长期性能/内存。用户授权先无证书完成候选，因此本次交付止于可签名 MSIX、已执行桌面主链与诚实的门禁状态，不要求重复授权或安装自签名证书。
