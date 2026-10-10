namespace FluoAxis.GatewayContract.Tests;

using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Xunit;

public sealed class SavedWorkbenchEvidenceTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirstRun = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SecondRun = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task LoadRestoresSavedResultsAssignmentsAndTraceWithoutReplayingCommands()
    {
        RunEvidenceDto saved = Evidence(FirstRun, "Saved experiment", analyzed: true);
        var gateway = new EvidenceGateway(_ => Task.FromResult(GatewayResult.Ok(saved)));
        var model = new WorkbenchViewModel(gateway);

        Assert.True(await model.LoadRunAsync(FirstRun, TestContext.Current.CancellationToken));

        Assert.Equal("Saved experiment", model.ProjectName);
        Assert.Equal(5, model.Run!.Revision);
        Assert.Equal(RunStateNames.Analyzed, model.RunState);
        Assert.Same(saved.Calibration, model.CalibrationResult);
        Assert.Same(saved.Control, model.ControlQcResult);
        Assert.Equal(saved.Samples, model.SampleResults);
        Assert.Same(saved, model.CurrentEvidence);
        Assert.Equal("data/raw/saved.csv", model.CurrentSource!.RelativeResourcePath);
        Assert.Null(model.CurrentPreview);
        Assert.Equal("SAVED-SAMPLE", Assert.Single(model.CurrentEvidence.Assignments).SampleId);
        Assert.Equal(0.40000000000000002, Assert.Single(model.CurrentEvidence.Traces).ResultRaw);
        Assert.Equal(new[] { "F_FAM_120", "Y_FAM", "Y_ROX" }, saved.Traces[0].IntermediateValues.Select(value => value.Name));
        Assert.Equal(1, gateway.ReadCount);
        Assert.Equal(0, gateway.MutationCount);
        Assert.Equal("complete", model.StageProgress("results").State);
        Assert.Equal("complete", model.StageProgress("plate").State);
    }

    [Fact]
    public async Task OpeningAnotherRunClearsPriorResultsAndRestoresMappedDraftEvidence()
    {
        RunEvidenceDto analyzed = Evidence(FirstRun, "Finished", analyzed: true);
        RunEvidenceDto mapped = Evidence(SecondRun, "Mapped", analyzed: false);
        var gateway = new EvidenceGateway(id => Task.FromResult(GatewayResult.Ok(id == FirstRun ? analyzed : mapped)));
        var model = new WorkbenchViewModel(gateway);
        Assert.True(await model.LoadRunAsync(FirstRun, TestContext.Current.CancellationToken));

        Assert.True(await model.LoadRunAsync(SecondRun, TestContext.Current.CancellationToken));

        Assert.Equal(SecondRun, model.Run!.RunId);
        Assert.Equal(RunStateNames.Mapped, model.RunState);
        Assert.Empty(model.SampleResults);
        Assert.Null(model.CalibrationResult);
        Assert.Null(model.ControlQcResult);
        Assert.Empty(model.CurrentEvidence!.Traces);
        Assert.Equal("A1", Assert.Single(model.CurrentEvidence.Mapping!.Fam.WellColumns).Well);
        Assert.Equal(1, Assert.Single(model.CurrentEvidence.Assignments).ReplicateIndex);
        Assert.True(model.CanRunCalibration);
        Assert.Equal("available", model.StageProgress("calibration").State);
        Assert.Equal("pending", model.StageProgress("analysis").State);
        Assert.Equal(0, gateway.MutationCount);
    }

    [Fact]
    public async Task FailedStageProgressUsesBackendStatusWithoutInventingCompletion()
    {
        RunEvidenceDto saved = Evidence(FirstRun, "Failed", false);
        var snapshot = new RunSnapshotDto(FirstRun, 3, RunStateNames.RunInvalid, [RunAction.ViewExplorer], [], [],
            saved.Snapshot.MethodId, saved.Snapshot.MethodVersion, saved.Snapshot.MethodSnapshotHash, saved.Snapshot.SourceFiles,
            saved.Snapshot.MappingSummary, new CalibrationSummaryDto(StatusNames.Fail), updatedUtc: Created);
        var evidence = new RunEvidenceDto(saved.ProjectName, snapshot, saved.Mapping, saved.Assignments,
            new CalibrationResultDto(StatusNames.Fail), null, [], [], ProvenanceCompletenessNames.Unavailable);
        var model = new WorkbenchViewModel(new EvidenceGateway(_ => Task.FromResult(GatewayResult.Ok(evidence))));
        Assert.True(await model.LoadRunAsync(FirstRun, TestContext.Current.CancellationToken));
        Assert.Equal("failed", model.StageProgress("calibration").State);
        Assert.Equal("pending", model.StageProgress("analysis").State);
        Assert.False(model.CanAnalyzeSamples);
    }

    [Fact]
    public async Task OlderDelayedEvidenceCannotReplaceLaterProject()
    {
        var delayed = new TaskCompletionSource<GatewayResult<RunEvidenceDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new EvidenceGateway(id => id == FirstRun ? delayed.Task : Task.FromResult(GatewayResult.Ok(Evidence(SecondRun, "Latest", false))));
        var model = new WorkbenchViewModel(gateway);
        Task<bool> older = model.LoadRunAsync(FirstRun, TestContext.Current.CancellationToken);
        Assert.True(await model.LoadRunAsync(SecondRun, TestContext.Current.CancellationToken));

        delayed.SetResult(GatewayResult.Ok(Evidence(FirstRun, "Older", true)));
        Assert.False(await older);

        Assert.Equal("Latest", model.ProjectName);
        Assert.Equal(SecondRun, model.Run!.RunId);
        Assert.Empty(model.SampleResults);
    }

    [Fact]
    public async Task ReadFailureClearsOldProjectAndDisablesMutationActions()
    {
        var gateway = new EvidenceGateway(id => Task.FromResult(id == FirstRun
            ? GatewayResult.Ok(Evidence(FirstRun, "Saved", false))
            : GatewayResult.Fail<RunEvidenceDto>(new AppErrorDto("SYSTEM_EVIDENCE_READ_FAILED", SeverityNames.Blocking, "Read failed.", "Reopen the workspace."))));
        var model = new WorkbenchViewModel(gateway);
        Assert.True(await model.LoadRunAsync(FirstRun, TestContext.Current.CancellationToken));

        Assert.False(await model.LoadRunAsync(SecondRun, TestContext.Current.CancellationToken));

        Assert.Null(model.Run);
        Assert.Null(model.CurrentEvidence);
        Assert.Empty(model.SampleResults);
        Assert.False(model.CanRunCalibration);
        Assert.Equal("SYSTEM_EVIDENCE_READ_FAILED", model.ErrorCode);
        Assert.Equal(WorkbenchUiState.RecoverableError, model.UiState);
        Assert.Equal(0, gateway.MutationCount);
    }

    [Fact]
    public void EmptyWorkspaceDoesNotCreateAProject()
    {
        var gateway = new EvidenceGateway(_ => throw new InvalidOperationException("No query expected."));
        var model = new WorkbenchViewModel(gateway);
        model.ShowEmptyWorkspace();
        Assert.Equal(WorkbenchUiState.Empty, model.UiState);
        Assert.Null(model.Run);
        Assert.False(model.CanImport);
        Assert.Equal(0, gateway.ReadCount);
        Assert.Equal(0, gateway.MutationCount);
    }

    [Fact]
    public async Task MutationRefreshUsesStoredEvidenceAndPublishesCommittedStage()
    {
        RunEvidenceDto current = Evidence(FirstRun, "Saved", false);
        var gateway = new EvidenceGateway(_ => Task.FromResult(GatewayResult.Ok(current)));
        gateway.CalibrationOperation = (_, _) =>
        {
            current = CalibratedEvidence(current);
            return Task.FromResult(GatewayResult.Ok(new CalibrationResultDto(StatusNames.Pass)));
        };
        var model = new WorkbenchViewModel(gateway);
        Assert.True(await model.LoadRunAsync(FirstRun, TestContext.Current.CancellationToken));

        Assert.True(await model.RunCalibrationAsync(TestContext.Current.CancellationToken));

        Assert.Same(current.Calibration, model.CalibrationResult);
        Assert.Equal(1000.0, model.CalibrationResult!.RunFmFam);
        Assert.Equal(RunStateNames.CalibrationPassed, model.RunState);
        Assert.Equal(3, model.Run!.Revision);
        Assert.Equal(2, gateway.ReadCount);
        Assert.Equal(1, gateway.MutationCount);
    }

    [Fact]
    public async Task CancellationAfterCommitRestoresSavedStageThroughIndependentReadToken()
    {
        RunEvidenceDto current = Evidence(FirstRun, "Saved", false);
        var gateway = new EvidenceGateway(_ => Task.FromResult(GatewayResult.Ok(current)));
        using var cancellation = new CancellationTokenSource();
        gateway.CalibrationOperation = (_, token) =>
        {
            current = CalibratedEvidence(current);
            cancellation.Cancel();
            return Task.FromCanceled<GatewayResult<CalibrationResultDto>>(token);
        };
        var model = new WorkbenchViewModel(gateway);
        Assert.True(await model.LoadRunAsync(FirstRun, TestContext.Current.CancellationToken));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.RunCalibrationAsync(cancellation.Token));

        Assert.Same(current.Calibration, model.CalibrationResult);
        Assert.Equal(3, model.Run!.Revision);
        Assert.True(model.CanRunControlQc);
        Assert.False(model.IsBusy);
        Assert.Equal(2, gateway.ReadCount);
    }

    [Fact]
    public async Task FailedPostCommitEvidenceReadDisablesFurtherMutations()
    {
        int calls = 0;
        var gateway = new EvidenceGateway(_ => Task.FromResult(++calls == 1
            ? GatewayResult.Ok(Evidence(FirstRun, "Saved", false))
            : GatewayResult.Fail<RunEvidenceDto>(new AppErrorDto("SYSTEM_EVIDENCE_READ_FAILED", SeverityNames.Blocking, "Read failed.", "Reopen."))));
        gateway.CalibrationOperation = (_, _) => Task.FromResult(GatewayResult.Ok(new CalibrationResultDto(StatusNames.Pass)));
        var model = new WorkbenchViewModel(gateway);
        Assert.True(await model.LoadRunAsync(FirstRun, TestContext.Current.CancellationToken));

        Assert.False(await model.RunCalibrationAsync(TestContext.Current.CancellationToken));

        Assert.False(model.CanRunCalibration);
        Assert.False(model.CanRunControlQc);
        Assert.Null(model.CalibrationResult);
        Assert.Equal("SYSTEM_EVIDENCE_READ_FAILED", model.ErrorCode);
    }

    private static RunEvidenceDto CalibratedEvidence(RunEvidenceDto previous)
    {
        var snapshot = new RunSnapshotDto(previous.Snapshot.RunId, 3, RunStateNames.CalibrationPassed,
            [RunAction.RunControlQc, RunAction.ViewExplorer], [], [], previous.Snapshot.MethodId,
            previous.Snapshot.MethodVersion, previous.Snapshot.MethodSnapshotHash, previous.Snapshot.SourceFiles,
            previous.Snapshot.MappingSummary, new CalibrationSummaryDto(StatusNames.Pass, 1000, 500, 100, 50), updatedUtc: Created);
        return new RunEvidenceDto(previous.ProjectName, snapshot, previous.Mapping, previous.Assignments,
            new CalibrationResultDto(StatusNames.Pass, 1000, 500, 100, 50), null, [], [], ProvenanceCompletenessNames.Unavailable);
    }

    private static RunEvidenceDto Evidence(Guid runId, string name, bool analyzed)
    {
        string hash = new('a', 64);
        var source = new SourceFileDto("saved.csv", "data/raw/saved.csv", new string('b', 64), 173, Created);
        var mapping = new RunMappingDto(
            new ChannelRegionMappingDto(ChannelNames.Fam, "text-body", 0, 0, [new(1, "A1")]),
            new ChannelRegionMappingDto(ChannelNames.Rox, "text-body", 0, 0, [new(2, "A1")]));
        var snapshot = new RunSnapshotDto(runId, analyzed ? 5 : 2, analyzed ? RunStateNames.Analyzed : RunStateNames.Mapped,
            analyzed ? [RunAction.ViewExplorer, RunAction.Export] : [RunAction.RunCalibration, RunAction.ViewExplorer], [], [],
            "BRAF-V600E-Research", "1.0", hash, [source],
            new MappingSummaryDto(1, 1, ["SAVED-SAMPLE"]),
            analyzed ? new CalibrationSummaryDto(StatusNames.Pass, 1000, 500, 100, 50) : null,
            analyzed ? new ControlSummaryDto(StatusNames.Pass, 0, 0, CallNames.Positive, 0.6) : null,
            analyzed ? new AnalysisSummaryDto(StatusNames.Pass, 1, 1, 0, 0) : null, Created);
        string traceId = "saved-trace-1";
        return new RunEvidenceDto(name, snapshot, mapping, [new("A1", WellRoleNames.Sample, "SAVED-SAMPLE", replicateIndex: 1)],
            analyzed ? new CalibrationResultDto(StatusNames.Pass, 1000, 500, 100, 50) : null,
            analyzed ? new ControlQcResultDto(StatusNames.Pass, 0, 0, CallNames.Positive, 0.6) : null,
            analyzed ? [new SampleAnalysisDto("SAVED-SAMPLE", StatusNames.Pass, CallNames.Positive, 0.4,
                [new PerWellResultDto("A1", 0.4, 0, 0.4, CallNames.Positive, traceId)])] : [],
            analyzed ? [new CalculationTraceDto(traceId, "braf-engine-test-v1", hash, "A1", ChannelNames.Fam, 2,
                [new("F_FAM_120", 460), new("Y_FAM", 0.4), new("Y_ROX", 0)], ">=", 0.4, 0.4, StatusNames.Pass)] : [],
            analyzed ? ProvenanceCompletenessNames.StoredPartial : ProvenanceCompletenessNames.Unavailable);
    }

    private sealed class EvidenceGateway(Func<Guid, Task<GatewayResult<RunEvidenceDto>>> read) : IFluoAxisGateway, IRunEvidenceGateway
    {
        public int ReadCount { get; private set; }
        public int MutationCount { get; private set; }
        public Func<RunCalibrationCommand, CancellationToken, Task<GatewayResult<CalibrationResultDto>>>? CalibrationOperation { get; set; }
        public Task<GatewayResult<RunEvidenceDto>> GetRunEvidenceAsync(GetRunQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return read(query.RunId);
        }

        private Task<T> RejectMutation<T>()
        {
            MutationCount++;
            throw new InvalidOperationException("Opening saved evidence must not issue mutation commands.");
        }

        public Task<GatewayResult<ProjectSnapshotDto>> CreateProjectAsync(CreateProjectCommand command, CancellationToken cancellationToken) => RejectMutation<GatewayResult<ProjectSnapshotDto>>();
        public Task<GatewayResult<ImportPreviewDto>> InspectImportAsync(InspectImportCommand command, IProgress<OperationProgressDto>? progress, CancellationToken cancellationToken) => RejectMutation<GatewayResult<ImportPreviewDto>>();
        public Task<GatewayResult<RunSnapshotDto>> ConfirmMappingAsync(ConfirmMappingCommand command, CancellationToken cancellationToken) => RejectMutation<GatewayResult<RunSnapshotDto>>();
        public Task<GatewayResult<RunSnapshotDto>> AssignWellsAsync(AssignWellsCommand command, CancellationToken cancellationToken) => RejectMutation<GatewayResult<RunSnapshotDto>>();
        public Task<GatewayResult<CalibrationResultDto>> RunCalibrationAsync(RunCalibrationCommand command, CancellationToken cancellationToken)
        {
            if (CalibrationOperation is null) return RejectMutation<GatewayResult<CalibrationResultDto>>();
            MutationCount++;
            return CalibrationOperation(command, cancellationToken);
        }
        public Task<GatewayResult<ControlQcResultDto>> RunControlQcAsync(RunControlQcCommand command, CancellationToken cancellationToken) => RejectMutation<GatewayResult<ControlQcResultDto>>();
        public Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesAsync(AnalyzeSamplesCommand command, CancellationToken cancellationToken) => RejectMutation<GatewayResult<IReadOnlyList<SampleAnalysisDto>>>();
        public Task<GatewayResult<RunSnapshotDto>> GetRunAsync(GetRunQuery query, CancellationToken cancellationToken) => throw new InvalidOperationException("The saved-evidence query is authoritative.");
    }
}
