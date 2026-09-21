namespace FluoAxis.Application.Tests;

using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using Xunit;

public sealed class ApplicationGatewayTests
{
    private static readonly DateTimeOffset IssuedUtc = new(2026, 9, 3, 1, 2, 3, TimeSpan.Zero);
    private const string ValidSha256 = "a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00";

    [Fact]
    public async Task CreateProject_CreatesNewRunWithBackendDerivedSnapshot()
    {
        var gateway = CreateGateway();
        var command = CreateCommand(Guid.NewGuid());

        GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(command, CancellationToken.None);
        GatewayResult<RunSnapshotDto> fetched = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), created.Value!.RunId), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal(0, created.Value.RunRevision);
        Assert.Equal(RunStateNames.New, created.Value.RunDomainState);
        Assert.True(fetched.IsSuccess);
        Assert.Equal([RunAction.Import], fetched.Value!.AllowedActions);
        Assert.Equal("BRAF-V600E-Research", fetched.Value.MethodId);
        Assert.Equal("1.0", fetched.Value.MethodVersion);
        Assert.Equal(64, fetched.Value.MethodSnapshotHash.Length);
        Assert.Empty(fetched.Value.SourceFiles);
    }

    [Fact]
    public async Task CreateProject_ReplaysSameRequestWithoutCreatingAnotherRun()
    {
        var gateway = CreateGateway();
        CreateProjectCommand command = CreateCommand(Guid.NewGuid());

        GatewayResult<ProjectSnapshotDto> first = await gateway.CreateProjectAsync(command, CancellationToken.None);
        GatewayResult<ProjectSnapshotDto> replay = await gateway.CreateProjectAsync(command, CancellationToken.None);

        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
    }

    [Fact]
    public async Task CreateProject_IsAtomicForConcurrentRequestReplay()
    {
        var gateway = CreateGateway();
        CreateProjectCommand command = CreateCommand(Guid.NewGuid());

        GatewayResult<ProjectSnapshotDto>[] results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => gateway.CreateProjectAsync(command, CancellationToken.None)));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Single(results.Select(result => result.Value!.ProjectId).Distinct());
        Assert.Single(results.Select(result => result.Value!.RunId).Distinct());
    }

    [Fact]
    public async Task CreateProject_RejectsRequestIdReuseWithDifferentPayload()
    {
        var gateway = CreateGateway();
        Guid requestId = Guid.NewGuid();
        await gateway.CreateProjectAsync(CreateCommand(requestId), CancellationToken.None);

        var conflicting = new CreateProjectCommand(requestId, "Different", "researcher", IssuedUtc);
        GatewayResult<ProjectSnapshotDto> result = await gateway.CreateProjectAsync(conflicting, CancellationToken.None);

        AssertFailure(result, ErrorCodes.SystemRequestIdConflict);
    }

    [Fact]
    public async Task Gateway_RejectsUnsupportedContractVersionBeforeStateLookup()
    {
        var gateway = CreateGateway();
        var query = new GetRunQuery(Guid.NewGuid(), Guid.NewGuid(), contractVersion: 999);

        GatewayResult<RunSnapshotDto> result = await gateway.GetRunAsync(query, CancellationToken.None);

        AssertFailure(result, ErrorCodes.SystemUnsupportedContractVersion);
    }

    [Fact]
    public async Task GetRun_ReturnsStructuredNotFoundFailure()
    {
        var gateway = CreateGateway();

        GatewayResult<RunSnapshotDto> result = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        AssertFailure(result, ErrorCodes.ProjectRunNotFound);
    }

    [Theory]
    [MemberData(nameof(AllowedActionCases))]
    public async Task GetRun_DerivesAllowedActionsFromApplicationOwnedState(
        RunState state,
        string[] expectedActions)
    {
        ProjectRunAggregate aggregate = Aggregate(state);
        var gateway = new ApplicationGateway(new FixedAggregateRepository(aggregate));

        GatewayResult<RunSnapshotDto> result = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), aggregate.RunId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RunStateNames.FromDomain(state), result.Value!.DomainState);
        Assert.Equal(expectedActions, result.Value.AllowedActions);
    }

    [Fact]
    public async Task Mutation_RejectsStaleRevisionBeforeStateGuard()
    {
        var gateway = CreateGateway();
        ProjectSnapshotDto created = (await gateway.CreateProjectAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None)).Value!;
        ConfirmMappingCommand command = CreateMappingCommand(created.RunId, expectedRevision: 1);

        GatewayResult<RunSnapshotDto> result = await gateway.ConfirmMappingAsync(command, CancellationToken.None);

        AssertFailure(result, ErrorCodes.SystemStaleRunRevision);
    }

    [Fact]
    public async Task Mutation_RejectsOperationOutsideExpectedState()
    {
        var gateway = CreateGateway();
        ProjectSnapshotDto created = (await gateway.CreateProjectAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None)).Value!;

        // The run is New, but assignment requires Imported: a genuine state
        // refusal, and one that needs no adapter to reach.
        GatewayResult<RunSnapshotDto> result = await gateway.AssignWellsAsync(
            new AssignWellsCommand(
                Guid.NewGuid(), created.RunId, 0, "researcher", IssuedUtc,
                [new WellAssignmentDto("A1", WellRoleNames.Ntc)]),
            CancellationToken.None);

        AssertFailure(result, ErrorCodes.SystemInvalidRunState);
    }

    [Fact]
    public async Task InspectImport_FailsExplicitlyUntilTransientSourcePortExists()
    {
        var gateway = CreateGateway();
        ProjectSnapshotDto created = (await gateway.CreateProjectAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None)).Value!;
        var command = new InspectImportCommand(Guid.NewGuid(), created.RunId, "run.xlsx", 42, "data/raw/run.xlsx");

        GatewayResult<ImportPreviewDto> result = await gateway.InspectImportAsync(command, null, CancellationToken.None);

        AssertFailure(result, ErrorCodes.SystemCapabilityUnavailable);
    }

    [Fact]
    public async Task InspectImport_ValidatesVersionThenRunThenStateBeforePort()
    {
        var port = new RecordingInspectionPort(Preview());
        ProjectRunAggregate imported = Aggregate(RunState.Imported);
        var gateway = new ApplicationGateway(new FixedAggregateRepository(imported), importInspection: port);

        GatewayResult<ImportPreviewDto> wrongVersion = await gateway.InspectImportAsync(
            InspectionCommand(Guid.NewGuid(), contractVersion: 999), null, CancellationToken.None);
        GatewayResult<ImportPreviewDto> missing = await gateway.InspectImportAsync(
            InspectionCommand(Guid.NewGuid()), null, CancellationToken.None);
        GatewayResult<ImportPreviewDto> wrongState = await gateway.InspectImportAsync(
            InspectionCommand(imported.RunId), null, CancellationToken.None);

        AssertFailure(wrongVersion, ErrorCodes.SystemUnsupportedContractVersion);
        AssertFailure(missing, ErrorCodes.ProjectRunNotFound);
        AssertFailure(wrongState, ErrorCodes.SystemInvalidRunState);
        Assert.Equal(0, port.CallCount);
    }

    [Fact]
    public async Task InspectImport_DelegatesManagedIdentityAndDoesNotMutateRun()
    {
        var repository = new InMemoryProjectRunRepository();
        var port = new RecordingInspectionPort(Preview());
        var gateway = new ApplicationGateway(repository, new FixedTimeProvider(IssuedUtc.AddMinutes(1)), port);
        ProjectSnapshotDto project = (await gateway.CreateProjectAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None)).Value!;
        var progress = new Progress<OperationProgressDto>();

        GatewayResult<ImportPreviewDto> inspected = await gateway.InspectImportAsync(
            InspectionCommand(project.RunId), progress, CancellationToken.None);
        GatewayResult<RunSnapshotDto> run = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), project.RunId), CancellationToken.None);

        Assert.True(inspected.IsSuccess);
        Assert.Equal("delimitedText", inspected.Value!.DetectedFormat);
        Assert.Equal(["text-body"], inspected.Value.SheetNames);
        Assert.Equal("A1", Assert.Single(Assert.Single(inspected.Value.CandidateRegions).WellColumns).Well);
        Assert.Equal(1, port.CallCount);
        Assert.Equal(project.RunId, port.LastRequest!.RunId);
        Assert.Equal("run.csv", port.LastRequest.DisplayName);
        Assert.Equal(42, port.LastRequest.DeclaredSizeBytes);
        Assert.Equal("data/raw/run.csv", port.LastRequest.ResourcePath.Value);
        Assert.Same(progress, port.LastProgress);
        Assert.Equal(RunStateNames.New, run.Value!.DomainState);
        Assert.Equal(0, run.Value.Revision);
    }

    [Fact]
    public async Task InspectImport_PropagatesPortErrorsAndCancellation()
    {
        var repository = new InMemoryProjectRunRepository();
        var port = new RecordingInspectionPort(Preview()) { Error = new AppError(
            ErrorCodes.ImportManagedResourceNotFound, ErrorSeverity.Blocking, "Resource missing.", "resourceMissing") };
        var gateway = new ApplicationGateway(repository, importInspection: port);
        ProjectSnapshotDto project = (await gateway.CreateProjectAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None)).Value!;

        GatewayResult<ImportPreviewDto> failed = await gateway.InspectImportAsync(
            InspectionCommand(project.RunId), null, CancellationToken.None);
        AssertFailure(failed, ErrorCodes.ImportManagedResourceNotFound);
        Assert.False(string.IsNullOrWhiteSpace(failed.Errors[0].RecommendedAction));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.InspectImportAsync(InspectionCommand(project.RunId), null, cancellation.Token));
        Assert.Equal(1, port.CallCount);
    }

    [Fact]
    public async Task Gateway_HonorsPreCanceledTokenWithoutMutation()
    {
        var gateway = CreateGateway();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.CreateProjectAsync(CreateCommand(Guid.NewGuid()), cancellation.Token));
    }

    private static ApplicationGateway CreateGateway() => new(
        new InMemoryProjectRunRepository(),
        new FixedTimeProvider(IssuedUtc.AddMinutes(1)));

    private static CreateProjectCommand CreateCommand(Guid requestId) =>
        new(requestId, "Research Alpha", "researcher", IssuedUtc);

    private static ConfirmMappingCommand CreateMappingCommand(Guid runId, int expectedRevision)
    {
        WellColumnMappingDto[] wells = [new(1, "A1")];
        return new ConfirmMappingCommand(
            Guid.NewGuid(),
            runId,
            expectedRevision,
            "researcher",
            IssuedUtc,
            new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", 0, 0, wells),
            new ChannelRegionMappingDto(ChannelNames.Rox, "ROX", 0, 0, wells),
            "run.csv",
            "data/raw/run.csv",
            ValidSha256,
            42,
            SourceFileDto.MediaTypeNames.Csv);
    }

    private static InspectImportCommand InspectionCommand(Guid runId, int contractVersion = ContractVersion.Current) =>
        new(Guid.NewGuid(), runId, "run.csv", 42, "data/raw/run.csv", "csv", contractVersion);

    private static ImportPreviewDto Preview() => new(
        "delimitedText", ["text-body"],
        [new RegionCandidateDto("text-body", 0, 0, [new WellColumnMappingDto(1, "A1")])]);

    public static TheoryData<RunState, string[]> AllowedActionCases => new()
    {
        { RunState.New, [RunAction.Import] },
        { RunState.Imported, [RunAction.ConfirmMapping, RunAction.ViewExplorer] },
        { RunState.Mapped, [RunAction.AssignWells, RunAction.ViewExplorer] },
        { RunState.Calibrating, [RunAction.ViewExplorer] },
        { RunState.CalibrationFailed, [RunAction.ViewExplorer] },
        { RunState.CalibrationPassed, [RunAction.RunControlQc, RunAction.ViewExplorer] },
        { RunState.ControlQc, [RunAction.ViewExplorer] },
        { RunState.ControlQcFailed, [RunAction.ViewExplorer] },
        { RunState.ReadyForAnalysis, [RunAction.AnalyzeSamples, RunAction.ViewExplorer] },
        { RunState.Analyzing, [RunAction.ViewExplorer] },
        { RunState.Analyzed, [RunAction.Export, RunAction.ViewExplorer] },
        { RunState.ReplicateConflict, [RunAction.ViewExplorer] },
        { RunState.Exported, [RunAction.ViewExplorer] },
        { RunState.RunInvalid, [RunAction.ViewExplorer] },
    };

    private static ProjectRunAggregate Aggregate(RunState state) => new()
    {
        ProjectId = Guid.NewGuid(),
        ProjectName = "State fixture",
        CreatedAtUtc = IssuedUtc,
        RunId = Guid.NewGuid(),
        RunRevision = 7,
        RunState = state,
        Method = BrafMethodSnapshot.Default,
        UpdatedUtc = IssuedUtc,
    };

    private static void AssertFailure<T>(GatewayResult<T> result, string expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Single(result.Errors);
        Assert.Equal(expectedCode, result.Errors[0].Code);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    /// <summary>
    /// A repository frozen at one aggregate that refuses every mutation, used
    /// to prove the pre-flight guards fire before any state can change.
    /// </summary>
    private sealed class FixedAggregateRepository(ProjectRunAggregate aggregate) : IProjectRunRepository
    {
        public ValueTask<CreateProjectStoreResult> CreateOrReplayAsync(
            CreateProjectIdentity identity,
            Func<ProjectRunAggregate> aggregateFactory,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ProjectRunAggregate?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ProjectRunAggregate?>(runId == aggregate.RunId ? aggregate : null);
        }

        public ValueTask<RunMutationOutcome> MutateAsync(
            RunMutationRequest request,
            RunWorkflowPayload payload,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<PreparedSourceIdentity?> GetSourceAsync(Guid runId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<PreparedSourceIdentity?>(null);

        public ValueTask<IReadOnlyList<SignalSeries>> GetSignalsAsync(Guid runId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<SignalSeries>>([]);

        public ValueTask<IReadOnlyList<WellAssignment>> GetAssignmentsAsync(Guid runId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<WellAssignment>>([]);

        public ValueTask<PersistedCalibration?> GetCalibrationAsync(Guid runId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<PersistedCalibration?>(null);

        public ValueTask<PersistedControl?> GetControlAsync(Guid runId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<PersistedControl?>(null);

        public ValueTask<IReadOnlyList<PersistedSample>> GetSamplesAsync(Guid runId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<PersistedSample>>([]);

        public ValueTask<IReadOnlyList<PersistedTrace>> GetTracesAsync(Guid runId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<PersistedTrace>>([]);
    }

    private sealed class RecordingInspectionPort(ImportPreviewDto preview) : IImportInspectionPort
    {
        public int CallCount { get; private set; }
        public ManagedImportRequest? LastRequest { get; private set; }
        public IProgress<OperationProgressDto>? LastProgress { get; private set; }
        public AppError? Error { get; init; }

        public Task<Result<ImportPreviewDto>> InspectAsync(
            ManagedImportRequest request,
            IProgress<OperationProgressDto>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;
            LastProgress = progress;
            return Task.FromResult(Error is null
                ? Result<ImportPreviewDto>.Ok(preview)
                : Result<ImportPreviewDto>.Fail(Error));
        }
    }
}
