namespace FluoAxis.GatewayContract.Tests;

using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Xunit;

public sealed class WorkbenchViewModelTests
{
    private static readonly Guid RunId = Guid.Parse("0199a2b4-4c00-7000-8000-000000000001");

    [Fact]
    public async Task Initialize_loads_project_and_backend_derived_actions()
    {
        var gateway = new StubGateway();
        var viewModel = new WorkbenchViewModel(gateway);

        await viewModel.InitializeAsync("Research workspace", CancellationToken.None);

        Assert.Equal(WorkbenchUiState.Ready, viewModel.UiState);
        Assert.Equal("Research workspace", viewModel.ProjectName);
        Assert.Equal(RunStateNames.New, viewModel.RunState);
        Assert.Equal("BRAF-V600E-Research v1.0", viewModel.MethodIdentity);
        Assert.True(viewModel.IsStageAvailable(viewModel.GuidedStages[0]));
        Assert.True(viewModel.IsStageAvailable(viewModel.GuidedStages[1]));
        Assert.All(viewModel.GuidedStages.Skip(2), stage => Assert.False(viewModel.IsStageAvailable(stage)));
        Assert.True(viewModel.CanImport);
        Assert.True(viewModel.CanConfirmMapping);
        Assert.False(viewModel.CanAssignWells);
        Assert.False(viewModel.CanRunCalibration);
        Assert.False(viewModel.CanRunControlQc);
        Assert.False(viewModel.CanAnalyzeSamples);
        Assert.False(viewModel.CanExport);
    }

    [Fact]
    public async Task Initialize_surfaces_structured_failure_and_correlation()
    {
        GatewayResult<ProjectSnapshotDto> failure = GatewayResult.Fail<ProjectSnapshotDto>(
            new AppErrorDto(GatewayErrorCodes.CapabilityUnavailable, SeverityNames.Blocking, "Unavailable.", "Retry later."));
        var gateway = new StubGateway
        {
            CreateResult = failure,
        };
        var viewModel = new WorkbenchViewModel(gateway);

        await viewModel.InitializeAsync("Research workspace", CancellationToken.None);

        Assert.Equal(WorkbenchUiState.RecoverableError, viewModel.UiState);
        Assert.Equal(GatewayErrorCodes.CapabilityUnavailable, viewModel.ErrorCode);
        Assert.Equal("Retry later.", viewModel.RecommendedAction);
        Assert.Equal(failure.CorrelationId, viewModel.CorrelationId);
    }

