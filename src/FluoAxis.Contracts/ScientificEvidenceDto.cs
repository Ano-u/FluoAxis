namespace FluoAxis.Contracts;

/// <summary>Versioned identity of the exact source, mapping, assignments and raw series consumed by Analysis.</summary>
public sealed record AnalysisInputIdentityDto
{
    public int Version { get; }
    public string SourceSha256 { get; }
    public string MappingSha256 { get; }
    public string InputSnapshotSha256 { get; }
    public string MethodSnapshotHash { get; }
    public string Algorithm { get; }
    public int InputRevision { get; }
    public AnalysisInputIdentityDto(string sourceSha256, string mappingSha256, string inputSnapshotSha256,
        string methodSnapshotHash, string algorithm, int inputRevision, int version = 1)
    {
        if (version != 1) throw new ArgumentException("Unsupported analysis evidence identity version.", nameof(version));
        foreach (string hash in new[] { sourceSha256, mappingSha256, inputSnapshotSha256, methodSnapshotHash }) ContractGuard.RequireSha256(hash, nameof(sourceSha256));
        ContractGuard.RequireNonBlank(algorithm, nameof(algorithm));
        ContractGuard.RequireNonNegative(inputRevision, nameof(inputRevision));
        Version = version; SourceSha256 = sourceSha256; MappingSha256 = mappingSha256;
        InputSnapshotSha256 = inputSnapshotSha256; MethodSnapshotHash = methodSnapshotHash; Algorithm = algorithm; InputRevision = inputRevision;
    }
}

/// <summary>Actual selected source observation. Row and column are zero-based physical source coordinates, never inferred for legacy records.</summary>
public sealed record SourcePointEvidenceDto
{
    public string Well { get; }
    public string Channel { get; }
    public int PointIndex { get; }
    public long ElapsedTicks { get; }
    public double RawRfu { get; }
    public string SheetName { get; }
    public int? SourceRowIndex { get; }
    public int? SourceColumnIndex { get; }
    public SourcePointEvidenceDto(string well, string channel, int pointIndex, long elapsedTicks, double rawRfu,
        string sheetName, int? sourceRowIndex = null, int? sourceColumnIndex = null)
    {
        ContractGuard.RequireNonBlank(well, nameof(well)); ContractGuard.RequireWellId(well);
        ContractGuard.RequireNonBlank(channel, nameof(channel)); ContractGuard.RequireChannel(channel);
        ContractGuard.RequireNonNegative(pointIndex, nameof(pointIndex)); ContractGuard.RequireNonBlank(sheetName, nameof(sheetName));
        if (!double.IsFinite(rawRfu) || sourceRowIndex is < 0 || sourceColumnIndex is < 0) throw new ArgumentException("Invalid source point evidence.");
        Well = well; Channel = channel; PointIndex = pointIndex; ElapsedTicks = elapsedTicks; RawRfu = rawRfu;
        SheetName = sheetName; SourceRowIndex = sourceRowIndex; SourceColumnIndex = sourceColumnIndex;
    }
}

public sealed record SourceWindowEvidenceDto
{
    public double StartMinutes { get; }
    public double EndMinutes { get; }
    public int FirstPointIndex { get; }
    public int LastPointIndex { get; }
    public int PointCount { get; }
    public SourceWindowEvidenceDto(double startMinutes, double endMinutes, int firstPointIndex, int lastPointIndex, int pointCount)
    {
        if (!double.IsFinite(startMinutes) || !double.IsFinite(endMinutes) || startMinutes > endMinutes
            || firstPointIndex < 0 || lastPointIndex < firstPointIndex || pointCount < 1 || pointCount != lastPointIndex - firstPointIndex + 1)
            throw new ArgumentException("Invalid selected analysis window.");
        StartMinutes = startMinutes; EndMinutes = endMinutes; FirstPointIndex = firstPointIndex; LastPointIndex = lastPointIndex; PointCount = pointCount;
    }
}

