---
id: FT-20261010-002
kind: feature
status: validated
area: method-calibration
stage: versioned-fm-calculation
tasks: [fam-plateau-rox-endpoint, legacy-reproduction, scientific-evidence, user-interface, candidate-validation]
priority: high
branch: main
pr: null
paired_doc: null
paths: [src/FluoAxis.Domain/, src/FluoAxis.Analysis/, src/FluoAxis.Application/ScientificEvidenceCapture.cs, src/FluoAxis.Contracts/ScientificEvidenceDto.cs, src/FluoAxis.Reporting/ReportTables.cs, src/FluoAxis.App/, tests/, samples/golden/, docs/method/, docs/design/, README.md, plan.md, BRAF_V600E_科研判读软件_需求与现代化实现架构_V1.0.md, .github/workflows/ci.yml, docs/change_index.jsonl]
contracts: [method-1.1-draft, fam-plateau-mean, rox-endpoint-fm, method-1.0-reproduction]
hotspots: [endpoint-tolerance, control-replicates, denominator, canonical-hash, scientific-evidence, golden-version]
bug:
  repro: "User specifies that Positive100 plateau QC/window mean apply to FAM only; ROX Fm must use the 120 min endpoint."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [channel-specific-fm, missing-endpoint, inclusive-tolerance, denominator-failure, replicate-mean, legacy-canonical-hash, independent-oracle, evidence-roundtrip, native-smoke, path-policy]
  tests_run: ["Analysis 95/95", "Independent Golden 34/34", "Contracts 77/77", "Persistence 32/32", "Reports 33/33", "Infrastructure 97/97", "Final solution 1010/1010, 0 failed/skipped, 33.157 s", "Final Release native UIA 15 steps passed", "Actual exported FAM=1000 ROX=600 and endpoint provenance passed", "Path and scientific boundary passed"]
  benchmarks_run: ["Final Release empty startup 488.9728 ms, single observation, not SLA"]
search:
  aliases: [ROX Fm endpoint, FAM plateau, Method 1.1, 120 min]
---

# 分通道计算 Fm：FAM 平台均值、ROX 端点

## Summary

分类 feature：用户明确要求改变既有方法行为。新建运行使用 Method 1.1 草案；用户此前保留未签字草案的决定继续有效。本实现授权不等于科学/临床批准。

最新 EXE：`artifacts/release/FluoAxis-research-win-x64-20261010-method11-final/FluoAxis.App.exe`。对应未签名 MSIX：`artifacts/release/FluoAxis-msix-20261010-method11/FluoAxis.Research_1.0.0.0_x64_unsigned.msix`。基于实际 main / 1fd01d9 开发，没有重写历史。

## Plan

1. 用已持久化 Method Version 区分 1.0 双通道平台与 1.1 FAM 平台/ROX 端点，保持 1.0 canonical JSON/hash。
2. ROX 读取同孔同通道实际端点，不调用平台 QC；所有阳性孔仍必须存在合法端点。多孔先独立取值再求均值。
3. 追溯记录真实来源，ROX 不伪造平台 PASS/RR/窗口；界面及报告呈现已保存值。
4. 保留 V1.0 candidate manifest，新增 V1.1 独立 oracle 候选；测试失效边界与保存重开。

## Exploration log

- `BrafAnalysisEngine.Calibrate` 在双通道循环中无条件 SelectWindow/EvaluatePlateau，导致 ROX 被错误套用用户仅针对 FAM 的规则。
- Method snapshot 已把 Version 写入 canonical hash，数据库/工程包重开显式恢复版本；无需改存储 schema 或重写旧快照。
- PositiveWellResult、ScientificEvidenceCapture 目前假定每个阳性通道都有平台；需显式表示端点来源，不能伪造 RR=0 平台通过。
- contract concern 已委派只读审查；引擎/证据由主代理实现，独立参考候选按版本分开。
- 首版新增测试用不同 FAM/ROX 时间轴构造“ROX 只有一个点”，被 WellSignals 输入合同拒绝。该输入规则不在用户变更范围，保留时间轴校验，改测“ROX 只消费一个端点的证据”；成对数据只有一个端点时仍因 FAM 平台不足失败。
- 首次 solution build 在 Golden 子任务尚未写入新 manifest 时失败；资源落盘后重新构建通过。最终一次 build 0 errors，有既有测试命名/分析器 warnings，未把失败构建当成成功。
- 复核发现硬编码120/90–120的来源标签对自定义端点/窗口不准确，最终 UI/report 使用“所选端点/合格平台均值”，实际时间来自已保存 Endpoint/Window。非阳性角色显示不适用。已加旧算法身份断言与 JSON 空字段大小写无关检查。

## Root cause

