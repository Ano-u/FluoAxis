namespace FluoAxis.Contracts;

using FluoAxis.Domain;

/// <summary>
/// Metadata of one imported source file as returned in the run snapshot
/// (plan.md §5.3). Mirrors <see cref="Domain.SourceFileMetadata"/>. The
/// resource path is validated as a package-relative path at construction:
/// rooted paths and traversal are rejected here, not persisted (P0
/// path-policy invariant).
/// </summary>
public sealed record SourceFileDto
{
    public string OriginalFileName { get; }
    public string RelativeResourcePath { get; }
    public string Sha256 { get; }
    public long FileSizeBytes { get; }
    public DateTimeOffset ImportedAtUtc { get; }

    public SourceFileDto(
        string originalFileName,
        string relativeResourcePath,
        string sha256,
        long fileSizeBytes,
        DateTimeOffset importedAtUtc)
    {
        ContractGuard.RequireNonBlank(originalFileName, nameof(originalFileName));
        ContractGuard.RequireNonBlank(relativeResourcePath, nameof(relativeResourcePath));
        ContractGuard.RequireSha256(sha256, nameof(sha256));
        ContractGuard.RequirePositive(fileSizeBytes, nameof(fileSizeBytes));
        ContractGuard.RequireUtc(importedAtUtc, nameof(importedAtUtc));

        if (originalFileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("OriginalFileName must be a bare file name, not a path.", nameof(originalFileName));
        }

        // Throws FormatException for rooted/traversal/URI paths.
        RelativePath resourcePath = RelativePath.Parse(relativeResourcePath);
        _ = ResolveMediaType(originalFileName, resourcePath.FileName);

        OriginalFileName = originalFileName;
        RelativeResourcePath = resourcePath.Value;
        Sha256 = sha256.ToLowerInvariant();
        FileSizeBytes = fileSizeBytes;
        ImportedAtUtc = importedAtUtc;
    }

    /// <summary>Converts to the domain value object.</summary>
    public SourceFileMetadata ToDomain() => new(
        OriginalFileName,
        RelativePath.Parse(RelativeResourcePath),
        new HexHash(Sha256),
        FileSizeBytes,
        ImportedAtUtc,
        ResolveMediaType(OriginalFileName, RelativePath.Parse(RelativeResourcePath).FileName));

    /// <summary>Converts from the domain value object.</summary>
    public static SourceFileDto FromDomain(SourceFileMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        string derivedMediaType = ResolveMediaType(
            metadata.OriginalFileName,
            metadata.RelativeResourcePath.FileName);
        if (!string.Equals(metadata.MediaType, derivedMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Source metadata media type must match its supported file extension.",
                nameof(metadata));
        }

        return new SourceFileDto(
            metadata.OriginalFileName,
            metadata.RelativeResourcePath.Value,
            metadata.Sha256.Value,
            metadata.FileSizeBytes,
            metadata.ImportedAtUtc);
    }

    private static string ResolveMediaType(string originalFileName, string resourceFileName)
    {
        string originalExtension = Path.GetExtension(originalFileName).ToLowerInvariant();
        string resourceExtension = Path.GetExtension(resourceFileName).ToLowerInvariant();
        if (!string.Equals(originalExtension, resourceExtension, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "OriginalFileName and RelativeResourcePath must identify the same file format.",
                nameof(originalFileName));
        }

        return originalExtension switch
        {
            ".xlsx" => MediaTypeNames.Xlsx,
            ".xls" => MediaTypeNames.Xls,
            ".csv" => MediaTypeNames.Csv,
            ".tsv" => MediaTypeNames.Tsv,
            ".txt" => MediaTypeNames.Txt,
            _ => throw new ArgumentException(
                "Source files must use a supported XLSX, XLS, CSV, TSV, or TXT extension.",
                nameof(originalFileName)),
        };
    }

    public static class MediaTypeNames
    {
        public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public const string Xls = "application/vnd.ms-excel";
        public const string Csv = "text/csv";
        public const string Tsv = "text/tab-separated-values";
        public const string Txt = "text/plain";
    }
}
