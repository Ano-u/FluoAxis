namespace FluoAxis.Persistence;

using System.Globalization;
using FluoAxis.Application;
using FluoAxis.Domain;
using Microsoft.Data.Sqlite;

/// <summary>SQLite implementation of the Application project/Run repository port.</summary>
public sealed partial class SqliteProjectRunRepository : IProjectRunRepository, IAsyncDisposable
{
    private readonly Func<SqliteConnection> connectionFactory;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private bool initialized;

    public SqliteProjectRunRepository(Func<SqliteConnection> connectionFactory)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async ValueTask<CreateProjectStoreResult> CreateOrReplayAsync(
        CreateProjectIdentity identity,
        Func<ProjectRunAggregate> aggregateFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(aggregateFactory);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            StoredRequest? existing = await LoadRequestAsync(connection, transaction, identity.RequestId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                ProjectRunAggregate aggregate = await LoadAggregateAsync(connection, transaction, existing.RunId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("A persisted create request references a missing Run.");
                bool conflict = existing.Identity != identity;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new CreateProjectStoreResult(aggregate, IsReplay: !conflict, IsConflict: conflict);
            }

            ProjectRunAggregate created = aggregateFactory();
            await InsertAggregateAsync(connection, transaction, identity, created, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CreateProjectStoreResult(created, IsReplay: false, IsConflict: false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async ValueTask<ProjectRunAggregate?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await LoadAggregateAsync(connection, null, runId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies one revision-guarded state transition plus the workflow data
    /// that stage produced, in a single immediate transaction. The run is
    /// re-read inside the transaction, the <see cref="RunStateMachine"/> gate
    /// is re-checked, and the state, revision, audit event, idempotency
    /// record, and stage rows all commit together — or none of them do.
    /// </summary>
    public async ValueTask<RunMutationOutcome> MutateAsync(
        RunMutationRequest request,
        RunWorkflowPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(payload);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

            StoredMutation? recorded = await LoadMutationAsync(connection, transaction, request.RequestId, cancellationToken).ConfigureAwait(false);
            if (recorded is not null)
            {
                ProjectRunAggregate replayAggregate = await LoadAggregateAsync(connection, transaction, recorded.RunId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("A persisted mutation references a missing Run.");
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return recorded.Request.Matches(request)
                    ? new RunMutationOutcome(replayAggregate, RunMutationFailure.None, IsReplay: true)
                    : RunMutationOutcome.Refused(RunMutationFailure.RequestConflict);
            }

            ProjectRunAggregate? aggregate = await LoadAggregateAsync(connection, transaction, request.RunId, cancellationToken).ConfigureAwait(false);
            if (aggregate is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return RunMutationOutcome.Refused(RunMutationFailure.NotFound);
            }

            if (aggregate.RunRevision != request.ExpectedRunRevision)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return RunMutationOutcome.Refused(RunMutationFailure.StaleRevision);
            }

            if (aggregate.RunState != request.ExpectedState)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return RunMutationOutcome.Refused(RunMutationFailure.WrongState);
            }

            if (!RunStateMachine.IsValidPath(request.TransitionPath, request.ExpectedState, request.TargetState))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return RunMutationOutcome.Refused(RunMutationFailure.IllegalTransition);
            }

            await WritePayloadAsync(connection, transaction, request, payload, cancellationToken).ConfigureAwait(false);

            int nextRevision = aggregate.RunRevision + 1;
            await ExecuteAsync(connection, transaction, """
                UPDATE run_record
                SET run_state = $state, run_revision = $revision, updated_utc = $updated
                WHERE run_id = $runId;
                """, cancellationToken,
                ("$state", (int)request.TargetState), ("$revision", nextRevision),
                ("$updated", Text(request.IssuedUtc)), ("$runId", Text(request.RunId))).ConfigureAwait(false);

            await ExecuteAsync(connection, transaction, """
                INSERT INTO audit_event(event_id, project_id, run_id, request_id, actor_id, event_type, run_revision, schema_version, occurred_utc)
                SELECT $eventId, project_id, run_id, $requestId, $actor, $eventType, $revision, 2, $occurred
                FROM run_record WHERE run_id = $runId;
                """, cancellationToken,
                ("$eventId", Text(Guid.CreateVersion7())), ("$requestId", Text(request.RequestId)),
                ("$actor", request.ActorId), ("$eventType", request.EventType),
                ("$revision", nextRevision), ("$occurred", Text(request.IssuedUtc)),
                ("$runId", Text(request.RunId))).ConfigureAwait(false);

            await ExecuteAsync(connection, transaction, """
                INSERT INTO mutation_request(request_id, run_id, event_type, expected_run_revision, resulting_run_revision, from_state, to_state, actor_id, issued_utc, created_utc)
                VALUES($requestId, $runId, $eventType, $expected, $resulting, $from, $to, $actor, $issued, $created);
                """, cancellationToken,
                ("$requestId", Text(request.RequestId)), ("$runId", Text(request.RunId)),
                ("$eventType", request.EventType), ("$expected", request.ExpectedRunRevision),
                ("$resulting", nextRevision), ("$from", (int)request.ExpectedState), ("$to", (int)request.TargetState),
                ("$actor", request.ActorId), ("$issued", Text(request.IssuedUtc)), ("$created", Text(request.IssuedUtc))).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            ProjectRunAggregate advanced = aggregate with
            {
                RunState = request.TargetState,
                RunRevision = nextRevision,
                UpdatedUtc = request.IssuedUtc,
            };
            return new RunMutationOutcome(advanced, RunMutationFailure.None, IsReplay: false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async ValueTask<StoredRunMutation?> GetMutationAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        StoredMutation? recorded = await LoadMutationAsync(connection, transaction, requestId, cancellationToken).ConfigureAwait(false);
        if (recorded is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        ProjectRunAggregate aggregate = await LoadAggregateAsync(connection, transaction, recorded.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("A persisted mutation references a missing Run.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new StoredRunMutation(recorded.Request, new RunMutationOutcome(aggregate, RunMutationFailure.None, IsReplay: true));
    }

    public ValueTask DisposeAsync()
    {
        initializationGate.Dispose();
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!initialized)
            {
                await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await SqliteSchema.InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
                initialized = true;
            }
        }
        finally
        {
            initializationGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = connectionFactory();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task InsertAggregateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CreateProjectIdentity identity,
        ProjectRunAggregate aggregate,
        CancellationToken cancellationToken)
    {
        await InsertMethodAsync(connection, transaction, aggregate.Method, aggregate.CreatedAtUtc, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO project(project_id, project_name, schema_version, revision, created_utc, updated_utc)
            VALUES($projectId, $name, 1, 0, $created, $updated);
            """, cancellationToken,
            ("$projectId", Text(aggregate.ProjectId)), ("$name", aggregate.ProjectName),
            ("$created", Text(aggregate.CreatedAtUtc)), ("$updated", Text(aggregate.UpdatedUtc))).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO run_record(run_id, project_id, run_revision, run_state, method_snapshot_hash, schema_version, created_utc, updated_utc)
            VALUES($runId, $projectId, $revision, $state, $methodHash, 1, $created, $updated);
            """, cancellationToken,
            ("$runId", Text(aggregate.RunId)), ("$projectId", Text(aggregate.ProjectId)),
            ("$revision", aggregate.RunRevision), ("$state", (int)aggregate.RunState),
            ("$methodHash", aggregate.Method.ComputeCanonicalHash()), ("$created", Text(aggregate.CreatedAtUtc)),
            ("$updated", Text(aggregate.UpdatedUtc))).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO create_request(request_id, project_id, run_id, project_name, actor_id, issued_utc, contract_version, created_utc)
            VALUES($requestId, $projectId, $runId, $name, $actor, $issued, $version, $created);
            """, cancellationToken,
            ("$requestId", Text(identity.RequestId)), ("$projectId", Text(aggregate.ProjectId)),
            ("$runId", Text(aggregate.RunId)), ("$name", identity.ProjectName), ("$actor", identity.ActorId),
            ("$issued", Text(identity.IssuedUtc)), ("$version", identity.ContractVersion), ("$created", Text(aggregate.CreatedAtUtc))).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO audit_event(event_id, project_id, run_id, request_id, actor_id, event_type, run_revision, schema_version, occurred_utc)
            VALUES($eventId, $projectId, $runId, $requestId, $actor, 'project.created', $revision, 1, $occurred);
            """, cancellationToken,
            ("$eventId", Text(Guid.CreateVersion7())), ("$projectId", Text(aggregate.ProjectId)),
            ("$runId", Text(aggregate.RunId)), ("$requestId", Text(identity.RequestId)),
            ("$actor", identity.ActorId), ("$revision", aggregate.RunRevision), ("$occurred", Text(aggregate.CreatedAtUtc))).ConfigureAwait(false);
    }

    private static async Task InsertMethodAsync(
        SqliteConnection connection, SqliteTransaction transaction, BrafMethodSnapshot method,
        DateTimeOffset createdUtc, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT OR IGNORE INTO method_snapshot(
                hash, method_id, method_version, endpoint_minutes, endpoint_tolerance_seconds,
                plateau_start_minutes, plateau_end_minutes, plateau_relative_range_max, plateau_min_window_points,
                ntc_normalized_min, ntc_normalized_max, wt_fam_mean_max_exclusive, wt_rox_mean_max_exclusive,
                dr_prime_threshold_inclusive, positive_label, negative_label, require_classification_consistency,
                ntc_minimum_wells, positive100_minimum_wells, wt_minimum_wells, lqc_minimum_wells, schema_version, created_utc)
            VALUES(
                $hash, $methodId, $methodVersion, $endpoint, $tolerance, $plateauStart, $plateauEnd, $plateauRange, $plateauPoints,
                $ntcMin, $ntcMax, $wtFam, $wtRox, $threshold, $positive, $negative, $consistency,
                $ntcWells, $positiveWells, $wtWells, $lqcWells, 1, $created);
            """, cancellationToken,
            ("$hash", method.ComputeCanonicalHash()), ("$methodId", method.MethodId), ("$methodVersion", method.Version),
            ("$endpoint", method.EndpointMinutes), ("$tolerance", method.EndpointToleranceSeconds),
            ("$plateauStart", method.Plateau.StartMinutes), ("$plateauEnd", method.Plateau.EndMinutes),
            ("$plateauRange", method.Plateau.RelativeRangeMax), ("$plateauPoints", method.Plateau.MinWindowPoints),
            ("$ntcMin", method.Ntc.NormalizedMin), ("$ntcMax", method.Ntc.NormalizedMax),
            ("$wtFam", method.Wt.FamMeanMaxExclusive), ("$wtRox", method.Wt.RoxMeanMaxExclusive),
            ("$threshold", method.Classification.DrPrimeThresholdInclusive), ("$positive", method.Classification.PositiveLabel),
            ("$negative", method.Classification.NegativeLabel), ("$consistency", method.Replicates.RequireClassificationConsistency ? 1 : 0),
            ("$ntcWells", method.Roles.NtcMinimumWells), ("$positiveWells", method.Roles.Positive100MinimumWells),
            ("$wtWells", method.Roles.WildTypeMinimumWells), ("$lqcWells", method.Roles.Lqc01PercentMinimumWells),
            ("$created", Text(createdUtc))).ConfigureAwait(false);
    }

    private static async Task<StoredRequest?> LoadRequestAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid requestId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT run_id, project_name, actor_id, issued_utc, contract_version
            FROM create_request WHERE request_id = $requestId;
            """;
        command.Parameters.AddWithValue("$requestId", Text(requestId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredRequest(
                Guid.Parse(reader.GetString(0)),
                new CreateProjectIdentity(requestId, reader.GetString(1), reader.GetString(2), ParseTime(reader.GetString(3)), reader.GetInt32(4)))
            : null;
    }

    private static async Task<ProjectRunAggregate?> LoadAggregateAsync(
        SqliteConnection connection, SqliteTransaction? transaction, Guid runId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.project_id, p.project_name, p.created_utc, r.run_revision, r.run_state, r.updated_utc,
                   m.method_id, m.method_version, m.endpoint_minutes, m.endpoint_tolerance_seconds,
                   m.plateau_start_minutes, m.plateau_end_minutes, m.plateau_relative_range_max, m.plateau_min_window_points,
                   m.ntc_normalized_min, m.ntc_normalized_max, m.wt_fam_mean_max_exclusive, m.wt_rox_mean_max_exclusive,
                   m.dr_prime_threshold_inclusive, m.positive_label, m.negative_label, m.require_classification_consistency,
                   m.ntc_minimum_wells, m.positive100_minimum_wells, m.wt_minimum_wells, m.lqc_minimum_wells, m.hash
            FROM run_record r
            JOIN project p ON p.project_id = r.project_id
            JOIN method_snapshot m ON m.hash = r.method_snapshot_hash
            WHERE r.run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", Text(runId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var method = new BrafMethodSnapshot(
            reader.GetString(6), reader.GetString(7), reader.GetDouble(8), reader.GetDouble(9),
            new PlateauSpec(reader.GetDouble(10), reader.GetDouble(11), reader.GetDouble(12), reader.GetInt32(13)),
            new NtcSpec(reader.GetDouble(14), reader.GetDouble(15)),
            new WtSpec(reader.GetDouble(16), reader.GetDouble(17)),
            new ClassificationSpec(reader.GetDouble(18), reader.GetString(19), reader.GetString(20)),
            new ReplicateSpec(reader.GetInt32(21) != 0),
            new RoleCardinalitySpec(reader.GetInt32(22), reader.GetInt32(23), reader.GetInt32(24), reader.GetInt32(25)));
        string storedMethodHash = reader.GetString(26);
        if (!string.Equals(method.ComputeCanonicalHash(), storedMethodHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The stored Method snapshot does not match its canonical hash.");
        }

        return new ProjectRunAggregate
        {
            ProjectId = Guid.Parse(reader.GetString(0)),
            ProjectName = reader.GetString(1),
            CreatedAtUtc = ParseTime(reader.GetString(2)),
            RunId = runId,
            RunRevision = reader.GetInt32(3),
            RunState = (RunState)reader.GetInt32(4),
            Method = method,
            UpdatedUtc = ParseTime(reader.GetString(5)),
        };
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Text(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    private static string Text(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record StoredRequest(Guid RunId, CreateProjectIdentity Identity);
}
