namespace FluoAxis.Import.Tests;

using System.Text;
using FluoAxis.Domain;
using FluoAxis.Import;
using Xunit;

public sealed class ImportOptionsTests
{
    [Fact]
    public async Task ExplicitCultureDelimiterSecondsAndRegionEndPreservePhysicalCoordinates()
    {
        const string content = "metadata\n\nTime;A1;Rox\n0;100,5;50,25\n5400;1000,5;500,25\n7200;1000,5;500,25\nignored footer\n";
        var options = new ImportReadOptions("utf-8", "semicolon", "de-DE", "seconds");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var mapping = Mapping(2, 2, 5, 5, 1, 2, options);

        Result<ImportedDataset> result = await new DelimitedTextImporter(options: options).ReadAsync(stream, mapping, ImportLimits.Default, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, string.Join(';', result.Errors.Select(error => error.Code)));
        SignalSeries fam = Assert.Single(result.Value!.Series, series => series.Channel == FluorescenceChannel.Fam);
        Assert.Equal(100.5, fam.Points[0].Rfu);
        Assert.Equal(TimeSpan.FromMinutes(120), fam.Points[2].Elapsed);
        Assert.Equal(3, fam.Points[0].SourceRowIndex);
        Assert.Equal(5, fam.Points[2].SourceRowIndex);
        Assert.All(fam.Points, point => Assert.Equal(1, point.SourceColumnIndex));
    }

