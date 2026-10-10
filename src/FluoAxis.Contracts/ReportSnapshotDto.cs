namespace FluoAxis.Contracts;

/// <summary>Immutable stored-evidence content. Creating a report never changes the Run or computes a scientific result.</summary>
public sealed record ReportContentDto
{
    public const int CurrentSchemaVersion = 1;
    public const string DraftApproval = "draftUnapproved";
    public const string RequiredResearchNotice = "仅供科研使用，不用于临床诊断、治疗决策或患者管理。Method 与 Golden 数据尚未完成科学批准；本报告是基于已存储证据的科研技术草稿。";

    public int SchemaVersion { get; }
    public string SoftwareVersion { get; }
    public DateTimeOffset CreatedUtc { get; }
    public string ApprovalStatus { get; }
    public string ResearchUseNotice { get; }
    public RunEvidenceDto Evidence { get; }
    public IReadOnlyList<ReportSignalFigureDto>? SignalFigures { get; }
    public ReportPlotFigureDto? PlotFigure { get; }

    public ReportContentDto(string softwareVersion, DateTimeOffset createdUtc, RunEvidenceDto evidence,
        int schemaVersion = CurrentSchemaVersion, string approvalStatus = DraftApproval,
        string researchUseNotice = RequiredResearchNotice, IReadOnlyList<ReportSignalFigureDto>? signalFigures = null,
        ReportPlotFigureDto? plotFigure = null)
    {
        ContractGuard.RequireNonBlank(softwareVersion, nameof(softwareVersion));
        ContractGuard.RequireUtc(createdUtc, nameof(createdUtc));
        ArgumentNullException.ThrowIfNull(evidence);
        if (schemaVersion != CurrentSchemaVersion || approvalStatus != DraftApproval || researchUseNotice != RequiredResearchNotice)
            throw new ArgumentException("Only the current research technical draft report contract is supported.", nameof(schemaVersion));

        SchemaVersion = schemaVersion;
        SoftwareVersion = softwareVersion;
        CreatedUtc = createdUtc;
        ApprovalStatus = approvalStatus;
        ResearchUseNotice = researchUseNotice;
        Evidence = FreezeEvidence(evidence);
        if (signalFigures?.Count > 12 || signalFigures?.Any(figure => figure is null) == true)
            throw new ArgumentException("Invalid report signal views.", nameof(signalFigures));
        SignalFigures = signalFigures is null ? null : Freeze(signalFigures);
        if (plotFigure is not null && (plotFigure.RunId != evidence.Snapshot.RunId || plotFigure.RunRevision != evidence.Snapshot.Revision))
            throw new ArgumentException("REPORT_FIGURE_RUN_MISMATCH", nameof(plotFigure));
        PlotFigure = plotFigure;
    }

