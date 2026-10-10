---
id: FX-20261009-003
kind: fix
status: validated
area: explorer-layout
stage: desktop-visibility
tasks: [plot-visible-height, expandable-research-panels, focus-view, native-3d-validation]
priority: high
branch: main
pr: null
paired_doc: docs/fix/FX-20261009-002-explorer-selection-export.md
paths: [src/FluoAxis.App/, build/Invoke-DesktopAcceptance.ps1, docs/design/Research-User-Guide.md, README.md]
contracts: [display-only-explorer, native-viewport-layout]
hotspots: [webview-clipping, short-window, plot-resize, table-selection]
bug:
  repro: "User screenshot shows a wide and shallow 3D canvas with clipped axes; page headers and the data table consume the visible workspace."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [native-2d-and-3d-visible-bounds, focus-restore, selection-regression, desktop-exports, build, path-policy]
  tests_run: ["Final App build: 0 warnings/errors", "Full suite: 981/981, 0 failed/skipped", "Node PlotHost: 12/12", "Six native viewport cases and selection: passed", "Final Release UIA: 15/15", "Release manifest: 549 files", "MSIX roundtrip: 554 files"]
  benchmarks_run: ["Short canvas 829x461 DIP, focused 829x532 DIP, wide 1149x721 DIP", "Published empty startup 431.2533 ms; UIA workflow 34650.1 ms including waits"]
search:
  aliases: [compressed 3D plot, larger canvas, focus plot, clipped axes]
---

# 让绘图区占据可见工作区

## Summary

分类 fix。用户明确要求扩大可视区域；仅提高页面滚动高度并不能保证图形完整可见。保留既有科研数据、判读和固定相机合同，重排原生界面。

2026-10-10 完成工程验证：当前 EXE 为 `artifacts/release/FluoAxis-research-win-x64-20261009-viewport/FluoAxis.App.exe`；可签名包为 `artifacts/release/FluoAxis-msix-20261010-viewport/FluoAxis.Research_1.0.0.0_x64_unsigned.msix`。检查的实际分支为 main，保留历史脏工作区，未提交或推送。

## Plan

1. Explorer 使用有界窗口网格；移除占据顶部空间的导入/品牌区域，保留项目身份和科研标记。
2. 图形填充主要区域；设置、表格以可关闭面板打开，不挤压画布。加入放大/还原图形。
3. 验证 960×720 与宽窗口的 2D/3D、面板打开及图表选择，并交付新的可运行包。

## Exploration log

- 既有 ExplorerSurface 强制 820/1080 DIP，并置于纵向 ScrollViewer；单独的 WebView 完整截图不能证明它在窗口内完整可见。
- 品牌、导入、方案和项目管理占据多行固定高度，图表另与设置/180 DIP 表格共享页面。
- 用户提供新的三维裁剪截图，故调整验收指标为 WebView 在实际窗口可视边界内的尺寸，而非仅 DOM 高度。
- 前一轮桌面脚本的 PNG/SVG/HTML 保存已成功；随后工程包保存状态检查失败。最终确认文件实际已保存，但工作台状态文本在 Explorer 隐藏；返回分析工作台后检查通过，见 paired fix。
- 第一版扩大视区后，原生边界检查通过但实际三维图标题仍被 WebGL 场景边缘截断；保留 `artifacts/ui-layout/viewport-a/` 证据，不把该轮标为最终视觉通过。
- 尝试增大 Plotly 外围 margin，三维场景反而缩小，未解决其内部裁剪，因此撤回。尝试 `.xtitle/.ytitle/.ztitle`、SVG text 和文本节点几何断言均发现三维坐标文字不在 DOM，它由 WebGL 绘制；这些检查被移除，保留真实像素视觉复核与 scene 轴标题契约检查。
- 最终保持正常留白及相机方向，观察距离由 (1.35,1.35,1.15) 同比扩为 (1.8,1.8,1.5333333333333334)，给轴标题留出完整空间。此调整只改变研究显示投影，不改变数据、刻度范围或正式算法。
- 验证辅助代码曾有 CS0136 局部变量重名，修正后最终 App build 0 警告/0 错误；不引用早期失败编译为通过证据。

## Root cause

页面高度与实际可视区域混淆；辅助内容占据主图空间。此前最小高度修补未解决主要视区分配。

## Implementation

- `MainWindow.xaml` 移除 Explorer 全页滚动和 820/1080 DIP 固定高度，改为有界 Grid 的星号行填满可见空间。标题保留项目身份/RUO；Explorer 隐藏分析用品牌/导入/项目工具区，离开该页自动恢复。
- `MainWindow.Appearance.cs` 与 `MainWindow.xaml.cs` 管理放大/还原及页面切换；设置、表格改为本地 WinUI Flyout，独立滚动与关闭，不改画布尺寸。表格仍虚拟化，RawPageText 来源信息移入“数据表与选点”。
- `MainWindow.Explorer.cs` 保留 view 保存提示并调整三维固定相机距离；`MainWindow.ExplorerSelection.cs` 的身份联动保持适用。
- `MainWindow.NativePlotReview.cs` 检查 WebView 顶部/底部均处于真实 RootSurface 范围，覆盖二维/三维、短/宽窗、放大/还原和浮层；分别捕获真实 WebView 像素与 XAML 布局。`MainWindow.VisualReview.cs` 更新设置浮层入口。
- `build/Invoke-DesktopAcceptance.ps1` 跟随新的设置入口，补充发布版放大/还原，完成 15 步。`build/Verify-PlotBrowser.cjs`、PlotHost 测试及用户说明/README/发布指南同步；不增加运行依赖或修改 Method。

