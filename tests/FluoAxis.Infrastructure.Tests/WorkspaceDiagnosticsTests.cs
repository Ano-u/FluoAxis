namespace FluoAxis.Infrastructure.Tests;

using System.Globalization;
using System.IO.Compression;
using System.Text;
using FluoAxis.Contracts;
using FluoAxis.Infrastructure;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class WorkspaceDiagnosticsTests
{
    [Fact]
    public async Task Clean_close_removes_session_marker_and_reopen_is_not_recovery()
    {
        using var fixture = new Fixture();
        await using (var first = new PersistentDesktopStore(fixture.Root))
        {
            await first.Ready;
            Assert.True(File.Exists(Path.Combine(fixture.Root, "session.active")));
            Assert.False((await first.GetDiagnosticsAsync(TestContext.Current.CancellationToken)).Value!.RecoveredUncleanSession);
        }
        Assert.False(File.Exists(Path.Combine(fixture.Root, "session.active")));
        await using var reopened = new PersistentDesktopStore(fixture.Root);
        Assert.False((await reopened.GetDiagnosticsAsync(TestContext.Current.CancellationToken)).Value!.RecoveredUncleanSession);
    }

    [Fact]
    public async Task Interrupted_session_is_reported_after_integrity_check_without_replacing_saved_run()
    {
        using var fixture = new Fixture();
        Guid runId;
        await using (var first = new PersistentDesktopStore(fixture.Root))
        {
            var project = await first.CreateProjectAsync(new(Guid.NewGuid(), "private research name", "test", DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
            runId = project.Value!.RunId;
        }
        File.WriteAllText(Path.Combine(fixture.Root, "session.active"), "previous session");
        await using var reopened = new PersistentDesktopStore(fixture.Root);
        var diagnostic = (await reopened.GetDiagnosticsAsync(TestContext.Current.CancellationToken)).Value!;
        Assert.True(diagnostic.RecoveredUncleanSession);
        Assert.Contains(diagnostic.RecentEvents, item => item.Code == "WORKSPACE_RECOVERED");
        Assert.Equal(runId, Assert.Single((await reopened.ListProjectsAsync(TestContext.Current.CancellationToken)).Value!).RunId);
    }

    [Fact]
    public async Task Support_bundle_contains_only_allowlisted_metadata_not_project_names_or_machine_paths()
    {
        using var fixture = new Fixture();
        await using var store = new PersistentDesktopStore(fixture.Root);
        await store.CreateProjectAsync(new(Guid.NewGuid(), "SECRET_STUDY_LABEL", "test", DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
        var result = await store.CreateSupportBundleAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        SupportBundleDto receipt = result.Value!;
        using var output = new MemoryStream();
        Assert.True((await store.CopyManagedExportAsync(receipt.RelativeEntry, receipt.Sha256, output, TestContext.Current.CancellationToken)).IsSuccess);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.Equal(new[] { "diagnostics.json", "README.txt" }, zip.Entries.Select(entry => entry.FullName));
        using var reader = new StreamReader(zip.GetEntry("diagnostics.json")!.Open(), Encoding.UTF8);
        string json = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("SECRET_STUDY_LABEL", json, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        Assert.True(receipt.FileSizeBytes < 64 * 1024);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string approved;
        internal string Root { get; }
        internal Fixture()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
            approved = Path.Combine(repository!.FullName, "artifacts", "test-tmp");
            Root = Path.Combine(approved, nameof(WorkspaceDiagnosticsTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe cleanup.");
            Directory.Delete(Root, true);
        }
    }
}
