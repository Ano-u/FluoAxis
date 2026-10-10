---
id: FX-20261009-002
kind: fix
status: validated
area: explorer-interaction
stage: selection-export-validation
tasks: [point-table-selection, vector-export-eligibility, expanded-layout-validation]
priority: high
branch: main
pr: null
paired_doc: docs/fix/FX-20261009-003-plot-visible-workspace.md
paths: [src/FluoAxis.App/, web/FluoAxis.PlotHost/, build/Verify-PlotBrowser.cjs, build/Invoke-DesktopAcceptance.ps1, docs/design/Research-User-Guide.md]
contracts: [plot-selection-v1, display-only-explorer, vector-export-eligibility]
hotspots: [selection-feedback, source-point-index, secondary-series, filtered-rows, webgl-svg]
bug:
  repro: "Plot clicks show point text but do not select table rows; table selection does not reach Plotly. Dense WebGL 2D still offers SVG."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [native-point-table-roundtrip, filtered-selection, real-png-svg-exports, webgl-format-rejection, expanded-settings-layout, release-workflow, path-policy]
  tests_run: ["App build: 0 warnings/errors", "GatewayContract: 93/93", "Full suite: 981/981, 0 failed/skipped, 24.479 s", "PlotHost: 12/12, 153.1899 ms", "Native filtered secondary-series selection and clear: passed", "Final Release UIA: 15/15", "Path and scientific boundary: passed"]
  benchmarks_run: ["Actual 3200x1800 PNG 103 ms, SVG 84 ms, dense PNG 329 ms; single browser observation, not SLA"]
search:
  aliases: [point table linkage, SVG WebGL, PNG export, selection feedback]
---

# 完成图表选择联动和导出格式资格

## Summary

继续用户授权的绘图/桌面工程验收。分类 fix，保持 Raw/Method/科学公式不变，基于实际 main checkout 修复已有交互合同。

## Plan

1. 使用既有 plot.setSelection/plot.selectionChanged，补齐图表与表格双向选择并防止回调循环。
2. 明确 WebGL/3D 不提供纯矢量 SVG，测试真实 PNG/SVG 和保存对话框。
3. 验证展开设置/短窗口、过滤排序后的点身份，以及发布成品；更新本记录。

## Exploration log

- ShowRawPoint 只生成状态文字和孔板高亮，未选择 ExplorerTable；表格事件只调用 ShowRawPoint，没有发送 SetSelection；SelectionChanged 消息未被主窗口消费。
- PlotHost 在点数≥10000 且 WebGL 可用时用 scattergl，但 SVG 拒绝条件只检查 3D，界面同样只检查维数。
- 当前 sandbox helper setup refresh 失败，只读和构建需经工具升级权限；没有更改系统策略。此前代理因 403 不可用，按 contract/runtime/data-path/tests/docs/review 由主代理人工分项复核。
- 首次构建暴露 CA1859 参数具体类型及数组 Count/Length 编译错误，修正后 App 构建和 93 项 GatewayContract 测试通过；早期失败构建不作为最终证据。
- 清空选择使用空 selectedpoints 数组会把所有点保持淡化；改为 null 恢复未选择状态，真实浏览器和原生窗口均验证。
- `artifacts/desktop-validation/20261009-selection-debug-a/interaction.json` 中工程包文件实际已保存，但脚本仍在 Explorer 查找工作台隐藏的 OperationText，导致超时误报。增加返回分析工作台步骤后 Debug 14 步及最终 Release 15 步通过。没有通过直接写工程包绕过保存对话框。
- 用户进一步要求扩大实际可视区域，新增 paired fix FX-20261009-003；本记录的选择/导出改动一起进入最终交付。

## Root cause

既有协议已支持选择，UI 漏接；导出资格判断漏掉二维 WebGL。先补行为与真实验证，避免仅靠静态源码宣布完成。

## Implementation