## Validation

```powershell
dotnet build src/FluoAxis.App/FluoAxis.App.csproj --no-restore --verbosity quiet
dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 981
node --test web/FluoAxis.PlotHost/plot-host.test.js
node build/Verify-PlotBrowser.cjs artifacts/plot-validation/20261009-selection-g
./src/FluoAxis.App/bin/Debug/net10.0-windows10.0.22621.0/win-x64/FluoAxis.App.exe --native-plot-check artifacts/test-tmp/native-ui/viewport-b artifacts/ui-evidence/smart-refinement/valid.csv artifacts/ui-layout/viewport-b
./build/Publish-ResearchBuild.ps1 -OutputDirectory artifacts/release/FluoAxis-research-win-x64-20261009-viewport
./build/Invoke-DesktopAcceptance.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261009-viewport -SourceEntry artifacts/ui-evidence/smart-refinement/valid.csv -EvidenceDirectory artifacts/desktop-validation/20261009-viewport-release
./build/New-UnsignedMsix.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261009-viewport -OutputDirectory artifacts/release/FluoAxis-msix-20261010-viewport
./build/Verify-RepositoryPolicy.ps1
./build/Verify-ScientificSourceBoundary.ps1
git diff --check
```

最终 App build 0 警告/0 错误；981/981（24.479 s，0 失败/跳过）；12/12 Node（153.1899 ms）。原生 `viewport-b/status.txt`：

| 实际窗口 | 图形画布（DIP） | 验证 |
|---|---|---|
| 1280×980 | 1149×721 | 2D/3D 完整视区 |
| 960×720 | 829×461 | 2D/3D，top=165，rootHeight=683，底部未裁剪 |
| 960×720 放大 | 829×532 | top=95；还原后恢复 829×461 |
| 设置/表格浮层 | 画布高度不变 | 筛选/倒序/第二序列双向联动仍通过 |

真实 WebView 的 `3d-short.png`、`3d-focus.png`、`3d-wide.png` 完整显示三轴标题与刻度；独立 XAML 布局图不含 WebView 像素，不能当成图形黑屏证据。标签视觉检查针对本次合成 fixture，不声称覆盖任意长度用户标签。

Intel Core i7-13650HX / 20 逻辑处理器，150% DPI，SDK 10.0.400，Windows App SDK 2.4.0，Edge 149.0.4022.69；source hash 见 paired fix。旧最小高度只保证 320 DIP且可能被外层滚动裁剪；现以真实窗口边界和上述可见尺寸验收，不声称做过帧率优化基准。最终发布空工作区启动 431.2533 ms（`artifacts/startup-validation/a2fe1726956647a39243b87829b09f5d/startup.json`），完整 UIA 15 步 34,650.1 ms 含驱动等待，单次本机观察，非 SLA。

发布版 PNG/SVG 为 3200×1800，分别 179,319/8,883 字节，各 11,564 曲线颜色像素，SVG 无嵌入位图。549 文件发布清单在桌面流程后匹配；MSIX MakeAppx 校验及 554 文件解包 hash 一致，98,742,043 字节，SHA-256 `0407a8c2c878e05df2e4918e2acac98d5080037dd1dbbf920a8cb95bd5308bfb`。

路径审计 429 文本、科学边界 34 第一方展示文件、diff 空白检查通过。测试目录独立且相对，启动测试从仓库父目录执行验证 cwd 独立；工程包在另一隔离工作区重开通过。无新存储/schema/缓存/线程池，旧 Method 和 Raw 快照保持可复现。

## Review outcomes

使用 contract/runtime/data-path/tests/docs/review 人工分项复核；此前代理因服务 403 不可用。未获得独立 GitHub/Codex review，无 PR/强制外部 reviewer 配置。

- 正确性/合同：调整界面布局与科研显示相机，无正式 Analysis 公式、阈值、角色或结果标签改动；相机方向锁定不变。
- runtime：复用现有 WebView/ResizeObserver 和 WinUI 控件，无额外 worker 或数据副本；页面切换还原全局工具，表格仍使用有界虚拟化。
- data/path：Raw 不可变，无新文件信任边界；同源离线资源/CSP 仍受控；绝对路径未进入产品源文件/快照。
- 可访问性：原生按钮提供文字、自动化标识及放大还原状态，UIA 已验证；Narrator/高对比完整验收不能由此次替代。
- 文档/测试：paired fix 记录选点和导出细节，真实像素复核弥补原先仅看 DOM 几何的缺口；全部失败尝试保留。

## Follow-ups

科学签字、正式证书、干净机安装与完整辅助技术资格保持独立门禁。
