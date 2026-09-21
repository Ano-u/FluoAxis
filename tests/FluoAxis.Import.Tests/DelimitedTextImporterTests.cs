namespace FluoAxis.Import.Tests;

using System.Text;
using FluoAxis.Domain;
using FluoAxis.Import;
using Xunit;

public class DelimitedTextImporterTests
{
    private static readonly ImportLimits DefaultLimits = ImportLimits.Default;

    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B2");

    private static DataMapping Mapping(int headerRow, params WellId[] wells)
    {
        var wellColumns = new Dictionary<int, WellId>();
        for (int i = 0; i < wells.Length; i++)
        {
            wellColumns[i + 1] = wells[i];
        }

        return new DataMapping(
            RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, DelimitedTextImporter.BodySheetName, headerRow, 0, wellColumns),
            new ChannelRegionMapping(FluorescenceChannel.Rox, DelimitedTextImporter.BodySheetName, headerRow, 0, wellColumns));
    }

    private static DelimitedTextImporter Csv() => new();

    private static MemoryStream CsvStream(string content) => new(Encoding.UTF8.GetBytes(content));

    private static string Rows(params string[] rows) => string.Join("\r\n", rows);

    [Fact]
    public async Task Inspect_comma_csv_detects_region()
    {
        string csv = Rows(
            "Time,A1,A2",
            "0,100,200",
            "2,101,201");

        using var stream = CsvStream(csv);
        Result<ImportPreview> result = await Csv().InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        ImportPreview preview = result.Value!;
        Assert.Equal(ImportFileFormat.DelimitedText, preview.Format);
        Assert.Single(preview.Regions);
        Assert.Equal(0, preview.Regions[0].HeaderRowIndex);
        Assert.Equal(2, preview.Regions[0].WellColumnMap.Count);
    }

    [Fact]
    public async Task Inspect_tab_tsv_detects_delimiter()
    {
        string tsv = "Time\tA1\tA2\r\n0\t100\t200\r\n2\t101\t201";

        using var stream = CsvStream(tsv);
        Result<ImportPreview> result = await Csv().InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal('\t', result.Value!.DetectedDelimiter);
    }

    [Fact]
    public async Task Inspect_utf8_bom_detected()
    {
        string csv = "Time,A1\r\n0,100";
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(csv)];

        using var stream = new MemoryStream(bytes);
        Result<ImportPreview> result = await Csv().InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("utf-8", result.Value!.DetectedEncoding, StringComparer.OrdinalIgnoreCase);
        Assert.False(result.Value.EncodingAssumed);
    }

    [Fact]
    public async Task Inspect_no_bom_marks_encoding_assumed()
    {
        using var stream = CsvStream("Time,A1\r\n0,100");
        Result<ImportPreview> result = await Csv().InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EncodingAssumed);
    }

    [Fact]
    public async Task Read_comma_csv_yields_series()
    {
        string csv = Rows(
            "Time,A1,A2",
            "0,100,200",
            "2,101,201",
            "4,102,202");

        using var stream = CsvStream(csv);
        Result<ImportedDataset> result = await Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1), new WellId('A', 2)), DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        IReadOnlyList<SignalSeries> series = result.Value!.Series;
        Assert.Equal(4, series.Count); // 2 wells x 2 channels
        SignalSeries a1Fam = series.Single(s => s.Channel == FluorescenceChannel.Fam && s.Well == new WellId('A', 1));
        Assert.Equal(3, a1Fam.Points.Count);
        Assert.Equal(100, a1Fam.Points[0].Rfu);
        Assert.Equal(TimeSpan.Zero, a1Fam.Points[0].Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(2), a1Fam.Points[1].Elapsed);
    }

    [Fact]
    public async Task Read_quoted_field_with_delimiter_resolves()
    {
        string csv = Rows(
            "\"Time\",\"A1\"",
            "0,100",
            "2,101");

        using var stream = CsvStream(csv);
        Result<ImportedDataset> result = await Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1)), DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        Assert.Equal(2, result.Value!.Series.Count);
        Assert.Equal(101, result.Value.Series[0].Points[1].Rfu);
    }

    [Fact]
    public async Task Read_semicolon_delimiter_detected()
    {
        string csv = Rows(
            "Time;A1;A2",
            "0;100;200",
            "2;101;201");

        using var stream = CsvStream(csv);
        Result<ImportedDataset> result = await Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1), new WellId('A', 2)), DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        Assert.Equal(4, result.Value!.Series.Count);
    }

    [Fact]
    public async Task Read_non_numeric_cell_fails_closed()
    {
        string csv = Rows(
            "Time,A1",
            "0,100",
            "2,NaN");

        using var stream = CsvStream(csv);
        Result<ImportedDataset> result = await Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1)), DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == ImportErrorCodes.ImportInvalidValue);
    }

    [Fact]
    public async Task Read_column_drift_extra_column_ignored_by_mapping()
    {
        // A third (unmapped) column appears in one row: the confirmed
        // mapping is authoritative, so unmapped extra cells are ignored.
        string csv = Rows(
            "Time,A1",
            "0,100",
            "2,101,202");

        using var stream = CsvStream(csv);
        Result<ImportedDataset> result = await Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1)), DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        Assert.Equal(2, result.Value!.Series[0].Points.Count);
    }

    [Fact]
    public async Task Read_row_limit_exceeded_fails()
    {
        var lines = new List<string> { "Time,A1" };
        lines.AddRange(Enumerable.Range(0, 50).Select(i => $"{i},100"));

        ImportLimits small = new(maxRows: 10);
        using var stream = CsvStream(string.Join("\r\n", lines));
        Result<ImportedDataset> result = await Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1)), small, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Code == ImportErrorCodes.ImportTooManyRows);
    }

    [Fact]
    public async Task Read_negative_rfu_accepted()
    {
        string csv = Rows(
            "Time,A1",
            "0,-5",
            "2,-4");

        using var stream = CsvStream(csv);
        Result<ImportedDataset> result = await Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1)), DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        Assert.Equal(-5, result.Value!.Series[0].Points[0].Rfu);
    }

    [Fact]
    public async Task Read_cancellation_propagates()
    {
        string csv = Rows("Time,A1", "0,100", "2,101");
        using var stream = CsvStream(csv);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1)), DefaultLimits, cts.Token));
    }

    [Fact]
    public async Task Read_100k_rows_performance_smoke()
    {
        var lines = new List<string> { "Time,A1" };
        lines.AddRange(Enumerable.Range(0, 100_000).Select(i => $"{i * 0.002:0.###},{100 + (i % 50) * 0.1:0.###}"));

        using var stream = CsvStream(string.Join("\r\n", lines));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Result<ImportedDataset> result = await Csv().ReadAsync(stream, Mapping(0, new WellId('A', 1)), DefaultLimits, CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        Assert.Equal(100_000, result.Value!.Series[0].Points.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"100k rows took {stopwatch.Elapsed.TotalSeconds:F1}s");
    }
}
