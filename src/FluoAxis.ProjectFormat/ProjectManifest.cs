namespace FluoAxis.ProjectFormat;

using FluoAxis.Domain;

/// <summary>
/// Root manifest of a .fluoaxisproj package (CLAUDE.md §21). Every reference
/// inside is relative to the package root; no machine-specific location ever
/// appears here (P0 invariant). Serialized camelCase via
/// <see cref="ProjectFormatJsonContext"/>.
/// </summary>
public sealed record ProjectManifest
{
    /// <summary>Identifier of the package format itself.</summary>
    public const string FormatIdentifier = "fluoaxisproj";

    /// <summary>Current manifest schema version this build reads and writes.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// App version recorded into new manifests. Placeholder until build-time
    /// injection exists. TODO: inject from a single version source during
    /// release packaging; callers may override via the manifest factory.
    /// </summary>
    public const string CurrentAppVersion = "0.1.0-dev";

    /// <summary>Only hash algorithm this layer ever writes or accepts.</summary>
    public const string Sha256Algorithm = "SHA-256";

    public required string Format { get; init; }

    public required int FormatVersion { get; init; }

    public required Guid ProjectId { get; init; }

    public required string DisplayName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset ModifiedAtUtc { get; init; }

    public required string AppVersion { get; init; }

    /// <summary>Method identifiers and versions used by this project.</summary>
    public required IReadOnlyList<string> MethodVersions { get; init; }

    /// <summary>Canonical method-snapshot hashes (BrafMethodSnapshot.ComputeCanonicalHash).</summary>
    public required IReadOnlyList<string> MethodSnapshotHashes { get; init; }

    public required string HashAlgorithm { get; init; }

    public required IReadOnlyList<ManifestEntry> Entries { get; init; }

    /// <summary>
    /// Creates a manifest with current format/app versions. Callers own the
    /// metadata; the entry hash/size list is built by the save pipeline.
    /// </summary>
    public static ProjectManifest Create(
        Guid projectId,
        string displayName,
        DateTimeOffset createdAtUtc,
        DateTimeOffset modifiedAtUtc,
        IReadOnlyList<string> methodVersions,
        IReadOnlyList<string> methodSnapshotHashes,
        IReadOnlyList<ManifestEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(methodVersions);
        ArgumentNullException.ThrowIfNull(methodSnapshotHashes);
        ArgumentNullException.ThrowIfNull(entries);

        return new ProjectManifest
        {
            Format = FormatIdentifier,
            FormatVersion = CurrentFormatVersion,
            ProjectId = projectId,
            DisplayName = displayName,
            CreatedAtUtc = createdAtUtc.ToUniversalTime(),
            ModifiedAtUtc = modifiedAtUtc.ToUniversalTime(),
            AppVersion = CurrentAppVersion,
            MethodVersions = methodVersions,
            MethodSnapshotHashes = methodSnapshotHashes,
            HashAlgorithm = Sha256Algorithm,
            Entries = entries,
        };
    }

    /// <summary>
    /// Validates a deserialized manifest structurally. Unknown fields are
    /// intentionally tolerated (they surface as warnings at the caller, not
    /// failures); a future formatVersion fails closed instead.
    /// </summary>
    public List<AppError> Validate()
    {
        List<AppError> errors = [];

        if (!string.Equals(Format, FormatIdentifier, StringComparison.Ordinal))
        {
            errors.Add(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                "The file does not declare the fluoaxisproj package format.",
                "format"));
        }

        if (FormatVersion > CurrentFormatVersion)
        {
            errors.Add(new AppError(
                ProjectErrorCodes.UnsupportedFormatVersion,
                ErrorSeverity.Blocking,
                $"The package uses format version {FormatVersion}, which this application does not support.",
                "formatVersion"));
        }

        if (!string.Equals(HashAlgorithm, Sha256Algorithm, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                $"The package uses unsupported hash algorithm '{HashAlgorithm}'.",
                "hashAlgorithm"));
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            errors.Add(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                "The package manifest has no display name.",
                "displayName"));
        }

        if (MethodSnapshotHashes.Count != MethodVersions.Count)
        {
            errors.Add(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                "The manifest method versions and snapshot hashes do not pair up.",
                "methodMismatch"));
        }

        HashSet<string> ordinal = new(StringComparer.Ordinal);
        HashSet<string> ignoreCase = new(StringComparer.OrdinalIgnoreCase);
        foreach (ManifestEntry entry in Entries)
        {
            if (!ordinal.Add(entry.RelativePath.Value))
            {
                errors.Add(new AppError(
                    ProjectErrorCodes.DuplicateEntry,
                    ErrorSeverity.Blocking,
                    "The manifest declares the same entry path twice.",
                    entry.RelativePath.Value));
            }

            if (!ignoreCase.Add(entry.RelativePath.Value))
            {
                errors.Add(new AppError(
                    ProjectErrorCodes.CaseCollision,
                    ErrorSeverity.Blocking,
                    "The manifest declares entries that collide case-insensitively.",
                    entry.RelativePath.Value));
            }

            if (entry.SizeBytes < 0)
            {
                errors.Add(new AppError(
                    ProjectErrorCodes.ManifestInvalid,
                    ErrorSeverity.Blocking,
                    "A manifest entry declares a negative size.",
                    entry.RelativePath.Value));
            }
        }

        return errors;
    }
}
