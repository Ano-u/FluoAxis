namespace FluoAxis.Infrastructure.Tests;

using System.Globalization;
using System.Text;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Infrastructure;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class ScientificEvidenceRoundTripTests
{
    private static readonly DateTimeOffset Issued = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Actual_source_coordinates_qc_points_and_input_identity_survive_reopen_and_package()
    {
        using var fixture = new Fixture();
        RunEvidenceDto before;
        ScientificPointPageDto ntcPoints;
        string packagePath;
        await using (var store = new PersistentDesktopStore(fixture.Managed))
        {
            Guid runId = await CompleteAsync(store, fixture.Source);
            before = Value(await store.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
            Assert.Equal(ProvenanceCompletenessNames.StoredComplete, before.ProvenanceCompleteness);
            Assert.Equal(3, before.ScientificEvidence!.Stages.Count);
            Assert.Equal(Issued, Assert.Single(before.Snapshot.SourceFiles).ImportedAtUtc);
            Assert.Single(before.ScientificEvidence.Stages.Select(stage => stage.Identity.InputSnapshotSha256).Distinct());
            Assert.All(before.ScientificEvidence.Stages, stage =>
            {
                Assert.Equal(before.Snapshot.MethodSnapshotHash, stage.Identity.MethodSnapshotHash);
                Assert.Equal(Assert.Single(before.Snapshot.SourceFiles).Sha256, stage.Identity.SourceSha256);
            });
            AnalysisStageEvidenceDto calibration = Assert.Single(before.ScientificEvidence.Stages, stage => stage.Stage == "calibration");
            WellScientificEvidenceDto positive = Assert.Single(calibration.Wells, well => well.Role == WellRoleNames.Positive100 && well.Channel == ChannelNames.Fam);
            Assert.Equal(1000, Assert.Single(positive.Values, value => value.Name == "meanRfu").Value);
            Assert.Equal(0, Assert.Single(positive.Values, value => value.Name == "relativeRange").Value);
            Assert.Equal(2, positive.Window!.PointCount); Assert.Equal(1, positive.Window.FirstPointIndex);
            Assert.Equal(3, positive.Endpoint.SourceRowIndex); Assert.Equal(2, positive.Endpoint.SourceColumnIndex);
            Assert.Equal("1.1", before.Snapshot.MethodVersion);
            Assert.Equal("plateauMean", positive.FmSource);
            WellScientificEvidenceDto rox = Assert.Single(calibration.Wells, well => well.Role == WellRoleNames.Positive100 && well.Channel == ChannelNames.Rox);
            Assert.Equal("endpoint", rox.FmSource);
            Assert.Null(rox.Window);
            Assert.Equal(rox.Endpoint.RawRfu, Assert.Single(rox.Values, value => value.Name == "Fm").Value);
            Assert.DoesNotContain(rox.Values, value => value.Name is "meanRfu" or "relativeRange" or "minimumWindowPoints");
            Assert.Equal("BRAF-ENGINE-1.1", calibration.Identity.Algorithm);
            AnalysisStageEvidenceDto control = Assert.Single(before.ScientificEvidence.Stages, stage => stage.Stage == "control");
            Assert.Equal(2, control.Wells.Count(well => well.Role == WellRoleNames.WildType));
            Assert.All(before.Traces, trace =>
            {
                Assert.Equal(2, trace.Provenance!.SourcePoints.Count);
                Assert.All(trace.Provenance.SourcePoints, point => Assert.Equal(3, point.SourceRowIndex));
                Assert.Equal(calibration.Identity.InputSnapshotSha256, trace.Provenance.Identity.InputSnapshotSha256);
            });
            ntcPoints = Value(await store.GetScientificPointPageAsync(runId, "calibration", "A1", ChannelNames.Fam, 0, 2, TestContext.Current.CancellationToken));
            Assert.Equal(3, ntcPoints.TotalPoints); Assert.Equal(2, ntcPoints.Points.Count);
            Assert.All(ntcPoints.Points, point => { Assert.Equal(0, point.Normalized); Assert.Equal(100, point.RawRfu); });
            Assert.Equal([1, 2], ntcPoints.Points.Select(point => point.SourceRowIndex));
            Assert.False((await store.GetScientificPointPageAsync(runId, "calibration", "A1", ChannelNames.Fam, 0, 251, TestContext.Current.CancellationToken)).IsSuccess);
            ManagedProjectPackageDto package = Value(await store.CreateProjectPackageAsync(runId, TestContext.Current.CancellationToken));
            packagePath = Path.Combine(fixture.Managed, package.RelativeResourcePath);
        }
        await using (var reopened = new PersistentDesktopStore(fixture.Managed))
        {
            RunEvidenceDto after = Value(await reopened.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), before.Snapshot.RunId), TestContext.Current.CancellationToken));
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        }
        await using var destination = new PersistentDesktopStore(fixture.Other);
        Value(await destination.ImportProjectPackageAsync(packagePath, TestContext.Current.CancellationToken));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(Value(await destination.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), before.Snapshot.RunId), TestContext.Current.CancellationToken))));
        Assert.Equal(JsonSerializer.Serialize(ntcPoints), JsonSerializer.Serialize(Value(await destination.GetScientificPointPageAsync(before.Snapshot.RunId, "calibration", "A1", ChannelNames.Fam, 0, 2, TestContext.Current.CancellationToken))));
        fixture.WriteUiEvidence(packagePath, before.Snapshot.RunId);
    }

    private static async Task<Guid> CompleteAsync(PersistentDesktopStore store, string source)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        SourceFileDto staged = Value(await store.StageAsync(source, token));
        Guid runId = Value(await store.CreateProjectAsync(new CreateProjectCommand(Guid.NewGuid(), "Full evidence", "research", Issued), token)).RunId;
        RunSnapshotDto snapshot = Value(await store.ConfirmMappingAsync(new ConfirmMappingCommand(Guid.NewGuid(), runId, 0, "research", Issued,
            new ChannelRegionMappingDto(ChannelNames.Fam, "text-body", 0, 0, [new(1, "A1"), new(2, "A2"), new(3, "A3"), new(4, "A4"), new(5, "A5")], 3),
            new ChannelRegionMappingDto(ChannelNames.Rox, "text-body", 0, 0, [new(6, "A1"), new(7, "A2"), new(8, "A3"), new(9, "A4"), new(10, "A5")], 3),
            staged.OriginalFileName, staged.RelativeResourcePath, staged.Sha256, staged.FileSizeBytes, SourceFileDto.MediaTypeNames.Csv,
            options: new ImportOptionsDto()), token));
        snapshot = Value(await store.AssignWellsAsync(new AssignWellsCommand(Guid.NewGuid(), runId, snapshot.Revision, "research", Issued,
            [new("A1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100), new("A3", WellRoleNames.WildType), new("A4", WellRoleNames.Lqc01Percent), new("A5", WellRoleNames.Sample, "Sample-1", replicateIndex: 1)]), token));
        Assert.Equal(StatusNames.Pass, Value(await store.RunCalibrationAsync(new RunCalibrationCommand(Guid.NewGuid(), runId, snapshot.Revision, "research", Issued.AddMinutes(1)), token)).Status);
        snapshot = Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));
        Assert.Equal(StatusNames.Pass, Value(await store.RunControlQcAsync(new RunControlQcCommand(Guid.NewGuid(), runId, snapshot.Revision, "research", Issued.AddMinutes(2)), token)).Status);
        snapshot = Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));
        Value(await store.AnalyzeSamplesAsync(new AnalyzeSamplesCommand(Guid.NewGuid(), runId, snapshot.Revision, "research", Issued.AddMinutes(3)), token));
        return runId;
    }

    private static T Value<T>(GatewayResult<T> result) { Assert.True(result.IsSuccess, string.Join(';', result.Errors.Select(error => error.Code))); return result.Value!; }

    private sealed class Fixture : IDisposable
    {
        private readonly string approved;
        private readonly string repositoryRoot;
        internal string Root { get; }
        internal string Managed => Path.Combine(Root, "managed");
        internal string Other => Path.Combine(Root, "other");
        internal string Source => Path.Combine(Root, "source.csv");
        internal Fixture()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
            repositoryRoot = repository?.FullName ?? throw new InvalidOperationException("Repository root not found.");
            approved = Path.Combine(repositoryRoot, "artifacts", "test-tmp");
            Root = Path.Combine(approved, nameof(ScientificEvidenceRoundTripTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root); File.WriteAllText(Source, ManagedFileWorkflowTests.CsvFixture, new UTF8Encoding(false));
        }
        internal void WriteUiEvidence(string packagePath, Guid runId)
        {
            string relative = $"artifacts/ui-evidence/{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}/{Guid.NewGuid():N}";
            string destination = Path.Combine(repositoryRoot, relative);
            Directory.CreateDirectory(destination);
            File.Copy(Source, Path.Combine(destination, "source.csv"), overwrite: false);
            File.Copy(packagePath, Path.Combine(destination, "synthetic-completed.fluoaxisproj"), overwrite: false);
            File.WriteAllText(Path.Combine(destination, "fixture.json"), JsonSerializer.Serialize(new
            { kind = "synthetic-engineering-fixture", runId, source = "source.csv", package = "synthetic-completed.fluoaxisproj", approved = false }), new UTF8Encoding(false));
            TestContext.Current.TestOutputHelper?.WriteLine($"Synthetic native UI fixture: {relative}");
        }
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe cleanup target.");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
