namespace FluoAxis.ProjectFormat;

/// <summary>
/// Defensible limits for opening a .fluoaxisproj archive (plan.md §7.4
/// bomb/traversal defenses). All limits are checked before content is ever
/// extracted; exceeding any of them is a structured rejection, never an
/// out-of-memory failure.
/// </summary>
public sealed record PackageLimits
{
    /// <summary>Shared defaults used when callers do not override limits.</summary>
    public static PackageLimits Default { get; } = new();

    /// <summary>Maximum summed uncompressed size of all entries (default 2 GB).</summary>
    public long MaxTotalUncompressedBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Maximum uncompressed size of a single entry (default 512 MB).</summary>
    public long MaxEntryUncompressedBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>
    /// Maximum compression ratio (uncompressed / compressed) of a single
    /// entry (default 200). Highly compressible entries are the classic
    /// zip-bomb shape even when their declared size is small.
    /// </summary>
    public int MaxCompressionRatio { get; init; } = 200;

    /// <summary>Maximum number of entries in one package (default 10,000).</summary>
    public int MaxEntryCount { get; init; } = 10_000;

    /// <summary>
    /// Maximum uncompressed size of manifest.json itself (default 16 MB).
    /// Manifests are pure metadata (path/hash/size rows), so this is far
    /// above any legitimate need and bounds parsing cost.
    /// </summary>
    public long MaxManifestUncompressedBytes { get; init; } = 16L * 1024 * 1024;
}
