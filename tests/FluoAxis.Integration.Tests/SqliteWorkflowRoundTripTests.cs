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
            signalPreparation: new SyntheticSignalPort(), engine: new BrafAnalysisEngine());

        Guid runId = (await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Stale", "researcher", Start), CancellationToken.None)).Value!.RunId;
        await gateway.ConfirmMappingAsync(PersistedCommands.Mapping(runId, 0), CancellationToken.None);

        GatewayResult<RunSnapshotDto> stale = await gateway.AssignWellsAsync(
            PersistedCommands.Assign(runId, expectedRevision: 0), CancellationToken.None);
        Assert.False(stale.IsSuccess);
        Assert.Equal(ErrorCodes.SystemStaleRunRevision, Assert.Single(stale.Errors).Code);
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
