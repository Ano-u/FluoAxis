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
