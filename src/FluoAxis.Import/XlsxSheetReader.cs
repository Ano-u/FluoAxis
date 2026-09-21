namespace FluoAxis.Import;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluoAxis.Domain;
using System.Text;

/// <summary>
/// Streaming XLSX sheet reader built on <see cref="OpenXmlReader"/> in SAX
/// mode: no whole-workbook DOM is ever materialized. The shared-string
/// table is loaded in one bounded pass before sheet rows are streamed, so
/// shared-string cells resolve with an O(1) lookup. Formula cells yield
/// only their cached <c>&lt;v&gt;</c> text; formulas are never evaluated.
/// </summary>
internal static class XlsxSheetReader
{
    /// <summary>Loads the shared-string table, capped at <paramref name="maxEntries"/>.</summary>
    /// <returns>The strings in index order, or null when the workbook has no shared-string part.</returns>
    public static string[]? LoadSharedStrings(SpreadsheetDocument document, int maxEntries)
    {
        SharedStringTablePart? part = document.WorkbookPart?.SharedStringTablePart;
        if (part is null)
        {
            return null;
        }

        var strings = new List<string>(capacity: 1024);
        using OpenXmlReader reader = OpenXmlReader.Create(part);
        // A shared-string item is either <si><t>text</t></si> or a sequence of
        // rich-text runs <si><r><t>a</t></r><r><t>b</t></r></si>; all <t> texts
        // of one <si> are concatenated.
        StringBuilder? current = null;
        while (reader.Read())
        {
            if (reader.IsStartElement && reader.ElementType == typeof(SharedStringItem))
            {
                current = new StringBuilder();
            }
            else if (reader.IsStartElement && reader.ElementType == typeof(Text) && current is not null)
            {
                current.Append(reader.GetText());
            }
            else if (reader.IsEndElement && reader.ElementType == typeof(SharedStringItem) && current is not null)
            {
                strings.Add(current.ToString());
                current = null;
                if (strings.Count > maxEntries)
                {
                    throw new ImportLimitException(
                        ImportErrorCodes.ImportSharedStringOverflow,
                        $"Shared-string table exceeds {maxEntries} entries.");
                }
            }
        }

        string[] result = new string[strings.Count];
        for (int i = 0; i < strings.Count; i++)
        {
            result[i] = strings[i];
        }

        return result;
    }

    /// <summary>Sheet name to worksheet part mapping in workbook order.</summary>
    public static List<(string Name, WorksheetPart Part)> GetSheets(SpreadsheetDocument document)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new ImportLimitException(ImportErrorCodes.ImportWorkbookCorrupt, "workbookPartMissing");
        Sheets? manifest = workbookPart.Workbook?.Sheets
            ?? throw new ImportLimitException(ImportErrorCodes.ImportWorkbookCorrupt, "sheetsMissing");

        var sheets = new List<(string Name, WorksheetPart Part)>();
        // The workbook manifest (sheet names and relationship ids) is a tiny
        // part; loading its sheet list is not a whole-workbook DOM expansion.
        foreach (Sheet sheet in manifest.Elements<Sheet>())
        {
            string? name = sheet.Name?.Value;
            string? id = sheet.Id?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (workbookPart.TryGetPartById(id, out OpenXmlPart? resolved) && resolved is WorksheetPart worksheet)
            {
                sheets.Add((name, worksheet));
            }
        }

