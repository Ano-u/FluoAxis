namespace FluoAxis.ProjectFormat;

using FluoAxis.Domain;

/// <summary>
/// Inputs for <see cref="FluoAxisProjectArchive.SaveAsync"/>: the manifest
/// metadata plus the byte content of every entry. Hash and size of each
/// entry are computed by the save pipeline itself, so a caller cannot
/// declare a stale hash.
/// </summary>
public sealed record ProjectPackageContents
{
    public required Guid ProjectId { get; init; }

    public required string DisplayName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset ModifiedAtUtc { get; init; }

    public required IReadOnlyList<string> MethodVersions { get; init; }

    public required IReadOnlyList<string> MethodSnapshotHashes { get; init; }

    /// <summary>
    /// Entry content keyed by validated relative path, with its entry type.
    /// Paths must be unique after canonicalization (enforced at save).
    /// </summary>
    public required IReadOnlyList<ProjectPackageContentFile> Files { get; init; }
}

/// <summary>One content file staged for saving into a package.</summary>
public sealed record ProjectPackageContentFile
{
    public required RelativePath Path { get; init; }

    public required ProjectEntryType EntryType { get; init; }

    /// <summary>In-memory content; hashed and compressed by the save pipeline.</summary>
    public required byte[] Content { get; init; }
}
