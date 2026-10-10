namespace FluoAxis.Import;

using System.Globalization;
using System.Text;
using ExcelDataReader;
using FluoAxis.Domain;

/// <summary>Read-only BIFF/OLE adapter. ExcelDataReader returns stored values and never evaluates formulas or macros.</summary>
public sealed class XlsImporter : IImporter
{
    public static bool HasSignature(Stream stream)
    {
        if (!stream.CanSeek) return false;
        Span<byte> prefix = stackalloc byte[8];
        long position = stream.Position;
        int read = stream.Read(prefix);
        stream.Position = position;
        return read == 8 && prefix.SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 })
            || read >= 4 && prefix[0] == 9 && prefix[1] is 0 or 2 or 4 or 8;
    }

    public Task<Result<ImportPreview>> InspectAsync(Stream stream, ImportLimits limits, CancellationToken cancellationToken) =>
        Task.Run(() => Inspect(stream, limits, cancellationToken), cancellationToken);

    public Task<Result<ImportedDataset>> ReadAsync(Stream stream, DataMapping mapping, ImportLimits limits, CancellationToken cancellationToken) =>
        Task.Run(() => Read(stream, mapping, limits, cancellationToken), cancellationToken);

    private static Result<ImportPreview> Inspect(Stream stream, ImportLimits limits, CancellationToken token)
    {
        try
        {
            using SeekableStreamScope scope = SeekableStreamScope.Create(stream, limits);
            if (!HasSignature(scope.Stream)) return Result<ImportPreview>.Fail(ImportErrors.FormatMismatch("xls"));
            using IExcelDataReader reader = Open(scope.Stream);
            List<RegionCandidate> regions = [];
            long rows = 0; int columns = 0;
            var budget = new Budget(limits);
            do
            {
                token.ThrowIfCancellationRequested();
                List<RawRow> window = [];
                while (window.Count < limits.HeaderScanWindowRows && reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    RawRow row = ReadRow(reader, window.Count, budget, null);
                    window.Add(row); rows++; columns = Math.Max(columns, row.Cells.Count);
                }
                int? header = RegionDiscovery.FindHeaderRow(window);
                Dictionary<int, WellId> wells = header is { } index ? RegionDiscovery.MapWellColumns(window[index]) : [];
                RawRow[] body = [.. window.Where(row => header is { } index && row.RowIndex > index)];
                regions.Add(new RegionCandidate(reader.Name, header, RegionDiscovery.FindTimeColumn(body), wells,
                    RegionDiscovery.SuggestChannel(reader.Name), body.Length));
                if (regions.Count > 512) throw new InvalidDataException("Workbook has too many sheets.");
            } while (reader.NextResult());
            return Result<ImportPreview>.Ok(new ImportPreview(ImportFileFormat.Xls, regions, rows, columns, []));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Result<ImportPreview>.Fail(ImportErrors.WorkbookCorrupt("legacyWorkbookRejected")); }
    }

    private static Result<ImportedDataset> Read(Stream stream, DataMapping mapping, ImportLimits limits, CancellationToken token)
    {
        try
        {
            using SeekableStreamScope scope = SeekableStreamScope.Create(stream, limits);
            if (!HasSignature(scope.Stream)) return Result<ImportedDataset>.Fail(ImportErrors.FormatMismatch("xls"));
            var budget = new Budget(limits);
            return new DatasetBuilder(mapping.RunId, mapping.Options).Build(mapping.Fam,
                new RowSource(scope.Stream, mapping.Fam.SheetName, budget, mapping.Options), mapping.Rox,
                new RowSource(scope.Stream, mapping.Rox.SheetName, budget, mapping.Options), token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Result<ImportedDataset>.Fail(ImportErrors.WorkbookCorrupt("legacyWorkbookRejected")); }
    }

    private static IExcelDataReader Open(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        stream.Position = 0;
        return ExcelReaderFactory.CreateBinaryReader(stream, new ExcelReaderConfiguration
        {
            LeaveOpen = true,
            FallbackEncoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        });
    }

    private static RawRow ReadRow(IExcelDataReader reader, int rowIndex, Budget budget, ImportReadOptions? options)
    {
        budget.Add(reader.FieldCount);
        List<RawCell> cells = new(reader.FieldCount);
        for (int column = 0; column < reader.FieldCount; column++)
        {
            object? value = reader.GetValue(column);
            if (value is null) continue;
            // Reader-style date conversions are not silently treated as elapsed time.
            if (value is DateTime or TimeSpan or bool) cells.Add(new RawCell(column, "unsupported-cell-type", null, false));
            else if (value is double or float or int or long or decimal)
                cells.Add(new RawCell(column, null, Convert.ToDouble(value, CultureInfo.InvariantCulture)));
            else
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (text.Length > 32767) throw new InvalidDataException("Workbook text cell exceeds its limit.");
                double number;
                bool numeric = options is null ? RawCell.TryParseFinite(text, out number) : options.TryParseFinite(text, out number);
                cells.Add(numeric ? new RawCell(column, null, number) : new RawCell(column, text, null, options is null));
            }
        }
        return new RawRow(rowIndex, cells);
    }

    private sealed class Budget(ImportLimits limits)
    {
        private long rows;
        private long cells;
        public void Add(int count)
        {
            rows++; cells += count;
            if (count > limits.MaxColumns || rows > limits.MaxRows || cells > limits.MaxCells)
                throw new InvalidDataException("Legacy workbook exceeds the configured parse budget.");
        }
    }

    private sealed class RowSource(Stream stream, string sheet, Budget budget, ImportReadOptions? options) : DatasetBuilder.IRowSource
    {
        public IEnumerable<Result<RawRow>> StreamDataRows(int headerRowIndex, CancellationToken cancellationToken)
        {
            using IExcelDataReader reader = Open(stream);
            int sheets = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++sheets > 512) throw new InvalidDataException("Workbook has too many sheets.");
                if (!string.Equals(reader.Name, sheet, StringComparison.Ordinal)) continue;
                int index = 0;
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RawRow row = ReadRow(reader, index++, budget, options);
                    if (row.RowIndex > headerRowIndex) yield return Result<RawRow>.Ok(row);
                }
                yield break;
            } while (reader.NextResult());
            yield return Result<RawRow>.Fail(ImportErrors.InvalidMapping("sheetNotFound"));
        }
    }
}
