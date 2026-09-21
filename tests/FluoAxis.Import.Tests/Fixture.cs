namespace FluoAxis.Import.Tests;

using FluoAxis.Domain;
using FluoAxis.Import;

/// <summary>
/// Shared fixture material: canonical kinetic tables and the mapping used
/// to read them. Times are minutes, wells are a small A-row subset (the
/// streaming engine is well-count agnostic).
/// </summary>
public static class Fixture
{
    public static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    /// <summary>Header + rows of a two-well kinetic table (wells A1, A2).</summary>
    public static IReadOnlyList<IReadOnlyList<object?>> KineticTable(double startMinutes, double stepMinutes, int rowCount) =>
    [
        ["Time", "A1", "A2"],
        .. Enumerable.Range(0, rowCount).Select(i => (IReadOnlyList<object?>)
            [
                Math.Round(startMinutes + (i * stepMinutes), 3),
                100 + (i * 1.5),
                200 + (i * 2.5),
            ]),
    ];

    /// <summary>Metadata block rows as typically exported above the table.</summary>
    public static IReadOnlyList<IReadOnlyList<object?>> InstrumentMetadata(int lineCount) =>
        [
            .. Enumerable.Range(0, lineCount).Select(i => (IReadOnlyList<object?>)new object?[] { $"Metadata line {i + 1}: instrument header" }),
        ];

    /// <summary>The standard FAM/ROX mapping over a two-sheet workbook.</summary>
    public static DataMapping TwoSheetMapping() => new(
        RunId,
        new ChannelRegionMapping(
            FluorescenceChannel.Fam,
            "FAM",
            headerRowIndex: 5,
            timeColumnIndex: 0,
            wellColumns: new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }),
        new ChannelRegionMapping(
            FluorescenceChannel.Rox,
            "ROX",
            headerRowIndex: 0,
            timeColumnIndex: 0,
            wellColumns: new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }));

    /// <summary>Mapping for a single-region file (both channels on one body).</summary>
    public static DataMapping BodyMapping() => new(
        RunId,
        new ChannelRegionMapping(
            FluorescenceChannel.Fam,
            DelimitedTextImporter.BodySheetName,
            headerRowIndex: 0,
            timeColumnIndex: 0,
            wellColumns: new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }),
        new ChannelRegionMapping(
            FluorescenceChannel.Rox,
            DelimitedTextImporter.BodySheetName,
            headerRowIndex: 0,
            timeColumnIndex: 0,
            wellColumns: new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }));

    /// <summary>Escapes one CSV field with RFC 4180 quoting when required.</summary>
    public static string CsvEscape(string field)
    {
        if (field.Contains('"'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        return field;
    }

    /// <summary>Builds the text of a CSV/TSV file from rows of raw strings.</summary>
    public static string BuildDelimited(char delimiter, IEnumerable<IEnumerable<string>> rows) =>
        string.Join("\r\n", rows.Select(r => string.Join(delimiter.ToString(), r.Select(CsvEscape))));
}
