namespace FluoAxis.ProjectFormat.Tests;

using System.IO.Compression;
using FluoAxis.Domain;
using FluoAxis.ProjectFormat;
using Xunit;

/// <summary>
/// plan.md §7.4 defensive checklist: traversal, absolute paths, collisions,
/// bombs, hash mismatches, undeclared entries. Every case builds a
/// hand-crafted ZIP whose entry names are arbitrary strings, which is what
/// an attacker controls.
/// </summary>
public class PackageSecurityTests
{
    [Theory]
    [MemberData(nameof(UnsafeEntryNames))]
    public async Task Open_rejects_traversal_and_absolute_entry_names(string evilName)
    {
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [(evilName, [1, 2, 3])],
            PackageTestFactory.EmptyManifestBytes());

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e =>
            e.Code == ProjectErrorCodes.EntryOutsideRoot
            || e.Code == ProjectErrorCodes.EntryAbsolutePath
            || e.Code == ProjectErrorCodes.EntryTraversal
            || e.Code == ProjectErrorCodes.EntryNameInvalid);
    }

    public static TheoryData<string> UnsafeEntryNames() => new()
    {
        "../evil.txt",
        string.Concat("C", ":", "\\", "abs.txt"),
        string.Concat("/", "rooted.txt"),
        "a/../../escape",
    };

    [Theory]
    [InlineData("Data/a.txt", "data/A.txt")]
    [InlineData("data/a.txt", "DATA/a.txt")]
    public async Task Open_rejects_case_insensitive_collisions(string first, string second)
    {
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [
                (first, [1, 2, 3]),
                (second, [4, 5, 6]),
            ],
            PackageTestFactory.EmptyManifestBytes());

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.CaseCollision);
    }

    [Fact]
    public async Task Open_rejects_exact_duplicate_entries()
    {
        // ZipArchive.CreateEntry allows creating the same name twice.
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (int i in Enumerable.Range(0, 2))
            {
                ZipArchiveEntry entry = archive.CreateEntry("data/dup.bin", CompressionLevel.Optimal);
                using Stream entryStream = entry.Open();
                entryStream.Write([(byte)i]);
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntry.ManifestPath, CompressionLevel.Optimal);
            using Stream manifestStream = manifestEntry.Open();
            manifestStream.Write(PackageTestFactory.EmptyManifestBytes());
        }

        using var stream = new MemoryStream(buffer.ToArray());
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.DuplicateEntry);
    }

    [Fact]
    public async Task Open_rejects_high_compression_ratio_entry()
    {
        // 200,000 identical bytes deflate far below the 200:1 ratio limit.
        byte[] bomb = new byte[200_000];
        Array.Fill(bomb, (byte)0x41);

        byte[] packageBytes = PackageTestFactory.BuildZip(
            [("data/bomb.bin", bomb)],
            PackageTestFactory.ManifestFor(
                "data/bomb.bin",
                ProjectEntryType.SourceRaw,
                PackageTestFactory.Sha256Of(bomb),
                bomb.LongLength));

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.CompressionRatio);
    }

    [Fact]
    public async Task Open_rejects_entry_size_over_the_per_entry_limit()
    {
        // Small total, but per-entry limit set below the actual entry size.
        byte[] content = PackageTestFactory.MakeBytes(7, 4096);
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [("data/big.bin", content)],
            PackageTestFactory.ManifestFor(
                "data/big.bin",
                ProjectEntryType.SourceRaw,
                PackageTestFactory.Sha256Of(content),
                content.LongLength));

        var limits = new PackageLimits { MaxEntryUncompressedBytes = 2048 };
        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(
            stream, limits, TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.EntrySizeExceeded);
    }

    [Fact]
    public async Task Open_rejects_total_size_over_the_package_limit()
    {
        byte[] contentA = PackageTestFactory.MakeBytes(1, 4096);
        byte[] contentB = PackageTestFactory.MakeBytes(2, 4096);
        byte[] packageBytes = BuildMultiEntryZip(
        [
            ("data/a.bin", contentA, ProjectEntryType.SourceRaw),
            ("data/b.bin", contentB, ProjectEntryType.SourceRaw),
        ]);

        var limits = new PackageLimits { MaxTotalUncompressedBytes = 4096 };
        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(
            stream, limits, TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.TotalSizeExceeded);
    }

    [Fact]
    public async Task Open_rejects_entry_count_over_the_limit()
    {
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [("data/a.bin", [1])],
            PackageTestFactory.EmptyManifestBytes());
        // The entry exists but is undeclared; count limit 0 makes the count
        // check fire first.
        var limits = new PackageLimits { MaxEntryCount = 0 };
        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(
            stream, limits, TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.EntryCountExceeded);
    }

    [Fact]
    public async Task Open_rejects_hash_mismatch()
    {
        byte[] original = PackageTestFactory.FakeXlsx;
        string wrongHash = PackageTestFactory.Sha256Of(PackageTestFactory.FakeSqlite);

        // Save a valid package, then rebuild it with an entry whose bytes
        // differ from the manifest's hash.
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [("data/raw/source-001.xlsx", original)],
            PackageTestFactory.ManifestFor(
                "data/raw/source-001.xlsx",
                ProjectEntryType.SourceRaw,
                wrongHash,
                original.LongLength));

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.EntryHashMismatch);
    }

    [Fact]
    public async Task Open_rejects_declared_size_mismatch()
    {
        byte[] content = PackageTestFactory.FakeXlsx;
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [("data/raw/source-001.xlsx", content)],
            PackageTestFactory.ManifestFor(
                "data/raw/source-001.xlsx",
                ProjectEntryType.SourceRaw,
                PackageTestFactory.Sha256Of(content),
                sizeBytes: 999));

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.EntryHashMismatch);
    }

    [Fact]
    public async Task Open_rejects_undeclared_zip_entry()
    {
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [("data/smuggled.bin", [9, 9, 9])],
            PackageTestFactory.EmptyManifestBytes());

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.EntryNotDeclared);
    }

    [Fact]
    public async Task Open_rejects_manifest_entry_missing_from_zip()
    {
        byte[] missingContent = PackageTestFactory.FakeXlsx;
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [],
            PackageTestFactory.ManifestFor(
                "data/raw/missing.xlsx",
                ProjectEntryType.SourceRaw,
                PackageTestFactory.Sha256Of(missingContent),
                missingContent.LongLength));

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e => e.Code == ProjectErrorCodes.EntryNotDeclared);
    }

    [Fact]
    public async Task Directory_entries_are_skipped_but_name_validated()
    {
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [
                ("data/", []),
                ("data/raw/", []),
            ],
            PackageTestFactory.EmptyManifestBytes());

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(open.IsSuccess, string.Join("; ", open.Errors.Select(e => e.UserMessage)));
        Assert.Empty(open.Value!.Manifest.Entries);
    }

    [Fact]
    public async Task Directory_entry_with_traversal_is_rejected()
    {
        byte[] packageBytes = PackageTestFactory.BuildZip(
            [("../evil-dir/", [])],
            PackageTestFactory.EmptyManifestBytes());

        using var stream = new MemoryStream(packageBytes);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(!open.IsSuccess);
        Assert.Contains(open.Errors, e =>
            e.Code == ProjectErrorCodes.EntryTraversal
            || e.Code == ProjectErrorCodes.EntryOutsideRoot);
    }

    [Fact]
    public async Task Save_rejects_duplicate_paths()
    {
        ProjectPackageContents contents = PackageTestFactory.ValidContents() with
        {
            Files =
            [
                new ProjectPackageContentFile
                {
                    Path = RelativePath.Parse("data/a.bin"),
                    EntryType = ProjectEntryType.SourceRaw,
                    Content = [1, 2, 3],
                },
                new ProjectPackageContentFile
                {
                    Path = RelativePath.Parse("data/a.bin"),
                    EntryType = ProjectEntryType.SourceRaw,
                    Content = [4, 5, 6],
                },
            ],
        };

        using var stream = new MemoryStream();
        Result<ProjectManifest> save = await FluoAxisProjectArchive.SaveAsync(stream, contents, TestContext.Current.CancellationToken);

        Assert.True(!save.IsSuccess);
        Assert.Contains(save.Errors, e => e.Code == ProjectErrorCodes.DuplicateEntry);
    }

    [Fact]
    public async Task Save_rejects_case_colliding_paths()
    {
        ProjectPackageContents contents = PackageTestFactory.ValidContents() with
        {
            Files =
            [
                new ProjectPackageContentFile
                {
                    Path = RelativePath.Parse("data/a.bin"),
                    EntryType = ProjectEntryType.SourceRaw,
                    Content = [1, 2, 3],
                },
                new ProjectPackageContentFile
                {
                    Path = RelativePath.Parse("Data/A.bin"),
                    EntryType = ProjectEntryType.SourceRaw,
                    Content = [4, 5, 6],
                },
            ],
        };

        using var stream = new MemoryStream();
        Result<ProjectManifest> save = await FluoAxisProjectArchive.SaveAsync(stream, contents, TestContext.Current.CancellationToken);

        Assert.True(!save.IsSuccess);
        Assert.Contains(save.Errors, e => e.Code == ProjectErrorCodes.CaseCollision);
    }

    [Fact]
    public async Task Save_rejects_reserved_manifest_path_as_entry()
    {
        ProjectPackageContents contents = PackageTestFactory.ValidContents() with
        {
            Files =
            [
                new ProjectPackageContentFile
                {
                    Path = RelativePath.Parse("manifest.json"),
                    EntryType = ProjectEntryType.SourceRaw,
                    Content = [1],
                },
            ],
        };

        using var stream = new MemoryStream();
        Result<ProjectManifest> save = await FluoAxisProjectArchive.SaveAsync(stream, contents, TestContext.Current.CancellationToken);

        Assert.True(!save.IsSuccess);
        Assert.Contains(save.Errors, e => e.Code == ProjectErrorCodes.SaveInvalidContents);
    }

    private static byte[] BuildMultiEntryZip(
        IReadOnlyList<(string Path, byte[] Content, ProjectEntryType Type)> entries)
    {
        string entryJson = string.Join(",", entries.Select(e =>
            $$"""
                {
                  "path": "{{e.Path}}",
                  "entryType": "{{e.Type.ToString()}}",
                  "sha256": "{{PackageTestFactory.Sha256Of(e.Content)}}",
                  "sizeBytes": {{e.Content.LongLength}}
                }
            """));
        string manifestJson = $$"""
        {
          "format": "fluoaxisproj",
          "formatVersion": 1,
          "projectId": "{{PackageTestFactory.ProjectId}}",
          "displayName": "Multi",
          "createdAtUtc": "2026-09-01T08:00:00Z",
          "modifiedAtUtc": "2026-09-01T09:30:00Z",
          "appVersion": "0.1.0-test",
          "methodVersions": [],
          "methodSnapshotHashes": [],
          "hashAlgorithm": "SHA-256",
          "entries": [{{entryJson}}]
        }
        """;

        return PackageTestFactory.BuildZip(
            entries.Select(e => (e.Path, e.Content)).ToList(),
            System.Text.Encoding.UTF8.GetBytes(manifestJson));
    }
}
