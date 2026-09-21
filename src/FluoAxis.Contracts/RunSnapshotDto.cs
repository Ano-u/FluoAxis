namespace FluoAxis.Contracts;

using System.Collections.Frozen;
using FluoAxis.Domain;

/// <summary>
/// Read model of one guided-analysis run as consumed by the frontend
/// (plan.md §5.3). All UI button availability derives from
/// <see cref="AllowedActions"/>; the ViewModel never re-derives state
/// transitions or QC conditions. Each stage summary is null until that
/// stage has run.
/// </summary>
public sealed record RunSnapshotDto
{
    public Guid RunId { get; }
    public int Revision { get; }
    public string DomainState { get; }
    public IReadOnlyList<string> AllowedActions { get; }
    public IReadOnlyList<IssueDto> BlockingIssues { get; }
    public IReadOnlyList<IssueDto> WarningIssues { get; }
    public string MethodId { get; }
    public string MethodVersion { get; }
    public string MethodSnapshotHash { get; }
    public IReadOnlyList<SourceFileDto> SourceFiles { get; }
    public MappingSummaryDto? MappingSummary { get; }
    public CalibrationSummaryDto? CalibrationSummary { get; }
    public ControlSummaryDto? ControlSummary { get; }
    public AnalysisSummaryDto? AnalysisSummary { get; }
    public DateTimeOffset UpdatedUtc { get; }

    public RunSnapshotDto(
        Guid runId,
        int revision,
        string domainState,
        IReadOnlyList<string> allowedActions,
        IReadOnlyList<IssueDto> blockingIssues,
        IReadOnlyList<IssueDto> warningIssues,
        string methodId,
        string methodVersion,
        string methodSnapshotHash,
        IReadOnlyList<SourceFileDto> sourceFiles,
        MappingSummaryDto? mappingSummary = null,
        CalibrationSummaryDto? calibrationSummary = null,
        ControlSummaryDto? controlSummary = null,
        AnalysisSummaryDto? analysisSummary = null,
        DateTimeOffset updatedUtc = default)
    {
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId));
        ContractGuard.RequireNonNegative(revision, nameof(revision));
        ContractGuard.RequireNonBlank(domainState, nameof(domainState));
        ContractGuard.RequireNonBlank(methodId, nameof(methodId));
        ContractGuard.RequireNonBlank(methodVersion, nameof(methodVersion));
        ContractGuard.RequireSha256(methodSnapshotHash, nameof(methodSnapshotHash));

        ArgumentNullException.ThrowIfNull(allowedActions, nameof(allowedActions));
        ArgumentNullException.ThrowIfNull(blockingIssues, nameof(blockingIssues));
        ArgumentNullException.ThrowIfNull(warningIssues, nameof(warningIssues));
        ArgumentNullException.ThrowIfNull(sourceFiles, nameof(sourceFiles));

        foreach (string action in allowedActions)
        {
            if (!RunAction.All.Contains(action))
            {
                throw new ArgumentException(
                    $"'{action}' is not a member of the RunAction contract vocabulary.",
                    nameof(allowedActions));
            }
        }

        foreach (string state in new[] { domainState })
        {
            if (!RunStateNames.All.Contains(state))
            {
                throw new ArgumentException(
                    $"'{state}' is not a member of the RunState contract vocabulary.",
                    nameof(domainState));
            }
        }

        RunId = runId;
        Revision = revision;
        DomainState = domainState;
        AllowedActions = allowedActions;
        BlockingIssues = blockingIssues;
        WarningIssues = warningIssues;
        MethodId = methodId;
        MethodVersion = methodVersion;
        MethodSnapshotHash = methodSnapshotHash.ToLowerInvariant();
        SourceFiles = sourceFiles;
        MappingSummary = mappingSummary;
        CalibrationSummary = calibrationSummary;
        ControlSummary = controlSummary;
        AnalysisSummary = analysisSummary;
        UpdatedUtc = updatedUtc;
    }
}

/// <summary>camelCase wire names of <see cref="Domain.RunState"/>.</summary>
public static class RunStateNames
{
    public const string New = "new";
    public const string Imported = "imported";
    public const string Mapped = "mapped";
    public const string Calibrating = "calibrating";
    public const string CalibrationFailed = "calibrationFailed";
    public const string CalibrationPassed = "calibrationPassed";
    public const string ControlQc = "controlQc";
    public const string ControlQcFailed = "controlQcFailed";
    public const string ReadyForAnalysis = "readyForAnalysis";
    public const string Analyzing = "analyzing";
    public const string Analyzed = "analyzed";
    public const string ReplicateConflict = "replicateConflict";
    public const string Exported = "exported";
    public const string RunInvalid = "runInvalid";

    public static IReadOnlyDictionary<string, Domain.RunState> ToDomainState { get; } =
        new Dictionary<string, Domain.RunState>
        {
            [New] = Domain.RunState.New,
            [Imported] = Domain.RunState.Imported,
            [Mapped] = Domain.RunState.Mapped,
            [Calibrating] = Domain.RunState.Calibrating,
            [CalibrationFailed] = Domain.RunState.CalibrationFailed,
            [CalibrationPassed] = Domain.RunState.CalibrationPassed,
            [ControlQc] = Domain.RunState.ControlQc,
            [ControlQcFailed] = Domain.RunState.ControlQcFailed,
            [ReadyForAnalysis] = Domain.RunState.ReadyForAnalysis,
            [Analyzing] = Domain.RunState.Analyzing,
            [Analyzed] = Domain.RunState.Analyzed,
            [ReplicateConflict] = Domain.RunState.ReplicateConflict,
            [Exported] = Domain.RunState.Exported,
            [RunInvalid] = Domain.RunState.RunInvalid,
        };

