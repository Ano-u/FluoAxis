namespace FluoAxis.GatewayContract.Tests;

using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Xunit;

public sealed class AutomaticAnalysisTests
{
    private static readonly string[] Wells = ["A1", "A2", "A3", "A4", "A5"];
    private static readonly string[] Roles = [WellRoleNames.Ntc, WellRoleNames.Positive100, WellRoleNames.WildType, WellRoleNames.Lqc01Percent, WellRoleNames.Sample];
    private static readonly SourceFileDto Source = new("run.csv", "data/raw/run.csv", new string('a', 64), 100, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Automatic_chain_refreshes_every_revision_and_stays_busy_between_steps()
    {
        var gateway = new Gateway();
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Automatic", TestContext.Current.CancellationToken);
        var progress = new InlineProgress(_ =>
        {
            Assert.True(viewModel.IsBusy); Assert.False(viewModel.CanImport); Assert.False(viewModel.CanRunCalibration);
            viewModel.ShowEmptyWorkspace(); Assert.NotNull(viewModel.Run);
        });

        Assert.True(await viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(), TestContext.Current.CancellationToken, progress));

        Assert.Equal([0, 1, 2, 3, 4], gateway.Commands.Select(command => command.ExpectedRunRevision));
        Assert.Equal(5, viewModel.Run!.Revision); Assert.Equal("completed", viewModel.AutomaticStage);
        Assert.False(viewModel.IsBusy); Assert.Equal(6, gateway.Reads);
    }

    [Theory]
    [InlineData("calibration", 3)]
    [InlineData("control", 4)]
    [InlineData("analysis", 5)]
    public async Task Scientific_failure_or_conflict_stops_the_chain_with_saved_invalid_state(string stage, int commandCount)
    {
        var gateway = new Gateway { FailStage = stage };
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Invalid", TestContext.Current.CancellationToken);

        Assert.False(await viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(), TestContext.Current.CancellationToken));

        Assert.Equal(commandCount, gateway.Commands.Count); Assert.Equal(RunStateNames.RunInvalid, viewModel.RunState);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Stale_response_stops_without_issuing_control_or_sample_commands()
    {
        var gateway = new Gateway { StaleCalibration = true };
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Stale", TestContext.Current.CancellationToken);

        Assert.False(await viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(), TestContext.Current.CancellationToken));

