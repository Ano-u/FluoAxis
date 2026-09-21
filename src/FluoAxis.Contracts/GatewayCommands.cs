namespace FluoAxis.Contracts;

using System.Text.Json.Serialization;
using FluoAxis.Domain;

/// <summary>Creates a new project and its first run (plan.md §5.1).</summary>
public sealed record CreateProjectCommand
{
    public Guid RequestId { get; }
    public string ProjectName { get; }
    public string ActorId { get; }
    public DateTimeOffset IssuedUtc { get; }
    public int ContractVersion { get; }

    public CreateProjectCommand(
        Guid requestId,
        string projectName,
        string actorId,
        DateTimeOffset issuedUtc,
        int contractVersion = Contracts.ContractVersion.Current)
    {
        ContractGuard.RequireNonEmptyGuid(requestId, nameof(requestId));
        ContractGuard.RequireNonBlank(projectName, nameof(projectName));
        ContractGuard.RequireNonBlank(actorId, nameof(actorId));
        ContractGuard.RequireUtc(issuedUtc, nameof(issuedUtc));

        RequestId = requestId;
        ProjectName = projectName;
        ActorId = actorId;
        IssuedUtc = issuedUtc;
        ContractVersion = contractVersion;
    }

    /// <summary>Derived flag, never part of the wire payload.</summary>
    [JsonIgnore]
    public bool IsCurrentVersion => ContractVersion == Contracts.ContractVersion.Current;
}

/// <summary>
/// Read-only inspection of an external file before it becomes a project
/// resource (plan.md §5.1/§7.1). The resource path is the
/// project-package-relative destination the file would occupy; original
/// machine locations never enter the contract.
/// </summary>
public sealed record InspectImportCommand
{
    public Guid RequestId { get; }
    public Guid RunId { get; }
    public string OriginalFileName { get; }
    public long FileSizeBytes { get; }
    public string RelativeResourcePath { get; }
    public string? FormatHint { get; }
    public int ContractVersion { get; }

    public InspectImportCommand(
        Guid requestId,
        Guid runId,
        string originalFileName,
        long fileSizeBytes,
        string relativeResourcePath,
        string? formatHint = null,
        int contractVersion = Contracts.ContractVersion.Current)
    {
        ContractGuard.RequireNonEmptyGuid(requestId, nameof(requestId));
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId));
        ContractGuard.RequireNonBlank(originalFileName, nameof(originalFileName));
        ContractGuard.RequirePositive(fileSizeBytes, nameof(fileSizeBytes));
        ContractGuard.RequireNonBlank(relativeResourcePath, nameof(relativeResourcePath));

        if (originalFileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("OriginalFileName must be a bare file name, not a path.", nameof(originalFileName));
        }

        // Rejects rooted paths, traversal, and file:// URIs (P0 path invariant).
        RelativePath.Parse(relativeResourcePath);

        if (formatHint is not null && string.IsNullOrWhiteSpace(formatHint))
        {
            throw new ArgumentException("FormatHint, when present, must not be blank.", nameof(formatHint));
        }

        RequestId = requestId;
        RunId = runId;
        OriginalFileName = originalFileName;
        FileSizeBytes = fileSizeBytes;
        RelativeResourcePath = RelativePath.Parse(relativeResourcePath).Value;
        FormatHint = formatHint;
        ContractVersion = contractVersion;
    }

    /// <summary>Derived flag, never part of the wire payload.</summary>
    [JsonIgnore]
    public bool IsCurrentVersion => ContractVersion == Contracts.ContractVersion.Current;
}

/// <summary>
/// Confirms the proposed column/channel mapping after inspection and lands
/// the staged source into the Run (plan.md §5.1). The frozen Gateway has no
/// separate commit-import command, so this is the point where a managed
/// resource becomes the Run's immutable imported data: it carries both the
/// confirmed FAM/ROX regions and the identity of the already-staged resource.
/// </summary>
public sealed record ConfirmMappingCommand : CommandBase
{
    public ChannelRegionMappingDto Fam { get; }
    public ChannelRegionMappingDto Rox { get; }

    /// <summary>Bare file name of the staged source (never a machine path).</summary>
    public string OriginalFileName { get; }

    /// <summary>Managed, package-relative location of the staged source.</summary>
    public string RelativeResourcePath { get; }

    /// <summary>SHA-256 of the staged source observed when it was staged.</summary>
    public string SourceSha256 { get; }

    /// <summary>Size in bytes of the staged source observed when it was staged.</summary>
    public long SourceSizeBytes { get; }

    /// <summary>Media type of the staged source.</summary>
    public string SourceMediaType { get; }

