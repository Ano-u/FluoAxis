---
id: FX-20261008-001
kind: fix
status: validated
area: native-desktop-rendering
stage: workbench-readability
tasks: [per-monitor-dpi, embedded-manifest-validation, release-artifact]
priority: high
branch: main
pr: null
paired_doc: docs/feature/FT-20261007-007-engineering-workflow-completion.md
paths: [src/FluoAxis.App/app.manifest, src/FluoAxis.App/App.xaml.cs, src/FluoAxis.App/FluoAxis.App.csproj, src/FluoAxis.Infrastructure/NativeDpiSmoke.Windows.cs, build/Verify-WindowsDpi.ps1, build/Publish-ResearchBuild.ps1, tests/FluoAxis.Architecture.Tests/WindowsDpiPolicyTests.cs, .github/workflows/ci.yml, README.md, docs/design/Research-User-Guide.md]
contracts: [windows-process-dpi-awareness, release-manifest]
hotspots: [bitmap-virtualization, double-scaling, executable-manifest]
bug:
  repro: "User reports blurred text or the whole window at desktop scaling; the checked-in application manifest has no DPI-awareness declaration."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [inspect-embedded-manifest, dpi-policy-regression, debug-build, release-publish, full-suite, path-audit]
  tests_run:
    - "Debug App build --no-restore: passed, 0 warnings, 0 errors"
    - "Architecture focused tests: 18 passed"
    - "dotnet test FluoAxis.slnx --no-restore --verbosity quiet: 893 passed, 0 failed, 0 skipped, 25.622 s"
    - "dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 893: 893 passed, 0 failed, 0 skipped, 30.862 s"
    - "node --test web/FluoAxis.PlotHost/plot-host.test.js: 9 passed"
    - "./build/Verify-WindowsDpi.ps1: original manifest rejected; corrected Debug and Release EXE passed"
    - "./build/Publish-ResearchBuild.ps1 -OutputDirectory artifacts/release/FluoAxis-research-win-x64-20261008-dpi: exit 0, 545 files"
    - "Release manifest file length/SHA-256 verification: 545 matched, 0 mismatches"
    - "./build/Verify-RepositoryPolicy.ps1: 376 text files passed"
    - "./build/Verify-ScientificSourceBoundary.ps1: 21 presentation files passed"
  benchmarks_run:
    - "Native window DPI probe before: unaware/96 DPI/XAML scale 1; after: PerMonitorV2/144 DPI/XAML scale 1.5; no throughput benchmark"
search:
  aliases: [blurry window, fuzzy text, DPI, PerMonitorV2, display scaling]
---

# 修正原生窗口高 DPI 清晰度

## Summary

用户明确反馈“文字或整个窗口发虚”。实测确认进程未声明 DPI 感知：WinUI 只按 96 DPI / 1.0 倍绘制，由系统再放大。在源码 manifest 声明 Per-Monitor V2 后，同一环境实测为 144 DPI / 1.5 倍，Windows 窗口上下文确认 PerMonitorV2。成品 EXE 验证已加入 CI 和发布脚本。

本轮分类 fix，基于实测 `main` 未提交工作树，保留既有修改；未提交或推送。Method/Data Contract 继续草案，科研数值和持久化合同未改。FT007工程交付收尾和FX008 LQC均值修复已记录在各自配对文档。

## Plan

1. 核查源码/最终EXE清单及WinUI初始化，区分DPI声明缺失与字体/截图缩放。
2. 在进程创建前声明逐显示器DPI，不在XAML/WebView2手工重复放大。
3. 将最终可执行产物的清单验证加入发布门禁，验证Debug/Release并更新实际证据。

## Exploration log

源码 app.manifest 只有 OS 兼容性；生成的 WinUI 入口没有显式设置 DPI。旧 Debug 和 FT007-r2 成品 EXE 的 RT_MANIFEST 均没有 dpiAware/dpiAwareness 节点。独立审查未发现主窗口 Viewbox/ScaleTransform、CSS zoom 或 WebView2 RasterizationScale 人工覆盖，所以没有依据通过再次乘 DPI 比例修复。

为了区分框架自动设定与 manifest 缺失，先添加只编入 Debug 的自关闭原生窗口探针，在未修改 manifest 的构建上运行。得到 `PerMonitorV2=false, Awareness=0, WindowDpi=96, XamlRasterizationScale=1`。随后仅加入标准进程 DPI 声明并重建，同一探针得到 `true, 2, 144, 1.5`。原 gate 拒绝旧清单，修正后 Debug 和 Release 成品均通过。

此前桌面截图与 UIA 捕获存在不一致，且用户曾按 Escape 停止桌面输入。本轮没有恢复 Computer Use 点击、键盘或系统设置修改；DPI 取证使用经工具批准启动的 Debug 自关闭检查窗口，读取原生 API 数值，不操作其他应用。仍不把该数值检查当成主窗口全部像素或跨屏验收。

## Root cause

应用的自定义 manifest 覆盖了模板默认清单但未带 DPI 声明；生成 WinUI Main 也未提前设置进程 DPI。Windows 因而把窗口视为 DPI Unaware，`GetDpiForWindow` 返回虚拟化 96 DPI，XAML 栅格比例为 1；当前显示器实际为 144 DPI。150% 系统位图放大会使原先 1 倍绘制的文字和控件发虚。原生成品清单及前后 HWND API 数据共同确认该根因，不依赖截图观感猜测。

