namespace FluoAxis.Infrastructure.Tests;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class PersistentDesktopStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "persistence-fixture";

    [Fact]
    public async Task CompletedRunReopensWithIdenticalTracesFromRelocatedCopy()
    {
        using var fixture = new WorkspaceFixture();
        RunEvidenceDto before;
        SourceFileDto staged;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();
        await using (var store = new PersistentDesktopStore(fixture.ManagedRoot))
        {
            await store.Ready;
            Assert.Empty(Value(await store.ListProjectsAsync(TestContext.Current.CancellationToken)));
            staged = Value(await store.StageAsync(fixture.Source, TestContext.Current.CancellationToken));
            ProjectSnapshotDto project = Value(await store.CreateProjectAsync(
                new CreateProjectCommand(Guid.NewGuid(), "Persisted synthetic run", Actor, Now), TestContext.Current.CancellationToken));
            await CompleteRunAsync(store, project.RunId, staged);
            before = Value(await store.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), project.RunId), TestContext.Current.CancellationToken));
            Assert.Equal(RunStateNames.Analyzed, before.Snapshot.DomainState);
            Assert.Equal(0.4, Assert.Single(before.Samples).MeanDrPrime);
            Assert.Equal(2, before.Traces.Count); // Stored LQC and sample traces.
            Assert.Equal(ProvenanceCompletenessNames.StoredComplete, before.ProvenanceCompleteness);
        }
        // Relocation reads each file exclusively after shutdown and reopens the copy.
        fixture.CopyClosedWorkspaceToNewRoot();
        await using (var reopened = new PersistentDesktopStore(fixture.ManagedRoot))
        {
            await reopened.Ready;
            ProjectSnapshotDto listed = Assert.Single(Value(await reopened.ListProjectsAsync(TestContext.Current.CancellationToken)));
            Assert.Equal(before.Snapshot.RunId, listed.RunId);
            Assert.Equal(before.Snapshot.Revision, listed.RunRevision);
            RunEvidenceDto after = Value(await reopened.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), listed.RunId), TestContext.Current.CancellationToken));
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
            Assert.Equal(Encoding.UTF8.GetBytes(ManagedFileWorkflowTests.CsvFixture),
                File.ReadAllBytes(Path.Combine(fixture.ManagedRoot, staged.RelativeResourcePath)));
            string serialized = JsonSerializer.Serialize(after);
            Assert.DoesNotContain(fixture.Root, serialized, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0.4, Assert.Single(after.Traces, trace => trace.Well == "A5").ThresholdRaw);
        }
        elapsed.Stop();
        fixture.WriteMeasurement(JsonSerializer.Serialize(new
        {
            scenario = "persistent-import-analysis-close-copy-reopen", iterations = 1, concurrency = 1,
            elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
            allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
            allocationScope = "process-wide; includes fixture, assertions and concurrent tests",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            logicalProcessors = Environment.ProcessorCount, wells = 5, channels = 2, timePoints = 3,
            fixtureSha256 = "d927f3ff1f8c14acad1ed79c005d2f9fb4bc7ccb71f930d72bc22af86bb36153",
            baseline = "no earlier persistent desktop flow", slaAsserted = false,
        }));
        TestContext.Current.TestOutputHelper?.WriteLine($"Persistent import/analyze/close/copy/reopen: {elapsed.Elapsed.TotalMilliseconds:F2} ms; .NET {Environment.Version}; logical CPUs {Environment.ProcessorCount}; fixture d927f3ff1f8c14acad1ed79c005d2f9fb4bc7ccb71f930d72bc22af86bb36153; no SLA.");
    }

    [Fact]
    public async Task ImportedRunReopensWithEditableMappingAndOriginalManagedSource()
    {
        using var fixture = new WorkspaceFixture();
        Guid runId;
        await using (var first = new PersistentDesktopStore(fixture.ManagedRoot))
        {
            SourceFileDto source = Value(await first.StageAsync(fixture.Source, TestContext.Current.CancellationToken));
            runId = Value(await first.CreateProjectAsync(new CreateProjectCommand(Guid.NewGuid(), "Continue mapping", Actor, Now), TestContext.Current.CancellationToken)).RunId;
            Value(await first.ConfirmMappingAsync(Mapping(runId, source), TestContext.Current.CancellationToken));
        }
        await using var second = new PersistentDesktopStore(fixture.ManagedRoot);
        RunEvidenceDto evidence = Value(await second.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
        Assert.Equal(RunStateNames.Imported, evidence.Snapshot.DomainState);
        Assert.Contains(RunAction.AssignWells, evidence.Snapshot.AllowedActions);
        Assert.NotNull(evidence.Mapping);
        Assert.Equal(5, evidence.Mapping.Fam.WellColumns.Count);
        Assert.Empty(evidence.Assignments);
        Assert.Empty(evidence.Samples);
        Assert.Equal(ProvenanceCompletenessNames.Unavailable, evidence.ProvenanceCompleteness);
    }

    [Fact]
    public async Task SecondInstanceFailsSafelyAndOwnerCanContinue()
    {
        using var fixture = new WorkspaceFixture();
        await using var owner = new PersistentDesktopStore(fixture.ManagedRoot);
        await owner.Ready;
        await using var blocked = new PersistentDesktopStore(fixture.ManagedRoot);
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => blocked.Ready);
        Assert.Equal("SYSTEM_WORKSPACE_OPEN_FAILED", failure.Message);
        Assert.Null(failure.InnerException);
        GatewayResult<IReadOnlyList<ProjectSnapshotDto>> refused = await blocked.ListProjectsAsync(TestContext.Current.CancellationToken);
        Assert.False(refused.IsSuccess);
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(refused), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Value(await owner.ListProjectsAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task CloseIsIdempotentAndLateOperationsFailWithoutDisposalRaces()
    {
        using var fixture = new WorkspaceFixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        await store.Ready;
        var tasks = Enumerable.Range(0, 12).Select(index => store.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), $"Queued {index}", Actor, Now), TestContext.Current.CancellationToken)).ToArray();
        Task close = store.CloseAsync();
        await Task.WhenAll(tasks);
        await Task.WhenAll(close, store.CloseAsync());
        Assert.All(tasks, task => Assert.True(task.Result.IsSuccess || task.Result.Errors.All(error => error.Code == "SYSTEM_WORKSPACE_CLOSED")));
        GatewayResult<IReadOnlyList<ProjectSnapshotDto>> late = await store.ListProjectsAsync(TestContext.Current.CancellationToken);
        Assert.False(late.IsSuccess);
        Assert.Equal("SYSTEM_WORKSPACE_CLOSED", Assert.Single(late.Errors).Code);
        await using var reopened = new PersistentDesktopStore(fixture.ManagedRoot);
        Assert.Equal(tasks.Count(task => task.Result.IsSuccess), Value(await reopened.ListProjectsAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task InvalidDatabaseFailsClosedWithoutCreatingAReplacement()
    {
        using var fixture = new WorkspaceFixture();
        Directory.CreateDirectory(fixture.ManagedRoot);
        string database = Path.Combine(fixture.ManagedRoot, "workspace.db");
        byte[] corrupt = Encoding.UTF8.GetBytes("Not a SQLite database");
        File.WriteAllBytes(database, corrupt);
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Ready);
        Assert.False((await store.ListProjectsAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(corrupt, File.ReadAllBytes(database));
    }

    private static async Task CompleteRunAsync(PersistentDesktopStore store, Guid runId, SourceFileDto source)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        Value(await store.InspectImportAsync(new InspectImportCommand(Guid.NewGuid(), runId,
            source.OriginalFileName, source.FileSizeBytes, source.RelativeResourcePath), null, token));
        RunSnapshotDto snapshot = Value(await store.ConfirmMappingAsync(Mapping(runId, source), token));
        WellAssignmentDto[] roles =
        [new("A1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100), new("A3", WellRoleNames.WildType),
         new("A4", WellRoleNames.Lqc01Percent), new("A5", WellRoleNames.Sample, "SYN-040", replicateIndex: 1)];
        snapshot = Value(await store.AssignWellsAsync(new AssignWellsCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, Now, roles), token));
        Assert.Equal(StatusNames.Pass, Value(await store.RunCalibrationAsync(new RunCalibrationCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, Now), token)).Status);
        snapshot = Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));
        Assert.Equal(StatusNames.Pass, Value(await store.RunControlQcAsync(new RunControlQcCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, Now), token)).Status);
        snapshot = Value(await store.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), token));
        Assert.Equal(CallNames.Positive, Assert.Single(Value(await store.AnalyzeSamplesAsync(new AnalyzeSamplesCommand(Guid.NewGuid(), runId, snapshot.Revision, Actor, Now), token))).Call);
    }

    private static ConfirmMappingCommand Mapping(Guid runId, SourceFileDto source) => new(Guid.NewGuid(), runId, 0, Actor, Now,
        new ChannelRegionMappingDto(ChannelNames.Fam, DelimitedTextImporter.BodySheetName, 0, 0, [new(1, "A1"), new(2, "A2"), new(3, "A3"), new(4, "A4"), new(5, "A5")]),
        new ChannelRegionMappingDto(ChannelNames.Rox, DelimitedTextImporter.BodySheetName, 0, 0, [new(6, "A1"), new(7, "A2"), new(8, "A3"), new(9, "A4"), new(10, "A5")]),
        source.OriginalFileName, source.RelativeResourcePath, source.Sha256, source.FileSizeBytes, SourceFileDto.MediaTypeNames.Csv);

    private static T Value<T>(GatewayResult<T> result)
    {
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Code)));
        return result.Value!;
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        private readonly string approved;
        private readonly string repositoryRoot;
        public string Root { get; }
        public string ManagedRoot { get; private set; }
        public string Source { get; }
        public WorkspaceFixture()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
            if (repository is null) throw new InvalidOperationException("Repository not found.");
            repositoryRoot = repository.FullName;
            approved = Path.Combine(repository.FullName, "artifacts", "test-tmp");
            Root = Path.Combine(approved, nameof(PersistentDesktopStoreTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ManagedRoot = Path.Combine(Root, "managed");
            Source = Path.Combine(Root, "fixture.csv");
            File.WriteAllText(Source, ManagedFileWorkflowTests.CsvFixture, new UTF8Encoding(false));
        }
        public void WriteMeasurement(string json)
        {
            string report = Path.Combine(repositoryRoot, "artifacts", "benchmarks", nameof(PersistentDesktopStoreTests),
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"), "measurement.json");
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, json, new UTF8Encoding(false));
            TestContext.Current.TestOutputHelper?.WriteLine(Path.GetRelativePath(repositoryRoot, report));
        }
        public void CopyClosedWorkspaceToNewRoot()
        {
            string relocated = Path.Combine(Root, "relocated");
            ValidateOwned(ManagedRoot);
            ValidateOwned(relocated);
            Directory.CreateDirectory(relocated);
            foreach (string source in Directory.EnumerateFiles(ManagedRoot, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(relocated, Path.GetRelativePath(ManagedRoot, source));
                ValidateOwned(source);
                ValidateOwned(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // This also verifies that the repository and exclusive workspace
                // lease have released their file handles before relocation.
                using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.None);
                using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
            ManagedRoot = relocated;
        }
        private void ValidateOwned(string path)
        {
            if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing filesystem operation outside test artifacts.");
        }
        public void Dispose()
        {
            ValidateOwned(Root);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
