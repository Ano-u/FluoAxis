namespace FluoAxis.Application;

using FluoAxis.Contracts;
using FluoAxis.Domain;

public sealed partial class ApplicationGateway
{
    /// <summary>
    /// Reads persisted evidence without invoking Analysis or loading Raw RFU.
    /// Two revision observations prevent publishing evidence assembled across
    /// an atomic workflow mutation. The caller may retry a stale read.
    /// </summary>
    public async Task<GatewayResult<RunEvidenceDto>> GetRunEvidenceAsync(GetRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        GatewayResult<RunEvidenceDto>? versionFailure = ValidateVersion<RunEvidenceDto>(query.ContractVersion);
        if (versionFailure is not null) return versionFailure;

        ProjectRunAggregate? before = await repository.GetByRunIdAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        if (before is null)
        {
            return Fail<RunEvidenceDto>(ErrorCodes.ProjectRunNotFound,
                "The requested Run does not exist.", "Select an existing Run from the project list.");
        }

        PreparedSourceIdentity? source = await repository.GetSourceAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        PreparedDataMapping? mapping = await repository.GetMappingAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WellAssignment> assignments = await repository.GetAssignmentsAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        PersistedCalibration? calibration = await repository.GetCalibrationAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        PersistedControl? control = await repository.GetControlAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PersistedSample> samples = await repository.GetSamplesAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PersistedTrace> traces = await repository.GetTracesAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AnalysisStageEvidenceDto> scientific = await repository.GetScientificStagesAsync(query.RunId, cancellationToken).ConfigureAwait(false);

        ProjectRunAggregate? after = await repository.GetByRunIdAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        if (after is null || before.RunRevision != after.RunRevision || before.RunState != after.RunState
            || before.Method.ComputeCanonicalHash() != after.Method.ComputeCanonicalHash())
        {
            return Fail<RunEvidenceDto>(ErrorCodes.SystemStaleRunRevision,
                "The Run changed while its evidence was being read.", "Refresh the Run evidence and retry.");
        }

        string methodHash = before.Method.ComputeCanonicalHash();
        string? mappingHash = mapping is null ? null : ScientificEvidenceCapture.MappingHash(mapping);
        if ((calibration is not null && !string.Equals(calibration.MethodSnapshotHash, methodHash, StringComparison.Ordinal))
            || traces.Any(trace => !string.Equals(trace.MethodSnapshotHash, methodHash, StringComparison.Ordinal))
            || scientific.Any(stage => stage.Identity.MethodSnapshotHash != methodHash || stage.Identity.SourceSha256 != source?.Sha256
                || stage.Identity.InputRevision >= before.RunRevision || stage.Identity.MappingSha256 != mappingHash)
            || scientific.Select(stage => stage.Identity.InputSnapshotSha256).Distinct(StringComparer.Ordinal).Count() > 1
            || traces.Any(trace => trace.Provenance is { } provenance && (provenance.Identity.MethodSnapshotHash != methodHash
                || provenance.Identity.SourceSha256 != source?.Sha256 || provenance.Identity.MappingSha256 != mappingHash
                || !scientific.Any(stage => stage.Identity == provenance.Identity))))
        {
            return Fail<RunEvidenceDto>(ErrorCodes.SystemInvalidRunState,
                "Stored calibration or calculation evidence does not belong to this Run's Method snapshot.",
                "Keep the existing project unchanged and review its stored Method and evidence identities.");
        }

        try
        {
            // Older records can lack a mapping or traces. Use only stored
            // identity/assignment metadata; never load or reconstruct signals.
            int totalWells = mapping?.Fam.WellColumns.Values.Distinct().Count()
                ?? assignments.Select(assignment => assignment.Well).Distinct().Count();
            RunSnapshotDto snapshot = BuildSnapshot(before, source, assignments, totalWells, calibration, control, samples);
            return GatewayResult.Ok(new RunEvidenceDto(
                before.ProjectName,
                snapshot,
                mapping is null ? null : new RunMappingDto(ToEvidenceRegion(mapping.Fam), ToEvidenceRegion(mapping.Rox), mapping.Options),
                [.. assignments.Select(ToEvidenceAssignment)],
                calibration is null ? null : ToCalibrationDto(calibration),
                control is null ? null : ToControlDto(control),
                [.. samples.Select(ToSampleDto)],
                [.. traces.Select(ToEvidenceTrace)],
                Completeness(before, scientific, traces),
                scientific.Count == 0 ? null : new ScientificEvidenceDto(scientific)));
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            return Fail<RunEvidenceDto>(ErrorCodes.SystemInvalidRunState,
                "Stored Run evidence contains invalid values and cannot be displayed safely.",
                "Keep the existing project unchanged and review its stored evidence.");
        }
    }

    private static ChannelRegionMappingDto ToEvidenceRegion(PreparedChannelRegion region) => new(
        ChannelName(region.Channel), region.SheetName, region.HeaderRowIndex, region.TimeColumnIndex,
        [.. region.WellColumns.OrderBy(pair => pair.Key).Select(pair => new WellColumnMappingDto(pair.Key, pair.Value.ToString()))], region.EndRowIndex);

