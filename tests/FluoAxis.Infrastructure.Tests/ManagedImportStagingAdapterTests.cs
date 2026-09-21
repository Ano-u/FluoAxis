namespace FluoAxis.Infrastructure.Tests;

using System.Security.Cryptography;
using System.Text;
using FluoAxis.Domain;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class ManagedImportStagingAdapterTests
{
    [Fact]
    public async Task Stage_copies_once_and_returns_path_free_hash_metadata()
    {
        using StagingWorkspace workspace = new();
        byte[] content = Encoding.UTF8.GetBytes("Time,A1\r\n0,123\r\n2,456");
        string source = workspace.WriteExternal("selected.csv", content);
        var adapter = new ManagedImportStagingAdapter(workspace.ManagedRoot);

        Result<StagedImportResource> result = await adapter.StageAsync(
            source, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, ErrorText(result));
        StagedImportResource staged = result.Value!;
        Assert.Equal("selected.csv", staged.DisplayName);
        Assert.StartsWith("data/raw/source-", staged.ResourcePath.Value, StringComparison.Ordinal);
        Assert.EndsWith(".csv", staged.ResourcePath.Value, StringComparison.Ordinal);
        Assert.Equal("text/csv", staged.MediaType);
        Assert.Equal(content.Length, staged.SizeBytes);
        Assert.Equal(HexHash.FromBytes(SHA256.HashData(content)), staged.Sha256);
        Assert.Equal(content, workspace.ReadManaged(staged.ResourcePath));
        Assert.DoesNotContain(workspace.Root, staged.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(workspace.StagingFiles());
    }

    [Fact]
    public async Task Stage_is_cwd_independent_and_parallel_calls_publish_unique_resources()
    {
        using StagingWorkspace workspace = new();
        string source = workspace.WriteExternal("selected.tsv", "Time\tA1\n0\t1"u8.ToArray());
        string originalCwd = Environment.CurrentDirectory;
        string foreignCwd = workspace.CreateDirectory("foreign-cwd");
        var adapter = new ManagedImportStagingAdapter(workspace.ManagedRoot);

        try
        {
            Environment.CurrentDirectory = foreignCwd;
            Result<StagedImportResource>[] results = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => adapter.StageAsync(source, TestContext.Current.CancellationToken)));

            Assert.All(results, result => Assert.True(result.IsSuccess, ErrorText(result)));
            Assert.Equal(8, results.Select(result => result.Value!.ResourcePath.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(results, result => Assert.Equal("text/tab-separated-values", result.Value!.MediaType));
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }

        Assert.Empty(workspace.StagingFiles());
    }

    [Fact]
    public async Task Stage_never_overwrites_on_identifier_collision_and_removes_its_partial_copy()
    {
        using StagingWorkspace workspace = new();
        string source = workspace.WriteExternal("selected.txt", "first"u8.ToArray());
        Guid fixedId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var adapter = new ManagedImportStagingAdapter(workspace.ManagedRoot, createId: () => fixedId);

        Result<StagedImportResource> first = await adapter.StageAsync(source, TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, ErrorText(first));
        byte[] published = workspace.ReadManaged(first.Value!.ResourcePath);

        File.WriteAllBytes(source, "second"u8.ToArray());
        Result<StagedImportResource> second = await adapter.StageAsync(source, TestContext.Current.CancellationToken);

        AssertFailure(second, ErrorCodes.ImportStagingCollision, workspace);
        Assert.Equal(published, workspace.ReadManaged(first.Value.ResourcePath));
        Assert.Empty(workspace.StagingFiles());
    }

    [Fact]
    public async Task Stage_rejects_missing_unsupported_empty_and_oversized_sources_without_artifacts_or_paths()
    {
        using StagingWorkspace workspace = new();
        string missing = Path.Combine(workspace.ExternalRoot, "missing.csv");
        string unsupported = workspace.WriteExternal("selected.bin", [1]);
        string empty = workspace.WriteExternal("empty.csv", []);
        string oversized = workspace.WriteExternal("large.csv", new byte[17]);
        var adapter = new ManagedImportStagingAdapter(workspace.ManagedRoot, maxBytes: 16);

        Result<StagedImportResource> missingResult = await adapter.StageAsync(missing, TestContext.Current.CancellationToken);
        Result<StagedImportResource> unsupportedResult = await adapter.StageAsync(unsupported, TestContext.Current.CancellationToken);
        Result<StagedImportResource> emptyResult = await adapter.StageAsync(empty, TestContext.Current.CancellationToken);
        Result<StagedImportResource> oversizedResult = await adapter.StageAsync(oversized, TestContext.Current.CancellationToken);

        AssertFailure(missingResult, ErrorCodes.ImportSourceUnavailable, workspace);
        AssertFailure(unsupportedResult, ImportErrorCodes.ImportFormatMismatch, workspace);
        AssertFailure(emptyResult, ErrorCodes.ImportSourceUnavailable, workspace);
        AssertFailure(oversizedResult, ImportErrorCodes.ImportFileTooLarge, workspace);
        Assert.Empty(workspace.ManagedFiles());
    }

    [Fact]
    public async Task Stage_honors_pre_cancellation_without_creating_managed_content()
    {
        using StagingWorkspace workspace = new();
        string source = workspace.WriteExternal("selected.xlsx", "content"u8.ToArray());
        var adapter = new ManagedImportStagingAdapter(workspace.ManagedRoot);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.StageAsync(source, cancellation.Token));

        Assert.Empty(workspace.ManagedFiles());
    }

    [Fact]
    public async Task Stage_cancellation_during_copy_removes_partial_and_unpublished_content()
    {
        using StagingWorkspace workspace = new();
        string source = workspace.WriteExternal("selected.csv", new byte[256 * 1024]);
        var adapter = new ManagedImportStagingAdapter(workspace.ManagedRoot);
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelOnFirstProgress(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.StageAsync(
            source, progress, cancellation.Token));

        Assert.True(progress.WasReported);
        Assert.Empty(workspace.ManagedFiles());
        Assert.Empty(workspace.StagingFiles());
    }

    [Fact]
    public async Task Stage_holds_source_read_only_until_copy_finishes()
    {
        using StagingWorkspace workspace = new();
        string source = workspace.WriteExternal("selected.csv", new byte[128 * 1024]);
        var adapter = new ManagedImportStagingAdapter(workspace.ManagedRoot);
        var probe = new SourceMutationProbe(source);

        Result<StagedImportResource> result = await adapter.StageAsync(
            source, probe, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, ErrorText(result));
        Assert.True(probe.WasReported);
        Assert.IsType<IOException>(probe.MutationFailure);
        Assert.Equal(128 * 1024, result.Value!.SizeBytes);
    }

    [Fact]
    public void Constructor_rejects_relative_root_and_non_positive_limit()
    {
        Assert.Throws<ArgumentException>(() => new ManagedImportStagingAdapter("managed"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManagedImportStagingAdapter(
            Path.GetFullPath("artifacts"), maxBytes: 0));
    }

    private static void AssertFailure(
        Result<StagedImportResource> result,
        string expectedCode,
        StagingWorkspace workspace)
    {
        Assert.False(result.IsSuccess);
        AppError error = Assert.Single(result.Errors);
        Assert.Equal(expectedCode, error.Code);
        string errorText = $"{error.UserMessage} {error.SafeDetailCode}";
        Assert.DoesNotContain(workspace.Root, errorText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workspace.ExternalRoot, errorText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workspace.ManagedRoot, errorText, StringComparison.OrdinalIgnoreCase);
    }

    private static string ErrorText(Result<StagedImportResource> result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.UserMessage}"));

    private sealed class CancelOnFirstProgress(CancellationTokenSource cancellation) : IProgress<long>
    {
        public bool WasReported { get; private set; }

        public void Report(long value)
        {
            Assert.True(value > 0);
            WasReported = true;
            cancellation.Cancel();
        }
    }

    private sealed class SourceMutationProbe(string source) : IProgress<long>
    {
        public bool WasReported { get; private set; }
        public Exception? MutationFailure { get; private set; }

        public void Report(long value)
        {
            if (WasReported)
            {
                return;
            }

            Assert.True(value > 0);
            WasReported = true;
            try
            {
                using FileStream ignored = new(source, FileMode.Open, FileAccess.Write, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MutationFailure = ex;
            }
        }
    }

    private sealed class StagingWorkspace : IDisposable
    {
        private readonly string repositoryRoot = FindRepositoryRoot();
        public string Root { get; }
        public string ExternalRoot { get; }
        public string ManagedRoot { get; }

        public StagingWorkspace()
        {
            Root = Path.Combine(repositoryRoot, "artifacts", "test-tmp", nameof(FluoAxis.Infrastructure.Tests),
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                nameof(ManagedImportStagingAdapterTests), Guid.NewGuid().ToString("N"));
            ExternalRoot = Path.Combine(Root, "external");
            ManagedRoot = Path.Combine(Root, "managed");
            Directory.CreateDirectory(ExternalRoot);
        }

        public string WriteExternal(string fileName, byte[] content)
        {
            string path = Path.Combine(ExternalRoot, fileName);
            File.WriteAllBytes(path, content);
            return path;
        }

        public byte[] ReadManaged(RelativePath path) => File.ReadAllBytes(Path.Combine(
            ManagedRoot, path.Value.Replace('/', Path.DirectorySeparatorChar)));

        public string CreateDirectory(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

        public string[] ManagedFiles() => Directory.Exists(ManagedRoot)
            ? Directory.GetFiles(ManagedRoot, "*", SearchOption.AllDirectories)
            : [];

        public string[] StagingFiles() => Directory.Exists(ManagedRoot)
            ? Directory.GetFiles(ManagedRoot, ".staging-*", SearchOption.AllDirectories)
            : [];

        public void Dispose()
        {
            string approved = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "test-tmp"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(approved, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove a staging fixture outside the approved test root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "FluoAxis.slnx")))
            {
                current = current.Parent;
            }

            return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        }
    }
}
