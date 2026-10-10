namespace FluoAxis.Persistence;

using System.Globalization;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;

/// <summary>Validated identity of the single Run carried by a portable database.</summary>
public sealed record PortableRunIdentity(ProjectRunAggregate Run, PreparedSourceIdentity? Source);

public sealed partial class SqliteProjectRunRepository
{
    // Fixed projections are owned by the application. No SQL from an imported
    // database is executed or copied into a destination database.
    private static readonly PackageTable[] PackageTables =
    [
        new("method_snapshot", "hash,method_id,method_version,endpoint_minutes,endpoint_tolerance_seconds,plateau_start_minutes,plateau_end_minutes,plateau_relative_range_max,plateau_min_window_points,ntc_normalized_min,ntc_normalized_max,wt_fam_mean_max_exclusive,wt_rox_mean_max_exclusive,dr_prime_threshold_inclusive,positive_label,negative_label,require_classification_consistency,ntc_minimum_wells,positive100_minimum_wells,wt_minimum_wells,lqc_minimum_wells,schema_version,created_utc", "hash = $method"),
        new("project", "project_id,project_name,schema_version,revision,created_utc,updated_utc", "project_id = $project"),
        new("run_record", "run_id,project_id,run_revision,run_state,method_snapshot_hash,schema_version,created_utc,updated_utc", "run_id = $run"),
        new("create_request", "request_id,project_id,run_id,project_name,actor_id,issued_utc,contract_version,created_utc", "run_id = $run"),
        new("audit_event", "event_id,project_id,run_id,request_id,actor_id,event_type,run_revision,schema_version,occurred_utc", "run_id = $run"),
        new("source_file", "source_file_id,run_id,original_file_name,relative_resource_path,sha256,size_bytes,media_type,imported_utc,schema_version", "run_id = $run"),
        new("channel_mapping", "run_id,channel,sheet_name,header_row_index,time_column_index,end_row_index", "run_id = $run"),
        new("run_import_options", "run_id,encoding_name,delimiter,decimal_culture,time_unit", "run_id = $run"),
        new("well_column_mapping", "run_id,channel,column_index,well_row,well_column", "run_id = $run"),
        new("well_assignment", "run_id,well_row,well_column,role,sample_id,group_label,replicate_index,target,notes", "run_id = $run"),
        new("signal_point", "run_id,channel,well_row,well_column,point_index,elapsed_ticks,rfu,source_row_index,source_column_index", "run_id = $run"),
        new("run_calibration", "run_id,status,run_fm_fam,run_fm_rox,run_fb_fam,run_fb_rox,method_snapshot_hash,created_utc", "run_id = $run"),
        new("calibration_well_result", "run_id,well_row,well_column,channel,code,message,value", "run_id = $run"),
        new("control_result", "run_id,status,wt_y_fam_mean,wt_y_rox_mean,lqc_call,lqc_mean_dr_prime,created_utc", "run_id = $run"),
        new("control_well_result", "run_id,well_row,well_column,channel,code,message,value", "run_id = $run"),
        new("sample", "run_id,sample_id,status,call,mean_dr_prime,error_code", "run_id = $run"),
        new("calculation_trace", "trace_id,run_id,algorithm,method_snapshot_hash,well_row,well_column,channel,source_point_index,comparison_operator,threshold_raw,result_raw,final_status", "run_id = $run"),
        new("sample_well_result", "run_id,sample_id,well_row,well_column,y_fam,y_rox,dr_prime,call,trace_id", "run_id = $run"),
        new("calculation_trace_value", "trace_id,ordinal,name,value", "trace_id IN (SELECT trace_id FROM calculation_trace WHERE run_id = $run)"),
        new("trace_provenance", "trace_id,provenance_json,provenance_sha256", "trace_id IN (SELECT trace_id FROM calculation_trace WHERE run_id = $run)"),
        new("scientific_stage", "run_id,stage,evidence_json,evidence_sha256", "run_id = $run"),
        new("scientific_point", "run_id,stage,well,channel,point_index,elapsed_ticks,raw_rfu,normalized,source_row_index,source_column_index,within_band", "run_id = $run"),
        new("mutation_request", "request_id,run_id,event_type,expected_run_revision,resulting_run_revision,from_state,to_state,actor_id,issued_utc,created_utc", "run_id = $run"),
    ];

