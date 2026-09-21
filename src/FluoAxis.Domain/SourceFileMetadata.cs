namespace FluoAxis.Domain;

/// <summary>
/// Metadata of an imported source file once it has become a managed
/// project resource. Never carries the original machine location (P0
/// path-policy invariant); the persisted reference is the relative
/// resource path inside the project package.
/// </summary>
public sealed record SourceFileMetadata
{
    public string OriginalFileName { get; }
    public RelativePath RelativeResourcePath { get; }
    public HexHash Sha256 { get; }
    public long FileSizeBytes { get; }
    public DateTimeOffset ImportedAtUtc { get; }
    public string MediaType { get; }

    public SourceFileMetadata(
        string originalFileName,
        RelativePath relativeResourcePath,
        HexHash sha256,
        long fileSizeBytes,
        DateTimeOffset importedAtUtc,
        string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentNullException.ThrowIfNull(relativeResourcePath);
        ArgumentNullException.ThrowIfNull(mediaType);

        if (originalFileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("OriginalFileName must be a bare file name, not a path.", nameof(originalFileName));
        }

        if (fileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes), fileSizeBytes, "FileSizeBytes must be positive.");
        }

        OriginalFileName = originalFileName;
        RelativeResourcePath = relativeResourcePath;
        Sha256 = sha256;
        FileSizeBytes = fileSizeBytes;
        ImportedAtUtc = importedAtUtc.ToUniversalTime();
        MediaType = mediaType;
    }
}
