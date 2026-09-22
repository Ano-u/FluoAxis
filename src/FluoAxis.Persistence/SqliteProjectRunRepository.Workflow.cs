namespace FluoAxis.Persistence;

using System.Globalization;
using FluoAxis.Application;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;

/// <summary>
/// The workflow half of <see cref="SqliteProjectRunRepository"/>: writing the
/// data each stage produces and reading it back for the next stage. Every
/// write happens inside the caller's transaction, so a stage's rows and its
/// state advance commit or roll back together.
/// </summary>
public sealed partial class SqliteProjectRunRepository
{
    /// <summary>
    /// Writes whatever parts of a workflow payload are present. Each stage
    /// contributes only its own part, so an earlier stage's rows survive a
    /// later stage's mutation.
    /// </summary>
    private static async Task WritePayloadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RunMutationRequest request,
        RunWorkflowPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Source is { } source && payload.Signals is { } signals)
        {
            await WriteSourceAsync(connection, transaction, request, source, cancellationToken).ConfigureAwait(false);
            await WriteSignalsAsync(connection, transaction, request.RunId, signals, cancellationToken).ConfigureAwait(false);
        }

        if (payload.Mapping is { } mapping)
        {
            await WriteMappingAsync(connection, transaction, request.RunId, mapping, cancellationToken).ConfigureAwait(false);
        }

        if (payload.Assignments is { } assignments)
        {
            await WriteAssignmentsAsync(connection, transaction, request.RunId, assignments, cancellationToken).ConfigureAwait(false);
        }

        if (payload.Calibration is { } calibration)
        {
            await WriteCalibrationAsync(connection, transaction, request, calibration, cancellationToken).ConfigureAwait(false);
        }

        if (payload.Control is { } control)
        {
            await WriteControlAsync(connection, transaction, request, control, cancellationToken).ConfigureAwait(false);
        }

        if (payload.Traces is { } traces)
        {
            await WriteTracesAsync(connection, transaction, request.RunId, traces, cancellationToken).ConfigureAwait(false);
        }

        if (payload.Samples is { } samples)
        {
            await WriteSamplesAsync(connection, transaction, request.RunId, samples, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RunMutationRequest request,
        PreparedSourceIdentity source,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO source_file(source_file_id, run_id, original_file_name, relative_resource_path, sha256, size_bytes, media_type, imported_utc, schema_version)
            VALUES($id, $runId, $name, $path, $sha, $size, $media, $imported, 2);
            """, cancellationToken,
            ("$id", Text(Guid.CreateVersion7())), ("$runId", Text(request.RunId)),
            ("$name", source.DisplayName), ("$path", source.ResourcePath.Value),
            ("$sha", source.Sha256), ("$size", source.SizeBytes), ("$media", source.MediaType),
            ("$imported", Text(request.IssuedUtc))).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes every point of every series with one prepared statement inside
    /// the caller's transaction (requirements doc §18.3): a single
    /// transaction, a single prepared command, batched parameter reuse — not
    /// one transaction per point.
    /// </summary>
    private static async Task WriteSignalsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        IReadOnlyList<SignalSeries> signals,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO signal_point(run_id, channel, well_row, well_column, point_index, elapsed_ticks, rfu)
            VALUES($runId, $channel, $row, $column, $index, $ticks, $rfu);
            """;
        SqliteParameter runIdParameter = command.Parameters.Add("$runId", SqliteType.Text);
        SqliteParameter channelParameter = command.Parameters.Add("$channel", SqliteType.Integer);
        SqliteParameter rowParameter = command.Parameters.Add("$row", SqliteType.Integer);
        SqliteParameter columnParameter = command.Parameters.Add("$column", SqliteType.Integer);
        SqliteParameter indexParameter = command.Parameters.Add("$index", SqliteType.Integer);
        SqliteParameter ticksParameter = command.Parameters.Add("$ticks", SqliteType.Integer);
        SqliteParameter rfuParameter = command.Parameters.Add("$rfu", SqliteType.Real);
        command.Prepare();

        string runIdText = Text(runId);
        foreach (SignalSeries series in signals)
        {
            int row = series.Well.Row - 'A';
            runIdParameter.Value = runIdText;
            channelParameter.Value = (int)series.Channel;
            rowParameter.Value = row;
            columnParameter.Value = series.Well.Column;
            for (int index = 0; index < series.Points.Count; index++)
            {
                SignalPoint point = series.Points[index];
                indexParameter.Value = index;
                ticksParameter.Value = point.Elapsed.Ticks;
                rfuParameter.Value = point.Rfu;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteMappingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        PreparedDataMapping mapping,
        CancellationToken cancellationToken)
    {
        foreach (PreparedChannelRegion region in new[] { mapping.Fam, mapping.Rox })
        {
            int channel = (int)region.Channel;
            await ExecuteAsync(connection, transaction, """
                INSERT INTO channel_mapping(run_id, channel, sheet_name, header_row_index, time_column_index)
                VALUES($runId, $channel, $sheet, $header, $time);
                """, cancellationToken,
                ("$runId", Text(runId)), ("$channel", channel), ("$sheet", region.SheetName),
                ("$header", region.HeaderRowIndex), ("$time", region.TimeColumnIndex)).ConfigureAwait(false);

            foreach ((int columnIndex, WellId well) in region.WellColumns)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO well_column_mapping(run_id, channel, column_index, well_row, well_column)
                    VALUES($runId, $channel, $column, $row, $wellColumn);
                    """, cancellationToken,
                    ("$runId", Text(runId)), ("$channel", channel), ("$column", columnIndex),
                    ("$row", well.Row - 'A'), ("$wellColumn", well.Column)).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteAssignmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        IReadOnlyList<WellAssignment> assignments,
        CancellationToken cancellationToken)
    {
        foreach (WellAssignment assignment in assignments)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO well_assignment(run_id, well_row, well_column, role, sample_id, group_label, replicate_index, target, notes)
                VALUES($runId, $row, $column, $role, $sampleId, $group, $replicate, $target, $notes);
                """, cancellationToken,
                ("$runId", Text(runId)), ("$row", assignment.Well.Row - 'A'), ("$column", assignment.Well.Column),
                ("$role", assignment.Role.ToString()), ("$sampleId", (object?)assignment.Sample?.SampleId ?? DBNull.Value),
                ("$group", (object?)assignment.Sample?.Group ?? DBNull.Value),
                ("$replicate", (object?)assignment.Sample?.ReplicateIndex ?? DBNull.Value),
                ("$target", (object?)assignment.Sample?.Target ?? DBNull.Value),
                ("$notes", (object?)assignment.Sample?.Notes ?? DBNull.Value)).ConfigureAwait(false);
        }
    }

    private static async Task WriteCalibrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RunMutationRequest request,
        PersistedCalibration calibration,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO run_calibration(run_id, status, run_fm_fam, run_fm_rox, run_fb_fam, run_fb_rox, method_snapshot_hash, created_utc)
            VALUES($runId, $status, $fmFam, $fmRox, $fbFam, $fbRox, $hash, $created);
            """, cancellationToken,
            ("$runId", Text(request.RunId)), ("$status", calibration.Status),
            ("$fmFam", Nullable(calibration.RunFmFam)), ("$fmRox", Nullable(calibration.RunFmRox)),
            ("$fbFam", Nullable(calibration.RunFbFam)), ("$fbRox", Nullable(calibration.RunFbRox)),
            ("$hash", calibration.MethodSnapshotHash), ("$created", Text(request.IssuedUtc))).ConfigureAwait(false);
        await WriteDiagnosticsAsync(connection, transaction, "calibration_well_result", request.RunId, calibration.Diagnostics, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteControlAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RunMutationRequest request,
        PersistedControl control,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO control_result(run_id, status, wt_y_fam_mean, wt_y_rox_mean, lqc_call, lqc_mean_dr_prime, created_utc)
            VALUES($runId, $status, $wtFam, $wtRox, $call, $mean, $created);
            """, cancellationToken,
            ("$runId", Text(request.RunId)), ("$status", control.Status),
            ("$wtFam", Nullable(control.WtYFamMean)), ("$wtRox", Nullable(control.WtYRoxMean)),
            ("$call", (object?)control.LqcCall ?? DBNull.Value), ("$mean", Nullable(control.LqcMeanDrPrime)),
            ("$created", Text(request.IssuedUtc))).ConfigureAwait(false);
        await WriteDiagnosticsAsync(connection, transaction, "control_well_result", request.RunId, control.Diagnostics, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteDiagnosticsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        Guid runId,
        IReadOnlyList<PersistedDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (PersistedDiagnostic diagnostic in diagnostics)
        {
            WellId well = WellId.Parse(diagnostic.Well);
            await ExecuteAsync(connection, transaction, $"""
                INSERT OR REPLACE INTO {table}(run_id, well_row, well_column, channel, code, message, value)
                VALUES($runId, $row, $column, $channel, $code, $message, $value);
                """, cancellationToken,
                ("$runId", Text(runId)), ("$row", well.Row - 'A'), ("$column", well.Column),
                ("$channel", diagnostic.Channel), ("$code", diagnostic.Code),
                ("$message", diagnostic.Message), ("$value", Nullable(diagnostic.Value))).ConfigureAwait(false);
        }
    }

    private static async Task WriteTracesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        IReadOnlyList<PersistedTrace> traces,
        CancellationToken cancellationToken)
    {
        foreach (PersistedTrace trace in traces)
        {
            WellId well = WellId.Parse(trace.Well);
            await ExecuteAsync(connection, transaction, """
                INSERT OR REPLACE INTO calculation_trace(trace_id, run_id, algorithm, method_snapshot_hash, well_row, well_column, channel, source_point_index, comparison_operator, threshold_raw, result_raw, final_status)
                VALUES($id, $runId, $algorithm, $hash, $row, $column, $channel, $point, $op, $threshold, $result, $status);
                """, cancellationToken,
                ("$id", trace.TraceId), ("$runId", Text(runId)), ("$algorithm", trace.Algorithm),
                ("$hash", trace.MethodSnapshotHash), ("$row", well.Row - 'A'), ("$column", well.Column),
                ("$channel", trace.Channel), ("$point", (object?)trace.SourcePointIndex ?? DBNull.Value),
                ("$op", trace.ComparisonOperator), ("$threshold", trace.ThresholdRaw),
                ("$result", Nullable(trace.ResultRaw)), ("$status", trace.FinalStatus)).ConfigureAwait(false);

            for (int ordinal = 0; ordinal < trace.IntermediateValues.Count; ordinal++)
            {
                PersistedTraceValue value = trace.IntermediateValues[ordinal];
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO calculation_trace_value(trace_id, ordinal, name, value)
                    VALUES($id, $ordinal, $name, $value);
                    """, cancellationToken,
                    ("$id", trace.TraceId), ("$ordinal", ordinal), ("$name", value.Name), ("$value", value.Value)).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteSamplesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        IReadOnlyList<PersistedSample> samples,
        CancellationToken cancellationToken)
    {
        foreach (PersistedSample sample in samples)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO sample(run_id, sample_id, status, call, mean_dr_prime, error_code)
                VALUES($runId, $sampleId, $status, $call, $mean, $error);
                """, cancellationToken,
                ("$runId", Text(runId)), ("$sampleId", sample.SampleId), ("$status", sample.Status),
                ("$call", (object?)sample.Call ?? DBNull.Value), ("$mean", Nullable(sample.MeanDrPrime)),
                ("$error", (object?)sample.ErrorCode ?? DBNull.Value)).ConfigureAwait(false);

            foreach (PersistedSampleWell well in sample.Wells)
            {
                WellId id = WellId.Parse(well.Well);
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO sample_well_result(run_id, sample_id, well_row, well_column, y_fam, y_rox, dr_prime, call, trace_id)
                    VALUES($runId, $sampleId, $row, $column, $yFam, $yRox, $dr, $call, $traceId);
                    """, cancellationToken,
                    ("$runId", Text(runId)), ("$sampleId", sample.SampleId), ("$row", id.Row - 'A'),
                    ("$column", id.Column), ("$yFam", well.YFam), ("$yRox", well.YRox),
                    ("$dr", well.DrPrime), ("$call", well.Call), ("$traceId", well.TraceId)).ConfigureAwait(false);
            }
        }
    }

    private static object Nullable(double? value) => value is { } present ? present : DBNull.Value;

    private static async Task<StoredMutation?> LoadMutationAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid requestId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT run_id, event_type, expected_run_revision, from_state, to_state, actor_id, issued_utc
            FROM mutation_request WHERE request_id = $requestId;
            """;
        command.Parameters.AddWithValue("$requestId", Text(requestId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredMutation(
                Guid.Parse(reader.GetString(0)),
                new RunMutationRequest(
                    requestId,
                    Guid.Parse(reader.GetString(0)),
                    reader.GetInt32(2),
                    (RunState)reader.GetInt32(3),
                    (RunState)reader.GetInt32(4),
                    reader.GetString(5),
                    ParseTime(reader.GetString(6)),
                    FluoAxis.Contracts.ContractVersion.Current,
                    reader.GetString(1)))
            : null;
    }

    public async ValueTask<PreparedSourceIdentity?> GetSourceAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT original_file_name, relative_resource_path, sha256, size_bytes, media_type
            FROM source_file WHERE run_id = $runId LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new PreparedSourceIdentity(
                reader.GetString(0),
                RelativePath.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4))
            : null;
    }

    public async ValueTask<IReadOnlyList<SignalSeries>> GetSignalsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT channel, well_row, well_column, elapsed_ticks, rfu
            FROM signal_point
            WHERE run_id = $runId
            ORDER BY well_row, well_column, channel, point_index;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));

        List<SignalSeries> series = [];
        Dictionary<(int Row, int Column, int Channel), List<SignalPoint>> grouped = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(0));
                if (!grouped.TryGetValue(key, out List<SignalPoint>? points))
                {
                    points = [];
                    grouped[key] = points;
                }

                points.Add(new SignalPoint(TimeSpan.FromTicks(reader.GetInt64(3)), reader.GetDouble(4)));
            }
        }

        foreach (KeyValuePair<(int Row, int Column, int Channel), List<SignalPoint>> entry in grouped)
        {
            var well = new WellId((char)('A' + entry.Key.Row), entry.Key.Column);
            series.Add(SignalSeries.Create(runId, well, (FluorescenceChannel)entry.Key.Channel, entry.Value));
        }

        return series;
    }

    public async ValueTask<PreparedDataMapping?> GetMappingAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<int, (string Sheet, int Header, int Time, Dictionary<int, WellId> Wells)> regions = [];

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT channel, sheet_name, header_row_index, time_column_index
                FROM channel_mapping WHERE run_id = $runId ORDER BY channel;
                """;
            command.Parameters.AddWithValue("$runId", Text(runId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                regions[reader.GetInt32(0)] = (reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), []);
            }
        }

        if (!regions.TryGetValue((int)FluorescenceChannel.Fam, out var fam)
            || !regions.TryGetValue((int)FluorescenceChannel.Rox, out var rox))
        {
            return null;
        }

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT channel, column_index, well_row, well_column
                FROM well_column_mapping WHERE run_id = $runId ORDER BY channel, column_index;
                """;
            command.Parameters.AddWithValue("$runId", Text(runId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                int channel = reader.GetInt32(0);
                var well = new WellId((char)('A' + reader.GetInt32(2)), reader.GetInt32(3));
                if (channel == (int)FluorescenceChannel.Fam)
                {
                    fam.Wells[reader.GetInt32(1)] = well;
                }
                else if (channel == (int)FluorescenceChannel.Rox)
                {
                    rox.Wells[reader.GetInt32(1)] = well;
                }
            }
        }

        return new PreparedDataMapping(
            runId,
            new PreparedChannelRegion(FluorescenceChannel.Fam, fam.Sheet, fam.Header, fam.Time, fam.Wells),
            new PreparedChannelRegion(FluorescenceChannel.Rox, rox.Sheet, rox.Header, rox.Time, rox.Wells));
    }

    public async ValueTask<IReadOnlyList<WellAssignment>> GetAssignmentsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT well_row, well_column, role, sample_id, group_label, replicate_index, target, notes
            FROM well_assignment WHERE run_id = $runId
            ORDER BY well_row, well_column;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));

        List<WellAssignment> assignments = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var well = new WellId((char)('A' + reader.GetInt32(0)), reader.GetInt32(1));
            WellRole role = Enum.Parse<WellRole>(reader.GetString(2), ignoreCase: false);
            SampleAssignment? sample = reader.IsDBNull(3)
                ? null
                : new SampleAssignment(
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7));
            assignments.Add(new WellAssignment(well, role, sample));
        }

        return assignments;
    }

    public async ValueTask<PersistedCalibration?> GetCalibrationAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, run_fm_fam, run_fm_rox, run_fb_fam, run_fb_rox, method_snapshot_hash
            FROM run_calibration WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        IReadOnlyList<PersistedDiagnostic> diagnostics = await ReadDiagnosticsAsync(connection, "calibration_well_result", runId, cancellationToken).ConfigureAwait(false);
        return new PersistedCalibration(
            reader.GetString(0),
            ReadNullable(reader, 1),
            ReadNullable(reader, 2),
            ReadNullable(reader, 3),
            ReadNullable(reader, 4),
            reader.GetString(5),
            diagnostics);
    }

    public async ValueTask<PersistedControl?> GetControlAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, wt_y_fam_mean, wt_y_rox_mean, lqc_call, lqc_mean_dr_prime
            FROM control_result WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        IReadOnlyList<PersistedDiagnostic> diagnostics = await ReadDiagnosticsAsync(connection, "control_well_result", runId, cancellationToken).ConfigureAwait(false);
        return new PersistedControl(
            reader.GetString(0),
            ReadNullable(reader, 1),
            ReadNullable(reader, 2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            ReadNullable(reader, 4),
            diagnostics);
    }

    public async ValueTask<IReadOnlyList<PersistedSample>> GetSamplesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sample_id, status, call, mean_dr_prime, error_code
            FROM sample WHERE run_id = $runId ORDER BY sample_id;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));

        List<(string SampleId, string Status, string? Call, double? Mean, string? Error)> headers = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                headers.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    ReadNullable(reader, 3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        List<PersistedSample> samples = [];
        foreach ((string sampleId, string status, string? call, double? mean, string? error) in headers)
        {
            List<PersistedSampleWell> wells = [];
            await using SqliteCommand wellCommand = connection.CreateCommand();
            wellCommand.CommandText = """
                SELECT well_row, well_column, y_fam, y_rox, dr_prime, call, trace_id
                FROM sample_well_result WHERE run_id = $runId AND sample_id = $sampleId
                ORDER BY well_row, well_column;
                """;
            wellCommand.Parameters.AddWithValue("$runId", Text(runId));
            wellCommand.Parameters.AddWithValue("$sampleId", sampleId);
            await using SqliteDataReader reader = await wellCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var well = new WellId((char)('A' + reader.GetInt32(0)), reader.GetInt32(1));
                wells.Add(new PersistedSampleWell(
                    well.ToString(), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4),
                    reader.GetString(5), reader.GetString(6)));
            }

            samples.Add(new PersistedSample(sampleId, status, call, mean, error, wells));
        }

        return samples;
    }

    public async ValueTask<IReadOnlyList<PersistedTrace>> GetTracesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT trace_id, algorithm, method_snapshot_hash, well_row, well_column, channel, source_point_index, comparison_operator, threshold_raw, result_raw, final_status
            FROM calculation_trace WHERE run_id = $runId ORDER BY trace_id;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));

        List<PersistedTrace> traces = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string traceId = reader.GetString(0);
                var well = new WellId((char)('A' + reader.GetInt32(3)), reader.GetInt32(4));
                List<PersistedTraceValue> values = [];
                await using SqliteCommand valueCommand = connection.CreateCommand();
                valueCommand.CommandText = "SELECT name, value FROM calculation_trace_value WHERE trace_id = $id ORDER BY ordinal;";
                valueCommand.Parameters.AddWithValue("$id", traceId);
                await using SqliteDataReader valueReader = await valueCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await valueReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    values.Add(new PersistedTraceValue(valueReader.GetString(0), valueReader.GetDouble(1)));
                }

                traces.Add(new PersistedTrace(
                    traceId, reader.GetString(1), reader.GetString(2), well.ToString(), reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6), values, reader.GetString(7),
                    reader.GetDouble(8), ReadNullable(reader, 9), reader.GetString(10)));
            }
        }

        return traces;
    }

    private static async Task<IReadOnlyList<PersistedDiagnostic>> ReadDiagnosticsAsync(
        SqliteConnection connection, string table, Guid runId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT well_row, well_column, channel, code, message, value
            FROM {table} WHERE run_id = $runId ORDER BY well_row, well_column, channel, code;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));

        List<PersistedDiagnostic> diagnostics = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var well = new WellId((char)('A' + reader.GetInt32(0)), reader.GetInt32(1));
            diagnostics.Add(new PersistedDiagnostic(
                well.ToString(), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), ReadNullable(reader, 5)));
        }

        return diagnostics;
    }

    private static double? ReadNullable(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private sealed record StoredMutation(Guid RunId, RunMutationRequest Request)
    {
        public bool Matches(RunMutationRequest other) =>
            Request.EventType == other.EventType
            && Request.ExpectedRunRevision == other.ExpectedRunRevision
            && Request.ExpectedState == other.ExpectedState
            && Request.TargetState == other.TargetState;
    }
}
