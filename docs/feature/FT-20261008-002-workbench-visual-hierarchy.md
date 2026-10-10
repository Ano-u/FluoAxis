---
id: FT-20261008-002
kind: feature
status: validated
area: workbench-presentation
stage: interface-hierarchy
tasks: [semantic-colors, page-hierarchy, focused-actions, responsive-layout]
priority: high
branch: main
pr: null
paired_doc: null
paths: [src/FluoAxis.App/, src/FluoAxis.Infrastructure/NativeVisualEvidence.Windows.cs, docs/design/, README.md]
contracts: [backend-action-visibility, presentation-only, accessible-color-semantics]
hotspots: [theme-resources, stage-selection, dense-controls, high-contrast]
bug:
  repro: "Current controls, project management, stage content and technical details have similar visual weight, obscuring the next action."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [winui-build, plot-host-tests, gateway-regression, scientific-boundary, path-policy, release-manifest]
  tests_run:
    - "dotnet build FluoAxis.slnx --no-restore --verbosity quiet: 0 warnings, 0 errors"
    - "dotnet test tests/FluoAxis.GatewayContract.Tests/FluoAxis.GatewayContract.Tests.csproj --no-restore --verbosity quiet: 69 passed, 0 failed, 1.050 s"
    - "dotnet test FluoAxis.slnx --no-restore --verbosity quiet: 893 passed, 0 failed, 0 skipped, 42.257 s"
    - "Final Explorer height adjustment: App build 0 warnings/0 errors; native g captures PASS"
    - "node --test web/FluoAxis.PlotHost/plot-host.test.js: 9 passed"
    - "./build/Verify-RepositoryPolicy.ps1: 381 text files passed"
    - "./build/Verify-ScientificSourceBoundary.ps1: 24 presentation files passed"
    - "./build/Verify-WindowsDpi.ps1: Debug and published Release EXE passed"
    - "./build/Publish-ResearchBuild.ps1 -OutputDirectory artifacts/release/FluoAxis-research-win-x64-20261008-ui: exit 0, 545 files; all hashes matched"
    - "Native RenderTargetBitmap: 10 captures in artifacts/ui-layout/20261008-g, reviewed with no unresolved clipping/color findings"
  benchmarks_run:
    - "Single-iteration native capture plus PNG encoding: 56.549–365.9488 ms/view, .NET 10.0.12, 20 logical CPUs, 150 percent scaling; not frame-time or SLA evidence"
search:
  aliases: [UI hierarchy, colors, layout, visual priority, workbench design]
---

# 工作台排版、语义配色与操作层级

## Summary

已按用户要求完成原生界面的主次、配色和排版优化：项目管理与当前任务分离，青蓝用于当前步骤/主要操作，中性表面承载数据，状态同时保留文字。主题包含 Light、Dark 和系统 High Contrast 资源；十张实际 WinUI XAML 渲染图已检查。主分类为 feature，包含两处界面草稿/预览一致性修复。

基于当前 `main` 未提交工作树，保留已有改动，未提交或推送。后端 allowedActions、正式算法/阈值、Method 草案、Raw RFU 与持久化合同不变；保留 Per-Monitor V2 清晰度修复。内部科研构建已生成，不代表所有科学和整机验收完成。

## Plan

1. 核对当前XAML与代码生成控件，把内容分成导航、当前任务、主要动作、辅助来源四层。
2. 主题资源单独整理；主代理调整结构和结果层级；独立代理审查状态/可访问性和资源。
3. 编译、既有界面契约/绘图测试、路径及科学边界审计；生成可运行科研构建并记录验证边界。

## Exploration log

当前main保留前轮未提交功能。顶部两排项目管理与页内主动作同权重；步骤按钮含完整状态而缺少醒目的选中指示；映射/Explorer设置纵向堆叠；技术hash长文本占用常驻空间。已完成PerMonitorV2修复，本轮不以缩放替代排版。

