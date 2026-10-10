namespace FluoAxis.Persistence;

using System.Text.Json;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;

public sealed partial class SqliteProjectRunRepository
{
    private static readonly ScientificEvidenceJsonContext ScientificJson = new(new JsonSerializerOptions(ScientificEvidenceJsonContext.WireOptions));

    private static async Task WriteScientificEvidenceAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid runId, RunWorkflowPayload payload, CancellationToken token)
    {
        foreach (AnalysisStageEvidenceDto stage in payload.ScientificStages ?? [])
        {
            string json = JsonSerializer.Serialize(stage, ScientificJson.AnalysisStageEvidenceDto);
            if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("Scientific stage evidence exceeds its storage limit.");
            await ExecuteAsync(connection, transaction, "INSERT INTO scientific_stage(run_id,stage,evidence_json,evidence_sha256) VALUES($run,$stage,$json,$hash);",
                token, ("$run", Text(runId)), ("$stage", stage.Stage), ("$json", json), ("$hash", HexHash.FromText(json).Value)).ConfigureAwait(false);
        }
        if (payload.ScientificPoints is { } points)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO scientific_point(run_id,stage,well,channel,point_index,elapsed_ticks,raw_rfu,normalized,source_row_index,source_column_index,within_band)
                VALUES($run,$stage,$well,$channel,$index,$ticks,$raw,$y,$row,$column,$within);
                """;
            foreach (string name in new[] { "$run", "$stage", "$well", "$channel", "$index", "$ticks", "$raw", "$y", "$row", "$column", "$within" })
                command.Parameters.AddWithValue(name, DBNull.Value);
            command.Prepare();
            foreach (PersistedScientificPoint point in points)
            {
                command.Parameters["$run"].Value = Text(runId); command.Parameters["$stage"].Value = point.Stage;
                command.Parameters["$well"].Value = point.Well; command.Parameters["$channel"].Value = point.Channel;
                command.Parameters["$index"].Value = point.PointIndex; command.Parameters["$ticks"].Value = point.ElapsedTicks;
                command.Parameters["$raw"].Value = point.RawRfu; command.Parameters["$y"].Value = (object?)point.Normalized ?? DBNull.Value;
                command.Parameters["$row"].Value = (object?)point.SourceRowIndex ?? DBNull.Value;
                command.Parameters["$column"].Value = (object?)point.SourceColumnIndex ?? DBNull.Value;
                command.Parameters["$within"].Value = point.WithinBand is { } within ? (within ? 1 : 0) : DBNull.Value;
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
        foreach (PersistedTrace trace in payload.Traces ?? [])
        {
            if (trace.Provenance is not { } provenance) continue;
            string json = JsonSerializer.Serialize(provenance, ScientificJson.TraceProvenanceDto);
            await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO trace_provenance(trace_id,provenance_json,provenance_sha256) VALUES($id,$json,$hash);",
                token, ("$id", trace.TraceId), ("$json", json), ("$hash", HexHash.FromText(json).Value)).ConfigureAwait(false);
        }
    }

    public async ValueTask<IReadOnlyList<AnalysisStageEvidenceDto>> GetScientificStagesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT evidence_json,evidence_sha256 FROM scientific_stage WHERE run_id=$run ORDER BY CASE stage WHEN 'calibration' THEN 0 WHEN 'control' THEN 1 ELSE 2 END;";
        command.Parameters.AddWithValue("$run", Text(runId));
        List<AnalysisStageEvidenceDto> result = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string json = reader.GetString(0);
            if (json.Length > 2 * 1024 * 1024 || HexHash.FromText(json).Value != reader.GetString(1)) throw new InvalidDataException("Stored scientific evidence hash is invalid.");
            result.Add(JsonSerializer.Deserialize(json, ScientificJson.AnalysisStageEvidenceDto) ?? throw new InvalidDataException("Stored scientific evidence is absent."));
        }
        return result.AsReadOnly();
    }

    public async ValueTask<PersistedScientificPointPage> GetScientificPointsAsync(Guid runId, string stage, string well, string channel,
        int offset, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset); ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 250);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("$run", Text(runId)); command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$well", well); command.Parameters.AddWithValue("$channel", channel);
        command.CommandText = "SELECT count(*) FROM scientific_point WHERE run_id=$run AND stage=$stage AND well=$well AND channel=$channel;";
        int count = checked((int)(long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
        command.CommandText = "SELECT point_index,elapsed_ticks,raw_rfu,normalized,source_row_index,source_column_index,within_band FROM scientific_point WHERE run_id=$run AND stage=$stage AND well=$well AND channel=$channel ORDER BY point_index LIMIT $size OFFSET $offset;";
        command.Parameters.AddWithValue("$size", pageSize); command.Parameters.AddWithValue("$offset", offset);
        List<PersistedScientificPoint> points = new(Math.Min(count, pageSize));
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                points.Add(new(stage, well, channel, reader.GetInt32(0), reader.GetInt64(1), reader.GetDouble(2),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6) != 0));
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(count, points.AsReadOnly());
    }

    private static async Task<TraceProvenanceDto?> ReadTraceProvenanceAsync(SqliteConnection connection, string traceId, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT provenance_json,provenance_sha256 FROM trace_provenance WHERE trace_id=$id;";
        command.Parameters.AddWithValue("$id", traceId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        string json = reader.GetString(0);
        if (json.Length > 65_536 || HexHash.FromText(json).Value != reader.GetString(1)) throw new InvalidDataException("Stored trace provenance hash is invalid.");
        return JsonSerializer.Deserialize(json, ScientificJson.TraceProvenanceDto);
    }

    private static async Task<ImportOptionsDto?> ReadImportOptionsAsync(SqliteConnection connection, Guid runId, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT encoding_name,delimiter,decimal_culture,time_unit FROM run_import_options WHERE run_id=$run;";
        command.Parameters.AddWithValue("$run", Text(runId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? new ImportOptionsDto(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)) : null;
    }
}
