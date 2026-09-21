namespace FluoAxis.Persistence.Tests;

using System.Diagnostics;
using FluoAxis.Application;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class FileBackedWalWorkspaceTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FileBackedRepositoryUsesWalAndCheckpointProducesPortableDatabase()
    {
        using var fixture = new WorkspaceFixture();
        var workspace = new SqliteWalWorkspace(fixture.CreateConnection);
        await workspace.InitializeAsync(TestContext.Current.CancellationToken);

        ProjectRunAggregate expected = Aggregate();
        await using (var repository = new SqliteProjectRunRepository(fixture.CreateConnection))
        {
            await repository.CreateOrReplayAsync(Identity(), () => expected, TestContext.Current.CancellationToken);
        }

        await using (SqliteConnection inspection = fixture.CreateConnection())
        {
            await inspection.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Equal("wal", await ScalarTextAsync(inspection, "PRAGMA journal_mode;"));
            Assert.Equal(1L, await ScalarLongAsync(inspection, "SELECT count(*) FROM project;"));
        }

        WalCheckpointResult checkpoint = await workspace.CheckpointForPackagingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, checkpoint.BusyConnections);
        Assert.Equal(checkpoint.LogPages, checkpoint.CheckpointedPages);

        string movedDatabase = await fixture.MoveWorkspaceAsync(TestContext.Current.CancellationToken);
        var movedBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = movedDatabase,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        };
        await using var reopened = new SqliteProjectRunRepository(() => new SqliteConnection(movedBuilder.ToString()));
        ProjectRunAggregate? actual = await reopened.GetByRunIdAsync(expected.RunId, TestContext.Current.CancellationToken);
        Assert.NotNull(actual);
        Assert.Equal(expected.ProjectId, actual.ProjectId);
        Assert.Equal(expected.Method.ComputeCanonicalHash(), actual.Method.ComputeCanonicalHash());
    }

    [Fact]
    public async Task WorkspaceRejectsPooledOrMemoryConnections()
    {
        using var fixture = new WorkspaceFixture();
        var pooled = new SqliteWalWorkspace(() => new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.DatabasePath,
            Pooling = true,
        }.ToString()));
        var memory = new SqliteWalWorkspace(() => new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = "wal-workspace-test",
            Mode = SqliteOpenMode.Memory,
            Pooling = false,
        }.ToString()));
        var memoryAlias = new SqliteWalWorkspace(() => new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = string.Concat(":", "memory:"),
            Pooling = false,
        }.ToString()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => pooled.InitializeAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => memory.InitializeAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => memoryAlias.InitializeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WorkspaceRejectsAndDisposesAnAlreadyOpenFactoryConnection()
    {
        using var fixture = new WorkspaceFixture();
        SqliteConnection connection = fixture.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var workspace = new SqliteWalWorkspace(() => connection);

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task CheckpointFailsClosedWhileAnOlderReadSnapshotIsActive()
    {
        using var fixture = new WorkspaceFixture();
        var workspace = new SqliteWalWorkspace(fixture.CreateConnection);
        await workspace.InitializeAsync(TestContext.Current.CancellationToken);

        await using (var repository = new SqliteProjectRunRepository(fixture.CreateConnection))
        {
            await repository.CreateOrReplayAsync(Identity(), Aggregate, TestContext.Current.CancellationToken);
        }

        await using SqliteConnection readerConnection = fixture.CreateConnection();
        await readerConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand readerCommand = readerConnection.CreateCommand();
        readerCommand.CommandText = "SELECT project_id FROM project ORDER BY project_id;";
        await using SqliteDataReader reader = await readerCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));

        await using (var repository = new SqliteProjectRunRepository(fixture.CreateConnection))
        {
            await repository.CreateOrReplayAsync(Identity(), Aggregate, TestContext.Current.CancellationToken);
        }

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.CheckpointForPackagingAsync(TestContext.Current.CancellationToken));
        Assert.Equal("The workspace database is busy and is not ready for packaging.", error.Message);
    }

    [Fact]
    public async Task FileBackedCreateAndCheckpointMeasurementIsRecordedWithoutAnSla()
    {
        using var fixture = new WorkspaceFixture();
        var workspace = new SqliteWalWorkspace(fixture.CreateConnection);
        await workspace.InitializeAsync(TestContext.Current.CancellationToken);
        var stopwatch = Stopwatch.StartNew();

        await using (var repository = new SqliteProjectRunRepository(fixture.CreateConnection))
        {
            for (int i = 0; i < 50; i++)
            {
                ProjectRunAggregate aggregate = Aggregate();
                await repository.CreateOrReplayAsync(Identity(), () => aggregate, TestContext.Current.CancellationToken);
            }
        }

        WalCheckpointResult checkpoint = await workspace.CheckpointForPackagingAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"50 transactional creates plus checkpoint: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(0, checkpoint.BusyConnections);

        await using SqliteConnection inspection = fixture.CreateConnection();
        await inspection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(50L, await ScalarLongAsync(inspection, "SELECT count(*) FROM project;"));
        Assert.Equal(50L, await ScalarLongAsync(inspection, "SELECT count(*) FROM audit_event;"));
    }

    private static CreateProjectIdentity Identity() =>
        new(Guid.NewGuid(), "WAL research workspace", "researcher", FixedUtc, FluoAxis.Contracts.ContractVersion.Current);

    private static ProjectRunAggregate Aggregate() => new()
    {
        ProjectId = Guid.NewGuid(),
        ProjectName = "WAL research workspace",
        CreatedAtUtc = FixedUtc,
        RunId = Guid.NewGuid(),
        RunRevision = 0,
        RunState = RunState.New,
        Method = BrafMethodSnapshot.Default,
        UpdatedUtc = FixedUtc,
    };

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        private readonly string approvedRoot;
        private string workspaceRoot;

        public WorkspaceFixture()
        {
            approvedRoot = Path.Combine(FindRepositoryRoot(), "artifacts", "test-tmp", nameof(FluoAxis.Persistence.Tests),
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            workspaceRoot = Path.Combine(approvedRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspaceRoot);
        }

        public string DatabasePath => Path.Combine(workspaceRoot, "project.sqlite");

        public SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        public async Task<string> MoveWorkspaceAsync(CancellationToken cancellationToken)
        {
            string moved = Path.Combine(approvedRoot, Guid.NewGuid().ToString("N"));
            const int maxAttempts = 5;
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Directory.Move(workspaceRoot, moved);
                    workspaceRoot = moved;
                    return DatabasePath;
                }
                catch (IOException ex) when (attempt < maxAttempts && IsTransientWindowsMoveFailure(ex))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
                }
            }
        }

        private static bool IsTransientWindowsMoveFailure(IOException error)
        {
            const int accessDenied = 5;
            const int sharingViolation = 32;
            const int lockViolation = 33;
            int nativeCode = error.HResult & 0xffff;
            return nativeCode is accessDenied or sharingViolation or lockViolation;
        }

        public void Dispose()
        {
            string approved = Path.GetFullPath(approvedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(workspaceRoot);
            if (!candidate.StartsWith(approved, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove a workspace fixture outside the approved test root.");
            }

            if (Directory.Exists(candidate))
            {
                Directory.Delete(candidate, recursive: true);
            }
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
    }
}