/// <summary>Bounded per-well summary; the selected/evaluated time-course points are retrieved separately by page.</summary>
public sealed record WellScientificEvidenceDto
{
    public string Well { get; }
    public string Channel { get; }
    public string Role { get; }
    public string Status { get; }
    public SourcePointEvidenceDto Endpoint { get; }
    public SourceWindowEvidenceDto? Window { get; }
    public IReadOnlyList<TraceValueDto> Values { get; }
    public IReadOnlyList<string> ErrorCodes { get; }
    public WellScientificEvidenceDto(string well, string channel, string role, string status, SourcePointEvidenceDto endpoint,
        SourceWindowEvidenceDto? window, IReadOnlyList<TraceValueDto> values, IReadOnlyList<string> errorCodes)
    {
        ContractGuard.RequireWellId(well); ContractGuard.RequireChannel(channel);
        if (!WellRoleNames.ToDomainRole.ContainsKey(role)) throw new ArgumentException("Invalid evidence role.", nameof(role));
        ContractGuard.RequireInVocabulary(status, StatusNames.All, nameof(status), "Status");
        ArgumentNullException.ThrowIfNull(endpoint); ArgumentNullException.ThrowIfNull(values); ArgumentNullException.ThrowIfNull(errorCodes);
        if (endpoint.Well != well || endpoint.Channel != channel || values.Count > 64 || errorCodes.Count > 64) throw new ArgumentException("Inconsistent well evidence.");
        foreach (string code in errorCodes) ContractGuard.RequireErrorCode(code);
        Well = well; Channel = channel; Role = role; Status = status; Endpoint = endpoint; Window = window;
        Values = Array.AsReadOnly(values.ToArray()); ErrorCodes = Array.AsReadOnly(errorCodes.ToArray());
    }
}

public sealed record AnalysisStageEvidenceDto
{
    public string Stage { get; }
    public AnalysisInputIdentityDto Identity { get; }
    public IReadOnlyList<WellScientificEvidenceDto> Wells { get; }
    public string ProvenanceCompleteness { get; }
    public string? EvaluatedPointsSha256 { get; }
    public AnalysisStageEvidenceDto(string stage, AnalysisInputIdentityDto identity, IReadOnlyList<WellScientificEvidenceDto> wells, string provenanceCompleteness,
        string? evaluatedPointsSha256 = null)
    {
        if (stage is not ("calibration" or "control" or "sample")) throw new ArgumentException("Invalid scientific evidence stage.", nameof(stage));
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(wells);
        if (wells.Count > 192 || wells.Any(well => well is null)) throw new ArgumentException("Scientific evidence exceeds the plate size or contains a missing well.", nameof(wells));
        ContractGuard.RequireInVocabulary(provenanceCompleteness, ProvenanceCompletenessNames.All, nameof(provenanceCompleteness), "ProvenanceCompleteness");
        if (provenanceCompleteness == ProvenanceCompletenessNames.StoredComplete
            && wells.Any(well => well.Endpoint.SourceRowIndex is null || well.Endpoint.SourceColumnIndex is null))
            throw new ArgumentException("Complete stage evidence requires recorded source coordinates.");
        if (evaluatedPointsSha256 is not null) ContractGuard.RequireSha256(evaluatedPointsSha256, nameof(evaluatedPointsSha256));
        Stage = stage; Identity = identity; Wells = Array.AsReadOnly(wells.ToArray()); ProvenanceCompleteness = provenanceCompleteness;
        EvaluatedPointsSha256 = evaluatedPointsSha256;
    }
}

public sealed record ScientificEvidenceDto
{
    public int Version { get; }
    public IReadOnlyList<AnalysisStageEvidenceDto> Stages { get; }
    public ScientificEvidenceDto(IReadOnlyList<AnalysisStageEvidenceDto> stages, int version = 1)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (version != 1 || stages.Count > 3 || stages.Any(stage => stage is null) || stages.Select(stage => stage.Stage).Distinct(StringComparer.Ordinal).Count() != stages.Count)
            throw new ArgumentException("Invalid scientific evidence version or stage set.");
        Version = version; Stages = Array.AsReadOnly(stages.ToArray());
    }
}

