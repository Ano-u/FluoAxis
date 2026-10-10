namespace FluoAxis.Infrastructure.Tests;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using FluoAxis.Reporting;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class PersistentReportExportTests
{
    private static readonly DateTimeOffset IssuedUtc = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "report-integration-fixture";

    [Fact]
    public async Task ResearchFigureRejectsStaleRevisionAndChangesOnlyFrozenReport()
    {
        const string pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aFfoAAAAASUVORK5CYII=";
        using var fixture = new ReportFixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        Guid runId = await CompleteAsync(store, fixture.SourceFile);
        ReportSnapshotDto plain = Value(await store.CreateReportSnapshotAsync(runId, TestContext.Current.CancellationToken));
        int revision = plain.Content.Evidence.Snapshot.Revision;
        var stale = new ReportPlotFigureDto(runId, revision - 1, Guid.NewGuid(), "stale research view", pixel);
        Assert.False((await store.AttachReportPlotFigureAsync(stale, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(plain.SnapshotSha256, Value(await store.CreateReportSnapshotAsync(runId, TestContext.Current.CancellationToken)).SnapshotSha256);
        var figure = new ReportPlotFigureDto(runId, revision, Guid.NewGuid(), "synthetic research view", pixel);
        Assert.True(Value(await store.AttachReportPlotFigureAsync(figure, TestContext.Current.CancellationToken)));
        ReportSnapshotDto illustrated = Value(await store.CreateReportSnapshotAsync(runId, TestContext.Current.CancellationToken));
        Assert.NotEqual(plain.SnapshotSha256, illustrated.SnapshotSha256);
        Assert.Equal(figure, illustrated.Content.PlotFigure);
        Assert.Equal(revision, Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken)).Revision);
        Assert.Equal(JsonSerializer.Serialize(plain.Content.Evidence), JsonSerializer.Serialize(illustrated.Content.Evidence));
    }

    [Fact]
    public async Task CompletedStoredRunExportsOneFrozenSnapshotWithoutChangingEvidenceOrSource()
    {
        using var fixture = new ReportFixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        Guid runId = await CompleteAsync(store, fixture.SourceFile);
        RunEvidenceDto before = Value(await store.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
        byte[] raw = File.ReadAllBytes(Path.Combine(fixture.ManagedRoot, Assert.Single(before.Snapshot.SourceFiles).RelativeResourcePath));
        ReportSnapshotDto snapshot = Value(await store.CreateReportSnapshotAsync(runId, TestContext.Current.CancellationToken));
        ReportSnapshotDto repeatedPreview = Value(await store.CreateReportSnapshotAsync(runId, TestContext.Current.CancellationToken));
        Assert.Equal(snapshot.ReportId, repeatedPreview.ReportId);
        Assert.Equal(snapshot.SnapshotSha256, repeatedPreview.SnapshotSha256);
        var renderer = new ResearchReportRenderer();
        string canonical = renderer.GetCanonicalJson(snapshot);
        Assert.Equal(snapshot.SnapshotSha256, Hash(Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(ReportContentDto.DraftApproval, snapshot.Content.ApprovalStatus);
        Assert.Equal(before.Snapshot.Revision, snapshot.Content.Evidence.Snapshot.Revision);
        Assert.Equal(before.Snapshot.MethodSnapshotHash, snapshot.Content.Evidence.Snapshot.MethodSnapshotHash);
        Assert.Equal(ProvenanceCompletenessNames.StoredComplete, snapshot.Content.Evidence.ProvenanceCompleteness);
        Assert.NotEmpty(snapshot.Content.SoftwareVersion);
        Assert.Equal(canonical, File.ReadAllText(Path.Combine(fixture.ManagedRoot, "reports", snapshot.SnapshotSha256 + ".json")));

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var exportTimer = System.Diagnostics.Stopwatch.StartNew();
        ReportExportDto htmlIdentity = Value(await store.CreateReportExportAsync(snapshot, "html", TestContext.Current.CancellationToken));
        exportTimer.Stop();
        double htmlMilliseconds = exportTimer.Elapsed.TotalMilliseconds;
        exportTimer.Restart();
        ReportExportDto xlsxIdentity = Value(await store.CreateReportExportAsync(snapshot, "xlsx", TestContext.Current.CancellationToken));
        exportTimer.Stop();
        double xlsxMilliseconds = exportTimer.Elapsed.TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        byte[] html = await CopyAsync(store, htmlIdentity);
        byte[] xlsx = await CopyAsync(store, xlsxIdentity);
        Assert.Equal(Encoding.UTF8.GetBytes(renderer.RenderHtml(snapshot)), html);
        using var expectedXlsx = new MemoryStream();
        await renderer.RenderXlsxAsync(snapshot, expectedXlsx, TestContext.Current.CancellationToken);
        Assert.Equal(expectedXlsx.ToArray(), xlsx);
        Assert.Equal(snapshot.SnapshotSha256, htmlIdentity.SnapshotSha256);
        Assert.Equal(snapshot.SnapshotSha256, xlsxIdentity.SnapshotSha256);
        ReportExportDto second = Value(await store.CreateReportExportAsync(snapshot, "xlsx", TestContext.Current.CancellationToken));
        Assert.Equal(xlsxIdentity, second);
        Assert.Equal(xlsx, await CopyAsync(store, second));

        using (var read = new MemoryStream(xlsx))
        using (SpreadsheetDocument document = SpreadsheetDocument.Open(read, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken));
            Cell[] cells = [.. document.WorkbookPart!.WorksheetParts.SelectMany(part => part.Worksheet!.Descendants<Cell>())];
            Assert.Contains(cells, cell => cell.CellValue?.Text == "0.40000000000000002");
            Assert.All(cells, cell => Assert.Null(cell.CellFormula));
        }

        RunEvidenceDto after = Value(await store.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        Assert.Equal(raw, File.ReadAllBytes(Path.Combine(fixture.ManagedRoot, Assert.Single(after.Snapshot.SourceFiles).RelativeResourcePath)));
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(snapshot), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(htmlIdentity), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Root, Encoding.UTF8.GetString(html), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.ManagedRoot, "reports"), "*.tmp"));
        fixture.WriteReviewArtifacts(html, xlsx, snapshot);
        fixture.WriteMeasurement(JsonSerializer.Serialize(new
        {
            scenario = "synthetic-stored-report-html-xlsx", iterations = 1, concurrency = 1,
            htmlMilliseconds, xlsxMilliseconds, htmlBytes = html.LongLength, xlsxBytes = xlsx.LongLength,
            htmlSha256 = htmlIdentity.Sha256, xlsxSha256 = xlsxIdentity.Sha256,
            snapshotSha256 = snapshot.SnapshotSha256,
            fixtureSha256 = "d927f3ff1f8c14acad1ed79c005d2f9fb4bc7ccb71f930d72bc22af86bb36153",
            allocatedBytes = allocated, allocationScope = "process-wide; may include concurrent tests",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            logicalProcessors = Environment.ProcessorCount, baseline = "first integrated report export", slaAsserted = false,
        }));
    }

    [Fact]
    public async Task InvalidCalibrationProducesDraftEvidenceWithoutFormalSampleCalls()
    {
        using var fixture = new ReportFixture(invalidCalibration: true);
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        Guid runId = await CompleteAsync(store, fixture.SourceFile, stopAfterCalibration: true);
        RunEvidenceDto before = Value(await store.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
        Assert.Equal(RunStateNames.RunInvalid, before.Snapshot.DomainState);

        ReportSnapshotDto snapshot = Value(await store.CreateReportSnapshotAsync(runId, TestContext.Current.CancellationToken));
        ReportExportDto identity = Value(await store.CreateReportExportAsync(snapshot, "html", TestContext.Current.CancellationToken));
        string html = Encoding.UTF8.GetString(await CopyAsync(store, identity));

        Assert.Contains("INVALID", html, StringComparison.Ordinal);
        Assert.Contains("CALIBRATION_PLATEAU_RANGE_EXCEEDED", html, StringComparison.Ordinal);
        Assert.DoesNotContain("BRAF V600E 阳性", html, StringComparison.Ordinal);
        Assert.DoesNotContain("BRAF V600E 阴性", html, StringComparison.Ordinal);
        Assert.Equal(before.Snapshot.Revision, Value(await store.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken)).Snapshot.Revision);
    }

    [Fact]
    public async Task UnfinishedRunIsNotReadyAndNoReportIsPublished()
    {
        using var fixture = new ReportFixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        ProjectSnapshotDto project = Value(await store.CreateProjectAsync(new CreateProjectCommand(Guid.NewGuid(), "Unfinished", Actor, IssuedUtc), TestContext.Current.CancellationToken));

        GatewayResult<ReportSnapshotDto> rejected = await store.CreateReportSnapshotAsync(project.RunId, TestContext.Current.CancellationToken);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("SYSTEM_REPORT_NOT_READY", Assert.Single(rejected.Errors).Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.ManagedRoot, "reports")));
        Assert.Equal(0, Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), project.RunId), TestContext.Current.CancellationToken)).Revision);
    }

    [Theory]
    [InlineData("../outside.html")]
    [InlineData("data/raw/source.csv")]
    [InlineData("reports/../../outside.html")]
    public async Task ManagedExportCopyRejectsUnsafeOrNonExportIdentityWithoutWriting(string relativeEntry)
    {
        using var fixture = new ReportFixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        using var destination = new MemoryStream();

        GatewayResult<bool> rejected = await store.CopyManagedExportAsync(relativeEntry, new string('a', 64), destination, TestContext.Current.CancellationToken);

        Assert.False(rejected.IsSuccess);
        Assert.Equal(0, destination.Length);
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(rejected), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HashMismatchAndTamperedSnapshotDoNotPublishOrCopy()
    {
        using var fixture = new ReportFixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        Guid runId = await CompleteAsync(store, fixture.SourceFile);
        ReportSnapshotDto snapshot = Value(await store.CreateReportSnapshotAsync(runId, TestContext.Current.CancellationToken));
        ReportExportDto export = Value(await store.CreateReportExportAsync(snapshot, "html", TestContext.Current.CancellationToken));
        using var destination = new MemoryStream();

        GatewayResult<bool> mismatch = await store.CopyManagedExportAsync($"reports/{snapshot.SnapshotSha256}.html", new string('0', 64), destination, TestContext.Current.CancellationToken);
        GatewayResult<ReportExportDto> tampered = await store.CreateReportExportAsync(new ReportSnapshotDto(snapshot.Content, new string('0', 64)), "html", TestContext.Current.CancellationToken);

        Assert.False(mismatch.IsSuccess);
        Assert.Equal("SYSTEM_EXPORT_HASH_MISMATCH", Assert.Single(mismatch.Errors).Code);
        Assert.Equal(0, destination.Length);
        Assert.False(tampered.IsSuccess);
        Assert.False(File.Exists(Path.Combine(fixture.ManagedRoot, "reports", new string('0', 64) + ".html")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.ManagedRoot, "reports"), "*.tmp"));
        Assert.Equal(export.Sha256, Hash(await CopyAsync(store, export)));
    }

    [Fact]
    public async Task PreCanceledSnapshotExportAndCopyLeaveNoPartialFiles()
    {
        using var fixture = new ReportFixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        Guid runId = await CompleteAsync(store, fixture.SourceFile);
        ReportSnapshotDto snapshot = Value(await store.CreateReportSnapshotAsync(runId, TestContext.Current.CancellationToken));
        string reports = Path.Combine(fixture.ManagedRoot, "reports");
        string[] before = [.. Directory.EnumerateFiles(reports).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal)];
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var destination = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateReportSnapshotAsync(runId, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateReportExportAsync(snapshot, "html", canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateReportExportAsync(snapshot, "xlsx", canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CopyManagedExportAsync($"reports/{snapshot.SnapshotSha256}.html", new string('a', 64), destination, canceled.Token));

        Assert.Equal(before, Directory.EnumerateFiles(reports).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(reports, "*.tmp"));
        Assert.Equal(0, destination.Length);
        Assert.Equal(snapshot.Content.Evidence.Snapshot.Revision, Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken)).Revision);
    }

    private static async Task<byte[]> CopyAsync(PersistentDesktopStore store, ReportExportDto identity)
    {
        using var result = new MemoryStream();
        Assert.True(Value(await store.CopyManagedExportAsync($"reports/{identity.SnapshotSha256}.{identity.Format}", identity.Sha256, result, TestContext.Current.CancellationToken)));
        byte[] bytes = result.ToArray();
        Assert.Equal(identity.FileSizeBytes, bytes.LongLength);
        Assert.Equal(identity.Sha256, Hash(bytes));
        return bytes;
    }

    private static async Task<Guid> CompleteAsync(PersistentDesktopStore store, string sourceFile, bool stopAfterCalibration = false)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        SourceFileDto source = Value(await store.StageAsync(sourceFile, token));
        ProjectSnapshotDto project = Value(await store.CreateProjectAsync(new CreateProjectCommand(Guid.NewGuid(), "双通道科研技术报告样本", Actor, IssuedUtc), token));
        Guid runId = project.RunId;
        Value(await store.InspectImportAsync(new InspectImportCommand(Guid.NewGuid(), runId, source.OriginalFileName, source.FileSizeBytes, source.RelativeResourcePath), null, token));
        RunSnapshotDto snapshot = Value(await store.ConfirmMappingAsync(new ConfirmMappingCommand(Guid.NewGuid(), runId, 0, Actor, IssuedUtc,
            new ChannelRegionMappingDto(ChannelNames.Fam, DelimitedTextImporter.BodySheetName, 0, 0, [new(1, "A1"), new(2, "A2"), new(3, "A3"), new(4, "A4"), new(5, "A5")]),
            new ChannelRegionMappingDto(ChannelNames.Rox, DelimitedTextImporter.BodySheetName, 0, 0, [new(6, "A1"), new(7, "A2"), new(8, "A3"), new(9, "A4"), new(10, "A5")]),
            source.OriginalFileName, source.RelativeResourcePath, source.Sha256, source.FileSizeBytes, SourceFileDto.MediaTypeNames.Csv), token));
        WellAssignmentDto[] roles = [new("A1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100), new("A3", WellRoleNames.WildType), new("A4", WellRoleNames.Lqc01Percent), new("A5", WellRoleNames.Sample, "SYN-040", replicateIndex: 1, notes: "已存储双通道证据；此样本不是患者数据。")];
        snapshot = Value(await store.AssignWellsAsync(new AssignWellsCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc, roles), token));
        CalibrationResultDto calibration = Value(await store.RunCalibrationAsync(new RunCalibrationCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token));
        if (stopAfterCalibration) { Assert.Equal(StatusNames.Fail, calibration.Status); return runId; }
        Assert.Equal(StatusNames.Pass, calibration.Status);
        snapshot = Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));
        Assert.Equal(StatusNames.Pass, Value(await store.RunControlQcAsync(new RunControlQcCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token)).Status);
        snapshot = Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));
        Assert.Equal(CallNames.Positive, Assert.Single(Value(await store.AnalyzeSamplesAsync(new AnalyzeSamplesCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token))).Call);
        return runId;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static T Value<T>(GatewayResult<T> result)
    {
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Code)));
        return result.Value!;
    }

    private sealed class ReportFixture : IDisposable
    {
        private readonly string repositoryRoot;
        private readonly string approvedRoot;
        public string Root { get; }
        public string ManagedRoot { get; }
        public string SourceFile { get; }

        public ReportFixture(bool invalidCalibration = false)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluoAxis.slnx"))) directory = directory.Parent;
            repositoryRoot = directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
            approvedRoot = Path.Combine(repositoryRoot, "artifacts", "test-tmp");
            Root = Path.Combine(approvedRoot, nameof(PersistentReportExportTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            ManagedRoot = Path.Combine(Root, "managed");
            SourceFile = Path.Combine(Root, "instrument.csv");
            Directory.CreateDirectory(Root);
            string content = invalidCalibration ? ManagedFileWorkflowTests.CsvFixture.Replace("120,100,1000,100", "120,100,1200,100", StringComparison.Ordinal) : ManagedFileWorkflowTests.CsvFixture;
            File.WriteAllText(SourceFile, content, new UTF8Encoding(false));
        }

        public void WriteReviewArtifacts(byte[] html, byte[] xlsx, ReportSnapshotDto snapshot)
        {
            string output = Path.Combine(repositoryRoot, "artifacts", "reports", "ft006");
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "sample.html"), html);
            File.WriteAllBytes(Path.Combine(output, "sample.xlsx"), xlsx);
            var context = new ReportJsonContext(new JsonSerializerOptions(ReportJsonContext.WireOptions));
            File.WriteAllText(Path.Combine(output, "snapshot.json"), JsonSerializer.Serialize(snapshot, context.ReportSnapshotDto), new UTF8Encoding(false));
        }

        public void WriteMeasurement(string json) => File.WriteAllText(
            Path.Combine(repositoryRoot, "artifacts", "reports", "ft006", "measurement.json"), json, new UTF8Encoding(false));

        public void Dispose()
        {
            string normalized = Path.GetFullPath(Root);
            string approved = Path.GetFullPath(approvedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!normalized.StartsWith(approved, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing cleanup outside test artifacts.");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
