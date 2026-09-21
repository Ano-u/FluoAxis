namespace FluoAxis.ProjectFormat.Tests;

using System.IO.Compression;
using FluoAxis.Domain;
using FluoAxis.ProjectFormat;
using Xunit;

/// <summary>
/// Repository-local temp root per Data-Contract §4, unique across processes
/// and tests. Repository discovery starts from the test assembly location and
/// therefore does not depend on the process current working directory.
/// </summary>
public sealed class TempRoot : IDisposable
{
    private static readonly string ApprovedRoot = Path.Combine(
        FindRepositoryRoot(),
        "artifacts",
        "test-tmp",
        "FluoAxis.ProjectFormat.Tests");

    public TempRoot([System.Runtime.CompilerServices.CallerMemberName] string testName = "test")
    {
        Root = Path.Combine(
            ApprovedRoot,
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Sanitize(testName),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Creates a unique subdirectory and returns its full path.</summary>
    public string NewDir(string name)
    {
        string path = Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates a unique file path (file itself not created).</summary>
    public string NewFile(string name)
        => Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        string approved = Path.GetFullPath(ApprovedRoot);
        string candidate = Path.GetFullPath(Root);
        string approvedPrefix = approved.EndsWith(Path.DirectorySeparatorChar)
            ? approved
            : approved + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(approvedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to clean a test directory outside the approved artifact root.");
        }

        try
        {
            Directory.Delete(candidate, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FluoAxis.slnx"))
                && File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the FluoAxis repository root from the test assembly directory.");
    }
}

/// <summary>Builders for valid packages and hand-crafted malicious ones.</summary>
public static class PackageTestFactory
{
    public static readonly Guid ProjectId = Guid.NewGuid();

    public static readonly HexHash AnyHash = new("a1".PadRight(64, '0'));

    public static byte[] FakeSqlite { get; } = MakeBytes(0x51, 2048);

    public static byte[] FakeXlsx { get; } = MakeBytes(0xAB, 4096);

    /// <summary>Save→open round-trip-friendly contents with two entries.</summary>
    public static ProjectPackageContents ValidContents() => new()
    {
        ProjectId = ProjectId,
        DisplayName = "Round-trip project",
        CreatedAtUtc = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 9, 30, 0, TimeSpan.Zero),
        MethodVersions = ["BRAF-V600E/1.0"],
        MethodSnapshotHashes = [AnyHash.Value],
        Files =
        [
            new ProjectPackageContentFile
            {
                Path = RelativePath.Parse("project.sqlite"),
                EntryType = ProjectEntryType.Database,
                Content = FakeSqlite,
            },
            new ProjectPackageContentFile
            {
                Path = RelativePath.Parse("data/raw/source-001.xlsx"),
                EntryType = ProjectEntryType.SourceRaw,
                Content = FakeXlsx,
            },
        ],
    };

    /// <summary>Saves valid contents to a fresh byte array.</summary>
    public static async Task<byte[]> SaveValidPackageBytesAsync()
    {
        using var stream = new MemoryStream();
        Result<ProjectManifest> result = await FluoAxisProjectArchive.SaveAsync(
            stream, ValidContents(), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.UserMessage)));
        return stream.ToArray();
    }

    /// <summary>
    /// The manifest metadata fields of <see cref="ValidContents"/> for
    /// round-trip assertions; entry hash/size are asserted separately.
    /// </summary>
    public static ProjectManifest ValidContentsManifest() => new()
    {
        Format = ProjectManifest.FormatIdentifier,
        FormatVersion = ProjectManifest.CurrentFormatVersion,
        ProjectId = ProjectId,
        DisplayName = "Round-trip project",
        CreatedAtUtc = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 9, 30, 0, TimeSpan.Zero),
        AppVersion = ProjectManifest.CurrentAppVersion,
        MethodVersions = ["BRAF-V600E/1.0"],
        MethodSnapshotHashes = [AnyHash.Value],
        HashAlgorithm = ProjectManifest.Sha256Algorithm,
        Entries = [],
    };

    /// <summary>
    /// Builds a ZIP byte array with arbitrary entry names (including
    /// malicious ones that ZipArchive normally rejects) and arbitrary
    /// payloads, plus a manifest entry. Each entry is a (name, bytes) pair;
    /// pass a null manifest to build a manifest-less package.
    /// </summary>
    public static byte[] BuildZip(
        IReadOnlyList<(string Name, byte[] Content)> entries,
        byte[]? manifest = null)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using Stream entryStream = entry.Open();
                entryStream.Write(content);
            }

            if (manifest is not null)
            {
                ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntry.ManifestPath, CompressionLevel.Optimal);
                using Stream manifestStream = manifestEntry.Open();
                manifestStream.Write(manifest);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// A manifest JSON describing exactly one entry, used for
    /// hash-mismatch and size-mismatch attacks.
    /// </summary>
    public static byte[] ManifestFor(
        string path,
        ProjectEntryType entryType,
        string sha256,
        long sizeBytes,
        int formatVersion = 1)
    {
        string json = $$"""
        {
          "format": "fluoaxisproj",
          "formatVersion": {{formatVersion}},
          "projectId": "{{ProjectId}}",
          "displayName": "Hand-crafted",
          "createdAtUtc": "2026-09-01T08:00:00Z",
          "modifiedAtUtc": "2026-09-01T09:30:00Z",
          "appVersion": "0.1.0-test",
          "methodVersions": ["BRAF-V600E/1.0"],
          "methodSnapshotHashes": ["{{AnyHash.Value}}"],
          "hashAlgorithm": "SHA-256",
          "entries": [
            {
              "path": "{{path}}",
              "entryType": "{{entryType.ToString()}}",
              "sha256": "{{sha256}}",
              "sizeBytes": {{sizeBytes}}
            }
          ]
        }
        """;
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    /// <summary>Valid, empty manifest (no entries).</summary>
    public static byte[] EmptyManifestBytes(int formatVersion = 1)
    {
        string json = $$"""
        {
          "format": "fluoaxisproj",
          "formatVersion": {{formatVersion}},
          "projectId": "{{ProjectId}}",
          "displayName": "Empty project",
          "createdAtUtc": "2026-09-01T08:00:00Z",
          "modifiedAtUtc": "2026-09-01T09:30:00Z",
          "appVersion": "0.1.0-test",
          "methodVersions": [],
          "methodSnapshotHashes": [],
          "hashAlgorithm": "SHA-256",
          "entries": []
        }
        """;
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public static byte[] MakeBytes(byte seed, int length)
    {
        byte[] bytes = new byte[length];
        var random = new Random(seed);
        random.NextBytes(bytes);
        return bytes;
    }

    public static string Sha256Of(byte[] content)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
}
