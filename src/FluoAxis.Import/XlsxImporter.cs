namespace FluoAxis.Import;

using DocumentFormat.OpenXml.Packaging;
using FluoAxis.Domain;

/// <summary>
/// XLSX import adapter. Format detection requires the ZIP magic bytes
/// followed by a readable Open XML workbook; the workbook is opened
/// read-only and traversed strictly through the SAX reader (shared
/// strings once, then each mapped sheet once). Sheet-name channel hints
/// are preview suggestions only; ReadAsync trusts the confirmed mapping.
/// </summary>
public sealed class XlsxImporter : IImporter
{
    /// <summary>Expected ZIP local-file-header magic of every XLSX package.</summary>
    internal static ReadOnlySpan<byte> ZipMagic => "PK\x03\x04"u8;

    /// <summary>Returns true when the stream starts with the ZIP magic bytes. The stream position is restored.</summary>
    public static bool HasZipMagic(Stream stream)
    {
        if (!stream.CanSeek || stream.Length - stream.Position < 4)
        {
            return false;
        }

        long position = stream.Position;
        try
        {
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            int b3 = stream.ReadByte();
            return b0 == 'P' && b1 == 'K' && b2 == 3 && b3 == 4;
        }
        finally
        {
            stream.Position = position;
        }
    }

