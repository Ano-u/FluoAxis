namespace FluoAxis.Presentation;

using System.Globalization;
using FluoAxis.Contracts;

/// <summary>Display-only formatting of confirmed geometry. No scientific or assignment decisions.</summary>
public static class PlateProfileDisplay
{
    public static string FormatWells(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] wells = values.Take(97).Select(value => new PlateProfileWellDto(value, WellRoleNames.Ignore).Well)
            .OrderBy(value => value[0]).ThenBy(Column).ToArray();
        if (wells.Length > 96 || wells.Distinct(StringComparer.Ordinal).Count() != wells.Length)
            throw new ArgumentException("A profile has at most 96 unique wells.", nameof(values));
        if (wells.Length <= 1) return string.Join(", ", wells);
        char firstRow = wells[0][0], lastRow = wells[^1][0];
        int firstColumn = wells.Min(Column), lastColumn = wells.Max(Column);
        if (wells.Length == (lastRow - firstRow + 1) * (lastColumn - firstColumn + 1))
            return string.Create(CultureInfo.InvariantCulture, $"{firstRow}{firstColumn}-{lastRow}{lastColumn}");
        return string.Join(", ", wells);
    }

    public static string DescribeCoverage(PlateProfileDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        int samples = definition.Wells.Count(well => well.Role == WellRoleNames.Sample);
        int ignored = definition.Wells.Count(well => well.Role == WellRoleNames.Ignore);
        int undefined = 96 - definition.Wells.Count;
        string additional = definition.RemainingWellsAreSamples ? "；未定义的已测孔作为样本"
            : undefined > 0 ? $"；{undefined} 个未定义孔需人工分配" : string.Empty;
        return $"样本区 {samples} 孔；忽略 {ignored} 孔{additional}";
    }

    private static int Column(string well) => int.Parse(well.AsSpan(1), CultureInfo.InvariantCulture);
}
