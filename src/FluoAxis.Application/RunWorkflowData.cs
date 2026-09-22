namespace FluoAxis.Application;

using FluoAxis.Domain;

/// <summary>
/// One per-well diagnostic in a form the persistence layer can store
/// without depending on the Analysis assembly. Mirrors the engine's
/// <c>WellDiagnostic</c> one field at a time.
/// </summary>
public sealed record PersistedDiagnostic(
    string Well,
    int Channel,
    string Code,
    string Message,
    double? Value);

/// <summary>
/// The run-level outcome of the calibration stage, as persisted. A FAIL is a
/// normal scientific value: the run-level means are simply absent. Storing
/// this is what lets control QC consume the *persisted* calibration instead
/// of recomputing it (Method-Spec §2 order is never rearranged).
/// </summary>
public sealed record PersistedCalibration(
    string Status,
    double? RunFmFam,
    double? RunFmRox,
    double? RunFbFam,
    double? RunFbRox,
    string MethodSnapshotHash,
    IReadOnlyList<PersistedDiagnostic> Diagnostics);

/// <summary>
/// The persisted WT reference coordinate and control-stage verdict. The
/// reference is populated only when the control stage passed; a failed or
/// invalid control stage must never publish a dr' origin.
/// </summary>
public sealed record PersistedControl(
    string Status,
    double? WtYFamMean,
    double? WtYRoxMean,
    string? LqcCall,
    double? LqcMeanDrPrime,
    IReadOnlyList<PersistedDiagnostic> Diagnostics);

/// <summary>One per-well sample replicate result as persisted.</summary>
public sealed record PersistedSampleWell(
    string Well,
    double YFam,
    double YRox,
    double DrPrime,
    string Call,
    string TraceId);

/// <summary>One sample group result as persisted.</summary>
public sealed record PersistedSample(
    string SampleId,
    string Status,
    string? Call,
    double? MeanDrPrime,
    string? ErrorCode,
    IReadOnlyList<PersistedSampleWell> Wells);

/// <summary>One ordered intermediate value of a calculation trace.</summary>
public sealed record PersistedTraceValue(string Name, double Value);

/// <summary>
/// The persisted audit trail of one well's classification (CLAUDE.md §13).
/// Carries the algorithm version and Method snapshot hash, so a stored
/// result can always be traced back to the exact method that produced it.
/// </summary>
public sealed record PersistedTrace(
    string TraceId,
    string Algorithm,
    string MethodSnapshotHash,
    string Well,
    int Channel,
    int? SourcePointIndex,
    IReadOnlyList<PersistedTraceValue> IntermediateValues,
    string ComparisonOperator,
    double ThresholdRaw,
    double? ResultRaw,
    string FinalStatus);

/// <summary>
/// Everything one run's workflow produces after import, in a persistence-
/// neutral shape. Each stage fills only the part it owns; the repository
/// writes whatever is present in the same transaction as the state change.
/// </summary>
public sealed record RunWorkflowPayload
{
    /// <summary>Set by the import/mapping stage together with the signals.</summary>
    public PreparedSourceIdentity? Source { get; init; }

    /// <summary>Set by the import/mapping stage.</summary>
    public IReadOnlyList<SignalSeries>? Signals { get; init; }

    /// <summary>Set by the import/mapping stage and retained for reopen.</summary>
    public PreparedDataMapping? Mapping { get; init; }

    /// <summary>Set by the well-assignment stage.</summary>
    public IReadOnlyList<WellAssignment>? Assignments { get; init; }

    /// <summary>Set by the calibration stage.</summary>
    public PersistedCalibration? Calibration { get; init; }

    /// <summary>Set by the control QC stage.</summary>
    public PersistedControl? Control { get; init; }

    /// <summary>Set by the sample analysis stage.</summary>
    public IReadOnlyList<PersistedSample>? Samples { get; init; }

    /// <summary>Set by the sample analysis (or control) stage.</summary>
    public IReadOnlyList<PersistedTrace>? Traces { get; init; }

    public static RunWorkflowPayload Empty { get; } = new();
}

/// <summary>
/// Read access to the workflow data one run has accumulated. Every method
/// returns null/empty when that stage has not run yet, so the Application
/// layer can fail closed instead of proceeding on missing inputs.
/// </summary>
public interface IRunWorkflowReader
{
    ValueTask<PreparedSourceIdentity?> GetSourceAsync(Guid runId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<SignalSeries>> GetSignalsAsync(Guid runId, CancellationToken cancellationToken);

    ValueTask<PreparedDataMapping?> GetMappingAsync(Guid runId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<WellAssignment>> GetAssignmentsAsync(Guid runId, CancellationToken cancellationToken);

    ValueTask<PersistedCalibration?> GetCalibrationAsync(Guid runId, CancellationToken cancellationToken);

    ValueTask<PersistedControl?> GetControlAsync(Guid runId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<PersistedSample>> GetSamplesAsync(Guid runId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<PersistedTrace>> GetTracesAsync(Guid runId, CancellationToken cancellationToken);
}
