namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// One candidate tabular region discovered inside an imported file: a
/// sheet (XLSX) or the single body (CSV/TSV/TXT), its detected header
/// row, time column, well columns, and a channel suggestion derived from
/// the sheet name. Everything here is advisory only — the user-confirmed
/// <see cref="DataMapping"/> is authoritative for <c>ReadAsync</c>.
/// </summary>
public sealed record RegionCandidate
{
    /// <summary>Sheet name for XLSX; file body label for delimited text.</summary>
    public string SheetName { get; }

    /// <summary>Detected 0-based row index of the header row, or null when none was found.</summary>
    public int? HeaderRowIndex { get; }

    /// <summary>Detected 0-based column index holding elapsed time, or null.</summary>
    public int? TimeColumnIndex { get; }

    /// <summary>0-based column index to well identifier; wells with column outside the map are ignored.</summary>
    public IReadOnlyDictionary<int, WellId> WellColumnMap { get; }

    /// <summary>Suggested channel inferred from the sheet name (FAM/ROX keywords); null when ambiguous.</summary>
    public FluorescenceChannel? ChannelSuggestion { get; }

    /// <summary>Estimated data row count below the header (rows scanned for discovery are capped).</summary>
    public long EstimatedDataRows { get; }

    public RegionCandidate(
        string sheetName,
        int? headerRowIndex,
        int? timeColumnIndex,
        IReadOnlyDictionary<int, WellId> wellColumnMap,
        FluorescenceChannel? channelSuggestion,
        long estimatedDataRows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ArgumentNullException.ThrowIfNull(wellColumnMap);

        SheetName = sheetName;
        HeaderRowIndex = headerRowIndex;
        TimeColumnIndex = timeColumnIndex;
        WellColumnMap = wellColumnMap;
        ChannelSuggestion = channelSuggestion;
        EstimatedDataRows = estimatedDataRows;
    }
}
