namespace FluoAxis.ProjectFormat.Tests;

using System.Text;
using FluoAxis.Domain;
using FluoAxis.ProjectFormat;
using Xunit;

/// <summary>
/// P0 acceptance criteria: a saved project stays usable after its
/// containing directory is moved, and atomic replacement never destroys
/// the previous good file (CLAUDE.md §4.6, §21).
/// </summary>
public class PortabilityAndAtomicTests
{
    [Fact]
    public async Task Package_openable_and_identical_after_directory_move()
    {
        using var temp = new TempRoot();
        string dirA = temp.NewDir("A");
        string dirB = temp.NewDir("B");

        byte[] packageBytes = await PackageTestFactory.SaveValidPackageBytesAsync();

        string pathA = Path.Combine(dirA, "study.fluoaxisproj");
        await File.WriteAllBytesAsync(pathA, packageBytes, TestContext.Current.CancellationToken);

        // Byte-level move of the whole containing directory.
        Directory.Move(dirA, Path.Combine(dirB, "moved"));

        string pathMoved = Path.Combine(dirB, "moved", "study.fluoaxisproj");
        await using FileStream movedStream = File.OpenRead(pathMoved);
        Result<ProjectPackage> open = await FluoAxisProjectArchive.OpenAsync(
            movedStream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(open.IsSuccess, string.Join("; ", open.Errors.Select(e => e.UserMessage)));
        Assert.Equal(PackageTestFactory.ProjectId, open.Value!.Manifest.ProjectId);
        Assert.Equal(
            PackageTestFactory.FakeSqlite,
            open.Value.ReadEntry(RelativePath.Parse("project.sqlite")));
        Assert.Equal(
            PackageTestFactory.FakeXlsx,
            open.Value.ReadEntry(RelativePath.Parse("data/raw/source-001.xlsx")));
    }

    [Fact]
    public async Task Saved_package_bytes_contain_no_absolute_path_strings()
    {
        byte[] packageBytes = await PackageTestFactory.SaveValidPackageBytesAsync();
        string asText = Encoding.UTF8.GetString(packageBytes);

        // The bytes are UTF-8 inspectable for JSON names; entry names are
        // stored raw. A machine marker must not appear anywhere.
        string driveRootMarker = string.Concat("C", ":", "\\");
        Assert.DoesNotContain(driveRootMarker, asText, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts/test-tmp", asText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artifacts\\test-tmp", asText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Atomic_replace_overwrites_existing_target_and_cleans_temp()
    {
        using var temp = new TempRoot();
        string target = temp.NewFile("project.fluoaxisproj");
        await File.WriteAllTextAsync(target, "old package", TestContext.Current.CancellationToken);

        byte[] newBytes = await PackageTestFactory.SaveValidPackageBytesAsync();
        Result<string> writeTemp = await AtomicFile.WriteTempAsync(
            target, newBytes, TestContext.Current.CancellationToken);
        Assert.True(writeTemp.IsSuccess);

        Result<FileInfo> replace = AtomicFile.Replace(writeTemp.Value!, target);
        Assert.True(replace.IsSuccess, string.Join("; ", replace.Errors.Select(e => e.UserMessage)));

        Assert.Equal(newBytes, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(writeTemp.Value));
    }

    [Fact]
    public async Task Atomic_replace_moves_when_target_absent()
    {
        using var temp = new TempRoot();
        string target = temp.NewFile("project.fluoaxisproj");

        byte[] newBytes = await PackageTestFactory.SaveValidPackageBytesAsync();
        Result<string> writeTemp = await AtomicFile.WriteTempAsync(
            target, newBytes, TestContext.Current.CancellationToken);
        Result<FileInfo> replace = AtomicFile.Replace(writeTemp.Value!, target);

        Assert.True(replace.IsSuccess);
        Assert.True(File.Exists(target));
        Assert.Equal(newBytes, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Atomic_replace_failure_keeps_original_and_cleans_temp()
    {
        using var temp = new TempRoot();
        string original = "precious original content";
        string target = temp.NewFile("locked.fluoaxisproj");
        await File.WriteAllTextAsync(target, original, TestContext.Current.CancellationToken);

        string goodTemp = temp.NewFile("prepared.tmp");
        await File.WriteAllTextAsync(goodTemp, "prepared new package", TestContext.Current.CancellationToken);

        // Make the target locked: open it with FileShare.None so Replace
        // throws IOException.
        using (File.Open(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Result<FileInfo> replace = AtomicFile.Replace(goodTemp, target);
            Assert.True(!replace.IsSuccess);
            Assert.Contains(replace.Errors, e => e.Code == ProjectErrorCodes.ReplaceFailed);
        }

        Assert.Equal(original, await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(goodTemp));
    }

    [Fact]
    public void Atomic_replace_reports_missing_temp_file()
    {
        using var temp = new TempRoot();
        string absent = temp.NewFile("never-written.tmp");

        Result<FileInfo> replace = AtomicFile.Replace(absent, temp.NewFile("target.fluoaxisproj"));

        Assert.True(!replace.IsSuccess);
        Assert.Contains(replace.Errors, e => e.Code == ProjectErrorCodes.ReplaceFailed);
    }
}
