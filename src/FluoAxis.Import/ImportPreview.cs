namespace FluoAxis.Import;

/// <summary>
/// File-level inspection summary produced by <c>InspectAsync</c>: detected
/// format, encoding / delimiter conclusions, region candidates, and
/// leading metadata lines (instrument header text above the table).
/// Nothing in a preview is authoritative for reading; it feeds the
/// user-confirmation step of the import workflow.
/// </summary>
public sealed record ImportPreview
{
    public ImportFileFormat Format { get; }

    /// <summary>Candidate regions discovered in the file (sheets for XLSX, single body for text).</summary>
    public IReadOnlyList<RegionCandidate> Regions { get; }

    /// <summary>Total physical row estimate across the file, capped by the scan budget.</summary>
    public long EstimatedTotalRows { get; }

    /// <summary>Total physical column estimate (max row width seen).</summary>
    public int EstimatedMaxColumns { get; }

    /// <summary>Leading non-table text lines above the discovered header (instrument metadata), capped.</summary>
    public IReadOnlyList<string> MetadataLines { get; }

    /// <summary>Detected text encoding (delimited text only); null for XLSX.</summary>
    public string? DetectedEncoding { get; }

    /// <summary>True when the encoding was assumed (no BOM) rather than positively detected.</summary>
    public bool EncodingAssumed { get; }

    /// <summary>Detected delimiter (delimited text only); null for XLSX.</summary>
    public char? DetectedDelimiter { get; }

    public ImportPreview(
        ImportFileFormat format,
        IReadOnlyList<RegionCandidate> regions,
        long estimatedTotalRows,
        int estimatedMaxColumns,
        IReadOnlyList<string> metadataLines,
        string? detectedEncoding = null,
        bool encodingAssumed = false,
        char? detectedDelimiter = null)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(metadataLines);

        Format = format;
        Regions = regions;
        EstimatedTotalRows = estimatedTotalRows;
        EstimatedMaxColumns = estimatedMaxColumns;
        MetadataLines = metadataLines;
        DetectedEncoding = detectedEncoding;
        EncodingAssumed = encodingAssumed;
        DetectedDelimiter = detectedDelimiter;
    }
}
