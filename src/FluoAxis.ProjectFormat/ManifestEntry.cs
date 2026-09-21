namespace FluoAxis.ProjectFormat;

using FluoAxis.Domain;

/// <summary>
/// One entry of the project manifest: a declared resource inside the package.
/// The path is always a validated <see cref="RelativePath"/> (P0 invariant);
/// the hash is a lowercase 64-char SHA-256 (never a machine location).
/// </summary>
public sealed record ManifestEntry
{
    /// <summary>Persisted property name for the relative path.</summary>
    public const string ManifestPath = "manifest.json";

    public required RelativePath RelativePath { get; init; }

    public required ProjectEntryType EntryType { get; init; }

    public required HexHash Sha256 { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>
    /// JSON-friendly form. <paramref name="relativePath"/> is parsed through
    /// <see cref="Domain.RelativePath.Parse"/> so a rooted or escaping value
    /// fails closed at deserialization time.
    /// </summary>
    public static ManifestEntry FromPersisted(string relativePath, ProjectEntryType entryType, string sha256, long sizeBytes)
        => new()
        {
            RelativePath = RelativePath.Parse(relativePath),
            EntryType = entryType,
            Sha256 = new HexHash(sha256),
            SizeBytes = sizeBytes,
        };

    /// <summary>Serializes to the camelCase persisted form.</summary>
    public (string Path, string Type, string Sha, long Size) ToPersisted()
        => (RelativePath.Value, EntryType.ToString(), Sha256.Value, SizeBytes);
}
