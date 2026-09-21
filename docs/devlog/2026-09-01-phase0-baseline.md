# 开发日志：Phase 0 基线 + 科学核心波次实施（2026-09-01）

| 项目 | 内容 |
|---|---|
| 分类 | feature（从零建设 FluoAxis 应用基线） |
| 范围 | 仓库工具链基线、科学契约文档、解决方案骨架、Domain→Analysis→…波次实施 |
| 依据 | plan.md Phase 0/1/2、CLAUDE.md、需求文档 V1.0 |

## 1. 本次会话完成的环境与基线

1. **SDK**：本机原无任何 .NET SDK；通过 `artifacts/setup-sdk.ps1` 安装 .NET SDK 10.0.400 到用户本地（`%LOCALAPPDATA%\Microsoft\dotnet`），`global.json` 锁定（ADR-001）。
2. **解决方案**：`FluoAxis.slnx`（.NET 10 新格式）注册 14 个项目；首建 0 警告 0 错误。
3. **集中包管理**：`Directory.Packages.props` 全固定版本（2026-09-01 NuGet 实查：xunit.v3 4.0.0、OpenXml 3.5.1、Sqlite 10.0.11、WinAppSDK 2.4.0、WebView2 1.0.4191.47 等），`DEPENDENCIES.md` 登记。
4. **科学真源文档**：`docs/method/Method-Specification-V1.0.md`（判读规则实现契约，含 provisional 清单 P-01~P-09）、`docs/method/Data-Contract-V1.0.md`（数据契约 + Q-SCI 状态表）、`docs/method/Golden-Dataset-Design.md`（金标准设计）。

## 2. 证据收集（补充材料 → Q-SCI 状态更新）

- 手稿（Manuscript-highlighted.docx 提取）确认：BioTek Cytation 5、每 2 min 一读持续 120 min；dr' 为 FAM/ROX 二维归一化坐标中样本与 WT 参考点欧氏距离；0.4 阈值来自 25 NAT 阴性样本 ROC 分析。
- 生产流程文档确认：仪器按 2/5/30/120 min 四点采集（与需求"全时程逐点 QC"表面冲突，计算规则兼容两者——Q-SCI-001 待用户裁决；不阻断兼容性基础设施，但阻断 G0、正式 Method 冻结及 Analysis Core 完成声明）。
- 三张微信图片无法读取内容（媒体转移失败），已向用户说明。

## 3. 波次实施计划（子代理调度）

- Wave 1：FluoAxis.Domain（值对象、Method 快照、状态机、RelativePath、错误模型）——单代理（一切的地基）。**已完成：243 测试全绿、0 构建警告**（验收人：主会话）。
- Wave 2（依赖 Domain）：Analysis（科学引擎）∥ Contracts（网关 DTO）∥ Import（XLSX/CSV）∥ ProjectFormat（工程包）。
  - analysis-agent / contracts-agent / projectformat-agent 已于 Wave 1 验收后并行启动。
  - import-agent 启动两次遭遇工具分类器临时限流，待重试。
- Wave 3（依赖前两层）：Application（用例/状态编排）∥ Golden（金标准 + 独立参考计算器）∥ Persistence（SQLite）。
- Wave 4：全量构建/测试/修复，架构规则核查（依赖方向、无绝对路径扫描）。

每波次完成以实际 `dotnet build`/`dotnet test` 退出码为准。

### Wave 1 验收记录（2026-09-01）
- `dotnet build src\FluoAxis.Domain\FluoAxis.Domain.csproj`：成功，0 警告 0 错误。
- `dotnet run --project tests\FluoAxis.Domain.Tests`：**Total: 243, Errors: 0, Failed: 0**。
- 抽查代码质量：BrafMethodSnapshot（默认值 = Method-Spec 附录 JSON，验证完备）、RelativePath（P0 防御全表：rooted/穿越/保留名/控制字符/file://，FrozenSet 性能）、RunStateMachine（转移表精确对应需求 §8，Exported/RunInvalid 终态）。
- Domain 测试工程被调整为 xunit.v3 自宿主（Microsoft.Testing.Platform，`dotnet run` 执行）——VSTest 栈与 MTP 模板冲突，此为仓库测试工程的标准模式，后续测试工程照此办理。