    /// <summary>Maps a domain state to its wire name; round-trips with the dictionary.</summary>
    public static string FromDomain(Domain.RunState state) => state switch
    {
        Domain.RunState.New => New,
        Domain.RunState.Imported => Imported,
        Domain.RunState.Mapped => Mapped,
        Domain.RunState.Calibrating => Calibrating,
        Domain.RunState.CalibrationFailed => CalibrationFailed,
        Domain.RunState.CalibrationPassed => CalibrationPassed,
        Domain.RunState.ControlQc => ControlQc,
        Domain.RunState.ControlQcFailed => ControlQcFailed,
        Domain.RunState.ReadyForAnalysis => ReadyForAnalysis,
        Domain.RunState.Analyzing => Analyzing,
        Domain.RunState.Analyzed => Analyzed,
        Domain.RunState.ReplicateConflict => ReplicateConflict,
        Domain.RunState.Exported => Exported,
        Domain.RunState.RunInvalid => RunInvalid,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown RunState member."),
    };

    public static FrozenSet<string> All { get; } = ToDomainState.Keys.ToFrozenSet();
}

/// <summary>Mapping-stage summary of the run snapshot.</summary>
public sealed record MappingSummaryDto
{
    public int TotalWells { get; }
    public int AssignedWells { get; }
    public IReadOnlyList<string> SampleIds { get; }

    public MappingSummaryDto(int totalWells, int assignedWells, IReadOnlyList<string> sampleIds)
    {
        ContractGuard.RequireNonNegative(totalWells, nameof(totalWells));
        ContractGuard.RequireNonNegative(assignedWells, nameof(assignedWells));

        if (assignedWells > totalWells)
        {
            throw new ArgumentException("AssignedWells cannot exceed TotalWells.", nameof(assignedWells));
        }

        ArgumentNullException.ThrowIfNull(sampleIds, nameof(sampleIds));
        foreach (string sampleId in sampleIds)
        {
            ContractGuard.RequireNonBlank(sampleId, nameof(sampleIds));
        }

        TotalWells = totalWells;
        AssignedWells = assignedWells;
        SampleIds = sampleIds;
    }
}

/// <summary>Calibration-stage summary of the run snapshot.</summary>
public sealed record CalibrationSummaryDto
{
    public string Status { get; }
    public double? RunFmFam { get; }
    public double? RunFmRox { get; }
    public double? RunFbFam { get; }
    public double? RunFbRox { get; }

    public CalibrationSummaryDto(
        string status,
        double? runFmFam = null,
        double? runFmRox = null,
        double? runFbFam = null,
        double? runFbRox = null)
    {
        ContractGuard.RequireInVocabulary(status, StatusNames.All, nameof(status), "Status");

        foreach (double? value in new[] { runFmFam, runFmRox, runFbFam, runFbRox })
        {
            if (value is { } v && !double.IsFinite(v))
            {
                throw new ArgumentException("Calibration summary values must be finite numbers.", nameof(runFmFam));
            }
        }

        Status = status;
        RunFmFam = runFmFam;
        RunFmRox = runFmRox;
        RunFbFam = runFbFam;
        RunFbRox = runFbRox;
    }
}

/// <summary>Control-QC-stage summary of the run snapshot.</summary>
public sealed record ControlSummaryDto
{
    public string Status { get; }
    public double? WtYFamMean { get; }
    public double? WtYRoxMean { get; }
    public string? LqcCall { get; }
    public double? LqcMeanDrPrime { get; }

    public ControlSummaryDto(
        string status,
        double? wtYFamMean = null,
        double? wtYRoxMean = null,
        string? lqcCall = null,
        double? lqcMeanDrPrime = null)
    {
        ContractGuard.RequireInVocabulary(status, StatusNames.All, nameof(status), "Status");

        if (lqcCall is not null && !CallNames.All.Contains(lqcCall))
        {
            throw new ArgumentException(
                $"'{lqcCall}' is not a member of the Call contract vocabulary.",
                nameof(lqcCall));
        }

        foreach (double? value in new[] { wtYFamMean, wtYRoxMean, lqcMeanDrPrime })
        {
            if (value is { } v && !double.IsFinite(v))
            {
                throw new ArgumentException("Control summary values must be finite numbers.", nameof(wtYFamMean));
            }
        }

        Status = status;
        WtYFamMean = wtYFamMean;
        WtYRoxMean = wtYRoxMean;
        LqcCall = lqcCall;
        LqcMeanDrPrime = lqcMeanDrPrime;
    }
}

/// <summary>Sample-analysis-stage summary of the run snapshot.</summary>
public sealed record AnalysisSummaryDto
{
    public string Status { get; }
    public int SampleCount { get; }
    public int PositiveCount { get; }
    public int NegativeCount { get; }
    public int InvalidCount { get; }

    public AnalysisSummaryDto(string status, int sampleCount, int positiveCount, int negativeCount, int invalidCount)
    {
        ContractGuard.RequireInVocabulary(status, StatusNames.All, nameof(status), "Status");
        ContractGuard.RequireNonNegative(sampleCount, nameof(sampleCount));
        ContractGuard.RequireNonNegative(positiveCount, nameof(positiveCount));
        ContractGuard.RequireNonNegative(negativeCount, nameof(negativeCount));
        ContractGuard.RequireNonNegative(invalidCount, nameof(invalidCount));

        if (positiveCount + negativeCount + invalidCount != sampleCount)
        {
            throw new ArgumentException(
                "PositiveCount + NegativeCount + InvalidCount must equal SampleCount.",
                nameof(positiveCount));
        }

        Status = status;
        SampleCount = sampleCount;
        PositiveCount = positiveCount;
        NegativeCount = negativeCount;
        InvalidCount = invalidCount;
    }
}
