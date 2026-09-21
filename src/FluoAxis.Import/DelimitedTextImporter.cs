namespace FluoAxis.Import;

using System.Text;
using FluoAxis.Domain;

/// <summary>
/// CSV/TSV/TXT import adapter. Encoding detection follows the Data
/// Contract: UTF-8 BOM selects UTF-8, UTF-16 BOM selects Unicode
/// (respecting the byte order), and a missing BOM defaults to UTF-8 with
/// <c>encodingAssumed=true</c>. The delimiter is inferred from a sample of
/// leading lines (comma, tab, or semicolon — highest count wins) unless
/// explicitly overridden via the constructor. RFC 4180 quoted cells are
/// supported. Parsing is streaming per line with InvariantCulture numbers;
/// ragged lines become row-level diagnostics in the preview stage and
/// fail closed during a confirmed read.
/// </summary>
public sealed class DelimitedTextImporter(char? delimiterOverride = null) : IImporter
{
    private const int DelimiterSampleLines = 20;

    /// <summary>Candidate delimiters considered during detection (in priority order).</summary>
    private static readonly char[] CandidateDelimiters = [',', '\t', ';'];

    /// <summary>Fixed logical sheet name for the single body region of delimited text.</summary>
    public const string BodySheetName = "text-body";

    /// <summary>Result of the bounded leading-sample read used for detection.</summary>
    private readonly record struct DetectionSample(string EncodingName, bool EncodingAssumed, char? Delimiter);

    /// <summary>Encoding conclusion plus the BOM length consumed before content starts.</summary>
    private readonly record struct EncodingDetection(Encoding Encoding, bool Assumed, int BomLength);

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

    private Result<ImportPreview> Inspect(Stream stream, ImportLimits limits, CancellationToken cancellationToken)
    {
        try
        {
            using SeekableStreamScope scope = SeekableStreamScope.Create(stream, limits);
            cancellationToken.ThrowIfCancellationRequested();

            Result<DetectionSample> detection = DetectEncodingAndDelimiter(scope.Stream, cancellationToken);
            if (!detection.IsSuccess)
            {
                return Result<ImportPreview>.Fail([.. detection.Errors]);
            }

            DetectionSample sample = detection.Value!;
            char delimiter = sample.Delimiter ?? ',';

            List<RawRow> window = ReadWindow(scope.Stream, limits, delimiter, limits.HeaderScanWindowRows, cancellationToken);

            int maxColumns = 0;
            foreach (RawRow row in window)
            {
                maxColumns = Math.Max(maxColumns, row.MaxColumnIndex + 1);
            }

            int? headerRow = RegionDiscovery.FindHeaderRow(window);
            Dictionary<int, WellId> wellMap = [];
            List<RawRow> dataRows = [];
            if (headerRow is int headerIndex)
            {
                wellMap = RegionDiscovery.MapWellColumns(window.First(r => r.RowIndex == headerIndex));
                dataRows = [.. window.Where(r => r.RowIndex > headerIndex)];
            }

            int? timeColumn = RegionDiscovery.FindTimeColumn(dataRows);
            List<string> metadataLines = RegionDiscovery.ExtractMetadataLines(window, headerRow, limits.MaxPreviewMetadataLines);

            var region = new RegionCandidate(
                BodySheetName,
                headerRow,
                timeColumn,
                wellMap,
                channelSuggestion: null,
                estimatedDataRows: dataRows.Count);

            return Result<ImportPreview>.Ok(new ImportPreview(
                ImportFileFormat.DelimitedText,
                [region],
                estimatedTotalRows: window.Count,
                estimatedMaxColumns: maxColumns,
                metadataLines,
                sample.EncodingName,
                sample.EncodingAssumed,
                sample.Delimiter));
        }
        catch (ImportLimitException ex)
        {
            return Result<ImportPreview>.Fail(LimitError(ex, limits));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<ImportPreview>.Fail(ImportErrors.WorkbookCorrupt(FormatExceptionDetail(ex)));
        }
    }

