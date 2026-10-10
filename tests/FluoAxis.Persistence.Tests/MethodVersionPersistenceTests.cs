namespace FluoAxis.Persistence.Tests;

using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// Guards the existing method_snapshot/archive contract while the channel
/// specific Fm rule is introduced as a new Method version.  The persistence
/// layer must retain the immutable version and canonical hash; interpretation
/// belongs to the Analysis engine.
/// </summary>
public sealed class MethodVersionPersistenceTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LegacyV10SnapshotRoundTripsWithoutHashDrift()
    {
        BrafMethodSnapshot method = new(version: "1.0");
        string hash = method.ComputeCanonicalHash();
        // Frozen in samples/golden/candidate-manifest-v1.json before v1.1.
        Assert.Equal("861f7304b0183e591a997a2e53881866119d9a9938ae13f5e1c2e7900ef80bd9", hash);
        ProjectRunAggregate expected = Aggregate(method);

        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        await repository.CreateOrReplayAsync(Identity(Guid.NewGuid()), () => expected, CancellationToken.None);

        ProjectRunAggregate? actual = await repository.GetByRunIdAsync(expected.RunId, CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal("1.0", actual.Method.Version);
        Assert.Equal(hash, actual.Method.ComputeCanonicalHash());
        Assert.Equal(expected.Method, actual.Method);
    }

    [Fact]
    public async Task DraftV11SnapshotRoundTripsAsADistinctImmutableMethod()
    {
        BrafMethodSnapshot method = new(version: "1.1");
        string hash = method.ComputeCanonicalHash();
        ProjectRunAggregate expected = Aggregate(method);

        await using var database = new SharedMemoryDatabase();
        await using var repository = new SqliteProjectRunRepository(database.CreateConnection);
        await repository.CreateOrReplayAsync(Identity(Guid.NewGuid()), () => expected, CancellationToken.None);

        ProjectRunAggregate? actual = await repository.GetByRunIdAsync(expected.RunId, CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal("1.1", actual.Method.Version);
        Assert.Equal(hash, actual.Method.ComputeCanonicalHash());
        Assert.NotEqual(new BrafMethodSnapshot(version: "1.0").ComputeCanonicalHash(), hash);
    }

    private static CreateProjectIdentity Identity(Guid requestId) =>
        new(requestId, "Method version persistence", "researcher", FixedUtc, ContractVersion.Current);

    private static ProjectRunAggregate Aggregate(BrafMethodSnapshot method) => new()
    {
        ProjectId = Guid.NewGuid(),
        ProjectName = "Method version persistence",
        CreatedAtUtc = FixedUtc,
        RunId = Guid.NewGuid(),
        RunRevision = 0,
        RunState = RunState.New,
        Method = method,
        UpdatedUtc = FixedUtc,
    };

    private sealed class SharedMemoryDatabase : IAsyncDisposable
    {
        private readonly string connectionString;
        private readonly SqliteConnection anchor;

        public SharedMemoryDatabase()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = $"fluoaxis-method-version-{Guid.NewGuid():N}",
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
}
