# FluoAxis 项目实施总计划

> 2026-10-10 方法更新：新建 Run 的 Method 1.1 使用 FAM 阳性平台均值、ROX 阳性 120 min 端点作为 Fm；旧 1.0 保留原计算与 hash。规则与候选验证见 `docs/method/Method-Specification-V1.1.md`、`docs/feature/FT-20261010-002-channel-specific-fm.md`，保持草案。

> 当前实施状态以 `docs/design/Requirements-Acceptance-Matrix-V1.1.md` 和 `docs/feature/FT-20261007-007-engineering-workflow-completion.md` 为准。下文的绿地仓库、初始工具链和 18 周排期是 2026-08-10 立项基线，保留用于历史追溯，不能作为当前 checkout 的完成度结论。用户已明确保留 Method/Data Contract 草案并先完成工程设计和验证。

---

| 项目 | 内容 |
|---|---|
| 文档状态 | Execution-ready draft / 待 Phase 0 科研规则冻结后转为 Baseline |
| 版本 | Plan V1.0 |
| 日期 | 2026-08-10 |
| 项目名称 | [FluoAxis](https://github.com/Ano-u/FluoAxis) |
| 规范仓库 | [Ano-u/FluoAxis](https://github.com/Ano-u/FluoAxis) |
| 任务分类 | feature：从零建设 Windows 科研数据分析与判读工作站 |
| 当前分支假设 | 当前 checkout 为 `main`，HEAD 为 `c09789e`；已有仓库绑定初始提交，但尚无应用源码基线 |
| 需求基线 | `BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md` |
| 补充材料 | `整套生产流程（预估版）.docx` |
| 产品边界 | 第一阶段仅限科研用途，不作为临床诊断软件，不输出治疗建议或良恶性判断 |
| 目标形态 | Windows 11 x64 原生桌面应用；原生 UI 与本地 C# 分析后端分层协作 |
| 路径硬门禁 | 所有项目自有路径必须是相对路径；绝对文件系统路径不得进入源码、配置、测试、文档、日志、报告、合同或持久化产物 |
| 计划周期 | 建议 18 周，4-6 人核心团队；以门禁通过而不是日历到期作为阶段完成条件 |

> 本计划不是对需求文档的复述，而是把需求转换为可执行的工作包、接口契约、双轨排期、验证矩阵和发布门禁。凡涉及科学判读的规则，以实验负责人签字冻结的 Method Specification 为准；凡涉及具体 SDK 或第三方库版本，以 Phase 0 当日可获得的 Stable 版本和兼容性实测为准。

## 1. 执行结论

### 1.1 当前项目真实状态

当前目录是一个尚未建立应用代码基线的绿地项目仓库：

- 当前 checkout 为 `main`，HEAD 为 `c09789e`，已有仓库绑定提交；
- 已跟踪内容主要是需求 Markdown、生产流程 Word 和既有预算/研究过程产物；
- 没有 `.sln`、应用源码、测试、CI、依赖锁文件或安装工程；
- 当前机器可以调用 `dotnet`，但 `dotnet --list-sdks` 没有列出已安装 SDK；
- 需求文档提到的真实双通道 XLSX、意见汇总、PPT 和原理 PDF 不在当前目录，无法据此完成算法独立复核；
- 因此“需求冻结基线”目前应理解为产品方向冻结，不应理解为所有科学输入契约和边界条件已经可编码。

### 1.2 总体实施判断

推荐继续采用需求基线中的总体方向：

```text
WinUI 3 native shell
  -> Presentation / Contracts
  -> in-process Application backend
  -> deterministic C# Analysis Engine
  -> Import / SQLite / Project / Reporting adapters
  -> local WebView2 + pinned Plotly renderer
```

这里的“前后端分离”是清晰的工程边界和协作契约，不是把本地桌面软件拆成网络微服务：

- 前端包括 WinUI 3 Shell、页面、ViewModel、原生表格/孔板、PlotHost 和可访问性；
- 后端包括 Application Use Cases、状态机、Domain、Analysis Engine、Import、Persistence、Project Archive、Reporting；
- 前端只通过稳定的 `Contracts`/Gateway 调用后端，不直接调用 SQL，不重算公式，不修改业务状态；
- Phase 1 以前端 Fake Gateway 和 JSON fixtures 并行开发，后端用 contract tests 保证替换 Fake 后行为一致；
- 第一阶段采用同进程调用，避免 localhost HTTP 服务、端口、防火墙、认证和部署复杂度；
- 将来若出现集中式多用户版本，再在不改变业务契约语义的前提下增加远程 transport adapter。

### 1.3 关键路径

```text
科研数据契约冻结
  -> Method 规则冻结
  -> Golden Dataset 与独立参考结果
  -> Contracts / Run State 冻结
  -> Analysis Engine
  -> Import + Guided Analysis 纵向闭环
  -> Project/Report 可复现闭环
  -> Explorer/3D
  -> Hardening + Pilot
```

Data Explorer、3D 和报表不能成为绕过科学主链路的理由。第一个可演示版本应优先完成“一份真实双通道文件从导入到可审计科研结果”的纵向闭环。

## 2. 启动前必须解决的阻断项

### 2.1 科研与数据契约问题

| ID | 未决问题 | 为什么阻断 | 责任人 | 关闭证据 |
|---|---|---|---|---|
| Q-SCI-001 | 生产流程写 `2/5/30/120 min` 四点采集，需求要求 NTC `0-120 min` 全时程逐点 QC；四点是仪器实际采样、报告抽样，还是生产 QC 摘要？ | 决定导入模型、NTC QC 点集和曲线 UI | 实验负责人 + 产品负责人 | 签字版采样协议、代表性原始文件、Method 条款 |
| Q-SCI-002 | 真实双通道 FAM/ROX 文件如何组织：双 Sheet、同 Sheet 两区、重复表头还是两个文件？ | 决定 importer 与 mapping UX | 实验负责人 | 至少 5 个去标识化真实 fixture |
| Q-SCI-003 | `endpointToleranceSeconds=15` 只是工程默认；是否接受、是否对所有仪器通用？ | 决定 endpoint 选择与 Invalid 条件 | 实验负责人 | Method V1.0 的明确容差和例子 |
| Q-SCI-004 | 90-120 min 平台窗口至少需要多少有效点？窗口端点是否闭区间？ | 防止只有 1 个点时 RR 被错误判 Pass | 实验负责人 | 最小点数、边界和缺失点规则 |
| Q-SCI-005 | NTC、Positive、WT、LQC、Sample 各自最低/期望重复孔数是多少？缺孔如何处理？ | 决定 Plate 校验和状态机 | 实验负责人 | Plate role cardinality 表 |
| Q-SCI-006 | WT 只检查组均值 `<0.05`，是否需要单孔范围或重复孔一致性规则？ | 均值可能掩盖异常孔 | 实验负责人 | WT per-well 规则或明确“不要求” |
| Q-SCI-007 | `Fm-Fb` 的“分母过小”数值阈值是什么？是绝对值、相对值还是仪器相关？ | 当前需求只有语义，没有可执行阈值 | 实验负责人 + 算法负责人 | 公式、单位、边界向量 |
| Q-SCI-008 | LQC 0.1% 的制备真值、可接受率和失败重测规则是什么？ | `0.1%` 是性能声明，需要数据支持 | 实验负责人 + QA | LQC 制备/定值/稳定性文件与接受标准 |
| Q-SCI-009 | `dr' >= 0.4` 的来源、训练/确定过程、独立验证集和适用样本类型是什么？ | 软件可正确实现规则，但不能替代规则的科学验证 | 科学负责人 | 阈值来源报告、锁定验证集和统计结论 |
| Q-SCI-010 | 样本基质、DNA 输入范围、仪器型号和试剂批次边界是什么？ | 决定 Intended Use 和 Golden Dataset 覆盖 | 科学负责人 | Research Intended Use V1.0 |
| Q-SCI-011 | 重复孔冲突后允许重分配、排除孔还是只能重做实验？ | 决定审计与 UI 操作权限 | 实验负责人 + QA | 异常处理 SOP |
| Q-SCI-012 | 原始文件中的公式单元格、空值、饱和值、负 RFU 和非等间隔时间如何处理？ | 决定导入安全与数值语义 | 算法负责人 | 数据清洗/拒绝规则 |

Q-SCI-001、002、003、004、005、007、009 未关闭前，不允许宣布 Analysis Core 完成。可以做 Spike 和接口原型，但所有相关决定必须标注为 provisional。

### 2.2 产品与交付问题

| ID | 未决问题 | 默认假设 | 冻结时间 |
|---|---|---|---|
| Q-PRD-001 | 首发 Windows 最低版本与实验室参考机配置 | Windows 11 22H2+、x64 | Phase 0 |
| Q-PRD-002 | 是否真的需要首版 `XLS` | 保留兼容 adapter，但若没有真实样本可从 MVP 移出 | Phase 0 |
| Q-PRD-003 | 是否真的需要首版 3D `surface` | scatter3D/line3D 必须，surface3D 可延后 | Phase 0 |
| Q-PRD-004 | Formula Builder 是否属于首个可用版本 | Guided MVP 不依赖；放到 Explorer 里程碑 | Phase 0 |
| Q-PRD-005 | PDF 报告的品牌、签字、语言和页模板 | 中文 Research Report，无电子签名 | Phase 1 |
| Q-PRD-006 | Method 谁可以创建、编辑、批准和停用 | 第一版管理员编辑，审批流程先记录不做临床签名 | Phase 1 |
| Q-PRD-007 | 是否允许把原始输入嵌入 `.fluoaxisproj` | 默认嵌入并保存 hash；提供明确选项 | Phase 1 |
| Q-PRD-008 | ARM64 是否首发 | 不进入首发承诺 | Release Planning |

## 3. 产品范围与声明边界

### 3.1 第一阶段必须交付

- 项目和 Run 管理；
- XLSX/CSV/TSV/TXT 导入，必要时通过隔离 adapter 支持 legacy XLS；
- 自动发现候选数据区与人工确认映射；
- 96 孔 Plate Map；
- 固定 BRAF V600E Research Method 的 Calibration、Control QC、Sample Analysis；
- 每孔和组级计算追溯；
- PASS/FAIL/INVALID 状态与结构化原因；
- 原始数据只读、Derived Dataset 隔离；
- `.fluoaxisproj` 保存、打开、恢复和版本迁移；
- Excel、PDF、PNG，符合条件的 2D 图支持 SVG；
- 2D/3D Explorer、PointId 回溯、距离、视觉平滑；
- Golden Dataset、导入 fixtures、契约测试、集成测试、性能基线；
- 离线安装包、诊断页、支持包和发布证据。

### 3.2 明确不在第一阶段

- 临床诊断、治疗建议、药物推荐、预后结论；
- 患者姓名、身份证、住院号或 HIS/LIS；
- 法规注册声明、电子签名、21 CFR Part 11 合规声明；
- 仪器实时连接、云同步、多用户服务端；
- AI 自动诊断；
- 任意 Python/C#/JavaScript 脚本执行；
- 任意第三方插件加载；
- 384 孔完整体验；
- 把 BRAF V600E 以外的 SNV 做成可发布 Method。

### 3.3 RUO 文案要求

所有 About、报告、导出和安装包说明统一使用经产品/法规审核的中文科研用途文案。推荐基线：

```text
仅供科研使用，不用于临床诊断、治疗决策或患者管理。
本软件结果必须结合已批准的研究方案、原始数据和人工复核解释。
```

不要在界面、网站或报告中暗示已获得 NMPA/FDA 批准。FDA 已批准的 BRAF companion diagnostic 与特定试剂、仪器、样本类型和适应证绑定，不能为本项目自定义双通道方法背书。

## 4. 目标架构

### 4.1 逻辑架构

```text
┌───────────────────────────────────────────────────────────────┐
│ WinUI 3 App                                                   │
│ Shell / Views / ViewModels / Native Grid / Plate / Dialogs    │
└──────────────────────────┬────────────────────────────────────┘
                           │ Commands / Queries / Snapshots
┌──────────────────────────▼────────────────────────────────────┐
│ FluoAxis.Contracts + IFluoAxisGateway                        │
│ versioned DTO / Result / Error / RunSnapshot / PlotProtocol   │
└──────────────────────────┬────────────────────────────────────┘
                           │ in-process adapter
┌──────────────────────────▼────────────────────────────────────┐
│ Application                                                   │
│ Use Cases / Run State Machine / Authorization / Transactions  │
└───────────────┬───────────────────┬───────────────────────────┘
                │                   │
┌───────────────▼──────────┐  ┌────▼────────────────────────────┐
│ Domain + Analysis        │  │ Ports                          │
│ Method / QC / dr' /      │  │ Import / Repository / Archive  │
│ deterministic trace      │  │ Report / Plot / Clock / Hash   │
└──────────────────────────┘  └────┬────────────────────────────┘
                                   │ adapters
                 ┌─────────────────▼────────────────────────────┐
                 │ OpenXML / ExcelDataReader / CSV / SQLite     │
                 │ ZIP64 / HTML-PDF / WebView2-Plotly / Logging │
                 └───────────────────────────────────────────────┘
```

### 4.2 建议 Solution 结构

```text
FluoAxis.sln
├─ src/
│  ├─ FluoAxis.App/                 WinUI composition root
│  ├─ FluoAxis.Presentation/        ViewModels/navigation/UI state
│  ├─ FluoAxis.Contracts/           frontend-backend contract
│  ├─ FluoAxis.Application/         use cases/state orchestration
│  ├─ FluoAxis.Domain/              entities/value objects/invariants
│  ├─ FluoAxis.Analysis/            pure deterministic calculations
│  ├─ FluoAxis.Import/              importer ports and adapters
│  ├─ FluoAxis.Persistence/         SQLite/migrations/repositories
│  ├─ FluoAxis.ProjectFormat/       .fluoaxisproj pack/open/verify/migrate
│  ├─ FluoAxis.Reporting/           report snapshot/templates/PDF
│  ├─ FluoAxis.PlotProtocol/        PlotSpec and bridge messages
│  ├─ FluoAxis.Visualization/       WebView2 renderer adapter
│  └─ FluoAxis.Infrastructure/      fs/logging/clock/diagnostics
├─ web/
│  └─ FluoAxis.PlotHost/            local HTML/TS/Plotly host
├─ tests/
│  ├─ FluoAxis.Analysis.Tests/
│  ├─ FluoAxis.Application.Tests/
│  ├─ FluoAxis.Contract.Tests/
│  ├─ FluoAxis.Import.Tests/
│  ├─ FluoAxis.Persistence.Tests/
│  ├─ FluoAxis.ProjectFormat.Tests/
│  ├─ FluoAxis.Reporting.Tests/
│  ├─ FluoAxis.Visualization.Tests/
│  ├─ FluoAxis.Golden.Tests/
│  ├─ FluoAxis.Integration.Tests/
│  └─ FluoAxis.Benchmarks/
├─ samples/
│  ├─ synthetic/
│  └─ golden/                            approved, hashed, change-controlled
├─ docs/
│  ├─ adr/
│  ├─ method/
│  ├─ validation/
│  ├─ risk/
│  └─ release/
├─ assets/
│  ├─ plots/
│  └─ reports/
├─ build/
├─ Directory.Build.props
├─ Directory.Packages.props
├─ global.json
├─ NuGet.Config
├─ .editorconfig
├─ DEPENDENCIES.md
├─ SECURITY.md
└─ README.md
```

### 4.3 依赖规则

1. `Domain` 不引用 UI、SQLite、Excel、WebView2、文件系统或日志实现。
2. `Analysis` 只引用 `Domain` 和 BCL；不得引用 `Application` 或 `Presentation`。
3. `Application` 引用 Domain/Analysis 和 port interfaces，不引用具体 adapter。
4. `Presentation` 只引用 `Contracts`，不引用 Domain/Analysis/Persistence。
5. `PlotHost` 不接收完整路径、数据库连接或算法对象，只接收 versioned plot DTO/只读数据 buffer。
6. `Reporting` 消费不可变 ReportSnapshot，不从 UI 或活动数据库临时拼结果。
7. 禁止环形项目引用；CI 用架构测试守护。

## 5. 前后端协作契约

### 5.1 Gateway

```csharp
public interface IFluoAxisGateway
{
    Task<Result<ProjectSnapshotDto>> CreateProjectAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken);

    Task<Result<ImportPreviewDto>> InspectImportAsync(
        InspectImportCommand command,
        IProgress<OperationProgressDto>? progress,
        CancellationToken cancellationToken);

    Task<Result<RunSnapshotDto>> ConfirmMappingAsync(
        ConfirmMappingCommand command,
        CancellationToken cancellationToken);

    Task<Result<RunSnapshotDto>> AssignWellsAsync(
        AssignWellsCommand command,
        CancellationToken cancellationToken);

    Task<Result<CalibrationResultDto>> RunCalibrationAsync(
        RunCalibrationCommand command,
        CancellationToken cancellationToken);

    Task<Result<ControlQcResultDto>> RunControlQcAsync(
        RunControlQcCommand command,
        CancellationToken cancellationToken);

    Task<Result<SampleAnalysisDto>> AnalyzeSamplesAsync(
        AnalyzeSamplesCommand command,
        CancellationToken cancellationToken);

    Task<Result<RunSnapshotDto>> GetRunAsync(
        GetRunQuery query,
        CancellationToken cancellationToken);
}
```

### 5.2 所有命令的并发字段

所有会改变 Run 的命令必须携带：

```text
requestId
runId
expectedRunRevision
actorId
issuedUtc
contractVersion
```

后端成功提交后增加 `runRevision`。旧页面提交旧 revision 时返回 `STALE_RUN_REVISION`，前端刷新快照并明确提示，不进行自动覆盖。

### 5.3 RunSnapshot

```text
RunSnapshotDto
├─ runId
├─ revision
├─ domainState
├─ allowedActions[]
├─ blockingIssues[]
├─ warningIssues[]
├─ methodId
├─ methodVersion
├─ sourceFiles[]
├─ mappingSummary
├─ calibrationSummary
├─ controlSummary
├─ analysisSummary
└─ updatedUtc
```

前端按钮是否可用必须依赖 `allowedActions`，不得在 ViewModel 中重写状态转换或 QC 条件。

### 5.4 统一结果与错误

```text
Result<T>
├─ isSuccess
├─ value?
├─ errors[]
└─ correlationId

AppErrorDto
├─ code
├─ severity: info | warning | blocking | fatal
├─ userMessage
├─ recommendedAction
├─ fieldPath?
├─ wellId?
├─ channel?
├─ sourceLocation?
└─ technicalReference?
```

错误代码属于兼容契约。文案可以优化，代码不可无迁移随意改名。首批命名空间：

```text
IMPORT_*
MAPPING_*
PLATE_*
METHOD_*
CALIBRATION_*
CONTROL_*
SAMPLE_*
PROJECT_*
REPORT_*
PLOT_*
SYSTEM_*
```

### 5.5 UI 状态与业务状态分离

业务状态由 Application 管理：

```text
NEW -> IMPORTED -> MAPPED -> CALIBRATING
  -> CALIBRATION_PASSED -> CONTROL_QC
  -> READY_FOR_ANALYSIS -> ANALYZING -> ANALYZED -> EXPORTED
  -> any blocking scientific failure -> RUN_INVALID
```

每页自己的异步 UI 状态：

```text
Initial | Loading | Empty | Ready | Submitting
| RecoverableError | FatalError
```

`RUN_INVALID` 后仍可进入 Explorer，但必须隐藏/失效旧的正式 classification，并显示结构化阻断原因。

### 5.6 Contract fixture 先行流程

每个纵向功能按同一顺序开发：

```text
requirement examples
 -> JSON contract fixture
 -> frontend Fake Gateway screen
 -> backend use case
 -> C# contract serialization test
 -> fake/real conformance test
 -> in-process integration test
 -> UI smoke
```

前端和后端的 Definition of Ready 都要求已存在成功、失败、取消、空数据和旧 revision fixture。

## 6. 科学计算合同

### 6.1 不可破坏的领域不变量

1. 原始 RFU 永不在原位修改。
2. 正式计算只使用 Raw RFU，不使用平滑、LOD、筛选后的显示数据。
3. `Fb` 仅来自通过 per-well NTC QC 的 NTC 组。
4. `Fm` 仅来自通过平台 QC 的 100% Positive 组。
5. NTC 先逐孔、逐通道、逐有效时间点 QC，再生成 Run `Fb`。
6. 关键 Positive 或 NTC 任一孔失败，Calibration 失败，不静默剔除。
7. WT 在 Calibration 通过后计算；WT 二维均值是 LQC/Sample 的共同参考点。
8. LQC/Sample 先逐孔分类，再检查重复孔方向，最后才允许计算并报告均值。
9. 重复孔方向冲突时最终状态是 INVALID，平均值不得覆盖冲突。
10. `dr'` 是 FAM/ROX 二维欧氏距离；Explorer 的三维距离不是 `dr'`。
11. 阈值比较使用未四舍五入 `double`；`0.4` 包含在阳性侧。
12. Method Snapshot 对 Run 不可变；Method 更新只影响新 Run。

### 6.2 计算顺序

```text
Select endpoint/window points
  -> Positive per-well plateau RR
  -> Positive per-well Fm
  -> Run Fm per channel
  -> NTC per-well Fb at endpoint
  -> NTC per-well full-course Y QC
  -> Run Fb per channel
  -> WT per-well endpoint Y
  -> WT reference mean (FAM, ROX)
  -> LQC per-well dr'/call/replicate consistency
  -> Sample per-well dr'/call/replicate consistency
  -> immutable analysis summary and trace
```

### 6.3 计算追溯记录

每个可见结果应由后端返回 immutable calculation trace：

```text
traceId
algorithmVersion
methodSnapshotHash
inputSnapshotHash
sourcePointIds[]
intermediateValues[]
comparisonOperator
thresholdRaw
resultRaw
displayFormatting
finalStatus
errorCodes[]
```

前端 `View Calculation` 只渲染 trace。报告也从同一 trace/report snapshot 生成，避免 UI、Excel、PDF 各自重算。

### 6.4 Golden Dataset 治理

Golden Dataset 不是普通测试 fixture。要求：

- 每个 case 有稳定 ID、Method hash、输入 hash、期望中间量和最终状态；
- 由实验负责人和算法负责人双人批准；
- 真实数据先去标识化；不能去标识化时不进入 Git；
- 期望值由独立参考计算产生，不复用生产 Analysis Engine；
- 修改 golden 需要变更说明、重新审批和旧版本保留；
- 至少包含 GD-001 至 GD-009，以及边界/异常扩展组。

扩展 case：

```text
GD-010 exact dr'=0.4
GD-011 exact RR=1%
GD-012 plateau single-point rejection
GD-013 duplicate time
GD-014 non-monotonic time
GD-015 missing one channel
GD-016 non-finite/overflow value
GD-017 negative RFU policy
GD-018 endpoint just inside tolerance
GD-019 endpoint just outside tolerance
GD-020 affine-scaling invariance
GD-021 stable result after save/open
GD-022 old Method snapshot after current Method update
```

## 7. 数据、存储与文件安全

### 7.1 导入流水线

```text
Open read-only stream
 -> identify format by extension + signature
 -> apply size/row/cell/decompression limits
 -> inspect metadata without formula execution
 -> discover candidate regions
 -> present preview
 -> user confirms mapping
 -> validate times/channels/wells
 -> normalize to internal rows
 -> one transactional persistence batch
 -> calculate and persist source hash
```

导入器不得：

- 启动 Excel、执行宏、计算公式或访问外部链接；
- 一次把不受限工作簿完整展开到内存；
- 默默猜测文本编码、分隔符或小数文化；
- 把空值自动变为 0；
- 在只读预览阶段创建正式 Run 结果。

### 7.2 内部数据模型

核心模型建议采用长表语义：

```text
SignalPoint
├─ runId
├─ channel
├─ wellId
├─ pointIndex
├─ elapsedTicks
├─ rfu
├─ sourceRow
├─ sourceColumn
└─ flags[]
```

原仪器宽表仅属于 import adapter。Domain/Analysis 不应依赖 Excel 列位置或 Sheet 名。

### 7.3 SQLite

- 开启 foreign keys；
- 工作区可以 WAL；正式打包前 checkpoint 并关闭连接；
- migrations 只前进，不在生产文件上做无备份的 destructive migration；
- 批量 signal 写入使用单事务和 prepared statement；
- 每次分析基于 immutable input revision；
- audit event 与业务事务同提交；
- 关键表保存 schema version、created/updated UTC 和 revision。

### 7.4 `.fluoaxisproj`

工程包采用 ZIP64，但打开时必须防御：

- Zip Slip/path traversal；
- 绝对路径、盘符、UNC 路径；
- 过高压缩比和解压炸弹；
- 重复 entry、大小写碰撞、非法 Unicode 名称；
- manifest 中未声明的关键 entry；
- hash 不匹配；
- 未支持的未来 formatVersion；
- 部分写入和 stale WAL。

解包到 workspace-local、唯一的项目工作目录。正式保存流程：

```text
freeze save snapshot
 -> write unique sibling temp package
 -> flush and close
 -> reopen
 -> validate manifest/schema/hashes/database
 -> atomic replace
 -> retain recovery metadata
```

不得硬编码开发机绝对路径。所有测试临时目录使用仓库内唯一根并包含测试节点/worker 标识。

### 7.5 导出安全

- Excel 单元格若以 `=`, `+`, `-`, `@` 开头且语义为文本，必须防公式注入；
- HTML/PDF 模板对 Sample ID、Notes、文件名做 context-aware encoding；
- SVG 不接受未经清洗的任意用户 markup；
- 交互 HTML 明确标注含数据，不自动引用 CDN；
- 导出文件使用不可变 ExportSnapshot，生成失败不改变 Run；
- 输出旁可生成 SHA-256/manifest，报告内记录 App/Method/algorithm version。

## 8. 前端实施设计

### 8.1 页面与导航

```text
Home
Project / Run
Import Preview
Data Mapping
Plate Map
Calibration
Control QC
Sample Analysis
Calculation Trace
Data Explorer
Report / Export
Diagnostics / Settings
```

黄金路径使用 stepper/状态导航，但主导航不显示教学性长文。任何阻断状态在工作区内给出原因和下一步动作。

### 8.2 Plate Map

- 固定 8x12 几何，不因文本变化改变孔位尺寸；
- FAM/ROX 绑定为同一逻辑 Well；
- 表格列与孔位双向高亮；
- 支持框选、Shift 扩选、批量角色分配；
- 键盘方向键移动，Space/Enter 操作；
- 角色使用颜色 + 图标/纹理 + 短标签，不能只用颜色；
- 前端即时显示明显冲突，后端仍做最终验证；
- Undo/Redo 记录批量 Assignment Command，而非 96 个零散 UI 事件。

### 8.3 Guided Analysis 页面

Calibration 页面同时显示：

- NTC per-well `Fb`；
- NTC 全程 Y 曲线与容许带；
- Positive per-well `Fm`；
- 90-120 min 平台窗口；
- RR 原值和阈值；
- Run `Fb/Fm`；
- PASS/FAIL/INVALID、ErrorCode 和行动建议。

图上的点、逐孔表和错误列表使用稳定 WellId/PointId 双向定位。页面不内嵌任何正式公式副本。

### 8.4 VirtualDataSheet

这是独立高风险组件，先 Spike 再承诺完整功能：

```text
ColumnHeaderPresenter
RowHeaderPresenter
Virtualizing CellViewport
SelectionLayer
ActiveCellLayer
one reusable CellEditorOverlay
shared ScrollModel
```

实施顺序：

1. 固定行高、列宽前缀 offset、viewport 命中测试；
2. 比较行虚拟化与二维 `VirtualizingLayout`；
3. 可见 cell recycling；
4. selection range，不为每格建对象；
5. 单一编辑器 overlay；
6. TSV clipboard 和批量验证；
7. immutable sort/filter view；
8. Grid/GridItem/Selection/Value AutomationPeer；
9. 大数据和 DPI/键盘/Narrator 验收。

性能目标：

- 典型 121 x 98 数据首次可交互 `< 200 ms`；
- 100,000 x 32 合成数据滚动 p95 frame `< 33 ms`；
- realized cell 不超过 viewport 需求的 1.5 倍；
- 复制 10,000 cells、滚动复用和取消编辑后无值串位。

### 8.5 PlotHost

PlotHost 握手：

```text
plot.ready
├─ protocolVersion
├─ plotlyVersion
├─ supportsSharedBuffer
├─ supportsWebGl2
└─ supportsToImage
```

Host 到 JS：

```text
plot.render | plot.resize | plot.export
plot.resetCamera | plot.setSelection | plot.dispose
```

JS 到 Host：

```text
plot.rendered | plot.error | plot.pointClicked
plot.selectionChanged | plot.cameraChanged | plot.exportCompleted
```

每条消息含 `schemaVersion/requestId/plotId/generation`。过期 generation 的返回必须丢弃。

渲染规则：

- 复用 WebView2，更新使用 `Plotly.react`；
- 用 `uirevision` 保留 camera/zoom，用 data revision 表示数据变化；
- 高频选择/样式更新合并；
- dispose 前解绑事件并 purge；
- SVG trace 才承诺真 SVG；`scattergl` 和 3D 按高分辨率 PNG/raster 处理；
- WebGL 不可用时给出可读降级状态，不能显示空白画布；
- PointId 回传后由 C# 取精确坐标并计算距离。

### 8.6 前端可访问性

- 所有黄金路径可只用键盘；
- 焦点顺序与视觉顺序一致；
- 图表旁有等价原生表格/摘要，Plotly canvas 不是唯一信息源；
- Plate/Grid 实现自定义 AutomationPeer；
- 支持 125/150/200% DPI、High Contrast、Narrator、深浅色；
- 状态使用文字和图标；
- 目标按 WCAG 2.2 AA 设计，Windows 实机使用 Accessibility Insights 复核。

## 9. 后端实施设计

### 9.1 Domain

优先建立以下强类型对象：

```text
WellId
ElapsedTime
FluorescenceChannel
WellRole
SignalPoint / SignalSeries
MethodId / MethodVersion / MethodSnapshot
RunId / RunRevision / RunState
SourceFile / SourceLocation
CalibrationResult
WtReference
ReplicateResult
SampleResult
CalculationTrace
```

禁止用 `DataTable`、`Dictionary<string, object>` 或随意 JSON 作为核心领域模型。

### 9.2 Analysis Engine

每个函数应纯、确定、无 IO、无全局设置、无当前时间依赖。接口按 use case 需要组织，不让 UI 自行串联：

```text
Calibrate
EvaluateControls
AnalyzeSamples
BuildCalculationTrace
```

算法实现需要：

- `double.IsFinite` 检查；
- checked size/count arithmetic；
- 统一 boundary semantics；
- 不提前 Round；
- 明确空集合、缺 endpoint、分母异常；
- 稳定的错误代码；
- 与独立 oracle 的 differential test；
- 保存 algorithm version，而不只保存 App version。

### 9.3 Application Use Cases

```text
CreateProjectUseCase
CreateRunUseCase
InspectImportUseCase
ConfirmMappingUseCase
AssignWellsUseCase
RunCalibrationUseCase
RunControlQcUseCase
AnalyzeSamplesUseCase
CreateDerivedDatasetUseCase
SaveProjectUseCase
OpenProjectUseCase
ExportResearchReportUseCase
BuildSupportBundleUseCase
```

Use Case 的共同模板：

```text
validate request/contract version
 -> load aggregate and check revision/state
 -> create immutable input snapshot
 -> execute pure domain/analysis logic
 -> persist result + audit event transactionally
 -> return new RunSnapshot
```

### 9.4 Method schema

Method JSON 使用严格 schema：

- 必填字段显式；
- 不允许未知字段静默改变语义；
- 所有数值有单位和 inclusive/exclusive 语义；
- 包含 method schema version 与 algorithm compatibility range；
- load 后 canonicalize 并计算 hash；
- 保存到 Run 时复制 immutable snapshot；
- 老版本只读兼容；迁移不能改变旧 Run 的历史结果。

### 9.5 Reporting

报告流程：

```text
freeze ReportSnapshot
 -> validate all required sections
 -> render local encoded HTML/CSS
 -> embed pinned chart images
 -> PrintToPdfAsync
 -> reopen/render/text-check PDF
 -> persist export record/hash
```

报告至少记录 source hash、Method hash/version、algorithm version、App version、每孔结果、重复孔一致性、Run validity 和 RUO 声明。

## 10. 安全与风险工程

### 10.1 初始威胁模型

主要信任边界：

```text
untrusted instrument file -> Importer
untrusted project archive -> Project opener
user text -> HTML/SVG/Excel exporter
C# trusted host <-> WebView2 PlotHost
workspace database -> packaged project
application -> local filesystem
```

首批滥用场景：

- 恶意 XLSX/ZIP 解压炸弹；
- 文件路径穿越和工程覆盖；
- Excel 公式注入；
- Notes/Sample ID 进入 HTML 的 XSS；
- WebView2 外部导航和恶意 WebMessage；
- Formula Builder 任意代码执行；
- 巨大数组导致内存耗尽；
- 旧 Method/工程被误解释为当前 schema；
- 日志或支持包泄漏原始数据；
- 部分保存导致有效工程损坏。

### 10.2 WebView2 基线

- Plotly/HTML/CSS/字体全部本地固定，不用 CDN；
- 使用虚拟主机映射和精确 origin allowlist；
- CSP 从 `default-src 'none'` 开始，仅按 Spike 结果开放最小能力；
- 拒绝外部 navigation/new-window/download/permission；
- Release 关闭 DevTools 和默认上下文菜单；
- 验证 WebMessage source、type、schema、payload size；
- 不注册 host object，不暴露路径/SQL/算法服务；
- SharedBuffer 只读、带长度/offset 校验并及时 release；
- CI 离线 smoke 中任何外部 HTTP(S) 请求均失败。

### 10.3 风险管理方式

科研版不宣称 IEC 62304/ISO 14971 或 NMPA 注册合规，但从第一天保留下列证据：

- software development plan；
- requirements -> design -> test traceability；
- risk register 与 risk controls；
- SOUP/third-party inventory；
- anomaly/known-issue list；
- release configuration 和 SBOM；
- verification summary；
- change control 和 post-release issue handling。

这样可以避免未来产品化时完全重建证据链，但不能把“采用这些做法”写成“已认证”。

## 11. 分阶段实施路线

### 11.1 周期假设

建议核心团队：

```text
1 Product/Scientific Owner
1 Backend/Architecture Lead
1 Analysis/Data Engineer
1-2 WinUI/Visualization Engineers
1 QA/Automation Engineer
part-time Regulatory/Quality Reviewer
```

4 人以下或人员兼任时，应按能力和门禁顺延，不应通过减少 Golden、契约、恢复或安全测试压缩周期。

### Phase 0 - 证据、科研合同与技术 Spike（第 1-2 周）

目标：在写大规模业务代码前关闭所有会导致返工的未知项。

前端：

- 安装并验证受支持的 .NET SDK、Visual Studio/Build Tools、Windows App SDK；
- 建最小 WinUI 壳；
- 验证 x64 Debug/Release、MSIX 与 self-contained 测试包；
- Spike WebView2 本地资源、Plotly 2D/3D、静态图导出、PrintToPdf；
- Spike SharedBuffer + JSON fallback；
- Spike VirtualDataSheet 行虚拟化与二维虚拟化；
- 验证 DPI、High Contrast、软件 GPU/远程桌面退化行为。

后端：

- 收集真实输入，建立数据 profile；
- 写 Method Specification V1.0 草案；
- 独立实现一个最小 reference calculator/oracle；
- 形成 Golden Dataset 候选；
- Spike OpenXML streaming、legacy XLS、CSV encoding；
- Spike SQLite WAL/checkpoint、ZIP64 原子保存；
- 定义首版 Contracts 和 Error catalog。

QA/科研：

- 关闭 Q-SCI 阻断问题；
- 冻结 Intended Use、样本类型、仪器、采样与重复孔规则；
- 建立风险登记和需求追踪矩阵；
- 采购/获取必要 CLSI 标准访问权；
- 给真实 fixtures 做去标识化和使用授权。

产物：

```text
ADR-001 UI/runtime selection
ADR-002 Plot renderer and export limits
ADR-003 frontend-backend contract boundary
ADR-004 importer strategy
ADR-005 project format and workspace policy
Method Specification V1.0 draft
Data Contract V1.0
Contract fixtures v0
Spike benchmark/report
Risk register v0
```

退出门 G0：

- 所有 blocking Q-SCI 有书面答案；
- 至少 5 个代表性双通道文件可复现读取；
- Golden 至少覆盖正、负、各关键 Fail、边界；
- WinUI/Plotly/WebView2/PDF/SQLite/Import 的高风险能力均有可运行证据；
- 具体 stable patch、license、Runtime 分发策略已记录；
- 没有未回答的“技术选型靠猜测”问题。

### Phase 1 - 仓库基线、Contracts 与 Shell（第 3-4 周）

前端：

- Shell、NavigationView、TabView、三栏布局；
- 主题 token、全局错误/进度/取消模式；
- Fake Gateway 和所有黄金路径页面骨架；
- UI 状态模型与设计系统基础；
- PlotHost 独立构建和协议握手。

后端：

- Solution/项目边界、central package management、lock files；
- Domain ID/value objects、Result/Error、RunSnapshot；
- Application gateway adapter；
- Run state machine 和 optimistic revision；
- SQLite schema v1/migration runner；
- logging/correlation/audit 基础。

平台/QA：

- Windows CI restore/build/test；
- format/analyzer/architecture test；
- dependency/license/SBOM 骨架；
- contract fixture serialization roundtrip；
- workspace-local unique test temp policy。

退出门 G1：

- 前端可用 Fake 完整走通无计算的黄金路径；
- Fake/Real Gateway contract conformance 测试存在；
- Run 状态只能由 Application 修改；
- CI 在干净 Windows runner 可 locked restore/build/test；
- Contracts V1 冻结，破坏性改动需版本升级。

### Phase 2 - Analysis Core 与 Golden（第 5-6 周）

前端并行：

- Calibration/Control/Sample 页面按 fixtures 完成 Ready/Pass/Fail/Invalid；
- Calculation Trace 组件；
- 状态图标、可访问名称和失败行动建议。

后端：

- Method schema/canonical hash/snapshot；
- endpoint/window selection；
- Positive plateau/Fm；
- NTC per-well Fb/full-course Y QC/Run Fb；
- WT reference；
- LQC/Sample per-well dr' 与 replicate consistency；
- 完整 CalculationTrace；
- Analysis 用例和状态转换。

QA/科研：

- 单元测试覆盖每个公式和边界；
- 与独立 oracle differential test；
- Golden 全中间量比对；
- randomized/non-finite/empty/overflow/error-path tests；
- 算法代码由科学负责人和独立开发者双审。

退出门 G2：

- 所有 approved Golden 逐字段通过；
- exact threshold 和 exact RR 边界通过；
- 没有 UI/JS 公式副本；
- 每个结果可追到输入 PointId、Method hash 和 algorithm version；
- Analysis 项目 syntax/import/build/test 全通过。

### Phase 3 - Import、Mapping 与 Plate（第 7-8 周）

前端：

- 拖拽/文件选择；
- Sheet/区域/表头候选预览；
- Time/FAM/ROX 映射；
- validation issue 定位到源行列；
- 96 孔 Map、框选、批量分配、双向高亮；
- 大文件进度/取消/重试。

后端：

- XLSX streaming reader；
- CSV/TSV/TXT 显式 encoding/delimiter/culture；
- legacy XLS adapter 或基于证据移出 MVP；
- candidate region detection；
- duplicate/non-monotonic/missing/channel mismatch validation；
- source SHA-256；
- transactional import persistence；
- mapping/plate revision 和 audit。

QA：

- 真实 fixture + 合成 malformed fixture；
- 大文件/取消/重复导入；
- macro/external link/formula 不执行；
- ZIP bomb/row-cell limit；
- 键盘完成 mapping 和 plate；
- cwd independence 与相对路径验证。

退出门 G3：

- 至少 5 种真实布局和全部 import fixtures 可解释处理；
- 映射错误有精确定位和行动建议；
- 取消后无半成品 Run；
- 导入结果与源 hash/映射快照可保存重开；
- Plate 所有 cardinality/互斥规则由后端守护。

### Phase 4 - Guided Analysis 纵向闭环（第 9-10 周）

前端：

- Calibration、Control QC、Sample、Trace 页面接 Real Gateway；
- NTC 容许带、Positive 窗口、失败点定位；
- WT/LQC/Sample 表与 2D reference scatter；
- INVALID 阻断、重试/重开、旧响应丢弃；
- 结果摘要和 RUO 文案。

后端：

- import snapshot -> calibration -> control -> sample 完整编排；
- 每步 transactional save 和 audit；
- stale revision、重复命令幂等保护；
- 不可变 AnalysisSnapshot；
- query optimized calculation trace；
- Run invalidation 与重新开始策略。

QA/科研：

- 每个状态转换与禁止转换；
- Golden end-to-end；
- 失败后旧正式结果不可见；
- 同时修改 mapping/运行分析的竞争测试；
- 真实数据人工逐值复核。

退出门 G4：

- 一份代表性真实文件可从导入到 Research Result 全流程完成；
- 所有关键失败均阻止正式 classification；
- 前端显示值、Excel 准备值、数据库值与 trace 一致；
- 实验负责人完成第一次 end-to-end sign-off。

### Phase 5 - Project、Recovery 与 Report（第 11-12 周）

前端：

- Save/Open/Save As、unsaved 状态；
- crash recovery 入口；
- Report preview/export；
- export progress/error；
- Diagnostics 与 Support Bundle 选择界面。

后端：

- `.fluoaxisproj` manifest/package/hash/migration；
- workspace lifecycle、WAL checkpoint、atomic replace；
- recovery marker；
- immutable ReportSnapshot；
- encoded HTML/CSS 和 PDF；
- Excel/PNG/SVG/HTML export；
- support bundle redaction。

QA：

- save/open roundtrip 保持所有结果和 Method；
- 强杀进程/磁盘满/目标只读/路径过长；
- Zip Slip/bomb/hash mismatch/future version；
- PDF 渲染 + 文本抽取 + 中文字体 + 多页；
- Excel formula injection；
- 报告失败不影响 Run。

退出门 G5：

- 原子保存和恢复故障注入通过；
- `.fluoaxisproj` 重开后 Golden 结果不变；
- PDF 视觉和文本 QA 通过；
- 每种导出有 schema/hash/version；
- Support Bundle 默认不含原始实验数据。

### Phase 6 - Data Explorer、2D/3D 与 Formula（第 11-14 周，与 Phase 5 部分并行）

前端：

- VirtualDataSheet；
- 2D scatter/line/scatter+line；
- 3D scatter/line，surface 视 Phase 0 决策；
- axis/series/legend/camera；
- PointId 选择与二维/三维距离；
- Formula Builder UI；
- Moving Average/Savitzky-Golay “仅视觉”标识；
- plot/image/interactive HTML export。

后端：

- DerivedDataset 与 lineage；
- PlotSpec schema/version；
- exact PointId lookup/distance；
- safe tokenizer/Pratt parser/AST/semantic validator；
- formula cache 和资源限制；
- smoothing/LOD 的 display-only provenance；
- Plot protocol + shared buffer encoder。

QA：

- parser injection/深嵌套/除零/超大表达式；
- Plot protocol unknown/old/malformed message；
- 25k points 与连续 render 内存；
- camera roundtrip、click -> PointId；
- 2D SVG、scattergl PNG、3D PNG screenshot；
- Data Explorer 操作绝不改变 Guided 结果。

退出门 G6：

- Derived lineage 可追到 Raw；
- Formula 无 IO/反射/网络/进程能力；
- 平滑/LOD 不进入 AnalysisSnapshot；
- 3D 无 GPU 时有明确降级；
- 表格/绘图性能达到 Phase 0 冻结预算。

### Phase 7 - Hardening、安装与发布候选（第 15-16 周）

前端：

- DPI、High Contrast、Narrator、键盘全流程；
- loading/empty/error/cancel/retry 全覆盖；
- UI automation smoke；
- 安装/升级/卸载/Runtime 缺失体验。

后端/平台：

- migration/backward compatibility；
- log rotation/redaction；
- resource limits；
- deterministic Release build、sign、symbols、SBOM；
- dependency lock/license/vulnerability review；
- benchmark baseline 和回归阈值；
- disaster/recovery runbook。

QA：

- 100 次导入；
- 100 次 WebView create/destroy；
- 100 次 save/open；
- 内存增长、句柄、临时目录残留；
- offline install/run/export；
- clean machine smoke；
- 全追踪矩阵和 known anomalies。

退出门 G7：

- 0 个 blocker/critical 未关闭缺陷；
- 性能、稳定性、可访问性、安全门全部通过；
- release artifact 可在干净参考机离线安装和运行；
- Release Evidence Bundle 完整；
- RC 版本获得产品、科学、工程和 QA 四方批准。

### Phase 8 - 科研试点与 V1.0 发布（第 17-18 周）

- 使用预注册 pilot protocol 跑多批次、多日、多操作者数据；
- 软件结果与独立参考结果逐值对比；
- 记录导入失败、人工映射修改、QC fail、重复孔冲突和用户错误；
- 不在 pilot 中临时改阈值；需要改 Method 时新建版本并重新验证；
- 完成 usability observation 和支持日志评估；
- 关闭 pilot blocker，发布 `v1.0.0-research`；
- 建立 post-release issue triage、备份和升级沟通机制。

退出门 G8：

- Pilot protocol 完成，无未解释的结果差异；
- 所有产品声明与实际验证范围一致；
- 用户手册、安装手册、算法说明、验证报告、known issues 已交付；
- Release hash、签名、SBOM、依赖版本和 Method hash 可核验。

## 12. 双轨协作与日常节奏

### 12.1 工作流

```text
Requirement/Method change
 -> contract example and acceptance criteria
 -> frontend fixture + backend test vector
 -> parallel implementation
 -> contract conformance
 -> integration
 -> scientific/UX review
 -> release evidence
```

### 12.2 每周固定活动

| 活动 | 参与者 | 输出 |
|---|---|---|
| 周一 contract sync | FE/BE/QA/Product | 本周契约版本、fixture、阻断项 |
| 周中 vertical slice demo | FE/BE | Fake 与 Real 的同场行为 |
| Scientific review | Scientific/BE/QA | 算法例子、Golden、异常语义 |
| UX/accessibility review | FE/QA/Product | 页面状态、键盘、失败动作 |
| 周五 integration gate | 全体 | CI、风险、decision log、下周依赖 |

### 12.3 分支与 PR

- 建议首个提交建立 `main`；后续短生命周期 `feature/*`；
- 一个 PR 聚焦一个纵向能力或一个基础契约；
- Contract breaking change 独立 PR，先合 fixture/schema，再合 FE/BE；
- PR 必须链接 requirement、risk control、测试证据和截图/benchmark（适用时）；
- 禁止把大规模格式化、依赖升级和业务修改混在同一 PR；
- 禁止提交真实敏感数据、用户目录、全局临时路径和浮动依赖。

### 12.4 RACI

| 事项 | Product/Scientific | Backend | Frontend | QA | Quality/Regulatory |
|---|---|---|---|---|---|
| Intended Use/Method | A/R | C | I | C | C |
| Contracts | C | A/R | R | C | I |
| Analysis | A/C | R | I | R | C |
| UI/UX | C | I | A/R | R | C |
| Golden approval | A/R | R | I | R | C |
| Risk controls | C | R | R | R | A |
| Release | A | R | R | R | C |

`A` accountable，`R` responsible，`C` consulted，`I` informed。

## 13. 测试与验证矩阵

### 13.1 测试层级

| 层级 | 重点 | 运行频率 |
|---|---|---|
| Unit | 数学、parser、state transition、ViewModel | 每次提交 |
| Contract | DTO/schema/Fake-Real/Error/Plot protocol | 每次提交 |
| Golden | 全中间量和最终结果 | 每次提交/Release |
| Import integration | 真实与恶意 fixture、取消、编码 | 每次 PR |
| Persistence/project | migration、transaction、roundtrip、fault injection | 每次 PR/夜间 |
| PlotHost | render/react/click/camera/export/screenshot | 每次 PR |
| WinUI integration | gateway、WebView2、file picker、recovery | 每次 PR/夜间 |
| Accessibility | UIA、键盘、Narrator、contrast/DPI | 每 Sprint/RC |
| Performance | 启动、导入、表格、plot、内存、autosave | 夜间/RC |
| Security | archive/import/export/WebMessage/formula | 每次 PR/RC |
| Pilot | 多日/多批/多操作者/参考方法 | RC 前 |

### 13.2 数学测试重点

- `dr'=0.399999/0.4/0.400001`；
- `RR=0.009999/0.01/0.010001`；
- window 0/1/最小允许点；
- endpoint 容差内外；
- `Fm <= Fb` 和 denominator epsilon；
- NaN/Infinity/overflow；
- positive scale + offset 下 normalization 不变量；
- 输入顺序变化不应改变基于时间排序后的结果；
- 每孔调用一致但均值位于边界另一侧的防回归；
- save/open 前后 bitwise 或定义容差内一致。

### 13.3 性能预算

需求文档中的性能值先作为 Phase 0 测量目标，而不是已承诺 SLA：

| 场景 | 初始目标 | 测量条件 |
|---|---:|---|
| 冷启动可操作 | <= 4 s | 参考实验室 PC，Release，离线 |
| 热启动 | <= 2 s | 同上 |
| 96 x 121 x 2 XLSX 导入 | <= 2 s | 固定真实 fixture |
| 单 Run BRAF 分析 | <= 100 ms | 不含 UI/IO |
| 2D 25k 首次渲染 | <= 500 ms | 固定 viewport/GPU |
| 3D 25k 首次渲染 | <= 800 ms | 固定 camera/GPU |
| 交互 frame | 30-60 FPS | p95 同时报 frame time |
| 普通项目内存 | < 500 MB | 含 WebView2，steady state |
| Autosave 阻塞感知 | < 200 ms | UI thread blocking 单独测量 |

基准报告必须记录 CPU、RAM、GPU、OS、Runtime、App SDK、WebView2、Plotly、构建配置和 fixture hash。

### 13.4 发布证据包

```text
requirements-traceability.csv
method-specification.pdf
golden-dataset-manifest.json
golden-test-report.xml/html
integration-test-report
benchmark-report
accessibility-report
risk-register.xlsx/csv
known-anomalies.md
dependencies-and-licenses.json
sbom.spdx.json or cyclonedx.json
build-manifest.json
installer/signature/hash
release-notes.md
```

## 14. CI/CD 与依赖治理

### 14.1 CI 顺序

```text
restore --locked-mode
 -> format/analyzers/architecture tests
 -> build Release
 -> unit + contract
 -> golden
 -> import/persistence/project integration
 -> PlotHost tests
 -> report render/text smoke
 -> selected WinUI/WebView2 smoke
 -> dependency/license/SBOM
 -> benchmark comparison
 -> package/sign
 -> clean-machine smoke
 -> archive evidence
```

### 14.2 版本策略

- `global.json` 锁 SDK feature band/patch policy；
- `Directory.Packages.props` 集中 NuGet 版本；
- lock files 入库，Release 使用 locked mode；
- Plotly bundle 入库或受控制品库，保存版本/license/hash；
- WebView2 Evergreen 记录 qualification matrix，SharedBuffer feature detect；
- 禁止 Preview/Experimental；
- 每次依赖升级单独 PR，运行 Golden、视觉、PDF、性能和安装回归；
- 若 Phase 0 无法实时确认需求文档声称的 `.NET 10 + Windows App SDK 2.3.1` 兼容性，则选择当日已验证 Stable 组合并记录 ADR。

### 14.3 环境基线

新开发机 bootstrap 必须可脚本化检查：

```text
Windows edition/build
Visual Studio/Build Tools workload
.NET SDK version
Windows App SDK package
WebView2 Runtime
Node/pnpm version for PlotHost
code signing availability
long path setting
GPU/WebGL capability
```

检查脚本只报告，不擅自下载/升级系统组件。安装动作由团队按批准版本执行。

## 15. 风险登记

| ID | 风险 | 概率 | 影响 | 缓解 | 触发/降级 |
|---|---|---:|---:|---|---|
| R-001 | 采样合同不一致 | 高 | 极高 | Phase 0 签字冻结 | 未关闭则禁止 G0 |
| R-002 | 缺少真实双通道数据 | 高 | 极高 | 收集 5+ fixtures | 只能做 synthetic Spike |
| R-003 | `0.4/0.1%` 缺独立验证 | 中高 | 极高 | CLSI 风格验证与参考方法 | 保持 RUO，不扩大声明 |
| R-004 | WinUI/App SDK 具体组合不稳定 | 中 | 高 | 最小 Spike、锁 patch、ADR | 回退已验证 stable；不追 Preview |
| R-005 | VirtualDataSheet 成本失控 | 中高 | 高 | 独立 Spike/预算/可访问性 | MVP 限定典型规模，Explorer 延后 |
| R-006 | WebView2 Evergreen 漂移 | 中 | 高 | qualification matrix + smoke | 必要时评估 Fixed Version |
| R-007 | WebGL/RDP/GPU 不可用 | 中 | 中高 | capability detection + 2D/native summary | 禁用 3D并可解释提示 |
| R-008 | WebGL 图误承诺 SVG | 中 | 中 | 明确 trace 能力 | raster 高 DPI export |
| R-009 | importer 遇到仪器变体 | 高 | 高 | fixture corpus + mapping confirmation | 新 adapter，不放宽静默猜测 |
| R-010 | Method 变更破坏历史复现 | 中 | 极高 | immutable snapshot/hash/version | 旧 Method 只读兼容 |
| R-011 | 工程损坏/部分保存 | 中 | 极高 | workspace + atomic package + recovery | 保留上一有效包 |
| R-012 | UI 与算法双真源 | 中 | 极高 | Contracts/trace/静态门禁 | PR 阻断公式副本 |
| R-013 | Data Explorer 污染正式结果 | 中 | 极高 | lineage + separate snapshots | Guided 只读 Raw |
| R-014 | 项目宣称滑向临床用途 | 中 | 极高 | Intended Use/RUO/review | 冻结发布文案，另立法规项目 |
| R-015 | 实际团队小于排期假设 | 高 | 高 | 优先 Guided vertical slice | Explorer/ARM64/surface 后移 |

## 16. Definition of Ready / Done

### 16.1 Feature Ready

```text
[ ] requirement ID and user outcome
[ ] success/failure/cancel/empty examples
[ ] contract fixture
[ ] scientific rule approved if applicable
[ ] error codes and recommended actions
[ ] security/privacy impact
[ ] accessibility behavior
[ ] test plan and performance impact
[ ] migration/backward-compatibility impact
[ ] FE/BE dependency owners
```

### 16.2 Feature Done

```text
[ ] implementation merged
[ ] unit/contract/error-path tests
[ ] cancellation and stale response handled
[ ] no UI-thread blocking
[ ] no formula in ViewModel/JS
[ ] no raw-data mutation
[ ] save/open roundtrip if persistent
[ ] audit/logging without data leakage
[ ] keyboard/AutomationName/high contrast/DPI
[ ] security checks for new boundary
[ ] docs/ADR/traceability updated
[ ] benchmark note
[ ] reviewer comments resolved
```

### 16.3 Release Done

```text
[ ] approved Method and Intended Use
[ ] Golden and independent reference agreement
[ ] all stage gates G0-G8
[ ] clean-machine offline install
[ ] migration/recovery/fault injection
[ ] accessibility review
[ ] threat model and risk controls reviewed
[ ] dependency/license/SBOM complete
[ ] no blocker/critical defect
[ ] known anomalies accepted
[ ] signed artifact + hashes + evidence bundle
```

## 17. 外部资料与计划影响

以下来源在 2026-08-10 通过 Web 检索结果核验了标题、范围或最新页面信号；具体付费标准正文需团队合法获取。Microsoft Learn 的直接页面访问在本次研究中被浏览器安全策略拒绝，相关版本信息来自需求基线，必须在 Phase 0 重新 live-check，不能直接作为永远有效的事实。

| 来源 | 核验到的重点 | 对本计划的影响 |
|---|---|---|
| [21 CFR 809.10](https://www.ecfr.gov/current/title-21/chapter-I/subchapter-H/part-809/subpart-B/section-809.10) | 实验研究阶段且不表述为有效 IVD 时，RUO 标签有明确边界 | 强制 RUO 声明；不能以科研标签掩盖临床营销 |
| [FDA IVD Labeling](https://www.fda.gov/medical-devices/device-labeling/in-vitro-diagnostic-device-labeling-requirements) | IVD 标签需说明 intended use、限制和性能等 | Intended Use 与限制必须成为发布受控文档 |
| [NMPA/CMDE 医疗器械软件注册审查指导原则 2022 年第 9 号](https://www.cncsdr.org/ggtz/ggzz/202203/t20220311_304189.html) | 面向医疗器械软件生存周期和申报资料，也供质量体系核查参考 | 科研版提前保留生命周期、追踪、风险和版本证据，但不声称注册合规 |
| [IEC 62304:2006](https://www.iso.org/standard/38421.html) | 定义医疗器械软件生命周期要求，覆盖开发和维护 | 采用需求、架构、实现、验证、维护证据链 |
| [ISO 14971:2019](https://www.iso.org/standard/72704.html) | 覆盖包括软件和 IVD 在内的风险管理过程 | 从导入、计算、报告、WebView2 到保存建立风险控制 |
| [CLSI EP12 Plus](https://clsi.org/shop/standards/ep12-plus/) | 定性/二元输出检测性能评估框架 | 阳性/阴性输出需独立的定性性能验证，不以软件单测代替 |
| [CLSI EP17](https://clsi.org/shop/standards/ep17/) | 检测能力声明的评估/验证 | `0.1%` 低丰度主张需要 LoB/LoD/检出能力证据与统计方案 |
| [CLSI nucleic acid validation/MM17](https://clsi.org/resources/insights-blog/nucleic-acid-testing-validation-and-verification/) | 支持核酸检测开发、验证、核查和质量控制 | Pilot/Golden 之外需要实验方法验证工作流 |
| [FDA cleared/approved companion diagnostic list](https://www.fda.gov/medical-devices/in-vitro-diagnostics/list-cleared-or-approved-companion-diagnostic-devices-in-vitro-and-imaging-tools) | BRAF companion diagnostics 与特定 assay/indication 绑定 | 本项目不得借其他已批准 assay 推导临床有效性 |
| [FDA Device Software Premarket Guidance](https://www.fda.gov/regulatory-information/search-fda-guidance-documents/content-premarket-submissions-device-software-functions) | 推荐医疗器械软件提交文档和证据 | 用作未来产品化证据结构参考，不是科研版认证声明 |
| [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) | 需求基线声称 .NET 10 为 LTS | Phase 0 再核验支持期与 patch 并锁定 |
| [Windows App SDK Downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads) | 需求基线声称 2.3.1 Stable | Phase 0 必须 live-check compatible Stable 组合 |
| [Open XML large spreadsheet](https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/how-to-parse-and-read-a-large-spreadsheet) | SAX/streaming 适合大工作表 | XLSX adapter 采用 streaming，不加载全 DOM |
| [WebView2 Security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security) | Web content/host bridge 需要最小权限和来源校验 | 本地资源、CSP、origin/message 验证、无 host objects |
| [Plotly WebGL vs SVG](https://plotly.com/javascript/webgl-vs-svg/) | SVG 与 WebGL 是不同渲染路径 | 只有 SVG trace 承诺矢量导出，WebGL/3D 用 PNG |
| [Plotly static export](https://plotly.com/javascript/static-image-export/) | 支持静态图片导出 | Phase 0 实测 PNG/SVG/PDF 嵌入质量与异常路径 |

这些资料不证明本项目的 `dr'=0.4`、NTC/WT 阈值或 0.1% LQC 科学有效；它们只决定验证方法、声明边界和工程证据要求。

## 18. 首批可直接进入任务系统的 Epic

| Epic | 标题 | 前置 | 完成门 |
|---|---|---|---|
| E-00 | Scientific/Data Contract Freeze | 原始资料/负责人 | G0 科研部分 |
| E-01 | Toolchain and Architecture Spikes | 参考机 | ADR + benchmark |
| E-02 | Repository/CI/Dependency Baseline | E-01 | clean build |
| E-03 | Contracts/Fake Gateway/Run State | E-00 | G1 |
| E-04 | Analysis Core/Golden/Trace | E-00,E-03 | G2 |
| E-05 | Import/Mapping/Plate | E-03 | G3 |
| E-06 | Guided Analysis Vertical Slice | E-04,E-05 | G4 |
| E-07 | Project/Recovery/Migration | E-03 | G5 storage |
| E-08 | Reporting/Export | E-06,E-07 | G5 report |
| E-09 | VirtualDataSheet | E-01,E-03 | performance/accessibility |
| E-10 | PlotHost/2D/3D | E-01,E-03 | plot protocol tests |
| E-11 | Formula/Derived/Lineage | E-04,E-09 | G6 |
| E-12 | Security/Threat Model | 全程 | risks closed |
| E-13 | Accessibility/Usability | 全程 | G7 |
| E-14 | Installer/Diagnostics/Support | E-07 | clean-machine smoke |
| E-15 | Pilot/Release Evidence | G7 | G8 |

## 19. 前 10 个执行动作

1. 由产品负责人确认本计划的产品边界、团队规模和 18 周假设。
2. 收集 V1.0 引用但当前缺失的意见汇总、原理 PDF/PPT 和真实双通道 XLSX。
3. 召开 90 分钟 scientific contract workshop，逐项关闭 Q-SCI-001 至 Q-SCI-012。
4. 建立 `docs/method/Method-Specification-V1.0.md` 和 `Data-Contract-V1.0.md`，由实验负责人签字冻结。
5. 在参考 Windows 11 x64 机器安装并记录 Stable SDK/toolchain，提交 ADR-001。
6. 建立仓库首个提交：solution skeleton、CI、dependency locking、测试临时目录政策。
7. 同时启动四个 Spike：Importer、WebView2/Plotly/PDF、VirtualDataSheet、SQLite/project package。
8. 产出 Contracts v0 和一套成功/失败 JSON fixtures，前端建立 Fake Gateway。
9. 建立独立 reference calculator 和第一批 Golden candidates，完成双人逐值核对。
10. 仅在 G0 通过后，把 Phase 1-4 工作拆入任务系统并承诺日期。

## 20. 计划维护规则

- 本文是实施主计划；需求变化先改 Method/Data Contract，再改本计划和 backlog；
- 每个 Gate 关闭时追加证据链接、日期、批准人和已知风险；
- 任何修改科学结果的变更都创建新 Method/algorithm version，不覆盖旧定义；
- 具体依赖版本只写入 `global.json`、`Directory.Packages.props` 和 `DEPENDENCIES.md`，本文只记录选择原则；
- 阶段延期时保留失败原因和已尝试方案，不擦除历史；
- 若未来启动临床产品化，应新建独立法规项目计划，在范围、QMS、风险分类和验证方案获得批准后再复用本项目证据。
