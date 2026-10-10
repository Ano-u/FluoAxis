namespace FluoAxis.Analysis;

using FluoAxis.Domain;

/// <summary>Input of the calibration entry point: the 100% positive and NTC wells.</summary>
public sealed record CalibrationInput
{
    public IReadOnlyList<WellSignals> Positive100Wells { get; }
    public IReadOnlyList<WellSignals> NtcWells { get; }

    public CalibrationInput(IReadOnlyList<WellSignals> positive100Wells, IReadOnlyList<WellSignals> ntcWells)
    {
        Positive100Wells = Array.AsReadOnly((positive100Wells ?? []).ToArray());
        NtcWells = Array.AsReadOnly((ntcWells ?? []).ToArray());
    }
}

/// <summary>
/// Per-well, per-channel outcome of one 100% positive well: its plateau
/// evaluation, its Fm_i (the window mean, populated only when the plateau
/// passed), and the endpoint point index selected for that series
/// (Method-Spec §5).
/// </summary>
public sealed record PositiveWellResult
{
    public WellId Well { get; }
    public FluorescenceChannel Channel { get; }
    public PlateauEvaluation Plateau { get; }
    public double? Fm { get; }
    public int? EndpointPointIndex { get; }
    public SignalPoint? Endpoint { get; }
    public IReadOnlyList<EvaluatedPoint> WindowPoints { get; }

    public PositiveWellResult(WellId well, FluorescenceChannel channel, PlateauEvaluation plateau, double? fm, int? endpointPointIndex,
        SignalPoint? endpoint = null, IReadOnlyList<EvaluatedPoint>? windowPoints = null)
    {
        Well = well;
        Channel = channel;
        Plateau = plateau;
        Fm = fm;
        EndpointPointIndex = endpointPointIndex;
        Endpoint = endpoint;
        WindowPoints = Array.AsReadOnly((windowPoints ?? []).ToArray());
    }
}

/// <summary>One out-of-band point of an NTC full-course QC evaluation (Method-Spec §7.4).</summary>
public sealed record NtcPointDiagnostic
{
    /// <summary>Index of the point within the well/channel series.</summary>
    public int PointIndex { get; }

    /// <summary>Elapsed time of the offending point, in minutes.</summary>
    public double ElapsedMinutes { get; }

    /// <summary>The normalized value Y(t) that fell outside the acceptance band.</summary>
    public double Y { get; }

    public double BandMin { get; }
    public double BandMax { get; }

    public NtcPointDiagnostic(int pointIndex, double elapsedMinutes, double y, double bandMin, double bandMax)
    {
        PointIndex = pointIndex;
        ElapsedMinutes = elapsedMinutes;
        Y = y;
        BandMin = bandMin;
        BandMax = bandMax;
    }
}

/// <summary>
/// Per-well, per-channel outcome of one NTC well: its own endpoint Fb_i, the
/// number of points checked by the full-course QC, and every out-of-band
/// point (Method-Spec §7).
/// </summary>
public sealed record NtcWellResult
{
    public WellId Well { get; }
    public FluorescenceChannel Channel { get; }
    public double Fb { get; }
    public int EndpointPointIndex { get; }

    /// <summary>Count of points evaluated by the 0–120 min full-course QC.</summary>
    public int QcPointCount { get; }

    public IReadOnlyList<NtcPointDiagnostic> OutOfBandPoints { get; }
    public SignalPoint? Endpoint { get; }
    public IReadOnlyList<EvaluatedPoint> QcPoints { get; }
    public double? MinimumY { get; }
    public double? MaximumY { get; }
    public double? Denominator { get; }

    public NtcWellResult(WellId well, FluorescenceChannel channel, double fb, int endpointPointIndex, int qcPointCount, IReadOnlyList<NtcPointDiagnostic> outOfBandPoints,
        SignalPoint? endpoint = null, IReadOnlyList<EvaluatedPoint>? qcPoints = null, double? minimumY = null, double? maximumY = null, double? denominator = null)
    {
        Well = well;
        Channel = channel;
        Fb = fb;
        EndpointPointIndex = endpointPointIndex;
        QcPointCount = qcPointCount;
        OutOfBandPoints = Array.AsReadOnly(outOfBandPoints.ToArray());
        Endpoint = endpoint; QcPoints = Array.AsReadOnly((qcPoints ?? []).ToArray());
        MinimumY = minimumY; MaximumY = maximumY; Denominator = denominator;
    }
}

/// <summary>
/// Outcome of the calibration stage (Method-Spec §5/§7). A FAIL status is a
/// normal scientific value, not a failed operation: per-well results and
/// diagnostics describe which wells failed. Run-level dictionaries are
/// populated only up to the stage the failure occurred in (Fm before NTC QC,
/// Fb only after every NTC well passed); consumers must check
/// <see cref="QcStatus"/> before reading them.
/// </summary>
public sealed record CalibrationResult
{
    public IReadOnlyList<PositiveWellResult> PositiveWells { get; }
    public IReadOnlyList<NtcWellResult> NtcWells { get; }

    /// <summary>Run-level Fm per channel; empty when the plateau stage failed.</summary>
    public IReadOnlyDictionary<FluorescenceChannel, double> RunFm { get; }

    /// <summary>Run-level Fb per channel; empty when any NTC well failed.</summary>
    public IReadOnlyDictionary<FluorescenceChannel, double> RunFb { get; }

    public QcStatus QcStatus { get; }
    public IReadOnlyList<WellDiagnostic> Diagnostics { get; }

    public CalibrationResult(
        IReadOnlyList<PositiveWellResult> positiveWells,
        IReadOnlyList<NtcWellResult> ntcWells,
        IReadOnlyDictionary<FluorescenceChannel, double> runFm,
        IReadOnlyDictionary<FluorescenceChannel, double> runFb,
        QcStatus qcStatus,
        IReadOnlyList<WellDiagnostic> diagnostics)
    {
        PositiveWells = Array.AsReadOnly(positiveWells.ToArray());
        NtcWells = Array.AsReadOnly(ntcWells.ToArray());
        RunFm = new System.Collections.ObjectModel.ReadOnlyDictionary<FluorescenceChannel, double>(runFm.ToDictionary());
        RunFb = new System.Collections.ObjectModel.ReadOnlyDictionary<FluorescenceChannel, double>(runFb.ToDictionary());
        QcStatus = qcStatus;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
}
