namespace FluoAxis.Contracts;

/// <summary>Read-only recognition evidence. No Run has been changed and no scientific calculation has run.</summary>
public sealed record AutomaticImportProposalDto
{
    public string DetectedFormat { get; }
    public ImportOptionsDto? Options { get; }
    public ChannelRegionMappingDto? Fam { get; }
    public ChannelRegionMappingDto? Rox { get; }
    public IReadOnlyList<WellAssignmentDto> Assignments { get; }
    public IReadOnlyList<IssueDto> Issues { get; }
    public IReadOnlyList<string> Reasons { get; }
    /// <summary>True only when the parsing choices and paired layout are complete. A role profile cannot fix false.</summary>
    public bool MappingReady { get; }
    public bool RequiresReview { get; }

    public AutomaticImportProposalDto(string detectedFormat, ImportOptionsDto? options,
        ChannelRegionMappingDto? fam, ChannelRegionMappingDto? rox,
        IReadOnlyList<WellAssignmentDto> assignments, IReadOnlyList<IssueDto> issues,
        IReadOnlyList<string> reasons, bool mappingReady, bool requiresReview)
    {
        if (detectedFormat is not ("xlsx" or "xls" or "delimitedText" or "unknown"))
            throw new ArgumentException("Unsupported recognition format.", nameof(detectedFormat));
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(reasons);
        if (assignments.Count > 96 || issues.Count > 128 || reasons.Count > 128
            || assignments.Any(item => item is null) || issues.Any(item => item is null)
            || reasons.Any(reason => string.IsNullOrWhiteSpace(reason) || reason.Length > 1024))
            throw new ArgumentException("Recognition evidence exceeds its limits.", nameof(assignments));
        if (mappingReady && (options is null || fam is null || rox is null)
            || !mappingReady && !requiresReview)
            throw new ArgumentException("Recognition readiness requires complete parsing and channel mappings.", nameof(mappingReady));
        DetectedFormat = detectedFormat; Options = options; Fam = fam; Rox = rox;
        Assignments = Array.AsReadOnly(assignments.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        Reasons = Array.AsReadOnly(reasons.ToArray());
        MappingReady = mappingReady; RequiresReview = requiresReview;
    }
}
