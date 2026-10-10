namespace FluoAxis.Application;

using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>Immutable Application-owned state for a project and its first Run.</summary>
public sealed record ProjectRunAggregate
{
    public required Guid ProjectId { get; init; }
    public required string ProjectName { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required Guid RunId { get; init; }
    public required int RunRevision { get; init; }
    public required RunState RunState { get; init; }
    public required BrafMethodSnapshot Method { get; init; }
    public required DateTimeOffset UpdatedUtc { get; init; }
}

/// <summary>The persisted identity of a create request, used for exact replay detection.</summary>
public sealed record CreateProjectIdentity(
    Guid RequestId,
    string ProjectName,
    string ActorId,
    DateTimeOffset IssuedUtc,
    int ContractVersion);

/// <summary>Atomic repository outcome for a project-creation request.</summary>
public sealed record CreateProjectStoreResult(
    ProjectRunAggregate Aggregate,
    bool IsReplay,
    bool IsConflict);

/// <summary>
/// Application persistence port for project creation, Run retrieval, and
/// atomic run mutation. Mutations are the only way a Run's state advances:
/// the port owns the compare-and-swap on revision and the
/// <see cref="RunStateMachine"/> gate, so no caller can move a run outside
/// the frozen transition table.
/// </summary>
public interface IProjectRunRepository : IRunWorkflowReader
{
    ValueTask<CreateProjectStoreResult> CreateOrReplayAsync(
        CreateProjectIdentity identity,
        Func<ProjectRunAggregate> aggregateFactory,
        CancellationToken cancellationToken);

    ValueTask<ProjectRunAggregate?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Applies one revision-guarded state transition plus the workflow data
    /// that stage produced, atomically. The implementation MUST re-read the
    /// run inside the transaction, re-check the gate, write the new state,
    /// increment the revision, append the audit event, and commit — or
    /// refuse the whole thing.
    /// </summary>
    ValueTask<RunMutationOutcome> MutateAsync(
        RunMutationRequest request,
        RunWorkflowPayload payload,
        CancellationToken cancellationToken);

    ValueTask<StoredRunMutation?> GetMutationAsync(Guid requestId, CancellationToken cancellationToken);
}

/// <summary>Previously committed workflow command identity and its terminal outcome.</summary>
public sealed record StoredRunMutation(RunMutationRequest Request, RunMutationOutcome Outcome);

/// <summary>
/// Concurrency-safe development/test adapter for the project repository
/// port. It mirrors the SQLite adapter's contract exactly — compare-and-swap
/// on revision, the <see cref="RunStateMachine"/> gate, request-id replay,
/// and all-or-nothing payload writes — so the workflow tests exercise the
/// same semantics they will get from the real store.
/// </summary>
public sealed class InMemoryProjectRunRepository : IProjectRunRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, (CreateProjectIdentity Identity, ProjectRunAggregate Aggregate)> requests = [];
    private readonly Dictionary<Guid, ProjectRunAggregate> runs = [];
    private readonly Dictionary<Guid, RunWorkflowPayload> payloads = [];
    private readonly Dictionary<Guid, (RunMutationRequest Request, RunMutationOutcome Outcome)> mutations = [];

