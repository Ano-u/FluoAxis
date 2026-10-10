namespace FluoAxis.Infrastructure.Tests;

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class FullPlateEngineeringAcceptanceTests
{
    [Fact]
    public async Task Full_plate_raw_to_complete_evidence_reports_package_and_reopen_matches_known_calls()
    {
        using var fixture = new Fixture();
        CancellationToken token = TestContext.Current.CancellationToken;
        string[] wells = [.. Enumerable.Range(0, 96).Select(index => $"{(char)('A' + index / 12)}{index % 12 + 1}")];
        using (var writer = new StreamWriter(fixture.Source, false, new UTF8Encoding(false)))
        {
            await writer.WriteLineAsync("Time," + string.Join(',', wells.Select(well => $"{well}_FAM")) + "," + string.Join(',', wells.Select(well => $"{well}_ROX")));
            for (int minute = 0; minute <= 120; minute++)
            {
                string[] fam = [.. Enumerable.Range(0, 96).Select(index => index is 2 or 3 ? "1000" : index is 6 or 7 ? "640" : index < 8 ? "100" : ((index - 8) / 2) % 2 == 0 ? "640" : "280")];
                string[] rox = [.. Enumerable.Range(0, 96).Select(index => index is 2 or 3 ? "500" : "50")];
                await writer.WriteLineAsync(minute.ToString(CultureInfo.InvariantCulture) + "," + string.Join(',', fam) + "," + string.Join(',', rox));
            }
        }
        var phases = new Dictionary<string, double>(StringComparer.Ordinal);
        var timer = Stopwatch.StartNew();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        Guid runId;
        string inputHash;
        string packageFile;
        RunEvidenceDto original;
        await using (var store = new PersistentDesktopStore(fixture.Managed))
        {
            await store.Ready;
            phases["startupMs"] = timer.Elapsed.TotalMilliseconds;
            var project = Value(await store.CreateProjectAsync(new(Guid.NewGuid(), "Synthetic 96-well engineering plate", "acceptance", DateTimeOffset.UtcNow), token));
            runId = project.RunId;
            timer.Restart();
            SourceFileDto source = Value(await store.StageAsync(fixture.Source, token));
            var options = new ImportOptionsDto();
            Value(await store.InspectImportAsync(new(Guid.NewGuid(), runId, source.OriginalFileName, source.FileSizeBytes, source.RelativeResourcePath, options: options), null, token));
            RunSnapshotDto run = Value(await store.ConfirmMappingAsync(new(Guid.NewGuid(), runId, 0, "acceptance", DateTimeOffset.UtcNow,
                new(ChannelNames.Fam, DelimitedTextImporter.BodySheetName, 0, 0, wells.Select((well, index) => new WellColumnMappingDto(index + 1, well)).ToArray()),
                new(ChannelNames.Rox, DelimitedTextImporter.BodySheetName, 0, 0, wells.Select((well, index) => new WellColumnMappingDto(index + 97, well)).ToArray()),
                source.OriginalFileName, source.RelativeResourcePath, source.Sha256, source.FileSizeBytes, SourceFileDto.MediaTypeNames.Csv, options: options), token));
            phases["stageInspectMapMs"] = timer.Elapsed.TotalMilliseconds;
            var assignments = wells.Select((well, index) => index < 8
                ? new WellAssignmentDto(well, index < 2 ? WellRoleNames.Ntc : index < 4 ? WellRoleNames.Positive100 : index < 6 ? WellRoleNames.WildType : WellRoleNames.Lqc01Percent)
                : new WellAssignmentDto(well, WellRoleNames.Sample, $"S-{(index - 8) / 2:D2}", replicateIndex: (index - 8) % 2 + 1)).ToArray();
            run = Value(await store.AssignWellsAsync(new(Guid.NewGuid(), runId, run.Revision, "acceptance", DateTimeOffset.UtcNow, assignments), token));
            timer.Restart();
            Assert.Equal(StatusNames.Pass, Value(await store.RunCalibrationAsync(new(Guid.NewGuid(), runId, run.Revision, "acceptance", DateTimeOffset.UtcNow), token)).Status);
            run = Value(await store.GetRunAsync(new(Guid.NewGuid(), runId), token));
            Assert.Equal(StatusNames.Pass, Value(await store.RunControlQcAsync(new(Guid.NewGuid(), runId, run.Revision, "acceptance", DateTimeOffset.UtcNow), token)).Status);
            run = Value(await store.GetRunAsync(new(Guid.NewGuid(), runId), token));
            IReadOnlyList<SampleAnalysisDto> samples = Value(await store.AnalyzeSamplesAsync(new(Guid.NewGuid(), runId, run.Revision, "acceptance", DateTimeOffset.UtcNow), token));
            phases["allScientificStagesMs"] = timer.Elapsed.TotalMilliseconds;
            Assert.Equal(44, samples.Count);
            Assert.Equal(22, samples.Count(sample => sample.Call == CallNames.Positive));
            Assert.Equal(22, samples.Count(sample => sample.Call == CallNames.Negative));
            Assert.All(samples, sample => Assert.Equal(StatusNames.Pass, sample.Status));
            original = Value(await store.GetRunEvidenceAsync(new(Guid.NewGuid(), runId), token));
            Assert.Equal(ProvenanceCompletenessNames.StoredComplete, original.ProvenanceCompleteness);
            Assert.Equal(3, original.ScientificEvidence!.Stages.Count);
            inputHash = original.ScientificEvidence.Stages[0].Identity.InputSnapshotSha256;
            Assert.All(original.ScientificEvidence.Stages, stage => Assert.Equal(inputHash, stage.Identity.InputSnapshotSha256));
            timer.Restart();
            ExplorerPairPageDto page = Value(await store.GetExplorerPairPageAsync(runId, "H12", 0, 25000, token));
            phases["pairedRawPageMs"] = timer.Elapsed.TotalMilliseconds;
            Assert.Equal(121, page.Points.Count);
            timer.Restart();
            ReportSnapshotDto snapshot = Value(await store.CreateReportSnapshotAsync(runId, token));
            Value(await store.CreateReportExportAsync(snapshot, "html", token));
            Value(await store.CreateReportExportAsync(snapshot, "xlsx", token));
            phases["snapshotHtmlXlsxMs"] = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            ManagedProjectPackageDto package = Value(await store.CreateProjectPackageAsync(runId, token));
            packageFile = Path.Combine(fixture.Managed, package.RelativeResourcePath);
            phases["packageMs"] = timer.Elapsed.TotalMilliseconds;
        }
        timer.Restart();
        await using (var destination = new PersistentDesktopStore(fixture.Other))
        {
            Value(await destination.ImportProjectPackageAsync(packageFile, token));
            RunEvidenceDto restored = Value(await destination.GetRunEvidenceAsync(new(Guid.NewGuid(), runId), token));
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(restored));
        }
        phases["importReopenMs"] = timer.Elapsed.TotalMilliseconds;
        byte[] bytes = File.ReadAllBytes(fixture.Source);
        fixture.Measure(new
        {
            scenario = "synthetic-96-wells-121-times-two-channels", iterations = 1, concurrency = 1,
            points = 96 * 121 * 2, samples = 44, positive = 22, negative = 22,
            inputSnapshotSha256 = inputHash, sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), phases,
            processWideAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore,
            allocationScope = "process-wide; may include concurrent tests", runtime = Environment.Version.ToString(),
            os = Environment.OSVersion.VersionString, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            logicalProcessors = Environment.ProcessorCount, referenceHardwareQualification = false, slaAsserted = false,
        });
    }

    private static T Value<T>(GatewayResult<T> result)
    {
        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(error => error.Code)));
        return result.Value!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string repository;
        private readonly string approved;
        private readonly string identity = Guid.NewGuid().ToString("N");
        private readonly string root;
        internal string Managed => Path.Combine(root, "managed");
        internal string Other => Path.Combine(root, "other");
        internal string Source => Path.Combine(root, "synthetic-plate.csv");
        internal Fixture()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluoAxis.slnx"))) directory = directory.Parent;
            repository = directory!.FullName;
            approved = Path.Combine(repository, "artifacts", "test-tmp");
            root = Path.Combine(approved, nameof(FullPlateEngineeringAcceptanceTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), identity);
            Directory.CreateDirectory(root);
        }
        internal void Measure(object measurement)
        {
            string target = Path.Combine(repository, "artifacts", "benchmarks", nameof(FullPlateEngineeringAcceptanceTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), identity);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "measurement.json"), JsonSerializer.Serialize(measurement));
            TestContext.Current.TestOutputHelper?.WriteLine(Path.GetRelativePath(repository, Path.Combine(target, "measurement.json")));
        }
        public void Dispose()
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup.");
            Directory.Delete(root, true);
        }
    }
}
