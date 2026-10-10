namespace FluoAxis.Reporting;

using System.Globalization;
using FluoAxis.Contracts;

internal readonly record struct ReportCell(string? Text, double? Number)
{
    internal static ReportCell FromText(string? value) => new(value ?? "未保存", null);
    internal static ReportCell FromNumber(double? value) => value is { } number ? new(null, number) : FromText("未保存");
    internal string Display => Number?.ToString("G17", CultureInfo.InvariantCulture) ?? Text ?? string.Empty;
}

internal sealed record ReportTable(string Name, IReadOnlyList<string> Columns, IReadOnlyList<ReportCell[]> Rows);

internal static class ReportTables
{
    internal const string IncompleteNotice = "来源完整性以各阶段和 trace 的已保存标记为准。storedPartial 表示部分证据，unavailable 表示缺少证据；storedComplete 表示工程来源字段完整，不表示方法已获科学批准。历史缺项明确保留，不重算或补造。";

    internal static IReadOnlyList<ReportTable> Create(ReportSnapshotDto snapshot)
    {
        ReportContentDto content = snapshot.Content;
        RunEvidenceDto evidence = content.Evidence;
        RunSnapshotDto run = evidence.Snapshot;
        var tables = new List<ReportTable>();
        tables.Add(new("Overview", ["字段", "已存储值 / 报告元数据"],
        [
            Row("报告", "FluoAxis 科研技术报告 · DRAFT / 未批准"),
            Row("科研用途", content.ResearchUseNotice), Row("报告编号", snapshot.ReportId),
            Row("快照 SHA-256", snapshot.SnapshotSha256), Row("创建时间 UTC", content.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)),
            Row("软件版本", content.SoftwareVersion), Row("项目", evidence.ProjectName),
            Row("Run ID", run.RunId.ToString("D")), Row("Run revision", run.Revision.ToString(CultureInfo.InvariantCulture)),
            Row("Run 状态", run.DomainState == RunStateNames.RunInvalid ? "INVALID / runInvalid" : run.DomainState), Row("Method", $"{run.MethodId} v{run.MethodVersion}"),
            Row("Method SHA-256", run.MethodSnapshotHash),
            Row("算法版本（已存储 trace）", evidence.Traces.Count == 0 ? "未保存" : string.Join(", ", evidence.Traces.Select(trace => trace.Algorithm).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))),
            Row("证据完整性", evidence.ProvenanceCompleteness), Row("完整性说明", IncompleteNotice),
            Row("结果使用", IsRunValid(evidence) ? "显示后端已保存的分类；未重新判读或计算。" : "Run 未提供有效分析结果；不输出正式阳性 / 阴性结论。"),
        ]));
        tables.Add(new("Sources", ["源文件显示名", "项目相对资源", "SHA-256", "字节数"],
            [.. run.SourceFiles.Select(source => Row(source.OriginalFileName, source.RelativeResourcePath, source.Sha256, source.FileSizeBytes.ToString(CultureInfo.InvariantCulture)))]));
        tables.Add(new("Mapping", ["通道", "工作表 / 区域", "表头行（0起）", "结束行（0起）", "时间列（0起）", "信号列（0起）", "孔位"], MappingRows(evidence.Mapping)));
        tables.Add(new("ImportOptions", ["编码", "分隔符", "小数格式", "时间单位"], evidence.Mapping?.Options is { } options
            ? [Row(options.EncodingName, options.Delimiter, options.DecimalCulture, options.TimeUnit)] : []));
        tables.Add(new("Plate", ["孔位", "角色", "Sample ID", "Group", "Replicate", "Target", "Notes"],
            [.. evidence.Assignments.Select(assignment => Row(assignment.Well, assignment.Role, assignment.SampleId,
                assignment.Group, assignment.ReplicateIndex?.ToString(CultureInfo.InvariantCulture), assignment.Target, assignment.Notes))]));
        CalibrationResultDto? cal = evidence.Calibration;
        tables.Add(new("Calibration", ["状态", "Fm FAM", "Fm ROX", "Fb FAM", "Fb ROX"],
            cal is null ? [] : [[T(cal.Status), N(cal.RunFmFam), N(cal.RunFmRox), N(cal.RunFbFam), N(cal.RunFbRox)]]));
        tables.Add(new("CalibrationIssues", ["孔位", "通道", "错误码", "说明", "已存储数值"],
            [.. (cal?.PerWellDiagnostics ?? []).Select(issue => new[] { T(issue.Well), T(issue.Channel), T(issue.Code), T(issue.Message), N(issue.Value) })]));
        ControlQcResultDto? control = evidence.Control;
        tables.Add(new("Controls", ["状态", "WT Y FAM", "WT Y ROX", "LQC 方向", "LQC mean dr′"],
            control is null ? [] : [[T(control.Status), N(control.WtYFamMean), N(control.WtYRoxMean), T(ControlDirection(control.LqcCall)), N(control.LqcMeanDrPrime)]]));
        tables.Add(new("Samples", ["Sample ID", "状态", "已保存分类", "mean dr′"],
            [.. evidence.Samples.Select(sample => new[] { T(sample.SampleId), T(sample.Status), T(Call(sample.Call, IsRunValid(evidence) && sample.Status == StatusNames.Pass)), N(sample.MeanDrPrime) })]));
        tables.Add(new("PerWell", ["Sample ID", "孔位", "Y FAM", "Y ROX", "dr′", "单孔技术分类", "Trace ID"],
            [.. evidence.Samples.SelectMany(sample => sample.PerWell.Select(well => new[] { T(sample.SampleId), T(well.Well), N(well.YFam), N(well.YRox), N(well.DrPrime), T(Call(well.Call, IsRunValid(evidence) && sample.Status == StatusNames.Pass)), T(well.TraceId) }))]));
        tables.Add(new("Issues", ["级别", "错误码", "孔位", "通道", "说明", "建议动作"],
        [
            .. run.BlockingIssues.Select(issue => IssueRow("blocking", issue)),
            .. run.WarningIssues.Select(issue => IssueRow("warning", issue)),
            .. (control?.Issues ?? []).Where(issue => !run.BlockingIssues.Contains(issue)).Select(issue => IssueRow("control", issue)),
        ]));
        tables.Add(new("Traces", ["Trace ID", "孔位", "通道", "源点索引（0起）", "算法版本", "Method SHA-256", "运算符", "阈值", "结果", "数值计算状态", "证据完整性"],
            [.. evidence.Traces.Select(trace => new[] { T(trace.TraceId), T(trace.Well), T(trace.Channel), T(trace.SourcePointIndex?.ToString(CultureInfo.InvariantCulture)), T(trace.Algorithm), T(trace.MethodSnapshotHash), T(trace.ComparisonOperator), N(trace.ThresholdRaw), N(trace.ResultRaw), T(trace.FinalStatus), T(trace.ProvenanceCompleteness) })]));
        tables.Add(new("TraceValues", ["Trace ID", "顺序（0起）", "中间值名称", "已存储原始精度值"],
            [.. evidence.Traces.SelectMany(trace => trace.IntermediateValues.Select((value, index) => new[] { T(trace.TraceId), T(index.ToString(CultureInfo.InvariantCulture)), T(value.Name), N(value.Value) }))]));
        AddScientificTables(tables, evidence);
        tables.Add(new("FigureRawPoints", ["孔", "通道", "序号（当前图内）", "Elapsed ticks", "冻结 Raw RFU", "原始序列总点数"],
            [.. (content.SignalFigures ?? []).SelectMany(figure => figure.Points.Select((point, index) => new[] { T(figure.Well), T(figure.Channel), T(index.ToString(CultureInfo.InvariantCulture)),
                T(point.ElapsedTicks.ToString(CultureInfo.InvariantCulture)), N(point.RawRfu), T(figure.TotalPoints.ToString(CultureInfo.InvariantCulture)) }))]));
        return tables.AsReadOnly();
    }

    private static void AddScientificTables(List<ReportTable> tables, RunEvidenceDto evidence)
    {
        IReadOnlyList<AnalysisStageEvidenceDto> stages = evidence.ScientificEvidence?.Stages ?? [];
        var identities = new List<ReportCell[]>();
        foreach (AnalysisStageEvidenceDto stage in stages)
        {
            AnalysisInputIdentityDto identity = stage.Identity;
            identities.Add(Row(stage.Stage, "证据完整性", stage.ProvenanceCompleteness));
            identities.Add(Row(stage.Stage, "输入 revision", identity.InputRevision.ToString(CultureInfo.InvariantCulture)));
            identities.Add(Row(stage.Stage, "Source SHA-256", identity.SourceSha256));
            identities.Add(Row(stage.Stage, "Mapping SHA-256", identity.MappingSha256));
            identities.Add(Row(stage.Stage, "Analysis Input SHA-256", identity.InputSnapshotSha256));
            identities.Add(Row(stage.Stage, "Method SHA-256", identity.MethodSnapshotHash));
            identities.Add(Row(stage.Stage, "Algorithm", identity.Algorithm));
            identities.Add(Row(stage.Stage, "Evaluated Points SHA-256", stage.EvaluatedPointsSha256));
        }
        tables.Add(new("StageIdentity", ["阶段", "身份字段", "已保存值"], identities));
        tables.Add(new("StageWells", ["阶段", "孔", "通道", "角色", "状态", "端点序号（0起）", "端点 ticks", "端点 Raw RFU", "工作表", "源行（0起）", "源列（0起）", "Fm 来源"],
            [.. stages.SelectMany(stage => stage.Wells.Select(well => new[] { T(stage.Stage), T(well.Well), T(well.Channel), T(well.Role), T(well.Status),
                T(well.Endpoint.PointIndex.ToString(CultureInfo.InvariantCulture)), T(well.Endpoint.ElapsedTicks.ToString(CultureInfo.InvariantCulture)), N(well.Endpoint.RawRfu), T(well.Endpoint.SheetName),
                T(well.Endpoint.SourceRowIndex?.ToString(CultureInfo.InvariantCulture)), T(well.Endpoint.SourceColumnIndex?.ToString(CultureInfo.InvariantCulture)),
                T(well.FmSource switch { "endpoint" => "所选端点", "plateauMean" => "合格平台均值", _ => well.Role == WellRoleNames.Positive100 ? "历史未记录" : "不适用" }) }))]));
        tables.Add(new("StageWindows", ["阶段", "孔", "通道", "窗口起始/min", "窗口结束/min", "首点序号", "末点序号", "实际点数"],
            [.. stages.SelectMany(stage => stage.Wells.Where(well => well.Window is not null).Select(well => new[] { T(stage.Stage), T(well.Well), T(well.Channel),
                N(well.Window!.StartMinutes), N(well.Window.EndMinutes), T(well.Window.FirstPointIndex.ToString(CultureInfo.InvariantCulture)), T(well.Window.LastPointIndex.ToString(CultureInfo.InvariantCulture)), T(well.Window.PointCount.ToString(CultureInfo.InvariantCulture)) }))]));
        tables.Add(new("StageValues", ["阶段", "孔", "通道", "计算量", "已存储原始精度值"],
            [.. stages.SelectMany(stage => stage.Wells.SelectMany(well => well.Values.Select(value => new[] { T(stage.Stage), T(well.Well), T(well.Channel), T(value.Name), N(value.Value) })))]));
        tables.Add(new("StageErrors", ["阶段", "孔", "通道", "已保存错误码"],
            [.. stages.SelectMany(stage => stage.Wells.SelectMany(well => well.ErrorCodes.Select(code => Row(stage.Stage, well.Well, well.Channel, code))))]));
        tables.Add(new("TraceSources", ["Trace ID", "孔", "通道", "点序号", "Elapsed ticks", "Raw RFU", "工作表", "源行（0起）", "源列（0起）", "输入 SHA-256"],
            [.. evidence.Traces.Where(trace => trace.Provenance is not null).SelectMany(trace => trace.Provenance!.SourcePoints.Select(point => new[] { T(trace.TraceId), T(point.Well), T(point.Channel), T(point.PointIndex.ToString(CultureInfo.InvariantCulture)),
                T(point.ElapsedTicks.ToString(CultureInfo.InvariantCulture)), N(point.RawRfu), T(point.SheetName), T(point.SourceRowIndex?.ToString(CultureInfo.InvariantCulture)), T(point.SourceColumnIndex?.ToString(CultureInfo.InvariantCulture)), T(trace.Provenance.Identity.InputSnapshotSha256) }))]));
    }

    private static IReadOnlyList<ReportCell[]> MappingRows(RunMappingDto? mapping) => mapping is null ? [] :
        [.. new[] { mapping.Fam, mapping.Rox }.SelectMany(region => region.WellColumns.Select(well => Row(region.Channel, region.SheetName,
            region.HeaderRowIndex.ToString(CultureInfo.InvariantCulture), region.EndRowIndex?.ToString(CultureInfo.InvariantCulture) ?? "到数据结束", region.TimeColumnIndex.ToString(CultureInfo.InvariantCulture),
            well.ColumnIndex.ToString(CultureInfo.InvariantCulture), well.Well)))];

    private static bool IsRunValid(RunEvidenceDto evidence) =>
        evidence.Snapshot.DomainState is RunStateNames.Analyzed or RunStateNames.Exported
        && evidence.Snapshot.AnalysisSummary?.Status == StatusNames.Pass;

    private static string Call(string? call, bool canShow) => !canShow ? "INVALID / 未提供有效判读" : call switch
    {
        CallNames.Positive => "BRAF V600E 阳性",
        CallNames.Negative => "BRAF V600E 阴性",
        _ => "未判定",
    };

    private static string ControlDirection(string? call) => call switch
    {
        CallNames.Positive => "Positive（已存 LQC 方向）",
        CallNames.Negative => "Negative（已存 LQC 方向）",
        _ => "未判定",
    };

    private static ReportCell[] IssueRow(string severity, IssueDto issue) => Row(severity, issue.Code, issue.WellId, issue.Channel, issue.UserMessage, issue.RecommendedAction);
    private static ReportCell T(string? value) => ReportCell.FromText(value);
    private static ReportCell N(double? value) => ReportCell.FromNumber(value);
    private static ReportCell[] Row(params string?[] values) => [.. values.Select(T)];
}
