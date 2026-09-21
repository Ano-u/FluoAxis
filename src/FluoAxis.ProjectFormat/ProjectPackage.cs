namespace FluoAxis.ProjectFormat;

using FluoAxis.Domain;

/// <summary>
/// An opened, fully validated .fluoaxisproj package. Holds the parsed
/// manifest and the content bytes of every declared entry, so it stays
/// usable after the source stream is closed. Nothing here carries a machine
/// location (P0 invariant) — every reference is package-relative. Total
/// retained bytes are bounded by the <see cref="PackageLimits"/> used while
/// opening.
/// </summary>
public sealed class ProjectPackage
{
    private readonly Dictionary<string, byte[]> _entriesByPath;

    internal ProjectPackage(ProjectManifest manifest, IReadOnlyDictionary<string, byte[]> entries)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(entries);

        Manifest = manifest;
        // RelativePath equality is exact string comparison on the canonical
        // '/'-separated form, so an ordinal string dictionary matches it.
        _entriesByPath = new Dictionary<string, byte[]>(entries, StringComparer.Ordinal);
    }

    /// <summary>Validated manifest of the package.</summary>
    public ProjectManifest Manifest { get; }

    /// <summary>All declared content entries, in manifest order.</summary>
    public IReadOnlyList<ManifestEntry> DeclaredEntries => Manifest.Entries;

    /// <summary>Declared entries of one <see cref="ProjectEntryType"/>.</summary>
    public IReadOnlyList<ManifestEntry> EntriesOfType(ProjectEntryType type)
        => [.. Manifest.Entries.Where(e => e.EntryType == type)];

    /// <summary>Whether an entry with this exact relative path exists.</summary>
    public bool HasEntry(RelativePath path) => _entriesByPath.ContainsKey(path.Value);

    /// <summary>
    /// Reads the full content of one entry. Returns a fresh copy, so callers
    /// cannot mutate the package contents.
    /// </summary>
    public byte[] ReadEntry(RelativePath path)
    {
        if (!_entriesByPath.TryGetValue(path.Value, out byte[]? content))
        {
            throw new KeyNotFoundException($"The package does not contain entry '{path.Value}'.");
        }

        return (byte[])content.Clone();
    }
}
