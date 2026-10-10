---
id: FX-20261009-001
kind: fix
status: validated
area: visualization-desktop
stage: real-render-validation
tasks: [plot-height, plotly-style-csp, native-save-interaction]
priority: high
branch: main
pr: null
paired_doc: docs/feature/FT-20261008-006-desktop-release-qualification.md
paths: [src/FluoAxis.App/MainWindow.xaml, src/FluoAxis.App/MainWindow.Appearance.cs, src/FluoAxis.App/MainWindow.xaml.cs, src/FluoAxis.App/App.xaml.cs, src/FluoAxis.App/MainWindow.NativePlotReview.cs, src/FluoAxis.Infrastructure/NativeVisualEvidence.Windows.cs, web/FluoAxis.PlotHost/plot-host.html, web/FluoAxis.PlotHost/plot-host.js, web/FluoAxis.PlotHost/plot-host.test.js, build/Verify-PlotBrowser.cjs, build/Invoke-DesktopAcceptance.ps1, build/Verify-RepositoryPolicy.ps1, docs/design/, README.md, docs/change_index.jsonl]
contracts: [display-only-plot, local-webview-security, native-picker]
hotspots: [short-viewports, dynamic-styles, constant-series, synchronous-uia-invoke]
bug:
  repro: "User screenshots show a flattened chart with no readable Y axis and stacked modebar buttons, plus a Save As RPC input-synchronous-call error."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [actual-browser-pixels, constant-rfu-axes, resize, native-save-picker, full-suite, path-policy]
  tests_run:
    - "PlotHost 11/11 passed, including constant RFU and CSP regression."
    - "Actual Edge: wide/narrow, constant/changing RFU pixels, axes, styles, point identity, resize and 3D passed."
    - "Native WebView: wide/short viewports, chart height at least 320 DIPs, axes and modebar passed."
    - "Final Release UI Automation: all nine import/save/report/reopen steps passed."
    - "Full solution 981/981 passed, 0 failed/0 skipped, 29.736 s."
    - "Final Release assets/DPI/startup and 549 resource manifest identities passed."
  benchmarks_run:
    - "Nine native UI steps: 22767.3 ms including explicit driver waits; no speedup/SLA claim."
search:
  aliases: [flattened plot, plotly CSP, Save As RPC, input synchronous call]
---

# 修复绘图区压缩与原生保存交互验证

## Summary

用户反馈实际图形无法正确绘制。优先修复显示，并继续已授权的桌面交互/可签名发布包工作。固定 A1 合成 RFU 1000 的正确显示应是一条水平曲线，坐标轴仍需完整可读；不变更原始数据或判读公式。

修复已纳入 `artifacts/release/FluoAxis-research-win-x64-20261009-plot/FluoAxis.App.exe`。实查分支 main，保留所有其他工作区改动；未提交或推送。相关 MSIX/桌面资格交付见 paired feature，科研 Method/阈值仍为草案。

## Plan

1. 纠正 PlotHost 动态样式策略与原生可用绘图高度。
2. 对真实本地 Plotly/WebView 进行短/宽窗、恒定/变化序列与像素核验。
3. 另存为测试改用异步原生按钮消息，区分 UIA 输入同步 RPC 与应用输出故障，保留失败证据。

## Exploration log

- 当前 Grid 给固定高表格、标题、设置与工具栏分配空间后，PlotWebView 只剩约 100 px；Plotly 默认上下边距接近其全部高度。
- 本地 CSP style-src self 拒绝 Plotly 动态 style 节点；截图的 modebar 按钮失去固定布局。应只放开样式内联，仍禁止内联脚本、外部连接与导航。
- 原生 UIA 文件打开已通过；另存为 InvokePattern 同步调用遇到 COM 输入同步错误，截图为直接证据。尚不能据此认定用户手动鼠标保存也失败；改用异步原生 BM_CLICK 后实际验证。
- 上轮构建被本代理启动的 Debug 进程锁 DLL；先关闭该已知测试实例再构建，不终止用户旧进程。
- 首次 Edge 实际测试已通过图形几何/像素，但直接点击 SVG path 被 Plotly 透明交互层拦截；测试改为在点的实际坐标发鼠标点击，正常穿过 Plotly 交互层后收到相同 PointId，不通过直接发送消息绕过点选。
- 真实保存按钮有时没有独立 HWND，第一次异步 BM_CLICK 无法发送；补用对话框 WM_COMMAND/IDOK 后，同一旧发布应用九项 UIA 链全通过。最终版取消按钮也统一排队原生消息后再次通过。证据确认同步 Invoke 驱动为截图保存错误的触发原因，没有修改软件的事务保存语义。
- 浏览器环境调用和发布清单修复来自 paired feature；图形/报表均在应用数据目录创建缓存。最终 UIA 和 PDF 执行后精确发布清单仍通过，证明本机运行未污染该目录。

## Root cause

图形异常是两项显示缺陷叠加：原生布局压缩绘图区至约 100 像素，Plotly 默认边距挤占曲线空间；CSP 拒绝动态 style 使 modebar 按钮失去定位。CSV 的 A1 全为 1000 所以水平曲线本身正确，不能人为弯曲数据来修复视觉。

保存弹窗由验收驱动在同步 UIA 输入调用期间再次触发系统保存，产生输入同步 RPC 限制。改驱动为排队原生按钮消息后，在同一发布字节上工程包/HTML/XLSX/PDF 均成功；不是用另一个简化保存实现代替原路径。

## Implementation