        Assert.Equal(3, gateway.Commands.Count); Assert.Equal(GatewayErrorCodes.StaleRunRevision, viewModel.ErrorCode);
        Assert.Equal(3, viewModel.Run!.Revision); Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Cancellation_after_a_committed_stage_refreshes_revision_and_rejects_other_requests()
    {
        var response = new TaskCompletionSource<GatewayResult<CalibrationResultDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new Gateway { PendingCalibration = response.Task };
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Cancellation", TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource();
        Task<bool> automatic = viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(), cancel.Token);
        Assert.True(viewModel.IsBusy); Assert.Equal(3, gateway.Commands.Count);
        Assert.False(await viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(), CancellationToken.None));
        Assert.False(await viewModel.RunControlQcAsync(CancellationToken.None));
        Assert.False(await viewModel.LoadRunAsync(Guid.NewGuid(), CancellationToken.None));
        await viewModel.InitializeAsync("Forbidden replacement", CancellationToken.None);
        Assert.Equal(1, gateway.Creates);
        cancel.Cancel(); response.SetCanceled(cancel.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => automatic);

        Assert.Equal(3, gateway.Commands.Count); Assert.Equal(3, viewModel.Run!.Revision);
        Assert.Equal(RunStateNames.CalibrationPassed, viewModel.RunState); Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Profile_resolves_only_roles_and_cannot_override_mapping_ambiguity()
    {
        var gateway = new Gateway();
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Profile", TestContext.Current.CancellationToken);
        PlateProfileDto profile = Profile();
        Assert.False(await viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(mappingReady: false, incompleteRoles: true), CancellationToken.None, profile: profile));
        Assert.Empty(gateway.Commands);
        Assert.True(await viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(incompleteRoles: true), CancellationToken.None, profile: profile));
        AssignWellsCommand assigned = Assert.IsType<AssignWellsCommand>(gateway.Commands[1]);
        Assert.Equal("A5", Assert.Single(assigned.Assignments, well => well.Role == WellRoleNames.Sample).SampleId);
    }

    [Fact]
    public async Task Missing_roles_require_review_before_any_backend_mutation()
    {
        var gateway = new Gateway();
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Review", TestContext.Current.CancellationToken);
        Assert.False(await viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(incompleteRoles: true), CancellationToken.None));
        Assert.Empty(gateway.Commands); Assert.Equal("PLATE_AUTO_REVIEW_REQUIRED", viewModel.ErrorCode);
    }

    [Fact]
    public async Task Profile_preserves_current_file_sample_metadata_instead_of_old_or_technical_identity()
    {
        var gateway = new Gateway();
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Explicit metadata", CancellationToken.None);
        Assert.True(await viewModel.AnalyzeAutomaticallyAsync(Source, Proposal(), CancellationToken.None, profile: Profile()));
        Assert.Equal("ExplicitSample", Assert.Single(Assert.IsType<AssignWellsCommand>(gateway.Commands[1]).Assignments,
            item => item.Role == WellRoleNames.Sample).SampleId);
    }

    [Theory]
    [InlineData("IMPORT_AUTO_ROLE_CONFLICT")]
    [InlineData("IMPORT_AUTO_SAMPLE_METADATA_REQUIRED")]
    public async Task Profile_does_not_override_explicit_role_conflicts_or_partial_sample_identity(string issueCode)
    {
        var gateway = new Gateway();
        var viewModel = new WorkbenchViewModel(gateway);
        await viewModel.InitializeAsync("Conflicting labels", CancellationToken.None);
        AutomaticImportProposalDto source = Proposal();
        var conflicting = new AutomaticImportProposalDto(source.DetectedFormat, source.Options, source.Fam, source.Rox, source.Assignments,
            [new(issueCode, "Incomplete or conflicting metadata.", "Review.")], [], true, true);
        Assert.False(await viewModel.AnalyzeAutomaticallyAsync(Source, conflicting, CancellationToken.None, profile: Profile()));
        Assert.Empty(gateway.Commands);
    }

    [Fact]
    public void Profile_application_preserves_explicit_identity_and_rejects_different_roles()
    {
        var source = Proposal();
        var applied = PlateProfileApplication.Apply(Profile(), Wells, source.Assignments);
        Assert.True(applied.IsSuccess);
        Assert.Equal("ExplicitSample", Assert.Single(applied.Value!, item => item.Role == WellRoleNames.Sample).SampleId);
        var conflicting = new[] { new WellAssignmentDto("A5", WellRoleNames.Ntc) };
        var rejected = PlateProfileApplication.Apply(Profile(), Wells, conflicting);
        Assert.False(rejected.IsSuccess);
        Assert.Equal("PLATE_AUTO_PROFILE_ROLE_CONFLICT", Assert.Single(rejected.Errors).Code);
    }

    [Fact]
    public void Profile_reuse_requires_every_declared_control_but_allows_absent_sample_wells()
    {
        PlateProfileDto profile = Profile(extraSample: true);
        Assert.True(PlateProfileApplication.Apply(profile, Wells).IsSuccess);
        Assert.False(PlateProfileApplication.Apply(profile, Wells[1..]).IsSuccess);
        PlateProfileDto strict = new(Guid.NewGuid(), new("Strict", Profile().Definition.Wells, false), new string('a', 64), DateTimeOffset.UnixEpoch);
        Assert.False(PlateProfileApplication.Apply(strict, Wells).IsSuccess);
    }

    private static AutomaticImportProposalDto Proposal(bool mappingReady = true, bool incompleteRoles = false) => new("delimitedText", new(),
        new(ChannelNames.Fam, "text-body", 0, 0, Wells.Select((well, index) => new WellColumnMappingDto(index + 1, well)).ToArray()),
        new(ChannelNames.Rox, "text-body", 0, 0, Wells.Select((well, index) => new WellColumnMappingDto(index + 6, well)).ToArray()),
        incompleteRoles ? [] : Wells.Select((well, index) => new WellAssignmentDto(well, Roles[index], index == 4 ? "ExplicitSample" : null, replicateIndex: index == 4 ? 1 : null)).ToArray(),
        [], [], mappingReady, !mappingReady || incompleteRoles);
    private static PlateProfileDto Profile(bool extraSample = false) => new(Guid.NewGuid(), new("Fixed controls",
        [.. Wells.Take(4).Select((well, index) => new PlateProfileWellDto(well, Roles[index])), .. extraSample ? new[] { new PlateProfileWellDto("A6", WellRoleNames.Sample) } : []], true, new()), new string('a', 64), DateTimeOffset.UnixEpoch);
    private sealed class InlineProgress(Action<OperationProgressDto> report) : IProgress<OperationProgressDto> { public void Report(OperationProgressDto value) => report(value); }

    private sealed class Gateway : IFluoAxisGateway
    {
        private readonly Guid runId = Guid.NewGuid();
        private int revision;
        private string state = RunStateNames.New;
        public List<CommandBase> Commands { get; } = [];
        public int Reads { get; private set; }
        public int Creates { get; private set; }
        public string? FailStage { get; init; }
        public bool StaleCalibration { get; init; }
        public Task<GatewayResult<CalibrationResultDto>>? PendingCalibration { get; init; }
        public Task<GatewayResult<ProjectSnapshotDto>> CreateProjectAsync(CreateProjectCommand command, CancellationToken cancellationToken)
        { Creates++; return Task.FromResult(GatewayResult.Ok(new ProjectSnapshotDto(Guid.NewGuid(), command.ProjectName, runId, 0, state, DateTimeOffset.UnixEpoch))); }
        public Task<GatewayResult<RunSnapshotDto>> GetRunAsync(GetRunQuery query, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Reads++; return Task.FromResult(GatewayResult.Ok(Snapshot())); }
        public Task<GatewayResult<ImportPreviewDto>> InspectImportAsync(InspectImportCommand command, IProgress<OperationProgressDto>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GatewayResult<RunSnapshotDto>> ConfirmMappingAsync(ConfirmMappingCommand command, CancellationToken cancellationToken)
        { Commit(command, RunStateNames.Imported); return Task.FromResult(GatewayResult.Ok(Snapshot())); }
        public Task<GatewayResult<RunSnapshotDto>> AssignWellsAsync(AssignWellsCommand command, CancellationToken cancellationToken)
        { Commit(command, RunStateNames.Mapped); return Task.FromResult(GatewayResult.Ok(Snapshot())); }
        public Task<GatewayResult<CalibrationResultDto>> RunCalibrationAsync(RunCalibrationCommand command, CancellationToken cancellationToken)
        {
            Commit(command, FailStage == "calibration" ? RunStateNames.RunInvalid : RunStateNames.CalibrationPassed);
            return StaleCalibration ? Task.FromResult(GatewayResult.Fail<CalibrationResultDto>(new AppErrorDto(GatewayErrorCodes.StaleRunRevision, SeverityNames.Blocking, "Stale.")))
                : PendingCalibration ?? Task.FromResult(GatewayResult.Ok(new CalibrationResultDto(FailStage == "calibration" ? StatusNames.Fail : StatusNames.Pass)));
        }
        public Task<GatewayResult<ControlQcResultDto>> RunControlQcAsync(RunControlQcCommand command, CancellationToken cancellationToken)
        { Commit(command, FailStage == "control" ? RunStateNames.RunInvalid : RunStateNames.ReadyForAnalysis); return Task.FromResult(GatewayResult.Ok(new ControlQcResultDto(FailStage == "control" ? StatusNames.Invalid : StatusNames.Pass))); }
        public Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesAsync(AnalyzeSamplesCommand command, CancellationToken cancellationToken)
        {
            Commit(command, FailStage == "analysis" ? RunStateNames.RunInvalid : RunStateNames.Analyzed);
            return Task.FromResult(GatewayResult.Ok<IReadOnlyList<SampleAnalysisDto>>([new("A5", FailStage == "analysis" ? StatusNames.Invalid : StatusNames.Pass,
                FailStage == "analysis" ? null : CallNames.Positive, FailStage == "analysis" ? null : 0.6)]));
        }
        private void Commit(CommandBase command, string next) { Assert.Equal(revision, command.ExpectedRunRevision); Commands.Add(command); revision++; state = next; }
        private RunSnapshotDto Snapshot() => new(runId, revision, state, state switch
        {
            RunStateNames.New => [RunAction.Import, RunAction.ConfirmMapping], RunStateNames.Imported => [RunAction.AssignWells],
            RunStateNames.Mapped => [RunAction.RunCalibration], RunStateNames.CalibrationPassed => [RunAction.RunControlQc],
            RunStateNames.ReadyForAnalysis => [RunAction.AnalyzeSamples], _ => [RunAction.ViewExplorer],
        }, [], [], "BRAF-V600E-Research", "1.0", new string('a', 64), [], updatedUtc: DateTimeOffset.UnixEpoch);
    }
}