    private static WellAssignmentDto ToEvidenceAssignment(WellAssignment assignment) => new(
        assignment.Well.ToString(), RoleName(assignment.Role), assignment.Sample?.SampleId,
        assignment.Sample?.Group, assignment.Sample?.ReplicateIndex, assignment.Sample?.Target, assignment.Sample?.Notes);

    private static CalculationTraceDto ToEvidenceTrace(PersistedTrace trace) => new(
        trace.TraceId, trace.Algorithm, trace.MethodSnapshotHash, trace.Well,
        ChannelName((FluorescenceChannel)trace.Channel), trace.SourcePointIndex,
        [.. trace.IntermediateValues.Select(value => new TraceValueDto(value.Name, value.Value))],
        trace.ComparisonOperator, trace.ThresholdRaw, trace.ResultRaw, trace.FinalStatus,
        trace.Provenance is { } provenance && provenance.SourcePoints.All(point => point.SourceRowIndex is not null && point.SourceColumnIndex is not null)
            ? ProvenanceCompletenessNames.StoredComplete : ProvenanceCompletenessNames.StoredPartial, trace.Provenance);

    private static string Completeness(ProjectRunAggregate run, IReadOnlyList<AnalysisStageEvidenceDto> stages, IReadOnlyList<PersistedTrace> traces)
    {
        bool stageSet = run.RunState switch
        {
            RunState.CalibrationPassed => stages.Any(stage => stage.Stage == "calibration"),
            RunState.ReadyForAnalysis => stages.Any(stage => stage.Stage == "calibration") && stages.Any(stage => stage.Stage == "control"),
            RunState.Analyzed or RunState.Exported => stages.Count == 3,
            RunState.RunInvalid => stages.Any(stage => stage.Stage == "calibration"),
            _ => false,
        };
        int expectedTraces = stages.SelectMany(stage => stage.Wells).Count(well => well.Role is WellRoleNames.Lqc01Percent or WellRoleNames.Sample) / 2;
        return stageSet && traces.Count == expectedTraces && stages.All(stage => stage.ProvenanceCompleteness == ProvenanceCompletenessNames.StoredComplete)
            && traces.All(trace => trace.Provenance is { } provenance && provenance.SourcePoints.All(point => point.SourceRowIndex is not null && point.SourceColumnIndex is not null))
            ? ProvenanceCompletenessNames.StoredComplete
            : traces.Count == 0 ? ProvenanceCompletenessNames.Unavailable : ProvenanceCompletenessNames.StoredPartial;
    }

    public async Task<GatewayResult<ScientificPointPageDto>> GetScientificPointPageAsync(Guid runId, string stage, string well,
        string channel, int offset, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (runId == Guid.Empty || stage is not ("calibration" or "control" or "sample")
            || channel is not (ChannelNames.Fam or ChannelNames.Rox) || offset < 0 || pageSize is < 1 or > 250)
            return Fail<ScientificPointPageDto>(ErrorCodes.SystemInvalidRunState, "Scientific evidence page request is invalid.", "Select an existing stage and a page of at most 250 points.");
        try { well = WellId.Parse(well).ToString(); }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { return Fail<ScientificPointPageDto>(ErrorCodes.PlateRoleCardinality, "The well is invalid.", "Select an existing well."); }
        ProjectRunAggregate? before = await repository.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        if (before is null) return Fail<ScientificPointPageDto>(ErrorCodes.ProjectRunNotFound, "Run not found.", "Refresh the Run list.");
        PersistedScientificPointPage page = await repository.GetScientificPointsAsync(runId, stage, well, channel, offset, pageSize, cancellationToken).ConfigureAwait(false);
        ProjectRunAggregate? after = await repository.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        if (after?.RunRevision != before.RunRevision) return Fail<ScientificPointPageDto>(ErrorCodes.SystemStaleRunRevision, "Run changed during evidence read.", "Refresh the evidence page.");
        return GatewayResult.Ok(new ScientificPointPageDto(runId, before.RunRevision, stage, well, channel, offset, page.TotalPoints,
            Array.AsReadOnly(page.Points.Select(point => new ScientificPointDto(point.PointIndex, point.ElapsedTicks, point.RawRfu,
                point.Normalized, point.SourceRowIndex, point.SourceColumnIndex, point.WithinBand)).ToArray())));
    }

    private static string ChannelName(FluorescenceChannel channel) => channel switch
    {
        FluorescenceChannel.Fam => ChannelNames.Fam,
        FluorescenceChannel.Rox => ChannelNames.Rox,
        _ => throw new ArgumentException("The persisted channel is invalid.", nameof(channel)),
    };

    private static string RoleName(WellRole role) => role switch
    {
        WellRole.Ntc => WellRoleNames.Ntc,
        WellRole.Positive100 => WellRoleNames.Positive100,
        WellRole.WildType => WellRoleNames.WildType,
        WellRole.Lqc01Percent => WellRoleNames.Lqc01Percent,
        WellRole.Sample => WellRoleNames.Sample,
        WellRole.Ignore => WellRoleNames.Ignore,
        _ => throw new ArgumentException("The persisted well role is invalid.", nameof(role)),
    };
}