    public ValueTask<CreateProjectStoreResult> CreateOrReplayAsync(
        CreateProjectIdentity identity,
        Func<ProjectRunAggregate> aggregateFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(aggregateFactory);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (requests.TryGetValue(identity.RequestId, out var existing))
            {
                bool conflict = existing.Identity != identity;
                return ValueTask.FromResult(new CreateProjectStoreResult(existing.Aggregate, !conflict, conflict));
            }

            ProjectRunAggregate aggregate = aggregateFactory();
            requests.Add(identity.RequestId, (identity, aggregate));
            runs.Add(aggregate.RunId, aggregate);
            payloads.Add(aggregate.RunId, RunWorkflowPayload.Empty);
            return ValueTask.FromResult(new CreateProjectStoreResult(aggregate, IsReplay: false, IsConflict: false));
        }
    }

    public ValueTask<ProjectRunAggregate?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runs.TryGetValue(runId, out ProjectRunAggregate? aggregate);
            return ValueTask.FromResult(aggregate);
        }
    }

    public ValueTask<RunMutationOutcome> MutateAsync(
        RunMutationRequest request,
        RunWorkflowPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Exact request replay returns the recorded outcome without a
            // second increment; a reused id with different data is a conflict.
            if (mutations.TryGetValue(request.RequestId, out var recorded))
            {
                if (!recorded.Request.Matches(request))
                {
                    return ValueTask.FromResult(RunMutationOutcome.Refused(RunMutationFailure.RequestConflict));
                }

                return ValueTask.FromResult(recorded.Outcome with { IsReplay = true });
            }

            if (!runs.TryGetValue(request.RunId, out ProjectRunAggregate? aggregate))
            {
                return ValueTask.FromResult(RunMutationOutcome.Refused(RunMutationFailure.NotFound));
            }

            if (aggregate.RunRevision != request.ExpectedRunRevision)
            {
                return ValueTask.FromResult(RunMutationOutcome.Refused(RunMutationFailure.StaleRevision));
            }

            if (aggregate.RunState != request.ExpectedState)
            {
                return ValueTask.FromResult(RunMutationOutcome.Refused(RunMutationFailure.WrongState));
            }

            if (!RunStateMachine.IsValidPath(request.TransitionPath, request.ExpectedState, request.TargetState))
            {
                return ValueTask.FromResult(RunMutationOutcome.Refused(RunMutationFailure.IllegalTransition));
            }

            ProjectRunAggregate advanced = aggregate with
            {
                RunState = request.TargetState,
                RunRevision = aggregate.RunRevision + 1,
                UpdatedUtc = request.IssuedUtc,
            };
            runs[request.RunId] = advanced;
            payloads[request.RunId] = Merge(payloads[request.RunId], payload);

            var outcome = new RunMutationOutcome(advanced, RunMutationFailure.None, IsReplay: false);
            mutations.Add(request.RequestId, (request, outcome));
            return ValueTask.FromResult(outcome);
        }
    }

    public ValueTask<StoredRunMutation?> GetMutationAsync(Guid requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(mutations.TryGetValue(requestId, out var recorded)
                ? new StoredRunMutation(recorded.Request, recorded.Outcome with { IsReplay = true })
                : null);
        }
    }

    public ValueTask<PreparedSourceIdentity?> GetSourceAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload) ? payload.Source : null);
        }
    }

    public ValueTask<IReadOnlyList<SignalSeries>> GetSignalsAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload)
                ? payload.Signals ?? []
                : (IReadOnlyList<SignalSeries>)[]);
        }
    }

    public ValueTask<PreparedDataMapping?> GetMappingAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload) ? payload.Mapping : null);
        }
    }

    public ValueTask<IReadOnlyList<WellAssignment>> GetAssignmentsAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload)
                ? payload.Assignments ?? []
                : (IReadOnlyList<WellAssignment>)[]);
        }
    }

    public ValueTask<PersistedCalibration?> GetCalibrationAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload) ? payload.Calibration : null);
        }
    }

    public ValueTask<PersistedControl?> GetControlAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload) ? payload.Control : null);
        }
    }

    public ValueTask<IReadOnlyList<PersistedSample>> GetSamplesAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload)
                ? payload.Samples ?? []
                : (IReadOnlyList<PersistedSample>)[]);
        }
    }

    public ValueTask<IReadOnlyList<PersistedTrace>> GetTracesAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload)
                ? payload.Traces ?? []
                : (IReadOnlyList<PersistedTrace>)[]);
        }
    }

    public ValueTask<IReadOnlyList<AnalysisStageEvidenceDto>> GetScientificStagesAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(payloads.TryGetValue(runId, out RunWorkflowPayload? payload)
            ? payload.ScientificStages ?? [] : (IReadOnlyList<AnalysisStageEvidenceDto>)[]);
    }

    public ValueTask<PersistedScientificPointPage> GetScientificPointsAsync(Guid runId, string stage, string well, string channel,
        int offset, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            IEnumerable<PersistedScientificPoint> points = payloads.TryGetValue(runId, out RunWorkflowPayload? payload)
                ? (payload.ScientificPoints ?? []).Where(point => point.Stage == stage && point.Well == well && point.Channel == channel)
                : [];
            return ValueTask.FromResult(new PersistedScientificPointPage(points.Count(), Array.AsReadOnly(points.OrderBy(point => point.PointIndex).Skip(offset).Take(pageSize).ToArray())));
        }
    }

    /// <summary>
    /// Merges a stage payload over what the run already stored, so each
    /// stage contributes only its own part and never erases an earlier one.
    /// </summary>
    private static RunWorkflowPayload Merge(RunWorkflowPayload existing, RunWorkflowPayload incoming) => new()
    {
        Source = incoming.Source ?? existing.Source,
        Signals = incoming.Signals ?? existing.Signals,
        Mapping = incoming.Mapping ?? existing.Mapping,
        Assignments = incoming.Assignments ?? existing.Assignments,
        Calibration = incoming.Calibration ?? existing.Calibration,
        Control = incoming.Control ?? existing.Control,
        Samples = incoming.Samples ?? existing.Samples,
        Traces = MergeTraces(existing.Traces, incoming.Traces),
        ScientificStages = incoming.ScientificStages is null ? existing.ScientificStages
            : Array.AsReadOnly((existing.ScientificStages ?? []).Where(stage => !incoming.ScientificStages.Any(item => item.Stage == stage.Stage)).Concat(incoming.ScientificStages).ToArray()),
        ScientificPoints = incoming.ScientificPoints is null ? existing.ScientificPoints
            : Array.AsReadOnly((existing.ScientificPoints ?? []).Concat(incoming.ScientificPoints).ToArray()),
    };

    private static IReadOnlyList<PersistedTrace>? MergeTraces(
        IReadOnlyList<PersistedTrace>? existing,
        IReadOnlyList<PersistedTrace>? incoming)
    {
        if (incoming is null) return existing;

        // Match SQLite's per-TraceId upsert. A later sample stage adds its own
        // evidence without deleting the earlier LQC calculation traces.
        Dictionary<string, PersistedTrace> byId = new(StringComparer.Ordinal);
        foreach (PersistedTrace trace in existing ?? []) byId[trace.TraceId] = trace;
        foreach (PersistedTrace trace in incoming) byId[trace.TraceId] = trace;
        return Array.AsReadOnly(byId.Values.OrderBy(trace => trace.TraceId, StringComparer.Ordinal).ToArray());
    }
}

