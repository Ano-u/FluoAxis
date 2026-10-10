namespace FluoAxis.Persistence.Tests;

using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class ProjectCatalogTests
{
    private static readonly DateTimeOffset StartedUtc = new(2026, 10, 7, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmptyCatalogDoesNotCreateProjectsOrAuditEvents()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);

        Assert.Empty(await repository.ListProjectsAsync(0, 100, TestContext.Current.CancellationToken));
        Assert.Empty(await repository.ListProjectsAsync(100, 100, TestContext.Current.CancellationToken));

        Assert.Equal(0L, await CountAsync(database, "project"));
        Assert.Equal(0L, await CountAsync(database, "run_record"));
        Assert.Equal(0L, await CountAsync(database, "audit_event"));
        Assert.Equal(0L, await CountAsync(database, "create_request"));
    }

    [Fact]
    public async Task CatalogPagesUseRunUpdateOrderAndStableRunIdTieBreak()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        ProjectRunAggregate first = Aggregate(1, StartedUtc);
        ProjectRunAggregate second = Aggregate(2, StartedUtc.AddMinutes(1));
        ProjectRunAggregate third = Aggregate(3, StartedUtc.AddMinutes(1));
        ProjectRunAggregate fourth = Aggregate(4, StartedUtc.AddMinutes(2));
        foreach (ProjectRunAggregate item in new[] { third, first, fourth, second })
        {
            await CreateAsync(repository, item);
        }

        IReadOnlyList<ProjectCatalogEntry> pageOne = await repository.ListProjectsAsync(0, 2, TestContext.Current.CancellationToken);
        IReadOnlyList<ProjectCatalogEntry> pageTwo = await repository.ListProjectsAsync(2, 2, TestContext.Current.CancellationToken);
        IReadOnlyList<ProjectCatalogEntry> repeated = await repository.ListProjectsAsync(0, 2, TestContext.Current.CancellationToken);

        Assert.Equal([fourth.RunId, second.RunId], pageOne.Select(item => item.RunId));
        Assert.Equal([third.RunId, first.RunId], pageTwo.Select(item => item.RunId));
        Assert.Equal(pageOne, repeated);
        Assert.Empty(await repository.ListProjectsAsync(4, 2, TestContext.Current.CancellationToken));
        Assert.Equal(fourth.ProjectId, pageOne[0].ProjectId);
        Assert.Equal(fourth.ProjectName, pageOne[0].ProjectName);
        Assert.Equal(fourth.CreatedAtUtc, pageOne[0].CreatedAtUtc);
        Assert.Equal(RunState.New, pageOne[0].RunState);
        Assert.Equal(0, pageOne[0].RunRevision);
        Assert.Equal(4L, await CountAsync(database, "audit_event"));
        Assert.Equal(4L, await CountAsync(database, "create_request"));
    }

    [Fact]
    public async Task CatalogRefreshReflectsRunMutationWithoutChangingProjectCreationIdentity()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        ProjectRunAggregate older = Aggregate(1, StartedUtc);
        ProjectRunAggregate newer = Aggregate(2, StartedUtc.AddMinutes(1));
        await CreateAsync(repository, older);
        await CreateAsync(repository, newer);
        Assert.Equal(newer.RunId, (await repository.ListProjectsAsync(0, 1, TestContext.Current.CancellationToken))[0].RunId);

        RunMutationOutcome changed = await repository.MutateAsync(new RunMutationRequest(
            Guid.NewGuid(), older.RunId, 0, RunState.New, RunState.Imported,
            "catalog-test", StartedUtc.AddMinutes(2), ContractVersion.Current, "catalog-test.imported"),
            RunWorkflowPayload.Empty, TestContext.Current.CancellationToken);
        Assert.True(changed.IsSuccess);

        ProjectCatalogEntry refreshed = Assert.Single(await repository.ListProjectsAsync(0, 1, TestContext.Current.CancellationToken));
        Assert.Equal(older.RunId, refreshed.RunId);
        Assert.Equal(older.CreatedAtUtc, refreshed.CreatedAtUtc);
        Assert.Equal(1, refreshed.RunRevision);
        Assert.Equal(RunState.Imported, refreshed.RunState);
        Assert.Equal(3L, await CountAsync(database, "audit_event"));
        Assert.Equal(1L, await CountAsync(database, "mutation_request"));
    }

    [Fact]
    public async Task MaximumPageRemainsBoundedAndLeavesFurtherRowsForNextPage()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        for (int index = 1; index <= 101; index++)
        {
            await CreateAsync(repository, Aggregate(index, StartedUtc));
        }

        IReadOnlyList<ProjectCatalogEntry> firstPage = await repository.ListProjectsAsync(0, 100, TestContext.Current.CancellationToken);
        ProjectCatalogEntry remaining = Assert.Single(await repository.ListProjectsAsync(100, 100, TestContext.Current.CancellationToken));

        Assert.Equal(100, firstPage.Count);
        Assert.DoesNotContain(firstPage, item => item.RunId == remaining.RunId);
        Assert.Equal(101L, await CountAsync(database, "project"));
        Assert.Equal(101L, await CountAsync(database, "audit_event"));
    }

    [Fact]
    public async Task CatalogDoesNotLoadMethodSnapshotsOrScientificPayloads()
    {
        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        ProjectRunAggregate expected = Aggregate(1, StartedUtc);
        await CreateAsync(repository, expected);
        await using (SqliteConnection connection = database.CreateConnection())
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE method_snapshot SET method_version = 'tampered';";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        ProjectCatalogEntry entry = Assert.Single(await repository.ListProjectsAsync(0, 100, TestContext.Current.CancellationToken));

        Assert.Equal(expected.RunId, entry.RunId);
        Assert.Equal(1L, await CountAsync(database, "audit_event"));
        Assert.Equal(0L, await CountAsync(database, "signal_point"));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetByRunIdAsync(
            expected.RunId, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task InvalidPageBoundsAreRejectedBeforeDatabaseAccess(int offset, int pageSize)
    {
        await using var repository = new SqliteProjectRunRepository(() => throw new InvalidOperationException("No database access expected."));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.ListProjectsAsync(
            offset, pageSize, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task PreCanceledCatalogReadDoesNotAccessDatabase()
    {
        await using var repository = new SqliteProjectRunRepository(() => throw new InvalidOperationException("No database access expected."));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ListProjectsAsync(0, 100, cancellation.Token).AsTask());
    }

    private static ProjectRunAggregate Aggregate(int index, DateTimeOffset updatedUtc) => new()
    {
        ProjectId = Guid.NewGuid(),
        ProjectName = $"Catalog research project {index}",
        RunId = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
        RunRevision = 0,
        RunState = RunState.New,
        CreatedAtUtc = StartedUtc,
        UpdatedUtc = updatedUtc,
        Method = BrafMethodSnapshot.Default,
    };

    private static async Task CreateAsync(SqliteProjectRunRepository repository, ProjectRunAggregate aggregate)
    {
        _ = await repository.CreateOrReplayAsync(new CreateProjectIdentity(
            Guid.NewGuid(), aggregate.ProjectName, "catalog-test", aggregate.CreatedAtUtc, ContractVersion.Current),
            () => aggregate, TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(SharedMemoryDatabase database, string table)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table};";
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class SharedMemoryDatabase : IAsyncDisposable
    {
        private readonly string connectionString;
        private readonly SqliteConnection anchor;

        public SharedMemoryDatabase()
        {
            connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"fluoaxis-catalog-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
            }.ToString();
            anchor = new SqliteConnection(connectionString);
            anchor.Open();
        }

        public SqliteConnection CreateConnection() => new(connectionString);

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }
}
