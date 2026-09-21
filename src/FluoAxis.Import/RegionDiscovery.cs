namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// Heuristic region discovery shared by all adapters. Scans the leading
/// rows of a raw table for the header row (columns labeled with well
/// identifiers A1..H12), the time column (numeric and strictly ascending
/// below the header), and leading non-table metadata lines. Sheet-name
/// channel keywords are evaluated separately because XLSX sheets carry
/// names while CSV bodies do not.
/// </summary>
internal static class RegionDiscovery
{
    /// <summary>Minimum well-labeled columns for a row to qualify as the header.</summary>
    /// <remarks>
    /// Well identifiers (A1..H12) are distinctive enough that two labeled
    /// columns already identify a header row; small fixture tables and real
    /// 96-well exports both satisfy this.
    /// </remarks>
    private const int MinHeaderWells = 2;

    private const int MaxMetadataLength = 200;

    /// <summary>
    /// Finds the best header row inside the scan window. A candidate row
    /// must contain at least <see cref="MinHeaderWells"/> cells whose text
    /// parses as a well identifier; the earliest candidate with the most
    /// well columns wins. Returns null when no row qualifies.
    /// </summary>
    public static int? FindHeaderRow(IReadOnlyList<RawRow> window)
    {
        ArgumentNullException.ThrowIfNull(window);

        int? bestRow = null;
        int bestWells = 0;

        foreach (RawRow row in window)
        {
            int wells = CountWellColumns(row);
            if (wells >= MinHeaderWells && wells > bestWells)
            {
                bestWells = wells;
                bestRow = row.RowIndex;
            }
        }

        return bestRow;
    }

    /// <summary>
    /// Maps column index to well identifier for every well-labeled cell of
    /// the given header row. Cells that do not parse as wells are ignored.
    /// </summary>
    public static Dictionary<int, WellId> MapWellColumns(RawRow headerRow)
    {
        ArgumentNullException.ThrowIfNull(headerRow);

        var map = new Dictionary<int, WellId>();
        foreach (RawCell cell in headerRow.Cells)
        {
            if (cell.Text is not null && TryParseWell(cell.Text, out WellId well))
            {
                map[cell.ColumnIndex] = well;
            }
        }

        return map;
    }

    /// <summary>
    /// Finds the time column among the data rows below the header: the
    /// lowest column index whose cells are numeric in every scanned row
    /// and strictly ascending across the rows. Returns null when fewer
    /// than two data rows were scanned or no column qualifies.
    /// </summary>
    public static int? FindTimeColumn(IReadOnlyList<RawRow> dataRows)
    {
        ArgumentNullException.ThrowIfNull(dataRows);

        if (dataRows.Count < 2)
        {
            return null;
        }

        // Columns numeric in every row.
        int[]? numericInAll = null;
        foreach (RawRow row in dataRows)
        {
            List<int>? numeric = null;
            foreach (RawCell cell in row.Cells)
            {
                if (cell.TryGetNumber(out _))
                {
                    (numeric ??= []).Add(cell.ColumnIndex);
                }
            }

            if (numeric is null)
            {
                return null;
            }

            numericInAll = numericInAll is null
                ? [.. numeric]
                : IntersectSorted(numericInAll, numeric);
            if (numericInAll.Length == 0)
            {
                return null;
            }
        }

        foreach (int column in numericInAll!)
        {
            if (IsStrictlyAscending(dataRows, column))
            {
                return column;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts leading metadata lines: rows above the header whose cells
    /// are all text (non-numeric), joined and capped. Rows containing any
    /// numeric cell are treated as data, not metadata.
    /// </summary>
    public static List<string> ExtractMetadataLines(IReadOnlyList<RawRow> window, int? headerRow, int maxLines)
    {
        var lines = new List<string>();
        if (headerRow is null || maxLines <= 0)
        {
            return lines;
        }

        foreach (RawRow row in window)
        {
            if (row.RowIndex >= headerRow)
            {
                break;
            }

            bool anyNumeric = false;
            foreach (RawCell cell in row.Cells)
            {
                if (cell.TryGetNumber(out _))
                {
                    anyNumeric = true;
                    break;
                }
            }

            if (anyNumeric)
            {
                continue;
            }

            string text = JoinCells(row);
            if (text.Length > 0)
            {
                lines.Add(text.Length <= MaxMetadataLength ? text : text[..MaxMetadataLength]);
                if (lines.Count >= maxLines)
                {
                    break;
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// Suggests a channel from a sheet name: contains "FAM" (case-insensitive)
    /// suggests Fam, contains "ROX" suggests Rox, anything else is null.
    /// </summary>
    public static FluorescenceChannel? SuggestChannel(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        if (sheetName.Contains("FAM", StringComparison.OrdinalIgnoreCase))
        {
            return FluorescenceChannel.Fam;
        }

        if (sheetName.Contains("ROX", StringComparison.OrdinalIgnoreCase))
        {
            return FluorescenceChannel.Rox;
        }

        return null;
    }

    internal static bool TryParseWell(string text, out WellId well)
    {
        try
        {
            well = WellId.Parse(text);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or ArgumentOutOfRangeException)
        {
            well = default;
            return false;
        }
    }

    private static int[] IntersectSorted(int[] left, List<int> right)
    {
        var kept = new List<int>(left.Length);
        int i = 0;
        int j = 0;
        while (i < left.Length && j < right.Count)
        {
            if (left[i] == right[j])
            {
                kept.Add(left[i]);
                i++;
                j++;
            }
            else if (left[i] < right[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return [.. kept];
    }

    private static bool IsStrictlyAscending(IReadOnlyList<RawRow> dataRows, int columnIndex)
    {
        double previous = double.MinValue;
        bool first = true;
        foreach (RawRow row in dataRows)
        {
            if (!TryFindNumber(row, columnIndex, out double value))
            {
                return false;
            }

            if (!first && value <= previous)
            {
                return false;
            }

            previous = value;
            first = false;
        }

        return true;
    }

    private static bool TryFindNumber(RawRow row, int columnIndex, out double value)
    {
        foreach (RawCell cell in row.Cells)
        {
            if (cell.ColumnIndex == columnIndex)
            {
                return cell.TryGetNumber(out value);
            }
        }

        value = default;
        return false;
    }

    private static int CountWellColumns(RawRow row)
    {
        int count = 0;
        foreach (RawCell cell in row.Cells)
        {
            if (cell.Text is not null && TryParseWell(cell.Text, out _))
            {
                count++;
            }
        }

        return count;
    }

    private static string JoinCells(RawRow row)
    {
        var parts = new List<string>(row.Cells.Count);
        foreach (RawCell cell in row.Cells)
        {
            if (!string.IsNullOrWhiteSpace(cell.Text))
            {
                parts.Add(cell.Text.Trim());
            }
        }

        return string.Join(" | ", parts);
    }
}
