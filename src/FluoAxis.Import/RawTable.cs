namespace FluoAxis.Import;

using System.Globalization;

/// <summary>Format-neutral cell produced by every adapter (XLSX SAX, CSV/TSV/TXT).</summary>
internal readonly record struct RawCell(int ColumnIndex, string? Text, double? Number, bool AllowTextNumericParsing = true)
{
    /// <summary>
    /// Numeric view of the cell: the stored number, or a parse of the text
    /// in InvariantCulture. Non-finite results are rejected (untrusted
    /// input never yields NaN or Infinity downstream).
    /// </summary>
    public bool TryGetNumber(out double value)
    {
        if (Number is double n)
        {
            value = n;
            return double.IsFinite(n);
        }

        if (AllowTextNumericParsing && Text is not null && TryParseFinite(Text, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    internal static bool TryParseFinite(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

/// <summary>Format-neutral row: 0-based row index plus its cells in column order.</summary>
internal sealed record RawRow(int RowIndex, IReadOnlyList<RawCell> Cells)
{
    public int MaxColumnIndex { get; } = Cells.Count == 0 ? -1 : Cells[^1].ColumnIndex;
}
