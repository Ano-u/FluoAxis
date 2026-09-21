namespace FluoAxis.Contracts;

/// <summary>
/// Progress of a long-running read-only import inspection, reported through
/// <see cref="IProgress{T}"/> while the gateway call itself returns only the
/// final preview (plan.md §5.1). Stage strings are the closed
/// <see cref="ImportStageNames"/> vocabulary.
/// </summary>
public sealed record OperationProgressDto
{
    public string Stage { get; }
    public double? PercentComplete { get; }
    public string Message { get; }

    public OperationProgressDto(string stage, double? percentComplete = null, string message = "")
    {
        ContractGuard.RequireInVocabulary(stage, ImportStageNames.All, nameof(stage), "ImportStage");

        if (percentComplete is { } p && (!double.IsFinite(p) || p is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentComplete), percentComplete, "PercentComplete must be within [0, 100].");
        }

        Stage = stage;
        PercentComplete = percentComplete;
        Message = message;
    }
}

/// <summary>
/// Result of a read-only import inspection: discovered candidate regions,
/// detected time axis, and channel columns awaiting user confirmation
/// (plan.md §5.1/§7.1). Inspection never creates official run results.
/// </summary>
public sealed record ImportPreviewDto
{
    public string DetectedFormat { get; }
    public IReadOnlyList<string> SheetNames { get; }
    public IReadOnlyList<RegionCandidateDto> CandidateRegions { get; }
    public int CandidateRegionCount => CandidateRegions.Count;
    public IReadOnlyList<IssueDto> ValidationIssues { get; }

    public ImportPreviewDto(
        string detectedFormat,
        IReadOnlyList<string>? sheetNames = null,
        IReadOnlyList<RegionCandidateDto>? candidateRegions = null,
        IReadOnlyList<IssueDto>? validationIssues = null)
    {
        ContractGuard.RequireNonBlank(detectedFormat, nameof(detectedFormat));
        ImportPreviewDtoGuard(sheetNames, candidateRegions, validationIssues);

        DetectedFormat = detectedFormat;
        SheetNames = Array.AsReadOnly<string>([.. sheetNames ?? []]);
        CandidateRegions = Array.AsReadOnly<RegionCandidateDto>([.. candidateRegions ?? []]);
        ValidationIssues = Array.AsReadOnly<IssueDto>([.. validationIssues ?? []]);
    }

    private static void ImportPreviewDtoGuard(
        IReadOnlyList<string>? sheetNames,
        IReadOnlyList<RegionCandidateDto>? candidateRegions,
        IReadOnlyList<IssueDto>? validationIssues)
    {
        if (sheetNames is not null)
        {
            foreach (string name in sheetNames)
            {
                ContractGuard.RequireNonBlank(name, nameof(sheetNames));
            }
        }

        if (candidateRegions is not null && candidateRegions.Any(candidate => candidate is null))
        {
            throw new ArgumentException("Candidate regions must not contain null entries.", nameof(candidateRegions));
        }

        if (validationIssues is not null)
        {
            foreach (IssueDto issue in validationIssues)
            {
                ArgumentNullException.ThrowIfNull(issue, nameof(validationIssues));
            }
        }
    }
}

/// <summary>
/// Snapshot returned right after project creation (plan.md §5.1). The first
/// run of a new project starts in the "new" state with no source files.
/// </summary>
public sealed record ProjectSnapshotDto
{
    public Guid ProjectId { get; }
    public string ProjectName { get; }
    public Guid RunId { get; }
    public int RunRevision { get; }
    public string RunDomainState { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public ProjectSnapshotDto(
        Guid projectId,
        string projectName,
        Guid runId,
        int runRevision,
        string runDomainState,
        DateTimeOffset createdAtUtc)
    {
        ContractGuard.RequireNonEmptyGuid(projectId, nameof(projectId));
        ContractGuard.RequireNonBlank(projectName, nameof(projectName));
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId));
        ContractGuard.RequireNonNegative(runRevision, nameof(runRevision));
        ContractGuard.RequireInVocabulary(runDomainState, RunStateNames.All, nameof(runDomainState), "RunState");
        ContractGuard.RequireUtc(createdAtUtc, nameof(createdAtUtc));

        ProjectId = projectId;
        ProjectName = projectName;
        RunId = runId;
        RunRevision = runRevision;
        RunDomainState = runDomainState;
        CreatedAtUtc = createdAtUtc;
    }
}
