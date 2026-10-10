namespace FluoAxis.Analysis;

using FluoAxis.Domain;

/// <summary>
/// Per-well endpoint normalization outcome for one control or sample well
/// (Method-Spec §8/§9.1), with the trace-ready inputs of both channels.
/// </summary>
public sealed record WellEndpointY
{
    public WellId Well { get; }
    public double? YFam { get; }
    public double? YRox { get; }

    /// <summary>Endpoint point index of the FAM series (primary trace anchor).</summary>
    public int? FamEndpointPointIndex { get; }
    /// <summary>Endpoint point index of the ROX series.</summary>
    public int? RoxEndpointPointIndex { get; }
    public SignalPoint? FamEndpoint { get; }
    public SignalPoint? RoxEndpoint { get; }

    public WellEndpointY(WellId well, double? yFam, double? yRox, int? famEndpointPointIndex, int? roxEndpointPointIndex,
        SignalPoint? famEndpoint = null, SignalPoint? roxEndpoint = null)
    {
        Well = well;
        YFam = yFam;
        YRox = yRox;
        FamEndpointPointIndex = famEndpointPointIndex;
        RoxEndpointPointIndex = roxEndpointPointIndex;
        FamEndpoint = famEndpoint; RoxEndpoint = roxEndpoint;
    }
}

/// <summary>
/// The WT reference coordinate in the (Y_FAM, Y_ROX) plane; the origin of
/// every dr' computed downstream (Method-Spec §8.2).
/// </summary>
public sealed record WtReference
{
    public double YFamMean { get; }
    public double YRoxMean { get; }

    public WtReference(double yFamMean, double yRoxMean)
    {
        YFamMean = yFamMean;
        YRoxMean = yRoxMean;
    }
}

/// <summary>Single-well dr' classification (Method-Spec §9.3): dr' &gt;= 0.4 is Positive.</summary>
public enum DrPrimeCall
{
    Positive,
    Negative,
}

/// <summary>Per-well dr' result with its single-well call and full trace.</summary>
public sealed record WellDrPrimeResult
{
    public WellId Well { get; }
    public double? DrPrime { get; }
    public DrPrimeCall? Call { get; }
    public CalculationTrace? Trace { get; }

    public WellDrPrimeResult(WellId well, double? drPrime, DrPrimeCall? call, CalculationTrace? trace)
    {
        Well = well;
        DrPrime = drPrime;
        Call = call;
        Trace = trace;
    }
}

/// <summary>
/// Outcome of the control QC stage: WT endpoint values and group means,
/// the WT reference coordinate, and the LQC per-well dr' results with the
/// replicate direction verdict.
/// </summary>
public sealed record ControlQcResult
{
    public IReadOnlyList<WellEndpointY> WtWells { get; }
    public IReadOnlyList<WellDrPrimeResult> LqcWells { get; }

    /// <summary>Populated only when WT passed; the dr' origin for LQC.</summary>
    public WtReference? WtReference { get; }

    /// <summary>
    /// The LQC group direction, or null when the LQC replicates disagreed.
    /// Carried explicitly so a caller reads the verdict instead of
    /// re-deriving it from localized label strings.
    /// </summary>
    public DrPrimeCall? LqcDirection { get; }

    /// <summary>Engine-computed mean after direction consistency, including a consistent Negative QC failure; null for an uncomputed or mixed group.</summary>
    public double? LqcMeanDrPrime { get; }

    public QcStatus QcStatus { get; }
    public IReadOnlyList<WellDiagnostic> Diagnostics { get; }
    public WtReference? EvaluatedWtReference { get; }

    public ControlQcResult(
        IReadOnlyList<WellEndpointY> wtWells,
        IReadOnlyList<WellDrPrimeResult> lqcWells,
        WtReference? wtReference,
        QcStatus qcStatus,
        IReadOnlyList<WellDiagnostic> diagnostics,
        DrPrimeCall? lqcDirection = null,
        WtReference? evaluatedWtReference = null,
        double? lqcMeanDrPrime = null)
    {
        WtWells = Array.AsReadOnly(wtWells.ToArray());
        LqcWells = Array.AsReadOnly(lqcWells.ToArray());
        WtReference = wtReference;
        LqcDirection = lqcDirection;
        LqcMeanDrPrime = lqcMeanDrPrime;
        QcStatus = qcStatus;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        EvaluatedWtReference = evaluatedWtReference ?? wtReference;
    }
}