    [Fact]
    public async Task ExplicitCultureDoesNotFallBackToInvariantNumbers()
    {
        var options = new ImportReadOptions("utf-8", "semicolon", "de-DE");
        using var stream = new MemoryStream("Time;A1;Rox\n0;100.5;50,25\n120;1000,5;500,25"u8.ToArray());
        Result<ImportedDataset> result = await new DelimitedTextImporter(options: options).ReadAsync(stream,
            Mapping(0, 0, null, null, 1, 2, options), ImportLimits.Default, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task VerticallyStackedRegionsReadOnlyTheirClosedRowIntervals()
    {
        using var stream = new MemoryStream("Time,A1\n0,100\n120,1000\nTime,A1\n0,50\n120,500\nfooter\n"u8.ToArray());
        var options = new ImportReadOptions();
        Result<ImportedDataset> result = await new DelimitedTextImporter(options: options).ReadAsync(stream,
            Mapping(0, 3, 2, 5, 1, 1, options), ImportLimits.Default, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal([100d, 1000d], result.Value!.Series[0].Points.Select(point => point.Rfu));
        Assert.Equal([50d, 500d], result.Value.Series[1].Points.Select(point => point.Rfu));
        Assert.Equal(4, result.Value.Series[1].Points[0].SourceRowIndex);
    }

    [Fact]
    public async Task XlsxStackedRegionsHonorExplicitBoundariesAndActualSourceCoordinates()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            ["Time", "A1"], [0d, 100d], [120d, 1000d],
            ["Time", "A1"], [0d, 50d], [120d, 500d], ["footer"],
        ];
        using var stream = new MemoryStream(WorkbookBuilder.Build(new WorkbookBuilder.SheetSpec("Signals", rows)));
        var mapping = new DataMapping(Guid.NewGuid(),
            new ChannelRegionMapping(FluorescenceChannel.Fam, "Signals", 0, 0, new Dictionary<int, WellId> { [1] = WellId.Parse("A1") }, 2),
            new ChannelRegionMapping(FluorescenceChannel.Rox, "Signals", 3, 0, new Dictionary<int, WellId> { [1] = WellId.Parse("A1") }, 5));

        Result<ImportedDataset> result = await new XlsxImporter().ReadAsync(stream, mapping, ImportLimits.Default, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1000, result.Value!.Series[0].Points[1].Rfu);
        Assert.Equal(500, result.Value.Series[1].Points[1].Rfu);
        Assert.Equal(5, result.Value.Series[1].Points[1].SourceRowIndex);
        Assert.Equal(1, result.Value.Series[1].Points[1].SourceColumnIndex);
    }

    [Fact]
    public async Task ExplicitEncodingRejectsConflictingBomAndXlsHonorsRowBudget()
    {
        byte[] utf16 = [0xff, 0xfe, .. Encoding.Unicode.GetBytes("Time,A1,Rox\n0,100,50")];
        using var text = new MemoryStream(utf16);
        var options = new ImportReadOptions("utf-8");
        Assert.False((await new DelimitedTextImporter(options: options).ReadAsync(text,
            Mapping(0, 0, null, null, 1, 2, options), ImportLimits.Default, TestContext.Current.CancellationToken)).IsSuccess);
        using var xls = new MemoryStream(Biff2());
        Assert.False((await new XlsImporter().InspectAsync(xls, new ImportLimits(maxRows: 1), TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Theory]
    [InlineData("windows-1252", "metadata café")]
    [InlineData("gb18030", "仪器数据")]
    public async Task ExplicitLegacyEncodingReadsActualEncodedBytes(string encoding, string metadata)
    {
        var options = new ImportReadOptions(encoding, "tab");
        using var stream = new MemoryStream(options.GetEncoding().GetBytes($"{metadata}\nTime\tA1\tRox\n0\t100\t50\n120\t1000\t500\n"));
        Result<ImportedDataset> result = await new DelimitedTextImporter(options: options).ReadAsync(stream,
            Mapping(1, 1, null, null, 1, 2, options), ImportLimits.Default, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(1000, result.Value!.Series[0].Points[1].Rfu);
    }

    [Fact]
    public async Task XlsBiff2StoredValuesReadWithoutOfficeAndWrongSignatureFails()
    {
        byte[] bytes = Biff2();
        using var source = new MemoryStream(bytes);
        var importer = new XlsImporter();
        Result<ImportPreview> preview = await importer.InspectAsync(source, ImportLimits.Default, TestContext.Current.CancellationToken);
        Assert.True(preview.IsSuccess, string.Join(';', preview.Errors.Select(error => error.Code)));
        Assert.Equal(ImportFileFormat.Xls, preview.Value!.Format);
        string sheet = Assert.Single(preview.Value.Regions).SheetName;
        var mapping = new DataMapping(Guid.NewGuid(),
            new ChannelRegionMapping(FluorescenceChannel.Fam, sheet, 0, 0, new Dictionary<int, WellId> { [1] = WellId.Parse("A1") }),
            new ChannelRegionMapping(FluorescenceChannel.Rox, sheet, 0, 0, new Dictionary<int, WellId> { [2] = WellId.Parse("A1") }));
        Result<ImportedDataset> read = await importer.ReadAsync(source, mapping, ImportLimits.Default, TestContext.Current.CancellationToken);
        Assert.True(read.IsSuccess);
        Assert.Equal(1000d, read.Value!.Series[0].Points[1].Rfu);
        using var wrong = new MemoryStream("not an xls"u8.ToArray());
        Assert.False((await importer.ReadAsync(wrong, mapping, ImportLimits.Default, TestContext.Current.CancellationToken)).IsSuccess);
    }

    private static DataMapping Mapping(int famHeader, int roxHeader, int? famEnd, int? roxEnd, int famColumn, int roxColumn, ImportReadOptions options) => new(
        Guid.NewGuid(), new ChannelRegionMapping(FluorescenceChannel.Fam, DelimitedTextImporter.BodySheetName, famHeader, 0,
            new Dictionary<int, WellId> { [famColumn] = WellId.Parse("A1") }, famEnd),
        new ChannelRegionMapping(FluorescenceChannel.Rox, DelimitedTextImporter.BodySheetName, roxHeader, 0,
            new Dictionary<int, WellId> { [roxColumn] = WellId.Parse("A1") }, roxEnd), options);

    private static byte[] Biff2()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Record(ushort id, byte[] body) { writer.Write(id); writer.Write((ushort)body.Length); writer.Write(body); }
        Record(0x0009, [2, 0, 16, 0]);
        void Label(ushort column, string text)
        {
            using var body = new MemoryStream(); using var data = new BinaryWriter(body);
            data.Write((ushort)0); data.Write(column); data.Write(new byte[3]); data.Write((byte)text.Length); data.Write(Encoding.ASCII.GetBytes(text));
            Record(0x0004, body.ToArray());
        }
        void Number(ushort row, ushort column, double value)
        {
            using var body = new MemoryStream(); using var data = new BinaryWriter(body);
            data.Write(row); data.Write(column); data.Write(new byte[3]); data.Write(value); Record(0x0003, body.ToArray());
        }
        Label(0, "Time"); Label(1, "A1"); Label(2, "A2");
        Number(1, 0, 0); Number(1, 1, 100); Number(1, 2, 50);
        Number(2, 0, 120); Number(2, 1, 1000); Number(2, 2, 500);
        Record(0x000A, []);
        return stream.ToArray();
    }
}