        return sheets;
    }

    /// <summary>
    /// Streams rows of one worksheet as <see cref="RawRow"/> in physical
    /// document order, enforcing the shared row/cell/column budgets.
    /// </summary>
    public static IEnumerable<Result<RawRow>> StreamRows(
        WorksheetPart part,
        string[]? sharedStrings,
        ImportLimits limits,
        ParseBudget budget,
        CancellationToken cancellationToken)
    {
        using OpenXmlReader reader = OpenXmlReader.Create(part);
        List<RawCell>? cells = null;
        int? rowIndex = null;
        int nextColumnWhenMissing = 0;
        PendingCell pending = default;
        bool hasPendingCell = false;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.IsStartElement)
            {
                if (reader.ElementType == typeof(Row))
                {
                    rowIndex = ReadRowIndex(reader) ?? (rowIndex is null ? 0 : rowIndex.Value + 1);
                    cells = [];
                    nextColumnWhenMissing = 0;
                }
                else if (reader.ElementType == typeof(Cell))
                {
                    pending = ReadCellAttributes(reader, ref nextColumnWhenMissing);
                    hasPendingCell = true;
                }
                else if (reader.ElementType == typeof(CellValue) && hasPendingCell)
                {
                    pending = pending with { ValueText = pending.ValueText + reader.GetText() };
                }
                else if (reader.ElementType == typeof(Text) && hasPendingCell && pending.CellType == "inlineStr")
                {
                    pending = pending with { ValueText = pending.ValueText + reader.GetText() };
                }
            }
            else if (reader.IsEndElement)
            {
                if (reader.ElementType == typeof(Cell) && hasPendingCell)
                {
                    AddCell(pending, sharedStrings, limits, budget, cells!);
                    hasPendingCell = false;
                    pending = default;
                }
                else if (reader.ElementType == typeof(Row))
                {
                    if (cells is not null && rowIndex is not null)
                    {
                        budget.CountRow();
                        yield return Result<RawRow>.Ok(new RawRow(rowIndex.Value, cells));
                        cells = null;
                        rowIndex = null;
                    }
                }
            }
        }
    }

    private readonly record struct PendingCell(int? ColumnReferenceIndex, string? CellType, string ValueText);

    private readonly record struct CellRef(int Column, int Row);

    private static int? ReadRowIndex(OpenXmlReader reader)
    {
        foreach (OpenXmlAttribute attribute in reader.Attributes)
        {
            if (attribute.LocalName == "r" && attribute.NamespaceUri.Length == 0 && int.TryParse(attribute.Value, out int row))
            {
                return row - 1;
            }
        }

        return null;
    }

    private static PendingCell ReadCellAttributes(OpenXmlReader reader, ref int nextColumnWhenMissing)
    {
        string? reference = null;
        string? type = null;
        foreach (OpenXmlAttribute attribute in reader.Attributes)
        {
            if (attribute.NamespaceUri.Length != 0)
            {
                continue;
            }

            if (attribute.LocalName == "r")
            {
                reference = attribute.Value;
            }
            else if (attribute.LocalName == "t")
            {
                type = attribute.Value;
            }
        }

        int? column = null;
        if (reference is not null && TryParseCellReference(reference, out CellRef parsed))
        {
            column = parsed.Column;
        }

        // CellType keeps the raw ST_CellType token ("n", "s", "str", ...) so
        // the AddCell dispatch stays a plain string comparison.

        int fallback = nextColumnWhenMissing;
        if (column is int explicitColumn)
        {
            nextColumnWhenMissing = explicitColumn + 1;
        }
        else
        {
            nextColumnWhenMissing++;
            column = fallback;
        }

        return new PendingCell(column, type, string.Empty);
    }

    private static void AddCell(PendingCell pending, string[]? sharedStrings, ImportLimits limits, ParseBudget budget, List<RawCell> cells)
    {
        int column = pending.ColumnReferenceIndex ?? 0;
        if (column >= limits.MaxColumns)
        {
            throw new ImportLimitException(
                ImportErrorCodes.ImportTooManyColumns,
                $"Column index {column} exceeds the limit of {limits.MaxColumns}.");
        }

        budget.CellsParsed++;
        if (budget.CellsParsed > budget.MaxCells)
        {
            throw new ImportLimitException(
                ImportErrorCodes.ImportTooManyCells,
                $"Cell budget of {budget.MaxCells} cells exceeded.");
        }

        switch (pending.CellType)
        {
            case "s":
                string? shared = null;
                if (sharedStrings is not null
                    && int.TryParse(pending.ValueText, System.Globalization.CultureInfo.InvariantCulture, out int index)
                    && index >= 0
                    && index < sharedStrings.Length)
                {
                    shared = sharedStrings[index];
                }

                cells.Add(new RawCell(column, shared, null));
                break;

            case null:
            case "n":
                if (RawCell.TryParseFinite(pending.ValueText, out double number))
                {
                    cells.Add(new RawCell(column, null, number));
                }
                else
                {
                    cells.Add(new RawCell(column, pending.ValueText, null));
                }

                break;

            case "b":
                cells.Add(new RawCell(column, pending.ValueText == "1" ? "TRUE" : "FALSE", null));
                break;

            default:
                // "str", "inlineStr", "e", "d": text cells; a cached formula
                // result ("str") arrives as the <v> text and lands here.
                cells.Add(new RawCell(column, pending.ValueText, null));
                break;
        }
    }

    private static bool TryParseCellReference(string reference, out CellRef cellRef)
    {
        cellRef = default;
        int i = 0;
        int column = 0;
        while (i < reference.Length && char.IsAsciiLetter(reference[i]))
        {
            column = (column * 26) + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
            i++;
        }

        if (i == 0 || i >= reference.Length)
        {
            return false;
        }

        if (!int.TryParse(reference[i..], out int row))
        {
            return false;
        }

        cellRef = new CellRef(column - 1, row - 1);
        return true;
    }

    /// <summary>Row and cell budget shared by all sheets streamed in one import operation.</summary>
    public sealed class ParseBudget(long maxRows, long maxCells)
    {
        public long MaxRows { get; } = maxRows;

        public long MaxCells { get; } = maxCells;

        public long RowsConsumed { get; set; }

        public long CellsParsed { get; set; }

        public void CountRow()
        {
            RowsConsumed++;
            if (RowsConsumed > MaxRows)
            {
                throw new ImportLimitException(
                    ImportErrorCodes.ImportTooManyRows,
                    $"Row budget of {MaxRows} rows exceeded.");
            }
        }
    }
}
