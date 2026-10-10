namespace FluoAxis.Integration.Tests;

using FluoAxis.Analysis;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// The full-stack proof: the synthetic plate flows through the real
/// Application orchestration, the real Analysis engine, and the real SQLite
/// repository, and the persisted rows are then read back and checked. This
/// is the evidence that a scientific result is actually stored, not merely
/// computed in memory.
/// </summary>
public sealed class SqliteWorkflowRoundTripTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Complete_run_persists_signals_state_and_results()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        var gateway = new ApplicationGateway(
            repository,
            new FixedTimeProvider(Start),
            importInspection: null,
            signalPreparation: new SyntheticSignalPort(),
            engine: new BrafAnalysisEngine());

        GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Persisted", "researcher", Start), CancellationToken.None);
        Guid runId = created.Value!.RunId;

        await gateway.ConfirmMappingAsync(PersistedCommands.Mapping(runId, 0), CancellationToken.None);
        await gateway.AssignWellsAsync(PersistedCommands.Assign(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        GatewayResult<CalibrationResultDto> calibration = await gateway.RunCalibrationAsync(
            PersistedCommands.Calibration(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        Assert.True(calibration.IsSuccess);
        Assert.Equal(StatusNames.Pass, calibration.Value!.Status);
        await gateway.RunControlQcAsync(
            PersistedCommands.Control(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        GatewayResult<IReadOnlyList<SampleAnalysisDto>> analyzed = await gateway.AnalyzeSamplesAsync(
            PersistedCommands.Analysis(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        Assert.True(analyzed.IsSuccess);

        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // The run reached Analyzed and its signals are persisted with the
        // schema required verbatim by requirements doc §18.3.
        Assert.Equal((long)RunState.Analyzed, await ScalarAsync(connection, "SELECT run_state FROM run_record;"));
        Assert.True(await ScalarAsync(connection, "SELECT count(*) FROM signal_point;") > 0);
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM source_file;"));
        Assert.Equal(8L, await ScalarAsync(connection, "SELECT count(*) FROM well_assignment;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM run_calibration WHERE status = 'pass';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM control_result WHERE status = 'pass';"));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM sample;"));
        Assert.True(await ScalarAsync(connection, "SELECT count(*) FROM calculation_trace;") >= 2);

        // One audit event per committed transition; the exact count is an
        // implementation detail, but it must be non-zero and monotonic.
        Assert.True(await ScalarAsync(connection, "SELECT count(*) FROM audit_event;") >= 5);

        // No persisted value may be an absolute filesystem path (P0 rule).
        string? storedPath = await TextAsync(connection, "SELECT relative_resource_path FROM source_file;");
        Assert.Equal("data/raw/synthetic-run.xlsx", storedPath);
        Assert.False(Path.IsPathFullyQualified(storedPath!));
    }

    [Fact]
    public async Task Reopening_a_workspace_reconstructs_the_run_and_its_signals()
    {
        await using var database = new SharedMemoryDatabase();
        Guid runId;

        await using (var repository = new SqliteProjectRunRepository(database.CreateConnection))
        {
            var gateway = new ApplicationGateway(
                repository, new FixedTimeProvider(Start),
                importInspection: null,
                signalPreparation: new SyntheticSignalPort(), engine: new BrafAnalysisEngine());
            runId = (await gateway.CreateProjectAsync(
                new CreateProjectCommand(Guid.NewGuid(), "Reopen", "researcher", Start), CancellationToken.None)).Value!.RunId;
            await gateway.ConfirmMappingAsync(PersistedCommands.Mapping(runId, 0), CancellationToken.None);
        }

        // A fresh repository instance over the same workspace sees the same
        // run state, revision, and imported signals.
        await using (var reopened = new SqliteProjectRunRepository(database.CreateConnection))
        {
            ProjectRunAggregate? aggregate = await reopened.GetByRunIdAsync(runId, CancellationToken.None);
            Assert.NotNull(aggregate);
            Assert.Equal(RunState.Imported, aggregate!.RunState);

            IReadOnlyList<SignalSeries> signals = await reopened.GetSignalsAsync(runId, CancellationToken.None);
            Assert.NotEmpty(signals);
            Assert.Contains(signals, series => series.Well.ToString() == "A1" && series.Channel == FluorescenceChannel.Fam);

            PreparedSourceIdentity? source = await reopened.GetSourceAsync(runId, CancellationToken.None);
            Assert.NotNull(source);
            Assert.Equal("synthetic-run.xlsx", source!.DisplayName);

            PreparedDataMapping? mapping = await reopened.GetMappingAsync(runId, CancellationToken.None);
            Assert.NotNull(mapping);
            Assert.Equal(0, mapping!.Fam.HeaderRowIndex);
            Assert.Equal(0, mapping.Fam.TimeColumnIndex);
            Assert.Equal("FAM", mapping.Fam.SheetName);
            Assert.Equal("A1", mapping.Fam.WellColumns[1].ToString());
            Assert.Equal("ROX", mapping.Rox.SheetName);
            Assert.Equal("A1", mapping.Rox.WellColumns[1].ToString());
        }
    }

    [Fact]
    public async Task Stale_revision_is_refused_against_the_persisted_state()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        var gateway = new ApplicationGateway(
            repository, new FixedTimeProvider(Start),
            importInspection: null,
            signalPreparation: new SyntheticSignalPort(), engine: new BrafAnalysisEngine());

        Guid runId = (await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Stale", "researcher", Start), CancellationToken.None)).Value!.RunId;
        await gateway.ConfirmMappingAsync(PersistedCommands.Mapping(runId, 0), CancellationToken.None);

        GatewayResult<RunSnapshotDto> stale = await gateway.AssignWellsAsync(
            PersistedCommands.Assign(runId, expectedRevision: 0), CancellationToken.None);
        Assert.False(stale.IsSuccess);
        Assert.Equal(ErrorCodes.SystemStaleRunRevision, Assert.Single(stale.Errors).Code);
    }

    [Fact]
    public async Task Exact_stage_retry_replays_from_sqlite_without_revision_increment()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        var gateway = new ApplicationGateway(
            repository, new FixedTimeProvider(Start),
            importInspection: null,
            signalPreparation: new SyntheticSignalPort(), engine: new BrafAnalysisEngine());

        Guid runId = (await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Replay", "researcher", Start), CancellationToken.None)).Value!.RunId;
        await gateway.ConfirmMappingAsync(PersistedCommands.Mapping(runId, 0), CancellationToken.None);
        await gateway.AssignWellsAsync(PersistedCommands.Assign(runId, 1), CancellationToken.None);
        RunCalibrationCommand command = PersistedCommands.Calibration(runId, 2);

        GatewayResult<CalibrationResultDto> first = await gateway.RunCalibrationAsync(command, CancellationToken.None);
        GatewayResult<CalibrationResultDto> replay = await gateway.RunCalibrationAsync(command, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.NotNull(first.Value);
        Assert.NotNull(replay.Value);
        Assert.Equal(first.Value!.Status, replay.Value!.Status);
        Assert.Equal(first.Value.RunFmFam, replay.Value.RunFmFam);
        Assert.Equal(first.Value.RunFmRox, replay.Value.RunFmRox);
        Assert.Equal(first.Value.RunFbFam, replay.Value.RunFbFam);
        Assert.Equal(first.Value.RunFbRox, replay.Value.RunFbRox);
        Assert.Equal(first.Value.PerWellDiagnostics, replay.Value.PerWellDiagnostics);
        Assert.Equal(3, await RevisionAsync(gateway, runId));
    }

    [Fact]
    public async Task Mixed_lqc_replicates_commit_invalid_control_and_block_samples_in_sqlite()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        ApplicationGateway gateway = CreateGateway(repository);
        Guid runId = (await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "LQC conflict", "researcher", Start), CancellationToken.None)).Value!.RunId;
        Assert.True((await gateway.ConfirmMappingAsync(PersistedCommands.Mapping(runId, 0), CancellationToken.None)).IsSuccess);

        // C1 is an existing negative well. Assigning it to LQC alongside
        // positive A4 exercises mixed directions through the real engine.
        AssignWellsCommand normal = PersistedCommands.Assign(runId, 1);
        AssignWellsCommand mixed = new(
            normal.RequestId, normal.RunId, normal.ExpectedRunRevision, normal.ActorId, normal.IssuedUtc,
            [.. normal.Assignments.Select(assignment => assignment.Well == "C1"
                ? new WellAssignmentDto("C1", WellRoleNames.Lqc01Percent)
                : assignment)]);
        Assert.True((await gateway.AssignWellsAsync(mixed, CancellationToken.None)).IsSuccess);
        Assert.True((await gateway.RunCalibrationAsync(PersistedCommands.Calibration(runId, 2), CancellationToken.None)).IsSuccess);

        RunControlQcCommand command = PersistedCommands.Control(runId, 3);
        GatewayResult<ControlQcResultDto> control = await gateway.RunControlQcAsync(command, CancellationToken.None);
        Assert.True(control.IsSuccess);
        Assert.Equal(StatusNames.Invalid, control.Value!.Status);
        Assert.Contains(control.Value.Issues, issue => issue.Code == AnalysisErrorCodes.ControlLqcReplicateInconsistent);
        Assert.Null(control.Value.WtYFamMean);
        Assert.Null(control.Value.LqcCall);

        GatewayResult<ControlQcResultDto> replay = await gateway.RunControlQcAsync(command, CancellationToken.None);
        Assert.True(replay.IsSuccess);
        Assert.Equal(StatusNames.Invalid, replay.Value!.Status);
        Assert.Equal(4, await RevisionAsync(gateway, runId));
        GatewayResult<IReadOnlyList<SampleAnalysisDto>> samples = await gateway.AnalyzeSamplesAsync(
            PersistedCommands.Analysis(runId, 4), CancellationToken.None);
        Assert.False(samples.IsSuccess);

        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal((long)RunState.RunInvalid, await ScalarAsync(connection, "SELECT run_state FROM run_record;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM control_result WHERE status = 'invalid';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM control_well_result WHERE code = 'CONTROL_LQC_REPLICATE_INCONSISTENT';"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM sample;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM audit_event WHERE event_type = 'run.controlQc';"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Corrupt_persisted_calibration_is_refused_without_state_or_audit_changes(bool afterControl, bool missingMean)
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        ApplicationGateway gateway = CreateGateway(repository);
        Guid runId = await PrepareRunAsync(gateway, afterControl);
        int revisionBefore = await RevisionAsync(gateway, runId);

        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        long auditBefore = await ScalarAsync(connection, "SELECT count(*) FROM audit_event;");
        await using (SqliteCommand corrupt = connection.CreateCommand())
        {
            corrupt.CommandText = missingMean
                ? "UPDATE run_calibration SET run_fb_rox = NULL;"
                : "UPDATE run_calibration SET method_snapshot_hash = $hash;";
            if (!missingMean)
            {
                corrupt.Parameters.AddWithValue("$hash", new string('0', 64));
            }

            await corrupt.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        IReadOnlyList<AppErrorDto> errors;
        if (afterControl)
        {
            GatewayResult<IReadOnlyList<SampleAnalysisDto>> result = await gateway.AnalyzeSamplesAsync(
                PersistedCommands.Analysis(runId, revisionBefore), CancellationToken.None);
            Assert.False(result.IsSuccess);
            errors = result.Errors;
        }
        else
        {
            GatewayResult<ControlQcResultDto> result = await gateway.RunControlQcAsync(
                PersistedCommands.Control(runId, revisionBefore), CancellationToken.None);
            Assert.False(result.IsSuccess);
            errors = result.Errors;
        }

        Assert.Equal(ErrorCodes.SystemInvalidRunState, Assert.Single(errors).Code);
        Assert.Equal(revisionBefore, await RevisionAsync(gateway, runId));
        Assert.Equal((long)(afterControl ? RunState.ReadyForAnalysis : RunState.CalibrationPassed),
            await ScalarAsync(connection, "SELECT run_state FROM run_record;"));
        Assert.Equal(auditBefore, await ScalarAsync(connection, "SELECT count(*) FROM audit_event;"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM sample;"));
    }

    [Fact]
    public async Task Missing_persisted_sample_channel_is_refused_without_analyzed_state()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        ApplicationGateway gateway = CreateGateway(repository);
        Guid runId = await PrepareRunAsync(gateway, afterControl: true);
        int revisionBefore = await RevisionAsync(gateway, runId);

        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        long auditBefore = await ScalarAsync(connection, "SELECT count(*) FROM audit_event;");
        await using (SqliteCommand corrupt = connection.CreateCommand())
        {
            corrupt.CommandText = "DELETE FROM signal_point WHERE well_row = 1 AND well_column = 2 AND channel = 1;";
            await corrupt.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        GatewayResult<IReadOnlyList<SampleAnalysisDto>> result = await gateway.AnalyzeSamplesAsync(
            PersistedCommands.Analysis(runId, revisionBefore), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(result.Errors).Code);
        Assert.Contains("B2", result.Errors[0].UserMessage, StringComparison.Ordinal);
        Assert.Equal(revisionBefore, await RevisionAsync(gateway, runId));
        Assert.Equal((long)RunState.ReadyForAnalysis, await ScalarAsync(connection, "SELECT run_state FROM run_record;"));
        Assert.Equal(auditBefore, await ScalarAsync(connection, "SELECT count(*) FROM audit_event;"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM sample;"));
    }

    private static ApplicationGateway CreateGateway(SqliteProjectRunRepository repository) => new(
        repository, new FixedTimeProvider(Start), importInspection: null,
        signalPreparation: new SyntheticSignalPort(), engine: new BrafAnalysisEngine());

    private static async Task<Guid> PrepareRunAsync(ApplicationGateway gateway, bool afterControl)
    {
        GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Input integrity", "researcher", Start), CancellationToken.None);
        Assert.True(created.IsSuccess);
        Guid runId = created.Value!.RunId;
        Assert.True((await gateway.ConfirmMappingAsync(PersistedCommands.Mapping(runId, 0), CancellationToken.None)).IsSuccess);
        Assert.True((await gateway.AssignWellsAsync(PersistedCommands.Assign(runId, 1), CancellationToken.None)).IsSuccess);
        GatewayResult<CalibrationResultDto> calibration = await gateway.RunCalibrationAsync(PersistedCommands.Calibration(runId, 2), CancellationToken.None);
        Assert.True(calibration.IsSuccess);
        Assert.Equal(StatusNames.Pass, calibration.Value!.Status);
        if (afterControl)
        {
            GatewayResult<ControlQcResultDto> control = await gateway.RunControlQcAsync(PersistedCommands.Control(runId, 3), CancellationToken.None);
            Assert.True(control.IsSuccess);
            Assert.Equal(StatusNames.Pass, control.Value!.Status);
        }

        return runId;
    }

    private static async Task<int> RevisionAsync(ApplicationGateway gateway, Guid runId) =>
        (await gateway.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None)).Value!.Revision;

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> TextAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SharedMemoryDatabase : IAsyncDisposable
    {
        private readonly string connectionString;
        private readonly SqliteConnection anchor;

        public SharedMemoryDatabase()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = $"fluoaxis-integration-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
            };
            connectionString = builder.ToString();
            anchor = new SqliteConnection(connectionString);
            anchor.Open();
        }

        public SqliteConnection CreateConnection() => new(connectionString);

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }

    private sealed class SyntheticSignalPort : ISignalPreparationPort
    {
        public Task<Result<PreparedRunSignals>> PrepareAsync(
            PreparedSignalRequest request,
            IProgress<OperationProgressDto>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result<PreparedRunSignals>.Ok(new PreparedRunSignals(
                request.Source, request.Mapping, PersistedPlate.BuildSeries(request.Mapping.RunId), diagnostics: [])));
        }
    }
}
