namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// Import adapter contract. Implementations are streaming: inspection and
/// reading never build a whole-workbook DOM, and every operation is
/// cancellable. Streams must be readable; seeking is used when available.
/// </summary>
public interface IImporter
{
    /// <summary>
    /// Inspects a file without committing to a mapping: detects the format,
    /// scans for candidate regions (header row, time column, well columns,
    /// channel suggestion), and reports encoding / delimiter conclusions.
    /// </summary>
    Task<Result<ImportPreview>> InspectAsync(Stream stream, ImportLimits limits, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the file using the user-confirmed <paramref name="mapping"/> and
    /// produces the validated per-well series. Channel placement is taken
    /// from the mapping only; sheet-name suggestions are ignored.
    /// </summary>
    Task<Result<ImportedDataset>> ReadAsync(Stream stream, DataMapping mapping, ImportLimits limits, CancellationToken cancellationToken);
}