变更文件：`src/FluoAxis.App/MainWindow.ExplorerSelection.cs` 新增原始 PointId 映射与重入防护；`MainWindow.Explorer.cs`、`MainWindow.xaml.cs` 接通点选、框选、表格选中和关联响应。命令响应只消耗请求，不回写较新的用户选择。第二序列去除后缀后定位源行，再按冻结图点身份发送；不把排序后的可见行号当作 Raw 身份。单次最多 10,000 图点，待响应集合最多 64 项，新图/清空时重置。

`web/FluoAxis.PlotHost/plot-host.js` 使用 null 清空选择；WebGL/3D 拒绝矢量 SVG，PNG 保留。界面对二维≥10,000 点保守禁用 SVG，即使环境退回 SVG 渲染也不承诺大数据矢量导出。协议仍为 V1，无 DTO/schema/Method 改动。

`MainWindow.NativePlotReview.cs`、`build/Verify-PlotBrowser.cjs`、`build/Invoke-DesktopAcceptance.ps1` 与 `plot-host.test.js` 覆盖真实渲染/交互；用户说明位于 `docs/design/Research-User-Guide.md`。正式 Analysis 和 Raw RFU 未重算或修改。

## Validation

使用 SDK 10.0.400，Intel Core i7-13650HX / 20 逻辑处理器，Edge 149.0.4022.69；synthetic source SHA-256 `ed783ca45013e34f9d791f791f3fe09ec104ba0441fa544c0bdd62129994c369`。

```powershell
dotnet build src/FluoAxis.App/FluoAxis.App.csproj --no-restore --verbosity quiet
dotnet test tests/FluoAxis.GatewayContract.Tests/FluoAxis.GatewayContract.Tests.csproj --no-restore --verbosity quiet
dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 981
node --test web/FluoAxis.PlotHost/plot-host.test.js
node build/Verify-PlotBrowser.cjs artifacts/plot-validation/20261009-selection-g
./build/Invoke-DesktopAcceptance.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261009-viewport -SourceEntry artifacts/ui-evidence/smart-refinement/valid.csv -EvidenceDirectory artifacts/desktop-validation/20261009-viewport-release
```

- 构建 0 警告/0 错误；93/93 focused、981/981 全套、12/12 Node 回归通过。
- 最终浏览器 `browser.json`：真实鼠标 PointId、选择/清空、CSP、resize、恒定/变化曲线、三维、WebGL SVG 拒绝通过；3200×1800 PNG 103 ms / SVG 84 ms / 10,000 点 PNG 329 ms（含协议、读取和像素校验，单次，非比较性优化结论）。
- 原生 `artifacts/ui-layout/viewport-b/status.txt`：筛选 time≥90 并降序后，第二序列点击选中 SourceIndex 2；表格第二行联动两条序列的 index 1，响应不覆盖最新选择；清空后 selectedpoints 为 null。
- 最终 Release 15 步通过，34,650.1 ms 含驱动等待；`plot-export-check.json` 确认原生 PNG/SVG 均为 3200×1800，均有 11,564 曲线颜色像素，SVG 无 image 元素。
- 路径审计 429 个文本、科学边界 34 个第一方展示文件通过；所有新测试在独立相对 artifacts 目录，发布启动从不同 cwd 通过。包移动重开经 UIA passed。

## Review outcomes

人工 concern review 已完成；未获取独立 GitHub/Codex reviewer，未创建 PR，此变更无强制外部 review 配置。

| Concern | 证据与结论 | 后续边界 |
|---|---|---|
| contract | V1 身份与 correlation 不变；显示选择不调用正式计算 | 大于联动预算时明确提示 |
| runtime | 原生鼠标→表格→真实 Plotly 双向通过；重入防护及新图清理 | 无新增线程池/缓存；长期交互压力待独立验收 |
| data/path | Raw 只读；无新文件边界；独立相对 artifacts 输出 | 不增加源文件路径泄露 |
| tests | Node、.NET、真实浏览器、原生与发布 UIA 均通过 | 合成 fixture 不代表真实仪器科学批准 |
| docs/review | 使用说明、registry 与 paired 布局记录同步 | 既有旧工程缺字段兼容与 RUO 边界保持 |

## Follow-ups

受信任签名/干净机安装及科学批准仍独立保留；本轮不创建证书或改变原固定孔位方案。
