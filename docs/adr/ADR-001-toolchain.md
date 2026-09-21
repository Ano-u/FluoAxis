# ADR-001：工具链与运行时选择

| 项目 | 内容 |
|---|---|
| 状态 | Accepted（Phase 0 内） |
| 日期 | 2026-09-01 |
| 背景 | plan.md Phase 0 要求冻结工具链选型并留下可核查证据 |

## 决策

1. **SDK**：.NET SDK 10.0.400（LTS 线），安装于用户本地目录（`%LOCALAPPDATA%\Microsoft\dotnet`，不污染全局）。`global.json` 锁定 10.0.400，`rollForward: latestPatch`，禁止预览版。
2. **首发 TargetFramework 分层**：
   - 科学与业务核心（Domain/Analysis/Contracts/Application/Import/Persistence/ProjectFormat/PlotProtocol/Reporting/Infrastructure）：`net10.0`，保持平台无关、可快速测试。
   - Windows 壳与可视化适配器（Presentation/App/Visualization）：`net10.0-windows10.0.22621.0` + Windows App SDK。当前 checkout 已引入技术验证项目；生产 Guided UI 尚未实现。
3. **包管理**：Central Package Management（`Directory.Packages.props`），全固定版本，lock file 开启。首发波次实际使用的包：测试栈（xunit v3 + Test SDK）与导入栈（DocumentFormat.OpenXml）等；其余版本条目为占位，实际引用时在 DEPENDENCIES.md 登记后才允许进入 csproj。
4. **测试框架**：xUnit v3。src 层 `TreatWarningsAsErrors=true`；tests 层放开（分析器仍开启）。
5. **构建验证**：每波次结束时必须实际执行 `dotnet build`/`dotnet test` 并记录退出码。

## 依据

- 需求文档 §10 选型（.NET 10 LTS、WinUI 3、Windows App SDK Stable）。
- CLAUDE.md §5（现代技术政策）与 §31（依赖管理）。
- 本机初始无任何 .NET SDK；经 dotnet-install 脚本安装 10.0.400 成功（见会话记录）。

## 后果

- 科学核心不依赖 Windows 桌面栈 → 无 Windows App SDK 依赖即可持续构建与测试，降低环境摩擦。
- WinUI/WebView2/Plotly 技术验证壳已经落地并由 ADR-002 跟踪运行、导出、安全与性能证据；这不代表生产 Guided UI 已完成。
- Windows App SDK 已固定为 `2.4.0`；运行时分发与完整发布资格仍由 ADR-002 和发布门禁跟踪。