原 1.0 草案明确两通道同规则；本次用户修订为通道特异规则。保留旧算法执行路径以避免改变历史结果含义。

## Implementation

- 方法：DefaultVersion=1.1；显式 LegacyVersion=1.0；canonical writer、SQLite 和 archive schema 保持。1.0 复算保留双平台路径与 BRAF-ENGINE-1.0，1.1 为 BRAF-ENGINE-1.1。未知版本可保存/读取但分析拒绝。
- 引擎：1.1 ROX 直接取已选择端点 Raw RFU；PositiveWellResult.Plateau=null、窗口点为空。FAM 原有窗口均值/RR/点数规则保留，所有必需孔均参与，多孔 Fm 顺序均值后进入原 NTC/WT/LQC/样本计算。
- 合同与证据：可选 FmSource=plateauMean/endpoint，null不序列化。ROX Endpoint保存真实时间/行列，Window=null且不伪造RR；FAM保留实际平台统计。旧记录不补造来源。
- 前端与报告：MainWindow 校准说明与证据详情显示来源，ReportTables 的 StageWells 增加 Fm 来源；只渲染冻结结果，不增加界面公式。
- 测试/文档：新增版本化 Golden 候选、7项 Analysis、旧hash/新旧SQLite往返、可选DTO兼容和报告来源回归；CI及README最小发现数更新1010。Method1.0文件只加历史说明，1.1规则另存新文件；更新需求说明和用户指南。

## Validation

```powershell
dotnet build FluoAxis.slnx --no-restore --verbosity quiet
dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 1010
./build/Publish-ResearchBuild.ps1 -OutputDirectory artifacts/release/FluoAxis-research-win-x64-20261010-method11-final
./build/Invoke-DesktopAcceptance.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261010-method11-final -SourceEntry artifacts/ui-evidence/method11/rox-endpoint.csv -EvidenceDirectory artifacts/desktop-validation/20261010-method11-final
python artifacts/ui-evidence/method11/check_report.py artifacts/desktop-validation/20261010-method11-final
./build/New-UnsignedMsix.ps1 -PublishDirectory artifacts/release/FluoAxis-research-win-x64-20261010-method11-final -OutputDirectory artifacts/release/FluoAxis-msix-20261010-method11
```

全套最终1010/1010，0失败/跳过，33.157 s。Focused结果见front matter。独立Golden两版各17项；1.1含11案例ID、128组随机全链路，1.0旧512组继续保留。旧canonical hash固定 `861f7304b0183e591a997a2e53881866119d9a9938ae13f5e1c2e7900ef80bd9`，新hash `805b30d9d2631a1eff9ca078ddc57fe8638552d01789482e92b77182f03a4649`。

实际桌面导入合成源：SHA-256 `11091274be17cebfc03eabda4dbeecd0a3f7c5dd1b55af56d64fdb115f0e049e`。阳性FAM 90/120min为998/1002，ROX为100/600；UIA 15步（含3图形格式、3报告格式、工程包及重启）全部通过。原生导出HTML数值核对确认 FAM Fm=1000（区别于endpoint1002）、ROX Fm=600（区别于window mean350），来源为所选端点，算法1.1，见 `fm-check.json`。未用直接写文件代替picker流程。

最终发布在独立空工作区、不同cwd启动通过488.9728 ms，DPI/10关键资产通过，549文件发布清单匹配。15步UIA含驱动等待34,170.4 ms。此为本机单次观察，未做性能优化或新的SLA声称；ROX不再分配平台窗口数组。未引入worker/cache，内存上限继续沿用当前Run/点数预算。MSIX 554文件MakeAppx校验/解包hash通过，98,744,085字节，SHA-256 `21d1a74efffd7553c0bbca5e4efd8d2161856ee4f396d3637d6ddaa32ce1b3a7`，无签名、不视为正式发布。

路径门禁437文本及科学边界34展示文件通过；测试目录带类名/进程/GUID或唯一任务目录，SQLite单元测试用GUID内存库。源码无绝对路径，正式算法仍唯一位于Analysis。新工程包跨工作区重开、旧canonical和方法存取回归通过。

## Review outcomes

独立 contract reviewer 审查通过，无未解决阻断项；Golden concern独立实现/核对通过。采纳来源标签、旧algorithm id、空JSON属性断言建议。人工复核范围包括方法合同、有限数值/分母、版本兼容、Raw只读、证据、路径/内存、UI与文档。未创建GitHub PR或取得外部GitHub review；本变更无强制外部批准配置。

## Follow-ups

Method 与 Golden 仍需科学/工程双批准；保留 120 min±15 s 最近实际点、等距较晚、无插值的工程规则，其余阈值和控制身份不变。
