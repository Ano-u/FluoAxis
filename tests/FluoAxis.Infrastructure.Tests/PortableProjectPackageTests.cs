namespace FluoAxis.Infrastructure.Tests;

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using FluoAxis.ProjectFormat;
using Microsoft.Data.Sqlite;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class PortableProjectPackageTests
{
    private static readonly DateTimeOffset IssuedUtc = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "package-test";

    [Fact]
    public async Task SelectedCompletedRunRoundTripsWithoutOtherProjectsOrRecalculation()
    {
        using var fixture = new PackageFixture();
        await using var source = new PersistentDesktopStore(fixture.SourceRoot);
        Guid selected = await CompleteRunAsync(source, fixture.SourceFile, "Selected research");
        ProjectSnapshotDto other = Value(await source.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "PRIVATE-OTHER-EXPERIMENT", Actor, IssuedUtc), TestContext.Current.CancellationToken));
        RunEvidenceDto before = Value(await source.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), selected), TestContext.Current.CancellationToken));

        ManagedProjectPackageDto package = Value(await source.CreateProjectPackageAsync(selected, TestContext.Current.CancellationToken));
        string packagePath = Path.Combine(fixture.SourceRoot, package.RelativeResourcePath);
        byte[] bytes = File.ReadAllBytes(packagePath);
        Assert.Equal(package.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(bytes.LongLength, package.FileSizeBytes);
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(package), StringComparison.OrdinalIgnoreCase);
        ProjectPackage opened = await OpenAsync(packagePath);
        Assert.Equal(2, opened.DeclaredEntries.Count);
        Assert.Equal(before.Snapshot.MethodSnapshotHash, Assert.Single(opened.Manifest.MethodSnapshotHashes));
        byte[] database = opened.ReadEntry(RelativePath.Parse("project.sqlite"));
        Assert.DoesNotContain("PRIVATE-OTHER-EXPERIMENT", Encoding.UTF8.GetString(database), StringComparison.Ordinal);
        Assert.DoesNotContain(other.ProjectId.ToString(), Encoding.UTF8.GetString(database), StringComparison.OrdinalIgnoreCase);

        await using var destination = new PersistentDesktopStore(fixture.DestinationRoot);
        ProjectSnapshotDto imported = Value(await destination.ImportProjectPackageAsync(packagePath, TestContext.Current.CancellationToken));
        Assert.Equal(selected, imported.RunId);
        Assert.Single(Value(await destination.ListProjectsAsync(TestContext.Current.CancellationToken)));
        RunEvidenceDto after = Value(await destination.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), selected), TestContext.Current.CancellationToken));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        SourceFileDto sourceIdentity = Assert.Single(after.Snapshot.SourceFiles);
        Assert.Equal(File.ReadAllBytes(fixture.SourceFile), File.ReadAllBytes(Path.Combine(fixture.DestinationRoot, sourceIdentity.RelativeResourcePath)));

        GatewayResult<ProjectSnapshotDto> duplicate = await destination.ImportProjectPackageAsync(packagePath, TestContext.Current.CancellationToken);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("PROJECT_PACKAGE_IDENTITY_CONFLICT", Assert.Single(duplicate.Errors).Code);
        Assert.Equal(JsonSerializer.Serialize(after), JsonSerializer.Serialize(Value(await destination.GetRunEvidenceAsync(
            new GetRunQuery(Guid.NewGuid(), selected), TestContext.Current.CancellationToken))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NegativeLqcMeanSurvivesReopenAndPackageWhileLegacyMissingValuesStayMissing(bool legacyMissingValues)
    {
        using var fixture = new PackageFixture();
        File.WriteAllText(fixture.SourceFile, ManagedFileWorkflowTests.CsvFixture.Replace(",640,", ",280,", StringComparison.Ordinal), new UTF8Encoding(false));
        Guid runId;
        string packagePath;
        RunEvidenceDto before;
        await using (var source = new PersistentDesktopStore(fixture.SourceRoot))
        {
            runId = await CompleteRunAsync(source, fixture.SourceFile, "Negative LQC", completeSamples: false);
            before = Value(await source.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
            Assert.Equal(RunStateNames.RunInvalid, before.Snapshot.DomainState);
            Assert.Equal(StatusNames.Fail, before.Control!.Status);
            Assert.Equal(CallNames.Negative, before.Control.LqcCall);
            Assert.Equal(0.2, before.Control.LqcMeanDrPrime!.Value, 12);
            Assert.Null(before.Control.WtYFamMean);
            Assert.Null(before.Control.WtYRoxMean);
            Assert.Empty(before.Samples);
            ManagedProjectPackageDto package = Value(await source.CreateProjectPackageAsync(runId, TestContext.Current.CancellationToken));
            packagePath = Path.Combine(fixture.SourceRoot, package.RelativeResourcePath);
        }
        await using (var reopened = new PersistentDesktopStore(fixture.SourceRoot))
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(Value(await reopened.GetRunEvidenceAsync(
                new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken))));
        if (legacyMissingValues)
            packagePath = await fixture.RewriteDatabaseAsync(await OpenAsync(packagePath),
                "UPDATE control_result SET lqc_call=NULL,lqc_mean_dr_prime=NULL;");
        await using var destination = new PersistentDesktopStore(fixture.DestinationRoot);
        Value(await destination.ImportProjectPackageAsync(packagePath, TestContext.Current.CancellationToken));
        RunEvidenceDto imported = Value(await destination.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
        Assert.Equal(StatusNames.Fail, imported.Control!.Status);
        Assert.Equal(RunStateNames.RunInvalid, imported.Snapshot.DomainState);
        Assert.Equal(legacyMissingValues ? null : CallNames.Negative, imported.Control.LqcCall);
        Assert.Equal(legacyMissingValues ? null : before.Control!.LqcMeanDrPrime, imported.Control.LqcMeanDrPrime);
        Assert.Equal(imported.Control.LqcMeanDrPrime, imported.Snapshot.ControlSummary!.LqcMeanDrPrime);
    }

    [Fact]
    public async Task EmptyNewRunCanBePackagedWithoutInventingASource()
    {
        using var fixture = new PackageFixture();
        await using var source = new PersistentDesktopStore(fixture.SourceRoot);
        ProjectSnapshotDto project = Value(await source.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Empty research", Actor, IssuedUtc), TestContext.Current.CancellationToken));
        ManagedProjectPackageDto package = Value(await source.CreateProjectPackageAsync(project.RunId, TestContext.Current.CancellationToken));
        string path = Path.Combine(fixture.SourceRoot, package.RelativeResourcePath);
        Assert.Single((await OpenAsync(path)).DeclaredEntries);
        await using var destination = new PersistentDesktopStore(fixture.DestinationRoot);
        Assert.Equal(project, Value(await destination.ImportProjectPackageAsync(path, TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("CREATE TRIGGER hostile AFTER INSERT ON project BEGIN DELETE FROM project; END;")]
    [InlineData("PRAGMA user_version=999;")]
    [InlineData("UPDATE method_snapshot SET method_version='changed-without-hash';")]
    [InlineData("DELETE FROM run_calibration; DELETE FROM calibration_well_result; DELETE FROM control_result; DELETE FROM control_well_result; DELETE FROM sample; DELETE FROM sample_well_result;")]
    [InlineData("UPDATE sample_well_result SET call='negative';")]
    [InlineData("UPDATE signal_point SET rfu=rfu+1 WHERE well_row=0 AND well_column=5;")]
    [InlineData("UPDATE scientific_point SET normalized=0.025 WHERE normalized IS NOT NULL;")]
    [InlineData("UPDATE project SET project_name=char(67)||':'||char(92)||'private'||char(92)||'experiment';")]
    public async Task UntrustedDatabaseSchemaOrMethodIsRejectedWithoutDestinationMutation(string sql)
    {
        using var fixture = new PackageFixture();
        await using var source = new PersistentDesktopStore(fixture.SourceRoot);
        Guid runId = await CompleteRunAsync(source, fixture.SourceFile, "Research");
        ManagedProjectPackageDto identity = Value(await source.CreateProjectPackageAsync(runId, TestContext.Current.CancellationToken));
        ProjectPackage original = await OpenAsync(Path.Combine(fixture.SourceRoot, identity.RelativeResourcePath));
        string malicious = await fixture.RewriteDatabaseAsync(original, sql);
        await using var destination = new PersistentDesktopStore(fixture.DestinationRoot);

        GatewayResult<ProjectSnapshotDto> rejected = await destination.ImportProjectPackageAsync(malicious, TestContext.Current.CancellationToken);

        Assert.False(rejected.IsSuccess);
        Assert.Empty(Value(await destination.ListProjectsAsync(TestContext.Current.CancellationToken)));
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(rejected), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(fixture.DestinationRoot, "data", "raw")));
    }

    [Fact]
    public async Task SourceBytesCannotBeChangedByUpdatingOnlyTheArchiveManifest()
    {
        using var fixture = new PackageFixture();
        await using var source = new PersistentDesktopStore(fixture.SourceRoot);
        Guid runId = await CompleteRunAsync(source, fixture.SourceFile, "Research");
        ManagedProjectPackageDto identity = Value(await source.CreateProjectPackageAsync(runId, TestContext.Current.CancellationToken));
        ProjectPackage original = await OpenAsync(Path.Combine(fixture.SourceRoot, identity.RelativeResourcePath));
        ManifestEntry raw = Assert.Single(original.EntriesOfType(ProjectEntryType.SourceRaw));
        string changed = await fixture.RewriteAsync(original, raw.RelativePath, "different raw experiment"u8.ToArray());
        await using var destination = new PersistentDesktopStore(fixture.DestinationRoot);

        GatewayResult<ProjectSnapshotDto> rejected = await destination.ImportProjectPackageAsync(changed, TestContext.Current.CancellationToken);

        Assert.False(rejected.IsSuccess);
        Assert.Empty(Value(await destination.ListProjectsAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task LegacyCompletedRunWithoutTraceRowsKeepsUnavailableProvenance()
    {
        using var fixture = new PackageFixture();
        await using var source = new PersistentDesktopStore(fixture.SourceRoot);
        Guid runId = await CompleteRunAsync(source, fixture.SourceFile, "Legacy research");
        ManagedProjectPackageDto identity = Value(await source.CreateProjectPackageAsync(runId, TestContext.Current.CancellationToken));
        ProjectPackage original = await OpenAsync(Path.Combine(fixture.SourceRoot, identity.RelativeResourcePath));
        // Simulate an older successful Run that predates all additive science provenance tables.
        string legacy = await fixture.RewriteDatabaseAsync(original, "DELETE FROM scientific_point; DELETE FROM scientific_stage; DELETE FROM trace_provenance; DELETE FROM calculation_trace_value; DELETE FROM calculation_trace;");
        await using var destination = new PersistentDesktopStore(fixture.DestinationRoot);

        ProjectSnapshotDto imported = Value(await destination.ImportProjectPackageAsync(legacy, TestContext.Current.CancellationToken));
        RunEvidenceDto evidence = Value(await destination.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), imported.RunId), TestContext.Current.CancellationToken));

        Assert.Equal(RunStateNames.Analyzed, evidence.Snapshot.DomainState);
        Assert.Single(evidence.Samples);
        Assert.Empty(evidence.Traces);
        Assert.Equal(ProvenanceCompletenessNames.Unavailable, evidence.ProvenanceCompleteness);
    }

    [Fact]
    public async Task ControlOnlyCompletedRunRemainsValidWithoutInventedSamples()
    {
        using var fixture = new PackageFixture();
        await using var source = new PersistentDesktopStore(fixture.SourceRoot);
        Guid runId = await CompleteRunAsync(source, fixture.SourceFile, "Control-only research");
        ManagedProjectPackageDto identity = Value(await source.CreateProjectPackageAsync(runId, TestContext.Current.CancellationToken));
        ProjectPackage original = await OpenAsync(Path.Combine(fixture.SourceRoot, identity.RelativeResourcePath));
        string controlsOnly = await fixture.RewriteDatabaseAsync(original, """
            DELETE FROM scientific_point; DELETE FROM scientific_stage; DELETE FROM trace_provenance;
            DELETE FROM sample_well_result; DELETE FROM sample;
            UPDATE well_assignment SET role='Ignore',sample_id=NULL,replicate_index=NULL WHERE role='Sample';
            DELETE FROM calculation_trace_value WHERE trace_id IN (SELECT trace_id FROM calculation_trace WHERE well_row=0 AND well_column=5);
            DELETE FROM calculation_trace WHERE well_row=0 AND well_column=5;
            """);
        await using var destination = new PersistentDesktopStore(fixture.DestinationRoot);

        ProjectSnapshotDto imported = Value(await destination.ImportProjectPackageAsync(controlsOnly, TestContext.Current.CancellationToken));
        RunEvidenceDto evidence = Value(await destination.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), imported.RunId), TestContext.Current.CancellationToken));

        Assert.Equal(RunStateNames.Analyzed, evidence.Snapshot.DomainState);
        Assert.Empty(evidence.Samples);
        Assert.Equal(StatusNames.Pass, evidence.Control!.Status);
    }

    [Fact]
    public async Task CanceledTransferDoesNotCreateProjectOrPublishedPackage()
    {
        using var fixture = new PackageFixture();
        await using var store = new PersistentDesktopStore(fixture.SourceRoot);
        await store.Ready;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateProjectPackageAsync(Guid.NewGuid(), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportProjectPackageAsync(fixture.SourceFile, cancelled.Token));
        Assert.Empty(Value(await store.ListProjectsAsync(TestContext.Current.CancellationToken)));
        Assert.False(Directory.Exists(Path.Combine(fixture.SourceRoot, "packages")));
    }

    [Fact]
    public async Task TraversalArchiveIsRejectedBeforeAnyEntryIsMaterialized()
    {
        using var fixture = new PackageFixture();
        string path = Path.Combine(fixture.Root, "traversal.fluoaxisproj");
        await using (FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry("../outside.txt").Open();
            entry.Write("outside"u8);
        }
        await using var destination = new PersistentDesktopStore(fixture.DestinationRoot);

        GatewayResult<ProjectSnapshotDto> rejected = await destination.ImportProjectPackageAsync(path, TestContext.Current.CancellationToken);

        Assert.False(rejected.IsSuccess);
        Assert.Empty(Value(await destination.ListProjectsAsync(TestContext.Current.CancellationToken)));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "outside.txt")));
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(rejected), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportRefusesSourceTamperingAndDoesNotPublishAPackage()
    {
        using var fixture = new PackageFixture();
        await using var source = new PersistentDesktopStore(fixture.SourceRoot);
        Guid runId = await CompleteRunAsync(source, fixture.SourceFile, "Research");
        RunEvidenceDto evidence = Value(await source.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
        SourceFileDto raw = Assert.Single(evidence.Snapshot.SourceFiles);
        File.WriteAllText(Path.Combine(fixture.SourceRoot, raw.RelativeResourcePath), "changed source", new UTF8Encoding(false));

        GatewayResult<ManagedProjectPackageDto> rejected = await source.CreateProjectPackageAsync(runId, TestContext.Current.CancellationToken);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("PROJECT_PACKAGE_SOURCE_MISMATCH", Assert.Single(rejected.Errors).Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.SourceRoot, "packages")));
        Assert.Single(Value(await source.ListProjectsAsync(TestContext.Current.CancellationToken)));
    }

    private static async Task<Guid> CompleteRunAsync(PersistentDesktopStore store, string sourceFile, string name, bool completeSamples = true)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        SourceFileDto source = Value(await store.StageAsync(sourceFile, token));
        ProjectSnapshotDto project = Value(await store.CreateProjectAsync(new CreateProjectCommand(Guid.NewGuid(), name, Actor, IssuedUtc), token));
        Guid runId = project.RunId;
        var fam = new ChannelRegionMappingDto(ChannelNames.Fam, DelimitedTextImporter.BodySheetName, 0, 0,
            [new(1, "A1"), new(2, "A2"), new(3, "A3"), new(4, "A4"), new(5, "A5")]);
        var rox = new ChannelRegionMappingDto(ChannelNames.Rox, DelimitedTextImporter.BodySheetName, 0, 0,
            [new(6, "A1"), new(7, "A2"), new(8, "A3"), new(9, "A4"), new(10, "A5")]);
        RunSnapshotDto snapshot = Value(await store.ConfirmMappingAsync(new ConfirmMappingCommand(Guid.NewGuid(), runId, 0, Actor,
            IssuedUtc, fam, rox, source.OriginalFileName, source.RelativeResourcePath, source.Sha256, source.FileSizeBytes, SourceFileDto.MediaTypeNames.Csv), token));
        snapshot = Value(await store.AssignWellsAsync(new AssignWellsCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc,
            [new("A1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100), new("A3", WellRoleNames.WildType),
             new("A4", WellRoleNames.Lqc01Percent), new("A5", WellRoleNames.Sample, "S1", replicateIndex: 1)]), token));
        Value(await store.RunCalibrationAsync(new RunCalibrationCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token));
        snapshot = Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));
        Value(await store.RunControlQcAsync(new RunControlQcCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token));
        if (!completeSamples) return runId;
        snapshot = Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));
        Value(await store.AnalyzeSamplesAsync(new AnalyzeSamplesCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, IssuedUtc), token));
        return runId;
    }

    private static T Value<T>(GatewayResult<T> result)
    {
        Assert.True(result.IsSuccess, string.Join(';', result.Errors.Select(error => error.Code)));
        return result.Value!;
    }

    private static async Task<ProjectPackage> OpenAsync(string path)
    {
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Result<ProjectPackage> opened = await FluoAxisProjectArchive.OpenAsync(input, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(opened.IsSuccess);
        return opened.Value!;
    }

    private sealed class PackageFixture : IDisposable
    {
        private readonly string approved;
        public string Root { get; }
        public string SourceRoot => Path.Combine(Root, "source");
        public string DestinationRoot => Path.Combine(Root, "destination");
        public string SourceFile { get; }

        public PackageFixture()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
            if (repository is null) throw new InvalidOperationException("Repository not found.");
            approved = Path.Combine(repository.FullName, "artifacts", "test-tmp");
            Root = Path.Combine(approved, nameof(PortableProjectPackageTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SourceFile = Path.Combine(Root, "synthetic.csv");
            File.WriteAllText(SourceFile, ManagedFileWorkflowTests.CsvFixture, new UTF8Encoding(false));
        }

        public async Task<string> RewriteDatabaseAsync(ProjectPackage original, string sql)
        {
            string database = Path.Combine(Root, $"tampered-{Guid.NewGuid():N}.sqlite");
            await File.WriteAllBytesAsync(database, original.ReadEntry(RelativePath.Parse("project.sqlite")), TestContext.Current.CancellationToken);
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            return await RewriteAsync(original, RelativePath.Parse("project.sqlite"), await File.ReadAllBytesAsync(database, TestContext.Current.CancellationToken));
        }

        public async Task<string> RewriteAsync(ProjectPackage original, RelativePath changed, byte[] bytes)
        {
            ProjectManifest manifest = original.Manifest;
            var contents = new ProjectPackageContents
            {
                ProjectId = manifest.ProjectId, DisplayName = manifest.DisplayName,
                CreatedAtUtc = manifest.CreatedAtUtc, ModifiedAtUtc = manifest.ModifiedAtUtc,
                MethodVersions = manifest.MethodVersions, MethodSnapshotHashes = manifest.MethodSnapshotHashes,
                Files = [.. manifest.Entries.Select(entry => new ProjectPackageContentFile
                {
                    Path = entry.RelativePath, EntryType = entry.EntryType,
                    Content = entry.RelativePath == changed ? bytes : original.ReadEntry(entry.RelativePath),
                })],
            };
            string path = Path.Combine(Root, $"tampered-{Guid.NewGuid():N}.fluoaxisproj");
            await using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            Assert.True((await FluoAxisProjectArchive.SaveAsync(output, contents, TestContext.Current.CancellationToken)).IsSuccess);
            return path;
        }

        public void Dispose()
        {
            string path = Path.GetFullPath(Root);
            if (!path.StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing cleanup outside approved test artifacts.");
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}
