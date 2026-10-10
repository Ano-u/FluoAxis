namespace FluoAxis.Persistence;

using Microsoft.Data.Sqlite;

/// <summary>Forward-only SQLite schema initialization for a FluoAxis workspace database.</summary>
public static class SqliteSchema
{
    public const int CurrentVersion = 3;

    public static Task InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        InitializeToVersionAsync(connection, CurrentVersion, cancellationToken);

    internal static Task InitializeLegacyVersion2Async(SqliteConnection connection, CancellationToken cancellationToken) =>
        InitializeToVersionAsync(connection, 2, cancellationToken);

    private static async Task InitializeToVersionAsync(SqliteConnection connection, int targetVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        int version = Convert.ToInt32(
            await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken, transaction).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (version > CurrentVersion)
        {
            throw new NotSupportedException($"Database schema version {version} is newer than supported version {CurrentVersion}.");
        }

        // Forward-only, stepwise migration: each step upgrades exactly one
        // version and is skipped once applied, so an existing workspace keeps
        // its data and a future version is refused above rather than mangled.
        if (version < 1)
        {
            await ExecuteAsync(connection, SchemaV1, cancellationToken, transaction).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA user_version = 1;", cancellationToken, transaction).ConfigureAwait(false);
        }

        if (version < 2)
        {
            await ExecuteAsync(connection, SchemaV2, cancellationToken, transaction).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA user_version = 2;", cancellationToken, transaction).ConfigureAwait(false);
        }

        if (version < 3 && targetVersion >= 3)
        {
            await ExecuteAsync(connection, SchemaV3, cancellationToken, transaction).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA user_version = 3;", cancellationToken, transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaV3 = """
        ALTER TABLE signal_point ADD COLUMN source_row_index INTEGER CHECK(source_row_index IS NULL OR source_row_index >= 0);
        ALTER TABLE signal_point ADD COLUMN source_column_index INTEGER CHECK(source_column_index IS NULL OR source_column_index >= 0);
        ALTER TABLE channel_mapping ADD COLUMN end_row_index INTEGER CHECK(end_row_index IS NULL OR end_row_index > header_row_index);
        CREATE TABLE run_import_options (
            run_id TEXT PRIMARY KEY REFERENCES run_record(run_id) ON DELETE RESTRICT,
            encoding_name TEXT NOT NULL, delimiter TEXT NOT NULL, decimal_culture TEXT NOT NULL, time_unit TEXT NOT NULL
        );
        CREATE TABLE scientific_stage (
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            stage TEXT NOT NULL CHECK(stage IN ('calibration','control','sample')),
            evidence_json TEXT NOT NULL, evidence_sha256 TEXT NOT NULL CHECK(length(evidence_sha256)=64),
            PRIMARY KEY(run_id,stage)
        );
        CREATE TABLE scientific_point (
            run_id TEXT NOT NULL, stage TEXT NOT NULL, well TEXT NOT NULL, channel TEXT NOT NULL CHECK(channel IN ('fam','rox')),
            point_index INTEGER NOT NULL CHECK(point_index>=0), elapsed_ticks INTEGER NOT NULL, raw_rfu REAL NOT NULL,
            normalized REAL, source_row_index INTEGER, source_column_index INTEGER, within_band INTEGER CHECK(within_band IS NULL OR within_band IN (0,1)),
            PRIMARY KEY(run_id,stage,well,channel,point_index),
            FOREIGN KEY(run_id,stage) REFERENCES scientific_stage(run_id,stage) ON DELETE CASCADE
        );
        CREATE TABLE trace_provenance (
            trace_id TEXT PRIMARY KEY REFERENCES calculation_trace(trace_id) ON DELETE CASCADE,
            provenance_json TEXT NOT NULL, provenance_sha256 TEXT NOT NULL CHECK(length(provenance_sha256)=64)
        );
        """;

    private const string SchemaV1 = """
        CREATE TABLE method_snapshot (
            hash TEXT PRIMARY KEY CHECK(length(hash) = 64),
            method_id TEXT NOT NULL,
            method_version TEXT NOT NULL,
            endpoint_minutes REAL NOT NULL,
            endpoint_tolerance_seconds REAL NOT NULL,
            plateau_start_minutes REAL NOT NULL,
            plateau_end_minutes REAL NOT NULL,
            plateau_relative_range_max REAL NOT NULL,
            plateau_min_window_points INTEGER NOT NULL,
            ntc_normalized_min REAL NOT NULL,
            ntc_normalized_max REAL NOT NULL,
            wt_fam_mean_max_exclusive REAL NOT NULL,
            wt_rox_mean_max_exclusive REAL NOT NULL,
            dr_prime_threshold_inclusive REAL NOT NULL,
            positive_label TEXT NOT NULL,
            negative_label TEXT NOT NULL,
            require_classification_consistency INTEGER NOT NULL CHECK(require_classification_consistency IN (0, 1)),
            ntc_minimum_wells INTEGER NOT NULL,
            positive100_minimum_wells INTEGER NOT NULL,
            wt_minimum_wells INTEGER NOT NULL,
            lqc_minimum_wells INTEGER NOT NULL,
            schema_version INTEGER NOT NULL,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE project (
            project_id TEXT PRIMARY KEY,
            project_name TEXT NOT NULL,
            schema_version INTEGER NOT NULL,
            revision INTEGER NOT NULL CHECK(revision >= 0),
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE run_record (
            run_id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL REFERENCES project(project_id) ON DELETE RESTRICT,
            run_revision INTEGER NOT NULL CHECK(run_revision >= 0),
            run_state INTEGER NOT NULL CHECK(run_state BETWEEN 0 AND 13),
            method_snapshot_hash TEXT NOT NULL REFERENCES method_snapshot(hash) ON DELETE RESTRICT,
            schema_version INTEGER NOT NULL,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE create_request (
            request_id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL REFERENCES project(project_id) ON DELETE RESTRICT,
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            project_name TEXT NOT NULL,
            actor_id TEXT NOT NULL,
            issued_utc TEXT NOT NULL,
            contract_version INTEGER NOT NULL,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE audit_event (
            event_id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL REFERENCES project(project_id) ON DELETE RESTRICT,
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            request_id TEXT NOT NULL,
            actor_id TEXT NOT NULL,
            event_type TEXT NOT NULL,
            run_revision INTEGER NOT NULL,
            schema_version INTEGER NOT NULL,
            occurred_utc TEXT NOT NULL
        );

        CREATE INDEX ix_run_project ON run_record(project_id);
        CREATE INDEX ix_audit_run_revision ON audit_event(run_id, run_revision);
        """;

    /// <summary>
    /// Version 2 adds the workflow tables the guided run writes as it
    /// advances. Every schema here holds scientific results and resource
    /// references only: no column may carry an absolute filesystem path
    /// (CLAUDE.md §4), so <c>source_file</c> stores a managed relative
    /// resource path plus its SHA-256.
    /// </summary>
    private const string SchemaV2 = """
        CREATE TABLE source_file (
            source_file_id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            original_file_name TEXT NOT NULL,
            relative_resource_path TEXT NOT NULL,
            sha256 TEXT NOT NULL CHECK(length(sha256) = 64),
            size_bytes INTEGER NOT NULL CHECK(size_bytes > 0),
            media_type TEXT NOT NULL,
            imported_utc TEXT NOT NULL,
            schema_version INTEGER NOT NULL
        );

        CREATE TABLE channel_mapping (
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            channel INTEGER NOT NULL CHECK(channel IN (0, 1)),
            sheet_name TEXT NOT NULL,
            header_row_index INTEGER NOT NULL CHECK(header_row_index >= 0),
            time_column_index INTEGER NOT NULL CHECK(time_column_index >= 0),
            PRIMARY KEY (run_id, channel)
        );

        CREATE TABLE well_column_mapping (
            run_id TEXT NOT NULL,
            channel INTEGER NOT NULL,
            column_index INTEGER NOT NULL CHECK(column_index >= 0),
            well_row INTEGER NOT NULL CHECK(well_row >= 0),
            well_column INTEGER NOT NULL CHECK(well_column >= 0),
            PRIMARY KEY (run_id, channel, column_index),
            FOREIGN KEY (run_id, channel) REFERENCES channel_mapping(run_id, channel) ON DELETE RESTRICT
        );

        CREATE TABLE well_assignment (
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            well_row INTEGER NOT NULL,
            well_column INTEGER NOT NULL,
            role TEXT NOT NULL,
            sample_id TEXT,
            group_label TEXT,
            replicate_index INTEGER,
            target TEXT,
            notes TEXT,
            PRIMARY KEY (run_id, well_row, well_column)
        );

        -- Signal Point Schema (requirements doc §18.3, verbatim column names
        -- and primary key). Written in one transaction with a prepared
        -- statement, batched per run; never one point per transaction.
        CREATE TABLE signal_point (
            run_id        TEXT    NOT NULL,
            channel       INTEGER NOT NULL,
            well_row      INTEGER NOT NULL,
            well_column   INTEGER NOT NULL,
            point_index   INTEGER NOT NULL,
            elapsed_ticks INTEGER NOT NULL,
            rfu           REAL    NOT NULL,
            PRIMARY KEY (
                run_id,
                channel,
                well_row,
                well_column,
                point_index
            )
        );

        CREATE TABLE run_calibration (
            run_id TEXT PRIMARY KEY REFERENCES run_record(run_id) ON DELETE RESTRICT,
            status TEXT NOT NULL,
            run_fm_fam REAL,
            run_fm_rox REAL,
            run_fb_fam REAL,
            run_fb_rox REAL,
            method_snapshot_hash TEXT NOT NULL,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE calibration_well_result (
            run_id TEXT NOT NULL REFERENCES run_calibration(run_id) ON DELETE CASCADE,
            well_row INTEGER NOT NULL,
            well_column INTEGER NOT NULL,
            channel INTEGER NOT NULL CHECK(channel IN (0, 1)),
            code TEXT NOT NULL,
            message TEXT NOT NULL,
            value REAL,
            PRIMARY KEY (run_id, well_row, well_column, channel, code)
        );

        CREATE TABLE control_result (
            run_id TEXT PRIMARY KEY REFERENCES run_record(run_id) ON DELETE RESTRICT,
            status TEXT NOT NULL,
            wt_y_fam_mean REAL,
            wt_y_rox_mean REAL,
            lqc_call TEXT,
            lqc_mean_dr_prime REAL,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE control_well_result (
            run_id TEXT NOT NULL REFERENCES control_result(run_id) ON DELETE CASCADE,
            well_row INTEGER NOT NULL,
            well_column INTEGER NOT NULL,
            channel INTEGER NOT NULL CHECK(channel IN (0, 1)),
            code TEXT NOT NULL,
            message TEXT NOT NULL,
            value REAL,
            PRIMARY KEY (run_id, well_row, well_column, channel, code)
        );

        CREATE TABLE sample (
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            sample_id TEXT NOT NULL,
            status TEXT NOT NULL,
            call TEXT,
            mean_dr_prime REAL,
            error_code TEXT,
            PRIMARY KEY (run_id, sample_id)
        );

        CREATE TABLE sample_well_result (
            run_id TEXT NOT NULL,
            sample_id TEXT NOT NULL,
            well_row INTEGER NOT NULL,
            well_column INTEGER NOT NULL,
            y_fam REAL NOT NULL,
            y_rox REAL NOT NULL,
            dr_prime REAL NOT NULL,
            call TEXT NOT NULL,
            trace_id TEXT NOT NULL,
            PRIMARY KEY (run_id, sample_id, well_row, well_column),
            FOREIGN KEY (run_id, sample_id) REFERENCES sample(run_id, sample_id) ON DELETE CASCADE
        );

        CREATE TABLE calculation_trace (
            trace_id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            algorithm TEXT NOT NULL,
            method_snapshot_hash TEXT NOT NULL,
            well_row INTEGER NOT NULL,
            well_column INTEGER NOT NULL,
            channel INTEGER NOT NULL CHECK(channel IN (0, 1)),
            source_point_index INTEGER,
            comparison_operator TEXT NOT NULL,
            threshold_raw REAL NOT NULL,
            result_raw REAL,
            final_status TEXT NOT NULL
        );

        CREATE TABLE calculation_trace_value (
            trace_id TEXT NOT NULL REFERENCES calculation_trace(trace_id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            name TEXT NOT NULL,
            value REAL NOT NULL,
            PRIMARY KEY (trace_id, ordinal)
        );

        -- Mutation idempotency for the workflow commands, mirroring
        -- create_request for project creation: an identical replay returns
        -- the recorded outcome without advancing the revision again.
        CREATE TABLE mutation_request (
            request_id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL REFERENCES run_record(run_id) ON DELETE RESTRICT,
            event_type TEXT NOT NULL,
            expected_run_revision INTEGER NOT NULL,
            resulting_run_revision INTEGER NOT NULL,
            from_state INTEGER NOT NULL,
            to_state INTEGER NOT NULL,
            actor_id TEXT NOT NULL,
            issued_utc TEXT NOT NULL,
            created_utc TEXT NOT NULL
        );

        CREATE INDEX ix_signal_run_well ON signal_point(run_id, well_row, well_column, channel, point_index);
        CREATE INDEX ix_sample_run ON sample(run_id, sample_id);
        CREATE INDEX ix_trace_run ON calculation_trace(run_id);
        CREATE INDEX ix_source_run ON source_file(run_id);
        """;
}