分工为主题资源与对比度、主窗口布局/runtime、状态/契约独立审查。先实现共享资源和页面结构，再用 Debug-only 自关闭原生窗口加载已验证的合成工程，调用 RenderTargetBitmap，未使用桌面点击或系统主题设置修改。

验证和修复历史保留如下：

1. a 轮实际图发现浅色页面动态生成的表格、阶段标题仍取到 App 深色 Brush，出现白字白底；同时窄窗口品牌与菜单重叠，报告横向偏移/裁切。没有以编译成功代替视觉通过。
2. 尝试直接订阅 AccessibilitySettings.HighContrastChanged，在当前 unpackaged 环境窗口构造抛出 HRESULT 80070490；b 轮无窗口，c/d 轮安全诊断定位到 `Subscribe > add_HighContrastChanged > .ctor`。最终移除该直接订阅。
3. 使用隐藏的原生 Border ThemeResource 属性代理，让动态控件从所在窗口解析 Brush；订阅 ActualThemeChanged 与代理 DependencyProperty 变化，合并队列刷新，Dispose 注销。e 轮七图证明明→暗→明切换正确，报告与品牌裁切关闭。
4. f 轮新增孔板/Explorer检查发现展开设置会把工具栏/状态挤出。将设置区改为有界 Grid/ScrollViewer，展开时分配剩余高度，图/表自适应缩小。g 轮十图确认工具栏和两行状态完整可见，设置通过内部滚动访问。
5. 独立审查发现恢复视图可能直接丢弃未保存编辑、同 revision 更换附图后显示旧 Report ID。已分别补上替换确认和预览失效说明，取消/失败不改原草稿或有效快照提示。

## Root cause

多个工程阶段逐步加入控件后，缺少共享色彩语义、统一卡片与信息层级；任务、管理和追溯信息同时展开。

主题问题的技术原因是 code-behind 全局资源查找按 App 主题解析，和窗口 RequestedTheme 不一致；简单重绘仍可能取错颜色。最终由窗口自身 XAML ThemeResource 代理提供颜色，并监听值变化。展开设置的高度问题则来自多个 Auto 行加固定最大高度叠加超出视口，不能只依赖外层窗口缩放。

## Implementation

| 文件/范围 | 实际实现 |
|---|---|
| `App.xaml`、`Themes/WorkbenchTheme.xaml` | 13 个语义颜色、Light/Default/HighContrast 字典；卡片/标题/提示/主按钮/阶段按钮共5样式；官方按钮模板的焦点和禁用状态继续保留 |
| `MainWindow.xaml` | 删除单一空壳Tab；项目名/dirty常驻，项目管理折叠；当前步骤、状态/问题、表格、追溯和主动作分层；Import 2×2、FAM/ROX卡片、响应式侧栏；报告固定输出区；Explorer分组设置和更多导出 |
| `MainWindow.xaml.cs`、`MainWindow.Appearance.cs` | 动态步骤选中/状态文字、主题代理和合并刷新；窄窗品牌避让/项目工具换行；表格字阶/边界；查看trace自动展开；当前Run revision变更后清旧报告提示；LQC用技术方向而非样本正式标签 |
| `MainWindow.Explorer.cs`、`MainWindow.PlotDelivery.cs` | 表头样式；恢复视图前处理dirty；同revision附图成功后预览失效；高级设置有界高度、保留工具栏与状态；不改公式/Raw/Analysis |
| `MainWindow.VisualReview.cs`、`NativeVisualEvidence.Windows.cs`、Debug App入口/csproj | 独立仓库内测试工作区，固定合成工程，十张原生渲染图和一次性测量；Release不含检查入口/实现；输出为相对artifact身份和安全技术值 |
| `docs/design/`、`README.md` | 更新视觉规则、使用位置和验收范围 |

配色：浅色灰白表面/深石板字，主强调 `#0C6E75`；深色石板表面/浅字，主强调 `#5ED8DE`。绿色表示流程/QC完成，红色表示失败，琥珀表示待处理或草稿警示，均有文字标记。BRAF阳性/阴性使用普通文本，不映射为好/坏颜色。High Contrast仅引用系统 Window/Text/Highlight 色，没有固化假定系统配色。