internal static class RunSnapshotPolicy
{
    public static RunSnapshotDto ToSnapshot(ProjectRunAggregate aggregate) => new(
        aggregate.RunId,
        aggregate.RunRevision,
        RunStateNames.FromDomain(aggregate.RunState),
        AllowedActions(aggregate.RunState),
        blockingIssues: [],
        warningIssues: [],
        aggregate.Method.MethodId,
        aggregate.Method.Version,
        aggregate.Method.ComputeCanonicalHash(),
        sourceFiles: [],
        updatedUtc: aggregate.UpdatedUtc);

    public static IReadOnlyList<string> AllowedActions(RunState state) => state switch
    {
        // ConfirmMapping commits the staged source and advances New -> Imported.
        RunState.New => [RunAction.Import, RunAction.ConfirmMapping],
        RunState.Imported => [RunAction.AssignWells, RunAction.ViewExplorer],
        // A mapped run has persisted roles; calibration is its next command.
        RunState.Mapped => [RunAction.RunCalibration, RunAction.ViewExplorer],
        RunState.CalibrationPassed => [RunAction.RunControlQc, RunAction.ViewExplorer],
        RunState.ReadyForAnalysis => [RunAction.AnalyzeSamples, RunAction.ViewExplorer],
        RunState.Analyzed => [RunAction.Export, RunAction.ViewExplorer],
        RunState.Exported or RunState.RunInvalid => [RunAction.ViewExplorer],
        RunState.Calibrating or RunState.CalibrationFailed or RunState.ControlQc or
            RunState.ControlQcFailed or RunState.Analyzing or RunState.ReplicateConflict => [RunAction.ViewExplorer],
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown Run state."),
    };
}
