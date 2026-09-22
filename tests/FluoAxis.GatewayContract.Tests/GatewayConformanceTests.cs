namespace FluoAxis.GatewayContract.Tests;

using System.Text.Json;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Presentation;
using Xunit;

public sealed class GatewayConformanceTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);
    private const string ValidSha256 = "a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00";

    public static TheoryData<string> AdapterNames => new() { "fake", "application" };

    [Theory]
    [MemberData(nameof(AdapterNames))]
    public async Task Create_get_and_empty_new_run_conform(string adapterName)
    {
        IFluoAxisGateway gateway = CreateGateway(adapterName);
        CreateProjectCommand command = CreateCommand(Guid.NewGuid());

        GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(command, TestContext.Current.CancellationToken);
        GatewayResult<RunSnapshotDto> fetched = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), created.Value!.RunId), TestContext.Current.CancellationToken);

        Assert.True(created.IsSuccess);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(0, fetched.Value!.Revision);
        Assert.Equal(RunStateNames.New, fetched.Value.DomainState);
        Assert.Equal([RunAction.Import, RunAction.ConfirmMapping], fetched.Value.AllowedActions);
        Assert.Empty(fetched.Value.SourceFiles);
        Assert.Empty(fetched.Value.BlockingIssues);
        Assert.Empty(fetched.Value.WarningIssues);
        Assert.Null(fetched.Value.MappingSummary);
        Assert.Null(fetched.Value.CalibrationSummary);
        Assert.Null(fetched.Value.ControlSummary);
        Assert.Null(fetched.Value.AnalysisSummary);
        Assert.Equal(BrafMethodSnapshot.Default.MethodId, fetched.Value.MethodId);
        Assert.Equal(BrafMethodSnapshot.Default.ComputeCanonicalHash(), fetched.Value.MethodSnapshotHash);
    }

    [Theory]
    [MemberData(nameof(AdapterNames))]
    public async Task Exact_create_replay_and_conflicting_reuse_conform(string adapterName)
    {
        IFluoAxisGateway gateway = CreateGateway(adapterName);
        Guid requestId = Guid.NewGuid();
        CreateProjectCommand command = CreateCommand(requestId);

        GatewayResult<ProjectSnapshotDto> first = await gateway.CreateProjectAsync(command, TestContext.Current.CancellationToken);
        GatewayResult<ProjectSnapshotDto> replay = await gateway.CreateProjectAsync(command, TestContext.Current.CancellationToken);
        GatewayResult<ProjectSnapshotDto> conflict = await gateway.CreateProjectAsync(
            new CreateProjectCommand(requestId, "Different", "researcher", FixedUtc), TestContext.Current.CancellationToken);

        Assert.Equal(first.Value, replay.Value);
        AssertFailure(conflict, GatewayErrorCodes.RequestIdConflict);
    }

    [Theory]
    [MemberData(nameof(AdapterNames))]
    public async Task Concurrent_create_replay_produces_one_project_and_run(string adapterName)
    {
        IFluoAxisGateway gateway = CreateGateway(adapterName);
        CreateProjectCommand command = CreateCommand(Guid.NewGuid());

        GatewayResult<ProjectSnapshotDto>[] results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => gateway.CreateProjectAsync(command, TestContext.Current.CancellationToken)));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Single(results.Select(result => result.Value!.ProjectId).Distinct());
        Assert.Single(results.Select(result => result.Value!.RunId).Distinct());
    }

    [Theory]
    [MemberData(nameof(AdapterNames))]
    public async Task Not_found_and_unsupported_version_conform(string adapterName)
    {
        IFluoAxisGateway gateway = CreateGateway(adapterName);

        GatewayResult<RunSnapshotDto> missing = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);
        GatewayResult<RunSnapshotDto> unsupported = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), Guid.NewGuid(), 999), TestContext.Current.CancellationToken);

        AssertFailure(missing, GatewayErrorCodes.RunNotFound);
        AssertFailure(unsupported, GatewayErrorCodes.UnsupportedContractVersion);
    }

    [Theory]
    [MemberData(nameof(AdapterNames))]
    public async Task Pre_cancellation_conforms_without_creating_a_run(string adapterName)
    {
        IFluoAxisGateway gateway = CreateGateway(adapterName);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.CreateProjectAsync(CreateCommand(Guid.NewGuid()), cancellation.Token));
    }

    [Theory]
    [MemberData(nameof(AdapterNames))]
    public async Task Stale_invalid_state_and_unavailable_import_conform(string adapterName)
    {
        IFluoAxisGateway gateway = CreateGateway(adapterName);
        ProjectSnapshotDto project = (await gateway.CreateProjectAsync(
            CreateCommand(Guid.NewGuid()), TestContext.Current.CancellationToken)).Value!;

        // Stale revision is reported first even though the state is also wrong.
        GatewayResult<RunSnapshotDto> stale = await gateway.ConfirmMappingAsync(
            CreateMappingCommand(project.RunId, expectedRevision: 1), TestContext.Current.CancellationToken);

        // AssignWells expects Imported, so a New run is a genuine state refusal.
        GatewayResult<RunSnapshotDto> invalid = await gateway.AssignWellsAsync(
            new AssignWellsCommand(
                Guid.NewGuid(), project.RunId, 0, "researcher", FixedUtc,
                [new WellAssignmentDto("A1", WellRoleNames.Ntc)]),
            TestContext.Current.CancellationToken);

        GatewayResult<ImportPreviewDto> unavailable = await gateway.InspectImportAsync(
            new InspectImportCommand(Guid.NewGuid(), project.RunId, "run.xlsx", 42, "data/raw/run.xlsx"), null, TestContext.Current.CancellationToken);

        AssertFailure(stale, GatewayErrorCodes.StaleRunRevision);
        AssertFailure(invalid, GatewayErrorCodes.InvalidRunState);
        AssertFailure(unavailable, GatewayErrorCodes.CapabilityUnavailable);
    }

    [Theory]
    [MemberData(nameof(AdapterNames))]
    public async Task Mapping_confirmation_without_a_preparation_adapter_is_unavailable(string adapterName)
    {
        IFluoAxisGateway gateway = CreateGateway(adapterName);
        ProjectSnapshotDto project = (await gateway.CreateProjectAsync(
            CreateCommand(Guid.NewGuid()), TestContext.Current.CancellationToken)).Value!;

        // The run is genuinely New and the revision matches, so the only
        // reason confirmation cannot proceed is the missing staged-source
        // adapter. Reporting that as unavailable keeps the frontend honest.
        GatewayResult<RunSnapshotDto> result = await gateway.ConfirmMappingAsync(
            CreateMappingCommand(project.RunId, expectedRevision: 0), TestContext.Current.CancellationToken);

        AssertFailure(result, GatewayErrorCodes.CapabilityUnavailable);
    }

    [Theory]
    [MemberData(nameof(AdapterNames))]
    public async Task Successful_read_only_import_inspection_conforms_and_preserves_new_run(string adapterName)
    {
        ImportPreviewDto expected = InspectionPreview();
        IFluoAxisGateway gateway = CreateGateway(adapterName, expected);
        ProjectSnapshotDto project = (await gateway.CreateProjectAsync(
            CreateCommand(Guid.NewGuid()), TestContext.Current.CancellationToken)).Value!;

        GatewayResult<ImportPreviewDto> inspected = await gateway.InspectImportAsync(
            new InspectImportCommand(
                Guid.NewGuid(), project.RunId, "run.csv", 42, "data/raw/run.csv", "csv"),
            null,
            TestContext.Current.CancellationToken);
        GatewayResult<RunSnapshotDto> run = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), project.RunId), TestContext.Current.CancellationToken);

        Assert.True(inspected.IsSuccess);
        Assert.Equal(expected, inspected.Value);
        Assert.Equal(RunStateNames.New, run.Value!.DomainState);
        Assert.Equal(0, run.Value.Revision);
        Assert.Empty(run.Value.SourceFiles);
    }

    [Fact]
    public void Scenario_fixture_covers_the_required_current_baseline()
    {
        string root = FindRepositoryRoot();
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root, "samples", "contracts", "gateway-current-capability-v3.json")));
        JsonElement scenarios = fixture.RootElement.GetProperty("scenarios");
        string[] ids = [.. scenarios.EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];

        Assert.Equal(ContractVersion.Current, fixture.RootElement.GetProperty("contractVersion").GetInt32());
        string[] expectedIds =
        [
            "create-get-empty",
            "create-replay",
            "request-id-conflict",
            "run-not-found",
            "unsupported-version",
            "pre-canceled",
            "stale-revision",
            "invalid-run-state",
            "import-capability-unavailable",
            "managed-import-inspection",
        ];
        Assert.Equal(expectedIds, ids);
        Assert.All(scenarios.EnumerateArray(), scenario =>
        {
            string outcome = scenario.GetProperty("outcome").GetString()!;
            Assert.Contains(outcome, new[] { "success", "failure", "canceled" });
            if (outcome == "failure")
            {
                string errorCode = scenario.GetProperty("errorCode").GetString()!;
                Assert.True(
                    errorCode.StartsWith("SYSTEM_", StringComparison.Ordinal)
                    || errorCode.StartsWith("PROJECT_", StringComparison.Ordinal));
            }
        });
    }

    private static IFluoAxisGateway CreateGateway(string adapterName, ImportPreviewDto? preview = null) => adapterName switch
    {
        "fake" => new FakeFluoAxisGateway(
            new FakeMethodIdentity(
                BrafMethodSnapshot.Default.MethodId,
                BrafMethodSnapshot.Default.Version,
                BrafMethodSnapshot.Default.ComputeCanonicalHash()),
            new FixedTimeProvider(FixedUtc.AddMinutes(1)),
            importInspection: preview is null ? null : (_, _, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(GatewayResult.Ok(preview));
            }),
        "application" => new ApplicationGateway(
            new InMemoryProjectRunRepository(),
            new FixedTimeProvider(FixedUtc.AddMinutes(1)),
            preview is null ? null : new FixtureInspectionPort(preview)),
        _ => throw new ArgumentOutOfRangeException(nameof(adapterName)),
    };

    private static ImportPreviewDto InspectionPreview() => new(
        "delimitedText",
        ["text-body"],
        [new RegionCandidateDto("text-body", 0, 0, [new WellColumnMappingDto(1, "A1")])]);

    private static CreateProjectCommand CreateCommand(Guid requestId) =>
        new(requestId, "Research Alpha", "researcher", FixedUtc);

    private static ConfirmMappingCommand CreateMappingCommand(Guid runId, int expectedRevision)
    {
        WellColumnMappingDto[] wells = [new(1, "A1")];
        return new ConfirmMappingCommand(
            Guid.NewGuid(),
            runId,
            expectedRevision,
            "researcher",
            FixedUtc,
            new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", 0, 0, wells),
            new ChannelRegionMappingDto(ChannelNames.Rox, "ROX", 0, 0, wells),
            "run-001.xlsx",
            "data/raw/run-001.xlsx",
            ValidSha256,
            2048,
            SourceFileDto.MediaTypeNames.Xlsx);
    }

    private static void AssertFailure<T>(GatewayResult<T> result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Single(result.Errors);
        Assert.Equal(code, result.Errors[0].Code);
        Assert.Equal(SeverityNames.Blocking, result.Errors[0].Severity);
        Assert.False(string.IsNullOrWhiteSpace(result.Errors[0].RecommendedAction));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FluoAxis.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FixtureInspectionPort(ImportPreviewDto preview) : IImportInspectionPort
    {
        public Task<Result<ImportPreviewDto>> InspectAsync(
            ManagedImportRequest request,
            IProgress<OperationProgressDto>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result<ImportPreviewDto>.Ok(preview));
        }
    }
}
