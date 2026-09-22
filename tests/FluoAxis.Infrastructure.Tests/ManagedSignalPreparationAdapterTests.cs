namespace FluoAxis.Infrastructure.Tests;

using System.Text;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using Xunit;

public sealed class ManagedSignalPreparationAdapterTests
{
    [Fact]
    public async Task Prepare_rejects_same_size_source_replacement_when_hash_differs()
    {
        string root = Path.Combine(FindRepositoryRoot(), "artifacts", "test-tmp", nameof(ManagedSignalPreparationAdapterTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data", "raw"));
        string path = Path.Combine(root, "data", "raw", "run.csv");
        byte[] bytes = Encoding.UTF8.GetBytes("Time,A1\r\n0,1\r\n2,2\r\n");
        File.WriteAllBytes(path, bytes);

        try
        {
            var importer = new RecordingImporter();
            var adapter = new ManagedSignalPreparationAdapter(root, delimitedImporter: importer);
            PreparedDataMapping mapping = Mapping(Guid.NewGuid());
            var request = new PreparedSignalRequest(
                new PreparedSourceIdentity(
                    "run.csv",
                    RelativePath.Parse("data/raw/run.csv"),
                    new string('0', 64),
                    bytes.Length,
                    SourceFileDto.MediaTypeNames.Csv),
                mapping);

            Result<PreparedRunSignals> result = await adapter.PrepareAsync(
                request, null, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.ImportSourceChanged, Assert.Single(result.Errors).Code);
            Assert.Equal(0, importer.ReadCalls);
        }
        finally
        {
            DeleteContained(root);
        }
    }

    private static PreparedDataMapping Mapping(Guid runId)
    {
        var fam = new PreparedChannelRegion(
            FluorescenceChannel.Fam, "text-body", 0, 0,
            new Dictionary<int, WellId> { [1] = WellId.Parse("A1") });
        var rox = new PreparedChannelRegion(
            FluorescenceChannel.Rox, "text-body", 0, 0,
            new Dictionary<int, WellId> { [1] = WellId.Parse("A1") });
        return new PreparedDataMapping(runId, fam, rox);
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

    private static void DeleteContained(string path)
    {
        string root = Path.GetFullPath(Path.Combine(FindRepositoryRoot(), "artifacts", "test-tmp"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(path);
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove a path outside the approved test root.");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed class RecordingImporter : IImporter
    {
        public int ReadCalls { get; private set; }

        public Task<Result<ImportPreview>> InspectAsync(Stream stream, ImportLimits limits, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ImportedDataset>> ReadAsync(Stream stream, DataMapping mapping, ImportLimits limits, CancellationToken cancellationToken)
        {
            ReadCalls++;
            return Task.FromResult(Result<ImportedDataset>.Ok(new ImportedDataset([], [])));
        }
    }
}
