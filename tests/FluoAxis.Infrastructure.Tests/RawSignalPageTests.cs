namespace FluoAxis.Infrastructure.Tests;

using System.Globalization;
using System.Text;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using Xunit;

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class RawSignalPageTests
{
    private static readonly DateTimeOffset IssuedUtc = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StoredRawPagesKeepExactIdentityTicksValuesAndSourceUnchanged()
    {
        using var fixture = new Fixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        (Guid runId, SourceFileDto source) = await ImportAsync(store, fixture.SourceFile);
        byte[] sourceBefore = File.ReadAllBytes(Path.Combine(fixture.ManagedRoot, source.RelativeResourcePath));
        RunEvidenceDto before = Value(await store.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken));

        RawSignalPageDto first = Value(await store.GetRawSignalPageAsync(runId, "a5", ChannelNames.Fam, 0, 2, TestContext.Current.CancellationToken));
        RawSignalPageDto second = Value(await store.GetRawSignalPageAsync(runId, "A5", ChannelNames.Fam, 2, 2, TestContext.Current.CancellationToken));
        RawSignalPageDto rox = Value(await store.GetRawSignalPageAsync(runId, "A5", ChannelNames.Rox, 0, 25_000, TestContext.Current.CancellationToken));
        RawSignalPageDto beyond = Value(await store.GetRawSignalPageAsync(runId, "A5", ChannelNames.Fam, int.MaxValue, 1, TestContext.Current.CancellationToken));

        Assert.Equal(runId, first.RunId);
        Assert.Equal(1, first.Revision);
        Assert.Equal("A5", first.Well);
        Assert.Equal(ChannelNames.Fam, first.Channel);
        Assert.Equal(3, first.TotalPoints);
        Assert.Equal([0, 1], first.Points.Select(point => point.PointIndex));
        Assert.Equal([0L, TimeSpan.FromMinutes(90).Ticks], first.Points.Select(point => point.ElapsedTicks));
        Assert.Equal([100d, 280d], first.Points.Select(point => point.Rfu));
        RawSignalPointDto last = Assert.Single(second.Points);
        Assert.Equal(2, last.PointIndex);
        Assert.Equal(TimeSpan.FromMinutes(120).Ticks, last.ElapsedTicks);
        Assert.Equal(460d, last.Rfu);
        Assert.All(rox.Points, point => Assert.Equal(50d, point.Rfu));
        Assert.Equal(3, beyond.TotalPoints);
        Assert.Empty(beyond.Points);
        Assert.Equal(sourceBefore, File.ReadAllBytes(Path.Combine(fixture.ManagedRoot, source.RelativeResourcePath)));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(Value(await store.GetRunEvidenceAsync(
            new GetRunQuery(Guid.NewGuid(), runId), TestContext.Current.CancellationToken))));
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyRunOrUnmappedWellHasZeroPointsWhileUnknownRunIsRejected()
    {
        using var fixture = new Fixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        ProjectSnapshotDto project = Value(await store.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Empty Raw query", "raw-test", IssuedUtc), TestContext.Current.CancellationToken));

        RawSignalPageDto empty = Value(await store.GetRawSignalPageAsync(project.RunId, "H12", ChannelNames.Rox, 0, 25000, TestContext.Current.CancellationToken));
        GatewayResult<RawSignalPageDto> missing = await store.GetRawSignalPageAsync(Guid.NewGuid(), "A1", ChannelNames.Fam, 0, 2, TestContext.Current.CancellationToken);

        Assert.Equal(0, empty.Revision);
        Assert.Equal(0, empty.TotalPoints);
        Assert.Empty(empty.Points);
        Assert.False(missing.IsSuccess);
        Assert.Equal(GatewayErrorCodes.RunNotFound, Assert.Single(missing.Errors).Code);
    }

    [Theory]
    [InlineData("A1", "fam", -1, 1)]
    [InlineData("A1", "fam", 0, 0)]
    [InlineData("A1", "fam", 0, 25001)]
    [InlineData("Z1", "fam", 0, 2)]
    [InlineData("A1", "FAM", 0, 2)]
    public async Task InvalidQueriesAreRejectedWithoutClampingOrCreatingData(string well, string channel, int offset, int pageSize)
    {
        using var fixture = new Fixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);

        GatewayResult<RawSignalPageDto> result = await store.GetRawSignalPageAsync(Guid.NewGuid(), well, channel, offset, pageSize, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("PLOT_RAW_QUERY_INVALID", Assert.Single(result.Errors).Code);
        Assert.Empty(Value(await store.ListProjectsAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task PreCanceledQueryDoesNotPublishADataPage()
    {
        using var fixture = new Fixture();
        await using var store = new PersistentDesktopStore(fixture.ManagedRoot);
        (Guid runId, _) = await ImportAsync(store, fixture.SourceFile);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetRawSignalPageAsync(runId, "A5", ChannelNames.Fam, 0, 2, canceled.Token));
        Assert.Equal(1, Value(await store.GetRawSignalPageAsync(runId, "A5", ChannelNames.Fam, 0, 2, TestContext.Current.CancellationToken)).Revision);
    }

    [Fact]
    public async Task NewRevisionIsObservedWithoutChangingRawPointsAndSurvivesReopen()
    {
        using var fixture = new Fixture();
        Guid runId;
        RawSignalPageDto latest;
        await using (var store = new PersistentDesktopStore(fixture.ManagedRoot))
        {
            (runId, _) = await ImportAsync(store, fixture.SourceFile);
            RawSignalPageDto before = Value(await store.GetRawSignalPageAsync(runId, "A5", ChannelNames.Fam, 0, 3, TestContext.Current.CancellationToken));
            Value(await store.AssignWellsAsync(new AssignWellsCommand(Guid.NewGuid(), runId, before.Revision, "raw-test", IssuedUtc,
                [new("A1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100), new("A3", WellRoleNames.WildType),
                 new("A4", WellRoleNames.Lqc01Percent), new("A5", WellRoleNames.Sample, "Raw-sample", replicateIndex: 1)]), TestContext.Current.CancellationToken));
            latest = Value(await store.GetRawSignalPageAsync(runId, "A5", ChannelNames.Fam, 0, 3, TestContext.Current.CancellationToken));
            Assert.Equal(2, latest.Revision);
            Assert.Equal(before.Points, latest.Points);
        }
        await using var reopened = new PersistentDesktopStore(fixture.ManagedRoot);
        RawSignalPageDto after = Value(await reopened.GetRawSignalPageAsync(runId, "A5", ChannelNames.Fam, 0, 3, TestContext.Current.CancellationToken));
        Assert.Equal(JsonSerializer.Serialize(latest), JsonSerializer.Serialize(after));
    }

    [Fact]
    public void RawPageDefensivelyCopiesPointsWithoutChangingNegativeRfu()
    {
        List<RawSignalPointDto> points = [new(0, 0, -5), new(1, 1, -4)];
        var page = new RawSignalPageDto(Guid.NewGuid(), 3, "A1", ChannelNames.Fam, 2, points);
        points.Clear();

        Assert.Equal(2, page.Points.Count);
        Assert.Equal(-5, page.Points[0].Rfu);
        Assert.Throws<NotSupportedException>(() => ((IList<RawSignalPointDto>)page.Points).Clear());
    }

    private static async Task<(Guid RunId, SourceFileDto Source)> ImportAsync(PersistentDesktopStore store, string sourceFile)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        SourceFileDto source = Value(await store.StageAsync(sourceFile, token));
        ProjectSnapshotDto project = Value(await store.CreateProjectAsync(new CreateProjectCommand(Guid.NewGuid(), "Raw research", "raw-test", IssuedUtc), token));
        var fam = new ChannelRegionMappingDto(ChannelNames.Fam, DelimitedTextImporter.BodySheetName, 0, 0,
            [new(1, "A1"), new(2, "A2"), new(3, "A3"), new(4, "A4"), new(5, "A5")]);
        var rox = new ChannelRegionMappingDto(ChannelNames.Rox, DelimitedTextImporter.BodySheetName, 0, 0,
            [new(6, "A1"), new(7, "A2"), new(8, "A3"), new(9, "A4"), new(10, "A5")]);
        Value(await store.ConfirmMappingAsync(new ConfirmMappingCommand(Guid.NewGuid(), project.RunId, 0, "raw-test", IssuedUtc,
            fam, rox, source.OriginalFileName, source.RelativeResourcePath, source.Sha256, source.FileSizeBytes, SourceFileDto.MediaTypeNames.Csv), token));
        return (project.RunId, source);
    }

    private static T Value<T>(GatewayResult<T> result)
    {
        Assert.True(result.IsSuccess, string.Join(';', result.Errors.Select(error => error.Code)));
        return result.Value!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string approved;
        public string Root { get; }
        public string ManagedRoot => Path.Combine(Root, "managed");
        public string SourceFile { get; }

        public Fixture()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
            if (repository is null) throw new InvalidOperationException("Repository not found.");
            approved = Path.Combine(repository.FullName, "artifacts", "test-tmp");
            Root = Path.Combine(approved, nameof(RawSignalPageTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SourceFile = Path.Combine(Root, "synthetic.csv");
            File.WriteAllText(SourceFile, ManagedFileWorkflowTests.CsvFixture, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            string candidate = Path.GetFullPath(Root);
            if (!candidate.StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing cleanup outside approved test artifacts.");
            if (Directory.Exists(candidate)) Directory.Delete(candidate, recursive: true);
        }
    }
}