    [Fact]
    public async Task Initialize_honors_cancellation_without_publishing_ready_state()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var viewModel = new WorkbenchViewModel(new StubGateway());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            viewModel.InitializeAsync("Research workspace", cancellation.Token));

        Assert.Equal(WorkbenchUiState.Initial, viewModel.UiState);
        Assert.Null(viewModel.Run);
    }

    [Fact]
    public async Task Later_load_suppresses_an_older_delayed_response()
    {
        var first = new TaskCompletionSource<GatewayResult<ProjectSnapshotDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new StubGateway { FirstCreate = first.Task };
        var viewModel = new WorkbenchViewModel(gateway);
        Task older = viewModel.InitializeAsync("Older", CancellationToken.None);

        await viewModel.InitializeAsync("Newer", CancellationToken.None);
        first.SetResult(StubGateway.Project("Older"));
        await older;

        Assert.Equal("Newer", viewModel.ProjectName);
        Assert.Equal(WorkbenchUiState.Ready, viewModel.UiState);
    }

    [Fact]
    public void SelectArea_changes_only_presentation_navigation_state()
    {
        var viewModel = new WorkbenchViewModel(new StubGateway());

        viewModel.SelectArea("diagnostics");

        Assert.Equal("diagnostics", viewModel.ActiveArea);
        Assert.Null(viewModel.Run);
    }

    [Fact]
    public async Task ManagedImportAndMutationsUseCurrentContractsAndRefreshSnapshots()
    {
        var gateway = new WorkflowGateway();
        var viewModel = new WorkbenchViewModel(gateway, "research-operator", new FixedTimeProvider());
        await viewModel.InitializeAsync("Research workspace", TestContext.Current.CancellationToken);
        var source = new SourceFileDto("run.xlsx", "data/raw/run.xlsx", new string('b', 64), 128, FixedTimeProvider.Now);

        ImportPreviewDto? preview = await viewModel.InspectImportAsync(source, TestContext.Current.CancellationToken);
        Assert.Same(gateway.Preview, preview);
        Assert.Same(source, viewModel.CurrentSource);
        Assert.Same(preview, viewModel.CurrentPreview);
        Assert.Equal(source.OriginalFileName, gateway.Inspection!.OriginalFileName);
        Assert.Equal(source.RelativeResourcePath, gateway.Inspection.RelativeResourcePath);
        Assert.Equal(SourceFileDto.MediaTypeNames.Xlsx, gateway.Inspection.FormatHint);

        var fam = new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", 2, 0, [new(1, "A1")]);
        var rox = new ChannelRegionMappingDto(ChannelNames.Rox, "ROX", 4, 0, [new(1, "A1")]);
        Assert.True(await viewModel.ConfirmMappingAsync(fam, rox, TestContext.Current.CancellationToken));
        Assert.Equal(8, viewModel.Run!.Revision);
        ConfirmMappingCommand mapping = Assert.IsType<ConfirmMappingCommand>(gateway.Commands[0]);
        Assert.Same(fam, mapping.Fam);
        Assert.Same(rox, mapping.Rox);
        Assert.Equal(source.Sha256, mapping.SourceSha256);
        Assert.Equal(source.FileSizeBytes, mapping.SourceSizeBytes);

        WellAssignmentDto[] assignments = [new("A1", WellRoleNames.Ntc)];
        Assert.True(await viewModel.AssignWellsAsync(assignments, TestContext.Current.CancellationToken));
        Assert.True(await viewModel.RunCalibrationAsync(TestContext.Current.CancellationToken));
        Assert.True(await viewModel.RunControlQcAsync(TestContext.Current.CancellationToken));
        Assert.True(await viewModel.AnalyzeSamplesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new[] { 7, 8, 9, 10, 11 }, gateway.Commands.Select(command => command.ExpectedRunRevision));
        Assert.All(gateway.Commands, command =>
        {
            Assert.Equal("research-operator", command.ActorId);
            Assert.Equal(FixedTimeProvider.Now, command.IssuedUtc);
            Assert.Equal(ContractVersion.Current, command.ContractVersion);
            Assert.NotEqual(Guid.Empty, command.RequestId);
        });
        Assert.Equal(12, viewModel.Run!.Revision);
        Assert.Equal(RunStateNames.Analyzed, viewModel.RunState);
        Assert.Equal(StatusNames.Pass, viewModel.CalibrationResult!.Status);
        Assert.Equal(StatusNames.Pass, viewModel.ControlQcResult!.Status);
        Assert.Single(viewModel.SampleResults);
        Assert.Equal(6, gateway.GetRunCalls);
    }

    [Fact]
    public async Task BusyOperationRejectsDuplicateSubmissionAndDisablesActions()
    {
        var pending = new TaskCompletionSource<GatewayResult<CalibrationResultDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new WorkflowGateway { PendingCalibration = pending.Task };
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Research workspace", TestContext.Current.CancellationToken);

        Task<bool> first = viewModel.RunCalibrationAsync(TestContext.Current.CancellationToken);
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanRunCalibration);
        Assert.False(await viewModel.RunCalibrationAsync(TestContext.Current.CancellationToken));
        Assert.Single(gateway.Commands);
        pending.SetResult(GatewayResult.Ok(new CalibrationResultDto(StatusNames.Pass)));
        Assert.True(await first);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task StaleRevisionRefreshesSnapshotAndPreservesActionableError()
    {
        var gateway = new WorkflowGateway { RejectCalibrationAsStale = true };
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Research workspace", TestContext.Current.CancellationToken);

        Assert.False(await viewModel.RunCalibrationAsync(TestContext.Current.CancellationToken));

        Assert.Equal(8, viewModel.Run!.Revision);
        Assert.Equal(GatewayErrorCodes.StaleRunRevision, viewModel.ErrorCode);
        Assert.Equal(WorkbenchUiState.RecoverableError, viewModel.UiState);
        Assert.Equal("Refresh and retry.", viewModel.RecommendedAction);
        Assert.Null(viewModel.CalibrationResult);
        Assert.Equal(2, gateway.GetRunCalls);
    }

    [Fact]
    public async Task StageCancellationKeepsLastSnapshotAndNoResult()
    {
        var gateway = new WorkflowGateway();
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Research workspace", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewModel.RunCalibrationAsync(cancellation.Token));

        Assert.Equal(7, viewModel.Run!.Revision);
        Assert.Equal(WorkbenchUiState.Ready, viewModel.UiState);
        Assert.Null(viewModel.CalibrationResult);
    }

    [Fact]
    public async Task MissingRunAndMissingPreviewAreStructuredFailures()
    {
        var viewModel = new WorkbenchViewModel(new WorkflowGateway());
        Assert.False(await viewModel.RunCalibrationAsync(TestContext.Current.CancellationToken));
        Assert.Equal("SYSTEM_NO_RUN_LOADED", viewModel.ErrorCode);

        await viewModel.InitializeAsync("Research workspace", TestContext.Current.CancellationToken);
        var fam = new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", 0, 0, [new(1, "A1")]);
        var rox = new ChannelRegionMappingDto(ChannelNames.Rox, "ROX", 0, 0, [new(1, "A1")]);
        Assert.False(await viewModel.ConfirmMappingAsync(fam, rox, TestContext.Current.CancellationToken));
        Assert.Equal("SYSTEM_NO_PREVIEW_LOADED", viewModel.ErrorCode);
    }

    [Fact]
    public async Task CancellationAfterCommitRefreshesTheAuthoritativeRevision()
    {
        var response = new TaskCompletionSource<GatewayResult<CalibrationResultDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new WorkflowGateway { PendingCalibration = response.Task };
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Research workspace", TestContext.Current.CancellationToken);
        int previousRevision = viewModel.Run!.Revision;
        using var cancellation = new CancellationTokenSource();
        Task<bool> operation = viewModel.RunCalibrationAsync(cancellation.Token);
        Assert.Equal(previousRevision + 1, gateway.Revision);
        cancellation.Cancel();
        response.SetCanceled(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.Equal(gateway.Revision, viewModel.Run.Revision);
        Assert.Equal(RunStateNames.CalibrationPassed, viewModel.RunState);
        Assert.False(viewModel.IsBusy);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class WorkflowGateway : IFluoAxisGateway
    {
        private static readonly Guid RunId = Guid.Parse("0199a2b4-4c00-7000-8000-000000000001");
        public List<CommandBase> Commands { get; } = [];
        public int Revision { get; private set; } = 7;
        public string State { get; private set; } = RunStateNames.New;
        public int GetRunCalls { get; private set; }
        public InspectImportCommand? Inspection { get; private set; }
        public ImportPreviewDto Preview { get; } = new("xlsx", ["FAM", "ROX"],
            [new("FAM", 2, 0, [new(1, "A1")], ChannelNames.Fam), new("ROX", 4, 0, [new(1, "A1")], ChannelNames.Rox)]);
        public Task<GatewayResult<CalibrationResultDto>>? PendingCalibration { get; init; }
        public bool RejectCalibrationAsStale { get; init; }

        public Task<GatewayResult<ProjectSnapshotDto>> CreateProjectAsync(CreateProjectCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(StubGateway.Project(command.ProjectName));
        }

        public Task<GatewayResult<ImportPreviewDto>> InspectImportAsync(InspectImportCommand command, IProgress<OperationProgressDto>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inspection = command;
            progress?.Report(new OperationProgressDto(ImportStageNames.Validating, 100));
            return Task.FromResult(GatewayResult.Ok(Preview));
        }

        public Task<GatewayResult<RunSnapshotDto>> ConfirmMappingAsync(ConfirmMappingCommand command, CancellationToken cancellationToken)
        {
            Commit(command, RunStateNames.Imported, cancellationToken);
            return Task.FromResult(GatewayResult.Ok(Snapshot()));
        }

        public Task<GatewayResult<RunSnapshotDto>> AssignWellsAsync(AssignWellsCommand command, CancellationToken cancellationToken)
        {
            Commit(command, RunStateNames.Mapped, cancellationToken);
            return Task.FromResult(GatewayResult.Ok(Snapshot()));
        }

        public Task<GatewayResult<CalibrationResultDto>> RunCalibrationAsync(RunCalibrationCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RejectCalibrationAsStale)
            {
                Revision++;
                return Task.FromResult(GatewayResult.Fail<CalibrationResultDto>(
                    new AppErrorDto(GatewayErrorCodes.StaleRunRevision, SeverityNames.Blocking, "The Run changed.", "Refresh and retry.")));
            }
            Commit(command, RunStateNames.CalibrationPassed, cancellationToken);
            return PendingCalibration ?? Task.FromResult(GatewayResult.Ok(new CalibrationResultDto(StatusNames.Pass)));
        }

        public Task<GatewayResult<ControlQcResultDto>> RunControlQcAsync(RunControlQcCommand command, CancellationToken cancellationToken)
        {
            Commit(command, RunStateNames.ReadyForAnalysis, cancellationToken);
            return Task.FromResult(GatewayResult.Ok(new ControlQcResultDto(StatusNames.Pass)));
        }

        public Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesAsync(AnalyzeSamplesCommand command, CancellationToken cancellationToken)
        {
            Commit(command, RunStateNames.Analyzed, cancellationToken);
            return Task.FromResult(GatewayResult.Ok<IReadOnlyList<SampleAnalysisDto>>([new("S-1", StatusNames.Pass, CallNames.Positive, 0.5)]));
        }

        public Task<GatewayResult<RunSnapshotDto>> GetRunAsync(GetRunQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetRunCalls++;
            return Task.FromResult(GatewayResult.Ok(Snapshot()));
        }

        private void Commit(CommandBase command, string state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(Revision, command.ExpectedRunRevision);
            Commands.Add(command);
            Revision++;
            State = state;
        }

        private RunSnapshotDto Snapshot() => new(RunId, Revision, State,
            [RunAction.Import, RunAction.ConfirmMapping, RunAction.AssignWells, RunAction.RunCalibration, RunAction.RunControlQc, RunAction.AnalyzeSamples],
            [], [], "BRAF-V600E-Research", "1.0", new string('a', 64), [], updatedUtc: FixedTimeProvider.Now);
    }

    private sealed class StubGateway : IFluoAxisGateway
    {
        private int createCalls;
        public GatewayResult<ProjectSnapshotDto>? CreateResult { get; init; }
        public Task<GatewayResult<ProjectSnapshotDto>>? FirstCreate { get; init; }

        public Task<GatewayResult<ProjectSnapshotDto>> CreateProjectAsync(CreateProjectCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref createCalls) == 1 && FirstCreate is not null) return FirstCreate;
            return Task.FromResult(CreateResult ?? Project(command.ProjectName));
        }

        public Task<GatewayResult<RunSnapshotDto>> GetRunAsync(GetRunQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GatewayResult.Ok(new RunSnapshotDto(
                RunId, 0, RunStateNames.New, [RunAction.Import, RunAction.ConfirmMapping], [], [],
                "BRAF-V600E-Research", "1.0", new string('a', 64), [], updatedUtc: DateTimeOffset.UnixEpoch)));
        }

        public static GatewayResult<ProjectSnapshotDto> Project(string name) => GatewayResult.Ok(
            new ProjectSnapshotDto(Guid.NewGuid(), name, RunId, 0, RunStateNames.New, DateTimeOffset.UnixEpoch));

        public Task<GatewayResult<ImportPreviewDto>> InspectImportAsync(InspectImportCommand command, IProgress<OperationProgressDto>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GatewayResult<RunSnapshotDto>> ConfirmMappingAsync(ConfirmMappingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GatewayResult<RunSnapshotDto>> AssignWellsAsync(AssignWellsCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GatewayResult<CalibrationResultDto>> RunCalibrationAsync(RunCalibrationCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GatewayResult<ControlQcResultDto>> RunControlQcAsync(RunControlQcCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesAsync(AnalyzeSamplesCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
