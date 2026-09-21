namespace FluoAxis.Import;

/// <summary>
/// Upper bounds applied to every import operation. Imported laboratory
/// files are untrusted input (CLAUDE.md §28, Data-Contract §3): size,
/// row, cell, and column counts are all capped, and exceeding any cap
/// returns a structured blocking error instead of exhausting memory.
/// </summary>
public sealed record ImportLimits
{
    public static ImportLimits Default { get; } = new();

    /// <summary>Maximum accepted input file size in bytes (default 200 MB).</summary>
    public long MaxBytes { get; }

    /// <summary>Maximum number of physical rows parsed across all sheets (default 200 000).</summary>
    public long MaxRows { get; }

    /// <summary>Maximum number of cells parsed across all sheets (default 5 000 000).</summary>
    public long MaxCells { get; }

    /// <summary>Maximum number of columns in any single row (default 512).</summary>
    public int MaxColumns { get; }

    /// <summary>Maximum number of shared-string table entries loaded per workbook (default 100 000).</summary>
    public int MaxSharedStringEntries { get; }

    /// <summary>Number of leading rows scanned for region discovery during inspection (default 50).</summary>
    public int HeaderScanWindowRows { get; }

    /// <summary>Maximum number of metadata text lines surfaced in a preview (default 20).</summary>
    public int MaxPreviewMetadataLines { get; }

    public ImportLimits(
        long maxBytes = 200 * 1024 * 1024,
        long maxRows = 200_000,
        long maxCells = 5_000_000,
        int maxColumns = 512,
        int maxSharedStringEntries = 100_000,
        int headerScanWindowRows = 50,
        int maxPreviewMetadataLines = 20)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "MaxBytes must be positive.");
        }

        if (maxRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows), maxRows, "MaxRows must be positive.");
        }

        if (maxCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCells), maxCells, "MaxCells must be positive.");
        }

        if (maxColumns <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxColumns), maxColumns, "MaxColumns must be at least 2.");
        }

        if (maxSharedStringEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSharedStringEntries), maxSharedStringEntries, "MaxSharedStringEntries must be positive.");
        }

        if (headerScanWindowRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(headerScanWindowRows), headerScanWindowRows, "HeaderScanWindowRows must be positive.");
        }

        if (maxPreviewMetadataLines < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPreviewMetadataLines), maxPreviewMetadataLines, "MaxPreviewMetadataLines must not be negative.");
        }

        MaxBytes = maxBytes;
        MaxRows = maxRows;
        MaxCells = maxCells;
        MaxColumns = maxColumns;
        MaxSharedStringEntries = maxSharedStringEntries;
        HeaderScanWindowRows = headerScanWindowRows;
        MaxPreviewMetadataLines = maxPreviewMetadataLines;
    }
}