    private Result<ImportedDataset> Read(Stream stream, DataMapping mapping, ImportLimits limits, CancellationToken cancellationToken)
    {
        try
        {
            using SeekableStreamScope scope = SeekableStreamScope.Create(stream, limits);
            cancellationToken.ThrowIfCancellationRequested();

            Result<DetectionSample> detection = DetectEncodingAndDelimiter(scope.Stream, cancellationToken);
            if (!detection.IsSuccess)
            {
                return Result<ImportedDataset>.Fail([.. detection.Errors]);
            }

            char delimiter = delimiterOverride ?? detection.Value!.Delimiter ?? ',';

            // Delimited text has a single body region; both channels must map
            // that same region.
            if (!string.Equals(mapping.Fam.SheetName, BodySheetName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(mapping.Rox.SheetName, BodySheetName, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ImportedDataset>.Fail(
                    ImportErrors.InvalidMapping("delimitedTextRequiresBodyRegion"));
            }

            var source = new DelimitedRowSource(scope.Stream, delimiter, limits);
            return new DatasetBuilder(mapping.RunId).Build(
                mapping.Fam,
                source,
                mapping.Rox,
                source,
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
        catch (Exception ex)
        {
            return Result<ImportedDataset>.Fail(ImportErrors.WorkbookCorrupt(FormatExceptionDetail(ex)));
        }
    }

    /// <summary>
    /// Reads the BOM and a bounded sample of leading lines for encoding and
    /// delimiter detection, rewinding the stream for the full parse. The
    /// stream must be seekable (the scope guarantees this).
    /// </summary>
    private Result<DetectionSample> DetectEncodingAndDelimiter(Stream stream, CancellationToken cancellationToken)
    {
        EncodingDetection encodingDetection = DetectEncoding(stream);
        stream.Position = encodingDetection.BomLength;

        List<string> lines = [];
        using (var reader = new StreamReader(stream, encodingDetection.Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true))
        {
            for (int i = 0; i < DelimiterSampleLines; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                lines.Add(line);
                if (reader.EndOfStream)
                {
                    break;
                }
            }
        }

        stream.Position = 0;

        char? delimiter = delimiterOverride ?? DetectDelimiter(lines);
        return Result<DetectionSample>.Ok(new DetectionSample(
            encodingDetection.Encoding.WebName,
            encodingDetection.Assumed,
            delimiter));
    }

    /// <summary>Reads the BOM without consuming it permanently; position is restored to zero.</summary>
    private static EncodingDetection DetectEncoding(Stream stream)
    {
        stream.Position = 0;
        int b0 = stream.ReadByte();
        int b1 = stream.ReadByte();
        int b2 = stream.ReadByte();
        stream.Position = 0;

        if (b0 == 0xEF && b1 == 0xBB && b2 == 0xBF)
        {
            return new EncodingDetection(Encoding.UTF8, Assumed: false, BomLength: 3);
        }

        if (b0 == 0xFF && b1 == 0xFE)
        {
            return new EncodingDetection(Encoding.Unicode, Assumed: false, BomLength: 2);
        }

        if (b0 == 0xFE && b1 == 0xFF)
        {
            return new EncodingDetection(Encoding.BigEndianUnicode, Assumed: false, BomLength: 2);
        }

        return new EncodingDetection(Encoding.UTF8, Assumed: true, BomLength: 0);
    }

    private static char? DetectDelimiter(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        char bestDelimiter = CandidateDelimiters[0];
        int bestCount = 0;
        foreach (char candidate in CandidateDelimiters)
        {
            int count = CountOutsideQuotes(lines, candidate);
            if (count > bestCount)
            {
                bestCount = count;
                bestDelimiter = candidate;
            }
        }

        return bestCount > 0 ? bestDelimiter : null;
    }

    private static int CountOutsideQuotes(List<string> lines, char delimiter)
    {
        int count = 0;
        foreach (string line in lines)
        {
            bool inQuotes = false;
            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == delimiter && !inQuotes)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Splits one physical line into cells honoring RFC 4180 quoting.</summary>
    internal static List<string> SplitLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"' && current.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static List<RawRow> ReadWindow(Stream stream, ImportLimits limits, char delimiter, int maxRows, CancellationToken cancellationToken)
    {
        var window = new List<RawRow>(maxRows);
        EncodingDetection encodingDetection = DetectEncoding(stream);
        stream.Position = encodingDetection.BomLength;

        using var reader = new StreamReader(stream, encodingDetection.Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        int rowIndex = 0;
        string? line;
        while (window.Count < maxRows && (line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length == 0)
            {
                continue;
            }

            List<string> fields = SplitLine(line, delimiter);
            if (fields.Count > limits.MaxColumns)
            {
                throw new ImportLimitException(
                    ImportErrorCodes.ImportTooManyColumns,
                    $"A line exceeds the column limit of {limits.MaxColumns}.");
            }

            var cells = new List<RawCell>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                cells.Add(RawCell.TryParseFinite(fields[i], out double number)
                    ? new RawCell(i, null, number)
                    : new RawCell(i, fields[i], null));
            }

            window.Add(new RawRow(rowIndex, cells));
            rowIndex++;
        }

        stream.Position = 0;
        return window;
    }

    private static AppError LimitError(ImportLimitException ex, ImportLimits limits) => ex.Code switch
    {
        ImportErrorCodes.ImportFileTooLarge => ImportErrors.FileTooLarge(limits.MaxBytes),
        ImportErrorCodes.ImportTooManyRows => ImportErrors.TooManyRows(limits.MaxRows),
        ImportErrorCodes.ImportTooManyCells => ImportErrors.TooManyCells(limits.MaxCells),
        ImportErrorCodes.ImportTooManyColumns => ImportErrors.TooManyColumns(limits.MaxColumns),
        _ => ImportErrors.WorkbookCorrupt(ex.Code),
    };

    private static string FormatExceptionDetail(Exception ex) =>
        ex.GetType().Name.Length > 0 ? ex.GetType().Name : "unknown";

    /// <summary>Streams rows from a delimited text stream starting after the BOM.</summary>
    private sealed class DelimitedRowSource(Stream stream, char delimiter, ImportLimits limits) : DatasetBuilder.IRowSource
    {
        public IEnumerable<Result<RawRow>> StreamAllRows(CancellationToken cancellationToken)
        {
            EncodingDetection encodingDetection = DetectEncoding(stream);
            stream.Position = encodingDetection.BomLength;
            using var reader = new StreamReader(stream, encodingDetection.Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            int rowIndex = 0;
            long cellsParsed = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (rowIndex >= limits.MaxRows)
                {
                    throw new ImportLimitException(
                        ImportErrorCodes.ImportTooManyRows,
                        $"Row limit of {limits.MaxRows} rows exceeded.");
                }

                if (line.Length > 0)
                {
                    List<string> fields = SplitLine(line, delimiter);
                    if (fields.Count > limits.MaxColumns)
                    {
                        throw new ImportLimitException(
                            ImportErrorCodes.ImportTooManyColumns,
                            $"A line exceeds the column limit of {limits.MaxColumns}.");
                    }

                    cellsParsed += fields.Count;
                    if (cellsParsed > limits.MaxCells)
                    {
                        throw new ImportLimitException(
                            ImportErrorCodes.ImportTooManyCells,
                            $"Cell budget of {limits.MaxCells} cells exceeded.");
                    }

                    var cells = new List<RawCell>(fields.Count);
                    for (int i = 0; i < fields.Count; i++)
                    {
                        cells.Add(RawCell.TryParseFinite(fields[i], out double number)
                            ? new RawCell(i, null, number)
                            : new RawCell(i, fields[i], null));
                    }

                    yield return Result<RawRow>.Ok(new RawRow(rowIndex, cells));
                }

                rowIndex++;
            }
        }

        public IEnumerable<Result<RawRow>> StreamDataRows(int headerRowIndex, CancellationToken cancellationToken)
        {
            foreach (Result<RawRow> row in StreamAllRows(cancellationToken))
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

        private static EncodingDetection DetectEncoding(Stream stream)
        {
            if (!stream.CanSeek)
            {
                return new EncodingDetection(Encoding.UTF8, Assumed: true, BomLength: 0);
            }

            long position = stream.Position;
            try
            {
                stream.Position = 0;
                int b0 = stream.ReadByte();
                int b1 = stream.ReadByte();
                int b2 = stream.ReadByte();
                if (b0 == 0xEF && b1 == 0xBB && b2 == 0xBF)
                {
                    return new EncodingDetection(Encoding.UTF8, Assumed: false, BomLength: 3);
                }

                if (b0 == 0xFF && b1 == 0xFE)
                {
                    return new EncodingDetection(Encoding.Unicode, Assumed: false, BomLength: 2);
                }

                if (b0 == 0xFE && b1 == 0xFF)
                {
                    return new EncodingDetection(Encoding.BigEndianUnicode, Assumed: false, BomLength: 2);
                }

                return new EncodingDetection(Encoding.UTF8, Assumed: true, BomLength: 0);
            }
            finally
            {
                stream.Position = position;
            }
        }
    }
}