实际资源对比度计算：六种文字色在四种常用背景共24种组合，Light最低5.07:1，Dark最低5.85:1；主按钮正常/悬停/按下最低分别满足Light 6.00:1、Dark 8.82:1。该计算仅适用固定主题资源，不声称High Contrast实机已验。

交互边界：阶段历史仍可查看，执行权限继续由后端允许动作驱动；INVALID与推荐动作靠前且不折叠；未保存编辑在项目工具收起后仍可见。报告按核对→快照→输出排列，PDF为主操作、Excel/HTML为次操作，取消仅有活动操作时展示。详细hash、trace、高级图形参数均可展开；恢复研究视图取消时保留原数据和dirty状态，附图成功后需重新读取快照身份。

## Validation

精确结果见front matter。893项全套源码测试通过后，最后只调整Explorer布局高度与回调；其后App重建0警告/0错误、g原生检查与Release编译通过。没有为单纯颜色/间距添加镜像测试；复用69项Gateway状态/草稿回归、9项PlotHost回归及全套893项验证。测试数量不是视觉验收证据，原生渲染另行检查。

`artifacts/ui-layout/20261008-g/` 包含浅色结果、映射、孔板、Explorer控件、展开设置、报告、深色结果、窄窗结果、展开项目管理与窄窗报告，共10张。主代理和独立代理检查了文本对比、状态层级、左右边界、菜单避让和设置展开后工具栏/状态。结果宽屏1280×880逻辑窗口、窄屏820×880，系统150%栅格比例；g `status.txt` 为PASS。图为实际XAML输出，Explorer明确显示图像未包含的检查占位；不冒充WebView绘图或完整点击流程验收。

测量文件 `artifacts/ui-layout/20261008-g/measurement.json`：一次性XAML捕获+PNG编码耗时为结果浅色365.9488 ms、映射99.5104、孔板124.3687、Explorer控件100.7228、设置97.1112、报告89.1827、深色结果163.5805、窄结果64.2421、项目工具101.7186、窄报告56.549。环境.NET10.0.12、Windows10.0.22631、20逻辑处理器、scale1.5；不含180ms settle等待，包含图片编码/IO，不是交互帧时间。没有同条件before或SLA主张。合成工程SHA-256为 `f1635b703ec0fa45b126b3b471763193c768eeb7481e9600b2952966eaf20626`。

新版产物 `artifacts/release/FluoAxis-research-win-x64-20261008-ui/` 发布退出0；545个清单文件、251,097,017 bytes逐项长度/hash匹配，清单SHA-256 `4ba75f2b0c768916cbba5b0665e8d79c923e9a24b9a3fb1ef4c1cd2cec511652`。43组件依赖证据和最终EXE PerMonitorV2门禁通过。发布包仅是未签名科研工程版。

路径审计381文本、科学边界24文件、diff空白检查通过；矩阵71 FR无增漏，registry49条唯一。临时/图片数据在仓库artifacts，Debug边界验证相对输出、拒遍历/reparse祖先；项目和Method未改。无新增正式数据缓存或无限worker；主题刷新合并到dispatcher一次，渲染检查串行十次、只编入Debug。

## Review outcomes

资源/视觉代理与契约代理独立审查。浅色主题、初始化异常、报告横裁、窄窗标题和Explorer溢出均有实际复现并在后轮关闭；两处dirty/报告身份提示意见已静态确认修复。最终g图无未解决阻塞意见。

没有创建PR/触发GitHub或外部Codex review；采用独立代理、实际原生渲染和手工边界检查，记录为validated。主题High Contrast仅资源/模板静态检查；对话框取消和报告附图交互分支只做静态复核，不把渲染图当作点击回归。

## Follow-ups

High Contrast实机、Narrator/键盘完整操作、真实WebView与高密度数据表、125%/200%及跨屏仍需资格验收。受控颜色和渲染图不替代这些门禁。科学批准、真实仪器fixture/Golden双批准、签名和干净机安装继续按既有设计记录执行。
