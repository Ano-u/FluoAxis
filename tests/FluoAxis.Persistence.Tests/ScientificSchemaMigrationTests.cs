namespace FluoAxis.Persistence.Tests;

using FluoAxis.Application;
using FluoAxis.Domain;
using FluoAxis.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class ScientificSchemaMigrationTests
{
    [Fact]
    public async Task Version2WorkspaceMigratesWithoutInventingSourceCoordinatesOrStageEvidence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        await SqliteSchema.InitializeLegacyVersion2Async(connection, TestContext.Current.CancellationToken);
        await ExecuteAsync(connection, "INSERT INTO signal_point(run_id,channel,well_row,well_column,point_index,elapsed_ticks,rfu) VALUES('legacy',0,0,1,0,0,123.5);");

        await SqliteSchema.InitializeAsync(connection, TestContext.Current.CancellationToken);
        await SqliteSchema.InitializeAsync(connection, TestContext.Current.CancellationToken);

        Assert.Equal(3L, await ScalarAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM signal_point WHERE rfu=123.5 AND source_row_index IS NULL AND source_column_index IS NULL;"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM scientific_stage;"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM scientific_point;"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM trace_provenance;"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM run_import_options;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortableUpgradeAcceptsOnlyTheExactLegacySchema(bool malicious)
    {
        string name = "scientific-migration-" + Guid.NewGuid().ToString("N");
        string settings = new SqliteConnectionStringBuilder { DataSource = name, Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared }.ToString();
        await using var anchor = new SqliteConnection(settings);
        await SqliteSchema.InitializeLegacyVersion2Async(anchor, TestContext.Current.CancellationToken);
        if (malicious) await ExecuteAsync(anchor, "CREATE TRIGGER unexpected AFTER INSERT ON project BEGIN DELETE FROM project; END;");

        if (malicious)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => SqliteProjectRunRepository.UpgradePortableDatabaseAsync(() => new SqliteConnection(settings), TestContext.Current.CancellationToken));
            Assert.Equal(2L, await ScalarAsync(anchor, "PRAGMA user_version;"));
        }
        else
        {
            await SqliteProjectRunRepository.UpgradePortableDatabaseAsync(() => new SqliteConnection(settings), TestContext.Current.CancellationToken);
            Assert.Equal(3L, await ScalarAsync(anchor, "PRAGMA user_version;"));
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
