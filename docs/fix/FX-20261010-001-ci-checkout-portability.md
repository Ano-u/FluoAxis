---
id: FX-20261010-001
kind: fix
status: validated
area: ci-portability
stage: test-and-checkout
tasks: [deterministic-timestamp-fixture, pinned-renderer-bytes, fresh-checkout-validation]
priority: high
branch: main
pr: null
paired_doc: null
paths: [.gitattributes, tests/FluoAxis.Infrastructure.Tests/SafeDiagnosticsTests.cs, tests/FluoAxis.Visualization.Tests/OfflinePlotDocumentTests.cs, docs/change_index.jsonl]
contracts: [utc-diagnostic-timestamp, exact-pinned-renderer-sha256]
hotspots: [runner-timezone, git-autocrlf, immutable-vendor-asset, temporary-paths]
bug:
  repro: "GitHub Windows CI on d7aee32 reports 979/981: expected diagnostic exception absent and OFFLINE_PLOT_ASSET_HASH_MISMATCH."
  root_cause_status: confirmed
  root_cause_confidence: high
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned: [explicit-utc-offsets, git-autocrlf-checkout-reproduction, trusted-renderer-tamper-rejection, focused-suites, full-981-suite, path-policy, pushed-ci]
  tests_run: ["Infrastructure: 97/97 passed", "Visualization: 16/16 passed", "Full suite: 981/981 passed, 0 failed/skipped, 26.063 s", "Fresh checkout autocrlf=true/false: 4,293,347 bytes and pinned SHA-256 in both", "Repository path policy: passed", "Scientific source boundary: passed", "git diff --check: passed"]
  benchmarks_run: []
search:
  aliases: [UTC CI, DateTimeOffset.Now, OFFLINE_PLOT_ASSET_HASH_MISMATCH, CRLF Plotly]
---

# 修复 UTC runner 与 Git 换行转换导致的 CI 失败

## Summary

分类 fix。基于 main / d7aee32 的干净工作区处理用户提供的 GitHub CI 失败。保持诊断契约和严格固定资源 hash，不修改科学算法、UI、Method 或依赖版本。

## Plan

1. 以显式非零偏移替代测试对当前主机时区的假设。
2. 用 Git 属性保留 Plotly 精确字节，在新检出中复现并验证不同 autocrlf 设置。
3. 验证真实固定资源遭篡改仍拒绝，运行 focused/full suite 和路径门禁；提交推送并核对远程 CI。

## Exploration log

- 失败行 SafeDiagnosticsTests:92 首先检查 DateTimeOffset.Now；构造函数仅拒绝非零 Offset。在 UTC runner 上 Now.Offset 为零，合法输入不会抛错。最初从测试名称推测字段校验失效，检查实际失败行后排除该假设。
- Git 未定义 .gitattributes；本地 core.autocrlf=true，但原始 Plotly 工作树与 Git blob 都为 LF。现有未重新检出的文件掩盖了新 clone 的换行变化。
- Git blob / 工作区均为 4,293,347 字节，SHA-256 `14461f3b4c91c8bb590a99d6d03c3fd031ca40eec07ebab79a5e3eac107cd7ca`，与应用固定值一致。仅转 CRLF 后为 4,296,948 字节，hash `6fd63cbb154ddf288a851df1f2277c7b2c064a35e10898e15f8453995ade94c4`。因此不更换依赖、不扩展 hash 接受列表。
- 已委派只读 CI portability concern review；主代理负责实现和真实检出验证。

## Root cause

一处测试把本地时间等同于非 UTC；一处依赖资源的字节锁定没有对应 Git 检出属性。均是主机环境差异暴露，不能通过忽略测试或放宽资源校验解决。

## Implementation

- `tests/FluoAxis.Infrastructure.Tests/SafeDiagnosticsTests.cs` 不再使用本机 `DateTimeOffset.Now` 作为“必须失败”的输入；改用明确的 -08:00、-01:00、+01:00、+08:00 偏移，且保留 UTC 合法路径覆盖。生产 `SafeDiagnosticEvent` 不变。
- 新增根目录 `.gitattributes`，仅对 `web/FluoAxis.PlotHost/vendor/plotly.min.js` 使用 `-text`。这样 CI 或开发者在任意 `core.autocrlf` 设置下都得到固定 Plotly 字节；`OfflinePlotDocument.PlotlySha256` 未放宽或变更。
- `OfflinePlotDocumentTests` 将 renderer 读取集中到 helper，并增加真实 Plotly 最后字节篡改后必须抛出 `OFFLINE_PLOT_ASSET_HASH_MISMATCH` 的回归。

## Validation

用户粘贴的 GitHub Windows runner 日志原始失败为 979/981：

- `SafeDiagnosticsTests.Structured_event_rejects_free_form_or_path_carrying_fields` 在 UTC runner 上未抛异常，根因是 `DateTimeOffset.Now.Offset == TimeSpan.Zero`，不是结构字段校验失效。
- `OfflinePlotDocumentTests.OfflineDocumentBindsPinnedRendererAndEscapesEmbeddedData` 抛 `OFFLINE_PLOT_ASSET_HASH_MISMATCH`；本地 Git checkout 使用 `core.autocrlf=true`，但在修复前没有属性保证二进制文本资产保留字节。

修复后执行：

```powershell
dotnet test tests/FluoAxis.Infrastructure.Tests/FluoAxis.Infrastructure.Tests.csproj --no-restore --verbosity quiet
dotnet test tests/FluoAxis.Visualization.Tests/FluoAxis.Visualization.Tests.csproj --no-restore --verbosity quiet
dotnet test --test-modules '**/bin/Debug/net10.0*/**/*Tests.dll' --root-directory . --minimum-expected-tests 981
./build/Verify-RepositoryPolicy.ps1
./build/Verify-ScientificSourceBoundary.ps1
git diff --check
```

Focused suites分别通过 97/97 与 16/16；全套 981/981 通过，0 失败、0 跳过，26.063 秒。临时 Git seed 使用相同 `.gitattributes`，在 `core.autocrlf=true` 和 `false` 两次 clone 中均得到 4,293,347 字节、`14461f3b4c91c8bb590a99d6d03c3fd031ca40eec07ebab79a5e3eac107cd7ca`。该验证目录在 ignored `artifacts/test-tmp` 下，未进入提交。

## Review outcomes

人工 contract/runtime/data-path/tests/docs/review 检查通过。独立只读 portability review 确认两个根因和最小修复方向；未使用 GitHub/Codex 外部 reviewer，仓库没有强制外部 review 配置。未修改生产算法、Method、UI、依赖版本或资源内容。

## Follow-ups

本修复已解决用户粘贴的 CI 失败；正式签名、干净机安装和科学批准沿用既有未完成门禁。本修复不声称推进科学资格。推送后应观察 GitHub Actions 的新 run，确认 Windows runner 从全新 checkout 通过。