    public ConfirmMappingCommand(
        Guid requestId,
        Guid runId,
        int expectedRunRevision,
        string actorId,
        DateTimeOffset issuedUtc,
        ChannelRegionMappingDto fam,
        ChannelRegionMappingDto rox,
        string originalFileName,
        string relativeResourcePath,
        string sourceSha256,
        long sourceSizeBytes,
        string sourceMediaType,
        int contractVersion = Contracts.ContractVersion.Current)
        : base(requestId, runId, expectedRunRevision, actorId, issuedUtc, contractVersion)
    {
        ArgumentNullException.ThrowIfNull(fam);
        ArgumentNullException.ThrowIfNull(rox);
        if (fam.Channel != ChannelNames.Fam)
        {
            throw new ArgumentException("The FAM mapping must identify the fam channel.", nameof(fam));
        }

        if (rox.Channel != ChannelNames.Rox)
        {
            throw new ArgumentException("The ROX mapping must identify the rox channel.", nameof(rox));
        }

        string[] famWells = [.. fam.WellColumns.Select(item => item.Well).Order(StringComparer.Ordinal)];
        string[] roxWells = [.. rox.WellColumns.Select(item => item.Well).Order(StringComparer.Ordinal)];
        if (!famWells.SequenceEqual(roxWells, StringComparer.Ordinal))
        {
            throw new ArgumentException("FAM and ROX mappings must cover the same logical wells.", nameof(rox));
        }

        ContractGuard.RequireNonBlank(originalFileName, nameof(originalFileName));
        if (originalFileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("OriginalFileName must be a bare file name, not a path.", nameof(originalFileName));
        }

        ContractGuard.RequireNonBlank(sourceMediaType, nameof(sourceMediaType));
        ContractGuard.RequireSha256(sourceSha256, nameof(sourceSha256));
        ContractGuard.RequirePositive(sourceSizeBytes, nameof(sourceSizeBytes));

        // Rejects rooted paths, traversal, and file:// URIs (P0 path invariant).
        RelativePath.Parse(relativeResourcePath);

        Fam = fam;
        Rox = rox;
        OriginalFileName = originalFileName;
        RelativeResourcePath = RelativePath.Parse(relativeResourcePath).Value;
        SourceSha256 = sourceSha256.ToLowerInvariant();
        SourceSizeBytes = sourceSizeBytes;
        SourceMediaType = sourceMediaType;
    }
}

/// <summary>Submits the full plate assignment for a run (plan.md §5.1).</summary>
public sealed record AssignWellsCommand : CommandBase
{
    public IReadOnlyList<WellAssignmentDto> Assignments { get; }

    public AssignWellsCommand(
        Guid requestId,
        Guid runId,
        int expectedRunRevision,
        string actorId,
        DateTimeOffset issuedUtc,
        IReadOnlyList<WellAssignmentDto> assignments,
        int contractVersion = Contracts.ContractVersion.Current)
        : base(requestId, runId, expectedRunRevision, actorId, issuedUtc, contractVersion)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        if (assignments.Count == 0)
        {
            throw new ArgumentException("Assignments must contain at least one well.", nameof(assignments));
        }

        foreach (WellAssignmentDto assignment in assignments)
        {
            ArgumentNullException.ThrowIfNull(assignment, nameof(assignments));
        }

        Assignments = assignments;
    }
}

/// <summary>Executes calibration (plateau Fm and NTC Fb) for a run (plan.md §5.1).</summary>
public sealed record RunCalibrationCommand : CommandBase
{
    public RunCalibrationCommand(
        Guid requestId,
        Guid runId,
        int expectedRunRevision,
        string actorId,
        DateTimeOffset issuedUtc,
        int contractVersion = Contracts.ContractVersion.Current)
        : base(requestId, runId, expectedRunRevision, actorId, issuedUtc, contractVersion)
    {
    }
}

/// <summary>Executes the WT/LQC control QC stage for a run (plan.md §5.1).</summary>
public sealed record RunControlQcCommand : CommandBase
{
    public RunControlQcCommand(
        Guid requestId,
        Guid runId,
        int expectedRunRevision,
        string actorId,
        DateTimeOffset issuedUtc,
        int contractVersion = Contracts.ContractVersion.Current)
        : base(requestId, runId, expectedRunRevision, actorId, issuedUtc, contractVersion)
    {
    }
}

/// <summary>Executes per-sample dr' classification for a run (plan.md §5.1).</summary>
public sealed record AnalyzeSamplesCommand : CommandBase
{
    public AnalyzeSamplesCommand(
        Guid requestId,
        Guid runId,
        int expectedRunRevision,
        string actorId,
        DateTimeOffset issuedUtc,
        int contractVersion = Contracts.ContractVersion.Current)
        : base(requestId, runId, expectedRunRevision, actorId, issuedUtc, contractVersion)
    {
    }
}