/// <summary>Input of the control QC entry point.</summary>
public sealed record ControlQcInput
{
    public IReadOnlyList<WellSignals> WtWells { get; }
    public IReadOnlyList<WellSignals> LqcWells { get; }

    public ControlQcInput(IReadOnlyList<WellSignals> wtWells, IReadOnlyList<WellSignals> lqcWells)
    {
        WtWells = Array.AsReadOnly((wtWells ?? []).ToArray());
        LqcWells = Array.AsReadOnly((lqcWells ?? []).ToArray());
    }
}

/// <summary>
/// Final result of one sample group: direction-consistent wells yield the
/// official label and MeanDrPrime; replicate disagreement yields INVALID
/// with no group call and no mean (Method-Spec §9.4/§9.5).
/// </summary>
public sealed record SampleResult
{
    public string SampleId { get; }
    public QcStatus Status { get; }

    /// <summary>The official label (阳性/阴性); null when replicate directions disagree.</summary>
    public string? Call { get; }

    /// <summary>
    /// The classification direction behind <see cref="Call"/>, as the single
    /// source of truth for the wire vocabulary. Null when the group is
    /// invalid, so a caller never has to re-derive direction by comparing
    /// localized label strings.
    /// </summary>
    public DrPrimeCall? Direction { get; }

    /// <summary>Mean of per-well dr'; reported only after direction consistency (Method-Spec §9.5).</summary>
    public double? MeanDrPrime { get; }

    public IReadOnlyList<WellDrPrimeResult> Wells { get; }

    /// <summary>Structured code of the group-level failure, when <see cref="Status"/> is not Pass.</summary>
    public string? ErrorCode { get; }

    public SampleResult(
        string sampleId,
        QcStatus status,
        string? call,
        double? meanDrPrime,
        IReadOnlyList<WellDrPrimeResult> wells,
        string? errorCode = null,
        DrPrimeCall? direction = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleId);

        SampleId = sampleId;
        Status = status;
        Call = call;
        Direction = direction;
        MeanDrPrime = meanDrPrime;
        Wells = Array.AsReadOnly(wells.ToArray());
        ErrorCode = errorCode;
    }
}

/// <summary>One sample group of the sample analysis entry point.</summary>
public sealed record SampleGroup
{
    public string SampleId { get; }
    public IReadOnlyList<WellSignals> Wells { get; }

    public SampleGroup(string sampleId, IReadOnlyList<WellSignals> wells)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleId);

        SampleId = sampleId;
        Wells = Array.AsReadOnly((wells ?? []).ToArray());
    }
}

/// <summary>
/// Input of the sample analysis entry point.
/// <para>
/// The WT reference is deliberately <em>not</em> a bare coordinate: it must
/// be accompanied by the control-stage verdict it came from. A failed or
/// invalid control stage must never originate a sample dr', so the engine
/// refuses to classify against a reference that did not come from a passing
/// control (defense in depth behind the Application state machine, which
/// also never allows a run to reach sample analysis without a passing
/// control).
/// </para>
/// </summary>
public sealed record SampleAnalysisInput
{
    public IReadOnlyList<SampleGroup> Samples { get; }

    /// <summary>Verdict of the control stage that produced the reference.</summary>
    public QcStatus ControlStatus { get; }

    /// <summary>True when the control stage published a WT reference coordinate.</summary>
    public bool HasWtReference { get; }

    public SampleAnalysisInput(
        IReadOnlyList<SampleGroup> samples,
        QcStatus controlStatus = QcStatus.Pass,
        bool hasWtReference = true)
    {
        Samples = Array.AsReadOnly((samples ?? []).ToArray());
        ControlStatus = controlStatus;
        HasWtReference = hasWtReference;
    }
}

/// <summary>
/// Aggregated outcome of the sample analysis stage: one result per sample
/// group, each carrying per-well traces.
/// </summary>
public sealed record SampleAnalysisResult
{
    public IReadOnlyList<SampleResult> Results { get; }

    public SampleAnalysisResult(IReadOnlyList<SampleResult> results)
    {
        Results = Array.AsReadOnly(results.ToArray());
    }
}
