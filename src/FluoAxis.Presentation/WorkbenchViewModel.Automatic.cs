namespace FluoAxis.Presentation;

using FluoAxis.Contracts;

public sealed partial class WorkbenchViewModel
{
    private bool automaticWorkflowActive;
    private bool automaticStepAuthorized;
    private string automaticStage = string.Empty;
    public string AutomaticStage { get => automaticStage; private set => SetProperty(ref automaticStage, value); }

    /// <summary>Uses an explicit recognition/profile decision and the existing sequential gateway commands. Never calculates scientific results.</summary>
    public async Task<bool> AnalyzeAutomaticallyAsync(SourceFileDto source, AutomaticImportProposalDto proposal,
        CancellationToken cancellationToken, IProgress<OperationProgressDto>? progress = null, PlateProfileDto? profile = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsBusy || Run is not { } initial || !snapshotIsCurrent) return false;
        if (initial.DomainState != RunStateNames.New || !initial.AllowedActions.Contains(RunAction.ConfirmMapping, StringComparer.Ordinal))
            return AutomaticFailure("SYSTEM_AUTO_NEW_RUN_REQUIRED", "新文件只能在新建 Run 中自动分析，已有结果保持不变。");
        if (!proposal.MappingReady || proposal.Options is null || proposal.Fam is null || proposal.Rox is null)
            return AutomaticFailure("MAPPING_AUTO_REVIEW_REQUIRED", "文件的通道、解析规则或时间单位仍有歧义，请先人工确认。");
        if (proposal.Issues.Any(issue => profile is null || issue.Code != "IMPORT_AUTO_ROLES_REQUIRED"))
            return AutomaticFailure("IMPORT_AUTO_REVIEW_REQUIRED", "文件识别仍有冲突或不完整解析信息，孔位方案不能覆盖这些问题。");
        if (proposal.Fam.Channel != ChannelNames.Fam || proposal.Rox.Channel != ChannelNames.Rox
            || !proposal.Fam.WellColumns.Select(well => well.Well).ToHashSet(StringComparer.Ordinal)
                .SetEquals(proposal.Rox.WellColumns.Select(well => well.Well)))
            return AutomaticFailure("MAPPING_AUTO_CHANNEL_MISMATCH", "FAM/ROX 孔位必须一一对应。");
        IReadOnlyList<WellAssignmentDto> assignments = proposal.Assignments;
        if (profile is not null)
        {
            if (profile.Definition.Options is { } options && options != proposal.Options)
                return AutomaticFailure("IMPORT_AUTO_PROFILE_OPTIONS_MISMATCH", "识别所用解析选项与已选孔位方案不一致，请重新识别。");
            GatewayResult<IReadOnlyList<WellAssignmentDto>> applied = PlateProfileApplication.Apply(profile,
                proposal.Fam.WellColumns.Select(well => well.Well).ToArray(), proposal.Assignments);
            if (!applied.IsSuccess) { SetFailure(applied.Errors, applied.CorrelationId); return false; }
            assignments = applied.Value!;
        }
        else if (proposal.RequiresReview)
            return AutomaticFailure("PLATE_AUTO_REVIEW_REQUIRED", "文件缺少明确孔角色或样本信息，请选择已确认的孔位方案或手工填写。");
        if (!AssignmentsCoverMapping(assignments, proposal.Fam))
            return AutomaticFailure("PLATE_AUTO_ASSIGNMENTS_INCOMPLETE", "自动分析需要完整、唯一的孔位分配及四类明确对照。");

