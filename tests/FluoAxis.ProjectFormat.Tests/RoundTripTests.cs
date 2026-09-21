namespace FluoAxis.ProjectFormat.Tests;

using FluoAxis.Domain;
using FluoAxis.ProjectFormat;
using Xunit;

public class RoundTripTests
{
    [Fact]
    public async Task Save_then_Open_round_trips_manifest_and_entry_bytes()
    {
        using var stream = new MemoryStream();
        Result<ProjectManifest> save = await FluoAxisProjectArchive.SaveAsync(stream, PackageTestFactory.ValidContents(), TestContext.Current.CancellationToken);
        Assert.True(save.IsSuccess, string.Join("; ", save.Errors.Select(e => e.UserMessage)));

        stream.Position = 0;
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(open.IsSuccess, string.Join("; ", open.Errors.Select(e => e.UserMessage)));

        ProjectPackage package = open.Value!;
        ProjectManifest original = PackageTestFactory.ValidContentsManifest();

        Assert.Equal(original.ProjectId, package.Manifest.ProjectId);
        Assert.Equal(original.DisplayName, package.Manifest.DisplayName);
        Assert.Equal(ProjectManifest.CurrentFormatVersion, package.Manifest.FormatVersion);
        Assert.Equal(ProjectManifest.FormatIdentifier, package.Manifest.Format);
        Assert.Equal("SHA-256", package.Manifest.HashAlgorithm);
        Assert.Equal(original.MethodVersions, package.Manifest.MethodVersions);
        Assert.Equal(original.MethodSnapshotHashes, package.Manifest.MethodSnapshotHashes);
        Assert.Equal(original.CreatedAtUtc, package.Manifest.CreatedAtUtc);
        Assert.Equal(original.ModifiedAtUtc, package.Manifest.ModifiedAtUtc);
        Assert.Equal(2, package.Manifest.Entries.Count);

        Assert.Equal(PackageTestFactory.FakeSqlite, package.ReadEntry(RelativePath.Parse("project.sqlite")));
        Assert.Equal(PackageTestFactory.FakeXlsx, package.ReadEntry(RelativePath.Parse("data/raw/source-001.xlsx")));
        Assert.True(package.HasEntry(RelativePath.Parse("project.sqlite")));
        Assert.False(package.HasEntry(RelativePath.Parse("no/such.bin")));
    }

    [Fact]
    public async Task Saved_manifest_hashes_and_sizes_are_computed_from_content()
    {
        using var stream = new MemoryStream();
        Result<ProjectManifest> save = await FluoAxisProjectArchive.SaveAsync(stream, PackageTestFactory.ValidContents(), TestContext.Current.CancellationToken);

        Assert.True(save.IsSuccess);
        foreach (ManifestEntry entry in save.Value!.Entries)
        {
            byte[] content = entry.EntryType == ProjectEntryType.Database
                ? PackageTestFactory.FakeSqlite
                : PackageTestFactory.FakeXlsx;
            Assert.Equal(PackageTestFactory.Sha256Of(content), entry.Sha256.Value);
            Assert.Equal(content.LongLength, entry.SizeBytes);
        }
    }

    [Fact]
    public async Task ReadEntry_returns_a_copy_not_the_internal_buffer()
    {
        byte[] packageBytes = await PackageTestFactory.SaveValidPackageBytesAsync();
        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(open.IsSuccess);

        byte[] first = open.Value!.ReadEntry(RelativePath.Parse("project.sqlite"));
        first[0] = 0xFF;
        byte[] second = open.Value.ReadEntry(RelativePath.Parse("project.sqlite"));
        Assert.NotEqual(first[0], second[0]);
    }

    [Fact]
    public async Task Empty_package_manifest_only_is_valid()
    {
        byte[] packageBytes = PackageTestFactory.BuildZip([], PackageTestFactory.EmptyManifestBytes());
        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(open.IsSuccess, string.Join("; ", open.Errors.Select(e => e.UserMessage)));
        Assert.Empty(open.Value!.Manifest.Entries);
    }

    [Fact]
    public async Task Open_rejects_non_zip_bytes()
    {
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.NotAPackage);
    }

    [Fact]
    public async Task Open_rejects_missing_manifest()
    {
        byte[] packageBytes = PackageTestFactory.BuildZip([("data/raw/a.bin", [1, 2, 3])], manifest: null);
        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.ManifestMissing);
    }

    [Fact]
    public async Task Open_rejects_corrupt_manifest_json()
    {
        byte[] packageBytes = PackageTestFactory.BuildZip([], "{ this is not json"u8.ToArray());
        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.ManifestInvalid);
    }

    [Fact]
    public async Task Open_rejects_future_format_version()
    {
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [],
            PackageTestFactory.EmptyManifestBytes(formatVersion: 99));
        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.UnsupportedFormatVersion);
    }

    [Fact]
    public async Task Save_rejects_blank_display_name()
    {
        ProjectPackageContents contents = PackageTestFactory.ValidContents() with { DisplayName = " " };
        using var stream = new MemoryStream();
        Result<ProjectManifest> save = await FluoAxisProjectArchive.SaveAsync(stream, contents, TestContext.Current.CancellationToken);

        Assert.True(!save.IsSuccess);
        Assert.Contains(save.Errors, e => e.Code == ProjectErrorCodes.SaveInvalidContents);
    }
}
