namespace FluoAxis.Persistence;

using System.Globalization;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;

/// <summary>Stored values from a single well/channel. These records contain no filesystem identity.</summary>
public sealed record StoredRawSignalPoint(int PointIndex, long ElapsedTicks, double Rfu);

public sealed record StoredRawSignalPage(int TotalPoints, IReadOnlyList<StoredRawSignalPoint> Points);

public sealed partial class SqliteProjectRunRepository
{
    /// <summary>Reads count and one bounded data page in one SQLite read transaction; never loads other series.</summary>
    public async ValueTask<StoredRawSignalPage> GetRawSignalPageAsync(Guid runId, WellId well,
        FluorescenceChannel channel, int offset, int pageSize, CancellationToken cancellationToken)
    {
        if (runId == Guid.Empty) throw new ArgumentException("A Run identity is required.", nameof(runId));
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        _ = new WellId(well.Row, well.Column);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 25_000);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$run", Text(runId));
        command.Parameters.AddWithValue("$row", well.Row - 'A');
        command.Parameters.AddWithValue("$column", well.Column);
        command.Parameters.AddWithValue("$channel", (int)channel);
        command.CommandText = """
            SELECT count(*) FROM signal_point
            WHERE run_id=$run AND well_row=$row AND well_column=$column AND channel=$channel;
            """;
        int total = checked((int)Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$offset", offset);
        command.Parameters.AddWithValue("$size", pageSize);
        command.CommandText = """
            SELECT point_index,elapsed_ticks,rfu FROM signal_point
            WHERE run_id=$run AND well_row=$row AND well_column=$column AND channel=$channel
            ORDER BY point_index LIMIT $size OFFSET $offset;
            """;
        List<StoredRawSignalPoint> points = new(Math.Min(pageSize, total));
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                points.Add(new StoredRawSignalPoint(reader.GetInt32(0), reader.GetInt64(1), reader.GetDouble(2)));
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new StoredRawSignalPage(total, points.AsReadOnly());
    }
}
