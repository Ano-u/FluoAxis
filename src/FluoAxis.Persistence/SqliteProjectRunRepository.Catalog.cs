namespace FluoAxis.Persistence;

using FluoAxis.Application;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;

public sealed partial class SqliteProjectRunRepository
{
    /// <summary>
    /// Lists a bounded page of project/Run pairs, most recently updated Run
    /// first, with a stable Run ID tie-break. Each call reads the current
    /// catalog; callers should refresh pagination after a Run mutation.
    /// </summary>
    public async ValueTask<IReadOnlyList<ProjectCatalogEntry>> ListProjectsAsync(
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.project_id, p.project_name, r.run_id, r.run_revision, r.run_state, p.created_utc
            FROM run_record r
            JOIN project p ON p.project_id = r.project_id
            ORDER BY r.updated_utc DESC, r.run_id ASC
            LIMIT $pageSize OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$pageSize", pageSize);
        command.Parameters.AddWithValue("$offset", offset);

        List<ProjectCatalogEntry> entries = new(pageSize);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new ProjectCatalogEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                (RunState)reader.GetInt32(4),
                ParseTime(reader.GetString(5))));
        }

        return entries.AsReadOnly();
    }
}