## Implementation

| 文件 | 变更 |
|---|---|
| `src/FluoAxis.App/app.manifest` | asm.v3 application/windowsSettings 中使用 2016 dpiAwareness=PerMonitorV2，2005 dpiAware=true/pm 兼容值，在进程创建前生效 |
| `src/FluoAxis.Infrastructure/NativeDpiSmoke.Windows.cs` | Debug-only 自关闭 WinUI 窗口；读取真实 HWND awareness、PerMonitorV2 上下文、DPI 和 XamlRoot 栅格比例；只保存四个数字/布尔字段 |
| `src/FluoAxis.App/App.xaml.cs`、`FluoAxis.App.csproj` | Debug 专用探针入口/链接；Release 不包含探针，正常工作区和业务启动流程保持原合同 |
| `build/Verify-WindowsDpi.ps1` | 检查正确 XML 命名空间/单节点/值/项目绑定；资源模式读取 EXE RT_MANIFEST #1，最大 1 MiB，不执行 EXE 或依赖 DLL |
| `build/Publish-ResearchBuild.ps1`、`.github/workflows/ci.yml` | 发布与 CI 对真正构建出的 EXE 执行 DPI 门禁，避免只验证源码 |
| `tests/FluoAxis.Architecture.Tests/WindowsDpiPolicyTests.cs` | 三项回归：正确模式通过，System 模式与错误命名空间失败；异位 cwd、仓库内进程/GUID 临时根与清理 containment |
| `README.md`、`docs/design/Research-User-Guide.md` | 更新科研构建位置、893 测试门槛、重启旧进程才能应用 DPI 修复的说明 |

没有修改系统缩放或兼容性设置，没有给 XAML 根容器或 WebView2 叠加 ZoomFactor。控件继续使用逻辑像素，逐显示器调整由 Windows/WinUI 管理。探针仅新增一次有 20 秒上限的窗口加载等待，无长期 worker/后台队列。

## Validation

精确命令及结果见 front matter。Debug App 构建为 0 警告、0 错误；全套源码测试 893/893，CI 发现模式再次 893/893，均零失败零跳过。Node 9/9。历史完整测试重新编译可能产生既有命名/分析器提示，本轮不宣称整个仓库无历史告警。最终未继续重复测试已通过的无关路径。

原生数据文件：`artifacts/validation/dpi/before-native/window-dpi.json` 与 `artifacts/validation/dpi/after-native/window-dpi.json`。取证系统为当前 Windows 10.0.22631、150% 显示缩放；探针未修改缩放设置。数值变化如下：

| 原生指标 | 修复前 | 修复后 |
|---|---:|---:|
| PerMonitorV2 上下文 | false | true |
| DPI awareness | 0（unaware） | 2（per-monitor） |
| GetDpiForWindow | 96 | 144 |
| XamlRoot.RasterizationScale | 1.0 | 1.5 |

`artifacts/release/FluoAxis-research-win-x64-20261008-dpi/` publish 退出码 0；依赖证据包含 43 个 resolved components；发布 EXE 的 DPI 资源门禁通过。545 个清单文件共 251,103,297 bytes，逐项长度/hash 复核全匹配。`research-build.json` SHA-256 为 `6707be2827cc76efbc1e5a2169fa0cb5c1859120a8cf58a96a12c56b5c15d7b9`。发布脚本还扫描产物 UTF-8/UTF-16 中本机仓库/用户身份并排除 PDB；这不替代所有第三方二进制或所有可能绝对路径形式的最终发行审计。

本修复没有性能优化主张，没有吞吐 benchmark；前后 DPI probe 是功能行为测量。先前满 96 孔、23,232 点和报告单次基准保留在 FT007，不用它们证明多 DPI 性能或视觉资格。路径审计 376 个仓库文本、科学边界 21 个文件通过；测试 root 由程序集位置发现，临时目录进程/GUID 唯一。Raw、Analysis、Method、项目存储与归档数值均不受 DPI 更改影响，旧项目仍由现有兼容测试覆盖。

## Review outcomes

独立代理检查源码与成品清单、前后原生数据、Debug 排除条件、资源读取安全、命名空间校验和 CI/发布接线，结论通过，无阻塞意见。确认没有手工二次缩放或系统设置变更。主代理复核 545 文件 hash、最终全套测试与 71 项 FR 完整覆盖。

本地没有创建 PR 或触发 GitHub/外部 Codex review；采用独立只读代理和手工检查，状态为 validated，不表示科学方法批准或完整发布资格。

## Follow-ups

1. 用户需退出旧 FluoAxis 进程，从新构建目录启动，保留全部同级运行文件。运行中的旧进程不会因替换文件改变 DPI 模式。
2. 此次确认了当前 150% 显示器的原生 DPI 修复；主窗口/PlotHost 像素清晰度、125%/200%、跨显示器移动、Narrator/High Contrast 仍需原生视觉验收。
3. 科学签字、真实仪器/Golden 双批准、签名和干净机安装属于FT007记录中的独立门禁。内部科研构建继续标记 unsigned-engineering-build / draft-unapproved。
