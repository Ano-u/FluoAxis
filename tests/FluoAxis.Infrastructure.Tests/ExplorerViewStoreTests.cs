namespace FluoAxis.Infrastructure.Tests;

using System.Text;
using FluoAxis.Contracts;
using FluoAxis.Infrastructure;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class ExplorerViewStoreTests
{
    [Fact]
    public async Task PairedRawValuesAndResearchViewSurviveReopenWithoutChangingRun()
    {
        using var fixture = new Workspace();
        Guid runId;
        SavedExplorerViewDto saved;
        ManagedProjectPackageDto package;
        await using (var store = new PersistentDesktopStore(fixture.Root))
        {
            await store.Ready;
            ProjectSnapshotDto project = Value(await store.CreateProjectAsync(new(Guid.NewGuid(), "Explorer test", "tester", DateTimeOffset.UtcNow), TestContext.Current.CancellationToken));
            runId = project.RunId;
            string external = fixture.WriteSource("Time,A1,A2,A1_ROX,A2_ROX\n0,100,200,50,70\n90,110,220,55,75\n120,120,240,60,80\n");
            SourceFileDto source = Value(await store.StageAsync(external, TestContext.Current.CancellationToken));
            _ = Value(await store.ConfirmMappingAsync(new(Guid.NewGuid(), runId, 0, "tester", DateTimeOffset.UtcNow,
                new(ChannelNames.Fam, "text-body", 0, 0, [new(1, "A1"), new(2, "A2")]),
                new(ChannelNames.Rox, "text-body", 0, 0, [new(3, "A1"), new(4, "A2")]),
                source.OriginalFileName, source.RelativeResourcePath, source.Sha256, source.FileSizeBytes, SourceFileDto.MediaTypeNames.Csv), TestContext.Current.CancellationToken));
            ExplorerPairPageDto pair = Value(await store.GetExplorerPairPageAsync(runId, "A1", 1, 2, TestContext.Current.CancellationToken));
            Assert.Equal(3, pair.TotalPoints);
            Assert.Equal(1, pair.Offset);
            Assert.Equal(1, pair.Points[0].PointIndex);
            Assert.Equal(110.0, pair.Points[0].Fam);
            Assert.Equal(55.0, pair.Points[0].Rox);
            var definition = new ExplorerViewDefinitionDto(runId, source.Sha256, "A1", "raw", "Scatter3D", "fam", "rox", "time", "Linear", "Linear", "Linear",
                [new("sum", "双通道和", "RFU", "fam+rox")], [], [], [], filterColumn: "sum", filterMinimum: 100, sortColumn: "sum", sortDescending: true, pageOffset: 1);
            saved = Value(await store.SaveExplorerViewAsync(definition, TestContext.Current.CancellationToken));
            RunSnapshotDto snapshot = Value(await store.GetRunAsync(new(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));
            Assert.Equal(1, snapshot.Revision);
            Assert.Equal(RunStateNames.Imported, snapshot.DomainState);
            Assert.Null(snapshot.AnalysisSummary);
            package = Value(await store.CreateProjectPackageAsync(runId, TestContext.Current.CancellationToken));
        }
        await using var reopened = new PersistentDesktopStore(fixture.Root);
        await reopened.Ready;
        SavedExplorerViewDto? loaded = Value(await reopened.LoadExplorerViewAsync(runId, TestContext.Current.CancellationToken));
        Assert.NotNull(loaded);
        Assert.Equal(saved.Sha256, loaded.Sha256);
        Assert.Equal("fam+rox", Assert.Single(loaded.Definition.DerivedColumns).Expression);
        Assert.Equal("sum", loaded.Definition.FilterColumn);
        Assert.Equal(100.0, loaded.Definition.FilterMinimum);
        Assert.True(loaded.Definition.SortDescending);
        Assert.Equal(1, loaded.Definition.PageOffset);
        Assert.Equal(120.0, Value(await reopened.GetExplorerPairPageAsync(runId, "A1", 2, 1, TestContext.Current.CancellationToken)).Points[0].Fam);

        using var destination = new Workspace();
        await using var importedStore = new PersistentDesktopStore(destination.Root);
        await importedStore.Ready;
        string packageFile = Path.Combine(fixture.Root, package.RelativeResourcePath.Replace('/', Path.DirectorySeparatorChar));
        ProjectSnapshotDto imported = Value(await importedStore.ImportProjectPackageAsync(packageFile, TestContext.Current.CancellationToken));
        SavedExplorerViewDto? packagedView = Value(await importedStore.LoadExplorerViewAsync(imported.RunId, TestContext.Current.CancellationToken));
        Assert.NotNull(packagedView);
        Assert.Equal(saved.Sha256, packagedView.Sha256);
        Assert.Equal("sum", packagedView.Definition.FilterColumn);
        Assert.Equal(1, packagedView.Definition.PageOffset);
        ExplorerPairPageDto packagedPoints = Value(await importedStore.GetExplorerPairPageAsync(imported.RunId, "A1", 0, 3, TestContext.Current.CancellationToken));
        Assert.Equal(new[] { 100.0, 110.0, 120.0 }, packagedPoints.Points.Select(point => point.Fam));
        Assert.Equal(new[] { 50.0, 55.0, 60.0 }, packagedPoints.Points.Select(point => point.Rox));
    }

    [Fact]
    public async Task ManualDefinitionHasNoRawIdentityAndTamperedViewIsRejected()
    {
        using var fixture = new Workspace();
        await using var store = new PersistentDesktopStore(fixture.Root);
        await store.Ready;
        ProjectSnapshotDto project = Value(await store.CreateProjectAsync(new(Guid.NewGuid(), "Manual", "tester", DateTimeOffset.UtcNow), TestContext.Current.CancellationToken));
        Assert.Null(Value(await store.LoadExplorerViewAsync(project.RunId, TestContext.Current.CancellationToken)));
        var definition = new ExplorerViewDefinitionDto(project.RunId, null, "A1", "manual", "Scatter2D", "x", "y", "z", "Linear", "Linear", "Linear", [], [], [], [new(0, 1, 2, 3)]);
        _ = Value(await store.SaveExplorerViewAsync(definition, TestContext.Current.CancellationToken));
        string path = Path.Combine(fixture.Root, "explorer", project.RunId.ToString("N"), "view.json");
        string json = File.ReadAllText(path).Replace("\"x\":1", "\"x\":9", StringComparison.Ordinal);
        File.WriteAllText(path, json);
        GatewayResult<SavedExplorerViewDto?> corrupted = await store.LoadExplorerViewAsync(project.RunId, TestContext.Current.CancellationToken);
        Assert.False(corrupted.IsSuccess);
        Assert.Equal("PLOT_EXPLORER_VIEW_IDENTITY_MISMATCH", corrupted.Errors[0].Code);
        RunSnapshotDto snapshot = Value(await store.GetRunAsync(new(Guid.NewGuid(), project.RunId), TestContext.Current.CancellationToken));
        Assert.Empty(snapshot.SourceFiles);
        Assert.Equal(0, snapshot.Revision);
    }

    [Fact]
    public async Task PageBudgetAndWellFailuresKeepTheirStructuredPlotErrorCodes()
    {
        using var fixture = new Workspace();
        await using var store = new PersistentDesktopStore(fixture.Root);
        await store.Ready;
        Guid runId = Value(await store.CreateProjectAsync(new(Guid.NewGuid(), "Empty", "tester", DateTimeOffset.UtcNow), TestContext.Current.CancellationToken)).RunId;
        GatewayResult<ExplorerPairPageDto> budget = await store.GetExplorerPairPageAsync(runId, "A1", 0, 25001, TestContext.Current.CancellationToken);
        Assert.Equal("PLOT_EXPLORER_PAGE_INVALID", Assert.Single(budget.Errors).Code);
        GatewayResult<ExplorerPairPageDto> well = await store.GetExplorerPairPageAsync(runId, "Z99", 0, 1, TestContext.Current.CancellationToken);
        Assert.Equal("PLOT_EXPLORER_WELL_INVALID", Assert.Single(well.Errors).Code);
        GatewayResult<ExplorerPairPageDto> source = await store.GetExplorerPairPageAsync(runId, "A1", 0, 1, TestContext.Current.CancellationToken);
        Assert.Equal("PLOT_EXPLORER_SOURCE_UNAVAILABLE", Assert.Single(source.Errors).Code);
    }

    private static T Value<T>(GatewayResult<T> result) { Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(error => error.Code))); return result.Value!; }
    private sealed class Workspace : IDisposable
    {
        private readonly string approved;
        public string Root { get; }
        public Workspace()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluoAxis.slnx"))) directory = directory.Parent;
            approved = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository missing."), "artifacts", "test-tmp");
            Root = Path.Combine(approved, nameof(ExplorerViewStoreTests), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        public string WriteSource(string text) { string file = Path.Combine(Root, "synthetic.csv"); File.WriteAllText(file, text, new UTF8Encoding(false)); return file; }
        public void Dispose()
        {
            string full = Path.GetFullPath(Root);
            if (!full.StartsWith(Path.GetFullPath(approved).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe cleanup.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
