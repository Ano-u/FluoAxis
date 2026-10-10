namespace FluoAxis.Import.Tests;

using FluoAxis.Domain;
using FluoAxis.Import;
using Xunit;

public class XlsxImporterTests
{
    private static readonly XlsxImporter Importer = new();

    private static ImportLimits DefaultLimits => ImportLimits.Default;

    [Fact]
    public async Task Inspect_standard_single_sheet_finds_region()
    {
        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", Fixture.KineticTable(startMinutes: 0, stepMinutes: 2, rowCount: 4)));

        using var stream = new MemoryStream(workbook);
        Result<ImportPreview> result = await Importer.InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        ImportPreview preview = result.Value!;
        Assert.Equal(ImportFileFormat.Xlsx, preview.Format);
        RegionCandidate region = Assert.Single(preview.Regions);
        Assert.Equal("FAM", region.SheetName);
        Assert.Equal(0, region.HeaderRowIndex);
        Assert.Equal(0, region.TimeColumnIndex);
        Assert.Equal(FluorescenceChannel.Fam, region.ChannelSuggestion);
        Assert.Equal(2, region.WellColumnMap.Count);
        Assert.Equal(new WellId('A', 1), region.WellColumnMap[1]);
        Assert.Equal(new WellId('A', 2), region.WellColumnMap[2]);
    }

    [Fact]
    public async Task Inspect_two_sheets_reports_both_regions_with_channel_suggestions()
    {
        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", Fixture.KineticTable(0, 2, 4)),
            new WorkbookBuilder.SheetSpec("ROX", Fixture.KineticTable(0, 2, 4)));

