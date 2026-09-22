namespace FluoAxis.Persistence.Tests;

using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqlitePersistenceTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SchemaInitializesVersionAndForeignKeys()
    {
        await using var database = new SharedMemoryDatabase();
        await using SqliteConnection connection = database.CreateConnection();

        await SqliteSchema.InitializeAsync(connection, CancellationToken.None);

        Assert.Equal(2L, await ScalarAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(5L, await ScalarAsync(connection, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('method_snapshot','project','run_record','create_request','audit_event');"));

        // V2 adds the workflow tables, including the schema required verbatim
        // by requirements doc §18.3 and the lineage/result tables.
        Assert.Equal(
            14L,
            await ScalarAsync(connection, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('source_file','channel_mapping','well_column_mapping','well_assignment','signal_point','run_calibration','calibration_well_result','control_result','control_well_result','sample','sample_well_result','calculation_trace','calculation_trace_value','mutation_request');"));
    }

    [Fact]
    public async Task SchemaInitializationIsIdempotent()
    {
        await using var database = new SharedMemoryDatabase();
        await using SqliteConnection connection = database.CreateConnection();

        await SqliteSchema.InitializeAsync(connection, CancellationToken.None);
        await SqliteSchema.InitializeAsync(connection, CancellationToken.None);

        Assert.Equal(2L, await ScalarAsync(connection, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task SchemaRejectsFutureVersion()
    {
        await using var database = new SharedMemoryDatabase();
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connection, "PRAGMA user_version = 99;");

        NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(
            () => SqliteSchema.InitializeAsync(connection, CancellationToken.None));

        Assert.Contains("newer than supported", error.Message, StringComparison.Ordinal);
        Assert.Equal(99L, await ScalarAsync(connection, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task RepositoryPersistsAndReconstructsCompleteMethodSnapshot()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        CreateProjectIdentity identity = Identity(Guid.NewGuid());
        ProjectRunAggregate expected = Aggregate(new BrafMethodSnapshot(
            methodId: "BRAF-V600E-Custom", version: "1.1", endpointMinutes: 119.5, endpointToleranceSeconds: 12,
            plateau: new PlateauSpec(80, 119, 0.02, 3), ntc: new NtcSpec(-0.04, 0.04),
            wt: new WtSpec(0.03, 0.04), classification: new ClassificationSpec(0.45, "positive research", "negative research"),
            replicates: new ReplicateSpec(false), roles: new RoleCardinalitySpec(2, 3, 4, 5)));

        await repository.CreateOrReplayAsync(identity, () => expected, TestContext.Current.CancellationToken);
        ProjectRunAggregate? actual = await repository.GetByRunIdAsync(expected.RunId, TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Method.ComputeCanonicalHash(), actual.Method.ComputeCanonicalHash());
    }

    [Fact]
    public async Task RepositoryReplaysExactRequestAndWritesOneAuditEvent()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        CreateProjectIdentity identity = Identity(Guid.NewGuid());
        ProjectRunAggregate aggregate = Aggregate(BrafMethodSnapshot.Default);

        CreateProjectStoreResult first = await repository.CreateOrReplayAsync(identity, () => aggregate, CancellationToken.None);
        CreateProjectStoreResult replay = await repository.CreateOrReplayAsync(identity, () => throw new InvalidOperationException(), CancellationToken.None);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.False(replay.IsConflict);
        Assert.Equal(first.Aggregate, replay.Aggregate);
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM project;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM run_record;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM audit_event;"));
    }

    [Fact]
    public async Task RepositoryRejectsRequestReuseWithDifferentIdentityWithoutExtraRows()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        Guid requestId = Guid.NewGuid();
        ProjectRunAggregate aggregate = Aggregate(BrafMethodSnapshot.Default);
        await repository.CreateOrReplayAsync(Identity(requestId), () => aggregate, CancellationToken.None);

        CreateProjectStoreResult conflict = await repository.CreateOrReplayAsync(
            Identity(requestId) with { ProjectName = "Different" }, () => throw new InvalidOperationException(), CancellationToken.None);

        Assert.True(conflict.IsConflict);
        Assert.False(conflict.IsReplay);
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM project;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM audit_event;"));
    }

    [Fact]
    public async Task RepositorySerializesConcurrentCreateReplay()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        CreateProjectIdentity identity = Identity(Guid.NewGuid());
        ProjectRunAggregate aggregate = Aggregate(BrafMethodSnapshot.Default);

        CreateProjectStoreResult[] results = await Task.WhenAll(Enumerable.Range(0, 24).Select(
            _ => repository.CreateOrReplayAsync(identity, () => aggregate, CancellationToken.None).AsTask()));

        Assert.Single(results, result => !result.IsReplay);
        Assert.Equal(23, results.Count(result => result.IsReplay));
        Assert.All(results, result => Assert.Equal(aggregate.RunId, result.Aggregate.RunId));
    }

    [Fact]
    public async Task SeparateRepositoryInstancesSerializeConcurrentReplay()
    {
        await using var database = new SharedMemoryDatabase();
        await using var first = new SqliteProjectRunRepository(database.CreateConnection);
        await using var second = new SqliteProjectRunRepository(database.CreateConnection);
        CreateProjectIdentity identity = Identity(Guid.NewGuid());
        ProjectRunAggregate aggregate = Aggregate(BrafMethodSnapshot.Default);

        CreateProjectStoreResult[] results = await Task.WhenAll(
            first.CreateOrReplayAsync(identity, () => aggregate, CancellationToken.None).AsTask(),
            second.CreateOrReplayAsync(identity, () => aggregate, CancellationToken.None).AsTask());

        Assert.Single(results, result => !result.IsReplay);
        Assert.Single(results, result => result.IsReplay);
        Assert.All(results, result => Assert.False(result.IsConflict));
    }

    [Fact]
    public async Task FailedCreateRollsBackMethodProjectRequestAndAuditTogether()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        ProjectRunAggregate existing = Aggregate(BrafMethodSnapshot.Default);
        await repository.CreateOrReplayAsync(Identity(Guid.NewGuid()), () => existing, CancellationToken.None);
        var distinctMethod = new BrafMethodSnapshot(methodId: "ROLLBACK-CANDIDATE", version: "1.0");
        ProjectRunAggregate conflicting = Aggregate(distinctMethod) with { ProjectId = existing.ProjectId };

        await Assert.ThrowsAsync<SqliteException>(() => repository.CreateOrReplayAsync(
            Identity(Guid.NewGuid()) with { ProjectName = conflicting.ProjectName },
            () => conflicting,
            CancellationToken.None).AsTask());

        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM project;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM run_record;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM create_request;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM audit_event;"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT count(*) FROM method_snapshot WHERE hash = '{distinctMethod.ComputeCanonicalHash()}';"));
    }

    [Fact]
    public async Task ApplicationGatewayWorksAcrossRepositoryInstances()
    {
        await using var database = new SharedMemoryDatabase();
        Guid runId;
        await using (var firstRepository = new SqliteProjectRunRepository(database.CreateConnection))
        {
            var gateway = new ApplicationGateway(firstRepository, new FixedTimeProvider(FixedUtc));
            GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(
                new CreateProjectCommand(Guid.NewGuid(), "Persistent project", "researcher", FixedUtc), CancellationToken.None);
            Assert.True(created.IsSuccess);
            runId = created.Value!.RunId;
        }

        await using var secondRepository = new SqliteProjectRunRepository(database.CreateConnection);
        var reopenedGateway = new ApplicationGateway(secondRepository);
        GatewayResult<RunSnapshotDto> reopened = await reopenedGateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None);

        Assert.True(reopened.IsSuccess);
        Assert.Equal(RunStateNames.New, reopened.Value!.DomainState);
        Assert.Equal([RunAction.Import, RunAction.ConfirmMapping], reopened.Value.AllowedActions);
    }

    [Fact]
    public async Task ForeignKeyConstraintRejectsOrphanRun()
    {
        await using var database = new SharedMemoryDatabase();
        await using SqliteConnection connection = database.CreateConnection();
        await SqliteSchema.InitializeAsync(connection, CancellationToken.None);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO run_record(run_id, project_id, run_revision, run_state, method_snapshot_hash, schema_version, created_utc, updated_utc)
            VALUES('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', 0, 0,
                   'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 1, '2026-09-03T08:00:00.0000000+00:00', '2026-09-03T08:00:00.0000000+00:00');
            """));
    }

    [Fact]
    public async Task RepositoryHonorsPreCanceledTokenWithoutCreatingSchema()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.CreateOrReplayAsync(
            Identity(Guid.NewGuid()), () => Aggregate(BrafMethodSnapshot.Default), cancellation.Token).AsTask());

        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'project';"));
    }

    private static CreateProjectIdentity Identity(Guid requestId) =>
        new(requestId, "Research Alpha", "researcher", FixedUtc, ContractVersion.Current);

    private static ProjectRunAggregate Aggregate(BrafMethodSnapshot method) => new()
    {
        ProjectId = Guid.NewGuid(),
        ProjectName = "Research Alpha",
        CreatedAtUtc = FixedUtc,
        RunId = Guid.NewGuid(),
        RunRevision = 0,
        RunState = RunState.New,
        Method = method,
        UpdatedUtc = FixedUtc,
    };

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SharedMemoryDatabase : IAsyncDisposable
    {
        private readonly string connectionString;
        private readonly SqliteConnection anchor;

        public SharedMemoryDatabase()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = $"fluoaxis-{Guid.NewGuid():N}",
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
