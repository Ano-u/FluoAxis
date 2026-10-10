# 科研工程候选包与签名交接

当前方法及 Golden 仍为未批准科研草案。用户授权先完成可签名安装包与可执行的工程验收，没有授权以自签名证书代替受信任发布身份。

最新方法 1.1 可运行包为 `artifacts/release/FluoAxis-research-win-x64-20261010-method11-final/FluoAxis.App.exe`；对应未签名包为 `artifacts/release/FluoAxis-msix-20261010-method11/FluoAxis.Research_1.0.0.0_x64_unsigned.msix`。发布版 15 步真实 UIA 记录位于 `artifacts/desktop-validation/20261010-method11-final/interaction.json`，实际 FAM/ROX Fm 数值与来源核对见同目录 `fm-check.json`。包 hash 与 MakeAppx 证据见安装包目录 `package-evidence.json`，方法/兼容记录见 `docs/feature/FT-20261010-002-channel-specific-fm.md`。

## 可重现构建

在仓库根目录使用锁定 SDK，所有输出为新的相对目录：

```powershell
./build/Publish-ResearchBuild.ps1 -OutputDirectory artifacts/release/research-candidate
./build/Invoke-DesktopAcceptance.ps1 -PublishDirectory artifacts/release/research-candidate -SourceEntry artifacts/ui-evidence/smart-refinement/valid.csv -EvidenceDirectory artifacts/desktop-validation/candidate-run
./build/Verify-ReleaseManifest.ps1 -PublishDirectory artifacts/release/research-candidate
./build/New-UnsignedMsix.ps1 -PublishDirectory artifacts/release/research-candidate -OutputDirectory artifacts/release/msix-candidate -Publisher 'CN=FluoAxis Research' -Version 1.0.0.0
```

`CN=FluoAxis Research` 是工程候选身份。拿到真实代码签名证书后，必须按证书 Subject 原样设置 Publisher 并重建，不能直接用另一个 Subject 签此包。包名、版本、SHA-256、SDK pin、MakeAppx 校验与解包逐文件 hash 记录在 `package-evidence.json`。

构建使用已锁定的 Microsoft.Windows.SDK.BuildTools，既不另装 SDK，也不从外网取随机二进制。MSIX 包含自包含 .NET/Windows App SDK、XBF/PRI 与本地 Plotly。Evergreen WebView2 仍为环境前提，不声称已随包分发离线安装程序。代码不会将浏览器缓存写入安装目录。

发布目录只允许 research-build.json 声明的文件。新增运行缓存、调试符号、目录穿越、重复路径、大小/hash 变化均阻断打包。用于交互测试的实验工作区和报告在 artifacts/desktop-validation 下，不进入 MSIX。

## 后续签名

受信任证书或组织签名服务由发布负责人提供，私钥不写入仓库。用匹配的 Publisher 重建后，通过固定 SDK 的 SignTool/组织服务进行 SHA-256 签名及可信时间戳，再执行 SignTool verify /pa。签名后重新计算包 hash 并保存签名状态、签名者及时间戳证据；未验证成功不能把 qualification 改成正式发布。

禁止把本候选自签名证书导入用户信任库以“通过”安装。当前机器没有可用签名证书，因此安装、升级、卸载和信任提示不被自动标记通过。

## 工程验收边界

桌面脚本通过真实 UIA 和原生文件对话框执行取消选择、导入分析、图形放大/还原、保存/恢复视图、PNG/SVG/离线 HTML 图形导出、工程包保存、报告快照、HTML/XLSX/PDF 报告导出、关闭重开以及在另一隔离工作区打开工程包。系统另存为按钮使用排队原生消息，避免同步 UIA Invoke 期间触发输入同步 RPC 错误。

绘图另通过真实 Edge/本地 Plotly 与 WinUI WebView 检查：恒定/变化曲线、坐标轴、工具栏、宽窄窗口、缩放、点身份，以及三维渲染。此结果不等于验证了所有交互组合。

尚需独立证据的门禁：正式发布者/受信任签名、干净 Windows 安装/升级/卸载、离线 WebView2 部署、完整拖放/键盘/Narrator/High Contrast、多显示器 DPI、参考机性能/长期内存、科学 Method/真实仪器/双批准 Golden。未执行项目必须保持 not-tested/pending，不能用本机自包含启动代替干净机安装验收。
