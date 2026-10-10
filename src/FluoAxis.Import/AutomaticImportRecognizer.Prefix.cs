namespace FluoAxis.Import;

using System.Globalization;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelDataReader;

public sealed partial class AutomaticImportRecognizer
{
    private static PrefixBook ReadPrefix(Stream stream, ImportFileFormat format, ImportLimits limits,
        RecognitionParsingOptions? configured, Findings findings, CancellationToken token)
    {
        if (configured is not null)
        {
            _ = new ImportReadOptions(configured.EncodingName, configured.Delimiter, configured.DecimalCulture, configured.TimeUnit);
            findings.Reason("使用用户已保存并明确选择的解析选项；不根据 RFU 猜测孔位角色。");
        }
        var budget = new PrefixBudget(limits);
        return format switch
        {
            ImportFileFormat.Xlsx => ReadXlsxPrefix(stream, limits, budget, findings, token),
            ImportFileFormat.Xls => ReadXlsPrefix(stream, budget, findings, token),
            ImportFileFormat.DelimitedText => ReadTextPrefix(stream, budget, configured, findings, token),
            _ => throw new InvalidDataException("Unsupported recognition format."),
        };
    }

    private static PrefixBook ReadXlsxPrefix(Stream stream, ImportLimits limits, PrefixBudget budget, Findings findings, CancellationToken token)
    {
        if (!XlsxImporter.HasZipMagic(stream)) throw new InvalidDataException("Workbook signature mismatch.");
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (archive.Entries.Count > 2048) throw new InvalidDataException("Workbook entry count exceeds recognition budget.");
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.Length > 64L * 1024 * 1024 || entry.Length > Math.Max(1, entry.CompressedLength) * 200d)
                    throw new InvalidDataException("Workbook expansion exceeds recognition budget.");
                total = checked(total + entry.Length);
                if (total > 256L * 1024 * 1024) throw new InvalidDataException("Workbook expansion exceeds recognition budget.");
            }
        }
        stream.Position = 0;
        using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false,
            new OpenSettings { MaxCharactersInPart = 4L * 1024 * 1024 });
        string[]? strings = ReadRecognitionSharedStrings(document, Math.Min(limits.MaxSharedStringEntries, MaximumPrefixCells), budget, token);
        List<(string Name, WorksheetPart Part)> sheets = XlsxSheetReader.GetSheets(document);
        if (sheets.Count > MaximumSheets)
            findings.Issue("IMPORT_AUTO_PREFIX_LIMIT", "工作表数量超出自动识别范围，请手工选择需要的工作表。");
        var parseBudget = new XlsxSheetReader.ParseBudget(Math.Min(limits.MaxRows, MaximumRowsPerSheet * MaximumSheets), Math.Min(limits.MaxCells, MaximumPrefixCells));
        List<PrefixSheet> result = [];
        foreach ((string name, WorksheetPart part) in sheets.Take(MaximumSheets))
        {
            token.ThrowIfCancellationRequested();
            List<RawRow> rows = [];
            bool truncated = false;
            foreach (var row in XlsxSheetReader.StreamRows(part, strings, limits, parseBudget, token))
            {
                if (!row.IsSuccess) throw new InvalidDataException("Workbook row is invalid.");
                if (rows.Count >= MaximumRowsPerSheet || row.Value!.RowIndex >= MaximumRowsPerSheet) { truncated = true; break; }
                budget.Add(row.Value!);
                rows.Add(row.Value!);
            }
            result.Add(new PrefixSheet(name, rows.AsReadOnly(), truncated));
        }
        findings.Reason("XLSX 只读 SAX 识别；公式只读取已保存缓存，不执行公式、宏或外部链接。");
        return new PrefixBook(result.AsReadOnly(), "utf-8", "comma");
    }

    private static string[]? ReadRecognitionSharedStrings(SpreadsheetDocument document, int maximum,
        PrefixBudget budget, CancellationToken token)
    {
        if (document.WorkbookPart?.SharedStringTablePart is not { } part) return null;
        using OpenXmlReader reader = OpenXmlReader.Create(part);
        List<string> strings = [];
        StringBuilder? text = null;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.IsStartElement && reader.ElementType == typeof(SharedStringItem)) text = new StringBuilder();
            else if (reader.IsStartElement && reader.ElementType == typeof(Text) && text is not null)
            {
                text.Append(reader.GetText());
                if (text.Length > MaximumCellCharacters) throw new InvalidDataException("Shared string exceeds recognition budget.");
            }
            else if (reader.IsEndElement && reader.ElementType == typeof(SharedStringItem) && text is not null)
            {
                if (strings.Count >= maximum) throw new InvalidDataException("Shared strings exceed recognition budget.");
                string value = text.ToString(); budget.AddText(value); strings.Add(value); text = null;
            }
        }
        return [.. strings];
    }

    private static PrefixBook ReadXlsPrefix(Stream stream, PrefixBudget budget, Findings findings, CancellationToken token)
    {
        if (!XlsImporter.HasSignature(stream)) throw new InvalidDataException("Legacy workbook signature mismatch.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        stream.Position = 0;
        using IExcelDataReader reader = ExcelReaderFactory.CreateBinaryReader(stream, new ExcelReaderConfiguration
        {
            LeaveOpen = true,
            FallbackEncoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        });
        List<PrefixSheet> sheets = [];
        do
        {
            token.ThrowIfCancellationRequested();
            if (sheets.Count >= MaximumSheets)
            {
                findings.Issue("IMPORT_AUTO_PREFIX_LIMIT", "工作表数量超出自动识别范围，请手工选择需要的工作表。");
                break;
            }
            List<RawRow> rows = [];
            bool truncated = false;
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (rows.Count >= MaximumRowsPerSheet) { truncated = true; break; }
                if (reader.FieldCount > budget.MaxColumns) throw new InvalidDataException("Workbook columns exceed recognition budget.");
                List<RawCell> cells = [];
                for (int column = 0; column < reader.FieldCount; column++)
                {
                    object? value = reader.GetValue(column);
                    if (value is null) continue;
                    cells.Add(value is double or float or decimal or int or long
                        ? new RawCell(column, null, Convert.ToDouble(value, CultureInfo.InvariantCulture))
                        : new RawCell(column, value is DateTime or TimeSpan ? "unsupported-time-cell" : Convert.ToString(value, CultureInfo.InvariantCulture), null));
                }
                RawRow row = new(rows.Count, cells.AsReadOnly()); budget.Add(row); rows.Add(row);
            }
            sheets.Add(new PrefixSheet(reader.Name, rows.AsReadOnly(), truncated));
        } while (reader.NextResult());
        findings.Reason("XLS 使用只读 BIFF/OLE 行读取；仅读取保存值，不执行公式或宏。");
        return new PrefixBook(sheets.AsReadOnly(), "utf-8", "comma");
    }

    private static PrefixBook ReadTextPrefix(Stream stream, PrefixBudget budget, RecognitionParsingOptions? configured,
        Findings findings, CancellationToken token)
    {
        Span<byte> prefix = stackalloc byte[3];
        stream.Position = 0; int count = stream.Read(prefix); stream.Position = 0;
        string? bomEncoding = count >= 3 && prefix.SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }) ? "utf-8"
            : count >= 2 && prefix[0] == 0xff && prefix[1] == 0xfe ? "utf-16"
            : count >= 2 && prefix[0] == 0xfe && prefix[1] == 0xff ? "utf-16BE" : null;
        if (configured is not null && bomEncoding is not null && configured.EncodingName != bomEncoding)
        {
            findings.Issue("IMPORT_AUTO_ENCODING_CONFLICT", "文件 BOM 与所选孔位方案的编码不同，请明确选择编码。");
            return new PrefixBook([], null, null);
        }
        string encodingName = configured?.EncodingName ?? bomEncoding ?? "utf-8";
        Encoding encoding = new ImportReadOptions(encodingName).GetEncoding();
        if (bomEncoding is not null) stream.Position = bomEncoding == "utf-8" ? 3 : 2;
        List<string> lines = [];
        bool truncated = false;
        try
        {
            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
            for (int index = 0; index <= MaximumRowsPerSheet; index++)
            {
                token.ThrowIfCancellationRequested();
                string? line = ReadBoundedLine(reader, token);
                if (line is null) break;
                if (index == MaximumRowsPerSheet) { truncated = true; break; }
                lines.Add(line);
            }
        }
        catch (DecoderFallbackException)
        {
            findings.Issue("IMPORT_AUTO_ENCODING_REQUIRED", "文本不是所选或严格 UTF 编码，请手工选择编码后重试。");
            return new PrefixBook([], null, null);
        }
        string? delimiterName = configured?.Delimiter;
        if (delimiterName is null)
        {
            string[] candidates = [.. new[] { (Name: "comma", Value: ','), (Name: "tab", Value: '\t'), (Name: "semicolon", Value: ';') }
                .Where(candidate => lines.Any(line =>
                {
                    List<string> cells = DelimitedTextImporter.SplitLine(line, candidate.Value);
                    return cells.Count > 1 && cells.Any(cell => TryTimeHeader(cell, out _))
                        && cells.Any(cell => TrySignalHeader(cell, null, out _, out _, out _));
                })).Select(candidate => candidate.Name)];
            if (candidates.Length != 1)
            {
                findings.Issue("IMPORT_AUTO_DELIMITER_REQUIRED", "未识别到唯一分隔符与语义表头组合，请手工选择分隔符。");
                return new PrefixBook([], encodingName, null);
            }
            delimiterName = candidates[0];
            findings.Reason("分隔符由唯一的 Time/Elapsed 与孔位通道表头组合识别；不根据 RFU 趋势选时间列。");
        }
        var options = new ImportReadOptions(encodingName, delimiterName, configured?.DecimalCulture ?? "invariant", configured?.TimeUnit ?? "minutes");
        List<RawRow> rows = [];
        for (int rowIndex = 0; rowIndex < lines.Count; rowIndex++)
        {
            List<string> fields = DelimitedTextImporter.SplitLine(lines[rowIndex], options.Delimiter);
            if (fields.Count > budget.MaxColumns) throw new InvalidDataException("Text columns exceed recognition budget.");
            RawCell[] cells = [.. fields.Select((text, column) => new RawCell(column, text,
                options.TryParseFinite(text, out double value) ? value : null, false))];
            RawRow row = new(rowIndex, cells); budget.Add(row); rows.Add(row);
        }
        findings.Reason(bomEncoding is not null ? "编码来自文件 BOM。" : configured is not null ? "文本编码来自用户保存的解析选项。" : "无 BOM 的前缀已按严格 UTF-8 解码；无法解码时不会猜测本地编码。");
        return new PrefixBook([new PrefixSheet(DelimitedTextImporter.BodySheetName, rows.AsReadOnly(), truncated)], encodingName, delimiterName);
    }

    private static string? ReadBoundedLine(StreamReader reader, CancellationToken token)
    {
        StringBuilder line = new();
        while (true)
        {
            if ((line.Length & 1023) == 0) token.ThrowIfCancellationRequested();
            int next = reader.Read();
            if (next < 0) return line.Length == 0 ? null : line.ToString();
            if (next == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= MaximumLineCharacters) throw new InvalidDataException("Text line exceeds recognition budget.");
            line.Append((char)next);
        }
    }

    private sealed class PrefixBudget(ImportLimits limits)
    {
        private long rows;
        private long cells;
        private int characters;
        public int MaxColumns { get; } = limits.MaxColumns;
        public void Add(RawRow row)
        {
            if (++rows > limits.MaxRows || (cells += row.Cells.Count) > Math.Min(limits.MaxCells, MaximumPrefixCells)
                || row.MaxColumnIndex >= limits.MaxColumns) throw new InvalidDataException("Recognition prefix exceeds configured limits.");
            foreach (RawCell cell in row.Cells) if (cell.Text is not null) AddText(cell.Text);
        }
        public void AddText(string text)
        {
            if (text.Length > MaximumCellCharacters || (characters += text.Length) > MaximumTextCharacters)
                throw new InvalidDataException("Recognition text exceeds its budget.");
        }
    }
}