    public Task<Result<ImportPreview>> InspectAsync(Stream stream, ImportLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);
        return Task.Run(() => Inspect(stream, limits, cancellationToken), cancellationToken);
    }

    public Task<Result<ImportedDataset>> ReadAsync(Stream stream, DataMapping mapping, ImportLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(limits);
        return Task.Run(() => Read(stream, mapping, limits, cancellationToken), cancellationToken);
    }

    private static Result<ImportPreview> Inspect(Stream stream, ImportLimits limits, CancellationToken cancellationToken)
    {
        try
        {
            using SeekableStreamScope scope = SeekableStreamScope.Create(stream, limits);
            if (!HasZipMagic(scope.Stream))
            {
                return Result<ImportPreview>.Fail(ImportErrors.FormatMismatch("xlsx"));
            }

            string[]? sharedStrings;
            List<(string Name, WorksheetPart Part)> sheets;
            using (SpreadsheetDocument document = OpenWorkbook(scope.Stream))
            {
                sharedStrings = XlsxSheetReader.LoadSharedStrings(document, limits.MaxSharedStringEntries);
                sheets = XlsxSheetReader.GetSheets(document);

                var regions = new List<RegionCandidate>();
                long totalRows = 0;
                int maxColumns = 0;
                List<string> metadataLines = [];

                foreach ((string name, WorksheetPart part) in sheets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var budget = new XlsxSheetReader.ParseBudget(limits.MaxRows, limits.MaxCells);
                    List<RawRow> window = StreamWindow(part, sharedStrings, limits, budget, limits.HeaderScanWindowRows, cancellationToken);

                    totalRows += window.Count;
                    foreach (RawRow row in window)
                    {
                        maxColumns = Math.Max(maxColumns, row.MaxColumnIndex + 1);
                    }

                    int? headerRow = RegionDiscovery.FindHeaderRow(window);
                    Dictionary<int, WellId> wellMap = headerRow is int headerIndex
                        ? RegionDiscovery.MapWellColumns(window.First(r => r.RowIndex == headerIndex))
                        : [];
                    List<RawRow> dataRows = headerRow is int headerRowIndexValue
                        ? [.. window.Where(r => r.RowIndex > headerRowIndexValue)]
                        : [];
                    int? timeColumn = RegionDiscovery.FindTimeColumn(dataRows);

                    if (headerRow is not null && metadataLines.Count == 0)
                    {
                        metadataLines = RegionDiscovery.ExtractMetadataLines(window, headerRow, limits.MaxPreviewMetadataLines);
                    }

                    regions.Add(new RegionCandidate(
                        name,
                        headerRow,
                        timeColumn,
                        wellMap,
                        RegionDiscovery.SuggestChannel(name),
                        estimatedDataRows: dataRows.Count));
                }

                return Result<ImportPreview>.Ok(new ImportPreview(
                    ImportFileFormat.Xlsx,
                    regions,
                    totalRows,
                    maxColumns,
                    metadataLines));
            }
        }
        catch (ImportLimitException ex)
        {
            return Result<ImportPreview>.Fail(LimitError(ex, limits));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsPackageOpenFailure(ex))
        {
            return Result<ImportPreview>.Fail(ImportErrors.FormatMismatch("xlsx"));
        }
        catch (Exception ex)
        {
            return Result<ImportPreview>.Fail(ImportErrors.WorkbookCorrupt(FormatExceptionDetail(ex)));
        }
    }

    private static Result<ImportedDataset> Read(Stream stream, DataMapping mapping, ImportLimits limits, CancellationToken cancellationToken)
    {
        try
        {
            using SeekableStreamScope scope = SeekableStreamScope.Create(stream, limits);
            if (!HasZipMagic(scope.Stream))
            {
                return Result<ImportedDataset>.Fail(ImportErrors.FormatMismatch("xlsx"));
            }

            using SpreadsheetDocument document = OpenWorkbook(scope.Stream);
            string[]? sharedStrings = XlsxSheetReader.LoadSharedStrings(document, limits.MaxSharedStringEntries);
            List<(string Name, WorksheetPart Part)> sheets = XlsxSheetReader.GetSheets(document);

            WorksheetPart? famPart = FindSheet(sheets, mapping.Fam.SheetName);
            WorksheetPart? roxPart = FindSheet(sheets, mapping.Rox.SheetName);
            if (famPart is null || roxPart is null)
            {
                string missing = famPart is null ? mapping.Fam.SheetName : mapping.Rox.SheetName;
                return Result<ImportedDataset>.Fail(ImportErrors.InvalidMapping($"sheetNotFound={missing}"));
            }

            var budget = new XlsxSheetReader.ParseBudget(limits.MaxRows, limits.MaxCells);
            var famSource = new XlsxRowSource(famPart, sharedStrings, limits, budget);
            var roxSource = new XlsxRowSource(roxPart, sharedStrings, limits, budget);

            return new DatasetBuilder(mapping.RunId).Build(
                mapping.Fam,
                famSource,
                mapping.Rox,
                roxSource,
                cancellationToken);
        }
        catch (ImportLimitException ex)
        {
            return Result<ImportedDataset>.Fail(LimitError(ex, limits));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsPackageOpenFailure(ex))
        {
            return Result<ImportedDataset>.Fail(ImportErrors.FormatMismatch("xlsx"));
        }
        catch (Exception ex)
        {
            return Result<ImportedDataset>.Fail(ImportErrors.WorkbookCorrupt(FormatExceptionDetail(ex)));
        }
    }

    private static SpreadsheetDocument OpenWorkbook(Stream stream)
    {
        try
        {
            return SpreadsheetDocument.Open(stream, isEditable: false);
        }
        catch (Exception ex) when (IsPackageOpenFailure(ex))
        {
            throw new ImportFormatException(ex);
        }
    }

    private static bool IsPackageOpenFailure(Exception ex) => ex is ImportFormatException
        || ex is System.IO.InvalidDataException
        || ex is DocumentFormat.OpenXml.Packaging.OpenXmlPackageException;

    private static string FormatExceptionDetail(Exception ex) =>
        ex.GetType().Name.Length > 0 ? ex.GetType().Name : "unknown";

    private static AppError LimitError(ImportLimitException ex, ImportLimits limits) => ex.Code switch
    {
        ImportErrorCodes.ImportFileTooLarge => ImportErrors.FileTooLarge(limits.MaxBytes),
        ImportErrorCodes.ImportTooManyRows => ImportErrors.TooManyRows(limits.MaxRows),
        ImportErrorCodes.ImportTooManyCells => ImportErrors.TooManyCells(limits.MaxCells),
        ImportErrorCodes.ImportTooManyColumns => ImportErrors.TooManyColumns(limits.MaxColumns),
        ImportErrorCodes.ImportSharedStringOverflow => ImportErrors.SharedStringOverflow(limits.MaxSharedStringEntries),
        _ => ImportErrors.WorkbookCorrupt(ex.Code),
    };

    private static List<RawRow> StreamWindow(
        WorksheetPart part,
        string[]? sharedStrings,
        ImportLimits limits,
        XlsxSheetReader.ParseBudget budget,
        int maxRows,
        CancellationToken cancellationToken)
    {
        var window = new List<RawRow>(maxRows);
        foreach (Result<RawRow> row in XlsxSheetReader.StreamRows(part, sharedStrings, limits, budget, cancellationToken))
        {
            if (!row.IsSuccess)
            {
                break;
            }

            window.Add(row.Value!);
            if (window.Count >= maxRows)
            {
                break;
            }
        }

        return window;
    }

    private static WorksheetPart? FindSheet(List<(string Name, WorksheetPart Part)> sheets, string sheetName) =>
        sheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase)).Part;

    /// <summary>Row source over one worksheet; lazily streams rows after the header row.</summary>
    private sealed class XlsxRowSource(WorksheetPart part, string[]? sharedStrings, ImportLimits limits, XlsxSheetReader.ParseBudget budget)
        : DatasetBuilder.IRowSource
    {
        public IReadOnlyList<AppError> Diagnostics { get; } = [];

        public IEnumerable<Result<RawRow>> StreamDataRows(int headerRowIndex, CancellationToken cancellationToken)
        {
            foreach (Result<RawRow> row in XlsxSheetReader.StreamRows(part, sharedStrings, limits, budget, cancellationToken))
            {
                if (!row.IsSuccess)
                {
                    yield return row;
                    yield break;
                }

                if (row.Value!.RowIndex > headerRowIndex)
                {
                    yield return row;
                }
            }
        }
    }

    /// <summary>Wraps package-open failures so they surface as format mismatches.</summary>
    private sealed class ImportFormatException(Exception inner) : Exception(inner.Message, inner);
}
