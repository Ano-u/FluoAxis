# BRAF V600E Research Method V1.1 — 分通道 Fm 草案

状态：implementation draft / 未签字；2026-10-10。变更授权来自用户：平台均值逻辑只适用于 FAM，ROX 的 Fm 直接取 120 min 终点荧光值。科研工程验证不代表方法已获得科学批准。

除本节列明的 Fm 规则外，沿用 `docs/method/Method-Specification-V1.0.md` 的端点选择、NTC、WT、LQC、样本判读、重复孔与数值保护。输入仍为不可变 Raw RFU，双通道共用已验证时间轴。

| 对照与通道 | 使用数据 | 单孔 Fm / 质控 | Run 级汇总 |
|---|---|---|---|
| 100% Positive · FAM | 90–120 min 闭区间的全部实际点 | RR=(max−min)/mean；RR≤0.01，点数≥2、均值有限且>0；Fm=窗口均值 | 全部必需阳性孔通过后，mean(Fm_i,FAM) |
| 100% Positive · ROX | 120 min 实际端点 | Fm=该端点 Raw RFU；不计算 ROX 平台 RR，不用窗口均值，也不进行 ROX 平台 QC | 按同一批阳性孔 mean(Fm_i,ROX) |

每孔 FAM/ROX 必须具有合法端点。工程端点容差保持 ±15 秒闭区间，取与 120 min 最近的实际点，等距取较晚点，不插值。正好有 120 min 点时优先取它。记录实际采样时间和源行列，不能把 119.75/120.25 min 样点的时间写成 120 min。

示例：阳性孔在 90、120 min 的 FAM 为 998、1002，ROX 为 100、600，则 FAM Fm=1000、RR=0.004（0.4%），ROX Fm=600。ROX 的大幅变化不导致平台失败。如果第二个阳性孔的 ROX 端点为 800，则 Run Fm_ROX=700；不能先把各孔时间窗口混在一起求均值。

顺序仍为：验证输入/端点 → FAM 平台与 ROX 端点 Fm → Run Fm → 每个 NTC 使用自己的端点 Fb 做全程归一化质控 → Run Fb → WT → LQC → 样本。任一必需 FAM 平台失败则本轮校准失败，不剔除该阳性孔后继续。ROX 缺端点、非有限值、Run 均值溢出或归一化分母 `Fm_ROX−Fb_ROX≤0`/非有限仍失败关闭；不添加未获授权的 epsilon。

## 版本与旧工程

- 新建 Run 默认 `BRAF-V600E-Research` / `1.1`，算法 `BRAF-ENGINE-1.1`。所有既有数值参数保持不变；版本已参与 canonical JSON/hash，因此无需改 SQLite/archive schema。
- 显式 `1.0` 快照继续执行双通道平台均值和平台 QC，canonical hash 不变，保留旧算法标识。打开旧工程显示保存结果，不迁移方法、不静默重算。
- 需要用新规则复核旧文件时，在新版使用“从此文件重新设置”建立新 Run；不能覆盖旧结果。
- 未知方法版本保留读取能力，但三个 Analysis 入口返回 `METHOD_VERSION_UNSUPPORTED`，不猜测其语义。

## 可追溯证据

1.1 阳性孔证据增加可选 `fmSource`：FAM=`plateauMean`、ROX=`endpoint`。FAM 保存真实平台窗口、RR 和均值；ROX `Window=null`，仅保存真实端点与 Fm，不伪造平台 PASS/RR=0。ROX 的 PASS 仅表示本孔端点取值完成，不替代 Run 校准结论。字段未出现在旧记录时保持 null 且不序列化，不补造历史来源。

UI 和 HTML/PDF/XLSX 只读取后端冻结证据；不重新计算 Fm。1.0 Golden candidate 保留原 hash/期望，新 1.1 candidate 独立登记；二者仍 `candidate-unapproved`，双批准和真实仪器资格未关闭。
