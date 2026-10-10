namespace FluoAxis.Infrastructure.Tests;

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using Xunit;

/// <summary>Real synthetic bytes cross every production import and analysis boundary; no signal port is mocked.</summary>
[Collection(FilesystemBoundaryGroup.Name)]
public sealed class ManagedFileWorkflowTests
{
    private static readonly DateTimeOffset IssuedUtc = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "synthetic-workflow-test";
    private const string CsvSha256 = "d927f3ff1f8c14acad1ed79c005d2f9fb4bc7ccb71f930d72bc22af86bb36153";

    // Independent hand calculation: NTC/WT are (100,50), positive is (1000,500).
    // LQC: (640-100)/900 = 0.6 and ROX Y=0; sample: (460-100)/900 = 0.4 and ROX Y=0.
    // The other time points prove that the endpoint, not a time-course average, drives the sample call.
    internal const string CsvFixture =
        "Time,A1,A2,A3,A4,A5,A1_ROX,A2_ROX,A3_ROX,A4_ROX,A5_ROX\n" +
        "0,100,100,100,100,100,50,50,50,50,50\n" +
        "90,100,1000,100,640,280,50,500,50,50,50\n" +
        "120,100,1000,100,640,460,50,500,50,50,50\n";

    [Theory]
    [InlineData("csv", ',')]
    [InlineData("tsv", '\t')]
    public async Task StagedFileBytesReachAnalyzedWithIndependentChannelValues(string extension, char delimiter)
    {
        using var fixture = new FileWorkspace();
        byte[] bytes = Encoding.UTF8.GetBytes(CsvFixture.Replace(',', delimiter));
        string sourcePath = fixture.WriteExternal($"dual-channel.{extension}", bytes);
        Result<StagedImportResource> staging = await new ManagedImportStagingAdapter(fixture.ManagedRoot)
            .StageAsync(sourcePath, TestContext.Current.CancellationToken);
        Assert.True(staging.IsSuccess, DomainErrors(staging));
        StagedImportResource source = staging.Value!;
        string expectedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal(expectedHash, source.Sha256.Value);
        Assert.Equal(bytes.Length, source.SizeBytes);
        Assert.Equal(extension == "csv" ? SourceFileDto.MediaTypeNames.Csv : SourceFileDto.MediaTypeNames.Tsv, source.MediaType);
        Assert.Equal(bytes, fixture.ReadManaged(source.ResourcePath));
        if (extension == "csv") Assert.Equal(CsvSha256, expectedHash);

        var repository = new InMemoryProjectRunRepository();
        var gateway = new ApplicationGateway(repository,
            importInspection: new ManagedImportInspectionAdapter(fixture.ManagedRoot),
            signalPreparation: new ManagedSignalPreparationAdapter(fixture.ManagedRoot));
        await ExecuteAndAssertAsync(gateway, repository, source, expectedHash);
        Assert.Equal(bytes, fixture.ReadManaged(source.ResourcePath));
    }

    [Fact]
    public async Task ReusedStagedFileMeasurementRecordsThirtyFreshRunsWithoutAnSla()
    {
        using var fixture = new FileWorkspace();
        byte[] bytes = Encoding.UTF8.GetBytes(CsvFixture);
        string sourcePath = fixture.WriteExternal("dual-channel.csv", bytes);
        Result<StagedImportResource> staging = await new ManagedImportStagingAdapter(fixture.ManagedRoot)
            .StageAsync(sourcePath, TestContext.Current.CancellationToken);
        Assert.True(staging.IsSuccess, DomainErrors(staging));
        StagedImportResource source = staging.Value!;
        Assert.Equal(CsvSha256, source.Sha256.Value);
        var repository = new InMemoryProjectRunRepository();
        var gateway = new ApplicationGateway(repository,
            importInspection: new ManagedImportInspectionAdapter(fixture.ManagedRoot),
            signalPreparation: new ManagedSignalPreparationAdapter(fixture.ManagedRoot));

        // Warm-up is excluded; the measured loop includes real file reads, hash revalidation,
        // gateway calls, engine calculation, in-memory persistence, and correctness assertions.
        await ExecuteAndAssertAsync(gateway, repository, source, CsvSha256);
        const int iterations = 30;
        using Process process = Process.GetCurrentProcess();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        for (int iteration = 0; iteration < iterations; iteration++)
            await ExecuteAndAssertAsync(gateway, repository, source, CsvSha256);
        stopwatch.Stop();
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        TimeSpan cpuElapsed = process.TotalProcessorTime - cpuBefore;

        var measurement = new
        {
            schemaVersion = 1,
            fixture = "dual-channel.csv",
            fixtureSha256 = CsvSha256,
            fixtureBytes = bytes.Length,
            wells = 5,
            channels = 2,
            timePoints = 3,
            iterations,
            warmupIterations = 1,
            concurrency = 1,
            elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            meanMillisecondsPerRun = stopwatch.Elapsed.TotalMilliseconds / iterations,
            cpuMilliseconds = cpuElapsed.TotalMilliseconds,
            allocatedBytes,
            allocationScope = "process-wide managed allocation; includes assertions and retained in-memory Run records",
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            logicalProcessorCount = Environment.ProcessorCount,
            processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString(),
            scope = "inspection through analyzed; source staging excluded; sequential; synthetic engineering evidence only",
            slaAsserted = false,
        };
        string json = JsonSerializer.Serialize(measurement, new JsonSerializerOptions { WriteIndented = true });
        string reportEntry = fixture.WriteBenchmarkReport(json);
        TestContext.Current.TestOutputHelper?.WriteLine(json);
        TestContext.Current.TestOutputHelper?.WriteLine($"Measurement artifact: {reportEntry}");
        Assert.Equal(bytes, fixture.ReadManaged(source.ResourcePath));
    }