        automaticWorkflowActive = true; NotifyDerivedState();
        try
        {
            CurrentSource = source; currentSourceMediaType = MediaTypeFor(source.OriginalFileName);
            CurrentPreview = new ImportPreviewDto(proposal.DetectedFormat,
                new[] { proposal.Fam.SheetName, proposal.Rox.SheetName }.Distinct(StringComparer.Ordinal).ToArray(),
                [Candidate(proposal.Fam), Candidate(proposal.Rox)], proposal.Issues);
            if (!await AutomaticStepAsync(initial.RunId, "mapping", 10, RunAction.ConfirmMapping,
                () => ConfirmMappingAsync(proposal.Fam, proposal.Rox, cancellationToken, proposal.Options), cancellationToken, progress)) return false;
            if (!await AutomaticStepAsync(initial.RunId, "plate", 30, RunAction.AssignWells,
                () => AssignWellsAsync(assignments, cancellationToken), cancellationToken, progress)) return false;
            return await ContinueAutomaticCoreAsync(initial.RunId, cancellationToken, progress);
        }
        finally { automaticStepAuthorized = false; automaticWorkflowActive = false; NotifyDerivedState(); }
    }

    /// <summary>Continues a manually confirmed Run from its backend-allowed scientific stage.</summary>
    public async Task<bool> ContinueAnalysisAutomaticallyAsync(CancellationToken cancellationToken, IProgress<OperationProgressDto>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsBusy || Run is not { } initial || !snapshotIsCurrent) return false;
        if (initial.DomainState is not (RunStateNames.Mapped or RunStateNames.CalibrationPassed or RunStateNames.ReadyForAnalysis))
            return AutomaticFailure("SYSTEM_AUTO_STAGE_UNAVAILABLE", "请先完成映射和孔位分配；失败或已有结果不会自动重算。");
        automaticWorkflowActive = true; NotifyDerivedState();
        try { return await ContinueAutomaticCoreAsync(initial.RunId, cancellationToken, progress); }
        finally { automaticStepAuthorized = false; automaticWorkflowActive = false; NotifyDerivedState(); }
    }

    private async Task<bool> ContinueAutomaticCoreAsync(Guid runId, CancellationToken token, IProgress<OperationProgressDto>? progress)
    {
        if (Run?.DomainState == RunStateNames.Mapped)
        {
            if (!await AutomaticStepAsync(runId, "calibration", 50, RunAction.RunCalibration,
                () => RunCalibrationAsync(token), token, progress)) return false;
            if (Run?.DomainState != RunStateNames.CalibrationPassed || CalibrationResult?.Status != StatusNames.Pass) return false;
        }
        if (Run?.DomainState == RunStateNames.CalibrationPassed)
        {
            if (!await AutomaticStepAsync(runId, "control", 70, RunAction.RunControlQc,
                () => RunControlQcAsync(token), token, progress)) return false;
            if (Run?.DomainState != RunStateNames.ReadyForAnalysis || ControlQcResult?.Status != StatusNames.Pass) return false;
        }
        if (Run?.DomainState != RunStateNames.ReadyForAnalysis) return false;
        if (!await AutomaticStepAsync(runId, "analysis", 90, RunAction.AnalyzeSamples,
            () => AnalyzeSamplesAsync(token), token, progress)) return false;
        bool passed = Run?.DomainState == RunStateNames.Analyzed && SampleResults.All(sample => sample.Status == StatusNames.Pass);
        AutomaticStage = passed ? "completed" : "results";
        progress?.Report(new OperationProgressDto(ImportStageNames.Validating, 100, passed ? "自动科研分析已完成。" : "分析已停止，请复核 INVALID 及逐孔证据。"));
        return passed;
    }

    private async Task<bool> AutomaticStepAsync(Guid runId, string stage, int percent, string requiredAction,
        Func<Task<bool>> operation, CancellationToken token, IProgress<OperationProgressDto>? progress)
    {
        token.ThrowIfCancellationRequested();
        if (Run is not { } before || before.RunId != runId || !snapshotIsCurrent
            || !before.AllowedActions.Contains(requiredAction, StringComparer.Ordinal))
            return AutomaticFailure("SYSTEM_AUTO_STAGE_UNAVAILABLE", "后端未允许下一分析步骤，自动流程已停止。");
        AutomaticStage = stage;
        progress?.Report(new OperationProgressDto(ImportStageNames.Validating, percent, stage switch
        { "mapping" => "确认已识别的数据映射…", "plate" => "应用明确孔位方案…", "calibration" => "运行校准…", "control" => "运行对照质控…", _ => "分析样本…" }));
        Task<bool> task;
        automaticStepAuthorized = true;
        try { task = operation(); }
        finally { automaticStepAuthorized = false; }
        bool success = await task;
        token.ThrowIfCancellationRequested();
        if (!success) return false;
        if (Run?.RunId != runId || Run.Revision != before.Revision + 1)
            return AutomaticFailure("SYSTEM_STALE_RUN_REVISION", "运行在自动步骤之间发生变化，请刷新并人工复核。");
        return true;
    }

    private static bool AssignmentsCoverMapping(IReadOnlyList<WellAssignmentDto> assignments, ChannelRegionMappingDto mapping)
    {
        if (assignments.Count != mapping.WellColumns.Count || assignments.Count is < 4 or > 96
            || assignments.Any(item => item is null) || assignments.Select(item => item.Well).Distinct(StringComparer.Ordinal).Count() != assignments.Count
            || !assignments.Select(item => item.Well).ToHashSet(StringComparer.Ordinal).SetEquals(mapping.WellColumns.Select(item => item.Well))) return false;
        foreach (string role in new[] { WellRoleNames.Ntc, WellRoleNames.Positive100, WellRoleNames.WildType, WellRoleNames.Lqc01Percent })
            if (!assignments.Any(item => item.Role == role)) return false;
        return assignments.Where(item => item.Role == WellRoleNames.Sample).All(item => item.ReplicateIndex is >= 1)
            && assignments.Where(item => item.Role == WellRoleNames.Sample).GroupBy(item => item.SampleId, StringComparer.Ordinal)
                .All(group => group.Select(item => item.ReplicateIndex).Distinct().Count() == group.Count());
    }

    private static RegionCandidateDto Candidate(ChannelRegionMappingDto mapping) =>
        new(mapping.SheetName, mapping.HeaderRowIndex, mapping.TimeColumnIndex, mapping.WellColumns, mapping.Channel);
    private bool AutomaticFailure(string code, string message)
    {
        SetFailure([new AppErrorDto(code, SeverityNames.Blocking, message, "查看当前步骤与保存证据；确认缺项后可手工继续。")], Guid.NewGuid());
        return false;
    }
}