    private static RunEvidenceDto FreezeEvidence(RunEvidenceDto evidence)
    {
        RunSnapshotDto run = evidence.Snapshot;
        var snapshot = new RunSnapshotDto(run.RunId, run.Revision, run.DomainState,
            Freeze(run.AllowedActions.Order(StringComparer.Ordinal)), Freeze(run.BlockingIssues), Freeze(run.WarningIssues),
            run.MethodId, run.MethodVersion, run.MethodSnapshotHash,
            Freeze(run.SourceFiles.OrderBy(source => source.RelativeResourcePath, StringComparer.Ordinal)),
            run.MappingSummary is { } mappingSummary
                ? new MappingSummaryDto(mappingSummary.TotalWells, mappingSummary.AssignedWells, Freeze(mappingSummary.SampleIds.Order(StringComparer.Ordinal))) : null,
            run.CalibrationSummary, run.ControlSummary, run.AnalysisSummary, run.UpdatedUtc);

        RunMappingDto? mapping = evidence.Mapping is { } storedMapping
            ? new RunMappingDto(FreezeRegion(storedMapping.Fam), FreezeRegion(storedMapping.Rox), storedMapping.Options) : null;
        CalibrationResultDto? calibration = evidence.Calibration is { } cal
            ? new CalibrationResultDto(cal.Status, cal.RunFmFam, cal.RunFmRox, cal.RunFbFam, cal.RunFbRox, Freeze(cal.PerWellDiagnostics)) : null;
        ControlQcResultDto? control = evidence.Control is { } ctl
            ? new ControlQcResultDto(ctl.Status, ctl.WtYFamMean, ctl.WtYRoxMean, ctl.LqcCall, ctl.LqcMeanDrPrime, Freeze(ctl.Issues)) : null;
        SampleAnalysisDto[] samples = [.. evidence.Samples.OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .Select(sample => new SampleAnalysisDto(sample.SampleId, sample.Status, sample.Call, sample.MeanDrPrime,
                Freeze(sample.PerWell.OrderBy(well => well.Well, StringComparer.Ordinal))))];
        CalculationTraceDto[] traces = [.. evidence.Traces.OrderBy(trace => trace.TraceId, StringComparer.Ordinal)
            .Select(trace => new CalculationTraceDto(trace.TraceId, trace.Algorithm, trace.MethodSnapshotHash, trace.Well,
                trace.Channel, trace.SourcePointIndex, Freeze(trace.IntermediateValues), trace.ComparisonOperator,
                trace.ThresholdRaw, trace.ResultRaw, trace.FinalStatus, trace.ProvenanceCompleteness, trace.Provenance))];

        return new RunEvidenceDto(evidence.ProjectName, snapshot, mapping,
            Freeze(evidence.Assignments.OrderBy(assignment => assignment.Well, StringComparer.Ordinal)),
            calibration, control, Freeze(samples), Freeze(traces), evidence.ProvenanceCompleteness, evidence.ScientificEvidence);
    }

    private static ChannelRegionMappingDto FreezeRegion(ChannelRegionMappingDto region) => new(
        region.Channel, region.SheetName, region.HeaderRowIndex, region.TimeColumnIndex,
        Freeze(region.WellColumns.OrderBy(well => well.ColumnIndex)), region.EndRowIndex);

    private static System.Collections.ObjectModel.ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

/// <summary>Content-bound report identity; renderers verify the canonical content hash before generating any output.</summary>
public sealed record ReportSnapshotDto
{
    public ReportContentDto Content { get; }
    public string SnapshotSha256 { get; }
    public string ReportId => $"RUO-{SnapshotSha256[..16]}";

    public ReportSnapshotDto(ReportContentDto content, string snapshotSha256)
    {
        ArgumentNullException.ThrowIfNull(content);
        ContractGuard.RequireSha256(snapshotSha256, nameof(snapshotSha256));
        Content = content;
        SnapshotSha256 = snapshotSha256.ToLowerInvariant();
    }
}

/// <summary>Safe export receipt. The chosen external location never crosses the Infrastructure boundary.</summary>
public sealed record ReportExportDto
{
    public string DisplayName { get; }
    public string Format { get; }
    public string Sha256 { get; }
    public long FileSizeBytes { get; }
    public string SnapshotSha256 { get; }

    public ReportExportDto(string displayName, string format, string sha256, long fileSizeBytes, string snapshotSha256)
    {
        ContractGuard.RequireNonBlank(displayName, nameof(displayName));
        if (displayName.IndexOfAny(['/', '\\', ':']) >= 0 || displayName.Any(char.IsControl))
            throw new ArgumentException("Export display name must be a safe bare file name.", nameof(displayName));
        if (format is not ("html" or "xlsx" or "pdf")) throw new ArgumentException("Unsupported report format.", nameof(format));
        if (!displayName.EndsWith($".{format}", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Export display name must match its format.", nameof(displayName));
        ContractGuard.RequireSha256(sha256, nameof(sha256));
        ContractGuard.RequirePositive(fileSizeBytes, nameof(fileSizeBytes));
        ContractGuard.RequireSha256(snapshotSha256, nameof(snapshotSha256));
        DisplayName = displayName;
        Format = format;
        Sha256 = sha256.ToLowerInvariant();
        FileSizeBytes = fileSizeBytes;
        SnapshotSha256 = snapshotSha256.ToLowerInvariant();
    }
}