### Wave 2 调度与网关故障记录（2026-09-01）
- 四个代理并行启动后，本机推理网关（127.0.0.1:15721）持续间歇 503（"No active API keys available for this group"）：analysis/import 各失败 2 次、contracts/projectformat 各失败 1 次，均重派接手。
- contracts 前代理留下 15 个源文件（15 个编译错误、测试未建），由 contracts-agent-2 带修复任务接手后再次 503 失败——**主会话直接接手修复**（15 个机械性错误：集合表达式目标类型、RequireSha256 签名、CA1000 泛型静态成员、STJ context Options 遮蔽、命名参数大小写）。
- projectformat 前代理留下 4 个 Manifest 文件（质量良好），projectformat-agent-2 完成接手并通过主会话独立验收。

### Wave 2 完成验收记录（2026-09-01）
全解决方案构建：0 警告 0 错误。各层独立验证：

| 层 | 测试 | 完成方式 |
|---|---|---|
| FluoAxis.Domain | 243/243 | domain-agent 一次成功 |
| FluoAxis.ProjectFormat | 35/35 | projectformat-agent-2 接手完成 |
| FluoAxis.Contracts | 46/46 | 前代理 2 次 503 后**主会话直接修复**（5 处 src 缺陷） |
| FluoAxis.Analysis | 59/59 | analysis-agent-3 接手中断草稿补齐（修 trace 端点值缺失、NTC 分母语义、LQC 混合→Invalid 等 4 处） |
| FluoAxis.Import | 31/31 | import-agent-4 中断后**主会话补齐**（CSV 测试套件、int cell 支持、表头阈值 4→2） |

Import 主会话修复明细：
1. WorkbookBuilder 支持 int/long 装箱 cell（7 个测试解锁）。
2. RegionDiscovery.MinHeaderWells 4→2：A1..H12 孔名模式足够特异，2 列即识别表头（小 fixture 与真实 96 孔导出都覆盖）。
3. CSV/TSV 测试套件（13 个：BOM/无 BOM/分隔符探测/quoted cell/行上限/负 RFU/取消/100k 行性能冒烟）。
4. 列漂移语义裁决：DataMapping 是权威，未映射的多余列忽略（温度列等仪器列不阻断导入）；测试改为锁定该行为。
5. TaskCanceledException 是 OperationCanceledException 子类——断言改 ThrowsAny。

Analysis 报告的 Method-Spec 歧义（已记入待确认清单）：
- **P-01 初始错误路径（保留历史）**：`endpointToleranceSeconds=15` 字段名含"秒"，但早期 Method-Spec §4.3 算例与 GD-018/019 把边界误写为 105 min；初版实现因此按 15 分钟处理并且只设下界。2026-09-03 复审时依据上位需求 FR-TIME-005~007 与 §43 裁决为 15 秒，改为 120 min 双侧闭区间、最近点选择、等距取较晚点；默认 15 秒仍须用真实数据验证后锁定。详见 `docs/fix/FX-20260903-002-endpoint-tolerance-seconds.md`。

### Wave 3 启动（2026-09-01）
- application-agent：Run 聚合/乐观并发/Use Cases/InProcessGateway/审计端口/AllowedActions 状态推导 + fakes 测试
- golden-agent：GD-001~GD-020 金标准 + 独立参考计算器 + 差分测试
- Persistence 待 Application 端口定形后接入（Wave 4）

## 4. 硬门禁核查点（每波次）

- [ ] 依赖方向：Domain 无外依赖；Analysis 只引 Domain；Presentation 只引 Contracts
- [ ] 无绝对文件系统路径进入源码/测试/fixture
- [ ] src 层 0 警告（TreatWarningsAsErrors）
- [ ] 科学常量只在 BrafMethodSnapshot.Default 一处
- [ ] 错误码格式符合命名空间前缀规则

## 5. 遗留与待用户确认

见 `Data-Contract-V1.0.md` §2 Q-SCI 表——P-01~P-09 provisional 项需实验负责人签字；四点 vs 全时程（Q-SCI-001）需裁决；真实双通道 XLSX fixture 待提供（5+ 个）。
