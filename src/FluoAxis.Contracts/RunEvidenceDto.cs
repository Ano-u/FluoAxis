namespace FluoAxis.Contracts;

using System.Collections.Frozen;

/// <summary>Honest provenance states for legacy persisted traces; neither state asserts complete scientific lineage.</summary>
public static class ProvenanceCompletenessNames
{
    public const string Unavailable = "unavailable";
    public const string StoredPartial = "storedPartial";
    public const string StoredComplete = "storedComplete";

    public static FrozenSet<string> All { get; } = new[] { Unavailable, StoredPartial, StoredComplete }.ToFrozenSet();
}

/// <summary>The persisted mapping in logical source coordinates. No external source path is carried.</summary>
public sealed record RunMappingDto
{
    public ChannelRegionMappingDto Fam { get; }
    public ChannelRegionMappingDto Rox { get; }
    public ImportOptionsDto? Options { get; }

    public RunMappingDto(ChannelRegionMappingDto fam, ChannelRegionMappingDto rox, ImportOptionsDto? options = null)
    {
        ArgumentNullException.ThrowIfNull(fam);
        ArgumentNullException.ThrowIfNull(rox);
        if (fam.Channel != ChannelNames.Fam || rox.Channel != ChannelNames.Rox)
        {
            throw new ArgumentException("Evidence mapping must identify FAM and ROX channels.", nameof(fam));
        }

        Fam = fam;
        Rox = rox;
        Options = options;
    }
}

/// <summary>One stored, full-precision intermediate quantity, retained in its persisted order.</summary>
public sealed record TraceValueDto
{
    public string Name { get; }
    public double Value { get; }

    public TraceValueDto(string name, double value)
    {
        ContractGuard.RequireNonBlank(name, nameof(name));
        if (!double.IsFinite(value)) throw new ArgumentException("Trace values must be finite.", nameof(value));
        Name = name;
        Value = value;
    }
}

/// <summary>
/// Stored calculation evidence, never a reconstructed calculation. Schema 3
/// records exact input identity and both channel endpoints. Older evidence
/// without those fields stays storedPartial; missing source indices stay null.
/// </summary>
public sealed record CalculationTraceDto
{
    public string TraceId { get; }
    public string Algorithm { get; }
    public string MethodSnapshotHash { get; }
    public string Well { get; }
    public string Channel { get; }
    public int? SourcePointIndex { get; }
    public IReadOnlyList<TraceValueDto> IntermediateValues { get; }
    public string ComparisonOperator { get; }
    public double ThresholdRaw { get; }
    public double? ResultRaw { get; }
    public string FinalStatus { get; }
    public string ProvenanceCompleteness { get; }
    public TraceProvenanceDto? Provenance { get; }

