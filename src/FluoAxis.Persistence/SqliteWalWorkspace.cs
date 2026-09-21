namespace FluoAxis.Persistence;

using Microsoft.Data.Sqlite;

/// <summary>
/// File-backed SQLite lifecycle primitive for a managed project workspace.
/// The caller owns filesystem resolution and supplies a path-confined
/// connection factory; machine paths never cross this API or its results.
/// </summary>
public sealed class SqliteWalWorkspace
{
    private readonly Func<SqliteConnection> connectionFactory;

    public SqliteWalWorkspace(Func<SqliteConnection> connectionFactory)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>Initializes the schema and confirms persistent WAL journal mode.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = CreatePackagingSafeConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        string journalMode = await ScalarTextAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The workspace database did not enter WAL journal mode.");
        }

        await ExecuteAsync(connection, "PRAGMA synchronous = NORMAL;", cancellationToken).ConfigureAwait(false);
        await SqliteSchema.InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checkpoints all committed WAL pages and truncates the sidecar before
    /// the database is read for packaging. A busy reader fails closed.
    /// </summary>
    public async Task<WalCheckpointResult> CheckpointForPackagingAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = CreatePackagingSafeConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("SQLite returned no WAL checkpoint result.");
        }

        var result = new WalCheckpointResult(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        if (result.BusyConnections != 0 || result.LogPages != result.CheckpointedPages)
        {
            throw new InvalidOperationException("The workspace database is busy and is not ready for packaging.");
        }

        return result;
    }

    private SqliteConnection CreatePackagingSafeConnection()
    {
        SqliteConnection connection = connectionFactory();
        if (connection is null)
        {
            throw new InvalidOperationException("The workspace connection factory returned no connection.");
        }

        try
        {
            if (connection.State != System.Data.ConnectionState.Closed)
            {
                throw new InvalidOperationException("The workspace connection factory must return a closed connection.");
            }

            var settings = new SqliteConnectionStringBuilder(connection.ConnectionString);
            if (string.IsNullOrWhiteSpace(settings.DataSource)
                || string.Equals(settings.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
                || settings.Mode == SqliteOpenMode.Memory
                || settings.Pooling)
            {
                throw new InvalidOperationException("A packageable workspace requires a file-backed SQLite connection with pooling disabled.");
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static async Task<string> ScalarTextAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Path-free result of a successful SQLite WAL checkpoint.</summary>
public sealed record WalCheckpointResult(int BusyConnections, int LogPages, int CheckpointedPages);
