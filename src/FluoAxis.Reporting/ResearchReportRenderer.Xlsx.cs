namespace FluoAxis.Reporting;

using System.Globalization;
using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluoAxis.Contracts;

public sealed partial class ResearchReportRenderer
{
    /// <summary>Writes a completed package asynchronously; the caller owns the destination and its atomic publication.</summary>
    public async Task RenderXlsxAsync(ReportSnapshotDto snapshot, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("REPORT_DESTINATION_NOT_WRITABLE", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();
        _ = ValidateSnapshot(snapshot);
        using var package = new MemoryStream();
        using (SpreadsheetDocument document = SpreadsheetDocument.Create(package, SpreadsheetDocumentType.Workbook, autoSave: true))
        {
            WorkbookPart workbook = document.AddWorkbookPart();
            document.ChangeIdOfPart(workbook, "rIdWorkbook");
            workbook.Workbook = new Workbook();
            var sheets = workbook.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;
            foreach (ReportTable table in ReportTables.Create(snapshot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relationship = $"rIdSheet{sheetId}";
                WorksheetPart worksheet = workbook.AddNewPart<WorksheetPart>(relationship);
                using (OpenXmlWriter writer = OpenXmlWriter.Create(worksheet))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteStartElement(new SheetData());
                    WriteRow(writer, [ReportCell.FromText("FluoAxis RESEARCH DRAFT · 未批准 · 仅供科研使用")]);
                    WriteRow(writer, [ReportCell.FromText(snapshot.Content.ResearchUseNotice)]);
                    WriteRow(writer, [ReportCell.FromText(snapshot.ReportId), ReportCell.FromText(snapshot.SnapshotSha256)]);
                    WriteRow(writer, [.. table.Columns.Select(ReportCell.FromText)]);
                    foreach (ReportCell[] row in table.Rows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        WriteRow(writer, row);
                    }
                    if (table.Rows.Count == 0) WriteRow(writer, [ReportCell.FromText(table.Name is "Issues" or "CalibrationIssues"
                        ? "没有已记录的问题。" : "未保存此阶段证据；未重算。")]);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                sheets.Append(new Sheet { Id = relationship, SheetId = sheetId++, Name = table.Name });
            }
            workbook.Workbook.Save();
        }
        if (package.Length > ReportSafety.MaxOutputBytes) throw new InvalidOperationException("REPORT_OUTPUT_LIMIT_EXCEEDED");
        package.Position = 0;
        // Open XML creates ZIP timestamps at runtime. Normalize entry order and
        // timestamps so the same immutable report produces identical package bytes.
        using var normalized = new MemoryStream();
        using (var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(normalized, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ZipArchiveEntry copied = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copied.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using Stream input = entry.Open();
                using Stream output = copied.Open();
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
        normalized.Position = 0;
        await normalized.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteRow(OpenXmlWriter writer, IReadOnlyList<ReportCell> cells)
    {
        writer.WriteStartElement(new Row());
        foreach (ReportCell value in cells)
        {
            if (value.Number is { } number)
            {
                writer.WriteElement(new Cell { DataType = CellValues.Number, CellValue = new CellValue(number.ToString("G17", CultureInfo.InvariantCulture)) });
            }
            else
            {
                string text = SpreadsheetText(value.Text ?? string.Empty);
                writer.WriteElement(new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }),
                });
            }
        }
        writer.WriteEndElement();
    }

    private static string SpreadsheetText(string value)
    {
        ReadOnlySpan<char> trimmed = value.AsSpan().TrimStart();
        return trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
    }
}