public sealed record TraceProvenanceDto
{
    public AnalysisInputIdentityDto Identity { get; }
    public IReadOnlyList<SourcePointEvidenceDto> SourcePoints { get; }
    public IReadOnlyList<string> ErrorCodes { get; }
    public TraceProvenanceDto(AnalysisInputIdentityDto identity, IReadOnlyList<SourcePointEvidenceDto> sourcePoints, IReadOnlyList<string> errorCodes)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(sourcePoints); ArgumentNullException.ThrowIfNull(errorCodes);
        if (sourcePoints.Count != 2 || sourcePoints.Any(point => point is null) || sourcePoints.Select(point => point.Channel).Distinct().Count() != 2) throw new ArgumentException("A trace needs both channel endpoints.");
        foreach (string code in errorCodes) ContractGuard.RequireErrorCode(code);
        Identity = identity; SourcePoints = Array.AsReadOnly(sourcePoints.ToArray()); ErrorCodes = Array.AsReadOnly(errorCodes.ToArray());
    }
}

public sealed record ScientificPointDto
{
    public int PointIndex { get; }
    public long ElapsedTicks { get; }
    public double RawRfu { get; }
    public double? Normalized { get; }
    public int? SourceRowIndex { get; }
    public int? SourceColumnIndex { get; }
    public bool? WithinBand { get; }
    public ScientificPointDto(int pointIndex, long elapsedTicks, double rawRfu, double? normalized, int? sourceRowIndex, int? sourceColumnIndex, bool? withinBand = null)
    {
        if (pointIndex < 0 || !double.IsFinite(rawRfu) || normalized is { } y && !double.IsFinite(y)
            || sourceRowIndex is < 0 || sourceColumnIndex is < 0) throw new ArgumentException("Invalid scientific point.");
        PointIndex = pointIndex; ElapsedTicks = elapsedTicks; RawRfu = rawRfu; Normalized = normalized;
        SourceRowIndex = sourceRowIndex; SourceColumnIndex = sourceColumnIndex;
        WithinBand = withinBand;
    }
}

public sealed record ScientificPointPageDto
{
    public Guid RunId { get; }
    public int Revision { get; }
    public string Stage { get; }
    public string Well { get; }
    public string Channel { get; }
    public int Offset { get; }
    public int TotalPoints { get; }
    public IReadOnlyList<ScientificPointDto> Points { get; }
    public ScientificPointPageDto(Guid runId, int revision, string stage, string well, string channel, int offset, int totalPoints, IReadOnlyList<ScientificPointDto> points)
    {
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId)); ContractGuard.RequireNonNegative(revision, nameof(revision));
        ContractGuard.RequireNonBlank(well, nameof(well)); ContractGuard.RequireNonBlank(channel, nameof(channel));
        ContractGuard.RequireWellId(well); ContractGuard.RequireChannel(channel);
        ArgumentNullException.ThrowIfNull(points);
        if (stage is not ("calibration" or "control" or "sample") || offset < 0 || totalPoints < 0 || points.Count > 250 || points.Count > totalPoints || points.Any(point => point is null)) throw new ArgumentException("Invalid scientific evidence page bounds.");
        RunId = runId; Revision = revision; Stage = stage; Well = well; Channel = channel; Offset = offset; TotalPoints = totalPoints;
        Points = Array.AsReadOnly(points.ToArray());
    }
}

public interface IScientificEvidenceGateway
{
    Task<GatewayResult<ScientificPointPageDto>> GetScientificPointPageAsync(Guid runId, string stage, string well,
        string channel, int offset, int pageSize, CancellationToken cancellationToken);
}