    public CalculationTraceDto(string traceId, string algorithm, string methodSnapshotHash,
        string well, string channel, int? sourcePointIndex, IReadOnlyList<TraceValueDto> intermediateValues,
        string comparisonOperator, double thresholdRaw, double? resultRaw, string finalStatus,
        string provenanceCompleteness = ProvenanceCompletenessNames.StoredPartial, TraceProvenanceDto? provenance = null)
    {
        ContractGuard.RequireNonBlank(traceId, nameof(traceId));
        ContractGuard.RequireNonBlank(algorithm, nameof(algorithm));
        ContractGuard.RequireSha256(methodSnapshotHash, nameof(methodSnapshotHash));
        ContractGuard.RequireNonBlank(well, nameof(well));
        ContractGuard.RequireWellId(well);
        ContractGuard.RequireNonBlank(channel, nameof(channel));
        ContractGuard.RequireChannel(channel);
        if (sourcePointIndex is < 0) throw new ArgumentOutOfRangeException(nameof(sourcePointIndex));
        ArgumentNullException.ThrowIfNull(intermediateValues);
        if (intermediateValues.Any(value => value is null)) throw new ArgumentException("Trace values must not contain null entries.", nameof(intermediateValues));
        ContractGuard.RequireNonBlank(comparisonOperator, nameof(comparisonOperator));
        if (!double.IsFinite(thresholdRaw) || (resultRaw is { } result && !double.IsFinite(result)))
            throw new ArgumentException("Trace threshold and result must be finite.", nameof(thresholdRaw));
        ContractGuard.RequireInVocabulary(finalStatus, StatusNames.All, nameof(finalStatus), "Status");
        if (provenanceCompleteness is not (ProvenanceCompletenessNames.StoredPartial or ProvenanceCompletenessNames.StoredComplete)
            || provenanceCompleteness == ProvenanceCompletenessNames.StoredComplete && provenance is null)
            throw new ArgumentException("Complete provenance requires recorded endpoints and input identity.", nameof(provenanceCompleteness));
        if (provenance is not null && (provenance.Identity.MethodSnapshotHash != methodSnapshotHash
            || provenance.Identity.Algorithm != algorithm
            || provenance.SourcePoints.Any(point => point.Well != well)
            || sourcePointIndex is { } recordedIndex && !provenance.SourcePoints.Any(point => point.Channel == channel && point.PointIndex == recordedIndex)))
            throw new ArgumentException("Trace provenance does not match its well, method, algorithm or primary endpoint.", nameof(provenance));
        if (provenanceCompleteness == ProvenanceCompletenessNames.StoredComplete
            && provenance!.SourcePoints.Any(point => point.SourceRowIndex is null || point.SourceColumnIndex is null))
            throw new ArgumentException("Complete provenance requires physical source coordinates.", nameof(provenanceCompleteness));

        TraceId = traceId;
        Algorithm = algorithm;
        MethodSnapshotHash = methodSnapshotHash.ToLowerInvariant();
        Well = well;
        Channel = channel;
        SourcePointIndex = sourcePointIndex;
        IntermediateValues = Array.AsReadOnly(intermediateValues.ToArray());
        ComparisonOperator = comparisonOperator;
        ThresholdRaw = thresholdRaw;
        ResultRaw = resultRaw;
        FinalStatus = finalStatus;
        ProvenanceCompleteness = provenanceCompleteness;
        Provenance = provenance;
    }
}

/// <summary>
/// A revision-consistent read of persisted Run evidence. Empty collections and
/// null stages mean no stored evidence exists. Selected raw endpoints are
/// included in provenance; full time courses remain bounded separate queries.
/// </summary>
public sealed record RunEvidenceDto
{
    public string ProjectName { get; }
    public RunSnapshotDto Snapshot { get; }
    public RunMappingDto? Mapping { get; }
    public IReadOnlyList<WellAssignmentDto> Assignments { get; }
    public CalibrationResultDto? Calibration { get; }
    public ControlQcResultDto? Control { get; }
    public IReadOnlyList<SampleAnalysisDto> Samples { get; }
    public IReadOnlyList<CalculationTraceDto> Traces { get; }
    public string ProvenanceCompleteness { get; }
    public ScientificEvidenceDto? ScientificEvidence { get; }

    public RunEvidenceDto(string projectName, RunSnapshotDto snapshot, RunMappingDto? mapping,
        IReadOnlyList<WellAssignmentDto> assignments, CalibrationResultDto? calibration, ControlQcResultDto? control,
        IReadOnlyList<SampleAnalysisDto> samples, IReadOnlyList<CalculationTraceDto> traces, string provenanceCompleteness,
        ScientificEvidenceDto? scientificEvidence = null)
    {
        ContractGuard.RequireNonBlank(projectName, nameof(projectName));
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(traces);
        if (assignments.Any(value => value is null) || samples.Any(value => value is null) || traces.Any(value => value is null))
            throw new ArgumentException("Evidence collections must not contain null entries.", nameof(assignments));
        ContractGuard.RequireInVocabulary(provenanceCompleteness, ProvenanceCompletenessNames.All, nameof(provenanceCompleteness), "ProvenanceCompleteness");
        string expectedCompleteness = traces.Count == 0 ? ProvenanceCompletenessNames.Unavailable : ProvenanceCompletenessNames.StoredPartial;
        if (provenanceCompleteness != expectedCompleteness && !(provenanceCompleteness == ProvenanceCompletenessNames.StoredComplete && scientificEvidence is not null))
            throw new ArgumentException("Provenance completeness must reflect the stored traces.", nameof(provenanceCompleteness));

        ProjectName = projectName;
        Snapshot = snapshot;
        Mapping = mapping;
        Assignments = Array.AsReadOnly(assignments.ToArray());
        Calibration = calibration;
        Control = control;
        Samples = Array.AsReadOnly(samples.ToArray());
        Traces = Array.AsReadOnly(traces.ToArray());
        ProvenanceCompleteness = provenanceCompleteness;
        ScientificEvidence = scientificEvidence;
    }
}
