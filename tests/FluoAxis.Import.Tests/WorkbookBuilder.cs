namespace FluoAxis.Import.Tests;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Code-generated XLSX fixtures: writes real Open XML packages into
/// MemoryStream so the streaming reader consumes genuine workbook XML
/// (shared strings, formulas, inline strings, multi-sheet, metadata
/// blocks). No binary fixture files and no disk locations.
/// </summary>
public static class WorkbookBuilder
{
    /// <summary>One sheet of the fixture: name plus rows of cell values.</summary>
    public sealed record SheetSpec(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows);

    /// <summary>
    /// Builds a workbook. String cells become shared-string references;
    /// doubles are written as numeric cells; <c>null</c> skips the cell
    /// entirely (sparse row); <see cref="FormulaCell"/> writes a cached
    /// formula value; <see cref="InlineCell"/> writes an inline string.
    /// </summary>
    public static byte[] Build(params SheetSpec[] sheets)
    {
        using var buffer = new MemoryStream();
        using (SpreadsheetDocument document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbookPart = document.AddWorkbookPart();
            var sheetsElement = new Sheets();
            workbookPart.Workbook = new Workbook(sheetsElement);
            SharedStringTablePart sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sharedTable = new SharedStringTable();
            sharedPart.SharedStringTable = sharedTable;
            var sharedIndex = new Dictionary<string, int>();

            int sheetNumber = 1;
            foreach (SheetSpec spec in sheets)
            {
                WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
                var worksheet = new Worksheet();
                var rows = new List<Row>();

                for (int rowIndex = 0; rowIndex < spec.Rows.Count; rowIndex++)
                {
                    var cells = new List<Cell>();
                    for (int columnIndex = 0; columnIndex < spec.Rows[rowIndex].Count; columnIndex++)
                    {
                        Cell? cell = CreateCell(spec.Rows[rowIndex][columnIndex], columnIndex, rowIndex, sharedTable, sharedIndex);
                        if (cell is not null)
                        {
                            cells.Add(cell);
                        }
                    }

                    rows.Add(new Row(cells) { RowIndex = (uint)(rowIndex + 1) });
                }

                worksheet.Append(new SheetData(rows));
                part.Worksheet = worksheet;

                var sheet = new Sheet
                {
                    Name = spec.Name,
                    SheetId = (uint)sheetNumber,
                    Id = workbookPart.GetIdOfPart(part),
                };
                sheetsElement.Append(sheet);
                sheetNumber++;
            }

            sharedTable.Save();
            workbookPart.Workbook.Save();
        }

        return buffer.ToArray();
    }

    /// <summary>A formula cell with only a cached value (no evaluation).</summary>
    public static FormulaCell Formula(string cachedValue) => new(cachedValue);

    /// <summary>An inline-string cell (t="inlineStr").</summary>
    public static InlineCell Inline(string text) => new(text);

    /// <summary>A text cell written into the shared-string table.</summary>
    public static SharedCell Shared(string text) => new(text);

    /// <summary>Numeric cell written as a raw number.</summary>
    public static NumberCell Number(double value) => new(value);

    private static Cell? CreateCell(
        object? value,
        int columnIndex,
        int rowIndex,
        SharedStringTable sharedTable,
        Dictionary<string, int> sharedIndex)
    {
        if (value is null)
        {
            return null;
        }

        string reference = $"{ColumnLetter(columnIndex)}{rowIndex + 1}";
        switch (value)
        {
            case double d:
                return new Cell(new CellValue(d.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    CellReference = reference,
                    DataType = CellValues.Number,
                };

            case int or long:
                return new Cell(new CellValue(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    CellReference = reference,
                    DataType = CellValues.Number,
                };

            case string s:
                if (!sharedIndex.TryGetValue(s, out int index))
                {
                    index = sharedTable.Count();
                    sharedTable.Append(new SharedStringItem(new Text(s)));
                    sharedIndex[s] = index;
                }

                return new Cell(new CellValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    CellReference = reference,
                    DataType = CellValues.SharedString,
                };

            case FormulaCell formula:
                return new Cell(new CellValue(formula.CachedValue))
                {
                    CellReference = reference,
                    DataType = CellValues.String,
                };

            case InlineCell inline:
                return new Cell(new InlineString(new Text(inline.Text)))
                {
                    CellReference = reference,
                    DataType = CellValues.InlineString,
                };

            default:
                throw new NotSupportedException($"Fixture cell type {value.GetType().Name} is not supported.");
        }
    }

    private static string ColumnLetter(int columnIndex)
    {
        int n = columnIndex + 1;
        var letters = new System.Text.StringBuilder();
        while (n > 0)
        {
            n--;
            letters.Insert(0, (char)('A' + (n % 26)));
            n /= 26;
        }

        return letters.ToString();
    }

    public readonly record struct FormulaCell(string CachedValue);

    public readonly record struct InlineCell(string Text);

    public readonly record struct SharedCell(string Text);

    public readonly record struct NumberCell(double Value);
}