    /// <summary>Copies only the selected Run into a new, trusted-schema database.</summary>
    public async Task<PortableRunIdentity> ExportRunDatabaseAsync(Guid runId,
        Func<SqliteConnection> destinationFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destinationFactory);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection source = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction sourceTransaction = source.BeginTransaction(deferred: true);
            ProjectRunAggregate run = await LoadAggregateAsync(source, sourceTransaction, runId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The selected Run does not exist.");
            PreparedSourceIdentity? identity = await ReadPackageSourceAsync(source, sourceTransaction, run.RunId, cancellationToken).ConfigureAwait(false);
            await using SqliteConnection destination = destinationFactory();
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (SqliteCommand budget = destination.CreateCommand())
            {
                // Bound the temporary database while writing, not only after
                // loading its bytes for the archive (64 MiB at 4 KiB/page).
                budget.CommandText = "PRAGMA page_size=4096; PRAGMA max_page_count=16384;";
                await budget.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await SqliteSchema.InitializeAsync(destination, cancellationToken).ConfigureAwait(false);
            if (await PackageScalarAsync(destination, null, "SELECT count(*) FROM project;", cancellationToken).ConfigureAwait(false) != 0)
                throw new InvalidDataException("A portable database destination must be empty.");
            await using SqliteTransaction transaction = destination.BeginTransaction();
            await CopyPackageRowsAsync(source, sourceTransaction, destination, transaction, run, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await sourceTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new PortableRunIdentity(run, identity);
        }
        finally { writeGate.Release(); }
    }

    /// <summary>Validates a closed package database without migrating or modifying it.</summary>
    public static async Task<PortableRunIdentity> InspectPackageDatabaseAsync(
        Func<SqliteConnection> sourceFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        await using SqliteConnection source = sourceFactory();
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        PortableRunIdentity identity = await InspectPackageConnectionAsync(source, cancellationToken).ConfigureAwait(false);
        await ValidatePortableReadModelsAsync(sourceFactory, identity.Run, cancellationToken).ConfigureAwait(false);
        return identity;
    }

    /// <summary>Upgrades only an extracted private scratch database after validating its exact known schema. The archive itself is never changed.</summary>
    public static async Task UpgradePortableDatabaseAsync(Func<SqliteConnection> writableScratchFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writableScratchFactory);
        await using SqliteConnection source = writableScratchFactory();
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        long version = await PackageScalarAsync(source, null, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false);
        if (version == SqliteSchema.CurrentVersion) return;
        if (version != 2) throw new InvalidDataException("The portable database version cannot be migrated.");
        await using var expected = new SqliteConnection("Data Source=:memory:;Pooling=False");
        await SqliteSchema.InitializeLegacyVersion2Async(expected, cancellationToken).ConfigureAwait(false);
        string[] actualSchema = await ReadPackageSchemaAsync(source, cancellationToken).ConfigureAwait(false);
        string[] expectedSchema = await ReadPackageSchemaAsync(expected, cancellationToken).ConfigureAwait(false);
        if (!actualSchema.SequenceEqual(expectedSchema, StringComparer.Ordinal))
            throw new InvalidDataException("The legacy portable database has an unknown schema.");
        await SqliteSchema.InitializeAsync(source, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Imports one validated Run atomically; any existing identity is a conflict.</summary>
    public async Task<ProjectRunAggregate> ImportRunDatabaseAsync(
        Func<SqliteConnection> sourceFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection source = sourceFactory();
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        PortableRunIdentity imported = await InspectPackageConnectionAsync(source, cancellationToken).ConfigureAwait(false);
        await ValidatePortableReadModelsAsync(sourceFactory, imported.Run, cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection destination = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = destination.BeginTransaction(deferred: false);
            await using (SqliteCommand conflicts = destination.CreateCommand())
            {
                conflicts.Transaction = transaction;
                conflicts.CommandText = "SELECT (SELECT count(*) FROM project WHERE project_id=$project) + (SELECT count(*) FROM run_record WHERE run_id=$run);";
                conflicts.Parameters.AddWithValue("$project", Text(imported.Run.ProjectId));
                conflicts.Parameters.AddWithValue("$run", Text(imported.Run.RunId));
                if (Convert.ToInt64(await conflicts.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException("PROJECT_PACKAGE_IDENTITY_CONFLICT");
            }
            await CopyPackageRowsAsync(source, null, destination, transaction, imported.Run, cancellationToken).ConfigureAwait(false);
            ProjectRunAggregate verified = await LoadAggregateAsync(destination, transaction, imported.Run.RunId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The imported Run could not be verified.");
            // Cancellation is honored before the commit boundary. Once commit
            // starts it must produce an unambiguous success so the caller never
            // removes a published source referenced by a committed Run.
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return verified;
        }
        finally { writeGate.Release(); }
    }

    private static async Task<PortableRunIdentity> InspectPackageConnectionAsync(SqliteConnection source, CancellationToken token)
    {
        await using (SqliteCommand settings = source.CreateCommand())
        {
            settings.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA query_only=ON;";
            await settings.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        if (await PackageScalarAsync(source, null, "PRAGMA user_version;", token).ConfigureAwait(false) != SqliteSchema.CurrentVersion)
            throw new InvalidDataException("The portable database schema version is unsupported.");
        await using var expected = new SqliteConnection("Data Source=:memory:;Pooling=False");
        await SqliteSchema.InitializeAsync(expected, token).ConfigureAwait(false);
        string[] expectedSchema = await ReadPackageSchemaAsync(expected, token).ConfigureAwait(false);
        string[] actualSchema = await ReadPackageSchemaAsync(source, token).ConfigureAwait(false);
        if (!expectedSchema.SequenceEqual(actualSchema, StringComparer.Ordinal))
            throw new InvalidDataException("The portable database schema does not match the supported trusted schema.");
        await using (SqliteCommand check = source.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(Convert.ToString(await check.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture), "ok", StringComparison.Ordinal))
                throw new InvalidDataException("The portable database integrity check failed.");
            check.CommandText = "PRAGMA foreign_key_check;";
            await using SqliteDataReader violations = await check.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await violations.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidDataException("The portable database contains invalid references.");
        }
        foreach (string table in new[] { "project", "run_record", "method_snapshot" })
            if (await PackageScalarAsync(source, null, $"SELECT count(*) FROM {table};", token).ConfigureAwait(false) != 1)
                throw new InvalidDataException("A portable package must contain exactly one project, Run and Method.");
        if (await PackageScalarAsync(source, null, "SELECT count(*) FROM signal_point;", token).ConfigureAwait(false) > 1_000_000)
            throw new InvalidDataException("The portable Run exceeds the signal budget.");
        Guid runId;
        await using (SqliteCommand id = source.CreateCommand())
        {
            id.CommandText = "SELECT run_id FROM run_record;";
            runId = Guid.Parse((string)(await id.ExecuteScalarAsync(token).ConfigureAwait(false))!);
        }
        ProjectRunAggregate run = await LoadAggregateAsync(source, null, runId, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The portable Run is unavailable.");
        if (run.ProjectId == Guid.Empty || run.RunId == Guid.Empty || string.IsNullOrWhiteSpace(run.ProjectName)
            || !Enum.IsDefined(run.RunState) || run.RunRevision < 0)
            throw new InvalidDataException("The portable Run identity is invalid.");
        foreach (PackageTable table in PackageTables)
        {
            await using SqliteCommand scope = source.CreateCommand();
            scope.CommandText = $"SELECT count(*) FROM {table.Name} WHERE NOT ({table.Filter});";
            AddPackageKeys(scope, run);
            if (Convert.ToInt64(await scope.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("The package contains data outside the selected Run.");
        }
        PreparedSourceIdentity? identity = await ReadPackageSourceAsync(source, null, runId, token).ConfigureAwait(false);
        if ((run.RunState != RunState.New && identity is null)
            || await PackageScalarAsync(source, null, "SELECT count(*) FROM source_file;", token).ConfigureAwait(false) > 1)
            throw new InvalidDataException("The portable source identity is incomplete.");
        await using (SqliteCommand identityCheck = source.CreateCommand())
        {
            identityCheck.CommandText = """
                SELECT (SELECT count(*) FROM run_calibration WHERE method_snapshot_hash != $method)
                     + (SELECT count(*) FROM calculation_trace WHERE method_snapshot_hash != $method)
                     + (SELECT count(*) FROM audit_event WHERE project_id != $project)
                     + (SELECT count(*) FROM create_request WHERE project_id != $project);
                """;
            AddPackageKeys(identityCheck, run);
            if (Convert.ToInt64(await identityCheck.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("The portable evidence identity is inconsistent.");
        }
        return new PortableRunIdentity(run, identity);
    }

    private static async Task ValidatePortableReadModelsAsync(Func<SqliteConnection> factory, ProjectRunAggregate run, CancellationToken token)
    {
        // Schema was checked already. Mark this temporary reader initialized so
        // it never runs migrations against the untrusted read-only database.
        await using var reader = new SqliteProjectRunRepository(factory) { initialized = true };
        Guid runId = run.RunId;
        var evidence = await new ApplicationGateway(reader).GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), token).ConfigureAwait(false);
        if (!evidence.IsSuccess) throw new InvalidDataException("The portable Run evidence is inconsistent.");
        PreparedDataMapping? mapping = await reader.GetMappingAsync(runId, token).ConfigureAwait(false);
        IReadOnlyList<SignalSeries> signals = await reader.GetSignalsAsync(runId, token).ConfigureAwait(false);
        if (signals.Sum(series => (long)series.Points.Count) > 1_000_000)
            throw new InvalidDataException("The portable Run exceeds the signal budget.");
        if (mapping is null && signals.Count != 0 || mapping is not null && signals.Count == 0)
            throw new InvalidDataException("The portable mapping and signals do not agree.");
        foreach (IGrouping<WellId, SignalSeries> well in signals.GroupBy(series => series.Well))
        {
            SignalSeries[] pair = [.. well];
            if (pair.Length != 2 || pair.Select(series => series.Channel).Distinct().Count() != 2)
                throw new InvalidDataException("A portable well is missing a channel.");
            if (pair[0].Points.Count != pair[1].Points.Count
                || !pair[0].Points.Select(point => point.Elapsed).SequenceEqual(pair[1].Points.Select(point => point.Elapsed)))
                throw new InvalidDataException("A portable well has mismatched time axes.");
        }
        if (mapping is not null && (!mapping.Fam.WellColumns.Values.ToHashSet().SetEquals(signals.Select(series => series.Well))
            || !mapping.Rox.WellColumns.Values.ToHashSet().SetEquals(signals.Select(series => series.Well))))
            throw new InvalidDataException("The portable mapping does not identify every signal well.");
        if (mapping is not null && evidence.Value!.ScientificEvidence is { } scientific)
        {
            PreparedSourceIdentity sourceIdentity = await reader.GetSourceAsync(runId, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Scientific evidence has no source identity.");
            IReadOnlyList<WellAssignment> assignments = await reader.GetAssignmentsAsync(runId, token).ConfigureAwait(false);
            AnalysisInputIdentityDto actual = ScientificInputIdentity.Capture(run, sourceIdentity, mapping, assignments, signals);
            if (scientific.Stages.Any(stage => stage.Identity.InputSnapshotSha256 != actual.InputSnapshotSha256))
                throw new InvalidDataException("The scientific evidence input identity does not match stored raw signals or assignments.");
            await using SqliteConnection pointConnection = factory();
            await pointConnection.OpenAsync(token).ConfigureAwait(false);
            foreach (AnalysisStageEvidenceDto stage in scientific.Stages)
            {
                if (stage.EvaluatedPointsSha256 is null) continue;
                await using SqliteCommand command = pointConnection.CreateCommand();
                command.CommandText = "SELECT well,channel,point_index,elapsed_ticks,raw_rfu,normalized,source_row_index,source_column_index,within_band FROM scientific_point WHERE run_id=$run AND stage=$stage ORDER BY well COLLATE BINARY,channel COLLATE BINARY,point_index;";
                command.Parameters.AddWithValue("$run", Text(runId)); command.Parameters.AddWithValue("$stage", stage.Stage);
                using var digest = new ScientificPointDigest();
                await using SqliteDataReader points = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await points.ReadAsync(token).ConfigureAwait(false))
                    digest.Append(new(stage.Stage, points.GetString(0), points.GetString(1), points.GetInt32(2), points.GetInt64(3), points.GetDouble(4),
                        points.IsDBNull(5) ? null : points.GetDouble(5), points.IsDBNull(6) ? null : points.GetInt32(6), points.IsDBNull(7) ? null : points.GetInt32(7), points.IsDBNull(8) ? null : points.GetInt32(8) != 0));
                if (digest.Complete() != stage.EvaluatedPointsSha256) throw new InvalidDataException("Stored scientific point evidence does not match its captured digest.");
            }
        }
        ValidatePortableStageEvidence(run, evidence.Value!, mapping, signals);
    }

    private static void ValidatePortableStageEvidence(ProjectRunAggregate run, RunEvidenceDto evidence,
        PreparedDataMapping? mapping, IReadOnlyList<SignalSeries> signals)
    {
        // A package carries a committed revision, never a half-computed stage.
        if (run.RunState is RunState.Calibrating or RunState.ControlQc or RunState.Analyzing)
            throw new InvalidDataException("An active computation cannot be imported as a committed Run.");
        if (run.RunState == RunState.New)
        {
            if (mapping is not null || signals.Count != 0 || evidence.Assignments.Count != 0
                || evidence.Calibration is not null || evidence.Control is not null || evidence.Samples.Count != 0)
                throw new InvalidDataException("A new Run contains unexpected analysis data.");
            return;
        }
        if (mapping is null || signals.Count == 0 || evidence.Snapshot.SourceFiles.Count != 1)
            throw new InvalidDataException("The Run lacks its imported source, mapping or signals.");
        if (run.RunState == RunState.Imported)
        {
            if (evidence.Calibration is not null || evidence.Control is not null || evidence.Samples.Count != 0)
                throw new InvalidDataException("An imported Run contains unexpected stage results.");
            return;
        }

        HashSet<string> mappedWells = signals.Select(series => series.Well.ToString()).ToHashSet(StringComparer.Ordinal);
        if (evidence.Assignments.Select(item => item.Well).Distinct(StringComparer.Ordinal).Count() != evidence.Assignments.Count
            || !mappedWells.SetEquals(evidence.Assignments.Select(item => item.Well)))
            throw new InvalidDataException("The portable plate assignments do not cover the imported wells.");
        foreach ((string Role, int Minimum) requirement in new[]
        {
            (WellRoleNames.Ntc, Math.Max(1, run.Method.Roles.NtcMinimumWells)),
            (WellRoleNames.Positive100, Math.Max(1, run.Method.Roles.Positive100MinimumWells)),
            (WellRoleNames.WildType, Math.Max(1, run.Method.Roles.WildTypeMinimumWells)),
            (WellRoleNames.Lqc01Percent, Math.Max(1, run.Method.Roles.Lqc01PercentMinimumWells)),
        })
            if (evidence.Assignments.Count(item => item.Role == requirement.Role) < requirement.Minimum)
                throw new InvalidDataException("The portable plate is missing a required control role.");
        if (run.RunState == RunState.Mapped)
        {
            if (evidence.Calibration is not null || evidence.Control is not null || evidence.Samples.Count != 0)
                throw new InvalidDataException("A mapped Run contains unexpected stage results.");
            return;
        }

        bool calibrationPassed = evidence.Calibration?.Status == StatusNames.Pass;
        bool controlPassed = evidence.Control?.Status == StatusNames.Pass;
        if ((run.RunState is RunState.CalibrationPassed or RunState.ReadyForAnalysis or RunState.Analyzed or RunState.Exported or RunState.ReplicateConflict or RunState.ControlQcFailed)
            && !calibrationPassed)
            throw new InvalidDataException("The portable Run has no passing calibration for its state.");
        if (calibrationPassed)
        {
            CalibrationResultDto calibration = evidence.Calibration!;
            if (calibration.RunFmFam is not { } fmFam || calibration.RunFmRox is not { } fmRox
                || calibration.RunFbFam is not { } fbFam || calibration.RunFbRox is not { } fbRox
                || !double.IsFinite(fmFam - fbFam) || !double.IsFinite(fmRox - fbRox) || fmFam <= fbFam || fmRox <= fbRox)
                throw new InvalidDataException("The passing calibration has incomplete or invalid normalization values.");
        }
        if ((run.RunState is RunState.ReadyForAnalysis or RunState.Analyzed or RunState.Exported or RunState.ReplicateConflict)
            && !controlPassed)
            throw new InvalidDataException("The portable Run has no passing control QC for its state.");
        if (controlPassed && (evidence.Control!.WtYFamMean is null || evidence.Control.WtYRoxMean is null
            || evidence.Control.LqcCall != CallNames.Positive))
            throw new InvalidDataException("The passing control QC lacks its WT reference or positive LQC direction.");
        if (evidence.Samples.Count != 0 && (!calibrationPassed || !controlPassed))
            throw new InvalidDataException("Sample results exist without passing calibration and controls.");

        bool samplesRequired = run.RunState is RunState.Analyzed or RunState.Exported or RunState.ReplicateConflict;
        if (samplesRequired && evidence.Samples.Count == 0 && evidence.Assignments.Any(item => item.Role == WellRoleNames.Sample))
            throw new InvalidDataException("The completed Run has no stored sample results.");
        if (evidence.Samples.Count != 0)
        {
            var groups = evidence.Assignments.Where(item => item.Role == WellRoleNames.Sample)
                .GroupBy(item => item.SampleId!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (!groups.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(evidence.Samples.Select(sample => sample.SampleId)))
                throw new InvalidDataException("Stored sample groups do not match the assigned plate.");
            foreach (SampleAnalysisDto sample in evidence.Samples)
            {
                WellAssignmentDto[] assigned = groups[sample.SampleId];
                if (assigned.Select(item => item.ReplicateIndex ?? 1).Distinct().Count() != assigned.Length
                    || !assigned.Select(item => item.Well).ToHashSet(StringComparer.Ordinal).SetEquals(sample.PerWell.Select(well => well.Well))
                    || sample.PerWell.Count != assigned.Length)
                    throw new InvalidDataException("Stored sample replicates do not match their assignments.");
                foreach (PerWellResultDto well in sample.PerWell)
                    if (well.DrPrime < 0 || well.Call is not (CallNames.Positive or CallNames.Negative))
                        throw new InvalidDataException("A stored per-well result contains an invalid distance or direction.");
                string[] directions = [.. sample.PerWell.Select(well => well.Call).Distinct(StringComparer.Ordinal)];
                if (directions.Length == 1)
                {
                    if (sample.Status != StatusNames.Pass || sample.Call != directions[0] || sample.MeanDrPrime is null)
                        throw new InvalidDataException("A consistent sample has an invalid stored group result.");
                }
                else if (sample.Status != StatusNames.Invalid || sample.Call is not null || sample.MeanDrPrime is not null)
                    throw new InvalidDataException("A mixed sample must remain INVALID without a group call or mean.");
            }
        }
        if ((run.RunState is RunState.Analyzed or RunState.Exported) && evidence.Samples.Any(sample => sample.Status != StatusNames.Pass))
            throw new InvalidDataException("A completed valid Run contains invalid sample groups.");
        if (run.RunState == RunState.RunInvalid
            && evidence.Calibration?.Status is not (StatusNames.Fail or StatusNames.Invalid)
            && evidence.Control?.Status is not (StatusNames.Fail or StatusNames.Invalid)
            && evidence.Samples.All(sample => sample.Status != StatusNames.Invalid))
            throw new InvalidDataException("An invalid Run has no stored failure evidence.");
        if (run.RunState == RunState.CalibrationFailed && evidence.Calibration?.Status is not (StatusNames.Fail or StatusNames.Invalid)
            || run.RunState == RunState.ControlQcFailed && evidence.Control?.Status is not (StatusNames.Fail or StatusNames.Invalid)
            || run.RunState == RunState.ReplicateConflict && evidence.Samples.All(sample => sample.Status != StatusNames.Invalid))
            throw new InvalidDataException("A failed stage has no matching stored failure evidence.");
        // Legacy records may omit trace rows. Preserve that absence and the
        // gateway's partial/unavailable marker instead of fabricating evidence.
    }

    private static async Task<string[]> ReadPackageSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_schema WHERE name NOT GLOB 'sqlite_*' ORDER BY type,name;";
        List<string> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (entries.Count >= 128) throw new InvalidDataException("The portable schema is too large.");
            entries.Add(string.Join('|', reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
        }
        return [.. entries];
    }

    private static async Task CopyPackageRowsAsync(SqliteConnection source, SqliteTransaction? sourceTransaction,
        SqliteConnection destination, SqliteTransaction transaction, ProjectRunAggregate run, CancellationToken token)
    {
        long totalRows = 0;
        foreach (PackageTable table in PackageTables)
        {
            await using SqliteCommand read = source.CreateCommand();
            read.Transaction = sourceTransaction;
            read.CommandText = $"SELECT {table.Columns} FROM {table.Name} WHERE {table.Filter};";
            AddPackageKeys(read, run);
            await using SqliteCommand write = destination.CreateCommand();
            write.Transaction = transaction;
            string[] columns = table.Columns.Split(',');
            string parameters = string.Join(',', Enumerable.Range(0, columns.Length).Select(index => $"$p{index}"));
            string verb = table.Name == "method_snapshot" ? "INSERT OR IGNORE" : "INSERT";
            write.CommandText = $"{verb} INTO {table.Name}({table.Columns}) VALUES({parameters});";
            for (int index = 0; index < columns.Length; index++) write.Parameters.AddWithValue($"$p{index}", DBNull.Value);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (++totalRows > 2_000_000) throw new InvalidDataException("The portable Run exceeds the supported row budget.");
                for (int index = 0; index < columns.Length; index++)
                {
                    object value = reader.GetValue(index);
                    int textLimit = table.Name == "scientific_stage" && columns[index] == "evidence_json" ? 2 * 1024 * 1024
                        : table.Name == "trace_provenance" && columns[index] == "provenance_json" ? 65_536 : 8192;
                    if (value is byte[] || value is string text && text.Length > textLimit || value is double number && !double.IsFinite(number))
                        throw new InvalidDataException("A portable database value exceeds its supported bounds.");
                    write.Parameters[index].Value = value;
                }
                await write.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
    }

    private static void AddPackageKeys(SqliteCommand command, ProjectRunAggregate run)
    {
        command.Parameters.AddWithValue("$run", Text(run.RunId));
        command.Parameters.AddWithValue("$project", Text(run.ProjectId));
        command.Parameters.AddWithValue("$method", run.Method.ComputeCanonicalHash());
    }

    private static async Task<PreparedSourceIdentity?> ReadPackageSourceAsync(SqliteConnection connection, SqliteTransaction? transaction,
        Guid runId, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT original_file_name,relative_resource_path,sha256,size_bytes,media_type,imported_utc FROM source_file WHERE run_id=$run;";
        command.Parameters.AddWithValue("$run", Text(runId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var result = new PreparedSourceIdentity(reader.GetString(0), RelativePath.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), ParseTime(reader.GetString(5)));
        if (await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidDataException("A portable Run may have only one managed source.");
        return result;
    }

    private static async Task<long> PackageScalarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private sealed record PackageTable(string Name, string Columns, string Filter);
}
