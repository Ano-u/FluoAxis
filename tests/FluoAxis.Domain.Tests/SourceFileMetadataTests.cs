namespace FluoAxis.Domain.Tests;

using Xunit;

public class SourceFileMetadataTests
{
    private static readonly HexHash Hash = new("0123456789abcdef".PadRight(64, '0'));

    [Fact]
    public void Constructor_accepts_a_complete_metadata_record()
    {
        var importedAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(8));

        var metadata = new SourceFileMetadata(
            "run-001.xlsx",
            RelativePath.Parse("data/raw/run-001.xlsx"),
            Hash,
            1024,
            importedAt,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        Assert.Equal("run-001.xlsx", metadata.OriginalFileName);
        Assert.Equal("data/raw/run-001.xlsx", metadata.RelativeResourcePath.Value);
        Assert.Equal(Hash, metadata.Sha256);
        Assert.Equal(1024, metadata.FileSizeBytes);
        Assert.Equal(importedAt.ToUniversalTime(), metadata.ImportedAtUtc);
    }

    [Theory]
    [InlineData("data/raw/run-001.xlsx")]
    [InlineData("raw\\run-001.xlsx")]
    public void Constructor_rejects_path_characters_in_original_file_name(string fileName)
    {
        Assert.Throws<ArgumentException>(() => new SourceFileMetadata(
            fileName, RelativePath.Parse("data/raw/a.bin"), Hash, 1, DateTimeOffset.UtcNow, "application/octet-stream"));
    }

    [Fact]
    public void Constructor_rejects_non_positive_file_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceFileMetadata(
            "a.bin", RelativePath.Parse("data/raw/a.bin"), Hash, 0, DateTimeOffset.UtcNow, "application/octet-stream"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceFileMetadata(
            "a.bin", RelativePath.Parse("data/raw/a.bin"), Hash, -5, DateTimeOffset.UtcNow, "application/octet-stream"));
    }

    [Fact]
    public void Constructor_rejects_blank_or_null_inputs()
    {
        Assert.Throws<ArgumentException>(() => new SourceFileMetadata(
            "", RelativePath.Parse("data/raw/a.bin"), Hash, 1, DateTimeOffset.UtcNow, "application/octet-stream"));
        Assert.Throws<ArgumentNullException>(() => new SourceFileMetadata(
            "a.bin", null!, Hash, 1, DateTimeOffset.UtcNow, "application/octet-stream"));
        Assert.Throws<ArgumentNullException>(() => new SourceFileMetadata(
            "a.bin", RelativePath.Parse("data/raw/a.bin"), Hash, 1, DateTimeOffset.UtcNow, null!));
    }

    [Fact]
    public void Metadata_carries_no_machine_location()
    {
        var metadata = new SourceFileMetadata(
            "a.xlsx", RelativePath.Parse("data/raw/a.xlsx"), Hash, 10, DateTimeOffset.UtcNow, "text/csv");

        Assert.True(metadata.RelativeResourcePath.Value.IndexOf(':') < 0);
        Assert.True(!Path.IsPathRooted(metadata.RelativeResourcePath.Value));
    }
}