    private static async Task ExecuteAndAssertAsync(ApplicationGateway gateway, InMemoryProjectRunRepository repository,
        StagedImportResource source, string expectedHash)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ProjectSnapshotDto project = Value(await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Synthetic dual-channel run", Actor, IssuedUtc), token));
        Guid runId = project.RunId;
        ImportPreviewDto preview = Value(await gateway.InspectImportAsync(new InspectImportCommand(
            Guid.NewGuid(), runId, source.DisplayName, source.SizeBytes, source.ResourcePath.Value, source.MediaType), null, token));
        Assert.Equal("delimitedText", preview.DetectedFormat);
        RegionCandidateDto candidate = Assert.Single(preview.CandidateRegions);
        Assert.Equal(DelimitedTextImporter.BodySheetName, candidate.SheetName);
        Assert.Equal(0, candidate.HeaderRowIndex);
        Assert.Equal(0, candidate.TimeColumnIndex);
        Assert.Equal(5, candidate.WellColumns.Count);
        Assert.Equal(3, candidate.EstimatedDataRows);
        RunSnapshotDto snapshot = await SnapshotAsync(gateway, runId, token);
        Assert.Equal(RunStateNames.New, snapshot.DomainState);
        Assert.Empty(snapshot.SourceFiles);

        // Both channels share a table, with explicit disjoint column sets. The preview does not guess ROX.
        var fam = new ChannelRegionMappingDto(ChannelNames.Fam, candidate.SheetName, 0, 0,
            [new(1, "A1"), new(2, "A2"), new(3, "A3"), new(4, "A4"), new(5, "A5")]);
        var rox = new ChannelRegionMappingDto(ChannelNames.Rox, candidate.SheetName, 0, 0,
            [new(6, "A1"), new(7, "A2"), new(8, "A3"), new(9, "A4"), new(10, "A5")]);
        snapshot = Value(await gateway.ConfirmMappingAsync(new ConfirmMappingCommand(
            Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc, fam, rox, source.DisplayName,
            source.ResourcePath.Value, source.Sha256.Value, source.SizeBytes, source.MediaType), token));
        Assert.Equal(RunStateNames.Imported, snapshot.DomainState);
        SourceFileDto savedSource = Assert.Single(snapshot.SourceFiles);
        Assert.Equal(expectedHash, savedSource.Sha256);
        Assert.Equal(source.ResourcePath.Value, savedSource.RelativeResourcePath);
        Assert.Equal(source.SizeBytes, savedSource.FileSizeBytes);

        IReadOnlyList<SignalSeries> signals = await repository.GetSignalsAsync(runId, token);
        Assert.Equal(10, signals.Count);
        Assert.All(signals, series =>
        {
            Assert.Equal(runId, series.RunId);
            Assert.Equal(new[] { 0.0, 90.0, 120.0 }, series.Points.Select(point => point.Elapsed.TotalMinutes));
        });
        Assert.Equal(460.0, signals.Single(series => series.Well.ToString() == "A5" && series.Channel == FluorescenceChannel.Fam).Points[^1].Rfu);
        Assert.Equal(50.0, signals.Single(series => series.Well.ToString() == "A5" && series.Channel == FluorescenceChannel.Rox).Points[^1].Rfu);

        WellAssignmentDto[] roles =
        [
            new("A1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100), new("A3", WellRoleNames.WildType),
            new("A4", WellRoleNames.Lqc01Percent), new("A5", WellRoleNames.Sample, "SYN-040", replicateIndex: 1),
        ];
        snapshot = Value(await gateway.AssignWellsAsync(new AssignWellsCommand(
            Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc, roles), token));
        Assert.Equal(RunStateNames.Mapped, snapshot.DomainState);
        CalibrationResultDto calibration = Value(await gateway.RunCalibrationAsync(new RunCalibrationCommand(
            Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token));
        Assert.Equal(StatusNames.Pass, calibration.Status);
        Assert.Equal(100.0, calibration.RunFbFam);
        Assert.Equal(50.0, calibration.RunFbRox);
        Assert.Equal(1000.0, calibration.RunFmFam);
        Assert.Equal(500.0, calibration.RunFmRox);
        snapshot = await SnapshotAsync(gateway, runId, token);
        Assert.Equal(RunStateNames.CalibrationPassed, snapshot.DomainState);

        ControlQcResultDto control = Value(await gateway.RunControlQcAsync(new RunControlQcCommand(
            Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token));
        Assert.Equal(StatusNames.Pass, control.Status);
        Assert.Equal(0.0, control.WtYFamMean);
        Assert.Equal(0.0, control.WtYRoxMean);
        Assert.Equal(0.6, control.LqcMeanDrPrime!.Value, 12);
        Assert.Equal(CallNames.Positive, control.LqcCall);
        snapshot = await SnapshotAsync(gateway, runId, token);
        Assert.Equal(RunStateNames.ReadyForAnalysis, snapshot.DomainState);

        IReadOnlyList<SampleAnalysisDto> results = Value(await gateway.AnalyzeSamplesAsync(new AnalyzeSamplesCommand(
            Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token));
        SampleAnalysisDto sample = Assert.Single(results);
        Assert.Equal("SYN-040", sample.SampleId);
        Assert.Equal(StatusNames.Pass, sample.Status);
        Assert.Equal(CallNames.Positive, sample.Call);
        Assert.Equal(0.4, sample.MeanDrPrime);
        PerWellResultDto well = Assert.Single(sample.PerWell);
        Assert.Equal("A5", well.Well);
        Assert.Equal(0.4, well.YFam);
        Assert.Equal(0.0, well.YRox);
        Assert.Equal(0.4, well.DrPrime);
        Assert.Equal(CallNames.Positive, well.Call);
        Assert.NotEqual(Guid.Empty, Guid.Parse(well.TraceId));
        snapshot = await SnapshotAsync(gateway, runId, token);
        Assert.Equal(RunStateNames.Analyzed, snapshot.DomainState);
        Assert.Equal(StatusNames.Pass, snapshot.AnalysisSummary!.Status);
        Assert.Equal(1, snapshot.AnalysisSummary.PositiveCount);
        Assert.Equal(0, snapshot.AnalysisSummary.NegativeCount);
        Assert.Equal(0, snapshot.AnalysisSummary.InvalidCount);
        Assert.Empty(snapshot.BlockingIssues);
        PersistedTrace trace = Assert.Single((await repository.GetTracesAsync(runId, token)).Where(item => item.TraceId == well.TraceId));
        Assert.Equal(snapshot.MethodSnapshotHash, trace.MethodSnapshotHash);
        Assert.Equal(0.4, trace.ThresholdRaw);
        Assert.Equal(0.4, trace.ResultRaw);
        Assert.Equal(">=", trace.ComparisonOperator);
        Assert.Equal(2, trace.SourcePointIndex);
        Assert.Equal(460.0, trace.IntermediateValues.Single(item => item.Name == "F_FAM_120").Value);
        Assert.Equal(50.0, trace.IntermediateValues.Single(item => item.Name == "F_ROX_120").Value);
    }

    private static async Task<RunSnapshotDto> SnapshotAsync(ApplicationGateway gateway, Guid runId, CancellationToken token) =>
        Value(await gateway.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));

    private static T Value<T>(GatewayResult<T> result)
    {
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.UserMessage}")));
        return result.Value!;
    }

    private static string DomainErrors<T>(Result<T> result) => string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.UserMessage}"));

    private sealed class FileWorkspace : IDisposable
    {
        private readonly string repositoryRoot = FindRepositoryRoot();
        private readonly string instance = Guid.NewGuid().ToString("N");
        public string Root { get; }
        public string ManagedRoot { get; }

        public FileWorkspace()
        {
            Root = Path.Combine(repositoryRoot, "artifacts", "test-tmp", nameof(FluoAxis.Infrastructure.Tests),
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture), nameof(ManagedFileWorkflowTests), instance);
            ManagedRoot = Path.Combine(Root, "managed");
            Directory.CreateDirectory(Path.Combine(Root, "external"));
        }

        public string WriteExternal(string name, byte[] bytes)
        {
            string path = Path.Combine(Root, "external", name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public byte[] ReadManaged(RelativePath entry) => File.ReadAllBytes(Path.Combine(ManagedRoot, entry.Value.Replace('/', Path.DirectorySeparatorChar)));

        public string WriteBenchmarkReport(string json)
        {
            string relative = $"artifacts/benchmarks/{nameof(ManagedFileWorkflowTests)}/{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}/{instance}/measurement.json";
            string target = Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, json, new UTF8Encoding(false));
            return relative;
        }

        public void Dispose()
        {
            string approved = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "test-tmp")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(approved, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture cleanup target is outside the approved test root.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "FluoAxis.slnx"))) current = current.Parent;
            return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        }
    }
}