- Explorer 外层增加垂直 ScrollViewer，面板按可用高与最小 820/1080 DIP（设置收起/展开）布局；WebView 至少 320 DIP。短窗口可滚动看到完整图形和数据表，原始表格仍使用既有有界页。
- Plotly 设置明确左右/上下边距与 autosize，ResizeObserver 在容器恢复可见/变化时更新尺寸，保留原 PointId、坐标和 Raw RFU。
- CSP 仅样式允许 unsafe-inline 以适配固定本地 Plotly 的动态样式；脚本仍 self，连接/object/base/form 仍禁用，既有 host/source/message 安全门禁不变。不放开内联脚本或远端 CDN。
- 原生 Debug `--native-plot-check` 在实际 C# UI/Gateway 渲染中检查 WebView DOM 几何并通过 CapturePreview 保存真实浏览器像素；XAML RenderTargetBitmap 单独保存布局，不混淆成 WebView 像素。
- Edge/Playwright 检查路由只允许本地资产 URL、断开其他请求，实际加载 pin 为 4.0.0 的 Plotly；检验三个 2D 场景、3D、CSP 违例零、点选身份与容器 resize。此辅助测试使用本机已有 QA runtime，不增加产品依赖。
- UIA 测试驱动以 BM_CLICK 或 WM_COMMAND 排队确认/取消原生对话框，修正字段等待和身份记录；source/EXE/App DLL hash、阶段状态输出无外部路径或原始数据。

## Validation

```powershell
node --test web/FluoAxis.PlotHost/plot-host.test.js
node build/Verify-PlotBrowser.cjs artifacts/plot-validation/20261009-browser-final
dotnet build src/FluoAxis.App/FluoAxis.App.csproj --no-restore --verbosity quiet
./src/FluoAxis.App/bin/Debug/net10.0-windows10.0.22621.0/win-x64/FluoAxis.App.exe --native-plot-check artifacts/test-tmp/native-ui/plot-20261009-a artifacts/ui-evidence/smart-refinement/valid.csv artifacts/ui-layout/plot-20261009-a
dotnet test FluoAxis.slnx --no-restore --verbosity quiet
```

PlotHost 11/11（121.848 ms）；App 最后构建 0 warning/0 error（13.52 s）；完整 .NET 981/981（29.736 s）。浏览器测试使用已有 Playwright/Sharp，通过 NODE_PATH 提供，不把机器安装路径写入脚本或清单。Edge 149.0.4022.69；同机 i7-13650HX/20 logical processors，Windows 10.0.22631.0、.NET 10.0.11、SDK 10.0.400。源 hash `ed783ca45013e34f9d791f791f3fe09ec104ba0441fa544c0bdd62129994c369`。

| 实际渲染 | 已验证证据 |
|---|---|
| 1280×460 恒定 RFU | 有效曲线区 344 px，5 个 Y ticks，FAM 标题，值仍为 [1000,1000,1000]，3027 个蓝色曲线像素 |
| 680×340 恒定 RFU | 有效曲线区 224 px，坐标完整，1947 个蓝色曲线像素 |
| 1280×460 变化 RFU | 有效曲线区 344 px，[100,500,1000]，3606 个蓝色曲线像素 |
| 工具栏与 CSP | 三个场景均绝对定位、一行按钮、零 securitypolicyviolation |
| resize 与鼠标点选 | 容器更新为 920 px 宽，实际鼠标点选收到 p-0 身份 |
| 3D | 实际 WebGL 渲染成功，三维轴/点/标签像素已目视检查；固定相机决策不变 |
| 原生 WinUI/WebView | 1280×980 和 960×720 窗口，WebView 均至少 320 DIP，Y ticks≥3、曲线区域≥200 px、modebar 定位正确 |

Edge 证据：`artifacts/plot-validation/20261009-browser-final/browser.json` 及 wide-constant/narrow-constant/wide-changing/three-dimensional.png；原生证据：`artifacts/ui-layout/plot-20261009-a/status.txt`、plot-wide.png、plot-short.png 及相应 layout.png。均已查看真实图像。短原生窗口需滚动，不宣称 XAML 截图中被裁切的可视区域等于整图；WebView CapturePreview 已独立确认完整绘图。

最终发布 UIA：`artifacts/desktop-validation/20261009-final-b/interaction.json`，取消/原生导入/工程包保存/报告快照/HTML/XLSX/PDF/关闭重开/工程包重开九项全通过，22767.3 ms 含等待。该时长是测试观察，不是性能基准或加速声明。实际输出文件、PDF 渲染与 XLSX 内容检查见 paired feature。

最终发布程序空工作区与现有工作区分别启动 498.048/485.8384 ms；完整交互后 549 文件清单大小/hash 仍匹配，嵌入 PerMonitorV2 和必需资产检查通过。未改变科学公式，因此既有算法/边界/Golden candidate 随全套回归通过，但仍无科学双批准。

## Review outcomes

代理受 403 限制，主代理按 concern 人工自审：科学数据与坐标未重写；显示脚本不计算正式 QC；仅放开必要样式，脚本/外网/host object 边界保持；新临时输出均 artifacts 唯一路径；旧 Method/Run 不迁移；没有增加 worker pool 或取消显式数据上限。发布/包门禁正负回归见 FT006。外部 PR/Codex review 未触发，本记录没有强制外部评审。

当前直接截图问题已解决；不将九项 UIA 当成拖放、所有键盘路径、Narrator/High Contrast 或完整干净机验收。样式与真实像素验证不替代整机长期性能。最终路径审计和 diff 检查在文档收尾后执行。

收尾检查结果：426 个仓库文本文件路径审计通过（含新增 cjs），33 个一方呈现文件科学源边界通过；git diff --check exit 0；索引 54 个唯一记录、文档均存在。

## Follow-ups

科研草案、正式证书/签名、干净机安装与辅助技术门禁不变。用户应从 20261009-plot 完整发布目录启动，旧目录保留作问题追溯；新版 MSIX 为无签名候选，不直接宣称可信安装资格。
