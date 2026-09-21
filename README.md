# FluoAxis

Windows 原生科研荧光数据分析工作站（Research Use Only）——双通道 FAM/ROX 动力学荧光数据的导入、校准、质控、dr' 判读、追溯与可视化。

> 仅供科研使用，不用于临床诊断、治疗决策或患者管理。本软件结果必须结合已批准的研究方案、原始数据和人工复核解释。

## 当前状态

Phase 0/1：科学计算核心与技术验证建设期。当前解决方案包含 12 个生产项目和 13 个测试项目，按 plan.md §42 优先级自科学核心向上生长：

```text
src/           Domain → Analysis → Contracts → Application
               Import（OpenXML 流式）→ Infrastructure（受管资源边界）
               Persistence（SQLite）→ ProjectFormat；PlotProtocol → Visualization
               Presentation / App（WinUI 工作台 Shell 与 Guided 阶段骨架，尚非完整生产工作流）
tests/         每层镜像测试 + Golden.Tests
```

WinUI 3 工作台 Shell 已包含 NavigationView、项目 Tab、Guided 阶段骨架、三栏工作区、RUO 标识和按需加载的本地 Plotly Explorer。当前 Fake Gateway 只真实驱动项目/Run 读取和后端 `allowedActions`；Import 到 Analysis 的状态变更、正式 Report 和完整生产工作流尚未接入（见 `docs/adr/ADR-001-toolchain.md` 与 `docs/adr/ADR-002-plot-renderer-and-export.md`）。

## 文档地图

| 文档 | 角色 |
|---|---|
| `BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md` | 需求冻结基线 |
| `plan.md` | 实施总计划（阶段、门禁、验证矩阵） |
| `CLAUDE.md` / `AGENTS.md` | 工程宪法（架构不变量、相对路径 P0 政策） |
| `docs/method/Method-Specification-V1.0.md` | 判读规则实现契约（代码与测试的科学真源） |
| `docs/method/Data-Contract-V1.0.md` | 数据契约与未决科学问题状态表 |
| `docs/adr/` | 架构决策记录 |
| `DEPENDENCIES.md` | 依赖登记（固定版本） |

## 构建与测试

需要 .NET 10 SDK（`global.json` 锁定 10.0.400）。

```powershell
dotnet restore FluoAxis.slnx --locked-mode
dotnet build FluoAxis.slnx --no-restore
./build/Verify-RepositoryPolicy.ps1
./build/Verify-ScientificSourceBoundary.ps1
dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 603
```

测试命令使用 .NET 10 Microsoft.Testing.Platform 的模块发现模式；`--minimum-expected-tests` 防止空测试程序集或发现回归被误判为成功。新增或删除测试时必须同步更新该下限。

## 核心不变量

1. 原始数据不可变；2. C# Analysis Engine 是唯一科学真源；3. `dr' >= 0.4` → 阳性；4. 重复孔冲突 → INVALID 而非均值掩盖；5. 任何持久化内容禁止绝对文件系统路径；6. 全程离线可用。完整清单见 `CLAUDE.md` §47。
