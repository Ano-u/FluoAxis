namespace FluoAxis.Analysis;

using FluoAxis.Domain;

/// <summary>
/// Immutable per-well audit trail linking the endpoint fluorescence to the
/// final classification (CLAUDE.md §13). Every intermediate quantity the
/// method defines is captured at full precision, in method order, so a
/// reviewer can reproduce the decision by hand.
/// </summary>
public sealed record CalculationTrace
{
    /// <summary>Algorithm identity of the engine that produced this trace; bump on any semantics change.</summary>
    public const string AlgorithmVersion = "BRAF-ENGINE-1.0";

    public Guid TraceId { get; }
    public string Algorithm { get; }
    public string MethodSnapshotHash { get; }
    public WellId SourceWell { get; }
    public FluorescenceChannel SourceChannel { get; }

    /// <summary>Index of the endpoint point within the source series (FAM series when both channels are involved).</summary>
    public int? SourcePointIndex { get; }

    /// <summary>Ordered intermediate values, in method computation order.</summary>
    public IReadOnlyList<KeyValuePair<string, double>> IntermediateValues { get; }

    /// <summary>Comparison operator of the classification rule, as applied.</summary>
    public string ComparisonOperator { get; }

    /// <summary>The raw dr' threshold, without rounding (Method-Spec §3.4).</summary>
    public double ThresholdRaw { get; }

    /// <summary>The raw dr' result, without rounding.</summary>
    public double? ResultRaw { get; }

    public QcStatus FinalStatus { get; }

    /// <summary>Structured error codes raised while computing this well, if any.</summary>
    public IReadOnlyList<string> ErrorCodes { get; }

    public CalculationTrace(
        Guid traceId,
        string algorithm,
        string methodSnapshotHash,
        WellId sourceWell,
        FluorescenceChannel sourceChannel,
        int? sourcePointIndex,
        IReadOnlyList<KeyValuePair<string, double>> intermediateValues,
        string comparisonOperator,
        double thresholdRaw,
        double? resultRaw,
        QcStatus finalStatus,
        IReadOnlyList<string> errorCodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonOperator);

        TraceId = traceId;
        Algorithm = algorithm;
        MethodSnapshotHash = methodSnapshotHash;
        SourceWell = sourceWell;
        SourceChannel = sourceChannel;
        SourcePointIndex = sourcePointIndex;
        IntermediateValues = intermediateValues;
        ComparisonOperator = comparisonOperator;
        ThresholdRaw = thresholdRaw;
        ResultRaw = resultRaw;
        FinalStatus = finalStatus;
        ErrorCodes = errorCodes;
    }

    /// <summary>Looks up an intermediate value by its method-order key (e.g. "Y_FAM").</summary>
    public double? GetIntermediate(string key)
    {
        foreach (KeyValuePair<string, double> pair in IntermediateValues)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