        using var stream = new MemoryStream(workbook);
        Result<ImportPreview> result = await Importer.InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        ImportPreview preview = result.Value!;
        Assert.Equal(2, preview.Regions.Count);
        Assert.Equal(FluorescenceChannel.Fam, preview.Regions[0].ChannelSuggestion);
        Assert.Equal(FluorescenceChannel.Rox, preview.Regions[1].ChannelSuggestion);
    }

    [Fact]
    public async Task Inspect_sheet_without_channel_keyword_has_no_suggestion()
    {
        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("Data", Fixture.KineticTable(0, 2, 4)));

        using var stream = new MemoryStream(workbook);
        Result<ImportPreview> result = await Importer.InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Regions[0].ChannelSuggestion);
    }

    [Fact]
    public async Task Inspect_metadata_above_header_is_surfaced()
    {
        List<IReadOnlyList<object?>> rows = [.. Fixture.InstrumentMetadata(5), .. Fixture.KineticTable(0, 2, 4)];
        byte[] workbook = WorkbookBuilder.Build(new WorkbookBuilder.SheetSpec("FAM", rows));

        using var stream = new MemoryStream(workbook);
        Result<ImportPreview> result = await Importer.InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        ImportPreview preview = result.Value!;
        Assert.Equal(5, preview.MetadataLines.Count);
        Assert.Equal(5, preview.Regions[0].HeaderRowIndex);
    }

    [Fact]
    public async Task Inspect_rejects_non_xlsx_stream()
    {
        using var stream = new MemoryStream("not a workbook"u8.ToArray());
        Result<ImportPreview> result = await Importer.InspectAsync(stream, DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorCodes.ImportFormatMismatch, result.Errors[0].Code);
    }

    [Fact]
    public async Task RoundTrip_two_sheets_yields_correct_series_values()
    {
        IReadOnlyList<IReadOnlyList<object?>> famRows = Fixture.KineticTable(0, 2, 3);
        IReadOnlyList<IReadOnlyList<object?>> roxRows =
        [
            ["Time", "A1", "A2"],
            (IReadOnlyList<object?>)[0.0, 1000, 2000],
            (IReadOnlyList<object?>)[2.0, 1010, 2020],
            (IReadOnlyList<object?>)[4.0, 1020, 2040],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", famRows),
            new WorkbookBuilder.SheetSpec("ROX", roxRows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.UserMessage}")));
        ImportedDataset dataset = result.Value!;
        Assert.Equal(4, dataset.Series.Count);

        SignalSeries famA1 = dataset.Series.Single(s => s.Channel == FluorescenceChannel.Fam && s.Well == new WellId('A', 1));
        Assert.Equal(3, famA1.Points.Count);
        Assert.Equal(TimeSpan.Zero, famA1.Points[0].Elapsed);
        Assert.Equal(2 * TimeSpan.TicksPerMinute, famA1.Points[1].Elapsed.Ticks);
        Assert.Equal(100, famA1.Points[0].Rfu);
        Assert.Equal(101.5, famA1.Points[1].Rfu);

        SignalSeries roxA2 = dataset.Series.Single(s => s.Channel == FluorescenceChannel.Rox && s.Well == new WellId('A', 2));
        Assert.Equal(2000, roxA2.Points[0].Rfu);
        Assert.Equal(2040, roxA2.Points[2].Rfu);
    }

    [Fact]
    public async Task RoundTrip_one_sheet_with_distinct_channel_columns_preserves_both_signals()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            ["Time", "A1", "A1_ROX"],
            (IReadOnlyList<object?>)[0.0, 100.0, 900.0],
            (IReadOnlyList<object?>)[2.0, 110.0, 920.0],
        ];
        byte[] workbook = WorkbookBuilder.Build(new WorkbookBuilder.SheetSpec("Signals", rows));
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "Signals", 0, 0, new Dictionary<int, WellId> { [1] = WellId.Parse("A1") }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "Signals", 0, 0, new Dictionary<int, WellId> { [2] = WellId.Parse("A1") }));
        using var stream = new MemoryStream(workbook);

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.UserMessage)));
        Assert.Equal(100.0, result.Value!.Series.Single(series => series.Channel == FluorescenceChannel.Fam).Points[0].Rfu);
        Assert.Equal(900.0, result.Value.Series.Single(series => series.Channel == FluorescenceChannel.Rox).Points[0].Rfu);
    }

    [Fact]
    public async Task RoundTrip_first_valid_time_maps_to_zero()
    {
        // Acquisition starting at 120 s: first point must normalize to t=0.
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            ["Time", "A1"],
            (IReadOnlyList<object?>)[2.0, 100],
            (IReadOnlyList<object?>)[4.0, 110],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        SignalSeries series = result.Value!.Series.Single(s => s.Channel == FluorescenceChannel.Fam);
        Assert.Equal(TimeSpan.Zero, series.Points[0].Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(2), series.Points[1].Elapsed);
    }

    [Fact]
    public async Task Read_shared_strings_mixed_content_resolves()
    {
        // Header and metadata through the shared-string table; numbers inline.
        List<IReadOnlyList<object?>> rows =
        [
            new object?[] { "Instrument: Cytation 5" },
            new object?[] { "Export: Gen5" },
            (IReadOnlyList<object?>)["Time", "A1", "A2"],
            (IReadOnlyList<object?>)[0.0, 10.5, 20.25],
            (IReadOnlyList<object?>)[2.0, 11.5, 21.25],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 2, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 2, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        SignalSeries famA1 = result.Value!.Series.Single(s => s.Channel == FluorescenceChannel.Fam && s.Well == new WellId('A', 1));
        Assert.Equal(10.5, famA1.Points[0].Rfu);
        Assert.Equal(11.5, famA1.Points[1].Rfu);
    }

    [Fact]
    public async Task Read_formula_cells_use_cached_values()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            (IReadOnlyList<object?>)["Time", "A1"],
            (IReadOnlyList<object?>)[0.0, WorkbookBuilder.Formula("42.5")],
            (IReadOnlyList<object?>)[2.0, 43.5],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        SignalSeries series = result.Value!.Series.Single(s => s.Channel == FluorescenceChannel.Fam);
        Assert.Equal(42.5, series.Points[0].Rfu);
    }

    [Fact]
    public async Task Read_inline_string_cells_resolve()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            (IReadOnlyList<object?>)[WorkbookBuilder.Inline("Time"), WorkbookBuilder.Inline("A1")],
            (IReadOnlyList<object?>)[0.0, 50.0],
            (IReadOnlyList<object?>)[2.0, 51.0],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
    }

    [Fact]
    public async Task Read_sparse_rows_and_missing_cells_fail_closed()
    {
        // Row 2 has no A2 cell (sparse): required value missing -> fail closed.
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            (IReadOnlyList<object?>)["Time", "A1", "A2"],
            (IReadOnlyList<object?>)[0.0, 100, 200],
            (IReadOnlyList<object?>)[2.0, 110],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorCodes.ImportInvalidValue, result.Errors[0].Code);
    }

    [Fact]
    public async Task Read_well_set_mismatch_between_channels_fails()
    {
        IReadOnlyList<IReadOnlyList<object?>> famRows =
        [
            ["Time", "A1"],
            (IReadOnlyList<object?>)[0.0, 100],
            (IReadOnlyList<object?>)[2.0, 110],
        ];
        IReadOnlyList<IReadOnlyList<object?>> roxRows =
        [
            ["Time", "A1", "A2"],
            (IReadOnlyList<object?>)[0.0, 100, 200],
            (IReadOnlyList<object?>)[2.0, 110, 210],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", famRows),
            new WorkbookBuilder.SheetSpec("ROX", roxRows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorCodes.ImportWellSetMismatch, result.Errors[0].Code);
        Assert.Contains("A2", result.Errors[0].SafeDetailCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_duplicate_time_fails_with_row_position()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            ["Time", "A1"],
            (IReadOnlyList<object?>)[0.0, 100],
            (IReadOnlyList<object?>)[2.0, 110],
            (IReadOnlyList<object?>)[2.0, 120],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ImportDuplicateTime, result.Errors[0].Code);
        Assert.Contains("row=4", result.Errors[0].SafeDetailCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_non_monotonic_time_fails_with_row_position()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            ["Time", "A1"],
            (IReadOnlyList<object?>)[0.0, 100],
            (IReadOnlyList<object?>)[4.0, 110],
            (IReadOnlyList<object?>)[2.0, 120],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ImportNonMonotonicTime, result.Errors[0].Code);
        Assert.Contains("row=4", result.Errors[0].SafeDetailCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_channel_time_axis_mismatch_fails()
    {
        IReadOnlyList<IReadOnlyList<object?>> famRows =
        [
            ["Time", "A1"],
            (IReadOnlyList<object?>)[0.0, 100],
            (IReadOnlyList<object?>)[2.0, 110],
        ];
        IReadOnlyList<IReadOnlyList<object?>> roxRows =
        [
            ["Time", "A1"],
            (IReadOnlyList<object?>)[0.0, 100],
            (IReadOnlyList<object?>)[5.0, 110],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", famRows),
            new WorkbookBuilder.SheetSpec("ROX", roxRows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorCodes.ImportChannelTimeAxisMismatch, result.Errors[0].Code);
    }

    [Fact]
    public async Task Read_non_numeric_text_cell_fails_closed()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            ["Time", "A1"],
            (IReadOnlyList<object?>)[0.0, "OVER"],
            (IReadOnlyList<object?>)[2.0, 110],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorCodes.ImportInvalidValue, result.Errors[0].Code);
    }

    [Fact]
    public async Task Read_negative_rfu_is_accepted()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            ["Time", "A1"],
            (IReadOnlyList<object?>)[0.0, -12.5],
            (IReadOnlyList<object?>)[2.0, -10.0],
        ];

        byte[] workbook = WorkbookBuilder.Build(
            new WorkbookBuilder.SheetSpec("FAM", rows),
            new WorkbookBuilder.SheetSpec("ROX", rows));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "ROX", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        SignalSeries series = result.Value!.Series.Single(s => s.Channel == FluorescenceChannel.Fam);
        Assert.Equal(-12.5, series.Points[0].Rfu);
    }

    [Fact]
    public async Task Read_sheet_name_mismatch_is_invalid_mapping()
    {
        byte[] workbook = WorkbookBuilder.Build(new WorkbookBuilder.SheetSpec("FAM", Fixture.KineticTable(0, 2, 2)));

        using var stream = new MemoryStream(workbook);
        DataMapping mapping = new(
            Fixture.RunId,
            new ChannelRegionMapping(FluorescenceChannel.Fam, "FAM", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "DoesNotExist", 0, 0, new Dictionary<int, WellId> { [1] = new WellId('A', 1), [2] = new WellId('A', 2) }));

        Result<ImportedDataset> result = await Importer.ReadAsync(stream, mapping, DefaultLimits, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorCodes.ImportInvalidMapping, result.Errors[0].Code);
    }
}
