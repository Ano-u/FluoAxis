# FluoAxis 依赖登记

所有 NuGet 版本由 `Directory.Packages.props` 精确固定，解析结果由各项目的 `packages.lock.json` 锁定。禁止浮动版本和 Preview/RC。许可证元数据以 NuGet 官方 catalog 和上游 HTTPS 源仓库核对，逐组件审查信息存放在 `build/dependency-metadata.json`。

## 当前直接依赖

| Package | Purpose | License | Pinned Version | Owner | Upgrade gate |
|---|---|---|---|---|---|
| xunit.v3 | 测试框架与 Microsoft.Testing.Platform runner | Apache-2.0 | 4.0.0 | Test Infrastructure | 独立变更；全量构建、测试与发现数量门禁 |
| DocumentFormat.OpenXml | 无 Excel/COM 的 XLSX 读取 | MIT | 3.5.1 | Import | 独立变更；导入 fixture、畸形输入与资源限制回归 |
| Microsoft.Data.Sqlite | SQLite 持久化 | MIT | 10.0.11 | Persistence | 独立变更；migration、并发、WAL、移动重开与工程 roundtrip 回归 |
| Microsoft.WindowsAppSDK | WinUI 3 shell 与自包含 Windows runtime | Microsoft Windows App SDK license | 2.4.0 | App | 独立变更；Debug/Release、启动、DPI、可访问性、可视化和发布回归 |
| Microsoft.Web.WebView2 | 受控本地 PlotHost adapter | BSD-3-Clause | 1.0.4191.47 | Visualization | 独立变更；origin、navigation、WebMessage、离线、导出和 runtime smoke |

`Microsoft.Data.Sqlite` 的许可证是 MIT；旧表中的 Apache-2.0 记录已按 NuGet catalog 修正。SQLitePCLRaw 传递组件为 Apache-2.0。

## 规划中但尚未引用

以下集中版本是后续技术 spike 的候选 pin，不属于当前解析依赖，也不得出现在当前 SBOM 中。引入前仍须完成对应 stable 版本、license、运行时分发和替代方案复核。

| Package | Intended purpose | Candidate version | Decision gate |
|---|---|---|---|
| Microsoft.Extensions.Hosting | Composition root and DI | 10.0.11 | App composition-root slice |
| Microsoft.Extensions.Logging.Abstractions | Path-redacted logging abstraction | 10.0.11 | Infrastructure logging slice |
| Microsoft.Extensions.DependencyInjection.Abstractions | DI abstractions | 10.0.11 | App composition-root slice |
| CommunityToolkit.Mvvm | Presentation MVVM | 8.4.2 | Presentation spike |
| ExcelDataReader | Optional legacy XLS adapter | 3.7.0 | Q-PRD-002 approval and real-file evidence |
| Microsoft.NET.Test.Sdk | Optional IDE test adapter | 18.9.0 | Only if an actual project reference is needed |
| xunit.runner.visualstudio | Optional Visual Studio adapter | 4.0.0 | Only if an actual project reference is needed |
| coverlet.collector | Optional coverage collector | 6.0.4 | Coverage evidence design |

## Evidence generation

Run after locked restore:

```powershell
./build/Generate-DependencyEvidence.ps1
```

The command writes `artifacts/compliance/dependencies-and-licenses.json` and `artifacts/compliance/sbom.cyclonedx.json`. These are generated Phase 1 evidence skeletons, not signed release attestations. The generator fails closed on an unpinned direct package, prerelease/floating pin, lock conflict, missing reviewed metadata, stale metadata, or output outside `artifacts/`.
